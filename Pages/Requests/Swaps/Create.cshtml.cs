using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Requests.Swaps;

[Authorize]
public class CreateModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;

    public CreateModel(IStringLocalizer<SharedResources> localizer, AppDbContext db, ICompanyContext companyContext)
        : base(localizer)
    {
        _db = db;
        _companyContext = companyContext;
    }

    public record AssignmentVM(int AssignmentId, string Label);
    public List<AssignmentVM> MyAssignments { get; set; } = new();
    public List<AppUser> OtherUsers { get; set; } = new();

    [BindProperty] public int? SelectedAssignmentId { get; set; }
    [BindProperty] public int? ToUserId { get; set; }

    public async Task<IActionResult> OnGetAsync()
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

        var upcoming = await (from a in _db.ShiftAssignments
                              join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                              join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                              where a.UserId == userId && si.WorkDate >= DateOnly.FromDateTime(DateTime.Today)
                              orderby si.WorkDate
                              select new AssignmentVM(a.Id, $"{si.WorkDate:yyyy-MM-dd} {st.Key}")).ToListAsync();
        MyAssignments = upcoming;

        var companyId = _companyContext.GetCompanyIdOrThrow();
        OtherUsers = await _db.Users.Where(u => u.CompanyId == companyId && u.IsActive && u.Id != userId).OrderBy(u => u.DisplayName).ToListAsync();
        return Page();
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
            ModelState.AddModelError("", _localizer["Error_SelectValidShiftAssignment"].Value);
            await OnGetAsync();
            return Page();
        }

        if (!ToUserId.HasValue || ToUserId.Value <= 0)
        {
            ModelState.AddModelError("", _localizer["Error_SelectValidSwapUser"].Value);
            await OnGetAsync();
            return Page();
        }

        // Prevent swapping with yourself
        if (ToUserId.Value == userId)
        {
            ModelState.AddModelError("", _localizer["Error_CannotSwapWithSelf"].Value);
            await OnGetAsync();
            return Page();
        }

        // Validate that the assignment belongs to the current user (authorization check)
        var assignment = await _db.ShiftAssignments
            .Include(a => a.ShiftInstance)
            .FirstOrDefaultAsync(a => a.Id == SelectedAssignmentId.Value);

        if (assignment == null)
        {
            ModelState.AddModelError("", _localizer["Error_ShiftAssignmentNotFound"].Value);
            await OnGetAsync();
            return Page();
        }

        if (assignment.UserId != userId)
        {
            ModelState.AddModelError("", _localizer["Error_CanOnlySwapOwnShifts"].Value);
            await OnGetAsync();
            return Page();
        }

        // Validate that ToUser is valid and in same company
        var companyId = _companyContext.GetCompanyIdOrThrow();
        var toUser = await _db.Users.FindAsync(ToUserId.Value);

        if (toUser == null || !toUser.IsActive || toUser.CompanyId != companyId)
        {
            ModelState.AddModelError("", _localizer["Error_InvalidSwapTargetUser"].Value);
            await OnGetAsync();
            return Page();
        }

        _db.SwapRequests.Add(new SwapRequest { FromAssignmentId = SelectedAssignmentId.Value, FromUserId = userId, ToUserId = ToUserId.Value });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Requests/Index");
    }
}
