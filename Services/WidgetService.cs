using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using System.Globalization;

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
    /// <param name="moleculeId">The user's molecule ID. When &gt; 0, uses QuickInfoConfig-based lookup instead of grant-based.</param>
    Task<OnCallWidgetData> BuildOnCallWidgetAsync(int userId, int currentCompanyId = 0, int moleculeId = 0);

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
    private readonly IStoreService _storeService;
    private readonly IQuickInfoConfigService _configService;

    public WidgetService(
        AppDbContext context,
        IGrantService grantService,
        IStoreService storeService,
        IQuickInfoConfigService configService)
    {
        _context = context;
        _grantService = grantService;
        _storeService = storeService;
        _configService = configService;
    }

    public async Task<OnCallWidgetData> BuildOnCallWidgetAsync(int userId, int currentCompanyId = 0, int moleculeId = 0)
    {
        if (moleculeId > 0)
        {
            return await BuildMoleculeBasedWidgetAsync(userId, currentCompanyId, moleculeId);
        }

        // FALLBACK: Old grant-based shift-based lookup (backward compat for users without molecule)
        return await BuildGrantBasedWidgetAsync(userId, currentCompanyId);
    }

    /// <summary>
    /// New molecule-based widget path using QuickInfoConfig + OnDuty table + Store statuses.
    /// </summary>
    private async Task<OnCallWidgetData> BuildMoleculeBasedWidgetAsync(int userId, int currentCompanyId, int moleculeId)
    {
        var contacts = new List<ContactInfo>();
        var storeStatuses = new List<StoreStatus>();
        var targetDate = DateOnly.FromDateTime(DateTime.Today);
        var isHebrew = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "he";

        // Load QuickInfoConfig for this molecule (or defaults if none configured)
        var hasConfig = await _configService.HasConfigAsync(moleculeId);
        List<QuickInfoConfigItem> configItems;
        if (hasConfig)
        {
            var configs = await _configService.GetConfigForMoleculeAsync(moleculeId);
            configItems = configs
                .Where(c => c.IsEnabled)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new QuickInfoConfigItem
                {
                    SectionType = c.SectionType,
                    EntityId = c.EntityId,
                    DisplayOrder = c.DisplayOrder,
                    IsEnabled = c.IsEnabled,
                    ShowBackup = c.ShowBackup
                })
                .ToList();
        }
        else
        {
            configItems = await _configService.GetDefaultConfigAsync(moleculeId);
        }

        foreach (var item in configItems)
        {
            if (item.SectionType == QuickInfoSectionType.OnCallRole)
            {
                if (item.EntityId == QuickInfoConfig.PrimaryHakamEntityId)
                {
                    // Built-in primary Hakam (OnDutyType.Hakam == 0) — no OnDutyTypeConfig row exists,
                    // so the role name is the localized "Hakam" literal (matches GetCurrentHakamAsync).
                    var primaryName = isHebrew ? "חק\"ם" : "Hakam";
                    contacts.Add(await BuildOnCallContactAsync(item.EntityId, primaryName, targetDate));

                    // Additive backup: when ShowBackup is on, also render the configured backup type
                    // (lowest active custom on-duty type) as a second contact under the same heading.
                    if (item.ShowBackup)
                    {
                        var backupTypeValue = await _configService.GetBackupHakamTypeValueAsync();
                        if (backupTypeValue.HasValue)
                        {
                            var backupConfig = await _context.OnDutyTypeConfigs
                                .FirstOrDefaultAsync(dt => dt.TypeValue == backupTypeValue.Value);
                            var backupName = backupConfig != null
                                ? (isHebrew ? (backupConfig.NameHe ?? backupConfig.NameEn) : backupConfig.NameEn)
                                : (isHebrew ? "חק\"ם רזרבה" : "Backup-hakam");
                            contacts.Add(await BuildOnCallContactAsync(backupTypeValue.Value, backupName, targetDate));
                        }
                    }
                }
                else
                {
                    // Custom on-duty type — display name comes from its OnDutyTypeConfig row.
                    var dutyTypeConfig = await _context.OnDutyTypeConfigs
                        .FirstOrDefaultAsync(dt => dt.TypeValue == item.EntityId);
                    var roleName = dutyTypeConfig != null
                        ? (isHebrew ? (dutyTypeConfig.NameHe ?? dutyTypeConfig.NameEn) : dutyTypeConfig.NameEn)
                        : "On-Call";
                    contacts.Add(await BuildOnCallContactAsync(item.EntityId, roleName, targetDate));
                }
            }
            else if (item.SectionType == QuickInfoSectionType.Store)
            {
                var storeStatus = await _storeService.ComputeStoreStatusAsync(item.EntityId, DateTime.Now);
                if (storeStatus != null)
                    storeStatuses.Add(storeStatus);
            }
        }

        var preferences = await GetUserWidgetPreferencesAsync(userId);
        var friendsOnCall = await GetFriendsOnCallAsync(userId);

        return new OnCallWidgetData
        {
            Contacts = contacts,
            StoreStatuses = storeStatuses,
            FriendsOnCall = friendsOnCall,
            IsCollapsed = preferences.OnCallWidgetCollapsed,
            ShowOfficeNumbers = preferences.ShowOfficeNumbers,
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Builds a single on-call <see cref="ContactInfo"/> for the given on-duty type value on a date:
    /// the assigned active user if one exists, otherwise an unassigned placeholder carrying the role
    /// name. Shared by the primary-Hakam, backup-Hakam, and custom on-duty sections so they render
    /// identically.
    /// </summary>
    private async Task<ContactInfo> BuildOnCallContactAsync(int typeValue, string roleName, DateOnly targetDate)
    {
        // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
        var onDutyEntry = await _context.OnDuties
            .IgnoreQueryFilters()
            .Where(od => od.Date == targetDate
                && (int)od.Type == typeValue
                && od.CanceledAt == null)
            .Join(
                _context.Users.IgnoreQueryFilters().Where(u => u.IsActive),
                od => od.UserId,
                u => u.Id,
                (od, u) => new { u.Id, u.DisplayName, u.Phone, u.Rank, u.AvatarFileName, u.CompanyId })
            .FirstOrDefaultAsync();

        if (onDutyEntry != null)
        {
            return new ContactInfo
            {
                UserId = onDutyEntry.Id,
                Name = onDutyEntry.DisplayName,
                Role = roleName,
                PhoneNumber = onDutyEntry.Phone ?? "",
                AvatarInitial = GetInitial(onDutyEntry.DisplayName),
                AvatarUrl = GetThumbnailUrl(onDutyEntry.Id, onDutyEntry.CompanyId, onDutyEntry.AvatarFileName),
                ContactType = ContactType.Hakam,
                Rank = onDutyEntry.Rank
            };
        }

        // No one assigned for this role today — show placeholder
        return new ContactInfo
        {
            UserId = 0,
            Name = roleName,
            Role = roleName,
            PhoneNumber = "",
            AvatarInitial = "?",
            AvatarUrl = null,
            ContactType = ContactType.Hakam
        };
    }

    /// <summary>
    /// Original grant-based widget path — backward compatibility for users without molecule context.
    /// </summary>
    private async Task<OnCallWidgetData> BuildGrantBasedWidgetAsync(int userId, int currentCompanyId)
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
        // Note: ShiftType.Name is [NotMapped], so we search by NameEn or Key instead
        var hakamShiftTypeIds = await _context.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => (st.NameEn != null && st.NameEn.Contains("Hakam")) || st.Key.Contains("Hakam"))
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
                    Rank = u.Rank,
                    u.AvatarFileName,
                    u.CompanyId
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
            AvatarUrl = GetThumbnailUrl(hakamData.UserId, hakamData.CompanyId, hakamData.AvatarFileName),
            ContactType = ContactType.Hakam,
            Rank = hakamData.Rank
        };
    }

    public async Task<List<ContactInfo>> GetCompanyOnCallAsync(int companyId, DateTime? date = null)
    {
        var contacts = new List<ContactInfo>();
        var targetDate = DateOnly.FromDateTime(date ?? DateTime.Today);

        // Get company with localized name
        var companyEntity = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (companyEntity == null) return contacts;
        var companyDisplayName = companyEntity.LocalizedName;

        // Security: IgnoreQueryFilters — on-call contacts are cross-company by design
        // Note: ShiftType.Name is [NotMapped], so we search by NameEn or Key instead
        var brShiftTypes = await _context.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => (st.NameEn != null && (st.NameEn.Contains("BR") || st.NameEn.Contains("Katzin")))
                      || st.Key.Contains("BR") || st.Key.Contains("Katzin"))
            .Select(st => new { st.Id, Name = st.NameEn ?? st.Key })
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
                    x.ShiftTypeId,
                    u.AvatarFileName,
                    u.CompanyId
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
                Role = $"{shiftTypeName} - {companyDisplayName}",
                PhoneNumber = shift.Phone ?? "",
                AvatarInitial = GetInitial(shift.DisplayName),
                AvatarUrl = GetThumbnailUrl(shift.UserId, shift.CompanyId, shift.AvatarFileName),
                ContactType = ContactType.CompanyOnCall,
                CompanyName = companyDisplayName,
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
                u.Phone,
                u.AvatarFileName,
                u.CompanyId
            })
            .ToListAsync();

        // Pre-fetch on-call shift type IDs to avoid [NotMapped] ShiftType.Name in LINQ-to-SQL
        // Note: ShiftType.Name is [NotMapped], so we search by NameEn or Key instead
        var onCallShiftTypeIds = await _context.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => (st.NameEn != null && (st.NameEn.Contains("BR") || st.NameEn.Contains("Katzin") || st.NameEn.Contains("Hakam")))
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
                AvatarUrl = GetThumbnailUrl(friend.Id, friend.CompanyId, friend.AvatarFileName),
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

    private static string? GetThumbnailUrl(int userId, int companyId, string? avatarFileName)
    {
        return string.IsNullOrWhiteSpace(avatarFileName)
            ? null
            : $"/avatars/{companyId}/{userId}_thumb.jpg";
    }
}

#region Data Transfer Objects

public class OnCallWidgetData
{
    public List<ContactInfo> Contacts { get; set; } = new();
    public List<StoreStatus> StoreStatuses { get; set; } = new();
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
    public string? AvatarUrl { get; set; }
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
    public string? AvatarUrl { get; set; }
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
