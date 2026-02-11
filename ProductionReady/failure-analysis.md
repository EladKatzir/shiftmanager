# Production QA Test Failure Analysis

**Date:** 2026-02-08
**Total Tests:** 431 | **Passed:** 220 | **Failed:** 211 | **Pass Rate:** 51.0%

---

## Summary Table

| Classification | Count | % of Failures |
|---|---|---|
| MISSING_USER | 82 | 38.9% |
| HIDDEN_ELEMENT | 43 | 20.4% |
| SKIPPED_TEST | 24 | 11.4% |
| SELECTOR_MISMATCH | 22 | 10.4% |
| MISSING_TEXT | 9 | 4.3% |
| COUNT_MISMATCH | 11 | 5.2% |
| LOGIC_ERROR | 10 | 4.7% |
| SERVER_500 | 3 | 1.4% |
| PRODUCT_BUG | 7 | 3.3% |
| **TOTAL** | **211** | **100%** |

---

## MISSING_USER (82 failures)

**Root Cause:** Tests attempt to log in as non-owner users (employee, manager, director, trainee, assigner) that were never created. The `Setup-04` test that creates these users failed because `selectOption` could not find the expected role values in the `NewRole` dropdown. Since user creation failed, every subsequent test that logs in as a non-owner user times out on `page.waitForURL` because the login redirects back to `/Auth/Login` (credentials invalid).

This is the **single largest failure category** and represents a cascading failure from the setup phase, not 82 independent bugs.

| # | Test Name | Error |
|---|---|---|
| 1 | Setup-04: Create test users and verify each exists | selectOption timeout -- role options not found in dropdown |
| 17 | D-01: Owner creates user with each of 6 roles | selectOption timeout -- role options not found |
| 18 | D-02: Create user with each job type | selectOption timeout -- role options not found |
| 21 | D-08: Manager or non-owner user access to /Admin/Users | selectOption timeout (user creation prerequisite) |
| 22 | D-09: Employee cannot access /Admin/Users | selectOption timeout (user creation prerequisite) |
| 26 | E-07: Employee does NOT see context switcher | selectOption timeout (user creation prerequisite) |
| 65 | J-12: AlhutLead manager can access shifts calendar | Login timeout -- user does not exist |
| 66 | J-13: TextLead manager can access shifts calendar | Login timeout -- user does not exist |
| 67 | J-14: BRDirector can access shifts calendar | Login timeout -- user does not exist |
| 68 | J-15: Employee sees readonly calendar with no assignment controls | Login timeout -- user does not exist |
| 69 | J-16: Trainee sees readonly calendar | Login timeout -- user does not exist |
| 70 | J-17: Assigner has no shift assignment controls | Login timeout -- user does not exist |
| 73 | K-10: Assigner can access chore calendar with edit controls | Login timeout -- user does not exist |
| 74 | K-11: Assigner CANNOT manage on-duty calendar | Login timeout -- user does not exist |
| 75 | K-12: Employee sees read-only chore calendar | Login timeout -- user does not exist |
| 79 | L-11: Assigner has no edit controls on on-duty calendar | Login timeout -- user does not exist |
| 80 | L-12: Employee sees read-only on-duty calendar | Login timeout -- user does not exist |
| 83 | M-13: Employee can access overview calendar (read-only) | Login timeout -- user does not exist |
| 84 | M-16: Manager can access overview calendar with editing | Login timeout -- user does not exist |
| 85 | N-01: Employee navigates to time-off creation page | Login timeout -- user does not exist |
| 86 | N-02: Employee sees vacation type selector | Login timeout -- user does not exist |
| 87 | N-05: Employee sees own requests on My/Requests page | Login timeout -- user does not exist |
| 88 | O-01: Employee can access swap request form on My/Requests | Login timeout -- user does not exist |
| 89 | O-02: Swap form shows shift selector and user list | Login timeout -- user does not exist |
| 90 | O-05: Employee swap form only shows same-company users | Login timeout -- user does not exist |
| 92 | O-10: Employee sees swap history on My/Requests | Login timeout -- user does not exist |
| 93 | P-01: emp.tz.alhut sees only Tzafona data | Login timeout -- user does not exist |
| 94 | P-02: emp.hir.alhut sees only Hir data | Login timeout -- user does not exist |
| 95 | P-03: emp.hit.alhut sees only Hitazmut data | Login timeout -- user does not exist |
| 96 | P-04: emp.alpha.alhut sees only QA-Alpha data | Login timeout -- user does not exist |
| 98 | P-06: Direct URL to non-existent shift ID does not leak data | Login timeout -- user does not exist |
| 99 | P-07: Chores page is scoped to molecule for employee | Login timeout -- user does not exist |
| 100 | P-08: Time-off requests scoped to company for employee | Login timeout -- user does not exist |
| 101 | P-09: Manager user list is scoped | Login timeout -- user does not exist |
| 102 | P-10: Notifications are scoped | Login timeout -- user does not exist |
| 103 | Q-director-01: Director home page loads | Login timeout -- user does not exist |
| 104 | Q-director-02: Director sidebar has admin nav but NO Owner link | Login timeout -- user does not exist |
| 105 | Q-director-03: Director is denied access to /Owner/Index | Login timeout -- user does not exist |
| 106 | Q-director-04: Director can access Calendar/Shifts | Login timeout -- user does not exist |
| 107 | Q-director-05: Director API /Api/SessionStatus returns 200 | Login timeout -- user does not exist |
| 108 | Q-manager-01: Manager home page loads correctly | Login timeout -- user does not exist |
| 109 | Q-manager-02: Manager sidebar has admin nav, NO Owner link | Login timeout -- user does not exist |
| 110 | Q-manager-03: Manager is denied access to /Owner/Index | Login timeout -- user does not exist |
| 111 | Q-manager-04: Manager can access Calendar/Shifts | Login timeout -- user does not exist |
| 112 | Q-manager-05: Manager API /Api/SessionStatus returns 200 | Login timeout -- user does not exist |
| 113 | Q-employee-01: Employee home page loads correctly | Login timeout -- user does not exist |
| 114 | Q-employee-02: Employee sidebar has NO admin nav links | Login timeout -- user does not exist |
| 115 | Q-employee-03: Employee is denied access to /Owner/Index | Login timeout -- user does not exist |
| 116 | Q-employee-04: Employee sees calendar in read-only mode | Login timeout -- user does not exist |
| 117 | Q-employee-05: Employee API /Api/SessionStatus returns 200 | Login timeout -- user does not exist |
| 118 | Q-employee-06: Employee is denied access to /Admin/Users | Login timeout -- user does not exist |
| 119 | Q-employee-07: Employee is denied access to /Admin/Config | Login timeout -- user does not exist |
| 120 | Q-trainee-01: Trainee home page loads correctly | Login timeout -- user does not exist |
| 121 | Q-trainee-02: Trainee sidebar has NO admin nav links | Login timeout -- user does not exist |
| 122 | Q-trainee-03: Trainee is denied access to /Owner/Index | Login timeout -- user does not exist |
| 123 | Q-trainee-04: Trainee sees calendar in read-only mode | Login timeout -- user does not exist |
| 124 | Q-trainee-05: Trainee API /Api/SessionStatus returns 200 | Login timeout -- user does not exist |
| 125 | Q-assigner-01: Assigner home page loads correctly | Login timeout -- user does not exist |
| 126 | Q-assigner-02: Assigner sidebar has chore-related links | Login timeout -- user does not exist |
| 127 | Q-assigner-03: Assigner is denied access to /Owner/Index | Login timeout -- user does not exist |
| 128 | Q-assigner-04: Assigner can access chores page | Login timeout -- user does not exist |
| 129 | Q-assigner-05: Assigner API /Api/SessionStatus returns 200 | Login timeout -- user does not exist |
| 130 | Q-assigner-06: Assigner has NO shift/duty edit controls | Login timeout -- user does not exist |
| 131 | R-01: Two contexts login as owner simultaneously | Login timeout -- user does not exist |
| 132 | R-02: Three contexts view shift calendar (SignalR) | Login timeout -- user does not exist |
| 133 | R-03: Two contexts view chores calendar concurrently | Login timeout -- user does not exist |
| 134 | R-04: Two contexts view requests page concurrently | Login timeout -- user does not exist |
| 135 | R-05: Two contexts view admin users page concurrently | Login timeout -- user does not exist |
| 136 | R-06: Two independent sessions on requests page | Login timeout -- user does not exist |
| 137 | R-07: Browser back/forward after page navigation | Login timeout -- user does not exist |
| 138 | R-08: Session remains valid after multiple navigations | Login timeout -- user does not exist |
| 139 | R-09: Two separate contexts both hit SessionStatus API | Login timeout -- user does not exist |
| 140 | R-10: Three contexts view chores calendar simultaneously | Login timeout -- user does not exist |
| 141 | S-01: GET /api/v1/shifts -- paginated list | Login timeout (API key setup depends on user) |
| 162 | T-01: GET /Api/Calendar/GetShiftsData | Login timeout -- user does not exist |
| 163 | T-02: GET /Api/Calendar/GetChoresData | Login timeout -- user does not exist |
| 164 | T-03: GET /Api/Calendar/GetOnCallData | Login timeout -- user does not exist |
| 165 | T-04: GET /Api/Calendar/GetOverviewData | Login timeout -- user does not exist |
| 166 | T-05: GET /Api/Calendar/ShiftHistory | Login timeout -- user does not exist |
| 190 | W-01: Director hub dashboard renders | Login timeout -- user does not exist |
| 191 | W-02: Director page has company/filter controls | Login timeout -- user does not exist |
| 200 | W-13: Employee dashboard loads after login | Login timeout -- user does not exist |
| 209 | Z-02: Employee navigates to /Owner -- gets AccessDenied | Login timeout -- user does not exist |

---

## HIDDEN_ELEMENT (43 failures)

**Root Cause:** The `<h1 class="page-title">` element exists in the DOM but has `visibility: hidden` or `display: none` (CSS renders it hidden). The tests use `locator('h1').first()` or `locator('.page-title, h1').first()` and assert `.toBeVisible()`, but the first `<h1>` resolved is the hidden one. This is a **CSS/layout issue** in the application -- the `.page-title` heading in the page header is present but styled as hidden, likely because the layout uses a sidebar-based design where the page title is duplicated (once in the header for screen readers, once visible in the content area).

| # | Test Name | Error |
|---|---|---|
| 7 | A-08: Seed creates role templates | `h1.page-title` is hidden |
| 11 | B-09: ForgotPassword page has password inputs | `h1, h2, h3` first resolves to hidden h1 |
| 27 | Admin Config: Save settings button exists | `h1, h2, .page-title` first is hidden |
| 29 | NotificationCenter: Page loads with content | `h1, h2` first is hidden |
| 30 | ApiKeys: Generate button and page elements exist | `h1, h2` first is hidden |
| 31 | Requests: Page loads with tab or form elements | `h1, h2` first is hidden |
| 34 | F-10: Edit blueprint name (EN + HE) | `#editNameModal` exists but modal not shown |
| 35 | F-11: Edit blueprint times | Modal/element hidden |
| 36 | F-12: Check blueprint usage before delete | Element hidden |
| 37 | F-13: Delete unused blueprint | Element hidden |
| 41 | G-01: Create program -- Alhut Morning Sun-Thu | `input[name="SelectedDays"]` not found |
| 42 | G-02: Create program -- Alhut Night Sun-Thu | Element not found/hidden |
| 43 | G-03: Create program -- Text Afternoon Sun-Fri | Element not found/hidden |
| 44 | G-04: Create program -- BR Morning Sun-Thu | Element not found/hidden |
| 45 | G-05: Create program -- Hakam Morning Daily | Element not found/hidden |
| 50 | G-11: Generate instances for 3 weeks | `.generate-btn` not found |
| 51 | G-15: Generate with overwriteExisting=false | Element not found |
| 52 | G-16: Generate with overwriteExisting=true | Element not found |
| 62 | J-05: Clicking add button triggers assignment UI | `.excel-calendar__add-btn` not found |
| 72 | K-05: Clicking add button triggers chore assignment UI | `.excel-calendar__add-btn` not found |
| 91 | O-06: Requests/Index loads without errors for owner | `.page-title` hidden |
| 174 | U-06: Database console: SQL injection rejected | `.alert-error` not found |
| 175 | U-07: Database console: DROP/INSERT blocked | `.alert-error` not found |
| 179 | Requests page loads with meaningful content | `h1, h2` first is hidden |
| 180 | Modal opens on edit button click and closes on ESC | Modal hidden after trigger |
| 182 | V-01: Organization page renders hierarchy tree | `.page-title, h1` first is hidden |
| 187 | V-09: Role templates page lists templates | `.page-title, h1` first is hidden |
| 188 | V-10: Admin Users page loads | `h1, h2` first is hidden |
| 192 | W-03: My Profile page loads with form fields | `.page-title, h1` first is hidden |
| 193 | W-04: My Settings page loads with form elements | `.page-title, h1, h2` first is hidden |
| 194 | W-05: Notification center page loads | Complex locator resolves to hidden |
| 195 | W-07: Help page renders with FAQ content | `h1` strict mode (2 elements) |
| 196 | W-09: My Requests page loads | `h1, h2` first is hidden |
| 199 | W-12: Feedback page renders with form | `h1, h2` first is hidden |
| 202 | X-09: Modal dialogs render in Hebrew | Modal element hidden |
| 203 | X-11: Company localization override page loads | `h1, h2, .page-title` not found |
| 204 | X-12: Language edit mode page accessible | `h1, h2, .page-title` not found |
| 205 | Y-01: Notification center page loads successfully | `h1, h2` first is hidden |
| 206 | Y-02: Notification center displays notification list | `h1, h2` first is hidden |
| 207 | Y-05: Clicking notification bell navigates to center | `h1, h2` first is hidden |
| 208 | Y-06: Mark notification as read | `h1, h2` first is hidden |
| 201 | X-06: Calendar day names appear in Hebrew | Hebrew day names not found in body text |
| 178 | Mobile hamburger menu is visible at small viewport | `#mobileNavToggle` click timeout |

---

## SKIPPED_TEST (24 failures)

**Root Cause:** Tests were explicitly skipped or conditionally skipped because prerequisite data was not available. The S-series (S-02 through S-21) were all skipped because the S-01 API key setup test failed (it depends on logging in as a user that does not exist), causing all dependent REST API tests to be skipped. Setup-03 is intentionally skipped (needs hierarchy API). C-08/C-09 are skipped because no pending join requests exist.

| # | Test Name | Reason |
|---|---|---|
| 0 | Setup-03: Create QA companies (skipped) | Intentionally skipped -- hierarchy API needed |
| 2 | Setup-05: Verify Organization page loads | Conditionally skipped |
| 15 | C-08: Owner approves a pending join request | Skipped -- no pending join requests |
| 16 | C-09: Owner rejects a join request | Skipped -- no pending join requests |
| 142 | S-02: GET /api/v1/shifts/1 | Skipped -- API key not obtained (S-01 failed) |
| 143 | S-03: GET /api/v1/chores | Skipped -- API key not obtained |
| 144 | S-04: GET /api/v1/time-off-requests | Skipped -- API key not obtained |
| 145 | S-05: GET /api/v1/swap-requests | Skipped -- API key not obtained |
| 146 | S-06: GET /api/v1/on-duty | Skipped -- API key not obtained |
| 147 | S-07: GET /api/v1/users | Skipped -- API key not obtained |
| 148 | S-08: GET /api/v1/notifications | Skipped -- API key not obtained |
| 149 | S-09: GET /api/v1/analytics/summary | Skipped -- API key not obtained |
| 150 | S-10: GET /api/v1/audit-logs | Skipped -- API key not obtained |
| 151 | S-11: POST /api/v1/feedback | Skipped -- API key not obtained |
| 152 | S-12: POST /api/v1/shifts | Skipped -- API key not obtained |
| 153 | S-13: POST /api/v1/chores | Skipped -- API key not obtained |
| 154 | S-14: POST /api/v1/time-off-requests | Skipped -- API key not obtained |
| 155 | S-15: POST /api/v1/swap-requests | Skipped -- API key not obtained |
| 156 | S-16: POST /api/v1/on-duty | Skipped -- API key not obtained |
| 157 | S-17: PUT /api/v1/shifts/1 | Skipped -- API key not obtained |
| 158 | S-18: DELETE /api/v1/shifts/99999 | Skipped -- API key not obtained |
| 159 | S-19: PUT /api/v1/time-off-requests/1/approve | Skipped -- API key not obtained |
| 160 | S-20: Request with invalid API key returns 401 | Skipped -- API key not obtained |
| 161 | S-21: Request with no API key header returns 401 | Skipped -- API key not obtained |

---

## SELECTOR_MISMATCH (22 failures)

**Root Cause:** Tests use `locator('h1')` without `.first()`, and the page contains **two `<h1>` elements** -- one in the sidebar/header (e.g., `<h1 class="page-title">Owner Hub</h1>`) and another in the content area (e.g., with an emoji prefix like `<h1>Owner Hub</h1>`). Playwright's strict mode rejects ambiguous single-element locators that resolve to multiple elements, throwing "strict mode violation: resolved to 2 elements."

| # | Test Name | Error |
|---|---|---|
| 23 | D-11: Audit log page loads with entries | `.page-title` resolves to 2 h1 elements |
| 24 | D-12: Role templates are visible in grants management | `h1` resolves to 2 elements |
| 25 | D-13: Grant actions tab shows revoke capability | `h1` resolves to 2 elements |
| 33 | F-01: View existing blueprints for company | `h1` resolves to 2 elements |
| 38 | F-14: Delete blueprint used by instances | `h1` resolves to 2 elements |
| 39 | F-15: Delete blueprint used by programs blocked | `h1` resolves to 2 elements |
| 40 | F-16: Blueprint per-company isolation | `h1` resolves to 2 elements |
| 53 | G-17: Create program in Hir | `h1` resolves to 2 elements |
| 54 | G-18: Create program in Hitazmut | `h1` resolves to 2 elements |
| 55 | G-19: Create program for QA-Alpha | `h1` resolves to 2 elements |
| 56 | G-20: Program per-company isolation | `h1` resolves to 2 elements |
| 57 | H-01: Create master program template | `h1` resolves to 2 elements |
| 58 | H-02: Apply master to Tzafona | `h1` resolves to 2 elements |
| 59 | H-03: Apply master to Hir | `h1` resolves to 2 elements |
| 60 | H-04: Edit master program | `h1` resolves to 2 elements |
| 61 | H-05: Delete master program | `h1` resolves to 2 elements |
| 63 | J-08: Shift/User mode toggle changes view | Toggle link resolves to 3 elements |
| 64 | J-11: CapacityMode toggle is visible for Owner | Toggle link resolves to multiple elements |
| 171 | U-01: Owner dashboard renders with stats | `h1` resolves to 2 elements |
| 172 | U-02: Backup page renders with create button | `h1` resolves to 2 elements |
| 176 | U-08: System health page displays health checks | `h1` resolves to 2 elements |
| 177 | U-09: Feature flags page displays toggle switches | `h1` resolves to 2 elements |

---

## MISSING_TEXT (9 failures)

**Root Cause:** Tests expect specific text strings on the page body that do not match the actual seed data or page content. The Organization page does not display the expected molecule names, department names, or job type names. This is likely because the seed data uses different naming than what the tests expect, or the Organization page layout has changed.

| # | Test Name | Error |
|---|---|---|
| 4 | A-03: Seed creates 9 molecules with correct names | Expected "Shikma" not found on page |
| 5 | A-05: Seed creates 6 tech departments | Expected "Pie" not found on page |
| 6 | A-06: Seed creates 4 job types | Expected "Alhut" not found on page |
| 8 | A-09: Seed creates ShiftGroupings for Oren | Expected grouping names not in body text |
| 9 | A-10: Seed creates ShiftGrouping for Ella | Expected "HitazmutYeadim" not in body text |
| 10 | A-11: Seed creates ShiftGroupings for Gefen | Expected "HamasaKabah"/"Matot" not found |
| 13 | C-03: Duplicate email rejected (admin@local) | `.auth-alert--error` not found (no error shown) |
| 14 | C-04: Duplicate pending request rejected | `.auth-alert--error` not found |
| 32 | Public: Feedback page loads with form elements | `.feedback-container, main, .page-content` not found |

---

## COUNT_MISMATCH (11 failures)

**Root Cause:** Tests assert minimum element counts (e.g., `.toBeGreaterThanOrEqual(1)`) but find zero matching elements. This usually means the expected UI elements (add buttons, editable cells, file inputs, table rows) either use different class names than expected, or the page renders differently than the test anticipates.

| # | Test Name | Error |
|---|---|---|
| 3 | A-01: Fresh seed creates Project "Shifty" | Expected >= 3 rows but found 0 |
| 28 | Owner Backup: Create, download buttons exist | Expected >= 1 file input but found 0 |
| 46 | G-07: Edit program name | Expected >= 1 program rows but found 0 |
| 47 | G-08: Edit program weekly mask | Expected >= 1 program rows but found 0 |
| 48 | G-09: Edit program staffing | Expected >= 1 program rows but found 0 |
| 49 | G-10: Delete program (soft) | Expected >= 1 program rows but found 0 |
| 71 | K-04: Owner sees add-buttons on chore calendar cells | Expected >= 1 `.excel-calendar__add-btn` but found 0 |
| 76 | L-05: Owner sees add-buttons on on-call calendar cells | Expected >= 1 `.excel-calendar__add-btn` but found 0 |
| 82 | M-09: Overview cells have editable class for note editing | Expected >= 1 `.excel-calendar-cell--editable` but found 0 |
| 183 | V-05: Hierarchy page has quick action links | `a[href="/Admin/Companies"]` strict violation (2 elements) |
| 184 | V-06: Hierarchy detail page renders tree with drag-drop | `h1` strict violation (2 elements) |

---

## LOGIC_ERROR (10 failures)

**Root Cause:** Test logic issues where the test's assertion structure does not match the actual application behavior. Includes incorrect cookie setup, URL parameter assertions that do not account for the application using POST-based filters instead of query parameters, and JSON parsing errors when the API returns plaintext.

| # | Test Name | Error |
|---|---|---|
| 12 | B-12: RTL layout on Hebrew login | `addCookies` requires `url` or `path` property -- test missing cookie path |
| 77 | L-07: Selecting a DutyType filter updates the calendar | URL does not contain "DutyTypeFilter" -- app uses POST not GET params |
| 78 | L-10: Changing area selector navigates to different area | URL does not contain "AreaId" -- app uses POST not GET params |
| 81 | M-06: Users filter selector has active/inactive options | URL does not contain "UsersFilter=inactive" -- app uses POST not GET |
| 97 | P-05: API /Api/Calendar/GetShiftsData respects tenant scoping | Expected 200 but got 400 -- missing required parameters |
| 167 | T-07: GET /Api/OnDuty/GetEligibleUsers returns JSON | Expected status in [200, 400] but got 401 |
| 169 | T-10: Unauthenticated AJAX to /Api/SessionStatus returns 401 | JSON parse error -- API returns "Unauthorized" plaintext, not JSON |
| 173 | U-05: Database console: valid SELECT returns results | `hasResults` is false -- query may need different table/syntax |
| 181 | Zero JS console errors during page navigation sweep | JS error: `updateSelectedCount` setting null textContent |
| 210 | Z-08: No JavaScript console errors on main pages | Same JS error: `updateSelectedCount` setting null textContent |

---

## SERVER_500 (3 failures)

**Root Cause:** Three Organization admin pages return HTTP 500 server errors. These are genuine server-side crashes, likely caused by null reference exceptions or missing database relationships in the Organization module's page models.

| # | Test Name | Error |
|---|---|---|
| 185 | V-07: JobTypes page lists job types | Server error 500 at `/Admin/Organization/JobTypes` |
| 186 | V-08: ShiftGroupings page loads | Server error 500 at `/Admin/Organization/ShiftGroupings` |
| 189 | V-11: Grants page loads with permissions data | Server error 500 at `/Admin/Organization/Grants` |

---

## PRODUCT_BUG (7 failures)

**Root Cause:** These represent genuine application issues where the behavior differs from expected/correct behavior.

| # | Test Name | Error |
|---|---|---|
| 19 | D-05: Toggle user active/inactive status | `form[action*="Toggle"]` not found -- toggle UI may have changed |
| 20 | D-06: Reset user password | `form[action*="ResetPassword"]` not found -- reset UI may have changed |
| 168 | T-09: Unauthenticated request to GetShiftsData returns 401 | Returns 200 instead of 401 -- **security issue: unauthenticated access to calendar API** |
| 170 | T-16: Anonymous request to GetChoresData returns 401 | Returns 200 instead of 401 -- **security issue: unauthenticated access to chores API** |
| 197 | W-10: Public chores page accessible without login | Redirects to `/Auth/Login` -- public page requires auth |
| 198 | W-11: Public on-duty page accessible without login | Redirects to `/Auth/Login` -- public page requires auth |
| 181/210 | JS console errors during navigation | `updateSelectedCount` TypeError on Admin/Users -- null reference in JS |

---

## Priority Recommendations

### P0 -- Fix Immediately (Security)
1. **Unauthenticated API access (T-09, T-16):** `/Api/Calendar/GetShiftsData` and `/Api/Calendar/GetChoresData` return 200 to unauthenticated requests. These internal API endpoints should require cookie authentication.

### P1 -- Fix Before Release (Server Crashes)
2. **Server 500 errors (V-07, V-08, V-11):** Three Organization admin pages crash. Fix the null reference or missing data issues in JobTypes, ShiftGroupings, and Grants page models.
3. **JS TypeError on Admin/Users (181, 210):** `updateSelectedCount` tries to set `.textContent` on null element. Fix the selector or add null guard.

### P2 -- Fix Test Infrastructure (Unblocks 106 tests)
4. **User creation dropdown mismatch (Setup-04):** The `NewRole` dropdown option values do not match what the test sends via `selectOption`. Align test role values with actual `<option>` values in the dropdown. Fixing this single issue will unblock **82 MISSING_USER tests + 20 SKIPPED_TEST tests = 102 tests**.

### P3 -- Fix Test Selectors (Unblocks 65 tests)
5. **Duplicate `<h1>` strict mode violations:** Add `.first()` to `h1` locators, or use `getByRole('heading', { name: '...' })` instead. Fixes 22 SELECTOR_MISMATCH tests.
6. **Hidden `.page-title` heading:** Use `locator('h1:visible').first()` or target the content-area heading specifically. Fixes 43 HIDDEN_ELEMENT tests.

### P4 -- Public Page Auth
7. **Public chores/on-duty pages require login (W-10, W-11):** Decide whether these should be public (add to anonymous endpoints) or update tests to expect redirect.
