using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface ISetupTaskService
{
    /// <summary>
    /// Generates setup tasks for a new molecule.
    /// </summary>
    Task<List<SetupTaskDto>> GenerateTasksForMoleculeAsync(int moleculeId, int assignToUserId);

    /// <summary>
    /// Generates setup tasks for a new company within a molecule.
    /// </summary>
    Task<List<SetupTaskDto>> GenerateTasksForCompanyAsync(int companyId, int assignToUserId);

    /// <summary>
    /// Gets pending setup tasks for a user.
    /// </summary>
    Task<List<SetupTaskDto>> GetPendingTasksAsync(int userId);

    /// <summary>
    /// Gets all setup tasks for a molecule.
    /// </summary>
    Task<List<SetupTaskDto>> GetTasksForMoleculeAsync(int moleculeId);

    /// <summary>
    /// Gets setup task progress summary for a molecule.
    /// </summary>
    Task<SetupProgressDto> GetProgressAsync(int moleculeId);

    /// <summary>
    /// Marks a task as completed.
    /// </summary>
    Task<bool> CompleteTaskAsync(int taskId, int completedByUserId);

    /// <summary>
    /// Marks a task as skipped.
    /// </summary>
    Task<bool> SkipTaskAsync(int taskId, int skippedByUserId);

    /// <summary>
    /// Updates task status.
    /// </summary>
    Task<bool> UpdateTaskStatusAsync(int taskId, SetupTaskStatus status, int updatedByUserId);
}

public record SetupTaskDto(
    int Id,
    SetupTaskType Type,
    string Title,
    string Description,
    SetupTaskStatus Status,
    int? MoleculeId,
    string? MoleculeName,
    int? CompanyId,
    string? CompanyName,
    int? JobTypeId,
    string? JobTypeName,
    int? SuggestedUserId,
    string? SuggestedUserName,
    string? SuggestionReason,
    int AssignedToUserId,
    string AssignedToUserName,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? CompletedByUserName
);

public record SetupProgressDto(
    int MoleculeId,
    string MoleculeName,
    int TotalTasks,
    int CompletedTasks,
    int PendingTasks,
    int SkippedTasks,
    int InProgressTasks,
    double CompletionPercent
);
