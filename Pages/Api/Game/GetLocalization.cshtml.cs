using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Api.Game;

/// <summary>
/// ✅ PHASE 19: API endpoint to get game localization strings for current user's language
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class GetLocalizationModel : PageModel
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<GetLocalizationModel> _logger;

    public GetLocalizationModel(IStringLocalizer<SharedResources> localizer, ILogger<GetLocalizationModel> logger)
    {
        _localizer = localizer;
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        try
        {
            // Return all game-related localization strings
            var localization = new
            {
                title = _localizer["Game_Title"].Value,
                instructions = _localizer["Game_Instructions"].Value,
                score = _localizer["Game_Score"].Value,
                trophy = _localizer["Game_Trophy"].Value,
                playAgain = _localizer["Game_PlayAgain"].Value,
                viewLeaderboard = _localizer["Game_ViewLeaderboard"].Value,
                scoreSaved = _localizer["Game_ScoreSaved"].Value,
                milestoneReached = _localizer["Game_MilestoneReached"].Value,

                // Roasting messages
                roasts = new
                {
                    r1000 = new[] {
                        _localizer["Game_Roast_1000_A"].Value,
                        _localizer["Game_Roast_1000_B"].Value,
                        _localizer["Game_Roast_1000_C"].Value
                    },
                    r2500 = new[] {
                        _localizer["Game_Roast_2500_A"].Value,
                        _localizer["Game_Roast_2500_B"].Value,
                        _localizer["Game_Roast_2500_C"].Value
                    },
                    r5000 = new[] {
                        _localizer["Game_Roast_5000_A"].Value,
                        _localizer["Game_Roast_5000_B"].Value,
                        _localizer["Game_Roast_5000_C"].Value
                    },
                    r7500 = new[] {
                        _localizer["Game_Roast_7500_A"].Value,
                        _localizer["Game_Roast_7500_B"].Value,
                        _localizer["Game_Roast_7500_C"].Value
                    },
                    r10000 = new[] {
                        _localizer["Game_Roast_10000_A"].Value,
                        _localizer["Game_Roast_10000_B"].Value,
                        _localizer["Game_Roast_10000_C"].Value
                    },
                    r15000 = new[] {
                        _localizer["Game_Roast_15000_A"].Value,
                        _localizer["Game_Roast_15000_B"].Value,
                        _localizer["Game_Roast_15000_C"].Value
                    },
                    r20000 = new[] {
                        _localizer["Game_Roast_20000_A"].Value,
                        _localizer["Game_Roast_20000_B"].Value,
                        _localizer["Game_Roast_20000_C"].Value
                    }
                },

                // Leaderboard strings
                leaderboard = new
                {
                    title = _localizer["Game_LeaderboardTitle"].Value,
                    allTime = _localizer["Game_AllTime"].Value,
                    monthly = _localizer["Game_Monthly"].Value,
                    rank = _localizer["Game_Rank"].Value,
                    player = _localizer["Game_Player"].Value,
                    yourBest = _localizer["Game_YourBest"].Value,
                    noScoresYet = _localizer["Game_NoScoresYet"].Value,
                    backToGame = _localizer["Game_BackToGame"].Value,
                    you = _localizer["Game_You"].Value
                }
            };

            return new JsonResult(localization);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching game localization");
            return new JsonResult(new { error = "Internal server error" })
            {
                StatusCode = 500
            };
        }
    }
}
