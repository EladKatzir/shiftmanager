using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class QuickInfoConfigService : IQuickInfoConfigService
{
    private readonly AppDbContext _db;
    private readonly ILogger<QuickInfoConfigService> _logger;

    public QuickInfoConfigService(AppDbContext db, ILogger<QuickInfoConfigService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get all enabled QuickInfoConfig entries for a molecule, ordered by DisplayOrder.
    /// </summary>
    public async Task<List<QuickInfoConfig>> GetConfigForMoleculeAsync(int moleculeId)
    {
        return await _db.QuickInfoConfigs
            .AsNoTracking()
            .Where(q => q.MoleculeId == moleculeId && q.IsEnabled)
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync();
    }

    /// <summary>
    /// Save QuickInfoConfig for a molecule using delete-and-reinsert pattern.
    /// </summary>
    public async Task<(bool Success, string Message)> SaveConfigAsync(
        int moleculeId, int userId, List<QuickInfoConfigItem> items)
    {
        try
        {
            if (items == null)
            {
                return (false, "Items list cannot be null.");
            }

            // Verify molecule exists
            var moleculeExists = await _db.Molecules.AnyAsync(m => m.Id == moleculeId);
            if (!moleculeExists)
            {
                return (false, "Molecule not found.");
            }

            // Delete existing configs for this molecule
            var existing = await _db.QuickInfoConfigs
                .Where(q => q.MoleculeId == moleculeId)
                .ToListAsync();

            _db.QuickInfoConfigs.RemoveRange(existing);

            // Add new configs from items
            foreach (var item in items)
            {
                var config = new QuickInfoConfig
                {
                    MoleculeId = moleculeId,
                    SectionType = item.SectionType,
                    EntityId = item.EntityId,
                    DisplayOrder = item.DisplayOrder,
                    IsEnabled = item.IsEnabled,
                    // ShowBackup only carries meaning on the primary-Hakam section; harmless on others.
                    ShowBackup = item.ShowBackup,
                    CreatedBy = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _db.QuickInfoConfigs.Add(config);
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "QuickInfoConfig saved for molecule {MoleculeId} by user {UserId}: {ItemCount} items",
                moleculeId, userId, items.Count);

            return (true, "Configuration saved successfully.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error saving QuickInfoConfig for molecule {MoleculeId}", moleculeId);
            return (false, "An error occurred while saving configuration.");
        }
    }

    /// <summary>
    /// Compute default config on-the-fly (NOT persisted).
    /// Always begins with the built-in primary-Hakam section (EntityId 0, ShowBackup off) so the
    /// primary Hakam is visible by default — it has no OnDutyTypeConfig row and was previously
    /// invisible. Then the remaining active custom on-duty types (excluding any TypeValue 0 row and
    /// the configured backup type, which is folded into the Hakam section's ShowBackup toggle),
    /// then Stores for the molecule's area.
    /// </summary>
    public async Task<List<QuickInfoConfigItem>> GetDefaultConfigAsync(int moleculeId)
    {
        // Get molecule's AreaId
        var areaId = await _db.Molecules
            .Where(m => m.Id == moleculeId)
            .Select(m => m.AreaId)
            .FirstOrDefaultAsync();

        if (areaId == 0)
        {
            _logger.LogWarning("Molecule {MoleculeId} not found when computing default QuickInfoConfig", moleculeId);
            return new List<QuickInfoConfigItem>();
        }

        var items = new List<QuickInfoConfigItem>();
        int order = 0;

        // Always lead with the built-in primary Hakam (OnDutyType.Hakam == 0). It is an enum value
        // with no OnDutyTypeConfig row, so without this explicit section it never appears.
        items.Add(new QuickInfoConfigItem
        {
            SectionType = QuickInfoSectionType.OnCallRole,
            EntityId = QuickInfoConfig.PrimaryHakamEntityId,
            DisplayOrder = order++,
            IsEnabled = true,
            ShowBackup = false
        });

        // Get all active OnDutyTypeConfigs (global, no area filter)
        var dutyTypes = await _db.OnDutyTypeConfigs
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeValue)
            .ToListAsync();

        // "The configured backup type" is the lowest active custom type (TypeValue > 1). It is surfaced
        // through the primary-Hakam section's ShowBackup toggle, so it must NOT also appear standalone.
        var backupTypeValue = dutyTypes.FirstOrDefault(t => t.TypeValue > QuickInfoConfig.PrimaryHakamEntityId + 1)?.TypeValue;

        foreach (var dt in dutyTypes)
        {
            // Skip a TypeValue 0 row (already represented by the prepended primary section) and the
            // backup type (folded into ShowBackup). Lead (TypeValue 1) and other customs stay standalone.
            if (dt.TypeValue == QuickInfoConfig.PrimaryHakamEntityId) continue;
            if (backupTypeValue.HasValue && dt.TypeValue == backupTypeValue.Value) continue;

            items.Add(new QuickInfoConfigItem
            {
                SectionType = QuickInfoSectionType.OnCallRole,
                EntityId = dt.TypeValue,
                DisplayOrder = order++,
                IsEnabled = true
            });
        }

        // Get all active Stores for the molecule's area
        var stores = await _db.Stores
            .AsNoTracking()
            .Where(s => s.AreaId == areaId && s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        foreach (var store in stores)
        {
            items.Add(new QuickInfoConfigItem
            {
                SectionType = QuickInfoSectionType.Store,
                EntityId = store.Id,
                DisplayOrder = order++,
                IsEnabled = true
            });
        }

        return items;
    }

    /// <summary>
    /// Check whether any QuickInfoConfig entries exist for a molecule.
    /// </summary>
    public async Task<bool> HasConfigAsync(int moleculeId)
    {
        return await _db.QuickInfoConfigs.AnyAsync(q => q.MoleculeId == moleculeId);
    }

    /// <inheritdoc />
    public async Task<int?> GetBackupHakamTypeValueAsync()
    {
        // Lowest active custom on-duty type (TypeValue > 1). By seed convention this is "Backup-hakam"
        // (TypeValue 2). Nullable so callers can no-op when no custom type exists.
        return await _db.OnDutyTypeConfigs
            .AsNoTracking()
            .Where(t => t.IsActive && t.TypeValue > QuickInfoConfig.PrimaryHakamEntityId + 1)
            .OrderBy(t => t.TypeValue)
            .Select(t => (int?)t.TypeValue)
            .FirstOrDefaultAsync();
    }
}
