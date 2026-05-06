using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Admin.Molecules;

[Authorize]
public class ApprovalSettingsModel : LocalizedPageModel
{
    private readonly AppDbContext _db;

    public ApprovalSettingsModel(IStringLocalizer<SharedResources> localizer, AppDbContext db) : base(localizer)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public int MoleculeId { get; set; }
    [BindProperty] public int DualApprovalDayThreshold { get; set; } = 7;

    public string? MoleculeName { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var (canEdit, resolvedMoleculeId) = await ResolveAndAuthorizeAsync(MoleculeId);
        if (!canEdit) return Forbid();
        MoleculeId = resolvedMoleculeId;

        // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — page authorization in
        // ResolveAndAuthorizeAsync already enforced that the user belongs to this molecule.
        var molecule = await _db.Molecules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == MoleculeId);
        MoleculeName = molecule?.Name;

        var settings = await _db.MoleculeApprovalSettings
            .FirstOrDefaultAsync(s => s.MoleculeId == MoleculeId);
        DualApprovalDayThreshold = settings?.DualApprovalDayThreshold ?? 7;

        StatusMessage = TempData["StatusMessage"] as string;
        ErrorMessage = TempData["ErrorMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (canEdit, _) = await ResolveAndAuthorizeAsync(MoleculeId);
        if (!canEdit) return Forbid();

        var clamped = Math.Max(1, Math.Min(365, DualApprovalDayThreshold));
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
            return RedirectToPage("/Auth/Login");

        var settings = await _db.MoleculeApprovalSettings
            .FirstOrDefaultAsync(s => s.MoleculeId == MoleculeId);
        if (settings == null)
        {
            settings = new MoleculeApprovalSettings
            {
                MoleculeId = MoleculeId,
                DualApprovalDayThreshold = clamped,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = userId
            };
            _db.MoleculeApprovalSettings.Add(settings);
        }
        else
        {
            settings.DualApprovalDayThreshold = clamped;
            settings.UpdatedAt = DateTime.UtcNow;
            settings.UpdatedByUserId = userId;
        }
        await _db.SaveChangesAsync();

        TempData["StatusMessage"] = _localizer["Saved"].Value;
        return RedirectToPage(new { MoleculeId });
    }

    /// <summary>
    /// Resolves the molecule for the current user. If MoleculeId param is 0/missing,
    /// uses the user's own molecule (via Company.MoleculeId). Authorizes the user as
    /// MoleculeAdmin OR Director (any JobType) within that molecule.
    /// </summary>
    private async Task<(bool CanEdit, int MoleculeId)> ResolveAndAuthorizeAsync(int requestedMoleculeId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId)) return (false, 0);

        // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — we look up the current authenticated
        // user's own record (by their ClaimTypes.NameIdentifier id) to determine their molecule.
        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.RoleTemplate)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, 0);

        // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — we look up the user's OWN company
        // (filtered by user.CompanyId from the authenticated record) to read its MoleculeId.
        var userMoleculeId = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == user.CompanyId).Select(c => (int?)c.MoleculeId)
            .FirstOrDefaultAsync();
        if (userMoleculeId == null) return (false, 0);

        var targetMoleculeId = requestedMoleculeId == 0 ? userMoleculeId.Value : requestedMoleculeId;
        // Users can only edit their own molecule's settings — prevents cross-molecule IDOR
        if (targetMoleculeId != userMoleculeId.Value) return (false, targetMoleculeId);

        var key = user.RoleTemplate?.Key;
        var canEdit = key == "MoleculeAdmin" || key == "Director";
        return (canEdit, targetMoleculeId);
    }
}
