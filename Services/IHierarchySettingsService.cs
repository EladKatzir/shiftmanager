namespace ShiftManager.Services;

public interface IHierarchySettingsService
{
    /// <summary>
    /// Gets the effective settings for a company, resolving the cascade from Area → Molecule → Company.
    /// </summary>
    Task<EffectiveSettings> GetEffectiveSettingsAsync(int companyId);

    /// <summary>
    /// Gets settings for a molecule, resolving the cascade from Area → Molecule.
    /// </summary>
    Task<EffectiveSettings> GetMoleculeSettingsAsync(int moleculeId);

    /// <summary>
    /// Gets the base settings for an area.
    /// </summary>
    Task<AreaSettingsDto?> GetAreaSettingsAsync(int areaId);

    /// <summary>
    /// Updates area settings.
    /// </summary>
    Task<bool> UpdateAreaSettingsAsync(int areaId, int restHours, int weeklyCap, int updatedByUserId);

    /// <summary>
    /// Gets molecule settings overrides (null values mean inherit).
    /// </summary>
    Task<MoleculeSettingsDto?> GetMoleculeSettingsOverrideAsync(int moleculeId);

    /// <summary>
    /// Updates molecule settings overrides.
    /// </summary>
    Task<bool> UpdateMoleculeSettingsAsync(int moleculeId, int? restHoursOverride, int? weeklyCapOverride, int updatedByUserId);

    /// <summary>
    /// Gets company settings overrides (null values mean inherit).
    /// </summary>
    Task<CompanySettingsDto?> GetCompanySettingsOverrideAsync(int companyId);

    /// <summary>
    /// Updates company settings overrides.
    /// </summary>
    Task<bool> UpdateCompanySettingsAsync(int companyId, int? restHoursOverride, int? weeklyCapOverride, int updatedByUserId);
}

public record EffectiveSettings(
    int RestHours,
    int WeeklyCap,
    string RestHoursSource,     // "Area", "Molecule", or "Company"
    string WeeklyCapSource
);

public record AreaSettingsDto(
    int Id,
    int AreaId,
    string AreaName,
    int DefaultRestHours,
    int DefaultWeeklyCap,
    DateTime UpdatedAt,
    string? UpdatedByName
);

public record MoleculeSettingsDto(
    int Id,
    int MoleculeId,
    string MoleculeName,
    int? RestHoursOverride,
    int? WeeklyCapOverride,
    DateTime UpdatedAt,
    string? UpdatedByName
);

public record CompanySettingsDto(
    int Id,
    int CompanyId,
    string CompanyName,
    int? RestHoursOverride,
    int? WeeklyCapOverride,
    DateTime UpdatedAt,
    string? UpdatedByName
);
