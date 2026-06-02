using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// DTO for save operations on QuickInfoConfig.
/// </summary>
public class QuickInfoConfigItem
{
    public QuickInfoSectionType SectionType { get; set; }
    public int EntityId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// For the primary-Hakam section (EntityId == <see cref="QuickInfoConfig.PrimaryHakamEntityId"/>):
    /// when true the widget also shows the backup Hakam. See <see cref="QuickInfoConfig.ShowBackup"/>.
    /// </summary>
    public bool ShowBackup { get; set; } = false;
}

public interface IQuickInfoConfigService
{
    Task<List<QuickInfoConfig>> GetConfigForMoleculeAsync(int moleculeId);
    Task<(bool Success, string Message)> SaveConfigAsync(int moleculeId, int userId, List<QuickInfoConfigItem> items);
    Task<List<QuickInfoConfigItem>> GetDefaultConfigAsync(int moleculeId);
    Task<bool> HasConfigAsync(int moleculeId);

    /// <summary>
    /// Resolves "the configured backup Hakam" on-duty type value: the lowest-numbered active
    /// custom on-duty type (OnDutyTypeConfig with TypeValue &gt; 1). By seed convention this is
    /// the "Backup-hakam" type (TypeValue == 2). Returns null when no custom type is configured.
    /// Single source of truth used by the default-config builder, the widget renderer, and the
    /// config API so the three never diverge on what "backup" means.
    /// </summary>
    Task<int?> GetBackupHakamTypeValueAsync();
}
