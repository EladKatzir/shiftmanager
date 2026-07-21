# Calendar Tabs — Phase A (Quick Fixes) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the two standalone frontend bugs — Quick Entry hijacking clicks on the trainee picker (Bug 2), and the raw "already assigned" error string on a concurrent-edit collision (Bug 4, error-message half only).

**Architecture:** Both are surgical edits to existing calendar JS. Bug 2 adds one selector to an ignore-list; Bug 4 adds one `errorKey` branch to an existing switch. No backend, no schema, no new files.

**Tech Stack:** ASP.NET Core 8 Razor Pages, vanilla JS (`wwwroot/js/*.js`, no bundler), SignalR (already wired). Air-gapped Windows/IIS deploy.

## Global Constraints

- **Air-gapped:** no CDN/npm at runtime; never add external requests. (Not exercised here — no new assets.)
- **Bilingual he-IL / en:** user-facing JS strings in these files follow the existing **inline-culture** pattern — `const culture = getCurrentCulture(); culture === 'he-IL' ? '<he>' : '<en>'`. Match it.
- **Dev serves STALE static JS until restart:** `:5000` = the `bin/Debug` exe; edits to `wwwroot/js/*` are NOT served until app restart (or the browser is hard-refreshed against a restarted app). Before browser-testing a JS change, restart the app and `curl` the served file for your edit marker.
- **Build `-c Release`** if you must build while the Debug app is running (avoids the locked-exe rebuild-stale trap).
- **Branch:** all work on `feat/calendar-tabs-selectors` (created off `dev`). Do not commit to `dev`.
- **Scope of this plan:** Bug 4's real-time auto-refresh (PF3) is intentionally deferred to the Tabs calendar-integration phase (it must be tab-filtered and would otherwise be built twice). This plan ships only the friendly error message with honest "please refresh" wording.

---

### Task 1: Bug 2 — Quick Entry must ignore clicks on the trainee picker

The Quick-Entry cell handler (`handleCellClick`) has an allow-list of elements it should NOT intercept. The runtime-injected trainee picker `<select class="excel-calendar__trainee-picker">` is inserted as a sibling of the assignment chip, still inside `.excel-calendar__cell`, and matches none of the ignore selectors — so Quick Entry swallows the click and opens its own dropdown instead of the trainee picker.

**Files:**
- Modify: `wwwroot/js/calendar-quick-entry.js:955-962` (the ignore-list in `handleCellClick`)

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing consumed by later tasks (self-contained behavioral fix).

- [ ] **Step 1: Reproduce the bug in the browser (confirm it fails)**

Restart the app, open `http://localhost:5000/Calendar/Shifts` (login as an owner/lead, seeded molecule with an assignment that shows the trainee `+`). Enable Quick Entry mode (the `#quickEntryToggle` button). Click a shift assignment's `+` trainee button, then click the `<select>` that appears.
Expected (bug present): the **Quick Entry input** opens instead of the native trainee dropdown.

- [ ] **Step 2: Add the picker selectors to the ignore-list**

In `wwwroot/js/calendar-quick-entry.js`, extend the guard block (currently lines 955-962):

```javascript
        // Don't intercept clicks on existing assignments or buttons
        if (e.target.closest('.excel-calendar__assignment') ||
            e.target.closest('.excel-calendar__add-btn') ||
            e.target.closest('.excel-calendar__trainee-picker') ||
            e.target.closest('.fill-handle') ||
            e.target.closest('button') ||
            e.target.closest('a') ||
            e.target.closest('select')) {
            return;
        }
```

(Two additions: `.excel-calendar__trainee-picker` — the injected picker — and a defensive bare `select` so any in-cell native select the user opens is left to the browser.)

- [ ] **Step 3: Restart the app and confirm the served file changed**

Restart the app (`-c Release` if the Debug exe is locked). Then:
Run: `curl -s http://localhost:5000/js/calendar-quick-entry.js | grep -c "excel-calendar__trainee-picker"`
Expected: `1` (the served file now contains the new selector).

- [ ] **Step 4: Verify the fix in the browser**

Repeat Step 1's interaction. Expected (fixed): clicking the trainee `<select>` opens the **native trainee dropdown**; choosing a trainee assigns them. Then click a *different empty cell* — Quick Entry still opens normally there (no dead state).

- [ ] **Step 5: Commit**

```bash
git add wwwroot/js/calendar-quick-entry.js
git commit -m "fix(calendar): stop Quick Entry from hijacking trainee-picker clicks (Bug 2)"
```

---

### Task 2: Bug 4 (message half) — friendly "already assigned" collision message

When two users edit the same grid and the second assigns an already-assigned user, the server returns `{ success:false, error:"User is already assigned to this shift", errorKey:"ALREADY_ASSIGNED" }` (HTTP 200). The client's `quickAddShift` has no branch for this errorKey, so it falls through to the raw-error toast (`showToast(result.message || result.error || 'Error', 'error')`). Add an explicit branch — mirroring the existing `SHIFT_FULLY_STAFFED` errorKey handling — that shows a friendly, guidance-bearing message.

**Files:**
- Modify: `wwwroot/js/calendar-inline-edit.js:571-573` (the final `else` in `quickAddShift`'s result handling)

**Interfaces:**
- Consumes: `result.errorKey` (already present in the AssignEmployee JSON response — same field the `SHIFT_FULLY_STAFFED` branch at `:527-529` reads), `getCurrentCulture()`, `showToast(message, severity)` (both already used throughout this file).
- Produces: nothing consumed by later tasks.

> Wording note: this ships the **honest** wording ("Please refresh the page…") because Phase A does NOT include the auto-refresh (PF3). When the Tabs calendar-integration phase adds the server-rendered auto-refresh, this string is upgraded to "…The calendar has been refreshed."

- [ ] **Step 1: Reproduce the raw error (confirm it fails)** `[2-browser]`

Two browser sessions on the same shift calendar. In session A, assign user X to a shift. In session B (whose grid is now stale), assign the **same** user X to the **same** shift.
Expected (bug present): session B shows a red error toast reading the raw string "User is already assigned to this shift" with no guidance.

- [ ] **Step 2: Add the `ALREADY_ASSIGNED` branch**

In `wwwroot/js/calendar-inline-edit.js`, replace the final `else` of `quickAddShift`'s result handling (currently lines 571-573):

```javascript
        } else if (result.errorKey === 'ALREADY_ASSIGNED') {
            // Concurrent edit: another user already assigned this person to this shift.
            // (Auto-refresh of the peer grid arrives with the Tabs calendar-integration phase;
            // until then, guide the user to refresh.)
            const culture = getCurrentCulture();
            const msg = culture === 'he-IL'
                ? 'המשתמש כבר משובץ למשמרת זו. יש לרענן את הדף כדי לראות את השינויים האחרונים.'
                : 'The user is already assigned to this shift. Please refresh the page to see the latest changes.';
            showToast(msg, 'warning');
        } else {
            showToast(result.message || result.error || 'Error', 'error');
        }
```

- [ ] **Step 3: Check the same collision path isn't ALSO surfaced by the bottom-sheet assign**

Run: `grep -rn "ALREADY_ASSIGNED\|handler=AssignEmployee" wwwroot/js/`
Expected: confirm whether `calendar-bottom-sheet.js` posts to `AssignEmployee` independently of `quickAddShift`. If it does and shows its own error toast, add the identical `ALREADY_ASSIGNED` branch there; if it delegates to `quickAddShift`, no further change. (Record which in the commit message.)

- [ ] **Step 4: Restart and confirm the served file changed**

Restart the app. Then:
Run: `curl -s http://localhost:5000/js/calendar-inline-edit.js | grep -c "ALREADY_ASSIGNED"`
Expected: `>= 1`.

- [ ] **Step 5: Verify the fix in the browser** `[2-browser]`

Repeat Step 1. Expected (fixed): session B shows a dismissible **warning** toast with the friendly, localized message (verify in both EN and HE cultures — set the `.AspNetCore.Culture` cookie `c=he-IL|uic=he-IL` for the Hebrew check); the selection is not undone and no raw string appears.

- [ ] **Step 6: Commit**

```bash
git add wwwroot/js/calendar-inline-edit.js
# add wwwroot/js/calendar-bottom-sheet.js too if Step 3 required it
git commit -m "fix(calendar): friendly message on concurrent 'already assigned' collision (Bug 4, message half)"
```

---

## Phase A done — what ships

- Bug 2 fully fixed (Quick Entry no longer hijacks the trainee picker).
- Bug 4's raw error replaced with a friendly, bilingual, non-blocking warning.
- Bug 4's real-time auto-refresh (PF3) is carried into the Tabs calendar-integration phase (Plan 5), where it is built once, tab-filtered, with full server-rendered chip markup.

**Acceptance checklist items covered:** QE-1, QE-2 (Bug 2); CON-2's message half. (Full CON-1..5 auto-refresh verification is deferred to Plan 5.)
