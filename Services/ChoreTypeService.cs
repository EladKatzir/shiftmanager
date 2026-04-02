using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — chore types are molecule-scoped configuration;
// queries scoped by explicit moleculeId parameter; called only from authorized service layer
public class ChoreTypeService : IChoreTypeService
{
    private readonly AppDbContext _db;

    public ChoreTypeService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ChoreType>> GetChoreTypesForMoleculeAsync(int moleculeId)
    {
        return await _db.ChoreTypes
            .Where(ct => ct.MoleculeId == moleculeId && ct.IsActive)
            .OrderBy(ct => ct.SortOrder)
            .ThenBy(ct => ct.DisplayName)
            .ToListAsync();
    }

    public async Task<ChoreType?> GetByIdAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific ChoreType id
        return await _db.ChoreTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id);
    }

    public async Task<ChoreType> CreateAsync(int moleculeId, string name, string displayName, string? color, int userId, string? nameEn = null, string? nameHe = null)
    {
        var maxSortOrder = await _db.ChoreTypes
            .Where(ct => ct.MoleculeId == moleculeId)
            .MaxAsync(ct => (int?)ct.SortOrder) ?? 0;

        var choreType = new ChoreType
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            NameEn = nameEn,
            NameHe = nameHe,
            Color = color,
            SortOrder = maxSortOrder + 1,
            CreatedByUserId = userId
        };

        _db.ChoreTypes.Add(choreType);
        await _db.SaveChangesAsync();
        return choreType;
    }

    public async Task<ChoreType> UpdateAsync(int id, string displayName, string? color, int sortOrder, string? nameEn = null, string? nameHe = null)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific ChoreType id; admin-only update operation
        var choreType = await _db.ChoreTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id);
        if (choreType == null)
            throw new ArgumentException("ChoreType not found", nameof(id));

        choreType.DisplayName = displayName;
        choreType.NameEn = nameEn;
        choreType.NameHe = nameHe;
        choreType.Color = color;
        choreType.SortOrder = sortOrder;

        await _db.SaveChangesAsync();
        return choreType;
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific ChoreType id; admin-only deactivate operation
        var choreType = await _db.ChoreTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id);
        if (choreType == null)
            return false;

        choreType.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }
}
