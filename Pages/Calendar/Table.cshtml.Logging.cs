using Microsoft.Extensions.Logging;

namespace ShiftManager.Pages.Calendar;

// Source-generated LoggerMessage delegates for TableModel (closes 39 sites of F-C-013).
// EventId range 4000-4099 reserved. 14 unique methods cover 39 call sites by
// parameterizing repeated boilerplate ({Action} placeholder for catch-and-notify
// patterns that recurred 18+10 times). Range allocations:
//   4000-4009: Page lifecycle (OnGet date parsing + initial load) — 4 methods, 4 sites
//   4010-4019: Action error catches — 2 methods, 18 sites (15 simple + 3 with InstanceId)
//   4020-4029: Calendar notification dispatch failures — 1 method, 10 sites
//   4030-4039: Information logs — 6 methods, 6 sites (each unique)
//   4040-4049: Misc validation — 1 method, 1 site
public partial class TableModel
{
    // ── Page lifecycle ─────────────────────────────────────────────────────

    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning,
        Message = "Invalid start date year: {Start}, defaulting to current week")]
    private static partial void LogInvalidStartDateYear(ILogger logger, string? start);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Invalid start date format: {Start}, defaulting to current week")]
    private static partial void LogInvalidStartDateFormat(ILogger logger, string? start);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information,
        Message = "Loaded {Count} shift types, molecule mode: {IsMoleculeMode}")]
    private static partial void LogShiftTypesLoaded(ILogger logger, int count, bool isMoleculeMode);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning,
        Message = "No shift types found - calendar will be empty")]
    private static partial void LogNoShiftTypes(ILogger logger);

    // ── Action error catches (shared {Action} placeholder) ─────────────────

    [LoggerMessage(EventId = 4010, Level = LogLevel.Error,
        Message = "Error {Action}")]
    private static partial void LogErrorAction(ILogger logger, System.Exception ex, string action);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Error,
        Message = "Error {Action} {InstanceId}")]
    private static partial void LogErrorActionInstance(ILogger logger, System.Exception ex, string action, int instanceId);

    // ── Calendar notification dispatch failures (shared {Action} placeholder) ──

    [LoggerMessage(EventId = 4020, Level = LogLevel.Warning,
        Message = "Failed to send calendar notification for {Action}")]
    private static partial void LogCalendarNotificationFailed(ILogger logger, System.Exception ex, string action);

    // ── Information logs (each unique) ─────────────────────────────────────

    [LoggerMessage(EventId = 4030, Level = LogLevel.Information,
        Message = "Auto-detached ShiftInstance {InstanceId} from Program {ProgramId} due to manual staffing change")]
    private static partial void LogAutoDetachedFromProgram(ILogger logger, int instanceId, int? programId);

    [LoggerMessage(EventId = 4031, Level = LogLevel.Information,
        Message = "Deleted shift instance {InstanceId} with {AssignmentCount} assignments")]
    private static partial void LogShiftInstanceDeleted(ILogger logger, int instanceId, int assignmentCount);

    [LoggerMessage(EventId = 4032, Level = LogLevel.Information,
        Message = "Created custom shift type {ShiftTypeId} with name '{Name}' for company {CompanyId}")]
    private static partial void LogCustomShiftTypeCreated(ILogger logger, int shiftTypeId, string name, int companyId);

    [LoggerMessage(EventId = 4033, Level = LogLevel.Information,
        Message = "Detached ShiftInstance {InstanceId} from Program. Reason: {Reason}")]
    private static partial void LogShiftInstanceDetached(ILogger logger, int instanceId, string reason);

    [LoggerMessage(EventId = 4034, Level = LogLevel.Information,
        Message = "Reset ShiftInstance {InstanceId} to Program {ProgramId} defaults")]
    private static partial void LogShiftInstanceReset(ILogger logger, int instanceId, int? programId);

    [LoggerMessage(EventId = 4035, Level = LogLevel.Information,
        Message = "Fill range completed: {Created} created, {Updated} updated using mode {Mode}")]
    private static partial void LogFillRangeCompleted(ILogger logger, int created, int updated, string mode);

    // ── Misc validation ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 4040, Level = LogLevel.Warning,
        Message = "Invalid target date: {Date}")]
    private static partial void LogInvalidTargetDate(ILogger logger, string? date);
}
