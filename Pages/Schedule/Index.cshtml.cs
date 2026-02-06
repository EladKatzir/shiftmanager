using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Schedule
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly IGrantService _grantService;

        public IndexModel(IGrantService grantService)
        {
            _grantService = grantService;
        }

        public bool IsAdmin { get; set; }
        public string UserName { get; set; } = "";
        public string DefaultView { get; set; } = "month"; // month, week, day
        public string DefaultMode { get; set; } = "calendar"; // calendar, table

        public async Task OnGetAsync(string? view, string? mode)
        {
            UserName = User.FindFirst(ClaimTypes.Name)?.Value ?? "";

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                IsAdmin = await _grantService.HasGrantAsync(userId, "AccessAdminNavigation");
            }

            // Set view and mode from query params or defaults
            DefaultView = view?.ToLower() switch
            {
                "week" => "week",
                "day" => "day",
                _ => "month"
            };

            DefaultMode = mode?.ToLower() switch
            {
                "table" => "table",
                _ => "calendar"
            };
        }
    }
}
