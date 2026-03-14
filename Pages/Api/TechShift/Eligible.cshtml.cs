using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages.Api.TechShift;

/// <summary>
/// DEPRECATED: TechShift eligibility is now driven by ShiftType.EligibleCompanyIds
/// and ShiftType.RequiresOfficerRank fields. This endpoint returns an empty result
/// with a deprecation flag.
/// </summary>
[Authorize(Policy = "Grant:ManageShifts")]
[IgnoreAntiforgeryToken]
public class EligibleModel : PageModel
{
    private readonly ILogger<EligibleModel> _logger;

    public EligibleModel(ILogger<EligibleModel> logger)
    {
        _logger = logger;
    }

    public IActionResult OnGet(string? type, int? companyId)
    {
        _logger.LogWarning("TechShift/Eligible API is deprecated. Use Calendar shift-type-based eligibility.");
        return new JsonResult(new { users = new List<object>(), deprecated = true });
    }
}
