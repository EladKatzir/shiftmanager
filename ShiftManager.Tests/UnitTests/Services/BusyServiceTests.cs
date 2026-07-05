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

        var membershipService = new CompanyMembershipService(
            _db,
            NullLogger<CompanyMembershipService>.Instance);

        _service = new BusyService(
            _db,
            localizerMock.Object,
            NullLogger<BusyService>.Instance,
            hierarchyMock.Object,
            configCacheMock.Object,
            configMock.Object,
            membershipService,
            new EligibilityEvaluator());
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

    [Fact]
    public async Task ValidateChore_PrimaryCompanyInMolecule_NoMembershipRows_Passes()
    {
        // Backward-compatibility: a user whose PRIMARY company is in the target molecule,
        // with NO CompanyMembership rows at all, must NOT get USER_NOT_IN_MOLECULE.
        // This covers all pre-backfill users and most existing unit tests.
        _db.Companies.Add(new Company { Id = 200, MoleculeId = 10, Name = "CompanyPrimary", DisplayName = "Primary Co" });
        await _db.SaveChangesAsync();

        var user = await SeedUserAsync(id: 80, companyId: 200);
        var date = new DateOnly(2026, 6, 1);

        var validation = await _service.ValidateAsync(
            new BusyTarget.Chore(date, MoleculeId: 10, ChoreTypeId: null),
            userId: user.Id, actorUserId: 999);

        // No USER_NOT_IN_MOLECULE error — the primary company is in molecule 10
        validation.Errors.Should().NotContain(e => e.Key == "USER_NOT_IN_MOLECULE");
    }

    [Fact]
    public async Task ValidateChore_UserReachesTargetMoleculeViaMembership_NotRejected()
    {
        // Bug fix: a user whose PRIMARY company is in molecule M1 but has a CompanyMembership
        // row in a company belonging to molecule M2 should NOT be rejected with USER_NOT_IN_MOLECULE
        // when the target chore is in M2.
        // Molecule M1 (user's home molecule)
        _db.Companies.Add(new Company { Id = 300, MoleculeId = 20, Name = "CompanyHome", DisplayName = "Home Co" });
        // Molecule M2 (target chore's molecule) — user reaches it only via membership
        _db.Companies.Add(new Company { Id = 301, MoleculeId = 21, Name = "CompanyMember", DisplayName = "Member Co" });
        await _db.SaveChangesAsync();

        // User's PRIMARY company is in M1
        var user = await SeedUserAsync(id: 90, companyId: 300);

        // Add a CompanyMembership row: user also belongs to CompanyId=301 (M2)
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            UserId = user.Id,
            CompanyId = 301,
            IsPrimary = false,
            DoesShifts = true,
            GrantedBy = 0,
            JoinedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var date = new DateOnly(2026, 6, 1);

        var validation = await _service.ValidateAsync(
            new BusyTarget.Chore(date, MoleculeId: 21, ChoreTypeId: null),
            userId: user.Id, actorUserId: 999);

        // Should NOT produce USER_NOT_IN_MOLECULE — membership covers molecule 21
        validation.Errors.Should().NotContain(e => e.Key == "USER_NOT_IN_MOLECULE",
            "user reaches molecule 21 via a secondary CompanyMembership");
    }

    [Fact]
    public async Task ValidateShift_UserReachesTargetMoleculeViaMembership_NotRejected()
    {
        // Same cross-membership scenario for the SHIFT validation path.
        // Molecule M1 (user's home)
        _db.Companies.Add(new Company { Id = 400, MoleculeId = 30, Name = "CompanyHomeShift", DisplayName = "Home Shift Co" });
        // Molecule M2 (target shift's molecule) — user reaches it only via membership
        _db.Companies.Add(new Company { Id = 401, MoleculeId = 31, Name = "CompanyMemberShift", DisplayName = "Member Shift Co" });
        await _db.SaveChangesAsync();

        // User's PRIMARY company is in M1
        var user = await SeedUserAsync(id: 91, companyId: 400);

        // Add a CompanyMembership row: user also belongs to CompanyId=401 (M2)
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            UserId = user.Id,
            CompanyId = 401,
            IsPrimary = false,
            DoesShifts = true,
            GrantedBy = 0,
            JoinedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Seed a shift instance in CompanyId=401 (M2)
        var shiftType = new ShiftType
        {
            Id = 8001, Key = "MORNING_M2", Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0),
            MoleculeId = 31, CompanyId = 401
        };
        _db.ShiftTypes.Add(shiftType);
        var shiftInstance = new ShiftInstance
        {
            CompanyId = 401, ShiftTypeId = shiftType.Id,
            WorkDate = new DateOnly(2026, 6, 2), StaffingRequired = 1
        };
        _db.ShiftInstances.Add(shiftInstance);
        await _db.SaveChangesAsync();

        var validation = await _service.ValidateAsync(
            new BusyTarget.Shift(shiftInstance.Id),
            userId: user.Id, actorUserId: 999);

        // Should NOT produce USER_NOT_IN_MOLECULE
        validation.Errors.Should().NotContain(e => e.Key == "USER_NOT_IN_MOLECULE",
            "user reaches molecule 31 via a secondary CompanyMembership");
    }

    // ---------------------------------------------------------------------
    // Issue 1: per-shift-type "is blocking" + "counts toward hour limits".
    // Overlap/rest fire only when BOTH shifts block; a shift's hours count
    // toward the weekly cap only when it counts. Presence types stay exempt.
    // ---------------------------------------------------------------------

    /// <summary>Seeds a shift type + instance + an assignment for <paramref name="userId"/> (an EXISTING shift).</summary>
    private async Task SeedAssignedShiftAsync(int stId, int userId, int companyId, DateOnly date,
        TimeOnly start, TimeOnly end, string key, bool isBlocking = true, bool countsTowardHours = true)
    {
        var st = new ShiftType
        {
            Id = stId, Key = key, Start = start, End = end, MoleculeId = 1, CompanyId = companyId,
            IsBlocking = isBlocking, CountsTowardHourLimits = countsTowardHours
        };
        _db.ShiftTypes.Add(st);
        var inst = new ShiftInstance { CompanyId = companyId, ShiftTypeId = st.Id, WorkDate = date, StaffingRequired = 1 };
        _db.ShiftInstances.Add(inst);
        await _db.SaveChangesAsync();
        _db.ShiftAssignments.Add(new ShiftAssignment { CompanyId = companyId, UserId = userId, ShiftInstanceId = inst.Id, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    /// <summary>Seeds a shift type + an UNASSIGNED instance (the candidate the user is about to take).</summary>
    private async Task<int> SeedCandidateInstanceAsync(int stId, int companyId, DateOnly date,
        TimeOnly start, TimeOnly end, string key, bool isBlocking = true, bool countsTowardHours = true)
    {
        var st = new ShiftType
        {
            Id = stId, Key = key, Start = start, End = end, MoleculeId = 1, CompanyId = companyId,
            IsBlocking = isBlocking, CountsTowardHourLimits = countsTowardHours
        };
        _db.ShiftTypes.Add(st);
        var inst = new ShiftInstance { CompanyId = companyId, ShiftTypeId = st.Id, WorkDate = date, StaffingRequired = 1 };
        _db.ShiftInstances.Add(inst);
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    [Fact]
    public async Task NonBlockingCandidate_OverlappingBlockingNeighbor_NoOverlapError()
    {
        // A non-blocking status shift can coexist with a real (blocking) shift on the same window.
        await SeedMoleculeWithCompaniesAsync();
        var user = await SeedUserAsync(id: 500, companyId: 100);
        var date = new DateOnly(2026, 6, 10);
        await SeedAssignedShiftAsync(9100, user.Id, 100, date, new TimeOnly(8, 0), new TimeOnly(16, 0), "MORNING_A", isBlocking: true);
        var candidate = await SeedCandidateInstanceAsync(9101, 100, date, new TimeOnly(8, 0), new TimeOnly(16, 0), "BLUE", isBlocking: false);

        var validation = await _service.ValidateAsync(new BusyTarget.Shift(candidate), userId: user.Id, actorUserId: 100);

        validation.Errors.Should().NotContain(e => e.Key == "OVERLAP", "a non-blocking shift never conflicts on overlap");
    }

    [Fact]
    public async Task BlockingCandidate_OverlappingNonBlockingNeighbor_NoOverlapError()
    {
        // The reverse: a real blocking shift ignores a non-blocking neighbor.
        await SeedMoleculeWithCompaniesAsync();
        var user = await SeedUserAsync(id: 502, companyId: 100);
        var date = new DateOnly(2026, 6, 12);
        await SeedAssignedShiftAsync(9120, user.Id, 100, date, new TimeOnly(8, 0), new TimeOnly(16, 0), "BLUE_N", isBlocking: false);
        var candidate = await SeedCandidateInstanceAsync(9121, 100, date, new TimeOnly(8, 0), new TimeOnly(16, 0), "MORNING_C", isBlocking: true);

        var validation = await _service.ValidateAsync(new BusyTarget.Shift(candidate), userId: user.Id, actorUserId: 100);

        validation.Errors.Should().NotContain(e => e.Key == "OVERLAP", "a non-blocking neighbor is skipped in overlap detection");
    }

    [Fact]
    public async Task BlockingCandidate_OverlappingBlockingNeighbor_StillErrorsOverlap()
    {
        // Regression: two blocking shifts on the same window still conflict.
        await SeedMoleculeWithCompaniesAsync();
        var user = await SeedUserAsync(id: 501, companyId: 100);
        var date = new DateOnly(2026, 6, 11);
        await SeedAssignedShiftAsync(9110, user.Id, 100, date, new TimeOnly(8, 0), new TimeOnly(16, 0), "MORNING_B", isBlocking: true);
        var candidate = await SeedCandidateInstanceAsync(9111, 100, date, new TimeOnly(8, 0), new TimeOnly(16, 0), "AFT_B", isBlocking: true);

        var validation = await _service.ValidateAsync(new BusyTarget.Shift(candidate), userId: user.Id, actorUserId: 100);

        validation.Errors.Should().Contain(e => e.Key == "OVERLAP", "two blocking shifts still overlap");
    }

    [Fact]
    public async Task OfflineCandidate_StaysExemptFromOverlap_EvenWithDefaultBlockingColumn()
    {
        // Presence types are intrinsically non-blocking even when the column defaults to true
        // (tests build the schema from the model with no backfill). Guards the "unify" invariant.
        await SeedMoleculeWithCompaniesAsync();
        var user = await SeedUserAsync(id: 503, companyId: 100);
        var date = new DateOnly(2026, 6, 13);
        await SeedAssignedShiftAsync(9130, user.Id, 100, date, new TimeOnly(8, 0), new TimeOnly(16, 0), "MORNING_D", isBlocking: true);
        var candidate = await SeedCandidateInstanceAsync(9131, 100, date, new TimeOnly(8, 0), new TimeOnly(16, 0), ShiftType.KEY_OFFLINE, isBlocking: true);

        var validation = await _service.ValidateAsync(new BusyTarget.Shift(candidate), userId: user.Id, actorUserId: 100);

        validation.Errors.Should().NotContain(e => e.Key == "OVERLAP", "OFFLINE is non-blocking regardless of the column");
    }

    [Fact]
    public async Task NonCountingCandidate_DoesNotTriggerWeeklyCap()
    {
        // A non-counting shift never trips the weekly cap, even when the week is already full.
        await SeedMoleculeWithCompaniesAsync();
        var user = await SeedUserAsync(id: 600, companyId: 100);
        var sunday = new DateOnly(2026, 6, 14); // config week starts Sunday
        for (int i = 0; i < 4; i++)
            await SeedAssignedShiftAsync(9200 + i, user.Id, 100, sunday.AddDays(i), new TimeOnly(0, 0), new TimeOnly(15, 0), $"LONG_{i}", isBlocking: true, countsTowardHours: true); // 15h x4 = 60h > 56
        var candidate = await SeedCandidateInstanceAsync(9250, 100, sunday.AddDays(5), new TimeOnly(9, 0), new TimeOnly(17, 0), "STATUS", isBlocking: false, countsTowardHours: false);

        var validation = await _service.ValidateAsync(new BusyTarget.Shift(candidate), userId: user.Id, actorUserId: 100);

        validation.Warnings.Should().NotContain(w => w.Key == "EXCEEDS_WEEKLY_CAP", "a non-counting shift is exempt from the weekly cap");
    }

    [Fact]
    public async Task NonCountingExistingShifts_ExcludedFromWeeklyCapSum()
    {
        // The latent double-count: existing non-counting hours must not inflate the weekly total
        // against which a normal counting shift is checked.
        await SeedMoleculeWithCompaniesAsync();
        var user = await SeedUserAsync(id: 601, companyId: 100);
        var sunday = new DateOnly(2026, 6, 14);
        for (int i = 0; i < 4; i++)
            await SeedAssignedShiftAsync(9300 + i, user.Id, 100, sunday.AddDays(i), new TimeOnly(0, 0), new TimeOnly(23, 0), $"NC_{i}", isBlocking: false, countsTowardHours: false); // 23h x4, non-counting
        var candidate = await SeedCandidateInstanceAsync(9350, 100, sunday.AddDays(5), new TimeOnly(9, 0), new TimeOnly(17, 0), "NORMAL", isBlocking: true, countsTowardHours: true); // 8h

        var validation = await _service.ValidateAsync(new BusyTarget.Shift(candidate), userId: user.Id, actorUserId: 100);

        validation.Warnings.Should().NotContain(w => w.Key == "EXCEEDS_WEEKLY_CAP", "non-counting existing shifts are excluded from the weekly sum");
    }
}
