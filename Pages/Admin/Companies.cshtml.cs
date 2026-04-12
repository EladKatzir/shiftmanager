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
using ShiftManager.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace ShiftManager.Pages.Admin;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditCompany policy;
// company management inherently requires cross-company visibility
[Authorize(Policy = "Grant:EditCompany")]
public class CompaniesModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<CompaniesModel> _logger;
    private readonly ISetupTaskService _setupTaskService;

    private readonly ICompanyCacheService _companyCacheService;
    private readonly IRoleService _roleService;
    private readonly IConcurrencyService _concurrencyService;

    public CompaniesModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<CompaniesModel> logger,
        ISetupTaskService setupTaskService,
        ICompanyCacheService companyCacheService,
        IRoleService roleService,
        IConcurrencyService concurrencyService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _setupTaskService = setupTaskService;
        _companyCacheService = companyCacheService;
        _roleService = roleService;
        _concurrencyService = concurrencyService;
    }

    public record CompanyVM(int Id, string Name, string? NameHe, string? Slug, string? DisplayName, int UserCount, string? MoleculeName);
    public record DirectorVM(int Id, string DisplayName, string Email);
    public record MoleculeVM(int Id, string Name, string DisplayName);

    public List<CompanyVM> Companies { get; set; } = new();
    public List<DirectorVM> AvailableDirectors { get; set; } = new();
    public List<MoleculeVM> AvailableMolecules { get; set; } = new();

    [BindProperty] public string CompanyName { get; set; } = string.Empty;
    [BindProperty] public string? CompanyNameHe { get; set; }
    [BindProperty] public string CompanySlug { get; set; } = string.Empty;
    [BindProperty] public string CompanyDisplayName { get; set; } = string.Empty;
    [BindProperty] public int? SelectedMoleculeId { get; set; }

    [BindProperty] public int? SelectedDirectorId { get; set; }

    [BindProperty, EmailAddress] public string ManagerEmail { get; set; } = string.Empty;
    [BindProperty] public string ManagerDisplayName { get; set; } = string.Empty;
    [BindProperty] public string ManagerPassword { get; set; } = string.Empty;

    [BindProperty] public int RenameCompanyId { get; set; }
    [BindProperty] public string NewCompanyName { get; set; } = string.Empty;
    [BindProperty] public string? NewCompanyNameHe { get; set; }

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

        Companies = (await _db.Companies
            .IgnoreQueryFilters()
            .Include(c => c.Molecule)
            .Select(c => new CompanyVM(
                c.Id,
                c.Name,
                c.NameHe,
                c.Slug,
                c.DisplayName,
                userCountsByCompany.GetValueOrDefault(c.Id, 0),
                c.Molecule != null ? (c.Molecule.DisplayName ?? c.Molecule.Name) : null
            ))
            .ToListAsync())
            .OrderBy(c => Models.Company.ResolveLocalizedName(c.Name, c.DisplayName, c.NameHe),
                StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true))
            .ToList();

        // Load molecules for the molecule dropdown
        AvailableMolecules = await _db.Molecules
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayName ?? m.Name)
            .Select(m => new MoleculeVM(m.Id, m.Name, m.DisplayName))
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

        if (!string.IsNullOrWhiteSpace(CompanyNameHe) && ContainsDangerousContent(CompanyNameHe))
        {
            _logger.LogWarning("XSS attempt detected in company Hebrew name: {NameHe}", CompanyNameHe);
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

        if (!string.IsNullOrWhiteSpace(CompanyNameHe) && CompanyNameHe.Length > 200)
        {
            Error = _localizer["Error_CompanyNameOrSlugTooLong"];
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

        // Validate slug is unique (cross-tenant check via service)
        if (await _companyCacheService.IsSlugTakenAsync(CompanySlug))
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
                NameHe = string.IsNullOrWhiteSpace(CompanyNameHe) ? null : CompanyNameHe.Trim(),
                Slug = CompanySlug,
                DisplayName = string.IsNullOrWhiteSpace(CompanyDisplayName) ? CompanyName : CompanyDisplayName,
                MoleculeId = SelectedMoleculeId > 0 ? SelectedMoleculeId : null
            };

            _db.Companies.Add(company);
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "Company");
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return Page();
                }
            }

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
                {
                    var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                        () => _db.SaveChangesAsync(), "Company", company.Id);
                    if (!saveResult.Success)
                    {
                        Error = _localizer["Error_ConcurrencyConflict"];
                        return Page();
                    }
                }

                _logger.LogInformation("Assigned Director {DirectorId} ({DirectorEmail}) to company {CompanyName}",
                    SelectedDirectorId.Value, director?.Email, company.Name);

                successUserInfo = string.Format(_localizer["Success_DirectorAssigned"], director?.DisplayName, director?.Email);
            }
            else
            {
                // Create the manager user for this company with RoleTemplate
                var (hash, salt) = PasswordHasher.CreateHash(ManagerPassword);
                var managerTemplateKey = RoleTemplateMapper.MapUserRoleToRoleTemplateKey(UserRole.Manager);
                var managerTemplate = await _roleService.GetRoleTemplateByKeyAsync(managerTemplateKey);
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
                {
                    var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                        () => _db.SaveChangesAsync(), "Company", company.Id);
                    if (!saveResult.Success)
                    {
                        Error = _localizer["Error_ConcurrencyConflict"];
                        return Page();
                    }
                }

                _logger.LogInformation("Created manager user {ManagerEmail} for company {CompanyName}",
                    ManagerEmail, company.Name);

                successUserInfo = string.Format(_localizer["Success_ManagerCreated"], ManagerEmail);
            }

            // Shift types are now molecule-scoped — no per-company seeding needed.
            // Shifts are seeded when the molecule is created (see IShiftTypeSeedService).
            _logger.LogInformation("Company {CompanyName} created — shift types are molecule-scoped, no per-company seed", company.Name);

            // 4. Create default config for the new company
            var defaultConfigs = new[]
            {
                new AppConfig { CompanyId = company.Id, Key = "RestHours", Value = "8" },
                new AppConfig { CompanyId = company.Id, Key = "WeeklyHoursCap", Value = "40" }
            };

            _db.Configs.AddRange(defaultConfigs);
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "Company", company.Id);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return Page();
                }
            }

            _logger.LogInformation("Created default config for company {CompanyName}", company.Name);

            await transaction.CommitAsync();

            // Auto-generate setup tasks for the new company if it belongs to a molecule.
            // Duplicate guard: fetch tasks scoped to the molecule and check for this company's tasks.
            if (company.MoleculeId.HasValue)
            {
                var existingCompanyTasks = await _setupTaskService.GetTasksForMoleculeAsync(company.MoleculeId.Value);
                var hasCompanyTasks = existingCompanyTasks.Any(t => t.CompanyId == company.Id);
                if (!hasCompanyTasks)
                {
                    if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
                    {
                        _logger.LogWarning("Invalid or missing NameIdentifier claim");
                        return RedirectToPage("/Auth/Login");
                    }
                    await _setupTaskService.GenerateTasksForCompanyAsync(company.Id, currentUserId);
                    _logger.LogInformation("Auto-generated setup tasks for new company {CompanyId}", company.Id);
                }
            }

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

        if (!string.IsNullOrWhiteSpace(NewCompanyNameHe) && ContainsDangerousContent(NewCompanyNameHe))
        {
            _logger.LogWarning("XSS attempt detected in company rename (Hebrew): {NameHe}", NewCompanyNameHe);
            TempData["ErrorMessage"] = _localizer["Error_DangerousContentDetected"].Value;
            return RedirectToPage();
        }

        // Validate field length
        if (NewCompanyName.Length > 200)
        {
            TempData["ErrorMessage"] = _localizer["Error_CompanyNameOrSlugTooLong"].Value;
            return RedirectToPage();
        }

        if (!string.IsNullOrWhiteSpace(NewCompanyNameHe) && NewCompanyNameHe.Length > 200)
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
        company.DisplayName = NewCompanyName;
        // Explicit Hebrew-field semantics: empty input clears the override (falls back to Name in Hebrew UI).
        // Never silently preserve a stale NameHe — the admin submitted a rename, they own this decision.
        company.NameHe = string.IsNullOrWhiteSpace(NewCompanyNameHe) ? null : NewCompanyNameHe.Trim();
        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "Company", RenameCompanyId);
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }
        }

        _companyCacheService.InvalidateCache(RenameCompanyId);

        _logger.LogInformation("Company {CompanyId} renamed to {NewName}", RenameCompanyId, NewCompanyName);
        TempData["SuccessMessage"] = string.Format(_localizer["Success_CompanyRenamed"], NewCompanyName);

        return RedirectToPage();
    }

    /// <summary>
    /// Detects potentially dangerous content like script tags, HTML tags, and JavaScript event handlers
    /// </summary>
    private static bool ContainsDangerousContent(string? input)
        => InputSanitizer.ContainsDangerousContent(input);

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

        await _concurrencyService.SaveWithConcurrencyHandlingAsync(
            () => _db.SaveChangesAsync(), "Company");
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

            // 5. Delete company-scoped shift types only (molecule-scoped shifts are shared)
            await _db.ShiftTypes.Where(st => st.CompanyId == id).ExecuteDeleteAsync();

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

            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "Company", id);
                if (!saveResult.Success)
                {
                    Error = _localizer["Error_ConcurrencyConflict"];
                    return RedirectToPage();
                }
            }
            await transaction.CommitAsync();

            _companyCacheService.InvalidateCache(id);

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
