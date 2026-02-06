# Feature Implementation Status

**Version:** 2.0
**Date:** 2026-02-06
**Source:** V3 Design Documents, Genesis Docs, Plan Files, Codebase Verification

---

## Overview

This document tracks the implementation status of all major features that were designed during the V3 planning phase. Updated 2026-02-06 after a full codebase audit.

---

## Status Legend

| Status | Meaning |
|--------|---------|
| :white_check_mark: **Implemented** | Fully implemented and tested |
| :construction: **In Progress** | Actively being developed |
| :warning: **Partial** | Core exists, some workflows missing |
| :x: **Not Implemented** | Zero code exists |

---

## Summary Matrix

| # | Feature | Status | Evidence |
|---|---------|--------|----------|
| 1 | Military Rank System | :white_check_mark: Implemented | Enum, migration, service, 18 integration tests |
| 2 | V3 Grant Scope Inheritance | :white_check_mark: Implemented | Full hierarchy traversal, 97+ grant types, scope resolution |
| 3 | Auto-Grant System | :white_check_mark: Implemented | ApplyAutoGrantsAsync wired to role assignment/removal |
| 4 | Setup Tasks | :white_check_mark: Implemented | Model, service, admin UI, auto-generation |
| 5 | Circle/Friends | :white_check_mark: Implemented | Model, service, request/accept workflow, tests |
| 6 | Shift Programs & Master Programs | :white_check_mark: Implemented | Models, generation service, UI pages |
| 7 | Duty Rotation | :x: Not Implemented | No models, services, or UI |
| 8 | Settings Hierarchy | :white_check_mark: Implemented | Cascade resolution, tests |
| 9 | Announcements | :white_check_mark: Implemented | Model, service, admin page, migration, tests |
| 10 | Schedule Export/Print | :white_check_mark: Implemented | QuestPDF + ClosedXML + CSV, API endpoint |
| 11 | Tech Shifts | :warning: Partial | Grant types seeded, workflows missing |
| 12 | Vacation Approval Chain | :warning: Partial | Model exists, multi-level routing incomplete |
| 13 | CSS Token Migration | :white_check_mark: Implemented | No legacy --text-primary references remain |
| 14 | Excel-Like Calendars | :construction: In Progress | Design complete, ~40% implemented in worktree |

---

## Implemented Features

### 1. Military Rank System :white_check_mark:

**Completed:** 2026-02-04
**Key Files:**
- `Models/Support/MilitaryRank.cs` — 18 IDF ranks (Turai → RavAluf)
- `Models/Support/MilitaryRankExtensions.cs` — IsOfficer(), IsNCO(), IsEnlisted(), GetBadgeClass(), GetDisplayName(), GetAbbreviation()
- `Models/AppUser.cs` — Rank property (default: Turai)
- `Migrations/20260204161515_AddMilitaryRankToAppUser.cs`
- `Services/OnDutyService.cs` — Rank-based eligibility with EnforceRankEligibility feature flag
- `ShiftManager.Tests/IntegrationTests/MilitaryRankIntegrationTests.cs` — 18 test cases
- `ShiftManager.Tests/UnitTests/Models/MilitaryRankExtensionsTests.cs`
- UI: Admin EditProfile rank dropdown, User Settings, OnDuty public page rank badges

### 2. V3 Grant Scope Inheritance :white_check_mark:

**Completed:** 2026-02-06
**Key Files:**
- `Services/GrantService.cs` — Full scope inheritance traversal (Project → Area → Molecule → Company)
- `Services/IGrantService.cs` — HasGrantWithScopeAsync(), GetAccessibleCompanyIdsForGrantAsync(), HasGrantForCompanyAsync()
- `Data/SeedData/GrantTypeSeed.cs` — 97+ grant types across 15 categories
- `GrantAuthorizationHandler` — Policy integration
- `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs`
- `docs/plans/2026-02-06-scope-aware-grants-refactoring.md` — All 5 phases completed

### 3. Auto-Grant System :white_check_mark:

**Completed:** 2026-02-06
**Key Files:**
- `Services/GrantService.cs` — ApplyAutoGrantsAsync() (lines 325-374), RemoveAutoGrantsAsync()
- `Services/RoleService.cs` — Auto-grant wiring on role assignment/removal
- `Pages/Admin/Users.cshtml.cs` — Grant-based authorization (converted from role checks)
- Tests in GrantServiceTests.cs (lines 447-524)

### 4. Setup Tasks :white_check_mark:

**Key Files:**
- `Models/SetupTask.cs`, `Models/Support/SetupTaskType.cs`, `Models/Support/SetupTaskStatus.cs`
- `Services/SetupTaskService.cs` — Auto-generation on molecule creation
- `Services/ISetupTaskService.cs`
- `Pages/Admin/SetupTasks/Index.cshtml.cs` — Admin management UI

### 5. Circle/Friends System :white_check_mark:

**Key Files:**
- `Models/UserFriendship.cs` — Full model with status, timestamps
- `Services/FriendshipService.cs` — GetFriends, SendRequest, Accept/Reject workflow
- `Services/IFriendshipService.cs`
- `Pages/Friends/Index.cshtml.cs` — UI page
- `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/FriendshipServiceTests.cs`

### 6. Shift Programs & Master Programs :white_check_mark:

**Key Files:**
- `Models/ShiftProgram.cs`, `Models/ProgramDay.cs`, `Models/MasterProgram.cs`, `Models/MasterProgramItem.cs`
- `Services/ShiftProgramService.cs` — CreateProgram, GenerateInstances, ApplyToDateRange, Detach/Reset
- `Services/MasterProgramService.cs` — Program composition
- `Pages/Owner/Programs.cshtml.cs`, `Pages/Owner/MasterPrograms.cshtml.cs` — UI

### 7. Settings Hierarchy :white_check_mark:

**Key Files:**
- `Models/AreaSettings.cs`, `Models/MoleculeSettings.cs`, `Models/CompanySettings.cs`
- `Services/HierarchySettingsService.cs` — GetEffectiveSettingsAsync() with Company→Molecule→Area→Default cascade
- `Pages/Admin/Settings/Index.cshtml.cs` — Admin UI
- `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/HierarchySettingsServiceTests.cs`

### 8. Announcements :white_check_mark:

**Key Files:**
- `Models/Announcement.cs` — Title, Content, Scope (All/Department/Role), IsPinned, ExpiresAt
- `Services/AnnouncementService.cs` — GetActiveAnnouncements (scope-filtered), GetAll
- `Pages/Admin/Announcements.cshtml.cs` — CRUD management
- `Migrations/20260203222201_AddAnnouncements.cs`
- `ShiftManager.Tests/UnitTests/Services/AnnouncementServiceTests.cs`

### 9. Schedule Export/Print :white_check_mark:

**Key Files:**
- `Services/ScheduleExportService.cs` — QuestPDF, ClosedXML, CSV format handlers
- `Models/Export/ScheduleExportData.cs`, `Models/Export/ScheduleExportRequest.cs`
- `Pages/Api/ScheduleExport.cshtml.cs` — API endpoint
- `wwwroot/css/print.css` — Print-optimized stylesheet

### 10. CSS Token Migration :white_check_mark:

All 33 files migrated. No `--text-primary` or `--text-secondary` references remain. Modern tokens (`--text`, `--text-muted`, `--text-subtle`) used throughout all 10 CSS files in `wwwroot/css/`.

---

## In Progress

### 14. Excel-Like Calendars :construction:

**Status:** Design complete, ~40% implemented in `.worktrees/excel-calendars` branch
**Design Doc:** `docs/plans/2026-02-05-excel-calendars-design.md`

**What's Done (in worktree):**
- :white_check_mark: Database migration (ShiftCapacityOverride, UserDayNote tables)
- :white_check_mark: IShiftCalendarService / ShiftCalendarService
- :white_check_mark: CalendarHub (SignalR real-time updates)
- :white_check_mark: Calendar landing page with premium cards
- :white_check_mark: Shifts Calendar page
- :white_check_mark: Chores Calendar page
- :white_check_mark: On-Call Calendar page
- :white_check_mark: Overview Calendar page
- :white_check_mark: calendar-realtime.js with SignalR integration
- :white_check_mark: Feature flags and URL redirects

**What's Left:**
- [ ] Cell editor popovers (RTL-aware)
- [ ] Advanced filter panel UI
- [ ] Capacity mode UI
- [ ] Row grouping drag-and-drop
- [ ] Full testing and QA
- [ ] Merge to main branch

**Key Models (in worktree):**
- `Models/ShiftCapacityOverride.cs`
- `Models/UserDayNote.cs`
- `Models/ChoreType.cs` (already in main branch)

---

## Not Yet Implemented

### 7. Duty Rotation :x:

**Design Doc:** V3 Design (Section 14)
**What Was Designed:**
- Automatic duty rotation through eligible users
- Frequency-based scheduling (daily, weekly, biweekly, monthly)
- Primary + backup assignee per duty
- Skip rules (vacation, conflict detection)
- Rotation queue management

**Missing:** All of it — no models, services, or UI exist.
**Dependencies:** Military Rank (done), Vacation system (partial)
**Effort:** 24-32 hours

---

### 11. Tech Shifts (Department-Specific) :warning:

**What Exists:**
- :white_check_mark: Grant types for tech shifts seeded (Delta, Hanava, Yekev, Moviltech, NOC, Shiklut)
- :white_check_mark: Department entity exists

**Missing:**
- [ ] Tech shift type seeding
- [ ] Tech calendar views
- [ ] Department-scoped filtering in calendars
- [ ] Tech shift assignment workflow

**Effort:** 16-24 hours (reduced — infrastructure already in place)

---

### 12. Vacation Approval Chain :warning:

**What Exists:**
- :white_check_mark: `TimeOffRequest` model with Status (Pending/Approved/Rejected) and ApproverId

**Missing:**
- [ ] JobType → Approver mapping
- [ ] Multi-level approval routing service
- [ ] Auto-approval for short leave (< N days)
- [ ] Approval matrix with fallback logic

**Effort:** 16-24 hours

---

## Remaining Work Summary

| Feature | Effort | Blocking Release? |
|---------|--------|-------------------|
| Excel Calendars (finish + merge) | 20-30h | No (behind feature flag) |
| Duty Rotation | 24-32h | No |
| Tech Shifts (workflows) | 16-24h | No |
| Vacation Approval (routing) | 16-24h | No |
| **Total** | **~76-110h** | **None are blockers** |

---

## Active Code TODOs

| File | Line | Issue |
|------|------|-------|
| `Models/Molecule.cs` | 22 | ChoreType navigation property commented out |
| `Pages/Api/Hierarchy/Reorder.cshtml.cs` | 79 | Reorder is a stub (returns success, no persistence) |
| `Services/ShiftAssignmentService.cs` | 216 | Weekly cap hardcoded to 60h (should use settings hierarchy) |
| `Services/WidgetService.cs` | 232 | Widget preferences not persisted (localStorage only) |
| `Services/DirectorService.cs` | 50 | Interface async refactor noted |
| `wwwroot/js/localization-attributes.js` | 269 | Draft system integration incomplete |

---

**Last Updated:** 2026-02-06 (full codebase verification audit)
