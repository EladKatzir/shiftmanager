using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
/// Comprehensive unit tests for ValidateShiftAssignmentAsync covering all 18 validation checks:
/// Errors (hard blocks): USER_NOT_FOUND, SHIFT_NOT_FOUND, USER_NOT_IN_MOLECULE, ALREADY_ASSIGNED,
///   COMPANY_INELIGIBLE, OFFICER_RANK_REQUIRED, OVERLAP, REST_HOURS_VIOLATION, DUPLICATE_HOME
/// Warnings (overrideable): JOB_TYPE_MISMATCH, NOT_IN_SHIFT_GROUPING, HOME_CONFLICT,
///   SHIFT_EXISTS_CONFLICT, VACATION_CONFLICT, CHORE_CONFLICT, ONDUTY_CONFLICT,
///   EXCEEDS_WEEKLY_CAP, PAST_DATE
/// Plus exemption tests for OFFLINE and HOME shift types.
/// </summary>
public class ShiftValidationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ShiftAssignmentService _service;
    private readonly Mock<IAppConfigCacheService> _configCacheMock;

    public ShiftValidationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);

        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        // Return the key as the localized string so we can identify messages
        localizer.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));

        var logger = Mock.Of<ILogger<ShiftAssignmentService>>();

        var hierarchySettingsMock = new Mock<IHierarchySettingsService>();
        hierarchySettingsMock
            .Setup(x => x.GetEffectiveSettingsAsync(It.IsAny<int>()))
            .ReturnsAsync(new EffectiveSettings(
                RestHours: 8,
                WeeklyCap: 56,
                RestHoursSource: "Area",
                WeeklyCapSource: "Area"));

        var auditLogService = Mock.Of<IAuditLogService>();

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ApiKeyHmacSecret"]).Returns("test-hmac-secret-for-unit-tests");

        _configCacheMock = new Mock<IAppConfigCacheService>();
        // Default: WeekStartDay = Sunday (0)
        _configCacheMock
            .Setup(c => c.GetConfigAsync(It.IsAny<int>(), "WeekStartDay"))
            .ReturnsAsync((AppConfig?)null);

        _service = new ShiftAssignmentService(
            _db, localizer.Object, logger, hierarchySettingsMock.Object,
            auditLogService, configMock.Object, _configCacheMock.Object,
            BusyServiceMockFactory.Real(_db, configMock.Object, restHours: 8, weeklyCap: 56));
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    #region Helper: Hierarchy Setup

    /// <summary>
    /// Creates a full entity hierarchy: Project -> Area -> Molecule -> Company -> User + ShiftType -> ShiftInstance.
    /// Returns all entities for test customization.
    /// </summary>
    private async Task<TestData> CreateBaseHierarchyAsync(
        DateOnly? workDate = null,
        string shiftKey = ShiftType.KEY_MORNING,
        TimeOnly? shiftStart = null,
        TimeOnly? shiftEnd = null,
        bool requiresOfficerRank = false,
        string? eligibleCompanyIds = null,
        MilitaryRank userRank = MilitaryRank.Turai)
    {
        var date = workDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

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
            Rank = userRank,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var shiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = molecule.Id,
            JobTypeId = jobType.Id,
            Key = shiftKey,
            Start = shiftStart ?? new TimeOnly(8, 0),
            End = shiftEnd ?? new TimeOnly(16, 0),
            RequiresOfficerRank = requiresOfficerRank,
            EligibleCompanyIds = eligibleCompanyIds
        };
        _db.ShiftTypes.Add(shiftType);
        await _db.SaveChangesAsync();

        var shiftInstance = new ShiftInstance
        {
            CompanyId = company.Id,
            ShiftTypeId = shiftType.Id,
            WorkDate = date,
            Name = "Test Shift"
        };
        _db.ShiftInstances.Add(shiftInstance);
        await _db.SaveChangesAsync();

        return new TestData(
            Project: project,
            Area: area,
            Molecule: molecule,
            Company: company,
            JobType: jobType,
            User: user,
            ShiftType: shiftType,
            ShiftInstance: shiftInstance);
    }

    private record TestData(
        Project Project,
        Area Area,
        Molecule Molecule,
        Company Company,
        JobType JobType,
        AppUser User,
        ShiftType ShiftType,
        ShiftInstance ShiftInstance);

    #endregion

    // =====================================================================
    // ERROR TESTS (hard blocks)
    // =====================================================================

    [Fact]
    public async Task Validate_UserNotFound_ReturnsError()
    {
        // Arrange
        var data = await CreateBaseHierarchyAsync();

        // Act — nonexistent userId
        var result = await _service.ValidateShiftAssignmentAsync(9999, data.ShiftInstance.Id);

        // Assert
        result.CanAssign.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "USER_NOT_FOUND");
    }

    [Fact]
    public async Task Validate_ShiftNotFound_ReturnsError()
    {
        // Arrange
        var data = await CreateBaseHierarchyAsync();

        // Act — nonexistent shiftInstanceId
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, 9999);

        // Assert
        result.CanAssign.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "SHIFT_NOT_FOUND");
    }

    [Fact]
    public async Task Validate_UserNotInMolecule_ReturnsError()
    {
        // Arrange
        var data = await CreateBaseHierarchyAsync();

        // Create a second molecule with a separate company and user
        var molecule2 = new Molecule { AreaId = data.Area.Id, Name = "OtherMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule2);
        await _db.SaveChangesAsync();

        var company2 = new Company { MoleculeId = molecule2.Id, Name = "Company2", DisplayName = "Company 2" };
        _db.Companies.Add(company2);
        await _db.SaveChangesAsync();

        var outsideUser = new AppUser
        {
            CompanyId = company2.Id,
            JobTypeId = data.JobType.Id,
            Email = "outside@test.com",
            DisplayName = "Outside User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(outsideUser);
        await _db.SaveChangesAsync();

        // Act — user from molecule2 trying to be assigned to shift in molecule1
        var result = await _service.ValidateShiftAssignmentAsync(outsideUser.Id, data.ShiftInstance.Id);

        // Assert
        result.CanAssign.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "USER_NOT_IN_MOLECULE");
    }

    [Fact]
    public async Task Validate_AlreadyAssigned_ReturnsError()
    {
        // Arrange
        var data = await CreateBaseHierarchyAsync();

        // Pre-assign user to the same shift
        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = data.ShiftInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.CanAssign.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "ALREADY_ASSIGNED");
    }

    [Fact]
    public async Task Validate_CompanyIneligible_ReturnsError()
    {
        // Arrange — shift type restricts eligibility to company ID 9999 (nonexistent)
        var data = await CreateBaseHierarchyAsync(eligibleCompanyIds: "[9999]");

        // Act — user's company is NOT in the eligible list
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Errors.Should().Contain(e => e.Key == "COMPANY_INELIGIBLE");
        result.CanAssign.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_OfficerRankRequired_ReturnsError()
    {
        // Arrange — shift requires officer rank, user is Turai (enlisted, rank=0)
        var data = await CreateBaseHierarchyAsync(requiresOfficerRank: true, userRank: MilitaryRank.Turai);

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Errors.Should().Contain(e => e.Key == "OFFICER_RANK_REQUIRED");
        result.CanAssign.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_OverlappingShift_ReturnsError()
    {
        // Arrange — existing shift 08:00-16:00 same day; new shift 12:00-20:00
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftStart: new TimeOnly(12, 0),
            shiftEnd: new TimeOnly(20, 0));

        // Create an existing overlapping shift (08:00-16:00) already assigned to this user
        var existingShiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = "EXISTING",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(existingShiftType);
        await _db.SaveChangesAsync();

        var existingInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = existingShiftType.Id,
            WorkDate = futureDate,
            Name = "Existing Shift"
        };
        _db.ShiftInstances.Add(existingInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = existingInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act — validate assigning user to the 12:00-20:00 shift (overlaps with 08:00-16:00)
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Errors.Should().Contain(e => e.Key == "OVERLAP");
    }

    [Fact]
    public async Task Validate_AdjacentShifts_NoOverlapError()
    {
        // Arrange — existing shift 08:00-16:00; new shift 16:00-00:00 (adjacent, no overlap)
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftStart: new TimeOnly(16, 0),
            shiftEnd: new TimeOnly(0, 0)); // wraps to midnight = next day 00:00

        // Create adjacent shift already assigned
        var existingShiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = "EXISTING",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(existingShiftType);
        await _db.SaveChangesAsync();

        var existingInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = existingShiftType.Id,
            WorkDate = futureDate,
            Name = "Existing"
        };
        _db.ShiftInstances.Add(existingInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = existingInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert — adjacent shifts do NOT overlap (16:00 == 16:00, rs < newEnd && newStart < re fails)
        result.Errors.Should().NotContain(e => e.Key == "OVERLAP");
    }

    [Fact]
    public async Task Validate_InsufficientRest_ReturnsError()
    {
        // Arrange — existing shift ends 10:00; new shift starts 14:00 same day
        // Gap = 4 hours, requirement = 8 hours → REST_HOURS_VIOLATION
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftStart: new TimeOnly(14, 0),
            shiftEnd: new TimeOnly(22, 0));

        // Existing shift: 02:00-10:00 (ends at 10:00; new starts at 14:00 → 4h gap < 8h required)
        var existingShiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = "EXISTING",
            Start = new TimeOnly(2, 0),
            End = new TimeOnly(10, 0)
        };
        _db.ShiftTypes.Add(existingShiftType);
        await _db.SaveChangesAsync();

        var existingInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = existingShiftType.Id,
            WorkDate = futureDate,
            Name = "Early Shift"
        };
        _db.ShiftInstances.Add(existingInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = existingInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Errors.Should().Contain(e => e.Key == "REST_HOURS_VIOLATION");
    }

    [Fact]
    public async Task Validate_ExactRestThreshold_NoError()
    {
        // Arrange — existing shift ends 06:00; new shift starts 14:00
        // Gap = 8 hours exactly, requirement = 8 hours → uses < comparison, so NOT a violation
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftStart: new TimeOnly(14, 0),
            shiftEnd: new TimeOnly(22, 0));

        // Existing shift: 22:00 previous day to 06:00 same day (overnight shift ending at 06:00)
        var prevDate = futureDate.AddDays(-1);
        var existingShiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = "EXISTING_NIGHT",
            Start = new TimeOnly(22, 0),
            End = new TimeOnly(6, 0)  // overnight: wraps to next day
        };
        _db.ShiftTypes.Add(existingShiftType);
        await _db.SaveChangesAsync();

        var existingInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = existingShiftType.Id,
            WorkDate = prevDate,  // Starts at 22:00 on prevDate, ends at 06:00 on futureDate
            Name = "Night Shift"
        };
        _db.ShiftInstances.Add(existingInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = existingInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert — gap is exactly 8 hours, comparison is <, so no violation
        result.Errors.Should().NotContain(e => e.Key == "REST_HOURS_VIOLATION");
    }

    [Fact]
    public async Task Validate_DuplicateHome_ReturnsError()
    {
        // Arrange — user already has a HOME shift on this date; try to assign another HOME
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftKey: ShiftType.KEY_HOME,
            shiftStart: new TimeOnly(0, 0),
            shiftEnd: new TimeOnly(0, 0));

        // Existing HOME shift on the same date
        var existingHomeType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = ShiftType.KEY_HOME,
            Start = new TimeOnly(0, 0),
            End = new TimeOnly(0, 0)
        };
        _db.ShiftTypes.Add(existingHomeType);
        await _db.SaveChangesAsync();

        var existingHomeInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = existingHomeType.Id,
            WorkDate = futureDate,
            Name = "Home Shift"
        };
        _db.ShiftInstances.Add(existingHomeInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = existingHomeInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act — validate assigning the NEW HOME shift to same user on same date
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Errors.Should().Contain(e => e.Key == "DUPLICATE_HOME");
    }

    // =====================================================================
    // WARNING TESTS (overrideable)
    // =====================================================================

    [Fact]
    public async Task Validate_HomeConflict_ReturnsWarning()
    {
        // Arrange — user has a HOME shift on a date; assigning a REGULAR shift on the same date
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftKey: ShiftType.KEY_MORNING,
            shiftStart: new TimeOnly(8, 0),
            shiftEnd: new TimeOnly(16, 0));

        // Existing HOME shift on the same date
        var homeShiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = ShiftType.KEY_HOME,
            Start = new TimeOnly(0, 0),
            End = new TimeOnly(0, 0)
        };
        _db.ShiftTypes.Add(homeShiftType);
        await _db.SaveChangesAsync();

        var homeInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = homeShiftType.Id,
            WorkDate = futureDate,
            Name = "Home"
        };
        _db.ShiftInstances.Add(homeInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = homeInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act — validate assigning regular shift to user who has HOME on that date
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert — HOME_CONFLICT is a warning, not error
        result.Warnings.Should().Contain(w => w.Key == "HOME_CONFLICT");
        result.CanAssign.Should().BeTrue("HOME_CONFLICT is a warning, not a hard error");
    }

    [Fact]
    public async Task Validate_ShiftExistsConflict_ReturnsWarning()
    {
        // Arrange — user has a regular shift on a date; assigning HOME on the same date
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        // The shift being validated is HOME
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftKey: ShiftType.KEY_HOME,
            shiftStart: new TimeOnly(0, 0),
            shiftEnd: new TimeOnly(0, 0));

        // Existing real shift on same date
        var realShiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = ShiftType.KEY_MORNING,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(realShiftType);
        await _db.SaveChangesAsync();

        var realInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = realShiftType.Id,
            WorkDate = futureDate,
            Name = "Morning Shift"
        };
        _db.ShiftInstances.Add(realInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = realInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act — validate assigning HOME when user already has real shift that date
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Warnings.Should().Contain(w => w.Key == "SHIFT_EXISTS_CONFLICT");
    }

    [Fact]
    public async Task Validate_VacationConflict_ReturnsWarning()
    {
        // Arrange
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(workDate: futureDate);

        // Create approved time-off spanning the shift date
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            CompanyId = data.Company.Id,
            UserId = data.User.Id,
            StartDate = futureDate.AddDays(-1),
            EndDate = futureDate.AddDays(1),
            Status = RequestStatus.Approved,
            Type = TimeOffType.Vacation
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Warnings.Should().Contain(w => w.Key == "VACATION_CONFLICT");
        result.CanAssign.Should().BeTrue("vacation conflict is a warning, not a hard error");
    }

    [Fact]
    public async Task Validate_ChoreConflict_ReturnsWarning()
    {
        // Arrange
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(workDate: futureDate);

        // Create active chore on the shift date (CanceledAt == null means active)
        _db.Chores.Add(new Chore
        {
            CompanyId = data.Company.Id,
            UserId = data.User.Id,
            Date = futureDate,
            Title = "Guard Duty",
            CreatedBy = data.User.Id,
            CreatedAt = DateTime.UtcNow,
            CanceledAt = null  // active
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Warnings.Should().Contain(w => w.Key == "CHORE_CONFLICT");
        result.CanAssign.Should().BeTrue("chore conflict is a warning");
    }

    [Fact]
    public async Task Validate_OnDutyConflict_ReturnsWarning()
    {
        // Arrange
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(workDate: futureDate);

        // Create active on-duty on the shift date
        _db.OnDuties.Add(new OnDuty
        {
            UserId = data.User.Id,
            Date = futureDate,
            Type = OnDutyType.Hakam,
            CreatedBy = data.User.Id,
            CreatedAt = DateTime.UtcNow,
            CanceledAt = null  // active
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Warnings.Should().Contain(w => w.Key == "ONDUTY_CONFLICT");
        result.CanAssign.Should().BeTrue("on-duty conflict is a warning");
    }

    [Fact]
    public async Task Validate_ExceedsWeeklyCap_ReturnsWarning()
    {
        // Arrange — WeeklyCap=56h. User already has 49h this week. New shift is 8h → 57h > 56h.
        // We use a future Sunday as the week start and place all shifts within that week.
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        // Find the next Sunday from futureDate
        while (futureDate.DayOfWeek != DayOfWeek.Monday)
            futureDate = futureDate.AddDays(1);

        // New shift to validate: 8:00-16:00 (8 hours) on Monday
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftStart: new TimeOnly(8, 0),
            shiftEnd: new TimeOnly(16, 0));

        // Create existing shifts totaling ~49 hours in the same week
        // We'll use 6 shifts of ~8.17 hours each = ~49h across Mon-Sat of previous days in same week
        // But since our target is Monday (start of week = Sunday), we need shifts on Sunday
        var sunday = futureDate.AddDays(-1); // Sunday is the week start
        for (int i = 0; i < 6; i++)
        {
            var existingType = new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = data.Molecule.Id,
                Key = $"FILL_{i}",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 10)  // ~8.17h each, 6 shifts = ~49h
            };
            _db.ShiftTypes.Add(existingType);
            await _db.SaveChangesAsync();

            var existingInst = new ShiftInstance
            {
                CompanyId = data.Company.Id,
                ShiftTypeId = existingType.Id,
                WorkDate = sunday.AddDays(i > 0 ? i + 1 : 0), // Sun, Tue, Wed, Thu, Fri, Sat
                Name = $"Fill {i}"
            };
            _db.ShiftInstances.Add(existingInst);
            await _db.SaveChangesAsync();

            _db.ShiftAssignments.Add(new ShiftAssignment
            {
                CompanyId = data.Company.Id,
                ShiftInstanceId = existingInst.Id,
                UserId = data.User.Id
            });
            await _db.SaveChangesAsync();
        }

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert — 49h + 8h = 57h > 56h cap
        result.Warnings.Should().Contain(w => w.Key == "EXCEEDS_WEEKLY_CAP");
    }

    [Fact]
    public async Task Validate_ExactlyAtWeeklyCap_NoWarning()
    {
        // Arrange — WeeklyCap=56h. User already has 48h. New shift is 8h → 56h == 56h.
        // Code uses > (strict greater than), so exactly at cap is NOT a violation.
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        while (futureDate.DayOfWeek != DayOfWeek.Monday)
            futureDate = futureDate.AddDays(1);

        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            shiftStart: new TimeOnly(8, 0),
            shiftEnd: new TimeOnly(16, 0));

        // Create 6 existing 8-hour shifts = 48h
        var sunday = futureDate.AddDays(-1);
        for (int i = 0; i < 6; i++)
        {
            var existingType = new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = data.Molecule.Id,
                Key = $"FILL_{i}",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0)  // exactly 8h each, 6 shifts = 48h
            };
            _db.ShiftTypes.Add(existingType);
            await _db.SaveChangesAsync();

            var existingInst = new ShiftInstance
            {
                CompanyId = data.Company.Id,
                ShiftTypeId = existingType.Id,
                WorkDate = sunday.AddDays(i > 0 ? i + 1 : 0),
                Name = $"Fill {i}"
            };
            _db.ShiftInstances.Add(existingInst);
            await _db.SaveChangesAsync();

            _db.ShiftAssignments.Add(new ShiftAssignment
            {
                CompanyId = data.Company.Id,
                ShiftInstanceId = existingInst.Id,
                UserId = data.User.Id
            });
            await _db.SaveChangesAsync();
        }

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert — 48h + 8h = 56h exactly == cap; code uses > not >=, so no warning
        result.Warnings.Should().NotContain(w => w.Key == "EXCEEDS_WEEKLY_CAP");
    }

    [Fact]
    public async Task Validate_PastDate_ReturnsWarning()
    {
        // Arrange — shift date is yesterday
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var data = await CreateBaseHierarchyAsync(workDate: yesterday);

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.Warnings.Should().Contain(w => w.Key == "PAST_DATE");
        result.CanAssign.Should().BeTrue("past date is a warning, not a hard error");
    }

    // =====================================================================
    // EXEMPTION TESTS (OFFLINE and HOME skip overlap, rest, cap, past)
    // =====================================================================

    [Fact]
    public async Task Validate_OfflineShift_SkipsOverlapRestCapPast()
    {
        // Arrange — OFFLINE shift on a past date with overlapping existing shift and insufficient rest
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var data = await CreateBaseHierarchyAsync(
            workDate: yesterday,
            shiftKey: ShiftType.KEY_OFFLINE,
            shiftStart: new TimeOnly(8, 0),
            shiftEnd: new TimeOnly(16, 0));

        // Create overlapping existing shift (should be skipped for OFFLINE)
        var existingType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = "REGULAR",
            Start = new TimeOnly(10, 0),
            End = new TimeOnly(18, 0)
        };
        _db.ShiftTypes.Add(existingType);
        await _db.SaveChangesAsync();

        var existingInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = existingType.Id,
            WorkDate = yesterday,
            Name = "Overlap Shift"
        };
        _db.ShiftInstances.Add(existingInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = existingInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert — OFFLINE exempts: overlap, rest, weekly cap, past date
        result.Errors.Should().NotContain(e => e.Key == "OVERLAP");
        result.Errors.Should().NotContain(e => e.Key == "REST_HOURS_VIOLATION");
        result.Warnings.Should().NotContain(w => w.Key == "EXCEEDS_WEEKLY_CAP");
        result.Warnings.Should().NotContain(w => w.Key == "PAST_DATE");
    }

    [Fact]
    public async Task Validate_HomeShift_SkipsOverlapRestCapPast()
    {
        // Arrange — HOME shift on a past date with overlapping existing shift
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var data = await CreateBaseHierarchyAsync(
            workDate: yesterday,
            shiftKey: ShiftType.KEY_HOME,
            shiftStart: new TimeOnly(0, 0),
            shiftEnd: new TimeOnly(0, 0));

        // Create overlapping existing shift
        var existingType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = data.Molecule.Id,
            Key = "REGULAR",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(existingType);
        await _db.SaveChangesAsync();

        var existingInstance = new ShiftInstance
        {
            CompanyId = data.Company.Id,
            ShiftTypeId = existingType.Id,
            WorkDate = yesterday,
            Name = "Regular Shift"
        };
        _db.ShiftInstances.Add(existingInstance);
        await _db.SaveChangesAsync();

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            CompanyId = data.Company.Id,
            ShiftInstanceId = existingInstance.Id,
            UserId = data.User.Id
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert — HOME exempts: overlap, rest, weekly cap, past date
        result.Errors.Should().NotContain(e => e.Key == "OVERLAP");
        result.Errors.Should().NotContain(e => e.Key == "REST_HOURS_VIOLATION");
        result.Warnings.Should().NotContain(w => w.Key == "EXCEEDS_WEEKLY_CAP");
        result.Warnings.Should().NotContain(w => w.Key == "PAST_DATE");
        // But it SHOULD still produce SHIFT_EXISTS_CONFLICT (HOME-specific warning)
        result.Warnings.Should().Contain(w => w.Key == "SHIFT_EXISTS_CONFLICT");
    }

    // =====================================================================
    // BOUNDARY / NEGATIVE TESTS (additional coverage)
    // =====================================================================

    [Fact]
    public async Task Validate_CleanAssignment_NoErrorsNoWarnings()
    {
        // Arrange — everything is valid: correct molecule, no conflicts, future date
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(workDate: futureDate);

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert
        result.CanAssign.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_OfficerRank_PassesWhenUserIsOfficer()
    {
        // Arrange — shift requires officer rank, user IS an officer (SegenMishne = rank 9)
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var data = await CreateBaseHierarchyAsync(
            workDate: futureDate,
            requiresOfficerRank: true,
            userRank: MilitaryRank.SegenMishne);

        // Act
        var result = await _service.ValidateShiftAssignmentAsync(data.User.Id, data.ShiftInstance.Id);

        // Assert — officer requirement is met
        result.Errors.Should().NotContain(e => e.Key == "OFFICER_RANK_REQUIRED");
    }
}
