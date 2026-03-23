# ShiftManager v3.1.x — Release Readiness Assessment

## Release Decision
**NO-GO** — Two deployment blockers and multiple P1 security/reliability findings must be fixed before shipping. After fixes, re-test and upgrade to CONDITIONAL GO.

## Executive Summary

ShiftManager v3.1.x is a **mature, well-architected application** that demonstrates strong fundamentals across its 158 Razor Pages, 151 services, and comprehensive grant-based authorization system. The application successfully handles dual-language (English/Hebrew RTL), dark/light themes, real-time SignalR updates, and complex scheduling workflows. **However, deep code and deployment analysis revealed critical issues that would break real-world air-gapped deployments and expose cross-tenant scheduling data.**

**Major Strengths:**
- Solid grant-based authorization with proper page/handler protection
- All 363 unit tests pass (0 failures)
- Clean build (0 warnings, 0 errors)
- Comprehensive System Health monitoring page
- Well-organized Admin Hub and Owner Hub dashboards
- Smooth language switching with full RTL support
- Calendar assignment flow works correctly with real-time SignalR updates
- Strong empty-state messaging throughout
- Good error handling on login (generic "Invalid credentials" message)
- Skip-to-content accessibility link on all pages
- Automatic database backup on startup with integrity checks
- Tenant isolation via query filters is comprehensive (35+ filters, SECURITY-AUDITED annotations)
- Login security: rate limiting, account lockout, PII masking, PBKDF2/SHA256/100k iterations
- No `int.Parse` on claims — all use `int.TryParse` consistently
- DatabaseConsole uses read-only SQLite connection + semicolon rejection
- Middleware pipeline ordering is correct

**Major Weaknesses — BLOCKING:**
- **DEPLOYMENT BLOCKER:** HTTPS redirect active by default in production — completely breaks HTTP-only air-gapped deployments
- **DEPLOYMENT BLOCKER:** Shipped production config contains client-specific military domain secrets and weak passwords
- **P1 SECURITY:** Calendar API endpoints (GetShiftsData, GetChoresData, GetOnCallData, GetOverviewData) allow any authenticated user to query ANY molecule's scheduling data — cross-tenant data leakage
- **P1 RELIABILITY:** Database restore while app is running causes corruption risk with no restart mechanism
- **P1 RELIABILITY:** `busy_timeout=5000` only set on startup connection, not per-connection — production gets `busy_timeout=0` causing immediate SQLITE_BUSY errors

**Major Weaknesses — NON-BLOCKING:**
- Quick Info widget overlaps page content on every page (recurring UX blocker)
- Debug logging visible in production console on every page
- Branding inconsistency ("Shift Manager" vs "Shifty" vs "מנהל משמרות")
- `.Result` sync-over-async in _Layout.cshtml can deadlock under IIS
- Public signup enabled by default in production seeds
- Health checks hardcode `app.db` path, ignoring configurable connection string
- Manual backup doesn't use VACUUM INTO (inconsistent with auto-backup)
- START_HERE.bat password validation is a no-op
- Feature flag count discrepancy in Owner Hub

**Systemic Risks:**
- Widget overlap pattern affects all pages — root cause likely z-index/positioning
- Debug console output on every page navigation (20+ log lines per page)
- Sidebar clipping in collapsed state across pages

**Pilot Risks:**
- Duplicate company names in dropdowns ("כלל צוותי" appears 8+ times)
- Developer-facing language in some UI elements ("RestHours, WeeklyCap")
- Inline password setting in Admin/Users table

**Test/Build Status:**
- Build: PASS (0 warnings, 0 errors)
- Tests: 363/363 PASS (0 failures, 0 skipped)
- App startup: ~9.4s with automatic migration, seeding, and backup

## Pre-Flight Results

| Check | Status | Notes |
|-------|--------|-------|
| App accessible | ✅ | https://localhost:5001 |
| Repo accessible | ✅ | Git repo with 35+ modified files on `update` branch |
| Build | ✅ | 0 warnings, 0 errors |
| Tests | ✅ | 363/363 pass in 2s |
| Browser automation | ✅ | Playwright MCP working |
| Test accounts | ✅ | 6 standard + 53 QA users seeded |
| Hebrew locale | ✅ | Full RTL rendering confirmed |
| English locale | ✅ | Full LTR rendering confirmed |
| Dark mode | ✅ | Theme toggle functional |
| Light mode | ✅ | Default theme working |
| SignalR | ✅ | Real-time calendar updates confirmed |
| SQLite DB | ✅ | WAL mode, 5s busy_timeout, 2.27MB |

**Credentials tested:**
- Manager: test.manager@shifty.test / TestManager123!
- Owner: test.owner@shifty.test / TestOwner123!

## Coverage Summary

| Category | Discovered | Tested | Partially | Untested |
|----------|-----------|--------|-----------|----------|
| Pages (Razor) | 158 | 17 deep | ~30 via navigation | ~111 |
| PageModels | 131 | — | — | Code review by agent |
| Services | 151 | — | — | Code review by agent |
| JS assets | 38 | Console observed | — | — |
| CSS assets | 15 | Visual verification | — | — |
| Resource files | 2 | Language switching | — | — |
| Roles tested | 7 available | 2 (Manager, Owner) | — | Employee, Director, Trainee, Assigner, AreaAdmin |
| Locales | 2 | Both tested | — | — |
| Themes | 2 | Both tested | — | — |

## Coverage Matrix

| Page | URL | Role | EN | HE | Light | Dark | Happy | Unhappy | Persist | AuthZ |
|------|-----|------|----|----|-------|------|-------|---------|---------|-------|
| Login | /Auth/Login | Anon | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ (empty+wrong pwd) | ✅ | ✅ |
| Logout | /Auth/Logout | Auth | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ✅ | ✅ |
| Onboarding | /My/Onboarding | Mgr | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Home/Dashboard | /Home | Mgr | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Calendar/Shifts | /Calendar/Shifts | Mgr | ✅ | ✅ | ✅ | ❌ | ✅ (assign) | ❌ | ✅ | ✅ |
| Calendar/Overview | /Calendar/Overview | Mgr | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ✅ | ✅ |
| Calendar/Chores | /Calendar/Chores | Mgr | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Calendar/OnCall | /Calendar/OnCall | Mgr | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Admin/Index | /Admin/Index | Mgr | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ |
| Admin/Users | /Admin/Users | Mgr | ✅ | ❌ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ |
| Requests | /Requests/Index | Mgr | ✅ | ❌ | ✅ | ❌ | ✅ (empty) | ❌ | ❌ | ✅ |
| My/Profile | /My/Profile | Mgr | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Owner/Index | /Owner/Index | Mgr | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ (403) | ❌ | ✅ |
| Owner/Hub | /Owner/Hub | Owner | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Owner/Backup | /Owner/Backup | Owner | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Owner/SystemHealth | /Owner/SystemHealth | Owner | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ❌ | ✅ |
| Public/OnDuty | /Public/OnDuty | Mgr | ✅ | ❌ | ✅ | ❌ | ✅ (redirect) | ❌ | ❌ | ✅ |

**Untested pages (requiring dedicated testing):** Calendar/Table, Calendar/Day, Calendar/Week, Calendar/Month, Assignments/Manage, Admin/Analytics, Admin/AuditLog, Admin/Config, Admin/Companies, Admin/Directors, Admin/Announcements, Admin/Organization/*, Admin/Settings/*, Admin/DutyRotation, Admin/HomeTypes, My/Settings, My/ApiKeys, My/Help, My/NotificationCenter, My/Requests, MyTeam/Index, Friends/Index, Game/Leaderboard, Director/*, Owner/DatabaseConsole, Owner/FeatureFlags, Owner/EmailConfig, Owner/EmailTemplates, Owner/Programs, Owner/Blueprints, Owner/MasterPrograms, Owner/LanguageManagement, Owner/Telemetry, Owner/DataLifecycle, Owner/LockedUsers, Owner/GameConfig, Owner/GriffinConfig, Owner/AreaConfig, Owner/Permissions, Auth/Signup, Auth/ForgotPassword, Requests/TimeOff/Create, Requests/Swaps/Create, Schedule/Index, Public/Chores, Public/Feedback, all API endpoints.

## Track 1 — Functionality Review

### Summary
Core workflows tested and functional. Login, calendar assignment, language switching, theme toggling, navigation, and authorization boundaries all work correctly. SignalR real-time updates confirmed. Empty states handled well.

### Confirmed Findings

**F-001: Calendar assignment triggers full page reload instead of in-place update (P3)**
- Category: Functionality / UX
- Where: Calendar/Shifts — assign user flow
- Steps: Click "+", select user, click "שבץ"
- Expected: Cell updates in-place via SignalR
- Actual: Full page navigation/reload occurs. SignalR `AssignmentChanged` event fires but page reloads
- Impact: Slower UX on large calendars. Happy path works.
- Workaround: None needed — works correctly, just slower than ideal

**F-002: Owner Hub "Settings" card shows "0 flags on" despite 8+ active feature flags (P2)**
- Category: Functionality / Data Display
- Where: /Owner/Hub → Settings card
- Expected: Count of enabled feature flags (FF_EXCEL_CALENDARS, FF_WIDGETS_ENABLED, etc.)
- Actual: Shows "0 flags on"
- Impact: Misleading for operators checking system configuration
- Evidence: Startup logs show 8 feature flags enabled

**F-003: Backup page says "No backups found" despite automatic backups running (P3)**
- Category: Functionality / Confusing UX
- Where: /Owner/Backup
- Expected: Show automatic backups created by DatabaseBackupService
- Actual: Only shows company-scoped manual backups, not system-level automatic ones
- Impact: Operators may think no backups exist when they do
- Evidence: Startup log shows "Database backup completed. File: app.db.backup-20260322-041614-startup"

**F-004: Backup page "Automatic Backups" text contradicts system behavior (P3)**
- Category: Functionality / Documentation
- Where: /Owner/Backup → Important Information section
- Text says: "Automatic Backups: Backups are created manually using the button above"
- Reality: System creates automatic backups on startup and daily at 3am
- Impact: Misleading for operators

### Release Blockers
None identified from functionality testing.

### Non-Blocking Issues
- OnCall calendar is read-only for Manager role (by design per grant system)
- /Public/OnDuty redirects to /Calendar/OnCall for authenticated users (by design)

## Track 2 — Design / Aesthetics / UX Review

### Summary
The application presents a polished, modern UI with good component consistency. Dark mode is well-implemented. The main aesthetic concerns are the persistent Quick Info widget overlap, sidebar clipping in collapsed state, and branding inconsistency.

### Confirmed Findings

**D-001: Quick Info widget overlaps page content on every page (P2)**
- Category: Design / UX Blocker
- Where: ALL authenticated pages — Home, Calendar, Admin, Owner, etc.
- Description: The floating "Quick Info" / "מידע מהיר" widget at the bottom of the page overlaps main content, particularly covering action buttons and content sections
- Evidence: Screenshots show widget covering "הבא" button on Onboarding, "People Management" on Admin Hub, content areas on all pages
- Impact: Content is partially obscured; on some pages buttons may be hard to click
- Severity: P2 — does not block core workflows but affects every page

**D-002: Sidebar labels clipped in collapsed state (P3)**
- Category: Design / Layout
- Where: All pages with collapsed sidebar (dark mode screenshot shows "MY SHI...", "MA...", "AD...")
- Expected: Icons only in collapsed state, or truncation that doesn't show partial text
- Actual: Partial text visible creating messy appearance
- Impact: Cosmetic — icons still navigable

**D-003: Branding inconsistency across the application (P2)**
- Category: Design / Trust Perception
- Where: Multiple locations
  - Login page heading: "Shift Manager" (EN) / "מנהל משמרות" (HE)
  - Sidebar logo text: "Shifty"
  - Page title: "Test's shifty"
  - Access Denied page: "Shift Manager"
  - Logo alt text: "Shifty Logo"
- Impact: Confusing product identity. Pilot users will notice.

**D-004: Debug console output visible on every page load (P2)**
- Category: Design / Production Polish
- Where: Browser console on ALL pages
- Observed: 20+ console.log lines per page including:
  - "✅ PHASE 19 + SUPER-MERGE: Shift Swap game loaded"
  - "🐛 DEBUG MODE: Performance logging enabled"
  - "[OK] Sidebar CSS loaded correctly"
  - "[Localization API] Initialized"
  - "[CacheManager] Initialized"
  - etc.
- Impact: Unprofessional if pilot users open dev tools. Also slight performance overhead.

**D-005: Admin/Users "Company Settings" description uses developer jargon (P3)**
- Category: Design / Language
- Where: /Admin/Index → Company Settings card
- Text: "RestHours, WeeklyCap, OnDuty types"
- Should be: "Rest periods, weekly hour limits, duty types" or similar user-friendly text

### Top Aesthetic Improvements Before Pilot
1. Fix Quick Info widget positioning to not overlap content (P2)
2. Resolve branding to one consistent name (P2)
3. Strip or gate debug console output behind a dev flag (P2)
4. Fix sidebar collapsed state label clipping (P3)
5. Replace developer jargon in Company Settings description (P3)

## Track 3 — Localization / RTL Review

### Summary
Localization is strong overall. Hebrew rendering is complete with proper RTL layout. Language switching works instantly and persists. The localization attributes system dynamically translates 7-11 keys per page. Minor issues exist with mixed-language data display and one typo.

### Confirmed Findings

**L-001: Double colon typo in Admin/Users deletion warning (P4)**
- Where: /Admin/Users → User Deletion Warning
- Text: "permanently remove::" (double colon)
- Should be: "permanently remove:"

**L-002: Hebrew entity names displayed in English UI (P4)**
- Where: Multiple pages in English mode
- Examples: Job type "אלחוט", Molecule "אורן", Company names "ארבל", "מחנות", etc.
- Assessment: These are DATA values, not UI labels. Expected behavior for a Hebrew-primary deployment.
- Impact: Mixed-language feel in English mode. Acceptable for pilot.

**L-003: Role template name "Mapotz" not translated in English UI (P3)**
- Where: /Admin/Users → Role column for Test Manager
- Displayed: "Mapotz" (Hebrew military term מפוץ)
- Impact: Confusing for English-speaking users. Role template names should have English equivalents.

**L-004: Job type dropdown prefix "190 - " visible in Admin/Users (P4)**
- Where: /Admin/Users → Add User → Job Type dropdown
- Shows: "190 - אלחוט", "190 - ב\"ר", etc.
- Impact: Internal identifier prefix leaking into UI

### Most Noticeable Localization/RTL Weaknesses Users Will Notice First
1. Mixed Hebrew data in English mode (entity names)
2. "Mapotz" role name without English translation
3. "190 -" prefix in Job Type dropdowns
4. Duplicate company names "כלל צוותי" appearing 8+ times in Owner dropdowns

## Track 4 — Code Review / Implementation Review

### Summary
Comprehensive code review by background agent (Opus 4.6, 174K tokens, 70 tool uses) covering all PageModels, API endpoints, services, middleware, SignalR hub, data access, seed data, and JS files. Found **2 P1 security issues** (cross-molecule data leakage), **4 P2 issues**, and **6 P3 issues**. Overall code quality is good with mature security patterns, but API endpoint scope validation is a critical gap.

### Codebase Inventory
- **Razor Pages:** 158 (.cshtml files)
- **PageModels:** 131 (.cshtml.cs files)
- **Services:** 151 (including interfaces)
- **Middleware:** 9 files
- **SignalR Hubs:** 1 (CalendarHub.cs)
- **JS assets:** 38 custom files
- **CSS assets:** 15 custom files
- **Resource files:** 2 (.resx)
- **Migrations:** 141
- **Test files:** 363 tests across 18 test classes
- **Unit test pass rate:** 100% (363/363)

### Confirmed Defects

**C-SEC-001: Calendar API endpoints lack molecule-access authorization (P1)**
- Category: Security — Cross-tenant data leakage
- Files: `Pages/Api/Calendar/GetShiftsData.cshtml.cs`, `GetChoresData.cshtml.cs`, `GetOnCallData.cshtml.cs`, `GetOverviewData.cshtml.cs`
- Description: These endpoints accept `moleculeId`/`areaId`/`companyId` as query parameters and return scheduling data. They require `[Authorize]` (any authenticated user) but do NOT verify the user has access to the requested molecule/area. A user from Company A (Molecule X) could query Molecule Y's data by changing the parameter.
- Expected: Validate user's grants or company membership includes the requested molecule.
- Actual: Any authenticated user can query any scope.
- Impact: **Confidentiality breach** — scheduling data leakage across organizational boundaries.
- Fix: Add scope validation in each endpoint's OnGet handler.

**C-SEC-002: DatabaseConsole query has no row limit (P2)**
- Category: Reliability / Security
- File: `Pages/Owner/DatabaseConsole.cshtml.cs` lines 97-117
- Description: Allows executing arbitrary SELECT queries with no row limit. `SELECT * FROM ShiftAssignments` on production could return millions of rows, consuming all memory and crashing the process.
- Fix: Add `LIMIT 1000` or equivalent row cap.

**C-SEC-003: GriffinCallback stores raw ADFS token in cookie (P2)**
- Category: Security — Defense in depth
- File: `Pages/Auth/GriffinCallback.cshtml.cs` line 98
- Description: Griffin ADFS token stored as-is in HttpOnly cookie. If extracted via server-side vulnerability, enables session hijacking.
- Fix: Store token server-side with opaque session reference in cookie.

**C-SEC-004: 27+ endpoints use IgnoreAntiforgeryToken relying on single X-Requested-With header check (P2)**
- Category: Security — CSRF
- Files: Multiple under `Pages/Api/Calendar/`, `Pages/Api/Hierarchy/`
- Description: Relies solely on `X-Requested-With: XMLHttpRequest` header for CSRF protection. Valid for current air-gapped deployment with SameSite=Lax cookies, but single-layer defense.
- Note: Acceptable for current deployment, but should be documented as intentional.

**C-SEC-005: OnDuty table has no tenant query filter (P2)**
- Category: Security — Cross-area visibility
- File: `Data/AppDbContext.cs` lines 539, 705
- Description: OnDuty is a "Global/Public Table" by design (area-scoped), but no implicit filtering means all on-duty assignments visible to all authenticated users unless consumer explicitly filters.

### Risks / Fragility

**C-R1: Telemetry rate limiter uses global lock (P3)**
- File: `Pages/Api/Telemetry.cshtml.cs` lines 312-348
- Impact: Serializes ALL rate limit checks under high telemetry volume.

**C-R2: CompanyIdInterceptor creates new scope per SaveChanges call (P3)**
- File: `Data/CompanyIdInterceptor.cs` lines 62-64
- Impact: Significant allocation overhead during bulk operations.

**C-R3: Hierarchy Create allows duplicate slugs — returns 500 instead of 409 (P3)**
- File: `Pages/Api/Hierarchy/Create.cshtml.cs` lines 72-197
- Impact: Poor error UX when duplicate names are created.

**C-R4: N+1 in GrantService.GetAccessibleCompanyIdsForGrantAsync (P3)**
- File: `Services/GrantService.cs` lines 189-267
- Impact: One query per grant per scope level for users with many grants.

**C-R5: CalendarHub LeaveCalendarGroup skips validation (P3)**
- File: `Hubs/CalendarHub.cs` lines 66-84
- Impact: Asymmetry with JoinCalendarGroup; extremely low risk.

**C-R6: HMAC secret stored in static mutable field (P3)**
- File: `Middleware/ApiAuthenticationMiddleware.cs` line 392
- Impact: Defense-in-depth concern; should use IOptions pattern.

### High-Confidence Implementation Findings

**C-001: Sensitive data logging enabled in production path (P2)**
- Category: Security / Configuration
- Where: Startup logs show "Sensitive data logging is enabled"
- Evidence: EF Core warning during startup
- Impact: May log PII in production if log files are accessible
- Fix: Ensure `EnableSensitiveDataLogging` is gated behind `IsDevelopment()`

**C-002: Debug/performance logging not gated by environment (P2)**
- Category: Performance / Security
- Where: wwwroot/js/shift-swap-game.js and other JS files
- Evidence: "🐛 DEBUG MODE: Performance logging enabled" on every page
- Impact: Performance overhead and information disclosure

**C-003: apple-mobile-web-app-capable deprecation warning (P4)**
- Category: Compatibility
- Where: All pages — meta tag in layout
- Console: `<meta name="apple-mobile-web-app-capable" content="yes">` deprecated
- Impact: Minor — cosmetic console warning

### Risks / Fragility

**C-R1: Disk space sensitivity — 2.5% → 0.3% drop during single session (P2)**
- Observed disk space dropping from 2.5% to 0.3% during audit session
- Risk: SQLite + automatic backups + logs could exhaust disk in air-gapped environment
- The critical disk space warning IS working, but the threshold and backup retention policy need review

**C-R2: SignalR "Connection failed" warnings during page navigation (P4)**
- Console shows `[CalendarRealtime] Connection failed: Cannot send data if the connection is not in the 'Connected' State`
- Occurs during page-to-page navigation as connections close
- Risk: Benign during navigation, but could confuse operators checking logs

## Track 5 — Pilot / Publication / Windows Air-Gapped Readiness Review

### Summary
Comprehensive deployment review by background agent (Opus 4.6, 140K tokens, 142 tool uses) inspecting Program.cs, publish output, web.config, appsettings, batch scripts, health checks, backup/restore, startup behavior, and IIS compatibility. Found **2 DEPLOYMENT BLOCKERS**, **3 PILOT BLOCKERS**, and **12 additional operational risks**. While the application has strong air-gapped fundamentals (all assets local, SQLite WAL, automatic backups), critical configuration issues would prevent successful deployment to real air-gapped Windows/IIS environments.

### Release Blockers

**T5-BLOCK-001: HTTPS redirect active by default — breaks HTTP-only air-gapped deployments (P0)**
- Category: DEPLOYMENT BLOCKER
- File: `Program.cs` line 1376
- Description: When `ASPNETCORE_ENVIRONMENT` is not set (default for publish), `EnableHttpsRedirection` defaults to `true`. The published `appsettings.Production.json` does NOT override this. Every HTTP request gets a 307 redirect to HTTPS. If IIS has no HTTPS binding (typical in locked-down military networks), **the application is completely inaccessible**.
- Fix: Add `"EnableHttpsRedirection": false` to `appsettings.Production.json`

**T5-BLOCK-002: Shipped production config contains client-specific military secrets (P0)**
- Category: DEPLOYMENT BLOCKER
- File: `FinalProductPublish/appsettings.Production.json`
- Contains: `"Email": "admin@d8200.mil"` (real military domain), `"TokenConsumerUrl": "https://7108dev.d8200.mil/Auth/GriffinCallback"` (deployment-specific URL), `"Password": "easteregg"` (weak seeded owner password), `"ApiKeyHmacSecret": "CHANGE-THIS-TO-A-RANDOM-64-CHAR-STRING"` (placeholder that passes startup check)
- Impact: Different pilot sites receive another unit's configuration.

### Pilot Blockers

**T5-PILOT-001: START_HERE.bat password validation is a no-op (P1)**
- Category: PILOT BLOCKER
- File: `FinalProductPublish/START_HERE.bat` line 158
- Description: Checks for `SEED_ADMIN_PASSWORD` in appsettings.json, but actual password key is `Seeding:Owner:Password`. The `findstr` always returns "not found", validation always passes — even with `admin123` password.

**T5-PILOT-002: Database restore while app running causes corruption (P1)**
- Category: PILOT BLOCKER
- File: `Pages/Owner/Backup.cshtml.cs` lines 155-159
- Description: Restore overwrites live DB via `FileStream` with `FileMode.Create` while EF Core connections and `DatabaseBackupService` hold open connections. Causes SQLITE_BUSY errors or WAL corruption. Success message says "restart required" but no mechanism to trigger restart.

**T5-PILOT-003: busy_timeout only set on startup connection, not per-connection (P1)**
- Category: PILOT BLOCKER
- File: `Program.cs` lines 513-517
- Description: `PRAGMA busy_timeout=5000` set once on a startup connection, then connection closed. SQLite PRAGMAs are per-connection. Every new EF Core connection uses `busy_timeout=0`, causing immediate `SQLITE_BUSY` errors under concurrent writes.
- Fix: Add `Busy Timeout=5000` to connection string directly.

### Publication Risks

**P-001: Debug output in production JS (P2)**
- Multiple JS files emit debug logging unconditionally
- Fix: Gate behind environment check or remove before publish

**P-002: Disk space exhaustion risk (P2)**
- Auto-backups + low-capacity workstations risk cascade to DB failure

**P-006: .Result sync-over-async in _Layout.cshtml (P2)**
- File: `Pages/Shared/_Layout.cshtml` line 45
- `ViewAsModeService.GetViewAsCompanyNameAsync().Result` — can deadlock under IIS in-process hosting when thread pool exhausted. Works in dev with Kestrel but fails under production load.

**P-007: Health checks hardcode `app.db` path (P2)**
- Files: `Services/HealthChecks.cs`, `Pages/Owner/SystemHealth.cshtml.cs`, `ViewComponents/SystemAlertsViewComponent.cs`
- When production uses `"Data Source=C:\\ShiftManager\\Data\\app.db"`, health checks report wrong file sizes, disk space for wrong drive, WAL size as zero.

**P-008: Manual backup doesn't use VACUUM INTO (P3)**
- File: `Pages/Owner/Backup.cshtml.cs` line 90
- Uses raw `FileStream.CopyToAsync` without WAL checkpoint. Auto-backup correctly uses `VACUUM INTO`.

**P-009: Public signup enabled by default in seeds (P3)**
- File: `Data/SeedData/FeatureFlagSeed.cs` line 45
- All flags including `FF_ALLOW_PUBLIC_SIGNUP` seeded as enabled. Should be disabled by default for military deployment.

### Pilot Risks

**P-003: Backup page UX mismatch (P3)**
- Operators see "No backups found" when auto-backups exist

**P-004: Duplicate company names in dropdowns (P3)**
- "כלל צוותי" appears 8+ times

**P-005: Default password warning on System Health (P3 — Pilot Positive)**
- Detection logic works well for pilot

**P-010: VERSION.txt has encoding corruption (P3)**
- File: `FinalProductPublish/VERSION.txt`
- Garbled Unicode: `âœ¨` instead of emoji, `Â©` instead of copyright. Windows notepad shows garbage.

**P-011: Production template shows Linux paths (P3)**
- File: `appsettings.Production.template.json` line 14
- Shows `/var/lib/shiftmanager/production.db` as example for Windows-only deployment.

**P-012: Missing Backups/ and DataProtection-Keys/ in publish output (P3)**
- IIS app pool identities may not have directory creation permissions on locked-down workstations.

**P-013: AllowedHosts wildcard in production config (P3)**
- File: `FinalProductPublish/appsettings.Production.json` line 17
- `"AllowedHosts": "*"` accepts any Host header — susceptible to host header injection.

**P-014: stdout logging disabled in web.config (P3)**
- `stdoutLogEnabled="false"` — if app fails to start under IIS, only diagnostic is startup-error.txt.

**P-015: EF Core 9 on .NET 8 — unsupported combination (P3)**
- Project targets net8.0 but uses EF Core 9.0.9. Works but unsupported per Microsoft's version matrix.

**P-016: QuestPDF community license revenue threshold (P4)**
- `LicenseType.Community` free only under $1M revenue. Military org may exceed this.

### Day-1 Support Risks
- Disk space alerts may trigger immediately on low-capacity workstations
- HTTPS redirect will completely block access if not pre-configured
- Backup confusion may generate operator questions
- Health check showing wrong data if DB path is customized
- Deadlock potential under concurrent IIS load

### Windows / IIS / Air-Gap Specific Concerns
- All assets bundled locally (Lucide icons, SignalR client, SortableJS) ✅
- No CDN dependencies detected ✅
- DataProtection keys stored locally ✅
- Graceful shutdown service registered ✅
- **HTTPS redirect must be disabled for HTTP-only deployments** ❌
- **busy_timeout must be in connection string** ❌
- **Missing directories need pre-creation or permissions** ❌

### What Might Break Only After Publish
- HTTPS redirect (immediate failure on HTTP-only IIS)
- `busy_timeout=0` causing SQLITE_BUSY under concurrent writes
- `.Result` deadlock in _Layout.cshtml under IIS thread pool pressure
- Health checks reporting wrong data for custom DB paths
- IIS app pool recycle causing SignalR disconnections
- Session cookie expiration behavior under IIS vs Kestrel

### Top Things That Could Embarrass Us During Pilot
1. Quick Info widget overlapping content on every page
2. Debug console output visible to anyone who opens F12
3. "Shift Manager" vs "Shifty" branding confusion
4. "0 flags on" in Owner Hub when flags are clearly active
5. Duplicate company names in dropdown
6. "No backups found" when backups exist

### Recommended Pre-Pilot Hardening Steps
1. Fix Quick Info widget z-index/positioning
2. Strip debug console output or gate behind env check
3. Standardize product branding
4. Fix feature flag count in Owner Hub
5. Reconcile backup page with actual auto-backup system
6. Review disk space thresholds and backup retention

## Cross-Cutting Root Cause Clusters

### Cluster 1: Quick Info Widget Positioning
- **Symptoms:** Widget overlaps content on all pages (Onboarding, Home, Admin, Calendar, Owner)
- **Affected areas:** All authenticated pages
- **Likely cause:** Fixed/absolute positioning without proper viewport boundary checking
- **Severity:** P2
- **Fix:** Adjust widget CSS positioning, add bottom margin to main content

### Cluster 2: Debug Output Leak
- **Symptoms:** Console.log on every page, "DEBUG MODE", "PHASE 19", sensitivity warning
- **Affected areas:** All pages (JS), startup (C# EF Core)
- **Likely cause:** Debug logging not gated behind environment/build checks
- **Severity:** P2
- **Fix:** Add environment check to JS debug logging, review EF config

### Cluster 3: Branding Inconsistency
- **Symptoms:** "Shift Manager", "Shifty", "מנהל משמרות", "shifty" in page titles
- **Affected areas:** Login, sidebar, page titles, error pages
- **Likely cause:** Incremental naming evolution without standardization pass
- **Severity:** P2
- **Fix:** Single search-and-replace standardization

### Cluster 4: Owner Hub Data Accuracy
- **Symptoms:** Feature flag count wrong, backup page misleading, seed data health ✗
- **Affected areas:** Owner Hub dashboard cards, Backup page
- **Likely cause:** Dashboard queries may be scoped differently than actual feature flag storage
- **Severity:** P2-P3
- **Fix:** Audit each Owner Hub card query against actual data

## Release Blockers (P0 + P1)

| ID | Sev | Track | Issue | Fix Effort |
|----|-----|-------|-------|------------|
| T5-BLOCK-001 | **P0** | T5 | HTTPS redirect breaks HTTP-only air-gapped deployments | Low |
| T5-BLOCK-002 | **P0** | T5 | Production config ships client-specific military secrets | Low |
| C-SEC-001 | **P1** | T4 | Calendar API endpoints expose cross-molecule scheduling data | Medium |
| T5-PILOT-001 | **P1** | T5 | START_HERE.bat password validation is a no-op | Low |
| T5-PILOT-002 | **P1** | T5 | Database restore while running causes corruption | Medium |
| T5-PILOT-003 | **P1** | T5 | busy_timeout=0 on all production EF Core connections | Low |

**Total: 2 P0 + 4 P1 = 6 release blockers. All must be fixed before shipping.**

## Non-Blocking Issues (P2-P4)

### Functionality (P2-P3)
| ID | Severity | Issue |
|----|----------|-------|
| F-002 | P2 | Owner Hub "0 flags on" discrepancy |
| F-003 | P3 | Backup page "No backups found" mismatch |
| F-004 | P3 | Backup page contradictory auto-backup text |
| F-001 | P3 | Calendar assignment full page reload |

### Design / Aesthetics / UX (P2-P4)
| ID | Severity | Issue |
|----|----------|-------|
| D-001 | P2 | Quick Info widget overlaps content globally |
| D-003 | P2 | Branding inconsistency |
| D-004 | P2 | Debug console output on all pages |
| D-002 | P3 | Sidebar labels clipped in collapsed state |
| D-005 | P3 | Developer jargon in Company Settings description |

### Localization / RTL (P3-P4)
| ID | Severity | Issue |
|----|----------|-------|
| L-003 | P3 | "Mapotz" role name not translated |
| L-001 | P4 | Double colon typo in deletion warning |
| L-002 | P4 | Hebrew entity names in English UI (data-level) |
| L-004 | P4 | "190 -" prefix in Job Type dropdowns |

### Code / Implementation (P2-P4)
| ID | Severity | Issue |
|----|----------|-------|
| C-001 | P2 | Sensitive data logging may be active in prod |
| C-002 | P2 | Debug logging not environment-gated |
| C-R1 | P2 | Disk space exhaustion risk |
| C-003 | P4 | apple-mobile-web-app-capable deprecation |
| C-R2 | P4 | SignalR connection warnings during navigation |

### Pilot / Publication / Air-Gapped (P2-P3)
| ID | Severity | Issue |
|----|----------|-------|
| P-001 | P2 | Debug output in production JS |
| P-002 | P2 | Disk space exhaustion risk |
| P-003 | P3 | Backup page UX mismatch |
| P-004 | P3 | Duplicate company names |

## Opportunities / Ideas

### O-001: Consolidate Quick Info widget into sidebar or collapsible panel
- **Category:** UX simplification
- **What:** Move on-call status info into sidebar or a non-overlapping panel
- **Why:** Eliminates the most widespread visual issue in the app
- **Impact:** High — affects every page
- **Effort:** Medium

### O-002: Add "Last Backup" timestamp to System Health page
- **Category:** Operability improvement
- **What:** Show when the last successful auto-backup occurred
- **Why:** Gives operators confidence without navigating to Backup page
- **Impact:** Medium — reduces day-1 support questions
- **Effort:** Low

### O-003: Add environment indicator to page footer or sidebar
- **Category:** Deployment safety
- **What:** Show "Development" / "Production" badge
- **Why:** Prevents confusion between dev and prod instances
- **Impact:** Low but prevents costly mistakes
- **Effort:** Low (data already available — startup log shows environment)

### O-004: Add English display names to role templates
- **Category:** Localization
- **What:** Role templates like "Mapotz" should have `DisplayNameEn` field
- **Why:** Makes English mode fully usable without Hebrew knowledge
- **Impact:** Medium for mixed-language teams
- **Effort:** Medium (schema change + seed data update)

### O-005: Dashboard welcome message time-of-day awareness
- **Category:** UX polish
- **What:** Already shows "בוקר טוב" (Good morning) — verify it changes throughout day
- **Why:** Nice touch that makes the app feel alive
- **Impact:** Low — already partially implemented
- **Effort:** Low

### O-006: Print stylesheet audit
- **Category:** Pilot readiness
- **What:** Test calendar print output quality (print.css exists)
- **Why:** Managers in IDF-style environments often print schedules
- **Impact:** Medium for pilot satisfaction
- **Effort:** Low (audit existing print.css)

## Recommended Fix Order

**PHASE 1 — Must fix before shipping (Blockers):**
1. **T5-BLOCK-001** — Add `"EnableHttpsRedirection": false` to production config (5 min)
2. **T5-BLOCK-002** — Sanitize production config: remove military-specific secrets, use placeholders (30 min)
3. **T5-PILOT-003** — Add `Busy Timeout=5000` to SQLite connection string (5 min)
4. **C-SEC-001** — Add molecule/scope validation to 4 calendar API endpoints (2-4 hours)
5. **T5-PILOT-001** — Fix START_HERE.bat findstr to check correct JSON key (15 min)
6. **T5-PILOT-002** — Add pre-restore safety: close DB connections, checkpoint WAL, add restart guidance (2-4 hours)

**PHASE 2 — Should fix before pilot (High-value):**
7. **P-006** — Replace `.Result` with `await` in _Layout.cshtml (30 min)
8. **D-001** — Fix Quick Info widget positioning (1 hour)
9. **D-004 / C-002 / P-001** — Strip debug console output (1 hour)
10. **P-007** — Fix health checks to use connection string for DB path (1 hour)
11. **D-003** — Standardize branding (30 min)
12. **P-009** — Disable public signup by default in seeds (15 min)

**PHASE 3 — Nice to have:**
13. **C-SEC-002** — Add row limit to DatabaseConsole
14. **P-008** — Use VACUUM INTO for manual backups
15. **F-002** — Fix feature flag count in Owner Hub
16. **P-010** — Fix VERSION.txt encoding
17. All P3-P4 items

## Evidence Archive

### Screenshots captured:
- `audit-login-hebrew.png` — Login page in Hebrew/RTL
- `audit-onboarding-hebrew.png` — Onboarding page showing widget overlap
- `audit-home-hebrew.png` — Dashboard in Hebrew
- `audit-calendar-shifts-hebrew.png` — Calendar/Shifts weekly view
- `audit-admin-english-dark.png` — Admin Hub in English/Dark mode

### Console observations:
- All pages: 20+ console.log entries per navigation
- Debug markers: "PHASE 19 + SUPER-MERGE", "DEBUG MODE"
- SignalR groups observed: shifts-1-1, chores-1, oncall-1, overview-32
- Session check: 10079 minutes remaining (7-day session)
- Localization: 7-11 keys fetched and applied per page

### Key behavioral observations:
- Login: Generic error message for wrong credentials ✅
- Empty submit: HTML5 validation catches ✅
- Assignment: Persists across pages (Overview confirms Shifts assignment) ✅
- Language switch: Instant, persists, full re-render ✅
- Dark mode: Instant, persists, good contrast ✅
- Owner pages: Correctly blocked for Manager role ✅
- SignalR: Connects and joins appropriate groups per page ✅
- Startup: 9.4s with migration, seeding, backup ✅

## Final Verdict

### Is launch safe?
**NO — not until 6 blockers are fixed.** Two P0 deployment blockers (HTTPS redirect, leaked military config) would prevent successful deployment. Four P1 issues (cross-molecule data leak, broken restore, broken busy_timeout, broken setup script) would cause security breaches and data integrity issues.

### Is pilot safe?
**After Phase 1 fixes (estimated 6-10 hours), YES with conditions.** The 6 blockers are all fixable with targeted changes. Once fixed, the application is functionally solid for a controlled pilot. Phase 2 items (widget overlap, debug output, branding, deadlock fix) should ideally be done before pilot for polish, but are not safety-critical.

### What must be fixed before launch (Phase 1):
1. Disable HTTPS redirect in production config
2. Sanitize production config — remove military secrets
3. Add busy_timeout to connection string
4. Add scope validation to 4 calendar API endpoints
5. Fix START_HERE.bat password check
6. Fix database restore safety

### What should be fixed before pilot (Phase 2):
7. Replace `.Result` sync-over-async in _Layout.cshtml
8. Fix Quick Info widget overlap
9. Strip debug console output
10. Fix health checks for custom DB paths
11. Standardize branding
12. Disable public signup by default

### What can wait:
- P3-P4 items (role name translation, typos, sidebar clipping, version file encoding)
- Untested pages in less-used areas (Game, Director, most Owner sub-pages)
- Performance optimizations (rate limiter lock, CompanyIdInterceptor scope, N+1 in GrantService)

### What deserves special watch during rollout/pilot:
- **SQLITE_BUSY errors** — even after connection string fix, monitor under concurrent load
- **Disk space** — monitor closely, especially with auto-backups on low-capacity machines
- **IIS deadlocks** — watch for random request hangs (`.Result` in layout)
- **SignalR reconnection** — watch for disconnection complaints during IIS app pool recycles
- **Cross-molecule queries** — monitor API logs for suspicious moleculeId access patterns after fix
- **Session length** — 7-day sessions observed; verify this is intentional for air-gapped environment

---
*Report generated: 2026-03-22*
*Auditor: Claude Opus 4.6 (1M context)*
*Model: Strongest model per CLAUDE.md policy for final correctness validation*
*Build: 0 warnings, 0 errors | Tests: 363/363 pass*
*Browser evidence: 17 pages deep-tested, 2 roles, 2 locales, 2 themes*
