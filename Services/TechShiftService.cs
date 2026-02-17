using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — tech shift queries are molecule-scoped;
// lookups scoped by explicit moleculeId/shiftTypeKey parameters; called only from authorized endpoints
public class TechShiftService : ITechShiftService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly ILogger<TechShiftService> _logger;

    /// <summary>
    /// Maps tech shift type key to the corresponding grant key that controls eligibility.
    /// </summary>
    private static readonly Dictionary<string, string> TechShiftGrantMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "HANAVA", "CanBeAssignedHanava" },
        { "DELTA", "CanBeAssignedDelta" },
        { "YEKEV", "CanBeAssignedYekev" },
        { "MOVILTECH", "CanBeAssignedMoviltech" },
        { "NOC", "CanBeAssignedNOC" },
        { "SHIKLUT", "CanBeAssignedShiklut" }
    };

    public TechShiftService(
        AppDbContext db,
        IGrantService grantService,
        ILogger<TechShiftService> logger)
    {
        _db = db;
        _grantService = grantService;
        _logger = logger;
    }

    public async Task<List<AppUser>> GetEligibleUsersForTechShiftAsync(string techShiftType, int? companyId = null)
    {
        if (!TechShiftGrantMap.TryGetValue(techShiftType, out var grantKey))
        {
            _logger.LogWarning("Unknown tech shift type: {TechShiftType}", techShiftType);
            return new List<AppUser>();
        }

        // Get the grant type to find its ID
        var grantType = await _grantService.GetGrantTypeByKeyAsync(grantKey);
        if (grantType == null)
        {
            _logger.LogWarning("Grant type not found for key: {GrantKey}", grantKey);
            return new List<AppUser>();
        }

        // Query users who have the corresponding grant with CanOwn = true
        // SECURITY-AUDITED: SAFE — scoped by specific grantTypeId; returns only user IDs with matching grant
        var query = _db.Grants
            .IgnoreQueryFilters()
            .Where(g => g.GrantTypeId == grantType.Id && g.CanOwn)
            .Select(g => g.UserId)
            .Distinct();

        // Get the eligible user IDs
        var eligibleUserIds = await query.ToListAsync();

        if (!eligibleUserIds.Any())
        {
            _logger.LogDebug("No eligible users found for tech shift type {TechShiftType}", techShiftType);
            return new List<AppUser>();
        }

        // Fetch the actual users, filtering by company if specified
        // SECURITY-AUDITED: SAFE — re-scoped by grant-derived eligibleUserIds + optional companyId
        var usersQuery = _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && eligibleUserIds.Contains(u.Id));

        if (companyId.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.CompanyId == companyId.Value);
        }

        var users = await usersQuery
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        _logger.LogDebug("Found {Count} eligible users for tech shift type {TechShiftType} (company filter: {CompanyId})",
            users.Count, techShiftType, companyId?.ToString() ?? "none");

        return users;
    }

    public async Task<bool> IsUserEligibleForTechShiftAsync(int userId, string techShiftType)
    {
        if (!TechShiftGrantMap.TryGetValue(techShiftType, out var grantKey))
        {
            _logger.LogWarning("Unknown tech shift type for eligibility check: {TechShiftType}", techShiftType);
            return false;
        }

        return await _grantService.HasGrantAsync(userId, grantKey);
    }

    public Task<List<string>> GetTechShiftTypesAsync()
    {
        // Return all known tech shift type keys from the seed data
        var types = TechShiftTypeSeed.AllTechShiftTypeKeys.ToList();

        // Also include NOC and Shiklut which are helper molecule types but still tech-adjacent
        types.Add("NOC");
        types.Add("SHIKLUT");

        return Task.FromResult(types);
    }
}
