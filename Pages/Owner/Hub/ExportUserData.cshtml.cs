using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Text.Json;

namespace ShiftManager.Pages.Owner.Hub;

/// <summary>
/// Exports all user data as JSON (GDPR-style data portability).
/// QA Item 65 / E-06.
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters in UserDataExportService are SAFE — OwnerHub requires Grant:AdminAccess (all 107 grants)
[Authorize(Policy = "Grant:AdminAccess")]
public class ExportUserDataModel : PageModel
{
    private readonly UserDataExportService _exportService;
    private readonly ILogger<ExportUserDataModel> _logger;

    public ExportUserDataModel(UserDataExportService exportService, ILogger<ExportUserDataModel> logger)
    {
        _exportService = exportService;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(int userId)
    {
        if (userId <= 0)
        {
            return BadRequest("Invalid user ID");
        }

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
}
