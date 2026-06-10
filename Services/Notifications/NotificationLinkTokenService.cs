using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ShiftManager.Services.Notifications;

/// <summary>
/// Data-Protection-backed implementation of <see cref="INotificationLinkTokenService"/>.
/// The token is <c>Protect("{userId}:{companyId}")</c>; tampering or a wrong key throws
/// <see cref="CryptographicException"/> on unprotect, which we translate to a clean validation
/// failure. No expiry is applied: opt-out links in old emails should keep working, and the action
/// is idempotent (set Quiet), so there is no replay risk.
/// </summary>
public sealed class NotificationLinkTokenService : INotificationLinkTokenService
{
    private readonly IDataProtector _protector;

    public NotificationLinkTokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ShiftManager.Notifications.OptOutLink.v1");
    }

    public string CreateQuietToken(int userId)
        => _protector.Protect(userId.ToString());

    public bool TryParse(string? token, out int userId)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var raw = _protector.Unprotect(token);
            return int.TryParse(raw, out userId) && userId > 0;
        }
        catch (CryptographicException)
        {
            return false; // tampered / wrong key / not one of our tokens
        }
        catch (FormatException)
        {
            return false; // not valid protected payload
        }
    }
}
