using System;
using System.Collections.Generic;

namespace ShiftManager.Models.Schedule;

/// <summary>
/// The personal-schedule item types that make up a user's unified timeline. Shared by the rich
/// <c>/My</c> timeline page AND the Home schedule-spine (Phase 6 merge) — both render the same
/// <see cref="TimelineItem"/> shape produced by <c>IPersonalTimelineService</c>.
/// </summary>
public enum ItemType { Vacation, OnDuty, Shift, Chore }

/// <summary>One entry in a user's personal schedule timeline (a shift, vacation, on-duty, or chore).</summary>
public record TimelineItem(
    int Id,
    ItemType Type,
    DateOnly Date,
    DateOnly? EndDate,
    string Title,
    string? TimeRange,
    string? Metadata,
    bool HasConflict,
    double Hours,
    string? SubType = null
);

/// <summary>Per-type counts + hours over the queried range (drives the /My stats chart).</summary>
public record StatsData(
    int VacationCount, double VacationHours,
    int OnDutyCount, double OnDutyHours,
    int ShiftCount, double ShiftHours,
    int ChoreCount, double ChoreHours
);

/// <summary>The result of a personal-timeline query: the ordered items plus their aggregate stats.</summary>
public record PersonalTimeline(IReadOnlyList<TimelineItem> Items, StatsData Stats);
