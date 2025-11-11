using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Api;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing API keys with approval workflow
/// </summary>
public class ApiKeyService : IApiKeyService
{
    private readonly AppDbContext _context;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(
        AppDbContext context,
        IAuditLogService auditLogService,
        ILogger<ApiKeyService> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<(ApiKeyRequest? request, string? error)> RequestApiKeyAsync(
        int companyId,
        int userId,
        string name,
        string description,
        string requestedScopes)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(name))
            return (null, "API key name is required");

        if (string.IsNullOrWhiteSpace(description))
            return (null, "Description is required");

        if (string.IsNullOrWhiteSpace(requestedScopes))
            return (null, "At least one scope must be requested");

        // Check for duplicate pending requests with same name
        var existingPending = await _context.ApiKeyRequests
            .Where(r => r.CompanyId == companyId &&
                       r.RequestedBy == userId &&
                       r.Name == name &&
                       r.Status == ApiKeyRequestStatus.Pending)
            .AnyAsync();

        if (existingPending)
            return (null, "You already have a pending request with this name");

        // Create the request
        var request = new ApiKeyRequest
        {
            CompanyId = companyId,
            RequestedBy = userId,
            Name = name,
            Description = description,
            RequestedScopes = requestedScopes,
            Status = ApiKeyRequestStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        _context.ApiKeyRequests.Add(request);
        await _context.SaveChangesAsync();

        // Audit log
        await _auditLogService.LogAsync(
            "ApiKeyRequest.Created",
            "ApiKeyRequest",
            request.Id,
            $"Requested API key: {name}",
            System.Text.Json.JsonSerializer.Serialize(new { name, requestedScopes }));

        _logger.LogInformation("API key request created: {RequestId} by user {UserId} in company {CompanyId}",
            request.Id, userId, companyId);

        return (request, null);
    }

    public async Task<List<ApiKeyRequest>> ListPendingRequestsAsync(int companyId)
    {
        return await _context.ApiKeyRequests
            .Include(r => r.RequestedByUser)
            .Where(r => r.CompanyId == companyId && r.Status == ApiKeyRequestStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync();
    }

    public async Task<List<ApiKeyRequest>> ListAllRequestsAsync(int companyId, int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        return await _context.ApiKeyRequests
            .Include(r => r.RequestedByUser)
            .Include(r => r.ReviewedByUser)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<ApiKeyRequest>> ListUserRequestsAsync(int companyId, int userId)
    {
        return await _context.ApiKeyRequests
            .Include(r => r.ReviewedByUser)
            .Include(r => r.GeneratedApiKey)
            .Where(r => r.CompanyId == companyId && r.RequestedBy == userId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync();
    }

    public async Task<(string? apiKey, ApiKeyRequest? request, string? error)> ApproveRequestAsync(
        int requestId,
        int reviewerId,
        string? reviewNotes = null,
        string? approvedScopes = null,
        int? rateLimitPerMinute = null,
        DateTime? expiresAt = null)
    {
        var request = await _context.ApiKeyRequests
            .Include(r => r.Company)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return (null, null, "Request not found");

        if (request.Status != ApiKeyRequestStatus.Pending)
            return (null, null, $"Request is not pending (current status: {request.Status})");

        // Use requested scopes if no custom scopes provided
        var scopes = approvedScopes ?? request.RequestedScopes;

        // Validate scopes
        if (string.IsNullOrWhiteSpace(scopes))
            return (null, null, "Approved scopes cannot be empty");

        // Generate API key
        var (plainTextKey, keyHash) = GenerateApiKey();

        // Create the API key record
        var apiKey = new ApiKey
        {
            KeyHash = keyHash,
            PlainTextKey = plainTextKey,  // Store plain text for owner access
            CompanyId = request.CompanyId,
            Name = request.Name,
            Scopes = scopes,
            IsActive = true,
            RateLimitPerMinute = rateLimitPerMinute ?? 100,
            CreatedBy = reviewerId,  // Reviewer creates it on behalf of requester
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();

        // Update the request
        request.Status = ApiKeyRequestStatus.Approved;
        request.ReviewedBy = reviewerId;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewNotes = reviewNotes;
        request.GeneratedApiKeyId = apiKey.Id;
        request.ApprovedScopes = scopes;
        request.ApprovedRateLimit = apiKey.RateLimitPerMinute;
        request.ApprovedExpiresAt = expiresAt;

        await _context.SaveChangesAsync();

        // Audit log
        await _auditLogService.LogAsync(
            "ApiKeyRequest.Approved",
            "ApiKeyRequest",
            request.Id,
            $"Approved API key request: {request.Name}",
            System.Text.Json.JsonSerializer.Serialize(new { requestId, apiKeyId = apiKey.Id, scopes, requestedBy = request.RequestedBy }));

        _logger.LogInformation("API key request approved: {RequestId} by reviewer {ReviewerId}, generated key {KeyId}",
            requestId, reviewerId, apiKey.Id);

        return (plainTextKey, request, null);
    }

    public async Task<(ApiKeyRequest? request, string? error)> RejectRequestAsync(
        int requestId,
        int reviewerId,
        string reviewNotes)
    {
        if (string.IsNullOrWhiteSpace(reviewNotes))
            return (null, "Rejection reason is required");

        var request = await _context.ApiKeyRequests
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return (null, "Request not found");

        if (request.Status != ApiKeyRequestStatus.Pending)
            return (null, $"Request is not pending (current status: {request.Status})");

        request.Status = ApiKeyRequestStatus.Rejected;
        request.ReviewedBy = reviewerId;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewNotes = reviewNotes;

        await _context.SaveChangesAsync();

        // Audit log
        await _auditLogService.LogAsync(
            "ApiKeyRequest.Rejected",
            "ApiKeyRequest",
            request.Id,
            $"Rejected API key request: {request.Name}",
            System.Text.Json.JsonSerializer.Serialize(new { requestId, requestedBy = request.RequestedBy, reason = reviewNotes }));

        _logger.LogInformation("API key request rejected: {RequestId} by reviewer {ReviewerId}",
            requestId, reviewerId);

        return (request, null);
    }

    public async Task<(ApiKey? key, string? error)> RevokeApiKeyAsync(int keyId, int revokedBy, string? reason = null)
    {
        var apiKey = await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId);

        if (apiKey == null)
            return (null, "API key not found");

        if (!apiKey.IsActive)
            return (null, "API key is already inactive");

        apiKey.IsActive = false;
        await _context.SaveChangesAsync();

        // Audit log
        await _auditLogService.LogAsync(
            "ApiKey.Revoked",
            "ApiKey",
            apiKey.Id,
            $"Revoked API key: {apiKey.Name}",
            System.Text.Json.JsonSerializer.Serialize(new { keyId, reason }));

        _logger.LogInformation("API key revoked: {KeyId} by user {RevokedBy}. Reason: {Reason}",
            keyId, revokedBy, reason ?? "No reason provided");

        return (apiKey, null);
    }

    public async Task<List<ApiKey>> ListUserKeysAsync(int companyId, int userId)
    {
        // Find keys where the user requested them
        var userRequestIds = await _context.ApiKeyRequests
            .Where(r => r.CompanyId == companyId &&
                       r.RequestedBy == userId &&
                       r.Status == ApiKeyRequestStatus.Approved &&
                       r.GeneratedApiKeyId != null)
            .Select(r => r.GeneratedApiKeyId!.Value)
            .ToListAsync();

        return await _context.ApiKeys
            .Where(k => k.CompanyId == companyId && userRequestIds.Contains(k.Id) && k.IsActive)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<ApiKey>> ListAllKeysAsync(int companyId, bool includeInactive = false)
    {
        var query = _context.ApiKeys
            .Include(k => k.CreatedByUser)
            .Where(k => k.CompanyId == companyId);

        if (!includeInactive)
            query = query.Where(k => k.IsActive);

        return await query
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();
    }

    public async Task<ApiKeyRequest?> GetRequestByIdAsync(int requestId)
    {
        return await _context.ApiKeyRequests
            .Include(r => r.RequestedByUser)
            .Include(r => r.ReviewedByUser)
            .Include(r => r.GeneratedApiKey)
            .FirstOrDefaultAsync(r => r.Id == requestId);
    }

    public async Task<ApiKey?> GetKeyByIdAsync(int keyId)
    {
        return await _context.ApiKeys
            .Include(k => k.CreatedByUser)
            .Include(k => k.Company)
            .FirstOrDefaultAsync(k => k.Id == keyId);
    }

    public async Task<(string? newApiKey, string? error)> RegenerateApiKeyAsync(int keyId, int regeneratedBy)
    {
        var apiKey = await _context.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId);

        if (apiKey == null)
            return (null, "API key not found");

        if (!apiKey.IsActive)
            return (null, "Cannot regenerate inactive API key");

        // Generate new key
        var (plainTextKey, keyHash) = GenerateApiKey();

        // Update existing record
        var oldKeyPreview = apiKey.KeyHash.Substring(0, Math.Min(8, apiKey.KeyHash.Length));
        apiKey.KeyHash = keyHash;
        apiKey.PlainTextKey = plainTextKey;  // Update plain text key for owner access
        apiKey.LastUsedAt = null;  // Reset usage

        await _context.SaveChangesAsync();

        // Audit log
        await _auditLogService.LogAsync(
            "ApiKey.Regenerated",
            "ApiKey",
            apiKey.Id,
            $"Regenerated API key: {apiKey.Name}",
            System.Text.Json.JsonSerializer.Serialize(new { keyId, oldKeyPreview }));

        _logger.LogInformation("API key regenerated: {KeyId} by user {RegeneratedBy}",
            keyId, regeneratedBy);

        return (plainTextKey, null);
    }

    /// <summary>
    /// Generates a cryptographically secure API key
    /// Returns (plainTextKey, keyHash)
    /// </summary>
    private (string plainTextKey, string keyHash) GenerateApiKey()
    {
        // Generate 48 bytes to ensure we have enough characters after cleanup
        var randomBytes = new byte[48];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        // Convert to base64 and remove special characters
        var base64 = Convert.ToBase64String(randomBytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");

        // Take first 48 characters (or all if less)
        var keyPart = base64.Length >= 48 ? base64.Substring(0, 48) : base64;
        var plainTextKey = $"sk_{keyPart}";

        // Hash the key for storage
        var keyHash = HashApiKey(plainTextKey);

        return (plainTextKey, keyHash);
    }

    /// <summary>
    /// Hashes an API key using SHA256
    /// </summary>
    private string HashApiKey(string apiKey)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
