using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api;

/// <summary>
/// Page handler for a multi-company member to select which company is their active tenant.
/// Membership is validated inside <see cref="IActiveCompanySelectorService.SelectCompanyAsync"/>
/// (async DB check) — no extra authorization beyond <see cref="AuthorizeAttribute"/> is needed here.
/// Mirrors <see cref="SelectMoleculeModel"/> structure: POST-only, anti-forgery (Razor Pages default),
/// returnUrl validated via <see cref="IUrlHelper.IsLocalUrl"/>.
/// </summary>
[Authorize] // any authenticated user; membership is enforced inside the selector service
public class SelectMemberCompanyModel : PageModel
{
    private readonly IActiveCompanySelectorService _selector;

    public SelectMemberCompanyModel(IActiveCompanySelectorService selector) => _selector = selector;

    /// <summary>
    /// GET removed — company selection is POST-only to prevent CSRF via crafted link.
    /// Mirrors SelectMolecule.OnGet() redirect behavior.
    /// </summary>
    public IActionResult OnGet() => RedirectToPage("/Index");

    /// <summary>
    /// POST handler — validates membership and writes the active-company cookie, then redirects.
    /// Anti-forgery token is validated automatically by Razor Pages.
    /// </summary>
    public async Task<IActionResult> OnPostAsync(int companyId, string? returnUrl = null)
    {
        // SelectCompanyAsync does the authoritative IsMemberAsync gate;
        // the cookie is only written when membership is confirmed.
        await _selector.SelectCompanyAsync(companyId);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Index");
    }
}
