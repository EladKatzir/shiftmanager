using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// All inputs required to render the Justice analytics page.
/// Bound from the query string of /Admin/Analytics. Defaults filled in by the page model
/// when the user lands without parameters.
/// </summary>
public sealed record JusticeQuery(
    JusticeScope Scope,
    int? ScopeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    JusticeWorkType WorkType,
    bool ExcludeExemptShifts,
    JusticeLevel Level,
    FairnessBasis Basis = FairnessBasis.BySize);

/// <summary>
/// Top-level result for the Justice page.
/// Spread index summarises the imbalance over the rendered rows; lower is fairer.
/// </summary>
public sealed record JusticeViewModel(
    JusticeQuery Query,
    decimal SpreadIndex,
    int SpreadSeverity,                 // 0..4 — number of filled circles in 5-circle severity glyph
    string SpreadLabelKey,              // localization key for the verdict label
    JusticeRow? MostOver,
    JusticeRow? MostUnder,
    List<JusticeRow> Rows,
    decimal MaxRibbonValue);            // largest Actual across rows; used to scale the equity ribbon

/// <summary>
/// One row in the Justice table — represents a user, company, or molecule depending on Level.
/// </summary>
public sealed record JusticeRow(
    int Id,
    string Name,
    string? AvatarUrl,                  // populated only for UsersInCompany level; null otherwise
    decimal Actual,
    decimal Expected,
    decimal? DeviationPercent,          // null when Expected == 0 (no target / no capacity); render "no target" pill
    DeviationBand Band);

/// <summary>
/// Color/severity bucket for the deviation pill. Asymmetric on purpose:
/// under-loaded is "opportunity" (info-blue), over-loaded is "alarm" (danger-red).
/// </summary>
public enum DeviationBand
{
    NoTarget = -1,
    Under = 0,        // <= -25%
    SoftUnder = 1,    // -25% .. -10%
    Balanced = 2,     // -10% .. +10%
    SoftOver = 3,     // +10% .. +25%
    Over = 4          // >= +25%
}

// ===================================================================================
// Phase 2: in-context Justice for the calendar pages.
// Used by the slide-in drawer triggered by the [Justice] toolbar button on
// /Calendar/Shifts, /Calendar/Chores, /Calendar/OnCall.
// ===================================================================================

/// <summary>
/// Compact Justice payload for the calendar drawer. Same Justice math as the standalone page
/// but trimmed to what the drawer renders: verdict pieces + rows + a small "where to focus" list
/// of the next-N unfilled holes in the active scope.
/// </summary>
public sealed record InContextJusticeViewModel(
    JusticeQuery Query,
    decimal SpreadIndex,
    int SpreadSeverity,
    string SpreadLabelKey,
    JusticeRow? MostOver,
    JusticeRow? MostUnder,
    List<JusticeRow> Rows,
    decimal MaxRibbonValue,
    List<UnfilledHole> WhereToFocus,    // top-N upcoming unfilled cells in scope
    string? FullViewUrl);               // deep-link to /Admin/Analytics pre-scoped (or null)

/// <summary>
/// One unfilled cell on the calendar that the assigner could fix.
/// Renders in the "Where to focus" list and feeds the eligibility-ranking flow.
/// </summary>
public sealed record UnfilledHole(
    string Kind,                        // "shift" | "chore" | "onduty"
    int? ShiftInstanceId,               // populated for Kind="shift"
    DateOnly Date,
    string Label,                       // e.g., "Sat 18 — Night Shift"
    int Deficit,                        // StaffingRequired - FilledCount (or 1 for unassigned chore/onduty)
    int? CompanyId,                     // narrows the eligibility query
    int? JobTypeId,                     // narrows the eligibility query for shifts
    int? DutyTypeValue,                 // narrows for on-duty kinds
    int? UserId);                       // populated for Kind="chore"; lets the frontend build rowId="user-{UserId}"

// ===================================================================================
// Phase 2b: eligibility ranking + Phase 2c: what-if preview.
// ===================================================================================

/// <summary>
/// Identifies which hole the user wants to fill, plus the parent calendar's scope so the service
/// can rank candidates correctly.
/// </summary>
public sealed record HoleSelector(
    string Kind,                        // "shift" | "chore" | "onduty"
    int? ShiftInstanceId,
    DateOnly Date,
    int? CompanyId,
    int? JobTypeId,
    int? DutyTypeValue,
    int? MoleculeId,                    // required for chore/onduty BusyService validation; null for shifts (resolved via company join)
    JusticeQuery ParentScope);          // used to compute current Actual / Expected / spread

/// <summary>
/// Ranked candidate list returned by GetEligibleCandidatesAsync.
/// Candidates are ordered least-loaded-first (most negative deviation %).
/// HardBlocked candidates are returned in a separate list so the drawer can collapse them.
/// </summary>
public sealed record EligibleCandidatesViewModel(
    HoleSelector Hole,
    List<RankedCandidate> Candidates,           // visible, ranked best-fit first
    List<RankedCandidate> HardBlocked);         // collapsed at bottom; included for transparency

public sealed record RankedCandidate(
    int UserId,
    string DisplayName,
    string? AvatarUrl,
    decimal Actual,
    decimal Expected,
    decimal? DeviationPercent,
    DeviationBand Band,
    CandidateEligibility Eligibility);

/// <summary>
/// Per-candidate validation result. For shifts, comes from
/// <see cref="IShiftAssignmentService.ValidateShiftAssignmentAsync"/>.
/// For chores and on-duty (Phase 2d), comes from a degraded check that only verifies
/// vacation overlap and scope membership — the existing POST handlers re-validate the rest.
/// </summary>
public sealed record CandidateEligibility(
    bool IsHardBlocked,
    string? HardBlockReason,            // localization key OR a raw token already in the resx
    List<string> WarningKeys);          // localization keys for soft warnings (vacation overlap, etc.)

/// <summary>
/// Predicted impact of a hypothetical assignment, computed in-memory without writing.
/// </summary>
public sealed record SimulatedAssignment(
    int UserId,
    HoleSelector Hole);

public sealed record ImpactPreviewViewModel(
    decimal SpreadIndexBefore,
    int SpreadSeverityBefore,
    decimal SpreadIndexAfter,
    int SpreadSeverityAfter,
    decimal CandidateActualBefore,
    decimal CandidateActualAfter,
    decimal? CandidateDeviationBefore,
    decimal? CandidateDeviationAfter);
