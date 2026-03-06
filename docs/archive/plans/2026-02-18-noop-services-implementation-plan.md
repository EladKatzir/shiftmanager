# No-Op Services — Implementation Plan

**Design Doc:** `docs/plans/2026-02-18-noop-services-design.md`
**Review Fixes:** All HIGH/MEDIUM issues from Opus review incorporated below.
**Execution:** Parallel Opus 4.6 agents in dependency-aware waves.

---

## Review Fixes Applied

| # | Issue | Fix |
|---|-------|-----|
| H1 | RoleService/JobTypeService no IgnoreQueryFilters | Add to ALL queries in both services (Wave 0) |
| H2 | GrantService circular dep (RoleService→IGrantService→IRoleService) | EXCLUDE GrantService from IRoleService wiring; keep direct _db access |
| H3 | ConcurrencySaveResult.ErrorMessage not .Message | Fixed in all agent prompts |
| H4 | ILocalizationService needs DateOnly/TimeOnly overloads | Add 2 overloads (Wave 0) |
| H5 | FormatDate returns "02/18" but codebase uses "MMM dd, yyyy" | Add FormatMediumDate method (Wave 0) |
| H6 | MailService.cs missing (15+ format strings) | Added to Agent 6 scope |
| M1 | 4 missing view files | Added: NotificationCenter, Feedback, Home/Index, Owner/Permissions |
| M2 | DataLifecycle.cshtml.cs missing from CompanyCache | Added to Agent 5 scope |
| M3 | CompanyCacheService no IgnoreQueryFilters | Add to query (Wave 0) |
| M4 | ConcurrencyService catches DbUpdateException silently | Remove catch; only handle DbUpdateConcurrencyException (Wave 0) |
| M5 | CompanyCacheService returns AsNoTracking entities | Document: cache = read-only; never mutate returned Company |
| M6 | Test file updates | GrantServiceTests mock not needed (excluded from wiring) |
| M7 | ILocalizationService format mismatch | Use FormatMediumDate (not FormatDate) for "MMM dd, yyyy" display |

---

## Dependency Graph

```
Agent 0 (prerequisites) ──→ Agent 5 (CompanyCache) ──→ Agent 1 (Role) ──→ Agent 2 (JobType) ──┐
                        ├──→ Agent 6 (Localization)                                            ├──→ Agent 4 (Concurrency)
                        └──→ Agent 3 (CalendarNotification) ──────────────────────────────────┘
```

## Execution Schedule

| Round | Agent(s) | Parallel? | Files Modified |
|-------|----------|-----------|----------------|
| 1 | Agent 0: Service prerequisites | Solo | 10 service/interface files + 2 .resx |
| 2 | Agent 5 + Agent 6 + Agent 3 | YES (parallel) | 14 + 22 + 1 files |
| 3 | Agent 1: IRoleService consumers | Solo (after Agent 5) | 14 files |
| 4 | Agent 2: IJobTypeService consumers | Solo (after Agent 1) | 15 files |
| 5 | Agent 4: IConcurrencyService consumers | Solo (after Agent 2 + Agent 3) | 7 files |
| 6 | Build + Test | Solo | Verify 0W/0E + 236/236 |

---

## Agent 0: Service Prerequisites

**Files to modify (12 total):**

### 1. ILocalizationService.cs — Add methods to interface
```csharp
// Expose existing concrete-only methods:
string FormatShortDate(DateTime date);
string FormatLongDate(DateTime date);
string FormatDayOfWeek(DateTime date);
string FormatRelativeTime(DateTime dateTime);

// NEW methods:
string FormatMediumDate(DateTime date);    // "MMM dd, yyyy" (EN) / "dd בMMM yyyy" (HE)
string FormatMediumDate(DateOnly date);    // same for DateOnly
string FormatDate(DateOnly date);          // "MM/dd/yyyy" / "dd/MM/yyyy"
string FormatTime(TimeOnly time);          // "h:mm tt" / "HH:mm"
string FormatShortDate(DateOnly date);     // "MM/dd" / "dd/MM"
string FormatLongDate(DateOnly date);      // "MMMM dd, yyyy" / "dd MMMM yyyy"
```

### 2. LocalizationService.cs — Implement new methods
```csharp
public string FormatMediumDate(DateTime date)
{
    if (IsHebrew)
        return date.ToString("dd MMM yyyy", new CultureInfo("he-IL"));
    return date.ToString("MMM dd, yyyy", CurrentCulture);
}

public string FormatMediumDate(DateOnly date) => FormatMediumDate(date.ToDateTime(TimeOnly.MinValue));
public string FormatDate(DateOnly date) => FormatDate(date.ToDateTime(TimeOnly.MinValue));
public string FormatTime(TimeOnly time) => FormatTime(DateTime.Today.Add(time.ToTimeSpan()));
public string FormatShortDate(DateOnly date) => FormatShortDate(date.ToDateTime(TimeOnly.MinValue));
public string FormatLongDate(DateOnly date) => FormatLongDate(date.ToDateTime(TimeOnly.MinValue));
```

### 3. IRoleService.cs — Add 5 methods
```csharp
Task<List<RoleTemplate>> GetAssignableRoleTemplatesAsync();
Task<List<RoleTemplate>> GetSignupRoleTemplatesAsync();
Task<RoleTemplate?> GetRoleTemplateWithAutoGrantsAsync(int roleTemplateId);
Task<RoleTemplate?> GetRoleTemplateWithAutoGrantsByKeyAsync(string key);
Task<int> GetActiveRoleTemplateCountAsync();
```

### 4. RoleService.cs — Implement + add IgnoreQueryFilters to ALL queries
Every `_db.RoleTemplates` and `_db.UserRoleAssignments` query must have `.IgnoreQueryFilters()`.

### 5. IJobTypeService.cs — Add 4 methods
```csharp
Task<JobType> CreateJobTypeAsync(string name, int areaId, TimeOnly start, TimeOnly end, bool isActive = true);
Task<bool> ToggleActiveAsync(int jobTypeId);
Task<bool> DeleteJobTypeAsync(int jobTypeId);
Task<int> GetJobTypeCountAsync();
Task<List<JobType>> GetAllJobTypesWithAreaAsync();
```

### 6. JobTypeService.cs — Implement + add IgnoreQueryFilters to ALL queries

### 7. CompanyCacheService.cs — Add IgnoreQueryFilters to query (line 55-57)
```csharp
company = await _db.Companies
    .IgnoreQueryFilters()
    .AsNoTracking()
    .FirstOrDefaultAsync(c => c.Id == companyId);
```

### 8. ConcurrencyService.cs — Remove DbUpdateException catch (lines 68-80)
Only catch `DbUpdateConcurrencyException`. Let other DB errors propagate normally.

### 9-10. SharedResources.resx + SharedResources.he-IL.resx
Add key: `Error_ConcurrencyConflict`
- EN: "This record was modified by another user. Please refresh and try again."
- HE: "רשומה זו שונתה על ידי משתמש אחר. אנא רענן ונסה שוב."

---

## Agent 3: ICalendarNotificationService → Calendar/Table.cshtml.cs

**1 file.** Inject `ICalendarNotificationService` into `TableModel` constructor. After each successful mutation handler's `SaveChangesAsync()`, call the appropriate notify method.

For handlers that only receive `AssignmentId`, join-load `ShiftAssignment → ShiftInstance → ShiftType` to resolve `MoleculeId`/`JobTypeId` for `CalendarGroups.Shifts(moleculeId, jobTypeId)`.

10 handlers: AssignEmployee, UnassignEmployee, ClearAssignment, AddTrainee, RemoveTrainee, ChangeUser, UpdateShiftStaffing, DeleteShiftInstance, CreateCustomShiftType, FillRange.

Notification calls are fire-and-forget (wrap in try/catch, log errors, don't fail the handler).

---

## Agent 5: ICompanyCacheService (14 files)

Inject `ICompanyCacheService` and replace `_db.Companies.FindAsync(id)` / `FirstOrDefaultAsync(c => c.Id == id)` with `_companyCacheService.GetCompanyAsync(id)`.

**Service files (8):**
- Services/ViewAsModeService.cs
- Services/OwnerCompanySelectorService.cs (2 methods: Set + Get)
- Services/ShiftCalendarService.cs (AssignUserToShiftAsync)
- Services/ChoreService.cs (CreateChoreAsync)
- Services/ScheduleExportService.cs
- Services/PurgeService.cs (also add InvalidateCache after purge)
- Services/ArchiveService.cs
- Services/ImportService.cs

**Page files (6):**
- Pages/Auth/Signup.cshtml.cs (2 FindAsync calls → 1 cached)
- Pages/Api/ScopeSwitcher.cshtml.cs
- Pages/Admin/Analytics.cshtml.cs
- Pages/Admin/AuditLog.cshtml.cs
- Pages/Admin/Companies.cshtml.cs (add InvalidateCache after update/delete)
- Pages/Owner/DataLifecycle.cshtml.cs

**CONSTRAINT:** Cache returns AsNoTracking entities. NEVER assign to EF-tracked properties.
Replace `_db.Companies.AnyAsync(...)` with `(await _cache.GetCompanyAsync(id)) != null`.

---

## Agent 6: ILocalizationService (22 files)

### Page models (4 files) — inject ILocalizationService via constructor:
- Pages/Calendar/Week.cshtml.cs (lines 102-103: FormatMediumDate)
- Pages/Calendar/Day.cshtml.cs (lines 88-89: FormatMediumDate)
- Services/NotificationService.cs (8 format strings: FormatMediumDate + FormatTime)
- Services/MailService.cs (15+ format strings: FormatMediumDate + FormatTime)

### Views (18 files) — inject via `@inject ShiftManager.Services.ILocalizationService Localization`:

**Calendar views:**
- Pages/Calendar/Shifts.cshtml (line 75: FormatMediumDate)
- Pages/Calendar/Overview.cshtml (line 68: FormatMediumDate)
- Pages/Calendar/OnCall.cshtml (line 83: FormatMediumDate)
- Pages/Calendar/Chores.cshtml (line 79: FormatMediumDate)
- Pages/Calendar/Week.cshtml (line 31: FormatMediumDate)
- Pages/Calendar/Month.cshtml (line 287: FormatLongDate for aria-label)
- Pages/Calendar/Table.cshtml (line 1507: FormatMediumDate; line 1563: FormatTime)

**Admin/Owner views:**
- Pages/Admin/AuditLog.cshtml (line 170: FormatMediumDate)
- Pages/Admin/Users.cshtml (line 794: FormatTime)
- Pages/Owner/Hub/AuditSearch.cshtml (line 556: FormatMediumDate)
- Pages/Owner/Blueprints.cshtml (line 163: FormatTime)
- Pages/Owner/Programs.cshtml (lines 64, 157: FormatTime)
- Pages/Owner/AreaConfig.cshtml (line 80: FormatMediumDate)

**User-facing views:**
- Pages/Friends/Index.cshtml (lines 120, 149: FormatMediumDate)
- Pages/Requests/Index.cshtml (lines 484, 643, 646: FormatMediumDate)
- Pages/My/Requests.cshtml (lines 126, 178, 236: FormatTime + FormatMediumDate)
- Pages/My/ApiKeys.cshtml (lines 102, 105, 212, 215, 369: FormatMediumDate)
- Pages/My/NotificationCenter.cshtml (line 76, 81: FormatMediumDate + FormatTime)
- Pages/Public/Feedback.cshtml (line 225: FormatMediumDate + FormatTime)
- Pages/Home/Index.cshtml (line 49: FormatTime; line 115: FormatMediumDate + FormatTime)
- Pages/Owner/Permissions.cshtml (line 133: FormatMediumDate)
- Pages/My/_TimelineItem.cshtml (lines 17-18: FormatMediumDate)

**CRITICAL RULE:** DO NOT touch `yyyy-MM-dd` in `data-*` attributes, URL params, `<input type="date">` values, JSON responses, or JS variables. These are ISO wire formats.
Only replace `"MMM dd, yyyy"`, `"MMM dd"`, `"MMMM d, yyyy"`, `"HH:mm"` in USER-VISIBLE display text.

Use `Localization.FormatMediumDate()` for "MMM dd, yyyy" display.
Use `Localization.FormatTime()` for "HH:mm" display.
Use `Localization.FormatLongDate()` for "MMMM d, yyyy" display.

---

## Agent 1: IRoleService (14 files)

Inject `IRoleService` and replace `_db.RoleTemplates` / `_db.UserRoleAssignments` queries.

**Excludes:** GrantService.cs (circular dep), Owner/Hub/RoleTemplates/* (CRUD admin), SeedData.cshtml.cs.

| File | Changes |
|------|---------|
| Pages/Admin/Users.cshtml.cs | 13 RoleTemplate queries → service methods; inject IRoleService |
| Pages/Admin/Announcements.cshtml.cs | 1 query → GetRoleTemplatesAsync() |
| Pages/Admin/Companies.cshtml.cs | 1 query → GetRoleTemplateByKeyAsync() |
| Pages/Admin/EditProfile.cshtml.cs | 2 queries → GetRoleTemplateAsync + GetAssignableRoleTemplatesAsync |
| Pages/Admin/Organization/Roles/Index.cshtml.cs | 2 template + 2 assignment queries |
| Pages/Admin/Organization/Roles/Assign.cshtml.cs | FindAsync + AssignRoleAsync + dropdown |
| Pages/Auth/Signup.cshtml.cs | 1 query → GetRoleTemplateAsync (with IsVisibleInSignup manual check) |
| Pages/Auth/Login.cshtml.cs | 1 query → GetRoleTemplateByKeyAsync |
| Pages/Api/Signup/GetSignupOptions.cshtml.cs | 1 query → GetSignupRoleTemplatesAsync |
| Pages/Owner/GriffinConfig.cshtml.cs | 1 query → GetRoleTemplatesAsync |
| Pages/Owner/Hub/Grants.cshtml.cs | Count + template list + GetByKey + AJAX template |
| Pages/Owner/Hub/Index.cshtml.cs | 2 count queries → GetActiveRoleTemplateCountAsync |
| Services/UserDataExportService.cs | 1 assignment query → GetUserRolesAsync |

---

## Agent 2: IJobTypeService (15 files)

Inject `IJobTypeService` and replace `_db.JobTypes` queries.

| File | Changes |
|------|---------|
| Pages/Calendar/Shifts.cshtml.cs | 1 query → GetJobTypesAsync(areaId) |
| Pages/Calendar/Month.cshtml.cs | 1 query → GetJobTypesAsync(areaId) |
| Pages/Admin/Users.cshtml.cs | 4 queries → GetAllJobTypesWithAreaAsync + GetJobTypeAsync |
| Pages/Admin/EditProfile.cshtml.cs | 1 query → GetJobTypesAsync(areaId) |
| Pages/Admin/Settings/ApprovalRules.cshtml.cs | 1 query → GetAllJobTypesAsync |
| Pages/Admin/Organization/ShiftGroupings/Index.cshtml.cs | 1 query → GetAllJobTypesWithAreaAsync |
| Pages/Admin/Organization/Roles/Assign.cshtml.cs | 1 query → GetAllJobTypesWithAreaAsync |
| Pages/Admin/Organization/Grants/Assign.cshtml.cs | 1 query → GetAllJobTypesWithAreaAsync |
| Pages/Admin/Organization/JobTypes/Index.cshtml.cs | List + Create + Toggle + Delete |
| Pages/Api/Signup/GetSignupOptions.cshtml.cs | 2 queries → GetJobTypesAsync + GetAllJobTypesAsync |
| Pages/Owner/Hub/RoleTemplates/Edit.cshtml.cs | 1 query → GetAllJobTypesAsync |
| Pages/Owner/Hub/RoleTemplates/Create.cshtml.cs | 1 query → GetAllJobTypesAsync |
| Pages/Owner/Hub/SeedData.cshtml.cs | 1 count → GetJobTypeCountAsync |
| Services/ScheduleExportService.cs | 1 query → GetJobTypeAsync |
| Services/SetupTaskService.cs | 2 queries → GetJobTypesAsync(areaId) |
| Data/SeedData/DataMigrationHelper.cs | 1 query → GetAllJobTypesAsync |

---

## Agent 4: IConcurrencyService (7 files, ~66 SaveChangesAsync calls)

Inject `IConcurrencyService` into each page model. Replace bare `SaveChangesAsync()` with:
```csharp
var result = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
    () => _db.SaveChangesAsync(), "EntityType", entityId);
if (!result.Success)
{
    // JSON handlers (Calendar/Table):
    return new JsonResult(new { success = false, error = result.ErrorMessage }) { StatusCode = 409 };
    // Redirect handlers (Admin pages):
    Error = _localizer["Error_ConcurrencyConflict"];
    return RedirectToPage();
}
```

**NOTE:** `result.ErrorMessage` (NOT `result.Message`).

| File | Calls | Entity Types |
|------|-------|-------------|
| Pages/Calendar/Table.cshtml.cs | 20 | ShiftInstance, ShiftAssignment, ShiftType |
| Pages/Admin/Users.cshtml.cs | 18 | AppUser, UserRoleAssignment |
| Pages/Admin/Companies.cshtml.cs | 8 | Company |
| Pages/Owner/Hub/Grants.cshtml.cs | 6 | Grant, AutoGrant |
| Pages/Owner/Blueprints.cshtml.cs | 4 | ShiftType |
| Pages/Requests/Index.cshtml.cs | 7 | TimeOffRequest, SwapRequest |
| Pages/Assignments/Manage.cshtml.cs | 3 | ShiftAssignment |

**IMPORTANT:** Place the concurrency call INSIDE the existing try/catch blocks, BEFORE the catch. The service only catches DbUpdateConcurrencyException — all other exceptions propagate to existing catch handlers.

---

## Post-Implementation

1. `dotnet build` — target: 0 warnings, 0 errors
2. `dotnet test` — target: 236/236 passing
3. Browser verification of key pages
