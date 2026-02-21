# FINDING-003: No Past-Date Validation in Shift Assignment

| Field | Value |
|-------|-------|
| **ID** | FINDING-003 |
| **Date** | 2026-02-18 |
| **Category** | Business Logic / Calendar |
| **Severity** | MEDIUM |

## Expected Behavior

`ValidateShiftAssignmentAsync` should reject attempts to assign employees to shifts on dates that have already passed (or at minimum emit a warning).

## Actual Behavior

The 8 validation checks in `ShiftAssignmentService.ValidateShiftAssignmentAsync()` are:
1. JOB_TYPE_MISMATCH
2. SHIFT_GROUPING
3. WEEKLY_CAP
4. REST_HOURS
5. TRAINEE
6. CONCURRENCY
7. TECH_SHIFT
8. (capacity/duplicate in AssignShiftAsync)

None check whether the shift's `WorkDate` is in the past.

## Evidence

**ShiftAssignmentService.cs** — `ValidateShiftAssignmentAsync()`:
- Reviews all 8 validation methods
- No comparison of `shiftInstance.WorkDate` against `DateTime.Today` or `DateOnly.FromDateTime(DateTime.UtcNow)`

**ShiftAssignmentService.cs** — `AssignShiftAsync()`:
- Transaction-wrapped assignment with duplicate check and capacity check
- No past-date guard

## Root Cause

Past-date assignment was likely considered an intentional feature for back-filling historical records. However, it can lead to accidental modifications of historical shift data.

## Fix Recommendation

Add a WARNING-level validation (not a hard error) in `ValidateShiftAssignmentAsync`:

```csharp
if (shiftInstance.WorkDate < DateOnly.FromDateTime(DateTime.UtcNow))
{
    issues.Add(new ValidationIssue(
        "PAST_DATE", ValidationSeverity.Warning,
        "This shift date has already passed. Assignment will modify historical records."));
}
```

This preserves back-fill capability via override tokens while alerting users.

## Verification Plan

1. Attempt to assign an employee to a past date shift
2. Verify warning is displayed (after fix)
3. Verify override token allows the assignment to proceed

## Confidence

**85%** — Code review confirms no past-date check. Severity depends on whether historical back-fill is an intended workflow (possible business decision).
