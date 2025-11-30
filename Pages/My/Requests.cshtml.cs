using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ShiftManager.Pages.My;

[Authorize]
public class RequestsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<RequestsModel> _logger;
    public RequestsModel(AppDbContext db, ILogger<RequestsModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    [BindProperty]
    public TimeOffRequestForm TimeOffRequest { get; set; } = new();

    [BindProperty]
    public SwapRequestForm SwapRequest { get; set; } = new();

    public List<MyTimeOffRequest> MyTimeOffRequests { get; set; } = new();
    public List<MySwapRequest> MySwapRequests { get; set; } = new();
    public List<AvailableShift> AvailableShifts { get; set; } = new();
    public List<ManagerUser> AvailableApprovers { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            _logger.LogInformation("Starting OnGetAsync for requests page");
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                Error = "Authentication error. Please log in again.";
                return;
            }
            _logger.LogInformation("User ID: {UserId}", userId);

            // Load user's time off requests
            _logger.LogInformation("Loading time off requests for user {UserId}", userId);
            MyTimeOffRequests = await _db.TimeOffRequests
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new MyTimeOffRequest
                {
                    Id = r.Id,
                    StartDate = r.StartDate,
                    EndDate = r.EndDate,
                    Reason = r.Reason ?? "",
                    Status = r.Status.ToString(),
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();
            _logger.LogInformation("Loaded {Count} time off requests for user {UserId}", MyTimeOffRequests.Count, userId);

            // Load user's swap requests - simplified query first
            _logger.LogInformation("Loading shift assignments for user {UserId}", userId);
            var userAssignmentIds = await _db.ShiftAssignments
                .Where(sa => sa.UserId == userId)
                .Select(sa => sa.Id)
                .ToListAsync();
            _logger.LogInformation("Found {Count} shift assignments for user {UserId}", userAssignmentIds.Count, userId);

            _logger.LogInformation("Loading swap requests for user assignments");
            MySwapRequests = await _db.SwapRequests
                .Where(sr => userAssignmentIds.Contains(sr.FromAssignmentId))
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new MySwapRequest
                {
                    Id = r.Id,
                    ShiftDate = DateOnly.FromDateTime(DateTime.Today), // Will fix with proper join later
                    ShiftTypeName = "Swap Request", // Will fix with proper join later
                    ToUserName = "Target User", // Will fix with proper join later
                    Status = r.Status.ToString(),
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();
            _logger.LogInformation("Loaded {Count} swap requests for user {UserId}", MySwapRequests.Count, userId);

            // Load available shifts for swapping (user's upcoming assignments)
            _logger.LogInformation("Loading available shifts for swapping for user {UserId}", userId);
            AvailableShifts = await _db.ShiftAssignments
                .Where(sa => sa.UserId == userId)
                .Join(_db.ShiftInstances,
                    sa => sa.ShiftInstanceId,
                    si => si.Id,
                    (sa, si) => new { Assignment = sa, Instance = si })
                .Join(_db.ShiftTypes,
                    x => x.Instance.ShiftTypeId,
                    st => st.Id,
                    (x, st) => new AvailableShift
                    {
                        ShiftId = x.Assignment.Id,
                        Date = x.Instance.WorkDate,
                        ShiftTypeName = st.Name,
                        StartTime = st.Start,
                        EndTime = st.End
                    })
                .Where(s => s.Date >= DateOnly.FromDateTime(DateTime.Today))
                .OrderBy(s => s.Date)
                .ToListAsync();
            _logger.LogInformation("Loaded {Count} available shifts for user {UserId}", AvailableShifts.Count, userId);

            // Load available approvers (managers, directors, owners in the same company)
            _logger.LogInformation("Loading available approvers for user {UserId}", userId);
            var currentUser = await _db.Users.FindAsync(userId);
            if (currentUser != null)
            {
                AvailableApprovers = await _db.Users
                    .Where(u => u.CompanyId == currentUser.CompanyId &&
                               u.IsActive &&
                               (u.Role == UserRole.Manager || u.Role == UserRole.Director || u.Role == UserRole.Owner))
                    .OrderBy(u => u.DisplayName)
                    .Select(u => new ManagerUser
                    {
                        Id = u.Id,
                        Name = u.DisplayName,
                        Role = u.Role.ToString()
                    })
                    .ToListAsync();
                _logger.LogInformation("Loaded {Count} available approvers for user {UserId}", AvailableApprovers.Count, userId);
            }

            _logger.LogInformation("OnGetAsync completed successfully for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnGetAsync for requests page");
            Error = "An error occurred while loading your requests. Please try again.";
        }
    }

    public async Task<IActionResult> OnPostTimeOffAsync()
    {
        try
        {
            _logger.LogInformation("Starting time off request submission");

            // Clear validation errors for other forms (since both models are on the same page)
            ModelState.ClearValidationState(nameof(SwapRequest));

            // For After-duty vacation, EndDate should equal StartDate
            if (TimeOffRequest.Type == TimeOffType.After)
            {
                TimeOffRequest.EndDate = TimeOffRequest.StartDate;
            }

            // Custom validation for date range (only for regular vacation)
            if (TimeOffRequest.Type == TimeOffType.Vacation && TimeOffRequest.EndDate < TimeOffRequest.StartDate)
            {
                ModelState.AddModelError("TimeOffRequest.EndDate", "End date cannot be before start date.");
                _logger.LogWarning("Time off request validation failed: End date {EndDate} is before start date {StartDate}", TimeOffRequest.EndDate, TimeOffRequest.StartDate);
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Time off request model state is invalid: {Errors}", string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                await OnGetAsync();
                return Page();
            }

            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                Error = "Authentication error. Please log in again.";
                await OnGetAsync();
                return Page();
            }
            _logger.LogInformation("Time off request for user {UserId}, dates {StartDate} to {EndDate}", userId, TimeOffRequest.StartDate, TimeOffRequest.EndDate);

            // Validate approver if specified
            if (TimeOffRequest.ApproverId.HasValue && TimeOffRequest.ApproverId.Value > 0)
            {
                var approver = await _db.Users.FindAsync(TimeOffRequest.ApproverId.Value);
                if (approver == null || !approver.IsActive ||
                    (approver.Role != UserRole.Manager && approver.Role != UserRole.Director && approver.Role != UserRole.Owner))
                {
                    Error = "Invalid approver selected. Please select a valid manager.";
                    await OnGetAsync();
                    return Page();
                }
            }

            var request = new TimeOffRequest
            {
                UserId = userId,
                StartDate = TimeOffRequest.StartDate,
                EndDate = TimeOffRequest.EndDate,
                Type = TimeOffRequest.Type,
                Reason = TimeOffRequest.Reason,
                ApproverId = TimeOffRequest.ApproverId > 0 ? TimeOffRequest.ApproverId : null,
                Status = RequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _db.TimeOffRequests.Add(request);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Time off request {RequestId} submitted successfully for user {UserId}, Type: {Type}, Approver: {ApproverId}",
                request.Id, userId, request.Type, request.ApproverId);
            Message = "Time off request submitted successfully!";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting time off request");
            Error = "An error occurred while submitting your request. Please try again.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSwapAsync()
    {
        try
        {
            _logger.LogInformation("Starting swap request submission");

            // Clear validation errors for other forms (since both models are on the same page)
            ModelState.ClearValidationState(nameof(TimeOffRequest));

            // Manual validation for swap request since attributes were removed to prevent cross-validation
            if (SwapRequest.ShiftId <= 0)
            {
                ModelState.AddModelError("SwapRequest.ShiftId", "Please select a shift to swap.");
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Swap request model state is invalid: {Errors}", string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                await OnGetAsync();
                return Page();
            }

            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                Error = "Authentication error. Please log in again.";
                await OnGetAsync();
                return Page();
            }
            _logger.LogInformation("Swap request for user {UserId}, ShiftId {ShiftId}", userId, SwapRequest.ShiftId);

            // Verify the assignment belongs to the user
            var assignment = await _db.ShiftAssignments
                .FirstOrDefaultAsync(sa => sa.Id == SwapRequest.ShiftId && sa.UserId == userId);

            if (assignment == null)
            {
                _logger.LogWarning("Assignment {ShiftId} not found for user {UserId}", SwapRequest.ShiftId, userId);
                Error = "You are not assigned to this shift.";
                await OnGetAsync();
                return Page();
            }

            _logger.LogInformation("Found assignment {AssignmentId} for user {UserId}", assignment.Id, userId);

            var swapRequest = new SwapRequest
            {
                FromAssignmentId = assignment.Id,
                ToUserId = SwapRequest.ToUserId == 0 ? 1 : SwapRequest.ToUserId, // Default to admin if no specific user
                Status = RequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _db.SwapRequests.Add(swapRequest);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Swap request {RequestId} submitted successfully for assignment {AssignmentId}", swapRequest.Id, assignment.Id);
            Message = "Shift swap request submitted successfully!";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting swap request");
            Error = "An error occurred while submitting your swap request. Please try again.";
            await OnGetAsync();
            return Page();
        }
    }

    public class TimeOffRequestForm
    {
        [Required]
        [DataType(DataType.Date)]
        public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        [Required]
        [DataType(DataType.Date)]
        public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        [Required]
        public TimeOffType Type { get; set; } = TimeOffType.Vacation;

        [StringLength(500)]
        public string Reason { get; set; } = "";

        public int? ApproverId { get; set; }
    }

    public class SwapRequestForm
    {
        public int ShiftId { get; set; }

        public int ToUserId { get; set; } // 0 for open request
    }

    public class MyTimeOffRequest
    {
        public int Id { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class MySwapRequest
    {
        public int Id { get; set; }
        public DateOnly ShiftDate { get; set; }
        public string ShiftTypeName { get; set; } = "";
        public string ToUserName { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class AvailableShift
    {
        public int ShiftId { get; set; }
        public DateOnly Date { get; set; }
        public string ShiftTypeName { get; set; } = "";
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
    }

    public class ManagerUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
    }
}