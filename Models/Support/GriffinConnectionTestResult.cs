namespace ShiftManager.Models.Support;

/// <summary>
/// Result object for Griffin connection test diagnostics.
/// Contains detailed information about the HTTP request/response for troubleshooting.
/// </summary>
public class GriffinConnectionTestResult
{
    /// <summary>
    /// Whether the connection test succeeded (HTTP 200 or expected redirect)
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// HTTP status code from Griffin response (null if network error)
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// URL where Griffin redirected (typically ADFS login page)
    /// </summary>
    public string? RedirectUrl { get; set; }

    /// <summary>
    /// Error message if the test failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Duration of the connection test in milliseconds
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// HTTP response headers from Griffin
    /// </summary>
    public Dictionary<string, string>? ResponseHeaders { get; set; }

    /// <summary>
    /// HTTP response body from Griffin (limited for display)
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// List of validation errors (if URL format invalid, etc.)
    /// </summary>
    public List<string>? ValidationErrors { get; set; }
}
