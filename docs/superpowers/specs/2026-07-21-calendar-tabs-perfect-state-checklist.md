# Calendar Tabs & Selectors — "Perfect State" Acceptance Checklist

- **Date:** 2026-07-21
- **Companion to:** `2026-07-21-calendar-tabs-selectors-and-desync-design.md`
- **Source:** two-agent design review (ReviewerA completeness/architecture, ReviewerB usability/failure-mode) + cross-review.
- **Purpose:** the browser-testable **definition of done**. Every item is given/when/then, pass/fail, verifiable on the running app.

**Seed baseline** (§2.7 of the spec): Oren molecule, Alhut job type, tabs **Geo** {Tzafona, City, Camps, Hir} + **Tacti** {Radio}; **BR** job type has no tabs; trainees seeded with `JobTypeId`.
**Markers:** `[2-browser]` needs two sessions. `[flag]` must run under **both** `FF_CATEGORY_BASED_SHIFT_ELIGIBILITY` states unless noted.

---

## ORG — Origin bug & selector baseline
- **ORG-1** `[flag]` City lead on Oren/Alhut → trainee picker on a City assignment lists Tzafona Alhut trainees (and null-jobtype trainees); selecting one **saves** — no pick-then-reject, no error.
- **ORG-2** `[flag]` Bottom-sheet on an Alhut shift lists users from **all** Oren companies (with company suffix) — incl. companies outside every ShiftGrouping **and** for a shift type whose own `CompanyId` is set (the `ShiftAssignmentService.cs:104-106` branch). Cross-company pick saves.
- **ORG-3** `[flag]` Quick-entry on an empty cell: typing a Tzafona user's name lists them; Enter assigns.
- **ORG-4** Flag ON → candidates = molecule-wide DoesShifts+category (jobtype NOT applied); flag OFF → molecule-wide jobtype matches. Verify the differing sets.
- **ORG-5** Tech molecule → Tech eligibility path (EligibleCompanyIds + officer rank) still applies, widened consistently, no tab prioritization (no jobtype context).
- **ORG-6** Trainee with `JobTypeId=null` is listed in every trainee picker (universal-relevance safety net).
- **ORG-7** Multi-company user (primary Tzafona, DoesShifts membership in City) is listed exactly **once**.
- **ORG-8** On an existing dev DB migrated in place, previously-seeded trainees still appear (JobTypeId backfilled/re-seeded — skip-if-exists seed must not leave them hidden).

## ADM — Tabs admin page (`/Admin/Organization/Tabs`)
- **ADM-1** Lead with `ManageCalendarTabs` sees the nav leaf (EN "Tabs" / HE "לשוניות", registered in `Services/Navigation/NavRegistry.cs`) under Settings; molecule + jobtype auto-select when exactly one accessible.
- **ADM-2** Tech molecule (no jobtypes): flow proceeds without a jobtype step; tabs save with `JobTypeId=null`.
- **ADM-3** Missing NameEn OR NameHe → localized validation error, nothing persists.
- **ADM-4** Duplicate NameEn OR NameHe within the same (molecule, jobtype) → friendly localized uniqueness error (no raw DB exception).
- **ADM-5** Both checkbox lists empty → inline "nothing selected = no restriction" helper; save succeeds; that tab shows all companies / all jobtype shift types.
- **ADM-6** A save leaving ≥1 shift type covered by no tab → non-blocking warning naming the uncovered shift type(s).
- **ADM-7** Delete → confirmation with impact preview (companies/shift types; users whose remembered tab it is — the `OnGetCheckTabUsageAsync` pattern); affected users fall back per LTM-2.
- **ADM-8** Strip order deterministic (SortOrder then NameEn); create/delete never scrambles surviving pills.
- **ADM-9** No tab controls remain in `/Owner/Blueprints` or the shift-type edit form (old 6-handler editor incl. drag-reorder removed); exactly one tab-management surface.

## STR — Strip & view filtering (`/Calendar/Shifts`)
- **STR-1** Oren/Alhut → strip shows exactly Geo + Tacti (language-appropriate names); jobtype→BR removes the strip with no sticky/layout corruption; back restores the remembered tab.
- **STR-2** Geo in By-Shifts → only Geo's effective shift types render **and** HOME/OFFLINE presence rows still render on every tab.
- **STR-3** A shift type selected by both tabs renders on both tabs' By-Shifts.
- **STR-4** Null-JobTypeId shared shift type is offered in every jobtype's admin checklist (or always renders) — never invisible on all tabs.
- **STR-5** Geo in By-User → roster = Geo companies' users ∪ off-tab users assigned to a Geo-effective shift this week (cross-over).
- **STR-6** A Radio (Tacti) user also assigned to a Geo-only shift → on Tacti By-User the off-tab assignment renders **ghosted** per the SET-based rule (shift type ∉ active tab's effective set).
- **STR-7** Zero-tab (molecule, jobtype) → no strip; views/pickers behave pre-tabs.
- **STR-8** `?Tab=0` (legacy) or a deleted/foreign tab id → loads normally on remembered-else-first tab; no empty grid, no error.
- **STR-9** (parity) Server-rendered By-Shifts and a `GetShiftsData` refresh for the same (molecule, jobtype, tab) yield the **same** rows/instances (the `:106-107` TabId filter rewired to the join with identical empty=all semantics).

## PRI — Prioritization & off-tab warning
- **PRI-1** Geo with prioritize ON → all three pickers render the same two labeled groups ("this tab" first, "other in molecule") with the same ordering.
- **PRI-2** Grouping is server-computed (`inTab:bool` per candidate from the tabId param, membership-aware) → the multi-company user (member via membership only) is in the TAB group, **no** warning.
- **PRI-3** Selecting an "other"-group user → assignment **succeeds**, exactly one non-blocking dismissible warning (approved wording, both cultures), does not stack over the success feedback, selection never undone.
- **PRI-4** Prioritize OFF, or zero-company tab, or no tab → flat molecule-wide list, no warning ever.
- **PRI-5** New tab → `PrioritizeCompanyUsers` starts at the decided default; verify no warnings until enabled.
- **PRI-6** HOME/OFFLINE row → no grouping, no warning, regardless of tab.
- **PRI-7** Quick-entry fill-handle replicating an off-tab assignment across 5 days → at most **one** warning (no toast storm).

## LTM — Last-tab memory & navigation
- **LTM-1** Select Tacti, leave, return to Oren/Alhut → Tacti active; memory is per (user, molecule, jobtype) — Alhut choice doesn't affect BR or another molecule.
- **LTM-2** Remembered tab deleted → first-available (SortOrder, NameEn); if none, no-tabs fallback. No error.
- **LTM-3** Jobtype switch via `#jobTypeSelect` → no stale Tab id in the URL (drop Tab on jobtype change, not only molecule change — `Shifts.cshtml:667-671`); the new jobtype resolves its own remembered tab.

## CON — Concurrency & realtime (Bug 4)
- **CON-1** `[2-browser]` Same (molecule, jobtype, tab), By-Shifts: A assigns into an **empty** cell (new ShiftInstance minted). Within ~2s B's grid shows the assignee **with full chip anatomy** (remove ×, trainee +, busy classes) without reload — refresh materializes cells absent from B's DOM via tab-filtered server-rendered content, not the `updateCalendarCells` skeleton.
- **CON-2** `[2-browser]` B (stale) assigns the same user to the same cell → B sees the friendly localized ALREADY_ASSIGNED message (verify `errorKey` flows through the `/Calendar/Table?handler=AssignEmployee` contract) **and** B's grid then truly shows A's assignment (the "refreshed" claim is true).
- **CON-3** `[2-browser]` A on Geo, B on Tacti: A assigns on a Geo-only shift → B's refresh fires (SignalR group is tab-agnostic) but Tacti's view is unchanged (tab filter re-applied server-side).
- **CON-4** `[2-browser]` Admin deletes B's active tab while B's calendar is open → next realtime refresh does not blank/error (`GetShiftsData` unknown-tab semantics changed from "matches nothing" to graceful fallback returning data); B's next navigation lands on a valid tab.
- **CON-5** `[2-browser]` A removes an assignment → B's cell updates (count + chip removed) without reload.

## QE — Bug 2 (quick-entry vs trainee picker)
- **QE-1** Quick-entry ON: click trainee "+" then the injected `<select>` → quick-entry does **not** open; picker operates; choosing a trainee assigns.
- **QE-2** Picker open: Escape closes it; clicking a different empty cell then opens quick-entry normally (no dead state).
- **QE-3** If the trainee picker is searchable-enhanced: typing does not trigger the 150ms blur self-dismiss (`calendar-inline-edit.js:897`) and cloned options retain `data-company`/`data-jobtype` (`:877-882`); if left native, that exception is documented.

## SEL — Searchable dropdowns
- **SEL-1** A `data-searchable` single select filters live in EN(LTR) + HE(RTL); Arrow/Enter selects; Escape restores; underlying select fires exactly one change.
- **SEL-2** The 4 ShiftGroupings multi-selects: type-to-filter, Enter/Space toggle, chips with × + Backspace-removes-last (logical order in RTL); POST persists exactly the chip set (underlying `<select multiple>` synced).
- **SEL-3** Bottom-sheet (rebuilt per open, incl. from hidden) is enhanced on every open with correct width on first open.
- **SEL-4** Async busy decoration after load (`calendar-bottom-sheet.js:903-949`) → enhanced widget shows busy glyphs + disables hard-conflict users (option-mutation resync / `refresh()`).
- **SEL-5** Realtime refresh churn → no observer performance degradation (observer scoped, not document-wide); no jank during CON tests.
- **SEL-6** Quick-entry `eligibleCache` → prioritization ordering applied at render, never baked into cached response order.

## LOC — Localization / RTL
- **LOC-1** Every new string renders translated in both cultures — no key leakage, no EN fallback in HE; grepped-before-added; present in both resx.
- **LOC-2** Pills show NameHe under HE / NameEn under EN; a Latin name in the RTL strip renders unscrambled (bidi isolation), and vice-versa.
- **LOC-3** The Tabs admin page fully mirrored under `dir="rtl"` (form, checkbox lists, chips, buttons); no horizontal scroll.

## DFT — Draft mode
- **DFT-1** Active shifts draft: staged Geo assignments remain staged + visible after switching to Tacti and back.
- **DFT-2** Staged cells on rows hidden by the current tab → commit surface discloses the off-view staged count; commit applies all staged cells.
- **DFT-3** A staged synthetic cell uses the SET-based tab-match — renders on every tab whose effective set contains its shift type.

## REG — Regressions
- **REG-1** ShiftGroupings still band calendar rows as before (only the eligibility restriction removed); `IsInShiftGrouping` display unchanged.
- **REG-2** Chores / On-Call / Overview / Team: no strip, no prioritization, pickers unchanged.
- **REG-3** `/Assignments/Manage` trainee picker still works after the `GetCompanyTraineesAsync` contract change (its 4 tests + a browser check).
- **REG-4** A (molecule, jobtype) that never had tabs behaves byte-for-byte as v5.2.9 (views, pickers, quick-entry, drafts).
- **REG-5** Full suite green **serialized** (`xUnit.ParallelizeTestCollections=false`, `MaxParallelThreads=1`); `packages.lock.json` restored; reshaped-service tests compile (ShiftTabServiceTests, DraftOverlayRenderTests, ShiftsUserRowShiftCountTests, RoleTemplateAutoGrantTests counts).
- **REG-6** Migration runs clean on a **non-empty** existing dev DB (SQLite rebuilds for TabId/Name drops, ShiftTabCompany re-key, UserShiftTabPreference unique change with row cleanup); app boots, calendars load.

## SEC — Permissions & security
- **SEC-1** No `ManageCalendarTabs` → nav leaf hidden AND direct GET of the page denied.
- **SEC-2** Forged POST to another molecule's id (or jobtype ∉ molecule, company ∉ molecule, shift type ∉ scope) → each handler rejects (per-handler IDOR, not just page-level authorize).
- **SEC-3** A user holding only `Grant:ManagerHomeAccess` (old Blueprints gate) can no longer create/edit/delete tabs anywhere.
- **SEC-4** `GetEligibleUsersForShift` with a foreign-molecule tabId → rejected/ignored (no cross-molecule tab-config leak); results stay molecule-scoped.
- **SEC-5** Grant added append-only at seed end; `RoleTemplateAutoGrantTests` InlineData counts updated; Lead-and-above templates receive it (verify: a Lead sees the page, an Employee doesn't).
