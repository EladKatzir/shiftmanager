# Pre-Release E2E Test Report — 2026-07-17

**Method:** live browser verification (Claude-in-Chrome) against dev app on :5000, using the freshly seeded 543-user population. Persona-based sweep: one login per seeded user type, each exercising its feature surface. All personas use password `Test1234!`.

**Verdict: core flows are release-ready; 1 P1 server bug, 1 recurring UI bug, and 1 feature-reachability gap need decisions before release.**

---

## FIXES APPLIED + RE-VERIFIED (2026-07-18, rebuilt + browser-confirmed)

All bugs below fixed at root cause and re-verified live on :5000 (rebuilt Debug, 0 build errors, 0 console errors, 0 NREs in server log).

| Bug | Root cause | Fix | Verified |
|-----|-----------|-----|----------|
| **P1 on-call assign NRE** | On-call By-Duty rows are keyed `dutytype-{v}`, but quick-entry's user-selection commit assumed a `shift-{id}` row → `parseInt('dutytype-0'.replace('shift-',''))`=NaN → JSON `null` → `[FromBody]` binds non-nullable `int ShiftTypeId` to null → `request` null → NRE at `Table.cshtml.cs:818` | (1) `calendar-quick-entry.js` `selectItem`: route `user` picks on a `dutytype-` row to `quickAddOnDuty` (not `quickAddShift`). (2) `OnPostAssignEmployeeAsync` null-guards `request` → clean **400** (`Calendar_Error_InvalidRequest`, EN+HE) instead of NRE. | Malformed body → HTTP 400 "בקשה לא תקינה"; **0 NRE** in log; served JS routes `dutytype-…→quickAddOnDuty`; on-duty happy-path create → 200 `onDutyId:2` |
| **P2 Team read-only banner** | `IsReadOnly = !canEditNotes` is always true on Team (passes `canEditNotes:false`), but `CanEnterTimeOff` is a separate axis the banner ignored → stark "Read-Only Mode" hid the working time-off entry | `CalendarReadonlyBannerModel.CanEnterTimeOff` + banner shows "Time-off entry only…" (new resx `Calendar_TimeOffEntryOnlyMode`, EN+HE) when time-off is allowed; threaded through Team/Overview/Shifts | Team banner now `--timeoff` variant: "הזנת חופשות בלבד — …" (feature was reachable all along; banner was lying) |
| **P2 toast full-height column** | Two `.toast` rulesets collide: legacy site.css `.toast{position:fixed;bottom:2rem}` (body-level calendar toasts) bled into the new `.toast-container`-hosted toast → pinned top+bottom → full viewport height + compositor stall | `components.css`: `.toast-container > .toast.toast-undo { position:static; top/bottom:auto }` — container owns placement | Toast height **930px → 61px**, `position:static`, compact top pill; stall gone |
| **P3 on-call picker duplicate names** | Area-wide pool (609 users) rendered `DisplayName` only; no company | `OnCall.CompanyNamesById` map → `data-company` on options → bottom sheet renders "name — company", inline typeahead shows muted company suffix | 3× "רועי שלום" now carry distinct companies (צפונה / חוץ / סמפקמיה) |
| **Cosmetic: `[plus-circle]` literal** | `plus-circle` missing from `IconTagHelper` dict → placeholder fallback | Added Lucide `plus-circle` path | Requests "בקש חופשה" renders real `<svg class="icon icon--plus-circle">`, no placeholder |
| **Cosmetic: "⋮⋮" in duty names** | Bottom sheet scraped the whole label cell, incl. the row-reorder grip (`.excel-calendar__row-grip`) | Scrape inner `.excel-calendar__row-label-name` | `rowLabel` = "חק״מכו" (clean) even with grip present |
| **Cosmetic: chore empty-Enter silent-discard** | `commitChoreTitle` silently closed on empty title (server requires a title) | Keep editor open + info-toast hint ("Enter a title for the chore" / "יש להזין כותרת למטלה") | Served JS carries keep-open+hint logic |

### Deferred (disclosed)
- **Cosmetic: Requests nav item shown to Mil/GroupUser** (redirects gracefully). The correct fix needs new infrastructure — either an `AccountType` login claim (stale-claim tradeoff, touches Griffin+cookie login) or a per-nav-render DB lookup — disproportionate for a P3 that already redirects with a friendly message. Nav is grant-policy-only by design (visibility==access); `AccountType` isn't a claim. **Needs a design decision on the mechanism before implementing.**

### Test-env artifacts left by re-verification
- On-duty assignment: נועה לוי, Hakam, 2026-07-22 (`onDutyId:2`). DB is a testing environment; harmless.

---

## ORIGINAL FINDINGS (pre-fix, 2026-07-17)

---

## Coverage matrix (persona × features)

| # | Persona | User | Result |
|---|---------|------|--------|
| 1 | Soldier (חייל) | alhut1.tzafona@test (יובל כהן) | PASS |
| 2 | Lead (מפ"צ) | lead.alhut.tzafona@test (גיא חדד) | PASS |
| 3 | Kabar (קב"ר) | kabar.tzafona@test (ניר אשכנזי) | PASS w/ bugs found |
| 4 | Molecule Admin (מפק"מ) | moladmin.harava@test | PASS |
| 5 | Mil multi-company (מילואים) | mil4@test (אורי סבן) | PASS |
| 6 | GroupUser (יוזר קיבוצי) | group3@test (תורן פאי) | PASS |
| 7 | QA-molecule soldier | alhut1.qa-alpha@test | PASS |
| 8 | Owner | owner2@test | PASS w/ finding |
| — | Trainee login | trainee1.tzafona@test | SKIPPED (covered from manager side) |

## Verified end-to-end flows (34 checks PASS)

**Auth & identity:** login/logout (logout = header POST; GET /Auth/Logout is a JS-driven page), lockout fixtures untouched, Hebrew display names, no onboarding wizard (seeded flag respected), role-appropriate nav for every tier (Employee < Lead < Kabar < MolAdmin < Owner incl. Director Tools / System sections).

**Requests → approval pipeline (full round trip):** soldier files Regular Vacation (20-21/07) → lead receives bell notification + "Action Required" chip in real time → kabar sees it in approvals inbox with full details → Approve → header flips to "All Clear", Approved(1) tab → vacation MATERIALIZES as exactly 2 vacation badges on the soldier's Overview row. Swap panel correctly empty for shift-less user.

**Shift assignment (3 surfaces):** inline typeahead (molecule-wide 80-user alhut pool incl. cross-desk, Hebrew filtering), bottom-sheet Add Assignment (same pool), draft mode (banner, ?DraftMode=true, ghost chip, Commit → live). Assignment persists across reloads.

**Guard rails:** rest-hours policy BLOCKED a same-day Morning→Afternoon double-booking ("Assignment violates minimum rest hours requirement"); Employee sees Read-Only calendars; eligibility pools are job-type scoped.

**Trainee (נחפף):** chip + → company-scoped trainee dropdown (4 QA + seeded טליה בר) → two-cube render (soldier + trainee w/ icon) in one cell.

**Chores:** DoesChores gating exact (העיר accordion = 22 = 20 soldiers + 2 leads; kabar/trainee excluded); moladmin created chore type שטיפת כלים (audit-logged) → appears in legend → assigned to soldier → "Chore created successfully".

**On-call:** kabar has ManageOnDuty (server-verified); duty rows Hakam/Lead/Backup-hakam; Justice fairness panel opens in context; bottom-sheet duty assign → "On-duty assignment created successfully".

**Multi-company (מילואים):** mil4 gets a switcher dropdown listing exactly its 3 seeded memberships (חמסה/קבה"ח/מטות under GEFEN); switching works; /My/Requests REDIRECTS (Mil capability gate).

**GroupUser:** retains kabar-level powers + switcher, absent from all rosters/calendars.

**Tech molecule (Shikma):** Delta/Hanava/Moviltech/Yekev tech shifts render, no job-type filter, פרויקטור assigned successfully.

**QA molecule:** the 8 shift types seeded by BulkTestUserSeed render; board fully functional (was empty before the seeder).

**Owner:** sees 36/36 companies; /Admin/Users fine with 616 users; Overview editable.

**Hebrew/RTL:** full RTL flip, sidebar right, "צופה ב-36 מתוך 36 דסקים" (דסק terminology correct), Hebrew nav complete.

---

## Bugs found (ranked)

### P1 — Server NRE on on-call inline typeahead assign
`POST /Calendar/Table?handler=AssignEmployee` → `NullReferenceException` at `Pages/Calendar/Table.cshtml.cs:818` (`OnPostAssignEmployeeAsync`, LINQ param `request.ShiftTypeId` on null) when the assignment originates from the ON-CALL calendar's inline typeahead. User sees generic "Failed to assign employee" ×3. Bottom-sheet path works → the inline path posts an incomplete payload for duty cells. Repro: On-Call calendar → click duty cell → type name → click suggestion.

### P2 — Feature-reachability: Team calendar Read-Only for EVERY role
Employee, Lead, Kabar, MoleculeAdmin AND Owner all get "Read-Only Mode" on /Calendar/Team — the Team leg of the manual time-off feature (merge `2c90a89`) is unreachable by any role tested. Overview is editable (Owner verified). Either a gating regression or an intentional restriction that contradicts the feature spec — needs a product call.

### P2 — Toast renders with full-height white backdrop panel
Every success/error toast (assign, trainee, rest-hours, chore) renders inside a full-height white column overlaying the right side of the calendar until dismissed. Matches the known stuck-feedback-backdrop artifact (`03-stuck-feedback-backdrop.png`). Repro: any calendar toast.

### P2 — Renderer/compositor stalls after sheet/typeahead interactions
Repeatedly, right after bottom-sheet or typeahead interactions (On-Call, Shifts, Overview), CDP screenshot capture times out for ~30s+ while JS stays responsive; 6 active animations found at the time. Same family as the backdrop bug — suspect an infinite animation keeping the compositor busy. Users may perceive occasional jank/hangs.

### P3 — On-call picker: duplicate names indistinguishable
Area-wide pool (609 users) shows e.g. three identical "רועי שלום" entries with no company/desk badge. Needs disambiguation (company tag in dropdown).

### P3 — Cosmetics
- `[plus-circle]` literal text instead of icon on /Requests "Request time off" button.
- Drag-handle glyph "⋮⋮" leaks into duty-type names in bottom sheet ("No ⋮⋮ Hakam assigned on Sat, Jul 18").
- "Requests" nav item still visible for Mil users (dead link → redirect).
- Chore inline editor: Enter with focus on badge (not title input) silently discards (no POST).

### Notes (not bugs)
- Health check flagged Unhealthy: dev app memory 1083MB > 800MB threshold (616 users + heavy browsing).
- Email sends fail gracefully in dev (`Error_MailService_NotConfigured` warnings) — expected without SMTP.
- ChoreTypes create logs "ModelState is Invalid" yet succeeds — validation-logging noise.
- `RequestLoggingMiddleware` logs `UserId=anonymous` on authenticated POSTs — logging-order quirk.

## Not covered (explicit)
- Trainee's own login view (trainee features covered from the manager side).
- Manual time-off inline entry on Overview — surface is editable but the entry interaction wasn't triggered by click+type in automation; verify by hand (may need Quick-Entry-mode-specific interaction).
- Duty rotations auto-fill, swap-request round trip (needs 2 users with swappable shifts), announcements, friendships, justice-table deep checks, master plans/blueprints, email delivery, API surface — deeper passes deferred.

## Artifacts created during the test (left in DB, testing env)
- Approved vacation for יובל כהן 20-21/07 + Hakam duty רועי שלום Sat 18/07 + shift assignments (יובל כהן+טליה בר Sat Morning; נועה לוי Tue Morning via draft; אסף אברהם Hanava Sat)
- Chore type שטיפת כלים (kitchen_duty, molecule ערבה) + one Dishes chore for אורי חדד Sat
