using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using System.Security.Claims;

namespace ShiftManager.Pages.Game;

/// <summary>
/// Leaderboard page showing top game scores — company-scoped (E-08 fix)
/// </summary>
[Authorize]
public class LeaderboardModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<LeaderboardModel> _logger;

    public LeaderboardModel(AppDbContext db, ILogger<LeaderboardModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public List<LeaderboardEntry> AllTimeLeaderboard { get; set; } = new();
    public List<LeaderboardEntry> MonthlyLeaderboard { get; set; } = new();
    public LeaderboardEntry? UserBestAllTime { get; set; }
    public LeaderboardEntry? UserBestMonthly { get; set; }
    public int CurrentUserId { get; set; }

    public async Task OnGetAsync()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(companyIdClaim))
            return;

        CurrentUserId = int.Parse(userIdClaim);
        var companyId = int.Parse(companyIdClaim);

        // E-08: Scope leaderboard by company to prevent cross-tenant user name exposure
        var companyUserIds = await _db.Users
            .Where(u => u.CompanyId == companyId)
            .Select(u => u.Id)
            .ToListAsync();

        AllTimeLeaderboard = await GetLeaderboardData(companyUserIds, allTime: true);
        MonthlyLeaderboard = await GetLeaderboardData(companyUserIds, allTime: false);
        UserBestAllTime = await GetUserBest(CurrentUserId, companyUserIds, allTime: true);
        UserBestMonthly = await GetUserBest(CurrentUserId, companyUserIds, allTime: false);
    }

    private async Task<List<LeaderboardEntry>> GetLeaderboardData(List<int> companyUserIds, bool allTime)
    {
        // E-08: Company-scoped leaderboard — only show users from same company
        var query = _db.GameScores.Where(gs => companyUserIds.Contains(gs.UserId));

        if (!allTime)
        {
            var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");
            query = query.Where(gs => gs.CurrentMonth == currentMonth);
        }

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

        var userIds = leaderboard.Select(l => l.UserId).ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync();

        return leaderboard.Select((item, index) => new LeaderboardEntry
        {
            Rank = index + 1,
            UserId = item.UserId,
            DisplayName = AbbreviateName(users.FirstOrDefault(u => u.Id == item.UserId)?.DisplayName, item.UserId == CurrentUserId),
            Score = item.Score,
            PlayedAt = item.PlayedAt,
            IsCurrentUser = item.UserId == CurrentUserId
        }).ToList();
    }

    private async Task<LeaderboardEntry?> GetUserBest(int userId, List<int> companyUserIds, bool allTime)
    {
        var query = _db.GameScores.Where(gs => gs.UserId == userId);

        if (!allTime)
        {
            var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");
            query = query.Where(gs => gs.CurrentMonth == currentMonth);
        }

        var userBestScore = await query.MaxAsync(gs => (int?)gs.Score);
        if (!userBestScore.HasValue) return null;

        // E-08: Calculate rank within company only
        var allScoresQuery = _db.GameScores.Where(gs => companyUserIds.Contains(gs.UserId));
        if (!allTime)
        {
            var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");
            allScoresQuery = allScoresQuery.Where(gs => gs.CurrentMonth == currentMonth);
        }

        var rank = await allScoresQuery
            .GroupBy(gs => gs.UserId)
            .Select(g => g.Max(gs => gs.Score))
            .Where(s => s >= userBestScore.Value)
            .CountAsync();

        var currentUser = await _db.Users.FindAsync(userId);

        return new LeaderboardEntry
        {
            Rank = rank,
            UserId = userId,
            DisplayName = currentUser?.DisplayName ?? "You",
            Score = userBestScore.Value,
            IsCurrentUser = true
        };
    }

    /// <summary>
    /// Abbreviates a name for OPSEC on public leaderboard (B-11).
    /// Full names are not shown — only first name + last initial.
    /// Current user sees their own full name.
    /// </summary>
    private static string AbbreviateName(string? displayName, bool isCurrentUser)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "???";
        if (isCurrentUser) return displayName; // Show own full name

        var parts = displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0]} {parts[^1][0]}.";
        }
        return parts[0]; // Single name — show as-is
    }

    public class LeaderboardEntry
    {
        public int Rank { get; set; }
        public int UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int Score { get; set; }
        public DateTime PlayedAt { get; set; }
        public bool IsCurrentUser { get; set; }
    }
}
