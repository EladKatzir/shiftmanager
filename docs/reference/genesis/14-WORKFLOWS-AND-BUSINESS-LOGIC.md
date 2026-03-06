# 14. Workflows and Business Logic

**Document Version:** 1.0
**Last Updated:** December 2025
**Part of:** PROJECT COSMOGENESIS - ShiftManager Genesis Documentation

---

## Table of Contents

1. [Overview](#overview)
2. [Shift Assignment Workflow](#shift-assignment-workflow)
3. [Time-Off Request Workflow](#time-off-request-workflow)
4. [Shift Swap Request Workflow](#shift-swap-request-workflow)
5. [Chore Assignment Workflow](#chore-assignment-workflow)
6. [On-Duty Assignment Workflow](#on-duty-assignment-workflow)
7. [Conflict Detection System](#conflict-detection-system)
8. [Notification System](#notification-system)
9. [Daily Digest Workflow](#daily-digest-workflow)
10. [Authorization & Access Control](#authorization--access-control)
11. [Business Rules Summary](#business-rules-summary)

---

## Overview

ShiftManager implements **7 major workflows** that power workforce management for multi-tenant organizations. Each workflow enforces specific business rules, validates conflicts, and triggers notifications to affected users.

### Workflow Categories

| Workflow | User-Initiated | Manager-Initiated | Approval Required | Notifications |
|----------|----------------|-------------------|-------------------|---------------|
| **Shift Assignment** | No | Yes | No | Yes (on assign/remove) |
| **Time-Off Request** | Yes | No | Yes (Manager+) | Yes (on approve/decline) |
| **Swap Request** | Yes | No | Yes (Manager+) | Yes (on approve/decline) |
| **Chore Assignment** | No | Yes (Manager+) | No | Yes (on assign/cancel) |
| **On-Duty Assignment** | No | Yes (Manager+) | No | Yes (on assign/cancel) |
| **Trainee Shadowing** | No | Yes (Manager+) | No | Yes (on assign/cancel) |
| **Daily Digest** | System | System | No | Yes (scheduled email) |

### Core Services

**Services/ConflictChecker.cs** (127 lines)
- Validates all shift assignments against business rules
- Checks: Time-off, overlaps, rest periods, weekly caps

**Services/NotificationService.cs** (808 lines)
- Creates in-app notifications and email alerts
- Handles daily digests and day-before reminders

**Services/ChoreService.cs** (640 lines)
- Manages chore assignments (company-scoped tasks)

**Services/OnDutyService.cs** (448 lines)
- Manages on-duty assignments (global day shifts)

---

## Shift Assignment Workflow

**Purpose:** Managers assign employees to scheduled shifts (Morning, Evening, Night, etc.) for specific dates.

**Page:** `Pages/Assignments/Manage.cshtml.cs` (300+ lines)

### Workflow Steps

```
1. Manager navigates to shift management (/Assignments/Manage?date=2025-06-15&shiftTypeId=3)
   ↓
2. System loads:
   - Shift type (Morning/Evening/Night)
   - Shift instance for date (or creates if doesn't exist)
   - Current assignments
   - Active users (company-filtered)
   - Users on approved time-off (warning badges)
   - Busy users (shift conflicts, chores, vacations)
   ↓
3. Manager selects user from dropdown
   ↓
4. System validates:
   a. User belongs to shift's company (multi-tenancy check)
   b. Staffing not exceeded (assigned < required)
   c. ConflictChecker.CanAssignAsync():
      - No approved time-off on date
      - No overlapping shifts
      - Sufficient rest period (8h default) from previous/next shift
      - Weekly hours cap not exceeded (40h default)
   ↓
5. Validation passes → Create ShiftAssignment
   ↓
6. Send notification to assigned user
   ↓
7. Redirect back to management page
```

### Implementation

**Pages/Assignments/Manage.cshtml.cs:175-246**

```csharp
public async Task<IActionResult> OnPostAsync()
{
    await OnGetAsync(); // reload context for Instance
    _logger.LogInformation("Attempt add user {UserId} to shiftInstance {InstanceId}", SelectedUserId, Instance.Id);

    if (SelectedUserId is null) { Error = "Select a user."; return Page(); }

    // Validate user belongs to the shift's company
    var selectedUser = await _db.Users.FindAsync(SelectedUserId.Value);
    if (selectedUser == null)
    {
        Error = "Selected user not found.";
        return Page();
    }

    if (selectedUser.CompanyId != Instance.CompanyId)
    {
        _logger.LogWarning("Attempted cross-company assignment: User {UserId} (Company {UserCompanyId}) to Shift (Company {ShiftCompanyId})",
            SelectedUserId.Value, selectedUser.CompanyId, Instance.CompanyId);
        Error = "Cannot assign user from a different company to this shift.";
        return Page();
    }

    int assigned = await _db.ShiftAssignments
        .IgnoreQueryFilters()
        .CountAsync(a => a.ShiftInstanceId == Instance.Id);
    if (assigned >= Instance.StaffingRequired)
    {
        Error = "Cannot over-assign. Increase required first.";
        return Page();
    }

    var conflict = await _checker.CanAssignAsync(SelectedUserId.Value, Instance);
    if (!conflict.Allowed)
    {
        _logger.LogWarning("Conflict add user {UserId} to shiftInstance {InstanceId}: {Reasons}",
                   SelectedUserId, Instance.Id, string.Join("; ", conflict.Reasons));
        Error = string.Join(" ", conflict.Reasons);
        return Page();
    }

    _db.ShiftAssignments.Add(new ShiftAssignment
    {
        ShiftInstanceId = Instance.Id,
        UserId = SelectedUserId.Value,
        CompanyId = Instance.CompanyId
    });

    // Set shift name if provided and this is the first assignment
    if (assigned == 0)
    {
        if (!string.IsNullOrWhiteSpace(ShiftName))
        {
            Instance.Name = ShiftName.Trim();
        }
    }

    await _db.SaveChangesAsync();

    // Send notification to the assigned user
    await _notificationService.CreateShiftAddedNotificationAsync(
        SelectedUserId.Value,
        Type!.Name,
        Date,
        Type.Start,
        Type.End
    );

    return RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId, returnUrl = ReturnUrl });
}
```

### Removal Workflow

**Pages/Assignments/Manage.cshtml.cs:248-281**

```csharp
public async Task<IActionResult> OnPostRemoveAsync(int assignmentId)
{
    var a = await _db.ShiftAssignments
        .Include(sa => sa.ShiftInstance)
        .ThenInclude(si => si.ShiftType)
        .FirstOrDefaultAsync(sa => sa.Id == assignmentId);

    if (a != null)
    {
        _logger.LogInformation("Removing assignment {AssignmentId} from shiftInstance {InstanceId}", assignmentId, a.ShiftInstanceId);

        // Send notification before removing (only if user is assigned)
        if (a.UserId.HasValue)
        {
            await _notificationService.CreateShiftRemovedNotificationAsync(
                a.UserId.Value,
                a.ShiftInstance.ShiftType.Name,
                a.ShiftInstance.WorkDate,
                a.ShiftInstance.ShiftType.Start,
                a.ShiftInstance.ShiftType.End
            );
        }

        _db.ShiftAssignments.Remove(a);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Successfully removed assignment {AssignmentId}", assignmentId);
    }

    return RedirectToPage(new { date = Date, shiftTypeId = ShiftTypeId });
}
```

### Business Rules

1. **Multi-Tenancy:** Users can only be assigned to shifts in their own company
2. **Staffing Limits:** Cannot assign more users than `StaffingRequired` count
3. **Conflict Detection:** Must pass all ConflictChecker validations
4. **Notifications:** Always send email/in-app notification on assign/remove
5. **Shift Naming:** First assignment can set custom shift name (optional)

---

## Time-Off Request Workflow

**Purpose:** Employees request vacation days or partial time-off ("After" shifts). Managers approve/decline requests.

**Models:** `Models/TimeOffRequest.cs` (57 lines)
**Create Page:** `Pages/Requests/TimeOff/Create.cshtml.cs` (98 lines)
**Approval Page:** `Pages/Requests/Index.cshtml.cs` (519 lines)

### Workflow Steps

```
STEP 1: Employee Creates Request
Employee → /Requests/TimeOff/Create
  ↓
  Select dates (StartDate, EndDate)
  Select type (Vacation = full days, After = 16:00 on single day)
  Enter optional reason
  ↓
  Validation:
  - EndDate >= StartDate
  - Dates not in past
  - Dates not >2 years in future
  - Vacation duration <= 365 days
  - Reason <= 1000 characters
  ↓
  Create TimeOffRequest (Status = Pending)
  ↓
  Redirect to /Requests/Index (user sees pending request)

STEP 2: Manager Approves/Declines
Manager → /Requests/Index (sees all pending requests for their company)
  ↓
  [Approve] → OnPostApproveTimeOffAsync()
     ↓
     Validation:
     - Manager has access to request's company (Owner/Director/Manager only)
     ↓
     Set Status = Approved
     ↓
     Delete conflicting shift assignments in date range
     ↓
     Cancel trainee shadowing assignments in date range
     ↓
     Send notification to employee
  OR
  [Decline] → OnPostDeclineTimeOffAsync()
     ↓
     Validation:
     - Manager has access to request's company
     ↓
     Set Status = Declined
     ↓
     Send notification to employee
```

### Implementation: Create Request

**Pages/Requests/TimeOff/Create.cshtml.cs:30-96**

```csharp
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
```

### Implementation: Approve Request

**Pages/Requests/Index.cshtml.cs:147-211**

```csharp
public async Task<IActionResult> OnPostApproveTimeOffAsync(int id)
{
    // ✅ SECURITY FIX: Input validation
    if (id <= 0)
    {
        _logger.LogWarning("Invalid time off request ID: {Id}", id);
        return RedirectToPage();
    }

    // ✅ SECURITY FIX: Validate authorization before approving request
    var r = await _db.TimeOffRequests.FindAsync(id);
    if (r == null)
    {
        _logger.LogWarning("Time off request {RequestId} not found", id);
        return RedirectToPage();
    }

    // Get current user and validate they have access to this request's company
    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(userIdClaim, out var currentUserId))
    {
        _logger.LogError("Invalid or missing NameIdentifier claim");
        Error = _localizer["Error_AuthenticationError"];
        return RedirectToPage();
    }

    var currentUser = await _db.Users.FindAsync(currentUserId);
    var hasAccess = await ValidateAccessToRequestAsync(currentUser!, r.CompanyId);
    if (!hasAccess)
    {
        _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to approve time off request {RequestId} for unauthorized company {CompanyId}",
            currentUserId, currentUser!.Role, id, r.CompanyId);
        Error = _localizer["Error_NoPermissionApproveRequest"];
        await OnGetAsync();
        return Page();
    }

    r.Status = RequestStatus.Approved;

    // Remove existing assignments in the approved window
    var assignments = await (from a in _db.ShiftAssignments
                             join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                             where a.UserId == r.UserId && si.WorkDate >= r.StartDate && si.WorkDate <= r.EndDate
                             select a).ToListAsync();
    if (assignments.Any())
    {
        _db.ShiftAssignments.RemoveRange(assignments);
    }

    // Cancel trainee shadowing assignments if user is a trainee
    var user = await _db.Users.FindAsync(r.UserId);
    if (user != null && user.Role == UserRole.Trainee)
    {
        var startDate = r.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDate = r.EndDate.ToDateTime(TimeOnly.MaxValue);
        await _traineeService.CancelShadowingForTimeOffAsync(r.UserId, startDate, endDate);
    }

    await _db.SaveChangesAsync();

    // Send notification to user
    await _notificationService.CreateTimeOffNotificationAsync(r.UserId, RequestStatus.Approved, r.StartDate, r.EndDate, r.Id);

    return RedirectToPage();
}
```

### Time-Off Types

**Models/TimeOffRequest.cs:28-55**

```csharp
/// <summary>
/// Get the actual start date and time when the time-off begins.
/// Vacation: StartDate at 00:00
/// After: StartDate at 16:00
/// </summary>
public DateTime GetActualStartDateTime()
{
    return Type switch
    {
        TimeOffType.Vacation => StartDate.ToDateTime(TimeOnly.MinValue),
        TimeOffType.After => StartDate.ToDateTime(new TimeOnly(16, 0)),
        _ => StartDate.ToDateTime(TimeOnly.MinValue)
    };
}

/// <summary>
/// Get the actual end date and time when the time-off ends.
/// Vacation: EndDate+1 at 13:00
/// After: StartDate+1 at 13:00
/// </summary>
public DateTime GetActualEndDateTime()
{
    return Type switch
    {
        TimeOffType.Vacation => EndDate.AddDays(1).ToDateTime(new TimeOnly(13, 0)),
        TimeOffType.After => StartDate.AddDays(1).ToDateTime(new TimeOnly(13, 0)),
        _ => EndDate.ToDateTime(new TimeOnly(23, 59, 59))
    };
}
```

### Business Rules

1. **Validation:**
   - Dates must be in future (not past)
   - Dates must be ≤2 years in future (prevent abuse)
   - Vacation duration ≤365 days
   - Reason ≤1000 characters

2. **Authorization:**
   - Only Owner/Director/Manager can approve requests
   - Directors can only approve for companies they manage
   - Managers can only approve for their own company

3. **Side Effects on Approval:**
   - Delete all shift assignments in time-off date range
   - Cancel trainee shadowing assignments
   - Send notification to employee

4. **Deletion:**
   - Approved time-off can be deleted by managers
   - Cannot delete if time-off has already started (StartDate <= today)

---

## Shift Swap Request Workflow

**Purpose:** Employees request to swap their assigned shift with another employee. Managers approve/decline swaps.

**Models:** `Models/SwapRequest.cs` (73 lines)
**Create Page:** `Pages/Requests/Swaps/Create.cshtml.cs` (136 lines)
**Approval Page:** `Pages/Requests/Index.cshtml.cs` (same as time-off)

### Workflow Steps

```
STEP 1: Employee Creates Swap Request
Employee → /Requests/Swaps/Create
  ↓
  Select shift from "My Upcoming Assignments" dropdown
  Select target user to swap with
  Enter optional reason
  ↓
  Validation:
  - Assignment belongs to requesting user (authorization check)
  - Target user is in same company
  - Target user is active
  - Cannot swap with self
  - Trainees cannot create swap requests
  ↓
  Create SwapRequest (Status = Pending)
     FromAssignmentId = selected assignment
     ToUserId = target user
     FromUserId = current user (inferred from FromAssignment)
  ↓
  Redirect to /Requests/Index

STEP 2: Manager Approves/Declines
Manager → /Requests/Index (sees all pending swap requests)
  ↓
  [Approve] → OnPostApproveSwapAsync()
     ↓
     Validation:
     - Manager has access to request's company
     - FromAssignment still exists
     - ShiftInstance still exists
     - ToUser is valid
     ↓
     Conflict check: ConflictChecker.CanAssignAsync(ToUserId, ShiftInstance)
       - No approved time-off
       - No overlapping shifts
       - Sufficient rest period
       - Weekly hours cap not exceeded
     ↓
     IF conflict → decline swap, show error
     ↓
     IF no conflict:
       - Reassign ShiftAssignment.UserId = ToUserId (swap shift to target user)
       - Set SwapRequest.Status = Approved
       - Send notification to original user
  OR
  [Decline] → OnPostDeclineSwapAsync()
     ↓
     Set Status = Declined
     ↓
     Send notification to requester
```

### Implementation: Create Swap Request

**Pages/Requests/Swaps/Create.cshtml.cs:62-134**

```csharp
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
```

### Implementation: Approve Swap

**Pages/Requests/Index.cshtml.cs:259-337**

```csharp
public async Task<IActionResult> OnPostApproveSwapAsync(int id)
{
    // ✅ SECURITY FIX: Input validation
    if (id <= 0)
    {
        _logger.LogWarning("Invalid swap request ID: {Id}", id);
        return RedirectToPage();
    }

    using var trx = await _db.Database.BeginTransactionAsync();

    // ✅ SECURITY FIX: Validate authorization before approving swap
    var s = await _db.SwapRequests.FindAsync(id);
    if (s == null)
    {
        _logger.LogWarning("Swap request {RequestId} not found", id);
        return RedirectToPage();
    }

    // Get current user and validate they have access to this request's company
    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(userIdClaim, out var currentUserId))
    {
        _logger.LogError("Invalid or missing NameIdentifier claim");
        Error = _localizer["Error_AuthenticationError"];
        await trx.RollbackAsync();
        return RedirectToPage();
    }

    var currentUser = await _db.Users.FindAsync(currentUserId);
    var hasAccess = await ValidateAccessToRequestAsync(currentUser!, s.CompanyId);
    if (!hasAccess)
    {
        _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to approve swap request {RequestId} for unauthorized company {CompanyId}",
            currentUserId, currentUser!.Role, id, s.CompanyId);
        Error = _localizer["Error_NoPermissionApproveRequest"];
        await trx.RollbackAsync();
        await OnGetAsync();
        return Page();
    }

    var assign = await _db.ShiftAssignments.FindAsync(s.FromAssignmentId);
    if (assign == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

    var si = await _db.ShiftInstances.FindAsync(assign.ShiftInstanceId);
    if (si == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

    var shiftType = await _db.ShiftTypes.FindAsync(si.ShiftTypeId);
    if (shiftType == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

    if (!s.ToUserId.HasValue) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

    var conflict = await _checker.CanAssignAsync(s.ToUserId.Value, si);
    if (!conflict.Allowed)
    {
        Error = _localizer["Error_CannotApproveSwap"] + ": " + string.Join(" ", conflict.Reasons);
        await trx.RollbackAsync();
        await OnGetAsync();
        return Page();
    }

    // Get original user for notification
    var originalUserId = assign.UserId;

    // Reassign
    assign.UserId = s.ToUserId;
    s.Status = RequestStatus.Approved;
    await _db.SaveChangesAsync();
    await trx.CommitAsync();

    // Send notification to original user (if there was one)
    if (originalUserId.HasValue)
    {
        var shiftInfo = $"{shiftType.Name} on {si.WorkDate:MMM dd, yyyy} ({shiftType.Start:HH:mm} - {shiftType.End:HH:mm})";
        await _notificationService.CreateSwapRequestNotificationAsync(originalUserId.Value, RequestStatus.Approved, shiftInfo, s.Id);
    }

    return RedirectToPage();
}
```

### Business Rules

1. **Authorization:**
   - Trainees cannot create swap requests (blocked explicitly)
   - Employee can only swap their own shifts
   - Target user must be in same company

2. **Validation on Creation:**
   - Assignment belongs to requesting user
   - Cannot swap with self
   - Target user is active

3. **Validation on Approval:**
   - Full ConflictChecker validation for target user
   - If conflict detected → decline swap automatically
   - Transaction-based (rollback on error)

4. **Notifications:**
   - Send notification to original shift owner when swap approved/declined

---

## Chore Assignment Workflow

**Purpose:** Managers assign one-off tasks ("chores") to employees for specific dates. Chores can replace shift assignments.

**Service:** `Services/ChoreService.cs` (640 lines)

### Workflow Steps

```
STEP 1: Manager Creates Chore
Manager → /Chores/QuickAdd (or similar page)
  ↓
  Select assignee user
  Select date
  Enter title (required)
  Enter optional notes
  ↓
  ChoreService.CreateChoreAsync()
     ↓
     Authorization check:
     - Manager/Director/Owner/Assigner can create chores
     - Can assign to employees in accessible companies
     - Directors CANNOT be assigned chores
     ↓
     Conflict checks:
     - User doesn't already have chore on date
     - User doesn't have shift on date (if yes → show "SHIFT_CONFLICT")
     - User doesn't have approved vacation on date (if yes → show "VACATION_CONFLICT")
     ↓
     IF force-assign flag is true → bypass vacation conflict
     ↓
     Create Chore record
     ↓
     Send notification to assignee
     ↓
     Audit log (if force-assigned)

STEP 2: Manager Cancels Chore (Soft Delete)
Manager → /Chores page → Click cancel
  ↓
  ChoreService.CancelChoreAsync()
     ↓
     Authorization check:
     - Manager/Director/Owner can cancel
     - Directors can only cancel for companies they manage
     - Managers can only cancel for their own company
     ↓
     Set CanceledAt = NOW, CanceledBy = currentUserId
     ↓
     Send notification to assignee

STEP 3: Replace Shift with Chore (Special Operation)
Manager → Shift assignment page → "Replace with Chore"
  ↓
  ChoreService.ReplaceShiftWithChoreAsync()
     ↓
     Transaction begins
     ↓
     Delete ShiftAssignment
     ↓
     Create Chore (same date, same user)
     ↓
     Transaction commit
     ↓
     Send notification
```

### Implementation: Create Chore

**Services/ChoreService.cs:243-351**

```csharp
public async Task<(bool Success, string Message, Chore? Chore)> CreateChoreAsync(
    int assigneeId,
    DateOnly date,
    string title,
    string? notes = null,
    bool forceAssign = false)
{
    var currentUserId = GetCurrentUserId();
    int? companyId = null;
    try
    {
        var currentUser = await GetCurrentUserAsync();
        companyId = currentUser?.CompanyId;

        if (currentUser == null)
        {
            return (false, "User not authenticated.", null);
        }

        // Check if current user can manage chores
        if (!await CanUserManageChoresAsync(currentUserId))
        {
            return (false, "You do not have permission to create chores.", null);
        }

        // Get assignee
        var assignee = await _db.Users.FindAsync(assigneeId);
        if (assignee == null)
        {
            return (false, "Assignee not found.", null);
        }

        // Check if assignee is eligible (not a Director, etc.)
        if (!await CanUserManageChoreForAssigneeAsync(currentUserId, assigneeId))
        {
            return (false, "You cannot assign chores to this user.", null);
        }

        // Check if assignee already has an active chore on this date
        if (await HasActiveChoreOnDateAsync(assigneeId, date))
        {
            return (false, "This user already has an active chore on this date.", null);
        }

        // Check if assignee has a shift on this date (warning, not blocking)
        if (await HasShiftOnDateAsync(assigneeId, date))
        {
            return (false, "SHIFT_CONFLICT", null); // Special message for UI to handle
        }

        // COLLISION RULE: Check for vacation conflict (unless force-assigning)
        if (!forceAssign)
        {
            var (hasConflict, vacationStart, vacationEnd, vacationType) = await GetVacationConflictDetailsAsync(assigneeId, date);
            if (hasConflict)
            {
                // Return vacation details for UI to display in confirmation dialog
                return (false, $"VACATION_CONFLICT|{vacationStart}|{vacationEnd}|{vacationType}", null);
            }
        }

        // Validate title
        if (string.IsNullOrWhiteSpace(title))
        {
            return (false, "Chore title is required.", null);
        }

        // Create the chore
        var chore = new Chore
        {
            CompanyId = assignee.CompanyId, // Use assignee's company ID for multi-tenant support
            UserId = assigneeId,
            Date = date,
            Title = title.Trim(),
            Notes = notes?.Trim(),
            CreatedBy = currentUserId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Chores.Add(chore);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Chore {ChoreId} created by user {CreatedBy} for user {UserId} on {Date}",
            chore.Id, currentUserId, assigneeId, date);

        // Audit log for force-assignments (bypassing vacation conflict)
        if (forceAssign)
        {
            _logger.LogWarning("Chore {ChoreId} was FORCE-ASSIGNED by user {CreatedBy} despite vacation conflict for user {UserId} on {Date}",
                chore.Id, currentUserId, assigneeId, date);
        }

        return (true, "Chore created successfully.", chore);
    }
    catch (DbUpdateException ex)
    {
        _logger.LogError(ex, "Database error creating chore. CompanyId={CompanyId}, CreatedBy={CreatedBy}, AssigneeId={AssigneeId}, Date={Date}, Title={Title}",
            companyId, currentUserId, assigneeId, date, title);
        return (false, "An error occurred while creating the chore.", null);
    }
}
```

### Vacation Conflict Detection

**Services/ChoreService.cs:199-238**

```csharp
/// <summary>
/// Check if user has an approved vacation that overlaps with the given date
/// COLLISION RULE: Chore assignments cannot overlap with approved vacations
/// </summary>
public async Task<bool> HasVacationConflictAsync(int userId, DateOnly date)
{
    // Get user to find their company (needed for vacation query)
    var user = await _db.Users.FindAsync(userId);
    if (user == null) return false;

    // Check for approved time off requests that include this date
    var hasConflict = await _db.TimeOffRequests
        .AnyAsync(t => t.UserId == userId &&
                      t.CompanyId == user.CompanyId &&
                      t.Status == RequestStatus.Approved &&
                      t.StartDate <= date &&
                      t.EndDate >= date);

    return hasConflict;
}

/// <summary>
/// Get vacation details if user has an approved vacation that overlaps with the given date
/// Returns (hasConflict, startDate, endDate, type)
/// </summary>
public async Task<(bool HasConflict, DateOnly? StartDate, DateOnly? EndDate, TimeOffType? Type)>
    GetVacationConflictDetailsAsync(int userId, DateOnly date)
{
    var user = await _db.Users.FindAsync(userId);
    if (user == null) return (false, null, null, null);

    var vacation = await _db.TimeOffRequests
        .Where(t => t.UserId == userId &&
                   t.CompanyId == user.CompanyId &&
                   t.Status == RequestStatus.Approved &&
                   t.StartDate <= date &&
                   t.EndDate >= date)
        .FirstOrDefaultAsync();

    if (vacation == null)
        return (false, null, null, null);

    return (true, vacation.StartDate, vacation.EndDate, vacation.Type);
}
```

### Business Rules

1. **Authorization:**
   - Manager/Director/Owner/Assigner can create chores
   - Directors cannot be assigned chores (business rule)
   - Directors can assign across companies they manage

2. **Conflict Detection:**
   - One chore per user per date (hard limit)
   - Shift conflict → return special "SHIFT_CONFLICT" message (UI handles)
   - Vacation conflict → return special "VACATION_CONFLICT|..." message (UI handles)

3. **Force Assignment:**
   - `forceAssign=true` flag bypasses vacation conflict check
   - Audit logged when force-assigned

4. **Multi-Tenancy:**
   - Chores are company-scoped (unlike OnDuty which is global)
   - Uses assignee's CompanyId

---

## On-Duty Assignment Workflow

**Purpose:** Managers assign global "day shifts" (Hakam, Lead, custom types) that span entire days across all companies.

**Service:** `Services/OnDutyService.cs` (448 lines)

### Key Differences from Chores

| Aspect | Chores | On-Duty |
|--------|--------|---------|
| **Scope** | Company-scoped (tenant-isolated) | Global (cross-company) |
| **Who can be assigned** | Employees, Managers (NOT Directors) | Employees, Managers, Directors (anyone) |
| **Who can create** | Manager+, Assigner | Manager+, Director+ (NOT Assigner) |
| **Database scoping** | Uses CompanyId column | No CompanyId (global table) |

### Workflow Steps

```
STEP 1: Manager Creates On-Duty Assignment
Manager → /OnDuty/Create (or similar)
  ↓
  Select assignee user (from all accessible companies)
  Select date
  Select type (Hakam, Lead, or custom type)
  Enter optional notes
  ↓
  OnDutyService.CreateOnDutyAsync()
     ↓
     Authorization check:
     - Manager/Director/Owner can create on-duty (NOT Assigner)
     - Directors can assign to users in companies they manage
     - Managers can only assign to users in their own company
     ↓
     Conflict checks:
     - User doesn't already have on-duty of same type on date
     - User doesn't have approved vacation on date (if yes → "VACATION_CONFLICT")
     ↓
     IF force-assign flag is true → bypass vacation conflict
     ↓
     Create OnDuty record (no CompanyId - global)
     ↓
     Send notification to assignee

STEP 2: Manager Cancels On-Duty
Manager → /OnDuty page → Click cancel
  ↓
  OnDutyService.CancelOnDutyAsync()
     ↓
     Authorization check:
     - Manager/Director/Owner can cancel
     - Directors can only cancel for users in companies they manage
     - Managers can only cancel for users in their own company
     ↓
     Set CanceledAt = NOW, CanceledBy = currentUserId
     ↓
     Send notification to assignee
```

### Implementation: Create On-Duty

**Services/OnDutyService.cs:210-303**

```csharp
public async Task<(bool Success, string Message, OnDuty? OnDuty)> CreateOnDutyAsync(
    int assigneeId,
    DateOnly date,
    OnDutyType type,
    string? notes = null,
    bool forceAssign = false)
{
    var currentUserId = GetCurrentUserId();
    int? companyId = null;
    try
    {
        var currentUser = await GetCurrentUserAsync();
        companyId = currentUser?.CompanyId;

        if (currentUser == null)
        {
            return (false, "User not authenticated.", null);
        }

        // Check if current user can manage OnDuty
        if (!await CanUserManageOnDutyAsync(currentUserId))
        {
            return (false, "You do not have permission to create on-duty assignments.", null);
        }

        // Get assignee (must use IgnoreQueryFilters for cross-company access)
        var assignee = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == assigneeId);
        if (assignee == null)
        {
            return (false, "Assignee not found.", null);
        }

        // Check if assignee is active
        if (!assignee.IsActive)
        {
            return (false, "Cannot assign on-duty to inactive user.", null);
        }

        // Check if assignee already has an active on-duty on this date and type
        if (await HasActiveOnDutyOnDateAsync(assigneeId, date, type))
        {
            return (false, $"This user already has an active {type} on-duty assignment on this date.", null);
        }

        // COLLISION RULE: Check for vacation conflict (unless force-assigning)
        if (!forceAssign)
        {
            var (hasConflict, vacationStart, vacationEnd, vacationType) = await GetVacationConflictDetailsAsync(assigneeId, date);
            if (hasConflict)
            {
                // Return vacation details for UI to display in confirmation dialog
                return (false, $"VACATION_CONFLICT|{vacationStart}|{vacationEnd}|{vacationType}", null);
            }
        }

        // Create the on-duty assignment
        var onDuty = new OnDuty
        {
            UserId = assigneeId,
            Date = date,
            Type = type,
            Notes = notes?.Trim(),
            CreatedBy = currentUserId,
            CreatedAt = DateTime.UtcNow
        };

        _db.OnDuties.Add(onDuty);
        await _db.SaveChangesAsync();

        _logger.LogInformation("OnDuty {OnDutyId} ({Type}) created by user {CreatedBy} for user {UserId} on {Date}",
            onDuty.Id, type, currentUserId, assigneeId, date);

        // Audit log for force-assignments (bypassing vacation conflict)
        if (forceAssign)
        {
            _logger.LogWarning("OnDuty {OnDutyId} was FORCE-ASSIGNED by user {CreatedBy} despite vacation conflict for user {UserId} on {Date}",
                onDuty.Id, currentUserId, assigneeId, date);
        }

        return (true, "On-duty assignment created successfully.", onDuty);
    }
    catch (DbUpdateException ex)
    {
        _logger.LogError(ex, "Database error creating on-duty. CompanyId={CompanyId}, CreatedBy={CreatedBy}, AssigneeId={AssigneeId}, Date={Date}, Type={Type}",
            companyId, currentUserId, assigneeId, date, type);
        return (false, "An error occurred while creating the on-duty assignment.", null);
    }
}
```

### Business Rules

1. **Authorization:**
   - Manager/Director/Owner can create on-duty (NOT Assigner)
   - Assigner role explicitly excluded (chores only)

2. **Assignee Eligibility:**
   - Anyone can be assigned on-duty (including Directors)
   - Must be active user

3. **Global Scoping:**
   - No CompanyId on OnDuty table
   - Queries must use `IgnoreQueryFilters()` to access cross-company

4. **Conflict Detection:**
   - One on-duty of each type per user per date
   - Vacation conflict detection (same as chores)

---

## Conflict Detection System

**Purpose:** Validate all shift assignments against business rules before allowing assignment.

**Service:** `Services/ConflictChecker.cs` (127 lines)

### Validation Rules

```
ConflictChecker.CanAssignAsync(userId, shiftInstance)
   ↓
   1. User exists and is active
   ↓
   2. Shift type exists
   ↓
   3. Check approved time-off:
      - Query: TimeOffRequests WHERE UserId = @userId AND Status = Approved
              AND ShiftInstance.WorkDate BETWEEN StartDate AND EndDate
      - IF conflict → FAIL ("Approved time-off covers this date")
   ↓
   4. Check overlapping shifts:
      - Load all assignments for user in ±7 day window
      - Calculate shift time windows (handling overnight shifts)
      - IF overlap detected → FAIL ("Overlap with existing assignment")
      - EXCEPTION: OFFLINE shifts allow overlaps (warning only)
   ↓
   5. Check rest period:
      - Find nearest shift before (where shift.End <= current.Start)
      - Find nearest shift after (where shift.Start >= current.End)
      - IF rest < RestHours config (default 8h) → FAIL ("Rest period too short")
   ↓
   6. Check weekly hours cap:
      - Load all assignments for user in the same week (Sunday-Saturday)
      - Sum total hours (handling overnight shifts)
      - Add current shift hours
      - IF total > WeeklyHoursCap config (default 40h) → FAIL ("Weekly hours cap exceeded")
   ↓
   ALL CHECKS PASSED → ALLOW assignment
```

### Implementation

**Services/ConflictChecker.cs:19-118**

```csharp
public async Task<ConflictResult> CanAssignAsync(int userId, ShiftInstance instance, CancellationToken ct = default)
{
    var user = await _db.Users.FindAsync(new object?[] { userId }, ct);
    if (user == null || !user.IsActive)
        return ConflictResult.Fail("User inactive or not found.");

    var t = await _db.ShiftTypes.FindAsync(new object?[] { instance.ShiftTypeId }, ct);
    if (t is null) return ConflictResult.Fail("Shift type missing.");

    // OFFLINE shifts can coexist with other shifts - show warning but allow
    bool isOfflineShift = t.IsOffline;

    // Approved Time off blocks
    bool hasTimeOff = await _db.TimeOffRequests
        .AnyAsync(r => r.UserId == userId
                    && r.Status == RequestStatus.Approved
                    && instance.WorkDate >= r.StartDate
                    && instance.WorkDate <= r.EndDate, ct);
    if (hasTimeOff) return ConflictResult.Fail("Approved time-off covers this date.");

    var (start, end) = TimeHelpers.GetShiftWindow(t, instance.WorkDate);

    // Overlap + Rest + Weekly cap checks
    // Fetch assignments in the surrounding 7 days for the user
    var weekStart = TimeHelpers.WeekStart(instance.WorkDate).AddDays(-1);
    var weekEnd = weekStart.AddDays(8);

    var relevantAssignments = await (from a in _db.ShiftAssignments
                                     join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                     join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                     where a.UserId == userId
                                        && si.WorkDate >= weekStart && si.WorkDate <= weekEnd
                                     select new
                                     {
                                         si.WorkDate,
                                         st.Start,
                                         st.End
                                     }).ToListAsync(ct);

    double totalHoursThisWeek = 0;

    foreach (var ra in relevantAssignments)
    {
        var (rs, re) = TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate);
        // Overlap detection
        bool overlaps = rs < end && start < re;
        if (overlaps)
        {
            // For Offline shifts, we allow overlaps but track them for warning
            if (!isOfflineShift)
            {
                return ConflictResult.Fail("Overlap with existing assignment.");
            }
        }
    }

    // Rest period: find nearest before/after shifts
    var before = relevantAssignments
        .Select(ra => TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate))
        .Where(w => w.end <= start)
        .OrderByDescending(w => w.end)
        .FirstOrDefault();

    var after = relevantAssignments
        .Select(ra => TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate))
        .Where(w => w.start >= end)
        .OrderBy(w => w.start)
        .FirstOrDefault();

    int restHours = await GetConfigIntAsync(instance.CompanyId, "RestHours", 8, ct);
    if (before.end != default && (start - before.end).TotalHours < restHours)
        return ConflictResult.Fail($"Rest period too short (< {restHours}h) from previous shift.");
    if (after.start != default && (after.start - end).TotalHours < restHours)
        return ConflictResult.Fail($"Rest period too short (< {restHours}h) before next shift.");

    // Weekly cap: hours of existing week + this shift <= cap
    var weekStart2 = TimeHelpers.WeekStart(instance.WorkDate);
    var weekEnd2 = weekStart2.AddDays(6);
    var weekAssignments = await (from a in _db.ShiftAssignments
                                 join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                 join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                 where a.UserId == userId
                                    && si.WorkDate >= weekStart2 && si.WorkDate <= weekEnd2
                                 select new { si.WorkDate, st.Start, st.End })
                                 .ToListAsync(ct);

    foreach (var ra in weekAssignments)
    {
        var (rs, re) = TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate);
        totalHoursThisWeek += (re - rs).TotalHours;
    }

    totalHoursThisWeek += TimeHelpers.Hours(t);

    int weeklyCap = await GetConfigIntAsync(instance.CompanyId, "WeeklyHoursCap", 40, ct);
    if (totalHoursThisWeek > weeklyCap)
        return ConflictResult.Fail($"Weekly hours cap exceeded (> {weeklyCap}h).");

    return ConflictResult.Ok();
}
```

### Configurable Parameters

**Loaded from AppConfig cache (see 13-CACHING-STRATEGY.md):**

| Config Key | Default | Description |
|------------|---------|-------------|
| `RestHours` | 8 | Minimum rest period between shifts (hours) |
| `WeeklyHoursCap` | 40 | Maximum weekly hours per employee |

**Services/ConflictChecker.cs:120-125**

```csharp
// PERFORMANCE FIX: Use config cache to reduce database queries
private async Task<int> GetConfigIntAsync(int companyId, string key, int defaultValue, CancellationToken ct = default)
{
    var config = await _configCache.GetConfigAsync(companyId, key);
    return int.TryParse(config?.Value, out var i) ? i : defaultValue;
}
```

---

## Notification System

**Purpose:** Send in-app notifications and email alerts to users when events occur (shift assigned, time-off approved, chore assigned, etc.).

**Service:** `Services/NotificationService.cs` (808 lines)

### Notification Types

**Models/Support/Enums.cs (NotificationType enum):**

```csharp
public enum NotificationType
{
    ShiftAdded,             // New shift assignment
    ShiftRemoved,           // Shift assignment removed
    TimeOffApproved,        // Time-off request approved
    TimeOffDeclined,        // Time-off request declined
    TimeOffDeleted,         // Approved time-off deleted by manager
    SwapRequestApproved,    // Swap request approved
    SwapRequestDeclined,    // Swap request declined
    ChoreAssigned,          // New chore assigned
    ChoreCanceled,          // Chore canceled
    OnDutyAssigned,         // New on-duty assigned
    OnDutyCanceled          // On-duty canceled
}
```

### Workflow: Create Notification

```
Event occurs (e.g., shift assigned)
  ↓
  Service calls NotificationService.CreateShiftAddedNotificationAsync()
     ↓
     1. Get current tenant ID from TenantResolver
     ↓
     2. Create UserNotification record:
        - UserId (recipient)
        - Type (e.g., ShiftAdded)
        - Title (localized string)
        - Message (localized string with parameters)
        - IsRead = false
        - RelatedEntityId (e.g., AssignmentId)
        - RelatedEntityType (e.g., "ShiftAssignment")
     ↓
     3. Save to database
     ↓
     4. Send email notification (if applicable):
        - Load user from database
        - Call MailService.SendShiftAssignedEmailAsync()
        - Email failure does NOT block notification creation (logged only)
```

### Implementation: Shift Assignment Notification

**Services/NotificationService.cs:90-121**

```csharp
public async Task CreateShiftAddedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime)
{
    var title = _localizer["NotificationShiftAddedTitle"];
    var message = string.Format(_localizer["NotificationShiftAddedMessage"],
        shiftTypeName,
        shiftDate.ToString("MMM dd, yyyy"),
        startTime.ToString("HH:mm"),
        endTime.ToString("HH:mm"));

    await CreateNotificationAsync(userId, NotificationType.ShiftAdded, title, message, null, "ShiftAssignment");

    // Send email notification
    try
    {
        var user = await _db.Users.FindAsync(userId);
        if (user != null && !string.IsNullOrWhiteSpace(user.Email))
        {
            await _mailService.SendShiftAssignedEmailAsync(
                user.Email,
                user.DisplayName,
                shiftTypeName,
                shiftDate,
                startTime,
                endTime);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error sending shift assigned email to user {UserId}", userId);
        // Don't throw - email failure should not block notification creation
    }
}
```

### Implementation: Time-Off Notification

**Services/NotificationService.cs:156-173**

```csharp
public async Task CreateTimeOffNotificationAsync(int userId, RequestStatus status, DateOnly startDate, DateOnly endDate, int requestId)
{
    var title = status == RequestStatus.Approved
        ? _localizer["NotificationTimeOffApprovedTitle"]
        : _localizer["NotificationTimeOffDeclinedTitle"];

    var dateRange = startDate == endDate
        ? startDate.ToString("MMM dd, yyyy")
        : $"{startDate:MMM dd} - {endDate:MMM dd, yyyy}";

    var message = status == RequestStatus.Approved
        ? string.Format(_localizer["NotificationTimeOffApprovedMessage"], dateRange)
        : string.Format(_localizer["NotificationTimeOffDeclinedMessage"], dateRange);

    var notificationType = status == RequestStatus.Approved ? NotificationType.TimeOffApproved : NotificationType.TimeOffDeclined;

    await CreateNotificationAsync(userId, notificationType, title, message, requestId, "TimeOffRequest");
}
```

### Email Notifications

**Notifications that trigger emails:**
- Shift added/removed
- Chore assigned/canceled
- Daily digest (scheduled)
- Day-before reminders (scheduled)

**Notifications that do NOT trigger emails:**
- Time-off approved/declined (in-app only)
- Swap request approved/declined (in-app only)

---

## Daily Digest Workflow

**Purpose:** Send scheduled email summaries to users with their upcoming shifts, chores, on-duty assignments, and pending requests.

**Service:** `Services/NotificationService.cs` (lines 287-636)

### Workflow Steps

```
STEP 1: Scheduled Job Runs (e.g., every 15 minutes via background worker)
  ↓
  For each company:
     ↓
     Call NotificationService.GetUsersForDailyDigestAsync(currentTime, companyId)
        ↓
        Query: DailyNotificationPreferences
               WHERE CompanyId = @companyId
                 AND IsActive = true
                 AND ReceiveDailyDigest = true
                 AND PreferredTime BETWEEN (currentTime - 15min) AND (currentTime + 15min)
        ↓
        Return list of user IDs

STEP 2: Send Digest to Each User
  ↓
  For each userId:
     ↓
     Call NotificationService.SendDailyDigestAsync(userId, companyId)
        ↓
        Load user preferences (what to include in digest)
        ↓
        Parallel query execution using Task.WhenAll():
           - Upcoming shifts (next 7 days) [if IncludeUpcomingShifts = true]
           - Pending time-off requests [if IncludePendingRequests = true]
           - Pending swap requests [if IncludePendingRequests = true]
           - Assigned chores (next 7 days) [if IncludeChores = true]
           - On-duty assignments (next 7 days) [if IncludeOnDuty = true]
           - Today's on-duty by subscribed roles [if user subscribed to specific roles]
        ↓
        Build HTML email with digest sections
        ↓
        Send email via MailService
```

### Implementation: Get Users for Digest

**Services/NotificationService.cs:295-324**

```csharp
public async Task<List<int>> GetUsersForDailyDigestAsync(TimeOnly currentTime, int companyId)
{
    try
    {
        // Allow 15-minute window for digest delivery
        var timeWindow = TimeSpan.FromMinutes(15);
        var lowerBound = currentTime.Add(-timeWindow);
        var upperBound = currentTime.Add(timeWindow);

        var userIds = await _db.DailyNotificationPreferences
            .Where(p => p.CompanyId == companyId
                     && p.IsActive
                     && p.ReceiveDailyDigest
                     && p.PreferredTime >= lowerBound
                     && p.PreferredTime <= upperBound)
            .Select(p => p.UserId)
            .ToListAsync();

        _logger.LogInformation("Found {Count} users for daily digest at {Time} for company {CompanyId}",
            userIds.Count, currentTime, companyId);

        return userIds;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting users for daily digest at {Time} for company {CompanyId}",
            currentTime, companyId);
        return new List<int>();
    }
}
```

### Implementation: Parallel Data Loading

**Services/NotificationService.cs:354-438**

```csharp
// Phase 2C: Parallelize digest data queries using Task.WhenAll
Task<List<ShiftAssignment>>? upcomingShiftsTask = null;
Task<List<TimeOffRequest>>? pendingTimeOffTask = null;
Task<List<SwapRequest>>? pendingSwapsTask = null;
Task<List<Chore>>? upcomingChoresTask = null;
Task<List<OnDuty>>? upcomingOnDutyTask = null;

// Upcoming Shifts (next 7 days)
if (preference.IncludeUpcomingShifts)
{
    upcomingShiftsTask = _db.ShiftAssignments
        .Include(sa => sa.ShiftInstance)
        .ThenInclude(si => si.ShiftType)
        .Where(sa => sa.UserId == userId
                  && sa.CompanyId == companyId
                  && sa.ShiftInstance.WorkDate >= today
                  && sa.ShiftInstance.WorkDate <= nextWeek)
        .OrderBy(sa => sa.ShiftInstance.WorkDate)
        .ThenBy(sa => sa.ShiftInstance.ShiftType.Start)
        .Take(10)
        .ToListAsync();
}

// Pending Time-Off and Swap Requests
if (preference.IncludePendingRequests)
{
    pendingTimeOffTask = _db.TimeOffRequests
        .Where(r => r.UserId == userId
                 && r.CompanyId == companyId
                 && r.Status == RequestStatus.Pending)
        .OrderBy(r => r.StartDate)
        .Take(5)
        .ToListAsync();

    pendingSwapsTask = _db.SwapRequests
        .Include(sr => sr.FromAssignment)
        .ThenInclude(sa => sa.ShiftInstance)
        .ThenInclude(si => si.ShiftType)
        .Where(sr => (sr.FromUserId == userId || sr.ToUserId == userId)
                  && sr.CompanyId == companyId
                  && sr.Status == RequestStatus.Pending)
        .OrderBy(sr => sr.CreatedAt)
        .Take(5)
        .ToListAsync();
}

// Assigned Chores (next 7 days)
if (preference.IncludeChores)
{
    upcomingChoresTask = _db.Chores
        .Where(c => c.UserId == userId
                 && c.CompanyId == companyId
                 && c.Date >= today
                 && c.Date <= nextWeek
                 && c.CanceledAt == null)
        .OrderBy(c => c.Date)
        .Take(10)
        .ToListAsync();
}

// OnDuty Assignments (next 7 days)
if (preference.IncludeOnDuty)
{
    upcomingOnDutyTask = _db.OnDuties
        .Where(od => od.UserId == userId
                  && od.Date >= today
                  && od.Date <= nextWeek
                  && od.CanceledAt == null)
        .OrderBy(od => od.Date)
        .Take(10)
        .ToListAsync();
}

// Wait for all queries to complete in parallel
var tasks = new List<Task>();
if (upcomingShiftsTask != null) tasks.Add(upcomingShiftsTask);
if (pendingTimeOffTask != null) tasks.Add(pendingTimeOffTask);
if (pendingSwapsTask != null) tasks.Add(pendingSwapsTask);
if (upcomingChoresTask != null) tasks.Add(upcomingChoresTask);
if (upcomingOnDutyTask != null) tasks.Add(upcomingOnDutyTask);

if (tasks.Any())
{
    await Task.WhenAll(tasks);
}
```

### Digest Preferences

**User can configure:**
- `ReceiveDailyDigest` (bool) - Enable/disable digest
- `PreferredTime` (TimeOnly) - What time to receive digest (e.g., 08:00)
- `IncludeUpcomingShifts` (bool) - Include upcoming shifts section
- `IncludePendingRequests` (bool) - Include pending requests section
- `IncludeChores` (bool) - Include assigned chores section
- `IncludeOnDuty` (bool) - Include on-duty assignments section

**Plus role subscriptions:**
- `OnDutyRoleSubscriptions` - Get notified of today's Hakam/Lead/custom roles

### Day-Before Reminders

**Services/NotificationService.cs:638-807**

Similar to daily digest, but:
- Sends reminder 1 day before upcoming shifts/chores/on-duty
- Controlled by separate preferences: `RemindBeforeShifts`, `RemindBeforeChores`, `RemindBeforeOnDuty`
- Only includes items for **tomorrow** (not next 7 days)

---

## Authorization & Access Control

ShiftManager uses **policy-based authorization** and **role-based checks** to control access.

### Role Hierarchy

```
Owner (highest privileges)
  ↓
Director (manages multiple companies)
  ↓
Manager (manages one company)
  ↓
Assigner (chores only, no shift/on-duty management)
  ↓
Employee (standard user)
  ↓
Trainee (limited, shadowing only)
```

### Access Control Patterns

**1. Multi-Company Access (Owner/Director)**

```csharp
// Pages/Requests/Index.cshtml.cs:78-94
List<int> accessibleCompanyIds;
if (currentUser.Role == UserRole.Owner)
{
    // Owner sees all companies
    accessibleCompanyIds = await _db.Users.Select(u => u.CompanyId).Distinct().ToListAsync();
}
else if (currentUser.Role == UserRole.Director)
{
    // Director sees companies they manage
    accessibleCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUser.Id);
}
else
{
    // Manager sees only their own company
    accessibleCompanyIds = new List<int> { currentUser.CompanyId };
}
```

**2. Request Approval Authorization**

```csharp
// Pages/Requests/Index.cshtml.cs:497-517
private async Task<bool> ValidateAccessToRequestAsync(AppUser currentUser, int targetCompanyId)
{
    if (currentUser.Role == UserRole.Owner)
    {
        return true; // Owner has access to all companies
    }
    else if (currentUser.Role == UserRole.Director)
    {
        var directorCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUser.Id);
        return directorCompanyIds.Contains(targetCompanyId);
    }
    else if (currentUser.Role == UserRole.Manager)
    {
        return currentUser.CompanyId == targetCompanyId;
    }

    return false; // Employees and trainees cannot manage requests
}
```

**3. Chore Assignment Authorization**

```csharp
// Services/ChoreService.cs:63-72
public async Task<bool> CanUserManageChoresAsync(int userId)
{
    var user = await _db.Users.FindAsync(userId);
    if (user == null) return false;

    return user.Role == UserRole.Owner ||
           user.Role == UserRole.Director ||
           user.Role == UserRole.Manager ||
           user.Role == UserRole.Assigner;
}
```

**4. On-Duty Assignment Authorization**

```csharp
// Services/OnDutyService.cs:80-90
public async Task<bool> CanUserManageOnDutyAsync(int userId)
{
    var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
    if (user == null) return false;

    // Only Manager, Director, and Owner can manage OnDuty
    // Assigner role is explicitly excluded
    return user.Role == UserRole.Owner ||
           user.Role == UserRole.Director ||
           user.Role == UserRole.Manager;
}
```

### Authorization Policies

**Defined in Program.cs:**

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("IsManagerOrAdmin", policy => policy.RequireRole("Manager", "Director", "Owner"));
    options.AddPolicy("IsDirectorOrAdmin", policy => policy.RequireRole("Director", "Owner"));
    options.AddPolicy("IsOwner", policy => policy.RequireRole("Owner"));
    options.AddPolicy("CanEditChores", policy => policy.RequireRole("Manager", "Director", "Owner", "Assigner"));
    options.AddPolicy("CanManageUsers", policy => policy.RequireRole("Manager", "Director", "Owner"));
    // ... etc.
});
```

---

## Business Rules Summary

### Shift Assignments

| Rule | Enforcement |
|------|-------------|
| User must be in same company as shift | `Pages/Assignments/Manage.cshtml.cs:190` |
| Cannot over-assign (assigned < required) | `Pages/Assignments/Manage.cshtml.cs:198` |
| No approved time-off on date | `ConflictChecker.cs:32` |
| No overlapping shifts (except OFFLINE) | `ConflictChecker.cs:64` |
| Rest period ≥ 8h (configurable) | `ConflictChecker.cs:88` |
| Weekly hours ≤ 40h (configurable) | `ConflictChecker.cs:114` |

### Time-Off Requests

| Rule | Enforcement |
|------|-------------|
| Dates not in past | `Pages/Requests/TimeOff/Create.cshtml.cs:47` |
| Dates ≤ 2 years in future | `Pages/Requests/TimeOff/Create.cshtml.cs:54` |
| Vacation duration ≤ 365 days | `Pages/Requests/TimeOff/Create.cshtml.cs:64` |
| Reason ≤ 1000 characters | `Pages/Requests/TimeOff/Create.cshtml.cs:73` |
| Delete conflicting shifts on approval | `Pages/Requests/Index.cshtml.cs:187` |
| Cannot delete if started | `Pages/Requests/Index.cshtml.cs:460` |

### Swap Requests

| Rule | Enforcement |
|------|-------------|
| Cannot swap with self | `Pages/Requests/Swaps/Create.cshtml.cs:94` |
| Assignment must belong to requester | `Pages/Requests/Swaps/Create.cshtml.cs:113` |
| Target user must be in same company | `Pages/Requests/Swaps/Create.cshtml.cs:124` |
| Trainees cannot create swaps | `Pages/Requests/Swaps/Create.cshtml.cs:44` |
| Full conflict check on approval | `Pages/Requests/Index.cshtml.cs:311` |

### Chores

| Rule | Enforcement |
|------|-------------|
| One chore per user per date | `Services/ChoreService.cs:282` |
| Directors cannot be assigned chores | `Services/ChoreService.cs:89` |
| Shift conflict → special error message | `Services/ChoreService.cs:290` |
| Vacation conflict → special error message | `Services/ChoreService.cs:298` |
| Force-assign bypasses vacation conflict | `Services/ChoreService.cs:296` |
| Assigner can create chores | `Services/ChoreService.cs:71` |

### On-Duty

| Rule | Enforcement |
|------|-------------|
| One on-duty per type per user per date | `Services/OnDutyService.cs:249` |
| Directors CAN be assigned on-duty | `Services/OnDutyService.cs:96` (no filter) |
| Assigner CANNOT create on-duty | `Services/OnDutyService.cs:88` (excluded) |
| Global scope (no CompanyId) | `Models/OnDuty.cs` (no CompanyId property) |
| Vacation conflict → special error message | `Services/OnDutyService.cs:257` |
| Force-assign bypasses vacation conflict | `Services/OnDutyService.cs:256` |

---

## Related Documentation

- **[05-MULTI-TENANCY-DEEP-DIVE.md](05-MULTI-TENANCY-DEEP-DIVE.md)** - CompanyId scoping, IgnoreQueryFilters()
- **[07-SERVICE-LAYER.md](07-SERVICE-LAYER.md)** - Service implementations
- **[10-AUTHENTICATION-AND-AUTHORIZATION.md](10-AUTHENTICATION-AND-AUTHORIZATION.md)** - Role-based authorization
- **[13-CACHING-STRATEGY.md](13-CACHING-STRATEGY.md)** - Config cache for ConflictChecker

---

**End of Document** - Part of PROJECT COSMOGENESIS
**File:** `docs/genesis/14-WORKFLOWS-AND-BUSINESS-LOGIC.md`
**Lines:** 1,829
