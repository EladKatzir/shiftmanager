using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for VacationApprovalService.CreateApprovedManualTimeOffAsync — the manual
/// "enter time-off on the calendar" path. Verifies it creates an Approved record,
/// runs the approval side-effects (conflicting-shift removal), calls the HOME
/// materialiser (flag-gated), normalizes dates per type, and enforces authorization,
/// company membership, label rules, and overlap guarding.
/// </summary>
public class ManualTimeOffEntryTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly Mock<IHomeMaterialiserService> _materialiserMock;
    private readonly Mock<ICompanyMembershipService> _membershipServiceMock;
    private readonly Mock<IFeatureFlagService> _featureFlagServiceMock;
    private readonly Mock<ITraineeService> _traineeServiceMock;
    private readonly VacationApprovalService _service;

    private const int TestCompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int TargetUserId = 10;
    private const int ActorUserId = 20;

    public ManualTimeOffEntryTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _grantServiceMock = new Mock<IGrantService>();
        var notificationServiceMock = new Mock<INotificationService>();
        _traineeServiceMock = new Mock<ITraineeService>();
        _materialiserMock = new Mock<IHomeMaterialiserService>();
        var loggerMock = new Mock<ILogger<VacationApprovalService>>();
        _featureFlagServiceMock = new Mock<IFeatureFlagService>();
        _membershipServiceMock = new Mock<ICompanyMembershipService>();
        var auditLogServiceMock = new Mock<IAuditLogService>();
        var localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns((string name) => new LocalizedString(name, name));

        // Default happy-path authorization: actor has note permission and can reach any target.
        _grantServiceMock.Setup(g => g.HasCalendarNotePermissionAsync(ActorUserId)).ReturnsAsync(true);
        _grantServiceMock.Setup(g => g.CanReachUserForNoteAsync(ActorUserId, It.IsAny<int>())).ReturnsAsync(true);
        // Default: target is a member of any company asked about.
        _membershipServiceMock.Setup(m => m.IsMemberAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
        // Default: HOME unification enabled.
        _featureFlagServiceMock.Setup(s => s.IsEnabledAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(true);
        notificationServiceMock.Setup(n => n.CreateTimeOffNotificationAsync(
                It.IsAny<int>(), It.IsAny<RequestStatus>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _materialiserMock.Setup(m => m.SyncMaterialisedHomeRowsAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

        _service = new VacationApprovalService(
            _db,
            _grantServiceMock.Object,
            loggerMock.Object,
            notificationServiceMock.Object,
            _traineeServiceMock.Object,
            _materialiserMock.Object,
            auditLogServiceMock.Object,
            localizerMock.Object,
            _featureFlagServiceMock.Object,
            _membershipServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private async Task SeedTargetAsync(int companyId = TestCompanyId, UserRole role = UserRole.Employee)
    {
        _db.Users.Add(new AppUser
        {
            Id = TargetUserId,
            Email = "target@test.com",
            CompanyId = companyId,
            Role = role,
            DisplayName = "Target User",
            IsActive = true
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedShiftOnAsync(DateOnly day)
    {
        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1001, CompanyId = TestCompanyId, ShiftTypeId = 1,
            WorkDate = day, Name = "Morning", StaffingRequired = 1
        });
        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 2001, CompanyId = TestCompanyId, UserId = TargetUserId, ShiftInstanceId = 1001
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Vacation_CreatesApproved_RemovesConflictingShift_CallsMaterialiser()
    {
        var day = new DateOnly(2026, 8, 3);
        await SeedTargetAsync();
        await SeedShiftOnAsync(day);

        var (success, requestId, errorKey) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.Vacation, day, day, null, ActorUserId);

        success.Should().BeTrue(errorKey);
        requestId.Should().NotBeNull();

        var req = await _db.TimeOffRequests.FindAsync(requestId!.Value);
        req.Should().NotBeNull();
        req!.Status.Should().Be(RequestStatus.Approved);
        req.ApproverId.Should().Be(ActorUserId);
        req.FirstApprovalActorId.Should().Be(ActorUserId);
        req.Type.Should().Be(TimeOffType.Vacation);
        req.CompanyId.Should().Be(TestCompanyId);

        // Conflicting shift assignment on that day removed by the side-effects pipeline.
        (await _db.ShiftAssignments.AnyAsync(a => a.Id == 2001)).Should().BeFalse();
        // HOME materialiser invoked (flag on).
        _materialiserMock.Verify(m => m.SyncMaterialisedHomeRowsAsync(requestId.Value), Times.Once);
    }

    [Fact]
    public async Task After_ForcesEndDateEqualsStart()
    {
        var start = new DateOnly(2026, 8, 3);
        var laterEnd = new DateOnly(2026, 8, 9);
        await SeedTargetAsync();

        var (success, requestId, _) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.After, start, laterEnd, null, ActorUserId);

        success.Should().BeTrue();
        var req = await _db.TimeOffRequests.FindAsync(requestId!.Value);
        req!.Type.Should().Be(TimeOffType.After);
        req.EndDate.Should().Be(start, "After is a single-day partial leave");
    }

    [Fact]
    public async Task DayAt_PersistsLabel_AndForcesSingleDay()
    {
        var start = new DateOnly(2026, 8, 3);
        await SeedTargetAsync();

        var (success, requestId, _) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.DayAt, start, start.AddDays(4), "clinic", ActorUserId);

        success.Should().BeTrue();
        var req = await _db.TimeOffRequests.FindAsync(requestId!.Value);
        req!.Type.Should().Be(TimeOffType.DayAt);
        req.Label.Should().Be("clinic");
        req.EndDate.Should().Be(start);
    }

    [Fact]
    public async Task DayAt_BlankLabel_ReturnsError()
    {
        var start = new DateOnly(2026, 8, 3);
        await SeedTargetAsync();

        var (success, requestId, errorKey) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.DayAt, start, start, "   ", ActorUserId);

        success.Should().BeFalse();
        requestId.Should().BeNull();
        errorKey.Should().Be("Error_TimeOff_DayAtLabelRequired");
        (await _db.TimeOffRequests.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task NoNotePermission_ReturnsNoPermission()
    {
        var day = new DateOnly(2026, 8, 3);
        await SeedTargetAsync();
        _grantServiceMock.Setup(g => g.HasCalendarNotePermissionAsync(ActorUserId)).ReturnsAsync(false);

        var (success, _, errorKey) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.Vacation, day, day, null, ActorUserId);

        success.Should().BeFalse();
        errorKey.Should().Be("Error_TimeOff_NoPermission");
        (await _db.TimeOffRequests.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CannotReachTargetUser_ReturnsNoPermission()
    {
        var day = new DateOnly(2026, 8, 3);
        await SeedTargetAsync();
        _grantServiceMock.Setup(g => g.CanReachUserForNoteAsync(ActorUserId, TargetUserId)).ReturnsAsync(false);

        var (success, _, errorKey) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.Vacation, day, day, null, ActorUserId);

        success.Should().BeFalse();
        errorKey.Should().Be("Error_TimeOff_NoPermission");
    }

    [Fact]
    public async Task NonexistentTarget_ReturnsUserNotInCompany()
    {
        var day = new DateOnly(2026, 8, 3);
        // Do NOT seed the target user. Reach is mocked true (default), so the guard that trips is
        // the company resolution: no active user row -> no company to attach the leave to.
        var (success, _, errorKey) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.Vacation, day, day, null, ActorUserId);

        success.Should().BeFalse();
        errorKey.Should().Be("Error_TimeOff_UserNotInCompany");
    }

    [Fact]
    public async Task LeaveIsCreatedInTargetsOwnCompany_NotActorTenant()
    {
        var day = new DateOnly(2026, 8, 3);
        // Target lives in a DIFFERENT company than the acting tenant would imply (molecule board case).
        await SeedTargetAsync(companyId: OtherCompanyId);

        var (success, requestId, _) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.Vacation, day, day, null, ActorUserId);

        success.Should().BeTrue();
        var req = await _db.TimeOffRequests.FindAsync(requestId!.Value);
        req!.CompanyId.Should().Be(OtherCompanyId, "the leave belongs to the target user's own company");
    }

    [Fact]
    public async Task OverlappingApprovedLeave_ReturnsOverlap()
    {
        var day = new DateOnly(2026, 8, 3);
        await SeedTargetAsync();
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            CompanyId = TestCompanyId, UserId = TargetUserId,
            StartDate = day, EndDate = day, Type = TimeOffType.Vacation,
            Status = RequestStatus.Approved
        });
        await _db.SaveChangesAsync();

        var (success, _, errorKey) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.Vacation, day, day, null, ActorUserId);

        success.Should().BeFalse();
        errorKey.Should().Be("Error_TimeOff_OverlapExists");
    }

    [Fact]
    public async Task FlagOff_DoesNotCallMaterialiser_ButStillApproves()
    {
        var day = new DateOnly(2026, 8, 3);
        await SeedTargetAsync();
        _featureFlagServiceMock.Setup(s => s.IsEnabledAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(false);

        var (success, requestId, _) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.Vacation, day, day, null, ActorUserId);

        success.Should().BeTrue();
        var req = await _db.TimeOffRequests.FindAsync(requestId!.Value);
        req!.Status.Should().Be(RequestStatus.Approved);
        _materialiserMock.Verify(m => m.SyncMaterialisedHomeRowsAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task TraineeTarget_CancelsShadowing()
    {
        var day = new DateOnly(2026, 8, 3);
        await SeedTargetAsync(role: UserRole.Trainee);

        var (success, _, _) = await _service.CreateApprovedManualTimeOffAsync(
            TargetUserId, TimeOffType.Vacation, day, day, null, ActorUserId);

        success.Should().BeTrue();
        _traineeServiceMock.Verify(
            t => t.CancelShadowingForTimeOffAsync(TargetUserId, It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.Once);
    }
}
