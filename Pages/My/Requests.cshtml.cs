using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Data.SeedData;
using ShiftManager.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ShiftManager.Pages.My;

[Authorize]
public class RequestsModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<RequestsModel> _logger;
    private readonly IVacationApprovalService _vacationApprovalService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IGrantService _grantService;
    public RequestsModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<RequestsModel> logger,
        IVacationApprovalService vacationApprovalService,
        IFeatureFlagService featureFlagService,
        IGrantService grantService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _vacationApprovalService = vacationApprovalService;
        _featureFlagService = featureFlagService;
        _grantService = grantService;
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
                Error = _localizer["Error_AuthenticationError"];
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

            // Load available approvers — users who hold ApproveVacations or ApproveExtendedLeave grants
            // scoped to the current user's company
            _logger.LogInformation("Loading available approvers for user {UserId}", userId);
            var currentUser = await _db.Users.FindAsync(userId);
            if (currentUser != null)
            {
                // Phase 2: Query grant table for users with approval grants covering this company
                // Resolve the company's position in the hierarchy for scope matching
                var approvalGrantKeys = new[] { "ApproveVacations", "ApproveExtendedLeave" };
                var company = await _db.Companies.FindAsync(currentUser.CompanyId);
                int? companyMoleculeId = company?.MoleculeId;

                var approverUserIds = await _db.Grants
                    .Where(g => _db.GrantTypes
                        .Where(gt => approvalGrantKeys.Contains(gt.Key))
                        .Select(gt => gt.Id)
                        .Contains(g.GrantTypeId))
                    .Where(g => g.CanOwn)
                    // Match grants whose scope actually covers this user's company hierarchy
                    .Where(g => g.CompanyId == currentUser.CompanyId
                             // Molecule-scoped: grant's molecule must contain this company
                             || (g.MoleculeId != null && g.MoleculeId == companyMoleculeId)
                             // Area-scoped: grant's area must contain this company's molecule
                             || (g.AreaId != null && companyMoleculeId != null
                                 && _db.Molecules.Any(m => m.Id == companyMoleculeId && m.AreaId == g.AreaId))
                             // Project-scoped: grant's project must contain this company's area
                             || (g.ProjectId != null && companyMoleculeId != null
                                 && _db.Molecules.Any(m => m.Id == companyMoleculeId
                                        && _db.Areas.Any(a => a.Id == m.AreaId && a.ProjectId == g.ProjectId)))
                             // Self-scoped (all nulls): approver must be in the same company
                             || (!g.CompanyId.HasValue && !g.MoleculeId.HasValue
                                 && !g.AreaId.HasValue && !g.ProjectId.HasValue
                                 && _db.Users.Any(u => u.Id == g.UserId && u.CompanyId == currentUser.CompanyId)))
                    .Select(g => g.UserId)
                    .Distinct()
                    .ToListAsync();

                AvailableApprovers = await _db.Users
                    .Where(u => approverUserIds.Contains(u.Id) && u.IsActive)
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
            Error = _localizer["Error_LoadingRequestsFailed"];
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
                ModelState.AddModelError("TimeOffRequest.EndDate", _localizer["Error_EndDateBeforeStartDate"]);
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
                Error = _localizer["Error_AuthenticationError"];
                await OnGetAsync();
                return Page();
            }
            _logger.LogInformation("Time off request for user {UserId}, dates {StartDate} to {EndDate}", userId, TimeOffRequest.StartDate, TimeOffRequest.EndDate);

            // Validate approver if specified — must hold ApproveVacations or ApproveExtendedLeave grant
            if (TimeOffRequest.ApproverId.HasValue && TimeOffRequest.ApproverId.Value > 0)
            {
                var approver = await _db.Users.FindAsync(TimeOffRequest.ApproverId.Value);
                if (approver == null || !approver.IsActive)
                {
                    Error = _localizer["Error_InvalidApproverSelected"];
                    await OnGetAsync();
                    return Page();
                }

                // Phase 2: Verify approver holds an approval grant scoped to THIS user's company+jobtype
                var requestingUser = await _db.Users.FindAsync(userId);
                bool hasApproveVacations = await _grantService.HasGrantWithScopeAsync(
                    TimeOffRequest.ApproverId.Value, "ApproveVacations",
                    companyId: requestingUser?.CompanyId, jobTypeId: requestingUser?.JobTypeId);
                bool hasApproveExtendedLeave = await _grantService.HasGrantWithScopeAsync(
                    TimeOffRequest.ApproverId.Value, "ApproveExtendedLeave",
                    companyId: requestingUser?.CompanyId, jobTypeId: requestingUser?.JobTypeId);
                if (!hasApproveVacations && !hasApproveExtendedLeave)
                {
                    Error = _localizer["Error_InvalidApproverSelected"];
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

            // Submit for approval if the vacation approval feature flag is enabled
            if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.VacationApprovalEnabled))
            {
                var (success, approvalMessage) = await _vacationApprovalService.SubmitForApprovalAsync(request.Id, userId);
                _logger.LogInformation("Vacation approval result for request {RequestId}: Success={Success}, Message={Message}",
                    request.Id, success, approvalMessage);
            }

            Message = _localizer["Success_TimeOffRequestSubmitted"];
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting time off request");
            Error = _localizer["Error_SubmittingRequestFailed"];
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
                ModelState.AddModelError("SwapRequest.ShiftId", _localizer["Error_PleaseSelectShiftToSwap"]);
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
                Error = _localizer["Error_AuthenticationError"];
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
                Error = _localizer["Error_NotAssignedToShift"];
                await OnGetAsync();
                return Page();
            }

            _logger.LogInformation("Found assignment {AssignmentId} for user {UserId}", assignment.Id, userId);

            // Load current user to get CompanyId
            var currentUser = await _db.Users.FindAsync(userId);
            if (currentUser == null)
            {
                _logger.LogError("User {UserId} not found", userId);
                Error = _localizer["Error_UserNotFound"];
                await OnGetAsync();
                return Page();
            }

            var swapRequest = new SwapRequest
            {
                FromAssignmentId = assignment.Id,
                FromUserId = userId,
                CompanyId = currentUser.CompanyId,
                ToUserId = SwapRequest.ToUserId > 0 ? SwapRequest.ToUserId : null,
                Status = RequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _db.SwapRequests.Add(swapRequest);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Swap request {RequestId} submitted successfully for assignment {AssignmentId}", swapRequest.Id, assignment.Id);
            Message = _localizer["Success_SwapRequestSubmitted"];
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting swap request");
            Error = _localizer["Error_SubmittingSwapRequestFailed"];
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCancelRequestAsync(int requestId)
    {
        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                Error = _localizer["Error_AuthenticationError"];
                return RedirectToPage();
            }

            // Verify request belongs to current user and is still Pending
            var request = await _db.TimeOffRequests
                .FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == userId);

            if (request == null)
            {
                _logger.LogWarning("Cancel request: TimeOffRequest {RequestId} not found for user {UserId}", requestId, userId);
                Error = _localizer["Error_RequestNotFound"];
                return RedirectToPage();
            }

            if (request.Status != RequestStatus.Pending)
            {
                _logger.LogWarning("Cancel request: TimeOffRequest {RequestId} is not pending (status={Status})", requestId, request.Status);
                Error = _localizer["Error_RequestAlreadyProcessed"];
                return RedirectToPage();
            }

            var (success, message) = await _vacationApprovalService.CancelRequestAsync(requestId, userId);

            if (success)
            {
                _logger.LogInformation("TimeOffRequest {RequestId} canceled by user {UserId}", requestId, userId);
                Message = _localizer["RequestCanceled"];
            }
            else
            {
                _logger.LogWarning("Failed to cancel TimeOffRequest {RequestId}: {Message}", requestId, message);
                Error = _localizer["Error_CancelRequestFailed"];
            }

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling time off request {RequestId}", requestId);
            Error = _localizer["Error_CancelRequestFailed"];
            return RedirectToPage();
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