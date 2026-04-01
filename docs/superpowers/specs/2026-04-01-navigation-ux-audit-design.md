# Navigation & UX Audit — Design Spec

## Goal

Validate that every user role has clear, intuitive access to the features they need. Fix navigation gaps, trace key workflows end-to-end, and evaluate usability across the product.

## Target Outcomes

1. Core features reachable within 3 clicks for each role
2. First-time users can understand what's available without external guidance
3. Every major workflow validated end-to-end with screenshots
4. Empty states, errors, and confirmations guide the user clearly
5. Consistent terminology and navigation in both Hebrew and English
6. Users can recover from mistakes and always know what to do next

---

## Phase A: Fix Remaining Navigation Issues

Quick fixes identified during the sidebar audit but not yet implemented. **Phase A must be fully complete before Phase C testing begins** — several workflows depend on these fixes.

### A1. Add Roles & Grants to Organization Hub Quick Actions

**Problem:** `/Admin/Organization/Roles` and `/Admin/Organization/Grants` exist but are not linked from the Organization hub's Quick Actions grid.

**Fix:** Add two buttons to the Organization hub Quick Actions:
- "Manage Roles" → `/Admin/Organization/Roles` (gate: `AssignRoles`)
- "Manage Grants" → `/Admin/Organization/Grants` (gate: `ViewGrants`)

**Note:** Gate "Manage Grants" with `ViewGrants` (not `AssignGrants`) to match the page's `[Authorize(Policy = "Grant:ViewGrants")]`. The Assign sub-page is separately gated.

**Pre-existing inconsistency:** The existing Quick Action buttons (Manage Projects, Manage Areas, etc.) have NO grant gates — only the newly added HomeTypes button is gated. For consistency, the new Roles/Grants buttons follow the gated pattern. Gating all existing buttons is out of scope for this audit but noted as a future improvement.

### A2. People sidebar gate for Leads

**Problem:** The People sidebar item is gated by `ViewAllUsers` (molecule-scope, grant 34). Leads have `ViewUsers` (company-scope, grant 28) but not `ViewAllUsers`. They can access `/Admin/Users` (gated by `ManagerHomeAccess`) but can't find it from the sidebar.

**Fix:** Change the People sidebar gate from `ViewAllUsers` to `ViewUsers`. Also update the Admin section header grant list to include `ViewUsers`.

**Risk check:** Employee has `ViewCompanyUsers` (grant 117, different key), not `ViewUsers` (grant 28). No Employee will see the People link. All roles with `ViewUsers` also have `ManagerHomeAccess`, so the page authorization holds.

### A3. Create DepartmentLead test user

**Problem:** `deptlead.pie@test` in QaTestUserSeed is seeded with the "BRDirector" template (not "DepartmentLead"). No test user with the DepartmentLead (Template 9) role exists.

**Fix:** Add a DepartmentLead test user to QaTestUserSeed with the correct template, assigned to a Shikma tech company.

---

## Phase B: Test User Inventory

QaTestUserSeed provides 43+ users across most role templates. One gap identified (A3 above).

### Priority Role Test Accounts

| Role (HE) | Template | Email | Password | Company | JobType | Molecule |
|---|---|---|---|---|---|---|
| חייל | Employee | emp.tz.alhut@test | Test1234! | Tzafona | Alhut | Oren |
| חייל | Employee | emp.hit.text@test | Test1234! | Hitazmut | Text | Ella |
| חייל | Employee | emp.hir.br@test | Test1234! | Hir | BR | Oren |
| מפ"צ | Lead | mgr.alhut.tz@test | Test1234! | Tzafona | Alhut | Oren |
| מפ"צ | Lead | mgr.text.tz@test | Test1234! | Tzafona | Text | Oren |
| קב"ר | BRDirector | mgr.br.hir@test | Test1234! | Hir | BR | Oren |
| קב"ב | AreaAdmin | areaadmin@test | Test1234! | Tzafona | (none) | Oren |

### Secondary Role Test Accounts

| Role | Template | Email | Password | Source |
|---|---|---|---|---|
| Director | Director | dir.alhut@test | Test1234! | QaTestUserSeed |
| MoleculeAdmin | MoleculeAdmin | moladmin.oren@test | Test1234! | QaTestUserSeed |
| DepartmentLead | DepartmentLead | (new — see A3) | Test1234! | QaTestUserSeed (to be added) |
| Assigner | Assigner | assigner.oren@test | Test1234! | QaTestUserSeed |
| Trainee | Trainee | trainee.alhut@test | Test1234! | QaTestUserSeed |
| Owner | Owner | test.owner@shifty.test | TestOwner123! | TestDataSeed |
| NoGrants (edge) | (none) | nogrants@test | Test1234! | QaTestUserSeed |

---

## Phase C: Priority Role Workflow Audit

For each of the 4 priority roles, trace every key workflow with Playwright. Capture screenshots at each step. Evaluate against the target outcomes.

**Prerequisites:**
- Phase A must be fully implemented (L9 depends on A2)
- `DutyRotationEnabled` feature flag must be active (for A10)
- All feature flags are enabled by default in test environment — verified

**Playwright notes:**
- Calendar shift assignment flows (L2, B4, A11) involve dynamic JS rendering and bottom-sheet interactions — use `wait_for_selector` before clicking slots
- SignalR-dependent updates may need extra wait time after assignment operations
- Test all calendar view modes (week is the default; month and day should also be verified)

### C1. חייל (Employee) — Workflows

| # | Workflow | Start → Goal | Expected Path |
|---|---|---|---|
| E1 | View my shift schedule | Sidebar → Schedule | 1 click |
| E2 | View company overview | Sidebar → Company Overview | 1 click |
| E3 | Request time off | Sidebar → Requests → Create | 2-3 clicks |
| E4 | Request shift swap | Sidebar → Requests → Create Swap | 2-3 clicks |
| E5 | View chores calendar | Sidebar → Chores | 1 click |
| E6 | View on-duty calendar | Sidebar → On-Duty | 1 click |
| E7 | Update my profile | Sidebar → My Profile | 1 click |
| E8 | Change my settings | Sidebar → My Settings | 1 click |
| E9 | View notifications | Header bell icon → Notification Center | 1 click |
| E10 | Get help | Sidebar → Help | 1 click |
| E11 | Submit feedback | Sidebar → Help → Feedback card | 2 clicks |
| E12 | First-time onboarding | Login → Onboarding wizard | Automatic |
| E13 | View my groups/team | Sidebar → My Groups | 1 click |
| E14 | View friends | Sidebar → My Friends | 1 click (requires FriendshipsEnabled FF) |
| E15 | Calendar view modes | Schedule → switch week/month/day | 2 clicks |

**Evaluate:** Empty states (no shifts, no requests), Hebrew labels, calendar navigation (date picker, view modes), request creation form clarity.

### C2. מפ"צ (Lead) — Workflows

**Dependency:** L9 requires Phase A2 (People sidebar gate fix) to be implemented first.

| # | Workflow | Start → Goal | Expected Path |
|---|---|---|---|
| L1 | View command center dashboard | Sidebar → Command Center | 1 click |
| L2 | Assign soldier to shift | Sidebar → Schedule → Click slot → Assign | 3 clicks |
| L3 | View/manage shift plans | Sidebar → Shift Plans | 1 click |
| L4 | View/manage blueprints | Sidebar → Blueprints | 1 click |
| L5 | Approve vacation request | Sidebar → Requests → Pending tab → Approve | 3 clicks |
| L6 | Approve shift swap | Sidebar → Requests → Pending tab → Approve | 3 clicks |
| L7 | View analytics | Sidebar → Analytics | 1 click (via Admin section) |
| L8 | View audit log | Sidebar → Audit Log | 1 click |
| L9 | Manage users in company | Sidebar → People → Users list | 1-2 clicks |
| L10 | Assign chores | Sidebar → Chores → Click slot → Assign | 3 clicks |
| L11 | View organization structure | Sidebar → Organization | 1 click (via Admin section) |
| L12 | Navigate Blueprints → Programs | Blueprints page → "Shift Plans" button | 1 click |
| L13 | Navigate Programs → Blueprints | Programs page → "Blueprints" button | 1 click |
| L14 | Use Assignments Manager | Command Center → Assignments card | 2 clicks |
| L15 | Manage roles | Sidebar → Organization → Manage Roles | 2 clicks |

**Evaluate:** Shift assignment flow clarity, approval workflow, cross-navigation between scheduling pages, empty program/blueprint states.

### C3. קב"ר (BRDirector) — Workflows

| # | Workflow | Start → Goal | Expected Path |
|---|---|---|---|
| B1 | View command center | Sidebar → Command Center | 1 click |
| B2 | Manage users | Sidebar → People → Users list | 1-2 clicks |
| B3 | Edit company settings | Sidebar → Companies | 1 click (via Admin section) |
| B4 | Assign BR shifts | Sidebar → Schedule → Click slot → Assign | 3 clicks |
| B5 | Approve vacations (BR + Hakam) | Sidebar → Requests → Pending → Approve | 3 clicks |
| B6 | Initiate shift swap | Sidebar → Requests → Create swap | 2-3 clicks |
| B7 | View all companies | Sidebar → Companies | 1 click |
| B8 | Reset user password | Sidebar → People → User → Reset | 3 clicks |
| B9 | Manage blueprints | Sidebar → Blueprints | 1 click |
| B10 | View analytics/reports | Sidebar → Analytics | 1 click |
| B11 | View audit log | Sidebar → Audit Log | 1 click |
| B12 | Manage directors | Command Center → Directors card | 2 clicks |

**Evaluate:** User management flow, company-scoped operations, approval breadth (BR + Hakam dual approvals via TargetJobTypeId sentinels).

### C4. קב"ב (AreaAdmin) — Workflows

**Prerequisite:** `DutyRotationEnabled` feature flag must be active for workflow A10.

| # | Workflow | Start → Goal | Expected Path |
|---|---|---|---|
| A1 | View command center | Sidebar → Command Center | 1 click |
| A2 | Use Director Tools | Sidebar → Director Tools → View As Mode | 2 clicks |
| A3 | Manage hierarchy | Sidebar → Organization → Hierarchy tree | 2 clicks |
| A4 | Create new company | Sidebar → Organization → Add Company modal | 2-3 clicks |
| A5 | Manage job types | Sidebar → Organization → Manage Job Types | 2 clicks |
| A6 | Manage duty types | Sidebar → Organization → Manage Duty Types | 2 clicks |
| A7 | Manage home types | Sidebar → Organization → Home Types | 2 clicks |
| A8 | Configure area settings | Sidebar → Settings | 1 click (via Admin section) |
| A9 | Manage announcements | Sidebar → Announcements | 1 click (via Admin section) |
| A10 | Manage duty rotations | Sidebar → Duty Rotations | 1 click (requires DutyRotationEnabled FF) |
| A11 | Assign shifts across companies | Sidebar → Schedule → Select molecule | 2 clicks |
| A12 | View all areas | Sidebar → Organization → Areas | 2 clicks |
| A13 | Manage stores | Sidebar → Organization → Manage Stores | 2 clicks |
| A14 | Manage grants | Sidebar → Organization → Manage Grants | 2 clicks |
| A15 | Manage roles | Sidebar → Organization → Manage Roles | 2 clicks |

**Evaluate:** Area-scoped operations, hierarchy management, Director Tools discoverability, cross-molecule operations.

### Evaluation Criteria for Each Workflow

For every workflow step, evaluate:

1. **Reachability** — Can the user reach the target in ≤3 clicks? Is the path obvious?
2. **First-time clarity** — Would a new user understand what to do without instructions?
3. **Empty state** — What shows when there's no data? Is it helpful or confusing?
4. **Error handling** — What happens on invalid input? Is the message clear?
5. **Hebrew consistency** — Are all labels localized? Any raw English keys showing?
6. **Active state** — Does the sidebar highlight the correct item?
7. **Breadcrumbs** — Can the user navigate back? Is the trail clear?
8. **Confirmation** — After completing an action, does the user know it succeeded?
9. **Dark mode** — Does the page render correctly in dark theme? (Given history of dark-on-dark text bugs)
10. **Mobile/collapsed sidebar** — On narrow viewport, can the user still reach the target?

---

## Phase D: Secondary Role Audit

Split into sub-phases based on role complexity.

### D1. Roles with significant unique pages (medium depth)

#### Director (dir.alhut@test)
| # | Workflow | Expected Path |
|---|---|---|
| D-Dir1 | Director Tools — View As Mode | Sidebar → Director Tools → View As Mode |
| D-Dir2 | Director Tools — Notification Hub | Sidebar → Director Tools → Notification Hub |
| D-Dir3 | Director Tools — Company Filter | Sidebar → Director Tools → Company Filter |
| D-Dir4 | Multi-company switching | Scope switcher → select company → verify data changes |
| D-Dir5 | Manage announcements | Sidebar → Announcements |
| D-Dir6 | View all areas | Sidebar → Organization → Areas |

#### MoleculeAdmin (moladmin.oren@test)
| # | Workflow | Expected Path |
|---|---|---|
| D-Mol1 | Configure molecule settings | Sidebar → Settings → molecule-level config |
| D-Mol2 | Manage chore types | Sidebar → Organization → Manage Chore Types |
| D-Mol3 | Manage shift groupings | Sidebar → Organization → Manage Shift Groupings |
| D-Mol4 | Manage home types | Sidebar → Organization → Home Types |
| D-Mol5 | Manage duty rotations | Sidebar → Duty Rotations |
| D-Mol6 | Create/deactivate users | Sidebar → People → Create user / Deactivate |
| D-Mol7 | Manage hierarchy | Sidebar → Organization → hierarchy tree |

#### Owner (test.owner@shifty.test)
| # | Workflow | Expected Path |
|---|---|---|
| D-Own1 | Owner Hub dashboard | Sidebar → Owner Administration |
| D-Own2 | Feature Flags management | Sidebar → Feature Flags |
| D-Own3 | System Health check | Sidebar → System Health |
| D-Own4 | Telemetry | Sidebar → Telemetry |
| D-Own5 | Database Console | Owner Hub → Quick Links → Database Console |
| D-Own6 | Email Configuration | Owner Hub → Quick Links → Email Config |
| D-Own7 | Backup management | Owner Hub → Quick Links → Backup |
| D-Own8 | Role Templates management | Owner Hub → Grants card → Role Templates |
| D-Own9 | Seed Data | Owner Hub → Seed Data card |
| D-Own10 | Locked Users | Owner Hub → Quick Links → Locked Users |
| D-Own11 | Language Management | Owner Hub → Quick Links → Languages |
| D-Own12 | Master Programs | Owner Hub → Quick Links → Master Programs |

### D2. Roles with minimal unique behavior (light touch)

| Role | Test Account | Verify |
|---|---|---|
| DepartmentLead | (new user from A3) | Sidebar shows correct items, tech shift assignment works, limited admin section |
| Assigner | assigner.oren@test | Sidebar matches Employee, chore assignment flow works within Chores calendar |
| Trainee | trainee.alhut@test | Sidebar matches Employee, no swap request option available |

### D3. Edge cases (verification only)

| Case | Test Account | Verify |
|---|---|---|
| NoGrants user | nogrants@test | What sidebar renders, what Home shows, no crashes |
| Locked user | locked@test | Login blocked with clear message |
| Deactivated user | deactivated@test | Login blocked with clear message |

---

## Phase E: Cross-Cutting UX Evaluation

After individual role testing, evaluate product-wide concerns:

### E1. Hebrew (RTL) Consistency
- Switch to Hebrew, trace Employee + Lead workflows
- Check all sidebar labels, page titles, breadcrumbs, form labels
- Check RTL layout of sidebar, forms, tables, modals, calendar grids

### E2. Empty States
- Login as Employee in Test Company Empty (`test-company-empty`)
- Navigate to: Schedule, Requests, Chores, On-Duty, Company Overview, My Groups
- Document what each empty state shows — is it helpful or just blank?
- Verify empty states have clear guidance ("No shifts scheduled — your schedule will appear here")

### E3. First-Time Experience
- Login as a fresh test user (first login triggers onboarding)
- Evaluate onboarding wizard: does it explain the app? Is it skippable?
- After onboarding, land on Home — is the next action clear?
- Does the Home page help the user understand what to do first?

### E4. Error & Edge Cases
- Submit invalid vacation request (past date, overlapping)
- Try to access pages without permission (direct URL → Access Denied page)
- Locked user login attempt → clear lock message
- Deactivated user login attempt → clear deactivation message
- Invalid form submission → validation messages visible and helpful

### E5. Dark Mode
- Toggle dark mode, trace Employee + Lead workflows
- Check for dark-on-dark text bugs (known recurring issue per MEMORY.md)
- Verify sidebar, cards, forms, calendars, modals all render with adequate contrast

---

## Deliverables

1. **Screenshot-documented workflow traces** for all 4 priority roles
2. **Issue catalog** — categorized by severity (blocker, major, minor, cosmetic)
3. **Fix implementations** for all blocker/major issues found
4. **Updated plan file** tracking progress through all phases
