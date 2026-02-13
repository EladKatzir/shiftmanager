using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Director;

/// <summary>
/// Director Landing Page - Central hub for cross-company management.
/// ✅ P2-2: Provides company selection and quick access to management tools.
/// </summary>
[Authorize(Policy = "IsDirector")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IDirectorService _directorService;
    private readonly ICompanyFilterService? _filterService;

    public IndexModel(
        AppDbContext db,
        IDirectorService directorService,
        ICompanyFilterService? filterService = null)
    {
        _db = db;
        _directorService = directorService;
        _filterService = filterService;
    }

    public List<Company> AssignedCompanies { get; set; } = new();
    public List<int> SelectedCompanyIds { get; set; } = new();
    public bool HasFilter => SelectedCompanyIds.Any();

    // Stats across assigned companies
    public int TotalUsers { get; set; }
    public int TotalShifts { get; set; }
    public int TotalRequests { get; set; }

    public async Task OnGetAsync()
    {
        // Load assigned companies
        var companyIds = await _directorService.GetDirectorCompanyIdsAsync();
        AssignedCompanies = await _db.Companies
            .Where(c => companyIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Load selected filter
        if (_filterService != null)
        {
            SelectedCompanyIds = await _filterService.GetSelectedCompanyIdsAsync();
        }

        // Calculate stats across assigned companies (filtered if filter is active)
        var targetCompanyIds = HasFilter ? SelectedCompanyIds : companyIds;

        try
        {
            // IgnoreQueryFilters: Director manages multiple companies but tenant filter
            // restricts to their own CompanyId — explicit targetCompanyIds handles scoping
            TotalUsers = await _db.Users.IgnoreQueryFilters()
                .CountAsync(u => u.IsActive && targetCompanyIds.Contains(u.CompanyId));
            TotalShifts = await _db.ShiftInstances.IgnoreQueryFilters()
                .CountAsync(si => targetCompanyIds.Contains(si.CompanyId));
            TotalRequests = await _db.TimeOffRequests.IgnoreQueryFilters()
                .CountAsync(r => r.Status == RequestStatus.Pending && targetCompanyIds.Contains(r.CompanyId));
        }
        catch
        {
            TotalUsers = 0;
            TotalShifts = 0;
            TotalRequests = 0;
        }
    }
}
