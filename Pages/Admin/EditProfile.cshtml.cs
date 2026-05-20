using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Dto;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Admin;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManagerHomeAccess policy;
// hierarchy lookups (Companies, JobTypes, Departments, Grants) are reference data for profile editing
[Authorize(Policy = "Grant:ManagerHomeAccess")]
public class EditProfileModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IProfileService _profileService;
    private readonly IAvatarService _avatarService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IAuditLogService _auditLogService;
    private readonly IRoleService _roleService;
    private readonly IJobTypeService _jobTypeService;

    public EditProfileModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IProfileService profileService,
        IAvatarService avatarService,
        ITenantResolver tenantResolver,
        IAuditLogService auditLogService,
        IRoleService roleService,
        IJobTypeService jobTypeService)
        : base(localizer)
    {
        _db = db;
        _profileService = profileService;
        _avatarService = avatarService;
        _tenantResolver = tenantResolver;
        _auditLogService = auditLogService;
        _roleService = roleService;
        _jobTypeService = jobTypeService;
    }

    [BindProperty(SupportsGet = true)]
    public int UserId { get; set; }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string? PreferredName { get; set; }

    [BindProperty]
    public string? Phone { get; set; }

    [BindProperty]
    public string? City { get; set; }

    [BindProperty]
    public DateOnly? DateOfBirth { get; set; }

    [BindProperty]
    public DateOnly? HireDate { get; set; }

    // Computed display-only fields (derived from user's organizational hierarchy)
    public string? MoleculeName { get; set; }
    public string? ComputedJobTitle { get; set; }

    [BindProperty]
    public string? Skills { get; set; }

    [BindProperty]
    public string? Certifications { get; set; }

    [BindProperty]
    public string? EmergencyContactName { get; set; }

    [BindProperty]
    public string? EmergencyContactPhone { get; set; }

    [BindProperty]
    public string? EmergencyContactRelation { get; set; }

    [BindProperty]
    public UserRole Role { get; set; }

    [BindProperty]
    public int? RoleTemplateId { get; set; }

    [BindProperty]
    public bool IsActive { get; set; }

    [BindProperty]
    public IFormFile? AvatarFile { get; set; }

    // v3.0 Organizational Hierarchy
    [BindProperty]
    public int? JobTypeId { get; set; }

    [BindProperty]
    public int? DepartmentId { get; set; }

    // Military Rank
    [BindProperty]
    public MilitaryRank Rank { get; set; } = MilitaryRank.Turai;

    public List<SelectListItem> RankOptions { get; set; } = new();

    public string? AvatarUrl { get; set; }
    public string? InitialsForAvatar { get; set; }
    // SuccessMessage / ErrorMessage removed — feedback now flows through TempData → _Layout FeedbackModal bridge.

    public List<ProfileChangeAudit> RecentChanges { get; set; } = new();
    public List<RoleTemplate> AvailableRoleTemplates { get; set; } = new();

    // v3.0 Dropdown options
    public record JobTypeOption(int Id, string Name, string AreaName);
    public record DepartmentOption(int Id, string Name, string MoleculeName);
    public List<JobTypeOption> AvailableJobTypes { get; set; } = new();
    public List<DepartmentOption> AvailableDepartments { get; set; } = new();
    public int GrantsCount { get; set; }
    public bool IsWorkforceMolecule { get; set; }
    public bool IsTechMolecule { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // ✅ SECURITY FIX: Input validation
        if (UserId <= 0)
        {
            return BadRequest(_localizer["Error_InvalidUserId"].Value);
        }

        var user = await _db.Users
            .Include(u => u.JobType)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == UserId);
        if (user == null)
        {
            return NotFound();
        }

        // Verify same company
        var companyId = _tenantResolver.GetCurrentTenantId();
        if (user.CompanyId != companyId)
        {
            return Forbid();
        }

        await LoadUserDataAsync(user);
        await LoadOrganizationalOptionsAsync(user);
        await LoadRecentChangesAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // ✅ SECURITY FIX: Input validation
        if (UserId <= 0)
        {
            return BadRequest(_localizer["Error_InvalidUserId"].Value);
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(DisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_EmailAndDisplayNameRequired"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        // Length validation to prevent DoS and database errors
        if (Email.Length > 255)
        {
            TempData["ErrorMessage"] = _localizer["Error_EmailTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (DisplayName.Length > 200)
        {
            TempData["ErrorMessage"] = _localizer["Error_DisplayNameTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(PreferredName) && PreferredName.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_PreferredNameTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Phone) && Phone.Length > 50)
        {
            TempData["ErrorMessage"] = _localizer["Error_PhoneTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(City) && City.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_CityTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Skills) && Skills.Length > 5000)
        {
            TempData["ErrorMessage"] = _localizer["Error_SkillsTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Certifications) && Certifications.Length > 5000)
        {
            TempData["ErrorMessage"] = _localizer["Error_CertificationsTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactName) && EmergencyContactName.Length > 200)
        {
            TempData["ErrorMessage"] = _localizer["Error_EmergencyContactNameTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactPhone) && EmergencyContactPhone.Length > 50)
        {
            TempData["ErrorMessage"] = _localizer["Error_EmergencyContactPhoneTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactRelation) && EmergencyContactRelation.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_EmergencyContactRelationTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        // Basic email format validation
        if (!Email.Contains('@') || Email.Length < 3)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidEmailFormat"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                await LoadUserDataAsync(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var editorUserId))
        {
            return BadRequest(_localizer["Error_InvalidUserClaim"].Value);
        }
        var targetUser = await _db.Users.FindAsync(UserId);

        if (targetUser == null)
        {
            return NotFound();
        }

        // Verify same company
        var companyId = _tenantResolver.GetCurrentTenantId();
        if (targetUser.CompanyId != companyId)
        {
            return Forbid();
        }

        // Handle avatar upload first
        if (AvatarFile != null)
        {
            var (success, fileName, error) = await _avatarService.UploadAvatarAsync(UserId, AvatarFile);
            if (!success)
            {
                TempData["ErrorMessage"] = error; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await LoadUserDataAsync(targetUser);
                await LoadOrganizationalOptionsAsync(targetUser);
                await LoadRecentChangesAsync();
                return Page();
            }
        }

        // Parse skills and certifications
        var skillsList = ParseJsonArrayOrCommaSeparated(Skills);
        var certificationsList = ParseJsonArrayOrCommaSeparated(Certifications);

        // Update profile
        var dto = new ProfileUpdateDto
        {
            Email = Email,
            DisplayName = DisplayName,
            PreferredName = PreferredName,
            Phone = Phone,
            City = City,
            DateOfBirth = DateOfBirth,
            Department = targetUser.LegacyDepartment,
            JobTitle = targetUser.JobTitle,
            HireDate = HireDate,
            Skills = skillsList.Any() ? JsonSerializer.Serialize(skillsList) : null,
            Certifications = certificationsList.Any() ? JsonSerializer.Serialize(certificationsList) : null,
            EmergencyContactName = EmergencyContactName,
            EmergencyContactPhone = EmergencyContactPhone,
            EmergencyContactRelation = EmergencyContactRelation,
            Role = Role,
            IsActive = IsActive
        };

        var (updateSuccess, updateError) = await _profileService.UpdateProfileAsync(editorUserId, UserId, dto);

        if (!updateSuccess)
        {
            TempData["ErrorMessage"] = updateError; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(targetUser);
            await LoadOrganizationalOptionsAsync(targetUser);
            await LoadRecentChangesAsync();
            return Page();
        }

        // Update v3.0 organizational fields directly (not in ProfileUpdateDto)
        // Validate job type is valid for user's molecule before assigning
        if (JobTypeId.HasValue && JobTypeId != targetUser.JobTypeId)
        {
            var validation = await _jobTypeService.ValidateJobTypeForUserAsync(targetUser.Id, JobTypeId.Value);
            if (validation != JobTypeValidationResult.Valid)
            {
                TempData["ErrorMessage"] = (validation switch
                {
                    JobTypeValidationResult.MoleculeMismatch => _localizer["Error_JobTypeNotAvailableForMolecule"],
                    JobTypeValidationResult.JobTypeNotFound => _localizer["Error_InvalidJobTypeSelected"],
                    _ => _localizer["Error_InvalidJobTypeSelected"]
                }).Value;
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await LoadUserDataAsync(targetUser);
                await LoadOrganizationalOptionsAsync(targetUser);
                await LoadRecentChangesAsync();
                return Page();
            }
        }
        targetUser.JobTypeId = JobTypeId;
        targetUser.DepartmentId = DepartmentId;

        // Sync RoleTemplate — if RoleTemplateId was submitted, update and sync Role
        if (RoleTemplateId.HasValue && RoleTemplateId != targetUser.RoleTemplateId)
        {
            var newTemplate = await _roleService.GetRoleTemplateAsync(RoleTemplateId.Value);
            if (newTemplate != null)
            {
                targetUser.RoleTemplateId = newTemplate.Id;
                if (newTemplate.DerivedUserRole.HasValue)
                    targetUser.Role = newTemplate.DerivedUserRole.Value;
            }
        }

        // Update military rank (validate enum value first)
        if (Enum.IsDefined(typeof(MilitaryRank), Rank))
        {
            var oldRank = targetUser.Rank;
            targetUser.Rank = Rank;

            // Log rank change if it actually changed
            if (oldRank != Rank)
            {
                var currentCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
                var language = currentCulture.StartsWith("he") ? "he" : "en";
                var change = new ProfileChangeAudit
                {
                    CompanyId = targetUser.CompanyId,
                    TargetUserId = targetUser.Id,
                    FieldName = "Rank",
                    OldValue = oldRank.GetDisplayName(language),
                    NewValue = Rank.GetDisplayName(language),
                    Timestamp = DateTime.UtcNow,
                    ChangedBy = editorUserId
                };
                _db.ProfileChangeAudits.Add(change);

                // Log to main audit log for compliance tracking
                await _auditLogService.LogUserActionAsync(
                    userId: editorUserId,
                    action: "UserRankChanged",
                    entityType: "User",
                    entityId: targetUser.Id,
                    description: $"Changed military rank for {targetUser.DisplayName} ({targetUser.Email})",
                    details: $"{{\"oldRank\": \"{oldRank}\", \"newRank\": \"{Rank}\", \"oldRankDisplay\": \"{oldRank.GetDisplayName(language)}\", \"newRankDisplay\": \"{Rank.GetDisplayName(language)}\"}}"
                );
            }
        }

        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = _localizer["Success_ProfileUpdated"].Value;

        // Reload user data
        targetUser = await _db.Users
            .Include(u => u.JobType)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == UserId);

        // Refresh the auth cookie if the admin is editing their own profile — no-op
        // for admin-editing-someone-else (their browser holds the cookie, not ours).
        // See Helpers/ClaimsRefresher for the patch semantics.
        if (targetUser != null)
        {
            await Helpers.ClaimsRefresher.RefreshIfSelfAsync(HttpContext, UserId, targetUser);
        }

        await LoadUserDataAsync(targetUser!);
        await LoadOrganizationalOptionsAsync(targetUser!);
        await LoadRecentChangesAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAvatarAsync()
    {
        // ✅ SECURITY FIX: Input validation
        if (UserId <= 0)
        {
            return BadRequest(_localizer["Error_InvalidUserId"].Value);
        }

        var success = await _avatarService.DeleteAvatarAsync(UserId);

        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_AvatarDeleted"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_FailedToDeleteAvatar"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }

        var user = await _db.Users
            .Include(u => u.JobType)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == UserId);

        // Avatar removal nulls user.AvatarFileName — drop the stale claim from the
        // admin's cookie if they just deleted their own avatar.
        if (user != null)
        {
            await Helpers.ClaimsRefresher.RefreshIfSelfAsync(HttpContext, UserId, user);
        }

        await LoadUserDataAsync(user!);
        await LoadOrganizationalOptionsAsync(user!);
        await LoadRecentChangesAsync();

        return Page();
    }

    private async Task LoadUserDataAsync(AppUser user)
    {
        Email = user.Email;
        DisplayName = user.DisplayName;
        PreferredName = user.PreferredName;
        Phone = user.Phone;
        City = user.City;
        DateOfBirth = user.DateOfBirth;
        HireDate = user.HireDate;
        EmergencyContactName = user.EmergencyContactName;
        EmergencyContactPhone = user.EmergencyContactPhone;
        EmergencyContactRelation = user.EmergencyContactRelation;
        Role = user.Role;
        RoleTemplateId = user.RoleTemplateId;
        IsActive = user.IsActive;
        JobTypeId = user.JobTypeId;
        DepartmentId = user.DepartmentId;
        Rank = user.Rank;

        // Molecule name: User → Company → Molecule
        // SECURITY: IgnoreQueryFilters safe — fetching by explicit user.CompanyId
        var company = await _db.Companies
            .IgnoreQueryFilters()
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == user.CompanyId);
        MoleculeName = company?.Molecule?.DisplayName;

        // Computed job title: JobType + Role
        var roleKey = $"Role_{user.Role}";
        var roleName = _localizer[roleKey].Value;
        if (user.JobTypeId.HasValue)
        {
            var jobType = await _db.JobTypes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(jt => jt.Id == user.JobTypeId.Value);
            ComputedJobTitle = jobType != null
                ? $"{jobType.DisplayName} — {roleName}"
                : roleName;
        }
        else
        {
            ComputedJobTitle = roleName;
        }

        // Parse skills and certifications
        var skillsList = ParseJsonArray(user.Skills);
        var certificationsList = ParseJsonArray(user.Certifications);
        Skills = string.Join(", ", skillsList);
        Certifications = string.Join(", ", certificationsList);

        // Avatar
        AvatarUrl = _avatarService.GetAvatarUrl(user.Id, user.AvatarFileName, thumbnail: false);
        InitialsForAvatar = _avatarService.GetDefaultAvatarInitials(user.DisplayName);

        // Populate rank dropdown options
        PopulateRankOptions();
    }

    private async Task LoadRecentChangesAsync()
    {
        RecentChanges = await _profileService.GetProfileHistoryAsync(UserId, days: 30);
    }

    private async Task LoadOrganizationalOptionsAsync(AppUser user)
    {
        // Get the user's company and determine molecule type
        var company = await _db.Companies
            .IgnoreQueryFilters()
            .Include(c => c.Molecule)
            .ThenInclude(m => m!.Area)
            .FirstOrDefaultAsync(c => c.Id == user.CompanyId);

        if (company?.Molecule != null)
        {
            var moleculeType = company.Molecule.Type;
            IsWorkforceMolecule = moleculeType == Models.Support.MoleculeType.Workforce;
            IsTechMolecule = moleculeType == Models.Support.MoleculeType.Tech;

            if (IsWorkforceMolecule || IsTechMolecule)
            {
                // Load job types for the user's molecule (Tech molecules now use Companies + optional JobTypes like Workforce)
                var areaDisplayName = company.Molecule.Area?.DisplayName ?? "";
                var jobTypesForMolecule = await _jobTypeService.GetJobTypesForMoleculeAsync(company.MoleculeId!.Value);
                AvailableJobTypes = jobTypesForMolecule
                    .Select(jt => new JobTypeOption(jt.Id, jt.DisplayName, areaDisplayName))
                    .ToList();
            }
        }

        // Load grants count
        GrantsCount = await _db.Grants
            .IgnoreQueryFilters()
            .CountAsync(g => g.UserId == user.Id);

        // Load available role templates for dropdown
        AvailableRoleTemplates = await _roleService.GetAssignableRoleTemplatesAsync();
    }

    private List<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private List<string> ParseJsonArrayOrCommaSeparated(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<string>();

        // Try parsing as JSON first
        if (input.TrimStart().StartsWith("["))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(input);
                return list ?? new List<string>();
            }
            catch
            {
                // Fall through to comma-separated parsing
            }
        }

        // Parse as comma-separated
        return input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => !string.IsNullOrWhiteSpace(s))
                   .ToList();
    }

    private void PopulateRankOptions()
    {
        var currentCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
        var language = currentCulture.StartsWith("he") ? "he" : "en";

        RankOptions = Enum.GetValues<MilitaryRank>()
            .Select(r => new SelectListItem(r.GetDisplayName(language), ((int)r).ToString()))
            .ToList();
    }
}
