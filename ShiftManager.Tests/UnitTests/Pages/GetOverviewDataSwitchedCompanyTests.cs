using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Api.Calendar;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Regression tests for the GetOverviewData shadow-refresh endpoint fixes (commit 35eaf1d):
///  1. The caller's own record is looked up with IgnoreQueryFilters, so a switched Owner (whose home
///     company differs from the active tenant) is not wrongly 401'd "User not found".
///  2. When the companyId query param is omitted, the fallback uses the switcher-aware tenant, not
///     the home-company claim, so a switched Owner gets the ACTIVE company's data.
/// </summary>
[Collection("MasterTests")]
public sealed class GetOverviewDataSwitchedCompanyTests
{
    private readonly MasterTestFixture _fx;
    public GetOverviewDataSwitchedCompanyTests(MasterTestFixture fx) => _fx = fx;

    [Fact]
    public async Task SwitchedOwner_SelfLookupNotTenantFiltered_DoesNotReturn401()
    {
        // Owner's home = SystemAdmins; active (owner-selected) = Tzafona. The AppUser query filter is
        // scoped to the active tenant (Tzafona), which excludes the Owner's own record — a naive
        // FindAsync would return null and 401. The IgnoreQueryFilters self-lookup must still find them.
        var (model, _) = MakeModel(explicitCompanyId: _fx.CompanyByName["Tzafona"].Id);

        var result = await model.OnGetAsync(
            companyId: _fx.CompanyByName["Tzafona"].Id, startDate: "2026-07-12", endDate: "2026-07-18");

        var json = result as JsonResult;
        json.Should().NotBeNull();
        json!.StatusCode.Should().NotBe(401,
            "the switched Owner's own record must be found via IgnoreQueryFilters despite the tenant filter");
        Serialize(json.Value).Should().Contain("\"success\":true");
    }

    [Fact]
    public async Task SwitchedOwner_NoCompanyIdParam_FallsBackToActiveCompany()
    {
        // Same switched Owner, but the companyId query param is omitted. The fallback must resolve to
        // the ACTIVE company (Tzafona) — so the returned users are Tzafona's, NOT the home company's.
        var (model, _) = MakeModel(explicitCompanyId: null);

        var result = await model.OnGetAsync(
            companyId: null, startDate: "2026-07-12", endDate: "2026-07-18");

        var json = (result as JsonResult)!;
        var payload = Serialize(json.Value);
        json.StatusCode.Should().NotBe(401);
        payload.Should().Contain("\"success\":true");
        payload.Should().Contain("(Tzafona)",
            "with no companyId param the endpoint must fall back to the switcher-aware active company (Tzafona)");
        payload.Should().NotContain("(SystemAdmins)",
            "it must NOT fall back to the Owner's home company (SystemAdmins)");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────

    private (GetOverviewDataModel model, ShiftManager.Data.AppDbContext db) MakeModel(int? explicitCompanyId)
    {
        var owner = _fx.UserByEmail["master.systemadmins.owner@test.com"];
        var systemAdmins = _fx.CompanyByName["SystemAdmins"];
        var tzafona = _fx.CompanyByName["Tzafona"];

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, owner.Id.ToString()),
            new(ClaimTypes.Name, owner.Email!),
            new(ClaimTypes.Role, "Owner"),
            new("CompanyId", systemAdmins.Id.ToString())   // home-company claim
        };
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        var services = new ServiceCollection();
        services.AddSingleton<IOwnerCompanySelectorService>(new OwnerSelectorStub(tzafona.Id)); // active = Tzafona
        ctx.RequestServices = services.BuildServiceProvider();

        var accessor = new HttpContextAccessorStub(ctx);
        var tenantResolver = new TenantResolver(accessor, NullLogger<TenantResolver>.Instance);
        var db = _fx.CreateDbContext(tenantResolver); // query filters active, scoped to Tzafona

        var textEntry = new Mock<ICalendarTextEntryService>();
        textEntry.Setup(s => s.GetOverviewNotesForCompanyAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new Dictionary<(int UserId, DateOnly Date), string>());
        textEntry.Setup(s => s.GetForUsersAndDateRangeWithTypeAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text, CalendarTextEntryType EntryType, int CompanyId)>>());

        var scope = new Mock<IScopeFilterService>();
        scope.Setup(s => s.ValidateScopeAccessAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()))
            .ReturnsAsync(true); // Owner has cross-company overview access

        var localizer = new Mock<IStringLocalizer<SharedResources>>();

        var model = new GetOverviewDataModel(
            textEntry.Object, tenantResolver, scope.Object, db,
            NullLogger<GetOverviewDataModel>.Instance, localizer.Object)
        {
            PageContext = new PageContext { HttpContext = ctx }
        };
        return (model, db);
    }

    private static string Serialize(object? value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
        public HttpContextAccessorStub(HttpContext ctx) => HttpContext = ctx;
    }

    private sealed class OwnerSelectorStub : IOwnerCompanySelectorService
    {
        private readonly int? _selectedCompanyId;
        public OwnerSelectorStub(int? selectedCompanyId) => _selectedCompanyId = selectedCompanyId;
        public int? GetSelectedCompanyId() => _selectedCompanyId;
        public bool IsOwner() => true;
        public Task<bool> SelectCompanyAsync(int companyId) => throw new NotSupportedException();
        public Task ClearSelectionAsync() => throw new NotSupportedException();
        public Task<string?> GetSelectedCompanyNameAsync() => throw new NotSupportedException();
        public int? GetHomeCompanyId() => throw new NotSupportedException();
    }
}
