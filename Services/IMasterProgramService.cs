using ShiftManager.Models;
using ShiftManager.Models.Results;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing MasterPrograms (collections of Programs forming complete weekly schedules).
/// A MasterProgram is a convenience wrapper for applying multiple Programs at once.
///
/// All write operations return <see cref="OperationResult"/> / <see cref="OperationResult{T}"/>
/// with localized error messages keyed under <c>Error_MasterProgramService_*</c>. Read operations
/// return nullable / list types directly (null = not found is acceptable for reads).
/// </summary>
public interface IMasterProgramService
{
    // ==================== CRUD Operations ====================

    /// <summary>
    /// Creates a new MasterProgram containing multiple Programs.
    /// </summary>
    Task<OperationResult<MasterProgram>> CreateMasterProgramAsync(
        int companyId,
        string name,
        string? description,
        List<int> programIds,
        int userId);

    /// <summary>
    /// Gets a single MasterProgram by ID with Items and nested Programs included.
    /// Returns null if not found.
    /// </summary>
    Task<MasterProgram?> GetMasterProgramAsync(int masterProgramId);

    /// <summary>
    /// Gets all MasterPrograms for a company.
    /// </summary>
    Task<List<MasterProgram>> GetCompanyMasterProgramsAsync(int companyId, bool includeInactive = false);

    /// <summary>
    /// Updates a MasterProgram's name, description, and included Programs.
    /// </summary>
    Task<OperationResult> UpdateMasterProgramAsync(
        int masterProgramId,
        string name,
        string? description,
        List<int> programIds,
        int userId);

    /// <summary>
    /// Soft deletes a MasterProgram (sets IsActive = false).
    /// Does NOT delete the Programs or ShiftInstances.
    /// </summary>
    Task<OperationResult> DeleteMasterProgramAsync(int masterProgramId, int userId);

    // ==================== Instance Generation ====================

    /// <summary>
    /// Generates ShiftInstances for ALL Programs in this MasterProgram.
    /// Returns the per-program instance lists on success. Partial failures (one program
    /// failing while others succeed) are recorded as <see cref="ValidationSeverity.Warning"/>
    /// issues on the result with the per-program key in the message; <c>Success</c> is true
    /// as long as the master program itself was found.
    /// </summary>
    Task<OperationResult<Dictionary<int, List<ShiftInstance>>>> GenerateFromMasterProgramAsync(
        int masterProgramId,
        DateOnly startDate,
        DateOnly endDate,
        bool overwriteExisting = false);

    /// <summary>
    /// Gets summary statistics for a MasterProgram.
    /// </summary>
    Task<OperationResult<MasterProgramSummary>> GetMasterProgramSummaryAsync(int masterProgramId);
}

/// <summary>
/// Summary statistics for a MasterProgram.
/// </summary>
public class MasterProgramSummary
{
    public int TotalPrograms { get; set; }
    public int UniqueShiftTypes { get; set; }
    public int TotalProgramDays { get; set; }
    public List<string> ShiftTypeNames { get; set; } = new();
}
