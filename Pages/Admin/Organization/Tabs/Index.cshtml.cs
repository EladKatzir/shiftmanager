using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.Tabs;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageCalendarTabs;
// tab config is molecule-scoped; every mutating handler re-verifies the target molecule (and that the
// jobtype/companies/shift types are in scope) via IsUserAuthorizedForMoleculeAsync + the service guards (IDOR).
[Authorize(Policy = "Grant:ManageCalendarTabs")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IShiftTabService _tabService;
    private readonly IJobTypeService _jobTypeService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IShiftTabService tabService,
        IJobTypeService jobTypeService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _tabService = tabService;
        _jobTypeService = jobTypeService;
        _grantService = grantService;
        _companyContext = companyContext;
        _auditLogService = auditLogService;
    }

    // View models
    public record MoleculeOption(int Id, string Name);
    public record JobTypeOption(int Id, string Name);
    public record CompanyOption(int Id, string Name);
    public record ShiftTypeOption(int Id, string Name);
    public record TabVM(int Id, string NameEn, string NameHe, string? Color, bool PrioritizeCompanyUsers,
        HashSet<int> CompanyIds, HashSet<int> ShiftTypeIds, int ShiftTypeCount, int CompanyCount, int RememberedByCount);

    // Selection
    [BindProperty(SupportsGet = true)] public int? MoleculeId { get; set; }
    [BindProperty(SupportsGet = true)] public int? JobTypeId { get; set; }

    public List<MoleculeOption> AvailableMolecules { get; set; } = new();
    public List<JobTypeOption> AvailableJobTypes { get; set; } = new();
    public bool IsTechMolecule { get; set; }
    public List<CompanyOption> MoleculeCompanies { get; set; } = new();
    public List<ShiftTypeOption> ScopeShiftTypes { get; set; } = new();
    public List<TabVM> Tabs { get; set; } = new();
    public List<string> UncoveredShiftTypeNames { get; set; } = new();

    // Create/edit form
    [BindProperty] public int EditTabId { get; set; }          // 0 = create
    [BindProperty] public string NameEn { get; set; } = string.Empty;
    [BindProperty] public string NameHe { get; set; } = string.Empty;
    [BindProperty] public string? Color { get; set; }
    [BindProperty] public bool PrioritizeCompanyUsers { get; set; } = true;
    [BindProperty] public List<int> SelectedCompanyIds { get; set; } = new();
    [BindProperty] public List<int> SelectedShiftTypeIds { get; set; } = new();

    public async Task OnGetAsync() => await LoadDataAsync();

    private async Task LoadDataAsync()
    {
        if (!TryGetUserId(out var userId)) return;

        // Accessible molecules = grant scope ∪ own molecule (mirrors ChoreTypes).
        var moleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ManageCalendarTabs"));
        var ownMoleculeId = await GetOwnMoleculeIdAsync();
        if (ownMoleculeId.HasValue) moleculeIds.Add(ownMoleculeId.Value);

        // SECURITY-AUDITED: SAFE — filtered to molecules the user has ManageCalendarTabs access to.
        AvailableMolecules = await _db.Molecules.IgnoreQueryFilters()
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName))
            .ToListAsync();

        // Auto-select the sole accessible molecule, or default to own; validate the current pick.
        if (!MoleculeId.HasValue)
            MoleculeId = AvailableMolecules.Count == 1 ? AvailableMolecules[0].Id : ownMoleculeId;
        if (MoleculeId.HasValue && !AvailableMolecules.Any(m => m.Id == MoleculeId.Value))
            MoleculeId = null;
        if (!MoleculeId.HasValue) return;

        var molecule = await _db.Molecules.IgnoreQueryFilters().FirstAsync(m => m.Id == MoleculeId.Value);
        IsTechMolecule = molecule.Type == MoleculeType.Tech;

        // Job types for the molecule (Tech → none; JobTypeId stays null).
        if (!IsTechMolecule)
        {
            AvailableJobTypes = (await _jobTypeService.GetJobTypesForMoleculeAsync(MoleculeId.Value))
                .Select(jt => new JobTypeOption(jt.Id, string.IsNullOrWhiteSpace(jt.DisplayName) ? jt.Name : jt.DisplayName))
                .ToList();
            if (!JobTypeId.HasValue)
                JobTypeId = AvailableJobTypes.Count == 1 ? AvailableJobTypes[0].Id : (int?)null;
            if (JobTypeId.HasValue && !AvailableJobTypes.Any(jt => jt.Id == JobTypeId.Value))
                JobTypeId = null;
            if (!JobTypeId.HasValue) return; // wait for the user to pick a job type
        }
        else
        {
            JobTypeId = null;
        }

        // Companies of the molecule (checklist source). Materialize before projecting LocalizedName —
        // it's a computed property (static-method fallback chain) EF Core cannot translate to SQL.
        var moleculeCompanies = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == MoleculeId.Value)
            .OrderBy(c => c.Name)
            .ToListAsync();
        MoleculeCompanies = moleculeCompanies.Select(c => new CompanyOption(c.Id, c.LocalizedName)).ToList();

        // Shift types in the molecule + jobtype scope (jobtype match or null) — checklist source + coverage.
        var scopeShiftTypes = await _db.ShiftTypes.IgnoreQueryFilters()
            .Where(st => st.MoleculeId == MoleculeId.Value && (st.JobTypeId == JobTypeId || st.JobTypeId == null))
            .OrderBy(st => st.Start)
            .ToListAsync();
        ScopeShiftTypes = scopeShiftTypes
            .Select(st => new ShiftTypeOption(st.Id, string.IsNullOrWhiteSpace(st.NameEn) ? st.Key : st.NameEn!))
            .ToList();

        // Tabs for (molecule, jobtype) with their membership + usage + remembered-by counts.
        var tabs = await _tabService.GetTabsForMoleculeAsync(MoleculeId.Value, JobTypeId, includeInactive: true);
        var tabVms = new List<TabVM>(tabs.Count);
        var coverageSets = new List<HashSet<int>>();
        foreach (var t in tabs)
        {
            var companyIds = await _tabService.GetCompanyIdsForTabAsync(t.Id);
            var shiftTypeIds = await _tabService.GetShiftTypeIdsForTabAsync(t.Id);
            var (stCount, coCount) = await _tabService.GetUsageAsync(t.Id);
            var remembered = await _db.UserShiftTabPreferences.CountAsync(p => p.TabId == t.Id);
            tabVms.Add(new TabVM(t.Id, t.NameEn, t.NameHe, t.Color, t.PrioritizeCompanyUsers,
                companyIds, shiftTypeIds, stCount, coCount, remembered));
            coverageSets.Add(shiftTypeIds);
        }
        Tabs = tabVms;

        // Advisory coverage warning: shift types in no tab (empty tab = all → nothing uncovered).
        var uncovered = ComputeUncovered(scopeShiftTypes.Select(st => st.Id), coverageSets);
        UncoveredShiftTypeNames = ScopeShiftTypes.Where(o => uncovered.Contains(o.Id)).Select(o => o.Name).ToList();
    }

    /// <summary>Shift-type ids covered by NO tab. An empty tab means "all" → coverage complete.</summary>
    public static HashSet<int> ComputeUncovered(IEnumerable<int> allShiftTypeIds, List<HashSet<int>> tabShiftTypeSets)
    {
        var all = allShiftTypeIds.ToHashSet();
        if (tabShiftTypeSets.Any(s => s.Count == 0)) return new HashSet<int>();  // some tab = "all"
        var covered = new HashSet<int>();
        foreach (var s in tabShiftTypeSets) covered.UnionWith(s);
        all.ExceptWith(covered);
        return all;
    }

    // ─────────────────────────── CRUD handlers ───────────────────────────

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        if (!MoleculeId.HasValue) { TempError("Tabs_Error_MoleculeRequired"); return Redirect(); }
        if (!await IsUserAuthorizedForMoleculeAsync(MoleculeId.Value)) { TempError("Tabs_Error_NotAuthorized"); return Redirect(); }
        if (!await IsJobTypeInScopeAsync(MoleculeId.Value, JobTypeId)) { TempError("Tabs_Error_InvalidSelection"); return Redirect(); }
        if (string.IsNullOrWhiteSpace(NameEn) || string.IsNullOrWhiteSpace(NameHe)) { TempError("Tabs_Error_NamesRequired"); return Redirect(); }

        // Atomic: name+priority+companies+shift types are one logical create. A failure at any step (name clash,
        // out-of-scope company/shift type) must leave NO tab behind — the transaction rolls back the insert and
        // every membership write together, so there is never a half-made tab (replaces compensating-delete).
        await using var tx = await _db.Database.BeginTransactionAsync();

        var created = await _tabService.CreateAsync(MoleculeId.Value, JobTypeId, NameEn.Trim(), NameHe.Trim(),
            ColorUtilities.SanitizeHexColor(Color), userId);
        if (created == null) { TempError("Tabs_Error_NameExists"); return Redirect(); }

        // Priority default is ON at create; apply the submitted membership (IDOR-guarded in the service).
        if (!created.PrioritizeCompanyUsers.Equals(PrioritizeCompanyUsers))
            await _tabService.RenameAsync(created.Id, created.NameEn, created.NameHe, created.Color, PrioritizeCompanyUsers);
        if (!await _tabService.SetCompaniesForTabAsync(created.Id, SelectedCompanyIds)
            || !await _tabService.SetShiftTypesForTabAsync(created.Id, SelectedShiftTypeIds))
        {
            TempError("Tabs_Error_InvalidSelection");
            return Redirect(); // tx disposed uncommitted → rolls back the insert + any company writes
        }

        await tx.CommitAsync();
        await _auditLogService.LogAsync("ShiftTabCreated", "ShiftTab", created.Id,
            $"Created tab '{created.NameEn}' in molecule {MoleculeId.Value}, jobtype {JobTypeId?.ToString() ?? "none"}.");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Tabs_Success_Created"], created.NameEn);
        return Redirect();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        var tab = await _tabService.GetTabAsync(EditTabId);
        if (tab == null || !await IsUserAuthorizedForMoleculeAsync(tab.MoleculeId)) { TempError("Tabs_Error_NotAuthorized"); return Redirect(); }
        if (string.IsNullOrWhiteSpace(NameEn) || string.IsNullOrWhiteSpace(NameHe)) { TempError("Tabs_Error_NamesRequired"); return Redirect(); }

        // Atomic: rename+priority+companies+shift types are one logical update. Without this, a late failure
        // (e.g. a shift type re-scoped out of (molecule,jobtype) between form load and submit) would persist the
        // rename+company changes yet reject the shift-type change, leaving a visible half-updated tab.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var renamed = await _tabService.RenameAsync(tab.Id, NameEn.Trim(), NameHe.Trim(),
            ColorUtilities.SanitizeHexColor(Color), PrioritizeCompanyUsers);
        if (!renamed) { TempError("Tabs_Error_NameExists"); return Redirect(); }

        if (!await _tabService.SetCompaniesForTabAsync(tab.Id, SelectedCompanyIds)
            || !await _tabService.SetShiftTypesForTabAsync(tab.Id, SelectedShiftTypeIds))
        { TempError("Tabs_Error_InvalidSelection"); return Redirect(); } // tx disposed uncommitted → full rollback

        await tx.CommitAsync();
        await _auditLogService.LogAsync("ShiftTabUpdated", "ShiftTab", tab.Id, $"Updated tab '{NameEn.Trim()}'.");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Tabs_Success_Updated"], NameEn.Trim());
        return Redirect();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int tabId)
    {
        if (!TryGetUserId(out _)) return Forbid();
        var tab = await _tabService.GetTabAsync(tabId);
        if (tab == null || !await IsUserAuthorizedForMoleculeAsync(tab.MoleculeId)) { TempError("Tabs_Error_NotAuthorized"); return Redirect(); }

        var nameEn = tab.NameEn;
        await _tabService.DeleteAsync(tabId);
        await _auditLogService.LogAsync("ShiftTabDeleted", "ShiftTab", tabId, $"Deleted tab '{nameEn}'.");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Tabs_Success_Deleted"], nameEn);
        return Redirect();
    }

    /// <summary>AJAX delete-impact preview (mirrors Blueprints' OnGetCheckTabUsage pattern).</summary>
    public async Task<IActionResult> OnGetCheckTabUsageAsync(int tabId)
    {
        if (!TryGetUserId(out _)) return new JsonResult(new { ok = false }) { StatusCode = 403 };
        var tab = await _tabService.GetTabAsync(tabId);
        if (tab == null || !await IsUserAuthorizedForMoleculeAsync(tab.MoleculeId))
            return new JsonResult(new { ok = false }) { StatusCode = 403 };
        var (shiftTypeCount, companyCount) = await _tabService.GetUsageAsync(tabId);
        var rememberedBy = await _db.UserShiftTabPreferences.CountAsync(p => p.TabId == tabId);
        return new JsonResult(new
        {
            ok = true,
            message = string.Format(CultureInfo.CurrentCulture, _localizer["Tabs_DeleteConfirm"],
                tab.NameEn, shiftTypeCount, companyCount, rememberedBy)
        });
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private bool TryGetUserId(out int userId)
        => int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out userId);

    private async Task<int?> GetOwnMoleculeIdAsync()
    {
        var companyId = _companyContext.CompanyId;
        if (!companyId.HasValue) return null;
        return await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == companyId.Value).Select(c => c.MoleculeId).FirstOrDefaultAsync();
    }

    /// <summary>The current user has ManageCalendarTabs grant access to the molecule (∪ own molecule).</summary>
    private async Task<bool> IsUserAuthorizedForMoleculeAsync(int moleculeId)
    {
        if (!TryGetUserId(out var userId)) return false;
        var accessible = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ManageCalendarTabs"));
        var own = await GetOwnMoleculeIdAsync();
        if (own.HasValue) accessible.Add(own.Value);
        return accessible.Contains(moleculeId);
    }

    /// <summary>The jobtype is valid for the molecule: null ONLY for a Tech molecule, else it must own shift
    /// types in the molecule. A null-jobtype tab on a workforce molecule would be invisible in the strip
    /// (GetTabsForMoleculeAsync filters by the real jobtype) — an unreachable orphan — so reject it (IDOR/POST).</summary>
    private async Task<bool> IsJobTypeInScopeAsync(int moleculeId, int? jobTypeId)
    {
        if (jobTypeId == null)
        {
            var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == moleculeId);
            return molecule?.Type == MoleculeType.Tech;
        }
        return await _db.ShiftTypes.IgnoreQueryFilters()
            .AnyAsync(st => st.MoleculeId == moleculeId && st.JobTypeId == jobTypeId.Value);
    }

    private void TempError(string key) => TempData["ErrorMessage"] = _localizer[key].Value;
    private IActionResult Redirect() => RedirectToPage(new { MoleculeId, JobTypeId });
}
