using System;
using ShiftManager.Data;

namespace ShiftManager.Models;

/// <summary>
/// Diagnostic log entry for Griffin ADFS connection tests.
/// Used for troubleshooting Griffin authentication integration issues in air-gapped environments.
/// Tracks connection attempts, HTTP responses, and redirect behavior.
/// </summary>
public class GriffinApiLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    // Request Details
    public string RequestUrl { get; set; } = string.Empty;
    public string RequestMethod { get; set; } = "GET";
    public string? RequestHeaders { get; set; }  // JSON serialized

    // Response Details
    public int? ResponseStatusCode { get; set; }
    public string? ResponseHeaders { get; set; }  // JSON serialized
    public string? ResponseBody { get; set; }

    // Griffin-Specific Details
    public string? RedirectUrl { get; set; }  // Where Griffin redirects (ADFS login page)

    // Result & Metadata
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Validation Info
    public string? ValidationErrors { get; set; }  // JSON serialized

    // Navigation Property
    public Company? Company { get; set; }
}
