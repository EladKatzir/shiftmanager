using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IChoreTypeService
{
    Task<List<ChoreType>> GetChoreTypesForMoleculeAsync(int moleculeId);
    Task<ChoreType?> GetByIdAsync(int id);
    Task<ChoreType> CreateAsync(int moleculeId, string name, string displayName, string? color, int userId, string? nameEn = null, string? nameHe = null);
    Task<ChoreType> UpdateAsync(int id, string displayName, string? color, int sortOrder, string? nameEn = null, string? nameHe = null);
    Task<bool> DeactivateAsync(int id);
}
