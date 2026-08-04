using ShiftManager.Models.Api;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing API keys and approval workflow
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// User requests a new API key
    /// </summary>
    Task<(ApiKeyRequest? request, string? error)> RequestApiKeyAsync(
        int companyId,
        int userId,
        string name,
        string description,
        string requestedScopes);

    /// <summary>
    /// List all pending API key requests for a company (admin view)
    /// </summary>
    Task<List<ApiKeyRequest>> ListPendingRequestsAsync(int companyId);

    /// <summary>
    /// List all API key requests (all statuses) for a company (admin view)
    /// </summary>
    Task<List<ApiKeyRequest>> ListAllRequestsAsync(int companyId, int page = 1, int pageSize = 50);

    /// <summary>
    /// List API key requests for a specific user
    /// </summary>
    Task<List<ApiKeyRequest>> ListUserRequestsAsync(int companyId, int userId);

    /// <summary>
    /// Approve an API key request and generate the key
    /// Returns the plain-text API key (only shown once)
    /// </summary>
    Task<(string? apiKey, ApiKeyRequest? request, string? error)> ApproveRequestAsync(
        int requestId,
        int reviewerId,
        string? reviewNotes = null,
        string? approvedScopes = null,
        int? rateLimitPerMinute = null,
        DateTime? expiresAt = null);

    /// <summary>
    /// Reject an API key request
    /// </summary>
    Task<(ApiKeyRequest? request, string? error)> RejectRequestAsync(
        int requestId,
        int reviewerId,
        string reviewNotes);

    /// <summary>
    /// Revoke an active API key
    /// </summary>
    /// <param name="callerIsAdmin">
    /// Set by callers that have ALREADY verified administrative rights (the admin revoke handler).
    /// Defaults to false so the check fails closed: a self-service caller must own the key.
    /// "Own" means the caller is the user whose approved request generated it
    /// (<see cref="ApiKeyRequest.RequestedBy"/>) — NOT <see cref="ApiKey.CreatedBy"/>, which holds the
    /// approving reviewer.
    /// </param>
    Task<(ApiKey? key, string? error)> RevokeApiKeyAsync(int keyId, int revokedBy, string? reason = null, bool callerIsAdmin = false);

    /// <summary>
    /// List all active API keys for a user
    /// </summary>
    Task<List<ApiKey>> ListUserKeysAsync(int companyId, int userId);

    /// <summary>
    /// List all API keys for a company (admin view)
    /// </summary>
    Task<List<ApiKey>> ListAllKeysAsync(int companyId, bool includeInactive = false);

    /// <summary>
    /// Get an API key request by ID
    /// </summary>
    Task<ApiKeyRequest?> GetRequestByIdAsync(int requestId);

    /// <summary>
    /// Get an API key by ID
    /// </summary>
    Task<ApiKey?> GetKeyByIdAsync(int keyId);

    /// <summary>
    /// Regenerate an existing API key (creates new key hash, invalidates old key)
    /// </summary>
    /// <param name="callerIsAdmin">See <see cref="RevokeApiKeyAsync"/>. Defaults to false (fail closed).</param>
    Task<(string? newApiKey, string? error)> RegenerateApiKeyAsync(int keyId, int regeneratedBy, bool callerIsAdmin = false);
}
