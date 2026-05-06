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

    public async Task<bool> IsDirectorAsync()
    {
        if (CurrentUserId == null)
            return false;

        return await _grantService.HasGrantAsync(CurrentUserId.Value, DirectorHubAccessGrant);
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

    public async Task<bool> CanAssignRoleAsync(string role)
    {
        // Safe string overload - try parse and delegate to strongly-typed version
        if (string.IsNullOrWhiteSpace(role))
            return false;

        // Try to parse the role string (case-insensitive)
        if (Enum.TryParse<UserRole>(role.Trim(), ignoreCase: true, out var targetRole))
        {
            return await CanAssignRoleAsync(targetRole);
        }

        // Invalid role string
        return false;
    }

    /// <remarks>
    /// This method checks role TYPE permission only. Scope validation (whether the target user
    /// belongs to a company the caller manages) must be performed at the call site.
    /// </remarks>
    public async Task<bool> CanAssignRoleAsync(UserRole targetRole)
    {
        if (CurrentUserId == null)
            return false;

        // Check AdminAccess first — it's a superset permission that implies all others
        // (Owner template may not include the specific AssignRoles grant)
        if (await _grantService.HasGrantAsync(CurrentUserId.Value, AdminAccessGrant))
        {
            // Admin can assign any role including Owner
            return true;
        }

        // Check if user has AssignRoles grant
        if (!await _grantService.HasGrantAsync(CurrentUserId.Value, AssignRolesGrant))
            return false;

        // Get the user's grants to determine their scope/level
        // Users can only assign roles at or below their permission level

        // Check if user has DirectorHubAccess (director-level permissions)
        if (await _grantService.HasGrantAsync(CurrentUserId.Value, DirectorHubAccessGrant))
        {
            // Directors can assign Employee, Manager, Director, Trainee (but NOT Owner)
            return targetRole == UserRole.Employee
                || targetRole == UserRole.Manager
                || targetRole == UserRole.Director
                || targetRole == UserRole.Trainee;
        }

        // Check if user has ManagerHomeAccess (manager-level permissions)
        if (await _grantService.HasGrantAsync(CurrentUserId.Value, ManagerHomeAccessGrant))
        {
            // Managers can assign Employee and Trainee only
            return targetRole == UserRole.Employee
                || targetRole == UserRole.Trainee;
        }

        // User has AssignRoles but no elevated access - cannot assign any roles
        return false;
    }
}
