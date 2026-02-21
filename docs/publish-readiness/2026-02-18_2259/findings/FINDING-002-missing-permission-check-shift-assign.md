# FINDING-002: Missing Permission/Grant Check on Shift Assignment

| Field | Value |
|-------|-------|
| **ID** | FINDING-002 |
| **Date** | 2026-02-18 |
| **Category** | Authorization / Calendar |
| **Severity** | MEDIUM |

## Expected Behavior

`OnPostAssignEmployeeAsync` in `Calendar/Table.cshtml.cs` should verify the caller has the appropriate grant (e.g., `CanAssignShifts` or similar) before assigning an employee to a shift.

## Actual Behavior

The handler performs shift validation (job type, rest hours, weekly cap, etc.) via `IShiftAssignmentService.ValidateShiftAssignmentAsync()` but does **not** check whether the logged-in user holds a grant that authorizes them to assign shifts. The page itself has `[Authorize]` which ensures authentication, but any authenticated user who can reach the Calendar/Table page could POST an assignment.

## Evidence

**Calendar/Table.cshtml.cs** — `OnPostAssignEmployeeAsync` (line ~531):
- Calls `ValidateShiftAssignmentAsync()` for business-rule validation
- Calls `AssignShiftAsync()` to persist
- No call to `IGrantService.HasGrantAsync()` or any authorization check

**Contrast with other handlers:**
- `Requests/Index.cshtml.cs` `OnPostApproveTimeOffAsync()` calls `ValidateAccessToRequestAsync()` which checks grants
- `VacationApprovalService.ApproveAsync()` checks `HasApproveVacationGrant`

## Root Cause

The Calendar/Table page was built as a manager-facing CRUD surface and relies on page-level `[Authorize]` + UI visibility (only managers see the assign controls). However, no server-side grant check prevents a lower-privilege authenticated user from crafting a POST request.

## Fix Recommendation

Add a grant check at the top of `OnPostAssignEmployeeAsync`:

```csharp
var hasGrant = await _grantService.HasGrantWithScopeAsync(
    userId, GrantType.CanAssignShifts, companyId, moleculeId, null, null);
if (!hasGrant)
    return Forbid();
```

## Verification Plan

1. Log in as Employee role (no assignment grants)
2. POST to Calendar/Table AssignEmployee handler
3. Verify 403 Forbidden response

## Confidence

**90%** — Code review confirms no grant check exists. Requires runtime verification to confirm Employee role can reach the endpoint.
