using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class HierarchySettingsService : IHierarchySettingsService
{
    private readonly AppDbContext _db;
    private readonly ILogger<HierarchySettingsService> _logger;

    // Default values if no settings exist
    private const int DefaultRestHours = 11;
    private const int DefaultWeeklyCap = 60;

    public HierarchySettingsService(AppDbContext db, ILogger<HierarchySettingsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<EffectiveSettings> GetEffectiveSettingsAsync(int companyId)
    {
        // Load company and its hierarchy
        // SECURITY-AUDITED: SAFE — scoped by specific companyId parameter
        var company = await _db.Companies.IgnoreQueryFilters()
            .Include(c => c.Molecule)
                .ThenInclude(m => m!.Area)
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
        {
            return new EffectiveSettings(DefaultRestHours, DefaultWeeklyCap, "Default", "Default");
        }

        // Load all settings
        // SECURITY-AUDITED: SAFE — scoped by specific companyId parameter
        var companySettings = await _db.CompanySettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(cs => cs.CompanyId == companyId);

        MoleculeSettings? moleculeSettings = null;
        AreaSettings? areaSettings = null;

        if (company.MoleculeId.HasValue)
        {
            // SECURITY-AUDITED: SAFE — scoped by company's moleculeId (derived from companyId parameter)
            moleculeSettings = await _db.MoleculeSettings.IgnoreQueryFilters()
                .FirstOrDefaultAsync(ms => ms.MoleculeId == company.MoleculeId.Value);

            if (company.Molecule?.AreaId != null)
            {
                // SECURITY-AUDITED: SAFE — scoped by company's molecule's areaId (derived from companyId parameter)
                areaSettings = await _db.AreaSettings.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(a => a.AreaId == company.Molecule.AreaId);
            }
        }

        // Resolve cascade: Company → Molecule → Area → Default
        int restHours = DefaultRestHours;
        string restHoursSource = "Default";

        int weeklyCap = DefaultWeeklyCap;
        string weeklyCapSource = "Default";

        // Apply area settings first (lowest priority)
        if (areaSettings != null)
        {
            restHours = areaSettings.DefaultRestHours;
            restHoursSource = "Area";
            weeklyCap = areaSettings.DefaultWeeklyCap;
            weeklyCapSource = "Area";
        }

        // Apply molecule overrides
        if (moleculeSettings != null)
        {
            if (moleculeSettings.RestHoursOverride.HasValue)
            {
                restHours = moleculeSettings.RestHoursOverride.Value;
                restHoursSource = "Molecule";
            }
            if (moleculeSettings.WeeklyCapOverride.HasValue)
            {
                weeklyCap = moleculeSettings.WeeklyCapOverride.Value;
                weeklyCapSource = "Molecule";
            }
        }

        // Apply company overrides (highest priority)
        if (companySettings != null)
        {
            if (companySettings.RestHoursOverride.HasValue)
            {
                restHours = companySettings.RestHoursOverride.Value;
                restHoursSource = "Company";
            }
            if (companySettings.WeeklyCapOverride.HasValue)
            {
                weeklyCap = companySettings.WeeklyCapOverride.Value;
                weeklyCapSource = "Company";
            }
        }

        return new EffectiveSettings(restHours, weeklyCap, restHoursSource, weeklyCapSource);
    }

    public async Task<EffectiveSettings> GetMoleculeSettingsAsync(int moleculeId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter
        var molecule = await _db.Molecules.IgnoreQueryFilters()
            .Include(m => m.Area)
            .FirstOrDefaultAsync(m => m.Id == moleculeId);

        if (molecule == null)
        {
            return new EffectiveSettings(DefaultRestHours, DefaultWeeklyCap, "Default", "Default");
        }

        // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter
        var moleculeSettings = await _db.MoleculeSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(ms => ms.MoleculeId == moleculeId);

        // SECURITY-AUDITED: SAFE — scoped by molecule's areaId (derived from moleculeId parameter)
        var areaSettings = await _db.AreaSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.AreaId == molecule.AreaId);

        int restHours = DefaultRestHours;
        string restHoursSource = "Default";
        int weeklyCap = DefaultWeeklyCap;
        string weeklyCapSource = "Default";

        if (areaSettings != null)
        {
            restHours = areaSettings.DefaultRestHours;
            restHoursSource = "Area";
            weeklyCap = areaSettings.DefaultWeeklyCap;
            weeklyCapSource = "Area";
        }

        if (moleculeSettings != null)
        {
            if (moleculeSettings.RestHoursOverride.HasValue)
            {
                restHours = moleculeSettings.RestHoursOverride.Value;
                restHoursSource = "Molecule";
            }
            if (moleculeSettings.WeeklyCapOverride.HasValue)
            {
                weeklyCap = moleculeSettings.WeeklyCapOverride.Value;
                weeklyCapSource = "Molecule";
            }
        }

        return new EffectiveSettings(restHours, weeklyCap, restHoursSource, weeklyCapSource);
    }

    public async Task<AreaSettingsDto?> GetAreaSettingsAsync(int areaId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific areaId parameter; admin-only settings page
        var settings = await _db.AreaSettings.IgnoreQueryFilters()
            .Include(a => a.Area)
            .Include(a => a.UpdatedByUser)
            .FirstOrDefaultAsync(a => a.AreaId == areaId);

        if (settings == null)
        {
            // Return defaults if no settings record exists
            // SECURITY-AUDITED: SAFE — scoped by specific areaId parameter
            var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == areaId);
            if (area == null) return null;

            return new AreaSettingsDto(
                0, areaId, area.DisplayName,
                DefaultRestHours, DefaultWeeklyCap,
                DateTime.UtcNow, null
            );
        }

        return new AreaSettingsDto(
            settings.Id,
            settings.AreaId,
            settings.Area.DisplayName,
            settings.DefaultRestHours,
            settings.DefaultWeeklyCap,
            settings.UpdatedAt,
            settings.UpdatedByUser?.DisplayName
        );
    }

    public async Task<bool> UpdateAreaSettingsAsync(int areaId, int restHours, int weeklyCap, int updatedByUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific areaId parameter; admin-only write operation
        var settings = await _db.AreaSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.AreaId == areaId);

        if (settings == null)
        {
            settings = new AreaSettings
            {
                AreaId = areaId,
                DefaultRestHours = restHours,
                DefaultWeeklyCap = weeklyCap,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = updatedByUserId
            };
            _db.AreaSettings.Add(settings);
        }
        else
        {
            settings.DefaultRestHours = restHours;
            settings.DefaultWeeklyCap = weeklyCap;
            settings.UpdatedAt = DateTime.UtcNow;
            settings.UpdatedByUserId = updatedByUserId;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated area settings for {AreaId} by {UserId}", areaId, updatedByUserId);
        return true;
    }

    public async Task<MoleculeSettingsDto?> GetMoleculeSettingsOverrideAsync(int moleculeId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter; admin-only settings page
        var settings = await _db.MoleculeSettings.IgnoreQueryFilters()
            .Include(m => m.Molecule)
            .Include(m => m.UpdatedByUser)
            .FirstOrDefaultAsync(m => m.MoleculeId == moleculeId);

        if (settings == null)
        {
            // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter
            var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == moleculeId);
            if (molecule == null) return null;

            return new MoleculeSettingsDto(
                0, moleculeId, molecule.DisplayName,
                null, null,
                DateTime.UtcNow, null
            );
        }

        return new MoleculeSettingsDto(
            settings.Id,
            settings.MoleculeId,
            settings.Molecule.DisplayName,
            settings.RestHoursOverride,
            settings.WeeklyCapOverride,
            settings.UpdatedAt,
            settings.UpdatedByUser?.DisplayName
        );
    }

    public async Task<bool> UpdateMoleculeSettingsAsync(int moleculeId, int? restHoursOverride, int? weeklyCapOverride, int updatedByUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter; admin-only write operation
        var settings = await _db.MoleculeSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.MoleculeId == moleculeId);

        if (settings == null)
        {
            settings = new MoleculeSettings
            {
                MoleculeId = moleculeId,
                RestHoursOverride = restHoursOverride,
                WeeklyCapOverride = weeklyCapOverride,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = updatedByUserId
            };
            _db.MoleculeSettings.Add(settings);
        }
        else
        {
            settings.RestHoursOverride = restHoursOverride;
            settings.WeeklyCapOverride = weeklyCapOverride;
            settings.UpdatedAt = DateTime.UtcNow;
            settings.UpdatedByUserId = updatedByUserId;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated molecule settings for {MoleculeId} by {UserId}", moleculeId, updatedByUserId);
        return true;
    }

    public async Task<CompanySettingsDto?> GetCompanySettingsOverrideAsync(int companyId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific companyId parameter; admin-only settings page
        var settings = await _db.CompanySettings.IgnoreQueryFilters()
            .Include(c => c.Company)
            .Include(c => c.UpdatedByUser)
            .FirstOrDefaultAsync(c => c.CompanyId == companyId);

        if (settings == null)
        {
            // SECURITY-AUDITED: SAFE — scoped by specific companyId parameter
            var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == companyId);
            if (company == null) return null;

            return new CompanySettingsDto(
                0, companyId, company.Name,
                null, null,
                DateTime.UtcNow, null
            );
        }

        return new CompanySettingsDto(
            settings.Id,
            settings.CompanyId,
            settings.Company.Name,
            settings.RestHoursOverride,
            settings.WeeklyCapOverride,
            settings.UpdatedAt,
            settings.UpdatedByUser?.DisplayName
        );
    }

    public async Task<bool> UpdateCompanySettingsAsync(int companyId, int? restHoursOverride, int? weeklyCapOverride, int updatedByUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific companyId parameter; admin-only write operation
        var settings = await _db.CompanySettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId);

        if (settings == null)
        {
            settings = new CompanySettings
            {
                CompanyId = companyId,
                RestHoursOverride = restHoursOverride,
                WeeklyCapOverride = weeklyCapOverride,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = updatedByUserId
            };
            _db.CompanySettings.Add(settings);
        }
        else
        {
            settings.RestHoursOverride = restHoursOverride;
            settings.WeeklyCapOverride = weeklyCapOverride;
            settings.UpdatedAt = DateTime.UtcNow;
            settings.UpdatedByUserId = updatedByUserId;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated company settings for {CompanyId} by {UserId}", companyId, updatedByUserId);
        return true;
    }
}
