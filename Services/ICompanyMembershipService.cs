using ShiftManager.Models;

namespace ShiftManager.Services;

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

    /// <summary>Add a non-primary membership. Throws if an active membership already exists for (userId, companyId).</summary>
    Task<CompanyMembership> AddMembershipAsync(
        int userId, int companyId, int? roleTemplateId, int? jobTypeId, int? departmentId,
        bool doesShifts, int? homeTypeId, int actingAdminId);

    /// <summary>Soft-delete a membership. (Orphan-cleanup wiring lands in Epic 4.)</summary>
    Task RemoveMembershipAsync(int membershipId, int actingAdminId);

    /// <summary>Promote (userId, companyId) to primary, demote the rest, and sync AppUser.CompanyId.
    /// Throws if the user has no active membership in that company.</summary>
    Task SetPrimaryAsync(int userId, int companyId);
}
