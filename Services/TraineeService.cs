using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

public class TraineeService : ITraineeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TraineeService> _logger;
    private readonly INotificationService _notificationService;
    private readonly ITenantResolver _tenantResolver;
    private readonly ICompanyLocalizationService _localizationService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILocalizationService _localization;

    public TraineeService(
        AppDbContext db,
        ILogger<TraineeService> logger,
        INotificationService notificationService,
        ITenantResolver tenantResolver,
        ICompanyLocalizationService localizationService,
        IStringLocalizer<SharedResources> localizer,
        ILocalizationService localization)
    {
        _db = db;
        _logger = logger;
        _notificationService = notificationService;
        _tenantResolver = tenantResolver;
        _localizationService = localizationService;
        _localizer = localizer;
        _localization = localization;
    }

    /// <summary>Localized "{shiftType} on {date}" fragment used inside trainee notifications.</summary>
    private string FormatShiftInfo(string shiftTypeName, DateOnly date)
        => string.Format(_localizer["Trainee_ShiftInfo"].Value, shiftTypeName, _localization.FormatMediumDate(date));

    /// <summary>Localized human phrase for a shadowing-cancellation reason token.</summary>
    private string LocalizeReason(string reason) => reason switch
    {
        "RoleChanged" => _localizer["Trainee_Reason_RoleChanged"].Value,
        "TimeOff" => _localizer["Trainee_Reason_TimeOff"].Value,
        _ => reason
    };

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

            // SECURITY-AUDITED: SAFE — same-company business rule still enforced in
            // ValidateTraineeAssignmentAsync (line ~202); bypass needed so cross-tenant
            // managers get the explicit "must belong to same company" error rather than
            // a misleading "trainee not found".
            var trainee = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == traineeUserId);
            if (trainee == null)
            {
                _logger.LogWarning("Trainee user {TraineeUserId} not found", traineeUserId);
                return false;
            }

            assignment.TraineeUserId = traineeUserId;
            await _db.SaveChangesAsync();

            // Send notifications
            var shiftTypeName = await _localizationService.ResolveShiftTypeNameAsync(assignment.ShiftInstance.ShiftType, assignment.ShiftInstance.CompanyId, CultureInfo.CurrentUICulture.Name);
            var shiftInfo = FormatShiftInfo(shiftTypeName, assignment.ShiftInstance.WorkDate);
            var primaryUserName = assignment.User?.DisplayName ?? "an employee";

            await _notificationService.NotifyAsync(
                traineeUserId,
                NotificationType.TraineeShadowingAdded,
                Notifications.NotificationCategory.Trainee,
                _localizer["Trainee_ShadowingAddedTitle"].Value,
                string.Format(_localizer["Trainee_ShadowingAddedMessage"].Value, primaryUserName, shiftInfo),
                personallyActionable: true,
                relatedEntityId: shiftAssignmentId,
                relatedEntityType: "ShiftAssignment"
            );

            // Notify primary user if assigned (informational → not personally-actionable)
            if (assignment.UserId.HasValue)
            {
                await _notificationService.NotifyAsync(
                    assignment.UserId.Value,
                    NotificationType.EmployeeTraineeAdded,
                    Notifications.NotificationCategory.Trainee,
                    _localizer["Trainee_AssignedTitle"].Value,
                    string.Format(_localizer["Trainee_AssignedMessage"].Value, trainee.DisplayName, shiftInfo),
                    personallyActionable: false,
                    relatedEntityId: shiftAssignmentId,
                    relatedEntityType: "ShiftAssignment"
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
            var shiftTypeName = await _localizationService.ResolveShiftTypeNameAsync(assignment.ShiftInstance.ShiftType, assignment.ShiftInstance.CompanyId, CultureInfo.CurrentUICulture.Name);
            var shiftInfo = FormatShiftInfo(shiftTypeName, assignment.ShiftInstance.WorkDate);

            assignment.TraineeUserId = null;
            await _db.SaveChangesAsync();

            // Send notifications
            var notificationType = reason == "RoleChanged"
                ? NotificationType.TraineeShadowingCanceledRoleChange
                : reason == "TimeOff"
                    ? NotificationType.TraineeShadowingCanceledTimeOff
                    : NotificationType.TraineeShadowingRemoved;

            await _notificationService.NotifyAsync(
                traineeId,
                notificationType,
                Notifications.NotificationCategory.Trainee,
                _localizer["Trainee_ShadowingRemovedTitle"].Value,
                string.Format(_localizer["Trainee_ShadowingRemovedMessage"].Value, shiftInfo, LocalizeReason(reason)),
                personallyActionable: true,
                relatedEntityId: shiftAssignmentId,
                relatedEntityType: "ShiftAssignment"
            );

            // Notify primary employee if assigned (informational → not personally-actionable)
            if (assignment.UserId.HasValue)
            {
                await _notificationService.NotifyAsync(
                    assignment.UserId.Value,
                    NotificationType.EmployeeTraineeRemoved,
                    Notifications.NotificationCategory.Trainee,
                    _localizer["Trainee_RemovedTitle"].Value,
                    string.Format(_localizer["Trainee_RemovedMessage"].Value, traineeName, shiftInfo),
                    personallyActionable: false,
                    relatedEntityId: shiftAssignmentId,
                    relatedEntityType: "ShiftAssignment"
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
            return (false, _localizer["Error_TraineeAssignment_ShiftNotFound"].Value);
        }

        if (!assignment.UserId.HasValue || assignment.UserId.Value == traineeUserId)
        {
            return (false, _localizer["Error_TraineeAssignment_CannotAssign"].Value);
        }

        if (assignment.TraineeUserId != null)
        {
            return (false, _localizer["Error_TraineeAssignment_AlreadyHasTrainee"].Value);
        }

        // SECURITY-AUDITED: SAFE — bypass tenant filter so the explicit same-company check
        // below produces the actionable error instead of a misleading "trainee not found".
        var trainee = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == traineeUserId);
        if (trainee == null)
        {
            return (false, _localizer["Error_TraineeAssignment_TraineeNotFound"].Value);
        }

        if (trainee.Role != UserRole.Trainee)
        {
            return (false, _localizer["Error_TraineeAssignment_NotATrainee"].Value);
        }

        if (trainee.CompanyId != assignment.CompanyId)
        {
            return (false, _localizer["Error_TraineeAssignment_DifferentCompany"].Value);
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
            return (false, _localizer["Error_TraineeAssignment_TimeConflict"].Value);
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

            // SECURITY-AUDITED: SAFE — read-only display name lookup; cross-tenant cancel needs
            // to surface the correct trainee name in notifications.
            var trainee = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == userId);
            var traineeName = trainee?.DisplayName ?? "User";
            var companyId = assignments.First().CompanyId;

            // Batch create notifications for better performance
            var notifications = new List<UserNotification>();

            foreach (var assignment in assignments)
            {
                var shiftTypeName = await _localizationService.ResolveShiftTypeNameAsync(assignment.ShiftInstance.ShiftType, assignment.ShiftInstance.CompanyId, CultureInfo.CurrentUICulture.Name);
                var shiftInfo = FormatShiftInfo(shiftTypeName, assignment.ShiftInstance.WorkDate);

                assignment.TraineeUserId = null;

                // Add notification for the primary employee if assigned
                if (assignment.UserId.HasValue)
                {
                    notifications.Add(new UserNotification
                    {
                        CompanyId = companyId,
                        UserId = assignment.UserId.Value,
                        Type = NotificationType.EmployeeTraineeRemoved,
                        Title = _localizer["Trainee_RemovedTitle"].Value,
                        Message = string.Format(_localizer["Trainee_RemovedWithReasonMessage"].Value, traineeName, shiftInfo, LocalizeReason(reason)),
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
                Title = _localizer["Trainee_AllCanceledTitle"].Value,
                Message = string.Format(_localizer["Trainee_AllCanceledMessage"].Value, LocalizeReason(reason)),
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
                var shiftTypeName = await _localizationService.ResolveShiftTypeNameAsync(assignment.ShiftInstance.ShiftType, assignment.ShiftInstance.CompanyId, CultureInfo.CurrentUICulture.Name);
                var shiftInfo = FormatShiftInfo(shiftTypeName, assignment.ShiftInstance.WorkDate);

                assignment.TraineeUserId = null;

                // Add notification for trainee
                notifications.Add(new UserNotification
                {
                    CompanyId = companyId,
                    UserId = traineeUserId,
                    Type = NotificationType.TraineeShadowingCanceledTimeOff,
                    Title = _localizer["Trainee_ShadowingCanceledTimeOffTitle"].Value,
                    Message = string.Format(_localizer["Trainee_ShadowingCanceledTimeOffMessage"].Value, shiftInfo),
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
                        Title = _localizer["Trainee_RemovedTitle"].Value,
                        Message = string.Format(_localizer["Trainee_RemovedTimeOffMessage"].Value, traineeName, shiftInfo),
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

    public async Task<List<AppUser>> GetMoleculeTraineesAsync(int moleculeId, int? jobTypeId)
    {
        // PF12 (D1/D6): trainees are molecule-wide — the origin bug was a City lead unable to pick a Tzafona
        // trainee. Job-type scoped with a NULL-inclusion safety net: a trainee whose JobTypeId is null (mis-
        // seeded, or pre-backfill on an existing DB — ORG-8) is universally relevant so it is never hidden,
        // the exact origin-bug failure. When there is no job-type context (jobTypeId == null, e.g. Tech),
        // show every molecule trainee.
        // SECURITY-AUDITED: SAFE — molecule-scoped (Company.MoleculeId == moleculeId). The calling page gates
        // molecule access before this runs (IDOR); only trainees inside the requested molecule are returned.
        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Role == UserRole.Trainee
                        && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId)
                        && (jobTypeId == null || u.JobTypeId == jobTypeId || u.JobTypeId == null))
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }
}
