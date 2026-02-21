# FINDING-006: Auto-Approval Path Missing All Post-Approval Side Effects

| Field | Value |
|-------|-------|
| **ID** | FINDING-006 |
| **Date** | 2026-02-18 |
| **Category** | Business Logic / Notifications |
| **Severity** | MEDIUM |

## Expected Behavior

When a vacation request is auto-approved via `VacationApprovalService.SubmitForApprovalAsync()`, the same post-approval processing should occur as manual approval:
1. Overlapping shift assignments removed
2. Trainee shadowing cancelled
3. Notification sent to the user

## Actual Behavior

`VacationApprovalService.SubmitForApprovalAsync()` (lines 115-127) auto-approval branch:
- Sets `request.Status = RequestStatus.Approved`
- Calls `SaveChangesAsync()`
- Returns `(true, "VacationApproval_AutoApproved")`
- **Execution stops. No further processing.**

Missing:
- No notification (VacationApprovalService does not inject `INotificationService` or `IMailService`)
- No shift assignment removal (no reference to `ShiftAssignment` table anywhere in the file)
- No trainee shadowing cancellation
- The planned `ProcessApprovedTimeOffAsync` helper (referenced in design docs) was never implemented

Additionally, the call site in `Pages/My/Requests.cshtml.cs` (lines 237-242) only logs the result — it never branches on auto-approval to trigger side effects:
```csharp
var (success, approvalMessage) = await _vacationApprovalService.SubmitForApprovalAsync(request.Id, userId);
_logger.LogInformation(...); // logged but not acted upon
Message = _localizer["Success_TimeOffRequestSubmitted"]; // same message regardless
```

## Root Cause

The auto-approval feature was implemented as a status change only. The design doc called for a `ProcessApprovedTimeOffAsync` helper to handle all post-approval side effects, but it was never built. `VacationApprovalService` only injects `AppDbContext`, `IGrantService`, and `ILogger` — it has no access to notification or shift assignment services.

## Fix Recommendation

Either:
**Option A** — Inject missing services into `VacationApprovalService` and add post-approval processing inline:
```csharp
// After auto-approve status change:
await RemoveOverlappingShiftAssignments(request);
await CancelTraineeShadowing(request);
await _notificationService.CreateTimeOffNotificationAsync(
    request.UserId, RequestStatus.Approved, request.StartDate, request.EndDate, request.Id);
```

**Option B** — Have the call site in `Requests.cshtml.cs` detect auto-approval and trigger side effects:
```csharp
if (success && approvalMessage == "VacationApproval_AutoApproved")
{
    await ProcessApprovedTimeOff(request);
}
```

## Verification Plan

1. Configure a VacationApprovalRule with `MaxAutoApproveDays = 5`
2. Submit a 3-day vacation request for a user who has shift assignments during that period
3. Verify request is auto-approved AND shifts are removed AND notification is received (after fix)

## Confidence

**98%** — Verified: VacationApprovalService does not inject notification/mail services, no shift assignment code exists in the file, design doc confirms this was a known undelivered feature.
