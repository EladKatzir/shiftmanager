using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ShiftManager.Models.Navigation;

namespace ShiftManager.Services.Navigation;

/// <inheritdoc />
public class NavigationService : INavigationService
{
    private readonly IAuthorizationService _authz;
    private readonly IFeatureFlagService _flags;
    private readonly ICurrentUserService _currentUser;

    public NavigationService(
        IAuthorizationService authz,
        IFeatureFlagService flags,
        ICurrentUserService currentUser)
    {
        _authz = authz;
        _flags = flags;
        _currentUser = currentUser;
    }

    private int? UserIdOrNull => _currentUser.IsAuthenticated ? _currentUser.UserId : null;

    public async Task<IReadOnlyList<NavNode>> GetVisibleNavAsync(ClaimsPrincipal user)
    {
        var result = new List<NavNode>();
        foreach (var node in NavRegistry.Root)
        {
            var filtered = await FilterAsync(user, node);
            if (filtered is not null) result.Add(filtered);
        }
        return result;
    }

    public async Task<IReadOnlyList<NavNode>> GetVisibleLeavesAsync(ClaimsPrincipal user)
    {
        var tree = await GetVisibleNavAsync(user);
        var leaves = new List<NavNode>();
        void Walk(IEnumerable<NavNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.Route is not null) leaves.Add(n);
                Walk(n.ChildNodes);
            }
        }
        Walk(tree);
        return leaves;
    }

    /// <summary>Returns a copy of <paramref name="node"/> with only visible children, or null if it should be hidden.</summary>
    private async Task<NavNode?> FilterAsync(ClaimsPrincipal user, NavNode node)
    {
        // Feature-flag gate (e.g. Friends, Duty Rotations) — independent of authorization.
        if (node.Flag is not null && !_flags.IsEnabled(node.Flag, UserIdOrNull, _currentUser.CompanyId))
            return null;

        if (node.IsGroup)
        {
            var visibleChildren = new List<NavNode>();
            foreach (var child in node.ChildNodes)
            {
                var fc = await FilterAsync(user, child);
                if (fc is not null) visibleChildren.Add(fc);
            }

            if (visibleChildren.Count > 0)
                return node with { Children = visibleChildren };

            // A group with its own route (clickable hub) survives if that route is authorized.
            if (node.Route is not null && await AuthorizedAsync(user, node))
                return node with { Children = System.Array.Empty<NavNode>() };

            return null; // empty hub — no visible children, not clickable → hidden (no empty shells)
        }

        // Leaf: visible iff authorized for its destination page's policy.
        return await AuthorizedAsync(user, node) ? node : null;
    }

    /// <summary>
    /// The core of P2: visibility is the page's OWN policy result, not a re-implemented check.
    /// Null policy == "any authenticated user" (the AuthorizeFolder("/") default).
    /// </summary>
    private async Task<bool> AuthorizedAsync(ClaimsPrincipal user, NavNode node)
    {
        if (node.Policy is null)
            return user.Identity?.IsAuthenticated == true;

        var result = await _authz.AuthorizeAsync(user, node.Policy);
        return result.Succeeded;
    }
}
