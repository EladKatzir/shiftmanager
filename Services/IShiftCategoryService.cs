using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Manages molecule-scoped shift <see cref="ShiftCategory"/> entities: CRUD, the shift-type↔category
/// assignment (<see cref="ShiftType.CategoryId"/>), and per-user membership (<see cref="UserShiftCategory"/>).
/// Categories are the functional grouping that drives the by-shift and by-user calendar layouts and
/// supersedes the legacy <c>AppUser.PrimaryShiftTypeId</c>. Distinct from the geographic
/// <see cref="ShiftGrouping"/> axis.
/// </summary>
public interface IShiftCategoryService
{
    // ---- Category queries ----
    Task<List<ShiftCategory>> GetCategoriesForMoleculeAsync(int moleculeId, bool includeInactive = false);
    Task<ShiftCategory?> GetCategoryAsync(int categoryId);

    // ---- Category CRUD ----
    /// <summary>Creates a category. Returns null if the (molecule, name) pair already exists.</summary>
    Task<ShiftCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null);
    /// <summary>Renames/recolors a category. Returns false on not-found or a (molecule, name) collision.</summary>
    Task<bool> RenameAsync(int categoryId, string name, string displayName, string? color);
    /// <summary>Hard-deletes a category: cascades its memberships and nulls its shift types' CategoryId (FK SetNull).</summary>
    Task<bool> DeleteAsync(int categoryId);

    /// <summary>Counts the shift types owned by, and users mapped to, a category (for delete-impact preview).</summary>
    Task<(int ShiftTypeCount, int MemberCount)> GetUsageAsync(int categoryId);

    // ---- Shift-type ↔ category assignment ----
    /// <summary>
    /// Sets (or clears, when categoryId is null) a shift type's owning category. The category must live in
    /// the same molecule the shift type resolves to. Returns false on not-found / cross-molecule mismatch.
    /// </summary>
    Task<bool> AssignShiftTypeAsync(int shiftTypeId, int? categoryId);

    // ---- Per-user membership ----
    Task<List<int>> GetUserCategoryIdsAsync(int userId);
    /// <summary>Replaces a user's category memberships with exactly the supplied set (within one molecule).</summary>
    Task SetUserCategoriesAsync(int userId, IReadOnlyCollection<int> categoryIds);
}
