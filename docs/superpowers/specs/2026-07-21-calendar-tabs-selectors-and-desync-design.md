# Calendar Tabs, Selector Relevance, Searchable Dropdowns & Concurrent-Edit Desync — Design

- **Date:** 2026-07-21
- **Status:** Draft — awaiting user review
- **Branch of origin:** `dev`
- **Delivery:** Single combined delivery (user decision), implemented in phases.

---

## 0. Origin & summary

**The bug that started this:** a Lead in company *City* could not select a trainee from company *Tzafona* on `/Calendar/Shifts`, even though both are in the **same molecule** and the trainee is the **same job type**, and the shifts calendar is conceptually and literally **molecule-scoped**. The root cause: the trainee picker (and, it turns out, the assignment pickers) are scoped **narrower than the molecule**. The only reason not to simply make every selector full-molecule is that **some companies are irrelevant** to a given lead's current context and pollute the list.

**The resolution:** make all assignment selectors **molecule + job-type wide by default** (fixing the bug), and introduce **Tabs** as an optional *relevance* layer — a tab filters the calendar *views* and *prioritizes* (never restricts) users in the pickers.

This spec covers four items shipped together:

| # | Item | Size | Nature |
|---|------|------|--------|
| ① | **Bug 2** — Quick Entry swallows clicks on the trainee picker | S | Bug fix |
| ② | **Bug 4** — Concurrent-edit desync + raw "already assigned" error | S | Bug fix |
| ③ | **Feature 3** — Searchable/type-to-filter `<select>` dropdowns (RTL+LTR) | M | Feature |
| ④ | **Tabs** — job-type-scoped calendar tabs + molecule-wide selector baseline + prioritization (subsumes **Bug 1**, the origin bug) | L | Feature |

---

## 1. Decisions made (delegated by user — veto any at review)

- **D1 — Molecule+jobtype baseline for all selectors.** Quick-entry, bottom-sheet (`#bottom-sheet-user-select`), and trainee pickers all default to *every user of the current job type in the molecule*. This removes today's **hard `ShiftGrouping` company-filter** from the assignment picker (`ShiftAssignmentService.GetEligibleUsersForShiftTypeAsync`). Groupings keep their other roles (calendar row-banding, `IsInShiftGrouping`). This is the origin-bug fix.
- **D2 — Extend `ShiftTab`, reshape schema freely.** No production tab data to preserve (user confirmed all test data); no complex migration. We reset/replace test tab data.
- **D3 — View-filter vs prioritization are separate.** A selected tab **always** filters the *views*; the per-tab **toggle** only governs selector *prioritization + warning*.
- **D4 — Pickers never hard-restrict.** Tab-company users sort first; all molecule+jobtype users remain selectable; picking an off-tab user shows a **non-blocking, dismissible warning**.
- **D5 — New grant + page location.** New grant `ManageCalendarTabs` (molecule-scoped, Lead-and-above); management page at `/Admin/Organization/Tabs`, added to the **הגדרות/Settings** nav subgroup, label EN `Tabs` / HE `לשוניות`.
- **D6 — Trainee job type.** Reuse the existing `AppUser.JobTypeId` field (no schema column); populate trainee job types in the seed; the trainee picker scopes to molecule trainees whose job type matches the calendar's job type (null-job-type trainees are treated as universally relevant to avoid re-hiding).

---

## 2. Item ④ — Tabs feature

### 2.1 Data model (extends the existing `ShiftTab` entities)

**`ShiftTab`** — becomes scoped to exactly one `(Molecule, JobType)`:

| Field | Notes |
|-------|-------|
| `Id` | PK |
| `MoleculeId` | required |
| `JobTypeId` (`int?`) | The job type this tab belongs to. Null only for Tech molecules (whose calendar job type is null). For workforce molecules it is set. |
| `NameEn` (`string`) | English display name (required) |
| `NameHe` (`string`) | Hebrew display name (required) |
| `PrioritizeCompanyUsers` (`bool`) | The "prioritize users from these companies when assigning" toggle |
| `Color` (`string?`) | Kept — existing pill design uses it |
| `SortOrder` (`int`) | Kept for stable ordering (no drag-drop UI; order = insertion/`SortOrder` then `NameEn`) |
| `IsActive` (`bool`) | Kept |
| `CreatedAt`, `CreatedByUserId`, `UpdatedAt` | Audit fields per project convention |

- Drop/repurpose the old single `Name`/`DisplayName` (superseded by `NameEn`/`NameHe`).
- Uniqueness: `UNIQUE(MoleculeId, JobTypeId, NameEn)` and `(MoleculeId, JobTypeId, NameHe)` to avoid duplicate tab names within a scope.

**`ShiftTabCompany`** `(ShiftTabId, CompanyId)` — the tab's **selected companies**.
- **Drop the existing `UNIQUE(CompanyId)` index.** A company may now belong to multiple tabs (across job types and even within one job type's tab set). Composite PK `(ShiftTabId, CompanyId)`.

**`ShiftTabShiftType`** `(ShiftTabId, ShiftTypeId)` — **NEW** join, the tab's **selected shift types**.
- **Replaces `ShiftType.TabId`** (drop that column). Rationale: per-tab multi-select of shift types, independent per tab, and the "empty selection = all job-type shifts" semantic reads cleanly as "no rows → no restriction". A shift type may appear in more than one tab's view.
- Composite PK `(ShiftTabId, ShiftTypeId)`.

**`UserShiftTabPreference`** — add `JobTypeId`. Scope becomes `(UserId, MoleculeId, JobTypeId) → ShiftTabId?`. `UNIQUE(UserId, MoleculeId, JobTypeId)`.

**EF/SQLite notes:** composite-PK join tables already exist here (`ShiftGroupingCompany`, `ShiftGroupingJobType`), so no new translation gotcha. All the cross-tenant reads use `IgnoreQueryFilters()` with a `SECURITY-AUDITED` comment (tab config is molecule-scoped; the endpoint/page gates molecule access first).

### 2.2 Management page — `/Admin/Organization/Tabs`

Follows the `ChoreTypes`/`DutyTypes`/`ShiftGroupings` pattern.

- **Route/namespace:** `Pages/Admin/Organization/Tabs/Index.cshtml(.cs)`, `ShiftManager.Pages.Admin.Organization.Tabs`.
- **Authorization:** `[Authorize(Policy = "Grant:ManageCalendarTabs")]`. Every POST handler re-verifies the target molecule via the existing `IsUserAuthorizedForMoleculeAsync` IDOR guard.
- **Grant:** new `ManageCalendarTabs` (see §2.6).
- **Nav:** add a leaf under the **הגדרות/Settings** subgroup (`_NavSidebar` data structure), `LocKey` → EN `Tabs` / HE `לשוניות`, visibility gated on the grant.
- **Service:** extend `IShiftTabService`/`ShiftTabService` (reuse the merged service; add company/shift-type/jobtype-aware methods).

**Page flow:**
1. **Select Molecule, then Job Type.** If the user has access to exactly one molecule, auto-select it; likewise auto-select the sole job type if only one is accessible in that molecule. (Reuse the accessible-molecule/job-type resolution already used by the Shifts calendar filters.)
2. **Manage the tabs for that exact `(molecule, jobtype)`:** Add, Delete, Edit name, Edit settings. **No drag-drop / manual ordering** (≈4 tabs max per scope).
3. **Tab settings form (per tab):**
   - `NameEn`, `NameHe`
   - **Companies** — checkbox list of the molecule's companies (empty = no restriction)
   - **Shift types** — checkbox list of the job type's shift types (empty = no restriction)
   - **`PrioritizeCompanyUsers`** toggle — label EN "Prioritize users from these companies when assigning" / HE (new resx key)

### 2.3 Calendar behavior — `/Calendar/Shifts`

The existing `.cal-tabs` strip UI is **kept and extended** (no new UI pattern).

- **Tab strip scoping:** the strip lists only tabs where `MoleculeId == current` **and** `JobTypeId == current job type`. Switching molecule or job type (`#jobTypeSelect` / `#moleculeSelect` → `updateCalendarFilters()`) re-derives the strip. Tabs of another molecule/jobtype never appear.
- **Selecting a tab:**
  - **By-User view** → roster = the tab's companies' users (∪ cross-over: anyone assigned to the tab's shifts). Empty companies → **all molecule companies** (no restriction).
  - **By-Shifts view** → rows = the tab's selected shift types. Empty shift types → **all job-type shift types** (no restriction).
  - **Prioritization** → if `PrioritizeCompanyUsers` is on, the tab's companies' users are prioritized in the pickers (see §2.4); if off, pickers behave normally molecule-wide.
  - **Name** → `NameHe` or `NameEn` per the user's current UI language.
- **No-tabs fallback:** if **no tabs exist** for `(molecule, jobtype)`, the calendar behaves exactly as before tabs existed: one molecule+jobtype calendar, no tab selection required, all companies in By-User, all job-type shift types in By-Shifts, all molecule users selectable in every picker, **no** company prioritization, **no** outside-company warnings.
- **Last-tab memory:** `(User, Molecule, JobType) → ShiftTab`. On return/switch: restore the remembered tab if it still exists; else the **first available tab** (order = `SortOrder`, then `NameEn`); else the no-tabs fallback. (Stored via the extended `UserShiftTabPreference`.)

> Note (view semantics): when tabs exist and each defines specific shift types/companies, a shift type or company in **no** tab is not shown on any tab unless a tab uses the "empty = all" default. This is the admin's intended view partitioning, consistent with the spec.

### 2.4 Selector prioritization + warning (the pickers)

Applies to all three assignment selectors:
- **Quick-entry** combobox (`.quick-entry-input`, `calendar-quick-entry.js`)
- **Bottom-sheet** user select (`#bottom-sheet-user-select`, `calendar-bottom-sheet.js`)
- **Trainee** picker (`.excel-calendar__trainee-picker`, `calendar-inline-edit.js`)

**Baseline (always):** the candidate set is the full **molecule + current job type** (D1). Nobody eligible is ever hidden.

**When a tab is active with `PrioritizeCompanyUsers = true`:**
- The tab's companies' users are **grouped/sorted first** (e.g. an optgroup/section "This tab" above "Other in molecule"). The searchable-select (§Item ③) renders and searches across both.
- Selecting a user **outside** the tab's companies → a **quick, dismissible, non-blocking warning** ("This user isn't from this tab's companies") via the existing `Toast`/`FeedbackModal` surface. It **must not** block or undo the selection.
- If the tab has **zero companies**, prioritization is a no-op over the whole molecule and **no** outside-company warnings fire.

**When the toggle is off (or no tab):** no prioritization, no warning; pickers are plain molecule+jobtype lists.

**Server support:** `/Api/Calendar/GetEligibleUsersForShift` already derives job type from `shiftType.JobTypeId` (no new param needed). Changes:
- Widen candidate companies from grouping-scoped to **all molecule companies** (keep category/`DoesShifts`/rank filters).
- Return each candidate's `CompanyId` (already available) so the client can group by the active tab's company set. The active tab's company set is provided to the page (it already loads the tab).
- **HOME/OFFLINE presence** shift types remain molecule-wide and **bypass** tab prioritization entirely (they are assignable to anyone).

**Trainee picker specifics:** widen `TraineeService.GetCompanyTraineesAsync(companyId)` → a molecule+jobtype query (`Role==Trainee` & `JobTypeId == current || JobTypeId == null`), server-rendered with `data-company`/`data-jobtype` for client grouping; molecule-wide selectable; same prioritization + warning. `IgnoreQueryFilters()` + `SECURITY-AUDITED` comment + molecule-scope re-verification (IDOR).

### 2.5 Localization

- `NameEn`/`NameHe` stored per tab; the calendar shows the language-appropriate one.
- All new UI strings (page title "Tabs"/"לשוניות", the toggle label, the outside-company warning, buttons) go through the resx pipeline. **Grep each key before adding** (the `Next7Days` dup lesson) and add to both `SharedResources.resx` and `SharedResources.he-IL.resx`.

### 2.6 Permissions — new grant `ManageCalendarTabs`

- Add per the **Grant Change Checklist**: append-only `id` at the end of `GrantTypeSeed` (never insert mid-seed), `Category = Shift`, `DefaultScope = Molecule`.
- Auto-grant to **Lead-and-above** role templates in `RoleTemplateSeed` (mirror the `ViewJusticeTable` "lead-and-above" precedent); update `RoleTemplateAutoGrantTests.cs` InlineData counts.
- Page uses `[Authorize(Policy = "Grant:ManageCalendarTabs")]`; handlers re-verify molecule scope.
- *(Alternative considered: reuse `ManageShiftCategories` as the merged tab feature did. Rejected for clarity — tabs are now a distinct, lead-managed surface. Open to reversing if you prefer no new grant.)*

### 2.7 Test-data reset

Reshape the seed (`ShiftyOrganizationSeed` / any tab seed) to the new model: create example `(molecule, jobtype)` tabs (e.g. Oren/Alhut → "Geo" {Tzafona, City, Camps, Hir} and "Tacti" {Radio}); populate **trainee `JobTypeId`** in `BulkTestUserSeed`. No migration of existing tab rows required.

---

## 3. Item ① — Bug 2: Quick Entry click conflict

- **Root cause:** `handleCellClick` in `calendar-quick-entry.js` (~:940-975) has an ignore-list (`.excel-calendar__assignment`, `button`, `a`, …); the runtime-injected trainee `<select>` (`.excel-calendar__trainee-picker`, inserted as a chip sibling inside the cell) matches none, so Quick Entry swallows the click and opens its own dropdown.
- **Fix:** add `.excel-calendar__trainee-picker` (and a bare `select`) to the ignore-list guard so Quick Entry leaves picker clicks alone.
- **Test:** JS/DOM regression asserting a click on the injected `<select>` does not open Quick Entry when Quick Entry mode is active.

---

## 4. Item ② — Bug 4: Concurrent-edit desync + error message

Real-time SignalR sync **already exists** (`calendar-realtime.js`; server broadcasts `AssignmentChanged` to `shifts-{moleculeId}-{jobTypeId}`).

- **Root cause fix:** `handleAssignmentChanged` (`Shifts.cshtml:~648`) only refreshes when a cell with the broadcast `shiftInstanceId` already exists in the DOM. When User A fills an empty hole, the server mints a **new** `ShiftInstance` and broadcasts its new id, which User B's older DOM lacks → refresh skipped → B stays stale → collision. Fix: refresh (or targeted cell update by row+date) even when the broadcast instance id is not yet present.
- **Error-message fix:** the assign endpoint returns `{ success:false, error, errorKey:"ALREADY_ASSIGNED" }` (HTTP 200). The client (`calendar-inline-edit.js:~571`) falls through to a raw toast. Special-case `errorKey === 'ALREADY_ASSIGNED'` → friendly message + trigger a grid refresh. Suggested string (EN): *"The user is already assigned to this shift. The calendar has been refreshed."* (adjusted from the reporter's wording since auto-refresh now happens); add HE resx.
- **Tests:** the guard fix (broadcast for a new instance id triggers refresh) and the error-key branch.

---

## 5. Item ③ — Feature 3: Searchable dropdowns

- **New** `wwwroot/js/searchable-select.js` — vanilla, air-gapped (no library), RTL+LTR aware, reusing the existing `calendar-quick-entry` filter/keyboard patterns.
- **Opt-in** via `data-searchable` on the ~50 high-benefit selects (user/assignee/trainee/director pickers, company/molecule/jobtype/role/grant selectors). Tiny enum/view-mode selects stay native (no benefit).
- **Multi-select support (in scope — complete feature):** the widget must handle `<select multiple>` — the 4 `ShiftGroupings` multi-selects (`SelectedCompanyIds`/`SelectedJobTypeIds`, create + edit forms) get a **checkbox/chip multi-select mode**: type-to-filter, toggle multiple options, show selected as removable chips, keep the underlying `<select multiple>` in sync so the existing POST binding is unchanged. Keyboard: type to filter, Enter/Space to toggle, Backspace to remove last chip.
- **Dynamic creation:** enhance at creation time for JS-built selects (bottom-sheet built fresh per open, calendar clones) plus a `MutationObserver` fallback — a one-shot `DOMContentLoaded` pass is insufficient. Also handle selects inside hidden/`display:none` modals (width measured on open).
- **Integration with ④:** this is the mechanism that renders the prioritized grouping ("This tab" / "Other") and the type-to-filter search over the full molecule set.
- **RTL:** mirror affordances (dropdown arrow, chip layout, clear button) via logical properties; validate in both `dir="rtl"` and `dir="ltr"`.
- **Accessibility:** `role="combobox"`/`listbox`, `aria-expanded`, `aria-activedescendant`, focus trap in the dropdown, Escape to close — matching the existing quick-entry combobox a11y.
- **Tests:** single + multi filter, keyboard nav, RTL rendering, dynamic-select enhancement, chip add/remove ↔ underlying `<select multiple>` sync.

---

## 6. Cross-cutting concerns & edge cases

- **Save-path parity (must verify):** confirm the shift-assign save path (`ShiftAssignmentService.AssignShiftAsync`) and the trainee save path do **not** independently hard-block on `ShiftGrouping` membership; if they do, relax to molecule+jobtype so the widened pickers don't produce pick-then-reject failures (the exact failure mode of the origin bug). The trainee save path is known to use a molecule-based *warning* (not a company hard-block).
- **Multi-company users:** eligibility already derives shift-doers from per-company `CompanyMembership.DoesShifts`, so a user who does shifts via a non-primary company is included in the molecule+jobtype baseline. Prioritization/warning group by the tab's company set; de-duplicate users across companies.
- **Tech molecules / null job type:** job type is null; tabs with `JobTypeId == null` (if any) apply; otherwise no-tabs fallback. No prioritization when there's no job type context.
- **Area-scoped / on-call calendars:** out of scope — no tab strip, no prioritization (the Tabs feature is `/Calendar/Shifts` only).
- **Draft mode:** tab is a view/relevance layer; the draft overlay stays keyed by `(ShiftTypeId, WorkDate)` and re-resolves the active tab at render — staged cells must not "disappear" when the active tab changes.
- **Empty-set safety:** empty companies → whole molecule; empty shift types → all job-type shifts. A selector must **never** render "nobody" because of tab config.

---

## 7. Implementation phasing (one delivery, staged internally)

1. **Phase A — independent fixes (low risk):** ① Bug 2, ② Bug 4. Ship-ready alone.
2. **Phase B — searchable-select foundation:** ③ `searchable-select.js` + tag high-benefit selects. Independent, and a prerequisite for ④'s picker UX.
3. **Phase C — selector baseline widening (the origin-bug fix):** D1 — widen the three pickers to molecule+jobtype; verify save-path parity (§6). Delivers the bug fix even before tabs are configured.
4. **Phase D — Tabs data model + service + management page:** §2.1, §2.2, §2.6, grant, nav, localization, seed reset.
5. **Phase E — Calendar tab integration:** §2.3, §2.4 — strip scoping, view filtering, prioritization + warning, last-tab memory, no-tabs fallback.

---

## 8. Testing strategy

- **Unit/service:** tab CRUD scoped by (molecule, jobtype); company/shift-type membership; empty=all semantics; last-tab resolution + fallback; widened eligibility candidate set; prioritization grouping; grant auto-assignment counts.
- **Full suite serialized:** `-- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1` (parallel gives spurious `:memory:` failures). Restore `packages.lock.json` after build.
- **Browser E2E (seeded Oren/Alhut on :5000):** the origin bug (City lead can now pick a Tzafona trainee); tab strip changes with job type (Alhut shows Geo/Tacti, BR shows none/own); By-User + By-Shifts view filtering; prioritization ordering + outside-company warning (non-blocking); no-tabs fallback; last-tab memory; concurrent-edit auto-refresh (Bug 4); Quick-Entry no longer hijacks the trainee picker (Bug 2); searchable dropdowns in HE (RTL) + EN.
- Build `-c Release` to avoid the running Debug-exe lock.

---

## 9. Resolved decisions (user-approved 2026-07-21)

1. **Grant:** new `ManageCalendarTabs` (molecule-scoped, Lead-and-above), added append-only per the Grant Change Checklist. *Resolved: new grant.*
2. **`ShiftType.TabId` → `ShiftTabShiftType` join:** **Yes — drop the column** and rewire By-Shifts/cross-over/draft to the join. Test-data reset makes this clean.
3. **Wording:** off-tab warning EN "This user isn't from this tab's companies." / HE (new resx). Bug 4 EN "The user is already assigned to this shift. The calendar has been refreshed." / HE (new resx). *Final unless the review proposes better.*
4. **Null-job-type trainees:** **include as universally relevant** (show when `JobTypeId == current || JobTypeId == null`). In practice trainees will have a job type; the null case is a safety net so a mis-seeded trainee is never hidden — the exact failure of the origin bug.
5. **Save-path parity:** relaxing the shift-assign save path to molecule+jobtype (to match the widened pickers) is **approved**; implementation must verify `AssignShiftAsync` (and the trainee save path) do not hard-block on `ShiftGrouping`, and relax if they do.

## 10. Completeness commitment

This ships as a **complete, usable feature** — no scope deferrals. Multi-select searchable dropdowns are **in** (§5). The two-agent design review (2026-07-21) produced the **"Perfect State" acceptance checklist** (see companion `2026-07-21-calendar-tabs-perfect-state-checklist.md`) that browser E2E must satisfy before this is considered done.

---

## 11. Design-review hardening (two-agent review + cross-review, 2026-07-21)

A two-reviewer review (completeness + usability) with cross-review found 6 blockers and produced the fixes below. **PLAN-FIXES** are folded into the design; the **PENDING DECISIONS** need user sign-off before implementation.

### 11.1 Resolved conflicts
- **"All" pseudo-tab (safety net).** The "empty-per-tab = all, no Main" model risks making any shift type/company covered by no tab — *and every newly-created shift type* — invisible everywhere. Resolution: a **synthetic, always-present "All" pseudo-tab** (view-only, **no DB row**) whose behavior == the no-tabs fallback (all molecule+jobtype shift types, all companies, no prioritization, no warnings). Zero admin action, no schema, auto-absorbs new shift types, and guarantees HOME/OFFLINE + null-jobtype shifts are always reachable. Plus an **advisory** management-page warning when configured tabs don't cover every shift type (for admins who *want* strict partitioning). *(Adoption = PENDING DECISION UD1.)*
- **Bug 4 refresh mechanism.** The current client-side cell-patching (`GetShiftsData` JSON → `updateCalendarCells`/`updateSingleCell`, `Shifts.cshtml:616-644`) **cannot materialize a newly-minted instance** (it only iterates cells already in the DOM) and strips chip markup — so the "calendar refreshed" message would be false exactly when it fires. Resolution: on `AssignmentChanged`, **re-fetch a server-rendered, tab-filtered partial of the affected rows/region and swap the DOM** — materializes new instances/rows, preserves full remove-×/trainee-+/busy/draft markup, and never injects an off-tab row (SignalR group `shifts-{mol}-{jobtype}` is tab-agnostic, so every peer receives the event and the server re-applies the tab filter).

### 11.2 RESOLVED DECISIONS (user-approved 2026-07-21)
- **UD1 — "All" pseudo-tab: YES.** Always-present, synthetic, view-only (no DB row); shows all molecule+jobtype shift types + all companies, no prioritization, no warnings; auto-absorbs new shift types. The strip renders `[All] [tab] [tab]…`. The management page still shows an advisory warning when configured tabs don't cover every shift type.
- **UD2 — First-visit / no-preference default tab: "All".** With no saved preference, the calendar lands on the **All** tab (see everything), then remembers the user's explicit choice per (user, molecule, jobtype).
- **UD3 — Default `PrioritizeCompanyUsers` for a new tab: TRUE (ON).** A newly-created tab prioritizes its companies + warns on off-tab picks immediately. *(Note: this composes safely with UD2 — the default landing is "All", which has no company restriction and therefore no prioritization/warnings; the ON default only takes effect once a user deliberately enters a configured tab.)*
- **UD4 — Off-tab warning cadence: once per (tab, session).** Warn the first time a user picks an off-tab user on a given tab, then stay silent for that tab for the rest of the session (resets next session). Never stacks with the success toast; the fill-handle/bulk gesture fires at most one.
- **UD5 — Remove the old `/Owner/Blueprints` tab editor: YES.** One tab-management surface (the new page); the Blueprints tab controls + `ReorderTabsAsync` + drag UI are removed (PF1).

### 11.3 PLAN-FIXES (folded into the design)
**Blockers:**
- **PF1** Remove the old Blueprints tab editor (`Blueprints.cshtml.cs:727-856` + markup + `ReorderTabsAsync`) and reconcile its grant (`ManagerHomeAccess` no longer confers tab editing).
- **PF2** Widen candidate companies to **all molecule companies unconditionally** in BOTH `ShiftAssignmentService.cs:96-98` (grouping-empty fallback) **and** `:104-106` (single-company `shiftType.CompanyId` branch) — not just removing the grouping filter; keep category/DoesShifts/rank. Reconcile the narrative with `FF_CATEGORY_BASED_SHIFT_ELIGIBILITY`: flag-ON = molecule-wide DoesShifts+category (**not** jobtype-scoped), flag-OFF = molecule-wide jobtype; don't label the picker "by job type" when the flag is on. Address the separate **Tech** path (`ShiftCalendarService`).
- **PF3** Bug-4 server-rendered, tab-filtered grid-region refresh (see §11.1); retire the client cell-patch for this use.
- **PF4** Enumerate & rewire **every** `ShiftType.TabId` reader to the `ShiftTabShiftType` join: by-shift (`Shifts.cshtml.cs:479-481`), by-user shift-type list (`:607-609`), cross-over + `_tabOfShiftType` (→ `shiftTypeId→SET<tabId>`, `TabMatches`→set-contains, `:639-653`), **`GetShiftsData.cshtml.cs:106-107`** (+ change unknown-tab semantics from match-nothing → graceful fallback), Blueprints handlers, EF config `AppDbContext.cs:1475-1481`, tests (`DraftOverlayRenderTests`, `ShiftsUserRowShiftCountTests`, `ShiftTabServiceTests`).
- **PF5** Server-side `inTab: bool` tagging on the eligible-users **and** trainee endpoints (endpoint returns no `companyId` today — `GetEligibleUsersForShift.cshtml.cs:90`); pass the active `tabId`; compute against the tab's company set AND each user's full `CompanyMembership` set so multi-company in-tab users aren't mis-warned.
- **PF6** Schema migration + data reset: SQLite table-rebuild migration (drop `ShiftType.TabId`+FK+index, drop `UNIQUE(ShiftTabCompany.CompanyId)`, add `ShiftTab.JobTypeId/NameEn/NameHe/PrioritizeCompanyUsers/audit`, drop `Name/DisplayName`+`(MoleculeId,Name)` unique, add `ShiftTabShiftType`, alter `UserShiftTabPreference`); **DELETE all `ShiftTab`+`UserShiftTabPreference` rows** (JobTypeId enters the unique index — cannot backfill); **backfill trainee `JobTypeId`** on already-seeded users (skip-if-exists seeder won't) via DB wipe or targeted backfill.

**Severe:**
- **PF7** HOME/OFFLINE presence rows and null-`JobTypeId` shared shift types always render under every tab (never hidden by the view filter). *(Also satisfied by the "All" tab + auto-inclusion.)*
- **PF8** One shared client grouping/ordering utility across the three pickers; apply prioritization order **client-side at render** (quick-entry `eligibleCache` else carries stale order).
- **PF9** Searchable-select ↔ calendar JS: trainee blur-dismiss (`calendar-inline-edit.js:897`) must not self-destruct when enhanced; option clone (`:877-882`) must copy `data-company`/`data-jobtype`; async busy-decoration needs a widget `refresh()` hook; scope the `MutationObserver` (avoid realtime DOM-churn).

**Moderate/Low:**
- **PF10** Tab-resolution rewrite (`Shifts.cshtml.cs:271-299`): remove null=Main first-class state; a concrete tab (or "All") always active; drop `?Tab=0`/`else if(AvailableTabs>0)→TabId==null`; `AvailableTabs` (molecule,jobtype)-scoped; drop `Tab` on jobtype change in `updateCalendarFilters`.
- **PF11** Service signature ripple: `CreateAsync/RenameAsync`(→NameEn/NameHe), `AssignShiftType/CompanyToTab` (disjoint→m2m toggle), `GetCompanyIdsForTabAsync` (Main branch removed), `Get/SetLastTabAsync`(+jobtype); update all callers + tests.
- **PF12** Trainee widening: `GetCompanyTraineesAsync(companyId)`→molecule+jobtype (`JobTypeId==current||null`); update the other caller `Pages/Assignments/Manage.cshtml.cs` + 4 tests; molecule resolution + IDOR + `IgnoreQueryFilters`+SECURITY-AUDITED.
- **PF13** Management-page completeness: dup-name over BOTH NameEn AND NameHe; empty-config helper text + live summary; delete impact-preview (`OnGetCheckTabUsageAsync` pattern); per-handler IDOR (jobtype∈molecule, companies/shifttypes∈scope); audit logging; molecule/jobtype auto-select; nav leaf in **`Services/Navigation/NavRegistry.cs`**; advisory coverage-warning.
- **PF14** Save-path parity: verify `AssignShiftAsync` + trainee save path don't hard-block on `ShiftGrouping`; relax to molecule+jobtype if they do.
- **PF15** Draft × tab: commit all staged cells (draft is molecule+jobtype-scoped) but disclose the off-view staged count.
- **PF16** Sticky-layout + RTL: verify sticky offsets survive strip appear/disappear; bidi-isolate Latin tab names in the RTL strip; define the Tech/null-jobtype admin flow.
- **PF17** Add `ALREADY_ASSIGNED` to the existing `calendar-inline-edit.js:529` errorKey switch (same pattern as `SHIFT_FULLY_STAFFED`) + HE resx; verify `errorKey` passes through the assign response contract.
- **PF18** Config-churn graceful degrade (server re-validates tab ids); surface audit fields on the admin page.
