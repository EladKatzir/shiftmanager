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

public class ProfileModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IProfileService _profileService;
    private readonly IAvatarService _avatarService;
    private readonly ITenantResolver _tenantResolver;

    public ProfileModel(
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

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        LoadUserData(user);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
        {
            return RedirectToPage("/Auth/Login");
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
        var isOwner = User.IsInRole("Owner");

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
            Department = isOwner ? Department : user.Department,
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

        SuccessMessage = isOwner && (Department != user.Department || JobTitle != user.JobTitle || HireDate != user.HireDate)
            ? "Profile updated successfully! Professional information has been updated."
            : "Profile updated successfully!";

        // Reload user data
        user = await _db.Users.FindAsync(userId);
        LoadUserData(user!);

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAvatarAsync()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
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
        Department = user.Department;
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
