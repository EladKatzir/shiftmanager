using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.My;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Verifies that the account-type gate in Pages/My/Requests.cshtml.cs blocks
/// Mil and GroupUser accounts from submitting requests (OnPostTimeOffAsync returns Forbid
/// and no TimeOffRequest row is created), while Standard accounts can still succeed.
///
/// Guards tested:
///   - OnPostTimeOffAsync: Forbid for Mil, Forbid for GroupUser, success for Standard.
/// </summary>
public sealed class RequestsLockoutTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    // Mocked dependencies for RequestsModel
    private readonly Mock<IVacationApprovalService> _vacationApprovalService = new();
    private readonly Mock<IFeatureFlagService> _featureFlagService = new();
    private readonly Mock<ICompanyLocalizationService> _companyLocalizationService = new();
    private readonly Mock<ITenantResolver> _tenantResolver = new();
    private readonly Mock<ILeaveFanoutService> _leaveFanoutService = new();
    private readonly Mock<IStringLocalizer<SharedResources>> _localizer = new();

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Default localizer: return key as value so asserts can match on key names.
        _localizer
            .Setup(l => l[It.IsAny<string>()])
            .Returns<string>(k => new LocalizedString(k, k));

        // Feature flag disabled by default — prevents approval side-effects in success test.
        // Use It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>() to match optional params
        // that Moq's expression-tree matcher requires to be explicit.
        _featureFlagService
            .Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(false);

        // Fan-out returns no clones (single-company, no fan-out needed).
        _leaveFanoutService
            .Setup(l => l.FanOutAsync(It.IsAny<TimeOffRequest>(), It.IsAny<int>()))
            .ReturnsAsync(((Guid?)null, (IReadOnlyList<TimeOffRequest>)new List<TimeOffRequest>()));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>Seeds a company hierarchy and a user with the given AccountType. Returns the user id.</summary>
    private async Task<int> SeedUserAsync(AccountType accountType)
    {
        // Each test invocation gets unique ids to avoid conflicts between Mil, GroupUser, Standard seeds.
        var projectId = (int)accountType + 100;
        var areaId = (int)accountType + 200;
        var moleculeId = (int)accountType + 300;
        var companyId = (int)accountType + 400;
        var userId = (int)accountType + 500;

        if (!_db.Projects.Any(p => p.Id == projectId))
        {
            _db.Projects.Add(new Project { Id = projectId, Name = $"P{projectId}", DisplayName = $"P{projectId}" });
            _db.Areas.Add(new Area { Id = areaId, ProjectId = projectId, Name = $"A{areaId}", DisplayName = $"A{areaId}" });
            _db.Molecules.Add(new Molecule { Id = moleculeId, AreaId = areaId, Name = $"M{moleculeId}", Type = MoleculeType.Workforce });
            _db.Companies.Add(new Company { Id = companyId, MoleculeId = moleculeId, Name = $"C{companyId}", DisplayName = $"C{companyId}" });
            _db.Users.Add(new AppUser
            {
                Id = userId,
                Email = $"{accountType.ToString().ToLowerInvariant()}@test.com",
                DisplayName = accountType.ToString(),
                CompanyId = companyId,
                IsActive = true,
                AccountType = accountType,
                Role = UserRole.Employee,
                PasswordHash = Array.Empty<byte>(),
                PasswordSalt = Array.Empty<byte>()
            });
            await _db.SaveChangesAsync();
        }

        return userId;
    }

    private RequestsModel BuildModel(int userId)
    {
        var model = new RequestsModel(
            _localizer.Object,
            _db,
            NullLogger<RequestsModel>.Instance,
            _vacationApprovalService.Object,
            _featureFlagService.Object,
            _companyLocalizationService.Object,
            _tenantResolver.Object,
            _leaveFanoutService.Object);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        // Bind a minimal valid TimeOffRequest form to avoid ModelState.IsValid = false.
        var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        model.TimeOffRequest = new RequestsModel.TimeOffRequestForm
        {
            StartDate = tomorrow,
            EndDate = tomorrow,
            Type = TimeOffType.Vacation,
            Reason = "Test reason"
        };

        return model;
    }

    // ── Mil account is blocked ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnPostTimeOffAsync_MilAccount_ReturnsForbid_AndNoRowCreated()
    {
        var userId = await SeedUserAsync(AccountType.Mil);
        var model = BuildModel(userId);

        var result = await model.OnPostTimeOffAsync();

        result.Should().BeOfType<ForbidResult>("Mil accounts must be blocked by the account-type gate");
        (await _db.TimeOffRequests.IgnoreQueryFilters().AnyAsync()).Should().BeFalse(
            "the gate must fire before any DB write");
    }

    // ── GroupUser account is blocked ───────────────────────────────────────────────────

    [Fact]
    public async Task OnPostTimeOffAsync_GroupUser_ReturnsForbid_AndNoRowCreated()
    {
        var userId = await SeedUserAsync(AccountType.GroupUser);
        var model = BuildModel(userId);

        var result = await model.OnPostTimeOffAsync();

        result.Should().BeOfType<ForbidResult>("GroupUser accounts must be blocked by the account-type gate");
        (await _db.TimeOffRequests.IgnoreQueryFilters().AnyAsync()).Should().BeFalse(
            "the gate must fire before any DB write");
    }

    // ── Standard account succeeds ──────────────────────────────────────────────────────

    [Fact]
    public async Task OnPostTimeOffAsync_StandardAccount_Succeeds_AndRowIsCreated()
    {
        var userId = await SeedUserAsync(AccountType.Standard);
        var model = BuildModel(userId);

        var result = await model.OnPostTimeOffAsync();

        // A redirect (success) — not a Forbid and not a Page().
        result.Should().NotBeOfType<ForbidResult>("Standard accounts must pass the gate");
        (await _db.TimeOffRequests.IgnoreQueryFilters().CountAsync()).Should().Be(1,
            "exactly one TimeOffRequest row should be created for the Standard user");
        var row = await _db.TimeOffRequests.IgnoreQueryFilters().SingleAsync();
        row.UserId.Should().Be(userId);
    }
}
