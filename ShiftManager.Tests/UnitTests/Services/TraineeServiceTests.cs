using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class TraineeServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<ICompanyLocalizationService> _localizationServiceMock;
    private readonly TraineeService _service;

    private const int CompanyId = 1;
    private const int ManagerUserId = 100;

    public TraineeServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _notificationServiceMock = new Mock<INotificationService>();
        _notificationServiceMock.Setup(n => n.CreateNotificationAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyId);

        _localizationServiceMock = new Mock<ICompanyLocalizationService>();
        _localizationServiceMock.Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);

        var localizerMock = new Mock<Microsoft.Extensions.Localization.IStringLocalizer<ShiftManager.Resources.SharedResources>>();
        localizerMock.Setup(x => x[It.IsAny<string>()])
            .Returns<string>(key => new Microsoft.Extensions.Localization.LocalizedString(key, key));
        var localizationMock = new Mock<ILocalizationService>();
        localizationMock.Setup(l => l.FormatMediumDate(It.IsAny<DateOnly>()))
            .Returns<DateOnly>(d => d.ToString("yyyy-MM-dd"));

        _service = new TraineeService(
            _db,
            Mock.Of<ILogger<TraineeService>>(),
            _notificationServiceMock.Object,
            _tenantResolverMock.Object,
            _localizationServiceMock.Object,
            localizerMock.Object,
            localizationMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private ShiftType CreateShiftType(int id = 1, string name = "Morning",
        TimeOnly? start = null, TimeOnly? end = null)
    {
        return new ShiftType
        {
            Id = id,
            Name = name,
            Key = ShiftType.KEY_MORNING,
            MoleculeId = 1,
            Start = start ?? new TimeOnly(7, 0),
            End = end ?? new TimeOnly(15, 0)
        };
    }

    private async Task SeedShiftTypeAsync(ShiftType shiftType)
    {
        if (!await _db.Set<ShiftType>().AnyAsync(st => st.Id == shiftType.Id))
        {
            _db.Add(shiftType);
            await _db.SaveChangesAsync();
        }
    }

    private async Task<(ShiftInstance Instance, ShiftAssignment Assignment)> SeedShiftWithAssignmentAsync(
        int employeeId, int shiftTypeId = 1, DateOnly? workDate = null, int assignmentId = 0)
    {
        var date = workDate ?? new DateOnly(2026, 3, 15);
        var instance = new ShiftInstance
        {
            CompanyId = CompanyId,
            ShiftTypeId = shiftTypeId,
            WorkDate = date
        };
        _db.ShiftInstances.Add(instance);
        await _db.SaveChangesAsync();

        var assignment = new ShiftAssignment
        {
            CompanyId = CompanyId,
            ShiftInstanceId = instance.Id,
            UserId = employeeId
        };
        if (assignmentId > 0) assignment.Id = assignmentId;
        _db.ShiftAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        return (instance, assignment);
    }

    private async Task<AppUser> SeedUserAsync(int id, UserRole role = UserRole.Employee,
        string? displayName = null)
    {
        var user = new AppUser
        {
            Id = id,
            CompanyId = CompanyId,
            Email = $"user{id}@test.com",
            DisplayName = displayName ?? $"User {id}",
            Role = role,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // --- ValidateTraineeAssignmentAsync ---

    [Fact]
    public async Task ValidateTraineeAssignment_AssignmentNotFound_ReturnsInvalid()
    {
        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(999, 1);

        isValid.Should().BeFalse();
        error.Should().Contain("Error_TraineeAssignment_ShiftNotFound");
    }

    [Fact]
    public async Task ValidateTraineeAssignment_UnassignedSlot_ReturnsInvalid()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);

        var instance = new ShiftInstance { CompanyId = CompanyId, ShiftTypeId = 1, WorkDate = new DateOnly(2026, 3, 15) };
        _db.ShiftInstances.Add(instance);
        await _db.SaveChangesAsync();

        // Empty slot — UserId is null
        var assignment = new ShiftAssignment { CompanyId = CompanyId, ShiftInstanceId = instance.Id, UserId = null };
        _db.ShiftAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 5);

        isValid.Should().BeFalse();
        error.Should().Contain("Error_TraineeAssignment_CannotAssign");
    }

    [Fact]
    public async Task ValidateTraineeAssignment_TraineeSameAsPrimary_ReturnsInvalid()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);

        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 10);

        isValid.Should().BeFalse();
        error.Should().Contain("Error_TraineeAssignment_CannotAssign");
    }

    [Fact]
    public async Task ValidateTraineeAssignment_AlreadyHasTrainee_ReturnsInvalid()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee);
        await SeedUserAsync(30, UserRole.Trainee);

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);
        assignment.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 30);

        isValid.Should().BeFalse();
        error.Should().Contain("Error_TraineeAssignment_AlreadyHasTrainee");
    }

    [Fact]
    public async Task ValidateTraineeAssignment_TraineeNotFound_ReturnsInvalid()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);

        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 999);

        isValid.Should().BeFalse();
        error.Should().Contain("Error_TraineeAssignment_TraineeNotFound");
    }

    [Fact]
    public async Task ValidateTraineeAssignment_UserNotTraineeRole_ReturnsInvalid()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Employee); // Not a Trainee

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);

        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 20);

        isValid.Should().BeFalse();
        error.Should().Contain("Error_TraineeAssignment_NotATrainee");
    }

    [Fact]
    public async Task ValidateTraineeAssignment_DifferentCompany_ReturnsInvalid()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);

        // Create trainee in different company
        var trainee = new AppUser
        {
            Id = 20,
            CompanyId = 999, // different company
            Email = "trainee@other.com",
            DisplayName = "Other Trainee",
            Role = UserRole.Trainee,
            IsActive = true
        };
        _db.Users.Add(trainee);
        await _db.SaveChangesAsync();

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);

        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 20);

        isValid.Should().BeFalse();
        error.Should().Contain("Error_TraineeAssignment_DifferentCompany");
    }

    [Fact]
    public async Task ValidateTraineeAssignment_NoConflict_ReturnsValid()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee);

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);

        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 20);

        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTraineeAssignment_TimeConflict_ReturnsInvalid()
    {
        // Morning shift: 07:00-15:00
        var morningType = CreateShiftType(id: 1, name: "Morning",
            start: new TimeOnly(7, 0), end: new TimeOnly(15, 0));
        await SeedShiftTypeAsync(morningType);

        // Overlapping shift: 10:00-18:00
        var middleType = new ShiftType
        {
            Id = 2, Name = "Middle", Key = ShiftType.KEY_MIDDLE, MoleculeId = 1,
            Start = new TimeOnly(10, 0), End = new TimeOnly(18, 0)
        };
        await SeedShiftTypeAsync(middleType);

        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee);

        var workDate = new DateOnly(2026, 3, 15);

        // Trainee already assigned to the middle shift on same date
        var (_, existingAssignment) = await SeedShiftWithAssignmentAsync(employeeId: 10, shiftTypeId: 2, workDate: workDate);
        existingAssignment.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        // Now try to also assign trainee to overlapping morning shift
        var (_, morningAssignment) = await SeedShiftWithAssignmentAsync(employeeId: 10, shiftTypeId: 1, workDate: workDate);

        var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(morningAssignment.Id, 20);

        isValid.Should().BeFalse();
        error.Should().Contain("Error_TraineeAssignment_TimeConflict");
    }

    // --- AssignTraineeToShiftAsync ---

    [Fact]
    public async Task AssignTraineeToShift_HappyPath_AssignsAndNotifies()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee, "Primary Employee");
        await SeedUserAsync(20, UserRole.Trainee, "Test Trainee");

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);

        var result = await _service.AssignTraineeToShiftAsync(assignment.Id, 20, ManagerUserId);

        result.Should().BeTrue();

        // Verify assignment updated
        var updated = await _db.ShiftAssignments.FindAsync(assignment.Id);
        updated!.TraineeUserId.Should().Be(20);

        // Verify notifications sent to trainee and primary user (now via NotifyAsync = in-app + email)
        _notificationServiceMock.Verify(n => n.NotifyAsync(
            20, NotificationType.TraineeShadowingAdded, ShiftManager.Services.Notifications.NotificationCategory.Trainee,
            It.IsAny<string>(), It.IsAny<string>(), true, false, assignment.Id, "ShiftAssignment"), Times.Once);

        _notificationServiceMock.Verify(n => n.NotifyAsync(
            10, NotificationType.EmployeeTraineeAdded, ShiftManager.Services.Notifications.NotificationCategory.Trainee,
            It.IsAny<string>(), It.IsAny<string>(), false, false, assignment.Id, "ShiftAssignment"), Times.Once);
    }

    [Fact]
    public async Task AssignTraineeToShift_ValidationFails_ReturnsFalse()
    {
        // No shift types, no users — everything will fail
        var result = await _service.AssignTraineeToShiftAsync(999, 888, ManagerUserId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AssignTraineeToShift_TraineeNotFound_ReturnsFalse()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);

        var result = await _service.AssignTraineeToShiftAsync(assignment.Id, 999, ManagerUserId);

        result.Should().BeFalse();
    }

    // --- RemoveTraineeFromShiftAsync ---

    [Fact]
    public async Task RemoveTraineeFromShift_HappyPath_ClearsAndNotifies()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee, "Primary");
        await SeedUserAsync(20, UserRole.Trainee, "Trainee");

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);
        assignment.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        var result = await _service.RemoveTraineeFromShiftAsync(assignment.Id, "No longer needed", ManagerUserId);

        result.Should().BeTrue();

        var updated = await _db.ShiftAssignments.FindAsync(assignment.Id);
        updated!.TraineeUserId.Should().BeNull();
    }

    [Fact]
    public async Task RemoveTraineeFromShift_NoTrainee_ReturnsFalse()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);
        // No trainee assigned

        var result = await _service.RemoveTraineeFromShiftAsync(assignment.Id, "test", ManagerUserId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveTraineeFromShift_AssignmentNotFound_ReturnsFalse()
    {
        var result = await _service.RemoveTraineeFromShiftAsync(999, "test", ManagerUserId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveTraineeFromShift_RoleChangedReason_UsesCorrectNotificationType()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee, "Trainee");

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);
        assignment.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        await _service.RemoveTraineeFromShiftAsync(assignment.Id, "RoleChanged", ManagerUserId);

        _notificationServiceMock.Verify(n => n.NotifyAsync(
            20, NotificationType.TraineeShadowingCanceledRoleChange, ShiftManager.Services.Notifications.NotificationCategory.Trainee,
            It.IsAny<string>(), It.IsAny<string>(), true, false, It.IsAny<int?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RemoveTraineeFromShift_TimeOffReason_UsesCorrectNotificationType()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee, "Trainee");

        var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);
        assignment.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        await _service.RemoveTraineeFromShiftAsync(assignment.Id, "TimeOff", ManagerUserId);

        _notificationServiceMock.Verify(n => n.NotifyAsync(
            20, NotificationType.TraineeShadowingCanceledTimeOff, ShiftManager.Services.Notifications.NotificationCategory.Trainee,
            It.IsAny<string>(), It.IsAny<string>(), true, false, It.IsAny<int?>(), It.IsAny<string?>()), Times.Once);
    }

    // --- GetTraineeShadowedShiftsAsync ---

    [Fact]
    public async Task GetTraineeShadowedShifts_ReturnsAssignmentsInRange()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee);

        var date1 = new DateOnly(2026, 3, 10);
        var date2 = new DateOnly(2026, 3, 20);
        var dateOutside = new DateOnly(2026, 4, 1);

        var (_, a1) = await SeedShiftWithAssignmentAsync(10, workDate: date1);
        a1.TraineeUserId = 20;
        var (_, a2) = await SeedShiftWithAssignmentAsync(10, workDate: date2);
        a2.TraineeUserId = 20;
        var (_, a3) = await SeedShiftWithAssignmentAsync(10, workDate: dateOutside);
        a3.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        var result = await _service.GetTraineeShadowedShiftsAsync(
            20, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTraineeShadowedShifts_ReturnsEmpty_WhenNoAssignments()
    {
        var result = await _service.GetTraineeShadowedShiftsAsync(
            999, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        result.Should().BeEmpty();
    }

    // --- CancelAllShadowingAssignmentsAsync ---

    [Fact]
    public async Task CancelAllShadowingAssignments_ClearsFutureAssignments()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee, "Trainee");

        // Future shift
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var (_, futureAssignment) = await SeedShiftWithAssignmentAsync(10, workDate: futureDate);
        futureAssignment.TraineeUserId = 20;

        // Past shift — should not be affected
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
        var (_, pastAssignment) = await SeedShiftWithAssignmentAsync(10, workDate: pastDate);
        pastAssignment.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        var result = await _service.CancelAllShadowingAssignmentsAsync(20, "RoleChanged", ManagerUserId);

        result.Should().Be(1); // only future assignment canceled

        var updatedFuture = await _db.ShiftAssignments.FindAsync(futureAssignment.Id);
        updatedFuture!.TraineeUserId.Should().BeNull();

        var updatedPast = await _db.ShiftAssignments.FindAsync(pastAssignment.Id);
        updatedPast!.TraineeUserId.Should().Be(20); // past not touched
    }

    [Fact]
    public async Task CancelAllShadowingAssignments_NoAssignments_ReturnsZero()
    {
        await SeedUserAsync(20, UserRole.Trainee);

        var result = await _service.CancelAllShadowingAssignmentsAsync(20, "test", ManagerUserId);

        result.Should().Be(0);
    }

    [Fact]
    public async Task CancelAllShadowingAssignments_CreatesNotifications()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee, "Trainee");

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var (_, assignment) = await SeedShiftWithAssignmentAsync(10, workDate: futureDate);
        assignment.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        await _service.CancelAllShadowingAssignmentsAsync(20, "RoleChanged", ManagerUserId);

        // Should have created notifications in the database directly (batch approach)
        var notifications = await _db.UserNotifications.ToListAsync();
        notifications.Should().NotBeEmpty();

        // One for the primary employee + one for the trainee
        notifications.Should().Contain(n => n.UserId == 20); // trainee notification
        notifications.Should().Contain(n => n.UserId == 10); // primary employee notification
    }

    // --- CancelShadowingForTimeOffAsync ---

    [Fact]
    public async Task CancelShadowingForTimeOff_CancelsOverlappingAssignments()
    {
        var shiftType = CreateShiftType();
        await SeedShiftTypeAsync(shiftType);
        await SeedUserAsync(10, UserRole.Employee);
        await SeedUserAsync(20, UserRole.Trainee, "Trainee");

        // Shift on March 15 — within time off range
        var (_, a1) = await SeedShiftWithAssignmentAsync(10, workDate: new DateOnly(2026, 3, 15));
        a1.TraineeUserId = 20;

        // Shift on March 20 — outside time off range
        var (_, a2) = await SeedShiftWithAssignmentAsync(10, workDate: new DateOnly(2026, 3, 20));
        a2.TraineeUserId = 20;
        await _db.SaveChangesAsync();

        var result = await _service.CancelShadowingForTimeOffAsync(
            20, new DateTime(2026, 3, 10), new DateTime(2026, 3, 16));

        result.Should().Be(1);

        var updated1 = await _db.ShiftAssignments.FindAsync(a1.Id);
        updated1!.TraineeUserId.Should().BeNull();

        var updated2 = await _db.ShiftAssignments.FindAsync(a2.Id);
        updated2!.TraineeUserId.Should().Be(20); // not in range
    }

    [Fact]
    public async Task CancelShadowingForTimeOff_NoOverlap_ReturnsZero()
    {
        var result = await _service.CancelShadowingForTimeOffAsync(
            999, new DateTime(2026, 3, 10), new DateTime(2026, 3, 16));

        result.Should().Be(0);
    }

    // --- GetCompanyTraineesAsync ---

    [Fact]
    public async Task GetCompanyTrainees_ReturnsOnlyTraineeRole()
    {
        await SeedUserAsync(1, UserRole.Employee);
        await SeedUserAsync(2, UserRole.Trainee, "Trainee A");
        await SeedUserAsync(3, UserRole.Trainee, "Trainee B");
        await SeedUserAsync(4, UserRole.Manager);

        var result = await _service.GetCompanyTraineesAsync(CompanyId);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(u => u.Role == UserRole.Trainee);
    }

    [Fact]
    public async Task GetCompanyTrainees_ReturnsOnlyFromCorrectCompany()
    {
        await SeedUserAsync(1, UserRole.Trainee);

        // Trainee in different company
        _db.Users.Add(new AppUser
        {
            Id = 2,
            CompanyId = 999,
            Email = "other@test.com",
            DisplayName = "Other Company Trainee",
            Role = UserRole.Trainee,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetCompanyTraineesAsync(CompanyId);

        result.Should().HaveCount(1);
        result.First().CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public async Task GetCompanyTrainees_ReturnsSortedByDisplayName()
    {
        await SeedUserAsync(1, UserRole.Trainee, "Zara");
        await SeedUserAsync(2, UserRole.Trainee, "Alice");
        await SeedUserAsync(3, UserRole.Trainee, "Moe");

        var result = await _service.GetCompanyTraineesAsync(CompanyId);

        result.Should().HaveCount(3);
        result[0].DisplayName.Should().Be("Alice");
        result[1].DisplayName.Should().Be("Moe");
        result[2].DisplayName.Should().Be("Zara");
    }

    [Fact]
    public async Task GetCompanyTrainees_EmptyCompany_ReturnsEmpty()
    {
        var result = await _service.GetCompanyTraineesAsync(999);

        result.Should().BeEmpty();
    }
}
