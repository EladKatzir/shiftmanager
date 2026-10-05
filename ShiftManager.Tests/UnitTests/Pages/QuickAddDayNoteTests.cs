using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;
using QuickAddDayNoteModel = ShiftManager.Pages.Api.Calendar.QuickAddDayNoteModel;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// POST /Api/Calendar/QuickAddDayNote attaches a note to the CALENDAR the user is looking at —
/// (molecule, job type, tab), all three named in the request.
///
/// Because the request can name any of the three, each is validated: the molecule against
/// <see cref="ShiftCalendarAccess"/> (can the caller view it at all), the job type against the set
/// that molecule can actually resolve, and the tab against the (molecule, job type) pair that owns
/// it. Without those, anyone holding the broad note-writing grant could key a note onto a calendar
/// they cannot see, or onto a combination that does not exist.
///
/// The endpoint ALWAYS INSERTS. It used to upsert and to treat empty text as a delete, which let any
/// WriteOverviewNotes holder silently overwrite or clear a colleague's note; deletion now lives in
/// DeleteDayNote.
///
/// This uses the REAL JobTypeService so the area-scoping predicate itself is under test, not a mock
/// of it. Seed: area 1 holds molecules 1 and 2 (Workforce) and 6 (Tech), with job type 1; area 2
/// holds molecule 5 with job type 9. Desks 1 and 2 are in molecule 1, desk 3 in molecule 2, desk 4 in
/// molecule 6. Tab 1 belongs to (molecule 1, job type 1); tab 2 to (molecule 2, job type 1).
/// </summary>
public sealed class QuickAddDayNoteTests
{
    private const int Caller = 7;
    private const int JtArea1 = 1, JtArea2 = 9;
    private const int MolTech = 6;

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.AddRange(
            new Area { Id = 1, ProjectId = 1, Name = "A1", DisplayName = "A1" },
            new Area { Id = 2, ProjectId = 1, Name = "A2", DisplayName = "A2" });
        db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1", Type = MoleculeType.Workforce },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2", Type = MoleculeType.Workforce },
            new Molecule { Id = 5, AreaId = 2, Name = "M5", DisplayName = "M5", Type = MoleculeType.Workforce },
            new Molecule { Id = MolTech, AreaId = 1, Name = "MT", DisplayName = "MT", Type = MoleculeType.Tech });
        db.Companies.AddRange(
            new Company { Id = 1, Name = "A", Slug = "a", DisplayName = "A", MoleculeId = 1 },
            new Company { Id = 2, Name = "B", Slug = "b", DisplayName = "B", MoleculeId = 1 },
            new Company { Id = 3, Name = "C", Slug = "c", DisplayName = "C", MoleculeId = 2 },
            new Company { Id = 4, Name = "T", Slug = "t", DisplayName = "T", MoleculeId = MolTech });
        db.Users.Add(new AppUser { Id = Caller, Email = "c@test", DisplayName = "Caller", CompanyId = 1, IsActive = true });
        db.JobTypes.AddRange(
            new JobType { Id = JtArea1, AreaId = 1, Name = "Alhut", DisplayName = "Alhut", IsActive = true, SortOrder = 1 },
            new JobType { Id = JtArea2, AreaId = 2, Name = "Elsewhere", DisplayName = "Elsewhere", IsActive = true, SortOrder = 1 });
        db.ShiftTabs.AddRange(
            new ShiftTab { Id = 1, MoleculeId = 1, JobTypeId = JtArea1, NameEn = "Geo", NameHe = "גאו", IsActive = true },
            new ShiftTab { Id = 2, MoleculeId = 2, JobTypeId = JtArea1, NameEn = "Other", NameHe = "אחר", IsActive = true });
        await db.SaveChangesAsync();
    }

    /// <summary>The model plus the collaborators a test may want to assert against.</summary>
    private sealed record Harness(QuickAddDayNoteModel Model, Mock<ICalendarNotificationService> Notifications);

    private static QuickAddDayNoteModel MakeModel(
        AppDbContext db, string body, int tenantCompanyId,
        IEnumerable<int> viewShiftsMolecules, bool canWriteNotes = true)
        => MakeHarness(db, body, tenantCompanyId, viewShiftsMolecules, canWriteNotes).Model;

    private static Harness MakeHarness(
        AppDbContext db, string body, int tenantCompanyId,
        IEnumerable<int> viewShiftsMolecules, bool canWriteNotes = true)
    {
        var grants = new Mock<IGrantService>();
        grants.Setup(g => g.HasCalendarNotePermissionAsync(Caller)).ReturnsAsync(canWriteNotes);
        grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(Caller, "ViewShifts"))
              .ReturnsAsync(viewShiftsMolecules.ToList());

        var tenant = new Mock<ITenantResolver>();
        tenant.Setup(t => t.GetCurrentTenantId()).Returns(tenantCompanyId);

        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        // The REAL service, so the area/molecule/workforce-only predicate is what gets tested.
        // GetJobTypesForMoleculeAsync touches only the DbContext, so the hierarchy service is unused.
        var jobTypes = new JobTypeService(
            db, Mock.Of<IHierarchyService>(), NullLogger<JobTypeService>.Instance, loc.Object);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, Caller.ToString()) }, "test"))
        };
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        var notifications = new Mock<ICalendarNotificationService>();

        var model = new QuickAddDayNoteModel(
            db,
            new CalendarDayNoteService(db),
            Mock.Of<IAuditLogService>(),
            grants.Object,
            jobTypes,
            notifications.Object,
            tenant.Object,
            NullLogger<QuickAddDayNoteModel>.Instance,
            loc.Object)
        {
            PageContext = new PageContext { HttpContext = http }
        };
        return new Harness(model, notifications);
    }

    private static int StatusOf(IActionResult r) => ((JsonResult)r).StatusCode ?? 200;

    private static Task<int> NoteCountAsync(AppDbContext db) =>
        db.CalendarDayNotes.IgnoreQueryFilters().CountAsync();

    private static async Task<SqliteDbContextFixture> SeedThenAsync()
    {
        var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        return f;
    }

    // --- the molecule gate ---

    [Fact]
    public async Task RequestWithoutAMolecule_IsRejected()
    {
        await using var f = await SeedThenAsync();
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x"}""", tenantCompanyId: 1, viewShiftsMolecules: new[] { 1 });

        StatusOf(await model.OnPostAsync()).Should().Be(400);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task MoleculeTheCallerCannotView_IsForbidden_AndNothingIsWritten()
    {
        await using var f = await SeedThenAsync();
        // Caller sits in desk 1 (molecule 1), has no ViewShifts reach, and names molecule 2.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":2,"jobTypeId":1}""", tenantCompanyId: 1, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task CallersOwnMolecule_IsWritable_WithoutAnyViewShiftsGrant()
    {
        await using var f = await SeedThenAsync();
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"Exercise day","moleculeId":1,"jobTypeId":1}""", tenantCompanyId: 1, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await model.OnPostAsync()).Should().Be(200);
        var note = await f.Db.CalendarDayNotes.IgnoreQueryFilters().SingleAsync();
        note.MoleculeId.Should().Be(1);
        note.JobTypeId.Should().Be(JtArea1);
        note.TabId.Should().BeNull("no tab was named, so the note belongs to the All view");
        note.CreatedByUserId.Should().Be(Caller);
    }

    [Fact]
    public async Task AnotherMoleculeReachedByTheGrant_IsWritable_AndRecordsTheWritersDesk()
    {
        await using var f = await SeedThenAsync();
        // Caller's active desk is 3 (molecule 2) but ViewShifts reaches molecule 1 — e.g. a director.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1,"jobTypeId":1}""", tenantCompanyId: 3, viewShiftsMolecules: new[] { 1 });

        StatusOf(await model.OnPostAsync()).Should().Be(200);
        var note = await f.Db.CalendarDayNotes.IgnoreQueryFilters().SingleAsync();
        note.MoleculeId.Should().Be(1, "the note belongs to the calendar being annotated");
        note.CompanyId.Should().Be(3, "the writer's desk is kept as provenance");
    }

    [Fact]
    public async Task WithoutNotePermission_IsForbidden()
    {
        await using var f = await SeedThenAsync();
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1,"jobTypeId":1}""", tenantCompanyId: 1, viewShiftsMolecules: new[] { 1 }, canWriteNotes: false);

        StatusOf(await model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    // --- the job-type gate ---

    [Fact]
    public async Task JobTypeFromAnotherArea_IsForbidden()
    {
        await using var f = await SeedThenAsync();
        // Job type 9 lives in area 2; molecule 1 is in area 1, so its calendar can never show it.
        // Accepting it would key the note to a calendar that does not exist.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1,"jobTypeId":9}""", tenantCompanyId: 1, viewShiftsMolecules: new[] { 1 });

        StatusOf(await model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task NonTechMoleculeWithoutAJobType_IsRejected()
    {
        await using var f = await SeedThenAsync();
        // (molecule, NULL, NULL) is how a TECH calendar's notes are encoded. Allowing a non-Tech
        // molecule to write that shape would put a row in the data whose meaning depends on the
        // molecule's Type rather than on the row itself.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1}""", tenantCompanyId: 1, viewShiftsMolecules: new[] { 1 });

        StatusOf(await model.OnPostAsync()).Should().Be(400);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task TechMoleculeWithAJobType_IsRejected()
    {
        await using var f = await SeedThenAsync();
        // The rule has to be TWO-SIDED. "null job type ⟹ Tech" alone is not enough, because a Tech
        // molecule still RESOLVES job types — GetJobTypesForMoleculeAsync only drops IsWorkforceOnly
        // ones, so molecule 6 resolves job type 1 here (and BR/Hakam/ProjectManager/Techno in the real
        // database). Meanwhile Shifts.cshtml.cs forces JobTypeId = null for every Tech calendar, so a
        // row at (6, 1, *) is read by NO calendar: invisible, with no × and therefore no delete path,
        // and — because the JobType FK is Restrict — it blocks deleting that job type forever.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":6,"jobTypeId":1}""", tenantCompanyId: 4, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await model.OnPostAsync()).Should().Be(400);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task TechMolecule_AcceptsANullJobType()
    {
        await using var f = await SeedThenAsync();
        // A Tech molecule's calendar genuinely runs with no job type, so NULL is correct here.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"tech note","moleculeId":6}""", tenantCompanyId: 4, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await model.OnPostAsync()).Should().Be(200);
        var note = await f.Db.CalendarDayNotes.IgnoreQueryFilters().SingleAsync();
        note.MoleculeId.Should().Be(MolTech);
        note.JobTypeId.Should().BeNull();
    }

    // --- the tab gate ---

    [Fact]
    public async Task TabBelongingToAnotherCalendar_IsForbidden()
    {
        await using var f = await SeedThenAsync();
        // Tab 2 belongs to molecule 2. Naming it while writing to molecule 1 would plant the note on
        // a tab the caller is not even looking at.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1,"jobTypeId":1,"tabId":2}""", tenantCompanyId: 1, viewShiftsMolecules: new[] { 1 });

        StatusOf(await model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task TabOnThisCalendar_IsAccepted_AndRecorded()
    {
        await using var f = await SeedThenAsync();
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"geo note","moleculeId":1,"jobTypeId":1,"tabId":1}""", tenantCompanyId: 1, viewShiftsMolecules: new[] { 1 });

        StatusOf(await model.OnPostAsync()).Should().Be(200);
        var note = await f.Db.CalendarDayNotes.IgnoreQueryFilters().SingleAsync();
        note.TabId.Should().Be(1);
    }

    // --- add semantics ---

    [Fact]
    public async Task EmptyText_IsRejected_RatherThanDeleting()
    {
        await using var f = await SeedThenAsync();
        await new CalendarDayNoteService(f.Db).AddDayNoteAsync(
            new DateOnly(2026, 9, 18), new CalendarScope(1, JtArea1, null), authorCompanyId: 2,
            "from desk 2", userId: Caller);

        // Empty text used to DELETE the day's note — which let anyone silently clear a colleague's
        // note with no ownership check, and wrote an audit row with entityId: 0 so the deletion was
        // unattributable. Deleting is now an explicit action against a specific note id.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"","moleculeId":1,"jobTypeId":1}""", tenantCompanyId: 1, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await model.OnPostAsync()).Should().Be(400);
        (await NoteCountAsync(f.Db)).Should().Be(1, "the existing note must survive an empty-text post");
    }

    [Fact]
    public async Task AddingANote_BroadcastsToTheShiftsGroupForThatMoleculeAndJobType()
    {
        await using var f = await SeedThenAsync();
        var h = MakeHarness(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1,"jobTypeId":1,"tabId":1}""", 1, new[] { 1 });

        StatusOf(await h.Model.OnPostAsync()).Should().Be(200);

        // The hub group is keyed to (molecule, jobType) only, so TabId has to travel in the payload
        // for a viewer on another tab of the same calendar to ignore the event.
        h.Notifications.Verify(n => n.NotifyDayNoteChangedAsync(
            "shifts-1-1",
            It.Is<CalendarDayNoteChangedEvent>(e =>
                e.MoleculeId == 1 && e.JobTypeId == 1 && e.TabId == 1 && e.ChangeType == "created")),
            Times.Once);
    }

    [Fact]
    public async Task ARealtimeFailure_DoesNotFailTheWriteTheUserJustMade()
    {
        await using var f = await SeedThenAsync();
        var h = MakeHarness(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1,"jobTypeId":1}""", 1, new[] { 1 });
        h.Notifications
            .Setup(n => n.NotifyDayNoteChangedAsync(It.IsAny<string>(), It.IsAny<CalendarDayNoteChangedEvent>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        StatusOf(await h.Model.OnPostAsync()).Should().Be(200, "the note is already saved; realtime is best-effort");
        (await NoteCountAsync(f.Db)).Should().Be(1);
    }

    [Fact]
    public async Task ADoubleSubmit_BroadcastsNothing_AndWritesNoSecondAuditRow()
    {
        await using var f = await SeedThenAsync();
        const string body = """{"date":"2026-09-18","text":"same text","moleculeId":1,"jobTypeId":1}""";

        var first = MakeHarness(f.Db, body, 1, new[] { 1 });
        StatusOf(await first.Model.OnPostAsync()).Should().Be(200);

        // Identical text from the same author moments later is a double-submit: the service returns
        // the EXISTING note and writes nothing. Reporting it as "created" would put a phantom event
        // in the audit trail and make every other viewer of the calendar re-render for nothing.
        var second = MakeHarness(f.Db, body, 1, new[] { 1 });
        StatusOf(await second.Model.OnPostAsync()).Should().Be(200, "the user's note IS on the calendar");

        (await NoteCountAsync(f.Db)).Should().Be(1);
        second.Notifications.Verify(n => n.NotifyDayNoteChangedAsync(
            It.IsAny<string>(), It.IsAny<CalendarDayNoteChangedEvent>()), Times.Never);
    }

    [Fact]
    public async Task TwoPostsOnTheSameDay_BothSurvive_RatherThanTheSecondOverwritingTheFirst()
    {
        await using var f = await SeedThenAsync();

        StatusOf(await MakeModel(f.Db, """{"date":"2026-09-18","text":"first","moleculeId":1,"jobTypeId":1}""", 1, new[] { 1 }).OnPostAsync())
            .Should().Be(200);
        StatusOf(await MakeModel(f.Db, """{"date":"2026-09-18","text":"second","moleculeId":1,"jobTypeId":1}""", 1, new[] { 1 }).OnPostAsync())
            .Should().Be(200);

        (await NoteCountAsync(f.Db)).Should().Be(2);
    }
}
