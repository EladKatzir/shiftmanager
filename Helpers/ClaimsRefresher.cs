using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ShiftManager.Models;
using System.Security.Claims;

namespace ShiftManager.Helpers;

/// <summary>
/// Re-mints the current request's auth cookie so display-time identity claims
/// (Name / GivenName / Email / AvatarFileName) reflect the latest AppUser values
/// immediately, instead of waiting for the next sign-out/sign-in.
///
/// Background: ASP.NET Core cookie auth serialises Claims into the encrypted cookie
/// at SignInAsync time — there is no per-request DB read of AppUser. So a profile
/// edit silently desyncs the cookie until the cookie is re-minted or expires. This
/// helper closes that gap for the four claims that drive UI greetings and avatars.
///
/// SCOPE — what this helper refreshes:
///   • <see cref="ClaimTypes.Name"/>        ← <c>user.DisplayName</c>
///   • <see cref="ClaimTypes.GivenName"/>   ← <c>user.PreferredName ?? FirstToken(DisplayName)</c>
///   • <see cref="ClaimTypes.Email"/>       ← <c>user.Email</c>
///   • <c>"AvatarFileName"</c>              ← <c>user.AvatarFileName</c> (omitted when null/blank)
///
/// Every OTHER claim from the existing principal is preserved verbatim — Role,
/// CompanyId, hierarchy IDs, RoleTemplateKey, AuthMethod, Griffin:UniqueID, etc.
/// The original ClaimsIdentity AuthenticationType is preserved too, so a Griffin
/// session stays "Griffin" and a local-password session stays "Cookies".
///
/// SELF-EDIT GUARD: a no-op when the editor isn't editing themselves. Admin
/// EditProfile can target any user, but we cannot remotely refresh another user's
/// browser cookie — their session lives in their browser, not ours. They will see
/// the new values on their next login. The early-return lets call sites invoke this
/// unconditionally after a save without branching on "is this self-edit?".
/// </summary>
public static class ClaimsRefresher
{
    private static readonly HashSet<string> StalenessSensitiveClaimTypes = new(StringComparer.Ordinal)
    {
        ClaimTypes.Name,
        ClaimTypes.GivenName,
        ClaimTypes.Email,
        "AvatarFileName"
    };

    public static async Task RefreshIfSelfAsync(HttpContext httpContext, int editedUserId, AppUser freshUser)
    {
        var currentIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(currentIdClaim, out var currentUserId)) return;
        if (currentUserId != editedUserId) return; // editing someone else — no-op

        if (httpContext.User.Identity is not ClaimsIdentity oldIdentity || !oldIdentity.IsAuthenticated)
            return;

        // Same fallback chain as GriffinService.BuildPrincipalFromClaimsAsync and
        // Pages/Auth/Login.cshtml.cs — keep these three in sync.
        var givenNameForClaim = !string.IsNullOrEmpty(freshUser.PreferredName)
            ? freshUser.PreferredName!
            : (freshUser.DisplayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? freshUser.DisplayName);

        // Patch-style rebuild: take everything except the four staleness-sensitive claims,
        // then re-add those with fresh DB values. Preserves AuthMethod, hierarchy claims,
        // RoleTemplateKey, theme, Griffin:* identifiers, etc.
        var preserved = oldIdentity.Claims
            .Where(c => !StalenessSensitiveClaimTypes.Contains(c.Type))
            .ToList();

        preserved.Add(new Claim(ClaimTypes.Name, freshUser.DisplayName ?? string.Empty));
        preserved.Add(new Claim(ClaimTypes.GivenName, givenNameForClaim ?? string.Empty));
        preserved.Add(new Claim(ClaimTypes.Email, freshUser.Email ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(freshUser.AvatarFileName))
            preserved.Add(new Claim("AvatarFileName", freshUser.AvatarFileName));

        var newIdentity = new ClaimsIdentity(preserved, oldIdentity.AuthenticationType);
        var newPrincipal = new ClaimsPrincipal(newIdentity);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, newPrincipal);
    }
}
