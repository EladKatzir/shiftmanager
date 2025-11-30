using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ShiftManager.Pages.Schedule
{
    [Authorize]
    public class IndexModel : PageModel
    {
        public bool IsAdmin { get; set; }
        public string UserName { get; set; } = "";
        public string DefaultView { get; set; } = "month"; // month, week, day
        public string DefaultMode { get; set; } = "calendar"; // calendar, table

        public void OnGet(string? view, string? mode)
        {
            UserName = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
            IsAdmin = User.IsInRole("Owner") || User.IsInRole("Manager") || User.IsInRole("Director");

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
