using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages.Dev;

[Authorize(Policy = "Grant:AdminAccess")]
public class FeedbackPreviewModel : PageModel
{
    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public void OnGet() { }

    public IActionResult OnPostTriggerTempDataSuccess()
    {
        TempData["SuccessMessage"] = "Server-side success via TempData → FeedbackModal.";
        return RedirectToPage();
    }

    public IActionResult OnPostTriggerTempDataError()
    {
        TempData["ErrorMessage"] = "Server-side error via TempData → FeedbackModal.";
        return RedirectToPage();
    }

    public IActionResult OnPostTriggerInlineSuccess()
    {
        Message = "Server-side success via per-page inline alert (Pattern 2 — inconsistent).";
        return RedirectToPage();
    }

    public IActionResult OnPostTriggerInlineError()
    {
        Error = "Server-side error via per-page inline alert (Pattern 2 — inconsistent).";
        return RedirectToPage();
    }

    public IActionResult OnPostTriggerContextSwitch()
    {
        TempData["ContextSwitchMessage"] = "Context switched (ephemeral 3s toast).";
        return RedirectToPage();
    }
}
