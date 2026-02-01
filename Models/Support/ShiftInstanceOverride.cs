using System.Text.Json;

namespace ShiftManager.Models.Support;

/// <summary>
/// Represents which fields have been overridden on a ShiftInstance that was generated from a Program.
/// Used to track detachment from the original Program template.
/// Stored as JSON in ShiftInstance.OverriddenFields.
/// </summary>
public class ShiftInstanceOverride
{
    /// <summary>
    /// True if StaffingRequired was changed from Program default.
    /// </summary>
    public bool Staffing { get; set; }

    /// <summary>
    /// True if Start/End time was changed from ShiftType default.
    /// </summary>
    public bool Time { get; set; }

    /// <summary>
    /// True if a custom Name was set on the instance.
    /// </summary>
    public bool Name { get; set; }

    /// <summary>
    /// Deserialize from JSON string. Returns empty object if null/empty.
    /// </summary>
    public static ShiftInstanceOverride Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ShiftInstanceOverride();

        try
        {
            return JsonSerializer.Deserialize<ShiftInstanceOverride>(json) ?? new ShiftInstanceOverride();
        }
        catch
        {
            return new ShiftInstanceOverride();
        }
    }

    /// <summary>
    /// Serialize to JSON string for database storage.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    /// <summary>
    /// Check if any override flag is set.
    /// </summary>
    public bool HasAnyOverride()
    {
        return Staffing || Time || Name;
    }
}
