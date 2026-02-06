using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Dto;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.My;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IProfileService _profileService;
    private readonly IAvatarService _avatarService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IGrantService _grantService;

    public ProfileModel(
        AppDbContext db,
        IProfileService profileService,
        IAvatarService avatarService,
        ITenantResolver tenantResolver,
        IGrantService grantService)
    {
        _db = db;
        _profileService = profileService;
        _avatarService = avatarService;
        _tenantResolver = tenantResolver;
        _grantService = grantService;
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

    // Owner-editable fields (read-only for other users)
    [BindProperty]
    public string? Department { get; set; }

    [BindProperty]
    public string? JobTitle { get; set; }

    [BindProperty]
    public DateOnly? HireDate { get; set; }

    public string? AvatarUrl { get; set; }
    public string? InitialsForAvatar { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

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

        LoadUserData(user);

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
            ErrorMessage = "Display name is required.";
            LoadUserData(user);
            return Page();
        }

        if (DisplayName.Length > 200)
        {
            ErrorMessage = "Display name must not exceed 200 characters.";
            LoadUserData(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(PreferredName) && PreferredName.Length > 100)
        {
            ErrorMessage = "Preferred name must not exceed 100 characters.";
            LoadUserData(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Phone) && Phone.Length > 50)
        {
            ErrorMessage = "Phone must not exceed 50 characters.";
            LoadUserData(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(City) && City.Length > 100)
        {
            ErrorMessage = "City must not exceed 100 characters.";
            LoadUserData(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Skills) && Skills.Length > 5000)
        {
            ErrorMessage = "Skills must not exceed 5000 characters.";
            LoadUserData(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Certifications) && Certifications.Length > 5000)
        {
            ErrorMessage = "Certifications must not exceed 5000 characters.";
            LoadUserData(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactName) && EmergencyContactName.Length > 200)
        {
            ErrorMessage = "Emergency contact name must not exceed 200 characters.";
            LoadUserData(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactPhone) && EmergencyContactPhone.Length > 50)
        {
            ErrorMessage = "Emergency contact phone must not exceed 50 characters.";
            LoadUserData(user);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactRelation) && EmergencyContactRelation.Length > 100)
        {
            ErrorMessage = "Emergency contact relation must not exceed 100 characters.";
            LoadUserData(user);
            return Page();
        }

        // Handle avatar upload first
        if (AvatarFile != null)
        {
            var (success, fileName, error) = await _avatarService.UploadAvatarAsync(userId, AvatarFile);
            if (!success)
            {
                ErrorMessage = error;
                LoadUserData(user);
                return Page();
            }
        }

        // Check if user is Owner (can edit Department, JobTitle, HireDate)
        var isOwner = await _grantService.HasGrantAsync(userId, "AdminAccess");

        // Validate HireDate for Owner
        if (isOwner && HireDate.HasValue && HireDate.Value > DateOnly.FromDateTime(DateTime.Today))
        {
            ErrorMessage = "Hire date cannot be in the future.";
            LoadUserData(user);
            return Page();
        }

        // Validate Department and JobTitle length
        if (isOwner && Department != null && Department.Length > 100)
        {
            ErrorMessage = "Department name too long (max 100 characters).";
            LoadUserData(user);
            return Page();
        }

        if (isOwner && JobTitle != null && JobTitle.Length > 100)
        {
            ErrorMessage = "Job title too long (max 100 characters).";
            LoadUserData(user);
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
            // Owner-only fields
            Department = isOwner ? Department : user.LegacyDepartment,
            JobTitle = isOwner ? JobTitle : user.JobTitle,
            HireDate = isOwner ? HireDate : user.HireDate
        };

        var (updateSuccess, updateError) = await _profileService.UpdateProfileAsync(userId, userId, dto);

        if (!updateSuccess)
        {
            ErrorMessage = updateError;
            LoadUserData(user);
            return Page();
        }

        SuccessMessage = isOwner && (Department != user.LegacyDepartment || JobTitle != user.JobTitle || HireDate != user.HireDate)
            ? "Profile updated successfully! Professional information has been updated."
            : "Profile updated successfully!";

        // Reload user data
        user = await _db.Users.FindAsync(userId);
        LoadUserData(user!);

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
            SuccessMessage = "Avatar deleted successfully!";
        }
        else
        {
            ErrorMessage = "Failed to delete avatar.";
        }

        var user = await _db.Users.FindAsync(userId);
        LoadUserData(user!);

        return Page();
    }

    private void LoadUserData(AppUser user)
    {
        DisplayName = user.DisplayName;
        PreferredName = user.PreferredName;
        Phone = user.Phone;
        City = user.City;
        DateOfBirth = user.DateOfBirth;
        Department = user.LegacyDepartment;
        JobTitle = user.JobTitle;
        HireDate = user.HireDate;
        EmergencyContactName = user.EmergencyContactName;
        EmergencyContactPhone = user.EmergencyContactPhone;
        EmergencyContactRelation = user.EmergencyContactRelation;

        // Parse skills and certifications
        SkillsList = ParseJsonArray(user.Skills);
        CertificationsList = ParseJsonArray(user.Certifications);
        Skills = string.Join(", ", SkillsList);
        Certifications = string.Join(", ", CertificationsList);

        // Avatar
        AvatarUrl = _avatarService.GetAvatarUrl(user.Id, user.AvatarFileName, thumbnail: false);
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
