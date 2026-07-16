using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Models.Validation;
using ShiftManager.Pages.Calendar;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Draft Mode (Epic 4) — shift-mode render overlay. Reproduces the bug where a staged assignment did not
/// appear on the by-shift board: <c>ApplyDraftOverlayAsync</c> built synthetic <see cref="ShiftAssignment"/>s
/// that (a) never populated the <c>User</c> nav — so <c>BuildCellsForShiftType</c>'s
/// <c>a.User?.DisplayName ?? Unassigned</c> fell back to "Unassigned", and (b) for an empty cell created a
/// synthetic <see cref="ShiftInstance"/> that was never added to the <c>instances</c> list the cell builder
/// resolves against — so the staged chip was dropped entirely.
///
/// Uses the real <see cref="DraftModeService"/> against real SQLite (:memory:) so staging + overlay run the
/// production code path; only the (commit-only) assignment validator is mocked. AppUsers ARE seeded because
/// the overlay resolves DisplayNames from the DB (Foreign Keys=False keeps the assignment→user link loose).
/// </summary>
public sealed class DraftOverlayRenderTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    private const int CompanyId = 1;
    private const int MoleculeId = 1;
    private const int OwnerUserId = 99;
    private static readonly DateOnly D = new(2026, 7, 15);

    // Shift types: 1 = has a live instance/assignment; 2 = empty cell (no instance).
    private const int LiveShiftTypeId = 1;
    private const int EmptyShiftTypeId = 2;

    // Users: Alice is the live assignee (baseline), Carol is staged into the live cell, Bob into the empty cell.
    private const int AliceId = 10, BobId = 11, CarolId = 12;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.ShiftTypes.AddRange(
            new ShiftType { Id = LiveShiftTypeId, CompanyId = CompanyId, Scope = ShiftScope.Company, Key = ShiftType.KEY_MORNING, Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0) },
            new ShiftType { Id = EmptyShiftTypeId, CompanyId = CompanyId, Scope = ShiftScope.Company, Key = "EVENING", Start = new TimeOnly(15, 0), End = new TimeOnly(23, 0) });
        _db.Users.AddRange(
            new AppUser { Id = AliceId, CompanyId = CompanyId, Email = "alice@x.mil", DisplayName = "Alice" },
            new AppUser { Id = BobId, CompanyId = CompanyId, Email = "bob@x.mil", DisplayName = "Bob" },
            new AppUser { Id = CarolId, CompanyId = CompanyId, Email = "carol@x.mil", DisplayName = "Carol" });
        // Live instance for shift type 1 on D, with Alice assigned. Shift type 2 has no instance at all.
        _db.ShiftInstances.Add(new ShiftInstance { Id = 1000, CompanyId = CompanyId, ShiftTypeId = LiveShiftTypeId, WorkDate = D, StaffingRequired = 1 });
        _db.ShiftAssignments.Add(new ShiftAssignment { Id = 5000, CompanyId = CompanyId, ShiftInstanceId = 1000, UserId = AliceId });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ShiftsModel BuildModel(IDraftModeService draftService)
    {
        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var model = new ShiftsModel(
            db: _db,
            calendarService: Mock.Of<IShiftCalendarService>(),
            grantService: Mock.Of<IGrantService>(),
            localizer: localizer.Object,
            companyLocalizationService: Mock.Of<ICompanyLocalizationService>(),
            tenantResolver: Mock.Of<ITenantResolver>(),
            logger: NullLogger<ShiftsModel>.Instance,
            jobTypeService: Mock.Of<IJobTypeService>(),
            traineeService: Mock.Of<ITraineeService>(),
            choreTypeService: Mock.Of<IChoreTypeService>(),
            textEntryService: Mock.Of<ICalendarTextEntryService>(),
            dayNoteService: Mock.Of<ICalendarDayNoteService>(),
            justiceService: Mock.Of<IJusticeService>(),
            distributionListService: Mock.Of<IDistributionListService>(),
            categoryService: Mock.Of<IShiftCategoryService>(),
            tabService: new ShiftTabService(_db),
            draftService: draftService,
            featureFlags: Mock.Of<IFeatureFlagService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, OwnerUserId.ToString()) }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.CurrentUserId = OwnerUserId;
        model.DraftMode = true;
        model.StartDate = D;
        model.EndDate = D;
        return model;
    }

    private static Mock<IShiftAssignmentService> CleanValidator()
    {
        var m = new Mock<IShiftAssignmentService>();
        m.Setup(v => v.ValidateShiftAssignmentAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ShiftAssignmentValidation(true, Array.Empty<ValidationIssue>(), Array.Empty<ValidationIssue>()));
        return m;
    }

    [Fact]
    public async Task StagedAssignments_RenderWithName_InBothEmptyAndLiveCells()
    {
        // --- Arrange: enter a draft and stage into an empty cell (shift 2) and a live cell (shift 1) ---
        var draftService = new DraftModeService(_db, CleanValidator().Object);
        var session = await draftService.EnterDraftAsync(OwnerUserId, MoleculeId, jobTypeId: null, weekStart: D, weekEnd: D);
        await draftService.StageAssignAsync(session.Id, EmptyShiftTypeId, D, BobId);   // empty cell → Bob
        await draftService.StageAssignAsync(session.Id, LiveShiftTypeId, D, CarolId);  // live cell (has Alice) → +Carol
        _db.ChangeTracker.Clear();

        var model = BuildModel(draftService);
        model.ShiftTypes = await _db.ShiftTypes.ToListAsync();

        // Production data shape: instances + assignments hydrated like GetShiftInstancesAsync/GetAssignmentsAsync.
        var instances = await _db.ShiftInstances.ToListAsync();
        var assignments = await _db.ShiftAssignments
            .Include(a => a.ShiftInstance).ThenInclude(si => si.ShiftType)
            .Include(a => a.User)
            .ToListAsync();

        // --- Act: overlay the draft, then build the shift-mode cells ---
        var overlaid = await model.ApplyDraftOverlayAsync(assignments, instances, MoleculeId, jobTypeId: null);

        var empty = new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>();
        var notes = new Dictionary<(int UserId, DateOnly Date), string>();
        var emptyCellRow = model.BuildCellsForShiftType(EmptyShiftTypeId, instances, overlaid, empty, notes);
        var liveCellRow = model.BuildCellsForShiftType(LiveShiftTypeId, instances, overlaid, empty, notes);

        // --- Assert ---
        model.ActiveDraftId.Should().Be(session.Id, "the overlay resolves and exposes the active draft");

        // Bug (b): empty cell staged Bob — previously rendered nothing because the synthetic instance was
        // never registered in `instances`.
        var emptyCellNames = emptyCellRow[D].Assignments.Select(a => a.Name).ToList();
        emptyCellNames.Should().ContainSingle().Which.Should().Be("Bob");

        // Bug (a): live cell shows baseline Alice + staged Carol — both by real name, never "Unassigned".
        var liveCellNames = liveCellRow[D].Assignments.Select(a => a.Name).ToList();
        liveCellNames.Should().BeEquivalentTo(new[] { "Alice", "Carol" });
        liveCellNames.Should().NotContain("Unassigned");
    }
}
