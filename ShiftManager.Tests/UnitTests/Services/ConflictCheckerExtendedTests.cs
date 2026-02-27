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
/// Extended ConflictChecker tests — covers weekly hours cap,
/// offline shift coexistence, and time-off blocking.
///
/// AF-U01: Weekly hours cap exceeded → blocked
/// AF-U02: Offline shift coexists with regular shift
/// AF-U03: Time-off blocks assignment
/// </summary>
public class ConflictCheckerExtendedTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ConflictChecker _sut;
    private readonly Mock<IAppConfigCacheService> _configCacheMock;
    private readonly Mock<ILogger<ConflictChecker>> _loggerMock;

    private int _companyId;

    public ConflictCheckerExtendedTests()
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
            .ReturnsAsync(new AppConfig { Value = "0" }); // Sunday
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<(int userId, ShiftType morning, ShiftType night, ShiftType offline)> SetupBaseEntitiesAsync()
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
            Email = "conflict-test@test.com",
            DisplayName = "Conflict Test User",
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

        // Offline shift: no start/end times (zero-length shift for tracking)
        var offline = new ShiftType
        {
            CompanyId = company.Id,
            Key = ShiftType.KEY_OFFLINE,
            CustomName = "Offline",
            Start = new TimeOnly(0, 0),
            End = new TimeOnly(0, 0)
        };

        _db.ShiftTypes.AddRange(morning, night, offline);
        await _db.SaveChangesAsync();

        return (user.Id, morning, night, offline);
    }

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
    // AF-U01  Weekly hours cap exceeded → blocked
    // -----------------------------------------------------------------------
    [Fact]
    public async Task AFU01_WeeklyHoursCap_WhenExceeded_ReturnsConflict()
    {
        // Arrange: User already assigned to 5 morning shifts (5 × 8h = 40h) in one week.
        // Adding a 6th (48h) would exceed the 40h cap.
        var (userId, morning, night, offline) = await SetupBaseEntitiesAsync();
        var weekStart = new DateOnly(2026, 3, 1); // Sunday

        // Fill 5 days: Sun-Thu
        for (int i = 0; i < 5; i++)
        {
            await CreateAssignmentAsync(userId, morning, weekStart.AddDays(i));
        }

        // New shift to check: 6th morning on Friday (same week)
        var newInstance = new ShiftInstance
        {
            CompanyId = _companyId,
            ShiftTypeId = morning.Id,
            WorkDate = weekStart.AddDays(5), // Friday
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(newInstance);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CanAssignAsync(userId, newInstance);

        // Assert: Should be blocked by weekly hours cap
        result.Allowed.Should().BeFalse("48h exceeds WeeklyHoursCap=40h");
        result.ErrorCode.Should().Be("CONFLICT_WEEKLY_CAP");
    }

    // -----------------------------------------------------------------------
    // AF-U02  Offline shift coexists with regular shift (no overlap)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task AFU02_OfflineShift_CoexistsWithRegularShift()
    {
        // Arrange: User has a morning shift. Offline shifts (0:00-0:00) should
        // NOT trigger overlap or rest violation because they have zero duration.
        var (userId, morning, night, offline) = await SetupBaseEntitiesAsync();
        var testDate = new DateOnly(2026, 3, 1);

        await CreateAssignmentAsync(userId, morning, testDate);

        // New: Offline shift on same date
        var newInstance = new ShiftInstance
        {
            CompanyId = _companyId,
            ShiftTypeId = offline.Id,
            WorkDate = testDate,
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(newInstance);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CanAssignAsync(userId, newInstance);

        // Assert: Offline shifts should always be allowed (0-duration = no overlap)
        result.Allowed.Should().BeTrue("Offline shift (0:00-0:00) should coexist with regular shifts");
    }

    // -----------------------------------------------------------------------
    // AF-U03  Time-off blocks assignment
    // -----------------------------------------------------------------------
    [Fact]
    public async Task AFU03_TimeOff_BlocksAssignment()
    {
        // Arrange: User has approved time-off for a date. Assignment should be blocked.
        var (userId, morning, night, offline) = await SetupBaseEntitiesAsync();
        var testDate = new DateOnly(2026, 3, 1);

        // Create approved time-off request covering the test date
        var timeOff = new TimeOffRequest
        {
            UserId = userId,
            CompanyId = _companyId,
            StartDate = testDate,
            EndDate = testDate,
            Status = RequestStatus.Approved,
            Reason = "Vacation",
            CreatedAt = DateTime.UtcNow
        };
        _db.TimeOffRequests.Add(timeOff);
        await _db.SaveChangesAsync();

        // New shift to check on the time-off date
        var newInstance = new ShiftInstance
        {
            CompanyId = _companyId,
            ShiftTypeId = morning.Id,
            WorkDate = testDate,
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(newInstance);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CanAssignAsync(userId, newInstance);

        // Assert: Should be blocked by time-off
        result.Allowed.Should().BeFalse("Approved time-off should block shift assignment");
        result.ErrorCode.Should().Be("CONFLICT_TIME_OFF");
    }
}
