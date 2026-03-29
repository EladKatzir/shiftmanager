namespace ShiftManager.Models;

public enum QuickInfoSectionType
{
    OnCallRole = 0,
    Store = 1
}

/// <summary>
/// Configuration for which items appear in the Quick Info widget for a given molecule.
/// SECURITY-AUDITED: No query filter needed — molecule-global table (like ChoreType).
/// Service MUST always scope queries by moleculeId explicitly.
/// </summary>
public class QuickInfoConfig
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public QuickInfoSectionType SectionType { get; set; }
    public int EntityId { get; set; } // OnDutyTypeConfig.TypeValue when OnCallRole, Store.Id when Store
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule? Molecule { get; set; }
}
