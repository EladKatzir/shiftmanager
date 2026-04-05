using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.Shifts;

/// <summary>
/// Tests for ValidateShiftAssignmentAsync — covers error/warning generation
/// for job type mismatch, shift grouping, weekly cap, rest hours, overlap, and exemptions.
/// </summary>
[Collection("MasterTests")]
public class ShiftValidationTests : MasterTestBase
{
    public ShiftValidationTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // HELPERS
    // ================================================================

    private async Task<ShiftType> CreateShiftTypeAsync(
        string key = ShiftType.KEY_MORNING,
        TimeOnly? start = null,
        TimeOnly? end = null,
        int? jobTypeId = null,
        int? shiftGroupingId = null,
        int? moleculeId = null,
        bool isOffline = false,
        bool isHome = false)
    {
        var mol = moleculeId ?? Fixture.MoleculeByName["Oren"].Id;
        var actualKey = isOffline ? ShiftType.KEY_OFFLINE
                      : isHome ? ShiftType.KEY_HOME
                      : key;

        var shiftType = new ShiftType
        {
            Key = actualKey,
            Scope = ShiftScope.Molecule,
            MoleculeId = mol,
            JobTypeId = jobTypeId,
            ShiftGroupingId = shiftGroupingId,
            Start = start ?? new TimeOnly(7, 0),
            End = end ?? new TimeOnly(15, 0)
        };
        Db.ShiftTypes.Add(shiftType);
        await Db.SaveChangesAsync();
        return TrackEntity(shiftType);
    }

    private async Task<ShiftInstance> CreateShiftInstanceAsync(
        ShiftType shiftType,
        DateOnly? workDate = null,
        int staffingRequired = 5)
    {
        var companyId = Fixture.CompanyByName["Tzafona"].Id;
        var instance = new ShiftInstance
        {
            CompanyId = companyId,
            ShiftTypeId = shiftType.Id,
            WorkDate = workDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Name = shiftType.Name,
            StaffingRequired = staffingRequired
        };
        Db.ShiftInstances.Add(instance);
        await Db.SaveChangesAsync();
        return TrackEntity(instance);
    }

    private async Task<ShiftAssignment> CreateDirectAssignmentAsync(
        int userId, int shiftInstanceId, int companyId)
    {
        var assignment = new ShiftAssignment
        {
            CompanyId = companyId,
            ShiftInstanceId = shiftInstanceId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        Db.ShiftAssignments.Add(assignment);
        await Db.SaveChangesAsync();
        return TrackEntity(assignment);
    }

    // ================================================================
    // TESTS
    // ================================================================

    [Fact]
    public async Task Validate_ValidAssignment_IsClean()
    {
        // Arrange — no JobType filter on shift → no mismatch; no grouping → no grouping warning
        // Use Lead user (Alhut) with a shift that has no filters → clean validation
        var shiftType = await CreateShiftTypeAsync(jobTypeId: null, shiftGroupingId: null);
        var instance = await CreateShiftInstanceAsync(shiftType);
        var user = GetTestUser("Tzafona", "Lead"); // Alhut lead in Oren molecule

        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, instance.Id);

        // Assert
        validation.CanAssign.Should().BeTrue();
        validation.Errors.Should().BeEmpty();
        validation.Warnings.Should().BeEmpty();
        validation.IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_UserNotFound_HasError()
    {
        // Arrange
        var shiftType = await CreateShiftTypeAsync();
        var instance = await CreateShiftInstanceAsync(shiftType);
        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(99999, instance.Id);

        // Assert
        validation.CanAssign.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Key == "USER_NOT_FOUND");
    }

    [Fact]
    public async Task Validate_ShiftNotFound_HasError()
    {
        // Arrange
        var user = GetTestUser("Tzafona", "Lead");
        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, 99999);

        // Assert
        validation.CanAssign.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Key == "SHIFT_NOT_FOUND");
    }

    [Fact]
    public async Task Validate_JobTypeMismatch_HasWarning()
    {
        // Arrange — BR shift type but user has Alhut job type
        var brJobTypeId = Fixture.JobTypeByName["BR"].Id;
        var shiftType = await CreateShiftTypeAsync(jobTypeId: brJobTypeId);
        var instance = await CreateShiftInstanceAsync(shiftType);
        var user = GetTestUser("Tzafona", "Lead"); // Alhut lead

        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, instance.Id);

        // Assert
        validation.Warnings.Should().Contain(w => w.Key == "JOB_TYPE_MISMATCH");
    }

    [Fact]
    public async Task Validate_NotInShiftGrouping_HasWarning()
    {
        // Arrange — Darom grouping (Camps + Hir), but user is in Tzafona
        var alhutJobTypeId = Fixture.JobTypeByName["Alhut"].Id;
        var daromGroupingId = Fixture.ShiftGroupingByName["Darom"].Id;
        var shiftType = await CreateShiftTypeAsync(
            jobTypeId: alhutJobTypeId,
            shiftGroupingId: daromGroupingId);
        var instance = await CreateShiftInstanceAsync(shiftType);
        var user = GetTestUser("Tzafona", "Lead"); // Tzafona is NOT in Darom grouping

        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, instance.Id);

        // Assert
        validation.Warnings.Should().Contain(w => w.Key == "NOT_IN_SHIFT_GROUPING");
    }

    [Fact]
    public async Task Validate_WeeklyCapExceeded_HasWarning()
    {
        // Arrange — create 6 x 8-hour shifts in the same week = 48 hours,
        // then validate a 7th. WeeklyCap is mocked at 48.
        var user = GetTestUser("Tzafona", "Lead");
        var companyId = Fixture.CompanyByName["Tzafona"].Id;

        // Find a future Sunday (default WeekStart) at least 21 days out
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21));
        while (futureDate.DayOfWeek != DayOfWeek.Sunday)
            futureDate = futureDate.AddDays(1);

        // Create 6 shifts Sun-Fri, each 8 hours (07:00-15:00), no JobType filter
        var existingShiftType = await CreateShiftTypeAsync(
            key: ShiftType.KEY_MORNING,
            start: new TimeOnly(7, 0),
            end: new TimeOnly(15, 0),
            jobTypeId: null);

        for (int i = 0; i < 6; i++)
        {
            var dayInstance = await CreateShiftInstanceAsync(
                existingShiftType,
                workDate: futureDate.AddDays(i));
            await CreateDirectAssignmentAsync(user.Id, dayInstance.Id, companyId);
        }

        // 7th shift on Saturday of same week
        var seventhShiftType = await CreateShiftTypeAsync(
            key: ShiftType.KEY_AFTERNOON,
            start: new TimeOnly(7, 0),
            end: new TimeOnly(15, 0),
            jobTypeId: null);
        var seventhInstance = await CreateShiftInstanceAsync(
            seventhShiftType,
            workDate: futureDate.AddDays(6));

        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, seventhInstance.Id);

        // Assert — should have weekly cap warning
        validation.Warnings.Should().Contain(w => w.Key == "EXCEEDS_WEEKLY_CAP",
            $"6 existing 8h shifts (48h) + 7th (8h) = 56h exceeds cap of 48h");
    }

    [Fact]
    public async Task Validate_RestHoursViolation_HasError()
    {
        // Arrange — afternoon shift 15:00-23:00, then morning shift 07:00-15:00 next day
        // Gap = 8 hours < 11 hours rest requirement
        var user = GetTestUser("Tzafona", "Lead");
        var companyId = Fixture.CompanyByName["Tzafona"].Id;

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21));

        // First shift: 15:00-23:00 on day 1
        var afternoonType = await CreateShiftTypeAsync(
            key: ShiftType.KEY_AFTERNOON,
            start: new TimeOnly(15, 0),
            end: new TimeOnly(23, 0),
            jobTypeId: null);
        var afternoonInstance = await CreateShiftInstanceAsync(
            afternoonType, workDate: futureDate);
        await CreateDirectAssignmentAsync(user.Id, afternoonInstance.Id, companyId);

        // Second shift: 07:00-15:00 on day 2 → gap = 8 hours < 11 hours
        var morningType = await CreateShiftTypeAsync(
            key: ShiftType.KEY_MORNING,
            start: new TimeOnly(7, 0),
            end: new TimeOnly(15, 0),
            jobTypeId: null);
        var morningInstance = await CreateShiftInstanceAsync(
            morningType, workDate: futureDate.AddDays(1));

        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, morningInstance.Id);

        // Assert
        validation.CanAssign.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Key == "REST_HOURS_VIOLATION");
    }

    [Fact]
    public async Task Validate_OverlapDetected_HasError()
    {
        // Arrange — two overlapping shifts on the same day
        var user = GetTestUser("Tzafona", "Lead");
        var companyId = Fixture.CompanyByName["Tzafona"].Id;

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21));

        // First shift: 07:00-15:00
        var morningType = await CreateShiftTypeAsync(
            key: ShiftType.KEY_MORNING,
            start: new TimeOnly(7, 0),
            end: new TimeOnly(15, 0),
            jobTypeId: null);
        var morningInstance = await CreateShiftInstanceAsync(
            morningType, workDate: futureDate);
        await CreateDirectAssignmentAsync(user.Id, morningInstance.Id, companyId);

        // Second shift: 10:00-18:00 (overlaps with first)
        var midType = await CreateShiftTypeAsync(
            key: ShiftType.KEY_MIDDLE,
            start: new TimeOnly(10, 0),
            end: new TimeOnly(18, 0),
            jobTypeId: null);
        var midInstance = await CreateShiftInstanceAsync(
            midType, workDate: futureDate);

        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, midInstance.Id);

        // Assert
        validation.CanAssign.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Key == "OVERLAP");
    }

    [Fact]
    public async Task Validate_ExemptShift_SkipsOverlapRestAndCap()
    {
        // Arrange — user has an existing shift, then we validate an OFFLINE shift
        // on the same day/time. OFFLINE is exempt from overlap, rest, and weekly cap.
        var user = GetTestUser("Tzafona", "Lead");
        var companyId = Fixture.CompanyByName["Tzafona"].Id;

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21));

        // Existing regular shift 07:00-15:00
        var morningType = await CreateShiftTypeAsync(
            key: ShiftType.KEY_MORNING,
            start: new TimeOnly(7, 0),
            end: new TimeOnly(15, 0),
            jobTypeId: null);
        var morningInstance = await CreateShiftInstanceAsync(
            morningType, workDate: futureDate);
        await CreateDirectAssignmentAsync(user.Id, morningInstance.Id, companyId);

        // Validate an OFFLINE shift at the same time
        var offlineType = await CreateShiftTypeAsync(
            isOffline: true,
            start: new TimeOnly(7, 0),
            end: new TimeOnly(15, 0));
        var offlineInstance = await CreateShiftInstanceAsync(
            offlineType, workDate: futureDate);

        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, offlineInstance.Id);

        // Assert — exempt shift: no overlap, no rest violation, no cap warnings
        validation.Errors.Should().NotContain(e => e.Key == "OVERLAP");
        validation.Errors.Should().NotContain(e => e.Key == "REST_HOURS_VIOLATION");
        validation.Warnings.Should().NotContain(w => w.Key == "EXCEEDS_WEEKLY_CAP");
    }

    [Fact]
    public async Task Validate_CleanAssignment_CanAssignTrue()
    {
        // Arrange — clean scenario: no job type filter, future date, no conflicts
        var shiftType = await CreateShiftTypeAsync(jobTypeId: null);
        var instance = await CreateShiftInstanceAsync(shiftType,
            workDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        var user = GetTestUser("Tzafona", "Lead");

        var service = CreateShiftAssignmentService();

        // Act
        var validation = await service.ValidateShiftAssignmentAsync(user.Id, instance.Id);

        // Assert
        validation.CanAssign.Should().BeTrue();
        validation.Errors.Should().BeEmpty();
    }
}
