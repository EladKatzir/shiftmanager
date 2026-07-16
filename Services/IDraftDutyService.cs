using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// A staged on-call cell for the private draft overlay: the desired (global) on-duty user set for a
/// (dutyTypeValue, date) coordinate. Mirrors <see cref="DraftOverlayCell"/> for shifts.
/// </summary>
public record DraftDutyOverlayCell(int DutyTypeValue, DateOnly WorkDate, List<int> UserIds);

/// <summary>
/// On-call Draft Mode staging + overlay surface (sub-project D), the analog of <see cref="IDraftModeService"/>
/// for shifts. A cell is (dutyTypeValue, WorkDate) → a set of user ids, and the baseline / staged / live sets
/// are ALWAYS computed over the GLOBAL active <see cref="OnDuty"/> set — on-call has no company/molecule/area
/// data boundary, and the session's AreaId is a UI hint only (see Spec D §3). The commit reconciler half of
/// on-call draft lives on the same class via <see cref="IDraftReconciler"/>.
/// </summary>
public interface IDraftDutyService
{
    /// <summary>Return the caller's Active on-call draft for this (area, week) UI scope, or null.</summary>
    Task<DraftSession?> GetActiveDraftAsync(int ownerUserId, int? areaId, DateOnly weekStart);

    /// <summary>Stage a user INTO the (dutyTypeValue, date) cell (captures the global baseline on first touch).</summary>
    Task<bool> StageDutyAssignAsync(int draftSessionId, int dutyTypeValue, DateOnly date, int userId);

    /// <summary>Stage a user OUT of the (dutyTypeValue, date) cell (captures the global baseline on first touch).</summary>
    Task<bool> StageDutyClearAsync(int draftSessionId, int dutyTypeValue, DateOnly date, int userId);

    /// <summary>The session's touched cells with their staged (desired) user sets, for the private page overlay.</summary>
    Task<List<DraftDutyOverlayCell>> GetDutyOverlayAsync(int draftSessionId);
}
