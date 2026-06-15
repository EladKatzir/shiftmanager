using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.Grants;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ViewGrants policy;
// grant overview is inherently cross-company administrative data
[Authorize(Policy = "Grant:ViewGrants")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IAuditLogService _auditLogService;
    private readonly IGrantService _grantService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IAuditLogService auditLogService,
        IGrantService grantService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _auditLogService = auditLogService;
        _grantService = grantService;
    }

    public record GrantVM(
        int Id,
        string UserName,
        int UserId,
        string GrantTypeName,
        string ScopeDescription,
        bool CanOwn,
        bool CanGive,
        bool IsAutoGrant,
        DateTime GrantedAt,
        string? GrantedByName);

    public record UserGrantSummary(int UserId, string UserName, string Email, int GrantCount);
    public record GrantTypeOption(int Id, string Key, string NameKey);

    public List<GrantVM> Grants { get; set; } = new();
    public List<UserGrantSummary> UserSummaries { get; set; } = new();
    public List<GrantTypeOption> AvailableGrantTypes { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? FilterUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FilterGrantTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "users"; // "users" or "grants"

    public async Task OnGetAsync()
    {

        // Load grant types for filter
        AvailableGrantTypes = await _db.GrantTypes
            .IgnoreQueryFilters()
            .Where(gt => gt.IsActive)
            .OrderBy(gt => gt.Category).ThenBy(gt => gt.Key)
            .Select(gt => new GrantTypeOption(gt.Id, gt.Key, gt.NameKey))
            .ToListAsync();

        if (ViewMode == "grants")
        {
            // Show individual grants
            var grantsQuery = _db.Grants
                .IgnoreQueryFilters()
                .Include(g => g.User)
                .Include(g => g.GrantType)
                .Include(g => g.GrantedByUser)
                .Include(g => g.Project)
                .Include(g => g.Area)
                .Include(g => g.Molecule)
                .Include(g => g.Department)
                .Include(g => g.Company)
                .Include(g => g.JobType)
                .AsQueryable();

            if (FilterUserId.HasValue)
            {
                grantsQuery = grantsQuery.Where(g => g.UserId == FilterUserId.Value);
            }

            if (FilterGrantTypeId.HasValue)
            {
                grantsQuery = grantsQuery.Where(g => g.GrantTypeId == FilterGrantTypeId.Value);
            }

            var grantsData = await grantsQuery
                .OrderBy(g => g.User.DisplayName).ThenBy(g => g.GrantType.Key)
                .ToListAsync();

            Grants = grantsData.Select(g => new GrantVM(
                g.Id,
                g.User.DisplayName,
                g.UserId,
                g.GrantType.NameKey,
                BuildScopeDescription(g),
                g.CanOwn,
                g.CanGive,
                g.IsAutoGrant,
                g.GrantedAt,
                g.GrantedByUser?.DisplayName
            )).ToList();
        }
        else
        {
            // Show user summaries (default view)
            UserSummaries = (await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive)
                .Select(u => new UserGrantSummary(
                    u.Id,
                    u.DisplayName,
                    u.Email,
                    _db.Grants.IgnoreQueryFilters().Count(g => g.UserId == u.Id)
                ))
                .ToListAsync())
                .Where(us => us.GrantCount > 0)
                .OrderByDescending(us => us.GrantCount)
                .ThenBy(us => us.UserName)
                .ToList();
        }
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        // F1 SECURITY: the page class gate is ViewGrants (view-only, broadly held). Revoking a grant
        // requires the dedicated RevokeGrants grant. Fast gate FIRST (before any lookup) so a caller
        // without RevokeGrants at all is rejected and cannot probe grant-id existence.
        var actorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(actorIdClaim, out var actorId)
            || !await _grantService.HasGrantAsync(actorId, "RevokeGrants"))
        {
            _logger.LogWarning("Unauthorized grant-revoke attempt (no RevokeGrants): actor {ActorId} on grant {GrantId}", actorIdClaim, id);
            return Forbid();
        }

        var grant = await _db.Grants
            .IgnoreQueryFilters()
            .Include(g => g.User)
            .Include(g => g.GrantType)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (grant == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_GrantNotFound"].Value;
            return RedirectToPage(new { ViewMode = "grants" });
        }

        // Scoped check: the actor must hold RevokeGrants for the TARGET user's company (cascades to
        // molecule/area if their grant is scoped higher) — prevents cross-tenant grant revocation.
        if (!await _grantService.HasGrantForCompanyAsync(actorId, "RevokeGrants", grant.User.CompanyId))
        {
            _logger.LogWarning("Unauthorized cross-scope grant-revoke: actor {ActorId} on grant {GrantId} (owner {OwnerId}, company {CompanyId})",
                actorId, id, grant.UserId, grant.User.CompanyId);
            return Forbid();
        }

        _db.Grants.Remove(grant);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Revoked grant {GrantId} ({GrantType}) from user {UserId}",
            id, grant.GrantType.Key, grant.UserId);

        await _auditLogService.LogAsync("GrantRevoked", "Grant", id,
            $"Revoked grant '{grant.GrantType.Key}' from user '{grant.User.DisplayName}' (UserId={grant.UserId})");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_GrantRevoked"],
            _localizer[grant.GrantType.NameKey], grant.User.DisplayName);

        return RedirectToPage(new { ViewMode = "grants", FilterUserId = grant.UserId });
    }

    private static string BuildScopeDescription(Grant g)
    {
        var parts = new List<string>();

        if (g.Project != null) parts.Add($"Project: {g.Project.DisplayName}");
        if (g.Area != null) parts.Add($"Area: {g.Area.DisplayName}");
        if (g.Molecule != null) parts.Add($"Molecule: {g.Molecule.DisplayName}");
        if (g.Company != null) parts.Add($"Company: {g.Company.Name}");
        if (g.Department != null) parts.Add($"Dept: {g.Department.DisplayName}");
        if (g.JobType != null) parts.Add($"JobType: {g.JobType.DisplayName}");

        return parts.Any() ? string.Join(", ", parts) : "Global";
    }
}
