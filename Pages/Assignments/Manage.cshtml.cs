using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Assignments;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManagerHomeAccess policy;
// access further validated per-company (Owner/Director/Manager checks); cross-company assignment explicitly blocked
[Authorize(Policy = "Grant:ManagerHomeAccess")]
public class ManageModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IShiftAssignmentService _assignmentService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ManageModel> _logger;
    private readonly ICompanyContext _companyContext;
    private readonly IDirectorService _directorService;
    private readonly ITraineeService _traineeService;
    private readonly IBusyUserService _busyUserService;
    private readonly IGrantService _grantService;
    private readonly IConcurrencyService _concurrencyService;
    private readonly IAuditLogService _auditLogService;

    public ManageModel(IStringLocalizer<SharedResources> localizer, AppDbContext db, IShiftAssignmentService assignmentService, INotificationService notificationService, ILogger<ManageModel> logger, ICompanyContext companyContext, IDirectorService directorService, ITraineeService traineeService, IBusyUserService busyUserService, IGrantService grantService, IConcurrencyService concurrencyService, IAuditLogService auditLogService)
        : base(localizer)
    {
        _db = db;
        _assignmentService = assignmentService;
        _notificationService = notificationService;
        _logger = logger;
        _companyContext = companyContext;
        _directorService = directorService;
        _traineeService = traineeService;
        _busyUserService = busyUserService;
        _grantService = grantService;
        _concurrencyService = concurrencyService;
        _auditLogService = auditLogService;
    }


    [BindProperty(SupportsGet = true)] public DateOnly Date { get; set; }
    [BindProperty(SupportsGet = true)] public int ShiftTypeId { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public ShiftType? Type { get; set; }
    public ShiftInstance Instance { get; set; } = default!;
    public List<(int AssignmentId, string UserLabel, int? TraineeUserId, string? TraineeName)> Assigned { get; set; } = new();

    [BindProperty] public int? SelectedUserId { get; set; }
    [BindProperty] public string ShiftName { get; set; } = string.Empty;
    public List<AppUser> ActiveUsers { get; set; } = new();
    public List<AppUser> Trainees { get; set; } = new();
    public HashSet<int> UsersOnTimeOff { get; set; } = new();
    public Dictionary<int, BusyStatus> BusyUsers { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        // Load the shift type (across all companies using IgnoreQueryFilters)
        Type = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == ShiftTypeId);

        if (Type == null)
        {
            _logger.LogWarning("ShiftType {ShiftTypeId} not found", ShiftTypeId);
            return RedirectToPage("/Calendar/Month");
        }

        // Validate user identity
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            return RedirectToPage("/Error");
        }

        var currentUser = await _db.Users.FindAsync(currentUserId);
        if (currentUser == null)
        {
            _logger.LogError("User {UserId} not found in database", currentUserId);
            return RedirectToPage("/Error");
        }

        // Company context: use shift type's CompanyId if company-scoped, else current user's company
        var companyId = Type.GetEffectiveCompanyId(currentUser.CompanyId);

        // Check access via grants (not role)
        var isAdmin = await _grantService.HasGrantAsync(currentUser.Id, "AdminAccess");
        bool hasAccess = isAdmin || await _directorService.IsDirectorOfAsync(companyId) || currentUser.CompanyId == companyId;

        if (!hasAccess)
        {
            _logger.LogWarning("User {UserId} attempted to access shift for company {CompanyId} without permission", currentUserId, companyId);
            return RedirectToPage("/AccessDenied");
        }

        Instance = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.WorkDate == Date && i.ShiftTypeId == ShiftTypeId)
            ?? new ShiftInstance { CompanyId = companyId, WorkDate = Date, ShiftTypeId = ShiftTypeId, StaffingRequired = 0 };

        if (Instance.Id == 0)
        {
            _db.ShiftInstances.Add(Instance);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftInstance");
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage("/Calendar/Month");
            }
        }

        var assignments = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Where(a => a.ShiftInstanceId == Instance.Id)
            .ToListAsync();

        // Load users and trainees separately to avoid query filter issues
        var userIds = assignments.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).ToList();
        var traineeIds = assignments.Where(a => a.TraineeUserId.HasValue).Select(a => a.TraineeUserId!.Value).ToList();
        var allUserIds = userIds.Concat(traineeIds).Distinct().ToList();

        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        Assigned = assignments.Where(a => a.UserId.HasValue).Select(a => (
            a.Id,
            $"{users[a.UserId!.Value].DisplayName} ({users[a.UserId.Value].Email})",
            a.TraineeUserId,
            a.TraineeUserId.HasValue && users.ContainsKey(a.TraineeUserId.Value)
                ? users[a.TraineeUserId.Value].DisplayName
                : null
        )).ToList();

        // Load current shift name for display
        ShiftName = Instance.Name ?? string.Empty;

        // Only show users from the shift's company (exclude trainees from regular user list)
        ActiveUsers = await _db.Users
            .Where(u => u.IsActive && u.CompanyId == companyId && u.Role != UserRole.Trainee)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        // Load trainees for this company
        Trainees = await _traineeService.GetCompanyTraineesAsync(companyId);

        // Check which users have approved time off on this date (from same company)
        UsersOnTimeOff = (await _db.TimeOffRequests
            .IgnoreQueryFilters()
            .Where(r => r.CompanyId == companyId &&
                       r.Status == RequestStatus.Approved &&
                       r.StartDate <= Date &&
                       r.EndDate >= Date)
            .Select(r => r.UserId)
            .ToListAsync())
            .ToHashSet();

        // Load busy user status (vacation, shift, chore) for this date
        BusyUsers = await _busyUserService.GetBusyUsersAsync(
            Date,
            Type.Start,
            Type.End,
            excludeShiftTypeId: ShiftTypeId // Exclude current shift type from busy check
        );

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await OnGetAsync(); // reload context for Instance
        _logger.LogInformation("Attempt add user {UserId} to shiftInstance {InstanceId}", SelectedUserId, Instance.Id);

        if (SelectedUserId is null) { Error = _localizer["Error_SelectUser"].Value; return Page(); }

        // Validate user belongs to the shift's company
        var selectedUser = await _db.Users.FindAsync(SelectedUserId.Value);
        if (selectedUser == null)
        {
            Error = _localizer["Error_SelectedUserNotFound"].Value;
            return Page();
        }

        if (selectedUser.CompanyId != Instance.CompanyId)
        {
            _logger.LogWarning("Attempted cross-company assignment: User {UserId} (Company {UserCompanyId}) to Shift (Company {ShiftCompanyId})",
                SelectedUserId.Value, selectedUser.CompanyId, Instance.CompanyId);
            Error = _localizer["Error_CannotAssignCrossCompany"].Value;
            return Page();
        }

        int assigned = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .CountAsync(a => a.ShiftInstanceId == Instance.Id);
        if (assigned >= Instance.StaffingRequired)
        {
            Error = _localizer["Error_CannotOverAssign"].Value;
            return Page();
        }

        var validation = await _assignmentService.ValidateShiftAssignmentAsync(SelectedUserId.Value, Instance.Id);
        if (!validation.CanAssign)
        {
            _logger.LogWarning("Validation blocked add user {UserId} to shiftInstance {InstanceId}: {Errors}",
                       SelectedUserId, Instance.Id, string.Join("; ", validation.Errors.Select(e => e.Message)));
            Error = string.Join(" ", validation.Errors.Select(e => e.Message));
            return Page();
        }
        // Show overrideable warnings (for now, log them — UI override flow to be added later)
        if (validation.Warnings.Count > 0)
        {
            _logger.LogInformation("Validation warnings for user {UserId} to shiftInstance {InstanceId}: {Warnings}",
                       SelectedUserId, Instance.Id, string.Join("; ", validation.Warnings.Select(w => w.Message)));
        }

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            ShiftInstanceId = Instance.Id,
            UserId = SelectedUserId.Value,
            CompanyId = Instance.CompanyId
        });

        // Set shift name if provided and this is the first assignment
        // Also check for pending shift name from localStorage (from modal creation)
        if (assigned == 0)
        {
            if (!string.IsNullOrWhiteSpace(ShiftName))
            {
                Instance.Name = ShiftName.Trim();
            }
            // Note: Frontend will handle localStorage-based shift names via JavaScript
        }

        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftAssignment");
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return Page();
            }
        }

        // Send notification to the assigned user
        await _notificationService.CreateShiftAddedNotificationAsync(
            SelectedUserId.Value,
            Type!.Name,
            Date,
            Type.Start,
            Type.End
        );

        await _auditLogService.LogAsync("ShiftAssignmentManaged", "ShiftAssignment", Instance.Id,
            $"Assigned user {SelectedUserId.Value} to shift '{Type.Name}' on {Date:yyyy-MM-dd}");

        return RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId, returnUrl = ReturnUrl });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int assignmentId)
    {
        var a = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
            .ThenInclude(si => si.ShiftType)
            .FirstOrDefaultAsync(sa => sa.Id == assignmentId);

        if (a != null)
        {
            // SECURITY FIX (HIGH-001): Validate company scope to prevent IDOR
            var removeUserIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(removeUserIdClaim, out var removeCurrentUserId))
            {
                var removeCurrentUser = await _db.Users.FindAsync(removeCurrentUserId);
                if (removeCurrentUser != null)
                {
                    var isAdmin = await _grantService.HasGrantAsync(removeCurrentUserId, "AdminAccess");
                    if (!isAdmin)
                    {
                        var removeHasAccess = await _directorService.IsDirectorOfAsync(a.CompanyId) || removeCurrentUser.CompanyId == a.CompanyId;
                        if (!removeHasAccess)
                        {
                            _logger.LogWarning("User {UserId} attempted to remove assignment {AssignmentId} from company {CompanyId} without access",
                                removeCurrentUserId, assignmentId, a.CompanyId);
                            return RedirectToPage("/AccessDenied");
                        }
                    }
                }
            }

            _logger.LogInformation("Removing assignment {AssignmentId} from shiftInstance {InstanceId}", assignmentId, a.ShiftInstanceId);

            // Send notification before removing (only if user is assigned)
            if (a.UserId.HasValue)
            {
                await _notificationService.CreateShiftRemovedNotificationAsync(
                    a.UserId.Value,
                    a.ShiftInstance.ShiftType.Name,
                    a.ShiftInstance.WorkDate,
                    a.ShiftInstance.ShiftType.Start,
                    a.ShiftInstance.ShiftType.End
                );
            }

            _db.ShiftAssignments.Remove(a);
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "ShiftAssignment", assignmentId);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId });
                }
            }
            _logger.LogInformation("Successfully removed assignment {AssignmentId}", assignmentId);
        }
        else
        {
            _logger.LogWarning("Assignment {AssignmentId} not found for removal", assignmentId);
        }

        return !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? Redirect(ReturnUrl) : RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId });
    }

    public async Task<IActionResult> OnPostAssignTraineeAsync(int assignmentId, int traineeUserId)
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId, returnUrl = ReturnUrl });
        }

        // SECURITY FIX (HIGH-002): Validate company scope to prevent IDOR
        var traineeAssignment = await _db.ShiftAssignments.AsNoTracking().FirstOrDefaultAsync(sa => sa.Id == assignmentId);
        if (traineeAssignment != null)
        {
            var traineeCurrentUser = await _db.Users.FindAsync(currentUserId);
            if (traineeCurrentUser != null)
            {
                var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
                if (!isAdmin)
                {
                    var traineeHasAccess = await _directorService.IsDirectorOfAsync(traineeAssignment.CompanyId) || traineeCurrentUser.CompanyId == traineeAssignment.CompanyId;
                    if (!traineeHasAccess)
                    {
                        _logger.LogWarning("User {UserId} attempted trainee assignment on company {CompanyId} without access",
                            currentUserId, traineeAssignment.CompanyId);
                        return RedirectToPage("/AccessDenied");
                    }
                }
            }
        }

        var success = await _traineeService.AssignTraineeToShiftAsync(assignmentId, traineeUserId, currentUserId);

        if (!success)
        {
            TempData["ErrorMessage"] = _localizer["Error_FailedToAssignTrainee"].Value;
        }
        else
        {
            TempData["SuccessMessage"] = _localizer["Success_TraineeAssigned"].Value;
        }

        return RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId, returnUrl = ReturnUrl });
    }

    public async Task<IActionResult> OnPostRemoveTraineeAsync(int assignmentId)
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId, returnUrl = ReturnUrl });
        }

        // SECURITY FIX (HIGH-002): Validate company scope to prevent IDOR
        var removeTraineeAssignment = await _db.ShiftAssignments.AsNoTracking().FirstOrDefaultAsync(sa => sa.Id == assignmentId);
        if (removeTraineeAssignment != null)
        {
            var removeTraineeCurrentUser = await _db.Users.FindAsync(currentUserId);
            if (removeTraineeCurrentUser != null)
            {
                var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
                if (!isAdmin)
                {
                    var removeTraineeHasAccess = await _directorService.IsDirectorOfAsync(removeTraineeAssignment.CompanyId) || removeTraineeCurrentUser.CompanyId == removeTraineeAssignment.CompanyId;
                    if (!removeTraineeHasAccess)
                    {
                        _logger.LogWarning("User {UserId} attempted trainee removal on company {CompanyId} without access",
                            currentUserId, removeTraineeAssignment.CompanyId);
                        return RedirectToPage("/AccessDenied");
                    }
                }
            }
        }

        var success = await _traineeService.RemoveTraineeFromShiftAsync(assignmentId, "Manual", currentUserId);

        if (!success)
        {
            TempData["ErrorMessage"] = _localizer["Error_FailedToRemoveTrainee"].Value;
        }
        else
        {
            TempData["SuccessMessage"] = _localizer["Success_TraineeRemoved"].Value;
        }

        return RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId, returnUrl = ReturnUrl });
    }
}
