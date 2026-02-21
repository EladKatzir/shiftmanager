# FINDING-004: API Time-Off Approval Missing Shift Cleanup and Notifications

| Field | Value |
|-------|-------|
| **ID** | FINDING-004 |
| **Date** | 2026-02-18 |
| **Category** | API / Business Logic |
| **Severity** | MEDIUM |

## Expected Behavior

When a time-off request is approved via the API (`TimeOffApiService.ApproveTimeOffRequestAsync`), the system should:
1. Remove overlapping shift assignments during the approved period
2. Cancel trainee shadowing assignments
3. Send notification to the requesting employee

This matches the behavior of the page-based approval in `Requests/Index.cshtml.cs`.

## Actual Behavior

`TimeOffApiService.ApproveTimeOffRequestAsync()` (lines 183-208) only:
1. Validates the request exists and is Pending
2. Sets `Status = Approved`, `ReviewedBy`, `ReviewedAt`
3. Calls `SaveChangesAsync()`

It does NOT remove overlapping shifts, cancel trainee assignments, or send notifications.

## Evidence

**TimeOffApiService.cs** (lines 183-208):
```csharp
public async Task<TimeOffRequestDto> ApproveTimeOffRequestAsync(int requestId, int reviewerId)
{
    var request = await _context.TimeOffRequests...
    request.Status = TimeOffStatus.Approved;
    request.ReviewedBy = reviewerId;
    request.ReviewedAt = DateTime.UtcNow;
    await _context.SaveChangesAsync();
    return MapToDto(request);
}
```

**Contrast — Requests/Index.cshtml.cs** `OnPostApproveTimeOffAsync()` (lines 166-250):
- Lines 217-225: Removes overlapping shift assignments
- Cancels trainee shadowing
- Sends notification via `_notificationService`
- Uses `_concurrencyService.SaveWithConcurrencyHandlingAsync()`

## Root Cause

The API endpoint was implemented as a thin CRUD layer without replicating the business logic from the page handler. The page and API code paths diverged — the page gained side-effects (shift cleanup, notifications) that were never ported to the API service.

## Fix Recommendation

Extract shared approval logic into a dedicated service method (or use `VacationApprovalService` which already exists) and call it from both the API and page paths:

```csharp
// In TimeOffApiService.ApproveTimeOffRequestAsync:
await _vacationApprovalService.ApproveAsync(requestId, reviewerId);
```

## Verification Plan

1. Create a time-off request for a user who has shift assignments during that period
2. Approve via API (`PUT /api/v1/time-off-requests/{id}/approve`)
3. Verify shift assignments are removed
4. Verify notification is sent

## Confidence

**95%** — Code comparison between API and page handler confirms the gap.
