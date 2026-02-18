using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

// SECURITY-AUDITED: IgnoreQueryFilters() in this class is SAFE — Owner page requires Grant:AdminAccess (all 107 grants);
// cross-company SSO config management by design
[Authorize(Policy = "Grant:AdminAccess")]
public class GriffinConfigModel : LocalizedPageModel
{
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IGriffinApiLogService _griffinApiLogService;
    private readonly IAuditLogService _auditLogService;
    private readonly AppDbContext _db;
    private readonly ILogger<GriffinConfigModel> _logger;
    private readonly IRoleService _roleService;

    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public string BaseUrl { get; set; } = string.Empty;
    [BindProperty] public string TokenConsumerUrl { get; set; } = string.Empty;
    [BindProperty] public bool AutoProvisionUsers { get; set; }
    [BindProperty] public UserRole DefaultProvisionedRole { get; set; } = UserRole.Employee;
    [BindProperty] public int? DefaultProvisionedRoleTemplateId { get; set; }
    [BindProperty] public int TimeoutSeconds { get; set; } = 10;

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? TestConnectionResult { get; set; }

    // Diagnostic properties
    public string? TestDiagnostics { get; set; }
    public DateTime? LastTestTimestamp { get; set; }
    public bool? LastTestSuccess { get; set; }
    public string? LastTestError { get; set; }
    public List<GriffinApiLog> RecentLogs { get; set; } = new();
    public List<GriffinApiLog> RecentFailures { get; set; } = new();

    public SelectList RoleOptions { get; set; } = null!;
    public List<RoleTemplate> AvailableRoleTemplates { get; set; } = new();

    public GriffinConfigModel(
        IStringLocalizer<SharedResources> localizer,
        IGriffinConfigService griffinConfigService,
        IGriffinApiLogService griffinApiLogService,
        IAuditLogService auditLogService,
        AppDbContext db,
        ILogger<GriffinConfigModel> logger,
        IRoleService roleService)
        : base(localizer)
    {
        _griffinConfigService = griffinConfigService;
        _griffinApiLogService = griffinApiLogService;
        _auditLogService = auditLogService;
        _db = db;
        _logger = logger;
        _roleService = roleService;
    }

    public async Task OnGetAsync()
    {
        await LoadConfigAsync();
        await LoadRoleOptionsAsync();
        await LoadRecentLogsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadRoleOptionsAsync();

        // Validate
        var validationError = ValidateInputs();
        if (!string.IsNullOrEmpty(validationError))
        {
            ErrorMessage = validationError;
            return Page();
        }

        try
        {
            var userName = User.Identity?.Name ?? "Unknown";

            await _griffinConfigService.SaveGriffinConfigAsync(
                Enabled,
                BaseUrl,
                TokenConsumerUrl,
                AutoProvisionUsers,
                DefaultProvisionedRole,
                DefaultProvisionedRoleTemplateId,
                TimeoutSeconds,
                userName);

            SuccessMessage = _localizer["Success_GriffinConfigSaved"].Value;

            // Audit log
            await _auditLogService.LogUserActionAsync(
                GetCurrentUserId(),
                "GriffinConfigUpdated",
                "GriffinConfig",
                null,
                "Griffin ADFS configuration updated",
                $"Enabled={Enabled}, BaseUrl={BaseUrl}, AutoProvision={AutoProvisionUsers}");

            _logger.LogInformation("Griffin config updated by {User}", userName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Griffin configuration");
            ErrorMessage = _localizer["Error_FailedToSaveGriffinConfig"].Value;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostTestConnectionAsync()
    {
        await LoadRoleOptionsAsync();
        await LoadRecentLogsAsync(); // Load logs for display

        // ✅ CRITICAL FIX: Validate before testing
        var validationError = ValidateInputs();
        if (!string.IsNullOrEmpty(validationError))
        {
            ErrorMessage = validationError;
            return Page();
        }

        try
        {
            var userName = User.Identity?.Name ?? "Unknown";

            // ✅ CRITICAL FIX: SAVE configuration FIRST, then test
            // This ensures the test uses the SAME configuration that Login will use
            _logger.LogInformation("Saving Griffin config before testing (to ensure test matches login behavior)");

            await _griffinConfigService.SaveGriffinConfigAsync(
                Enabled,
                BaseUrl,
                TokenConsumerUrl,
                AutoProvisionUsers,
                DefaultProvisionedRole,
                DefaultProvisionedRoleTemplateId,
                TimeoutSeconds,
                userName);

            // Now test using the SAVED configuration (not the form value)
            var savedConfig = await _griffinConfigService.GetGriffinConfigAsync();

            if (savedConfig == null || string.IsNullOrWhiteSpace(savedConfig.BaseUrl))
            {
                ErrorMessage = _localizer["Error_FailedToLoadGriffinConfig"].Value;
                return Page();
            }

            _logger.LogInformation("Testing connection with SAVED configuration: BaseUrl={BaseUrl}", savedConfig.BaseUrl);

            var result = await _griffinConfigService.TestConnectionAsync(savedConfig.BaseUrl, savedConfig.TimeoutSeconds);

            TestConnectionResult = result.Success;
            LastTestTimestamp = DateTime.UtcNow;
            LastTestSuccess = result.Success;
            LastTestError = result.ErrorMessage;

            // Format diagnostic output for display
            TestDiagnostics = FormatDiagnostics(result);

            if (result.Success)
            {
                SuccessMessage = $"✅ Configuration saved and connection successful! Griffin responded with HTTP {result.StatusCode} in {result.DurationMs}ms. Login with ADFS will now work with these settings.";

                // Audit log
                await _auditLogService.LogUserActionAsync(
                    GetCurrentUserId(),
                    "GriffinConfigTestedSuccessfully",
                    "GriffinConfig",
                    null,
                    "Griffin ADFS connection test successful",
                    $"BaseUrl={savedConfig.BaseUrl}, StatusCode={result.StatusCode}, Duration={result.DurationMs}ms");
            }
            else
            {
                ErrorMessage = $"⚠️ Configuration saved, but connection test failed: {result.ErrorMessage}. Please verify the Base URL and ensure the Griffin server is reachable.";
            }

            // Reload logs to show the new test result
            await LoadRecentLogsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Griffin connection test failed with exception");
            TestConnectionResult = false;
            LastTestSuccess = false;
            LastTestError = "An unexpected error occurred during the connection test.";
            ErrorMessage = "Connection test failed. Please check your configuration and try again.";
        }

        return Page();
    }

    private async Task LoadConfigAsync()
    {
        var config = await _griffinConfigService.GetGriffinConfigAsync();

        if (config != null)
        {
            Enabled = config.Enabled;
            BaseUrl = config.BaseUrl ?? string.Empty;
            TokenConsumerUrl = config.TokenConsumerUrl ?? string.Empty;
            AutoProvisionUsers = config.AutoProvisionUsers;
            DefaultProvisionedRole = config.DefaultProvisionedRole;
            DefaultProvisionedRoleTemplateId = config.DefaultProvisionedRoleTemplateId;
            TimeoutSeconds = config.TimeoutSeconds;
        }
    }

    private async Task LoadRoleOptionsAsync()
    {
        RoleOptions = new SelectList(Enum.GetValues(typeof(UserRole)).Cast<UserRole>());
        AvailableRoleTemplates = await _roleService.GetRoleTemplatesAsync();
    }

    private string? ValidateInputs()
    {
        if (Enabled)
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
                return _localizer["Error_GriffinBaseUrlRequired"].Value;

            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != "http" && baseUri.Scheme != "https"))
                return _localizer["Error_GriffinBaseUrlInvalid"].Value;

            if (string.IsNullOrWhiteSpace(TokenConsumerUrl))
                return _localizer["Error_GriffinTokenConsumerUrlRequired"].Value;

            if (!Uri.TryCreate(TokenConsumerUrl, UriKind.Absolute, out var callbackUri) ||
                (callbackUri.Scheme != "http" && callbackUri.Scheme != "https"))
                return _localizer["Error_GriffinTokenConsumerUrlInvalid"].Value;
        }

        if (TimeoutSeconds < 1 || TimeoutSeconds > 60)
            return _localizer["Error_GriffinTimeoutOutOfRange"].Value;

        return null;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    private async Task LoadRecentLogsAsync()
    {
        try
        {
            RecentLogs = await _griffinApiLogService.GetRecentLogsAsync(10);
            RecentFailures = await _griffinApiLogService.GetFailedLogsAsync(5);

            // Set last test status from most recent log
            var lastLog = RecentLogs.FirstOrDefault();
            if (lastLog != null)
            {
                LastTestTimestamp = lastLog.Timestamp;
                LastTestSuccess = lastLog.Success;
                LastTestError = lastLog.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Griffin API logs");
            // Don't fail the page load if log loading fails
        }
    }

    private string FormatDiagnostics(GriffinConnectionTestResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== Griffin Connection Test Diagnostics ===");
        sb.AppendLine();
        sb.AppendLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Duration: {result.DurationMs} ms");
        sb.AppendLine($"Success: {(result.Success ? "✓ YES" : "✗ NO")}");
        sb.AppendLine();

        if (result.ValidationErrors != null && result.ValidationErrors.Any())
        {
            sb.AppendLine("--- Validation Errors ---");
            foreach (var error in result.ValidationErrors)
            {
                sb.AppendLine($"  • {error}");
            }
            sb.AppendLine();
        }

        if (result.StatusCode.HasValue)
        {
            sb.AppendLine("--- HTTP Response ---");
            sb.AppendLine($"Status Code: {result.StatusCode}");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(result.RedirectUrl))
        {
            sb.AppendLine("--- Redirect Information ---");
            sb.AppendLine($"Redirect URL: {result.RedirectUrl}");
            sb.AppendLine("(This is expected - Griffin redirects to ADFS login page)");
            sb.AppendLine();
        }

        if (result.ResponseHeaders != null && result.ResponseHeaders.Any())
        {
            sb.AppendLine("--- Response Headers ---");
            foreach (var header in result.ResponseHeaders.OrderBy(h => h.Key))
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(result.ResponseBody) && result.ResponseBody.Length > 0)
        {
            sb.AppendLine("--- Response Body (first 500 chars) ---");
            var bodyPreview = result.ResponseBody.Length > 500
                ? result.ResponseBody.Substring(0, 500) + "... (truncated)"
                : result.ResponseBody;
            sb.AppendLine(bodyPreview);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            sb.AppendLine("--- Error Details ---");
            sb.AppendLine(result.ErrorMessage);
            sb.AppendLine();
        }

        sb.AppendLine("=== End Diagnostics ===");

        return sb.ToString();
    }
}
