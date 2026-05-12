using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Models.Validation;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Regression tests for the unified busy/conflict detector.
/// Each test pins a behaviour change introduced by the busy-unification migration so
/// future refactors don't silently re-break the cross-tenant chore/on-duty bug.
/// </summary>
public class BusyServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly BusyService _service;

    public BusyServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));
        var hierarchyMock = new Mock<IHierarchySettingsService>();
        hierarchyMock.Setup(h => h.GetEffectiveSettingsAsync(It.IsAny<int>()))
            .ReturnsAsync(new EffectiveSettings(RestHours: 8, WeeklyCap: 56, "Test", "Test"));
        var configCacheMock = new Mock<IAppConfigCacheService>();
        configCacheMock.Setup(c => c.GetConfigAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((AppConfig?)null);

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ApiKeyHmacSecret"]).Returns("test-busyservice-hmac-secret");

        _service = new BusyService(
            _db,
            localizerMock.Object,
            NullLogger<BusyService>.Instance,
            hierarchyMock.Object,
            configCacheMock.Object,
            configMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private async Task SeedMoleculeWithCompaniesAsync()
    {
        // Two companies in the same molecule — the cross-tenant scenario.
        _db.Companies.AddRange(
            new Company { Id = 100, MoleculeId = 1, Name = "CompanyA", DisplayName = "Company A" },
            new Company { Id = 101, MoleculeId = 1, Name = "CompanyB", DisplayName = "Company B" });
        await _db.SaveChangesAsync();
    }

    private async Task<AppUser> SeedUserAsync(int id, int companyId)
    {
        var u = new AppUser
        {
            Id = id, Email = $"u{id}@test.com", DisplayName = $"User {id}",
            CompanyId = companyId, IsActive = true, Role = UserRole.Employee
        };
        _db.Users.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private async Task<int> SeedShiftAssignmentAsync(int userId, int companyId, DateOnly date, string key = ShiftType.KEY_MORNING)
    {
        var st = new ShiftType
        {
            Id = 7000 + (int)key.GetHashCode() % 100,
            Key = key,
            Start = key == ShiftType.KEY_MORNING ? new TimeOnly(8, 0) : new TimeOnly(0, 0),
            End = key == ShiftType.KEY_MORNING ? new TimeOnly(16, 0) : new TimeOnly(23, 59),
            MoleculeId = 1,
            CompanyId = companyId
        };
        _db.ShiftTypes.Add(st);

        var inst = new ShiftInstance
        {
            CompanyId = companyId,
            ShiftTypeId = st.Id,
            WorkDate = date,
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(inst);
        await _db.SaveChangesAsync();

        var sa = new ShiftAssignment
        {
            CompanyId = companyId,
            UserId = userId,
            ShiftInstanceId = inst.Id,
            CreatedAt = DateTime.UtcNow
        };
        _db.ShiftAssignments.Add(sa);
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    [Fact]
    public async Task ChoreOnShiftDay_CrossTenant_EmitsOverridableWarning()
    {
        // The bug we caught live on 2026-05-03: manager in CompanyA tries to create a
        // chore for an assignee in CompanyB (same molecule) on a date where the assignee
        // already has a shift in CompanyB. Pre-migration, this silently succeeded because
        // the chore service ran the shift query through a tenant filter scoped to CompanyA.
        // Post-migration, BusyService runs the predicate via IgnoreQueryFilters() and
        // emits a SHIFT_EXISTS_CONFLICT warning that the manager can override.
        await SeedMoleculeWithCompaniesAsync();
        var assignee = await SeedUserAsync(id: 27, companyId: 101);
        var date = new DateOnly(2026, 5, 20);
        await SeedShiftAssignmentAsync(assignee.Id, companyId: 101, date);

        var validation = await _service.ValidateAsync(
            new BusyTarget.Chore(date, MoleculeId: 1, ChoreTypeId: null),
            userId: assignee.Id,
            actorUserId: 100);

        validation.CanProceed.Should().BeTrue("hard errors are absent — only an overrideable warning fires");
        validation.Warnings.Should().ContainSingle(w => w.Key == "SHIFT_EXISTS_CONFLICT");
        var warn = validation.Warnings.Single(w => w.Key == "SHIFT_EXISTS_CONFLICT");
        warn.Severity.Should().Be(ValidationSeverity.Warning);
        warn.Detail.Should().NotBeNull();
        warn.Detail!.ResourceType.Should().Be("shift");
        warn.Detail.Date.Should().Be(date);
        warn.Detail.StartTime.Should().Be("08:00");
        warn.Detail.EndTime.Should().Be("16:00");
    }

    [Fact]
    public async Task DuplicateChore_NowReturnsWarning_NotHardError()
    {
        // Per 2026-05-03 policy: two chores same day = overrideable warning, not hard error.
        await SeedMoleculeWithCompaniesAsync();
        var assignee = await SeedUserAsync(id: 50, companyId: 100);
        var date = new DateOnly(2026, 5, 20);

        _db.Chores.Add(new Chore
        {
            CompanyId = 100, UserId = assignee.Id, Date = date, MoleculeId = 1,
            Title = "Existing", CreatedBy = 100, CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var validation = await _service.ValidateAsync(
            new BusyTarget.Chore(date, MoleculeId: 1, ChoreTypeId: null),
            userId: assignee.Id, actorUserId: 100);

        validation.CanProceed.Should().BeTrue();
        validation.Errors.Should().BeEmpty();
        validation.Warnings.Should().Contain(w => w.Key == "CHORE_CONFLICT");
    }

    [Fact]
    public async Task OnDutyOnShiftDay_NowEmitsWarning()
    {
        // Pre-migration, OnDutyService didn't check shifts at all. Post-migration, it does.
        await SeedMoleculeWithCompaniesAsync();
        var assignee = await SeedUserAsync(id: 60, companyId: 100);
        var date = new DateOnly(2026, 5, 20);
        await SeedShiftAssignmentAsync(assignee.Id, companyId: 100, date);

        var validation = await _service.ValidateAsync(
            new BusyTarget.OnDuty(date, OnDutyType.Hakam, MoleculeId: 1),
            userId: assignee.Id, actorUserId: 100);

        validation.CanProceed.Should().BeTrue();
        validation.Warnings.Should().Contain(w => w.Key == "SHIFT_EXISTS_CONFLICT");
    }

    [Fact]
    public async Task OverrideToken_RoundtripsAcrossTargetTypes()
    {
        var target = new BusyTarget.Chore(new DateOnly(2026, 5, 20), MoleculeId: 1, ChoreTypeId: null);
        var token = _service.GenerateOverrideToken(target, userId: 27, new[] { "CHORE_CONFLICT" });

        _service.ValidateOverrideToken(token, target, userId: 27).Should().BeTrue();

        // Different target → token rejected
        var otherTarget = new BusyTarget.Chore(new DateOnly(2026, 5, 21), MoleculeId: 1, ChoreTypeId: null);
        _service.ValidateOverrideToken(token, otherTarget, userId: 27).Should().BeFalse();

        // Different user → token rejected
        _service.ValidateOverrideToken(token, target, userId: 99).Should().BeFalse();

        // Empty/garbage tokens
        _service.ValidateOverrideToken(null, target, 27).Should().BeFalse();
        _service.ValidateOverrideToken("garbage", target, 27).Should().BeFalse();
    }

    [Fact]
    public async Task GetBusyStates_DecoratesUsersWithShiftHits()
    {
        await SeedMoleculeWithCompaniesAsync();
        var u1 = await SeedUserAsync(id: 70, companyId: 100);
        var u2 = await SeedUserAsync(id: 71, companyId: 101);
        var date = new DateOnly(2026, 5, 20);
        await SeedShiftAssignmentAsync(u1.Id, companyId: 100, date);

        var states = await _service.GetBusyStatesAsync(
            new[] { u1.Id, u2.Id }, date, moleculeId: 1, target: null);

        states.Should().ContainKey(u1.Id);
        states[u1.Id].Summary.HasShift.Should().BeTrue();
        states[u1.Id].Summary.Highest.Should().Be(BusyLevel.Soft);
        states[u2.Id].Summary.HasShift.Should().BeFalse();
        states[u2.Id].Summary.Highest.Should().Be(BusyLevel.None);
    }
}
