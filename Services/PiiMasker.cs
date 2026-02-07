namespace ShiftManager.Services;

/// <summary>
/// Utility for masking PII (Personally Identifiable Information) in log output.
/// Fixes E-02: PII in logs.
/// </summary>
public static class PiiMasker
{
    /// <summary>
    /// Masks an email address for log output.
    /// "user@domain.com" becomes "u***@d***.com"
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return "***";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return "***";

        var local = email[..atIndex];
        var domain = email[(atIndex + 1)..];
        var dotIndex = domain.LastIndexOf('.');

        var maskedLocal = local.Length > 1
            ? local[0] + new string('*', Math.Min(local.Length - 1, 3))
            : local;

        var maskedDomain = dotIndex > 0
            ? domain[0] + new string('*', Math.Min(dotIndex - 1, 3)) + domain[dotIndex..]
            : domain[0] + "***";

        return $"{maskedLocal}@{maskedDomain}";
    }
}
