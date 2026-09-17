using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// The single rule for "which molecules' Shifts calendars may this user view". It previously lived
/// inline in the Shifts page (LoadAvailableMoleculesAsync); the day-note write endpoint now needs the
/// same answer to authorise a note against the molecule it targets. Two copies of an authorization
/// rule drift — that divergence is the defect class the access audit kept finding — so both callers
/// go through this one method.
/// </summary>
public class ShiftCalendarAccessTests
{
    private static async Task SeedAsync(AppDbContext db)
    {
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1", IsActive = true },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2", IsActive = true },
            new Molecule { Id = 3, AreaId = 1, Name = "M3", DisplayName = "M3", IsActive = false });
        await db.SaveChangesAsync();
    }

    private static IGrantService GrantsViewing(params int[] moleculeIds)
    {
        var grants = new Mock<IGrantService>();
        grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(7, "ViewShifts"))
              .ReturnsAsync(moleculeIds.ToList());
        return grants.Object;
    }

    [Fact]
    public async Task IncludesMoleculesReachableThroughTheViewShiftsGrant()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);

        var ids = await ShiftCalendarAccess.GetViewableMoleculeIdsAsync(f.Db, GrantsViewing(2), userId: 7, ownMoleculeId: 1);

        Assert.Contains(2, ids);
    }

    [Fact]
    public async Task AlwaysIncludesTheCallersOwnMolecule_EvenWithoutAGrant()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);

        var ids = await ShiftCalendarAccess.GetViewableMoleculeIdsAsync(f.Db, GrantsViewing(), userId: 7, ownMoleculeId: 1);

        Assert.Equal(new[] { 1 }, ids.OrderBy(i => i));
    }

    [Fact]
    public async Task ExcludesInactiveMolecules_EvenWhenTheGrantReachesThem()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);

        var ids = await ShiftCalendarAccess.GetViewableMoleculeIdsAsync(f.Db, GrantsViewing(2, 3), userId: 7, ownMoleculeId: 1);

        Assert.Equal(new[] { 1, 2 }, ids.OrderBy(i => i));
    }

    [Fact]
    public async Task WithNoOwnMoleculeAndNoGrant_ReturnsNothing()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);

        var ids = await ShiftCalendarAccess.GetViewableMoleculeIdsAsync(f.Db, GrantsViewing(), userId: 7, ownMoleculeId: null);

        Assert.Empty(ids);
    }
}
