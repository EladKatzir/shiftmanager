using System.Security.Claims;
using ShiftManager.Models.Navigation;

namespace ShiftManager.Services.Navigation;

/// <summary>
/// Builds the per-user domain-hub navigation by deriving each node's visibility from its
/// destination page's authorization policy (P2 — visibility == access). Also gates the
/// whole new-nav experience behind FF_NEW_NAV.
/// </summary>
public interface INavigationService
{
    /// <summary>True when the domain-hub navigation should render for the current user/company.</summary>
    bool IsNewNavEnabled();

    /// <summary>The navigation tree filtered to what this user may see (groups with no visible child are dropped).</summary>
    Task<IReadOnlyList<NavNode>> GetVisibleNavAsync(ClaimsPrincipal user);

    /// <summary>Flat list of every visible, route-bearing node — for the Ctrl-K command palette.</summary>
    Task<IReadOnlyList<NavNode>> GetVisibleLeavesAsync(ClaimsPrincipal user);
}
