namespace ShiftManager.Models.Validation;

/// <summary>
/// Severity of a validation issue.
/// Errors are hard blocks; Warnings can be overridden with a signed token (where supported by the consuming service).
/// </summary>
public enum ValidationSeverity
{
    Error,
    Warning
}
