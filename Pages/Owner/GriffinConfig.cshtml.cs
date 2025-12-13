using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

[Authorize(Policy = "IsAdmin")]
public class GriffinConfigModel : PageModel
{
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<GriffinConfigModel> _logger;

    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public string BaseUrl { get; set; } = string.Empty;
    [BindProperty] public string TokenConsumerUrl { get; set; } = string.Empty;
    [BindProperty] public bool AutoProvisionUsers { get; set; }
    [BindProperty] public UserRole DefaultProvisionedRole { get; set; } = UserRole.Employee;
    [BindProperty] public int TimeoutSeconds { get; set; } = 10;

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? TestConnectionResult { get; set; }

    public SelectList RoleOptions { get; set; } = null!;

    public GriffinConfigModel(
        IGriffinConfigService griffinConfigService,
        IAuditLogService auditLogService,
        ILogger<GriffinConfigModel> logger)
    {
        _griffinConfigService = griffinConfigService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task OnGetAsync()
    {
        await LoadConfigAsync();
        LoadRoleOptions();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        LoadRoleOptions();

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
                TimeoutSeconds,
                userName);

            SuccessMessage = "Griffin ADFS configuration saved successfully.";

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
            ErrorMessage = "Failed to save configuration. Please try again.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostTestConnectionAsync()
    {
        LoadRoleOptions();
        await LoadConfigAsync(); // Preserve current values

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            ErrorMessage = "Please enter a Base URL to test.";
            return Page();
        }

        try
        {
            var isAvailable = await _griffinConfigService.TestConnectionAsync(BaseUrl, TimeoutSeconds);
            TestConnectionResult = isAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Griffin connection test failed");
            TestConnectionResult = false;
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
            TimeoutSeconds = config.TimeoutSeconds;
        }
    }

    private void LoadRoleOptions()
    {
        RoleOptions = new SelectList(Enum.GetValues(typeof(UserRole)).Cast<UserRole>());
    }

    private string? ValidateInputs()
    {
        if (Enabled)
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
                return "Base URL is required when Griffin is enabled.";

            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != "http" && baseUri.Scheme != "https"))
                return "Base URL must be a valid HTTP or HTTPS URL.";

            if (string.IsNullOrWhiteSpace(TokenConsumerUrl))
                return "Token Consumer URL is required when Griffin is enabled.";

            if (!Uri.TryCreate(TokenConsumerUrl, UriKind.Absolute, out var callbackUri) ||
                (callbackUri.Scheme != "http" && callbackUri.Scheme != "https"))
                return "Token Consumer URL must be a valid HTTP or HTTPS URL.";
        }

        if (TimeoutSeconds < 1 || TimeoutSeconds > 60)
            return "Timeout must be between 1 and 60 seconds.";

        return null;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
