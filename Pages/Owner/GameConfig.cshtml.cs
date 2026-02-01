using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Game Configuration - Configure Shift Swap match-3 game settings
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class GameConfigModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<GameConfigModel> _logger;

    public GameConfigModel(
        AppDbContext db,
        IAuditLogService auditLogService,
        ILogger<GameConfigModel> logger)
    {
        _db = db;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    // Game Status
    [BindProperty] public bool GameEnabled { get; set; }

    // Scoring
    [BindProperty] public int PointsPer3Match { get; set; }
    [BindProperty] public int PointsPer4Match { get; set; }
    [BindProperty] public int PointsPer5PlusMatch { get; set; }

    // Mega Combo
    [BindProperty] public int MegaComboMultiplier { get; set; }
    [BindProperty] public int MegaCombo3MatchMinLines { get; set; }
    [BindProperty] public int MegaCombo4MatchMinLines { get; set; }
    [BindProperty] public int MegaCombo5MatchMinLines { get; set; }

    // Grid & Milestones
    [BindProperty] public int GridSize { get; set; }
    [BindProperty] public string Milestones { get; set; } = string.Empty;

    public string? Success { get; set; }
    public string? Error { get; set; }
    public string? Warning { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            // Load current configuration
            GameEnabled = await GetBoolAsync("GameEnabled", true);
            GridSize = await GetIntAsync("GameGridSize", 6);
            PointsPer3Match = await GetIntAsync("GamePointsPer3Match", 40);
            PointsPer4Match = await GetIntAsync("GamePointsPer4Match", 100);
            PointsPer5PlusMatch = await GetIntAsync("GamePointsPer5PlusMatch", 200);
            MegaComboMultiplier = await GetIntAsync("GameMegaComboMultiplier", 2);
            MegaCombo3MatchMinLines = await GetIntAsync("GameMegaCombo3MatchMinLines", 0);
            MegaCombo4MatchMinLines = await GetIntAsync("GameMegaCombo4MatchMinLines", 2);
            MegaCombo5MatchMinLines = await GetIntAsync("GameMegaCombo5MatchMinLines", 0);
            Milestones = await GetStringAsync("GameMilestones", "1000,2500,5000,7500,10000,15000,20000");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading game configuration");
            Error = "Failed to load game configuration.";
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            // Validate inputs
            var validationError = ValidateInputs();
            if (validationError != null)
            {
                Error = validationError;
                return Page();
            }

            var currentUserId = GetCurrentUserId();
            var userName = User.Identity?.Name ?? "Unknown";

            // Save all configuration values
            await SetAsync("GameEnabled", GameEnabled.ToString());
            await SetAsync("GameGridSize", GridSize.ToString());
            await SetAsync("GamePointsPer3Match", PointsPer3Match.ToString());
            await SetAsync("GamePointsPer4Match", PointsPer4Match.ToString());
            await SetAsync("GamePointsPer5PlusMatch", PointsPer5PlusMatch.ToString());
            await SetAsync("GameMegaComboMultiplier", MegaComboMultiplier.ToString());
            await SetAsync("GameMegaCombo3MatchMinLines", MegaCombo3MatchMinLines.ToString());
            await SetAsync("GameMegaCombo4MatchMinLines", MegaCombo4MatchMinLines.ToString());
            await SetAsync("GameMegaCombo5MatchMinLines", MegaCombo5MatchMinLines.ToString());
            await SetAsync("GameMilestones", Milestones);

            await _db.SaveChangesAsync();

            // Log the configuration change
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "GameConfigUpdated",
                "GameConfig",
                null,
                "Game configuration updated",
                $"Enabled={GameEnabled}, GridSize={GridSize}, 3Match={PointsPer3Match}, 4Match={PointsPer4Match}, 5Match={PointsPer5PlusMatch}, MegaMultiplier={MegaComboMultiplier}");

            Success = "Game configuration saved successfully. Changes will apply to new game sessions.";

            // Add warning if grid size is not 6
            if (GridSize != 6)
            {
                Warning = "Warning: Grid size has been changed from the default (6). This may require JavaScript updates.";
            }

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving game configuration");
            Error = "Failed to save game configuration.";
            return Page();
        }
    }

    private string? ValidateInputs()
    {
        // Grid size validation
        if (GridSize < 4 || GridSize > 10)
        {
            return "Grid size must be between 4 and 10.";
        }

        // Points validation
        if (PointsPer3Match < 0 || PointsPer3Match > 10000)
        {
            return "Points for 3-match must be between 0 and 10,000.";
        }
        if (PointsPer4Match < 0 || PointsPer4Match > 10000)
        {
            return "Points for 4-match must be between 0 and 10,000.";
        }
        if (PointsPer5PlusMatch < 0 || PointsPer5PlusMatch > 10000)
        {
            return "Points for 5+-match must be between 0 and 10,000.";
        }

        // Logical validation
        if (PointsPer4Match < PointsPer3Match)
        {
            return "Points for 4-match should be >= points for 3-match.";
        }
        if (PointsPer5PlusMatch < PointsPer4Match)
        {
            return "Points for 5+-match should be >= points for 4-match.";
        }

        // Mega combo validation
        if (MegaComboMultiplier < 1 || MegaComboMultiplier > 10)
        {
            return "Mega combo multiplier must be between 1 and 10.";
        }
        if (MegaCombo3MatchMinLines < 0 || MegaCombo3MatchMinLines > 10)
        {
            return "Mega combo 3-match min lines must be between 0 and 10.";
        }
        if (MegaCombo4MatchMinLines < 0 || MegaCombo4MatchMinLines > 10)
        {
            return "Mega combo 4-match min lines must be between 0 and 10.";
        }
        if (MegaCombo5MatchMinLines < 0 || MegaCombo5MatchMinLines > 10)
        {
            return "Mega combo 5-match min lines must be between 0 and 10.";
        }

        // At least one mega combo type must be enabled
        if (MegaCombo3MatchMinLines == 0 && MegaCombo4MatchMinLines == 0 && MegaCombo5MatchMinLines == 0)
        {
            return "At least one mega combo trigger must be enabled (not all zero).";
        }

        // Milestones validation
        var milestoneValues = Milestones.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var milestone in milestoneValues)
        {
            if (!int.TryParse(milestone.Trim(), out var val) || val <= 0)
            {
                return "Milestones must be positive integers separated by commas.";
            }
        }

        return null; // Valid
    }

    // Helper methods to get/set config values
    private async Task<bool> GetBoolAsync(string key, bool defaultValue)
    {
        var companyId = GetCompanyId();
        var config = await _db.Configs.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Key == key);
        if (config == null) return defaultValue;
        return bool.TryParse(config.Value, out var value) ? value : defaultValue;
    }

    private async Task<int> GetIntAsync(string key, int defaultValue)
    {
        var companyId = GetCompanyId();
        var config = await _db.Configs.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Key == key);
        if (config == null) return defaultValue;
        return int.TryParse(config.Value, out var value) ? value : defaultValue;
    }

    private async Task<string> GetStringAsync(string key, string defaultValue)
    {
        var companyId = GetCompanyId();
        var config = await _db.Configs.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Key == key);
        return config?.Value ?? defaultValue;
    }

    private async Task SetAsync(string key, string value)
    {
        var companyId = GetCompanyId();
        var config = await _db.Configs.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Key == key);

        if (config == null)
        {
            config = new Models.AppConfig
            {
                CompanyId = companyId,
                Key = key,
                Value = value
            };
            _db.Configs.Add(config);
        }
        else
        {
            config.Value = value;
        }
    }

    private int GetCompanyId()
    {
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        return int.TryParse(companyIdClaim, out var id) ? id : 1;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var id) ? id : 0;
    }
}
