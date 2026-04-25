using ShiftManager.Models;
using ShiftManager.Models.Results;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing ShiftPrograms (weekly templates) and generating ShiftInstances.
/// Programs define WHEN a specific shift type runs (weekly mask) and DEFAULT staffing.
///
/// Migrated to <see cref="OperationResult"/> / <see cref="OperationResult{T}"/> as part of the
/// project-wide error-handling overhaul. Fire-and-forget callers (those that ignore the
/// return value) remain source-compatible because <c>await Task&lt;T&gt;</c> compiles whether or
/// not the value is captured.
/// </summary>
public interface IShiftProgramService
{
    // ==================== CRUD Operations ====================

    Task<OperationResult<ShiftProgram>> CreateProgramAsync(
        int companyId,
        int shiftTypeId,
        string name,
        List<DayOfWeek> days,
        int defaultStaffing,
        Dictionary<DayOfWeek, int>? perDayStaffing,
        int userId);

    Task<ShiftProgram?> GetProgramAsync(int programId);

    Task<List<ShiftProgram>> GetCompanyProgramsAsync(int companyId, bool includeInactive = false);

    Task<OperationResult> UpdateProgramAsync(
        int programId,
        string name,
        List<DayOfWeek> days,
        int defaultStaffing,
        Dictionary<DayOfWeek, int>? perDayStaffing,
        int userId);

    Task<OperationResult> DeleteProgramAsync(int programId, int userId);

    // ==================== Instance Generation ====================

    Task<OperationResult<List<ShiftInstance>>> GenerateInstancesAsync(
        int programId,
        DateOnly startDate,
        DateOnly endDate,
        bool overwriteExisting = false);

    Task<OperationResult<int>> ApplyProgramToDateRangeAsync(int programId, DateOnly startDate, DateOnly endDate, bool overwriteExisting = false);

    // ==================== Detachment & Reset ====================

    Task<OperationResult> DetachInstanceAsync(int instanceId, string overrideType);

    Task<OperationResult> ResetInstanceToProgramAsync(int instanceId);

    Task<List<ShiftInstance>> GetInstancesFromProgramAsync(int programId, DateOnly? startDate = null, DateOnly? endDate = null);
}
