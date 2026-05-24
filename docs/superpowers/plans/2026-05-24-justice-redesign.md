# Justice Page Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Overhaul the `/Admin/Analytics` "Justice" page into a drill-down, role-capped workload-fairness dashboard with a share donut + fairness gauge, switchable fairness basis, % of total, 6-month sparklines, and A/B month comparison — reusing existing `JusticeService` math, extending it safely, and fixing three latent bugs.

**Architecture:** Read-only `JusticeService` (stateless, immutable `JusticeQuery` record) gains: a `FairnessBasis`, basis-aware Expected, per-row shares, `IsDrillable`, sparkline series, and an A/B comparison method. The page becomes one parameterized level-renderer (Area / Companies-in-Molecule / Users-in-Company / Users-in-Molecule). Authorization for "see all siblings, drill only your own" reuses the existing grant cascade (read scope) with a `drillableIds` subset (no new grants, no new query-filter bypass).

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core + SQLite (air-gapped Windows/IIS), xUnit + real-SQLite test fixtures, vanilla JS + inline SVG (no chart libs), bilingual resx (en + he-IL), light/dark + Hebrew RTL.

**Reference mockups (verified, offline):** `.superpowers/brainstorm/design-drafts/converged-d.html` (companies-in-molecule, canonical), `level-area.html`, `level-users-in-company.html`, `level-users-in-molecule.html`. The spec: `docs/superpowers/specs/2026-05-24-justice-redesign-design.md`.

**Global rules:** Before any `dotnet build`/`test`, ensure the app is NOT running (locked-exe rule) — `taskkill /F /IM ShiftManager.exe 2>$null` or confirm with user. Commit after each green task. Token-only CSS colors. All strings localized to BOTH resx files.

---

## File Structure

**Modify (service/model layer):**
- `Models/JusticeEnums.cs` — add `FairnessBasis { BySize=0, EqualShare=1 }`.
- `Services/JusticeViewModels.cs` — extend `JusticeQuery` (+`FairnessBasis Basis`) and `JusticeRow` (+`IsDrillable`, `+ActualShare`, `+ExpectedEqual`, `+ExpectedShareBySize`, `+ExpectedShareEqual`, `+DeviationPercentEqual`, `+BandEqual`, `+Sparkline`, `+DeltaVsCompare`); add `JusticeComparisonViewModel`.
- `Services/IJusticeService.cs` — add `GetSparklineSeriesAsync`, `GetComparisonViewAsync`.
- `Services/JusticeService.cs` — bug fixes (#1 targets IgnoreQueryFilters, #2 IsHome exclusion, #3 N+1 batch); basis-aware Expected; share computation; drillable marking; sparkline + comparison methods.
- `Services/IGrantService.cs` + `Services/GrantService.cs` — add `GetAccessibleAreaIdsForGrantAsync` (needed for clean Area read/drill distinction).

**Modify (page/UI layer):**
- `Pages/Admin/Analytics.cshtml.cs` — level navigation, `drillableIds`, read-vs-drill auth, basis precompute, A/B wiring, CSV columns.
- `Pages/Admin/Analytics.cshtml` — new layout (control bar, hero two charts, 8-col table, breadcrumb, drill-into, ribbon, reminder), one partial per row entity via a shared `_JusticeLevel` partial.
- `wwwroot/css/justice.css` — port from `converged-d.html`.
- `wwwroot/js/justice.js` — interactions (work-type, basis toggle, presets+A/B, drill-into, group-by).

**Modify (localization):**
- `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx` — new `Justice_*` keys.
- `Pages/Shared/_LocalizationScript.cshtml` — JS-visible keys.

**Create (tests):**
- `ShiftManager.Tests/UnitTests/Services/JusticeServiceFairnessBasisTests.cs`
- `ShiftManager.Tests/UnitTests/Services/JusticeServiceSharesAndDrillTests.cs`
- `ShiftManager.Tests/UnitTests/Services/JusticeServiceSparklineComparisonTests.cs`
- `ShiftManager.Tests/UnitTests/Services/JusticeServiceBugfixTests.cs`
- `ShiftManager.Tests/UnitTests/Pages/JusticeAuthorizationTests.cs`

---

## Phase A — Service & model foundation (TDD)

Use the existing real-SQLite fixture helper `Helpers/SqliteDbContextFixture.cs` for all DB-touching tests (NOT `UseInMemoryDatabase` — the SQL translator must run; see project memory). Seed parent rows (Project/Area/Molecule/Company/Users) explicitly.

### Task A1: Fix bug #1 — JusticeTargets must be read with IgnoreQueryFilters

**Files:**
- Modify: `Services/JusticeService.cs:62` and `:694`
- Test: `ShiftManager.Tests/UnitTests/Services/JusticeServiceBugfixTests.cs`

- [ ] **Step 1: Write the failing test.** Seed two companies in different tenants (CompanyId 1 and 2) under one molecule; seed a Company-scoped `JusticeTarget` override for company 2 (Chore, ExpectedCount high); set the ambient tenant to company 1 (so the query filter would hide company 2's override). Query `GetJusticeViewAsync` at `Molecule` scope / `CompaniesInMolecule` level / WorkType=Chore. Assert company 2's row uses the override's Expected, not the global default.

```csharp
[Fact]
public async Task GetJusticeView_CompanyTargetOverride_VisibleAcrossTenant()
{
    await using var fx = new SqliteDbContextFixture(); // FKs ON, seed parents
    // seed: Area 1, Molecule 1; Company 1 (tenant), Company 2 (other tenant); a few active users each
    // seed global chore target (1/month) + company-2 override (10/month)
    var svc = fx.CreateJusticeService(currentTenant: 1);
    var q = new JusticeQuery(JusticeScope.Molecule, 1, new DateOnly(2026,5,1), new DateOnly(2026,5,31),
                             JusticeWorkType.Chore, true, JusticeLevel.CompaniesInMolecule);
    var view = await svc.GetJusticeViewAsync(q);
    var c2 = view.Rows.Single(r => r.Id == 2);
    Assert.True(c2.Expected >= 9m); // override (≈10) applied, not global (≈users*1)
}
```

- [ ] **Step 2: Run, verify it fails** (override hidden → falls to global → Expected wrong). `dotnet test --filter JusticeServiceBugfixTests.GetJusticeView_CompanyTargetOverride_VisibleAcrossTenant`
- [ ] **Step 3: Fix.** In `JusticeService.cs` change both target loads:
```csharp
// line ~62 and ~694 — SECURITY: scope already gated at page-model boundary; cross-company
// admins must see company-scoped overrides for all companies in the viewed scope.
var targets = await _db.JusticeTargets.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
```
Also update `GetTargetsAsync` (line 55) identically.
- [ ] **Step 4: Run, verify pass.**
- [ ] **Step 5: Commit** `fix(justice): read JusticeTargets with IgnoreQueryFilters so cross-company overrides apply`

### Task A2: Fix bug #2 — exclude all HOME variants, not just KEY_HOME

**Files:**
- Modify: `Services/JusticeService.cs` (CountActualPerUserAsync ~354, SumShiftCapacityPerCompanyAsync ~434, BuildShiftHolesAsync ~647)
- Test: `JusticeServiceBugfixTests.cs`

- [ ] **Step 1: Write failing test.** First inspect `Models/ShiftType.cs` to confirm the `IsHome` property and the `KEY_HOME_PM`/`KEY_HOME_AM` constants. Seed shift assignments of type `HOME_PM` for a user; query Actual with `ExcludeExemptShifts=true`; assert the HOME_PM shift is excluded (Actual count excludes it).
- [ ] **Step 2: Run, verify fails** (HOME_PM counted).
- [ ] **Step 3: Fix.** Replace the three raw key comparisons. Prefer an EF-translatable predicate using the same set the `IsHome` property uses. If `ShiftType.IsHome` is a computed C# property (not mapped), replace with an explicit key set:
```csharp
// excludes HOME, HOME_AM, HOME_PM, OFFLINE — mirror ShiftType.IsHome semantics
shiftQ = shiftQ.Where(a => a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME
                         && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME_AM
                         && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME_PM
                         && a.ShiftInstance.ShiftType.Key != ShiftType.KEY_OFFLINE);
```
Apply the identical change in all three locations. (If `KEY_HOME_AM/PM` don't exist, use whatever the home-unification spec defined; verify in `ShiftType.cs`.)
- [ ] **Step 4: Run, verify pass.**
- [ ] **Step 5: Commit** `fix(justice): exclude HOME_AM/HOME_PM from exempt-shift filtering`

### Task A3: FairnessBasis enum + basis-aware Expected

**Files:**
- Modify: `Models/JusticeEnums.cs`, `Services/JusticeViewModels.cs` (JusticeQuery), `Services/JusticeService.cs`
- Test: `JusticeServiceFairnessBasisTests.cs`

- [ ] **Step 1: Add enum** to `JusticeEnums.cs`:
```csharp
/// <summary>How "Expected" is derived. BySize = capacity/target-weighted (default, today's behavior).
/// EqualShare = total work ÷ number of rows (every unit expected to carry the same).</summary>
public enum FairnessBasis { BySize = 0, EqualShare = 1 }
```
- [ ] **Step 2: Extend JusticeQuery** (additive, default keeps old behavior): add `FairnessBasis Basis = FairnessBasis.BySize` as the last positional param with a default; update existing constructor call sites (Analytics page, in-context handlers) — they keep default.
- [ ] **Step 3: Write failing test.** Seed a molecule with 3 companies of unequal headcount; total chores known. Assert: under `BySize` expected differs per company (capacity/target-weighted); under `EqualShare` each company's Expected == totalActual/3 (within rounding) regardless of size.
- [ ] **Step 4: Run, verify fails.**
- [ ] **Step 5: Implement.** In `GetJusticeViewAsync`, after building `rows`, if `q.Basis == EqualShare`, recompute each row's `Expected` as `Σ rows.Actual / rows.Count` (equal split of the actual total — the canonical "everyone equal" reading) and recompute `(DeviationPercent, Band)` via `ComputeDeviation`. Keep `BySize` path untouched. Factor a small helper `ApplyEqualShareBasis(List<JusticeRow>)`. NOTE: equal-share uses the *actual total* as the pool to split (so deviation = distance from the mean), matching the mockup's "≈ equal = total ÷ N".
- [ ] **Step 6: Run, verify pass. Commit** `feat(justice): switchable fairness basis (by-size vs equal-share)`

### Task A4: Per-row shares + precompute BOTH bases for client toggle

**Files:**
- Modify: `Services/JusticeViewModels.cs` (JusticeRow), `Services/JusticeService.cs`
- Test: `JusticeServiceSharesAndDrillTests.cs`

- [ ] **Step 1: Extend `JusticeRow`** with additive init-only props (keep positional ctor intact; add as `init` properties so existing `new JusticeRow(...)` calls compile):
```csharp
public decimal? ActualShare { get; init; }          // Actual / Σ Actual  (basis-independent)
public decimal ExpectedBySize { get; init; }        // expected under BySize
public decimal ExpectedEqual { get; init; }         // expected under EqualShare
public decimal? ExpectedShareBySize { get; init; }
public decimal? ExpectedShareEqual { get; init; }
public decimal? DeviationPercentEqual { get; init; }
public DeviationBand BandEqual { get; init; }
```
(Existing `Expected`/`DeviationPercent`/`Band` stay = the ACTIVE basis for backward compat with the drawer.)
- [ ] **Step 2: Write failing test.** Assert `ActualShare` sums to ~1.0 across rows; `ExpectedShareBySize` populated; for a known fixture, `ExpectedEqual == totalActual/N`.
- [ ] **Step 3: Run, verify fails.**
- [ ] **Step 4: Implement** `ComputeSharesAndBothBases(rows)` called in `GetJusticeViewAsync` before sorting: compute `Σactual`, set `ActualShare`; compute both bases' expected (reuse Task A3 helper for equal, existing values for by-size); set both expected-shares (expected/Σexpected for that basis); set both deviation/band pairs. Populate `Expected/DeviationPercent/Band` from `q.Basis`.
- [ ] **Step 5: Run, verify pass. Commit** `feat(justice): per-row actual/expected shares + dual-basis precompute`

### Task A5: IsDrillable + comparison-tier marking + grant area helper

**Files:**
- Modify: `Services/JusticeViewModels.cs` (`JusticeRow.IsDrillable`), `Services/IGrantService.cs`, `Services/GrantService.cs`, `Services/JusticeService.cs`, `Pages/Admin/Analytics.cshtml.cs`
- Test: `JusticeServiceSharesAndDrillTests.cs`, `Pages/JusticeAuthorizationTests.cs`

- [ ] **Step 1: Add `bool IsDrillable { get; init; } = true;`** to `JusticeRow`.
- [ ] **Step 2: Add grant helper.** In `IGrantService` + `GrantService` add `Task<List<int>> GetAccessibleAreaIdsForGrantAsync(int userId, string grantKey)` mirroring the molecule helper but resolving to area IDs (an area is accessible if the user can access ≥1 molecule in it OR has an area/project-scoped grant). Follow the exact cascade pattern of `GetAccessibleMoleculeIdsForGrantAsync` (read it first).
- [ ] **Step 3: Write failing auth test.** A user whose grant scope = molecule 1 (in area 1 with molecules 1,2,3). At `MoleculesInArea` view of area 1: assert all 3 molecule rows are returned (read), but only molecule 1 has `IsDrillable == true`; molecules 2,3 have `IsDrillable == false`.
- [ ] **Step 4: Run, verify fails.**
- [ ] **Step 5: Implement.** Page model computes `drillableIds` = the user's own subtree at the child level (e.g., accessible molecule IDs for the Area view; accessible company IDs for the Molecule view; for user levels every row is terminal so drillable is irrelevant). Pass `IReadOnlyCollection<int> drillableChildIds` into a new overload `GetJusticeViewAsync(q, drillableChildIds, ct)` (keep the old 1-arg overload delegating with `null` = all drillable). In the row builders, set `IsDrillable = drillableChildIds == null || drillableChildIds.Contains(row.Id)`.
- [ ] **Step 6: Handler-level drill rejection.** In `Analytics.cshtml.cs`, extend `UserCanAccessScopeAsync` so a *drill* request (navigating into a child scopeId) is rejected unless that scopeId ∈ the user's drillable set — even if it's readable for comparison. Add `JusticeAuthorizationTests` asserting a capped user gets `Forbid` when requesting `?scope=Molecule&scopeId=2` (a sibling they may rank but not drill).
- [ ] **Step 7: Run, verify pass. Commit** `feat(justice): comparison-tier visibility (read siblings, drill own subtree)`

### Task A6: 6-month sparkline series (single GROUP BY)

**Files:**
- Modify: `Services/IJusticeService.cs`, `Services/JusticeService.cs`, `Services/JusticeViewModels.cs` (`JusticeRow.Sparkline`)
- Test: `JusticeServiceSparklineComparisonTests.cs`

- [ ] **Step 1: Add `IReadOnlyList<decimal>? Sparkline { get; init; }`** to `JusticeRow` and `Task<Dictionary<int, List<decimal>>> GetSparklineSeriesAsync(JusticeQuery baseQuery, int buckets, CancellationToken ct)` to the interface.
- [ ] **Step 2: Write failing test.** Seed chores for a user across 3 distinct months; call sparkline with buckets=6; assert the returned list length==6 and the last 3 buckets reflect the seeded monthly counts in order.
- [ ] **Step 3: Run, verify fails.**
- [ ] **Step 4: Implement.** Compute `buckets` consecutive month windows ending at `baseQuery.PeriodEnd`'s month. Run ONE grouped query per work type over the full span using SQLite-translatable date grouping. Because EF+SQLite can't always translate `STRFTIME`, group in two steps: pull `(rowKey, Date)` rows for the span via the existing `IgnoreQueryFilters` predicates, then bucket in memory by month index. Return `rowId -> [c1..c6]`. Keep it O(1) queries per work type. Populate `JusticeRow.Sparkline` in `GetJusticeViewAsync` only when requested (add a `bool includeSparklines` to the new overload to avoid cost on the drawer path).
- [ ] **Step 5: Run, verify pass. Commit** `feat(justice): 6-month per-row sparkline series`

### Task A7: A/B period comparison

**Files:**
- Modify: `Services/IJusticeService.cs`, `Services/JusticeService.cs`, `Services/JusticeViewModels.cs` (add `JusticeComparisonViewModel`, `JusticeRow.DeltaVsCompare`)
- Test: `JusticeServiceSparklineComparisonTests.cs`

- [ ] **Step 1: Add DTO + interface method.**
```csharp
public sealed record JusticeComparisonViewModel(JusticeViewModel Primary, JusticeViewModel Compare,
    IReadOnlyDictionary<int, decimal> ActualDeltaByRowId); // Primary.Actual - Compare.Actual per row id
// IJusticeService:
Task<JusticeComparisonViewModel> GetComparisonViewAsync(JusticeQuery periodA, JusticeQuery periodB,
    IReadOnlyCollection<int>? drillableChildIds, CancellationToken ct = default);
```
- [ ] **Step 2: Write failing test.** Seed chores so a company has 10 in May, 6 in April. Build comparison (A=May, B=April). Assert `ActualDeltaByRowId[companyId] == 4`.
- [ ] **Step 3: Run, verify fails.**
- [ ] **Step 4: Implement.** `Task.WhenAll` the two `GetJusticeViewAsync` calls (share nothing — both stateless); compute per-row Actual deltas; populate `Primary.Rows[i].DeltaVsCompare`. Return the DTO.
- [ ] **Step 5: Run, verify pass. Commit** `feat(justice): A/B period comparison with per-row deltas`

### Task A8: Fix bug #3 — batch the per-company chore-hole loop

**Files:**
- Modify: `Services/JusticeService.cs` (`BuildChoreHolesAsync` ~700)
- Test: `JusticeServiceBugfixTests.cs`

- [ ] **Step 1: Write characterization test.** Seed a molecule with 4 companies; assert `GetInContextViewAsync` (WorkType=Chore) returns the same WhereToFocus holes as before the refactor (golden values) — this guards behavior while we change the query shape.
- [ ] **Step 2: Run, verify pass currently** (characterization baseline).
- [ ] **Step 3: Refactor.** Replace the `foreach (cid in companyIds) BuildUsersInCompanyAsync(...)` loop with a single pass: compute per-user actuals for ALL companies at once (`CountActualPerUserAsync(q, companyIds)` already supports an array), load per-user company + headcount once, resolve per-user expected per company, build the underloaded set in memory — no per-company round-trips.
- [ ] **Step 4: Run, verify the characterization test still passes** (behavior unchanged, query count reduced).
- [ ] **Step 5: Commit** `perf(justice): batch chore-hole building to remove per-company N+1`

---

## Phase B — Page & front-end

### Task B1: Port `justice.css` from the verified mockup

**Files:**
- Modify/replace: `wwwroot/css/justice.css`
- Source: `.superpowers/brainstorm/design-drafts/converged-d.html` (canonical) — extract the `<style>` block.

- [ ] **Step 1:** Read the `<style>` block of `converged-d.html`. Port it into `justice.css`, converting embedded hex back to the project `var(--token)` references from `wwwroot/css/tokens.css` (the mockup embedded hex for offline standalone; production must use tokens so dark mode + theming work). Keep these verified fixes: `@keyframes fadeUp { to { ... transform: none; } }`; `.controls-shell { position: relative; z-index: 40; }`; `[data-theme="dark"] .dev-band-softover { color: var(--warning) !important; }`.
- [ ] **Step 2:** Remove the mockup's standalone `:root` token block and the in-mockup theme-toggle button styles (production uses the global theme system + `tokens.css`).
- [ ] **Step 3: Manual check** (no test): the file references only existing tokens; no `#hex` literals remain except inside SVG gradients that must be tokens too. Commit `style(justice): production justice.css ported from approved mockup`.

### Task B2: Rewrite `Analytics.cshtml` + add `_JusticeLevel` partial

**Files:**
- Modify: `Pages/Admin/Analytics.cshtml`
- Create: `Pages/Shared/_JusticeLevel.cshtml` (the reusable hero+table block, driven by the view model)

- [ ] **Step 1:** Build the control bar (work-type pill rail, fairness-basis toggle, time presets + A/B pickers, "Drill Into ▾" dropdown), breadcrumb, hero (server-rendered SVG **fairness gauge** + **share donut** from `JusticeViewModel`), the 8-column table (Name · Actual · % of total · Expected[+basis tag] · Expected % of total · Deviation pill[shape+color] · vs other month · Trend sparkline), distribution ribbon, reminder card. Mirror the markup/classes from `converged-d.html` but emit values from the model and all text via `<loc key="Justice_*">`.
- [ ] **Step 2:** Render `IsDrillable==false` rows with the locked treatment (dimmed + 🔒 + tooltip, no drill arrow). Render avatars (via `<avatar src name size>`) at user levels; company tag at users-in-molecule.
- [ ] **Step 3:** Server-render the two SVGs from data (gauge arc from spread index/severity; donut arcs from each row's `ActualShare`, colored by band). Provide `data-*` attributes carrying both-basis values so JS can re-render on toggle.
- [ ] **Step 4: Manual render check** deferred to Playwright (Task D1). Commit `feat(justice): new Analytics page layout + _JusticeLevel partial`.

### Task B3: `Analytics.cshtml.cs` — navigation, auth, basis, A/B, CSV

**Files:**
- Modify: `Pages/Admin/Analytics.cshtml.cs`

- [ ] **Step 1:** Add bound props: `Basis` (FairnessBasis), `CompareFrom`/`CompareTo` (optional A/B period). Compute `drillableChildIds` per current level from the grant helpers. Call the new `GetJusticeViewAsync(q, drillableChildIds, includeSparklines:true, ct)` (and `GetComparisonViewAsync` when compare period set).
- [ ] **Step 2:** Adaptive entry: pick the default level/scope from the broadest accessible (ceiling = Area in v1). Keep `NormalizeScopeAndLevelDefaults`. Add the Users-in-Molecule branch (Scope=Molecule, Level=new `UsersInMolecule`). **(Add `JusticeLevel.UsersInMolecule` enum value + a `BuildUsersInMoleculeAsync` builder in the service — pools users across the molecule's companies; mirror `BuildUsersInCompanyAsync` but over all company IDs in the molecule; each row carries a company tag via a new optional `JusticeRow.GroupLabel`.)**
- [ ] **Step 3:** Extend `UserCanAccessScopeAsync` for read-vs-drill (Task A5 step 6) and for the Molecule users-pool.
- [ ] **Step 4:** Extend CSV export with the new columns (% of total, expected, expected %, deviation, basis).
- [ ] **Step 5: Commit** `feat(justice): page-model level navigation, capped auth, basis + A/B, CSV`

### Task B4: `justice.js` — interactions

**Files:**
- Modify/replace: `wwwroot/js/justice.js`

- [ ] **Step 1:** Implement: work-type pill select (navigates with `?workType=`), fairness-basis toggle (client-side: swap displayed Expected/Expected%/Deviation/Band + re-color donut using the `data-*` dual-basis values, no round-trip), time presets + A/B pickers (navigate with query params), "Drill Into ▾" dropdown (open/close, outside-click, Esc, ARIA, navigates with `?scope/scopeId/level`), group-by toggle (users-in-molecule). Respect `prefers-reduced-motion`. No external libs.
- [ ] **Step 2: Commit** `feat(justice): justice.js interactions (basis toggle, drill-into, presets)`

---

## Phase C — Localization

### Task C1: Bilingual strings

**Files:**
- Modify: `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx`, `Pages/Shared/_LocalizationScript.cshtml`

- [ ] **Step 1:** Enumerate every new visible string (column headers, control labels, basis labels "by size"/"equal share", "Comparison only", "Your molecule", drill-into items, banner text, group-by labels, donut/gauge labels, callouts). Add a `Justice_*` key for each to BOTH resx files with correct Hebrew (mirror tone of existing `Justice_*` keys). Add JS-visible ones to `_LocalizationScript.cshtml`.
- [ ] **Step 2: Build** to confirm resx compiles. **Commit** `i18n(justice): bilingual strings for the redesigned page`.

---

## Phase D — QA, review, validation

### Task D1: Playwright front-end tests

**Files:**
- Create: a Playwright spec (follow the project's webapp-testing skill) OR use the MCP browser tools directly to verify the running app's `/Admin/Analytics`.

- [ ] **Step 1:** Launch the app (ensure built, not locked). Navigate to `/Admin/Analytics` as a seeded admin. Verify: page renders with no console errors; hero gauge + donut present; 8 columns populated incl. `% of total`; fairness toggle updates table + donut; drill-into opens above charts; deviation pills carry a shape icon (not hue-only); switch to dark theme — no dark-on-dark; switch to Hebrew — RTL correct. Capture screenshots.
- [ ] **Step 2:** Drill flow: Area → Oren → company → users; and the Users-in-Molecule branch. Verify breadcrumb + locked sibling rejects drill.
- [ ] **Step 3: Commit** any fixes; record results.

### Task D2: Localization QA pass

- [ ] **Step 1:** Run the `localization-qa-inspector` agent over the new page + partial + js. Fix any hardcoded strings / missing he-IL / RTL issues it finds. Commit fixes.

### Task D3: Code review + full suite + final validation

- [ ] **Step 1:** `taskkill` app if running; `dotnet build`; `dotnet test` — entire suite green (existing + new).
- [ ] **Step 2:** Run `feature-dev:code-reviewer` over the diff; address high-confidence findings.
- [ ] **Step 3:** Final strongest-model validation pass ("Did we do everything correctly?") against the spec's acceptance checklist.
- [ ] **Step 4:** Produce the production-readiness report with an explicit Deferred Items list (project-level entry; standalone trend chart; second donut ring).

---

## Self-review notes (gaps closed)
- Added `JusticeLevel.UsersInMolecule` + builder + `GroupLabel` (was implied by the branch, now an explicit task in B3).
- Added `GetAccessibleAreaIdsForGrantAsync` (A5) — required for clean Area read/drill.
- Backward compatibility: all `JusticeRow` additions are `init` props with defaults; `GetJusticeViewAsync` keeps a 1-arg overload so `GetInContextViewAsync` and the calendar drawers are unaffected.
- Dual-basis precompute (A4) feeds the client-side toggle (B4) — no extra round-trips, works air-gapped.
