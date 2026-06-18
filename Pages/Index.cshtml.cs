using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages;

/// <summary>
/// Root "/" landing. Under the domain-hub navigation (now the only nav), Home is the schedule-spine
/// for everyone, so "/" redirects to /Home/Index — which renders the personal spine + role widgets +
/// company announcements. The former "Task Overview" dashboard that lived here was consolidated into
/// Home (Phase 6 Home-spine merge); its announcements feed moved with it.
/// </summary>
[Authorize]
public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Home/Index");
}
