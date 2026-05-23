using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// AJAX-only endpoint backing the distribution-list manager modal embedded in the Shifts calendar.
/// Not navigated to directly — all handlers return JSON. Authenticated; mutation handlers are gated by the
/// ManageDistributionLists grant (re-checked inside the service against the LIST's stored molecule — IDOR guard).
/// Reads (molecule users / list detail) require the manage grant too, since they only feed the manage modal.
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — molecule-scoped user lookup (cross-company within the
// molecule), gated by an explicit ManageDistributionLists grant check before any data is returned.
[Authorize]
public class ManageDistributionListsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IDistributionListService _distributionListService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<ManageDistributionListsModel> _logger;

    public ManageDistributionListsModel(
        AppDbContext db,
        IGrantService grantService,
        IDistributionListService distributionListService,
        IStringLocalizer<SharedResources> localizer,
        ILogger<ManageDistributionListsModel> logger)
    {
        _db = db;
        _grantService = grantService;
        _distributionListService = distributionListService;
        _localizer = localizer;
        _logger = logger;
    }

    private int CurrentUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    private Task<bool> CanManageAsync(int moleculeId) =>
        _grantService.HasGrantWithScopeAsync(CurrentUserId, "ManageDistributionLists", moleculeId: moleculeId);

    /// <summary>GET: active users in the molecule (cross-company) for the member picker. Managers only.</summary>
    public async Task<IActionResult> OnGetMoleculeUsersAsync(int moleculeId, string? search)
    {
        if (!await CanManageAsync(moleculeId))
            return new JsonResult(new { ok = false }) { StatusCode = 403 };

        // SECURITY-AUDITED: SAFE — scoped to companies within the requested molecule; gated above.
        var query = _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive
                && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.DisplayName.ToLower().Contains(s)
                                  || (u.Email != null && u.Email.ToLower().Contains(s)));
        }

        var users = await query
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.CompanyId, u.AvatarFileName })
            .Take(500)
            .ToListAsync();

        // Company names for display (molecule-scoped).
        var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
        var companyNames = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.LocalizedName);

        var result = users.Select(u => new
        {
            id = u.Id,
            displayName = u.DisplayName,
            companyName = companyNames.GetValueOrDefault(u.CompanyId, ""),
            // Cross-company avatar: use the user's OWN CompanyId path, never the caller's tenant.
            avatarUrl = string.IsNullOrWhiteSpace(u.AvatarFileName) ? "" : $"/avatars/{u.CompanyId}/{u.Id}_thumb.jpg"
        });

        return new JsonResult(new { ok = true, users = result });
    }

    /// <summary>GET: one list's detail (name + member IDs) for the edit modal. Managers only.</summary>
    public async Task<IActionResult> OnGetListAsync(int listId, int moleculeId)
    {
        if (!await CanManageAsync(moleculeId))
            return new JsonResult(new { ok = false }) { StatusCode = 403 };

        var detail = await _distributionListService.GetListDetailAsync(listId, moleculeId);
        if (detail == null)
            return new JsonResult(new { ok = false }) { StatusCode = 404 };

        return new JsonResult(new { ok = true, id = detail.Id, name = detail.Name, memberUserIds = detail.MemberUserIds });
    }

    public class CreateListRequest
    {
        public int MoleculeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<int> UserIds { get; set; } = new();
    }

    public class UpdateListRequest
    {
        public int ListId { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<int> UserIds { get; set; } = new();
    }

    public class DeleteListRequest
    {
        public int ListId { get; set; }
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] CreateListRequest req)
    {
        var result = await _distributionListService.CreateAsync(CurrentUserId, req.MoleculeId, req.Name, req.UserIds);
        return MapResult(result, _localizer["DistributionList_Created"]);
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] UpdateListRequest req)
    {
        var result = await _distributionListService.UpdateAsync(CurrentUserId, req.ListId, req.Name, req.UserIds);
        return MapResult(result, _localizer["DistributionList_Updated"]);
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeleteListRequest req)
    {
        var result = await _distributionListService.DeleteAsync(CurrentUserId, req.ListId);
        return MapResult(result, _localizer["DistributionList_Deleted"]);
    }

    /// <summary>Maps a service outcome to a JSON response with the right HTTP status + localized message.</summary>
    private IActionResult MapResult(DistributionListResult result, string successMessage)
    {
        return result.Outcome switch
        {
            DistributionListOutcome.Ok =>
                new JsonResult(new { ok = true, listId = result.ListId, message = successMessage }),
            DistributionListOutcome.Forbidden =>
                new JsonResult(new { ok = false }) { StatusCode = 403 },
            DistributionListOutcome.NotFound =>
                new JsonResult(new { ok = false }) { StatusCode = 404 },
            DistributionListOutcome.NameRequired =>
                new JsonResult(new { ok = false, error = _localizer["DistributionList_NameRequired"].Value }) { StatusCode = 400 },
            DistributionListOutcome.NameTaken =>
                new JsonResult(new { ok = false, error = _localizer["DistributionList_NameTaken"].Value }) { StatusCode = 409 },
            DistributionListOutcome.NoMembers =>
                new JsonResult(new { ok = false, error = _localizer["DistributionList_SelectAtLeastOne"].Value }) { StatusCode = 400 },
            DistributionListOutcome.InvalidMembers =>
                new JsonResult(new { ok = false, error = _localizer["DistributionList_SaveFailed"].Value }) { StatusCode = 400 },
            _ =>
                new JsonResult(new { ok = false, error = _localizer["DistributionList_SaveFailed"].Value }) { StatusCode = 500 }
        };
    }
}
