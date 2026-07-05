namespace ShiftManager.Services;

public interface ICompanyMoleculeTransferService
{
    /// <summary>Read-only: counts of what moving <paramref name="companyId"/> to
    /// <paramref name="targetMoleculeId"/> would delete/affect. Does not mutate anything.</summary>
    Task<CompanyMoveImpact> GetMoveImpactAsync(int companyId, int targetMoleculeId);

    /// <summary>
    /// Transactional molecule re-scope of a whole company. Deletes the company's shift scheduling
    /// data and chores, resets its users' molecule-scoped config, rebuilds their grants for the new
    /// hierarchy, and repoints Company.MoleculeId. Vacations (TimeOffRequest) and accounts are kept.
    /// Assumes the caller has already authorized the move.
    /// </summary>
    Task<CompanyMoveResult> MoveCompanyToMoleculeAsync(int companyId, int targetMoleculeId, int actingAdminId);
}

public record CompanyMoveImpact(
    int ShiftInstances,
    int ShiftAssignments,
    int ShiftPrograms,
    int Chores,
    int AffectedUsers,
    int CompanyScopedShiftTypes,
    bool TargetMoleculeValid,
    IReadOnlyList<string> Warnings);

public record CompanyMoveResult(bool Success, string? ErrorKey = null, string? ErrorArg = null);
