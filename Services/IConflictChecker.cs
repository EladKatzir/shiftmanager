using ShiftManager.Models;

namespace ShiftManager.Services;

public record ConflictResult(bool Allowed, List<string> Reasons)
{
    /// <summary>
    /// B-03: Error code for UI localization. The Reasons list contains English defaults.
    /// UI should check ErrorCode first and use localized string if available.
    /// </summary>
    public string? ErrorCode { get; init; }

    public static ConflictResult Fail(string reason, string? errorCode = null) =>
        new(false, new List<string> { reason }) { ErrorCode = errorCode };
    public static ConflictResult Ok() => new(true, new());
}

public interface IConflictChecker
{
    Task<ConflictResult> CanAssignAsync(int userId, ShiftInstance instance, CancellationToken ct = default);
}
