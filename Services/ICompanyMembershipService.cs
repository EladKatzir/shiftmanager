using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Read-only counts of what removing the user's membership in a specific company would clear.
/// FutureOnDuty is always 0 because OnDuty is globally-scoped (no CompanyId) — it cannot be
/// attributed to a single company and is therefore not touched by company-scoped removal.
/// </summary>
public record MembershipRemovalImpact(
    int FutureShifts,
    int PendingOrFutureTimeOff,
    int FutureChores,
    int OpenSwapRequests,
    int FutureOnDuty,
    int GrantsRemoved);

/// <summary>
/// Manages a user's company memberships (the user ↔ company N:N). The IsPrimary membership
/// mirrors AppUser.CompanyId; this service is the single write-path that keeps them in sync,
/// enforcing the "exactly one primary per user" invariant.
/// NOTE (epic boundaries): grant application on add is Epic 5; full orphan-cleanup on remove
/// is Epic 4 (reuses UserCompanyTransferService). RemoveMembershipAsync here is soft-delete only.
/// </summary>
public interface ICompanyMembershipService
{
    /// <summary>All active (non-deleted) memberships for the user, primary first.</summary>
    Task<IReadOnlyList<CompanyMembership>> GetMembershipsAsync(int userId);

    /// <summary>True if the user has an active membership in the company.</summary>
    Task<bool> IsMemberAsync(int userId, int companyId);

    /// <summary>Add a non-primary membership. Throws InvalidOperationException if an active membership
    /// already exists for (userId, companyId) — including the concurrent-add race, caught at the unique index.</summary>
    Task<CompanyMembership> AddMembershipAsync(
        int userId, int companyId, int? roleTemplateId, int? jobTypeId, int? departmentId,
        bool doesShifts, int? homeTypeId, int actingAdminId);

    /// <summary>Soft-delete a membership. (Orphan-cleanup wiring lands in Epic 4.)</summary>
    Task RemoveMembershipAsync(int membershipId, int actingAdminId);

    /// <summary>Promote (userId, companyId) to primary, demote the rest, and sync AppUser.CompanyId.
    /// Throws if the user has no active membership in that company.</summary>
    Task SetPrimaryAsync(int userId, int companyId);

    /// <summary>
    /// Returns a read-only count of what removing the user's membership in the given company would
    /// clear. Mirrors <see cref="IUserCompanyTransferService.GetMoveImpactAsync"/> but scoped to
    /// a single company. Does NOT mutate any data.
    /// </summary>
    Task<MembershipRemovalImpact> GetRemovalImpactAsync(int userId, int companyId);

    /// <summary>
    /// Clears all of the user's records scoped to the given company (future shifts, pending/future
    /// time-off, future chores, open swap requests, grants), then soft-deletes the membership —
    /// all inside a single transaction.
    /// Returns false if the user has no active membership in that company.
    /// Throws <see cref="InvalidOperationException"/> if the membership is the primary
    /// (promote another membership to primary first, then remove this one).
    /// </summary>
    Task<bool> RemoveMembershipWithCleanupAsync(int userId, int companyId, int actingAdminId);

    /// <summary>
    /// Updates an ADDITIONAL (non-primary) membership's role template, job type, and does-shifts
    /// flag WITHOUT touching operational records (shifts, time-off, chores, swap requests).
    /// Grant reconciliation is performed in the handler (which has access to BuildGrantScopeForTemplateAsync).
    /// Throws <see cref="InvalidOperationException"/> if the membership is the primary or not found.
    /// Returns the old RoleTemplateId so the handler can do a company-scoped grant reconcile.
    /// </summary>
    Task<int?> UpdateMembershipAsync(int membershipId, int? roleTemplateId, int? jobTypeId, bool doesShifts, int actingAdminId);
}
