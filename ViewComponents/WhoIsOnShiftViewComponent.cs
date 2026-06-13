using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

/// <summary>
/// "Who is on Shift" home dashboard widget for Area Admins (מפק"מ).
/// Self-gates: renders empty unless the user is an Area Admin (EditArea), a CO/מפק"מ = MoleculeAdmin
/// (EditMolecule), or an Owner (AdminAccess).
/// </summary>
public class WhoIsOnShiftViewComponent : ViewComponent
{
    private readonly IWhoIsOnShiftService _whoIsOnShiftService;
    private readonly IGrantService _grantService;

    public WhoIsOnShiftViewComponent(IWhoIsOnShiftService whoIsOnShiftService, IGrantService grantService)
    {
        _whoIsOnShiftService = whoIsOnShiftService;
        _grantService = grantService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userIdClaim = UserClaimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
            return Content(string.Empty);

        // Gate: Area Admins (EditArea), COs / מפק"מ = MoleculeAdmin (EditMolecule), or Owners (AdminAccess).
        var hasEditArea = await _grantService.HasGrantAsync(userId, "EditArea");
        var hasEditMolecule = await _grantService.HasGrantAsync(userId, "EditMolecule");
        var hasAdminAccess = await _grantService.HasGrantAsync(userId, "AdminAccess");
        if (!hasEditArea && !hasEditMolecule && !hasAdminAccess)
            return Content(string.Empty);

        var cubes = await _whoIsOnShiftService.BuildWhoIsOnShiftAsync(userId);
        var monitorable = await _whoIsOnShiftService.GetMonitorableShiftTypesAsync(userId);
        var selectedIds = await _whoIsOnShiftService.GetSelectedShiftIdsAsync(userId);

        var vm = new WhoIsOnShiftViewModel
        {
            Cubes = cubes,
            Monitorable = monitorable,
            SelectedIds = selectedIds.ToHashSet()
        };

        return View(vm);
    }
}

/// <summary>View model for the WhoIsOnShift widget.</summary>
public class WhoIsOnShiftViewModel
{
    public List<ShiftCube> Cubes { get; set; } = new();
    public List<MonitorableShiftType> Monitorable { get; set; } = new();
    public HashSet<int> SelectedIds { get; set; } = new();
}
