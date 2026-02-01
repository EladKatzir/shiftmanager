using System;
using ShiftManager.Data;

namespace ShiftManager.Models;

/// <summary>
/// Diagnostic log entry for email API requests and responses.
/// Used for troubleshooting email integration issues in air-gapped environments.
/// </summary>
public class EmailApiLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    // Request Details
    public string RequestUrl { get; set; } = string.Empty;
    public string RequestMethod { get; set; } = "POST";
    public string? RequestHeaders { get; set; }  // JSON serialized
    public string? RequestBody { get; set; }      // JSON serialized

    // Response Details
    public int? ResponseStatusCode { get; set; }
    public string? ResponseHeaders { get; set; }  // JSON serialized
    public string? ResponseBody { get; set; }

    // Email Context
    public string RecipientEmail { get; set; } = string.Empty;
    public string EmailSubject { get; set; } = string.Empty;

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
