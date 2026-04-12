using System.Globalization;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Extensions;

/// <summary>
/// Display helpers for <see cref="ShiftType"/>.
/// </summary>
public static class ShiftTypeExtensions
{
    /// <summary>
    /// Returns the correct display name for a ShiftType, matching the three-layer lookup of the
    /// <c>&lt;loc&gt;</c> tag helper and then falling back to DB-stored <c>NameHe</c> / <c>NameEn</c>
    /// so that runtime-generated keys like <c>ShiftType_CUSTOM_*_Name</c> — which were never
    /// seeded into the <c>.resx</c> files — no longer leak as raw text in dropdowns and badges.
    ///
    /// Lookup order:
    ///   1. Company override (<see cref="ICompanyLocalizationService.GetOverrideValueAsync"/>)
    ///   2. Base <c>.resx</c> (only resolves for canonical seeded keys)
    ///   3. DB <c>NameHe</c> / <c>NameEn</c> selected by current UI culture
    ///   4. Computed <see cref="ShiftType.Name"/> (predefined English names from <c>Key</c>)
    /// </summary>
    public static async Task<string> GetDisplayNameAsync(
        this ShiftType st,
        IStringLocalizer<SharedResources> localizer,
        ICompanyLocalizationService? overrideService = null,
        int companyId = 0)
    {
        if (st == null) return string.Empty;

        var culture = CultureInfo.CurrentUICulture;
        var isHebrew = culture.TwoLetterISOLanguageName
            .Equals("he", StringComparison.OrdinalIgnoreCase);

        // 1. Company-scoped runtime override (editable via Blueprints "Edit Name" button).
        if (!string.IsNullOrWhiteSpace(st.NameKey) && overrideService != null && companyId > 0)
        {
            var overrideValue = await overrideService.GetOverrideValueAsync(
                companyId, culture.Name, st.NameKey);
            if (!string.IsNullOrEmpty(overrideValue))
                return overrideValue;
        }

        // 2. Base .resx — succeeds only for canonical seeded keys
        //    (ShiftType_MORNING_Name, ShiftType_OFFLINE_Name, etc.)
        if (!string.IsNullOrWhiteSpace(st.NameKey))
        {
            var localized = localizer[st.NameKey];
            if (!localized.ResourceNotFound && !string.IsNullOrEmpty(localized.Value))
                return localized.Value;
        }

        // 3. DB-backed culture-appropriate display name for user-created custom types.
        if (isHebrew && !string.IsNullOrWhiteSpace(st.NameHe))
            return st.NameHe;

        if (!isHebrew && !string.IsNullOrWhiteSpace(st.NameEn))
            return st.NameEn;

        // 4. Final fallback — the computed English Name (see ShiftType.Name switch).
        return st.Name;
    }
}
