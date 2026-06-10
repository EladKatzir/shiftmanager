using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
/// Tests for VacationApprovalService.ApproveAsync — verifies overlap detection,
/// self-approval blocking, already-processed rejection, and status-based filtering.
///
/// VA-A01: Overlapping approved request blocks approval
/// VA-A02: Non-overlapping request allows approval
/// VA-A03: Canceled requests do not block approval
/// VA-A04: Self-approval is blocked
/// VA-A05: Already-processed request is rejected
/// VA-A06: Pending requests do not block each other
/// </summary>
public class VacationApprovalApproveTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly Mock<IHomeMaterialiserService> _materialiserMock;
    private readonly Mock<ICompanyMembershipService> _membershipServiceMock;
    private readonly VacationApprovalService _service;

    private const int TestCompanyId = 1;
    private const int EmployeeUserId = 10;
    private const int ApproverUserId = 20;

    public VacationApprovalApproveTests()
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
        var traineeServiceMock = new Mock<ITraineeService>();
        _materialiserMock = new Mock<IHomeMaterialiserService>();
        var loggerMock = new Mock<ILogger<VacationApprovalService>>();
        var featureFlagServiceMock = new Mock<IFeatureFlagService>();
        _membershipServiceMock = new Mock<ICompanyMembershipService>();

        // Default: approver has the required grant
        _grantServiceMock
            .Setup(g => g.HasGrantWithScopeAsync(
                ApproverUserId,
                It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(true);

        // Default: membership service returns empty list → single-company behavior preserved
        _membershipServiceMock
            .Setup(m => m.GetMembershipsAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<CompanyMembership>());

        // Default: notification service succeeds silently
        notificationServiceMock
            .Setup(n => n.CreateTimeOffNotificationAsync(
                It.IsAny<int>(), It.IsAny<RequestStatus>(),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        // Default: materialiser succeeds silently
        _materialiserMock
            .Setup(m => m.SyncMaterialisedHomeRowsAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _materialiserMock
            .Setup(m => m.RestoreRotationHomeAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .Returns(Task.CompletedTask);

        // Default: feature flag is enabled for tests
        featureFlagServiceMock
            .Setup(s => s.IsEnabledAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(true);

        var auditLogServiceMock = new Mock<IAuditLogService>();
        var localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        // Default behavior: return a LocalizedString whose Value equals the key (so tests
        // asserting Contain("KeyFragment") still pass against either raw keys or localizer results).
        localizerMock
            .Setup(l => l[It.IsAny<string>()])
            .Returns((string name) => new LocalizedString(name, name));

        _service = new VacationApprovalService(
            _db,
            _grantServiceMock.Object,
            loggerMock.Object,
            notificationServiceMock.Object,
            traineeServiceMock.Object,
            _materialiserMock.Object,
            auditLogServiceMock.Object,
            localizerMock.Object,
            featureFlagServiceMock.Object,
            _membershipServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    /// <summary>
    /// Creates the employee user and the approver user in the DB.
    /// </summary>
    private async Task SeedUsersAsync()
    {
        _db.Users.AddRange(
            new AppUser
            {
                Id = EmployeeUserId,
                Email = "employee@test.com",
                CompanyId = TestCompanyId,
                Role = UserRole.Employee,
                DisplayName = "Test Employee",
                IsActive = true
            },
            new AppUser
            {
                Id = ApproverUserId,
                Email = "approver@test.com",
                CompanyId = TestCompanyId,
                Role = UserRole.Manager,
                DisplayName = "Test Approver",
                IsActive = true
            });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a TimeOffRequest for the employee with the specified dates and status.
    /// </summary>
    private async Task<TimeOffRequest> CreateRequestAsync(
        DateOnly startDate, DateOnly endDate,
        RequestStatus status = RequestStatus.Pending)
    {
        var request = new TimeOffRequest
        {
            CompanyId = TestCompanyId,
            UserId = EmployeeUserId,
            StartDate = startDate,
            EndDate = endDate,
            Type = TimeOffType.Vacation,
            Status = status
        };
        _db.TimeOffRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    /// <summary>
    /// VA-A01: An already-approved request with overlapping dates blocks a new approval.
    /// Approved March 1-5 + Pending March 3-7 → approve pending → Success=false, overlap message.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_RejectsOverlappingApprovedRequest()
    {
        // Arrange
        await SeedUsersAsync();
        await CreateRequestAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5),
            RequestStatus.Approved);
        var pending = await CreateRequestAsync(
            new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 7));

        // Act
        var (success, message) = await _service.ApproveAsync(pending.Id, ApproverUserId);

        // Assert
        success.Should().BeFalse();
        message.Should().Contain("OverlappingApproved");
    }

    /// <summary>
    /// VA-A02: Non-overlapping approved request does not block approval.
    /// Approved March 1-5 + Pending March 10-15 → approve → Success=true.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_AllowsNonOverlappingRequest()
    {
        // Arrange
        await SeedUsersAsync();
        await CreateRequestAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5),
            RequestStatus.Approved);
        var pending = await CreateRequestAsync(
            new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 15));

        // Act
        var (success, message) = await _service.ApproveAsync(pending.Id, ApproverUserId);

        // Assert
        success.Should().BeTrue();
        message.Should().Contain("Approved");
    }

    /// <summary>
    /// VA-A03: Canceled requests with overlapping dates do not block approval.
    /// Canceled March 1-5 + Pending March 3-7 → approve → Success=true.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_CanceledRequestsDoNotBlock()
    {
        // Arrange
        await SeedUsersAsync();
        await CreateRequestAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5),
            RequestStatus.Canceled);
        var pending = await CreateRequestAsync(
            new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 7));

        // Act
        var (success, message) = await _service.ApproveAsync(pending.Id, ApproverUserId);

        // Assert
        success.Should().BeTrue();
        message.Should().Contain("Approved");
    }

    /// <summary>
    /// VA-A04: A user cannot approve their own vacation request.
    /// Employee creates request, employee tries to approve → Success=false.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_BlocksSelfApproval()
    {
        // Arrange
        await SeedUsersAsync();
        var pending = await CreateRequestAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5));

        // The employee tries to approve their own request — grant mock not needed,
        // self-approval check fires before grant check.

        // Act
        var (success, message) = await _service.ApproveAsync(pending.Id, EmployeeUserId);

        // Assert
        success.Should().BeFalse();
        message.Should().Contain("CannotApproveSelf");
    }

    /// <summary>
    /// VA-A05: An already-approved request cannot be approved again.
    /// Request with Status=Approved → try to approve → Success=false, already processed.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_RejectsAlreadyProcessedRequest()
    {
        // Arrange
        await SeedUsersAsync();
        var approved = await CreateRequestAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5),
            RequestStatus.Approved);

        // Act
        var (success, message) = await _service.ApproveAsync(approved.Id, ApproverUserId);

        // Assert
        success.Should().BeFalse();
        message.Should().Contain("AlreadyProcessed");
    }

    /// <summary>
    /// VA-A06: Pending requests with overlapping dates do not block each other.
    /// Two pending requests March 1-5 and March 3-7 → approve second → Success=true.
    /// Only Approved status triggers the overlap check.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_PendingRequestsDoNotBlockEachOther()
    {
        // Arrange
        await SeedUsersAsync();
        await CreateRequestAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5),
            RequestStatus.Pending);
        var secondPending = await CreateRequestAsync(
            new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 7));

        // Act
        var (success, message) = await _service.ApproveAsync(secondPending.Id, ApproverUserId);

        // Assert
        success.Should().BeTrue();
        message.Should().Contain("Approved");
    }

    /// <summary>
    /// VA-A07: Union approver authorization — approver holds grant in company 2 only;
    /// requester is a member of companies 1 AND 2 → CanUserApproveAsync returns TRUE.
    /// This is the core cross-company union authorization test: the request is in company 1
    /// but the approver is authorized via their grant in company 2 (another shift company).
    /// </summary>
    [Fact]
    public async Task CanUserApproveAsync_UnionAuth_ApproverHasGrantInSecondCompanyOnly_ReturnsTrue()
    {
        // Arrange
        const int secondCompanyId = 2;
        await SeedUsersAsync();

        // Requester's request is in company 1 (primary company)
        var request = await CreateRequestAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 5));

        // Requester has active memberships in BOTH company 1 (primary) and company 2
        _membershipServiceMock
            .Setup(m => m.GetMembershipsAsync(EmployeeUserId))
            .ReturnsAsync(new List<CompanyMembership>
            {
                new() { UserId = EmployeeUserId, CompanyId = TestCompanyId,  IsPrimary = true,  DoesShifts = true },
                new() { UserId = EmployeeUserId, CompanyId = secondCompanyId, IsPrimary = false, DoesShifts = true }
            });

        // Approver (user 20) holds the grant ONLY in company 2; company 1 returns false
        _grantServiceMock
            .Setup(g => g.HasGrantWithScopeAsync(
                ApproverUserId,
                It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(false); // default: false for all

        _grantServiceMock
            .Setup(g => g.HasGrantWithScopeAsync(
                ApproverUserId,
                It.IsAny<string>(),
                null, null, null, null,
                secondCompanyId,          // companyId = 2
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(true); // only true for company 2

        // Act
        var canApprove = await _service.CanUserApproveAsync(ApproverUserId, request.Id);

        // Assert
        canApprove.Should().BeTrue("approver holds the grant in company 2, one of the requester's shift companies");
    }

    /// <summary>
    /// VA-A08: Union approver authorization — approver holds the grant in NEITHER of the
    /// requester's membership companies → CanUserApproveAsync returns FALSE.
    /// </summary>
    [Fact]
    public async Task CanUserApproveAsync_UnionAuth_ApproverHasGrantInNeitherCompany_ReturnsFalse()
    {
        // Arrange
        const int secondCompanyId = 2;
        await SeedUsersAsync();

        var request = await CreateRequestAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 5));

        // Requester has memberships in companies 1 and 2
        _membershipServiceMock
            .Setup(m => m.GetMembershipsAsync(EmployeeUserId))
            .ReturnsAsync(new List<CompanyMembership>
            {
                new() { UserId = EmployeeUserId, CompanyId = TestCompanyId,  IsPrimary = true,  DoesShifts = true },
                new() { UserId = EmployeeUserId, CompanyId = secondCompanyId, IsPrimary = false, DoesShifts = true }
            });

        // Approver holds the grant in NEITHER company
        _grantServiceMock
            .Setup(g => g.HasGrantWithScopeAsync(
                ApproverUserId,
                It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(false);

        // Act
        var canApprove = await _service.CanUserApproveAsync(ApproverUserId, request.Id);

        // Assert
        canApprove.Should().BeFalse("approver holds the grant in neither of the requester's companies");
    }
}
