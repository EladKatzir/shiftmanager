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
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class UsersModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<UsersModel> _logger;
    private readonly ICompanyContext _companyContext;
    private readonly IDirectorService _directorService;
    private readonly ITraineeService _traineeService;
    private readonly IAuditLogService _auditLogService;
    private readonly IMailService _mailService;

    public UsersModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<UsersModel> logger,
        ICompanyContext companyContext,
        IDirectorService directorService,
        ITraineeService traineeService,
        IAuditLogService auditLogService,
        IMailService mailService)
        : base(localizer)
    {
        _db = db;
        _logger = logger;
        _companyContext = companyContext;
        _directorService = directorService;
        _traineeService = traineeService;
        _auditLogService = auditLogService;
        _mailService = mailService;
    }

    public record UserVM(int Id, string DisplayName, string Email, string CompanyName, string Role, bool IsActive);
    public record JoinRequestVM(int Id, string Email, string DisplayName, string CompanyName, string RequestedRole, DateTime CreatedAt, JoinRequestStatus Status);

    // Batch approval support
    public class BatchApprovalItem
    {
        public int RequestId { get; set; }
        public UserRole AssignedRole { get; set; }
    }

    public List<UserVM> Users { get; set; } = new();
    public List<JoinRequestVM> JoinRequests { get; set; } = new();
    public List<Company> AvailableCompanies { get; set; } = new();

    // Pagination properties
    [BindProperty(SupportsGet = true)]
    public int CurrentPage { get; set; } = 1;

    public int PageSize { get; set; } = 50;
    public int TotalUsers { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalUsers / (double)PageSize);

    [BindProperty(SupportsGet = true)]
    public int JoinRequestsPage { get; set; } = 1;

    public int JoinRequestsPageSize { get; set; } = 50;
    public int TotalJoinRequests { get; set; }
    public int TotalJoinRequestsPages => (int)Math.Ceiling(TotalJoinRequests / (double)JoinRequestsPageSize);

    // Expose assignable roles for UI filtering
    public List<UserRole> AssignableRoles
    {
        get
        {
            var roles = new List<UserRole>();
            if (_directorService.CanAssignRole(UserRole.Employee)) roles.Add(UserRole.Employee);
            if (_directorService.CanAssignRole(UserRole.Manager)) roles.Add(UserRole.Manager);
            if (_directorService.CanAssignRole(UserRole.Director)) roles.Add(UserRole.Director);
            if (_directorService.CanAssignRole(UserRole.Owner)) roles.Add(UserRole.Owner);
            if (_directorService.CanAssignRole(UserRole.Trainee)) roles.Add(UserRole.Trainee);
            // ✅ PHASE 18: Add Assigner role to assignable roles
            if (_directorService.CanAssignRole(UserRole.Assigner)) roles.Add(UserRole.Assigner);
            return roles;
        }
    }

    // Filter parameters for join requests
    [BindProperty(SupportsGet = true)]
    public JoinRequestStatus FilterStatus { get; set; } = JoinRequestStatus.Pending;

    [BindProperty(SupportsGet = true)]
    public int? FilterCompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public UserRole? FilterRole { get; set; }

    // Filter parameters for existing users
    [BindProperty(SupportsGet = true)]
    public int? UserFilterCompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public UserRole? UserFilterRole { get; set; }

    [BindProperty, EmailAddress] public string NewEmail { get; set; } = string.Empty;
    [BindProperty] public string NewDisplayName { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;
    [BindProperty] public string NewRole { get; set; } = "Employee";

    // Batch approval properties
    [BindProperty]
    public List<int> SelectedRequests { get; set; } = new();

    public Dictionary<int, UserRole> RequestRoles { get; set; } = new();

    public async Task OnGetAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            return;
        }

        var currentUser = await _db.Users.FindAsync(currentUserId);
        if (currentUser == null)
        {
            _logger.LogError("User {UserId} not found in database", currentUserId);
            return;
        }

        var role = currentUser.Role;

        // Determine accessible company IDs based on role
        List<int> accessibleCompanyIds;

        if (role == UserRole.Owner)
        {
            // Owner: all companies
            accessibleCompanyIds = await _db.Companies.Select(c => c.Id).ToListAsync();
        }
        else if (role == UserRole.Director)
        {
            // Director: companies they direct
            accessibleCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUserId);
        }
        else if (role == UserRole.Manager)
        {
            // Manager: their company only
            accessibleCompanyIds = new List<int> { currentUser.CompanyId };
        }
        else
        {
            // Employee: no access (shouldn't reach here due to authorization, but just in case)
            accessibleCompanyIds = new List<int>();
        }

        // Load join requests with filters and scoping
        var joinRequestsQuery = _db.UserJoinRequests
            .AsNoTracking()
            .Where(jr => accessibleCompanyIds.Contains(jr.CompanyId))
            .Where(jr => jr.Status == FilterStatus);

        if (FilterCompanyId.HasValue)
        {
            joinRequestsQuery = joinRequestsQuery.Where(jr => jr.CompanyId == FilterCompanyId.Value);
        }

        if (FilterRole.HasValue)
        {
            joinRequestsQuery = joinRequestsQuery.Where(jr => jr.RequestedRole == FilterRole.Value);
        }

        // Get total count for pagination
        TotalJoinRequests = await joinRequestsQuery.CountAsync();

        // Apply pagination
        var joinRequestData = await joinRequestsQuery
            .OrderBy(jr => jr.CreatedAt)
            .Skip((JoinRequestsPage - 1) * JoinRequestsPageSize)
            .Take(JoinRequestsPageSize)
            .ToListAsync();

        // Load companies for join requests
        var companyIds = joinRequestData.Select(jr => jr.CompanyId).Distinct().ToList();
        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        JoinRequests = joinRequestData
            .Select(jr => new JoinRequestVM(
                jr.Id,
                jr.Email,
                jr.DisplayName,
                companies[jr.CompanyId].Name,
                jr.RequestedRole.ToString(),
                jr.CreatedAt,
                jr.Status
            ))
            .ToList();

        // Load available companies for filter dropdown
        AvailableCompanies = await _db.Companies
            .Where(c => accessibleCompanyIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Load existing users with filters
        var usersQuery = _db.Users
            .AsNoTracking()
            .Where(u => accessibleCompanyIds.Contains(u.CompanyId));

        // Apply role filter first (before handling Directors specially)
        if (UserFilterRole.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.Role == UserFilterRole.Value);
        }

        var userData = await usersQuery.ToListAsync();

        // Load companies for users
        var userCompanyIds = userData.Select(u => u.CompanyId).Distinct().ToList();
        var userCompanies = await _db.Companies
            .AsNoTracking()
            .Where(c => userCompanyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        // Build user list, handling Directors specially
        var userList = new List<UserVM>();

        foreach (var u in userData)
        {
            if (u.Role == UserRole.Director)
            {
                // For Directors, get all companies they manage
                var directorCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(u.Id);

                // Filter by accessible companies
                var managedCompanyIds = directorCompanyIds.Where(id => accessibleCompanyIds.Contains(id)).ToList();

                // Apply company filter if specified
                if (UserFilterCompanyId.HasValue)
                {
                    managedCompanyIds = managedCompanyIds.Where(id => id == UserFilterCompanyId.Value).ToList();
                }

                // Load company names for managed companies
                var managedCompanies = await _db.Companies
                    .AsNoTracking()
                    .Where(c => managedCompanyIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id);

                // Create one entry per managed company
                foreach (var companyId in managedCompanyIds)
                {
                    userList.Add(new UserVM(
                        u.Id,
                        u.DisplayName,
                        u.Email,
                        managedCompanies[companyId].Name,
                        u.Role.ToString(),
                        u.IsActive
                    ));
                }
            }
            else
            {
                // For non-Directors, use their primary company
                // Apply company filter if specified
                if (!UserFilterCompanyId.HasValue || u.CompanyId == UserFilterCompanyId.Value)
                {
                    userList.Add(new UserVM(
                        u.Id,
                        u.DisplayName,
                        u.Email,
                        userCompanies[u.CompanyId].Name,
                        u.Role.ToString(),
                        u.IsActive
                    ));
                }
            }
        }

        // Get total count for pagination
        TotalUsers = userList.Count;

        // Apply pagination
        Users = userList
            .OrderBy(u => u.CompanyName)
            .ThenBy(u => u.DisplayName)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        await OnGetAsync();

        // ✅ SECURITY FIX: Input validation
        if (string.IsNullOrWhiteSpace(NewEmail) || string.IsNullOrWhiteSpace(NewDisplayName) || string.IsNullOrWhiteSpace(NewPassword))
        { Error = _localizer["Error_AllFieldsRequired"]; return Page(); }

        // Length validation to prevent DoS and database errors
        if (NewEmail.Length > 255)
        { Error = _localizer["Error_EmailTooLong"]; return Page(); }

        if (NewDisplayName.Length > 200)
        { Error = _localizer["Error_DisplayNameTooLong"]; return Page(); }

        if (NewPassword.Length < 6)
        { Error = _localizer["Error_PasswordTooShort"]; return Page(); }

        if (NewPassword.Length > 128)
        { Error = _localizer["Error_PasswordTooLong"]; return Page(); }

        // Basic email format validation
        if (!NewEmail.Contains('@') || NewEmail.Length < 3)
        { Error = _localizer["Error_InvalidEmailFormat"]; return Page(); }

        if (await _db.Users.AnyAsync(u => u.Email == NewEmail)) { Error = _localizer["Error_EmailAlreadyExists"]; return Page(); }

        // Validate role string and permission to assign
        if (!Enum.TryParse<UserRole>(NewRole, ignoreCase: true, out var targetRole))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRole"];
            return RedirectToPage();
        }

        if (!_directorService.CanAssignRole(targetRole))
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionAssignRole"], targetRole);
            return RedirectToPage();
        }

        var companyId = _companyContext.GetCompanyIdOrThrow();
        var (h, s) = PasswordHasher.CreateHash(NewPassword);
        var newUser = new AppUser
        {
            CompanyId = companyId,
            Email = NewEmail,
            DisplayName = NewDisplayName,
            Role = targetRole,
            IsActive = true,
            PasswordHash = h,
            PasswordSalt = s
        };
        _db.Users.Add(newUser);
        await _db.SaveChangesAsync();

        // Audit logging (RoleAssignmentAudit)
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim during user creation audit");
            currentUserId = 0; // Fallback for audit trail
        }
        _db.RoleAssignmentAudits.Add(new RoleAssignmentAudit
        {
            ChangedBy = currentUserId,
            TargetUserId = newUser.Id,
            FromRole = null,
            ToRole = targetRole,
            CompanyId = companyId,
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Audit logging (general audit log)
        await _auditLogService.LogAsync(
            "UserCreated",
            "User",
            newUser.Id,
            $"Created new user '{newUser.DisplayName}' ({newUser.Email}) with role {targetRole}");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_UserCreated"], NewDisplayName, targetRole);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"];
            return RedirectToPage();
        }

        var u = await _db.Users.FindAsync(id);
        if (u != null)
        {
            // Check if current user has permission to modify this user
            if (!CanModifyUser(u.Role))
            {
                TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionModifyUser"], u.Role);
                return RedirectToPage();
            }

            u.IsActive = !u.IsActive;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRoleAsync(int id, string role)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"];
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(role) || role.Length > 50)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRole"];
            return RedirectToPage();
        }

        // Validate role string and permission to assign
        if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var targetRole))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRole"];
            return RedirectToPage();
        }

        if (!_directorService.CanAssignRole(targetRole))
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionAssignRole"], targetRole);
            return RedirectToPage();
        }

        var u = await _db.Users.FindAsync(id);
        if (u != null)
        {
            var oldRole = u.Role;

            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"];
                return RedirectToPage();
            }

            // If changing from Trainee to another role, validate and cancel shadowing
            if (oldRole == UserRole.Trainee && targetRole != UserRole.Trainee)
            {
                // Check if trainee has active shadowing assignments
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var activeShadowingCount = await _db.ShiftAssignments
                    .Include(sa => sa.ShiftInstance)
                    .Where(sa => sa.TraineeUserId == id && sa.ShiftInstance.WorkDate >= today)
                    .CountAsync();

                if (activeShadowingCount > 0)
                {
                    // Cancel all shadowing assignments
                    var canceledCount = await _traineeService.CancelAllShadowingAssignmentsAsync(id, "RoleChanged", currentUserId);

                    _logger.LogInformation("Canceled {Count} shadowing assignments for user {UserId} due to role change from {OldRole} to {NewRole}",
                        canceledCount, id, oldRole, targetRole);
                }
            }

            // If changing to Trainee from another role, check if they have active shifts as primary employee
            if (oldRole != UserRole.Trainee && targetRole == UserRole.Trainee)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var activeShiftsCount = await _db.ShiftAssignments
                    .Include(sa => sa.ShiftInstance)
                    .Where(sa => sa.UserId == id && sa.ShiftInstance.WorkDate >= today)
                    .CountAsync();

                if (activeShiftsCount > 0)
                {
                    TempData["ErrorMessage"] = string.Format(_localizer["Error_CannotChangeToTrainee_ActiveShifts"], activeShiftsCount);
                    return RedirectToPage();
                }

                // Check if they have trainees shadowing them
                var traineeShadowingCount = await _db.ShiftAssignments
                    .Include(sa => sa.ShiftInstance)
                    .Where(sa => sa.UserId == id && sa.TraineeUserId != null && sa.ShiftInstance.WorkDate >= today)
                    .CountAsync();

                if (traineeShadowingCount > 0)
                {
                    TempData["ErrorMessage"] = string.Format(_localizer["Error_CannotChangeToTrainee_ShadowingTrainees"], traineeShadowingCount);
                    return RedirectToPage();
                }
            }

            u.Role = targetRole;
            await _db.SaveChangesAsync();

            // Audit logging
            _db.RoleAssignmentAudits.Add(new RoleAssignmentAudit
            {
                ChangedBy = currentUserId,
                TargetUserId = u.Id,
                FromRole = oldRole,
                ToRole = targetRole,
                CompanyId = u.CompanyId,
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = string.Format(_localizer["Success_RoleUpdated"], targetRole, u.DisplayName);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int id, string newPassword)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"];
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            TempData["ErrorMessage"] = _localizer["Error_PasswordRequired"];
            return RedirectToPage();
        }

        if (newPassword.Length < 6)
        {
            TempData["ErrorMessage"] = "Password must be at least 6 characters.";
            return RedirectToPage();
        }

        if (newPassword.Length > 128)
        {
            TempData["ErrorMessage"] = "Password must not exceed 128 characters.";
            return RedirectToPage();
        }

        var u = await _db.Users.FindAsync(id);
        if (u != null)
        {
            // Check if current user has permission to modify this user
            if (!CanModifyUser(u.Role))
            {
                TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionResetPassword"], u.Role);
                return RedirectToPage();
            }

            var (h, s) = PasswordHasher.CreateHash(newPassword);
            u.PasswordHash = h; u.PasswordSalt = s;
            await _db.SaveChangesAsync();

            // Log the password reset for security audit
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim during password reset audit");
                currentUserId = 0; // Fallback for audit trail
            }
            await _auditLogService.LogUserActionAsync(
                userId: currentUserId,
                action: "PasswordReset",
                entityType: "User",
                entityId: u.Id,
                description: $"Password reset for user {u.DisplayName} ({u.Email})"
            );

            TempData["SuccessMessage"] = string.Format(_localizer["Success_PasswordUpdated"], u.DisplayName);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(int id)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"];
                return RedirectToPage();
            }

            // Prevent self-deletion
            if (id == currentUserId)
            {
                _logger.LogWarning("User {CurrentUserId} attempted to delete themselves", currentUserId);
                Error = _localizer["Error_CannotDeleteOwnAccount"];
                await OnGetAsync();
                return Page();
            }

            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found for deletion", id);
                Error = _localizer["Error_UserNotFound"];
                await OnGetAsync();
                return Page();
            }

            var companyId = _companyContext.GetCompanyIdOrThrow();
            if (user.CompanyId != companyId)
            {
                _logger.LogWarning("User {CurrentUserId} attempted to delete user {TargetUserId} from different company", currentUserId, id);
                Error = _localizer["Error_CanOnlyDeleteOwnCompanyUsers"];
                await OnGetAsync();
                return Page();
            }

            // Check if current user has permission to delete this user based on role hierarchy
            if (!CanModifyUser(user.Role))
            {
                _logger.LogWarning("User {CurrentUserId} attempted to delete user {TargetUserId} with higher role {TargetRole}",
                    currentUserId, id, user.Role);
                TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionDeleteUser"], user.Role);
                return RedirectToPage();
            }

            _logger.LogInformation("Starting deletion of user {UserId} ({UserName}) by admin {CurrentUserId}", id, user.DisplayName, currentUserId);

            // Get count of shift assignments for logging before deletion
            var shiftAssignmentCount = await _db.ShiftAssignments.Where(sa => sa.UserId == id).CountAsync();

            // Get shift assignment IDs for swap request deletion
            var userAssignmentIds = await _db.ShiftAssignments
                .Where(sa => sa.UserId == id)
                .Select(sa => sa.Id)
                .ToListAsync();

            // 1. Delete all swap requests (both from and to this user)
            // Must do this first before deleting shift assignments due to foreign key
            var swapRequestsCount = await _db.SwapRequests
                .Where(sr => userAssignmentIds.Contains(sr.FromAssignmentId) || sr.ToUserId == id)
                .CountAsync();

            if (swapRequestsCount > 0)
            {
                _logger.LogInformation("Deleting {Count} swap requests related to user {UserId}", swapRequestsCount, id);
                await _db.SwapRequests
                    .Where(sr => userAssignmentIds.Contains(sr.FromAssignmentId) || sr.ToUserId == id)
                    .ExecuteDeleteAsync();
            }

            // 2. Remove all shift assignments using ExecuteDeleteAsync for better performance
            if (shiftAssignmentCount > 0)
            {
                _logger.LogInformation("Removing {Count} shift assignments for user {UserId}", shiftAssignmentCount, id);
                await _db.ShiftAssignments.Where(sa => sa.UserId == id).ExecuteDeleteAsync();
            }

            // 3. Delete all time-off requests
            var timeOffRequestCount = await _db.TimeOffRequests.Where(tor => tor.UserId == id).CountAsync();
            if (timeOffRequestCount > 0)
            {
                _logger.LogInformation("Deleting {Count} time-off requests for user {UserId}", timeOffRequestCount, id);
                await _db.TimeOffRequests.Where(tor => tor.UserId == id).ExecuteDeleteAsync();
            }

            // 4. Delete the user
            _logger.LogInformation("Deleting user {UserId} ({UserName})", id, user.DisplayName);
            _db.Users.Remove(user);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Successfully deleted user {UserId} ({UserName}) and cleaned up all related data", id, user.DisplayName);

            // Use TempData to show success message after redirect
            TempData["SuccessMessage"] = string.Format(_localizer["Success_UserDeleted"], user.DisplayName);

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            Error = _localizer["Error_DeletingUser"];
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostApproveJoinRequestAsync(int id)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRequestId"];
            return RedirectToPage();
        }

        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            TempData["ErrorMessage"] = "Invalid user claim. Please log in again.";
            return RedirectToPage();
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        var joinRequest = await _db.UserJoinRequests
            .Include(jr => jr.Company)
            .FirstOrDefaultAsync(jr => jr.Id == id);

        if (joinRequest == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JoinRequestNotFound"];
            return RedirectToPage();
        }

        // Verify user has permission to approve this request
        var hasPermission = false;
        if (currentUser!.Role == UserRole.Owner)
        {
            hasPermission = true;
        }
        else if (currentUser.Role == UserRole.Director)
        {
            var directorCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUserId);
            hasPermission = directorCompanyIds.Contains(joinRequest.CompanyId);
        }
        else if (currentUser.Role == UserRole.Manager)
        {
            hasPermission = currentUser.CompanyId == joinRequest.CompanyId;
        }

        if (!hasPermission)
        {
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionApproveJoinRequest"];
            return RedirectToPage();
        }

        if (joinRequest.Status != JoinRequestStatus.Pending)
        {
            TempData["ErrorMessage"] = _localizer["Error_RequestAlreadyReviewed"];
            return RedirectToPage();
        }

        // Check if user with this email already exists
        if (await _db.Users.AnyAsync(u => u.Email == joinRequest.Email))
        {
            TempData["ErrorMessage"] = _localizer["Error_UserEmailAlreadyExists"];
            return RedirectToPage();
        }

        // Validate permission to assign the requested role
        if (!_directorService.CanAssignRole(joinRequest.RequestedRole))
        {
            TempData["ErrorMessage"] = $"You do not have permission to assign the {joinRequest.RequestedRole} role.";
            return RedirectToPage();
        }

        // Create the user account
        var newUser = new AppUser
        {
            Email = joinRequest.Email,
            DisplayName = joinRequest.DisplayName,
            PasswordHash = joinRequest.PasswordHash,
            PasswordSalt = joinRequest.PasswordSalt,
            CompanyId = joinRequest.CompanyId,
            Role = joinRequest.RequestedRole,
            IsActive = true
        };

        _db.Users.Add(newUser);

        // Update join request status
        joinRequest.Status = JoinRequestStatus.Approved;
        joinRequest.ReviewedBy = currentUserId;
        joinRequest.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Link the created user to the join request
        joinRequest.CreatedUserId = newUser.Id;
        await _db.SaveChangesAsync();

        // Send account approval email notification
        _ = _mailService.SendAccountApprovedEmailAsync(
            newUser.Email,
            newUser.DisplayName,
            newUser.Role.ToString(),
            joinRequest.Company?.Name ?? "the company"
        );

        _logger.LogInformation("Join request {RequestId} approved by {ApproverId}. Created user {UserId} ({Email}) for company {CompanyId}",
            id, currentUserId, newUser.Id, newUser.Email, joinRequest.CompanyId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_JoinRequestApproved"], joinRequest.DisplayName, joinRequest.Email, joinRequest.RequestedRole, joinRequest.Company?.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectJoinRequestAsync(int id, string? reason)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRequestId"];
            return RedirectToPage();
        }

        if (!string.IsNullOrWhiteSpace(reason) && reason.Length > 1000)
        {
            TempData["ErrorMessage"] = _localizer["Error_RejectionReasonTooLong"];
            return RedirectToPage();
        }

        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            TempData["ErrorMessage"] = "Invalid user claim. Please log in again.";
            return RedirectToPage();
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        var joinRequest = await _db.UserJoinRequests
            .Include(jr => jr.Company)
            .FirstOrDefaultAsync(jr => jr.Id == id);

        if (joinRequest == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JoinRequestNotFound"];
            return RedirectToPage();
        }

        // Verify user has permission to reject this request
        var hasPermission = false;
        if (currentUser!.Role == UserRole.Owner)
        {
            hasPermission = true;
        }
        else if (currentUser.Role == UserRole.Director)
        {
            var directorCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUserId);
            hasPermission = directorCompanyIds.Contains(joinRequest.CompanyId);
        }
        else if (currentUser.Role == UserRole.Manager)
        {
            hasPermission = currentUser.CompanyId == joinRequest.CompanyId;
        }

        if (!hasPermission)
        {
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionRejectRequest"];
            return RedirectToPage();
        }

        if (joinRequest.Status != JoinRequestStatus.Pending)
        {
            TempData["ErrorMessage"] = _localizer["Error_RequestAlreadyReviewed"];
            return RedirectToPage();
        }

        // Update join request status
        joinRequest.Status = JoinRequestStatus.Rejected;
        joinRequest.ReviewedBy = currentUserId;
        joinRequest.ReviewedAt = DateTime.UtcNow;
        joinRequest.RejectionReason = reason;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Join request {RequestId} rejected by {ReviewerId}. Email: {Email}, Company: {CompanyId}",
            id, currentUserId, joinRequest.Email, joinRequest.CompanyId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_JoinRequestRejected"], joinRequest.DisplayName, joinRequest.Email);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBatchApproveJoinRequestsAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            TempData["ErrorMessage"] = "Invalid user claim. Please log in again.";
            return RedirectToPage();
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        if (SelectedRequests == null || !SelectedRequests.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_NoRequestsSelected"];
            return RedirectToPage();
        }

        // Manually bind RequestRoles dictionary from form data
        RequestRoles = new Dictionary<int, UserRole>();
        foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("RequestRoles[")))
        {
            // Extract the ID from "RequestRoles[123]"
            var idString = key.Substring(13, key.Length - 14); // Remove "RequestRoles[" and "]"
            if (int.TryParse(idString, out var requestId) &&
                int.TryParse(Request.Form[key].ToString(), out var roleInt) &&
                Enum.IsDefined(typeof(UserRole), roleInt))
            {
                RequestRoles[requestId] = (UserRole)roleInt;
            }
        }

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var approvedCount = 0;
            var skippedCount = 0;
            var errors = new List<string>();

            // ✅ SECURITY FIX (DEFECT-019): Validate request IDs before processing
            // Get all selected join requests
            var joinRequests = await _db.UserJoinRequests
                .Include(jr => jr.Company)
                .Where(jr => SelectedRequests.Contains(jr.Id))
                .ToListAsync();

            // Check if any selected IDs were not found (potential tampering)
            var foundIds = joinRequests.Select(jr => jr.Id).ToHashSet();
            var invalidIds = SelectedRequests.Where(id => !foundIds.Contains(id)).ToList();
            if (invalidIds.Any())
            {
                _logger.LogWarning("SECURITY: User {UserId} submitted invalid join request IDs: {InvalidIds}",
                    currentUserId, string.Join(", ", invalidIds));
            }

            // Get accessible company IDs for permission check
            List<int> accessibleCompanyIds;
            if (currentUser!.Role == UserRole.Owner)
            {
                accessibleCompanyIds = await _db.Companies.Select(c => c.Id).ToListAsync();
            }
            else if (currentUser.Role == UserRole.Director)
            {
                accessibleCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUserId);
            }
            else if (currentUser.Role == UserRole.Manager)
            {
                accessibleCompanyIds = new List<int> { currentUser.CompanyId };
            }
            else
            {
                TempData["ErrorMessage"] = _localizer["Error_NoPermissionApproveRequests"];
                return RedirectToPage();
            }

            foreach (var joinRequest in joinRequests)
            {
                // ✅ SECURITY FIX (DEFECT-019): Check permission for this specific request
                if (!accessibleCompanyIds.Contains(joinRequest.CompanyId))
                {
                    _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to approve join request {RequestId} for unauthorized company {CompanyId}",
                        currentUserId, currentUser.Role, joinRequest.Id, joinRequest.CompanyId);
                    errors.Add(string.Format(_localizer["Error_NoPermissionDifferentCompany"], joinRequest.DisplayName));
                    skippedCount++;
                    continue;
                }

                // Check if already reviewed
                if (joinRequest.Status != JoinRequestStatus.Pending)
                {
                    errors.Add(string.Format(_localizer["Error_AlreadyReviewed"], joinRequest.DisplayName));
                    skippedCount++;
                    continue;
                }

                // Check if user already exists
                if (await _db.Users.AnyAsync(u => u.Email == joinRequest.Email))
                {
                    errors.Add(string.Format(_localizer["Error_UserWithEmailExists"], joinRequest.Email));
                    skippedCount++;
                    continue;
                }

                // Get assigned role from form (default to requested role if not specified)
                var assignedRole = RequestRoles.ContainsKey(joinRequest.Id)
                    ? RequestRoles[joinRequest.Id]
                    : joinRequest.RequestedRole;

                // Validate permission to assign the role
                if (!_directorService.CanAssignRole(assignedRole))
                {
                    errors.Add(string.Format(_localizer["Error_NoPermissionAssignRoleTo"], assignedRole, joinRequest.DisplayName));
                    skippedCount++;
                    continue;
                }

                // Create the user account
                var newUser = new AppUser
                {
                    Email = joinRequest.Email,
                    DisplayName = joinRequest.DisplayName,
                    PasswordHash = joinRequest.PasswordHash,
                    PasswordSalt = joinRequest.PasswordSalt,
                    CompanyId = joinRequest.CompanyId,
                    Role = assignedRole,
                    IsActive = true
                };

                _db.Users.Add(newUser);

                // Update join request status
                joinRequest.Status = JoinRequestStatus.Approved;
                joinRequest.ReviewedBy = currentUserId;
                joinRequest.ReviewedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(); // Save to get newUser.Id

                // Link the created user to the join request
                joinRequest.CreatedUserId = newUser.Id;

                _logger.LogInformation(
                    "Batch approval: Join request {RequestId} approved by {ApproverId}. Created user {UserId} ({Email}) with role {Role} for company {CompanyId}",
                    joinRequest.Id, currentUserId, newUser.Id, newUser.Email, assignedRole, joinRequest.CompanyId);

                // Log to audit log
                await _auditLogService.LogUserActionAsync(
                    userId: currentUserId,
                    action: "BatchApproveJoinRequest",
                    entityType: "UserJoinRequest",
                    entityId: joinRequest.Id,
                    description: $"Approved join request for {newUser.DisplayName} ({newUser.Email}) with role {assignedRole}"
                );

                approvedCount++;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Build success message
            var successMessage = string.Format(_localizer["Success_ApprovedCount"], approvedCount);
            if (skippedCount > 0)
            {
                successMessage += " " + string.Format(_localizer["Success_SkippedCount"], skippedCount);
            }

            TempData["SuccessMessage"] = successMessage;

            if (errors.Any())
            {
                TempData["ErrorMessage"] = _localizer["Error_SomeRequestsHadIssues"] + ": " + string.Join("; ", errors.Take(3));
            }

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error during batch approval of join requests");
            TempData["ErrorMessage"] = _localizer["Error_BatchApprovalFailed"];
            return RedirectToPage();
        }
    }

    /// <summary>
    /// Check if current user has permission to modify a user with the specified role.
    /// Uses the same role hierarchy as CanAssignRole.
    /// </summary>
    private bool CanModifyUser(UserRole targetUserRole)
    {
        var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;

        if (string.IsNullOrEmpty(currentUserRole))
            return false;

        // Owner can modify anyone
        if (currentUserRole == nameof(UserRole.Owner))
            return true;

        // ✅ PHASE 18: Director can modify Employee, Manager, Director, Trainee, Assigner (but NOT Owner)
        if (currentUserRole == nameof(UserRole.Director))
        {
            return targetUserRole == UserRole.Employee
                || targetUserRole == UserRole.Manager
                || targetUserRole == UserRole.Director
                || targetUserRole == UserRole.Trainee
                || targetUserRole == UserRole.Assigner;
        }

        // ✅ PHASE 18: Manager can modify Employee, Trainee, and Assigner (NOT Owner, Director, or other Managers)
        if (currentUserRole == nameof(UserRole.Manager))
        {
            return targetUserRole == UserRole.Employee
                || targetUserRole == UserRole.Trainee
                || targetUserRole == UserRole.Assigner;
        }

        // Employees, Trainees, and Assigners cannot modify anyone
        return false;
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"];
                return RedirectToPage();
            }

            var currentUser = await _db.Users.FindAsync(currentUserId);
            if (currentUser == null)
            {
                _logger.LogError("User {UserId} not found in database", currentUserId);
                return RedirectToPage();
            }

            var role = currentUser.Role;

            // Determine accessible company IDs based on role (same as OnGetAsync)
            List<int> accessibleCompanyIds;

            if (role == UserRole.Owner)
            {
                accessibleCompanyIds = await _db.Companies.Select(c => c.Id).ToListAsync();
            }
            else if (role == UserRole.Director)
            {
                accessibleCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUserId);
            }
            else if (role == UserRole.Manager)
            {
                accessibleCompanyIds = new List<int> { currentUser.CompanyId };
            }
            else
            {
                accessibleCompanyIds = new List<int>();
            }

            // Load ALL users (without pagination) respecting filters
            var usersQuery = _db.Users
                .AsNoTracking()
                .Where(u => accessibleCompanyIds.Contains(u.CompanyId));

            // Apply filters (same as OnGetAsync)
            if (UserFilterRole.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.Role == UserFilterRole.Value);
            }

            if (UserFilterCompanyId.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.CompanyId == UserFilterCompanyId.Value);
            }

            var users = await usersQuery
                .OrderBy(u => u.CompanyId)
                .ThenBy(u => u.DisplayName)
                .ToListAsync();

            // Build CSV
            var csv = new StringBuilder();
            csv.AppendLine("display_name,email");

            foreach (var user in users)
            {
                // Proper CSV escaping: quote fields if they contain commas, quotes, or newlines
                var displayName = EscapeCsvField(user.DisplayName);
                var email = EscapeCsvField(user.Email);
                csv.AppendLine($"{displayName},{email}");
            }

            var fileName = $"Users_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting users to CSV");
            TempData["ErrorMessage"] = _localizer["Error_ExportingUsers"];
            return RedirectToPage();
        }
    }

    // Helper method for CSV field escaping
    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "\"\"";

        // If field contains comma, quote, or newline, wrap in quotes and escape internal quotes
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}
