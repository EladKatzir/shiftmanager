using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Security;

/// <summary>
/// End-to-end tenant isolation tests for the multi-company membership feature.
///
/// These tests prove that when a multi-company member switches their active company:
///   - READS  (EF query filters) follow the switch — company-20 context sees only company-20 rows.
///   - WRITES (CompanyIdInterceptor) follow the switch — new entities are stamped with company 20.
///   - A FORGED cookie for a non-member company is ignored — the resolver falls back to the
///     home company and the data effect is correct.
///   - WITHOUT a switch cookie the home company is used.
///
/// Wiring:
///   A real HttpContextAccessor is used (not a mock) because it has a settable HttpContext
///   property, letting us swap the active HTTP context (and therefore claims/cookies) between
///   the seeding phase and the assertion phase within a single test — both phases go through the
///   SAME TenantResolver instance, which is the object captured in EF query-filter closures.
///
///   Each test creates its own SqliteConnection (DataSource=:memory:;Foreign Keys=False) so
///   EF's model cache is isolated: OnModelCreating runs fresh for each test and registers the
///   query filters against that test's TenantResolver instance.
///
///   The CompanyIdInterceptor is wired via a ServiceProvider that exposes the SAME
///   ITenantResolver, matching the production DI pattern (see DistributionListServiceTests for
///   precedent).
///
/// Entity choice: Announcement — implements IBelongsToCompany, has a standard
///   CompanyId == GetCurrentTenantId() query filter in AppDbContext.OnModelCreating (line ~807),
///   and can be constructed with minimal required fields (Title, Content, CreatedBy, CreatedAt).
/// </summary>
public sealed class MultiCompanyTenantIsolationTests : IAsyncLifetime
{
    // ─── Fixed company IDs used across all tests ─────────────────────────────
    private const int HomeCompanyId = 10;     // CompanyId claim (the user's "home" company)
    private const int SwitchedCompanyId = 20; // The company the member switches to
    private const int NonMemberCompanyId = 99; // A company the user is NOT a member of

    // ─── Wired per-test in InitializeAsync ───────────────────────────────────
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private HttpContextAccessor _accessor = null!; // mutable — we swap HttpContext between test phases
    private TenantResolver _resolver = null!;

    // ─── IAsyncLifetime ──────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        // Open a fresh in-memory SQLite connection. Foreign Keys=False mirrors the FK-off fixture
        // pattern so we can seed Company rows without seeding the full Project→Area→Molecule chain.
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();

        // Mutable accessor: tests swap HttpContext to change the "current user" as seen by the resolver.
        _accessor = new HttpContextAccessor();

        // Real TenantResolver over the mutable accessor.
        _resolver = new TenantResolver(_accessor, NullLogger<TenantResolver>.Instance);

        // Wire CompanyIdInterceptor with a service-provider that exposes the SAME resolver,
        // matching the production Singleton+CreateScope() pattern in CompanyIdInterceptor.SetCompanyId.
        var sp = new ServiceCollection()
            .AddScoped<ITenantResolver>(_ => _resolver)
            .BuildServiceProvider();
        var interceptor = new CompanyIdInterceptor(
            sp,
            new ConfigurationBuilder().Build(),        // EnforceCompanyScope defaults to false
            NullLogger<CompanyIdInterceptor>.Instance);

        // Pass the resolver to AppDbContext so OnModelCreating registers query filters.
        // This MUST be the very first AppDbContext built on these options so the EF model
        // is compiled with query filters referencing _resolver (not null).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        _db = new AppDbContext(options, _resolver);
        await _db.Database.EnsureCreatedAsync();

        // Seed two lightweight Company rows so FK lookups (if enabled) would pass.
        // With FK=false they are optional, but having them makes the test data realistic.
        _db.Companies.AddRange(
            new Company { Id = HomeCompanyId,    Name = "HomeCompany",    Slug = "home",    MoleculeId = 1 },
            new Company { Id = SwitchedCompanyId, Name = "SwitchedCompany", Slug = "switched", MoleculeId = 1 });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Build an authenticated ClaimsPrincipal for a multi-company member.
    /// </summary>
    private static ClaimsPrincipal MakeMemberPrincipal(
        int companyId,
        string memberCompanyIds,
        string role = "Employee")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "42"),
            new(ClaimTypes.Name, "TestMember"),
            new(ClaimTypes.Role, role),
            new("CompanyId", companyId.ToString()),
            new("MemberCompanyIds", memberCompanyIds)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    /// <summary>
    /// Set the accessor's HttpContext to a member principal with an optional
    /// member_selected_company cookie. This is what TenantResolver.GetCurrentTenantId() reads.
    /// </summary>
    private void SetHttpContext(ClaimsPrincipal user, int? selectedCompanyCookie = null)
    {
        var ctx = new DefaultHttpContext { User = user };
        if (selectedCompanyCookie.HasValue)
        {
            // Set the cookie header so IRequestCookieCollection.TryGetValue works.
            ctx.Request.Headers["Cookie"] =
                $"{ActiveCompanySelectorService.CookieName}={selectedCompanyCookie.Value}";
        }
        _accessor.HttpContext = ctx;
    }

    /// <summary>
    /// Seed an Announcement row bypassing the active query filter.
    /// We use IgnoreQueryFilters() on a read-back check; for seeding we explicitly set
    /// CompanyId so the interceptor leaves it alone (CompanyId != 0).
    /// </summary>
    private Announcement MakeAnnouncement(int companyId, string title) => new()
    {
        CompanyId = companyId,
        Title = title,
        Content = $"Content for {title}",
        CreatedBy = 1,
        CreatedAt = DateTime.UtcNow,
        IsActive = true
    };

    // ─── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test 1 — READ follows the switch.
    ///
    /// Seed one Announcement in company 10 and one in company 20.
    /// Set up the HTTP context: CompanyId=10 claim, MemberCompanyIds=10,20,
    /// member_selected_company=20 cookie.
    ///
    /// Assert: a filtered query returns ONLY the company-20 row; the company-10 row is hidden.
    /// This proves the query filter dynamically follows the active-company switch.
    /// </summary>
    [Fact]
    public async Task ReadFollowsSwitch_FilteredQueryReturnsOnlySwitchedCompanyRows()
    {
        // ── Arrange: seed one row per company while no HTTP context is active
        // (accessor.HttpContext is null → GetCurrentTenantId() returns 0 → filter yields nothing,
        //  but writes are not filtered, and CompanyId is explicitly set on each row so the
        //  interceptor skips them).  We clear HttpContext first to guarantee this.
        _accessor.HttpContext = null;

        var rowCompany10 = MakeAnnouncement(HomeCompanyId, "Home announcement");
        var rowCompany20 = MakeAnnouncement(SwitchedCompanyId, "Switched announcement");

        // Bypass query filter for the AddRange so both rows land in the DB regardless of tenant.
        // Actually, Add/AddRange is unaffected by query filters (those are SELECT-only).
        // We add via IgnoreQueryFilters approach: just add them directly. EF writes are not filtered.
        _db.Announcements.AddRange(rowCompany10, rowCompany20);
        await _db.SaveChangesAsync();

        // ── Act: switch to company 20
        var user = MakeMemberPrincipal(
            companyId: HomeCompanyId,
            memberCompanyIds: $"{HomeCompanyId},{SwitchedCompanyId}");
        SetHttpContext(user, selectedCompanyCookie: SwitchedCompanyId);

        // Verify the resolver itself agrees on the active tenant BEFORE querying EF.
        var activeTenant = _resolver.GetCurrentTenantId();
        activeTenant.Should().Be(SwitchedCompanyId,
            "the resolver must honor the valid member_selected_company cookie");

        // Filtered query — query filter applies e.CompanyId == resolver.GetCurrentTenantId() == 20
        var filtered = await _db.Announcements.ToListAsync();

        // ── Assert
        filtered.Should().ContainSingle(
            "only the company-20 row should pass the query filter after switching to company 20");
        filtered[0].CompanyId.Should().Be(SwitchedCompanyId,
            "the returned row must belong to the switched-to company");
        filtered[0].Title.Should().Be("Switched announcement");

        // Confirm the company-10 row was NOT returned (defense-in-depth check)
        filtered.Should().NotContain(a => a.CompanyId == HomeCompanyId,
            "the company-10 row must be hidden by the query filter while tenant is company 20");
    }

    /// <summary>
    /// Test 2 — WRITE follows the switch.
    ///
    /// With the member switched to company 20, insert a new Announcement leaving
    /// CompanyId as 0.  Assert that SaveChanges stamps it with 20 (via CompanyIdInterceptor),
    /// not 10.
    /// </summary>
    [Fact]
    public async Task WriteFollowsSwitch_InterceptorStampsSwitchedCompanyId()
    {
        // ── Arrange: activate company-20 context
        var user = MakeMemberPrincipal(
            companyId: HomeCompanyId,
            memberCompanyIds: $"{HomeCompanyId},{SwitchedCompanyId}");
        SetHttpContext(user, selectedCompanyCookie: SwitchedCompanyId);

        _resolver.GetCurrentTenantId().Should().Be(SwitchedCompanyId,
            "precondition: resolver must return the switched company before the write");

        // ── Act: add a new entity with CompanyId left at 0 (the interceptor's trigger)
        var newAnnouncement = new Announcement
        {
            CompanyId = 0, // deliberately unset — interceptor must fill this in
            Title = "Interceptor-stamped announcement",
            Content = "Written while tenant is company 20",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        _db.Announcements.Add(newAnnouncement);
        await _db.SaveChangesAsync();

        // ── Assert: read back bypassing query filter so we can inspect the raw CompanyId
        var saved = await _db.Announcements
            .IgnoreQueryFilters()
            .SingleAsync(a => a.Title == "Interceptor-stamped announcement");

        saved.CompanyId.Should().Be(SwitchedCompanyId,
            "the CompanyIdInterceptor must stamp the active tenant (company 20) on a new entity with CompanyId=0 " +
            "when the member has switched to company 20");
        saved.CompanyId.Should().NotBe(HomeCompanyId,
            "the home company (10) must NOT be stamped — the interceptor follows the switched tenant");
    }

    /// <summary>
    /// Test 3 — Non-member company is NOT reachable via a forged cookie.
    ///
    /// MemberCompanyIds=10 (NOT 20) but member_selected_company=20 (forged).
    /// Assert: GetCurrentTenantId() returns 10 (resolver ignores the forged cookie),
    /// and a filtered query returns ONLY company-10 rows.
    /// </summary>
    [Fact]
    public async Task ForgedCookieForNonMemberCompany_IsIgnored_QuerySeesHomeCompanyOnly()
    {
        // ── Arrange: seed one row in each company
        _accessor.HttpContext = null;

        var rowCompany10 = MakeAnnouncement(HomeCompanyId, "Home row");
        var rowCompany20 = MakeAnnouncement(SwitchedCompanyId, "Switched row (non-member company)");
        _db.Announcements.AddRange(rowCompany10, rowCompany20);
        await _db.SaveChangesAsync();

        // ── Act: forge a cookie for company 20 but MemberCompanyIds only contains 10
        var user = MakeMemberPrincipal(
            companyId: HomeCompanyId,
            memberCompanyIds: $"{HomeCompanyId}"); // user is NOT a member of company 20
        SetHttpContext(user, selectedCompanyCookie: SwitchedCompanyId); // forge company 20

        // Resolver must ignore the forged cookie and fall through to the CompanyId claim
        var activeTenant = _resolver.GetCurrentTenantId();
        activeTenant.Should().Be(HomeCompanyId,
            "a forged cookie for a company not in MemberCompanyIds must be ignored; " +
            "resolver must fall through to the CompanyId claim (10)");

        // Filtered query must reflect the resolver's decision
        var filtered = await _db.Announcements.ToListAsync();

        // ── Assert
        filtered.Should().ContainSingle(
            "only the company-10 row should be visible when the resolver falls back to the home company");
        filtered[0].CompanyId.Should().Be(HomeCompanyId,
            "the returned row must belong to the home company");

        filtered.Should().NotContain(a => a.CompanyId == SwitchedCompanyId,
            "the company-20 row must NOT be visible — the forged cookie was rejected by the resolver");
    }

    /// <summary>
    /// Test 4 — No switch cookie → home company.
    ///
    /// No member_selected_company cookie.  Tenant is the CompanyId claim (home company).
    /// Queries see ONLY the home company's data.
    /// </summary>
    [Fact]
    public async Task NoCookie_HomeCompanyIsUsed_QuerySeesOnlyHomeCompanyRows()
    {
        // ── Arrange: seed one row in each company
        _accessor.HttpContext = null;

        var rowCompany10 = MakeAnnouncement(HomeCompanyId, "Home row (no cookie)");
        var rowCompany20 = MakeAnnouncement(SwitchedCompanyId, "Switched row (no cookie)");
        _db.Announcements.AddRange(rowCompany10, rowCompany20);
        await _db.SaveChangesAsync();

        // ── Act: set up context without any member_selected_company cookie
        var user = MakeMemberPrincipal(
            companyId: HomeCompanyId,
            memberCompanyIds: $"{HomeCompanyId},{SwitchedCompanyId}");
        SetHttpContext(user, selectedCompanyCookie: null); // no cookie

        var activeTenant = _resolver.GetCurrentTenantId();
        activeTenant.Should().Be(HomeCompanyId,
            "without a cookie the resolver must fall through to the CompanyId claim (10)");

        var filtered = await _db.Announcements.ToListAsync();

        // ── Assert
        filtered.Should().ContainSingle(
            "only the home company's row should be visible when no switch cookie is present");
        filtered[0].CompanyId.Should().Be(HomeCompanyId);

        filtered.Should().NotContain(a => a.CompanyId == SwitchedCompanyId,
            "the company-20 row must not be visible when the active tenant is the home company");
    }
}
