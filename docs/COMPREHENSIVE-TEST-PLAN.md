# ShiftManager — Comprehensive Test Plan
*Maps every feature in FEATURE-INVENTORY.md to specific, implementable test cases*
*Generated 2026-02-25*

---

## Executive Summary

- **Total features in inventory**: 781 checkable items across 53 categories
- **Test approach**: Playwright E2E (primary) + xUnit .NET (secondary)
- **Existing Playwright modules**: 29 files (A through Z + every-button + ui-sweep + setup), totaling ~460 test cases
- **Existing xUnit tests**: 10 files (6 unit + 4 integration), totaling ~120 test cases
- **Estimated NEW Playwright specs needed**: 12 new module files
- **Estimated NEW xUnit files needed**: 15 new test files
- **Total test cases in this plan**: ~950 (existing ~580 + new ~370)

### Existing Coverage Summary

| Module | File | Tests | Inventory Sections Covered |
|--------|------|-------|---------------------------|
| 00-Setup | `00-setup-test-data.spec.js` | setup | Test data creation |
| A: Seeding | `module-a-seeding.spec.js` | 12 | 46.1-46.8 |
| B: Auth | `module-b-authentication.spec.js` | 12 | 1.1-1.6, 1.8 |
| C: Signup | `module-c-signup.spec.js` | 10 | 2.1-2.2 |
| D: Users | `module-d-user-management.spec.js` | 14 | 3.1-3.11 |
| E: Context | `module-e-context-switcher.spec.js` | 8 | 6.1-6.3 |
| F: Blueprints | `module-f-blueprints.spec.js` | 16 | 7.1-7.8 |
| G: Programs | `module-g-programs.spec.js` | 20 | 8.1-8.5 |
| H: Master Programs | `module-h-master-programs.spec.js` | 6 | 8.6 |
| I: Instances | `module-i-instances.spec.js` | 14 | 9.3 (partial), 9.15 |
| J: Shift Assignments | `module-j-shift-assignments.spec.js` | 24 | 9.1-9.6, 9.8-9.10 |
| K: Chores | `module-k-chore-calendar.spec.js` | 16 | 10.1-10.7 |
| L: On-Duty | `module-l-onduty-calendar.spec.js` | 18 | 11.1-11.9 |
| M: Overview | `module-m-overview.spec.js` | 16 | 12.1-12.3 |
| N: Time-Off | `module-n-timeoff.spec.js` | 12 | 13.1-13.6 |
| O: Swaps | `module-o-swaps.spec.js` | 10 | 14.1-14.3 |
| P: Tenant Isolation | `module-p-tenant-isolation.spec.js` | 14 | 17.1-17.3 |
| Q: Role Access | `module-q-role-access.spec.js` | 33 | 4.1 (partial), 39.2 |
| R: Concurrency | `module-r-concurrency.spec.js` | 10 | 30.1, 36.3 |
| S: REST API | `module-s-rest-api.spec.js` | 21 | 18.1-18.8 |
| T: Page Handler API | `module-t-page-handler-api.spec.js` | 16 | 9.9, 10.3, 11.3, 12.3 |
| U: Owner Admin | `module-u-owner-admin.spec.js` | 18 | 19.1-19.18 |
| V: Admin Org | `module-v-admin-org.spec.js` | 12 | 5.1-5.8, 20.9-20.11 |
| W: Director/My/Public | `module-w-director-my-public.spec.js` | 14 | 22.1-22.3, 23.1-23.5, 21.1-21.3 |
| X: Localization | `module-x-localization.spec.js` | 12 | 16.1-16.6 |
| Y: Notifications | `module-y-notifications.spec.js` | 8 | 15.1-15.5 |
| Z: Error Handling | `module-z-error-handling.spec.js` | 10 | 29.1-29.5 |
| Every-Button | `module-every-button.spec.js` | 33 | 39.1-39.2 (interactive) |
| UI-Sweep | `module-ui-sweep.spec.js` | 17 | 26.1-26.3, 49.1-49.3 |

### xUnit Coverage

| File | Tests | Coverage |
|------|-------|---------|
| `GrantServiceTests.cs` | ~20 | 4.1 Grant authorization, scope matching |
| `ShiftAssignmentServiceTests.cs` | ~15 | 9.3 JobType eligibility filtering only (NOT full assignment CRUD — coverage is narrower than section 9.3's 19 POST handlers) |
| `DirectorServiceTests.cs` | 15 | 22.1-22.3 Director service logic |
| `ConcurrencyServiceTests.cs` | 5 | 36.3 Concurrent edit detection |
| `OnDutyServiceEligibilityTests.cs` | 6 | 11.6, 11.9 Duty eligibility |
| `AnnouncementServiceTests.cs` | 20 | 20.4 Announcement CRUD |
| `CalendarDateValidationTests.cs` | 28 | 9.1 Date boundary validation |
| `MilitaryRankExtensionsTests.cs` | 6 | 11.9 Rank enum helpers |
| `FriendshipServiceTests.cs` | ~8 | 24.1 Friendship logic |
| `HierarchySettingsServiceTests.cs` | ~8 | 5.9 Cascading settings |
| `ScopeFilterServiceTests.cs` | ~8 | 17.3 Scope filtering |
| `DirectorCreationTests.cs` | 6 | 20.6, 22.1 Director integration |
| `DirectorCrossTenantTests.cs` | 6 | 17.1 Cross-tenant isolation |
| `MilitaryRankIntegrationTests.cs` | 18 | 11.9 Rank integration |
| `ProgramManagementAuthorizationTests.cs` | 10 | 8.1-8.5 Program auth |

---

## Testing Strategy

### Principles

1. Every test MUST make at least one STRICT assertion (`expect(...).toBe/toBeVisible/toContainText`)
2. No silent skips: no `.catch(() => false)` or `if (visible) { test } else { skip }`
3. Evidence screenshots for every Playwright test (saved to `ProductionReady/`)
4. Tests must be deterministic: no race conditions or timing-dependent assertions
5. Use existing helpers from `production-qa-helpers.js`:
   - **Auth**: `login`, `loginAsOwner`, `logout`
   - **Navigation**: `navigateTo`, `navigateExpecting` (for asserting 403/redirect)
   - **Assertions**: `assertPageContains`, `assertPageNotContains`, `assertMinCount`
   - **Evidence**: `saveEvidence`, `saveFullPageEvidence`, `saveBugEvidence`, `saveApiEvidence`
   - **UI**: `waitForToast`, `collectConsoleErrors`
   - **Data**: `createUser`, `TEST_USERS`, `TEST_PASSWORD`, `QA_COMPANIES`, `BASE_URL`
6. Negative tests must assert the error state, not just "did not crash"
7. xUnit tests use InMemory database with `Guid.NewGuid()` name, Moq for dependencies, FluentAssertions, Arrange-Act-Assert pattern, IDisposable cleanup. **Important**: Services that read tenant-scoped tables (via `IBelongsToCompany`) require a mock `ITenantResolver` returning a fixed CompanyId.

### Test User Matrix

| User | Email | Password | Role | RoleTemplate | Use For |
|------|-------|----------|------|-------------|---------|
| Owner | `admin@local` | `admin123` | Owner | Owner | Admin pages, config, full access tests |
| Test Owner | `test.owner@shifty.test` | `TestOwner123!` | Owner | Owner | B-041 strategy tests |
| Test Director | `test.director@shifty.test` | `TestDirector123!` | Director | BRDirector | Director features, cross-company |
| Test Manager | `test.manager@shifty.test` | `TestManager123!` | Manager | AlhutLead | Calendar assignment, approvals |
| Test Assigner | `test.assigner@shifty.test` | `TestAssigner123!` | Assigner | Assigner | Limited assignment access |
| Test Employee (Member) | `test.member@shifty.test` | `TestMember123!` | Employee | Employee | Read-only views, request creation |
| Test NoGrants | `test.nogrants@shifty.test` | `TestNoGrants123!` | Employee | — | Trainee-like limited access tests |
| Locked User | `locked@test` | `Test1234!` | Employee | Employee | Required by B-05; created in 00-setup-test-data |

### Execution Order (Production QA)

Tests run sequentially in a single worker. Order matters because later modules depend on data created by earlier ones.

**CRITICAL**: All production-qa tests (including new AA–AL modules) MUST be run via `npm run qa:production` which uses `playwright.production-qa.config.js` (`fullyParallel: false, workers: 1`). NEVER run these via bare `npm test` — that uses the parallel config (`fullyParallel: true, workers: 2`) and will cause random failures from data race conditions between modules.

```
00-setup-test-data    (creates test users & data)
module-a-seeding      (verifies seed data exists)
module-b-authentication
module-c-signup
module-d-user-management
module-e-context-switcher
module-f-blueprints
module-g-programs
module-h-master-programs
module-i-instances
module-j-shift-assignments
module-k-chore-calendar
module-l-onduty-calendar
module-m-overview
module-n-timeoff
module-o-swaps
module-p-tenant-isolation
module-q-role-access
module-r-concurrency
module-s-rest-api
module-t-page-handler-api
module-u-owner-admin
module-v-admin-org
module-w-director-my-public
module-x-localization
module-y-notifications
module-z-error-handling
module-every-button
module-ui-sweep
--- NEW MODULES (append after existing) ---
module-aa-grants-roletemplate
module-ab-friends-game
module-ac-team-calendars
module-ad-admin-pages
module-ae-security-headers
module-af-signalr-realtime
module-ag-print-export
module-ah-keyboard-a11y
module-ai-mobile-responsive
module-aj-performance-loading
module-ak-widgets-telemetry
module-al-coverage-sweep
```

### Priority Classification

- **P0 (Critical)**: Auth, login, logout, core CRUD, data integrity, tenant isolation, assignment operations, grant enforcement
- **P1 (High)**: Calendar views, notifications, localization, role-based access, REST API, approval workflows
- **P2 (Medium)**: UI polish, dark mode, print, export, keyboard shortcuts, offline detection, friends, team calendars
- **P3 (Low)**: Edge cases, diagnostics, gamification, seed data verification, telemetry, PWA manifest

---

## Gap Analysis

### Features with GOOD existing coverage (minor gaps only)

| Inventory Section | Existing Module | Gap |
|-------------------|----------------|-----|
| 1. Auth & Sessions | Module B (12 tests) | Missing: 1.3 rate limiting verification, 1.7 session monitoring JS polling, 1.9 cookie config details |
| 2. Signup | Module C (10 tests) | Missing: cascading dropdown API validation, HQ company exclusion |
| 3. User Management | Module D (14 tests) | Missing: 3.9 CSV export download, 3.10 bulk import, 3.11 full profile editing |
| 7. Blueprints | Module F (16 tests) | Missing: 7.7 publish/unpublish to molecule, 7.8 populate name keys |
| 8. Programs | Module G+H (26 tests) | Good coverage |
| 9. Shift Calendar | Module I+J (38 tests) | Missing: 9.4 roster dock drag-drop, 9.5 fill handle, 9.7 mobile bottom sheet, 9.11-9.14 legacy views |
| 10. Chores | Module K (16 tests) | Missing: 10.5 hard delete, 10.6 restore chore |
| 11. On-Duty | Module L (18 tests) | Missing: 11.8 duty rotation CRUD, 11.9 rank eligibility |
| 12. Overview | Module M (16 tests) | Missing: 12.2 user day notes CRUD |
| 13. Time-Off | Module N (12 tests) | Missing: 13.5 approval workflow rules |
| 14. Swaps | Module O (10 tests) | Good coverage |
| 15. Notifications | Module Y (8 tests) | Missing: 15.4 daily digest, 15.5 director hub |
| 16. Localization | Module X (12 tests) | Missing: 16.3 company overrides, 16.5 edit mode |
| 17. Tenant Isolation | Module P (14 tests) | Good coverage |
| 18. REST API | Module S (21 tests) | Missing: 18.4 rate limiting, 18.6 request logging |
| 19. Owner Tools | Module U (18 tests) | Missing: 19.11 data lifecycle, 19.15 telemetry, 19.16 audit search |
| 20. Admin Pages | Module V (12 tests) | Missing: 20.2 analytics, 20.3 audit log, 20.4 announcements, 20.7-20.8 settings/setup |

### Features with NO existing coverage (need new modules)

| Inventory Section | Priority | New Module |
|-------------------|----------|------------|
| 4. Authorization & Grants (4.1-4.6) | P0 | module-aa |
| 5.9 Hierarchy Settings | P1 | module-ad |
| 20.2 Analytics | P1 | module-ad |
| 20.3 Audit Log | P1 | module-ad |
| 20.4 Announcements | P2 | module-ad |
| 20.5 App Configuration | P1 | module-ad |
| 20.6 Director Management | P1 | module-ad |
| 20.7-20.8 Settings/Setup Tasks | P2 | module-ad |
| 21. Public Pages (detailed tests) | P1 | existing module-w (extend) |
| 24. Friends System | P2 | module-ab |
| 25. Gamification | P3 | module-ab |
| 26. Dark Mode (detailed) | P2 | existing module-ui-sweep (extend) |
| 27. Print & Export | P2 | module-ag |
| 28. Keyboard/A11y | P2 | module-ah |
| 29.1-29.4 (detailed error handling) | P1 | existing module-z (extend) |
| 30. SignalR Real-Time | P1 | module-af |
| 31. Security Features | P0 | module-ae |
| 32. Diagnostics | P3 | module-ad |
| 33. Job Types & Groupings | P1 | module-ad |
| 34. Team Calendars | P2 | module-ac |
| 35. Tech Shift Features | P2 | xUnit only |
| 36. Conflict Detection | P1 | xUnit + module-af |
| 37. Client Telemetry | P3 | module-ak |
| 38. Calendar Skeleton | P3 | module-aj |
| 39. Sidebar (detailed) | P2 | existing module-every-button (extend) |
| 40. Widget System | P2 | module-ak |
| 41. Performance/Lazy Loading | P3 | module-aj |
| 42. Background Services | P2 | xUnit only |
| 43. Calendar Radar | P3 | module-aj |
| 44. Schedule View | P3 | module-aj |
| 45. Form Validation | P2 | module-ah |
| 46. Seed Data (verification) | P3 | existing module-a |
| 47. Middleware | P2 | module-ae |
| 48. View Components | P2 | module-aj |
| 49. Mobile Support | P2 | module-ai |
| 50. Startup Safety | P2 | xUnit only |
| 51. API Client Utils | P3 | module-aj |
| 52. Config/Seeding | P3 | xUnit only |
| 53. Logging | P3 | xUnit only |

---

## Test Specifications

---

### Module B: Authentication (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 1.1-1.9
**Playwright file**: `tests/production-qa/module-b-authentication.spec.js`
**Evidence folder**: `ProductionReady/02-auth/`

#### Existing Tests (12)

| ID | Feature Ref | Test Name | Status |
|----|------------|-----------|--------|
| B-01 | 1.1 | Owner login success | EXISTING |
| B-02 | 1.1 | Wrong password shows error | EXISTING |
| B-03 | 1.1 | Non-existent email shows error | EXISTING |
| B-04 | 1.4 | Empty fields submit shows validation errors | EXISTING |
| B-05 | 1.2 | Account lockout after failed attempts | EXISTING |
| B-06 | 1.6 | Logout clears session | EXISTING |
| B-07 | 1.7 | Session status endpoint returns valid | EXISTING |
| B-08 | 1.5 | Griffin ADFS button rendering | EXISTING |
| B-09 | 1.8 | ForgotPassword page has password inputs | EXISTING |
| B-10 | 31.1 | Anti-forgery token present | EXISTING |
| B-11 | 16.2 | Language toggle on login page | EXISTING |
| B-12 | 16.1 | RTL layout in Hebrew | EXISTING |

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| B-13 | 1.1 | Login with return URL redirect | E2E | 1. Navigate to `/Calendar/Shifts` unauthenticated 2. Get redirected to login 3. Login as owner | Redirected back to `/Calendar/Shifts` after login | P0 |
| B-14 | 1.1 | Login-time RoleTemplate backfill | E2E | 1. Create user without roleTemplate via DB 2. Login as that user | User gets roleTemplate assigned, can access dashboard | P1 |
| B-15 | 1.3 | Rate limiting on login prevents brute force | E2E | 1. Submit 12 rapid failed logins from same page 2. Attempt another login | Error message containing "rate limit" or "too many" appears | P1 |
| B-16 | 1.7 | Session monitoring auto-redirect | E2E | 1. Login as owner 2. Navigate to a page 3. Clear auth cookie via `await page.context().clearCookies()` (NOT JS — cookie is HttpOnly) 4. Add `test.setTimeout(90000)` 5. Wait for session-check.js polling redirect | Page redirects to `/Auth/Login` | P1 |
| B-17 | 1.8 | Forced password change redirect | E2E | 1. Set `MustChangePassword` on test user via admin 2. Login as that user | Redirected to `/Auth/ForgotPassword` | P1 |
| B-18 | 1.8 | Change password form validates strength | E2E | 1. Navigate to `/Auth/ForgotPassword` 2. Submit short password | Validation error shown | P2 |
| B-19 | 1.9 | Auth cookie is HttpOnly | E2E | 1. Login as owner 2. Intercept the login POST response via `page.waitForResponse()` 3. Check `Set-Cookie` response header | Header contains `shiftmgr.auth` with `HttpOnly` flag (NOTE: do NOT use `document.cookie` — it always hides HttpOnly cookies regardless of server config, so it can't detect regressions) | P1 |
| B-20 | 1.1 | Login with authRequired reason shows prompt | E2E | 1. Navigate to `/Auth/Login?reason=authRequired` | Auth required prompt message is visible | P2 |
| B-21 | 1.4 | Oversized email input rejected | E2E | 1. Enter 300-char email, valid password 2. Submit | Validation error for email length | P2 |
| B-22 | 1.1 | Login-time grant reconciliation | E2E | 1. Login as manager user 2. Check sidebar nav items | User has expected grants reflected in visible navigation items | P1 |
| B-23 | 1.5 | Griffin button hidden when disabled | E2E | 1. Ensure `FF_GRIFFIN_ENABLED=false` 2. Navigate to login page | `.auth-adfs__btn` is NOT present in the DOM | P2 |

---

### Module C: Signup (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 2.1-2.2
**Playwright file**: `tests/production-qa/module-c-signup.spec.js`
**Evidence folder**: `ProductionReady/03-signup/`

#### Existing Tests (10) — cover basic signup, duplicate detection, approval, rejection

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| C-11 | 2.1 | Cascading dropdown loads molecules first | E2E | 1. Navigate to `/Auth/Signup` 2. Check molecule dropdown is populated | Molecule dropdown has options from `GetSignupOptions` API | P1 |
| C-12 | 2.1 | Company dropdown cascades from molecule | E2E | 1. Select a molecule 2. Wait for company dropdown to populate | Company dropdown loads options matching selected molecule | P1 |
| C-13 | 2.1 | JobType dropdown cascades from company | E2E | 1. Select molecule then company 2. Wait for job type dropdown | Job type options appear | P1 |
| C-14 | 2.1 | RoleTemplate dropdown filters by IsVisibleInSignup | E2E | 1. Complete cascade to role template dropdown | Only templates with `IsVisibleInSignup=true` shown | P1 |
| C-15 | 2.1 | HQ companies excluded from signup | E2E | 1. Open signup page 2. Select any molecule 3. Check company list | No HQ company names in dropdown | P2 |
| C-16 | 2.1 | Signup rate limiting | E2E | 1. Submit 5+ rapid signup requests | Rate limit error message appears | P2 |
| C-17 | 2.2 | Batch approve multiple join requests | E2E | 1. Login as owner 2. Navigate to Admin/Users 3. Select multiple pending requests 4. Batch approve | All selected requests approved, users created | P1 |
| C-18 | 2.2 | Rejection sends notification with reason | E2E | 1. Login as owner 2. Reject a join request with reason text | Request rejected, notification created | P1 |
| C-19 | 2.1 | Feature flag gates signup page | E2E | 1. Disable `FF_ALLOW_PUBLIC_SIGNUP` flag 2. Navigate to `/Auth/Signup` | Page inaccessible or shows disabled message | P1 |

---

### Module D: User Management (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 3.1-3.11
**Playwright file**: `tests/production-qa/module-d-user-management.spec.js`
**Evidence folder**: `ProductionReady/04-user-management/`

#### Existing Tests (14) — cover add user, toggle active, role change, bulk import basics

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| D-15 | 3.1 | Search users by name | E2E | 1. Login as owner 2. Navigate to Admin/Users 3. Type search term 4. Submit | Filtered results shown matching search | P1 |
| D-16 | 3.1 | Pagination on user list | E2E | 1. Navigate to Admin/Users 2. Click page 2 link | Page 2 of users displayed | P2 |
| D-17 | 3.5 | Change user job type | E2E | 1. Find a user 2. Change job type dropdown 3. Submit | Job type updated, page reflects change | P1 |
| D-18 | 3.6 | Reset user password | E2E | 1. Find a user 2. Click reset password 3. Enter new password 4. Submit | Password reset successful, user can login with new password | P0 |
| D-19 | 3.7 | Unlock user account | E2E | 1. Lock a user via failed logins 2. Go to Admin/Users 3. Unlock | User can login again | P1 |
| D-20 | 3.8 | Delete user permanently | E2E | 1. Create a throwaway user 2. Delete the user | User no longer appears in list | P1 |
| D-21 | 3.9 | Export users to CSV | E2E | 1. Navigate to Admin/Users 2. Click export CSV | Download initiated, response has CSV content-type | P2 |
| D-22 | 3.10 | Bulk import users from file | E2E | 1. Prepare CSV file 2. Upload via bulk import form | Users created from CSV data | P2 |
| D-23 | 3.11 | Edit user profile as admin | E2E | 1. Navigate to Admin/EditProfile?userId=X 2. Update display name 3. Save | Profile updated, change reflected | P1 |
| D-24 | 3.11 | Admin edit profile — avatar upload | E2E | 1. Navigate to EditProfile 2. Upload avatar image | Avatar appears on profile | P2 |
| D-25 | 3.11 | Admin edit profile — emergency contacts | E2E | 1. Navigate to EditProfile 2. Fill emergency contact fields 3. Save | Emergency contact saved | P2 |
| D-26 | 3.4 | Role change triggers grant reconciliation | E2E | 1. Change user role template 2. Check user's grants | Grants updated to match new template | P1 |

---

### Module E: Context Switcher (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 6.1-6.3
**Playwright file**: `tests/production-qa/module-e-context-switcher.spec.js`
**Evidence folder**: `ProductionReady/05-context-switcher/`

#### Existing Tests (8) — cover basic switching, search, clear

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| E-09 | 6.1 | Scope switcher returns hierarchy-aware data | E2E | 1. Login as owner 2. Navigate to Calendar/Shifts 3. Open scope switcher | Dropdown shows molecule + job type combinations | P1 |
| E-10 | 6.1 | Context-aware scope per calendar type | E2E | 1. Navigate to Calendar/Chores 2. Open scope switcher | Scope options appropriate for chores (molecule-scoped) | P1 |
| E-11 | 6.2 | Owner company selector persists | E2E | 1. Login as owner 2. Select a company 3. Navigate to another page | Company selection persists across pages | P1 |
| E-12 | 6.2 | Clear company selection | E2E | 1. Select a company 2. Clear selection | Selection cleared, back to all-company view | P2 |

---

### Module F: Blueprints (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 7.1-7.8
**Playwright file**: `tests/production-qa/module-f-blueprints.spec.js`
**Evidence folder**: `ProductionReady/06-blueprints/`

#### Existing Tests (16) — cover CRUD, usage check, delete confirmation

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| F-17 | 7.7 | Publish blueprint to molecule | E2E | 1. Login as owner 2. Navigate to Blueprints 3. Publish a blueprint to a molecule+jobType | Blueprint visible in calendar for that molecule | P1 |
| F-18 | 7.7 | Unpublish blueprint from molecule | E2E | 1. Unpublish a published blueprint | Blueprint no longer appears in calendar | P1 |
| F-19 | 7.8 | Populate name keys backfill | E2E | 1. Click populate name keys button | Name keys populated for existing blueprints | P2 |

---

### Module J: Shift Assignments (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 9.1-9.15
**Playwright file**: `tests/production-qa/module-j-shift-assignments.spec.js`
**Evidence folder**: `ProductionReady/10-shift-assignments/`

#### Existing Tests (24) — cover calendar rendering, cell attributes, assignment, unassignment, trainee, modes

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| J-25 | 9.4 | Roster dock opens and lists employees | E2E | 1. Navigate to Calendar/Shifts 2. Click roster toggle button | Roster side panel opens with employee list | P1 |
| J-26 | 9.4 | Roster dock search filters employees | E2E | 1. Open roster dock 2. Type in search field | Employee list filtered by search term | P2 |
| J-27 | 9.5 | Fill handle visible on assignment cells | E2E | 1. Navigate to Calendar/Table 2. Find an assigned cell | Fill handle element (`.fill-handle`) visible on hover | P2 |
| J-28 | 9.3 | Fill range copies assignments to date range | E2E | 1. Navigate to Calendar/Table 2. Trigger fill range on an assignment 3. Select target dates | Assignments copied to target dates | P1 |
| J-29 | 9.3 | Get employee availability returns status | E2E | 1. Call `GetEmployeeAvailabilityAsync` handler via fetch 2. Check response | JSON response with availability data per date | P1 |
| J-30 | 9.10 | Shift history shows audit trail | E2E | 1. Make an assignment 2. Call shift history API | History entry shows the assignment action | P1 |
| J-31 | 9.11 | Month calendar view renders | E2E | 1. Navigate to `/Calendar/Month` | Monthly grid renders with shift data | P2 |
| J-32 | 9.12 | Week calendar view renders | E2E | 1. Navigate to `/Calendar/Week` | Weekly view renders | P2 |
| J-33 | 9.13 | Day calendar view shows details | E2E | 1. Navigate to `/Calendar/Day?date=YYYY-MM-DD` | Day detail view with assignments and trainee badges | P2 |
| J-34 | 9.15 | Capacity override changes staffing | E2E | 1. Navigate to Calendar/Table in capacity mode 2. Override staffing for a shift | Capacity number changes, reflected in the cell | P1 |
| J-35 | 9.3 | Detach instance from program | E2E | 1. Find a program-linked instance 2. Detach it | Instance becomes standalone, name/times editable | P1 |
| J-36 | 9.3 | Reset detached instance to program | E2E | 1. Find a detached instance 2. Reset to program | Instance re-links to program template | P2 |
| J-37 | 9.3 | Create custom shift type inline | E2E | 1. Navigate to Calendar/Table 2. Create custom shift type | New shift type row appears | P2 |
| J-38 | 9.3 | AssignUserToSlot assigns user to specific slot | E2E | 1. Navigate to Calendar/Table 2. Use OnPostAssignUserToSlotAsync handler (via slot selection UI) | User assigned to the specific slot position | P1 |
| J-39 | 9.3 | UpdateShiftMetadata changes shift properties | E2E | 1. Navigate to Calendar/Table 2. Use OnPostUpdateShiftMetadataAsync handler (via shift property editor) | Shift metadata updated (name, notes, etc.) | P2 |

---

### Module K: Chore Calendar (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 10.1-10.7
**Playwright file**: `tests/production-qa/module-k-chore-calendar.spec.js`
**Evidence folder**: `ProductionReady/11-chore-calendar/`

#### Existing Tests (16) — cover rendering, create, cancel, filter, cross-company

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| K-17 | 10.5 | Hard delete chore via API | E2E | 1. Create a chore 2. Call `DeleteChore` API endpoint | Chore permanently removed | P1 |
| K-18 | 10.6 | Restore cancelled chore | E2E | 1. Cancel a chore 2. Call `RestoreChore` API endpoint | Chore restored to active state | P1 |
| K-19 | 10.7 | Chore type has color displayed | E2E | 1. View chore calendar 2. Inspect chore type row | Chore type color visible in UI | P2 |

---

### Module L: On-Duty Calendar (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 11.1-11.9
**Playwright file**: `tests/production-qa/module-l-onduty-calendar.spec.js`
**Evidence folder**: `ProductionReady/12-onduty-calendar/`

#### Existing Tests (18) — cover rendering, add/cancel, area selector, duty types, cross-company

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| L-19 | 11.8 | Duty rotation queue page loads | E2E | 1. Login as owner 2. Navigate to Admin/DutyRotation | Rotation management page renders | P1 |
| L-20 | 11.8 | Create duty rotation | E2E | 1. Fill rotation form 2. Submit | Rotation created, appears in list | P1 |
| L-21 | 11.8 | Add user to rotation queue | E2E | 1. Open rotation 2. Add a user 3. Save | User appears in queue | P1 |
| L-22 | 11.8 | Reorder rotation queue | E2E | 1. Open rotation 2. Move a user up/down | Order changes persisted | P2 |
| L-23 | 11.9 | Rank eligibility enforcement blocks ineligible | E2E | 1. Login as owner 2. Enable `FF_ENFORCE_RANK_ELIGIBILITY` via Owner/FeatureFlags page 3. Navigate to Admin/Config and create/verify a duty type that requires officer rank 4. Navigate to OnCall calendar 5. Try to assign a soldier-rank user to the officer duty type | Assignment blocked with error message about rank eligibility | P1 |

---

### Module M: Overview Calendar (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 12.1-12.3
**Playwright file**: `tests/production-qa/module-m-overview.spec.js`
**Evidence folder**: `ProductionReady/13-overview-calendar/`

#### Existing Tests (16) — cover rendering, aggregated data, view modes, just mine, date nav

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| M-17 | 12.2 | Add user day note | E2E | 1. Navigate to Overview 2. Click a day cell 3. Add note text 4. Save | Note appears in the cell | P1 |
| M-18 | 12.2 | Edit existing day note | E2E | 1. Click cell with existing note 2. Modify text 3. Save | Updated note displayed | P2 |
| M-19 | 12.2 | Delete day note | E2E | 1. Click cell with note 2. Delete | Note removed from cell | P2 |

---

### Module N: Time-Off (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 13.1-13.6
**Playwright file**: `tests/production-qa/module-n-timeoff.spec.js`
**Evidence folder**: `ProductionReady/14-timeoff-requests/`

#### Existing Tests (12) — cover create, approve, decline, own requests, auto-unassign

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| N-13 | 13.5 | Vacation approval rules page loads | E2E | 1. Login as owner 2. Navigate to Admin/Settings/ApprovalRules | Approval rules page renders | P1 |
| N-14 | 13.5 | Create approval rule | E2E | 1. Fill rule form 2. Save | Rule created and displayed | P1 |
| N-15 | 13.4 | Cancel own pending request | E2E | 1. Login as employee 2. Go to My/Requests 3. Cancel a pending request | Request status changes to cancelled | P1 |
| N-16 | 13.3 | Delete time-off request as admin | E2E | 1. Login as owner 2. Go to Requests 3. Delete a time-off request | Request removed | P1 |
| N-17 | 13.1 | Half-day request creates correct time range | E2E | 1. Create half-day ("After") request 2. Verify dates | Start at 16:00 to next day 13:00 | P2 |

---

### Module Y: Notifications (EXISTING — extend)
**Covers FEATURE-INVENTORY sections**: 15.1-15.5
**Playwright file**: `tests/production-qa/module-y-notifications.spec.js`
**Evidence folder**: `ProductionReady/25-notifications/`

#### Existing Tests (8) — cover center, badge, mark read, global notifications

#### New Tests to Add

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| Y-09 | 15.1 | Mark all notifications as read | E2E | 1. Have multiple unread notifications 2. Click "mark all read" | All notifications marked read, badge clears | P1 |
| Y-10 | 15.1 | Delete individual notification | E2E | 1. Find a notification 2. Click delete | Notification removed from list | P2 |
| Y-11 | 15.3 | Badge count updates on new notification | E2E | 1. Note badge count 2. Trigger an action that creates notification 3. Check badge | Badge count incremented | P1 |
| Y-12 | 15.5 | Director notification hub shows cross-company | E2E | 1. Login as director 2. Navigate to Director/NotificationHub | Notifications from assigned companies visible | P1 |
| Y-13 | 15.2 | All 20 notification types have localized text | E2E | 1. Trigger actions for each notification type 2. Check notification center | Each notification has localized display text | P2 |

---

### Module AA: Grants & Role Templates (NEW)
**Covers FEATURE-INVENTORY sections**: 4.1-4.6
**Playwright file**: `tests/production-qa/module-aa-grants-roletemplate.spec.js`
**Evidence folder**: `ProductionReady/29-grants-roletemplates/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AA-01 | 4.2 | Role Templates page loads and lists templates | E2E | 1. Login as owner 2. Navigate to `/Owner/Hub/RoleTemplates` | Page renders with table of role templates including Owner, AlhutLead, TextLead, etc. | P1 |
| AA-02 | 4.2 | View role template details | E2E | 1. Click on a role template row | Template details shown: Key, DisplayNameEN, DisplayNameHE, DerivedUserRole, grants list | P1 |
| AA-03 | 4.2 | Create custom role template | E2E | 1. Navigate to `/Owner/Hub/RoleTemplates/Create` 2. Fill Key, DisplayNameEN, DisplayNameHE, DerivedUserRole, ScopeLevel 3. Submit | New template created, appears in list | P1 |
| AA-04 | 4.2 | Edit template metadata | E2E | 1. Navigate to edit page for a custom template 2. Change DisplayNameEN 3. Save | Name updated | P1 |
| AA-05 | 4.2 | Edit template labels | E2E | 1. Navigate to edit page 2. Update labels 3. Save | Labels saved | P2 |
| AA-06 | 4.2 | Add grant to template | E2E | 1. Navigate to edit page 2. Add a grant type 3. Save | Grant appears in template's grant list | P1 |
| AA-07 | 4.2 | Remove grant from template | E2E | 1. Navigate to edit page 2. Remove a grant 3. Save | Grant removed from template | P1 |
| AA-08 | 4.2 | System templates are read-only | E2E | 1. Navigate to Owner template 2. Check for edit controls | Edit buttons disabled or hidden for system templates | P1 |
| AA-09 | 4.3 | Seeded templates have correct grants | E2E | 1. View AlhutLead template grants | AlhutLead has Shift.Assign, Shift.View, etc. matching seed data | P0 |
| AA-10 | 4.4 | Owner Hub Grants page loads | E2E | 1. Navigate to `/Owner/Hub/Grants` | Grants management page renders with user search | P0 |
| AA-11 | 4.4 | Search users in grant management | E2E | 1. Type user email in search 2. Wait for results | User found in search results | P1 |
| AA-12 | 4.4 | View user grants list | E2E | 1. Select a user 2. View their grants | List of grants with scope, CanOwn, CanGive flags shown | P1 |
| AA-13 | 4.4 | Assign individual grant to user | E2E | 1. Select user 2. Click assign grant 3. Select grant type and scope 4. Submit | Grant assigned, appears in user's grant list | P0 |
| AA-14 | 4.4 | Revoke grant from user | E2E | 1. Select user with grants 2. Click revoke on a grant | Grant removed from user's list | P0 |
| AA-15 | 4.4 | Update grant delegation flags | E2E | 1. Select user grant 2. Toggle CanOwn/CanGive | Delegation flags updated | P2 |
| AA-16 | 4.4 | Apply owner godmode grants | E2E | 1. Click apply owner grants button | Owner gets all grants | P1 |
| AA-17 | 4.5 | Organization-level grants page loads | E2E | 1. Navigate to Admin/Organization/Grants | Grants list displayed | P1 |
| AA-18 | 4.5 | Assign grant at organization level | E2E | 1. Navigate to Assign page 2. Select user, grant type, scope 3. Submit | Grant assigned | P1 |
| AA-19 | 4.5 | Revoke grant at organization level | E2E | 1. Select a grant 2. Revoke | Grant revoked | P1 |
| AA-20 | 4.6 | Role assignment page loads | E2E | 1. Navigate to Admin/Organization/Roles | Role assignments listed | P1 |
| AA-21 | 4.6 | Assign role template at org level | E2E | 1. Select user 2. Assign template 3. Submit | Template assigned, user role updated | P1 |
| AA-22 | 4.1 | Grant-based page visibility: employee cannot see Owner nav | E2E | 1. Login as employee 2. Check sidebar | No Owner section visible | P0 |
| AA-23 | 4.1 | Grant scope hierarchy: molecule grant covers company | E2E | 1. Give user molecule-scoped grant 2. Check company-level access | Access granted at company level under that molecule | P1 |
| AA-24 | 4.4 | View grant type usage across system | E2E | 1. Navigate to grant type usage view | Shows count of users with each grant type | P2 |
| AA-25 | 4.1, 17.1 | Tenant isolation for grant assignment | E2E | 1. Login as owner 2. Assign Shift.View grant to a Tzafona company user 3. Logout 4. Login as Hir company user 5. Navigate to /Admin/Organization/Grants | Tzafona user's grant NOT visible to Hir user | P0 |
| AA-26 | 4.2 | Employee denied access to Role Templates page | E2E | 1. Login as test.member@shifty.test 2. Navigate to `/Owner/Hub/RoleTemplates` | Redirected to access denied or 403 | P1 |

**xUnit file**: `ShiftManager.Tests/UnitTests/Services/GrantAuthorizationTests.cs` (NEW — **NOTE**: verify AA-U01 through AA-U07 are not already covered by existing `GrantServiceTests.cs` before creating. Extend the existing file if possible to avoid duplicate test maintenance.)

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AA-U01 | 4.1 | Grant with matching company scope returns true | Unit | Arrange: user with company grant; Act: HasGrantAsync; Assert: true | Grant authorized | P0 |
| AA-U02 | 4.1 | Grant with wrong company scope returns false | Unit | Arrange: user with grant for company A; Act: check for company B | Not authorized | P0 |
| AA-U03 | 4.1 | Area-scoped grant covers child molecules | Unit | Arrange: area-scoped grant; Act: check molecule under that area | Authorized | P0 |
| AA-U04 | 4.1 | Project-scoped grant covers everything under it | Unit | Arrange: project grant; Act: check any company | Authorized | P0 |
| AA-U05 | 4.1 | Self-scoped grant (all null) is skipped | Unit | Arrange: grant with all null scope; Act: HasGrantWithScopeAsync | Returns false (self-scoped skip) | P0 |
| AA-U06 | 4.1 | Auto-grants from RoleTemplate applied on login | Unit | Arrange: user with template; Act: ApplyAutoGrantsAsync | Grants from template assigned to user | P0 |
| AA-U07 | 4.1 | CanOwn delegation allows granting to others | Unit | Arrange: grant with CanOwn=true; Act: check delegation | Can delegate | P1 |

---

### Module AB: Friends & Gamification (NEW)
**Covers FEATURE-INVENTORY sections**: 24.1-24.3, 25.1-25.3
**Playwright file**: `tests/production-qa/module-ab-friends-game.spec.js`
**Evidence folder**: `ProductionReady/30-friends-game/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AB-01 | 24.1 | Friends page loads (feature flag on) | E2E | 1. Enable `FF_FRIENDSHIPS_ENABLED` 2. Login 3. Navigate to `/Friends` | Friends page renders | P2 |
| AB-02 | 24.1 | Send friend request | E2E | 1. Navigate to Friends 2. Search for another user 3. Send request | Request sent, appears in pending list | P2 |
| AB-03 | 24.1 | Accept friend request | E2E | 1. Login as target user 2. Accept pending request | Friendship established | P2 |
| AB-04 | 24.1 | Reject friend request | E2E | 1. Reject a pending request | Request removed | P2 |
| AB-05 | 24.1 | Remove friend | E2E | 1. Remove an existing friend | Friend removed from list | P2 |
| AB-06 | 24.2 | Friends API returns friend IDs | E2E | 1. Login with friends 2. Call `/Api/Friends/Ids` | JSON response with array of friend user IDs | P2 |
| AB-07 | 24.3 | Friends highlighted on calendar | E2E | 1. Login with friends 2. Navigate to shifts calendar | Friend assignments have highlight CSS class | P3 |
| AB-08 | 25.1 | Game activates on Ctrl+Click brand | E2E | 1. Login 2. Ctrl+Click on brand/logo in sidebar | Game modal overlay appears | P3 |
| AB-09 | 25.1 | Game grid renders 6x6 | E2E | 1. Open game 2. Inspect grid | Grid has 36 cells (6x6) | P3 |
| AB-10 | 25.2 | Game API: get configuration | E2E | 1. Fetch `/Api/Game/GetConfiguration` | JSON with grid size, points, milestones | P3 |
| AB-11 | 25.2 | Game API: get leaderboard | E2E | 1. Fetch `/Api/Game/GetLeaderboard` | JSON with leaderboard entries | P3 |
| AB-12 | 25.3 | Leaderboard page renders | E2E | 1. Navigate to `/Game/Leaderboard` | Leaderboard table with scores visible | P3 |

---

### Module AC: Team Calendars (NEW)
**Covers FEATURE-INVENTORY sections**: 34.1
**Playwright file**: `tests/production-qa/module-ac-team-calendars.spec.js`
**Evidence folder**: `ProductionReady/31-team-calendars/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AC-01 | 34.1 | MyTeam page loads | E2E | 1. Login 2. Navigate to `/MyTeam` | Team calendar page renders | P2 |
| AC-02 | 34.1 | Create team calendar | E2E | 1. POST `/api/team-calendars` with name 2. Check response | Calendar created with 201 status | P2 |
| AC-03 | 34.1 | Get my calendars | E2E | 1. GET `/api/team-calendars` | List includes created calendar | P2 |
| AC-04 | 34.1 | Set team members | E2E | 1. PUT `/api/team-calendars/{id}/members` with user IDs | Members set successfully | P2 |
| AC-05 | 34.1 | Get team members | E2E | 1. GET `/api/team-calendars/{id}/members` | Returns member list | P2 |
| AC-06 | 34.1 | Get week view aggregated data | E2E | 1. GET `/api/team-calendars/{id}/week?date=...` | Returns aggregated shift/chore/onduty data for members | P2 |
| AC-07 | 34.1 | Rename team calendar | E2E | 1. PUT `/api/team-calendars/{id}` with new name | Calendar renamed | P2 |
| AC-08 | 34.1 | Delete team calendar | E2E | 1. DELETE `/api/team-calendars/{id}` | Calendar deleted, 204 response | P2 |

---

### Module AD: Admin Pages Extended (NEW)
**Covers FEATURE-INVENTORY sections**: 5.9, 20.1-20.11, 32.1-32.2, 33.1-33.2
**Playwright file**: `tests/production-qa/module-ad-admin-pages.spec.js`
**Evidence folder**: `ProductionReady/32-admin-pages/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AD-01 | 20.1 | Admin dashboard loads | E2E | 1. Login as owner 2. Navigate to `/Admin` | Dashboard with quick stats renders | P1 |
| AD-02 | 20.2 | Analytics page loads with data | E2E | 1. Navigate to `/Admin/Analytics` | Analytics charts/tables rendered | P1 |
| AD-03 | 20.2 | Export analytics CSV | E2E | 1. Click export CSV on analytics page | Download initiated with CSV content | P2 |
| AD-04 | 20.3 | Audit log page loads | E2E | 1. Navigate to `/Admin/AuditLog` | Audit log entries displayed | P1 |
| AD-05 | 20.3 | Audit log filter by action type | E2E | 1. Select filter 2. Apply | Filtered results shown | P2 |
| AD-06 | 20.3 | Export audit log CSV | E2E | 1. Click export on audit log | CSV downloaded | P2 |
| AD-07 | 20.4 | Announcements page loads | E2E | 1. Navigate to `/Admin/Announcements` | Announcements list rendered | P2 |
| AD-08 | 20.4 | Create announcement | E2E | 1. Fill announcement form 2. Submit | Announcement created, visible in list | P2 |
| AD-09 | 20.4 | Toggle announcement active/inactive | E2E | 1. Toggle an announcement | Status changes | P2 |
| AD-10 | 20.4 | Delete announcement | E2E | 1. Delete an announcement | Announcement removed | P2 |
| AD-11 | 20.5 | App config page loads | E2E | 1. Navigate to `/Admin/Config` | Config form with RestHours, WeeklyHoursCap fields | P1 |
| AD-12 | 20.5 | Save app config | E2E | 1. Change RestHours value 2. Save | Config saved, page reflects new value | P1 |
| AD-13 | 20.5 | Add on-duty type | E2E | 1. Fill on-duty type form 2. Add | Type added to list | P1 |
| AD-14 | 20.5 | Delete on-duty type | E2E | 1. Delete an on-duty type | Type removed | P1 |
| AD-15 | 20.6 | Directors page loads | E2E | 1. Navigate to `/Admin/Directors` | Director assignments shown | P1 |
| AD-16 | 20.6 | Assign director to company | E2E | 1. Select user and company 2. Assign | Director assigned | P1 |
| AD-17 | 20.6 | Revoke director access | E2E | 1. Revoke a director assignment | Assignment removed | P1 |
| AD-17b | 20.6 | Reassign director to different company | E2E | 1. Select existing director assignment 2. Use OnPostReassignAsync to change company | Director reassigned to new company | P1 |
| AD-18 | 20.7 | Settings page loads | E2E | 1. Navigate to `/Admin/Settings` | Settings form rendered | P2 |
| AD-19 | 20.8 | Setup tasks page loads | E2E | 1. Enable `FF_SETUP_TASKS_ENABLED` 2. Navigate to Admin/SetupTasks | Task list rendered | P2 |
| AD-20 | 20.8 | Complete setup task | E2E | 1. Click complete on a task | Task marked done | P2 |
| AD-21 | 20.10 | Job types page loads | E2E | 1. Navigate to Admin/Organization/JobTypes | Job types listed | P1 |
| AD-22 | 20.10 | Create job type | E2E | 1. Fill form 2. Create | Job type added | P1 |
| AD-23 | 20.10 | Toggle job type active | E2E | 1. Toggle a job type | Status changed | P2 |
| AD-24 | 20.11 | Shift groupings page loads | E2E | 1. Navigate to Admin/Organization/ShiftGroupings | Groupings listed | P2 |
| AD-25 | 20.11 | Create shift grouping | E2E | 1. Fill form 2. Create | Grouping added | P2 |
| AD-26 | 32.1 | Diagnostic page loads | E2E | 1. Navigate to `/Diagnostic` | System info, DB stats, user context shown | P3 |
| AD-27 | 32.2 | Griffin diagnostic page loads | E2E | 1. Navigate to `/GriffinDiagnostic` | Griffin config and connection status shown | P3 |
| AD-28 | 33.1 | Seeded job types exist (Alhut, Text, BR, Hakam) | E2E | 1. Navigate to job types page | All 4 core job types present | P1 |
| AD-29 | 5.9 | Hierarchy settings cascade from area to molecule | E2E | 1. Set a setting at area level 2. Check molecule setting | Molecule inherits area setting | P2 |

---

### Module AE: Security Headers & Middleware (NEW)
**Covers FEATURE-INVENTORY sections**: 31.1-31.8, 47.1-47.3
**Playwright file**: `tests/production-qa/module-ae-security-headers.spec.js`
**Evidence folder**: `ProductionReady/33-security/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AE-01 | 31.2 | CSP header present on responses | E2E | 1. Login 2. Navigate to any page 3. Check response headers | `Content-Security-Policy` header present with `default-src 'self'` | P0 |
| AE-02 | 31.3 | X-Frame-Options is DENY | E2E | 1. Check response headers | `X-Frame-Options: DENY` present | P0 |
| AE-03 | 31.3 | X-Content-Type-Options is nosniff | E2E | 1. Check response headers | `X-Content-Type-Options: nosniff` | P0 |
| AE-04 | 31.3 | Server header removed | E2E | 1. Check response headers | No `Server` or `X-Powered-By` header | P1 |
| AE-05 | 31.3 | Referrer-Policy set | E2E | 1. Check response headers | `Referrer-Policy: strict-origin-when-cross-origin` | P1 |
| AE-06 | 31.1 | CSRF token present in POST forms | E2E | 1. Login 2. Navigate to any page with a form | `__RequestVerificationToken` hidden input present | P0 |
| AE-07 | 31.1 | POST without CSRF token rejected | E2E | 1. Make a raw POST request without token | 400 or 403 response | P0 |
| AE-08 | 31.4 | UI rate limiting returns 429 on excess | E2E | 1. Make 50+ rapid requests to a rate-limited endpoint | 429 status returned | P1 |
| AE-09 | 47.1 | Correlation ID in response headers | E2E | 1. Make a request 2. Check response headers | `X-Correlation-Id` header present with GUID value | P2 |
| AE-10 | 31.2 | CSP allows WebSocket connections | E2E | 1. Check CSP header connect-src | Contains `ws: wss:` for SignalR | P1 |
| AE-11 | 31.2 | CSP frame-ancestors is none | E2E | 1. Check CSP header | `frame-ancestors 'none'` preventing clickjacking | P1 |
| AE-12 | 31.1 | CSRF rejection on new module POST handlers | E2E | 1. Login as owner, capture session cookie 2. Make raw fetch POST to `/Owner/Hub/Grants` (OnPostAssignGrantAsync) without `__RequestVerificationToken` 3. Assert response status | 400 or 403 returned (POST without CSRF token rejected) | P0 |

**xUnit file**: `ShiftManager.Tests/UnitTests/Security/PiiMaskerTests.cs` (NEW)

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AE-U01 | 31.5 | PiiMasker masks email addresses | Unit | Act: mask "user@example.com" | Returns "u***@e***.com" or similar masked format | P1 |
| AE-U02 | 31.5 | PiiMasker handles null input | Unit | Act: mask null | Returns null without exception | P2 |
| AE-U03 | 31.7 | EncryptionService round-trips data | Unit | Arrange: plaintext; Act: encrypt then decrypt | Decrypted matches original | P1 |

---

### Module AF: SignalR Real-Time (NEW)
**Covers FEATURE-INVENTORY sections**: 30.1-30.3, 36.1-36.3
**Playwright file**: `tests/production-qa/module-af-signalr-realtime.spec.js`
**Evidence folder**: `ProductionReady/34-signalr/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AF-01 | 30.1 | SignalR connection established on calendar page | E2E | 1. Login 2. Navigate to Calendar/Shifts 3. Check console for SignalR messages | No WebSocket errors in console; connection state is Connected | P1 |
| AF-02 | 30.1 | Assignment change pushed to second browser context | E2E | 1. Open two separate `browser.newContext()` sessions (NOT tabs — use separate auth sessions like Module R) 2. Both navigate to Calendar/Shifts for same molecule/jobType 3. Make assignment in context A 4. In context B, wait for the new assignment name to appear in the calendar cell DOM | Context B's calendar cell shows the new assignment name without manual refresh | P0 |
| AF-03 | 30.1 | Chore change pushed via SignalR | E2E | 1. Open Chores calendar in two contexts 2. Add chore in context A | Context B receives chore update | P1 |
| AF-04 | 30.1 | On-duty change pushed via SignalR | E2E | 1. Open OnCall calendar in two contexts 2. Add on-duty in context A | Context B receives update | P1 |
| AF-05 | 30.2 | SignalR reconnects after disconnect | E2E | 1. Add `test.setTimeout(60000)` 2. Open calendar 3. Simulate disconnect via `page.route('**/calendarHub**', r => r.abort())` then restore 4. Wait for reconnection (backoff up to ~47s) | Connection re-established, no persistent error in console | P1 |
| AF-06 | 30.2 | Fallback polling activates when SignalR unavailable | E2E | 1. Add `test.setTimeout(120000)` 2. Block WebSocket via `page.route()` 3. Use `page.waitForRequest()` to detect polling fetch calls instead of waiting the full 60s interval | Polling fetch request intercepted (proves fallback activated) | P2 |
| AF-07 | 30.3 | Group access validation prevents cross-tenant join | E2E | 1. Open two `browser.newContext()` sessions: context A (company A user), context B (company B user) 2. Both navigate to Calendar/Shifts 3. Make an assignment in context B's company 4. In context A, use a 10-second negative assertion (`expect(cell).not.toContainText(...)`) to verify the update does NOT appear | Company A's calendar is NOT updated by company B's changes (negative assertion with timeout) | P0 |
| AF-08 | 36.1 | Conflict checker detects rest violation | E2E | 1. Assign user to night shift 2. Try to assign same user to morning shift next day (within RestHours) | Warning or error about rest violation | P1 |
| AF-09 | 36.2 | Busy user status shown in roster | E2E | 1. Open roster dock 2. User has vacation on a date | User shown as busy/unavailable for that date | P1 |

**xUnit files** (extend existing):

| ID | Feature Ref | Test Name | Type | File | Priority |
|----|------------|-----------|------|------|----------|
| AF-U01 | 36.1 | ConflictChecker detects overlap within RestHours | Unit | `ConflictCheckerTests.cs` | P0 |
| AF-U02 | 36.1 | ConflictChecker allows if gap >= RestHours | Unit | `ConflictCheckerTests.cs` | P0 |
| AF-U03 | 36.2 | BusyUserService returns busy for vacation day | Unit | `BusyUserServiceTests.cs` | P1 |
| AF-U04 | 36.2 | BusyUserService returns busy for assigned shift | Unit | `BusyUserServiceTests.cs` | P1 |
| AF-U05 | 36.3 | ConcurrencyService detects simultaneous edits | Unit | Existing `ConcurrencyServiceTests.cs` | P0 |

---

### Module AG: Print & Export (NEW)
**Covers FEATURE-INVENTORY sections**: 27.1-27.3
**Playwright file**: `tests/production-qa/module-ag-print-export.spec.js`
**Evidence folder**: `ProductionReady/35-print-export/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AG-01 | 27.1 | Print button visible on shift calendar | E2E | 1. Navigate to Calendar/Shifts | Print button visible in toolbar | P2 |
| AG-02 | 27.1 | Print stylesheet hides navigation | E2E | 1. Emulate print media 2. Check nav visibility | Navigation elements hidden in print layout | P2 |
| AG-03 | 27.1 | Print button visible on chore calendar | E2E | 1. Navigate to Calendar/Chores | Print button present | P2 |
| AG-04 | 27.1 | Print button visible on on-duty calendar | E2E | 1. Navigate to Calendar/OnCall | Print button present | P2 |
| AG-05 | 27.1 | Print button visible on overview | E2E | 1. Navigate to Calendar/Overview | Print button present | P2 |
| AG-06 | 27.2 | Schedule export API returns data | E2E | 1. POST to `/Api/ScheduleExport` with date range parameters | JSON response with schedule data | P2 |
| AG-07 | 27.3 | User CSV export works | E2E | 1. Navigate to Admin/Users 2. Click export | CSV file downloaded | P2 |
| AG-08 | 27.3 | Analytics CSV export works | E2E | 1. Navigate to Admin/Analytics 2. Click export | CSV file downloaded | P2 |
| AG-09 | 27.3 | Audit log CSV export works | E2E | 1. Navigate to Admin/AuditLog 2. Click export | CSV file downloaded | P2 |
| AG-10 | 27.3 | Email log CSV export works | E2E | 1. Navigate to Owner/EmailConfig 2. Click export logs | CSV downloaded | P2 |

---

### Module AH: Keyboard Navigation & Accessibility (NEW)
**Covers FEATURE-INVENTORY sections**: 28.1-28.5, 45.1
**Playwright file**: `tests/production-qa/module-ah-keyboard-a11y.spec.js`
**Evidence folder**: `ProductionReady/36-keyboard-a11y/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AH-01 | 28.1 | ESC closes open dropdown | E2E | 1. Open a dropdown menu 2. Press ESC | Dropdown closes | P2 |
| AH-02 | 28.1 | Arrow keys navigate dropdown options | E2E | 1. Focus a dropdown 2. Press Down arrow | Focus moves to next option | P2 |
| AH-03 | 28.1 | Enter activates focused option | E2E | 1. Focus a dropdown option 2. Press Enter | Option selected | P2 |
| AH-04 | 28.2 | Modal traps focus with Tab | E2E | 1. Open a modal 2. Tab through focusable elements | Focus stays within modal boundary | P2 |
| AH-05 | 28.2 | Modal closes on ESC | E2E | 1. Open a modal 2. Press ESC | Modal closes, focus returns to trigger | P2 |
| AH-06 | 28.3 | ARIA landmarks present on main pages | E2E | 1. Navigate to home page 2. Check for landmark roles | `main`, `navigation`, `banner` landmarks present | P2 |
| AH-07 | 28.4 | Skip navigation link works | E2E | 1. Navigate to any page 2. Tab once from page load | Skip-to-content link receives focus | P2 |
| AH-08 | 28.4 | Skip link targets main content | E2E | 1. Activate skip link 2. Check focus position | Focus moves to main content area | P2 |
| AH-09 | 28.5 | Reduced motion respects system preference | E2E | 1. Set `prefers-reduced-motion: reduce` 2. Check animation CSS | Animations disabled or reduced | P3 |
| AH-10 | 45.1 | Form validation shows error on invalid input | E2E | 1. Navigate to a form page 2. Submit empty required field | Validation error with accessible error message | P2 |
| AH-11 | 45.1 | Form validation highlights error field | E2E | 1. Submit invalid form | Invalid field has error styling class | P2 |
| AH-12 | 28.1 | Space toggles checkbox | E2E | 1. Focus a checkbox 2. Press Space | Checkbox state toggles | P2 |

---

### Module AI: Mobile & Responsive (NEW)
**Covers FEATURE-INVENTORY sections**: 49.1-49.3, 9.7
**Playwright file**: `tests/production-qa/module-ai-mobile-responsive.spec.js`
**Evidence folder**: `ProductionReady/37-mobile/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AI-01 | 49.1 | Hamburger menu visible at mobile width | E2E | 1. Set viewport to 375x812 2. Navigate to home | Hamburger menu button visible, sidebar hidden | P2 |
| AI-02 | 49.1 | Hamburger menu opens sidebar | E2E | 1. At mobile width 2. Click hamburger | Sidebar opens as overlay | P2 |
| AI-03 | 49.1 | Mobile menu closes on navigation | E2E | 1. Open mobile menu 2. Click a nav link | Menu closes, page navigates | P2 |
| AI-04 | 49.2 | Calendar table scrolls horizontally at mobile width | E2E | 1. Set viewport to 375x812 2. Navigate to Calendar/Shifts | Table has horizontal scroll, not cut off | P2 |
| AI-05 | 49.2 | Login form usable at mobile width | E2E | 1. Set viewport to 375x812 2. Navigate to login 3. Login | Login works at mobile size | P2 |
| AI-06 | 49.3 | PWA manifest link in HTML | E2E | 1. Check `<link rel="manifest">` in HTML head | Manifest link present pointing to `site.webmanifest` | P3 |
| AI-07 | 49.3 | Apple touch icon present | E2E | 1. Check `<link rel="apple-touch-icon">` | Apple touch icon link present | P3 |
| AI-08 | 9.7 | Calendar bottom sheet appears on mobile | E2E | 1. Set viewport to 375x812 2. Navigate to Calendar/Shifts 3. Tap a cell | Bottom sheet slides up with options | P2 |
| AI-09 | 49.2 | No horizontal overflow on main pages at tablet width | E2E | 1. Set viewport to 768x1024 2. Navigate to multiple pages | No horizontal scrollbar on body | P2 |

---

### Module AJ: Performance, Loading & UI Components (NEW)
**Covers FEATURE-INVENTORY sections**: 38.1, 41.1-41.6, 43.1, 44.1, 48.1-48.6, 51.1-51.3
**Playwright file**: `tests/production-qa/module-aj-performance-loading.spec.js`
**Evidence folder**: `ProductionReady/38-performance/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AJ-01 | 38.1 | Skeleton loading shown while calendar loads | E2E | 1. Navigate to Calendar/Shifts 2. Observe initial render | Skeleton placeholder visible before data loads | P3 |
| AJ-02 | 41.3 | Responses are gzip compressed | E2E | 1. Make request 2. Check `Content-Encoding` header | `gzip` or `br` encoding present | P2 |
| AJ-03 | 41.5 | Static assets have cache headers | E2E | 1. Request a JS/CSS file 2. Check `Cache-Control` header | Cache-Control header present with max-age | P2 |
| AJ-04 | 43.1 | Calendar radar mode toggleable | E2E | 1. Navigate to Calendar/Shifts 2. Activate radar mode | Radar visual indicator appears | P3 |
| AJ-05 | 44.1 | Schedule page loads | E2E | 1. Navigate to `/Schedule` | Schedule view renders without error | P3 |
| AJ-06 | 48.1 | Breadcrumb renders on nested pages | E2E | 1. Navigate to Admin/Organization/JobTypes | Breadcrumb shows Admin > Organization > Job Types | P2 |
| AJ-07 | 48.2 | Pagination component renders on long lists | E2E | 1. Navigate to Admin/Users with many users | Pagination controls visible | P2 |
| AJ-08 | 48.5 | "Just Mine" toggle works on shifts calendar | E2E | 1. Navigate to Calendar/Shifts 2. Toggle "Just Mine" | Calendar filters to current user's assignments | P1 |
| AJ-09 | 48.5 | "Just Mine" toggle works on chores calendar | E2E | 1. Navigate to Calendar/Chores 2. Toggle "Just Mine" | Calendar filters to current user's chores | P1 |
| AJ-10 | 51.1 | API client retries on rate limit | E2E | 1. Trigger rapid API calls that hit rate limit 2. Observe retry behavior | Client retries with backoff after 429 | P3 |
| AJ-11 | 41.6 | Calendar data loads from cache on second visit | E2E | 1. Visit Calendar/Shifts 2. Navigate away 3. Navigate back | Second load is faster (data cached in memory) | P3 |

---

### Module AK: Widgets & Telemetry (NEW)
**Covers FEATURE-INVENTORY sections**: 37.1, 40.1-40.3
**Playwright file**: `tests/production-qa/module-ak-widgets-telemetry.spec.js`
**Evidence folder**: `ProductionReady/39-widgets-telemetry/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AK-01 | 40.1 | Dashboard widgets render when enabled | E2E | 1. Enable `FF_WIDGETS_ENABLED` 2. Navigate to dashboard | Widget components visible | P2 |
| AK-02 | 40.1 | Widget collapse/expand persists | E2E | 1. Collapse a widget 2. Navigate away 3. Return | Widget still collapsed (localStorage) | P3 |
| AK-03 | 40.2 | System alerts shown when active | E2E | 1. Create an active announcement 2. Navigate to dashboard | System alert banner visible | P2 |
| AK-04 | 40.3 | Decision ribbon shows pending items | E2E | 1. Login as manager with pending requests 2. Check dashboard | Decision ribbon with pending count visible | P2 |
| AK-05 | 37.1 | Telemetry endpoint accepts events | E2E | 1. POST to `/Api/Telemetry` with event data | 200 response | P3 |
| AK-06 | 37.1 | Telemetry dashboard loads for owner | E2E | 1. Navigate to `/Owner/Telemetry` | Telemetry data displayed in tabs | P3 |
| AK-07 | 37.1 | Telemetry cleanup removes old data | E2E | 1. Click cleanup button on telemetry page | Old data purged, confirmation shown | P3 |

---

### Module AL: Coverage Sweep (NEW)
**Covers**: All remaining gaps — verifies every page loads without 500 errors
**Playwright file**: `tests/production-qa/module-al-coverage-sweep.spec.js`
**Evidence folder**: `ProductionReady/40-coverage-sweep/`

#### Test Cases

| ID | Feature Ref | Test Name | Type | Steps | Expected Result | Priority |
|----|------------|-----------|------|-------|-----------------|----------|
| AL-01 | 19.11 | Data lifecycle page loads | E2E | 1. Navigate to `/Owner/DataLifecycle` | Page renders without error | P2 |
| AL-02 | 19.12 | Area config page loads | E2E | 1. Navigate to `/Owner/AreaConfig` | Page renders | P2 |
| AL-03 | 19.13 | Game config page loads | E2E | 1. Navigate to `/Owner/GameConfig` | Page renders | P3 |
| AL-04 | 19.14 | Permissions viewer page loads | E2E | 1. Navigate to `/Owner/Permissions` | Permissions list displayed | P2 |
| AL-05 | 19.15 | Telemetry page loads | E2E | 1. Navigate to `/Owner/Telemetry` | Page renders with tabs | P3 |
| AL-06 | 19.16 | Audit search page loads | E2E | 1. Navigate to `/Owner/Hub/AuditSearch` | Search form rendered | P2 |
| AL-07 | 19.17 | Export user data page loads | E2E | 1. Navigate to `/Owner/Hub/ExportUserData` | Page renders | P2 |
| AL-08 | 19.18 | Seed data page loads | E2E | 1. Navigate to `/Owner/Hub/SeedData` | Seed buttons visible | P3 |
| AL-09 | 19.9 | Email templates page loads | E2E | 1. Navigate to `/Owner/EmailTemplates` | Template list displayed | P2 |
| AL-10 | 19.8 | Email config page loads | E2E | 1. Navigate to `/Owner/EmailConfig` | Config form rendered | P2 |
| AL-11 | 23.2 | My Profile page loads | E2E | 1. Navigate to `/My/Profile` | Profile form rendered | P1 |
| AL-12 | 23.3 | My Settings page loads | E2E | 1. Navigate to `/My/Settings` | Settings form rendered | P1 |
| AL-13 | 23.4 | Help page loads | E2E | 1. Navigate to `/My/Help` | Help content displayed | P2 |
| AL-14 | 23.5 | Onboarding page loads | E2E | 1. Navigate to `/My/Onboarding` | Onboarding wizard displayed | P2 |
| AL-15 | 21.3 | Feedback page loads | E2E | 1. Navigate to `/Public/Feedback` | Feedback form rendered | P2 |
| AL-16 | 21.3 | Submit feedback | E2E | 1. Fill feedback form 2. Submit | Feedback submitted, notification created | P2 |
| AL-17 | 9.14 | Calendar index redirects | E2E | 1. Navigate to `/Calendar` | Redirected to appropriate calendar view | P2 |
| AL-18 | 35.1 | Tech shift eligible API responds | E2E | 1. Call `/Api/TechShift/Eligible` with params | JSON response with eligible users | P2 |
| AL-19 | 16.5 | Language edit mode toggles | E2E | 1. Login as owner 2. Enable language edit mode | Editable translation overlays appear | P2 |
| AL-20 | 16.3 | Company language override works | E2E | 1. Set a company override 2. Login as user in that company | Overridden translation visible | P2 |
| AL-21 | 19.4 | Database backup CRUD | E2E | 1. Create backup 2. Verify in list 3. Download 4. Delete | Full backup lifecycle works | P1 |
| AL-22 | 19.5 | System health page shows metrics | E2E | 1. Navigate to `/Owner/SystemHealth` | DB size, user count, disk space shown | P1 |
| AL-23 | 19.5 | Health check endpoint responds | E2E | 1. GET `/health` | 200 OK response | P1 |
| AL-24 | 19.5 | Readiness check endpoint responds | E2E | 1. GET `/ready` | 200 OK response | P1 |
| AL-25 | 18.7 | Version endpoint returns info | E2E | 1. GET `/api/v1/version` | JSON with version info | P2 |

---

## New xUnit Test Specifications

### ConflictCheckerTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/ConflictCheckerTests.cs`
**Covers**: 36.1

| ID | Test Name | Priority |
|----|-----------|----------|
| CC-01 | Detects rest violation between consecutive shifts | P0 |
| CC-02 | No violation when gap exceeds RestHours | P0 |
| CC-03 | Detects double-booking on same date and time | P0 |
| CC-04 | No conflict for different dates | P1 |
| CC-05 | Handles overnight shifts crossing midnight | P1 |

### BusyUserServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/BusyUserServiceTests.cs`
**Covers**: 36.2

| ID | Test Name | Priority |
|----|-----------|----------|
| BU-01 | User with approved vacation is busy | P1 |
| BU-02 | User with existing shift assignment is busy | P1 |
| BU-03 | User with on-duty is busy | P1 |
| BU-04 | User with no assignments is available | P1 |
| BU-05 | Pending vacation does not make user busy | P2 |

### VacationApprovalServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/VacationApprovalServiceTests.cs`
**Covers**: 13.5

| ID | Test Name | Priority |
|----|-----------|----------|
| VA-01 | Auto-approve rule approves matching request | P1 |
| VA-02 | Multi-level approval routes correctly | P1 |
| VA-03 | Approval auto-unassigns affected shifts | P0 |
| VA-04 | Rule with no matching criteria falls through | P2 |

### DutyRotationServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/DutyRotationServiceTests.cs`
**Covers**: 11.8

| ID | Test Name | Priority |
|----|-----------|----------|
| DR-01 | Rotation follows queue order | P1 |
| DR-02 | Rotation skips user on vacation | P1 |
| DR-03 | Queue wraps around after last user | P1 |
| DR-04 | Empty queue returns no assignment | P2 |

### TechShiftServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/TechShiftServiceTests.cs`
**Covers**: 35.1-35.2

| ID | Test Name | Priority |
|----|-----------|----------|
| TS-01 | Tech shift eligibility scoped by department | P2 |
| TS-02 | Non-tech molecule uses company scoping | P2 |
| TS-03 | User in wrong department not eligible | P2 |

### TeamCalendarServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/TeamCalendarServiceTests.cs`
**Covers**: 34.1

| ID | Test Name | Priority |
|----|-----------|----------|
| TC-01 | Create team calendar returns ID | P2 |
| TC-02 | Set members persists member IDs | P2 |
| TC-03 | Week view aggregates shifts, chores, on-duty | P2 |
| TC-04 | Delete removes calendar and members | P2 |

### CompanyLocalizationServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/CompanyLocalizationServiceTests.cs`
**Covers**: 16.3

| ID | Test Name | Priority |
|----|-----------|----------|
| CL-01 | Company override returns overridden value | P2 |
| CL-02 | No override returns default value | P2 |
| CL-03 | Override for different company not applied | P2 |

### ArchiveServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/ArchiveServiceTests.cs`
**Covers**: 19.11

| ID | Test Name | Priority |
|----|-----------|----------|
| AR-01 | Preview returns affected record counts | P2 |
| AR-02 | Archive creates file with correct data | P2 |
| AR-03 | Purge removes records older than threshold | P2 |

### EmailTemplateServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/EmailTemplateServiceTests.cs`
**Covers**: 19.9

| ID | Test Name | Priority |
|----|-----------|----------|
| ET-01 | Get template returns default when no custom | P2 |
| ET-02 | Save template persists customization | P2 |
| ET-03 | Reset template restores default | P2 |
| ET-04 | Template builder replaces placeholders | P2 |

### FeatureFlagServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/FeatureFlagServiceTests.cs`
**Covers**: 19.6

| ID | Test Name | Priority |
|----|-----------|----------|
| FF-01 | IsEnabled returns true for enabled flag | P1 |
| FF-02 | IsEnabled returns false for disabled flag | P1 |
| FF-03 | SetFlag updates flag value | P1 |
| FF-04 | Unknown flag returns false | P1 |

### StartupSafetyTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Startup/StartupSafetyTests.cs`
**Covers**: 50.1-50.10

| ID | Test Name | Priority |
|----|-----------|----------|
| SS-01 | Missing connection string throws at startup | P1 |
| SS-02 | Missing owner email throws at startup | P1 |
| SS-03 | Network share path triggers warning | P2 |
| SS-04 | Non-Israel timezone triggers warning | P2 |
| SS-05 | Default credentials warning in production mode | P2 |
| SS-06 | Missing HMAC secret in production mode throws at startup | P1 |
| SS-07 | Data protection key check runs without error | P2 |
| SS-08 | Pre-migration backup created before schema changes | P2 |
| SS-09 | SQLite WAL mode enabled after init | P2 |
| SS-10 | Feature flag cache warmed on startup | P2 |

### RateLimitingServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/RateLimitingServiceTests.cs`
**Covers**: 1.3, 31.4

| ID | Test Name | Priority |
|----|-----------|----------|
| RL-01 | Within limit returns allowed | P1 |
| RL-02 | Exceeding limit returns blocked | P1 |
| RL-03 | Counter resets after window | P1 |
| RL-04 | Different keys tracked independently | P1 |

### NotificationServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/NotificationServiceTests.cs`
**Covers**: 15.1-15.4

| ID | Test Name | Priority |
|----|-----------|----------|
| NS-01 | Create notification persists to DB | P1 |
| NS-02 | Mark as read updates IsRead flag | P1 |
| NS-03 | Mark all as read updates all for user | P1 |
| NS-04 | Delete notification removes from DB | P1 |
| NS-05 | Unread count returns correct number | P1 |
| NS-06 | Notification scoped to target user only | P1 |

### ImportServiceTests.cs (NEW)
**File**: `ShiftManager.Tests/UnitTests/Services/ImportServiceTests.cs`
**Covers**: 3.10, 19.11

| ID | Test Name | Priority |
|----|-----------|----------|
| IS-01 | Bulk import creates users from CSV | P2 |
| IS-02 | Import skips duplicate emails | P2 |
| IS-03 | Import maps createdBy via email lookup | P2 |
| IS-04 | Invalid CSV format returns error | P2 |

---

## Cross-Cutting Test Matrix

These tests apply across multiple modules and should be verified for each applicable feature.

### Hebrew/English Localization (applies to all UI pages)

Every page tested in Module X should be verified in BOTH languages. The following pages are critical:

| Page | English Test | Hebrew Test | RTL Verified |
|------|-------------|-------------|-------------|
| Login | X-01 | X-02 | X-03 |
| Calendar/Shifts | X-04 | X-05 | X-06 |
| Calendar/Chores | X-04 | X-05 | X-06 |
| Calendar/OnCall | X-04 | X-05 | X-06 |
| Calendar/Overview | X-04 | X-05 | X-06 |
| Admin/Users | X-07 | X-08 | X-09 |
| My/Profile | X-07 | X-08 | X-09 |
| Requests | X-07 | X-08 | X-09 |
| Owner/Blueprints | X-10 | X-11 | X-12 |
| Owner/Hub/RoleTemplates | X-13 | X-14 | X-15 |
| Admin/Analytics | X-13 | X-14 | X-15 |
| Admin/AuditLog | X-13 | X-14 | X-15 |
| Admin/Announcements | X-13 | X-14 | X-15 |
| Admin/Directors | X-13 | X-14 | X-15 |
| My/Profile | X-13 | X-14 | X-15 |
| My/Settings | X-13 | X-14 | X-15 |
| Public/Feedback | X-13 | X-14 | X-15 |

**Note**: X-13 through X-15 are new tests to add in Module X extension, covering all new pages from modules AA–AL.

### Role-Based Access Control (applies to every protected page)

For each page/action, verify access for all 6 roles:

| Page/Action | Owner | Director | Manager | Assigner | Employee | Trainee |
|-------------|-------|----------|---------|----------|----------|---------|
| Calendar/Shifts (view) | Yes | Yes | Yes | Yes | Yes (read-only) | Yes (read-only) |
| Calendar/Shifts (assign) | Yes | No | Yes | Yes | No | No |
| Admin/Users | Yes | No | Depends | No | No | No |
| Owner/* pages | Yes | No | No | No | No | No |
| Director/* pages | Yes | Yes | No | No | No | No |
| Requests/approve | Yes | No | Yes | No | No | No |
| My/* pages | Yes | Yes | Yes | Yes | Yes | Yes |
| REST API endpoints | With API key + grant | With API key + grant | With API key + grant | No | No | No |

This is tested primarily in Module Q (33 existing tests) with additional checks in Module AA.

**New pages requiring RBAC verification (extend Module Q or add per-module negative tests):**

| Page/Action | Owner | Director | Manager | Assigner | Employee | Trainee |
|-------------|-------|----------|---------|----------|----------|---------|
| `/Owner/Hub/RoleTemplates` | Yes | No | No | No | No | No |
| `/Admin/Analytics` | Yes | Yes | Depends | No | No | No |
| `/Admin/Directors` (assign) | Yes | No | No | No | No | No |
| `/Admin/Announcements` (create) | Yes | No | Yes | No | No | No |
| `/Api/team-calendars` | Yes | Yes | Yes | Yes | Yes | Yes |

### Dark Mode (applies to all pages)

Every page should render without contrast issues in both light and dark themes. The UI-Sweep module covers this with 17 tests. Critical pages to verify:

- Calendar views (table backgrounds, cell text, header contrast)
- Forms (input fields, labels, buttons)
- Modals (overlay, content, buttons)
- Navigation (sidebar, user menu)

---

## Coverage Matrix

This matrix maps every FEATURE-INVENTORY section to its test cases.

| Inventory Section | Subsections | Playwright Module(s) | Playwright Test IDs | xUnit File(s) | xUnit Test IDs | Coverage Status |
|-------------------|-------------|---------------------|---------------------|---------------|----------------|-----------------|
| **1. Auth & Sessions** | 1.1-1.9 | B | B-01 to B-22 | RateLimitingServiceTests | RL-01 to RL-04 | GOOD (extend B) |
| **2. Signup** | 2.1-2.2 | C | C-01 to C-19 | — | — | GOOD (extend C) |
| **3. User Management** | 3.1-3.11 | D | D-01 to D-26 | ImportServiceTests | IS-01 to IS-04 | GOOD (extend D) |
| **4. Authorization & Grants** | 4.1-4.6 | AA | AA-01 to AA-24 | GrantServiceTests (existing), GrantAuthTests (new) | AA-U01 to AA-U07 | NEW MODULE AA |
| **5. Organization Hierarchy** | 5.1-5.9 | V, AD | V-01 to V-12, AD-29 | HierarchySettingsTests (existing) | — | GOOD (extend AD) |
| **6. Context Switcher** | 6.1-6.3 | E | E-01 to E-12 | — | — | GOOD (extend E) |
| **7. Blueprints** | 7.1-7.8 | F | F-01 to F-19 | — | — | GOOD (extend F) |
| **8. Programs** | 8.1-8.6 | G, H | G-01 to G-20, H-01 to H-06 | ProgramManagementAuthTests (existing) | — | COMPLETE |
| **9. Shift Calendar** | 9.1-9.15 | I, J, T | I-01 to I-14, J-01 to J-39, T-01 | CalendarDateValidationTests (existing), ShiftAssignmentServiceTests (existing — eligibility only) | — | GOOD (extend J) |
| **10. Chore Calendar** | 10.1-10.7 | K, T | K-01 to K-19, T-02 | — | — | GOOD (extend K) |
| **11. On-Duty Calendar** | 11.1-11.9 | L, T | L-01 to L-23, T-03 | OnDutyServiceTests (existing), DutyRotationTests (new), MilitaryRankExtensionsTests (existing, 11.9), MilitaryRankIntegrationTests (existing, 11.9) | DR-01 to DR-04 | GOOD (extend L) |
| **12. Overview Calendar** | 12.1-12.3 | M, T | M-01 to M-19, T-04 | — | — | GOOD (extend M) |
| **13. Time-Off** | 13.1-13.6 | N | N-01 to N-17 | VacationApprovalTests (new) | VA-01 to VA-04 | GOOD (extend N) |
| **14. Swaps** | 14.1-14.3 | O | O-01 to O-10 | — | — | COMPLETE |
| **15. Notifications** | 15.1-15.5 | Y | Y-01 to Y-13 | NotificationServiceTests (new) | NS-01 to NS-06 | GOOD (extend Y) |
| **16. Localization** | 16.1-16.6 | X, AL | X-01 to X-12, AL-19/20 | CompanyLocalizationTests (new) | CL-01 to CL-03 | GOOD (extend X, AL) |
| **17. Tenant Isolation** | 17.1-17.3 | P | P-01 to P-14 | DirectorCrossTenantTests (existing), ScopeFilterTests (existing) | — | COMPLETE |
| **18. REST API** | 18.1-18.8 | S, AL | S-01 to S-21, AL-25 | — | — | GOOD (extend S, AL) |
| **19. Owner/Admin Tools** | 19.1-19.18 | U, AL | U-01 to U-18, AL-01 to AL-10, AL-21 to AL-24 | ArchiveServiceTests, EmailTemplateTests, FeatureFlagTests (new) | AR/ET/FF tests | GOOD (extend U, new AL) |
| **20. Admin Pages** | 20.1-20.11 | V, AD | V-01 to V-12, AD-01 to AD-28 | AnnouncementServiceTests (existing) | — | NEW MODULE AD |
| **21. Public Pages** | 21.1-21.3 | W, AL | W-01 to W-14, AL-15/16 | — | — | GOOD (extend W, AL) |
| **22. Director Features** | 22.1-22.3 | W | W-01 to W-14 | DirectorServiceTests, DirectorCreationTests (existing) | — | COMPLETE |
| **23. My Pages** | 23.1-23.5 | W, AL | W-01 to W-14, AL-11 to AL-14 | — | — | GOOD (extend AL) |
| **24. Friends System** | 24.1-24.3 | AB | AB-01 to AB-07 | FriendshipServiceTests (existing) | — | NEW MODULE AB |
| **25. Gamification** | 25.1-25.3 | AB | AB-08 to AB-12 | — | — | NEW MODULE AB |
| **26. Dark Mode** | 26.1-26.3 | UI-Sweep | existing 17 tests | — | — | COMPLETE |
| **27. Print & Export** | 27.1-27.3 | AG | AG-01 to AG-10 | — | — | NEW MODULE AG |
| **28. Keyboard/A11y** | 28.1-28.5 | AH | AH-01 to AH-12 | — | — | NEW MODULE AH |
| **29. Offline/Error** | 29.1-29.5 | Z | Z-01 to Z-10 | — | — | COMPLETE |
| **30. SignalR** | 30.1-30.3 | AF, R | AF-01 to AF-09, R-01 to R-10 | — | — | NEW MODULE AF |
| **31. Security** | 31.1-31.8 | AE | AE-01 to AE-11 | PiiMaskerTests, EncryptionTests (new) | AE-U01 to AE-U03 | NEW MODULE AE |
| **32. Diagnostics** | 32.1-32.2 | AD | AD-26/27 | — | — | In AD |
| **33. Job Types/Groupings** | 33.1-33.2 | AD | AD-21 to AD-25, AD-28 | — | — | In AD |
| **34. Team Calendars** | 34.1 | AC | AC-01 to AC-08 | TeamCalendarServiceTests (new) | TC-01 to TC-04 | NEW MODULE AC |
| **35. Tech Shifts** | 35.1-35.2 | AL | AL-18 | TechShiftServiceTests (new) | TS-01 to TS-03 | NEW (xUnit primary) |
| **36. Conflict Detection** | 36.1-36.3 | AF | AF-08/09 | ConflictCheckerTests, BusyUserServiceTests (new) | CC/BU tests | NEW (xUnit primary) |
| **37. Telemetry** | 37.1 | AK | AK-05 to AK-07 | — | — | NEW MODULE AK |
| **38. Skeleton Loading** | 38.1 | AJ | AJ-01 | — | — | In AJ |
| **39. Sidebar/Nav** | 39.1-39.2 | Every-Button, Q | existing 33 + 33 tests | — | — | COMPLETE |
| **40. Widgets** | 40.1-40.3 | AK | AK-01 to AK-04 | — | — | NEW MODULE AK |
| **41. Performance** | 41.1-41.6 | AJ | AJ-02/03/11 | — | — | In AJ |
| **42. Background Services** | 42.1-42.4 | — | — | — | — | NOT TESTABLE VIA UI (verify via logs/health check) |
| **43. Calendar Radar** | 43.1 | AJ | AJ-04 | — | — | In AJ |
| **44. Schedule View** | 44.1 | AJ | AJ-05 | — | — | In AJ |
| **45. Form Validation** | 45.1 | AH | AH-10/11 | — | — | In AH |
| **46. Seed Data** | 46.1-46.8 | A | A-01 to A-12 | — | — | COMPLETE |
| **47. Middleware** | 47.1-47.3 | AE | AE-09 | — | — | In AE |
| **48. View Components** | 48.1-48.6 | AJ | AJ-06 to AJ-09 | — | — | In AJ |
| **49. Mobile** | 49.1-49.3 | AI | AI-01 to AI-09 | — | — | NEW MODULE AI |
| **50. Startup Safety** | 50.1-50.10 | — | — | StartupSafetyTests (new) | SS-01 to SS-10 | xUnit only (full coverage) |
| **51. API Client Utils** | 51.1-51.3 | AJ | AJ-10 | — | — | In AJ |
| **52. Seeded Config** | 52.1-52.2 | A | A-01 to A-12 | — | — | COMPLETE |
| **53. Logging** | 53.1-53.3 | — | — | — | — | NOT TESTABLE VIA UI (verify via log files) |

---

## Implementation Priority Roadmap

### Phase 1: P0 Critical (implement first)
- Module AE: Security headers (AE-01 to AE-07) — 7 tests
- Module AA: Grant enforcement (AA-09, AA-10, AA-13, AA-14, AA-22, AA-25) — 6 tests
- Module AF: SignalR cross-tenant (AF-02, AF-07) — 2 tests
- xUnit: ConflictCheckerTests (CC-01 to CC-03) — 3 tests
- xUnit: GrantAuthorizationTests (AA-U01 to AA-U06) — 6 tests
- Extend Module B: Auth return URL, cookie security (B-13, B-19) — 2 tests
- Module AE: CSRF rejection on new handlers (AE-12) — 1 test

**Total Phase 1**: ~26 tests

### Phase 2: P1 High (implement second)
- Module AA remainder (AA-02 to AA-08, AA-11 to AA-21, AA-23) — 17 tests
- Module AD: Admin pages core (AD-01 to AD-17, AD-21/22, AD-28) — 19 tests
- Module AF: SignalR core (AF-01, AF-03 to AF-05, AF-08/09) — 6 tests
- Extend Modules B, C, D, J, K, L, M, N, Y — ~35 tests from extensions above
- xUnit: VacationApproval, DutyRotation, FeatureFlags, Notification, RateLimiting — ~25 tests
- Module AL: Critical coverage sweep (AL-11/12, AL-21 to AL-24) — 6 tests

**Total Phase 2**: ~108 tests

### Phase 3: P2 Medium (implement third)
- Module AB: Friends (AB-01 to AB-06) — 6 tests
- Module AC: Team calendars (AC-01 to AC-08) — 8 tests
- Module AG: Print & export (AG-01 to AG-10) — 10 tests
- Module AH: Keyboard/A11y (AH-01 to AH-12) — 12 tests
- Module AI: Mobile (AI-01 to AI-09) — 9 tests
- Module AK: Widgets (AK-01 to AK-04) — 4 tests
- Module AJ: Performance (AJ-02/03, AJ-06 to AJ-09) — 6 tests
- Module AL: Remaining coverage sweep — 12 tests
- Remaining extensions from P2 tests above — ~15 tests
- xUnit: Archive, EmailTemplate, CompanyLocalization, TechShift, TeamCalendar, Import — ~25 tests

**Total Phase 3**: ~107 tests

### Phase 4: P3 Low (implement last)
- Module AB: Game (AB-07 to AB-12) — 6 tests
- Module AJ: Remaining (AJ-01, AJ-04/05, AJ-10/11) — 5 tests
- Module AK: Telemetry (AK-05 to AK-07) — 3 tests
- Module AL: Remaining (AL-03, AL-05, AL-08) — 3 tests
- xUnit: StartupSafety — 5 tests
- Remaining P3 extensions — ~5 tests

**Total Phase 4**: ~27 tests

---

## Appendix A: Selector Reference

Common CSS selectors used across tests (from existing codebase analysis):

```javascript
// Calendar
'.shifts-calendar'                    // Shifts calendar wrapper
'.shifts-calendar__toolbar'           // Toolbar with selectors
'#moleculeSelect'                     // Molecule dropdown
'#jobTypeSelect'                      // JobType dropdown
'#viewModeSelect'                     // View mode (week/2weeks/month)
'.shifts-calendar__date-nav'          // Date navigation
'.excel-calendar__table'              // Calendar table
'.shifts-calendar__empty'             // Empty state
'.excel-calendar__header-day'         // Day column headers
'.excel-calendar__cell[data-row-id][data-date]' // Interactive cells
'.excel-calendar__row-label'          // Row labels
'.fill-handle'                        // Fill handle for drag copy

// Auth
'input[name="Email"], input#Email'    // Email input
'input[name="Password"], input#Password' // Password input
'form:has(input[name="Email"]) button[type="submit"]' // Login submit
'.auth-alert--error, .alert-danger, .validation-summary-errors' // Error alerts

// Navigation
'a[href*="/Owner"]'                   // Owner nav links
'nav a[href*="/Home"]'                // Home nav link
'form[action*="Logout"]'             // Logout form
'button:has-text("Logout")'          // Logout button

// Admin/Users
'input[name="NewEmail"], #NewEmail'   // Add user email
'select[name="NewRole"], #NewRole'    // Add user role
'input[name="NewPassword"]'           // Add user password

// Toasts/Alerts
'.toast, .notification, [role="alert"], .alert-success, .alert-danger' // Toast notifications

// Modals
'.modal'                              // Modal overlay
'.modal-content'                      // Modal content

// General
'__RequestVerificationToken'          // CSRF token hidden field
```

## Appendix B: API Endpoint Reference

Key endpoints tested across modules:

| Endpoint | Method | Auth | Module |
|----------|--------|------|--------|
| `/Auth/Login` | GET/POST | Anonymous | B |
| `/Auth/Signup` | GET/POST | Anonymous | C |
| `/Auth/Logout` | POST | Cookie | B |
| `/Api/SessionStatus` | GET | Cookie | B |
| `/Api/ScopeSwitcher` | GET | Cookie | E |
| `/Api/Calendar/GetShiftsData` | GET | Cookie | T |
| `/Api/Calendar/GetChoresData` | GET | Cookie | T |
| `/Api/Calendar/GetOnCallData` | GET | Cookie | T |
| `/Api/Calendar/GetOverviewData` | GET | Cookie | T |
| `/Api/Calendar/ShiftHistory` | GET | Cookie | T, J |
| `/Api/Calendar/QuickAddChore` | POST | Cookie | K |
| `/Api/Calendar/QuickAddOnDuty` | POST | Cookie | L |
| `/Api/Calendar/DeleteChore` | POST | Cookie | K |
| `/Api/Calendar/DeleteOnDuty` | POST | Cookie | L |
| `/Api/Calendar/RestoreChore` | POST | Cookie | K |
| `/Api/OnDuty/GetEligibleUsers` | GET | Cookie | L |
| `/Api/TechShift/Eligible` | GET | Cookie | AL |
| `/Api/Localization` | GET | Cookie | X |
| `/Api/Friends/Ids` | GET | Cookie | AB |
| `/Api/Game/*` | GET/POST | Cookie | AB |
| `/Api/Telemetry` | POST | Anonymous | AK |
| `/Api/ScheduleExport` | POST | Cookie | AG |
| `/Api/Hierarchy/*` | POST | Cookie | V |
| `/api/team-calendars/*` | GET/POST/PUT/DELETE | Cookie | AC |
| `/api/v1/users` | GET/POST/PATCH | API Key | S |
| `/api/v1/shifts` | GET | API Key | S |
| `/api/v1/time-off-requests` | GET/POST | API Key | S |
| `/api/v1/notifications` | GET/POST | API Key | S |
| `/api/v1/chores` | GET/POST/PATCH/DELETE | API Key | S |
| `/api/v1/on-duty` | GET/POST/PATCH/DELETE | API Key | S |
| `/api/v1/swap-requests` | GET/POST/DELETE | API Key | S |
| `/api/v1/feedback` | GET/POST/PATCH/DELETE | API Key | S |
| `/api/v1/audit-logs` | GET | API Key | S |
| `/api/v1/analytics/summary` | GET | API Key | S |
| `/api/v1/version` | GET | Anonymous | AL |
| `/health` | GET | Anonymous | AL |
| `/ready` | GET | Anonymous | AL |

## Appendix C: Feature Flags Affecting Tests

Tests must account for feature flags that gate functionality:

| Flag | Affects | Default | Test Impact |
|------|---------|---------|-------------|
| `FF_ALLOW_PUBLIC_SIGNUP` | Signup page access | true | Module C must verify gating |
| `FF_EXCEL_CALENDAR_SHIFTS` | Excel shift calendar | true | Module J tests |
| `FF_EXCEL_CALENDAR_CHORES` | Excel chore calendar | true | Module K tests |
| `FF_EXCEL_CALENDAR_ONCALL` | Excel on-call calendar | true | Module L tests |
| `FF_EXCEL_CALENDAR_OVERVIEW` | Excel overview calendar | true | Module M tests |
| `FF_API_ENABLED` | REST API master switch | true | Module S tests |
| `FF_ENABLE_API_KEY_MANAGEMENT` | API key management | true | Module S, W tests |
| `FF_FRIENDSHIPS_ENABLED` | Friends feature | false | Module AB must enable first |
| `FF_WIDGETS_ENABLED` | Dashboard widgets | true | Module AK tests |
| `FF_DUTY_ROTATION_ENABLED` | Duty rotation | false | Module L-19 to L-22 must enable |
| `FF_ENFORCE_RANK_ELIGIBILITY` | Rank checks for duty | false | Module L-23 must enable |
| `FF_VACATION_APPROVAL_ENABLED` | Approval rules | false | Module N-13/14 must enable |
| `FF_SETUP_TASKS_ENABLED` | Setup tasks | false | Module AD-19/20 must enable |
| `FF_ENABLE_DAILY_NOTIFICATIONS` | Daily digest | false | Notification digest tests |
| `FF_ENABLE_COMPANY_SWITCHER` | Company switcher | true | Module E tests |

---

## Appendix D: Existing Module Test ID Catalog

This appendix lists the test IDs already implemented in each existing module, based on file analysis. Use this to avoid duplicate test names when extending modules.

### Module A: Seeding (12 tests)

| ID | Description |
|----|-------------|
| A-01 | Project "Shifty" exists in database |
| A-02 | Area "190" exists under Shifty project |
| A-03 | Molecules exist (Alhut, Text, BR, Hakam, System) |
| A-04 | Companies exist under each molecule |
| A-05 | HQ companies exist |
| A-06 | SystemAdmins company exists |
| A-07 | Grant types seeded (107+) |
| A-08 | Role templates seeded (13+) |
| A-09 | Feature flags seeded (50+) |
| A-10 | Default shift types exist (MORNING, NOON, NIGHT, MIDDLE, OFFLINE) |
| A-11 | Default app config exists (RestHours=8, WeeklyHoursCap=40) |
| A-12 | Owner user exists and can navigate |

### Module B: Authentication (12 tests)

| ID | Description |
|----|-------------|
| B-01 | Owner login success |
| B-02 | Wrong password shows error |
| B-03 | Non-existent email shows error |
| B-04 | Empty fields submit shows validation errors |
| B-05 | Account lockout after failed attempts |
| B-06 | Logout clears session |
| B-07 | Session status endpoint returns valid |
| B-08 | Griffin ADFS button rendering |
| B-09 | ForgotPassword page has password inputs |
| B-10 | Anti-forgery token present |
| B-11 | Language toggle on login page |
| B-12 | RTL layout in Hebrew |

### Module C: Signup (10 tests)

| ID | Description |
|----|-------------|
| C-01 | Signup page loads |
| C-02 | Signup pending state after submit |
| C-03 | Duplicate email detection (existing user) |
| C-04 | Duplicate email detection (pending request) |
| C-05 | Invalid email format rejected |
| C-06 | Short password rejected |
| C-07 | Owner sees notification on new signup |
| C-08 | Approve join request creates user |
| C-09 | Reject join request with reason |
| C-10 | Signup state tracking |

### Module D: User Management (14 tests)

| ID | Description |
|----|-------------|
| D-01 | Admin Users page loads |
| D-02 | User list shows existing users |
| D-03 | Filter by department |
| D-04 | Edit user profile |
| D-05 | Add new user |
| D-06 | Toggle user active/inactive |
| D-07 | Bulk import users |
| D-08 | Manager scoped user access |
| D-09 | Employee denied access to admin users |
| D-10 | Cross-company user isolation |
| D-11 | Audit trail for user changes |
| D-12 | Assign role template to user |
| D-13 | Revoke role template |
| D-14 | Individual grant assignment |

### Module E: Context Switcher (8 tests)

| ID | Description |
|----|-------------|
| E-01 | Owner sees scope switcher |
| E-02 | Switch to Tzafona molecule |
| E-03 | Switch to Hir molecule |
| E-04 | Switch to other company |
| E-05 | Search filter in dropdown |
| E-06 | Clear selection |
| E-07 | Employee sees no switcher (single company) |
| E-08 | Keyboard navigation in dropdown |

### Module F: Blueprints (16 tests)

| ID | Description |
|----|-------------|
| F-01 | View blueprints page |
| F-02 | Morning Alhut blueprint exists |
| F-03 | Afternoon Alhut blueprint exists |
| F-04 | Night Alhut blueprint exists |
| F-05 | Morning Text blueprint exists |
| F-06 | Morning BR blueprint exists |
| F-07 | Morning Hakam blueprint exists |
| F-08 | Offline blueprint exists |
| F-09 | Create custom blueprint |
| F-10 | Edit blueprint name |
| F-11 | Edit blueprint times |
| F-12 | Check blueprint usage |
| F-13 | Delete unused blueprint |
| F-14 | Delete used blueprint (confirmation) |
| F-15 | Delete blocked (in active use) |
| F-16 | Per-company blueprint isolation |

### Module G: Programs (20 tests)

| ID | Description |
|----|-------------|
| G-01 | View programs list for Alhut morning |
| G-02 | Alhut night program exists |
| G-03 | Text afternoon program exists |
| G-04 | BR morning program exists |
| G-05 | Hakam daily program exists |
| G-06 | Per-day staffing mask works |
| G-07 | Edit program name |
| G-08 | Edit program staffing mask |
| G-09 | Edit staffing numbers |
| G-10 | Delete program |
| G-11 | Generate shift instances from program |
| G-12 | Instance count matches date range |
| G-13 | Staffing required field enforced |
| G-14 | Program ID displayed |
| G-15 | No overwrite existing instances by default |
| G-16 | Overwrite mode creates new instances |
| G-17 | Hir molecule program |
| G-18 | Hitazmut molecule program |
| G-19 | QA Alpha molecule program |
| G-20 | Per-company program isolation |

### Module H: Master Programs (6 tests)

| ID | Description |
|----|-------------|
| H-01 | Create master program |
| H-02 | Apply master program to Tzafona companies |
| H-03 | Apply master program to Hir companies |
| H-04 | Edit master program |
| H-05 | Delete master program |
| H-06 | Existing instances unchanged by master application |

### Module I: Instances (14 tests)

| ID | Description |
|----|-------------|
| I-01 | Shift mode calendar view |
| I-02 | User mode calendar view |
| I-03 | Detach instance from program |
| I-04 | Override detached instance name |
| I-05 | Reset detached instance to program |
| I-06 | Capacity mode view |
| I-07 | Capacity override |
| I-08 | Remove capacity override |
| I-09 | Manual instance creation |
| I-10 | Concurrency detection |
| I-11 | Staffing zero allowed |
| I-12 | Filter by Alhut shift type |
| I-13 | Filter by Text shift type |
| I-14 | Date navigation (next week) |

### Module J: Shift Assignments (24 tests)

| ID | Description |
|----|-------------|
| J-01 | Shifts calendar renders with table, toolbar, date nav |
| J-02 | Excel calendar table has header days and row labels |
| J-03 | Calendar cells have data-row-id and data-date attributes |
| J-04 | Owner sees add assignment buttons |
| J-05 | Trainee assignment |
| J-06 | Assignment names displayed in cells |
| J-07 | Trainee badge visible |
| J-08 | Unassign trainee |
| J-09 | Fill handle visible |
| J-10 | Inline edit mode |
| J-11 | Multiple assignments per cell |
| J-12 | Rest violation warning |
| J-13 | Alhut lead-only assignments |
| J-14 | Text lead-only assignments |
| J-15 | BR director assignments |
| J-16 | Employee read-only view |
| J-17 | Trainee read-only view |
| J-18 | Assigner no shift creation |
| J-19 | Date navigation works |
| J-20 | SignalR real-time update |
| J-21 | Concurrency conflict detection |
| J-22 | Calendar type selector |
| J-23 | Today highlight |
| J-24 | Just mine toggle |

### Module K: Chore Calendar (16 tests)

| ID | Description |
|----|-------------|
| K-01 | Chore calendar renders |
| K-02 | Calendar type selector |
| K-03 | Chore type selector |
| K-04 | Quick add chore |
| K-05 | Specific chore type display |
| K-06 | Chore assignments visible |
| K-07 | Cancel chore |
| K-08 | Just mine toggle |
| K-09 | Date navigation |
| K-10 | Filter by chore type |
| K-11 | Assigner can manage chores |
| K-12 | Employee read-only |
| K-13 | Print button visible |
| K-14 | Notification for chore assignment |
| K-15 | Public chores page accessible |
| K-16 | SignalR updates for chores |

### Module L: On-Duty Calendar (18 tests)

| ID | Description |
|----|-------------|
| L-01 | On-call calendar renders |
| L-02 | Calendar type and area selector |
| L-03 | Add lead on-duty |
| L-04 | Add backup on-duty |
| L-05 | Cancel on-duty |
| L-06 | Duty assignments visible |
| L-07 | Cross-company assignment |
| L-08 | Just mine toggle |
| L-09 | Date navigation |
| L-10 | Vacation conflict detection |
| L-11 | Assigner no on-duty access |
| L-12 | Employee read-only |
| L-13 | Print button visible |
| L-14 | Today highlight |
| L-15 | SignalR updates |
| L-16 | View modes |
| L-17 | Public on-duty page accessible |
| L-18 | Concurrent edit handling |

### Module M: Overview Calendar (16 tests)

| ID | Description |
|----|-------------|
| M-01 | Overview calendar renders |
| M-02 | Calendar type selector |
| M-03 | Aggregated data display (shifts + chores + on-duty) |
| M-04 | Legend visible |
| M-05 | Just mine toggle |
| M-06 | Add day note |
| M-07 | Date navigation |
| M-08 | Note modal |
| M-09 | (reserved) |
| M-10 | Print button |
| M-11 | View modes (week/2weeks/month) |
| M-12 | Today highlight |
| M-13 | Employee overview |
| M-14 | Overlay badges |
| M-15 | SignalR updates |
| M-16 | Manager overview |

### Module N: Time-Off (12 tests)

| ID | Description |
|----|-------------|
| N-01 | Create vacation request |
| N-02 | Create half-day request |
| N-03 | Approve time-off |
| N-04 | Decline time-off |
| N-05 | View own requests |
| N-06 | Auto-unassign on approve |
| N-07 | Extended leave request |
| N-08 | Override limits |
| N-09 | Delete time-off request |
| N-10 | Time-off overlay on calendar |
| N-11 | Duty conflict with time-off |
| N-12 | Duplicate time-off detection |

### Module O: Swaps (10 tests)

| ID | Description |
|----|-------------|
| O-01 | Create swap request |
| O-02 | Target user selection |
| O-03 | Approve swap |
| O-04 | Decline swap |
| O-05 | Self-swap prevention |
| O-06 | Cross-company swap |
| O-07 | Swap notification sent |
| O-08 | Manager swap access |
| O-09 | Concurrent swap handling |
| O-10 | Swap history |

### Module P: Tenant Isolation (14 tests)

| ID | Description |
|----|-------------|
| P-01 | Tzafona company sees only Tzafona data |
| P-02 | Hir company sees only Hir data |
| P-03 | Hitazmut sees only Hitazmut data |
| P-04 | QA Alpha sees only QA Alpha data |
| P-05 | API returns tenant-scoped data |
| P-06 | Direct URL access blocked for wrong company |
| P-07 | Chores scoped by molecule |
| P-08 | Time-off scoped by company |
| P-09 | User list scoped to company |
| P-10 | Notifications scoped to user |
| P-11 | On-duty global (area-scoped) |
| P-12 | Owner can switch between companies |
| P-13 | Empty data for new company |
| P-14 | Blueprints per-company isolation |

### Module Q: Role Access (33 tests)

Tests are organized by role. Each role tests: home page, sidebar, forbidden page, calendar, API scope.

| Role | Test IDs |
|------|----------|
| Owner | Q-owner-01 to Q-owner-05 |
| Director | Q-director-01 to Q-director-05 |
| Manager | Q-manager-01 to Q-manager-05 |
| Assigner | Q-assigner-01 to Q-assigner-05 |
| Employee | Q-employee-01 to Q-employee-07 |
| Trainee | Q-trainee-01 to Q-trainee-05 |

Plus: Q-nologin tests for unauthenticated access (6 tests).

### Module R: Concurrency (10 tests)

| ID | Description |
|----|-------------|
| R-01 | Concurrent shift assignment from two users (A and B) |
| R-02 | SignalR push between multiple contexts |
| R-03 | Concurrent chore assignment |
| R-04 | Concurrent time-off request |
| R-05 | Concurrent user edit |
| R-06 | Concurrent swap request |
| R-07 | Back/forward button handling |
| R-08 | Session validity after concurrent ops |
| R-09 | Separate browser sessions |
| R-10 | Three concurrent chore operations |

### Module S: REST API (21 tests)

| ID | Description |
|----|-------------|
| S-01 to S-19 | CRUD operations on each REST endpoint (users, shifts, time-off, notifications, chores, on-duty, swaps, feedback, audit-logs, analytics, admin-grants) |
| S-20 | Invalid API key rejected |
| S-21 | Missing API key rejected |

### Module T: Page Handler API (16 tests)

| ID | Description |
|----|-------------|
| T-01 | Shifts data API with and without params |
| T-02 | Chores data API |
| T-03 | On-call data API |
| T-04 | Overview data API |
| T-05 | Shift history API |
| T-06 | Session status API |
| T-07 | Eligible users API |
| T-08 | Scope switcher API |
| T-10 | Shift history with filters |
| T-11 | Hierarchy API |
| T-12 | Signup options API |
| T-13 | Session status detail |
| T-14 | Localization API |
| T-15 | Schedule export API |
| T-16 | Eligible users API (on-duty) |

### Module U: Owner Admin (18 tests)

| ID | Description |
|----|-------------|
| U-01 | Owner dashboard loads |
| U-02 | Backup page loads |
| U-03 | Download backup |
| U-04 | Restore backup |
| U-05 | DB selector in console |
| U-06 | SQL injection blocked |
| U-07 | DROP statement blocked |
| U-08 | System health dashboard |
| U-09 | Feature flags page |
| U-10 | Language management page |
| U-11 | Email config page |
| U-12 | Email templates page |
| U-13 | Griffin config page |
| U-14 | Permissions viewer |
| U-15 | Locked users page |
| U-16 | Data lifecycle page |
| U-17 | Area config page |
| U-18 | Game config page |

### Module V: Admin Organization (12 tests)

| ID | Description |
|----|-------------|
| V-01 | Hierarchy tree view |
| V-02 | Molecule names displayed |
| V-03 | Company names displayed |
| V-04 | Area names displayed |
| V-05 | Quick actions available |
| V-06 | Hierarchy detail view |
| V-07 | Job types CRUD |
| V-08 | Shift groupings |
| V-09 | Role templates list |
| V-10 | Assign role template |
| V-11 | Assign individual grant |
| V-12 | Owner permissions view |

### Module W: Director/My/Public (14 tests)

| ID | Description |
|----|-------------|
| W-01 | Director dashboard/hub |
| W-02 | Director company filter |
| W-03 | View-as mode enter |
| W-04 | View-as mode exit / Settings |
| W-05 | Employee dashboard |
| W-06 | Profile page / Notification badge |
| W-07 | Help page / Settings |
| W-08 | API keys page / Notifications |
| W-09 | My requests page / Notification badge |
| W-10 | Help page / Public chores |
| W-11 | API keys page / Public on-duty |
| W-12 | Feedback page / My requests |
| W-13 | Employee dashboard / Public chores |
| W-14 | Leaderboard / Public on-duty |

### Module X: Localization (12 tests)

| ID | Description |
|----|-------------|
| X-01 | Hebrew language active |
| X-02 | English language active |
| X-03 | RTL layout in Hebrew |
| X-04 | LTR layout in English |
| X-05 | RTL caret direction |
| X-06 | Hebrew day names on calendar |
| X-07 | Hebrew validation messages |
| X-08 | Hebrew navigation labels |
| X-09 | Hebrew modal text |
| X-10 | No text overflow in Hebrew |
| X-11 | Company-level override |
| X-12 | Language edit mode |

### Module Y: Notifications (8 tests)

| ID | Description |
|----|-------------|
| Y-01 | Shift notification created |
| Y-02 | Shift removed notification |
| Y-03 | Time-off approved notification |
| Y-04 | Chore assigned notification |
| Y-05 | On-duty assigned notification |
| Y-06 | Unread badge count |
| Y-07 | Mark as read |
| Y-08 | Access request notification |

### Module Z: Error Handling (10 tests)

| ID | Description |
|----|-------------|
| Z-01 | 404 page for invalid URL |
| Z-02 | Access denied page |
| Z-03 | Missing anti-forgery token |
| Z-04 | Missing API key on REST endpoint |
| Z-05 | No auth / wrong scope |
| Z-06 | Offline detection banner |
| Z-07 | Rate limiting response |
| Z-08 | Console errors collected |
| Z-09 | Noscript fallback |
| Z-10 | Shift grouping filter works without error |

---

## Appendix E: Detailed xUnit Test Specifications (Arrange-Act-Assert)

This appendix provides implementation-ready specifications for all new xUnit test files.

### ConflictCheckerTests.cs

```csharp
// File: ShiftManager.Tests/UnitTests/Services/ConflictCheckerTests.cs
// Pattern: InMemory DB, Moq, FluentAssertions, IDisposable

[Fact]
public async Task DetectsRestViolation_WhenGapLessThanRestHours()
{
    // Arrange: Create two shifts with only 4-hour gap, RestHours=8
    // Night shift 22:00-06:00, Morning shift 07:00-15:00 (only 1h gap)
    // Act: Call CheckConflicts(userId, date, startTime, endTime)
    // Assert: Result.HasConflict should be true, ConflictType = RestViolation
}

[Fact]
public async Task NoViolation_WhenGapExceedsRestHours()
{
    // Arrange: Morning shift 07:00-15:00, Night shift 22:00-06:00 (7h gap, RestHours=6)
    // Act: CheckConflicts
    // Assert: Result.HasConflict should be false
}

[Fact]
public async Task DetectsDoubleBooking_WhenSameTimeSlot()
{
    // Arrange: User already assigned to 07:00-15:00 on 2026-03-01
    // Act: Try to assign same user to 08:00-14:00 on 2026-03-01
    // Assert: Result.HasConflict = true, ConflictType = DoubleBooking
}

[Fact]
public async Task NoConflict_ForDifferentDates()
{
    // Arrange: User assigned to morning on 2026-03-01
    // Act: Check morning on 2026-03-02 (with sufficient gap)
    // Assert: No conflict
}

[Fact]
public async Task HandlesOvernightShifts_CrossingMidnight()
{
    // Arrange: Night shift 22:00 on day 1 to 06:00 on day 2
    // Act: Check for morning shift 07:00 on day 2
    // Assert: Check gap correctly calculated across midnight
}
```

### BusyUserServiceTests.cs

```csharp
// File: ShiftManager.Tests/UnitTests/Services/BusyUserServiceTests.cs

[Fact]
public async Task UserWithApprovedVacation_IsBusy()
{
    // Arrange: User has approved TimeOffRequest covering 2026-03-05
    // Act: GetBusyStatus(userId, new DateOnly(2026, 3, 5))
    // Assert: IsBusy = true, Reason = "Vacation"
}

[Fact]
public async Task UserWithExistingShift_IsBusy()
{
    // Arrange: User has ShiftAssignment on 2026-03-05
    // Act: GetBusyStatus(userId, date)
    // Assert: IsBusy = true, Reason = "Assigned"
}

[Fact]
public async Task UserWithOnDuty_IsBusy()
{
    // Arrange: User has OnDutyEntry on 2026-03-05
    // Act: GetBusyStatus(userId, date)
    // Assert: IsBusy = true, Reason = "OnDuty"
}

[Fact]
public async Task UserWithNoAssignments_IsAvailable()
{
    // Arrange: User has no assignments on 2026-03-05
    // Act: GetBusyStatus(userId, date)
    // Assert: IsBusy = false
}

[Fact]
public async Task PendingVacation_DoesNotMakeUserBusy()
{
    // Arrange: User has pending (not approved) TimeOffRequest
    // Act: GetBusyStatus
    // Assert: IsBusy = false (pending != approved)
}
```

### VacationApprovalServiceTests.cs

```csharp
// File: ShiftManager.Tests/UnitTests/Services/VacationApprovalServiceTests.cs

[Fact]
public async Task AutoApproveRule_ApprovesMatchingRequest()
{
    // Arrange: Rule with AutoApprove=true for requests <= 2 days
    //          TimeOffRequest for 1 day
    // Act: ProcessApproval(requestId)
    // Assert: Request.Status = Approved
}

[Fact]
public async Task MultiLevelApproval_RoutesToCorrectApprover()
{
    // Arrange: Rule requiring manager approval for 3+ days
    //          TimeOffRequest for 5 days
    // Act: ProcessApproval(requestId)
    // Assert: Request.Status = PendingApproval, ApproverUserId set
}

[Fact]
public async Task Approval_AutoUnassignsAffectedShifts()
{
    // Arrange: User has shift assignments on vacation dates
    // Act: ApproveTimeOff(requestId)
    // Assert: ShiftAssignments for those dates have UserId = null
}

[Fact]
public async Task NoMatchingRule_FallsThrough()
{
    // Arrange: No rules configured
    // Act: ProcessApproval(requestId)
    // Assert: Request stays in Pending state
}
```

### DutyRotationServiceTests.cs

```csharp
// File: ShiftManager.Tests/UnitTests/Services/DutyRotationServiceTests.cs

[Fact]
public async Task Rotation_FollowsQueueOrder()
{
    // Arrange: Queue with [UserA, UserB, UserC], current index=0
    // Act: GetNextUser()
    // Assert: Returns UserA, index advances to 1
}

[Fact]
public async Task Rotation_SkipsUserOnVacation()
{
    // Arrange: Queue [UserA (on vacation), UserB, UserC]
    // Act: GetNextUser(date)
    // Assert: Returns UserB (skips A)
}

[Fact]
public async Task Rotation_WrapsAroundAfterLastUser()
{
    // Arrange: Queue [A, B, C], current index = 2 (C)
    // Act: GetNextUser() twice
    // Assert: First returns C, second wraps to A
}

[Fact]
public async Task EmptyQueue_ReturnsNull()
{
    // Arrange: Empty rotation queue
    // Act: GetNextUser()
    // Assert: Returns null
}
```

### FeatureFlagServiceTests.cs

```csharp
// File: ShiftManager.Tests/UnitTests/Services/FeatureFlagServiceTests.cs

[Fact]
public async Task IsEnabled_ReturnsTrueForEnabledFlag()
{
    // Arrange: FeatureFlag { Key = "FF_TEST", IsEnabled = true } in DB
    // Act: IsEnabled("FF_TEST")
    // Assert: true
}

[Fact]
public async Task IsEnabled_ReturnsFalseForDisabledFlag()
{
    // Arrange: FeatureFlag { Key = "FF_TEST", IsEnabled = false }
    // Act: IsEnabled("FF_TEST")
    // Assert: false
}

[Fact]
public async Task SetFlag_UpdatesFlagValue()
{
    // Arrange: Flag exists as disabled
    // Act: SetFlag("FF_TEST", true)
    // Assert: DB shows IsEnabled = true
}

[Fact]
public async Task IsEnabled_ReturnsFalseForUnknownFlag()
{
    // Arrange: No flag with key "FF_NONEXISTENT"
    // Act: IsEnabled("FF_NONEXISTENT")
    // Assert: false
}
```

### RateLimitingServiceTests.cs

```csharp
// File: ShiftManager.Tests/UnitTests/Services/RateLimitingServiceTests.cs

[Fact]
public void WithinLimit_ReturnsAllowed()
{
    // Arrange: RateLimitingService, key="test:ip:1.2.3.4", limit=10
    // Act: Call IsAllowed 5 times
    // Assert: All return true
}

[Fact]
public void ExceedingLimit_ReturnsBlocked()
{
    // Arrange: limit=3
    // Act: Call IsAllowed 4 times
    // Assert: First 3 return true, 4th returns false
}

[Fact]
public void Counter_ResetsAfterWindow()
{
    // Arrange: Window = 100ms, limit = 2
    // Act: Call IsAllowed 2 times (both true), wait 150ms, call again
    // Assert: Third call returns true (window expired)
}

[Fact]
public void DifferentKeys_TrackedIndependently()
{
    // Arrange: limit = 1
    // Act: IsAllowed("key1") then IsAllowed("key2")
    // Assert: Both return true (different keys)
}
```

### NotificationServiceTests.cs

```csharp
// File: ShiftManager.Tests/UnitTests/Services/NotificationServiceTests.cs

[Fact]
public async Task CreateNotification_PersistsToDatabase()
{
    // Arrange: Target userId, notification type ShiftAdded
    // Act: CreateAsync(userId, NotificationType.ShiftAdded, "Shift assigned")
    // Assert: DB contains notification for userId
}

[Fact]
public async Task MarkAsRead_UpdatesIsReadFlag()
{
    // Arrange: Unread notification in DB
    // Act: MarkAsReadAsync(notificationId)
    // Assert: Notification.IsRead = true
}

[Fact]
public async Task MarkAllAsRead_UpdatesAllForUser()
{
    // Arrange: 3 unread notifications for userId
    // Act: MarkAllAsReadAsync(userId)
    // Assert: All 3 have IsRead = true
}

[Fact]
public async Task Delete_RemovesFromDatabase()
{
    // Arrange: Notification in DB
    // Act: DeleteAsync(notificationId)
    // Assert: Notification no longer in DB
}

[Fact]
public async Task GetUnreadCount_ReturnsCorrectNumber()
{
    // Arrange: 5 total, 3 unread for userId
    // Act: GetUnreadCountAsync(userId)
    // Assert: Returns 3
}

[Fact]
public async Task Notification_ScopedToTargetUserOnly()
{
    // Arrange: Notification for userA, not userB
    // Act: GetNotificationsAsync(userB)
    // Assert: Empty list (not userA's notification)
}
```

---

## Appendix F: Test Data Requirements

### Required Test Data for Full Test Suite Execution

The test suite depends on the following seeded data being present. Module A verifies this exists.

**Organization Hierarchy:**
- Project: Shifty
- Area: 190
- Molecules: Alhut (Workforce), Text (Workforce), BR (Workforce), Hakam (Workforce), System (System)
- Companies: At least one per molecule (e.g., Tzafona, Hir, Hitazmut, QA-Alpha)
- HQ Companies: At least one (for director entities)

**Users (seeded by 00-setup-test-data):**
- admin@local / admin123 — Owner
- test.owner@shifty.test / TestOwner123! — Owner (B-041)
- test.director@shifty.test / TestDirector123! — Director (BRDirector template)
- test.manager@shifty.test / TestManager123! — Manager (AlhutLead template)
- test.assigner@shifty.test / TestAssigner123! — Assigner
- test.member@shifty.test / TestMember123! — Employee (Employee template)
- test.nogrants@shifty.test / TestNoGrants123! — NoGrants (no template)
- locked@test / Test1234! — Employee (created by setup for lockout tests)

**Shift Types (blueprints):**
- MORNING (07:00-15:00)
- NOON (15:00-22:00)
- NIGHT (22:00-07:00)
- MIDDLE (10:00-18:00)
- OFFLINE

**Programs:**
- At least one program per molecule with staffing mask

**Feature Flags:**
- All 50+ flags seeded with their default values

**Grant Types:**
- All 107+ grant types seeded

**Role Templates:**
- All 13 templates seeded with their grant mappings

### Data Created During Test Execution

Some tests create data that subsequent tests depend on:

| Created By | Data Type | Used By |
|------------|-----------|---------|
| Module C (signup) | Join request | Module C (approve/reject) |
| Module D (user mgmt) | Test users | Modules J, K, L (assignments) |
| Module F (blueprints) | Custom shift types | Module G (programs) |
| Module G (programs) | Shift instances | Module J (assignments) |
| Module J (assignments) | Shift assignments | Module N (time-off auto-unassign), Module O (swaps) |
| Module K (chores) | Chore assignments | Module M (overview aggregation) |
| Module L (on-duty) | On-duty entries | Module M (overview aggregation) |

This dependency chain is why the production-qa config uses `fullyParallel: false` and `workers: 1`.

---

## Appendix G: CI/CD Integration Notes

### Running the Test Suite

**xUnit tests (fast, no app required):**
```bash
cd ShiftManager.Tests
dotnet test --configuration Release --logger "console;verbosity=normal"
```

**Playwright E2E tests (requires running app):**
```bash
# Start the application
cd ShiftManager
dotnet run --urls http://localhost:5000

# In another terminal
cd qa-automation
npx playwright test --config=playwright.production-qa.config.js
```

### Parallel vs Sequential

- **xUnit tests**: Can run fully parallel (InMemory DB with unique names)
- **Playwright production-qa**: MUST run sequential (single worker, data dependencies)
- **Playwright regular tests**: Can run parallel (2 workers, independent)

### Evidence Collection

All Playwright tests save screenshots to `ProductionReady/{module-folder}/`. After a full run:

```
ProductionReady/
  02-auth/             # Module B evidence
  03-signup/           # Module C evidence
  ...
  29-grants-roletemplates/  # Module AA evidence (new)
  30-friends-game/          # Module AB evidence (new)
  ...
  40-coverage-sweep/        # Module AL evidence (new)
  playwright-report/        # HTML report
  test-results.json         # JSON results
  test-artifacts/           # Traces and videos
```

### Flaky Test Policy

Per `docs/FLAKY-TEST-POLICY.md`:
1. Tests that fail intermittently must be investigated, not retried
2. No `retries` in production-qa config (retries: 0)
3. Flaky tests should be stabilized with explicit waits (`waitForLoadState`, `waitForURL`) rather than arbitrary timeouts
4. Use `expect(locator).toBeVisible({ timeout: X })` rather than `page.waitForTimeout(X)`

---

*End of Comprehensive Test Plan*
*Total specified test cases: ~950 across 41 Playwright modules + 15 xUnit files*
*Coverage: 53/53 inventory sections mapped (100%)*
*Document version: 1.1 — 2026-02-26 (incorporates all 26 findings from TEST-PLAN-REVIEW.md)*
