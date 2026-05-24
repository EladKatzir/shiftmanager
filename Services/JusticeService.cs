using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Implementation of <see cref="IJusticeService"/>. Phase 1: read-only views.
///
/// Algorithm summary (see plan file <c>we-want-to-redesign-proud-ullman.md</c> for full design):
///   1. Build the row set for the requested <see cref="JusticeLevel"/>:
///        - UsersInCompany     -> one row per user in scope
///        - CompaniesInMolecule -> one row per company in scope
///        - MoleculesInArea    -> one row per molecule in scope
///   2. For each row, compute Actual (count of completed work in period) and Expected
///      (resolved per the targets/overrides table or, for Shift work-type, from
///      Sum(ShiftInstance.StaffingRequired)).
///   3. Compute deviation %, band, and the page-wide spread index.
///
/// The "exclude exempt" toggle (<see cref="JusticeQuery.ExcludeExemptShifts"/>) gates BOTH
/// the Actual and Expected paths for the Shift work-type symmetrically. Otherwise the deviation
/// math goes nonsense when the toggle flips.
/// </summary>
public class JusticeService : IJusticeService
{
    private readonly AppDbContext _db;
    private readonly IShiftAssignmentService _shiftAssignmentService;
    private readonly IChoreService _choreService;
    private readonly IOnDutyService _onDutyService;

    // Conversion factors used to scale per-user-per-PeriodKind targets to the actual page period.
    // 30.4375 = average days per month (365.25 / 12). Good enough for fairness math; we don't need
    // calendar-month precision when comparing 30 vs 31 days.
    private const decimal DaysPerWeek = 7m;
    private const decimal DaysPerMonth = 30.4375m;
    private const decimal DaysPerQuarter = 91.3125m;

    // Phase 2: how many holes to surface in the in-context drawer's "Where to focus" list.
    private const int WhereToFocusTopN = 5;

    public JusticeService(
        AppDbContext db,
        IShiftAssignmentService shiftAssignmentService,
        IChoreService choreService,
        IOnDutyService onDutyService)
    {
        _db = db;
        _shiftAssignmentService = shiftAssignmentService;
        _choreService = choreService;
        _onDutyService = onDutyService;
    }

    public async Task<List<JusticeTarget>> GetTargetsAsync(CancellationToken ct = default)
    {
        // SECURITY: IgnoreQueryFilters is required here — scope already gated at page-model
        // boundary; cross-company admins must see company-scoped overrides for all companies
        // in the viewed scope.
        return await _db.JusticeTargets.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
    }

    public Task<JusticeViewModel> GetJusticeViewAsync(JusticeQuery q, CancellationToken ct = default)
        => GetJusticeViewAsync(q, drillableChildIds: null, includeSparklines: false, ct);

    public Task<JusticeViewModel> GetJusticeViewAsync(JusticeQuery q, IReadOnlyCollection<int>? drillableChildIds, CancellationToken ct = default)
        => GetJusticeViewAsync(q, drillableChildIds, includeSparklines: false, ct);

    public async Task<JusticeViewModel> GetJusticeViewAsync(JusticeQuery q, IReadOnlyCollection<int>? drillableChildIds, bool includeSparklines, CancellationToken ct = default)
    {
        // Targets cache: load once per request so ResolveExpected doesn't round-trip the DB
        // for each row.
        // SECURITY: IgnoreQueryFilters is required here — scope already gated at page-model
        // boundary; cross-company admins must see company-scoped overrides for all companies
        // in the viewed scope.
        var targets = await _db.JusticeTargets.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);

        var rows = q.Level switch
        {
            JusticeLevel.UsersInCompany => await BuildUsersInCompanyAsync(q, targets, drillableChildIds, ct),
            JusticeLevel.CompaniesInMolecule => await BuildCompaniesInMoleculeAsync(q, targets, drillableChildIds, ct),
            JusticeLevel.MoleculesInArea => await BuildMoleculesInAreaAsync(q, targets, drillableChildIds, ct),
            _ => new List<JusticeRow>()
        };

        // A4: precompute both bases + share fields so the front-end can toggle without a round-trip.
        // This also sets the PRIMARY fields (Expected/DeviationPercent/Band) to the active basis,
        // replacing the standalone A3 ApplyEqualShareBasis call.
        rows = ComputeSharesAndBothBases(rows, q);

        var validRows = rows.Where(r => r.Band != DeviationBand.NoTarget && r.DeviationPercent.HasValue).ToList();

        var spreadIndex = ComputeSpreadIndex(validRows);
        var (severity, labelKey) = MapSpreadToSeverity(spreadIndex);

        JusticeRow? mostOver = validRows
            .Where(r => r.DeviationPercent > 0)
            .OrderByDescending(r => r.DeviationPercent)
            .FirstOrDefault();
        JusticeRow? mostUnder = validRows
            .Where(r => r.DeviationPercent < 0)
            .OrderBy(r => r.DeviationPercent)
            .FirstOrDefault();

        var maxRibbon = rows.Any() ? Math.Max(1m, rows.Max(r => r.Actual)) : 1m;

        // Default sort: |deviation %| descending (biggest imbalance first).
        // Rows with no target / null deviation sort to the bottom.
        rows = rows
            .OrderByDescending(r => r.DeviationPercent.HasValue ? Math.Abs(r.DeviationPercent.Value) : -1m)
            .ToList();

        // A6: optionally enrich each row with a 6-month sparkline series.
        // Skipped entirely (null Sparkline) when includeSparklines=false to avoid extra DB cost.
        if (includeSparklines && rows.Count > 0)
        {
            const int SparklineBuckets = 6;
            var sparklines = await GetSparklineSeriesAsync(q, SparklineBuckets, ct);
            var allZeros = Enumerable.Repeat(0m, SparklineBuckets).ToList() as IReadOnlyList<decimal>;
            rows = rows
                .Select(r => r with
                {
                    Sparkline = sparklines.TryGetValue(r.Id, out var s) ? s : allZeros
                })
                .ToList();
        }

        return new JusticeViewModel(
            Query: q,
            SpreadIndex: spreadIndex,
            SpreadSeverity: severity,
            SpreadLabelKey: labelKey,
            MostOver: mostOver,
            MostUnder: mostUnder,
            Rows: rows,
            MaxRibbonValue: maxRibbon);
    }

    // -----------------------------------------------------------------------------------
    // Row builders — one per JusticeLevel
    // -----------------------------------------------------------------------------------

    // A5: a row is drillable iff the caller did not supply a cap (null) OR the cap contains the row id.
    // User-level rows are terminal (no drill-down), so IsDrillable is irrelevant there and left true.
    private static bool IsRowDrillable(IReadOnlyCollection<int>? drillableChildIds, int rowId)
        => drillableChildIds == null || drillableChildIds.Contains(rowId);

    private async Task<List<JusticeRow>> BuildUsersInCompanyAsync(JusticeQuery q, List<JusticeTarget> targets, IReadOnlyCollection<int>? drillableChildIds, CancellationToken ct)
    {
        if (q.Scope != JusticeScope.Company || q.ScopeId is null)
            return new List<JusticeRow>();

        var companyId = q.ScopeId.Value;

        // SECURITY: IgnoreQueryFilters is required because the Justice page may be invoked by users
        // whose tenant differs from the requested scope (e.g., Owner / AreaAdmin viewing another company).
        // The scope itself is gated by IGrantService.GetAccessibleCompanyIdsForGrantAsync at the page
        // model level — by the time we reach here, the caller is authorized to read that company.
        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.CompanyId == companyId && u.IsActive)
            .Select(u => new { u.Id, u.DisplayName, u.AvatarFileName })
            .AsNoTracking()
            .ToListAsync(ct);

        if (users.Count == 0) return new List<JusticeRow>();
        var headcount = users.Count;

        // Per-user actual
        var userActuals = await CountActualPerUserAsync(q, new[] { companyId }, ct);

        // Compute scope-wide shift capacity (only for Shift / All) so we can divide it by headcount
        // to give each user their fair share. Chore/OnDuty per-user expected falls out of the targets
        // table directly (override-aware), so headcount-division applies only to capacity.
        decimal scopeShiftCapacity = 0m;
        if (q.WorkType is JusticeWorkType.Shift or JusticeWorkType.All)
        {
            var capacityByCompany = await SumShiftCapacityPerCompanyAsync(q, new[] { companyId }, ct);
            scopeShiftCapacity = capacityByCompany.GetValueOrDefault(companyId, 0m);
        }

        var perUserExpected = ResolvePerUserExpected(q, companyId, headcount, scopeShiftCapacity, targets);

        var rows = new List<JusticeRow>(users.Count);
        foreach (var u in users)
        {
            var actual = userActuals.GetValueOrDefault(u.Id, 0m);
            var (devPct, band) = ComputeDeviation(actual, perUserExpected);
            rows.Add(new JusticeRow(
                Id: u.Id,
                Name: u.DisplayName ?? $"#{u.Id}",
                AvatarUrl: BuildAvatarThumbUrl(u.AvatarFileName, companyId, u.Id),
                Actual: actual,
                Expected: perUserExpected,
                DeviationPercent: devPct,
                Band: band)
            {
                // User rows are terminal; honor the cap if one was supplied (defaults to true).
                IsDrillable = IsRowDrillable(drillableChildIds, u.Id)
            });
        }
        return rows;
    }

    // Per-user expected = sum across enabled work-types of per-user share.
    // Shift share = scope_capacity / headcount.
    // Chore/OnDuty share = company-scoped override (if any) / headcount, else global per-user default.
    private decimal ResolvePerUserExpected(JusticeQuery q, int companyId, int headcount, decimal shiftCapacity, List<JusticeTarget> targets)
    {
        if (headcount <= 0) return 0m;

        decimal expected = 0m;

        if (q.WorkType is JusticeWorkType.Shift or JusticeWorkType.All)
        {
            expected += shiftCapacity / headcount;
        }
        if (q.WorkType is JusticeWorkType.Chore or JusticeWorkType.All)
        {
            expected += ResolvePerUserChoreOrOnDuty(q, JusticeWorkType.Chore, companyId, headcount, targets);
        }
        if (q.WorkType is JusticeWorkType.OnDuty or JusticeWorkType.All)
        {
            expected += ResolvePerUserChoreOrOnDuty(q, JusticeWorkType.OnDuty, companyId, headcount, targets);
        }
        return expected;
    }

    private decimal ResolvePerUserChoreOrOnDuty(JusticeQuery q, JusticeWorkType wt, int companyId, int headcount, List<JusticeTarget> targets)
    {
        // Company-scope override wins.
        var compOverride = targets.FirstOrDefault(t =>
            t.WorkType == wt && t.ScopeKind == JusticeScope.Company && t.ScopeId == companyId);
        if (compOverride != null && headcount > 0)
        {
            var totalForCompany = compOverride.ExpectedCount * PeriodMultiplier(q, compOverride.PeriodKind);
            return totalForCompany / headcount;
        }

        // Else: per-user global default applied directly (already a per-user number).
        var globalTarget = targets.FirstOrDefault(t =>
            t.WorkType == wt && t.ScopeKind == JusticeScope.Global);
        if (globalTarget == null) return 0m;
        return globalTarget.ExpectedCount * PeriodMultiplier(q, globalTarget.PeriodKind);
    }

    private async Task<List<JusticeRow>> BuildCompaniesInMoleculeAsync(JusticeQuery q, List<JusticeTarget> targets, IReadOnlyCollection<int>? drillableChildIds, CancellationToken ct)
    {
        if (q.Scope != JusticeScope.Molecule || q.ScopeId is null)
            return new List<JusticeRow>();

        var moleculeId = q.ScopeId.Value;

        var companies = await _db.Companies
            .IgnoreQueryFilters() // SECURITY: Justice viewer may span tenants (Owner/AreaAdmin); scope-gated upstream.
            .Where(c => c.MoleculeId == moleculeId)
            .Select(c => new { c.Id, c.Name })
            .AsNoTracking()
            .ToListAsync(ct);

        if (companies.Count == 0) return new List<JusticeRow>();

        var companyIds = companies.Select(c => c.Id).ToArray();
        var actualByCompany = await CountActualPerCompanyAsync(q, companyIds, ct);

        // Per-company headcount, for the per-user-default rollup path.
        var headcountByCompany = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
            .GroupBy(u => u.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count, ct);

        // For shifts we need actual capacity per company; chore/on-duty roll up via override or per-capita.
        Dictionary<int, decimal> capacityByCompany = new();
        if (q.WorkType == JusticeWorkType.Shift || q.WorkType == JusticeWorkType.All)
        {
            capacityByCompany = await SumShiftCapacityPerCompanyAsync(q, companyIds, ct);
        }

        var rows = new List<JusticeRow>(companies.Count);
        foreach (var c in companies)
        {
            var actual = actualByCompany.GetValueOrDefault(c.Id, 0m);
            var head = headcountByCompany.GetValueOrDefault(c.Id, 0);
            var capacity = capacityByCompany.GetValueOrDefault(c.Id, 0m);

            var expected = ResolveExpectedForCompanyOrMolecule(q, JusticeScope.Company, c.Id, head, capacity, targets);
            var (devPct, band) = ComputeDeviation(actual, expected);
            rows.Add(new JusticeRow(
                Id: c.Id,
                Name: c.Name ?? $"Company #{c.Id}",
                AvatarUrl: null,
                Actual: actual,
                Expected: expected,
                DeviationPercent: devPct,
                Band: band)
            {
                IsDrillable = IsRowDrillable(drillableChildIds, c.Id)
            });
        }
        return rows;
    }

    private async Task<List<JusticeRow>> BuildMoleculesInAreaAsync(JusticeQuery q, List<JusticeTarget> targets, IReadOnlyCollection<int>? drillableChildIds, CancellationToken ct)
    {
        if (q.Scope != JusticeScope.Area || q.ScopeId is null)
            return new List<JusticeRow>();

        var areaId = q.ScopeId.Value;

        var molecules = await _db.Molecules
            .IgnoreQueryFilters() // SECURITY: AreaAdmin/Owner can view across tenancies; scope-gated upstream.
            .Where(m => m.AreaId == areaId)
            .Select(m => new { m.Id, m.Name })
            .AsNoTracking()
            .ToListAsync(ct);

        if (molecules.Count == 0) return new List<JusticeRow>();

        var moleculeIds = molecules.Select(m => m.Id).ToArray();

        // Companies grouped by their molecule, so we can sum work per molecule via company list.
        var companiesByMolecule = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.MoleculeId != null && moleculeIds.Contains(c.MoleculeId.Value))
            .Select(c => new { c.Id, MoleculeId = c.MoleculeId!.Value })
            .AsNoTracking()
            .ToListAsync(ct);

        var companyIdsByMolecule = companiesByMolecule
            .GroupBy(x => x.MoleculeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToArray());

        // Per-molecule headcount
        var allCompanyIds = companiesByMolecule.Select(x => x.Id).ToArray();
        var headcountByCompany = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => allCompanyIds.Contains(u.CompanyId) && u.IsActive)
            .GroupBy(u => u.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count, ct);

        var capacityByCompany = (q.WorkType == JusticeWorkType.Shift || q.WorkType == JusticeWorkType.All)
            ? await SumShiftCapacityPerCompanyAsync(q, allCompanyIds, ct)
            : new Dictionary<int, decimal>();

        var actualByCompany = await CountActualPerCompanyAsync(q, allCompanyIds, ct);

        var rows = new List<JusticeRow>(molecules.Count);
        foreach (var m in molecules)
        {
            var compIds = companyIdsByMolecule.GetValueOrDefault(m.Id, Array.Empty<int>());
            decimal moleculeActual = compIds.Sum(cid => actualByCompany.GetValueOrDefault(cid, 0m));
            int moleculeHead = compIds.Sum(cid => headcountByCompany.GetValueOrDefault(cid, 0));
            decimal moleculeCapacity = compIds.Sum(cid => capacityByCompany.GetValueOrDefault(cid, 0m));

            var expected = ResolveExpectedForCompanyOrMolecule(q, JusticeScope.Molecule, m.Id, moleculeHead, moleculeCapacity, targets);
            var (devPct, band) = ComputeDeviation(moleculeActual, expected);
            rows.Add(new JusticeRow(
                Id: m.Id,
                Name: m.Name ?? $"Molecule #{m.Id}",
                AvatarUrl: null,
                Actual: moleculeActual,
                Expected: expected,
                DeviationPercent: devPct,
                Band: band)
            {
                IsDrillable = IsRowDrillable(drillableChildIds, m.Id)
            });
        }
        return rows;
    }

    // -----------------------------------------------------------------------------------
    // Actual count queries (Shift / Chore / OnDuty / All)
    // -----------------------------------------------------------------------------------

    private async Task<decimal> CountActualAsync(JusticeQuery q, int[] companyIds, CancellationToken ct)
    {
        var perUser = await CountActualPerUserAsync(q, companyIds, ct);
        return perUser.Values.Sum();
    }

    private async Task<Dictionary<int, decimal>> CountActualPerUserAsync(JusticeQuery q, int[] companyIds, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var endCap = q.PeriodEnd < today ? q.PeriodEnd : today;

        var byUser = new Dictionary<int, decimal>();

        if (q.WorkType is JusticeWorkType.Shift or JusticeWorkType.All)
        {
            // SECURITY: IgnoreQueryFilters required for cross-company analytics views.
            var shiftQ = _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(a => a.UserId != null
                            && companyIds.Contains(a.CompanyId)
                            && a.ShiftInstance.WorkDate >= q.PeriodStart
                            && a.ShiftInstance.WorkDate <= endCap);
            if (q.ExcludeExemptShifts)
            {
                shiftQ = shiftQ.Where(a => a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME
                                         && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME_AM
                                         && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME_PM
                                         && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_OFFLINE);
            }
            var shiftRows = await shiftQ
                .GroupBy(a => a.UserId!.Value)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            foreach (var r in shiftRows)
                byUser[r.UserId] = byUser.GetValueOrDefault(r.UserId, 0m) + r.Count;
        }

        if (q.WorkType is JusticeWorkType.Chore or JusticeWorkType.All)
        {
            // SECURITY: IgnoreQueryFilters required for cross-company analytics views.
            var choreRows = await _db.Chores
                .IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.CompanyId)
                            && c.CanceledAt == null
                            && c.Date >= q.PeriodStart
                            && c.Date <= endCap)
                .GroupBy(c => c.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            foreach (var r in choreRows)
                byUser[r.UserId] = byUser.GetValueOrDefault(r.UserId, 0m) + r.Count;
        }

        if (q.WorkType is JusticeWorkType.OnDuty or JusticeWorkType.All)
        {
            // OnDuty has no CompanyId (global table); restrict by joining to Users.
            // SECURITY NOTE: OnDuty is intentionally global per the project's data model;
            // the scoping is performed by joining to AppUser and filtering by the caller's authorized companies.
            var dutyRows = await _db.OnDuties
                .Where(od => od.CanceledAt == null
                             && od.Date >= q.PeriodStart
                             && od.Date <= endCap
                             && _db.Users.IgnoreQueryFilters()
                                  .Any(u => u.Id == od.UserId && companyIds.Contains(u.CompanyId)))
                .GroupBy(od => od.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            foreach (var r in dutyRows)
                byUser[r.UserId] = byUser.GetValueOrDefault(r.UserId, 0m) + r.Count;
        }

        return byUser;
    }

    private async Task<Dictionary<int, decimal>> CountActualPerCompanyAsync(JusticeQuery q, int[] companyIds, CancellationToken ct)
    {
        // Reuse the per-user counter then attribute each user back to a company. One DB roundtrip
        // for users-in-companies plus the work counts.
        var perUser = await CountActualPerUserAsync(q, companyIds, ct);
        if (perUser.Count == 0) return new Dictionary<int, decimal>();

        var userToCompany = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => perUser.Keys.Contains(u.Id))
            .Select(u => new { u.Id, u.CompanyId })
            .ToDictionaryAsync(u => u.Id, u => u.CompanyId, ct);

        var byCompany = new Dictionary<int, decimal>();
        foreach (var kv in perUser)
        {
            if (userToCompany.TryGetValue(kv.Key, out var cid))
                byCompany[cid] = byCompany.GetValueOrDefault(cid, 0m) + kv.Value;
        }
        return byCompany;
    }

    private async Task<Dictionary<int, decimal>> SumShiftCapacityPerCompanyAsync(JusticeQuery q, int[] companyIds, CancellationToken ct)
    {
        // SECURITY: IgnoreQueryFilters required for cross-company aggregation; scope-gated upstream.
        var query = _db.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => companyIds.Contains(si.CompanyId)
                         && si.WorkDate >= q.PeriodStart
                         && si.WorkDate <= q.PeriodEnd);
        if (q.ExcludeExemptShifts)
        {
            query = query.Where(si => si.ShiftType.Key != ShiftType.KEY_HOME
                                    && si.ShiftType.Key != ShiftType.KEY_HOME_AM
                                    && si.ShiftType.Key != ShiftType.KEY_HOME_PM
                                    && si.ShiftType.Key != ShiftType.KEY_OFFLINE);
        }
        var rows = await query
            .GroupBy(si => si.CompanyId)
            .Select(g => new { CompanyId = g.Key, Total = g.Sum(si => si.StaffingRequired) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.CompanyId, r => (decimal)r.Total);
    }

    // -----------------------------------------------------------------------------------
    // A6: Sparkline series
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Computes <paramref name="buckets"/> consecutive calendar-month work-item counts ending
    /// at the month that contains <paramref name="baseQuery"/>.PeriodEnd.
    ///
    /// Algorithm:
    ///   1. Build the <paramref name="buckets"/> month windows (each = [firstOfMonth, lastOfMonth]).
    ///   2. Issue AT MOST one query per enabled work-type over the full span [firstBucketStart..lastBucketEnd],
    ///      pulling (userId, Date) pairs — no per-month round trips.
    ///   3. Bucket in memory by month index.
    ///   4. Attribute to the level's row key:
    ///        UsersInCompany       → userId
    ///        CompaniesInMolecule  → user's CompanyId
    ///        MoleculesInArea      → user's company's MoleculeId
    ///   5. Return rowId → List&lt;decimal&gt;[buckets], oldest→newest.
    ///      Rows in scope that had zero work are included with all-zeros.
    /// </summary>
    public async Task<Dictionary<int, List<decimal>>> GetSparklineSeriesAsync(
        JusticeQuery baseQuery, int buckets, CancellationToken ct = default)
    {
        if (buckets <= 0) return new Dictionary<int, List<decimal>>();

        // --- 1. Build bucket windows ---
        // The last bucket ends at the last day of the month containing PeriodEnd.
        var lastBucketEnd   = new DateOnly(baseQuery.PeriodEnd.Year, baseQuery.PeriodEnd.Month,
                                           DateTime.DaysInMonth(baseQuery.PeriodEnd.Year, baseQuery.PeriodEnd.Month));
        // Walk back (buckets-1) months for the first bucket.
        var firstBucketStart = lastBucketEnd.AddMonths(-(buckets - 1));
        firstBucketStart = new DateOnly(firstBucketStart.Year, firstBucketStart.Month, 1);

        // Build (start, end) pairs for each bucket index 0..buckets-1.
        var bucketStarts = new DateOnly[buckets];
        var bucketEnds   = new DateOnly[buckets];
        for (int i = 0; i < buckets; i++)
        {
            var monthStart = firstBucketStart.AddMonths(i);
            var monthEnd   = new DateOnly(monthStart.Year, monthStart.Month,
                                          DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
            bucketStarts[i] = monthStart;
            bucketEnds[i]   = monthEnd;
        }

        // Cap to today (same convention as CountActualPerUserAsync).
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var spanEnd = lastBucketEnd < today ? lastBucketEnd : today;

        // --- 2. Determine the company IDs in scope ---
        // We reuse ResolveScopeCompanyIdsAsync which already handles all three scope kinds.
        var companyIds = await ResolveScopeCompanyIdsAsync(baseQuery, ct);

        // --- 3. Build row-key maps for non-user levels ---
        // userToRowKey: userId → the row ID to accumulate into (userId / companyId / moleculeId).
        Dictionary<int, int> userToRowKey;
        HashSet<int> rowKeys; // all valid row keys in scope (needed for all-zeros padding)

        if (baseQuery.Level == JusticeLevel.UsersInCompany)
        {
            // Row key = userId. Valid row keys = all active users in scope company.
            if (baseQuery.Scope != JusticeScope.Company || baseQuery.ScopeId is null)
                return new Dictionary<int, List<decimal>>();
            var usersInCompany = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.CompanyId == baseQuery.ScopeId.Value && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync(ct);
            userToRowKey = usersInCompany.ToDictionary(uid => uid, uid => uid);
            rowKeys = usersInCompany.ToHashSet();
        }
        else if (baseQuery.Level == JusticeLevel.CompaniesInMolecule)
        {
            // Row key = companyId. Need userId → companyId map.
            if (baseQuery.Scope != JusticeScope.Molecule || baseQuery.ScopeId is null)
                return new Dictionary<int, List<decimal>>();
            var companiesInMolecule = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.MoleculeId == baseQuery.ScopeId.Value)
                .Select(c => c.Id)
                .ToListAsync(ct);
            rowKeys = companiesInMolecule.ToHashSet();

            var usersInScope = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
                .Select(u => new { u.Id, u.CompanyId })
                .ToListAsync(ct);
            userToRowKey = usersInScope.ToDictionary(u => u.Id, u => u.CompanyId);
        }
        else if (baseQuery.Level == JusticeLevel.MoleculesInArea)
        {
            // Row key = moleculeId. Need userId → moleculeId via company.
            if (baseQuery.Scope != JusticeScope.Area || baseQuery.ScopeId is null)
                return new Dictionary<int, List<decimal>>();
            var companiesWithMolecule = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.MoleculeId != null && companyIds.Contains(c.Id))
                .Select(c => new { c.Id, MoleculeId = c.MoleculeId!.Value })
                .ToListAsync(ct);
            var companyToMolecule = companiesWithMolecule.ToDictionary(c => c.Id, c => c.MoleculeId);
            rowKeys = companiesWithMolecule.Select(c => c.MoleculeId).ToHashSet();

            var usersInScope = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
                .Select(u => new { u.Id, u.CompanyId })
                .ToListAsync(ct);
            userToRowKey = new Dictionary<int, int>();
            foreach (var u in usersInScope)
            {
                if (companyToMolecule.TryGetValue(u.CompanyId, out var molId))
                    userToRowKey[u.Id] = molId;
            }
        }
        else
        {
            return new Dictionary<int, List<decimal>>();
        }

        // --- 4. Initialise accumulator: rowKey → bucket totals ---
        var accum = new Dictionary<int, decimal[]>();
        foreach (var rk in rowKeys)
            accum[rk] = new decimal[buckets];

        // Helper: given a date, return the bucket index (0-based oldest) or -1 if outside span.
        int BucketIndex(DateOnly d)
        {
            for (int i = 0; i < buckets; i++)
                if (d >= bucketStarts[i] && d <= bucketEnds[i])
                    return i;
            return -1;
        }

        // --- 5. Query work items for the full span, bucket in memory ---

        if (baseQuery.WorkType is JusticeWorkType.Shift or JusticeWorkType.All)
        {
            // SECURITY: IgnoreQueryFilters required for cross-company analytics; scope-gated upstream.
            var shiftQ = _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(a => a.UserId != null
                            && companyIds.Contains(a.CompanyId)
                            && a.ShiftInstance.WorkDate >= firstBucketStart
                            && a.ShiftInstance.WorkDate <= spanEnd);
            if (baseQuery.ExcludeExemptShifts)
            {
                shiftQ = shiftQ.Where(a => a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME
                                          && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME_AM
                                          && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME_PM
                                          && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_OFFLINE);
            }
            var shiftRows = await shiftQ
                .Select(a => new { UserId = a.UserId!.Value, Date = a.ShiftInstance.WorkDate })
                .ToListAsync(ct);

            foreach (var r in shiftRows)
            {
                if (!userToRowKey.TryGetValue(r.UserId, out var rk)) continue;
                if (!accum.TryGetValue(rk, out var arr)) continue;
                int bi = BucketIndex(r.Date);
                if (bi >= 0) arr[bi]++;
            }
        }

        if (baseQuery.WorkType is JusticeWorkType.Chore or JusticeWorkType.All)
        {
            // SECURITY: IgnoreQueryFilters required for cross-company analytics; scope-gated upstream.
            var choreRows = await _db.Chores
                .IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.CompanyId)
                            && c.CanceledAt == null
                            && c.Date >= firstBucketStart
                            && c.Date <= spanEnd)
                .Select(c => new { c.UserId, c.Date })
                .ToListAsync(ct);

            foreach (var r in choreRows)
            {
                if (!userToRowKey.TryGetValue(r.UserId, out var rk)) continue;
                if (!accum.TryGetValue(rk, out var arr)) continue;
                int bi = BucketIndex(r.Date);
                if (bi >= 0) arr[bi]++;
            }
        }

        if (baseQuery.WorkType is JusticeWorkType.OnDuty or JusticeWorkType.All)
        {
            // OnDuty has no CompanyId; restrict by joining to Users — same pattern as CountActualPerUserAsync.
            // SECURITY NOTE: OnDuty is intentionally global; scope is enforced by the user→company join.
            var dutyRows = await _db.OnDuties
                .Where(od => od.CanceledAt == null
                             && od.Date >= firstBucketStart
                             && od.Date <= spanEnd
                             && _db.Users.IgnoreQueryFilters()
                                  .Any(u => u.Id == od.UserId && companyIds.Contains(u.CompanyId)))
                .Select(od => new { od.UserId, od.Date })
                .ToListAsync(ct);

            foreach (var r in dutyRows)
            {
                if (!userToRowKey.TryGetValue(r.UserId, out var rk)) continue;
                if (!accum.TryGetValue(rk, out var arr)) continue;
                int bi = BucketIndex(r.Date);
                if (bi >= 0) arr[bi]++;
            }
        }

        // --- 6. Materialise into the return type ---
        return accum.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToList());
    }

    // -----------------------------------------------------------------------------------
    // Expected resolution
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Compute total expected for a Company or Molecule scope (used at row-level aggregation in
    /// the Companies-in-Molecule and Molecules-in-Area views). Headcount-based per-user rollup
    /// when no explicit override exists at this scope.
    /// </summary>
    private decimal ResolveExpectedForCompanyOrMolecule(JusticeQuery q, JusticeScope scopeKind, int scopeId, int headcount, decimal shiftCapacity, List<JusticeTarget> targets)
    {
        // For a pure single-work-type query, just resolve that one type.
        if (q.WorkType != JusticeWorkType.All)
        {
            return ResolveSingleWorkTypeExpected(q.WorkType, q, scopeKind, scopeId, headcount, shiftCapacity, targets);
        }

        // For All, sum across the three.
        decimal total = 0m;
        total += ResolveSingleWorkTypeExpected(JusticeWorkType.Shift, q, scopeKind, scopeId, headcount, shiftCapacity, targets);
        total += ResolveSingleWorkTypeExpected(JusticeWorkType.Chore, q, scopeKind, scopeId, headcount, 0m, targets);
        total += ResolveSingleWorkTypeExpected(JusticeWorkType.OnDuty, q, scopeKind, scopeId, headcount, 0m, targets);
        return total;
    }

    private decimal ResolveSingleWorkTypeExpected(JusticeWorkType workType, JusticeQuery q, JusticeScope scopeKind, int scopeId, int headcount, decimal shiftCapacity, List<JusticeTarget> targets)
    {
        if (workType == JusticeWorkType.Shift)
        {
            // Capacity-driven: ignore targets table.
            return shiftCapacity;
        }

        // Look for explicit override at this scope.
        var explicitTarget = targets.FirstOrDefault(t =>
            t.WorkType == workType && t.ScopeKind == scopeKind && t.ScopeId == scopeId);
        if (explicitTarget != null)
        {
            return explicitTarget.ExpectedCount * PeriodMultiplier(q, explicitTarget.PeriodKind);
        }

        // Fall through to global per-user default.
        var globalTarget = targets.FirstOrDefault(t =>
            t.WorkType == workType && t.ScopeKind == JusticeScope.Global);
        if (globalTarget == null) return 0m;

        return globalTarget.ExpectedCount * headcount * PeriodMultiplier(q, globalTarget.PeriodKind);
    }

    // -----------------------------------------------------------------------------------
    // Period multiplier — converts per-Period target unit into the page's actual period
    // -----------------------------------------------------------------------------------

    private decimal PeriodMultiplier(JusticeQuery q, PeriodKind targetUnit)
    {
        var days = (decimal)(q.PeriodEnd.DayNumber - q.PeriodStart.DayNumber + 1);
        if (days <= 0) days = 1m;

        decimal unitDays = targetUnit switch
        {
            PeriodKind.PerWeek => DaysPerWeek,
            PeriodKind.PerMonth => DaysPerMonth,
            PeriodKind.PerQuarter => DaysPerQuarter,
            _ => DaysPerMonth
        };
        return days / unitDays;
    }

    // -----------------------------------------------------------------------------------
    // A4: Dual-basis precompute + share fields
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Enriches each row with both fairness-basis metrics and per-row share fields so
    /// the front-end can toggle between BySize and EqualShare without a round-trip.
    ///
    /// Contract (called AFTER the row builders, BEFORE spread/sort):
    ///   • Rows as received are the BySize output (Expected = capacity/target-weighted).
    ///   • <see cref="JusticeRow.ExpectedBySize"/> captures that as-built Expected.
    ///   • <see cref="JusticeRow.ExpectedEqual"/> = Σ Actual / N for every row.
    ///   • <see cref="JusticeRow.ActualShare"/> = Actual / Σ Actual (null when Σ == 0).
    ///   • <see cref="JusticeRow.ExpectedShareBySize"/> = ExpectedBySize / Σ ExpectedBySize (null when Σ == 0).
    ///   • <see cref="JusticeRow.ExpectedShareEqual"/> = 1 / N.
    ///   • <see cref="JusticeRow.DeviationPercentEqual"/> and <see cref="JusticeRow.BandEqual"/>
    ///     are computed against ExpectedEqual.
    ///   • PRIMARY fields (<c>Expected</c>, <c>DeviationPercent</c>, <c>Band</c>) are set to
    ///     the ACTIVE basis (<paramref name="q"/>.Basis), replicating A3's behavior exactly.
    /// </summary>
    private List<JusticeRow> ComputeSharesAndBothBases(List<JusticeRow> rows, JusticeQuery q)
    {
        if (rows.Count == 0) return rows;

        int n = rows.Count;
        decimal sumActual = rows.Sum(r => r.Actual);
        decimal equalExpected = sumActual / n;
        decimal sumExpectedBySize = rows.Sum(r => r.Expected);

        var result = new List<JusticeRow>(n);
        foreach (var row in rows)
        {
            // --- Shares (basis-independent) ---
            decimal? actualShare = sumActual > 0m ? row.Actual / sumActual : (decimal?)null;
            decimal? expectedShareBySize = sumExpectedBySize > 0m ? row.Expected / sumExpectedBySize : (decimal?)null;
            decimal? expectedShareEqual = 1m / n;

            // --- Equal-basis deviation ---
            var (devPctEqual, bandEqual) = ComputeDeviation(row.Actual, equalExpected);

            // --- Active-basis primary fields ---
            decimal primaryExpected;
            decimal? primaryDevPct;
            DeviationBand primaryBand;

            if (q.Basis == FairnessBasis.EqualShare)
            {
                primaryExpected = equalExpected;
                primaryDevPct   = devPctEqual;
                primaryBand     = bandEqual;
            }
            else
            {
                // BySize: keep as built (row.Expected / row.DeviationPercent / row.Band are already correct).
                primaryExpected = row.Expected;
                primaryDevPct   = row.DeviationPercent;
                primaryBand     = row.Band;
            }

            result.Add(row with
            {
                // Primary (active basis)
                Expected        = primaryExpected,
                DeviationPercent = primaryDevPct,
                Band            = primaryBand,
                // Both bases
                ExpectedBySize        = row.Expected,   // as built = BySize
                ExpectedEqual         = equalExpected,
                ActualShare           = actualShare,
                ExpectedShareBySize   = expectedShareBySize,
                ExpectedShareEqual    = expectedShareEqual,
                DeviationPercentEqual = devPctEqual,
                BandEqual             = bandEqual,
            });
        }
        return result;
    }

    // -----------------------------------------------------------------------------------
    // Deviation math
    // -----------------------------------------------------------------------------------

    private (decimal? deviationPct, DeviationBand band) ComputeDeviation(decimal actual, decimal expected)
    {
        if (expected <= 0m) return (null, DeviationBand.NoTarget);

        var pct = (actual - expected) / expected * 100m;

        DeviationBand band = pct switch
        {
            <= -25m => DeviationBand.Under,
            <= -10m => DeviationBand.SoftUnder,
            <  10m => DeviationBand.Balanced,
            <  25m => DeviationBand.SoftOver,
            _ => DeviationBand.Over
        };
        // <= 10 should be the same as Balanced — the >= -10 lower bound is already filtered above.
        // The middle band -10..+10 catches anything between -10 (exclusive) and +10 (exclusive).
        // Edge cases: pct == -10 -> SoftUnder, pct == +10 -> SoftOver, pct == 0 -> Balanced.

        return (pct, band);
    }

    /// <summary>
    /// Spread index = mean absolute deviation %. Lower is fairer; 0 means perfect distribution.
    /// We deliberately use mean-absolute rather than stdev so the number reads as "% off-target".
    /// </summary>
    private decimal ComputeSpreadIndex(List<JusticeRow> rows)
    {
        if (rows.Count == 0) return 0m;
        decimal sum = 0m;
        int count = 0;
        foreach (var r in rows)
        {
            if (!r.DeviationPercent.HasValue) continue;
            sum += Math.Abs(r.DeviationPercent.Value);
            count++;
        }
        if (count == 0) return 0m;
        return Math.Round(sum / count / 100m, 2); // express as 0..1 fraction
    }

    /// <summary>
    /// Maps spread index (0..1+) to a 5-circle severity glyph and a verdict label key.
    /// </summary>
    private (int severity, string labelKey) MapSpreadToSeverity(decimal spread)
    {
        if (spread <= 0.10m) return (0, "Justice_Spread_Excellent");
        if (spread <= 0.20m) return (1, "Justice_Spread_Good");
        if (spread <= 0.35m) return (2, "Justice_Spread_Moderate");
        if (spread <= 0.50m) return (3, "Justice_Spread_Uneven");
        return (4, "Justice_Spread_Severe");
    }

    // -----------------------------------------------------------------------------------
    // Avatar URL helper — see project-memory rule: cross-company avatars must use the user's
    // own CompanyId, NOT the caller's tenant.
    // -----------------------------------------------------------------------------------

    private static string? BuildAvatarThumbUrl(string? avatarFileName, int userCompanyId, int userId)
    {
        if (string.IsNullOrWhiteSpace(avatarFileName)) return null;
        // Convention: /avatars/{companyId}/{userId}_thumb.jpg (per project memory).
        return $"/avatars/{userCompanyId}/{userId}_thumb.jpg";
    }

    // ===============================================================================
    // Phase 2 — In-context calendar drawer
    // ===============================================================================

    public async Task<InContextJusticeViewModel> GetInContextViewAsync(JusticeQuery query, CancellationToken ct = default)
    {
        // Reuse the standalone Justice math — same Actual / Expected / spread computation.
        var view = await GetJusticeViewAsync(query, ct);

        var holes = await BuildWhereToFocusAsync(query, ct);
        var fullViewUrl = BuildFullViewUrl(query);

        return new InContextJusticeViewModel(
            Query: query,
            SpreadIndex: view.SpreadIndex,
            SpreadSeverity: view.SpreadSeverity,
            SpreadLabelKey: view.SpreadLabelKey,
            MostOver: view.MostOver,
            MostUnder: view.MostUnder,
            Rows: view.Rows,
            MaxRibbonValue: view.MaxRibbonValue,
            WhereToFocus: holes,
            FullViewUrl: fullViewUrl);
    }

    /// <summary>
    /// Dispatcher: routes to a kind-specific hole builder. For <see cref="JusticeWorkType.All"/>
    /// the three lists are concatenated and re-ranked by date so the user sees the next imminent
    /// holes regardless of work type.
    /// </summary>
    private async Task<List<UnfilledHole>> BuildWhereToFocusAsync(JusticeQuery q, CancellationToken ct)
    {
        return q.WorkType switch
        {
            JusticeWorkType.Shift => await BuildShiftHolesAsync(q, ct),
            JusticeWorkType.Chore => await BuildChoreHolesAsync(q, ct),
            JusticeWorkType.OnDuty => await BuildOnDutyHolesAsync(q, ct),
            JusticeWorkType.All => (await Task.WhenAll(
                                        BuildShiftHolesAsync(q, ct),
                                        BuildChoreHolesAsync(q, ct),
                                        BuildOnDutyHolesAsync(q, ct)))
                                    .SelectMany(h => h)
                                    .OrderBy(h => h.Date)
                                    .Take(WhereToFocusTopN)
                                    .ToList(),
            _ => new List<UnfilledHole>()
        };
    }

    private async Task<List<UnfilledHole>> BuildShiftHolesAsync(JusticeQuery q, CancellationToken ct)
    {
        var companyIds = await ResolveScopeCompanyIdsAsync(q, ct);
        if (companyIds.Length == 0) return new List<UnfilledHole>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var lookFrom = q.PeriodStart > today ? q.PeriodStart : today;

        // SECURITY: IgnoreQueryFilters required for cross-company aggregation across the molecule;
        // the caller has already gated this scope via IGrantService.GetAccessibleMoleculeIdsForGrantAsync.
        var query = _db.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => companyIds.Contains(si.CompanyId)
                         && si.WorkDate >= lookFrom
                         && si.WorkDate <= q.PeriodEnd);

        if (q.ExcludeExemptShifts)
        {
            query = query.Where(si => si.ShiftType.Key != ShiftType.KEY_HOME
                                    && si.ShiftType.Key != ShiftType.KEY_HOME_AM
                                    && si.ShiftType.Key != ShiftType.KEY_HOME_PM
                                    && si.ShiftType.Key != ShiftType.KEY_OFFLINE);
        }

        var rows = await query
            .Select(si => new
            {
                si.Id,
                si.CompanyId,
                si.WorkDate,
                si.ShiftType.JobTypeId,
                ShiftTypeName = si.ShiftType.Name,
                si.StaffingRequired,
                FilledCount = _db.ShiftAssignments
                    .IgnoreQueryFilters()
                    .Count(a => a.ShiftInstanceId == si.Id && a.UserId != null)
            })
            .Where(x => x.StaffingRequired > x.FilledCount)
            .OrderBy(x => x.WorkDate)
            .ThenByDescending(x => x.StaffingRequired - x.FilledCount)
            .Take(WhereToFocusTopN)
            .ToListAsync(ct);

        return rows.Select(x => new UnfilledHole(
            Kind: "shift",
            ShiftInstanceId: x.Id,
            Date: x.WorkDate,
            Label: $"{x.WorkDate:ddd MMM d} — {x.ShiftTypeName}",
            Deficit: x.StaffingRequired - x.FilledCount,
            CompanyId: x.CompanyId,
            JobTypeId: x.JobTypeId,
            DutyTypeValue: null,
            UserId: null
        )).ToList();
    }

    /// <summary>
    /// Phase 2d: chore holes are user-date pairs where a genuinely under-loaded user has no chore
    /// on a given date. "Under-loaded" reuses the existing Justice band math (Under or SoftUnder)
    /// so this list stays consistent with the verdict strip and Justice Table.
    /// </summary>
    private async Task<List<UnfilledHole>> BuildChoreHolesAsync(JusticeQuery q, CancellationToken ct)
    {
        var companyIds = await ResolveScopeCompanyIdsAsync(q, ct);
        if (companyIds.Length == 0) return new List<UnfilledHole>();

        // Separate target load — BuildWhereToFocusAsync runs independently of GetJusticeViewAsync.
        // SECURITY: IgnoreQueryFilters is required here — scope already gated at page-model
        // boundary; cross-company admins must see company-scoped overrides for all companies
        // in the viewed scope.
        var targets = await _db.JusticeTargets.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);

        // Build user-level rows per company in scope. Reuse BuildUsersInCompanyAsync to keep math
        // identical to the verdict strip. The query passed in must be at Company scope per the
        // existing precondition; iterate once per company under the molecule/area.
        var allRows = new List<(JusticeRow row, int companyId)>();
        foreach (var cid in companyIds)
        {
            var perCompanyQuery = q with { Scope = JusticeScope.Company, ScopeId = cid, Level = JusticeLevel.UsersInCompany, WorkType = JusticeWorkType.Chore };
            // Drill-capping is irrelevant for the internal "where to focus" hole-finding path —
            // it produces per-user holes, not a navigable comparison tier. Pass null (all drillable).
            var rows = await BuildUsersInCompanyAsync(perCompanyQuery, targets, drillableChildIds: null, ct);
            foreach (var r in rows) allRows.Add((r, cid));
        }

        // Filter to genuinely under-loaded users (negative deviation banded as Under or SoftUnder).
        var underloaded = allRows
            .Where(t => t.row.Band == DeviationBand.Under || t.row.Band == DeviationBand.SoftUnder)
            .ToList();
        if (underloaded.Count == 0) return new List<UnfilledHole>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var lookFrom = q.PeriodStart > today ? q.PeriodStart : today;
        var underloadedIds = underloaded.Select(t => t.row.Id).ToList();

        // Single batch query: which (user, date) pairs already have an active chore in the window.
        // SECURITY-AUDITED: IgnoreQueryFilters needed — chores are tenant-scoped by IBelongsToCompany
        // but Justice spans companies within a molecule. Scope is bounded by underloadedIds which
        // are themselves grant-authorized via the upstream scope check.
        var assignedSet = (await _db.Chores
            .IgnoreQueryFilters()
            .Where(c => underloadedIds.Contains(c.UserId)
                        && c.Date >= lookFrom
                        && c.Date <= q.PeriodEnd
                        && c.CanceledAt == null)
            .Select(c => new { c.UserId, c.Date })
            .ToListAsync(ct))
            .Select(x => (x.UserId, x.Date))
            .ToHashSet();

        // Cross-product (under-loaded user × upcoming date) minus already-assigned pairs.
        // Order by deviation magnitude descending so the most under-loaded surfaces first.
        var holes = new List<UnfilledHole>();
        var dayCount = lookFrom.DayNumber > q.PeriodEnd.DayNumber ? 0 : (q.PeriodEnd.DayNumber - lookFrom.DayNumber + 1);
        var orderedUsers = underloaded
            .OrderByDescending(t => Math.Abs(t.row.DeviationPercent ?? 0m))
            .ToList();

        foreach (var (row, cid) in orderedUsers)
        {
            for (int i = 0; i < dayCount; i++)
            {
                var d = lookFrom.AddDays(i);
                if (assignedSet.Contains((row.Id, d))) continue;
                holes.Add(new UnfilledHole(
                    Kind: "chore",
                    ShiftInstanceId: null,
                    Date: d,
                    Label: $"{d:ddd MMM d} — {row.Name}",
                    Deficit: 1,
                    CompanyId: cid,
                    JobTypeId: null,
                    DutyTypeValue: null,
                    UserId: row.Id));
                if (holes.Count >= WhereToFocusTopN) return holes;
            }
        }
        return holes;
    }

    /// <summary>
    /// Phase 2d: on-duty holes are (date × dutyType) pairs in the lookforward window with no active
    /// assignment. OnDuty types include built-ins (Hakam=0, Lead=1) plus active custom types from
    /// <c>OnDutyTypeConfig</c>. The hole list is bounded by the date window, not by company scope —
    /// OnDuty is global by design.
    /// </summary>
    private async Task<List<UnfilledHole>> BuildOnDutyHolesAsync(JusticeQuery q, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var lookFrom = q.PeriodStart > today ? q.PeriodStart : today;
        if (lookFrom > q.PeriodEnd) return new List<UnfilledHole>();

        // SECURITY-AUDITED: OnDutyTypeConfig is a global table by design (no IBelongsToCompany);
        // there is no tenant filter to bypass.
        var customTypes = await _db.OnDutyTypeConfigs
            .Where(t => t.IsActive)
            .Select(t => t.TypeValue)
            .ToListAsync(ct);

        // Built-in types: Hakam=0, Lead=1. Concat with active custom types and dedupe.
        var allTypes = new HashSet<int>(new[] { 0, 1 });
        foreach (var v in customTypes) allTypes.Add(v);

        // SECURITY-AUDITED: OnDuty has no CompanyId column (global table). IgnoreQueryFilters has
        // nothing to bypass; the date window bounds the query naturally.
        var assignedPairs = (await _db.OnDuties
            .IgnoreQueryFilters()
            .Where(o => o.CanceledAt == null
                        && o.Date >= lookFrom
                        && o.Date <= q.PeriodEnd)
            .Select(o => new { o.Date, TypeValue = (int)o.Type })
            .ToListAsync(ct))
            .Select(x => (x.Date, x.TypeValue))
            .ToHashSet();

        static string DutyTypeName(int tv) => tv == 0 ? "Hakam" : tv == 1 ? "Lead" : $"Type {tv}";

        var holes = new List<UnfilledHole>();
        var dayCount = q.PeriodEnd.DayNumber - lookFrom.DayNumber + 1;
        for (int i = 0; i < dayCount; i++)
        {
            var d = lookFrom.AddDays(i);
            foreach (var tv in allTypes.OrderBy(x => x))
            {
                if (assignedPairs.Contains((d, tv))) continue;
                holes.Add(new UnfilledHole(
                    Kind: "onduty",
                    ShiftInstanceId: null,
                    Date: d,
                    Label: $"{d:ddd MMM d} — {DutyTypeName(tv)}",
                    Deficit: 1,
                    CompanyId: null,
                    JobTypeId: null,
                    DutyTypeValue: tv,
                    UserId: null));
                if (holes.Count >= WhereToFocusTopN) return holes;
            }
        }
        return holes;
    }

    /// <summary>
    /// Build a deep-link to /Admin/Analytics pre-scoped to the same query.
    /// The full standalone page handles the comparative views (Spread Trend, Heatmap, etc.)
    /// that don't fit in a calendar drawer.
    /// </summary>
    private static string? BuildFullViewUrl(JusticeQuery q)
    {
        if (q.ScopeId is null) return null;
        var scope = q.Scope.ToString();
        var level = q.Level.ToString();
        var workType = q.WorkType.ToString();
        return $"/Admin/Analytics?scope={scope}&scopeId={q.ScopeId}&level={level}&workType={workType}" +
               $"&excludeExempt={q.ExcludeExemptShifts}" +
               $"&from={q.PeriodStart:yyyy-MM-dd}&to={q.PeriodEnd:yyyy-MM-dd}";
    }

    /// <summary>
    /// Resolves the JusticeQuery's scope to the concrete list of CompanyIds that fall under it.
    /// Used by BuildWhereToFocusAsync and the eligibility / preview helpers.
    /// </summary>
    private async Task<int[]> ResolveScopeCompanyIdsAsync(JusticeQuery q, CancellationToken ct)
    {
        if (q.ScopeId is null) return Array.Empty<int>();
        // SECURITY: IgnoreQueryFilters needed because the caller may be Owner/AreaAdmin viewing a
        // different tenant; scope authorization happens in the page model via IGrantService.
        return q.Scope switch
        {
            JusticeScope.Company => new[] { q.ScopeId.Value },
            JusticeScope.Molecule => await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.MoleculeId == q.ScopeId.Value)
                .Select(c => c.Id)
                .ToArrayAsync(ct),
            JusticeScope.Area => await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.MoleculeId != null
                            && _db.Molecules.IgnoreQueryFilters()
                                  .Any(m => m.Id == c.MoleculeId && m.AreaId == q.ScopeId.Value))
                .Select(c => c.Id)
                .ToArrayAsync(ct),
            _ => Array.Empty<int>()
        };
    }

    // ===============================================================================
    // Phase 2b — eligibility ranking
    // ===============================================================================

    public async Task<EligibleCandidatesViewModel> GetEligibleCandidatesAsync(HoleSelector hole, CancellationToken ct = default)
    {
        // Build the rows for the current scope so we know each candidate's deviation %.
        var view = await GetJusticeViewAsync(hole.ParentScope, ct);

        // Filter rows to users only — the parent scope must be UsersInCompany for ranking to make sense.
        // If the parent is Companies/Molecules level OR if UsersInCompany returned empty (because the
        // parent scope's Scope didn't match Company — e.g. OnDuty molecule-scoped ranking), fall back
        // to listing users in the hole's company / molecule scope. The deviation pills will read as
        // "no target" for these candidates because per-user rows weren't built — the ranking still
        // routes through real BusyService validation, which is the authority for hard-block decisions.
        var userIdsForRanking = (view.Query.Level == JusticeLevel.UsersInCompany && view.Rows.Count > 0)
            ? view.Rows.Select(r => r.Id).ToList()
            : await ResolveUsersInScopeAsync(hole, ct);

        if (userIdsForRanking.Count == 0)
            return new EligibleCandidatesViewModel(hole, new List<RankedCandidate>(), new List<RankedCandidate>());

        // Pre-load user data for all candidates (one round-trip).
        // SECURITY: IgnoreQueryFilters required for cross-company candidate display in molecule/area scopes.
        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => userIdsForRanking.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.DisplayName, u.AvatarFileName, u.CompanyId, u.JobTypeId })
            .AsNoTracking()
            .ToListAsync(ct);

        // For shift holes, narrow to users matching the JobType (same rule the assignment flow uses).
        if (hole.Kind == "shift" && hole.JobTypeId is int jt)
        {
            users = users.Where(u => u.JobTypeId == null || u.JobTypeId == jt).ToList();
        }

        // Build a quick lookup of (userId -> deviation row) so we can carry Justice context per candidate.
        var rowById = view.Rows.ToDictionary(r => r.Id, r => r);

        var candidates = new List<RankedCandidate>();
        var blocked = new List<RankedCandidate>();

        foreach (var u in users)
        {
            var row = rowById.GetValueOrDefault(u.Id);
            var actual = row?.Actual ?? 0m;
            var expected = row?.Expected ?? 0m;
            var dev = row?.DeviationPercent;
            var band = row?.Band ?? DeviationBand.NoTarget;

            CandidateEligibility eligibility = hole.Kind switch
            {
                "shift" when hole.ShiftInstanceId is int shiftInstanceId
                    => await CheckShiftEligibilityAsync(u.Id, shiftInstanceId),
                "chore" when hole.MoleculeId is int chMid
                    => await CheckChoreEligibilityAsync(u.Id, hole.Date, chMid, ct),
                "onduty" when hole.MoleculeId is int odMid && hole.DutyTypeValue is int dtv
                    => await CheckOnDutyEligibilityAsync(u.Id, hole.Date, (Models.Support.OnDutyType)dtv, odMid, ct),
                _ => new CandidateEligibility(false, null, new List<string>())
            };

            var candidate = new RankedCandidate(
                UserId: u.Id,
                DisplayName: u.DisplayName ?? $"#{u.Id}",
                AvatarUrl: BuildAvatarThumbUrl(u.AvatarFileName, u.CompanyId, u.Id),
                Actual: actual,
                Expected: expected,
                DeviationPercent: dev,
                Band: band,
                Eligibility: eligibility);

            if (eligibility.IsHardBlocked) blocked.Add(candidate);
            else candidates.Add(candidate);
        }

        // Rank visible candidates least-loaded-first.
        // Null deviation (no target) sorts last so they don't pretend to be best fit.
        candidates = candidates
            .OrderBy(c => c.DeviationPercent.HasValue ? 0 : 1)
            .ThenBy(c => c.DeviationPercent ?? 0m)
            .ToList();

        return new EligibleCandidatesViewModel(hole, candidates, blocked);
    }

    private async Task<CandidateEligibility> CheckShiftEligibilityAsync(int userId, int shiftInstanceId)
    {
        var validation = await _shiftAssignmentService.ValidateShiftAssignmentAsync(userId, shiftInstanceId);
        if (validation.Errors.Count > 0)
        {
            return new CandidateEligibility(
                IsHardBlocked: true,
                HardBlockReason: validation.Errors[0].Message,
                WarningKeys: validation.Warnings.Select(w => w.Message).ToList());
        }
        return new CandidateEligibility(
            IsHardBlocked: false,
            HardBlockReason: null,
            WarningKeys: validation.Warnings.Select(w => w.Message).ToList());
    }

    /// <summary>
    /// Phase 2d real validation for chores. Replaces the prior degraded path. Delegates entirely
    /// to <see cref="IChoreService.ValidateChoreAssignmentAsync"/> which routes through BusyService —
    /// same authority that the actual chore POST handler uses, so warnings here are the same
    /// warnings the user will see on "Make it real".
    /// </summary>
    private async Task<CandidateEligibility> CheckChoreEligibilityAsync(int userId, DateOnly date, int moleculeId, CancellationToken ct)
    {
        var v = await _choreService.ValidateChoreAssignmentAsync(userId, date, moleculeId, choreTypeId: null, overrideToken: null, ct);
        if (v.Errors.Count > 0)
        {
            return new CandidateEligibility(
                IsHardBlocked: true,
                HardBlockReason: v.Errors[0].Message,
                WarningKeys: v.Warnings.Select(w => w.Message).ToList());
        }
        return new CandidateEligibility(
            IsHardBlocked: false,
            HardBlockReason: null,
            WarningKeys: v.Warnings.Select(w => w.Message).ToList());
    }

    /// <summary>
    /// Phase 2d real validation for on-duty. Replaces the prior degraded path. The
    /// <paramref name="moleculeId"/> here is the same value the JS-side "Make it real" will pass
    /// in the POST body so HMAC override-token canonical forms match.
    /// </summary>
    private async Task<CandidateEligibility> CheckOnDutyEligibilityAsync(int userId, DateOnly date, Models.Support.OnDutyType type, int moleculeId, CancellationToken ct)
    {
        var v = await _onDutyService.ValidateOnDutyAssignmentAsync(userId, date, type, moleculeId, overrideToken: null, ct);
        if (v.Errors.Count > 0)
        {
            return new CandidateEligibility(
                IsHardBlocked: true,
                HardBlockReason: v.Errors[0].Message,
                WarningKeys: v.Warnings.Select(w => w.Message).ToList());
        }
        return new CandidateEligibility(
            IsHardBlocked: false,
            HardBlockReason: null,
            WarningKeys: v.Warnings.Select(w => w.Message).ToList());
    }

    /// <summary>
    /// When the parent scope is wider than UsersInCompany (e.g., CompaniesInMolecule),
    /// we still need a list of user candidates for the chosen hole. We constrain to users
    /// in the hole's CompanyId so chore/on-duty rankings stay within the entity's natural reach.
    /// </summary>
    private async Task<List<int>> ResolveUsersInScopeAsync(HoleSelector hole, CancellationToken ct)
    {
        var companyIds = hole.CompanyId.HasValue
            ? new[] { hole.CompanyId.Value }
            : await ResolveScopeCompanyIdsAsync(hole.ParentScope, ct);

        if (companyIds.Length == 0) return new List<int>();

        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    // ===============================================================================
    // Phase 2c — what-if preview (in-memory recompute, no DB write)
    // ===============================================================================

    public async Task<ImpactPreviewViewModel> PreviewImpactAsync(SimulatedAssignment sim, CancellationToken ct = default)
    {
        var beforeView = await GetJusticeViewAsync(sim.Hole.ParentScope, ct);

        // Locate the candidate's row to mutate. If the parent scope groups by user,
        // the candidate's row is directly identifiable by UserId. Otherwise (Company/Molecule level)
        // we fall back to attributing the +1 to the candidate's company/molecule row.
        var rows = beforeView.Rows.ToList();
        var candidateRow = beforeView.Query.Level == JusticeLevel.UsersInCompany
            ? rows.FirstOrDefault(r => r.Id == sim.UserId)
            : await ResolveCandidateAggregateRowAsync(sim, rows, ct);

        if (candidateRow is null)
        {
            // Candidate not in the visible scope — nothing to preview.
            return new ImpactPreviewViewModel(
                SpreadIndexBefore: beforeView.SpreadIndex,
                SpreadSeverityBefore: beforeView.SpreadSeverity,
                SpreadIndexAfter: beforeView.SpreadIndex,
                SpreadSeverityAfter: beforeView.SpreadSeverity,
                CandidateActualBefore: 0m,
                CandidateActualAfter: 0m,
                CandidateDeviationBefore: null,
                CandidateDeviationAfter: null);
        }

        var (devBefore, _) = ComputeDeviation(candidateRow.Actual, candidateRow.Expected);
        var newActual = candidateRow.Actual + 1m;
        var (devAfter, bandAfter) = ComputeDeviation(newActual, candidateRow.Expected);

        // Build the after-view rows by replacing only the candidate's row in-place.
        var afterRows = rows.Select(r => r.Id == candidateRow.Id
            ? r with { Actual = newActual, DeviationPercent = devAfter, Band = bandAfter }
            : r).ToList();

        var afterValidRows = afterRows.Where(r => r.Band != DeviationBand.NoTarget && r.DeviationPercent.HasValue).ToList();
        var afterSpread = ComputeSpreadIndex(afterValidRows);
        var (afterSeverity, _) = MapSpreadToSeverity(afterSpread);

        return new ImpactPreviewViewModel(
            SpreadIndexBefore: beforeView.SpreadIndex,
            SpreadSeverityBefore: beforeView.SpreadSeverity,
            SpreadIndexAfter: afterSpread,
            SpreadSeverityAfter: afterSeverity,
            CandidateActualBefore: candidateRow.Actual,
            CandidateActualAfter: newActual,
            CandidateDeviationBefore: devBefore,
            CandidateDeviationAfter: devAfter);
    }

    /// <summary>
    /// When the parent scope is at Company / Molecule level, the visible rows are aggregates.
    /// For the what-if delta we attribute the candidate's +1 to whichever aggregate row contains them.
    /// </summary>
    private async Task<JusticeRow?> ResolveCandidateAggregateRowAsync(SimulatedAssignment sim, List<JusticeRow> rows, CancellationToken ct)
    {
        // AppUser has no Company navigation; join manually.
        var user = await (
            from u in _db.Users.IgnoreQueryFilters()
            join c in _db.Companies.IgnoreQueryFilters() on u.CompanyId equals c.Id
            where u.Id == sim.UserId
            select new { u.CompanyId, c.MoleculeId }).FirstOrDefaultAsync(ct);
        if (user is null) return null;

        return sim.Hole.ParentScope.Level switch
        {
            JusticeLevel.CompaniesInMolecule => rows.FirstOrDefault(r => r.Id == user.CompanyId),
            JusticeLevel.MoleculesInArea when user.MoleculeId is int mid => rows.FirstOrDefault(r => r.Id == mid),
            _ => null
        };
    }
}
