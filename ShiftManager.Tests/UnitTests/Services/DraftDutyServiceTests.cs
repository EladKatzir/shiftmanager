using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for the on-call Draft Mode engine (sub-project D): <see cref="DraftDutyService"/> is
/// the on-call <see cref="IDraftReconciler"/> driven by the shared <see cref="DraftLifecycle"/>. A cell is
/// (dutyTypeValue, date) → a set of users, and the baseline / staged / live sets are ALWAYS computed over the
/// GLOBAL active <see cref="OnDuty"/> set — the session's AreaId is a UI hint only. These tests exercise
/// staging privacy, stage/commit/discard, the GLOBAL-baseline landmine (an out-of-area user on the same
/// (type,date) is preserved through commit), per-cell drift-skip, per-duty-type re-authorization at commit,
/// rank-ineligible skip-with-issue, and on-call SignalR parity (no broadcast).
/// </summary>
public sealed class DraftDutyServiceTests
{
    private static readonly DateOnly D = new(2026, 6, 15);
    private const int Hakam = (int)OnDutyType.Hakam; // 0
    private const int Lead = (int)OnDutyType.Lead;   // 1 — requires officer rank
    private const int InArea = 1;
    private const int OutOfArea = 2;

    // ---- builders -------------------------------------------------------------------------------------

    private static IGrantService OnCallGrant(bool authorized = true)
    {
        var g = new Mock<IGrantService>();
        g.Setup(x => x.HasGrantAsync(It.IsAny<int>(), "AssignHakamDuties")).ReturnsAsync(authorized);
        g.Setup(x => x.HasGrantAsync(It.IsAny<int>(), "AssignKatzinDuties")).ReturnsAsync(authorized);
        g.Setup(x => x.HasGrantAsync(It.IsAny<int>(), "ManageOnDuty")).ReturnsAsync(authorized);
        g.Setup(x => x.HasGrantAsync(It.IsAny<int>(), "EditOnCallCalendar")).ReturnsAsync(authorized);
        return g.Object;
    }

    private static IFeatureFlagService RankEnforcement(bool on)
    {
        var f = new Mock<IFeatureFlagService>();
        f.Setup(x => x.IsEnabledAsync(FeatureFlagSeed.Flags.EnforceRankEligibility, It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(on);
        return f.Object;
    }

    private static OnDutyService BuildOnDutyService(AppDbContext db, IGrantService grant, IFeatureFlagService? ff = null)
    {
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(x => x.HttpContext).Returns((HttpContext?)null); // commit uses actingUserId, never HttpContext
        return new OnDutyService(
            db,
            http.Object,
            new Mock<IDirectorService>().Object,
            grant,
            Mock.Of<ILogger<OnDutyService>>(),
            ff ?? Mock.Of<IFeatureFlagService>(),
            BusyServiceMockFactory.NoOp());
    }

    private static (DraftDutyService reconciler, DraftLifecycle lifecycle) Build(
        SqliteDbContextFixture f, IGrantService? grant = null, IFeatureFlagService? ff = null)
    {
        var onDuty = BuildOnDutyService(f.Db, grant ?? OnCallGrant(), ff);
        var reconciler = new DraftDutyService(f.Db, onDuty);
        var lifecycle = new DraftLifecycle(f.Db, new IDraftReconciler[] { reconciler });
        return (reconciler, lifecycle);
    }

    private static DraftScope OnCallScope(int? areaId = InArea) =>
        new(DraftSurface.OnCall, MoleculeId: null, JobTypeId: null, AreaId: areaId, WeekStart: D, WeekEnd: D);

    /// <summary>Seed a two-area hierarchy: users 10/11 in area 1, user 99 in area 2 (out-of-area).</summary>
    private static async Task SeedAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.AddRange(
            new Area { Id = InArea, ProjectId = 1, Name = "A1", DisplayName = "A1" },
            new Area { Id = OutOfArea, ProjectId = 1, Name = "A2", DisplayName = "A2" });
        f.Db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = InArea, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = OutOfArea, Name = "M2", DisplayName = "M2" });
        f.Db.Companies.AddRange(
            new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 },
            new Company { Id = 2, Name = "Co2", Slug = "co2", MoleculeId = 2 });
        f.Db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "Owner", Rank = MilitaryRank.Seren },
            new AppUser { Id = 11, CompanyId = 1, Email = "b@x.mil", DisplayName = "InArea", Rank = MilitaryRank.Turai },
            new AppUser { Id = 99, CompanyId = 2, Email = "z@x.mil", DisplayName = "OutOfArea", Rank = MilitaryRank.Seren });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task AddLiveOnDutyAsync(SqliteDbContextFixture f, int userId, int typeValue, DateOnly date)
    {
        f.Db.OnDuties.Add(new OnDuty { UserId = userId, Type = (OnDutyType)typeValue, Date = date, CreatedBy = 10 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Task<List<int>> LiveUsersAsync(SqliteDbContextFixture f, int typeValue, DateOnly date)
    {
        var type = (OnDutyType)typeValue;
        return f.Db.OnDuties.IgnoreQueryFilters()
            .Where(o => o.Type == type && o.Date == date && o.CanceledAt == null)
            .Select(o => o.UserId).ToListAsync();
    }

    // ---- tests ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Staging_Does_Not_Touch_The_Live_OnDuties()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        await AddLiveOnDutyAsync(f, userId: 99, Hakam, D); // a live global assignment
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, OnCallScope());
        await svc.StageDutyAssignAsync(draft.Id, Hakam, D, userId: 11);

        var overlay = await svc.GetDutyOverlayAsync(draft.Id);
        overlay.Should().ContainSingle();
        overlay[0].UserIds.Should().BeEquivalentTo(new[] { 99, 11 }, "the staged set folds onto the global baseline");

        (await LiveUsersAsync(f, Hakam, D)).Should().BeEquivalentTo(new[] { 99 }, "staging must not alter the live board");
    }

    [Fact]
    public async Task Commit_Applies_A_Staged_Assignment_Creating_A_Global_OnDuty()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, OnCallScope());
        await svc.StageDutyAssignAsync(draft.Id, Hakam, D, userId: 11); // empty cell → baseline {}

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);

        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle();
        result.Applied[0].RowId.Should().Be(Hakam);
        result.Skipped.Should().BeEmpty();
        result.Unauthorized.Should().BeEmpty();
        result.ValidationIssues.Should().BeEmpty();

        (await LiveUsersAsync(f, Hakam, D)).Should().BeEquivalentTo(new[] { 11 });
    }

    [Fact]
    public async Task Commit_A_Staged_Clear_SoftDeletes_The_Live_OnDuty()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        await AddLiveOnDutyAsync(f, userId: 11, Hakam, D);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, OnCallScope());
        await svc.StageDutyClearAsync(draft.Id, Hakam, D, userId: 11); // baseline {11} → staged {}

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle();

        (await LiveUsersAsync(f, Hakam, D)).Should().BeEmpty("the staged clear soft-deletes the row");
        var row = await f.Db.OnDuties.IgnoreQueryFilters().FirstAsync(o => o.UserId == 11 && o.Type == OnDutyType.Hakam && o.Date == D);
        row.CanceledAt.Should().NotBeNull();
        row.CanceledBy.Should().Be(10, "the acting user is recorded as the canceler");
    }

    /// <summary>
    /// THE SCOPE LANDMINE (Spec D §3). The baseline for (Hakam, D) is captured over the GLOBAL active set, so
    /// an out-of-area user (99) already on that (type,date) is part of the baseline AND the staged set. Adding
    /// an in-area user (11) must NOT clobber 99: after commit BOTH remain. Had the baseline been captured over
    /// the area-filtered subset, 99 would be absent from the staged set and the commit's live−staged diff would
    /// have canceled 99.
    /// </summary>
    [Fact]
    public async Task GlobalBaseline_Preserves_An_OutOfArea_User_On_The_Same_TypeDate_Through_Commit()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        await AddLiveOnDutyAsync(f, userId: 99, Hakam, D); // out-of-area, global live
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, OnCallScope(areaId: InArea)); // owner "drafts area 1"
        await svc.StageDutyAssignAsync(draft.Id, Hakam, D, userId: 11);

        // Baseline was captured GLOBALLY (must include the out-of-area 99).
        var cell = await f.Db.DraftDutyCells.FirstAsync(c => c.DraftSessionId == draft.Id);
        DraftCell.Decode(cell.BaselineUserIds).Should().BeEquivalentTo(new[] { 99 },
            "baseline is the GLOBAL active set, not the area-filtered subset");

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue();
        result.Applied.Should().ContainSingle();

        (await LiveUsersAsync(f, Hakam, D)).Should().BeEquivalentTo(new[] { 99, 11 },
            "the out-of-area user is preserved; only the in-area addition is applied");
    }

    [Fact]
    public async Task Commit_Skips_A_Drifted_Cell_But_Still_Commits_The_Session()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, OnCallScope());
        await svc.StageDutyAssignAsync(draft.Id, Hakam, D, userId: 11); // baseline {} captured

        // Another assigner adds user 99 to the same global cell after the draft touched it.
        await AddLiveOnDutyAsync(f, userId: 99, Hakam, D);

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue("per-cell policy commits the session even when a cell drifted");
        result.Applied.Should().BeEmpty();
        result.Skipped.Should().ContainSingle();
        result.Skipped[0].RowId.Should().Be(Hakam);

        (await LiveUsersAsync(f, Hakam, D)).Should().BeEquivalentTo(new[] { 99 },
            "a drifted cell is skipped — the staged addition is NOT applied");
        (await f.Db.DraftSessions.FirstAsync(d => d.Id == draft.Id)).Status.Should().Be(DraftSessionStatus.Committed);
    }

    [Fact]
    public async Task Commit_Skips_A_Cell_Whose_OnCall_Grant_Was_Revoked()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life) = Build(f, grant: OnCallGrant(authorized: false)); // no on-call grant at commit

        var draft = await life.EnterAsync(10, OnCallScope());
        await svc.StageDutyAssignAsync(draft.Id, Hakam, D, userId: 11);

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue();
        result.Applied.Should().BeEmpty();
        result.Unauthorized.Should().ContainSingle();
        result.Unauthorized[0].RowId.Should().Be(Hakam);

        (await LiveUsersAsync(f, Hakam, D)).Should().BeEmpty("an unauthorized cell applies nothing");
    }

    [Fact]
    public async Task Commit_Skips_A_RankIneligible_Lead_User_With_A_ValidationIssue()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        // Rank enforcement ON; Lead requires an officer; user 11 is a Turai (non-officer).
        var (svc, life) = Build(f, ff: RankEnforcement(on: true));

        var draft = await life.EnterAsync(10, OnCallScope());
        await svc.StageDutyAssignAsync(draft.Id, Lead, D, userId: 11);

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue();
        // The CELL is authorized + not drifted, so it is "applied"; the single rank-ineligible user is skipped
        // and surfaced as a per-user issue (auto-skip-and-report).
        result.Applied.Should().ContainSingle();
        result.ValidationIssues.Should().ContainSingle();
        result.ValidationIssues[0].UserId.Should().Be(11);
        result.ValidationIssues[0].Message.Should().Be("OFFICER_RANK_REQUIRED");

        (await LiveUsersAsync(f, Lead, D)).Should().BeEmpty("the rank-ineligible user is not written live");
    }

    [Fact]
    public async Task Commit_Reports_No_NotifiedGroups_OnCall_SignalR_Parity()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, OnCallScope());
        await svc.StageDutyAssignAsync(draft.Id, Hakam, D, userId: 11);

        var result = await life.CommitAsync(draft.Id, actingUserId: 10);
        result.NotifiedGroups.Should().BeEmpty(
            "the live on-call write path broadcasts nothing today (NotifyOnCallChanged unused) — draft commit keeps parity");
    }

    [Fact]
    public async Task Discard_Removes_The_Sandbox_And_Leaves_Live_Untouched()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        await AddLiveOnDutyAsync(f, userId: 99, Hakam, D);
        var (svc, life) = Build(f);

        var draft = await life.EnterAsync(10, OnCallScope());
        await svc.StageDutyAssignAsync(draft.Id, Hakam, D, userId: 11);
        await svc.StageDutyClearAsync(draft.Id, Hakam, D, userId: 99);

        await life.DiscardAsync(draft.Id, ownerUserId: 10);

        (await f.Db.DraftSessions.CountAsync()).Should().Be(0);
        (await f.Db.DraftDutyCells.CountAsync()).Should().Be(0, "cells cascade with the session");
        (await LiveUsersAsync(f, Hakam, D)).Should().BeEquivalentTo(new[] { 99 }, "discard restores nothing live-side");
    }

    [Fact]
    public async Task Enter_Is_Idempotent_For_The_Same_OnCall_Scope()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var (svc, life) = Build(f);

        var a = await life.EnterAsync(10, OnCallScope());
        var b = await life.EnterAsync(10, OnCallScope());
        b.Id.Should().Be(a.Id, "re-entering returns the existing active on-call sandbox");
        (await f.Db.DraftSessions.CountAsync()).Should().Be(1);

        // GetActiveDraftAsync resolves the same session by (owner, area, week).
        (await svc.GetActiveDraftAsync(10, InArea, D))!.Id.Should().Be(a.Id);
    }
}
