using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Moves a whole company from one molecule to another. Modeled on <see cref="UserCompanyTransferService"/>:
/// the company keeps its Id (so CompanyId foreign keys — users, vacations — stay valid automatically),
/// but everything molecule-scoped (shift scheduling, chores, per-user job-type/department/home-type and
/// category memberships, molecule-scoped grants) belongs to the OLD molecule and is cleared/rebuilt.
/// Vacations (TimeOffRequest) and accounts survive.
/// </summary>
public class CompanyMoleculeTransferService : ICompanyMoleculeTransferService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IConcurrencyService _concurrencyService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<CompanyMoleculeTransferService> _logger;

    public CompanyMoleculeTransferService(
        AppDbContext db,
        IGrantService grantService,
        IConcurrencyService concurrencyService,
        IAuditLogService auditLogService,
        ILogger<CompanyMoleculeTransferService> logger)
    {
        _db = db;
        _grantService = grantService;
        _concurrencyService = concurrencyService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<CompanyMoveImpact> GetMoveImpactAsync(int companyId, int targetMoleculeId)
    {
        // SECURITY-AUDITED: read-only preview, every count scoped to the explicit companyId.
        var company = await _db.Companies.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        var targetValid = await _db.Molecules.IgnoreQueryFilters().AnyAsync(m => m.Id == targetMoleculeId);

        if (company == null)
            return new CompanyMoveImpact(0, 0, 0, 0, 0, 0, targetValid, new[] { "Company not found" });

        var warnings = new List<string>();
        if (company.MoleculeId == targetMoleculeId)
            warnings.Add("Company is already in the target molecule");

        var shiftInstances = await _db.ShiftInstances.IgnoreQueryFilters().CountAsync(i => i.CompanyId == companyId);
        var shiftAssignments = await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(a => a.CompanyId == companyId);
        var shiftPrograms = await _db.ShiftPrograms.IgnoreQueryFilters().CountAsync(p => p.CompanyId == companyId);
        var chores = await _db.Chores.IgnoreQueryFilters().CountAsync(c => c.CompanyId == companyId);
        var affectedUsers = await _db.Users.IgnoreQueryFilters().CountAsync(u => u.CompanyId == companyId);
        var companyShiftTypes = await _db.ShiftTypes.IgnoreQueryFilters().CountAsync(st => st.CompanyId == companyId);

        return new CompanyMoveImpact(shiftInstances, shiftAssignments, shiftPrograms, chores,
            affectedUsers, companyShiftTypes, targetValid, warnings);
    }

    public async Task<CompanyMoveResult> MoveCompanyToMoleculeAsync(int companyId, int targetMoleculeId, int actingAdminId)
    {
        var company = await _db.Companies.IgnoreQueryFilters() // SECURITY-AUDITED: load move target by explicit id
            .FirstOrDefaultAsync(c => c.Id == companyId);
        if (company == null) return new CompanyMoveResult(false, "Error_CompanyNotFound");

        var targetMolecule = await _db.Molecules.IgnoreQueryFilters() // SECURITY-AUDITED: validate dest by explicit id
            .FirstOrDefaultAsync(m => m.Id == targetMoleculeId);
        if (targetMolecule == null) return new CompanyMoveResult(false, "Error_MoleculeNotFound");
        if (company.MoleculeId == targetMoleculeId) return new CompanyMoveResult(false, "Error_MoveSameMolecule");

        var sourceMoleculeId = company.MoleculeId;

        // Users whose PRIMARY company is this one — they move with the company.
        var userIds = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.CompanyId == companyId).Select(u => u.Id).ToListAsync();

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // --- A. Shift scheduling data ("all existing shifts will be deleted"). ---
            // SECURITY-AUDITED: each scoped to CompanyId == companyId; IgnoreQueryFilters is query-global.
            await _db.ShiftAssignments.IgnoreQueryFilters().Where(a => a.CompanyId == companyId).ExecuteDeleteAsync();
            await _db.ShiftInstances.IgnoreQueryFilters().Where(i => i.CompanyId == companyId).ExecuteDeleteAsync();

            // Weekly/master templates would otherwise regenerate instances against the OLD molecule's
            // shift types. Delete their child rows first (ProgramDay.ProgramId / MasterProgramItem.MasterProgramId).
            var programIds = await _db.ShiftPrograms.IgnoreQueryFilters().Where(p => p.CompanyId == companyId).Select(p => p.Id).ToListAsync();
            if (programIds.Count > 0)
                await _db.ProgramDays.Where(pd => programIds.Contains(pd.ProgramId)).ExecuteDeleteAsync();
            await _db.ShiftPrograms.IgnoreQueryFilters().Where(p => p.CompanyId == companyId).ExecuteDeleteAsync();

            var masterIds = await _db.MasterPrograms.IgnoreQueryFilters().Where(mp => mp.CompanyId == companyId).Select(mp => mp.Id).ToListAsync();
            if (masterIds.Count > 0)
                await _db.MasterProgramItems.Where(mi => masterIds.Contains(mi.MasterProgramId)).ExecuteDeleteAsync();
            await _db.MasterPrograms.IgnoreQueryFilters().Where(mp => mp.CompanyId == companyId).ExecuteDeleteAsync();

            // Company-scoped shift types reference the OLD molecule (molecule-scoped types are shared — leave them).
            await _db.ShiftTypes.IgnoreQueryFilters().Where(st => st.CompanyId == companyId).ExecuteDeleteAsync();

            // Old-molecule grouping membership.
            await _db.ShiftGroupingCompanies.Where(g => g.CompanyId == companyId).ExecuteDeleteAsync();

            // --- B. Chores (molecule/ChoreType-tied). ---
            await _db.Chores.IgnoreQueryFilters().Where(c => c.CompanyId == companyId).ExecuteDeleteAsync();

            // --- C. Per-user molecule-scoped config for the company's users. ---
            if (userIds.Count > 0)
            {
                await _db.UserShiftCategories.Where(uc => userIds.Contains(uc.UserId)).ExecuteDeleteAsync();
                await _db.UserChoreCategories.Where(uc => userIds.Contains(uc.UserId)).ExecuteDeleteAsync();
                await _db.UserChoreExemptions.Where(ue => userIds.Contains(ue.UserId)).ExecuteDeleteAsync();
                // Scalar FKs point at old-molecule/area-scoped rows — reset (admin re-assigns in the new molecule).
                await _db.Users.IgnoreQueryFilters().Where(u => userIds.Contains(u.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.JobTypeId, (int?)null)
                        .SetProperty(u => u.DepartmentId, (int?)null)
                        .SetProperty(u => u.HomeTypeId, (int?)null));
            }

            // Home-rotation config for this company (carries the old MoleculeId).
            await _db.HomeTypeOverrides.IgnoreQueryFilters().Where(h => h.CompanyId == companyId).ExecuteDeleteAsync();
            await _db.HomeTypes.IgnoreQueryFilters().Where(h => h.CompanyId == companyId).ExecuteDeleteAsync();

            // Reset the per-company membership molecule columns (covers primary + secondary members of this company).
            await _db.CompanyMemberships.Where(m => m.CompanyId == companyId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.JobTypeId, (int?)null)
                    .SetProperty(m => m.DepartmentId, (int?)null)
                    .SetProperty(m => m.HomeTypeId, (int?)null));

            // --- D. The essential mutation. Persist BEFORE rebuilding grants so the grant scope
            //        builder resolves the NEW hierarchy. ---
            company.MoleculeId = targetMoleculeId;
            var save1 = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "CompanyMoleculeMove", companyId);
            if (!save1.Success)
            {
                await tx.RollbackAsync();
                return new CompanyMoveResult(false, "Error_ConcurrencyConflict");
            }

            // --- E. Rebuild grants for the company's users under the new hierarchy. ---
            foreach (var uid in userIds)
            {
                // Revoke old grants/role-assignments (scoped to the old molecule/area).
                await _db.Grants.IgnoreQueryFilters().Where(g => g.UserId == uid).ExecuteDeleteAsync();
                await _db.UserRoleAssignments.IgnoreQueryFilters().Where(a => a.UserId == uid).ExecuteDeleteAsync();

                var user = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == uid);
                string templateKey;
                if (user.RoleTemplateId.HasValue)
                {
                    templateKey = await _db.RoleTemplates.IgnoreQueryFilters()
                        .Where(rt => rt.Id == user.RoleTemplateId.Value)
                        .Select(rt => rt.Key)
                        .FirstOrDefaultAsync() ?? Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(user.Role, null);
                }
                else
                {
                    templateKey = Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(user.Role, null);
                }

                var scope = await _grantService.BuildRoleTemplateScopeAsync(templateKey, companyId, jobTypeId: null);
                await _grantService.AssignRoleTemplateGrantsAsync(uid, templateKey, scope, grantedByUserId: actingAdminId);
            }

            var save2 = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "CompanyMoleculeMove", companyId);
            if (!save2.Success)
            {
                await tx.RollbackAsync();
                return new CompanyMoveResult(false, "Error_ConcurrencyConflict");
            }

            // --- Audit both molecules so admins on either side can see the move. ---
            var detail = $"companyId={companyId};from={sourceMoleculeId};to={targetMoleculeId};by={actingAdminId};users={userIds.Count}";
            await _auditLogService.LogUserActionAsync(actingAdminId, companyId, "CompanyMovedMolecule", "Company", companyId,
                $"Company {companyId} moved from molecule {sourceMoleculeId} to {targetMoleculeId}", detail);

            await tx.CommitAsync();
            return new CompanyMoveResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Company move failed: company {CompanyId} -> molecule {MoleculeId}", companyId, targetMoleculeId);
            try { await tx.RollbackAsync(); } catch { /* tx already done */ }
            return new CompanyMoveResult(false, "Error_MoveFailed");
        }
    }
}
