using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Models.Validation;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for the shifts Draft Mode engine after the Foundation (Spec F) rework: the shift
/// commit logic is now the shifts <see cref="IDraftReconciler"/> (<see cref="DraftModeService"/>) driven by
/// the shared <see cref="DraftLifecycle"/>. Staging stays off the live board; commit applies conflict-free
/// cells and SKIPS drifted / unauthorized ones per-cell (never an all-or-nothing abort); discard throws the
/// sandbox away; and the single-active invariant is a DB filtered-unique index. The assignment validator is
/// mocked "clean" and the grant service "admin" so these tests exercise the reconciliation/conflict/auth
/// wiring, not the underlying assignment/grant rules (covered elsewhere).
/// </summary>
public sealed class DraftModeServiceTests
{
    private static readonly DateOnly D = new(2026, 6, 15);
    private const int ShiftA = 100; // seeded with a live instance
    private const int ShiftB = 101; // seeded shift type, no instance (empty cell)

    private static Mock<IShiftAssignmentService> CleanValidator()
    {
        var m = new Mock<IShiftAssignmentService>();
        m.Setup(v => v.ValidateShiftAssignmentAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ShiftAssignmentValidation(true, Array.Empty<ValidationIssue>(), Array.Empty<ValidationIssue>()));
        return m;
    }

    private static IGrantService AdminGrant()
    {
        var g = new Mock<IGrantService>();
        g.Setup(x => x.HasGrantAsync(It.IsAny<int>(), "AdminAccess")).ReturnsAsync(true);
        return g.Object;
    }

    private static (DraftModeService reconciler, DraftLifecycle lifecycle) Build(SqliteDbContextFixture f, IGrantService? grant = null)
    {
        var reconciler = new DraftModeService(f.Db, CleanValidator().Object, grant ?? AdminGrant());
        var lifecycle = new DraftLifecycle(f.Db, new IDraftReconciler[] { reconciler });
        return (reconciler, lifecycle);
    }

    private static DraftScope ShiftScopeOf() => new(DraftSurface.Shifts, 1, null, null, D, D);

    private static async Task SeedAsync(SqliteDbContextFixture f, bool assignUser10)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 });
        f.Db.ShiftTypes.AddRange(
            new ShiftType { Id = ShiftA, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "MORNING" },
            new ShiftType { Id = ShiftB, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "EVENING" });
        f.Db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A" },
            new AppUser { Id = 11, CompanyId = 1, Email = "b@x.mil", DisplayName = "B" },
            new AppUser { Id = 12, CompanyId = 1, Email = "c@x.mil", DisplayName = "C" });
        // Live instance for shift A on D with two slots (one filled by user 10 when requested, one empty).
        f.Db.ShiftInstances.Add(new ShiftInstance { Id = 1000, CompanyId = 1, ShiftTypeId = ShiftA, WorkDate = D, StaffingRequired = 2 });
        f.Db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 5000, CompanyId = 1, ShiftInstanceId = 1000, UserId = assignUser10 ? 10 : (int?)null },
            new ShiftAssignment { Id = 5001, CompanyId = 1, ShiftInstanceId = 1000, UserId = null });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Task<List<int>> LiveCellUsersAsync(SqliteDbContextFixture f, int shiftTypeId)
        => f.Db.ShiftAssignments.IgnoreQueryFilters()
            .Where(a => a.UserId != null && a.ShiftInstance.ShiftTypeId == shiftTypeId && a.ShiftInstance.WorkDate == D)
            .Select(a => a.UserId!.Value).ToListAsync();

    [Fact]
    public async Task Staging_Does_Not_Touch_The_Live_Board()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, 11);

        var overlay = await svc.GetOverlayAsync(draft.Id);
        overlay.Should().ContainSingle();
        overlay[0].UserIds.Should().BeEquivalentTo(new[] { 10, 11 });

        f.Db.ChangeTracker.Clear();
        (await LiveCellUsersAsync(f, ShiftA)).Should().BeEquivalentTo(new[] { 10 }, "staging must not alter the live board");
    }

    [Fact]
    public async Task Commit_Applies_Staged_Cells_To_Live()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, 11);

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue();
        result.Skipped.Should().BeEmpty();
        result.Unauthorized.Should().BeEmpty();
        result.Applied.Should().ContainSingle();
        result.Applied[0].RowId.Should().Be(ShiftA);

        f.Db.ChangeTracker.Clear();
        (await LiveCellUsersAsync(f, ShiftA)).Should().BeEquivalentTo(new[] { 10, 11 });
    }

    [Fact]
    public async Task Commit_Skips_A_Drifted_Cell_But_Still_Commits_The_Session()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, 11); // baseline captured as {10}

        // Someone else changes the live cell after the draft entry: user 12 takes the empty slot.
        var emptySlot = await f.Db.ShiftAssignments.IgnoreQueryFilters().FirstAsync(a => a.Id == 5001);
        emptySlot.UserId = 12;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue("per-cell policy commits the session even when a cell drifted");
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().ContainSingle();
        result.Skipped[0].RowId.Should().Be(ShiftA);

        f.Db.ChangeTracker.Clear();
        (await LiveCellUsersAsync(f, ShiftA)).Should().BeEquivalentTo(new[] { 10, 12 }, "a drifted cell is skipped — its staged set is NOT applied");

        // Session moved to Committed (a drifted cell does not keep it Active).
        (await f.Db.DraftSessions.FirstAsync(d => d.Id == draft.Id)).Status.Should().Be(DraftSessionStatus.Committed);
    }

    [Fact]
    public async Task Commit_Applies_Clean_Cells_And_Skips_Only_The_Drifted_One()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, 11); // cell A: baseline {10}
        await svc.StageAssignAsync(draft.Id, ShiftB, D, 11); // cell B (empty): baseline {}

        // Drift ONLY cell A (user 12 grabs the empty slot); cell B stays clean.
        var emptySlot = await f.Db.ShiftAssignments.IgnoreQueryFilters().FirstAsync(a => a.Id == 5001);
        emptySlot.UserId = 12;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue();
        result.Applied.Select(c => c.RowId).Should().BeEquivalentTo(new[] { ShiftB });
        result.Skipped.Select(c => c.RowId).Should().BeEquivalentTo(new[] { ShiftA });

        f.Db.ChangeTracker.Clear();
        (await LiveCellUsersAsync(f, ShiftA)).Should().BeEquivalentTo(new[] { 10, 12 }, "drifted cell A unchanged");
        (await LiveCellUsersAsync(f, ShiftB)).Should().BeEquivalentTo(new[] { 11 }, "clean cell B applied a fresh instance");
    }

    [Fact]
    public async Task Commit_Skips_A_Cell_Whose_Grant_Was_Revoked()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        // No-grant service: HasGrantAsync/HasGrantWithScopeAsync both return false → cell fails re-auth at commit.
        var (svc, life) = Build(f, grant: Mock.Of<IGrantService>());

        var draft = await life.EnterAsync(10, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, 11);

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue();
        result.Applied.Should().BeEmpty();
        result.Unauthorized.Should().ContainSingle();
        result.Unauthorized[0].RowId.Should().Be(ShiftA);

        f.Db.ChangeTracker.Clear();
        (await LiveCellUsersAsync(f, ShiftA)).Should().BeEquivalentTo(new[] { 10 }, "an unauthorized cell applies nothing");
    }

    [Fact]
    public async Task Commit_Reports_NotifiedGroups_For_Applied_Cells()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, 11);

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        // Shifts group is shifts-{molecule}-{jobType ?? 0}; this draft is molecule 1, jobType null.
        result.NotifiedGroups.Should().BeEquivalentTo(new[] { "shifts-1-0" });
    }

    [Fact]
    public async Task Discard_Removes_The_Sandbox_And_Leaves_Live_Untouched()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, 11);
        await svc.StageClearAsync(draft.Id, ShiftA, D, 10);

        await life.DiscardAsync(draft.Id, ownerUserId: 10);

        f.Db.ChangeTracker.Clear();
        (await f.Db.DraftSessions.CountAsync()).Should().Be(0);
        (await f.Db.DraftCells.CountAsync()).Should().Be(0, "cells cascade with the session");
        (await LiveCellUsersAsync(f, ShiftA)).Should().BeEquivalentTo(new[] { 10 });
    }

    [Fact]
    public async Task Discard_Does_Not_Remove_Another_Users_Session()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var (_, life) = Build(f);

        var draft = await life.EnterAsync(10, ShiftScopeOf());
        await life.DiscardAsync(draft.Id, ownerUserId: 999); // not the owner

        (await f.Db.DraftSessions.CountAsync()).Should().Be(1, "discard is owner-scoped");
    }

    [Fact]
    public async Task Enter_Is_Idempotent_For_The_Same_Owner_Scope()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: false);
        var (_, life) = Build(f);

        var a = await life.EnterAsync(10, ShiftScopeOf());
        var b = await life.EnterAsync(10, ShiftScopeOf());
        b.Id.Should().Be(a.Id, "re-entering returns the existing active sandbox");
        (await f.Db.DraftSessions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FilteredUniqueIndex_Blocks_A_Second_Active_Session_For_The_Same_Scope()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: false);

        f.Db.DraftSessions.Add(new DraftSession
        {
            OwnerUserId = 10, Surface = DraftSurface.Shifts, MoleculeId = 1, JobTypeId = null,
            WeekStart = D, WeekEnd = D, Status = DraftSessionStatus.Active
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        f.Db.DraftSessions.Add(new DraftSession
        {
            OwnerUserId = 10, Surface = DraftSurface.Shifts, MoleculeId = 1, JobTypeId = null,
            WeekStart = D, WeekEnd = D, Status = DraftSessionStatus.Active
        });

        var act = async () => await f.Db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("the per-surface filtered-unique index enforces one Active draft per scope");
    }

    [Fact]
    public async Task FilteredUniqueIndex_Allows_A_New_Active_Session_After_The_First_Committed()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: false);

        // The filter is WHERE Status = Active, so a Committed row for the same scope does not block a re-draft.
        f.Db.DraftSessions.Add(new DraftSession
        {
            OwnerUserId = 10, Surface = DraftSurface.Shifts, MoleculeId = 1, JobTypeId = null,
            WeekStart = D, WeekEnd = D, Status = DraftSessionStatus.Committed
        });
        await f.Db.SaveChangesAsync();

        f.Db.DraftSessions.Add(new DraftSession
        {
            OwnerUserId = 10, Surface = DraftSurface.Shifts, MoleculeId = 1, JobTypeId = null,
            WeekStart = D, WeekEnd = D, Status = DraftSessionStatus.Active
        });

        var act = async () => await f.Db.SaveChangesAsync();
        await act.Should().NotThrowAsync("the unique index is filtered to Active rows only");
    }

    /// <summary>
    /// The Foundation migration auto-discards in-flight Active drafts on deploy (Spec F §8): drafts are
    /// ephemeral sandboxes and back-filling baselines into a schema they were never captured under is unsafe.
    /// Applies the real migration chain to the pre-Foundation draft schema, seeds one Active + one Committed
    /// draft, then applies the Foundation migration and asserts only the Active one is gone.
    /// </summary>
    [Fact]
    public async Task Migration_AutoDiscards_InFlight_Active_Drafts()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            await using var db = new AppDbContext(options);
            var migrator = db.Database.GetService<IMigrator>();

            // 1) Apply everything through the ORIGINAL draft schema (MoleculeId non-null, no Surface/AreaId).
            await migrator.MigrateAsync("20260609150413_AddDraftMode");

            // 2) Seed an Active (0) + a Committed (1) draft under that old schema.
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO DraftSessions (OwnerUserId, MoleculeId, JobTypeId, WeekStart, WeekEnd, Status, CreatedAt) VALUES " +
                "(1, 1, NULL, '2026-06-15', '2026-06-15', 0, '2026-06-15 00:00:00'), " +
                "(1, 1, NULL, '2026-06-15', '2026-06-15', 1, '2026-06-15 00:00:00');");

            // 3) Apply the rest of the chain, including the Foundation migration (auto-discard + new indexes).
            await migrator.MigrateAsync();

            var statuses = await db.Database.SqlQueryRaw<int>("SELECT Status AS Value FROM DraftSessions").ToListAsync();
            statuses.Should().BeEquivalentTo(new[] { 1 },
                "the Active (0) draft is auto-discarded by the migration; the Committed (1) row survives");
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
