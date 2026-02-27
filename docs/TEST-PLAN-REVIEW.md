# Test Plan Review Report
*Reviewed 2026-02-26. Reviewer: Claude Code (claude-sonnet-4-6)*

---

## Review Summary

**Overall Assessment**: The comprehensive test plan is a well-structured document that correctly maps all 53 feature inventory sections to test IDs and demonstrates strong awareness of the existing test infrastructure. The plan is implementable as written for the majority of modules. However, it contains several accuracy problems that will cause concrete failures on first run — most critically, mismatched test user credentials, a configuration conflict between the plan's serial-execution requirement and the actual parallel `playwright.config.js` settings, and an incomplete helper function reference. These must be resolved before teams begin writing new module tests.

**Coverage Score**:
- Feature sections covered: **53/53 (100%)** — every inventory section has at least one test ID assigned
- Checkbox items verifiably reachable: **~720/781 (~92%)** — the ~61-item shortfall is concentrated in startup-safety sub-items (50.6–50.10) and background-service internals (42.x) that the plan correctly marks "not testable via UI"
- Critical gaps found: **4**
- Accuracy issues found: **11**
- Feasibility concerns found: **6**
- Consistency issues found: **5**

**Top-priority fixes before implementation begins**:
1. Fix test user credential mismatches (Issue 1) — will cause immediate login failures
2. Fix playwright.config.js parallel/serial conflict (Issue 8) — will cause inter-module data race conditions
3. Expand helper function reference list (Issue 7) — new module authors will miss required functions
4. Correct B-09 feature mapping (Issue 5) — misleads reviewers about existing Griffin coverage
5. Resolve "AlhutSoldier"/"Employee" template inconsistency (Issue 9) — user creation may fail

---

## Completeness Audit

### Sections with complete or good coverage

Sections 1–23, 26–31, 34, 36–41, 43–46, 48–49 all have well-defined test IDs mapped to specific feature inventory sub-items.

### Section-by-section gaps

**Section 1.3 (Rate Limiting)** — The plan maps RL-01 to RL-04 as new xUnit tests and B-15 as an E2E brute-force test. Missing: no E2E test distinguishes the *per-account* rate limit key (`login:account:{email}`) from the *per-IP* key (`login:ip:{ip}`). Both are defined in `Services/RateLimitingService.cs`. B-15 covers only the combined observable outcome; consider splitting it into B-15a (per-IP) and B-15b (per-account, using same IP different accounts).

**Section 4.1 (Grant-Based Authorization)** — AA-U05 tests that self-scoped grants (all-null scope) are skipped by `HasGrantWithScopeAsync`, correctly flagged as a known pitfall in MEMORY.md. However, there is no corresponding E2E test that places a user with only self-scoped grants and verifies they are denied a scoped action in the UI. This gap exists at the integration level.

**Section 9.3 (Shift Assignment operations)** — The plan maps the 13+ POST handlers of `Table.cshtml.cs` but two handlers are unrepresented by any test ID: `OnPostAssignUserToSlotAsync` and `OnPostUpdateShiftMetadataAsync`. These are omitted from the gap analysis table (plan line 166) and the Module J new tests table (plan lines 362–376).

**Section 13.5 (Vacation Approval Workflow)** — N-13 and N-14 test the approval rules page but navigate to `/Admin/Settings/ApprovalRules`. The feature inventory (line 732) confirms the file is at `Pages/Admin/Settings/ApprovalRules.cshtml.cs`, making this URL plausible but unverified. If the Razor Pages routing convention gives a different URL, both tests will get 404s and produce misleading failures.

**Section 20.6 (Director Management — Reassign)** — AD-16 and AD-17 cover assign and revoke. The feature inventory (section 20.6, line 1110) lists `OnPostReassignAsync` as a third sub-feature. No test ID covers director reassignment.

**Section 42 (Background Services)** — Correctly marked "not testable via UI." A unit-level DI registration test could verify that `EmailBackgroundProcessor`, `DailyNotificationJob`, and `DatabaseBackupService` are registered as `IHostedService`. This is not present in the plan.

**Sections 50.6–50.10 (Startup Safety sub-items)** — `StartupSafetyTests.cs` covers SS-01 through SS-05 (items 50.1–50.5). Items 50.6 (HMAC secret validation), 50.7 (data protection key check), 50.8 (pre-migration backup), 50.9 (SQLite WAL mode), and 50.10 (feature flag cache warming) are uncovered. Item 50.6 is security-relevant.

**Section 52.1 (Configurable Seeding from appsettings.json)** — The plan maps section 52 to Module A (A-01 to A-12). Module A tests seed data *presence* in the UI; it does not test `SeedingOptions` where molecules/companies are provided via `appsettings.json` configuration. This is a distinct scenario with no coverage.

---

## Accuracy Issues

### Issue 1 — Test user password mismatch (Confidence: 100%)

**Location**: Plan lines 89–95 (Test User Matrix)

The plan lists all six test users with password `TestOwner123!`. The actual `fixtures/test-users.json` (lines 108–152) assigns separate passwords:

| Email | Plan Password | Actual Password (fixtures) |
|-------|--------------|---------------------------|
| `test.director@shifty.test` | `TestOwner123!` | `TestDirector123!` |
| `test.manager@shifty.test` | `TestOwner123!` | `TestManager123!` |
| `test.assigner@shifty.test` | `TestOwner123!` | `TestAssigner123!` |
| `test.member@shifty.test` | (listed as `test.employee@`) | `TestMember123!` |

Additionally, the plan uses email `test.employee@shifty.test` but the fixtures file uses `test.member@shifty.test` for the employee role. These are different addresses.

**Impact**: Every new test that calls `login(page, 'test.director@shifty.test', 'TestOwner123!')` will fail immediately on first run.

**Fix**: Update plan lines 89–95 to match `fixtures/test-users.json` exactly, or update both files to agree on canonical values.

---

### Issue 2 — GrantServiceTests.cs already covers material in proposed GrantAuthorizationTests.cs (Confidence: 100%)

**Location**: Plan line 55 (existing xUnit table) and line 504 (new xUnit file proposal)

`GrantServiceTests.cs` exists at `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` and exercises grant authorization, scope matching, and hierarchy checking (confirmed in lines 1–60 of that file, which tests company-scoped grants, area-scoped grants, etc.). The plan proposes a *new* `GrantAuthorizationTests.cs` at `ShiftManager.Tests/UnitTests/Services/GrantAuthorizationTests.cs` with AA-U01 through AA-U07. Before creating this new file, verify that AA-U01 through AA-U07 are not already covered by existing tests in `GrantServiceTests.cs`. Duplicate test files create maintenance burden.

---

### Issue 3 — ShiftAssignmentServiceTests.cs scope is narrower than the plan claims (Confidence: 100%)

**Location**: Plan line 57 (xUnit coverage table)

The plan states `ShiftAssignmentServiceTests.cs` covers "9.3 Assignment logic" with ~15 tests. The actual file header reads: "Task 13.3: Shift Assignment by **JobType** — Verifies that shift eligibility respects JobType and ShiftGrouping rules." This covers *eligibility filtering*, not the full breadth of the 19 POST handlers listed in inventory section 9.3 (lines 487–506). The plan overstates existing unit test coverage for section 9.3.

---

### Issue 4 — Rank test files omitted from coverage matrix for section 11 (Confidence: 100%)

**Location**: Plan line 1035 (Coverage Matrix row for section 11)

Two existing rank test files — `MilitaryRankExtensionsTests.cs` at `ShiftManager.Tests/UnitTests/Models/` and `MilitaryRankIntegrationTests.cs` at `ShiftManager.Tests/IntegrationTests/` — both cover inventory section 11.9. These are listed in the xUnit coverage table (plan lines 62 and 68) but are absent from the coverage matrix row for section 11 at line 1035. The matrix should reference these files as existing coverage for section 11.9.

---

### Issue 5 — B-09 is mapped to feature 1.5 (Griffin) but actually tests feature 1.8 (ForgotPassword) (Confidence: 100%)

**Location**: Plan lines 246–248 (Module B existing tests table)

The plan lists:
- B-08: Feature 1.5 — "Griffin ADFS button rendering"
- B-09: Feature 1.5 — "Griffin disabled state"

The actual `module-b-authentication.spec.js` shows:
- B-08 (line 170): Tests `.auth-adfs__btn` — correctly maps to 1.5
- B-09 (line 188): Tests `ForgotPassword page has password inputs` by navigating to `/Auth/ForgotPassword` — this covers feature **1.8** (Forced Password Change), not 1.5

The plan's existing test table assigns the wrong feature reference to B-09. Any new tests added as B-13+ should not rely on B-09 as Griffin-disabled-state coverage.

---

### Issue 6 — `locked@test` user dependency not documented in Test User Matrix (Confidence: 100%)

**Location**: Plan lines 89–95 (Test User Matrix) vs. `module-b-authentication.spec.js` lines 112–120

B-05 depends on `locked@test` with password `Test1234!` existing from Setup-04. The test comment at line 112 acknowledges this. The plan's Test User Matrix does not list `locked@test` at all, making the dependency invisible to new developers working from the plan alone.

**Fix**: Add `locked@test / Test1234! / Employee` to the Test User Matrix with note "Required by B-05; created in 00-setup-test-data".

---

### Issue 7 — Helper function inventory in the plan is incomplete (Confidence: 100%)

**Location**: Plan line 81

The plan instructs: "Use existing helpers: `login`, `loginAsOwner`, `logout`, `navigateTo`, `saveEvidence`, `assertPageContains`, `assertMinCount`"

`production-qa-helpers.js` exports the following additional functions that new module tests will need but that the plan does not mention:

| Function | Used By (new modules) |
|----------|-----------------------|
| `navigateExpecting(page, url, statusOrRedirect)` | AE-07 (assert 403 on CSRF-less POST) |
| `waitForToast(page, text)` | AD-08/09/10, Y-09 (success confirmations) |
| `assertPageNotContains(page, text)` | Tenant isolation sub-tests in AA/AC |
| `saveApiEvidence(folder, filename, data)` | S-module extensions |
| `collectConsoleErrors(page)` | AE module (detect JS errors) |
| `reportBug(page, bug)` | Bug-reporting workflow |
| `createUser(page, userData)` | D extensions, AA setup |
| `TEST_USERS`, `TEST_PASSWORD`, `QA_COMPANIES` | Data constants across all modules |

Module authors who read only line 81 will not know `navigateExpecting` exists and may implement their own version or write incorrect assertions.

---

### Issue 8 — `playwright.config.js` is set to parallel but the plan requires serial execution (Confidence: 100%)

**Location**: Plan line 99 ("Tests run sequentially in a single worker") vs. `qa-automation/playwright.config.js` lines 15–16

`playwright.config.js` sets `fullyParallel: true` and `workers: 2`. The plan explicitly states production-qa modules must run sequentially because "later modules depend on data created by earlier ones." Module B could start before `00-setup-test-data` finishes. Module J depends on blueprints created by Module F.

`test.describe.serial()` inside a file enforces serial *within* that file, but `fullyParallel: true` in the config causes *spec files* to run in parallel across workers. The new modules AA–AL inherit this problem.

**Impact**: Random intermittent failures when two modules run in parallel and one depends on data created by the other.

**Fix**: The project already has `playwright.production-qa.config.js` with `fullyParallel: false, workers: 1`. Ensure the plan explicitly states that ALL production-qa tests (including new AA–AL modules) must run via `npm run qa:production` (which uses `playwright.production-qa.config.js`), never via bare `npm test`.

---

### Issue 9 — "AlhutSoldier" template for Test Employee conflicts with actual helpers (Confidence: 80%)

**Location**: Plan line 94

The plan lists the Test Employee's RoleTemplate as "AlhutSoldier". The feature inventory (section 4.3) confirms "AlhutSoldier" as a seeded template name. However, `production-qa-helpers.js` TEST_USERS entries for all employee accounts (lines 322–350) use `template: 'Employee'`. The string `'Employee'` does not appear in the seeded template list from the inventory. Either:
(a) `'Employee'` is an alternate key for AlhutSoldier, or
(b) `'Employee'` is an invalid template key that silently fails to assign the template on user creation

This inconsistency — plan says "AlhutSoldier", helpers say `'Employee'` — must be resolved by checking `RoleTemplateSeed.cs` for the actual `Key` property value.

---

### Issue 10 — AD-13/14 incorrectly cited as covering section 11 in coverage matrix (Confidence: 80%)

**Location**: Plan line 1035 (Coverage Matrix, section 11 row)

The coverage matrix row for section 11 includes "AD-13/14" as test references. AD-13 tests "Add on-duty type" and AD-14 tests "Delete on-duty type" — both under `Admin/Config` (section 20.5). These configure on-duty *types*, not the on-duty *calendar operations* in section 11. Section 11 calendar operations (11.2 CRUD, 11.4 QuickAdd, etc.) are covered by L-01 through L-18, not AD-13/14. The coverage matrix entry for section 11 is misleading.

---

### Issue 11 — Module AF has no timeout annotations for tests requiring >30 seconds (Confidence: 100%)

**Location**: Plan lines 647–649 (AF-05, AF-06)

AF-05 ("SignalR reconnects after disconnect") requires waiting for reconnection with exponential backoff (0, 2s, 5s, 10s, 30s — so up to ~47 seconds to attempt reconnection). AF-06 ("Fallback polling activates when SignalR unavailable") requires waiting 60 seconds for the polling interval. Both exceed the 30-second global timeout in `playwright.config.js` line 87. Neither test specifies `test.setTimeout()`. Both will time out on first run.

---

## Feasibility Concerns

### Concern 1 — B-16: Cannot clear HttpOnly cookie via JavaScript (Confidence: 90%)

**Location**: Plan line 259

B-16 proposes: "Clear auth cookie via JS" to test session monitoring auto-redirect. The auth cookie is `shiftmgr.auth` with `HttpOnly` flag (confirmed by B-19 in the same plan). JavaScript cannot access or delete HttpOnly cookies via `document.cookie`. The step "Clear auth cookie via JS" will silently do nothing.

**Fix**: Replace the JS cookie clear with `await page.context().clearCookies()`. Add `test.setTimeout(90000)` to wait for the polling interval.

---

### Concern 2 — AF-02: Two-tab SignalR test has undefined actor model and no concrete assertion (Confidence: 85%)

**Location**: Plan lines 644–645

AF-02 opens two contexts, assigns in context A, expects "Tab B receives update via SignalR." The plan does not specify:
- Whether to use `page.newPage()` (same session) or `browser.newContext()` (separate sessions)
- What DOM change or network event constitutes "receives update"
- Any explicit wait mechanism

Module R's concurrency tests (verified in `module-r-concurrency.spec.js` lines 21–31) use `browser.newContext()` for separate sessions. AF-02 should follow this pattern. The assertion needs to wait for a specific DOM selector to update, e.g., a new assignment name to appear in the second context's calendar cell.

---

### Concern 3 — AF-06: 60-second polling wait exceeds 30-second test timeout (Confidence: 90%)

**Location**: Plan lines 648–649

AF-06 blocks WebSocket and waits for the 60-second polling fallback. The `playwright.config.js` global timeout is 30 seconds. This test is guaranteed to time out as written.

**Fix**: Either annotate `test.setTimeout(120000)`, or use `page.route()` to intercept the polling `fetch` calls (which fire every 60s when disconnected) and assert they are attempted, instead of waiting for the full interval.

---

### Concern 4 — L-23: Feature flag and prerequisite on-duty type not set up before the test (Confidence: 80%)

**Location**: Plan lines 411–412

L-23 enables `FF_ENFORCE_RANK_ELIGIBILITY` and tries to assign a soldier to officer duty. The plan has no setup step that enables this flag or creates a duty type requiring officer rank. The flag is likely off by default; the "officer duty" type must exist. Without setup, the test will assign the soldier successfully (because no enforcement exists) and then have no error to assert.

---

### Concern 5 — B-19: HttpOnly detection via `document.cookie` is trivially unfalsifiable (Confidence: 85%)

**Location**: Plan line 261

B-19 evaluates `document.cookie` and asserts `shiftmgr.auth` is not readable. HttpOnly cookies are *always* invisible to `document.cookie` by the browser specification. This assertion will pass even if the HttpOnly flag is accidentally removed from the server configuration, because the browser enforces the restriction regardless. The test cannot detect a regression.

**Fix**: Inspect the `Set-Cookie` response header during login:
```javascript
const loginResponse = await page.waitForResponse(
  r => r.url().includes('/Auth/Login') && r.request().method() === 'POST'
);
const setCookie = loginResponse.headers()['set-cookie'] || '';
expect(setCookie).toMatch(/HttpOnly/i);
expect(setCookie).toContain('shiftmgr.auth');
```

---

### Concern 6 — AF-07: Server-side SignalR group rejection has no client-observable signal (Confidence: 80%)

**Location**: Plan lines 649–650

AF-07 tests that a company-A user cannot join a company-B SignalR group. When `ValidateGroupAccessAsync` rejects a join, it simply does not add the connection to the group. The client receives no error event. The plan's assertion ("join rejected or no data received") requires a second actor: a company-B user must make a change and the test must assert that company-A's tab does *not* receive the update. This negative assertion pattern needs a defined timeout and must use `page.on('websocket', ...)` frame capture or a polling negative assertion. The test steps as written are not implementable without this clarification.

---

## Consistency Issues

### Issue 1 — New module files cover multiple unrelated feature areas

**Location**: Plan lines 562–599 (Module AD), 733–753 (Module AJ)

Existing modules each map to a single cohesive area (Module K = Chore Calendar, Module L = On-Duty). New modules mix unrelated sections:
- Module AD: sections 5.9 + 20.1–20.11 + 32.1–32.2 + 33.1–33.2 (hierarchy settings, admin pages, diagnostics, job types)
- Module AJ: sections 38.1 + 41.1–41.6 + 43.1 + 44.1 + 48.1–48.6 + 51.1–51.3 (six disparate categories)

This makes test failures harder to isolate and evidence folders confusing.

**Recommendation**: Split AD into `module-ad-admin-config.spec.js` (sections 20.1–20.8) and `module-ad2-org-pages.spec.js` (sections 20.9–20.11, 33.x). Split AJ into `module-aj-performance.spec.js` (sections 41, 43) and `module-aj2-view-components.spec.js` (section 48).

---

### Issue 2 — P0 priority assigned to page-load tests that do not match P0 definition

**Location**: Plan lines 479–480 (AA-01, AA-02 as P0)

The P0 definition at line 148 states: "Auth, login, logout, core CRUD, data integrity, tenant isolation, assignment operations, grant enforcement." AA-01 ("Role Templates page loads") and AA-02 ("View role template details") are admin UI page-load tests, not P0 by this definition. AA-09 (seeded grants correct), AA-13 (assign grant), and AA-14 (revoke grant) are correctly P0.

---

### Issue 3 — Coverage matrix uses inconsistent ID formats across rows

**Location**: Plan lines 1025–1077 (Coverage Matrix)

Some rows list specific xUnit test IDs (e.g., "RL-01 to RL-04"), while others say only "existing" with no IDs (e.g., section 9 row: "CalendarDateValidationTests (existing), ShiftAssignmentTests (existing)"). The matrix should be uniform: every row that has existing xUnit tests should list the test IDs, or the matrix should consistently omit IDs for existing tests and list them only for new ones.

---

### Issue 4 — Evidence folder numbering restarts at 29 but existing folders go up to 28

**Location**: Plan lines 473, 519, 543, etc.

New module evidence paths start at `ProductionReady/29-grants-roletemplates/` continuing through `ProductionReady/40-coverage-sweep/`. This is a reasonable continuation of the existing `ProductionReady/28-every-button/` directory. The `saveEvidence()` helper auto-creates directories. This is noted as consistent — no change required.

---

### Issue 5 — xUnit pattern description omits ITenantResolver mocking

**Location**: Plan line 83

The plan documents "InMemory database with `Guid.NewGuid()` name" as an xUnit pattern. Reading the existing test files confirms this is accurate (`GrantServiceTests.cs` line 23, `AnnouncementServiceTests.cs` line 22). The pattern documentation is correct. However, the plan does not mention that some existing tests also mock `ITenantResolver` to return a fixed company ID — this is a required pattern when testing services that use `IBelongsToCompany` query filters. New test authors will need this pattern for any service that reads from tenant-scoped tables.

---

## Missing Cross-Cutting Tests

### 1. CSRF protection not tested for new POST handlers in new modules

The plan covers CSRF in AE-06 (token presence) and AE-07 (raw POST rejection). However, no test in the new module tables (AA through AL) makes a raw POST to their specific handlers without a CSRF token to verify 400/403 is returned.

Missing CSRF coverage:
- `OnPostAssignGrantAsync` and `OnPostRevokeGrantAsync` (Module AA)
- `OnPostCreateAsync` for announcements and directors (Module AD)
- Any new API endpoints added in Module AC (team calendars) — these use REST conventions and may not have CSRF protection in the same way

**Recommendation**: Add one test per new module that submits the module's most sensitive POST action without `__RequestVerificationToken` and asserts 400 or 403.

---

### 2. Tenant isolation not tested for new data-creating operations

Module P (14 tests) covers tenant isolation for the core calendar features. New modules create data via new endpoints not covered by Module P:

- Module AA: `OnPostAssignGrantAsync` — no test verifies that a grant assigned via this handler is invisible to a different company's user
- Module AD: `OnPostCreateAsync` (announcements) — no test verifies announcement company-scoping
- Module AC: Team calendar API — AC-01 to AC-08 test CRUD for one user but not cross-company data leakage (user A from company X cannot GET user B's calendar from company Y)

**Recommendation**: Add one tenant isolation sub-test per new module that creates persistent data: "login as company A, create entity, login as company B, assert entity not visible."

---

### 3. Hebrew/English not verified for any new UI page

The Cross-Cutting Test Matrix at plan lines 979–989 lists specific pages for Hebrew/English verification, but none of the 12 new modules (AA–AL) are represented. New pages without Hebrew/RTL tests include:
- Role Templates edit page (Module AA)
- Admin pages: Analytics, AuditLog, Announcements, Config, Directors (Module AD)
- My Profile, My Settings (Module AL — AL-11/12)
- Feedback page (Module AL — AL-15/16)

Module X has 12 localization tests targeting pre-existing pages. The plan should either extend Module X to include new pages or require each new module to include one Hebrew/RTL test.

---

### 4. Role-based access not tested for any new protected page

The RBAC matrix at plan lines 992–1006 does not include any new pages. Missing:
- `/Owner/Hub/RoleTemplates` — should be Owner-only; Module AA has no test verifying a Manager is denied
- `/Admin/Analytics` — role access unspecified in the plan
- `/Admin/Directors` — director assignment is sensitive; no test verifies an Employee cannot call `OnPostAssignAsync`

Module Q's 33 tests cover pre-existing pages only. The plan should note that Module Q must be extended, or each new module should include one negative RBAC test (e.g., assert Employee gets 403 on the module's most sensitive action).

---

## Recommended Additions

### Addition 1 — Fix the Test User Matrix (BLOCKING)

**File**: Plan lines 89–95

Update the matrix to match `fixtures/test-users.json` and `production-qa-helpers.js`:

| User | Email | Password | Role | Source |
|------|-------|----------|------|--------|
| Owner (seeded) | `admin@local` | `admin123` | Owner | Seeded |
| Test Owner | `test.owner@shifty.test` | `TestOwner123!` | Owner | fixtures |
| Test Director | `test.director@shifty.test` | `TestDirector123!` | Director | fixtures (corrected) |
| Test Manager | `test.manager@shifty.test` | `TestManager123!` | Manager | fixtures (corrected) |
| Test Assigner | `test.assigner@shifty.test` | `TestAssigner123!` | Assigner | fixtures (corrected) |
| Test Employee | `test.member@shifty.test` | `TestMember123!` | Employee | fixtures (email corrected) |
| Test Trainee | `test.nogrants@shifty.test` | `TestNoGrants123!` | Trainee | fixtures |
| Locked | `locked@test` | `Test1234!` | Employee | helpers (required by B-05) |

---

### Addition 2 — Clarify Playwright config for new modules

The plan must explicitly state that ALL production-qa tests (including new AA–AL modules) must be run via:
```bash
npm run qa:production
```
which uses `playwright.production-qa.config.js` (`fullyParallel: false, workers: 1`), and NEVER via bare `npm test` (which uses the parallel config).

---

### Addition 3 — Add SS-06 for HMAC secret validation (security-relevant)

In `StartupSafetyTests.cs`, add:
```
SS-06 | Missing HMAC secret in production mode throws at startup | P1
```
This covers inventory item 50.6 which has security implications (API authentication would be broken without the HMAC secret).

---

### Addition 4 — Add AA-25: Tenant isolation for grant assignment

```
AA-25 | Grant assigned in company A not visible to company B user | E2E | P0
Steps:
1. Login as Owner
2. Assign a grant (Shift.View) to emp.tz.alhut@test (Tzafona company)
3. Logout
4. Login as emp.hir.alhut@test (Hir company)
5. Navigate to /Admin/Organization/Grants
6. Assert that the Tzafona user's grant is not listed
Expected: Grant not visible across company boundary
```

---

### Addition 5 — Add CSRF rejection tests to Module AE (one per sensitive new POST)

Add `AE-12`:
```
AE-12 | OnPostAssignGrantAsync rejects POST without CSRF token | E2E | P0
Steps:
1. Login as Owner, get base URL and session cookie
2. Make a raw fetch POST to /Owner/Hub/Grants with grant data but no __RequestVerificationToken header
3. Assert response status is 400 or 403
```

---

### Addition 6 — Add timeout annotations to AF-05 and AF-06

Both tests must explicitly set `test.setTimeout`:
- AF-05: `test.setTimeout(60000)` (reconnection backoff up to ~47 seconds)
- AF-06: `test.setTimeout(120000)` (polling fires at 60-second interval)

---

### Addition 7 — Fix B-19 to detect HttpOnly flag in response header

Replace the `document.cookie` check with:
```javascript
const loginResponse = await page.waitForResponse(
  r => r.url().includes('/Auth/Login') && r.request().method() === 'POST'
);
const setCookie = loginResponse.headers()['set-cookie'] ?? '';
expect(setCookie).toMatch(/HttpOnly/i);
expect(setCookie).toContain('shiftmgr.auth');
await saveEvidence(page, EVIDENCE, 'B-19-httponly-flag.png');
```

---

### Addition 8 — Fix B-16 to use Playwright cookie API instead of JavaScript

Replace "Clear auth cookie via JS" with:
```javascript
await page.context().clearCookies();
```
Add `test.setTimeout(90000)` and wait up to 65 seconds for the session-check.js polling redirect.

---

## Recommended Removals / Changes

### Change 1 — Correct B-09 feature mapping in Module B existing-tests table

Update plan line 248 to:
- B-09: Feature **1.8** — "ForgotPassword page has password inputs"

If a genuine Griffin-disabled-state test is needed, add B-23:
- B-23: Feature 1.5 — "Griffin button hidden when Griffin disabled" — navigate to login, verify `.auth-adfs__btn` is absent when feature flag `FF_GRIFFIN_ENABLED=false`.

---

### Change 2 — Restructure Module AD into two files

Split the 29-test module-ad into:
- `module-ad-admin-config.spec.js`: AD-01 to AD-20 (sections 20.1–20.8, 32.x diagnostics)
- `module-ad2-org-pages.spec.js`: AD-21 to AD-28 (sections 20.9–20.11, 33.x job types)

---

### Change 3 — Resolve "AlhutSoldier" vs "Employee" template key

Check `Data/SeedData/RoleTemplateSeed.cs` for the actual `Key` property value of the employee-level template. Update plan line 94 and `production-qa-helpers.js` TEST_USERS entries to use the same value.

---

### Change 4 — Downgrade AA-01 and AA-02 from P0 to P1

Per the plan's own P0 definition (line 148), admin page-load tests do not qualify as P0. AA-09, AA-13, and AA-14 are correctly P0.

---

### Change 5 — Expand helper function reference at plan line 81

Replace the single-line list with a reference to the full exports of `production-qa-helpers.js`, or add a table of all exported functions with their signatures. At minimum, call out `navigateExpecting`, `waitForToast`, `assertPageNotContains`, and `collectConsoleErrors` as functions new module authors will frequently need.

---

*Review complete. Total issues: 11 accuracy + 6 feasibility + 5 consistency + 4 cross-cutting gaps = 26 findings. 8 recommended additions. 5 recommended changes.*
