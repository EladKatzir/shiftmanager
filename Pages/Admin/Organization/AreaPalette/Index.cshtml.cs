using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.AreaPalette;

[Authorize(Policy = "Grant:EditAreaCalendarPalette")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAreaPaletteService _paletteService;
    private readonly IHierarchyService _hierarchyService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<IndexModel> _logger;
    private readonly IGrantService _grantService;

    public IndexModel(AppDbContext db, IAreaPaletteService paletteService,
        IHierarchyService hierarchyService, IStringLocalizer<SharedResources> localizer,
        ILogger<IndexModel> logger, IGrantService grantService)
    {
        _db = db;
        _paletteService = paletteService;
        _hierarchyService = hierarchyService;
        _localizer = localizer;
        _logger = logger;
        _grantService = grantService;
    }

    // F10c SECURITY: the class gate grants EditAreaCalendarPalette but does not bind it to a specific
    // area. Verify the actor holds the grant scoped to the TARGET area (cascades from area/project scope).
    private async Task<bool> CanEditAreaPaletteAsync(int actorId, int areaId)
        => actorId > 0 && await _grantService.HasGrantWithScopeAsync(actorId, "EditAreaCalendarPalette", areaId: areaId);

    public List<Models.Area> Areas { get; set; } = new();
    public int? SelectedAreaId { get; set; }
    public AreaCalendarPalette? CurrentPalette { get; set; }

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    [BindProperty] public int AreaId { get; set; }
    [BindProperty] public string? ShiftMorning { get; set; }
    [BindProperty] public string? ShiftAfternoon { get; set; }
    [BindProperty] public string? ShiftNight { get; set; }
    [BindProperty] public string? ShiftHome { get; set; }
    [BindProperty] public string? OnDuty { get; set; }
    [BindProperty] public string? Chore { get; set; }
    [BindProperty] public string? Vacation { get; set; }

    public async Task OnGetAsync(int? areaId = null)
    {
        Areas = await _db.Areas.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync();

        // Default: caller's area.
        if (!areaId.HasValue)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var uid))
            {
                var hierarchy = await _hierarchyService.GetUserHierarchyContextAsync(uid);
                areaId = hierarchy?.Path?.Area?.Id;
            }
        }

        SelectedAreaId = areaId;
        if (areaId.HasValue)
        {
            CurrentPalette = await _paletteService.GetPaletteForAreaAsync(areaId.Value);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (AreaId <= 0)
        {
            ErrorMessage = _localizer["AreaPalette_AreaRequired"].Value;
            await OnGetAsync();
            return Page();
        }

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        int.TryParse(userIdClaim?.Value, out var actorId);

        if (!await CanEditAreaPaletteAsync(actorId, AreaId)) return Forbid();

        // Sanitize every hex slot. Invalid values are rejected (stored as null, not the bad input).
        var values = new AreaCalendarPalette
        {
            AreaId         = AreaId,
            ShiftMorning   = ColorUtilities.SanitizeHexColor(ShiftMorning),
            ShiftAfternoon = ColorUtilities.SanitizeHexColor(ShiftAfternoon),
            ShiftNight     = ColorUtilities.SanitizeHexColor(ShiftNight),
            ShiftHome      = ColorUtilities.SanitizeHexColor(ShiftHome),
            OnDuty         = ColorUtilities.SanitizeHexColor(OnDuty),
            Chore          = ColorUtilities.SanitizeHexColor(Chore),
            Vacation       = ColorUtilities.SanitizeHexColor(Vacation),
        };

        await _paletteService.SavePaletteAsync(AreaId, values, actorId);
        _logger.LogInformation("AUDIT: AreaCalendarPalette saved for Area {AreaId} by user {ActorId}", AreaId, actorId);
        SuccessMessage = _localizer["AreaPalette_SavedMessage"].Value;
        return RedirectToPage(new { areaId = AreaId });
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        if (AreaId <= 0) return RedirectToPage();

        var resetUserClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        int.TryParse(resetUserClaim?.Value, out var resetActorId);
        if (!await CanEditAreaPaletteAsync(resetActorId, AreaId)) return Forbid();

        var existing = await _db.AreaCalendarPalettes.FirstOrDefaultAsync(p => p.AreaId == AreaId);
        if (existing != null)
        {
            _db.AreaCalendarPalettes.Remove(existing);
            await _db.SaveChangesAsync();
            _paletteService.InvalidateAreaCache(AreaId);
        }
        SuccessMessage = _localizer["AreaPalette_ResetMessage"].Value;
        return RedirectToPage(new { areaId = AreaId });
    }
}
