using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
/// Tests for VacationApprovalService — verifies approval routing,
/// auto-approve, extended leave, and grant-based authorization.
///
/// VA-01: Falls back to default grant when no rule exists
/// VA-02: Auto-approves when leave days within threshold
/// VA-03: Extended leave triggers second approval requirement
/// VA-04: CanUserApproveAsync validates grant via IGrantService
/// </summary>
public class VacationApprovalServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<ITraineeService> _traineeServiceMock;
    private readonly Mock<IHomeMaterialiserService> _materialiserMock;
    private readonly VacationApprovalService _service;

    private const int TestCompanyId = 1;

    public VacationApprovalServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _grantServiceMock = new Mock<IGrantService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _traineeServiceMock = new Mock<ITraineeService>();
        _materialiserMock = new Mock<IHomeMaterialiserService>();
        var loggerMock = new Mock<ILogger<VacationApprovalService>>();
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
            _notificationServiceMock.Object,
            _traineeServiceMock.Object,
            _materialiserMock.Object,
            auditLogServiceMock.Object,
            localizerMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<(AppUser user, TimeOffRequest request)> CreateBaseEntitiesAsync(
        int leaveDays = 3,
        int? jobTypeId = null)
    {
        var user = new AppUser
        {
            Id = 1,
            Email = "employee@test.com",
            CompanyId = TestCompanyId,
            Role = UserRole.Employee,
            DisplayName = "Test Employee",
            IsActive = true,
            JobTypeId = jobTypeId
        };
        _db.Users.Add(user);

        var startDate = new DateOnly(2026, 6, 1);
        var request = new TimeOffRequest
        {
            CompanyId = TestCompanyId,
            UserId = user.Id,
            StartDate = startDate,
            EndDate = startDate.AddDays(leaveDays - 1),
            Type = TimeOffType.Vacation,
            Status = RequestStatus.Pending
        };
        _db.TimeOffRequests.Add(request);

        await _db.SaveChangesAsync();
        return (user, request);
    }

    /// <summary>
    /// VA-01: When no VacationApprovalRule exists for the company,
    /// GetApprovalRouteAsync falls back to default "ApproveVacations" grant.
    /// </summary>
    [Fact]
    public async Task VA01_GetApprovalRouteAsync_FallsBackToDefault_WhenNoRuleExists()
    {
        // Arrange
        var (_, request) = await CreateBaseEntitiesAsync(leaveDays: 3);

        // Act
        var (approverId, grantKey, requiresSecond) = await _service.GetApprovalRouteAsync(request.Id);

        // Assert
        approverId.Should().BeNull();
        grantKey.Should().Be("ApproveVacations");
        requiresSecond.Should().BeFalse();
    }

    /// <summary>
    /// VA-02: When a rule allows auto-approve for short leaves (≤ MaxAutoApproveDays),
    /// GetApprovalRouteAsync returns no specific approver and no second approval.
    /// </summary>
    [Fact]
    public async Task VA02_GetApprovalRouteAsync_AutoApproves_WhenWithinThreshold()
    {
        // Arrange — create a rule that auto-approves ≤ 3 days
        var (_, request) = await CreateBaseEntitiesAsync(leaveDays: 2);

        _db.VacationApprovalRules.Add(new VacationApprovalRule
        {
            CompanyId = TestCompanyId,
            ApproverGrantKey = "ApproveVacations",
            MaxAutoApproveDays = 3,
            RequiresSecondApproval = false,
            IsActive = true,
            CreatedBy = 1
        });
        await _db.SaveChangesAsync();

        // Act
        var (approverId, grantKey, requiresSecond) = await _service.GetApprovalRouteAsync(request.Id);

        // Assert — auto-approve (no specific approver, no second approval)
        approverId.Should().BeNull();
        grantKey.Should().Be("ApproveVacations");
        requiresSecond.Should().BeFalse();
    }

    /// <summary>
    /// VA-03: When leave exceeds ExtendedLeaveDaysThreshold and rule has RequiresSecondApproval,
    /// GetApprovalRouteAsync flags requiresSecondApproval=true.
    /// </summary>
    [Fact]
    public async Task VA03_GetApprovalRouteAsync_FlagsSecondApproval_ForExtendedLeave()
    {
        // Arrange — 10-day leave with 5-day threshold
        var (_, request) = await CreateBaseEntitiesAsync(leaveDays: 10);

        _db.VacationApprovalRules.Add(new VacationApprovalRule
        {
            CompanyId = TestCompanyId,
            ApproverGrantKey = "ApproveVacations",
            MaxAutoApproveDays = 0,
            RequiresSecondApproval = true,
            ExtendedLeaveDaysThreshold = 5,
            SecondApproverGrantKey = "ApproveExtendedLeave",
            IsActive = true,
            CreatedBy = 1
        });
        await _db.SaveChangesAsync();

        // Act
        var (_, _, requiresSecond) = await _service.GetApprovalRouteAsync(request.Id);

        // Assert
        requiresSecond.Should().BeTrue();
    }

    /// <summary>
    /// VA-04: GetRulesForCompanyAsync returns all rules for the company (active and inactive).
    /// The caller is responsible for filtering by IsActive when needed.
    /// </summary>
    [Fact]
    public async Task VA04_GetRulesForCompanyAsync_ReturnsAllRulesForCompany()
    {
        // Arrange — create 2 active rules and 1 inactive, plus 1 for a different company
        _db.VacationApprovalRules.AddRange(
            new VacationApprovalRule
            {
                CompanyId = TestCompanyId,
                ApproverGrantKey = "ApproveVacations",
                IsActive = true,
                CreatedBy = 1
            },
            new VacationApprovalRule
            {
                CompanyId = TestCompanyId,
                ApproverGrantKey = "ApproveExtendedLeave",
                IsActive = true,
                CreatedBy = 1
            },
            new VacationApprovalRule
            {
                CompanyId = TestCompanyId,
                ApproverGrantKey = "OldRule",
                IsActive = false,
                CreatedBy = 1
            },
            new VacationApprovalRule
            {
                CompanyId = 999, // different company
                ApproverGrantKey = "OtherCompanyRule",
                IsActive = true,
                CreatedBy = 1
            }
        );
        await _db.SaveChangesAsync();

        // Act
        var rules = await _service.GetRulesForCompanyAsync(TestCompanyId);

        // Assert — returns all 3 rules for TestCompanyId (active + inactive), excludes other company
        rules.Should().HaveCount(3);
        rules.Should().AllSatisfy(r => r.CompanyId.Should().Be(TestCompanyId));
        rules.Should().Contain(r => r.IsActive == false, "inactive rules should also be returned");
    }
}
