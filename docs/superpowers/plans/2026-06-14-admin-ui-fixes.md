# Admin UI Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `/Admin/Users` 11-column table fit inside the viewport (no page breakout) and fix the `/Admin/Analytics` donut chart so it renders intact with readable text — in light + dark + Hebrew RTL.

**Architecture:** Presentation-only. Two independent fixes, both diagnose-first (capture real screenshots, identify the actual root cause, then apply the narrowest-scope CSS/markup fix). No business logic, auth, data, or migration changes. Reuse the existing `.table-responsive` utility rather than introducing new wrapper classes (DRY).

**Tech Stack:** ASP.NET Core 8 Razor Pages, plain CSS (no build step for CSS — static assets in `wwwroot/css`), Playwright (via the `webapp-testing` skill) for visual verification. Dev app on `http://localhost:5000`.

**Spec:** `docs/superpowers/specs/2026-06-14-admin-ui-fixes-design.md`

---

## Environment & workflow rules (read once before starting)

- **Branch:** `dev` (already checked out). Do **not** sweep unrelated working-tree changes into commits — `git add` only the exact files each task names.
- **Stale-asset trap:** the running dev app serves static assets from its loaded copy. **Source edits to `wwwroot/css/*` and `.cshtml` are NOT served until the app is rebuilt/restarted.** After every edit, you MUST restart and confirm the served file changed via a `curl` marker before browser-verifying. (Project memory: `dev_app_stale_static_assets`.)
- **Locked-executable rule (global CLAUDE.md §3):** never rebuild while the app is running. Stop the process first, confirm it's dead, then rebuild. Never retry a build through a lock error.
- **Restart recipe** (used in multiple tasks):
  ```bash
  # 1. Stop the running app (PowerShell)
  powershell -Command "Get-Process -Name ShiftManager -ErrorAction SilentlyContinue | Stop-Process -Force"
  # 2. Confirm it's gone (expect no output)
  powershell -Command "Get-Process -Name ShiftManager -ErrorAction SilentlyContinue"
  # 3. Rebuild + run (foreground in its own terminal, or background)
  dotnet run --urls http://localhost:5000
  # 4. Wait until it logs "Now listening on: http://localhost:5000"
  ```
- **Login for verification:** browse to `http://localhost:5000/Auth/Login`. Use an **Owner/admin QA account** (an `@test` account; password `Test1234!`) that holds both `ManagerHomeAccess` (for `/Admin/Users`) and `ViewJusticeTable` (for `/Admin/Analytics`). If unsure which account, pick the Owner seed user.
- **Theme + language toggles:** dark mode via the sidebar theme switch; Hebrew (RTL) via the language switcher (sets the culture cookie). Verify all three modes: light-LTR(English), dark-LTR(English), Hebrew-RTL.
- **Screenshot widths:** desktop (~1440px), tablet (~820px), narrow (~375px).

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `Pages/Admin/Users.cshtml` | Users admin page markup + page-scoped `<style>` | Modify: swap the inline `overflow-x: clip` wrapper (line 901) for the shared `.table-responsive` class |
| `wwwroot/css/site.css` | Shared table utilities (`.table-responsive` @ 2719) + layout shrink fix | Modify: only if diagnosis shows an ancestor needs `min-width: 0` |
| `wwwroot/css/navigation.css` | `.app-main` / `.app-content` layout chain | Modify: only the `min-width: 0` branch, if diagnosis points here |
| `Pages/Admin/Analytics.cshtml` | Analytics donut markup (519–551) | Modify: only if the structural fix is markup-level |
| `wwwroot/css/justice.css` | Donut + legend styles (682–728) | Modify: structural sizing + text contrast fixes |
| `docs/superpowers/specs/2026-06-14-admin-ui-fixes-design.md` | Approved spec | Reference only |

---

## Task 1: Baseline diagnosis (both pages)

**Goal:** Capture before-state screenshots and determine the *actual* root cause of each bug. No code changes in this task — its output is two written determinations that gate Tasks 2 and 3.

**Files:** none (investigation only).

- [ ] **Step 1: Ensure the app is running and you can log in**

Run the Restart recipe above (or confirm it's already up). Then, using the `webapp-testing` skill (Playwright), navigate to `http://localhost:5000/Auth/Login` and log in with the Owner/admin QA account.

Expected: you land authenticated and can open `/Admin/Users` and `/Admin/Analytics` without a 403.

- [ ] **Step 2: Capture `/Admin/Users` baseline in all three modes × three widths**

For each mode (light-LTR, dark-LTR, Hebrew-RTL) and each width (1440, 820, 375), screenshot `/Admin/Users`. Save to `docs/superpowers/plans/artifacts/users-before-<mode>-<width>.png`.

Expected: at least one mode/width reproduces the breakout (page-level horizontal scrollbar, or content extending past the viewport edge / sidebar/header misalignment).

- [ ] **Step 3: Diagnose the Users breakout mechanism**

In the Playwright page, run these checks at the 820px width where it breaks and record the answers:

```javascript
// Is the body/page itself wider than the viewport? (true => layout breakout, not just a clipped table)
document.documentElement.scrollWidth > document.documentElement.clientWidth
// Which element is the widest offender — walk up from the table:
(() => { let el = document.querySelector('#usersTable'); const out=[];
  while (el) { const cs = getComputedStyle(el);
    out.push({tag: el.tagName, cls: el.className, w: el.scrollWidth,
              overflowX: cs.overflowX, minWidth: cs.minWidth, display: cs.display});
    el = el.parentElement; }
  return out; })()
```

Record the determination as ONE of:
- **(A) Wrapper-clip only:** body is NOT wider than viewport, table is just cut off → fix = swap wrapper to `.table-responsive` (Task 2 Step 1 only).
- **(B) Flex-item won't shrink:** body IS wider than viewport, and an ancestor (`.app-main` / `.app-content` / page container) has `overflowX: visible` + no effective `min-width:0` → fix = Task 2 Step 1 **plus** add `min-width: 0` to that specific ancestor (Task 2 Step 2).

- [ ] **Step 4: Capture `/Admin/Analytics` baseline in all three modes**

Screenshot `/Admin/Analytics` in light-LTR, dark-LTR, Hebrew-RTL at 1440px. Save to `docs/superpowers/plans/artifacts/analytics-before-<mode>.png`.

Expected: the donut and/or its surrounding text reproduce the reported breakage.

- [ ] **Step 5: Diagnose the donut breakage**

In Playwright on `/Admin/Analytics`, record:

```javascript
(() => { const w = document.querySelector('.donut-wrap'); const svg = document.querySelector('.donut-svg');
  const inner = document.querySelector('.hero-chart-inner'); const val = document.querySelector('.donut-center-val');
  const lab = document.querySelector('.donut-center-label');
  const r = el => el ? el.getBoundingClientRect() : null;
  return {
    wrap: r(w), svg: r(svg), inner: r(inner),
    wrapW: w && getComputedStyle(w).width, wrapH: w && getComputedStyle(w).height,
    innerDisplay: inner && getComputedStyle(inner).display,
    valColor: val && getComputedStyle(val).color,
    labColor: lab && getComputedStyle(lab).color,
    // is the SVG visually clipped by an ancestor?
    svgClippedBy: (() => { let el = svg; while (el) { const cs = getComputedStyle(el);
      if (['hidden','clip','auto','scroll'].includes(cs.overflow) || cs.overflow.includes('hidden')) return {tag:el.tagName, cls:el.className, overflow: cs.overflow}; el = el.parentElement;} return null; })()
  }; })()
```

Record the determination as a combination of:
- **Structural:** is `.donut-wrap` collapsed (width/height not 200×200), or is the SVG clipped by an ancestor with `overflow:hidden/clip`, or is `.hero-chart-inner` not laying out (display/flex collapse)?
- **Contrast:** do `valColor` / `labColor` resolve to a color with poor contrast against the actual background behind the donut hole (check in dark mode especially)?

- [ ] **Step 6: Commit the baseline artifacts + determinations**

Write the two determinations into a short note `docs/superpowers/plans/artifacts/diagnosis-2026-06-14.md` (Users = A or B; Donut = structural cause + contrast yes/no).

```bash
git add docs/superpowers/plans/artifacts
git commit -m "test(ui): baseline screenshots + root-cause diagnosis for Admin UI fixes"
```

---

## Task 2: Fix `/Admin/Users` responsive table

**Files:**
- Modify: `Pages/Admin/Users.cshtml:901` (the table wrapper `<div>`)
- Modify (branch B only): `wwwroot/css/navigation.css` (`.app-main` or `.app-content`) **or** `wwwroot/css/site.css` — whichever ancestor Task 1 Step 3 identified.

- [ ] **Step 1: Replace the inline-clip wrapper with the shared `.table-responsive` utility**

In `Pages/Admin/Users.cshtml`, change line 901 from:

```html
    <div style="overflow-x: clip; overflow-y: visible;">
```

to:

```html
    <div class="table-responsive">
```

Rationale: `.table-responsive` (`site.css:2719`) already provides `overflow-x: auto` (scroll instead of clip) and is the established pattern. The matching `</div>` after the table stays unchanged. Leave the table's `style="min-width: 900px;"` (line 902) intact so columns stay legible and the wrapper scrolls horizontally.

- [ ] **Step 2: (Branch B ONLY — skip if Task 1 determined A) Let the ancestor shrink**

If Task 1 Step 3 determined **(B)**, add `min-width: 0` to the exact ancestor it named. Most likely `.app-main` in `wwwroot/css/navigation.css:1305`:

```css
.app-main {
  min-height: 100vh;
  min-width: 0; /* allow flex item to shrink below intrinsic content width so wide tables scroll internally instead of widening the page */
}
```

If Task 1 named `.app-content` or the page container instead, add `min-width: 0` there rather than to `.app-main`. Apply to the narrowest-scope element identified — do not add it to multiple elements speculatively.

> ⚠️ This touches a shared layout class. After applying, you MUST re-verify a normal page (e.g. `/Admin/Analytics` and a calendar page like `/Calendar/Shifts`) in Step 5 to confirm no layout regression.

- [ ] **Step 3: Add a served-asset marker (only if you edited a CSS file in Step 2)**

If Step 2 edited a `.css` file, append a sentinel comment at the very end of that file so you can confirm the restart served it:

```css
/* C-FIX-MARKER-USERS */
```

(If you only edited `Users.cshtml` in Step 1, the marker check uses the rendered HTML instead — see Step 5.)

- [ ] **Step 4: Restart the app**

Run the Restart recipe (stop → confirm dead → `dotnet run`). Wait for "Now listening on".

- [ ] **Step 5: Verify the served asset + the fix**

First confirm the new code is actually served:

```bash
# If you edited a CSS file in Step 2:
curl -s http://localhost:5000/css/navigation.css | grep C-FIX-MARKER-USERS   # expect a match
# Always: confirm the markup changed (wrapper now uses the class):
curl -s "http://localhost:5000/Admin/Users" -b <auth-cookie> | grep 'class="table-responsive"'
```

Then, via Playwright at 1440 / 820 / 375 in light-LTR, dark-LTR, Hebrew-RTL, assert:

```javascript
document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1   // expect true: no page breakout
// sticky header still pins:
getComputedStyle(document.querySelector('#usersTable thead th')).position === 'sticky'   // expect true
// horizontal scroll now lives on the wrapper:
(() => { const w = document.querySelector('.table-responsive'); return w.scrollWidth > w.clientWidth ? 'scrolls-internally' : 'fits'; })()
```

Expected: no page-body horizontal scrollbar in any mode; sticky header + sticky first column still pin while scrolling the wrapper. Save after-screenshots to `artifacts/users-after-<mode>-<width>.png`.

- [ ] **Step 6: Commit**

```bash
git add Pages/Admin/Users.cshtml
# include the css file ONLY if you edited it in Step 2 (and remove the marker comment first):
# git add wwwroot/css/navigation.css
git commit -m "fix(admin/users): responsive table via .table-responsive wrapper (no page breakout)"
```

> Before committing, delete the `C-FIX-MARKER-USERS` sentinel comment you added in Step 3.

---

## Task 3: Fix `/Admin/Analytics` donut (structural + contrast)

**Files:**
- Modify: `wwwroot/css/justice.css` (`.donut-wrap` / `.donut-center-*` / `.hero-chart-inner`, 682–728)
- Modify (only if structural fix is markup-level): `Pages/Admin/Analytics.cshtml:519–551`

- [ ] **Step 1: Apply the structural fix identified in Task 1 Step 5**

Based on the recorded structural cause, apply the matching fix in `wwwroot/css/justice.css`:

- If **`.donut-wrap` collapsed** (not 200×200): it already has fixed `width/height` + `flex-shrink:0` (682–686); the collapse is therefore the *parent* `.hero-chart-inner` not giving it room. Ensure the inner is a column flex that doesn't squeeze it:

```css
.hero-chart-inner {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  min-width: 0;
}
```

- If the **SVG is clipped by an ancestor** (`svgClippedBy` non-null): the SVG uses `overflow: visible` and `transform: rotate(-90deg)`; the decorative tick ring at `r=94` extends past the 200×200 box. Give the wrap a little breathing room instead of letting an ancestor clip it:

```css
.donut-wrap {
  position: relative;
  width: 200px; height: 200px;
  flex-shrink: 0;
  overflow: visible;        /* ensure the r=94 tick ring isn't clipped */
}
```

Apply only the branch(es) the diagnosis supports. If diagnosis found no structural issue (pure contrast), skip to Step 2.

- [ ] **Step 2: Fix text contrast (center value, center label, legend)**

In `wwwroot/css/justice.css`, make the donut text robust against the background behind it in all themes. Replace the existing `.donut-center-val` / `.donut-center-label` color declarations (693–700) with explicit, theme-safe tokens, and ensure the legend name/share are readable:

```css
.donut-center-val {
  font-size: 1.9rem; font-weight: 800; letter-spacing: -0.04em;
  color: var(--text); line-height: 1; font-variant-numeric: tabular-nums;
}
.donut-center-label {
  font-size: 0.64rem; color: var(--text); opacity: 0.75; font-weight: 600;
  letter-spacing: 0.05em; text-transform: uppercase; margin-top: 2px;
}
```

Rationale (project rule): never rely on `--text-muted` for small text that must stay readable; use `--text` with a controlled `opacity` so it adapts to both themes. If Task 1 found the legend text (`.dl-item`, `.dl-share` @ 709/719 — `--text-muted`) unreadable in dark mode, apply the same `color: var(--text); opacity: 0.7` treatment there.

> Do NOT add a blanket `!important` on a base element, and do NOT introduce undefined custom properties (project memory: CSS contrast rules). Verify the resolved color in dark mode in Step 4.

- [ ] **Step 3: Add a served-asset marker + restart**

Append to the end of `wwwroot/css/justice.css`:

```css
/* C-FIX-MARKER-DONUT */
```

Then run the Restart recipe.

- [ ] **Step 4: Verify served asset + the fix in all three modes**

```bash
curl -s http://localhost:5000/css/justice.css | grep C-FIX-MARKER-DONUT   # expect a match
```

Via Playwright on `/Admin/Analytics` in light-LTR, dark-LTR, Hebrew-RTL at 1440px, assert and eyeball:

```javascript
(() => { const w = document.querySelector('.donut-wrap'); const r = w.getBoundingClientRect();
  return { intact: Math.round(r.width) === 200 && Math.round(r.height) === 200,
           segs: document.querySelectorAll('.donut-seg').length }; })()
// expect intact:true and segs > 0 (ring renders with all segments)
```

Eyeball each saved screenshot `artifacts/analytics-after-<mode>.png`: ring is a full intact donut (all segments visible, tick ring not clipped); center total + label clearly readable; legend readable. Confirm **dark mode** specifically — that's where contrast regressions hide.

- [ ] **Step 5: Commit**

```bash
git add wwwroot/css/justice.css
# include Analytics.cshtml ONLY if you changed markup in Step 1:
# git add Pages/Admin/Analytics.cshtml
git commit -m "fix(admin/analytics): donut renders intact with readable text (light/dark/RTL)"
```

> Before committing, delete the `C-FIX-MARKER-DONUT` sentinel comment.

---

## Task 4: Full cross-mode verification + suite

**Files:** none (verification + final state).

- [ ] **Step 1: Confirm both fixes coexist with no regression**

Restart once more (clean state). Walk `/Admin/Users` and `/Admin/Analytics` in light-LTR, dark-LTR, Hebrew-RTL at 1440 / 820 / 375. Confirm:
- Users: no page breakout, sticky header + first column pin, horizontal scroll inside wrapper only.
- Analytics: donut intact + all text legible.
- A control page unaffected: open `/Calendar/Shifts` and confirm the sticky calendar still works (guards against the Task 2 Branch-B shared-layout change).

- [ ] **Step 2: Confirm no markup/compile regression**

```bash
dotnet build
```

Expected: build succeeds with no new errors (stop the running app first per the locked-executable rule).

- [ ] **Step 3: Run the test suite (sequential — project convention)**

```bash
dotnet test -- xUnit.ParallelizeTestCollections=false
```

Expected: same green baseline as before this work (no new failures). These are CSS/markup changes, so no behavioral test count change is expected; this run guards against an accidental Razor break.

- [ ] **Step 4: Confirm clean, scoped commits**

```bash
git log --oneline -4
git status   # working tree should show only your intended files + the pre-existing unrelated changes you did NOT touch
```

Expected: 3–4 commits from this plan (baseline, users fix, donut fix, optional artifacts), none of them sweeping in the unrelated QA-sweep / vacation-approval working-tree files.

---

## Done criteria (maps to spec acceptance)

- [ ] `/Admin/Users`: no page-body horizontal scrollbar / no breakout at desktop/tablet/375px; sticky header + first column intact; verified light + dark + RTL. *(Spec Part 1)*
- [ ] `/Admin/Analytics`: donut renders as an intact ring; center total + label + legend readable; no regression to arc math, band colors, or aria labels; verified light + dark + RTL. *(Spec Part 2)*
- [ ] Scope held to "not-broken + readable" — no tooltips/captions/category charts (those are spec B).
- [ ] `dotnet build` clean; test suite at prior green baseline.
- [ ] No localization keys changed (none needed — pure presentation).
