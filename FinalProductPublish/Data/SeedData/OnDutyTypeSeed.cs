using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

public static class OnDutyTypeSeed
{
    public static List<OnDutyTypeConfig> GetOnDutyTypes()
    {
        var now = DateTime.UtcNow;
        return new List<OnDutyTypeConfig>
        {
            // Icon now stores a Lucide icon name (see IconTagHelper); legacy emojis still render via fallback in markup.
            new() { TypeValue = 2, NameEn = "Backup-hakam", NameHe = "חק\"מ רזרבה", Icon = "shield", Color = "#6B8E23", IsActive = true, RequiresOfficerRank = false, CreatedAt = now, CreatedBy = 0 },
        };
    }
}
