using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages.Scheduling.Eligibility;

/// <summary>
/// The single discoverable "where do I edit who can do shift/chore category X" home (Issue 3).
/// Phase 1: a hub landing that gathers the three eligibility surfaces in one place.
/// Phase 3 replaces this body with the By-category / By-person editor.
///
/// Policy MUST match the NavRegistry node policy for this route (P2: visibility == access).
/// </summary>
[Authorize(Policy = "Grant:ManageShiftCategories")]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
