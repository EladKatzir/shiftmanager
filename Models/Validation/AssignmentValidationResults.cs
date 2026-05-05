namespace ShiftManager.Models.Validation;

/// <summary>
/// Result of <see cref="Services.IChoreService.ValidateChoreAssignmentAsync"/> — mirrors the
/// shift-assignment validation shape so callers (notably JusticeService eligibility ranking)
/// can render warnings/errors uniformly across all three work-type calendars.
///
/// The actual checks live in <see cref="Services.IBusyService"/>; this record is just the
/// outward-facing DTO that the chore service exposes.
/// </summary>
public record ChoreAssignmentValidation(
    bool CanAssign,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Warnings);

/// <summary>
/// Result of <see cref="Services.IOnDutyService.ValidateOnDutyAssignmentAsync"/> — same shape
/// as <see cref="ChoreAssignmentValidation"/> for symmetry. OnDuty has no MoleculeId column on
/// the entity itself; the moleculeId parameter on the service method is used purely for
/// HMAC override-token scoping (see BusyService.TargetCanonical).
/// </summary>
public record OnDutyAssignmentValidation(
    bool CanAssign,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Warnings);
