using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.ViewComponents;

/// <summary>
/// ViewComponent for Owner role to select which company to manage.
/// Shows dropdown of all companies with current selection.
/// </summary>
public class OwnerCompanySelectorViewComponent : ViewComponent
{
    private readonly IOwnerCompanySelectorService _ownerCompanySelector;
    private readonly AppDbContext _db;

    public OwnerCompanySelectorViewComponent(
        IOwnerCompanySelectorService ownerCompanySelector,
        AppDbContext db)
    {
        _ownerCompanySelector = ownerCompanySelector;
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        // Only show for Owner role
        if (!_ownerCompanySelector.IsOwner())
        {
            return Content(string.Empty);
        }

        var model = new OwnerCompanySelectorViewModel
        {
            Companies = (await _db.Companies
                .OrderBy(c => c.Name)
                .ToListAsync())
                .Select(c => new CompanyOption { Id = c.Id, Name = c.LocalizedName })
                .ToList(),
            SelectedCompanyId = _ownerCompanySelector.GetSelectedCompanyId(),
            HomeCompanyId = _ownerCompanySelector.GetHomeCompanyId()
        };

        return View(model);
    }
}

/// <summary>
/// ViewModel for Owner company selector dropdown.
/// </summary>
public class OwnerCompanySelectorViewModel
{
    public List<CompanyOption> Companies { get; set; } = new();
    public int? SelectedCompanyId { get; set; }
    public int? HomeCompanyId { get; set; }
}

/// <summary>
/// Simplified company data for dropdown options.
/// </summary>
public class CompanyOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
