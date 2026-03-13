# ShiftManager v3.1.x — Release Readiness Assessment

**Assessment Date**: 2026-03-13
**Build**: v3.1.2 (commit 10693c9)
**Assessor**: Orchestrated review by Claude Opus 4.6 (3 parallel review agents)
**Environment**: Windows / localhost:5001 / Chromium headless via Playwright

---

## Release Decision: NO-GO

**Reason**: 4 release-blocking issues found (1 P0, 3 P1) across Design and Localization domains. Coverage gates not fully met across all three agents.

---

## Executive Summary

| Agent | Rating | Tests / Checks | Issues | P0 | P1 | P2 | P3 | P4 |
|-------|--------|---------------|--------|----|----|----|----|-----|
| **Functionality** | CONDITIONAL PASS | 62 tests, 52 passed | 7 | 0 | 0 | 2 | 3 | 2 |
| **Design** | GOOD | 48 checks, 36 tested | 11 | 0 | 1 | 5 | 4 | 1 |
| **Localization** | MOSTLY COMPLETE (~92%) | 62 checks, 54 tested | 11 | 1 | 2 | 4 | 2 | 2 |
| **TOTAL** | — | — | **29** | **1** | **3** | **11** | **9** | **5** |

**Core workflows are functionally sound** — authentication, calendar navigation, shift assignment, time-off requests, admin/owner management all work correctly. No data loss or functional blockers. The blockers are in presentation (mobile layout) and localization (Hebrew/English text leakage).

---

## Coverage Gates

| Agent | Threshold | Actual | Status | Gap |
|-------|-----------|--------|--------|-----|
| Functionality | ≥90% | ~84% | BELOW | Error Handling (40%), SignalR (50%) |
| Design | ≥85% | ~75% | BELOW | Loading States (40%), Accessibility (70%) |
| Localization | ≥90% | ~87% | BELOW | English Leakage (75%), Static Strings (75%) |

### Blind Spots (areas with <50% coverage)

| Agent | Area | Coverage | Risk |
|-------|------|----------|------|
| **Functionality** | Error Handling | 40% (2/5 tested) | CSRF mismatch, network interruption, API error toast untested |
| **Functionality** | Real-Time SignalR | 50% (2/4 tested) | Multi-tab sync inconclusive; note/chore/oncall propagation untested |
| **Design** | Loading & Transition States | 40% (2/5 tested) | Skeleton screens, form submission indicators, toasts not observed |

### Coverage Mitigation

The below-threshold coverage does not invalidate findings — it means additional testing is recommended for the gaps listed above. The most critical gap is **SignalR real-time sync**, which could not be reliably tested in headless mode and requires manual verification with two browser windows.

---

## Release Blockers (P0 + P1)

### P0 — Blocker (1 issue)

| ID | Agent | Issue | Impact |
|----|-------|-------|--------|
| **LOC-01** | Localization | Calendar day-of-week column headers show Hebrew (ראשון, שני...) even in English mode, across ALL calendar views (Shifts, Chores, OnCall, Overview) | All English-language users see Hebrew text in the core feature area daily |

**Root cause**: Calendar page models likely use a fixed `CultureInfo("he-IL")` or company-configured culture instead of `CultureInfo.CurrentUICulture` when formatting day names.

**Fix effort**: Small — change day-name formatting in calendar page models to use current UI culture.

---

### P1 — Critical (3 issues)

| ID | Agent | Issue | Impact |
|----|-------|-------|--------|
| **DR-MOBILE-01** | Design | Page title text overlaps toolbar action buttons on ALL pages at 375px mobile viewport, making titles unreadable | Every mobile user on every page |
| **LOC-03** | Localization | Password Management page "Change Password" section has fully untranslated English description paragraph in Hebrew mode | All Hebrew users visiting password management |
| **LOC-04** | Localization | System disk warning banner ("WARNING: Disk space is low") appears in English on every page in Hebrew mode | All Hebrew users on every page when disk warning is active |

**Fix efforts**:
- DR-MOBILE-01: Small — add `flex-wrap: wrap` or media query to header toolbar CSS
- LOC-03: Small — wrap hardcoded string in `<loc>` tag, add .resx entry
- LOC-04: Small — wrap warning string in `<loc>` tag, add .resx entry

---

## Non-Blocking Issues (P2-P4)

### Functionality (5 issues)

| ID | Sev | Issue |
|----|-----|-------|
| FUNC-01 | P2 | `/My/Feedback` returns 404 — page missing or moved |
| FUNC-02 | P2 | `/My/ShiftSwapGame` returns 404 — page missing or moved |
| FUNC-03 | P3 | Admin Hub "Blueprints" link points to `/Admin/Blueprints` (404) instead of `/Owner/Blueprints` |
| FUNC-04 | P3 | OnCall calendar page title says "Day Shift Calendar" — inconsistent with sidebar "On-Call" label |
| FUNC-05 | P3 | "CANCELED" spelling (American English) — may confuse non-US English users |
| FUNC-06 | P4 | Disk space warning banner appears on every page for Owner (informational) |
| FUNC-07 | P4 | Owner always redirected to `/My/Onboarding` even if onboarding completed |

### Design (10 issues)

| ID | Sev | Issue |
|----|-----|-------|
| DR-A11Y-02 | P2 | 85+ form inputs lack accessible labels (worst: Admin/Users with 54) |
| DR-RESP-01 | P2 | Horizontal scrollbar on 13/19 pages at mobile — consistent 7px overflow suggests single root cause |
| DR-ERROR-01 | P2 | 404 error page completely unstyled (bare "HTTP 404" text) |
| DR-RESP-02 | P2 | Owner/Blueprints table overflows at tablet and mobile (881px content) |
| DR-A11Y-04 | P2 | Notification bell links to `/Notifications` which returns 404 |
| DR-THEME-01 | P3 | Dark mode select dropdowns on Calendar/Overview show stacked chevron artifacts |
| DR-A11Y-01 | P3 | Skip-link has 1:1 contrast ratio in dark mode (invisible) |
| DR-A11Y-03 | P3 | 8 interactive elements have touch targets below 44px minimum on mobile |
| DR-PRINT-01 | P3 | Quick Info panel and warning banner remain visible in print output |
| DR-LOGIN-01 | P4 | Login button contrast ratio is 2.96:1 (borderline WCAG AA) |

### Localization (8 issues)

| ID | Sev | Issue |
|----|-----|-------|
| LOC-05 | P2 | "Shift Manager" brand not localized in Hebrew login subtitle (sidebar uses "מנהל משמרות") |
| LOC-06 | P2 | Raw resource key "CompanyMoleculeHint" visible on Companies page in Hebrew mode |
| LOC-07 | P2 | Audit log action/entity values ("UserCreated", "User") untranslated in Hebrew mode |
| LOC-08 | P2 | Company ID hint "(lowercase, letters, numbers, hyphens)" untranslated |
| LOC-09 | P3 | Data Lifecycle purge confirmation text in English (may be intentional for safety) |
| LOC-10 | P3 | 404 page shows only "HTTP 404" with no localized content |
| LOC-11 | P4 | Email template variable names in English (intentional/acceptable) |
| LOC-12 | P4 | Two aria-labels ("Dismiss", "Notifications") untranslated in Hebrew mode |

---

## Cross-Reference Notes

Issues independently flagged by multiple agents from different perspectives:

| Issue | Agents | Perspective |
|-------|--------|-------------|
| 404 error page | Design (DR-ERROR-01: unstyled), Localization (LOC-10: unlocalised) | Same root: no custom error page |
| `/Notifications` 404 | Design (DR-A11Y-04), Functionality (bell icon linked) | Same root: page doesn't exist |
| Calendar "Day Shift" naming | Functionality (FUNC-04: naming mismatch), Localization (LOC-01: wrong culture) | Related calendar page issues |

---

## Recommendations (Priority Order)

### Must Fix Before Release (effort estimates)

| # | What | Why | Effort | Files to Check |
|---|------|-----|--------|----------------|
| 1 | **Fix calendar day-of-week culture** (LOC-01) | P0 — English users see Hebrew in core feature | S | Calendar page models (`*.cshtml.cs`), look for `CultureInfo` or `ToString("dddd")` |
| 2 | **Fix mobile header layout** (DR-MOBILE-01) | P1 — titles unreadable on all mobile pages | S | `wwwroot/css/site.css` — add flex-wrap or media query to header toolbar |
| 3 | **Localize disk warning banner** (LOC-04) | P1 — English on every page in Hebrew mode | S | `Pages/Shared/_Layout.cshtml` — wrap in `<loc>`, add .resx entry |
| 4 | **Localize password change description** (LOC-03) | P1 — English paragraph on password page in Hebrew | S | `Pages/Auth/ForgotPassword.cshtml` — wrap in `<loc>`, add .resx entry |

### Should Fix Before Release

| # | What | Why | Effort |
|---|------|-----|--------|
| 5 | Fix 7px mobile overflow root cause (DR-RESP-01) | P2 — scrollbar on 13 pages, likely single fix | S |
| 6 | Create styled 404 error page (DR-ERROR-01, LOC-10) | P2 — bare "HTTP 404" unprofessional | S |
| 7 | Fix/remove Notifications link (DR-A11Y-04) | P2 — toolbar bell → 404 | S |
| 8 | Add missing "CompanyMoleculeHint" .resx key (LOC-06) | P2 — raw key visible to admins | S |
| 9 | Remove or implement `/My/Feedback` and `/My/ShiftSwapGame` (FUNC-01, FUNC-02) | P2 — 404 if referenced | S-M |
| 10 | Add accessible labels to form inputs (DR-A11Y-02) | P2 — WCAG compliance | M |

### Can Ship, Fix in Next Patch

| # | What | Effort |
|---|------|--------|
| 11 | Fix dark mode select chevron artifacts (DR-THEME-01) | S |
| 12 | Localize audit log enum values (LOC-07) | M |
| 13 | Fix Admin Hub Blueprints link (FUNC-03) | S |
| 14 | Unify "On-Call" / "Day Shift" naming (FUNC-04) | S |
| 15 | Fix skip-link dark mode contrast (DR-A11Y-01) | S |
| 16 | Hide Quick Info panel in print CSS (DR-PRINT-01) | S |
| 17 | Make Blueprints table responsive (DR-RESP-02) | M |
| 18 | Increase mobile touch targets (DR-A11Y-03) | S |

---

## Manual Testing Required

The following areas could not be adequately tested via headless automation and require manual verification:

| Area | What to Test | Why |
|------|-------------|-----|
| **SignalR real-time sync** | Open two browser windows on same calendar, assign in one, verify update in other | WebSocket state unreliable in headless mode |
| **Modal interactions** | All confirmation/edit/delete modals across the app | No modals were triggered in automated flow |
| **Toast notifications** | Success/error/warning toasts on form submissions | Not captured in headless screenshots |
| **Keyboard navigation** | Tab through calendar grid, modal focus traps | Requires interactive keyboard simulation |
| **Drag-and-drop** | Roster dock drag assignment | Cannot simulate in headless mode |
| **Multi-role views** | Dashboard/calendar views as Manager vs Employee vs Director | Only Owner account was thoroughly tested by Design agent |

---

## Positive Observations

**Functionality**: All critical paths work — login, calendar CRUD, requests, admin, owner operations, CSRF protection, access control.

**Design**: Strong design language with consistent cards/buttons/typography, well-implemented dark mode, meaningful empty states, good sidebar organization.

**Localization**: ~92% completion is excellent. RTL layout is flawless (sidebar, text alignment, flex containers, calendar columns all properly mirrored). Language switching persists correctly. No raw key patterns (`Nav_*`, `Calendar_*`, etc.) were found.

---

## Path to GO

To move from NO-GO to GO, the following must be resolved:

1. Fix all 4 P0+P1 issues (estimated total effort: ~2-4 hours for an experienced developer)
2. Re-verify the fixes via targeted re-testing of affected areas
3. Manually verify SignalR real-time sync
4. Optionally address high-impact P2 issues (7px overflow, 404 page, Notifications link)

After fixes, a focused re-test of calendar pages (LOC-01), mobile header (DR-MOBILE-01), password page (LOC-03), and disk warning (LOC-04) would be sufficient — full re-review is not needed.

---

## Evidence Archive

| Report | Location | Size |
|--------|----------|------|
| Functionality Review | `docs/qa/functionality-review.md` | 284 lines |
| Design Review | `docs/qa/design-review.md` | 401 lines |
| Localization Review | `docs/qa/localization-review.md` | 518 lines |
| Screenshots | `qa_screenshots/` | 90+ images |
