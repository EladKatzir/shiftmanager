using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Requests.TimeOff;

[Authorize]
public class CreateModel : LocalizedPageModel
{
    private readonly AppDbContext _db;

    public CreateModel(IStringLocalizer<SharedResources> localizer, AppDbContext db) : base(localizer)
    {
        _db = db;
    }

    [BindProperty] public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public TimeOffType Type { get; set; } = TimeOffType.Vacation;
    [BindProperty] public string? Reason { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        // For "After" type, set EndDate to StartDate
        if (Type == TimeOffType.After)
        {
            EndDate = StartDate;
        }

        // ✅ SECURITY FIX: Input validation
        if (EndDate < StartDate)
        {
            ModelState.AddModelError("", _localizer["Error_EndDateBeforeStartDate"]);
            return Page();
        }

        // Validate dates are not in the past
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (StartDate < today)
        {
            ModelState.AddModelError("", _localizer["Error_CannotRequestTimeOffForPastDates"]);
            return Page();
        }

        // Validate dates are not too far in the future (prevent abuse)
        var maxFutureDate = today.AddYears(2);
        if (StartDate > maxFutureDate || EndDate > maxFutureDate)
        {
            ModelState.AddModelError("", _localizer["Error_CannotRequestTimeOffTooFarInFuture"]);
            return Page();
        }

        // Validate time-off duration is reasonable (max 1 year for Vacation)
        if (Type == TimeOffType.Vacation)
        {
            var daysDifference = EndDate.DayNumber - StartDate.DayNumber;
            if (daysDifference > 365)
            {
                ModelState.AddModelError("", _localizer["Error_TimeOffRequestTooLong"]);
                return Page();
            }
        }

        // Validate reason length
        if (!string.IsNullOrWhiteSpace(Reason) && Reason.Length > 1000)
        {
            ModelState.AddModelError("", _localizer["Error_ReasonTooLong"]);
            return Page();
        }

        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }

        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            UserId = userId,
            StartDate = StartDate,
            EndDate = EndDate,
            Type = Type,
            Reason = Reason
        });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Requests/Index");
    }
}
