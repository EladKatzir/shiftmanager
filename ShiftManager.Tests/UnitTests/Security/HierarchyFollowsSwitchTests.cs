using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Security;

/// <summary>
/// Verifies that after a company switch the hierarchy resolution used by both
/// SaveQuickInfoConfig (IDOR guard) and OnCallWidgetViewComponent (molecule lookup)
/// correctly follows the ACTIVE company rather than the login-baked MoleculeId claim.
///
/// Seed: Project 1 → Area 1 → Molecule M1 (id=1) → Company 10 (home)
///                           → Molecule M2 (id=2) → Company 20 (switched)
///
/// The core assertion is that GetHierarchyPathForCompanyAsync(20) returns M2,
/// which is precisely the molecule the two fixed callers derive at runtime.
/// </summary>
public sealed class HierarchyFollowsSwitchTests : IAsyncLifetime
{
    // ─── Fixed IDs ──────────────────────────────────────────────────────────
    private const int ProjectId  = 1;
    private const int AreaId     = 1;
    private const int M1Id       = 1;   // Molecule that contains the home company
    private const int M2Id       = 2;   // Molecule that contains the switched company
    private const int HomeCompanyId     = 10;
    private const int SwitchedCompanyId = 20;

    // ─── Infrastructure ──────────────────────────────────────────────────────
    private SqliteConnection _connection = null!;
    private AppDbContext     _db         = null!;

    public async Task InitializeAsync()
    {
        // DataSource=:memory:;Foreign Keys=False mirrors MultiCompanyTenantIsolationTests.
        // FK=False lets us insert rows in any order without satisfying every FK constraint,
        // which is fine here because HierarchyService does Include() navigation, not FK checks.
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();

        // We need a TenantResolver to construct AppDbContext.  For HierarchyService tests we
        // do not care about query filters on the entities being tested (Projects/Areas/Molecules/
        // Companies are not IBelongsToCompany), so a minimal resolver pointing at HomeCompanyId
        // is sufficient — the resolver's value is not exercised by GetHierarchyPathForCompanyAsync.
        var accessor = new HttpContextAccessor();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim("CompanyId", HomeCompanyId.ToString()),
            new Claim("MemberCompanyIds", $"{HomeCompanyId},{SwitchedCompanyId}")
        }, "test"));
        accessor.HttpContext = new DefaultHttpContext { User = principal };

        var resolver = new TenantResolver(accessor, NullLogger<TenantResolver>.Instance);

        // DynamicModelCacheKeyFactory — same isolation pattern as MultiCompanyTenantIsolationTests
        // to prevent EF from reusing a cached model from an earlier test in the full suite.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<IModelCacheKeyFactory, DynamicModelCacheKeyFactory>()
            .Options;

        _db = new AppDbContext(options, resolver);
        await _db.Database.EnsureCreatedAsync();

        // ── Seed the full Project→Area→Molecule→Company hierarchy ────────────
        // Insert in dependency order. FK=False means EF won't enforce relationships,
        // but we still seed in the correct order so Include() navigations resolve.

        _db.Projects.Add(new Project
        {
            Id       = ProjectId,
            Name     = "TestProject",
            IsActive = true
        });

        _db.Areas.Add(new Area
        {
            Id        = AreaId,
            ProjectId = ProjectId,
            Name      = "TestArea",
            IsActive  = true
        });

        _db.Molecules.AddRange(
            new Molecule
            {
                Id       = M1Id,
                AreaId   = AreaId,
                Name     = "Molecule1",
                Type     = MoleculeType.Workforce,
                IsActive = true
            },
            new Molecule
            {
                Id       = M2Id,
                AreaId   = AreaId,
                Name     = "Molecule2",
                Type     = MoleculeType.Workforce,
                IsActive = true
            });

        _db.Companies.AddRange(
            new Company { Id = HomeCompanyId,     Name = "HomeCompany",     MoleculeId = M1Id },
            new Company { Id = SwitchedCompanyId, Name = "SwitchedCompany", MoleculeId = M2Id });

        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Core resolution test: GetHierarchyPathForCompanyAsync must map each company to the
    /// correct molecule — this is the exact resolution both fixes rely on at runtime.
    /// </summary>
    [Fact]
    public async Task GetHierarchyPathForCompany_ReturnsCorrectMolecule()
    {
        var sut = new HierarchyService(_db);

        // Home company resolves to M1
        var homePath = await sut.GetHierarchyPathForCompanyAsync(HomeCompanyId);
        homePath.Should().NotBeNull("home company must resolve to a hierarchy path");
        homePath!.Molecule.Id.Should().Be(M1Id,
            "home company (10) lives in molecule M1 (id=1)");
        homePath.Company!.Id.Should().Be(HomeCompanyId);

        // Switched company resolves to M2 — this is the key assertion the fix depends on
        var switchedPath = await sut.GetHierarchyPathForCompanyAsync(SwitchedCompanyId);
        switchedPath.Should().NotBeNull("switched company must resolve to a hierarchy path");
        switchedPath!.Molecule.Id.Should().Be(M2Id,
            "switched company (20) lives in molecule M2 (id=2), NOT in molecule M1 — " +
            "the fix must resolve M2, not the stale M1 claim");
        switchedPath.Company!.Id.Should().Be(SwitchedCompanyId);
    }

    /// <summary>
    /// Cross-molecule isolation: home and switched companies resolve to DIFFERENT molecules.
    /// This proves the fix correctly distinguishes the two persona companies.
    /// </summary>
    [Fact]
    public async Task GetHierarchyPathForCompany_HomeMolecule_DiffersFromSwitchedMolecule()
    {
        var sut = new HierarchyService(_db);

        var homePath    = await sut.GetHierarchyPathForCompanyAsync(HomeCompanyId);
        var switchedPath = await sut.GetHierarchyPathForCompanyAsync(SwitchedCompanyId);

        homePath!.Molecule.Id.Should().NotBe(switchedPath!.Molecule.Id,
            "the two companies are in different molecules — " +
            "the stale MoleculeId claim (M1) would 403 a switched user requesting M2-scoped data, " +
            "which is exactly the bug being fixed");
    }

    /// <summary>
    /// IDOR equivalence for single-company users: resolving via GetHierarchyPathForCompanyAsync
    /// on the home company returns the same molecule id as what the old claim-based check used.
    ///
    /// This is the mathematical basis of the 'no regression for single-company users' argument:
    /// active company == home company  ⟹  resolved molecule == claim molecule  ⟹  same 200/403 decision.
    /// </summary>
    [Fact]
    public async Task GetHierarchyPathForCompany_HomeCompany_MatchesTrustworthy_M1()
    {
        var sut = new HierarchyService(_db);

        var path = await sut.GetHierarchyPathForCompanyAsync(HomeCompanyId);

        // A single-company user's claim was baked from their home company's molecule (M1).
        // The new code calls GetHierarchyPathForCompanyAsync(homeCompanyId) and reads .Molecule.Id.
        // Both must equal M1Id — no regression.
        path!.Molecule.Id.Should().Be(M1Id,
            "for single-company users, active company == home company, " +
            "so the resolved molecule matches the login-baked claim value exactly");
    }

    /// <summary>
    /// Nonexistent company returns null — matching the old int.TryParse default of 0.
    /// The callers handle null gracefully: SaveQuickInfoConfig 403s (null != moleculeId),
    /// OnCallWidgetViewComponent uses ?? 0.
    /// </summary>
    [Fact]
    public async Task GetHierarchyPathForCompany_UnknownCompany_ReturnsNull()
    {
        var sut = new HierarchyService(_db);

        var path = await sut.GetHierarchyPathForCompanyAsync(9999);

        path.Should().BeNull(
            "an unknown company must yield null so callers can apply their ?? 0 / 403 fallbacks");
    }
}
