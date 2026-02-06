using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// Calendar landing page - entry point to all calendar types.
/// Displays premium cards for Shifts, Chores, On-Call, and Overview calendars.
/// </summary>
[Authorize]
public class IndexModel : PageModel
{
    public void OnGet()
    {
        // Simple landing page - no data loading required.
        // Access control for individual calendars is handled on their respective pages.
    }
}
