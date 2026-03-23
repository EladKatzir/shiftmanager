using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Text.Json;

namespace ShiftManager.Pages.Owner.Hub;

/// <summary>
/// Exports all user data as JSON (GDPR-style data portability).
/// Without a userId: renders a selection UI listing all users.
/// With a userId: immediately downloads single-user JSON export (existing behaviour).
/// QA Item 65 / E-06.
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters in UserDataExportService are SAFE — OwnerHub requires Grant:AdminAccess (all 107 grants)
// SECURITY-AUDITED: User list query uses IgnoreQueryFilters — SAFE — Owner has cross-tenant read access by design (Grant:AdminAccess)
[Authorize(Policy = "Grant:AdminAccess")]
public class ExportUserDataModel : PageModel
{
    private readonly UserDataExportService _exportService;
    private readonly AppDbContext _db;
    private readonly ILogger<ExportUserDataModel> _logger;

    public ExportUserDataModel(
        UserDataExportService exportService,
        AppDbContext db,
        ILogger<ExportUserDataModel> logger)
    {
        _exportService = exportService;
        _db = db;
        _logger = logger;
    }

    // ── Selection UI state ────────────────────────────────────────────────
    public List<UserListItem> AllUsers { get; set; } = new();

    [BindProperty]
    public List<int> SelectedUserIds { get; set; } = new();

    public record UserListItem(int Id, string DisplayName, string Email, string CompanyName);

    // ── GET: /Owner/Hub/ExportUserData (no userId → show selection UI) ────
    // ── GET: /Owner/Hub/ExportUserData/{userId} (userId > 0 → download) ──
    public async Task<IActionResult> OnGetAsync(int userId = 0)
    {
        if (userId > 0)
        {
            // Existing single-user download behaviour — unchanged
            return await DownloadSingleUserAsync(userId);
        }

        // No userId: load user list for the selection UI
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — Owner has cross-tenant read access via Grant:AdminAccess
        var rawUsers = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Join(
                _db.Companies.IgnoreQueryFilters().AsNoTracking(),
                u => u.CompanyId,
                c => c.Id,
                (u, c) => new { u.Id, u.DisplayName, u.Email, CompanyName = c.Name })
            .OrderBy(x => x.CompanyName)
            .ThenBy(x => x.DisplayName)
            .ToListAsync();

        AllUsers = rawUsers
            .Select(x => new UserListItem(x.Id, x.DisplayName, x.Email, x.CompanyName))
            .ToList();

        return Page();
    }

    // ── POST: /Owner/Hub/ExportUserData?handler=ExportBatch ──────────────
    public async Task<IActionResult> OnPostExportBatchAsync()
    {
        if (SelectedUserIds == null || SelectedUserIds.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one user to export.");
            // Reload user list so the page can re-render
            await LoadUserListAsync();
            return Page();
        }

        try
        {
            _logger.LogInformation(
                "Batch export requested for {Count} users by Owner",
                SelectedUserIds.Count);

            const int batchSize = 200;
            var results = new List<object>();

            for (int i = 0; i < SelectedUserIds.Count; i += batchSize)
            {
                var batch = SelectedUserIds.Skip(i).Take(batchSize).ToList();
                foreach (var uid in batch)
                {
                    var data = await _exportService.ExportUserDataAsync(uid);
                    if (data != null)
                    {
                        results.Add(data);
                    }
                    else
                    {
                        _logger.LogWarning("User {UserId} not found during batch export — skipped", uid);
                    }
                }
            }

            var json = JsonSerializer.Serialize(new
            {
                ExportedAt = DateTime.UtcNow,
                ExportVersion = "1.0",
                TotalUsers = results.Count,
                Users = results
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var fileName = $"BatchUserData_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            _logger.LogInformation(
                "Batch export completed: {Count} users exported",
                results.Count);

            return File(bytes, "application/json", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch user data export");
            ModelState.AddModelError(string.Empty, "An error occurred during export. Please try again.");
            await LoadUserListAsync();
            return Page();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<IActionResult> DownloadSingleUserAsync(int userId)
    {
        try
        {
            var data = await _exportService.ExportUserDataAsync(userId);

            if (data == null)
            {
                return NotFound($"User {userId} not found");
            }

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var fileName = $"UserData_{userId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            _logger.LogInformation("User data exported for UserId={UserId} by Owner", userId);

            return File(bytes, "application/json", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting data for user {UserId}", userId);
            return StatusCode(500, "Error exporting user data");
        }
    }

    private async Task LoadUserListAsync()
    {
        // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — Owner has cross-tenant read access via Grant:AdminAccess
        var rawUsers = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Join(
                _db.Companies.IgnoreQueryFilters().AsNoTracking(),
                u => u.CompanyId,
                c => c.Id,
                (u, c) => new { u.Id, u.DisplayName, u.Email, CompanyName = c.Name })
            .OrderBy(x => x.CompanyName)
            .ThenBy(x => x.DisplayName)
            .ToListAsync();

        AllUsers = rawUsers
            .Select(x => new UserListItem(x.Id, x.DisplayName, x.Email, x.CompanyName))
            .ToList();
    }
}
