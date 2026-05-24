namespace ShiftManager.Models;

/// <summary>
/// Hierarchy level a Justice query is scoped to.
/// Mirrors the org hierarchy: Project > Area > Molecule > Company.
/// Global = no scope filter (used for default per-user targets).
/// </summary>
public enum JusticeScope
{
    Global = 0,
    Project = 1,
    Area = 2,
    Molecule = 3,
    Company = 4
}

/// <summary>
/// Which row dimension the Justice Table is grouping by.
/// Different from JusticeScope — Scope is the filter, Level is the row aggregation.
/// E.g., Scope=Molecule + Level=CompaniesInMolecule means "filter to one molecule,
/// show one row per company inside it".
/// </summary>
public enum JusticeLevel
{
    UsersInCompany = 0,
    CompaniesInMolecule = 1,
    MoleculesInArea = 2
}

/// <summary>
/// Work item category being measured.
/// Shifts are capacity-driven (expected = sum of StaffingRequired).
/// Chore + OnDuty are target-driven (expected = per-user target * headcount, with optional override).
/// </summary>
public enum JusticeWorkType
{
    All = 0,
    Shift = 1,
    Chore = 2,
    OnDuty = 3
}

/// <summary>
/// Period bucket for per-user expected counts.
/// E.g., "3 chores per month per user" -> PeriodKind=PerMonth, ExpectedCount=3.
/// </summary>
public enum PeriodKind
{
    PerWeek = 0,
    PerMonth = 1,
    PerQuarter = 2
}

/// <summary>
/// How "Expected" is derived for each row in the Justice table.
/// BySize = capacity/target-weighted (default, today's behavior).
/// EqualShare = the actual total split evenly across rows
/// (everyone expected to carry the same — distance-from-mean semantics,
/// matching the UI's "≈ equal = total ÷ N").
/// </summary>
public enum FairnessBasis { BySize = 0, EqualShare = 1 }
