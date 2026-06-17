using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages.My;

/// <summary>
/// "Where everything lives" — a per-user navigation guide. Section 1 renders the CURRENT user's
/// reachable destinations (built from INavigationService, so it always matches their sidebar and
/// is auto-localized). Section 2 is a static per-role guide for all six ranks.
/// Authenticated only (AuthorizeFolder("/") convention) — no special grant.
/// </summary>
public class HelpNavigationModel : PageModel
{
    public void OnGet()
    {
    }
}
