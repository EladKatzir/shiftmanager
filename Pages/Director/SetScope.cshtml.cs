using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Director;

/// <summary>
/// POST-only endpoint behind the header "Viewing N of M" scope control. Sets the cross-company
/// viewing scope and returns the user to the page they were on (so scope is adjustable from anywhere,
/// not just the old /Director/CompanyFilter page).
/// </summary>
[Authorize(Policy = "Grant:DirectorHubAccess")]
public class SetScopeModel : PageModel
{
    private readonly ICompanyFilterService _filter;

    public SetScopeModel(ICompanyFilterService filter) => _filter = filter;

    [BindProperty] public List<int> CompanyIds { get; set; } = new();
    [BindProperty] public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => RedirectToPage("/Home/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await _filter.SetSelectedCompanyIdsAsync(CompanyIds);
        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            return Redirect(ReturnUrl);
        return RedirectToPage("/Home/Index");
    }
}
