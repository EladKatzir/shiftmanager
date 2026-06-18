namespace ShiftManager.Models.Navigation;

/// <summary>
/// A single node in the domain-hub navigation tree (Model B redesign).
///
/// One source of truth: <see cref="NavRegistry"/> declares the whole tree; both the
/// renderer (<c>_NavSidebar.cshtml</c>) and the policy-parity test read from it.
///
/// VISIBILITY == ACCESS (design principle P2): <see cref="Policy"/> MUST be the SAME
/// authorization policy the destination page enforces (e.g. "Grant:ManagerHomeAccess").
/// The navigation service derives visibility by asking <c>IAuthorizationService</c> about
/// that policy — never a hand-rolled re-check — so a link can never be visible-but-403.
/// A null <see cref="Policy"/> means "any authenticated user" (the AuthorizeFolder("/") default).
/// </summary>
public sealed record NavNode(
    string LocKey,
    string? Route = null,
    string? Policy = null,
    string? Icon = null,
    string? Flag = null,
    string? ActiveMatch = null,
    IReadOnlyList<NavNode>? Children = null,
    NavRole MinRole = NavRole.Any)
{
    /// <summary>True when this node is a hub/group header that owns child nodes.</summary>
    public bool IsGroup => Children is { Count: > 0 };

    /// <summary>Path prefix used for active-state highlighting; defaults to <see cref="Route"/>.</summary>
    public string? ActivePrefix => ActiveMatch ?? Route;

    public IReadOnlyList<NavNode> ChildNodes => Children ?? System.Array.Empty<NavNode>();
}

/// <summary>
/// Coarse role tier, reserved for role-aware label/depth (P4 — e.g. the "My Schedule" ⇄
/// "Scheduling" slot). Visibility itself is always driven by <see cref="NavNode.Policy"/>,
/// not by this enum; this only tunes presentation.
/// </summary>
public enum NavRole
{
    Any,
    Manager,
    Director,
    Admin,
    Owner
}
