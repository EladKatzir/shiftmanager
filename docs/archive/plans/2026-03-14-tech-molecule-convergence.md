# Tech Molecule Convergence Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert Shikma from Department-based Tech molecule to Company-based with data-driven shift eligibility rules, enabling Tech users to fully participate in the shift system.

**Architecture:** Departments become Companies; shift eligibility is controlled by `EligibleCompanyIds` (JSON) and `RequiresOfficerRank` (bool) on ShiftType; Calendar pages branch on `MoleculeType.Tech` for single-view mode; `IShiftCalendarService` methods accept nullable `jobTypeId`; TechShiftService is deleted and replaced by ShiftType-based eligibility.

**Tech Stack:** ASP.NET Core 8.0, Razor Pages, EF Core (SQLite), SignalR, JavaScript (vanilla)

**Spec:** `docs/superpowers/specs/2026-03-14-tech-molecule-convergence-design.md`

---

## Chunk 1: Schema & Model Changes

### Task 1: Add Eligibility Fields to ShiftType Model

**Files:**
- Modify: `Models/ShiftType.cs:16-32`

- [ ] **Step 1: Add EligibleCompanyIds and RequiresOfficerRank properties**

In `Models/ShiftType.cs`, after the `TechShiftType` property (line 32), add:

```csharp
/// <summary>
/// JSON array of CompanyIds whose users can be assigned this shift type.
/// Null = all companies in the molecule are eligible (default workforce behavior).
/// </summary>
public string? EligibleCompanyIds { get; set; }

/// <summary>
/// If true, only users with officer rank (>= SegenMishne) can be assigned.
/// Follows existing pattern from OnDutyTypeConfig.RequiresOfficerRank.
/// </summary>
public bool RequiresOfficerRank { get; set; } = false;
```

- [ ] **Step 2: Remove unused Tech shift type constants**

In `Models/ShiftType.cs`, remove lines 21-22:

```csharp
// DELETE these two lines:
public const string TECH_SUPPORT = "SUPPORT";
public const string TECH_ONCALL = "ONCALL";
```

- [ ] **Step 3: Add helper method to parse EligibleCompanyIds**

In `Models/ShiftType.cs`, add a helper method:

```csharp
/// <summary>
/// Parses EligibleCompanyIds JSON into a list of ints.
/// Returns null if no restriction (all companies eligible).
/// </summary>
[NotMapped]
public List<int>? GetEligibleCompanyIdList()
{
    if (string.IsNullOrEmpty(EligibleCompanyIds)) return null;
    try
    {
        return System.Text.Json.JsonSerializer.Deserialize<List<int>>(EligibleCompanyIds);
    }
    catch
    {
        return null;
    }
}
```

- [ ] **Step 4: Update Molecule.cs comment (cosmetic)**

In `Models/Molecule.cs`, update the `Departments` navigation property comment:

```csharp
// BEFORE:
public List<Department> Departments { get; set; } = new(); // Tech molecules

// AFTER:
public List<Department> Departments { get; set; } = new(); // Legacy: was used for Tech molecules before convergence
```

- [ ] **Step 5: Verify build**

Run: `dotnet build --no-restore`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add Models/ShiftType.cs Models/Molecule.cs
git commit -m "feat: add EligibleCompanyIds and RequiresOfficerRank to ShiftType"
```

---

### Task 2: Make ShiftCapacityOverride.JobTypeId Nullable

**Files:**
- Modify: `Models/ShiftCapacityOverride.cs:8,17`

- [ ] **Step 1: Update JobTypeId to nullable**

In `Models/ShiftCapacityOverride.cs`, change line 8:

```csharp
// BEFORE:
public int JobTypeId { get; set; }

// AFTER:
public int? JobTypeId { get; set; }
```

- [ ] **Step 2: Update JobType navigation to nullable**

In `Models/ShiftCapacityOverride.cs`, change line 17:

```csharp
// BEFORE:
public JobType JobType { get; set; } = null!;

// AFTER:
public JobType? JobType { get; set; }
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --no-restore`
Expected: Build succeeded (may show warnings about nullable reference — fix any callers that assume non-null)

- [ ] **Step 4: Commit**

```bash
git add Models/ShiftCapacityOverride.cs
git commit -m "feat: make ShiftCapacityOverride.JobTypeId nullable for Tech shifts"
```

---

### Task 3: Create EF Migration

**Files:**
- Create: `Migrations/{timestamp}_TechMoleculeConvergence.cs` (auto-generated)

- [ ] **Step 1: Generate migration**

Run: `dotnet ef migrations add TechMoleculeConvergence`
Expected: Migration created successfully

- [ ] **Step 2: Review the generated migration**

Open the generated migration file and verify it contains:
1. `AddColumn: EligibleCompanyIds (string, nullable)` on ShiftTypes table
2. `AddColumn: RequiresOfficerRank (bool, default false)` on ShiftTypes table
3. `AlterColumn: JobTypeId (int → int?, nullable)` on ShiftCapacityOverrides table

- [ ] **Step 3: Apply migration**

Run: `dotnet ef database update`
Expected: Database updated successfully

- [ ] **Step 4: Commit**

```bash
git add Migrations/
git commit -m "migration: TechMoleculeConvergence - eligibility fields + nullable JobTypeId"
```

---

## Chunk 2: Seed Data Changes

### Task 4: Update ShiftyOrganizationSeed — Shikma Companies + JobTypes

**Files:**
- Modify: `Data/SeedData/ShiftyOrganizationSeed.cs`

**Reference:** Read the current file first, especially lines 78-178 (molecule/department/company creation).

- [ ] **Step 1: Replace Shikma Departments with Companies**

In `ShiftyOrganizationSeed.cs`, find the `shikmaDepartments` list (around line 169-178) and replace with Companies. Also add Shikma-specific companies to the existing company-seeding section.

Replace the Departments block:
```csharp
// BEFORE (DELETE):
// DEPARTMENTS (for Tech Molecule - Shikma)
var shikmaDepartments = new List<Department>
{
    new() { MoleculeId = shikma.Id, Name = "Pie", DisplayName = "פאי" },
    new() { MoleculeId = shikma.Id, Name = "Tao", DisplayName = "טאו" },
    new() { MoleculeId = shikma.Id, Name = "Yekeb", DisplayName = "יקב" },
    new() { MoleculeId = shikma.Id, Name = "Snir", DisplayName = "שניר" },
    new() { MoleculeId = shikma.Id, Name = "Arbel", DisplayName = "ארבל" },
    new() { MoleculeId = shikma.Id, Name = "Samapkam", DisplayName = "סמפקמה" }
};
db.Departments.AddRange(shikmaDepartments);
```

With Companies (add near the other company definitions):
```csharp
// SHIKMA COMPANIES (Tech molecule — uses Companies like Workforce)
var shikYekev = new Company { MoleculeId = shikma.Id, Name = "Yekev", DisplayName = "יקב" };
var shikSnir = new Company { MoleculeId = shikma.Id, Name = "Snir", DisplayName = "שניר" };
var shikArbel = new Company { MoleculeId = shikma.Id, Name = "Arbel", DisplayName = "ארבל" };
var shikPie = new Company { MoleculeId = shikma.Id, Name = "Pie", DisplayName = "פאי" };
var shikSamapkamia = new Company { MoleculeId = shikma.Id, Name = "Samapkamia", DisplayName = "סמפקמיה" };
var shikTao = new Company { MoleculeId = shikma.Id, Name = "Tao", DisplayName = "טאו" };
db.Companies.AddRange(shikYekev, shikSnir, shikArbel, shikPie, shikSamapkamia, shikTao);
```

**IMPORTANT:** Keep the existing HQ company creation for Shikma (`hqMolecules` array around line 150). Do NOT add Shikma companies to that array — HQ is separate.

- [ ] **Step 2: Add Shikma-specific JobTypes**

After the existing jobTypes seeding (around line 58-71), add molecule-scoped job types for Shikma:

```csharp
// Shikma-specific job types (organizational only — no shift eligibility impact)
var shikmaJobTypes = new List<JobType>
{
    new() { AreaId = area.Id, MoleculeId = shikma.Id, Name = "ProjectManager", DisplayName = "מנהל פרוייקט", SortOrder = 10 },
    new() { AreaId = area.Id, MoleculeId = shikma.Id, Name = "Hakam", DisplayName = "חק\"ם", SortOrder = 20 }
};
db.JobTypes.AddRange(shikmaJobTypes);
```

**CRITICAL:** Use `area.Id` (resolved from the area variable), NOT hardcoded `190`.

- [ ] **Step 3: Verify build**

Run: `dotnet build --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Data/SeedData/ShiftyOrganizationSeed.cs
git commit -m "seed: replace Shikma departments with companies, add molecule-scoped job types"
```

---

### Task 5: Seed Tech Shift Types with Eligibility Rules

**Files:**
- Modify: `Data/SeedData/TechShiftTypeSeed.cs`
- Modify: `Data/SeedData/ShiftyOrganizationSeed.cs`

**CRITICAL CONTEXT:** `TechShiftTypeSeed.GetTechShiftTypes()` is currently **dead code** — never called from anywhere. Tech shift types must be explicitly seeded. The call site is `ShiftyOrganizationSeed.SeedAsync()`.

- [ ] **Step 1: Update TechShiftTypeSeed to include eligibility fields**

In `TechShiftTypeSeed.cs`, update `GetTechShiftTypes(int companyId)` to set `RequiresOfficerRank` on the Moviltech entry:

```csharp
// In the Moviltech ShiftType:
RequiresOfficerRank = true
// All others: RequiresOfficerRank = false (default)
```

`EligibleCompanyIds` CANNOT be set here because company IDs are auto-generated and not known until after `SaveChanges`. This will be set in a post-save step in `ShiftyOrganizationSeed`.

- [ ] **Step 2: Call GetTechShiftTypes from ShiftyOrganizationSeed**

In `ShiftyOrganizationSeed.SeedAsync()`, after creating Shikma companies and calling `SaveChanges` (so company IDs are available), add:

```csharp
// ============================================================
// TECH SHIFT TYPES (for Shikma)
// ============================================================
var hqShikma = await db.Companies.FirstOrDefaultAsync(c => c.MoleculeId == shikma.Id && c.IsHeadquarters);
if (hqShikma != null)
{
    var techShiftTypes = TechShiftTypeSeed.GetTechShiftTypes(hqShikma.Id);

    // Set EligibleCompanyIds now that company IDs are known
    foreach (var st in techShiftTypes)
    {
        st.MoleculeId = shikma.Id;

        if (st.TechShiftType == ShiftType.TECH_HANAVA || st.TechShiftType == ShiftType.TECH_DELTA)
        {
            st.EligibleCompanyIds = System.Text.Json.JsonSerializer.Serialize(
                new[] { shikTao.Id, shikPie.Id, shikSamapkamia.Id });
        }
        else if (st.TechShiftType == ShiftType.TECH_YEKEV)
        {
            st.EligibleCompanyIds = System.Text.Json.JsonSerializer.Serialize(
                new[] { shikYekev.Id });
        }
        // MOVILTECH: EligibleCompanyIds stays null (all companies), RequiresOfficerRank = true
    }

    db.ShiftTypes.AddRange(techShiftTypes);
    await db.SaveChangesAsync();
}
```

Place this AFTER the Shikma companies `SaveChanges` and BEFORE the shift groupings section.

- [ ] **Step 2: Verify build**

Run: `dotnet build --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Data/SeedData/TechShiftTypeSeed.cs
git commit -m "seed: add eligibility rules (EligibleCompanyIds, RequiresOfficerRank) to Tech shift types"
```

---

### Task 6: Update QaTestUserSeed + RoleTemplateSeed

**Files:**
- Modify: `Data/SeedData/QaTestUserSeed.cs`
- Modify: `Data/SeedData/RoleTemplateSeed.cs`

- [ ] **Step 1: Convert AddDeptUser to AddUser in QaTestUserSeed**

Read `QaTestUserSeed.cs` and find all `AddDeptUser` calls. Convert them to `AddUser` calls using the new Shikma Company variables instead of Department references. Assign `ProjectManager` or `Hakam` as the job type.

Key conversions:
- `deptlead.pie@test` → `AddUser` with Pie company, BRDirector template (was DepartmentLead)
- `emp.tech.pie@test` → `AddUser` with Pie company, Employee template, ProjectManager job type
- `emp.tech.tao@test` → `AddUser` with Tao company, Employee template, ProjectManager job type

- [ ] **Step 2: Set DepartmentLead IsVisibleInSignup = false**

In `RoleTemplateSeed.cs`, find the DepartmentLead template (Id=9, around line 104) and change:

```csharp
// BEFORE:
IsVisibleInSignup = true,

// AFTER:
IsVisibleInSignup = false,
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Data/SeedData/QaTestUserSeed.cs Data/SeedData/RoleTemplateSeed.cs
git commit -m "seed: convert Tech test users to company-based, hide DepartmentLead from signup"
```

---

## Chunk 3: Service Layer Changes

### Task 7: Update IShiftCalendarService + ShiftCalendarService

**Files:**
- Modify: `Services/IShiftCalendarService.cs:27-43`
- Modify: `Services/ShiftCalendarService.cs`

- [ ] **Step 1: Change interface signatures to nullable jobTypeId**

In `Services/IShiftCalendarService.cs`, change ALL `int jobTypeId` parameters to `int? jobTypeId`:

```csharp
Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int? jobTypeId);
Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);
Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);
Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date);
Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date, int capacity, int userId);
Task RemoveCapacityOverrideAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date);
Task<Dictionary<(int ShiftTypeId, DateOnly Date), int>> GetCapacitiesBatchAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);
```

- [ ] **Step 2: Add new eligibility method to interface**

In `Services/IShiftCalendarService.cs`, add:

```csharp
/// <summary>
/// Gets users eligible for a specific shift type based on EligibleCompanyIds and RequiresOfficerRank.
/// For Workforce shift types (no eligibility rules), returns all molecule users with matching JobTypeId.
/// </summary>
Task<List<AppUser>> GetEligibleUsersForShiftTypeAsync(int moleculeId, int shiftTypeId);
```

- [ ] **Step 3: Update ShiftCalendarService implementations**

In `Services/ShiftCalendarService.cs`, update each method implementation:

For `GetUsersForCalendarAsync`:
```csharp
public async Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int? jobTypeId)
{
    var query = _db.Users
        .IgnoreQueryFilters()
        .Where(u => u.IsActive
            && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));

    // For Workforce: filter by JobTypeId; for Tech (null): return all molecule users
    if (jobTypeId.HasValue)
        query = query.Where(u => u.JobTypeId == jobTypeId.Value);

    return await query
        .Include(u => u.JobType)
        .OrderBy(u => u.DisplayName)
        .ToListAsync();
}
```

Apply similar pattern to `GetShiftInstancesAsync`, `GetAssignmentsAsync`, and capacity methods:
- When `jobTypeId` is null, query by `MoleculeId` only (no JobTypeId filter on ShiftType)
- When `jobTypeId` has a value, use existing behavior

- [ ] **Step 4: Implement GetEligibleUsersForShiftTypeAsync**

In `Services/ShiftCalendarService.cs`, add:

```csharp
public async Task<List<AppUser>> GetEligibleUsersForShiftTypeAsync(int moleculeId, int shiftTypeId)
{
    var shiftType = await _db.ShiftTypes
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(st => st.Id == shiftTypeId);

    if (shiftType == null) return new List<AppUser>();

    var query = _db.Users
        .IgnoreQueryFilters()
        .Where(u => u.IsActive
            && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));

    // Apply company eligibility filter
    var eligibleCompanyIds = shiftType.GetEligibleCompanyIdList();
    if (eligibleCompanyIds != null)
        query = query.Where(u => eligibleCompanyIds.Contains(u.CompanyId));

    // Apply officer rank filter
    if (shiftType.RequiresOfficerRank)
        query = query.Where(u => (int)u.Rank >= 9); // MilitaryRank.SegenMishne = 9

    return await query
        .Include(u => u.JobType)
        .OrderBy(u => u.DisplayName)
        .ToListAsync();
}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build --no-restore`
Expected: Build errors on callers that pass `int` — these will be fixed in subsequent tasks

- [ ] **Step 6: Fix compilation errors from callers**

The following files call `IShiftCalendarService` methods with `int jobTypeId` and need updating to pass `int?`:

1. `Pages/Calendar/Shifts.cshtml.cs` — `BuildShiftBasedCalendarAsync`, `BuildUserBasedCalendarAsync`, `GetUsersForCalendarAsync` calls (Tasks 12-13 will refactor these further, but they must compile now)
2. `Pages/Calendar/Table.cshtml.cs` — multiple calls in POST handlers and `OnGetAsync`
3. `Pages/Api/Calendar/GetShiftsData.cshtml.cs` — `GetShiftInstancesAsync`, `GetAssignmentsAsync` calls
4. Any test files that call these service methods directly

Most callers already have `int? JobTypeId` page properties, so passing them directly (without `.Value`) will work. For POST handlers that receive `int jobTypeId` as a parameter, they can be cast to `int?`.

- [ ] **Step 7: Verify build succeeds**

Run: `dotnet build --no-restore`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add Services/IShiftCalendarService.cs Services/ShiftCalendarService.cs
git commit -m "feat: nullable jobTypeId in IShiftCalendarService, add GetEligibleUsersForShiftTypeAsync"
```

---

### Task 8: Update ShiftTypeCacheService

**Files:**
- Modify: `Services/ShiftTypeCacheService.cs:14,16,79,127`

- [ ] **Step 1: Update interface and implementation signatures**

Change `int jobTypeId` to `int? jobTypeId` on:
- `GetShiftTypesForMoleculeAsync(int moleculeId, int? jobTypeId)` (line 14, 79)
- `InvalidateMoleculeCache(int moleculeId, int? jobTypeId)` (line 16, 127)

- [ ] **Step 2: Update cache key generation**

The cache key must handle null jobTypeId. Use `0` as sentinel:

```csharp
private static string CacheKey(int moleculeId, int? jobTypeId)
    => $"shifttypes-{moleculeId}-{jobTypeId ?? 0}";
```

For `InvalidateMoleculeCache` when `jobTypeId` is null, invalidate ALL cache entries for the molecule (since Tech shows all shift types together):

```csharp
public void InvalidateMoleculeCache(int moleculeId, int? jobTypeId)
{
    if (jobTypeId.HasValue)
    {
        _cache.Remove(CacheKey(moleculeId, jobTypeId));
    }
    else
    {
        // Tech molecule: invalidate all job-type-specific caches for this molecule
        // Since we don't track all keys, use a version counter approach
        // or simply clear the specific sentinel key
        _cache.Remove(CacheKey(moleculeId, null));
    }
}
```

- [ ] **Step 3: Update query logic for null jobTypeId**

In `GetShiftTypesForMoleculeAsync`, when `jobTypeId` is null, query shift types by MoleculeId only:

```csharp
if (jobTypeId.HasValue)
    query = query.Where(st => st.JobTypeId == jobTypeId.Value);
// else: return all shift types for the molecule (Tech mode)
```

- [ ] **Step 4: Fix callers**

Search for all `InvalidateMoleculeCache` and `GetShiftTypesForMoleculeAsync` callers. Update them to pass `int?` instead of `int`. Key locations:
- `Table.cshtml.cs` — ~10 callsites guarded by `shiftType.JobTypeId != null`

- [ ] **Step 5: Verify build and commit**

Run: `dotnet build --no-restore`
Expected: Build succeeded

```bash
git add Services/ShiftTypeCacheService.cs
git commit -m "feat: nullable jobTypeId in ShiftTypeCacheService"
```

---

### Task 9: Delete TechShiftService + Update ShiftAssignmentService (Combined)

**Files:**
- Delete: `Services/ITechShiftService.cs`
- Delete: `Services/TechShiftService.cs`
- Modify: `Program.cs:327`
- Modify: `Services/ShiftAssignmentService.cs:20,32,272-284,903-911`
- Modify: `Pages/Api/TechShift/Eligible.cshtml.cs`
- Modify: `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/ShiftAssignmentServiceTests.cs:42`

**NOTE:** These MUST be done in a single task because deleting TechShiftService breaks ShiftAssignmentService and the test file. All three consumers must be updated before the build can pass.

- [ ] **Step 1: Remove ITechShiftService dependency from ShiftAssignmentService**

In `Services/ShiftAssignmentService.cs`:
1. Remove `private readonly ITechShiftService _techShiftService;` field (around line 20)
2. Remove the `ITechShiftService techShiftService` constructor parameter (around line 32)
3. Remove the `_techShiftService = techShiftService;` assignment

- [ ] **Step 2: Replace TECH_SHIFT_INELIGIBLE in ValidateShiftAssignmentAsync (lines 272-284)**

Replace the existing block:
```csharp
// BEFORE (DELETE):
if (!string.IsNullOrEmpty(shiftInstance.ShiftType.TechShiftType))
{
    var isEligible = await _techShiftService.IsUserEligibleForTechShiftAsync(userId, shiftInstance.ShiftType.TechShiftType);
    if (!isEligible)
    {
        warnings.Add(new ValidationIssue("TECH_SHIFT_INELIGIBLE", ...));
    }
}

// AFTER:
// Company eligibility check (data-driven — replaces grant-based TechShiftService)
var eligibleCompanyIds = shiftInstance.ShiftType.GetEligibleCompanyIdList();
if (eligibleCompanyIds != null && !eligibleCompanyIds.Contains(user.CompanyId))
{
    errors.Add(new ValidationIssue(
        "COMPANY_INELIGIBLE",
        _localizer["Error_CompanyIneligibleForShift"],
        ValidationSeverity.Error,
        ValidationCategory.TechShift));
}

// Officer rank check
if (shiftInstance.ShiftType.RequiresOfficerRank && !user.Rank.IsOfficer())
{
    errors.Add(new ValidationIssue(
        "OFFICER_RANK_REQUIRED",
        _localizer["Error_OfficerRankRequired"],
        ValidationSeverity.Error,
        ValidationCategory.TechShift));
}
```

**IMPORTANT:** These go in the `errors` list (hard block), NOT `warnings`. This is an intentional upgrade from Warning→Error per spec — company membership and rank are structural facts.

- [ ] **Step 3: Apply identical logic in ValidateBatchAsync (lines 903-911)**

Replace the same `TECH_SHIFT_INELIGIBLE` block with the identical new checks. Keep both callsites consistent per MEMORY.md rule: "`ValidateBatchAsync` (FillRange) must stay consistent with single validation defaults and algorithms."

- [ ] **Step 4: Update ShiftAssignmentServiceTests.cs**

In `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/ShiftAssignmentServiceTests.cs`:
1. Remove the `Mock<ITechShiftService>()` (around line 42)
2. Remove the mock from the `ShiftAssignmentService` constructor call
3. Remove any test setups that configure `_techShiftService` mock behavior

- [ ] **Step 5: Fix Eligible.cshtml.cs**

In `Pages/Api/TechShift/Eligible.cshtml.cs`, remove the `ITechShiftService` dependency and replace with a deprecation response:

```csharp
// Remove constructor injection of ITechShiftService
// Replace handler body with:
_logger.LogWarning("TechShift/Eligible API is deprecated. Use Calendar shift-type-based eligibility.");
return new JsonResult(new { users = new List<object>(), deprecated = true });
```

- [ ] **Step 6: Remove DI registration from Program.cs**

In `Program.cs`, delete line 327:
```csharp
builder.Services.AddScoped<ITechShiftService, TechShiftService>();
```

- [ ] **Step 7: Delete service files**

```bash
rm Services/ITechShiftService.cs Services/TechShiftService.cs
```

- [ ] **Step 8: Add localization keys**

In `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx`, add:
- `Error_CompanyIneligibleForShift` = "User's company is not eligible for this shift type" / "החברה של המשתמש אינה זכאית לסוג משמרת זה"
- `Error_OfficerRankRequired` = "This shift type requires officer rank" / "סוג משמרת זה דורש דרגת קצין"

- [ ] **Step 9: Verify build (including test project) and commit**

Run: `dotnet build` (full build including tests)
Expected: Build succeeded

```bash
git add -A
git commit -m "refactor: delete TechShiftService, replace with ShiftType eligibility fields in validation"
```

---

## Chunk 4: SignalR Changes

### Task 11: Update CalendarHub + SignalR Group Pattern

**Files:**
- Modify: `Hubs/CalendarHub.cs:148-150,300`
- Modify: `wwwroot/js/calendar-realtime.js:73`

- [ ] **Step 1: Update CalendarGroups.Shifts signature**

In `Hubs/CalendarHub.cs`, around line 300:

```csharp
// BEFORE:
public static string Shifts(int moleculeId, int jobTypeId) => $"shifts-{moleculeId}-{jobTypeId}";

// AFTER:
public static string Shifts(int moleculeId, int? jobTypeId) => $"shifts-{moleculeId}-{jobTypeId ?? 0}";
```

- [ ] **Step 2: Update hub group validation**

In `Hubs/CalendarHub.cs`, around lines 148-150, update the validation to accept `0` as a valid jobTypeId segment:

The existing validation parses `shifts-{moleculeId}-{jobTypeId}` and expects valid integers. Ensure `0` is accepted (it already would be as a valid integer, but verify no `> 0` check exists).

- [ ] **Step 3: Update client-side group name building**

In `wwwroot/js/calendar-realtime.js`, line 73:

```javascript
// BEFORE:
return `shifts-${scope.moleculeId}-${scope.jobTypeId}`;

// AFTER:
return `shifts-${scope.moleculeId}-${scope.jobTypeId ?? 0}`;
```

- [ ] **Step 4: Update Table.cshtml.cs notification callsites**

In `Pages/Calendar/Table.cshtml.cs`, find all 10 callsites that look like:

```csharp
if (shiftType?.MoleculeId != null && shiftType?.JobTypeId != null)
{
    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId.Value);
```

Change each to:

```csharp
if (shiftType?.MoleculeId != null)
{
    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
```

This removes the `JobTypeId != null` guard so Tech shift notifications fire, and passes nullable `JobTypeId` directly.

**Locations (verify line numbers against current file):**
Lines ~817, 884, 941, 1078, 1167, 1258, 1321, 1414, 1561, 2284

- [ ] **Step 5: Verify build and commit**

Run: `dotnet build --no-restore`
Expected: Build succeeded

```bash
git add Hubs/CalendarHub.cs wwwroot/js/calendar-realtime.js Pages/Calendar/Table.cshtml.cs
git commit -m "feat: SignalR group pattern supports nullable jobTypeId for Tech molecules"
```

---

## Chunk 5: Calendar Page Changes

### Task 12: Update Calendar/Shifts for Tech Molecules

**Files:**
- Modify: `Pages/Calendar/Shifts.cshtml.cs`
- Modify: `Pages/Calendar/Shifts.cshtml`

**Reference:** Read both files completely before editing. Pay special attention to the `OnGetAsync` method and JS scope initialization.

- [ ] **Step 1: Load molecule type for Tech detection**

In `Shifts.cshtml.cs`, in `OnGetAsync()`, after loading `SelectedMolecule` (around line 154), add a property to expose it:

```csharp
public bool IsTechMolecule { get; set; }

// In OnGetAsync, after SelectedMolecule is set:
IsTechMolecule = SelectedMolecule?.Type == MoleculeType.Tech;
```

- [ ] **Step 2: Update guard conditions for Tech molecules**

In `OnGetAsync()`, update the guards around lines 177-193:

```csharp
// BEFORE:
if (MoleculeId.HasValue && JobTypeId.HasValue)
{
    if (Mode == "user")
        await BuildUserBasedCalendarAsync(MoleculeId.Value, JobTypeId.Value);
    else
        await BuildShiftBasedCalendarAsync(MoleculeId.Value, JobTypeId.Value);
}

if (MoleculeId.HasValue && JobTypeId.HasValue)
{
    Users = await _calendarService.GetUsersForCalendarAsync(MoleculeId.Value, JobTypeId.Value);
}

// AFTER:
if (MoleculeId.HasValue && (JobTypeId.HasValue || IsTechMolecule))
{
    if (Mode == "user")
        await BuildUserBasedCalendarAsync(MoleculeId.Value, JobTypeId);
    else
        await BuildShiftBasedCalendarAsync(MoleculeId.Value, JobTypeId);
}

if (MoleculeId.HasValue && (JobTypeId.HasValue || IsTechMolecule))
{
    Users = await _calendarService.GetUsersForCalendarAsync(MoleculeId.Value, JobTypeId);
}
```

- [ ] **Step 3: Update BuildShiftBasedCalendarAsync to accept nullable jobTypeId**

Change signature from `(int moleculeId, int jobTypeId)` to `(int moleculeId, int? jobTypeId)`.

Update the shift types query:
```csharp
var shiftTypesQuery = _db.ShiftTypes
    .IgnoreQueryFilters()
    .Where(st => st.MoleculeId == moleculeId);

if (jobTypeId.HasValue)
    shiftTypesQuery = shiftTypesQuery.Where(st => st.JobTypeId == jobTypeId.Value);

var shiftTypes = await shiftTypesQuery
    .OrderBy(st => st.Start)
    .ToListAsync();
```

Apply same pattern to `BuildUserBasedCalendarAsync`.

- [ ] **Step 4: Update Shifts.cshtml — hide JobType dropdown for Tech**

In `Pages/Calendar/Shifts.cshtml`, find the JobType dropdown (combobox labeled "סוג עבודה") and conditionally hide it:

```html
@if (!Model.IsTechMolecule)
{
    <div class="calendar-filter">
        <label>@Localizer["JobType"]</label>
        <select name="JobTypeId" ...>
            @foreach (var jt in Model.AvailableJobTypes) { ... }
        </select>
    </div>
}
```

- [ ] **Step 5: Update JS scope initialization in Shifts.cshtml**

Find the JS block that initializes the SignalR scope (around line 234) and update:

```javascript
// Ensure jobTypeId is 0 for Tech molecules
jobTypeId: @(Model.JobTypeId?.ToString() ?? "0")
```

- [ ] **Step 6: Verify build and commit**

Run: `dotnet build --no-restore`
Expected: Build succeeded

```bash
git add Pages/Calendar/Shifts.cshtml.cs Pages/Calendar/Shifts.cshtml
git commit -m "feat: Calendar/Shifts single-view mode for Tech molecules"
```

---

### Task 13: Update Calendar/Table for Tech Molecules

**Files:**
- Modify: `Pages/Calendar/Table.cshtml.cs`
- Modify: `Pages/Calendar/Table.cshtml`

- [ ] **Step 1: Update IsMoleculeMode property**

In `Table.cshtml.cs`, around line 90:

```csharp
// BEFORE:
public bool IsMoleculeMode => MoleculeId.HasValue && JobTypeId.HasValue;

// AFTER:
public bool IsMoleculeMode => MoleculeId.HasValue && (JobTypeId.HasValue || IsTechMolecule);

public bool IsTechMolecule { get; set; }
```

Set `IsTechMolecule` in the `OnGetAsync` or `OnPostAsync` methods after loading the molecule.

- [ ] **Step 2: Update guards in OnGetAsync/LoadMoleculeData**

Apply same pattern as Shifts: `(JobTypeId.HasValue || IsTechMolecule)` replaces `JobTypeId.HasValue` in guards.

- [ ] **Step 3: Update Table.cshtml JS scope**

Similar to Shifts.cshtml, ensure the JS scope uses `jobTypeId ?? 0` for SignalR.

- [ ] **Step 4: Verify build and commit**

Run: `dotnet build --no-restore`
Expected: Build succeeded

```bash
git add Pages/Calendar/Table.cshtml.cs Pages/Calendar/Table.cshtml
git commit -m "feat: Calendar/Table supports Tech molecules (IsMoleculeMode, nullable JobTypeId)"
```

---

### Task 14: Update GetShiftsData API

**Files:**
- Modify: `Pages/Api/Calendar/GetShiftsData.cshtml.cs`

- [ ] **Step 1: Fix jobTypeId validation gate**

Around line 66, update the validation:

```csharp
// BEFORE:
if (moleculeId <= 0 || jobTypeId <= 0)
    return BadRequest(new { error = "..." });

// AFTER:
if (moleculeId <= 0)
    return BadRequest(new { error = "Invalid moleculeId" });

// For Tech molecules, jobTypeId may be 0 (single calendar view)
if (jobTypeId <= 0)
{
    var molecule = await _db.Molecules.FindAsync(moleculeId);
    if (molecule?.Type != MoleculeType.Tech)
        return BadRequest(new { error = "Invalid jobTypeId" });
    jobTypeId = 0; // Sentinel for Tech
}
```

- [ ] **Step 2: Update downstream queries**

When `jobTypeId == 0`, query shift data by `MoleculeId` only (no JobTypeId filter). Apply same nullable pattern.

- [ ] **Step 3: Verify build and commit**

Run: `dotnet build --no-restore`
Expected: Build succeeded

```bash
git add Pages/Api/Calendar/GetShiftsData.cshtml.cs
git commit -m "feat: GetShiftsData API accepts jobTypeId=0 for Tech molecules"
```

---

## Chunk 6: Signup Flow + Admin + Cleanup

### Task 15: Update Signup Flow

**Files:**
- Modify: `Pages/Auth/Signup.cshtml:585-608`
- Modify: `Pages/Auth/Signup.cshtml.cs:217-244`

- [ ] **Step 1: Remove Tech-specific JS branch in Signup.cshtml**

In `Pages/Auth/Signup.cshtml`, find the molecule change handler (around line 585-608). Remove the `if (currentMoleculeIsTech)` branch that hides JobType and shows Department label. Tech molecules should now show the same UI as Workforce: JobType dropdown visible, Company label stays "Company."

There are **4 Tech-specific JS blocks** that need updating. Keep `currentMoleculeIsTech` variable — it's still used for role template filtering.

**Block 1 — Molecule change handler (lines ~598-608):** Remove the `if (currentMoleculeIsTech)` branch that hides JobType:
```javascript
// BEFORE: if (currentMoleculeIsTech) { hide jobType } else { show jobType, load... }
// AFTER (always show JobType, always load):
if (jobTypeField) jobTypeField.style.display = '';
if (jobTypeSelect) jobTypeSelect.required = true;
loadJobTypesForMolecule(moleculeId);
```

**Block 2 — `loadCompaniesForMolecule` function (lines ~518-549):** Remove the `if (currentMoleculeIsTech)` branch that loads Departments. Always load Companies:
```javascript
// BEFORE: if (currentMoleculeIsTech) { fetch departments } else { fetch companies }
// AFTER: always fetch companies
if (companyLabel) companyLabel.textContent = '@Localizer["Company"].Value.Trim()';
// ... fetch companies code only (delete the departments fetch branch)
```

**Block 3 — Post-back restoration (lines ~691-698):** Remove the `if (currentMoleculeIsTech)` branch that hides JobType on post-back:
```javascript
// BEFORE: if (currentMoleculeIsTech) { hide jobType }
// AFTER: always show jobType and load
if (jobTypeField) jobTypeField.style.display = '';
if (jobTypeSelect) jobTypeSelect.required = true;
loadJobTypesForMolecule(moleculeSelect.value);
```

**Block 4 — Company change event (lines ~665-670):** Remove the `if (currentMoleculeIsTech && departmentIdInput)` branch that syncs DepartmentId:
```javascript
// BEFORE: if (currentMoleculeIsTech && departmentIdInput) { departmentIdInput.value = companySelect.value; }
// AFTER: remove this block entirely — DepartmentId hidden field is no longer used
```

- [ ] **Step 2: Remove Tech-specific server block in Signup.cshtml.cs**

In `Pages/Auth/Signup.cshtml.cs`, remove or simplify lines 217-244 (the `else if (moleculeType == MoleculeType.Tech ...)` blocks):

```csharp
// DELETE the two else-if blocks that:
// 1. Require DepartmentId for Tech
// 2. Auto-assign HQ company
// 3. Clear JobTypeId = 0
```

Tech users now follow the standard path: they select a Company directly and optionally a JobType.

- [ ] **Step 3: Verify build and commit**

Run: `dotnet build --no-restore`
Expected: Build succeeded

```bash
git add Pages/Auth/Signup.cshtml Pages/Auth/Signup.cshtml.cs
git commit -m "feat: signup flow treats Tech molecules like Workforce (Company + optional JobType)"
```

---

### Task 16: Admin Pages Updates

**Files:**
- Modify: `Pages/Admin/Users.cshtml.cs` — Tech users show Company not Department
- Modify: `Pages/Admin/EditProfile.cshtml.cs` — Company dropdown for Tech users

- [ ] **Step 1: Update Admin/Users for Tech molecule users**

In `Pages/Admin/Users.cshtml.cs`, find any places that display Department info for Tech users. After migration, Tech users have `CompanyId` (not `DepartmentId`), so they should appear correctly in the user list with their Company. Verify:
- User list shows Company name (not Department)
- Filtering by Company works for Shikma users
- If there's a "Department" column shown for Tech users, hide or repurpose it

- [ ] **Step 2: Update EditProfile for Tech users**

In `Pages/Admin/EditProfile.cshtml.cs`, ensure Tech users see:
- Company dropdown (not Department)
- JobType dropdown shows Shikma-specific types (ProjectManager, Hakam)

Check the `IsTechMolecule` / `IsWorkforceMolecule` flags — if the page conditionally shows Department dropdown for Tech, update to show Company dropdown instead.

- [ ] **Step 3: Verify build and commit**

Run: `dotnet build --no-restore`
Expected: Build succeeded

```bash
git add Pages/Admin/Users.cshtml.cs Pages/Admin/EditProfile.cshtml.cs
git commit -m "chore: update Admin/Users and EditProfile for Tech molecule convergence"
```

---

### Task 17: Data Migration for Existing Records

**Files:**
- Create or modify: A data migration script or seed extension

**CONTEXT:** The seed data changes (Tasks 4-6) only apply to fresh databases. For the existing development database with real Shikma users, we need a one-time data migration. This is spec Section 8c.

- [ ] **Step 1: Create a data migration method**

Add a migration method to `ShiftyOrganizationSeed` (or a new `TechMoleculeConvergenceMigration` class) that:

1. Finds all users with `DepartmentId` pointing to a Shikma department
2. Maps each department to the corresponding new Company:
   - Pie → shikPie company
   - Tao → shikTao company
   - Yekev → shikYekev company
   - Snir → shikSnir company
   - Arbel → shikArbel company
   - Samapkam → shikSamapkamia company
3. Sets `user.CompanyId` to the new Company, clears `user.DepartmentId`
4. Finds users with `RoleTemplateId == 9` (DepartmentLead):
   - Revokes DepartmentLead grants via `_grantService`
   - Assigns BRDirector template with Company scope
   - Updates `user.RoleTemplateId` to BRDirector's ID (2)
5. Updates existing Tech ShiftType records with `EligibleCompanyIds` and `RequiresOfficerRank`

- [ ] **Step 2: Wire the migration into Program.cs**

Add a one-time migration call in the startup seeding section (after line 824), guarded by a check:

```csharp
// One-time Tech molecule convergence migration
if (await db.Departments.AnyAsync(d => d.MoleculeId == shikmaId))
{
    await TechMoleculeConvergenceMigration.RunAsync(db, grantService, logger);
}
```

- [ ] **Step 3: Test the migration locally**

1. Run the app with the existing database
2. Verify Shikma users now have CompanyId (not just DepartmentId)
3. Verify DepartmentLead users are now BRDirector
4. Verify Tech shift types have EligibleCompanyIds populated

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "migration: one-time Tech molecule convergence data migration"
```

---

### Task 18: Final Build Verification + Integration Test

- [ ] **Step 1: Full build**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Run existing tests**

Run: `dotnet test`
Expected: All existing tests pass (some ShiftAssignment tests may need updating for the new validation logic)

- [ ] **Step 3: Manual smoke test**

1. Start the app: `dotnet run`
2. Navigate to `http://localhost:5000/Auth/Signup`
3. Select שקמה molecule — verify JobType dropdown shows and Companies appear (not Departments)
4. Log in as `shik@a` / `123456`
5. Navigate to Calendar/Shifts — verify single calendar view with all Tech shift types
6. Verify JobType dropdown is hidden for Shikma

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "feat: Tech molecule convergence complete — Shikma uses Companies with shift eligibility rules"
```
