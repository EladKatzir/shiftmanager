using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — ShiftCategory/UserShiftCategory/ShiftType are
// molecule-scoped (no tenant filter); membership writes are scoped by explicit userId/categoryId params.
// Callers (Blueprints / Admin Users) re-verify the ManageShiftCategories grant against the molecule.
public class ShiftCategoryService : IShiftCategoryService
{
    private readonly AppDbContext _db;

    public ShiftCategoryService(AppDbContext db) => _db = db;

    public async Task<List<ShiftCategory>> GetCategoriesForMoleculeAsync(int moleculeId, bool includeInactive = false)
    {
        var q = _db.ShiftCategories.Where(c => c.MoleculeId == moleculeId);
        if (!includeInactive)
            q = q.Where(c => c.IsActive);
        return await q.OrderBy(c => c.SortOrder).ThenBy(c => c.DisplayName).ToListAsync();
    }

    public Task<ShiftCategory?> GetCategoryAsync(int categoryId)
        => _db.ShiftCategories.FirstOrDefaultAsync(c => c.Id == categoryId);

    public async Task<ShiftCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null)
    {
        name = name.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Enforce the (molecule, name) uniqueness before hitting the DB index so the caller gets a clean null.
        var exists = await _db.ShiftCategories.AnyAsync(c => c.MoleculeId == moleculeId && c.Name == name);
        if (exists)
            return null;

        var nextSort = await _db.ShiftCategories.Where(c => c.MoleculeId == moleculeId)
            .Select(c => (int?)c.SortOrder).MaxAsync() ?? 0;

        var category = new ShiftCategory
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            SortOrder = nextSort + 1,
            IsActive = true
        };
        _db.ShiftCategories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task<bool> RenameAsync(int categoryId, string name, string displayName, string? color)
    {
        var category = await _db.ShiftCategories.FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category == null)
            return false;

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!string.Equals(category.Name, name, StringComparison.Ordinal))
        {
            var clash = await _db.ShiftCategories
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
        var category = await _db.ShiftCategories.FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category == null)
            return false;

        // FK behavior handles the rest: UserShiftCategory cascades; ShiftType.CategoryId is set null.
        _db.ShiftCategories.Remove(category);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(int ShiftTypeCount, int MemberCount)> GetUsageAsync(int categoryId)
    {
        var shiftTypeCount = await _db.ShiftTypes.CountAsync(st => st.CategoryId == categoryId);
        var memberCount = await _db.UserShiftCategories.CountAsync(m => m.ShiftCategoryId == categoryId);
        return (shiftTypeCount, memberCount);
    }

    public async Task<bool> AssignShiftTypeAsync(int shiftTypeId, int? categoryId)
    {
        // SECURITY-AUDITED: shift types are not tenant-filtered; load by explicit id.
        var shiftType = await _db.ShiftTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);
        if (shiftType == null)
            return false;

        if (categoryId == null)
        {
            shiftType.CategoryId = null;
            await _db.SaveChangesAsync();
            return true;
        }

        var category = await _db.ShiftCategories.FirstOrDefaultAsync(c => c.Id == categoryId.Value);
        if (category == null)
            return false;

        // The category must belong to the molecule the shift type resolves to (direct, or via its company).
        var shiftMoleculeId = shiftType.MoleculeId
            ?? await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.Id == shiftType.CompanyId)
                .Select(c => c.MoleculeId)
                .FirstOrDefaultAsync();
        if (shiftMoleculeId != category.MoleculeId)
            return false;

        shiftType.CategoryId = categoryId;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<int>> GetUserCategoryIdsAsync(int userId)
        => await _db.UserShiftCategories
            .Where(m => m.UserId == userId)
            .Select(m => m.ShiftCategoryId)
            .ToListAsync();

    public async Task SetUserCategoriesAsync(int userId, IReadOnlyCollection<int> categoryIds)
    {
        var desired = categoryIds.Distinct().ToHashSet();

        // Only keep ids that actually exist (defensive against stale/forged ids).
        if (desired.Count > 0)
        {
            var valid = await _db.ShiftCategories
                .Where(c => desired.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync();
            desired = valid.ToHashSet();
        }

        var existing = await _db.UserShiftCategories
            .Where(m => m.UserId == userId)
            .ToListAsync();
        var existingIds = existing.Select(m => m.ShiftCategoryId).ToHashSet();

        // Remove memberships no longer desired.
        var toRemove = existing.Where(m => !desired.Contains(m.ShiftCategoryId)).ToList();
        if (toRemove.Count > 0)
            _db.UserShiftCategories.RemoveRange(toRemove);

        // Add newly desired memberships.
        foreach (var id in desired.Where(id => !existingIds.Contains(id)))
            _db.UserShiftCategories.Add(new UserShiftCategory { UserId = userId, ShiftCategoryId = id });

        await _db.SaveChangesAsync();
    }
}
