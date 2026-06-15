namespace ShiftManager.Models.Support;

/// <summary>
/// Sensitive personal attribute used ONLY for gender-segregated chore eligibility
/// (male-only / female-only chores). Unspecified is the backfill default and means
/// "not recorded". A gender-restricted chore produces an OVERRIDEABLE WARNING for any
/// non-matching user (including Unspecified) — the manager may override (decided 2026-06-14).
/// </summary>
public enum Gender
{
    Unspecified = 0,
    Male = 1,
    Female = 2
}
