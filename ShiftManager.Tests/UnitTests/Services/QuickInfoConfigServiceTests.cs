using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class QuickInfoConfigServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly QuickInfoConfigService _service;

    private const int TestMoleculeId = 1;
    private const int TestAreaId = 1;

    public QuickInfoConfigServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);
        _service = new QuickInfoConfigService(_db, Mock.Of<ILogger<QuickInfoConfigService>>());

        // Seed hierarchy: Area -> Molecule
        _db.Areas.Add(new Area { Id = TestAreaId, ProjectId = 1, Name = "TestArea", DisplayName = "Test Area" });
        _db.Molecules.Add(new Molecule { Id = TestMoleculeId, AreaId = TestAreaId, Name = "TestMol", DisplayName = "Test Molecule" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // --- HasConfigAsync ---

    [Fact]
    public async Task HasConfigAsync_NoConfig_ReturnsFalse()
    {
        var result = await _service.HasConfigAsync(TestMoleculeId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasConfigAsync_WithConfig_ReturnsTrue()
    {
        _db.QuickInfoConfigs.Add(new QuickInfoConfig
        {
            Id = 1, MoleculeId = TestMoleculeId, SectionType = QuickInfoSectionType.OnCallRole,
            EntityId = 0, DisplayOrder = 0, IsEnabled = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.HasConfigAsync(TestMoleculeId);

        result.Should().BeTrue();
    }

    // --- GetConfigForMoleculeAsync ---

    [Fact]
    public async Task GetConfigForMoleculeAsync_ReturnsEnabledOnly_OrderedByDisplayOrder()
    {
        _db.QuickInfoConfigs.AddRange(
            new QuickInfoConfig { Id = 1, MoleculeId = TestMoleculeId, SectionType = QuickInfoSectionType.OnCallRole, EntityId = 0, DisplayOrder = 2, IsEnabled = true },
            new QuickInfoConfig { Id = 2, MoleculeId = TestMoleculeId, SectionType = QuickInfoSectionType.Store, EntityId = 1, DisplayOrder = 0, IsEnabled = true },
            new QuickInfoConfig { Id = 3, MoleculeId = TestMoleculeId, SectionType = QuickInfoSectionType.OnCallRole, EntityId = 1, DisplayOrder = 1, IsEnabled = false }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetConfigForMoleculeAsync(TestMoleculeId);

        result.Should().HaveCount(2); // disabled one excluded
        result[0].EntityId.Should().Be(1); // Store with DisplayOrder 0
        result[1].EntityId.Should().Be(0); // OnCallRole with DisplayOrder 2
    }

    [Fact]
    public async Task GetConfigForMoleculeAsync_DifferentMolecule_ReturnsEmpty()
    {
        _db.QuickInfoConfigs.Add(new QuickInfoConfig
        {
            Id = 1, MoleculeId = TestMoleculeId, SectionType = QuickInfoSectionType.OnCallRole,
            EntityId = 0, DisplayOrder = 0, IsEnabled = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetConfigForMoleculeAsync(999);

        result.Should().BeEmpty();
    }

    // --- SaveConfigAsync ---

    [Fact]
    public async Task SaveConfigAsync_HappyPath_SavesNewConfig()
    {
        var items = new List<QuickInfoConfigItem>
        {
            new() { SectionType = QuickInfoSectionType.OnCallRole, EntityId = 0, DisplayOrder = 0, IsEnabled = true },
            new() { SectionType = QuickInfoSectionType.Store, EntityId = 5, DisplayOrder = 1, IsEnabled = true }
        };

        var (success, message) = await _service.SaveConfigAsync(TestMoleculeId, userId: 1, items);

        success.Should().BeTrue();
        message.Should().Contain("saved successfully");

        var saved = await _db.QuickInfoConfigs.Where(q => q.MoleculeId == TestMoleculeId).ToListAsync();
        saved.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveConfigAsync_ReplacesExistingConfig()
    {
        // Pre-seed existing config
        _db.QuickInfoConfigs.Add(new QuickInfoConfig
        {
            Id = 1, MoleculeId = TestMoleculeId, SectionType = QuickInfoSectionType.OnCallRole,
            EntityId = 0, DisplayOrder = 0, IsEnabled = true, CreatedBy = 1
        });
        await _db.SaveChangesAsync();

        var newItems = new List<QuickInfoConfigItem>
        {
            new() { SectionType = QuickInfoSectionType.Store, EntityId = 10, DisplayOrder = 0, IsEnabled = true }
        };

        var (success, _) = await _service.SaveConfigAsync(TestMoleculeId, userId: 2, newItems);

        success.Should().BeTrue();
        var saved = await _db.QuickInfoConfigs.Where(q => q.MoleculeId == TestMoleculeId).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].SectionType.Should().Be(QuickInfoSectionType.Store);
        saved[0].EntityId.Should().Be(10);
    }

    [Fact]
    public async Task SaveConfigAsync_NullItems_ReturnsError()
    {
        var (success, message) = await _service.SaveConfigAsync(TestMoleculeId, userId: 1, items: null!);

        success.Should().BeFalse();
        message.Should().Contain("null");
    }

    [Fact]
    public async Task SaveConfigAsync_MoleculeNotFound_ReturnsError()
    {
        var items = new List<QuickInfoConfigItem>
        {
            new() { SectionType = QuickInfoSectionType.OnCallRole, EntityId = 0, DisplayOrder = 0 }
        };

        var (success, message) = await _service.SaveConfigAsync(999, userId: 1, items);

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    // --- GetDefaultConfigAsync ---

    [Fact]
    public async Task GetDefaultConfigAsync_ReturnsDutyTypesAndStores()
    {
        _db.OnDutyTypeConfigs.AddRange(
            new OnDutyTypeConfig { Id = 1, TypeValue = 0, NameEn = "Hakam", NameHe = "חקם", IsActive = true },
            new OnDutyTypeConfig { Id = 2, TypeValue = 1, NameEn = "Lead", NameHe = "ראש", IsActive = true },
            new OnDutyTypeConfig { Id = 3, TypeValue = 2, NameEn = "Inactive", NameHe = "", IsActive = false }
        );
        _db.Stores.AddRange(
            new Store { Id = 1, AreaId = TestAreaId, NameEn = "PX", SortOrder = 0, IsActive = true },
            new Store { Id = 2, AreaId = TestAreaId, NameEn = "Cafe", SortOrder = 1, IsActive = true },
            new Store { Id = 3, AreaId = 99, NameEn = "Other Area", SortOrder = 0, IsActive = true }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetDefaultConfigAsync(TestMoleculeId);

        // 2 active duty types + 2 stores in our area
        result.Should().HaveCount(4);
        result[0].SectionType.Should().Be(QuickInfoSectionType.OnCallRole);
        result[0].EntityId.Should().Be(0); // Hakam TypeValue
        result[1].SectionType.Should().Be(QuickInfoSectionType.OnCallRole);
        result[1].EntityId.Should().Be(1); // Lead TypeValue
        result[2].SectionType.Should().Be(QuickInfoSectionType.Store);
        result[3].SectionType.Should().Be(QuickInfoSectionType.Store);

        // Verify consecutive display order
        for (int i = 0; i < result.Count; i++)
            result[i].DisplayOrder.Should().Be(i);
    }

    [Fact]
    public async Task GetDefaultConfigAsync_MoleculeNotFound_ReturnsEmpty()
    {
        var result = await _service.GetDefaultConfigAsync(999);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDefaultConfigAsync_NoDutyTypesOrStores_ReturnsEmpty()
    {
        var result = await _service.GetDefaultConfigAsync(TestMoleculeId);

        result.Should().BeEmpty();
    }
}
