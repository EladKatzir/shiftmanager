using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace ShiftManager.Pages.Admin;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditCompany policy;
// company management inherently requires cross-company visibility
[Authorize(Policy = "Grant:EditCompany")]
public class CompaniesModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<CompaniesModel> _logger;

    public CompaniesModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<CompaniesModel> logger) : base(localizer)
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

    public async Task OnGetAsync()
    {
        // Get success message from TempData if available
        if (TempData["SuccessMessage"] is string successMsg)
        {
            Success = successMsg;
        }

        // Auto-generate slugs for legacy companies that don't have one
        await BackfillMissingSlugsAsync();

        // First, get user counts per company using IgnoreQueryFilters
        var userCountsByCompany = await _db.Users
            .IgnoreQueryFilters()
            .GroupBy(u => u.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count);

        Companies = await _db.Companies
            .IgnoreQueryFilters()
            .OrderBy(c => c.Name)
            .Select(c => new CompanyVM(
                c.Id,
                c.Name,
                c.Slug,
                c.DisplayName,
                userCountsByCompany.GetValueOrDefault(c.Id, 0)
            ))
            .ToListAsync();

        // Load all users whose RoleTemplate derives to Director (or legacy Director role)
        AvailableDirectors = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.RoleTemplate)
            .Where(u => u.Role == UserRole.Director
                || (u.RoleTemplate != null && u.RoleTemplate.DerivedUserRole == UserRole.Director))
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
            Error = _localizer["Error_CompanyNameAndSlugRequired"];
            return Page();
        }

        // ✅ SECURITY FIX: Validate input for XSS attempts
        if (ContainsDangerousContent(CompanyName))
        {
            _logger.LogWarning("XSS attempt detected in company name: {CompanyName}", CompanyName);
            Error = _localizer["Error_DangerousContentDetected"].Value;
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(CompanyDisplayName) && ContainsDangerousContent(CompanyDisplayName))
        {
            _logger.LogWarning("XSS attempt detected in company display name: {DisplayName}", CompanyDisplayName);
            Error = _localizer["Error_DangerousContentDetected"].Value;
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(ManagerDisplayName) && ContainsDangerousContent(ManagerDisplayName))
        {
            _logger.LogWarning("XSS attempt detected in manager display name: {DisplayName}", ManagerDisplayName);
            Error = _localizer["Error_DangerousContentDetected"].Value;
            return Page();
        }

        // Validate field lengths to prevent DoS via large strings
        if (CompanyName.Length > 200 || CompanySlug.Length > 100)
        {
            Error = _localizer["Error_CompanyNameOrSlugTooLong"];
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(CompanyDisplayName) && CompanyDisplayName.Length > 200)
        {
            Error = _localizer["Error_CompanyDisplayNameTooLong"];
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(ManagerDisplayName) && ManagerDisplayName.Length > 200)
        {
            Error = _localizer["Error_ManagerDisplayNameTooLong"];
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(ManagerEmail) && ManagerEmail.Length > 255)
        {
            Error = _localizer["Error_ManagerEmailTooLong"];
            return Page();
        }

        // Validate slug is unique
        if (await _db.Companies.AnyAsync(c => c.Slug == CompanySlug))
        {
            Error = _localizer["Error_CompanySlugAlreadyExists"];
            return Page();
        }

        // Validate slug format (lowercase, alphanumeric, hyphens only)
        if (!System.Text.RegularExpressions.Regex.IsMatch(CompanySlug, @"^[a-z0-9-]+$"))
        {
            Error = _localizer["Error_CompanySlugInvalidFormat"];
            return Page();
        }

        // Block path traversal attempts
        if (CompanySlug.Contains("..") || CompanySlug.Contains("/") || CompanySlug.Contains("\\"))
        {
            Error = _localizer["Error_CompanySlugInvalidFormat"];
            _logger.LogWarning("Path traversal attempt detected in company slug: {Slug}", CompanySlug);
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
                Error = _localizer["Error_ManagerFieldsRequired"];
                return Page();
            }

            // Validate manager email is unique
            if (await _db.Users.AnyAsync(u => u.Email == ManagerEmail))
            {
                Error = _localizer["Error_ManagerEmailAlreadyExists"];
                return Page();
            }
        }
        else
        {
            // Validate that the selected Director exists
            // Validate that selected user has Director role (synced from DerivedUserRole)
            var directorExists = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == SelectedDirectorId!.Value
                && (u.Role == UserRole.Director || (u.RoleTemplate != null && u.RoleTemplate.DerivedUserRole == UserRole.Director)));
            if (!directorExists)
            {
                Error = _localizer["Error_SelectedDirectorNotFound"];
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

                successUserInfo = string.Format(_localizer["Success_DirectorAssigned"], director?.DisplayName, director?.Email);
            }
            else
            {
                // Create the manager user for this company with RoleTemplate
                var (hash, salt) = PasswordHasher.CreateHash(ManagerPassword);
                var managerTemplateKey = RoleTemplateMapper.MapUserRoleToRoleTemplateKey(UserRole.Manager);
                var managerTemplate = await _db.RoleTemplates.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(rt => rt.Key == managerTemplateKey);
                var manager = new AppUser
                {
                    CompanyId = company.Id,
                    Email = ManagerEmail,
                    DisplayName = ManagerDisplayName,
                    Role = managerTemplate?.DerivedUserRole ?? UserRole.Manager,
                    RoleTemplateId = managerTemplate?.Id,
                    IsActive = true,
                    PasswordHash = hash,
                    PasswordSalt = salt
                };

                _db.Users.Add(manager);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Created manager user {ManagerEmail} for company {CompanyName}",
                    ManagerEmail, company.Name);

                successUserInfo = string.Format(_localizer["Success_ManagerCreated"], ManagerEmail);
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

            TempData["SuccessMessage"] = string.Format(_localizer["Success_CompanyCreated"], company.Name, successUserInfo);

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating company {CompanyName}", CompanyName);
            Error = _localizer["Error_CompanyCreationFailed"];
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRenameCompanyAsync()
    {
        if (RenameCompanyId <= 0 || string.IsNullOrWhiteSpace(NewCompanyName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidCompanyIdOrName"].Value;
            return RedirectToPage();
        }

        // ✅ SECURITY FIX: Validate input for XSS attempts
        if (ContainsDangerousContent(NewCompanyName))
        {
            _logger.LogWarning("XSS attempt detected in company rename: {CompanyName}", NewCompanyName);
            TempData["ErrorMessage"] = _localizer["Error_DangerousContentDetected"].Value;
            return RedirectToPage();
        }

        // Validate field length
        if (NewCompanyName.Length > 200)
        {
            TempData["ErrorMessage"] = _localizer["Error_CompanyNameOrSlugTooLong"].Value;
            return RedirectToPage();
        }

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == RenameCompanyId);
        if (company == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_CompanyNotFound"].Value;
            return RedirectToPage();
        }

        company.Name = NewCompanyName;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Company {CompanyId} renamed to {NewName}", RenameCompanyId, NewCompanyName);
        TempData["SuccessMessage"] = string.Format(_localizer["Success_CompanyRenamed"], NewCompanyName);

        return RedirectToPage();
    }

    /// <summary>
    /// Detects potentially dangerous content like script tags, HTML tags, and JavaScript event handlers
    /// </summary>
    private static bool ContainsDangerousContent(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Check for script tags, HTML tags, and JavaScript event handlers
        var dangerousPatterns = new[]
        {
            @"<script[^>]*>",
            @"</script>",
            @"javascript:",
            @"on\w+\s*=",  // onclick, onerror, onload, etc.
            @"<iframe[^>]*>",
            @"<object[^>]*>",
            @"<embed[^>]*>",
            @"<img[^>]*>",
            @"<link[^>]*>",
            @"<style[^>]*>",
            @"eval\s*\(",
            @"expression\s*\(",
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Auto-generates slugs for legacy companies that were created without one.
    /// Generates from company Name: lowercase, non-alphanumeric → hyphens, deduped.
    /// </summary>
    private async Task BackfillMissingSlugsAsync()
    {
        var companiesWithoutSlug = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.Slug == null || c.Slug == "")
            .ToListAsync();

        if (!companiesWithoutSlug.Any()) return;

        // Get all existing slugs to ensure uniqueness
        var existingSlugs = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.Slug != null && c.Slug != "")
            .Select(c => c.Slug!)
            .ToListAsync();

        var usedSlugs = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);

        foreach (var company in companiesWithoutSlug)
        {
            var baseSlug = GenerateSlug(company.Name);
            var slug = baseSlug;
            var counter = 2;
            while (usedSlugs.Contains(slug))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
            }

            company.Slug = slug;
            usedSlugs.Add(slug);
            _logger.LogInformation("Auto-generated slug '{Slug}' for company '{CompanyName}' (ID: {CompanyId})",
                slug, company.Name, company.Id);
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Generates a URL-safe slug from a name: lowercase, non-alphanumeric chars → hyphens, trimmed.
    /// </summary>
    private static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "company";

        // Lowercase and replace non-alphanumeric with hyphens
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-");
        // Trim leading/trailing hyphens
        slug = slug.Trim('-');
        // Fallback if slug is empty (e.g., all non-latin chars)
        return string.IsNullOrEmpty(slug) ? "company" : slug;
    }

    public async Task<IActionResult> OnPostDeleteCompanyAsync(int id)
    {
        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_CompanyNotFound"].Value;
            return RedirectToPage();
        }

        // CRITICAL: Check if any Owner users belong to this company
        // IgnoreQueryFilters: admin may be in a different tenant than the company being deleted
        var hasOwnerUsers = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.CompanyId == id
            && (u.Role == UserRole.Owner || (u.RoleTemplate != null && u.RoleTemplate.DerivedUserRole == UserRole.Owner)));
        if (hasOwnerUsers)
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteCompanyWithOwners"].Value;
            _logger.LogWarning("Attempted to delete company {CompanyId} with Owner users still assigned", id);
            return RedirectToPage();
        }

        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // Delete all related data for this company using ExecuteDeleteAsync for better performance
            // This avoids loading all entities into memory before deletion

            // IgnoreQueryFilters on all: admin's tenant may differ from the company being deleted;
            // ExecuteDeleteAsync respects global query filters, so we must bypass them explicitly
            // 1. Delete all swap requests
            await _db.SwapRequests.IgnoreQueryFilters().Where(sr => sr.CompanyId == id).ExecuteDeleteAsync();

            // 2. Delete all time-off requests
            await _db.TimeOffRequests.IgnoreQueryFilters().Where(tor => tor.CompanyId == id).ExecuteDeleteAsync();

            // 3. Delete all shift assignments
            await _db.ShiftAssignments.IgnoreQueryFilters().Where(sa => sa.CompanyId == id).ExecuteDeleteAsync();

            // 4. Delete all shift instances
            await _db.ShiftInstances.IgnoreQueryFilters().Where(si => si.CompanyId == id).ExecuteDeleteAsync();

            // 5. Delete all shift types
            await _db.ShiftTypes.IgnoreQueryFilters().Where(st => st.CompanyId == id).ExecuteDeleteAsync();

            // 6. Delete all configs
            await _db.Configs.IgnoreQueryFilters().Where(c => c.CompanyId == id).ExecuteDeleteAsync();

            // 7. Delete all director assignments
            await _db.DirectorCompanies.Where(dc => dc.CompanyId == id).ExecuteDeleteAsync();

            // 8. Delete all user notifications
            await _db.UserNotifications.IgnoreQueryFilters().Where(n => n.CompanyId == id).ExecuteDeleteAsync();

            // 9. Delete all join requests
            await _db.UserJoinRequests.IgnoreQueryFilters().Where(jr => jr.CompanyId == id).ExecuteDeleteAsync();

            // 10. Deactivate all non-Owner users (soft-delete preserves audit trail integrity)
            await _db.Users.IgnoreQueryFilters().Where(u => u.CompanyId == id && u.Role != UserRole.Owner)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));

            // 11. Finally, delete the company itself
            _db.Companies.Remove(company);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Company {CompanyId} ({CompanyName}) deleted successfully", id, company.Name);
            TempData["SuccessMessage"] = string.Format(_localizer["Success_CompanyDeleted"], company.Name);

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error deleting company {CompanyId}", id);
            TempData["ErrorMessage"] = _localizer["Error_CompanyDeletionFailed"].Value;
            return RedirectToPage();
        }
    }
}
