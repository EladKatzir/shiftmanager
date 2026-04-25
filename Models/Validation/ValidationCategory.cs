namespace ShiftManager.Models.Validation;

/// <summary>
/// Category of validation check for UI grouping, override scoping, and cross-service classification.
///
/// Categories starting with shift-domain values (JobType ... HomeConflict) originated from
/// ShiftAssignmentService and are kept stable for backward compatibility.
///
/// Cross-cutting categories (Authorization ... Quota) were added as part of the project-wide
/// error-handling overhaul to support the OperationResult pattern in non-shift services.
/// </summary>
public enum ValidationCategory
{
    // Shift-domain categories (original)
    JobType,
    ShiftGrouping,
    WeeklyHours,
    RestHours,
    Trainee,
    Concurrency,
    TechShift,
    Overlap,
    TimeOff,
    ChoreConflict,
    OnDutyConflict,
    HomeConflict,

    // Cross-cutting categories (added for OperationResult)
    Authorization,
    NotFound,
    Persistence,
    Configuration,
    External,
    Quota
}
