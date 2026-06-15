using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Molecule-scoped CRUD over reusable <see cref="ChoreTemplate"/> definitions a manager stamps across a
/// date range. Carries NO schedule and does NOT stamp — stamping is <see cref="IChoreService.StampTemplateAsync"/>.
/// Mirrors <see cref="IChoreTypeService"/>. Page layer gates with the existing EditChoreTypes grant.
/// </summary>
public interface IChoreTemplateService
{
    Task<List<ChoreTemplate>> GetTemplatesForMoleculeAsync(int moleculeId, bool includeInactive = false);
    Task<ChoreTemplate?> GetByIdAsync(int id);
    Task<ChoreTemplate?> CreateAsync(int moleculeId, string name, int? choreTypeId, string defaultTitle,
        TimeOnly? startTime, TimeOnly? endTime, int? weightMinutesOverride, string? notes, int userId);
    Task<bool> UpdateAsync(int id, string name, int? choreTypeId, string defaultTitle,
        TimeOnly? startTime, TimeOnly? endTime, int? weightMinutesOverride, string? notes);
    Task<bool> DeactivateAsync(int id);
    Task<bool> ActivateAsync(int id);
    Task<bool> DeleteAsync(int id);
}
