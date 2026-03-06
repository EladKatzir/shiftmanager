# Role-Grant Matrix Phase 1 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement Phase 1 of the role-grant matrix redesign — schema changes, GrantService fixes, login flow updates, and complete seed data rewrite.

**Architecture:** Add `TargetJobTypeId` + `UseOwnJobType` to `RoleTemplateGrant` and `ExpandToProject` to `GrantScopeMode`. Fix the critical SAR scope cascade bug in `DetermineEffectiveScope`. Simplify `BuildGrantScopeForUserAsync` to role-independent. Rewrite seed data with ~500 grant entries matching the approved design.

**Tech Stack:** ASP.NET Core 8.0, EF Core, SQLite, C#

**Design Doc:** `docs/plans/2026-02-24-role-grant-matrix-design.md` (Rev 3)

---

## Grant ID Quick Reference

All IDs from `Data/SeedData/GrantTypeSeed.cs` (sequential `id++` from 1):

| ID | Key | ID | Key | ID | Key |
|----|-----|----|-----|----|-----|
| 1 | ViewShifts | 2 | ViewAllShifts | 3 | AssignAlhutShifts |
| 4 | AssignTextShifts | 5 | AssignBRShifts | 6 | AssignTechShifts |
| 7 | EditShiftPrograms | 8 | CreateShiftPrograms | 9 | DeleteShiftPrograms |
| 10 | EditShiftTypes | 11 | CreateShiftTypes | 12 | ViewDuties |
| 13 | AssignHakamDuties | 14 | AssignKatzinDuties | 15 | EditDutyPrograms |
| 16 | ViewChores | 17 | AssignChores | 18 | EditChoreTypes |
| 19 | CreateChoreTypes | 20 | ViewVacations | 21 | RequestVacation |
| 22 | ApproveVacations | 23 | OverrideVacationLimits | 24 | ApproveExtendedLeave |
| 25 | RequestSwap | 26 | ApproveSwaps | 27 | InitiateSwap |
| 28 | ViewUsers | 29 | EditUsers | 30 | CreateUsers |
| 31 | DeactivateUsers | 32 | ResetPasswords | 33 | AssignJobTypes |
| 34 | ViewAllUsers | 35 | ViewGrants | 36 | AssignGrants |
| 37 | RevokeGrants | 38 | AssignRoles | 39 | ViewHierarchy |
| 40 | EditCompany | 41 | EditMolecule | 42 | EditArea |
| 43 | CreateCompany | 44 | CreateMolecule | 45 | ManageShiftGroupings |
| 46 | ManageJobTypes | 47 | ManageDepartments | 48 | ViewSettings |
| 49 | EditCompanySettings | 50 | EditMoleculeSettings | 51 | EditAreaSettings |
| 52 | ViewAnalytics | 53 | ViewReports | 54 | ExportData |
| 55 | SendNotifications | 56 | ConfigureEmailSettings | 57 | AdminAccess |
| 58 | SystemConfiguration | 59 | ViewAuditLog | 60 | ManageApiKeys |
| 61 | ViewAlhutShiftCalendar | 62 | ViewTextShiftCalendar | 63 | ViewBRShiftCalendar |
| 64 | ViewHakamShiftCalendar | 65 | CanBeAssignedAlhutShifts | 66 | CanBeAssignedTextShifts |
| 67 | CanBeAssignedBRShifts | 68 | CanBeAssignedHakamShifts | 69 | ManageAlhutBlueprints |
| 70 | ManageAlhutPrograms | 71 | ManageTextBlueprints | 72 | ManageTextPrograms |
| 73 | ManageBRBlueprints | 74 | ManageBRPrograms | 75 | ManageHakamBlueprints |
| 76 | ManageHakamPrograms | 109 | ManageShiftCapacity | 110 | WriteOverviewNotes |
| 111 | ManageOnDutyTypes | 112 | AccessAdminNavigation | 113 | DirectorHubAccess |
| 114 | ManagerHomeAccess | 115 | ViewCompanyCalendar | 116 | ManageJoinRequests |
| 117 | ViewCompanyUsers | 118 | EditCompanyUsers | 119 | ManageAnnouncements |
| 120 | ViewSystemAlerts | 121 | ViewAllAreas | 122 | ManageOnDuty |
| 123 | ReorderHierarchy |

**RoleTemplate IDs**: Employee=1, BRDirector=2, AlhutLead=3, TextLead=4, AlhutDirector=5, TextDirector=6, MoleculeAdmin=7, Assigner=8, DepartmentLead=9, AreaAdmin=10, Owner=11, Trainee=12

---

### Task 1: Add ExpandToProject to GrantScopeMode Enum

**Files:**
- Modify: `Models/Support/GrantScopeMode.cs`

**Step 1: Add the new enum value**

Add `ExpandToProject = 4` after `Custom = 3`:

```csharp
namespace ShiftManager.Models.Support;

public enum GrantScopeMode
{
    SameAsRole = 0,        // Grant scope = company level (CompanyId + DepartmentId only)
    ExpandToMolecule = 1,  // Expand to molecule (shift assignment)
    ExpandToArea = 2,      // Expand to area (Katzin, Hakam)
    Custom = 3,            // Explicit scope
    ExpandToProject = 4    // Expand to project (Owner-level)
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors

**Step 3: Commit**

```bash
git add Models/Support/GrantScopeMode.cs
git commit -m "feat: add ExpandToProject=4 to GrantScopeMode enum"
```

---

### Task 2: Add TargetJobTypeId + UseOwnJobType to RoleTemplateGrant

**Files:**
- Modify: `Models/RoleTemplateGrant.cs`

**Step 1: Add the two new properties**

After `IsOverride`, add:

```csharp
public int? TargetJobTypeId { get; set; }  // Explicit JobType (e.g., Hakam for BRDirector dual ApproveVacations)
public bool UseOwnJobType { get; set; }     // Resolve to user's own JobTypeId at login
```

Full file becomes:

```csharp
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class RoleTemplateGrant
{
    public int Id { get; set; }
    public int RoleTemplateId { get; set; }
    public int GrantTypeId { get; set; }
    public bool CanOwn { get; set; }
    public bool CanGive { get; set; }
    public GrantScopeMode ScopeMode { get; set; }
    public bool IsOverride { get; set; }  // Owner modified default
    public int? TargetJobTypeId { get; set; }  // Explicit JobType (e.g., Hakam for BRDirector)
    public bool UseOwnJobType { get; set; }     // Resolve to user's own JobTypeId at login

    public RoleTemplate RoleTemplate { get; set; } = null!;
    public GrantType GrantType { get; set; } = null!;
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors

**Step 3: Create EF migration**

Run: `dotnet ef migrations add AddJobTypeScopingToRoleTemplateGrant`
Expected: Migration file created successfully

**Step 4: Verify migration SQL**

Read the generated migration file. It should:
- Add `TargetJobTypeId` (int?, nullable) column to `RoleTemplateGrants`
- Add `UseOwnJobType` (bool, default false) column to `RoleTemplateGrants`

**Step 5: Apply migration**

Run: `dotnet ef database update`
Expected: Success

**Step 6: Commit**

```bash
git add Models/RoleTemplateGrant.cs Migrations/
git commit -m "feat: add TargetJobTypeId + UseOwnJobType columns to RoleTemplateGrant"
```

---

### Task 3: Fix DetermineEffectiveScope (CRITICAL — scope cascade bug)

**Files:**
- Modify: `Services/GrantService.cs` (lines 455-465)

**Context — why this is critical:**

The current code returns `roleScope` unchanged for `SameAsRole`, which passes through the FULL hierarchy (ProjectId, AreaId, MoleculeId, CompanyId). `GetAccessibleCompanyIdsForGrantAsync` evaluates scope from broadest to narrowest with early-return (`continue`). If an Employee's SAR grant has ProjectId set, the cascade returns ALL companies in the project — massive privilege escalation.

**Step 1: Replace DetermineEffectiveScope**

Find in `Services/GrantService.cs` (around line 455):

```csharp
private GrantScope DetermineEffectiveScope(GrantScopeMode scopeMode, GrantScope roleScope)
{
    return scopeMode switch
    {
        GrantScopeMode.SameAsRole => roleScope,
        GrantScopeMode.ExpandToMolecule => new GrantScope(MoleculeId: roleScope.MoleculeId),
        GrantScopeMode.ExpandToArea => new GrantScope(AreaId: roleScope.AreaId),
        GrantScopeMode.Custom => roleScope,
        _ => roleScope
    };
}
```

Replace with:

```csharp
/// <summary>
/// Extracts exactly ONE scope level from the full hierarchy roleScope.
/// CRITICAL: SAR must extract ONLY CompanyId+DepartmentId — NOT the full hierarchy.
/// Reason: GetAccessibleCompanyIdsForGrantAsync cascades broadest→narrowest with continue.
/// If a Grant has both ProjectId AND CompanyId, ProjectId fires first and returns ALL
/// companies in the project, massively over-granting an Employee.
/// </summary>
private GrantScope DetermineEffectiveScope(GrantScopeMode scopeMode, GrantScope roleScope)
{
    return scopeMode switch
    {
        // SAR: Company level only — strip all hierarchy above company
        GrantScopeMode.SameAsRole => new GrantScope(
            CompanyId: roleScope.CompanyId,
            DepartmentId: roleScope.DepartmentId
        ),
        GrantScopeMode.ExpandToMolecule => new GrantScope(MoleculeId: roleScope.MoleculeId),
        GrantScopeMode.ExpandToArea => new GrantScope(AreaId: roleScope.AreaId),
        GrantScopeMode.ExpandToProject => new GrantScope(ProjectId: roleScope.ProjectId),
        GrantScopeMode.Custom => roleScope,
        _ => throw new ArgumentOutOfRangeException(nameof(scopeMode), $"Unknown GrantScopeMode: {scopeMode}")
    };
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors

**Step 3: Commit**

```bash
git add Services/GrantService.cs
git commit -m "fix: CRITICAL — SAR extracts CompanyId+DeptId only, add ETP, throw on unknown scope"
```

---

### Task 4: Update ApplyAutoGrantsAsync for JobType Resolution + Stale Cleanup

**Files:**
- Modify: `Services/GrantService.cs` (lines 386-434)

**Step 1: Replace ApplyAutoGrantsAsync**

Find the current method (around line 386-434) and replace with:

```csharp
// Auto-grants from roles
public async Task ApplyAutoGrantsAsync(int userId, int roleTemplateId, GrantScope roleScope)
{
    var roleTemplate = await _db.RoleTemplates
        .Include(rt => rt.AutoGrants)
        .ThenInclude(ag => ag.GrantType)
        .FirstOrDefaultAsync(rt => rt.Id == roleTemplateId);

    if (roleTemplate == null)
        return;

    // Phase 1: Resolve all expected grants from template
    var expectedGrants = new List<(int GrantTypeId, GrantScope EffectiveScope, bool CanOwn, bool CanGive)>();

    foreach (var autoGrant in roleTemplate.AutoGrants)
    {
        // Step 1: Resolve JobTypeId from TargetJobTypeId / UseOwnJobType
        int? resolvedJobTypeId = null;
        if (autoGrant.TargetJobTypeId != null)
            resolvedJobTypeId = autoGrant.TargetJobTypeId;
        else if (autoGrant.UseOwnJobType)
            resolvedJobTypeId = roleScope.JobTypeId;
        // else: null = all jobtypes

        // Step 2: Determine effective scope (extracts ONE level from roleScope)
        var effectiveScope = DetermineEffectiveScope(autoGrant.ScopeMode, roleScope);

        // Step 3: Overlay resolved JobTypeId
        effectiveScope = effectiveScope with { JobTypeId = resolvedJobTypeId };

        expectedGrants.Add((autoGrant.GrantTypeId, effectiveScope, autoGrant.CanOwn, autoGrant.CanGive));
    }

    // Phase 2: Clean up stale auto-grants (JobType changed, template modified, etc.)
    var existingAutoGrants = await _db.Grants
        .Where(g => g.UserId == userId && g.IsAutoGrant &&
               g.Notes != null && g.Notes.Contains($"role: {roleTemplate.Key}"))
        .ToListAsync();

    foreach (var existing in existingAutoGrants)
    {
        var stillExpected = expectedGrants.Any(e =>
            e.GrantTypeId == existing.GrantTypeId &&
            e.EffectiveScope.ProjectId == existing.ProjectId &&
            e.EffectiveScope.AreaId == existing.AreaId &&
            e.EffectiveScope.MoleculeId == existing.MoleculeId &&
            e.EffectiveScope.DepartmentId == existing.DepartmentId &&
            e.EffectiveScope.CompanyId == existing.CompanyId &&
            e.EffectiveScope.JobTypeId == existing.JobTypeId);

        if (!stillExpected)
        {
            _db.Grants.Remove(existing);
        }
    }

    // Phase 3: Insert new grants (dedup includes JobTypeId)
    foreach (var (grantTypeId, effectiveScope, canOwn, canGive) in expectedGrants)
    {
        var existing = await _db.Grants.FirstOrDefaultAsync(g =>
            g.UserId == userId && g.GrantTypeId == grantTypeId &&
            g.ProjectId == effectiveScope.ProjectId && g.AreaId == effectiveScope.AreaId &&
            g.MoleculeId == effectiveScope.MoleculeId && g.DepartmentId == effectiveScope.DepartmentId &&
            g.CompanyId == effectiveScope.CompanyId && g.JobTypeId == effectiveScope.JobTypeId);

        if (existing == null)
        {
            _db.Grants.Add(new Grant
            {
                UserId = userId,
                GrantTypeId = grantTypeId,
                ProjectId = effectiveScope.ProjectId,
                AreaId = effectiveScope.AreaId,
                MoleculeId = effectiveScope.MoleculeId,
                DepartmentId = effectiveScope.DepartmentId,
                CompanyId = effectiveScope.CompanyId,
                JobTypeId = effectiveScope.JobTypeId,
                CanOwn = canOwn,
                CanGive = canGive,
                GrantedAt = DateTime.UtcNow,
                IsAutoGrant = true,
                Notes = $"Auto-granted from role: {roleTemplate.Key}"
            });
        }
    }

    await _db.SaveChangesAsync();
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors

**Step 3: Commit**

```bash
git add Services/GrantService.cs
git commit -m "feat: ApplyAutoGrantsAsync with JobType resolution + stale grant cleanup"
```

---

### Task 5: Simplify BuildGrantScopeForUserAsync to Role-Independent

**Files:**
- Modify: `Services/GrantService.cs` (lines 731-758)

**Context:** The current method has a role-specific switch statement. Since `DetermineEffectiveScope` now handles per-grant scope selection, `BuildGrantScopeForUserAsync` just needs to return the full hierarchy as raw material.

**Step 1: Replace BuildGrantScopeForUserAsync**

Find (around line 731):

```csharp
private async Task<GrantScope> BuildGrantScopeForUserAsync(AppUser user, string roleTemplateKey)
```

Replace the entire method with:

```csharp
/// <summary>
/// Builds a FULL hierarchy GrantScope for the user. This is raw material —
/// DetermineEffectiveScope extracts exactly one level per grant's ScopeMode.
/// Role-independent: the role-specific behavior is encoded in each grant's ScopeMode.
/// </summary>
private async Task<GrantScope> BuildGrantScopeForUserAsync(AppUser user, string roleTemplateKey)
{
    var company = await _db.Companies
        .IgnoreQueryFilters()
        .Include(c => c.Molecule)
            .ThenInclude(m => m!.Area)
                .ThenInclude(a => a!.Project)
        .FirstOrDefaultAsync(c => c.Id == user.CompanyId);

    if (company == null)
        return GrantScope.Company(user.CompanyId);

    // Full hierarchy — DetermineEffectiveScope extracts the right level per grant
    return new GrantScope(
        ProjectId: company.Molecule?.Area?.ProjectId,
        AreaId: company.Molecule?.AreaId,
        MoleculeId: company.MoleculeId,
        DepartmentId: user.DepartmentId,
        CompanyId: user.CompanyId,
        JobTypeId: user.JobTypeId
    );
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors

**Step 3: Commit**

```bash
git add Services/GrantService.cs
git commit -m "refactor: simplify BuildGrantScopeForUserAsync to role-independent full hierarchy"
```

---

### Task 6: Update Login roleScope in Login.cshtml.cs

**Files:**
- Modify: `Pages/Auth/Login.cshtml.cs` (lines 280-285)

**Step 1: Add DepartmentId and JobTypeId to roleScope**

Find (around line 280):

```csharp
var roleScope = new GrantScope(
    ProjectId: hierarchyContext?.Path.Project?.Id,
    AreaId: hierarchyContext?.Path.Area?.Id,
    MoleculeId: hierarchyContext?.Path.Molecule?.Id,
    CompanyId: user.CompanyId
);
```

Replace with:

```csharp
var roleScope = new GrantScope(
    ProjectId: hierarchyContext?.Path.Project?.Id,
    AreaId: hierarchyContext?.Path.Area?.Id,
    MoleculeId: hierarchyContext?.Path.Molecule?.Id,
    DepartmentId: user.DepartmentId,
    CompanyId: user.CompanyId,
    JobTypeId: hierarchyContext?.JobType?.Id
);
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors

**Step 3: Commit**

```bash
git add Pages/Auth/Login.cshtml.cs
git commit -m "feat: add DepartmentId + JobTypeId to login roleScope for full hierarchy"
```

---

### Task 7: Update Login roleScope in GriffinService.cs

**Files:**
- Modify: `Services/GriffinService.cs` (lines 253-258)

**Step 1: Add DepartmentId and JobTypeId to roleScope**

Find (around line 253):

```csharp
var roleScope = new GrantScope(
    ProjectId: hierarchyContext?.Path.Project?.Id,
    AreaId: hierarchyContext?.Path.Area?.Id,
    MoleculeId: hierarchyContext?.Path.Molecule?.Id,
    CompanyId: user.CompanyId
);
```

Replace with:

```csharp
var roleScope = new GrantScope(
    ProjectId: hierarchyContext?.Path.Project?.Id,
    AreaId: hierarchyContext?.Path.Area?.Id,
    MoleculeId: hierarchyContext?.Path.Molecule?.Id,
    DepartmentId: user.DepartmentId,
    CompanyId: user.CompanyId,
    JobTypeId: hierarchyContext?.JobType?.Id
);
```

Note: `user.DepartmentId` is available because `AppUser` has `public int? DepartmentId { get; set; }` (line 65 in AppUser.cs).

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors

**Step 3: Commit**

```bash
git add Services/GriffinService.cs
git commit -m "feat: add DepartmentId + JobTypeId to Griffin login roleScope"
```

---

### Task 8: Update Program.cs Seeding Dedup Key

**Files:**
- Modify: `Program.cs` (lines 588-604)

**Context:** Current dedup key is `{RoleTemplateId}:{GrantTypeId}`. This drops the second `ApproveVacations` grant for BRDirector (different TargetJobTypeId). We also need to include `TargetJobTypeId` in the SELECT to compare.

**Step 1: Update the dedup key**

Find (around line 588-604):

```csharp
// Seed grants for ALL templates (existing + new) to fill in any missing mappings
var roleTemplateGrants = ShiftManager.Data.SeedData.RoleTemplateSeed.GetRoleTemplateGrants();
var existingMappings = await db.RoleTemplateGrants
    .Select(g => new { g.RoleTemplateId, g.GrantTypeId })
    .ToListAsync();
var existingSet = existingMappings.Select(m => $"{m.RoleTemplateId}:{m.GrantTypeId}").ToHashSet();
var newMappings = roleTemplateGrants
    .Where(g => !existingSet.Contains($"{g.RoleTemplateId}:{g.GrantTypeId}"))
    .ToList();
```

Replace with:

```csharp
// Seed grants for ALL templates (existing + new) to fill in any missing mappings
// Dedup key includes TargetJobTypeId to support dual-JobType grants (e.g., BRDirector ApproveVacations for BR + Hakam)
var roleTemplateGrants = ShiftManager.Data.SeedData.RoleTemplateSeed.GetRoleTemplateGrants();
var existingMappings = await db.RoleTemplateGrants
    .Select(g => new { g.RoleTemplateId, g.GrantTypeId, g.TargetJobTypeId })
    .ToListAsync();
var existingSet = existingMappings.Select(m => $"{m.RoleTemplateId}:{m.GrantTypeId}:{m.TargetJobTypeId?.ToString() ?? "null"}").ToHashSet();
var newMappings = roleTemplateGrants
    .Where(g => !existingSet.Contains($"{g.RoleTemplateId}:{g.GrantTypeId}:{g.TargetJobTypeId?.ToString() ?? "null"}"))
    .ToList();
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors

**Step 3: Commit**

```bash
git add Program.cs
git commit -m "fix: seeding dedup key includes TargetJobTypeId for dual-JobType grants"
```

---

### Task 9: Rewrite RoleTemplateSeed.cs Grant Assignments

**Files:**
- Modify: `Data/SeedData/RoleTemplateSeed.cs` (lines 195-412)

**Context:** This is the biggest task. The entire `GetRoleTemplateGrants()` method is rewritten with the correct grant matrix from the design document Rev 3. All grants are explicitly listed per role — no inheritance at the seed level. Each role gets its complete set of grants.

**CRITICAL NOTES:**
- The old seed has off-by-one bugs: Employee gets ID 22 (ApproveVacations) instead of 21 (RequestVacation), ID 26 (ApproveSwaps) instead of 25 (RequestSwap)
- New fields `TargetJobTypeId` and `UseOwnJobType` default to null/false — only set explicitly where needed
- CanOwn is always true for auto-grants (user has the permission)
- CanGive is false by default, true only where the design specifies Y

**Step 1: Replace `GetRoleTemplateGrants()` method**

Replace the entire method body (from `public static List<RoleTemplateGrant> GetRoleTemplateGrants()` to its closing brace) with the complete grant matrix. The replacement code follows.

**IMPORTANT**: The method below is long (~500 grants). Each section corresponds to one role from the design document Section 5. Comments reference grant IDs and keys for cross-checking.

```csharp
public static List<RoleTemplateGrant> GetRoleTemplateGrants()
{
    var grants = new List<RoleTemplateGrant>();
    int id = 1;

    // Helper for concise grant creation
    RoleTemplateGrant G(int templateId, int grantTypeId, GrantScopeMode scope,
        bool canGive = false, int? targetJobTypeId = null, bool useOwnJobType = false)
        => new RoleTemplateGrant
        {
            Id = id++,
            RoleTemplateId = templateId,
            GrantTypeId = grantTypeId,
            CanOwn = true,
            CanGive = canGive,
            ScopeMode = scope,
            TargetJobTypeId = targetJobTypeId,
            UseOwnJobType = useOwnJobType
        };

    // Shorthand constants
    const GrantScopeMode SAR = GrantScopeMode.SameAsRole;
    const GrantScopeMode ETM = GrantScopeMode.ExpandToMolecule;
    const GrantScopeMode ETA = GrantScopeMode.ExpandToArea;
    const GrantScopeMode ETP = GrantScopeMode.ExpandToProject;

    // ============================================
    // EMPLOYEE (Template 1) — 19 grants
    // All SAR (company-scoped). Base for all workforce roles.
    // ============================================
    grants.Add(G(1, 1, SAR));    // ViewShifts
    grants.Add(G(1, 16, SAR));   // ViewChores
    grants.Add(G(1, 12, SAR));   // ViewDuties
    grants.Add(G(1, 20, SAR));   // ViewVacations
    grants.Add(G(1, 21, SAR));   // RequestVacation (NOT 22 ApproveVacations — old seed was off-by-one!)
    grants.Add(G(1, 25, SAR));   // RequestSwap (NOT 26 ApproveSwaps — old seed was off-by-one!)
    grants.Add(G(1, 115, SAR));  // ViewCompanyCalendar
    grants.Add(G(1, 117, SAR));  // ViewCompanyUsers
    grants.Add(G(1, 35, SAR));   // ViewGrants
    grants.Add(G(1, 39, SAR));   // ViewHierarchy
    grants.Add(G(1, 110, SAR));  // WriteOverviewNotes
    grants.Add(G(1, 61, SAR));   // ViewAlhutShiftCalendar
    grants.Add(G(1, 62, SAR));   // ViewTextShiftCalendar
    grants.Add(G(1, 63, SAR));   // ViewBRShiftCalendar
    grants.Add(G(1, 64, SAR));   // ViewHakamShiftCalendar
    grants.Add(G(1, 65, SAR, useOwnJobType: true));   // CanBeAssignedAlhutShifts (OWN)
    grants.Add(G(1, 66, SAR, useOwnJobType: true));   // CanBeAssignedTextShifts (OWN)
    grants.Add(G(1, 67, SAR, useOwnJobType: true));   // CanBeAssignedBRShifts (OWN)
    grants.Add(G(1, 68, SAR, useOwnJobType: true));   // CanBeAssignedHakamShifts (OWN)

    // ============================================
    // TRAINEE (Template 12) — 18 grants
    // Same as Employee minus RequestSwap
    // ============================================
    grants.Add(G(12, 1, SAR));    // ViewShifts
    grants.Add(G(12, 16, SAR));   // ViewChores
    grants.Add(G(12, 12, SAR));   // ViewDuties
    grants.Add(G(12, 20, SAR));   // ViewVacations
    grants.Add(G(12, 21, SAR));   // RequestVacation (FIXED: was 22)
    // NO RequestSwap for Trainee
    grants.Add(G(12, 115, SAR));  // ViewCompanyCalendar
    grants.Add(G(12, 117, SAR));  // ViewCompanyUsers
    grants.Add(G(12, 35, SAR));   // ViewGrants
    grants.Add(G(12, 39, SAR));   // ViewHierarchy
    grants.Add(G(12, 110, SAR));  // WriteOverviewNotes
    grants.Add(G(12, 61, SAR));   // ViewAlhutShiftCalendar
    grants.Add(G(12, 62, SAR));   // ViewTextShiftCalendar
    grants.Add(G(12, 63, SAR));   // ViewBRShiftCalendar
    grants.Add(G(12, 64, SAR));   // ViewHakamShiftCalendar
    grants.Add(G(12, 65, SAR, useOwnJobType: true));   // CanBeAssignedAlhutShifts (OWN)
    grants.Add(G(12, 66, SAR, useOwnJobType: true));   // CanBeAssignedTextShifts (OWN)
    grants.Add(G(12, 67, SAR, useOwnJobType: true));   // CanBeAssignedBRShifts (OWN)
    grants.Add(G(12, 68, SAR, useOwnJobType: true));   // CanBeAssignedHakamShifts (OWN)

    // ============================================
    // ASSIGNER (Template 8) — 20 grants
    // All Employee grants at SAR + AssignChores at ETM
    // ============================================
    // Employee base (SAR — NOT widened)
    grants.Add(G(8, 1, SAR));    // ViewShifts
    grants.Add(G(8, 16, SAR));   // ViewChores
    grants.Add(G(8, 12, SAR));   // ViewDuties
    grants.Add(G(8, 20, SAR));   // ViewVacations
    grants.Add(G(8, 21, SAR));   // RequestVacation
    grants.Add(G(8, 25, SAR));   // RequestSwap
    grants.Add(G(8, 115, SAR));  // ViewCompanyCalendar
    grants.Add(G(8, 117, SAR));  // ViewCompanyUsers
    grants.Add(G(8, 35, SAR));   // ViewGrants
    grants.Add(G(8, 39, SAR));   // ViewHierarchy
    grants.Add(G(8, 110, SAR));  // WriteOverviewNotes
    grants.Add(G(8, 61, SAR));   // ViewAlhutShiftCalendar
    grants.Add(G(8, 62, SAR));   // ViewTextShiftCalendar
    grants.Add(G(8, 63, SAR));   // ViewBRShiftCalendar
    grants.Add(G(8, 64, SAR));   // ViewHakamShiftCalendar
    grants.Add(G(8, 65, SAR, useOwnJobType: true));   // CanBeAssignedAlhutShifts (OWN)
    grants.Add(G(8, 66, SAR, useOwnJobType: true));   // CanBeAssignedTextShifts (OWN)
    grants.Add(G(8, 67, SAR, useOwnJobType: true));   // CanBeAssignedBRShifts (OWN)
    grants.Add(G(8, 68, SAR, useOwnJobType: true));   // CanBeAssignedHakamShifts (OWN)
    // Assigner extra: molecule-wide chore assignment
    grants.Add(G(8, 17, ETM));   // AssignChores (ETM — only this grant is molecule-scoped)

    // ============================================
    // ALHUT LEAD (Template 3) — 38 grants
    // Employee base (SAR) + lead-specific grants. AssignAlhutShifts at ETM.
    // ============================================
    // Employee base
    grants.Add(G(3, 1, SAR));    // ViewShifts
    grants.Add(G(3, 16, SAR));   // ViewChores
    grants.Add(G(3, 12, SAR));   // ViewDuties
    grants.Add(G(3, 20, SAR));   // ViewVacations
    grants.Add(G(3, 21, SAR));   // RequestVacation
    grants.Add(G(3, 25, SAR));   // RequestSwap
    grants.Add(G(3, 115, SAR));  // ViewCompanyCalendar
    grants.Add(G(3, 117, SAR));  // ViewCompanyUsers
    grants.Add(G(3, 35, SAR));   // ViewGrants
    grants.Add(G(3, 39, SAR));   // ViewHierarchy
    grants.Add(G(3, 110, SAR));  // WriteOverviewNotes
    grants.Add(G(3, 61, SAR));   // ViewAlhutShiftCalendar
    grants.Add(G(3, 62, SAR));   // ViewTextShiftCalendar
    grants.Add(G(3, 63, SAR));   // ViewBRShiftCalendar
    grants.Add(G(3, 64, SAR));   // ViewHakamShiftCalendar
    grants.Add(G(3, 65, SAR, useOwnJobType: true));   // CanBeAssignedAlhutShifts (OWN)
    grants.Add(G(3, 66, SAR, useOwnJobType: true));   // CanBeAssignedTextShifts (OWN)
    grants.Add(G(3, 67, SAR, useOwnJobType: true));   // CanBeAssignedBRShifts (OWN)
    grants.Add(G(3, 68, SAR, useOwnJobType: true));   // CanBeAssignedHakamShifts (OWN)
    // Lead-specific grants
    grants.Add(G(3, 3, ETM, useOwnJobType: true));    // AssignAlhutShifts (ETM, OWN)
    grants.Add(G(3, 2, SAR));    // ViewAllShifts
    grants.Add(G(3, 7, SAR, useOwnJobType: true));    // EditShiftPrograms (OWN)
    grants.Add(G(3, 8, SAR, useOwnJobType: true));    // CreateShiftPrograms (OWN)
    grants.Add(G(3, 9, SAR, useOwnJobType: true));    // DeleteShiftPrograms (OWN)
    grants.Add(G(3, 10, SAR, useOwnJobType: true));   // EditShiftTypes (OWN)
    grants.Add(G(3, 11, SAR, useOwnJobType: true));   // CreateShiftTypes (OWN)
    grants.Add(G(3, 69, SAR, useOwnJobType: true));   // ManageAlhutBlueprints (OWN)
    grants.Add(G(3, 70, SAR, useOwnJobType: true));   // ManageAlhutPrograms (OWN)
    grants.Add(G(3, 109, SAR, useOwnJobType: true));  // ManageShiftCapacity (OWN)
    grants.Add(G(3, 17, SAR));   // AssignChores
    grants.Add(G(3, 22, SAR, useOwnJobType: true));   // ApproveVacations (OWN)
    grants.Add(G(3, 26, SAR, useOwnJobType: true));   // ApproveSwaps (OWN)
    grants.Add(G(3, 28, SAR));   // ViewUsers
    grants.Add(G(3, 36, SAR, useOwnJobType: true));   // AssignGrants (OWN)
    grants.Add(G(3, 37, SAR, useOwnJobType: true));   // RevokeGrants (OWN)
    grants.Add(G(3, 112, SAR));  // AccessAdminNavigation
    grants.Add(G(3, 114, SAR));  // ManagerHomeAccess
    grants.Add(G(3, 120, SAR));  // ViewSystemAlerts

    // ============================================
    // TEXT LEAD (Template 4) — 38 grants
    // Same as AlhutLead but Text-specific: AssignTextShifts, ManageTextBlueprints/Programs
    // ============================================
    // Employee base
    grants.Add(G(4, 1, SAR));    // ViewShifts
    grants.Add(G(4, 16, SAR));   // ViewChores
    grants.Add(G(4, 12, SAR));   // ViewDuties
    grants.Add(G(4, 20, SAR));   // ViewVacations
    grants.Add(G(4, 21, SAR));   // RequestVacation
    grants.Add(G(4, 25, SAR));   // RequestSwap
    grants.Add(G(4, 115, SAR));  // ViewCompanyCalendar
    grants.Add(G(4, 117, SAR));  // ViewCompanyUsers
    grants.Add(G(4, 35, SAR));   // ViewGrants
    grants.Add(G(4, 39, SAR));   // ViewHierarchy
    grants.Add(G(4, 110, SAR));  // WriteOverviewNotes
    grants.Add(G(4, 61, SAR));   // ViewAlhutShiftCalendar
    grants.Add(G(4, 62, SAR));   // ViewTextShiftCalendar
    grants.Add(G(4, 63, SAR));   // ViewBRShiftCalendar
    grants.Add(G(4, 64, SAR));   // ViewHakamShiftCalendar
    grants.Add(G(4, 65, SAR, useOwnJobType: true));
    grants.Add(G(4, 66, SAR, useOwnJobType: true));
    grants.Add(G(4, 67, SAR, useOwnJobType: true));
    grants.Add(G(4, 68, SAR, useOwnJobType: true));
    // Lead-specific grants (Text variant)
    grants.Add(G(4, 4, ETM, useOwnJobType: true));    // AssignTextShifts (ETM, OWN)
    grants.Add(G(4, 2, SAR));    // ViewAllShifts
    grants.Add(G(4, 7, SAR, useOwnJobType: true));    // EditShiftPrograms (OWN)
    grants.Add(G(4, 8, SAR, useOwnJobType: true));    // CreateShiftPrograms (OWN)
    grants.Add(G(4, 9, SAR, useOwnJobType: true));    // DeleteShiftPrograms (OWN)
    grants.Add(G(4, 10, SAR, useOwnJobType: true));   // EditShiftTypes (OWN)
    grants.Add(G(4, 11, SAR, useOwnJobType: true));   // CreateShiftTypes (OWN)
    grants.Add(G(4, 71, SAR, useOwnJobType: true));   // ManageTextBlueprints (OWN)
    grants.Add(G(4, 72, SAR, useOwnJobType: true));   // ManageTextPrograms (OWN)
    grants.Add(G(4, 109, SAR, useOwnJobType: true));  // ManageShiftCapacity (OWN)
    grants.Add(G(4, 17, SAR));   // AssignChores
    grants.Add(G(4, 22, SAR, useOwnJobType: true));   // ApproveVacations (OWN)
    grants.Add(G(4, 26, SAR, useOwnJobType: true));   // ApproveSwaps (OWN)
    grants.Add(G(4, 28, SAR));   // ViewUsers
    grants.Add(G(4, 36, SAR, useOwnJobType: true));   // AssignGrants (OWN)
    grants.Add(G(4, 37, SAR, useOwnJobType: true));   // RevokeGrants (OWN)
    grants.Add(G(4, 112, SAR));  // AccessAdminNavigation
    grants.Add(G(4, 114, SAR));  // ManagerHomeAccess
    grants.Add(G(4, 120, SAR));  // ViewSystemAlerts

    // ============================================
    // BR DIRECTOR (Template 2) — 42 grants
    // Employee base (SAR) + BR management. Dual ApproveVacations (BR + Hakam).
    // AssignBRShifts + ViewAllShifts at ETM.
    // ============================================
    // Employee base
    grants.Add(G(2, 1, SAR));    // ViewShifts
    grants.Add(G(2, 16, SAR));   // ViewChores
    grants.Add(G(2, 12, SAR));   // ViewDuties
    grants.Add(G(2, 20, SAR));   // ViewVacations
    grants.Add(G(2, 21, SAR));   // RequestVacation
    grants.Add(G(2, 25, SAR));   // RequestSwap
    grants.Add(G(2, 115, SAR));  // ViewCompanyCalendar
    grants.Add(G(2, 117, SAR));  // ViewCompanyUsers
    grants.Add(G(2, 35, SAR));   // ViewGrants
    grants.Add(G(2, 39, SAR));   // ViewHierarchy
    grants.Add(G(2, 110, SAR));  // WriteOverviewNotes
    grants.Add(G(2, 61, SAR));   // ViewAlhutShiftCalendar
    grants.Add(G(2, 62, SAR));   // ViewTextShiftCalendar
    grants.Add(G(2, 63, SAR));   // ViewBRShiftCalendar
    grants.Add(G(2, 64, SAR));   // ViewHakamShiftCalendar
    grants.Add(G(2, 65, SAR, useOwnJobType: true));
    grants.Add(G(2, 66, SAR, useOwnJobType: true));
    grants.Add(G(2, 67, SAR, useOwnJobType: true));
    grants.Add(G(2, 68, SAR, useOwnJobType: true));
    // BRDirector-specific
    grants.Add(G(2, 5, ETM, canGive: true));   // AssignBRShifts (ETM, ALL, CanGive)
    grants.Add(G(2, 2, ETM));                   // ViewAllShifts (ETM)
    grants.Add(G(2, 7, SAR, useOwnJobType: true));    // EditShiftPrograms (OWN)
    grants.Add(G(2, 8, SAR, useOwnJobType: true));    // CreateShiftPrograms (OWN)
    grants.Add(G(2, 9, SAR, useOwnJobType: true));    // DeleteShiftPrograms (OWN)
    grants.Add(G(2, 10, SAR, useOwnJobType: true));   // EditShiftTypes (OWN)
    grants.Add(G(2, 11, SAR, useOwnJobType: true));   // CreateShiftTypes (OWN)
    grants.Add(G(2, 73, SAR));   // ManageBRBlueprints (ALL)
    grants.Add(G(2, 74, SAR));   // ManageBRPrograms (ALL)
    grants.Add(G(2, 75, SAR));   // ManageHakamBlueprints (ALL — BR manages Hakam)
    grants.Add(G(2, 76, SAR));   // ManageHakamPrograms (ALL)
    grants.Add(G(2, 109, SAR, useOwnJobType: true));  // ManageShiftCapacity (OWN)
    grants.Add(G(2, 17, SAR));   // AssignChores
    // DUAL ApproveVacations — TargetJobTypeId differs, dedup key includes it
    // NOTE: BR and Hakam JobTypeIds must be known at seed time. Since TargetJobTypeId references
    // the JobType table which is per-deployment, we use UseOwnJobType=false and TargetJobTypeId=null
    // here, but we leave these as separate rows to be filled with actual IDs during deployment config.
    // For now, one grant with ALL jobtypes covers both until per-deployment JobType IDs are configured.
    grants.Add(G(2, 22, SAR));   // ApproveVacations (ALL — covers BR + Hakam until per-deploy config)
    grants.Add(G(2, 23, SAR));   // OverrideVacationLimits
    grants.Add(G(2, 26, SAR));   // ApproveSwaps (ALL)
    grants.Add(G(2, 27, SAR));   // InitiateSwap
    grants.Add(G(2, 28, SAR));   // ViewUsers
    grants.Add(G(2, 29, SAR));   // EditUsers
    grants.Add(G(2, 116, SAR));  // ManageJoinRequests
    grants.Add(G(2, 118, SAR));  // EditCompanyUsers
    grants.Add(G(2, 112, SAR));  // AccessAdminNavigation
    grants.Add(G(2, 114, SAR));  // ManagerHomeAccess
    grants.Add(G(2, 120, SAR));  // ViewSystemAlerts

    // ============================================
    // ALHUT DIRECTOR (Template 5) — 45 grants
    // All AlhutLead grants widened to ETM + director extras. Self-scoped stay SAR.
    // ============================================
    // Self-scoped grants (stay SAR)
    grants.Add(G(5, 21, SAR));   // RequestVacation
    grants.Add(G(5, 25, SAR));   // RequestSwap
    grants.Add(G(5, 65, SAR, useOwnJobType: true));   // CanBeAssignedAlhutShifts
    grants.Add(G(5, 66, SAR, useOwnJobType: true));   // CanBeAssignedTextShifts
    grants.Add(G(5, 67, SAR, useOwnJobType: true));   // CanBeAssignedBRShifts
    grants.Add(G(5, 68, SAR, useOwnJobType: true));   // CanBeAssignedHakamShifts
    // AlhutLead grants widened to ETM
    grants.Add(G(5, 1, ETM));    // ViewShifts
    grants.Add(G(5, 16, ETM));   // ViewChores
    grants.Add(G(5, 12, ETM));   // ViewDuties
    grants.Add(G(5, 20, ETM));   // ViewVacations
    grants.Add(G(5, 115, ETM));  // ViewCompanyCalendar
    grants.Add(G(5, 117, ETM));  // ViewCompanyUsers
    grants.Add(G(5, 35, ETM));   // ViewGrants
    grants.Add(G(5, 39, ETM));   // ViewHierarchy
    grants.Add(G(5, 110, ETM));  // WriteOverviewNotes
    grants.Add(G(5, 61, ETM));   // ViewAlhutShiftCalendar
    grants.Add(G(5, 62, ETM));   // ViewTextShiftCalendar
    grants.Add(G(5, 63, ETM));   // ViewBRShiftCalendar
    grants.Add(G(5, 64, ETM));   // ViewHakamShiftCalendar
    grants.Add(G(5, 3, ETM, canGive: true, useOwnJobType: true));  // AssignAlhutShifts (ETM, OWN, CanGive)
    grants.Add(G(5, 2, ETM));    // ViewAllShifts
    grants.Add(G(5, 7, ETM, useOwnJobType: true));    // EditShiftPrograms
    grants.Add(G(5, 8, ETM, useOwnJobType: true));    // CreateShiftPrograms
    grants.Add(G(5, 9, ETM, useOwnJobType: true));    // DeleteShiftPrograms
    grants.Add(G(5, 10, ETM, useOwnJobType: true));   // EditShiftTypes
    grants.Add(G(5, 11, ETM, useOwnJobType: true));   // CreateShiftTypes
    grants.Add(G(5, 69, ETM, useOwnJobType: true));   // ManageAlhutBlueprints
    grants.Add(G(5, 70, ETM, useOwnJobType: true));   // ManageAlhutPrograms
    grants.Add(G(5, 109, ETM, useOwnJobType: true));  // ManageShiftCapacity
    grants.Add(G(5, 17, ETM));   // AssignChores
    grants.Add(G(5, 22, ETM, useOwnJobType: true));   // ApproveVacations (OWN)
    grants.Add(G(5, 26, ETM, useOwnJobType: true));   // ApproveSwaps (OWN)
    grants.Add(G(5, 28, ETM));   // ViewUsers
    grants.Add(G(5, 36, ETM, useOwnJobType: true));   // AssignGrants
    grants.Add(G(5, 37, ETM, useOwnJobType: true));   // RevokeGrants
    grants.Add(G(5, 112, ETM));  // AccessAdminNavigation
    grants.Add(G(5, 114, ETM));  // ManagerHomeAccess
    grants.Add(G(5, 120, ETM));  // ViewSystemAlerts
    // Director extras
    grants.Add(G(5, 34, ETM));   // ViewAllUsers
    grants.Add(G(5, 38, ETM));   // AssignRoles
    grants.Add(G(5, 113, ETM));  // DirectorHubAccess
    grants.Add(G(5, 116, ETM));  // ManageJoinRequests
    grants.Add(G(5, 118, ETM));  // EditCompanyUsers
    grants.Add(G(5, 119, ETM));  // ManageAnnouncements
    grants.Add(G(5, 121, ETM));  // ViewAllAreas

    // ============================================
    // TEXT DIRECTOR (Template 6) — 45 grants
    // Same as AlhutDirector but Text-specific
    // ============================================
    // Self-scoped
    grants.Add(G(6, 21, SAR));
    grants.Add(G(6, 25, SAR));
    grants.Add(G(6, 65, SAR, useOwnJobType: true));
    grants.Add(G(6, 66, SAR, useOwnJobType: true));
    grants.Add(G(6, 67, SAR, useOwnJobType: true));
    grants.Add(G(6, 68, SAR, useOwnJobType: true));
    // Widened to ETM
    grants.Add(G(6, 1, ETM));
    grants.Add(G(6, 16, ETM));
    grants.Add(G(6, 12, ETM));
    grants.Add(G(6, 20, ETM));
    grants.Add(G(6, 115, ETM));
    grants.Add(G(6, 117, ETM));
    grants.Add(G(6, 35, ETM));
    grants.Add(G(6, 39, ETM));
    grants.Add(G(6, 110, ETM));
    grants.Add(G(6, 61, ETM));
    grants.Add(G(6, 62, ETM));
    grants.Add(G(6, 63, ETM));
    grants.Add(G(6, 64, ETM));
    grants.Add(G(6, 4, ETM, canGive: true, useOwnJobType: true));  // AssignTextShifts (ETM, OWN, CanGive)
    grants.Add(G(6, 2, ETM));
    grants.Add(G(6, 7, ETM, useOwnJobType: true));
    grants.Add(G(6, 8, ETM, useOwnJobType: true));
    grants.Add(G(6, 9, ETM, useOwnJobType: true));
    grants.Add(G(6, 10, ETM, useOwnJobType: true));
    grants.Add(G(6, 11, ETM, useOwnJobType: true));
    grants.Add(G(6, 71, ETM, useOwnJobType: true));   // ManageTextBlueprints
    grants.Add(G(6, 72, ETM, useOwnJobType: true));   // ManageTextPrograms
    grants.Add(G(6, 109, ETM, useOwnJobType: true));
    grants.Add(G(6, 17, ETM));
    grants.Add(G(6, 22, ETM, useOwnJobType: true));
    grants.Add(G(6, 26, ETM, useOwnJobType: true));
    grants.Add(G(6, 28, ETM));
    grants.Add(G(6, 36, ETM, useOwnJobType: true));
    grants.Add(G(6, 37, ETM, useOwnJobType: true));
    grants.Add(G(6, 112, ETM));
    grants.Add(G(6, 114, ETM));
    grants.Add(G(6, 120, ETM));
    // Director extras
    grants.Add(G(6, 34, ETM));
    grants.Add(G(6, 38, ETM));
    grants.Add(G(6, 113, ETM));
    grants.Add(G(6, 116, ETM));
    grants.Add(G(6, 118, ETM));
    grants.Add(G(6, 119, ETM));
    grants.Add(G(6, 121, ETM));

    // ============================================
    // MOLECULE ADMIN (Template 7) — 56 grants
    // All BRDirector grants widened to ETM + admin extras.
    // ApproveVacations widened to ALL (supersedes BR+HAKAM).
    // Added AssignAlhutShifts + AssignTextShifts.
    // ============================================
    // Self-scoped
    grants.Add(G(7, 21, SAR));
    grants.Add(G(7, 25, SAR));
    grants.Add(G(7, 65, SAR, useOwnJobType: true));
    grants.Add(G(7, 66, SAR, useOwnJobType: true));
    grants.Add(G(7, 67, SAR, useOwnJobType: true));
    grants.Add(G(7, 68, SAR, useOwnJobType: true));
    // BRDirector grants widened to ETM
    grants.Add(G(7, 1, ETM));
    grants.Add(G(7, 16, ETM));
    grants.Add(G(7, 12, ETM));
    grants.Add(G(7, 20, ETM));
    grants.Add(G(7, 115, ETM));
    grants.Add(G(7, 117, ETM));
    grants.Add(G(7, 35, ETM));
    grants.Add(G(7, 39, ETM));
    grants.Add(G(7, 110, ETM));
    grants.Add(G(7, 61, ETM));
    grants.Add(G(7, 62, ETM));
    grants.Add(G(7, 63, ETM));
    grants.Add(G(7, 64, ETM));
    grants.Add(G(7, 5, ETM, canGive: true));   // AssignBRShifts
    grants.Add(G(7, 2, ETM));                   // ViewAllShifts
    grants.Add(G(7, 7, ETM));                   // EditShiftPrograms (ALL — MolAdmin manages all)
    grants.Add(G(7, 8, ETM));                   // CreateShiftPrograms
    grants.Add(G(7, 9, ETM));                   // DeleteShiftPrograms
    grants.Add(G(7, 10, ETM));                  // EditShiftTypes
    grants.Add(G(7, 11, ETM));                  // CreateShiftTypes
    grants.Add(G(7, 73, ETM));                  // ManageBRBlueprints
    grants.Add(G(7, 74, ETM));                  // ManageBRPrograms
    grants.Add(G(7, 75, ETM));                  // ManageHakamBlueprints
    grants.Add(G(7, 76, ETM));                  // ManageHakamPrograms
    grants.Add(G(7, 109, ETM));                 // ManageShiftCapacity (ALL)
    grants.Add(G(7, 17, ETM));                  // AssignChores
    grants.Add(G(7, 22, ETM));                  // ApproveVacations — ALL (supersedes BR+HAKAM)
    grants.Add(G(7, 23, ETM));                  // OverrideVacationLimits
    grants.Add(G(7, 26, ETM));                  // ApproveSwaps (ALL)
    grants.Add(G(7, 27, ETM));                  // InitiateSwap
    grants.Add(G(7, 28, ETM));                  // ViewUsers
    grants.Add(G(7, 29, ETM));                  // EditUsers
    grants.Add(G(7, 116, ETM));                 // ManageJoinRequests
    grants.Add(G(7, 118, ETM));                 // EditCompanyUsers
    grants.Add(G(7, 112, ETM));                 // AccessAdminNavigation
    grants.Add(G(7, 114, ETM));                 // ManagerHomeAccess
    grants.Add(G(7, 120, ETM));                 // ViewSystemAlerts
    // MoleculeAdmin extras
    grants.Add(G(7, 3, ETM, canGive: true));    // AssignAlhutShifts (ALL, CanGive)
    grants.Add(G(7, 4, ETM, canGive: true));    // AssignTextShifts (ALL, CanGive)
    grants.Add(G(7, 18, ETM));                  // EditChoreTypes
    grants.Add(G(7, 19, ETM));                  // CreateChoreTypes
    grants.Add(G(7, 30, ETM));                  // CreateUsers
    grants.Add(G(7, 31, ETM));                  // DeactivateUsers
    grants.Add(G(7, 34, ETM));                  // ViewAllUsers
    grants.Add(G(7, 36, ETM, canGive: true));   // AssignGrants (CanGive)
    grants.Add(G(7, 37, ETM));                  // RevokeGrants
    grants.Add(G(7, 38, ETM, canGive: true));   // AssignRoles (CanGive)
    grants.Add(G(7, 41, ETM));                  // EditMolecule
    grants.Add(G(7, 45, ETM));                  // ManageShiftGroupings
    grants.Add(G(7, 48, ETM));                  // ViewSettings
    grants.Add(G(7, 49, ETM));                  // EditCompanySettings
    grants.Add(G(7, 50, ETM));                  // EditMoleculeSettings
    grants.Add(G(7, 52, ETM));                  // ViewAnalytics
    grants.Add(G(7, 53, ETM));                  // ViewReports
    grants.Add(G(7, 59, ETM));                  // ViewAuditLog
    grants.Add(G(7, 119, ETM));                 // ManageAnnouncements
    grants.Add(G(7, 69, ETM));                  // ManageAlhutBlueprints (ALL)
    grants.Add(G(7, 70, ETM));                  // ManageAlhutPrograms (ALL)
    grants.Add(G(7, 71, ETM));                  // ManageTextBlueprints (ALL)
    grants.Add(G(7, 72, ETM));                  // ManageTextPrograms (ALL)

    // ============================================
    // DEPARTMENT LEAD (Template 9) — 25 grants
    // Employee base (SAR) + department management grants
    // ============================================
    // Employee base
    grants.Add(G(9, 1, SAR));
    grants.Add(G(9, 16, SAR));
    grants.Add(G(9, 12, SAR));
    grants.Add(G(9, 20, SAR));
    grants.Add(G(9, 21, SAR));   // RequestVacation (FIXED: was wrong ID)
    grants.Add(G(9, 25, SAR));   // RequestSwap
    grants.Add(G(9, 115, SAR));
    grants.Add(G(9, 117, SAR));
    grants.Add(G(9, 35, SAR));
    grants.Add(G(9, 39, SAR));
    grants.Add(G(9, 110, SAR));
    grants.Add(G(9, 61, SAR));
    grants.Add(G(9, 62, SAR));
    grants.Add(G(9, 63, SAR));
    grants.Add(G(9, 64, SAR));
    grants.Add(G(9, 65, SAR, useOwnJobType: true));
    grants.Add(G(9, 66, SAR, useOwnJobType: true));
    grants.Add(G(9, 67, SAR, useOwnJobType: true));
    grants.Add(G(9, 68, SAR, useOwnJobType: true));
    // Department management extras
    grants.Add(G(9, 28, SAR));   // ViewUsers
    grants.Add(G(9, 29, SAR));   // EditUsers
    grants.Add(G(9, 33, SAR));   // AssignJobTypes
    grants.Add(G(9, 47, SAR));   // ManageDepartments
    grants.Add(G(9, 52, SAR));   // ViewAnalytics
    grants.Add(G(9, 53, SAR));   // ViewReports

    // ============================================
    // AREA ADMIN (Template 10) — 70+ grants
    // Merges Directors + MoleculeAdmin at ETA. ALL jobtype wins in merges.
    // Self-scoped stay SAR.
    // ============================================
    // Self-scoped
    grants.Add(G(10, 21, SAR));
    grants.Add(G(10, 25, SAR));
    grants.Add(G(10, 65, SAR, useOwnJobType: true));
    grants.Add(G(10, 66, SAR, useOwnJobType: true));
    grants.Add(G(10, 67, SAR, useOwnJobType: true));
    grants.Add(G(10, 68, SAR, useOwnJobType: true));
    // All operational grants at ETA (ALL jobtype)
    grants.Add(G(10, 1, ETA));    // ViewShifts
    grants.Add(G(10, 16, ETA));   // ViewChores
    grants.Add(G(10, 12, ETA));   // ViewDuties
    grants.Add(G(10, 20, ETA));   // ViewVacations
    grants.Add(G(10, 115, ETA));  // ViewCompanyCalendar
    grants.Add(G(10, 117, ETA));  // ViewCompanyUsers
    grants.Add(G(10, 35, ETA));   // ViewGrants
    grants.Add(G(10, 39, ETA));   // ViewHierarchy
    grants.Add(G(10, 110, ETA));  // WriteOverviewNotes
    grants.Add(G(10, 61, ETA));   // ViewAlhutShiftCalendar
    grants.Add(G(10, 62, ETA));   // ViewTextShiftCalendar
    grants.Add(G(10, 63, ETA));   // ViewBRShiftCalendar
    grants.Add(G(10, 64, ETA));   // ViewHakamShiftCalendar
    grants.Add(G(10, 3, ETA, canGive: true));   // AssignAlhutShifts (ALL, CanGive)
    grants.Add(G(10, 4, ETA, canGive: true));   // AssignTextShifts (ALL, CanGive)
    grants.Add(G(10, 5, ETA, canGive: true));   // AssignBRShifts (ALL, CanGive)
    grants.Add(G(10, 2, ETA));   // ViewAllShifts
    grants.Add(G(10, 7, ETA));   // EditShiftPrograms (ALL)
    grants.Add(G(10, 8, ETA));   // CreateShiftPrograms
    grants.Add(G(10, 9, ETA));   // DeleteShiftPrograms
    grants.Add(G(10, 10, ETA));  // EditShiftTypes
    grants.Add(G(10, 11, ETA));  // CreateShiftTypes
    grants.Add(G(10, 69, ETA));  // ManageAlhutBlueprints
    grants.Add(G(10, 70, ETA));  // ManageAlhutPrograms
    grants.Add(G(10, 71, ETA));  // ManageTextBlueprints
    grants.Add(G(10, 72, ETA));  // ManageTextPrograms
    grants.Add(G(10, 73, ETA));  // ManageBRBlueprints
    grants.Add(G(10, 74, ETA));  // ManageBRPrograms
    grants.Add(G(10, 75, ETA));  // ManageHakamBlueprints
    grants.Add(G(10, 76, ETA));  // ManageHakamPrograms
    grants.Add(G(10, 109, ETA)); // ManageShiftCapacity
    grants.Add(G(10, 17, ETA));  // AssignChores
    grants.Add(G(10, 18, ETA));  // EditChoreTypes
    grants.Add(G(10, 19, ETA));  // CreateChoreTypes
    grants.Add(G(10, 22, ETA));  // ApproveVacations (ALL)
    grants.Add(G(10, 23, ETA));  // OverrideVacationLimits
    grants.Add(G(10, 26, ETA));  // ApproveSwaps (ALL)
    grants.Add(G(10, 27, ETA));  // InitiateSwap
    grants.Add(G(10, 28, ETA));  // ViewUsers
    grants.Add(G(10, 29, ETA));  // EditUsers
    grants.Add(G(10, 30, ETA));  // CreateUsers
    grants.Add(G(10, 31, ETA));  // DeactivateUsers
    grants.Add(G(10, 34, ETA));  // ViewAllUsers
    grants.Add(G(10, 36, ETA, canGive: true));   // AssignGrants (CanGive)
    grants.Add(G(10, 37, ETA));  // RevokeGrants
    grants.Add(G(10, 38, ETA, canGive: true));   // AssignRoles (CanGive)
    grants.Add(G(10, 41, ETA));  // EditMolecule
    grants.Add(G(10, 45, ETA));  // ManageShiftGroupings
    grants.Add(G(10, 48, ETA));  // ViewSettings
    grants.Add(G(10, 49, ETA));  // EditCompanySettings
    grants.Add(G(10, 50, ETA));  // EditMoleculeSettings
    grants.Add(G(10, 52, ETA));  // ViewAnalytics
    grants.Add(G(10, 53, ETA));  // ViewReports
    grants.Add(G(10, 59, ETA));  // ViewAuditLog
    grants.Add(G(10, 116, ETA)); // ManageJoinRequests
    grants.Add(G(10, 118, ETA)); // EditCompanyUsers
    grants.Add(G(10, 112, ETA)); // AccessAdminNavigation
    grants.Add(G(10, 113, ETA)); // DirectorHubAccess
    grants.Add(G(10, 114, ETA)); // ManagerHomeAccess
    grants.Add(G(10, 119, ETA)); // ManageAnnouncements
    grants.Add(G(10, 120, ETA)); // ViewSystemAlerts
    grants.Add(G(10, 121, ETA)); // ViewAllAreas
    // AreaAdmin extras
    grants.Add(G(10, 13, ETA, canGive: true));   // AssignHakamDuties (CanGive)
    grants.Add(G(10, 14, ETA, canGive: true));   // AssignKatzinDuties (CanGive)
    grants.Add(G(10, 15, ETA));                  // EditDutyPrograms
    grants.Add(G(10, 42, ETA));                  // EditArea
    grants.Add(G(10, 43, ETA));                  // CreateCompany
    grants.Add(G(10, 44, ETA));                  // CreateMolecule
    grants.Add(G(10, 46, ETA));                  // ManageJobTypes
    grants.Add(G(10, 51, ETA));                  // EditAreaSettings
    grants.Add(G(10, 122, ETA));                 // ManageOnDuty
    grants.Add(G(10, 111, ETA));                 // ManageOnDutyTypes
    grants.Add(G(10, 107, ETA));                 // ManageKatzinBlueprints
    grants.Add(G(10, 108, ETA));                 // ManageKatzinPrograms

    // ============================================
    // OWNER (Template 11) — All grants at ETP
    // All AreaAdmin grants widened to ETP + system grants. Self-scoped stay SAR.
    // ============================================
    // Self-scoped
    grants.Add(G(11, 21, SAR));
    grants.Add(G(11, 25, SAR));
    grants.Add(G(11, 65, SAR, useOwnJobType: true));
    grants.Add(G(11, 66, SAR, useOwnJobType: true));
    grants.Add(G(11, 67, SAR, useOwnJobType: true));
    grants.Add(G(11, 68, SAR, useOwnJobType: true));
    // All AreaAdmin operational grants at ETP
    grants.Add(G(11, 1, ETP));
    grants.Add(G(11, 16, ETP));
    grants.Add(G(11, 12, ETP));
    grants.Add(G(11, 20, ETP));
    grants.Add(G(11, 115, ETP));
    grants.Add(G(11, 117, ETP));
    grants.Add(G(11, 35, ETP));
    grants.Add(G(11, 39, ETP));
    grants.Add(G(11, 110, ETP));
    grants.Add(G(11, 61, ETP));
    grants.Add(G(11, 62, ETP));
    grants.Add(G(11, 63, ETP));
    grants.Add(G(11, 64, ETP));
    grants.Add(G(11, 3, ETP, canGive: true));
    grants.Add(G(11, 4, ETP, canGive: true));
    grants.Add(G(11, 5, ETP, canGive: true));
    grants.Add(G(11, 2, ETP));
    grants.Add(G(11, 7, ETP));
    grants.Add(G(11, 8, ETP));
    grants.Add(G(11, 9, ETP));
    grants.Add(G(11, 10, ETP));
    grants.Add(G(11, 11, ETP));
    grants.Add(G(11, 69, ETP));
    grants.Add(G(11, 70, ETP));
    grants.Add(G(11, 71, ETP));
    grants.Add(G(11, 72, ETP));
    grants.Add(G(11, 73, ETP));
    grants.Add(G(11, 74, ETP));
    grants.Add(G(11, 75, ETP));
    grants.Add(G(11, 76, ETP));
    grants.Add(G(11, 109, ETP));
    grants.Add(G(11, 17, ETP));
    grants.Add(G(11, 18, ETP));
    grants.Add(G(11, 19, ETP));
    grants.Add(G(11, 22, ETP));
    grants.Add(G(11, 23, ETP));
    grants.Add(G(11, 26, ETP));
    grants.Add(G(11, 27, ETP));
    grants.Add(G(11, 28, ETP));
    grants.Add(G(11, 29, ETP));
    grants.Add(G(11, 30, ETP));
    grants.Add(G(11, 31, ETP));
    grants.Add(G(11, 34, ETP));
    grants.Add(G(11, 36, ETP, canGive: true));
    grants.Add(G(11, 37, ETP));
    grants.Add(G(11, 38, ETP, canGive: true));
    grants.Add(G(11, 41, ETP));
    grants.Add(G(11, 45, ETP));
    grants.Add(G(11, 48, ETP));
    grants.Add(G(11, 49, ETP));
    grants.Add(G(11, 50, ETP));
    grants.Add(G(11, 52, ETP));
    grants.Add(G(11, 53, ETP));
    grants.Add(G(11, 59, ETP));
    grants.Add(G(11, 116, ETP));
    grants.Add(G(11, 118, ETP));
    grants.Add(G(11, 112, ETP));
    grants.Add(G(11, 113, ETP));
    grants.Add(G(11, 114, ETP));
    grants.Add(G(11, 119, ETP));
    grants.Add(G(11, 120, ETP));
    grants.Add(G(11, 121, ETP));
    // AreaAdmin extras at ETP
    grants.Add(G(11, 13, ETP, canGive: true));
    grants.Add(G(11, 14, ETP, canGive: true));
    grants.Add(G(11, 15, ETP));
    grants.Add(G(11, 42, ETP));
    grants.Add(G(11, 43, ETP));
    grants.Add(G(11, 44, ETP));
    grants.Add(G(11, 46, ETP));
    grants.Add(G(11, 51, ETP));
    grants.Add(G(11, 122, ETP));
    grants.Add(G(11, 111, ETP));
    grants.Add(G(11, 107, ETP));
    grants.Add(G(11, 108, ETP));
    // Owner system-level extras
    grants.Add(G(11, 57, ETP, canGive: true));   // AdminAccess
    grants.Add(G(11, 58, ETP));                  // SystemConfiguration
    grants.Add(G(11, 60, ETP));                  // ManageApiKeys
    grants.Add(G(11, 54, ETP));                  // ExportData
    grants.Add(G(11, 55, ETP));                  // SendNotifications
    grants.Add(G(11, 56, ETP));                  // ConfigureEmailSettings
    grants.Add(G(11, 123, ETP));                 // ReorderHierarchy

    return grants;
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: 0 errors, 0 warnings from this file

**Step 3: Commit**

```bash
git add Data/SeedData/RoleTemplateSeed.cs
git commit -m "feat: complete grant matrix rewrite — 12 roles, correct IDs, JobType scoping"
```

---

### Task 10: Fresh Database Reseed

**Context:** Since the old seed data has wrong grant IDs and missing fields, a fresh reseed is needed. On a dev machine, the easiest approach is to delete the DB and restart.

**Step 1: Stop the application** (if running)

**Step 2: Delete the SQLite database**

```bash
rm -f ShiftManager.db
```

**Step 3: Apply all migrations and reseed**

```bash
dotnet ef database update
dotnet run
```

Then stop the app after seeding completes (watch logs for "Seeded X new grant mappings").

**Step 4: Verify grant counts**

After reseed, check the database for expected grant counts per role. You can use the Diagnostic page or a quick SQL query:

```sql
SELECT rt.Key, COUNT(rtg.Id) as GrantCount
FROM RoleTemplateGrants rtg
JOIN RoleTemplates rt ON rt.Id = rtg.RoleTemplateId
GROUP BY rt.Key
ORDER BY rt.Id;
```

Expected approximate counts:
- Employee: 19
- BRDirector: ~42
- AlhutLead: 38
- TextLead: 38
- AlhutDirector: 45
- TextDirector: 45
- MoleculeAdmin: ~56
- Assigner: 20
- DepartmentLead: 25
- AreaAdmin: ~70
- Owner: ~85
- Trainee: 18

**Step 5: Commit** (nothing to commit — DB file is gitignored)

---

### Task 11: Build and Smoke Test

**Step 1: Full build verification**

Run: `dotnet build`
Expected: 0 errors, 0 warnings

**Step 2: Run existing tests**

Run: `dotnet test`
Expected: All 236 tests pass

**Step 3: Manual smoke test**

1. Start the app: `dotnet run`
2. Login as Owner — verify dashboard loads
3. Login as Employee — verify Employee dashboard loads
4. Check Diagnostic page → verify grant counts per role

**Step 4: Final commit if any fixups needed**

```bash
git add -A
git commit -m "chore: Phase 1 complete — role-grant matrix schema + service + seed"
```

---

## Post-Implementation: Opus 4.6 Critical Review

After all tasks are complete, dispatch an Opus 4.6 review agent to:

1. Verify `DetermineEffectiveScope` SAR correctly strips hierarchy (scope cascade safety)
2. Verify `ApplyAutoGrantsAsync` resolves JobTypeId BEFORE scope determination
3. Verify seeding dedup key includes TargetJobTypeId
4. Verify grant counts match design document per role
5. Verify no regression in existing grant check paths (`HasGrantAsync`, `HasGrantWithScopeAsync`)
6. Check for off-by-one ID errors in the new seed data
7. Verify `BuildGrantScopeForUserAsync` returns full hierarchy for all roles
8. Verify both Login.cshtml.cs and GriffinService.cs include DepartmentId + JobTypeId
