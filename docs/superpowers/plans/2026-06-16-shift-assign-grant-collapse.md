# Shift-Assignment Grant Collapse (8 → 1 `AssignShifts`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 8 per-job-type/per-tech-family `Assign*Shifts` grants with one job-type-agnostic `AssignShifts` grant, fixing the tech-assign lockout (review #3) and the caller-company scope bug (#5), with an idempotent migration for existing air-gapped DBs.

**Architecture:** Append `AssignShifts` (id 137) to the grant seed; repoint all 9 code consumers + 7 role-template assignments at it; deprecate (not delete) the 8 old grant types via a new `GrantType.IsDeprecated` flag; self-heal existing DBs at startup (mirror the F2 block) by removing stale template mappings and migrating user grant rows BEFORE the per-user repair passes. Permission becomes job-type-agnostic (scope still enforced); job-type relevance moves entirely to candidate filtering (sub-project 3b, separate).

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core + SQLite, xUnit + FluentAssertions, grant-based authorization (`IGrantService`).

**Spec:** `docs/superpowers/specs/2026-06-14-shift-assign-grant-collapse-design.md`

---

## Ground-truth facts (verified 2026-06-16 at HEAD `56db000`)

- Max grant id in `GrantTypeSeed.cs` = **136** (`ManageShiftCategories`). New `AssignShifts` = **137**.
- `GrantType` model (`Models/GrantType.cs`) has `Key/NameKey/DescriptionKey/Category/DefaultScope/IsSystem/IsActive` — **no `IsDeprecated`** (Task 4 adds it).
- The 8 grant ids: `AssignAlhutShifts`=3, `AssignTextShifts`=4, `AssignBRShifts`=5, `AssignTechShifts`=6, `AssignHanavaShifts`=81, `AssignDeltaShifts`=82, `AssignYekevShifts`=83, `AssignMoviltechShifts`=84.
- **9 code consumers** (exact, verified):
  1. `Pages/Calendar/Table.cshtml.cs:804-807` — `OnPostAssignEmployeeAsync` OR-chain (+ `probeShiftJobTypeId` lookup ~798-802 feeding only this).
  2. `Pages/Calendar/Table.cshtml.cs:2842-2845` — `CanAssignForShiftScopeAsync` OR-chain.
  3. `Pages/Calendar/Shifts.cshtml.cs:239-242` — `CanEdit` OR-chain.
  4. `Pages/Calendar/Shifts.cshtml.cs:253` — `RequiredGrantNameKeys` arg list.
  5. `Services/GrantService.cs:53-56` — `HasCalendarEditPermissionAsync` (also lists `AssignChores/ManageOnDuty/EditOnCallCalendar` — **keep those**).
  6. `Services/GrantService.cs:113-116` — `HasAnyAssignGrantAsync` (also `AssignChores/ManageOnDuty` — **keep**).
  7. `Services/GrantService.cs:94-98` — `assignGrantKeys` array in `CanReachUserForNoteAsync` (also `AssignChores/ManageOnDuty` — **keep**).
  8. `Pages/Calendar/Month.cshtml:217,336,397` — `<require-grant key="AssignAlhutShifts,AssignTextShifts,AssignBRShifts,AssignTechShifts" mode="any">`.
  9. (Seeds — Tasks 2/3.)
- **7 templates** assign the 8 grants (replace each block with one `AssignShifts` at the listed scope, dropping `useOwnJobType`):
  | Template | Lines | Collapsed `AssignShifts` |
  |---|---|---|
  | 2 | 368 | `G(2, 137, ETM, canGive: true)` |
  | 3 | 309,310 | `G(3, 137, ETM)` |
  | 5 | 436,437 | `G(5, 137, ETM, canGive: true)` |
  | 7 | 504,529,530,557,562,563,564,565 | `G(7, 137, ETM, canGive: true)` |
  | 9 | 620,625,626,627,628 | `G(9, 137, SAR)` |
  | 10 | 674,675,676,743,748,749,750,751 | `G(10, 137, ETA, canGive: true)` |
  | 11 | 801,802,803,804,890,891,892,893 | `G(11, 137, ETP, canGive: true)` |
- F2 self-heal block to mirror: `Program.cs:824-861`. Repair passes run AFTER it (~1520, ~1547, ~1627).
- Count test: `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs:1096` asserts `HaveCount(136)`.
- Catalog page: `Pages/Owner/Hub/Grants.cshtml.cs:120,126` queries `_db.GrantTypes` (filter `IsDeprecated` in Task 11).
- **Run all tests sequentially:** `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false` (parallel `:memory:` runs yield ~220 spurious contention failures).
- **Build-lock rule:** if a rebuild fails with `ShiftManager.exe ... used by another process`, stop the running dev app (`Get-Process ShiftManager | Where Path -like '*Downloads\ShiftManager*' | Stop-Process -Force`) before retrying. Never edit `FinalProductPublish/**` (generated; regenerate via `scripts/Update-FinalProductPublish.ps1` in Task 13).

---

## Task 1: Add the `AssignShifts` grant type to the seed

**Files:**
- Modify: `Data/SeedData/GrantTypeSeed.cs` (after the `ManageShiftCategories` line, currently ~351)
- Modify: `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs:1095-1096`

- [ ] **Step 1: Update the count test to expect 137 (RED)**

In `GrantServiceTests.cs`, change the assertion (~line 1096):
```csharp
// Assert - 137 grant types (added AssignShifts on 2026-06-16, collapsing the 8 per-type assign grants)
grantTypes.Should().HaveCount(137, "Should have exactly 137 grant types including the unified AssignShifts grant");
```

- [ ] **Step 2: Run it — verify it fails**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~GrantTypeSeed" -- xUnit.ParallelizeTestCollections=false`
Expected: FAIL — `Expected collection to contain 137 item(s), but found 136`.

- [ ] **Step 3: Append `AssignShifts` to the seed (GREEN)**

In `GrantTypeSeed.cs`, immediately AFTER the `ManageShiftCategories` line (the current last `Id = id++` entry), add:
```csharp
        // Id 137 — AssignShifts: the unified, job-type-agnostic shift-assignment grant that
        // replaces the 8 per-type/per-tech-family Assign*Shifts grants (3,4,5,6,81-84). Permission
        // is gated by org scope only; job-type relevance lives in candidate filtering (sub-project 3b).
        grants.Add(new GrantType { Id = id++, Key = "AssignShifts", NameKey = "Grant_AssignShifts", DescriptionKey = "Grant_AssignShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
```

- [ ] **Step 4: Run — verify it passes**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~GrantTypeSeed" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Data/SeedData/GrantTypeSeed.cs ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs
git commit -m "feat(grants): add unified AssignShifts grant type (id 137)"
```

---

## Task 2: Add resx entries (EN + HE)

**Files:**
- Modify: `Resources/SharedResources.resx`
- Modify: `Resources/SharedResources.he-IL.resx`

- [ ] **Step 1: Confirm the keys are absent first**

Run: `grep -c "Grant_AssignShifts" Resources/SharedResources.resx` → Expected: `0` (do NOT add a duplicate; duplicates break localization tests).

- [ ] **Step 2: Add EN entries**

In `Resources/SharedResources.resx`, add two `<data>` entries (place near the other `Grant_Assign*` keys):
```xml
  <data name="Grant_AssignShifts" xml:space="preserve">
    <value>Assign Shifts</value>
  </data>
  <data name="Grant_AssignShifts_Desc" xml:space="preserve">
    <value>Assign users to shifts within the granted scope.</value>
  </data>
```

- [ ] **Step 3: Add HE entries**

In `Resources/SharedResources.he-IL.resx`:
```xml
  <data name="Grant_AssignShifts" xml:space="preserve">
    <value>שיבוץ למשמרות</value>
  </data>
  <data name="Grant_AssignShifts_Desc" xml:space="preserve">
    <value>שיבוץ עובדים למשמרות בתוך ההיקף שהוענק.</value>
  </data>
```

- [ ] **Step 4: Build to confirm resx compiles**

Run: `dotnet build "ShiftManager.csproj" --nologo`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat(grants): localize AssignShifts grant name+desc (EN/HE)"
```

---

## Task 3: Repoint role templates at `AssignShifts`

**Files:**
- Modify: `Data/SeedData/RoleTemplateSeed.cs` (the 7 blocks in the Ground-truth table)
- Modify: `ShiftManager.Tests/MasterTests/GrantAuthorization/RoleTemplateAutoGrantTests.cs` (InlineData counts)

- [ ] **Step 1: Replace each template's Assign*Shifts lines with one AssignShifts**

For each template, DELETE all its `G(<t>, {3|4|5|6|81|82|83|84}, …)` lines and ADD a single line per the table. Example for template 7 (delete lines 504,529,530,557,562-565, add one):
```csharp
        grants.Add(G(7, 137, ETM, canGive: true));   // AssignShifts (unified; replaces Alhut/Text/BR/Tech/Hanava/Delta/Yekev/Moviltech)
```
Apply the same for templates 2 `G(2,137,ETM,canGive:true)`, 3 `G(3,137,ETM)`, 5 `G(5,137,ETM,canGive:true)`, 9 `G(9,137,SAR)`, 10 `G(10,137,ETA,canGive:true)`, 11 `G(11,137,ETP,canGive:true)`. Place each new line where the template's first old assign line was. Update any per-block "N grants" header comment for those templates (decrement by `K-1`).

- [ ] **Step 2: Run the template-count test — read the actual failures (RED)**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~RoleTemplateAutoGrantTests" -- xUnit.ParallelizeTestCollections=false`
Expected: FAIL for templates that lost grants. Each `[InlineData("…", "<role>", N)]` is off by `(K-1)` (K = that template's old assign-shift count from the table: 2→3 means -1, 5→-4, 8→-7; template 2 K=1 → unchanged).

- [ ] **Step 3: Update each InlineData to the seed reality (GREEN)**

For every failing row, set N to the **actual** value printed in the failure (`Expected … to be <actual>`). The seed is the source of truth; the test asserts it. Add a trailing comment `// -(K-1) AssignShifts collapse` on each changed row.

- [ ] **Step 4: Run — verify it passes**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~RoleTemplateAutoGrantTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Data/SeedData/RoleTemplateSeed.cs ShiftManager.Tests/MasterTests/GrantAuthorization/RoleTemplateAutoGrantTests.cs
git commit -m "feat(grants): role templates grant unified AssignShifts (collapse 8 assign grants)"
```

---

## Task 4: Add `IsDeprecated` to `GrantType` + EF migration

**Files:**
- Modify: `Models/GrantType.cs`
- Create: migration via `dotnet ef migrations add AddGrantTypeIsDeprecated`

- [ ] **Step 1: Add the property**

In `Models/GrantType.cs`, after `public bool IsActive { get; set; } = true;`:
```csharp
    /// <summary>
    /// True for grant types retained for ID-stability/audit history but no longer assignable
    /// (e.g. the 8 per-type Assign*Shifts grants superseded by AssignShifts). Hidden from the
    /// grant-admin catalog; never re-applied to templates or users.
    /// </summary>
    public bool IsDeprecated { get; set; } = false;
```

- [ ] **Step 2: Create the migration**

Run: `dotnet ef migrations add AddGrantTypeIsDeprecated --project ShiftManager.csproj`
Expected: a new `Migrations/<ts>_AddGrantTypeIsDeprecated.cs` adding a non-null `IsDeprecated` bit column defaulting to `0`.

- [ ] **Step 3: Verify the migration body**

Open the generated migration. Confirm `Up` adds the column with `defaultValue: false` and `Down` drops it. No data changes here (deprecation marking happens at seed/startup, Task 5).

- [ ] **Step 4: Build + apply on a throwaway DB to confirm it's valid**

Run: `dotnet build "ShiftManager.csproj" --nologo`
Expected: `Build succeeded` (the model snapshot now includes `IsDeprecated`).

- [ ] **Step 5: Commit**

```bash
git add Models/GrantType.cs Migrations/
git commit -m "feat(grants): add GrantType.IsDeprecated flag + migration"
```

---

## Task 5: Startup self-heal — deprecate the 8, remove stale template mappings, migrate user grants

**Files:**
- Modify: `Program.cs` (insert a block immediately AFTER the F2 self-heal block, ~line 861, BEFORE the reconcile/repair passes)
- Test: `ShiftManager.Tests/UnitTests/Migrations/AssignShiftsCollapseSelfHealTests.cs` (new)

- [ ] **Step 1: Write the self-heal regression test (RED)**

Create `ShiftManager.Tests/UnitTests/Migrations/AssignShiftsCollapseSelfHealTests.cs`. It exercises the extracted heal method (Step 3 extracts the logic into a static `AssignShiftsCollapse.HealAsync(AppDbContext, ILogger)` so it is unit-testable rather than living only inline in `Program.cs`):
```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Migrations;

public class AssignShiftsCollapseSelfHealTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;

    public AssignShiftsCollapseSelfHealTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        // Grant types: the new AssignShifts (137) + two old ones (3 Alhut, 6 Tech).
        _db.GrantTypes.Add(new GrantType { Id = 137, Key = "AssignShifts", NameKey = "x", DescriptionKey = "x", IsSystem = true });
        _db.GrantTypes.Add(new GrantType { Id = 3, Key = "AssignAlhutShifts", NameKey = "x", DescriptionKey = "x", IsSystem = true });
        _db.GrantTypes.Add(new GrantType { Id = 6, Key = "AssignTechShifts", NameKey = "x", DescriptionKey = "x", IsSystem = true });
        _db.RoleTemplates.Add(new RoleTemplate { Id = 7, Key = "MoleculeAdmin", Name = "MoleculeAdmin" });
        // Stale template->old-grant mapping that the seed no longer produces.
        _db.RoleTemplateGrants.Add(new RoleTemplateGrant { RoleTemplateId = 7, GrantTypeId = 6 });
        // A user holding two old grants at the same molecule scope (must collapse to ONE AssignShifts).
        _db.Users.Add(new AppUser { Id = 50, Email = "u@test", CompanyId = 1, RoleTemplateId = 7, DisplayName = "U" });
        _db.Grants.Add(new Grant { UserId = 50, GrantTypeId = 3, MoleculeId = 9, CanOwn = true });
        _db.Grants.Add(new Grant { UserId = 50, GrantTypeId = 6, MoleculeId = 9, CanOwn = true });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Heal_DeprecatesOldGrants_RemovesStaleMappings_MigratesUserGrants_Idempotently()
    {
        await AssignShiftsCollapse.HealAsync(_db, NullLogger.Instance);

        // 8 old grant types marked deprecated (here we seeded 2 of them).
        (await _db.GrantTypes.Where(g => g.Key == "AssignAlhutShifts" || g.Key == "AssignTechShifts")
            .AllAsync(g => g.IsDeprecated)).Should().BeTrue();
        // Stale template mapping for an old grant removed.
        (await _db.RoleTemplateGrants.AnyAsync(m => m.GrantTypeId == 6)).Should().BeFalse();
        // User's two old grant rows removed.
        (await _db.Grants.AnyAsync(g => g.UserId == 50 && (g.GrantTypeId == 3 || g.GrantTypeId == 6))).Should().BeFalse();
        // Exactly ONE AssignShifts row created at the held scope (deduped).
        var migrated = await _db.Grants.Where(g => g.UserId == 50 && g.GrantTypeId == 137).ToListAsync();
        migrated.Should().ContainSingle();
        migrated[0].MoleculeId.Should().Be(9);
        migrated[0].CanOwn.Should().BeTrue();

        // Idempotent: second run is a no-op (still exactly one AssignShifts row).
        await AssignShiftsCollapse.HealAsync(_db, NullLogger.Instance);
        (await _db.Grants.CountAsync(g => g.UserId == 50 && g.GrantTypeId == 137)).Should().Be(1);
    }
}
```

- [ ] **Step 2: Run — verify it fails (RED)**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~AssignShiftsCollapseSelfHeal" -- xUnit.ParallelizeTestCollections=false`
Expected: FAIL — `AssignShiftsCollapse` does not exist.

- [ ] **Step 3: Implement the heal method (GREEN)**

Create `Data/SeedData/AssignShiftsCollapse.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Idempotent startup self-heal for the 8→1 Assign*Shifts collapse. Mirrors the F2 ViewGrants
/// self-heal: runs BEFORE RepairUserGrants so the de-seeded grants are not re-applied. Marks the
/// 8 old grant types deprecated, removes their template mappings, and migrates each holder's old
/// grant rows to a single AssignShifts row per distinct org-scope tuple (job-type pin dropped).
/// </summary>
public static class AssignShiftsCollapse
{
    private static readonly string[] OldKeys =
    {
        "AssignAlhutShifts", "AssignTextShifts", "AssignBRShifts", "AssignTechShifts",
        "AssignHanavaShifts", "AssignDeltaShifts", "AssignYekevShifts", "AssignMoviltechShifts"
    };

    public static async Task HealAsync(AppDbContext db, ILogger logger)
    {
        var newType = await db.GrantTypes.FirstOrDefaultAsync(g => g.Key == "AssignShifts");
        if (newType == null) return; // seed hasn't run yet; nothing to heal

        var oldTypes = await db.GrantTypes.Where(g => OldKeys.Contains(g.Key)).ToListAsync();
        if (oldTypes.Count == 0) return;
        var oldTypeIds = oldTypes.Select(t => t.Id).ToHashSet();

        // 1) Mark deprecated (only flips rows not already deprecated → idempotent).
        var toDeprecate = oldTypes.Where(t => !t.IsDeprecated).ToList();
        foreach (var t in toDeprecate) t.IsDeprecated = true;

        // 2) Remove stale template→old-grant mappings (seed never deletes de-seeded mappings).
        var staleMappings = await db.RoleTemplateGrants
            .Where(m => oldTypeIds.Contains(m.GrantTypeId)).ToListAsync();
        if (staleMappings.Count > 0) db.RoleTemplateGrants.RemoveRange(staleMappings);

        // 3) Migrate user grant rows: one AssignShifts per distinct scope tuple (drop JobTypeId).
        var oldUserGrants = await db.Grants.IgnoreQueryFilters()
            .Where(g => oldTypeIds.Contains(g.GrantTypeId)).ToListAsync();

        int created = 0;
        if (oldUserGrants.Count > 0)
        {
            // Existing AssignShifts rows, keyed by (user, scope tuple), to dedup against.
            var existingNew = await db.Grants.IgnoreQueryFilters()
                .Where(g => g.GrantTypeId == newType.Id).ToListAsync();
            var seen = new HashSet<string>(existingNew.Select(ScopeKey));

            foreach (var g in oldUserGrants)
            {
                var key = ScopeKey(WithType(g, newType.Id));
                if (seen.Add(key))
                {
                    db.Grants.Add(new Grant
                    {
                        UserId = g.UserId,
                        GrantTypeId = newType.Id,
                        ProjectId = g.ProjectId,
                        AreaId = g.AreaId,
                        MoleculeId = g.MoleculeId,
                        DepartmentId = g.DepartmentId,
                        CompanyId = g.CompanyId,
                        JobTypeId = null,           // unified grant is job-type-agnostic
                        CanOwn = g.CanOwn,
                        CanGive = g.CanGive
                    });
                    created++;
                }
            }
            db.Grants.RemoveRange(oldUserGrants);
        }

        if (toDeprecate.Count > 0 || staleMappings.Count > 0 || oldUserGrants.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation(
                "AssignShifts collapse self-heal: deprecated {Dep} grant types, removed {Maps} stale template mappings, migrated {Old} old user grants into {New} AssignShifts rows",
                toDeprecate.Count, staleMappings.Count, oldUserGrants.Count, created);
        }
    }

    // Scope identity = the six hierarchy columns + CanOwn/CanGive (JobTypeId intentionally excluded,
    // because the unified grant drops the pin: two job-type-pinned grants at the same molecule collapse to one).
    private static string ScopeKey(Grant g) =>
        $"{g.UserId}|{g.ProjectId}|{g.AreaId}|{g.MoleculeId}|{g.DepartmentId}|{g.CompanyId}|{g.CanOwn}|{g.CanGive}";

    private static Grant WithType(Grant g, int typeId) => new()
    {
        UserId = g.UserId, ProjectId = g.ProjectId, AreaId = g.AreaId, MoleculeId = g.MoleculeId,
        DepartmentId = g.DepartmentId, CompanyId = g.CompanyId, CanOwn = g.CanOwn, CanGive = g.CanGive
    };
}
```

- [ ] **Step 4: Run — verify it passes (GREEN)**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~AssignShiftsCollapseSelfHeal" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS.

- [ ] **Step 5: Wire it into Program.cs**

In `Program.cs`, immediately AFTER the F2 self-heal block's closing brace (~line 861) and BEFORE the reconcile block (~863), add:
```csharp
        // AssignShifts collapse (review #3): mirror F2 — heal de-seeded Assign*Shifts BEFORE repair runs.
        await ShiftManager.Data.SeedData.AssignShiftsCollapse.HealAsync(db, logger);
```

- [ ] **Step 6: Build + full suite (sequential)**

Run: `dotnet build "ShiftManager.csproj" --nologo` → `Build succeeded`.
Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false` → all pass.

- [ ] **Step 7: Commit**

```bash
git add Data/SeedData/AssignShiftsCollapse.cs Program.cs ShiftManager.Tests/UnitTests/Migrations/AssignShiftsCollapseSelfHealTests.cs
git commit -m "feat(grants): idempotent self-heal migrating 8 assign grants -> AssignShifts"
```

---

## Task 6: Consumer #2 — `CanAssignForShiftScopeAsync` (the workhorse)

**Files:**
- Modify: `Pages/Calendar/Table.cshtml.cs:2842-2845`

- [ ] **Step 1: Replace the 4-key OR-chain with one AssignShifts check**

Replace lines 2842-2845:
```csharp
        return await _grantService.HasGrantWithScopeAsync(userId, "AssignShifts", companyId: companyId, moleculeId: moleculeId, jobTypeId: jobTypeId);
```
NOTE: this method's signature changes in Task 8 (adds `moleculeId`); for now keep its existing params and pass what it has (`companyId`, `jobTypeId`). If the method currently has only `(int userId, int? companyId, int? jobTypeId)`, write:
```csharp
        return await _grantService.HasGrantWithScopeAsync(userId, "AssignShifts", companyId: companyId, jobTypeId: jobTypeId);
```

- [ ] **Step 2: Build**

Run: `dotnet build "ShiftManager.csproj" --nologo`
Expected: `Build succeeded`.

- [ ] **Step 3: Run the Table/assign tests**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~AssignShiftAsync|FullyQualifiedName~Table" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (these assert AdminAccess + scope, unaffected by which key).

- [ ] **Step 4: Commit**

```bash
git add Pages/Calendar/Table.cshtml.cs
git commit -m "refactor(calendar): CanAssignForShiftScopeAsync uses unified AssignShifts grant"
```

---

## Task 7: Consumer #1 — `OnPostAssignEmployeeAsync` (drop the probe lookup)

**Files:**
- Modify: `Pages/Calendar/Table.cshtml.cs:~798-807`

- [ ] **Step 1: Replace the OR-chain; keep the jobType probe only if still needed**

The `probeShiftJobTypeId` lookup (~798-802) existed to feed the 4-key chain. Replace lines 804-807 with:
```csharp
                var hasAnyShiftGrant = await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignShifts", companyId: companyId, jobTypeId: probeShiftJobTypeId);
```
Keep `probeShiftJobTypeId` (it still scopes correctly — a null pin matches broadly; a real one narrows). Do NOT delete the probe here (it is harmless and keeps parity with the scope check); deletion is optional cleanup, out of scope.

- [ ] **Step 2: Build + assign tests**

Run: `dotnet build "ShiftManager.csproj" --nologo` → `Build succeeded`.
Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~AssignShiftAsync" -- xUnit.ParallelizeTestCollections=false` → PASS.

- [ ] **Step 3: Commit**

```bash
git add Pages/Calendar/Table.cshtml.cs
git commit -m "refactor(calendar): OnPostAssignEmployeeAsync uses unified AssignShifts grant"
```

---

## Task 8: Fix #5 — pass the target molecule, not the caller's company

**Files:**
- Modify: `Pages/Calendar/Table.cshtml.cs` — `CanAssignForShiftScopeAsync` signature + the two call sites in `EnsureShiftInstance`/`CreateShiftInstance`
- Test: `ShiftManager.Tests/UnitTests/Services/AssignShiftAsyncTests.cs` or a Table-handler test (see Step 1)

- [ ] **Step 1: Write a failing test for the cross-molecule scope (RED)**

Add a test asserting that a manager whose `AssignShifts` grant is molecule-scoped to molecule A is REJECTED when ensuring an instance for a molecule-scoped ShiftType whose `MoleculeId` is molecule B (different), even though `GetEffectiveCompanyId` would resolve to the caller's own company. Use the existing Table/assign test harness pattern. If no handler-level harness exists, add the assertion at the `CanAssignForShiftScopeAsync` level by making it accept `moleculeId` and verifying a B-molecule target with an A-only grant returns false.

```csharp
[Fact]
public async Task CanAssignForShiftScope_MoleculeScopedShiftType_UsesTargetMolecule_NotCallerCompany()
{
    // Manager holds AssignShifts scoped to molecule A. Target shift type is molecule-scoped to B.
    // Must be rejected (previously GetEffectiveCompanyId resolved to the caller's own company,
    // letting the A-grant authorize a B-molecule shift type).
    // ... arrange grant at moleculeId=A, call CanAssignForShiftScopeAsync(user, companyId:null, moleculeId:B, jobTypeId:null)
    var ok = await model.CanAssignForShiftScopeForTest(userId, companyId: null, moleculeId: bMoleculeId, jobTypeId: null);
    ok.Should().BeFalse();
}
```
(If `CanAssignForShiftScopeAsync` is private, add an `internal` test-visible wrapper `CanAssignForShiftScopeForTest` or use `InternalsVisibleTo` already present in the test project.)

- [ ] **Step 2: Run — verify it fails (RED)**

Run the new test. Expected: FAIL (current method has no `moleculeId` param / authorizes via caller company).

- [ ] **Step 3: Add `moleculeId` to the method and resolve target scope at call sites (GREEN)**

Change `CanAssignForShiftScopeAsync` signature to `(int userId, int? companyId, int? moleculeId, int? jobTypeId)` and body:
```csharp
    private async Task<bool> CanAssignForShiftScopeAsync(int userId, int? companyId, int? moleculeId, int? jobTypeId)
    {
        if (await _grantService.HasGrantAsync(userId, "AdminAccess")) return true;
        return await _grantService.HasGrantWithScopeAsync(userId, "AssignShifts",
            companyId: companyId, moleculeId: moleculeId, jobTypeId: jobTypeId);
    }
```
At the `EnsureShiftInstance`/`CreateShiftInstance` call sites, for a molecule/area-scoped ShiftType (`shiftType.CompanyId == null`) pass `moleculeId: shiftType.MoleculeId, companyId: null`; for a company-scoped type pass `companyId: shiftType.CompanyId.Value, moleculeId: null`. Update all OTHER callers of `CanAssignForShiftScopeAsync` to pass `moleculeId: null` (they already pass the real target company from the instance/assignment, which is correct).

- [ ] **Step 4: Run — verify it passes; run full assign suite**

Run the new test → PASS.
Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~AssignShiftAsync|FullyQualifiedName~Table" -- xUnit.ParallelizeTestCollections=false` → PASS.

- [ ] **Step 5: Commit**

```bash
git add Pages/Calendar/Table.cshtml.cs ShiftManager.Tests/
git commit -m "fix(calendar): authorize Ensure/CreateShiftInstance against target molecule, not caller company (review #5)"
```

---

## Task 9: Consumer #3/#4 — `Shifts.cshtml.cs` CanEdit + RequiredGrantNameKeys

**Files:**
- Modify: `Pages/Calendar/Shifts.cshtml.cs:239-242, 253`

- [ ] **Step 1: Replace the CanEdit OR-chain**

Replace lines 239-242:
```csharp
        CanEdit = await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignShifts", moleculeId: MoleculeId, jobTypeId: JobTypeId);
```

- [ ] **Step 2: Replace the RequiredGrantNameKeys arg**

Replace line 253:
```csharp
                "AssignShifts");
```

- [ ] **Step 3: Build + localization tests**

Run: `dotnet build "ShiftManager.csproj" --nologo` → `Build succeeded`.
Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~Localization|FullyQualifiedName~Shifts" -- xUnit.ParallelizeTestCollections=false` → PASS.

- [ ] **Step 4: Commit**

```bash
git add Pages/Calendar/Shifts.cshtml.cs
git commit -m "refactor(calendar): Shifts page CanEdit/required-grant uses unified AssignShifts"
```

---

## Task 10: Consumers #5/#6/#7 — GrantService (preserve chore/duty keys)

**Files:**
- Modify: `Services/GrantService.cs:53-56, 96, 113-116`

- [ ] **Step 1: `HasCalendarEditPermissionAsync` (lines 53-56)**

Replace the four `Assign*Shifts` `|| await HasGrantAsync(...)` lines with ONE, keeping the surrounding `AdminAccess`/`AssignChores`/`ManageOnDuty`/`EditOnCallCalendar` lines:
```csharp
            || await HasGrantAsync(userId, "AssignShifts")
```

- [ ] **Step 2: `HasAnyAssignGrantAsync` (lines 113-116)**

Same replacement — four lines become one `|| await HasGrantAsync(userId, "AssignShifts")`, keep `AssignChores`/`ManageOnDuty`.

- [ ] **Step 3: `assignGrantKeys` array (line 96)**

Replace the four shift keys with `"AssignShifts"`, keep `"AssignChores", "ManageOnDuty"`:
```csharp
            "AssignShifts", "AssignChores", "ManageOnDuty"
```

- [ ] **Step 4: Build + GrantService + note-permission tests**

Run: `dotnet build "ShiftManager.csproj" --nologo` → `Build succeeded`.
Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~GrantService|FullyQualifiedName~Note|FullyQualifiedName~TextEntry" -- xUnit.ParallelizeTestCollections=false` → PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/GrantService.cs
git commit -m "refactor(grants): note-permission/assign-gate methods use unified AssignShifts (chore/duty keys preserved)"
```

---

## Task 11: Consumer #8 — `Month.cshtml` require-grant + hide deprecated in catalog

**Files:**
- Modify: `Pages/Calendar/Month.cshtml:217,336,397`
- Modify: `Pages/Owner/Hub/Grants.cshtml.cs:126`

- [ ] **Step 1: Replace the 3 require-grant keys**

In `Month.cshtml`, change all 3 occurrences:
```html
            <require-grant key="AssignShifts">
```
(single key — `mode="any"` no longer needed; remove the `mode` attribute or keep it harmlessly. Prefer removing it.)

- [ ] **Step 2: Hide deprecated grants from the admin catalog**

In `Grants.cshtml.cs`, the grant-type query (~line 126) — add a `.Where(gt => !gt.IsDeprecated)` so the 8 collapsed grants don't clutter the catalog or get re-assigned. The `TotalGrantTypes` count (line 120, `CountAsync(gt => gt.IsActive)`) — change to `CountAsync(gt => gt.IsActive && !gt.IsDeprecated)`.

- [ ] **Step 3: Build + run Owner-hub tests if any**

Run: `dotnet build "ShiftManager.csproj" --nologo` → `Build succeeded`.
Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~Grants|FullyQualifiedName~Owner" -- xUnit.ParallelizeTestCollections=false` → PASS.

- [ ] **Step 4: Commit**

```bash
git add Pages/Calendar/Month.cshtml Pages/Owner/Hub/Grants.cshtml.cs
git commit -m "refactor(grants): Month require-grant uses AssignShifts; hide deprecated grants from catalog"
```

---

## Task 12: Regression — former tech-only holder can now assign; old-key tests

**Files:**
- Modify: `ShiftManager.Tests/MasterTests/GrantAuthorization/AuthorizationDenialTests.cs` (and `GrantScopeResolutionTests.cs` if they reference old keys)
- Create/extend: a regression test that a holder of `AssignShifts` (formerly AssignHanavaShifts only) passes `CanAssignForShiftScopeAsync`.

- [ ] **Step 1: Find tests referencing the old keys**

Run: `grep -rln "AssignAlhutShifts\|AssignBRShifts\|AssignTechShifts\|AssignHanavaShifts" ShiftManager.Tests/`
For each, decide: if it asserts a base role is DENIED an assign grant, repoint it at `AssignShifts`; if it asserts the deprecated key resolves, either delete or convert to "deprecated key not assigned to templates."

- [ ] **Step 2: Add the lockout-fix regression (RED then GREEN)**

Add to `AuthorizationDenialTests.cs` (or a new `AssignShiftsCollapseTests.cs`): a user granted `AssignShifts` at molecule scope passes `HasGrantWithScopeAsync(user, "AssignShifts", moleculeId: m)` for that molecule — proving a single grant authorizes all shift families (the tech lockout is gone). Run it; it should pass immediately against the new seed (this is a guard, not a RED-first behavior change).

- [ ] **Step 3: Run the full grant-authorization suite**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" --filter "FullyQualifiedName~GrantAuthorization" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add ShiftManager.Tests/MasterTests/GrantAuthorization/
git commit -m "test(grants): AssignShifts collapse regressions + repoint old-key assertions"
```

---

## Task 13: Full suite, FinalProductPublish regen, docs/MEMORY

**Files:**
- Run: `scripts/Update-FinalProductPublish.ps1`
- Modify: `docs/reference/GRANTS-AND-PERMISSIONS-DEEP-DIVE.md` (grant count → 137; note AssignShifts + 8 deprecated)
- Modify: the `grant_change_checklist` touchpoints + `MEMORY.md` grant note (→ 137 grant types; one active assign grant; 8 deprecated)

- [ ] **Step 1: Full sequential suite (the gate)**

Run: `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false`
Expected: `Failed: 0` — all pass.

- [ ] **Step 2: Regenerate FinalProductPublish**

Run: `pwsh scripts/Update-FinalProductPublish.ps1` (or `powershell -File scripts/Update-FinalProductPublish.ps1`).
Expected: the `FinalProductPublish/` mirror updates (do NOT hand-edit it).

- [ ] **Step 3: Update docs + MEMORY**

In `docs/reference/GRANTS-AND-PERMISSIONS-DEEP-DIVE.md`: update the grant count to 137 and document `AssignShifts` (137) + the 8 deprecated. In `MEMORY.md` Patterns section: update "Grant system: **136** grant types" → **137**, last addition `AssignShifts` (137, Shift/Molecule), note the 8 `Assign*Shifts` are deprecated (kept for id stability, `IsDeprecated=true`, replaced by AssignShifts; permission is job-type-agnostic).

- [ ] **Step 4: Commit**

```bash
git add FinalProductPublish/ docs/ "C:/Users/katzi/.claude/projects/C--Users-katzi-Downloads-ShiftManager/memory/MEMORY.md"
git commit -m "docs(grants): AssignShifts collapse — regen FinalProductPublish, update grant docs/MEMORY (137 types)"
```

---

## Self-Review notes (author)

- **Spec coverage:** §3a steps 1-7 map to Tasks 1/2/3 (new grant + resx + templates), Task 4/5 (deprecate flag + self-heal migration), Tasks 6-11 (9 consumers), Task 8 (fix #5 folded), Tasks 12/13 (tests + docs + FinalProductPublish). Efficiency #8 (4-query fan-out) is resolved by Tasks 6/7/9/10 collapsing the OR-chains to one call.
- **Out of scope (deferred, per spec §13):** candidate-filtering / eligibility (sub-project 3b); the `probeShiftJobTypeId` deletion (kept harmless); structural authz page-filter (altitude I2) and bidirectional RepairUserGrants (I3) — post-collapse hardening.
- **Ordering trap:** Task 5 (self-heal) must land its `HealAsync` call BEFORE the repair passes in `Program.cs`; the seed/template changes (Tasks 1/3) must precede a deploy so the heal has the new grant + cleaned templates to reconcile against.
- **InlineData counts (Task 3):** deliberately recompute-and-verify against the test failure rather than hard-coding, because the seed is the source of truth and the exact per-template totals depend on LATE-ADDITIONS rows not in scope here.
