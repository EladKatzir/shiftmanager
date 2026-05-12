using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for AssignShiftAsync — covers assignment creation, error handling,
/// and override token flow.
/// </summary>
public class AssignShiftAsyncTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly ShiftAssignmentService _service;
    private const string HmacSecret = "test-hmac-secret-for-unit-tests";

    public AssignShiftAsyncTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

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
        var auditLogService = Mock.Of<IAuditLogService>();
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ApiKeyHmacSecret"]).Returns(HmacSecret);
        var configCacheMock = Mock.Of<IAppConfigCacheService>();

        _service = new ShiftAssignmentService(
            _db, localizer, logger,
            hierarchySettingsServiceMock.Object,
            auditLogService,
            configMock.Object,
            configCacheMock,
            BusyServiceMockFactory.Real(_db, configMock.Object, restHours: 11, weeklyCap: 48));
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    /// <summary>
    /// Creates a minimal hierarchy: Project → Area → Molecule → Company → User + ShiftType + ShiftInstance.
    /// The shift type has NO ShiftGroupingId and the user's JobType matches the shift,
    /// so validation passes cleanly (no errors, no warnings).
    /// </summary>
    private async Task<TestData> SetupCleanAssignmentAsync()
    {
        var project = new Project { Name = "TestProject", DisplayName = "Test" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "TestArea", DisplayName = "Test" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        var jobType = new JobType { AreaId = area.Id, Name = "Alhut", DisplayName = "Alhut", SortOrder = 1 };
        _db.JobTypes.Add(jobType);
        await _db.SaveChangesAsync();

        var molecule = new Molecule { AreaId = area.Id, Name = "TestMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        var company = new Company { MoleculeId = molecule.Id, Name = "Company1", DisplayName = "Company 1" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        var user = new AppUser
        {
            CompanyId = company.Id,
            JobTypeId = jobType.Id,
            Email = "user@test.com",
            DisplayName = "Test User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        var managerUser = new AppUser
        {
            CompanyId = company.Id,
            JobTypeId = jobType.Id,
            Email = "manager@test.com",
            DisplayName = "Manager",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.AddRange(user, managerUser);
        await _db.SaveChangesAsync();

        // ShiftType: molecule-scoped, matching jobType, no shift grouping → no warnings
        var shiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = molecule.Id,
            JobTypeId = jobType.Id,
            ShiftGroupingId = null,
            Key = ShiftType.KEY_MORNING,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(shiftType);
        await _db.SaveChangesAsync();

        var shiftInstance = new ShiftInstance
        {
            CompanyId = company.Id,
            ShiftTypeId = shiftType.Id,
            WorkDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            Name = "Morning Shift",
            StaffingRequired = 2
        };
        _db.ShiftInstances.Add(shiftInstance);
        await _db.SaveChangesAsync();

        return new TestData(company, user, managerUser, jobType, molecule, shiftType, shiftInstance);
    }

    /// <summary>
    /// Creates a hierarchy where the user's JobType differs from the ShiftType's JobType,
    /// which triggers a JOB_TYPE_MISMATCH warning during validation.
    /// </summary>
    private async Task<TestData> SetupWarningAssignmentAsync()
    {
        var project = new Project { Name = "WarnProject", DisplayName = "Warn" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "WarnArea", DisplayName = "Warn" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        var alhutJobType = new JobType { AreaId = area.Id, Name = "Alhut", DisplayName = "Alhut", SortOrder = 1 };
        var textJobType = new JobType { AreaId = area.Id, Name = "Text", DisplayName = "Text", SortOrder = 2 };
        _db.JobTypes.AddRange(alhutJobType, textJobType);
        await _db.SaveChangesAsync();

        var molecule = new Molecule { AreaId = area.Id, Name = "WarnMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        var company = new Company { MoleculeId = molecule.Id, Name = "WarnCompany", DisplayName = "Warn Company" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        // User has Text job type, but shift requires Alhut → JOB_TYPE_MISMATCH warning
        var user = new AppUser
        {
            CompanyId = company.Id,
            JobTypeId = textJobType.Id,
            Email = "text-user@test.com",
            DisplayName = "Text User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        var manager = new AppUser
        {
            CompanyId = company.Id,
            JobTypeId = alhutJobType.Id,
            Email = "mgr@test.com",
            DisplayName = "Manager",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.AddRange(user, manager);
        await _db.SaveChangesAsync();

        var shiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = molecule.Id,
            JobTypeId = alhutJobType.Id, // Alhut — mismatches user's Text
            ShiftGroupingId = null,
            Key = ShiftType.KEY_MORNING,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(shiftType);
        await _db.SaveChangesAsync();

        var shiftInstance = new ShiftInstance
        {
            CompanyId = company.Id,
            ShiftTypeId = shiftType.Id,
            WorkDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            Name = "Morning Shift",
            StaffingRequired = 2
        };
        _db.ShiftInstances.Add(shiftInstance);
        await _db.SaveChangesAsync();

        return new TestData(company, user, manager, textJobType, molecule, shiftType, shiftInstance);
    }

    [Fact]
    public async Task AssignShift_Succeeds_WhenNoErrorsOrWarnings()
    {
        // Arrange
        var data = await SetupCleanAssignmentAsync();

        // Act
        var result = await _service.AssignShiftAsync(
            data.User.Id,
            data.ShiftInstance.Id,
            data.Manager.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.AssignmentId.Should().NotBeNull();
        result.ErrorKey.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task AssignShift_Fails_WhenValidationReturnsErrors()
    {
        // Arrange — use a userId that does not exist in the database
        var data = await SetupCleanAssignmentAsync();
        int nonExistentUserId = 99999;

        // Act
        var result = await _service.AssignShiftAsync(
            nonExistentUserId,
            data.ShiftInstance.Id,
            data.Manager.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.AssignmentId.Should().BeNull();
        result.ErrorKey.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task AssignShift_RequiresOverride_WhenWarningsExist()
    {
        // Arrange — user's JobType mismatches the shift's JobType → JOB_TYPE_MISMATCH warning
        var data = await SetupWarningAssignmentAsync();

        // Act — no override token provided
        var result = await _service.AssignShiftAsync(
            data.User.Id,
            data.ShiftInstance.Id,
            data.Manager.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("WARNINGS_REQUIRE_OVERRIDE");
        result.Validation.Should().NotBeNull();
        result.Validation!.Warnings.Should().Contain(w => w.Key == "JOB_TYPE_MISMATCH");
    }

    [Fact]
    public async Task AssignShift_Succeeds_WithValidOverrideToken()
    {
        // Arrange — same warning scenario, but provide a valid override token
        var data = await SetupWarningAssignmentAsync();

        // Generate a valid override token using the service's own method
        var token = _service.GenerateOverrideToken(
            data.ShiftInstance.Id,
            data.User.Id,
            new List<string> { "JOB_TYPE_MISMATCH" });

        // Act
        var result = await _service.AssignShiftAsync(
            data.User.Id,
            data.ShiftInstance.Id,
            data.Manager.Id,
            overrideToken: token);

        // Assert
        result.Success.Should().BeTrue();
        result.AssignmentId.Should().NotBeNull();
        result.ErrorKey.Should().BeNull();
    }

    [Fact]
    public async Task AssignShift_CreatesAssignmentInDatabase()
    {
        // Arrange
        var data = await SetupCleanAssignmentAsync();

        // Act
        var result = await _service.AssignShiftAsync(
            data.User.Id,
            data.ShiftInstance.Id,
            data.Manager.Id);

        // Assert — verify the record actually exists in the database
        result.Success.Should().BeTrue("precondition: assignment should succeed");

        var assignment = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(sa => sa.Id == result.AssignmentId);

        assignment.Should().NotBeNull();
        assignment!.UserId.Should().Be(data.User.Id);
        assignment.ShiftInstanceId.Should().Be(data.ShiftInstance.Id);
        assignment.CompanyId.Should().Be(data.Company.Id);
    }

    private record TestData(
        Company Company,
        AppUser User,
        AppUser Manager,
        JobType JobType,
        Molecule Molecule,
        ShiftType ShiftType,
        ShiftInstance ShiftInstance
    );
}
