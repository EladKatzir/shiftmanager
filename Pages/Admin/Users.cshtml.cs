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

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires IsManagerOrAdmin policy;
// Owner-only paths are gated by AdminAccess grant check; grant-scoped queries enforce per-company access;
// hierarchy data (Molecules, JobTypes, Companies) is reference data for dropdowns, not sensitive
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
        // IgnoreQueryFilters: tenant filter would restrict to admin's own company,
        // but join requests need cross-company visibility scoped by accessibleCompanyIds
        var joinRequestsQuery = _db.UserJoinRequests
            .IgnoreQueryFilters()
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

        // Pre-load director-company mappings to avoid N+1 queries in the loop
        var directorUserIds = userData.Where(u => u.Role == UserRole.Director).Select(u => u.Id).ToList();
        var directorCompanyMap = new Dictionary<int, List<int>>();
        var allManagedCompanyIds = new HashSet<int>();

        foreach (var dirId in directorUserIds)
        {
            var dirManagedIds = await _directorService.GetDirectorCompanyIdsAsync(dirId);
            var filtered = dirManagedIds.Where(id => accessibleCompanyIds.Contains(id)).ToList();
            if (UserFilterCompanyId.HasValue)
            {
                filtered = filtered.Where(id => id == UserFilterCompanyId.Value).ToList();
            }
            directorCompanyMap[dirId] = filtered;
            foreach (var id in filtered) allManagedCompanyIds.Add(id);
        }

        // Batch-load all managed company names (single query instead of per-director)
        var managedCompaniesLookup = allManagedCompanyIds.Any()
            ? await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => allManagedCompanyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name)
            : new Dictionary<int, string>();

        // Build user list, handling Directors specially
        var userList = new List<UserVM>();

        foreach (var u in userData)
        {
            if (u.Role == UserRole.Director)
            {
                var managedCompanyIds = directorCompanyMap.TryGetValue(u.Id, out var ids) ? ids : new List<int>();

                // Create one entry per managed company
                foreach (var companyId in managedCompanyIds)
                {
                    var companyName = managedCompaniesLookup.TryGetValue(companyId, out var name)
                        ? name
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
            TempData["ErrorMessage"] = _localizer["Error_InvalidRole"].Value;
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
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }

        if (NewUserCompanyId.HasValue)
        {
            // User selected a company - verify they have EditCompanyUsers grant for it
            var isAdmin = await _grantService.HasGrantAsync(currentUserIdForCompany, "AdminAccess");
            var hasEditGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(currentUserIdForCompany, "EditCompanyUsers", NewUserCompanyId.Value);
            if (!hasEditGrant)
            {
                TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
                return RedirectToPage();
            }

            // Verify company exists
            var company = await _db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == NewUserCompanyId.Value);

            if (company == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_InvalidCompanySelected"].Value;
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
                TempData["ErrorMessage"] = _localizer["Error_InvalidJobTypeSelected"].Value;
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
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"].Value;
            return RedirectToPage();
        }

        var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (u != null)
        {
            // Grant-based company scope check
            var toggleUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(toggleUserIdClaim, out var toggleCurrentUserId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
                return RedirectToPage();
            }

            var isAdmin = await _grantService.HasGrantAsync(toggleCurrentUserId, "AdminAccess");
            var hasEditGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(toggleCurrentUserId, "EditCompanyUsers", u.CompanyId);
            if (!hasEditGrant)
            {
                _logger.LogWarning("User {CurrentUserId} attempted to toggle user {TargetUserId} without EditCompanyUsers grant for company {CompanyId}",
                    toggleCurrentUserId, id, u.CompanyId);
                TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
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
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"].Value;
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(role) || role.Length > 50)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRole"].Value;
            return RedirectToPage();
        }

        // Validate role string and permission to assign
        if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var targetRole))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRole"].Value;
            return RedirectToPage();
        }

        if (!_directorService.CanAssignRole(targetRole))
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionAssignRole"], targetRole);
            return RedirectToPage();
        }

        var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (u != null)
        {
            var oldRole = u.Role;

            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
                return RedirectToPage();
            }

            // HIGH-004 FIX: Grant-based company scope check
            var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
            var hasRoleEditGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(currentUserId, "EditCompanyUsers", u.CompanyId);
            if (!hasRoleEditGrant)
            {
                _logger.LogWarning("User {CurrentUserId} attempted role change on user {TargetUserId} without EditCompanyUsers grant for company {CompanyId}",
                    currentUserId, id, u.CompanyId);
                TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
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
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"].Value;
            return RedirectToPage();
        }

        var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (u == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value;
            return RedirectToPage();
        }

        // Grant-based company scope check
        var jobTypeUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(jobTypeUserIdClaim, out var jobTypeCurrentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }

        var isAdmin = await _grantService.HasGrantAsync(jobTypeCurrentUserId, "AdminAccess");
        var hasJobTypeGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(jobTypeCurrentUserId, "EditCompanyUsers", u.CompanyId);
        if (!hasJobTypeGrant)
        {
            _logger.LogWarning("User {CurrentUserId} attempted job type change on user {TargetUserId} without EditCompanyUsers grant for company {CompanyId}",
                jobTypeCurrentUserId, id, u.CompanyId);
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
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
                TempData["ErrorMessage"] = _localizer["Error_InvalidJobTypeSelected"].Value;
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
            TempData["ErrorMessage"] = _localizer["Error_PasswordTooShort"].Value;
            return RedirectToPage();
        }

        if (newPassword.Length > 128)
        {
            TempData["ErrorMessage"] = _localizer["Error_PasswordTooLong"].Value;
            return RedirectToPage();
        }

        var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (u != null)
        {
            // Grant-based company scope check
            var resetUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(resetUserIdClaim, out var resetCurrentUserId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim during password reset authorization");
                return RedirectToPage();
            }

            var isAdmin = await _grantService.HasGrantAsync(resetCurrentUserId, "AdminAccess");
            var hasResetGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(resetCurrentUserId, "EditCompanyUsers", u.CompanyId);
            if (!hasResetGrant)
            {
                _logger.LogWarning("User {CurrentUserId} attempted password reset on user {TargetUserId} without EditCompanyUsers grant for company {CompanyId}",
                    resetCurrentUserId, id, u.CompanyId);
                TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
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
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }

        var targetUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (targetUser == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value;
            return RedirectToPage();
        }

        // Grant-based company scope check
        var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
        var hasUnlockGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(currentUserId, "EditCompanyUsers", targetUser.CompanyId);
        if (!hasUnlockGrant)
        {
            _logger.LogWarning("User {CurrentUserId} attempted to unlock user {TargetUserId} without EditCompanyUsers grant for company {CompanyId}",
                currentUserId, id, targetUser.CompanyId);
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
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
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
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

            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found for deletion", id);
                Error = _localizer["Error_UserNotFound"];
                await OnGetAsync();
                return Page();
            }

            // Grant-based: Check if user has EditCompanyUsers grant for target user's company
            // AdminAccess holders can manage users across all companies
            var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
            var hasEditGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(currentUserId, "EditCompanyUsers", user.CompanyId);
            if (!hasEditGrant)
            {
                _logger.LogWarning("User {CurrentUserId} attempted to delete user {TargetUserId} without EditCompanyUsers grant for company {CompanyId}",
                    currentUserId, id, user.CompanyId);
                Error = _localizer["Error_CanOnlyDeleteOwnCompanyUsers"];
                await OnGetAsync();
                return Page();
            }

            _logger.LogInformation("Starting deletion of user {UserId} ({UserName}) by admin {CurrentUserId}", id, user.DisplayName, currentUserId);

            // IgnoreQueryFilters: target user's related data may be in a different company
            // Get count of shift assignments for logging before deletion
            var shiftAssignmentCount = await _db.ShiftAssignments.IgnoreQueryFilters().Where(sa => sa.UserId == id).CountAsync();

            // Get shift assignment IDs for swap request deletion
            var userAssignmentIds = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(sa => sa.UserId == id)
                .Select(sa => sa.Id)
                .ToListAsync();

            // 1. Delete all swap requests (both from and to this user)
            // Must do this first before deleting shift assignments due to foreign key
            var swapRequestsCount = await _db.SwapRequests
                .IgnoreQueryFilters()
                .Where(sr => userAssignmentIds.Contains(sr.FromAssignmentId) || sr.ToUserId == id)
                .CountAsync();

            if (swapRequestsCount > 0)
            {
                _logger.LogInformation("Deleting {Count} swap requests related to user {UserId}", swapRequestsCount, id);
                await _db.SwapRequests
                    .IgnoreQueryFilters()
                    .Where(sr => userAssignmentIds.Contains(sr.FromAssignmentId) || sr.ToUserId == id)
                    .ExecuteDeleteAsync();
            }

            // 2. Remove all shift assignments using ExecuteDeleteAsync for better performance
            if (shiftAssignmentCount > 0)
            {
                _logger.LogInformation("Removing {Count} shift assignments for user {UserId}", shiftAssignmentCount, id);
                await _db.ShiftAssignments.IgnoreQueryFilters().Where(sa => sa.UserId == id).ExecuteDeleteAsync();
            }

            // 3. Delete all time-off requests
            var timeOffRequestCount = await _db.TimeOffRequests.IgnoreQueryFilters().Where(tor => tor.UserId == id).CountAsync();
            if (timeOffRequestCount > 0)
            {
                _logger.LogInformation("Deleting {Count} time-off requests for user {UserId}", timeOffRequestCount, id);
                await _db.TimeOffRequests.IgnoreQueryFilters().Where(tor => tor.UserId == id).ExecuteDeleteAsync();
            }

            // 4. Deactivate the user (soft-delete to preserve audit trail integrity)
            _logger.LogInformation("Deactivating user {UserId} ({UserName})", id, user.DisplayName);
            user.IsActive = false;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Successfully deactivated user {UserId} ({UserName}) and cleaned up all related data", id, user.DisplayName);

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
            TempData["ErrorMessage"] = _localizer["Error_InvalidRequestId"].Value;
            return RedirectToPage();
        }

        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        var joinRequest = await _db.UserJoinRequests
            .IgnoreQueryFilters()
            .Include(jr => jr.Company)
            .FirstOrDefaultAsync(jr => jr.Id == id);

        if (joinRequest == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JoinRequestNotFound"].Value;
            return RedirectToPage();
        }

        // ✅ Grant-based: Verify user has permission to approve this request
        var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
        var hasPermission = isAdmin || await _grantService.HasGrantForCompanyAsync(currentUserId, "ManageJoinRequests", joinRequest.CompanyId);

        if (!hasPermission)
        {
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionApproveJoinRequest"].Value;
            return RedirectToPage();
        }

        if (joinRequest.Status != JoinRequestStatus.Pending)
        {
            TempData["ErrorMessage"] = _localizer["Error_RequestAlreadyReviewed"].Value;
            return RedirectToPage();
        }

        // Check if user with this email already exists
        if (await _db.Users.AnyAsync(u => u.Email == joinRequest.Email))
        {
            TempData["ErrorMessage"] = _localizer["Error_UserEmailAlreadyExists"].Value;
            return RedirectToPage();
        }

        // Validate permission to assign the requested role
        if (!_directorService.CanAssignRole(joinRequest.RequestedRole))
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Error_NoPermissionAssignRole"].Value, joinRequest.RequestedRole);
            return RedirectToPage();
        }

        // HIGH-009 FIX: Wrap in try/catch to prevent unhandled exceptions
        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving join request {RequestId}", id);
            TempData["ErrorMessage"] = _localizer["Error_ApprovingJoinRequest"].Value;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostRejectJoinRequestAsync(int id, string? reason)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRequestId"].Value;
            return RedirectToPage();
        }

        if (!string.IsNullOrWhiteSpace(reason) && reason.Length > 1000)
        {
            TempData["ErrorMessage"] = _localizer["Error_RejectionReasonTooLong"].Value;
            return RedirectToPage();
        }

        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        var joinRequest = await _db.UserJoinRequests
            .IgnoreQueryFilters()
            .Include(jr => jr.Company)
            .FirstOrDefaultAsync(jr => jr.Id == id);

        if (joinRequest == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JoinRequestNotFound"].Value;
            return RedirectToPage();
        }

        // ✅ Grant-based: Verify user has permission to reject this request
        var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
        var hasPermission = isAdmin || await _grantService.HasGrantForCompanyAsync(currentUserId, "ManageJoinRequests", joinRequest.CompanyId);

        if (!hasPermission)
        {
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionRejectRequest"].Value;
            return RedirectToPage();
        }

        if (joinRequest.Status != JoinRequestStatus.Pending)
        {
            TempData["ErrorMessage"] = _localizer["Error_RequestAlreadyReviewed"].Value;
            return RedirectToPage();
        }

        // HIGH-010 FIX: Wrap in try/catch to prevent unhandled exceptions
        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting join request {RequestId}", id);
            TempData["ErrorMessage"] = _localizer["Error_RejectingJoinRequest"].Value;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostBatchApproveJoinRequestsAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        if (SelectedRequests == null || !SelectedRequests.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_NoRequestsSelected"].Value;
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
            // IgnoreQueryFilters: tenant filter would hide cross-company requests;
            // grant-based permission check below enforces proper authorization
            var joinRequests = await _db.UserJoinRequests
                .IgnoreQueryFilters()
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

            // Grant-based: AdminAccess bypasses company-scoped grant check
            var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
            List<int> accessibleCompanyIds;
            if (isAdmin)
            {
                // Admin has access to all companies
                accessibleCompanyIds = await _db.Companies
                    .IgnoreQueryFilters()
                    .Select(c => c.Id)
                    .ToListAsync();
            }
            else
            {
                accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "ManageJoinRequests");
                if (!accessibleCompanyIds.Any())
                {
                    TempData["ErrorMessage"] = _localizer["Error_NoPermissionApproveRequests"].Value;
                    return RedirectToPage();
                }
            }

            foreach (var joinRequest in joinRequests)
            {
                // ✅ SECURITY FIX (DEFECT-019): Check permission for this specific request
                if (!accessibleCompanyIds.Contains(joinRequest.CompanyId))
                {
                    _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to approve join request {RequestId} for unauthorized company {CompanyId}",
                        currentUserId, currentUser!.Role, joinRequest.Id, joinRequest.CompanyId);
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
                TempData["ErrorMessage"] = _localizer["Error_SomeRequestsHadIssues"].Value + ": " + string.Join("; ", errors.Take(3));
            }

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error during batch approval of join requests");
            TempData["ErrorMessage"] = _localizer["Error_BatchApprovalFailed"].Value;
            return RedirectToPage();
        }
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
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
                return RedirectToPage();
            }

            var currentUser = await _db.Users.FindAsync(currentUserId);
            if (currentUser == null)
            {
                _logger.LogError("User {UserId} not found in database", currentUserId);
                return RedirectToPage();
            }

            // ✅ Grant-based: AdminAccess sees all users; others use ManageJoinRequests scope
            var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");

            IQueryable<AppUser> usersQuery;
            if (isAdmin)
            {
                // Admin sees all users across all companies (matches OnGetAsync behavior)
                usersQuery = _db.Users
                    .IgnoreQueryFilters()
                    .AsNoTracking();
            }
            else
            {
                var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "ManageJoinRequests");

                // Fall back to user's own company if no grants found
                if (!accessibleCompanyIds.Any())
                {
                    accessibleCompanyIds = new List<int> { currentUser.CompanyId };
                }

                usersQuery = _db.Users
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(u => accessibleCompanyIds.Contains(u.CompanyId));
            }

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
            // Add UTF-8 BOM for Hebrew Excel compatibility (fixes G-07)
            var preamble = Encoding.UTF8.GetPreamble();
            var csvBytes = Encoding.UTF8.GetBytes(csv.ToString());
            var result = new byte[preamble.Length + csvBytes.Length];
            preamble.CopyTo(result, 0);
            csvBytes.CopyTo(result, preamble.Length);
            return File(result, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting users to CSV");
            TempData["ErrorMessage"] = _localizer["Error_ExportingUsers"].Value;
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

    /// <summary>
    /// I-07: Bulk user import from CSV file.
    /// Expected CSV format: Email,DisplayName,Password,Role,Phone,DepartmentName
    /// First row is treated as header and skipped.
    /// </summary>
    [BindProperty]
    public IFormFile? BulkImportFile { get; set; }

    public string? BulkImportResult { get; set; }

    public async Task<IActionResult> OnPostBulkImportAsync()
    {
        await OnGetAsync();

        if (BulkImportFile == null || BulkImportFile.Length == 0)
        {
            Error = "Please select a CSV file to import.";
            return Page();
        }

        if (!BulkImportFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            Error = "Only CSV files are supported.";
            return Page();
        }

        // Max 5MB file size
        if (BulkImportFile.Length > 5 * 1024 * 1024)
        {
            Error = "File too large. Maximum 5MB.";
            return Page();
        }

        var companyId = _companyContext.GetCompanyIdOrThrow();
        var lines = new List<string>();

        using (var reader = new StreamReader(BulkImportFile.OpenReadStream(), Encoding.UTF8))
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }
        }

        if (lines.Count < 2) // header + at least 1 row
        {
            Error = "CSV must have a header row and at least one data row.";
            return Page();
        }

        // Skip header
        var created = 0;
        var skipped = 0;
        var errors = new List<string>();

        for (int i = 1; i < lines.Count && i <= 500; i++) // Max 500 users per import
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 3)
            {
                errors.Add($"Row {i + 1}: insufficient columns (need at least Email,DisplayName,Password).");
                continue;
            }

            var email = parts[0].Trim().Trim('"');
            var displayName = parts[1].Trim().Trim('"');
            var password = parts[2].Trim().Trim('"');
            var roleStr = parts.Length > 3 ? parts[3].Trim().Trim('"') : "Employee";
            var phone = parts.Length > 4 ? parts[4].Trim().Trim('"') : null;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(password))
            {
                errors.Add($"Row {i + 1}: Email, DisplayName, and Password are required.");
                continue;
            }

            if (!email.Contains('@'))
            {
                errors.Add($"Row {i + 1}: Invalid email format '{email}'.");
                continue;
            }

            if (password.Length < 12)
            {
                errors.Add($"Row {i + 1}: Password must be at least 12 characters.");
                continue;
            }

            if (await _db.Users.AnyAsync(u => u.Email == email))
            {
                skipped++;
                continue;
            }

            if (!Enum.TryParse<UserRole>(roleStr, ignoreCase: true, out var role))
                role = UserRole.Employee;

            // Don't allow bulk creation of Owner/Director
            if (role == UserRole.Owner || role == UserRole.Director)
                role = UserRole.Employee;

            var (hash, salt) = PasswordHasher.CreateHash(password);
            var user = new AppUser
            {
                Email = email,
                DisplayName = displayName,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = role,
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
                CompanyId = companyId,
                IsActive = true,
                MustChangePassword = true // Force password change on first login
            };

            _db.Users.Add(user);
            created++;
        }

        if (created > 0)
            await _db.SaveChangesAsync();

        var resultParts = new List<string>();
        resultParts.Add($"Created: {created}");
        if (skipped > 0) resultParts.Add($"Skipped (existing): {skipped}");
        if (errors.Count > 0) resultParts.Add($"Errors: {errors.Count}");
        BulkImportResult = string.Join(" | ", resultParts);
        if (errors.Count > 0)
            BulkImportResult += "\n" + string.Join("\n", errors.Take(10));

        _logger.LogInformation("Bulk import completed: {Created} created, {Skipped} skipped, {Errors} errors for company {CompanyId}",
            created, skipped, errors.Count, companyId);

        return Page();
    }
}
