using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.My;

/// <summary>
/// Phase 6: User settings page for managing notification preferences
/// </summary>
[Authorize]
public class SettingsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<SettingsModel> _logger;
    private readonly IAuditLogService _auditLogService;

    public SettingsModel(AppDbContext db, ITenantResolver tenantResolver, IStringLocalizer<SharedResources> localizer, ILogger<SettingsModel> logger, IAuditLogService auditLogService)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _localizer = localizer;
        _logger = logger;
        _auditLogService = auditLogService;
    }

    // Daily Notification Preferences
    [BindProperty]
    public bool ReceiveDailyDigest { get; set; } = true;

    [BindProperty]
    public TimeOnly PreferredTime { get; set; } = new TimeOnly(8, 0);

    [BindProperty]
    public bool IncludeUpcomingShifts { get; set; } = true;

    [BindProperty]
    public bool IncludePendingRequests { get; set; } = true;

    [BindProperty]
    public bool IncludeChores { get; set; } = true;

    [BindProperty]
    public bool IncludeOnDuty { get; set; } = true;

    // Phase 6 Extension: Day-Before Reminders
    [BindProperty]
    public bool RemindBeforeShifts { get; set; } = false;

    [BindProperty]
    public bool RemindBeforeChores { get; set; } = false;

    [BindProperty]
    public bool RemindBeforeOnDuty { get; set; } = false;

    // Phase 6 Extension: OnDuty Role Subscriptions
    [BindProperty]
    public List<int> SelectedOnDutyRoleTypes { get; set; } = new();

    public List<OnDutyTypeOption> AvailableOnDutyTypes { get; set; } = new();

    // Military Rank
    [BindProperty]
    public MilitaryRank Rank { get; set; } = MilitaryRank.Turai;

    public List<SelectListItem> RankOptions { get; set; } = new();

    // Task 4 (#6): per-user opt-out for success toasts. Global on AppUser (mirrors ThemeMode/ThemeColor) —
    // not part of DailyNotificationPreference, which is company-scoped and email-only.
    [BindProperty]
    public bool SuppressSuccessToasts { get; set; }

    // SuccessMessage / ErrorMessage removed — feedback now flows through TempData → _Layout FeedbackModal bridge.
    public bool HasExistingPreference { get; set; }

    /// <summary>
    /// Helper class for on-duty type multi-select options
    /// </summary>
    public class OnDutyTypeOption
    {
        public int TypeValue { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    public async Task OnGetAsync()
    {
        var userId = GetCurrentUserId();
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Load existing preference if exists
        var preference = await _db.DailyNotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CompanyId == companyId && p.IsActive);

        if (preference != null)
        {
            HasExistingPreference = true;
            ReceiveDailyDigest = preference.ReceiveDailyDigest;
            PreferredTime = preference.PreferredTime;
            IncludeUpcomingShifts = preference.IncludeUpcomingShifts;
            IncludePendingRequests = preference.IncludePendingRequests;
            IncludeChores = preference.IncludeChores;
            IncludeOnDuty = preference.IncludeOnDuty;

            // Phase 6 Extension: Load day-before reminder preferences
            RemindBeforeShifts = preference.RemindBeforeShifts;
            RemindBeforeChores = preference.RemindBeforeChores;
            RemindBeforeOnDuty = preference.RemindBeforeOnDuty;
        }
        else
        {
            HasExistingPreference = false;
            // Use default values (already set in property initialization)
        }

        // Phase 6 Extension: Load available on-duty types
        await LoadAvailableOnDutyTypesAsync();

        // Phase 6 Extension: Load existing on-duty role subscriptions
        var existingSubscriptions = await _db.OnDutyRoleSubscriptions
            .Where(s => s.UserId == userId && s.CompanyId == companyId && s.IsActive)
            .Select(s => s.OnDutyTypeValue)
            .ToListAsync();

        SelectedOnDutyRoleTypes = existingSubscriptions;

        // Load user's military rank + toast preference
        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            Rank = user.Rank;
            SuppressSuccessToasts = user.SuppressSuccessToasts;
        }

        // Populate rank options
        PopulateRankOptions();
    }

    private void PopulateRankOptions()
    {
        var currentCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
        var language = currentCulture.StartsWith("he") ? "he" : "en";

        RankOptions = Enum.GetValues<MilitaryRank>()
            .Select(r => new SelectListItem(r.GetDisplayName(language), ((int)r).ToString()))
            .ToList();
    }

    private async Task LoadAvailableOnDutyTypesAsync()
    {
        AvailableOnDutyTypes = new List<OnDutyTypeOption>();

        // Add enum types (Hakam=0, Lead=1)
        AvailableOnDutyTypes.Add(new OnDutyTypeOption
        {
            TypeValue = 0,
            DisplayName = _localizer["OnDutyTypeHakam"]
        });

        AvailableOnDutyTypes.Add(new OnDutyTypeOption
        {
            TypeValue = 1,
            DisplayName = _localizer["OnDutyTypeLead"]
        });

        // Add custom types from OnDutyTypeConfig
        var customTypes = await _db.OnDutyTypeConfigs
            .Where(c => c.IsActive)
            .OrderBy(c => c.TypeValue)
            .ToListAsync();

        var currentCulture = System.Globalization.CultureInfo.CurrentCulture.Name;

        foreach (var customType in customTypes)
        {
            AvailableOnDutyTypes.Add(new OnDutyTypeOption
            {
                TypeValue = customType.TypeValue,
                DisplayName = currentCulture.StartsWith("he") ? customType.NameHe : customType.NameEn
            });
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = GetCurrentUserId();
        if (userId == 0)
        {
            return RedirectToPage("/Auth/Login");
        }

        var companyId = _tenantResolver.GetCurrentTenantId();

        // Validate enum value
        if (!Enum.IsDefined(typeof(MilitaryRank), Rank))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRank"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            PopulateRankOptions();
            await LoadAvailableOnDutyTypesAsync();
            return Page();
        }

        try
        {
            var preference = await _db.DailyNotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CompanyId == companyId && p.IsActive);

            if (preference == null)
            {
                // Create new preference
                preference = new DailyNotificationPreference
                {
                    UserId = userId,
                    CompanyId = companyId,
                    ReceiveDailyDigest = ReceiveDailyDigest,
                    PreferredTime = PreferredTime,
                    IncludeUpcomingShifts = IncludeUpcomingShifts,
                    IncludePendingRequests = IncludePendingRequests,
                    IncludeChores = IncludeChores,
                    IncludeOnDuty = IncludeOnDuty,
                    RemindBeforeShifts = RemindBeforeShifts,
                    RemindBeforeChores = RemindBeforeChores,
                    RemindBeforeOnDuty = RemindBeforeOnDuty,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _db.DailyNotificationPreferences.Add(preference);
            }
            else
            {
                // Update existing preference
                preference.ReceiveDailyDigest = ReceiveDailyDigest;
                preference.PreferredTime = PreferredTime;
                preference.IncludeUpcomingShifts = IncludeUpcomingShifts;
                preference.IncludePendingRequests = IncludePendingRequests;
                preference.IncludeChores = IncludeChores;
                preference.IncludeOnDuty = IncludeOnDuty;
                preference.RemindBeforeShifts = RemindBeforeShifts;
                preference.RemindBeforeChores = RemindBeforeChores;
                preference.RemindBeforeOnDuty = RemindBeforeOnDuty;
                preference.UpdatedAt = DateTime.UtcNow;
            }

            // Phase 6 Extension: Update OnDuty Role Subscriptions
            await UpdateOnDutyRoleSubscriptionsAsync(userId, companyId);

            // Update user's military rank
            var user = await _db.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                PopulateRankOptions();
                await LoadAvailableOnDutyTypesAsync();
                return Page();
            }

            user.Rank = Rank;
            user.SuppressSuccessToasts = SuppressSuccessToasts;
            user.ProfileLastUpdated = DateTime.UtcNow;
            user.ProfileLastUpdatedBy = userId;

            await _db.SaveChangesAsync();

            await _auditLogService.LogAsync("SettingsUpdated", "User", userId,
                $"Updated notification settings and preferences",
                $"DailyDigest={ReceiveDailyDigest}, Rank={Rank}");

            TempData["SuccessMessage"] = _localizer["SettingsSavedSuccess"].Value;
            HasExistingPreference = true;

            // Repopulate options for page display
            await LoadAvailableOnDutyTypesAsync();
            PopulateRankOptions();

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving user settings");
            TempData["ErrorMessage"] = _localizer["Error_SavingSettings"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            PopulateRankOptions();
            await LoadAvailableOnDutyTypesAsync();
            return Page();
        }
    }

    private async Task UpdateOnDutyRoleSubscriptionsAsync(int userId, int companyId)
    {
        // Get all existing subscriptions for this user
        var existingSubscriptions = await _db.OnDutyRoleSubscriptions
            .Where(s => s.UserId == userId && s.CompanyId == companyId && s.IsActive)
            .ToListAsync();

        var existingTypeValues = existingSubscriptions.Select(s => s.OnDutyTypeValue).ToList();

        // Determine which subscriptions to add and remove
        var typesToAdd = SelectedOnDutyRoleTypes.Except(existingTypeValues).ToList();
        var typesToRemove = existingTypeValues.Except(SelectedOnDutyRoleTypes).ToList();

        // Remove unselected subscriptions (soft delete by setting IsActive = false)
        foreach (var subscription in existingSubscriptions.Where(s => typesToRemove.Contains(s.OnDutyTypeValue)))
        {
            subscription.IsActive = false;
        }

        // Add new subscriptions
        foreach (var typeValue in typesToAdd)
        {
            _db.OnDutyRoleSubscriptions.Add(new OnDutyRoleSubscription
            {
                UserId = userId,
                CompanyId = companyId,
                OnDutyTypeValue = typeValue,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
