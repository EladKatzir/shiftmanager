using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Seed data for tech shift types (Hanava, Delta, Yekev, Moviltech).
/// Eligibility is company-based (via EligibleCompanyIds) and rank-based (via RequiresOfficerRank).
/// </summary>
public static class TechShiftTypeSeed
{
    /// <summary>
    /// Gets the tech shift types to be seeded for a given molecule.
    /// Eligibility rules (EligibleCompanyIds) are set post-save in ShiftyOrganizationSeed.
    /// </summary>
    public static List<ShiftType> GetTechShiftTypes(int companyId, int moleculeId)
    {
        return new List<ShiftType>
        {
            new ShiftType
            {
                CompanyId = companyId,
                MoleculeId = moleculeId,
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
                MoleculeId = moleculeId,
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
                MoleculeId = moleculeId,
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
                MoleculeId = moleculeId,
                Key = ShiftType.TECH_MOVILTECH,
                TechShiftType = ShiftType.TECH_MOVILTECH,
                CustomName = "Moviltech Tech Shift",
                NameKey = "TechShift_Moviltech",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#D9A04A",
                RequiresOfficerRank = true
            },
            // HOME shift type — rotation day off, exempt from overlap/rest/cap validation
            new ShiftType
            {
                CompanyId = companyId,
                MoleculeId = moleculeId,
                Key = ShiftType.KEY_HOME,
                CustomName = "Home",
                NameKey = "Home",
                Start = new TimeOnly(0, 0),
                End = new TimeOnly(23, 59),
                RowColor = "#F8E7B1"
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
