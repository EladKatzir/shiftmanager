using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>One touched chore cell's staged descriptor set, for the render overlay (sub-project C).</summary>
public record DraftChoreOverlayCell(int UserId, DateOnly WorkDate, List<DraftChoreDescriptor> Descriptors);

/// <summary>
/// The chores-surface staging + overlay API (analog of <see cref="IDraftModeService"/> for shifts). The
/// commit reconciliation lives in the <see cref="IDraftReconciler"/> half of the same implementation, driven
/// by the shared <see cref="IDraftLifecycle"/>. Staging never touches the live board or fires SignalR.
/// </summary>
public interface IDraftChoreService
{
    /// <summary>The caller's Active chores draft for (molecule, week), or null.</summary>
    Task<DraftSession?> GetActiveChoreDraftAsync(int ownerUserId, int moleculeId, DateOnly weekStart);

    /// <summary>Stage a chore descriptor into the (user, date) cell (adds to the desired set; set-deduplicated).</summary>
    Task<bool> StageChoreAsync(int draftSessionId, int userId, DateOnly date, DraftChoreDescriptor descriptor);

    /// <summary>Remove one staged descriptor from the (user, date) cell, keyed by its canonical <c>Encode()</c> string.</summary>
    Task<bool> ClearChoreAsync(int draftSessionId, int userId, DateOnly date, string descriptorKey);

    /// <summary>All touched cells for the session as staged descriptor lists (for the render overlay).</summary>
    Task<List<DraftChoreOverlayCell>> GetChoreOverlayAsync(int draftSessionId);
}
