using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class TraineeService : ITraineeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TraineeService> _logger;
    private readonly INotificationService _notificationService;
    private readonly ITenantResolver _tenantResolver;

    public TraineeService(
        AppDbContext db,
        ILogger<TraineeService> logger,
        INotificationService notificationService,
        ITenantResolver tenantResolver)
    {
        _db = db;
        _logger = logger;
        _notificationService = notificationService;
        _tenantResolver = tenantResolver;
    }

    public async Task<bool> AssignTraineeToShiftAsync(int shiftAssignmentId, int traineeUserId, int assignedByUserId)
    {
        try
        {
            var (isValid, errorMessage) = await ValidateTraineeAssignmentAsync(shiftAssignmentId, traineeUserId);
            if (!isValid)
            {
                _logger.LogWarning("Trainee assignment validation failed: {ErrorMessage}", errorMessage);
                return false;
            }

            var assignment = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Include(sa => sa.User)
                .FirstOrDefaultAsync(sa => sa.Id == shiftAssignmentId);

            if (assignment == null)
            {
                _logger.LogWarning("Shift assignment {ShiftAssignmentId} not found", shiftAssignmentId);
                return false;
            }

            var trainee = await _db.Users.FindAsync(traineeUserId);
            if (trainee == null)
            {
                _logger.LogWarning("Trainee user {TraineeUserId} not found", traineeUserId);
                return false;
            }

            assignment.TraineeUserId = traineeUserId;
            await _db.SaveChangesAsync();

            // Send notifications
            var shiftInfo = $"{assignment.ShiftInstance.ShiftType.Name} on {assignment.ShiftInstance.WorkDate:MMM dd, yyyy}";
            var primaryUserName = assignment.User?.DisplayName ?? "an employee";

            await _notificationService.CreateNotificationAsync(
                traineeUserId,
                NotificationType.TraineeShadowingAdded,
                "Shadowing Assignment",
                $"You are now shadowing {primaryUserName} for {shiftInfo}",
                shiftAssignmentId,
                "ShiftAssignment"
            );

            // Notify primary user if assigned
            if (assignment.UserId.HasValue)
            {
                await _notificationService.CreateNotificationAsync(
                    assignment.UserId.Value,
                    NotificationType.EmployeeTraineeAdded,
                    "Trainee Assigned",
                    $"{trainee.DisplayName} will shadow your shift: {shiftInfo}",
                    shiftAssignmentId,
                    "ShiftAssignment"
                );
            }

            _logger.LogInformation("Trainee {TraineeId} assigned to shift assignment {ShiftAssignmentId} by user {AssignedById}",
                traineeUserId, shiftAssignmentId, assignedByUserId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning trainee {TraineeId} to shift assignment {ShiftAssignmentId}",
                traineeUserId, shiftAssignmentId);
            return false;
        }
    }

    public async Task<bool> RemoveTraineeFromShiftAsync(int shiftAssignmentId, string reason, int removedByUserId)
    {
        try
        {
            var assignment = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Include(sa => sa.Trainee)
                .FirstOrDefaultAsync(sa => sa.Id == shiftAssignmentId);

            if (assignment == null || assignment.TraineeUserId == null)
            {
                return false;
            }

            var traineeId = assignment.TraineeUserId.Value;
            var traineeName = assignment.Trainee?.DisplayName ?? "Trainee";
            var shiftInfo = $"{assignment.ShiftInstance.ShiftType.Name} on {assignment.ShiftInstance.WorkDate:MMM dd, yyyy}";

            assignment.TraineeUserId = null;
            await _db.SaveChangesAsync();

            // Send notifications
            var notificationType = reason == "RoleChanged"
                ? NotificationType.TraineeShadowingCanceledRoleChange
                : reason == "TimeOff"
                    ? NotificationType.TraineeShadowingCanceledTimeOff
                    : NotificationType.TraineeShadowingRemoved;

            await _notificationService.CreateNotificationAsync(
                traineeId,
                notificationType,
                "Shadowing Assignment Removed",
                $"Your shadowing assignment for {shiftInfo} has been removed. Reason: {reason}",
                shiftAssignmentId,
                "ShiftAssignment"
            );

            // Notify primary employee if assigned
            if (assignment.UserId.HasValue)
            {
                await _notificationService.CreateNotificationAsync(
                    assignment.UserId.Value,
                    NotificationType.EmployeeTraineeRemoved,
                    "Trainee Removed",
                    $"{traineeName} is no longer shadowing your shift: {shiftInfo}",
                    shiftAssignmentId,
                    "ShiftAssignment"
                );
            }

            _logger.LogInformation("Trainee removed from shift assignment {ShiftAssignmentId}. Reason: {Reason}",
                shiftAssignmentId, reason);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing trainee from shift assignment {ShiftAssignmentId}", shiftAssignmentId);
            return false;
        }
    }

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateTraineeAssignmentAsync(int shiftAssignmentId, int traineeUserId)
    {
        var assignment = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .FirstOrDefaultAsync(sa => sa.Id == shiftAssignmentId);

        if (assignment == null)
        {
            return (false, "Shift assignment not found");
        }

        if (!assignment.UserId.HasValue || assignment.UserId.Value == traineeUserId)
        {
            return (false, "Cannot assign trainee to this shift");
        }

        if (assignment.TraineeUserId != null)
        {
            return (false, "This shift already has a trainee assigned");
        }

        var trainee = await _db.Users.FindAsync(traineeUserId);
        if (trainee == null)
        {
            return (false, "Trainee user not found");
        }

        if (trainee.Role != UserRole.Trainee)
        {
            return (false, "User is not a trainee");
        }

        if (trainee.CompanyId != assignment.CompanyId)
        {
            return (false, "Trainee must belong to the same company");
        }

        // Check for time conflicts
        var shiftStartTime = assignment.ShiftInstance.ShiftType.Start;
        var shiftEndTime = assignment.ShiftInstance.ShiftType.End;

        var hasConflict = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => (sa.UserId == traineeUserId || sa.TraineeUserId == traineeUserId)
                      && sa.ShiftInstance.WorkDate == assignment.ShiftInstance.WorkDate)
            .AnyAsync(sa =>
                // Check if time ranges overlap
                (sa.ShiftInstance.ShiftType.Start < shiftEndTime &&
                 sa.ShiftInstance.ShiftType.End > shiftStartTime)
            );

        if (hasConflict)
        {
            return (false, "Trainee has a conflicting shift at this time");
        }

        return (true, null);
    }

    public async Task<List<ShiftAssignment>> GetTraineeShadowedShiftsAsync(int traineeUserId, DateTime startDate, DateTime endDate)
    {
        var startDateOnly = DateOnly.FromDateTime(startDate);
        var endDateOnly = DateOnly.FromDateTime(endDate);

        return await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Include(sa => sa.User)
            .Where(sa => sa.TraineeUserId == traineeUserId
                      && sa.ShiftInstance.WorkDate >= startDateOnly
                      && sa.ShiftInstance.WorkDate <= endDateOnly)
            .OrderBy(sa => sa.ShiftInstance.WorkDate)
            .ThenBy(sa => sa.ShiftInstance.ShiftType.Start)
            .ToListAsync();
    }

    public async Task<int> CancelAllShadowingAssignmentsAsync(int userId, string reason, int changedByUserId)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var assignments = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Include(sa => sa.User)
                .Where(sa => sa.TraineeUserId == userId && sa.ShiftInstance.WorkDate >= today)
                .ToListAsync();

            if (!assignments.Any())
            {
                return 0;
            }

            var trainee = await _db.Users.FindAsync(userId);
            var traineeName = trainee?.DisplayName ?? "User";
            var companyId = assignments.First().CompanyId;

            // Batch create notifications for better performance
            var notifications = new List<UserNotification>();

            foreach (var assignment in assignments)
            {
                var shiftInfo = $"{assignment.ShiftInstance.ShiftType.Name} on {assignment.ShiftInstance.WorkDate:MMM dd, yyyy}";

                assignment.TraineeUserId = null;

                // Add notification for the primary employee if assigned
                if (assignment.UserId.HasValue)
                {
                    notifications.Add(new UserNotification
                    {
                        CompanyId = companyId,
                        UserId = assignment.UserId.Value,
                        Type = NotificationType.EmployeeTraineeRemoved,
                        Title = "Trainee Removed",
                        Message = $"{traineeName} is no longer shadowing your shift: {shiftInfo} (Reason: {reason})",
                        RelatedEntityId = assignment.Id,
                        RelatedEntityType = "ShiftAssignment",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Add notification to the former trainee
            var notificationType = reason == "RoleChanged"
                ? NotificationType.TraineeShadowingCanceledRoleChange
                : NotificationType.TraineeShadowingRemoved;

            notifications.Add(new UserNotification
            {
                CompanyId = companyId,
                UserId = userId,
                Type = notificationType,
                Title = "All Shadowing Assignments Canceled",
                Message = $"All your shadowing assignments have been canceled. Reason: {reason}",
                RelatedEntityId = null,
                RelatedEntityType = "ShiftAssignment",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            // Batch insert all notifications
            if (notifications.Any())
            {
                await _db.UserNotifications.AddRangeAsync(notifications);
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Canceled {Count} shadowing assignments for user {UserId}. Reason: {Reason}",
                assignments.Count, userId, reason);

            return assignments.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling shadowing assignments for user {UserId}", userId);
            return 0;
        }
    }

    public async Task<int> CancelShadowingForTimeOffAsync(int traineeUserId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var startDateOnly = DateOnly.FromDateTime(startDate);
            var endDateOnly = DateOnly.FromDateTime(endDate);

            var overlappingAssignments = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Include(sa => sa.User)
                .Include(sa => sa.Trainee)
                .Where(sa => sa.TraineeUserId == traineeUserId
                          && sa.ShiftInstance.WorkDate >= startDateOnly
                          && sa.ShiftInstance.WorkDate <= endDateOnly)
                .ToListAsync();

            if (!overlappingAssignments.Any())
            {
                return 0;
            }

            var traineeName = overlappingAssignments.First().Trainee?.DisplayName ?? "Trainee";
            var companyId = overlappingAssignments.First().CompanyId;

            // Batch create notifications for better performance
            var notifications = new List<UserNotification>();

            foreach (var assignment in overlappingAssignments)
            {
                var shiftInfo = $"{assignment.ShiftInstance.ShiftType.Name} on {assignment.ShiftInstance.WorkDate:MMM dd, yyyy}";

                assignment.TraineeUserId = null;

                // Add notification for trainee
                notifications.Add(new UserNotification
                {
                    CompanyId = companyId,
                    UserId = traineeUserId,
                    Type = NotificationType.TraineeShadowingCanceledTimeOff,
                    Title = "Shadowing Canceled",
                    Message = $"Your shadowing assignment for {shiftInfo} was canceled due to approved time off",
                    RelatedEntityId = assignment.Id,
                    RelatedEntityType = "ShiftAssignment",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });

                // Add notification for primary employee if assigned
                if (assignment.UserId.HasValue)
                {
                    notifications.Add(new UserNotification
                    {
                        CompanyId = companyId,
                        UserId = assignment.UserId.Value,
                        Type = NotificationType.EmployeeTraineeRemoved,
                        Title = "Trainee Removed",
                        Message = $"{traineeName}'s shadowing for {shiftInfo} was canceled due to approved time off",
                        RelatedEntityId = assignment.Id,
                        RelatedEntityType = "ShiftAssignment",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Batch insert all notifications
            if (notifications.Any())
            {
                await _db.UserNotifications.AddRangeAsync(notifications);
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Canceled {Count} shadowing assignments for trainee {TraineeId} due to time off",
                overlappingAssignments.Count, traineeUserId);

            return overlappingAssignments.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling shadowing for time off for trainee {TraineeId}", traineeUserId);
            return 0;
        }
    }

    public async Task<List<AppUser>> GetCompanyTraineesAsync(int companyId)
    {
        return await _db.Users
            .Where(u => u.CompanyId == companyId && u.Role == UserRole.Trainee)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }
}
