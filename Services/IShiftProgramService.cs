using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing ShiftPrograms (weekly templates) and generating ShiftInstances.
/// Programs define WHEN a specific shift type runs (weekly mask) and DEFAULT staffing.
/// </summary>
public interface IShiftProgramService
{
    // ==================== CRUD Operations ====================

    /// <summary>
    /// Creates a new Program with weekly mask and default staffing.
    /// </summary>
    /// <param name="companyId">Company ID</param>
    /// <param name="shiftTypeId">ShiftType this Program applies to</param>
    /// <param name="name">Display name for the Program (e.g., "Morning Shifts Mon-Fri")</param>
    /// <param name="days">List of days of the week this Program runs (0=Sunday, 6=Saturday)</param>
    /// <param name="defaultStaffing">Default number of people required per shift</param>
    /// <param name="perDayStaffing">Optional per-day staffing overrides (null for day = use default)</param>
    /// <param name="userId">User creating the Program</param>
    /// <returns>The created ShiftProgram with ProgramDays included</returns>
    Task<ShiftProgram> CreateProgramAsync(
        int companyId,
        int shiftTypeId,
        string name,
        List<DayOfWeek> days,
        int defaultStaffing,
        Dictionary<DayOfWeek, int>? perDayStaffing,
        int userId);

    /// <summary>
    /// Gets a single Program by ID with ShiftType and ProgramDays included.
    /// </summary>
    Task<ShiftProgram?> GetProgramAsync(int programId);

    /// <summary>
    /// Gets all Programs for a company, with ShiftTypes and ProgramDays included.
    /// </summary>
    /// <param name="companyId">Company ID</param>
    /// <param name="includeInactive">If false, only returns active Programs</param>
    Task<List<ShiftProgram>> GetCompanyProgramsAsync(int companyId, bool includeInactive = false);

    /// <summary>
    /// Updates a Program's name, weekly mask, and staffing settings.
    /// </summary>
    /// <param name="programId">Program ID to update</param>
    /// <param name="name">New name</param>
    /// <param name="days">New weekly mask (replaces existing ProgramDays)</param>
    /// <param name="defaultStaffing">New default staffing</param>
    /// <param name="perDayStaffing">Optional per-day overrides (null = use default)</param>
    /// <param name="userId">User making the update</param>
    Task UpdateProgramAsync(
        int programId,
        string name,
        List<DayOfWeek> days,
        int defaultStaffing,
        Dictionary<DayOfWeek, int>? perDayStaffing,
        int userId);

    /// <summary>
    /// Soft deletes a Program (sets IsActive = false).
    /// Does NOT delete existing ShiftInstances generated from this Program.
    /// </summary>
    Task DeleteProgramAsync(int programId, int userId);

    // ==================== Instance Generation ====================

    /// <summary>
    /// Generates ShiftInstances for all days in range that match the Program's weekly mask.
    /// Sets OriginalProgramId on each instance for "Reset to Program" functionality.
    /// </summary>
    /// <param name="programId">Program ID to generate from</param>
    /// <param name="startDate">Start date (inclusive)</param>
    /// <param name="endDate">End date (inclusive)</param>
    /// <param name="overwriteExisting">If true, updates existing instances; if false, skips existing</param>
    /// <returns>List of created/updated ShiftInstances</returns>
    Task<List<ShiftInstance>> GenerateInstancesAsync(
        int programId,
        DateOnly startDate,
        DateOnly endDate,
        bool overwriteExisting = false);

    /// <summary>
    /// Higher-level method: applies Program to date range and returns count of created instances.
    /// </summary>
    /// <returns>Number of ShiftInstances created (or updated if overwriteExisting = true)</returns>
    Task<int> ApplyProgramToDateRangeAsync(int programId, DateOnly startDate, DateOnly endDate, bool overwriteExisting = false);

    // ==================== Detachment & Reset ====================

    /// <summary>
    /// Marks a ShiftInstance as detached from its Program.
    /// Called when a user edits staffing, time, or name on an Operation.
    /// </summary>
    /// <param name="instanceId">ShiftInstance ID</param>
    /// <param name="overrideType">Which field was overridden: "Staffing", "Time", or "Name"</param>
    Task DetachInstanceAsync(int instanceId, string overrideType);

    /// <summary>
    /// Resets a detached ShiftInstance back to its Program's defaults.
    /// Restores staffing, clears overrides, sets IsDetached = false.
    /// </summary>
    /// <param name="instanceId">ShiftInstance ID to reset</param>
    Task ResetInstanceToProgramAsync(int instanceId);

    /// <summary>
    /// Gets all ShiftInstances that were generated from a specific Program.
    /// Used for "Show all Operations from this Program" view.
    /// </summary>
    Task<List<ShiftInstance>> GetInstancesFromProgramAsync(int programId, DateOnly? startDate = null, DateOnly? endDate = null);
}
