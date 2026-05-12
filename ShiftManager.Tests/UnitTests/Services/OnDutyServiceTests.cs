using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for OnDutyService CRUD operations.
/// Eligibility tests are in OnDutyServiceEligibilityTests.cs — not duplicated here.
/// </summary>
public class OnDutyServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly OnDutyService _service;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly Mock<IFeatureFlagService> _featureFlagMock;

    private const int CurrentUserId = 100;
    private const int CurrentUserCompanyId = 1;

    public OnDutyServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        // Setup HTTP context with authenticated user
        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, CurrentUserId.ToString()),
            new Claim("CompanyId", CurrentUserCompanyId.ToString())
        }, "TestAuth"));
        httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _grantServiceMock = new Mock<IGrantService>();
        _featureFlagMock = new Mock<IFeatureFlagService>();
        var directorServiceMock = new Mock<IDirectorService>();
        var loggerMock = Mock.Of<ILogger<OnDutyService>>();

        _service = new OnDutyService(
            _db,
            httpContextAccessorMock.Object,
            directorServiceMock.Object,
            _grantServiceMock.Object,
            loggerMock,
            _featureFlagMock.Object,
            BusyServiceMockFactory.Real(_db));

        // Seed current user
        _db.Users.Add(new AppUser
        {
            Id = CurrentUserId, Email = "manager@test.com", DisplayName = "Manager User",
            CompanyId = CurrentUserCompanyId, IsActive = true, Role = UserRole.Manager
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private void SetupHakamGrantPermissions(bool canManage = true)
    {
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignHakamDuties"))
            .ReturnsAsync(canManage);
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignKatzinDuties"))
            .ReturnsAsync(false);
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(CurrentUserId, "AssignHakamDuties", It.IsAny<int>()))
            .ReturnsAsync(canManage);
    }

    private void SetupLeadGrantPermissions(bool canManage = true)
    {
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignKatzinDuties"))
            .ReturnsAsync(canManage);
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignHakamDuties"))
            .ReturnsAsync(false);
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(CurrentUserId, "AssignKatzinDuties", It.IsAny<int>()))
            .ReturnsAsync(canManage);
    }

    private void SetupAllDutyGrantPermissions()
    {
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignHakamDuties"))
            .ReturnsAsync(true);
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignKatzinDuties"))
            .ReturnsAsync(true);
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(CurrentUserId, "AssignHakamDuties", It.IsAny<int>()))
            .ReturnsAsync(true);
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(CurrentUserId, "AssignKatzinDuties", It.IsAny<int>()))
            .ReturnsAsync(true);
    }

    private async Task<AppUser> SeedAssigneeAsync(int id = 1, bool isActive = true, MilitaryRank rank = MilitaryRank.Samal)
    {
        var assignee = new AppUser
        {
            Id = id, Email = $"assignee{id}@test.com", DisplayName = $"Assignee {id}",
            CompanyId = CurrentUserCompanyId, IsActive = isActive, Role = UserRole.Employee, Rank = rank
        };
        _db.Users.Add(assignee);
        await _db.SaveChangesAsync();
        return assignee;
    }

    // --- CreateOnDutyAsync ---

    [Fact]
    public async Task CreateOnDutyAsync_HappyPath_CreatesHakamAssignment()
    {
        SetupHakamGrantPermissions();
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        var (success, message, onDuty, _, _) = await _service.CreateOnDutyAsync(
            assigneeId: 1, date: date, type: OnDutyType.Hakam, notes: "Test notes");

        success.Should().BeTrue();
        message.Should().Contain("created successfully");
        onDuty.Should().NotBeNull();
        onDuty!.UserId.Should().Be(1);
        onDuty.Date.Should().Be(date);
        onDuty.Type.Should().Be(OnDutyType.Hakam);
        onDuty.Notes.Should().Be("Test notes");
        onDuty.CreatedBy.Should().Be(CurrentUserId);
        onDuty.CanceledAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateOnDutyAsync_DuplicateSameUserDateType_ReturnsOverrideableWarning()
    {
        // Severity policy (2026-05-03): same-resource duplicates are overrideable warnings
        // — no override token supplied means the call returns BUSY_OVERRIDE_REQUIRED.
        SetupHakamGrantPermissions();
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam,
            CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message, _, validation, overrideToken) = await _service.CreateOnDutyAsync(
            assigneeId: 1, date: date, type: OnDutyType.Hakam);

        success.Should().BeFalse();
        message.Should().Be("BUSY_OVERRIDE_REQUIRED");
        validation.Should().NotBeNull();
        validation!.Warnings.Should().Contain(w => w.Key == "DUPLICATE_ONDUTY");
        overrideToken.Should().NotBeNullOrEmpty("a token is issued so the caller can retry with override");
    }

    [Fact]
    public async Task CreateOnDutyAsync_SameUserDifferentType_SucceedsWithForceAssign()
    {
        // Per the 2026-05-03 severity policy, ANY same-day on-duty (even of a different type)
        // emits an overrideable ONDUTY_CONFLICT warning. The cross-type assignment is still
        // allowed when the caller forces (forceAssign: true) — equivalent to the operator
        // explicitly acknowledging the conflict in the UI.
        SetupAllDutyGrantPermissions();
        await SeedAssigneeAsync(id: 1, rank: MilitaryRank.Seren); // Officer rank for Lead
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam,
            CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, _, onDuty, _, _) = await _service.CreateOnDutyAsync(
            assigneeId: 1, date: date, type: OnDutyType.Lead, forceAssign: true);

        success.Should().BeTrue();
        onDuty.Should().NotBeNull();
        onDuty!.Type.Should().Be(OnDutyType.Lead);
    }

    [Fact]
    public async Task CreateOnDutyAsync_CanceledExistingDoesNotBlock_SameUserDateType()
    {
        SetupHakamGrantPermissions();
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        // Canceled on-duty should not block new creation
        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam,
            CreatedBy = CurrentUserId, CanceledAt = DateTime.UtcNow, CanceledBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, _, onDuty, _, _) = await _service.CreateOnDutyAsync(
            assigneeId: 1, date: date, type: OnDutyType.Hakam);

        success.Should().BeTrue();
        onDuty.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOnDutyAsync_InactiveAssignee_ReturnsHardError()
    {
        SetupHakamGrantPermissions();
        await SeedAssigneeAsync(id: 1, isActive: false);

        var (success, message, _, validation, _) = await _service.CreateOnDutyAsync(
            assigneeId: 1, date: new DateOnly(2026, 3, 15), type: OnDutyType.Hakam);

        success.Should().BeFalse();
        message.Should().Be("USER_INACTIVE");
        validation.Should().NotBeNull();
        validation!.Errors.Should().Contain(e => e.Key == "USER_INACTIVE");
    }

    [Fact]
    public async Task CreateOnDutyAsync_AssigneeNotFound_ReturnsHardError()
    {
        SetupHakamGrantPermissions();

        var (success, message, _, validation, _) = await _service.CreateOnDutyAsync(
            assigneeId: 999, date: new DateOnly(2026, 3, 15), type: OnDutyType.Hakam);

        success.Should().BeFalse();
        message.Should().Be("USER_NOT_FOUND");
        validation.Should().NotBeNull();
        validation!.Errors.Should().Contain(e => e.Key == "USER_NOT_FOUND");
    }

    [Fact]
    public async Task CreateOnDutyAsync_WithoutGrant_ReturnsPermissionError()
    {
        SetupHakamGrantPermissions(canManage: false);
        await SeedAssigneeAsync(id: 1);

        var (success, message, _, _, _) = await _service.CreateOnDutyAsync(
            assigneeId: 1, date: new DateOnly(2026, 3, 15), type: OnDutyType.Hakam);

        success.Should().BeFalse();
        message.Should().Contain("permission");
    }

    [Fact]
    public async Task CreateOnDutyAsync_TrimNotes()
    {
        SetupHakamGrantPermissions();
        await SeedAssigneeAsync(id: 1);

        var (_, _, onDuty, _, _) = await _service.CreateOnDutyAsync(
            assigneeId: 1, date: new DateOnly(2026, 3, 15), type: OnDutyType.Hakam,
            notes: "  some notes  ");

        onDuty.Should().NotBeNull();
        onDuty!.Notes.Should().Be("some notes");
    }

    // --- CancelOnDutyAsync ---

    [Fact]
    public async Task CancelOnDutyAsync_HappyPath_SetsCanceledAt()
    {
        SetupAllDutyGrantPermissions();
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.CancelOnDutyAsync(onDutyId: 1, reason: "Reassigned");

        success.Should().BeTrue();
        message.Should().Contain("canceled successfully");

        var onDuty = await _db.OnDuties.FindAsync(1);
        onDuty!.CanceledAt.Should().NotBeNull();
        onDuty.CanceledBy.Should().Be(CurrentUserId);
    }

    [Fact]
    public async Task CancelOnDutyAsync_AlreadyCanceled_ReturnsError()
    {
        SetupAllDutyGrantPermissions();
        await SeedAssigneeAsync(id: 1);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = new DateOnly(2026, 3, 15), Type = OnDutyType.Hakam,
            CreatedBy = CurrentUserId, CanceledAt = DateTime.UtcNow, CanceledBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.CancelOnDutyAsync(onDutyId: 1);

        success.Should().BeFalse();
        message.Should().Contain("already canceled");
    }

    [Fact]
    public async Task CancelOnDutyAsync_NotFound_ReturnsError()
    {
        SetupAllDutyGrantPermissions();

        var (success, message) = await _service.CancelOnDutyAsync(onDutyId: 999);

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    // --- GetOnDutiesAsync ---

    [Fact]
    public async Task GetOnDutiesAsync_ReturnsActiveOnDutiesInDateRange()
    {
        await SeedAssigneeAsync(id: 1);
        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 5);
        var dateOutside = new DateOnly(2026, 4, 1);

        _db.OnDuties.AddRange(
            new OnDuty { Id = 1, UserId = 1, Date = date1, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId },
            new OnDuty { Id = 2, UserId = 1, Date = date2, Type = OnDutyType.Lead, CreatedBy = CurrentUserId },
            new OnDuty { Id = 3, UserId = 1, Date = dateOutside, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetOnDutiesAsync(startDate: date1, endDate: date2);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(o => o.Date >= date1 && o.Date <= date2);
    }

    [Fact]
    public async Task GetOnDutiesAsync_ExcludesCanceled_ByDefault()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.AddRange(
            new OnDuty { Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId },
            new OnDuty
            {
                Id = 2, UserId = 1, Date = date, Type = OnDutyType.Lead, CreatedBy = CurrentUserId,
                CanceledAt = DateTime.UtcNow, CanceledBy = CurrentUserId
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetOnDutiesAsync(startDate: date, endDate: date);

        result.Should().HaveCount(1);
        result.First().Type.Should().Be(OnDutyType.Hakam);
    }

    [Fact]
    public async Task GetOnDutiesAsync_IncludesCanceled_WhenFlagIsTrue()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.AddRange(
            new OnDuty { Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId },
            new OnDuty
            {
                Id = 2, UserId = 1, Date = date, Type = OnDutyType.Lead, CreatedBy = CurrentUserId,
                CanceledAt = DateTime.UtcNow, CanceledBy = CurrentUserId
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetOnDutiesAsync(startDate: date, endDate: date, includeCanceled: true);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetOnDutiesAsync_FiltersByType()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.AddRange(
            new OnDuty { Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId },
            new OnDuty { Id = 2, UserId = 1, Date = date, Type = OnDutyType.Lead, CreatedBy = CurrentUserId }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetOnDutiesAsync(type: OnDutyType.Lead);

        result.Should().HaveCount(1);
        result.First().Type.Should().Be(OnDutyType.Lead);
    }

    [Fact]
    public async Task GetOnDutiesAsync_FiltersByUserId()
    {
        await SeedAssigneeAsync(id: 1);
        await SeedAssigneeAsync(id: 2);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.AddRange(
            new OnDuty { Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId },
            new OnDuty { Id = 2, UserId = 2, Date = date, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetOnDutiesAsync(userId: 1);

        result.Should().HaveCount(1);
        result.First().UserId.Should().Be(1);
    }

    // --- GetOnDutyByIdAsync ---

    [Fact]
    public async Task GetOnDutyByIdAsync_ReturnsOnDuty_WhenExists()
    {
        await SeedAssigneeAsync(id: 1);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = new DateOnly(2026, 3, 15),
            Type = OnDutyType.Hakam, CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOnDutyByIdAsync(1);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(1);
        result.Type.Should().Be(OnDutyType.Hakam);
    }

    [Fact]
    public async Task GetOnDutyByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetOnDutyByIdAsync(999);

        result.Should().BeNull();
    }

    // --- HasActiveOnDutyOnDateAsync ---

    [Fact]
    public async Task HasActiveOnDutyOnDateAsync_ReturnsTrue_WhenActiveExists()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var result = await _service.HasActiveOnDutyOnDateAsync(userId: 1, date: date, type: OnDutyType.Hakam);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasActiveOnDutyOnDateAsync_ReturnsFalse_WhenCanceled()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam,
            CreatedBy = CurrentUserId, CanceledAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _service.HasActiveOnDutyOnDateAsync(userId: 1, date: date, type: OnDutyType.Hakam);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasActiveOnDutyOnDateAsync_ReturnsFalse_WhenDifferentType()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date, Type = OnDutyType.Hakam, CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var result = await _service.HasActiveOnDutyOnDateAsync(userId: 1, date: date, type: OnDutyType.Lead);

        result.Should().BeFalse();
    }

    // --- IsValidDutyTypeAsync ---

    [Fact]
    public async Task IsValidDutyTypeAsync_ReturnsTrue_ForBuiltInTypes()
    {
        var hakamResult = await _service.IsValidDutyTypeAsync((int)OnDutyType.Hakam);
        var leadResult = await _service.IsValidDutyTypeAsync((int)OnDutyType.Lead);

        hakamResult.Should().BeTrue();
        leadResult.Should().BeTrue();
    }

    [Fact]
    public async Task IsValidDutyTypeAsync_ReturnsFalse_ForUnknownType()
    {
        var result = await _service.IsValidDutyTypeAsync(9999);

        result.Should().BeFalse();
    }

    // --- RequiresOfficerForDutyTypeAsync ---

    [Fact]
    public async Task RequiresOfficerForDutyTypeAsync_ReturnsTrue_ForLeadType()
    {
        var result = await _service.RequiresOfficerForDutyTypeAsync(OnDutyType.Lead);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RequiresOfficerForDutyTypeAsync_ReturnsFalse_ForHakamType()
    {
        var result = await _service.RequiresOfficerForDutyTypeAsync(OnDutyType.Hakam);

        result.Should().BeFalse();
    }

    // --- CanUserManageOnDutyAsync ---

    [Fact]
    public async Task CanUserManageOnDutyAsync_ReturnsTrue_WhenHasHakamGrant()
    {
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignHakamDuties")).ReturnsAsync(true);
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignKatzinDuties")).ReturnsAsync(false);

        var result = await _service.CanUserManageOnDutyAsync(CurrentUserId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanUserManageOnDutyAsync_ReturnsTrue_WhenHasKatzinGrant()
    {
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignHakamDuties")).ReturnsAsync(false);
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignKatzinDuties")).ReturnsAsync(true);

        var result = await _service.CanUserManageOnDutyAsync(CurrentUserId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanUserManageOnDutyAsync_ReturnsFalse_WhenNoGrants()
    {
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignHakamDuties")).ReturnsAsync(false);
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignKatzinDuties")).ReturnsAsync(false);

        var result = await _service.CanUserManageOnDutyAsync(CurrentUserId);

        result.Should().BeFalse();
    }
}
