using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class ShiftCalendarServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ShiftCalendarService _service;
    private readonly Mock<ICompanyCacheService> _companyCacheMock;

    public ShiftCalendarServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _companyCacheMock = new Mock<ICompanyCacheService>();
        var logger = Mock.Of<ILogger<ShiftCalendarService>>();

        _service = new ShiftCalendarService(_db, logger, _companyCacheMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    #region Test Hierarchy Setup

    private async Task<TestData> SetupTestDataAsync()
    {
        var area = new Area { Id = 1, ProjectId = 1, Name = "TestArea", DisplayName = "Test Area" };
        _db.Areas.Add(area);

        var jobType = new JobType { Id = 1, AreaId = 1, Name = "Alhut", DisplayName = "Alhut", SortOrder = 1 };
        _db.JobTypes.Add(jobType);

        var molecule = new Molecule { Id = 1, AreaId = 1, Name = "TestMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);

        var company = new Company { Id = 1, MoleculeId = 1, Name = "Company1", DisplayName = "Company 1" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        var user1 = new AppUser
        {
            Id = 1, Email = "user1@test.com", DisplayName = "User One",
            CompanyId = 1, IsActive = true, JobTypeId = 1, Role = UserRole.Employee
        };
        var user2 = new AppUser
        {
            Id = 2, Email = "user2@test.com", DisplayName = "User Two",
            CompanyId = 1, IsActive = true, JobTypeId = 1, Role = UserRole.Employee
        };
        _db.Users.AddRange(user1, user2);
        await _db.SaveChangesAsync();

        var shiftType = new ShiftType
        {
            Id = 1, Key = ShiftType.KEY_MORNING, MoleculeId = 1, JobTypeId = 1,
            Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0)
        };
        var shiftTypeNight = new ShiftType
        {
            Id = 2, Key = ShiftType.KEY_NIGHT, MoleculeId = 1, JobTypeId = 1,
            Start = new TimeOnly(23, 0), End = new TimeOnly(7, 0)
        };
        var shiftTypeNoJob = new ShiftType
        {
            Id = 3, Key = ShiftType.KEY_OFFLINE, MoleculeId = 1, JobTypeId = null,
            Start = new TimeOnly(0, 0), End = new TimeOnly(0, 0)
        };
        _db.ShiftTypes.AddRange(shiftType, shiftTypeNight, shiftTypeNoJob);
        await _db.SaveChangesAsync();

        return new TestData(area, molecule, company, jobType, user1, user2, shiftType, shiftTypeNight, shiftTypeNoJob);
    }

    private record TestData(
        Area Area, Molecule Molecule, Company Company, JobType JobType,
        AppUser User1, AppUser User2,
        ShiftType MorningShift, ShiftType NightShift, ShiftType OfflineShift);

    #endregion

    // --- GetUsersForCalendarAsync ---

    [Fact]
    public async Task GetUsersForCalendarAsync_ReturnsUsersInMolecule()
    {
        await SetupTestDataAsync();

        var result = await _service.GetUsersForCalendarAsync(moleculeId: 1, jobTypeId: 1);

        result.Should().HaveCount(2);
        result.Should().BeInAscendingOrder(u => u.DisplayName);
    }

    [Fact]
    public async Task GetUsersForCalendarAsync_ExcludesInactiveUsers()
    {
        await SetupTestDataAsync();
        var inactiveUser = new AppUser
        {
            Id = 10, Email = "inactive@test.com", DisplayName = "Inactive",
            CompanyId = 1, IsActive = false, JobTypeId = 1, Role = UserRole.Employee
        };
        _db.Users.Add(inactiveUser);
        await _db.SaveChangesAsync();

        var result = await _service.GetUsersForCalendarAsync(moleculeId: 1, jobTypeId: 1);

        result.Should().HaveCount(2);
        result.Should().NotContain(u => u.Id == 10);
    }

    [Fact]
    public async Task GetUsersForCalendarAsync_WithNullJobTypeId_ReturnsAllActiveUsersInMolecule()
    {
        await SetupTestDataAsync();

        var result = await _service.GetUsersForCalendarAsync(moleculeId: 1, jobTypeId: null);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetUsersForCalendarAsync_WrongMolecule_ReturnsEmpty()
    {
        await SetupTestDataAsync();

        var result = await _service.GetUsersForCalendarAsync(moleculeId: 999, jobTypeId: 1);

        result.Should().BeEmpty();
    }

    // --- GetShiftInstancesAsync ---

    [Fact]
    public async Task GetShiftInstancesAsync_ReturnsInstancesInDateRange()
    {
        var data = await SetupTestDataAsync();
        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 2);
        var date3 = new DateOnly(2026, 3, 10); // outside range

        _db.ShiftInstances.AddRange(
            new ShiftInstance { Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date1, StaffingRequired = 2 },
            new ShiftInstance { Id = 2, CompanyId = 1, ShiftTypeId = 1, WorkDate = date2, StaffingRequired = 2 },
            new ShiftInstance { Id = 3, CompanyId = 1, ShiftTypeId = 1, WorkDate = date3, StaffingRequired = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetShiftInstancesAsync(
            moleculeId: 1, jobTypeId: 1, start: date1, end: date2);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(si => si.WorkDate >= date1 && si.WorkDate <= date2);
    }

    [Fact]
    public async Task GetShiftInstancesAsync_WithNoData_ReturnsEmpty()
    {
        await SetupTestDataAsync();

        var result = await _service.GetShiftInstancesAsync(
            moleculeId: 1, jobTypeId: 1,
            start: new DateOnly(2026, 6, 1), end: new DateOnly(2026, 6, 30));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetShiftInstancesAsync_IncludesNullJobTypeShifts_WhenJobTypeSpecified()
    {
        // Shifts with null JobType (like Offline/Home) should appear when any jobType is filtered
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 1);

        _db.ShiftInstances.AddRange(
            new ShiftInstance { Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2 },  // Morning, JobType=1
            new ShiftInstance { Id = 2, CompanyId = 1, ShiftTypeId = 3, WorkDate = date, StaffingRequired = 1 }   // Offline, JobType=null
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetShiftInstancesAsync(moleculeId: 1, jobTypeId: 1, start: date, end: date);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetShiftInstancesAsync_NullJobType_ReturnsOnlyNullJobTypeInstances()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 1);

        _db.ShiftInstances.AddRange(
            new ShiftInstance { Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2 },
            new ShiftInstance { Id = 2, CompanyId = 1, ShiftTypeId = 3, WorkDate = date, StaffingRequired = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetShiftInstancesAsync(moleculeId: 1, jobTypeId: null, start: date, end: date);

        result.Should().HaveCount(1);
        result.First().ShiftTypeId.Should().Be(3); // Offline (null JobType)
    }

    // --- GetCapacityAsync ---

    [Fact]
    public async Task GetCapacityAsync_ReturnsOverrideCapacity_WhenOverrideExists()
    {
        await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 1);

        _db.ShiftCapacityOverrides.Add(new ShiftCapacityOverride
        {
            Id = 1, ShiftTypeId = 1, MoleculeId = 1, JobTypeId = 1,
            Date = date, Capacity = 5, CreatedByUserId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetCapacityAsync(shiftTypeId: 1, moleculeId: 1, jobTypeId: 1, date: date);

        result.Should().Be(5);
    }

    [Fact]
    public async Task GetCapacityAsync_ReturnsDefaultCapacity_WhenNoOverrideExists()
    {
        await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 1);

        // Add a shift instance to provide default capacity
        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 3
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetCapacityAsync(shiftTypeId: 1, moleculeId: 1, jobTypeId: 1, date: date);

        result.Should().Be(3);
    }

    [Fact]
    public async Task GetDefaultCapacityAsync_Returns1_WhenNoShiftTypeOrInstance()
    {
        await SetupTestDataAsync();

        var result = await _service.GetDefaultCapacityAsync(shiftTypeId: 999);

        result.Should().Be(1); // DEFAULT_STAFFING_REQUIRED
    }

    // --- SetCapacityOverrideAsync ---

    [Fact]
    public async Task SetCapacityOverrideAsync_CreatesNewOverride()
    {
        await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 1);

        await _service.SetCapacityOverrideAsync(shiftTypeId: 1, moleculeId: 1, jobTypeId: 1, date: date, capacity: 7, userId: 1);

        var saved = await _db.ShiftCapacityOverrides.FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.Capacity.Should().Be(7);
    }

    [Fact]
    public async Task SetCapacityOverrideAsync_UpdatesExistingOverride()
    {
        await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 1);

        _db.ShiftCapacityOverrides.Add(new ShiftCapacityOverride
        {
            Id = 1, ShiftTypeId = 1, MoleculeId = 1, JobTypeId = 1,
            Date = date, Capacity = 3, CreatedByUserId = 1
        });
        await _db.SaveChangesAsync();

        await _service.SetCapacityOverrideAsync(shiftTypeId: 1, moleculeId: 1, jobTypeId: 1, date: date, capacity: 10, userId: 2);

        var saved = await _db.ShiftCapacityOverrides.FirstOrDefaultAsync();
        saved!.Capacity.Should().Be(10);
        saved.CreatedByUserId.Should().Be(2);
    }

    // --- RemoveCapacityOverrideAsync ---

    [Fact]
    public async Task RemoveCapacityOverrideAsync_RemovesExistingOverride()
    {
        await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 1);

        _db.ShiftCapacityOverrides.Add(new ShiftCapacityOverride
        {
            Id = 1, ShiftTypeId = 1, MoleculeId = 1, JobTypeId = 1,
            Date = date, Capacity = 5, CreatedByUserId = 1
        });
        await _db.SaveChangesAsync();

        await _service.RemoveCapacityOverrideAsync(shiftTypeId: 1, moleculeId: 1, jobTypeId: 1, date: date);

        var count = await _db.ShiftCapacityOverrides.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task RemoveCapacityOverrideAsync_DoesNothing_WhenNoOverrideExists()
    {
        await SetupTestDataAsync();

        // Should not throw
        await _service.RemoveCapacityOverrideAsync(shiftTypeId: 1, moleculeId: 1, jobTypeId: 1, date: new DateOnly(2026, 3, 1));

        var count = await _db.ShiftCapacityOverrides.CountAsync();
        count.Should().Be(0);
    }

    // --- GetCapacitiesBatchAsync ---

    [Fact]
    public async Task GetCapacitiesBatchAsync_ReturnsBatchCapacities()
    {
        await SetupTestDataAsync();
        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 2);

        _db.ShiftInstances.AddRange(
            new ShiftInstance { Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date1, StaffingRequired = 2 },
            new ShiftInstance { Id = 2, CompanyId = 1, ShiftTypeId = 1, WorkDate = date2, StaffingRequired = 3 }
        );
        // Override for date1 only
        _db.ShiftCapacityOverrides.Add(new ShiftCapacityOverride
        {
            Id = 1, ShiftTypeId = 1, MoleculeId = 1, JobTypeId = 1,
            Date = date1, Capacity = 10, CreatedByUserId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetCapacitiesBatchAsync(moleculeId: 1, jobTypeId: 1, start: date1, end: date2);

        result.Should().HaveCount(2);
        result[(1, date1)].Should().Be(10); // Override
        result[(1, date2)].Should().Be(3);  // Instance default
    }

    // --- GetEligibleUsersForShiftTypeAsync ---

    [Fact]
    public async Task GetEligibleUsersForShiftTypeAsync_ReturnsAllUsers_WhenNoEligibilityFilters()
    {
        await SetupTestDataAsync();

        var result = await _service.GetEligibleUsersForShiftTypeAsync(moleculeId: 1, shiftTypeId: 1);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetEligibleUsersForShiftTypeAsync_ReturnsEmpty_WhenShiftTypeNotFound()
    {
        await SetupTestDataAsync();

        var result = await _service.GetEligibleUsersForShiftTypeAsync(moleculeId: 1, shiftTypeId: 999);

        result.Should().BeEmpty();
    }

    // --- UnassignUserAsync ---

    [Fact]
    public async Task UnassignUserAsync_RemovesAssignment()
    {
        await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 1);

        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2
        });
        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 1, CompanyId = 1, ShiftInstanceId = 1, UserId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.UnassignUserAsync(shiftAssignmentId: 1, unassignedByUserId: 2);

        result.Should().BeTrue();
        var remaining = await _db.ShiftAssignments.CountAsync();
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task UnassignUserAsync_ReturnsFalse_WhenAssignmentNotFound()
    {
        await SetupTestDataAsync();

        var result = await _service.UnassignUserAsync(shiftAssignmentId: 999, unassignedByUserId: 1);

        result.Should().BeFalse();
    }

    // --- CheckRestViolationsAsync ---

    [Fact]
    public async Task CheckRestViolationsAsync_ReturnsEmpty_WhenNoPreviousDayShifts()
    {
        await SetupTestDataAsync();

        var result = await _service.CheckRestViolationsAsync(userId: 1, date: new DateOnly(2026, 3, 5), shiftTypeId: 1);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckRestViolationsAsync_ReturnsEmpty_WhenShiftTypeNotFound()
    {
        await SetupTestDataAsync();

        var result = await _service.CheckRestViolationsAsync(userId: 1, date: new DateOnly(2026, 3, 5), shiftTypeId: 999);

        result.Should().BeEmpty();
    }

    // --- GetAssignmentsAsync ---

    [Fact]
    public async Task GetAssignmentsAsync_ReturnsAssignmentsInDateRange()
    {
        var data = await SetupTestDataAsync();
        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 2);

        _db.ShiftInstances.AddRange(
            new ShiftInstance { Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date1, StaffingRequired = 2 },
            new ShiftInstance { Id = 2, CompanyId = 1, ShiftTypeId = 1, WorkDate = date2, StaffingRequired = 2 }
        );
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 1, CompanyId = 1, ShiftInstanceId = 1, UserId = 1 },
            new ShiftAssignment { Id = 2, CompanyId = 1, ShiftInstanceId = 2, UserId = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetAssignmentsAsync(moleculeId: 1, jobTypeId: 1, start: date1, end: date2);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAssignmentsAsync_ReturnsEmpty_WhenNoAssignments()
    {
        await SetupTestDataAsync();

        var result = await _service.GetAssignmentsAsync(
            moleculeId: 1, jobTypeId: 1,
            start: new DateOnly(2026, 6, 1), end: new DateOnly(2026, 6, 30));

        result.Should().BeEmpty();
    }
}
