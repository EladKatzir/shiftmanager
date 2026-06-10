namespace ShiftManager.Services.Notifications;

/// <summary>
/// Creates and validates the signed, stateless token embedded in the one-click opt-out link at the
/// bottom of every email. Backed by ASP.NET Data Protection, so the token is tamper-proof and needs
/// no DB lookup — the link works for anonymous (not-logged-in) recipients clicking from their inbox.
/// </summary>
public interface INotificationLinkTokenService
{
    /// <summary>Create an opt-out token encoding the recipient userId. The recipient's company is
    /// derived from the user at redemption time, so it is intentionally not part of the token
    /// (avoids a cross-tenant companyId mismatch when the footer is built in the background sender).</summary>
    string CreateQuietToken(int userId);

    /// <summary>
    /// Validate and decode a token. Returns false (and zeroed output) for any missing, malformed,
    /// or tampered token.
    /// </summary>
    bool TryParse(string? token, out int userId);
}
