using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Game;

/// <summary>
/// API endpoint to get game configuration for the current user's company
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class GetConfigurationModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<GetConfigurationModel> _logger;

    public GetConfigurationModel(AppDbContext db, ILogger<GetConfigurationModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            // Get company ID from authenticated user
            int? companyId = null;

            if (User.Identity?.IsAuthenticated == true)
            {
                var companyIdClaim = User.FindFirst("CompanyId")?.Value;
                if (!string.IsNullOrEmpty(companyIdClaim) && int.TryParse(companyIdClaim, out var parsedCompanyId))
                {
                    companyId = parsedCompanyId;
                }
            }

            // For anonymous users, return sensible defaults without loading from any company's data.
            // Game config is cosmetic (scoring, grid size, milestones) so defaults are safe.
            if (!companyId.HasValue)
            {
                return new JsonResult(new
                {
                    enabled = true,
                    gridSize = 6,
                    scoring = new
                    {
                        points3Match = 40,
                        points4Match = 100,
                        points5PlusMatch = 200
                    },
                    megaCombo = new
                    {
                        multiplier = 2,
                        min3MatchLines = 0,
                        min4MatchLines = 2,
                        min5MatchLines = 0
                    },
                    milestones = new[] { 1000, 2500, 5000, 7500, 10000, 15000, 20000 }
                });
            }

            // Load configuration from database for authenticated user's company
            var configs = await _db.Configs
                .Where(c => c.CompanyId == companyId.Value)
                .Where(c => c.Key.StartsWith("Game"))
                .ToDictionaryAsync(c => c.Key, c => c.Value);

            // Helper function to get config value with fallback
            string GetConfig(string key, string fallback) => configs.TryGetValue(key, out var value) ? value : fallback;
            int GetInt(string key, int fallback) => int.TryParse(GetConfig(key, fallback.ToString()), out var val) ? val : fallback;
            bool GetBool(string key, bool fallback) => bool.TryParse(GetConfig(key, fallback.ToString()), out var val) ? val : fallback;

            // Parse milestones
            var milestonesStr = GetConfig("GameMilestones", "1000,2500,5000,7500,10000,15000,20000");
            var milestones = milestonesStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(m => int.TryParse(m.Trim(), out var val) ? val : 0)
                .Where(m => m > 0)
                .ToList();

            var configuration = new
            {
                enabled = GetBool("GameEnabled", true),
                gridSize = GetInt("GameGridSize", 6),
                scoring = new
                {
                    points3Match = GetInt("GamePointsPer3Match", 40),
                    points4Match = GetInt("GamePointsPer4Match", 100),
                    points5PlusMatch = GetInt("GamePointsPer5PlusMatch", 200)
                },
                megaCombo = new
                {
                    multiplier = GetInt("GameMegaComboMultiplier", 2),
                    min3MatchLines = GetInt("GameMegaCombo3MatchMinLines", 0),
                    min4MatchLines = GetInt("GameMegaCombo4MatchMinLines", 2),
                    min5MatchLines = GetInt("GameMegaCombo5MatchMinLines", 0)
                },
                milestones = milestones
            };

            return new JsonResult(configuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching game configuration");
            return new JsonResult(new { error = "Internal server error" })
            {
                StatusCode = 500
            };
        }
    }
}
