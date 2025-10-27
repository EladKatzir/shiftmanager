using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class UsersModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<UsersModel> _logger;
    private readonly ICompanyContext _companyContext;
    private readonly IDirectorService _directorService;
    private readonly ITraineeService _traineeService;
    private readonly IAuditLogService _auditLogService;

    public UsersModel(AppDbContext db, ILogger<UsersModel> logger, ICompanyContext companyContext, IDirectorService directorService, ITraineeService traineeService, IAuditLogService auditLogService)
    {
        _db = db;
        _logger = logger;
        _companyContext = companyContext;
        _directorService = directorService;
        _traineeService = traineeService;
        _auditLogService = auditLogService;
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
    public string? Error { get; set; }

    // Batch approval properties
    [BindProperty]
    public List<int> SelectedRequests { get; set; } = new();

    public Dictionary<int, UserRole> RequestRoles { get; set; } = new();

    public async Task OnGetAsync()
    {
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var currentUser = await _db.Users.FindAsync(currentUserId);
        var role = currentUser!.Role;

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

        var joinRequestData = await joinRequestsQuery.ToListAsync();

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
            .OrderBy(jr => jr.CreatedAt)
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

        Users = userList
            .OrderBy(u => u.CompanyName)
            .ThenBy(u => u.DisplayName)
            .ToList();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        await OnGetAsync();
        if (string.IsNullOrWhiteSpace(NewEmail) || string.IsNullOrWhiteSpace(NewDisplayName) || string.IsNullOrWhiteSpace(NewPassword))
        { Error = "All fields are required."; return Page(); }

        if (await _db.Users.AnyAsync(u => u.Email == NewEmail)) { Error = "Email already exists."; return Page(); }

        // Validate role string and permission to assign
        if (!Enum.TryParse<UserRole>(NewRole, ignoreCase: true, out var targetRole))
        {
            TempData["ErrorMessage"] = "Invalid role specified.";
            return RedirectToPage();
        }

        if (!_directorService.CanAssignRole(targetRole))
        {
            TempData["ErrorMessage"] = $"You do not have permission to assign the {targetRole} role.";
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
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
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

        TempData["SuccessMessage"] = $"User {NewDisplayName} created successfully as {targetRole}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u != null)
        {
            // Check if current user has permission to modify this user
            if (!CanModifyUser(u.Role))
            {
                TempData["ErrorMessage"] = $"You do not have permission to modify users with the {u.Role} role.";
                return RedirectToPage();
            }

            u.IsActive = !u.IsActive;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRoleAsync(int id, string role)
    {
        // Validate role string and permission to assign
        if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var targetRole))
        {
            TempData["ErrorMessage"] = "Invalid role specified.";
            return RedirectToPage();
        }

        if (!_directorService.CanAssignRole(targetRole))
        {
            TempData["ErrorMessage"] = $"You do not have permission to assign the {targetRole} role.";
            return RedirectToPage();
        }

        var u = await _db.Users.FindAsync(id);
        if (u != null)
        {
            var oldRole = u.Role;

            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

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
                    TempData["ErrorMessage"] = $"Cannot change to Trainee role: user has {activeShiftsCount} active shift(s) as primary employee. Please remove these shifts first.";
                    return RedirectToPage();
                }

                // Check if they have trainees shadowing them
                var traineeShadowingCount = await _db.ShiftAssignments
                    .Include(sa => sa.ShiftInstance)
                    .Where(sa => sa.UserId == id && sa.TraineeUserId != null && sa.ShiftInstance.WorkDate >= today)
                    .CountAsync();

                if (traineeShadowingCount > 0)
                {
                    TempData["ErrorMessage"] = $"Cannot change to Trainee role: user has {traineeShadowingCount} shift(s) with trainees shadowing them. Please remove trainees first.";
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

            TempData["SuccessMessage"] = $"Role updated to {targetRole} for user {u.DisplayName}.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int id, string newPassword)
    {
        var u = await _db.Users.FindAsync(id);
        if (u != null && !string.IsNullOrWhiteSpace(newPassword))
        {
            // Check if current user has permission to modify this user
            if (!CanModifyUser(u.Role))
            {
                TempData["ErrorMessage"] = $"You do not have permission to reset passwords for users with the {u.Role} role.";
                return RedirectToPage();
            }

            var (h, s) = PasswordHasher.CreateHash(newPassword);
            u.PasswordHash = h; u.PasswordSalt = s;
            await _db.SaveChangesAsync();

            // Log the password reset for security audit
            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            await _auditLogService.LogUserActionAsync(
                userId: currentUserId,
                action: "PasswordReset",
                entityType: "User",
                entityId: u.Id,
                description: $"Password reset for user {u.DisplayName} ({u.Email})"
            );

            TempData["SuccessMessage"] = $"Password updated successfully for {u.DisplayName}.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(int id)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var currentUserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            // Prevent self-deletion
            if (id == currentUserId)
            {
                _logger.LogWarning("User {CurrentUserId} attempted to delete themselves", currentUserId);
                Error = "You cannot delete your own account.";
                await OnGetAsync();
                return Page();
            }

            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found for deletion", id);
                Error = "User not found.";
                await OnGetAsync();
                return Page();
            }

            var companyId = _companyContext.GetCompanyIdOrThrow();
            if (user.CompanyId != companyId)
            {
                _logger.LogWarning("User {CurrentUserId} attempted to delete user {TargetUserId} from different company", currentUserId, id);
                Error = "You can only delete users from your own company.";
                await OnGetAsync();
                return Page();
            }

            // Check if current user has permission to delete this user based on role hierarchy
            if (!CanModifyUser(user.Role))
            {
                _logger.LogWarning("User {CurrentUserId} attempted to delete user {TargetUserId} with higher role {TargetRole}",
                    currentUserId, id, user.Role);
                TempData["ErrorMessage"] = $"You do not have permission to delete users with the {user.Role} role.";
                return RedirectToPage();
            }

            _logger.LogInformation("Starting deletion of user {UserId} ({UserName}) by admin {CurrentUserId}", id, user.DisplayName, currentUserId);

            // 1. Remove all shift assignments
            var shiftAssignments = await _db.ShiftAssignments.Where(sa => sa.UserId == id).ToListAsync();
            if (shiftAssignments.Any())
            {
                _logger.LogInformation("Removing {Count} shift assignments for user {UserId}", shiftAssignments.Count, id);
                _db.ShiftAssignments.RemoveRange(shiftAssignments);
            }

            // 2. Delete all time-off requests
            var timeOffRequests = await _db.TimeOffRequests.Where(tor => tor.UserId == id).ToListAsync();
            if (timeOffRequests.Any())
            {
                _logger.LogInformation("Deleting {Count} time-off requests for user {UserId}", timeOffRequests.Count, id);
                _db.TimeOffRequests.RemoveRange(timeOffRequests);
            }

            // 3. Delete all swap requests (both from and to this user)
            var userAssignmentIds = shiftAssignments.Select(sa => sa.Id).ToList();
            var swapRequestsFrom = await _db.SwapRequests.Where(sr => userAssignmentIds.Contains(sr.FromAssignmentId)).ToListAsync();
            var swapRequestsTo = await _db.SwapRequests.Where(sr => sr.ToUserId == id).ToListAsync();

            var allSwapRequests = swapRequestsFrom.Union(swapRequestsTo).Distinct().ToList();
            if (allSwapRequests.Any())
            {
                _logger.LogInformation("Deleting {Count} swap requests related to user {UserId}", allSwapRequests.Count, id);
                _db.SwapRequests.RemoveRange(allSwapRequests);
            }

            // 4. Delete the user
            _logger.LogInformation("Deleting user {UserId} ({UserName})", id, user.DisplayName);
            _db.Users.Remove(user);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Successfully deleted user {UserId} ({UserName}) and cleaned up all related data", id, user.DisplayName);

            // Use TempData to show success message after redirect
            TempData["SuccessMessage"] = $"User {user.DisplayName} has been successfully deleted along with all their shifts, time-off requests, and swap requests.";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            Error = "An error occurred while deleting the user. Please try again.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostApproveJoinRequestAsync(int id)
    {
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var currentUser = await _db.Users.FindAsync(currentUserId);

        var joinRequest = await _db.UserJoinRequests
            .Include(jr => jr.Company)
            .FirstOrDefaultAsync(jr => jr.Id == id);

        if (joinRequest == null)
        {
            TempData["ErrorMessage"] = "Join request not found.";
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
            TempData["ErrorMessage"] = "You don't have permission to approve this request.";
            return RedirectToPage();
        }

        if (joinRequest.Status != JoinRequestStatus.Pending)
        {
            TempData["ErrorMessage"] = "This request has already been reviewed.";
            return RedirectToPage();
        }

        // Check if user with this email already exists
        if (await _db.Users.AnyAsync(u => u.Email == joinRequest.Email))
        {
            TempData["ErrorMessage"] = "A user with this email already exists.";
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

        _logger.LogInformation("Join request {RequestId} approved by {ApproverId}. Created user {UserId} ({Email}) for company {CompanyId}",
            id, currentUserId, newUser.Id, newUser.Email, joinRequest.CompanyId);

        TempData["SuccessMessage"] = $"Approved {joinRequest.DisplayName} ({joinRequest.Email}) as {joinRequest.RequestedRole} for {joinRequest.Company?.Name}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectJoinRequestAsync(int id, string? reason)
    {
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var currentUser = await _db.Users.FindAsync(currentUserId);

        var joinRequest = await _db.UserJoinRequests
            .Include(jr => jr.Company)
            .FirstOrDefaultAsync(jr => jr.Id == id);

        if (joinRequest == null)
        {
            TempData["ErrorMessage"] = "Join request not found.";
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
            TempData["ErrorMessage"] = "You don't have permission to reject this request.";
            return RedirectToPage();
        }

        if (joinRequest.Status != JoinRequestStatus.Pending)
        {
            TempData["ErrorMessage"] = "This request has already been reviewed.";
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

        TempData["SuccessMessage"] = $"Rejected join request from {joinRequest.DisplayName} ({joinRequest.Email}).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBatchApproveJoinRequestsAsync()
    {
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var currentUser = await _db.Users.FindAsync(currentUserId);

        if (SelectedRequests == null || !SelectedRequests.Any())
        {
            TempData["ErrorMessage"] = "No requests selected for approval.";
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

            // Get all selected join requests
            var joinRequests = await _db.UserJoinRequests
                .Include(jr => jr.Company)
                .Where(jr => SelectedRequests.Contains(jr.Id))
                .ToListAsync();

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
                TempData["ErrorMessage"] = "You don't have permission to approve requests.";
                return RedirectToPage();
            }

            foreach (var joinRequest in joinRequests)
            {
                // Check permission for this specific request
                if (!accessibleCompanyIds.Contains(joinRequest.CompanyId))
                {
                    errors.Add($"No permission to approve {joinRequest.DisplayName} (different company)");
                    skippedCount++;
                    continue;
                }

                // Check if already reviewed
                if (joinRequest.Status != JoinRequestStatus.Pending)
                {
                    errors.Add($"{joinRequest.DisplayName} already reviewed");
                    skippedCount++;
                    continue;
                }

                // Check if user already exists
                if (await _db.Users.AnyAsync(u => u.Email == joinRequest.Email))
                {
                    errors.Add($"User with email {joinRequest.Email} already exists");
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
                    errors.Add($"No permission to assign {assignedRole} role to {joinRequest.DisplayName}");
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
            var successMessage = $"Successfully approved {approvedCount} user(s).";
            if (skippedCount > 0)
            {
                successMessage += $" Skipped {skippedCount} request(s).";
            }

            TempData["SuccessMessage"] = successMessage;

            if (errors.Any())
            {
                TempData["ErrorMessage"] = "Some requests had issues: " + string.Join("; ", errors.Take(3));
            }

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error during batch approval of join requests");
            TempData["ErrorMessage"] = "An error occurred during batch approval. Please try again.";
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

        // Director can modify Employee, Manager, Director, Trainee (but NOT Owner)
        if (currentUserRole == nameof(UserRole.Director))
        {
            return targetUserRole == UserRole.Employee
                || targetUserRole == UserRole.Manager
                || targetUserRole == UserRole.Director
                || targetUserRole == UserRole.Trainee;
        }

        // Manager can modify Employee and Trainee ONLY (NOT Owner, Director, or other Managers)
        if (currentUserRole == nameof(UserRole.Manager))
        {
            return targetUserRole == UserRole.Employee
                || targetUserRole == UserRole.Trainee;
        }

        // Employees and Trainees cannot modify anyone
        return false;
    }
}
