namespace ShiftManager.Models.Api;

/// <summary>
/// Represents an API key for external integrations.
/// Sidecar table - does not modify existing authentication.
/// </summary>
public class ApiKey
{
    public int Id { get; set; }

    /// <summary>
    /// The actual API key string (hashed in database)
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Plain text API key (stored for owner access - SECURITY WARNING)
    /// This allows owners to retrieve keys at any time.
    /// In production, consider encrypting this field.
    /// </summary>
    public string? PlainTextKey { get; set; }

    /// <summary>
    /// Company this API key belongs to (multi-tenant isolation)
    /// </summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// Human-readable name for this API key
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of scopes (e.g., "user:read,shift:read,timeoff:write")
    /// </summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>
    /// Whether this API key is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Rate limit in requests per minute (default: 100)
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 100;

    /// <summary>
    /// User ID who created this API key
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// When this API key was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this API key expires (null = never expires)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Last time this API key was used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? CreatedByUser { get; set; }

    /// <summary>
    /// Check if this API key has a specific scope
    /// </summary>
    public bool HasScope(string scope)
    {
        var scopes = Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return scopes.Contains(scope) || scopes.Contains("*");
    }

    /// <summary>
    /// Check if this API key is valid (active, not expired)
    /// </summary>
    public bool IsValid()
    {
        if (!IsActive) return false;
        if (ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow) return false;
        return true;
    }
}
