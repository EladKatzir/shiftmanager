using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for the chores Draft-Mode engine (Spec C): DraftChoreService is the chores
/// <see cref="IDraftReconciler"/> driven by the shared <see cref="DraftLifecycle"/>. Staging stays off the
/// live board; commit CREATES staged-not-live chores, SOFT-DELETES live-not-staged ones, PRESERVES those in
/// both, SKIPS drifted / unauthorized cells per-cell, and fires the chore create/cancel notifications +
/// chores-{molecule} broadcast. BusyService is mocked "clean" and the grant service "admin" so these exercise
/// the reconciliation/diff/conflict wiring, not the underlying chore rules (covered elsewhere).
/// </summary>
public sealed class DraftChoreServiceTests
{
    private static readonly DateOnly D = new(2026, 6, 15);
    private const int Owner = 10;   // the assigner owning the draft
    private const int UserA = 20;   // chore recipient (row)
    private const int Mol = 1;

    private static IBusyService CleanBusy()
    {
        var m = new Mock<IBusyService>();
        m.Setup(b => b.ValidateAsync(It.IsAny<BusyTarget>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new BusyValidation(true, System.Array.Empty<ShiftManager.Models.Validation.ValidationIssue>(),
                                                   System.Array.Empty<ShiftManager.Models.Validation.ValidationIssue>()));
        return m.Object;
    }

    private static IGrantService AdminGrant()
    {
        var g = new Mock<IGrantService>();
        g.Setup(x => x.HasGrantAsync(It.IsAny<int>(), "AdminAccess")).ReturnsAsync(true);
        g.Setup(x => x.HasGrantForCompanyAsync(It.IsAny<int>(), "AssignChores", It.IsAny<int>())).ReturnsAsync(true);
        return g.Object;
    }

    private static (DraftChoreService svc, DraftLifecycle life, Mock<INotificationService> notif) Build(
        SqliteDbContextFixture f, IGrantService? grant = null, IBusyService? busy = null)
    {
        var notif = new Mock<INotificationService>();
        var svc = new DraftChoreService(f.Db, busy ?? CleanBusy(), grant ?? AdminGrant(), notif.Object);
        var life = new DraftLifecycle(f.Db, new IDraftReconciler[] { svc });
        return (svc, life, notif);
    }

    private static DraftScope ChoreScope() => new(DraftSurface.Chores, Mol, null, null, D, D);

    private static async Task SeedAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = Mol, AreaId = 1, Name = "M", DisplayName = "M" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = Mol });
        f.Db.Users.AddRange(
            new AppUser { Id = Owner, CompanyId = 1, Email = "own@x.mil", DisplayName = "Owner" },
            new AppUser { Id = UserA, CompanyId = 1, Email = "a@x.mil", DisplayName = "A" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Chore SeedLiveChore(SqliteDbContextFixture f, string title, int id)
        => new Chore
        {
            Id = id, CompanyId = 1, MoleculeId = Mol, UserId = UserA, Date = D,
            Title = title, WeightMinutes = 480, CreatedBy = Owner, CreatedAt = System.DateTime.UtcNow
        };

    private static Task<List<Chore>> LiveChoresAsync(SqliteDbContextFixture f)
        => f.Db.Chores.IgnoreQueryFilters()
            .Where(c => c.UserId == UserA && c.Date == D && c.CanceledAt == null).ToListAsync();

    private static DraftChoreDescriptor Desc(string title) =>
        DraftChoreDescriptor.Create(null, title, null, null, null);

    [Fact]
    public async Task Staging_Does_Not_Touch_The_Live_Board()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life, _) = Build(f);

        var draft = await life.EnterAsync(Owner, ChoreScope());
        await svc.StageChoreAsync(draft.Id, UserA, D, Desc("Kitchen"));

        var overlay = await svc.GetChoreOverlayAsync(draft.Id);
        overlay.Should().ContainSingle();
        overlay[0].Descriptors.Should().ContainSingle().Which.Title.Should().Be("Kitchen");

        f.Db.ChangeTracker.Clear();
        (await LiveChoresAsync(f)).Should().BeEmpty("staging must not create a live chore");
    }

    [Fact]
    public async Task Commit_Creates_A_Staged_Chore_Into_An_Empty_Cell_And_Notifies()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life, notif) = Build(f);

        var draft = await life.EnterAsync(Owner, ChoreScope());
        await svc.StageChoreAsync(draft.Id, UserA, D, Desc("Kitchen"));

        var result = await life.CommitAsync(draft.Id, Owner);
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle().Which.RowId.Should().Be(UserA);
        result.ValidationIssues.Should().BeEmpty();

        f.Db.ChangeTracker.Clear();
        var live = await LiveChoresAsync(f);
        live.Should().ContainSingle().Which.Title.Should().Be("Kitchen");

        notif.Verify(n => n.CreateChoreAssignedNotificationAsync(UserA, "Kitchen", D, It.Is<int>(id => id > 0)), Times.Once);
        notif.Verify(n => n.CreateChoreCanceledNotificationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Commit_SoftDeletes_A_Live_Chore_That_Was_Staged_For_Removal_And_Notifies()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        f.Db.Chores.Add(SeedLiveChore(f, "Kitchen", 500));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var (svc, life, notif) = Build(f);

        var draft = await life.EnterAsync(Owner, ChoreScope());
        var key = Desc("Kitchen").Encode();
        await svc.ClearChoreAsync(draft.Id, UserA, D, key); // baseline {Kitchen}, staged {}

        var result = await life.CommitAsync(draft.Id, Owner);
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle();

        f.Db.ChangeTracker.Clear();
        (await LiveChoresAsync(f)).Should().BeEmpty("the staged-for-removal chore is soft-deleted");
        var canceled = await f.Db.Chores.IgnoreQueryFilters().FirstAsync(c => c.Id == 500);
        canceled.CanceledAt.Should().NotBeNull();
        canceled.CanceledBy.Should().Be(Owner);

        notif.Verify(n => n.CreateChoreCanceledNotificationAsync(UserA, "Kitchen", D, 500), Times.Once);
    }

    [Fact]
    public async Task Commit_Preserves_A_Descriptor_In_Both_And_Only_Creates_The_New_One()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        f.Db.Chores.Add(SeedLiveChore(f, "Kitchen", 500)); // live baseline
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var (svc, life, notif) = Build(f);

        var draft = await life.EnterAsync(Owner, ChoreScope());
        // Touch the cell and ADD a second chore; the existing one stays staged (baseline {Kitchen}, staged {Kitchen, Bathroom}).
        await svc.StageChoreAsync(draft.Id, UserA, D, Desc("Bathroom"));

        var result = await life.CommitAsync(draft.Id, Owner);
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle();

        f.Db.ChangeTracker.Clear();
        var live = await LiveChoresAsync(f);
        live.Select(c => c.Title).Should().BeEquivalentTo(new[] { "Kitchen", "Bathroom" });
        // The preserved chore keeps its original Id (not re-created).
        live.Should().Contain(c => c.Id == 500 && c.Title == "Kitchen");

        // Only the NEW descriptor fires an assigned notification; nothing is canceled.
        notif.Verify(n => n.CreateChoreAssignedNotificationAsync(UserA, "Bathroom", D, It.Is<int>(id => id > 0)), Times.Once);
        notif.Verify(n => n.CreateChoreAssignedNotificationAsync(UserA, "Kitchen", It.IsAny<DateOnly>(), It.IsAny<int>()), Times.Never);
        notif.Verify(n => n.CreateChoreCanceledNotificationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Commit_Skips_A_Drifted_Cell_But_Still_Commits_The_Session()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        f.Db.Chores.Add(SeedLiveChore(f, "Kitchen", 500));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var (svc, life, notif) = Build(f);

        var draft = await life.EnterAsync(Owner, ChoreScope());
        await svc.StageChoreAsync(draft.Id, UserA, D, Desc("Bathroom")); // baseline {Kitchen}

        // Someone else adds a live chore to the same cell after the draft first-touch → drift.
        f.Db.Chores.Add(SeedLiveChore(f, "Laundry", 501));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var result = await life.CommitAsync(draft.Id, Owner);
        result.Committed.Should().BeTrue("per-cell policy commits the session even when a cell drifted");
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.RowId.Should().Be(UserA);

        f.Db.ChangeTracker.Clear();
        (await LiveChoresAsync(f)).Select(c => c.Title).Should().BeEquivalentTo(new[] { "Kitchen", "Laundry" },
            "a drifted cell is skipped — the staged Bathroom is NOT created");
        notif.Verify(n => n.CreateChoreAssignedNotificationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Commit_Skips_A_Cell_Whose_Grant_Was_Revoked()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life, notif) = Build(f, grant: Mock.Of<IGrantService>()); // all grant checks false

        var draft = await life.EnterAsync(Owner, ChoreScope());
        await svc.StageChoreAsync(draft.Id, UserA, D, Desc("Kitchen"));

        var result = await life.CommitAsync(draft.Id, Owner);
        result.Committed.Should().BeTrue();
        result.Applied.Should().BeEmpty();
        result.Unauthorized.Should().ContainSingle().Which.RowId.Should().Be(UserA);

        f.Db.ChangeTracker.Clear();
        (await LiveChoresAsync(f)).Should().BeEmpty("an unauthorized cell applies nothing");
        notif.Verify(n => n.CreateChoreAssignedNotificationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Commit_HardError_Skips_The_Descriptor_And_Reports_An_Issue()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var busy = new Mock<IBusyService>();
        busy.Setup(b => b.ValidateAsync(It.IsAny<BusyTarget>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new BusyValidation(false,
                new[] { new ShiftManager.Models.Validation.ValidationIssue("CHORE_SHIFT_CONFLICT", "has a shift", ShiftManager.Models.Validation.ValidationSeverity.Error, ShiftManager.Models.Validation.ValidationCategory.JobType) },
                System.Array.Empty<ShiftManager.Models.Validation.ValidationIssue>()));
        var (svc, life, notif) = Build(f, busy: busy.Object);

        var draft = await life.EnterAsync(Owner, ChoreScope());
        await svc.StageChoreAsync(draft.Id, UserA, D, Desc("Kitchen"));

        var result = await life.CommitAsync(draft.Id, Owner);
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle("the cell is applied — its hard-error descriptor is skipped inside it");
        result.ValidationIssues.Should().ContainSingle().Which.Message.Should().Be("has a shift");

        f.Db.ChangeTracker.Clear();
        (await LiveChoresAsync(f)).Should().BeEmpty("the hard-error descriptor is not created");
        notif.Verify(n => n.CreateChoreAssignedNotificationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Commit_Reports_The_Chores_Molecule_Group()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life, _) = Build(f);

        var draft = await life.EnterAsync(Owner, ChoreScope());
        await svc.StageChoreAsync(draft.Id, UserA, D, Desc("Kitchen"));

        var result = await life.CommitAsync(draft.Id, Owner);
        result.NotifiedGroups.Should().BeEquivalentTo(new[] { "chores-1" });
    }

    [Fact]
    public async Task Discard_Removes_The_Sandbox_And_Leaves_Live_Untouched()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        f.Db.Chores.Add(SeedLiveChore(f, "Kitchen", 500));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var (svc, life, _) = Build(f);

        var draft = await life.EnterAsync(Owner, ChoreScope());
        await svc.StageChoreAsync(draft.Id, UserA, D, Desc("Bathroom"));

        await life.DiscardAsync(draft.Id, Owner);

        f.Db.ChangeTracker.Clear();
        (await f.Db.DraftSessions.CountAsync()).Should().Be(0);
        (await f.Db.DraftChoreCells.CountAsync()).Should().Be(0, "chore cells cascade with the session");
        (await LiveChoresAsync(f)).Select(c => c.Title).Should().BeEquivalentTo(new[] { "Kitchen" });
    }

    [Fact]
    public async Task FilteredUniqueIndex_Blocks_A_Second_Active_Chores_Draft_For_The_Same_Scope()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);

        f.Db.DraftSessions.Add(new DraftSession { OwnerUserId = Owner, Surface = DraftSurface.Chores, MoleculeId = Mol, WeekStart = D, WeekEnd = D, Status = DraftSessionStatus.Active });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        f.Db.DraftSessions.Add(new DraftSession { OwnerUserId = Owner, Surface = DraftSurface.Chores, MoleculeId = Mol, WeekStart = D, WeekEnd = D, Status = DraftSessionStatus.Active });

        var act = async () => await f.Db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("UX_DraftSessions_Active_Chores enforces one Active chores draft per scope");
    }
}
