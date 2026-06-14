using System.Globalization;
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

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManagerHomeAccess policy;
// Owner-only paths are gated by AdminAccess grant check; grant-scoped queries enforce per-company access;
// hierarchy data (Molecules, JobTypes, Companies) is reference data for dropdowns, not sensitive
[Authorize(Policy = "Grant:ManagerHomeAccess")]
public partial class UsersModel : LocalizedPageModel
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
    private readonly IRoleService _roleService;
    private readonly IJobTypeService _jobTypeService;
    private readonly IConcurrencyService _concurrencyService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IHierarchyService _hierarchyService;
    private readonly IUserCompanyTransferService _userCompanyTransferService;
    private readonly IShiftCategoryService _categoryService;
    private readonly ICompanyMembershipService _companyMembershipService;

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
        IGrantService grantService,
        IRoleService roleService,
        IJobTypeService jobTypeService,
        IConcurrencyService concurrencyService,
        ITenantResolver tenantResolver,
        IHierarchyService hierarchyService,
        IUserCompanyTransferService userCompanyTransferService,
        IShiftCategoryService categoryService,
        ICompanyMembershipService companyMembershipService)
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
        _roleService = roleService;
        _jobTypeService = jobTypeService;
        _concurrencyService = concurrencyService;
        _tenantResolver = tenantResolver;
        _hierarchyService = hierarchyService;
        _userCompanyTransferService = userCompanyTransferService;
        _categoryService = categoryService;
        _companyMembershipService = companyMembershipService;
    }

    public record UserVM(int Id, string DisplayName, string Email, string CompanyName, string Role, bool IsActive, bool IsLocked, DateTime? LockoutEnd, int? JobTypeId, string? JobTypeName, string? JobTypeKey, string? DepartmentName, int GrantsCount, int? RoleTemplateId, bool DoesShifts, string? ShiftCategoryNames, List<int> ShiftCategoryIds, int? MoleculeId, int CompanyId = 0, IReadOnlyList<UserMembershipVM>? Memberships = null, AccountType AccountType = AccountType.Standard);
    public record UserMembershipVM(int MembershipId, int CompanyId, string CompanyName, bool IsPrimary, string? RoleName, string? JobTypeName, bool DoesShifts, int? RoleTemplateId = null, int? JobTypeId = null);
    public record JoinRequestVM(int Id, string Email, string DisplayName, string CompanyName, string RequestedRole, string? JobTypeName, string? JobTypeKey, DateTime CreatedAt, JoinRequestStatus Status, int? RequestedRoleTemplateId, string? AuthMethod);
    public record MoleculeOption(int Id, string Name, string AreaName);
    public record JobTypeOption(int Id, string Name, string AreaName, string? Key);
    public record CategoryOption(int Id, string Name);
    public record AccountTypeOption(int Value, string Label);

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

    /// <summary>Tooltip data: director user ID → list of managed company names</summary>
    public Dictionary<int, List<string>> DirectorCompanyNames { get; set; } = new();
    public List<JobTypeOption> AvailableJobTypes { get; set; } = new();
    public List<AccountTypeOption> AvailableAccountTypes { get; set; } = new();
    // Shift categories available per molecule (for the per-user category multi-select).
    public Dictionary<int, List<CategoryOption>> AvailableCategoriesByMolecule { get; set; } = new();
    public Dictionary<int, string> MoleculeNames { get; set; } = new();

    /// <summary>Maps companyId → list of valid jobTypeIds, for client-side filtering in the add-user form.</summary>
    public Dictionary<int, List<int>> JobTypesByCompany { get; set; } = new();

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

    // Template-based assignable roles (dynamic from DB)
    public List<RoleTemplate> AssignableRoleTemplates { get; set; } = new();

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
    [BindProperty] public int? NewRoleTemplateId { get; set; }

    // Owner cross-company user management
    [BindProperty]
    public int? NewUserCompanyId { get; set; }

    // Job type for new user
    [BindProperty]
    public int? NewJobTypeId { get; set; }

    // Molecule for Director HQ auto-assignment
    [BindProperty]
    public int? NewMoleculeId { get; set; }

    public List<Company> Companies { get; set; } = new();

    public bool IsOwner { get; set; }

    /// <summary>
    /// Set when a normal delete was blocked by FK constraints (error code 19).
    /// The view uses this to render the "Force delete anyway" option.
    /// </summary>
    public int? BlockedDeleteUserId { get; set; }
    public string? BlockedDeleteUserName { get; set; }

    // Batch approval properties
    [BindProperty]
    public List<int> SelectedRequests { get; set; } = new();

    public Dictionary<int, UserRole> RequestRoles { get; set; } = new();
    public Dictionary<int, int> RequestTemplateIds { get; set; } = new();

    public async Task OnGetAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            LogInvalidNameIdClaim(_logger);
            return;
        }

        var currentUser = await _db.Users.FindAsync(currentUserId);
        if (currentUser == null)
        {
            LogUserNotFoundInDatabase(_logger, currentUserId);
            return;
        }

        // ✅ Grant-based: Determine Owner status via AdminAccess grant
        IsOwner = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");

        // Load companies for user creation form (exclude HQ — auto-assigned for Directors)
        if (IsOwner)
        {
            Companies = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => !c.IsHeadquarters)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        else
        {
            // Non-owner admins: populate from their accessible companies (resolved below)
            // Deferred — will be set after accessibleCompanyIds is determined
        }

        // ✅ Grant-based: Determine accessible company IDs via ManageJoinRequests grant scope
        var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "ManageJoinRequests");

        // For directors without ManageJoinRequests grant, fall back to DirectorHubAccess scope
        // which correctly resolves all companies in their molecule
        var isDirectorUser = await _grantService.HasGrantAsync(currentUserId, "DirectorHubAccess");
        if (!accessibleCompanyIds.Any() && isDirectorUser)
        {
            accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "DirectorHubAccess");
        }

        // For directors: also build a list that includes HQ for join request queries.
        // Tech users' join requests have CompanyId = HQ (see UserJoinRequest.cs:51),
        // so excluding HQ makes them invisible to directors.
        var joinRequestCompanyIds = accessibleCompanyIds;
        if (isDirectorUser && accessibleCompanyIds.Any())
        {
            // accessibleCompanyIds may already include HQ; ensure it does for join requests
            var hqCompanyIds = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => accessibleCompanyIds.Contains(c.Id) && c.IsHeadquarters)
                .Select(c => c.Id)
                .ToListAsync();

            if (!hqCompanyIds.Any())
            {
                // HQ wasn't in the grant-resolved list; find HQ for the director's molecule
                var directorMoleculeId = _tenantResolver.GetDirectorSelectedMoleculeId();
                if (directorMoleculeId.HasValue)
                {
                    var hqForMolecule = await _db.Companies
                        .IgnoreQueryFilters()
                        .Where(c => c.MoleculeId == directorMoleculeId.Value && c.IsHeadquarters)
                        .Select(c => c.Id)
                        .FirstOrDefaultAsync();
                    if (hqForMolecule > 0)
                    {
                        joinRequestCompanyIds = accessibleCompanyIds.Append(hqForMolecule).ToList();
                    }
                }
            }
            // else: HQ already in list, joinRequestCompanyIds = accessibleCompanyIds (includes HQ)

            // For user display, exclude HQ (directors manage real companies, not HQ placeholders)
            accessibleCompanyIds = accessibleCompanyIds.Where(id => !hqCompanyIds.Contains(id)).ToList();
        }

        // Final fall back to user's own company if still empty
        if (!accessibleCompanyIds.Any())
        {
            accessibleCompanyIds = new List<int> { currentUser.CompanyId };
            joinRequestCompanyIds = accessibleCompanyIds;
        }

        // Populate Companies for non-owner admins (for user creation form)
        if (!IsOwner && !Companies.Any())
        {
            Companies = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => accessibleCompanyIds.Contains(c.Id) && !c.IsHeadquarters)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        // Auto-default molecule filter for directors viewing the page
        if (isDirectorUser && !UserFilterMoleculeId.HasValue && !IsOwner)
        {
            UserFilterMoleculeId = _tenantResolver.GetDirectorSelectedMoleculeId();
        }

        // Load join requests with filters and scoping
        // IgnoreQueryFilters: tenant filter would restrict to admin's own company,
        // but join requests need cross-company visibility scoped by joinRequestCompanyIds
        // (includes HQ for directors so tech join requests are visible)
        var joinRequestsQuery = _db.UserJoinRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(jr => joinRequestCompanyIds.Contains(jr.CompanyId))
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
            .Include(jr => jr.JobType)
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
                companies.TryGetValue(jr.CompanyId, out var company) ? company.LocalizedName : $"Company #{jr.CompanyId}",
                jr.RequestedRole.ToString(),
                jr.JobType?.DisplayName,
                jr.JobType?.Name,
                jr.CreatedAt,
                jr.Status,
                jr.RequestedRoleTemplateId,
                jr.AuthMethod
            ))
            .ToList();

        // Load available companies for filter dropdown (exclude HQ)
        if (IsOwner)
        {
            AvailableCompanies = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => !c.IsHeadquarters)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        else
        {
            // SECURITY-AUDITED: SAFE — scoped by accessibleCompanyIds from grant resolution;
            // IgnoreQueryFilters needed for directors who manage companies outside their own tenant
            AvailableCompanies = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => accessibleCompanyIds.Contains(c.Id) && !c.IsHeadquarters)
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
        var allJobTypesWithArea = await _jobTypeService.GetAllJobTypesWithAreaAsync();
        AvailableJobTypes = allJobTypesWithArea
            .Where(jt => jt.IsActive)
            .OrderBy(jt => jt.Area?.Name).ThenBy(jt => jt.Name)
            .Select(jt => new JobTypeOption(jt.Id, jt.DisplayName, jt.Area?.DisplayName ?? "", jt.Name))
            .ToList();

        // Populate account type options with localized labels
        AvailableAccountTypes = new List<AccountTypeOption>
        {
            new((int)AccountType.Standard, _localizer["AccountType_Standard"].Value),
            new((int)AccountType.Mil,      _localizer["AccountType_Mil"].Value),
            new((int)AccountType.GroupUser, _localizer["AccountType_GroupUser"].Value),
        };

        // Build companyId→validJobTypeIds mapping for add-user form dynamic filtering.
        // Uses same logic as JobTypeService.GetJobTypesForMoleculeAsync but computed in bulk.
        if (Companies.Any())
        {
            var moleculeIds = Companies.Where(c => c.MoleculeId.HasValue).Select(c => c.MoleculeId!.Value).Distinct().ToList();
            var molecules = await _db.Molecules
                .IgnoreQueryFilters()
                .Where(m => moleculeIds.Contains(m.Id))
                .Select(m => new { m.Id, m.AreaId, m.Type, m.DisplayName })
                .ToListAsync();
            var moleculeLookup = molecules.ToDictionary(m => m.Id);
            MoleculeNames = molecules.ToDictionary(m => m.Id, m => m.DisplayName);
            var activeJobTypes = allJobTypesWithArea.Where(jt => jt.IsActive).ToList();

            foreach (var company in Companies)
            {
                if (!company.MoleculeId.HasValue || !moleculeLookup.TryGetValue(company.MoleculeId.Value, out var mol))
                {
                    // No molecule → show all job types
                    JobTypesByCompany[company.Id] = activeJobTypes.Select(jt => jt.Id).ToList();
                    continue;
                }

                var isWorkforce = mol.Type == Models.Support.MoleculeType.Workforce
                                || mol.Type == Models.Support.MoleculeType.Helper;

                JobTypesByCompany[company.Id] = activeJobTypes
                    .Where(jt => jt.AreaId == mol.AreaId
                        && (jt.MoleculeId == null || jt.MoleculeId == mol.Id)
                        && (isWorkforce || !jt.IsWorkforceOnly))
                    .Select(jt => jt.Id)
                    .ToList();
            }
        }

        // Load shift categories per molecule for the per-user category multi-select.
        // Scoped to molecules the admin manages to prevent cross-molecule assignment.
        var accessibleMoleculeIds = Companies
            .Where(c => c.MoleculeId.HasValue)
            .Select(c => c.MoleculeId!.Value)
            .Distinct()
            .ToList();
        if (accessibleMoleculeIds.Count > 0)
        {
            AvailableCategoriesByMolecule = (await _db.ShiftCategories
                .Where(c => accessibleMoleculeIds.Contains(c.MoleculeId) && c.IsActive)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.DisplayName)
                .Select(c => new { c.MoleculeId, c.Id, c.DisplayName })
                .ToListAsync())
                .GroupBy(c => c.MoleculeId)
                .ToDictionary(g => g.Key, g => g.Select(c => new CategoryOption(c.Id, c.DisplayName)).ToList());
        }

        // Load assignable role templates (filtered by CanBeAssignedByDefault and user's grant level)
        AssignableRoleTemplates = await _roleService.GetAssignableRoleTemplatesAsync();

        // Filter templates by what the current user can assign (DerivedUserRole check).
        // Materialized loop instead of LINQ .Where() because CanAssignRoleAsync is async — see Batch D.
        var filteredTemplates = new List<RoleTemplate>(AssignableRoleTemplates.Count);
        foreach (var rt in AssignableRoleTemplates)
        {
            if (!rt.DerivedUserRole.HasValue || await _directorService.CanAssignRoleAsync(rt.DerivedUserRole.Value))
            {
                filteredTemplates.Add(rt);
            }
        }
        AssignableRoleTemplates = filteredTemplates;

        // Load existing users with filters
        IQueryable<AppUser> usersQuery;
        if (IsOwner)
        {
            // Owner sees ALL users across all companies
            // SECURITY-AUDITED: IgnoreQueryFilters propagates to PrimaryShiftType Include (cross-tenant FK)
            usersQuery = _db.Users
                .IgnoreQueryFilters()
                .Include(u => u.JobType)
                .Include(u => u.Department)
                .AsNoTracking();
        }
        else
        {
            // Other roles see filtered by accessible companies
            // SECURITY-AUDITED: SAFE — scoped by accessibleCompanyIds from grant resolution;
            // IgnoreQueryFilters needed for directors who manage users across multiple companies;
            // PrimaryShiftType is cross-tenant FK (IgnoreQueryFilters propagates to Include)
            usersQuery = _db.Users
                .IgnoreQueryFilters()
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

        // Batch-load shift-category memberships (id list + display names) for the listed users.
        var userCategoryRows = await _db.UserShiftCategories
            .Where(m => userIds.Contains(m.UserId))
            .Select(m => new { m.UserId, m.ShiftCategoryId, m.ShiftCategory.DisplayName })
            .ToListAsync();
        var userCategoryMap = userCategoryRows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => (Ids: g.Select(r => r.ShiftCategoryId).ToList(),
                      Names: string.Join(", ", g.OrderBy(r => r.DisplayName).Select(r => r.DisplayName))));

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
            ? (await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => allManagedCompanyIds.Contains(c.Id))
                .ToListAsync())
                .ToDictionary(c => c.Id, c => c.LocalizedName)
            : new Dictionary<int, string>();

        // Pre-load molecule names for directors (resolve via company → molecule)
        var directorMoleculeLookup = new Dictionary<int, string>();
        if (directorUserIds.Any())
        {
            var directorCompanyIds = userData
                .Where(u => u.Role == UserRole.Director)
                .Select(u => u.CompanyId)
                .Distinct()
                .ToList();

            directorMoleculeLookup = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => directorCompanyIds.Contains(c.Id) && c.MoleculeId.HasValue)
                .Include(c => c.Molecule)
                .Where(c => c.Molecule != null)
                .ToDictionaryAsync(c => c.Id, c => c.Molecule!.Name);
        }

        // Batch-load company memberships for all displayed users (single query).
        // IgnoreQueryFilters: CompanyMembership is not IBelongsToCompany so there is no tenant filter,
        // but IgnoreQueryFilters is applied for consistency with other cross-tenant lookups here.
        var membershipRows = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => userIds.Contains(m.UserId) && !m.IsDeleted)
            .ToListAsync();

        // Resolve company names for all companies referenced by memberships (beyond userCompanies).
        var membershipCompanyIds = membershipRows.Select(m => m.CompanyId).Distinct()
            .Where(id => !userCompanies.ContainsKey(id)).ToList();
        Dictionary<int, string> extraCompanyNames = new();
        if (membershipCompanyIds.Any())
        {
            extraCompanyNames = await _db.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => membershipCompanyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.LocalizedName);
        }

        // Resolve job type display names for all job type IDs referenced by memberships.
        var membershipJobTypeIds = membershipRows
            .Where(m => m.JobTypeId.HasValue)
            .Select(m => m.JobTypeId!.Value).Distinct().ToList();
        Dictionary<int, string> membershipJobTypeNames = new();
        if (membershipJobTypeIds.Any())
        {
            membershipJobTypeNames = await _db.JobTypes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(jt => membershipJobTypeIds.Contains(jt.Id))
                .ToDictionaryAsync(jt => jt.Id, jt => jt.DisplayName);
        }

        // Resolve role template names for all role template IDs referenced by memberships.
        var membershipRoleTemplateIds = membershipRows
            .Where(m => m.RoleTemplateId.HasValue)
            .Select(m => m.RoleTemplateId!.Value).Distinct().ToList();
        Dictionary<int, string> membershipRoleNames = new();
        if (membershipRoleTemplateIds.Any())
        {
            var roleTemplates = await _db.Set<RoleTemplate>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(rt => membershipRoleTemplateIds.Contains(rt.Id))
                .ToListAsync();
            foreach (var rt in roleTemplates)
            {
                // Prefer DisplayNameEN; fall back to NameKey localization.
                var displayName = string.IsNullOrEmpty(rt.DisplayNameEN)
                    ? _localizer[rt.NameKey].Value
                    : rt.DisplayNameEN;
                membershipRoleNames[rt.Id] = displayName;
            }
        }

        // Helper: resolve company name from the combined lookups.
        string ResolveMembershipCompanyName(int companyId)
        {
            if (userCompanies.TryGetValue(companyId, out var co)) return co.LocalizedName;
            if (extraCompanyNames.TryGetValue(companyId, out var name)) return name;
            return $"Company #{companyId}";
        }

        // Group memberships by userId, building UserMembershipVM list (primary first).
        var membershipsByUser = membershipRows
            .GroupBy(m => m.UserId)
            .ToDictionary(g => g.Key, g =>
                (IReadOnlyList<UserMembershipVM>)g
                    .OrderByDescending(m => m.IsPrimary)
                    .Select(m => new UserMembershipVM(
                        m.Id,
                        m.CompanyId,
                        ResolveMembershipCompanyName(m.CompanyId),
                        m.IsPrimary,
                        m.RoleTemplateId.HasValue && membershipRoleNames.TryGetValue(m.RoleTemplateId.Value, out var rn) ? rn : null,
                        m.JobTypeId.HasValue && membershipJobTypeNames.TryGetValue(m.JobTypeId.Value, out var jtn) ? jtn : null,
                        m.DoesShifts,
                        m.RoleTemplateId,
                        m.JobTypeId))
                    .ToList());

        // Build user list — directors get a single row with molecule scope display
        var userList = new List<UserVM>();

        foreach (var u in userData)
        {
            if (u.Role == UserRole.Director)
            {
                var managedCompanyIds = directorCompanyMap.TryGetValue(u.Id, out var ids) ? ids : new List<int>();
                var companyCount = managedCompanyIds.Count;

                // Resolve molecule name from the director's home company
                var moleculeName = directorMoleculeLookup.TryGetValue(u.CompanyId, out var molName)
                    ? molName
                    : "?";

                // Build display: "אלחוט (3 דסקים)" with tooltip listing company names
                var scopeDisplay = string.Format(
                    _localizer["Users_DirectorMoleculeScope"],
                    moleculeName,
                    companyCount);

                // Build tooltip data: list of managed company names
                var companyNames = managedCompanyIds
                    .Select(id => managedCompaniesLookup.TryGetValue(id, out var n) ? n : $"#{id}")
                    .ToList();
                DirectorCompanyNames[u.Id] = companyNames;

                userList.Add(new UserVM(
                    u.Id,
                    u.DisplayName,
                    u.Email,
                    scopeDisplay,
                    u.Role.ToString(),
                    u.IsActive,
                    u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTime.UtcNow,
                    u.LockoutEnd,
                    u.JobTypeId,
                    u.JobType?.DisplayName,
                    u.JobType?.Name,
                    u.Department?.DisplayName,
                    userGrantCounts.TryGetValue(u.Id, out var gc) ? gc : 0,
                    u.RoleTemplateId,
                    u.DoesShifts,
                    userCategoryMap.TryGetValue(u.Id, out var dirCat) ? dirCat.Names : null,
                    userCategoryMap.TryGetValue(u.Id, out var dirCat2) ? dirCat2.Ids : new List<int>(),
                    userCompanies.TryGetValue(u.CompanyId, out var dirCompany) ? dirCompany.MoleculeId : null,
                    u.CompanyId,
                    membershipsByUser.TryGetValue(u.Id, out var dirMem) ? dirMem : null,
                    u.AccountType
                ));
            }
            else
            {
                // For non-Directors, use their primary company
                // Apply company filter if specified
                if (!UserFilterCompanyId.HasValue || u.CompanyId == UserFilterCompanyId.Value)
                {
                    // ✅ FIX: Use TryGetValue to prevent KeyNotFoundException if company is missing
                    var companyName = userCompanies.TryGetValue(u.CompanyId, out var company)
                        ? company.LocalizedName
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
                        u.JobType?.Name,
                        u.Department?.DisplayName,
                        userGrantCounts.TryGetValue(u.Id, out var gc) ? gc : 0,
                        u.RoleTemplateId,
                        u.DoesShifts,
                        userCategoryMap.TryGetValue(u.Id, out var ndCat) ? ndCat.Names : null,
                        userCategoryMap.TryGetValue(u.Id, out var ndCat2) ? ndCat2.Ids : new List<int>(),
                        company?.MoleculeId,
                        u.CompanyId,
                        membershipsByUser.TryGetValue(u.Id, out var ndMem) ? ndMem : null,
                        u.AccountType
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

        // SECURITY-AUDITED: IgnoreQueryFilters for global email uniqueness — email is the login identifier.
        // Case-insensitive comparison: prevents admins from accidentally creating "Foo@x.com" when
        // "foo@x.com" already exists (which would later collide at login when the case-insensitive
        // lookup finds 2 matches and FirstOrDefaultAsync returns non-deterministically).
        var newEmailLower = NewEmail.ToLowerInvariant();
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == newEmailLower)) { Error = _localizer["Error_EmailAlreadyExists"]; return Page(); }

        // Resolve role template and derive UserRole
        RoleTemplate? roleTemplate = null;
        UserRole targetRole;

        if (NewRoleTemplateId.HasValue)
        {
            // Direct template assignment (new path)
            roleTemplate = await _roleService.GetRoleTemplateAsync(NewRoleTemplateId.Value);
            if (roleTemplate == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_InvalidRole"].Value;
                return RedirectToPage();
            }
            targetRole = roleTemplate.DerivedUserRole ?? UserRole.Employee;
        }
        else if (!string.IsNullOrEmpty(NewRole) && Enum.TryParse<UserRole>(NewRole, ignoreCase: true, out var parsedRole))
        {
            // Legacy enum-string fallback
            targetRole = parsedRole;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRole"].Value;
            return RedirectToPage();
        }

        if (!await _directorService.CanAssignRoleAsync(targetRole))
        {
            TempData["ErrorMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Error_NoPermissionAssignRole"], targetRole);
            return RedirectToPage();
        }

        // Director/AreaAdmin HQ auto-resolve: get assigned to the molecule's HQ company
        if (targetRole == UserRole.Director || targetRole == UserRole.AreaAdmin)
        {
            if (!NewMoleculeId.HasValue || NewMoleculeId.Value <= 0)
            {
                TempData["ErrorMessage"] = _localizer["Error_Signup_SelectMolecule"].Value;
                return RedirectToPage();
            }

            var hqCompany = await _db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.MoleculeId == NewMoleculeId.Value && c.IsHeadquarters);

            if (hqCompany == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_Signup_HQNotFound"].Value;
                return RedirectToPage();
            }

            NewUserCompanyId = hqCompany.Id;
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
            var jobType = await _jobTypeService.GetJobTypeAsync(NewJobTypeId.Value);
            if (jobType == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_InvalidJobTypeSelected"].Value;
                return RedirectToPage();
            }
        }

        // If template wasn't loaded via direct ID, resolve via legacy mapping
        if (roleTemplate == null)
        {
            string? jobTypeName = null;
            if (NewJobTypeId.HasValue)
            {
                var jt = await _jobTypeService.GetJobTypeAsync(NewJobTypeId.Value);
                jobTypeName = jt?.Name;
            }
            var roleTemplateKey = MapUserRoleToRoleTemplateKey(targetRole, jobTypeName);
            roleTemplate = await _roleService.GetRoleTemplateByKeyAsync(roleTemplateKey);
        }

        var (h, s) = PasswordHasher.CreateHash(NewPassword);
        var newUser = new AppUser
        {
            CompanyId = targetCompanyId,
            Email = NewEmail,
            DisplayName = NewDisplayName,
            Role = roleTemplate?.DerivedUserRole ?? targetRole,
            RoleTemplateId = roleTemplate?.Id,
            IsActive = true,
            HasCompletedOnboarding = true, // Admin-created users skip onboarding
            PasswordHash = h,
            PasswordSalt = s,
            JobTypeId = NewJobTypeId
        };
        _db.Users.Add(newUser);
        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "AppUser");
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }
        }

        // Assign role template grants with JobType-aware mapping
        var templateKey = roleTemplate?.Key ?? "Employee";
        var grantScope = await BuildGrantScopeForTemplateAsync(templateKey, targetCompanyId, NewJobTypeId);
        var grantsAssigned = await _grantService.AssignRoleTemplateGrantsAsync(newUser.Id, templateKey, grantScope, currentUserIdForCompany);
        LogGrantsAssignedToNewUser(_logger, grantsAssigned, templateKey, newUser.Id);

        // ✅ P0-4/P0-5 FIX: If creating a Director/AreaAdmin, also create DirectorCompany mapping
        if (targetRole == UserRole.Director || targetRole == UserRole.AreaAdmin)
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
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser", newUser.Id);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }

            LogDirectorCompanyMappingForNewDirector(_logger, newUser.Id, targetCompanyId);
        }

        // Audit logging (RoleAssignmentAudit)
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaimForAudit = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaimForAudit, out var currentUserId))
        {
            LogInvalidNameIdClaimUserCreationAudit(_logger);
            currentUserId = 0; // Fallback for audit trail
        }
        _db.RoleAssignmentAudits.Add(new RoleAssignmentAudit
        {
            ChangedBy = currentUserId,
            TargetUserId = newUser.Id,
            FromRole = null,
            ToRole = targetRole,
            FromRoleTemplateId = null,
            ToRoleTemplateId = roleTemplate?.Id,
            CompanyId = targetCompanyId,
            Timestamp = DateTime.UtcNow
        });
        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "AppUser", newUser.Id);
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }
        }

        // Audit logging (general audit log)
        await _auditLogService.LogAsync(
            "UserCreated",
            "User",
            newUser.Id,
            $"Created new user '{newUser.DisplayName}' ({newUser.Email}) with role {targetRole}");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_UserCreated"], NewDisplayName, targetRole);
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
                LogInvalidNameIdClaim(_logger);
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
                return RedirectToPage();
            }

            var isAdmin = await _grantService.HasGrantAsync(toggleCurrentUserId, "AdminAccess");
            var hasEditGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(toggleCurrentUserId, "EditCompanyUsers", u.CompanyId);
            if (!hasEditGrant)
            {
                LogUnauthorizedUserActionWithGrant(_logger, toggleCurrentUserId, "to toggle", id, u.CompanyId);
                TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
                return RedirectToPage();
            }

            var wasActive = u.IsActive;
            u.IsActive = !u.IsActive;

            // Batch F (F-H-013): on DEACTIVATION, clear auth-scoped relationships that lose meaning
            // when the user is inactive. Grants and shift assignments are intentionally NOT touched
            // here — current reactivation flow at the bottom of this method depends on grants
            // staying intact OR being restorable from RoleTemplate; shift assignments are
            // operational data that admins may want preserved across toggle. Both are documented
            // as deferred design-alignment items in 00-index.md Batch F.
            if (wasActive && !u.IsActive)
            {
                // Other users training UNDER this (deactivated) user lose their trainer.
                // Null the FK so the foreign-key constraint stays valid and the trainee isn't
                // pointing at an inactive trainer.
                await _db.ShiftAssignments.IgnoreQueryFilters()
                    .Where(sa => sa.TraineeUserId == id)
                    .ExecuteUpdateAsync(sa => sa.SetProperty(a => a.TraineeUserId, (int?)null));

                // DirectorCompany rows are pure auth-scope mappings — a deactivated user
                // should not retain Director privileges over any company. Mirrors the
                // OnPostDeleteUserAsync cleanup at line 1655.
                // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific userId
                await _db.DirectorCompanies.IgnoreQueryFilters()
                    .Where(dc => dc.UserId == id)
                    .ExecuteDeleteAsync();

                // Per-user calendar ordering is a personal UI preference — drop it on deactivation.
                // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific userId; entity is not company-scoped.
                await _db.UserCalendarRowOrders.IgnoreQueryFilters()
                    .Where(o => o.UserId == id)
                    .ExecuteDeleteAsync();
            }

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "AppUser", id);
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }

            // Reactivation: restore grants from RoleTemplate if user has no grants
            if (u.IsActive && u.RoleTemplateId.HasValue)
            {
                var grantCount = await _db.Grants.IgnoreQueryFilters().Where(g => g.UserId == u.Id).CountAsync();
                if (grantCount == 0)
                {
                    LogReactivatedUserHasNoGrants(_logger, u.Id, u.RoleTemplateId.Value);
                    try
                    {
                        var hierarchyContext = await _hierarchyService.GetUserHierarchyContextAsync(u.Id);
                        var roleScope = new GrantScope(
                            ProjectId: hierarchyContext?.Path.Project?.Id,
                            AreaId: hierarchyContext?.Path.Area?.Id,
                            MoleculeId: hierarchyContext?.Path.Molecule?.Id,
                            DepartmentId: u.DepartmentId,
                            CompanyId: u.CompanyId,
                            JobTypeId: hierarchyContext?.JobType?.Id
                        );
                        await _grantService.ApplyAutoGrantsAsync(u.Id, u.RoleTemplateId.Value, roleScope);
                        LogGrantsRestoredForReactivatedUser(_logger, u.Id);
                    }
                    catch (Exception ex)
                    {
                        LogFailedToRestoreGrants(_logger, ex, u.Id);
                        // Non-fatal: user is still reactivated, grants can be manually assigned
                    }
                }
            }

            // FINDING-008 FIX: Audit log for user enable/disable
            await _auditLogService.LogAsync(
                u.IsActive ? "UserEnabled" : "UserDisabled",
                "User", u.Id,
                $"User {u.Email} {(u.IsActive ? "enabled" : "disabled")} by admin");

            // Notify the affected user of the activation change. Deactivation is security-critical
            // (always emails) so the user learns of lost access even in Quiet/muted mode.
            if (u.IsActive)
            {
                await _notificationService.NotifyAsync(u.Id, NotificationType.AccountReactivated,
                    ShiftManager.Services.Notifications.NotificationCategory.Account,
                    _localizer["Notif_AccountReactivatedTitle"].Value, _localizer["Notif_AccountReactivatedMessage"].Value,
                    personallyActionable: true);
            }
            else
            {
                await _notificationService.NotifyAsync(u.Id, NotificationType.AccountDeactivated,
                    ShiftManager.Services.Notifications.NotificationCategory.AccountSecurity,
                    _localizer["Notif_AccountDeactivatedTitle"].Value, _localizer["Notif_AccountDeactivatedMessage"].Value,
                    personallyActionable: true, securityCritical: true);
            }
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRoleAsync(int id, int roleTemplateId, string? role = null)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"].Value;
            return RedirectToPage();
        }

        // Resolve target role from template ID or legacy enum string
        RoleTemplate? selectedTemplate = null;
        UserRole targetRole;

        if (roleTemplateId > 0)
        {
            // Direct template assignment (new path)
            selectedTemplate = await _roleService.GetRoleTemplateAsync(roleTemplateId);
            if (selectedTemplate == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_InvalidRole"].Value;
                return RedirectToPage();
            }
            if (!selectedTemplate.DerivedUserRole.HasValue)
                LogRoleTemplateMissingDerivedRole(_logger, selectedTemplate.Key, selectedTemplate.Id);
            targetRole = selectedTemplate.DerivedUserRole ?? UserRole.Employee;
        }
        else if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole))
        {
            // Legacy fallback
            targetRole = parsedRole;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRole"].Value;
            return RedirectToPage();
        }

        if (!await _directorService.CanAssignRoleAsync(targetRole))
        {
            TempData["ErrorMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Error_NoPermissionAssignRole"], targetRole);
            return RedirectToPage();
        }

        var u = await _db.Users.IgnoreQueryFilters().Include(x => x.JobType).FirstOrDefaultAsync(x => x.Id == id);
        if (u != null)
        {
            var oldRole = u.Role;

            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                LogInvalidNameIdClaim(_logger);
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
                return RedirectToPage();
            }

            // HIGH-004 FIX: Grant-based company scope check
            var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
            var hasRoleEditGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(currentUserId, "EditCompanyUsers", u.CompanyId);
            if (!hasRoleEditGrant)
            {
                LogUnauthorizedUserActionWithGrant(_logger, currentUserId, "role change on", id, u.CompanyId);
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

                    LogShadowingAssignmentsCanceled(_logger, canceledCount, id, oldRole, targetRole);
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
                    TempData["ErrorMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Error_CannotChangeToTrainee_ActiveShifts"], activeShiftsCount);
                    return RedirectToPage();
                }

                // Check if they have trainees shadowing them
                var traineeShadowingCount = await _db.ShiftAssignments
                    .Include(sa => sa.ShiftInstance)
                    .Where(sa => sa.UserId == id && sa.TraineeUserId != null && sa.ShiftInstance.WorkDate >= today)
                    .CountAsync();

                if (traineeShadowingCount > 0)
                {
                    TempData["ErrorMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Error_CannotChangeToTrainee_ShadowingTrainees"], traineeShadowingCount);
                    return RedirectToPage();
                }
            }

            var oldRoleTemplateId = u.RoleTemplateId;
            u.Role = targetRole;

            // Set RoleTemplateId — use direct template if available, otherwise resolve via mapping
            if (selectedTemplate != null)
            {
                u.RoleTemplateId = selectedTemplate.Id;
                if (selectedTemplate.DerivedUserRole.HasValue)
                    u.Role = selectedTemplate.DerivedUserRole.Value;
            }
            else
            {
                var templateKey = MapUserRoleToRoleTemplateKey(targetRole, u.JobType?.Name);
                var template = await _roleService.GetRoleTemplateByKeyAsync(templateKey);
                if (template != null)
                {
                    u.RoleTemplateId = template.Id;
                    if (template.DerivedUserRole.HasValue)
                        u.Role = template.DerivedUserRole.Value;
                }
            }
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser", u.Id);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }

            // FINDING-009 FIX: Reconcile grants on role change to prevent privilege escalation
            var oldTemplateIdForGrants = oldRoleTemplateId;
            if (!oldTemplateIdForGrants.HasValue)
            {
                // Fallback for legacy users without RoleTemplateId: resolve from old role enum
                var oldTemplateKeyForGrants = MapUserRoleToRoleTemplateKey(oldRole, u.JobType?.Name);
                var oldTemplateForGrants = await _roleService.GetRoleTemplateByKeyAsync(oldTemplateKeyForGrants);
                oldTemplateIdForGrants = oldTemplateForGrants?.Id;
            }
            if (oldTemplateIdForGrants.HasValue)
            {
                await _grantService.RemoveAutoGrantsAsync(u.Id, oldTemplateIdForGrants.Value);
                LogAutoGrantsRemovedDuringRoleChange(_logger, oldTemplateIdForGrants.Value, u.Id);
            }

            // Assign new role's auto-grants
            var newGrantTemplateKey = selectedTemplate?.Key ?? MapUserRoleToRoleTemplateKey(targetRole, u.JobType?.Name);
            var newGrantScope = await BuildGrantScopeForTemplateAsync(newGrantTemplateKey, u.CompanyId, u.JobTypeId);
            var grantsAssigned = await _grantService.AssignRoleTemplateGrantsAsync(u.Id, newGrantTemplateKey, newGrantScope, currentUserId);
            LogGrantsAssignedDuringRoleChange(_logger, grantsAssigned, newGrantTemplateKey, u.Id);

            // ✅ P0-4/P0-5 FIX: If changing TO Director/AreaAdmin, create DirectorCompany mapping
            if (oldRole != UserRole.Director && oldRole != UserRole.AreaAdmin &&
                (targetRole == UserRole.Director || targetRole == UserRole.AreaAdmin))
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
                    {
                        var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                            () => _db.SaveChangesAsync(), "AppUser", u.Id);
                        if (!saveResult.Success)
                        {
                            Error = _localizer["Error_ConcurrencyConflict"];
                            return RedirectToPage();
                        }
                    }

                    LogDirectorCompanyMappingPromoted(_logger, u.Id, u.CompanyId);
                }
            }

            // Audit logging
            _db.RoleAssignmentAudits.Add(new RoleAssignmentAudit
            {
                ChangedBy = currentUserId,
                TargetUserId = u.Id,
                FromRole = oldRole,
                ToRole = u.Role,
                FromRoleTemplateId = oldRoleTemplateId,
                ToRoleTemplateId = u.RoleTemplateId,
                CompanyId = u.CompanyId,
                Timestamp = DateTime.UtcNow
            });
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser", u.Id);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }

            // Notify the affected user of their role change (impactful account change).
            await _notificationService.NotifyAsync(u.Id, NotificationType.RoleChanged,
                ShiftManager.Services.Notifications.NotificationCategory.Account,
                _localizer["Notif_RoleChangedTitle"].Value, _localizer["Notif_RoleChangedMessage"].Value,
                personallyActionable: true);

            TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_RoleUpdated"], targetRole, u.DisplayName);
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
            LogInvalidNameIdClaim(_logger);
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }

        var isAdmin = await _grantService.HasGrantAsync(jobTypeCurrentUserId, "AdminAccess");
        var hasJobTypeGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(jobTypeCurrentUserId, "EditCompanyUsers", u.CompanyId);
        if (!hasJobTypeGrant)
        {
            LogUnauthorizedUserActionWithGrant(_logger, jobTypeCurrentUserId, "job type change on", id, u.CompanyId);
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
            return RedirectToPage();
        }

        // Validate job type if specified
        string? jobTypeName = null;
        if (jobTypeId.HasValue)
        {
            var jobType = await _jobTypeService.GetJobTypeAsync(jobTypeId.Value);
            if (jobType == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_InvalidJobTypeSelected"].Value;
                return RedirectToPage();
            }

            // Validate job type is valid for user's area/molecule
            var validation = await _jobTypeService.ValidateJobTypeForUserAsync(id, jobTypeId.Value);
            if (validation != JobTypeValidationResult.Valid)
            {
                TempData["ErrorMessage"] = validation switch
                {
                    JobTypeValidationResult.MoleculeMismatch => _localizer["Error_JobTypeNotAvailableForMolecule"].Value,
                    _ => _localizer["Error_InvalidJobTypeSelected"].Value
                };
                return RedirectToPage();
            }

            jobTypeName = jobType.DisplayName;
        }

        var oldJobTypeId = u.JobTypeId;
        u.JobTypeId = jobTypeId;
        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "AppUser", id);
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }
        }

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

        // Notify the affected user of their job-type change (impactful account change).
        await _notificationService.NotifyAsync(u.Id, NotificationType.JobTypeChanged,
            ShiftManager.Services.Notifications.NotificationCategory.Account,
            _localizer["Notif_JobTypeChangedTitle"].Value, _localizer["Notif_JobTypeChangedMessage"].Value,
            personallyActionable: true);

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_JobTypeUpdated"], u.DisplayName, jobTypeName ?? "-");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAccountTypeAsync(int id, int accountType)
    {
        // Input validation
        if (id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserId"].Value;
            return RedirectToPage();
        }

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company user lookup.
        var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (u == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value;
            return RedirectToPage();
        }

        // Grant-based company scope check — mirrors OnPostJobTypeAsync exactly
        var accountTypeUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(accountTypeUserIdClaim, out var accountTypeCurrentUserId))
        {
            LogInvalidNameIdClaim(_logger);
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }

        var isAccountTypeAdmin = await _grantService.HasGrantAsync(accountTypeCurrentUserId, "AdminAccess");
        var hasAccountTypeGrant = isAccountTypeAdmin || await _grantService.HasGrantForCompanyAsync(accountTypeCurrentUserId, "EditCompanyUsers", u.CompanyId);
        if (!hasAccountTypeGrant)
        {
            LogUnauthorizedUserActionWithGrant(_logger, accountTypeCurrentUserId, "account type change on", id, u.CompanyId);
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
            return RedirectToPage();
        }

        // Validate the enum value
        if (!Enum.IsDefined(typeof(AccountType), accountType))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidJobTypeSelected"].Value;
            return RedirectToPage();
        }

        var oldAccountType = u.AccountType;
        u.AccountType = (AccountType)accountType;
        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "AppUser", id);
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }
        }

        // Audit logging — mirrors OnPostJobTypeAsync argument shape
        var auditUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(auditUserIdClaim, out var auditCurrentUserId))
        {
            auditCurrentUserId = 0; // Fallback: record exists but without actor attribution
        }

        await _auditLogService.LogUserActionAsync(
            userId: auditCurrentUserId,
            action: "AccountTypeChanged",
            entityType: "User",
            entityId: u.Id,
            description: $"Changed account type for {u.DisplayName} from {oldAccountType} to {(AccountType)accountType}"
        );

        // ── Capability-enforcement cleanup ────────────────────────────────────
        // When the new type restricts what assignments the user may legally hold,
        // remove FUTURE assignments (dated >= today) that are no longer permitted.
        // Past/historical assignments are never touched (operational history).
        //
        // Capability matrix:
        //   Mil       → no chores  (does shifts, does on-call)
        //   GroupUser → no shifts, no chores, no on-call
        //   Standard  → no restriction — nothing to clean up
        //
        // Shift removal mirrors OnPostDoesShiftsAsync: vacate by setting UserId = null
        //   (the slot row is kept for calendar display; the same pattern as DoesShifts toggle).
        // Chore/OnDuty removal mirrors ChoreService/OnDutyService: soft-delete via CanceledAt
        //   (consistent with how these entities are "deleted" everywhere except hard user-delete).
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific userId
        var newType = (AccountType)accountType;
        bool cleanChores = newType is AccountType.Mil or AccountType.GroupUser;
        bool cleanShifts = newType is AccountType.GroupUser;
        bool cleanOnCall = newType is AccountType.GroupUser;

        if (cleanChores || cleanShifts || cleanOnCall)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var now = DateTime.UtcNow;
            int vacatedShifts = 0;
            int canceledChores = 0;
            int canceledOnCall = 0;

            if (cleanShifts)
            {
                // Vacate future shift assignments — mirrors DoesShiftsAsync pattern exactly.
                var futureShifts = await _db.ShiftAssignments.IgnoreQueryFilters()
                    .Include(sa => sa.ShiftInstance)
                    .Where(sa => sa.UserId == id && sa.ShiftInstance!.WorkDate >= today)
                    .ToListAsync();
                foreach (var sa in futureShifts) { sa.UserId = null; sa.TraineeUserId = null; }
                vacatedShifts = futureShifts.Count;
            }

            if (cleanChores)
            {
                // Soft-cancel future chores — mirrors ChoreService.CancelChoreAsync pattern.
                var futureChores = await _db.Chores.IgnoreQueryFilters()
                    .Where(c => c.UserId == id && c.Date >= today && c.CanceledAt == null)
                    .ToListAsync();
                foreach (var c in futureChores) { c.CanceledAt = now; c.CanceledBy = auditCurrentUserId; }
                canceledChores = futureChores.Count;
            }

            if (cleanOnCall)
            {
                // Soft-cancel future on-call (OnDuty) assignments — mirrors OnDutyService.CancelAsync pattern.
                var futureOnDuties = await _db.OnDuties.IgnoreQueryFilters()
                    .Where(o => o.UserId == id && o.Date >= today && o.CanceledAt == null)
                    .ToListAsync();
                foreach (var o in futureOnDuties) { o.CanceledAt = now; o.CanceledBy = auditCurrentUserId; }
                canceledOnCall = futureOnDuties.Count;
            }

            if (vacatedShifts > 0 || canceledChores > 0 || canceledOnCall > 0)
            {
                // Single save for all cleanup mutations.
                var cleanupSaveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser", id);
                if (!cleanupSaveResult.Success)
                {
                    // Non-fatal: account type was already persisted; log and continue.
                    _logger.LogWarning("Concurrency conflict during account-type cleanup save for user {UserId}; some future assignments may not have been removed.", id);
                }

                var cleanupDetails = new System.Text.StringBuilder();
                if (vacatedShifts > 0) cleanupDetails.Append($"; vacated {vacatedShifts} future shift(s)");
                if (canceledChores > 0) cleanupDetails.Append($"; canceled {canceledChores} future chore(s)");
                if (canceledOnCall > 0) cleanupDetails.Append($"; canceled {canceledOnCall} future on-call(s)");

                await _auditLogService.LogUserActionAsync(
                    userId: auditCurrentUserId,
                    action: "AccountTypeCleanup",
                    entityType: "User",
                    entityId: u.Id,
                    description: $"Removed disallowed future assignments for {u.DisplayName} after account-type change to {newType}{cleanupDetails}"
                );
            }
        }

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture,
            _localizer["Success_JobTypeUpdated"], u.DisplayName,
            _localizer[$"AccountType_{(AccountType)accountType}"].Value);
        return RedirectToPage();
    }

    /// <summary>
    /// Shared edit gate for the DoesShifts / category handlers — mirrors the JobType/Toggle handlers:
    /// loads the (cross-company) user and confirms the caller holds AdminAccess or EditCompanyUsers for
    /// the user's company. Returns the resolved user + caller id, or a localized error string.
    /// </summary>
    private async Task<(bool Ok, AppUser? User, int CurrentUserId, string? Error)> AuthorizeUserEditAsync(int id)
    {
        if (id <= 0)
            return (false, null, 0, _localizer["Error_InvalidUserId"].Value);

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company user lookup.
        var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (u == null)
            return (false, null, 0, _localizer["Error_UserNotFound"].Value);

        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
            return (false, null, 0, _localizer["Error_InvalidUserClaim"].Value);

        var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
        var hasEditGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(currentUserId, "EditCompanyUsers", u.CompanyId);
        if (!hasEditGrant)
            return (false, null, currentUserId, _localizer["Error_NoPermissionForCompany"].Value);

        return (true, u, currentUserId, null);
    }

    /// <summary>Future-shift impact preview (AJAX) for the "turn off Participates in Shifts" confirm.</summary>
    public async Task<IActionResult> OnGetDoesShiftsImpactAsync(int id)
    {
        var (ok, _, _, error) = await AuthorizeUserEditAsync(id);
        if (!ok)
            return new JsonResult(new { ok = false, error });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureShifts = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.UserId == id && sa.ShiftInstance!.WorkDate >= today);
        return new JsonResult(new { ok = true, futureShifts });
    }

    /// <summary>
    /// Master "Participates in Shifts" toggle. When turning OFF, <paramref name="deleteFutureShifts"/>
    /// controls whether the user's future shift assignments are vacated (UserId nulled) or kept.
    /// Category memberships are intentionally preserved so toggling back ON restores the prior mapping.
    /// </summary>
    public async Task<IActionResult> OnPostDoesShiftsAsync(int id, bool doesShifts, bool deleteFutureShifts = false)
    {
        var (ok, u, currentUserId, error) = await AuthorizeUserEditAsync(id);
        if (!ok) { TempData["ErrorMessage"] = error; return RedirectToPage(); }

        u!.DoesShifts = doesShifts;

        int vacated = 0;
        if (!doesShifts && deleteFutureShifts)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            // SECURITY-AUDITED: SAFE — vacating only the target user's own future assignments.
            var future = await _db.ShiftAssignments.IgnoreQueryFilters()
                .Include(sa => sa.ShiftInstance)
                .Where(sa => sa.UserId == id && sa.ShiftInstance!.WorkDate >= today)
                .ToListAsync();
            foreach (var sa in future) { sa.UserId = null; sa.TraineeUserId = null; }
            vacated = future.Count;
        }

        var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
            () => _db.SaveChangesAsync(), "AppUser", id);
        if (!saveResult.Success)
        {
            TempData["ErrorMessage"] = _localizer["Error_ConcurrencyConflict"].Value;
            return RedirectToPage();
        }

        await _auditLogService.LogUserActionAsync(
            userId: currentUserId,
            action: "DoesShiftsChanged",
            entityType: "User",
            entityId: u.Id,
            description: $"Set DoesShifts={doesShifts} for {u.DisplayName}" + (vacated > 0 ? $"; vacated {vacated} future shift(s)" : ""));

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture,
            (doesShifts ? _localizer["Users_DoesShiftsOn"] : _localizer["Users_DoesShiftsOff"]).Value, u.DisplayName);
        return RedirectToPage();
    }

    /// <summary>Replaces a user's shift-category memberships (AJAX). Ids are validated against the user's molecule.</summary>
    public async Task<IActionResult> OnPostUserCategoriesAsync([FromBody] UserCategoriesRequest request)
    {
        var (ok, u, currentUserId, error) = await AuthorizeUserEditAsync(request.UserId);
        if (!ok)
            return new JsonResult(new { success = false, error }) { StatusCode = 403 };

        // Constrain to categories in the user's molecule (defense against stale/forged ids).
        var moleculeId = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == u!.CompanyId).Select(c => c.MoleculeId).FirstOrDefaultAsync();
        var requested = request.CategoryIds ?? new List<int>();
        var valid = moleculeId == null
            ? new List<int>()
            : await _db.ShiftCategories
                .Where(c => c.MoleculeId == moleculeId && requested.Contains(c.Id))
                .Select(c => c.Id).ToListAsync();

        await _categoryService.SetUserCategoriesAsync(u!.Id, valid);
        await _auditLogService.LogUserActionAsync(
            userId: currentUserId,
            action: "UserCategoriesChanged",
            entityType: "User",
            entityId: u.Id,
            description: $"Set categories for {u.DisplayName} to [{string.Join(",", valid)}]");

        return new JsonResult(new { success = true, count = valid.Count });
    }

    public class UserCategoriesRequest
    {
        public int UserId { get; set; }
        public List<int>? CategoryIds { get; set; }
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
                LogInvalidNameIdClaimPasswordResetAuth(_logger);
                return RedirectToPage();
            }

            var isAdmin = await _grantService.HasGrantAsync(resetCurrentUserId, "AdminAccess");
            var hasResetGrant = isAdmin || await _grantService.HasGrantForCompanyAsync(resetCurrentUserId, "EditCompanyUsers", u.CompanyId);
            if (!hasResetGrant)
            {
                LogUnauthorizedUserActionWithGrant(_logger, resetCurrentUserId, "password reset on", id, u.CompanyId);
                TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
                return RedirectToPage();
            }

            var (h, s) = PasswordHasher.CreateHash(newPassword);
            u.PasswordHash = h; u.PasswordSalt = s;
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser", u.Id);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }

            // Log the password reset for security audit
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                LogInvalidNameIdClaimPasswordResetAudit(_logger);
                currentUserId = 0; // Fallback for audit trail
            }
            await _auditLogService.LogUserActionAsync(
                userId: currentUserId,
                action: "PasswordReset",
                entityType: "User",
                entityId: u.Id,
                description: $"Password reset for user {u.DisplayName} ({u.Email})"
            );

            // Notify the affected user (security-critical → always emails, even in Quiet/muted).
            await _notificationService.NotifyAsync(u.Id, NotificationType.PasswordReset,
                ShiftManager.Services.Notifications.NotificationCategory.AccountSecurity,
                _localizer["Notif_PasswordResetTitle"].Value, _localizer["Notif_PasswordResetMessage"].Value,
                personallyActionable: true, securityCritical: true);

            TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_PasswordUpdated"], u.DisplayName);
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
            LogInvalidNameIdClaim(_logger);
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
            LogUnauthorizedUserActionWithGrant(_logger, currentUserId, "to unlock", id, targetUser.CompanyId);
            TempData["ErrorMessage"] = _localizer["Error_NoPermissionForCompany"].Value;
            return RedirectToPage();
        }

        // Unlock the account
        targetUser.FailedLoginAttempts = 0;
        targetUser.LockoutEnd = null;
        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "AppUser", targetUser.Id);
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }
        }

        // Log the unlock action for security audit
        await _auditLogService.LogUserActionAsync(
            userId: currentUserId,
            action: "AccountUnlock",
            entityType: "User",
            entityId: targetUser.Id,
            description: $"Account unlocked for user {targetUser.DisplayName} ({targetUser.Email})"
        );

        LogAccountUnlocked(_logger, currentUserId, targetUser.Id, targetUser.Email);

        // Notify the affected user (security-critical → always emails).
        await _notificationService.NotifyAsync(targetUser.Id, NotificationType.AccountUnlocked,
            ShiftManager.Services.Notifications.NotificationCategory.AccountSecurity,
            _localizer["Notif_AccountUnlockedTitle"].Value, _localizer["Notif_AccountUnlockedMessage"].Value,
            personallyActionable: true, securityCritical: true);

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_AccountUnlocked"], targetUser.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(int id, bool forceDelete = false)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                LogInvalidNameIdClaim(_logger);
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
                return RedirectToPage();
            }

            // Prevent self-deletion
            if (id == currentUserId)
            {
                LogUserAttemptedSelfDelete(_logger, currentUserId);
                Error = _localizer["Error_CannotDeleteOwnAccount"];
                await OnGetAsync();
                return Page();
            }

            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
            if (user == null)
            {
                LogUserNotFoundForDeletion(_logger, id);
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
                LogUnauthorizedUserActionWithGrant(_logger, currentUserId, "to delete", id, user.CompanyId);
                Error = _localizer["Error_CanOnlyDeleteOwnCompanyUsers"];
                await OnGetAsync();
                return Page();
            }

            LogStartingUserDeletion(_logger, id, user.DisplayName, currentUserId);

            // Audit log BEFORE deletion so we have a record even if the delete fails
            await _auditLogService.LogUserActionAsync(
                userId: currentUserId,
                action: forceDelete ? "UserForceDeleted" : "UserDeleted",
                entityType: "User",
                entityId: user.Id,
                description: $"Permanently deleted user {user.DisplayName} (Email: {user.Email}, UserId: {user.Id}, CompanyId: {user.CompanyId}){(forceDelete ? " [FORCE DELETE — authored content reassigned/removed]" : "")}"
            );

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
                LogDeletingSwapRequests(_logger, swapRequestsCount, id);
                await _db.SwapRequests
                    .IgnoreQueryFilters()
                    .Where(sr => userAssignmentIds.Contains(sr.FromAssignmentId) || sr.ToUserId == id)
                    .ExecuteDeleteAsync();
            }

            // 2. Remove all shift assignments using ExecuteDeleteAsync for better performance
            if (shiftAssignmentCount > 0)
            {
                LogRemovingShiftAssignments(_logger, shiftAssignmentCount, id);
                await _db.ShiftAssignments.IgnoreQueryFilters().Where(sa => sa.UserId == id).ExecuteDeleteAsync();
            }

            // 3. Delete all time-off requests
            var timeOffRequestCount = await _db.TimeOffRequests.IgnoreQueryFilters().Where(tor => tor.UserId == id).CountAsync();
            if (timeOffRequestCount > 0)
            {
                LogDeletingTimeOffRequests(_logger, timeOffRequestCount, id);
                await _db.TimeOffRequests.IgnoreQueryFilters().Where(tor => tor.UserId == id).ExecuteDeleteAsync();
            }

            // 3a. Clean up grants (prevents orphaned grants for deactivated users)
            // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific userId being deactivated
            var grantCount = await _db.Grants.IgnoreQueryFilters().Where(g => g.UserId == id).CountAsync();
            if (grantCount > 0)
            {
                LogRemovingGrants(_logger, grantCount, id);
                await _db.Grants.IgnoreQueryFilters().Where(g => g.UserId == id).ExecuteDeleteAsync();
            }

            // 3b. Delete DirectorCompany mappings where user is a director
            // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific userId
            await _db.DirectorCompanies.IgnoreQueryFilters()
                .Where(dc => dc.UserId == id).ExecuteDeleteAsync();
            // Reassign GrantedBy where deleted user was the grantor for other directors
            await _db.DirectorCompanies.IgnoreQueryFilters()
                .Where(dc => dc.GrantedBy == id)
                .ExecuteUpdateAsync(dc => dc.SetProperty(x => x.GrantedBy, currentUserId));

            // 3c. Clear TraineeUserId references on shift assignments where this user is the trainee
            await _db.ShiftAssignments.IgnoreQueryFilters()
                .Where(sa => sa.TraineeUserId == id)
                .ExecuteUpdateAsync(sa => sa.SetProperty(a => a.TraineeUserId, (int?)null));

            // 3d. Delete user-owned records (belong to deleted user)
            await _db.UserNotifications.IgnoreQueryFilters().Where(n => n.UserId == id).ExecuteDeleteAsync();
            await _db.Chores.IgnoreQueryFilters().Where(c => c.UserId == id).ExecuteDeleteAsync();
            await _db.OnDuties.IgnoreQueryFilters().Where(o => o.UserId == id).ExecuteDeleteAsync();
            // Delete entries ABOUT the deleted user; reassign authorship of entries they created for others
            await _db.CalendarTextEntries.IgnoreQueryFilters().Where(e => e.UserId == id).ExecuteDeleteAsync();
            await _db.CalendarTextEntries.IgnoreQueryFilters()
                .Where(e => e.CreatedByUserId == id && e.UserId != id)
                .ExecuteUpdateAsync(e => e.SetProperty(x => x.CreatedByUserId, currentUserId));
            await _db.UserFriendships.IgnoreQueryFilters().Where(f => f.UserId == id || f.FriendId == id).ExecuteDeleteAsync();
            await _db.GameScores.IgnoreQueryFilters().Where(g => g.UserId == id).ExecuteDeleteAsync();
            await _db.OnDutyRoleSubscriptions.IgnoreQueryFilters().Where(s => s.UserId == id).ExecuteDeleteAsync();
            await _db.DutyRotationEntries.IgnoreQueryFilters().Where(e => e.UserId == id).ExecuteDeleteAsync();
            await _db.DailyNotificationPreferences.IgnoreQueryFilters().Where(p => p.UserId == id).ExecuteDeleteAsync();
            await _db.TeamCalendarMembers.IgnoreQueryFilters().Where(m => m.MemberUserId == id).ExecuteDeleteAsync();
            await _db.TeamCalendars.IgnoreQueryFilters().Where(t => t.OwnerId == id).ExecuteDeleteAsync();
            await _db.UserRoleAssignments.IgnoreQueryFilters().Where(r => r.UserId == id).ExecuteDeleteAsync();
            await _db.Feedbacks.IgnoreQueryFilters().Where(f => f.SubmittedBy == id).ExecuteDeleteAsync();
            await _db.Announcements.IgnoreQueryFilters().Where(a => a.CreatedBy == id).ExecuteDeleteAsync();
            await _db.ApiKeys.IgnoreQueryFilters().Where(a => a.CreatedBy == id).ExecuteDeleteAsync();
            await _db.ApiKeyRequests.IgnoreQueryFilters().Where(a => a.RequestedBy == id).ExecuteDeleteAsync();
            // Per-user calendar ordering — drop on hard-delete (no FK cascade exists).
            await _db.UserCalendarRowOrders.IgnoreQueryFilters()
                .Where(o => o.UserId == id)
                .ExecuteDeleteAsync();

            // 3e. Nullify nullable FK references (preserve records, clear user link)
            await _db.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == id)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.UserId, (int?)null));
            await _db.DutyRotationLogs.IgnoreQueryFilters().Where(d => d.AssignedUserId == id)
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.AssignedUserId, (int?)null));
            await _db.DutyRotationLogs.IgnoreQueryFilters().Where(d => d.SkippedUserId == id)
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.SkippedUserId, (int?)null));
            await _db.VacationApprovalRules.IgnoreQueryFilters().Where(v => v.ApproverUserId == id)
                .ExecuteUpdateAsync(v => v.SetProperty(x => x.ApproverUserId, (int?)null));
            await _db.ApiKeyRequests.IgnoreQueryFilters().Where(a => a.ReviewedBy == id)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.ReviewedBy, (int?)null));
            await _db.SwapRequests.IgnoreQueryFilters().Where(s => s.ReviewedBy == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewedBy, (int?)null));

            // 3f. Reassign non-nullable CreatedBy/UpdatedBy on shared config to current admin
            await _db.ChoreTypes.IgnoreQueryFilters().Where(ct => ct.CreatedByUserId == id)
                .ExecuteUpdateAsync(ct => ct.SetProperty(x => x.CreatedByUserId, currentUserId));
            await _db.ShiftCapacityOverrides.IgnoreQueryFilters().Where(s => s.CreatedByUserId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedByUserId, currentUserId));
            await _db.ShiftPrograms.IgnoreQueryFilters().Where(s => s.CreatedBy == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedBy, currentUserId));
            await _db.ShiftPrograms.IgnoreQueryFilters().Where(s => s.UpdatedBy == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedBy, currentUserId));
            await _db.MasterPrograms.IgnoreQueryFilters().Where(m => m.CreatedBy == id)
                .ExecuteUpdateAsync(m => m.SetProperty(x => x.CreatedBy, currentUserId));
            await _db.MasterPrograms.IgnoreQueryFilters().Where(m => m.UpdatedBy == id)
                .ExecuteUpdateAsync(m => m.SetProperty(x => x.UpdatedBy, currentUserId));
            await _db.DutyRotations.IgnoreQueryFilters().Where(d => d.CreatedBy == id)
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.CreatedBy, currentUserId));
            await _db.VacationApprovalRules.IgnoreQueryFilters().Where(v => v.CreatedBy == id)
                .ExecuteUpdateAsync(v => v.SetProperty(x => x.CreatedBy, currentUserId));

            // 3g. Delete audit records about the deleted user (main AuditLog already nullified above)
            await _db.ProfileChangeAudits.IgnoreQueryFilters()
                .Where(p => p.TargetUserId == id || p.ChangedBy == id).ExecuteDeleteAsync();
            await _db.RoleAssignmentAudits.IgnoreQueryFilters()
                .Where(r => r.TargetUserId == id || r.ChangedBy == id).ExecuteDeleteAsync();

            // 3h. Delete CompanyMembership rows (Restrict FK — blocks every delete)
            // SECURITY-AUDITED: scoped by userId
            await _db.CompanyMemberships.IgnoreQueryFilters()
                .Where(cm => cm.UserId == id).ExecuteDeleteAsync();

            // 3i. Delete DistributionListMember rows (Restrict FK on UserId)
            // SECURITY-AUDITED: scoped by userId
            await _db.DistributionListMembers.IgnoreQueryFilters()
                .Where(dlm => dlm.UserId == id).ExecuteDeleteAsync();

            // 3j. Delete UserShiftCategory rows (Cascade FK, but explicit for safety)
            // SECURITY-AUDITED: scoped by userId
            await _db.UserShiftCategories.IgnoreQueryFilters()
                .Where(usc => usc.UserId == id).ExecuteDeleteAsync();

            // 3k. Delete SetupTask rows referencing this user in any capacity (all Restrict FKs)
            // SECURITY-AUDITED: scoped by userId
            await _db.SetupTasks.IgnoreQueryFilters()
                .Where(st => st.AssignedToUserId == id || st.SuggestedUserId == id || st.CompletedByUserId == id)
                .ExecuteDeleteAsync();

            // 3l. Delete UserDayNote rows FOR this user; reassign notes this user created for others
            // SECURITY-AUDITED: scoped by userId
            await _db.UserDayNotes.IgnoreQueryFilters()
                .Where(n => n.UserId == id).ExecuteDeleteAsync();
            await _db.UserDayNotes.IgnoreQueryFilters()
                .Where(n => n.CreatedByUserId == id && n.UserId != id)
                .ExecuteUpdateAsync(n => n.SetProperty(x => x.CreatedByUserId, currentUserId));

            // 3m. Force-delete path: reassign/null authored content that normal delete leaves alone.
            // Only reached when admin explicitly confirms force-delete.
            if (forceDelete)
            {
                // Reassign Chore authorship (CreatedBy is non-nullable → reassign; CanceledBy is nullable → null)
                // SECURITY-AUDITED: scoped by userId
                await _db.Chores.IgnoreQueryFilters()
                    .Where(c => c.CreatedBy == id && c.UserId != id)
                    .ExecuteUpdateAsync(c => c.SetProperty(x => x.CreatedBy, currentUserId));
                await _db.Chores.IgnoreQueryFilters()
                    .Where(c => c.CanceledBy == id)
                    .ExecuteUpdateAsync(c => c.SetProperty(x => x.CanceledBy, (int?)null));

                // Reassign UserRoleAssignment.AssignedByUserId (non-nullable → reassign)
                // SECURITY-AUDITED: scoped by userId
                await _db.UserRoleAssignments.IgnoreQueryFilters()
                    .Where(ura => ura.AssignedByUserId == id)
                    .ExecuteUpdateAsync(ura => ura.SetProperty(x => x.AssignedByUserId, currentUserId));

                // Null out UserJoinRequest references (both nullable)
                // SECURITY-AUDITED: scoped by userId
                await _db.UserJoinRequests.IgnoreQueryFilters()
                    .Where(jr => jr.ReviewedBy == id)
                    .ExecuteUpdateAsync(jr => jr.SetProperty(x => x.ReviewedBy, (int?)null));
                await _db.UserJoinRequests.IgnoreQueryFilters()
                    .Where(jr => jr.CreatedUserId == id)
                    .ExecuteUpdateAsync(jr => jr.SetProperty(x => x.CreatedUserId, (int?)null));

                // Null settings UpdatedByUserId (all nullable)
                // SECURITY-AUDITED: scoped by userId
                await _db.AreaSettings.IgnoreQueryFilters()
                    .Where(a => a.UpdatedByUserId == id)
                    .ExecuteUpdateAsync(a => a.SetProperty(x => x.UpdatedByUserId, (int?)null));
                await _db.MoleculeSettings.IgnoreQueryFilters()
                    .Where(m => m.UpdatedByUserId == id)
                    .ExecuteUpdateAsync(m => m.SetProperty(x => x.UpdatedByUserId, (int?)null));
                await _db.CompanySettings.IgnoreQueryFilters()
                    .Where(cs => cs.UpdatedByUserId == id)
                    .ExecuteUpdateAsync(cs => cs.SetProperty(x => x.UpdatedByUserId, (int?)null));

                // Null GrantType.CreatedByUserId (nullable)
                // SECURITY-AUDITED: scoped by userId
                await _db.GrantTypes.IgnoreQueryFilters()
                    .Where(gt => gt.CreatedByUserId == id)
                    .ExecuteUpdateAsync(gt => gt.SetProperty(x => x.CreatedByUserId, (int?)null));

                // Null JusticeTarget.CreatedByUserId (nullable)
                // SECURITY-AUDITED: scoped by userId
                await _db.JusticeTargets.IgnoreQueryFilters()
                    .Where(jt => jt.CreatedByUserId == id)
                    .ExecuteUpdateAsync(jt => jt.SetProperty(x => x.CreatedByUserId, (int?)null));

                // Reassign HomeType.CreatedBy (non-nullable)
                // SECURITY-AUDITED: scoped by userId
                await _db.HomeTypes.IgnoreQueryFilters()
                    .Where(h => h.CreatedBy == id)
                    .ExecuteUpdateAsync(h => h.SetProperty(x => x.CreatedBy, currentUserId));

                // Reassign HomeTypeOverride.CreatedBy (non-nullable; UserId rows already deleted above via Cascade)
                // SECURITY-AUDITED: scoped by userId
                await _db.HomeTypeOverrides.IgnoreQueryFilters()
                    .Where(o => o.CreatedBy == id)
                    .ExecuteUpdateAsync(o => o.SetProperty(x => x.CreatedBy, currentUserId));

                // Reassign OnDutyTypeConfig.CreatedBy (non-nullable, global table)
                // SECURITY-AUDITED: scoped by userId
                await _db.OnDutyTypeConfigs
                    .Where(o => o.CreatedBy == id)
                    .ExecuteUpdateAsync(o => o.SetProperty(x => x.CreatedBy, currentUserId));

                // Reassign CompanyLanguageSettings CreatedBy/UpdatedBy (both non-nullable)
                // SECURITY-AUDITED: scoped by userId
                await _db.CompanyLanguageSettings.IgnoreQueryFilters()
                    .Where(cls => cls.CreatedBy == id)
                    .ExecuteUpdateAsync(cls => cls.SetProperty(x => x.CreatedBy, currentUserId));
                await _db.CompanyLanguageSettings.IgnoreQueryFilters()
                    .Where(cls => cls.UpdatedBy == id)
                    .ExecuteUpdateAsync(cls => cls.SetProperty(x => x.UpdatedBy, currentUserId));

                // Reassign CompanyLocalizationOverride CreatedBy/UpdatedBy (both non-nullable)
                // SECURITY-AUDITED: scoped by userId
                await _db.CompanyLocalizationOverrides.IgnoreQueryFilters()
                    .Where(clo => clo.CreatedBy == id)
                    .ExecuteUpdateAsync(clo => clo.SetProperty(x => x.CreatedBy, currentUserId));
                await _db.CompanyLocalizationOverrides.IgnoreQueryFilters()
                    .Where(clo => clo.UpdatedBy == id)
                    .ExecuteUpdateAsync(clo => clo.SetProperty(x => x.UpdatedBy, currentUserId));

                // On-duties the user CREATED for others (CreatedBy non-nullable → reassign; CanceledBy
                // nullable → null). Note: on-duties where this user is the ASSIGNEE (UserId) were already
                // deleted above; these are the ones they authored for OTHER people. This was the FK that
                // blocked force-delete for users who created on-call entries.
                // SECURITY-AUDITED: scoped by userId
                await _db.OnDuties.IgnoreQueryFilters()
                    .Where(o => o.CreatedBy == id)
                    .ExecuteUpdateAsync(o => o.SetProperty(x => x.CreatedBy, currentUserId));
                await _db.OnDuties.IgnoreQueryFilters()
                    .Where(o => o.CanceledBy == id)
                    .ExecuteUpdateAsync(o => o.SetProperty(x => x.CanceledBy, (int?)null));

                // Distribution lists the user created (CreatedBy non-nullable → reassign).
                // SECURITY-AUDITED: scoped by userId
                await _db.DistributionLists.IgnoreQueryFilters()
                    .Where(dl => dl.CreatedBy == id)
                    .ExecuteUpdateAsync(dl => dl.SetProperty(x => x.CreatedBy, currentUserId));

                // Feedback status-updater (nullable → null; feedbacks they SUBMITTED were already deleted).
                // SECURITY-AUDITED: scoped by userId
                await _db.Feedbacks.IgnoreQueryFilters()
                    .Where(f => f.StatusUpdatedBy == id)
                    .ExecuteUpdateAsync(f => f.SetProperty(x => x.StatusUpdatedBy, (int?)null));

                // Molecule approval settings updater (non-nullable → reassign).
                // SECURITY-AUDITED: scoped by userId
                await _db.MoleculeApprovalSettings.IgnoreQueryFilters()
                    .Where(m => m.UpdatedByUserId == id)
                    .ExecuteUpdateAsync(m => m.SetProperty(x => x.UpdatedByUserId, currentUserId));

                // Grants this user GRANTED to others (GrantedByUserId nullable → null; grants where this
                // user is the holder/UserId were already cascade/deleted above).
                // SECURITY-AUDITED: scoped by userId
                await _db.Grants.IgnoreQueryFilters()
                    .Where(g => g.GrantedByUserId == id)
                    .ExecuteUpdateAsync(g => g.SetProperty(x => x.GrantedByUserId, (int?)null));
            }

            LogCompletedRelatedRecordsCleanup(_logger, id);

            // 4. Hard-delete the user from the database
            LogPermanentlyDeletingUser(_logger, id, user.DisplayName);
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            LogSuccessfullyDeletedUser(_logger, id, user.DisplayName);

            // Use TempData to show success message after redirect
            TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_UserDeleted"], user.DisplayName);

            return RedirectToPage();
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is Microsoft.Data.Sqlite.SqliteException sqliteEx
            && sqliteEx.SqliteErrorCode == 19)
        {
            await transaction.RollbackAsync();
            LogFkConstraintPreventedDeletion(_logger, dbEx, id);
            Error = _localizer["Admin_UserDeleteBlockedByDependencies"];
            // Preserve the blocked user's id so the view can render the force-delete option
            var blockedUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
            BlockedDeleteUserId = id;
            BlockedDeleteUserName = blockedUser?.DisplayName ?? id.ToString();
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            LogErrorDeletingUser(_logger, ex, id);
            Error = _localizer["Error_DeletingUser"];
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostApproveJoinRequestAsync(int id, int? roleTemplateId = null)
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
            LogInvalidNameIdClaim(_logger);
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        var joinRequest = await _db.UserJoinRequests
            .IgnoreQueryFilters()
            .Include(jr => jr.Company)
            .Include(jr => jr.JobType)
            .FirstOrDefaultAsync(jr => jr.Id == id);

        if (joinRequest == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JoinRequestNotFound"].Value;
            return RedirectToPage();
        }

        // ✅ Grant-based: Verify user has permission to approve this request
        // Phase 2: Pass joinRequest.JobTypeId so leads with useOwnJobType grants
        // can only approve requests matching their job type
        var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
        var hasPermission = isAdmin || await _grantService.HasGrantWithScopeAsync(
            currentUserId, "ManageJoinRequests", companyId: joinRequest.CompanyId, jobTypeId: joinRequest.JobTypeId);

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

        // SECURITY-AUDITED: IgnoreQueryFilters for global email uniqueness — email is the login identifier.
        // Case-insensitive check (consistent with all other email-existence checks).
        var requestEmailLower = joinRequest.Email.ToLowerInvariant();
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == requestEmailLower))
        {
            TempData["ErrorMessage"] = _localizer["Error_UserEmailAlreadyExists"].Value;
            return RedirectToPage();
        }

        // Validate permission to assign the requested role
        if (!await _directorService.CanAssignRoleAsync(joinRequest.RequestedRole))
        {
            TempData["ErrorMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Error_NoPermissionAssignRole"].Value, joinRequest.RequestedRole);
            return RedirectToPage();
        }

        // HIGH-009 FIX: Wrap in try/catch to prevent unhandled exceptions
        try
        {
            // Create the user account — prefer admin-selected template, then join request's template, fall back to mapping
            RoleTemplate? approveTemplate = null;
            if (roleTemplateId.HasValue)
            {
                approveTemplate = await _roleService.GetRoleTemplateAsync(roleTemplateId.Value);
            }
            if (approveTemplate == null && joinRequest.RequestedRoleTemplateId.HasValue)
            {
                approveTemplate = await _roleService.GetRoleTemplateAsync(joinRequest.RequestedRoleTemplateId.Value);
            }
            if (approveTemplate == null)
            {
                var approveTemplateKey = MapUserRoleToRoleTemplateKey(joinRequest.RequestedRole, joinRequest.JobType?.Name);
                approveTemplate = await _roleService.GetRoleTemplateByKeyAsync(approveTemplateKey);
            }

            var newUser = new AppUser
            {
                Email = joinRequest.Email,
                DisplayName = joinRequest.DisplayName,
                PasswordHash = joinRequest.PasswordHash,
                PasswordSalt = joinRequest.PasswordSalt,
                CompanyId = joinRequest.CompanyId,
                JobTypeId = joinRequest.JobTypeId,
                DepartmentId = joinRequest.DepartmentId,
                Role = approveTemplate?.DerivedUserRole ?? joinRequest.RequestedRole,
                RoleTemplateId = approveTemplate?.Id,
                IsActive = true
            };

            _db.Users.Add(newUser);

            // Update join request status
            joinRequest.Status = JoinRequestStatus.Approved;
            joinRequest.ReviewedBy = currentUserId;
            joinRequest.ReviewedAt = DateTime.UtcNow;

            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser");
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }

            // Link the created user to the join request
            joinRequest.CreatedUserId = newUser.Id;
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser", newUser.Id);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }

            // ✅ Onboarding: Assign role template grants with JobType-aware mapping
            var roleTemplateKey = approveTemplate?.Key ?? MapUserRoleToRoleTemplateKey(joinRequest.RequestedRole, joinRequest.JobType?.Name);
            var grantScope = await BuildGrantScopeForTemplateAsync(roleTemplateKey, joinRequest.CompanyId, joinRequest.JobTypeId);
            var grantsAssigned = await _grantService.AssignRoleTemplateGrantsAsync(newUser.Id, roleTemplateKey, grantScope, currentUserId);
            LogGrantsAssignedViaJoinRequestApproval(_logger, grantsAssigned, roleTemplateKey, newUser.Id);

            // FINDING-010 FIX: Record initial role assignment in audit trail
            _db.RoleAssignmentAudits.Add(new RoleAssignmentAudit
            {
                ChangedBy = currentUserId,
                TargetUserId = newUser.Id,
                FromRole = null,
                ToRole = newUser.Role,
                FromRoleTemplateId = null,
                ToRoleTemplateId = newUser.RoleTemplateId,
                CompanyId = newUser.CompanyId,
                Timestamp = DateTime.UtcNow
            });

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

            LogJoinRequestApproved(_logger, id, currentUserId, newUser.Id, newUser.Email, joinRequest.CompanyId);

            TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_JoinRequestApproved"], joinRequest.DisplayName, joinRequest.Email, joinRequest.RequestedRole, joinRequest.Company?.Name);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            LogErrorApprovingJoinRequest(_logger, ex, id);
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
            LogInvalidNameIdClaim(_logger);
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
        // Phase 2: Pass joinRequest.JobTypeId for same-jobtype enforcement on leads
        var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
        var hasPermission = isAdmin || await _grantService.HasGrantWithScopeAsync(
            currentUserId, "ManageJoinRequests", companyId: joinRequest.CompanyId, jobTypeId: joinRequest.JobTypeId);

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

            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser", id);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }

            LogJoinRequestRejected(_logger, id, currentUserId, joinRequest.Email, joinRequest.CompanyId);

            // Email the applicant directly (they have no account → no in-app/footer). Fire-and-forget.
            if (!string.IsNullOrWhiteSpace(joinRequest.Email))
            {
                var rejectSubject = _localizer["Notif_JoinRejectedTitle"].Value;
                var rejectBody = System.Net.WebUtility.HtmlEncode(_localizer["Notif_JoinRejectedMessage"].Value);
                var rejectHtml = $"<!DOCTYPE html><html><body style='font-family:Arial,sans-serif;padding:16px;'><p>{rejectBody}</p></body></html>";
                _ = _mailService.SendMailAsync(joinRequest.Email, rejectSubject, rejectHtml);
            }

            TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_JoinRequestRejected"], joinRequest.DisplayName, joinRequest.Email);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            LogErrorRejectingJoinRequest(_logger, ex, id);
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
            LogInvalidNameIdClaim(_logger);
            TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
            return RedirectToPage();
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        if (SelectedRequests == null || !SelectedRequests.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_NoRequestsSelected"].Value;
            return RedirectToPage();
        }

        // Manually bind RequestTemplateIds dictionary from form data
        RequestTemplateIds = new Dictionary<int, int>();
        const string templatePrefix = "RequestTemplateIds[";
        foreach (var key in Request.Form.Keys.Where(k => k.StartsWith(templatePrefix)))
        {
            var idString = key[templatePrefix.Length..^1]; // Extract ID between "[" and "]"
            if (int.TryParse(idString, out var requestId) &&
                int.TryParse(Request.Form[key].ToString(), out var templateId) &&
                templateId > 0)
            {
                RequestTemplateIds[requestId] = templateId;
            }
        }

        // Legacy fallback: also parse RequestRoles if present
        RequestRoles = new Dictionary<int, UserRole>();
        const string rolesPrefix = "RequestRoles[";
        foreach (var key in Request.Form.Keys.Where(k => k.StartsWith(rolesPrefix)))
        {
            var idString = key[rolesPrefix.Length..^1]; // Extract ID between "[" and "]"
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
                .Include(jr => jr.JobType)
                .Where(jr => SelectedRequests.Contains(jr.Id))
                .ToListAsync();

            // Check if any selected IDs were not found (potential tampering)
            var foundIds = joinRequests.Select(jr => jr.Id).ToHashSet();
            var invalidIds = SelectedRequests.Where(id => !foundIds.Contains(id)).ToList();
            if (invalidIds.Any())
            {
                LogSecurityInvalidJoinRequestIds(_logger, currentUserId, string.Join(", ", invalidIds));
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
                // Phase 2: Use HasGrantWithScopeAsync for per-request company+JobType enforcement
                var canManage = isAdmin || await _grantService.HasGrantWithScopeAsync(
                    currentUserId, "ManageJoinRequests", companyId: joinRequest.CompanyId, jobTypeId: joinRequest.JobTypeId);
                if (!canManage)
                {
                    LogSecurityUnauthorizedJoinRequestApproval(_logger, currentUserId, currentUser!.Role, joinRequest.Id, joinRequest.CompanyId, joinRequest.JobTypeId);
                    errors.Add(string.Format(CultureInfo.CurrentCulture, _localizer["Error_NoPermissionDifferentCompany"], joinRequest.DisplayName));
                    skippedCount++;
                    continue;
                }

                // Check if already reviewed
                if (joinRequest.Status != JoinRequestStatus.Pending)
                {
                    errors.Add(string.Format(CultureInfo.CurrentCulture, _localizer["Error_AlreadyReviewed"], joinRequest.DisplayName));
                    skippedCount++;
                    continue;
                }

                // SECURITY-AUDITED: IgnoreQueryFilters for global email uniqueness — email is the login identifier.
                // Case-insensitive check — see siblings in this file for rationale.
                var jrEmailLower = joinRequest.Email.ToLowerInvariant();
                if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == jrEmailLower))
                {
                    errors.Add(string.Format(CultureInfo.CurrentCulture, _localizer["Error_UserWithEmailExists"], joinRequest.Email));
                    skippedCount++;
                    continue;
                }

                // Get assigned role — prefer template ID from form, fall back to legacy enum, then requested role
                RoleTemplate? batchTemplate = null;
                UserRole assignedRole;

                if (RequestTemplateIds.TryGetValue(joinRequest.Id, out var batchTemplateId))
                {
                    batchTemplate = await _roleService.GetRoleTemplateAsync(batchTemplateId);
                    if (batchTemplate == null)
                    {
                        errors.Add(string.Format(CultureInfo.CurrentCulture, _localizer["Error_InvalidRole"].Value));
                        skippedCount++;
                        continue;
                    }
                    if (!batchTemplate.DerivedUserRole.HasValue)
                        LogRoleTemplateMissingDerivedRole(_logger, batchTemplate.Key, batchTemplate.Id);
                    assignedRole = batchTemplate.DerivedUserRole ?? UserRole.Employee;
                }
                else if (RequestRoles.TryGetValue(joinRequest.Id, out var legacyRole))
                {
                    assignedRole = legacyRole;
                }
                else
                {
                    assignedRole = joinRequest.RequestedRole;
                }

                // Validate permission to assign the role
                if (!await _directorService.CanAssignRoleAsync(assignedRole))
                {
                    errors.Add(string.Format(CultureInfo.CurrentCulture, _localizer["Error_NoPermissionAssignRoleTo"], assignedRole, joinRequest.DisplayName));
                    skippedCount++;
                    continue;
                }

                // If template wasn't loaded via direct ID, resolve via mapping
                if (batchTemplate == null)
                {
                    var batchTemplateKey = MapUserRoleToRoleTemplateKey(assignedRole, joinRequest.JobType?.Name);
                    batchTemplate = await _roleService.GetRoleTemplateByKeyAsync(batchTemplateKey);
                }

                var newUser = new AppUser
                {
                    Email = joinRequest.Email,
                    DisplayName = joinRequest.DisplayName,
                    PasswordHash = joinRequest.PasswordHash,
                    PasswordSalt = joinRequest.PasswordSalt,
                    CompanyId = joinRequest.CompanyId,
                    JobTypeId = joinRequest.JobTypeId,
                    Role = batchTemplate?.DerivedUserRole ?? assignedRole,
                    RoleTemplateId = batchTemplate?.Id,
                    IsActive = true
                };

                _db.Users.Add(newUser);

                // Update join request status
                joinRequest.Status = JoinRequestStatus.Approved;
                joinRequest.ReviewedBy = currentUserId;
                joinRequest.ReviewedAt = DateTime.UtcNow;

                {
                    var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                        () => _db.SaveChangesAsync(), "AppUser");
                    if (!saveResult.Success)
                    {
                        errors.Add(_localizer["Error_ConcurrencyConflict"]);
                        skippedCount++;
                        continue;
                    }
                }

                // Link the created user to the join request
                joinRequest.CreatedUserId = newUser.Id;

                // ✅ Onboarding: Assign role template grants — prefer resolved template key, fall back to mapping
                var roleTemplateKey = batchTemplate?.Key ?? MapUserRoleToRoleTemplateKey(assignedRole, joinRequest.JobType?.Name);
                var grantScope = await BuildGrantScopeForTemplateAsync(roleTemplateKey, joinRequest.CompanyId, joinRequest.JobTypeId);
                var grantsAssigned = await _grantService.AssignRoleTemplateGrantsAsync(newUser.Id, roleTemplateKey, grantScope, currentUserId);

                LogBatchApprovalSuccess(_logger, joinRequest.Id, currentUserId, newUser.Id, newUser.Email, assignedRole, roleTemplateKey, joinRequest.CompanyId, grantsAssigned);

                // FINDING-010 FIX: Record initial role assignment in audit trail
                _db.RoleAssignmentAudits.Add(new RoleAssignmentAudit
                {
                    ChangedBy = currentUserId,
                    TargetUserId = newUser.Id,
                    FromRole = null,
                    ToRole = newUser.Role,
                    FromRoleTemplateId = null,
                    ToRoleTemplateId = newUser.RoleTemplateId,
                    CompanyId = newUser.CompanyId,
                    Timestamp = DateTime.UtcNow
                });

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

            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "AppUser");
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }
            await transaction.CommitAsync();

            // Build success message
            var successMessage = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ApprovedCount"], approvedCount);
            if (skippedCount > 0)
            {
                successMessage += " " + string.Format(CultureInfo.CurrentCulture, _localizer["Success_SkippedCount"], skippedCount);
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
            LogErrorBatchApproval(_logger, ex);
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
                LogInvalidNameIdClaim(_logger);
                TempData["ErrorMessage"] = _localizer["Error_InvalidUserClaim"].Value;
                return RedirectToPage();
            }

            var currentUser = await _db.Users.FindAsync(currentUserId);
            if (currentUser == null)
            {
                LogUserNotFoundInDatabase(_logger, currentUserId);
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
                .Include(u => u.JobType)
                .Include(u => u.Department)
                .Include(u => u.RoleTemplate)
                .OrderBy(u => u.CompanyId)
                .ThenBy(u => u.DisplayName)
                .ToListAsync();

            // Batch-load company names for CSV (AppUser has no Company nav property)
            var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
            var companyNames = await _db.Companies.IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);

            // Build CSV with comprehensive columns for re-import support
            var csv = new StringBuilder();
            csv.AppendLine("display_name,email,role,role_template,company,job_type,department,is_active,phone,job_title,rank,hire_date");

            foreach (var user in users)
            {
                csv.AppendLine(string.Join(",",
                    EscapeCsvField(user.DisplayName),
                    EscapeCsvField(user.Email),
                    EscapeCsvField(user.Role.ToString()),
                    EscapeCsvField(user.RoleTemplate?.Key ?? ""),
                    EscapeCsvField(companyNames.GetValueOrDefault(user.CompanyId, "")),
                    EscapeCsvField(user.JobType?.DisplayName ?? ""),
                    EscapeCsvField(user.Department?.Name ?? ""),
                    user.IsActive ? "true" : "false",
                    EscapeCsvField(user.Phone ?? ""),
                    EscapeCsvField(user.JobTitle ?? ""),
                    EscapeCsvField(user.Rank.ToString()),
                    user.HireDate.HasValue ? user.HireDate.Value.ToString("yyyy-MM-dd") : ""
                ));
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
            LogErrorExportingUsersToCsv(_logger, ex);
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
    /// Maps UserRole enum + JobType name to the correct RoleTemplate key.
    /// Delegates to centralized RoleTemplateMapper to ensure consistency across codebase.
    /// </summary>
    private static string MapUserRoleToRoleTemplateKey(UserRole role, string? jobTypeName)
        => Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(role, jobTypeName);

    /// <summary>
    /// Builds the correct GrantScope based on the role template key.
    /// Looks up the company → molecule → area → project hierarchy to populate the right scope parameters.
    /// </summary>
    private Task<GrantScope> BuildGrantScopeForTemplateAsync(string roleTemplateKey, int companyId, int? jobTypeId)
        => _grantService.BuildRoleTemplateScopeAsync(roleTemplateKey, companyId, jobTypeId);

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

            // Case-insensitive (CSV import uniqueness must match Login/Griffin lookup case-folding).
            var importEmailLower = email.ToLowerInvariant();
            if (await _db.Users.AnyAsync(u => u.Email.ToLower() == importEmailLower))
            {
                skipped++;
                continue;
            }

            // Try parsing as template key first, then fall back to UserRole enum
            RoleTemplate? importTemplate = null;
            UserRole role;

            // Look up template by key (supports custom roles)
            importTemplate = await _roleService.GetRoleTemplateByKeyAsync(roleStr);

            if (importTemplate != null)
            {
                if (!importTemplate.DerivedUserRole.HasValue)
                    LogRoleTemplateMissingDerivedRole(_logger, importTemplate.Key, importTemplate.Id);
                role = importTemplate.DerivedUserRole ?? UserRole.Employee;
            }
            else if (Enum.TryParse<UserRole>(roleStr, ignoreCase: true, out var parsedRole))
            {
                role = parsedRole;
                // Map enum to template
                var templateKey = MapUserRoleToRoleTemplateKey(role, null);
                importTemplate = await _roleService.GetRoleTemplateByKeyAsync(templateKey);
            }
            else
            {
                role = UserRole.Employee;
                importTemplate = await _roleService.GetRoleTemplateByKeyAsync("Employee");
            }

            // Don't allow bulk creation of Owner/Director/AreaAdmin
            if (role == UserRole.Owner || role == UserRole.Director || role == UserRole.AreaAdmin)
            {
                role = UserRole.Employee;
                importTemplate = await _roleService.GetRoleTemplateByKeyAsync("Employee");
            }

            var (hash, salt) = PasswordHasher.CreateHash(password);
            var user = new AppUser
            {
                Email = email,
                DisplayName = displayName,
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = role,
                RoleTemplateId = importTemplate?.Id,
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
                CompanyId = companyId,
                IsActive = true,
                MustChangePassword = true // Force password change on first login
            };

            _db.Users.Add(user);
            created++;
        }

        if (created > 0)
        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "AppUser");
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }
        }

        var resultParts = new List<string>();
        resultParts.Add($"Created: {created}");
        if (skipped > 0) resultParts.Add($"Skipped (existing): {skipped}");
        if (errors.Count > 0) resultParts.Add($"Errors: {errors.Count}");
        BulkImportResult = string.Join(" | ", resultParts);
        if (errors.Count > 0)
            BulkImportResult += "\n" + string.Join("\n", errors.Take(10));

        LogBulkImportCompleted(_logger, created, skipped, errors.Count, companyId);

        return Page();
    }
}
