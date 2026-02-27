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
        ILogger<ContextSwitcherViewComponent> logger)
    {
        _localizer = localizer;
        _context = context;
        _tenantResolver = tenantResolver;
        _grantService = grantService;
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
                            .OrderBy(c => c.Name)
                            .Select(c => CreateContextOption(c.Id, c.Name, "company", molecule.Name, molecule.Id))
                            .ToList()
                    };
                    contexts.AddRange(moleculeGroup.Options);
                }
            }
            else if (isDirector)
            {
                // Resolve director's accessible companies via grant scope (DirectorHubAccess grant
                // scoped to MoleculeId resolves to all companies in that molecule)
                var directorCompanyIds = await _grantService
                    .GetAccessibleCompanyIdsForGrantAsync(userId, "DirectorHubAccess");

                // Get the companies with their molecules, excluding HQ placeholders
                var companies = await _context.Companies
                    .IgnoreQueryFilters()
                    .Where(c => directorCompanyIds.Contains(c.Id) && !c.IsHeadquarters)
                    .Include(c => c.Molecule)
                    .OrderBy(c => c.Name)
                    .ToListAsync();

                foreach (var company in companies)
                {
                    contexts.Add(CreateContextOption(
                        company.Id,
                        company.Name,
                        "company",
                        company.Molecule?.Name ?? "",
                        company.MoleculeId));
                }
            }
            else
            {
                // Regular users see their assigned company plus any granted contexts
                // AppUser.CompanyId is int (non-nullable)
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
                            company.Name,
                            "company",
                            company.Molecule?.Name ?? "",
                            company.MoleculeId));
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

            // Get current context from tenant resolver
            var currentCompanyId = _tenantResolver.GetCurrentTenantId();
            var currentContext = contexts.FirstOrDefault(c => c.Id == currentCompanyId)
                ?? contexts.FirstOrDefault();

            // Edge Case: Current context was deleted or became unavailable
            if (currentCompanyId > 0 && !contexts.Any(c => c.Id == currentCompanyId))
            {
                model.CurrentContextUnavailable = true;
                // Fall back to first available context
                currentContext = contexts.FirstOrDefault();
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
