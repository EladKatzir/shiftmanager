using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Services;

/// <summary>
/// Service for resolving scope-based filtering of calendar data.
/// </summary>
public class ScopeFilterService : IScopeFilterService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IHierarchyService _hierarchyService;
    private readonly IUserPreferenceService _userPreferenceService;
    private readonly ILogger<ScopeFilterService> _logger;

    public ScopeFilterService(
        IHttpContextAccessor httpContextAccessor,
        AppDbContext db,
        IGrantService grantService,
        IHierarchyService hierarchyService,
        IUserPreferenceService userPreferenceService,
        ILogger<ScopeFilterService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _grantService = grantService;
        _hierarchyService = hierarchyService;
        _userPreferenceService = userPreferenceService;
        _logger = logger;
    }

    private ClaimsPrincipal? CurrentUser => _httpContextAccessor.HttpContext?.User;
    private HttpContext? HttpContext => _httpContextAccessor.HttpContext;

    private int? CurrentUserId
    {
        get
        {
            var userIdClaim = CurrentUser?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userIdClaim != null && int.TryParse(userIdClaim, out var id) ? id : null;
        }
    }

    /// <inheritdoc />
    public (string scopeType, int? scopeId) GetCurrentScope(string calendarType = "shifts")
    {
        var httpContext = HttpContext;
        if (httpContext == null)
            return ("company", null);

        // 1. Check URL query string first (highest priority)
        if (httpContext.Request.Query.TryGetValue("scope", out var queryScope) && !string.IsNullOrEmpty(queryScope))
        {
            var scopeType = queryScope.ToString().ToLowerInvariant();
            
            // Check for scope ID in query string
            int? scopeId = null;
            if (httpContext.Request.Query.TryGetValue("scopeId", out var queryScopeId) 
                && int.TryParse(queryScopeId.ToString(), out var parsedId))
            {
                scopeId = parsedId;
            }
            
            return (scopeType, scopeId);
        }

        // 2. Check cookie for saved preference
        var cookieKey = $"ShiftyScope_{calendarType}";
        if (httpContext.Request.Cookies.TryGetValue(cookieKey, out var cookieScope) && !string.IsNullOrEmpty(cookieScope))
        {
            return (cookieScope.ToLowerInvariant(), null);
        }

        // 3. Default to company view
        return ("company", null);
    }

    /// <inheritdoc />
    public async Task<List<int>> ResolveCompanyIdsForScopeAsync(string scopeType, int? scopeId = null)
    {
        var userId = CurrentUserId;
        if (userId == null)
        {
            _logger.LogWarning("ScopeFilterService: No current user ID");
            return new List<int>();
        }

        var user = await _db.Users.FindAsync(userId.Value);
        if (user == null)
        {
            _logger.LogWarning("ScopeFilterService: User {UserId} not found", userId);
            return new List<int>();
        }

        switch (scopeType.ToLowerInvariant())
        {
            case "mine":
                // Return user's company ID - the IsCurrentUser filter handles the user-specific filtering
                return user.CompanyId > 0 ? new List<int> { user.CompanyId } : new List<int>();

            case "company":
                // Return user's company
                return user.CompanyId > 0 ? new List<int> { user.CompanyId } : new List<int>();

            case "department":
                // For tech users - return no companies (they filter by department)
                // This is a placeholder - actual implementation depends on how tech calendars work
                return new List<int>();

            case "molecule":
                return await GetCompanyIdsForMoleculeAsync(userId.Value, scopeId);

            case "area":
                return await GetCompanyIdsForAreaAsync(userId.Value, scopeId);

            default:
                _logger.LogWarning("ScopeFilterService: Unknown scope type '{ScopeType}'", scopeType);
                return user.CompanyId > 0 ? new List<int> { user.CompanyId } : new List<int>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ValidateScopeAccessAsync(string scopeType, int? scopeId, string calendarType = "shifts")
    {
        var userId = CurrentUserId;
        if (userId == null)
            return false;

        // Organizational identity checks — determines data scope, not authorization
        var isOwner = CurrentUser?.IsInRole("Owner") ?? false;
        var isDirector = (CurrentUser?.IsInRole("Director") ?? false) || (CurrentUser?.IsInRole("AreaAdmin") ?? false);

        switch (scopeType.ToLowerInvariant())
        {
            case "mine":
            case "company":
                // Everyone has access to these scopes
                return true;

            case "molecule":
                if (isOwner || isDirector)
                    return true;
                    
                var moleculeGrantKey = $"View{ToTitleCase(calendarType)}Molecule";
                return await _grantService.HasGrantAsync(userId.Value, moleculeGrantKey);

            case "area":
                if (isOwner)
                    return true;
                    
                var areaGrantKey = $"View{ToTitleCase(calendarType)}Area";
                return await _grantService.HasGrantAsync(userId.Value, areaGrantKey);

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public async Task<(string scopeType, int? scopeId)> GetDefaultScopeAsync()
    {
        var userId = CurrentUserId;
        if (userId == null)
            return ("company", null);

        var user = await _db.Users.FindAsync(userId.Value);
        if (user == null)
            return ("company", null);

        // Default is company scope for workforce users
        if (user.CompanyId > 0)
            return ("company", user.CompanyId);

        // For tech users with department
        if (user.DepartmentId.HasValue)
            return ("department", user.DepartmentId);

        return ("mine", userId);
    }

    /// <inheritdoc />
    public bool ShouldFilterToCurrentUserOnly(string scopeType)
    {
        // For "mine" scope, always filter to current user
        if (scopeType.Equals("mine", StringComparison.OrdinalIgnoreCase))
            return true;

        // For other scopes, use user's preference
        return _userPreferenceService.GetShowMyItemsOnly();
    }

    /// <summary>
    /// Gets all company IDs within the user's molecule.
    /// </summary>
    private async Task<List<int>> GetCompanyIdsForMoleculeAsync(int userId, int? moleculeId)
    {
        // Get user's hierarchy context to find their molecule
        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
        
        int targetMoleculeId;
        if (moleculeId.HasValue)
        {
            targetMoleculeId = moleculeId.Value;
        }
        else if (userContext?.Path?.Molecule != null)
        {
            targetMoleculeId = userContext.Path.Molecule.Id;
        }
        else
        {
            _logger.LogWarning("ScopeFilterService: Cannot determine molecule for user {UserId}", userId);
            // Fall back to user's company
            var user = await _db.Users.FindAsync(userId);
            return user?.CompanyId > 0 ? new List<int> { user.CompanyId } : new List<int>();
        }

        // Get all companies in the molecule
        var companyIds = await _db.Companies
            .Where(c => c.MoleculeId.HasValue && c.MoleculeId.Value == targetMoleculeId)
            .Select(c => c.Id)
            .ToListAsync();

        _logger.LogDebug("ScopeFilterService: Molecule {MoleculeId} contains companies: [{CompanyIds}]", 
            targetMoleculeId, string.Join(", ", companyIds));

        return companyIds;
    }

    /// <summary>
    /// Gets all company IDs within the user's area (all molecules in the area).
    /// </summary>
    private async Task<List<int>> GetCompanyIdsForAreaAsync(int userId, int? areaId)
    {
        // Get user's hierarchy context to find their area
        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
        
        int targetAreaId;
        if (areaId.HasValue)
        {
            targetAreaId = areaId.Value;
        }
        else if (userContext?.Path?.Area != null)
        {
            targetAreaId = userContext.Path.Area.Id;
        }
        else
        {
            _logger.LogWarning("ScopeFilterService: Cannot determine area for user {UserId}", userId);
            // Fall back to molecule scope
            return await GetCompanyIdsForMoleculeAsync(userId, null);
        }

        // Get all molecules in the area, then all companies in those molecules
        var moleculeIds = await _db.Molecules
            .Where(m => m.AreaId == targetAreaId && m.IsActive)
            .Select(m => m.Id)
            .ToListAsync();

        var companyIds = await _db.Companies
            .Where(c => c.MoleculeId.HasValue && moleculeIds.Contains(c.MoleculeId.Value))
            .Select(c => c.Id)
            .ToListAsync();

        _logger.LogDebug("ScopeFilterService: Area {AreaId} contains companies: [{CompanyIds}]", 
            targetAreaId, string.Join(", ", companyIds));

        return companyIds;
    }

    private static string ToTitleCase(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        return char.ToUpper(str[0]) + str.Substring(1).ToLower();
    }
}
