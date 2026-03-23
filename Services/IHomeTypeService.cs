using ShiftManager.Models;

namespace ShiftManager.Services;

public record HomeTypeDto(int Id, string Name, string? NameHe, int MoleculeId, int UserCount, string? CycleDescription, bool IsActive);

public record GenerationConflict(DateOnly Date, int UserId, string UserName, string Reason);

public record GenerationResult(int Created, int Skipped, List<GenerationConflict> Conflicts);

public record DerivedRotationRule(
    int CycleWeeks,
    List<DayOfWeek> HomeDays,
    List<int> WeekOffsets,  // 0-based week indices within the cycle that are "home" weeks
    TimeOnly? StartTime,
    TimeOnly? EndTime
);

public enum RegenerationMode { KeepManualChanges, OverwriteAll }

public interface IHomeTypeService
{
    // CRUD
    Task<List<HomeTypeDto>> GetHomeTypesAsync(int moleculeId);
    Task<HomeType?> GetHomeTypeAsync(int id);
    Task<HomeType> CreateHomeTypeAsync(HomeType homeType);
    Task<bool> UpdateHomeTypeAsync(HomeType homeType);
    Task<bool> DeleteHomeTypeAsync(int id);

    // Rule derivation
    DerivedRotationRule? DeriveRuleFromPattern(List<DateOnly> paintedDates);

    // User assignment
    Task<List<AppUser>> GetUsersForHomeTypeAsync(int homeTypeId);
    Task AssignUsersAsync(int homeTypeId, List<int> userIds);
    Task UnassignUserAsync(int homeTypeId, int userId);

    // Generation
    Task<GenerationResult> GenerateHomeShiftsAsync(
        int homeTypeId,
        DateOnly startDate,
        DateOnly endDate,
        List<int> userIds,
        int createdByUserId,
        RegenerationMode mode = RegenerationMode.KeepManualChanges);

    // Per-user override
    Task SaveUserOverrideAsync(int homeTypeId, int userId, List<DateOnly> overrideDates, int createdByUserId);
    Task<List<DateOnly>> GetUserOverrideDatesAsync(int homeTypeId, int userId);
}
