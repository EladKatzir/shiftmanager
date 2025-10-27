using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Dto;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class EditProfileModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IProfileService _profileService;
    private readonly IAvatarService _avatarService;
    private readonly ITenantResolver _tenantResolver;

    public EditProfileModel(
        AppDbContext db,
        IProfileService profileService,
        IAvatarService avatarService,
        ITenantResolver tenantResolver)
    {
        _db = db;
        _profileService = profileService;
        _avatarService = avatarService;
        _tenantResolver = tenantResolver;
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
    public string? Department { get; set; }

    [BindProperty]
    public string? JobTitle { get; set; }

    [BindProperty]
    public DateOnly? HireDate { get; set; }

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
    public bool IsActive { get; set; }

    [BindProperty]
    public IFormFile? AvatarFile { get; set; }

    public string? AvatarUrl { get; set; }
    public string? InitialsForAvatar { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public List<ProfileChangeAudit> RecentChanges { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _db.Users.FindAsync(UserId);
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

        LoadUserData(user);
        await LoadRecentChangesAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var editorUserId))
        {
            return BadRequest("Invalid user claim");
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
                ErrorMessage = error;
                LoadUserData(targetUser);
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
            Department = Department,
            JobTitle = JobTitle,
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
            ErrorMessage = updateError;
            LoadUserData(targetUser);
            await LoadRecentChangesAsync();
            return Page();
        }

        SuccessMessage = "Profile updated successfully!";

        // Reload user data
        targetUser = await _db.Users.FindAsync(UserId);
        LoadUserData(targetUser!);
        await LoadRecentChangesAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAvatarAsync()
    {
        var success = await _avatarService.DeleteAvatarAsync(UserId);

        if (success)
        {
            SuccessMessage = "Avatar deleted successfully!";
        }
        else
        {
            ErrorMessage = "Failed to delete avatar.";
        }

        var user = await _db.Users.FindAsync(UserId);
        LoadUserData(user!);
        await LoadRecentChangesAsync();

        return Page();
    }

    private void LoadUserData(AppUser user)
    {
        Email = user.Email;
        DisplayName = user.DisplayName;
        PreferredName = user.PreferredName;
        Phone = user.Phone;
        City = user.City;
        DateOfBirth = user.DateOfBirth;
        Department = user.Department;
        JobTitle = user.JobTitle;
        HireDate = user.HireDate;
        EmergencyContactName = user.EmergencyContactName;
        EmergencyContactPhone = user.EmergencyContactPhone;
        EmergencyContactRelation = user.EmergencyContactRelation;
        Role = user.Role;
        IsActive = user.IsActive;

        // Parse skills and certifications
        var skillsList = ParseJsonArray(user.Skills);
        var certificationsList = ParseJsonArray(user.Certifications);
        Skills = string.Join(", ", skillsList);
        Certifications = string.Join(", ", certificationsList);

        // Avatar
        AvatarUrl = _avatarService.GetAvatarUrl(user.Id, user.AvatarFileName, thumbnail: false);
        InitialsForAvatar = _avatarService.GetDefaultAvatarInitials(user.DisplayName);
    }

    private async Task LoadRecentChangesAsync()
    {
        RecentChanges = await _profileService.GetProfileHistoryAsync(UserId, days: 30);
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
