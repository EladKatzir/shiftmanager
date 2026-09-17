using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;
using SaveColumnWidthsModel = ShiftManager.Pages.Api.Calendar.SaveColumnWidthsModel;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Input validation for POST /Api/Calendar/SaveColumnWidths.
///
/// The endpoint is authenticated but grant-less (a column width is a personal UI preference, like
/// row order), so the request body is the entire attack surface. These tests pin the bounds:
/// an out-of-range width is REJECTED rather than clamped, because the client already clamps during
/// the drag — a width outside the range means the caller is not our UI, and silently accepting a
/// coerced value would hide that.
/// </summary>
public sealed class SaveColumnWidthsTests
{
    private static SaveColumnWidthsModel MakeModel(AppDbContext db, string body, bool authenticated = true)
    {
        var claims = authenticated
            ? new[] { new Claim(ClaimTypes.NameIdentifier, "7") }
            : Array.Empty<Claim>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var httpCtx = new DefaultHttpContext { User = principal };
        httpCtx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return new SaveColumnWidthsModel(
            new CalendarColumnWidthService(db),
            Mock.Of<IAuditLogService>(),
            NullLogger<SaveColumnWidthsModel>.Instance)
        {
            PageContext = new PageContext { HttpContext = httpCtx }
        };
    }

    private static int StatusOf(IActionResult result) =>
        ((JsonResult)result).StatusCode ?? 200;

    [Fact]
    public async Task ValidPayload_PersistsWidths()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var model = MakeModel(f.Db,
            """{"contextKey":"shifts:9:2:shift","widths":{"label":220,"day:2026-09-17":160}}""");

        var result = await model.OnPostAsync();

        Assert.Equal(200, StatusOf(result));
        var saved = await new CalendarColumnWidthService(f.Db).GetWidthMapAsync(7, "shifts:9:2:shift");
        Assert.Equal(220, saved["label"]);
        Assert.Equal(160, saved["day:2026-09-17"]);
    }

    [Fact]
    public async Task WidthBelowMinimum_IsRejected_AndNothingPersisted()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var model = MakeModel(f.Db, """{"contextKey":"ctx","widths":{"label":3}}""");

        Assert.Equal(400, StatusOf(await model.OnPostAsync()));
        Assert.Empty(await new CalendarColumnWidthService(f.Db).GetWidthMapAsync(7, "ctx"));
    }

    [Fact]
    public async Task WidthAboveMaximum_IsRejected()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var model = MakeModel(f.Db, """{"contextKey":"ctx","widths":{"label":99999}}""");

        Assert.Equal(400, StatusOf(await model.OnPostAsync()));
    }

    [Fact]
    public async Task TooManyColumns_IsRejected()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var entries = string.Join(",", Enumerable.Range(0, 401).Select(i => $"\"c{i}\":100"));
        var model = MakeModel(f.Db, "{\"contextKey\":\"ctx\",\"widths\":{" + entries + "}}");

        Assert.Equal(400, StatusOf(await model.OnPostAsync()));
    }

    [Fact]
    public async Task OverlongContextKey_IsRejected()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var model = MakeModel(f.Db,
            "{\"contextKey\":\"" + new string('x', 129) + "\",\"widths\":{\"label\":100}}");

        Assert.Equal(400, StatusOf(await model.OnPostAsync()));
    }

    [Fact]
    public async Task OverlongColumnKey_IsRejected()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var model = MakeModel(f.Db,
            "{\"contextKey\":\"ctx\",\"widths\":{\"" + new string('x', 65) + "\":100}}");

        Assert.Equal(400, StatusOf(await model.OnPostAsync()));
    }

    [Fact]
    public async Task EmptyWidthMap_IsAccepted_AndClearsTheContext()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await new CalendarColumnWidthService(f.Db).SaveWidthsAsync(7, "ctx",
            new Dictionary<string, int> { ["label"] = 200 });

        var model = MakeModel(f.Db, """{"contextKey":"ctx","widths":{}}""");

        Assert.Equal(200, StatusOf(await model.OnPostAsync()));
        Assert.Empty(await new CalendarColumnWidthService(f.Db).GetWidthMapAsync(7, "ctx"));
    }

    [Fact]
    public void EndpointBounds_AreTheColumnPlannerBounds()
    {
        // The drag handle clamps client-side, the endpoint rejects server-side, and the view emits
        // the numbers to the DOM. All three must read the same constants or a legal drag starts
        // failing at the server.
        Assert.Equal(ShiftManager.ViewComponents.CalendarColumnPlanner.MinWidth, SaveColumnWidthsModel.MinWidth);
        Assert.Equal(ShiftManager.ViewComponents.CalendarColumnPlanner.MaxWidth, SaveColumnWidthsModel.MaxWidth);
    }

    [Fact]
    public async Task MalformedJson_IsRejected()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var model = MakeModel(f.Db, "{not json");

        Assert.Equal(400, StatusOf(await model.OnPostAsync()));
    }

    [Fact]
    public async Task Unauthenticated_IsRejected()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var model = MakeModel(f.Db, """{"contextKey":"ctx","widths":{"label":100}}""", authenticated: false);

        Assert.Equal(401, StatusOf(await model.OnPostAsync()));
    }
}
