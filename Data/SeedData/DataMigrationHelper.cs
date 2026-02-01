using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Helps migrate existing data to the v3.0 organizational hierarchy.
/// This handles linking existing companies/users to the new hierarchy entities.
/// </summary>
public class DataMigrationHelper
{
    private readonly AppDbContext _db;
    private readonly ILogger<DataMigrationHelper> _logger;

    public DataMigrationHelper(AppDbContext db, ILogger<DataMigrationHelper> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Runs all migration steps. Safe to call multiple times (idempotent).
    /// </summary>
    public async Task<MigrationResult> MigrateAsync()
    {
        var result = new MigrationResult();

        try
        {
            // Step 1: Link companies to molecules
            result.CompaniesLinked = await LinkCompaniesToMoleculesAsync();

            // Step 2: Link users to job types (based on existing data patterns)
            result.UsersLinked = await LinkUsersToJobTypesAsync();

            // Step 3: Update shift types with molecule/jobtype references
            result.ShiftTypesUpdated = await UpdateShiftTypesAsync();

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data migration failed");
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Links existing companies to their molecules based on name matching.
    /// </summary>
    private async Task<int> LinkCompaniesToMoleculesAsync()
    {
        var orphanedCompanies = await _db.Companies
            .Where(c => c.MoleculeId == null)
            .ToListAsync();

        if (!orphanedCompanies.Any())
        {
            _logger.LogInformation("No orphaned companies to link");
            return 0;
        }

        // Load all molecules with their existing companies
        var molecules = await _db.Molecules
            .Include(m => m.Companies)
            .ToListAsync();

        // Company name -> Molecule mapping (based on Shifty structure)
        var companyMoleculeMap = BuildCompanyMoleculeMap(molecules);

        int linked = 0;
        foreach (var company in orphanedCompanies)
        {
            var normalizedName = NormalizeName(company.Name);

            if (companyMoleculeMap.TryGetValue(normalizedName, out var moleculeId))
            {
                company.MoleculeId = moleculeId;
                linked++;
                _logger.LogInformation("Linked company {CompanyName} to molecule ID {MoleculeId}",
                    company.Name, moleculeId);
            }
            else
            {
                _logger.LogWarning("Could not find molecule for company {CompanyName}", company.Name);
            }
        }

        if (linked > 0)
        {
            await _db.SaveChangesAsync();
        }

        return linked;
    }

    /// <summary>
    /// Links users to job types based on existing role/department data.
    /// This is a heuristic - may need manual correction for some users.
    /// </summary>
    private async Task<int> LinkUsersToJobTypesAsync()
    {
        // Get users without JobTypeId
        var usersToLink = await _db.Users
            .Where(u => u.JobTypeId == null)
            .ToListAsync();

        if (!usersToLink.Any())
        {
            _logger.LogInformation("No users to link to job types");
            return 0;
        }

        // Get job types (area-scoped, so we need the area from company's molecule)
        var jobTypes = await _db.JobTypes
            .ToListAsync();

        // Get company to areaId mapping (via molecule)
        var companyAreaMap = await _db.Companies
            .Where(c => c.MoleculeId != null)
            .Include(c => c.Molecule)
            .ToDictionaryAsync(c => c.Id, c => c.Molecule!.AreaId);

        int linked = 0;

        foreach (var user in usersToLink)
        {
            if (!companyAreaMap.TryGetValue(user.CompanyId, out var areaId))
            {
                continue;
            }

            // Try to infer job type from user's existing role data
            var inferredJobType = InferJobTypeFromUser(user, jobTypes, areaId);

            if (inferredJobType != null)
            {
                user.JobTypeId = inferredJobType.Id;
                linked++;
                _logger.LogInformation("Linked user {UserId} ({UserName}) to job type {JobType}",
                    user.Id, user.DisplayName, inferredJobType.Name);
            }
        }

        if (linked > 0)
        {
            await _db.SaveChangesAsync();
        }

        return linked;
    }

    /// <summary>
    /// Updates ShiftType records to link to molecules, job types, and shift groupings.
    /// </summary>
    private async Task<int> UpdateShiftTypesAsync()
    {
        var shiftTypes = await _db.ShiftTypes
            .Where(st => st.MoleculeId == null)
            .ToListAsync();

        if (!shiftTypes.Any())
        {
            _logger.LogInformation("No shift types to update");
            return 0;
        }

        // Get company to molecule mapping
        var companyMolecules = await _db.Companies
            .Where(c => c.MoleculeId != null)
            .ToDictionaryAsync(c => c.Id, c => c.MoleculeId);

        int updated = 0;
        foreach (var shiftType in shiftTypes)
        {
            if (companyMolecules.TryGetValue(shiftType.CompanyId, out var moleculeId))
            {
                shiftType.MoleculeId = moleculeId;
                updated++;
            }
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync();
        }

        return updated;
    }

    /// <summary>
    /// Builds a map of normalized company names to molecule IDs.
    /// </summary>
    private Dictionary<string, int> BuildCompanyMoleculeMap(List<Molecule> molecules)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var molecule in molecules)
        {
            foreach (var company in molecule.Companies)
            {
                var normalizedName = NormalizeName(company.Name);
                if (!map.ContainsKey(normalizedName))
                {
                    map[normalizedName] = molecule.Id;
                }
            }
        }

        // Add known aliases (Hebrew/English variations)
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "tzafona", "tzafona" },
            { "צפונה", "tzafona" },
            { "hir", "hir" },
            { "חיר", "hir" },
            { "camps", "camps" },
            { "מחנות", "camps" },
            { "city", "city" },
            { "העיר", "city" },
            { "radio", "radio" },
            { "טקטי", "radio" },
            { "tacti", "radio" },
            { "hitazmut", "hitazmut" },
            { "התעצמות", "hitazmut" },
            { "gap", "gap" },
            { "גאפ", "gap" },
            { "yeadim", "yeadim" },
            { "יעדים", "yeadim" },
            { "element", "element" },
            { "אלמנט", "element" },
            { "inside", "inside" },
            { "פנים", "inside" },
            { "out", "out" },
            { "חוץ", "out" },
            { "hamasa", "hamasa" },
            { "חמסה", "hamasa" },
            { "kabah", "kabah" },
            { "קבהח", "kabah" },
            { "matot", "matot" },
            { "מטות", "matot" }
        };

        foreach (var alias in aliasMap)
        {
            if (map.TryGetValue(alias.Value, out var moleculeId) && !map.ContainsKey(alias.Key))
            {
                map[alias.Key] = moleculeId;
            }
        }

        return map;
    }

    /// <summary>
    /// Attempts to infer a user's job type from their existing data.
    /// </summary>
    private JobType? InferJobTypeFromUser(AppUser user, List<JobType> jobTypes, int areaId)
    {
        var areaJobTypes = jobTypes.Where(jt => jt.AreaId == areaId).ToList();

        if (!areaJobTypes.Any())
        {
            return null;
        }

        // Check if user has any existing fields that indicate their job type
        // This is a placeholder - in practice you'd check against existing role assignments,
        // shift participation patterns, or other domain-specific data

        // Default: return null (requires manual assignment)
        // In a real migration, you might have logic like:
        // - Check if user's email contains department indicators
        // - Check their existing shift participation
        // - Check their role assignments

        return null;
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToLowerInvariant()
            .Replace("\"", "")
            .Replace("'", "")
            .Replace(" ", "");
    }

    public class MigrationResult
    {
        public bool Success { get; set; }
        public int CompaniesLinked { get; set; }
        public int UsersLinked { get; set; }
        public int ShiftTypesUpdated { get; set; }
        public string? ErrorMessage { get; set; }

        public override string ToString()
        {
            if (!Success)
            {
                return $"Migration failed: {ErrorMessage}";
            }
            return $"Migration completed: {CompaniesLinked} companies linked, " +
                   $"{UsersLinked} users linked, {ShiftTypesUpdated} shift types updated";
        }
    }
}
