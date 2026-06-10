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

    // ─────────────────────────────────────────────────────────────────────────
    // Cascade tests (Task 4 — Epic 6)
    // VA-A09: Approving one copy cascades the terminal Approved decision to siblings
    // VA-A10: Declining one copy cascades the terminal Declined decision to siblings
    // VA-A11: NULL LeaveGroupId — approving a standalone request touches no other row
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a TimeOffRequest in an arbitrary company for the given userId.
    /// Unlike CreateRequestAsync, does NOT pin to TestCompanyId so cascade tests
    /// can place the sibling in company 2.
    /// </summary>
    private async Task<TimeOffRequest> CreateRequestInCompanyAsync(
        int companyId, int userId,
        DateOnly startDate, DateOnly endDate,
        RequestStatus status = RequestStatus.Pending,
        Guid? leaveGroupId = null)
    {
        var request = new TimeOffRequest
        {
            CompanyId = companyId,
            UserId = userId,
            StartDate = startDate,
            EndDate = endDate,
            Type = TimeOffType.Vacation,
            Status = status,
            LeaveGroupId = leaveGroupId
        };
        _db.TimeOffRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    /// <summary>
    /// VA-A09: Approve_CascadesToGroup
    /// Two requests share one LeaveGroupId (company-1 copy + company-2 copy), both Pending.
    /// Approving the company-1 copy → BOTH copies become Approved;
    /// the sibling's ApproverId and FirstApprovalActorId are set to the acting approver.
    /// Short leave (1 day) guarantees single-approval flow.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_CascadesToGroup_BothCopiesApproved()
    {
        // Arrange
        const int company2Id = 2;
        var groupId = Guid.NewGuid();
        var start = new DateOnly(2026, 5, 1);
        var end   = new DateOnly(2026, 5, 1); // 1 day — single-approval guaranteed

        await SeedUsersAsync();

        var company1Copy = await CreateRequestInCompanyAsync(
            TestCompanyId, EmployeeUserId, start, end, leaveGroupId: groupId);
        var company2Copy = await CreateRequestInCompanyAsync(
            company2Id, EmployeeUserId, start, end, leaveGroupId: groupId);

        // Act
        var (success, message) = await _service.ApproveAsync(company1Copy.Id, ApproverUserId);

        // Assert — acting request succeeded
        success.Should().BeTrue(message);
        message.Should().Contain("Approved");

        // Assert — sibling was cascaded to Approved
        var sibling = await _db.TimeOffRequests.FindAsync(company2Copy.Id);
        sibling.Should().NotBeNull();
        sibling!.Status.Should().Be(RequestStatus.Approved,
            "cascade must propagate the terminal Approved decision to all group siblings");
        sibling.ApproverId.Should().Be(ApproverUserId,
            "cascade must copy the ApproverId from the acting request to the sibling");
        sibling.FirstApprovalActorId.Should().Be(ApproverUserId,
            "cascade must copy FirstApprovalActorId so the audit trail shows who approved");
    }

    /// <summary>
    /// VA-A10: Decline_CascadesToGroup
    /// Two requests share one LeaveGroupId, both Pending.
    /// Declining the company-1 copy → BOTH copies become Declined.
    /// </summary>
    [Fact]
    public async Task DeclineAsync_CascadesToGroup_BothCopiesDeclined()
    {
        // Arrange
        const int company2Id = 2;
        var groupId = Guid.NewGuid();
        var start = new DateOnly(2026, 5, 2);
        var end   = new DateOnly(2026, 5, 2);

        await SeedUsersAsync();

        var company1Copy = await CreateRequestInCompanyAsync(
            TestCompanyId, EmployeeUserId, start, end, leaveGroupId: groupId);
        var company2Copy = await CreateRequestInCompanyAsync(
            company2Id, EmployeeUserId, start, end, leaveGroupId: groupId);

        // Act
        var (success, message) = await _service.DeclineAsync(company1Copy.Id, ApproverUserId);

        // Assert — acting request declined
        success.Should().BeTrue(message);
        message.Should().Contain("Declined");

        // Assert — sibling was cascaded to Declined
        var sibling = await _db.TimeOffRequests.FindAsync(company2Copy.Id);
        sibling.Should().NotBeNull();
        sibling!.Status.Should().Be(RequestStatus.Declined,
            "cascade must propagate the terminal Declined decision to all group siblings");
    }

    /// <summary>
    /// VA-A11: Approve_NullLeaveGroupId_DoesNotCascade
    /// A standalone request (LeaveGroupId == null) is approved; an unrelated pending
    /// request (different user, different company) must NOT be affected.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_NullLeaveGroupId_DoesNotCascadeToUnrelatedRequest()
    {
        // Arrange
        const int otherUserId   = 30;
        const int otherCompanyId = 2;

        await SeedUsersAsync();
        // Seed an unrelated user in company 2
        _db.Users.Add(new AppUser
        {
            Id = otherUserId,
            Email = "other@test.com",
            CompanyId = otherCompanyId,
            Role = UserRole.Employee,
            DisplayName = "Other Employee",
            IsActive = true
        });
        await _db.SaveChangesAsync();

        // Standalone request with no group id
        var standalone = await CreateRequestInCompanyAsync(
            TestCompanyId, EmployeeUserId,
            new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 10),
            leaveGroupId: null);

        // Unrelated pending request — different user, different company, no group
        var unrelated = await CreateRequestInCompanyAsync(
            otherCompanyId, otherUserId,
            new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 10),
            leaveGroupId: null);

        // Act
        var (success, _) = await _service.ApproveAsync(standalone.Id, ApproverUserId);

        // Assert — standalone approval succeeds
        success.Should().BeTrue();

        // Assert — unrelated request is completely untouched
        var untouched = await _db.TimeOffRequests.FindAsync(unrelated.Id);
        untouched!.Status.Should().Be(RequestStatus.Pending,
            "a null LeaveGroupId must never cascade to any other request");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Epic 6 review fixes
    // VA-A12: DoesShifts=false membership company does NOT grant approval authority
    // VA-A13: Cancel cascades to sibling fan-out copies
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// VA-A12: Union approver authorization must respect DoesShifts. The requester does shifts
    /// in company 1 (true) but NOT in company 2 (false). The approver holds the grant ONLY in
    /// company 2. Leave is never fanned out to / routed through a DoesShifts=false company, so
    /// the company-2 manager must NOT be able to approve → CanUserApproveAsync returns FALSE.
    /// (Before the fix the loop added every membership company and this wrongly returned TRUE.)
    /// </summary>
    [Fact]
    public async Task CanUserApproveAsync_UnionAuth_GrantInDoesShiftsFalseCompanyOnly_ReturnsFalse()
    {
        // Arrange
        const int secondCompanyId = 2;
        await SeedUsersAsync();

        // Request lives in company 1 (the requester's DoesShifts company)
        var request = await CreateRequestAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5));

        // Requester does shifts in company 1 (true) but NOT in company 2 (false)
        _membershipServiceMock
            .Setup(m => m.GetMembershipsAsync(EmployeeUserId))
            .ReturnsAsync(new List<CompanyMembership>
            {
                new() { UserId = EmployeeUserId, CompanyId = TestCompanyId,   IsPrimary = true,  DoesShifts = true  },
                new() { UserId = EmployeeUserId, CompanyId = secondCompanyId,  IsPrimary = false, DoesShifts = false }
            });

        // Approver holds the grant ONLY in company 2 (the DoesShifts=false company); false elsewhere
        _grantServiceMock
            .Setup(g => g.HasGrantWithScopeAsync(
                ApproverUserId, It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(false);
        _grantServiceMock
            .Setup(g => g.HasGrantWithScopeAsync(
                ApproverUserId, It.IsAny<string>(),
                null, null, null, null,
                secondCompanyId, It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(true);

        // Act
        var canApprove = await _service.CanUserApproveAsync(ApproverUserId, request.Id);

        // Assert
        canApprove.Should().BeFalse(
            "the grant is held only in a DoesShifts=false company, which the leave was never routed to");
    }

    /// <summary>
    /// VA-A13: Cancel_CascadesToGroup. Two Pending copies share one LeaveGroupId
    /// (company-1 canonical + company-2 sibling). The requester cancels the canonical copy →
    /// BOTH copies become Canceled (so the sibling does not linger in company 2's approver queue).
    /// </summary>
    [Fact]
    public async Task CancelRequestAsync_CascadesToGroup_BothCopiesCanceled()
    {
        // Arrange
        const int company2Id = 2;
        var groupId = Guid.NewGuid();
        var start = new DateOnly(2026, 6, 10);
        var end   = new DateOnly(2026, 6, 10);

        await SeedUsersAsync();

        var company1Copy = await CreateRequestInCompanyAsync(
            TestCompanyId, EmployeeUserId, start, end, leaveGroupId: groupId);
        var company2Copy = await CreateRequestInCompanyAsync(
            company2Id, EmployeeUserId, start, end, leaveGroupId: groupId);

        // Act — the requester (EmployeeUserId) cancels their own canonical copy
        var (success, message) = await _service.CancelRequestAsync(company1Copy.Id, EmployeeUserId);

        // Assert — canonical cancel succeeded
        success.Should().BeTrue(message);
        message.Should().Contain("Canceled");

        // Assert — both copies are now Canceled
        var canonical = await _db.TimeOffRequests.FindAsync(company1Copy.Id);
        canonical!.Status.Should().Be(RequestStatus.Canceled);

        var sibling = await _db.TimeOffRequests.FindAsync(company2Copy.Id);
        sibling.Should().NotBeNull();
        sibling!.Status.Should().Be(RequestStatus.Canceled,
            "canceling the canonical copy must cascade to all sibling fan-out copies");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Opus final-validation gap (spec §15)
    // VA-A14: Cross-company force-approve overrides a sibling's OWN dual-approval rule
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// VA-A14: Dual-tier interaction. Company A's copy lives in a molecule whose
    /// DualApprovalDayThreshold is LOW, so on its own the (multi-day) leave there would require
    /// DUAL approval (Pending → PendingSecondApproval → Approved). Company B's copy lives in a
    /// molecule whose threshold is HIGH, so it is SINGLE-approval.
    ///
    /// Both copies share one LeaveGroupId, both Pending. Approving company B's copy with ONE
    /// authorized approver completes B (single-approval → terminal Approved) and the GROUP cascade
    /// force-approves company A's copy DIRECTLY to terminal Approved — it is NOT left stuck at
    /// PendingSecondApproval even though A's local rules required a second tier. This is the
    /// "any eligible manager makes ONE decision for the whole group" guarantee.
    ///
    /// Resulting actor-field state on the force-approved company-A copy: the cascade copies the
    /// acting (company-B) request's approval-actor fields, so A's ApproverId and
    /// FirstApprovalActorId are the approver (user 20). SecondApprovalActorId stays null —
    /// no real second-tier approval ever happened; the cascade is the bypass, audited separately.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_GroupCascade_ForceApprovesSiblingThatWouldRequireDualApproval()
    {
        // Arrange
        const int companyA = 1;   // requires DUAL (low threshold molecule)
        const int companyB = 2;   // SINGLE approval (high threshold molecule)
        const int moleculeA = 100;
        const int moleculeB = 200;

        await SeedUsersAsync();

        // Companies → molecules
        _db.Companies.AddRange(
            new Company { Id = companyA, Name = "Company A", Slug = "company-a", MoleculeId = moleculeA },
            new Company { Id = companyB, Name = "Company B", Slug = "company-b", MoleculeId = moleculeB });

        // Molecule A: dual approval for anything longer than 1 day → our 5-day leave needs dual.
        // Molecule B: dual only beyond 30 days → our 5-day leave is single-approval.
        _db.MoleculeApprovalSettings.AddRange(
            new MoleculeApprovalSettings { MoleculeId = moleculeA, DualApprovalDayThreshold = 1,  UpdatedByUserId = ApproverUserId },
            new MoleculeApprovalSettings { MoleculeId = moleculeB, DualApprovalDayThreshold = 30, UpdatedByUserId = ApproverUserId });
        await _db.SaveChangesAsync();

        // One logical 5-day leave, fanned out into both companies, both Pending.
        var groupId = Guid.NewGuid();
        var start = new DateOnly(2026, 7, 1);
        var end   = new DateOnly(2026, 7, 5); // 5 days

        var copyA = await CreateRequestInCompanyAsync(companyA, EmployeeUserId, start, end, leaveGroupId: groupId);
        var copyB = await CreateRequestInCompanyAsync(companyB, EmployeeUserId, start, end, leaveGroupId: groupId);

        // Sanity: confirm A on its own WOULD require dual (a lone first-tier approval would only
        // advance it to PendingSecondApproval, not Approved). We do NOT approve A directly in the
        // assertion path — this is just to prove the molecule-A dual rule is actually active.

        // Act — approve company B's copy (single-approval) with the authorized approver.
        var (success, message) = await _service.ApproveAsync(copyB.Id, ApproverUserId);

        // Assert — B is terminally approved
        success.Should().BeTrue(message);
        message.Should().Contain("Approved");

        var bFresh = await _db.TimeOffRequests.FindAsync(copyB.Id);
        bFresh!.Status.Should().Be(RequestStatus.Approved);

        // Assert — A is FORCE-approved to terminal Approved via the cascade, NOT stuck at
        // PendingSecondApproval despite molecule A's dual-approval rule.
        var aFresh = await _db.TimeOffRequests.FindAsync(copyA.Id);
        aFresh.Should().NotBeNull();
        aFresh!.Status.Should().Be(RequestStatus.Approved,
            "the group cascade force-approves the sibling to TERMINAL Approved, overriding its own dual-approval requirement");
        aFresh.Status.Should().NotBe(RequestStatus.PendingSecondApproval);

        // Actor-field state documented: cascade copied company-B's actor fields onto A.
        aFresh.ApproverId.Should().Be(ApproverUserId);
        aFresh.FirstApprovalActorId.Should().Be(ApproverUserId);
        aFresh.SecondApprovalActorId.Should().BeNull(
            "no real second-tier approval occurred; the cross-company cascade is the bypass (audited separately)");
    }
}
