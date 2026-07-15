using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Pages.Auth;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api.My;

/// <summary>
/// Per-user UI-language sync endpoint (#10 — "switched UI to Hebrew, later it reverted to
/// English"). Scope: the authenticated user only — reads
/// <c>User.FindFirst(ClaimTypes.NameIdentifier)</c> and never accepts a userId from the request
/// body. Persists <c>AppUser.PreferredLanguage</c> synchronously so a toggle-then-navigate-away
/// (before <c>PreferredLanguageLearningMiddleware</c> would have passively learned it on the next
/// request) still sticks. Mirrors <see cref="ThemeModel"/>'s shape.
///
/// Also re-seeds the <c>.AspNetCore.Culture</c> cookie via
/// <see cref="LoginModel.ReseedCultureCookie"/> after the DB write, for parity with the
/// login-time re-seed — the client-side toggles already write the cookie directly before
/// calling this endpoint, but doing it here too keeps the server the single source of truth for
/// correct cookie attributes (notably <c>Secure</c>) once the DB write has succeeded.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class LanguageModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<LanguageModel> _logger;

    public LanguageModel(AppDbContext db, ILogger<LanguageModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        LanguagePayload? payload;
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            payload = JsonSerializer.Deserialize<LanguagePayload>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid JSON." });
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Culture))
            return BadRequest(new { error = "Culture is required." });

        var culture = payload.Culture.Trim();

        // Allow-list against the single source of truth for supported cultures — never trust a
        // client-supplied culture string directly (it would otherwise flow into
        // RequestCulture(...) downstream and could throw, or persist a bogus value that later
        // fails at re-seed time).
        if (!RequestLocalizationSetup.SupportedCultures.Contains(culture))
            return BadRequest(new { error = "Unsupported culture." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        user.PreferredLanguage = culture;
        await _db.SaveChangesAsync();

        LoginModel.ReseedCultureCookie(HttpContext, culture);

        _logger.LogInformation("PreferredLanguage updated for user {UserId}: {Culture}", userId, culture);

        return new JsonResult(new { ok = true, culture });
    }

    /// <summary>
    /// Self-scoping: userId always comes from the authenticated principal's own claim, never
    /// from the request body. Mirrors <see cref="ThemeModel"/>'s identical private helper.
    /// </summary>
    private int? CurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("UserId")?.Value
                  ?? User.FindFirst("sub")?.Value;
        return int.TryParse(raw, out var id) ? id : (int?)null;
    }

    private sealed class LanguagePayload
    {
        public string? Culture { get; set; }
    }
}
