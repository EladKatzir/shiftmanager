using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Task A5 — comparison-tier visibility security tests.
///
/// The Justice "comparison tier" lets a capped user READ all sibling units (so they can be
/// ranked against each other) but DRILL only into their own subtree. A drill request targeting
/// a sibling scope that is visible-but-not-drillable MUST be rejected.
///
/// Two surfaces are covered here:
///   1. <see cref="GrantService.GetAccessibleAreaIdsForGrantAsync"/> — the new grant helper that
///      backs the area-level scope picker AND the molecule-drill decision.
///   2. The drill decision itself — a user whose accessible molecules = {1} within an area that
///      contains molecules {1,2,3} may NOT drill into molecule 2 or 3 (siblings), only molecule 1.
///
/// The drill decision is the security core: a sibling row is intentionally rendered for comparison,
/// but navigating into it must be forbidden. We assert this against the grant-helper intersection
/// logic that the page model uses (drillable child molecules =
/// GetAccessibleMoleculeIdsForGrantAsync ∩ molecules-in-this-area), which is the exact rule the
/// page model's CanDrillScopeAsync applies before building the drilled view.
/// </summary>
public class JusticeAuthorizationTests : IDisposable
{
    private const string GrantKey = "ViewJusticeTable";

    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly GrantService _grantService;
    private readonly Mock<IHierarchyService> _hierarchyServiceMock;

    // Hierarchy: Project 1 > { Area 1 > {Mol 1, Mol 2, Mol 3}, Area 2 > {Mol 4} }
    // Companies: Co 1 (Mol 1), Co 2 (Mol 2), Co 3 (Mol 3), Co 4 (Mol 4)
    private const int ProjectId = 1;
    private const int AreaOneId = 1;
    private const int AreaTwoId = 2;
    private const int MolOneId = 1;
    private const int MolTwoId = 2;
    private const int MolThreeId = 3;
    private const int MolFourId = 4;
    private const int CoOneId = 1;
    private const int CoTwoId = 2;
    private const int CoThreeId = 3;
    private const int CoFourId = 4;
    private const int UserId = 999;

    private GrantType _grantType = null!;

    public JusticeAuthorizationTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _hierarchyServiceMock = new Mock<IHierarchyService>();
        _grantService = new GrantService(_db, _hierarchyServiceMock.Object, new Mock<IAuditLogService>().Object);

        SeedHierarchy();
    }

    private void SeedHierarchy()
    {
        _db.Projects.Add(new Project { Id = ProjectId, Name = "Proj", DisplayName = "Proj" });
        _db.Areas.AddRange(
            new Area { Id = AreaOneId, ProjectId = ProjectId, Name = "AreaOne", DisplayName = "AreaOne" },
            new Area { Id = AreaTwoId, ProjectId = ProjectId, Name = "AreaTwo", DisplayName = "AreaTwo" });
        _db.Molecules.AddRange(
            new Molecule { Id = MolOneId, AreaId = AreaOneId, Name = "MolOne", DisplayName = "MolOne", IsActive = true },
            new Molecule { Id = MolTwoId, AreaId = AreaOneId, Name = "MolTwo", DisplayName = "MolTwo", IsActive = true },
            new Molecule { Id = MolThreeId, AreaId = AreaOneId, Name = "MolThree", DisplayName = "MolThree", IsActive = true },
            new Molecule { Id = MolFourId, AreaId = AreaTwoId, Name = "MolFour", DisplayName = "MolFour", IsActive = true });
        _db.Companies.AddRange(
            new Company { Id = CoOneId, MoleculeId = MolOneId, Name = "CoOne", DisplayName = "CoOne" },
            new Company { Id = CoTwoId, MoleculeId = MolTwoId, Name = "CoTwo", DisplayName = "CoTwo" },
            new Company { Id = CoThreeId, MoleculeId = MolThreeId, Name = "CoThree", DisplayName = "CoThree" },
            new Company { Id = CoFourId, MoleculeId = MolFourId, Name = "CoFour", DisplayName = "CoFour" });

        _db.Users.Add(new AppUser
        {
            Id = UserId,
            CompanyId = CoOneId,
            Email = "u999@coone.com",
            DisplayName = "U999",
            IsActive = true,
            Role = UserRole.Employee,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        });

        _grantType = new GrantType
        {
            Key = GrantKey,
            NameKey = "Grant_ViewJusticeTable",
            DescriptionKey = "Grant_ViewJusticeTable_Desc",
            Category = GrantCategory.UserManagement,
            DefaultScope = GrantScopeLevel.Company,
            IsSystem = true,
            IsActive = true
        };
        _db.GrantTypes.Add(_grantType);
        _db.SaveChanges();

        // User's own hierarchy context — Company 1 / Molecule 1 / Area 1 / Project 1 (for Self resolution).
        var project = _db.Projects.Find(ProjectId)!;
        var area = _db.Areas.Find(AreaOneId)!;
        var molecule = _db.Molecules.Find(MolOneId)!;
        var company = _db.Companies.Find(CoOneId)!;
        _hierarchyServiceMock.Setup(h => h.GetUserHierarchyContextAsync(UserId))
            .ReturnsAsync(new UserHierarchyContext(
                UserId,
                new HierarchyPath(project, area, molecule, company, null),
                JobType: null,
                IsWorkforce: true,
                IsTech: false));
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private void AddGrant(int? projectId = null, int? areaId = null, int? moleculeId = null, int? companyId = null)
    {
        _db.Grants.Add(new Grant
        {
            UserId = UserId,
            GrantTypeId = _grantType.Id,
            ProjectId = projectId,
            AreaId = areaId,
            MoleculeId = moleculeId,
            CompanyId = companyId,
            CanOwn = true
        });
        _db.SaveChanges();
    }

    // =================================================================================
    // GetAccessibleAreaIdsForGrantAsync — area is accessible iff the user can access a
    // molecule in it (cascade) OR holds an area/project grant covering it.
    // =================================================================================

    [Fact]
    public async Task GetAccessibleAreaIds_WithCompanyScope_ReturnsOwningAreaOnly()
    {
        // Company-scoped grant on Co 1 (Mol 1 → Area 1). Only Area 1 is reachable.
        AddGrant(companyId: CoOneId);

        var result = await _grantService.GetAccessibleAreaIdsForGrantAsync(UserId, GrantKey);

        result.Should().Contain(AreaOneId, "Co 1 sits in Mol 1 which is in Area 1");
        result.Should().NotContain(AreaTwoId, "no accessible molecule lives in Area 2");
    }

    [Fact]
    public async Task GetAccessibleAreaIds_WithMoleculeScope_ReturnsThatMoleculesArea()
    {
        // Molecule-scoped grant on Mol 2 (in Area 1). Area 1 reachable; Area 2 not.
        AddGrant(moleculeId: MolTwoId);

        var result = await _grantService.GetAccessibleAreaIdsForGrantAsync(UserId, GrantKey);

        result.Should().Contain(AreaOneId);
        result.Should().NotContain(AreaTwoId);
    }

    [Fact]
    public async Task GetAccessibleAreaIds_WithAreaScope_ReturnsThatArea()
    {
        AddGrant(areaId: AreaTwoId);

        var result = await _grantService.GetAccessibleAreaIdsForGrantAsync(UserId, GrantKey);

        result.Should().Contain(AreaTwoId);
        result.Should().NotContain(AreaOneId);
    }

    [Fact]
    public async Task GetAccessibleAreaIds_WithProjectScope_ReturnsAllAreasInProject()
    {
        AddGrant(projectId: ProjectId);

        var result = await _grantService.GetAccessibleAreaIdsForGrantAsync(UserId, GrantKey);

        result.Should().Contain(new[] { AreaOneId, AreaTwoId });
    }

    [Fact]
    public async Task GetAccessibleAreaIds_ExcludesAreasWithNoAccessibleMolecule()
    {
        // Grant only on Co 1 → Area 1. Area 2 (Mol 4 / Co 4) must NOT appear.
        AddGrant(companyId: CoOneId);

        var result = await _grantService.GetAccessibleAreaIdsForGrantAsync(UserId, GrantKey);

        result.Should().NotContain(AreaTwoId,
            "Area 2 contains no molecule the user can access — it must be excluded");
    }

    [Fact]
    public async Task GetAccessibleAreaIds_WithNoGrant_ReturnsEmpty()
    {
        var result = await _grantService.GetAccessibleAreaIdsForGrantAsync(UserId, GrantKey);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAccessibleAreaIds_WithAreaScope_OnEmptyArea_StillReturnsTheArea()
    {
        // An area/project grant covering an area with no molecules must still be reported as
        // accessible — the cascade-via-molecules path alone would miss it. (Per A5 spec:
        // "OR holds an area/project-scoped grant covering it".)
        var emptyAreaId = 99;
        _db.Areas.Add(new Area { Id = emptyAreaId, ProjectId = ProjectId, Name = "Empty", DisplayName = "Empty" });
        _db.SaveChanges();
        AddGrant(areaId: emptyAreaId);

        var result = await _grantService.GetAccessibleAreaIdsForGrantAsync(UserId, GrantKey);

        result.Should().Contain(emptyAreaId, "an explicit area-scoped grant covers the area even with no molecules");
    }

    // =================================================================================
    // The DRILL decision (security core).
    //
    // A capped user (accessible molecules = {1} inside Area 1 = {1,2,3}) may READ all three
    // molecule rows for comparison, but may only DRILL into molecule 1. The page model computes
    // drillable child molecules as GetAccessibleMoleculeIdsForGrantAsync ∩ molecules-in-this-area
    // and refuses any drill scopeId outside that set. These tests pin that intersection rule —
    // the exact predicate CanDrillScopeAsync evaluates.
    // =================================================================================

    [Fact]
    public async Task DrillIntoOwnMolecule_IsAllowed()
    {
        AddGrant(moleculeId: MolOneId);

        var drillable = await ComputeDrillableMoleculesInArea(AreaOneId);

        drillable.Should().Contain(MolOneId, "molecule 1 is the user's own subtree — drill allowed");
    }

    [Fact]
    public async Task DrillIntoSiblingMolecule_IsRejected()
    {
        // Accessible molecules = {1}; molecule 2 is a readable sibling inside the same area.
        AddGrant(moleculeId: MolOneId);

        var drillable = await ComputeDrillableMoleculesInArea(AreaOneId);

        drillable.Should().NotContain(MolTwoId,
            "molecule 2 is a sibling shown for comparison only — drilling into it must be rejected");
        drillable.Should().NotContain(MolThreeId,
            "molecule 3 is also a readable-not-drillable sibling");
    }

    [Fact]
    public async Task DrillableMolecules_AreIntersectionOfAccessibleAndInArea()
    {
        // Grant covers the whole area → all molecules in Area 1 are drillable; Mol 4 (Area 2) is not.
        AddGrant(areaId: AreaOneId);

        var drillable = await ComputeDrillableMoleculesInArea(AreaOneId);

        drillable.Should().Contain(new[] { MolOneId, MolTwoId, MolThreeId });
        drillable.Should().NotContain(MolFourId, "Mol 4 lives in Area 2, not the area being drilled");
    }

    /// <summary>
    /// Mirrors the page model's per-level drillable computation for MoleculesInArea:
    ///   drillable child molecules = GetAccessibleMoleculeIdsForGrantAsync ∩ molecules-in-this-area.
    /// This is the exact set CanDrillScopeAsync(user, Molecule, scopeId) tests membership against.
    /// </summary>
    private async Task<List<int>> ComputeDrillableMoleculesInArea(int areaId)
    {
        var accessibleMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(UserId, GrantKey);
        var moleculesInArea = await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => m.AreaId == areaId)
            .Select(m => m.Id)
            .ToListAsync();
        return moleculesInArea.Where(id => accessibleMoleculeIds.Contains(id)).ToList();
    }
}
