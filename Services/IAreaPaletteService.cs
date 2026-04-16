using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Resolves the calendar-palette override for the current user's Area.
/// Added 2026-04-15 — drives the per-area color customization UI at
/// <c>/Admin/Organization/AreaPalette</c>.
///
/// Returns null when no override exists (caller falls back to tokens.css defaults).
/// </summary>
public interface IAreaPaletteService
{
    /// <summary>Palette for the current user's area (resolves via HierarchyService), or null if none.</summary>
    Task<AreaCalendarPalette?> GetPaletteForCurrentUserAsync();

    /// <summary>Palette for an explicit area, or null if none.</summary>
    Task<AreaCalendarPalette?> GetPaletteForAreaAsync(int areaId);

    /// <summary>
    /// Upsert the palette for <paramref name="areaId"/>. All provided hex values
    /// must already be validated (call <c>ColorUtilities.SanitizeHexColor</c> first).
    /// Pass null for a slot to clear it. Invalidates cache.
    /// </summary>
    Task SavePaletteAsync(int areaId, AreaCalendarPalette values, int actingUserId);

    /// <summary>Invalidate cache (safety hatch after manual DB edits).</summary>
    void InvalidateAreaCache(int areaId);
}
