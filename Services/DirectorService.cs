using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class DirectorService : IDirectorService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IGrantService _grantService;

    // Grant keys used by this service
    private const string DirectorHubAccessGrant = "DirectorHubAccess";
    private const string ManagerHomeAccessGrant = "ManagerHomeAccess";
    private const string AssignRolesGrant = "AssignRoles";
    private const string AdminAccessGrant = "AdminAccess";

    public DirectorService(AppDbContext db, IHttpContextAccessor httpContextAccessor, IGrantService grantService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _grantService = grantService;
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
        if (CurrentUserId == null)
            return false;

        // Check if user has DirectorHubAccess grant
        // Note: We use GetAwaiter().GetResult() here because the interface is synchronous.
        // Converting IsDirector() and CanAssignRole() to async would require updating
        // IDirectorService and all call sites (Razor pages, middleware, authorization handlers).
        // This is a known sync-over-async pattern; acceptable here since these are short-lived
        // grant lookups, but should be refactored when callers are migrated to async.
        return _grantService.HasGrantAsync(CurrentUserId.Value, DirectorHubAccessGrant)
            .GetAwaiter().GetResult();
    }

    public async Task<bool> IsDirectorOfAsync(int companyId)
    {
        if (CurrentUserId == null)
            return false;

        // Check if user has DirectorHubAccess grant for the specified company
        return await _grantService.HasGrantForCompanyAsync(CurrentUserId.Value, DirectorHubAccessGrant, companyId);
    }

    public async Task<List<int>> GetDirectorCompanyIdsAsync()
    {
        if (CurrentUserId == null)
            return new List<int>();

        return await GetDirectorCompanyIdsAsync(CurrentUserId.Value);
    }

    public async Task<List<int>> GetDirectorCompanyIdsAsync(int userId)
    {
        // Use grant scope resolution to get accessible company IDs
        return await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, DirectorHubAccessGrant);
    }

    public async Task<bool> CanManageCompanyAsync(int companyId)
    {
        if (CurrentUserId == null)
            return false;

        // Check if user has DirectorHubAccess grant for this company (Directors)
        if (await _grantService.HasGrantForCompanyAsync(CurrentUserId.Value, DirectorHubAccessGrant, companyId))
            return true;

        // Check if user has ManagerHomeAccess grant for this company (Managers)
        if (await _grantService.HasGrantForCompanyAsync(CurrentUserId.Value, ManagerHomeAccessGrant, companyId))
            return true;

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
        if (CurrentUserId == null)
            return false;

        // Check if user has AssignRoles grant
        var hasAssignRolesGrant = _grantService.HasGrantAsync(CurrentUserId.Value, AssignRolesGrant)
            .GetAwaiter().GetResult();

        if (!hasAssignRolesGrant)
            return false;

        // Get the user's grants to determine their scope/level
        // Users can only assign roles at or below their permission level

        // Check if user has AdminAccess (project-level, full admin permissions)
        // AdminAccess at project scope = Owner-level, can assign any role
        var hasAdminAccess = _grantService.HasGrantAsync(CurrentUserId.Value, AdminAccessGrant)
            .GetAwaiter().GetResult();

        if (hasAdminAccess)
        {
            // Admin can assign any role including Owner
            return true;
        }

        // Check if user has DirectorHubAccess (director-level permissions)
        var hasDirectorAccess = _grantService.HasGrantAsync(CurrentUserId.Value, DirectorHubAccessGrant)
            .GetAwaiter().GetResult();

        if (hasDirectorAccess)
        {
            // Directors can assign Employee, Manager, Director, Trainee (but NOT Owner)
            return targetRole == UserRole.Employee
                || targetRole == UserRole.Manager
                || targetRole == UserRole.Director
                || targetRole == UserRole.Trainee;
        }

        // Check if user has ManagerHomeAccess (manager-level permissions)
        var hasManagerAccess = _grantService.HasGrantAsync(CurrentUserId.Value, ManagerHomeAccessGrant)
            .GetAwaiter().GetResult();

        if (hasManagerAccess)
        {
            // Managers can assign Employee and Trainee only
            return targetRole == UserRole.Employee
                || targetRole == UserRole.Trainee;
        }

        // User has AssignRoles but no elevated access - cannot assign any roles
        return false;
    }
}
