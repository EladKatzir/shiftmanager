using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing MasterPrograms (collections of Programs forming complete weekly schedules).
/// A MasterProgram is a convenience wrapper for applying multiple Programs at once.
/// </summary>
public interface IMasterProgramService
{
    // ==================== CRUD Operations ====================

    /// <summary>
    /// Creates a new MasterProgram containing multiple Programs.
    /// </summary>
    /// <param name="companyId">Company ID</param>
    /// <param name="name">Display name (e.g., "Standard Week Schedule")</param>
    /// <param name="description">Optional description</param>
    /// <param name="programIds">List of Program IDs to include</param>
    /// <param name="userId">User creating the MasterProgram</param>
    /// <returns>The created MasterProgram with Items included</returns>
    Task<MasterProgram> CreateMasterProgramAsync(
        int companyId,
        string name,
        string? description,
        List<int> programIds,
        int userId);

    /// <summary>
    /// Gets a single MasterProgram by ID with Items and nested Programs included.
    /// </summary>
    Task<MasterProgram?> GetMasterProgramAsync(int masterProgramId);

    /// <summary>
    /// Gets all MasterPrograms for a company.
    /// </summary>
    /// <param name="companyId">Company ID</param>
    /// <param name="includeInactive">If false, only returns active MasterPrograms</param>
    Task<List<MasterProgram>> GetCompanyMasterProgramsAsync(int companyId, bool includeInactive = false);

    /// <summary>
    /// Updates a MasterProgram's name, description, and included Programs.
    /// </summary>
    /// <param name="masterProgramId">MasterProgram ID</param>
    /// <param name="name">New name</param>
    /// <param name="description">New description</param>
    /// <param name="programIds">New list of Program IDs (replaces existing)</param>
    /// <param name="userId">User making the update</param>
    Task UpdateMasterProgramAsync(
        int masterProgramId,
        string name,
        string? description,
        List<int> programIds,
        int userId);

    /// <summary>
    /// Soft deletes a MasterProgram (sets IsActive = false).
    /// Does NOT delete the Programs or ShiftInstances.
    /// </summary>
    Task DeleteMasterProgramAsync(int masterProgramId, int userId);

    // ==================== Instance Generation ====================

    /// <summary>
    /// Generates ShiftInstances for ALL Programs in this MasterProgram.
    /// Calls ShiftProgramService.GenerateInstancesAsync() for each Program.
    /// </summary>
    /// <param name="masterProgramId">MasterProgram ID</param>
    /// <param name="startDate">Start date (inclusive)</param>
    /// <param name="endDate">End date (inclusive)</param>
    /// <param name="overwriteExisting">If true, updates existing instances</param>
    /// <returns>Dictionary mapping ProgramId → List of created ShiftInstances</returns>
    Task<Dictionary<int, List<ShiftInstance>>> GenerateFromMasterProgramAsync(
        int masterProgramId,
        DateOnly startDate,
        DateOnly endDate,
        bool overwriteExisting = false);

    /// <summary>
    /// Gets summary statistics for a MasterProgram.
    /// </summary>
    /// <returns>Total Programs, Total unique ShiftTypes, Total ProgramDays across all Programs</returns>
    Task<MasterProgramSummary> GetMasterProgramSummaryAsync(int masterProgramId);
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
