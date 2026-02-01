namespace ShiftManager.Services;

/// <summary>
/// Service for resolving scope-based filtering of calendar data.
/// Translates scope selections (mine/company/molecule/area) into appropriate company ID filters.
/// </summary>
public interface IScopeFilterService
{
    /// <summary>
    /// Gets the current scope from URL query string or cookie preference.
    /// Returns the scope type and optional scope ID.
    /// </summary>
    /// <param name="calendarType">The calendar type context (e.g., "shifts", "chores")</param>
    /// <returns>Tuple of (scopeType, scopeId) where scopeId may be null for "mine" scope</returns>
    (string scopeType, int? scopeId) GetCurrentScope(string calendarType = "shifts");

    /// <summary>
    /// Resolves the scope to a list of company IDs that should be included in the data query.
    /// For "mine" scope: returns empty list (filtering by user happens separately)
    /// For "company" scope: returns the user's company
    /// For "molecule" scope: returns all companies in the molecule
    /// For "area" scope: returns all companies in all molecules in the area
    /// </summary>
    /// <param name="scopeType">The scope type (mine, company, molecule, area)</param>
    /// <param name="scopeId">Optional scope ID for molecule/area scopes</param>
    /// <returns>List of company IDs to filter by, or empty list for user-only filtering</returns>
    Task<List<int>> ResolveCompanyIdsForScopeAsync(string scopeType, int? scopeId = null);

    /// <summary>
    /// Validates that the current user has access to the requested scope.
    /// </summary>
    /// <param name="scopeType">The scope type to validate</param>
    /// <param name="scopeId">The scope ID to validate</param>
    /// <param name="calendarType">The calendar type for grant checking</param>
    /// <returns>True if user has access, false otherwise</returns>
    Task<bool> ValidateScopeAccessAsync(string scopeType, int? scopeId, string calendarType = "shifts");

    /// <summary>
    /// Returns the default scope for the current user.
    /// </summary>
    Task<(string scopeType, int? scopeId)> GetDefaultScopeAsync();

    /// <summary>
    /// Determines if the "mine only" filter should be applied based on scope.
    /// Returns true for "mine" scope, otherwise returns the user's preference.
    /// </summary>
    /// <param name="scopeType">The current scope type</param>
    bool ShouldFilterToCurrentUserOnly(string scopeType);
}
