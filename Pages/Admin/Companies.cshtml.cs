using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsAdmin")]
public class CompaniesModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<CompaniesModel> _logger;

    public CompaniesModel(AppDbContext db, ILogger<CompaniesModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public record CompanyVM(int Id, string Name, string? Slug, string? DisplayName, int UserCount);
    public record DirectorVM(int Id, string DisplayName, string Email);

    public List<CompanyVM> Companies { get; set; } = new();
    public List<DirectorVM> AvailableDirectors { get; set; } = new();

    [BindProperty] public string CompanyName { get; set; } = string.Empty;
    [BindProperty] public string CompanySlug { get; set; } = string.Empty;
    [BindProperty] public string CompanyDisplayName { get; set; } = string.Empty;

    [BindProperty] public int? SelectedDirectorId { get; set; }

    [BindProperty, EmailAddress] public string ManagerEmail { get; set; } = string.Empty;
    [BindProperty] public string ManagerDisplayName { get; set; } = string.Empty;
    [BindProperty] public string ManagerPassword { get; set; } = string.Empty;

    [BindProperty] public int RenameCompanyId { get; set; }
    [BindProperty] public string NewCompanyName { get; set; } = string.Empty;

    public string? Error { get; set; }
    public string? Success { get; set; }

    public async Task OnGetAsync()
    {
        // Get success message from TempData if available
        if (TempData["SuccessMessage"] is string successMsg)
        {
            Success = successMsg;
        }

        Companies = await _db.Companies
            .OrderBy(c => c.Name)
            .Select(c => new CompanyVM(
                c.Id,
                c.Name,
                c.Slug,
                c.DisplayName,
                _db.Users.Count(u => u.CompanyId == c.Id)
            ))
            .ToListAsync();

        // Load all Director users
        AvailableDirectors = await _db.Users
            .Where(u => u.Role == UserRole.Director)
            .OrderBy(u => u.DisplayName)
            .Select(u => new DirectorVM(u.Id, u.DisplayName, u.Email))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAddCompanyAsync()
    {
        await OnGetAsync();

        // ✅ SECURITY FIX: Enhanced input validation with length constraints
        if (string.IsNullOrWhiteSpace(CompanyName) || string.IsNullOrWhiteSpace(CompanySlug))
        {
            Error = "Company name and slug are required.";
            return Page();
        }

        // Validate field lengths to prevent DoS via large strings
        if (CompanyName.Length > 200 || CompanySlug.Length > 100)
        {
            Error = "Company name or slug exceeds maximum length (200/100 characters).";
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(CompanyDisplayName) && CompanyDisplayName.Length > 200)
        {
            Error = "Company display name must not exceed 200 characters.";
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(ManagerDisplayName) && ManagerDisplayName.Length > 200)
        {
            Error = "Manager display name must not exceed 200 characters.";
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(ManagerEmail) && ManagerEmail.Length > 255)
        {
            Error = "Manager email must not exceed 255 characters.";
            return Page();
        }

        // Validate slug is unique
        if (await _db.Companies.AnyAsync(c => c.Slug == CompanySlug))
        {
            Error = "Company slug already exists. Please choose a different slug.";
            return Page();
        }

        // Validate slug format (lowercase, alphanumeric, hyphens only)
        if (!System.Text.RegularExpressions.Regex.IsMatch(CompanySlug, @"^[a-z0-9-]+$"))
        {
            Error = "Company slug must be lowercase and contain only letters, numbers, and hyphens.";
            return Page();
        }

        // Check if Director is selected
        bool useDirector = SelectedDirectorId.HasValue && SelectedDirectorId.Value > 0;

        if (!useDirector)
        {
            // Validate all fields for manager (only if not using a Director)
            if (string.IsNullOrWhiteSpace(ManagerEmail) ||
                string.IsNullOrWhiteSpace(ManagerDisplayName) ||
                string.IsNullOrWhiteSpace(ManagerPassword))
            {
                Error = "Manager email, display name, and password are required (or select an existing Director).";
                return Page();
            }

            // Validate manager email is unique
            if (await _db.Users.AnyAsync(u => u.Email == ManagerEmail))
            {
                Error = "Manager email already exists.";
                return Page();
            }
        }
        else
        {
            // Validate that the selected Director exists
            var directorExists = await _db.Users.AnyAsync(u => u.Id == SelectedDirectorId!.Value && u.Role == UserRole.Director);
            if (!directorExists)
            {
                Error = "Selected Director does not exist or is not a Director.";
                return Page();
            }
        }

        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // 1. Create the company
            var company = new Company
            {
                Name = CompanyName,
                Slug = CompanySlug,
                DisplayName = string.IsNullOrWhiteSpace(CompanyDisplayName) ? CompanyName : CompanyDisplayName
            };

            _db.Companies.Add(company);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created new company: {CompanyName} (ID: {CompanyId}, Slug: {CompanySlug})",
                company.Name, company.Id, company.Slug);

            // 2. Either create manager or assign director
            string successUserInfo;
            if (useDirector)
            {
                // Assign the existing Director to this company
                var director = await _db.Users.FindAsync(SelectedDirectorId!.Value);
                // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var grantedBy))
                {
                    grantedBy = 0; // Fallback for audit trail
                }

                var directorAssignment = new DirectorCompany
                {
                    UserId = SelectedDirectorId!.Value,
                    CompanyId = company.Id,
                    GrantedBy = grantedBy,
                    GrantedAt = DateTime.UtcNow
                };

                _db.DirectorCompanies.Add(directorAssignment);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Assigned Director {DirectorId} ({DirectorEmail}) to company {CompanyName}",
                    SelectedDirectorId.Value, director?.Email, company.Name);

                successUserInfo = $"Director '{director?.DisplayName}' ({director?.Email}) has been assigned to manage this company.";
            }
            else
            {
                // Create the manager user for this company
                var (hash, salt) = PasswordHasher.CreateHash(ManagerPassword);
                var manager = new AppUser
                {
                    CompanyId = company.Id,
                    Email = ManagerEmail,
                    DisplayName = ManagerDisplayName,
                    Role = UserRole.Manager,
                    IsActive = true,
                    PasswordHash = hash,
                    PasswordSalt = salt
                };

                _db.Users.Add(manager);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Created manager user {ManagerEmail} for company {CompanyName}",
                    ManagerEmail, company.Name);

                successUserInfo = $"Manager can log in with email: {ManagerEmail}";
            }

            // 3. Create default shift types for the new company
            var defaultShiftTypes = new[]
            {
                new ShiftType { CompanyId = company.Id, Key = "MORNING", Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0) },
                new ShiftType { CompanyId = company.Id, Key = "NOON", Start = new TimeOnly(16, 0), End = new TimeOnly(0, 0) },
                new ShiftType { CompanyId = company.Id, Key = "NIGHT", Start = new TimeOnly(0, 0), End = new TimeOnly(8, 0) },
                new ShiftType { CompanyId = company.Id, Key = "MIDDLE", Start = new TimeOnly(12, 0), End = new TimeOnly(20, 0) }
            };

            _db.ShiftTypes.AddRange(defaultShiftTypes);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created default shift types for company {CompanyName}", company.Name);

            // 4. Create default config for the new company
            var defaultConfigs = new[]
            {
                new AppConfig { CompanyId = company.Id, Key = "RestHours", Value = "8" },
                new AppConfig { CompanyId = company.Id, Key = "WeeklyHoursCap", Value = "40" }
            };

            _db.Configs.AddRange(defaultConfigs);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created default config for company {CompanyName}", company.Name);

            await transaction.CommitAsync();

            TempData["SuccessMessage"] = $"Company '{company.Name}' created successfully. {successUserInfo}";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating company {CompanyName}", CompanyName);
            Error = "An error occurred while creating the company. Please try again.";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRenameCompanyAsync()
    {
        if (RenameCompanyId <= 0 || string.IsNullOrWhiteSpace(NewCompanyName))
        {
            TempData["ErrorMessage"] = "Invalid company ID or name.";
            return RedirectToPage();
        }

        var company = await _db.Companies.FindAsync(RenameCompanyId);
        if (company == null)
        {
            TempData["ErrorMessage"] = "Company not found.";
            return RedirectToPage();
        }

        company.Name = NewCompanyName;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Company {CompanyId} renamed to {NewName}", RenameCompanyId, NewCompanyName);
        TempData["SuccessMessage"] = $"Company renamed to '{NewCompanyName}' successfully.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCompanyAsync(int id)
    {
        var company = await _db.Companies.FindAsync(id);
        if (company == null)
        {
            TempData["ErrorMessage"] = "Company not found.";
            return RedirectToPage();
        }

        // CRITICAL: Check if any Owner users belong to this company
        var hasOwnerUsers = await _db.Users.AnyAsync(u => u.CompanyId == id && u.Role == UserRole.Owner);
        if (hasOwnerUsers)
        {
            TempData["ErrorMessage"] = "Cannot delete company: Owner users must be removed or transferred first. Owners must always remain in the system.";
            _logger.LogWarning("Attempted to delete company {CompanyId} with Owner users still assigned", id);
            return RedirectToPage();
        }

        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // Delete all related data for this company

            // 1. Delete all swap requests
            var swapRequests = await _db.SwapRequests.Where(sr => sr.CompanyId == id).ToListAsync();
            _db.SwapRequests.RemoveRange(swapRequests);

            // 2. Delete all time-off requests
            var timeOffRequests = await _db.TimeOffRequests.Where(tor => tor.CompanyId == id).ToListAsync();
            _db.TimeOffRequests.RemoveRange(timeOffRequests);

            // 3. Delete all shift assignments
            var shiftAssignments = await _db.ShiftAssignments.Where(sa => sa.CompanyId == id).ToListAsync();
            _db.ShiftAssignments.RemoveRange(shiftAssignments);

            // 4. Delete all shift instances
            var shiftInstances = await _db.ShiftInstances.Where(si => si.CompanyId == id).ToListAsync();
            _db.ShiftInstances.RemoveRange(shiftInstances);

            // 5. Delete all shift types
            var shiftTypes = await _db.ShiftTypes.Where(st => st.CompanyId == id).ToListAsync();
            _db.ShiftTypes.RemoveRange(shiftTypes);

            // 6. Delete all configs
            var configs = await _db.Configs.Where(c => c.CompanyId == id).ToListAsync();
            _db.Configs.RemoveRange(configs);

            // 7. Delete all director assignments
            var directorAssignments = await _db.DirectorCompanies.Where(dc => dc.CompanyId == id).ToListAsync();
            _db.DirectorCompanies.RemoveRange(directorAssignments);

            // 8. Delete all user notifications
            var notifications = await _db.UserNotifications.Where(n => n.CompanyId == id).ToListAsync();
            _db.UserNotifications.RemoveRange(notifications);

            // 9. Delete all join requests
            var joinRequests = await _db.UserJoinRequests.Where(jr => jr.CompanyId == id).ToListAsync();
            _db.UserJoinRequests.RemoveRange(joinRequests);

            // 10. Delete all non-Owner users (Owners were already checked above)
            var users = await _db.Users.Where(u => u.CompanyId == id && u.Role != UserRole.Owner).ToListAsync();
            _db.Users.RemoveRange(users);

            // 11. Finally, delete the company itself
            _db.Companies.Remove(company);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Company {CompanyId} ({CompanyName}) deleted successfully", id, company.Name);
            TempData["SuccessMessage"] = $"Company '{company.Name}' and all associated data deleted successfully.";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error deleting company {CompanyId}", id);
            TempData["ErrorMessage"] = "An error occurred while deleting the company. Please try again.";
            return RedirectToPage();
        }
    }
}
