using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// In-memory ring buffer of recent Griffin ADFS authentication events (success + failure).
/// Surfaces to the admin diagnostic page so an air-gapped operator can see WHAT broke
/// without needing access to the application log file or Windows Event Viewer.
///
/// Singleton lifetime — the buffer must survive across requests. Thread-safe.
///
/// PII handling: the email field is intentionally NOT masked here — the diagnostic page
/// is gated behind <c>Grant:AdminAccess</c> and the whole point of the surface is to let
/// an admin find out which user's login broke. The render layer is responsible for
/// applying display-time masking if it surfaces externally.
/// </summary>
public interface IGriffinAuthDiagnostics
{
    /// <summary>Record an auth attempt (success or failure).</summary>
    void Record(GriffinAuthEvent evt);

    /// <summary>Get the most recent N events, newest-first.</summary>
    IReadOnlyList<GriffinAuthEvent> GetRecent(int count = 50);

    /// <summary>Clear the buffer (admin-initiated; primarily for tests).</summary>
    void Clear();
}

/// <summary>
/// One Griffin auth attempt — failure or success. Captured at the GriffinCallback exit
/// points so it covers the full pipeline (transport → validate → claims → user lookup
/// → principal build).
/// </summary>
public sealed record GriffinAuthEvent(
    DateTime TimestampUtc,
    bool Success,
    GriffinStage? FailureStage,
    GriffinErrorCode? FailureCode,
    string? ErrorToken,
    string? Email,
    string IpAddress,
    int? UserId,
    string? ExceptionType,
    string? ExceptionMessage,
    string? StackTrace,
    string? TechnicalDetail,
    int DurationMs);
