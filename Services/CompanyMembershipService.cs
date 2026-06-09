using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class CompanyMembershipService : ICompanyMembershipService
{
    private readonly AppDbContext _db;

    public CompanyMembershipService(AppDbContext db) => _db = db;

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
        await _db.SaveChangesAsync();
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
        // Orphan-cleanup (future shifts/requests/grants in this company) lands in Epic 4.
    }

    public async Task SetPrimaryAsync(int userId, int companyId)
    {
        var memberships = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && !m.IsDeleted)
            .ToListAsync();

        var target = memberships.FirstOrDefault(m => m.CompanyId == companyId);
        if (target == null)
            throw new InvalidOperationException(
                $"User {userId} has no active membership in company {companyId}.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var m in memberships)
            m.IsPrimary = m.CompanyId == companyId;

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user != null)
            user.CompanyId = companyId; // keep the home pointer in sync with the primary membership

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
