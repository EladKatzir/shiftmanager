using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — ChoreCategory/UserChoreCategory/ChoreType are
// molecule-scoped (no tenant filter); membership writes are scoped by explicit userId/categoryId params.
// Callers (ChoreTypes admin / Admin Users) re-verify the EditChoreTypes grant against the molecule.
public class ChoreCategoryService : IChoreCategoryService
{
    private readonly AppDbContext _db;

    public ChoreCategoryService(AppDbContext db) => _db = db;

    public async Task<List<ChoreCategory>> GetCategoriesForMoleculeAsync(int moleculeId, bool includeInactive = false)
    {
        var q = _db.ChoreCategories.Where(c => c.MoleculeId == moleculeId);
        if (!includeInactive)
            q = q.Where(c => c.IsActive);
        return await q.OrderBy(c => c.SortOrder).ThenBy(c => c.DisplayName).ToListAsync();
    }

    public Task<ChoreCategory?> GetCategoryAsync(int categoryId)
        => _db.ChoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId);

    public async Task<ChoreCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null)
    {
        name = name.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var exists = await _db.ChoreCategories.AnyAsync(c => c.MoleculeId == moleculeId && c.Name == name);
        if (exists)
            return null;

        var nextSort = await _db.ChoreCategories.Where(c => c.MoleculeId == moleculeId)
            .Select(c => (int?)c.SortOrder).MaxAsync() ?? 0;

        var category = new ChoreCategory
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            SortOrder = nextSort + 1,
            IsActive = true
        };
        _db.ChoreCategories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task<bool> RenameAsync(int categoryId, string name, string displayName, string? color)
    {
        var category = await _db.ChoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category == null)
            return false;

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!string.Equals(category.Name, name, StringComparison.Ordinal))
        {
            var clash = await _db.ChoreCategories
                .AnyAsync(c => c.MoleculeId == category.MoleculeId && c.Name == name && c.Id != categoryId);
            if (clash)
                return false;
        }

        category.Name = name;
        category.DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        category.Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int categoryId)
    {
        var category = await _db.ChoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category == null)
            return false;

        // FK behavior handles the rest: UserChoreCategory cascades; ChoreType.ChoreCategoryId is set null.
        _db.ChoreCategories.Remove(category);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(int ChoreTypeCount, int MemberCount)> GetUsageAsync(int categoryId)
    {
        var choreTypeCount = await _db.ChoreTypes.CountAsync(ct => ct.ChoreCategoryId == categoryId);
        var memberCount = await _db.UserChoreCategories.CountAsync(m => m.ChoreCategoryId == categoryId);
        return (choreTypeCount, memberCount);
    }

    public async Task<bool> AssignChoreTypeAsync(int choreTypeId, int? categoryId)
    {
        // SECURITY-AUDITED: chore types are not tenant-filtered; load by explicit id.
        var choreType = await _db.ChoreTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == choreTypeId);
        if (choreType == null)
            return false;

        if (categoryId == null)
        {
            choreType.ChoreCategoryId = null;
            await _db.SaveChangesAsync();
            return true;
        }

        var category = await _db.ChoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId.Value);
        if (category == null)
            return false;

        // ChoreType has a direct MoleculeId (no company-scope fallback) — the category must match it.
        if (choreType.MoleculeId != category.MoleculeId)
            return false;

        choreType.ChoreCategoryId = categoryId;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<int>> GetUserCategoryIdsAsync(int userId)
        => await _db.UserChoreCategories
            .Where(m => m.UserId == userId)
            .Select(m => m.ChoreCategoryId)
            .ToListAsync();

    public async Task SetUserCategoriesAsync(int userId, IReadOnlyCollection<int> categoryIds)
    {
        var desired = categoryIds.Distinct().ToHashSet();

        // Only keep ids that actually exist (defensive against stale/forged ids).
        if (desired.Count > 0)
        {
            var valid = await _db.ChoreCategories
                .Where(c => desired.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync();
            desired = valid.ToHashSet();
        }

        var existing = await _db.UserChoreCategories
            .Where(m => m.UserId == userId)
            .ToListAsync();
        var existingIds = existing.Select(m => m.ChoreCategoryId).ToHashSet();

        var toRemove = existing.Where(m => !desired.Contains(m.ChoreCategoryId)).ToList();
        if (toRemove.Count > 0)
            _db.UserChoreCategories.RemoveRange(toRemove);

        foreach (var id in desired.Where(id => !existingIds.Contains(id)))
            _db.UserChoreCategories.Add(new UserChoreCategory { UserId = userId, ChoreCategoryId = id });

        await _db.SaveChangesAsync();
    }
}
