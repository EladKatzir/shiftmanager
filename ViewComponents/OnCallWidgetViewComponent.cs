using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Models.Support;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

public class OnCallWidgetViewComponent : ViewComponent
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly AppDbContext _context;
    private readonly IGrantService _grantService;
    private readonly ITenantResolver _tenantResolver;

    public OnCallWidgetViewComponent(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext context,
        IGrantService grantService,
        ITenantResolver tenantResolver)
    {
        _localizer = localizer;
        _context = context;
        _grantService = grantService;
        _tenantResolver = tenantResolver;
    }

    public async Task<IViewComponentResult> InvokeAsync(bool showInSidebar = true)
    {
        var user = HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            return Content(string.Empty);
        }

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Content(string.Empty);
        }

        var contacts = new List<OnCallContact>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Get current company context
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Get Hakam (on-call commander) - if user has grant to view
        // Note: Grant-based only; role fallbacks removed for proper grant-based authorization
        var hasHakamGrant = await _grantService.HasGrantAsync(userId, "ViewHakamOnCall");

        if (hasHakamGrant)
        {
            var hakamContact = await GetCurrentHakamAsync(today);
            if (hakamContact != null)
            {
                contacts.Add(hakamContact);
            }
        }

        // Get company-specific on-call contacts (additive merging based on grants)
        var companyGrants = await _grantService.GetUserGrantsAsync(userId);
        var companyIds = companyGrants
            .Where(g => g.GrantType?.Key == "ViewCompanyOnCall" && g.CompanyId.HasValue)
            .Select(g => g.CompanyId!.Value)
            .Distinct()
            .ToList();

        // Always include current company if user is a manager or above
        if (companyId > 0 && (user.IsInRole("Manager") || user.IsInRole("Director") || user.IsInRole("Owner")))
        {
            if (!companyIds.Contains(companyId))
            {
                companyIds.Add(companyId);
            }
        }

        foreach (var cId in companyIds)
        {
            var companyContacts = await GetCompanyOnCallAsync(cId, today);
            contacts.AddRange(companyContacts);
        }

        // Remove duplicates (by UserId)
        contacts = contacts
            .GroupBy(c => c.UserId)
            .Select(g => g.First())
            .ToList();

        // Get office numbers for current company
        var officeNumbers = await GetOfficeNumbersAsync(companyId);

        // Get user preferences (what to show, collapsed state)
        var preferences = await GetUserWidgetPreferencesAsync(userId);

        var model = new OnCallWidgetViewModel
        {
            Contacts = contacts,
            OfficeNumbers = officeNumbers,
            IsCollapsed = preferences?.IsCollapsed ?? false,
            ShowOfficeNumbers = preferences?.ShowOfficeNumbers ?? true,
            ShowInSidebar = showInSidebar,
            HasContacts = contacts.Any(),
            HasOfficeNumbers = officeNumbers.Any()
        };

        return View(model);
    }

    private async Task<OnCallContact?> GetCurrentHakamAsync(DateOnly date)
    {
        // First, get the IDs of Hakam shift types
        // Note: ShiftType.Name is [NotMapped], so we search by CustomName or Key instead
        var hakamShiftTypeIds = await _context.ShiftTypes
            .Where(st => (st.CustomName != null && st.CustomName.Contains("Hakam")) || st.Key.Contains("Hakam"))
            .Select(st => st.Id)
            .ToListAsync();

        if (!hakamShiftTypeIds.Any()) return null;

        // Now query shift instances with those IDs
        var hakamData = await _context.ShiftInstances
            .Where(si => si.WorkDate == date && hakamShiftTypeIds.Contains(si.ShiftTypeId))
            .Join(
                _context.ShiftAssignments.Where(sa => sa.UserId != null),
                si => si.Id,
                sa => sa.ShiftInstanceId,
                (si, sa) => new { si, sa.UserId }
            )
            .Join(
                _context.Users.Where(u => u.IsActive),
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

        return new OnCallContact
        {
            UserId = hakamData.UserId,
            Name = hakamData.DisplayName,
            Role = "Hakam",
            PhoneNumber = hakamData.Phone ?? "",
            AvatarInitial = GetInitial(hakamData.DisplayName),
            ContactType = OnCallContactType.Hakam,
            Rank = hakamData.Rank
        };
    }

    private async Task<List<OnCallContact>> GetCompanyOnCallAsync(int companyId, DateOnly date)
    {
        var contacts = new List<OnCallContact>();

        // Get company name
        var company = await _context.Companies
            .Where(c => c.Id == companyId)
            .Select(c => new { c.Name })
            .FirstOrDefaultAsync();

        if (company == null) return contacts;

        // First, get the IDs of BR/Katzin shift types
        // Note: ShiftType.Name is [NotMapped], so we search by CustomName or Key instead
        var brShiftTypes = await _context.ShiftTypes
            .Where(st => (st.CustomName != null && (st.CustomName.Contains("BR") || st.CustomName.Contains("Katzin")))
                      || st.Key.Contains("BR") || st.Key.Contains("Katzin"))
            .Select(st => new { st.Id, Name = st.CustomName ?? st.Key })
            .ToListAsync();

        if (!brShiftTypes.Any()) return contacts;

        var brShiftTypeIds = brShiftTypes.Select(st => st.Id).ToList();
        var shiftTypeNames = brShiftTypes.ToDictionary(st => st.Id, st => st.Name);

        // Now query shift instances with those IDs
        var brData = await _context.ShiftInstances
            .Where(si => si.WorkDate == date && si.CompanyId == companyId && brShiftTypeIds.Contains(si.ShiftTypeId))
            .Join(
                _context.ShiftAssignments.Where(sa => sa.UserId != null && sa.CompanyId == companyId),
                si => si.Id,
                sa => sa.ShiftInstanceId,
                (si, sa) => new { si.ShiftTypeId, sa.UserId }
            )
            .Join(
                _context.Users.Where(u => u.IsActive),
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

        foreach (var shift in brData)
        {
            var shiftTypeName = shiftTypeNames.GetValueOrDefault(shift.ShiftTypeId, "On-Call");
            contacts.Add(new OnCallContact
            {
                UserId = shift.UserId,
                Name = shift.DisplayName,
                Role = $"{shiftTypeName} - {company.Name}",
                PhoneNumber = shift.Phone ?? "",
                AvatarInitial = GetInitial(shift.DisplayName),
                ContactType = OnCallContactType.CompanyOnCall,
                CompanyName = company.Name,
                Rank = shift.Rank
            });
        }

        return contacts;
    }

    private Task<List<OfficeNumber>> GetOfficeNumbersAsync(int companyId)
    {
        if (companyId <= 0) return Task.FromResult(new List<OfficeNumber>());

        // Get office numbers from company settings or a dedicated table
        // For now, return empty - this would be populated from company configuration
        return Task.FromResult(new List<OfficeNumber>());
    }

    private Task<OnCallWidgetPreferences?> GetUserWidgetPreferencesAsync(int userId)
    {
        // This would typically come from a UserPreferences table
        // For now, return default preferences
        return Task.FromResult<OnCallWidgetPreferences?>(new OnCallWidgetPreferences
        {
            IsCollapsed = false,
            ShowOfficeNumbers = true
        });
    }

    private string GetInitial(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        return name.Trim().Substring(0, 1).ToUpper();
    }
}

public class OnCallWidgetViewModel
{
    public List<OnCallContact> Contacts { get; set; } = new();
    public List<OfficeNumber> OfficeNumbers { get; set; } = new();
    public bool IsCollapsed { get; set; }
    public bool ShowOfficeNumbers { get; set; }
    public bool ShowInSidebar { get; set; }
    public bool HasContacts { get; set; }
    public bool HasOfficeNumbers { get; set; }
}

public class OnCallContact
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string AvatarInitial { get; set; } = "";
    public OnCallContactType ContactType { get; set; }
    public string? CompanyName { get; set; }
    public MilitaryRank Rank { get; set; } = MilitaryRank.Turai;
}

public enum OnCallContactType
{
    Hakam,
    CompanyOnCall,
    Friend
}

public class OfficeNumber
{
    public string Label { get; set; } = "";
    public string Number { get; set; } = "";
}

public class OnCallWidgetPreferences
{
    public bool IsCollapsed { get; set; }
    public bool ShowOfficeNumbers { get; set; }
}
