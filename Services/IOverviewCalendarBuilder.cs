using ShiftManager.Models;
using ShiftManager.ViewComponents;

namespace ShiftManager.Services;

/// <summary>
/// Builds the shared "user rows x date columns" Overview-shaped calendar ViewModel from a
/// given user set + date range. Extracted from <c>OverviewModel</c> (Task #9.2 — see
/// docs/superpowers/specs/2026-07-14-ui-batch-and-team-page-design.md) so both
/// <c>/Calendar/Overview</c> and the future <c>/Calendar/Team</c> page can render an identical
/// calendar by construction — both call this same service.
///
/// User SELECTION (which users go into <paramref name="users"/>) stays page-specific — callers
/// resolve their own user set (Overview by company+filter, Team by company+jobType) and pass it
/// in. This service only turns an already-resolved user set + date range into the
/// <see cref="ExcelCalendarTableViewModel"/> the ExcelCalendarTable view component renders.
/// </summary>
public interface IOverviewCalendarBuilder
{
    /// <summary>
    /// Builds the Overview-shaped calendar ViewModel: one <see cref="ExcelCalendarRow"/> per user
    /// in <paramref name="users"/>, with vacations/shifts/chores/on-duties/notes aggregated into
    /// each date cell for the given range. Produces <c>CalendarType == "overview"</c>,
    /// <c>RowMode == "Shifts"</c>, and <c>RowOrderContextKey == $"overview:{companyId}"</c>.
    /// </summary>
    /// <param name="companyId">Tenant/company scope — used for shift-name localization overrides and the row-order context key.</param>
    /// <param name="users">The already-resolved user set to render as rows (order is preserved into row order).</param>
    /// <param name="startDate">First date column (inclusive).</param>
    /// <param name="endDate">Last date column (inclusive).</param>
    /// <param name="viewMode">"week" | "next7" | "2weeks" | "month" — passed through to the ViewModel, not interpreted here (the caller already resolved start/end from it).</param>
    /// <param name="canEditNotes">Drives <c>IsReadOnly</c> (<c>= !canEditNotes</c>) on the returned ViewModel.</param>
    /// <param name="canEnterTimeOff">Sets <c>CanEnterTimeOff</c> on the returned ViewModel — lets editors enter time-off on people rows even on a read-only grid (Team).</param>
    Task<ExcelCalendarTableViewModel> BuildAsync(
        int companyId,
        IReadOnlyList<AppUser> users,
        DateOnly startDate,
        DateOnly endDate,
        string viewMode,
        bool canEditNotes,
        bool canEnterTimeOff = false);
}
