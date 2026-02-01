using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Entry point for Language Edit Mode.
/// Sets cookies and redirects to home page for in-app translation editing.
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class LanguageEditModeModel : PageModel
{
    private readonly IOwnerCompanySelectorService _ownerCompanySelector;
    private readonly ILogger<LanguageEditModeModel> _logger;

    public LanguageEditModeModel(
        IOwnerCompanySelectorService ownerCompanySelector,
        ILogger<LanguageEditModeModel> logger)
    {
        _ownerCompanySelector = ownerCompanySelector;
        _logger = logger;
    }

    public IActionResult OnGet(int? companyId, string? culture)
    {
        // Validate parameters
        if (!companyId.HasValue || string.IsNullOrWhiteSpace(culture))
        {
            TempData["Error"] = "Missing company ID or culture parameter.";
            return RedirectToPage("/Owner/LanguageManagement");
        }

        // Validate culture
        var allowedCultures = new[] { "en-US", "he-IL" };
        if (!allowedCultures.Contains(culture))
        {
            TempData["Error"] = $"Invalid culture '{culture}'. Must be 'en-US' or 'he-IL'.";
            return RedirectToPage("/Owner/LanguageManagement");
        }

        // Validate that Owner has selected this company (security check)
        var selectedCompanyId = _ownerCompanySelector.GetSelectedCompanyId();
        if (selectedCompanyId != companyId.Value)
        {
            _logger.LogWarning("Owner attempted to enter edit mode for CompanyId={CompanyId} but has CompanyId={SelectedCompanyId} selected",
                companyId.Value, selectedCompanyId);
            TempData["Error"] = "You can only edit translations for the currently selected company.";
            return RedirectToPage("/Owner/LanguageManagement");
        }

        // Set edit mode cookies (2-hour expiry)
        var cookieOptions = new CookieOptions
        {
            HttpOnly = false, // JavaScript needs to read these
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(2)
        };

        Response.Cookies.Append("language_edit_mode", "true", cookieOptions);
        Response.Cookies.Append("language_edit_companyId", companyId.Value.ToString(), cookieOptions);
        Response.Cookies.Append("language_edit_culture", culture, cookieOptions);

        _logger.LogInformation("Owner entered language edit mode: CompanyId={CompanyId}, Culture={Culture}",
            companyId.Value, culture);

        // Redirect to home page (where user can click on text to edit)
        return RedirectToPage("/Index");
    }
}
