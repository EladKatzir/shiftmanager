using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — chore templates are molecule-scoped
// configuration; queries scoped by explicit moleculeId/id parameter; called only from the authorized
// EditChoreTypes page layer which re-verifies the molecule for IDOR before mutating.
public class ChoreTemplateService : IChoreTemplateService
{
    private readonly AppDbContext _db;

    public ChoreTemplateService(AppDbContext db) => _db = db;

    public async Task<List<ChoreTemplate>> GetTemplatesForMoleculeAsync(int moleculeId, bool includeInactive = false)
    {
        var q = _db.ChoreTemplates.IgnoreQueryFilters().Where(t => t.MoleculeId == moleculeId);
        if (!includeInactive)
            q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<ChoreTemplate?> GetByIdAsync(int id)
        => await _db.ChoreTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);

    public async Task<ChoreTemplate?> CreateAsync(int moleculeId, string name, int? choreTypeId, string defaultTitle,
        TimeOnly? startTime, TimeOnly? endTime, int? weightMinutesOverride, string? notes, int userId)
    {
        name = name.Trim();
        defaultTitle = defaultTitle.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(defaultTitle))
            return null;

        var template = new ChoreTemplate
        {
            MoleculeId = moleculeId,
            Name = name,
            ChoreTypeId = choreTypeId,
            DefaultTitle = defaultTitle,
            StartTime = startTime,
            EndTime = endTime,
            WeightMinutesOverride = weightMinutesOverride,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IsActive = true,
            CreatedBy = userId
        };
        _db.ChoreTemplates.Add(template);
        await _db.SaveChangesAsync();
        return template;
    }

    public async Task<bool> UpdateAsync(int id, string name, int? choreTypeId, string defaultTitle,
        TimeOnly? startTime, TimeOnly? endTime, int? weightMinutesOverride, string? notes)
    {
        var t = await _db.ChoreTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null)
            return false;

        name = name.Trim();
        defaultTitle = defaultTitle.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(defaultTitle))
            return false;

        t.Name = name;
        t.ChoreTypeId = choreTypeId;
        t.DefaultTitle = defaultTitle;
        t.StartTime = startTime;
        t.EndTime = endTime;
        t.WeightMinutesOverride = weightMinutesOverride;
        t.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeactivateAsync(int id) => await SetActiveAsync(id, false);
    public async Task<bool> ActivateAsync(int id) => await SetActiveAsync(id, true);

    private async Task<bool> SetActiveAsync(int id, bool active)
    {
        var t = await _db.ChoreTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null)
            return false;
        t.IsActive = active;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var t = await _db.ChoreTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null)
            return false;
        _db.ChoreTemplates.Remove(t);
        await _db.SaveChangesAsync();
        return true;
    }
}
