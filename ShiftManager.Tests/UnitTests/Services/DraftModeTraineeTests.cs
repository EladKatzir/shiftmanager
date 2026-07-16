using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Models.Validation;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for sub-project A (trainee shadowing in the shifts draft) + sub-project B (capacity
/// widening at commit). Staging a trainee never touches the live board; commit applies primary+trainee
/// together with per-cell drift/skip, gates the trainee on its primary assigning successfully (G5), and
/// preserves a live trainee the draft never touched (F Risk #4). The assignment validators are mocked so
/// these tests exercise the reconcile wiring, not the underlying assignment/trainee rules.
/// </summary>
public sealed class DraftModeTraineeTests
{
    private static readonly DateOnly D = new(2026, 6, 15);
    private const int ShiftA = 100; // seeded with a live instance
    private const int ShiftB = 101; // seeded shift type, no instance (empty cell)

    private const int PrimaryX = 10, PrimaryY = 11, TraineeT = 13, TraineeU = 14;

    private static Mock<IShiftAssignmentService> CleanValidator()
    {
        var m = new Mock<IShiftAssignmentService>();
        m.Setup(v => v.ValidateShiftAssignmentAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ShiftAssignmentValidation(true, Array.Empty<ValidationIssue>(), Array.Empty<ValidationIssue>()));
        m.Setup(v => v.ValidateTraineeAssignmentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ShiftAssignmentValidation(true, Array.Empty<ValidationIssue>(), Array.Empty<ValidationIssue>()));
        return m;
    }

    private static IGrantService AdminGrant()
    {
        var g = new Mock<IGrantService>();
        g.Setup(x => x.HasGrantAsync(It.IsAny<int>(), "AdminAccess")).ReturnsAsync(true);
        return g.Object;
    }

    private static (DraftModeService reconciler, DraftLifecycle lifecycle) Build(
        SqliteDbContextFixture f, Mock<IShiftAssignmentService>? validator = null, IGrantService? grant = null)
    {
        var reconciler = new DraftModeService(f.Db, (validator ?? CleanValidator()).Object, grant ?? AdminGrant());
        var lifecycle = new DraftLifecycle(f.Db, new IDraftReconciler[] { reconciler });
        return (reconciler, lifecycle);
    }

    private static DraftScope ShiftScopeOf() => new(DraftSurface.Shifts, 1, null, null, D, D);

    /// <param name="liveTrainee">When set, the live slot for PrimaryX shadows this trainee.</param>
    /// <param name="staffingRequired">Live instance capacity (defaults to 2; pass 1 to seed a "full" cell).</param>
    private static async Task SeedAsync(SqliteDbContextFixture f, int? liveTrainee = null, int staffingRequired = 2)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 });
        f.Db.ShiftTypes.AddRange(
            new ShiftType { Id = ShiftA, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "MORNING" },
            new ShiftType { Id = ShiftB, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "EVENING" });
        f.Db.Users.AddRange(
            new AppUser { Id = PrimaryX, CompanyId = 1, Email = "x@x.mil", DisplayName = "X" },
            new AppUser { Id = PrimaryY, CompanyId = 1, Email = "y@x.mil", DisplayName = "Y" },
            new AppUser { Id = TraineeT, CompanyId = 1, Email = "t@x.mil", DisplayName = "T", Role = UserRole.Trainee },
            new AppUser { Id = TraineeU, CompanyId = 1, Email = "u@x.mil", DisplayName = "U", Role = UserRole.Trainee });
        f.Db.ShiftInstances.Add(new ShiftInstance { Id = 1000, CompanyId = 1, ShiftTypeId = ShiftA, WorkDate = D, StaffingRequired = staffingRequired });
        f.Db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 5000, CompanyId = 1, ShiftInstanceId = 1000, UserId = PrimaryX, TraineeUserId = liveTrainee },
            new ShiftAssignment { Id = 5001, CompanyId = 1, ShiftInstanceId = 1000, UserId = null });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Task<ShiftAssignment?> LiveSlotForAsync(SqliteDbContextFixture f, int userId)
        => f.Db.ShiftAssignments.IgnoreQueryFilters()
            .Where(a => a.UserId == userId && a.ShiftInstance.ShiftTypeId == ShiftA && a.ShiftInstance.WorkDate == D)
            .FirstOrDefaultAsync();

    [Fact]
    public async Task Stage_Trainee_On_Assigned_Primary_Is_Applied_At_Commit()
    {
        // G1 regression: a trainee added onto an already-assigned primary changes only the trainee set — the
        // primary set is unchanged, so a primary-only "changed cell" predicate would discard it at commit.
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(PrimaryX, ShiftScopeOf());
        await svc.StageTraineeAsync(draft.Id, ShiftA, D, primaryUserId: PrimaryX, traineeUserId: TraineeT);

        // Staging is private: live slot still has no trainee.
        (await LiveSlotForAsync(f, PrimaryX))!.TraineeUserId.Should().BeNull("staging must not touch the live board");
        f.Db.ChangeTracker.Clear();

        var result = await life.CommitAsync(draft.Id, actingUserId: PrimaryX);
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle();

        f.Db.ChangeTracker.Clear();
        (await LiveSlotForAsync(f, PrimaryX))!.TraineeUserId.Should().Be(TraineeT, "the staged trainee is applied at commit");
    }

    [Fact]
    public async Task Commit_Preserves_An_Untouched_Live_Trainee()
    {
        // F Risk #4 regression: a draft that only adds ANOTHER primary to a cell must not drop the live
        // trainee shadowing the pre-existing primary.
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, liveTrainee: TraineeT);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(PrimaryX, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, PrimaryY); // add a second primary; never touch the trainee

        var result = await life.CommitAsync(draft.Id, actingUserId: PrimaryX);
        result.Committed.Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await LiveSlotForAsync(f, PrimaryX))!.TraineeUserId.Should().Be(TraineeT, "an untouched live trainee is preserved");
        (await LiveSlotForAsync(f, PrimaryY)).Should().NotBeNull("the newly staged primary is applied");
    }

    [Fact]
    public async Task Trainee_Is_Skipped_When_Its_Primary_Hard_Errors()
    {
        // G5: a trainee is gated on its primary assigning successfully. Primary Y hard-errors → its trainee is
        // skipped and reported.
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var validator = CleanValidator();
        validator.Setup(v => v.ValidateShiftAssignmentAsync(PrimaryY, It.IsAny<int>()))
            .ReturnsAsync(new ShiftAssignmentValidation(false,
                new List<ValidationIssue> { new("BUSY", "primary busy", ValidationSeverity.Error, ValidationCategory.WeeklyHours) },
                Array.Empty<ValidationIssue>()));
        var (svc, life) = Build(f, validator);

        var draft = await life.EnterAsync(PrimaryX, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftB, D, PrimaryY);                          // empty cell → primary Y
        await svc.StageTraineeAsync(draft.Id, ShiftB, D, primaryUserId: PrimaryY, traineeUserId: TraineeT);

        var result = await life.CommitAsync(draft.Id, actingUserId: PrimaryX);
        result.Committed.Should().BeTrue();
        result.ValidationIssues.Should().Contain(i => i.UserId == PrimaryY, "the primary hard-errored");
        result.ValidationIssues.Should().Contain(i => i.UserId == TraineeT, "its trainee is skipped + reported");

        f.Db.ChangeTracker.Clear();
        var traineeApplied = await f.Db.ShiftAssignments.IgnoreQueryFilters()
            .AnyAsync(a => a.TraineeUserId == TraineeT);
        traineeApplied.Should().BeFalse("no trainee is applied when its primary was not assigned");
    }

    [Fact]
    public async Task StageClear_On_Primary_Drops_Its_Staged_Trainee()
    {
        // Edge-case ruling 1: a trainee cannot outlive its primary. Clearing the primary drops the staged trainee.
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(PrimaryX, ShiftScopeOf());
        await svc.StageTraineeAsync(draft.Id, ShiftA, D, primaryUserId: PrimaryX, traineeUserId: TraineeT);
        await svc.StageClearAsync(draft.Id, ShiftA, D, PrimaryX); // clear the primary

        var overlay = await svc.GetOverlayAsync(draft.Id);
        overlay.Should().ContainSingle();
        overlay[0].UserIds.Should().NotContain(PrimaryX, "the primary was stage-cleared");
        overlay[0].Trainees.Should().NotContainKey(PrimaryX, "the staged trainee is dropped with its primary");

        var result = await life.CommitAsync(draft.Id, actingUserId: PrimaryX);
        result.Committed.Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await LiveSlotForAsync(f, PrimaryX)).Should().BeNull("the primary is cleared at commit");
        (await f.Db.ShiftAssignments.IgnoreQueryFilters().AnyAsync(a => a.TraineeUserId == TraineeT))
            .Should().BeFalse("no orphan trainee remains");
    }

    [Fact]
    public async Task Trainee_Drift_Skips_The_Cell()
    {
        // Edge-case ruling 4: a live trainee added by another user between enter and commit is drift → the
        // whole cell is skipped (per-cell policy), leaving the drifted live state intact.
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(PrimaryX, ShiftScopeOf());
        await svc.StageTraineeAsync(draft.Id, ShiftA, D, primaryUserId: PrimaryX, traineeUserId: TraineeT); // baseline trainees = {}

        // Another user shadows a DIFFERENT trainee onto the live primary after the draft entry.
        var liveSlot = await f.Db.ShiftAssignments.IgnoreQueryFilters().FirstAsync(a => a.Id == 5000);
        liveSlot.TraineeUserId = TraineeU;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var result = await life.CommitAsync(draft.Id, actingUserId: PrimaryX);
        result.Committed.Should().BeTrue();
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().ContainSingle();
        result.Skipped[0].RowId.Should().Be(ShiftA);

        f.Db.ChangeTracker.Clear();
        (await LiveSlotForAsync(f, PrimaryX))!.TraineeUserId.Should().Be(TraineeU, "a drifted cell keeps its live state");
    }

    [Fact]
    public async Task Commit_Widens_Capacity_Of_An_Existing_Full_Instance()
    {
        // Sub-project B: staging into a "full" cell must not touch live capacity; commit widens
        // StaffingRequired to fit the staged assignee count.
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, staffingRequired: 1); // full: 1 seat, PrimaryX already in it
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(PrimaryX, ShiftScopeOf());
        await svc.StageAssignAsync(draft.Id, ShiftA, D, PrimaryY); // staged {X, Y} → count 2 > capacity 1

        // Pre-commit: live capacity is untouched.
        (await f.Db.ShiftInstances.IgnoreQueryFilters().FirstAsync(i => i.Id == 1000)).StaffingRequired
            .Should().Be(1, "staging never writes live capacity");
        f.Db.ChangeTracker.Clear();

        var result = await life.CommitAsync(draft.Id, actingUserId: PrimaryX);
        result.Committed.Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await f.Db.ShiftInstances.IgnoreQueryFilters().FirstAsync(i => i.Id == 1000)).StaffingRequired
            .Should().Be(2, "commit widens capacity to fit the staged set");
        var assignedCount = await f.Db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.UserId != null && a.ShiftInstanceId == 1000);
        assignedCount.Should().Be(2, "both primaries are applied");
    }
}
