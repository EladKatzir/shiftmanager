using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IHierarchyService
{
    // Project operations
    Task<Project?> GetProjectAsync(int projectId);
    Task<List<Project>> GetAllProjectsAsync();

    // Area operations
    Task<Area?> GetAreaAsync(int areaId);
    Task<List<Area>> GetAreasAsync(int projectId);

    // Molecule operations
    Task<Molecule?> GetMoleculeAsync(int moleculeId);
    Task<List<Molecule>> GetMoleculesAsync(int areaId);

    // Company operations (workforce molecules)
    Task<List<Company>> GetCompaniesAsync(int moleculeId);

    // Department operations (tech molecules)
    Task<List<Department>> GetDepartmentsAsync(int moleculeId);

    // User hierarchy context
    Task<UserHierarchyContext?> GetUserHierarchyContextAsync(int userId);

    // Hierarchy path resolution
    Task<HierarchyPath?> GetHierarchyPathForCompanyAsync(int companyId);
    Task<HierarchyPath?> GetHierarchyPathForDepartmentAsync(int departmentId);
}

/// <summary>
/// Full hierarchy path from Project down to the leaf entity.
/// </summary>
public record HierarchyPath(
    Project Project,
    Area Area,
    Molecule Molecule,
    Company? Company,
    Department? Department
);

/// <summary>
/// User's complete hierarchy context including their position in the org structure.
/// </summary>
public record UserHierarchyContext(
    int UserId,
    HierarchyPath Path,
    JobType? JobType,
    bool IsWorkforce,
    bool IsTech
);
