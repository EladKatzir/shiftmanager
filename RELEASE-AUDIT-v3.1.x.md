# ShiftManager v3.1.x — Release Readiness Assessment

## Release Decision
**CONDITIONAL GO** — Release is possible after fixing 4 P1 blockers and ~18 P2 items. The application is architecturally sound with strong security foundations, but contains shipped debug artifacts, dark-mode regressions, a broken dashboard metric, and localization gaps that must be addressed before production deployment.

## Executive Summary

**Overall Judgment:** ShiftManager v3.1.x is a mature, well-architected application with strong security practices (grant-based auth, tenant isolation, rate limiting, input validation, HMAC-based API keys). The codebase is large (131 PageModels, 233 POST handlers, 30 API endpoints, 132 services) and the test suite (339 tests, all passing) covers critical paths. However, the pre-release audit identified **4 P1 blockers** and **~18 P2 high-priority issues** across security, functionality, dark mode, localization, and RTL rendering.

**Major Strengths:**
- Comprehensive grant-based authorization system with 125 grant types
- Proper tenant isolation via EF query filters + `IBelongsToCompany` interface
- SignalR hub with per-group validation and rate limiting
- Robust login security (rate limiting, account lockout, PII masking)
- DatabaseConsole correctly uses read-only SQLite connection with semicolon rejection
- Backup/restore has path traversal protection
- Pre-migration backup on every startup
- All 339 tests pass with 0 warnings, 0 build errors
- Localization keys perfectly matched: 4,087 EN = 4,087 HE

**Major Weaknesses:**
- Shipped Figma debug artifacts (CSP relaxation + external scripts on auth pages)
- Calendar bottom-sheet completely broken in dark mode (~20 undefined CSS tokens)
- Manager dashboard "Unassigned Shifts" metric always shows 0 (critical functional bug)
- 40+ calendar CRUD error messages hardcoded in English
- Several undefined CSS custom properties causing silent visual regressions
- Invalid `[dir="rtl"] @keyframes` CSS — RTL form animation broken

**Systemic Risks:**
- CSS token drift: new components written against non-existent token names
- Hardcoded week start (Sunday) in multiple places, ignoring configurable `WeekStartDay`
- 3 auth pages load external Figma script (will fail on air-gapped deployment)

**Build/Test Status:** Build succeeds (0 warnings), 339/339 tests pass

**Confidence Level:** HIGH for code review. MODERATE for functionality (no browser testing). LOW for visual/RTL verification (code-level only).

---

## Pre-Flight Results

| Check | Status | Notes |
|---|---|---|
| App Accessible | NO | App not running at localhost |
| Repo Accessible | YES | Full access to all files |
| Build Status | PASS | 0 errors, 0 warnings |
| Test Status | PASS | 339/339 tests pass in 2.46s |
| Test Accounts | N/A | Not browser-tested |
| Locale Toggle | N/A | Not browser-tested |
| Theme Toggle | N/A | Not browser-tested |
| Browser Automation | N/A | App not running |

---

## Coverage Summary

| Metric | Count |
|---|---|
| Pages Discovered | 131 PageModels |
| Pages Code-Reviewed | 131 (all) |
| Pages Browser-Tested | 0 (app not running) |
| POST Handlers Discovered | 233 |
| POST Handlers Code-Reviewed | ~160 (across all agents) |
| API Endpoints Discovered | 30 |
| API Endpoints Code-Reviewed | 30 (all) |
| SignalR Hubs | 1 (CalendarHub) — fully reviewed |
| Services Reviewed | ~20 key services |
| CSS Files Reviewed | 15 (all) |
| JS Files Reviewed | 37 (all in wwwroot/js/) |
| .resx Keys EN | 4,087 |
| .resx Keys HE | 4,087 (matched) |
| Locales Covered | EN + HE (code-level) |
| Themes Covered | Light + Dark (code-level CSS review) |
| Roles Covered | All (code-level auth review) |

---

## Release Blockers (P0 + P1)

### P1-1: Figma Debug Scripts on Production Auth Pages
**Track:** Security
**Files:** `Pages/Auth/Login.cshtml:20`, `Pages/Auth/Signup.cshtml:21`, `Pages/Auth/Logout.cshtml:18`

All three auth pages contain:
```html
<!-- TEMPORARY: Figma capture script for design export -->
<script src="https://mcp.figma.com/mcp/html-to-design/capture.js" async></script>
```

On air-gapped deployment: network timeout on every auth page load, console errors. These pages don't use `_Layout.cshtml` (which already removed the script), so they were missed.

**Fix:** Remove all 3 `<script>` tags and their comments. (~2 min)

---

### P1-2: CSP Header Relaxed for Figma Debug Domains
**Track:** Security
**File:** `Program.cs:1401-1409`

```csharp
// TEMPORARY: Relaxed CSP for Figma design capture — REVERT AFTER CAPTURE
```

CSP allows `script-src` and `connect-src` from `*.figma.com` — a wildcard on an external domain. Comment explicitly says "REVERT AFTER CAPTURE."

**Fix:** Remove `https://*.figma.com https://mcp.figma.com` from script-src, img-src, and connect-src. (~2 min)

---

### P1-3: Calendar Bottom-Sheet Broken in Dark Mode
**Track:** Design / CSS
**File:** `wwwroot/css/calendar.css:3208-3517`

~300 lines of bottom-sheet CSS use entirely different token names than the design system:
- `var(--border-color, #dee2e6)` — undefined, falls back to light-mode hex
- `var(--bg-subtle, #f8f9fa)` — undefined, falls back to light-mode hex
- `var(--text-primary, #212529)` — undefined, dark text on dark background
- `var(--primary-light, #e3f2fd)`, `var(--primary-dark, #1565c0)` — undefined
- `var(--hover-bg, #e9ecef)` — undefined

In dark mode: light-colored text, borders, and backgrounds inside the dark sheet — unreadable.

**Fix:** Replace all undefined tokens with canonical system tokens: `--border`, `--surface-soft`, `--text`, `--text-muted`, `--surface`. (~30 min)

---

### P1-4: Manager Dashboard "Unassigned Shifts" Always Shows 0
**Track:** Functionality
**File:** `Pages/Home/Index.cshtml.cs:176-197`

`LoadManagerDataAsync` counts ShiftAssignments including `UserId == null` empty capacity slots. Since empty slots are counted as "assigned," the unassigned count is always 0. Per project memory: "Always filter `.Where(a => a.UserId != null)` when displaying assignments."

**Fix:** Add `.Where(a => a.UserId != null)` to the assignment query at line 178. (~5 min)

---

## Track 1 — Functionality Review

### Summary
Code-level review identified 1 P1 blocker (dashboard metric), several P2 functional gaps, and consistent authorization patterns. The grant system is robust but some pages have weaker page-level auth.

### Confirmed Findings

| ID | Severity | Finding | File |
|---|---|---|---|
| F1-1 | P1 | Unassigned Shifts metric always 0 | `Home/Index.cshtml.cs:176-197` |
| F1-2 | P2 | Unauthenticated POST on AllowAnonymous pages | `Public/Chores.cshtml.cs`, `Public/OnDuty.cshtml.cs` |
| F1-3 | P2 | RestoreChore API — no tenant scope check | `Api/Calendar/RestoreChore.cshtml.cs:22-50` |
| F1-4 | P2 | `GetStartOfWeek` hardcodes Sunday, ignores `WeekStartDay` config | `Shifts.cshtml.cs:254`, `ShiftAssignmentService.cs:998`, `Home/Index.cshtml.cs:83` |
| F1-5 | P2 | Home/Index and Admin/Index silently return empty page on invalid claim | `Home/Index.cshtml.cs:68-72`, `Admin/Index.cshtml.cs:71-74` |
| F1-6 | P2 | `HomeTypeService.DeleteHomeTypeAsync` orphans `HomeTypeOverride` rows | `Services/HomeTypeService.cs:72-83` |
| F1-7 | P2 | Silent exception swallow in HomeTypeService JSON deserialization | `Services/HomeTypeService.cs:520,534` |
| F1-8 | P3 | GetShiftsData API — no molecule access validation | `Api/Calendar/GetShiftsData.cshtml.cs:72-76` |
| F1-9 | P3 | Director SignalR group access too narrow | `Hubs/CalendarHub.cs:148-151` |
| F1-10 | P3 | Admin/Companies has only `[Authorize]`, no grant policy at page level | `Admin/Companies.cshtml.cs` |
| F1-11 | P3 | TOCTOU gap in chore cancel/delete | `Public/Chores.cshtml.cs:370-377` |
| F1-12 | P4 | Duplicate `class` attribute on 7 divs in Home/Index | `Home/Index.cshtml:169,203,225,251,273,295,335` |

---

## Track 2 — Design / Aesthetics / UX Review

### Summary
The design token system (`tokens.css`) is comprehensive with full dark mode, but new components use wrong token names. Two button systems coexist. Several WCAG accessibility gaps exist.

### Confirmed Findings

| ID | Severity | Finding | File |
|---|---|---|---|
| D2-1 | P1 | Bottom-sheet dark mode broken (see P1-3) | `calendar.css:3208-3517` |
| D2-2 | P2 | Sidebar logout button: `--error`/`--error-soft` undefined | `navigation.css:856,860` |
| D2-3 | P2 | Avatar fallback: `--text-secondary` undefined | `components.css:2975` |
| D2-4 | P2 | `--radius` undefined in home-types.css | `home-types.css:16,44` |
| D2-5 | P2 | Hardcoded colors in home-types.css | `home-types.css:53-54` |
| D2-6 | P2 | `[dir="rtl"] @keyframes formEntry` — invalid CSS, silently ignored | `auth.css:690-694` |
| D2-7 | P2 | Dark-mode FOUC: `data-theme="light"` hardcoded in HTML | `_Layout.cshtml:49` |
| D2-8 | P2 | Notification bell `<a>` has no `aria-label` | `_Layout.cshtml:793` |
| D2-9 | P2 | Dark-mode focus ring nearly invisible (`--focus-ring: #1A2D42` on `#1A2332`) | `tokens.css:215` |
| D2-10 | P2 | Legacy `.btn` in site.css overrides design-system `.btn` from components.css | `site.css:275` |
| D2-11 | P2 | `--surface-strong` aliased to `var(--text)` in compat layer (text color as background) | `site.css:32` |
| D2-12 | P3 | `--text-subtle` fails WCAG AA in dark mode (~3.5:1 contrast) | `tokens.css:143` |
| D2-13 | P3 | ~30 hardcoded hex colors in site.css | `site.css` (multiple) |
| D2-14 | P3 | z-index: 999999999 bypasses token scale | `site.css:1805,1834` |
| D2-15 | P3 | Emoji nav icons inconsistent with Lucide icon system | `_Layout.cshtml:428-499` |
| D2-16 | P3 | Hover-only dropdown not keyboard accessible | `site.css:154` |
| D2-17 | P3 | Most modals lack `role="dialog"` and `aria-modal` | Multiple pages |
| D2-18 | P4 | `console.log` statements in production layout | `_Layout.cshtml:115,273` |
| D2-19 | P4 | Duplicate RTL rule blocks (7 component pairs) | `rtl.css` |

---

## Track 3 — Localization / RTL Review

### Summary
Localization infrastructure is mature (4,087 matched keys). Major gap: 40+ calendar CRUD error messages are hardcoded English. RTL handling exists but has an invalid `@keyframes` scoping bug.

### Confirmed Findings

| ID | Severity | Finding | File |
|---|---|---|---|
| L3-1 | P2 | 40+ calendar CRUD errors hardcoded English (returned as JSON) | `Calendar/Table.cshtml.cs` (throughout) |
| L3-2 | P2 | Lockout countdown JS uses hardcoded Hebrew/English, not .resx | `Auth/Login.cshtml:250-262` |
| L3-3 | P2 | Signup JS uses raw `.Value.Trim()` instead of `Json.Serialize()` (XSS risk via loc overrides) | `Auth/Signup.cshtml:356,446,456,468,479,489,499` |
| L3-4 | P2 | HomeTypes confirm dialog hardcoded English | `Admin/HomeTypes/Index.cshtml:90` |
| L3-5 | P2 | `home-type-calendar.js` locale hardcoded to `he-IL` + English strings | `home-type-calendar.js:24,33-34,52,96` |
| L3-6 | P2 | `calendar-realtime.js` connection indicator hardcodes `direction:rtl` in JS | `calendar-realtime.js:469` |
| L3-7 | P3 | language-edit-mode.js: 5 hardcoded English alert()/confirm() | `language-edit-mode.js:283,364,415,454,470` |
| L3-8 | P3 | localization-attributes.js: 2 hardcoded English alerts | `localization-attributes.js:213,273` |
| L3-9 | P3 | AuditLog action/entity strings are raw English from database | `Admin/AuditLog.cshtml:182,186,193` |
| L3-10 | P4 | DatabaseConsole error strings not localized (Owner-only) | `Owner/DatabaseConsole.cshtml.cs:55,65,72` |

---

## Track 4 — Code Review / Implementation Review

### Codebase Inventory

| Category | Count |
|---|---|
| PageModels | 131 |
| POST Handlers | 233 |
| API Endpoints (Razor Pages) | 30 |
| API Controllers (v1) | 10+ |
| SignalR Hubs | 1 (CalendarHub) |
| Services | 132 |
| JS Files | 37 |
| CSS Files | 15 |
| Models | 50+ |
| .resx Files | 2 (EN, HE-IL) |
| Test Files | 36 |
| Automated Tests | 339 |

### Confirmed Defects

| ID | Severity | Finding | File |
|---|---|---|---|
| C4-1 | P2 | HMAC secret hardcoded fallback: `"ShiftManager-ApiKey-HMAC-v1-Default"` | `ApiAuthenticationMiddleware.cs:392` |
| C4-2 | P2 | `int.Parse` on claims (4 locations) — will throw on missing claims | `UnreadNotificationCountViewComponent.cs:27`, `Companies.cshtml.cs:386`, `DutyRotation/Index.cshtml.cs:145`, `Molecules/Index.cshtml.cs:148` |
| C4-3 | P2 | AuditLog potential cross-tenant leak (verify `IBelongsToCompany`) | `Admin/AuditLog.cshtml.cs:64-145` |
| C4-4 | P2 | Production config contains real domain: `7108dev.d8200.mil` | `appsettings.Production.json:52,59` |
| C4-5 | P3 | Test data seeders run in all environments (no env guard) | `Program.cs:1272-1330` |
| C4-6 | P3 | Fire-and-forget Task.Run with scoped services in Signup | `Auth/Signup.cshtml.cs:311-326` |
| C4-7 | P3 | DatabaseConsole LoadTablesAsync uses shared EF connection | `Owner/DatabaseConsole.cshtml.cs:132-152` |
| C4-8 | P3 | `calendar-realtime.js` interval never cleaned up in dispose() | `calendar-realtime.js:536-540` |
| C4-9 | P3 | Dead private method `EnsureHomeInstanceAsync` | `HomeTypeService.cs:488-508` |
| C4-10 | P3 | Duplicate `opacity` declaration | `auth.css:97-103` |

---

## Cross-Cutting Root Cause Clusters

### Cluster 1: Figma Debug Artifacts Not Reverted
- **Symptoms:** CSP relaxation, external scripts on auth pages
- **Affected:** `Program.cs`, `Login.cshtml`, `Signup.cshtml`, `Logout.cshtml`
- **Cause:** Figma design capture workflow left debug artifacts
- **Severity:** P1
- **Fix Order:** 1st — remove 4 references (~5 min)

### Cluster 2: CSS Token Drift in New Components
- **Symptoms:** `--error`, `--error-soft`, `--radius`, `--text-secondary`, `--border-color`, `--bg-subtle`, `--text-primary`, `--primary-light`, `--primary-dark` all undefined
- **Affected:** `navigation.css`, `components.css`, `home-types.css`, `calendar.css` (bottom-sheet)
- **Cause:** New components written against Bootstrap/non-existent token names
- **Severity:** P1 (bottom-sheet) + P2 (others)
- **Fix Order:** 2nd — systematic token replacement (~45 min)

### Cluster 3: Hardcoded English in JSON API Responses
- **Symptoms:** 40+ English error messages in calendar CRUD, confirmation dialogs, overlap warnings
- **Affected:** `Calendar/Table.cshtml.cs` (primary), other POST handlers
- **Cause:** JSON API responses not routed through IStringLocalizer
- **Severity:** P2
- **Fix Order:** 3rd — route through localizer (~2 hours)

### Cluster 4: Hardcoded Week Start Day
- **Symptoms:** Dashboard stats, eligible user hours, calendar default date all use Sunday
- **Affected:** `Home/Index.cshtml.cs`, `Calendar/Shifts.cshtml.cs`, `ShiftAssignmentService.cs`
- **Cause:** Private `GetStartOfWeek` methods ignore `WeekStartDay` config
- **Severity:** P2
- **Fix Order:** 4th — replace with `TimeHelpers.WeekStart()` calls (~15 min)

### Cluster 5: ShiftAssignment Dual-Purpose Filtering
- **Symptoms:** Dashboard metric always 0, potential miscounts elsewhere
- **Affected:** `Home/Index.cshtml.cs:176-197`
- **Cause:** Query includes `UserId == null` empty capacity slots
- **Severity:** P1
- **Fix Order:** 5th — add `.Where(a => a.UserId != null)` (~5 min)

---

## Opportunities / Ideas

| ID | Category | Title | Impact | Effort |
|---|---|---|---|---|
| O-1 | Maintainability | CSS token linting — validate all `var()` refs resolve | HIGH | LOW |
| O-2 | Security | Environment guard for test data seeders | MEDIUM | LOW |
| O-3 | Security | HMAC secret startup validation (reject default in prod) | HIGH | LOW |
| O-4 | Maintainability | `ClaimsPrincipal.GetUserIdOrDefault()` extension method | MEDIUM | LOW |
| O-5 | Architecture | Move CSP to `appsettings.json` configuration | MEDIUM | LOW |
| O-6 | Quality | Automated dark mode Playwright screenshot tests | HIGH | MEDIUM |
| O-7 | UX | Dark-mode FOUC fix — server-render theme from cookie | MEDIUM | LOW |
| O-8 | Accessibility | Add `aria-label` to all icon-only buttons and links | HIGH | MEDIUM |
| O-9 | Accessibility | Fix dark-mode focus ring contrast | HIGH | LOW |
| O-10 | Architecture | Migrate `text-align: left/right` to `text-align: start/end` | MEDIUM | MEDIUM |
| O-11 | Maintainability | Deduplicate RTL CSS (~7 duplicate rule blocks) | LOW | LOW |
| O-12 | UX | Standardize on BEM button system (remove legacy `btn-primary`) | MEDIUM | MEDIUM |
| O-13 | Performance | Parallelize 4 sequential grant checks in Shifts.cshtml.cs OnGet | LOW | LOW |

---

## Recommended Fix Order

| # | Item | Effort | Impact |
|---|---|---|---|
| 1 | Remove Figma scripts from 3 auth pages (P1-1) | 2 min | Blocker |
| 2 | Revert CSP to production values (P1-2) | 2 min | Blocker |
| 3 | Fix `UserId != null` filter in dashboard metric (P1-4) | 5 min | Blocker |
| 4 | Fix bottom-sheet CSS tokens for dark mode (P1-3) | 30 min | Blocker |
| 5 | Fix `--error`/`--error-soft` in navigation.css (D2-2) | 2 min | Visual |
| 6 | Fix `int.Parse` on claims (C4-2, 4 locations) | 15 min | Correctness |
| 7 | Fix `[dir="rtl"] @keyframes` in auth.css (D2-6) | 10 min | RTL fix |
| 8 | Fix undefined CSS tokens (D2-3, D2-4, D2-5) | 15 min | Visual |
| 9 | Remove real domain from production config (C4-4) | 2 min | Security |
| 10 | Replace hardcoded `GetStartOfWeek` with configurable (F1-4) | 15 min | Correctness |
| 11 | Add auth guard to Public page POST handlers (F1-2) | 10 min | Defense-in-depth |
| 12 | Fix notification bell aria-label (D2-8) | 2 min | Accessibility |
| 13 | Fix dark-mode focus ring contrast (D2-9) | 5 min | Accessibility |
| 14 | Localize calendar CRUD error messages (L3-1) | 2 hrs | Localization |
| 15 | Add HMAC secret startup validation (O-3) | 5 min | Security |
| 16 | Add environment guard to test seeders (O-2) | 5 min | Security |

**Total estimated effort for P1 fixes:** ~40 minutes
**Total estimated effort for P1 + critical P2 fixes:** ~4 hours
**Total estimated effort for all recommended fixes:** ~6 hours

---

## Evidence Archive

All findings are code-derived (app was not running for browser testing).

| Evidence Type | Reference |
|---|---|
| Build output | `dotnet build` — 0 errors, 0 warnings |
| Test output | `dotnet test` — 339/339 passed in 2.46s |
| CSP finding | `Program.cs:1401` — comment says "TEMPORARY" |
| Figma scripts | `Login.cshtml:20`, `Signup.cshtml:21`, `Logout.cshtml:18` |
| HMAC secret | `ApiAuthenticationMiddleware.cs:392` |
| int.Parse on claims | `UnreadNotificationCountViewComponent.cs:27` |
| CSS token drift | `calendar.css:3208-3517`, `navigation.css:856,860`, `home-types.css:16,44` |
| Production config | `appsettings.Production.json:52,59` |
| Dashboard metric | `Home/Index.cshtml.cs:176-197` — missing `UserId != null` filter |
| Invalid RTL CSS | `auth.css:690-694` — `[dir="rtl"] @keyframes` invalid |
| Focus ring | `tokens.css:215` — `#1A2D42` on `#1A2332` background |
| Calendar errors | `Calendar/Table.cshtml.cs` — 40+ hardcoded English strings |
| Week start | `Shifts.cshtml.cs:254`, `ShiftAssignmentService.cs:998` |
| resx count | EN: 4,087 keys, HE: 4,087 keys (match) |

### Review Agents Used
- Track 4A (Auth/Security): 54 tool uses, 139K tokens, 245s
- Track 4B (Implementation/Quality): 86 tool uses, 145K tokens, 385s
- Track 1+3 (Functionality/Localization): 68 tool uses, 167K tokens, 439s
- Track 2 (Design/UX/CSS): 54 tool uses, 124K tokens, 333s

---

## Final Verdict

### Launch is conditionally safe.

**Must fix before launch (P1 — ~40 min):**
1. Remove Figma capture scripts from Login, Signup, Logout pages
2. Revert CSP header to remove `*.figma.com` domains
3. Fix manager dashboard "Unassigned Shifts" metric (add `UserId != null` filter)
4. Fix calendar bottom-sheet dark mode (replace ~20 undefined CSS tokens)

**Should fix before launch (P2, highest-impact subset — ~3 hrs):**
5. Fix `--error`/`--error-soft` undefined tokens in sidebar logout button
6. Replace `int.Parse` with `TryParse` on claims (4 locations)
7. Fix `[dir="rtl"] @keyframes` invalid CSS in auth page
8. Remove real domain references from `appsettings.Production.json`
9. Fix hardcoded `GetStartOfWeek` to use configurable `WeekStartDay`
10. Fix dark-mode focus ring contrast
11. Add notification bell `aria-label`

**Should fix soon after launch (P2, localization):**
12. Localize 40+ calendar CRUD error messages (2 hrs of work)
13. Fix `home-type-calendar.js` hardcoded locale
14. Fix Signup page JS localization injection risk

**Can wait but should watch during rollout:**
- Test data seeders creating test users in all environments
- Director SignalR real-time updates (silently broken for cross-company viewing)
- AuditLog cross-tenant visibility (needs model verification)

**The P1 fixes are straightforward (~40 minutes of work).** Once those are addressed, the application is ready for its target air-gapped IIS deployment.

---

*Report generated 2026-03-20 by pre-release audit using 4 parallel review agents*
*Model: Claude Opus 4.6 (1M context) + 4× Claude Sonnet 4.6 agents*
*Total review: 262 tool uses, 575K tokens, ~24 min wall clock*

**Deferred Items:**
- Browser-based testing of all 131 pages was not performed (app not running). All findings are code-derived.
- Visual/RTL rendering verification requires running the app in both locales.
- SignalR real-time behavior needs live testing.
- Form submission flows need end-to-end browser verification.
- Full coverage matrix with per-page status was not generated due to the inability to browser-test.
