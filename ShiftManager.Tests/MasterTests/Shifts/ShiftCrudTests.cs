using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.Shifts;

/// <summary>
/// Full lifecycle tests for shift creation, assignment, and unassignment.
/// Validates ShiftType/ShiftInstance CRUD and ShiftAssignmentService operations.
/// </summary>
[Collection("MasterTests")]
public class ShiftCrudTests : MasterTestBase
{
    public ShiftCrudTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // HELPERS
    // ================================================================

    private async Task<ShiftType> CreateShiftTypeAsync(
        string key = ShiftType.KEY_MORNING,
        TimeOnly? start = null,
        TimeOnly? end = null,
        int? jobTypeId = null,
        int? shiftGroupingId = null,
        bool isOffline = false,
        bool isHome = false)
    {
        var moleculeId = Fixture.MoleculeByName["Oren"].Id;
        var actualKey = isOffline ? ShiftType.KEY_OFFLINE
                      : isHome ? ShiftType.KEY_HOME
                      : key;

        var shiftType = new ShiftType
        {
            Key = actualKey,
            Scope = ShiftScope.Molecule,
            MoleculeId = moleculeId,
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
            WorkDate = workDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Name = shiftType.Name,
            StaffingRequired = staffingRequired
        };
        Db.ShiftInstances.Add(instance);
        await Db.SaveChangesAsync();
        return TrackEntity(instance);
    }

    // ================================================================
    // TESTS
    // ================================================================

    [Fact]
    public async Task CreateShiftType_AndInstance_Succeeds()
    {
        // Arrange & Act
        var shiftType = await CreateShiftTypeAsync(
            key: ShiftType.KEY_MORNING,
            start: new TimeOnly(7, 0),
            end: new TimeOnly(15, 0));

        var instance = await CreateShiftInstanceAsync(shiftType);

        // Assert — entities exist in DB
        var dbShiftType = await Db.ShiftTypes.FindAsync(shiftType.Id);
        dbShiftType.Should().NotBeNull();
        dbShiftType!.Key.Should().Be(ShiftType.KEY_MORNING);
        dbShiftType.MoleculeId.Should().Be(Fixture.MoleculeByName["Oren"].Id);
        dbShiftType.Scope.Should().Be(ShiftScope.Molecule);

        var dbInstance = await Db.ShiftInstances.FindAsync(instance.Id);
        dbInstance.Should().NotBeNull();
        dbInstance!.ShiftTypeId.Should().Be(shiftType.Id);
        dbInstance.StaffingRequired.Should().Be(5);
    }

    [Fact]
    public async Task AssignShift_ValidUser_Succeeds()
    {
        // Arrange — no JobType filter on shift → no JOB_TYPE_MISMATCH warning → clean assignment
        var shiftType = await CreateShiftTypeAsync(jobTypeId: null);
        var instance = await CreateShiftInstanceAsync(shiftType);
        var user = GetTestUser("Tzafona", "Lead"); // Alhut lead in Oren
        var assigner = GetTestUser("Tzafona", "Director");

        var service = CreateShiftAssignmentService();

        // Act
        var result = await service.AssignShiftAsync(user.Id, instance.Id, assigner.Id);

        // Assert
        result.Success.Should().BeTrue(
            $"Expected clean assignment but got: ErrorKey={result.ErrorKey}, " +
            $"Errors={string.Join(",", result.Validation?.Errors.Select(e => e.Key) ?? Array.Empty<string>())}, " +
            $"Warnings={string.Join(",", result.Validation?.Warnings.Select(w => w.Key) ?? Array.Empty<string>())}");
        result.AssignmentId.Should().NotBeNull();

        var assignment = await Db.ShiftAssignments
            .FirstOrDefaultAsync(a => a.Id == result.AssignmentId);
        assignment.Should().NotBeNull();
        assignment!.UserId.Should().Be(user.Id);
        assignment.ShiftInstanceId.Should().Be(instance.Id);

        // Cleanup
        TrackEntity(assignment);
    }

    [Fact]
    public async Task UnassignShift_ExistingAssignment_Succeeds()
    {
        // Arrange — clean assignment (no JobType filter)
        var shiftType = await CreateShiftTypeAsync(jobTypeId: null);
        var instance = await CreateShiftInstanceAsync(shiftType);
        var user = GetTestUser("Tzafona", "Lead");
        var assigner = GetTestUser("Tzafona", "Director");

        var service = CreateShiftAssignmentService();
        var assignResult = await service.AssignShiftAsync(user.Id, instance.Id, assigner.Id);
        assignResult.Success.Should().BeTrue("precondition: assignment must succeed");

        // Act
        var unassignResult = await service.UnassignShiftAsync(user.Id, instance.Id, assigner.Id, "test reason");

        // Assert
        unassignResult.Should().BeTrue();
        var remaining = await Db.ShiftAssignments
            .AnyAsync(a => a.ShiftInstanceId == instance.Id && a.UserId == user.Id);
        remaining.Should().BeFalse();
    }

    [Fact]
    public async Task GetEligibleUsers_FiltersByJobType()
    {
        // Arrange — Alhut-only shift type in Oren molecule
        var alhutJobTypeId = Fixture.JobTypeByName["Alhut"].Id;
        var shiftType = await CreateShiftTypeAsync(jobTypeId: alhutJobTypeId);
        var instance = await CreateShiftInstanceAsync(shiftType);

        var service = CreateShiftAssignmentService();

        // Act
        var eligible = await service.GetEligibleUsersForShiftAsync(instance.Id);

        // Assert — only Alhut users in Oren molecule should be returned
        eligible.Should().NotBeEmpty();

        // Query DB directly for the ground truth (fixture dict may miss users with duplicate emails)
        var orenMoleculeId = Fixture.MoleculeByName["Oren"].Id;
        var orenCompanyIds = await Db.Companies
            .Where(c => c.MoleculeId == orenMoleculeId)
            .Select(c => c.Id)
            .ToListAsync();
        var alhutUsersInOren = await Db.Users
            .Where(u => u.IsActive && u.JobTypeId == alhutJobTypeId && orenCompanyIds.Contains(u.CompanyId))
            .Select(u => u.Id)
            .ToListAsync();

        eligible.Select(e => e.UserId).Should().BeSubsetOf(alhutUsersInOren);

        // Verify no non-Alhut users appear
        var nonAlhutUserIds = await Db.Users
            .Where(u => u.IsActive && u.JobTypeId != alhutJobTypeId && orenCompanyIds.Contains(u.CompanyId))
            .Select(u => u.Id)
            .ToListAsync();
        eligible.Should().NotContain(u => nonAlhutUserIds.Contains(u.UserId));
    }

    [Fact]
    public async Task GetEligibleUsers_FiltersByShiftGrouping()
    {
        // Arrange — shift with Tzafon grouping (Tzafona + City companies)
        var alhutJobTypeId = Fixture.JobTypeByName["Alhut"].Id;
        var tzafonGroupingId = Fixture.ShiftGroupingByName["Tzafon"].Id;
        var shiftType = await CreateShiftTypeAsync(
            jobTypeId: alhutJobTypeId,
            shiftGroupingId: tzafonGroupingId);
        var instance = await CreateShiftInstanceAsync(shiftType);

        var service = CreateShiftAssignmentService();

        // Act
        var eligible = await service.GetEligibleUsersForShiftAsync(instance.Id);

        // Assert — only users from Tzafona and City companies with Alhut job type
        var tzafonaId = Fixture.CompanyByName["Tzafona"].Id;
        var cityId = Fixture.CompanyByName["City"].Id;
        var groupingCompanyIds = new HashSet<int> { tzafonaId, cityId };

        // All returned users should be from grouping companies
        var allUsers = await Db.Users.ToDictionaryAsync(u => u.Id);
        foreach (var eu in eligible)
        {
            allUsers.Should().ContainKey(eu.UserId);
            groupingCompanyIds.Should().Contain(allUsers[eu.UserId].CompanyId,
                $"user {eu.DisplayName} (company {allUsers[eu.UserId].CompanyId}) should be in Tzafon grouping");
        }

        // Verify at least one user was returned (Tzafona has Alhut users)
        eligible.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetEligibleUsers_NoFilters_ReturnsAllActiveUsersInMolecule()
    {
        // Arrange — shift with no JobType and no ShiftGrouping
        var shiftType = await CreateShiftTypeAsync(jobTypeId: null, shiftGroupingId: null);
        var instance = await CreateShiftInstanceAsync(shiftType);

        var service = CreateShiftAssignmentService();

        // Act
        var eligible = await service.GetEligibleUsersForShiftAsync(instance.Id);

        // Assert — should return all active users in Oren molecule companies
        var orenMoleculeId = Fixture.MoleculeByName["Oren"].Id;
        var orenCompanyIds = await Db.Companies
            .Where(c => c.MoleculeId == orenMoleculeId)
            .Select(c => c.Id)
            .ToListAsync();
        var orenCompanyIdSet = orenCompanyIds.ToHashSet();

        // All eligible users must be from Oren molecule
        var allUsers = await Db.Users.ToDictionaryAsync(u => u.Id);
        foreach (var eu in eligible)
        {
            allUsers.Should().ContainKey(eu.UserId);
            orenCompanyIdSet.Should().Contain(allUsers[eu.UserId].CompanyId);
        }

        // Count expected: all active users in Oren companies (from DB, not fixture dict)
        var expectedCount = await Db.Users
            .CountAsync(u => u.IsActive && orenCompanyIds.Contains(u.CompanyId));
        eligible.Should().HaveCount(expectedCount);
    }

    [Fact]
    public async Task AssignShift_DuplicateAssignment_Fails()
    {
        // Arrange — clean shift (no JobType filter)
        var shiftType = await CreateShiftTypeAsync(jobTypeId: null);
        var instance = await CreateShiftInstanceAsync(shiftType);
        var user = GetTestUser("Tzafona", "Lead");
        var assigner = GetTestUser("Tzafona", "Director");

        var service = CreateShiftAssignmentService();
        var firstResult = await service.AssignShiftAsync(user.Id, instance.Id, assigner.Id);
        firstResult.Success.Should().BeTrue("precondition: first assignment must succeed");
        var firstAssignment = await Db.ShiftAssignments.FindAsync(firstResult.AssignmentId);
        TrackEntity(firstAssignment!);

        // Act — attempt duplicate
        var duplicateResult = await service.AssignShiftAsync(user.Id, instance.Id, assigner.Id);

        // Assert
        duplicateResult.Success.Should().BeFalse();
        duplicateResult.ErrorKey.Should().Be("ALREADY_ASSIGNED");
    }

    [Fact]
    public async Task AssignShift_NonExistentUser_ReturnsError()
    {
        // Arrange
        var shiftType = await CreateShiftTypeAsync();
        var instance = await CreateShiftInstanceAsync(shiftType);
        var assigner = GetTestUser("Tzafona", "Lead");
        var service = CreateShiftAssignmentService();

        // Act
        var result = await service.AssignShiftAsync(99999, instance.Id, assigner.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Validation.Should().NotBeNull();
        result.Validation!.Errors.Should().Contain(e => e.Key == "USER_NOT_FOUND");
    }

    [Fact]
    public async Task AssignShift_NonExistentShift_ReturnsError()
    {
        // Arrange
        var user = GetTestUser("Tzafona", "Lead");
        var assigner = GetTestUser("Tzafona", "Director");
        var service = CreateShiftAssignmentService();

        // Act
        var result = await service.AssignShiftAsync(user.Id, 99999, assigner.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Validation.Should().NotBeNull();
        result.Validation!.Errors.Should().Contain(e => e.Key == "SHIFT_NOT_FOUND");
    }

    [Fact]
    public async Task AssignShift_SetsAssignmentFields()
    {
        // Arrange — clean shift (no JobType filter)
        var shiftType = await CreateShiftTypeAsync(jobTypeId: null);
        var instance = await CreateShiftInstanceAsync(shiftType);
        var user = GetTestUser("Tzafona", "Lead");
        var assigner = GetTestUser("Tzafona", "Director");

        var service = CreateShiftAssignmentService();
        var beforeAssign = DateTime.UtcNow;

        // Act
        var result = await service.AssignShiftAsync(user.Id, instance.Id, assigner.Id);

        // Assert
        result.Success.Should().BeTrue(
            $"Expected clean assignment but got: ErrorKey={result.ErrorKey}");
        result.AssignmentId.Should().NotBeNull();

        var assignment = await Db.ShiftAssignments.FindAsync(result.AssignmentId);
        assignment.Should().NotBeNull();
        assignment!.UserId.Should().Be(user.Id);
        assignment.ShiftInstanceId.Should().Be(instance.Id);
        assignment.CompanyId.Should().Be(user.CompanyId, "assignment should use the employee's CompanyId");
        assignment.CreatedAt.Should().BeOnOrAfter(beforeAssign);
        assignment.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Cleanup
        TrackEntity(assignment);
    }
}
