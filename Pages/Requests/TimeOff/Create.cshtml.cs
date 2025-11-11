using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Pages.Requests.TimeOff;

[Authorize]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    public CreateModel(AppDbContext db) => _db = db;

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
            ModelState.AddModelError("", "End date cannot be before start date.");
            return Page();
        }

        // Validate dates are not in the past
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (StartDate < today)
        {
            ModelState.AddModelError("", "Cannot request time off for past dates.");
            return Page();
        }

        // Validate dates are not too far in the future (prevent abuse)
        var maxFutureDate = today.AddYears(2);
        if (StartDate > maxFutureDate || EndDate > maxFutureDate)
        {
            ModelState.AddModelError("", "Cannot request time off more than 2 years in advance.");
            return Page();
        }

        // Validate time-off duration is reasonable (max 1 year for Vacation)
        if (Type == TimeOffType.Vacation)
        {
            var daysDifference = EndDate.DayNumber - StartDate.DayNumber;
            if (daysDifference > 365)
            {
                ModelState.AddModelError("", "Time off request cannot exceed 365 days. Please split into multiple requests.");
                return Page();
            }
        }

        // Validate reason length
        if (!string.IsNullOrWhiteSpace(Reason) && Reason.Length > 1000)
        {
            ModelState.AddModelError("", "Reason must not exceed 1000 characters.");
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
