namespace ShiftManager.Services;

/// <summary>One assignable candidate, uniform across the workforce + tech leaves.</summary>
public record EligibleCandidateDto(int Id, string Name, string? CompanyName);

/// <summary>
/// Router result. <see cref="Reason"/> is "category" | "sharedFallback" | "noCategory" and drives the
/// UI empty-state messaging (see the 3b plan's reason table).
/// </summary>
public record EligibleCandidatesResult(string Reason, IReadOnlyList<EligibleCandidateDto> Users);

/// <summary>
/// The single per-shift candidate router. Reads the company-level CategoryBasedShiftEligibility flag,
/// dispatches to the preserved workforce/tech leaf methods (does NOT merge their bodies), applies the
/// null-category fallback rules, and projects a uniform list. Authorization stays in the endpoint.
/// </summary>
public interface IShiftCandidateService
{
    Task<EligibleCandidatesResult> GetEligibleCandidatesAsync(
        int moleculeId, int shiftTypeId, int currentCompanyId, bool allowFallback = false);
}
