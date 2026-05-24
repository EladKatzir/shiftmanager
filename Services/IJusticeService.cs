using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Decision-support analytics for the Justice page. Read-only in Phase 1; settings upserts
/// land in Phase 2 along with the simulator endpoints.
/// </summary>
public interface IJusticeService
{
    /// <summary>
    /// Build the full Justice view: rows at the requested level, spread index, most-over/most-under.
    /// Equivalent to calling the comparison-tier overload with <c>drillableChildIds: null</c>
    /// (every row drillable — backward compatible).
    /// </summary>
    Task<JusticeViewModel> GetJusticeViewAsync(JusticeQuery query, CancellationToken ct = default);

    /// <summary>
    /// A5 comparison-tier overload. Builds the same view as <see cref="GetJusticeViewAsync(JusticeQuery, CancellationToken)"/>
    /// but marks each row's <see cref="JusticeRow.IsDrillable"/> based on <paramref name="drillableChildIds"/>:
    /// a row is drillable iff <paramref name="drillableChildIds"/> is null OR contains the row's Id.
    /// ALL rows are still returned (the comparison tier is intact — capped users can READ siblings
    /// to rank them, but may only DRILL into their own subtree). Passing null reproduces the legacy
    /// behavior exactly (every row drillable).
    /// </summary>
    Task<JusticeViewModel> GetJusticeViewAsync(JusticeQuery query, IReadOnlyCollection<int>? drillableChildIds, CancellationToken ct = default);

    /// <summary>
    /// A6 sparkline overload. Same as the A5 overload but also populates <see cref="JusticeRow.Sparkline"/>
    /// on each row when <paramref name="includeSparklines"/> is <c>true</c>.
    /// When false the sparkline is omitted (null), incurring no extra DB cost — use this in
    /// the fast-path callers (calendar drawer, eligibility ranking, what-if preview).
    /// </summary>
    Task<JusticeViewModel> GetJusticeViewAsync(JusticeQuery query, IReadOnlyCollection<int>? drillableChildIds, bool includeSparklines, CancellationToken ct = default);

    /// <summary>
    /// A6: Computes <paramref name="buckets"/> consecutive calendar-month work-item counts,
    /// ending at the month that contains <paramref name="baseQuery"/>.PeriodEnd.
    /// Honoring the same work-type filter and exclusions as <see cref="GetJusticeViewAsync"/>.
    ///
    /// Return value: rowId → List&lt;decimal&gt; of length <paramref name="buckets"/>, ordered
    /// oldest → newest. Rows that had zero work in the entire span are included with all-zeros
    /// only if they appear in the scope; rows outside the scope are absent.
    ///
    /// IMPORTANT: does NOT issue per-month DB round-trips. Pulls the full (rowKey, Date) set
    /// for the span in one query per work-type and buckets in memory.
    /// </summary>
    Task<Dictionary<int, List<decimal>>> GetSparklineSeriesAsync(JusticeQuery baseQuery, int buckets, CancellationToken ct = default);

    /// <summary>
    /// Returns all configured targets (defaults + overrides) visible to the current tenant.
    /// Phase 1 callers use this only to display the read-only "current targets" summary;
    /// the editable settings modal lands in Phase 2.
    /// </summary>
    Task<List<JusticeTarget>> GetTargetsAsync(CancellationToken ct = default);

    // ===============================================================================
    // Phase 2 — in-context Justice for calendar pages.
    // ===============================================================================

    /// <summary>
    /// Builds the calendar-drawer payload: same Justice math as <see cref="GetJusticeViewAsync"/>
    /// plus a top-N "where to focus" list of upcoming unfilled holes in scope. Caller is
    /// responsible for scope-gating (ensuring the user has ViewJusticeTable for the requested scope).
    /// </summary>
    Task<InContextJusticeViewModel> GetInContextViewAsync(JusticeQuery query, CancellationToken ct = default);

    /// <summary>
    /// Phase 2b: eligibility ranking for one unfilled hole.
    /// Shifts use the existing <see cref="IShiftAssignmentService.ValidateShiftAssignmentAsync"/>.
    /// Chore + OnDuty use a degraded check (vacation overlap + scope membership only — Path C from the plan).
    /// Candidates are returned least-loaded-first based on the current Justice deviation %.
    /// </summary>
    Task<EligibleCandidatesViewModel> GetEligibleCandidatesAsync(HoleSelector hole, CancellationToken ct = default);

    /// <summary>
    /// Phase 2c: in-memory what-if. Recomputes the spread index assuming the candidate were
    /// assigned to the hole. Does NOT write to the database. The drawer's "Make it real" CTA
    /// performs the actual write by deep-linking into the existing calendar POST handler.
    /// </summary>
    Task<ImpactPreviewViewModel> PreviewImpactAsync(SimulatedAssignment sim, CancellationToken ct = default);
}
