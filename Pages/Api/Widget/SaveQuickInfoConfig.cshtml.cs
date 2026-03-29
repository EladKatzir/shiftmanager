using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Widget;

/// <summary>
/// API endpoint for saving/loading Quick Info widget configuration.
/// GET: returns current config + available items for the config modal.
/// POST: saves the config (delete-and-reinsert pattern via IQuickInfoConfigService).
/// </summary>
[Authorize(Policy = "Grant:ManageStores")]
[IgnoreAntiforgeryToken]
public class SaveQuickInfoConfigModel : PageModel
{
    private readonly IQuickInfoConfigService _configService;
    private readonly AppDbContext _db;
    private readonly ILogger<SaveQuickInfoConfigModel> _logger;

    public SaveQuickInfoConfigModel(
        IQuickInfoConfigService configService,
        AppDbContext db,
        ILogger<SaveQuickInfoConfigModel> logger)
    {
        _configService = configService;
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(int moleculeId)
    {
        try
        {
            // IDOR check: verify moleculeId matches user's molecule
            var moleculeIdClaim = User.FindFirst("MoleculeId")?.Value;
            if (!int.TryParse(moleculeIdClaim, out var userMoleculeId) || userMoleculeId != moleculeId)
            {
                _logger.LogWarning(
                    "SECURITY: User attempted to read QuickInfoConfig for molecule {MoleculeId} but their claim is {ClaimMoleculeId}",
                    moleculeId, moleculeIdClaim ?? "null");
                return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 403 };
            }

            // Get current config items (or defaults if none saved)
            var hasConfig = await _configService.HasConfigAsync(moleculeId);
            var configItems = hasConfig
                ? (await _configService.GetConfigForMoleculeAsync(moleculeId))
                    .Select(c => new ConfigItemDto
                    {
                        SectionType = c.SectionType,
                        EntityId = c.EntityId,
                        IsEnabled = c.IsEnabled,
                        DisplayOrder = c.DisplayOrder
                    })
                    .ToList()
                : (await _configService.GetDefaultConfigAsync(moleculeId))
                    .Select(c => new ConfigItemDto
                    {
                        SectionType = c.SectionType,
                        EntityId = c.EntityId,
                        IsEnabled = c.IsEnabled,
                        DisplayOrder = c.DisplayOrder
                    })
                    .ToList();

            // Get available duty types (global, active only)
            var isHebrew = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "he";
            var dutyTypes = await _db.OnDutyTypeConfigs
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.TypeValue)
                .Select(t => new AvailableItemDto
                {
                    SectionType = QuickInfoSectionType.OnCallRole,
                    EntityId = t.TypeValue,
                    Name = isHebrew ? (t.NameHe ?? t.NameEn) : t.NameEn,
                    Icon = t.Icon
                })
                .ToListAsync();

            // Get available stores for the molecule's area
            var areaId = await _db.Molecules
                .Where(m => m.Id == moleculeId)
                .Select(m => m.AreaId)
                .FirstOrDefaultAsync();

            var stores = areaId > 0
                ? await _db.Stores
                    .AsNoTracking()
                    .Where(s => s.AreaId == areaId && s.IsActive)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.NameEn)
                    .Select(s => new AvailableItemDto
                    {
                        SectionType = QuickInfoSectionType.Store,
                        EntityId = s.Id,
                        Name = isHebrew ? (s.NameHe ?? s.NameEn) : s.NameEn,
                        Icon = null
                    })
                    .ToListAsync()
                : new List<AvailableItemDto>();

            return new JsonResult(new
            {
                success = true,
                hasConfig,
                items = configItems,
                availableDutyTypes = dutyTypes,
                availableStores = stores
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading QuickInfoConfig for molecule {MoleculeId}", moleculeId);
            return new JsonResult(new { success = false, message = "An error occurred while loading configuration" })
            {
                StatusCode = 500
            };
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            // Parse JSON body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<SaveConfigRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || request.Items == null)
            {
                return new JsonResult(new { success = false, message = "Invalid request data" })
                {
                    StatusCode = 400
                };
            }

            // IDOR check: verify moleculeId matches user's molecule
            var moleculeIdClaim = User.FindFirst("MoleculeId")?.Value;
            if (!int.TryParse(moleculeIdClaim, out var userMoleculeId) || userMoleculeId != request.MoleculeId)
            {
                _logger.LogWarning(
                    "SECURITY: User attempted to save QuickInfoConfig for molecule {MoleculeId} but their claim is {ClaimMoleculeId}",
                    request.MoleculeId, moleculeIdClaim ?? "null");
                return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 403 };
            }

            // Get userId
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("SaveQuickInfoConfig: User not authenticated - IsAuth={IsAuth}, NameId={NameId}",
                    User.Identity?.IsAuthenticated ?? false, userIdClaim ?? "null");
                return new JsonResult(new { success = false, message = "Invalid user" }) { StatusCode = 401 };
            }

            // Validate item count (reasonable upper bound to prevent abuse)
            if (request.Items.Count > 100)
            {
                return new JsonResult(new { success = false, message = "Too many configuration items" })
                {
                    StatusCode = 400
                };
            }

            // Map DTOs to service items
            var items = request.Items.Select(i => new QuickInfoConfigItem
            {
                SectionType = i.SectionType,
                EntityId = i.EntityId,
                DisplayOrder = i.DisplayOrder,
                IsEnabled = i.IsEnabled
            }).ToList();

            var (success, message) = await _configService.SaveConfigAsync(request.MoleculeId, userId, items);

            _logger.LogInformation(
                "QuickInfoConfig save attempt for molecule {MoleculeId} by user {UserId}: success={Success}",
                request.MoleculeId, userId, success);

            return new JsonResult(new { success, message })
            {
                StatusCode = success ? 200 : 400
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving QuickInfoConfig");
            return new JsonResult(new { success = false, message = "An error occurred while saving configuration" })
            {
                StatusCode = 500
            };
        }
    }
}

// Request/Response DTOs — scoped to this API endpoint
public class SaveConfigRequest
{
    public int MoleculeId { get; set; }
    public List<ConfigItemDto> Items { get; set; } = new();
}

public class ConfigItemDto
{
    public QuickInfoSectionType SectionType { get; set; }
    public int EntityId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class AvailableItemDto
{
    public QuickInfoSectionType SectionType { get; set; }
    public int EntityId { get; set; }
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
}
