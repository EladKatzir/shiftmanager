using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Regression suite for the "ViewAllShifts was a dead grant" defect (whole-app audit, cluster 1).
///
/// THE BUG: <c>ViewAllShifts</c> is seeded (GrantTypeSeed) and granted at ExpandToMolecule to
/// Lead/BRDirector/Director/MoleculeAdmin (RoleTemplateSeed:310 — "Lead/מפ"צ sees shifts across all
/// companies in their molecule"), and real leads hold it. But NO production code ever read it: the
/// only consumer of shift-view scope, /Calendar/Team, asked exclusively for <c>ViewShifts</c>
/// (company-scoped for Lead), so a lead's desk picker rendered a single option — the desk they were
/// already on. The grant was fully provisioned and enforced nowhere.
///
/// THE FIX: <see cref="IGrantService.GetShiftVisibleCompanyIdsAsync"/> is the ONE place that answers
/// "whose shifts may this user see" — the union of ViewShifts and ViewAllShifts scopes. Callers use
/// it instead of re-deriving the answer from a single grant key, so ViewAllShifts can never silently
/// go unenforced again.
///
/// Real SQLite (not UseInMemoryDatabase) per the fixture contract — these queries traverse
/// Company→Molecule→Area→Project and must go through the actual SQL translator.
/// </summary>
public class ShiftVisibleCompanyScopeTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly GrantService _service;

    private const int ViewShiftsTypeId = 1;
    private const int ViewAllShiftsTypeId = 2;

    public ShiftVisibleCompanyScopeTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _service = new GrantService(_db, new Mock<IHierarchyService>().Object, new Mock<IAuditLogService>().Object);
    }

    /// <summary>Seeds Project→Area→Molecule→{3 companies}, plus a second molecule with its own
    /// company so "molecule-wide" can be proven to stop at the molecule boundary.</summary>
    private async Task<(int MoleculeId, int OwnCompanyId, int SiblingAId, int SiblingBId, int OtherMoleculeCompanyId)> SeedHierarchyAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();

        var molecule = new Molecule { AreaId = area.Id, Name = "Oren", Type = MoleculeType.Workforce };
        var otherMolecule = new Molecule { AreaId = area.Id, Name = "Elsewhere", Type = MoleculeType.Workforce };
        _db.Molecules.AddRange(molecule, otherMolecule); await _db.SaveChangesAsync();

        var own = new Company { MoleculeId = molecule.Id, Name = "Hir", DisplayName = "Hir" };
        var sibA = new Company { MoleculeId = molecule.Id, Name = "City", DisplayName = "City" };
        var sibB = new Company { MoleculeId = molecule.Id, Name = "Tzafona", DisplayName = "Tzafona" };
        var far = new Company { MoleculeId = otherMolecule.Id, Name = "GAP", DisplayName = "GAP" };
        _db.Companies.AddRange(own, sibA, sibB, far); await _db.SaveChangesAsync();

        _db.GrantTypes.AddRange(
            new GrantType { Id = ViewShiftsTypeId, Key = "ViewShifts", NameKey = "Grant_ViewShifts", DescriptionKey = "d", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true },
            new GrantType { Id = ViewAllShiftsTypeId, Key = "ViewAllShifts", NameKey = "Grant_ViewAllShifts", DescriptionKey = "d", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        await _db.SaveChangesAsync();

        return (molecule.Id, own.Id, sibA.Id, sibB.Id, far.Id);
    }

    private async Task GrantAsync(int userId, int grantTypeId, int? companyId = null, int? moleculeId = null)
    {
        _db.Grants.Add(new Grant
        {
            UserId = userId,
            GrantTypeId = grantTypeId,
            CompanyId = companyId,
            MoleculeId = moleculeId,
            CanOwn = true,
            IsAutoGrant = true
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// THE regression test. A Lead holds ViewShifts at their own company AND ViewAllShifts at
    /// molecule scope (exactly what RoleTemplateSeed provisions). Shift-visible companies must be
    /// the whole molecule — before the fix this returned only the lead's own company, which is what
    /// collapsed /Calendar/Team's desk picker to a single option.
    /// </summary>
    [Fact]
    public async Task ShiftVisibleCompanies_LeadWithMoleculeWideViewAllShifts_SeesEveryDeskInMolecule()
    {
        const int LeadId = 700;
        var h = await SeedHierarchyAsync();
        await GrantAsync(LeadId, ViewShiftsTypeId, companyId: h.OwnCompanyId);
        await GrantAsync(LeadId, ViewAllShiftsTypeId, moleculeId: h.MoleculeId);

        var visible = await _service.GetShiftVisibleCompanyIdsAsync(LeadId);

        visible.Should().BeEquivalentTo(new[] { h.OwnCompanyId, h.SiblingAId, h.SiblingBId });
    }

    /// <summary>
    /// The molecule grant must not leak past its molecule — a company in a DIFFERENT molecule of the
    /// same area stays invisible. Guards against "fix the dead grant" turning into over-granting.
    /// </summary>
    [Fact]
    public async Task ShiftVisibleCompanies_MoleculeGrant_DoesNotReachOtherMolecules()
    {
        const int LeadId = 701;
        var h = await SeedHierarchyAsync();
        await GrantAsync(LeadId, ViewShiftsTypeId, companyId: h.OwnCompanyId);
        await GrantAsync(LeadId, ViewAllShiftsTypeId, moleculeId: h.MoleculeId);

        var visible = await _service.GetShiftVisibleCompanyIdsAsync(LeadId);

        visible.Should().NotContain(h.OtherMoleculeCompanyId);
    }

    /// <summary>
    /// An employee holds only company-scoped ViewShifts and no ViewAllShifts — they must still see
    /// exactly one desk. Proves the fix widens scope ONLY for holders of the molecule grant.
    /// </summary>
    [Fact]
    public async Task ShiftVisibleCompanies_WithoutViewAllShifts_StaysCompanyScoped()
    {
        const int EmployeeId = 702;
        var h = await SeedHierarchyAsync();
        await GrantAsync(EmployeeId, ViewShiftsTypeId, companyId: h.OwnCompanyId);

        var visible = await _service.GetShiftVisibleCompanyIdsAsync(EmployeeId);

        visible.Should().BeEquivalentTo(new[] { h.OwnCompanyId });
    }

    /// <summary>A user with no shift-view grants at all gets an empty set, never a null.</summary>
    [Fact]
    public async Task ShiftVisibleCompanies_NoGrants_ReturnsEmpty()
    {
        const int StrangerId = 703;
        await SeedHierarchyAsync();

        var visible = await _service.GetShiftVisibleCompanyIdsAsync(StrangerId);

        visible.Should().BeEmpty();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
