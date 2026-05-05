using ShiftManager.Models.Validation;

namespace ShiftManager.Models.Support;

/// <summary>
/// Severity of the highest busy condition for a user on a given date.
/// None = not busy. Soft = warnings present (overrideable). Hard = blocking issues.
/// </summary>
public enum BusyLevel
{
    None,
    Soft,
    Hard
}

/// <summary>
/// One conflicting resource with enough context for the UI to render the
/// "User is busy at [resource] on [date] [time]. Proceed anyway?" sentence
/// without server-side string formatting.
/// </summary>
public record BusyConflictDetail(
    string Key,
    string Category,
    string ResourceType,        // "shift" | "chore" | "onduty" | "vacation" | "home"
    string? ResourceName,
    DateOnly Date,
    string? StartTime,          // "HH:mm" — null for all-day resources
    string? EndTime);

/// <summary>
/// Summary of a user's resource bookings on a single date — used to decorate
/// assignee-picker options with badges. Target-agnostic: a user with a shift
/// on the date carries HasShift=true regardless of what we are about to assign.
/// </summary>
public record BusySummary(
    int UserId,
    bool HasShift,
    BusyShiftHit? Shift,
    bool HasChore,
    string? ChoreTitle,
    bool HasOnDuty,
    OnDutyType? OnDutyType,
    bool HasVacation,
    DateOnly? VacationEnd,
    BusyLevel Highest);

/// <summary>
/// Subset of a user's existing shift assignment relevant to busy detection.
/// </summary>
public record BusyShiftHit(
    string Name,
    string Start,               // "HH:mm"
    string End,
    bool IsHome,
    bool IsOffline);

/// <summary>
/// Discriminated target of an assignment validation — Shift, Chore, or OnDuty.
/// </summary>
public abstract record BusyTarget
{
    public sealed record Shift(int ShiftInstanceId) : BusyTarget;

    public sealed record Chore(
        DateOnly Date,
        int MoleculeId,
        int? ChoreTypeId = null) : BusyTarget;

    public sealed record OnDuty(
        DateOnly Date,
        OnDutyType Type,
        int MoleculeId) : BusyTarget;
}

/// <summary>
/// Validation result with errors (hard blocks) and warnings (overrideable).
/// </summary>
public record BusyValidation(
    bool CanProceed,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Warnings)
{
    public bool IsClean => Errors.Count == 0 && Warnings.Count == 0;
}
