using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Pages.Api.My;

/// <summary>
/// Per-user appearance (theme) endpoint. Scope: the authenticated user only —
/// reads <c>User.FindFirst(ClaimTypes.NameIdentifier)</c> and never accepts a userId
/// from the request body. Also updates the <c>theme</c> cookie so the next page
/// render derives colors without a DB hit.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class ThemeModel : PageModel
{
    private const string CookieName = "theme";
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly string[] AllowedModes = { "light", "dark", "auto" };

    private readonly AppDbContext _db;
    private readonly ILogger<ThemeModel> _logger;

    public ThemeModel(AppDbContext db, ILogger<ThemeModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => new { u.ThemeColor, u.ThemeMode })
            .FirstOrDefaultAsync();

        if (user is null) return NotFound();
        return new JsonResult(new { color = user.ThemeColor, mode = user.ThemeMode });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        ThemePayload? payload;
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            payload = JsonSerializer.Deserialize<ThemePayload>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid JSON." });
        }

        if (payload is null) return BadRequest(new { error = "Empty body." });

        // Null/empty color+mode means "reset to defaults".
        string? color = NormalizeColor(payload.Color);
        string? mode = NormalizeMode(payload.Mode);

        if (!string.IsNullOrEmpty(payload.Color) && color is null)
            return BadRequest(new { error = "Color must be #RRGGBB." });
        if (!string.IsNullOrEmpty(payload.Mode) && mode is null)
            return BadRequest(new { error = "Mode must be light|dark|auto." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        user.ThemeColor = color;
        user.ThemeMode = mode;
        await _db.SaveChangesAsync();

        SetThemeCookie(color, mode);
        _logger.LogInformation("Theme updated for user {UserId}: color={Color}, mode={Mode}", userId, color, mode);

        return new JsonResult(new { ok = true, color, mode });
    }

    public async Task<IActionResult> OnDeleteAsync()
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        user.ThemeColor = null;
        user.ThemeMode = null;
        await _db.SaveChangesAsync();

        Response.Cookies.Delete(CookieName);
        return new JsonResult(new { ok = true });
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("UserId")?.Value
                  ?? User.FindFirst("sub")?.Value;
        return int.TryParse(raw, out var id) ? id : (int?)null;
    }

    private static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        color = color.Trim();
        return HexColor.IsMatch(color) ? color.ToUpperInvariant() : null;
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return null;
        mode = mode.Trim().ToLowerInvariant();
        return AllowedModes.Contains(mode) ? mode : null;
    }

    private void SetThemeCookie(string? color, string? mode)
    {
        if (color is null && mode is null)
        {
            Response.Cookies.Delete(CookieName);
            return;
        }

        var value = JsonSerializer.Serialize(new { color, mode });
        Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = false,            // needs to be readable by theme-engine.js for offline derivation
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            Path = "/"
        });
    }

    private sealed class ThemePayload
    {
        public string? Color { get; set; }
        public string? Mode { get; set; }
    }
}
