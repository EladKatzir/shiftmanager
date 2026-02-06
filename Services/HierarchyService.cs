using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class HierarchyService : IHierarchyService
{
    private readonly AppDbContext _db;

    public HierarchyService(AppDbContext db)
    {
        _db = db;
    }

    // Project operations
    public async Task<Project?> GetProjectAsync(int projectId)
    {
        return await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.IsActive);
    }

    public async Task<List<Project>> GetAllProjectsAsync()
    {
        return await _db.Projects
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    // Area operations
    public async Task<Area?> GetAreaAsync(int areaId)
    {
        return await _db.Areas
            .Include(a => a.Project)
            .FirstOrDefaultAsync(a => a.Id == areaId && a.IsActive);
    }

    public async Task<List<Area>> GetAreasAsync(int projectId)
    {
        return await _db.Areas
            .Where(a => a.ProjectId == projectId && a.IsActive)
            .OrderBy(a => a.Name)
            .ToListAsync();
    }

    // Molecule operations
    public async Task<Molecule?> GetMoleculeAsync(int moleculeId)
    {
        return await _db.Molecules
            .Include(m => m.Area)
            .ThenInclude(a => a.Project)
            .FirstOrDefaultAsync(m => m.Id == moleculeId && m.IsActive);
    }

    public async Task<List<Molecule>> GetMoleculesAsync(int areaId)
    {
        return await _db.Molecules
            .Where(m => m.AreaId == areaId && m.IsActive)
            .OrderBy(m => m.Name)
            .ToListAsync();
    }

    // Company operations (workforce molecules)
    public async Task<List<Company>> GetCompaniesAsync(int moleculeId)
    {
        return await _db.Companies
            .Where(c => c.MoleculeId == moleculeId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    // Department operations (tech molecules)
    public async Task<List<Department>> GetDepartmentsAsync(int moleculeId)
    {
        return await _db.Departments
            .Where(d => d.MoleculeId == moleculeId && d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }

    // User hierarchy context
    public async Task<UserHierarchyContext?> GetUserHierarchyContextAsync(int userId)
    {
        // IMPORTANT: Use IgnoreQueryFilters to bypass tenant filtering.
        // This is needed because Owners/Directors may be viewing a different company
        // but still need their own user record to determine their hierarchy context.
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.JobType)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return null;

        HierarchyPath? path = null;

        // Workforce user - get path via Company
        if (user.CompanyId > 0)
        {
            path = await GetHierarchyPathForCompanyAsync(user.CompanyId);
        }
        // Tech user - get path via Department
        else if (user.DepartmentId.HasValue)
        {
            path = await GetHierarchyPathForDepartmentAsync(user.DepartmentId.Value);
        }

        if (path == null)
            return null;

        return new UserHierarchyContext(
            UserId: userId,
            Path: path,
            JobType: user.JobType,
            IsWorkforce: user.CompanyId > 0 && !user.DepartmentId.HasValue,
            IsTech: user.DepartmentId.HasValue
        );
    }

    // Hierarchy path resolution
    public async Task<HierarchyPath?> GetHierarchyPathForCompanyAsync(int companyId)
    {
        var company = await _db.Companies
            .Include(c => c.Molecule)
            .ThenInclude(m => m!.Area)
            .ThenInclude(a => a.Project)
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company?.Molecule?.Area?.Project == null)
            return null;

        return new HierarchyPath(
            Project: company.Molecule.Area.Project,
            Area: company.Molecule.Area,
            Molecule: company.Molecule,
            Company: company,
            Department: null
        );
    }

    public async Task<HierarchyPath?> GetHierarchyPathForDepartmentAsync(int departmentId)
    {
        var department = await _db.Departments
            .Include(d => d.Molecule)
            .ThenInclude(m => m.Area)
            .ThenInclude(a => a.Project)
            .FirstOrDefaultAsync(d => d.Id == departmentId);

        if (department?.Molecule?.Area?.Project == null)
            return null;

        return new HierarchyPath(
            Project: department.Molecule.Area.Project,
            Area: department.Molecule.Area,
            Molecule: department.Molecule,
            Company: null,
            Department: department
        );
    }
}
