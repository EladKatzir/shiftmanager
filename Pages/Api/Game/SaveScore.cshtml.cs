using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Game;

/// <summary>
/// ✅ PHASE 19: API endpoint to save game scores to the leaderboard
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// game leaderboard is global by design; rank calculation must span all companies
[Authorize]
[IgnoreAntiforgeryToken]
public class SaveScoreModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<SaveScoreModel> _logger;

    public SaveScoreModel(AppDbContext db, ILogger<SaveScoreModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            // Parse request body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<SaveScoreRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Score <= 0)
            {
                return new JsonResult(new { success = false, error = "Invalid score data" })
                {
                    StatusCode = 400
                };
            }

            // Get current user
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(companyIdClaim))
            {
                return new JsonResult(new { success = false, error = "User not authenticated" })
                {
                    StatusCode = 401
                };
            }

            if (!int.TryParse(userIdClaim, out var userId) || !int.TryParse(companyIdClaim, out var companyId))
            {
                return new JsonResult(new { success = false, error = "Invalid user data" })
                {
                    StatusCode = 400
                };
            }

            // Create game score entry
            var gameScore = new GameScore
            {
                CompanyId = companyId,
                UserId = userId,
                Score = data.Score,
                PlayedAt = DateTime.UtcNow,
                CurrentMonth = DateTime.UtcNow.ToString("yyyy-MM")
            };

            _db.GameScores.Add(gameScore);
            await _db.SaveChangesAsync();

            // Calculate user's rank (all-time, public leaderboard)
            var rank = await _db.GameScores
                .IgnoreQueryFilters()
                .Where(gs => gs.Score >= data.Score)
                .Select(gs => gs.UserId)
                .Distinct()
                .CountAsync();

            _logger.LogInformation("Game score saved: UserId={UserId}, CompanyId={CompanyId}, Score={Score}, Rank={Rank}",
                userId, companyId, data.Score, rank);

            return new JsonResult(new
            {
                success = true,
                rank = rank,
                score = data.Score
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving game score");
            return new JsonResult(new { success = false, error = "Internal server error" })
            {
                StatusCode = 500
            };
        }
    }

    private class SaveScoreRequest
    {
        public int Score { get; set; }
    }
}
