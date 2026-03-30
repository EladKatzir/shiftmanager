using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages.My;

[Authorize]
public class HelpCalendarModel : PageModel
{
    public void OnGet()
    {
    }
}
