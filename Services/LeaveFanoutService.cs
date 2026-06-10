using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Fan-out service for multi-company leave requests.
/// Single-company users (or multi-company users who do shifts in only one company) are
/// completely unaffected — LeaveGroupId stays null, one row, byte-for-byte identical to
/// the pre-Epic-6 path. Fan-out only fires when the filer has ≥2 active memberships with
/// DoesShifts == true.
/// </summary>
public sealed class LeaveFanoutService : ILeaveFanoutService
{
    private readonly AppDbContext _db;
    private readonly ICompanyMembershipService _membershipService;

    public LeaveFanoutService(AppDbContext db, ICompanyMembershipService membershipService)
    {
        _db = db;
        _membershipService = membershipService;
    }

    /// <inheritdoc/>
    public async Task<(Guid? GroupId, IReadOnlyList<TimeOffRequest> Clones)> FanOutAsync(
        TimeOffRequest primary, int userId)
    {
        // Load all active memberships for the user and filter to those where DoesShifts == true.
        var memberships = await _membershipService.GetMembershipsAsync(userId);
        var shiftCompanyIds = memberships
            .Where(m => m.DoesShifts)
            .Select(m => m.CompanyId)
            .Distinct()
            .ToList();

        // Identify companies other than the primary's own company.
        var additionalCompanyIds = shiftCompanyIds
            .Where(cid => cid != primary.CompanyId)
            .ToList();

        // Single shift-company (99% path): no fan-out, leave untouched.
        if (additionalCompanyIds.Count == 0)
            return (null, Array.Empty<TimeOffRequest>());

        // Multi-company: assign a shared group id and create one clone per additional company.
        var groupId = Guid.NewGuid();

        // Transaction-AGNOSTIC: this method does NOT open its own transaction. It stamps the
        // primary's LeaveGroupId + adds clones + SaveChangesAsync, participating in whatever
        // ambient transaction the caller has opened (Epic 6 spec §8: fan-out must be atomic with
        // the primary insert — if a clone fails the primary must NOT persist as a standalone
        // single-company leave). Called WITHOUT an ambient transaction (e.g. direct unit tests),
        // SaveChangesAsync still persists in its own implicit transaction, so the method works
        // standalone too.

        // Stamp the primary (already tracked by EF from the caller's SaveChangesAsync).
        primary.LeaveGroupId = groupId;

        var clones = new List<TimeOffRequest>(additionalCompanyIds.Count);
        foreach (var companyId in additionalCompanyIds)
        {
            var clone = new TimeOffRequest
            {
                // Explicit CompanyId — CompanyIdInterceptor only overwrites when CompanyId == 0,
                // so this value is preserved through SaveChangesAsync.
                CompanyId = companyId,
                UserId = primary.UserId,
                StartDate = primary.StartDate,
                EndDate = primary.EndDate,
                Type = primary.Type,
                Reason = primary.Reason,
                Label = primary.Label,
                Private = primary.Private,
                Status = primary.Status,
                CreatedAt = primary.CreatedAt,
                LeaveGroupId = groupId,
                // Each company routes its own approver — null so the normal routing logic applies.
                ApproverId = null
            };
            _db.TimeOffRequests.Add(clone);
            clones.Add(clone);
        }

        // One SaveChangesAsync covers: LeaveGroupId update on primary + all clone inserts.
        // No CommitAsync here — the caller's transaction (if any) owns the commit boundary.
        await _db.SaveChangesAsync();

        return (groupId, clones);
    }
}
