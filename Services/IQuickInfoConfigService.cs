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
}

public interface IQuickInfoConfigService
{
    Task<List<QuickInfoConfig>> GetConfigForMoleculeAsync(int moleculeId);
    Task<(bool Success, string Message)> SaveConfigAsync(int moleculeId, int userId, List<QuickInfoConfigItem> items);
    Task<List<QuickInfoConfigItem>> GetDefaultConfigAsync(int moleculeId);
    Task<bool> HasConfigAsync(int moleculeId);
}
