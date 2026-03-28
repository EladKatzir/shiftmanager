using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Text.Json;

namespace ShiftManager.Tests.UnitTests.Services;

public class HomeTypeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly HomeTypeService _service;

    private const int TestMoleculeId = 1;
    private const int TestCompanyId = 1;

    public HomeTypeServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);
        _service = new HomeTypeService(_db, Mock.Of<ILogger<HomeTypeService>>());

        // Seed hierarchy
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "Area1", DisplayName = "Area 1" });
        _db.Molecules.Add(new Molecule { Id = TestMoleculeId, AreaId = 1, Name = "Mol1", DisplayName = "Molecule 1" });
        _db.Companies.Add(new Company { Id = TestCompanyId, MoleculeId = TestMoleculeId, Name = "Co1", DisplayName = "Company 1" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private HomeType CreateTestHomeType(int id = 1, string name = "Rotation A", bool isActive = true)
    {
        return new HomeType
        {
            Id = id,
            CompanyId = TestCompanyId,
            MoleculeId = TestMoleculeId,
            Name = name,
            IsActive = isActive,
            CreatedBy = 1
        };
    }

    // --- CRUD ---

    [Fact]
    public async Task CreateHomeTypeAsync_HappyPath_CreatesAndReturns()
    {
        var ht = CreateTestHomeType(id: 0);

        var result = await _service.CreateHomeTypeAsync(ht);

        result.Id.Should().BeGreaterThan(0);
        result.Name.Should().Be("Rotation A");
        result.MoleculeId.Should().Be(TestMoleculeId);
    }

    [Fact]
    public async Task GetHomeTypeAsync_ReturnsHomeType_WhenExists()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        await _db.SaveChangesAsync();

        var result = await _service.GetHomeTypeAsync(1);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Rotation A");
    }

    [Fact]
    public async Task GetHomeTypeAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetHomeTypeAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateHomeTypeAsync_HappyPath_ReturnsTrue()
    {
        var ht = CreateTestHomeType();
        _db.HomeTypes.Add(ht);
        await _db.SaveChangesAsync();

        ht.Name = "Updated Name";
        var result = await _service.UpdateHomeTypeAsync(ht);

        result.Should().BeTrue();
        var updated = await _db.HomeTypes.FindAsync(1);
        updated!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task DeleteHomeTypeAsync_HappyPath_RemovesAndUnassignsUsers()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        _db.Users.Add(new AppUser
        {
            Id = 10, Email = "user@test.com", DisplayName = "User",
            CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee, HomeTypeId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.DeleteHomeTypeAsync(1);

        result.Should().BeTrue();
        (await _db.HomeTypes.FindAsync(1)).Should().BeNull();
        var user = await _db.Users.FindAsync(10);
        user!.HomeTypeId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteHomeTypeAsync_NotFound_ReturnsFalse()
    {
        var result = await _service.DeleteHomeTypeAsync(999);

        result.Should().BeFalse();
    }

    // --- GetHomeTypesAsync ---

    [Fact]
    public async Task GetHomeTypesAsync_ReturnsDtosWithUserCounts()
    {
        _db.HomeTypes.AddRange(
            CreateTestHomeType(id: 1, name: "Rotation A"),
            CreateTestHomeType(id: 2, name: "Rotation B")
        );
        _db.Users.AddRange(
            new AppUser { Id = 10, Email = "u1@test.com", DisplayName = "U1", CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee, HomeTypeId = 1 },
            new AppUser { Id = 11, Email = "u2@test.com", DisplayName = "U2", CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee, HomeTypeId = 1 },
            new AppUser { Id = 12, Email = "u3@test.com", DisplayName = "U3", CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee, HomeTypeId = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetHomeTypesAsync(TestMoleculeId);

        result.Should().HaveCount(2);
        result.Should().Contain(dto => dto.Name == "Rotation A" && dto.UserCount == 2);
        result.Should().Contain(dto => dto.Name == "Rotation B" && dto.UserCount == 1);
    }

    [Fact]
    public async Task GetHomeTypesAsync_DifferentMolecule_ReturnsEmpty()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        await _db.SaveChangesAsync();

        var result = await _service.GetHomeTypesAsync(999);

        result.Should().BeEmpty();
    }

    // --- DeriveRuleFromPattern ---

    [Fact]
    public void DeriveRuleFromPattern_EmptyList_ReturnsNull()
    {
        var result = _service.DeriveRuleFromPattern(new List<DateOnly>());

        result.Should().BeNull();
    }

    [Fact]
    public void DeriveRuleFromPattern_SingleWeek_DefaultsCycleToFour()
    {
        // Paint Thursday-Sunday of one week (a single home-week pattern)
        var dates = new List<DateOnly>
        {
            new(2026, 3, 5), // Thursday
            new(2026, 3, 6), // Friday
            new(2026, 3, 7), // Saturday
            new(2026, 3, 8), // Sunday
        };

        var result = _service.DeriveRuleFromPattern(dates);

        result.Should().NotBeNull();
        result!.CycleWeeks.Should().Be(4); // default for single week
        result.HomeDays.Should().Contain(DayOfWeek.Thursday);
        result.HomeDays.Should().Contain(DayOfWeek.Friday);
        result.HomeDays.Should().Contain(DayOfWeek.Saturday);
        result.HomeDays.Should().Contain(DayOfWeek.Sunday);
    }

    [Fact]
    public void DeriveRuleFromPattern_TwoWeeks_EqualGap_DetectsCycle()
    {
        // Paint every other week (2-week cycle)
        var dates = new List<DateOnly>
        {
            new(2026, 3, 5), // Thursday, week 0
            new(2026, 3, 6), // Friday, week 0
            new(2026, 3, 19), // Thursday, week 2
            new(2026, 3, 20), // Friday, week 2
        };

        var result = _service.DeriveRuleFromPattern(dates);

        result.Should().NotBeNull();
        result!.CycleWeeks.Should().Be(2); // detected from equal gaps
        result.HomeDays.Should().HaveCount(2);
        result.HomeDays.Should().Contain(DayOfWeek.Thursday);
        result.HomeDays.Should().Contain(DayOfWeek.Friday);
    }

    // --- User Assignment ---

    [Fact]
    public async Task GetUsersForHomeTypeAsync_ReturnsActiveUsersOnly()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        _db.Users.AddRange(
            new AppUser { Id = 10, Email = "active@test.com", DisplayName = "Active", CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee, HomeTypeId = 1 },
            new AppUser { Id = 11, Email = "inactive@test.com", DisplayName = "Inactive", CompanyId = TestCompanyId, IsActive = false, Role = UserRole.Employee, HomeTypeId = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetUsersForHomeTypeAsync(1);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(10);
    }

    [Fact]
    public async Task AssignUsersAsync_AssignsUsersInSameMolecule()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        _db.Users.AddRange(
            new AppUser { Id = 10, Email = "in@test.com", DisplayName = "In Molecule", CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = 11, Email = "out@test.com", DisplayName = "Other Molecule", CompanyId = 99, IsActive = true, Role = UserRole.Employee }
        );
        await _db.SaveChangesAsync();

        await _service.AssignUsersAsync(1, new List<int> { 10, 11 });

        var inUser = await _db.Users.FindAsync(10);
        inUser!.HomeTypeId.Should().Be(1);
        var outUser = await _db.Users.FindAsync(11);
        outUser!.HomeTypeId.Should().BeNull(); // excluded — company not in molecule
    }

    [Fact]
    public async Task UnassignUserAsync_ClearsHomeTypeId()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        _db.Users.Add(new AppUser
        {
            Id = 10, Email = "user@test.com", DisplayName = "User",
            CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee, HomeTypeId = 1
        });
        await _db.SaveChangesAsync();

        await _service.UnassignUserAsync(1, 10);

        var user = await _db.Users.FindAsync(10);
        user!.HomeTypeId.Should().BeNull();
    }

    [Fact]
    public async Task UnassignUserAsync_WrongHomeType_DoesNothing()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        _db.Users.Add(new AppUser
        {
            Id = 10, Email = "user@test.com", DisplayName = "User",
            CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee, HomeTypeId = 2
        });
        await _db.SaveChangesAsync();

        await _service.UnassignUserAsync(1, 10);

        var user = await _db.Users.FindAsync(10);
        user!.HomeTypeId.Should().Be(2); // unchanged
    }

    // --- Override dates ---

    [Fact]
    public async Task SaveUserOverrideAsync_CreatesNewOverride()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        _db.Users.Add(new AppUser
        {
            Id = 10, Email = "user@test.com", DisplayName = "User",
            CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee
        });
        await _db.SaveChangesAsync();

        var dates = new List<DateOnly> { new(2026, 4, 1), new(2026, 4, 2) };
        await _service.SaveUserOverrideAsync(1, 10, dates, createdByUserId: 1);

        var overrides = await _db.HomeTypeOverrides.ToListAsync();
        overrides.Should().HaveCount(1);
        overrides[0].HomeTypeId.Should().Be(1);
        overrides[0].UserId.Should().Be(10);
    }

    [Fact]
    public async Task SaveUserOverrideAsync_UpdatesExistingOverride()
    {
        _db.HomeTypes.Add(CreateTestHomeType());
        _db.HomeTypeOverrides.Add(new HomeTypeOverride
        {
            Id = 1, HomeTypeId = 1, UserId = 10, CompanyId = TestCompanyId,
            OverridePatternJson = "[\"2026-03-01\"]", CreatedBy = 1
        });
        await _db.SaveChangesAsync();

        var newDates = new List<DateOnly> { new(2026, 4, 10) };
        await _service.SaveUserOverrideAsync(1, 10, newDates, createdByUserId: 2);

        var overrides = await _db.HomeTypeOverrides.ToListAsync();
        overrides.Should().HaveCount(1);
        overrides[0].OverridePatternJson.Should().Contain("2026-04-10");
        overrides[0].CreatedBy.Should().Be(2);
    }

    [Fact]
    public async Task GetUserOverrideDatesAsync_ReturnsDeserializedDates()
    {
        _db.HomeTypeOverrides.Add(new HomeTypeOverride
        {
            Id = 1, HomeTypeId = 1, UserId = 10, CompanyId = TestCompanyId,
            OverridePatternJson = "[\"2026-04-01\",\"2026-04-02\"]", CreatedBy = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetUserOverrideDatesAsync(1, 10);

        result.Should().HaveCount(2);
        result.Should().Contain(new DateOnly(2026, 4, 1));
        result.Should().Contain(new DateOnly(2026, 4, 2));
    }

    [Fact]
    public async Task GetUserOverrideDatesAsync_NoOverride_ReturnsEmpty()
    {
        var result = await _service.GetUserOverrideDatesAsync(1, 10);

        result.Should().BeEmpty();
    }
}
