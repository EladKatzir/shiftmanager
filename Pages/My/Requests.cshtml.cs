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
public partial class RequestsModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<RequestsModel> _logger;
    private readonly IVacationApprovalService _vacationApprovalService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILeaveFanoutService _leaveFanoutService;

    public RequestsModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<RequestsModel> logger,
        IVacationApprovalService vacationApprovalService,
        IFeatureFlagService featureFlagService,
        ICompanyLocalizationService companyLocalizationService,
        ITenantResolver tenantResolver,
        ILeaveFanoutService leaveFanoutService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _vacationApprovalService = vacationApprovalService;
        _featureFlagService = featureFlagService;
        _companyLocalizationService = companyLocalizationService;
        _tenantResolver = tenantResolver;
        _leaveFanoutService = leaveFanoutService;
    }

    [BindProperty]
    public TimeOffRequestForm TimeOffRequest { get; set; } = new();

    [BindProperty]
    public SwapRequestForm SwapRequest { get; set; } = new();

    public List<MyTimeOffRequest> MyTimeOffRequests { get; set; } = new();
    public List<MySwapRequest> MySwapRequests { get; set; } = new();
    public List<AvailableShift> AvailableShifts { get; set; } = new();
    public List<ApproverOption> AvailableApprovers { get; set; } = new();

    // Message property removed — feedback now flows through TempData → _Layout FeedbackModal bridge.
    // Error property is inherited from LocalizedPageModel; we no longer assign to it.

    public async Task OnGetAsync()
    {
        try
        {
            LogStartingOnGet(_logger);
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                LogInvalidNameIdentifierClaim(_logger);
                TempData["ErrorMessage"] = _localizer["Error_AuthenticationError"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return;
            }
            LogUserId(_logger, userId);

            // ACCOUNT TYPE GATE: Only Standard accounts may access the Requests surface.
            var currentUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.CanAccessRequests())
            {
                TempData["ErrorMessage"] = _localizer["Requests_Locked_AccountType"].Value;
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                Response.Redirect("/Index");
                return;
            }

            // Load user's time off requests
            LogLoadingTimeOff(_logger, userId);
            var rawTimeOffRequests = await _db.TimeOffRequests
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new MyTimeOffRequest
                {
                    Id = r.Id,
                    StartDate = r.StartDate,
                    EndDate = r.EndDate,
                    Reason = r.Reason ?? "",
                    Status = r.Status.ToString(),
                    CreatedAt = r.CreatedAt,
                    LeaveGroupId = r.LeaveGroupId
                })
                .ToListAsync();

            // Epic 6 dedup: when a leave was fanned out across multiple companies, the user
            // should only see ONE row (the canonical copy — lowest Id in the group). Rows
            // with a null LeaveGroupId are each their own logical leave and pass through unchanged.
            MyTimeOffRequests = rawTimeOffRequests
                .GroupBy(r => (object?)r.LeaveGroupId ?? r.Id)
                .Select(g => g.OrderBy(r => r.Id).First())
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            LogLoadedTimeOff(_logger, MyTimeOffRequests.Count, userId);

            // Load user's swap requests - simplified query first
            LogLoadingAssignments(_logger, userId);
            var userAssignmentIds = await _db.ShiftAssignments
                .Where(sa => sa.UserId == userId)
                .Select(sa => sa.Id)
                .ToListAsync();
            LogFoundAssignments(_logger, userAssignmentIds.Count, userId);

            LogLoadingSwapRequests(_logger);
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
            LogLoadedSwapRequests(_logger, MySwapRequests.Count, userId);

            // Load available shifts for swapping (user's upcoming assignments)
            LogLoadingAvailableShifts(_logger, userId);
            var rawShifts = await _db.ShiftAssignments
                .Where(sa => sa.UserId == userId)
                .Join(_db.ShiftInstances,
                    sa => sa.ShiftInstanceId,
                    si => si.Id,
                    (sa, si) => new { Assignment = sa, Instance = si })
                .Join(_db.ShiftTypes,
                    x => x.Instance.ShiftTypeId,
                    st => st.Id,
                    (x, st) => new { x.Assignment, x.Instance, ShiftType = st })
                .Where(x => x.Instance.WorkDate >= DateOnly.FromDateTime(DateTime.Today))
                .OrderBy(x => x.Instance.WorkDate)
                .ToListAsync();

            var companyId = _tenantResolver.GetCurrentTenantId();
            var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
            var shiftTypeNames = new Dictionary<int, string>();
            foreach (var st in rawShifts.Select(x => x.ShiftType).DistinctBy(st => st.Id))
            {
                shiftTypeNames[st.Id] = await _companyLocalizationService.ResolveShiftTypeNameAsync(st, companyId, culture);
            }

            AvailableShifts = rawShifts.Select(x => new AvailableShift
            {
                ShiftId = x.Assignment.Id,
                Date = x.Instance.WorkDate,
                ShiftTypeName = shiftTypeNames.GetValueOrDefault(x.ShiftType.Id, x.ShiftType.Name),
                StartTime = x.ShiftType.Start,
                EndTime = x.ShiftType.End
            }).ToList();
            LogLoadedAvailableShifts(_logger, AvailableShifts.Count, userId);

            // Load available approvers — users who hold ApproveVacations or ApproveExtendedLeave grants
            // scoped to ANY of the current user's shift companies. Extracted to the service so the
            // /Requests/Index manager card can reuse the identical query (see GetGrantBasedApproverOptionsAsync).
            LogLoadingApprovers(_logger, userId);
            AvailableApprovers = await _vacationApprovalService.GetGrantBasedApproverOptionsAsync(userId);
            LogLoadedApprovers(_logger, AvailableApprovers.Count, userId);

            LogOnGetCompleted(_logger, userId);
        }
        catch (Exception ex)
        {
            LogErrorOnGet(_logger, ex);
            TempData["ErrorMessage"] = _localizer["Error_LoadingRequestsFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }
    }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnPostTimeOffAsync()
    {
        try
        {
            LogStartingTimeOffSubmit(_logger);

            // Clear validation errors for other forms (since both models are on the same page)
            ModelState.ClearValidationState(nameof(SwapRequest));

            // For After-duty vacation, EndDate should equal StartDate
            if (TimeOffRequest.Type == TimeOffType.After)
            {
                TimeOffRequest.EndDate = TimeOffRequest.StartDate;
            }

            // "Day at [X]" (Issue 4): a single day at a free-text location. Force single-day and require the label.
            if (TimeOffRequest.Type == TimeOffType.DayAt)
            {
                TimeOffRequest.EndDate = TimeOffRequest.StartDate;
                if (string.IsNullOrWhiteSpace(TimeOffRequest.Label))
                {
                    ModelState.AddModelError("TimeOffRequest.Label", _localizer["DayAt_LabelRequired"]);
                }
            }

            // Custom validation for date range (only for regular vacation)
            if (TimeOffRequest.Type == TimeOffType.Vacation && TimeOffRequest.EndDate < TimeOffRequest.StartDate)
            {
                ModelState.AddModelError("TimeOffRequest.EndDate", _localizer["Error_EndDateBeforeStartDate"]);
                LogValidationEndBeforeStart(_logger, TimeOffRequest.EndDate, TimeOffRequest.StartDate);
            }

            if (!ModelState.IsValid)
            {
                LogTimeOffModelInvalid(_logger, string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                await OnGetAsync();
                return Page();
            }

            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                LogInvalidNameIdentifierClaim(_logger);
                TempData["ErrorMessage"] = _localizer["Error_AuthenticationError"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await OnGetAsync();
                return Page();
            }
            LogTimeOffSubmitContext(_logger, userId, TimeOffRequest.StartDate, TimeOffRequest.EndDate);

            // ACCOUNT TYPE GATE: Only Standard accounts may submit requests.
            var requestingUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
            if (requestingUser == null || !requestingUser.CanAccessRequests())
                return Forbid();

            // Validate approver if specified — must hold ApproveVacations or ApproveExtendedLeave grant
            if (TimeOffRequest.ApproverId.HasValue && TimeOffRequest.ApproverId.Value > 0)
            {
                // Verify the chosen approver is in the requester's eligible pool. This reuses the
                // SAME source of truth as the form dropdown (GetGrantBasedApproverOptionsAsync via
                // IsEligibleApproverAsync), so what is OFFERED is always ACCEPTED — including approvers
                // scoped to a multi-company requester's SECONDARY company (the previous primary-company-
                // only check rejected those even though the dropdown offered them).
                //
                // NOTE: do NOT re-check existence via _db.Users.FindAsync here — that DbSet is
                // tenant-filtered, so a valid CROSS-COMPANY approver (one whose grant covers the
                // requester's molecule/area but who lives in another company) returns null and is
                // wrongly rejected. IsEligibleApproverAsync already subsumes existence + IsActive +
                // valid-grant (pool membership implies all three) and is tenant-independent.
                if (!await _vacationApprovalService.IsEligibleApproverAsync(userId, TimeOffRequest.ApproverId.Value))
                {
                    TempData["ErrorMessage"] = _localizer["Error_InvalidApproverSelected"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
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
                Label = TimeOffRequest.Type == TimeOffType.DayAt ? TimeOffRequest.Label?.Trim() : null, // Issue 4
                Reason = TimeOffRequest.Reason,
                ApproverId = TimeOffRequest.ApproverId > 0 ? TimeOffRequest.ApproverId : null,
                Private = TimeOffRequest.Private,
                Status = RequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            // Epic 6 spec §8: the primary insert and its fan-out clones MUST be atomic. Wrap
            // primary-add + SaveChangesAsync + FanOutAsync in ONE transaction. Inside a
            // transaction SaveChangesAsync assigns request.Id without committing, so FanOutAsync
            // can read it and clone. If any of the primary+fan-out work throws, the transaction
            // rolls back and no orphan single-company primary is left behind.
            IReadOnlyList<TimeOffRequest> fanOutClones;
            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                _db.TimeOffRequests.Add(request);
                await _db.SaveChangesAsync();

                LogTimeOffSubmitted(_logger, request.Id, userId, request.Type, request.ApproverId);

                // Epic 6: fan out to additional shift-companies (no-op for single-company users).
                (_, fanOutClones) = await _leaveFanoutService.FanOutAsync(request, userId);

                await tx.CommitAsync();
            }

            // Submit for approval AFTER the commit, so approval routes are set up on durable rows.
            // For multi-company users, submit each company copy independently so each company's
            // approval chain is seeded correctly.
            if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.VacationApprovalEnabled))
            {
                var (success, approvalMessage) = await _vacationApprovalService.SubmitForApprovalAsync(request.Id, userId);
                LogVacationApprovalResult(_logger, request.Id, success, approvalMessage);

                foreach (var clone in fanOutClones)
                {
                    var (cloneSuccess, cloneApprovalMessage) = await _vacationApprovalService.SubmitForApprovalAsync(clone.Id, userId);
                    LogVacationApprovalResult(_logger, clone.Id, cloneSuccess, cloneApprovalMessage);
                }
            }

            TempData["SuccessMessage"] = _localizer["Success_TimeOffRequestSubmitted"].Value;
            // returnUrl support: if a valid local returnUrl was provided, redirect there after success.
            if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                return Redirect(ReturnUrl);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            LogErrorSubmittingTimeOff(_logger, ex);
            TempData["ErrorMessage"] = _localizer["Error_SubmittingRequestFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSwapAsync()
    {
        try
        {
            LogStartingSwapSubmit(_logger);

            // Clear validation errors for other forms (since both models are on the same page)
            ModelState.ClearValidationState(nameof(TimeOffRequest));

            // Manual validation for swap request since attributes were removed to prevent cross-validation
            if (SwapRequest.ShiftId <= 0)
            {
                ModelState.AddModelError("SwapRequest.ShiftId", _localizer["Error_PleaseSelectShiftToSwap"]);
            }

            if (!ModelState.IsValid)
            {
                LogSwapModelInvalid(_logger, string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                await OnGetAsync();
                return Page();
            }

            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                LogInvalidNameIdentifierClaim(_logger);
                TempData["ErrorMessage"] = _localizer["Error_AuthenticationError"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await OnGetAsync();
                return Page();
            }
            LogSwapSubmitContext(_logger, userId, SwapRequest.ShiftId);

            // ACCOUNT TYPE GATE: Only Standard accounts may submit swap requests.
            var swapRequestingUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
            if (swapRequestingUser == null || !swapRequestingUser.CanAccessRequests())
                return Forbid();

            // Verify the assignment belongs to the user
            var assignment = await _db.ShiftAssignments
                .FirstOrDefaultAsync(sa => sa.Id == SwapRequest.ShiftId && sa.UserId == userId);

            if (assignment == null)
            {
                LogAssignmentNotFound(_logger, SwapRequest.ShiftId, userId);
                TempData["ErrorMessage"] = _localizer["Error_NotAssignedToShift"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await OnGetAsync();
                return Page();
            }

            LogFoundAssignment(_logger, assignment.Id, userId);

            // Load current user to get CompanyId
            var currentUser = await _db.Users.FindAsync(userId);
            if (currentUser == null)
            {
                LogUserNotFound(_logger, userId);
                TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
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

            LogSwapSubmitted(_logger, swapRequest.Id, assignment.Id);
            TempData["SuccessMessage"] = _localizer["Success_SwapRequestSubmitted"].Value;
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            LogErrorSubmittingSwap(_logger, ex);
            TempData["ErrorMessage"] = _localizer["Error_SubmittingSwapRequestFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
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
                LogInvalidNameIdentifierClaim(_logger);
                TempData["ErrorMessage"] = _localizer["Error_AuthenticationError"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

            // ACCOUNT TYPE GATE: Only Standard accounts may cancel requests.
            var cancelUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
            if (cancelUser == null || !cancelUser.CanAccessRequests())
                return Forbid();

            // Verify request belongs to current user and is still Pending
            var request = await _db.TimeOffRequests
                .FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == userId);

            if (request == null)
            {
                LogCancelTimeOffNotFound(_logger, requestId, userId);
                TempData["ErrorMessage"] = _localizer["Error_RequestNotFound"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

            if (request.Status != RequestStatus.Pending)
            {
                LogCancelTimeOffNotPending(_logger, requestId, request.Status);
                TempData["ErrorMessage"] = _localizer["Error_RequestAlreadyProcessed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

            var (success, message) = await _vacationApprovalService.CancelRequestAsync(requestId, userId);

            if (success)
            {
                LogTimeOffCanceled(_logger, requestId, userId);
                TempData["SuccessMessage"] = _localizer["RequestCanceled"].Value;
            }
            else
            {
                LogFailedCancelTimeOff(_logger, requestId, message);
                TempData["ErrorMessage"] = _localizer["Error_CancelRequestFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            }

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            LogErrorCancelTimeOff(_logger, ex, requestId);
            TempData["ErrorMessage"] = _localizer["Error_CancelRequestFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
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

        /// <summary>Free-text location for a "Day at [X]" request (Type == DayAt). Issue 4.</summary>
        [StringLength(50)]
        public string? Label { get; set; }

        [StringLength(500)]
        public string Reason { get; set; } = "";

        public int? ApproverId { get; set; }

        public bool Private { get; set; } = false;
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
        /// <summary>Used for fan-out dedup: rows sharing a non-null LeaveGroupId represent one
        /// logical leave and must not appear as separate entries in the filer's list.</summary>
        public Guid? LeaveGroupId { get; set; }
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

}