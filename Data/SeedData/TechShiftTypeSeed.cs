using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Seed data for tech shift types (Hanava, Delta, Yekev, Moviltech).
/// All seeded as molecule-scoped (Scope = Molecule, CompanyId = null).
/// Eligibility is company-based (via EligibleCompanyIds) and rank-based (via RequiresOfficerRank).
/// </summary>
public static class TechShiftTypeSeed
{
    /// <summary>
    /// Gets the tech shift types to be seeded for a given molecule.
    /// Eligibility rules (EligibleCompanyIds) are set post-save in ShiftyOrganizationSeed.
    /// </summary>
    public static List<ShiftType> GetTechShiftTypes(int moleculeId)
    {
        return new List<ShiftType>
        {
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                Key = ShiftType.TECH_HANAVA,
                TechShiftType = ShiftType.TECH_HANAVA,
                NameEn = "Hanava Tech Shift",
                NameHe = "משמרת חנב\"ה",
                NameKey = "TechShift_Hanava",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#4A90D9"
            },
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                Key = ShiftType.TECH_DELTA,
                TechShiftType = ShiftType.TECH_DELTA,
                NameEn = "Delta Tech Shift",
                NameHe = "משמרת דלתא",
                NameKey = "TechShift_Delta",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#D94A4A"
            },
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                Key = ShiftType.TECH_YEKEV,
                TechShiftType = ShiftType.TECH_YEKEV,
                NameEn = "Yekev Tech Shift",
                NameHe = "משמרת יקב",
                NameKey = "TechShift_Yekev",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#7B4AD9"
            },
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                Key = ShiftType.TECH_MOVILTECH,
                TechShiftType = ShiftType.TECH_MOVILTECH,
                NameEn = "Moviltech Tech Shift",
                NameHe = "משמרת מוביל-טכ",
                NameKey = "TechShift_Moviltech",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0),
                RowColor = "#D9A04A",
                RequiresOfficerRank = true
            },
            // HOME shift type — rotation day off, exempt from overlap/rest/cap validation
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                Key = ShiftType.KEY_HOME,
                NameEn = "Home",
                NameHe = "בית",
                NameKey = "Home",
                Start = new TimeOnly(0, 0),
                End = new TimeOnly(23, 59),
                RowColor = "#F8E7B1",
                IsBlocking = false,
                CountsTowardHourLimits = false
            },
            // OFFLINE shift type — at base doing backlogged/offline work
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                Key = ShiftType.KEY_OFFLINE,
                NameEn = "Offline",
                NameHe = "אופליין",
                NameKey = "ShiftType_OFFLINE_Name",
                Start = new TimeOnly(0, 0),
                End = new TimeOnly(0, 0),
                RowColor = "#E0E0E0",
                IsBlocking = false,
                CountsTowardHourLimits = false
            }
        };
    }

    /// <summary>
    /// Gets standard workforce shift types (MORNING, AFTERNOON, NIGHT, HOME, OFFLINE)
    /// to be seeded for a workforce molecule per JobType.
    /// </summary>
    public static List<ShiftType> GetWorkforceShiftTypes(int moleculeId, int jobTypeId)
    {
        return new List<ShiftType>
        {
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                JobTypeId = jobTypeId,
                Key = ShiftType.KEY_MORNING,
                NameKey = "ShiftType_MORNING_Name",
                Start = new TimeOnly(8, 0),
                End = new TimeOnly(16, 0)
            },
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                JobTypeId = jobTypeId,
                Key = ShiftType.KEY_AFTERNOON,
                NameKey = "ShiftType_AFTERNOON_Name",
                Start = new TimeOnly(16, 0),
                End = new TimeOnly(0, 0)
            },
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                JobTypeId = jobTypeId,
                Key = ShiftType.KEY_NIGHT,
                NameKey = "ShiftType_NIGHT_Name",
                Start = new TimeOnly(0, 0),
                End = new TimeOnly(8, 0)
            }
        };
    }

    /// <summary>
    /// Gets shared (no JobType) shift types for a workforce molecule: HOME and OFFLINE.
    /// </summary>
    public static List<ShiftType> GetWorkforceSharedShiftTypes(int moleculeId)
    {
        return new List<ShiftType>
        {
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                Key = ShiftType.KEY_HOME,
                NameEn = "Home",
                NameHe = "בית",
                NameKey = "Home",
                Start = new TimeOnly(0, 0),
                End = new TimeOnly(23, 59),
                RowColor = "#F8E7B1",
                IsBlocking = false,
                CountsTowardHourLimits = false
            },
            new ShiftType
            {
                Scope = ShiftScope.Molecule,
                MoleculeId = moleculeId,
                Key = ShiftType.KEY_OFFLINE,
                NameEn = "Offline",
                NameHe = "אופליין",
                NameKey = "ShiftType_OFFLINE_Name",
                Start = new TimeOnly(0, 0),
                End = new TimeOnly(0, 0),
                RowColor = "#E0E0E0",
                IsBlocking = false,
                CountsTowardHourLimits = false
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
