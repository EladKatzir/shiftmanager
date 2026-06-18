# HANDOFF — Navigation & Design-Bar Redesign (Issues 2 + 3)

**Created:** 2026-06-16 · **Branch:** `dev` · **For:** a fresh peer session to brainstorm + implement
**Sibling work:** Issue 1 (quick-entry day-notes) is being implemented in a separate session — see
`docs/superpowers/specs/2026-06-16-quick-entry-day-notes-design.md`. Keep this redesign separable from that.

---

## 0. How to start (process)

This is **creative/design work** → you MUST run the `superpowers:brainstorming` skill first, and do NOT
write code until a design is presented and the user approves it. The work is heavily visual (nav layouts,
information architecture, page mockups) → **offer the brainstorming Visual Companion** (its own message) early.

Recommended decomposition (each gets its own spec → plan → implement cycle):
1. **IA / page inventory rationalization** (what pages exist, what merges, what's orphaned).
2. **Sidebar navigation redesign** (structure, grouping, discoverability).
3. **Design-bar / top header redesign** (the action bar: notifications, language, theme, context-switcher).
4. **Issue 3 specifically: a discoverable home for "who is eligible for a shift category"** (see §4).

---

## 1. The user's ask (verbatim intent)

- **Issue 2:** "redesign the navigation and the design bar to make it comfortable after all the changes we
  recently made adding categories, changing the chore system and more. we also need to deeply brainstorm
  what pages need to change."
- **Issue 3:** "we now added shift category and enabled who 'eligible' for a shift, we need to know where we
  edit that and have easy navigation there."

**Why now:** the app has absorbed large features recently (ShiftCategory eligibility, chore-system overhaul,
multi-company membership, distribution lists, notifications). Navigation has accreted, not been redesigned,
around these. The user wants a deliberate IA pass.

---

## 2. CURRENT navigation — exact inventory (verified 2026-06-16)

### 2a. Sidebar — `Pages/Shared/_Layout.cshtml` (lines ~470–747)
Grant-gated via `<require-grant key="..." [negate] [mode="any"]>`. Six sections:

**Section 1 — MY SHIFTY** (loc `Calendar_MyCalendar`, always visible)
- Home → `/Home/Index` (always)
- Command Center → `/Admin/Index` `[ManagerHomeAccess]`
- Schedule (Shifts) → `/Calendar/Shifts|Month` (URL varies by ExcelCalendars flag)
- Company Overview → `/Calendar/Overview` (always)
- Requests (manager) → `/Requests/Index` `[ManagerHomeAccess]`
- Requests (employee) → `/My/Requests` `[negate ManagerHomeAccess]`

**Section 2 — MANAGEMENT** (loc `Calendar_Management`, shown if any of `[ManagerHomeAccess,ViewChores,ViewDuties,ManageOnDuty]`)
- Shifts Management → `/Calendar/Table` `[ManagerHomeAccess]`
- Shift Plans → `/Owner/Programs` `[ManagerHomeAccess]`
- Blueprints → `/Owner/Blueprints` `[ManagerHomeAccess]`
- Chores → `/Calendar/Chores|Public` `[ViewChores]`
- On Duty → `/Calendar/OnCall|Public` `[ViewDuties]`
- Duty Rotations → `/Admin/DutyRotation` `[ManageOnDuty]` (flag-gated)

**Section 3 — DIRECTOR TOOLS** (loc `Section_DirectorTools`, `[DirectorHubAccess]`)
- View As Manager → `/Director/ViewAsMode`
- Notification Hub → `/Director/NotificationHub`
- Company Filter → `/Director/CompanyFilter`

**Section 4 — ADMIN** (loc `Nav_Admin`, shown if any of `[ViewUsers,EditCompany,ViewAnalytics,ViewAuditLog,ViewSettings,ManageAnnouncements,ManagerHomeAccess]`)
- People → `/Admin/Users` `[ViewUsers]`
- Companies → `/Admin/Companies` `[EditCompany]`
- Analytics → `/Admin/Analytics` `[ViewAnalytics]`
- Audit Log → `/Admin/AuditLog` `[ViewAuditLog]`
- Settings → `/Admin/Config` `[ViewSettings]`
- Announcements → `/Admin/Announcements` `[ManageAnnouncements]`
- Organization → `/Admin/Organization` `[ViewHierarchy,ManagerHomeAccess]`

**Section 5 — OWNER PANEL** (loc `Owner_AdminPanel`, `[AdminAccess]`)
- Admin Panel → `/Owner/Index|Hub`
- Telemetry → `/Owner/Telemetry`
- Feature Flags → `/Owner/FeatureFlags`
- System Health → `/Owner/SystemHealth`

**Section 6 — PERSONAL** (loc `Nav_Personal`, always visible)
- My Groups → `/MyTeam/Index`
- My Friends → `/Friends` (flag `FriendshipsEnabled`)
- My Profile → `/My/Profile`
- My Settings → `/My/Settings`
- Help → `/My/Help`

Plus: **ContextSwitcher** component (multi-company) at lines ~490–493 (flag `EnableCompanySwitcher`);
sidebar footer user menu (lines ~700–747); collapse toggle (localStorage `shifty_sidebar_collapsed`);
mobile hamburger (≤768px).

### 2b. "Design bar" = top header — `_Layout.cshtml` lines ~788–820 (`.app-header-right`)
- DecisionRibbon component (not on `/Owner` routes)
- Notifications bell + badge
- Language toggle (EN/HE)
- Theme toggle (dark/light)
- Logout
CSS: `.app-header`, `.app-header-right`, `.action-btn` in `wwwroot/css/site.css` (~lines 473–530).

### 2c. Nav CSS lives in `wwwroot/css/navigation.css` + `site.css`
Key classes: `.app-sidebar`, `.app-sidebar-nav`, `.app-sidebar-nav-item(.active)`, `.nav-section-header`,
`.sidebar-toggle`, `.mobile-nav-toggle`, `.context-switcher*`, `.sidebar-user*`. RTL in `rtl.css`;
dark mode via `html[data-theme="dark"]`. Tokens in `tokens.css`.

---

## 3. Page inventory + redesign candidates

Top-level areas under `Pages/`: `Home`, `Calendar` (Shifts/Month/Week/Day/Table/Overview/Chores/OnCall/
ManageDistributionLists), `Public` (Chores/OnDuty/Feedback), `Admin` (Users/Companies/Analytics/AuditLog/
Config/Announcements/DutyRotation/HomeTypes/**Organization**/Settings/SetupTasks), `Owner` (~25 scattered
admin/config pages), `Requests`, `My` (Profile/Settings/NotificationCenter/Requests/Help×4/Onboarding/ApiKeys),
`MyTeam`, `Friends`, `Director` (ViewAsMode/NotificationHub/CompanyFilter), `Game`, `Api/*`.

**`Admin/Organization` is the deepest pain** — a ~14-child subtree: Areas, Departments, Projects, Stores,
JobTypes, Roles, Grants, ShiftGroupings, DutyTypes, **ChoreTypes**, **ChoreTemplates**, AreaPalette, Molecules,
Hierarchy. **Shift category + eligibility editing lives in here** (Hierarchy / category management). This is
exactly why Issue 3 exists: it's reachable but undiscoverable.

**Redesign candidates (consolidation):**
1. `Admin/Organization` — split into tabbed groups or a clearer hub; surface category/eligibility prominently.
2. `Owner` panel — ~25 scattered tools; group by purpose (System / Config / Data).
3. `My/Help*` — 4+ help pages → one hub with tabs.
4. `Requests` — `/Requests/Index` (mgr) vs `/My/Requests` (emp): unify into one role-aware view.
5. Possible orphans to verify: `Pages/Chores/Calendar.cshtml`, `Pages/Assignments/*`, `Pages/Schedule/Index`,
   `Admin/Molecules`, `Admin/Directors`, `Owner/MasterPrograms` (vs `Programs`).

---

## 4. Issue 3 — "where do I edit shift-category eligibility?" (the concrete sub-task)

**What the user means:** ShiftCategory (3b) drives *who is eligible* for a shift. Today the editing surfaces are:
- **Category definitions / membership:** under `/Admin/Organization` (Hierarchy + category management). The 3b
  feature: `DoesShifts` + `ShiftType.CategoryId` + per-user category membership (`UserChoreCategory` analog for
  shifts) + `EligibilityRule`. See memory `shift_assign_grant_and_eligibility_2026-06-14` and plan
  `docs/superpowers/plans/2026-06-16-category-based-shift-eligibility.md`.
- **Per-user "DoesShifts" + category multiselect:** in `/Admin/Users` editor.
- **Eligibility runtime:** flags `FF_CATEGORY_BASED_SHIFT_ELIGIBILITY` + `CalendarPageConfig.categoryEligibilityEnabled`.
  Endpoint `/Api/Calendar/GetEligibleUsersForShift`.

**Issue 3 goal:** a single, discoverable entry point — e.g. a top-level "Eligibility" or "Categories" nav item
(or a prominent card on a redesigned Organization hub) that lets an admin answer + edit "who can do shift
category X." Decide during brainstorming whether it's a new page, a new nav shortcut to existing pages, or a
reorganized Organization hub.

---

## 5. Constraints / gotchas (do not relearn the hard way)

- **`<require-grant>`** tag helper drives all nav visibility — any new nav item needs the right grant key
  (137-grant system; see memory `grant_change_checklist.md`). `AssignShifts` (137) is now job-type-agnostic.
- **Grant for category management:** `ManageShiftCategories` (136, Shift/Molecule).
- **Localization is mandatory** — every label uses `<loc key="...">`; add to BOTH `SharedResources.resx` and
  `SharedResources.he-IL.resx`. GREP the key first (dup keys break localization tests).
- **RTL + dark mode are first-class** — every nav/header change must be checked in Hebrew RTL and dark theme.
  Dark-on-dark text is the #1 recurring visual bug here (see MEMORY.md CSS contrast rules).
- **`FinalProductPublish/` is generated** — never edit it; regen via `scripts/Update-FinalProductPublish.ps1`.
- **Dev app at :5000 serves STALE static assets** until process restart — `curl` the served file for your
  marker before browser-testing JS/CSS.
- **Multi-company:** nav must respect the active-company switch (`_tenantResolver.GetCurrentTenantId()`).
- Full test suite must run **sequential** (`xUnit.MaxParallelThreads=1`) or ~233 spurious SQLite-contention fails.

---

## 6. Suggested first moves for the peer session

1. Run `superpowers:brainstorming`; offer the Visual Companion.
2. Ask the user to prioritize: IA cleanup vs sidebar visuals vs the Issue-3 eligibility entry point — which first?
3. Read `Pages/Shared/_Layout.cshtml` (470–820), `wwwroot/css/navigation.css`, `Pages/Admin/Organization/`.
4. Propose 2–3 IA approaches with mockups (companion), get approval per section, then spec → plan → implement.
5. Coordinate builds with any concurrent session (Issue 1 / notifications peer work on `dev`).
