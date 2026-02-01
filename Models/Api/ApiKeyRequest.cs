namespace ShiftManager.Models.Api;

/// <summary>
/// Represents a user's request for an API key.
/// Follows approval workflow: Pending -> Approved/Rejected
/// Sidecar table - additive only, no changes to existing auth.
/// </summary>
public class ApiKeyRequest
{
    public int Id { get; set; }

    /// <summary>
    /// Company this request belongs to (multi-tenant isolation)
    /// </summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// User requesting the API key
    /// </summary>
    public int RequestedBy { get; set; }

    /// <summary>
    /// Human-readable name for the API key
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description/reason for requesting the API key
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of requested scopes
    /// </summary>
    public string RequestedScopes { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the request
    /// </summary>
    public ApiKeyRequestStatus Status { get; set; } = ApiKeyRequestStatus.Pending;

    /// <summary>
    /// When the request was submitted
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who reviewed the request (approved or rejected)
    /// </summary>
    public int? ReviewedBy { get; set; }

    /// <summary>
    /// When the request was reviewed
    /// </summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// Admin's notes/reason for approval or rejection
    /// </summary>
    public string? ReviewNotes { get; set; }

    /// <summary>
    /// If approved, the generated API key ID
    /// </summary>
    public int? GeneratedApiKeyId { get; set; }

    /// <summary>
    /// Rate limit granted (requests per minute)
    /// </summary>
    public int? ApprovedRateLimit { get; set; }

    /// <summary>
    /// Approved scopes (may differ from requested scopes)
    /// </summary>
    public string? ApprovedScopes { get; set; }

    /// <summary>
    /// Expiration date for the generated key (null = never expires)
    /// </summary>
    public DateTime? ApprovedExpiresAt { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public AppUser? ReviewedByUser { get; set; }
    public ApiKey? GeneratedApiKey { get; set; }
}

/// <summary>
/// Status of an API key request
/// </summary>
public enum ApiKeyRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Expired = 3  // Request expired without review
}
