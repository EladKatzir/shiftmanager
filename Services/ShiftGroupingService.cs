using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class ShiftGroupingService : IShiftGroupingService
{
    private readonly AppDbContext _db;

    public ShiftGroupingService(AppDbContext db)
    {
        _db = db;
    }

    // Query operations
    public async Task<ShiftGrouping?> GetGroupingAsync(int groupingId)
    {
        return await _db.ShiftGroupings
            .Include(sg => sg.Molecule)
            .Include(sg => sg.Companies)
            .ThenInclude(sgc => sgc.Company)
            .Include(sg => sg.JobTypes)
            .ThenInclude(sgjt => sgjt.JobType)
            .FirstOrDefaultAsync(sg => sg.Id == groupingId && sg.IsActive);
    }

    public async Task<List<ShiftGrouping>> GetGroupingsAsync(int moleculeId)
    {
        return await _db.ShiftGroupings
            .Include(sg => sg.Companies)
            .ThenInclude(sgc => sgc.Company)
            .Include(sg => sg.JobTypes)
            .ThenInclude(sgjt => sgjt.JobType)
            .Where(sg => sg.MoleculeId == moleculeId && sg.IsActive)
            .OrderBy(sg => sg.Name)
            .ToListAsync();
    }

    public async Task<List<ShiftGrouping>> GetGroupingsForCompanyAsync(int companyId)
    {
        return await _db.ShiftGroupings
            .Include(sg => sg.Companies)
            .Include(sg => sg.JobTypes)
            .Where(sg => sg.IsActive && sg.Companies.Any(c => c.CompanyId == companyId))
            .ToListAsync();
    }

    public async Task<List<ShiftGrouping>> GetGroupingsForJobTypeAsync(int jobTypeId)
    {
        return await _db.ShiftGroupings
            .Include(sg => sg.Companies)
            .Include(sg => sg.JobTypes)
            .Where(sg => sg.IsActive && sg.JobTypes.Any(jt => jt.JobTypeId == jobTypeId))
            .ToListAsync();
    }

    // Create/Update operations
    public async Task<ShiftGrouping?> CreateGroupingAsync(int moleculeId, string name, string displayName,
        List<int>? companyIds = null, List<int>? jobTypeIds = null)
    {
        var molecule = await _db.Molecules.FindAsync(moleculeId);
        if (molecule == null)
            return null;

        var grouping = new ShiftGrouping
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.ShiftGroupings.Add(grouping);
        await _db.SaveChangesAsync();

        // Add companies
        if (companyIds != null)
        {
            foreach (var companyId in companyIds)
            {
                _db.ShiftGroupingCompanies.Add(new ShiftGroupingCompany
                {
                    ShiftGroupingId = grouping.Id,
                    CompanyId = companyId
                });
            }
        }

        // Add job types
        if (jobTypeIds != null)
        {
            foreach (var jobTypeId in jobTypeIds)
            {
                _db.ShiftGroupingJobTypes.Add(new ShiftGroupingJobType
                {
                    ShiftGroupingId = grouping.Id,
                    JobTypeId = jobTypeId
                });
            }
        }

        await _db.SaveChangesAsync();

        return await GetGroupingAsync(grouping.Id);
    }

    public async Task<bool> UpdateGroupingAsync(int groupingId, string? name = null, string? displayName = null,
        List<int>? companyIds = null, List<int>? jobTypeIds = null)
    {
        var grouping = await _db.ShiftGroupings
            .Include(sg => sg.Companies)
            .Include(sg => sg.JobTypes)
            .FirstOrDefaultAsync(sg => sg.Id == groupingId);

        if (grouping == null)
            return false;

        if (name != null)
            grouping.Name = name;

        if (displayName != null)
            grouping.DisplayName = displayName;

        // Update companies if provided
        if (companyIds != null)
        {
            // Remove existing
            _db.ShiftGroupingCompanies.RemoveRange(grouping.Companies);

            // Add new
            foreach (var companyId in companyIds)
            {
                _db.ShiftGroupingCompanies.Add(new ShiftGroupingCompany
                {
                    ShiftGroupingId = groupingId,
                    CompanyId = companyId
                });
            }
        }

        // Update job types if provided
        if (jobTypeIds != null)
        {
            // Remove existing
            _db.ShiftGroupingJobTypes.RemoveRange(grouping.JobTypes);

            // Add new
            foreach (var jobTypeId in jobTypeIds)
            {
                _db.ShiftGroupingJobTypes.Add(new ShiftGroupingJobType
                {
                    ShiftGroupingId = groupingId,
                    JobTypeId = jobTypeId
                });
            }
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeactivateGroupingAsync(int groupingId)
    {
        var grouping = await _db.ShiftGroupings.FindAsync(groupingId);
        if (grouping == null)
            return false;

        grouping.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }

    // Company membership
    public async Task<bool> AddCompanyToGroupingAsync(int groupingId, int companyId)
    {
        var exists = await _db.ShiftGroupingCompanies
            .AnyAsync(sgc => sgc.ShiftGroupingId == groupingId && sgc.CompanyId == companyId);

        if (exists)
            return true;

        _db.ShiftGroupingCompanies.Add(new ShiftGroupingCompany
        {
            ShiftGroupingId = groupingId,
            CompanyId = companyId
        });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveCompanyFromGroupingAsync(int groupingId, int companyId)
    {
        var mapping = await _db.ShiftGroupingCompanies
            .FirstOrDefaultAsync(sgc => sgc.ShiftGroupingId == groupingId && sgc.CompanyId == companyId);

        if (mapping == null)
            return false;

        _db.ShiftGroupingCompanies.Remove(mapping);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<Company>> GetCompaniesInGroupingAsync(int groupingId)
    {
        return await _db.ShiftGroupingCompanies
            .Include(sgc => sgc.Company)
            .Where(sgc => sgc.ShiftGroupingId == groupingId)
            .Select(sgc => sgc.Company)
            .ToListAsync();
    }

    // JobType membership
    public async Task<bool> AddJobTypeToGroupingAsync(int groupingId, int jobTypeId)
    {
        var exists = await _db.ShiftGroupingJobTypes
            .AnyAsync(sgjt => sgjt.ShiftGroupingId == groupingId && sgjt.JobTypeId == jobTypeId);

        if (exists)
            return true;

        _db.ShiftGroupingJobTypes.Add(new ShiftGroupingJobType
        {
            ShiftGroupingId = groupingId,
            JobTypeId = jobTypeId
        });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveJobTypeFromGroupingAsync(int groupingId, int jobTypeId)
    {
        var mapping = await _db.ShiftGroupingJobTypes
            .FirstOrDefaultAsync(sgjt => sgjt.ShiftGroupingId == groupingId && sgjt.JobTypeId == jobTypeId);

        if (mapping == null)
            return false;

        _db.ShiftGroupingJobTypes.Remove(mapping);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<JobType>> GetJobTypesInGroupingAsync(int groupingId)
    {
        return await _db.ShiftGroupingJobTypes
            .Include(sgjt => sgjt.JobType)
            .Where(sgjt => sgjt.ShiftGroupingId == groupingId)
            .Select(sgjt => sgjt.JobType)
            .ToListAsync();
    }

    // User queries
    public async Task<List<AppUser>> GetUsersInGroupingAsync(int groupingId)
    {
        var grouping = await GetGroupingAsync(groupingId);
        if (grouping == null)
            return new List<AppUser>();

        var companyIds = grouping.Companies.Select(c => c.CompanyId).ToList();
        var jobTypeIds = grouping.JobTypes.Select(jt => jt.JobTypeId).ToList();

        // Users must be in one of the grouping's companies AND have one of the grouping's job types
        var query = _db.Users.Where(u => u.IsActive);

        if (companyIds.Any())
            query = query.Where(u => companyIds.Contains(u.CompanyId));

        if (jobTypeIds.Any())
            query = query.Where(u => u.JobTypeId.HasValue && jobTypeIds.Contains(u.JobTypeId.Value));

        return await query
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    public async Task<List<AppUser>> GetEligibleUsersForGroupingAsync(int groupingId)
    {
        // Same as GetUsersInGroupingAsync for now
        // Could be extended to include potential users (same molecule, compatible job types)
        return await GetUsersInGroupingAsync(groupingId);
    }
}
