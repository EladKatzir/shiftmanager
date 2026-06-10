using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class CompanyMembershipService : ICompanyMembershipService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CompanyMembershipService> _logger;

    public CompanyMembershipService(AppDbContext db, ILogger<CompanyMembershipService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CompanyMembership>> GetMembershipsAsync(int userId)
    {
        // IgnoreQueryFilters: CompanyMembership has no tenant filter, but navigations touch
        // AppUser which does — and we must work before an active tenant is chosen.
        return await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && !m.IsDeleted)
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.CompanyId)
            .ToListAsync();
    }

    public async Task<bool> IsMemberAsync(int userId, int companyId)
    {
        return await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .AnyAsync(m => m.UserId == userId && m.CompanyId == companyId && !m.IsDeleted);
    }

    public async Task<CompanyMembership> AddMembershipAsync(
        int userId, int companyId, int? roleTemplateId, int? jobTypeId, int? departmentId,
        bool doesShifts, int? homeTypeId, int actingAdminId)
    {
        var exists = await IsMemberAsync(userId, companyId);
        if (exists)
            throw new InvalidOperationException(
                $"User {userId} already has an active membership in company {companyId}.");

        var membership = new CompanyMembership
        {
            UserId = userId,
            CompanyId = companyId,
            RoleTemplateId = roleTemplateId,
            JobTypeId = jobTypeId,
            DepartmentId = departmentId,
            DoesShifts = doesShifts,
            HomeTypeId = homeTypeId,
            IsPrimary = false,           // additional memberships are never primary; use SetPrimaryAsync
            GrantedBy = actingAdminId
        };
        _db.CompanyMemberships.Add(membership);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Race backstop: a concurrent add slipped past the IsMemberAsync check and hit the
            // (UserId, CompanyId) unique filtered index. Translate to the documented contract exception.
            throw new InvalidOperationException(
                $"User {userId} already has an active membership in company {companyId}.", ex);
        }
        _logger.LogInformation("Added membership: user {UserId} → company {CompanyId} (by {ActorId})", userId, companyId, actingAdminId);
        return membership;
    }

    public async Task RemoveMembershipAsync(int membershipId, int actingAdminId)
    {
        var membership = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == membershipId && !m.IsDeleted);
        if (membership == null) return;

        if (membership.IsPrimary)
            throw new InvalidOperationException(
                "Cannot remove the primary membership; promote another membership to primary first.");

        membership.IsDeleted = true;
        membership.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Soft-deleted membership {MembershipId} (user {UserId}, company {CompanyId}) by {ActorId}", membership.Id, membership.UserId, membership.CompanyId, actingAdminId);
        // actingAdminId is reserved for the Epic 4 audit / orphan-cleanup pass (no DeletedBy column yet).
        // Orphan-cleanup (future shifts/requests/grants in this company) lands in Epic 4.
    }

    public async Task SetPrimaryAsync(int userId, int companyId)
    {
        // Scope the whole read/check-then-write sequence inside the transaction to close the
        // TOCTOU window between the membership-existence check and the primary flip.
        // The early throw below rolls the (empty) transaction back via `await using`.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var memberships = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && !m.IsDeleted)
            .ToListAsync();

        var target = memberships.FirstOrDefault(m => m.CompanyId == companyId);
        if (target == null)
            throw new InvalidOperationException(
                $"User {userId} has no active membership in company {companyId}.");

        foreach (var m in memberships)
            m.IsPrimary = m.CompanyId == companyId;

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user != null)
            user.CompanyId = companyId; // keep the home pointer in sync with the primary membership

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        _logger.LogInformation("Set primary company for user {UserId} → {CompanyId}", userId, companyId);
    }

    /// <inheritdoc/>
    public async Task<MembershipRemovalImpact> GetRemovalImpactAsync(int userId, int companyId)
    {
        // SECURITY-AUDITED: all counts are scoped to BOTH UserId == userId AND CompanyId == companyId.
        // IgnoreQueryFilters() is required for cross-tenant reads (matches UserCompanyTransferService pattern).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Future shift assignments in this company (joins ShiftInstance for the date predicate).
        var futureShifts = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.UserId == userId
                && sa.CompanyId == companyId
                && sa.ShiftInstance!.WorkDate >= today);

        // Pending or future-approved time-off in this company.
        var timeOff = await _db.TimeOffRequests.IgnoreQueryFilters()
            .CountAsync(t => t.UserId == userId
                && t.CompanyId == companyId
                && (t.Status == RequestStatus.Pending
                    || t.Status == RequestStatus.PendingSecondApproval
                    || (t.Status == RequestStatus.Approved && t.EndDate >= today)));

        // Future active chores in this company.
        var chores = await _db.Chores.IgnoreQueryFilters()
            .CountAsync(c => c.UserId == userId
                && c.CompanyId == companyId
                && c.Date >= today
                && c.CanceledAt == null);

        // Open swap requests in this company (user is either side of the swap).
        var swaps = await _db.SwapRequests.IgnoreQueryFilters()
            .CountAsync(sr => sr.CompanyId == companyId
                && (sr.FromUserId == userId || sr.ToUserId == userId)
                && (sr.Status == RequestStatus.Pending
                    || sr.Status == RequestStatus.PendingSecondApproval));

        // NOTE: OnDuty has no CompanyId (it is globally-scoped by design).
        // We cannot attribute an OnDuty record to a specific company, so FutureOnDuty is always 0
        // in company-scoped impact. Company-wide removal (UserCompanyTransferService) handles it
        // for full moves. SECURITY-AUDITED: omission is intentional — no data loss occurs because
        // the user may still be on-duty for other companies after this secondary membership is removed.
        const int futureOnDuty = 0;

        // Grants scoped to this company.
        var grants = await _db.Grants.IgnoreQueryFilters()
            .CountAsync(g => g.UserId == userId && g.CompanyId == companyId);

        return new MembershipRemovalImpact(futureShifts, timeOff, chores, swaps, futureOnDuty, grants);
    }

    /// <inheritdoc/>
    public async Task<int?> UpdateMembershipAsync(int membershipId, int? roleTemplateId, int? jobTypeId, bool doesShifts, int actingAdminId)
    {
        var membership = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == membershipId && !m.IsDeleted);

        if (membership == null)
            throw new InvalidOperationException($"Membership {membershipId} not found or already deleted.");

        if (membership.IsPrimary)
            throw new InvalidOperationException(
                "Cannot update the primary membership via UpdateMembershipAsync; primary edits go through the user-row handlers that sync AppUser.");

        var oldRoleTemplateId = membership.RoleTemplateId;

        membership.RoleTemplateId = roleTemplateId;
        membership.JobTypeId = jobTypeId;
        membership.DoesShifts = doesShifts;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Updated membership {MembershipId} (user {UserId}, company {CompanyId}): roleTemplate {Old}→{New}, jobType {JobType}, doesShifts {DoesShifts} (by admin {ActorId})",
            membership.Id, membership.UserId, membership.CompanyId,
            oldRoleTemplateId, roleTemplateId, jobTypeId, doesShifts, actingAdminId);

        return oldRoleTemplateId;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveMembershipWithCleanupAsync(int userId, int companyId, int actingAdminId)
    {
        // SECURITY-AUDITED: all deletes/updates are scoped to BOTH UserId == userId AND
        // CompanyId == companyId. IgnoreQueryFilters() is required for cross-tenant writes.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        // Load the membership to validate it exists and is not primary.
        var membership = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.CompanyId == companyId && !m.IsDeleted);

        if (membership == null)
        {
            _logger.LogWarning(
                "RemoveMembershipWithCleanupAsync: no active membership for user {UserId} in company {CompanyId}",
                userId, companyId);
            return false;
        }

        if (membership.IsPrimary)
            throw new InvalidOperationException(
                "Cannot remove the primary membership; promote another membership to primary first.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // ── BUCKET 1: personal operational data scoped to this company ──────────────────

            // Future shift assignments: delete to free the slot.
            // SECURITY-AUDITED: UserId == userId AND CompanyId == companyId; future via ShiftInstance.WorkDate.
            await _db.ShiftAssignments.IgnoreQueryFilters()
                .Where(sa => sa.UserId == userId
                    && sa.CompanyId == companyId
                    && sa.ShiftInstance!.WorkDate >= today)
                .ExecuteDeleteAsync();

            // Pending / future-approved time-off in this company → Canceled.
            // SECURITY-AUDITED: UserId == userId AND CompanyId == companyId.
            await _db.TimeOffRequests.IgnoreQueryFilters()
                .Where(t => t.UserId == userId
                    && t.CompanyId == companyId
                    && (t.Status == RequestStatus.Pending
                        || t.Status == RequestStatus.PendingSecondApproval
                        || (t.Status == RequestStatus.Approved && t.EndDate >= today)))
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.Status, RequestStatus.Canceled));

            // Future chores in this company → soft-cancel.
            // SECURITY-AUDITED: UserId == userId AND CompanyId == companyId.
            await _db.Chores.IgnoreQueryFilters()
                .Where(c => c.UserId == userId
                    && c.CompanyId == companyId
                    && c.Date >= today
                    && c.CanceledAt == null)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.CanceledAt, (DateTime?)now));

            // Open swap requests in this company → Canceled.
            // SECURITY-AUDITED: CompanyId == companyId AND user is either side.
            await _db.SwapRequests.IgnoreQueryFilters()
                .Where(sr => sr.CompanyId == companyId
                    && (sr.FromUserId == userId || sr.ToUserId == userId)
                    && (sr.Status == RequestStatus.Pending
                        || sr.Status == RequestStatus.PendingSecondApproval))
                .ExecuteUpdateAsync(sr => sr.SetProperty(x => x.Status, RequestStatus.Canceled));

            // NOTE: OnDuty has no CompanyId — cannot be company-scoped. Intentionally skipped.
            // See GetRemovalImpactAsync for full explanation.

            // ── BUCKET 2: grants scoped to this company ───────────────────────────────────

            // Delete grants scoped to this company.
            // SECURITY-AUDITED: UserId == userId AND CompanyId == companyId.
            await _db.Grants.IgnoreQueryFilters()
                .Where(g => g.UserId == userId && g.CompanyId == companyId)
                .ExecuteDeleteAsync();

            // ── Soft-delete the membership ────────────────────────────────────────────────

            membership.IsDeleted = true;
            membership.DeletedAt = now;
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            _logger.LogInformation(
                "RemoveMembershipWithCleanupAsync: removed membership for user {UserId} in company {CompanyId} by admin {ActorId}",
                userId, companyId, actingAdminId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "RemoveMembershipWithCleanupAsync failed: user {UserId}, company {CompanyId}",
                userId, companyId);
            try { await tx.RollbackAsync(); } catch { /* tx already done */ }
            throw;
        }
    }
}
