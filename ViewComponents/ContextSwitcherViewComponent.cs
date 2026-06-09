using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

public class ContextSwitcherViewComponent : ViewComponent
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly AppDbContext _context;
    private readonly ITenantResolver _tenantResolver;
    private readonly IGrantService _grantService;
    private readonly ICompanyMembershipService _companyMembershipService;
    private readonly ILogger<ContextSwitcherViewComponent> _logger;

    // Threshold for suggesting virtualization (many contexts)
    private const int MANY_CONTEXTS_THRESHOLD = 50;
    // Max length for display name before truncation
    private const int MAX_NAME_DISPLAY_LENGTH = 30;

    public ContextSwitcherViewComponent(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext context,
        ITenantResolver tenantResolver,
        IGrantService grantService,
        ICompanyMembershipService companyMembershipService,
        ILogger<ContextSwitcherViewComponent> logger)
    {
        _localizer = localizer;
        _context = context;
        _tenantResolver = tenantResolver;
        _grantService = grantService;
        _companyMembershipService = companyMembershipService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var user = HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            return Content(string.Empty);
        }

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Content(string.Empty);
        }

        // Initialize model with error state handling
        var model = new ContextSwitcherViewModel();

        try
        {
            // Get all contexts (companies/molecules) the user has access to
            var contexts = new List<ContextOption>();

            // Context switching is based on organizational membership (not functional grants)
            // Owner = system-wide access, Director/AreaAdmin = assigned companies, Regular = own company
            // This is intentionally role-based as it reflects organizational structure, not permissions
            var isOwner = user.IsInRole("Owner");
            var isDirector = user.IsInRole("Director") || user.IsInRole("AreaAdmin");

            if (isOwner)
            {
                // Owner sees all molecules and their companies
                // Molecule uses IsActive property
                var molecules = await _context.Molecules
                    .Where(m => m.IsActive)
                    .Include(m => m.Companies)
                    .OrderBy(m => m.Name)
                    .ToListAsync();

                foreach (var molecule in molecules)
                {
                    var moleculeGroup = new ContextGroup
                    {
                        GroupName = molecule.Name,
                        GroupType = "molecule",
                        Options = molecule.Companies
                            .OrderBy(c => c.LocalizedName)
                            .Select(c => CreateContextOption(c.Id, c.LocalizedName, "company", molecule.Name, molecule.Id))
                            .ToList()
                    };
                    contexts.AddRange(moleculeGroup.Options);
                }
            }
            else if (isDirector)
            {
                // Directors switch between molecules (not companies) — shifts are molecule-scoped
                model.IsMoleculeMode = true;

                var moleculeIds = await _grantService
                    .GetAccessibleMoleculeIdsForGrantAsync(userId, "DirectorHubAccess");

                // SECURITY-AUDITED: SAFE — scoped by grant resolution; IgnoreQueryFilters needed
                // because directors manage molecules outside their own tenant (HQ company)
                var accessibleMolecules = await _context.Molecules
                    .IgnoreQueryFilters()
                    .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
                    .Include(m => m.Area)
                    .OrderBy(m => m.Name)
                    .ToListAsync();

                foreach (var mol in accessibleMolecules)
                {
                    contexts.Add(CreateContextOption(
                        mol.Id, mol.Name, "molecule",
                        mol.Area?.Name ?? "", mol.Id));
                }
            }
            else
            {
                // Regular users: check multi-company membership first.
                // If the user belongs to more than one company, list all membership companies
                // and enable member-mode switching. Single-membership users keep the existing
                // single-context non-interactive display (no behaviour change for ~99% of users).
                var memberships = await _companyMembershipService.GetMembershipsAsync(userId);

                if (memberships.Count > 1)
                {
                    // Multi-company member — build one option per membership company.
                    model.IsMemberMode = true;

                    var memberCompanyIds = memberships.Select(m => m.CompanyId).ToList();
                    // SECURITY-AUDITED: SAFE — scoped by membership rows for this specific user;
                    // IgnoreQueryFilters needed because member companies may be in different tenants.
                    var memberCompanies = await _context.Companies
                        .IgnoreQueryFilters()
                        .Where(c => memberCompanyIds.Contains(c.Id))
                        .Include(c => c.Molecule)
                        .ToListAsync();

                    // Preserve primary-first ordering from GetMembershipsAsync
                    foreach (var membership in memberships)
                    {
                        var company = memberCompanies.FirstOrDefault(c => c.Id == membership.CompanyId);
                        if (company != null)
                        {
                            contexts.Add(CreateContextOption(
                                company.Id,
                                company.LocalizedName,
                                "company",
                                company.Molecule?.Name ?? "",
                                company.MoleculeId));
                        }
                    }
                }
                else
                {
                    // Single membership (or zero — handled below): use home company from AppUser.
                    // AppUser.CompanyId is int (non-nullable).
                    var userCompanyId = await _context.Users
                        .Where(u => u.Id == userId)
                        .Select(u => u.CompanyId)
                        .FirstOrDefaultAsync();

                    if (userCompanyId > 0)
                    {
                        var company = await _context.Companies
                            .Include(c => c.Molecule)
                            .FirstOrDefaultAsync(c => c.Id == userCompanyId);

                        if (company != null)
                        {
                            contexts.Add(CreateContextOption(
                                company.Id,
                                company.LocalizedName,
                                "company",
                                company.Molecule?.Name ?? "",
                                company.MoleculeId));
                        }
                    }
                }
            }

            // Edge Case: No contexts available
            if (contexts.Count == 0)
            {
                model.ShowSwitcher = false;
                model.HasNoContexts = true;
                return View(model);
            }

            // Edge Case: Single context - show non-interactive display
            if (contexts.Count == 1)
            {
                model.ShowSwitcher = false;
                model.HasSingleContext = true;
                model.CurrentContext = contexts.First();
                return View(model);
            }

            // Edge Case: Many contexts (50+) - hint for performance
            model.HasManyContexts = contexts.Count >= MANY_CONTEXTS_THRESHOLD;
            model.TotalContextCount = contexts.Count;

            // Resolve current context — directors use molecule ID, owners/members use company ID
            ContextOption? currentContext;
            if (model.IsMoleculeMode)
            {
                // For directors: match against molecule ID from cookie or MoleculeId claim
                var currentMoleculeId = _tenantResolver.GetDirectorSelectedMoleculeId() ?? 0;
                currentContext = contexts.FirstOrDefault(c => c.Id == currentMoleculeId)
                    ?? contexts.FirstOrDefault();

                if (currentMoleculeId > 0 && !contexts.Any(c => c.Id == currentMoleculeId))
                {
                    model.CurrentContextUnavailable = true;
                    currentContext = contexts.FirstOrDefault();
                }
            }
            else
            {
                // For owners and multi-company members: match against company ID from tenant resolver.
                // TenantResolver already honors the member_selected_company cookie (Task 3), so
                // GetCurrentTenantId() returns the member's active company in both cases.
                var currentCompanyId = _tenantResolver.GetCurrentTenantId();
                currentContext = contexts.FirstOrDefault(c => c.Id == currentCompanyId)
                    ?? contexts.FirstOrDefault();

                if (currentCompanyId > 0 && !contexts.Any(c => c.Id == currentCompanyId))
                {
                    model.CurrentContextUnavailable = true;
                    currentContext = contexts.FirstOrDefault();
                }
            }

            // Group contexts by molecule
            var groups = contexts
                .GroupBy(c => c.ParentName)
                .Select(g => new ContextGroup
                {
                    GroupName = g.Key,
                    GroupType = "molecule",
                    Options = g.ToList()
                })
                .OrderBy(g => g.GroupName)
                .ToList();

            model.CurrentContext = currentContext;
            model.ContextGroups = groups;
            model.ShowSwitcher = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading context switcher data for user {UserId}", userId);
            model.HasError = true;
            model.ErrorMessage = _localizer["ContextSwitcher_LoadError"];
        }

        return View(model);
    }

    /// <summary>
    /// Creates a ContextOption with truncation handling for long names
    /// </summary>
    private ContextOption CreateContextOption(int id, string name, string type, string parentName, int? moleculeId)
    {
        return new ContextOption
        {
            Id = id,
            Name = name,
            DisplayName = TruncateName(name, MAX_NAME_DISPLAY_LENGTH),
            FullName = name,
            Type = type,
            ParentName = parentName,
            MoleculeId = moleculeId,
            NeedsTruncation = name.Length > MAX_NAME_DISPLAY_LENGTH
        };
    }

    /// <summary>
    /// Truncates a name with ellipsis if it exceeds the max length
    /// </summary>
    private static string TruncateName(string name, int maxLength)
    {
        if (string.IsNullOrEmpty(name) || name.Length <= maxLength)
            return name;

        return name.Substring(0, maxLength - 3) + "...";
    }
}

public class ContextSwitcherViewModel
{
    public ContextOption? CurrentContext { get; set; }
    public List<ContextGroup> ContextGroups { get; set; } = new();
    public bool ShowSwitcher { get; set; }

    // Edge case handling properties
    /// <summary>User has only one context available - show non-interactive display</summary>
    public bool HasSingleContext { get; set; }
    /// <summary>User has no contexts available (shouldn't happen, but handle gracefully)</summary>
    public bool HasNoContexts { get; set; }
    /// <summary>User has many contexts (50+) - search is especially important</summary>
    public bool HasManyContexts { get; set; }
    /// <summary>Total number of contexts available</summary>
    public int TotalContextCount { get; set; }
    /// <summary>Current context was deleted or became unavailable</summary>
    public bool CurrentContextUnavailable { get; set; }
    /// <summary>An error occurred loading context data</summary>
    public bool HasError { get; set; }
    /// <summary>Localized error message to display</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>True when the switcher operates at molecule level (directors) vs company level (owners)</summary>
    public bool IsMoleculeMode { get; set; }
    /// <summary>True when the switcher lists a multi-company member's membership companies.
    /// Mutually exclusive with IsMoleculeMode. Single-membership regular users get neither flag.</summary>
    public bool IsMemberMode { get; set; }
}

public class ContextGroup
{
    public string GroupName { get; set; } = "";
    public string GroupType { get; set; } = "";
    public List<ContextOption> Options { get; set; } = new();
}

public class ContextOption
{
    public int Id { get; set; }
    /// <summary>Original full name of the context</summary>
    public string Name { get; set; } = "";
    /// <summary>Truncated name for display (if needed)</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Full name for tooltip</summary>
    public string FullName { get; set; } = "";
    public string Type { get; set; } = "";
    public string ParentName { get; set; } = "";
    public int? MoleculeId { get; set; }
    /// <summary>True if name was truncated and needs tooltip</summary>
    public bool NeedsTruncation { get; set; }
}
