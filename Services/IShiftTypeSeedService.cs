namespace ShiftManager.Services;

/// <summary>
/// Seeds standard shift types when a new molecule is created.
/// Workforce: HOME + OFFLINE immediately. Per-JobType shifts when JobTypes are configured.
/// Tech: HOME + OFFLINE immediately. Tech shifts created manually via Blueprints.
/// Helper/System: nothing auto-seeded.
/// </summary>
public interface IShiftTypeSeedService
{
    /// <summary>
    /// Seed standard shift types for a newly created molecule based on its type.
    /// </summary>
    Task SeedForMoleculeAsync(int moleculeId);

    /// <summary>
    /// Seed per-JobType shifts (MORNING/AFTERNOON/NIGHT) when a JobType is assigned to a molecule.
    /// Only applies to Workforce molecules.
    /// </summary>
    Task SeedForJobTypeAsync(int moleculeId, int jobTypeId);
}
