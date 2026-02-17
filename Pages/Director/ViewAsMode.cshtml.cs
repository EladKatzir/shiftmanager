using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Director;

[Authorize(Policy = "Grant:DirectorHubAccess")]
public class ViewAsModeModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IDirectorService _directorService;
    private readonly IViewAsModeService _viewAsModeService;

    public ViewAsModeModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IDirectorService directorService,
        IViewAsModeService viewAsModeService)
        : base(localizer)
    {
        _db = db;
        _directorService = directorService;
        _viewAsModeService = viewAsModeService;
    }

    public List<Company> AssignedCompanies { get; set; } = new();
    public bool IsCurrentlyViewing { get; set; }
    public string? CurrentCompanyName { get; set; }

    public async Task OnGetAsync()
    {
        var companyIds = await _directorService.GetDirectorCompanyIdsAsync();
        AssignedCompanies = await _db.Companies
            .Where(c => companyIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();

        IsCurrentlyViewing = _viewAsModeService.IsViewingAsManager();
        if (IsCurrentlyViewing)
        {
            CurrentCompanyName = await _viewAsModeService.GetViewAsCompanyNameAsync();
        }
    }

    public async Task<IActionResult> OnPostEnterAsync(int companyId)
    {
        var success = await _viewAsModeService.EnterViewAsModeAsync(companyId);

        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_ViewAsModeEntered"].Value;
            return RedirectToPage("/Calendar/Month");
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_UnableToEnterViewAsMode"].Value;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostExitAsync()
    {
        await _viewAsModeService.ExitViewAsModeAsync();
        TempData["SuccessMessage"] = _localizer["Success_ViewAsModeExited"].Value;
        return RedirectToPage();
    }
}
