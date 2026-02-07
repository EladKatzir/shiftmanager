using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services.V3Hierarchy;

/// <summary>
/// Tests for Task 13.3: Shift Assignment by JobType
/// Verifies that shift eligibility respects JobType and ShiftGrouping rules.
/// </summary>
public class ShiftAssignmentServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ShiftAssignmentService _service;

    public ShiftAssignmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);

        var localizer = Mock.Of<IStringLocalizer<SharedResources>>();
        var logger = Mock.Of<ILogger<ShiftAssignmentService>>();
        var hierarchySettingsServiceMock = new Mock<IHierarchySettingsService>();
        hierarchySettingsServiceMock
            .Setup(x => x.GetEffectiveSettingsAsync(It.IsAny<int>()))
            .ReturnsAsync(new EffectiveSettings(
                RestHours: 11,
                WeeklyCap: 48,
                RestHoursSource: "Area",
                WeeklyCapSource: "Area"));
        _service = new ShiftAssignmentService(_db, localizer, logger, hierarchySettingsServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<TestHierarchy> SetupTestHierarchyAsync()
    {
        // Create project and area
        var project = new Project { Name = "TestProject", DisplayName = "Test" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "TestArea", DisplayName = "Test" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        // Create job types
        var alhutJobType = new JobType { AreaId = area.Id, Name = "Alhut", DisplayName = "אלחוט", SortOrder = 1 };
        var textJobType = new JobType { AreaId = area.Id, Name = "Text", DisplayName = "טקסט", SortOrder = 2 };
        var brJobType = new JobType { AreaId = area.Id, Name = "BR", DisplayName = "ב\"ר", SortOrder = 3 };
        _db.JobTypes.AddRange(alhutJobType, textJobType, brJobType);
        await _db.SaveChangesAsync();

        // Create molecule
        var molecule = new Molecule { AreaId = area.Id, Name = "TestMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        // Create companies
        var company1 = new Company { MoleculeId = molecule.Id, Name = "Company1", DisplayName = "Company 1" };
        var company2 = new Company { MoleculeId = molecule.Id, Name = "Company2", DisplayName = "Company 2" };
        _db.Companies.AddRange(company1, company2);
        await _db.SaveChangesAsync();

        // Create shift grouping (combines Company1 and Company2)
        var shiftGrouping = new ShiftGrouping { MoleculeId = molecule.Id, Name = "Group1", DisplayName = "Group 1" };
        _db.ShiftGroupings.Add(shiftGrouping);
        await _db.SaveChangesAsync();

        // Link companies and job types to shift grouping
        _db.Set<ShiftGroupingCompany>().AddRange(
            new ShiftGroupingCompany { ShiftGroupingId = shiftGrouping.Id, CompanyId = company1.Id },
            new ShiftGroupingCompany { ShiftGroupingId = shiftGrouping.Id, CompanyId = company2.Id }
        );
        _db.Set<ShiftGroupingJobType>().AddRange(
            new ShiftGroupingJobType { ShiftGroupingId = shiftGrouping.Id, JobTypeId = alhutJobType.Id },
            new ShiftGroupingJobType { ShiftGroupingId = shiftGrouping.Id, JobTypeId = textJobType.Id }
        );
        await _db.SaveChangesAsync();

        // Create users with different job types
        var alhutUser = new AppUser
        {
            CompanyId = company1.Id,
            JobTypeId = alhutJobType.Id,
            Email = "alhut@test.com",
            DisplayName = "Alhut User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        var textUser = new AppUser
        {
            CompanyId = company1.Id,
            JobTypeId = textJobType.Id,
            Email = "text@test.com",
            DisplayName = "Text User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        var brUser = new AppUser
        {
            CompanyId = company1.Id,
            JobTypeId = brJobType.Id,
            Email = "br@test.com",
            DisplayName = "BR User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        var company2User = new AppUser
        {
            CompanyId = company2.Id,
            JobTypeId = alhutJobType.Id,
            Email = "alhut2@test.com",
            DisplayName = "Alhut User 2",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.AddRange(alhutUser, textUser, brUser, company2User);
        await _db.SaveChangesAsync();

        // Create shift type with job type restriction
        var shiftType = new ShiftType
        {
            CompanyId = company1.Id,
            MoleculeId = molecule.Id,
            JobTypeId = alhutJobType.Id,
            ShiftGroupingId = shiftGrouping.Id,
            Key = ShiftType.KEY_MORNING,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(shiftType);
        await _db.SaveChangesAsync();

        return new TestHierarchy(
            Project: project,
            Area: area,
            Molecule: molecule,
            Companies: new[] { company1, company2 },
            JobTypes: new Dictionary<string, JobType>
            {
                ["Alhut"] = alhutJobType,
                ["Text"] = textJobType,
                ["BR"] = brJobType
            },
            ShiftGrouping: shiftGrouping,
            Users: new Dictionary<string, AppUser>
            {
                ["alhut1"] = alhutUser,
                ["text"] = textUser,
                ["br"] = brUser,
                ["alhut2"] = company2User
            },
            ShiftType: shiftType
        );
    }

    [Fact]
    public async Task GetEligibleUsersForShiftType_WithJobTypeFilter_ReturnsOnlyMatchingJobType()
    {
        // Arrange
        var hierarchy = await SetupTestHierarchyAsync();

        // Act
        var result = await _service.GetEligibleUsersForShiftTypeAsync(
            hierarchy.ShiftType.Id,
            hierarchy.JobTypes["Alhut"].Id,
            hierarchy.ShiftGrouping.Id
        );

        // Assert
        result.Should().NotBeEmpty();
        result.Should().OnlyContain(u => u.JobTypeName == "Alhut" || u.JobTypeName == "אלחוט");
        result.Should().HaveCount(2, "Both Alhut users from companies in the shift grouping");
    }

    [Fact]
    public async Task GetEligibleUsersForShiftType_WithShiftGrouping_ReturnsOnlyGroupingCompanies()
    {
        // Arrange
        var hierarchy = await SetupTestHierarchyAsync();

        // Create a user in a company NOT in the shift grouping
        var company3 = new Company { MoleculeId = hierarchy.Molecule.Id, Name = "Company3" };
        _db.Companies.Add(company3);
        await _db.SaveChangesAsync();

        var outsideUser = new AppUser
        {
            CompanyId = company3.Id,
            JobTypeId = hierarchy.JobTypes["Alhut"].Id,
            Email = "outside@test.com",
            DisplayName = "Outside User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(outsideUser);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetEligibleUsersForShiftTypeAsync(
            hierarchy.ShiftType.Id,
            hierarchy.JobTypes["Alhut"].Id,
            hierarchy.ShiftGrouping.Id
        );

        // Assert
        result.Should().NotContain(u => u.UserId == outsideUser.Id,
            "User from company not in shift grouping should not be eligible");
    }

    [Fact]
    public async Task ValidateShiftAssignment_WithJobTypeMismatch_ReturnsInvalid()
    {
        // Arrange
        var hierarchy = await SetupTestHierarchyAsync();

        // Create a shift instance for Alhut job type
        var shiftInstance = new ShiftInstance
        {
            CompanyId = hierarchy.Companies[0].Id,
            ShiftTypeId = hierarchy.ShiftType.Id,
            WorkDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            Name = "Test Shift"
        };
        _db.ShiftInstances.Add(shiftInstance);
        await _db.SaveChangesAsync();

        // Act - Try to assign Text user to Alhut shift
        var result = await _service.ValidateShiftAssignmentAsync(
            hierarchy.Users["text"].Id,
            shiftInstance.Id
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.HasJobTypeMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateShiftAssignment_WithMatchingJobType_ReturnsValid()
    {
        // Arrange
        var hierarchy = await SetupTestHierarchyAsync();

        var shiftInstance = new ShiftInstance
        {
            CompanyId = hierarchy.Companies[0].Id,
            ShiftTypeId = hierarchy.ShiftType.Id,
            WorkDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            Name = "Test Shift"
        };
        _db.ShiftInstances.Add(shiftInstance);
        await _db.SaveChangesAsync();

        // Act - Assign Alhut user to Alhut shift
        var result = await _service.ValidateShiftAssignmentAsync(
            hierarchy.Users["alhut1"].Id,
            shiftInstance.Id
        );

        // Assert
        result.IsValid.Should().BeTrue();
        result.HasJobTypeMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateShiftAssignment_UserNotInShiftGrouping_ReturnsInvalid()
    {
        // Arrange
        var hierarchy = await SetupTestHierarchyAsync();

        // Create company outside shift grouping
        var company3 = new Company { MoleculeId = hierarchy.Molecule.Id, Name = "Company3" };
        _db.Companies.Add(company3);
        await _db.SaveChangesAsync();

        var outsideUser = new AppUser
        {
            CompanyId = company3.Id,
            JobTypeId = hierarchy.JobTypes["Alhut"].Id,
            Email = "outside@test.com",
            DisplayName = "Outside",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(outsideUser);
        await _db.SaveChangesAsync();

        var shiftInstance = new ShiftInstance
        {
            CompanyId = hierarchy.Companies[0].Id,
            ShiftTypeId = hierarchy.ShiftType.Id,
            WorkDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            Name = "Test Shift"
        };
        _db.ShiftInstances.Add(shiftInstance);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(
            outsideUser.Id,
            shiftInstance.Id
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.NotInShiftGrouping.Should().BeTrue();
    }

    [Fact]
    public async Task GetEligibleUsersForShiftType_BRShifts_AreMoleculeWide()
    {
        // Arrange
        var hierarchy = await SetupTestHierarchyAsync();

        // Create BR shift type (no JobType filter, applies to all BR users in molecule)
        var brShiftType = new ShiftType
        {
            CompanyId = hierarchy.Companies[0].Id,
            MoleculeId = hierarchy.Molecule.Id,
            JobTypeId = hierarchy.JobTypes["BR"].Id, // BR job type
            ShiftGroupingId = null, // BR shifts are molecule-wide, not grouped
            Key = ShiftType.KEY_MORNING,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(brShiftType);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetEligibleUsersForShiftTypeAsync(
            brShiftType.Id,
            hierarchy.JobTypes["BR"].Id,
            null // No shift grouping
        );

        // Assert
        result.Should().NotBeEmpty();
        result.Should().OnlyContain(u => u.JobTypeName == "BR" || u.JobTypeName == "ב\"ר");
    }

    private record TestHierarchy(
        Project Project,
        Area Area,
        Molecule Molecule,
        Company[] Companies,
        Dictionary<string, JobType> JobTypes,
        ShiftGrouping ShiftGrouping,
        Dictionary<string, AppUser> Users,
        ShiftType ShiftType
    );
}
