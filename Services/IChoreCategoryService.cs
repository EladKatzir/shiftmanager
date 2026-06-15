using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Manages molecule-scoped <see cref="ChoreCategory"/> entities: CRUD, the chore-type↔category
/// assignment (<see cref="ChoreType.ChoreCategoryId"/>), and per-user membership
/// (<see cref="UserChoreCategory"/>). Mirrors <see cref="IShiftCategoryService"/>. Categories are the
/// "who does chores" axis (alongside <see cref="AppUser.DoesChores"/>).
/// </summary>
public interface IChoreCategoryService
{
    // ---- Category queries ----
    Task<List<ChoreCategory>> GetCategoriesForMoleculeAsync(int moleculeId, bool includeInactive = false);
    Task<ChoreCategory?> GetCategoryAsync(int categoryId);

    // ---- Category CRUD ----
    /// <summary>Creates a category. Returns null if the (molecule, name) pair already exists.</summary>
    Task<ChoreCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null, string? nameEn = null, string? nameHe = null);
    /// <summary>Renames/recolors a category. Returns false on not-found or a (molecule, name) collision.</summary>
    Task<bool> RenameAsync(int categoryId, string name, string displayName, string? color, string? nameEn = null, string? nameHe = null);
    /// <summary>Hard-deletes a category: cascades its memberships and nulls its chore types' ChoreCategoryId (FK SetNull).</summary>
    Task<bool> DeleteAsync(int categoryId);

    /// <summary>Counts the chore types owned by, and users mapped to, a category (for delete-impact preview).</summary>
    Task<(int ChoreTypeCount, int MemberCount)> GetUsageAsync(int categoryId);

    // ---- Chore-type ↔ category assignment ----
    /// <summary>
    /// Sets (or clears, when categoryId is null) a chore type's owning category. The category must live
    /// in the same molecule as the chore type. Returns false on not-found / cross-molecule mismatch.
    /// </summary>
    Task<bool> AssignChoreTypeAsync(int choreTypeId, int? categoryId);

    // ---- Per-user membership ----
    Task<List<int>> GetUserCategoryIdsAsync(int userId);
    /// <summary>Replaces a user's chore-category memberships with exactly the supplied set.</summary>
    Task SetUserCategoriesAsync(int userId, IReadOnlyCollection<int> categoryIds);
}
