using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Pages.Calendar;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Guards the switcher-awareness of <see cref="TableModel.IsCallerInMoleculeAsync"/> — the shared
/// molecule-access gate used by the roster/availability/conflicts/delete-instance/update-metadata
/// handlers. It must key off the caller's ACTIVE (switcher-aware) company via
/// <see cref="ITenantResolver"/>, NOT the raw home-company claim (<see cref="ICompanyContext"/>).
/// Otherwise a switched Owner (home in one molecule, viewing another) is wrongly 403'd.
/// </summary>
[Collection("MasterTests")]
public sealed class CalendarTableMoleculeGateTests
{
    private readonly MasterTestFixture _fx;
    public CalendarTableMoleculeGateTests(MasterTestFixture fx) => _fx = fx;

    [Fact]
    public async Task IsCallerInMolecule_SwitchedOwner_ActiveCompanyMolecule_ReturnsTrue()
    {
        // Owner whose HOME company is SystemAdmins (in the "System" molecule) has switched (owner-
        // selected) to Tzafona (in the "Oren" molecule). The gate for Oren must follow the SWITCH.
        var model = MakeModel(
            homeCompanyId: _fx.CompanyByName["SystemAdmins"].Id,
            ownerSelectedCompanyId: _fx.CompanyByName["Tzafona"].Id);

        var result = await model.IsCallerInMoleculeAsync(_fx.MoleculeByName["Oren"].Id);

        result.Should().BeTrue(
            "the switched Owner's active company (Tzafona) is in the Oren molecule, so molecule-mode " +
            "access must be granted — the gate must use the switcher-aware tenant, not the home claim");
    }

    [Fact]
    public async Task IsCallerInMolecule_SwitchedOwner_HomeMolecule_ReturnsFalse()
    {
        // Same switched Owner. Asking about their HOME molecule ("System") must now be FALSE,
        // because their ACTIVE company (Tzafona) is not in the System molecule. This proves the
        // gate no longer keys off the home-company claim (which would have returned true).
        var model = MakeModel(
            homeCompanyId: _fx.CompanyByName["SystemAdmins"].Id,
            ownerSelectedCompanyId: _fx.CompanyByName["Tzafona"].Id);

        var result = await model.IsCallerInMoleculeAsync(_fx.MoleculeByName["System"].Id);

        result.Should().BeFalse(
            "the active company (Tzafona) is not in the System molecule; keying off the home claim " +
            "(SystemAdmins, which IS in System) would wrongly return true");
    }

    [Fact]
    public async Task IsCallerInMolecule_NonSwitchedEmployee_OwnMolecule_ReturnsTrue()
    {
        // Regression guard for the common case: a plain employee with no switch. tenant == home,
        // so behaviour is unchanged — access to their own molecule stays granted.
        var model = MakeModel(
            homeCompanyId: _fx.CompanyByName["Tzafona"].Id,
            ownerSelectedCompanyId: null,
            role: "Employee");

        var result = await model.IsCallerInMoleculeAsync(_fx.MoleculeByName["Oren"].Id);

        result.Should().BeTrue("an employee of Tzafona (Oren molecule) can access their own molecule");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────

    private TableModel MakeModel(int homeCompanyId, int? ownerSelectedCompanyId, string role = "Owner")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Role, role),
            new("CompanyId", homeCompanyId.ToString())
        };
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        var services = new ServiceCollection();
        services.AddSingleton<IOwnerCompanySelectorService>(new OwnerSelectorStub(ownerSelectedCompanyId));
        ctx.RequestServices = services.BuildServiceProvider();

        var accessor = new HttpContextAccessorStub(ctx);
        var companyContext = new CompanyContext(accessor);                               // reads CompanyId claim => home
        var tenantResolver = new TenantResolver(accessor, NullLogger<TenantResolver>.Instance); // owner-selected => switched

        // Only _db, _companyContext and _tenantResolver are exercised by IsCallerInMoleculeAsync;
        // the remaining dependencies are irrelevant to this method and left null.
        return new TableModel(
            _fx.Db,             // db
            companyContext,     // companyContext
            null!,              // logger
            null!,              // busyUserService
            null!,              // shiftTypeCache
            null!,              // programService
            null!,              // assignmentService
            null!,              // calendarNotification
            null!,              // concurrencyService
            null!,              // grantService
            null!,              // jobTypeService
            null!,              // companyLocalizationService
            tenantResolver,     // tenantResolver
            null!,              // localizer
            null!,              // auditLogService
            null!,              // draftService
            null!);             // remediation
    }

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
