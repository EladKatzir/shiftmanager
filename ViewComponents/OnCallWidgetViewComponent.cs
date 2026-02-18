using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Models.Support;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

/// <summary>
/// On-call widget view component — delegates all data retrieval to IWidgetService.
/// </summary>
public class OnCallWidgetViewComponent : ViewComponent
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IWidgetService _widgetService;
    private readonly ITenantResolver _tenantResolver;

    public OnCallWidgetViewComponent(
        IStringLocalizer<SharedResources> localizer,
        IWidgetService widgetService,
        ITenantResolver tenantResolver)
    {
        _localizer = localizer;
        _widgetService = widgetService;
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

        // Get current company context for ManagerHomeAccess fallback
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Delegate all data retrieval to WidgetService
        var widgetData = await _widgetService.BuildOnCallWidgetAsync(userId, companyId);

        // Get office numbers for current company
        var officeNumberData = await _widgetService.GetOfficeNumbersAsync(companyId);

        // Map WidgetService DTOs to ViewComponent view model types
        var contacts = widgetData.Contacts.Select(c => new OnCallContact
        {
            UserId = c.UserId,
            Name = c.Name,
            Role = c.Role,
            PhoneNumber = c.PhoneNumber,
            AvatarInitial = c.AvatarInitial,
            ContactType = MapContactType(c.ContactType),
            CompanyName = c.CompanyName,
            Rank = c.Rank
        }).ToList();

        var officeNumbers = officeNumberData.Select(o => new OfficeNumber
        {
            Label = o.Label,
            Number = o.Number
        }).ToList();

        var model = new OnCallWidgetViewModel
        {
            Contacts = contacts,
            OfficeNumbers = officeNumbers,
            IsCollapsed = widgetData.IsCollapsed,
            ShowOfficeNumbers = widgetData.ShowOfficeNumbers,
            ShowInSidebar = showInSidebar,
            HasContacts = contacts.Any(),
            HasOfficeNumbers = officeNumbers.Any()
        };

        return View(model);
    }

    /// <summary>
    /// Maps WidgetService ContactType enum to ViewComponent OnCallContactType enum.
    /// </summary>
    private static OnCallContactType MapContactType(ContactType contactType) => contactType switch
    {
        ContactType.Hakam => OnCallContactType.Hakam,
        ContactType.CompanyOnCall => OnCallContactType.CompanyOnCall,
        ContactType.Friend => OnCallContactType.Friend,
        _ => OnCallContactType.CompanyOnCall
    };
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
