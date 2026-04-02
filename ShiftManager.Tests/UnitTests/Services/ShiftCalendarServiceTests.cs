using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);
        _companyCacheMock = new Mock<ICompanyCacheService>();
        var logger = Mock.Of<ILogger<ShiftCalendarService>>();

        var localizationMock = new Mock<ICompanyLocalizationService>();
        localizationMock.Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);

        _service = new ShiftCalendarService(_db, logger, _companyCacheMock.Object, localizationMock.Object);
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

    // --- AssignUserAsync ---

    [Fact]
    public async Task AssignUserAsync_HappyPath_AssignsUserAndReturnsSuccess()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 5);

        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2
        });
        await _db.SaveChangesAsync();

        // Mock ICompanyCacheService to return the company (with correct MoleculeId)
        _companyCacheMock
            .Setup(c => c.GetCompanyAsync(1))
            .ReturnsAsync(data.Company);

        var result = await _service.AssignUserAsync(shiftInstanceId: 1, userId: 1, assignedByUserId: 2);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        var assignment = await _db.ShiftAssignments.FirstOrDefaultAsync();
        assignment.Should().NotBeNull();
        assignment!.UserId.Should().Be(1);
        assignment.ShiftInstanceId.Should().Be(1);
    }

    [Fact]
    public async Task AssignUserAsync_ReturnsFalse_WhenShiftInstanceNotFound()
    {
        await SetupTestDataAsync();

        var result = await _service.AssignUserAsync(shiftInstanceId: 999, userId: 1, assignedByUserId: 2);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Shift instance not found");
    }

    [Fact]
    public async Task AssignUserAsync_ReturnsFalse_WhenUserNotFound()
    {
        await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 5);

        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2
        });
        await _db.SaveChangesAsync();

        var result = await _service.AssignUserAsync(shiftInstanceId: 1, userId: 999, assignedByUserId: 2);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("User not found");
    }

    [Fact]
    public async Task AssignUserAsync_ReturnsFalse_WhenUserNotInMolecule()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 5);

        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2
        });
        await _db.SaveChangesAsync();

        // Return a company with different MoleculeId
        var otherCompany = new Company { Id = 99, MoleculeId = 99, Name = "Other", DisplayName = "Other" };
        _companyCacheMock
            .Setup(c => c.GetCompanyAsync(1))
            .ReturnsAsync(otherCompany);

        var result = await _service.AssignUserAsync(shiftInstanceId: 1, userId: 1, assignedByUserId: 2);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("molecule");
    }

    [Fact]
    public async Task AssignUserAsync_ReturnsFalse_WhenDuplicateAssignment()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 5);

        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2
        });
        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 1, CompanyId = 1, ShiftInstanceId = 1, UserId = 1
        });
        await _db.SaveChangesAsync();

        _companyCacheMock
            .Setup(c => c.GetCompanyAsync(1))
            .ReturnsAsync(data.Company);

        var result = await _service.AssignUserAsync(shiftInstanceId: 1, userId: 1, assignedByUserId: 2);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already assigned");
    }

    [Fact]
    public async Task AssignUserAsync_ReturnsFalse_WhenCompanyCacheReturnsNull()
    {
        await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 5);

        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2
        });
        await _db.SaveChangesAsync();

        _companyCacheMock
            .Setup(c => c.GetCompanyAsync(1))
            .ReturnsAsync((Company?)null);

        var result = await _service.AssignUserAsync(shiftInstanceId: 1, userId: 1, assignedByUserId: 2);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("molecule");
    }

    // --- GetOverlaysAsync ---

    [Fact]
    public async Task GetOverlaysAsync_ReturnsEmpty_WhenNoUsersInMolecule()
    {
        // No users seeded for molecule 99
        await SetupTestDataAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 99,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverlaysAsync_ReturnsVacationOverlay_WhenApprovedTimeOff()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 1, CompanyId = 1, UserId = 1,
            StartDate = date, EndDate = date,
            Status = RequestStatus.Approved
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.Should().ContainKey((1, date));
        result[(1, date)].HasVacation.Should().BeTrue();
    }

    [Fact]
    public async Task GetOverlaysAsync_DoesNotInclude_PendingTimeOff()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 1, CompanyId = 1, UserId = 1,
            StartDate = date, EndDate = date,
            Status = RequestStatus.Pending // Not approved
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        // Should not have an overlay for pending time-off
        result.ContainsKey((1, date)).Should().BeFalse();
    }

    [Fact]
    public async Task GetOverlaysAsync_ReturnsChoreOverlay_WhenActiveChoreExists()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        var choreType = new ChoreType
        {
            Id = 1, MoleculeId = 1, Name = "Guard", DisplayName = "Guard Duty",
            Color = "#FF0000", SortOrder = 1, CreatedByUserId = 1
        };
        _db.Add(choreType);
        await _db.SaveChangesAsync();

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = 1, MoleculeId = 1, UserId = 1,
            Date = date, Title = "Guard Duty", ChoreTypeId = 1,
            CreatedBy = 1, CanceledAt = null // Active
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.Should().ContainKey((1, date));
        result[(1, date)].HasChore.Should().BeTrue();
        result[(1, date)].ChoreItems.Should().HaveCount(1);
        result[(1, date)].ChoreItems[0].Name.Should().Be("Guard Duty");
        result[(1, date)].ChoreItems[0].Color.Should().Be("#FF0000");
    }

    [Fact]
    public async Task GetOverlaysAsync_DoesNotInclude_CanceledChore()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = 1, MoleculeId = 1, UserId = 1,
            Date = date, Title = "Canceled Chore",
            CreatedBy = 1, CanceledAt = DateTime.UtcNow // Canceled
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.ContainsKey((1, date)).Should().BeFalse();
    }

    [Fact]
    public async Task GetOverlaysAsync_ReturnsOnDutyOverlay_WithBuiltInType()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date,
            Type = OnDutyType.Hakam, CreatedBy = 1, CanceledAt = null
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.Should().ContainKey((1, date));
        result[(1, date)].HasOnDuty.Should().BeTrue();
        result[(1, date)].OnDutyItems.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOverlaysAsync_DoesNotInclude_CanceledOnDuty()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date,
            Type = OnDutyType.Hakam, CreatedBy = 1, CanceledAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.ContainsKey((1, date)).Should().BeFalse();
    }

    [Fact]
    public async Task GetOverlaysAsync_ReturnsOnDutyOverlay_WithCustomDutyTypeConfig()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        // Seed a custom duty type config
        _db.OnDutyTypeConfigs.Add(new OnDutyTypeConfig
        {
            Id = 1, TypeValue = 5, NameEn = "Katzin", NameHe = "קצין",
            Color = "#8b5cf6", IsActive = true, CreatedBy = 1
        });
        await _db.SaveChangesAsync();

        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date,
            Type = (OnDutyType)5, CreatedBy = 1, CanceledAt = null
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.Should().ContainKey((1, date));
        result[(1, date)].OnDutyItems.Should().HaveCount(1);
        result[(1, date)].OnDutyItems[0].Name.Should().Be("קצין");
        result[(1, date)].OnDutyItems[0].Color.Should().Be("#8b5cf6");
    }

    [Fact]
    public async Task GetOverlaysAsync_ReturnsShiftOverlay_WhenShiftAssignmentExists()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, CompanyId = 1, ShiftTypeId = 1, WorkDate = date, StaffingRequired = 2
        });
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 1, CompanyId = 1, ShiftInstanceId = 1, UserId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.Should().ContainKey((1, date));
        result[(1, date)].OtherShifts.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOverlaysAsync_ReturnsCompositeOverlay_WhenMultipleTypesOnSameDay()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        // Approved vacation
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 1, CompanyId = 1, UserId = 1,
            StartDate = date, EndDate = date,
            Status = RequestStatus.Approved
        });

        // Active chore
        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = 1, MoleculeId = 1, UserId = 1,
            Date = date, Title = "Cleaning", CreatedBy = 1, CanceledAt = null
        });

        // Active on-duty
        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 1, Date = date,
            Type = OnDutyType.Lead, CreatedBy = 1, CanceledAt = null
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.Should().ContainKey((1, date));
        var overlay = result[(1, date)];
        overlay.HasVacation.Should().BeTrue();
        overlay.HasChore.Should().BeTrue();
        overlay.HasOnDuty.Should().BeTrue();
    }

    [Fact]
    public async Task GetOverlaysAsync_ReturnsChoreWithTimedSuffix()
    {
        var data = await SetupTestDataAsync();
        var date = new DateOnly(2026, 3, 3);

        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = 1, MoleculeId = 1, UserId = 1,
            Date = date, Title = "Guard",
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(14, 0),
            CreatedBy = 1, CanceledAt = null
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOverlaysAsync(
            moleculeId: 1,
            start: new DateOnly(2026, 3, 1),
            end: new DateOnly(2026, 3, 7));

        result.Should().ContainKey((1, date));
        // Chore name should include time suffix
        result[(1, date)].ChoreItems[0].Name.Should().Contain("10:00-14:00");
    }
}
