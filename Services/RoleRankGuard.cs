using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// "You may act at your own rank or below, never above."
///
/// Rank is <see cref="RoleTemplate.SortOrder"/>, where a LOWER number is MORE senior
/// (Owner 1 &lt; AreaAdmin 2 &lt; MoleculeAdmin 3 &lt; Director 5 &lt; BRDirector 10 &lt; Lead 20 &lt; … &lt; Trainee 101).
/// Acting at the SAME rank is allowed.
///
/// Why this exists: Lead/Kabar now hold EditCompanyUsers across their whole molecule, which also
/// covers reset-password, deactivate and delete. Without a rank rule a Lead could reset their
/// MoleculeAdmin's password in another desk and log in as them. The same rule stops anyone assigning
/// a role more senior than their own.
///
/// It is deliberately a RANK rule only — it says nothing about WHERE the actor may act. The existing
/// scope checks (EditCompanyUsers for a company, AssignRoles, …) still decide that, so no legitimate
/// in-scope action is affected; this only removes acting on someone more senior.
/// </summary>
public static class RoleRankGuard
{
    /// <summary>Least senior possible rank. Used when a rank cannot be resolved, so an unknown
    /// actor fails closed rather than being treated as senior.</summary>
    public const int LeastSeniorRank = int.MaxValue;

    /// <summary>True when the actor may act on <paramref name="targetUserId"/>.</summary>
    public static async Task<bool> CanActOnUserAsync(AppDbContext db, IGrantService grantService, int actorUserId, int targetUserId)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(grantService);

        if (actorUserId == targetUserId)
            return true;

        if (await grantService.HasGrantAsync(actorUserId, "AdminAccess"))
            return true;

        var actorRank = await GetUserRankAsync(db, actorUserId);
        var targetRank = await GetUserRankAsync(db, targetUserId);
        return actorRank <= targetRank;
    }

    /// <summary>True when the actor may assign the role template <paramref name="roleTemplateId"/>.</summary>
    public static async Task<bool> CanAssignTemplateAsync(AppDbContext db, IGrantService grantService, int actorUserId, int roleTemplateId)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(grantService);

        if (await grantService.HasGrantAsync(actorUserId, "AdminAccess"))
            return true;

        var actorRank = await GetUserRankAsync(db, actorUserId);

        // SECURITY-AUDITED: SAFE — reads only the ordering of a role template, which is reference data.
        var targetRank = await db.RoleTemplates
            .IgnoreQueryFilters()
            .Where(rt => rt.Id == roleTemplateId)
            .Select(rt => (int?)rt.SortOrder)
            .FirstOrDefaultAsync() ?? LeastSeniorRank;

        return actorRank <= targetRank;
    }

    /// <summary>
    /// The user's rank. Falls back to the template their <see cref="AppUser.Role"/> maps to when
    /// RoleTemplateId is unset — several active accounts have no template, and without the fallback
    /// they would be treated as the least senior user in the system.
    /// </summary>
    public static async Task<int> GetUserRankAsync(AppDbContext db, int userId)
    {
        ArgumentNullException.ThrowIfNull(db);

        // SECURITY-AUDITED: SAFE — resolves one user's role ordering by explicit id; no tenant data.
        var user = await db.Users
            .IgnoreQueryFilters()
            .Include(u => u.JobType)
            .Where(u => u.Id == userId)
            .Select(u => new { u.RoleTemplateId, u.Role, JobTypeName = u.JobType != null ? u.JobType.Name : null })
            .FirstOrDefaultAsync();

        if (user == null)
            return LeastSeniorRank;

        if (user.RoleTemplateId.HasValue)
        {
            var byTemplate = await db.RoleTemplates
                .IgnoreQueryFilters()
                .Where(rt => rt.Id == user.RoleTemplateId.Value)
                .Select(rt => (int?)rt.SortOrder)
                .FirstOrDefaultAsync();
            if (byTemplate.HasValue)
                return byTemplate.Value;
        }

        var key = RoleTemplateMapper.MapUserRoleToRoleTemplateKey(user.Role, user.JobTypeName);
        var byKey = await db.RoleTemplates
            .IgnoreQueryFilters()
            .Where(rt => rt.Key == key)
            .Select(rt => (int?)rt.SortOrder)
            .FirstOrDefaultAsync();

        return byKey ?? LeastSeniorRank;
    }
}
