using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ShiftManager.Models.Telemetry;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Owner Telemetry Dashboard — view client-side errors, analytics events,
/// performance (Web Vitals), and manage data retention.
/// B-019, B-020, B-021: Local Observability Stack
/// </summary>
[Authorize(Policy = "Grant:SystemConfiguration")]
public class TelemetryModel : ShiftManager.Pages.LocalizedPageModel
{
    private readonly IClientTelemetryService _telemetryService;
    private readonly ILogger<TelemetryModel> _logger;

    public TelemetryModel(
        IStringLocalizer<SharedResources> localizer,
        IClientTelemetryService telemetryService,
        ILogger<TelemetryModel> logger)
        : base(localizer)
    {
        _telemetryService = telemetryService;
        _logger = logger;
    }

    // ─── Errors Tab ────────────────────────────────────────────────────────
    public List<ClientError>? RecentErrors { get; set; }
    public Dictionary<string, int>? ErrorCountsByType { get; set; }

    // ─── Events Tab ────────────────────────────────────────────────────────
    public List<ClientAnalyticsEvent>? RecentEvents { get; set; }
    public Dictionary<string, int>? EventCountsByType { get; set; }

    // ─── Performance Tab ───────────────────────────────────────────────────
    public Dictionary<string, WebVitalsSummary>? WebVitalsSummary { get; set; }
    public List<PerformanceMetric>? RecentMetrics { get; set; }

    // ─── Shared ────────────────────────────────────────────────────────────
    public string ActiveTab { get; set; } = "errors";

    /// <summary>
    /// Date range used for counts / summary queries — last 7 days by default.
    /// </summary>
    public DateTime RangeStart { get; private set; }
    public DateTime RangeEnd { get; private set; }

    public async Task OnGetAsync(string? tab)
    {
        ActiveTab = tab ?? "errors";
        RangeEnd = DateTime.UtcNow;
        RangeStart = RangeEnd.AddDays(-7);

        switch (ActiveTab)
        {
            case "performance":
                WebVitalsSummary = await _telemetryService.GetWebVitalsSummaryAsync(RangeStart, RangeEnd);
                RecentMetrics = await _telemetryService.GetRecentPerformanceAsync(50);
                break;

            case "events":
                RecentEvents = await _telemetryService.GetRecentEventsAsync(100);
                EventCountsByType = await _telemetryService.GetEventCountsByTypeAsync(RangeStart, RangeEnd);
                break;

            case "cleanup":
                // No data required — just render the form.
                break;

            default: // "errors"
                ActiveTab = "errors";
                RecentErrors = await _telemetryService.GetRecentErrorsAsync(100);
                ErrorCountsByType = await _telemetryService.GetErrorCountsByTypeAsync(RangeStart, RangeEnd);
                break;
        }
    }

    /// <summary>
    /// Handles the data-cleanup POST. Deletes telemetry older than
    /// <paramref name="retentionDays"/> days and reports what was removed.
    /// </summary>
    public async Task<IActionResult> OnPostCleanupAsync(int retentionDays = 30)
    {
        if (retentionDays < 1 || retentionDays > 365)
        {
            Error = _localizer["Telemetry_Cleanup_InvalidRetention"].Value;
            ActiveTab = "cleanup";
            return Page();
        }

        try
        {
            var (events, errors, metrics) = await _telemetryService.CleanupOldDataAsync(retentionDays);
            _logger.LogInformation(
                "Telemetry cleanup triggered by owner: {Events} events, {Errors} errors, {Metrics} metrics deleted (>{Days}d)",
                events, errors, metrics, retentionDays);

            Success = string.Format(
                _localizer["Telemetry_Cleanup_Success"].Value,
                events, errors, metrics, retentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during telemetry cleanup");
            Error = _localizer["Telemetry_Cleanup_Error"].Value;
        }

        ActiveTab = "cleanup";
        return Page();
    }
}
