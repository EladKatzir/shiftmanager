using ShiftManager.Models.Validation;

namespace ShiftManager.Models.Results;

/// <summary>
/// Project-wide operation result for service methods that do not return a value.
///
/// <para>
/// Use <see cref="Ok"/> for success. Use <see cref="Fail"/> when the operation failed
/// for a known, named reason (always with a localized message). Use
/// <see cref="FromIssues"/> when validation produced a structured list of issues.
/// </para>
/// <para>
/// The shift-assignment domain retains its own <c>ShiftAssignmentResult</c> /
/// <c>ShiftAssignmentValidation</c> records because they carry domain-specific fields
/// (e.g. <c>AssignmentId</c>). The two coexist; OperationResult is for everything else.
/// </para>
/// </summary>
public record OperationResult(
    bool Success,
    string? ErrorKey,
    string? ErrorMessage,
    IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Successful result with no issues.</summary>
    public static OperationResult Ok()
        => new(true, null, null, Array.Empty<ValidationIssue>());

    /// <summary>
    /// Failure with a single named reason. <paramref name="key"/> is a stable error code
    /// (e.g. <c>"Error_RoleService_UserNotFound"</c>); <paramref name="message"/> is the
    /// localized user-facing string.
    /// </summary>
    public static OperationResult Fail(string key, string message, params ValidationIssue[] issues)
        => new(false, key, message, issues);

    /// <summary>
    /// Build a result from a pre-collected list of <see cref="ValidationIssue"/>.
    /// Success is inferred from the absence of <see cref="ValidationSeverity.Error"/> entries.
    /// The first error issue (if any) populates <see cref="ErrorKey"/> / <see cref="ErrorMessage"/>.
    /// </summary>
    public static OperationResult FromIssues(IReadOnlyList<ValidationIssue> issues)
    {
        var firstError = issues.FirstOrDefault(i => i.Severity == ValidationSeverity.Error);
        return new(firstError == null, firstError?.Key, firstError?.Message, issues);
    }
}

/// <summary>
/// Project-wide operation result that carries a value on success.
/// </summary>
public record OperationResult<T>(
    bool Success,
    T? Value,
    string? ErrorKey,
    string? ErrorMessage,
    IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Successful result carrying <paramref name="value"/>.</summary>
    public static OperationResult<T> Ok(T value)
        => new(true, value, null, null, Array.Empty<ValidationIssue>());

    /// <summary>Failure with a named reason; <see cref="Value"/> is <c>default</c>.</summary>
    public static OperationResult<T> Fail(string key, string message, params ValidationIssue[] issues)
        => new(false, default, key, message, issues);

    /// <summary>
    /// Lift a non-generic <see cref="OperationResult"/> into a typed one, optionally attaching a value.
    /// </summary>
    public static OperationResult<T> FromResult(OperationResult r, T? value = default)
        => new(r.Success, value, r.ErrorKey, r.ErrorMessage, r.Issues);
}
