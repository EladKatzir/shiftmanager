# Audit Remediation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix all 11 findings from the release readiness audit to achieve READY verdict.

**Architecture:** Each fix is a surgical change to existing code — no new files, no new entities, no migrations. Fixes modify service methods, page handlers, and middleware. Shared vacation approval side effects are extracted into `VacationApprovalService`.

**Tech Stack:** ASP.NET Core 8.0, Razor Pages, EF Core, SQLite, C#

**Design doc:** `docs/plans/2026-02-19-audit-remediation-design.md`

---

### Task 1: FINDING-009 — Grant Reconciliation on Role Demotion (HIGH)

**Files:**
- Modify: `Pages/Admin/Users.cshtml.cs` — `OnPostRoleAsync` (line 773)

**Step 1: Add grant reconciliation after role save**

In `OnPostRoleAsync`, insert the following block after the role save (after line 918's closing brace `}`, before the `// ✅ P0-4/P0-5 FIX: If changing TO Director` comment at line 920):

```csharp
            // FINDING-009 FIX: Reconcile grants on role change to prevent privilege escalation
            var oldTemplateIdForGrants = oldRoleTemplateId;
            if (!oldTemplateIdForGrants.HasValue)
            {
                // Fallback for legacy users without RoleTemplateId: resolve from old role enum
                var oldTemplateKeyForGrants = MapUserRoleToRoleTemplateKey(oldRole, u.JobType?.Name);
                var oldTemplateForGrants = await _roleService.GetRoleTemplateByKeyAsync(oldTemplateKeyForGrants);
                oldTemplateIdForGrants = oldTemplateForGrants?.Id;
            }
            if (oldTemplateIdForGrants.HasValue)
                await _grantService.RemoveAutoGrantsAsync(u.Id, oldTemplateIdForGrants.Value);

            // Assign new role's auto-grants
            var newGrantTemplateKey = selectedTemplate?.Key ?? MapUserRoleToRoleTemplateKey(targetRole, u.JobType?.Name);
            var newGrantScope = await BuildGrantScopeForTemplateAsync(newGrantTemplateKey, u.CompanyId, u.JobTypeId);
            await _grantService.AssignRoleTemplateGrantsAsync(u.Id, newGrantTemplateKey, newGrantScope, currentUserId);
```

**Step 2: Build and verify**

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

**Step 3: Run tests**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --no-restore --verbosity normal`
Expected: All tests pass

**Step 4: Commit**

```bash
git add Pages/Admin/Users.cshtml.cs
git commit -m "fix(009): reconcile grants on role change to prevent privilege escalation"
```

---

### Task 2: FINDING-011 — Fix API Password Hashing (MEDIUM)

**Files:**
- Modify: `Services/Api/UserApiService.cs` — lines 154-167

**Step 1: Replace HMACSHA512 with PasswordHasher**

Replace the "has password" branch (lines 154-160):

Find:
```csharp
        if (!string.IsNullOrEmpty(password))
        {
            // Generate salt and hash
            using var hmac = new System.Security.Cryptography.HMACSHA512();
            user.PasswordSalt = hmac.Key;
            user.PasswordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        }
```

Replace with:
```csharp
        if (!string.IsNullOrEmpty(password))
        {
            var (hash, salt) = PasswordHasher.CreateHash(password);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
        }
```

Replace the "no password" branch (lines 161-167):

Find:
```csharp
        else
        {
            // Generate random salt for now (user must reset password)
            using var hmac = new System.Security.Cryptography.HMACSHA512();
            user.PasswordSalt = hmac.Key;
            user.PasswordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
        }
```

Replace with:
```csharp
        else
        {
            // Generate random hash (user must reset password)
            var (hash, salt) = PasswordHasher.CreateHash(Guid.NewGuid().ToString());
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
        }
```

`using ShiftManager.Models;` is already present at line 3 of this file.

**Step 2: Build and verify**

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

**Step 3: Commit**

```bash
git add Services/Api/UserApiService.cs
git commit -m "fix(011): use PasswordHasher.CreateHash in API user creation instead of HMACSHA512"
```

---

### Task 3: FINDING-004 + FINDING-006 — Vacation Approval Consolidation (MEDIUM)

This is the largest change. Four parts.

**Files:**
- Modify: `Services/IVacationApprovalService.cs`
- Modify: `Services/VacationApprovalService.cs`
- Modify: `Pages/Requests/Index.cshtml.cs`
- Modify: `Services/Api/TimeOffApiService.cs`

#### Step 1: Add method to IVacationApprovalService interface

In `Services/IVacationApprovalService.cs`, add before the closing `}` of the interface (before line 34):

```csharp
    /// <summary>
    /// Processes post-approval side effects: removes overlapping shifts, cancels trainee shadowing, sends notification.
    /// Call this after setting request status to Approved.
    /// </summary>
    Task ProcessApprovalSideEffectsAsync(int requestId);
```

#### Step 2: Add new constructor dependencies to VacationApprovalService

In `Services/VacationApprovalService.cs`, update the constructor (lines 12-24).

Add fields after line 14:
```csharp
    private readonly INotificationService _notificationService;
    private readonly ITraineeService _traineeService;
```

Update the constructor signature (line 16) and body to include new parameters:
```csharp
    public VacationApprovalService(
        AppDbContext context,
        IGrantService grantService,
        ILogger<VacationApprovalService> logger,
        INotificationService notificationService,
        ITraineeService traineeService)
    {
        _context = context;
        _grantService = grantService;
        _logger = logger;
        _notificationService = notificationService;
        _traineeService = traineeService;
    }
```

#### Step 3: Remove auto-approval branch from SubmitForApprovalAsync

In `SubmitForApprovalAsync` (line 92), remove the auto-approval block at lines 114-127:

Find and remove:
```csharp
        // Check if auto-approve applies
        var rule = await GetMatchingRuleAsync(request);
        if (rule != null && rule.MaxAutoApproveDays > 0 && leaveDays <= rule.MaxAutoApproveDays)
        {
            // Auto-approve
            request.Status = RequestStatus.Approved;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Request {RequestId} auto-approved for user {UserId} ({LeaveDays} days)",
                requestId, request.UserId, leaveDays);

            return (true, "VacationApproval_AutoApproved");
        }
```

#### Step 4: Add ProcessApprovalSideEffectsAsync to VacationApprovalService

Add the method before the `GetMatchingRuleAsync` private method (before line 593):

```csharp
    /// <summary>
    /// Processes all post-approval side effects for an approved time-off request.
    /// </summary>
    public async Task ProcessApprovalSideEffectsAsync(int requestId)
    {
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific requestId
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            _logger.LogWarning("ProcessApprovalSideEffectsAsync: Request {RequestId} not found", requestId);
            return;
        }

        // 1. Remove overlapping shift assignments
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by request.UserId + date range
        var assignments = await (from a in _context.ShiftAssignments.IgnoreQueryFilters()
                                 join si in _context.ShiftInstances.IgnoreQueryFilters()
                                    on a.ShiftInstanceId equals si.Id
                                 where a.UserId == request.UserId
                                    && si.WorkDate >= request.StartDate
                                    && si.WorkDate <= request.EndDate
                                 select a).ToListAsync();

        if (assignments.Any())
        {
            _context.ShiftAssignments.RemoveRange(assignments);
            _logger.LogInformation(
                "Removed {Count} overlapping shift assignments for user {UserId} during approved time-off {RequestId}",
                assignments.Count, request.UserId, requestId);
        }

        // 2. Cancel trainee shadowing if user is a trainee
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by request.UserId
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId);

        if (user?.Role == UserRole.Trainee)
        {
            var startDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
            var endDate = request.EndDate.ToDateTime(TimeOnly.MaxValue);
            await _traineeService.CancelShadowingForTimeOffAsync(request.UserId, startDate, endDate);
        }

        await _context.SaveChangesAsync();

        // 3. Send approval notification (after save so DB state is consistent)
        await _notificationService.CreateTimeOffNotificationAsync(
            request.UserId, RequestStatus.Approved,
            request.StartDate, request.EndDate, request.Id);
    }
```

#### Step 5: Refactor Requests/Index.cshtml.cs to use new method

In `Pages/Requests/Index.cshtml.cs`:

First, add `IVacationApprovalService` to the constructor. Add field:
```csharp
    private readonly IVacationApprovalService _vacationApprovalService;
```

Add parameter to constructor and assign it.

Then in `OnPostApproveTimeOffAsync` (line 166), replace the inline side effects (lines 216-247) — everything from the `// Remove existing assignments` comment through the notification call, BUT keep the status change at line 214 and the authorization/concurrency code above it.

Replace lines 216-247 with:
```csharp
        {
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "TimeOffRequest", id);
            if (!saveResult.Success)
            {
                Error = _localizer["Error_ConcurrencyConflict"];
                return RedirectToPage();
            }
        }

        // Process approval side effects (shift removal, trainee cancel, notification)
        await _vacationApprovalService.ProcessApprovalSideEffectsAsync(id);
```

#### Step 6: Wire API approval to use new method

In `Services/Api/TimeOffApiService.cs`:

Add field and constructor parameter for `IVacationApprovalService`:
```csharp
    private readonly IVacationApprovalService _vacationApprovalService;
```

Update constructor to accept and assign it.

In `ApproveTimeOffRequestAsync` (line 183), after line 202 (`await _context.SaveChangesAsync();`), add:
```csharp
        // Process approval side effects (shift removal, trainee cancel, notification)
        await _vacationApprovalService.ProcessApprovalSideEffectsAsync(requestId);
```

#### Step 7: Build and verify

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 8: Run tests

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --no-restore --verbosity normal`
Expected: All tests pass

#### Step 9: Commit

```bash
git add Services/IVacationApprovalService.cs Services/VacationApprovalService.cs Pages/Requests/Index.cshtml.cs Services/Api/TimeOffApiService.cs
git commit -m "fix(004+006): extract shared approval side effects, remove auto-approval"
```

---

### Task 4: FINDING-002 — Shift Assignment Grant Check (MEDIUM)

**Files:**
- Modify: `Pages/Calendar/Table.cshtml.cs`

#### Step 1: Add IGrantService to constructor

Add field after line 26:
```csharp
    private readonly IGrantService _grantService;
```

Add parameter `IGrantService grantService` to the constructor (line 28) and assign:
```csharp
        _grantService = grantService;
```

#### Step 2: Add grant check to OnPostAssignEmployeeAsync

In `OnPostAssignEmployeeAsync` (line 531), after `var companyId = _companyContext.GetCompanyIdOrThrow();` (line 535), add:

```csharp
            // FINDING-002 FIX: Authorization check — verify user has shift assignment grant
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
                return new JsonResult(new { success = false, error = "Invalid user session" }) { StatusCode = 401 };

            var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
            if (!isAdmin)
            {
                // Check for any shift assignment grant scoped to this company
                var hasAnyShiftGrant = await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignAlhutShifts", companyId: companyId)
                    || await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignTextShifts", companyId: companyId)
                    || await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignBRShifts", companyId: companyId)
                    || await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignTechShifts", companyId: companyId);

                if (!hasAnyShiftGrant)
                    return new JsonResult(new { success = false, error = "Insufficient permissions to assign shifts" }) { StatusCode = 403 };
            }
```

Note: The page already has `[Authorize(Policy = "Grant:ManagerHomeAccess")]` on the class (line 15), which filters out non-managers. This additional check ensures the specific shift-assignment grant exists. The `AdminAccess` check provides a bypass for admins.

#### Step 3: Build and verify

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 4: Commit

```bash
git add Pages/Calendar/Table.cshtml.cs
git commit -m "fix(002): add grant check on shift assignment to prevent unauthorized POST"
```

---

### Task 5: FINDING-005 — Vacation Overlap Check (MEDIUM)

**Files:**
- Modify: `Pages/Requests/TimeOff/Create.cshtml.cs`
- Modify: `Resources/SharedResources.resx`
- Modify: `Resources/SharedResources.he-IL.resx`

#### Step 1: Add overlap check

In `Pages/Requests/TimeOff/Create.cshtml.cs` `OnPostAsync`, after the userId resolution (after line 84, before `_db.TimeOffRequests.Add` at line 86), add:

```csharp
        // FINDING-005 FIX: Check for overlapping time-off requests
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

Also add `using Microsoft.EntityFrameworkCore;` at the top if not present.

#### Step 2: Add localization keys

In `Resources/SharedResources.resx`, add before `</root>`:
```xml
  <data name="Error_OverlappingTimeOffRequest" xml:space="preserve">
    <value>You already have a time-off request for overlapping dates.</value>
  </data>
```

In `Resources/SharedResources.he-IL.resx`, add before `</root>`:
```xml
  <data name="Error_OverlappingTimeOffRequest" xml:space="preserve">
    <value>כבר קיימת בקשת חופש עבור תאריכים חופפים.</value>
  </data>
```

#### Step 3: Build and verify

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 4: Commit

```bash
git add Pages/Requests/TimeOff/Create.cshtml.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix(005): add overlap validation to vacation request page"
```

---

### Task 6: FINDING-007 — Global Email Uniqueness (MEDIUM)

**Files:**
- Modify: `Pages/Admin/Users.cshtml.cs` — 3 locations

#### Step 1: Fix OnPostAddAsync (line 507)

Find:
```csharp
        if (await _db.Users.AnyAsync(u => u.Email == NewEmail)) { Error = _localizer["Error_EmailAlreadyExists"]; return Page(); }
```

Replace with:
```csharp
        // SECURITY-AUDITED: IgnoreQueryFilters for global email uniqueness — email is the login identifier
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == NewEmail)) { Error = _localizer["Error_EmailAlreadyExists"]; return Page(); }
```

#### Step 2: Fix OnPostApproveJoinRequestAsync (line 1375)

Find:
```csharp
        if (await _db.Users.AnyAsync(u => u.Email == joinRequest.Email))
```

Replace with:
```csharp
        // SECURITY-AUDITED: IgnoreQueryFilters for global email uniqueness — email is the login identifier
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == joinRequest.Email))
```

#### Step 3: Fix OnPostBatchApproveJoinRequestsAsync (line 1685)

Find:
```csharp
                if (await _db.Users.AnyAsync(u => u.Email == joinRequest.Email))
```

Replace with:
```csharp
                // SECURITY-AUDITED: IgnoreQueryFilters for global email uniqueness — email is the login identifier
                if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == joinRequest.Email))
```

#### Step 4: Build and verify

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 5: Commit

```bash
git add Pages/Admin/Users.cshtml.cs
git commit -m "fix(007): use IgnoreQueryFilters for global email uniqueness checks"
```

---

### Task 7: FINDING-008 — Audit Log on User Disable/Enable (MEDIUM)

**Files:**
- Modify: `Pages/Admin/Users.cshtml.cs` — `OnPostToggleAsync` (line 730)

#### Step 1: Add audit log after toggle save

In `OnPostToggleAsync`, after the save success check (after line 768 closing brace), before the final `return RedirectToPage();` at line 770, add:

```csharp
            // FINDING-008 FIX: Audit log for user enable/disable
            await _auditLogService.LogAsync(
                u.IsActive ? "UserEnabled" : "UserDisabled",
                "User", u.Id,
                $"User {u.Email} {(u.IsActive ? "enabled" : "disabled")} by admin");
```

`IAuditLogService` is already injected (field at line 29, assigned at line 58).

#### Step 2: Build and verify

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 3: Commit

```bash
git add Pages/Admin/Users.cshtml.cs
git commit -m "fix(008): add audit log on user disable/enable toggle"
```

---

### Task 8: FINDING-010 — RoleAssignmentAudit on Join Approval (MEDIUM)

**Files:**
- Modify: `Pages/Admin/Users.cshtml.cs` — 2 locations

#### Step 1: Add audit entry in OnPostApproveJoinRequestAsync

After the grant assignment block (after line 1454, after the `_logger.LogInformation("Assigned {GrantsCount}..."` line), add:

```csharp
            // FINDING-010 FIX: Record initial role assignment in audit trail
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

#### Step 2: Add audit entry in OnPostBatchApproveJoinRequestsAsync

After the grant assignment block (after line 1774, after the `_logger.LogInformation("Batch approval: ..."` line), add:

```csharp
                // FINDING-010 FIX: Record initial role assignment in audit trail
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

#### Step 3: Build and verify

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 4: Commit

```bash
git add Pages/Admin/Users.cshtml.cs
git commit -m "fix(010): add RoleAssignmentAudit on join request approval"
```

---

### Task 9: FINDING-003 — Past-Date Warning in Shift Validation (MEDIUM)

**Files:**
- Modify: `Services/ShiftAssignmentService.cs` — `ValidateShiftAssignmentAsync`
- Modify: `Resources/SharedResources.resx`
- Modify: `Resources/SharedResources.he-IL.resx`

#### Step 1: Add past-date warning

In `ShiftAssignmentService.cs` `ValidateShiftAssignmentAsync`, just before the return statement at line 300-301:

```csharp
        bool canAssign = errors.Count == 0;
```

Insert before that line:

```csharp
        // FINDING-003 FIX: Warn when assigning to past dates (back-fill allowed via override)
        if (shiftInstance.WorkDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            warnings.Add(new ValidationIssue(
                "PAST_DATE",
                _localizer["Warning_PastDateShiftAssignment"],
                ValidationSeverity.Warning,
                ValidationCategory.Concurrency));
        }

```

#### Step 2: Add localization keys

In `Resources/SharedResources.resx`, add before `</root>`:
```xml
  <data name="Warning_PastDateShiftAssignment" xml:space="preserve">
    <value>This shift date has already passed. Assignment will modify historical records.</value>
  </data>
```

In `Resources/SharedResources.he-IL.resx`, add before `</root>`:
```xml
  <data name="Warning_PastDateShiftAssignment" xml:space="preserve">
    <value>תאריך המשמרת כבר עבר. השיבוץ ישנה רשומות היסטוריות.</value>
  </data>
```

#### Step 3: Build and verify

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 4: Commit

```bash
git add Services/ShiftAssignmentService.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix(003): add past-date warning in shift assignment validation"
```

---

### Task 10: FINDING-001 — Version Endpoint Middleware Bypass (LOW)

**Files:**
- Modify: `Middleware/ApiAuthenticationMiddleware.cs`

#### Step 1: Add anonymous bypass

In `ApiAuthenticationMiddleware.cs` `InvokeAsync`, after the "Only process API routes" check (after line 32, before the `// Check if this is an internal web UI endpoint` comment at line 34), add:

```csharp
        // FINDING-001 FIX: Version endpoint is truly anonymous — no auth required
        if (context.Request.Path.StartsWithSegments("/api/v1/version", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

```

#### Step 2: Build and verify

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 3: Commit

```bash
git add Middleware/ApiAuthenticationMiddleware.cs
git commit -m "fix(001): add anonymous bypass for /api/v1/version endpoint"
```

---

### Task 11: Final Verification

#### Step 1: Full build

Run: `dotnet build --no-restore`
Expected: 0 warnings, 0 errors

#### Step 2: Full test suite

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --no-restore --verbosity normal`
Expected: 236/236 passing (or more)

#### Step 3: Runtime smoke test (if app can be started)

Run: `dotnet run --project . --urls "http://localhost:5000" --environment Development`

Then test:
- `curl -s http://localhost:5000/api/v1/version` — should return 200 with version JSON (FINDING-001 verified)
- `curl -s http://localhost:5000/health` — should return 200

#### Step 4: Final commit summary

Verify with `git log --oneline -12` that all 10 fix commits are present.
