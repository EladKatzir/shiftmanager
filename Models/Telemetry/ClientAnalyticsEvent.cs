namespace ShiftManager.Models.Telemetry;

/// <summary>
/// Analytics event for tracking user interactions in the UI.
/// Designed for air-gapped local observability - no external dependencies.
/// B-019: Analytics Event Taxonomy (LOCAL)
/// </summary>
public class ClientAnalyticsEvent
{
    /// <summary>
    /// Primary key
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Event type identifier (e.g., calendar_view_changed, scope_changed, context_switched,
    /// widget_toggled, navigation_category_toggled)
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Additional event data in JSON format (e.g., {"from": "day", "to": "week"})
    /// </summary>
    public string? EventData { get; set; }

    /// <summary>
    /// SHA256 hash of user ID - no PII stored
    /// </summary>
    public string UserIdHash { get; set; } = string.Empty;

    /// <summary>
    /// Client-generated session identifier for grouping events
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Page URL where the event occurred (path only, no query params with PII)
    /// </summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Browser user agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// When the event occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; }
}
