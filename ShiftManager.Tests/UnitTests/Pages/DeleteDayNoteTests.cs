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
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;
using DeleteDayNoteModel = ShiftManager.Pages.Api.Calendar.DeleteDayNoteModel;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// POST /Api/Calendar/DeleteDayNote removes ONE note by id.
///
/// It replaces the old "post empty text to QuickAddDayNote" behaviour, which let any holder of the
/// broad WriteOverviewNotes grant silently clear a colleague's note with no ownership check, and
/// logged the deletion with entityId: 0 so it was unattributable
/// (docs/superpowers/audit/cluster-1-calendars.md:644-647).
///
/// The rule now: you may delete a note if you WROTE it, or if you hold AssignShifts scoped to the
/// calendar it belongs to.
///
/// The single most important test here is <see cref="LegacyNoteWithNullMolecule_IsNotFound_AndNoGrantIsConsulted"/>.
/// MoleculeId and JobTypeId are both nullable, so passing a legacy note's nulls into
/// HasGrantWithScopeAsync produces an ALL-NULL scope, and GrantService's project/area/molecule
/// branches each return true for any holder in their own hierarchy — turning the ownership rule into
/// "any assigner anywhere may delete it".
///
/// Seed: molecule 1 holds desks 1 and 2; molecule 2 holds desk 3. Author is user 7, other is user 8.
/// </summary>
public sealed class DeleteDayNoteTests
{
    private const int Author = 7, Other = 8;
    private const int Mol1 = 1, Mol2 = 2;
    private const int JtAlhut = 1;

    private static readonly DateOnly D = new(2026, 9, 18);

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        db.Molecules.AddRange(
            new Molecule { Id = Mol1, AreaId = 1, Name = "M1", DisplayName = "M1", Type = MoleculeType.Workforce },
            new Molecule { Id = Mol2, AreaId = 1, Name = "M2", DisplayName = "M2", Type = MoleculeType.Workforce });
        db.Companies.AddRange(
            new Company { Id = 1, Name = "A", Slug = "a", DisplayName = "A", MoleculeId = Mol1 },
            new Company { Id = 2, Name = "B", Slug = "b", DisplayName = "B", MoleculeId = Mol1 },
            new Company { Id = 3, Name = "C", Slug = "c", DisplayName = "C", MoleculeId = Mol2 });
        db.Users.AddRange(
            new AppUser { Id = Author, Email = "a@test", DisplayName = "Author", CompanyId = 1, IsActive = true },
            new AppUser { Id = Other, Email = "o@test", DisplayName = "Other", CompanyId = 1, IsActive = true });
        db.JobTypes.Add(new JobType { Id = JtAlhut, AreaId = 1, Name = "Alhut", DisplayName = "Alhut", IsActive = true, SortOrder = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedNoteAsync(
        AppDbContext db, int? moleculeId = Mol1, int? jobTypeId = JtAlhut, int? createdBy = Author)
    {
        var note = new CalendarDayNote
        {
            Date = D, Text = "note", CompanyId = 1,
            MoleculeId = moleculeId, JobTypeId = jobTypeId, TabId = null,
            CreatedByUserId = createdBy
        };
        db.CalendarDayNotes.Add(note);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return note.Id;
    }

    private sealed record Harness(DeleteDayNoteModel Model, Mock<IGrantService> Grants);

    private static Harness MakeModel(
        AppDbContext db, int noteId, int callerId,
        bool canWriteNotes = true,
        IEnumerable<int>? viewShiftsMolecules = null,
        bool hasAssignShifts = false,
        int tenantCompanyId = 1)
    {
        var grants = new Mock<IGrantService>(MockBehavior.Strict);
        grants.Setup(g => g.HasCalendarNotePermissionAsync(callerId)).ReturnsAsync(canWriteNotes);
        grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(callerId, "ViewShifts"))
              .ReturnsAsync((viewShiftsMolecules ?? Array.Empty<int>()).ToList());
        grants.Setup(g => g.HasGrantWithScopeAsync(
                  callerId, "AssignShifts",
                  It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                  It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()))
              .ReturnsAsync(hasAssignShifts);

        var tenant = new Mock<ITenantResolver>();
        tenant.Setup(t => t.GetCurrentTenantId()).Returns(tenantCompanyId);

        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
        };
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"noteId\":{noteId}}}"));

        var model = new DeleteDayNoteModel(
            db,
            new CalendarDayNoteService(db),
            Mock.Of<IAuditLogService>(),
            grants.Object,
            tenant.Object,
            NullLogger<DeleteDayNoteModel>.Instance,
            loc.Object)
        {
            PageContext = new PageContext { HttpContext = http }
        };
        return new Harness(model, grants);
    }

    private static int StatusOf(IActionResult r) => ((JsonResult)r).StatusCode ?? 200;

    private static Task<int> NoteCountAsync(AppDbContext db) =>
        db.CalendarDayNotes.IgnoreQueryFilters().CountAsync();

    [Fact]
    public async Task RequestWithoutANoteId_IsRejected()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var h = MakeModel(f.Db, noteId: 0, callerId: Author, viewShiftsMolecules: new[] { Mol1 });

        StatusOf(await h.Model.OnPostAsync()).Should().Be(400);
    }

    [Fact]
    public async Task WithoutNotePermission_IsForbidden_AndNothingIsDeleted()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db);
        var h = MakeModel(f.Db, id, Author, canWriteNotes: false, viewShiftsMolecules: new[] { Mol1 });

        StatusOf(await h.Model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(1);
    }

    [Fact]
    public async Task UnknownNoteId_IsNotFound()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var h = MakeModel(f.Db, noteId: 424242, callerId: Author, viewShiftsMolecules: new[] { Mol1 });

        StatusOf(await h.Model.OnPostAsync()).Should().Be(404);
    }

    [Fact]
    public async Task DeletingAnAlreadyDeletedNote_IsNotFound()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db);

        var first = MakeModel(f.Db, id, Author, viewShiftsMolecules: new[] { Mol1 });
        StatusOf(await first.Model.OnPostAsync()).Should().Be(200);

        // The double-click path. The client treats 404 as success so the second click does not raise
        // an error toast at the user.
        var second = MakeModel(f.Db, id, Author, viewShiftsMolecules: new[] { Mol1 });
        StatusOf(await second.Model.OnPostAsync()).Should().Be(404);
    }

    [Fact]
    public async Task LegacyNoteWithNullMolecule_IsNotFound_AndNoGrantIsConsulted()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db, moleculeId: null, jobTypeId: null);

        // Caller is NOT the author and has no assign grant, so the only thing that can save this note
        // is the null-molecule guard running BEFORE the grant call.
        var h = MakeModel(f.Db, id, Other, viewShiftsMolecules: new[] { Mol1 }, hasAssignShifts: false);

        StatusOf(await h.Model.OnPostAsync()).Should().Be(404,
            "a note on no calendar is not found, rather than evaluated for permission");
        (await NoteCountAsync(f.Db)).Should().Be(1);

        // THE point of this test. An all-null scope makes HasGrantWithScopeAsync permissive, so the
        // endpoint must never reach it with a legacy note's nulls.
        h.Grants.Verify(g => g.HasGrantWithScopeAsync(
            It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task NoteOnAMoleculeTheCallerCannotView_IsForbidden()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db, moleculeId: Mol2, jobTypeId: JtAlhut, createdBy: Author);

        // Caller is the AUTHOR, so this proves the molecule gate is checked before ownership: you
        // cannot reach into a calendar you cannot see, even for your own note.
        var h = MakeModel(f.Db, id, Author, tenantCompanyId: 1, viewShiftsMolecules: Array.Empty<int>());

        StatusOf(await h.Model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(1);
    }

    [Fact]
    public async Task Author_CanDeleteTheirOwnNote_WithoutAssignShifts()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db, createdBy: Author);

        // The caller's own desk is in molecule 1, which ShiftCalendarAccess always includes — so no
        // ViewShifts grant is needed either.
        var h = MakeModel(f.Db, id, Author, viewShiftsMolecules: Array.Empty<int>(), hasAssignShifts: false);

        StatusOf(await h.Model.OnPostAsync()).Should().Be(200);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task NonAuthorWithoutAssignShifts_IsForbidden()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db, createdBy: Author);

        // This is the audit finding: before this endpoint existed, any WriteOverviewNotes holder
        // could clear this note. WriteOverviewNotes is seeded to the Employee template.
        var h = MakeModel(f.Db, id, Other, hasAssignShifts: false);

        StatusOf(await h.Model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(1);
    }

    [Fact]
    public async Task NonAuthorWithAssignShiftsOnThatCalendar_CanDelete()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db, createdBy: Author);

        var h = MakeModel(f.Db, id, Other, hasAssignShifts: true);

        StatusOf(await h.Model.OnPostAsync()).Should().Be(200);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }

    [Fact]
    public async Task NoteWithANullAuthor_CannotBeDeletedByANonAssigner()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db, createdBy: null);

        // After the author is deleted the FK nulls this column. Nobody then satisfies the ownership
        // branch — which is the correct outcome: an orphaned note is removable only by an assigner.
        var h = MakeModel(f.Db, id, Other, hasAssignShifts: false);

        StatusOf(await h.Model.OnPostAsync()).Should().Be(403);
        (await NoteCountAsync(f.Db)).Should().Be(1);
    }

    [Fact]
    public async Task NoteWithANullAuthor_CanStillBeDeletedByAnAssigner()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f.Db);
        var id = await SeedNoteAsync(f.Db, createdBy: null);

        var h = MakeModel(f.Db, id, Other, hasAssignShifts: true);

        StatusOf(await h.Model.OnPostAsync()).Should().Be(200);
        (await NoteCountAsync(f.Db)).Should().Be(0);
    }
}
