namespace ShiftManager.Models.Support;

public enum GrantScopeMode
{
    SameAsRole = 0,        // Grant scope = role scope
    ExpandToMolecule = 1,  // Expand to molecule (shift assignment)
    ExpandToArea = 2,      // Expand to area (Katzin, Hakam)
    Custom = 3             // Explicit scope
}
