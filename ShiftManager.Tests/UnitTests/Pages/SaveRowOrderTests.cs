using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

public class SaveRowOrderTests
{
    [Fact]
    public async Task Service_Persists_OrderForUser()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        var svc = new CalendarRowOrderService(f.Db);
        await svc.SaveOrderAsync(7, "overview:5", "", new[] { "user-2", "user-1" });
        var map = await svc.GetOrderMapAsync(7, "overview:5");
        Assert.Equal(0, map[("", "user-2")]);
        Assert.Equal(1, map[("", "user-1")]);
    }
}
