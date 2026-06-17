using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;

namespace ShiftManager.ViewComponents;

/// <summary>
/// The "Viewing N of M companies" header control (design-bar redesign / P3 company-context primitive).
/// Promotes the cross-company scope from a buried /Director/CompanyFilter page to a persistent header
/// dropdown. Self-hides for anyone who doesn't oversee more than one company (so single-company users
/// and non-directors never see it). This is the PLURAL "scope" axis — distinct from the SINGULAR
/// active-company ContextSwitcher.
/// </summary>
public class ViewingScopeViewComponent : ViewComponent
{
    private readonly IDirectorService _director;
    private readonly ICompanyFilterService _filter;
    private readonly AppDbContext _db;

    public ViewingScopeViewComponent(IDirectorService director, ICompanyFilterService filter, AppDbContext db)
    {
        _director = director;
        _filter = filter;
        _db = db;
    }

    public record ScopeCompany(int Id, string Name);
    public record ViewingScopeVm(IReadOnlyList<ScopeCompany> Companies, HashSet<int> Selected, string ReturnUrl);

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var companyIds = await _director.GetDirectorCompanyIdsAsync();
        if (companyIds.Count <= 1) return Content(string.Empty); // not a multi-company overseer → no control

        // SECURITY-AUDITED: SAFE — only the caller's own director-assigned companies (companyIds) are read.
        var companies = await _db.Companies.IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new ScopeCompany(c.Id, c.Name))
            .ToListAsync();

        var selected = (await _filter.GetSelectedCompanyIdsAsync()).ToHashSet();
        var returnUrl = HttpContext.Request.Path + HttpContext.Request.QueryString;
        return View(new ViewingScopeVm(companies, selected, returnUrl));
    }
}
