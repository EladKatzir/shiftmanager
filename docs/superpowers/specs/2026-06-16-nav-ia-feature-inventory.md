# Navigation / IA Redesign — Feature Inventory & Diagnosis

**Created:** 2026-06-16 · **Branch:** `dev` · **Workstream 1 of 4** (IA / page-inventory rationalization)
**Source:** parallel codebase sweep (5 agents) over Shifts, Chores/Duties, People/Org, Owner/System, Personal.
**Purpose:** the complete capability inventory that the feature-driven IA is designed against. This is *input* to the design, not the design itself. The design spec is a separate file.

> Framing (user): *"We have a full feature list of everything we can do — where should each feature live, and how does the user access it comfortably?"*

---

## 1. Systemic problems (the diagnosis — these repeat in EVERY area)

1. **One nav link hides a whole subtree.** `Admin → Organization` is a single link concealing **16 child pages** and is the *sole* discovery path for ~13 of them. `Owner → Admin Panel` surfaces 4 links but the area has **~30 pages**; the richer `/Owner/Hub` (with a 23-item quick-link grid) has **no sidebar link at all** and is the only gateway to ~15 pages. Admin exposes 7 sidebar links over ~25 capabilities (~3:1 hidden:visible); Owner is worse (~6:1).
2. **Flag-controlled bait-and-switch on a single label.** "Schedule", "Chores", and "On-Duty" each route to one of *two structurally different pages* depending on feature flags (`ExcelCalendar*`). Same label, icon, and active-state — the user can't tell which experience (and which feature set) is live. "Shifts Management" (Table) only appears when the flag makes two URLs differ.
3. **Eligibility has no home (Issue 3).** The word "eligibility" appears in **no nav label or page title**. Editing "who can do shift/chore category X" requires THREE unlinked pages: `/Admin/Users` (per-user `DoesShifts`/`DoesChores` + category membership), `/Owner/Blueprints` (shift-category definitions), and `/Admin/Organization/ChoreTypes` (chore eligibility rules: gender/officer/waiver). No `ShiftCategory`/`EligibilityRule` CRUD home exists.
4. **Duplicate / legacy generations of the same thing.** Shift calendars: Excel vs legacy Month/Week/Day. Chore calendars **×3**: `/Calendar/Chores` (canonical) vs `/Public/Chores` (flag-off fallback) vs `/Chores/Calendar` (orphan twin). On-duty **×2**. Two Owner landing pages (`/Owner/Index` "legacy" vs `/Owner/Hub`). `Config` vs `Settings`. Grant domain across **4+ pages** (Permissions / Hub.Grants / RoleTemplates / PermissionSimulator). Data export **×3** (Backup / DataLifecycle / ExportUserData).
5. **Settings is split and mislabeled.** Sidebar "Settings" opens `/Admin/Config` (company defaults); the genuinely-named `/Admin/Settings` (hierarchy overrides on the *same two values*) has no link of its own. Notification preferences are split too: in-app engagement/mute/feed live in `/My/NotificationCenter`, email digest/reminders live in `/My/Settings`, with no cross-link.
6. **Authorization mismatch — links lie.** Link-gate ≠ page-gate in multiple places: Owner FeatureFlags/Telemetry/DatabaseConsole/DataLifecycle are inside an `AdminAccess`-gated sidebar group but the pages require `SystemConfiguration`; EmailConfig requires `ConfigureEmailSettings`; `/Admin/Analytics` link is gated `ViewAnalytics` but the page requires `ViewJusticeTable`. Result: a visible link can 403, or an accessible page can be invisible. **This is a latent permission bug class, not just IA.**
7. **Orphans / dead pages.** `/Schedule/Index` (non-functional toggles + iframes legacy calendars), `/Calendar/Index`, `/Calendar/Week`, `/Calendar/Day`, `/Chores/Calendar`, `/My` (richest personal dashboard, no nav), `/Game/Leaderboard` (whole Game feature is an undiscoverable easter egg), `/Requests/Swaps/Create` (redundant), `/Admin/Molecules/ApprovalSettings` (zero nav refs), `/Admin/Directors` (command-palette-only), `/Owner/LanguageEditMode` + `/Owner/SelectCompany|ClearCompanySelection` (plumbing stubs).
8. **Requests split by link-swapping, not routing.** `/My/Requests` (employee) and `/Requests/Index` (manager queue) share the label "Requests", swapped by a `ManagerHomeAccess` negate. `/Requests/Index` has no hard page gate (degrades to read-only), so employees can reach both — the boundary is blurred.
9. **Powerful tools buried as in-calendar controls.** Justice/equity analytics, Draft mode, quick-entry eligibility, and distribution-list *management* (full CRUD!) are reachable only from inside one calendar's toolbar/dropdown — invisible to anyone who doesn't open that calendar. `/Calendar/ManageDistributionLists` has zero nav presence.
10. **Misfiled configuration.** `Duty Rotations` sits in the *calendar* nav group though it's `/Admin/*` config; `HomeTypes`/`SetupTasks` are scheduling config filed under generic Admin; `GameConfig` gets equal "Infrastructure" weight as `DatabaseConsole`. Duty config is scattered across 3 places (DutyRotation + Organization/DutyTypes + OnDuty-type config inside Config).

---

## 2. The feature universe, grouped by domain (persona-agnostic)

This grouping is the raw material; it is *not yet* a nav proposal. Gates noted as `grant`/`flag`.

### A. Scheduling — view & assign (operational core)
- **Shift calendar** — Excel (`/Calendar/Shifts`, flag) + legacy **Month/Week/Day** (`/Calendar/*`). Assign/unassign, view modes, shift/user layout. `AssignShifts`(137).
- **Shifts Management / Table roster** — `/Calendar/Table`, `ManagerHomeAccess` (nav link only appears when Excel flag splits URLs).
- **Company Overview** — `/Calendar/Overview`, read-only company-wide; day-note modal. (Currently in "My Shifty" — arguably belongs near Management.)
- **Chore calendar** — `/Calendar/Chores` (canonical) / `/Public/Chores` (fallback). `ViewChores` / edit `AssignChores`.
- **On-Duty / On-Call calendar** — `/Calendar/OnCall` / `/Public/OnDuty`, area-scoped. `ViewDuties` / `ManageOnDuty`/`EditOnCallCalendar`.
- **In-calendar sub-features** — Justice/equity drawer (`ViewJusticeTable`), Draft mode, Quick-entry eligibility (`AssignShifts`+flags), Distribution-list *filter*.
- **Per-slot assignment drill-down** — `/Assignments/Manage`, `ManagerHomeAccess` (contextual).

### B. Scheduling — planning & templates
- **Shift Plans (Programs)** `/Owner/Programs` · **Master Programs** `/Owner/MasterPrograms` (button-only) · **Blueprints** `/Owner/Blueprints` (shift-category defs) — all `ManagerHomeAccess`, Owner-namespaced but Management-surfaced.
- **Chore Templates** `/Admin/Organization/ChoreTemplates` · **Duty Rotations** `/Admin/DutyRotation` (flag) · **Home rotation types** `/Admin/HomeTypes`.

### C. Scheduling — definitions & eligibility (the config layer)
- **Shift categories** — defined in Blueprints; `ManageShiftCategories`(136).
- **Chore types / categories / eligibility / exemptions** — `/Admin/Organization/ChoreTypes`, `EditChoreTypes` (gender/officer rules + per-user waivers).
- **Duty types** — `/Admin/Organization/DutyTypes`.
- **Job types** — `/Admin/Organization/JobTypes`, `ManageJobTypes`.
- **Shift groupings** — `/Admin/Organization/ShiftGroupings`, `ManageShiftGroupings`.
- **Per-user eligibility membership** — `DoesShifts`/`DoesChores` + category multiselect, edited in **People** (`/Admin/Users`). ← *the split that creates Issue 3*.

### D. Requests & approvals
- **My requests** (time-off + swaps) `/My/Requests` · **Manager approval queue** `/Requests/Index` (`ManagerHomeAccess`) · **Propose swap** `/Requests/Swaps/Create` (orphan) · **Approval rules** `/Admin/Settings/ApprovalRules` (flag) + **molecule approval threshold** `/Admin/Molecules/ApprovalSettings` (orphan) · **Director cross-company pending** `/Director/NotificationHub`.

### E. People
- **Users roster** `/Admin/Users` (mega-page, ~3200 LOC): add/activate/deactivate, role-template, job-type, AccountType, DoesShifts/DoesChores + categories, gender, password/unlock, delete/force-delete, **multi-company membership**, **join-request approval**, CSV import/export. `ManagerHomeAccess` + per-row `AdminAccess`/`EditCompanyUsers`.
- **Directors assignment** `/Admin/Directors` (palette-only), `AssignRoles`.

### F. Organization structure
- **Hierarchy** (Projects/Areas/Molecules/Departments/Companies/Stores) — `/Admin/Organization/*` children + `/Admin/Companies` (top-level). Gates: `EditArea`/`EditMolecule`/`ManageDepartments`/`EditCompany`/`ManageStores`.
- **Hierarchy view** (read-only) `/Admin/Organization/Hierarchy` (duplicates the hub tree) · **Area palette** `/Admin/Organization/AreaPalette`, `EditAreaCalendarPalette`.

### G. Identity & access (permissions)
- **Roles** `/Admin/Organization/Roles` (+Assign) `AssignRoles` · **Grants** `/Admin/Organization/Grants` (+Assign) `ViewGrants`/`AssignGrants` · **Role Templates** `/Owner/Hub/RoleTemplates` · **Permission Simulator** `/Owner/Hub/PermissionSimulator` · **Permissions dashboard** `/Owner/Permissions` (read-only stats) · **Locked Users** `/Owner/LockedUsers`. (4+ overlapping grant surfaces.)

### H. Insights / observability
- **Analytics (Justice Table)** `/Admin/Analytics` (`ViewJusticeTable`) · **Audit Log** `/Admin/AuditLog` + **Audit Search** `/Owner/Hub/AuditSearch` · **Telemetry** `/Owner/Telemetry` (`SystemConfiguration`) · **System Health** `/Owner/SystemHealth`.

### I. Company & system configuration
- **Company settings** (rest/cap + OnDuty types) `/Admin/Config` · **Hierarchy settings overrides** `/Admin/Settings` (`ViewSettings`) · **Feature Flags** `/Owner/FeatureFlags` (`SystemConfiguration`) · **Email Config** `/Owner/EmailConfig` (`ConfigureEmailSettings`) · **Email Templates** `/Owner/EmailTemplates` · **Griffin/ADFS** `/Owner/GriffinConfig` · **Game Config** `/Owner/GameConfig` · **Area Config** `/Owner/AreaConfig` · **Language Management** `/Owner/LanguageManagement` · **Announcements** `/Admin/Announcements` · **Setup Tasks** `/Admin/SetupTasks` (flag).

### J. Data & ops
- **Backup / Prepare-for-update** `/Owner/Backup` · **Data Lifecycle** (archive/purge) `/Owner/DataLifecycle` (`SystemConfiguration`) · **Database Console** `/Owner/DatabaseConsole` (`SystemConfiguration`, highest-risk) · **Export User Data (GDPR/DSAR)** `/Owner/Hub/ExportUserData`.

### K. Personal
- **Home dashboard** `/Home/Index` (role-branched) · **My overview** `/My` (orphan, richest personal page) · **Profile** `/My/Profile` · **My Settings** `/My/Settings` · **Notification Center** `/My/NotificationCenter` (inbox + in-app prefs + iCal feed) · **API Keys** `/My/ApiKeys` (buried) · **Onboarding** `/My/Onboarding` · **Help hub + 4 spokes** `/My/Help*` · **My Groups** `/MyTeam` · **Friends** `/Friends` (flag) · **Game leaderboard** `/Game/Leaderboard` (orphan) · **Feedback** `/Public/Feedback`.

### L. Context / utility (header + cross-cutting)
- **Context switcher** (active company, flag) · **Language toggle** · **Theme toggle** · **Decision ribbon** · **Notification bell** (+ unread count) · **View-As-Manager** `/Director/ViewAsMode` · **Company Filter** `/Director/CompanyFilter`. *(Three overlapping "which company am I viewing" mechanisms for directors: ContextSwitcher / CompanyFilter / View-As.)*

---

## 3. Orphan & duplicate ledger (cleanup targets)

**Confirmed orphans / dead:** `/Schedule/Index`, `/Calendar/Index`, `/Calendar/Week`, `/Calendar/Day` (when Excel on), `/Chores/Calendar`, `/My` (no nav), `/Game/Leaderboard`, `/Requests/Swaps/Create`, `/Admin/Molecules/ApprovalSettings`, `/Owner/LanguageEditMode`, `/Owner/SelectCompany`, `/Owner/ClearCompanySelection`. **Palette-only:** `/Admin/Directors`.

**Duplicate clusters to collapse to one canonical home each:**
- Shift calendars (Excel ↔ legacy Month/Week/Day) · Chore calendars ×3 · On-duty ×2
- Owner landing ×2 (Index legacy ↔ Hub) · Admin landing (Admin Hub ↔ sidebar ↔ site.js palette = 3 nav systems)
- Config ↔ Settings (rest/cap) · ApprovalRules ↔ ApprovalSettings
- Grant domain ×4 (Permissions / Hub.Grants / RoleTemplates / PermissionSimulator) · Organization/Hierarchy ↔ Org hub tree
- Data export ×3 (Backup / DataLifecycle / ExportUserData) · Notification prefs ×2 (NotificationCenter ↔ My/Settings)

**Latent permission bug (track separately from IA):** link-gate ≠ page-gate for Owner FeatureFlags/Telemetry/DatabaseConsole/DataLifecycle/EmailConfig and Admin Analytics — must be reconciled when the nav is rebuilt so visibility matches access.
