namespace ShiftManager.Models.Support;

public enum GrantScopeMode
{
    SameAsRole = 0,        // Grant scope = company level (CompanyId + DepartmentId only)
    ExpandToMolecule = 1,  // Expand to molecule (shift assignment)
    ExpandToArea = 2,      // Expand to area (Katzin, Hakam)
    Custom = 3,            // Explicit scope
    ExpandToProject = 4    // Expand to project (Owner-level)
}
