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
    private readonly IGrantService _grantService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IHierarchyService _hierarchyService;

    public OnCallWidgetViewComponent(
        IStringLocalizer<SharedResources> localizer,
        IWidgetService widgetService,
        IGrantService grantService,
        ITenantResolver tenantResolver,
        IHierarchyService hierarchyService)
    {
        _localizer = localizer;
        _widgetService = widgetService;
        _grantService = grantService;
        _tenantResolver = tenantResolver;
        _hierarchyService = hierarchyService;
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

        // Resolve moleculeId from the ACTIVE company rather than the login-baked MoleculeId claim.
        // The claim holds the HOME molecule and is stale after a company switch — resolving from
        // the active company ensures switched multi-company members see the correct on-call data.
        // Single-company users: active company == home company → same molecule as the claim, no regression.
        // Fallback to 0 mirrors the old int.TryParse default for a company with no resolvable molecule.
        var activePath = await _hierarchyService.GetHierarchyPathForCompanyAsync(companyId);
        var moleculeId = activePath?.Molecule?.Id ?? 0;

        // Delegate all data retrieval to WidgetService (pass moleculeId for new path)
        var widgetData = await _widgetService.BuildOnCallWidgetAsync(userId, companyId, moleculeId);

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
            AvatarUrl = c.AvatarUrl,
            ContactType = MapContactType(c.ContactType),
            CompanyName = c.CompanyName,
            Rank = c.Rank
        }).ToList();

        var officeNumbers = officeNumberData.Select(o => new OfficeNumber
        {
            Label = o.Label,
            Number = o.Number
        }).ToList();

        // Map StoreStatus DTOs to StoreStatusViewModel
        var stores = widgetData.StoreStatuses.Select(s => new StoreStatusViewModel
        {
            StoreId = s.StoreId,
            StoreName = s.StoreName,
            Status = s.Status.ToString(),
            Message = s.Message
        }).ToList();

        // Check if user has ManageStores grant (gear icon visibility)
        var showGear = await _grantService.HasGrantAsync(userId, "ManageStores");

        var model = new OnCallWidgetViewModel
        {
            Contacts = contacts,
            OfficeNumbers = officeNumbers,
            Stores = stores,
            IsCollapsed = widgetData.IsCollapsed,
            ShowOfficeNumbers = widgetData.ShowOfficeNumbers,
            ShowInSidebar = showInSidebar,
            ShowGearIcon = showGear,
            MoleculeId = moleculeId,
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
    public List<StoreStatusViewModel> Stores { get; set; } = new();
    public bool IsCollapsed { get; set; }
    public bool ShowOfficeNumbers { get; set; }
    public bool ShowInSidebar { get; set; }
    public bool ShowGearIcon { get; set; }
    public int MoleculeId { get; set; }
    public bool HasContacts { get; set; }
    public bool HasOfficeNumbers { get; set; }
}

public class StoreStatusViewModel
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public string Status { get; set; } = "";  // "Open", "Break", "Closed", "NoHoursSet"
    public string Message { get; set; } = "";  // e.g., "Open until 14:00"
}

public class OnCallContact
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string AvatarInitial { get; set; } = "";
    public string? AvatarUrl { get; set; }
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
