using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;

namespace ShiftManager.Pages.MyTeam;

[Authorize]
public class IndexModel : PageModel
{
    public void OnGet()
    {
        // Frontend will handle all data fetching via API calls
    }
}
