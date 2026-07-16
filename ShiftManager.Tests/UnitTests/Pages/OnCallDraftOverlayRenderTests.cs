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
using ShiftManager.Pages.Calendar;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Draft Mode (sub-project D) — on-call render overlay. Verifies that when a viewer is in Draft Mode, the
/// on-call board renders the viewer's STAGED (dutyType, date) users (as synthetic Id=0 rows carrying the real
/// DisplayName) in place of the touched cell's live set — and that a GLOBALLY-live out-of-area user already on
/// that (type, date) is preserved in the overlay (Spec D §3: the staged set folds onto the global baseline).
/// Untouched cells keep rendering their live assignments. Runs the real <see cref="DraftDutyService"/> +
/// <see cref="OnCallModel.ApplyDraftOverlayAsync"/>/<c>BuildCellsForDutyType</c> against real SQLite.
/// </summary>
public sealed class OnCallDraftOverlayRenderTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    private const int OwnerUserId = 99;
    private const int Hakam = (int)OnDutyType.Hakam;
    private const int Lead = (int)OnDutyType.Lead;
    private static readonly DateOnly D = new(2026, 6, 15);

    // Alice is globally live on Hakam@D (out-of-area); Bob is staged into that same cell; Carol into empty Lead.
    private const int AliceId = 10, BobId = 11, CarolId = 12;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Users.AddRange(
            new AppUser { Id = AliceId, CompanyId = 1, Email = "alice@x.mil", DisplayName = "Alice" },
            new AppUser { Id = BobId, CompanyId = 1, Email = "bob@x.mil", DisplayName = "Bob" },
            new AppUser { Id = CarolId, CompanyId = 1, Email = "carol@x.mil", DisplayName = "Carol" });
        _db.OnDuties.Add(new OnDuty { Id = 5000, UserId = AliceId, Type = OnDutyType.Hakam, Date = D, CreatedBy = OwnerUserId });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private OnCallModel BuildModel(IDraftDutyService draftDutyService)
    {
        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var model = new OnCallModel(
            db: _db,
            onDutyService: Mock.Of<IOnDutyService>(),
            grantService: Mock.Of<IGrantService>(),
            companyContext: Mock.Of<ICompanyContext>(),
            localizer: localizer.Object,
            logger: NullLogger<OnCallModel>.Instance,
            textEntryService: Mock.Of<ICalendarTextEntryService>(),
            justiceService: Mock.Of<IJusticeService>(),
            draftDutyService: draftDutyService,
            draftLifecycle: Mock.Of<IDraftLifecycle>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, OwnerUserId.ToString()) }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.CurrentUserId = OwnerUserId;
        model.DraftMode = true;
        model.AreaId = 1; // a UI hint only — overlay is global regardless
        model.StartDate = D;
        model.EndDate = D;
        return model;
    }

    [Fact]
    public async Task StagedDuties_Render_With_Name_And_Preserve_The_Global_LiveUser()
    {
        // Arrange: enter a draft; stage Bob into the (globally live) Hakam cell and Carol into the empty Lead cell.
        var draftDuty = new DraftDutyService(_db, Mock.Of<IOnDutyService>());
        var lifecycle = new DraftLifecycle(_db, new IDraftReconciler[] { draftDuty });
        var session = await lifecycle.EnterAsync(OwnerUserId,
            new DraftScope(DraftSurface.OnCall, MoleculeId: null, JobTypeId: null, AreaId: 1, WeekStart: D, WeekEnd: D));
        await draftDuty.StageDutyAssignAsync(session.Id, Hakam, D, BobId);   // baseline {Alice} → {Alice, Bob}
        await draftDuty.StageDutyAssignAsync(session.Id, Lead, D, CarolId);  // empty → {Carol}
        _db.ChangeTracker.Clear();

        var model = BuildModel(draftDuty);

        // Production shape: live on-duties for the range (like BuildDutyTypeBasedCalendarAsync loads).
        var live = await _db.OnDuties.IgnoreQueryFilters().Include(o => o.User)
            .Where(o => o.CanceledAt == null && o.Date >= D && o.Date <= D).ToListAsync();

        // Act: overlay the draft, then build the duty-type cells.
        var overlaid = await model.ApplyDraftOverlayAsync(live);

        var noTexts = new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>();
        var noNotes = new Dictionary<(int UserId, DateOnly Date), string>();
        var hakamRow = model.BuildCellsForDutyType(Hakam, overlaid, noTexts, noNotes);
        var leadRow = model.BuildCellsForDutyType(Lead, overlaid, noTexts, noNotes);

        // Assert
        model.ActiveDraftId.Should().Be(session.Id, "the overlay resolves and exposes the active draft");

        // Hakam cell: the global live user (Alice) is preserved AND the staged Bob is shown — both by name.
        var hakamNames = hakamRow[D].Assignments.Select(a => a.Name).ToList();
        hakamNames.Should().BeEquivalentTo(new[] { "Alice", "Bob" },
            "the staged set folds onto the GLOBAL baseline, so the out-of-area live user is preserved");

        // Lead cell: staged Carol renders by name (not "Unknown").
        var leadNames = leadRow[D].Assignments.Select(a => a.Name).ToList();
        leadNames.Should().ContainSingle().Which.Should().Be("Carol");
    }

    [Fact]
    public async Task StagedClear_Removes_A_LiveUser_From_The_Rendered_Cell()
    {
        var draftDuty = new DraftDutyService(_db, Mock.Of<IOnDutyService>());
        var lifecycle = new DraftLifecycle(_db, new IDraftReconciler[] { draftDuty });
        var session = await lifecycle.EnterAsync(OwnerUserId,
            new DraftScope(DraftSurface.OnCall, MoleculeId: null, JobTypeId: null, AreaId: 1, WeekStart: D, WeekEnd: D));
        await draftDuty.StageDutyClearAsync(session.Id, Hakam, D, AliceId); // baseline {Alice} → {}
        _db.ChangeTracker.Clear();

        var model = BuildModel(draftDuty);
        var live = await _db.OnDuties.IgnoreQueryFilters().Include(o => o.User)
            .Where(o => o.CanceledAt == null && o.Date >= D && o.Date <= D).ToListAsync();

        var overlaid = await model.ApplyDraftOverlayAsync(live);
        var noTexts = new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>();
        var noNotes = new Dictionary<(int UserId, DateOnly Date), string>();
        var hakamRow = model.BuildCellsForDutyType(Hakam, overlaid, noTexts, noNotes);

        hakamRow[D].Assignments.Should().BeEmpty("the staged clear removes the live user from the private overlay");
    }
}
