using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
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

    public Task<MoveImpact> GetMoveImpactAsync(int userId, int destCompanyId)
        => throw new NotImplementedException();

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

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // --- BUCKET 1: personal operational data (soft-cancel where supported, else delete) ---

            // Future shift assignments: delete to free the slot. SECURITY-AUDITED: scoped to userId; future via ShiftInstance.WorkDate.
            await _db.ShiftAssignments.IgnoreQueryFilters()
                .Where(sa => sa.UserId == userId && sa.ShiftInstance!.WorkDate >= today)
                .ExecuteDeleteAsync();

            // This user as trainer of others -> null the link. SECURITY-AUDITED: scoped to TraineeUserId == userId.
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
            // --- BUCKET 3 (Task 6): owned team calendars ---
            // --- SCALAR FK RESET + CompanyId (Task 7) ---
            user.CompanyId = destCompanyId;
            // --- GRANTS + DirectorCompany + UserRoleAssignment (Task 8) ---
            // --- AVATAR (Task 9) ---
            // --- AUDIT (Task 10) ---

            var save = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "UserCompanyMove", userId);
            if (!save.Success)
            {
                await tx.RollbackAsync();
                return new MoveResult(false, "Error_ConcurrencyConflict");
            }

            await tx.CommitAsync();
            return new MoveResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "User move failed: user {UserId} -> company {DestCompanyId}", userId, destCompanyId);
            try { await tx.RollbackAsync(); } catch { /* tx already done */ }
            return new MoveResult(false, "Error_MoveFailed");
        }
    }
}
