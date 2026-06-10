using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
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
    private readonly AppDbContext _db;

    public QuietModel(INotificationLinkTokenService tokens, INotificationPreferenceService preferences, AppDbContext db)
    {
        _tokens = tokens;
        _preferences = preferences;
        _db = db;
    }

    public enum ResultState { Invalid, QuietSet, Reengaged }

    public ResultState State { get; private set; } = ResultState.Invalid;

    /// <summary>Echoed back so the "undo" form can re-use the same signed token.</summary>
    public string? Token { get; private set; }

    /// <summary>Resolve the recipient's own company from the signed userId token.</summary>
    private async Task<(int userId, int companyId)?> ResolveAsync(string? token)
    {
        if (!_tokens.TryParse(token, out var userId))
            return null;

        var companyId = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.CompanyId)
            .FirstOrDefaultAsync();

        return companyId.HasValue ? (userId, companyId.Value) : null;
    }

    public async Task OnGetAsync(string? token)
    {
        var resolved = await ResolveAsync(token);
        if (resolved == null)
        {
            State = ResultState.Invalid;
            return;
        }

        await _preferences.SetEngagementModeAsync(resolved.Value.userId, resolved.Value.companyId, EngagementMode.Quiet);
        Token = token;
        State = ResultState.QuietSet;
    }

    public async Task OnPostReengageAsync(string? token)
    {
        var resolved = await ResolveAsync(token);
        if (resolved == null)
        {
            State = ResultState.Invalid;
            return;
        }

        await _preferences.SetEngagementModeAsync(resolved.Value.userId, resolved.Value.companyId, EngagementMode.Engaged);
        State = ResultState.Reengaged;
    }
}
