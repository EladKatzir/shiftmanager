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
    private readonly INotificationService _notificationService;
    private readonly IGrantService _grantService;

    public UsersModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<UsersModel> logger,
        ICompanyContext companyContext,
        IDirectorService directorService,
        ITraineeService traineeService,
        IAuditLogService auditLogService,
        IMailService mailService,
        INotificationService notificationService,
        IGrantService grantService)
        : base(localizer)
    {
        _db = db;
        _logger = logger;
        _companyContext = companyContext;
        _directorService = directorService;
        _traineeService = traineeService;
        _auditLogService = auditLogService;
        _mailService = mailService;
        _notificationService = notificationService;
        _grantService = grantService;
    }

    public record UserVM(int Id, string DisplayName, string Email, string CompanyName, string Role, bool IsActive, bool IsLocked, DateTime? LockoutEnd, int? JobTypeId, string? JobTypeName, string? DepartmentName, int GrantsCount);
    public record JoinRequestVM(int Id, string Email, string DisplayName, string CompanyName, string RequestedRole, DateTime CreatedAt, JoinRequestStatus Status);
    public record MoleculeOption(int Id, string Name, string AreaName);
    public record JobTypeOption(int Id, string Name, string AreaName);

    // Batch approval support
    public class BatchApprovalItem
    {
        public int RequestId { get; set; }
        public UserRole AssignedRole { get; set; }
    }

    public List<UserVM> Users { get; set; } = new();
    public List<JoinRequestVM> JoinRequests { get; set; } = new();
    public List<Company> AvailableCompanies { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();
    public List<JobTypeOption> AvailableJobTypes { get; set; } = new();

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

    [BindProperty(SupportsGet = true)]
    public int? UserFilterMoleculeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? UserFilterJobTypeId { get; set; }

    [BindProperty, EmailAddress] public string NewEmail { get; set; } = string.Empty;
    [BindProperty] public string NewDisplayName { get; set; } = string.Empty;
    [BindProperty] public string NewPassword { get; set; } = string.Empty;
    [BindProperty] public string NewRole { get; set; } = "Employee";

    // Owner cross-company user management
    [BindProperty]
    public int? NewUserCompanyId { get; set; }

    // Job type for new user
    [BindProperty]
    public int? NewJobTypeId { get; set; }

    public List<Company> Companies { get; set; } = new();

    public bool IsOwner { get; set; }

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

        // ✅ Grant-based: Determine Owner status via AdminAccess grant
        IsOwner = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");

        // Load all companies for Owner user management
        if (IsOwner)
        {
            Companies = await _db.Companies
                .IgnoreQueryFilters()
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        // ✅ Grant-based: Determine accessible company IDs via ManageJoinRequests grant scope
        var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "ManageJoinRequests");

        // Fall back to user's own company if no grants found
        if (!accessibleCompanyIds.Any())
        {
            accessibleCompanyIds = new List<int> { currentUser.CompanyId };
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
                companies.TryGetValue(jr.CompanyId, out var company) ? company.Name : $"Company #{jr.CompanyId}",
                jr.RequestedRole.ToString(),
                jr.CreatedAt,
                jr.Status
            ))
            .ToList();

        // Load available companies for filter dropdown
        if (IsOwner)
        {
            AvailableCompanies = await _db.Companies
                .IgnoreQueryFilters()
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        else
        {
            AvailableCompanies = await _db.Companies
                .Where(c => accessibleCompanyIds.Contains(c.Id))
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        // Load available molecules for filter dropdown
        AvailableMolecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .Include(m => m.Area)
            .OrderBy(m => m.Area.Name).ThenBy(m => m.Name)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName, m.Area.DisplayName))
            .ToListAsync();

        // Load available job types for filter dropdown
        AvailableJobTypes = await _db.JobTypes
            .IgnoreQueryFilters()
            .Where(jt => jt.IsActive)
            .Include(jt => jt.Area)
            .OrderBy(jt => jt.Area.Name).ThenBy(jt => jt.Name)
            .Select(jt => new JobTypeOption(jt.Id, jt.DisplayName, jt.Area.DisplayName))
            .ToListAsync();

        // Load existing users with filters
        IQueryable<AppUser> usersQuery;
        if (IsOwner)
        {
            // Owner sees ALL users across all companies
            usersQuery = _db.Users
                .IgnoreQueryFilters()
                .Include(u => u.JobType)
                .Include(u => u.Department)
                .AsNoTracking();
        }
        else
        {
            // Other roles see filtered by accessible companies
            usersQuery = _db.Users
                .Include(u => u.JobType)
                .Include(u => u.Department)
                .AsNoTracking()
                .Where(u => accessibleCompanyIds.Contains(u.CompanyId));
        }

        // Apply role filter first (before handling Directors specially)
        if (UserFilterRole.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.Role == UserFilterRole.Value);
        }

        // Apply molecule filter (via Company -> Molecule relationship)
        if (UserFilterMoleculeId.HasValue)
        {
            var companyIdsForMolecule = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.MoleculeId == UserFilterMoleculeId.Value)
                .Select(c => c.Id)
                .ToListAsync();
            usersQuery = usersQuery.Where(u => companyIdsForMolecule.Contains(u.CompanyId));
        }

        // Apply job type filter
        if (UserFilterJobTypeId.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.JobTypeId == UserFilterJobTypeId.Value);
        }

        var userData = await usersQuery.ToListAsync();

        // Load grants count per user
        var userIds = userData.Select(u => u.Id).ToList();
        var userGrantCounts = await _db.Grants
            .IgnoreQueryFilters()
            .Where(g => userIds.Contains(g.UserId))
            .GroupBy(g => g.UserId)
            .Select(grp => new { UserId = grp.Key, Count = grp.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        // Load companies for users
        var userCompanyIds = userData.Select(u => u.CompanyId).Distinct().ToList();
        Dictionary<int, Company> userCompanies;
        if (IsOwner)
        {
            userCompanies = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => userCompanyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);
        }
        else
        {
            userCompanies = await _db.Companies
                .AsNoTracking()
                .Where(c => userCompanyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);
        }

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
                    // ✅ FIX: Use TryGetValue to prevent KeyNotFoundException if company is missing
                    var companyName = managedCompanies.TryGetValue(companyId, out var company)
                        ? company.Name
                        : $"Company #{companyId}";

                    userList.Add(new UserVM(
                        u.Id,
                        u.DisplayName,
                        u.Email,
                        companyName,
                        u.Role.ToString(),
                        u.IsActive,
                        u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTime.UtcNow,
                        u.LockoutEnd,
                        u.JobTypeId,
                        u.JobType?.DisplayName,
                        u.Department?.DisplayName,
                        userGrantCounts.TryGetValue(u.Id, out var gc) ? gc : 0
                    ));
                }
            }
            else
            {
                // For non-Directors, use their primary company
                // Apply company filter if specified
                if (!UserFilterCompanyId.HasValue || u.CompanyId == UserFilterCompanyId.Value)
                {
                    // ✅ FIX: Use TryGetValue to prevent KeyNotFoundException if company is missing
                    var companyName = userCompanies.TryGetValue(u.CompanyId, out var company)
                        ? company.Name
                        : $"Company #{u.CompanyId}";

                    userList.Add(new UserVM(
                        u.Id,
                        u.DisplayName,
                        u.Email,
                        companyName,
                        u.Role.ToString(),
                        u.IsActive,
                        u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTime.UtcNow,
                        u.LockoutEnd,
                        u.JobTypeId,
                        u.JobType?.DisplayName,
                        u.Department?.DisplayName,
                        userGrantCounts.TryGetValue(u.Id, out var gc) ? gc : 0
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

        // ✅ Grant-based: Determine target company using EditCompanyUsers grant scope
        int targetCompanyId;
        var userIdClaimForCompany = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaimForCompany, out var currentUserIdForCompany))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"];
            return RedirectToPage();
        }

        if (NewUserCompanyId.HasValue)
        {
            // User selected a company - verify they have EditCompanyUsers grant for it
            var hasEditGrant = await _grantService.HasGrantForCompanyAsync(currentUserIdForCompany, "EditCompanyUsers", NewUserCompanyId.Value);
            if (!hasEditGrant)
            {
                TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"];
                return RedirectToPage();
            }

            // Verify company exists
            var company = await _db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == NewUserCompanyId.Value);

            if (company == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_InvalidCompanySelected"];
                return RedirectToPage();
            }

            targetCompanyId = NewUserCompanyId.Value;
        }
        else
        {
            targetCompanyId = _companyContext.GetCompanyIdOrThrow();
        }

        // Validate job type if specified
        if (NewJobTypeId.HasValue)
        {
            var jobType = await _db.JobTypes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(jt => jt.Id == NewJobTypeId.Value && jt.IsActive);
            if (jobType == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_InvalidJobTypeSelected"];
                return RedirectToPage();
            }
        }

        var (h, s) = PasswordHasher.CreateHash(NewPassword);
        var newUser = new AppUser
        {
            CompanyId = targetCompanyId,
            Email = NewEmail,
            DisplayName = NewDisplayName,
            Role = targetRole,
            IsActive = true,
            PasswordHash = h,
            PasswordSalt = s,
            JobTypeId = NewJobTypeId
        };
        _db.Users.Add(newUser);
        await _db.SaveChangesAsync();

        // ✅ Onboarding: Assign role template grants
        var roleTemplateKey = MapUserRoleToRoleTemplateKey(targetRole);
        var grantScope = GrantScope.Company(targetCompanyId);
        var grantsAssigned = await _grantService.AssignRoleTemplateGrantsAsync(newUser.Id, roleTemplateKey, grantScope, currentUserIdForCompany);
        _logger.LogInformation("Assigned {GrantsCount} grants from role template {RoleTemplate} to new user {UserId}",
            grantsAssigned, roleTemplateKey, newUser.Id);

        // ✅ P0-4/P0-5 FIX: If creating a Director, also create DirectorCompany mapping
        if (targetRole == UserRole.Director)
        {
            var directorAssignment = new DirectorCompany
            {
                UserId = newUser.Id,
                CompanyId = targetCompanyId,
                GrantedBy = 0, // Will be set below after getting currentUserId
                GrantedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            // Get current user ID for GrantedBy
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var grantedBy))
            {
                directorAssignment.GrantedBy = grantedBy;
            }

            _db.DirectorCompanies.Add(directorAssignment);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created DirectorCompany mapping for new Director {DirectorId} to Company {CompanyId}",
                newUser.Id, targetCompanyId);
        }

        // Audit logging (RoleAssignmentAudit)
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaimForAudit = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaimForAudit, out var currentUserId))
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
            CompanyId = targetCompanyId,
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

            // ✅ P0-4/P0-5 FIX: If changing TO Director, create DirectorCompany mapping
            if (oldRole != UserRole.Director && targetRole == UserRole.Director)
            {
                // Check if DirectorCompany mapping already exists
                var existingMapping = await _db.DirectorCompanies
                    .FirstOrDefaultAsync(dc => dc.UserId == u.Id && dc.CompanyId == u.CompanyId && !dc.IsDeleted);

                if (existingMapping == null)
                {
                    var directorAssignment = new DirectorCompany
                    {
                        UserId = u.Id,
                        CompanyId = u.CompanyId,
                        GrantedBy = currentUserId,
                        GrantedAt = DateTime.UtcNow,
                        IsDeleted = false
                    };

                    _db.DirectorCompanies.Add(directorAssignment);
                    await _db.SaveChangesAsync();

                    _logger.LogInformation("Created DirectorCompany mapping for user {UserId} promoted to Director for Company {CompanyId}",
                        u.Id, u.CompanyId);
                }
            }

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

    public async Task<IActionResult> OnPostJobTypeAsync(int id, int? jobTypeId)
    {
        // SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"];
            return RedirectToPage();
        }

        var u = await _db.Users.FindAsync(id);
        if (u == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"];
            return RedirectToPage();
        }

        // Check if current user has permission to modify this user
        if (!CanModifyUser(u.Role))
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionModifyUser"], u.Role);
            return RedirectToPage();
        }

        // Validate job type if specified
        string? jobTypeName = null;
        if (jobTypeId.HasValue)
        {
            var jobType = await _db.JobTypes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(jt => jt.Id == jobTypeId.Value && jt.IsActive);
            if (jobType == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_InvalidJobTypeSelected"];
                return RedirectToPage();
            }
            jobTypeName = jobType.DisplayName;
        }

        var oldJobTypeId = u.JobTypeId;
        u.JobTypeId = jobTypeId;
        await _db.SaveChangesAsync();

        // Audit logging
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var currentUserId))
        {
            await _auditLogService.LogUserActionAsync(
                userId: currentUserId,
                action: "JobTypeChanged",
                entityType: "User",
                entityId: u.Id,
                description: $"Changed job type for {u.DisplayName} from {oldJobTypeId} to {jobTypeId}"
            );
        }

        TempData["SuccessMessage"] = string.Format(_localizer["Success_JobTypeUpdated"], u.DisplayName, jobTypeName ?? "-");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int id, string newPassword)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"].Value;
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            TempData["ErrorMessage"] = _localizer["Error_PasswordRequired"].Value;
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

    public async Task<IActionResult> OnPostUnlockAccountAsync(int id)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"].Value;
            return RedirectToPage();
        }

        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"];
            return RedirectToPage();
        }

        var targetUser = await _db.Users.FindAsync(id);
        if (targetUser == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"];
            return RedirectToPage();
        }

        // Check if current user has permission to unlock this user
        if (!CanModifyUser(targetUser.Role))
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionToUnlock"], targetUser.Role);
            return RedirectToPage();
        }

        // Unlock the account
        targetUser.FailedLoginAttempts = 0;
        targetUser.LockoutEnd = null;
        await _db.SaveChangesAsync();

        // Log the unlock action for security audit
        await _auditLogService.LogUserActionAsync(
            userId: currentUserId,
            action: "AccountUnlock",
            entityType: "User",
            entityId: targetUser.Id,
            description: $"Account unlocked for user {targetUser.DisplayName} ({targetUser.Email})"
        );

        _logger.LogInformation("User {CurrentUserId} unlocked account for user {TargetUserId} ({Email})",
            currentUserId, targetUser.Id, targetUser.Email);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_AccountUnlocked"], targetUser.DisplayName);
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

            // ✅ Grant-based: Check if user has EditCompanyUsers grant for target user's company
            var hasEditGrant = await _grantService.HasGrantForCompanyAsync(currentUserId, "EditCompanyUsers", user.CompanyId);
            if (!hasEditGrant)
            {
                _logger.LogWarning("User {CurrentUserId} attempted to delete user {TargetUserId} without EditCompanyUsers grant for company {CompanyId}",
                    currentUserId, id, user.CompanyId);
                Error = _localizer["Error_CanOnlyDeleteOwnCompanyUsers"];
                await OnGetAsync();
                return Page();
            }

            // Check if current user has permission to delete this user based on role hierarchy (legacy check)
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

        // ✅ Grant-based: Verify user has permission to approve this request
        var hasPermission = await _grantService.HasGrantForCompanyAsync(currentUserId, "ManageJoinRequests", joinRequest.CompanyId);

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

        // ✅ Onboarding: Assign role template grants
        var roleTemplateKey = MapUserRoleToRoleTemplateKey(joinRequest.RequestedRole);
        var grantScope = GrantScope.Company(joinRequest.CompanyId);
        var grantsAssigned = await _grantService.AssignRoleTemplateGrantsAsync(newUser.Id, roleTemplateKey, grantScope, currentUserId);
        _logger.LogInformation("Assigned {GrantsCount} grants from role template {RoleTemplate} to user {UserId} via join request approval",
            grantsAssigned, roleTemplateKey, newUser.Id);

        // Send account approval email notification
        _ = _mailService.SendAccountApprovedEmailAsync(
            newUser.Email,
            newUser.DisplayName,
            newUser.Role.ToString(),
            joinRequest.Company?.Name ?? "the company"
        );

        // Create in-app notification for the new user
        _ = _notificationService.CreateAccessRequestApprovedNotificationAsync(
            newUser.Id,
            joinRequest.Company?.Name ?? "the company",
            newUser.Role.ToString()
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

        // ✅ Grant-based: Verify user has permission to reject this request
        var hasPermission = await _grantService.HasGrantForCompanyAsync(currentUserId, "ManageJoinRequests", joinRequest.CompanyId);

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

            // ✅ Grant-based: Get accessible company IDs for permission check
            var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "ManageJoinRequests");
            if (!accessibleCompanyIds.Any())
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

                // ✅ Onboarding: Assign role template grants
                var roleTemplateKey = MapUserRoleToRoleTemplateKey(assignedRole);
                var grantScope = GrantScope.Company(joinRequest.CompanyId);
                var grantsAssigned = await _grantService.AssignRoleTemplateGrantsAsync(newUser.Id, roleTemplateKey, grantScope, currentUserId);

                _logger.LogInformation(
                    "Batch approval: Join request {RequestId} approved by {ApproverId}. Created user {UserId} ({Email}) with role {Role} for company {CompanyId}. Assigned {GrantsCount} grants.",
                    joinRequest.Id, currentUserId, newUser.Id, newUser.Email, assignedRole, joinRequest.CompanyId, grantsAssigned);

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
    /// Check if current user has permission to modify a user with the specified role in the target company.
    /// ✅ Grant-based: Uses EditCompanyUsers grant with company scope instead of role hierarchy.
    /// </summary>
    private async Task<bool> CanModifyUserAsync(int currentUserId, int targetUserId, int targetCompanyId)
    {
        // Check if current user has EditCompanyUsers grant for the target user's company
        var hasEditGrant = await _grantService.HasGrantForCompanyAsync(currentUserId, "EditCompanyUsers", targetCompanyId);

        if (!hasEditGrant)
            return false;

        // Additional check: prevent self-modification through this method
        if (currentUserId == targetUserId)
            return false;

        return true;
    }

    /// <summary>
    /// Legacy synchronous check - kept for backward compatibility during migration.
    /// Prefer CanModifyUserAsync for new code.
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

            // ✅ Grant-based: Determine accessible company IDs via ManageJoinRequests grant scope
            var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "ManageJoinRequests");

            // Fall back to user's own company if no grants found
            if (!accessibleCompanyIds.Any())
            {
                accessibleCompanyIds = new List<int> { currentUser.CompanyId };
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

            if (UserFilterMoleculeId.HasValue)
            {
                var companyIdsForMolecule = await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => c.MoleculeId == UserFilterMoleculeId.Value)
                    .Select(c => c.Id)
                    .ToListAsync();
                usersQuery = usersQuery.Where(u => companyIdsForMolecule.Contains(u.CompanyId));
            }

            if (UserFilterJobTypeId.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.JobTypeId == UserFilterJobTypeId.Value);
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

    /// <summary>
    /// Maps UserRole enum to the corresponding RoleTemplate key.
    /// Used for grant assignment during onboarding.
    /// </summary>
    private static string MapUserRoleToRoleTemplateKey(UserRole role)
    {
        return role switch
        {
            UserRole.Owner => "Owner",
            UserRole.Director => "BRDirector",
            UserRole.Manager => "MoleculeAdmin",
            UserRole.Employee => "Employee",
            UserRole.Trainee => "Employee",
            UserRole.Assigner => "Assigner",
            _ => "Employee"
        };
    }
}
