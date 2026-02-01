using System.Security.Claims;
using ShiftManager.Models;
using ShiftManager.Models.DTOs;

namespace ShiftManager.Services;

/// <summary>
/// Service for interacting with Griffin ADFS HTTP APIs
/// </summary>
public interface IGriffinService
{
    /// <summary>
    /// Builds the redirect URL for initiating Griffin ADFS authentication
    /// </summary>
    string BuildAuthenticationUrl(string griffinBaseUrl, string tokenConsumerUrl);

    /// <summary>
    /// Validates a Griffin token by calling /authorization/validate
    /// </summary>
    Task<bool> ValidateTokenAsync(string token, string griffinBaseUrl, int timeoutSeconds);

    /// <summary>
    /// Retrieves user claims from Griffin by calling /authorization/getClaims
    /// </summary>
    Task<GriffinClaimsDto?> GetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds);

    /// <summary>
    /// Combined: validates token and gets claims (with caching)
    /// </summary>
    Task<GriffinClaimsDto?> ValidateAndGetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds);

    /// <summary>
    /// Full authentication flow: validate token, get claims, lookup/provision user, build ClaimsPrincipal
    /// </summary>
    Task<ClaimsPrincipal?> AuthenticateUserAsync(string token, GriffinConfig config, string ipAddress);
}
