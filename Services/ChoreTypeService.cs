using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

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
        return await _db.ChoreTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id);
    }

    public async Task<ChoreType> CreateAsync(int moleculeId, string name, string displayName, string? color, int userId)
    {
        var maxSortOrder = await _db.ChoreTypes
            .Where(ct => ct.MoleculeId == moleculeId)
            .MaxAsync(ct => (int?)ct.SortOrder) ?? 0;

        var choreType = new ChoreType
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            Color = color,
            SortOrder = maxSortOrder + 1,
            CreatedByUserId = userId
        };

        _db.ChoreTypes.Add(choreType);
        await _db.SaveChangesAsync();
        return choreType;
    }

    public async Task<ChoreType> UpdateAsync(int id, string displayName, string? color, int sortOrder)
    {
        var choreType = await _db.ChoreTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id);
        if (choreType == null)
            throw new ArgumentException("ChoreType not found", nameof(id));

        choreType.DisplayName = displayName;
        choreType.Color = color;
        choreType.SortOrder = sortOrder;

        await _db.SaveChangesAsync();
        return choreType;
    }

    public async Task<bool> DeactivateAsync(int id)
    {
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
