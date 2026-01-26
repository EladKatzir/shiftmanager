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

    // v3.0 Organizational Hierarchy
    [BindProperty]
    public int? JobTypeId { get; set; }

    [BindProperty]
    public int? DepartmentId { get; set; }

    public string? AvatarUrl { get; set; }
    public string? InitialsForAvatar { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public List<ProfileChangeAudit> RecentChanges { get; set; } = new();

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
            return BadRequest("Invalid user ID");
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

        LoadUserData(user);
        await LoadOrganizationalOptionsAsync(user);
        await LoadRecentChangesAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // ✅ SECURITY FIX: Input validation
        if (UserId <= 0)
        {
            return BadRequest("Invalid user ID");
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(DisplayName))
        {
            ErrorMessage = "Email and Display Name are required.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        // Length validation to prevent DoS and database errors
        if (Email.Length > 255)
        {
            ErrorMessage = "Email must not exceed 255 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (DisplayName.Length > 200)
        {
            ErrorMessage = "Display name must not exceed 200 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(PreferredName) && PreferredName.Length > 100)
        {
            ErrorMessage = "Preferred name must not exceed 100 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Phone) && Phone.Length > 50)
        {
            ErrorMessage = "Phone must not exceed 50 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(City) && City.Length > 100)
        {
            ErrorMessage = "City must not exceed 100 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Department) && Department.Length > 100)
        {
            ErrorMessage = "Department must not exceed 100 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(JobTitle) && JobTitle.Length > 100)
        {
            ErrorMessage = "Job title must not exceed 100 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Skills) && Skills.Length > 5000)
        {
            ErrorMessage = "Skills must not exceed 5000 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Certifications) && Certifications.Length > 5000)
        {
            ErrorMessage = "Certifications must not exceed 5000 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactName) && EmergencyContactName.Length > 200)
        {
            ErrorMessage = "Emergency contact name must not exceed 200 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactPhone) && EmergencyContactPhone.Length > 50)
        {
            ErrorMessage = "Emergency contact phone must not exceed 50 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EmergencyContactRelation) && EmergencyContactRelation.Length > 100)
        {
            ErrorMessage = "Emergency contact relation must not exceed 100 characters.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

        // Basic email format validation
        if (!Email.Contains('@') || Email.Length < 3)
        {
            ErrorMessage = "Invalid email format.";
            var user = await _db.Users.FindAsync(UserId);
            if (user != null)
            {
                LoadUserData(user);
                await LoadOrganizationalOptionsAsync(user);
            }
            await LoadRecentChangesAsync();
            return Page();
        }

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
            await LoadOrganizationalOptionsAsync(targetUser);
            await LoadRecentChangesAsync();
            return Page();
        }

        // Update v3.0 organizational fields directly (not in ProfileUpdateDto)
        targetUser.JobTypeId = JobTypeId;
        targetUser.DepartmentId = DepartmentId;
        await _db.SaveChangesAsync();

        SuccessMessage = "Profile updated successfully!";

        // Reload user data
        targetUser = await _db.Users
            .Include(u => u.JobType)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == UserId);
        LoadUserData(targetUser!);
        await LoadOrganizationalOptionsAsync(targetUser!);
        await LoadRecentChangesAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAvatarAsync()
    {
        // ✅ SECURITY FIX: Input validation
        if (UserId <= 0)
        {
            return BadRequest("Invalid user ID");
        }

        var success = await _avatarService.DeleteAvatarAsync(UserId);

        if (success)
        {
            SuccessMessage = "Avatar deleted successfully!";
        }
        else
        {
            ErrorMessage = "Failed to delete avatar.";
        }

        var user = await _db.Users
            .Include(u => u.JobType)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == UserId);
        LoadUserData(user!);
        await LoadOrganizationalOptionsAsync(user!);
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
        Department = user.LegacyDepartment;
        JobTitle = user.JobTitle;
        HireDate = user.HireDate;
        EmergencyContactName = user.EmergencyContactName;
        EmergencyContactPhone = user.EmergencyContactPhone;
        EmergencyContactRelation = user.EmergencyContactRelation;
        Role = user.Role;
        IsActive = user.IsActive;
        JobTypeId = user.JobTypeId;
        DepartmentId = user.DepartmentId;

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

            if (IsWorkforceMolecule)
            {
                // Load job types for the area
                var areaId = company.Molecule.AreaId;
                AvailableJobTypes = await _db.JobTypes
                    .IgnoreQueryFilters()
                    .Where(jt => jt.AreaId == areaId && jt.IsActive)
                    .Include(jt => jt.Area)
                    .OrderBy(jt => jt.SortOrder).ThenBy(jt => jt.Name)
                    .Select(jt => new JobTypeOption(jt.Id, jt.DisplayName, jt.Area.DisplayName))
                    .ToListAsync();
            }
            else if (IsTechMolecule)
            {
                // Load departments for the molecule
                var moleculeId = company.MoleculeId!.Value;
                AvailableDepartments = await _db.Departments
                    .IgnoreQueryFilters()
                    .Where(d => d.MoleculeId == moleculeId && d.IsActive)
                    .Include(d => d.Molecule)
                    .OrderBy(d => d.Name)
                    .Select(d => new DepartmentOption(d.Id, d.DisplayName, d.Molecule.DisplayName))
                    .ToListAsync();
            }
        }

        // Load grants count
        GrantsCount = await _db.Grants
            .IgnoreQueryFilters()
            .CountAsync(g => g.UserId == user.Id);
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
