namespace ShiftManager.Services;

public interface IUserCompanyTransferService
{
    /// <summary>Read-only: counts of what a move of <paramref name="userId"/> to
    /// <paramref name="destCompanyId"/> would clear/affect. Does not mutate anything.</summary>
    Task<MoveImpact> GetMoveImpactAsync(int userId, int destCompanyId);

    /// <summary>Transactional clean transfer. Assumes the caller has already authorized the move.</summary>
    Task<MoveResult> MoveUserToCompanyAsync(int userId, int destCompanyId, int actingAdminId);
}

public record MoveImpact(
    int FutureShifts,
    int PendingOrFutureTimeOff,
    int FutureChores,
    int OpenSwapRequests,
    int FutureOnDuty,
    int GameScores,
    int OwnedTeamCalendars,
    int ApiKeys,
    int ApproverRules,
    bool WillResetJobType,
    bool WillResetDepartment,
    bool WillResetPrimaryShiftType,
    bool WillResetHomeType,
    IReadOnlyList<string> DirectorCompaniesRemoved,
    IReadOnlyList<string> Warnings);

public record MoveResult(bool Success, string? ErrorKey = null, string? ErrorArg = null);
