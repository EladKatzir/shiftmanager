using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftManager.Models;

public class ShiftType : IBelongsToCompany
{
    // Predefined shift type keys
    public const string KEY_MORNING = "MORNING";
    public const string KEY_MIDDLE = "MIDDLE";
    public const string KEY_AFTERNOON = "AFTERNOON";
    public const string KEY_NOON = "NOON"; // Legacy alias for AFTERNOON
    public const string KEY_NIGHT = "NIGHT";
    public const string KEY_OFFLINE = "OFFLINE";
    public const string KEY_EVENING = "EVENING";

    // Tech shift type keys
    public const string TECH_HANAVA = "HANAVA";
    public const string TECH_DELTA = "DELTA";
    public const string TECH_YEKEV = "YEKEV";
    public const string TECH_MOVILTECH = "MOVILTECH";

    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Key { get; set; } = string.Empty; // MORNING, NOON, NIGHT, MIDDLE, OFFLINE, or CUSTOM_*

    // v3.0: Organizational hierarchy scope
    public int? MoleculeId { get; set; }
    public int? JobTypeId { get; set; }        // For workforce shifts (Alhut, Text)
    public int? ShiftGroupingId { get; set; }  // For grouped shifts (Tzafon, Darom)
    public string? TechShiftType { get; set; } // For tech shifts (Hanava, Delta, Support)

    /// <summary>
    /// JSON array of CompanyIds whose users can be assigned this shift type.
    /// Null = all companies in the molecule are eligible (default workforce behavior).
    /// </summary>
    public string? EligibleCompanyIds { get; set; }

    /// <summary>
    /// If true, only users with officer rank (>= SegenMishne) can be assigned.
    /// Follows existing pattern from OnDutyTypeConfig.RequiresOfficerRank.
    /// </summary>
    public bool RequiresOfficerRank { get; set; } = false;

    /// <summary>
    /// Custom display name for this shift type (company-specific).
    /// If null/empty, falls back to predefined names based on Key.
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    /// Resource key for localized shift name (e.g., "ShiftType_MORNING_Name").
    /// Used with &lt;loc&gt; tag helper for bilingual support via CompanyLocalizationOverride.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? NameKey { get; set; }

    [NotMapped]
    public string Name
    {
        get
        {
            // Use CustomName if provided (company-specific override)
            if (!string.IsNullOrWhiteSpace(CustomName))
                return CustomName;

            // Otherwise use predefined names
            return Key switch
            {
                KEY_MORNING => "Morning Shift",
                KEY_NOON => "Afternoon Shift",
                KEY_AFTERNOON => "Afternoon Shift",
                KEY_NIGHT => "Night Shift",
                KEY_MIDDLE => "Mid Shift",
                KEY_EVENING => "Evening Shift",
                KEY_OFFLINE => "Offline",
                _ => FormatCustomKey(Key)
            };
        }
        set { } // Empty setter since this is computed
    }

    /// <summary>
    /// Formats a raw key like "CUSTOM_NORTH_SHIFT" into "North Shift" for display.
    /// </summary>
    private static string FormatCustomKey(string key)
    {
        var name = key.StartsWith("CUSTOM_", StringComparison.OrdinalIgnoreCase)
            ? key.Substring(7) : key;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(name.Replace('_', ' ').ToLower());
    }

    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; } // if End <= Start => wraps to next day
    public string? RowColor { get; set; }  // Hex color for calendar row e.g. "#F0C14B"

    /// <summary>
    /// Returns true if this is the special "Offline" shift type that can overlap with other shifts.
    /// </summary>
    [NotMapped]
    public bool IsOffline => Key == KEY_OFFLINE;

    /// <summary>
    /// Get the sort order for this shift type (for consistent ordering across views).
    /// Morning=1, Middle=2, Afternoon=3, Night=4, Offline=99, Custom=50-98
    /// </summary>
    [NotMapped]
    public int SortOrder
    {
        get
        {
            return Key switch
            {
                KEY_MORNING => 1,
                KEY_MIDDLE => 2,
                KEY_AFTERNOON => 3,
                KEY_NOON => 3, // Same as AFTERNOON
                KEY_NIGHT => 4,
                KEY_OFFLINE => 99, // Always last
                _ => 50 // Custom shifts in the middle
            };
        }
    }

    /// <summary>
    /// Parses EligibleCompanyIds JSON into a list of ints.
    /// Returns null if no restriction (all companies eligible).
    /// </summary>
    public List<int>? GetEligibleCompanyIdList()
    {
        if (string.IsNullOrEmpty(EligibleCompanyIds)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<int>>(EligibleCompanyIds);
        }
        catch
        {
            return null;
        }
    }

    // v3.0: Navigation properties
    public Molecule? Molecule { get; set; }
    public JobType? JobType { get; set; }
    public ShiftGrouping? ShiftGrouping { get; set; }
}
