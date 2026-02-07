using ShiftManager.Models;

namespace ShiftManager.Services;

public interface ITechShiftService
{
    /// <summary>
    /// Gets all users eligible for a specific tech shift type, based on their grants.
    /// For example, "HANAVA" returns users who have the "CanBeAssignedHanava" grant.
    /// </summary>
    Task<List<AppUser>> GetEligibleUsersForTechShiftAsync(string techShiftType, int? companyId = null);

    /// <summary>
    /// Checks whether a specific user is eligible for a given tech shift type.
    /// </summary>
    Task<bool> IsUserEligibleForTechShiftAsync(int userId, string techShiftType);

    /// <summary>
    /// Gets all known tech shift type keys (HANAVA, DELTA, YEKEV, MOVILTECH).
    /// </summary>
    Task<List<string>> GetTechShiftTypesAsync();
}
