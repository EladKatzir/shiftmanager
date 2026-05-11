using System.Security.Claims;
using ShiftManager.Models;
using ShiftManager.Models.DTOs;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Service for interacting with Griffin ADFS HTTP APIs.
/// All HTTP-facing methods return <see cref="GriffinApiResult{T}"/> so callers
/// can surface a specific, stable error token to the user without needing
/// server logs (required for air-gapped deployments).
/// </summary>
public interface IGriffinService
{
    /// <summary>
    /// Builds the redirect URL for initiating Griffin ADFS authentication.
    /// </summary>
    string BuildAuthenticationUrl(string griffinBaseUrl, string tokenConsumerUrl);

    /// <summary>
    /// Exchanges a hashed token (A, from Griffin callback) for a JWT (B) by
    /// calling /authentication/claimToken. The response body may be a raw JWT,
    /// a JSON-quoted string, or a JSON object containing a token/jwt field —
    /// all three shapes are tolerated.
    /// </summary>
    Task<GriffinApiResult<string>> ExchangeTokenAsync(string hashedToken, string griffinBaseUrl, int timeoutSeconds);

    /// <summary>
    /// Validates a Griffin JWT by calling /authorization/validate.
    /// A successful call with Value=false is a normal negative response
    /// (expired token) — not a failure. Failure means the call couldn't complete.
    /// </summary>
    Task<GriffinApiResult<bool>> ValidateTokenAsync(string token, string griffinBaseUrl, int timeoutSeconds);

    /// <summary>
    /// Retrieves user claims from Griffin by calling /authorization/getClaims.
    /// Fails with MissingEmailAddress / MissingUniqueId when those fields are
    /// absent — those are treated as hard failures since auth cannot continue.
    /// </summary>
    Task<GriffinApiResult<GriffinClaimsDto>> GetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds);

    /// <summary>
    /// Combined: validates token and gets claims (with caching). Claims are
    /// cached by SHA-256(token) for 2 hours; failures are NOT cached.
    /// </summary>
    Task<GriffinApiResult<GriffinClaimsDto>> ValidateAndGetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds);

    /// <summary>
    /// Full authentication flow: validate token, get claims, lookup/provision user, build ClaimsPrincipal.
    /// </summary>
    Task<GriffinApiResult<ClaimsPrincipal>> AuthenticateUserAsync(string token, GriffinConfig config, string ipAddress);

    /// <summary>
    /// Post-Griffin pipeline ONLY — given already-resolved claims, runs the DB lookup,
    /// role-template backfill, hierarchy load, grant application, and ClaimsPrincipal build.
    /// Used by <see cref="AuthenticateUserAsync"/> in production AND by the /GriffinDiagnostic
    /// "Simulate ADFS Login" diagnostic to exercise the post-Griffin path without HTTP calls.
    /// Pass tokenForHash=null in simulation mode (a sentinel hash will be recorded).
    /// </summary>
    Task<GriffinApiResult<ClaimsPrincipal>> BuildPrincipalFromClaimsAsync(
        GriffinClaimsDto griffinClaims, string? tokenForHash, string ipAddress);
}
