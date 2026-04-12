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

        var companies = (await _db.Companies
            .Include(c => c.Molecule)
            .ToListAsync())
            .OrderBy(c => c.LocalizedName, StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true))
            .ToList();

        // Disambiguate duplicate LocalizedNames by appending the molecule name.
        // e.g. multiple "כלל צוותי" become "כלל צוותי - אורן", "כלל צוותי - גפן"
        var nameGroups = companies.GroupBy(c => c.LocalizedName);
        var disambiguatedOptions = new List<CompanyOption>();
        foreach (var group in nameGroups)
        {
            if (group.Count() > 1)
            {
                // Duplicate names exist — append molecule name for disambiguation
                foreach (var c in group)
                {
                    var moleculeName = c.Molecule?.DisplayName ?? c.Molecule?.Name;
                    var displayName = !string.IsNullOrEmpty(moleculeName)
                        ? $"{c.LocalizedName} - {moleculeName}"
                        : c.LocalizedName;
                    disambiguatedOptions.Add(new CompanyOption { Id = c.Id, Name = displayName });
                }
            }
            else
            {
                var c = group.Single();
                disambiguatedOptions.Add(new CompanyOption { Id = c.Id, Name = c.LocalizedName });
            }
        }

        var model = new OwnerCompanySelectorViewModel
        {
            Companies = disambiguatedOptions,
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
