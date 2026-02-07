using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Owner Area Configuration - Configure area-specific settings like rest hours, weekly caps
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — Owner page requires Grant:AdminAccess (all 107 grants)
[Authorize(Policy = "Grant:AdminAccess")]
public class AreaConfigModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<AreaConfigModel> _logger;

    public AreaConfigModel(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<AreaConfigModel> logger)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
    }

    // View data
    public List<AreaConfigVM> Areas { get; set; } = new();

    // Feedback
    public string? Success { get; set; }
    public string? Error { get; set; }

    // Form bindings
    [BindProperty]
    public int EditAreaId { get; set; }

    [BindProperty]
    public int DefaultRestHours { get; set; } = 11;

    [BindProperty]
    public int DefaultWeeklyCap { get; set; } = 60;

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg) Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg) Error = errorMsg;

        await LoadAreasAsync();
    }

    public async Task<IActionResult> OnPostUpdateSettingsAsync()
    {
        if (EditAreaId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidArea"];
            return RedirectToPage();
        }

        // Validate inputs
        if (DefaultRestHours < 0 || DefaultRestHours > 24)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRestHours"];
            return RedirectToPage();
        }

        if (DefaultWeeklyCap < 0 || DefaultWeeklyCap > 168)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidWeeklyCap"];
            return RedirectToPage();
        }

        var area = await _db.Areas
            .Include(a => a.Settings)
            .FirstOrDefaultAsync(a => a.Id == EditAreaId);

        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"];
            return RedirectToPage();
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);

        if (area.Settings == null)
        {
            // Create new settings
            area.Settings = new AreaSettings
            {
                AreaId = EditAreaId,
                DefaultRestHours = DefaultRestHours,
                DefaultWeeklyCap = DefaultWeeklyCap,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = currentUserId > 0 ? currentUserId : null
            };
            _db.Add(area.Settings);
        }
        else
        {
            // Update existing settings
            area.Settings.DefaultRestHours = DefaultRestHours;
            area.Settings.DefaultWeeklyCap = DefaultWeeklyCap;
            area.Settings.UpdatedAt = DateTime.UtcNow;
            area.Settings.UpdatedByUserId = currentUserId > 0 ? currentUserId : null;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated area settings for Area {AreaId}: RestHours={RestHours}, WeeklyCap={WeeklyCap}",
            EditAreaId, DefaultRestHours, DefaultWeeklyCap);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_AreaSettingsUpdated"], area.DisplayName);
        return RedirectToPage();
    }

    private async Task LoadAreasAsync()
    {
        var areasWithSettings = await _db.Areas
            .IgnoreQueryFilters()
            .Include(a => a.Settings)
            .Include(a => a.Project)
            .OrderBy(a => a.Project.Name)
            .ThenBy(a => a.Name)
            .ToListAsync();

        Areas = areasWithSettings.Select(a => new AreaConfigVM
        {
            Id = a.Id,
            Name = a.DisplayName,
            ProjectName = a.Project.DisplayName,
            IsActive = a.IsActive,
            DefaultRestHours = a.Settings?.DefaultRestHours ?? 11,
            DefaultWeeklyCap = a.Settings?.DefaultWeeklyCap ?? 60,
            LastUpdated = a.Settings?.UpdatedAt
        }).ToList();
    }

    public class AreaConfigVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public bool IsActive { get; set; }
        public int DefaultRestHours { get; set; }
        public int DefaultWeeklyCap { get; set; }
        public DateTime? LastUpdated { get; set; }
    }
}
