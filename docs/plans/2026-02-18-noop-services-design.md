# No-Op Services Full Integration Design

**Created:** 2026-02-18
**Goal:** Wire all 6 fully-implemented-but-never-injected services into their appropriate consumers for production release.
**Approach:** Parallel agent dispatch in dependency-aware waves.

---

## Service 1: IRoleService

### Current State
- Interface: `Services/IRoleService.cs` (16 methods)
- Implementation: `Services/RoleService.cs`
- DI Registration: `Program.cs:264` (`AddScoped`)
- Injection sites: **ZERO**

### Interface Additions Required

```csharp
// Filtered template lists
Task<List<RoleTemplate>> GetAssignableRoleTemplatesAsync();        // IsActive && CanBeAssignedByDefault
Task<List<RoleTemplate>> GetSignupRoleTemplatesAsync();            // IsActive && IsVisibleInSignup

// Templates with navigation properties (for grant management)
Task<RoleTemplate?> GetRoleTemplateWithAutoGrantsAsync(int id);
Task<RoleTemplate?> GetRoleTemplateWithAutoGrantsByKeyAsync(string key);

// Admin queries
Task<int> GetActiveRoleTemplateCountAsync();
Task<List<UserRoleAssignment>> GetUserRoleAssignmentsFilteredAsync(
    int? roleTemplateId = null, int? userId = null, bool includeUser = true, bool includeTemplate = true);
```

### Consumer Sites (16 files)

| File | Lines | Replacement Method |
|------|-------|--------------------|
| `Pages/Admin/Users.cshtml.cs` | 290-294 | `GetAssignableRoleTemplatesAsync()` |
| `Pages/Admin/Users.cshtml.cs` | 512, 601, 617, 758, 872, 967 | `GetRoleTemplateAsync(id)` / `GetRoleTemplateByKeyAsync(key)` |
| `Pages/Admin/Users.cshtml.cs` | 1311, 1316, 1322, 1592, 1625 | `GetRoleTemplateAsync(id)` / `GetRoleTemplateByKeyAsync(key)` |
| `Pages/Admin/Users.cshtml.cs` | 1982, 1996, 2002, 2010 | `GetRoleTemplateByKeyAsync(key)` |
| `Pages/Admin/Announcements.cshtml.cs` | 111-115 | `GetRoleTemplatesAsync()` |
| `Pages/Admin/Companies.cshtml.cs` | 265-266 | `GetRoleTemplateByKeyAsync(key)` |
| `Pages/Admin/EditProfile.cshtml.cs` | 433-434 | `GetRoleTemplateAsync(id)` |
| `Pages/Admin/EditProfile.cshtml.cs` | 611-615 | `GetAssignableRoleTemplatesAsync()` |
| `Pages/Admin/Organization/Roles/Index.cshtml.cs` | 71-76 | `GetRoleTemplatesAsync()` |
| `Pages/Admin/Organization/Roles/Index.cshtml.cs` | 81-91 | `GetUserRoleAssignmentsFilteredAsync(...)` |
| `Pages/Admin/Organization/Roles/Index.cshtml.cs` | 121-126 | `GetRoleTemplateWithAutoGrantsAsync` (list variant) |
| `Pages/Admin/Organization/Roles/Index.cshtml.cs` | 144-148 | `GetUserRoleAssignmentAsync(id)` + `RemoveRoleAsync(id)` |
| `Pages/Admin/Organization/Roles/Assign.cshtml.cs` | 113 | `GetRoleTemplateAsync(id)` |
| `Pages/Admin/Organization/Roles/Assign.cshtml.cs` | 131-168 | `AssignRoleAsync(...)` (includes duplicate check) |
| `Pages/Admin/Organization/Roles/Assign.cshtml.cs` | 215-220 | `GetRoleTemplatesAsync()` |
| `Pages/Auth/Signup.cshtml.cs` | 170-171 | `GetSignupRoleTemplatesAsync()` or `GetRoleTemplateAsync(id)` with manual filter |
| `Pages/Auth/Login.cshtml.cs` | 257 | `GetRoleTemplateByKeyAsync(key)` |
| `Pages/Api/Signup/GetSignupOptions.cshtml.cs` | 198-207 | `GetSignupRoleTemplatesAsync()` |
| `Pages/Owner/GriffinConfig.cshtml.cs` | 226-230 | `GetRoleTemplatesAsync()` |
| `Pages/Owner/Hub/Grants.cshtml.cs` | 63 | `GetActiveRoleTemplateCountAsync()` |
| `Pages/Owner/Hub/Grants.cshtml.cs` | 93-116 | `GetRoleTemplateWithAutoGrantsAsync` (list with AutoGrants) |
| `Pages/Owner/Hub/Grants.cshtml.cs` | 156 | `GetRoleTemplateByKeyAsync("Owner")` |
| `Pages/Owner/Hub/Grants.cshtml.cs` | 485-488 | `GetRoleTemplateWithAutoGrantsAsync(id)` |
| `Pages/Owner/Hub/Index.cshtml.cs` | 79, 100 | `GetActiveRoleTemplateCountAsync()` |
| `Services/GrantService.cs` | 389-392, 438-440 | `GetRoleTemplateWithAutoGrantsAsync(id)` |
| `Services/GrantService.cs` | 577-580, 657-660 | `GetRoleTemplateWithAutoGrantsByKeyAsync(key)` |
| `Services/UserDataExportService.cs` | 231-238 | `GetUserRolesAsync(userId)` |

### Excluded (architectural)
- `Pages/Owner/Hub/RoleTemplates/Index.cshtml.cs` — Owner CRUD management of templates themselves
- `Pages/Owner/Hub/RoleTemplates/Edit.cshtml.cs` — Owner CRUD with specialized includes
- `Pages/Owner/Hub/RoleTemplates/Create.cshtml.cs` — Owner CRUD
- `Pages/Owner/Hub/SeedData.cshtml.cs` — Seeding utility with bulk Add operations

---

## Service 2: IJobTypeService

### Current State
- Interface: `Services/IJobTypeService.cs` (10 methods)
- Implementation: `Services/JobTypeService.cs`
- DI Registration: `Program.cs:256` (`AddScoped`)
- Injection sites: **ZERO**

### Interface Additions Required

```csharp
// CRUD operations
Task<JobType> CreateJobTypeAsync(string name, int areaId, TimeOnly start, TimeOnly end, bool isActive = true);
Task<bool> ToggleActiveAsync(int jobTypeId);
Task<bool> DeleteJobTypeAsync(int jobTypeId);
Task<int> GetJobTypeCountAsync();

// Admin list with Area includes
Task<List<JobType>> GetAllJobTypesWithAreaAsync();  // IgnoreQueryFilters + Include(Area)
```

### Consumer Sites (15 files)

| File | Lines | Replacement Method |
|------|-------|--------------------|
| `Pages/Calendar/Shifts.cshtml.cs` | 248-252 | `GetJobTypesAsync(areaId)` |
| `Pages/Calendar/Month.cshtml.cs` | 179-183 | `GetJobTypesAsync(areaId)` |
| `Pages/Admin/Users.cshtml.cs` | 281-285 | `GetAllJobTypesWithAreaAsync()` |
| `Pages/Admin/Users.cshtml.cs` | 601-603, 617, 967-970 | `GetJobTypeAsync(id)` |
| `Pages/Admin/EditProfile.cshtml.cs` | 583-587 | `GetJobTypesAsync(areaId)` |
| `Pages/Admin/Settings/ApprovalRules.cshtml.cs` | 174-178 | `GetAllJobTypesAsync()` |
| `Pages/Admin/Organization/ShiftGroupings/Index.cshtml.cs` | 99-103 | `GetAllJobTypesWithAreaAsync()` |
| `Pages/Admin/Organization/Roles/Assign.cshtml.cs` | 259-263 | `GetAllJobTypesWithAreaAsync()` |
| `Pages/Admin/Organization/Grants/Assign.cshtml.cs` | 239-243 | `GetAllJobTypesWithAreaAsync()` |
| `Pages/Admin/Organization/JobTypes/Index.cshtml.cs` | 62-66 | `GetAllJobTypesWithAreaAsync()` |
| `Pages/Admin/Organization/JobTypes/Index.cshtml.cs` | 128 | `CreateJobTypeAsync(...)` |
| `Pages/Admin/Organization/JobTypes/Index.cshtml.cs` | 140 | `ToggleActiveAsync(id)` |
| `Pages/Admin/Organization/JobTypes/Index.cshtml.cs` | 172-179 | `DeleteJobTypeAsync(id)` |
| `Pages/Api/Signup/GetSignupOptions.cshtml.cs` | 130-134, 164-168 | `GetJobTypesAsync(areaId)` / `GetAllJobTypesAsync()` |
| `Pages/Owner/Hub/RoleTemplates/Edit.cshtml.cs` | 235-239 | `GetAllJobTypesAsync()` |
| `Pages/Owner/Hub/RoleTemplates/Create.cshtml.cs` | 139-143 | `GetAllJobTypesAsync()` |
| `Pages/Owner/Hub/SeedData.cshtml.cs` | 261 | `GetJobTypeCountAsync()` |
| `Services/ScheduleExportService.cs` | 76-78 | `GetJobTypeAsync(id)` |
| `Services/SetupTaskService.cs` | 68-70, 142-144 | `GetJobTypesAsync(areaId)` |
| `Data/SeedData/DataMigrationHelper.cs` | 118-119 | `GetAllJobTypesAsync()` |

### Key Implementation Note
`JobTypeService` must use `IgnoreQueryFilters()` for all queries — nearly every consumer site is an admin page that needs cross-tenant visibility. Verify current implementation and add if missing.

---

## Service 3: ICalendarNotificationService

### Current State
- Interface + Implementation: `Hubs/CalendarHub.cs` (lines 199-250)
- Event DTOs: Same file (5 record types)
- CalendarGroups helper: Same file (lines 255-261)
- DI Registration: `Program.cs:314` (`AddScoped`)
- Injection sites: **ZERO**

### No Interface Changes Required

### Consumer: Calendar/Table.cshtml.cs (10 POST handlers)

Each handler needs: inject `ICalendarNotificationService`, call appropriate notify method after successful `SaveChangesAsync()`.

| Handler | Notification Method | Group |
|---------|---------------------|-------|
| `OnPostAssignEmployeeAsync` | `NotifyAssignmentChangedAsync` | `CalendarGroups.Shifts(moleculeId, jobTypeId)` |
| `OnPostUnassignEmployeeAsync` | `NotifyAssignmentChangedAsync` | Same (join-load ShiftInstance→ShiftType) |
| `OnPostClearAssignmentAsync` | `NotifyAssignmentChangedAsync` | Same |
| `OnPostAddTraineeAsync` | `NotifyAssignmentChangedAsync` | Same |
| `OnPostRemoveTraineeAsync` | `NotifyAssignmentChangedAsync` | Same |
| `OnPostChangeUserAsync` | `NotifyAssignmentChangedAsync` | Same |
| `OnPostUpdateShiftStaffingAsync` | `NotifyCapacityChangedAsync` | Same |
| `OnPostDeleteShiftInstanceAsync` | `NotifyCapacityChangedAsync` | Same |
| `OnPostCreateCustomShiftTypeAsync` | `NotifyCapacityChangedAsync` | Same |
| `OnPostFillRangeAsync` | `NotifyAssignmentChangedAsync` | Same (per filled instance) |

### Group Name Resolution
Handlers receiving only `AssignmentId` must join-load: `ShiftAssignment → ShiftInstance → ShiftType` to get `MoleculeId` and `JobTypeId`. Handlers already loading ShiftInstance/ShiftType can reuse existing variables.

---

## Service 4: IConcurrencyService

### Current State
- Interface: `Services/IConcurrencyService.cs` (1 method + result types)
- Implementation: `Services/ConcurrencyService.cs`
- DI Registration: `Program.cs:261` (`AddScoped`)
- Injection sites: **ZERO**

### No Interface Changes Required

### Consumer Sites (7 files, ~66 SaveChangesAsync calls)

| File | SaveChangesAsync Calls | Entity Types |
|------|------------------------|--------------|
| `Pages/Calendar/Table.cshtml.cs` | 20 | ShiftInstance, ShiftAssignment, ShiftType |
| `Pages/Admin/Users.cshtml.cs` | 18 | AppUser, UserRoleAssignment |
| `Pages/Admin/Companies.cshtml.cs` | 8 | Company |
| `Pages/Owner/Hub/Grants.cshtml.cs` | 6 | Grant, AutoGrant |
| `Pages/Owner/Blueprints.cshtml.cs` | 4 | ShiftType |
| `Pages/Requests/Index.cshtml.cs` | 7 | TimeOffRequest, SwapRequest |
| `Pages/Assignments/Manage.cshtml.cs` | 3 | ShiftAssignment |

### Wiring Pattern

```csharp
// Replace bare:
await _db.SaveChangesAsync();

// With:
var result = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
    () => _db.SaveChangesAsync(), "EntityType", entityId);
if (!result.Success)
{
    // For JSON-returning handlers (Calendar/Table):
    return new JsonResult(new { success = false, error = result.Message }) { StatusCode = 409 };
    // For redirect-returning handlers (Admin/Users, Companies, etc.):
    Error = _localizer["Error_ConcurrencyConflict"];
    return RedirectToPage();
}
```

### Localization Key Needed
Add `Error_ConcurrencyConflict` to both EN and HE .resx files.

---

## Service 5: ICompanyCacheService

### Current State
- Interface + Implementation: `Services/CompanyCacheService.cs`
- Methods: `GetCompanyAsync(int)` (10-min cache), `InvalidateCache(int)`
- DI Registration: `Program.cs:220` (`AddScoped`)
- Injection sites: **ZERO**

### No Interface Changes Required

### Consumer Sites (12 files)

**High priority (hot paths):**

| File | Method | Current Call | Replacement |
|------|--------|-------------|-------------|
| `Services/ViewAsModeService.cs` | `GetViewAsCompanyNameAsync` | `_db.Companies.FindAsync(companyId)` | `_companyCacheService.GetCompanyAsync(companyId)` |
| `Services/OwnerCompanySelectorService.cs` | `SetSelectedCompanyAsync` | `_db.Companies.AnyAsync(...)` | `(await _cache.GetCompanyAsync(id)) != null` |
| `Services/OwnerCompanySelectorService.cs` | `GetSelectedCompanyNameAsync` | `_db.Companies.FindAsync(...)` | `_cache.GetCompanyAsync(id)` |
| `Services/ShiftCalendarService.cs` | `AssignUserToShiftAsync` | `_db.Companies.FirstOrDefaultAsync(...)` | `_cache.GetCompanyAsync(id)` |
| `Services/ChoreService.cs` | `CreateChoreAsync` | `_db.Companies.FindAsync(...)` | `_cache.GetCompanyAsync(id)` |

**Medium priority:**

| File | Method | Replacement |
|------|--------|-------------|
| `Pages/Auth/Signup.cshtml.cs` | `OnPostAsync` (2 calls) | 1 cached call replaces 2 |
| `Pages/Api/ScopeSwitcher.cshtml.cs` | `OnGetAsync` | `_cache.GetCompanyAsync(id)` |
| `Services/ScheduleExportService.cs` | `ExportAsync` | `_cache.GetCompanyAsync(id)` |
| `Pages/Admin/Analytics.cshtml.cs` | `OnPostExportAsync` | `_cache.GetCompanyAsync(id)` |
| `Pages/Admin/AuditLog.cshtml.cs` | `OnPostExportAsync` | `_cache.GetCompanyAsync(id)` |

**Low priority (infrequent ops):**

| File | Method | Replacement |
|------|--------|-------------|
| `Services/PurgeService.cs` | `PurgeCompanyDataAsync` | `_cache.GetCompanyAsync(id)` + `InvalidateCache(id)` |
| `Services/ArchiveService.cs` | `ArchiveCompanyDataAsync` | `_cache.GetCompanyAsync(id)` |
| `Services/ImportService.cs` | `ImportAsync` | `_cache.GetCompanyAsync(id)` |

**Cache Invalidation:**
- `Pages/Admin/Companies.cshtml.cs` — call `InvalidateCache(companyId)` after update/delete operations

### NOT wiring (bulk/navigation queries that don't fit cache-by-ID):
- `Services/GrantService.cs` — bulk `WHERE` + `Select(Id)` queries
- `Services/ShiftAssignmentService.cs` — batch `ToDictionary`
- `Services/ShiftCalendarService.cs` lines 32/328 — inline `.Any()` subquery in EF LINQ
- `Services/HierarchyService.cs` — list by MoleculeId
- `Services/HierarchySettingsService.cs` — needs `.Include(Molecule.Area)` navigation
- `Services/SetupTaskService.cs` — needs `.Include(Molecule.Area)` navigation

---

## Service 6: ILocalizationService

### Current State
- Interface: `Services/ILocalizationService.cs` (8 members)
- Implementation: `Services/LocalizationService.cs`
- DI Registration: `Program.cs:121` (`AddScoped`)
- Injection sites: **ZERO**

### No Interface Changes Required

### Critical Rule: DO NOT TOUCH wire formats
All `yyyy-MM-dd` in `data-*` attributes, URL query params, `<input type="date">` values, JSON API responses, and JavaScript variables MUST remain as ISO 8601. Only replace **user-facing display** format strings.

### Consumer Sites — Page Models (3 files)

| File | Lines | Format | Replacement |
|------|-------|--------|-------------|
| `Pages/Calendar/Week.cshtml.cs` | 102, 103 | `"MMM dd, yyyy"` | `_localization.FormatDate(date)` |
| `Pages/Calendar/Day.cshtml.cs` | 88, 89 | `"MMM dd, yyyy"` | `_localization.FormatDate(date)` |
| `Services/NotificationService.cs` | 114, 115, 147, 148, 900, 901, 1032, 1097 | Mixed `"HH:mm"` / `"MMM dd, yyyy"` | `_localization.FormatTime()` / `_localization.FormatDate()` |

### Consumer Sites — Views (15 files)

Inject via `@inject ShiftManager.Services.ILocalizationService Localization` in each view.

| File | Lines | Format(s) | Replacement |
|------|-------|-----------|-------------|
| `Pages/Calendar/Shifts.cshtml` | 75 | `"MMM dd"` / `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Calendar/Overview.cshtml` | 68 | `"MMM dd"` / `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Calendar/OnCall.cshtml` | 83 | `"MMM dd"` / `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Calendar/Chores.cshtml` | 79 | `"MMM dd"` / `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Calendar/Week.cshtml` | 31 | `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Calendar/Month.cshtml` | 287 | `"MMMM d, yyyy"` (aria-label) | `Localization.FormatDate()` |
| `Pages/Calendar/Table.cshtml` | 1507, 1563 | `"MMM dd, yyyy"` / `"HH:mm"` | `Localization.FormatDate()` / `Localization.FormatTime()` |
| `Pages/Admin/AuditLog.cshtml` | 170 | `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Admin/Users.cshtml` | 794 | `"HH:mm"` | `Localization.FormatTime()` |
| `Pages/Owner/Hub/AuditSearch.cshtml` | 556 | `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Owner/Blueprints.cshtml` | 163 | `"HH:mm"` | `Localization.FormatTime()` |
| `Pages/Owner/Programs.cshtml` | 64, 157 | `"HH:mm"` | `Localization.FormatTime()` |
| `Pages/Owner/AreaConfig.cshtml` | 80 | `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Friends/Index.cshtml` | 120, 149 | `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/Requests/Index.cshtml` | 484, 643, 646 | `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/My/Requests.cshtml` | 126, 178, 236 | `"HH:mm"` / `"MMM dd, yyyy"` | `Localization.FormatTime()` / `Localization.FormatDate()` |
| `Pages/My/ApiKeys.cshtml` | 102, 105, 212, 215, 369 | `"MMM dd, yyyy"` | `Localization.FormatDate()` |
| `Pages/My/_TimelineItem.cshtml` | 17, 18 | Date display | `Localization.FormatDate()` |

---

## Execution Plan

### Wave 1 — Independent services (parallel)
- **Agent 5:** ICompanyCacheService (12 service/page files, no overlap with other agents)
- **Agent 6:** ILocalizationService (18 view files + 3 .cs files, no .cs overlap)

### Wave 2 — Shared Admin/Users.cshtml.cs (sequential)
- **Agent 1:** IRoleService (16 files including Admin/Users for RoleTemplate queries)
- **Agent 2:** IJobTypeService (15 files including Admin/Users for JobType queries) — after Agent 1

### Wave 3 — Shared Calendar/Table.cshtml.cs (sequential)
- **Agent 3:** ICalendarNotificationService (Calendar/Table POST handlers)
- **Agent 4:** IConcurrencyService (7 files including Calendar/Table) — after Agent 3

### Build & Test
After all waves: full build (0 warnings target) + full test run (236/236 target).

---

## Localization Keys Required

| Key | EN | HE | Used By |
|-----|----|----|---------|
| `Error_ConcurrencyConflict` | `"This record was modified by another user. Please refresh and try again."` | `"רשומה זו שונתה על ידי משתמש אחר. אנא רענן ונסה שוב."` | IConcurrencyService consumers |
