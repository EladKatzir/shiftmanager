using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages.My;

[Authorize]
public class HelpRolesModel : PageModel
{
    public void OnGet()
    {
    }
}
