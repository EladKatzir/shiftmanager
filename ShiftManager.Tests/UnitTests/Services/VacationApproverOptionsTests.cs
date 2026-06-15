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
/// Tests for VacationApprovalService.GetGrantBasedApproverOptionsAsync — the GRANT-BASED
/// pool that backs the optional "specific approver" dropdown on the time-off request form.
/// This is distinct from GetApproverPoolAsync (job-type-vertical routing pool). It returns
/// users holding ApproveVacations or ApproveExtendedLeave scoped to any of the requester's
/// shift-active companies (multi-company union).
///
/// AO-01: company-scoped approver with CanOwn is included
/// AO-02: grant without CanOwn is excluded
/// AO-03: inactive approver is excluded
/// AO-04: approver scoped to a different company is excluded
/// AO-05: multi-company member — approver in a second DoesShifts company is included
/// </summary>
public class VacationApproverOptionsTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<ICompanyMembershipService> _membershipServiceMock;
    private readonly VacationApprovalService _service;

    private const int ApproveVacationsGrantTypeId = 100;
    private const int ApproveExtendedLeaveGrantTypeId = 101;

    public VacationApproverOptionsTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var grantServiceMock = new Mock<IGrantService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var traineeServiceMock = new Mock<ITraineeService>();
        var materialiserMock = new Mock<IHomeMaterialiserService>();
        var loggerMock = new Mock<ILogger<VacationApprovalService>>();
        var auditLogServiceMock = new Mock<IAuditLogService>();
        var localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        var featureFlagServiceMock = new Mock<IFeatureFlagService>();
        localizerMock
            .Setup(l => l[It.IsAny<string>()])
            .Returns((string name) => new LocalizedString(name, name));

        // Configurable per-test. Default: no extra memberships (single-company behavior).
        _membershipServiceMock = new Mock<ICompanyMembershipService>();
        _membershipServiceMock
            .Setup(m => m.GetMembershipsAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<CompanyMembership>());

        _service = new VacationApprovalService(
            _db,
            grantServiceMock.Object,
            loggerMock.Object,
            notificationServiceMock.Object,
            traineeServiceMock.Object,
            materialiserMock.Object,
            auditLogServiceMock.Object,
            localizerMock.Object,
            featureFlagServiceMock.Object,
            _membershipServiceMock.Object);

        // Two grant types the dropdown query keys off of.
        _db.GrantTypes.Add(new GrantType { Id = ApproveVacationsGrantTypeId, Key = "ApproveVacations" });
        _db.GrantTypes.Add(new GrantType { Id = ApproveExtendedLeaveGrantTypeId, Key = "ApproveExtendedLeave" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private Company AddCompany(int id, int moleculeId)
    {
        var c = new Company { Id = id, Name = $"Company {id}", MoleculeId = moleculeId };
        _db.Companies.Add(c);
        return c;
    }

    private AppUser AddUser(int id, int companyId, string name, bool isActive = true,
        UserRole role = UserRole.Employee)
    {
        var u = new AppUser
        {
            Id = id,
            Email = $"user{id}@test.com",
            CompanyId = companyId,
            DisplayName = name,
            IsActive = isActive,
            Role = role
        };
        _db.Users.Add(u);
        return u;
    }

    private void AddCompanyScopedGrant(int userId, int companyId, int grantTypeId, bool canOwn)
    {
        _db.Grants.Add(new Grant
        {
            UserId = userId,
            GrantTypeId = grantTypeId,
            CompanyId = companyId,
            CanOwn = canOwn
        });
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_IncludesCompanyScopedApproverWithCanOwn()
    {
        AddCompany(1, moleculeId: 10);
        AddUser(1, companyId: 1, "Requester");
        AddUser(2, companyId: 1, "Approver Alice");
        AddCompanyScopedGrant(userId: 2, companyId: 1, ApproveVacationsGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().ContainSingle(o => o.Id == 2 && o.Name == "Approver Alice");
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_ExcludesGrantWithoutCanOwn()
    {
        AddCompany(1, moleculeId: 10);
        AddUser(1, companyId: 1, "Requester");
        AddUser(2, companyId: 1, "CanGive Only");
        AddCompanyScopedGrant(userId: 2, companyId: 1, ApproveVacationsGrantTypeId, canOwn: false);
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().NotContain(o => o.Id == 2);
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_ExcludesInactiveUser()
    {
        AddCompany(1, moleculeId: 10);
        AddUser(1, companyId: 1, "Requester");
        AddUser(2, companyId: 1, "Inactive Approver", isActive: false);
        AddCompanyScopedGrant(userId: 2, companyId: 1, ApproveVacationsGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().NotContain(o => o.Id == 2);
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_ExcludesApproverScopedToDifferentCompany()
    {
        AddCompany(1, moleculeId: 10);
        AddCompany(2, moleculeId: 20);
        AddUser(1, companyId: 1, "Requester");
        AddUser(2, companyId: 2, "Other-Company Approver");
        // Grant scoped to company 2, which the requester is NOT a member of.
        AddCompanyScopedGrant(userId: 2, companyId: 2, ApproveVacationsGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().NotContain(o => o.Id == 2);
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_MultiCompanyMember_IncludesApproverInSecondCompany()
    {
        AddCompany(1, moleculeId: 10);
        AddCompany(2, moleculeId: 20);
        AddUser(1, companyId: 1, "Requester");
        AddUser(2, companyId: 2, "Second-Company Approver");
        AddCompanyScopedGrant(userId: 2, companyId: 2, ApproveExtendedLeaveGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        // Requester has a shift-active (DoesShifts) membership in company 2.
        _membershipServiceMock
            .Setup(m => m.GetMembershipsAsync(1))
            .ReturnsAsync(new List<CompanyMembership>
            {
                new() { UserId = 1, CompanyId = 2, DoesShifts = true }
            });

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().ContainSingle(o => o.Id == 2 && o.Name == "Second-Company Approver");
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_ExcludesApproverWhoseGrantIsPinnedToDifferentJobType()
    {
        // #4: the offered pool must agree with the job-type-aware approval gate (CanUserApproveAsync).
        // An approver whose ApproveVacations grant is pinned to a DIFFERENT job type than the requester
        // would be rejected at approval time, so it must NOT be offered in the dropdown.
        AddCompany(1, moleculeId: 10);
        var requester = AddUser(1, companyId: 1, "Requester");
        requester.JobTypeId = 7; // e.g. Alhut
        AddUser(2, companyId: 1, "BR-Pinned Approver");
        _db.Grants.Add(new Grant
        {
            UserId = 2,
            GrantTypeId = ApproveVacationsGrantTypeId,
            CompanyId = 1,
            JobTypeId = 8, // pinned to a different job type (e.g. BR)
            CanOwn = true
        });
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().NotContain(o => o.Id == 2);
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_IncludesApproverWhoseGrantIsPinnedToRequesterJobType()
    {
        // Positive companion: a grant pinned to the requester's OWN job type is still offered
        // (and a null-jobType grant remains offered — covered by AO-01).
        AddCompany(1, moleculeId: 10);
        var requester = AddUser(1, companyId: 1, "Requester");
        requester.JobTypeId = 7;
        AddUser(2, companyId: 1, "Matching Approver");
        _db.Grants.Add(new Grant
        {
            UserId = 2,
            GrantTypeId = ApproveVacationsGrantTypeId,
            CompanyId = 1,
            JobTypeId = 7, // same job type as the requester
            CanOwn = true
        });
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().ContainSingle(o => o.Id == 2 && o.Name == "Matching Approver");
    }

    // ── IsEligibleApproverAsync — submit-time validation must agree with the dropdown ──

    [Fact]
    public async Task IsEligibleApprover_PrimaryCompanyApprover_ReturnsTrue()
    {
        AddCompany(1, moleculeId: 10);
        AddUser(1, companyId: 1, "Requester");
        AddUser(2, companyId: 1, "Primary Approver");
        AddCompanyScopedGrant(userId: 2, companyId: 1, ApproveVacationsGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        (await _service.IsEligibleApproverAsync(requesterUserId: 1, approverUserId: 2)).Should().BeTrue();
    }

    [Fact]
    public async Task IsEligibleApprover_SecondaryCompanyApprover_ReturnsTrue()
    {
        // THE BUG FIX: a multi-company requester picks an approver scoped only to their
        // SECONDARY company. The old submit-validation checked only the primary company and
        // rejected this approver even though the dropdown offered them.
        AddCompany(1, moleculeId: 10);
        AddCompany(2, moleculeId: 20);
        AddUser(1, companyId: 1, "Requester");
        AddUser(2, companyId: 2, "Secondary-Company Approver");
        AddCompanyScopedGrant(userId: 2, companyId: 2, ApproveVacationsGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        _membershipServiceMock
            .Setup(m => m.GetMembershipsAsync(1))
            .ReturnsAsync(new List<CompanyMembership>
            {
                new() { UserId = 1, CompanyId = 2, DoesShifts = true }
            });

        (await _service.IsEligibleApproverAsync(requesterUserId: 1, approverUserId: 2)).Should().BeTrue();
    }

    [Fact]
    public async Task IsEligibleApprover_NonApprover_ReturnsFalse()
    {
        AddCompany(1, moleculeId: 10);
        AddUser(1, companyId: 1, "Requester");
        AddUser(2, companyId: 1, "Random User"); // holds no approval grant
        await _db.SaveChangesAsync();

        (await _service.IsEligibleApproverAsync(requesterUserId: 1, approverUserId: 2)).Should().BeFalse();
    }

    // ── AccountType filtering — AO-06/07/08 ──

    [Fact]
    public async Task GetGrantBasedApproverOptions_ExcludesMilApprover()
    {
        // AO-06: a Mil user with a valid, CanOwn ApproveVacations grant must NOT appear in the pool.
        AddCompany(1, moleculeId: 10);
        AddUser(1, companyId: 1, "Requester");

        var milUser = new AppUser
        {
            Id = 10,
            Email = "mil@test.com",
            CompanyId = 1,
            DisplayName = "Mil Approver",
            IsActive = true,
            Role = UserRole.Employee,
            AccountType = AccountType.Mil
        };
        _db.Users.Add(milUser);
        AddCompanyScopedGrant(userId: 10, companyId: 1, ApproveVacationsGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().NotContain(o => o.Id == 10,
            "Mil account types must not appear as selectable approvers");
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_ExcludesGroupUserApprover()
    {
        // AO-07: a GroupUser with a valid, CanOwn ApproveVacations grant must NOT appear in the pool.
        AddCompany(1, moleculeId: 10);
        AddUser(1, companyId: 1, "Requester");

        var groupUser = new AppUser
        {
            Id = 11,
            Email = "group@test.com",
            CompanyId = 1,
            DisplayName = "Group Approver",
            IsActive = true,
            Role = UserRole.Employee,
            AccountType = AccountType.GroupUser
        };
        _db.Users.Add(groupUser);
        AddCompanyScopedGrant(userId: 11, companyId: 1, ApproveVacationsGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().NotContain(o => o.Id == 11,
            "GroupUser account types must not appear as selectable approvers");
    }

    [Fact]
    public async Task GetGrantBasedApproverOptions_StandardApproverIncluded_MilAndGroupExcluded()
    {
        // AO-08: with all three account types holding the same grant, only Standard appears.
        AddCompany(1, moleculeId: 10);
        AddUser(1, companyId: 1, "Requester"); // id=1, Standard

        var standardApprover = new AppUser
        {
            Id = 20,
            Email = "std@test.com",
            CompanyId = 1,
            DisplayName = "Standard Approver",
            IsActive = true,
            Role = UserRole.Employee,
            AccountType = AccountType.Standard
        };
        var milApprover = new AppUser
        {
            Id = 21,
            Email = "mil2@test.com",
            CompanyId = 1,
            DisplayName = "Mil Approver",
            IsActive = true,
            Role = UserRole.Employee,
            AccountType = AccountType.Mil
        };
        var groupApprover = new AppUser
        {
            Id = 22,
            Email = "grp2@test.com",
            CompanyId = 1,
            DisplayName = "Group Approver",
            IsActive = true,
            Role = UserRole.Employee,
            AccountType = AccountType.GroupUser
        };
        _db.Users.AddRange(standardApprover, milApprover, groupApprover);
        AddCompanyScopedGrant(userId: 20, companyId: 1, ApproveVacationsGrantTypeId, canOwn: true);
        AddCompanyScopedGrant(userId: 21, companyId: 1, ApproveVacationsGrantTypeId, canOwn: true);
        AddCompanyScopedGrant(userId: 22, companyId: 1, ApproveVacationsGrantTypeId, canOwn: true);
        await _db.SaveChangesAsync();

        var options = await _service.GetGrantBasedApproverOptionsAsync(requesterUserId: 1);

        options.Should().ContainSingle(o => o.Id == 20,
            "only the Standard approver should be included");
        options.Should().NotContain(o => o.Id == 21, "Mil must be excluded");
        options.Should().NotContain(o => o.Id == 22, "GroupUser must be excluded");
    }
}
