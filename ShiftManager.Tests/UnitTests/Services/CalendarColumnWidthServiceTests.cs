using ShiftManager.Data;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers; // SqliteDbContextFixture

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Per-user calendar column widths. Mirrors <see cref="CalendarRowOrderServiceTests"/> — the two
/// features are the same shape (a personal, non-tenant UI preference keyed by ContextKey) and the
/// row-order service is the reference implementation.
/// </summary>
public class CalendarColumnWidthServiceTests
{
    private static CalendarColumnWidthService NewSvc(AppDbContext db) => new(db);

    [Fact]
    public async Task SaveThenGet_RoundTrips_Widths()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);

        await svc.SaveWidthsAsync(userId: 1, "shifts:9:2:shift", new Dictionary<string, int>
        {
            ["label"] = 220,
            ["day:2026-09-17"] = 160,
        });

        var map = await svc.GetWidthMapAsync(1, "shifts:9:2:shift");
        Assert.Equal(220, map["label"]);
        Assert.Equal(160, map["day:2026-09-17"]);
    }

    [Fact]
    public async Task SaveWidths_ReplacesWholeContext_DroppingOmittedColumns()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);

        await svc.SaveWidthsAsync(1, "ctx", new Dictionary<string, int>
        {
            ["label"] = 200,
            ["day:2026-09-17"] = 160,
        });
        // Re-save without "day:2026-09-17" — a column reset to default must stop being persisted.
        await svc.SaveWidthsAsync(1, "ctx", new Dictionary<string, int> { ["label"] = 240 });

        var map = await svc.GetWidthMapAsync(1, "ctx");
        Assert.Equal(240, map["label"]);
        Assert.False(map.ContainsKey("day:2026-09-17"));
    }

    [Fact]
    public async Task SaveWidths_DoesNotAffectOtherContexts()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);

        await svc.SaveWidthsAsync(1, "shifts:9:2:shift", new Dictionary<string, int> { ["label"] = 200 });
        await svc.SaveWidthsAsync(1, "chores:9", new Dictionary<string, int> { ["label"] = 300 });
        await svc.SaveWidthsAsync(1, "shifts:9:2:shift", new Dictionary<string, int> { ["label"] = 250 });

        Assert.Equal(300, (await svc.GetWidthMapAsync(1, "chores:9"))["label"]);
        Assert.Equal(250, (await svc.GetWidthMapAsync(1, "shifts:9:2:shift"))["label"]);
    }

    [Fact]
    public async Task Widths_AreIsolatedPerUser()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);

        await svc.SaveWidthsAsync(1, "ctx", new Dictionary<string, int> { ["label"] = 200 });

        Assert.Empty(await svc.GetWidthMapAsync(2, "ctx"));
    }

    [Fact]
    public async Task SaveWidths_WithEmptyMap_ClearsTheContext()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);

        await svc.SaveWidthsAsync(1, "ctx", new Dictionary<string, int> { ["label"] = 200 });
        await svc.SaveWidthsAsync(1, "ctx", new Dictionary<string, int>());

        Assert.Empty(await svc.GetWidthMapAsync(1, "ctx"));
    }
}
