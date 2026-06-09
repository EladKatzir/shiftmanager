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
/// Real-SQLite coverage for <see cref="DraftModeService"/> (Epic 4 — Draft Mode): staging stays off the
/// live board, commit applies touched cells, optimistic per-cell conflict detection blocks a drifted
/// commit, and discard throws the sandbox away. The validator is mocked to "clean" so we exercise the
/// reconciliation/conflict logic, not the assignment rules (covered elsewhere).
/// </summary>
public sealed class DraftModeServiceTests
{
    private static readonly DateOnly D = new(2026, 6, 15);

    private static Mock<IShiftAssignmentService> CleanValidator()
    {
        var m = new Mock<IShiftAssignmentService>();
        m.Setup(v => v.ValidateShiftAssignmentAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ShiftAssignmentValidation(true, Array.Empty<ValidationIssue>(), Array.Empty<ValidationIssue>()));
        return m;
    }

    private static async Task SeedAsync(SqliteDbContextFixture f, bool assignUser10)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 });
        f.Db.ShiftTypes.Add(new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "MORNING" });
        f.Db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A" },
            new AppUser { Id = 11, CompanyId = 1, Email = "b@x.mil", DisplayName = "B" },
            new AppUser { Id = 12, CompanyId = 1, Email = "c@x.mil", DisplayName = "C" });
        // Live instance for shift 100 on D with two slots (one filled by user 10 when requested, one empty).
        f.Db.ShiftInstances.Add(new ShiftInstance { Id = 1000, CompanyId = 1, ShiftTypeId = 100, WorkDate = D, StaffingRequired = 2 });
        f.Db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 5000, CompanyId = 1, ShiftInstanceId = 1000, UserId = assignUser10 ? 10 : (int?)null },
            new ShiftAssignment { Id = 5001, CompanyId = 1, ShiftInstanceId = 1000, UserId = null });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Task<List<int>> LiveCellUsersAsync(SqliteDbContextFixture f)
        => f.Db.ShiftAssignments.IgnoreQueryFilters()
            .Where(a => a.UserId != null && a.ShiftInstance.ShiftTypeId == 100 && a.ShiftInstance.WorkDate == D)
            .Select(a => a.UserId!.Value).ToListAsync();

    [Fact]
    public async Task Staging_Does_Not_Touch_The_Live_Board()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var svc = new DraftModeService(f.Db, CleanValidator().Object);

        var draft = await svc.EnterDraftAsync(10, 1, null, D, D);
        await svc.StageAssignAsync(draft.Id, 100, D, 11);

        var overlay = await svc.GetOverlayAsync(draft.Id);
        overlay.Should().ContainSingle();
        overlay[0].UserIds.Should().BeEquivalentTo(new[] { 10, 11 });

        f.Db.ChangeTracker.Clear();
        (await LiveCellUsersAsync(f)).Should().BeEquivalentTo(new[] { 10 }, "staging must not alter the live board");
    }

    [Fact]
    public async Task Commit_Applies_Staged_Cells_To_Live()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var svc = new DraftModeService(f.Db, CleanValidator().Object);

        var draft = await svc.EnterDraftAsync(10, 1, null, D, D);
        await svc.StageAssignAsync(draft.Id, 100, D, 11);

        var result = await svc.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeTrue();
        result.Conflicts.Should().BeEmpty();
        result.AppliedCells.Should().Be(1);

        f.Db.ChangeTracker.Clear();
        (await LiveCellUsersAsync(f)).Should().BeEquivalentTo(new[] { 10, 11 });
    }

    [Fact]
    public async Task Commit_Detects_Conflict_When_Live_Drifted_And_Applies_Nothing()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var svc = new DraftModeService(f.Db, CleanValidator().Object);

        var draft = await svc.EnterDraftAsync(10, 1, null, D, D);
        await svc.StageAssignAsync(draft.Id, 100, D, 11); // baseline captured as {10}

        // Someone else changes the live cell after the draft entry: user 12 takes the empty slot.
        var emptySlot = await f.Db.ShiftAssignments.IgnoreQueryFilters().FirstAsync(a => a.Id == 5001);
        emptySlot.UserId = 12;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var result = await svc.CommitAsync(draft.Id, actingUserId: 10);
        result.Committed.Should().BeFalse();
        result.Conflicts.Should().ContainSingle();
        result.Conflicts[0].ShiftTypeId.Should().Be(100);

        f.Db.ChangeTracker.Clear();
        (await LiveCellUsersAsync(f)).Should().BeEquivalentTo(new[] { 10, 12 }, "a conflicting commit must apply nothing");
    }

    [Fact]
    public async Task Discard_Removes_The_Sandbox_And_Leaves_Live_Untouched()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: true);
        var svc = new DraftModeService(f.Db, CleanValidator().Object);

        var draft = await svc.EnterDraftAsync(10, 1, null, D, D);
        await svc.StageAssignAsync(draft.Id, 100, D, 11);
        await svc.StageClearAsync(draft.Id, 100, D, 10);

        await svc.DiscardAsync(draft.Id);

        f.Db.ChangeTracker.Clear();
        (await f.Db.DraftSessions.CountAsync()).Should().Be(0);
        (await f.Db.DraftCells.CountAsync()).Should().Be(0, "cells cascade with the session");
        (await LiveCellUsersAsync(f)).Should().BeEquivalentTo(new[] { 10 });
    }

    [Fact]
    public async Task EnterDraft_Is_Idempotent_For_The_Same_Owner_Molecule_Week()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f, assignUser10: false);
        var svc = new DraftModeService(f.Db, CleanValidator().Object);

        var a = await svc.EnterDraftAsync(10, 1, null, D, D);
        var b = await svc.EnterDraftAsync(10, 1, null, D, D);
        b.Id.Should().Be(a.Id, "re-entering returns the existing active sandbox");
        (await f.Db.DraftSessions.CountAsync()).Should().Be(1);
    }
}
