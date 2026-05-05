using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Single source of truth for "is user X busy on date Y?" used by every
/// assignment flow (shift, chore, on-duty, swap, fill-range). See plan
/// in docs/superpowers/specs for design context.
/// </summary>
public interface IBusyService
{
    /// <summary>
    /// Returns per-user busy summaries for a date, keyed by user id.
    /// Used by assignee pickers to decorate options with busy badges.
    /// When <paramref name="target"/> is non-null, each summary also carries a
    /// hard-error flag computed by running the full validation predicate against
    /// the target — so the picker can disable hard-conflict users in lock-step
    /// with the post-submit validator.
    /// </summary>
    Task<Dictionary<int, BusySummaryWithHardError>> GetBusyStatesAsync(
        IReadOnlyList<int> userIds,
        DateOnly date,
        int moleculeId,
        BusyTarget? target = null);

    /// <summary>
    /// Validates an assignment target against a user. Returns errors (hard blocks)
    /// and warnings (overrideable via HMAC token). When <paramref name="overrideToken"/>
    /// is supplied and matches the warnings, the warnings list is cleared so callers
    /// can proceed.
    /// </summary>
    Task<BusyValidation> ValidateAsync(
        BusyTarget target,
        int userId,
        int actorUserId,
        string? overrideToken = null);

    /// <summary>
    /// Batch validation for fill-range and other bulk flows. Pre-loads users,
    /// shift instances, assignments in shared queries.
    /// </summary>
    Task<Dictionary<(BusyTarget target, int userId), BusyValidation>> ValidateBatchAsync(
        IReadOnlyList<(BusyTarget target, int userId)> assignments,
        int actorUserId);

    /// <summary>
    /// Generates a 5-minute HMAC token scoped to the exact target + user + warning keys.
    /// </summary>
    string GenerateOverrideToken(BusyTarget target, int userId, IReadOnlyList<string> warningKeys);

    /// <summary>
    /// Validates a previously-issued override token against the target + user.
    /// </summary>
    bool ValidateOverrideToken(string? token, BusyTarget target, int userId);
}

/// <summary>
/// Picker-friendly extension of <see cref="BusySummary"/> that also carries the
/// hard-error verdict for a given target, so the listbox can disable options
/// without a second roundtrip.
/// </summary>
public record BusySummaryWithHardError(
    BusySummary Summary,
    bool HasHardError,
    string? HardErrorKey);
