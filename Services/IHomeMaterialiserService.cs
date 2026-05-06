namespace ShiftManager.Services;

public interface IHomeMaterialiserService
{
    /// <summary>
    /// Idempotent diff-and-sync: ensures ShiftAssignment rows for a TimeOffRequest
    /// match the request's current state.
    /// - Status = Approved: desired set is computed from Type+dates (After: HOME_PM day-N + HOME_AM day-N+1; Vacation: HOME each day Start..End + HOME_AM End+1).
    /// - Status = Declined/Canceled/Pending/PendingSecondApproval: desired set is empty (rows deleted).
    /// Existing rows tagged with this SourceTimeOffRequestId are diffed: missing inserted, surplus deleted.
    /// </summary>
    Task SyncMaterialisedHomeRowsAsync(int timeOffRequestId);

    /// <summary>
    /// After a vacation cancellation, restore rotation HOME rows for the user across the
    /// formerly-covered date range using the deterministic DerivedRotationRule.
    /// No-op if the user has no HomeTypeId.
    /// </summary>
    Task RestoreRotationHomeAsync(int userId, DateOnly start, DateOnly end);
}
