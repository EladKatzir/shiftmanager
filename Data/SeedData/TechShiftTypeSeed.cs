using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Seed data for tech shift types (Hanava, Delta, Yekev, Moviltech).
/// These are department-scoped shift types used for technician scheduling.
/// </summary>
public static class TechShiftTypeSeed
{
    /// <summary>
    /// Gets the tech shift types to be seeded for a given company.
    /// Each tech shift type maps to a department-scoped grant for eligibility filtering.
    /// </summary>
    public static List<ShiftType> GetTechShiftTypes(int companyId)
    {
        return new List<ShiftType>
        {
            new ShiftType
            {
                CompanyId = companyId,
                Key = ShiftType.TECH_HANAVA,
                TechShiftType = ShiftType.TECH_HANAVA,
                CustomName = "Hanava Tech Shift",
                NameKey = "TechShift_Hanava",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#4A90D9"
            },
            new ShiftType
            {
                CompanyId = companyId,
                Key = ShiftType.TECH_DELTA,
                TechShiftType = ShiftType.TECH_DELTA,
                CustomName = "Delta Tech Shift",
                NameKey = "TechShift_Delta",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#D94A4A"
            },
            new ShiftType
            {
                CompanyId = companyId,
                Key = ShiftType.TECH_YEKEV,
                TechShiftType = ShiftType.TECH_YEKEV,
                CustomName = "Yekev Tech Shift",
                NameKey = "TechShift_Yekev",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#7B4AD9"
            },
            new ShiftType
            {
                CompanyId = companyId,
                Key = ShiftType.TECH_MOVILTECH,
                TechShiftType = ShiftType.TECH_MOVILTECH,
                CustomName = "Moviltech Tech Shift",
                NameKey = "TechShift_Moviltech",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#D9A04A"
            }
        };
    }

    /// <summary>
    /// All known tech shift type keys.
    /// </summary>
    public static readonly string[] AllTechShiftTypeKeys = new[]
    {
        ShiftType.TECH_HANAVA,
        ShiftType.TECH_DELTA,
        ShiftType.TECH_YEKEV,
        ShiftType.TECH_MOVILTECH
    };
}
