using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftManager.Models;

/// <summary>
/// Store entity — area-scoped, NO IBelongsToCompany (global table like OnDutyTypeConfig).
/// Access control via ManageStores grant (area-scoped).
/// </summary>
public class Store
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string NameEn { get; set; } = "";
    public string? NameHe { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    // Navigation
    public Area? Area { get; set; }
    public List<StoreHoursEntry> StoreHoursEntries { get; set; } = new();

    /// <summary>
    /// Returns the culture-appropriate display name: NameHe when Hebrew, otherwise NameEn.
    /// Follows the same pattern as Company.LocalizedName.
    /// </summary>
    [NotMapped]
    public string Name =>
        Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName == "he"
            ? (NameHe ?? NameEn)
            : NameEn;
}
