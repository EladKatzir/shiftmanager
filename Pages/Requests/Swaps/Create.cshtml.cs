using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Requests.Swaps;

[Authorize]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;

    public CreateModel(AppDbContext db, ICompanyContext companyContext)
    {
        _db = db;
        _companyContext = companyContext;
    }

    public record AssignmentVM(int AssignmentId, string Label);
    public List<AssignmentVM> MyAssignments { get; set; } = new();
    public List<AppUser> OtherUsers { get; set; } = new();

    [BindProperty] public int? SelectedAssignmentId { get; set; }
    [BindProperty] public int? ToUserId { get; set; }

    public async Task OnGetAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            Response.Redirect("/Auth/Login");
            return;
        }

        // Block trainees from creating swap requests
        var currentUser = await _db.Users.FindAsync(userId);
        if (currentUser?.Role == UserRole.Trainee)
        {
            Response.Redirect("/AccessDenied");
            return;
        }

        var upcoming = await (from a in _db.ShiftAssignments
                              join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                              join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                              where a.UserId == userId && si.WorkDate >= DateOnly.FromDateTime(DateTime.Today)
                              orderby si.WorkDate
                              select new AssignmentVM(a.Id, $"{si.WorkDate:yyyy-MM-dd} {st.Key}")).ToListAsync();
        MyAssignments = upcoming;

        var companyId = _companyContext.GetCompanyIdOrThrow();
        OtherUsers = await _db.Users.Where(u => u.CompanyId == companyId && u.IsActive && u.Id != userId).OrderBy(u => u.DisplayName).ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }

        // Block trainees from creating swap requests
        var currentUser = await _db.Users.FindAsync(userId);
        if (currentUser?.Role == UserRole.Trainee)
        {
            return RedirectToPage("/AccessDenied");
        }

        // ✅ SECURITY FIX: Input validation
        if (!SelectedAssignmentId.HasValue || SelectedAssignmentId.Value <= 0)
        {
            ModelState.AddModelError("", "Please select a valid shift assignment.");
            await OnGetAsync();
            return Page();
        }

        if (!ToUserId.HasValue || ToUserId.Value <= 0)
        {
            ModelState.AddModelError("", "Please select a valid user to swap with.");
            await OnGetAsync();
            return Page();
        }

        // Prevent swapping with yourself
        if (ToUserId.Value == userId)
        {
            ModelState.AddModelError("", "Cannot swap shift with yourself.");
            await OnGetAsync();
            return Page();
        }

        // Validate that the assignment belongs to the current user (authorization check)
        var assignment = await _db.ShiftAssignments
            .Include(a => a.ShiftInstance)
            .FirstOrDefaultAsync(a => a.Id == SelectedAssignmentId.Value);

        if (assignment == null)
        {
            ModelState.AddModelError("", "Shift assignment not found.");
            await OnGetAsync();
            return Page();
        }

        if (assignment.UserId != userId)
        {
            ModelState.AddModelError("", "You can only swap your own shifts.");
            await OnGetAsync();
            return Page();
        }

        // Validate that ToUser is valid and in same company
        var companyId = _companyContext.GetCompanyIdOrThrow();
        var toUser = await _db.Users.FindAsync(ToUserId.Value);

        if (toUser == null || !toUser.IsActive || toUser.CompanyId != companyId)
        {
            ModelState.AddModelError("", "Selected user is not valid or not in your company.");
            await OnGetAsync();
            return Page();
        }

        _db.SwapRequests.Add(new SwapRequest { FromAssignmentId = SelectedAssignmentId.Value, ToUserId = ToUserId.Value });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Requests/Index");
    }
}
