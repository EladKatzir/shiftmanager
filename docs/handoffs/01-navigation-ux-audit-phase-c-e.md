# Session Handoff — Navigation & UX Audit: Phase C/D/E (Playwright Testing)

## Goal
Run Playwright-based end-to-end workflow tests for all 4 priority roles (Phase C), secondary roles (Phase D), and cross-cutting UX concerns (Phase E) as defined in the Navigation & UX Audit spec.

Full spec: `docs/superpowers/specs/2026-04-01-navigation-ux-audit-design.md`

---

## Current Status
**Phase A code fixes: ALL DONE (verified 2026-04-01)**
- A1: Roles + Grants buttons already in `Pages/Admin/Organization/Index.cshtml` (gated by `AssignRoles` / `ViewGrants`)
- A2: People sidebar already uses `ViewUsers` gate in `Pages/Shared/_Layout.cshtml`
- A3: `deptlead.pie@test` already uses `"DepartmentLead"` template in `Data/SeedData/QaTestUserSeed.cs`

**Phase C/D/E testing: NOT STARTED**

No Playwright scripts exist. No test results exist. This is a greenfield testing task.

---

## Phase C — Priority Role Workflow Audit

Test each role with Playwright. Capture screenshots at each step. Evaluate against the 10 criteria in the spec.

| Role | Account | Password |
|---|---|---|
| Employee (חייל) | emp.tz.alhut@test | Test1234! |
| Lead (מפ"צ) | mgr.alhut.tz@test | Test1234! |
| BRDirector (קב"ר) | mgr.br.hir@test | Test1234! |
| AreaAdmin (קב"ב) | areaadmin@test | Test1234! |

Each role has ~12–15 workflows listed in the spec (E1–E15, L1–L15, B1–B12, A1–A15). Key evaluation criteria per workflow: reachability ≤3 clicks, first-time clarity, empty state quality, error handling, Hebrew labels, active sidebar state, breadcrumbs, confirmation feedback, dark mode, collapsed sidebar.

---

## Phase D — Secondary Role Audit

| Role | Account | Password |
|---|---|---|
| Director | dir.alhut@test | Test1234! |
| MoleculeAdmin | moladmin.oren@test | Test1234! |
| Owner | test.owner@shifty.test | TestOwner123! |
| DepartmentLead | deptlead.pie@test | Test1234! |
| Assigner | assigner.oren@test | Test1234! |
| Trainee | trainee.alhut@test | Test1234! |
| NoGrants (edge) | nogrants@test | Test1234! |

---

## Phase E — Cross-Cutting UX

- **E1 Hebrew RTL** — Switch to Hebrew, trace Employee + Lead workflows, verify all labels, RTL layout of sidebar/forms/tables/modals
- **E2 Empty States** — Login as Employee in Test Company Empty, navigate to Schedule/Requests/Chores/OnCall/Overview/My Groups
- **E3 First-Time Experience** — Fresh test user login → onboarding wizard → Home
- **E4 Error Edge Cases** — Invalid vacation, Access Denied pages, locked/deactivated user login
- **E5 Dark Mode** — Toggle dark mode, trace Employee + Lead, check for dark-on-dark text bugs (known recurring issue)

---

## Playwright Constraints

- **NEVER `taskkill //F //IM chrome.exe`** — kills ALL user Chrome sessions. Use unique `--user-data-dir` temp dir or ask user to close the specific window.
- Use `wait_for_selector` before clicking calendar slots (dynamic JS rendering)
- SignalR-dependent updates may need extra wait after assignment operations
- App base URL: typically `http://localhost:5000` or IIS-hosted

---

## Deliverables

1. Screenshot-documented workflow traces for all 4 priority roles
2. Issue catalog categorized by severity (blocker, major, minor, cosmetic)
3. Fix implementations for all blocker/major issues found
4. Updated plan file tracking progress

---

## Key Files

- `docs/superpowers/specs/2026-04-01-navigation-ux-audit-design.md` — full spec with all workflow tables and evaluation criteria
- `Data/SeedData/QaTestUserSeed.cs` — test user definitions
- `Pages/Shared/_Layout.cshtml` — sidebar
- `Pages/Admin/Organization/Index.cshtml` — Organization hub with Roles/Grants buttons
