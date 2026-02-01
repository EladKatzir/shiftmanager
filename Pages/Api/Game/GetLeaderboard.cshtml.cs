using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Game;

/// <summary>
/// ✅ PHASE 19: API endpoint to get leaderboard data (all-time or monthly)
/// </summary>
[Authorize]
public class GetLeaderboardModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<GetLeaderboardModel> _logger;

    public GetLeaderboardModel(AppDbContext db, ILogger<GetLeaderboardModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync([FromQuery] string type = "all-time")
    {
        try
        {
            // Get current user's company
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(companyIdClaim) || string.IsNullOrEmpty(userIdClaim))
            {
                return new JsonResult(new { success = false, error = "User not authenticated" })
                {
                    StatusCode = 401
                };
            }

            var companyId = int.Parse(companyIdClaim);
            var userId = int.Parse(userIdClaim);

            // Build query based on type
            var query = _db.GameScores
                .Where(gs => gs.CompanyId == companyId);

            if (type == "monthly")
            {
                var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");
                query = query.Where(gs => gs.CurrentMonth == currentMonth);
            }

            // Get top 10 scores (highest score per user)
            var leaderboard = await query
                .GroupBy(gs => gs.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    Score = g.Max(gs => gs.Score),
                    PlayedAt = g.OrderByDescending(gs => gs.Score).First().PlayedAt
                })
                .OrderByDescending(x => x.Score)
                .Take(10)
                .ToListAsync();

            // Get user display names
            var userIds = leaderboard.Select(l => l.UserId).ToList();
            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToListAsync();

            // Combine data
            var leaderboardData = leaderboard.Select((item, index) => new
            {
                rank = index + 1,
                userId = item.UserId,
                displayName = users.FirstOrDefault(u => u.Id == item.UserId)?.DisplayName ?? "Unknown",
                score = item.Score,
                playedAt = item.PlayedAt,
                isCurrentUser = item.UserId == userId
            }).ToList();

            // Get current user's best score (if not in top 10)
            var userInTop10 = leaderboardData.Any(l => l.isCurrentUser);
            object? userBest = null;

            if (!userInTop10)
            {
                var userBestScore = await query
                    .Where(gs => gs.UserId == userId)
                    .MaxAsync(gs => (int?)gs.Score);

                if (userBestScore.HasValue)
                {
                    // Calculate rank
                    var userRank = await query
                        .GroupBy(gs => gs.UserId)
                        .Select(g => g.Max(gs => gs.Score))
                        .Where(s => s >= userBestScore.Value)
                        .CountAsync();

                    var currentUser = await _db.Users.FindAsync(userId);

                    userBest = new
                    {
                        rank = userRank,
                        userId = userId,
                        displayName = currentUser?.DisplayName ?? "You",
                        score = userBestScore.Value,
                        isCurrentUser = true
                    };
                }
            }

            return new JsonResult(new
            {
                success = true,
                type = type,
                leaderboard = leaderboardData,
                userBest = userBest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching leaderboard");
            return new JsonResult(new { success = false, error = "Internal server error" })
            {
                StatusCode = 500
            };
        }
    }
}
