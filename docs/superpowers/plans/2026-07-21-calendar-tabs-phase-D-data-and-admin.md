# Calendar Tabs — Phase D: Data Model + Service + Admin Page — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reshape the `ShiftTab` schema to `(Molecule, JobType)`-scoped tabs with many-to-many company + shift-type membership, rewrite `ShiftTabService` to match, add the `ManageCalendarTabs` grant, and ship a dedicated `/Admin/Organization/Tabs` management page — retiring the old `/Owner/Blueprints` tab editor.

**Architecture:** `ShiftTab` becomes scoped to exactly one `(MoleculeId, JobTypeId?)`, carrying `NameEn`/`NameHe`/`PrioritizeCompanyUsers`/audit. Company membership (`ShiftTabCompany`) and a NEW shift-type membership (`ShiftTabShiftType`) are both many-to-many join tables (a company/shift type may belong to several tabs). The `ShiftType.TabId` column + its FK are dropped; the "Main" pseudo-tab concept is deleted (the always-present synthetic **"All"** view is Phase E's job and has no DB row). A new molecule-scoped grant `ManageCalendarTabs` gates a new admin page that follows the `ChoreTypes` CRUD + IDOR pattern. This phase delivers the **data + service + admin surface only**; the `/Calendar/Shifts` strip, view-filtering, prioritization, and last-tab UX are **Phase E** — the calendar readers are bridged to compile with a safe "no tab filter" interim.

**Tech Stack:** ASP.NET Core 8.0 Razor Pages, EF Core 8 + SQLite, xUnit + FluentAssertions, real-SQLite test fixture (`SqliteDbContextFixture`), resx localization (`SharedResources.resx` + `.he-IL.resx`), grant-policy authorization (`Grant:<Key>`).

## Global Constraints

- **Branch:** `feat/calendar-tabs-selectors`. Do all work here.
- **Build in a git worktree with `-c Release`.** The running dev app on :5000 locks `bin/Debug`; a Release build in an isolated worktree avoids the lock. If a lock still appears, STOP and ask the user to close the app (do not retry over a lock).
- **After any build, run `git checkout -- packages.lock.json`** — the build mutates it and it must stay clean for `git commit` (a clean tree is also required to publish later).
- **Tests run serialized:** `dotnet test --filter <...> -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`. Parallel runs give spurious `:memory:` SQLite failures. The FULL-suite gate at the end uses the same flags.
- **Grant seed is append-only.** `GrantType` IDs are assigned by `id++`; NEVER insert mid-list. The new grant is id **138** (current last = 137 `AssignShifts`). `RoleTemplateSeed` references grants by numeric id.
- **Localization:** every new string goes in BOTH `Resources/SharedResources.resx` AND `Resources/SharedResources.he-IL.resx`. **Grep each key before adding** (the `Next7Days` duplicate-key lesson breaks localization tests).
- **`IgnoreQueryFilters()` requires a `SECURITY-AUDITED` comment** at the call site (tab config is molecule-scoped, not tenant-filtered).
- **IGNORE `FinalProductPublish\`** entirely — it is generated output, never edit it.
- **EF migration workflow:** after reshaping the model, run `dotnet ef migrations add <Name>`, then read the generated file and hand-edit it (data reset), then verify it applies.

---

## Task 1: Reshape entities + EF config + `ShiftTabService`, bridge calendar readers, remove the Blueprints tab editor

This is one atomic task because dropping `ShiftType.TabId` and changing the service signatures breaks compilation across `Shifts.cshtml.cs`, `GetShiftsData.cshtml.cs`, and `Blueprints.cshtml(.cs)` simultaneously, and the test project references the web project — nothing compiles or tests until every consumer is fixed. TDD is driven by the rewritten `ShiftTabServiceTests`.

**Files:**
- Modify: `Models/ShiftTab.cs`
- Modify: `Models/ShiftTabCompany.cs`
- Modify: `Models/UserShiftTabPreference.cs`
- Modify: `Models/ShiftType.cs` (drop `TabId` + `Tab` nav)
- Create: `Models/ShiftTabShiftType.cs`
- Modify: `Data/AppDbContext.cs` (DbSet + config block ~117-119, ~1454-1509)
- Modify: `Services/IShiftTabService.cs`
- Modify: `Services/ShiftTabService.cs`
- Modify: `Pages/Calendar/Shifts.cshtml.cs` (service-call signatures + drop `TabId` reads — interim bridge)
- Modify: `Pages/Api/Calendar/GetShiftsData.cshtml.cs:103-108` (drop `TabId` read — interim bridge)
- Modify: `Pages/Owner/Blueprints.cshtml.cs` (remove tab handlers, DTOs, props, ctor injection, tab loading)
- Modify: `Pages/Owner/Blueprints.cshtml` (remove tab markup + tab column + per-shift `TabId` control)
- Test: `ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs` (rewrite to new contract)

**Interfaces:**
- Produces (entity fields):
  - `ShiftTab { int Id; int MoleculeId; int? JobTypeId; string NameEn; string NameHe; bool PrioritizeCompanyUsers; string? Color; int SortOrder; bool IsActive; DateTime CreatedAt; int? CreatedByUserId; DateTime? UpdatedAt; Molecule Molecule; JobType? JobType; List<ShiftTabShiftType> ShiftTypes; List<ShiftTabCompany> Companies; }`
  - `ShiftTabCompany { int ShiftTabId; int CompanyId; ShiftTab ShiftTab; Company Company; }` (composite PK, no `Id`)
  - `ShiftTabShiftType { int ShiftTabId; int ShiftTypeId; ShiftTab ShiftTab; ShiftType ShiftType; }` (composite PK)
  - `UserShiftTabPreference { int Id; int UserId; int MoleculeId; int? JobTypeId; int? TabId; DateTime UpdatedAt; ShiftTab? Tab; }`
- Produces (service — consumed by Task 4 admin page + Phase E):
  - `Task<List<ShiftTab>> GetTabsForMoleculeAsync(int moleculeId, int? jobTypeId, bool includeInactive = false)`
  - `Task<ShiftTab?> GetTabAsync(int tabId)`
  - `Task<ShiftTab?> CreateAsync(int moleculeId, int? jobTypeId, string nameEn, string nameHe, string? color = null, int? createdByUserId = null)`
  - `Task<bool> RenameAsync(int tabId, string nameEn, string nameHe, string? color, bool prioritizeCompanyUsers)`
  - `Task<bool> DeleteAsync(int tabId)`
  - `Task<(int ShiftTypeCount, int CompanyCount)> GetUsageAsync(int tabId)`
  - `Task<bool> SetCompaniesForTabAsync(int tabId, IReadOnlyCollection<int> companyIds)`
  - `Task<bool> SetShiftTypesForTabAsync(int tabId, IReadOnlyCollection<int> shiftTypeIds)`
  - `Task<HashSet<int>> GetCompanyIdsForTabAsync(int tabId)`
  - `Task<HashSet<int>> GetShiftTypeIdsForTabAsync(int tabId)`
  - `Task<int?> GetLastTabAsync(int userId, int moleculeId, int? jobTypeId)`
  - `Task SetLastTabAsync(int userId, int moleculeId, int? jobTypeId, int? tabId)`
  - **Removed:** `ReorderTabsAsync`, `AssignShiftTypeToTabAsync`, `AssignCompanyToTabAsync`.

---

- [ ] **Step 1: Rewrite the failing service tests to the new contract**

Replace the entire body of `ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs` with:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for the reshaped <see cref="ShiftTabService"/> (calendar "tabs" / לשונית):
/// (molecule, jobtype)-scoped CRUD + dual NameEn/NameHe uniqueness, many-to-many company + shift-type
/// membership (replace-set + cross-molecule/scope rejection), usage counts, per-(user,molecule,jobtype)
/// last-tab memory, and delete cascade (company + shift-type join rows drop, remembered pref → SetNull).
/// </summary>
public sealed class ShiftTabServiceTests
{
    // Molecule 1 (jobtypes 10,11) owns companies 1,2,3; molecule 2 owns company 4.
    private static async Task SeedHierarchyAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        f.Db.JobTypes.AddRange(
            new JobType { Id = 10, Name = "Alhut" },
            new JobType { Id = 11, Name = "Text" });
        f.Db.Companies.AddRange(
            new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 },
            new Company { Id = 2, Name = "Co2", Slug = "co2", MoleculeId = 1 },
            new Company { Id = 3, Name = "Co3", Slug = "co3", MoleculeId = 1 },
            new Company { Id = 4, Name = "Co4", Slug = "co4", MoleculeId = 2 });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Is_Unique_Per_MoleculeJobtype_Over_Both_Names_But_Reusable_Across_Scope()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);

        (await svc.CreateAsync(1, 10, "Geo", "גאו")).Should().NotBeNull();
        (await svc.CreateAsync(1, 10, "Geo", "אחר")).Should().BeNull("NameEn already used in (mol1, Alhut)");
        (await svc.CreateAsync(1, 10, "Other", "גאו")).Should().BeNull("NameHe already used in (mol1, Alhut)");
        (await svc.CreateAsync(1, 11, "Geo", "גאו")).Should().NotBeNull("free in a different jobtype");
        (await svc.CreateAsync(2, 10, "Geo", "גאו")).Should().NotBeNull("free in a different molecule");
        (await svc.CreateAsync(1, null, "Tech", "טכני")).Should().NotBeNull("null jobtype (Tech) is allowed");
    }

    [Fact]
    public async Task GetTabsForMolecule_Is_Scoped_To_Jobtype_And_Ordered()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        await svc.CreateAsync(1, 10, "Bravo", "ב");
        await svc.CreateAsync(1, 10, "Alpha", "א");
        await svc.CreateAsync(1, 11, "TextTab", "טקסט");

        var alhut = await svc.GetTabsForMoleculeAsync(1, 10);
        alhut.Select(t => t.NameEn).Should().ContainInOrder("Bravo", "Alpha"); // SortOrder (insertion) then NameEn
        alhut.Should().OnlyContain(t => t.JobTypeId == 10);
        (await svc.GetTabsForMoleculeAsync(1, 11)).Should().ContainSingle(t => t.NameEn == "TextTab");
        (await svc.GetTabsForMoleculeAsync(1, null)).Should().BeEmpty("no null-jobtype tabs in mol1");
    }

    [Fact]
    public async Task Rename_Updates_Names_Color_Priority_And_Rejects_Collision()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        await svc.CreateAsync(1, 10, "Geo", "גאו");
        var b = await svc.CreateAsync(1, 10, "Tacti", "טקטי");

        (await svc.RenameAsync(b!.Id, "Geo", "טקטי", null, true)).Should().BeFalse("NameEn collides in scope");
        (await svc.RenameAsync(b.Id, "Tacti", "גאו", null, true)).Should().BeFalse("NameHe collides in scope");
        (await svc.RenameAsync(b.Id, "Tacti2", "טקטי2", "#ff0000", false)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        var reloaded = await svc.GetTabAsync(b.Id);
        reloaded!.NameEn.Should().Be("Tacti2");
        reloaded.NameHe.Should().Be("טקטי2");
        reloaded.Color.Should().Be("#ff0000");
        reloaded.PrioritizeCompanyUsers.Should().BeFalse();
        reloaded.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task NewTab_Defaults_PrioritizeCompanyUsers_On_And_Stamps_Audit()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var tab = await svc.CreateAsync(1, 10, "Geo", "גאו", createdByUserId: 42);
        tab!.PrioritizeCompanyUsers.Should().BeTrue("UD3 default ON");
        tab.CreatedByUserId.Should().Be(42);
    }

    [Fact]
    public async Task SetCompanies_Replaces_Membership_Allows_ManyTabs_And_Rejects_CrossMolecule()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        var tacti = await svc.CreateAsync(1, 10, "Tacti", "טקטי");

        (await svc.SetCompaniesForTabAsync(geo!.Id, new[] { 1, 2 })).Should().BeTrue();
        (await svc.SetCompaniesForTabAsync(tacti!.Id, new[] { 1 })).Should().BeTrue("a company may be on many tabs now");
        (await svc.GetCompanyIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 1, 2 });
        (await svc.GetCompanyIdsForTabAsync(tacti.Id)).Should().BeEquivalentTo(new[] { 1 });

        (await svc.SetCompaniesForTabAsync(geo.Id, new[] { 2, 3 })).Should().BeTrue("replace-set");
        (await svc.GetCompanyIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 2, 3 });

        (await svc.SetCompaniesForTabAsync(geo.Id, new[] { 4 })).Should().BeFalse("company 4 is in molecule 2 (IDOR)");
        (await svc.GetCompanyIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 2, 3 }, "rejected — membership unchanged");

        (await svc.SetCompaniesForTabAsync(geo.Id, System.Array.Empty<int>())).Should().BeTrue("empty = no restriction");
        (await svc.GetCompanyIdsForTabAsync(geo.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetShiftTypes_Replaces_Membership_And_Rejects_OutOfScope()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.AddRange(
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "MORNING" },
            new ShiftType { Id = 101, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = null, Key = "OFFLINE" },
            new ShiftType { Id = 200, Scope = ShiftScope.Molecule, MoleculeId = 2, JobTypeId = 10, Key = "MORNING" },
            new ShiftType { Id = 300, Scope = ShiftScope.Area, AreaId = 1, Key = "AREA_DUTY" });
        await f.Db.SaveChangesAsync();
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");

        (await svc.SetShiftTypesForTabAsync(geo!.Id, new[] { 100, 101 })).Should().BeTrue("in molecule; jobtype match or null");
        (await svc.GetShiftTypeIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 100, 101 });

        (await svc.SetShiftTypesForTabAsync(geo.Id, new[] { 200 })).Should().BeFalse("shift type in molecule 2");
        (await svc.SetShiftTypesForTabAsync(geo.Id, new[] { 300 })).Should().BeFalse("area-scoped shift is never molecule-tabbed");
        (await svc.GetShiftTypeIdsForTabAsync(geo.Id)).Should().BeEquivalentTo(new[] { 100, 101 }, "rejected — unchanged");
    }

    [Fact]
    public async Task GetUsage_Counts_ShiftType_And_Company_Join_Rows()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.Add(new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "MORNING" });
        await f.Db.SaveChangesAsync();
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        await svc.SetCompaniesForTabAsync(geo!.Id, new[] { 1, 2 });
        await svc.SetShiftTypesForTabAsync(geo.Id, new[] { 100 });

        var (shiftTypeCount, companyCount) = await svc.GetUsageAsync(geo.Id);
        shiftTypeCount.Should().Be(1);
        companyCount.Should().Be(2);
    }

    [Fact]
    public async Task Delete_Cascades_Company_And_ShiftType_Joins_And_Clears_RememberedPref()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ShiftTypes.Add(new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, JobTypeId = 10, Key = "MORNING" });
        await f.Db.SaveChangesAsync();
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        await svc.SetCompaniesForTabAsync(geo!.Id, new[] { 1 });
        await svc.SetShiftTypesForTabAsync(geo.Id, new[] { 100 });
        await svc.SetLastTabAsync(userId: 10, moleculeId: 1, jobTypeId: 10, tabId: geo.Id);

        (await svc.DeleteAsync(geo.Id)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await f.Db.ShiftTabCompanies.CountAsync(tc => tc.ShiftTabId == geo.Id)).Should().Be(0, "company links cascade");
        (await f.Db.ShiftTabShiftTypes.CountAsync(x => x.ShiftTabId == geo.Id)).Should().Be(0, "shift-type links cascade");
        (await f.Db.ShiftTypes.FindAsync(100)).Should().NotBeNull("the shift type itself is untouched");
        (await svc.GetLastTabAsync(10, 1, 10)).Should().BeNull("remembered pref reverts (FK SetNull)");
    }

    [Fact]
    public async Task LastTab_Memory_Is_Isolated_Per_UserMoleculeJobtype_Including_NullJobtype()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ShiftTabService(f.Db);
        var geo = await svc.CreateAsync(1, 10, "Geo", "גאו");
        var tech = await svc.CreateAsync(1, null, "Tech", "טכני");

        (await svc.GetLastTabAsync(10, 1, 10)).Should().BeNull("no preference yet");

        await svc.SetLastTabAsync(10, 1, 10, geo!.Id);
        (await svc.GetLastTabAsync(10, 1, 10)).Should().Be(geo.Id);
        (await svc.GetLastTabAsync(10, 1, 11)).Should().BeNull("different jobtype");
        (await svc.GetLastTabAsync(10, 1, null)).Should().BeNull("different (null) jobtype");

        await svc.SetLastTabAsync(10, 1, null, tech!.Id); // null-jobtype (Tech) scope
        (await svc.GetLastTabAsync(10, 1, null)).Should().Be(tech.Id);

        await svc.SetLastTabAsync(10, 1, 10, null); // explicit clear
        (await svc.GetLastTabAsync(10, 1, 10)).Should().BeNull();
        (await svc.GetLastTabAsync(11, 1, 10)).Should().BeNull("different user");
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail to compile / fail**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ShiftTabServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: **build failure** (new methods/fields/entity `ShiftTabShiftTypes` don't exist yet). This is the red state.

- [ ] **Step 3: Reshape `Models/ShiftTab.cs`**

Replace the class body (keep the namespace + doc-comment header, but update the doc comment's "Main"/`TabId` references) with:

```csharp
namespace ShiftManager.Models;

/// <summary>
/// A calendar "tab" (Hebrew: לשונית) scoped to exactly one <c>(Molecule, JobType)</c> — a named
/// sub-calendar within a molecule's job-type calendar. Companies join a tab via <see cref="ShiftTabCompany"/>
/// (people-view roster base + selector prioritization) and shift types via <see cref="ShiftTabShiftType"/>
/// (by-shift view). Both memberships are many-to-many; empty = "no restriction" (all). There is no stored
/// "Main" tab — the always-present synthetic "All" view is view-only (no DB row).
///
/// A tab is purely a VIEW/relevance partition: it filters what the Shifts calendar renders and PRIORITIZES
/// pickers, but NEVER narrows eligibility, overlap/rest/weekly-hours, or fairness/Justice.
///
/// NOT tenant-filtered (no IBelongsToCompany); visibility is molecule-scoped via MoleculeId, isolation rests
/// on explicit MoleculeId checks at every call site.
/// </summary>
public class ShiftTab
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    /// <summary>The job type this tab belongs to. Null only for Tech molecules (calendar job type is null).</summary>
    public int? JobTypeId { get; set; }
    public string NameEn { get; set; } = string.Empty;   // English display name (required)
    public string NameHe { get; set; } = string.Empty;   // Hebrew display name (required)
    /// <summary>Prioritize this tab's companies' users in the assignment pickers (+ off-tab warning).</summary>
    public bool PrioritizeCompanyUsers { get; set; } = true;
    public string? Color { get; set; }                    // optional hex for the active pill, e.g. "#F0C14B"
    public int SortOrder { get; set; }                    // stable strip order (no drag-drop UI)
    public bool IsActive { get; set; } = true;

    // Audit
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public JobType? JobType { get; set; }
    public List<ShiftTabShiftType> ShiftTypes { get; set; } = new();   // shift types on this tab
    public List<ShiftTabCompany> Companies { get; set; } = new();      // companies on this tab
}
```

- [ ] **Step 4: Reshape `Models/ShiftTabCompany.cs` (composite PK, no `Id`)**

Replace the class body with:

```csharp
namespace ShiftManager.Models;

/// <summary>
/// Assigns a <see cref="Company"/> to a <see cref="ShiftTab"/> — the tab's people-view roster base +
/// selector prioritization set. A company may now belong to MULTIPLE tabs (across job types and within a
/// job type's tab set). Composite key <c>(ShiftTabId, CompanyId)</c>; both FKs cascade-delete.
/// </summary>
public class ShiftTabCompany
{
    public int ShiftTabId { get; set; }
    public int CompanyId { get; set; }

    // Navigation
    public ShiftTab ShiftTab { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
```

- [ ] **Step 5: Create `Models/ShiftTabShiftType.cs`**

```csharp
namespace ShiftManager.Models;

/// <summary>
/// Assigns a <see cref="ShiftType"/> to a <see cref="ShiftTab"/> — the tab's by-shift view membership.
/// Replaces the old <c>ShiftType.TabId</c> single-tab column: a shift type may now appear in multiple tabs'
/// views, and "no rows for a tab" means "no restriction" (all job-type shift types). Composite key
/// <c>(ShiftTabId, ShiftTypeId)</c>; both FKs cascade-delete.
/// </summary>
public class ShiftTabShiftType
{
    public int ShiftTabId { get; set; }
    public int ShiftTypeId { get; set; }

    // Navigation
    public ShiftTab ShiftTab { get; set; } = null!;
    public ShiftType ShiftType { get; set; } = null!;
}
```

- [ ] **Step 6: Reshape `Models/UserShiftTabPreference.cs` (+`JobTypeId`)**

Replace the class body with:

```csharp
namespace ShiftManager.Models;

/// <summary>
/// A single user's remembered tab selection per <c>(Molecule, JobType)</c> on the Shifts calendar. NOT
/// IBelongsToCompany — a per-user UI preference read only for its owner. <see cref="TabId"/> null =
/// no explicit tab (falls back to the synthetic "All" view). FK TabId → ShiftTab is SET NULL, so deleting a
/// tab reverts anyone's remembered preference. Unique per <c>(UserId, MoleculeId, JobTypeId)</c>.
/// </summary>
public class UserShiftTabPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int MoleculeId { get; set; }
    public int? JobTypeId { get; set; }
    public int? TabId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ShiftTab? Tab { get; set; }
}
```

- [ ] **Step 7: Drop `TabId` + `Tab` nav from `Models/ShiftType.cs`**

Delete line 46:
```csharp
    public int? TabId { get; set; }            // Calendar tab / לשונית (molecule sub-calendar) — see ShiftTab; null = "Main"
```
Delete line 223 (the `Tab` navigation, at the end of the nav-property block):
```csharp
    public ShiftTab? Tab { get; set; }
```

- [ ] **Step 8: Update `Data/AppDbContext.cs` — DbSet + config**

Add the join DbSet after line 119 (`public DbSet<UserShiftTabPreference> UserShiftTabPreferences => Set<UserShiftTabPreference>();`):
```csharp
    public DbSet<ShiftTabShiftType> ShiftTabShiftTypes => Set<ShiftTabShiftType>();
```

Replace the entire ShiftTab configuration block (lines ~1460-1509, from `modelBuilder.Entity<ShiftTab>()` through the `UserShiftTabPreference` FK config) with:

```csharp
        modelBuilder.Entity<ShiftTab>()
            .HasOne(t => t.Molecule)
            .WithMany()
            .HasForeignKey(t => t.MoleculeId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ShiftTab>()
            .HasOne(t => t.JobType)
            .WithMany()
            .HasForeignKey(t => t.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Lookup index + dual-name uniqueness within a (molecule, jobtype) scope.
        modelBuilder.Entity<ShiftTab>()
            .HasIndex(t => new { t.MoleculeId, t.JobTypeId });
        modelBuilder.Entity<ShiftTab>()
            .HasIndex(t => new { t.MoleculeId, t.JobTypeId, t.NameEn })
            .IsUnique();
        modelBuilder.Entity<ShiftTab>()
            .HasIndex(t => new { t.MoleculeId, t.JobTypeId, t.NameHe })
            .IsUnique();

        // ShiftTabShiftType: many-to-many shift-type membership (replaces ShiftType.TabId). Composite PK;
        // both FKs cascade so deleting a tab (or a shift type) drops the link.
        modelBuilder.Entity<ShiftTabShiftType>()
            .HasKey(x => new { x.ShiftTabId, x.ShiftTypeId });
        modelBuilder.Entity<ShiftTabShiftType>()
            .HasOne(x => x.ShiftTab)
            .WithMany(t => t.ShiftTypes)
            .HasForeignKey(x => x.ShiftTabId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ShiftTabShiftType>()
            .HasOne(x => x.ShiftType)
            .WithMany()
            .HasForeignKey(x => x.ShiftTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        // ShiftTabCompany: many-to-many company membership (a company may be on several tabs). Composite PK
        // (no more UNIQUE(CompanyId)); both FKs cascade.
        modelBuilder.Entity<ShiftTabCompany>()
            .HasKey(tc => new { tc.ShiftTabId, tc.CompanyId });
        modelBuilder.Entity<ShiftTabCompany>()
            .HasOne(tc => tc.ShiftTab)
            .WithMany(t => t.Companies)
            .HasForeignKey(tc => tc.ShiftTabId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ShiftTabCompany>()
            .HasOne(tc => tc.Company)
            .WithMany()
            .HasForeignKey(tc => tc.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // UserShiftTabPreference: per-user remembered tab per (molecule, jobtype). TabId → ShiftTab SetNull.
        modelBuilder.Entity<UserShiftTabPreference>()
            .HasIndex(p => new { p.UserId, p.MoleculeId, p.JobTypeId })
            .IsUnique();
        modelBuilder.Entity<UserShiftTabPreference>()
            .HasOne(p => p.Tab)
            .WithMany()
            .HasForeignKey(p => p.TabId)
            .OnDelete(DeleteBehavior.SetNull);
```

Note: the old `ShiftType → ShiftTab` FK/index config (old lines 1473-1481) is intentionally NOT carried over — it is deleted with this replacement.

- [ ] **Step 9: Rewrite `Services/IShiftTabService.cs`**

Replace the whole file with:

```csharp
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Manages <c>(Molecule, JobType)</c>-scoped <see cref="ShiftTab"/> entities (Hebrew: לשונית). A tab has two
/// admin-set memberships: shift types (<see cref="ShiftTabShiftType"/>, by-shift view) and companies
/// (<see cref="ShiftTabCompany"/>, people-view roster base + prioritization). Both are many-to-many; empty =
/// no restriction (all). Also owns per-user "last tab" memory (<see cref="UserShiftTabPreference"/>).
///
/// A tab is a VIEW/relevance partition only — it never affects eligibility, overlap/rest/hours, or fairness.
/// </summary>
public interface IShiftTabService
{
    // ---- Tab queries ----
    Task<List<ShiftTab>> GetTabsForMoleculeAsync(int moleculeId, int? jobTypeId, bool includeInactive = false);
    Task<ShiftTab?> GetTabAsync(int tabId);

    // ---- Tab CRUD ----
    /// <summary>Creates a tab. Returns null if NameEn OR NameHe already exists in the (molecule, jobtype)
    /// scope, or either name is blank. New tabs default PrioritizeCompanyUsers = true.</summary>
    Task<ShiftTab?> CreateAsync(int moleculeId, int? jobTypeId, string nameEn, string nameHe,
        string? color = null, int? createdByUserId = null);
    /// <summary>Edits name/color/prioritize. Returns false on not-found or a NameEn/NameHe collision.</summary>
    Task<bool> RenameAsync(int tabId, string nameEn, string nameHe, string? color, bool prioritizeCompanyUsers);
    /// <summary>Hard-deletes a tab: company + shift-type join rows cascade away; remembered prefs → SetNull.</summary>
    Task<bool> DeleteAsync(int tabId);
    /// <summary>Counts the shift-type and company join rows for a tab (delete-impact preview).</summary>
    Task<(int ShiftTypeCount, int CompanyCount)> GetUsageAsync(int tabId);

    // ---- Membership (many-to-many, replace-set) ----
    /// <summary>Replaces the tab's company set. Rejects (returns false, no change) if ANY company is not in the
    /// tab's molecule (IDOR guard). Empty = no restriction.</summary>
    Task<bool> SetCompaniesForTabAsync(int tabId, IReadOnlyCollection<int> companyIds);
    /// <summary>Replaces the tab's shift-type set. Rejects (returns false, no change) if ANY shift type is not
    /// in the tab's molecule or its jobtype scope (jobtype match or null). Empty = no restriction.</summary>
    Task<bool> SetShiftTypesForTabAsync(int tabId, IReadOnlyCollection<int> shiftTypeIds);
    /// <summary>The companies assigned to a tab (empty = no restriction).</summary>
    Task<HashSet<int>> GetCompanyIdsForTabAsync(int tabId);
    /// <summary>The shift types assigned to a tab (empty = no restriction).</summary>
    Task<HashSet<int>> GetShiftTypeIdsForTabAsync(int tabId);

    // ---- Per-user last-tab memory ----
    /// <summary>The user's remembered tab for a (molecule, jobtype) (null = none / the "All" view).</summary>
    Task<int?> GetLastTabAsync(int userId, int moleculeId, int? jobTypeId);
    /// <summary>Upserts the user's remembered tab for a (molecule, jobtype) (null = explicit "All").</summary>
    Task SetLastTabAsync(int userId, int moleculeId, int? jobTypeId, int? tabId);
}
```

- [ ] **Step 10: Rewrite `Services/ShiftTabService.cs`**

Replace the whole file with:

```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — ShiftTab/ShiftTabCompany/ShiftTabShiftType/ShiftType
// are molecule-scoped (no tenant filter); writes are scoped by explicit ids and every membership set enforces
// a same-molecule (and, for shift types, same-jobtype) invariant. The admin page re-verifies the
// ManageCalendarTabs grant against the molecule before every call.
public class ShiftTabService : IShiftTabService
{
    private readonly AppDbContext _db;

    public ShiftTabService(AppDbContext db) => _db = db;

    // ---- Tab queries ----

    public async Task<List<ShiftTab>> GetTabsForMoleculeAsync(int moleculeId, int? jobTypeId, bool includeInactive = false)
    {
        var q = _db.ShiftTabs.Where(t => t.MoleculeId == moleculeId && t.JobTypeId == jobTypeId);
        if (!includeInactive)
            q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.SortOrder).ThenBy(t => t.NameEn).ToListAsync();
    }

    public Task<ShiftTab?> GetTabAsync(int tabId)
        => _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);

    // ---- Tab CRUD ----

    public async Task<ShiftTab?> CreateAsync(int moleculeId, int? jobTypeId, string nameEn, string nameHe,
        string? color = null, int? createdByUserId = null)
    {
        nameEn = (nameEn ?? string.Empty).Trim();
        nameHe = (nameHe ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameHe))
            return null;

        // Dual-name uniqueness within (molecule, jobtype), before hitting the DB index → clean null.
        var clash = await _db.ShiftTabs.AnyAsync(t => t.MoleculeId == moleculeId && t.JobTypeId == jobTypeId
            && (t.NameEn == nameEn || t.NameHe == nameHe));
        if (clash)
            return null;

        var nextSort = await _db.ShiftTabs.Where(t => t.MoleculeId == moleculeId && t.JobTypeId == jobTypeId)
            .Select(t => (int?)t.SortOrder).MaxAsync() ?? -1;

        var tab = new ShiftTab
        {
            MoleculeId = moleculeId,
            JobTypeId = jobTypeId,
            NameEn = nameEn,
            NameHe = nameHe,
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            PrioritizeCompanyUsers = true,   // UD3
            SortOrder = nextSort + 1,
            IsActive = true,
            CreatedByUserId = createdByUserId
        };
        _db.ShiftTabs.Add(tab);
        await _db.SaveChangesAsync();
        return tab;
    }

    public async Task<bool> RenameAsync(int tabId, string nameEn, string nameHe, string? color, bool prioritizeCompanyUsers)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        nameEn = (nameEn ?? string.Empty).Trim();
        nameHe = (nameHe ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameHe))
            return false;

        var clash = await _db.ShiftTabs.AnyAsync(t => t.MoleculeId == tab.MoleculeId && t.JobTypeId == tab.JobTypeId
            && t.Id != tabId && (t.NameEn == nameEn || t.NameHe == nameHe));
        if (clash)
            return false;

        tab.NameEn = nameEn;
        tab.NameHe = nameHe;
        tab.Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        tab.PrioritizeCompanyUsers = prioritizeCompanyUsers;
        tab.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int tabId)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        // FK behavior handles the rest: ShiftTabCompany + ShiftTabShiftType → Cascade,
        // UserShiftTabPreference.TabId → SetNull. Shift instances/assignments are untouched.
        _db.ShiftTabs.Remove(tab);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(int ShiftTypeCount, int CompanyCount)> GetUsageAsync(int tabId)
    {
        var shiftTypeCount = await _db.ShiftTabShiftTypes.CountAsync(x => x.ShiftTabId == tabId);
        var companyCount = await _db.ShiftTabCompanies.CountAsync(tc => tc.ShiftTabId == tabId);
        return (shiftTypeCount, companyCount);
    }

    // ---- Membership (replace-set) ----

    public async Task<bool> SetCompaniesForTabAsync(int tabId, IReadOnlyCollection<int> companyIds)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        var ids = companyIds.Distinct().ToList();
        if (ids.Count > 0)
        {
            // C1 guard: every company must belong to the tab's molecule.
            var validCount = await _db.Companies.IgnoreQueryFilters()
                .CountAsync(c => ids.Contains(c.Id) && c.MoleculeId == tab.MoleculeId);
            if (validCount != ids.Count)
                return false;
        }

        var existing = await _db.ShiftTabCompanies.Where(tc => tc.ShiftTabId == tabId).ToListAsync();
        _db.ShiftTabCompanies.RemoveRange(existing.Where(e => !ids.Contains(e.CompanyId)));
        var have = existing.Select(e => e.CompanyId).ToHashSet();
        foreach (var cid in ids.Where(cid => !have.Contains(cid)))
            _db.ShiftTabCompanies.Add(new ShiftTabCompany { ShiftTabId = tabId, CompanyId = cid });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetShiftTypesForTabAsync(int tabId, IReadOnlyCollection<int> shiftTypeIds)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        var ids = shiftTypeIds.Distinct().ToList();
        if (ids.Count > 0)
        {
            // Every shift type must be in the tab's molecule AND its jobtype scope (jobtype match or null).
            var validCount = await _db.ShiftTypes.IgnoreQueryFilters()
                .CountAsync(st => ids.Contains(st.Id) && st.MoleculeId == tab.MoleculeId
                    && (st.JobTypeId == tab.JobTypeId || st.JobTypeId == null));
            if (validCount != ids.Count)
                return false;
        }

        var existing = await _db.ShiftTabShiftTypes.Where(x => x.ShiftTabId == tabId).ToListAsync();
        _db.ShiftTabShiftTypes.RemoveRange(existing.Where(e => !ids.Contains(e.ShiftTypeId)));
        var have = existing.Select(e => e.ShiftTypeId).ToHashSet();
        foreach (var sid in ids.Where(sid => !have.Contains(sid)))
            _db.ShiftTabShiftTypes.Add(new ShiftTabShiftType { ShiftTabId = tabId, ShiftTypeId = sid });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<HashSet<int>> GetCompanyIdsForTabAsync(int tabId)
        => (await _db.ShiftTabCompanies.Where(tc => tc.ShiftTabId == tabId)
            .Select(tc => tc.CompanyId).ToListAsync()).ToHashSet();

    public async Task<HashSet<int>> GetShiftTypeIdsForTabAsync(int tabId)
        => (await _db.ShiftTabShiftTypes.Where(x => x.ShiftTabId == tabId)
            .Select(x => x.ShiftTypeId).ToListAsync()).ToHashSet();

    // ---- Per-user last-tab memory ----

    public async Task<int?> GetLastTabAsync(int userId, int moleculeId, int? jobTypeId)
        => (await _db.UserShiftTabPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MoleculeId == moleculeId && p.JobTypeId == jobTypeId))?.TabId;

    public async Task SetLastTabAsync(int userId, int moleculeId, int? jobTypeId, int? tabId)
    {
        var pref = await _db.UserShiftTabPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MoleculeId == moleculeId && p.JobTypeId == jobTypeId);
        if (pref == null)
        {
            _db.UserShiftTabPreferences.Add(new UserShiftTabPreference
            {
                UserId = userId,
                MoleculeId = moleculeId,
                JobTypeId = jobTypeId,
                TabId = tabId,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            pref.TabId = tabId;
            pref.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 11: Bridge `Pages/Calendar/Shifts.cshtml.cs` to compile (interim — full tab UX is Phase E)**

The calendar's tab consumption is Phase E; here we only make it compile against the reshaped schema/service and behave as the safe "All" view (no tab filter). Make these exact edits:

(a) Tab resolution block — replace lines 271-299 (the `if (MoleculeId.HasValue) { AvailableTabs = ... }` block) with:
```csharp
        // Calendar tabs (לשונית): Phase D loads the strip data (molecule + jobtype scoped) but does NOT
        // filter/prioritize — the strip, view-filter, prioritization and last-tab UX are Phase E (PF10).
        // Interim behavior == the synthetic "All" view (no restriction). Tab stays null.
        if (MoleculeId.HasValue)
        {
            AvailableTabs = await _tabService.GetTabsForMoleculeAsync(MoleculeId.Value, JobTypeId);
            Tab = null; // Phase E resolves the active tab + last-tab memory
        }
```

(b) By-shift filter — delete lines 476-481 (the `// Tab (לשונית) ...` comment through the `else if (AvailableTabs.Count > 0)` filter):
```csharp
        // Tab (לשונית): a specific tab shows only its shift types; "Main" shows untagged shifts.
        // No tabs in the molecule → no filter (every shift is untagged anyway).
        if (Tab.HasValue)
            shiftTypeQuery = shiftTypeQuery.Where(st => st.TabId == Tab.Value);
        else if (AvailableTabs.Count > 0)
            shiftTypeQuery = shiftTypeQuery.Where(st => st.TabId == null);
```
Replace with:
```csharp
        // Tab view-filter (by-shift) is Phase E (PF4). Interim: no tab filter (the "All" view).
```

(c) By-user filter — delete lines 605-609 (the `// Tab (לשונית) ...` comment through the `else if`):
```csharp
        // Tab (לשונית): scope the assign bottom-sheet's shift-type list to the active tab (Main = untagged).
        if (Tab.HasValue)
            shiftTypeQuery = shiftTypeQuery.Where(st => st.TabId == Tab.Value);
        else if (AvailableTabs.Count > 0)
            shiftTypeQuery = shiftTypeQuery.Where(st => st.TabId == null);
```
Replace with:
```csharp
        // Tab view-filter (by-user shift-type list) is Phase E (PF4). Interim: no tab filter.
```

(d) By-user roster narrowing — delete lines 633-654 (the `// Tab (לשונית) roster ...` comment block through the `users = users.Where(...).ToList();`):
```csharp
        // Tab (לשונית) roster: company base (companies assigned to the tab; Main = unassigned companies)
        // ∪ cross-over (anyone assigned to one of this tab's shifts in view, INCLUDING staged draft
        // assignments — hence computed AFTER the draft overlay). A person can appear on multiple tabs
        // (mirrored rows). The tab NEVER narrows conflict/rest/hours/fairness — those see the full shift set.
        if (AvailableTabs.Count > 0)
        {
            bool TabMatches(int? shiftTabId) => Tab.HasValue ? shiftTabId == Tab.Value : shiftTabId == null;

            var tabCompanies = await _tabService.GetCompanyIdsForTabAsync(moleculeId, Tab);
            // shiftTypeId → TabId for the WHOLE molecule (not tab-filtered) so cross-over — and per-cell
            // ghosting (ShiftIsOnActiveTab) — resolve for any shift, including other tabs' shifts.
            _tabOfShiftType = await _db.ShiftTypes
                .Where(st => st.MoleculeId == moleculeId || (st.Scope == ShiftScope.Area && st.AreaId == userAreaId))
                .Select(st => new { st.Id, st.TabId })
                .ToDictionaryAsync(x => x.Id, x => x.TabId);
            var crossOver = assignments
                .Where(a => a.UserId.HasValue && a.ShiftInstance != null
                    && _tabOfShiftType.TryGetValue(a.ShiftInstance.ShiftTypeId, out var tid) && TabMatches(tid))
                .Select(a => a.UserId!.Value)
                .ToHashSet();
            users = users.Where(u => tabCompanies.Contains(u.CompanyId) || crossOver.Contains(u.Id)).ToList();
        }
```
Replace with:
```csharp
        // Tab roster narrowing + cross-over is Phase E (PF4/PF5). Interim: full molecule+jobtype roster.
```

(e) Ghosting field + helper — delete the field at line 47:
```csharp
    private Dictionary<int, int?> _tabOfShiftType = new();
```
and replace the `ShiftIsOnActiveTab` method (lines 1276-1281) with an interim stub:
```csharp
    // Tab ghosting is Phase E (PF4). Interim: nothing is ghosted (every shift renders as on-view).
    private bool ShiftIsOnActiveTab(int shiftTypeId) => true;
```
(Leave the `IsBusyElsewhere = !ShiftIsOnActiveTab(...)` caller at line 1315 as-is — it now always evaluates to `false`.)

- [ ] **Step 12: Bridge `Pages/Api/Calendar/GetShiftsData.cshtml.cs` to compile (interim)**

Replace lines 100-108 (the `// Tab (לשונית) ...` comment through the closing `}` of the `if (tab.HasValue)` block):
```csharp
            // Tab (לשונית): keep the shadow refresh aligned with the server render. The client sends
            // tab=<Tab ?? 0>: 0 = "Main" (untagged shifts), a real id = that tab. Instances are already
            // molecule-scoped, so a foreign/invalid tab id simply matches nothing (no cross-tenant leak).
            if (tab.HasValue)
            {
                instances = tab.Value == 0
                    ? instances.Where(i => i.ShiftType != null && i.ShiftType.TabId == null).ToList()
                    : instances.Where(i => i.ShiftType != null && i.ShiftType.TabId == tab.Value).ToList();
            }
```
Replace with:
```csharp
            // Tab-filtered refresh is Phase E (PF3/PF4). Interim: no tab filter (returns all molecule+jobtype
            // instances — the "All" view). The `tab` query param is accepted but ignored until Phase E.
```

- [ ] **Step 13: Remove the Blueprints tab editor (PF1 — code-behind)**

In `Pages/Owner/Blueprints.cshtml.cs`:
- Delete the field (line 32) `private readonly IShiftTabService _tabService;`, the ctor parameter (line 45) `IShiftTabService tabService`, and the assignment (line 57) `_tabService = tabService;`.
- Delete the tab-editor property declarations. Grep them first: `Grep "Tabs \|TabCompanyIds\|CanManageTabs\|MoleculeCompanies\|NewTabName\|NewTabColor" Pages/Owner/Blueprints.cshtml.cs` and delete each `[BindProperty]`/`public` declaration that exists only for the tab editor (`public List<ShiftTab> Tabs`, `public Dictionary<int, HashSet<int>> TabCompanyIds`, `public bool CanManageTabs`, `public List<Company> MoleculeCompanies`, `[BindProperty] public string? NewTabName`, `[BindProperty] public string? NewTabColor`).
- Delete the tab-loading block in `LoadDataAsync`/`OnGet` (lines 171-181, from the `// Shift tabs (לשונית) ...` comment through the `foreach (var tab in Tabs) TabCompanyIds[...] = ...` loop, and the `CanManageTabs = CanManageCategories;` line).
- Delete the `CanManageTabsAsync` helper (line 697).
- Delete these handlers in full: `OnPostCreateTabAsync` (699), `OnPostRenameTabAsync` (735), `OnGetCheckTabUsageAsync` (756), `OnPostDeleteTabAsync` (766), `OnPostAssignShiftTabAsync` (805), `OnPostAssignCompanyToTabAsync` (831), `OnPostReorderTabsAsync` (850).
- Delete the DTO classes `RenameTabRequest` (975), `AssignShiftTabRequest` (982), `AssignCompanyToTabRequest` (988), `ReorderTabsRequest` (994).
- After deleting, grep `_tabService` in the file to confirm zero remaining references: `Grep "_tabService" Pages/Owner/Blueprints.cshtml.cs` → expect no matches.

- [ ] **Step 14: Remove the Blueprints tab markup (PF1 — Razor + shift-type `TabId` control)**

In `Pages/Owner/Blueprints.cshtml`:
- Delete the `Tab` column header `<th><loc key="Blueprints_Tab" /></th>` (line 186).
- Delete the per-shift-type tab `<td>` cell that renders the `js-shift-tab` `<select>` and `currentTab` (the block spanning ~lines 310-335: `var currentTab = Model.Tabs.FirstOrDefault(...)` through its closing `</td>`).
- Delete the entire "Shift Tabs Section (לשונית)" block (from `@* Shift Tabs Section (לשונית) ... *@` at line 460 through the hidden `deleteTabForm` and its close — read from line 460 to the end of that `@if (Model.CanManageTabs && Model.SelectedMoleculeId.HasValue) { ... }` block and delete it whole).
- Grep the page's `@section Scripts` / any `blueprints*.js` for tab handlers and delete them: `Grep "js-tab\|js-shift-tab\|js-rename-tab\|js-delete-tab\|CheckTabUsage\|AssignShiftTab\|AssignCompanyToTab\|ReorderTabs\|deleteTabForm" Pages/Owner/Blueprints.cshtml wwwroot/js` → delete the JS event wiring and fetch calls those match (they POST to the now-deleted handlers). Leave the shift-category JS intact.

- [ ] **Step 15: Build the solution (Release, in the worktree) and confirm it compiles**

Run: `dotnet build -c Release`
Expected: **Build succeeded**, 0 errors. Then `git checkout -- packages.lock.json`.
If errors mention `TabId`, `AssignShiftTypeToTabAsync`, `AssignCompanyToTabAsync`, `ReorderTabsAsync`, `_tabOfShiftType`, `Model.Tabs`, or `CanManageTabs` — a Step 11-14 site was missed; fix it. Grep the whole app for stragglers: `Grep "\.TabId\b" Pages Services Models` (expect no matches outside comments) and `Grep "ReorderTabsAsync\|AssignShiftTypeToTabAsync\|AssignCompanyToTabAsync" Pages Services` (expect none).

- [ ] **Step 16: Run the reshaped service tests and confirm green**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ShiftTabServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: **Passed! - Failed: 0** (9 tests). Then `git checkout -- packages.lock.json`.

- [ ] **Step 17: Commit**

```bash
git add Models/ShiftTab.cs Models/ShiftTabCompany.cs Models/ShiftTabShiftType.cs Models/UserShiftTabPreference.cs Models/ShiftType.cs Data/AppDbContext.cs Services/IShiftTabService.cs Services/ShiftTabService.cs Pages/Calendar/Shifts.cshtml.cs Pages/Api/Calendar/GetShiftsData.cshtml.cs Pages/Owner/Blueprints.cshtml.cs Pages/Owner/Blueprints.cshtml ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs
git commit -m "feat(tabs): reshape ShiftTab to (molecule,jobtype) m2m schema + service; remove Blueprints tab editor (Phase D)"
```

**Covers:** PF6 (schema reshape — models/EF), PF11 (service signature ripple + all callers), PF1 (Blueprints tab editor + `ReorderTabsAsync` removed), ADM-9 (no tab controls remain in Blueprints/shift-type edit form), ADM-5 (empty=all membership semantics), ADM-8 (SortOrder-then-NameEn ordering), REG-5 (reshaped `ShiftTabServiceTests` compile + pass). Interim-bridged (full behavior = Phase E): STR/PRI/LTM/CON/DFT calendar consumption.

---

## Task 2: EF migration + data reset

**Files:**
- Create: `Migrations/<timestamp>_ReshapeShiftTabsForSelectors.cs` (+ `.Designer.cs`, snapshot updated by EF)

**Interfaces:**
- Consumes: the reshaped model from Task 1.
- Produces: a migration that rebuilds the SQLite tables (drop `ShiftType.TabId`+FK+index; re-key `ShiftTabCompany`; add `ShiftTabShiftTypes`; add `ShiftTab` columns + dual-name uniques; add `UserShiftTabPreference.JobTypeId` + unique change) and **DELETEs all `ShiftTab`, `ShiftTabCompany`, `UserShiftTabPreference` rows** (JobTypeId now enters the unique index — cannot backfill).

- [ ] **Step 1: Generate the migration**

Run: `dotnet ef migrations add ReshapeShiftTabsForSelectors`
Expected: creates `Migrations/<timestamp>_ReshapeShiftTabsForSelectors.cs`. Read it and confirm the `Up` contains: `DropForeignKey FK_ShiftTypes_ShiftTabs_TabId`, `DropColumn TabId` from `ShiftTypes`, a rebuild of `ShiftTabCompanies` (new composite PK, drop `Id` + `IX_ShiftTabCompanies_CompanyId` unique), `CreateTable ShiftTabShiftTypes`, added `ShiftTabs` columns (`JobTypeId`, `NameEn`, `NameHe`, `PrioritizeCompanyUsers`, `CreatedByUserId`, `UpdatedAt`) + dropped `Name`/`DisplayName` + old `IX_ShiftTabs_MoleculeId_Name`, new dual-name unique indexes, and `UserShiftTabPreferences.JobTypeId` + the `(UserId,MoleculeId,JobTypeId)` unique replacing `(UserId,MoleculeId)`.

- [ ] **Step 2: Hand-edit the migration to reset rows first**

At the VERY TOP of `Up(MigrationBuilder migrationBuilder)` (before any schema op), insert:
```csharp
            // Test data only (user-confirmed no production tab data). The reshape adds JobTypeId to the
            // ShiftTab uniqueness + the UserShiftTabPreference unique key, and drops ShiftType.TabId — none of
            // which can be back-filled deterministically. Clear all tab rows so the rebuilt tables start clean.
            migrationBuilder.Sql("DELETE FROM \"ShiftTabCompanies\";");
            migrationBuilder.Sql("DELETE FROM \"UserShiftTabPreferences\";");
            migrationBuilder.Sql("DELETE FROM \"ShiftTabs\";");
```
(`ShiftTabShiftTypes` is new — no rows to delete. `ShiftType.TabId` data is discarded by the column drop.)

- [ ] **Step 3: Build to confirm the migration compiles**

Run: `dotnet build -c Release`
Expected: **Build succeeded**. Then `git checkout -- packages.lock.json`.

- [ ] **Step 4: Verify the migration applies on a NON-EMPTY existing dev DB (REG-6)**

The dev connection string key is `"Default"`. Copy the current dev DB to a throwaway file and apply:
```bash
cp "<dev-db-path>.db" "$TMP/reshape-check.db"
ConnectionStrings__Default="Data Source=$TMP/reshape-check.db" dotnet ef database update
```
Expected: completes with no error; `Applying migration '<timestamp>_ReshapeShiftTabsForSelectors'`. Then open the DB and confirm `PRAGMA table_info(ShiftTypes)` no longer lists `TabId`, `ShiftTabShiftTypes` exists, and `ShiftTabs` has `NameEn`/`NameHe`/`JobTypeId`. (If you cannot locate the dev DB path, ask the user for it — do NOT skip this verification.)

- [ ] **Step 5: Commit**

```bash
git add Migrations/
git commit -m "feat(tabs): EF migration reshaping ShiftTab schema + resetting tab rows (Phase D)"
```

**Covers:** PF6 (migration + data reset), REG-6 (migration runs clean on a non-empty dev DB; app boots).

---

## Task 3: New grant `ManageCalendarTabs` + role-template auto-grant + test counts

**Files:**
- Modify: `Data/SeedData/GrantTypeSeed.cs` (append id 138)
- Modify: `Data/SeedData/RoleTemplateSeed.cs` (LATE ADDITIONS block)
- Modify: `ShiftManager.Tests/MasterTests/GrantAuthorization/RoleTemplateAutoGrantTests.cs` (InlineData counts)
- Modify: `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx` (grant name/desc keys)

**Interfaces:**
- Produces: grant key `"ManageCalendarTabs"` (id 138, `Category=Shift`, `DefaultScope=Molecule`, `IsSystem=true`), auto-granted to templates 3/2/5/7/8/9/10/11. Consumed by Task 4's `[Authorize(Policy="Grant:ManageCalendarTabs")]` and the nav leaf (Task 3b integrates via NavRegistry in Task 4... nav is Task 4? — see below).

- [ ] **Step 1: Append the grant type (append-only) in `GrantTypeSeed.cs`**

After the `AssignShifts` (id 137) line (currently the last grant, ~line 356), before `return grants;`, add:
```csharp

        // Id 138 — ManageCalendarTabs: molecule-scoped management of calendar tabs (לשונית) on
        // /Admin/Organization/Tabs. Floor = Lead (מפ"צ) and above. Distinct from ManageShiftCategories —
        // tabs are a separate lead-managed surface. Append-only per the Grant Change Checklist.
        grants.Add(new GrantType { Id = id++, Key = "ManageCalendarTabs", NameKey = "Grant_ManageCalendarTabs", DescriptionKey = "Grant_ManageCalendarTabs_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
```

- [ ] **Step 2: Auto-grant to Lead-and-above in `RoleTemplateSeed.cs` (LATE ADDITIONS)**

After the `ManageShiftCategories (ID 136)` block (ends ~line 1041, right before `return grants;`), add:
```csharp

        // ============================================
        // ManageCalendarTabs (ID 138) — Calendar tabs admin (2026-07-21)
        // Same Lead-and-above floor as ManageShiftCategories (136): Assigner (8) uses SAR (role scope IS
        // Molecule); DepartmentLead (9) uses ETM to expand Department → molecule. Employee (1) + Trainee (12)
        // intentionally excluded.
        // ============================================
        grants.Add(G(3, 138, ETM));                   // Lead (מפ"צ) — molecule
        grants.Add(G(2, 138, ETM));                   // BRDirector (קב"ר) — molecule
        grants.Add(G(5, 138, ETM));                   // Director (מ"מ) — molecule
        grants.Add(G(7, 138, ETM));                   // MoleculeAdmin (מפק"מ) — molecule
        grants.Add(G(8, 138, SAR));                   // Assigner (משבץ) — molecule (role scope is Molecule)
        grants.Add(G(9, 138, ETM));                   // DepartmentLead — expand Department → molecule
        grants.Add(G(10, 138, ETA));                  // AreaAdmin (קב"ב) — area
        grants.Add(G(11, 138, ETP, canGive: true));   // Owner — project
```

- [ ] **Step 3: Update the auto-grant COUNTS in `RoleTemplateAutoGrantTests.cs`**

Grant 138 is on the same 8 templates as 136 → +1 each. Update the `[InlineData]` rows (lines 190-198), leaving Employee unchanged:
```csharp
    [InlineData("Tzafona", "Employee", 21)]      // unchanged (not a Lead-and-above template)
    [InlineData("Tzafona", "Lead", 58)]          // +1 ManageCalendarTabs (138)
    [InlineData("Tzafona", "BRDirector", 71)]    // +1 ManageCalendarTabs
    [InlineData("Tzafona", "Director", 66)]      // +1 ManageCalendarTabs
    [InlineData("Tzafona", "Assigner", 27)]      // +1 ManageCalendarTabs
    [InlineData("Hitazmut", "MoleculeAdmin", 102)]// +1 ManageCalendarTabs
    [InlineData("Yekev", "DepartmentLead", 57)]  // +1 ManageCalendarTabs
    [InlineData("Tzafona", "AreaAdmin", 119)]    // +1 ManageCalendarTabs
    [InlineData("SystemAdmins", "Owner", 130)]   // +1 ManageCalendarTabs
```
Also update the comment above the `[Theory]` (lines 182-189) to note "+ 2026-07-21 ManageCalendarTabs (#138)".

- [ ] **Step 4: Add the grant resx keys (grep first)**

Run `Grep "Grant_ManageCalendarTabs" Resources` → expect no matches. Then add to `Resources/SharedResources.resx`:
- `Grant_ManageCalendarTabs` = `Manage Calendar Tabs`
- `Grant_ManageCalendarTabs_Desc` = `Create, edit, and delete calendar tabs (sub-calendars) for a molecule's job-type calendar.`

And to `Resources/SharedResources.he-IL.resx`:
- `Grant_ManageCalendarTabs` = `ניהול לשוניות לוח`
- `Grant_ManageCalendarTabs_Desc` = `יצירה, עריכה ומחיקה של לשוניות (תת-לוחות) בלוח לפי סוג תפקיד במולקולה.`

- [ ] **Step 5: Build + run the grant auto-grant tests**

Run: `dotnet build -c Release` then `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~RoleTemplateAutoGrantTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: **Build succeeded**; **Passed! - Failed: 0**. If a count is off, re-verify which templates received 138. Then `git checkout -- packages.lock.json`.

- [ ] **Step 6: Commit**

```bash
git add Data/SeedData/GrantTypeSeed.cs Data/SeedData/RoleTemplateSeed.cs ShiftManager.Tests/MasterTests/GrantAuthorization/RoleTemplateAutoGrantTests.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat(tabs): add ManageCalendarTabs grant (138), auto-grant Lead-and-above (Phase D)"
```

**Covers:** SEC-5 (grant appended at seed end, Lead-and-above receive it, `RoleTemplateAutoGrantTests` counts updated), LOC-1 (grant strings in both resx, grepped first). Part of PF13 (grant) and the Grant Change Checklist.

---

## Task 4: Admin page `/Admin/Organization/Tabs` + nav leaf + resx

**Files:**
- Create: `Pages/Admin/Organization/Tabs/Index.cshtml.cs`
- Create: `Pages/Admin/Organization/Tabs/Index.cshtml`
- Modify: `Services/Navigation/NavRegistry.cs` (nav leaf)
- Modify: `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx` (page + nav keys)
- Test: `ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs` (add coverage-helper test — the coverage math lives in a static helper on the page model, tested directly)

**Interfaces:**
- Consumes: `IShiftTabService` (Task 1), `IJobTypeService.GetJobTypesForMoleculeAsync(int moleculeId)`, `IGrantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ManageCalendarTabs")`, `Molecule.Type == MoleculeType.Tech`, `LocalizedPageModel` base, `ICompanyContext`, `IAuditLogService`.
- Produces: page namespace `ShiftManager.Pages.Admin.Organization.Tabs`, route `/Admin/Organization/Tabs`, policy `Grant:ManageCalendarTabs`.

- [ ] **Step 1: Add the nav leaf resx key (grep first)**

Run `Grep "Nav2_Tabs" Resources` → expect no matches. Add to `SharedResources.resx`: `Nav2_Tabs` = `Tabs`. Add to `SharedResources.he-IL.resx`: `Nav2_Tabs` = `לשוניות`.

- [ ] **Step 2: Register the nav leaf in `NavRegistry.cs`**

In the `Nav2_Sched_Definitions` subgroup (the "definitions" family alongside ShiftGroupings/ChoreTypes/DutyTypes, which IS the settings/definitions area in the current IA), add after the `Nav2_DutyTypes` leaf (line 63):
```csharp
                Link("Nav2_Tabs", "/Admin/Organization/Tabs", policy: G("ManageCalendarTabs"), icon: "layers")),
```
Adjust the preceding line's closing so the `Link(... "shield"))` that currently closes the subgroup instead ends with a comma and the new `Nav2_Tabs` line becomes the subgroup's last child. (Concretely: change `Link("Nav2_DutyTypes", "/Admin/Organization/DutyTypes", policy: G("ManageOnDutyTypes"), icon: "shield")),` to end with `icon: "shield"),` and append the `Nav2_Tabs` line above, keeping the `)` that closes `SubGroup`.)

> Nav parity: `NavRegistryPolicyParityTests` asserts the leaf policy is never looser than the page's `[Authorize(Policy)]`. Both are `Grant:ManageCalendarTabs`, so parity holds.

- [ ] **Step 3: Add the page + validation resx keys (grep each first)**

Grep each key in `Resources` before adding (expect no matches). Add to `SharedResources.resx` (EN) and `SharedResources.he-IL.resx` (HE):

| Key | EN | HE |
|-----|----|----|
| `Tabs_PageTitle` | Calendar Tabs | לשוניות לוח |
| `Tabs_PageSubtitle` | Split a molecule's job-type calendar into named sub-calendars. Assign companies and shift types to each tab; empty selections mean no restriction. | פיצול לוח לפי סוג תפקיד במולקולה לתת-לוחות בעלי שם. שיוך דסקים וסוגי משמרת לכל לשונית; בחירה ריקה = ללא הגבלה. |
| `Tabs_Molecule` | Molecule | מולקולה |
| `Tabs_JobType` | Job type | סוג תפקיד |
| `Tabs_JobTypeNone` | (No job type — Tech) | (ללא סוג תפקיד — טכני) |
| `Tabs_AddTab` | Add tab | הוספת לשונית |
| `Tabs_NameEn` | Name (English) | שם (אנגלית) |
| `Tabs_NameHe` | Name (Hebrew) | שם (עברית) |
| `Tabs_Color` | Color | צבע |
| `Tabs_Companies` | Companies | דסקים |
| `Tabs_ShiftTypes` | Shift types | סוגי משמרת |
| `Tabs_PrioritizeCompanyUsers` | Prioritize users from these companies when assigning | תעדוף משתמשים מהדסקים האלה בשיבוץ |
| `Tabs_EmptyMeansAll` | Nothing selected = no restriction (all companies / all shift types). | ללא בחירה = ללא הגבלה (כל הדסקים / כל סוגי המשמרת). |
| `Tabs_Save` | Save | שמירה |
| `Tabs_Delete` | Delete | מחיקה |
| `Tabs_Edit` | Edit | עריכה |
| `Tabs_NoTabs` | No tabs yet for this job type. Add one above. | אין עדיין לשוניות לסוג תפקיד זה. הוסיפו למעלה. |
| `Tabs_Error_NamesRequired` | Both an English and a Hebrew name are required. | נדרש שם באנגלית ובעברית. |
| `Tabs_Error_NameExists` | A tab with that English or Hebrew name already exists for this job type. | כבר קיימת לשונית עם שם זהה (אנגלית או עברית) לסוג תפקיד זה. |
| `Tabs_Error_MoleculeRequired` | Select a molecule first. | יש לבחור מולקולה תחילה. |
| `Tabs_Error_NotAuthorized` | You don't have permission to manage tabs for this molecule. | אין לך הרשאה לנהל לשוניות במולקולה זו. |
| `Tabs_Error_InvalidSelection` | One or more selected companies or shift types are outside this molecule's scope. | דסק או סוג משמרת שנבחרו הם מחוץ למולקולה זו. |
| `Tabs_Success_Created` | Tab '{0}' created. | הלשונית '{0}' נוצרה. |
| `Tabs_Success_Updated` | Tab '{0}' updated. | הלשונית '{0}' עודכנה. |
| `Tabs_Success_Deleted` | Tab '{0}' deleted. | הלשונית '{0}' נמחקה. |
| `Tabs_DeleteConfirm` | Delete '{0}'? It has {1} shift type(s) and {2} company/ies assigned, and is the remembered tab of {3} user(s). | למחוק את '{0}'? משויכים אליה {1} סוגי משמרת ו-{2} דסקים, והיא הלשונית השמורה של {3} משתמשים. |
| `Tabs_CoverageWarning` | {0} shift type(s) are in no tab and will appear only on the "All" view: {1} | {0} סוגי משמרת אינם באף לשונית ויופיעו רק בתצוגת "הכול": {1} |

- [ ] **Step 4: Write a failing test for the coverage helper**

Add to `ShiftTabServiceTests.cs` (the coverage math is a pure static helper on the page model; test it directly). Append:
```csharp
    [Fact]
    public void Coverage_Lists_ShiftTypes_In_No_Tab_And_Ignores_EmptyTabs()
    {
        // allShiftTypeIds = {1,2,3}; tabA covers {1}; tabB is empty (empty = all → nothing uncovered).
        var allIds = new[] { 1, 2, 3 };
        var tabSets = new List<HashSet<int>> { new() { 1 } };            // one configured tab
        var uncovered = ShiftManager.Pages.Admin.Organization.Tabs.IndexModel.ComputeUncovered(allIds, tabSets);
        uncovered.Should().BeEquivalentTo(new[] { 2, 3 });

        // If ANY tab is empty (= all), coverage is complete → no warning.
        var withEmpty = new List<HashSet<int>> { new() { 1 }, new HashSet<int>() };
        ShiftManager.Pages.Admin.Organization.Tabs.IndexModel.ComputeUncovered(allIds, withEmpty)
            .Should().BeEmpty();
    }
```
Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ShiftTabServiceTests.Coverage_Lists" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1` → **build failure** (`IndexModel`/`ComputeUncovered` don't exist yet).

- [ ] **Step 5: Create `Pages/Admin/Organization/Tabs/Index.cshtml.cs`**

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.Tabs;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageCalendarTabs;
// tab config is molecule-scoped; every mutating handler re-verifies the target molecule (and that the
// jobtype/companies/shift types are in scope) via IsUserAuthorizedForMoleculeAsync + the service guards (IDOR).
[Authorize(Policy = "Grant:ManageCalendarTabs")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IShiftTabService _tabService;
    private readonly IJobTypeService _jobTypeService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IShiftTabService tabService,
        IJobTypeService jobTypeService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _tabService = tabService;
        _jobTypeService = jobTypeService;
        _grantService = grantService;
        _companyContext = companyContext;
        _auditLogService = auditLogService;
    }

    // View models
    public record MoleculeOption(int Id, string Name);
    public record JobTypeOption(int Id, string Name);
    public record CompanyOption(int Id, string Name);
    public record ShiftTypeOption(int Id, string Name);
    public record TabVM(int Id, string NameEn, string NameHe, string? Color, bool PrioritizeCompanyUsers,
        HashSet<int> CompanyIds, HashSet<int> ShiftTypeIds, int ShiftTypeCount, int CompanyCount, int RememberedByCount);

    // Selection
    [BindProperty(SupportsGet = true)] public int? MoleculeId { get; set; }
    [BindProperty(SupportsGet = true)] public int? JobTypeId { get; set; }

    public List<MoleculeOption> AvailableMolecules { get; set; } = new();
    public List<JobTypeOption> AvailableJobTypes { get; set; } = new();
    public bool IsTechMolecule { get; set; }
    public List<CompanyOption> MoleculeCompanies { get; set; } = new();
    public List<ShiftTypeOption> ScopeShiftTypes { get; set; } = new();
    public List<TabVM> Tabs { get; set; } = new();
    public List<string> UncoveredShiftTypeNames { get; set; } = new();

    // Create/edit form
    [BindProperty] public int EditTabId { get; set; }          // 0 = create
    [BindProperty] public string NameEn { get; set; } = string.Empty;
    [BindProperty] public string NameHe { get; set; } = string.Empty;
    [BindProperty] public string? Color { get; set; }
    [BindProperty] public bool PrioritizeCompanyUsers { get; set; } = true;
    [BindProperty] public List<int> SelectedCompanyIds { get; set; } = new();
    [BindProperty] public List<int> SelectedShiftTypeIds { get; set; } = new();

    public async Task OnGetAsync() => await LoadDataAsync();

    private async Task LoadDataAsync()
    {
        if (!TryGetUserId(out var userId)) return;

        // Accessible molecules = grant scope ∪ own molecule (mirrors ChoreTypes).
        var moleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ManageCalendarTabs"));
        var ownMoleculeId = await GetOwnMoleculeIdAsync();
        if (ownMoleculeId.HasValue) moleculeIds.Add(ownMoleculeId.Value);

        // SECURITY-AUDITED: SAFE — filtered to molecules the user has ManageCalendarTabs access to.
        AvailableMolecules = await _db.Molecules.IgnoreQueryFilters()
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName))
            .ToListAsync();

        // Auto-select the sole accessible molecule, or default to own; validate the current pick.
        if (!MoleculeId.HasValue)
            MoleculeId = AvailableMolecules.Count == 1 ? AvailableMolecules[0].Id : ownMoleculeId;
        if (MoleculeId.HasValue && !AvailableMolecules.Any(m => m.Id == MoleculeId.Value))
            MoleculeId = null;
        if (!MoleculeId.HasValue) return;

        var molecule = await _db.Molecules.IgnoreQueryFilters().FirstAsync(m => m.Id == MoleculeId.Value);
        IsTechMolecule = molecule.Type == MoleculeType.Tech;

        // Job types for the molecule (Tech → none; JobTypeId stays null).
        if (!IsTechMolecule)
        {
            AvailableJobTypes = (await _jobTypeService.GetJobTypesForMoleculeAsync(MoleculeId.Value))
                .Select(jt => new JobTypeOption(jt.Id, jt.DisplayName ?? jt.Name)).ToList();
            if (!JobTypeId.HasValue)
                JobTypeId = AvailableJobTypes.Count == 1 ? AvailableJobTypes[0].Id : (int?)null;
            if (JobTypeId.HasValue && !AvailableJobTypes.Any(jt => jt.Id == JobTypeId.Value))
                JobTypeId = null;
            if (!JobTypeId.HasValue) return; // wait for the user to pick a job type
        }
        else
        {
            JobTypeId = null;
        }

        // Companies of the molecule (checklist source).
        MoleculeCompanies = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == MoleculeId.Value)
            .OrderBy(c => c.Name)
            .Select(c => new CompanyOption(c.Id, c.LocalizedName))
            .ToListAsync();

        // Shift types in the molecule + jobtype scope (jobtype match or null) — checklist source + coverage.
        var scopeShiftTypes = await _db.ShiftTypes.IgnoreQueryFilters()
            .Where(st => st.MoleculeId == MoleculeId.Value && (st.JobTypeId == JobTypeId || st.JobTypeId == null))
            .OrderBy(st => st.Start)
            .ToListAsync();
        ScopeShiftTypes = scopeShiftTypes
            .Select(st => new ShiftTypeOption(st.Id, string.IsNullOrWhiteSpace(st.NameEn) ? st.Key : st.NameEn!))
            .ToList();

        // Tabs for (molecule, jobtype) with their membership + usage + remembered-by counts.
        var tabs = await _tabService.GetTabsForMoleculeAsync(MoleculeId.Value, JobTypeId, includeInactive: true);
        var tabVms = new List<TabVM>(tabs.Count);
        var coverageSets = new List<HashSet<int>>();
        foreach (var t in tabs)
        {
            var companyIds = await _tabService.GetCompanyIdsForTabAsync(t.Id);
            var shiftTypeIds = await _tabService.GetShiftTypeIdsForTabAsync(t.Id);
            var (stCount, coCount) = await _tabService.GetUsageAsync(t.Id);
            var remembered = await _db.UserShiftTabPreferences.CountAsync(p => p.TabId == t.Id);
            tabVms.Add(new TabVM(t.Id, t.NameEn, t.NameHe, t.Color, t.PrioritizeCompanyUsers,
                companyIds, shiftTypeIds, stCount, coCount, remembered));
            coverageSets.Add(shiftTypeIds);
        }
        Tabs = tabVms;

        // Advisory coverage warning: shift types in no tab (empty tab = all → nothing uncovered).
        var uncovered = ComputeUncovered(scopeShiftTypes.Select(st => st.Id), coverageSets);
        UncoveredShiftTypeNames = ScopeShiftTypes.Where(o => uncovered.Contains(o.Id)).Select(o => o.Name).ToList();
    }

    /// <summary>Shift-type ids covered by NO tab. An empty tab means "all" → coverage complete.</summary>
    public static HashSet<int> ComputeUncovered(IEnumerable<int> allShiftTypeIds, List<HashSet<int>> tabShiftTypeSets)
    {
        var all = allShiftTypeIds.ToHashSet();
        if (tabShiftTypeSets.Any(s => s.Count == 0)) return new HashSet<int>();  // some tab = "all"
        var covered = new HashSet<int>();
        foreach (var s in tabShiftTypeSets) covered.UnionWith(s);
        all.ExceptWith(covered);
        return all;
    }

    // ─────────────────────────── CRUD handlers ───────────────────────────

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        if (!MoleculeId.HasValue) { TempError("Tabs_Error_MoleculeRequired"); return Redirect(); }
        if (!await IsUserAuthorizedForMoleculeAsync(MoleculeId.Value)) { TempError("Tabs_Error_NotAuthorized"); return Redirect(); }
        if (!await IsJobTypeInScopeAsync(MoleculeId.Value, JobTypeId)) { TempError("Tabs_Error_InvalidSelection"); return Redirect(); }
        if (string.IsNullOrWhiteSpace(NameEn) || string.IsNullOrWhiteSpace(NameHe)) { TempError("Tabs_Error_NamesRequired"); return Redirect(); }

        var created = await _tabService.CreateAsync(MoleculeId.Value, JobTypeId, NameEn.Trim(), NameHe.Trim(),
            ColorUtilities.SanitizeHexColor(Color), userId);
        if (created == null) { TempError("Tabs_Error_NameExists"); return Redirect(); }

        // Priority default is ON at create; apply the submitted membership (IDOR-guarded in the service).
        if (!created.PrioritizeCompanyUsers.Equals(PrioritizeCompanyUsers))
            await _tabService.RenameAsync(created.Id, created.NameEn, created.NameHe, created.Color, PrioritizeCompanyUsers);
        if (!await _tabService.SetCompaniesForTabAsync(created.Id, SelectedCompanyIds)
            || !await _tabService.SetShiftTypesForTabAsync(created.Id, SelectedShiftTypeIds))
        {
            await _tabService.DeleteAsync(created.Id); // roll back a half-made tab
            TempError("Tabs_Error_InvalidSelection");
            return Redirect();
        }

        await _auditLogService.LogAsync("ShiftTabCreated", "ShiftTab", created.Id,
            $"Created tab '{created.NameEn}' in molecule {MoleculeId.Value}, jobtype {JobTypeId?.ToString() ?? "none"}.");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Tabs_Success_Created"], created.NameEn);
        return Redirect();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        var tab = await _tabService.GetTabAsync(EditTabId);
        if (tab == null || !await IsUserAuthorizedForMoleculeAsync(tab.MoleculeId)) { TempError("Tabs_Error_NotAuthorized"); return Redirect(); }
        if (string.IsNullOrWhiteSpace(NameEn) || string.IsNullOrWhiteSpace(NameHe)) { TempError("Tabs_Error_NamesRequired"); return Redirect(); }

        var renamed = await _tabService.RenameAsync(tab.Id, NameEn.Trim(), NameHe.Trim(),
            ColorUtilities.SanitizeHexColor(Color), PrioritizeCompanyUsers);
        if (!renamed) { TempError("Tabs_Error_NameExists"); return Redirect(); }

        if (!await _tabService.SetCompaniesForTabAsync(tab.Id, SelectedCompanyIds)
            || !await _tabService.SetShiftTypesForTabAsync(tab.Id, SelectedShiftTypeIds))
        { TempError("Tabs_Error_InvalidSelection"); return Redirect(); }

        await _auditLogService.LogAsync("ShiftTabUpdated", "ShiftTab", tab.Id, $"Updated tab '{NameEn.Trim()}'.");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Tabs_Success_Updated"], NameEn.Trim());
        return Redirect();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int tabId)
    {
        if (!TryGetUserId(out var userId)) return Forbid();
        var tab = await _tabService.GetTabAsync(tabId);
        if (tab == null || !await IsUserAuthorizedForMoleculeAsync(tab.MoleculeId)) { TempError("Tabs_Error_NotAuthorized"); return Redirect(); }

        var nameEn = tab.NameEn;
        await _tabService.DeleteAsync(tabId);
        await _auditLogService.LogAsync("ShiftTabDeleted", "ShiftTab", tabId, $"Deleted tab '{nameEn}'.");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Tabs_Success_Deleted"], nameEn);
        return Redirect();
    }

    /// <summary>AJAX delete-impact preview (mirrors Blueprints' OnGetCheckTabUsage pattern).</summary>
    public async Task<IActionResult> OnGetCheckTabUsageAsync(int tabId)
    {
        if (!TryGetUserId(out _)) return new JsonResult(new { ok = false }) { StatusCode = 403 };
        var tab = await _tabService.GetTabAsync(tabId);
        if (tab == null || !await IsUserAuthorizedForMoleculeAsync(tab.MoleculeId))
            return new JsonResult(new { ok = false }) { StatusCode = 403 };
        var (shiftTypeCount, companyCount) = await _tabService.GetUsageAsync(tabId);
        var rememberedBy = await _db.UserShiftTabPreferences.CountAsync(p => p.TabId == tabId);
        return new JsonResult(new
        {
            ok = true,
            message = string.Format(CultureInfo.CurrentCulture, _localizer["Tabs_DeleteConfirm"],
                tab.NameEn, shiftTypeCount, companyCount, rememberedBy)
        });
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private bool TryGetUserId(out int userId)
        => int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out userId);

    private async Task<int?> GetOwnMoleculeIdAsync()
    {
        var companyId = _companyContext.CompanyId;
        if (!companyId.HasValue) return null;
        return await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == companyId.Value).Select(c => c.MoleculeId).FirstOrDefaultAsync();
    }

    /// <summary>The current user has ManageCalendarTabs grant access to the molecule (∪ own molecule).</summary>
    private async Task<bool> IsUserAuthorizedForMoleculeAsync(int moleculeId)
    {
        if (!TryGetUserId(out var userId)) return false;
        var accessible = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ManageCalendarTabs"));
        var own = await GetOwnMoleculeIdAsync();
        if (own.HasValue) accessible.Add(own.Value);
        return accessible.Contains(moleculeId);
    }

    /// <summary>The jobtype is valid for the molecule: null (Tech) or it owns shift types in the molecule.</summary>
    private async Task<bool> IsJobTypeInScopeAsync(int moleculeId, int? jobTypeId)
    {
        if (jobTypeId == null) return true; // Tech / null-jobtype calendar
        return await _db.ShiftTypes.IgnoreQueryFilters()
            .AnyAsync(st => st.MoleculeId == moleculeId && st.JobTypeId == jobTypeId.Value);
    }

    private void TempError(string key) => TempData["ErrorMessage"] = _localizer[key].Value;
    private IActionResult Redirect() => RedirectToPage(new { MoleculeId, JobTypeId });
}
```

- [ ] **Step 6: Create `Pages/Admin/Organization/Tabs/Index.cshtml`**

```html
@page
@model ShiftManager.Pages.Admin.Organization.Tabs.IndexModel
@{
    ViewData["Title"] = Localizer["Tabs_PageTitle"].Value;
}

<div class="page-header">
    <h1><icon name="layers" /> <loc key="Tabs_PageTitle" /></h1>
    <p class="page-subtitle"><loc key="Tabs_PageSubtitle" /></p>
</div>

@if (TempData["SuccessMessage"] != null)
{
    <div class="alert alert-success">@TempData["SuccessMessage"]</div>
}
@if (TempData["ErrorMessage"] != null)
{
    <div class="alert alert-danger">@TempData["ErrorMessage"]</div>
}

@* Molecule + job-type selectors (GET-round-trip; auto-selected when only one is accessible) *@
<form method="get" class="feature-category">
    <div class="form-row" style="display:flex; gap:16px; flex-wrap:wrap; align-items:flex-end;">
        <div class="form-group">
            <label asp-for="MoleculeId"><loc key="Tabs_Molecule" /></label>
            <select asp-for="MoleculeId" class="form-control" onchange="this.form.submit()" data-searchable>
                <option value=""></option>
                @foreach (var m in Model.AvailableMolecules)
                {
                    <option value="@m.Id" selected="@(Model.MoleculeId == m.Id)">@m.Name</option>
                }
            </select>
        </div>
        @if (Model.MoleculeId.HasValue && !Model.IsTechMolecule)
        {
            <div class="form-group">
                <label asp-for="JobTypeId"><loc key="Tabs_JobType" /></label>
                <select asp-for="JobTypeId" class="form-control" onchange="this.form.submit()" data-searchable>
                    <option value=""></option>
                    @foreach (var jt in Model.AvailableJobTypes)
                    {
                        <option value="@jt.Id" selected="@(Model.JobTypeId == jt.Id)">@jt.Name</option>
                    }
                </select>
            </div>
        }
        else if (Model.MoleculeId.HasValue && Model.IsTechMolecule)
        {
            <div class="form-group"><span class="text-muted"><loc key="Tabs_JobTypeNone" /></span></div>
        }
    </div>
</form>

@{ var scopeReady = Model.MoleculeId.HasValue && (Model.IsTechMolecule || Model.JobTypeId.HasValue); }
@if (scopeReady)
{
    @if (Model.UncoveredShiftTypeNames.Any())
    {
        <div class="alert alert-warning">
            @string.Format(Localizer["Tabs_CoverageWarning"].Value, Model.UncoveredShiftTypeNames.Count,
                string.Join(", ", Model.UncoveredShiftTypeNames))
        </div>
    }

    @* Existing tabs *@
    <div class="feature-category">
        @if (Model.Tabs.Any())
        {
            <table class="table table-hover">
                <thead>
                    <tr>
                        <th><loc key="Tabs_NameEn" /> / <loc key="Tabs_NameHe" /></th>
                        <th><loc key="Tabs_Companies" /></th>
                        <th><loc key="Tabs_ShiftTypes" /></th>
                        <th><loc key="Tabs_PrioritizeCompanyUsers" /></th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var t in Model.Tabs)
                    {
                        <tr>
                            <td>
                                <span style="display:inline-block; width:12px; height:12px; border-radius:50%; margin-inline-end:6px; vertical-align:middle; background:@(string.IsNullOrWhiteSpace(t.Color) ? "#888" : t.Color);"></span>
                                <bdi>@t.NameEn</bdi> / <bdi>@t.NameHe</bdi>
                            </td>
                            <td><small class="text-muted">@(t.CompanyCount == 0 ? Localizer["Tabs_EmptyMeansAll"].Value : t.CompanyCount.ToString())</small></td>
                            <td><small class="text-muted">@(t.ShiftTypeCount == 0 ? Localizer["Tabs_EmptyMeansAll"].Value : t.ShiftTypeCount.ToString())</small></td>
                            <td>@(t.PrioritizeCompanyUsers ? "✓" : "—")</td>
                            <td>
                                <button type="button" class="btn btn-sm btn-secondary js-edit-tab"
                                        data-id="@t.Id" data-name-en="@t.NameEn" data-name-he="@t.NameHe"
                                        data-color="@t.Color" data-priority="@t.PrioritizeCompanyUsers.ToString().ToLowerInvariant()"
                                        data-company-ids="@string.Join(",", t.CompanyIds)"
                                        data-shift-type-ids="@string.Join(",", t.ShiftTypeIds)">
                                    <loc key="Tabs_Edit" />
                                </button>
                                <button type="button" class="btn btn-sm btn-danger js-delete-tab" data-id="@t.Id">
                                    <loc key="Tabs_Delete" />
                                </button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }
        else
        {
            <div class="alert alert-info"><loc key="Tabs_NoTabs" /></div>
        }
    </div>

    @* Create / edit form (JS toggles handler + fills fields for edit) *@
    <form method="post" id="tabForm" class="feature-category" asp-page-handler="Create">
        <input type="hidden" asp-for="MoleculeId" />
        <input type="hidden" asp-for="JobTypeId" />
        <input type="hidden" asp-for="EditTabId" />
        <h2 id="tabFormTitle"><icon name="plus" /> <loc key="Tabs_AddTab" /></h2>
        <div class="form-row" style="display:flex; gap:16px; flex-wrap:wrap;">
            <div class="form-group"><label asp-for="NameEn"><loc key="Tabs_NameEn" /></label>
                <input asp-for="NameEn" class="form-control" maxlength="100" autocomplete="off" /></div>
            <div class="form-group"><label asp-for="NameHe"><loc key="Tabs_NameHe" /></label>
                <input asp-for="NameHe" class="form-control" maxlength="100" autocomplete="off" dir="rtl" /></div>
            <div class="form-group"><label asp-for="Color"><loc key="Tabs_Color" /></label>
                <input asp-for="Color" type="color" class="form-control" value="#3B82F6" style="width:56px; padding:2px;" /></div>
        </div>
        <div class="form-group">
            <label class="checkbox">
                <input asp-for="PrioritizeCompanyUsers" type="checkbox" /> <loc key="Tabs_PrioritizeCompanyUsers" />
            </label>
        </div>
        <p class="text-muted"><small><loc key="Tabs_EmptyMeansAll" /></small></p>
        <div class="form-row" style="display:flex; gap:32px; flex-wrap:wrap;">
            <fieldset class="form-group">
                <legend><loc key="Tabs_Companies" /></legend>
                @foreach (var c in Model.MoleculeCompanies)
                {
                    <label class="checkbox" style="display:block;">
                        <input type="checkbox" name="SelectedCompanyIds" value="@c.Id" class="js-company-chk" /> @c.Name
                    </label>
                }
            </fieldset>
            <fieldset class="form-group">
                <legend><loc key="Tabs_ShiftTypes" /></legend>
                @foreach (var st in Model.ScopeShiftTypes)
                {
                    <label class="checkbox" style="display:block;">
                        <input type="checkbox" name="SelectedShiftTypeIds" value="@st.Id" class="js-shifttype-chk" /> @st.Name
                    </label>
                }
            </fieldset>
        </div>
        <button type="submit" class="btn btn-primary"><loc key="Tabs_Save" /></button>
    </form>

    @* Hidden delete form (confirmed via the impact-preview fetch) *@
    <form method="post" id="deleteTabForm" asp-page-handler="Delete" style="display:none;">
        <input type="hidden" asp-for="MoleculeId" />
        <input type="hidden" asp-for="JobTypeId" />
        <input type="hidden" name="tabId" id="deleteTabId" />
    </form>
}

@section Scripts {
<script>
(function () {
    // Edit → switch the form to Update and pre-fill fields + checklists.
    document.querySelectorAll('.js-edit-tab').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var f = document.getElementById('tabForm');
            f.querySelector('[name="EditTabId"]').value = btn.dataset.id;
            f.setAttribute('asp-page-handler', 'Update');
            f.action = f.action.replace(/handler=Create/, 'handler=Update');
            if (!/handler=/.test(f.action)) { f.action += (f.action.indexOf('?') < 0 ? '?' : '&') + 'handler=Update'; }
            f.querySelector('[name="NameEn"]').value = btn.dataset.nameEn || '';
            f.querySelector('[name="NameHe"]').value = btn.dataset.nameHe || '';
            f.querySelector('[name="Color"]').value = btn.dataset.color || '#3B82F6';
            f.querySelector('[name="PrioritizeCompanyUsers"]').checked = (btn.dataset.priority === 'true');
            var cids = (btn.dataset.companyIds || '').split(',').filter(Boolean);
            var sids = (btn.dataset.shiftTypeIds || '').split(',').filter(Boolean);
            f.querySelectorAll('.js-company-chk').forEach(function (c) { c.checked = cids.indexOf(c.value) >= 0; });
            f.querySelectorAll('.js-shifttype-chk').forEach(function (c) { c.checked = sids.indexOf(c.value) >= 0; });
            document.getElementById('tabFormTitle').textContent = btn.dataset.nameEn;
            f.scrollIntoView({ behavior: 'smooth' });
        });
    });
    // Delete → fetch impact preview then confirm.
    document.querySelectorAll('.js-delete-tab').forEach(function (btn) {
        btn.addEventListener('click', async function () {
            var id = btn.dataset.id;
            try {
                var res = await fetch('?handler=CheckTabUsage&tabId=' + encodeURIComponent(id), { credentials: 'same-origin' });
                var data = await res.json();
                if (!data.ok) { return; }
                if (window.confirm(data.message)) {
                    document.getElementById('deleteTabId').value = id;
                    document.getElementById('deleteTabForm').submit();
                }
            } catch (e) { /* network — do nothing */ }
        });
    });
})();
</script>
}
```

- [ ] **Step 7: Build + run the coverage test + full targeted suite**

Run: `dotnet build -c Release`
Expected: **Build succeeded**. Then:
Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ShiftTabServiceTests|FullyQualifiedName~NavRegistry" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: **Passed! - Failed: 0** (service + coverage + nav parity tests). Then `git checkout -- packages.lock.json`.

- [ ] **Step 8: Commit**

```bash
git add Pages/Admin/Organization/Tabs/ Services/Navigation/NavRegistry.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs
git commit -m "feat(tabs): /Admin/Organization/Tabs management page + nav leaf + resx (Phase D)"
```

**Covers:** PF13 + ADM-1 (nav leaf EN "Tabs"/HE "לשוניות" under Settings/Definitions, molecule+jobtype auto-select), ADM-2 (Tech → no jobtype step, JobTypeId=null), ADM-3 (missing NameEn/NameHe → localized error), ADM-4 (dup over NameEn OR NameHe → friendly error), ADM-5 (empty = all inline helper), ADM-6 (coverage warning naming uncovered shift types), ADM-7 (delete confirm + impact preview via `OnGetCheckTabUsageAsync`), SEC-1 (policy-gated page + nav), SEC-2 (per-handler IDOR: molecule + jobtype-in-scope + companies/shift-types-in-scope via service guards), SEC-4 (foreign-molecule tab ids rejected by `IsUserAuthorizedForMoleculeAsync`), LOC-1/LOC-3 (all strings in both resx, RTL-mirrored form with `dir`/`bdi`). Audit logging on create/update/delete.

---

## Task 5: Seed reset — example Oren/Alhut tabs + trainee JobTypeId backfill

**Files:**
- Create: `Data/SeedData/ShiftTabSeed.cs`
- Modify: `Program.cs` (invoke the seeder after `BulkTestUserSeed`)
- Modify: `Data/SeedData/BulkTestUserSeed.cs` (targeted trainee JobTypeId backfill)

**Interfaces:**
- Consumes: `IShiftTabService` (or `AppDbContext` directly), resolved Oren molecule + Alhut job type + companies (Tzafona/City/Camps/Hir/Radio) from `ShiftyOrganizationSeed`/`BulkTestUserSeed`.
- Produces: dev-only example tabs Geo `{Tzafona, City, Camps, Hir}` + Tacti `{Radio}` for `(Oren, Alhut)`; existing null-jobtype trainees back-filled to their company's primary job type.

- [ ] **Step 1: Add the trainee JobTypeId backfill to `BulkTestUserSeed.cs`**

The skip-if-exists seeder leaves already-seeded users untouched, so trainees seeded before this feature keep a null `JobTypeId` and would be hidden by the Phase E trainee picker. Add a targeted backfill. In `SeedAsync`, after the `EnsureMembershipsAsync(...)` call (~line 199), insert:
```csharp
        var traineesFixed = await EnsureTraineeJobTypesAsync(db, companyMap, alhut, projectManager, logger);
```
And add the method near `EnsureQaMoleculeShiftTypesAsync`:
```csharp
    /// <summary>
    /// Backfill: existing trainees (@test) with a null JobTypeId get their company's primary job type
    /// (Alhut for workforce companies, ProjectManager for tech). The skip-if-exists seeder won't touch
    /// already-seeded users, so this closes the origin-bug gap where a null-jobtype trainee is hidden.
    /// </summary>
    private static async Task<int> EnsureTraineeJobTypesAsync(
        AppDbContext db, Dictionary<string, Company> companies, JobType alhut, JobType projectManager, ILogger logger)
    {
        var techCompanyIds = TechCompanies.Where(companies.ContainsKey).Select(n => companies[n].Id).ToHashSet();
        var trainees = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Role == UserRole.Trainee && u.JobTypeId == null && u.Email.EndsWith("@test"))
            .ToListAsync();
        foreach (var t in trainees)
            t.JobTypeId = techCompanyIds.Contains(t.CompanyId) ? projectManager.Id : alhut.Id;
        if (trainees.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("[BulkTestUserSeed] Backfilled JobTypeId on {Count} trainees", trainees.Count);
        }
        return trainees.Count;
    }
```
Update the final `logger.LogInformation` summary line to include `, trainees backfilled: {Trainees}` and pass `traineesFixed`.

- [ ] **Step 2: Create the example-tab seeder `Data/SeedData/ShiftTabSeed.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Dev/Test-only example calendar tabs (לשונית) for (Oren, Alhut): "Geo" {Tzafona, City, Camps, Hir} and
/// "Tacti" {Radio}. Idempotent — skips a tab whose (molecule, jobtype, NameEn) already exists. Shift-type
/// membership is left empty (= all Alhut shift types) so the example reads cleanly.
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — dev-only test data, guarded by IsTestEnvironment().
public static class ShiftTabSeed
{
    private static readonly (string NameEn, string NameHe, string[] Companies)[] OrenAlhutTabs =
    {
        ("Geo", "גאו", new[] { "Tzafona", "City", "Camps", "Hir" }),
        ("Tacti", "טקטי", new[] { "Radio" })
    };

    public static async Task SeedAsync(AppDbContext db, IShiftTabService tabService, ILogger logger)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (env != "Development" && env != "Test") return;

        var oren = await db.Molecules.FirstOrDefaultAsync(m => m.Name == "Oren");
        var alhut = await db.JobTypes.FirstOrDefaultAsync(j => j.Name == "Alhut" && j.MoleculeId == null);
        if (oren == null || alhut == null)
        {
            logger.LogInformation("[ShiftTabSeed] Skipping — Oren molecule or Alhut job type not found");
            return;
        }

        var companyByName = await db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == oren.Id)
            .ToDictionaryAsync(c => c.Name, c => c.Id);

        int created = 0;
        foreach (var (nameEn, nameHe, companyNames) in OrenAlhutTabs)
        {
            var exists = await db.ShiftTabs.AnyAsync(t => t.MoleculeId == oren.Id && t.JobTypeId == alhut.Id && t.NameEn == nameEn);
            if (exists) continue;

            var tab = await tabService.CreateAsync(oren.Id, alhut.Id, nameEn, nameHe);
            if (tab == null) continue;
            var ids = companyNames.Where(companyByName.ContainsKey).Select(n => companyByName[n]).ToList();
            await tabService.SetCompaniesForTabAsync(tab.Id, ids);
            created++;
        }

        if (created > 0)
            logger.LogInformation("[ShiftTabSeed] Created {Count} example (Oren, Alhut) tabs", created);
    }
}
```

- [ ] **Step 3: Invoke the seeder in `Program.cs`**

After the `BulkTestUserSeed.SeedAsync(...)` call (~line 1557), add:
```csharp
        var tabService = scope.ServiceProvider.GetRequiredService<ShiftManager.Services.IShiftTabService>();
        await ShiftManager.Data.SeedData.ShiftTabSeed.SeedAsync(db, tabService, logger);
```
(Use the same `scope`/`db`/`logger` locals in scope at the `BulkTestUserSeed` call site; if `IShiftTabService` is already resolvable there, reuse it.)

- [ ] **Step 4: Build + run the full suite serialized (the phase gate)**

Run: `dotnet build -c Release` then
`dotnet test -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: **Build succeeded**; whole suite **Failed: 0**. Then `git checkout -- packages.lock.json`.
(If any pre-existing test references `ShiftType.TabId`, `ReorderTabsAsync`, the old `ShiftTab.Name`/`DisplayName`, or a 2-arg `GetCompanyIdsForTabAsync`/`GetTabsForMoleculeAsync` — e.g. `DraftOverlayRenderTests`, `ShiftsUserRowShiftCountTests` — fix it to the new contract as part of this gate, per REG-5.)

- [ ] **Step 5: Commit**

```bash
git add Data/SeedData/ShiftTabSeed.cs Data/SeedData/BulkTestUserSeed.cs Program.cs
git commit -m "feat(tabs): seed example (Oren,Alhut) tabs + backfill trainee job types (Phase D)"
```

**Covers:** PF6 (seed reset + trainee JobTypeId backfill), ORG-8 (previously-seeded trainees no longer hidden after migrate-in-place), REG-5 (full suite green serialized; any reshaped-service test callers fixed).

---

## Self-Review (spec §2.1/2.2/2.6/11 + checklist ADM-1..9 / SEC-1..5 / LOC-1/3 / REG-5/6 + PF6/11/13/1)

**Spec coverage:**
- **§2.1 data model** → Task 1 (entities + EF) + Task 2 (migration). All fields present: `JobTypeId`, `NameEn`, `NameHe`, `PrioritizeCompanyUsers`, audit (`CreatedByUserId`, `UpdatedAt`); `ShiftTabCompany` composite PK (dropped `UNIQUE(CompanyId)`); NEW `ShiftTabShiftType`; dropped `ShiftType.TabId`; `UserShiftTabPreference.JobTypeId` + `(UserId,MoleculeId,JobTypeId)` unique. ✔
- **§2.2 management page** → Task 4. Route/namespace, `[Authorize(Policy="Grant:ManageCalendarTabs")]`, per-handler IDOR, molecule→jobtype selection w/ auto-select, Add/Delete/EditName/EditSettings, companies + shift-types checklists + PrioritizeCompanyUsers toggle. No drag-drop. ✔
- **§2.6 grant** → Task 3, append-only id 138, Category=Shift, DefaultScope=Molecule, Lead-and-above auto-grant, test counts. ✔
- **§11 PF6/PF11/PF13** → Tasks 1/2 (PF6), Task 1 (PF11 service ripple + all callers), Task 4 (PF13). **PF1** (remove Blueprints editor + `ReorderTabsAsync`) → Task 1 (folded in — see deviation note). ✔
- **§11.2 resolved decisions** → "All" pseudo-tab is view-only/no-DB-row (nothing stored; the synthetic view is Phase E — this plan removes "Main" and stores no "All"), UD3 default PrioritizeCompanyUsers=ON (`CreateAsync`), Blueprints editor removed (UD5). ✔

**Checklist:** ADM-1..9 ✔ (Task 4, ADM-9 via Task 1). SEC-1/2/4/5 ✔ (SEC-3 — a `ManagerHomeAccess`-only user can no longer edit tabs — follows from removing the Blueprints editor + the new page requiring `ManageCalendarTabs`; verified by browser E2E in Phase-D check + SEC-1 page-deny). LOC-1/LOC-3 ✔. REG-5/REG-6 ✔.

**Placeholder scan:** none — every code step has full code or an anchored before→after edit; deletions name exact members/line anchors.

**Type consistency:** service method names + signatures are identical across the interface (Step 9), impl (Step 10), tests (Step 1, Task 4 Step 4), admin page (Task 4 Step 5), and seed (Task 5). `ComputeUncovered` signature matches between test and page. Grant id 138 consistent across GrantTypeSeed/RoleTemplateSeed/tests.

**Deviations / disclosures (also relayed to team-lead):**
1. **Task ordering — Blueprints removal (PF1) is folded into Task 1, not last.** Dropping `ShiftType.TabId` + changing the service signatures breaks the Blueprints handlers' compilation, and the test project references the web project, so the solution cannot build (hence no task can go green) until the Blueprints tab editor is removed in the same task. Correctness (green build between tasks) over the suggested ordering.
2. **Calendar readers (`Shifts.cshtml.cs`, `GetShiftsData.cshtml.cs`) are bridged, not fully rewired.** Full PF4/PF5/PF10 view-filtering + prioritization + last-tab UX is **Phase E**; Task 1 makes them compile against the reshaped schema with a safe "All" (no-filter) interim, each site marked `// Phase E`. This is a disclosed phase boundary, not a workaround.
3. **Nav placement:** the spec says "Settings subgroup"; the current `NavRegistry` has no literal Settings subgroup — the sibling admin-org config leaves (ShiftGroupings/ChoreTypes/DutyTypes) live in `Nav2_Sched_Definitions`, so the Tabs leaf is placed there (the definitions/settings family). Flagged for team-lead in case a different node is preferred.
