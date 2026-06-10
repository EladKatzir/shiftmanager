namespace ShiftManager.Services.Notifications;

/// <summary>
/// Creates and validates the signed, stateless token embedded in the one-click opt-out link at the
/// bottom of every email. Backed by ASP.NET Data Protection, so the token is tamper-proof and needs
/// no DB lookup — the link works for anonymous (not-logged-in) recipients clicking from their inbox.
/// </summary>
public interface INotificationLinkTokenService
{
    /// <summary>Create an opt-out token encoding the recipient (userId + companyId).</summary>
    string CreateQuietToken(int userId, int companyId);

    /// <summary>
    /// Validate and decode a token. Returns false (and zeroed outputs) for any missing, malformed,
    /// or tampered token.
    /// </summary>
    bool TryParse(string? token, out int userId, out int companyId);
}
