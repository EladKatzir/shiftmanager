using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class DirectorService : IDirectorService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DirectorService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? CurrentUser => _httpContextAccessor.HttpContext?.User;

    private int? CurrentUserId
    {
        get
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = CurrentUser?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim != null && int.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }
            return null;
        }
    }

    public bool IsDirector()
    {
        return (CurrentUser?.IsInRole(nameof(UserRole.Owner)) ?? false)
            || (CurrentUser?.IsInRole(nameof(UserRole.Director)) ?? false);
    }

    public async Task<bool> IsDirectorOfAsync(int companyId)
    {
        if (!IsDirector() || CurrentUserId == null)
            return false;

        // Owner has access to all companies
        if (CurrentUser?.IsInRole(nameof(UserRole.Owner)) ?? false)
            return true;

        return await _db.DirectorCompanies
            .AnyAsync(dc => dc.UserId == CurrentUserId.Value
                         && dc.CompanyId == companyId
                         && !dc.IsDeleted);
    }

    public async Task<List<int>> GetDirectorCompanyIdsAsync()
    {
        if (CurrentUserId == null)
            return new List<int>();

        return await GetDirectorCompanyIdsAsync(CurrentUserId.Value);
    }

    public async Task<List<int>> GetDirectorCompanyIdsAsync(int userId)
    {
        return await _db.DirectorCompanies
            .Where(dc => dc.UserId == userId && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();
    }

    public async Task<bool> CanManageCompanyAsync(int companyId)
    {
        if (CurrentUserId == null)
            return false;

        // Owner can manage any company
        if (CurrentUser?.IsInRole(nameof(UserRole.Owner)) ?? false)
            return true;

        // Check if user is Director of this company
        if (IsDirector())
        {
            var isDirectorOf = await IsDirectorOfAsync(companyId);
            if (isDirectorOf)
                return true;
        }

        // Check if user is Manager of this company
        if (CurrentUser?.IsInRole(nameof(UserRole.Manager)) ?? false)
        {
            var user = await _db.Users.FindAsync(CurrentUserId.Value);
            return user?.CompanyId == companyId;
        }

        return false;
    }

    public bool CanAssignRole(string role)
    {
        // Safe string overload - try parse and delegate to strongly-typed version
        if (string.IsNullOrWhiteSpace(role))
            return false;

        // Try to parse the role string (case-insensitive)
        if (Enum.TryParse<UserRole>(role.Trim(), ignoreCase: true, out var targetRole))
        {
            return CanAssignRole(targetRole);
        }

        // Invalid role string
        return false;
    }

    public bool CanAssignRole(UserRole targetRole)
    {
        if (CurrentUser == null)
            return false;

        // Owner can assign any role
        if (CurrentUser.IsInRole(nameof(UserRole.Owner)))
            return true;

        // Director can assign Employee, Manager, Director, Trainee (but NOT Owner)
        if (CurrentUser.IsInRole(nameof(UserRole.Director)))
        {
            return targetRole == UserRole.Employee
                || targetRole == UserRole.Manager
                || targetRole == UserRole.Director
                || targetRole == UserRole.Trainee;
        }

        // Manager can assign Employee and Trainee
        if (CurrentUser.IsInRole(nameof(UserRole.Manager)))
        {
            return targetRole == UserRole.Employee
                || targetRole == UserRole.Trainee;
        }

        // Employee cannot assign any role
        return false;
    }
}
