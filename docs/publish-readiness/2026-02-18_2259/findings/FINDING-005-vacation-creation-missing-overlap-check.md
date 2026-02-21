# FINDING-005: Vacation Request Page Creation Missing Overlap Validation

| Field | Value |
|-------|-------|
| **ID** | FINDING-005 |
| **Date** | 2026-02-18 |
| **Category** | Business Logic / Requests |
| **Severity** | MEDIUM |

## Expected Behavior

When a user submits a vacation request via the `Create.cshtml.cs` page, the system should check for overlapping approved or pending time-off requests and warn or reject the submission.

## Actual Behavior

`Create.cshtml.cs` `OnPostAsync()` (lines 30-96) validates:
- End date >= start date
- Start date not in the past
- Duration not > 2 years ahead
- Duration <= 365 days for vacation type
- Reason length <= 1000 characters

It does NOT check for overlapping existing requests.

## Evidence

**Create.cshtml.cs** `OnPostAsync()` — no query against existing `TimeOffRequests` for the same user and overlapping date range.

**Contrast — TimeOffApiService.CreateTimeOffRequestAsync()** (lines 147-157):
```csharp
var overlapping = await _context.TimeOffRequests
    .Where(r => r.UserId == userId
        && r.Status != TimeOffStatus.Declined
        && r.StartDate <= request.EndDate
        && r.EndDate >= request.StartDate)
    .AnyAsync();
if (overlapping)
    throw new InvalidOperationException("Overlapping time-off request exists");
```

The API version HAS overlap detection. The page version does not.

## Root Cause

The page-based creation predates the API service. The overlap check was added to the API but not backported to the page handler.

## Fix Recommendation

Add overlap check in `Create.cshtml.cs` `OnPostAsync()` before saving:

```csharp
var userId = int.Parse(User.FindFirst("UserId")!.Value);
var hasOverlap = await _context.TimeOffRequests
    .Where(r => r.UserId == userId
        && r.Status != TimeOffStatus.Declined
        && r.StartDate <= Input.EndDate
        && r.EndDate >= Input.StartDate)
    .AnyAsync();
if (hasOverlap)
{
    ModelState.AddModelError("", "You already have a time-off request for this period.");
    return Page();
}
```

## Verification Plan

1. Create and approve a vacation request for dates X-Y
2. Attempt to create another request overlapping dates X-Y via the page
3. Verify rejection with appropriate error message (after fix)

## Confidence

**95%** — Code review confirms the gap; API code demonstrates the intended validation.
