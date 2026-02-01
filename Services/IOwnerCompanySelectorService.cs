namespace ShiftManager.Services;

/// <summary>
/// Service for Owner role to select which company to manage.
/// Owner can manage ALL companies in the system.
/// Uses cookie-based selection with fallback to home company.
/// </summary>
public interface IOwnerCompanySelectorService
{
    /// <summary>
    /// Check if current user has Owner role.
    /// </summary>
    /// <returns>True if current user is Owner, false otherwise</returns>
    bool IsOwner();

    /// <summary>
    /// Get the company ID that Owner has currently selected via cookie.
    /// Returns null if no selection or user is not Owner.
    /// </summary>
    /// <returns>Selected company ID or null</returns>
    int? GetSelectedCompanyId();

    /// <summary>
    /// Set Owner's active company selection.
    /// Only works if current user is Owner.
    /// Validates that company exists before setting cookie.
    /// </summary>
    /// <param name="companyId">Company ID to select</param>
    /// <returns>True if selection succeeded, false if not Owner or company doesn't exist</returns>
    Task<bool> SelectCompanyAsync(int companyId);

    /// <summary>
    /// Clear Owner's company selection (return to home company).
    /// Deletes the selection cookie.
    /// </summary>
    Task ClearSelectionAsync();

    /// <summary>
    /// Get name of currently selected company for display purposes.
    /// </summary>
    /// <returns>Company name or null if no selection</returns>
    Task<string?> GetSelectedCompanyNameAsync();

    /// <summary>
    /// Get Owner's home company ID from their CompanyId claim.
    /// This is the Owner's original company assignment.
    /// </summary>
    /// <returns>Home company ID or null if not found</returns>
    int? GetHomeCompanyId();
}
