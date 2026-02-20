# Audit Remediation Design — Release Readiness Fixes (v2 — post-review)

| Field | Value |
|-------|-------|
| **Date** | 2026-02-19 |
| **Branch** | `merged-canonical` @ `cd7af8a` |
| **Audit ref** | `docs/publish-readiness/2026-02-18_2259/` |
| **Findings** | 1 HIGH, 9 MEDIUM, 1 LOW |
| **Review** | Passed code-reviewer agent; all Critical/Important issues addressed in v2 |

## Design Decisions

### Product decision: No auto-approval for vacations
Auto-approval of vacation requests is not a desired feature. All vacations must be manually approved (via page or API). This eliminates FINDING-006 as a side-effects problem and turns it into a code-removal task.

### Assumption: No pre-existing API-created users
This is a pre-production system (air-gapped, not yet deployed). No users have been created through the external API yet, so no migration of HMACSHA512 hashes is needed.

---

## Fix 1 — FINDING-009 (HIGH): Grant Reconciliation on Role Change

**Problem:** When a user's role is demoted (e.g., Manager → Employee) via `OnPostRoleAsync`, the old role's grants remain in the `Grants` table permanently. The login-time backfill is gated by `!hasAnyGrants` and never fires for users who already have grants. This is a permanent privilege escalation.

**File:** `Pages/Admin/Users.cshtml.cs` — `OnPostRoleAsync` (line 773)

**Fix:** After saving the new role (after line ~918, before the DirectorCompany block), add:
```csharp
// Grant reconciliation: remove old role's auto-grants, assign new role's auto-grants
var oldTemplateId = oldRoleTemplateId;
if (!oldTemplateId.HasValue)
{
    // Fallback for legacy users without a RoleTemplateId: resolve from old role enum
    var oldTemplateKey = MapUserRoleToRoleTemplateKey(oldRole, u.JobType?.Name);
    var oldTemplate = await _roleService.GetRoleTemplateByKeyAsync(oldTemplateKey);
    oldTemplateId = oldTemplate?.Id;
}
if (oldTemplateId.HasValue)
    await _grantService.RemoveAutoGrantsAsync(u.Id, oldTemplateId.Value);

// Assign new role's auto-grants
var newTemplateKey = selectedTemplate?.Key ?? MapUserRoleToRoleTemplateKey(targetRole, u.JobType?.Name);
var newGrantScope = await BuildGrantScopeForTemplateAsync(newTemplateKey, u.CompanyId, u.JobTypeId);
await _grantService.AssignRoleTemplateGrantsAsync(u.Id, newTemplateKey, newGrantScope, currentUserId);
```

**Dependencies:** `IGrantService` and `IRoleService` are already injected. `BuildGrantScopeForTemplateAsync` (line ~1957) and `MapUserRoleToRoleTemplateKey` already exist in the same class. `oldRoleTemplateId` is captured at line 889.

**Reviewer fix:** Added fallback for legacy users with null `RoleTemplateId` — resolves old template via `MapUserRoleToRoleTemplateKey(oldRole)` before calling `RemoveAutoGrantsAsync`.

---

## Fix 2 — FINDING-011 (MEDIUM): API Password Hashing Mismatch

**Problem:** `UserApiService` uses HMACSHA512 (64-byte hash, 128-byte salt) while `PasswordHasher` uses PBKDF2-SHA256 (32-byte hash, 16-byte salt). `PasswordHasher.Verify` compares arrays of different lengths → always false. API-created users cannot log in.

**File:** `Services/Api/UserApiService.cs` — lines 155-167

**Fix:** Replace both HMACSHA512 branches:
```csharp
// "has password" branch (line 157) — replace:
using var hmac = new System.Security.Cryptography.HMACSHA512();
user.PasswordSalt = hmac.Key;
user.PasswordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
// with:
var (hash, salt) = PasswordHasher.CreateHash(password);
user.PasswordHash = hash;
user.PasswordSalt = salt;

// "no password" branch (line 164) — same replacement:
var (hash, salt) = PasswordHasher.CreateHash(Guid.NewGuid().ToString());
user.PasswordHash = hash;
user.PasswordSalt = salt;
```

`using ShiftManager.Models;` is already imported (line 3). `PasswordHash` and `PasswordSalt` are both `byte[]` on `AppUser` (lines 19-20).

**Reviewer note:** No migration needed — pre-production assumption documented above.

---

## Fix 3 — FINDING-002 (MEDIUM): Missing Shift Assignment Grant Check

**Problem:** `OnPostAssignEmployeeAsync` in `Calendar/Table.cshtml.cs` has `[Authorize]` (authentication) but no grant check (authorization). Any authenticated user who crafts a POST can assign shifts.

**File:** `Pages/Calendar/Table.cshtml.cs` — `OnPostAssignEmployeeAsync` (line 531)

**Reviewer fix — Critical:** `IGrantService` is NOT currently injected in `Table.cshtml.cs`. Must be added to constructor. Also, there is no generic "ManageShifts" grant key. The grant system uses type-specific keys: `AssignAlhutShifts`, `AssignTextShifts`, `AssignBRShifts`, `AssignTechShifts`, `AssignHanavaShifts`, `AssignDeltaShifts`, `AssignYekevShifts`, `AssignMoviltechShifts`.

**Fix strategy — Dynamic grant resolution from ShiftType:**
1. Add `IGrantService _grantService` to `Table.cshtml.cs` constructor
2. After `companyId` resolution (line 535), resolve the correct grant key from the ShiftType:
```csharp
// Parse current user ID
if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
    return new JsonResult(new { success = false, error = "Invalid user session" }) { StatusCode = 401 };

// Resolve grant key from shift type
var shiftType = await _db.ShiftTypes.FindAsync(request.ShiftTypeId);
if (shiftType == null)
    return new JsonResult(new { success = false, error = "Shift type not found" });

var grantKey = ResolveShiftAssignmentGrantKey(shiftType);
var canAssign = await _grantService.HasGrantWithScopeAsync(currentUserId, grantKey, companyId: companyId);
if (!canAssign)
    return new JsonResult(new { success = false, error = "Insufficient permissions" }) { StatusCode = 403 };
```

3. Add helper method to resolve grant key from ShiftType:
```csharp
private static string ResolveShiftAssignmentGrantKey(ShiftType shiftType)
{
    // Tech shifts have their own specific grant keys
    if (!string.IsNullOrEmpty(shiftType.TechShiftType))
    {
        return shiftType.TechShiftType switch
        {
            ShiftType.TECH_HANAVA => "AssignHanavaShifts",
            ShiftType.TECH_DELTA => "AssignDeltaShifts",
            ShiftType.TECH_YEKEV => "AssignYekevShifts",
            ShiftType.TECH_MOVILTECH => "AssignMoviltechShifts",
            _ => "AssignTechShifts"  // Generic fallback for tech shifts
        };
    }

    // Workforce shifts resolve by JobType name convention
    // JobType names map to grant keys: "Alhut" → AssignAlhutShifts, "Text" → AssignTextShifts, "BR" → AssignBRShifts
    // Fall back to AdminAccess for unknown types
    return "AdminAccess";
}
```

**Note:** The exact JobType → grant key mapping needs to be verified during implementation by reading the JobType seed data. The method above is a skeleton.

**Scope:** Apply the same grant check pattern to `OnPostUnassignEmployeeAsync` and other mutating POST handlers in Table.cshtml.cs.

---

## Fix 4+6 — FINDING-004 + FINDING-006 (MEDIUM): Vacation Approval Consolidation

**Problem (004):** `TimeOffApiService.ApproveTimeOffRequestAsync` sets status to Approved but doesn't remove overlapping shifts, cancel trainee shadowing, or send notification.

**Problem (006):** `VacationApprovalService.SubmitForApprovalAsync` auto-approves short vacations with no side effects. Auto-approval is not a desired feature.

### Part A: Remove auto-approval (FINDING-006)

**File:** `Services/VacationApprovalService.cs` — `SubmitForApprovalAsync` (lines 114-127)

**Change:** Remove the auto-approval branch (`if (rule != null && rule.MaxAutoApproveDays > 0 && ...)`). All requests proceed to pending approval routing.

### Part B: Extract shared approval side effects

**File:** `Services/VacationApprovalService.cs`

**New constructor dependencies:**
- `INotificationService _notificationService`
- `ITraineeService _traineeService`

**New method on `IVacationApprovalService` interface and `VacationApprovalService`:**
```csharp
/// <summary>
/// Processes all side effects that should occur when a time-off request is approved:
/// shift removal, trainee shadowing cancellation, and notification.
/// </summary>
Task ProcessApprovalSideEffectsAsync(int requestId);
```

**Implementation:**
```csharp
public async Task ProcessApprovalSideEffectsAsync(int requestId)
{
    // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific requestId; cross-company side effects needed for Directors
    var request = await _context.TimeOffRequests.IgnoreQueryFilters()
        .FirstOrDefaultAsync(r => r.Id == requestId);
    if (request == null) return;

    // 1. Remove overlapping shift assignments
    var assignments = await (from a in _context.ShiftAssignments.IgnoreQueryFilters()
                             join si in _context.ShiftInstances.IgnoreQueryFilters()
                                on a.ShiftInstanceId equals si.Id
                             where a.UserId == request.UserId
                                && si.WorkDate >= request.StartDate
                                && si.WorkDate <= request.EndDate
                             select a).ToListAsync();
    if (assignments.Any())
        _context.ShiftAssignments.RemoveRange(assignments);

    // 2. Cancel trainee shadowing if user is a trainee
    var user = await _context.Users.IgnoreQueryFilters()
        .FirstOrDefaultAsync(u => u.Id == request.UserId);
    if (user?.Role == UserRole.Trainee)
    {
        var startDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDate = request.EndDate.ToDateTime(TimeOnly.MaxValue);
        await _traineeService.CancelShadowingForTimeOffAsync(request.UserId, startDate, endDate);
    }

    // 3. Send approval notification
    await _notificationService.CreateTimeOffNotificationAsync(
        request.UserId, RequestStatus.Approved,
        request.StartDate, request.EndDate, request.Id);

    await _context.SaveChangesAsync();
}
```

**Verified interfaces:**
- `INotificationService.CreateTimeOffNotificationAsync(int userId, RequestStatus status, DateOnly startDate, DateOnly endDate, int requestId)` — confirmed at NotificationService.cs line 18/176
- `ITraineeService.CancelShadowingForTimeOffAsync(int traineeUserId, DateTime startDate, DateTime endDate)` — confirmed at ITraineeService.cs line 36

### Part C: Refactor page handler

**File:** `Pages/Requests/Index.cshtml.cs` — `OnPostApproveTimeOffAsync` (lines 216-247)

**Change:** Replace inline shift removal (218-225), trainee cancellation (228-234), save (236-244), and notification (247) with:
```csharp
// Process approval side effects (shift removal, trainee cancel, notification)
await _vacationApprovalService.ProcessApprovalSideEffectsAsync(id);
```

The status change and authorization check remain in the page handler. Only the side effects are delegated.

**Prerequisite:** Verify `IVacationApprovalService` is already injected in `Requests/Index.cshtml.cs`. If not, add to constructor.

### Part D: Wire API approval

**File:** `Services/Api/TimeOffApiService.cs` — `ApproveTimeOffRequestAsync` (line 183)

**Change:** Inject `IVacationApprovalService` into `TimeOffApiService` constructor. After setting status and saving (line 202), add:
```csharp
await _vacationApprovalService.ProcessApprovalSideEffectsAsync(requestId);
```

---

## Fix 5 — FINDING-005 (MEDIUM): Vacation Overlap Check on Page

**Problem:** `Requests/TimeOff/Create.cshtml.cs` `OnPostAsync` validates dates but doesn't check for overlapping existing requests. The API version has this check.

**File:** `Pages/Requests/TimeOff/Create.cshtml.cs` — `OnPostAsync` (after userId resolution at line 84, before `_db.TimeOffRequests.Add` at line 86)

**Fix:**
```csharp
var hasOverlap = await _db.TimeOffRequests
    .Where(r => r.UserId == userId
        && r.Status != RequestStatus.Declined
        && r.Status != RequestStatus.Canceled
        && r.StartDate <= EndDate
        && r.EndDate >= StartDate)
    .AnyAsync();
if (hasOverlap)
{
    ModelState.AddModelError("", _localizer["Error_OverlappingTimeOffRequest"]);
    return Page();
}
```

**Verified:** `RequestStatus.Declined` (enum value 2) and `RequestStatus.Canceled` (enum value 3) both exist in `Models/Support/Enums.cs`.

**Localization keys to add:**
- EN: `"Error_OverlappingTimeOffRequest"` → `"You already have a time-off request for overlapping dates."`
- HE: `"Error_OverlappingTimeOffRequest"` → `"כבר קיימת בקשת חופש עבור תאריכים חופפים."`

No additional DI needed.

---

## Fix 6 — FINDING-007 (MEDIUM): Global Email Uniqueness

**Problem:** Email uniqueness check in admin user creation is tenant-scoped. Same email can be created in different companies, causing a raw 500 from the DB unique index.

**Files:** `Pages/Admin/Users.cshtml.cs`

**Fix location 1 — OnPostAddAsync (line ~507):**
```csharp
if (await _db.Users
    .IgnoreQueryFilters()  // SECURITY-AUDITED: Global email uniqueness for login identifier
    .AnyAsync(u => u.Email == NewEmail))
```

**Fix location 2 — OnPostApproveJoinRequestAsync (line 1375):**
```csharp
if (await _db.Users
    .IgnoreQueryFilters()  // SECURITY-AUDITED: Global email uniqueness for login identifier
    .AnyAsync(u => u.Email == joinRequest.Email))
```

**Reviewer fix — Fix location 3 — OnPostBatchApproveJoinRequestsAsync (line 1685):**
```csharp
if (await _db.Users
    .IgnoreQueryFilters()  // SECURITY-AUDITED: Global email uniqueness for login identifier
    .AnyAsync(u => u.Email == joinRequest.Email))
```

---

## Fix 7 — FINDING-008 (MEDIUM): Audit Log on User Disable/Enable

**Problem:** `OnPostToggleAsync` flips `user.IsActive` with no audit trail.

**File:** `Pages/Admin/Users.cshtml.cs` — `OnPostToggleAsync` (line 730)

**Fix:** After the successful save (after the `if (!saveResult.Success)` block at line 768), add:
```csharp
await _auditLogService.LogAsync(
    u.IsActive ? "UserEnabled" : "UserDisabled",
    "User", u.Id,
    $"User {u.Email} {(u.IsActive ? "enabled" : "disabled")} by admin");
```

**Verified:** `IAuditLogService` is already injected (field at line 29, assigned at line 58). Uses entity type `"User"` to match existing convention (line 720 uses `"User"`).

---

## Fix 8 — FINDING-010 (MEDIUM): RoleAssignmentAudit on Join Approval

**Problem:** When a join request is approved and a user is created, no `RoleAssignmentAudit` entry records the initial role assignment.

**File:** `Pages/Admin/Users.cshtml.cs`

**Fix location 1 — OnPostApproveJoinRequestAsync (after line 1452, after grants are assigned):**
```csharp
_db.RoleAssignmentAudits.Add(new RoleAssignmentAudit
{
    ChangedBy = currentUserId,
    TargetUserId = newUser.Id,
    FromRole = null,  // Initial assignment — no previous role
    ToRole = newUser.Role,
    FromRoleTemplateId = null,
    ToRoleTemplateId = newUser.RoleTemplateId,
    CompanyId = newUser.CompanyId,
    Timestamp = DateTime.UtcNow
});
```

**Verified:** `RoleAssignmentAudit.FromRole` is `UserRole?` (nullable) — confirmed at `Models/RoleAssignmentAudit.cs` line 10.

**Reviewer fix — Fix location 2 — OnPostBatchApproveJoinRequestsAsync (after line 1770, after grants are assigned):**
```csharp
_db.RoleAssignmentAudits.Add(new RoleAssignmentAudit
{
    ChangedBy = currentUserId,
    TargetUserId = newUser.Id,
    FromRole = null,
    ToRole = newUser.Role,
    FromRoleTemplateId = null,
    ToRoleTemplateId = newUser.RoleTemplateId,
    CompanyId = newUser.CompanyId,
    Timestamp = DateTime.UtcNow
});
```

---

## Fix 9 — FINDING-003 (MEDIUM): Past-Date Warning in Shift Assignment

**Problem:** `ValidateShiftAssignmentAsync` has 8 validation checks but none for past dates. Back-filling is intentional, so this should be a warning (not a hard error).

**File:** `Services/ShiftAssignmentService.cs` — `ValidateShiftAssignmentAsync`

**Reviewer fix — Corrected constructor:** `ValidationIssue` takes 4 parameters: `(Key, Message, Severity, Category)`. The local variable is `warnings` (line 178), not `result.Warnings`.

**Fix:** Add after existing validations (before the return statement):
```csharp
if (shiftInstance.WorkDate < DateOnly.FromDateTime(DateTime.UtcNow))
{
    warnings.Add(new ValidationIssue(
        "PAST_DATE",
        _localizer["Warning_PastDateShiftAssignment"],
        ValidationSeverity.Warning,
        ValidationCategory.Concurrency));
}
```

**ValidationCategory:** Using `Concurrency` as the closest existing category (temporal concern). `ValidationCategory` enum: `{ JobType, ShiftGrouping, WeeklyHours, RestHours, Trainee, Concurrency, TechShift }` — at `IShiftAssignmentService.cs` line 12.

**Localization keys to add:**
- EN: `"Warning_PastDateShiftAssignment"` → `"This shift date has already passed. Assignment will modify historical records."`
- HE: `"Warning_PastDateShiftAssignment"` → `"תאריך המשמרת כבר עבר. השיבוץ ישנה רשומות היסטוריות."`

---

## Fix 10 — FINDING-001 (LOW): Version Endpoint Bypass

**Problem:** `/api/v1/version` returns 401 because `ApiAuthenticationMiddleware` intercepts all `/api/*` paths before endpoint authorization runs.

**File:** `Middleware/ApiAuthenticationMiddleware.cs` — `InvokeAsync` (after line 32, before the `IsInternalWebUiEndpoint` check)

**Fix:** Add anonymous bypass:
```csharp
// Anonymous API endpoints (no auth required)
if (context.Request.Path.StartsWithSegments("/api/v1/version", StringComparison.OrdinalIgnoreCase))
{
    await _next(context);
    return;
}
```

---

## Execution Order (dependency-aware)

| Step | Finding | Severity | Files Modified |
|------|---------|----------|---------------|
| 1 | 009 | HIGH | `Pages/Admin/Users.cshtml.cs` |
| 2 | 011 | MEDIUM | `Services/Api/UserApiService.cs` |
| 3 | 004+006 | MEDIUM | `Services/VacationApprovalService.cs`, `Services/IVacationApprovalService.cs`, `Pages/Requests/Index.cshtml.cs`, `Services/Api/TimeOffApiService.cs` |
| 4 | 002 | MEDIUM | `Pages/Calendar/Table.cshtml.cs` |
| 5 | 005 | MEDIUM | `Pages/Requests/TimeOff/Create.cshtml.cs` + localization |
| 6 | 007 | MEDIUM | `Pages/Admin/Users.cshtml.cs` (3 locations) |
| 7 | 008 | MEDIUM | `Pages/Admin/Users.cshtml.cs` |
| 8 | 010 | MEDIUM | `Pages/Admin/Users.cshtml.cs` (2 locations) |
| 9 | 003 | MEDIUM | `Services/ShiftAssignmentService.cs` + localization |
| 10 | 001 | LOW | `Middleware/ApiAuthenticationMiddleware.cs` |

## Verification Strategy

After all fixes:
1. `dotnet build --no-restore` — must be 0 warnings, 0 errors
2. `dotnet test` — must be 236/236 passing (or more if tests are added)
3. Runtime spot-check: boot app, verify `/api/v1/version` returns 200
