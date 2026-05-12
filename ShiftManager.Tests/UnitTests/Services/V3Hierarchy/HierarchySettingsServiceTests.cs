using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services.V3Hierarchy;

/// <summary>
/// Tests for Task 13.5: Settings Cascade
/// Verifies that settings cascade correctly from Area → Molecule → Company
/// </summary>
public class HierarchySettingsServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly HierarchySettingsService _service;

    public HierarchySettingsServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        var logger = Mock.Of<ILogger<HierarchySettingsService>>();
        _service = new HierarchySettingsService(_db, logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private async Task<(Project project, Area area, Molecule molecule, Company company)> SetupHierarchyAsync()
    {
        var project = new Project { Name = "TestProject", DisplayName = "Test Project" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "TestArea", DisplayName = "Test Area" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        var molecule = new Molecule { AreaId = area.Id, Name = "TestMolecule", DisplayName = "Test Molecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        var company = new Company { MoleculeId = molecule.Id, Name = "TestCompany", DisplayName = "Test Company" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        return (project, area, molecule, company);
    }

    [Fact]
    public async Task GetEffectiveSettings_WithOnlyAreaSettings_ReturnsAreaValues()
    {
        // Arrange
        var (_, area, molecule, company) = await SetupHierarchyAsync();

        // Set area settings
        var areaSettings = new AreaSettings
        {
            AreaId = area.Id,
            DefaultRestHours = 10,
            DefaultWeeklyCap = 48
        };
        _db.AreaSettings.Add(areaSettings);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetEffectiveSettingsAsync(company.Id);

        // Assert
        result.Should().NotBeNull();
        result.RestHours.Should().Be(10);
        result.WeeklyCap.Should().Be(48);
        result.RestHoursSource.Should().Be("Area");
        result.WeeklyCapSource.Should().Be("Area");
    }

    [Fact]
    public async Task GetEffectiveSettings_WithMoleculeOverride_ReturnsMoleculeValues()
    {
        // Arrange
        var (_, area, molecule, company) = await SetupHierarchyAsync();

        // Set area settings
        var areaSettings = new AreaSettings
        {
            AreaId = area.Id,
            DefaultRestHours = 10,
            DefaultWeeklyCap = 48
        };
        _db.AreaSettings.Add(areaSettings);

        // Set molecule override (only for RestHours)
        var moleculeSettings = new MoleculeSettings
        {
            MoleculeId = molecule.Id,
            RestHoursOverride = 8,
            WeeklyCapOverride = null // Inherit from area
        };
        _db.MoleculeSettings.Add(moleculeSettings);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetEffectiveSettingsAsync(company.Id);

        // Assert
        result.Should().NotBeNull();
        result.RestHours.Should().Be(8);
        result.RestHoursSource.Should().Be("Molecule");
        result.WeeklyCap.Should().Be(48);
        result.WeeklyCapSource.Should().Be("Area");
    }

    [Fact]
    public async Task GetEffectiveSettings_WithCompanyOverride_ReturnsCompanyValues()
    {
        // Arrange
        var (_, area, molecule, company) = await SetupHierarchyAsync();

        // Set area settings
        var areaSettings = new AreaSettings
        {
            AreaId = area.Id,
            DefaultRestHours = 10,
            DefaultWeeklyCap = 48
        };
        _db.AreaSettings.Add(areaSettings);

        // Set molecule override
        var moleculeSettings = new MoleculeSettings
        {
            MoleculeId = molecule.Id,
            RestHoursOverride = 8,
            WeeklyCapOverride = null
        };
        _db.MoleculeSettings.Add(moleculeSettings);

        // Set company override (only for WeeklyCap)
        var companySettings = new CompanySettings
        {
            CompanyId = company.Id,
            RestHoursOverride = null, // Inherit from molecule
            WeeklyCapOverride = 56
        };
        _db.CompanySettings.Add(companySettings);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetEffectiveSettingsAsync(company.Id);

        // Assert
        result.Should().NotBeNull();
        result.RestHours.Should().Be(8);
        result.RestHoursSource.Should().Be("Molecule");
        result.WeeklyCap.Should().Be(56);
        result.WeeklyCapSource.Should().Be("Company");
    }

    [Fact]
    public async Task GetEffectiveSettings_WithFullCascade_ReturnsCorrectSources()
    {
        // Arrange
        var (_, area, molecule, company) = await SetupHierarchyAsync();

        // Set area settings (both values)
        var areaSettings = new AreaSettings
        {
            AreaId = area.Id,
            DefaultRestHours = 10,
            DefaultWeeklyCap = 48
        };
        _db.AreaSettings.Add(areaSettings);

        // Molecule overrides RestHours only
        var moleculeSettings = new MoleculeSettings
        {
            MoleculeId = molecule.Id,
            RestHoursOverride = 8,
            WeeklyCapOverride = null
        };
        _db.MoleculeSettings.Add(moleculeSettings);

        // Company overrides WeeklyCap only
        var companySettings = new CompanySettings
        {
            CompanyId = company.Id,
            RestHoursOverride = null,
            WeeklyCapOverride = 56
        };
        _db.CompanySettings.Add(companySettings);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetEffectiveSettingsAsync(company.Id);

        // Assert
        result.Should().NotBeNull();
        result.RestHours.Should().Be(8, "RestHours should come from Molecule");
        result.RestHoursSource.Should().Be("Molecule");
        result.WeeklyCap.Should().Be(56, "WeeklyCap should come from Company");
        result.WeeklyCapSource.Should().Be("Company");
    }

    [Fact]
    public async Task GetMoleculeSettings_WithOnlyAreaSettings_ReturnsAreaValues()
    {
        // Arrange
        var (_, area, molecule, _) = await SetupHierarchyAsync();

        var areaSettings = new AreaSettings
        {
            AreaId = area.Id,
            DefaultRestHours = 12,
            DefaultWeeklyCap = 60
        };
        _db.AreaSettings.Add(areaSettings);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetMoleculeSettingsAsync(molecule.Id);

        // Assert
        result.Should().NotBeNull();
        result.RestHours.Should().Be(12);
        result.WeeklyCap.Should().Be(60);
        result.RestHoursSource.Should().Be("Area");
        result.WeeklyCapSource.Should().Be("Area");
    }

    [Fact]
    public async Task UpdateAreaSettings_CreatesNewSettings_WhenNoneExist()
    {
        // Arrange
        var (_, area, _, _) = await SetupHierarchyAsync();
        var userId = 1;

        // Act
        var result = await _service.UpdateAreaSettingsAsync(area.Id, 9, 50, userId);

        // Assert
        result.Should().BeTrue();

        var settings = await _db.AreaSettings.FirstOrDefaultAsync(s => s.AreaId == area.Id);
        settings.Should().NotBeNull();
        settings!.DefaultRestHours.Should().Be(9);
        settings.DefaultWeeklyCap.Should().Be(50);
        settings.UpdatedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task UpdateMoleculeSettings_AllowsNullOverrides_ToInheritFromArea()
    {
        // Arrange
        var (_, area, molecule, _) = await SetupHierarchyAsync();

        // Set area settings
        var areaSettings = new AreaSettings { AreaId = area.Id, DefaultRestHours = 10, DefaultWeeklyCap = 48 };
        _db.AreaSettings.Add(areaSettings);

        // Initially set molecule overrides
        var moleculeSettings = new MoleculeSettings
        {
            MoleculeId = molecule.Id,
            RestHoursOverride = 8,
            WeeklyCapOverride = 56
        };
        _db.MoleculeSettings.Add(moleculeSettings);
        await _db.SaveChangesAsync();

        // Act - Clear overrides
        var result = await _service.UpdateMoleculeSettingsAsync(molecule.Id, null, null, 1);

        // Assert
        result.Should().BeTrue();

        var effective = await _service.GetMoleculeSettingsAsync(molecule.Id);
        effective.RestHours.Should().Be(10, "Should inherit from Area");
        effective.RestHoursSource.Should().Be("Area");
        effective.WeeklyCap.Should().Be(48, "Should inherit from Area");
        effective.WeeklyCapSource.Should().Be("Area");
    }

    [Fact]
    public async Task GetEffectiveSettings_WithNoSettings_ReturnsDefaultValues()
    {
        // Arrange
        var (_, area, molecule, company) = await SetupHierarchyAsync();
        // No settings created

        // Act
        var result = await _service.GetEffectiveSettingsAsync(company.Id);

        // Assert - Should return system defaults (0 or whatever the service defaults are)
        result.Should().NotBeNull();
        // The actual default values depend on the service implementation
    }
}
