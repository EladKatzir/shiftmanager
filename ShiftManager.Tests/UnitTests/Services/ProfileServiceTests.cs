using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Dto;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

// NOTE: This fixture intentionally remains on UseInMemoryDatabase. Migrating it to real
// SQLite surfaces 11 unrelated test failures driven by FK-constraint / seed-data semantics
// that the In-Memory provider relaxes (e.g. SQLite enforces FK ordering during seed). Each
// of those is its own root-cause investigation. SearchProfilesAsync's LINQ-translatability
// is independently guarded by `SqliteTranslationGuardTests` against the production service
// directly — that focused test proves the fix without bringing in unrelated regressions.
public class ProfileServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ProfileService _service;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly Mock<ITenantResolver> _tenantResolverMock;

    private const int ManagerUserId = 100;
    private const int EmployeeUserId = 1;
    private const int CompanyId = 1;

    public ProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);

        _grantServiceMock = new Mock<IGrantService>();
        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyId);

        var localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        localizerMock.Setup(x => x[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));

        _service = new ProfileService(
            _db,
            _tenantResolverMock.Object,
            _grantServiceMock.Object,
            Mock.Of<ILogger<ProfileService>>(),
            localizerMock.Object);

        SeedBaseData();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedBaseData()
    {
        _db.Users.AddRange(
            new AppUser
            {
                Id = ManagerUserId, Email = "manager@test.com", DisplayName = "Manager User",
                CompanyId = CompanyId, IsActive = true, Role = UserRole.Manager
            },
            new AppUser
            {
                Id = EmployeeUserId, Email = "employee@test.com", DisplayName = "Employee User",
                CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee,
                Phone = "555-0001", City = "Tel Aviv"
            }
        );
        _db.SaveChanges();
    }

    private void SetupManagerGrant(bool hasGrant = true)
    {
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(ManagerUserId, "EditCompanyUsers", CompanyId))
            .ReturnsAsync(hasGrant);
    }

    private void SetupEmployeeGrant(bool hasGrant = false)
    {
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(EmployeeUserId, "EditCompanyUsers", CompanyId))
            .ReturnsAsync(hasGrant);
    }

    // --- CanEditFieldAsync ---

    [Fact]
    public async Task CanEditFieldAsync_ManagerWithGrant_CanEditAnyField()
    {
        SetupManagerGrant(true);

        var canEditEmail = await _service.CanEditFieldAsync(ManagerUserId, EmployeeUserId, nameof(AppUser.Email));
        var canEditDisplayName = await _service.CanEditFieldAsync(ManagerUserId, EmployeeUserId, nameof(AppUser.DisplayName));

        canEditEmail.Should().BeTrue();
        canEditDisplayName.Should().BeTrue();
    }

    [Fact]
    public async Task CanEditFieldAsync_EmployeeSelf_CanEditOwnFields()
    {
        SetupEmployeeGrant(false);

        var canEditDisplayName = await _service.CanEditFieldAsync(EmployeeUserId, EmployeeUserId, nameof(AppUser.DisplayName));
        var canEditPhone = await _service.CanEditFieldAsync(EmployeeUserId, EmployeeUserId, nameof(AppUser.Phone));

        canEditDisplayName.Should().BeTrue();
        canEditPhone.Should().BeTrue();
    }

    [Fact]
    public async Task CanEditFieldAsync_EmployeeSelf_CannotEditManagerOnlyFields()
    {
        SetupEmployeeGrant(false);

        var canEditEmail = await _service.CanEditFieldAsync(EmployeeUserId, EmployeeUserId, nameof(AppUser.Email));
        var canEditRole = await _service.CanEditFieldAsync(EmployeeUserId, EmployeeUserId, nameof(AppUser.Role));

        canEditEmail.Should().BeFalse();
        canEditRole.Should().BeFalse();
    }

    [Fact]
    public async Task CanEditFieldAsync_EmployeeOther_CannotEditAnything()
    {
        SetupEmployeeGrant(false);
        var otherEmployeeId = 2;
        _db.Users.Add(new AppUser { Id = otherEmployeeId, Email = "other@test.com", DisplayName = "Other", CompanyId = CompanyId, IsActive = true });
        await _db.SaveChangesAsync();

        var canEdit = await _service.CanEditFieldAsync(EmployeeUserId, otherEmployeeId, nameof(AppUser.DisplayName));

        canEdit.Should().BeFalse();
    }

    [Fact]
    public async Task CanEditFieldAsync_TargetNotFound_ReturnsFalse()
    {
        var canEdit = await _service.CanEditFieldAsync(ManagerUserId, 999, nameof(AppUser.DisplayName));

        canEdit.Should().BeFalse();
    }

    // --- UpdateProfileAsync ---

    [Fact]
    public async Task UpdateProfileAsync_ManagerUpdatesEmployeeDisplayName_Succeeds()
    {
        SetupManagerGrant(true);

        var dto = new ProfileUpdateDto { DisplayName = "New Name" };

        var (success, error) = await _service.UpdateProfileAsync(ManagerUserId, EmployeeUserId, dto);

        success.Should().BeTrue();
        error.Should().BeNull();

        var user = await _db.Users.FindAsync(EmployeeUserId);
        user!.DisplayName.Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateProfileAsync_EmployeeSelfUpdatesPhone_Succeeds()
    {
        SetupEmployeeGrant(false);

        var dto = new ProfileUpdateDto { Phone = "555-9999" };

        var (success, error) = await _service.UpdateProfileAsync(EmployeeUserId, EmployeeUserId, dto);

        success.Should().BeTrue();

        var user = await _db.Users.FindAsync(EmployeeUserId);
        user!.Phone.Should().Be("555-9999");
    }

    [Fact]
    public async Task UpdateProfileAsync_CreatesAuditEntries()
    {
        SetupManagerGrant(true);

        // Set Phone + City to preserve existing values, only change DisplayName + City
        var dto = new ProfileUpdateDto { DisplayName = "Audited Name", Phone = "555-0001", City = "Haifa" };

        await _service.UpdateProfileAsync(ManagerUserId, EmployeeUserId, dto);

        var audits = await _db.ProfileChangeAudits.Where(a => a.TargetUserId == EmployeeUserId).ToListAsync();
        audits.Should().HaveCount(2);
        audits.Should().Contain(a => a.FieldName == nameof(AppUser.DisplayName));
        audits.Should().Contain(a => a.FieldName == nameof(AppUser.City));
    }

    [Fact]
    public async Task UpdateProfileAsync_EmployeeCannotChangeEmail_ReturnsError()
    {
        SetupEmployeeGrant(false);

        var dto = new ProfileUpdateDto { Email = "new@test.com" };

        var (success, error) = await _service.UpdateProfileAsync(EmployeeUserId, EmployeeUserId, dto);

        success.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateProfileAsync_EmployeeCannotChangeOtherProfile_ReturnsError()
    {
        SetupEmployeeGrant(false);

        var dto = new ProfileUpdateDto { DisplayName = "Hacked" };

        var (success, error) = await _service.UpdateProfileAsync(EmployeeUserId, ManagerUserId, dto);

        success.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateProfileAsync_ManagerChangesRole_Succeeds()
    {
        SetupManagerGrant(true);

        var dto = new ProfileUpdateDto { Role = UserRole.Manager };

        var (success, error) = await _service.UpdateProfileAsync(ManagerUserId, EmployeeUserId, dto);

        success.Should().BeTrue();
        var user = await _db.Users.FindAsync(EmployeeUserId);
        user!.Role.Should().Be(UserRole.Manager);
    }

    [Fact]
    public async Task UpdateProfileAsync_EmployeeCannotChangeRole_ReturnsError()
    {
        SetupEmployeeGrant(false);

        var dto = new ProfileUpdateDto { Role = UserRole.Manager };

        var (success, error) = await _service.UpdateProfileAsync(EmployeeUserId, EmployeeUserId, dto);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfileAsync_ManagerChangesIsActive_Succeeds()
    {
        SetupManagerGrant(true);

        var dto = new ProfileUpdateDto { IsActive = false };

        var (success, error) = await _service.UpdateProfileAsync(ManagerUserId, EmployeeUserId, dto);

        success.Should().BeTrue();
        var user = await _db.Users.FindAsync(EmployeeUserId);
        user!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfileAsync_EditorNotFound_ReturnsError()
    {
        var dto = new ProfileUpdateDto { DisplayName = "Test" };

        var (success, error) = await _service.UpdateProfileAsync(999, EmployeeUserId, dto);

        success.Should().BeFalse();
        error.Should().Be("EditorUserNotFound");
    }

    [Fact]
    public async Task UpdateProfileAsync_TargetNotFound_ReturnsError()
    {
        var dto = new ProfileUpdateDto { DisplayName = "Test" };

        var (success, error) = await _service.UpdateProfileAsync(ManagerUserId, 999, dto);

        success.Should().BeFalse();
        error.Should().Be("TargetUserNotFound");
    }

    [Fact]
    public async Task UpdateProfileAsync_NoChanges_ReturnsSuccessWithoutAudit()
    {
        SetupEmployeeGrant(false);

        // Provide same values — no changes detected
        var dto = new ProfileUpdateDto { Phone = "555-0001", City = "Tel Aviv" };

        var (success, error) = await _service.UpdateProfileAsync(EmployeeUserId, EmployeeUserId, dto);

        success.Should().BeTrue();
        var audits = await _db.ProfileChangeAudits.CountAsync();
        audits.Should().Be(0);
    }

    [Fact]
    public async Task UpdateProfileAsync_SetsProfileLastUpdated()
    {
        SetupManagerGrant(true);

        var dto = new ProfileUpdateDto { DisplayName = "Updated" };

        await _service.UpdateProfileAsync(ManagerUserId, EmployeeUserId, dto);

        var user = await _db.Users.FindAsync(EmployeeUserId);
        user!.ProfileLastUpdated.Should().NotBeNull();
        user.ProfileLastUpdatedBy.Should().Be(ManagerUserId);
    }

    // --- GetProfileHistoryAsync ---

    [Fact]
    public async Task GetProfileHistoryAsync_ReturnsRecentAudits()
    {
        // Seed audit entries
        _db.ProfileChangeAudits.AddRange(
            new ProfileChangeAudit
            {
                CompanyId = CompanyId, TargetUserId = EmployeeUserId, ChangedBy = ManagerUserId,
                FieldName = "DisplayName", OldValue = "Old", NewValue = "New",
                Timestamp = DateTime.UtcNow.AddDays(-10)
            },
            new ProfileChangeAudit
            {
                CompanyId = CompanyId, TargetUserId = EmployeeUserId, ChangedBy = ManagerUserId,
                FieldName = "Phone", OldValue = "111", NewValue = "222",
                Timestamp = DateTime.UtcNow.AddDays(-100) // Outside 90-day window
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetProfileHistoryAsync(EmployeeUserId, days: 90);

        result.Should().HaveCount(1);
        result[0].FieldName.Should().Be("DisplayName");
    }

    // --- SearchProfilesAsync ---

    [Fact]
    public async Task SearchProfilesAsync_FindsByDisplayName()
    {
        var result = await _service.SearchProfilesAsync("Employee");

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(EmployeeUserId);
    }

    [Fact]
    public async Task SearchProfilesAsync_FindsByEmail()
    {
        var result = await _service.SearchProfilesAsync("manager@test.com");

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(ManagerUserId);
    }

    [Fact]
    public async Task SearchProfilesAsync_EmptyQuery_ReturnsEmpty()
    {
        var result = await _service.SearchProfilesAsync("");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchProfilesAsync_ExcludesInactiveUsers()
    {
        _db.Users.Add(new AppUser
        {
            Id = 50, Email = "inactive@test.com", DisplayName = "Inactive SearchUser",
            CompanyId = CompanyId, IsActive = false
        });
        await _db.SaveChangesAsync();

        var result = await _service.SearchProfilesAsync("Inactive SearchUser");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchProfilesAsync_RespectsMaxResults()
    {
        for (int i = 10; i < 30; i++)
        {
            _db.Users.Add(new AppUser
            {
                Id = i, Email = $"batch{i}@test.com", DisplayName = $"BatchUser {i}",
                CompanyId = CompanyId, IsActive = true
            });
        }
        await _db.SaveChangesAsync();

        var result = await _service.SearchProfilesAsync("BatchUser", maxResults: 5);

        result.Should().HaveCount(5);
    }
}
