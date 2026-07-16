using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// The shared, surface-agnostic Draft Mode orchestrator (Spec F §4). Owns entering (idempotent, one Active
/// session per scope), discarding, and the hardened commit engine (§5) — per-cell drift detection + per-cell
/// re-authorization inside one transaction, per-cell skip (never an all-or-nothing abort), and per-surface
/// SignalR after commit. Everything surface-specific is delegated to the matching <see cref="IDraftReconciler"/>.
/// </summary>
public interface IDraftLifecycle
{
    /// <summary>Return the caller's Active session for this scope, or null.</summary>
    Task<DraftSession?> GetActiveAsync(int ownerUserId, DraftScope scope);

    /// <summary>Enter a draft for this scope — idempotent (returns the existing Active session if one exists).</summary>
    Task<DraftSession> EnterAsync(int ownerUserId, DraftScope scope);

    /// <summary>Throw the sandbox away (cells cascade). Owner-checked by the caller.</summary>
    Task DiscardAsync(int draftSessionId, int ownerUserId);

    /// <summary>Reconcile the session's touched cells into live with per-cell drift/skip + re-auth, then notify.</summary>
    Task<DraftCommitResult> CommitAsync(int draftSessionId, int actingUserId);
}
