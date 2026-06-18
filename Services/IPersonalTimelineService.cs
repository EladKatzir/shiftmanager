using System;
using System.Threading.Tasks;
using ShiftManager.Models.Schedule;

namespace ShiftManager.Services;

/// <summary>
/// Builds a user's unified personal schedule timeline (shifts + vacations + on-duty + chores) over a
/// date range, with conflict detection and per-type stats. Extracted from the <c>/My</c> page model
/// so the SAME pipeline feeds both the rich <c>/My</c> timeline and the Home schedule-spine
/// (Phase 6 merge — "Home is the schedule-spine for everyone"). One source of truth for "what's on
/// my schedule," two presentations.
/// </summary>
public interface IPersonalTimelineService
{
    /// <summary>
    /// All of <paramref name="userId"/>'s schedule items whose date overlaps [rangeStart, rangeEnd],
    /// ordered by date, with same-date conflict flags and aggregate stats. Trainees also see the
    /// shifts they shadow. Returns empty (not null) if the user doesn't exist.
    /// </summary>
    Task<PersonalTimeline> GetTimelineAsync(int userId, DateOnly rangeStart, DateOnly rangeEnd);
}
