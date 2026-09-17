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
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;
using QuickAddDayNoteModel = ShiftManager.Pages.Api.Calendar.QuickAddDayNoteModel;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// POST /Api/Calendar/QuickAddDayNote now attaches the note to the molecule whose calendar the user
/// is looking at, named in the request.
///
/// Because a request can name ANY molecule id, the endpoint must prove the caller can view that
/// molecule's Shifts calendar — the same rule the calendar page uses to decide what to show
/// (<see cref="ShiftCalendarAccess"/>). Without that check, anyone holding the broad note-writing
/// grant could write onto every calendar in the deployment.
///
/// Seed: molecule 1 holds desks 1 and 2; molecule 2 holds desk 3. Caller is user 7.
/// </summary>
public sealed class QuickAddDayNoteTests
{
    private const int Caller = 7;

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        db.Companies.AddRange(
            new Company { Id = 1, Name = "A", Slug = "a", DisplayName = "A", MoleculeId = 1 },
            new Company { Id = 2, Name = "B", Slug = "b", DisplayName = "B", MoleculeId = 1 },
            new Company { Id = 3, Name = "C", Slug = "c", DisplayName = "C", MoleculeId = 2 });
        db.Users.Add(new AppUser { Id = Caller, Email = "c@test", DisplayName = "Caller", CompanyId = 1, IsActive = true });
        await db.SaveChangesAsync();
    }

    private static QuickAddDayNoteModel MakeModel(
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

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, Caller.ToString()) }, "test"))
        };
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return new QuickAddDayNoteModel(
            db,
            new CalendarDayNoteService(db),
            Mock.Of<IAuditLogService>(),
            grants.Object,
            tenant.Object,
            NullLogger<QuickAddDayNoteModel>.Instance,
            loc.Object)
        {
            PageContext = new PageContext { HttpContext = http }
        };
    }

    private static int StatusOf(IActionResult r) => ((JsonResult)r).StatusCode ?? 200;

    private static Task<int> NoteCountAsync(AppDbContext db) =>
        db.CalendarDayNotes.IgnoreQueryFilters().CountAsync();

    [Fact]
    public async Task RequestWithoutAMolecule_IsRejected()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x"}""", tenantCompanyId: 1, viewShiftsMolecules: new[] { 1 });

        StatusOf(await model.OnPostAsync()).Should().Be(400);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task MoleculeTheCallerCannotView_IsForbidden_AndNothingIsWritten()
    {
        await using var f = await SeedThenAsync();
        // Caller sits in desk 1 (molecule 1), has no ViewShifts reach, and names molecule 2.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":2}""", tenantCompanyId: 1, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task CallersOwnMolecule_IsWritable_WithoutAnyViewShiftsGrant()
    {
        await using var f = await SeedThenAsync();
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"Exercise day","moleculeId":1}""", tenantCompanyId: 1, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await model.OnPostAsync()).Should().Be(200);
        var note = await f.Db.CalendarDayNotes.IgnoreQueryFilters().SingleAsync();
        note.MoleculeId.Should().Be(1);
        note.CreatedByUserId.Should().Be(Caller);
    }

    [Fact]
    public async Task AnotherMoleculeReachedByTheGrant_IsWritable_AndRecordsTheWritersDesk()
    {
        await using var f = await SeedThenAsync();
        // Caller's active desk is 3 (molecule 2) but ViewShifts reaches molecule 1 — e.g. a director.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1}""", tenantCompanyId: 3, viewShiftsMolecules: new[] { 1 });

        StatusOf(await model.OnPostAsync()).Should().Be(200);
        var note = await f.Db.CalendarDayNotes.IgnoreQueryFilters().SingleAsync();
        note.MoleculeId.Should().Be(1, "the note belongs to the calendar being annotated");
        note.CompanyId.Should().Be(3, "the writer's desk is kept as provenance");
    }

    [Fact]
    public async Task WithoutNotePermission_IsForbidden()
    {
        await using var f = await SeedThenAsync();
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"x","moleculeId":1}""", tenantCompanyId: 1, viewShiftsMolecules: new[] { 1 }, canWriteNotes: false);

        StatusOf(await model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task EmptyText_DeletesTheMoleculesNote()
    {
        await using var f = await SeedThenAsync();
        await new CalendarDayNoteService(f.Db).SetDayNoteAsync(new DateOnly(2026, 9, 18), 1, 2, "from desk 2", Caller);

        // Deleting from desk 1 must hit the SAME molecule-keyed note that desk 2 wrote.
        var model = MakeModel(f.Db, """{"date":"2026-09-18","text":"","moleculeId":1}""", tenantCompanyId: 1, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await model.OnPostAsync()).Should().Be(200);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    private static async Task<SqliteDbContextFixture> SeedThenAsync()
    {
        var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        return f;
    }
}
