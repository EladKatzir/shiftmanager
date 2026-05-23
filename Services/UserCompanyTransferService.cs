using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class UserCompanyTransferService : IUserCompanyTransferService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IConcurrencyService _concurrencyService;
    private readonly IAuditLogService _auditLogService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<UserCompanyTransferService> _logger;

    public UserCompanyTransferService(
        AppDbContext db,
        IGrantService grantService,
        IConcurrencyService concurrencyService,
        IAuditLogService auditLogService,
        IWebHostEnvironment env,
        ILogger<UserCompanyTransferService> logger)
    {
        _db = db;
        _grantService = grantService;
        _concurrencyService = concurrencyService;
        _auditLogService = auditLogService;
        _env = env;
        _logger = logger;
    }

    public async Task<MoveImpact> GetMoveImpactAsync(int userId, int destCompanyId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var user = await _db.Users.IgnoreQueryFilters().AsNoTracking() // SECURITY-AUDITED: preview by explicit id
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return new MoveImpact(0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false, false,
                Array.Empty<string>(), new[] { "User not found" });

        // SECURITY-AUDITED: each count scoped to userId; read-only preview, mirrors the move's predicates.
        var futureShifts = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.UserId == userId && sa.ShiftInstance!.WorkDate >= today);
        var timeOff = await _db.TimeOffRequests.IgnoreQueryFilters()
            .CountAsync(t => t.UserId == userId && (t.Status == RequestStatus.Pending
                || t.Status == RequestStatus.PendingSecondApproval
                || (t.Status == RequestStatus.Approved && t.EndDate >= today)));
        var chores = await _db.Chores.IgnoreQueryFilters()
            .CountAsync(c => c.UserId == userId && c.Date >= today && c.CanceledAt == null);
        var swaps = await _db.SwapRequests.IgnoreQueryFilters()
            .CountAsync(sr => (sr.FromUserId == userId || sr.ToUserId == userId)
                && (sr.Status == RequestStatus.Pending || sr.Status == RequestStatus.PendingSecondApproval));
        var onDuty = await _db.OnDuties.IgnoreQueryFilters()
            .CountAsync(o => o.UserId == userId && o.Date >= today && o.CanceledAt == null);
        var games = await _db.GameScores.IgnoreQueryFilters().CountAsync(g => g.UserId == userId);
        var ownedCals = await _db.TeamCalendars.IgnoreQueryFilters().CountAsync(t => t.OwnerId == userId);
        var apiKeys = await _db.ApiKeys.IgnoreQueryFilters().CountAsync(k => k.CreatedBy == userId);
        var approverRules = await _db.VacationApprovalRules.IgnoreQueryFilters().CountAsync(r => r.ApproverUserId == userId);

        var directorCompanies = await _db.DirectorCompanies.IgnoreQueryFilters()
            .Where(d => d.UserId == userId)
            .Join(_db.Companies.IgnoreQueryFilters(), d => d.CompanyId, c => c.Id, (d, c) => c.Name)
            .ToListAsync();

        return new MoveImpact(
            futureShifts, timeOff, chores, swaps, onDuty, games, ownedCals, apiKeys, approverRules,
            WillResetJobType: user.JobTypeId.HasValue,
            WillResetDepartment: user.DepartmentId.HasValue,
            WillResetPrimaryShiftType: user.PrimaryShiftTypeId.HasValue,
            WillResetHomeType: user.HomeTypeId.HasValue,
            DirectorCompaniesRemoved: directorCompanies,
            Warnings: Array.Empty<string>());
    }

    public async Task<MoveResult> MoveUserToCompanyAsync(int userId, int destCompanyId, int actingAdminId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        var user = await _db.Users.IgnoreQueryFilters() // SECURITY-AUDITED: load move target by explicit id
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return new MoveResult(false, "Error_UserNotFound");

        var dest = await _db.Companies.IgnoreQueryFilters() // SECURITY-AUDITED: validate dest by explicit id
            .FirstOrDefaultAsync(c => c.Id == destCompanyId);
        if (dest == null) return new MoveResult(false, "Error_CompanyNotFound");
        if (dest.IsHeadquarters) return new MoveResult(false, "Error_MoveDestHq");
        if (user.CompanyId == destCompanyId) return new MoveResult(false, "Error_MoveSameCompany");

        var sourceCompanyId = user.CompanyId;
        var copiedDestPaths = new List<string>(); // dest avatar files copied pre-commit; cleaned up if the tx rolls back

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // --- BUCKET 1: personal operational data (soft-cancel where supported, else delete) ---

            // Future shift assignments: delete to free the slot. SECURITY-AUDITED: scoped to userId; future via ShiftInstance.WorkDate.
            // IgnoreQueryFilters() is query-global in EF Core (disables the ShiftInstance nav filter too) — verified by Move_WithActiveFilters_DifferentTenant_StillClearsFutureShift
            await _db.ShiftAssignments.IgnoreQueryFilters()
                .Where(sa => sa.UserId == userId && sa.ShiftInstance!.WorkDate >= today)
                .ExecuteDeleteAsync();

            // Detach this user from any shift where they are the shadowing trainee (TraineeUserId). SECURITY-AUDITED: scoped to TraineeUserId == userId.
            await _db.ShiftAssignments.IgnoreQueryFilters()
                .Where(sa => sa.TraineeUserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.TraineeUserId, (int?)null));

            // Pending / future-approved time-off -> Canceled. SECURITY-AUDITED: scoped to userId.
            await _db.TimeOffRequests.IgnoreQueryFilters()
                .Where(t => t.UserId == userId
                    && (t.Status == RequestStatus.Pending
                        || t.Status == RequestStatus.PendingSecondApproval
                        || (t.Status == RequestStatus.Approved && t.EndDate >= today)))
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.Status, RequestStatus.Canceled));

            // Future chores -> soft cancel. SECURITY-AUDITED: scoped to userId.
            await _db.Chores.IgnoreQueryFilters()
                .Where(c => c.UserId == userId && c.Date >= today && c.CanceledAt == null)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.CanceledAt, (DateTime?)now));

            // Future on-duty -> soft cancel. SECURITY-AUDITED: scoped to userId.
            await _db.OnDuties.IgnoreQueryFilters()
                .Where(o => o.UserId == userId && o.Date >= today && o.CanceledAt == null)
                .ExecuteUpdateAsync(o => o.SetProperty(x => x.CanceledAt, (DateTime?)now));

            // Open swap requests involving the user (From or To) -> Canceled. SECURITY-AUDITED: scoped to userId.
            await _db.SwapRequests.IgnoreQueryFilters()
                .Where(sr => (sr.FromUserId == userId || sr.ToUserId == userId)
                    && (sr.Status == RequestStatus.Pending || sr.Status == RequestStatus.PendingSecondApproval))
                .ExecuteUpdateAsync(sr => sr.SetProperty(x => x.Status, RequestStatus.Canceled));

            // Delete transient/company-specific rows. SECURITY-AUDITED: each scoped to userId.
            await _db.UserNotifications.IgnoreQueryFilters().Where(n => n.UserId == userId).ExecuteDeleteAsync();
            await _db.GameScores.IgnoreQueryFilters().Where(g => g.UserId == userId).ExecuteDeleteAsync();
            await _db.OnDutyRoleSubscriptions.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync();
            await _db.DailyNotificationPreferences.IgnoreQueryFilters().Where(p => p.UserId == userId).ExecuteDeleteAsync();
            await _db.HomeTypeOverrides.IgnoreQueryFilters().Where(h => h.UserId == userId).ExecuteDeleteAsync();
            await _db.FeatureFlags.IgnoreQueryFilters().Where(f => f.UserId == userId).ExecuteDeleteAsync();
            await _db.DutyRotationEntries.IgnoreQueryFilters().Where(d => d.UserId == userId).ExecuteDeleteAsync();
            await _db.CalendarTextEntries.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync();
            await _db.TeamCalendarMembers.IgnoreQueryFilters().Where(m => m.MemberUserId == userId).ExecuteDeleteAsync();

            // --- BUCKET 2 (Task 5): credentials/obligations ---
            // BUCKET 2: credentials & active obligations.
            // API keys are credentials for the OLD company — revoke. SECURITY-AUDITED: scoped to CreatedBy == userId.
            await _db.ApiKeys.IgnoreQueryFilters().Where(k => k.CreatedBy == userId).ExecuteDeleteAsync();
            // Pending API key requests by the user -> delete. SECURITY-AUDITED: scoped to RequestedBy == userId.
            await _db.ApiKeyRequests.IgnoreQueryFilters()
                .Where(r => r.RequestedBy == userId && r.Status == ApiKeyRequestStatus.Pending)
                .ExecuteDeleteAsync();
            // User is a named approver in old-company rules -> null the named approver (rule stays).
            // SECURITY-AUDITED: scoped to ApproverUserId == userId.
            await _db.VacationApprovalRules.IgnoreQueryFilters()
                .Where(r => r.ApproverUserId == userId)
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.ApproverUserId, (int?)null));
            // --- BUCKET 3 (Task 6): owned team calendars ---
            // Shared asset the old company still needs: keep the calendar, transfer ownership to the acting admin.
            // SECURITY-AUDITED: scoped to OwnerId == userId.
            await _db.TeamCalendars.IgnoreQueryFilters()
                .Where(t => t.OwnerId == userId)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.OwnerId, actingAdminId));

            // --- SCALAR FK RESET + CompanyId (Task 7) ---
            // Scalar FKs point at old-molecule/area-scoped rows — reset (admin re-assigns in dest).
            user.JobTypeId = null;
            user.DepartmentId = null;
            user.PrimaryShiftTypeId = null;
            user.HomeTypeId = null;
            // RoleTemplateId is global/cross-tenant — KEEP it.
            user.CompanyId = destCompanyId;
            // --- GRANTS + DirectorCompany + UserRoleAssignment (Task 8) ---
            // Resolve the role-template key (RoleTemplateId is kept). Fall back to the role mapper for legacy users.
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

            // Revoke all grants (Grant has no query filter; ExecuteDelete runs immediately within the tx).
            // SECURITY-AUDITED: scoped to UserId == userId.
            await _db.Grants.IgnoreQueryFilters().Where(g => g.UserId == userId).ExecuteDeleteAsync();

            // Remove old DirectorCompany mappings. SECURITY-AUDITED: scoped to UserId == userId.
            await _db.DirectorCompanies.IgnoreQueryFilters().Where(d => d.UserId == userId).ExecuteDeleteAsync();

            // Remove old role-assignment records. SECURITY-AUDITED: scoped to UserId == userId.
            await _db.UserRoleAssignments.IgnoreQueryFilters().Where(a => a.UserId == userId).ExecuteDeleteAsync();

            // Build the destination scope and re-apply the template's grants for the new hierarchy.
            var destScope = await _grantService.BuildRoleTemplateScopeAsync(templateKey, destCompanyId, jobTypeId: null);
            await _grantService.AssignRoleTemplateGrantsAsync(userId, templateKey, destScope, grantedByUserId: actingAdminId);

            // Record a destination-scoped role assignment (mirrors RoleService's assignment record).
            if (user.RoleTemplateId.HasValue)
            {
                _db.UserRoleAssignments.Add(new UserRoleAssignment
                {
                    UserId = userId,
                    RoleTemplateId = user.RoleTemplateId.Value,
                    CompanyId = destScope.CompanyId,
                    DepartmentId = destScope.DepartmentId,
                    MoleculeId = destScope.MoleculeId,
                    AreaId = destScope.AreaId,
                    JobTypeId = destScope.JobTypeId,
                    AssignedByUserId = actingAdminId,
                    AssignedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            // If the destination role is a director-type, recreate a DirectorCompany at the dest molecule HQ.
            if (user.Role == UserRole.Director || user.Role == UserRole.AreaAdmin)
            {
                var destHqId = await _db.Companies.IgnoreQueryFilters() // SECURITY-AUDITED: HQ lookup by dest molecule
                    .Where(c => c.MoleculeId == dest.MoleculeId && c.IsHeadquarters)
                    .Select(c => c.Id).FirstOrDefaultAsync();
                if (destHqId != 0)
                {
                    _db.DirectorCompanies.Add(new DirectorCompany
                    {
                        UserId = userId,
                        CompanyId = destHqId,
                        GrantedBy = actingAdminId,
                        GrantedAt = DateTime.UtcNow,
                        IsDeleted = false
                    });
                }
            }
            // --- AVATAR (Task 9) ---
            // Copy avatar files to the dest folder BEFORE commit. AvatarService.GetAvatarUrl resolves the
            // path via the CURRENT tenant, so the file must already exist under the destination company.
            if (!string.IsNullOrEmpty(user.AvatarFileName))
            {
                var srcAvatarDir = Path.Combine(_env.WebRootPath, "avatars", sourceCompanyId.ToString());
                var destAvatarDir = Path.Combine(_env.WebRootPath, "avatars", destCompanyId.ToString());
                Directory.CreateDirectory(destAvatarDir);
                foreach (var name in new[] { $"{userId}.jpg", $"{userId}_thumb.jpg" })
                {
                    var from = Path.Combine(srcAvatarDir, name);
                    var to = Path.Combine(destAvatarDir, name);
                    if (File.Exists(from))
                    {
                        File.Copy(from, to, overwrite: true);
                        copiedDestPaths.Add(to);
                    }
                }
            }

            // --- AUDIT (Task 10) ---
            // Two entries with EXPLICIT companyId so BOTH old- and new-company admins can see the move.
            var auditDetail = $"userId={userId};from={sourceCompanyId};to={destCompanyId};by={actingAdminId}";
            await _auditLogService.LogUserActionAsync(actingAdminId, sourceCompanyId, "UserMovedOut", "User", userId,
                $"User {userId} moved to company {destCompanyId}", auditDetail);
            await _auditLogService.LogUserActionAsync(actingAdminId, destCompanyId, "UserMovedIn", "User", userId,
                $"User {userId} moved from company {sourceCompanyId}", auditDetail);

            var save = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "UserCompanyMove", userId);
            if (!save.Success)
            {
                await tx.RollbackAsync();
                CleanupCopiedFiles(copiedDestPaths);
                return new MoveResult(false, "Error_ConcurrencyConflict");
            }

            await tx.CommitAsync();

            // Post-commit: remove old-folder avatar copies (DB is already correct; best-effort).
            if (!string.IsNullOrEmpty(user.AvatarFileName))
            {
                var oldAvatarDir = Path.Combine(_env.WebRootPath, "avatars", sourceCompanyId.ToString());
                foreach (var name in new[] { $"{userId}.jpg", $"{userId}_thumb.jpg" })
                {
                    var p = Path.Combine(oldAvatarDir, name);
                    try { if (File.Exists(p)) File.Delete(p); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { _logger.LogWarning(ex, "Failed deleting old avatar {Path}", p); }
                }
            }
            return new MoveResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "User move failed: user {UserId} -> company {DestCompanyId}", userId, destCompanyId);
            try { await tx.RollbackAsync(); } catch { /* tx already done */ }
            CleanupCopiedFiles(copiedDestPaths); // a rolled-back move must not leave orphan files in the dest folder
            return new MoveResult(false, "Error_MoveFailed");
        }
    }

    private void CleanupCopiedFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            try { if (File.Exists(p)) File.Delete(p); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _logger.LogWarning(ex, "Failed cleaning up copied avatar {Path}", p); }
        }
    }
}
