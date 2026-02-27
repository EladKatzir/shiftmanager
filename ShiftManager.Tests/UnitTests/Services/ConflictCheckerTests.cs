using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for ConflictChecker.CanAssignAsync — validates rest-period,
/// overlap, and time-off conflict detection logic.
///
/// CC-01: Detects rest violation between consecutive shifts
/// CC-02: No violation when gap exceeds RestHours
/// CC-03: Detects double-booking on same date and time (overlap)
/// </summary>
public class ConflictCheckerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ConflictChecker _sut;
    private readonly Mock<IAppConfigCacheService> _configCacheMock;
    private readonly Mock<ILogger<ConflictChecker>> _loggerMock;

    // Default test company ID (set during setup)
    private int _companyId;

    public ConflictCheckerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _configCacheMock = new Mock<IAppConfigCacheService>();
        _loggerMock = new Mock<ILogger<ConflictChecker>>();
        _sut = new ConflictChecker(_db, _configCacheMock.Object, _loggerMock.Object);

        // Default config: 8h rest, 40h weekly cap, Sunday week start
        _configCacheMock.Setup(x => x.GetConfigAsync(It.IsAny<int>(), "RestHours"))
            .ReturnsAsync(new AppConfig { Value = "8" });
        _configCacheMock.Setup(x => x.GetConfigAsync(It.IsAny<int>(), "WeeklyHoursCap"))
            .ReturnsAsync(new AppConfig { Value = "40" });
        _configCacheMock.Setup(x => x.GetConfigAsync(It.IsAny<int>(), "WeekStartDay"))
            .ReturnsAsync(new AppConfig { Value = "0" });
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    /// <summary>
    /// Creates the minimal hierarchy + active user + shift types needed for conflict tests.
    /// Returns (userId, morningShiftType, nightShiftType).
    /// </summary>
    private async Task<(int userId, ShiftType morning, ShiftType night)> SetupBaseEntitiesAsync()
    {
        var project = new Project { Name = "TestProject", DisplayName = "Test" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "TestArea", DisplayName = "Test Area" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        var molecule = new Molecule { AreaId = area.Id, Name = "TestMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        var company = new Company { MoleculeId = molecule.Id, Name = "TestCompany", DisplayName = "Test Company" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        _companyId = company.Id;

        var user = new AppUser
        {
            CompanyId = company.Id,
            Email = "test@test.com",
            DisplayName = "Test User",
            IsActive = true,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Morning shift: 07:00 - 15:00 (8 hours)
        var morning = new ShiftType
        {
            CompanyId = company.Id,
            Key = ShiftType.KEY_MORNING,
            CustomName = "Morning",
            Start = new TimeOnly(7, 0),
            End = new TimeOnly(15, 0)
        };

        // Night shift: 22:00 - 06:00 (wraps midnight, 8 hours)
        var night = new ShiftType
        {
            CompanyId = company.Id,
            Key = ShiftType.KEY_NIGHT,
            CustomName = "Night",
            Start = new TimeOnly(22, 0),
            End = new TimeOnly(6, 0)
        };

        _db.ShiftTypes.AddRange(morning, night);
        await _db.SaveChangesAsync();

        return (user.Id, morning, night);
    }

    /// <summary>
    /// Creates a ShiftInstance and assigns a user to it.
    /// </summary>
    private async Task<ShiftAssignment> CreateAssignmentAsync(int userId, ShiftType shiftType, DateOnly date)
    {
        var instance = new ShiftInstance
        {
            CompanyId = _companyId,
            ShiftTypeId = shiftType.Id,
            WorkDate = date,
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(instance);
        await _db.SaveChangesAsync();

        var assignment = new ShiftAssignment
        {
            CompanyId = _companyId,
            ShiftInstanceId = instance.Id,
            UserId = userId
        };
        _db.ShiftAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        return assignment;
    }

    // -----------------------------------------------------------------------
    // CC-01  Detects rest violation between consecutive shifts
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CC01_RestViolation_WhenGapLessThanRestHours_ReturnsConflict()
    {
        // Arrange: Night shift 22:00-06:00 on day 1, then morning shift 07:00-15:00 on day 2.
        // Gap = 1 hour (06:00 → 07:00), RestHours = 8 → CONFLICT.
        var (userId, morning, night) = await SetupBaseEntitiesAsync();
        var testDate = new DateOnly(2026, 3, 1); // Sunday

        // Existing assignment: Night shift on March 1 (22:00 Mar 1 → 06:00 Mar 2)
        await CreateAssignmentAsync(userId, night, testDate);

        // New shift to check: Morning shift on March 2 (07:00 → 15:00)
        var newInstance = new ShiftInstance
        {
            CompanyId = _companyId,
            ShiftTypeId = morning.Id,
            WorkDate = testDate.AddDays(1),
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(newInstance);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CanAssignAsync(userId, newInstance);

        // Assert
        result.Allowed.Should().BeFalse("gap between night end (06:00) and morning start (07:00) is only 1h, less than RestHours=8");
        result.ErrorCode.Should().Be("CONFLICT_REST_PERIOD");
        result.Reasons.Should().ContainSingle().Which.Should().Contain("Rest period");
    }

    // -----------------------------------------------------------------------
    // CC-02  No violation when gap exceeds RestHours
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CC02_NoRestViolation_WhenGapExceedsRestHours_ReturnsAllowed()
    {
        // Arrange: Night shift 22:00-06:00 on day 1, then afternoon (16:00-22:00) on day 2.
        // Gap = 10 hours (06:00 → 16:00), RestHours = 8 → ALLOWED.
        var (userId, morning, night) = await SetupBaseEntitiesAsync();
        var testDate = new DateOnly(2026, 3, 1); // Sunday

        // Create an afternoon shift type (16:00 - 22:00)
        var afternoon = new ShiftType
        {
            CompanyId = _companyId,
            Key = ShiftType.KEY_AFTERNOON,
            CustomName = "Afternoon",
            Start = new TimeOnly(16, 0),
            End = new TimeOnly(22, 0)
        };
        _db.ShiftTypes.Add(afternoon);
        await _db.SaveChangesAsync();

        // Existing assignment: Night shift on March 1 (22:00 Mar 1 → 06:00 Mar 2)
        await CreateAssignmentAsync(userId, night, testDate);

        // New shift to check: Afternoon on March 2 (16:00 → 22:00)
        var newInstance = new ShiftInstance
        {
            CompanyId = _companyId,
            ShiftTypeId = afternoon.Id,
            WorkDate = testDate.AddDays(1),
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(newInstance);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CanAssignAsync(userId, newInstance);

        // Assert
        result.Allowed.Should().BeTrue("gap between night end (06:00) and afternoon start (16:00) is 10h, exceeding RestHours=8");
        result.ErrorCode.Should().BeNull();
        result.Reasons.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // CC-03  Detects double-booking on same date and time (overlap)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CC03_Overlap_WhenShiftsOverlapOnSameDate_ReturnsConflict()
    {
        // Arrange: User assigned to morning 07:00-15:00 on March 1.
        // Try to assign to a custom shift 08:00-14:00 on same date → OVERLAP.
        var (userId, morning, night) = await SetupBaseEntitiesAsync();
        var testDate = new DateOnly(2026, 3, 1); // Sunday

        // Create a custom overlapping shift type (08:00 - 14:00)
        var customShift = new ShiftType
        {
            CompanyId = _companyId,
            Key = "CUSTOM_MID",
            CustomName = "Mid Shift",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(14, 0)
        };
        _db.ShiftTypes.Add(customShift);
        await _db.SaveChangesAsync();

        // Existing assignment: Morning shift on March 1
        await CreateAssignmentAsync(userId, morning, testDate);

        // New shift to check: Custom 08:00-14:00 on same date
        var newInstance = new ShiftInstance
        {
            CompanyId = _companyId,
            ShiftTypeId = customShift.Id,
            WorkDate = testDate,
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(newInstance);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CanAssignAsync(userId, newInstance);

        // Assert
        result.Allowed.Should().BeFalse("08:00-14:00 overlaps with existing 07:00-15:00 assignment on the same date");
        result.ErrorCode.Should().Be("CONFLICT_OVERLAP");
        result.Reasons.Should().ContainSingle().Which.Should().Contain("Overlap");
    }
}
