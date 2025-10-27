using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Pages.Requests.TimeOff;

[Authorize]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    public CreateModel(AppDbContext db) => _db = db;

    [BindProperty] public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public string? Reason { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (EndDate < StartDate) { ModelState.AddModelError("", "End date cannot be before start date."); return Page(); }
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }
        _db.TimeOffRequests.Add(new TimeOffRequest { UserId = userId, StartDate = StartDate, EndDate = EndDate, Reason = Reason });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Requests/Index");
    }
}
