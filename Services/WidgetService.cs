using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Service for building grant-based widgets with additive content merging.
/// </summary>
public interface IWidgetService
{
    /// <summary>
    /// Builds the on-call widget data for a user, merging contacts from all granted contexts.
    /// </summary>
    /// <param name="userId">The current user's ID.</param>
    /// <param name="currentCompanyId">The user's current tenant company ID (used for ManagerHomeAccess fallback).</param>
    Task<OnCallWidgetData> BuildOnCallWidgetAsync(int userId, int currentCompanyId = 0);

    /// <summary>
    /// Gets the current Hakam (on-call commander) for the given date.
    /// </summary>
    Task<ContactInfo?> GetCurrentHakamAsync(DateTime? date = null);

    /// <summary>
    /// Gets company-specific on-call contacts for the given company.
    /// </summary>
    Task<List<ContactInfo>> GetCompanyOnCallAsync(int companyId, DateTime? date = null);

    /// <summary>
    /// Gets user widget preferences (collapsed state, visible sections, etc.)
    /// </summary>
    Task<WidgetPreferences> GetUserWidgetPreferencesAsync(int userId);

    /// <summary>
    /// Saves user widget preferences.
    /// </summary>
    Task SaveUserWidgetPreferencesAsync(int userId, WidgetPreferences preferences);

    /// <summary>
    /// Gets friends on-call status for a user.
    /// </summary>
    Task<List<FriendOnCallInfo>> GetFriendsOnCallAsync(int userId, DateTime? date = null);

    /// <summary>
    /// Gets office numbers for a company.
    /// </summary>
    Task<List<OfficeNumberInfo>> GetOfficeNumbersAsync(int companyId);
}

public class WidgetService : IWidgetService
{
    private readonly AppDbContext _context;
    private readonly IGrantService _grantService;

    public WidgetService(AppDbContext context, IGrantService grantService)
    {
        _context = context;
        _grantService = grantService;
    }

    public async Task<OnCallWidgetData> BuildOnCallWidgetAsync(int userId, int currentCompanyId = 0)
    {
        var contacts = new List<ContactInfo>();
        var date = DateTime.Today;

        // Check for Hakam view grant
        var hasHakamGrant = await _grantService.HasGrantAsync(userId, "ViewHakamOnCall");
        if (hasHakamGrant)
        {
            var hakam = await GetCurrentHakamAsync(date);
            if (hakam != null)
            {
                contacts.Add(hakam);
            }
        }

        // Get all company-specific grants (additive merging)
        var userGrants = await _grantService.GetUserGrantsAsync(userId);
        var companyIds = userGrants
            .Where(g => g.GrantType?.Key == "ViewCompanyOnCall" && g.CompanyId.HasValue)
            .Select(g => g.CompanyId!.Value)
            .Distinct()
            .ToList();

        // ManagerHomeAccess fallback: always include current company if user has manager-level access
        if (currentCompanyId > 0)
        {
            var hasManagerAccess = await _grantService.HasGrantAsync(userId, "ManagerHomeAccess");
            if (hasManagerAccess && !companyIds.Contains(currentCompanyId))
            {
                companyIds.Add(currentCompanyId);
            }
        }

        foreach (var companyId in companyIds)
        {
            var companyContacts = await GetCompanyOnCallAsync(companyId, date);
            contacts.AddRange(companyContacts);
        }

        // Remove duplicates by UserId (keep first occurrence)
        contacts = contacts
            .GroupBy(c => c.UserId)
            .Select(g => g.First())
            .ToList();

        // Get user preferences
        var preferences = await GetUserWidgetPreferencesAsync(userId);

        // Get friends on-call if user has friends
        var friendsOnCall = await GetFriendsOnCallAsync(userId, date);

        return new OnCallWidgetData
        {
            Contacts = contacts,
            FriendsOnCall = friendsOnCall,
            IsCollapsed = preferences.OnCallWidgetCollapsed,
            ShowOfficeNumbers = preferences.ShowOfficeNumbers,
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task<ContactInfo?> GetCurrentHakamAsync(DateTime? date = null)
    {
        var targetDate = DateOnly.FromDateTime(date ?? DateTime.Today);

        // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
        // First, get the IDs of Hakam shift types
        // Note: ShiftType.Name is [NotMapped], so we search by CustomName or Key instead
        var hakamShiftTypeIds = await _context.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => (st.CustomName != null && st.CustomName.Contains("Hakam")) || st.Key.Contains("Hakam"))
            .Select(st => st.Id)
            .ToListAsync();

        if (!hakamShiftTypeIds.Any()) return null;

        // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
        var hakamData = await _context.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => si.WorkDate == targetDate && hakamShiftTypeIds.Contains(si.ShiftTypeId))
            .Join(
                _context.ShiftAssignments.IgnoreQueryFilters().Where(sa => sa.UserId != null),
                si => si.Id,
                sa => sa.ShiftInstanceId,
                (si, sa) => new { si, sa.UserId }
            )
            .Join(
                _context.Users.IgnoreQueryFilters().Where(u => u.IsActive),
                x => x.UserId,
                u => u.Id,
                (x, u) => new
                {
                    UserId = u.Id,
                    DisplayName = u.DisplayName,
                    Phone = u.Phone,
                    Rank = u.Rank
                }
            )
            .FirstOrDefaultAsync();

        if (hakamData == null) return null;

        return new ContactInfo
        {
            UserId = hakamData.UserId,
            Name = hakamData.DisplayName,
            Role = "Hakam",
            PhoneNumber = hakamData.Phone ?? "",
            AvatarInitial = GetInitial(hakamData.DisplayName),
            ContactType = ContactType.Hakam,
            Rank = hakamData.Rank
        };
    }

    public async Task<List<ContactInfo>> GetCompanyOnCallAsync(int companyId, DateTime? date = null)
    {
        var contacts = new List<ContactInfo>();
        var targetDate = DateOnly.FromDateTime(date ?? DateTime.Today);

        // Get company name
        var company = await _context.Companies
            .Where(c => c.Id == companyId)
            .Select(c => new { c.Name })
            .FirstOrDefaultAsync();

        if (company == null) return contacts;

        // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
        // Note: ShiftType.Name is [NotMapped], so we search by CustomName or Key instead
        var brShiftTypes = await _context.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => (st.CustomName != null && (st.CustomName.Contains("BR") || st.CustomName.Contains("Katzin")))
                      || st.Key.Contains("BR") || st.Key.Contains("Katzin"))
            .Select(st => new { st.Id, Name = st.CustomName ?? st.Key })
            .ToListAsync();

        if (!brShiftTypes.Any()) return contacts;

        var brShiftTypeIds = brShiftTypes.Select(st => st.Id).ToList();
        var shiftTypeNames = brShiftTypes.ToDictionary(st => st.Id, st => st.Name);

        // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
        var onDutyData = await _context.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => si.WorkDate == targetDate && si.CompanyId == companyId && brShiftTypeIds.Contains(si.ShiftTypeId))
            .Join(
                _context.ShiftAssignments.IgnoreQueryFilters().Where(sa => sa.UserId != null && sa.CompanyId == companyId),
                si => si.Id,
                sa => sa.ShiftInstanceId,
                (si, sa) => new { si.ShiftTypeId, sa.UserId }
            )
            .Join(
                _context.Users.IgnoreQueryFilters().Where(u => u.IsActive),
                x => x.UserId,
                u => u.Id,
                (x, u) => new
                {
                    UserId = u.Id,
                    DisplayName = u.DisplayName,
                    Phone = u.Phone,
                    Rank = u.Rank,
                    x.ShiftTypeId
                }
            )
            .ToListAsync();

        foreach (var shift in onDutyData)
        {
            var shiftTypeName = shiftTypeNames.GetValueOrDefault(shift.ShiftTypeId, "On-Call");
            contacts.Add(new ContactInfo
            {
                UserId = shift.UserId,
                Name = shift.DisplayName,
                Role = $"{shiftTypeName} - {company.Name}",
                PhoneNumber = shift.Phone ?? "",
                AvatarInitial = GetInitial(shift.DisplayName),
                ContactType = ContactType.CompanyOnCall,
                CompanyName = company.Name,
                Rank = shift.Rank
            });
        }

        return contacts;
    }

    public Task<WidgetPreferences> GetUserWidgetPreferencesAsync(int userId)
    {
        // Try to load from UserPreferences table if it exists
        // For now, return defaults
        return Task.FromResult(new WidgetPreferences
        {
            OnCallWidgetCollapsed = false,
            OfficeNumbersCollapsed = true,
            FriendsOnCallCollapsed = true,
            ShowOfficeNumbers = true,
            ShowFriendsOnCall = true
        });
    }

    public Task SaveUserWidgetPreferencesAsync(int userId, WidgetPreferences preferences)
    {
        // Note: Widget preferences are stored client-side in localStorage.
        // Server-side persistence planned for future release.
        return Task.CompletedTask;
    }

    public async Task<List<FriendOnCallInfo>> GetFriendsOnCallAsync(int userId, DateTime? date = null)
    {
        var friends = new List<FriendOnCallInfo>();
        var targetDate = DateOnly.FromDateTime(date ?? DateTime.Today);

        // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
        // Get user's accepted friends from UserFriendships table (bidirectional)
        var friendships = await _context.UserFriendships
            .IgnoreQueryFilters()
            .Where(f => (f.UserId == userId || f.FriendId == userId) && f.Status == FriendshipStatus.Accepted)
            .ToListAsync();
        var friendIds = friendships.Select(f => f.UserId == userId ? f.FriendId : f.UserId).ToList();

        if (!friendIds.Any()) return friends;

        // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
        // Get friend user data
        var friendUsers = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => friendIds.Contains(u.Id) && u.IsActive)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Phone
            })
            .ToListAsync();

        // Pre-fetch on-call shift type IDs to avoid [NotMapped] ShiftType.Name in LINQ-to-SQL
        // Note: ShiftType.Name is [NotMapped], so we search by CustomName or Key instead
        var onCallShiftTypeIds = await _context.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => (st.CustomName != null && (st.CustomName.Contains("BR") || st.CustomName.Contains("Katzin") || st.CustomName.Contains("Hakam")))
                      || st.Key.Contains("BR") || st.Key.Contains("Katzin") || st.Key.Contains("Hakam"))
            .Select(st => st.Id)
            .ToListAsync();

        foreach (var friend in friendUsers)
        {
            // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
            // Check if friend is on-call today using pre-fetched shift type IDs
            var isOnCall = await _context.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(sa => sa.UserId == friend.Id)
                .Join(
                    _context.ShiftInstances.IgnoreQueryFilters().Where(si => si.WorkDate == targetDate),
                    sa => sa.ShiftInstanceId,
                    si => si.Id,
                    (sa, si) => si.ShiftTypeId
                )
                .AnyAsync(stId => onCallShiftTypeIds.Contains(stId));

            friends.Add(new FriendOnCallInfo
            {
                UserId = friend.Id,
                Name = friend.DisplayName,
                AvatarInitial = GetInitial(friend.DisplayName),
                IsOnCall = isOnCall,
                PhoneNumber = friend.Phone ?? ""
            });
        }

        return friends;
    }

    public Task<List<OfficeNumberInfo>> GetOfficeNumbersAsync(int companyId)
    {
        // Get office numbers from CompanySettings or a dedicated table
        // For now, return empty list as this would need a new table
        // This is a placeholder for future implementation
        return Task.FromResult(new List<OfficeNumberInfo>());
    }

    private static string GetInitial(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        return name.Trim().Substring(0, 1).ToUpper();
    }
}

#region Data Transfer Objects

public class OnCallWidgetData
{
    public List<ContactInfo> Contacts { get; set; } = new();
    public List<FriendOnCallInfo> FriendsOnCall { get; set; } = new();
    public List<OfficeNumberInfo> OfficeNumbers { get; set; } = new();
    public bool IsCollapsed { get; set; }
    public bool ShowOfficeNumbers { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ContactInfo
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string AvatarInitial { get; set; } = "";
    public ContactType ContactType { get; set; }
    public string? CompanyName { get; set; }
    public MilitaryRank Rank { get; set; } = MilitaryRank.Turai;
}

public enum ContactType
{
    Hakam,
    CompanyOnCall,
    Friend
}

public class FriendOnCallInfo
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string AvatarInitial { get; set; } = "";
    public bool IsOnCall { get; set; }
    public string PhoneNumber { get; set; } = "";
}

public class OfficeNumberInfo
{
    public string Label { get; set; } = "";
    public string Number { get; set; } = "";
    public string? Description { get; set; }
}

public class WidgetPreferences
{
    public bool OnCallWidgetCollapsed { get; set; }
    public bool OfficeNumbersCollapsed { get; set; }
    public bool FriendsOnCallCollapsed { get; set; }
    public bool ShowOfficeNumbers { get; set; }
    public bool ShowFriendsOnCall { get; set; }
}

#endregion
