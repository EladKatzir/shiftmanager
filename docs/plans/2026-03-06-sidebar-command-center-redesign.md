# Sidebar & Command Center Redesign — Implementation Guide

**Date:** 2026-03-06
**Status:** Reviewed, corrected, and ready for implementation (2026-03-06)
**Scope:** Sidebar navigation, Admin Hub (Command Center), Owner Panel, grant seeding, Director Hub removal

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Grant Seed Changes](#2-grant-seed-changes)
3. [Sidebar Layouts Per Role](#3-sidebar-layouts-per-role)
4. [Sidebar Implementation Details](#4-sidebar-implementation-details)
5. [Admin Hub (Command Center) Changes](#5-admin-hub-command-center-changes)
6. [Owner Panel Changes](#6-owner-panel-changes)
7. [Director Hub Removal](#7-director-hub-removal)
8. [Localization](#8-localization)
9. [Edge Cases & Warnings](#9-edge-cases--warnings)
10. [Implementation Order](#10-implementation-order)
11. [Testing Checklist](#11-testing-checklist)

---

## 1. Executive Summary

### What We're Doing

- **Unifying the landing experience**: Director Hub (`/Director/Index`) is scrapped. All admin-nav roles land on the **Admin Hub** (`/Admin/Index`), now branded **"Command Center"**.
- **Simplifying sidebars**: Each role gets a focused, minimal sidebar organized into named sections. Items removed from sidebar remain accessible via Command Center cards.
- **Adding Director Tools to Admin Hub**: ViewAsMode, NotificationHub, CompanyFilter move from Director Hub to a new section in Admin Hub.
- **Adding cards to Owner Panel**: Setup Tasks, Duty Rotations, Approval Rules cards added so they can be removed from the Owner sidebar.
- **Grant expansions**: BRDirector, Lead, Director, MoleculeAdmin get new monitoring/analytics grants.

### Roles Affected

| ID | Key | Display (EN) | Display (HE) | Sidebar Changes? |
|----|-----|-------------|-------------|------------------|
| 2 | BRDirector | BR Director | מפקד ב"ר | Yes |
| 3 | Lead | Squad Leader | מ"כ | Yes |
| 5 | Director | Platoon Leader | מ"מ | Yes |
| 7 | MoleculeAdmin | Molecule Admin | מנהל מול' | Yes |
| 8 | Assigner | Assigner | שיבוצניק | Yes |
| 9 | DepartmentLead | Department Lead | מפקד מחלקה טכנית | Yes |
| 10 | AreaAdmin | Area Admin | מנהל אזור | Yes |
| 11 | Owner | Owner | בעלים | Yes (minor) |

---

## 2. Grant Seed Changes

### File: `Data/SeedData/RoleTemplateSeed.cs`

#### New Grants to Add

| Template | Key | Grant Key | Grant ID | Scope | useOwnJobType | Code |
|----------|-----|-----------|----------|-------|---------------|------|
| 2 | BRDirector | ViewAllUsers | 34 | SAR | - | `grants.Add(G(2, 34, SAR));` |
| 2 | BRDirector | ViewAnalytics | 52 | SAR | - | `grants.Add(G(2, 52, SAR));` |
| 2 | BRDirector | ViewReports | 53 | SAR | - | `grants.Add(G(2, 53, SAR));` |
| 2 | BRDirector | ViewAuditLog | 59 | SAR | - | `grants.Add(G(2, 59, SAR));` |
| 3 | Lead | ViewAnalytics | 52 | ETM | Yes | `grants.Add(G(3, 52, ETM, useOwnJobType: true));` |
| 3 | Lead | ViewReports | 53 | ETM | Yes | `grants.Add(G(3, 53, ETM, useOwnJobType: true));` |
| 3 | Lead | ViewAuditLog | 59 | ETM | Yes | `grants.Add(G(3, 59, ETM, useOwnJobType: true));` |
| 5 | Director | ViewAnalytics | 52 | ETM | Yes | `grants.Add(G(5, 52, ETM, useOwnJobType: true));` |
| 5 | Director | ViewReports | 53 | ETM | Yes | `grants.Add(G(5, 53, ETM, useOwnJobType: true));` |
| 5 | Director | ViewAuditLog | 59 | ETM | Yes | `grants.Add(G(5, 59, ETM, useOwnJobType: true));` |
| 7 | MoleculeAdmin | ManageOnDuty | 122 | ETM | - | `grants.Add(G(7, 122, ETM));` |

#### Where to Insert (by template)

**BRDirector (template 2)** — append after line 386 (`grants.Add(G(2, 120, SAR));  // ViewSystemAlerts`):
```csharp
// Sidebar redesign: monitoring + people visibility
grants.Add(G(2, 34, SAR));   // ViewAllUsers
grants.Add(G(2, 52, SAR));   // ViewAnalytics
grants.Add(G(2, 53, SAR));   // ViewReports
grants.Add(G(2, 59, SAR));   // ViewAuditLog
```
Update template comment: 50 grants → **54 grants**

**Lead (template 3)** — append after line 329 (`grants.Add(G(3, 120, SAR));  // ViewSystemAlerts`):
```csharp
// Sidebar redesign: monitoring grants (molecule-scoped, own jobtype)
grants.Add(G(3, 52, ETM, useOwnJobType: true));   // ViewAnalytics
grants.Add(G(3, 53, ETM, useOwnJobType: true));   // ViewReports
grants.Add(G(3, 59, ETM, useOwnJobType: true));   // ViewAuditLog
```
Update template comment: 43 grants → **46 grants**

**Director (template 5)** — append after line 446 (`grants.Add(G(5, 24, ETM, useOwnJobType: true));  // ApproveExtendedLeave`):
```csharp
// Sidebar redesign: monitoring grants (molecule-scoped, own jobtype)
grants.Add(G(5, 52, ETM, useOwnJobType: true));   // ViewAnalytics
grants.Add(G(5, 53, ETM, useOwnJobType: true));   // ViewReports
grants.Add(G(5, 59, ETM, useOwnJobType: true));   // ViewAuditLog
```
Update template comment: 52 grants → **55 grants**

**MoleculeAdmin (template 7)** — append after line 542 (`grants.Add(G(7, 124, ETM));  // ManageHierarchy`):
```csharp
// Sidebar redesign: duty rotation management
grants.Add(G(7, 122, ETM));  // ManageOnDuty
```
Update template comment: 89 grants → **90 grants** (NOTE: the existing comment says "87" but actual count is 89 — fix the comment)

#### Grant Count Verification

After changes:
- BRDirector: 50 + 4 = **54**
- Lead: 43 + 3 = **46**
- Director: 52 + 3 = **55**
- MoleculeAdmin: 89 + 1 = **90** (pre-existing comment said 87 — stale, actual count is 89)

#### DECIDED: Director `useOwnJobType: true` (confirmed)

Director's ViewAnalytics/ViewReports/ViewAuditLog use `ETM` with `useOwnJobType: true`. This gives **molecule-wide analytics filtered to the Director's own job type**. A platoon leader sees analytics across all companies in their molecule, but only for their squad's job type — not other squads.

This is intentionally different from Director's ViewAllUsers (ETM without `useOwnJobType`), which gives cross-job-type user visibility for management purposes. Analytics/audit are squad-scoped by design.

Lead's `useOwnJobType: true` is also correct and consistent with all other Lead grants.

#### Insertion Order

Insert grants **bottom-up** (MoleculeAdmin first, then Director, then BRDirector, then Lead) to avoid line number shifts invalidating subsequent insertion points.

---

## 3. Sidebar Layouts Per Role

### Section Header Keys

| Section | Loc Key | EN | HE |
|---------|---------|----|----|
| Main section | `Calendar_MyCalendar` | "My Shifty" | "השיפטי שלי" | (existing, includes emoji prefix)
| Management | `Calendar_Management` | "Management" | "ניהול" | (existing)
| Admin | `Nav_Admin` | "Admin" | "ניהול" → change to "ניהול מערכת" | (existing — DO NOT re-add, edit HE value)
| Personal | `Nav_Personal` | "Personal" | "אישי" | **NEW**

### URL Variables (from `_Layout.cshtml`)

These existing variables MUST be reused for all calendar links:

| Variable | FF ON | FF OFF |
|----------|-------|--------|
| `shiftsCalendarUrl` | `/Calendar/Shifts` | `/Calendar/Month` |
| `shiftsTableUrl` | `/Calendar/Shifts` | `/Calendar/Table` |
| `choresCalendarUrl` | `/Calendar/Chores` | `/Public/Chores` |
| `onCallCalendarUrl` | `/Calendar/OnCall` | `/Public/OnDuty` |

**IMPORTANT:** When Excel Calendars FF is ON, `shiftsCalendarUrl` and `shiftsTableUrl` resolve to the SAME URL (`/Calendar/Shifts`). See [Edge Case #1](#edge-case-1-duplicate-schedule-links).

---

### Assigner (template 8) — Employee Nav

**No `AccessAdminNavigation` grant — uses the employee sidebar block.**

```
--- MY SHIFTY (Calendar_MyCalendar) ---
  Home              /                              [key: Home]
  Schedule          {shiftsCalendarUrl}            [key: Schedule]
  Company Overview  /Calendar/Overview             [key: CompanyOverview]
  Requests          /My/Requests                   [key: Requests]
--- OPERATIONS (Calendar_Management) ---
  Chores            {choresCalendarUrl}            [key: Chores]
  On-Call            {onCallCalendarUrl}            [key: OnDuty]
--- PERSONAL (Nav_Personal) ---
  My Groups         /MyTeam/Index                  [key: MyGroups]
  My Friends*       /Friends                       [key: MyFriends]          (FF-gated)
  My Profile        /My/Profile                    [key: MyProfile]
  My Settings       /My/Settings                   [key: My_Settings]
  Help              /My/Help                       [key: Help_NavLink]
```

**Changes from current employee sidebar:** Reordered. Requests moved into MY SHIFTY (daily action). Chores/On-Call grouped under OPERATIONS. Personal section added with section header.

**Grants driving visibility:** None — all items are unconditional for employee nav.

---

### BRDirector (template 2) — Admin Nav

```
--- MY SHIFTY (Calendar_MyCalendar) ---
  Command Center    /Admin/Index                   [key: Nav_CommandCenter]   NEW
  Schedule          {shiftsCalendarUrl}            [key: Schedule]
  Company Overview  /Calendar/Overview             [key: CompanyOverview]
  Requests          /Requests/Index                [key: Requests]
--- MANAGEMENT (Calendar_Management) ---
  Scheduled Shifts  {shiftsTableUrl}               [key: Calendar_ShiftsManagement]  (grant: ViewShifts) [*]
  Shift Plans       /Owner/Programs                [key: Nav_ShiftPlans]    NEW
  Chores            {choresCalendarUrl}            [key: Calendar_ChoresManagement]  (grant: ViewChores)
  On-Call           {onCallCalendarUrl}            [key: Calendar_OnDutyManagement]  (grant: ViewDuties)
--- ADMIN (Nav_Admin) ---                                                    NEW section header
  People            /Admin/Users                   [key: People]            (grant: ViewAllUsers) [NEW grant]
  Analytics         /Admin/Analytics               [key: Analytics]         (grant: ViewAnalytics) [NEW grant]
--- PERSONAL (Nav_Personal) ---
  My Groups         /MyTeam/Index                  [key: MyGroups]
  My Friends*       /Friends                       [key: MyFriends]          (FF-gated)
  My Profile        /My/Profile                    [key: MyProfile]
  My Settings       /My/Settings                   [key: My_Settings]
  Help              /My/Help                       [key: Help_NavLink]
```

[*] See [Edge Case #1](#edge-case-1-duplicate-schedule-links) — when Excel FF is on, Scheduled Shifts link is hidden (same URL as Schedule).

**What the user can DO with each link:**
- Command Center: Stats overview, quick-action cards to all management tools
- Schedule: View shift calendar (who is assigned where today/this week)
- Company Overview: Aggregated daily view of all users (shifts, chores, vacations, notes)
- Requests: Approve/deny vacations (BR + Hakam), swaps, extended leave
- Scheduled Shifts: Assign soldiers to BR/Hakam shifts using Roster Dock
- Shift Plans: Create/edit weekly schedule templates for BR/Hakam
- Chores: Assign chores to soldiers
- On-Call: View on-duty assignments
- People: View/edit all users in company, reset passwords, assign job types
- Analytics: Company-scoped metrics and reports

---

### Lead (template 3) — Admin Nav

```
--- MY SHIFTY (Calendar_MyCalendar) ---
  Command Center    /Admin/Index                   [key: Nav_CommandCenter]
  Schedule          {shiftsCalendarUrl}            [key: Schedule]
  Company Overview  /Calendar/Overview             [key: CompanyOverview]
  Requests          /Requests/Index                [key: Requests]
--- MANAGEMENT (Calendar_Management) ---
  Scheduled Shifts  {shiftsTableUrl}               [key: Calendar_ShiftsManagement]  (grant: ViewShifts) [*]
  Shift Plans       /Owner/Programs                [key: Nav_ShiftPlans]
  Chores            {choresCalendarUrl}            [key: Calendar_ChoresManagement]  (grant: ViewChores)
  On-Call           {onCallCalendarUrl}            [key: Calendar_OnDutyManagement]  (grant: ViewDuties)
--- ADMIN (Nav_Admin) ---
  Analytics         /Admin/Analytics               [key: Analytics]         (grant: ViewAnalytics) [NEW grant]
--- PERSONAL (Nav_Personal) ---
  My Groups         /MyTeam/Index                  [key: MyGroups]
  My Friends*       /Friends                       [key: MyFriends]          (FF-gated)
  My Profile        /My/Profile                    [key: MyProfile]
  My Settings       /My/Settings                   [key: My_Settings]
  Help              /My/Help                       [key: Help_NavLink]
```

**Key difference from BRDirector:** No "People" link (Lead has `ViewUsers` but not `ViewAllUsers` — sees users only via My Groups). Shift Plans scoped to own JobType only (grant uses `useOwnJobType: true`).

**What the user can DO:**
- Requests: Approve vacations/swaps for own job type only
- Scheduled Shifts: Assign Alhut/Text shifts (own job type, molecule-scoped via ETM)
- Shift Plans: Manage programs for own job type
- Analytics: Molecule-scoped analytics filtered to own job type

---

### Director (template 5) — Admin Nav

```
--- MY SHIFTY (Calendar_MyCalendar) ---
  Command Center    /Admin/Index                   [key: Nav_CommandCenter]
  Schedule          {shiftsCalendarUrl}            [key: Schedule]
  Company Overview  /Calendar/Overview             [key: CompanyOverview]
  Requests          /Requests/Index                [key: Requests]
--- MANAGEMENT (Calendar_Management) ---
  Scheduled Shifts  {shiftsTableUrl}               [key: Calendar_ShiftsManagement]  (grant: ViewShifts) [*]
  Shift Plans       /Owner/Programs                [key: Nav_ShiftPlans]
  Chores            {choresCalendarUrl}            [key: Calendar_ChoresManagement]  (grant: ViewChores)
  On-Call           {onCallCalendarUrl}            [key: Calendar_OnDutyManagement]  (grant: ViewDuties)
--- ADMIN (Nav_Admin) ---
  People            /Admin/Users                   [key: People]            (grant: ViewAllUsers)
  Companies         /Admin/Companies               [key: Companies]         (grant: EditCompany)
  Analytics         /Admin/Analytics               [key: Analytics]         (grant: ViewAnalytics) [NEW grant]
--- PERSONAL (Nav_Personal) ---
  My Groups         /MyTeam/Index                  [key: MyGroups]
  My Friends*       /Friends                       [key: MyFriends]          (FF-gated)
  My Profile        /My/Profile                    [key: MyProfile]
  My Settings       /My/Settings                   [key: My_Settings]
  Help              /My/Help                       [key: Help_NavLink]
```

**Key differences from Lead:**
- Has **People** (ViewAllUsers grant — manages users across all companies in molecule)
- Has **Companies** (EditCompany grant — edits company settings)
- Director Tools (ViewAs, NotificationHub, CompanyFilter) accessible via **Command Center cards**, not sidebar

**What the user can DO:**
- People: View ALL users across molecule, reset passwords, assign job types, assign roles
- Companies: Edit company names, settings, manage tenants
- Command Center → Director Tools: Impersonate manager view, cross-company notifications, company filter

---

### DepartmentLead (template 9) — Admin Nav

```
--- MY SHIFTY (Calendar_MyCalendar) ---
  Command Center    /Admin/Index                   [key: Nav_CommandCenter]
  Schedule          {shiftsCalendarUrl}            [key: Schedule]
  Company Overview  /Calendar/Overview             [key: CompanyOverview]
  Requests          /Requests/Index                [key: Requests]
--- MANAGEMENT (Calendar_Management) ---
  Scheduled Shifts  {shiftsTableUrl}               [key: Calendar_ShiftsManagement]  (grant: ViewShifts) [*]
  Shift Plans       /Owner/Programs                [key: Nav_ShiftPlans]
  Chores            {choresCalendarUrl}            [key: Calendar_ChoresManagement]  (grant: ViewChores)
  On-Call           {onCallCalendarUrl}            [key: Calendar_OnDutyManagement]  (grant: ViewDuties)
--- ADMIN (Nav_Admin) ---
  Analytics         /Admin/Analytics               [key: Analytics]         (grant: ViewAnalytics) [already has]
--- PERSONAL (Nav_Personal) ---
  My Groups         /MyTeam/Index                  [key: MyGroups]
  My Friends*       /Friends                       [key: MyFriends]          (FF-gated)
  My Profile        /My/Profile                    [key: MyProfile]
  My Settings       /My/Settings                   [key: My_Settings]
  Help              /My/Help                       [key: Help_NavLink]
```

**Role context:** DepartmentLead is a tech-department manager (Hanava, Delta, Yekev, Moviltech). Scoped to `RoleScopeLevel.Department`. They assign tech shifts, manage tech blueprints/programs. They CANNOT approve vacations or swaps. `DerivedUserRole = Manager`.

**Identical to Lead sidebar** — differences are in grant scopes (SAR vs ETM) and which shift types they manage, not in sidebar structure.

---

### MoleculeAdmin (template 7) — Admin Nav

```
--- MY SHIFTY (Calendar_MyCalendar) ---
  Command Center    /Admin/Index                   [key: Nav_CommandCenter]
  Schedule          {shiftsCalendarUrl}            [key: Schedule]
  Company Overview  /Calendar/Overview             [key: CompanyOverview]
  Requests          /Requests/Index                [key: Requests]
--- MANAGEMENT (Calendar_Management) ---
  Scheduled Shifts  {shiftsTableUrl}               [key: Calendar_ShiftsManagement]  (grant: ViewShifts) [*]
  Shift Plans       /Owner/Programs                [key: Nav_ShiftPlans]
  Duty Rotations    /Admin/DutyRotation            [key: DutyRotations]     (grant: ManageOnDuty) [NEW grant] (FF-gated)
  Chores            {choresCalendarUrl}            [key: Calendar_ChoresManagement]  (grant: ViewChores)
  On-Call           {onCallCalendarUrl}            [key: Calendar_OnDutyManagement]  (grant: ViewDuties)
--- ADMIN (Nav_Admin) ---
  People            /Admin/Users                   [key: People]            (grant: ViewAllUsers)
  Companies         /Admin/Companies               [key: Companies]         (grant: EditCompany)
  Analytics         /Admin/Analytics               [key: Analytics]         (grant: ViewAnalytics)
  Audit Log         /Admin/AuditLog                [key: AuditLog]          (grant: ViewAuditLog)
  Settings          /Admin/Config                  [key: Settings]          (grant: ViewSettings) [already has]
--- PERSONAL (Nav_Personal) ---
  My Groups         /MyTeam/Index                  [key: MyGroups]
  My Friends*       /Friends                       [key: MyFriends]          (FF-gated)
  My Profile        /My/Profile                    [key: MyProfile]
  My Settings       /My/Settings                   [key: My_Settings]
  Help              /My/Help                       [key: Help_NavLink]
```

**Key differences from Director:**
- Has **Duty Rotations** (ManageOnDuty — NEW grant addition)
- Has **Settings** (ViewSettings — already has grant 48)
- Has **Audit Log** (ViewAuditLog — already has)
- No Director Tools in Command Center (no DirectorHubAccess)
- Manages ALL job types (no `useOwnJobType` restriction)

---

### AreaAdmin (template 10) — Admin Nav

```
--- MY SHIFTY (Calendar_MyCalendar) ---
  Command Center    /Admin/Index                   [key: Nav_CommandCenter]
  Schedule          {shiftsCalendarUrl}            [key: Schedule]
  Company Overview  /Calendar/Overview             [key: CompanyOverview]
  Requests          /Requests/Index                [key: Requests]
--- MANAGEMENT (Calendar_Management) ---
  Scheduled Shifts  {shiftsTableUrl}               [key: Calendar_ShiftsManagement]  (grant: ViewShifts) [*]
  Shift Plans       /Owner/Programs                [key: Nav_ShiftPlans]
  Duty Rotations    /Admin/DutyRotation            [key: DutyRotations]     (grant: ManageOnDuty) (FF-gated)
  Chores            {choresCalendarUrl}            [key: Calendar_ChoresManagement]  (grant: ViewChores)
  On-Call           {onCallCalendarUrl}            [key: Calendar_OnDutyManagement]  (grant: ViewDuties)
--- ADMIN (Nav_Admin) ---
  People            /Admin/Users                   [key: People]            (grant: ViewAllUsers)
  Companies         /Admin/Companies               [key: Companies]         (grant: EditCompany)
  Analytics         /Admin/Analytics               [key: Analytics]         (grant: ViewAnalytics)
  Audit Log         /Admin/AuditLog                [key: AuditLog]          (grant: ViewAuditLog)
  Settings          /Admin/Config                  [key: Settings]          (grant: ViewSettings)
--- PERSONAL (Nav_Personal) ---
  My Groups         /MyTeam/Index                  [key: MyGroups]
  My Friends*       /Friends                       [key: MyFriends]          (FF-gated)
  My Profile        /My/Profile                    [key: MyProfile]
  My Settings       /My/Settings                   [key: My_Settings]
  Help              /My/Help                       [key: Help_NavLink]
```

**Key differences from MoleculeAdmin:**
- Has **Settings** (ViewSettings — AreaAdmin configures area-wide settings)
- Has **DirectorHubAccess** — sees Director Tools section in Command Center
- Scoped to entire Area (ETA) — broadest non-Owner scope

---

### Owner (template 11) — Admin Nav

```
--- MY SHIFTY (Calendar_MyCalendar) ---
  Home              /Home/Index                    [key: Home]              (grant: AdminAccess)
  Schedule          {shiftsCalendarUrl}            [key: Schedule]
  Company Overview  /Calendar/Overview             [key: CompanyOverview]
  Requests          /Requests/Index                [key: Requests]
  Analytics         /Admin/Analytics               [key: Analytics]         (grant: ViewAnalytics)
  Audit Log         /Admin/AuditLog                [key: AuditLog]          (grant: ViewAuditLog)
  People            /Admin/Users                   [key: People]            (grant: ViewAllUsers)
  Companies         /Admin/Companies               [key: Companies]         (grant: EditCompany)
  Settings          /Admin/Config                  [key: Settings]          (grant: ViewSettings)
--- OWNER PANEL (Owner_AdminPanel) ---
  Admin Panel       /Owner/Index                   [key: Owner_AdminPanel]  (grant: AdminAccess)
  Telemetry         /Owner/Telemetry               [key: Telemetry]         (grant: AdminAccess)
--- PERSONAL (Nav_Personal) ---
  My Friends*       /Friends                       [key: MyFriends]          (FF-gated)
  Help              /My/Help                       [key: Help_NavLink]
```

**Changes from current:**
- **REMOVED** from sidebar: Duty Rotations, Setup Tasks, Approval Rules (moved to Owner Panel page)
- My Profile / My Settings remain accessible via sidebar footer user menu (as currently)
- Owner does NOT see "Command Center" label — they keep "Home" pointing to `/Home/Index`

---

## 4. Sidebar Implementation Details

### File: `Pages/Shared/_Layout.cshtml`

#### Structure Overview

> **IMPORTANT: Structural change.** The current sidebar uses **two** top-level `<require-grant>` blocks (`AccessAdminNavigation` present/absent), with Owner-specific items narrowed by inner `<require-grant key="AdminAccess">` guards. This rewrite changes to **three** top-level blocks. Carefully verify every existing nav item is accounted for in the correct block.
>
> **NOTE:** The variable `friendshipsEnabled` already exists at `_Layout.cshtml` line 26. Do NOT re-declare it.
>
> **NOTE:** The `ContextSwitcher` component renders BEFORE the `<nav>` block (lines 406-410) and must remain untouched.

The sidebar rewrite replaces lines ~413–673 (the entire `<nav class="app-sidebar-nav">` block). The new structure:

```razor
<nav class="app-sidebar-nav">
    @* ============================================ *@
    @* OWNER NAVIGATION (AdminAccess grant)         *@
    @* ============================================ *@
    <require-grant key="AdminAccess">
        @* ... Owner sidebar ... *@
    </require-grant>

    @* ============================================ *@
    @* ADMIN NAVIGATION (AccessAdminNavigation, NOT Owner) *@
    @* ============================================ *@
    <require-grant key="AdminAccess" negate="true">
        <require-grant key="AccessAdminNavigation">
            @* ... All admin roles: BRDirector, Lead, Director,
                   DeptLead, MoleculeAdmin, AreaAdmin ... *@
        </require-grant>
    </require-grant>

    @* ============================================ *@
    @* EMPLOYEE NAVIGATION (no AccessAdminNavigation) *@
    @* ============================================ *@
    <require-grant key="AccessAdminNavigation" negate="true">
        @* ... Assigner + all employees ... *@
    </require-grant>
</nav>
```

#### Admin Nav Block — Grant-Gated Items

Inside the admin nav block, most items are always visible. The grant-gated items are:

```razor
@* === MY SHIFTY section === *@
<div class="nav-section-header"><loc key="Calendar_MyCalendar" /></div>

@* Command Center — all admin-nav roles *@
<a href="/Admin/Index" class="app-sidebar-nav-item ...">
    <span class="app-sidebar-nav-icon">🎛️</span>
    <span><loc key="Nav_CommandCenter" /></span>
</a>

@* Schedule — all *@
<a href="@shiftsCalendarUrl" class="app-sidebar-nav-item ...">...</a>

@* Company Overview — all *@
<a href="/Calendar/Overview" class="app-sidebar-nav-item ...">...</a>

@* Requests — all *@
<a href="/Requests/Index" class="app-sidebar-nav-item ...">...</a>

@* === MANAGEMENT section === *@
<div class="nav-section-header"><loc key="Calendar_Management" /></div>

@* Scheduled Shifts — grant-gated, hidden when Excel FF merges URLs *@
<require-grant key="ViewShifts">
    @if (shiftsTableUrl != shiftsCalendarUrl)
    {
        <a href="@shiftsTableUrl" class="app-sidebar-nav-item ...">
            <span class="app-sidebar-nav-icon">📊</span>
            <span><loc key="Calendar_ShiftsManagement" /></span>
        </a>
    }
</require-grant>

@* Shift Plans — all admin-nav roles (page itself gates via ManagerHomeAccess) *@
<a href="/Owner/Programs" class="app-sidebar-nav-item ...">
    <span class="app-sidebar-nav-icon">📅</span>
    <span><loc key="Nav_ShiftPlans" /></span>
</a>

@* Duty Rotations — grant + FF gated *@
@if (FeatureFlagService.IsEnabled(FeatureFlagSeed.Flags.DutyRotationEnabled))
{
    <require-grant key="ManageOnDuty">
        <a href="/Admin/DutyRotation" class="app-sidebar-nav-item ...">...</a>
    </require-grant>
}

@* Chores — grant-gated *@
<require-grant key="ViewChores">
    <a href="@choresCalendarUrl" class="app-sidebar-nav-item ...">...</a>
</require-grant>

@* On-Call — grant-gated *@
<require-grant key="ViewDuties">
    <a href="@onCallCalendarUrl" class="app-sidebar-nav-item ...">...</a>
</require-grant>

@* === ADMIN section — only rendered if role has ANY admin items === *@
@* KEEP IN SYNC: This grant list must include ALL grants used for items in the Admin section below *@
<require-grant key="ViewAllUsers,ViewAnalytics,EditCompany,ViewAuditLog,ViewSettings" mode="any">
    <div class="nav-section-header"><loc key="Nav_Admin" /></div>
</require-grant>

@* People — only for ViewAllUsers *@
<require-grant key="ViewAllUsers">
    <a href="/Admin/Users" class="app-sidebar-nav-item ...">
        <span class="app-sidebar-nav-icon">👥</span>
        <span><loc key="People" /></span>
    </a>
</require-grant>

@* Companies — only for EditCompany *@
<require-grant key="EditCompany">
    <a href="/Admin/Companies" class="app-sidebar-nav-item ...">...</a>
</require-grant>

@* Analytics — only for ViewAnalytics *@
<require-grant key="ViewAnalytics">
    <a href="/Admin/Analytics" class="app-sidebar-nav-item ...">...</a>
</require-grant>

@* Audit Log — only for ViewAuditLog *@
<require-grant key="ViewAuditLog">
    <a href="/Admin/AuditLog" class="app-sidebar-nav-item ...">...</a>
</require-grant>

@* Settings — only for ViewSettings *@
<require-grant key="ViewSettings">
    <a href="/Admin/Config" class="app-sidebar-nav-item ...">...</a>
</require-grant>

@* === PERSONAL section === *@
<div class="nav-section-header"><loc key="Nav_Personal" /></div>

@* My Groups — always visible for admin-nav users *@
<a href="/MyTeam/Index" class="app-sidebar-nav-item ...">
    <span class="app-sidebar-nav-icon">👥</span>
    <span><loc key="MyGroups" /></span>
</a>

@* My Friends — FF-gated *@
@if (friendshipsEnabled) {
    <a href="/Friends" class="app-sidebar-nav-item ...">...</a>
}

@* My Profile *@
<a href="/My/Profile" class="app-sidebar-nav-item ...">...</a>

@* My Settings *@
<a href="/My/Settings" class="app-sidebar-nav-item ...">...</a>

@* Help *@
<a href="/My/Help" class="app-sidebar-nav-item ...">...</a>
```

#### Active State Patterns

Each link needs the correct `active` class detection:

| Link | Active when `currentPath` matches |
|------|----------------------------------|
| Command Center | `== "/Admin/Index"` or `== "/Admin"` |
| Schedule | `(.StartsWith("/Calendar") && !.StartsWith("/Calendar/Overview") && !.StartsWith("/Calendar/Table") && !.StartsWith("/Calendar/Chores") && !.StartsWith("/Calendar/OnCall"))` or `.StartsWith("/Schedule")` |
| Company Overview | `.StartsWith("/Calendar/Overview")` |
| Requests | `.StartsWith("/Requests")` |
| Scheduled Shifts | `.StartsWith("/Calendar/Table")` |
| Shift Plans | `.StartsWith("/Owner/Programs")` |
| Duty Rotations | `.StartsWith("/Admin/DutyRotation")` |
| Chores | `.StartsWith("/Public/Chores")` or `.StartsWith("/Chores")` or `.StartsWith("/Calendar/Chores")` |
| On-Call | `.StartsWith("/Public/OnDuty")` or `.StartsWith("/Calendar/OnCall")` |
| People | `.StartsWith("/Admin/Users")` |
| Companies | `.StartsWith("/Admin/Companies")` |
| Analytics | `.StartsWith("/Admin/Analytics")` |
| Audit Log | `.StartsWith("/Admin/AuditLog")` |
| Settings | `.StartsWith("/Admin/Config")` |
| My Groups | `.StartsWith("/MyTeam")` |
| My Friends | `.StartsWith("/Friends")` |
| My Profile | `.StartsWith("/My/Profile")` |
| My Settings | `.StartsWith("/My/Settings")` |
| Help | `.StartsWith("/My/Help")` |

---

## 5. Admin Hub (Command Center) Changes

### Files: `Pages/Admin/Index.cshtml` + `Pages/Admin/Index.cshtml.cs`

#### Code-Behind: Use Existing `IsDirector` Property

> **NOTE:** `_grantService` is already injected (line 24). `IsDirector` property and its grant check already exist at line 78:
> ```csharp
> IsDirector = await _grantService.HasGrantAsync(userId, "DirectorHubAccess");
> ```
> **Do NOT add a duplicate `HasDirectorHubAccess` property.** Use the existing `IsDirector` property.

#### View: Add "Director Tools" Section

In `Index.cshtml`, add after the "Configuration" section (line 247, before "Operations Management"):

```razor
@* Director Tools — Only visible to users with DirectorHubAccess *@
@if (Model.IsDirector)
{
    <h2 class="section-title">
        <span class="section-icon">👔</span>
        <loc key="Section_DirectorTools">Director Tools</loc>
    </h2>
    <div class="admin-tools-grid">
        <a href="/Director/ViewAsMode" class="admin-tool-card">
            <div class="tool-icon">👀</div>
            <div class="tool-content">
                <h3><loc key="Section_ViewAsManager">View As Manager</loc></h3>
                <p><loc key="Section_ViewAsManagerDesc">See the system as a company manager</loc></p>
            </div>
            <div class="tool-arrow">→</div>
        </a>

        <a href="/Director/NotificationHub" class="admin-tool-card">
            <div class="tool-icon">🔔</div>
            <div class="tool-content">
                <h3><loc key="Section_NotificationHub">Notification Hub</loc></h3>
                <p><loc key="Section_NotificationHubDesc">Cross-company notifications and pending requests</loc></p>
            </div>
            <div class="tool-arrow">→</div>
        </a>

        <a href="/Director/CompanyFilter" class="admin-tool-card">
            <div class="tool-icon">🔧</div>
            <div class="tool-content">
                <h3><loc key="Section_CompanyFilter">Company Filter</loc></h3>
                <p><loc key="Section_CompanyFilterDesc">Select which companies to manage</loc></p>
            </div>
            <div class="tool-arrow">→</div>
        </a>
    </div>
}
```

#### View: Add Pending Requests Stat

Add to the stats grid (the Admin Hub currently shows Users, Shifts, Chores, Assignments). Add a 5th stat for pending requests. In code-behind:

```csharp
public int TotalPendingRequests { get; set; }

// In OnGetAsync — follow the existing three-tier scoping pattern (lines 83-108):
if (IsOwner)
{
    TotalPendingRequests = await _db.TimeOffRequests
        .IgnoreQueryFilters()
        .CountAsync(r => r.Status == RequestStatus.Pending);
}
else if (IsDirector)
{
    TotalPendingRequests = await _db.TimeOffRequests
        .IgnoreQueryFilters()
        .Where(r => r.Status == RequestStatus.Pending && companyIds.Contains(r.CompanyId))
        .CountAsync();
}
else
{
    // Manager — uses query filters (auto-scoped to tenant)
    TotalPendingRequests = await _db.TimeOffRequests
        .CountAsync(r => r.Status == RequestStatus.Pending);
}
```

> **NOTE:** The `companyIds` variable and three-tier branching already exist in this method. Reuse the same pattern.

---

## 6. Owner Panel Changes

### Files: `Pages/Owner/Hub/Index.cshtml` + `Pages/Owner/Hub/Index.cshtml.cs`

> **CRITICAL:** `Pages/Owner/Index.cshtml.cs` OnGet() does a 302 redirect to `/Owner/Hub/Index`. Changes to Owner/Index would never render. The correct target is `Pages/Owner/Hub/Index.cshtml(.cs)`.

Add a new "Company Configuration" section with 3 cards for the items being removed from the Owner sidebar.

#### Code-Behind: Add `IFeatureFlagService` Injection + Properties

The current constructor (`Hub/Index.cshtml.cs` lines 23-29) injects `AppDbContext`, `ILogger`, `IRoleService`, `ICompanyCacheService`. **Must add `IFeatureFlagService`:**

```csharp
// Add to constructor parameters:
private readonly IFeatureFlagService _featureFlagService;

public IndexModel(
    AppDbContext db,
    ILogger<IndexModel> logger,
    IRoleService roleService,
    ICompanyCacheService companyCacheService,
    IFeatureFlagService featureFlagService)  // NEW
{
    // ... existing assignments ...
    _featureFlagService = featureFlagService;
}

// Add properties:
public bool DutyRotationEnabled { get; set; }
public bool SetupTasksEnabled { get; set; }
public bool VacationApprovalEnabled { get; set; }

// In OnGetAsync:
DutyRotationEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.DutyRotationEnabled);
SetupTasksEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.SetupTasksEnabled);
VacationApprovalEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.VacationApprovalEnabled);
```

#### View: Add Section

> **CRITICAL CSS NOTE:** Owner Hub uses `hub-card` classes, NOT `admin-tool-card`. The Admin Hub card CSS (`admin-tool-card`, `tool-icon`, `tool-content`, `tool-arrow`) is NOT defined in the Owner Hub stylesheet. New cards must follow the `hub-card` pattern used in existing Owner Hub sections.

Insert after the "Operations" section (before "Infrastructure"):

```razor
@* Company Configuration — items removed from Owner sidebar *@
<div class="hub-card">
    <div class="hub-card-header">
        <h3><loc key="Owner_Section_CompanyConfig">Company Configuration</loc></h3>
    </div>
    <div class="hub-card-actions">
        @if (Model.DutyRotationEnabled)
        {
            <a href="/Admin/DutyRotation" class="hub-action-link">
                <loc key="Section_DutyRotation">Duty Rotations</loc>
            </a>
        }
        @if (Model.SetupTasksEnabled)
        {
            <a href="/Admin/SetupTasks" class="hub-action-link">
                <loc key="Section_SetupTasks">Setup Tasks</loc>
            </a>
        }
        @if (Model.VacationApprovalEnabled)
        {
            <a href="/Admin/Settings/ApprovalRules" class="hub-action-link">
                <loc key="Section_ApprovalRules">Approval Rules</loc>
            </a>
        }
    </div>
</div>
```

---

## 7. Director Hub Removal

### Delete These Files

- `Pages/Director/Index.cshtml`
- `Pages/Director/Index.cshtml.cs`

### Keep These Files (still used)

| File | Referenced By |
|------|-------------- |
| `Pages/Director/ViewAsMode.cshtml(.cs)` | Admin Hub Director Tools card + `_Layout.cshtml:796` (ViewAs exit banner) |
| `Pages/Director/CompanyFilter.cshtml(.cs)` | Admin Hub Director Tools card |
| `Pages/Director/NotificationHub.cshtml(.cs)` | Admin Hub Director Tools card |

### Update References

| File | Line | Current | New |
|------|------|---------|-----|
| `_Layout.cshtml` | ~432 | `href="/Director/Index"` (Director home link) | Removed entirely — replaced by Command Center link |
| `AccessDenied.cshtml.cs` | 94 | `"/Director/"` in protectedPaths | **Keep** — sub-pages still exist |

### Update Breadcrumbs on Surviving Pages (BLOCKING)

All three kept Director pages have breadcrumbs linking to `/Director` (which resolves to the deleted Index page). Update each:

| File | Line | Current | New |
|------|------|---------|-----|
| `Pages/Director/ViewAsMode.cshtml` | 14 | `Url = "/Director"` | `Url = "/Admin/Index"` |
| `Pages/Director/NotificationHub.cshtml` | 14 | `Url = "/Director"` | `Url = "/Admin/Index"` |
| `Pages/Director/CompanyFilter.cshtml` | 14 | `Url = "/Director"` | `Url = "/Admin/Index"` |

Optionally update the breadcrumb label from `Localizer["DirectorHub_Title"]` to `Localizer["Nav_CommandCenter"]` to match the new "Command Center" branding.

### Update QA Test Files

| File | Line(s) | Action |
|------|---------|--------|
| `qa-automation/tests/production-qa/module-w-director-my-public.spec.js` | 9-35 | Remove or rewrite tests W-01 and W-02 that navigate to `/Director/Index` |
| `qa-automation/discovery/app-crawler.js` | 100 | Remove `/Director/Index` entry |
| `qa-automation/discovery/navigation-map.json` | 65, 155 | Remove `/Director/Index` entries |

### Optional: Add Redirect for Bookmarks

Consider adding to `Program.cs` for users who bookmarked `/Director/Index`:
```csharp
app.MapGet("/Director/Index", () => Results.Redirect("/Admin/Index"));
```

---

## 8. Localization

### New Keys to Add

#### `Resources/SharedResources.resx` (English)

```xml
<data name="Nav_CommandCenter" xml:space="preserve">
    <value>Command Center</value>
    <comment>Sidebar navigation - unified landing page for admin roles</comment>
</data>
<data name="Nav_ShiftPlans" xml:space="preserve">
    <value>Shift Plans</value>
    <comment>Sidebar navigation - link to /Owner/Programs</comment>
</data>
<!-- Nav_Admin ALREADY EXISTS in .resx with value "Admin" — DO NOT ADD AGAIN -->
<!-- If you want to change the value to "Administration", EDIT the existing entry instead -->
<data name="Nav_Personal" xml:space="preserve">
    <value>Personal</value>
    <comment>Sidebar section header for personal features</comment>
</data>
<data name="Section_DirectorTools" xml:space="preserve">
    <value>Director Tools</value>
    <comment>Admin Hub section header for director utilities</comment>
</data>
<data name="Section_CompanyFilter" xml:space="preserve">
    <value>Company Filter</value>
    <comment>Admin Hub card - director company filter</comment>
</data>
<data name="Section_CompanyFilterDesc" xml:space="preserve">
    <value>Select which companies to manage</value>
    <comment>Admin Hub card description - company filter</comment>
</data>
<data name="Owner_Section_CompanyConfig" xml:space="preserve">
    <value>Company Configuration</value>
    <comment>Owner Panel section for items moved from sidebar</comment>
</data>
```

#### `Resources/SharedResources.he-IL.resx` (Hebrew)

```xml
<data name="Nav_CommandCenter" xml:space="preserve">
    <value>מרכז פיקוד</value>
    <comment>Sidebar navigation - unified landing page for admin roles</comment>
</data>
<data name="Nav_ShiftPlans" xml:space="preserve">
    <value>תכנון משמרות</value>
    <comment>Sidebar navigation - link to /Owner/Programs</comment>
</data>
<!-- Nav_Admin ALREADY EXISTS in he-IL.resx with value "ניהול" — DO NOT ADD AGAIN -->
<!-- WARNING: "ניהול" duplicates Calendar_Management HE value. Consider changing to "ניהול מערכת" or "אדמיניסטרציה" -->
<data name="Nav_Personal" xml:space="preserve">
    <value>אישי</value>
    <comment>Sidebar section header for personal features</comment>
</data>
<data name="Section_DirectorTools" xml:space="preserve">
    <value>כלי מפקד</value>
    <comment>Admin Hub section header for director utilities</comment>
</data>
<data name="Section_CompanyFilter" xml:space="preserve">
    <value>סינון חברות</value>
    <comment>Admin Hub card - director company filter</comment>
</data>
<data name="Section_CompanyFilterDesc" xml:space="preserve">
    <value>בחר אילו חברות לנהל</value>
    <comment>Admin Hub card description - company filter</comment>
</data>
<data name="Owner_Section_CompanyConfig" xml:space="preserve">
    <value>הגדרות חברה</value>
    <comment>Owner Panel section for items moved from sidebar</comment>
</data>
```

### Existing Keys Reused (no changes needed)

| Key | EN | HE | Used for |
|-----|----|----|----------|
| `Calendar_MyCalendar` | "My Shifty" | "השיפטי שלי" | Section header |
| `Calendar_Management` | "Management" | "ניהול" | Section header |
| `Schedule` | "Schedule" | "לוח זמנים" | Sidebar link |
| `CompanyOverview` | "Company Overview" | "צפייה בצוות" | Sidebar link |
| `Requests` | "Requests" | "בקשות" | Sidebar link |
| `Calendar_ShiftsManagement` | "Scheduled Shifts" | "משמרות הפקה וב"ר" | Sidebar link |
| `Calendar_ChoresManagement` | "Chores" | "מטלות" | Sidebar link |
| `Calendar_OnDutyManagement` | "Day Shifts" | "משמרות רוחב" | Sidebar link |
| `People` | "People" | "אנשים" | Sidebar link |
| `Companies` | "Companies" | "חברות" | Sidebar link |
| `Analytics` | "Analytics" | "אנליטיקה" | Sidebar link |
| `AuditLog` | "Audit Log" | "יומן ביקורת" | Sidebar link |
| `Settings` | "Settings" | "הגדרות" | Sidebar link |
| `DutyRotations` | "Duty Rotations" | "רוטציות תורנות" | Sidebar link |
| `MyGroups` | "My Groups" | "הקבוצות שלי" | Sidebar link |
| `MyFriends` | "My Friends" | "החברים שלי" | Sidebar link |
| `MyProfile` | "My Profile" | "הפרופיל שלי" | Sidebar link |
| `My_Settings` | "My Settings" | "ההגדרות שלי" | Sidebar link |
| `Help_NavLink` | "Help" | "עזרה" | Sidebar link |
| `Home` | "Home" | "בית" | Owner/Assigner home link |
| `Owner_AdminPanel` | "Owner Administration" | — | Owner section header |
| `Telemetry` | "Telemetry" | — | Owner sidebar link |
| `Section_ViewAsManager` | "View As Manager" | — | Admin Hub card (already exists) |
| `Section_ViewAsManagerDesc` | "Impersonate Manager perspective" | — | Admin Hub card (already exists) |
| `Section_NotificationHub` | "Notification Hub" | — | Admin Hub card (already exists) |
| `Section_NotificationHubDesc` | "Cross-company notifications" | — | Admin Hub card (already exists) |

### Keys That Become Unused (can be kept or removed)

| Key | Was used for | Status |
|-----|-------------|--------|
| `Nav_DirectorHub` | Director Hub sidebar link — page deleted | Safe to remove |
| `Nav_AdminHub` | Admin Hub sidebar link — replaced by `Nav_CommandCenter` | Safe to remove |
| `DirectorHub_Title` | Director Hub page title | **DO NOT REMOVE** — still used by ViewAsMode, NotificationHub, CompanyFilter breadcrumbs. Remove only after breadcrumbs are migrated. |
| `DirectorHub_Subtitle` | Director Hub page subtitle | Safe to remove (only used in deleted Index.cshtml) |

### Existing Keys Used by Owner Panel Cards (already in .resx — verify, don't add)

| Key | EN Value | HE Value |
|-----|----------|----------|
| `Section_DutyRotation` | "Duty Rotations" | "רוטציות תורנות" |
| `Section_DutyRotationDesc` | "On-duty rotation management" | "ניהול רוטציות תורנות" |
| `Section_SetupTasks` | "Setup Tasks" | "משימות הקמה" |
| `Section_SetupTasksDesc` | "Company setup wizard and checklist" | "אשף הקמת חברה ורשימת ביקורת" |
| `Section_ApprovalRules` | "Approval Rules" | "כללי אישור" |
| `Section_ApprovalRulesDesc` | "Vacation approval workflow configuration" | "הגדרת תהליך אישור חופשות" |

---

## 9. Edge Cases & Warnings

### Edge Case 1: Duplicate Schedule Links

When Excel Calendar FFs are ON (the default), `shiftsCalendarUrl` and `shiftsTableUrl` both resolve to `/Calendar/Shifts`. This would create two identical sidebar links ("Schedule" and "Scheduled Shifts").

**Solution:** Conditionally hide "Scheduled Shifts" when URLs are the same:
```razor
@if (shiftsTableUrl != shiftsCalendarUrl)
{
    <a href="@shiftsTableUrl" ...>Scheduled Shifts</a>
}
```

This means when Excel Calendars is on, only "Schedule" appears (the unified page handles both viewing and management). When Excel Calendars is off (legacy), both links appear pointing to their separate pages.

### Edge Case 2: Company Overview is Company-Scoped

`/Calendar/Overview` uses `_companyContext.CompanyId` — it shows ONE company at a time. Directors managing multiple companies must use the **Context Switcher** (sidebar component) to switch between companies. The Company Overview page itself has no company dropdown.

### Edge Case 3: Audit Log Data Scope vs Grant Scope

The new `ViewAuditLog` grants for Lead/Director use `useOwnJobType: true`. However, the Audit Log page (`/Admin/AuditLog`) may not filter data by job type. The grant controls ACCESS, but the page needs to be verified to ensure it filters data appropriately for scoped users. This is a **follow-up task** — the grant addition is correct regardless.

### Edge Case 4: Owner Panel Dependencies

The Owner Hub (`/Owner/Hub/Index.cshtml.cs`) currently doesn't inject `IFeatureFlagService`. It needs to be added to the constructor and the `OnGetAsync` method for the new FF-gated cards. See Section 6 for the full constructor change. **Do NOT modify `/Owner/Index.cshtml.cs`** — that file is a redirect stub.

### Edge Case 5: My Profile / My Settings Paths

Admin-nav users currently access Profile/Settings only via the sidebar **footer user menu** (the avatar dropdown). The new sidebar adds explicit `/My/Profile` and `/My/Settings` links in the PERSONAL section. These pages must work for admin-nav users (they currently do — they're `[Authorize]` only, not role-restricted).

### Edge Case 6: ViewAs Exit Banner

`_Layout.cshtml:796` has a hardcoded link to `/Director/ViewAsMode`. This must be **kept** — the page still exists. Only `Director/Index` is deleted.

### Edge Case 7: Requests Page Routing

Admin-nav users link to `/Requests/Index` (management view). Employee-nav users link to `/My/Requests` (personal view). The Assigner sidebar (employee nav) must use `/My/Requests`, not `/Requests/Index`.

### Edge Case 8: Command Palette Divergence (Follow-up)

The command palette in `wwwroot/js/site.js` (lines 933-966) uses hardcoded role arrays (`['Manager', 'Director', 'Owner']`) per page, not grant-based checks. After this redesign, Ctrl+K results won't match sidebar links. E.g., BRDirector gains `ViewAnalytics` in sidebar but the command palette won't show Analytics. **Follow-up task:** Update `getCommandPalettePages()` to use grant-based filtering or mirror the new sidebar.

### Edge Case 9: ViewAs Mode Does Not Alter Sidebar

`isViewingAsManager` (`_Layout.cshtml` line 25) is only used for the warning banner (line 792), not sidebar rendering. A Director in ViewAs mode still sees Director-level sidebar items. This is pre-existing behavior, not introduced by this redesign. **Known limitation** — document and consider fixing separately.

### Edge Case 10: Audit Log Data Scope Verification (Follow-up)

The new `ViewAuditLog` grants for BRDirector (SAR), Lead (ETM+OJT), and Director (ETM+OJT) give these roles access to `/Admin/AuditLog`. Verify that the Audit Log page properly filters data by the caller's effective grant scope to prevent cross-scope information leakage.

### Edge Case 11: Phases 4+5 Must Deploy Together

If Phase 5 (sidebar rewrite) deploys without Phase 4 (localization keys), users see raw key names like `Nav_CommandCenter`. In air-gapped IIS manual deployments, combine Phases 4 and 5 into a single deployment step.

---

## 10. Implementation Order

Execute in this order to avoid broken states:

### Phase 1: Grant Seeding (no UI changes yet)

1. **`RoleTemplateSeed.cs`** — Add 11 new grants (BRDirector +4, Lead +3, Director +3, MoleculeAdmin +1)
2. Run application to verify seeding completes without errors
3. Verify: log in as each affected role and confirm new grants appear in user's grant list

### Phase 2: Owner Panel Cards

4. **`Owner/Hub/Index.cshtml.cs`** — Inject `IFeatureFlagService`, add FF properties (NOT Owner/Index.cshtml.cs — that file redirects)
5. **`Owner/Hub/Index.cshtml`** — Add "Company Configuration" section using `hub-card` CSS classes (NOT `admin-tool-card`)
6. Verify: Owner can reach Setup Tasks, Duty Rotations, Approval Rules from Owner Panel

### Phase 3: Admin Hub (Command Center)

7. **`Admin/Index.cshtml.cs`** — Use existing `IsDirector` property (already has the grant check at line 78)
8. **`Admin/Index.cshtml`** — Add "Director Tools" section (3 cards, grant-gated)
9. Verify: Director sees Director Tools cards, Manager does not (use `Model.IsDirector` not `Model.HasDirectorHubAccess`)

### Phase 4: Localization (MUST deploy with Phase 5)

10. **`SharedResources.resx`** — Add 7 new keys (Nav_Admin already exists — edit its value if needed, don't re-add)
11. **`SharedResources.he-IL.resx`** — Add 7 new Hebrew keys (change existing Nav_Admin HE from "ניהול" to "ניהול מערכת" to avoid duplication with Calendar_Management)
12. Verify: Switch language to Hebrew, confirm no missing keys and no duplicate section headers

### Phase 5: Sidebar Rewrite

13. **`_Layout.cshtml`** — Rewrite admin nav block (lines ~413–597)
14. **`_Layout.cshtml`** — Rewrite employee nav block (lines ~600–673)
15. **`_Layout.cshtml`** — Remove Director Hub home link branching (lines ~430–444)
16. Verify: Test sidebar for each role (see Testing Checklist)

### Phase 6: Owner Sidebar Cleanup

17. **`_Layout.cshtml`** — Remove Duty Rotations, Setup Tasks, Approval Rules from Owner nav
18. Verify: Owner sidebar is clean, items accessible from Owner Panel

### Phase 7: Director Hub Deletion (MUST run AFTER Phase 5)

19. **Update breadcrumbs**: Change `Url = "/Director"` to `Url = "/Admin/Index"` in `ViewAsMode.cshtml:14`, `NotificationHub.cshtml:14`, `CompanyFilter.cshtml:14`
20. **Delete** `Pages/Director/Index.cshtml`
21. **Delete** `Pages/Director/Index.cshtml.cs`
22. **Update QA tests**: Remove/rewrite `/Director/Index` references in `module-w-director-my-public.spec.js`, `app-crawler.js`, `navigation-map.json`
23. **(Optional)** Add redirect in `Program.cs`: `app.MapGet("/Director/Index", () => Results.Redirect("/Admin/Index"));`
24. Verify: `/Director/Index` returns 404 (or redirects). `/Director/ViewAsMode` still works. Breadcrumbs on kept pages link to `/Admin/Index`.

---

## 11. Testing Checklist

### Per-Role Sidebar Verification

For each role, log in and verify:

| Test | Assigner | BRDir | Lead | DeptLead | Director | MolAdmin | AreaAdmin | Owner |
|------|----------|-------|------|----------|----------|----------|-----------|-------|
| Correct landing page | / | /Admin | /Admin | /Admin | /Admin | /Admin | /Admin | /Home |
| Schedule link works | Y | Y | Y | Y | Y | Y | Y | Y |
| Company Overview loads | Y | Y | Y | Y | Y | Y | Y | Y |
| Requests page loads | /My/Req | /Req | /Req | /Req | /Req | /Req | /Req | /Req |
| Scheduled Shifts visible (FF OFF) | - | Y | Y | Y | Y | Y | Y | Y |
| Scheduled Shifts hidden (FF ON) | - | Y | Y | Y | Y | Y | Y | Y |
| Shift Plans loads | - | Y | Y | Y | Y | Y | Y | - |
| Duty Rotations visible | - | - | - | - | - | Y | Y | - |
| Chores link works | Y | Y | Y | Y | Y | Y | Y | Y |
| On-Call link works | Y | Y | Y | Y | Y | Y | Y | Y |
| People visible | - | Y | - | - | Y | Y | Y | Y |
| Companies visible | - | - | - | - | Y | Y | Y | Y |
| Analytics visible | - | Y | Y | Y | Y | Y | Y | Y |
| Audit Log visible | - | - | - | - | - | Y | Y | Y |
| Settings visible | - | - | - | - | - | Y | Y | Y |
| My Groups visible | Y | Y | Y | Y | Y | Y | Y | - |
| My Friends visible (FF ON) | Y | Y | Y | Y | Y | Y | Y | Y |
| My Profile link works | Y | Y | Y | Y | Y | Y | Y | - |
| My Settings link works | Y | Y | Y | Y | Y | Y | Y | - |
| Help link works | Y | Y | Y | Y | Y | Y | Y | Y |
| Section headers correct | 3 | 4 | 4 | 4 | 4 | 4 | 4 | 3 |

### Admin Hub (Command Center) Verification

| Test | Expected |
|------|----------|
| Director sees "Director Tools" section | 3 cards: ViewAs, NotificationHub, CompanyFilter |
| AreaAdmin sees "Director Tools" section | Same 3 cards |
| MoleculeAdmin does NOT see "Director Tools" | Section hidden |
| Lead does NOT see "Director Tools" | Section hidden |
| All roles see stats | Users, Shifts, Chores, Assignments counts |
| ViewAs card links to `/Director/ViewAsMode` | Page loads, functionality works |
| NotificationHub card links to `/Director/NotificationHub` | Page loads |
| CompanyFilter card links to `/Director/CompanyFilter` | Page loads |

### Owner Panel Verification

| Test | Expected |
|------|----------|
| Duty Rotations card visible (FF ON) | Links to `/Admin/DutyRotation` |
| Setup Tasks card visible (FF ON) | Links to `/Admin/SetupTasks` |
| Approval Rules card visible (FF ON) | Links to `/Admin/Settings/ApprovalRules` |
| Cards hidden when FFs are OFF | Not rendered |

### Regression Tests

| Test | Expected |
|------|----------|
| ViewAs exit banner still works | Link to `/Director/ViewAsMode` on `_Layout.cshtml:796` |
| `/Director/Index` returns 404 | Page deleted |
| `/Director/ViewAsMode` still accessible | Page kept |
| `/Director/CompanyFilter` still accessible | Page kept |
| `/Director/NotificationHub` still accessible | Page kept |
| AccessDenied protectedPaths still includes `/Director/` | Prevents redirect loops |
| Context Switcher still renders in sidebar | Unchanged |
| Sidebar collapse state persists | localStorage key unchanged |
| Mobile nav hamburger still works | Unchanged |
| RTL (Hebrew) layout correct | Section headers, icons, direction |

---

## Appendix: Role Quick-Reference

| Role | Scope Level | Can Approve Requests? | Can Manage Users? | Can Edit Companies? | Has Director Tools? |
|------|------------|----------------------|-------------------|--------------------|--------------------|
| Assigner (8) | Molecule | No | No | No | No |
| BRDirector (2) | Company | Yes (BR+Hakam) | Yes (company) | Yes (company) | No |
| Lead (3) | CompanyJobType | Yes (own JT) | View only | No | No |
| DepartmentLead (9) | Department | No | Edit (dept) | No | No |
| Director (5) | MoleculeJobType | Yes (own JT, molecule) | Yes (molecule) | Yes (molecule) | Yes (via CC) |
| MoleculeAdmin (7) | Molecule | Yes (all JTs) | Yes (molecule) | Yes (molecule) | No |
| AreaAdmin (10) | Area | Yes (all JTs, area) | Yes (area) | Yes (area) | Yes (via CC) |
| Owner (11) | Project | Yes (all) | Yes (all) | Yes (all) | Yes (via CC) |
