# Admin UI Fixes — Design Spec (Sub-project C)

- **Date:** 2026-06-14
- **Branch:** dev
- **Scope:** Presentation-only fixes to two admin pages. No business logic, auth, or data-model changes.
- **Sequencing:** This is sub-project **C** of a three-part effort. It ships first because it is independent of the other two. The remaining specs are:
  - **A — New account types** (`mil`, `groupuser`): orthogonal `AccountType` enum + central capability gate. (Recommended model recorded; finalized in spec A.)
  - **B — Analytics logic & access**: lead+ gatekeeping, filter does-shifts-off, shift-category breakdowns, chart readability + tooltips + new category charts.

## Goal

1. Make the `/Admin/Users` table fit within the screen container instead of breaking the page layout out of the viewport.
2. Fix the `/Admin/Analytics` donut chart so it renders intact and its text is readable, in light + dark + Hebrew RTL.

Both are quick-wins that leave a clean canvas for specs A and B.

## Out of scope (explicit boundaries)

- Donut **readability enhancements** beyond "not-broken + readable": tooltips, descriptive captions, and the new shift-category breakdown charts belong to **spec B**.
- Any change to the analytics query, fairness math, band colors, or aria semantics.
- Any change to the Users table columns, data, or authorization.

---

## Part 1 — `/Admin/Users` responsive table (constrained horizontal scroll)

### Verified current state
- `Pages/Admin/Users.cshtml:901` — wrapper: `<div style="overflow-x: clip; overflow-y: visible;">`
- `Pages/Admin/Users.cshtml:902` — table: `class="data-table data-table--sticky-header data-table--sticky-col" style="min-width: 900px;"` (11 columns: Name, Email, Company, JobType, Department, DoesShifts/Participates, Role, Grants, Status, Password, Actions).
- Sticky styles: `wwwroot/css/site.css:7193–7212` — header pins to `top: var(--header-height)`; first column pins to `inset-inline-start: 0`.
- Table base styles: inline `<style>` in `Users.cshtml:149–175`.

### Root cause (confirm live before coding)
The page widens past the viewport because:
1. The wrapper uses `overflow-x: clip` — it clips the 900px-min-width table instead of letting it scroll, so on narrow widths content is cut off; and
2. The ancestor flex/grid content column almost certainly lacks `min-width: 0`, so it refuses to shrink below the table's intrinsic min-width and pushes the entire layout wider than the viewport (the same `min-width: 0`-at-every-link issue documented for the calendar sticky chain).

Diagnosis step (implementation): run the app, open `/Admin/Users`, confirm which ancestor link in `.app-shell → .app-main → .app-content → page container` is missing `min-width: 0`, and confirm the wrapper clip vs. scroll behavior. Fix the actual links found — do not blindly apply to all.

### Approach
1. Replace the inline-styled wrapper with a reusable `.table-scroll` class defined in CSS (not inline), with `overflow-x: auto` so horizontal scroll happens **inside** the content area.
2. Add `min-width: 0` to the confirmed ancestor link(s) around the table so the wrapper can shrink to viewport width.
3. Keep a sensible table `min-width` so columns stay legible, **while preserving the sticky header and sticky first column**. Validate that turning the wrapper into a horizontal scroll container does not disturb vertical sticky behavior (the one subtle risk); adjust overflow axes as needed against the running app.

### Acceptance criteria
- At desktop, tablet, and ~375px narrow widths: **no horizontal scrollbar on the page body and no layout breakout**; horizontal scrolling occurs only inside the table wrapper.
- Sticky header and sticky first column continue to work while scrolling.
- Verified visually in light + dark + Hebrew RTL.

---

## Part 2 — `/Admin/Analytics` donut-wrap structural + contrast fix

### Verified current state
- Donut markup: `Pages/Admin/Analytics.cshtml:519–551` (inline SVG; `transform: rotate(-90deg); overflow: visible`; segments via `stroke-dasharray`/`stroke-dashoffset`).
- Legend: `Analytics.cshtml:553+`.
- CSS: `wwwroot/css/justice.css:682–728` — `.donut-wrap` is `width:200px; height:200px; flex-shrink:0`; `.donut-center-val` uses `color: var(--text)`; `.donut-center-label` and `.dl-item` use `color: var(--text-muted)`.

### Approach (diagnose-first — sequencing, not a deferral)
1. Run the app → `/Admin/Analytics`, capture the donut in light + dark + Hebrew RTL to identify the actual breakage: flex hero not sizing the donut vs. an ancestor clipping the rotated `overflow:visible` SVG vs. legend overflow vs. pure text contrast.
2. Fix the structural issue found.
3. Apply project contrast discipline to the text: pair backgrounds with explicit contrast tokens, never rely on `--text-muted` over a colored fill, ensure the center value/label and legend resolve to readable colors in light + dark + RTL.

### Acceptance criteria
- Donut renders as an intact ring with all segments visible (not clipped or collapsed).
- Center total + label and the legend are clearly readable in light + dark + Hebrew RTL.
- No regression to existing arc math, band colors, or aria labels (`Justice_DonutAria`, `Justice_DonutTotal`, `Justice_DonutLegend_Aria`, per-segment `aria-label`).
- Scope strictly "not-broken + readable" — tooltips/captions/category charts remain in spec B.

---

## Cross-cutting

- **CSS locations:** Users inline `<style>` + `site.css` (table); `justice.css` (donut). All load directly or via `asp-append-version="true"` — no `@import` cache-bust trap. **The dev app serves stale static assets until restarted**, so restart the dev app and `curl` the served file for an edit marker before browser-verifying.
- **Localization:** No business strings change. If a new wrapper needs an aria-label, add the key to **both** `SharedResources.resx` and `SharedResources.he-IL.resx`.
- **No data, no auth, no migration.**

## Verification

- Before/after screenshots of both pages in **light + dark + Hebrew RTL** at **3 widths** (desktop, tablet, ~375px).
- Confirm no page-body horizontal scrollbar on `/Admin/Users`; confirm sticky header + first column still pin.
- Confirm donut ring intact and all text legible on `/Admin/Analytics`.
- Existing test suite remains green (sequential run per project convention; no new behavior to unit-test for pure CSS, but ensure no Razor/markup compile regressions).

## Risks

- **Sticky vs. scroll-container interaction** (Part 1): making the table wrapper a horizontal scroll container can break vertical sticky. Mitigation: validate against the running app; keep page-level vertical scroll on `.app-content`.
- **Guessing contrast** (Part 2): the project's #1 recurring visual bug. Mitigation: diagnose from real screenshots first; verify in all three modes before claiming done.
