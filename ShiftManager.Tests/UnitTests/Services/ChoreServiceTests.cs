using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Tests.UnitTests.Services;

public class ChoreServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly ChoreService _service;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly Mock<ICompanyCacheService> _companyCacheMock;

    // Simulated current user ID for auth context
    private const int CurrentUserId = 100;
    private const int CurrentUserCompanyId = 1;

    public ChoreServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, CurrentUserId.ToString()),
            new Claim("CompanyId", CurrentUserCompanyId.ToString())
        }, "TestAuth"));
        httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _grantServiceMock = new Mock<IGrantService>();
        _companyCacheMock = new Mock<ICompanyCacheService>();
        var directorServiceMock = new Mock<IDirectorService>();
        var loggerMock = Mock.Of<ILogger<ChoreService>>();

        _service = new ChoreService(
            _db,
            Mock.Of<ITenantResolver>(),
            httpContextAccessorMock.Object,
            directorServiceMock.Object,
            _grantServiceMock.Object,
            loggerMock,
            _companyCacheMock.Object,
            BusyServiceMockFactory.Real(_db));

        // Seed the current user
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

    private void SetupGrantPermissions(bool canManage = true)
    {
        _grantServiceMock.Setup(g => g.HasGrantAsync(CurrentUserId, "AssignChores"))
            .ReturnsAsync(canManage);
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(CurrentUserId, "AssignChores", It.IsAny<int>()))
            .ReturnsAsync(canManage);
    }

    private async Task<AppUser> SeedAssigneeAsync(int id = 1, UserRole role = UserRole.Employee)
    {
        var assignee = new AppUser
        {
            Id = id, Email = $"assignee{id}@test.com", DisplayName = $"Assignee {id}",
            CompanyId = CurrentUserCompanyId, IsActive = true, Role = role
        };
        _db.Users.Add(assignee);

        var company = await _db.Companies.FindAsync(CurrentUserCompanyId);
        if (company == null)
        {
            _db.Companies.Add(new Company { Id = CurrentUserCompanyId, MoleculeId = 1, Name = "TestCo", DisplayName = "Test Company" });
        }

        await _db.SaveChangesAsync();

        _companyCacheMock.Setup(c => c.GetCompanyAsync(CurrentUserCompanyId))
            .ReturnsAsync(new Company { Id = CurrentUserCompanyId, MoleculeId = 1, Name = "TestCo", DisplayName = "Test Company" });

        return assignee;
    }

    // --- CreateChoreAsync ---

    [Fact]
    public async Task CreateChoreAsync_HappyPath_CreatesChoreWithCorrectFields()
    {
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        var (success, message, chore, _, _) = await _service.CreateChoreAsync(
            assigneeId: 1, date: date, title: "Guard Duty", notes: "North gate");

        success.Should().BeTrue();
        message.Should().Be("Chore created successfully.");
        chore.Should().NotBeNull();
        chore!.UserId.Should().Be(1);
        chore.Date.Should().Be(date);
        chore.Title.Should().Be("Guard Duty");
        chore.Notes.Should().Be("North gate");
        chore.CreatedBy.Should().Be(CurrentUserId);
        chore.CanceledAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateChoreAsync_EmptyTitle_ReturnsValidationError()
    {
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);

        var (success, message, chore, _, _) = await _service.CreateChoreAsync(
            assigneeId: 1, date: new DateOnly(2026, 3, 15), title: "  ");

        success.Should().BeFalse();
        message.Should().Contain("title is required");
        chore.Should().BeNull();
    }

    [Fact]
    public async Task CreateChoreAsync_AssigneeNotFound_ReturnsError()
    {
        SetupGrantPermissions();

        var (success, message, chore, _, _) = await _service.CreateChoreAsync(
            assigneeId: 999, date: new DateOnly(2026, 3, 15), title: "Guard Duty");

        success.Should().BeFalse();
        message.Should().Contain("Assignee not found");
        chore.Should().BeNull();
    }

    [Fact]
    public async Task CreateChoreAsync_WithoutGrant_ReturnsPermissionError()
    {
        SetupGrantPermissions(canManage: false);
        await SeedAssigneeAsync(id: 1);

        var (success, message, chore, _, _) = await _service.CreateChoreAsync(
            assigneeId: 1, date: new DateOnly(2026, 3, 15), title: "Guard Duty");

        success.Should().BeFalse();
        message.Should().Contain("permission");
        chore.Should().BeNull();
    }

    [Fact]
    public async Task CreateChoreAsync_DuplicateChoreOnSameDate_ReturnsOverrideableWarning()
    {
        // Severity policy (2026-05-03): a same-day duplicate chore is now an overrideable
        // warning, not a hard error. The service returns BUSY_OVERRIDE_REQUIRED + token.
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = date, Title = "Existing Chore", CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message, _, validation, overrideToken) = await _service.CreateChoreAsync(
            assigneeId: 1, date: date, title: "Second Chore");

        success.Should().BeFalse();
        message.Should().Be("BUSY_OVERRIDE_REQUIRED");
        validation.Should().NotBeNull();
        validation!.Warnings.Should().Contain(w => w.Key == "CHORE_CONFLICT");
        overrideToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateChoreAsync_DirectorAssignee_SucceedsWhenGrantPresent()
    {
        // Director assignees are no longer blocked at the service layer; authorization is
        // grant-based, so any role can be a recipient when the caller has AssignChores for
        // the assignee's company.
        SetupGrantPermissions();
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(CurrentUserId, "AssignChores", It.IsAny<int>()))
            .ReturnsAsync(true);
        await SeedAssigneeAsync(id: 1, role: UserRole.Director);

        var (success, message, chore, _, _) = await _service.CreateChoreAsync(
            assigneeId: 1, date: new DateOnly(2026, 3, 15), title: "Guard Duty");

        success.Should().BeTrue();
        message.Should().Contain("created");
        chore.Should().NotBeNull();
        chore!.UserId.Should().Be(1);
    }

    [Fact]
    public async Task CreateChoreAsync_TrimsTitleAndNotes()
    {
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);

        var (_, _, chore, _, _) = await _service.CreateChoreAsync(
            assigneeId: 1, date: new DateOnly(2026, 3, 15),
            title: "  Guard Duty  ", notes: "  North gate  ");

        chore.Should().NotBeNull();
        chore!.Title.Should().Be("Guard Duty");
        chore.Notes.Should().Be("North gate");
    }

    // --- CancelChoreAsync ---

    [Fact]
    public async Task CancelChoreAsync_HappyPath_SetsCanceledAt()
    {
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = date, Title = "Guard Duty", CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.CancelChoreAsync(choreId: 1, reason: "No longer needed");

        success.Should().BeTrue();
        message.Should().Contain("canceled successfully");

        var chore = await _db.Chores.FindAsync(1);
        chore!.CanceledAt.Should().NotBeNull();
        chore.CanceledBy.Should().Be(CurrentUserId);
    }

    [Fact]
    public async Task CancelChoreAsync_AlreadyCanceled_ReturnsError()
    {
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = new DateOnly(2026, 3, 15), Title = "Guard Duty",
            CreatedBy = CurrentUserId, CanceledAt = DateTime.UtcNow, CanceledBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.CancelChoreAsync(choreId: 1);

        success.Should().BeFalse();
        message.Should().Contain("already canceled");
    }

    [Fact]
    public async Task CancelChoreAsync_ChoreNotFound_ReturnsError()
    {
        SetupGrantPermissions();

        var (success, message) = await _service.CancelChoreAsync(choreId: 999);

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    // --- GetChoresAsync ---

    [Fact]
    public async Task GetChoresAsync_ReturnsActiveChoresInDateRange()
    {
        await SeedAssigneeAsync(id: 1);
        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 5);
        var dateOutside = new DateOnly(2026, 4, 1);

        _db.Chores.AddRange(
            new Chore { Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date1, Title = "Chore1", CreatedBy = CurrentUserId },
            new Chore { Id = 2, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date2, Title = "Chore2", CreatedBy = CurrentUserId },
            new Chore { Id = 3, CompanyId = CurrentUserCompanyId, UserId = 1, Date = dateOutside, Title = "Chore3", CreatedBy = CurrentUserId }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetChoresAsync(startDate: date1, endDate: date2);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(c => c.Date >= date1 && c.Date <= date2);
    }

    [Fact]
    public async Task GetChoresAsync_ExcludesCanceledChores_ByDefault()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.Chores.AddRange(
            new Chore { Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date, Title = "Active", CreatedBy = CurrentUserId },
            new Chore
            {
                Id = 2, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date, Title = "Canceled",
                CreatedBy = CurrentUserId, CanceledAt = DateTime.UtcNow, CanceledBy = CurrentUserId
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetChoresAsync(startDate: date, endDate: date);

        result.Should().HaveCount(1);
        result.First().Title.Should().Be("Active");
    }

    [Fact]
    public async Task GetChoresAsync_IncludesCanceled_WhenFlagIsTrue()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.Chores.AddRange(
            new Chore { Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date, Title = "Active", CreatedBy = CurrentUserId },
            new Chore
            {
                Id = 2, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date, Title = "Canceled",
                CreatedBy = CurrentUserId, CanceledAt = DateTime.UtcNow, CanceledBy = CurrentUserId
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetChoresAsync(startDate: date, endDate: date, includeCanceled: true);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetChoresAsync_FiltersByUserId()
    {
        await SeedAssigneeAsync(id: 1);
        await SeedAssigneeAsync(id: 2);
        var date = new DateOnly(2026, 3, 15);

        _db.Chores.AddRange(
            new Chore { Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date, Title = "User1 Chore", CreatedBy = CurrentUserId },
            new Chore { Id = 2, CompanyId = CurrentUserCompanyId, UserId = 2, Date = date, Title = "User2 Chore", CreatedBy = CurrentUserId }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetChoresAsync(userId: 1);

        result.Should().HaveCount(1);
        result.First().UserId.Should().Be(1);
    }

    [Fact]
    public async Task GetChoresAsync_FiltersByMoleculeId()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.Chores.AddRange(
            new Chore { Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date, Title = "Mol1", CreatedBy = CurrentUserId, MoleculeId = 1 },
            new Chore { Id = 2, CompanyId = CurrentUserCompanyId, UserId = 1, Date = date, Title = "Mol2", CreatedBy = CurrentUserId, MoleculeId = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetChoresAsync(moleculeId: 1);

        result.Should().HaveCount(1);
        result.First().MoleculeId.Should().Be(1);
    }

    // --- GetChoreByIdAsync ---

    [Fact]
    public async Task GetChoreByIdAsync_ReturnsChore_WhenExists()
    {
        await SeedAssigneeAsync(id: 1);
        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = new DateOnly(2026, 3, 15), Title = "Test Chore", CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetChoreByIdAsync(1);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Chore");
    }

    [Fact]
    public async Task GetChoreByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetChoreByIdAsync(999);

        result.Should().BeNull();
    }

    // --- HasActiveChoreOnDateAsync ---

    [Fact]
    public async Task HasActiveChoreOnDateAsync_ReturnsTrue_WhenActiveChoreExists()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = date, Title = "Test", CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var result = await _service.HasActiveChoreOnDateAsync(userId: 1, date: date);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasActiveChoreOnDateAsync_ReturnsFalse_WhenChoreIsCanceled()
    {
        await SeedAssigneeAsync(id: 1);
        var date = new DateOnly(2026, 3, 15);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = date, Title = "Test", CreatedBy = CurrentUserId,
            CanceledAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _service.HasActiveChoreOnDateAsync(userId: 1, date: date);

        result.Should().BeFalse();
    }

    // --- RestoreChoreAsync ---

    [Fact]
    public async Task RestoreChoreAsync_HappyPath_ClearsCancel()
    {
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = new DateOnly(2026, 3, 15), Title = "Canceled Chore",
            CreatedBy = CurrentUserId,
            CanceledAt = DateTime.UtcNow, CanceledBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.RestoreChoreAsync(choreId: 1);

        success.Should().BeTrue();
        message.Should().Contain("restored");

        var chore = await _db.Chores.FindAsync(1);
        chore!.CanceledAt.Should().BeNull();
        chore.CanceledBy.Should().BeNull();
    }

    [Fact]
    public async Task RestoreChoreAsync_NotCanceled_ReturnsError()
    {
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = new DateOnly(2026, 3, 15), Title = "Active Chore", CreatedBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.RestoreChoreAsync(choreId: 1);

        success.Should().BeFalse();
        message.Should().Contain("not canceled");
    }

    [Fact]
    public async Task RestoreChoreAsync_ExpiredUndoWindow_ReturnsError()
    {
        SetupGrantPermissions();
        await SeedAssigneeAsync(id: 1);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = CurrentUserCompanyId, UserId = 1,
            Date = new DateOnly(2026, 3, 15), Title = "Chore",
            CreatedBy = CurrentUserId,
            CanceledAt = DateTime.UtcNow.AddMinutes(-5), CanceledBy = CurrentUserId
        });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.RestoreChoreAsync(choreId: 1);

        success.Should().BeFalse();
        message.Should().Contain("expired");
    }
}
