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
    /// <summary>
    /// EntityId of the built-in primary on-duty Hakam section (OnDutyType.Hakam == 0).
    /// Unlike custom on-duty types (which live in OnDutyTypeConfigs with TypeValue &gt; 1),
    /// the primary Hakam is an enum value with no config row, so it must be represented
    /// explicitly by a section using this sentinel EntityId.
    /// </summary>
    public const int PrimaryHakamEntityId = 0;

    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public QuickInfoSectionType SectionType { get; set; }
    public int EntityId { get; set; } // OnDutyTypeConfig.TypeValue when OnCallRole, Store.Id when Store
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// For the primary-Hakam OnCallRole section (EntityId == <see cref="PrimaryHakamEntityId"/>):
    /// when true, the widget also renders the backup Hakam (the configured backup on-duty type)
    /// as an additional contact under the same Hakam heading. Default false = primary only.
    /// Ignored for any other section type or EntityId.
    /// </summary>
    public bool ShowBackup { get; set; } = false;

    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule? Molecule { get; set; }
}
