# Designed But Not Implemented Systems

**Version:** 1.0
**Date:** 2026-02-04
**Source:** V3 Design Documents, Genesis Docs, Plan Files

---

## Overview

This document catalogs all features that have been designed (partially or fully) but not yet implemented in the codebase. Each item includes current status, missing pieces, dependencies, and recommended next action.

---

## Priority Legend

| Priority | Meaning |
|----------|---------|
| 🔴 **Critical** | Blocks other features, security/correctness issue |
| 🟠 **High** | High business value, enables key workflows |
| 🟡 **Medium** | Useful but not blocking |
| 🟢 **Low** | Nice to have, can defer |

---

## 1. Military Rank System

| Attribute | Value |
|-----------|-------|
| **Status** | 📋 Fully Designed |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 23) |
| **Priority** | 🟠 High |
| **Effort** | 16-24 hours |

### What Was Designed
- `MilitaryRank` enum with 18 IDF ranks (Turai → RavAluf)
- `IsOfficer()`, `IsNCO()`, `IsEnlisted()` helper methods
- `RequiresOfficerRank` flag on duty types
- Rank-based eligibility for Katzin on-call
- Rank display/abbreviations in Hebrew and English

### Missing Pieces
- [ ] `MilitaryRank` enum not created
- [ ] `AppUser.Rank` property not added
- [ ] Database migration not created
- [ ] Eligibility service not implemented
- [ ] UI for rank selection not built
- [ ] Integration with OnDutyService

### Dependencies
- None (self-contained)

### Suggested Next Action
**Implementation plan created:** `docs/plans/2026-02-04-military-rank-system.md`
Execute the plan in a separate session.

---

## 2. V3 Grant System (Full Scope Inheritance)

| Attribute | Value |
|-----------|-------|
| **Status** | ⚠️ Partially Implemented |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Sections 6-10) |
| **Priority** | 🔴 Critical |
| **Effort** | 60-80 hours |

### What Was Designed
- 107 built-in grant types across 15 categories
- Hierarchical scope inheritance (Project → Area → Molecule → Company)
- `CanOwn` / `CanGive` permission delegation
- 6 cascading auto-grant rules
- Grant scope checking algorithm

### What's Implemented
- ✅ Grant, GrantType, RoleTemplate, RoleTemplateGrant models
- ✅ 97+ grant types seeded
- ✅ Basic `HasGrantAsync()` method
- ✅ `GrantAuthorizationHandler` policy integration

### Missing Pieces
- [ ] Full scope inheritance traversal (Project > Area > Molecule > Company)
- [ ] Auto-grant wiring on role assignment
- [ ] `CanGive` delegation enforcement
- [ ] Grant management UI (assign/revoke)
- [ ] Role template assignment workflow
- [ ] Scope resolution for nested hierarchies

### Dependencies
- Depends on: Hierarchy Navigation Service (for scope resolution)

### Suggested Next Action
1. Create implementation plan for "Grant Scope Inheritance"
2. Build HierarchyNavigationService first
3. Then implement scope traversal in GrantService

---

## 3. Auto-Grant System

| Attribute | Value |
|-----------|-------|
| **Status** | 📋 Designed, Not Implemented |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 10) |
| **Priority** | 🔴 Critical |
| **Effort** | 16-24 hours |

### What Was Designed
- When user is assigned a RoleTemplate, auto-create Grant records
- Auto-grant cascading rules (e.g., AssignShifts → ViewCalendar)
- Remove auto-grants when role is revoked
- `IsAutoGrant` flag to distinguish from manual grants

### Missing Pieces
- [ ] `ApplyAutoGrantsAsync()` not called on role assignment
- [ ] Role assignment service/UI not built
- [ ] Auto-grant cascade logic not implemented
- [ ] `RemoveAutoGrantsAsync()` on role revocation

### Dependencies
- Depends on: Grant system, Role assignment workflow

### Suggested Next Action
Create implementation plan for "Auto-Grant Wiring"

---

## 4. Setup Tasks System

| Attribute | Value |
|-----------|-------|
| **Status** | ⚠️ Partially Implemented |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 20) |
| **Priority** | 🟠 High |
| **Effort** | 24-32 hours |

### What Was Designed
- 10 setup task types for onboarding new molecules/companies
- Auto-generation when hierarchy entities created
- Suggested assignees based on existing roles
- Task completion triggers role/grant assignment
- Sequential task dependencies

### What's Implemented
- ✅ `SetupTask` model
- ✅ `SetupTaskType` enum
- ✅ `SetupTaskService` (partial)

### Missing Pieces
- [ ] Auto-generation on Molecule/Company create
- [ ] Setup task dashboard UI
- [ ] Task completion → role assignment wiring
- [ ] Suggested assignee algorithm
- [ ] Task dependency ordering

### Dependencies
- Depends on: Role assignment, Auto-grant system

### Suggested Next Action
Create implementation plan for "Setup Task Automation"

---

## 5. Circle/Friends System

| Attribute | Value |
|-----------|-------|
| **Status** | 📋 Designed, Minimal Implementation |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 16) |
| **Priority** | 🟡 Medium |
| **Effort** | 16-24 hours |

### What Was Designed
- Cross-molecule visibility via friendship connections
- Mutual friendship acceptance workflow
- View friends' shifts, vacations, chores
- Admin-managed friendships
- No limit on friend count

### What's Implemented
- ✅ `UserFriendship` model (minimal)

### Missing Pieces
- [ ] Friendship request/accept workflow
- [ ] Friends list UI
- [ ] Cross-molecule visibility in calendars
- [ ] Friend status in team calendar
- [ ] API endpoints for friendship management

### Dependencies
- Depends on: Calendar scope system (for visibility filtering)

### Suggested Next Action
Create implementation plan for "Circle/Friends Feature"

---

## 6. Shift Programs & Master Programs

| Attribute | Value |
|-----------|-------|
| **Status** | ⚠️ Partially Implemented |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 12) |
| **Priority** | 🟠 High |
| **Effort** | 32-40 hours |

### What Was Designed
- Weekly shift templates with day-of-week configuration
- Staffing requirements per shift per day
- Master programs combining multiple programs
- Auto-generation of shift instances from programs
- Program attachment/detachment from shifts

### What's Implemented
- ✅ `ShiftProgram`, `ProgramDay`, `MasterProgram`, `MasterProgramItem` models
- ✅ Database schema and indexes
- ✅ Basic program display in Table page

### Missing Pieces
- [ ] Program generation service (auto-create shifts)
- [ ] Program editor UI
- [ ] Master program composition UI
- [ ] Staffing requirement enforcement
- [ ] Program-based fill mode

### Dependencies
- None (self-contained)

### Suggested Next Action
Create implementation plan for "Shift Program Generation"

---

## 7. Duty Rotation System

| Attribute | Value |
|-----------|-------|
| **Status** | 📋 Designed, Not Implemented |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 14) |
| **Priority** | 🟡 Medium |
| **Effort** | 24-32 hours |

### What Was Designed
- Automatic duty rotation through eligible users
- Frequency-based scheduling (daily, weekly, biweekly, monthly)
- Primary + backup assignee per duty
- Skip rules (vacation, conflict detection)
- Rotation queue management

### Missing Pieces
- [ ] Duty rotation service
- [ ] Rotation queue entity/storage
- [ ] Auto-assignment scheduler
- [ ] Skip rule implementation
- [ ] Rotation management UI

### Dependencies
- Depends on: Military Rank (for officer eligibility), Vacation system

### Suggested Next Action
Create implementation plan for "Duty Rotation Automation"

---

## 8. Settings Hierarchy (Override Pattern)

| Attribute | Value |
|-----------|-------|
| **Status** | ⚠️ Partially Implemented |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 19) |
| **Priority** | 🟡 Medium |
| **Effort** | 8-16 hours |

### What Was Designed
- 3-level settings hierarchy: Area → Molecule → Company
- NULL = use parent default, set value = override
- Settings: RestHours, WeeklyCap, etc.
- Resolution service that walks hierarchy

### What's Implemented
- ✅ `AreaSettings`, `MoleculeSettings`, `CompanySettings` models
- ✅ Basic `SettingsService`

### Missing Pieces
- [ ] Full hierarchy resolution logic
- [ ] Settings management UI per level
- [ ] Override indicator in UI
- [ ] Settings change audit logging

### Dependencies
- None (self-contained)

### Suggested Next Action
Create implementation plan for "Settings Hierarchy Resolution"

---

## 9. Announcement Feed

| Attribute | Value |
|-----------|-------|
| **Status** | 📋 Fully Designed |
| **Design Doc** | `docs/plans/2026-02-03-backlog-items.md` (Announcements section) |
| **Priority** | 🟢 Low |
| **Effort** | 16-24 hours |

### What Was Designed
- Company-wide announcements with visibility scopes
- Pinned announcements
- Expiration dates
- Dashboard widget
- Admin CRUD management

### Missing Pieces
- [ ] `Announcement` model
- [ ] Database migration
- [ ] Announcement service
- [ ] Admin management page
- [ ] Dashboard widget
- [ ] Visibility filtering

### Dependencies
- None (self-contained)

### Suggested Next Action
Create implementation plan (low priority, defer)

---

## 10. Schedule Export/Print

| Attribute | Value |
|-----------|-------|
| **Status** | 📋 Fully Designed |
| **Design Doc** | `docs/plans/2026-02-03-backlog-items.md` (Export section) |
| **Priority** | 🟢 Low |
| **Effort** | 24-32 hours |

### What Was Designed
- Export formats: PDF (QuestPDF), Excel (ClosedXML), CSV
- Period selection: Week, Month, Custom
- Filter options: Department, JobType
- Print-optimized stylesheet

### Missing Pieces
- [ ] Export service with format handlers
- [ ] QuestPDF integration
- [ ] ClosedXML integration
- [ ] Export API endpoint
- [ ] Print stylesheet
- [ ] Export dialog UI

### Dependencies
- None (self-contained)

### Suggested Next Action
Create implementation plan (low priority, defer)

---

## 11. Tech Shifts (Department-Specific)

| Attribute | Value |
|-----------|-------|
| **Status** | 📋 Designed, Minimal Implementation |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 13) |
| **Priority** | 🟡 Medium |
| **Effort** | 24-32 hours |

### What Was Designed
- Department-specific shift types: HANAVA, DELTA, YEKEV, MOVILTECH
- Tech molecule structure (vs Workforce)
- Department-scoped grants
- Tech shift calendars

### What's Implemented
- ✅ Grant types for tech shifts defined
- ✅ Department entity exists

### Missing Pieces
- [ ] Tech shift type seeding
- [ ] Tech calendar views
- [ ] Department-scoped filtering
- [ ] Tech shift assignment workflow

### Dependencies
- Depends on: Department entity, Grant system

### Suggested Next Action
Create implementation plan for "Tech Molecule Shifts"

---

## 12. Vacation Approval Chain

| Attribute | Value |
|-----------|-------|
| **Status** | ⚠️ Partially Implemented |
| **Design Doc** | `docs/plans/2026-01-25-organizational-hierarchy-design-v3.md` (Section 15) |
| **Priority** | 🟡 Medium |
| **Effort** | 16-24 hours |

### What Was Designed
- JobType-specific approvers (Alhut Lead → Text Lead → BR Director)
- Approval matrix with fallback
- Multi-level approval for extended leave
- Auto-approval for under N days

### What's Implemented
- ✅ `TimeOffRequest` model with status
- ✅ Basic approval workflow

### Missing Pieces
- [ ] JobType → Approver mapping
- [ ] Approval routing service
- [ ] Multi-level approval logic
- [ ] Auto-approval configuration

### Dependencies
- Depends on: Role system (for approver identification)

### Suggested Next Action
Create implementation plan for "Vacation Approval Routing"

---

## Summary Matrix

| Feature | Status | Priority | Effort | Dependencies |
|---------|--------|----------|--------|--------------|
| Military Rank | Designed | 🟠 High | 16-24h | None |
| Grant Scope Inheritance | Partial | 🔴 Critical | 60-80h | Hierarchy |
| Auto-Grant System | Designed | 🔴 Critical | 16-24h | Grant, Role |
| Setup Tasks | Partial | 🟠 High | 24-32h | Role, Grant |
| Circle/Friends | Minimal | 🟡 Medium | 16-24h | Calendar |
| Shift Programs | Partial | 🟠 High | 32-40h | None |
| Duty Rotation | Designed | 🟡 Medium | 24-32h | Rank, Vacation |
| Settings Hierarchy | Partial | 🟡 Medium | 8-16h | None |
| Announcements | Designed | 🟢 Low | 16-24h | None |
| Export/Print | Designed | 🟢 Low | 24-32h | None |
| Tech Shifts | Minimal | 🟡 Medium | 24-32h | Dept, Grant |
| Vacation Approval | Partial | 🟡 Medium | 16-24h | Role |

**Total Estimated Effort:** ~300-400 hours (8-10 weeks)

---

## Recommended Implementation Order

### Phase 1: Foundation (Weeks 1-3)
1. **Military Rank System** - Self-contained, enables Katzin eligibility
2. **Grant Scope Inheritance** - Critical for all authorization
3. **Auto-Grant System** - Required for role assignment to work

### Phase 2: Core Features (Weeks 4-6)
4. **Setup Tasks** - Enables onboarding workflow
5. **Shift Programs** - Key scheduling feature
6. **Settings Hierarchy** - Quick win, low complexity

### Phase 3: Extended Features (Weeks 7-9)
7. **Vacation Approval Chain** - Improves workflow
8. **Tech Shifts** - Department-specific scheduling
9. **Duty Rotation** - Automation

### Phase 4: Nice-to-Have (Weeks 10+)
10. **Circle/Friends** - Social feature
11. **Announcements** - Communication
12. **Export/Print** - Reporting

---

## Tracking

This document should be updated when:
- A feature moves from "Designed" to "Implemented"
- New designs are created
- Priority changes based on business needs
- Dependencies are resolved

**Last Updated:** 2026-02-04
