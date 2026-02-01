# ShiftManager UI Overhaul - Production Complete Implementation Document

**Document Version:** 2.0 (PATCH Format)
**Date:** 2026-01-31
**Status:** Production Release Gate
**Author:** Claude (Opus 4.5)

---

# ==BASELINE (UNCHANGED)==

The baseline document is preserved in its entirety at:
`docs/UI-OVERHAUL-IMPLEMENTATION-COMPLETE.md`

**Baseline Summary (for reference):**
- Section A: 14 items (A.1.1 through A.5.5) - Plan completion work
- Section B: 15 items (B.1 through B.15) - Gaps outside plan
- Section C: Original Rumsfeld Matrix
- Section D: Original Release Readiness Checklist (54 criteria)

**Baseline Item ID Mapping:**
| Baseline Reference | New ID |
|-------------------|--------|
| A.1.1 Context Switcher Search | A-001 |
| A.1.2 Sidebar Collapse Persistence | A-002 |
| A.2.1 Scope Switcher Integration | A-003 |
| A.2.2 Calendar Responsive Grid | A-004 |
| A.2.3 Calendar Empty States | A-005 |
| A.2.4 Shift Badge Colors | A-006 |
| A.3.1 OnCallWidget Empty States | A-007 |
| A.3.2 Widget Collapse Persistence | A-008 |
| A.4.1 Admin Pages Tokenization | A-009 |
| A.4.2 Owner Pages Tokenization | A-010 |
| A.4.3 Calendar Pages Polish | A-011 |
| A.4.4 Remaining Pages | A-012 |
| A.5.1 WCAG AA Audit | A-013 |
| A.5.2 RTL Layout Polish | A-014 |
| A.5.3 Missing Localization | A-015 |
| A.5.4 Dark Mode Polish | A-016 |
| A.5.5 Playwright Test Suite | A-017 |
| B.1 prefers-reduced-motion | B-001 |
| B.2 Inline Styles Calendar | B-002 |
| B.3 Loading States | B-003 |
| B.4 Error States | B-004 |
| B.5 Icon System Migration | B-005 |
| B.6 Print Styles Verification | B-006 |
| B.7 Skeleton Loading | B-007 |
| B.8 Form Validation Errors | B-008 |
| B.9 Table Pagination | B-009 |
| B.10 Date/Time Localization | B-010 |
| B.11 Mobile Navigation | B-011 |
| B.12 Context Switcher Edge Cases | B-012 |
| B.13 Modal Focus Management | B-013 |
| B.14 Favicon/App Icons | B-014 |
| B.15 Brand Loading Animation | B-015 |

---

# ==ADDITIONS / EXTENSIONS (NEW)==

## EXTENSION: Baseline A-003 (Scope Switcher Integration)

**Location:** Baseline A.2.1
**Extension ID:** A-003-EXT

**Additional Acceptance Criteria:**
1. Backend service correctly filters data by scope (unit tests required)
2. Scope parameter passed as query string `?scope=mine|company|molecule|area`
3. Invalid scope values default to "mine" (not error)
4. Scope filtering respects user's grant boundaries (cannot view data outside grants)
5. API returns 403 if user requests scope beyond their grants
6. Performance: scope change completes in <500ms for 1000 records

**Additional Dependencies:**
- Backend: `ICalendarService.GetShiftsAsync(scope, userId, dateRange)`
- Grants: `IGrantService.GetMaxScopeForUser(userId, calendarType)`

**Rollback/Mitigation:**
- Feature flag `FF_SCOPE_SWITCHER_ENABLED` defaults to false
- If disabled, calendars show "mine" scope only (existing behavior)

---

## EXTENSION: Baseline A-009/A-010/A-012 (Page Tokenization)

**Location:** Baseline A.4.1, A.4.2, A.4.4
**Extension ID:** A-009-EXT

**Additional Acceptance Criteria:**
1. Each page passes `grep -E "#[0-9A-Fa-f]{3,8}" [file]` with 0 matches
2. Each page passes dark mode visual test (no invisible text, no contrast failures)
3. Each page has Playwright snapshot test capturing both themes
4. CSS specificity conflicts resolved (no `!important` for colors)

**Verification Script:**
```bash
#!/bin/bash
# Run for each tokenized page
FILE=$1
COLORS=$(grep -cE "#[0-9A-Fa-f]{3,8}" "$FILE" || echo 0)
RGBA=$(grep -c "rgba\?" "$FILE" || echo 0)
INLINE=$(grep -c "style=" "$FILE" || echo 0)
echo "$FILE: colors=$COLORS, rgba=$RGBA, inline=$INLINE"
[ "$COLORS" -eq 0 ] && [ "$RGBA" -eq 0 ] || exit 1
```

---

## EXTENSION: Baseline B-001 (prefers-reduced-motion)

**Location:** Baseline B.1
**Extension ID:** B-001-EXT

**Additional Acceptance Criteria:**
1. JavaScript animations also respect `window.matchMedia('(prefers-reduced-motion: reduce)')`
2. Widget collapse/expand uses instant transition when reduced motion enabled
3. Loading spinner uses static icon (not animated) when reduced motion enabled
4. Toast notifications slide in instantly (no animation) when reduced motion enabled

**Implementation for JavaScript:**
```javascript
const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
const TRANSITION_DURATION = prefersReducedMotion ? 0 : 200;
```

---

# NEW ITEMS: Section A (Left to-do from Plan)

## A-018: Scope Switcher Data Correctness Verification

| Field | Value |
|-------|-------|
| **ID** | A-018 |
| **Title** | Scope Switcher Data Correctness Verification |
| **Description** | Verify that scope filtering returns exactly the correct data for each scope level, with no data leakage across grant boundaries |
| **Why it matters** | Incorrect scope filtering could expose shifts/schedules users should not see (privacy/security) or hide data users should see (usability) |
| **Acceptance criteria** | 1. Unit tests for each scope level with mock data<br>2. "Mine" scope returns ONLY current user's assignments<br>3. "Company" scope returns ONLY assignments for user's company<br>4. "Molecule" scope returns ONLY assignments within user's molecule grant<br>5. "Area" scope returns ONLY assignments within user's area grant<br>6. User without molecule grant cannot see molecule scope data even if URL manipulated<br>7. Integration test: create user with limited grants, verify cannot access broader scope via direct API call |
| **Dependencies** | A-003 (Scope Switcher Integration), Backend services |
| **Owner/role** | Backend Developer + QA |
| **Complexity** | M |
| **Risk** | High - data leakage is a security issue |
| **Verification plan** | Unit tests (automated), Integration tests (automated), Manual penetration test with limited user |
| **Rollback/mitigation** | Scope filtering failures return empty result set (fail closed, not fail open) |

---

## A-019: Calendar Pagination Correctness

| Field | Value |
|-------|-------|
| **ID** | A-019 |
| **Title** | Calendar Pagination and Date Range Correctness |
| **Description** | Verify calendar correctly handles date boundaries, month transitions, pagination, and edge cases |
| **Why it matters** | Incorrect date handling causes shifts to appear on wrong days or be missing entirely |
| **Acceptance criteria** | 1. Month view shows correct number of days for each month (28/29/30/31)<br>2. February 29 shows correctly in leap years<br>3. DST transitions handled (no duplicate/missing hours)<br>4. Week view starts on correct day per locale (Sunday US, Monday IL)<br>5. Shifts spanning midnight appear on correct day<br>6. Multi-day shifts appear on all relevant days<br>7. Timezone: server stores UTC, client displays local time |
| **Dependencies** | None |
| **Owner/role** | Backend Developer |
| **Complexity** | M |
| **Risk** | Medium - date bugs are common and subtle |
| **Verification plan** | Unit tests for date edge cases, manual verification of Feb 29 2024, DST dates |
| **Rollback/mitigation** | N/A - logic bugs, must fix before release |

---

## A-020: Grant-Based UI Visibility Correctness

| Field | Value |
|-------|-------|
| **ID** | A-020 |
| **Title** | Grant-Based UI Visibility Correctness |
| **Description** | Verify that UI elements (nav items, scope buttons, action buttons) appear/hide correctly based on user grants |
| **Why it matters** | Users seeing buttons they can't use causes confusion; hiding buttons they should see causes support tickets |
| **Acceptance criteria** | 1. Navigation items match Plan Section 5.3 grant rules<br>2. Scope buttons appear only for users with matching grants<br>3. Action buttons (edit, assign) appear only for users with matching grants<br>4. UI never shows action that API will reject with 403<br>5. Test with: Owner (all), Director (multi-company), Manager (company), Member (personal) |
| **Dependencies** | A-003, A-001 |
| **Owner/role** | UI Developer + QA |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | Playwright tests with different user roles, manual walkthrough |
| **Rollback/mitigation** | N/A - logic bugs, must fix |

---

# NEW ITEMS: Section B (Gaps Outside Plan)

## B-016: Data Correctness - Stale Cache Handling

| Field | Value |
|-------|-------|
| **ID** | B-016 |
| **Title** | Stale Cache Handling for Calendar Data |
| **Description** | Define and implement cache invalidation strategy for calendar data when shifts are modified |
| **Why it matters** | Users seeing stale data (old assignments) after changes causes confusion and mistrust |
| **Acceptance criteria** | 1. After creating/updating/deleting shift, user immediately sees change (no manual refresh)<br>2. Other users viewing same calendar see change within 30 seconds<br>3. Browser back button does not show stale data<br>4. Cache-Control headers set appropriately (no-cache for calendar API, cache for static assets)<br>5. Service worker (if any) handles cache correctly |
| **Dependencies** | None |
| **Owner/role** | Backend Developer |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | Manual: make change in tab A, verify tab B updates; automated: API response header check |
| **Rollback/mitigation** | Set Cache-Control: no-store as fallback (performance hit but correct) |

---

## B-017: Data Correctness - Partial Data / Loading Failures

| Field | Value |
|-------|-------|
| **ID** | B-017 |
| **Title** | Partial Data and API Failure Handling |
| **Description** | Define behavior when calendar API returns partial data or fails entirely |
| **Why it matters** | Silent failures cause users to miss shifts; confusing error states cause support load |
| **Acceptance criteria** | 1. API timeout (>10s) shows "Loading taking longer than expected" with retry button<br>2. API 500 error shows "Could not load calendar" with retry button<br>3. API 403 shows "You don't have access" (no retry)<br>4. Partial success (some shifts load, some fail) shows loaded data + banner indicating partial load<br>5. Network offline shows offline banner (persistent until reconnect)<br>6. All error messages localized |
| **Dependencies** | B-004 (Error States) |
| **Owner/role** | UI Developer |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | DevTools network throttling/blocking, mock API errors |
| **Rollback/mitigation** | Fallback to generic error message |

---

## B-018: Data Correctness - Concurrent Edit Handling

| Field | Value |
|-------|-------|
| **ID** | B-018 |
| **Title** | Concurrent Edit Conflict Detection |
| **Description** | Handle case where two users edit the same shift simultaneously |
| **Why it matters** | Last-write-wins silently loses data; users don't know their changes were overwritten |
| **Acceptance criteria** | 1. Shift has `Version` or `LastModified` field<br>2. Update API includes version in request<br>3. If version mismatch, API returns 409 Conflict<br>4. UI shows "This shift was modified by someone else. Refresh to see changes." with Refresh button<br>5. Refresh loads current data, user can re-apply their changes<br>6. Message localized |
| **Dependencies** | Backend schema change |
| **Owner/role** | Backend Developer + UI Developer |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | Open same shift in two browsers, edit both, verify conflict detected |
| **Rollback/mitigation** | Disable optimistic locking, accept last-write-wins (document limitation) |

---

## B-019: Instrumentation - Analytics Event Taxonomy

| Field | Value |
|-------|-------|
| **ID** | B-019 |
| **Title** | Analytics Event Taxonomy Definition |
| **Description** | Define and implement analytics events for measuring UI overhaul success |
| **Why it matters** | Cannot measure "time to find calendar view" improvement without analytics |
| **Acceptance criteria** | 1. Event taxonomy document created with: event name, properties, trigger<br>2. Events implemented: `calendar_view_changed`, `scope_changed`, `context_switched`, `widget_toggled`, `navigation_category_toggled`<br>3. Properties include: user_id (hashed), timestamp, previous_value, new_value, time_on_page<br>4. No PII in analytics (names, emails, phone numbers)<br>5. Analytics disabled when user opts out (if applicable)<br>6. Events visible in analytics dashboard |
| **Dependencies** | Analytics platform decision (TBD) |
| **Owner/role** | Product + UI Developer |
| **Complexity** | M |
| **Risk** | Low |
| **Verification plan** | Enable debug mode, verify events fire correctly |
| **Rollback/mitigation** | Disable analytics entirely via feature flag |

---

## B-020: Instrumentation - Error Tracking Integration

| Field | Value |
|-------|-------|
| **ID** | B-020 |
| **Title** | Client-Side Error Tracking (Sentry or equivalent) |
| **Description** | Implement client-side error tracking to catch JavaScript errors in production |
| **Why it matters** | JS errors in production are invisible without error tracking; users experience broken UI silently |
| **Acceptance criteria** | 1. Error tracking SDK installed and initialized<br>2. Unhandled JS errors automatically captured<br>3. Errors include: stack trace, browser info, user ID (hashed), page URL<br>4. Source maps uploaded for readable stack traces<br>5. Alerts configured for error spike (>10 errors/minute)<br>6. PII scrubbed from error reports<br>7. Error boundary component catches React/component errors gracefully |
| **Dependencies** | Error tracking platform decision |
| **Owner/role** | DevOps + UI Developer |
| **Complexity** | M |
| **Risk** | Low |
| **Verification plan** | Intentionally throw error, verify captured in dashboard |
| **Rollback/mitigation** | Disable SDK if performance impact |

---

## B-021: Instrumentation - Performance Monitoring

| Field | Value |
|-------|-------|
| **ID** | B-021 |
| **Title** | Real User Monitoring (RUM) for Performance |
| **Description** | Implement performance monitoring to track Core Web Vitals in production |
| **Why it matters** | Lab tests (Lighthouse) don't reflect real user experience; need field data |
| **Acceptance criteria** | 1. RUM SDK captures: LCP, FID/INP, CLS, TTFB<br>2. Data segmented by: page, browser, device type, connection speed<br>3. Dashboard shows p50, p75, p95 for each metric<br>4. Alerts configured for regression (p75 degrades >20% week-over-week)<br>5. Calendar page specifically monitored (highest complexity) |
| **Dependencies** | RUM platform decision |
| **Owner/role** | DevOps |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Verify metrics appearing in dashboard after deployment |
| **Rollback/mitigation** | Disable RUM if performance overhead measured |

---

## B-022: Instrumentation - Structured Logging

| Field | Value |
|-------|-------|
| **ID** | B-022 |
| **Title** | Structured Logging for UI Operations |
| **Description** | Ensure server-side logs for UI-related operations are structured and queryable |
| **Why it matters** | Debugging production issues requires correlating user actions with server logs |
| **Acceptance criteria** | 1. All API endpoints log: request_id, user_id, endpoint, params, response_code, duration_ms<br>2. Logs are JSON structured (not free-form text)<br>3. Request ID propagated from client (X-Request-ID header) for correlation<br>4. Sensitive data (passwords, tokens) never logged<br>5. Log level configurable (DEBUG in dev, INFO in prod)<br>6. Logs queryable in centralized system (ELK, CloudWatch, etc.) |
| **Dependencies** | Logging infrastructure |
| **Owner/role** | Backend Developer + DevOps |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Make API call, verify log entry appears with correct fields |
| **Rollback/mitigation** | N/A - logging is always valuable |

---

## B-023: Rollout Plan - Feature Flags

| Field | Value |
|-------|-------|
| **ID** | B-023 |
| **Title** | Feature Flag Infrastructure for UI Overhaul |
| **Description** | Implement feature flags to enable gradual rollout and quick rollback |
| **Why it matters** | All-or-nothing deployments are risky; feature flags enable safe incremental rollout |
| **Acceptance criteria** | 1. Feature flags defined: `FF_NEW_NAV_ENABLED`, `FF_SCOPE_SWITCHER_ENABLED`, `FF_NEW_CALENDAR_STYLES`, `FF_WIDGETS_ENABLED`<br>2. Flags can be toggled per-user, per-company, or globally<br>3. Flag state checked server-side (not bypassable client-side)<br>4. Flag changes take effect within 1 minute (no restart required)<br>5. Dashboard shows current flag state and history<br>6. Flags can be set via admin UI or API |
| **Dependencies** | Feature flag infrastructure (existing or new) |
| **Owner/role** | Backend Developer + DevOps |
| **Complexity** | M |
| **Risk** | Low |
| **Verification plan** | Toggle flag, verify UI changes without deployment |
| **Rollback/mitigation** | Flags ARE the rollback mechanism |

---

## B-024: Rollout Plan - Phased Rollout Strategy

| Field | Value |
|-------|-------|
| **ID** | B-024 |
| **Title** | Phased Rollout Strategy Document |
| **Description** | Document the rollout phases, success criteria, and go/no-go decisions |
| **Why it matters** | Ad-hoc rollouts are chaotic; documented plan enables coordination |
| **Acceptance criteria** | 1. Document created with phases:<br>  - Phase 0: Internal testing (dev team only)<br>  - Phase 1: 5% of users (one company volunteer)<br>  - Phase 2: 25% of users<br>  - Phase 3: 100% of users<br>2. Each phase has: duration, success metrics, escalation path<br>3. Go/no-go criteria defined (error rate <1%, no P1 bugs, NPS neutral or better)<br>4. Rollback criteria defined (error rate >5%, any P1 bug, >10 support tickets/day)<br>5. Communication plan for each phase |
| **Dependencies** | B-023 (Feature Flags) |
| **Owner/role** | Product Manager |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Document review and approval |
| **Rollback/mitigation** | N/A - this IS the rollback plan |

---

## B-025: Rollout Plan - Rollback Procedure

| Field | Value |
|-------|-------|
| **ID** | B-025 |
| **Title** | Rollback Procedure Documentation |
| **Description** | Document step-by-step rollback procedure for emergency revert |
| **Why it matters** | During incident, clear procedure prevents panic and mistakes |
| **Acceptance criteria** | 1. Runbook created with exact commands/clicks to disable each feature flag<br>2. Estimated rollback time: <5 minutes<br>3. Rollback tested in staging environment<br>4. On-call team knows location of runbook<br>5. Runbook includes: who can authorize rollback, communication template, post-rollback verification steps |
| **Dependencies** | B-023 (Feature Flags) |
| **Owner/role** | DevOps + On-call team |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Simulate rollback in staging, time the process |
| **Rollback/mitigation** | N/A |

---

## B-026: Backend Integration - API Contract Documentation

| Field | Value |
|-------|-------|
| **ID** | B-026 |
| **Title** | API Contract Documentation for UI Changes |
| **Description** | Document all API changes required for UI overhaul |
| **Why it matters** | UI and backend must agree on contracts; undocumented changes cause integration failures |
| **Acceptance criteria** | 1. OpenAPI/Swagger spec updated for all changed endpoints<br>2. New query parameters documented: `scope`, `theme`, `locale`<br>3. New response fields documented<br>4. Error response format documented (code, message, details)<br>5. Breaking changes marked and versioned<br>6. Spec available at `/swagger` endpoint |
| **Dependencies** | None |
| **Owner/role** | Backend Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Swagger UI shows updated spec, endpoints match spec |
| **Rollback/mitigation** | N/A |

---

## B-027: Backend Integration - Rate Limiting for UI Endpoints

| Field | Value |
|-------|-------|
| **ID** | B-027 |
| **Title** | Rate Limiting Review for UI Endpoints |
| **Description** | Review and adjust rate limits for endpoints called by new UI components |
| **Why it matters** | Aggressive rate limits cause UI failures; no limits enable DoS |
| **Acceptance criteria** | 1. Calendar API: 60 requests/minute/user (scope changes may increase call frequency)<br>2. Context Switcher API: 30 requests/minute/user<br>3. Widget API: 30 requests/minute/user<br>4. Rate limit responses (429) trigger UI retry with backoff<br>5. Rate limits documented<br>6. Monitoring for rate limit hits |
| **Dependencies** | None |
| **Owner/role** | Backend Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Load test UI, verify rate limits not hit under normal use |
| **Rollback/mitigation** | Increase limits if legitimate traffic blocked |

---

## B-028: Backend Integration - Error Response Standardization

| Field | Value |
|-------|-------|
| **ID** | B-028 |
| **Title** | Standardized Error Response Format |
| **Description** | Ensure all API errors return consistent, UI-consumable format |
| **Why it matters** | Inconsistent error formats require special-case handling in UI |
| **Acceptance criteria** | 1. All errors return JSON: `{ "error": { "code": "string", "message": "string", "details": {} } }`<br>2. HTTP status codes correct (400 bad request, 401 unauth, 403 forbidden, 404 not found, 409 conflict, 500 server error)<br>3. Error messages are user-friendly (not stack traces)<br>4. Error codes are machine-readable (e.g., "SHIFT_NOT_FOUND", "GRANT_REQUIRED")<br>5. Validation errors include field-level details |
| **Dependencies** | None |
| **Owner/role** | Backend Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Trigger each error type, verify response format |
| **Rollback/mitigation** | N/A |

---

## B-029: Performance - Bundle Analysis

| Field | Value |
|-------|-------|
| **ID** | B-029 |
| **Title** | CSS/JS Bundle Size Analysis |
| **Description** | Analyze and optimize bundle sizes after tokenization |
| **Why it matters** | Token system may increase CSS size; must verify acceptable |
| **Acceptance criteria** | 1. CSS bundle size measured before and after<br>2. Target: total CSS <100KB gzipped<br>3. Unused CSS identified and removed (PurgeCSS or manual)<br>4. JS bundle size measured<br>5. No bundle size regression >10%<br>6. Bundle sizes tracked in CI |
| **Dependencies** | A-009 through A-012 (tokenization complete) |
| **Owner/role** | UI Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Build, measure with bundlesize tool |
| **Rollback/mitigation** | Optimize if exceeded, defer low-priority CSS |

---

## B-030: Performance - Code Splitting / Lazy Loading

| Field | Value |
|-------|-------|
| **ID** | B-030 |
| **Title** | Code Splitting Strategy Review |
| **Description** | Review and implement code splitting for large components |
| **Why it matters** | Loading all JS upfront delays interactivity |
| **Acceptance criteria** | 1. Calendar components loaded only on calendar pages<br>2. Admin-only components not loaded for regular users<br>3. Modals lazy-loaded on trigger<br>4. Initial JS payload <200KB gzipped<br>5. Subsequent chunks loaded on navigation |
| **Dependencies** | None |
| **Owner/role** | UI Developer |
| **Complexity** | M |
| **Risk** | Medium - may require build changes |
| **Verification plan** | Network tab analysis, verify chunk loading |
| **Rollback/mitigation** | Revert to single bundle if splitting causes issues |

---

## B-031: Performance - Image Optimization

| Field | Value |
|-------|-------|
| **ID** | B-031 |
| **Title** | Image Optimization Audit |
| **Description** | Ensure all images (icons, avatars, brand assets) are optimized |
| **Why it matters** | Unoptimized images are largest performance offender |
| **Acceptance criteria** | 1. All images have explicit width/height (no CLS)<br>2. SVG icons instead of PNG where possible<br>3. Avatar images lazy-loaded below fold<br>4. Brand images in WebP with PNG fallback<br>5. No image >100KB without justification<br>6. Responsive images with srcset for different sizes |
| **Dependencies** | None |
| **Owner/role** | UI Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Lighthouse image audit, manual review |
| **Rollback/mitigation** | N/A |

---

## B-032: Performance - Render Performance Profiling

| Field | Value |
|-------|-------|
| **ID** | B-032 |
| **Title** | Render Performance Profiling |
| **Description** | Profile and optimize render performance for complex pages |
| **Why it matters** | Janky scrolling and slow interactions frustrate users |
| **Acceptance criteria** | 1. Calendar scroll is 60fps (no jank)<br>2. Scope switch re-render <100ms<br>3. No forced synchronous layouts (layout thrashing)<br>4. Large lists use virtualization if >100 items<br>5. No memory leaks after repeated navigation<br>6. Chrome DevTools Performance recording analyzed for each major page |
| **Dependencies** | Tokenization complete |
| **Owner/role** | UI Developer |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | Performance profiler recording, verify metrics |
| **Rollback/mitigation** | Optimize hot paths identified in profiler |

---

## B-033: Security - XSS Audit

| Field | Value |
|-------|-------|
| **ID** | B-033 |
| **Title** | XSS Vulnerability Audit |
| **Description** | Audit all UI code for XSS vulnerabilities |
| **Why it matters** | User-generated content (names, notes) could contain malicious scripts |
| **Acceptance criteria** | 1. All user-generated content HTML-encoded before display<br>2. No use of `@Html.Raw()` with user content<br>3. No `innerHTML` assignment with user content in JS<br>4. CSP header configured (script-src 'self')<br>5. No inline event handlers (`onclick`, `onerror`)<br>6. Penetration test: inject `<script>alert(1)</script>` in all text fields |
| **Dependencies** | None |
| **Owner/role** | Security + Backend Developer |
| **Complexity** | M |
| **Risk** | High - XSS is serious vulnerability |
| **Verification plan** | Automated scanner + manual penetration test |
| **Rollback/mitigation** | N/A - must fix before release |

---

## B-034: Security - CSRF Protection Verification

| Field | Value |
|-------|-------|
| **ID** | B-034 |
| **Title** | CSRF Protection Verification |
| **Description** | Verify all state-changing operations have CSRF protection |
| **Why it matters** | Missing CSRF allows attackers to perform actions as logged-in user |
| **Acceptance criteria** | 1. All POST/PUT/DELETE endpoints require anti-forgery token<br>2. Token included in all forms via `@Html.AntiForgeryToken()`<br>3. AJAX requests include token in header<br>4. [IgnoreAntiforgeryToken] only on explicitly whitelisted endpoints (documented)<br>5. Test: submit form without token, verify 400 response |
| **Dependencies** | None |
| **Owner/role** | Security + Backend Developer |
| **Complexity** | S |
| **Risk** | High |
| **Verification plan** | Remove token from request, verify rejection |
| **Rollback/mitigation** | N/A - must fix |

---

## B-035: Security - Permission UI Alignment

| Field | Value |
|-------|-------|
| **ID** | B-035 |
| **Title** | UI Permission Alignment Audit |
| **Description** | Ensure UI permissions exactly match backend permissions |
| **Why it matters** | UI showing action but backend rejecting = bad UX; UI hiding action but backend allowing = security gap |
| **Acceptance criteria** | 1. Every action button's visibility condition matches backend policy<br>2. No action button visible that results in 403<br>3. No action possible via URL manipulation that UI doesn't show<br>4. Permission checks use same grant service as backend<br>5. Test matrix: all roles × all actions |
| **Dependencies** | A-020 |
| **Owner/role** | QA + Security |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | Permission matrix test, URL manipulation test |
| **Rollback/mitigation** | N/A - must fix |

---

## B-036: Security - Sensitive Data in Client

| Field | Value |
|-------|-------|
| **ID** | B-036 |
| **Title** | Sensitive Data Exposure Audit |
| **Description** | Audit for sensitive data exposed to client |
| **Why it matters** | PII in page source or localStorage is privacy violation |
| **Acceptance criteria** | 1. No full phone numbers in HTML (mask: XXX-XXX-1234)<br>2. No email addresses in localStorage<br>3. No API keys in JavaScript<br>4. User IDs in analytics are hashed<br>5. Password fields use `type="password"`<br>6. View source and localStorage inspection reveals no PII |
| **Dependencies** | None |
| **Owner/role** | Security |
| **Complexity** | S |
| **Risk** | Medium |
| **Verification plan** | Manual inspection of page source and localStorage |
| **Rollback/mitigation** | Fix exposures immediately |

---

## B-037: Accessibility - Keyboard Navigation Audit

| Field | Value |
|-------|-------|
| **ID** | B-037 |
| **Title** | Keyboard Navigation Comprehensive Audit |
| **Description** | Verify all functionality is accessible via keyboard only |
| **Why it matters** | Users with motor disabilities rely on keyboard navigation |
| **Acceptance criteria** | 1. All interactive elements focusable via Tab<br>2. Focus order matches visual order<br>3. No focus traps (except modals)<br>4. Skip link present and functional<br>5. Dropdown menus navigable via arrow keys<br>6. Calendar navigable via arrow keys<br>7. ESC closes all overlays<br>8. Enter activates buttons/links<br>9. Space toggles checkboxes<br>10. No mouse-only interactions |
| **Dependencies** | B-013 (Modal Focus) |
| **Owner/role** | Accessibility Specialist |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | Unplug mouse, complete all user flows |
| **Rollback/mitigation** | N/A - must fix |

---

## B-038: Accessibility - Screen Reader Flow Testing

| Field | Value |
|-------|-------|
| **ID** | B-038 |
| **Title** | Screen Reader User Flow Testing |
| **Description** | Test complete user flows with screen reader |
| **Why it matters** | ARIA attributes alone don't guarantee screen reader usability |
| **Acceptance criteria** | 1. NVDA or VoiceOver can complete: login, view calendar, change scope, view shift details<br>2. All form fields announced with label<br>3. Error messages announced when they appear<br>4. Dynamic content changes announced (aria-live)<br>5. Images have meaningful alt text (not "image123.png")<br>6. Buttons and links have descriptive text (not "click here") |
| **Dependencies** | A-013 (WCAG Audit) |
| **Owner/role** | Accessibility Specialist |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | Manual screen reader testing |
| **Rollback/mitigation** | N/A - must fix |

---

## B-039: QA - Browser Compatibility Matrix

| Field | Value |
|-------|-------|
| **ID** | B-039 |
| **Title** | Browser Compatibility Testing Matrix |
| **Description** | Define and execute browser compatibility test matrix |
| **Why it matters** | CSS variables and modern features may not work in older browsers |
| **Acceptance criteria** | 1. Matrix defined: Chrome 90+, Firefox 90+, Safari 14+, Edge 90+<br>2. Each browser tested at minimum supported version<br>3. CSS custom properties work (no fallback needed for supported versions)<br>4. JavaScript features work (no polyfills needed for supported versions)<br>5. Known issues documented with workarounds<br>6. IE11 explicitly NOT supported (documented) |
| **Dependencies** | Tokenization complete |
| **Owner/role** | QA |
| **Complexity** | M |
| **Risk** | Medium |
| **Verification plan** | BrowserStack or local testing |
| **Rollback/mitigation** | Add polyfills/fallbacks for critical browsers |

---

## B-040: QA - Visual Regression Testing

| Field | Value |
|-------|-------|
| **ID** | B-040 |
| **Title** | Visual Regression Test Suite |
| **Description** | Implement visual regression testing for UI changes |
| **Why it matters** | Subtle CSS regressions are hard to catch manually |
| **Acceptance criteria** | 1. Visual snapshot tests for all major pages<br>2. Tests run in CI on every PR<br>3. Threshold: <0.1% pixel difference allowed<br>4. Tests cover: light mode, dark mode, mobile viewport<br>5. Baseline images stored and versioned<br>6. Easy baseline update process |
| **Dependencies** | Playwright infrastructure |
| **Owner/role** | QA + UI Developer |
| **Complexity** | M |
| **Risk** | Low |
| **Verification plan** | Intentionally break CSS, verify test catches it |
| **Rollback/mitigation** | Disable visual tests if too flaky (investigate root cause) |

---

## B-041: QA - Test Data Strategy

| Field | Value |
|-------|-------|
| **ID** | B-041 |
| **Title** | Test Data Strategy for UI Tests |
| **Description** | Define and implement test data strategy for reliable UI testing |
| **Why it matters** | Tests dependent on production data are flaky and may expose PII |
| **Acceptance criteria** | 1. Seed data script creates consistent test scenarios<br>2. Test users for each role: Owner, Director, Manager, Member<br>3. Test data includes: empty calendar, full calendar, multi-company, single company<br>4. Tests reset to known state before each run<br>5. No production data in test environment<br>6. Test data documented |
| **Dependencies** | None |
| **Owner/role** | QA |
| **Complexity** | M |
| **Risk** | Low |
| **Verification plan** | Run tests 5x, verify consistent results |
| **Rollback/mitigation** | N/A |

---

## B-042: QA - Flaky Test Policy

| Field | Value |
|-------|-------|
| **ID** | B-042 |
| **Title** | Flaky Test Policy and Remediation |
| **Description** | Define policy for handling flaky tests |
| **Why it matters** | Flaky tests erode trust in CI; ignored failures hide real bugs |
| **Acceptance criteria** | 1. Policy documented: flaky test identified = P2 bug filed immediately<br>2. Flaky tests quarantined (run but don't block merge)<br>3. Quarantine limited to 5 business days, then skip or fix<br>4. Flaky test causes root-caused and documented<br>5. Metrics tracked: flaky test count over time (target: 0) |
| **Dependencies** | B-040, A-017 |
| **Owner/role** | QA Lead |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Policy documented and team trained |
| **Rollback/mitigation** | N/A |

---

## B-043: Documentation - Component Usage Guidelines

| Field | Value |
|-------|-------|
| **ID** | B-043 |
| **Title** | Component Usage Guidelines Document |
| **Description** | Document how to use design system components correctly |
| **Why it matters** | Undocumented components lead to inconsistent usage |
| **Acceptance criteria** | 1. Each component documented: purpose, props, do/don't<br>2. Code examples for common use cases<br>3. Accessibility requirements per component<br>4. When to use which component (decision tree)<br>5. Documentation lives next to code (README or Storybook) |
| **Dependencies** | Tokenization complete |
| **Owner/role** | UI Developer |
| **Complexity** | M |
| **Risk** | Low |
| **Verification plan** | New developer can use components from docs alone |
| **Rollback/mitigation** | N/A |

---

## B-044: Documentation - Migration Notes

| Field | Value |
|-------|-------|
| **ID** | B-044 |
| **Title** | Migration Notes for Existing Code |
| **Description** | Document how to migrate existing pages to new design system |
| **Why it matters** | Future pages need clear migration path |
| **Acceptance criteria** | 1. Step-by-step migration checklist<br>2. Common patterns: replace `#color` with `var(--token)`<br>3. Common pitfalls documented<br>4. Before/after examples<br>5. Automated migration script if feasible |
| **Dependencies** | Tokenization complete |
| **Owner/role** | UI Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Use guide to migrate one page, verify completeness |
| **Rollback/mitigation** | N/A |

---

## B-045: Documentation - Changelog

| Field | Value |
|-------|-------|
| **ID** | B-045 |
| **Title** | UI Overhaul Changelog |
| **Description** | Document all user-facing changes for release notes |
| **Why it matters** | Users need to understand what changed; support needs reference |
| **Acceptance criteria** | 1. Changelog lists all visible changes<br>2. Changes categorized: New, Improved, Fixed, Changed<br>3. Screenshots of major changes included<br>4. Known issues section<br>5. FAQ for common questions ("Where did X go?") |
| **Dependencies** | All work complete |
| **Owner/role** | Product Manager |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Review covers all changes |
| **Rollback/mitigation** | N/A |

---

## B-046: Documentation - Operational Runbook

| Field | Value |
|-------|-------|
| **ID** | B-046 |
| **Title** | Operational Runbook for UI Overhaul |
| **Description** | Document operational procedures for UI-related issues |
| **Why it matters** | On-call needs clear procedures for UI incidents |
| **Acceptance criteria** | 1. Troubleshooting guide: "Calendar not loading" → check X, Y, Z<br>2. Common issues and resolutions<br>3. How to check feature flag status<br>4. How to roll back<br>5. Escalation path<br>6. Monitoring dashboards linked |
| **Dependencies** | B-023 (Feature Flags), B-021 (Monitoring) |
| **Owner/role** | DevOps |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | On-call team review and sign-off |
| **Rollback/mitigation** | N/A |

---

## B-047: UI States - Disabled State Styling

| Field | Value |
|-------|-------|
| **ID** | B-047 |
| **Title** | Disabled State Styling Consistency |
| **Description** | Ensure all disabled UI elements have consistent styling |
| **Why it matters** | Inconsistent disabled states confuse users about what's interactive |
| **Acceptance criteria** | 1. Disabled buttons: 50% opacity, cursor: not-allowed<br>2. Disabled inputs: grayed background, cursor: not-allowed<br>3. Disabled links: no hover effect, cursor: default<br>4. Disabled state obvious in both light and dark mode<br>5. Disabled elements not focusable (tabindex=-1 or disabled attribute) |
| **Dependencies** | None |
| **Owner/role** | UI Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Visual inspection of all disabled states |
| **Rollback/mitigation** | N/A |

---

## B-048: UI States - Hover State Consistency

| Field | Value |
|-------|-------|
| **ID** | B-048 |
| **Title** | Hover State Consistency |
| **Description** | Ensure all interactive elements have consistent hover states |
| **Why it matters** | Missing hover states make UI feel unresponsive |
| **Acceptance criteria** | 1. All buttons have hover state (background color change)<br>2. All links have hover state (underline or color change)<br>3. All clickable cards have hover state (shadow or border)<br>4. Hover states work in both light and dark mode<br>5. Hover transitions use design token duration (200ms) |
| **Dependencies** | None |
| **Owner/role** | UI Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Hover over all interactive elements |
| **Rollback/mitigation** | N/A |

---

## B-049: UI States - Active/Pressed State

| Field | Value |
|-------|-------|
| **ID** | B-049 |
| **Title** | Active/Pressed State Styling |
| **Description** | Ensure all buttons have active (pressed) state |
| **Why it matters** | Missing active state makes buttons feel unresponsive |
| **Acceptance criteria** | 1. All buttons have active state (darker background)<br>2. Active state distinct from hover state<br>3. Active state visible during click/tap<br>4. Touch devices: active state triggers on touch start |
| **Dependencies** | None |
| **Owner/role** | UI Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Click and hold buttons, verify state |
| **Rollback/mitigation** | N/A |

---

## B-050: UI States - Offline/Flaky Network Handling

| Field | Value |
|-------|-------|
| **ID** | B-050 |
| **Title** | Offline and Flaky Network Handling |
| **Description** | Define and implement behavior for poor network conditions |
| **Why it matters** | Military environments may have intermittent connectivity |
| **Acceptance criteria** | 1. Offline banner appears when navigator.onLine is false<br>2. Banner persists until connection restored<br>3. Form submissions queue if offline, retry on reconnect<br>4. Queued actions shown to user with status<br>5. Conflicting changes (edited while offline) handled gracefully<br>6. Timeout errors suggest offline (not just "error occurred") |
| **Dependencies** | B-004 (Error States) |
| **Owner/role** | UI Developer |
| **Complexity** | L |
| **Risk** | Medium |
| **Verification plan** | DevTools offline mode, airplane mode on device |
| **Rollback/mitigation** | Disable offline queue, require connectivity |

---

## B-051: UI States - Error Boundary Implementation

| Field | Value |
|-------|-------|
| **ID** | B-051 |
| **Title** | JavaScript Error Boundary |
| **Description** | Implement error boundaries to prevent full-page crashes from component errors |
| **Why it matters** | Single component error shouldn't break entire page |
| **Acceptance criteria** | 1. Error boundary catches unhandled JS errors in components<br>2. Fallback UI shows "Something went wrong" with retry button<br>3. Error details logged to error tracking (B-020)<br>4. Non-critical components (widgets) have own boundaries<br>5. Critical components (calendar) fallback to full-page error |
| **Dependencies** | B-020 (Error Tracking) |
| **Owner/role** | UI Developer |
| **Complexity** | M |
| **Risk** | Low |
| **Verification plan** | Intentionally throw error in component, verify boundary catches |
| **Rollback/mitigation** | N/A |

---

## B-052: UI States - Toast/Alert Patterns

| Field | Value |
|-------|-------|
| **ID** | B-052 |
| **Title** | Toast and Alert Pattern Standardization |
| **Description** | Standardize when to use toasts vs inline alerts vs modals |
| **Why it matters** | Inconsistent feedback patterns confuse users |
| **Acceptance criteria** | 1. Pattern guide: Toast = transient success, Alert = persistent warning/error, Modal = blocking action required<br>2. Toasts auto-dismiss after 5 seconds<br>3. Toasts dismissible manually<br>4. Only one toast visible at a time (queue others)<br>5. Alerts persist until dismissed or condition resolved<br>6. All patterns localized |
| **Dependencies** | B-004 (Error States) |
| **Owner/role** | UI Developer |
| **Complexity** | S |
| **Risk** | Low |
| **Verification plan** | Trigger each pattern, verify behavior |
| **Rollback/mitigation** | N/A |

---

# ==IMPROVEMENT NOTES (NON-BINDING)==

These are suggestions for future consideration, not requirements for the current release.

## IMP-001: Design System Evolution

Consider extracting the design system into a standalone package (`@shifty/design-system`) if other applications need to share the visual language. Current implementation is tightly coupled to the main app, which is appropriate for a single-product team but may limit future reuse.

## IMP-002: Automated Visual Testing with AI

Tools like Applitools or Percy use AI to detect visual regressions with fewer false positives than pixel-diff approaches. Consider for post-launch if visual regression testing proves valuable but generates too many false positives.

## IMP-003: CSS-in-JS Consideration

If the team grows and component isolation becomes more critical, consider whether CSS Modules or a CSS-in-JS solution would reduce style conflicts. Current global CSS with BEM naming is working but requires discipline.

## IMP-004: Component Library (Storybook)

A Storybook instance would provide a living component library for designers and developers. Low priority for current team size but valuable if team grows beyond 3-4 frontend developers.

## IMP-005: Dark Mode User Preference Sync

Currently dark mode preference is localStorage only. Consider syncing to user profile so preference follows them across devices. Low priority - most users access from single device.

## IMP-006: Progressive Web App Features

Service worker for offline calendar viewing could be valuable for military environments with intermittent connectivity. Complex to implement correctly; defer unless specific user need identified.

---

# ==ERRATA (IF ANY)==

## ERR-001: Baseline Document Version Discrepancy

The baseline document (`UI-OVERHAUL-IMPLEMENTATION-COMPLETE.md`) references "Section C" and "Section D" but uses different numbering in actual headers. The ID mapping in this document is authoritative.

## ERR-002: Calendar Page Count

Earlier analysis found ~96 pages via `find` command. Manual review shows some are partial views or components. Actual pages requiring full tokenization: ~60. The difference doesn't affect effort estimates significantly.

## ERR-003: prefers-reduced-motion Metric

Earlier grep showed 0 occurrences of `prefers-reduced-motion`. This is correct for CSS files but the `animations` section in `tokens.css` defines `--transition-duration` which could be used with JS-based motion reduction. B-001-EXT addresses both CSS and JS approaches.

---

# ==RUMSFELD MATRIX==

## Known Knowns (Documented, Understood)

| Category | Items |
|----------|-------|
| **Design Tokens** | Color system complete, typography complete, spacing complete |
| **Component List** | 14 component types identified in plan |
| **Page Count** | ~60 pages needing tokenization |
| **Localization** | <loc> tag system, SharedResources pattern |
| **RTL** | 83 RTL rules exist, patterns understood |
| **Test Infrastructure** | Playwright exists, 5 test files present |

## Known Unknowns (Questions We Know to Ask)

| Question | Discovery Method | Owner | Due |
|----------|-----------------|-------|-----|
| How many hardcoded colors remain after Phase 5? | Grep audit post-completion | UI Dev | After A-012 |
| What is actual WCAG compliance level? | axe DevTools audit | A11y Specialist | During A-013 |
| Which browsers are actually used by users? | Add browser analytics | DevOps | B-021 |
| What is real p95 page load time? | RUM implementation | DevOps | B-021 |
| How many users will be affected by each phase? | User segmentation analysis | Product | Before B-024 |
| What is current error rate baseline? | Implement error tracking first | DevOps | B-020 |

## Unknown Knowns (Knowledge We Have But Haven't Documented)

| Area | Risk | Mitigation |
|------|------|------------|
| Team knows CSS variable quirks but it's not documented | New developer breaks something | Add CSS gotchas to B-044 |
| Specific browser workarounds exist in code comments | Lost when code refactored | Extract to compatibility doc |
| Grant system behavior nuances known to senior dev | Bus factor risk | Document in A-020 acceptance criteria |
| Specific users who will complain about changes | Support load surprise | Identify and pre-communicate |
| Which pages are "sacred cows" that users hate changes to | Rollback pressure | Identify, add feature flags |

## Unknown Unknowns (Discovery Triggers)

| Trigger | What We'd Learn | Response Protocol |
|---------|----------------|-------------------|
| First production bug report | Real user behavior differs from assumptions | Post-mortem, add test case |
| Analytics shows unexpected navigation pattern | Users don't use UI as designed | UX research, potential redesign |
| Performance regression in specific browser | Browser-specific optimization needed | Add to compatibility matrix |
| Localization bug in production | Translation quality issue | Review all translations |
| Accessibility complaint from user | WCAG audit missed something | Expand A-013 scope |

---

## Discovery Playbook

### Pre-Launch Discovery (Before 100% Rollout)

**Week 1: Internal Testing (Phase 0)**
- [ ] All developers use new UI for daily work
- [ ] Note any friction points, unexpected behaviors
- [ ] Check error tracking dashboard daily
- [ ] Verify analytics events firing correctly

**Week 2: Limited Rollout (Phase 1: 5%)**
- [ ] Monitor error rate (should stay <1%)
- [ ] Monitor support ticket volume
- [ ] Check RUM metrics for performance
- [ ] Collect qualitative feedback from pilot users

**Week 3-4: Expanded Rollout (Phase 2: 25%)**
- [ ] Compare metrics between old and new UI cohorts
- [ ] Identify any browser-specific issues
- [ ] Review most common user flows in analytics
- [ ] Address P1/P2 issues before expanding

### Post-Launch Discovery (After 100% Rollout)

**First 30 Days:**
- [ ] Weekly review of error tracking trends
- [ ] Weekly review of support tickets mentioning UI
- [ ] Monthly review of Core Web Vitals
- [ ] Collect NPS or satisfaction feedback

**Ongoing:**
- [ ] Quarterly accessibility audit
- [ ] Quarterly bundle size review
- [ ] Annual design token refresh review

---

# ==RELEASE READINESS CHECKLIST (PASS/FAIL)==

## Gate 0: Pre-Development Readiness

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 0.1 | Plan document approved | Yes | ⬜ PENDING | Plan file exists |
| 0.2 | Design tokens file exists | Yes | ✅ PASS | tokens.css (448 lines) |
| 0.3 | Test infrastructure exists | Yes | ✅ PASS | Playwright configured |
| 0.4 | Localization infrastructure exists | Yes | ✅ PASS | <loc> tag helper works |
| 0.5 | Feature flag infrastructure exists | Yes | ⬜ PENDING | B-023 not implemented |

## Gate 1: Design Token Foundation

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 1.1 | All semantic colors defined | Yes | ✅ PASS | tokens.css lines 1-100 |
| 1.2 | Light mode palette complete | Yes | ✅ PASS | tokens.css |
| 1.3 | Dark mode palette complete | Yes | ✅ PASS | tokens.css media query |
| 1.4 | Typography scale defined | Yes | ✅ PASS | tokens.css |
| 1.5 | Spacing scale defined | Yes | ✅ PASS | tokens.css |
| 1.6 | Shadow scale defined | Yes | ✅ PASS | tokens.css |
| 1.7 | Animation/transition tokens defined | Yes | ✅ PASS | tokens.css |
| 1.8 | Shift type colors defined | Yes | ✅ PASS | tokens.css |

## Gate 2: Navigation & Context

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 2.1 | Context Switcher component exists | Yes | ✅ PASS | Component file exists |
| 2.2 | Context Switcher works for multi-context users | Yes | ⬜ PENDING | A-001 not verified |
| 2.3 | Context Switcher hidden for single-context users | Yes | ⬜ PENDING | A-001 not verified |
| 2.4 | Sidebar categories collapsible | No | ⬜ PENDING | A-002 |
| 2.5 | Navigation respects grants | Yes | ⬜ PENDING | A-020 |
| 2.6 | Navigation works in RTL | Yes | ⬜ PENDING | A-014 |

## Gate 3: Calendar Functionality

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 3.1 | Scope Switcher component exists | Yes | ✅ PASS | Component file exists |
| 3.2 | Scope Switcher integrated on shift calendar | Yes | ⬜ PENDING | A-003 |
| 3.3 | Scope filtering returns correct data | Yes | ⬜ PENDING | A-018 |
| 3.4 | Calendar responsive on mobile | No | ⬜ PENDING | A-004 |
| 3.5 | Calendar empty states implemented | No | ⬜ PENDING | A-005 |
| 3.6 | Shift badge colors use tokens | Yes | ⬜ PENDING | A-006 |
| 3.7 | Date pagination correct | Yes | ⬜ PENDING | A-019 |
| 3.8 | Calendar loads in <3s (LCP) | No | ⬜ PENDING | B-021 |

## Gate 4: Widget System

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 4.1 | OnCallWidget component exists | Yes | ✅ PASS | Component file exists |
| 4.2 | OnCallWidget displays contacts correctly | Yes | ⬜ PENDING | Manual test |
| 4.3 | OnCallWidget empty state implemented | No | ⬜ PENDING | A-007 |
| 4.4 | Widget collapse state persists | No | ⬜ PENDING | A-008 |
| 4.5 | Widgets respect grants (additive merge) | Yes | ⬜ PENDING | A-020 |

## Gate 5: Page Tokenization

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 5.1 | Admin pages use design tokens | Yes | ⬜ PENDING | A-009 |
| 5.2 | Owner pages use design tokens | Yes | ⬜ PENDING | A-010 |
| 5.3 | Calendar pages use design tokens | Yes | ⬜ PENDING | A-011 |
| 5.4 | All remaining pages use design tokens | Yes | ⬜ PENDING | A-012 |
| 5.5 | Zero hardcoded hex colors in CSHTML | Yes | ⬜ PENDING | Grep verification |
| 5.6 | Zero hardcoded hex colors in CSS | No | ⬜ PENDING | Grep verification |

## Gate 6: Accessibility

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 6.1 | WCAG AA color contrast (4.5:1 text) | Yes | ⬜ PENDING | A-013 |
| 6.2 | All images have alt text | Yes | ⬜ PENDING | A-013 |
| 6.3 | All forms have labels | Yes | ⬜ PENDING | A-013 |
| 6.4 | Focus visible on all interactive elements | Yes | ⬜ PENDING | A-013 |
| 6.5 | Keyboard navigation complete | Yes | ⬜ PENDING | B-037 |
| 6.6 | Screen reader tested | No | ⬜ PENDING | B-038 |
| 6.7 | prefers-reduced-motion respected | No | ⬜ PENDING | B-001 |
| 6.8 | Modal focus management correct | Yes | ⬜ PENDING | B-013 |

## Gate 7: Localization

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 7.1 | All visible text uses <loc> tags | Yes | ⬜ PENDING | A-015 |
| 7.2 | All keys exist in SharedResources.resx | Yes | ⬜ PENDING | A-015 |
| 7.3 | All keys have Hebrew translations | Yes | ⬜ PENDING | A-015 |
| 7.4 | RTL layout correct | Yes | ⬜ PENDING | A-014 |
| 7.5 | Date formats respect locale | Yes | ⬜ PENDING | B-010 |
| 7.6 | Number formats respect locale | No | ⬜ PENDING | B-010 |
| 7.7 | ARIA labels localized | No | ⬜ PENDING | A-015 |

## Gate 8: Performance

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 8.1 | CSS bundle <100KB gzipped | No | ⬜ PENDING | B-029 |
| 8.2 | JS bundle <200KB gzipped | No | ⬜ PENDING | B-030 |
| 8.3 | No memory leaks detected | Yes | ⬜ PENDING | B-032 |
| 8.4 | Calendar scroll 60fps | No | ⬜ PENDING | B-032 |
| 8.5 | Lighthouse performance score >80 | No | ⬜ PENDING | Manual test |

## Gate 9: Security

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 9.1 | No XSS vulnerabilities | Yes | ⬜ PENDING | B-033 |
| 9.2 | CSRF protection verified | Yes | ⬜ PENDING | B-034 |
| 9.3 | UI permissions match backend | Yes | ⬜ PENDING | B-035 |
| 9.4 | No PII in client storage | Yes | ⬜ PENDING | B-036 |
| 9.5 | CSP header configured | No | ⬜ PENDING | B-033 |

## Gate 10: Testing

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 10.1 | Playwright tests pass (>95%) | Yes | ⬜ PENDING | A-017 |
| 10.2 | Browser compatibility verified | Yes | ⬜ PENDING | B-039 |
| 10.3 | Visual regression tests exist | No | ⬜ PENDING | B-040 |
| 10.4 | Test data strategy documented | No | ⬜ PENDING | B-041 |
| 10.5 | Flaky test policy in place | No | ⬜ PENDING | B-042 |

## Gate 11: Documentation & Operations

| # | Criterion | Blocker | Status | Evidence |
|---|-----------|---------|--------|----------|
| 11.1 | Component usage guidelines exist | No | ⬜ PENDING | B-043 |
| 11.2 | Migration notes documented | No | ⬜ PENDING | B-044 |
| 11.3 | Changelog prepared | Yes | ⬜ PENDING | B-045 |
| 11.4 | Operational runbook exists | Yes | ⬜ PENDING | B-046 |
| 11.5 | Feature flags documented | Yes | ⬜ PENDING | B-023 |
| 11.6 | Rollback procedure documented | Yes | ⬜ PENDING | B-025 |
| 11.7 | Error tracking configured | Yes | ⬜ PENDING | B-020 |
| 11.8 | Monitoring dashboard exists | No | ⬜ PENDING | B-021 |

---

## Checklist Summary

| Gate | Total | Blockers | Non-Blockers | Pass | Pending |
|------|-------|----------|--------------|------|---------|
| Gate 0 | 5 | 5 | 0 | 3 | 2 |
| Gate 1 | 8 | 8 | 0 | 8 | 0 |
| Gate 2 | 6 | 4 | 2 | 1 | 5 |
| Gate 3 | 8 | 5 | 3 | 1 | 7 |
| Gate 4 | 5 | 3 | 2 | 1 | 4 |
| Gate 5 | 6 | 5 | 1 | 0 | 6 |
| Gate 6 | 8 | 5 | 3 | 0 | 8 |
| Gate 7 | 7 | 5 | 2 | 0 | 7 |
| Gate 8 | 5 | 1 | 4 | 0 | 5 |
| Gate 9 | 5 | 4 | 1 | 0 | 5 |
| Gate 10 | 5 | 2 | 3 | 0 | 5 |
| Gate 11 | 8 | 5 | 3 | 0 | 8 |
| **TOTAL** | **86** | **62** | **24** | **14** | **72** |

**Release Readiness: 14/86 criteria pass (16%)**
**Blocker Status: 12/62 blockers pass (19%)**

---

# ==COVERAGE MAP (MANDATORY)==

## 1. Data Correctness

| Item ID | Title | Coverage |
|---------|-------|----------|
| A-018 | Scope Switcher Data Correctness | Scope filtering, grant boundaries |
| A-019 | Calendar Pagination Correctness | Date edge cases, timezone, DST |
| A-020 | Grant-Based UI Visibility | UI/backend permission alignment |
| B-016 | Stale Cache Handling | Cache invalidation strategy |
| B-017 | Partial Data / Loading Failures | API failure handling |
| B-018 | Concurrent Edit Handling | Optimistic locking, conflict detection |

**Gap Analysis:** ✅ Comprehensive coverage of data correctness scenarios

---

## 2. Instrumentation

| Item ID | Title | Coverage |
|---------|-------|----------|
| B-019 | Analytics Event Taxonomy | User behavior tracking |
| B-020 | Error Tracking Integration | Client-side error capture |
| B-021 | Performance Monitoring (RUM) | Core Web Vitals, real user metrics |
| B-022 | Structured Logging | Server-side log standardization |

**Gap Analysis:** ✅ Full observability stack covered

---

## 3. Rollout Plan

| Item ID | Title | Coverage |
|---------|-------|----------|
| B-023 | Feature Flag Infrastructure | Toggle mechanism |
| B-024 | Phased Rollout Strategy | Rollout phases, go/no-go criteria |
| B-025 | Rollback Procedure | Emergency revert runbook |

**Gap Analysis:** ✅ Complete rollout lifecycle covered

---

## 4. Backend Integration

| Item ID | Title | Coverage |
|---------|-------|----------|
| A-003-EXT | Scope Switcher Backend | API contract for scope filtering |
| B-026 | API Contract Documentation | OpenAPI/Swagger updates |
| B-027 | Rate Limiting | Endpoint limits for new UI patterns |
| B-028 | Error Response Standardization | Consistent error format |

**Gap Analysis:** ✅ UI-backend contract fully specified

---

## 5. Performance

| Item ID | Title | Coverage |
|---------|-------|----------|
| B-029 | Bundle Analysis | CSS/JS size tracking |
| B-030 | Code Splitting | Lazy loading strategy |
| B-031 | Image Optimization | Asset optimization |
| B-032 | Render Performance | 60fps, layout thrashing, memory |

**Gap Analysis:** ✅ All performance dimensions covered

---

## 6. Security

| Item ID | Title | Coverage |
|---------|-------|----------|
| B-033 | XSS Audit | User content sanitization |
| B-034 | CSRF Verification | Anti-forgery token coverage |
| B-035 | Permission Alignment | UI/backend permission match |
| B-036 | Sensitive Data Exposure | PII in client audit |

**Gap Analysis:** ✅ OWASP top concerns addressed

---

## 7. Accessibility

| Item ID | Title | Coverage |
|---------|-------|----------|
| A-013 | WCAG AA Audit | Automated accessibility audit |
| B-001 | prefers-reduced-motion | Motion sensitivity |
| B-037 | Keyboard Navigation | Full keyboard access |
| B-038 | Screen Reader Testing | Assistive technology support |

**Gap Analysis:** ✅ WCAG AA requirements covered

---

## 8. QA Strategy

| Item ID | Title | Coverage |
|---------|-------|----------|
| A-017 | Playwright Test Suite | Automated E2E tests |
| B-039 | Browser Compatibility | Cross-browser matrix |
| B-040 | Visual Regression | Screenshot comparison |
| B-041 | Test Data Strategy | Reliable test fixtures |
| B-042 | Flaky Test Policy | Test reliability process |

**Gap Analysis:** ✅ Comprehensive QA approach

---

## 9. Documentation

| Item ID | Title | Coverage |
|---------|-------|----------|
| B-043 | Component Usage Guidelines | Developer documentation |
| B-044 | Migration Notes | Upgrade path documentation |
| B-045 | Changelog | User-facing release notes |
| B-046 | Operational Runbook | On-call procedures |

**Gap Analysis:** ✅ All documentation types covered

---

## 10. UI States

| Item ID | Title | Coverage |
|---------|-------|----------|
| A-005 | Calendar Empty States | Empty calendar scenarios |
| A-007 | Widget Empty States | Empty widget scenarios |
| B-003 | Loading States | Spinners, skeletons |
| B-004 | Error States | Error messages, recovery |
| B-047 | Disabled State Styling | Disabled element consistency |
| B-048 | Hover State Consistency | Interactive feedback |
| B-049 | Active/Pressed State | Click feedback |
| B-050 | Offline/Flaky Network | Network resilience |
| B-051 | Error Boundary | Component crash handling |
| B-052 | Toast/Alert Patterns | Notification standardization |

**Gap Analysis:** ✅ All UI state scenarios covered

---

## Coverage Summary

| Domain | Items | Status |
|--------|-------|--------|
| 1. Data Correctness | 6 | ✅ Complete |
| 2. Instrumentation | 4 | ✅ Complete |
| 3. Rollout Plan | 3 | ✅ Complete |
| 4. Backend Integration | 4 | ✅ Complete |
| 5. Performance | 4 | ✅ Complete |
| 6. Security | 4 | ✅ Complete |
| 7. Accessibility | 4 | ✅ Complete |
| 8. QA Strategy | 5 | ✅ Complete |
| 9. Documentation | 4 | ✅ Complete |
| 10. UI States | 10 | ✅ Complete |
| **TOTAL** | **48** | **10/10 Domains** |

**All 10 mandatory domains have coverage. Zero gaps.**

---

# ==FINAL SUMMARY==

## Document Statistics

| Metric | Count |
|--------|-------|
| Baseline items (from original) | 32 (A-001 to A-017, B-001 to B-015) |
| Extensions to baseline | 3 (A-003-EXT, A-009-EXT, B-001-EXT) |
| New Section A items | 3 (A-018 to A-020) |
| New Section B items | 37 (B-016 to B-052) |
| **Total unique work items** | **75** |
| Improvement notes | 6 |
| Errata corrections | 3 |
| Rumsfeld Matrix quadrants | 4 |
| Discovery playbook phases | 2 |
| Release checklist criteria | 86 |
| Coverage map domains | 10 |

## Critical Path to Release

1. **Foundation (Weeks 1-2):** Complete A-001 through A-008 (navigation, calendar basics)
2. **Tokenization (Weeks 3-4):** Complete A-009 through A-012 (all page updates)
3. **Quality (Week 5):** Complete A-013 through A-017, B-033 through B-038 (a11y, security, tests)
4. **Infrastructure (Week 6):** Complete B-019 through B-025 (observability, rollout)
5. **Documentation (Week 7):** Complete B-043 through B-046 (docs, runbooks)
6. **Launch (Week 8):** Phased rollout per B-024

## Acceptance

This document defines all work required to ship the UI overhaul to production quality. There are **zero known gaps** - all 10 mandatory domains are covered, all baseline items are extended where needed, and all discovered gaps are documented with acceptance criteria.

**Production readiness requires:**
- All 62 blocker criteria passing (currently 12/62)
- All Rumsfeld "Known Unknowns" resolved
- Discovery Playbook followed through Phase 2 minimum

---

**Document Complete**
**Version:** 2.0 (PATCH Format)
**Last Updated:** 2026-01-31
**Author:** Claude (Opus 4.5)
