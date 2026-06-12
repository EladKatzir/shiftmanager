namespace ShiftManager.Services;

/// <summary>A shift type the user may select to monitor on the home dashboard.</summary>
public record MonitorableShiftType(int ShiftTypeId, string Name, string? Key);

/// <summary>A single user shown inside a shift cube on the home dashboard.</summary>
public record ShiftCubeUser(string Name, bool IsTrainee);

/// <summary>
/// Dashboard cube representing a single monitored shift type for the current day.
/// Always present (even when no instance exists today) so the admin still sees the row.
/// </summary>
public record ShiftCube(
    int ShiftTypeId,
    string ShiftName,
    int AssignedCount,
    int StaffingRequired,
    bool IsLiveNow,
    List<ShiftCubeUser> Users);

/// <summary>
/// Dashboard service for the "Who is on Shift" home widget.
/// Provides scope resolution, selection persistence, and live cube building.
/// </summary>
public interface IWhoIsOnShiftService
{
    /// <summary>
    /// Returns the shift types visible to the user (in their accessible molecules / area).
    /// Uses ViewShifts grant scope, mirroring the shift calendar's molecule resolution.
    /// </summary>
    Task<List<MonitorableShiftType>> GetMonitorableShiftTypesAsync(int userId);

    /// <summary>Returns the ShiftTypeIds the user has saved for monitoring.</summary>
    Task<List<int>> GetSelectedShiftIdsAsync(int userId);

    /// <summary>
    /// Whole-set replace: deletes all existing selections for the user and writes the new set.
    /// Mirrors CalendarRowOrderService.SaveOrderAsync whole-namespace-replace semantics.
    /// </summary>
    Task SaveSelectedShiftsAsync(int userId, IReadOnlyList<int> shiftTypeIds);

    /// <summary>
    /// Builds one ShiftCube per selected shift type showing who is on shift today.
    /// Always returns a cube per selection (empty users + count 0 when no instance today).
    /// IsLiveNow handles overnight shifts where End &lt; Start.
    /// </summary>
    Task<List<ShiftCube>> BuildWhoIsOnShiftAsync(int userId);
}
