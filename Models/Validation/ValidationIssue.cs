using ShiftManager.Models.Support;

namespace ShiftManager.Models.Validation;

/// <summary>
/// A single validation issue produced by a service operation.
///
/// <para>
/// <b>Key</b> is a stable, machine-readable identifier (e.g. "JOB_TYPE_MISMATCH",
/// "Error_RoleService_UserNotFound"). UI clients may switch on this value to
/// render specific affordances (retry buttons, override flows, link to settings).
/// </para>
/// <para>
/// <b>Message</b> is a fully-localized, user-facing string. Producers MUST resolve this
/// via <c>IStringLocalizer&lt;SharedResource&gt;</c> before constructing the issue.
/// </para>
/// <para>
/// <b>Detail</b> is an optional structured payload used by busy/conflict warnings to
/// let the client render the localized "User is busy at X on Y" sentence without
/// server-side string formatting. Null when not applicable.
/// </para>
/// </summary>
public record ValidationIssue(
    string Key,
    string Message,
    ValidationSeverity Severity,
    ValidationCategory Category,
    BusyConflictDetail? Detail = null);
