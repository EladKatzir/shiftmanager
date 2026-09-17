using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Access;

/// <summary>
/// Person-first access administration: find a person, see everything they can do and where, change it
/// in place.
///
/// The access domain was spread over pages organised by TABLE (roles here, grants there, templates in
/// two more places, statistics in a third), so answering "what can this person do?" meant visiting
/// several of them. This page answers that question directly.
///
/// It deliberately does NOT re-implement role changing — that already exists on /Admin/Users and
/// /Admin/Organization/Roles/Assign, and a third copy would be a third thing to keep in step. The
/// person's role is shown here and links there.
///
/// Every write re-uses the rules the rest of the system uses:
///   • the actor must hold the permission they are handing out (AdminAccess excepted),
///   • RoleRankGuard: never act on someone more senior than you,
///   • revoking keeps the existing RevokeGrants company-scope check.
/// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — access administration is cross-company by
/// nature (the page is gated by ViewGrants) and every write is authorised per action.
/// </summary>
[Authorize(Policy = "Grant:ViewGrants")]
public class PermissionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IAuditLogService _auditLogService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<PermissionsModel> _logger;

    public PermissionsModel(
        AppDbContext db,
        IGrantService grantService,
        IAuditLogService auditLogService,
        IStringLocalizer<SharedResources> localizer,
        ILogger<PermissionsModel> logger)
    {
        _db = db;
        _grantService = grantService;
        _auditLogService = auditLogService;
        _localizer = localizer;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? UserId { get; set; }

    public record PersonResult(int Id, string DisplayName, string Email, string CompanyName);
    public record GrantRow(int Id, string Key, string NameKey, string Category, string ScopeLabel,
                           bool CanOwn, bool CanGive, bool IsAutoGrant, string? GrantedByName, DateTime GrantedAt);
    public record Option(int Id, string Name);

    public List<PersonResult> SearchResults { get; private set; } = new();
    public AppUser? Person { get; private set; }
    public string PersonCompanyName { get; private set; } = string.Empty;
    public string PersonMoleculeName { get; private set; } = string.Empty;
    public string? PersonRoleKey { get; private set; }
    public List<GrantRow> PersonGrants { get; private set; } = new();

    public List<GrantType> AssignableGrantTypes { get; private set; } = new();
    public List<Option> Areas { get; private set; } = new();
    public List<Option> Molecules { get; private set; } = new();
    public List<Option> Companies { get; private set; } = new();
    public List<Option> Departments { get; private set; } = new();
    public List<Option> JobTypes { get; private set; } = new();

    public bool CanAssign { get; private set; }
    public bool CanRevoke { get; private set; }
    public string? Error { get; set; }
    public string? Success { get; set; }

    private int CurrentUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var actorId = CurrentUserId;
        CanAssign = await _grantService.HasGrantAsync(actorId, "AssignGrants");
        CanRevoke = await _grantService.HasGrantAsync(actorId, "RevokeGrants");

        if (!string.IsNullOrWhiteSpace(Query) && Query.Trim().Length >= 2)
        {
            var q = Query.Trim();
            var found = await _db.Users.IgnoreQueryFilters()
                .Where(u => u.IsActive && (u.DisplayName.Contains(q) || u.Email.Contains(q)))
                .OrderBy(u => u.DisplayName)
                .Take(25)
                .Select(u => new { u.Id, u.DisplayName, u.Email, u.CompanyId })
                .ToListAsync();

            var companyNames = await _db.Companies.IgnoreQueryFilters()
                .Where(c => found.Select(f => f.CompanyId).Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);

            SearchResults = found
                .Select(u => new PersonResult(u.Id, u.DisplayName, u.Email, companyNames.GetValueOrDefault(u.CompanyId, "")))
                .ToList();

            // One match is almost always the one you meant.
            if (SearchResults.Count == 1 && !UserId.HasValue)
                UserId = SearchResults[0].Id;
        }

        if (UserId.HasValue)
            await LoadPersonAsync(UserId.Value);
    }

    private async Task LoadPersonAsync(int userId)
    {
        Person = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.RoleTemplate)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (Person == null) return;

        PersonRoleKey = Person.RoleTemplate?.Key;

        var company = await _db.Companies.IgnoreQueryFilters()
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == Person.CompanyId);
        PersonCompanyName = company?.Name ?? string.Empty;
        PersonMoleculeName = company?.Molecule?.Name ?? string.Empty;

        PersonGrants = (await _db.Grants.IgnoreQueryFilters()
            .Include(g => g.GrantType)
            .Include(g => g.GrantedByUser)
            .Include(g => g.Project).Include(g => g.Area).Include(g => g.Molecule)
            .Include(g => g.Company).Include(g => g.Department).Include(g => g.JobType)
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.GrantType.Category).ThenBy(g => g.GrantType.Key)
            .ToListAsync())
            .Select(g => new GrantRow(
                g.Id, g.GrantType.Key, g.GrantType.NameKey, g.GrantType.Category.ToString(),
                DescribeScope(g), g.CanOwn, g.CanGive, g.IsAutoGrant,
                g.GrantedByUser?.DisplayName, g.GrantedAt))
            .ToList();

        AssignableGrantTypes = await _db.GrantTypes
            .Where(gt => gt.IsActive && !gt.IsDeprecated)
            .OrderBy(gt => gt.Category).ThenBy(gt => gt.Key)
            .ToListAsync();

        Areas = await _db.Areas.IgnoreQueryFilters().OrderBy(a => a.Name)
            .Select(a => new Option(a.Id, a.Name)).ToListAsync();
        Molecules = await _db.Molecules.IgnoreQueryFilters().Where(m => m.IsActive).OrderBy(m => m.Name)
            .Select(m => new Option(m.Id, m.Name)).ToListAsync();
        Companies = await _db.Companies.IgnoreQueryFilters().OrderBy(c => c.Name)
            .Select(c => new Option(c.Id, c.Name)).ToListAsync();
        Departments = await _db.Departments.IgnoreQueryFilters().OrderBy(d => d.Name)
            .Select(d => new Option(d.Id, d.Name)).ToListAsync();
        JobTypes = await _db.JobTypes.IgnoreQueryFilters().Where(j => j.IsActive).OrderBy(j => j.Name)
            .Select(j => new Option(j.Id, j.Name)).ToListAsync();
    }

    /// <summary>Human-readable "where does this permission apply".</summary>
    private string DescribeScope(Grant g)
    {
        if (g.ProjectId.HasValue) return $"{_localizer["Project"].Value}: {g.Project?.Name}";
        if (g.AreaId.HasValue) return $"{_localizer["Area"].Value}: {g.Area?.Name}";
        if (g.MoleculeId.HasValue) return $"{_localizer["Molecule"].Value}: {g.Molecule?.Name}";
        if (g.CompanyId.HasValue)
        {
            var label = $"{_localizer["Company"].Value}: {g.Company?.Name}";
            if (g.DepartmentId.HasValue) label += $" / {g.Department?.Name}";
            if (g.JobTypeId.HasValue) label += $" ({g.JobType?.Name})";
            return label;
        }
        if (g.DepartmentId.HasValue) return $"{_localizer["Department"].Value}: {g.Department?.Name}";
        if (g.JobTypeId.HasValue) return $"{_localizer["JobType"].Value}: {g.JobType?.Name}";
        return _localizer["Access_ScopeSelfOnly"].Value;
    }

    public async Task<IActionResult> OnPostAssignAsync(int userId, int grantTypeId, int? areaId,
        int? moleculeId, int? companyId, int? departmentId, int? jobTypeId, bool canGive, string? notes)
    {
        var actorId = CurrentUserId;

        if (!await _grantService.HasGrantAsync(actorId, "AssignGrants"))
            return Denied(userId, "Error_NoPermissionForCompany");

        var grantType = await _db.GrantTypes.FindAsync(grantTypeId);
        if (grantType == null)
            return Denied(userId, "Error_GrantTypeNotFound");

        // You cannot give away more than you hold (AdminAccess excepted).
        var isAdmin = await _grantService.HasGrantAsync(actorId, "AdminAccess");
        if (!isAdmin && !await _grantService.HasGrantAsync(actorId, grantType.Key))
        {
            _logger.LogWarning("SECURITY: User {ActorId} attempted to grant '{Key}' which they do not hold", actorId, grantType.Key);
            return Denied(userId, "Error_CannotGrantWhatYouDoNotHold");
        }

        if (!await RoleRankGuard.CanActOnUserAsync(_db, _grantService, actorId, userId))
            return Denied(userId, "Error_CannotActOnHigherRankedUser");

        var duplicate = await _db.Grants.IgnoreQueryFilters().AnyAsync(g =>
            g.UserId == userId && g.GrantTypeId == grantTypeId &&
            g.AreaId == areaId && g.MoleculeId == moleculeId && g.CompanyId == companyId &&
            g.DepartmentId == departmentId && g.JobTypeId == jobTypeId);
        if (duplicate)
            return Denied(userId, "Error_GrantAlreadyExists");

        _db.Grants.Add(new Grant
        {
            UserId = userId,
            GrantTypeId = grantTypeId,
            AreaId = areaId,
            MoleculeId = moleculeId,
            CompanyId = companyId,
            DepartmentId = departmentId,
            JobTypeId = jobTypeId,
            CanOwn = true,
            CanGive = canGive,
            GrantedByUserId = actorId > 0 ? actorId : null,
            GrantedAt = DateTime.UtcNow,
            Notes = notes,
            IsAutoGrant = false
        });
        await _db.SaveChangesAsync();

        await _auditLogService.LogAsync("GrantAssigned", "Grant", userId,
            $"Assigned grant '{grantType.Key}' to user {userId} via Access ▸ Permissions");

        TempData["SuccessMessage"] = _localizer["Access_GrantAdded"].Value;
        return RedirectToPage(new { userId });
    }

    public async Task<IActionResult> OnPostRevokeAsync(int grantId, int userId)
    {
        var actorId = CurrentUserId;

        var grant = await _db.Grants.IgnoreQueryFilters()
            .Include(g => g.User)
            .Include(g => g.GrantType)
            .FirstOrDefaultAsync(g => g.Id == grantId);
        if (grant == null)
            return Denied(userId, "Error_GrantNotFound");

        // Same gate the existing Grants list uses: RevokeGrants, scoped to the holder's company.
        var isAdmin = await _grantService.HasGrantAsync(actorId, "AdminAccess");
        var mayRevoke = isAdmin ||
            (await _grantService.HasGrantAsync(actorId, "RevokeGrants")
             && await _grantService.HasGrantForCompanyAsync(actorId, "RevokeGrants", grant.User.CompanyId));
        if (!mayRevoke)
            return Denied(userId, "Error_NoPermissionForCompany");

        if (!await RoleRankGuard.CanActOnUserAsync(_db, _grantService, actorId, grant.UserId))
            return Denied(userId, "Error_CannotActOnHigherRankedUser");

        _db.Grants.Remove(grant);
        await _db.SaveChangesAsync();

        await _auditLogService.LogAsync("GrantRevoked", "Grant", grantId,
            $"Revoked grant '{grant.GrantType.Key}' from user {grant.UserId} via Access ▸ Permissions");

        TempData["SuccessMessage"] = _localizer["Access_GrantRevoked"].Value;
        return RedirectToPage(new { userId });
    }

    private IActionResult Denied(int userId, string errorKey)
    {
        TempData["ErrorMessage"] = _localizer[errorKey].Value;
        return RedirectToPage(new { userId });
    }
}
