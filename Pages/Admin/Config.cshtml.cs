using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class ConfigModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly IAuditLogService _auditLogService;
    private readonly IEmailConfigService _emailConfigService;

    public ConfigModel(
        AppDbContext db,
        ICompanyContext companyContext,
        IAuditLogService auditLogService,
        IEmailConfigService emailConfigService)
    {
        _db = db;
        _companyContext = companyContext;
        _auditLogService = auditLogService;
        _emailConfigService = emailConfigService;
    }

    // Company Settings
    [BindProperty] public int RestHours { get; set; }
    [BindProperty] public int WeeklyCap { get; set; }

    // OnDuty Type Configuration
    public List<OnDutyTypeConfig> OnDutyTypes { get; set; } = new();

    [BindProperty] public int TypeValue { get; set; }
    [BindProperty] public string NameEn { get; set; } = string.Empty;
    [BindProperty] public string NameHe { get; set; } = string.Empty;
    [BindProperty] public string Icon { get; set; } = "📌";
    [BindProperty] public string Color { get; set; } = "#6366f1";

    public string? Error { get; set; }
    public string? Success { get; set; }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    public async Task OnGetAsync()
    {
        var companyId = _companyContext.GetCompanyIdOrThrow();
        RestHours = GetInt(companyId, "RestHours", 8);
        WeeklyCap = GetInt(companyId, "WeeklyHoursCap", 40);

        // Load OnDuty type configurations
        OnDutyTypes = await _db.OnDutyTypeConfigs
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeValue)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // ✅ SECURITY FIX: Input validation for configuration values
        if (RestHours < 0 || RestHours > 24)
        {
            Error = "Rest hours must be between 0 and 24.";
            await OnGetAsync();
            return Page();
        }

        if (WeeklyCap < 0 || WeeklyCap > 168)
        {
            Error = "Weekly hours cap must be between 0 and 168 (7 days × 24 hours).";
            await OnGetAsync();
            return Page();
        }

        // Logical validation: WeeklyCap should be reasonable
        if (WeeklyCap > 0 && WeeklyCap < RestHours)
        {
            Error = "Weekly hours cap cannot be less than rest hours requirement.";
            await OnGetAsync();
            return Page();
        }

        var companyId = _companyContext.GetCompanyIdOrThrow();
        await Set(companyId, "RestHours", RestHours.ToString());
        await Set(companyId, "WeeklyHoursCap", WeeklyCap.ToString());

        await _auditLogService.LogAsync(
            action: "ConfigUpdated",
            entityType: "AppConfig",
            entityId: companyId,
            description: $"Updated company configuration: RestHours={RestHours}, WeeklyCap={WeeklyCap}",
            details: System.Text.Json.JsonSerializer.Serialize(new { RestHours, WeeklyCap }));

        Success = "Configuration updated successfully.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddOnDutyTypeAsync()
    {
        // Validation
        if (string.IsNullOrWhiteSpace(NameEn) || string.IsNullOrWhiteSpace(NameHe))
        {
            Error = "Both English and Hebrew names are required.";
            await OnGetAsync();
            return Page();
        }

        if (NameEn.Length > 100 || NameHe.Length > 100)
        {
            Error = "Names must not exceed 100 characters.";
            await OnGetAsync();
            return Page();
        }

        // ✅ PHASE 18: Auto-increment TypeValue (max existing + 1, minimum 2 since 0=Hakam, 1=Lead)
        var maxTypeValue = await _db.OnDutyTypeConfigs
            .Select(t => (int?)t.TypeValue)
            .MaxAsync();

        var autoTypeValue = maxTypeValue.HasValue ? maxTypeValue.Value + 1 : 2;

        var newType = new OnDutyTypeConfig
        {
            TypeValue = autoTypeValue,
            NameEn = NameEn.Trim(),
            NameHe = NameHe.Trim(),
            Icon = string.IsNullOrWhiteSpace(Icon) ? "📌" : Icon.Trim(),
            Color = string.IsNullOrWhiteSpace(Color) ? "#6366f1" : Color.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = GetCurrentUserId()
        };

        _db.OnDutyTypeConfigs.Add(newType);
        await _db.SaveChangesAsync();

        await _auditLogService.LogAsync(
            action: "OnDutyTypeCreated",
            entityType: "OnDutyTypeConfig",
            entityId: newType.Id,
            description: $"Created custom OnDuty type: {NameEn} ({NameHe}) with value {autoTypeValue}",
            details: System.Text.Json.JsonSerializer.Serialize(new { TypeValue = autoTypeValue, NameEn, NameHe, Icon, Color }));

        Success = $"On-Duty type '{NameEn}' created successfully.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteOnDutyTypeAsync(int typeId)
    {
        var type = await _db.OnDutyTypeConfigs.FindAsync(typeId);
        if (type == null)
        {
            Error = "On-Duty type not found.";
            await OnGetAsync();
            return Page();
        }

        // Check if this type is in use
        var inUse = await _db.OnDuties
            .IgnoreQueryFilters()
            .AnyAsync(o => (int)o.Type == type.TypeValue && o.CanceledAt == null);

        if (inUse)
        {
            Error = $"Cannot delete '{type.NameEn}' - it is currently assigned to active on-duty shifts.";
            await OnGetAsync();
            return Page();
        }

        // Soft delete by marking as inactive
        type.IsActive = false;
        await _db.SaveChangesAsync();

        await _auditLogService.LogAsync(
            action: "OnDutyTypeDeleted",
            entityType: "OnDutyTypeConfig",
            entityId: type.Id,
            description: $"Deleted custom OnDuty type: {type.NameEn} (value {type.TypeValue})",
            details: System.Text.Json.JsonSerializer.Serialize(new { TypeValue = type.TypeValue, NameEn = type.NameEn }));

        Success = $"On-Duty type '{type.NameEn}' deleted successfully.";
        return RedirectToPage();
    }

    private int GetInt(int companyId, string key, int def)
    {
        var v = _db.Configs.FirstOrDefault(c => c.CompanyId == companyId && c.Key == key)?.Value;
        return int.TryParse(v, out var i) ? i : def;
    }
    private async Task Set(int companyId, string key, string value)
    {
        var c = await _db.Configs.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Key == key);
        if (c == null) { c = new AppConfig{ CompanyId = companyId, Key = key, Value = value }; _db.Configs.Add(c); }
        else c.Value = value;
        await _db.SaveChangesAsync();
    }
}
