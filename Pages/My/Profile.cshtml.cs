using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Dto;
using ShiftManager.Pages;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.My;

[Authorize]
public class ProfileModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IProfileService _profileService;
    private readonly IAvatarService _avatarService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IGrantService _grantService;
    private readonly IAuditLogService _auditLogService;

    public ProfileModel(
        AppDbContext db,
        IProfileService profileService,
        IAvatarService avatarService,
        ITenantResolver tenantResolver,
        IGrantService grantService,
        IAuditLogService auditLogService,
        IStringLocalizer<SharedResources> localizer) : base(localizer)
    {
        _db = db;
        _profileService = profileService;
        _avatarService = avatarService;
        _tenantResolver = tenantResolver;
        _grantService = grantService;
        _auditLogService = auditLogService;
    }

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
    public IFormFile? AvatarFile { get; set; }

    [BindProperty]
    public DateOnly? HireDate { get; set; }

    // Computed display-only fields (derived from user's organizational hierarchy)
    public string? MoleculeName { get; set; }
    public string? ComputedJobTitle { get; set; }

    public string? AvatarUrl { get; set; }
    public string? InitialsForAvatar { get; set; }
    // SuccessMessage / ErrorMessage removed — feedback now flows through TempData → _Layout FeedbackModal bridge.

    public List<string> SkillsList { get; set; } = new();
    public List<string> CertificationsList { get; set; } = new();

    // Grant-based access flag (set in OnGetAsync)
    public bool CanEditProfessionalInfo { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        // Grant-based check for professional info editing (Owner-level access)
        CanEditProfessionalInfo = await _grantService.HasGrantAsync(userId, "AdminAccess");

        await LoadUserDataAsync(user);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        // ✅ SECURITY FIX: Input validation
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_DisplayNameRequired"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (DisplayName.Length > 200)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_DisplayNameTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(PreferredName) && PreferredName.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_PreferredNameTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Phone) && Phone.Length > 50)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_PhoneTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(City) && City.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_CityTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Skills) && Skills.Length > 5000)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_SkillsTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Certifications) && Certifications.Length > 5000)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_CertificationsTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactName) && EmergencyContactName.Length > 200)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_EmergencyContactNameTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactPhone) && EmergencyContactPhone.Length > 50)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_EmergencyContactPhoneTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactRelation) && EmergencyContactRelation.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_EmergencyContactRelationTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        // Handle avatar upload first
        if (AvatarFile != null)
        {
            var (success, fileName, error) = await _avatarService.UploadAvatarAsync(userId, AvatarFile);
            if (!success)
            {
                TempData["ErrorMessage"] = error; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await LoadUserDataAsync(user);
                return Page();
            }
        }

        // Check if user is Owner (can edit HireDate)
        var isOwner = await _grantService.HasGrantAsync(userId, "AdminAccess");

        // Validate HireDate for Owner
        if (isOwner && HireDate.HasValue && HireDate.Value > DateOnly.FromDateTime(DateTime.Today))
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_HireDateFuture"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        // Parse skills and certifications from JSON strings if needed
        var skillsList = ParseJsonArrayOrCommaSeparated(Skills);
        var certificationsList = ParseJsonArrayOrCommaSeparated(Certifications);

        // Update profile
        var dto = new ProfileUpdateDto
        {
            DisplayName = DisplayName,
            PreferredName = PreferredName,
            Phone = Phone,
            City = City,
            DateOfBirth = DateOfBirth,
            Skills = skillsList.Any() ? JsonSerializer.Serialize(skillsList) : null,
            Certifications = certificationsList.Any() ? JsonSerializer.Serialize(certificationsList) : null,
            EmergencyContactName = EmergencyContactName,
            EmergencyContactPhone = EmergencyContactPhone,
            EmergencyContactRelation = EmergencyContactRelation,
            // Preserve existing values (Molecule and JobTitle are now computed display-only)
            Department = user.LegacyDepartment,
            JobTitle = user.JobTitle,
            HireDate = isOwner ? HireDate : user.HireDate
        };

        var (updateSuccess, updateError) = await _profileService.UpdateProfileAsync(userId, userId, dto);

        if (!updateSuccess)
        {
            TempData["ErrorMessage"] = updateError; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadUserDataAsync(user);
            return Page();
        }

        await _auditLogService.LogAsync("ProfileUpdated", "User", userId,
            $"Updated profile for user '{DisplayName}'");

        TempData["SuccessMessage"] = (isOwner && HireDate != user.HireDate
            ? _localizer["Profile_Success_UpdatedWithProfessionalInfo"]
            : _localizer["Profile_Success_Updated"]).Value;

        // Reload user data + refresh the auth cookie so DisplayName / PreferredName / Email
        // / AvatarFileName changes take effect on the very next page render without forcing
        // a sign-out. See Helpers/ClaimsRefresher for the patch semantics.
        user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            await Helpers.ClaimsRefresher.RefreshIfSelfAsync(HttpContext, userId, user);
        }
        await LoadUserDataAsync(user!);

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAvatarAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }
        var success = await _avatarService.DeleteAvatarAsync(userId);

        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Profile_Success_AvatarDeleted"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Profile_Error_AvatarDeleteFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }

        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            // Avatar removal also nulls user.AvatarFileName — drop the stale claim from the cookie.
            await Helpers.ClaimsRefresher.RefreshIfSelfAsync(HttpContext, userId, user);
        }
        await LoadUserDataAsync(user!);

        return Page();
    }

    private async Task LoadUserDataAsync(AppUser user)
    {
        DisplayName = user.DisplayName;
        PreferredName = user.PreferredName;
        Phone = user.Phone;
        City = user.City;
        DateOfBirth = user.DateOfBirth;
        HireDate = user.HireDate;
        EmergencyContactName = user.EmergencyContactName;
        EmergencyContactPhone = user.EmergencyContactPhone;
        EmergencyContactRelation = user.EmergencyContactRelation;

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
        SkillsList = ParseJsonArray(user.Skills);
        CertificationsList = ParseJsonArray(user.Certifications);
        Skills = string.Join(", ", SkillsList);
        Certifications = string.Join(", ", CertificationsList);

        // Avatar — supply the user's own CompanyId so the URL is always correct, even if the
        // active tenant were to differ (e.g. after a future multi-company context switch).
        AvatarUrl = _avatarService.GetAvatarUrl(user.Id, user.AvatarFileName, thumbnail: false, ownerCompanyId: user.CompanyId);
        InitialsForAvatar = _avatarService.GetDefaultAvatarInitials(user.DisplayName);
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
}
