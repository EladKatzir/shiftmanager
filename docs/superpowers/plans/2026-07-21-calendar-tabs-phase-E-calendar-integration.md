# Calendar Tabs — Phase E: Calendar Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the (already-shaped) `ShiftTab` data model into the `/Calendar/Shifts` runtime — a job-type-scoped tab strip with a synthetic "All" pill, tab-aware view filtering, server-computed picker prioritization + off-tab warning, a server-rendered concurrent-edit auto-refresh, and per-(user,molecule,jobtype) last-tab memory.

**Architecture:** A tab is a *view + relevance* layer, never an eligibility gate. The page resolves an active tab (a real `ShiftTab.Id` or the synthetic **All** = `null`) from `?Tab=`, remembered preference, else All. By-Shifts filters rows to the tab's effective shift-type set (empty selection = all job-type shifts; HOME/OFFLINE + null-jobtype shifts always render). By-User filters the roster to the tab's companies (empty = whole molecule) ∪ cross-over. The server tags each picker candidate `inTab` (membership-aware); one shared client utility groups "this tab" first and fires a once-per-(tab,session) non-blocking warning on off-tab picks. Realtime `AssignmentChanged` routes through the existing server-rendered full-grid swap (`triggerCalendarRefresh`), which re-applies the tab filter server-side and materializes newly-minted instances with full chip anatomy.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core + SQLite, SignalR (`shifts-{mol}-{jobtype}` group), vanilla ES5 JS (air-gapped, RTL-aware), xUnit + FluentAssertions with a real-SQLite fixture, Playwright for browser E2E.

## Global Constraints

- **Depends on Phase D.** Assume Phase D has already delivered the reshaped schema + service (see *Phase D consumed interfaces* below). Do NOT re-implement schema/migration/service-CRUD here.
- **IGNORE `FinalProductPublish/`** — it is generated (`scripts/Update-FinalProductPublish.ps1`); never edit it.
- **Branch:** `feat/calendar-tabs-selectors`. Work in a **git worktree** so the running app on `:5000` (which locks the Debug `bin/`) does not block builds.
- **Build `-c Release`** to dodge the running Debug-exe lock: `dotnet build -c Release`.
- **Tests run serialized** (parallel gives spurious `:memory:` SQLite failures): append `-- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1` to every `dotnet test`.
- After any build/restore, run `git checkout -- packages.lock.json` (the build mutates it; leave it clean).
- **Localization (LOC-1):** every new user-visible string is added to BOTH `Resources/SharedResources.resx` AND `Resources/SharedResources.he-IL.resx`. **Grep the key first** (`grep -rn "\"KeyName\"" Resources/`) — a duplicate key breaks `LocalizationDriftTests`. JS-facing strings are additionally registered in `Pages/Shared/_LocalizationScript.cshtml` (`window.AppLocalizer`).
- **Served JS/CSS is fingerprinted at build (`asp-append-version`)** and the dev app serves the compiled `bin/Debug` copy — after editing a `.js`, restart the app (or `curl` the served file for your marker) before browser-testing. Confirm with `curl -s http://localhost:5000/js/<file>.js | grep -c "<marker>"`.
- **Playwright HE (RTL):** set cookie `.AspNetCore.Culture` = `c=he-IL|uic=he-IL` (the `?culture=` query param does nothing — cookie provider only). Launch with a **unique `--user-data-dir`**; **NEVER** `taskkill //F //IM chrome.exe` (it kills the user's real browser).
- **Security:** every new cross-tenant read uses `IgnoreQueryFilters()` + a `SECURITY-AUDITED:` comment; every handler taking a client-supplied tab/molecule id re-verifies scope (IDOR), not just page-level `[Authorize]`.

---

## Phase D consumed interfaces (assume these exist)

Referenced by Consumes blocks below. Do not build these — Phase D owns them.

```csharp
// ShiftTab entity (reshaped by Phase D):
//   int Id; int MoleculeId; int? JobTypeId; string NameEn; string NameHe;
//   bool PrioritizeCompanyUsers; string? Color; int SortOrder; bool IsActive;
// New join entities + DbSets:
//   ShiftTabShiftType { int ShiftTabId; int ShiftTypeId; }   -> _db.ShiftTabShiftTypes
//   ShiftTabCompany   { int ShiftTabId; int CompanyId;   }   -> _db.ShiftTabCompanies
// ShiftType.TabId COLUMN DROPPED. UserShiftTabPreference gains int? JobTypeId.

public interface IShiftTabService   // Phase-D signatures consumed by Phase E:
{
    Task<List<ShiftTab>> GetTabsForMoleculeAsync(int moleculeId, int? jobTypeId, bool includeInactive = false);
    Task<HashSet<int>>   GetCompanyIdsForTabAsync(int tabId);      // tab's selected companies; empty = no restriction
    Task<HashSet<int>>   GetShiftTypeIdsForTabAsync(int tabId);    // tab's selected shift types; empty = all jobtype shifts
    Task<int?>           GetLastTabAsync(int userId, int moleculeId, int? jobTypeId);   // null = All / no preference
    Task                 SetLastTabAsync(int userId, int moleculeId, int? jobTypeId, int? tabId); // null = All
    Task<ShiftTab?>      GetTabAsync(int tabId);
    // ...plus admin CRUD (Phase D) not consumed here.
}
```

**Phase E ADDS two methods to `IShiftTabService`/`ShiftTabService`** (Tasks 2 & 3 produce them):

```csharp
// shiftTypeId -> set of tab ids whose EFFECTIVE set contains it (empty selection expands to all
// molecule+jobtype shift types; HOME/OFFLINE + null-jobtype shared types map to EVERY tab).
Task<Dictionary<int, HashSet<int>>> GetShiftTypeTabMapAsync(int moleculeId, int? jobTypeId);
// users who belong (via ANY CompanyMembership) to the tab's company set. null/zero-company tab -> empty set.
Task<HashSet<int>> GetInTabUserIdsAsync(int? tabId, IReadOnlyCollection<int> userIds);
```

**Tab-state contract used throughout:** `ShiftsModel.Tab` is `int?` where **`null` = the synthetic "All" pseudo-tab** (no DB row: all molecule+jobtype shift types, all companies, no prioritization, no warnings) and a non-null value is a real `ShiftTab.Id`. The pre-Phase-E "`null` = Main / untagged" meaning is **removed**.

---

## Task order

1. **Task 1** — Tab strip + "All" pseudo-tab + tab-resolution rewrite (PF10) · STR-1/7/8, LTM-1/2/3, LOC-2, PF16(strip appear/disappear)
2. **Task 2** — View filtering: rewire every `ShiftType.TabId` reader to the join (PF4/PF7) · STR-2/3/4/5/6/9, CON-3/4, DFT-3
3. **Task 3** — Server-side `inTab` tagging on the eligible + trainee sources (PF5) · PRI-2, SEC-4
4. **Task 4** — Shared client prioritization utility + wire the 3 pickers + off-tab warning (PF8/PF9/UD4) · PRI-1/3/4/5/6/7, SEL-6
5. **Task 5** — Bug-4 auto-refresh via server-rendered grid swap + `ALREADY_ASSIGNED` upgrade (PF3/PF17) · CON-1/2/3/4/5
6. **Task 6** — Draft × tab disclosure (PF15) + PF16 bidi/sticky polish + full-suite + browser E2E · DFT-1/2/3, LOC-2, REG

---

### Task 1: Tab strip + "All" pseudo-tab + tab-resolution rewrite (PF10)

**Files:**
- Modify: `Pages/Calendar/Shifts.cshtml.cs` — OnGet tab block (~271-299), new props, new `internal static ResolveActiveTab`, `TabPrioritizeActive` compute
- Modify: `Pages/Calendar/Shifts.cshtml` — `tabParam` (~17-19), strip markup (~307-331), `CalendarPageConfig` (~537-541), `updateCalendarFilters` (~660-681)
- Create: `ShiftManager.Tests/UnitTests/Calendar/ShiftsTabResolutionTests.cs`
- Modify: `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx` — `Calendar_Tab_All`

**Interfaces:**
- Consumes (Phase D): `GetTabsForMoleculeAsync(int moleculeId, int? jobTypeId, bool)`, `GetLastTabAsync(int,int,int?)`, `SetLastTabAsync(int,int,int?,int?)`, `GetCompanyIdsForTabAsync(int tabId)`, `ShiftTab { Id, NameEn, NameHe, PrioritizeCompanyUsers, Color, SortOrder }`.
- Produces (consumed by Tasks 2-6):
  - `ShiftsModel.Tab` (`int?`, null = All) — the effective active tab after OnGet.
  - `ShiftsModel.AvailableTabs` (`List<ShiftTab>`, (molecule,jobtype)-scoped, `SortOrder` then `NameEn` order).
  - `ShiftsModel.TabPrioritizeActive` (`bool`) — true iff `Tab` is real AND its `PrioritizeCompanyUsers` AND it has ≥1 company.
  - `internal static int? ShiftsModel.ResolveActiveTab(IReadOnlyList<ShiftTab> availableTabs, bool tabParamPresent, int? requestedTab, int? rememberedTab)`.
  - `window.CalendarPageConfig.activeTabId` (`int|null`), `window.CalendarPageConfig.tabPrioritize` (`bool`).

- [ ] **Step 1: Write the failing resolution test**

Create `ShiftManager.Tests/UnitTests/Calendar/ShiftsTabResolutionTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Pages.Calendar;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Calendar;

public class ShiftsTabResolutionTests
{
    private static List<ShiftTab> Tabs(params int[] ids)
    {
        var list = new List<ShiftTab>();
        foreach (var id in ids) list.Add(new ShiftTab { Id = id });
        return list;
    }

    [Fact]
    public void NoTabs_AlwaysAll()
        => ShiftsModel.ResolveActiveTab(Tabs(), tabParamPresent: true, requestedTab: 5, rememberedTab: 5)
            .Should().BeNull();

    [Fact]
    public void ExplicitRealTab_Selected()
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: true, requestedTab: 4, rememberedTab: 3)
            .Should().Be(4);

    [Fact]
    public void ExplicitZero_ResolvesToAll()
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: true, requestedTab: 0, rememberedTab: 3)
            .Should().BeNull();

    [Fact]
    public void ExplicitForeignId_ResolvesToAll()   // STR-8: deleted / foreign id -> All (valid, non-empty)
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: true, requestedTab: 99, rememberedTab: 3)
            .Should().BeNull();

    [Fact]
    public void NoParam_RememberedValid_Restored()   // LTM-1
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: false, requestedTab: null, rememberedTab: 3)
            .Should().Be(3);

    [Fact]
    public void NoParam_RememberedDeleted_FallsBackToAll()   // LTM-2 (post-UD2: All is the safe default)
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: false, requestedTab: null, rememberedTab: 99)
            .Should().BeNull();

    [Fact]
    public void NoParam_NoPreference_DefaultsToAll()   // UD2 first-visit
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: false, requestedTab: null, rememberedTab: null)
            .Should().BeNull();
}
```

- [ ] **Step 2: Run it and confirm it fails to compile**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ShiftsTabResolutionTests" -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: FAIL — `ShiftsModel.ResolveActiveTab` does not exist (compile error).

- [ ] **Step 3: Add `ResolveActiveTab` + new props to `ShiftsModel`**

In `Pages/Calendar/Shifts.cshtml.cs`, change the `_tabOfShiftType` field type (line ~47) and the `Tab` doc comment, then add the new props next to `AvailableTabs` (~161) and the static helper. First, the properties (add after `public List<ShiftTab> AvailableTabs { get; set; } = new();`):

```csharp
    /// <summary>True when the active tab is a real tab whose PrioritizeCompanyUsers is on AND it has ≥1
    /// company. Drives client picker prioritization + off-tab warnings. False for "All"/zero-company tabs.</summary>
    public bool TabPrioritizeActive { get; set; }

    /// <summary>Membership-aware set of picker-candidate user ids that belong to the active tab's companies.
    /// Emitted as data-in-tab on the hidden trainee/assignee selects for client grouping. Empty unless
    /// <see cref="TabPrioritizeActive"/>.</summary>
    public HashSet<int> InTabUserIds { get; set; } = new();
```

Update the `Tab` binding-property doc comment (~126-131) to:

```csharp
    /// <summary>
    /// Selected calendar tab (לשונית). Bound from ?Tab=. After OnGet resolution this holds the EFFECTIVE
    /// tab: <c>null</c> = the synthetic "All" pseudo-tab (all molecule+jobtype shift types, all companies,
    /// no prioritization); otherwise a real ShiftTab id in this (molecule, jobtype). <c>?Tab=0</c> or a
    /// deleted/foreign id resolves to All. Downstream view filters + prioritization key off it.
    /// </summary>
```

Add the static helper (place it right after `OnGetAsync`, before `CalculateDateRange`):

```csharp
    /// <summary>
    /// Resolves the effective active tab. Pure + static so it is unit-testable without the DB (PF10).
    /// null = the synthetic "All" pseudo-tab. Rules:
    ///   • no tabs in scope            → All (no strip).
    ///   • ?Tab= present: real id      → that tab; 0 / deleted / foreign id → All.
    ///   • ?Tab= absent: remembered valid → restore; else (no pref / dangling / deleted) → All (UD2 default).
    /// (LTM-2's literal "first-available" is reconciled to "All" per UD2 — All is always valid + non-empty.)
    /// </summary>
    internal static int? ResolveActiveTab(
        IReadOnlyList<ShiftTab> availableTabs, bool tabParamPresent, int? requestedTab, int? rememberedTab)
    {
        if (availableTabs.Count == 0) return null;
        if (tabParamPresent)
            return (requestedTab.HasValue && requestedTab.Value != 0
                    && availableTabs.Any(t => t.Id == requestedTab.Value))
                ? requestedTab
                : (int?)null;
        return (rememberedTab.HasValue && availableTabs.Any(t => t.Id == rememberedTab.Value))
            ? rememberedTab
            : (int?)null;
    }
```

- [ ] **Step 4: Run the resolution test — expect PASS**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ShiftsTabResolutionTests" -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: PASS (7/7).

- [ ] **Step 5: Rewrite the OnGet tab block to use the resolver + jobtype scope**

In `Pages/Calendar/Shifts.cshtml.cs`, replace the entire `if (MoleculeId.HasValue) { AvailableTabs = ... }` block (currently ~271-299) with:

```csharp
        // Calendar tabs (לשונית): (molecule, jobtype)-scoped strip + synthetic "All" pseudo-tab.
        // After this block, Tab is the EFFECTIVE tab (null = All; otherwise a real tab id in this scope).
        if (MoleculeId.HasValue)
        {
            AvailableTabs = await _tabService.GetTabsForMoleculeAsync(MoleculeId.Value, JobTypeId);
            var tabParamPresent = Request.Query.ContainsKey("Tab");
            var remembered = tabParamPresent
                ? (int?)null
                : await _tabService.GetLastTabAsync(currentUserId, MoleculeId.Value, JobTypeId);
            Tab = ResolveActiveTab(AvailableTabs, tabParamPresent, Tab, remembered);
            // Persist ONLY an explicit choice (incl. explicit All = null) so the strip remembers it.
            if (tabParamPresent)
                await _tabService.SetLastTabAsync(currentUserId, MoleculeId.Value, JobTypeId, Tab);

            // Prioritization is active only for a real tab that opts in AND has ≥1 company.
            if (Tab.HasValue)
            {
                var activeTab = AvailableTabs.FirstOrDefault(t => t.Id == Tab.Value);
                var tabCompanies = await _tabService.GetCompanyIdsForTabAsync(Tab.Value);
                TabPrioritizeActive = activeTab?.PrioritizeCompanyUsers == true && tabCompanies.Count > 0;
            }
        }
        else
        {
            AvailableTabs = new List<ShiftTab>();
            Tab = null;
        }
```

- [ ] **Step 6: Compute `InTabUserIds` after Users + Trainees load**

In `Pages/Calendar/Shifts.cshtml.cs`, immediately AFTER the `Trainees = await _traineeService.GetCompanyTraineesAsync(...)` line (~356), add:

```csharp
        // Membership-aware "in this tab's companies" set for the trainee/assignee picker grouping.
        // Only computed when the active tab actually prioritizes (else every list is flat).
        if (TabPrioritizeActive && Tab.HasValue)
        {
            var pickerUserIds = Users.Select(u => u.Id)
                .Concat(Trainees.Select(t => t.Id))
                .Distinct()
                .ToList();
            InTabUserIds = await _tabService.GetInTabUserIdsAsync(Tab.Value, pickerUserIds);
        }
```

(`GetInTabUserIdsAsync` is produced by Task 3 — until Task 3 exists this line will not compile; implement Task 3 before running a full page build, or temporarily stub the method to return `new HashSet<int>()`.)

- [ ] **Step 7: Rewrite the tab strip markup + tabParam (All pill, culture names, bidi)**

In `Pages/Calendar/Shifts.cshtml`, replace `tabParam` (~17-19):

```cshtml
    // Calendar tab (לשונית) param, appended to hand-built nav links so the active tab persists across
    // week navigation / mode toggles. Tab=0 = the synthetic "All" pill; a real id = that tab.
    var tabParam = Model.AvailableTabs.Any() ? $"&Tab={Model.Tab ?? 0}" : "";
    var isHeStrip = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("he");
```

Replace the `@if (Model.AvailableTabs.Any()) { ... }` strip block (~307-331) with:

```cshtml
    @* Calendar tabs (לשונית) — molecule+jobtype sub-calendars. The always-present "All" pill (Tab=0,
       no DB row) shows everything with no prioritization. Selecting a real tab filters the views and
       (if it opts in) prioritizes pickers. Hidden entirely when this (molecule, jobtype) has no tabs. *@
    @if (Model.AvailableTabs.Any())
    {
        var tabBaseQ = $"?MoleculeId={Model.MoleculeId}&JobTypeId={Model.JobTypeId}&Start={Model.StartDate:yyyy-MM-dd}&ViewMode={Model.ViewMode}&Mode={Model.Mode}{dlParam}";
        <div class="cal-tabs" role="tablist" aria-label="@Localizer["ShiftsCalendar"]">
            <a href="@(tabBaseQ)&Tab=0" role="tab"
               aria-selected="@((!Model.Tab.HasValue).ToString().ToLowerInvariant())"
               class="cal-tab @(!Model.Tab.HasValue ? "cal-tab--active" : "")">@Localizer["Calendar_Tab_All"]</a>
            @foreach (var t in Model.AvailableTabs)
            {
                var active = Model.Tab == t.Id;
                var tabName = isHeStrip ? t.NameHe : t.NameEn;
                <a href="@(tabBaseQ)&Tab=@t.Id" role="tab"
                   aria-selected="@active.ToString().ToLowerInvariant()"
                   class="cal-tab @(active ? "cal-tab--active" : "")">
                    @if (!string.IsNullOrEmpty(t.Color))
                    {
                        <span class="cal-tab__dot" style="background:@t.Color"></span>
                    }
                    @* bidi-isolate the (possibly Latin) tab name so it renders unscrambled in the RTL strip (PF16/LOC-2). *@
                    <bdi>@tabName</bdi>
                </a>
            }
        </div>
    }
```

- [ ] **Step 8: Extend `CalendarPageConfig` + drop Tab on jobtype change**

In `Pages/Calendar/Shifts.cshtml`, replace the `window.CalendarPageConfig = {...}` block (~537-541):

```cshtml
        window.CalendarPageConfig = {
            isTechMolecule: @(Model.IsTechMolecule ? "true" : "false"),
            categoryEligibilityEnabled: @(Model.CategoryEligibilityEnabled ? "true" : "false"),
            moleculeId: @(Model.MoleculeId ?? 0),
            activeTabId: @(Model.Tab?.ToString() ?? "null"),
            tabPrioritize: @(Model.TabPrioritizeActive ? "true" : "false")
        };
```

In `updateCalendarFilters` (~660-671), replace the molecule-only Tab-drop with a molecule-OR-jobtype drop (LTM-3):

```cshtml
            const params = new URLSearchParams(window.location.search);
            // Drop the tab when EITHER molecule or job type changes — a tab id is scoped to one
            // (molecule, jobtype); the server would ignore a stale id anyway, and the new scope must
            // resolve its OWN remembered tab (LTM-3).
            const currentMolecule = '@(Model.MoleculeId ?? 0)';
            const currentJobType = '@(Model.JobTypeId?.ToString() ?? "")';
            if (String(moleculeId) !== currentMolecule || String(jobTypeId) !== currentJobType) {
                params.delete('Tab');
            }
            params.set('MoleculeId', moleculeId);
```

(Leave the rest of `updateCalendarFilters` — the `params.set('JobTypeId'...)` / `params.delete('JobTypeId')` / `ViewMode` lines — unchanged.)

- [ ] **Step 9: Add the `Calendar_Tab_All` resx key (both cultures)**

Grep first: `grep -rn "Calendar_Tab_All" Resources/` → expect no hit. Then add to `Resources/SharedResources.resx`:

```xml
  <data name="Calendar_Tab_All" xml:space="preserve">
    <value>All</value>
  </data>
```

and to `Resources/SharedResources.he-IL.resx`:

```xml
  <data name="Calendar_Tab_All" xml:space="preserve">
    <value>הכל</value>
  </data>
```

- [ ] **Step 10: Build + run the resolution test + loc drift test**

Run: `dotnet build -c Release` then `git checkout -- packages.lock.json`
Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ShiftsTabResolutionTests|FullyQualifiedName~LocalizationDrift" -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: PASS (resolution 7/7 + loc drift green). Note: the page will not fully compile until Task 3's `GetInTabUserIdsAsync` exists — if you are executing tasks strictly in order, temporarily stub it (Step 6 note) so this builds, then remove the stub in Task 3.

- [ ] **Step 11: Commit**

```bash
git add Pages/Calendar/Shifts.cshtml.cs Pages/Calendar/Shifts.cshtml \
        ShiftManager.Tests/UnitTests/Calendar/ShiftsTabResolutionTests.cs \
        Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat(calendar-tabs): job-type-scoped strip + All pseudo-tab + tab-resolution rewrite (PF10)"
```

**Covers:** STR-1 (strip = Geo+Tacti for Oren/Alhut, culture names), STR-7 (zero-tab → no strip), STR-8 (`?Tab=0`/foreign → All), LTM-1/2/3 (per-(user,molecule,jobtype) memory, deleted→All, jobtype-change drops Tab), LOC-2 (bidi-isolated names + All pill), PF10, PF16 (strip appear/disappear via `updateCalendarFilters`).

---

### Task 2: View filtering — rewire every `ShiftType.TabId` reader to the join (PF4/PF7)

**Files:**
- Modify: `Services/ShiftTabService.cs` + `Services/IShiftTabService.cs` — add `GetShiftTypeTabMapAsync`
- Modify: `Pages/Calendar/Shifts.cshtml.cs` — `_tabOfShiftType` field type (~47), By-Shifts filter (~476-481), By-User shift-type list (~605-609), By-User roster + map (~633-654), `ShiftIsOnActiveTab` (~1276-1281)
- Modify: `Pages/Api/Calendar/GetShiftsData.cshtml.cs` — tab filter (~103-108) → join + graceful fallback
- Modify: `ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs` — `GetShiftTypeTabMapAsync` tests

**Interfaces:**
- Consumes (Phase D): `GetShiftTypeIdsForTabAsync(int tabId)`, `GetCompanyIdsForTabAsync(int tabId)`, `_db.ShiftTabShiftTypes`, `_db.ShiftTabCompanies`.
- Produces: `IShiftTabService.GetShiftTypeTabMapAsync(int moleculeId, int? jobTypeId) → Dictionary<int, HashSet<int>>` (effective membership: empty selection = all; shared/HOME/OFFLINE map to every tab). `ShiftsModel._tabOfShiftType` retyped to `Dictionary<int, HashSet<int>>`.

- [ ] **Step 1: Write the failing `GetShiftTypeTabMapAsync` test**

Add to `ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs` (reuse the file's existing real-SQLite fixture + seed helpers; adapt entity construction to the file's conventions):

```csharp
[Fact]
public async Task GetShiftTypeTabMap_EmptySelectionTab_MapsAllMoleculeJobtypeShiftTypes()
{
    // Arrange: molecule M, jobtype J, shift types A,B (jobtype J) + shared S (jobtype null) + home H.
    // Tab "Geo" selects only A; tab "Wide" selects nothing (empty = all).
    var (svc, db) = NewService();
    var (mId, jId, a, b, s, h) = await SeedMoleculeWithShiftTypes(db);   // helper in this file's style
    var geo  = await svc.CreateTabAsync(mId, jId, "Geo", "Geo");         // Phase D CRUD
    var wide = await svc.CreateTabAsync(mId, jId, "Wide", "Wide");
    await svc.SetTabShiftTypesAsync(geo.Id, new[] { a });                 // Phase D membership setter
    // wide: leave empty

    // Act
    var map = await svc.GetShiftTypeTabMapAsync(mId, jId);

    // Assert
    map[a].Should().Contain(new[] { geo.Id, wide.Id });   // A on Geo (explicit) + Wide (empty=all)
    map[b].Should().Contain(wide.Id);                     // B only on Wide (empty=all), NOT Geo
    map[b].Should().NotContain(geo.Id);
    map[s].Should().Contain(new[] { geo.Id, wide.Id });   // shared null-jobtype maps to EVERY tab (PF7)
    map[h].Should().Contain(new[] { geo.Id, wide.Id });   // HOME maps to EVERY tab (PF7)
}
```

(If the exact Phase-D CRUD/setter names differ, adjust to the real ones — `CreateAsync`/`SetTabShiftTypesAsync` etc. — that the `ShiftTabServiceTests` file already exercises.)

- [ ] **Step 2: Run it — expect FAIL (method missing)**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~GetShiftTypeTabMap_EmptySelectionTab" -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: FAIL — `GetShiftTypeTabMapAsync` not defined.

- [ ] **Step 3: Implement `GetShiftTypeTabMapAsync`**

Add the signature to `Services/IShiftTabService.cs`:

```csharp
    /// <summary>
    /// shiftTypeId → the set of tab ids whose EFFECTIVE shift-type set contains it, for one (molecule,
    /// jobtype). A tab with an explicit selection contributes those ids; a tab with NO selection expands
    /// to every molecule+jobtype shift type ("empty = all"); HOME/OFFLINE + null-jobtype shared shift
    /// types map to EVERY tab (PF7 — never ghosted / never hidden). Powers by-user cross-over + ghosting.
    /// </summary>
    Task<Dictionary<int, HashSet<int>>> GetShiftTypeTabMapAsync(int moleculeId, int? jobTypeId);
```

Implement in `Services/ShiftTabService.cs` (mirror the By-Shifts shift-type universe query so the "empty = all" set matches exactly what the grid shows under All):

```csharp
    // SECURITY-AUDITED: SAFE — molecule-scoped read; callers gate molecule access before this runs.
    public async Task<Dictionary<int, HashSet<int>>> GetShiftTypeTabMapAsync(int moleculeId, int? jobTypeId)
    {
        var areaId = await _db.Molecules.IgnoreQueryFilters()
            .Where(m => m.Id == moleculeId).Select(m => m.AreaId).FirstOrDefaultAsync();

        // The shift-type universe the "All" grid shows for this (molecule, jobtype).
        var universe = _db.ShiftTypes.IgnoreQueryFilters()
            .Where(st => st.MoleculeId == moleculeId
                      || (st.Scope == Models.Support.ShiftScope.Area && st.AreaId == areaId));
        universe = jobTypeId.HasValue
            ? universe.Where(st => st.JobTypeId == jobTypeId.Value || st.JobTypeId == null)
            : universe.Where(st => st.JobTypeId == null);
        var allTypes = await universe
            .Select(st => new { st.Id, st.JobTypeId, st.IsHome, st.IsOffline })
            .ToListAsync();

        var tabs = await GetTabsForMoleculeAsync(moleculeId, jobTypeId);
        var explicitByTab = new Dictionary<int, HashSet<int>>();
        foreach (var t in tabs)
            explicitByTab[t.Id] = await GetShiftTypeIdsForTabAsync(t.Id);

        var map = new Dictionary<int, HashSet<int>>();
        foreach (var st in allTypes)
        {
            var set = new HashSet<int>();
            var alwaysEverywhere = st.JobTypeId == null || st.IsHome || st.IsOffline; // PF7
            foreach (var t in tabs)
            {
                var chosen = explicitByTab[t.Id];
                if (alwaysEverywhere || chosen.Count == 0 || chosen.Contains(st.Id))
                    set.Add(t.Id);
            }
            map[st.Id] = set;
        }
        return map;
    }
```

- [ ] **Step 4: Run the map test — expect PASS**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~GetShiftTypeTabMap_EmptySelectionTab" -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: PASS.

- [ ] **Step 5: Retype `_tabOfShiftType` + rewrite `ShiftIsOnActiveTab`**

In `Pages/Calendar/Shifts.cshtml.cs`, change the field (~45-47):

```csharp
    // Per-request: shiftTypeId → set of tab ids whose effective set contains it (empty = all; shared/home/
    // offline on every tab). Populated in BuildUserBasedCalendarAsync; drives cross-over + per-cell ghosting.
    private Dictionary<int, HashSet<int>> _tabOfShiftType = new();
```

Rewrite `ShiftIsOnActiveTab` (~1271-1281):

```csharp
    /// <summary>
    /// True if a shift type is on the active tab (or "All"/no tabs). Drives by-user "busy elsewhere"
    /// ghosting: a shift NOT on the active tab shows greyed + non-interactive, but is NEVER hidden from
    /// conflict/rest/hours (those see the full shift set). A shift type can be on multiple tabs (STR-3).
    /// </summary>
    private bool ShiftIsOnActiveTab(int shiftTypeId)
    {
        if (!Tab.HasValue) return true; // "All" (or no tabs) → nothing is "elsewhere"
        return _tabOfShiftType.TryGetValue(shiftTypeId, out var tset) && tset.Contains(Tab.Value);
    }
```

- [ ] **Step 6: Rewrite the By-Shifts shift-type filter (PF4/PF7)**

In `BuildShiftBasedCalendarAsync`, replace the tab filter (~476-481):

```csharp
        // Tab (לשונית): a real tab shows its selected shift types; empty selection = all job-type shifts.
        // "All" (Tab == null) applies no restriction. HOME/OFFLINE presence + null-jobtype shared shift
        // types ALWAYS render on every tab (PF7) so a tab can never hide a reachable shift.
        if (Tab.HasValue)
        {
            var tabShiftTypeIds = await _tabService.GetShiftTypeIdsForTabAsync(Tab.Value);
            if (tabShiftTypeIds.Count > 0)
                shiftTypeQuery = shiftTypeQuery.Where(st =>
                    tabShiftTypeIds.Contains(st.Id) || st.JobTypeId == null || st.IsHome || st.IsOffline);
        }
```

- [ ] **Step 7: Rewrite the By-User shift-type list + roster + map (PF4/PF7)**

In `BuildUserBasedCalendarAsync`, replace the shift-type list tab filter (~605-609) with the SAME block as Step 6 (so the assign bottom-sheet's shift-type dropdown offers the tab's + shared shifts).

Then replace the roster/cross-over block (~633-654) with:

```csharp
        // Tab (לשונית) roster: base = the tab's companies (empty selection = whole molecule) ∪ cross-over
        // (anyone assigned to a shift type whose tab set contains the active tab, INCLUDING staged draft
        // assignments — computed AFTER the draft overlay). A person can appear on multiple tabs. The tab
        // NEVER narrows conflict/rest/hours/fairness — those see the full shift set.
        _tabOfShiftType = await _tabService.GetShiftTypeTabMapAsync(moleculeId, jobTypeId);
        if (Tab.HasValue)
        {
            var tabCompanies = await _tabService.GetCompanyIdsForTabAsync(Tab.Value);
            var crossOver = assignments
                .Where(a => a.UserId.HasValue && a.ShiftInstance != null
                    && _tabOfShiftType.TryGetValue(a.ShiftInstance.ShiftTypeId, out var tset)
                    && tset.Contains(Tab.Value))
                .Select(a => a.UserId!.Value)
                .ToHashSet();
            // Empty company selection = no restriction (whole molecule) — only filter when the tab names companies.
            if (tabCompanies.Count > 0)
                users = users.Where(u => tabCompanies.Contains(u.CompanyId) || crossOver.Contains(u.Id)).ToList();
        }
```

(Note: the `AvailableTabs.Count > 0` outer guard is gone — the map is always built so ghosting resolves; when `Tab` is null/All, `ShiftIsOnActiveTab` short-circuits to `true` and no roster filter applies.)

- [ ] **Step 8: Rewrite `GetShiftsData` tab filter → join + graceful fallback (STR-9/CON-4)**

In `Pages/Api/Calendar/GetShiftsData.cshtml.cs`, replace the tab filter (~100-108). Inject `IShiftTabService _tabService` into the constructor first (add the field + ctor param, mirroring the existing injected services), then:

```csharp
            // Tab (לשונית): keep the shadow refresh aligned with the server render — filter instances to the
            // active tab's effective shift-type set via the ShiftTabShiftType join. Semantics match the page:
            //   tab omitted / tab==0            → "All": no restriction.
            //   real tab with selection         → its shift types ∪ shared/HOME/OFFLINE (PF7).
            //   real tab with empty selection   → all job-type shifts (no restriction).
            //   UNKNOWN / deleted / foreign id  → graceful fallback: return ALL data (never match-nothing) so a
            //                                     peer whose tab was deleted mid-session doesn't blank (CON-4).
            if (tab.HasValue && tab.Value != 0)
            {
                var tabExists = await _db.ShiftTabs.IgnoreQueryFilters()
                    .AnyAsync(t => t.Id == tab.Value && t.MoleculeId == moleculeId);
                if (tabExists)
                {
                    var tabShiftTypeIds = await _tabService.GetShiftTypeIdsForTabAsync(tab.Value);
                    if (tabShiftTypeIds.Count > 0)
                        instances = instances.Where(i => i.ShiftType != null &&
                            (tabShiftTypeIds.Contains(i.ShiftTypeId)
                             || i.ShiftType.JobTypeId == null || i.ShiftType.IsHome || i.ShiftType.IsOffline))
                            .ToList();
                    // empty selection → no filter (all)
                }
                // unknown tab → no filter (graceful fallback)
            }
```

- [ ] **Step 9: Build + run the tab service + affected page-model tests**

Run: `dotnet build -c Release` then `git checkout -- packages.lock.json`
Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~ShiftTabServiceTests|FullyQualifiedName~DraftOverlayRenderTests|FullyQualifiedName~ShiftsUserRowShiftCount" -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: PASS. (These test classes were migrated off `ShiftType.TabId` in Phase D; they must still be green here.)

- [ ] **Step 10: Commit**

```bash
git add Services/ShiftTabService.cs Services/IShiftTabService.cs \
        Pages/Calendar/Shifts.cshtml.cs Pages/Api/Calendar/GetShiftsData.cshtml.cs \
        ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs
git commit -m "feat(calendar-tabs): rewire view filtering to ShiftTabShiftType join + effective-set ghosting (PF4/PF7)"
```

**Covers:** STR-2 (Geo By-Shifts = Geo's types + HOME/OFFLINE), STR-3 (dual-tab shift renders on both), STR-4 (null-jobtype shared never invisible), STR-5 (By-User roster = companies ∪ cross-over), STR-6 (SET-based ghosting), STR-9 (`GetShiftsData` parity), CON-3 (server re-applies filter), CON-4 (graceful unknown-tab fallback), DFT-3 (staged synthetic cells use SET match), PF4, PF7.

---

### Task 3: Server-side `inTab` tagging on the eligible + trainee sources (PF5)

**Files:**
- Modify: `Services/ShiftTabService.cs` + `Services/IShiftTabService.cs` — add `GetInTabUserIdsAsync`
- Modify: `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs` — accept `tab`, return `inTab` per user
- Modify: `Pages/Calendar/Shifts.cshtml` — `#traineeSelect` + shift-mode `#assigneeSelect-shifts` options get `data-in-tab` (+ trainee `data-company`/`data-jobtype`)
- Modify: `ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs` — `GetInTabUserIdsAsync` tests

**Interfaces:**
- Consumes (Phase D): `GetCompanyIdsForTabAsync(int tabId)`, `GetTabAsync(int tabId)`, `_db.CompanyMemberships { int UserId; int CompanyId; }`.
- Produces: `IShiftTabService.GetInTabUserIdsAsync(int? tabId, IReadOnlyCollection<int> userIds) → HashSet<int>` (membership-aware). `GetEligibleUsersForShift` JSON gains `inTab` per user. Hidden selects carry `data-in-tab` (+ trainee `data-company`/`data-jobtype`).

- [ ] **Step 1: Write the failing `GetInTabUserIdsAsync` test (multi-company aware)**

Add to `ShiftTabServiceTests.cs`:

```csharp
[Fact]
public async Task GetInTabUserIds_MembershipAware_IncludesMultiCompanyUser()
{
    // Tab companies = {City}. User U primary company = Tzafona but has a CompanyMembership in City.
    var (svc, db) = NewService();
    var (mId, jId, city, tzafona) = await SeedTwoCompanies(db);          // helper in this file's style
    var tab = await svc.CreateTabAsync(mId, jId, "Geo", "Geo");
    await svc.SetTabCompaniesAsync(tab.Id, new[] { city });              // Phase D setter
    var u = await SeedUser(db, primaryCompanyId: tzafona);
    await SeedMembership(db, u.Id, city);                                // DoesShifts membership in City

    var inTab = await svc.GetInTabUserIdsAsync(tab.Id, new[] { u.Id });

    inTab.Should().Contain(u.Id);   // in-tab via membership, NOT primary company (PRI-2)
}

[Fact]
public async Task GetInTabUserIds_NullTab_ReturnsEmpty()
{
    var (svc, _) = NewService();
    (await svc.GetInTabUserIdsAsync(null, new[] { 1, 2, 3 })).Should().BeEmpty();
}
```

- [ ] **Step 2: Run — expect FAIL (method missing)**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~GetInTabUserIds" -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: FAIL.

- [ ] **Step 3: Implement `GetInTabUserIdsAsync`**

Signature in `Services/IShiftTabService.cs`:

```csharp
    /// <summary>
    /// The subset of <paramref name="userIds"/> that belong — via ANY CompanyMembership — to the tab's
    /// company set. Multi-company aware (a user in-tab through a non-primary membership counts). null tab,
    /// or a tab with no companies, → empty (no prioritization). SECURITY: caller gates molecule access.
    /// </summary>
    Task<HashSet<int>> GetInTabUserIdsAsync(int? tabId, IReadOnlyCollection<int> userIds);
```

Implementation in `Services/ShiftTabService.cs`:

```csharp
    // SECURITY-AUDITED: SAFE — membership read scoped to the tab's companies + the supplied candidate ids;
    // the calling page/endpoint gates molecule access before this runs.
    public async Task<HashSet<int>> GetInTabUserIdsAsync(int? tabId, IReadOnlyCollection<int> userIds)
    {
        if (tabId is null || userIds.Count == 0) return new HashSet<int>();
        var tabCompanies = await GetCompanyIdsForTabAsync(tabId.Value);
        if (tabCompanies.Count == 0) return new HashSet<int>();
        var ids = userIds.ToList();
        var inTab = await _db.CompanyMemberships.IgnoreQueryFilters()
            .Where(m => tabCompanies.Contains(m.CompanyId) && ids.Contains(m.UserId))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync();
        return inTab.ToHashSet();
    }
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~GetInTabUserIds" -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: PASS (2/2). Also re-run Task 1's compile note is now satisfied (remove any temporary stub of this method).

- [ ] **Step 5: Have `GetEligibleUsersForShift` accept `tab` + return `inTab` (PF5/SEC-4)**

In `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs`: inject `IShiftTabService _tabService` (add field + ctor param). Change the handler signature to accept `tab`:

```csharp
    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int moleculeId,
        [FromQuery] int shiftTypeId,
        [FromQuery] int? tab,
        [FromQuery] bool allowFallback = false)
```

Then, after `var result = await _candidateService.GetEligibleCandidatesAsync(...)` and before the `return new JsonResult(...)`, compute the in-tab set and tag the projection:

```csharp
            // Tab (לשונית) prioritization: tag each candidate inTab (membership-aware) so the client groups
            // "this tab" first. SEC-4: a foreign-molecule tab id is ignored (treated as no prioritization) —
            // never leak cross-molecule tab config, and results stay molecule-scoped either way.
            var inTabUserIds = new HashSet<int>();
            if (tab.HasValue && tab.Value != 0)
            {
                var t = await _tabService.GetTabAsync(tab.Value);
                if (t != null && t.MoleculeId == moleculeId)
                    inTabUserIds = await _tabService.GetInTabUserIdsAsync(
                        tab.Value, result.Users.Select(u => u.Id).ToList());
            }

            return new JsonResult(new
            {
                success = true,
                reason = result.Reason,
                users = result.Users.Select(u => new
                {
                    id = u.Id,
                    name = u.Name,
                    companyName = u.CompanyName,
                    inTab = inTabUserIds.Contains(u.Id)
                }).ToList()
            });
```

- [ ] **Step 6: Tag the hidden `#traineeSelect` + shift-mode `#assigneeSelect-shifts` options**

In `Pages/Calendar/Shifts.cshtml`, the shift-mode assignee select (~443-448):

```cshtml
    @* Shift-mode: rows are shift types, so the dropdown offers users to assign *@
    <select id="assigneeSelect-shifts" data-role="assignee-select" data-item-type="user" style="display:none">
        @foreach (var user in Model.Users)
        {
            <option value="@user.Id" data-in-tab="@(Model.InTabUserIds.Contains(user.Id) ? "1" : "0")">@user.DisplayName</option>
        }
    </select>
```

The trainee select (~452-460):

```cshtml
@if (Model.Trainees.Any())
{
    <select id="traineeSelect" data-role="trainee-select" style="display:none">
        @foreach (var trainee in Model.Trainees)
        {
            <option value="@trainee.Id"
                    data-in-tab="@(Model.InTabUserIds.Contains(trainee.Id) ? "1" : "0")"
                    data-company="@trainee.CompanyId"
                    data-jobtype="@(trainee.JobTypeId?.ToString() ?? "")">@trainee.DisplayName</option>
        }
    </select>
}
```

- [ ] **Step 7: Build + curl the endpoint contract**

Run: `dotnet build -c Release` then `git checkout -- packages.lock.json`. Restart the app.
With a logged-in cookie jar (or via a signed-in browser session), hit:
`curl -s "http://localhost:5000/Api/Calendar/GetEligibleUsersForShift?moleculeId=<M>&shiftTypeId=<ST>&tab=<GeoId>" --cookie <jar>`
Expected JSON: `success:true`, each `users[]` entry has an `inTab` boolean; a City-membership user for a Geo(City) tab is `inTab:true`.

- [ ] **Step 8: Commit**

```bash
git add Services/ShiftTabService.cs Services/IShiftTabService.cs \
        Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs Pages/Calendar/Shifts.cshtml \
        ShiftManager.Tests/UnitTests/Services/ShiftTabServiceTests.cs
git commit -m "feat(calendar-tabs): server-computed membership-aware inTab tagging on pickers (PF5)"
```

**Covers:** PRI-2 (server-computed, membership-aware — multi-company user in-tab, no warning), SEC-4 (foreign-molecule tab ignored), PF5. Feeds Task 4's client grouping.

---

### Task 4: Shared client prioritization utility + wire the 3 pickers + off-tab warning (PF8/PF9/UD4)

**Files:**
- Create: `wwwroot/js/calendar-tab-prioritization.js`
- Modify: `Pages/Calendar/Shifts.cshtml` — load the new script (in `@section Scripts`)
- Modify: `wwwroot/js/calendar-quick-entry.js` — `fetchEligible` sends `tab`, cache key includes tab, `renderEligibleDropdown` groups + orders, `selectItem` warns
- Modify: `wwwroot/js/calendar-bottom-sheet.js` — `populateEligibleUsersAsync` sends `tab` + groups via optgroups; `getAvailableUsers`/legacy path reads `data-in-tab`; `handleAssign` warns
- Modify: `wwwroot/js/calendar-inline-edit.js` — `openInlineTraineePicker` copies `data-company`/`data-jobtype`/`data-in-tab`, builds optgroups, guards blur when enhanced, warns on pick
- Modify: `Pages/Shared/_LocalizationScript.cshtml` — register 3 keys
- Modify: `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx` — `CalendarTab_Group_ThisTab`, `CalendarTab_Group_Other`, `CalendarTab_OffTabWarning`

**Interfaces:**
- Consumes: `window.CalendarPageConfig.tabPrioritize` + `.activeTabId` (Task 1); `inTab` on eligible JSON (Task 3); `data-in-tab` on `#traineeSelect`/`#assigneeSelect-shifts` options (Task 3).
- Produces: `window.CalendarTabPrioritization = { isActive(), partition(users), label(which), maybeWarnOffTab(user) }`.

- [ ] **Step 1: Add the 3 resx keys (both cultures) + register in AppLocalizer**

Grep first: `grep -rn "CalendarTab_Group_ThisTab\|CalendarTab_Group_Other\|CalendarTab_OffTabWarning" Resources/` → expect no hit. Add to `Resources/SharedResources.resx`:

```xml
  <data name="CalendarTab_Group_ThisTab" xml:space="preserve"><value>This tab</value></data>
  <data name="CalendarTab_Group_Other" xml:space="preserve"><value>Other in molecule</value></data>
  <data name="CalendarTab_OffTabWarning" xml:space="preserve"><value>This user isn't from this tab's companies.</value></data>
```

Add to `Resources/SharedResources.he-IL.resx`:

```xml
  <data name="CalendarTab_Group_ThisTab" xml:space="preserve"><value>לשונית זו</value></data>
  <data name="CalendarTab_Group_Other" xml:space="preserve"><value>אחרים במולקולה</value></data>
  <data name="CalendarTab_OffTabWarning" xml:space="preserve"><value>משתמש זה אינו מהחברות של לשונית זו.</value></data>
```

Register in `Pages/Shared/_LocalizationScript.cshtml` (append inside the object, before the closing `}`; add a trailing comma to the current last entry `Team_DeleteTableConfirm`):

```cshtml
        // Calendar tab prioritization (calendar-tab-prioritization.js)
        "CalendarTab_Group_ThisTab": @Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["CalendarTab_Group_ThisTab"].Value)),
        "CalendarTab_Group_Other": @Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["CalendarTab_Group_Other"].Value)),
        "CalendarTab_OffTabWarning": @Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["CalendarTab_OffTabWarning"].Value))
```

- [ ] **Step 2: Create the shared utility**

Create `wwwroot/js/calendar-tab-prioritization.js`:

```javascript
/**
 * Calendar Tab Prioritization (Phase E, PF8) — the ONE shared grouping/ordering + off-tab warning
 * utility used by all three assignment pickers (quick-entry, bottom-sheet, trainee). A tab NEVER
 * hard-restricts: it only sorts "this tab" first and warns (once per tab/session) on an off-tab pick.
 * Ordering is applied at RENDER time (never baked into any cache — SEL-6).
 */
(function () {
    'use strict';

    // Session-scoped (page lifetime): which tabs have already fired their one-time off-tab warning (UD4).
    var warnedTabs = {};

    function cfg() { return window.CalendarPageConfig || {}; }

    function isActive() {
        return !!cfg().tabPrioritize; // true only for a real tab that opts in AND has ≥1 company (server)
    }

    function label(which) {
        var L = window.AppLocalizer || {};
        return which === 'this'
            ? (L.CalendarTab_Group_ThisTab || 'This tab')
            : (L.CalendarTab_Group_Other || 'Other in molecule');
    }

    // users: [{ id, inTab, ... }] in the caller's current order. Returns two arrays, order preserved
    // within each group (stable). When inactive, everything is "other" (callers render a flat list).
    function partition(users) {
        var thisTab = [], other = [];
        (users || []).forEach(function (u) {
            if (isActive() && u && u.inTab) thisTab.push(u); else other.push(u);
        });
        return { thisTab: thisTab, other: other };
    }

    // Non-blocking, dismissible, at-most-once-per-(tab, session) warning on an off-tab pick (UD4).
    // Uses FeedbackModal (a separate surface from Toast) so it never stacks over / gets wiped by the
    // transient success toast. No-op when prioritization is inactive or the picked user is in-tab.
    function maybeWarnOffTab(user) {
        if (!isActive() || !user || user.inTab) return;
        var key = 'tab-' + (cfg().activeTabId == null ? 'none' : cfg().activeTabId);
        if (warnedTabs[key]) return;
        warnedTabs[key] = true;
        var msg = (window.AppLocalizer && window.AppLocalizer.CalendarTab_OffTabWarning)
                  || "This user isn't from this tab's companies.";
        if (window.FeedbackModal && typeof window.FeedbackModal.show === 'function') {
            window.FeedbackModal.show('warning', msg);
        } else if (window.showToast) {
            window.showToast(msg, 'warning');
        }
    }

    window.CalendarTabPrioritization = {
        isActive: isActive,
        partition: partition,
        label: label,
        maybeWarnOffTab: maybeWarnOffTab
    };
})();
```

- [ ] **Step 3: Load the utility before the pickers**

In `Pages/Calendar/Shifts.cshtml` `@section Scripts`, add BEFORE `calendar-inline-edit.js` (~545):

```cshtml
    <script src="~/js/calendar-tab-prioritization.js" asp-append-version="true"></script>
```

- [ ] **Step 4: Quick-entry — send `tab`, cache per tab, group + order at render, warn on pick**

In `wwwroot/js/calendar-quick-entry.js`:

(a) `eligibleKey` (~185) → include tab:

```javascript
    function eligibleKey(mol, st) {
        var t = (window.CalendarPageConfig && window.CalendarPageConfig.activeTabId != null)
            ? window.CalendarPageConfig.activeTabId : 0;
        return mol + ':' + st + ':' + t;
    }
```

(b) `fetchEligible` URL (~203) → append `&tab=`:

```javascript
        var t = (window.CalendarPageConfig && window.CalendarPageConfig.activeTabId != null)
            ? window.CalendarPageConfig.activeTabId : 0;
        var url = '/Api/Calendar/GetEligibleUsersForShift?moleculeId=' + mol + '&shiftTypeId=' + st
            + '&tab=' + t + (allowFallback ? '&allowFallback=true' : '');
```

(c) `loadItems` (~411) → carry `inTab` from the cached eligible entry onto each item:

```javascript
                activeInput._eligible.users.forEach(function (u) {
                    allItems.push({ id: String(u.id), text: u.name, companyName: u.companyName || null,
                                    inTab: !!u.inTab, type: 'user', key: null, color: null });
                });
```

(d) `renderEligibleDropdown` (~385-401) — group + order at render (SEL-6). Replace the final render loop (`for (var i = 0; i < items.length; i++) renderEligUserOption(items[i]);`) with:

```javascript
        // PF8: apply "this tab" → "other" ordering at RENDER (never baked into eligibleCache — SEL-6).
        if (window.CalendarTabPrioritization && window.CalendarTabPrioritization.isActive()) {
            var parts = window.CalendarTabPrioritization.partition(items);
            if (parts.thisTab.length) {
                appendEligGroupHeader(window.CalendarTabPrioritization.label('this'));
                for (var ti = 0; ti < parts.thisTab.length; ti++) renderEligUserOption(parts.thisTab[ti]);
            }
            if (parts.other.length) {
                appendEligGroupHeader(window.CalendarTabPrioritization.label('other'));
                for (var oi = 0; oi < parts.other.length; oi++) renderEligUserOption(parts.other[oi]);
            }
        } else {
            for (var i = 0; i < items.length; i++) renderEligUserOption(items[i]);
        }
```

Add a small group-header helper next to `appendEligState` (~247):

```javascript
    function appendEligGroupHeader(text) {
        var h = document.createElement('div');
        h.className = 'quick-entry-group-header';
        h.setAttribute('aria-hidden', 'true');
        var s = document.createElement('span');
        s.textContent = text;
        h.appendChild(s);
        dropdown.appendChild(h);
    }
```

(e) `selectItem` — warn on an off-tab user pick. In the `else if (item.type === 'user')` branch (~1353-1356), insert BEFORE the `promise = window.quickAddShift(...)` line:

```javascript
        } else if (item.type === 'user') {
            // Shifts By-Shift: row is a ShiftType (shift-{id}), the pick is the assignee.
            if (window.CalendarTabPrioritization) window.CalendarTabPrioritization.maybeWarnOffTab(item);
            var shiftTypeId = rowId.replace('shift-', '');
            promise = window.quickAddShift(parseInt(shiftTypeId, 10), date, parseInt(item.id, 10), qeConfirm);
```

- [ ] **Step 5: Bottom-sheet — send `tab`, group via optgroups, warn on assign**

In `wwwroot/js/calendar-bottom-sheet.js`:

(a) `populateEligibleUsersAsync` (~876-924) — append `&tab=` to the URL, and render the returned users into two optgroups when prioritization is active. Replace the URL line (~884) and the `data.users.forEach(...)` option loop (~893-901):

```javascript
            var tParam = (window.CalendarPageConfig && window.CalendarPageConfig.activeTabId != null)
                ? window.CalendarPageConfig.activeTabId : 0;
            var url = '/Api/Calendar/GetEligibleUsersForShift?moleculeId=' + moleculeId + '&shiftTypeId=' + shiftTypeId
                + '&tab=' + tParam + (allowFallback ? '&allowFallback=true' : '');
```

```javascript
            if (Array.isArray(data.users) && data.users.length > 0) {
                appendUsersGrouped(selectEl, data.users);   // PF8 optgroup grouping (see helper below)
                if (date && moleculeId) {
                    decorateOptionsWithBusyAsync(selectEl, data.users.map(function (u) { return u.id; }), date, moleculeId, null)
                        .catch(function (err) { console.warn('Busy decoration failed:', err); });
                }
            } else if (data.reason === 'noCategory') {
```

Add the grouping helper near `appendDisabledOption` (~842):

```javascript
    // Append user options, split into "this tab" / "other" native <optgroup>s when prioritization is
    // active; otherwise a flat option list. Each option keeps data-in-tab so a later resync knows the group.
    function appendUsersGrouped(selectEl, users) {
        function opt(u) {
            var o = document.createElement('option');
            o.value = u.id;
            o.dataset.userName = u.name;
            o.dataset.inTab = u.inTab ? '1' : '0';
            o.textContent = u.companyName ? (u.name + ' — ' + u.companyName) : u.name;
            return o;
        }
        var P = window.CalendarTabPrioritization;
        if (P && P.isActive()) {
            var parts = P.partition(users);
            if (parts.thisTab.length) {
                var g1 = document.createElement('optgroup'); g1.label = P.label('this');
                parts.thisTab.forEach(function (u) { g1.appendChild(opt(u)); });
                selectEl.appendChild(g1);
            }
            if (parts.other.length) {
                var g2 = document.createElement('optgroup'); g2.label = P.label('other');
                parts.other.forEach(function (u) { g2.appendChild(opt(u)); });
                selectEl.appendChild(g2);
            }
        } else {
            users.forEach(function (u) { selectEl.appendChild(opt(u)); });
        }
    }
```

(b) `handleAssign` (~1113-1131) — warn on an off-tab pick before calling `quickAddShift` in the shift-mode branch. At the top of the `calendarType === 'shifts'` block, resolve the picked option's `inTab` and warn:

```javascript
        } else if (calendarType === 'shifts' && typeof window.quickAddShift === 'function') {
            // PF8: off-tab warning (once per tab/session) when the chosen user isn't from the tab's companies.
            var sheetSelect = document.getElementById('bottom-sheet-user-select');
            var chosenOpt = sheetSelect ? sheetSelect.options[sheetSelect.selectedIndex] : null;
            if (window.CalendarTabPrioritization && chosenOpt && cellData.rowId
                && cellData.rowId.indexOf('shift-') === 0) {
                window.CalendarTabPrioritization.maybeWarnOffTab(
                    { id: userId, inTab: chosenOpt.dataset.inTab === '1' });
            }
            // Detect mode from row ID prefix
            if (cellData.rowId && cellData.rowId.indexOf('user-') === 0) {
```

(c) `getAvailableUsers` (~989-1004) — carry `data-in-tab` from the source select options into the returned objects so the legacy (flag-off) shift-mode path can also group:

```javascript
            for (var i = 0; i < select.options.length; i++) {
                var option = select.options[i];
                if (option.value) {
                    users.push({ id: option.value, name: option.textContent,
                                 companyName: option.dataset.company || null,
                                 inTab: option.dataset.inTab === '1' });
                }
            }
```

Then in the legacy `else` branch that builds `#bottom-sheet-user-select` from `getAvailableUsers` (~510-519), route through `appendUsersGrouped` instead of the manual loop:

```javascript
                // Populate from available users (read from page data or cell context)
                var availableUsers = getAvailableUsers(cellData);
                appendUsersGrouped(userSelect, availableUsers.map(function (u) {
                    return { id: u.id, name: u.name, companyName: u.companyName, inTab: !!u.inTab };
                }));
```

(Leave the busy-decoration block right after it unchanged.)

- [ ] **Step 6: Trainee picker — copy data attrs, optgroup ordering, blur guard, warn (PF9)**

In `wwwroot/js/calendar-inline-edit.js`, rewrite `openInlineTraineePicker`'s option-clone + build (currently ~871-901). Replace the option-clone loop, blur handler, and change handler:

```javascript
    var select = document.createElement('select');
    select.className = 'excel-calendar__trainee-picker';
    var def = document.createElement('option');
    def.value = '';
    def.textContent = window.AppLocalizer?.BottomSheet_AddTrainee || 'Add trainee...';
    select.appendChild(def);

    // PF8/PF9: clone options, PRESERVING data-company/data-jobtype/data-in-tab, into two <optgroup>s
    // ("this tab" first) when prioritization is active; otherwise a flat list.
    function cloneOpt(src) {
        var o = document.createElement('option');
        o.value = src.value;
        o.textContent = src.textContent;
        o.dataset.company = src.dataset.company || '';
        o.dataset.jobtype = src.dataset.jobtype || '';
        o.dataset.inTab = src.dataset.inTab || '0';
        return o;
    }
    var srcOpts = Array.prototype.slice.call(source.options).filter(function (o) { return o.value; });
    var P = window.CalendarTabPrioritization;
    if (P && P.isActive()) {
        var norm = srcOpts.map(function (o) { return { opt: o, inTab: o.dataset.inTab === '1' }; });
        var parts = P.partition(norm);
        if (parts.thisTab.length) {
            var g1 = document.createElement('optgroup'); g1.label = P.label('this');
            parts.thisTab.forEach(function (n) { g1.appendChild(cloneOpt(n.opt)); });
            select.appendChild(g1);
        }
        if (parts.other.length) {
            var g2 = document.createElement('optgroup'); g2.label = P.label('other');
            parts.other.forEach(function (n) { g2.appendChild(cloneOpt(n.opt)); });
            select.appendChild(g2);
        }
    } else {
        srcOpts.forEach(function (o) { select.appendChild(cloneOpt(o)); });
    }

    select.addEventListener('change', function () {
        var traineeId = parseInt(select.value, 10);
        if (select.value && !isNaN(traineeId)) {
            var chosen = select.options[select.selectedIndex];
            if (P) P.maybeWarnOffTab({ id: traineeId, inTab: chosen && chosen.dataset.inTab === '1' });
            select.disabled = true;
            if (draftCoords) {
                stageDraftAddTrainee(draftCoords.shiftTypeId, draftCoords.date, draftCoords.primaryUserId, traineeId);
                select.remove();
            } else {
                addTraineeToAssignment(assignmentId, traineeId);
            }
        }
    });
    // Dismiss on Escape or when focus leaves — BUT when the searchable-select widget (Phase B) has
    // enhanced this <select>, its own listbox steals focus; do NOT self-destruct then (PF9).
    select.addEventListener('keydown', function (ev) { if (ev.key === 'Escape') select.remove(); });
    select.addEventListener('blur', function () {
        setTimeout(function () {
            if (select.dataset.searchableEnhanced === '1') return; // enhanced widget owns dismissal
            if (select.parentNode) select.remove();
        }, 150);
    });
```

(The `var select = document.createElement('select');` line already existed at ~871 — replace from that line through the end of the change/keydown/blur listeners at ~897. The subsequent `chip.insertAdjacentElement('afterend', select); select.focus();` lines are unchanged.)

- [ ] **Step 7: Build, restart, curl markers**

Run: `dotnet build -c Release` then `git checkout -- packages.lock.json`. Restart the app.
Run: `curl -s http://localhost:5000/js/calendar-tab-prioritization.js | grep -c "maybeWarnOffTab"` → expect `>= 1`.
Run: `curl -s http://localhost:5000/js/calendar-quick-entry.js | grep -c "appendEligGroupHeader"` → expect `>= 1`.

- [ ] **Step 8: Browser verify prioritization + warning `[flag]`**

Launch Playwright (unique `--user-data-dir`; HE run sets the culture cookie). On seeded Oren/Alhut, select the **Geo** tab (prioritize ON). Map each check:
- PRI-1: open each of the three pickers on a Geo shift; each shows a "This tab" group above "Other in molecule", same order.
- PRI-3: pick an "Other" user → assignment SUCCEEDS, exactly one dismissible warning (approved wording, both cultures), not stacked over the success toast, selection not undone.
- PRI-7: fill-handle an off-tab assignment across 5 days → at most ONE warning.
- PRI-4: switch to **All** (or a zero-company tab) → flat list, no warning ever.
- PRI-6: HOME/OFFLINE presence row → no grouping, no warning.
- SEL-6: with the quick-entry cache warm (open the same shift twice), the ordering is still grouped (order applied at render, not baked).

- [ ] **Step 9: Commit**

```bash
git add wwwroot/js/calendar-tab-prioritization.js wwwroot/js/calendar-quick-entry.js \
        wwwroot/js/calendar-bottom-sheet.js wwwroot/js/calendar-inline-edit.js \
        Pages/Calendar/Shifts.cshtml Pages/Shared/_LocalizationScript.cshtml \
        Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat(calendar-tabs): shared picker prioritization utility + off-tab warning (PF8/PF9/UD4)"
```

**Covers:** PRI-1/3/4/5/6/7, PF8 (one shared utility, order at render), PF9 (data-attr copy + blur guard), SEL-6 (no baked order), UD4 (once per tab/session, non-blocking, no toast stacking).

---

### Task 5: Bug-4 auto-refresh via server-rendered grid swap + `ALREADY_ASSIGNED` upgrade (PF3/PF17)

**Design note (root cause, no bespoke partial endpoint):** the existing `triggerCalendarRefresh()` (`calendar-inline-edit.js:11-69`) already IS a server-rendered, tab-filtered grid swap — it re-fetches `window.location.href` (which carries the active `Tab=`), lets the server re-render `ExcelCalendarTable` with **Task 2's** tab filter, and swaps `.excel-calendar` — restoring scroll/focus and dispatching `calendar:grid-refreshed`. It materializes newly-minted instances with full chip anatomy (remove-×, trainee-+, busy/draft) because it is the *same* render the local-edit path already uses. The Bug-4 root cause is purely that the SignalR handler (`handleAssignmentChanged`) calls the broken JSON cell-patch (`refreshCalendarData` → `GetShiftsData` → `updateCalendarCells`, which cannot materialize a new instance and strips chip markup) and only when the broadcast instance id is already in the DOM. Fix = route the realtime handlers through `triggerCalendarRefresh` (debounced) and retire the JSON cell-patch. Building a separate partial endpoint would duplicate the entire `ExcelCalendarTable` render path (violates DRY) for no acceptance-criteria gain — CON-1..5 are all met by the full-grid swap.

**Files:**
- Modify: `wwwroot/js/calendar-inline-edit.js` — export `window.triggerCalendarRefresh`; upgrade the `ALREADY_ASSIGNED` branch in `quickAddShift`
- Modify: `Pages/Calendar/Shifts.cshtml` — inline script: retire `refreshCalendarData`/`updateCalendarCells`/`updateSingleCell`; route realtime through `triggerCalendarRefresh` (debounced), fire regardless of instance id
- Modify: `Pages/Shared/_LocalizationScript.cshtml` — register `Error_ShiftAlreadyAssignedRefreshed`
- Modify: `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx` — `Error_ShiftAlreadyAssignedRefreshed`

**Interfaces:**
- Consumes: `triggerCalendarRefresh()` (global, `calendar-inline-edit.js`); `CalendarRealtime.refresh()` (debounced, `calendar-realtime.js`); the assign JSON contract `{ success:false, error, errorKey:"ALREADY_ASSIGNED" }` (`Table.cshtml.cs:944`, confirmed).
- Produces: `window.triggerCalendarRefresh`; realtime `onAssignmentChanged`/`onCapacityChanged` → server-rendered swap.

- [ ] **Step 1: Add the friendly refreshed message (both cultures) + register**

Grep first: `grep -rn "Error_ShiftAlreadyAssignedRefreshed" Resources/` → expect no hit. Add to `Resources/SharedResources.resx`:

```xml
  <data name="Error_ShiftAlreadyAssignedRefreshed" xml:space="preserve">
    <value>The user is already assigned to this shift. The calendar has been refreshed.</value>
  </data>
```

`Resources/SharedResources.he-IL.resx`:

```xml
  <data name="Error_ShiftAlreadyAssignedRefreshed" xml:space="preserve">
    <value>המשתמש כבר משובץ למשמרת זו. היומן רוענן.</value>
  </data>
```

Register in `_LocalizationScript.cshtml` (append with the Task 4 keys):

```cshtml
        "Error_ShiftAlreadyAssignedRefreshed": @Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["Error_ShiftAlreadyAssignedRefreshed"].Value)),
```

- [ ] **Step 2: Export `triggerCalendarRefresh` + upgrade the `ALREADY_ASSIGNED` branch**

In `wwwroot/js/calendar-inline-edit.js`, right after the `triggerCalendarRefresh` function definition (~69), add the explicit global export (make the cross-file dependency intentional per the function-reference gate):

```javascript
// Explicit global handle so the Shifts page's realtime handler can route SignalR refreshes through the
// SAME server-rendered, tab-filtered grid swap the local-edit path uses (PF3). Classic scripts share
// global scope, but exposing it on window makes the cross-file contract explicit.
window.triggerCalendarRefresh = triggerCalendarRefresh;
```

Then in `quickAddShift`, replace the final `else` of the result handling (~571-573):

```javascript
        } else if (result.errorKey === 'ALREADY_ASSIGNED') {
            // Concurrent-edit collision: another user assigned this person first. The realtime refresh
            // (PF3) re-renders the grid from the server, so tell the user it's already reflected + refresh
            // now to guarantee it (idempotent — the assignment already exists server-side).
            var refreshedMsg = (window.AppLocalizer && window.AppLocalizer.Error_ShiftAlreadyAssignedRefreshed)
                || (getCurrentCulture() === 'he-IL'
                    ? 'המשתמש כבר משובץ למשמרת זו. היומן רוענן.'
                    : 'The user is already assigned to this shift. The calendar has been refreshed.');
            showToast(refreshedMsg, 'info');
            triggerCalendarRefresh();
        } else {
            showToast(result.message || result.error || 'Error', 'error');
        }
```

- [ ] **Step 3: Route realtime through the server-rendered swap; retire the JSON cell-patch**

In `Pages/Calendar/Shifts.cshtml` inline `@section Scripts`, change the `CalendarRealtime.initialize` `onRefresh` (~574) to the server-render swap:

```cshtml
                onRefresh: window.triggerCalendarRefresh,
```

Replace `handleAssignmentChanged` + `handleCapacityChanged` (~646-657) with debounced, always-fire versions (no `if (cell)` guard — CON-1/CON-3):

```cshtml
        // Bug 4 (PF3): route every realtime change through the server-rendered, tab-filtered full-grid
        // swap (triggerCalendarRefresh, via the debounced CalendarRealtime.refresh). Fire even when the
        // broadcast instance id is NOT yet in this DOM — that IS the empty-cell-fill case that left the
        // peer stale. CalendarRealtime.refresh() coalesces bursts (250ms debounce) so a fill-handle storm
        // triggers at most one refresh.
        function handleAssignmentChanged(evt) { window.CalendarRealtime.refresh(); }
        function handleCapacityChanged(evt) { window.CalendarRealtime.refresh(); }
```

Delete the now-dead `refreshCalendarData`, `updateCalendarCells`, `updateSingleCell`, and `escapeHtml` functions (~582-644) — they were the JSON cell-patch path (`GetShiftsData` JSON → skeleton cells) that PF3 retires. (`GetShiftsData` itself stays — Task 2 keeps it compiling/correct — but the Shifts page no longer consumes its JSON.)

- [ ] **Step 4: Build, restart, curl markers**

Run: `dotnet build -c Release` then `git checkout -- packages.lock.json`. Restart the app.
Run: `curl -s http://localhost:5000/js/calendar-inline-edit.js | grep -c "window.triggerCalendarRefresh"` → expect `>= 1`.
Run: `curl -s http://localhost:5000/js/calendar-inline-edit.js | grep -c "ALREADY_ASSIGNED"` → expect `>= 1`.

- [ ] **Step 5: Two-browser concurrent-edit verification `[2-browser]`**

Two Playwright sessions (each a unique `--user-data-dir`), both on Oren/Alhut, same tab, By-Shifts:
- CON-1: A assigns into an EMPTY cell (new instance). Within ~2s B's grid shows the assignee with full chip anatomy (remove ×, trainee +, busy classes) — no reload. Confirm the chip is the real server-rendered chip, not a text skeleton.
- CON-2: B (now stale) assigns the SAME user to the SAME cell → B sees the friendly `Error_ShiftAlreadyAssignedRefreshed` message (verify via console/network that `errorKey:"ALREADY_ASSIGNED"` came back) AND B's grid then truly shows A's assignment.
- CON-3: A on **Geo**, B on **Tacti**; A assigns on a Geo-only shift → B's refresh fires (SignalR group is tab-agnostic) but Tacti's view is unchanged (server re-applies the tab filter on B's URL).
- CON-4: admin deletes B's active tab while B's calendar is open → B's next realtime refresh does not blank/error (Task 1 resolution lands B on All; Task 2 `GetShiftsData` graceful fallback), and B's next navigation lands on a valid tab.
- CON-5: A removes an assignment → B's cell updates (count + chip removed) without reload.

- [ ] **Step 6: Commit**

```bash
git add wwwroot/js/calendar-inline-edit.js Pages/Calendar/Shifts.cshtml \
        Pages/Shared/_LocalizationScript.cshtml Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "fix(calendar-tabs): realtime auto-refresh via server-rendered tab-filtered grid swap + ALREADY_ASSIGNED (PF3/PF17)"
```

**Covers:** CON-1/2/3/4/5, PF3 (root-cause server-rendered refresh, retire JSON cell-patch), PF17 (`ALREADY_ASSIGNED` friendly message + refresh), Bug-4 auto-refresh half.

---

### Task 6: Draft × tab disclosure (PF15) + PF16 polish + full-suite + browser E2E

**Files:**
- Modify: `Pages/Calendar/Shifts.cshtml.cs` — compute `OffViewStagedCount` in `ApplyDraftOverlayAsync`/OnGet
- Modify: `Pages/Calendar/Shifts.cshtml` — surface off-view staged count near the Commit button
- Verify (no change expected): sticky layout offsets survive strip appear/disappear (PF16)

**Interfaces:**
- Consumes: `_tabOfShiftType` (Task 2), `Tab` (Task 1), draft overlay (`ApplyDraftOverlayAsync`).
- Produces: `ShiftsModel.OffViewStagedCount` (`int`).

- [ ] **Step 1: Compute the off-view staged count (DFT-2)**

In `Pages/Calendar/Shifts.cshtml.cs`, add the property near `ActiveDraftId` (~116):

```csharp
    /// <summary>When a draft is active on a real tab, the number of staged cells whose shift type is NOT on
    /// the active tab's effective set — disclosed at commit so the assigner knows off-view cells are included
    /// (the commit is molecule+jobtype-scoped and applies ALL staged cells regardless of the active tab). DFT-2.</summary>
    public int OffViewStagedCount { get; set; }
```

In `ApplyDraftOverlayAsync`, after `var overlay = await _draftService.GetOverlayAsync(draft.Id);` and the `if (overlay.Count == 0) return live;` guard, add:

```csharp
        // DFT-2 disclosure: count staged cells whose shift type is off the active tab (only meaningful on a
        // real tab; on "All" everything is on-view). _tabOfShiftType is populated by the by-user builder;
        // fall back to a direct map lookup so shift-mode also discloses correctly.
        if (Tab.HasValue)
        {
            var tabMap = _tabOfShiftType.Count > 0
                ? _tabOfShiftType
                : await _tabService.GetShiftTypeTabMapAsync(moleculeId, jobTypeId);
            OffViewStagedCount = overlay.Count(o =>
                !(tabMap.TryGetValue(o.ShiftTypeId, out var tset) && tset.Contains(Tab.Value)));
        }
```

- [ ] **Step 2: Surface the count at the Commit control**

In `Pages/Calendar/Shifts.cshtml`, in the Draft controls block where `commitDraftBtn` renders (~196-199), add a disclosure line after the commit button:

```cshtml
                        <button type="button" class="btn btn-primary" id="commitDraftBtn" data-draft-id="@Model.ActiveDraftId">
                            <span class="btn-icon"><icon name="check" /></span>
                            @Localizer["Draft_Commit"]
                        </button>
                        @if (Model.OffViewStagedCount > 0)
                        {
                            <span class="cal-draft-offview-note" title="@Localizer["Draft_OffViewStagedHint"]">
                                @string.Format(Localizer["Draft_OffViewStagedCount"].Value, Model.OffViewStagedCount)
                            </span>
                        }
```

Add the two resx keys (grep first). `Resources/SharedResources.resx`:

```xml
  <data name="Draft_OffViewStagedCount" xml:space="preserve"><value>+{0} staged on other tabs</value></data>
  <data name="Draft_OffViewStagedHint" xml:space="preserve"><value>Committing applies all staged changes, including those not shown on this tab.</value></data>
```

`Resources/SharedResources.he-IL.resx`:

```xml
  <data name="Draft_OffViewStagedCount" xml:space="preserve"><value>+{0} מוכנים בלשוניות אחרות</value></data>
  <data name="Draft_OffViewStagedHint" xml:space="preserve"><value>שמירת הטיוטה מחילה את כל השינויים המוכנים, כולל אלה שאינם מוצגים בלשונית זו.</value></data>
```

- [ ] **Step 3: Build + full serialized suite**

Run: `dotnet build -c Release` then `git checkout -- packages.lock.json`.
Run: `dotnet test ShiftManager.Tests -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
Expected: full suite GREEN (including `ShiftTabServiceTests`, `ShiftsTabResolutionTests`, `DraftOverlayRenderTests`, `ShiftsUserRowShiftCountTests`, `RoleTemplateAutoGrantTests`, `LocalizationDrift`). Record the pass count. Run `git checkout -- packages.lock.json` again afterward.

- [ ] **Step 4: Browser E2E — draft, sticky/RTL, regressions**

Playwright (unique `--user-data-dir`; run once EN, once HE with the culture cookie):
- DFT-1: enter a shifts draft on **Geo**, stage assignments, switch to **Tacti** and back → staged Geo cells still visible.
- DFT-2: with staged cells on off-Geo shift types, the Commit control shows the "+N staged on other tabs" note; Commit applies ALL staged cells.
- DFT-3: a staged synthetic cell on a shift type shared by two tabs renders on both tabs' By-Shifts.
- LOC-2/PF16: in HE, the strip pills show `NameHe`, a Latin tab name renders unscrambled (bidi `<bdi>`), and switching jobtype **Alhut → BR** removes the strip with **no** sticky-header/layout corruption (the grid's sticky corner + header column still stick); switching back restores the remembered tab.
- REG-1: ShiftGroupings still band calendar rows (only eligibility restriction removed — Phase C); `IsInShiftGrouping` display unchanged.
- REG-2: Chores/On-Call/Overview/Team have no strip, no prioritization; their pickers behave as before.
- REG-4: a (molecule, jobtype) that never had tabs behaves as v5.2.9 (strip absent, All-equivalent everywhere, quick-entry/drafts unchanged).

- [ ] **Step 5: Commit**

```bash
git add Pages/Calendar/Shifts.cshtml.cs Pages/Calendar/Shifts.cshtml \
        Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat(calendar-tabs): draft off-view staged disclosure + sticky/bidi polish (PF15/PF16)"
```

**Covers:** DFT-1/2/3, PF15, PF16 (bidi + sticky survival + Tech/null-jobtype strip), LOC-2, REG-1/2/4, full serialized suite (REG-5).

---

## Self-review (vs STR / PRI / LTM / CON / DFT-1..3 + PF3/4/5/7/8/9/10/15/16)

| Item | Task | Status |
|------|------|--------|
| STR-1 strip = Geo+Tacti, culture names | 1 | ✓ |
| STR-2 By-Shifts Geo types + HOME/OFFLINE | 2 | ✓ (Step 6 union) |
| STR-3 dual-tab shift on both tabs | 2 | ✓ (map = set of tabs) |
| STR-4 null-jobtype shared never invisible | 2 | ✓ (PF7 union) |
| STR-5 By-User roster = companies ∪ cross-over | 2 | ✓ |
| STR-6 SET-based ghosting | 2 | ✓ (`ShiftIsOnActiveTab`) |
| STR-7 zero-tab → no strip | 1 | ✓ |
| STR-8 `?Tab=0`/foreign → valid load | 1 | ✓ (→ All) |
| STR-9 GetShiftsData parity | 2 | ✓ (join + empty=all) |
| PRI-1..7 grouping + warning | 3,4 | ✓ |
| LTM-1 per-(user,mol,jobtype) memory | 1 | ✓ |
| LTM-2 deleted → fallback (All, per UD2) | 1 | ✓ (documented reconciliation) |
| LTM-3 jobtype switch drops Tab | 1 | ✓ (`updateCalendarFilters`) |
| CON-1..5 realtime | 5 | ✓ |
| DFT-1/2/3 | 2,6 | ✓ |
| PF3 server-rendered refresh | 5 | ✓ (reuse `triggerCalendarRefresh`, retire JSON patch) |
| PF4 all `ShiftType.TabId` readers rewired | 2 | ✓ (BuildShift/User, `_tabOfShiftType`, `ShiftIsOnActiveTab`, GetShiftsData) |
| PF5 server inTab (membership-aware) | 3 | ✓ |
| PF7 HOME/OFFLINE + null-jobtype always render | 2 | ✓ |
| PF8 one shared utility, order at render | 4 | ✓ |
| PF9 trainee picker data-attrs + blur guard | 4 | ✓ |
| PF10 tab-resolution rewrite | 1 | ✓ |
| PF15 draft commit-all + off-view disclosure | 6 | ✓ |
| PF16 sticky survival + bidi + Tech path | 1,6 | ✓ |
| LOC-2 pills NameHe/En + bidi | 1 | ✓ |

**Documented reconciliation (LTM-2):** Post-UD2, all last-tab fallbacks (no preference, dangling, deleted-tab-via-FK-SetNull) resolve to the always-valid, non-empty **All** pseudo-tab rather than the checklist's literal "first-available". This satisfies LTM-2's substantive requirement (no error, valid non-empty landing) and is strictly safer post-UD2. Flagged for reviewer sign-off; reversing to "first-available" would require Phase D to preserve a dangling pref id (not FK-SetNull) so resolution can detect deletion.

**Out-of-Phase-E dependencies noted inline:** Task 1 Step 6/10 depends on Task 3's `GetInTabUserIdsAsync` (stub-then-implement if executing strictly in order); the searchable-select widget (Phase B) supplies `data-searchable`/`searchableEnhanced` — Task 4's picker grouping degrades gracefully to native `<optgroup>`s without it. `ALREADY_ASSIGNED` client branch (Task 5) supersedes any Phase-A interim version.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-21-calendar-tabs-phase-E-calendar-integration.md`. Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks.
2. **Inline Execution** — execute tasks in this session with checkpoints.

Which approach?
