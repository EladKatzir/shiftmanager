using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Idempotent startup self-heal for the 8→1 Assign*Shifts collapse (review #3). Mirrors the F2
/// ViewGrants self-heal: runs BEFORE RepairUserGrants so the de-seeded grants are not re-applied.
/// Marks the 8 old grant types deprecated, removes their template mappings, and migrates each
/// holder's old grant rows to a single AssignShifts row per distinct org-scope tuple (job-type pin
/// dropped — two job-type-pinned grants at the same molecule collapse to one).
/// </summary>
public static class AssignShiftsCollapse
{
    private static readonly string[] OldKeys =
    {
        "AssignAlhutShifts", "AssignTextShifts", "AssignBRShifts", "AssignTechShifts",
        "AssignHanavaShifts", "AssignDeltaShifts", "AssignYekevShifts", "AssignMoviltechShifts"
    };

    public static async Task HealAsync(AppDbContext db, ILogger logger)
    {
        var newType = await db.GrantTypes.FirstOrDefaultAsync(g => g.Key == "AssignShifts");
        if (newType == null) return; // seed hasn't run yet; nothing to heal

        var oldTypes = await db.GrantTypes.Where(g => OldKeys.Contains(g.Key)).ToListAsync();
        if (oldTypes.Count == 0) return;
        var oldTypeIds = oldTypes.Select(t => t.Id).ToHashSet();

        // 1) Mark deprecated (only flips rows not already deprecated → idempotent).
        var toDeprecate = oldTypes.Where(t => !t.IsDeprecated).ToList();
        foreach (var t in toDeprecate) t.IsDeprecated = true;

        // 2) Remove stale template→old-grant mappings (the seed never deletes de-seeded mappings).
        var staleMappings = await db.RoleTemplateGrants
            .Where(m => oldTypeIds.Contains(m.GrantTypeId)).ToListAsync();
        if (staleMappings.Count > 0) db.RoleTemplateGrants.RemoveRange(staleMappings);

        // 3) Migrate user grant rows: one AssignShifts per distinct scope tuple (drop JobTypeId).
        var oldUserGrants = await db.Grants.IgnoreQueryFilters()
            .Where(g => oldTypeIds.Contains(g.GrantTypeId)).ToListAsync();

        int created = 0;
        if (oldUserGrants.Count > 0)
        {
            // Dedup against existing AssignShifts rows, keyed by (user, scope tuple, CanOwn, CanGive).
            var existingNew = await db.Grants.IgnoreQueryFilters()
                .Where(g => g.GrantTypeId == newType.Id).ToListAsync();
            var seen = new HashSet<string>(existingNew.Select(ScopeKey));

            foreach (var g in oldUserGrants)
            {
                if (seen.Add(ScopeKey(g)))
                {
                    db.Grants.Add(new Grant
                    {
                        UserId = g.UserId,
                        GrantTypeId = newType.Id,
                        ProjectId = g.ProjectId,
                        AreaId = g.AreaId,
                        MoleculeId = g.MoleculeId,
                        DepartmentId = g.DepartmentId,
                        CompanyId = g.CompanyId,
                        JobTypeId = null,            // unified grant is job-type-agnostic
                        CanOwn = g.CanOwn,
                        CanGive = g.CanGive,
                        IsAutoGrant = g.IsAutoGrant
                    });
                    created++;
                }
            }
            db.Grants.RemoveRange(oldUserGrants);
        }

        if (toDeprecate.Count > 0 || staleMappings.Count > 0 || oldUserGrants.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation(
                "AssignShifts collapse self-heal: deprecated {Dep} grant types, removed {Maps} stale template mappings, migrated {Old} old user grants into {New} AssignShifts rows",
                toDeprecate.Count, staleMappings.Count, oldUserGrants.Count, created);
        }
    }

    // Scope identity = the six hierarchy columns + CanOwn/CanGive. JobTypeId is intentionally
    // EXCLUDED so two job-type-pinned grants at the same scope collapse into a single AssignShifts.
    private static string ScopeKey(Grant g) =>
        $"{g.UserId}|{g.ProjectId}|{g.AreaId}|{g.MoleculeId}|{g.DepartmentId}|{g.CompanyId}|{g.CanOwn}|{g.CanGive}";
}
