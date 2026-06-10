using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Services.Notifications;

namespace ShiftManager.Pages.N;

/// <summary>
/// One-click opt-out landing page linked from the footer of every email. Anonymous + token-driven:
/// the signed token encodes (userId, companyId), so a recipient can switch to Quiet mode straight
/// from their inbox without logging in. Idempotent and reversible (an "undo" re-enables all emails).
/// </summary>
[AllowAnonymous]
public class QuietModel : PageModel
{
    private readonly INotificationLinkTokenService _tokens;
    private readonly INotificationPreferenceService _preferences;

    public QuietModel(INotificationLinkTokenService tokens, INotificationPreferenceService preferences)
    {
        _tokens = tokens;
        _preferences = preferences;
    }

    public enum ResultState { Invalid, QuietSet, Reengaged }

    public ResultState State { get; private set; } = ResultState.Invalid;

    /// <summary>Echoed back so the "undo" form can re-use the same signed token.</summary>
    public string? Token { get; private set; }

    public async Task OnGetAsync(string? token)
    {
        if (!_tokens.TryParse(token, out var userId, out var companyId))
        {
            State = ResultState.Invalid;
            return;
        }

        await _preferences.SetEngagementModeAsync(userId, companyId, EngagementMode.Quiet);
        Token = token;
        State = ResultState.QuietSet;
    }

    public async Task OnPostReengageAsync(string? token)
    {
        if (!_tokens.TryParse(token, out var userId, out var companyId))
        {
            State = ResultState.Invalid;
            return;
        }

        await _preferences.SetEngagementModeAsync(userId, companyId, EngagementMode.Engaged);
        State = ResultState.Reengaged;
    }
}
