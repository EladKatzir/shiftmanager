# Unified Calendar Validation Design

## Status: IMPLEMENTED - All 10 steps complete, build passes, pending manual testing

## Goal
Unify shift assignment validation into `ShiftAssignmentService.ValidateShiftAssignmentAsync`,
retire `ConflictChecker`, and add missing conflict checks per business rules.

## Business Rules (confirmed by user)
- Overlapping shifts (same hours): **Error** (hard block)
- Back-to-back / insufficient rest: **Error** (hard block)
- Vacation / time-off: **Warning** (overrideable) -- NOTE: this is a deliberate downgrade
  from the previous hard-block behavior. Swap approvals can now override vacation conflicts.
- Chore conflict: **Warning** (overrideable)
- On-duty conflict: **Warning** (overrideable)
- Offline shifts: exempt from overlap + rest checks (can coexist with regular shifts)
- Rest hours default: 8h (from AreaSettings seed; code fallback changing from 11 to 8)
- Weekly cap: per HierarchySettings cascade (seed: 56h; code fallback changing from 60 to 56)

---

## Implementation Steps

### Step 1: Update HierarchySettings defaults
**Files:** `Services/HierarchySettingsService.cs`, `Models/AreaSettings.cs`

Change code-level fallback constants to match seed data:
- `DefaultRestHours`: 11 -> 8
- `DefaultWeeklyCap`: 60 -> 56
- `AreaSettings.DefaultRestHours` property default: 11 -> 8
- `AreaSettings.DefaultWeeklyCap` property default: 60 -> 56

### Step 2: Add new ValidationCategory values
**File:** `Services/IShiftAssignmentService.cs`

Add to enum:
```
Overlap, TimeOff, ChoreConflict
```

### Step 3: Inject IAppConfigCacheService into ShiftAssignmentService
**File:** `Services/ShiftAssignmentService.cs`

Need `IAppConfigCacheService` for reading `WeekStartDay` config (finding #9).

### Step 4: Rewrite ValidateShiftAssignmentAsync
**File:** `Services/ShiftAssignmentService.cs` (lines 175-335)

Insert new checks AFTER line 241 (duplicate check) and AFTER line 284 (effectiveSettings load).
Remove old CheckRestHoursViolationAsync call (lines 312-321).

#### New checks to add (in order):

**A. Load nearby assignments (7-day window)**
```csharp
var shiftType = shiftInstance.ShiftType;
bool isOfflineShift = shiftType.IsOffline;
var (newStart, newEnd) = TimeHelpers.GetShiftWindow(shiftType, shiftInstance.WorkDate);

var windowStart = TimeHelpers.WeekStart(shiftInstance.WorkDate).AddDays(-1);
var windowEnd = windowStart.AddDays(8);
var nearbyAssignments = await _db.ShiftAssignments.IgnoreQueryFilters()
    .Where(sa => sa.UserId == userId
        && sa.ShiftInstanceId != shiftInstanceId  // FINDING #7: exclude self
        && sa.ShiftInstance.WorkDate >= windowStart
        && sa.ShiftInstance.WorkDate <= windowEnd)
    .Select(sa => new {
        sa.ShiftInstance.WorkDate,
        sa.ShiftInstance.ShiftType.Start,
        sa.ShiftInstance.ShiftType.End,
        IsOffline = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_OFFLINE
    }).ToListAsync();
```

**B. Overlap detection (Error)**
```csharp
if (!isOfflineShift)
{
    foreach (var ra in nearbyAssignments)
    {
        if (ra.IsOffline) continue;
        var (rs, re) = TimeHelpers.GetShiftWindow(
            new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate);
        if (rs < newEnd && newStart < re)
        {
            errors.Add(new ValidationIssue(
                "OVERLAP", _localizer["Error_ShiftOverlap"],
                ValidationSeverity.Error, ValidationCategory.Overlap));
            break;
        }
    }
}
```

**C. Rest period (Error) — replaces old Warning**
```csharp
if (!isOfflineShift)
{
    var restRequired = effectiveSettings?.RestHours ?? 8;
    var nonOfflineWindows = nearbyAssignments
        .Where(ra => !ra.IsOffline)
        .Select(ra => TimeHelpers.GetShiftWindow(
            new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate))
        .ToList();

    var before = nonOfflineWindows
        .Where(w => w.end <= newStart)
        .OrderByDescending(w => w.end)
        .FirstOrDefault();
    var after = nonOfflineWindows
        .Where(w => w.start >= newEnd)
        .OrderBy(w => w.start)
        .FirstOrDefault();

    bool restViolation = false;
    if (before.end != default && (newStart - before.end).TotalHours < restRequired)
        restViolation = true;
    if (after.start != default && (after.start - newEnd).TotalHours < restRequired)
        restViolation = true;

    if (restViolation)
    {
        errors.Add(new ValidationIssue(
            "REST_HOURS_VIOLATION", _localizer["Error_RestHoursViolation"],
            ValidationSeverity.Error, ValidationCategory.RestHours));
    }
}
```
NOTE: Uses `(newStart - before.end).TotalHours < restRequired` which correctly catches
zero-gap (0 < 8 = true). This matches ConflictChecker's logic.

**D. Vacation conflict (Warning)**
```csharp
bool hasTimeOff = await _db.TimeOffRequests.IgnoreQueryFilters()
    .AnyAsync(r => r.UserId == userId
        && r.Status == RequestStatus.Approved
        && shiftInstance.WorkDate >= r.StartDate
        && shiftInstance.WorkDate <= r.EndDate);
if (hasTimeOff)
{
    warnings.Add(new ValidationIssue(
        "VACATION_CONFLICT", _localizer["Warning_VacationConflict"],
        ValidationSeverity.Warning, ValidationCategory.TimeOff));
}
```

**E. Chore conflict (Warning)**
```csharp
// FINDING #8: Use CanceledAt == null, NOT IsActive (which is [NotMapped])
bool hasChore = await _db.Chores.IgnoreQueryFilters()
    .AnyAsync(c => c.UserId == userId
        && c.Date == shiftInstance.WorkDate
        && c.CanceledAt == null);
if (hasChore)
{
    warnings.Add(new ValidationIssue(
        "CHORE_CONFLICT", _localizer["Warning_ChoreConflict"],
        ValidationSeverity.Warning, ValidationCategory.ChoreConflict));
}
```

**F. On-duty conflict (Warning)**
```csharp
bool hasOnDuty = await _db.OnDuties.IgnoreQueryFilters()
    .AnyAsync(o => o.UserId == userId
        && o.Date == shiftInstance.WorkDate
        && o.CanceledAt == null);
if (hasOnDuty)
{
    warnings.Add(new ValidationIssue(
        "ONDUTY_CONFLICT", _localizer["Warning_OnDutyConflict"],
        ValidationSeverity.Warning, ValidationCategory.ChoreConflict));
}
```

### Step 5: Fix weekly cap calculation
**File:** `Services/ShiftAssignmentService.cs`

Two fixes:
1. Read WeekStartDay from config (finding #9):
```csharp
var weekStartDay = await _configCache.GetConfigAsync(shiftInstance.CompanyId, "WeekStartDay");
var weekStartDayOfWeek = (DayOfWeek)Math.Clamp(
    int.TryParse(weekStartDay?.Value, out var d) ? d : 0, 0, 6);
var startOfWeek = TimeHelpers.WeekStart(shiftInstance.WorkDate, weekStartDayOfWeek);
```

2. Port MergeAndSumHours for deduplication (finding #6):
   Copy the method from ConflictChecker. Use it instead of simple .Sum().

### Step 6: Delete old CheckRestHoursViolationAsync
**File:** `Services/ShiftAssignmentService.cs` (lines 806-845)

Delete the method entirely. The new rest-period logic in Step 4C replaces it.
Also remove the old rest-hours Warning code (lines 312-321).

### Step 7: Migrate Assignments/Manage.cshtml.cs
**File:** `Pages/Assignments/Manage.cshtml.cs`

Replace:
```csharp
private readonly IConflictChecker _checker;
// ... line 211:
var conflict = await _checker.CanAssignAsync(SelectedUserId.Value, Instance);
```
With:
```csharp
private readonly IShiftAssignmentService _assignmentService;
// ... line 211:
var validation = await _assignmentService.ValidateShiftAssignmentAsync(
    SelectedUserId.Value, Instance.Id);
if (!validation.CanAssign)
{
    // Show errors (same as current conflict handling)
}
if (validation.Warnings.Count > 0)
{
    // Show warnings with override option
}
```

IMPORTANT (finding #4): Keep the existing cross-company guard at lines 194-200.
It is a page-level policy check, NOT a validation concern.

### Step 8: Migrate Requests/Index.cshtml.cs
**File:** `Pages/Requests/Index.cshtml.cs`

Replace:
```csharp
var conflict = await _checker.CanAssignAsync(s.ToUserId.Value, si);
```
With:
```csharp
var validation = await _assignmentService.ValidateShiftAssignmentAsync(
    s.ToUserId.Value, si.Id);
```

NOTE (finding #5): `si` is an EXISTING ShiftInstance loaded from DB, not newly created.
`si.Id` is always valid.

### Step 9: Delete ConflictChecker
**Files to delete:**
- `Services/ConflictChecker.cs`
- `Services/IConflictChecker.cs`

**Files to modify:**
- `Program.cs:263` — Remove `builder.Services.AddScoped<IConflictChecker, ConflictChecker>()`

**Test files to update/delete:**
- `ShiftManager.Tests/UnitTests/Services/ConflictCheckerTests.cs`
- `ShiftManager.Tests/UnitTests/Services/ConflictCheckerExtendedTests.cs`
(Port relevant test cases to ShiftAssignmentService tests)

### Step 10: Add localization keys
**Files:** `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx`

Keys needed:
- Error_ShiftOverlap / "This shift overlaps with an existing assignment" / "משמרת זו חופפת לשיבוץ קיים"
- Warning_VacationConflict / "User has approved vacation on this date" / "למשתמש חופשה מאושרת בתאריך זה"
- Warning_ChoreConflict / "User has a chore assignment on this date" / "למשתמש תורנות בתאריך זה"
- Warning_OnDutyConflict / "User has an on-duty assignment on this date" / "למשתמש כוננות בתאריך זה"

---

## Known Limitations (accepted)

### Race condition on overlap/rest (finding #10)
Validation happens outside the transaction in AssignShiftAsync. Two concurrent requests
could both pass validation and create conflicting assignments. The transaction only
re-checks for exact duplicates (same shiftInstanceId + userId). This is an accepted
limitation — same as the old ConflictChecker behavior. A full fix would require
serializable transaction isolation, which has performance implications.

### Overnight shift edge case
GetShiftWindow correctly handles overnight shifts (End <= Start → adds 1 day to end).
The overlap formula `rs < newEnd && newStart < re` works for overnight shifts because
both sides of the comparison use DateTime (not TimeOnly), so wrapping is already resolved.

---

## Verification Checklist
- [ ] Build succeeds with 0 warnings
- [ ] Morning 07:00-15:00 + Afternoon 15:00-23:00 same day → REST_HOURS_VIOLATION Error
- [ ] Morning 07:00-15:00 + Morning 07:00-15:00 same day → OVERLAP Error
- [ ] Offline + Morning on same day → Allowed (no overlap error)
- [ ] User with approved vacation → VACATION_CONFLICT Warning (overrideable)
- [ ] User with chore on date → CHORE_CONFLICT Warning (overrideable)
- [ ] User with on-duty on date → ONDUTY_CONFLICT Warning (overrideable)
- [ ] Weekly cap from HierarchySettings cascade works correctly
- [ ] WeekStartDay config respected in weekly cap calculation
- [ ] Assignments/Manage still works with new validation
- [ ] Requests/Index swap approval still works
- [ ] ConflictChecker fully removed, no remaining references
