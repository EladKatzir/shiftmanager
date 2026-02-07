using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface IDutyRotationService
{
    Task<DutyRotation> CreateRotationAsync(string name, OnDutyType dutyType, RotationFrequency frequency, bool includeWeekends, int maxConsecutive, int createdBy);
    Task<DutyRotation?> GetRotationAsync(int rotationId);
    Task<List<DutyRotation>> GetAllRotationsAsync(bool includeInactive = false);
    Task<bool> UpdateRotationAsync(int rotationId, string? name, RotationFrequency? frequency, bool? includeWeekends, int? maxConsecutive, bool? isActive);
    Task<bool> DeleteRotationAsync(int rotationId);

    // Queue management
    Task<bool> AddUserToQueueAsync(int rotationId, int userId);
    Task<bool> RemoveUserFromQueueAsync(int rotationId, int userId);
    Task<bool> ReorderQueueAsync(int rotationId, List<int> userIdsInOrder);
    Task<List<DutyRotationEntry>> GetQueueAsync(int rotationId);

    // Assignment
    Task<(bool Success, string Message, OnDuty? OnDuty)> AssignNextAsync(int rotationId, DateOnly date, int assignedBy);
    Task<List<(DateOnly Date, int UserId, string? SkipReason)>> PreviewRotationAsync(int rotationId, DateOnly startDate, DateOnly endDate);
    Task<int> GenerateAssignmentsAsync(int rotationId, DateOnly startDate, DateOnly endDate, int generatedBy);

    // Logs
    Task<List<DutyRotationLog>> GetLogsAsync(int rotationId, DateOnly? startDate = null, DateOnly? endDate = null);
}
