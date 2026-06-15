using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers; // SqliteDbContextFixture

namespace ShiftManager.Tests.UnitTests.Services;

public class CalendarRowOrderServiceTests
{
    private static CalendarRowOrderService NewSvc(AppDbContext db) => new(db);

    [Fact]
    public async Task SaveThenGet_RoundTrips_RowOrder()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var db = f.Db;
        var svc = NewSvc(db);

        await svc.SaveOrderAsync(userId: 1, "shifts:9:2:user", "category-5",
            new[] { "user-3", "user-1", "user-2" });

        var map = await svc.GetOrderMapAsync(1, "shifts:9:2:user");
        Assert.Equal(0, map[("category-5", "user-3")]);
        Assert.Equal(1, map[("category-5", "user-1")]);
        Assert.Equal(2, map[("category-5", "user-2")]);
    }

    [Fact]
    public async Task SaveOrder_ReplacesGroupNamespace_NotOtherGroups()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);
        await svc.SaveOrderAsync(1, "ctx", "category-5", new[] { "user-1", "user-2" });
        await svc.SaveOrderAsync(1, "ctx", "category-6", new[] { "user-9" });
        // re-save category-5 with fewer items — must not touch category-6
        await svc.SaveOrderAsync(1, "ctx", "category-5", new[] { "user-2" });

        var map = await svc.GetOrderMapAsync(1, "ctx");
        Assert.True(map.ContainsKey(("category-6", "user-9")));
        Assert.False(map.ContainsKey(("category-5", "user-1"))); // removed by replace
        Assert.Equal(0, map[("category-5", "user-2")]);
    }

    [Fact]
    public async Task CategoryOrder_UsesEmptyGroupNamespace()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);
        await svc.SaveOrderAsync(1, "ctx", "", new[] { "category-6", "category-5" });
        var map = await svc.GetOrderMapAsync(1, "ctx");
        Assert.Equal(0, map[("", "category-6")]);
        Assert.Equal(1, map[("", "category-5")]);
    }

    [Fact]
    public async Task Orders_AreIsolatedPerUser()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);
        await svc.SaveOrderAsync(1, "ctx", "g", new[] { "user-1" });
        var other = await svc.GetOrderMapAsync(2, "ctx");
        Assert.Empty(other);
    }

    [Fact]
    public async Task SaveOrder_WhenInsertFails_PreservesExistingOrder()
    {
        // #6: SaveOrderAsync did ExecuteDeleteAsync (commits immediately) then insert+SaveChanges with no
        // transaction, so a failed insert left the user's order WIPED. A save whose items contain a
        // duplicate RowId violates the unique (UserId,ContextKey,GroupId,RowId) index on insert; the prior
        // order must survive (atomic replace = delete rolls back with the failed insert).
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = NewSvc(f.Db);
        await svc.SaveOrderAsync(1, "ctx", "g", new[] { "a", "b", "c" });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            svc.SaveOrderAsync(1, "ctx", "g", new[] { "x", "x" }));

        var map = await svc.GetOrderMapAsync(1, "ctx");
        Assert.Equal(3, map.Count); // a, b, c preserved
    }
}
