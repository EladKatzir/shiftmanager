using Microsoft.Extensions.Localization;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

/// <summary>
/// Turns a <see cref="GriffinApiError"/> into a three-part user-facing message
/// (title / detail / remediation) plus the stable error token the user should
/// quote to an admin. Title and remediation are localized via .resx; detail
/// is admin-facing English because it names HTTP statuses, hostnames and
/// stages that don't translate meaningfully.
///
/// This is the ONLY place that maps error codes to strings — keep it here so
/// the callback page view stays a dumb display of already-formatted text.
/// </summary>
public static class GriffinErrorMessages
{
    public record DisplayMessage(string Title, string Detail, string Remediation, string ErrorToken);

    public static DisplayMessage Describe(GriffinApiError error, IStringLocalizer<SharedResources> localizer)
    {
        var title = localizer["Auth_GriffinError_Title"].Value;
        var detail = BuildDetail(error);
        var remediation = BuildRemediation(error, localizer);
        return new DisplayMessage(title, detail, remediation, error.ErrorToken);
    }

    private static string BuildDetail(GriffinApiError e)
    {
        var host = string.IsNullOrEmpty(e.Host) ? "the ADFS server" : $"'{e.Host}'";
        var stage = StageLabel(e.Stage);

        return e.Code switch
        {
            GriffinErrorCode.NetworkDnsFailure
                => $"DNS lookup for {host} failed during {stage}. The application server cannot resolve the hostname configured in Griffin BaseUrl.",

            GriffinErrorCode.NetworkConnectionRefused
                => $"{host} refused the connection during {stage}. The Griffin service may be stopped, or a firewall may be blocking the port.",

            GriffinErrorCode.NetworkTimeout
                => $"{host} did not respond within the timeout during {stage}. Network latency or firewall drops are the usual causes.",

            GriffinErrorCode.NetworkSslError
                => $"TLS handshake with {host} failed during {stage}. The ADFS certificate may be self-signed, expired, or not trusted by this machine. Set Griffin:SkipSslValidation=true in appsettings for non-production use only.",

            GriffinErrorCode.NetworkOther
                => $"A network-level error occurred reaching {host} during {stage}.",

            GriffinErrorCode.HttpBadRequest
                => $"{host} returned HTTP 400 during {stage}. The request was rejected as malformed — the Griffin API contract may have changed, or the hashed token was truncated.",

            GriffinErrorCode.HttpUnauthorized
                => $"{host} returned HTTP 401 during {stage}. The token was rejected as unauthorized — it may have already been used or has expired.",

            GriffinErrorCode.HttpForbidden
                => $"{host} returned HTTP 403 during {stage}. The ADFS server refused the token; your account may not be permitted to sign in.",

            GriffinErrorCode.HttpNotFound
                => $"{host} returned HTTP 404 during {stage}. The configured Griffin BaseUrl does not expose the expected endpoint — verify it in /Owner/GriffinConfig.",

            GriffinErrorCode.HttpServerError
                => $"{host} returned HTTP {e.HttpStatus} during {stage}. This is a server-side error on the ADFS side.",

            GriffinErrorCode.HttpOther
                => $"{host} returned an unexpected HTTP {e.HttpStatus} during {stage}.",

            GriffinErrorCode.EmptyResponse
                => $"{host} returned an empty body during {stage}. The endpoint responded but gave no data — likely a Griffin-side issue.",

            GriffinErrorCode.UnreadableResponse
                => $"{host} returned a response during {stage} that could not be parsed. The Griffin API may have changed its contract."
                   + (string.IsNullOrEmpty(e.ResponsePreview) ? "" : $" Preview: '{e.ResponsePreview}'."),

            GriffinErrorCode.UnexpectedResponseShape
                => $"{host} returned an unexpected response shape during {stage}."
                   + (string.IsNullOrEmpty(e.ResponsePreview) ? "" : $" Preview: '{e.ResponsePreview}'."),

            GriffinErrorCode.MissingEmailAddress
                => "ADFS authenticated you successfully but did not return an email address. Ask the ADFS administrator to ensure your user profile has an email.",

            GriffinErrorCode.MissingUniqueId
                => "ADFS authenticated you successfully but did not return a unique user ID. Ask the ADFS administrator to check your user profile.",

            GriffinErrorCode.UserNotRegistered
                => "ADFS authenticated you, but no ShiftManager account exists for your email address, and auto-provisioning is disabled.",

            GriffinErrorCode.UserDeactivated
                => "Your ShiftManager account has been deactivated.",

            GriffinErrorCode.AutoProvisionFailed
                => "ADFS authenticated you, but creating a ShiftManager account for you failed. A database error is the likely cause — check the server log.",

            GriffinErrorCode.UnhandledException
                => $"An unexpected error occurred during {stage}. Technical detail: {e.TechnicalDetail}",

            _ => e.TechnicalDetail
        };
    }

    private static string BuildRemediation(GriffinApiError e, IStringLocalizer<SharedResources> loc)
    {
        return e.Code switch
        {
            GriffinErrorCode.NetworkDnsFailure
                or GriffinErrorCode.NetworkConnectionRefused
                or GriffinErrorCode.NetworkTimeout
                or GriffinErrorCode.NetworkSslError
                or GriffinErrorCode.NetworkOther
                or GriffinErrorCode.HttpNotFound
                => loc["Auth_GriffinError_Remediation_CheckConfig"].Value,

            GriffinErrorCode.HttpUnauthorized
                or GriffinErrorCode.HttpForbidden
                => loc["Auth_GriffinError_Remediation_Retry"].Value,

            GriffinErrorCode.HttpBadRequest
                or GriffinErrorCode.HttpServerError
                or GriffinErrorCode.HttpOther
                or GriffinErrorCode.EmptyResponse
                or GriffinErrorCode.UnreadableResponse
                or GriffinErrorCode.UnexpectedResponseShape
                => loc["Auth_GriffinError_Remediation_ContactAdfsAdmin"].Value,

            GriffinErrorCode.MissingEmailAddress
                or GriffinErrorCode.MissingUniqueId
                or GriffinErrorCode.UserNotRegistered
                or GriffinErrorCode.UserDeactivated
                or GriffinErrorCode.AutoProvisionFailed
                or GriffinErrorCode.UnhandledException
                => loc["Auth_GriffinError_Remediation_ContactSysAdmin"].Value,

            _ => loc["Auth_GriffinError_Remediation_ContactSysAdmin"].Value
        };
    }

    private static string StageLabel(GriffinStage stage) => stage switch
    {
        GriffinStage.TokenExchange => "token exchange (claimToken)",
        GriffinStage.TokenValidation => "token validation",
        GriffinStage.ClaimsRetrieval => "claims retrieval (getClaims)",
        GriffinStage.UserLookup => "user lookup",
        GriffinStage.UserProvisioning => "user provisioning",
        _ => stage.ToString()
    };
}
