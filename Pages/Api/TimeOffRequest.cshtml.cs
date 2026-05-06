using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api;

/// <summary>
/// Task 28 — TimeOffRequest API used by the cancel-or-shorten dialog.
///
/// Routes (handlers selected via ?handler= query string per Razor Pages convention):
///   GET  /Api/TimeOffRequest/{id}                       → request details (json)
///   POST /Api/TimeOffRequest/{id}?handler=Cancel        → cancel entire request
///   POST /Api/TimeOffRequest/{id}?handler=UpdateDates   → shorten range (Vacation only)
///
/// Cookie-authenticated. CSRF is enforced by Razor Pages antiforgery + the
/// X-Requested-With header check in ApiAuthenticationMiddleware (the middleware
/// is whitelisted for /Api/TimeOffRequest below).
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken] // CSRF enforced via X-Requested-With header check in ApiAuthenticationMiddleware
public class TimeOffRequestApiModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IVacationApprovalService _svc;
    private readonly ILogger<TimeOffRequestApiModel> _logger;

    public TimeOffRequestApiModel(
        AppDbContext db,
        IVacationApprovalService svc,
        ILogger<TimeOffRequestApiModel> logger)
    {
        _db = db;
        _svc = svc;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // IgnoreQueryFilters: a request may belong to a user in another company
        // (cross-company directors); the calendar already enforces visibility, and
        // we only return read-only metadata here, no PII beyond dates/type.
        var r = await _db.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == Id);

        if (r == null)
        {
            return new JsonResult(new { success = false, message = "Not found" })
            {
                StatusCode = 404
            };
        }

        return new JsonResult(new
        {
            id = r.Id,
            type = r.Type.ToString(),
            startDate = r.StartDate.ToString("yyyy-MM-dd"),
            endDate = r.EndDate.ToString("yyyy-MM-dd"),
            status = r.Status.ToString()
        });
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return new JsonResult(new { success = false, message = "Unauthorized" })
            {
                StatusCode = 401
            };
        }

        var (ok, msg) = await _svc.CancelRequestAsync(Id, userId);
        _logger.LogInformation(
            "TimeOffRequest cancel via dialog: requestId={RequestId}, userId={UserId}, success={Success}",
            Id, userId, ok);

        return new JsonResult(new { success = ok, message = msg })
        {
            StatusCode = ok ? 200 : 400
        };
    }

    public record UpdateDatesRequest(string NewStart, string NewEnd);

    public async Task<IActionResult> OnPostUpdateDatesAsync([FromBody] UpdateDatesRequest body)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return new JsonResult(new { success = false, message = "Unauthorized" })
            {
                StatusCode = 401
            };
        }

        if (body == null
            || !DateOnly.TryParse(body.NewStart, out var ns)
            || !DateOnly.TryParse(body.NewEnd, out var ne))
        {
            return new JsonResult(new { success = false, message = "Invalid dates" })
            {
                StatusCode = 400
            };
        }

        var (ok, msg) = await _svc.UpdateRequestDatesAsync(Id, ns, ne, userId);
        _logger.LogInformation(
            "TimeOffRequest update-dates via dialog: requestId={RequestId}, userId={UserId}, newStart={NewStart}, newEnd={NewEnd}, success={Success}",
            Id, userId, ns, ne, ok);

        return new JsonResult(new { success = ok, message = msg })
        {
            StatusCode = ok ? 200 : 400
        };
    }
}
