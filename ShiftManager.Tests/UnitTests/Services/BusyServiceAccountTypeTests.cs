using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
/// Hard-block tests for AccountType enforcement inside BusyService.
///
/// BAT-01: GroupUser is blocked from shift assignment (GROUPUSER_CANNOT_BE_ASSIGNED).
/// BAT-02: Mil user is blocked from chore assignment (ACCOUNT_CANNOT_DO_CHORES).
/// BAT-03: GroupUser is blocked from chore assignment (ACCOUNT_CANNOT_DO_CHORES).
/// BAT-04: Standard user passes both shift and chore validation (no account-type errors).
/// BAT-05: The shift block is a hard Error (not a warning), CanProceed = false.
/// BAT-06: The chore block is a hard Error (not a warning), CanProceed = false.
/// </summary>
public class BusyServiceAccountTypeTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly BusyService _service;

    // Reusable constants for seeded entities.
    private const int MoleculeId = 50;
    private const int CompanyId = 500;

    public BusyServiceAccountTypeTests()
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
        configMock.Setup(c => c["ApiKeyHmacSecret"]).Returns("test-busy-account-type-secret");

        var membershipService = new CompanyMembershipService(
            _db, NullLogger<CompanyMembershipService>.Instance);

        _service = new BusyService(
            _db,
            localizerMock.Object,
            NullLogger<BusyService>.Instance,
            hierarchyMock.Object,
            configCacheMock.Object,
            configMock.Object,
            membershipService,
            new EligibilityEvaluator());

        // Seed a company in the shared molecule once for all tests.
        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = MoleculeId, Name = "TestCo", DisplayName = "Test Co" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<AppUser> SeedUserAsync(int id, AccountType accountType)
    {
        var u = new AppUser
        {
            Id = id,
            Email = $"u{id}@test.com",
            DisplayName = $"User {id}",
            CompanyId = CompanyId,
            IsActive = true,
            Role = UserRole.Employee,
            AccountType = accountType
        };
        _db.Users.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    /// <summary>
    /// Seeds a ShiftType + ShiftInstance in the shared molecule/company and returns the instance id.
    /// </summary>
    private async Task<int> SeedShiftInstanceAsync(int shiftTypeId, DateOnly date)
    {
        var st = new ShiftType
        {
            Id = shiftTypeId,
            Key = $"SHIFT_{shiftTypeId}",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0),
            MoleculeId = MoleculeId,
            CompanyId = CompanyId
        };
        _db.ShiftTypes.Add(st);

        var inst = new ShiftInstance
        {
            CompanyId = CompanyId,
            ShiftTypeId = st.Id,
            WorkDate = date,
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(inst);
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    // ── Tests ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateShift_GroupUser_EmitsHardError_GroupUserCannotBeAssigned()
    {
        // BAT-01 + BAT-05: GroupUser attempting shift assignment must be blocked.
        var user = await SeedUserAsync(id: 1001, AccountType.GroupUser);
        var shiftInstanceId = await SeedShiftInstanceAsync(shiftTypeId: 9001, new DateOnly(2026, 7, 1));

        var result = await _service.ValidateAsync(
            new BusyTarget.Shift(shiftInstanceId),
            userId: user.Id,
            actorUserId: 999);

        result.CanProceed.Should().BeFalse("GroupUser must be hard-blocked from shift assignment");
        result.Errors.Should().Contain(e => e.Key == "GROUPUSER_CANNOT_BE_ASSIGNED",
            "the specific error code must be GROUPUSER_CANNOT_BE_ASSIGNED");
        var error = result.Errors.First(e => e.Key == "GROUPUSER_CANNOT_BE_ASSIGNED");
        error.Severity.Should().Be(ValidationSeverity.Error,
            "this must be a hard Error, not a warning");
    }

    [Fact]
    public async Task ValidateChore_MilUser_EmitsHardError_AccountCannotDoChores()
    {
        // BAT-02: Mil user attempting chore assignment must be blocked.
        var user = await SeedUserAsync(id: 1002, AccountType.Mil);
        var date = new DateOnly(2026, 7, 1);

        var result = await _service.ValidateAsync(
            new BusyTarget.Chore(date, MoleculeId, ChoreTypeId: null),
            userId: user.Id,
            actorUserId: 999);

        result.CanProceed.Should().BeFalse("Mil user must be hard-blocked from chore assignment");
        result.Errors.Should().Contain(e => e.Key == "ACCOUNT_CANNOT_DO_CHORES",
            "the specific error code must be ACCOUNT_CANNOT_DO_CHORES");
        var error = result.Errors.First(e => e.Key == "ACCOUNT_CANNOT_DO_CHORES");
        error.Severity.Should().Be(ValidationSeverity.Error,
            "this must be a hard Error, not a warning");
    }

    [Fact]
    public async Task ValidateChore_GroupUser_EmitsHardError_AccountCannotDoChores()
    {
        // BAT-03 + BAT-06: GroupUser attempting chore assignment must be blocked.
        var user = await SeedUserAsync(id: 1003, AccountType.GroupUser);
        var date = new DateOnly(2026, 7, 1);

        var result = await _service.ValidateAsync(
            new BusyTarget.Chore(date, MoleculeId, ChoreTypeId: null),
            userId: user.Id,
            actorUserId: 999);

        result.CanProceed.Should().BeFalse("GroupUser must be hard-blocked from chore assignment");
        result.Errors.Should().Contain(e => e.Key == "ACCOUNT_CANNOT_DO_CHORES",
            "the specific error code must be ACCOUNT_CANNOT_DO_CHORES");
        var error = result.Errors.First(e => e.Key == "ACCOUNT_CANNOT_DO_CHORES");
        error.Severity.Should().Be(ValidationSeverity.Error,
            "this must be a hard Error, not a warning");
    }

    [Fact]
    public async Task ValidateShift_StandardUser_NoAccountTypeError()
    {
        // BAT-04 (shift path): Standard user must not produce any account-type error.
        var user = await SeedUserAsync(id: 1004, AccountType.Standard);
        var shiftInstanceId = await SeedShiftInstanceAsync(shiftTypeId: 9004, new DateOnly(2026, 7, 2));

        var result = await _service.ValidateAsync(
            new BusyTarget.Shift(shiftInstanceId),
            userId: user.Id,
            actorUserId: 999);

        result.Errors.Should().NotContain(e =>
            e.Key == "GROUPUSER_CANNOT_BE_ASSIGNED" || e.Key == "ACCOUNT_CANNOT_DO_CHORES",
            "Standard users must pass the AccountType gate with no account-type error");
    }

    [Fact]
    public async Task ValidateChore_StandardUser_NoAccountTypeError()
    {
        // BAT-04 (chore path): Standard user must not produce any account-type error.
        var user = await SeedUserAsync(id: 1005, AccountType.Standard);
        var date = new DateOnly(2026, 7, 2);

        var result = await _service.ValidateAsync(
            new BusyTarget.Chore(date, MoleculeId, ChoreTypeId: null),
            userId: user.Id,
            actorUserId: 999);

        result.Errors.Should().NotContain(e =>
            e.Key == "GROUPUSER_CANNOT_BE_ASSIGNED" || e.Key == "ACCOUNT_CANNOT_DO_CHORES",
            "Standard users must pass the AccountType gate with no account-type error");
    }

    [Fact]
    public async Task ValidateShift_MilUser_IsAllowed_NoGroupUserError()
    {
        // Mil users CAN be assigned shifts (CanBeAssignedShift returns true for Mil).
        // This test ensures we did not accidentally block Mil from shifts.
        var user = await SeedUserAsync(id: 1006, AccountType.Mil);
        var shiftInstanceId = await SeedShiftInstanceAsync(shiftTypeId: 9006, new DateOnly(2026, 7, 3));

        var result = await _service.ValidateAsync(
            new BusyTarget.Shift(shiftInstanceId),
            userId: user.Id,
            actorUserId: 999);

        result.Errors.Should().NotContain(e => e.Key == "GROUPUSER_CANNOT_BE_ASSIGNED",
            "Mil users ARE allowed to do shifts — only GroupUser is blocked");
    }
}
