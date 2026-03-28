using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class StoreServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StoreService _service;
    private readonly IMemoryCache _cache;

    private const int TestAreaId = 1;

    public StoreServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());

        var localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        // Setup localizer to return the key as value for testing
        localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));
        localizerMock.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns((string key, object[] args) => new LocalizedString(key, string.Format(key, args)));

        _service = new StoreService(
            _db,
            _cache,
            Mock.Of<ILogger<StoreService>>(),
            localizerMock.Object);

        // Seed an area
        _db.Areas.Add(new Area { Id = TestAreaId, ProjectId = 1, Name = "TestArea", DisplayName = "Test Area" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    // --- CreateStoreAsync ---

    [Fact]
    public async Task CreateStoreAsync_HappyPath_CreatesStore()
    {
        var (success, message, store) = await _service.CreateStoreAsync(TestAreaId, "Main PX", "חנות ראשית", 1);

        success.Should().BeTrue();
        message.Should().Contain("created successfully");
        store.Should().NotBeNull();
        store!.NameEn.Should().Be("Main PX");
        store.NameHe.Should().Be("חנות ראשית");
        store.AreaId.Should().Be(TestAreaId);
        store.SortOrder.Should().Be(1);
        store.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateStoreAsync_EmptyName_ReturnsError()
    {
        var (success, message, store) = await _service.CreateStoreAsync(TestAreaId, "  ", null);

        success.Should().BeFalse();
        message.Should().Contain("required");
        store.Should().BeNull();
    }

    [Fact]
    public async Task CreateStoreAsync_AreaNotFound_ReturnsError()
    {
        var (success, message, store) = await _service.CreateStoreAsync(999, "Store", null);

        success.Should().BeFalse();
        message.Should().Contain("Area not found");
        store.Should().BeNull();
    }

    [Fact]
    public async Task CreateStoreAsync_TrimsName()
    {
        var (_, _, store) = await _service.CreateStoreAsync(TestAreaId, "  Main PX  ", "  חנות  ");

        store.Should().NotBeNull();
        store!.NameEn.Should().Be("Main PX");
        store.NameHe.Should().Be("חנות");
    }

    // --- GetStoreByIdAsync ---

    [Fact]
    public async Task GetStoreByIdAsync_ReturnsStore_WhenExists()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "PX", IsActive = true });
        await _db.SaveChangesAsync();

        var result = await _service.GetStoreByIdAsync(1);

        result.Should().NotBeNull();
        result!.NameEn.Should().Be("PX");
    }

    [Fact]
    public async Task GetStoreByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetStoreByIdAsync(999);

        result.Should().BeNull();
    }

    // --- GetStoresForAreaAsync ---

    [Fact]
    public async Task GetStoresForAreaAsync_ReturnsStoresOrderedBySortOrderThenName()
    {
        _db.Stores.AddRange(
            new Store { Id = 1, AreaId = TestAreaId, NameEn = "Bravo Store", SortOrder = 2, IsActive = true },
            new Store { Id = 2, AreaId = TestAreaId, NameEn = "Alpha Store", SortOrder = 1, IsActive = true },
            new Store { Id = 3, AreaId = TestAreaId, NameEn = "Charlie Store", SortOrder = 1, IsActive = true },
            new Store { Id = 4, AreaId = 99, NameEn = "Other Area Store", SortOrder = 0, IsActive = true }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetStoresForAreaAsync(TestAreaId);

        result.Should().HaveCount(3);
        result[0].NameEn.Should().Be("Alpha Store");
        result[1].NameEn.Should().Be("Charlie Store");
        result[2].NameEn.Should().Be("Bravo Store");
    }

    // --- UpdateStoreAsync ---

    [Fact]
    public async Task UpdateStoreAsync_HappyPath_UpdatesFields()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Old Name", IsActive = true });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.UpdateStoreAsync(1, "New Name", "שם חדש", false, 5);

        success.Should().BeTrue();
        message.Should().Contain("updated successfully");

        var store = await _db.Stores.FindAsync(1);
        store!.NameEn.Should().Be("New Name");
        store.NameHe.Should().Be("שם חדש");
        store.IsActive.Should().BeFalse();
        store.SortOrder.Should().Be(5);
    }

    [Fact]
    public async Task UpdateStoreAsync_NotFound_ReturnsError()
    {
        var (success, message) = await _service.UpdateStoreAsync(999, "Name", null, true, 0);

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateStoreAsync_EmptyName_ReturnsError()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.UpdateStoreAsync(1, "", null, true, 0);

        success.Should().BeFalse();
        message.Should().Contain("required");
    }

    // --- DeleteStoreAsync ---

    [Fact]
    public async Task DeleteStoreAsync_HappyPath_RemovesStoreAndHoursAndConfigs()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Delete Me", IsActive = true });
        _db.StoreHoursEntries.Add(new StoreHoursEntry
        {
            Id = 1, StoreId = 1, DayOfWeek = 0,
            OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true
        });
        _db.QuickInfoConfigs.Add(new QuickInfoConfig
        {
            Id = 1, MoleculeId = 1, SectionType = QuickInfoSectionType.Store, EntityId = 1, DisplayOrder = 0
        });
        await _db.SaveChangesAsync();

        var (success, message) = await _service.DeleteStoreAsync(1);

        success.Should().BeTrue();
        message.Should().Contain("deleted successfully");
        (await _db.Stores.FindAsync(1)).Should().BeNull();
        (await _db.StoreHoursEntries.CountAsync()).Should().Be(0);
        (await _db.QuickInfoConfigs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteStoreAsync_NotFound_ReturnsError()
    {
        var (success, message) = await _service.DeleteStoreAsync(999);

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    // --- SetStoreHoursAsync ---

    [Fact]
    public async Task SetStoreHoursAsync_HappyPath_ReplacesEntries()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        _db.StoreHoursEntries.Add(new StoreHoursEntry
        {
            Id = 1, StoreId = 1, DayOfWeek = 0,
            OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(12, 0), IsActive = true
        });
        await _db.SaveChangesAsync();

        var newEntries = new List<StoreHoursEntry>
        {
            new() { DayOfWeek = 0, OpenTime = new TimeOnly(9, 0), CloseTime = new TimeOnly(14, 0), IsActive = true },
            new() { DayOfWeek = 0, OpenTime = new TimeOnly(16, 0), CloseTime = new TimeOnly(20, 0), IsActive = true }
        };

        var (success, message) = await _service.SetStoreHoursAsync(1, newEntries);

        success.Should().BeTrue();
        var entries = await _db.StoreHoursEntries.Where(e => e.StoreId == 1).ToListAsync();
        entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetStoreHoursAsync_StoreNotFound_ReturnsError()
    {
        var (success, message) = await _service.SetStoreHoursAsync(999, new List<StoreHoursEntry>());

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    // --- GetStoreHoursAsync ---

    [Fact]
    public async Task GetStoreHoursAsync_ReturnsEntriesOrderedByDayThenTime()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        _db.StoreHoursEntries.AddRange(
            new StoreHoursEntry { Id = 1, StoreId = 1, DayOfWeek = 1, OpenTime = new TimeOnly(16, 0), CloseTime = new TimeOnly(20, 0), IsActive = true },
            new StoreHoursEntry { Id = 2, StoreId = 1, DayOfWeek = 0, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true },
            new StoreHoursEntry { Id = 3, StoreId = 1, DayOfWeek = 1, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetStoreHoursAsync(1);

        result.Should().HaveCount(3);
        result[0].DayOfWeek.Should().Be(0);
        result[1].DayOfWeek.Should().Be(1);
        result[1].OpenTime.Should().Be(new TimeOnly(8, 0));
        result[2].DayOfWeek.Should().Be(1);
        result[2].OpenTime.Should().Be(new TimeOnly(16, 0));
    }

    [Fact]
    public async Task GetStoreHoursAsync_CachesResult()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        _db.StoreHoursEntries.Add(new StoreHoursEntry
        {
            Id = 1, StoreId = 1, DayOfWeek = 0,
            OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true
        });
        await _db.SaveChangesAsync();

        // First call populates cache
        var result1 = await _service.GetStoreHoursAsync(1);
        result1.Should().HaveCount(1);

        // Second call should return cached value (verify by checking cache directly)
        _cache.TryGetValue("store-hours-1", out List<StoreHoursEntry>? cached).Should().BeTrue();
        cached.Should().HaveCount(1);
    }

    // --- ComputeStoreStatusAsync ---

    [Fact]
    public async Task ComputeStoreStatusAsync_StoreNotFound_ReturnsNull()
    {
        var result = await _service.ComputeStoreStatusAsync(999, DateTime.Now);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ComputeStoreStatusAsync_NoHoursForToday_ReturnsNoHoursSet()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        await _db.SaveChangesAsync();

        var result = await _service.ComputeStoreStatusAsync(1, DateTime.Now);

        result.Should().NotBeNull();
        result!.Status.Should().Be(StoreStatusType.NoHoursSet);
    }

    [Fact]
    public async Task ComputeStoreStatusAsync_CurrentlyOpen_ReturnsOpen()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        // Use a fixed time: Wednesday 10:00 (DayOfWeek=3)
        var now = new DateTime(2026, 3, 25, 10, 0, 0); // Wednesday
        _db.StoreHoursEntries.Add(new StoreHoursEntry
        {
            Id = 1, StoreId = 1, DayOfWeek = 3,
            OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.ComputeStoreStatusAsync(1, now);

        result.Should().NotBeNull();
        result!.Status.Should().Be(StoreStatusType.Open);
        result.NextChange.Should().Be(new TimeOnly(14, 0));
    }

    [Fact]
    public async Task ComputeStoreStatusAsync_BeforeOpenTime_ReturnsClosed()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        var now = new DateTime(2026, 3, 25, 7, 0, 0); // Wednesday 7:00
        _db.StoreHoursEntries.Add(new StoreHoursEntry
        {
            Id = 1, StoreId = 1, DayOfWeek = 3,
            OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.ComputeStoreStatusAsync(1, now);

        result.Should().NotBeNull();
        result!.Status.Should().Be(StoreStatusType.Closed);
        result.NextChange.Should().Be(new TimeOnly(8, 0));
    }

    [Fact]
    public async Task ComputeStoreStatusAsync_BetweenWindows_ReturnsBreak()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        var now = new DateTime(2026, 3, 25, 14, 30, 0); // Wednesday 14:30
        _db.StoreHoursEntries.AddRange(
            new StoreHoursEntry { Id = 1, StoreId = 1, DayOfWeek = 3, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true },
            new StoreHoursEntry { Id = 2, StoreId = 1, DayOfWeek = 3, OpenTime = new TimeOnly(16, 0), CloseTime = new TimeOnly(20, 0), IsActive = true }
        );
        await _db.SaveChangesAsync();

        var result = await _service.ComputeStoreStatusAsync(1, now);

        result.Should().NotBeNull();
        result!.Status.Should().Be(StoreStatusType.Break);
        result.NextChange.Should().Be(new TimeOnly(16, 0));
    }

    [Fact]
    public async Task ComputeStoreStatusAsync_AfterAllWindows_ReturnsClosed()
    {
        _db.Stores.Add(new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store", IsActive = true });
        var now = new DateTime(2026, 3, 25, 21, 0, 0); // Wednesday 21:00
        _db.StoreHoursEntries.Add(new StoreHoursEntry
        {
            Id = 1, StoreId = 1, DayOfWeek = 3,
            OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.ComputeStoreStatusAsync(1, now);

        result.Should().NotBeNull();
        result!.Status.Should().Be(StoreStatusType.Closed);
    }

    // --- ComputeAllStoreStatusesAsync ---

    [Fact]
    public async Task ComputeAllStoreStatusesAsync_EmptyList_ReturnsEmpty()
    {
        var result = await _service.ComputeAllStoreStatusesAsync(new List<int>(), DateTime.Now);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeAllStoreStatusesAsync_MultipleStores_ReturnsAllStatuses()
    {
        _db.Stores.AddRange(
            new Store { Id = 1, AreaId = TestAreaId, NameEn = "Store A", IsActive = true },
            new Store { Id = 2, AreaId = TestAreaId, NameEn = "Store B", IsActive = true }
        );
        var now = new DateTime(2026, 3, 25, 10, 0, 0); // Wednesday
        _db.StoreHoursEntries.AddRange(
            new StoreHoursEntry { Id = 1, StoreId = 1, DayOfWeek = 3, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(14, 0), IsActive = true },
            new StoreHoursEntry { Id = 2, StoreId = 2, DayOfWeek = 3, OpenTime = new TimeOnly(9, 0), CloseTime = new TimeOnly(12, 0), IsActive = true }
        );
        await _db.SaveChangesAsync();

        var result = await _service.ComputeAllStoreStatusesAsync(new List<int> { 1, 2 }, now);

        result.Should().HaveCount(2);
        result.Should().Contain(s => s.StoreId == 1 && s.Status == StoreStatusType.Open);
        result.Should().Contain(s => s.StoreId == 2 && s.Status == StoreStatusType.Open);
    }
}
