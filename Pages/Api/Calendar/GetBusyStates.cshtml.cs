using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// Returns per-user busy summaries for a date — used by assignee pickers to
/// decorate options with busy badges. When a target is supplied, also runs
/// the full validation predicate per user so the picker can disable hard-conflict
/// users in lock-step with the post-submit validator.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class GetBusyStatesModel : PageModel
{
    private readonly IBusyService _busyService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetBusyStatesModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public GetBusyStatesModel(
        IBusyService busyService,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetBusyStatesModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _busyService = busyService;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
                return new JsonResult(new { success = false, message = "Not authenticated" }) { StatusCode = 401 };

            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<BusyStatesRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || request.UserIds == null || request.UserIds.Length == 0)
                return new JsonResult(new { success = false, message = "Invalid request" }) { StatusCode = 400 };

            if (!DateOnly.TryParse(request.Date, out var date))
                return new JsonResult(new { success = false, message = "Invalid date" }) { StatusCode = 400 };

            if (request.MoleculeId <= 0)
                return new JsonResult(new { success = false, message = "Invalid moleculeId" }) { StatusCode = 400 };

            // SECURITY: validate molecule scope access — same pattern as GetEligibleUsersForShift
            var hasAccess = await _scopeFilterService.ValidateScopeAccessAsync("molecule", request.MoleculeId, "shifts");
            if (!hasAccess)
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                var userCompany = user != null ? await _db.Companies.FindAsync(user.CompanyId) : null;
                if (userCompany?.MoleculeId != request.MoleculeId)
                {
                    _logger.LogWarning("SECURITY: User {UserId} attempted to query busy state for molecule {MoleculeId} outside scope",
                        currentUserId, request.MoleculeId);
                    return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                }
            }

            BusyTarget? target = ParseTarget(request.Target);

            var states = await _busyService.GetBusyStatesAsync(
                request.UserIds,
                date,
                request.MoleculeId,
                target);

            var payload = states.ToDictionary(
                kv => kv.Key.ToString(),
                kv => new
                {
                    userId = kv.Key,
                    hasShift = kv.Value.Summary.HasShift,
                    shift = kv.Value.Summary.Shift,
                    hasChore = kv.Value.Summary.HasChore,
                    choreTitle = kv.Value.Summary.ChoreTitle,
                    hasOnDuty = kv.Value.Summary.HasOnDuty,
                    onDutyType = kv.Value.Summary.OnDutyType?.ToString(),
                    hasVacation = kv.Value.Summary.HasVacation,
                    vacationEnd = kv.Value.Summary.VacationEnd?.ToString("yyyy-MM-dd"),
                    highest = kv.Value.Summary.Highest.ToString(),
                    hasHardError = kv.Value.HasHardError,
                    hardErrorKey = kv.Value.HardErrorKey
                });

            return new JsonResult(new { success = true, busy = payload });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetBusyStates");
            return new JsonResult(new { success = false, message = "Internal error" }) { StatusCode = 500 };
        }
    }

    private static BusyTarget? ParseTarget(BusyStatesTarget? raw)
    {
        if (raw == null || string.IsNullOrEmpty(raw.Kind)) return null;

        return raw.Kind.ToLowerInvariant() switch
        {
            "shift" when raw.ShiftInstanceId.HasValue => new BusyTarget.Shift(raw.ShiftInstanceId.Value),
            "chore" when raw.MoleculeId.HasValue && DateOnly.TryParse(raw.Date, out var cd)
                => new BusyTarget.Chore(cd, raw.MoleculeId.Value, raw.ChoreTypeId),
            "onduty" when raw.MoleculeId.HasValue && raw.OnDutyType.HasValue && DateOnly.TryParse(raw.Date, out var od)
                => new BusyTarget.OnDuty(od, (OnDutyType)raw.OnDutyType.Value, raw.MoleculeId.Value),
            _ => null
        };
    }

    private class BusyStatesRequest
    {
        [JsonPropertyName("userIds")]
        public int[] UserIds { get; set; } = Array.Empty<int>();

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("moleculeId")]
        public int MoleculeId { get; set; }

        [JsonPropertyName("target")]
        public BusyStatesTarget? Target { get; set; }
    }

    private class BusyStatesTarget
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("shiftInstanceId")]
        public int? ShiftInstanceId { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("moleculeId")]
        public int? MoleculeId { get; set; }

        [JsonPropertyName("choreTypeId")]
        public int? ChoreTypeId { get; set; }

        [JsonPropertyName("onDutyType")]
        public int? OnDutyType { get; set; }
    }
}
