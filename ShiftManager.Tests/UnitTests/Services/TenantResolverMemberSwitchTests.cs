using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for the member-company-switch rung added to <see cref="TenantResolver.GetCurrentTenantId"/>.
/// Builds a TenantResolver directly with an IHttpContextAccessor stub so no DI container or
/// database is needed. All scenarios use cookie-based inputs validated against the
/// MemberCompanyIds claim (baked at login).
/// </summary>
public sealed class TenantResolverMemberSwitchTests
{
    // ────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a ClaimsPrincipal with a CompanyId claim and optionally a MemberCompanyIds claim.
    /// </summary>
    private static ClaimsPrincipal MakeUser(
        int companyId,
        string? memberCompanyIds = null,
        string role = "Employee")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Role, role),
            new("CompanyId", companyId.ToString())
        };
        if (memberCompanyIds != null)
            claims.Add(new Claim("MemberCompanyIds", memberCompanyIds));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    /// <summary>
    /// Builds a TenantResolver with a fake HTTP context carrying the given user and optional
    /// member_selected_company cookie. When <paramref name="ownerSelectorStub"/> is supplied it is
    /// registered in the context's RequestServices so the Owner-selector rung (Priority 2) can
    /// resolve it — mirroring how TenantResolver pulls IOwnerCompanySelectorService at runtime.
    /// </summary>
    private static TenantResolver MakeResolver(
        ClaimsPrincipal user,
        string? memberSelectedCookie = null,
        IOwnerCompanySelectorService? ownerSelectorStub = null)
    {
        var ctx = new DefaultHttpContext { User = user };
        if (memberSelectedCookie != null)
        {
            ctx.Request.Headers["Cookie"] =
                $"{ActiveCompanySelectorService.CookieName}={memberSelectedCookie}";
        }

        if (ownerSelectorStub != null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(ownerSelectorStub);
            ctx.RequestServices = services.BuildServiceProvider();
        }

        var accessor = new HttpContextAccessorStub(ctx);
        return new TenantResolver(accessor, NullLogger<TenantResolver>.Instance);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Tests
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetCurrentTenantId_MemberCookieMatchesClaim_ReturnsSelectedCompany()
    {
        // User is a member of companies 10 and 20; their home CompanyId claim is 10;
        // a member_selected_company cookie is set to 20.
        // Expected: resolver returns 20 (the selected company).
        var user = MakeUser(companyId: 10, memberCompanyIds: "10,20");
        var resolver = MakeResolver(user, memberSelectedCookie: "20");

        resolver.GetCurrentTenantId().Should().Be(20,
            "cookie value 20 is present in the MemberCompanyIds claim so the switch is honoured");
    }

    [Fact]
    public void GetCurrentTenantId_MemberCookiePresentButNotInClaim_FallsThroughToCompanyIdClaim()
    {
        // The cookie value 99 is NOT in MemberCompanyIds=10 (spoof attempt).
        // Expected: resolver ignores the spoofed cookie and returns the CompanyId claim (10).
        var user = MakeUser(companyId: 10, memberCompanyIds: "10");
        var resolver = MakeResolver(user, memberSelectedCookie: "99");

        resolver.GetCurrentTenantId().Should().Be(10,
            "cookie value 99 is not in MemberCompanyIds so it is ignored; CompanyId claim is used");
    }

    [Fact]
    public void GetCurrentTenantId_NoCookie_ReturnsPrimaryCompanyIdClaim()
    {
        // No member_selected_company cookie at all.
        // Expected: resolver returns the CompanyId claim.
        var user = MakeUser(companyId: 10, memberCompanyIds: "10,20");
        var resolver = MakeResolver(user, memberSelectedCookie: null);

        resolver.GetCurrentTenantId().Should().Be(10,
            "without a cookie the resolver falls through to the CompanyId claim");
    }

    [Fact]
    public void GetCurrentTenantId_NoMemberCompanyIdsClaim_CookieIgnored()
    {
        // Single-company user has no MemberCompanyIds claim; a rogue cookie is set.
        // Expected: resolver ignores the cookie and returns the CompanyId claim.
        var user = MakeUser(companyId: 10, memberCompanyIds: null);
        var resolver = MakeResolver(user, memberSelectedCookie: "20");

        resolver.GetCurrentTenantId().Should().Be(10,
            "without a MemberCompanyIds claim the cookie is meaningless and must be ignored");
    }

    [Fact]
    public void GetCurrentTenantId_MemberCookieIsSubstringOfClaimEntry_FallsThroughToCompanyIdClaim()
    {
        // MemberCompanyIds = "20,200"; cookie = "2". A naive substring/Contains(string) check
        // would match because "2" is a substring of "20" and "200". The whole-token
        // Split(',').Contains("2") must NOT match, so the resolver falls through to CompanyId=10.
        var user = MakeUser(companyId: 10, memberCompanyIds: "20,200");
        var resolver = MakeResolver(user, memberSelectedCookie: "2");

        resolver.GetCurrentTenantId().Should().Be(10,
            "cookie '2' is only a substring of the claim entries '20'/'200', not a whole token, so it must be ignored");
    }

    [Fact]
    public void GetCurrentTenantId_Owner_WithOwnerSelection_ReturnsOwnerSelectedCompany_IgnoringMemberCookie()
    {
        // (4a) Owner WITH an owner-selection (99) plus a stale member cookie (20).
        // The Owner-selected rung (Priority 2) must win — resolver returns 99, never 20.
        var user = MakeUser(companyId: 10, memberCompanyIds: "10,20", role: "Owner");
        var ownerStub = new OwnerCompanySelectorStub(selectedCompanyId: 99);
        var resolver = MakeResolver(user, memberSelectedCookie: "20", ownerSelectorStub: ownerStub);

        resolver.GetCurrentTenantId().Should().Be(99,
            "an Owner's selected company (Priority 2) takes precedence over any member cookie");
    }

    [Fact]
    public void GetCurrentTenantId_Owner_WithoutOwnerSelection_IgnoresMemberCookie_ReturnsCompanyIdClaim()
    {
        // (4b) Owner with NO owner-selection (stub returns null) plus a VALID member cookie (20)
        // whose value IS in MemberCompanyIds="20". This is the exact privilege-bug scenario:
        // without the Owner-guard on the Priority 2.5 block, the resolver would incorrectly
        // honor the member cookie and return 20. With the guard, the member rung is skipped for
        // Owners and the resolver falls through to the CompanyId claim (10).
        var user = MakeUser(companyId: 10, memberCompanyIds: "20", role: "Owner");
        var ownerStub = new OwnerCompanySelectorStub(selectedCompanyId: null);
        var resolver = MakeResolver(user, memberSelectedCookie: "20", ownerSelectorStub: ownerStub);

        resolver.GetCurrentTenantId().Should().Be(10,
            "an Owner must NOT fall through into the member-selected rung; the stale member cookie is ignored and the CompanyId claim is used");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Stubs
    // ────────────────────────────────────────────────────────────────────────────

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
        public HttpContextAccessorStub(HttpContext ctx) => HttpContext = ctx;
    }

    /// <summary>
    /// Minimal IOwnerCompanySelectorService stub: only GetSelectedCompanyId is exercised by
    /// TenantResolver's Priority 2 rung; the rest throw to flag unexpected use.
    /// </summary>
    private sealed class OwnerCompanySelectorStub : IOwnerCompanySelectorService
    {
        private readonly int? _selectedCompanyId;
        public OwnerCompanySelectorStub(int? selectedCompanyId) => _selectedCompanyId = selectedCompanyId;

        public int? GetSelectedCompanyId() => _selectedCompanyId;

        public bool IsOwner() => true;
        public Task<bool> SelectCompanyAsync(int companyId) => throw new NotSupportedException();
        public Task ClearSelectionAsync() => throw new NotSupportedException();
        public Task<string?> GetSelectedCompanyNameAsync() => throw new NotSupportedException();
        public int? GetHomeCompanyId() => throw new NotSupportedException();
    }
}
