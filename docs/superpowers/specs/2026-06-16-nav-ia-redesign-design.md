# Navigation & Information-Architecture Redesign — Design

**Created:** 2026-06-16 · **Branch:** `dev` · **Workstream 1 of 4** (IA / page-inventory rationalization)
**Decision:** Model B (domain hubs) + command palette, hardened by a six-persona advocacy pass.
**Companion inputs:** feature inventory + diagnosis → `2026-06-16-nav-ia-feature-inventory.md` (read first).
**Addresses:** Issue 2 (redesign nav + design-bar after categories/chores/etc.) and Issue 3 (a discoverable home for shift-category eligibility). Issue 3 is fully resolved here by the single Eligibility home (§6.5).

> User framing: *"We have a full feature list of everything we can do — where should each feature live, and how does the user access it comfortably?"* This is feature-driven IA: organize by what a capability **is**, gate by role, give every capability exactly **one canonical home**, and use the command palette as the fast path.

---

## 1. Goals & non-goals

**Goals**
- Replace today's **org-chart-shaped** nav (mirrors code/grants: `Owner` area, `Admin` area, `Organization` folder) with **domain-shaped** nav (mirrors what users do).
- Give every capability one canonical home; collapse the duplicate clusters; remove the orphans.
- Make Issue-3 eligibility a first-class, discoverable surface.
- Keep the heaviest daily paths (employee "my week", lead/assigner board, manager approvals, operator health) at ≤1–2 clicks.
- Fix the structural defect where a nav link can 403 (link-gate ≠ page-gate).

**Non-goals (this workstream)**
- The *visual* sidebar restyle (Workstream 2) and the *design-bar/header* restyle (Workstream 3) consume this IA but are separate. This doc defines **structure, placement, labels, and the cross-cutting behaviors**, not pixels.
- No new scheduling/eligibility *business logic*; we relocate and unify existing capabilities (one new surface: the category-first eligibility editor, assembled from existing pieces).

---

## 2. The decision: Model B + command palette

Top-level navigation is organized by **domain hub**. Each hub is a set of tabs. **Grants decide which hubs and which tabs a user sees** (via policy-derived visibility, §6.1). A persistent **Ctrl-K command palette** (the shortcut already stubbed in the sidebar footer) is the fast path to any destination for power users.

Rejected alternatives: **Model A (persona lanes)** — keeps the dual-role lane-hop and still needs deep hubs; **Model C (lean + palette only)** — weak for "what *can* I do here?" discovery. Model B borrows C's palette as an accelerator.

---

## 3. The seven principles (these resolve the persona conflicts)

1. **P1 — The calendar is the cockpit.** Assignment-time tools (Draft mode, Justice/equity drawer, quick-entry eligibility, distribution-list *filter*) stay as in-calendar toolbar controls. Hubs may *signpost* them (a discoverable entry that deep-links into the calendar with the tool open) but never *replace* the in-context control. *(Lead, Assigner)*
2. **P2 — Visibility == access.** A nav element renders **iff** the current user satisfies the **destination page's own authorization policy**, queried live (`IAuthorizationService.AuthorizeAsync`). A hub shows iff ≥1 of its tabs is authorized; a deep-link to an unauthorized tab redirects to the first authorized tab — **never a 403**. This makes the link-gate≠page-gate bug class structurally impossible. *(Owner, all)*
3. **P3 — One company-context primitive.** Two header controls: **Active** (singular current tenant) and **Viewing scope** (plural, "Viewing N of M companies"). **View-As** is a *mode*, entered from a company row, exited via a **persistent global banner** present on every page while active. Replaces today's 3–4 overlapping switchers. *(Director, Owner)*
4. **P4 — Hubs deep-link, never toll-booth.** A hub with one authorized child deep-links straight through (no landing page). A multi-tab hub remembers the user's last tab. Labels are role-aware: a viewer's slot reads **"My Schedule"**; an assigner's reads **"Scheduling"**. *(Employee, Assigner)*
5. **P5 — One Eligibility home (Issue 3).** All eligibility *authoring* — categories, rules (gender/officer/waiver/exemptions), and per-user membership — lives in `Scheduling ▸ Eligibility`, with **By-category** and **By-person** views that always show the **reason** a person is (in)eligible. The calendar only *consumes* eligibility (inline quick-entry hint). The People roster keeps a **read-only** "Eligible for…" summary that deep-links here. Employees get a read-only "why am I (in)eligible?" on the shift/chore itself. *(Admin, Assigner, Employee)*
6. **P6 — Danger zone.** Irreversible/high-blast-radius operations (purge, raw-SQL write, prepare-for-update, GDPR export, backup) are visually segregated in `System ▸ Data & ops`, ordered last, with typed-confirmation for irreversible actions and read-only-by-default for the SQL console. *(Owner)*
7. **P7 — Home is the schedule-spine for everyone.** The orphaned rich `/My` timeline merges into Home as the personal spine; role widgets (who's-on-shift, approval counts, oversight) stack on top, never replace it. Director Home = cross-company oversight board; Operator default landing = `Insights ▸ Health`. *(Employee, Director, Owner)*

---

## 4. Target top-level navigation

Order is by descending daily frequency for the typical authorized user. Everything is policy-gated (P2).

| Slot | Role-aware? | Authorized for | Default landing |
|---|---|---|---|
| **Home** | yes (widgets) | everyone | schedule-spine dashboard |
| **My Schedule ⇄ Scheduling** | yes (label+depth) | everyone (viewer) / assigners (hub) | viewer → own week; assigner → last tab |
| **Requests** | yes (scope) | everyone | own requests; managers/dirs → approvals |
| **People** | no | `ViewUsers`/manager+ | Roster |
| **Organization** | no | `ViewHierarchy`/`EditArea`+ | Hierarchy |
| **Access** | no | grant/role admins | Roles |
| **Insights** | no | analytics/audit/ops grants | Health (ops) / Analytics (mgr) |
| **System** | no | `SystemConfiguration`/`AdminAccess` | first authorized tab (no dashboard) |
| **Me** | no | everyone | Profile |

**Header (the "design-bar"):** `Active ▾` · `Viewing N/M ▾` (only when user oversees >1 company) · 🔔 notification bell (+ unread badge) · language toggle · theme toggle · (persistent **View-As exit banner** when impersonating). The legacy per-action Logout button moves into the `Me`/user menu (already exists in the sidebar footer). Decision Ribbon retained.

---

## 5. Hub-by-hub structure (with old → new mapping)

### 5.1 Home
- **Is:** role-aware landing. Spine = the personal week/timeline (merge `/My` + `/Home/Index`). Stacked widgets by grant: who's-on-shift (read-only version for all; richer for managers), approval-count, coverage gaps, director oversight board, operator health link.
- **Moves:** `/My` (orphan) → merged into Home. `/Home/Index` card-grid signpost → replaced by live spine. `WhoIsOnShift` widget → gains a read-only variant for grant-less users.

### 5.2 Scheduling  *(viewer label: "My Schedule")*
Verb-first tabs (Assigner's rule): **Planning = generators · Configuration/Definitions = the types they reference · Eligibility = who qualifies · Rules = policy.**

- **▸ Calendars** — Shifts (Excel canonical) · Chores (one canonical) · On-Duty (one canonical) · **Table roster** (promoted to first-class — kill the flag-conditional disappearing link) · **Overview** (pulled out of "My Shifty"). In-calendar toolbar keeps Draft, Justice, quick-entry, DL-filter (P1). *Legacy `/Calendar/Month|Week|Day`, `/Schedule/Index`, `/Calendar/Index`, `/Chores/Calendar` removed from nav (orphans).*
- **▸ Planning** — **Shift Plans** (rename `/Owner/Programs`) · **Master Plans** (rename `/Owner/MasterPrograms`) · **Chore Templates** · **Duty Rotations**. *(All leave the `/Owner/*` namespace lie.)*
- **▸ Definitions** — **Shift Blueprints** (moved here from "planning" — a blueprint defines a *type*, it doesn't schedule) · **Job Types** (↗ canonical home is Organization; deep-linked here) · **Shift Groupings** · **Duty Types** · **Home rotation types**.
- **▸ Eligibility** — see §6.5. Shift Categories & rules · Chore types/rules/exemptions · **Membership** (By-category / By-person) · **Distribution Lists** (promoted from the AJAX-only modal to a managed surface; in-calendar filter stays).
- **▸ Rules** — scope-aware policy page (Company/Molecule/Area, effective-vs-overridden): rest-hours, weekly-cap, vacation-approval thresholds. **Merges `/Admin/Config` + `/Admin/Settings` + `/Admin/Settings/ApprovalRules` + the orphaned `/Admin/Molecules/ApprovalSettings`.** (System keeps a deep-link.)

### 5.3 Requests
- **Is:** one role-aware top-level slot. Default = **your own** requests (time-off + **swaps folded in**, startable from a shift). Managers/directors additionally see the **approvals queue** with a live badge; directors default to **"all my companies"** scope (P3). Hard rule: an employee is never shown the approval queue.
- **Moves:** `/My/Requests` + `/Requests/Index` unified into one scope/role-aware hub. `/Requests/Swaps/Create` (orphan) folded in. `/Director/NotificationHub` → becomes the "all companies" scope of Approvals (renamed out of "Notification"). The split-by-link-swap defect (#8) is replaced by scope-driven divergence with a hard page gate.

### 5.4 People
- **▸ Roster** — `/Admin/Users`, **slimmed**: identity, role-template, job-type, AccountType, gender, password/unlock, activate/deactivate, delete. Per-row deep-links to *Eligibility* and *Access/Grants* (no inline eligibility multiselect). *(Splitting the 3200-LOC page is in-scope; see §8.)*
- **▸ Join requests** — split out of the roster into its own approval queue.
- **▸ Companies** — multi-company membership (`CompanyMembership`). **Renamed from "Membership"** to avoid colliding with eligibility-membership.

### 5.5 Organization  *(structure only)*
- **▸ Hierarchy** — `/Admin/Organization` tree becomes the **editor**: Projects/Areas/Molecules/Departments/Companies/Stores are inline CRUD *on the tree*, not 16 sibling buttons. The read-only `/Admin/Organization/Hierarchy` twin is absorbed.
- **▸ Job Types** — `/Admin/Organization/JobTypes` (canonical; deep-linked from Scheduling ▸ Definitions).
- **Moves/demotions:** `AreaPalette` → demoted into the Area editor ("Calendar colors" sub-panel), not a top tab. `ShiftGroupings`, `DutyTypes`, `ChoreTypes`, `ChoreTemplates` leave Organization → Scheduling (Definitions/Eligibility/Planning). `/Admin/Companies` → folds into the Hierarchy tree.

### 5.6 Access  *(the permissions domain — collapses 4+ scattered surfaces)*
- **▸ Roles** (`/Admin/Organization/Roles` +Assign) · **▸ Grants** (`/Admin/Organization/Grants` +Assign) · **▸ Templates** (`/Owner/Hub/RoleTemplates` — *authoring* stays `SystemConfiguration`-gated; admins assign) · **▸ Simulator** (`/Owner/Hub/PermissionSimulator` — rescued from palette-only) · **▸ Directors** (`/Admin/Directors` — rescued from palette-only) · **▸ Locked users** (`/Owner/LockedUsers`).
- Absorbs the read-only `/Owner/Permissions` stats into the Grants tab header.

### 5.7 Insights  *(observation — read surfaces; opens on Health for operators, Analytics for managers)*
- **▸ Health** (`/Owner/SystemHealth` + `/Owner/Index` quick-stats merged) · **▸ Telemetry** (`/Owner/Telemetry`) · **▸ Audit** (`/Admin/AuditLog` + `/Owner/Hub/AuditSearch` unified; one-click drill to read-only DB console) · **▸ Analytics** (`/Admin/Analytics` Justice Table; managers deep-link here; the in-calendar Justice drawer stays per P1).

### 5.8 System  *(mutation — write surfaces; opens on first authorized tab, NO landing dashboard)*
- **▸ Feature flags** (`/Owner/FeatureFlags`) · **▸ Integrations** (one tab, sub-sectioned: Email = EmailConfig+EmailTemplates; SSO = Griffin/ADFS; **Game = sub-section, not a peer tab**) · **▸ Localization** (`/Owner/LanguageManagement`; LanguageEditMode folds in as a mode) · **▸ Content & config** (low-frequency: AreaConfig, Announcements, SetupTasks, SeedData) · **▸ ⚠ Data & ops** (Danger zone §6.7: Backup/Prepare-for-update, Data Lifecycle archive/purge, Export User Data, Database Console).
- **Kills both legacy landings** (`/Owner/Index` legacy + `/Owner/Hub`). There is no "Owner area" — only System + Insights (+ shared Access). The Owner Company Selector becomes the persistent Active-context banner (P3).

### 5.9 Me  *(everyone)*
- **▸ Profile** · **▸ Settings** (unified: **all** notification prefs — in-app engagement/mute AND email digest/reminders — in one place; military rank; API-keys card) · **▸ Groups** (`/MyTeam`) · **▸ Friends** (flag) · **▸ Help** (hub + spokes) · API Keys (under Settings) · Onboarding (flow, no nav) · Feedback (moved out of `/Public/` into Help).
- **Notifications inbox:** entry is the **header bell** (1 tap from every page, unread badge) — *no separate sidebar/Me item* (per §9). The old `/My/NotificationCenter` page is retired; its inbox → bell, its preferences → `Me ▸ Settings`.

---

## 6. Cross-cutting systems

### 6.1 Policy-derived visibility (the permission-bug fix)
> A nav element renders **iff** the user satisfies the **destination page's own policy**; visibility is *derived from* the page, never hand-declared on the nav.
- Implement a nav model where each entry carries its destination's policy name; the layout calls `IAuthorizationService.AuthorizeAsync(user, policy)` per candidate entry.
- Hub shows iff ≥1 tab authorized (no empty shells). Active-tab/breadcrumb derive from the authorized set. Deep-link to an unauthorized tab → redirect to first authorized, never 403.
- Reconciles the known mismatches: Owner FeatureFlags/Telemetry/DatabaseConsole/DataLifecycle (`SystemConfiguration`), EmailConfig (`ConfigureEmailSettings`), Analytics (link `ViewAnalytics` vs page `ViewJusticeTable`), AuditLog (`ViewAuditLog` vs `ManagerHomeAccess`). **Each page's policy becomes the single source of truth.**

### 6.2 Company-context primitive (P3)
- **Active** (header, singular): the current tenant. For Owners = company-mode across molecules; for Directors = molecule-mode within grant (contents role-derived, control shared).
- **Viewing scope** (header, plural): "Viewing N of M companies ▾" multiselect, persisted, governs lists/calendars/dashboards/**Requests default**. Shown only to users who oversee >1 company. Replaces `/Director/CompanyFilter` (page deleted).
- **View-As**: a *mode* (impersonation, reduced powers), entered from a company row in Home/oversight (+ palette command), exited via a **persistent global banner** on every page. Never adjacent to Active/Scope (different semantics: "be X" vs "look at X").
- The Owner Company Selector (ambient config-context for ~7 pages) = the same Active primitive, surfaced as a persistent banner inside System — eliminating the wrong-tenant-edit footgun.

### 6.3 Command palette (Ctrl-K)
- Promote the existing stub to a real palette: fuzzy search over all authorized destinations + verb commands ("view as <company>", "scope to <companies>", "requests: all companies", "open eligibility for <person>"). Authorized-only (respects P2). The escape hatch that keeps the sidebar short for everyone.

### 6.4 Calendar cockpit (P1)
- Draft mode, Justice/equity drawer, quick-entry eligibility, distribution-list filter remain in the calendar toolbar. Hubs add *signpost* entries that deep-link into the calendar with the relevant tool open (for discoverability by new assigners). Two doors to one capability is acceptable; one door (hub-only) is not.

### 6.5 Eligibility home (Issue 3) (P5)
The single discoverable answer to "who can do shift/chore category X" and "why can't person Y?". Lives at `Scheduling ▸ Eligibility`. **One page, two views:**
- **By category** — pick a category → its rules (gender/officer/waiver — reuse the chip UI from `ChoreTypes`) + the live "who's eligible" roster; add/remove **membership** inline.
- **By person** — search a person → every category they can/can't do, with the **reason** for each block (rule failed vs missing `DoesShifts`/`DoesChores` vs not-a-member); toggle inline.
- **Subsumes** today's three fragments: Blueprints' shift-category defs, ChoreTypes' rules+exemptions, and the roster's `DoesShifts`/`DoesChores` + category multiselect (which **moves out** of `/Admin/Users`). Distribution Lists live here too (a targeting/eligibility construct).
- **Consumption stays on the calendar** (inline quick-entry hint) and **a read-only "why" surface** appears on the shift/chore for employees (esp. military rank/gender, group accounts).

### 6.6 Home as schedule-spine (P7)
- Merge `/My` (rich timeline: today/this-week feed, conflict badges, count-vs-hours chart, detail drawer) into Home as the universal spine. Role widgets stack above/beside; a manager is still an employee with their own shifts. Director variant = cross-company oversight board (staffing gaps + fairness drift + pending counts, one row per company). Operator default landing = `Insights ▸ Health`.

### 6.7 Danger zone (P6)
- `System ▸ ⚠ Data & ops`: distinct visual identity (amber/red accent, ⚠ glyph, header warning that ops are audit-logged). **Tier 1 (reversible-ish):** Backup, GDPR export → single confirm. **Tier 2 (irreversible):** Data purge, raw-SQL write, prepare-for-update → **typed confirmation** (type company name / `PURGE`) + audit row with operator identity. DB console defaults read-only; entering write mode is itself logged+confirmed. Friction is the feature.

---

## 7. Role-aware experience (what each persona actually sees)

- **Employee / soldier:** Home (my week is the page) · My Schedule (deep-links to my week — *not* "Scheduling") · Requests (mine; swaps from a shift) · Me. Bell for notifications. Read-only "why (in)eligible?" on shifts. ~4 honest items, no hub-tax.
- **Lead (dual-role):** same calendar serves "my shifts" and "the team board" (view-mode, not nav split); toolbar cockpit (Justice/Draft/quick-entry); Requests serves approve-team + file-own on one page; short grant-gated sidebar.
- **Assigner/Scheduler:** Scheduling hub is the workspace (Calendars/Planning/Definitions/Eligibility/Rules); hub remembers last tab; Table & Approvals ≤2 clicks; Eligibility ends the three-page scavenger hunt.
- **Director:** Home = cross-company oversight; Requests defaults to "all my companies"; header Active + Viewing-scope controls; View-As from a company row with a persistent exit banner; Insights for cross-company fairness. No Director island — lenses on shared hubs.
- **Molecule/Area Admin:** People / Organization / Access / Scheduling(Eligibility,Rules,Definitions); the 16-button wall becomes a tree-editor; one Eligibility home owns rules+membership; Config↔Settings merged into scope-aware Rules.
- **Owner/Operator:** Insights ▸ Health is the landing; System for mutation (flags/integrations/localization/content/⚠data-ops); Access for templates/simulator/locked-users; one context banner; the 403-link bug gone.

---

## 8. Page-level actions (the work, summarized)

**Renames:** Programs→Shift Plans · MasterPrograms→Master Plans · "Notification Hub"→Approvals(all companies) · People "Membership"→"Companies".
**Merges/unify:** Home+`/My` · `/My/Requests`+`/Requests/Index`(+Swaps/Create) · `/Admin/Config`+`/Admin/Settings`+ApprovalRules+ApprovalSettings → Scheduling▸Rules · `/Admin/AuditLog`+AuditSearch → Insights▸Audit · `/Owner/SystemHealth`+`/Owner/Index` stats → Insights▸Health · EmailConfig+EmailTemplates → Integrations▸Email · `/Owner/Permissions` stats → Access▸Grants header · `/Owner/Index`+`/Owner/Hub` → deleted (distributed into System/Insights).
**Splits (page-level, not just nav):** `/Admin/Users` roster → Roster (slim) + Join-requests + (eligibility-membership → Eligibility). *Largest single lift; see risks.*
**New surface:** category-first Eligibility editor (By-category / By-person with reasons) — assembled from existing rule/exemption/membership pieces.
**Promotions:** Table roster → first-class · Overview → Scheduling · Distribution Lists → managed Eligibility surface · Directors & Permission Simulator → Access (out of palette-only).
**Removals from nav (orphans):** `/Schedule/Index` · `/Calendar/Index` · `/Calendar/Week|Day` · `/Chores/Calendar` · `/Public/Chores`/`Public/OnDuty` (canonicalize to one each) · `/Admin/Molecules/ApprovalSettings` · `/Owner/LanguageEditMode`/`SelectCompany`/`ClearCompanySelection` (plumbing) · `/Game/Leaderboard` (decide: gate behind a real flag or surface intentionally).
**Demotions:** AreaPalette → Area editor sub-panel · GameConfig → Integrations sub-section.

---

## 9. Forks resolved
- **Rules location** → `Scheduling ▸ Rules` (scope-aware), System deep-link. *(frequency/ownership: schedulers/admins tune it far more than operators.)*
- **Requests label** → one role-aware slot (defaults to own; managers also get approvals).
- **Notifications** → header bell = inbox; all preferences unified in `Me ▸ Settings`.

---

## 10. Constraints & non-negotiables
- **Localization:** every label uses `<loc key>`; add to BOTH `SharedResources.resx` and `SharedResources.he-IL.resx`; **grep the key before adding** (dup keys break localization tests). Many new hub/tab labels are required.
- **RTL + dark mode are first-class:** every nav/header change verified in Hebrew RTL and dark theme; dark-on-dark text is the #1 recurring visual bug here (see MEMORY.md CSS contrast rules).
- **Multi-company:** nav respects the active-company switch (`_tenantResolver.GetCurrentTenantId()`), per P3.
- **Grants/`<require-grant>`:** new nav items need correct policy keys; policy-derived visibility (P2) supersedes the hand-gated group pattern. No new grants are required by the IA itself (placement only); any that emerge follow `grant_change_checklist.md`.
- **`FinalProductPublish/` is generated** — never edit; regen via `scripts/Update-FinalProductPublish.ps1`.
- **Tests run sequential** (`xUnit.MaxParallelThreads=1`) to avoid ~233 spurious SQLite-contention fails.

---

## 11. Scope & phasing
This doc is **Workstream 1 (IA)**. It defines structure/placement/labels/behaviors. Suggested implementation sequencing (to be detailed by the writing-plans step — each phase independently shippable behind the existing flag discipline):
1. **Nav shell + P2 policy-derived visibility** (the new sidebar/hub/palette scaffolding + the visibility==access rule). Foundation; also fixes the 403-link bug.
2. **Scheduling hub** (highest-traffic; folds in the `/Owner/*` planning lie, canonicalizes calendars, promotes Table/Overview/Distribution-Lists).
3. **Eligibility home (Issue 3)** — the new By-category/By-person editor + roster read-only summary + employee "why" surface.
4. **People / Organization / Access** consolidation (incl. roster split, tree-as-editor, grant-surface collapse).
5. **Insights / System** (operator hubs, danger zone, landing reconciliation).
6. **Requests** unification + **company-context primitive** + **Home spine merge**.
7. Workstreams 2 (sidebar visual) & 3 (header/design-bar visual) layer on top.

---

## 12. Open questions / risks
- **Roster split is the biggest lift** (3200-LOC PageModel, ~14 capabilities). Risk of regression; should be its own carefully-tested phase. Confirm appetite before scheduling.
- **Policy-derived visibility** requires a nav model refactor (entries carry policy names) — touches `_Layout.cshtml` deeply. High value (kills a bug class) but central; sequence first and test hard.
- **Company-context primitive** unifies 3–4 existing mechanisms with *different semantics* — must preserve each axis (active vs scope vs impersonation); a naive merge is worse than the status quo.
- **Game feature** (player leaderboard, currently an undiscoverable easter egg loaded globally): decide intentionally — real flag + a home, or remove. Out of the IA critical path.
- **Per-company rollout:** the new nav should sit behind a flag so it can be enabled per company, matching existing discipline; flag-off = today's nav byte-for-byte until cutover.

---

## 13. Appendix
- Full capability inventory + systemic diagnosis: `docs/superpowers/specs/2026-06-16-nav-ia-feature-inventory.md`.
- Design method: six-persona advocacy pass (Employee, Lead, Assigner, Director, Molecule/Area Admin, Owner/Operator) over Model B; conflicts surfaced and resolved into the seven principles (§3).
