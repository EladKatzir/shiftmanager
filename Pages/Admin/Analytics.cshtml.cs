using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin;

/// <summary>
/// Justice — workload distribution analytics page (rewritten 2026-05-03).
///
/// Replaces the legacy retrospective dashboard that mixed HR process metrics with assignment
/// decision support. Now focused exclusively on the latter: who is over/under-loaded across
/// users, companies, and molecules; and what targets we expect each scope to deliver.
///
/// Phase 1 ships read-only views (Justice Table + verdict strip + equity ribbon + CSV export).
/// Phase 2 adds the What-If simulator, settings modal, and decision-support widgets.
/// </summary>
[Authorize(Policy = "Grant:ViewJusticeTable")]
public class AnalyticsModel : LocalizedPageModel
{
    private readonly IJusticeService _justiceService;
    private readonly IGrantService _grantService;
    private readonly AppDbContext _db;
    private readonly ILogger<AnalyticsModel> _logger;

    public AnalyticsModel(
        IStringLocalizer<SharedResources> localizer,
        IJusticeService justiceService,
        IGrantService grantService,
        AppDbContext db,
        ILogger<AnalyticsModel> logger)
        : base(localizer)
    {
        _justiceService = justiceService;
        _grantService = grantService;
        _db = db;
        _logger = logger;
    }

    // ----- query parameters (bound from query string) ------------------------------------

    [BindProperty(SupportsGet = true, Name = "scope")] public JusticeScope Scope { get; set; } = JusticeScope.Molecule;
    [BindProperty(SupportsGet = true, Name = "scopeId")] public int? ScopeId { get; set; }
    [BindProperty(SupportsGet = true, Name = "level")] public JusticeLevel Level { get; set; } = JusticeLevel.CompaniesInMolecule;
    [BindProperty(SupportsGet = true, Name = "workType")] public JusticeWorkType WorkType { get; set; } = JusticeWorkType.All;
    [BindProperty(SupportsGet = true, Name = "excludeExempt")] public bool ExcludeExemptShifts { get; set; } = true;
    [BindProperty(SupportsGet = true, Name = "from")] public DateOnly? PeriodStart { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public DateOnly? PeriodEnd { get; set; }

    // ----- new query parameters (B3a) -------------------------------------------------------

    /// <summary>
    /// Fairness basis: BySize (capacity/target-weighted, default) or EqualShare (total ÷ N).
    /// Carried through to <see cref="JusticeQuery.Basis"/>.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "basis")] public FairnessBasis Basis { get; set; } = FairnessBasis.BySize;

    /// <summary>
    /// Optional ShiftCategory filter. When set, only users who belong to this category
    /// (via UserShiftCategory) are shown, and only shifts of that category are counted.
    /// Only meaningful for WorkType=Shift or All at user levels.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "shiftCategoryId")] public int? ShiftCategoryId { get; set; }

    /// <summary>
    /// A/B comparison: start of the compare period (period B). When both CompareFrom and
    /// CompareTo are provided the service computes per-row deltas between the primary and compare periods.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "cmpFrom")] public DateOnly? CompareFrom { get; set; }

    /// <summary>
    /// A/B comparison: end of the compare period (period B).
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "cmpTo")] public DateOnly? CompareTo { get; set; }

    // ----- view model surface --------------------------------------------------------------

    public JusticeViewModel? View { get; private set; }

    /// <summary>
    /// Populated when both <see cref="CompareFrom"/> and <see cref="CompareTo"/> are provided
    /// and form a valid range. When set, <see cref="View"/> is reassigned to
    /// <see cref="JusticeComparisonViewModel.Primary"/> so row-level <c>DeltaVsCompare</c>
    /// values are available to the view.
    /// </summary>
    public JusticeComparisonViewModel? Comparison { get; private set; }

    /// <summary>
    /// Companies the current user can pick as the scope target. Populated for all levels but
    /// only meaningful for UsersInCompany.
    /// </summary>
    public List<ScopeOption> CompanyOptions { get; private set; } = new();

    /// <summary>
    /// Molecules the current user can pick. Filled for Molecule and Area levels.
    /// </summary>
    public List<ScopeOption> MoleculeOptions { get; private set; } = new();

    /// <summary>
    /// Areas the current user can pick (drilled from molecules they can access).
    /// </summary>
    public List<ScopeOption> AreaOptions { get; private set; } = new();

    public DateOnly EffectivePeriodStart { get; private set; }
    public DateOnly EffectivePeriodEnd { get; private set; }

    /// <summary>
    /// Shift categories available for filtering. Populated when a molecule scope is resolved
    /// (either directly for UsersInMolecule, or via company → molecule for UsersInCompany).
    /// Empty when no molecule can be determined (e.g. CompaniesInMolecule, MoleculesInArea levels).
    /// </summary>
    public List<ScopeOption> CategoryOptions { get; private set; } = new();

    public sealed record ScopeOption(int Id, string Name);

    // ----- handlers ------------------------------------------------------------------------

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Forbid();
        }

        await PopulateScopeOptionsAsync(userId, ct);

        // Period defaults: current calendar month from the 1st to today.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        EffectivePeriodStart = PeriodStart ?? new DateOnly(today.Year, today.Month, 1);
        EffectivePeriodEnd = PeriodEnd ?? today;
        if (EffectivePeriodEnd < EffectivePeriodStart)
        {
            EffectivePeriodEnd = EffectivePeriodStart;
        }

        // Normalize Scope <-> Level coupling. The page picker pairs each Level with one Scope kind;
        // if the user landed without parameters or with a mismatched pair, snap to a sensible default.
        NormalizeScopeAndLevelDefaults();

        if (ScopeId is null)
        {
            View = null;
            return Page();
        }

        // Authorization (READ / comparison tier): confirm the requested ScopeId is within what the
        // user can SEE. A capped user may read a scope to rank its sibling children even when they
        // can only drill into their own subtree.
        if (!await UserCanAccessScopeAsync(userId, Scope, ScopeId.Value, ct))
        {
            _logger.LogWarning("Justice access denied for user {UserId} on scope {Scope} id {ScopeId}", userId, Scope, ScopeId);
            return Forbid();
        }

        // Authorization (DRILL): a request that navigates INTO a scope must target the user's own
        // subtree. This is the security core of A5 — a sibling scope is visible as a comparison row
        // but drilling into it (?scopeId=<sibling>) must be rejected even though it is readable.
        // The read check above can be (and is, for Area top-level) deliberately broader; this check
        // is the strict gate that prevents cross-scope drill-through.
        if (!await CanDrillScopeAsync(userId, Scope, ScopeId.Value, ct))
        {
            _logger.LogWarning("Justice drill denied for user {UserId} on scope {Scope} id {ScopeId} (readable-not-drillable)", userId, Scope, ScopeId);
            return Forbid();
        }

        // Compute which child rows the user may drill into at the CURRENT level. Sibling rows the
        // user can only read (not drill) are still returned for comparison, but flagged
        // IsDrillable=false so the front-end disables their drill affordance.
        var drillableChildIds = await ComputeDrillableChildIdsAsync(userId, Scope, ScopeId.Value, Level, ct);

        // Populate ShiftCategory options — only meaningful at user-level views where a molecule
        // can be determined. Guard: skip if ScopeId is null (already handled above).
        await PopulateCategoryOptionsAsync(ct);

        var query = new JusticeQuery(
            Scope: Scope,
            ScopeId: ScopeId,
            PeriodStart: EffectivePeriodStart,
            PeriodEnd: EffectivePeriodEnd,
            WorkType: WorkType,
            ExcludeExemptShifts: ExcludeExemptShifts,
            Level: Level,
            Basis: Basis,
            ShiftCategoryId: ShiftCategoryId);

        try
        {
            // A/B comparison: run when both compare bounds are provided and form a valid range.
            bool hasCompare = CompareFrom.HasValue && CompareTo.HasValue && CompareTo.Value >= CompareFrom.Value;
            if (hasCompare)
            {
                var compareQuery = query with
                {
                    PeriodStart = CompareFrom!.Value,
                    PeriodEnd = CompareTo!.Value
                };

                // GetComparisonViewAsync populates Primary rows with DeltaVsCompare and includes sparklines.
                Comparison = await _justiceService.GetComparisonViewAsync(query, compareQuery, drillableChildIds, ct);

                // Assign Primary as the surface View so the existing template can render rows with
                // DeltaVsCompare populated.
                View = Comparison.Primary;
            }
            else
            {
                // Standard (non-comparison) path with sparklines enabled.
                View = await _justiceService.GetJusticeViewAsync(query, drillableChildIds, includeSparklines: true, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Justice query failed for user {UserId}", userId);
            Error = _localizer["Justice_Error_LoadFailed"].Value;
        }

        return Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Forbid();
        }

        // Re-run the same query the page is rendering. Keeps the export aligned with what the
        // user sees on screen (same scope, same period, same toggle state).
        await PopulateScopeOptionsAsync(userId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = PeriodStart ?? new DateOnly(today.Year, today.Month, 1);
        var end = PeriodEnd ?? today;
        NormalizeScopeAndLevelDefaults();
        if (ScopeId is null)
        {
            return RedirectToPage();
        }
        if (!await UserCanAccessScopeAsync(userId, Scope, ScopeId.Value, ct))
        {
            return Forbid();
        }

        // Include Basis in the export query so the Expected / share columns match the active basis.
        var query = new JusticeQuery(Scope, ScopeId, start, end, WorkType, ExcludeExemptShifts, Level, Basis);
        var view = await _justiceService.GetJusticeViewAsync(query, ct);

        // Helper: pick the Expected and ExpectedShare values that match the active Basis so
        // the CSV columns are consistent with what the user sees on the page.
        decimal GetActiveExpected(JusticeRow r) => Basis == FairnessBasis.EqualShare ? r.ExpectedEqual : r.ExpectedBySize;
        decimal? GetActiveExpectedShare(JusticeRow r) => Basis == FairnessBasis.EqualShare ? r.ExpectedShareEqual : r.ExpectedShareBySize;
        decimal? GetActiveDeviationPercent(JusticeRow r) => Basis == FairnessBasis.EqualShare ? r.DeviationPercentEqual : r.DeviationPercent;
        string GetActiveBand(JusticeRow r) => (Basis == FairnessBasis.EqualShare ? r.BandEqual : r.Band).ToString();

        var csv = new StringBuilder();
        csv.AppendLine($"Justice Report");
        csv.AppendLine($"Generated,{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        csv.AppendLine($"Scope,{Scope}/{ScopeId}");
        csv.AppendLine($"Level,{Level}");
        csv.AppendLine($"Work Type,{WorkType}");
        csv.AppendLine($"Period,{start:yyyy-MM-dd},{end:yyyy-MM-dd}");
        csv.AppendLine($"Exclude Exempt Shifts,{ExcludeExemptShifts}");
        csv.AppendLine($"Basis,{Basis}");
        csv.AppendLine($"Spread Index,{view.SpreadIndex:F2}");
        csv.AppendLine();
        // Columns: Name | Actual | % of total (ActualShare) | Expected | Expected % of total (ExpectedShare) | Deviation % | Band | Basis
        csv.AppendLine("Name,Actual,% of total,Expected,Expected % of total,Deviation %,Band,Basis");
        foreach (var row in view.Rows)
        {
            var actualShare = row.ActualShare.HasValue ? $"{row.ActualShare.Value * 100m:F1}%" : "";
            var expected = GetActiveExpected(row);
            var expectedShare = GetActiveExpectedShare(row);
            var expectedShareStr = expectedShare.HasValue ? $"{expectedShare.Value * 100m:F1}%" : "";
            var devPct = GetActiveDeviationPercent(row);
            var dev = devPct.HasValue ? $"{devPct.Value:F1}" : "";
            var band = GetActiveBand(row);
            // Neutralize spreadsheet formula injection (OWASP CSV injection)
            var rawName = row.Name;
            if (rawName.Length > 0 && (rawName[0] == '=' || rawName[0] == '+' || rawName[0] == '-' ||
                rawName[0] == '@' || rawName[0] == '\t' || rawName[0] == '\r'))
            {
                rawName = "'" + rawName;
            }
            var safeName = $"\"{rawName.Replace("\"", "\"\"")}\"";
            csv.AppendLine($"{safeName},{row.Actual:F0},{actualShare},{expected:F2},{expectedShareStr},{dev},{band},{Basis}");
        }

        var fileName = $"Justice_{Level}_{Basis}_{start:yyyyMMdd}_{end:yyyyMMdd}.csv";
        // UTF-8 BOM for Hebrew Excel compatibility — same trick the legacy page used.
        var preamble = Encoding.UTF8.GetPreamble();
        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        var output = new byte[preamble.Length + bytes.Length];
        preamble.CopyTo(output, 0);
        bytes.CopyTo(output, preamble.Length);
        return File(output, "text/csv", fileName);
    }

    // ----- scope option / authorization helpers --------------------------------------------

    private async Task PopulateScopeOptionsAsync(int userId, CancellationToken ct)
    {
        // Pull the IDs the user can read for the Justice grant. Cascade: companies > molecules > areas.
        var companyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "ViewJusticeTable");
        var moleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewJusticeTable");

        // SECURITY: IgnoreQueryFilters is required because Owner / AreaAdmin may have
        // accessible companies/molecules outside their own tenant. The grant lookups above
        // already gate which IDs the user is authorized to see.
        if (companyIds.Count > 0)
        {
            CompanyOptions = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.Id))
                .OrderBy(c => c.Name)
                .Select(c => new ScopeOption(c.Id, c.Name ?? $"#{c.Id}"))
                .ToListAsync(ct);
        }

        if (moleculeIds.Count > 0)
        {
            MoleculeOptions = await _db.Molecules
                .IgnoreQueryFilters()
                .Where(m => moleculeIds.Contains(m.Id))
                .OrderBy(m => m.Name)
                .Select(m => new ScopeOption(m.Id, m.Name ?? $"#{m.Id}"))
                .ToListAsync(ct);

            // Areas reachable via the user's accessible molecules.
            var areaIds = await _db.Molecules
                .IgnoreQueryFilters()
                .Where(m => moleculeIds.Contains(m.Id))
                .Select(m => m.AreaId)
                .Distinct()
                .ToListAsync(ct);
            if (areaIds.Count > 0)
            {
                AreaOptions = await _db.Areas
                    .IgnoreQueryFilters()
                    .Where(a => areaIds.Contains(a.Id))
                    .OrderBy(a => a.Name)
                    .Select(a => new ScopeOption(a.Id, a.Name ?? $"#{a.Id}"))
                    .ToListAsync(ct);
            }
        }
    }

    /// <summary>
    /// Populates <see cref="CategoryOptions"/> from the active molecule's ShiftCategories.
    /// Resolves the molecule ID from the current Level/ScopeId:
    ///   • UsersInMolecule  → ScopeId IS the molecule ID.
    ///   • UsersInCompany   → look up Company.MoleculeId for the current ScopeId.
    ///   • Other levels     → leave empty (category filter not applicable).
    /// Must be called AFTER NormalizeScopeAndLevelDefaults so ScopeId is resolved.
    /// </summary>
    private async Task PopulateCategoryOptionsAsync(CancellationToken ct)
    {
        if (ScopeId is null) return;

        int? moleculeId = null;
        if (Level == JusticeLevel.UsersInMolecule)
        {
            moleculeId = ScopeId;
        }
        else if (Level == JusticeLevel.UsersInCompany)
        {
            // SECURITY: IgnoreQueryFilters required — the Justice viewer may be an owner or
            // area admin whose tenant differs from the requested company.
            moleculeId = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Id == ScopeId.Value)
                .Select(c => c.MoleculeId)
                .FirstOrDefaultAsync(ct);
        }

        if (moleculeId is null) return;

        // SECURITY: ShiftCategory is not tenant-filtered (no IBelongsToCompany); molecule-scoped.
        CategoryOptions = await _db.ShiftCategories
            .Where(sc => sc.MoleculeId == moleculeId.Value && sc.IsActive)
            .OrderBy(sc => sc.SortOrder)
            .Select(sc => new ScopeOption(sc.Id, sc.DisplayName))
            .ToListAsync(ct);
    }

    private void NormalizeScopeAndLevelDefaults()
    {
        // Each level is paired with exactly one scope kind. If they conflict, prefer the level.
        // UsersInMolecule uses Molecule scope (same as CompaniesInMolecule — filter to one molecule,
        // show one row per pooled user across all companies in that molecule).
        Scope = Level switch
        {
            JusticeLevel.UsersInCompany => JusticeScope.Company,
            JusticeLevel.CompaniesInMolecule => JusticeScope.Molecule,
            JusticeLevel.MoleculesInArea => JusticeScope.Area,
            JusticeLevel.UsersInMolecule => JusticeScope.Molecule,
            _ => Scope
        };

        // If no ScopeId set yet, pick the first accessible option for the chosen level.
        // UsersInMolecule shares the Molecule options list (same scope kind as CompaniesInMolecule).
        if (ScopeId is null)
        {
            ScopeId = Level switch
            {
                JusticeLevel.UsersInCompany => CompanyOptions.FirstOrDefault()?.Id,
                JusticeLevel.CompaniesInMolecule => MoleculeOptions.FirstOrDefault()?.Id,
                JusticeLevel.MoleculesInArea => AreaOptions.FirstOrDefault()?.Id,
                JusticeLevel.UsersInMolecule => MoleculeOptions.FirstOrDefault()?.Id,
                _ => null
            };
        }
        else
        {
            // Validate the chosen ScopeId is in the accessible options for this level.
            // If not, drop it and force the user back to the picker.
            bool inOptions = Level switch
            {
                JusticeLevel.UsersInCompany => CompanyOptions.Any(o => o.Id == ScopeId),
                JusticeLevel.CompaniesInMolecule => MoleculeOptions.Any(o => o.Id == ScopeId),
                JusticeLevel.MoleculesInArea => AreaOptions.Any(o => o.Id == ScopeId),
                JusticeLevel.UsersInMolecule => MoleculeOptions.Any(o => o.Id == ScopeId),
                _ => false
            };
            if (!inOptions) ScopeId = null;
        }
    }

    private async Task<bool> UserCanAccessScopeAsync(int userId, JusticeScope scope, int scopeId, CancellationToken ct)
    {
        switch (scope)
        {
            case JusticeScope.Company:
                {
                    var ids = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "ViewJusticeTable");
                    return ids.Contains(scopeId);
                }
            case JusticeScope.Molecule:
                {
                    var ids = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewJusticeTable");
                    return ids.Contains(scopeId);
                }
            case JusticeScope.Area:
                {
                    // Reachable area = an area that contains at least one accessible molecule.
                    var moleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewJusticeTable");
                    if (moleculeIds.Count == 0) return false;
                    return await _db.Molecules
                        .IgnoreQueryFilters()
                        .AnyAsync(m => moleculeIds.Contains(m.Id) && m.AreaId == scopeId, ct);
                }
            default:
                return false;
        }
    }

    /// <summary>
    /// DRILL authorization (A5) — distinct from <see cref="UserCanAccessScopeAsync"/>.
    ///
    /// Returns true only when the requested scope is genuinely within the user's OWN subtree, i.e.
    /// a scope they may navigate INTO (not merely read as a sibling for comparison). This is the
    /// strict gate that blocks cross-scope drill-through: a sibling molecule/company is rendered as
    /// a readable comparison row, but a request that drills into its scopeId must be forbidden.
    ///
    /// Rules:
    ///   • Company scope → companyId must be in GetAccessibleCompanyIdsForGrantAsync.
    ///   • Molecule scope → moleculeId must be in GetAccessibleMoleculeIdsForGrantAsync.
    ///   • Area scope → areaId must be in GetAccessibleAreaIdsForGrantAsync (area contains an
    ///     accessible molecule OR is covered by an area/project grant). The area is the top of the
    ///     comparison tier; an accessible area is drillable.
    /// </summary>
    private async Task<bool> CanDrillScopeAsync(int userId, JusticeScope scope, int scopeId, CancellationToken ct)
    {
        switch (scope)
        {
            case JusticeScope.Company:
                {
                    var ids = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "ViewJusticeTable");
                    return ids.Contains(scopeId);
                }
            case JusticeScope.Molecule:
                {
                    var ids = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewJusticeTable");
                    return ids.Contains(scopeId);
                }
            case JusticeScope.Area:
                {
                    var ids = await _grantService.GetAccessibleAreaIdsForGrantAsync(userId, "ViewJusticeTable");
                    return ids.Contains(scopeId);
                }
            default:
                return false;
        }
    }

    /// <summary>
    /// Computes the set of CHILD-row ids at the current <paramref name="level"/> that the user may
    /// DRILL into. Sibling rows the user can only read are excluded from this set (so the service
    /// flags them IsDrillable=false) but are still returned for comparison. Returns null at user
    /// levels (terminal rows — no drill-down).
    /// </summary>
    private async Task<IReadOnlyCollection<int>?> ComputeDrillableChildIdsAsync(int userId, JusticeScope scope, int scopeId, JusticeLevel level, CancellationToken ct)
    {
        switch (level)
        {
            case JusticeLevel.MoleculesInArea:
                {
                    // drillable child molecules = accessible molecules ∩ molecules-in-this-area.
                    var accessible = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewJusticeTable");
                    if (accessible.Count == 0) return Array.Empty<int>();
                    var accessibleSet = accessible.ToHashSet();
                    // SECURITY: IgnoreQueryFilters — molecules in the viewed area may span tenants;
                    // the result is intersected with the grant-authorized accessible set below.
                    var moleculesInArea = await _db.Molecules
                        .IgnoreQueryFilters()
                        .Where(m => m.AreaId == scopeId)
                        .Select(m => m.Id)
                        .ToListAsync(ct);
                    return moleculesInArea.Where(accessibleSet.Contains).ToList();
                }
            case JusticeLevel.CompaniesInMolecule:
                {
                    // drillable child companies = accessible companies ∩ companies-in-this-molecule.
                    var accessible = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "ViewJusticeTable");
                    if (accessible.Count == 0) return Array.Empty<int>();
                    var accessibleSet = accessible.ToHashSet();
                    // SECURITY: IgnoreQueryFilters — companies in the viewed molecule may span tenants;
                    // the result is intersected with the grant-authorized accessible set below.
                    var companiesInMolecule = await _db.Companies
                        .IgnoreQueryFilters()
                        .Where(c => c.MoleculeId == scopeId)
                        .Select(c => c.Id)
                        .ToListAsync(ct);
                    return companiesInMolecule.Where(accessibleSet.Contains).ToList();
                }
            case JusticeLevel.UsersInMolecule:
                // Pooled users across all companies in the molecule — rows are terminal (no drill-down).
                return null;
            default:
                // UsersInCompany — rows are terminal; no drill-down, leave uncapped (all drillable).
                return null;
        }
    }
}
