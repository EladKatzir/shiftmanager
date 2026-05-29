# Calendar Sticky Headers Overhaul — Design Spec

**Date:** 2026-05-29
**Status:** Approved (design); ready for implementation planning
**Component:** `Pages/Shared/Components/ExcelCalendarTable/` (shared by `/Calendar/Shifts`, `/Calendar/OnCall`, `/Calendar/Chores`, `/Calendar/Overview`)
**Grants:** none added or changed
**Scope tier:** "Full polish" — option 3 of 4 in the brainstorm, with every reviewer extension folded in

---

## 1. Goal

When a user scrolls in a calendar grid — far down a long list of users, far across a month-view of date columns, or both — they must always know three things: **what date am I looking at**, **whose row is this**, and **which team / group is this row part of**. They must also keep one-tap access to the toolbar controls (mode toggle, navigation arrows, filter, distribution lists) regardless of scroll depth.

The current implementation **intends** this via `position: sticky` on the date row (Zone A), left column (Zone B), and corner (Zone D), but Zone B does not engage in production despite the CSS looking correct. The group header band (Zone C) and the toolbar have no sticky CSS at all. This spec addresses all four zones plus a set of latent bugs and accessibility gaps the brainstorm reviewers (UI + UX) surfaced along the way.

---

## 2. Locked product decisions

| Topic | Decision |
|---|---|
| **Scroll model** | Inner scroll inside `.excel-calendar` on BOTH axes. The `.cal-toolbar` pins to `.cal-page` (sibling of `.excel-calendar`, not inside it). The viewport's vertical scroll is owned by the flex chain; there is no page-body horizontal scrollbar. |
| **Sticky zones** | A: date header row (already works ✅, gains shadow). B: left column of names / shift types (broken — fix root cause). C: group header band (new sticky). D: corner cell (already sticky ✅, gains shadow + mode label). Plus: cal-toolbar pinned to top of `.cal-page`. Plus: readonly banner pinned as a second tier below the toolbar. |
| **Group band behavior** | Stack-and-replace: the current group's band sticks at `top: var(--excel-calendar-header-height)`, and when the next group's band scrolls up to meet it, it pushes the previous one off screen. Matches Notion / Linear / macOS Finder. |
| **Collapsed groups** | When a group is collapsed, its band is set to `position: static` — it's the only visible row of the group, so pinning would be a no-op that creates a double-border glitch. |
| **Edge shadows** | Scroll-activated (not always-on). Appear on `scrollLeft > 0` / `scrollTop > 0` via IntersectionObserver against 1px sentinels. Opacity transition only (never transition `box-shadow` itself — causes flicker). |
| **Magic-number height** | Eliminated. `max-height: calc(100vh - 200px)` on `.excel-calendar` is replaced by a height-locked flex-column chain: `.app-shell → .app-main → .app-content → .cal-page → .excel-calendar`, every link with `min-height: 0` so children can shrink. |
| **RTL discipline** | Every `left: 0` / `right: 0` in calendar.css converts to `inset-inline-start: 0` / `inset-inline-end: 0`. Edge `box-shadow` does NOT respect `dir` — explicit `[dir="rtl"]` overrides flip the sign on inline-axis shadows. |
| **Auto-condense toolbar** | After 200px of vertical scroll, the sticky toolbar reduces padding and hides selector labels (icon-only nav buttons). Class `.is-condensed` applied via the scroll listener. |
| **Mobile collapse** | At `< 768px`, mode toggles + DL + Filter + JustMine collapse into a "Tools ▾" overflow that opens a bottom-sheet. Always-visible: date nav + view-mode + molecule. |
| **Next group jump** | A "↓" chevron button inside each sticky group band, only rendered when a next group exists. Click scrolls to the next group's natural band position. `behavior: 'smooth'` when motion is allowed; `'auto'` under `prefers-reduced-motion`. |
| **Mode token in corner** | The corner cell renders the active calendar mode ("Shifts ▾" / "People ▾" / "Duty ▾" / "Chores ▾") — gives row labels constant context. The chevron is decorative (not interactive in v1; reserved for a future quick-switch). |
| **Group count format** | When a filter is active, group bands show `(visible/total)` (e.g., `(3/6)`). When no filter is active, just `(total)`. Recomputed on `applyFilter` / `clearFilter`. |
| **Latent DL-dropdown bug** | Fixed in same PR. `--z-dropdown` is bumped to `1060` (above the new `--z-sticky-corner: 1023`). Without this fix, making the toolbar sticky introduces a regression: the dropdown opens below the sticky thead and gets clipped. |
| **Pre-existing weekend dark-contrast bug** | Fixed in same PR. Current `.excel-calendar__header-day--weekend` in dark mode produces white-on-`#7AB3E3` (2.4:1 — fails WCAG AA). Sticky makes the header always visible, so this becomes a permanent accessibility failure if not fixed. |

---

## 3. Scope

### In scope
1. Diagnose and fix Zone B (left column sticky not engaging despite correct CSS).
2. New sticky on Zone C (group header bands).
3. Sticky `.cal-toolbar` pinned to `.cal-page`.
4. Sticky readonly banner relocated out of `.excel-calendar` to `.cal-page`, becoming a second-tier band under the toolbar.
5. Flex-column layout chain replacing the magic-number height.
6. RTL correctness (`inset-inline-start` + `[dir="rtl"]` shadow flips).
7. Spreadsheet-style edge shadows (scroll-activated, opacity-transitioned).
8. Z-index tier extension (`--z-sticky-col/group/header/corner`).
9. New shadow tokens (`--shadow-sticky-inline`, `--shadow-sticky-block`) with light + dark + RTL variants.
10. Calendar header height token (`--excel-calendar-header-height: 44px`).
11. Mode token in corner cell.
12. "Next group ↓" jump affordance inside each sticky band.
13. Auto-condense toolbar after 200px scroll.
14. Mobile (`< 768px`) toolbar collapse to "Tools ▾" overflow sheet.
15. Group count `(visible/total)` recompute on filter.
16. Accessibility additions: `aria-rowcount` / `aria-rowindex`, visually-hidden corner label, `focusCell` `scrollIntoView`, `forced-colors` border fallback, `prefers-reduced-motion` shadow handling.
17. Latent DL-dropdown z-index regression fix.
18. Pre-existing weekend dark-mode contrast fix.
19. Localization additions (7 new resx keys EN + HE).

### Out of scope (explicitly deferred)
1. **Virtualization audit** of `calendar-lazy-rows.js` interaction with sticky cells. The current implementation uses `display: none` for past rows, which does not break sticky. If it ever moves to mount/unmount, sticky group bands may recompute incorrectly — document the assumption in a code comment, no audit.
2. **Print-mode redesign.** The existing `@media print` rules hide `.cal-toolbar` and don't apply sticky. The flex-column layout must not break print; verify, but don't redesign.
3. **`Pages/Chores/Calendar.cshtml`** — uses a different `.calendar-grid` markup, not `ExcelCalendarTable`. Out of scope. Add a `/* TODO */` comment in its CSS pointing to this spec's tokens for future alignment.
4. **Scroll-position memory across navigation.** Pre-existing problem; flagged for a follow-up PR.
5. **"Today" pin button in toolbar** (one-click jump to today's row + column). Genuine UX win, but a new toolbar control with its own design / localization. Follow-up.
6. **Row-index breadcrumb** ("Row 23 of 156"). Out of scope; the `aria-rowcount`/`rowindex` addition gives SR users this, but no visual UI for sighted users in v1.
7. **Aligning `.shift-table` in `Pages/Chores/Calendar.cshtml` to the new z-index tokens.** Add `/* TODO */` comment; do not refactor.
8. **The auto-condense and mode-token features inside the `Pages/Calendar/Overview.cshtml`** must work without modification — Overview shares the same component, so it inherits sticky / RTL / shadow behavior. Verify; don't re-implement.

---

## 4. Architecture — the layout chain

### Current chain
```
.app-shell  (flex row, min-height: 100vh)
└─ .app-main  (flex: 1; display: flex; flex-direction: column; min-width: 0)
   ├─ .app-header
   └─ <main class="app-content" id="main-content">
      └─ .cal-page  (no display, no height — just block-level)
         ├─ .cal-toolbar  (block-level)
         └─ .excel-calendar  (overflow: auto; max-height: calc(100vh - 200px))   ← magic number
            └─ .excel-calendar__table  (border-collapse: separate; min-width: 100%)
```

### New chain
```
.app-shell  (height: 100dvh; flex row)                  ← min-height → height
└─ .app-main  (flex: 1; display: flex; flex-direction: column; min-width: 0; min-height: 0)   ← adds min-height: 0
   ├─ .app-header  (flex: 0 0 auto)
   └─ <main class="app-content" id="main-content"
            style="display: flex; flex-direction: column; flex: 1; min-height: 0">
      └─ .cal-page  (display: flex; flex-direction: column; flex: 1; min-height: 0)
         ├─ .cal-toolbar  (position: sticky; top: 0; z-index: var(--z-sticky); flex: 0 0 auto)
         ├─ .excel-calendar__readonly-banner  (position: sticky; top: var(--toolbar-h); z-index: var(--z-sticky); flex: 0 0 auto)  ← MOVED HERE from inside .excel-calendar
         └─ .excel-calendar  (flex: 1 1 auto; min-height: 0; overflow: auto)   ← no more max-height
            └─ .excel-calendar__table  (border-collapse: separate; min-width: 100%; width: max-content)
```

**Critical invariant.** Every link in the flex chain must have `min-height: 0` (or `min-width: 0` for horizontal). Without it, flex defaults to `min-height: auto` which equals content height, defeating the shrink-to-fit behavior that lets the calendar fill the remaining viewport.

**Why move the readonly banner.** Today it's the first child of `.excel-calendar`, so when the user scrolls the calendar vertically the banner scrolls away too. Read-only users repeatedly forget *why* they can't edit; the banner needs to stay visible. Moving it to `.cal-page` and making it sticky-second-tier (sticky to `top: var(--cal-toolbar-height)`) keeps it visible without polluting the table.

**Toolbar height as a CSS variable.** Set by the sticky-shadows JS via `ResizeObserver` on the toolbar: `.cal-page { --cal-toolbar-height: <measured>px }`. Used by the readonly banner's `top:` and by the calendar's natural top offset.

---

## 5. Sticky zone CSS specifications

All five sticky surfaces. Listed in z-index order (lowest → highest).

### 5.1 `.excel-calendar__row-label` — Zone B (left column)
```css
.excel-calendar__row-label {
    position: sticky;
    inset-inline-start: 0;          /* RTL-correct */
    z-index: var(--z-sticky-col);   /* 1019 */
    background: var(--surface);
    font-weight: var(--font-medium);
    padding: var(--space-3);
    min-width: 150px;
    white-space: nowrap;
    /* No border-inline-end — replaced by scroll-activated shadow below */
}

/* Scroll-activated edge shadow (LTR) */
.excel-calendar.is-scrolled-x .excel-calendar__row-label {
    box-shadow: var(--shadow-sticky-inline);
}

/* RTL: flip shadow x offset */
[dir="rtl"] .excel-calendar.is-scrolled-x .excel-calendar__row-label {
    box-shadow: var(--shadow-sticky-inline-rtl);
}
```

**Root-cause caveat.** Today's CSS already declares `position: sticky; left: 0` but does not engage in production. Phase 2 of the implementation plan is a DevTools-driven diagnosis of why. Likely candidates: (a) an ancestor `transform` / `filter` / `contain` added incidentally; (b) the lack of `min-width: 0` somewhere in the flex chain causing the table to overflow without creating a proper sticky containing block; (c) `border-collapse` being inherited as `collapse` from a parent rule (would not be obvious in computed style). The fix is whichever applies — not a new sticky declaration.

### 5.2 `.cal-toolbar` — sticky toolbar
```css
.cal-toolbar {
    /* Existing card styling preserved: background, border, border-radius, padding */
    position: sticky;
    top: 0;
    z-index: var(--z-sticky);       /* 1020 */
    transition: box-shadow var(--transition-fast),
                padding var(--transition-fast);
}

/* Only sticky when there's actually a calendar to scroll */
.cal-page:not(:has(.excel-calendar)) .cal-toolbar {
    position: static;
}

/* Shadow when content is sliding under */
.cal-toolbar.is-pinned {
    box-shadow: var(--shadow-md);
    border-color: var(--border-strong);
}

/* Condense after 200px scroll */
.cal-toolbar.is-pinned.is-condensed {
    padding: var(--space-1) var(--space-2);
}
.cal-toolbar.is-pinned.is-condensed .cal-toolbar__selector label {
    display: none;
}
.cal-toolbar.is-pinned.is-condensed .nav-text {
    display: none;
}
```

**Pinning detection.** A sentinel `<div class="cal-toolbar-sentinel" aria-hidden="true"></div>` is added immediately above the toolbar inside `.cal-page`. When the sentinel leaves the viewport (`IntersectionObserver` with `threshold: 0`), `.is-pinned` is added to the toolbar.

### 5.3 `.excel-calendar__readonly-banner` — sticky second tier
```css
.excel-calendar__readonly-banner {
    position: sticky;
    top: var(--cal-toolbar-height, 64px);   /* JS-measured, with 64px fallback */
    z-index: var(--z-sticky);
    /* Existing visual styling preserved */
}
```

### 5.4 `.excel-calendar__group-header td` — Zone C (group bands)
```css
/* Sticky applied to the td INSIDE the tr — sticky on <tr> is unreliable cross-browser */
.excel-calendar__group-header td {
    position: sticky;
    top: var(--excel-calendar-header-height, 44px);   /* Below sticky thead */
    z-index: var(--z-sticky-group);                   /* 1021 */
    background: var(--surface-soft);
    /* Existing padding / border-inline-start / font preserved */
}

/* Pinned-state shadow (scroll-activated) */
.excel-calendar.is-group-pinned .excel-calendar__group-header.is-pinned td {
    box-shadow: var(--shadow-sticky-block);
}

/* Collapsed groups: don't sticky — the band IS the only row */
.excel-calendar__group-header.is-collapsed td {
    position: static;
}
```

### 5.5 `.excel-calendar__header` — Zone A (date row, already sticky)
```css
.excel-calendar__header {
    position: sticky;
    top: 0;     /* relative to .excel-calendar scroll container */
    z-index: var(--z-sticky-header);    /* 1022 — above group bands */
    background: var(--primary);
    color: var(--primary-contrast) !important;
}

/* Existing border-bottom: 2px on `th` → reduced to 1px solid var(--border-strong)
   as the persistent at-rest separator. The 2px was too heavy with the new shadow on top. */
.excel-calendar__header th {
    border-bottom: 1px solid var(--border-strong);
}

/* Scroll-activated shadow that stacks on top of the 1px at-rest line */
.excel-calendar.is-scrolled-y .excel-calendar__header {
    box-shadow: var(--shadow-sticky-block);
}
```

**Weekend dark-mode contrast fix** (pre-existing bug):
```css
.excel-calendar__header-day--weekend {
    background: var(--primary-hover);
}
/* CURRENT BUG: in dark mode, --primary-hover is #7AB3E3; white text = 2.4:1, fails AA */
/* FIX: dark mode uses a stronger token */
[data-theme="dark"] .excel-calendar__header-day--weekend {
    background: var(--primary-strong, #2A5A8F);
    /* color already var(--primary-contrast) !important via .excel-calendar__header th */
}
```
Add `--primary-strong: #2A5A8F` to `tokens.css` if not present. White on `#2A5A8F` = 6.1:1 — passes AA.

### 5.6 `.excel-calendar__corner` — Zone D (top-left)
```css
.excel-calendar__corner {
    position: sticky;
    top: 0;
    inset-inline-start: 0;
    z-index: var(--z-sticky-corner);    /* 1023 — top of stack */
    background: var(--primary);
    color: var(--primary-contrast) !important;
}

/* Shadow when scrolled on EITHER axis */
.excel-calendar.is-scrolled-x .excel-calendar__corner,
.excel-calendar.is-scrolled-y .excel-calendar__corner {
    box-shadow: var(--shadow-sticky-block),
                var(--shadow-sticky-inline);
}

[dir="rtl"] .excel-calendar.is-scrolled-x .excel-calendar__corner,
[dir="rtl"] .excel-calendar.is-scrolled-y .excel-calendar__corner {
    box-shadow: var(--shadow-sticky-block),
                var(--shadow-sticky-inline-rtl);
}
```

**Mode token inside corner.** The corner currently renders an empty `<th>`. New markup:
```html
<th class="excel-calendar__corner" scope="col" data-mode="@Model.RowMode">
    <span class="visually-hidden"><loc key="Calendar_RowLabel_ColumnHeader" /></span>
    <span class="excel-calendar__mode-token" aria-hidden="true">
        <loc key="@Model.RowModeLabelKey" />
        <span class="excel-calendar__mode-chevron">▾</span>
    </span>
</th>
```
The visually-hidden span gives screen readers a column header (today they hear nothing). The mode token gives sighted users persistent context for what the row labels mean.

`@Model.RowMode` is one of `"shifts"`, `"users"`, `"duty"`, `"chores"` and is added to `ExcelCalendarTableViewModel`.
`@Model.RowModeLabelKey` is one of `"Calendar_RowMode_Shifts"`, `"Calendar_RowMode_Users"`, `"Calendar_RowMode_Duty"`, `"Calendar_RowMode_Chores"`.

---

## 6. Z-index tier plan

Add to `wwwroot/css/tokens.css` under the Z-INDEX SCALE section:

```css
:root {
    /* Sticky tiers — calendar uses all five; data tables use first two */
    --z-sticky-col:    1019;   /* Sticky left/right column under header */
    --z-sticky:        1020;   /* Default sticky (toolbars, single-axis) — UNCHANGED */
    --z-sticky-group:  1021;   /* In-body sticky bands (group headers) */
    --z-sticky-header: 1022;   /* Sticky table header (above body bands) */
    --z-sticky-corner: 1023;   /* Two-axis intersection (above all) */

    /* Bump dropdown above the new sticky-corner to prevent clipping when the
       toolbar pins. Without this, opening the DL dropdown inside a pinned
       toolbar would render the menu BEHIND the sticky thead. */
    --z-dropdown: 1060;        /* was 1000 */
}
```

**Stacking order verified, bottom-up**:
1. Cells (no z-index, base flow)
2. `.excel-calendar__row-label` — 1019
3. `.cal-toolbar`, `.excel-calendar__readonly-banner` — 1020 (different stacking contexts, no conflict)
4. `.excel-calendar__group-header td` — 1021
5. `.excel-calendar__header` — 1022
6. `.excel-calendar__corner` — 1023
7. DL dropdown menu, native datepicker popout — 1060

**Stacking-context invariant.** `.excel-calendar` must NOT have `z-index` set or `isolation: isolate` applied. If it ever gains either, the entire calendar becomes a stacking context isolated from `.cal-toolbar` (z 1020), and the toolbar will visually appear ABOVE every sticky element in the calendar including the corner — semantically OK (toolbar is "outside") but unexpected. Add a comment to the CSS preventing this regression.

---

## 7. Shadow tokens and motion

### 7.1 Shadow tokens
Add to `wwwroot/css/tokens.css` under the SHADOW SYSTEM section:

```css
:root {
    /* Sticky edge shadows. Horizontally-oriented (column edge) and vertically-
       oriented (row edge) variants for spreadsheet-style frozen-pane affordance. */
    --shadow-sticky-inline:      2px 0 4px -2px rgba(0, 0, 0, 0.15);
    --shadow-sticky-inline-rtl: -2px 0 4px -2px rgba(0, 0, 0, 0.15);
    --shadow-sticky-block:       0 2px 4px -2px rgba(0, 0, 0, 0.15);
}

:root[data-theme="dark"] {
    --shadow-sticky-inline:      2px 0 4px -2px rgba(0, 0, 0, 0.45);
    --shadow-sticky-inline-rtl: -2px 0 4px -2px rgba(0, 0, 0, 0.45);
    --shadow-sticky-block:       0 2px 4px -2px rgba(0, 0, 0, 0.45);
}
```

Each sticky element references the tokens; the `[dir="rtl"]` selectors swap `--shadow-sticky-inline` to `--shadow-sticky-inline-rtl`. The block shadow is direction-neutral.

### 7.2 Motion contract
- **Edge shadow appearance**: triggered by IntersectionObserver against sentinel elements at the leading edges of the scroll container. Transition: `opacity var(--transition-fast)` only. The shadow is rendered as the box-shadow of the sticky element itself; the *opacity* property toggles via class — implemented by giving the element `opacity: 0` on its `::after` pseudo-element... no — simpler: the box-shadow is conditional on the `.is-scrolled-x` / `.is-scrolled-y` class, and CSS transitions handle the snap. Empirically `box-shadow` transitions cause flicker in some Chromium versions; if we observe it, fall back to a separate `::after` pseudo-element with `opacity` transition. Decided during implementation phase 7.
- **Toolbar pin transition**: `box-shadow var(--transition-fast), padding var(--transition-fast)` on `.cal-toolbar`. Pin is instantaneous (the IO fires on the sentinel crossing); the shadow / padding animate in.
- **`prefers-reduced-motion: reduce`**: existing global rule at `tokens.css` lines 658-667 disables all transitions. The shadow appears instantly. Sticky positioning itself is not "motion" — it's rendering, and not disabled.
- **Next-group jump scroll**: `behavior: 'smooth'` normally; `behavior: 'auto'` when reduced-motion is active. JS reads `window.matchMedia('(prefers-reduced-motion: reduce)').matches`.

### 7.3 Forced-colors fallback (Windows high-contrast)
```css
@media (forced-colors: active) {
    .excel-calendar__row-label,
    .excel-calendar__corner {
        border-inline-end: 2px solid CanvasText;
    }
    .excel-calendar__header,
    .excel-calendar__group-header td,
    .cal-toolbar {
        border-bottom: 2px solid CanvasText;
    }
    /* box-shadow is ignored in forced-colors mode anyway; explicit borders
       give us a guaranteed visible separator independent of scroll state. */
}
```

### 7.4 Sticky shadows JS — `wwwroot/js/calendar-sticky-shadows.js`
Approx 50–80 lines. Responsibilities:

1. Locate each `.excel-calendar` on the page.
2. Inject sentinels: one for the top edge (inside, at `top: 0`), one for the inline-start edge (inside, at `inset-inline-start: 0`).
3. `IntersectionObserver` on each sentinel; toggle `.is-scrolled-y` / `.is-scrolled-x` on the wrapper accordingly.
4. For each `.excel-calendar__group-header`, observe its band against the header bottom edge (rootMargin offset by `--excel-calendar-header-height`). When intersecting, add `.is-pinned`; when not, remove. Also toggle `.is-group-pinned` on the wrapper when any band is pinned.
5. Locate `.cal-toolbar-sentinel` (injected by Razor); IO with `threshold: 0`; toggle `.is-pinned` on `.cal-toolbar`. Threshold-200 listener (debounced) toggles `.is-condensed`.
6. `ResizeObserver` on `.cal-toolbar` to set `.cal-page { --cal-toolbar-height: <Npx> }`.
7. Disable on `prefers-reduced-motion: reduce`? No — we still need the *position* changes, just no transitions. The CSS handles that already.
8. Cleanup on `pagehide` / SPA navigation (not applicable here — full page reloads — but include for safety).

---

## 8. RTL discipline

### 8.1 Conversion list — `calendar.css`
Every `left: 0` / `right: 0` / `left: <n>` / `right: <n>` in the file must be reviewed. The UI agent enumerated 12 sites; phase 5 of the implementation plan does a fresh `grep`-driven sweep against the final code state.

Mandatory conversions (sticky-related):
- `.excel-calendar__row-label { left: 0 }` → `inset-inline-start: 0`
- `.excel-calendar__corner { left: 0 }` → `inset-inline-start: 0`

Other conversions (not sticky but in the same component for consistency):
- `.shift-table td:first-child { left: 0 }` (if used in any in-scope page; check during implementation).
- Any `left/right: <n>` inside `.calendar-week__day-column`.

### 8.2 Shadow direction
`box-shadow` is a physical property — `[dir]` does not flip it. Each shadow with an inline-axis x-offset requires an `[dir="rtl"]` override that swaps to the `-rtl` token variant.

### 8.3 Group chevron rotation
Already handled at calendar.css:2939. Leave it. The chevron click target sits AT LEAST 24px below the top edge of the pinned band on mobile (via padding-top: `var(--space-2)`) — keeps it out of iOS browser-chrome tap routing.

---

## 9. Accessibility

### 9.1 ARIA additions
- `<table aria-rowcount="@Model.TotalRows" ...>` on `.excel-calendar__table`. `TotalRows` is added to the view model (count of rendered rows including group headers).
- `<tr aria-rowindex="@(i+1)" ...>` on each `<tr>` in `_CalendarRow.cshtml`. The index is per-row position in the full set.
- `<th class="excel-calendar__corner" scope="col" ...>` gains a visually-hidden `<loc key="Calendar_RowLabel_ColumnHeader" />` so SR users hear a column header (currently silent).
- `aria-label` on the sticky toolbar: `<div class="cal-toolbar" role="toolbar" aria-label="@Localizer["Calendar_StickyToolbar_AriaLabel"]">` — clarifies its role as a persistent control surface, not as a section heading.
- `aria-label` on the date header thead: `<thead aria-label="@Localizer["Calendar_StickyHeader_AriaLabel"]">`.

### 9.2 Focus management
- `wwwroot/js/calendar-keyboard-nav.js` `focusCell()`: after `cell.focus()`, call `cell.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'auto' })`. This ensures arrow-key navigation never lands the focus behind a sticky element.
- After scrolling, if the focused cell is still within `var(--excel-calendar-header-height)` of the top of the scroll viewport (i.e., obscured by the sticky thead), nudge the container: `container.scrollBy({ top: -(headerHeight + padding) })`.
- Same for inline axis: if within row-label width of the inline-start edge, nudge `container.scrollBy({ left: -(rowLabelWidth + padding) })` (with sign flipped under RTL).

### 9.3 `forced-colors` mode
See §7.3.

### 9.4 Skip-link interaction
`<a href="#main-content">` (the skip link in `_Layout.cshtml:388`) lands on `<main id="main-content">`. The new sticky toolbar lives inside `<main>`, so the skip link still works — first interactive element after the skip target is now the toolbar's first selector. **Not** a regression. A second skip-link `#calendar-grid` jumping past the toolbar is a follow-up; not in scope.

### 9.5 Screen-reader announcement of scroll state
Sticky position does not change DOM order, so SR navigation reads the toolbar once at the top and then enters the table normally. We do NOT announce "header pinned" state changes — would be noise on every scroll tick. The `aria-label` on the toolbar covers the role context.

---

## 10. Localization additions

New keys in `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx`:

| Key | EN | HE |
|---|---|---|
| `Calendar_StickyToolbar_AriaLabel` | Calendar controls | פקדי לוח שנה |
| `Calendar_StickyHeader_AriaLabel` | Calendar dates | תאריכי לוח שנה |
| `Calendar_PinnedGroup_AriaLabel` | Pinned group header | כותרת קבוצה מוצמדת |
| `Calendar_RowLabel_ColumnHeader` | Name | שם |
| `Calendar_NextGroup` | Next group | קבוצה הבאה |
| `Calendar_ScrollHint_Horizontal` | Scroll horizontally to see more dates | גלול אופקית לראות תאריכים נוספים |
| `Calendar_ScrollHint_Vertical` | Scroll down to see more rows | גלול מטה לראות שורות נוספות |
| `Calendar_RowMode_Shifts` | Shifts | משמרות |
| `Calendar_RowMode_Users` | People | אנשים |
| `Calendar_RowMode_Duty` | Duty | תורנות |
| `Calendar_RowMode_Chores` | Chores | מטלות |
| `Calendar_Tools_Overflow_Label` | Tools | כלים |
| `Calendar_Tools_Overflow_AriaLabel` | More calendar tools | כלי לוח שנה נוספים |

Total: 13 new keys. All consumed via `IStringLocalizer<SharedResources>` or `<loc key="...">`. No hardcoded strings introduced.

---

## 11. File inventory

### Created
| File | Purpose |
|---|---|
| `wwwroot/js/calendar-sticky-shadows.js` | IntersectionObserver-driven sticky shadow state toggler, sentinel injection, ResizeObserver for `--cal-toolbar-height`, threshold-200 condense listener. |

### Modified
| File | Change summary |
|---|---|
| `wwwroot/css/tokens.css` | Add `--z-sticky-col/group/header/corner`, `--shadow-sticky-inline/inline-rtl/block` (light + dark), `--excel-calendar-header-height`, `--primary-strong`. Bump `--z-dropdown` to `1060`. |
| `wwwroot/css/calendar.css` | All sticky CSS for toolbar / row-label / group-band / corner / readonly banner. RTL `left/right` → `inset-inline-*` sweep (~12 sites). Edge-shadow rules + `[dir="rtl"]` flips. `forced-colors` fallback. Weekend-header dark-mode contrast fix. Auto-condense rules. Mobile `<768px` toolbar collapse. Remove `max-height: calc(100vh - 200px)`. |
| `wwwroot/css/site.css` | `.app-shell` `min-height: 100vh` → `height: 100dvh`. `.app-main` adds `min-height: 0`. `<main class="app-content">` adds `display: flex; flex-direction: column; flex: 1; min-height: 0` (or move this into a class for cleanliness). Align `.data-table--sticky-*` to the new tier tokens (small consistency win). |
| `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` | Add `<div class="cal-toolbar-sentinel" aria-hidden="true">` before toolbar (in caller pages, see below). Add `aria-rowcount` to `<table>`. Add visually-hidden `<loc>` label in corner. Add mode token markup in corner. Add `data-mode` attribute. Wire `RowMode` and `TotalRows` from view model. |
| `Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml` | Add `aria-rowindex` per `<tr>`. Add "Next group ↓" button inside `.excel-calendar__group-header td` (conditional on `HasNextGroup`). |
| `Pages/Calendar/Shifts.cshtml`, `OnCall.cshtml`, `Chores.cshtml`, `Overview.cshtml` | Verify `.cal-toolbar` is the first child of `.cal-page` (most already are). Add `<script src="~/js/calendar-sticky-shadows.js" asp-append-version="true">` to the Scripts section. Add mobile "Tools ▾" overflow markup + bottom-sheet trigger inside the toolbar. |
| **Readonly banner relocation (locked strategy)** | Refactor `ExcelCalendarTable` to NOT render the banner internally. Instead the four caller pages render `<partial name="_CalendarReadonlyBanner" model="...">` BEFORE the `@await Component.InvokeAsync("ExcelCalendarTable", ...)` call, inside `.cal-page`. Create `Pages/Shared/_CalendarReadonlyBanner.cshtml` housing the existing banner markup. This keeps the caller pages in control of the page-level layout (toolbar + banner + grid as siblings of `.cal-page`) and removes the cross-concern from the component. |
| `wwwroot/js/calendar-keyboard-nav.js` | `focusCell()` adds `cell.scrollIntoView` + nudge for sticky-obscured cells. |
| `wwwroot/js/excel-calendar-groups.js` | `applyFilter()` / `clearFilter()` recompute `(visible/total)` per group and update each band's count span. Expose `updateGroupCounts()` helper. |
| `wwwroot/js/distribution-lists.js` | Audit: ensure DL dropdown menu CSS reads `var(--z-popover, var(--z-dropdown))`. No JS change required if CSS handles it. |
| `wwwroot/css/distribution-lists.css` | Change `.dl-dropdown__menu { z-index: var(--z-dropdown, 1100) }` to `var(--z-popover, 1060)`. |
| `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx` | 13 new keys. |
| `ViewComponents/ExcelCalendarTableViewModel.cs` (class file — locate via `grep` in implementation phase 1) | Add public properties `string RowMode { get; set; }` (values: `"Shifts"`, `"Users"`, `"Duty"`, `"Chores"` — PascalCase to match resx key suffix directly) and `int TotalRows { get; set; }`. Add `string RowModeLabelKey => $"Calendar_RowMode_{RowMode}"` helper that the corner cell binds to. The `data-mode` attribute on `<th class="excel-calendar__corner">` emits the lowercase form via `@Model.RowMode.ToLowerInvariant()` so CSS attribute selectors stay conventional. |
| `Pages/Calendar/Shifts.cshtml.cs`, `OnCall.cshtml.cs`, `Chores.cshtml.cs`, `Overview.cshtml.cs` | When building the calendar view-model, populate `RowMode` (`"Users"` or `"Shifts"` for Shifts page based on Mode; `"Duty"` for OnCall; `"Chores"` for Chores; `"Shifts"` for Overview default) and `TotalRows` (`Model.Rows.Count + (Model.Groups?.Count ?? 0)` — include group header rows in the count for accurate `aria-rowcount`). |
| `_CalendarReadonlyBanner.cshtml` (NEW partial) | Houses the readonly banner markup that's currently inline at top of `Default.cshtml` (lines 19–40). Receives the same `Model.IsReadOnly`, `Model.RequiredGrantNameKeys` data. No behavior change; just moved out. |

### Not modified (explicitly)
- `Pages/Chores/Calendar.cshtml` and its CSS — separate `.calendar-grid` markup; out of scope.
- `wwwroot/js/calendar-lazy-rows.js` — virtualization audit deferred. Add code comment: "Sticky group bands assume rows use `display:none` rather than mount/unmount. If this ever changes, re-audit `calendar-sticky-shadows.js`."
- `@media print` rules — already hide toolbar; verify but don't redesign.

---

## 12. Implementation phases

Each phase is independently verifiable. The implementation plan (writing-plans skill) will refine these into concrete tasks. Listed in execution order; phases 2 (diagnosis) and 3 (flex layout) are prerequisites for everything visual.

1. **Tokens + z-index plumbing** — no visible change. Adds tokens, bumps `--z-dropdown`. Smoke test: every page that used `--z-dropdown` still renders correctly (DL dropdown, filter modal, justice drawer, native datepicker popout).
2. **Diagnose root cause of broken Zone B sticky** — DevTools-driven. Run app, inspect `.excel-calendar__row-label` computed style + containing block. Identify the breaker. Document in a comment in `calendar.css` near the fix.
3. **Flex-column layout chain** — `site.css` + `calendar.css`. Drop `max-height: calc(100vh - 200px)`. Verify on all 4 calendars × 2 themes × 2 languages × 3 viewport widths.
4. **Sticky toolbar + readonly banner relocation** — move banner out of `ExcelCalendarTable` component into a pre-grid slot. Verify Justice drawer, filter modal, DL dropdown (z-index fix now visible), date picker popout.
5. **RTL `left/right` → `inset-inline-*` sweep** in `calendar.css`. Add `[dir="rtl"]` shadow flips. Verify Hebrew mode looks mirror-flipped of English mode.
6. **Sticky group bands (Zone C)** — `td`-level sticky, stack-and-replace behavior, collapsed-group handling (`position: static`).
7. **Edge shadows JS + CSS** (`calendar-sticky-shadows.js`). Verify shadows appear on scroll, fade on return to top, `prefers-reduced-motion` makes them instant, `forced-colors` falls back to borders.
8. **Auto-condense toolbar at 200px scroll** — JS scroll listener (debounced via `requestAnimationFrame`) toggles `.is-condensed`.
9. **Mobile `<768px` toolbar collapse** to "Tools ▾" overflow sheet. Reuse the bottom-sheet pattern from `calendar-bottom-sheet.js`.
10. **Accessibility pass** — `aria-rowcount/rowindex`, visually-hidden corner label, `focusCell` `scrollIntoView`, weekend-dark-mode contrast fix.
11. **Group count `(visible/total)` recompute on filter** — `excel-calendar-groups.js`.
12. **Mode token in corner cell** — Razor markup + view-model field + 4 new resx keys.
13. **"Next group ↓" affordance** inside each sticky band — Razor partial logic + JS click handler + smooth-scroll respecting reduced motion.
14. **Verification matrix run + Playwright baselines** — see §13.

---

## 13. Testing & verification

### 13.1 Automated
- **Playwright visual regression** via the `webapp-testing` skill: capture 4 screenshots per calendar (top-left rest, scrolled-right, scrolled-down, scrolled-both) × 2 themes × 2 languages = 16 baselines per calendar × 4 calendars = **64 baseline images**. Threshold comparison gates future regressions.
- **CSS token order test** (xUnit): parse `tokens.css`, assert `--z-sticky-corner > --z-sticky-header > --z-sticky-group > --z-sticky > --z-sticky-col`, and `--z-dropdown ≥ --z-sticky-corner`.
- **Localization completeness test** (extend existing test): assert every new key from §10 exists in both `.resx` files and that none is empty.

### 13.2 Manual verification matrix
| Dimension | Values |
|---|---|
| Calendar | Shifts, OnCall, Chores, Overview |
| Mode (where applicable) | by-Shift, by-User |
| Theme | Light, Dark |
| Language | English LTR, Hebrew RTL |
| Viewport | 1920×1080, 1366×768, 768×1024, 414×896 |
| Scroll state | rest, right-scrolled, down-scrolled, both-scrolled |
| Group state | All expanded, all collapsed, mixed |
| Filter state | none active, filter active (group count must read `(visible/total)`) |
| Read-only state | normal + readonly banner present |

Spot-check, not exhaustive — every sticky element verified in at least one cell of each dimension.

### 13.3 Specific bug-regression tests
- ✅ Zone B sticky engages (DevTools-confirmed `position: sticky` is honored).
- ✅ DL dropdown opens fully visible above sticky thead.
- ✅ Justice drawer opens above sticky toolbar.
- ✅ Filter modal traps focus correctly; on close, focus returns to the still-visible Filter button.
- ✅ Native date picker popout doesn't clip at top of sticky toolbar.
- ✅ `prefers-reduced-motion: reduce` — shadows appear instantly, no opacity transition.
- ✅ `forced-colors: active` — sticky borders use `CanvasText` instead of shadows.
- ✅ iOS Safari momentum scroll — row labels stay pinned (accept 1-2 frame lag per known WebKit behavior).
- ✅ Hebrew RTL — row label sticks to the visual right (logical start); shadow falls to the left of it.
- ✅ Weekend column header in dark mode reads white-on-`#2A5A8F` (6.1:1, passes AA).
- ✅ Empty calendar — toolbar does NOT sticky (gated by `:has(.excel-calendar)`).
- ✅ Single-group calendar — band does not visually pin (natural sticky degradation).
- ✅ Long-group calendar (80+ rows in one group) — band pins for full group, "Next group ↓" appears when next group exists.
- ✅ Read-only banner stays visible when scrolling vertically.
- ✅ Collapsed group — band visually static, no double-border glitch.
- ✅ Group count `(3/6)` correctly updates when filter applied/cleared.

### 13.4 Performance check
- Scroll FPS on a 100-row × 30-column calendar in by-User mode at Chrome DevTools → Performance: target ≥ 55 fps median during continuous horizontal scroll. If below, the most likely culprit is the IntersectionObserver firing too frequently — switch to `passive` `scroll` listener with `requestAnimationFrame` throttling.

---

## 14. Risks

| # | Risk | Mitigation |
|---|---|---|
| 1 | iOS Safari + sticky inside flex with `min-height: 0` historically buggy before iOS 13 | Smoke test on iOS Safari 14+. Acceptance: degrades to non-sticky toolbar on older iOS; non-blocking. Document the minimum supported iOS in CSS comment. |
| 2 | Virtualization (`calendar-lazy-rows.js`) interaction with sticky bands | Documented assumption: lazy-rows uses `display: none` (not mount/unmount). Code comment in `calendar-sticky-shadows.js` flags re-audit if that ever changes. |
| 3 | Pre-existing scroll-position-not-restored-on-navigation | Not introduced by this PR; flagged as out-of-scope follow-up. |
| 4 | Mobile toolbar overflow sheet is the first such pattern in the app | Match existing bottom-sheet styling (`calendar-bottom-sheet.js` + `.bottom-sheet` CSS) for consistency. |
| 5 | `box-shadow` transition flicker in older Chromium versions | If observed during phase 7 verification, fall back to a `::after` pseudo-element with `opacity` transition. Decision deferred to implementation. |
| 6 | `--z-dropdown` bump from 1000 to 1060 is a global change — could affect other dropdowns | Audit other consumers of `--z-dropdown` before merging. Currently: DL dropdown, possibly date picker fallback. None expect to be *below* sticky content; the bump is safe. |
| 7 | `position: static` on collapsed group bands relies on JS to add `.is-collapsed` class | This class is already managed by `excel-calendar-groups.js` via `toggleGroup`; verify it's added on initial render for server-side-collapsed groups (not just JS-toggled). |
| 8 | Sticky element backgrounds must be fully opaque to occlude scrolled content | All sticky elements use opaque tokens (`--surface`, `--surface-soft`, `--primary`). Verify no alpha-channel token sneaks in during implementation. |

---

## 15. Out-of-scope (follow-up tickets to file)

1. **"Today" pin button in toolbar** — one-click jump to today's row + column. Standalone feature; needs its own design + localization. Estimated 1 day.
2. **Row-index breadcrumb** ("Row 23 of 156") — visual UI for sighted users. SR users already covered by `aria-rowcount/rowindex`. Estimated 0.5 day.
3. **Scroll-position memory across navigation** — extend the `FOCUS_HINT_KEY` sessionStorage pattern from `calendar-keyboard-nav.js`. Estimated 0.5 day.
4. **Second skip-link `#calendar-grid`** for keyboard users to jump past the sticky toolbar. Estimated 0.25 day.
5. **Lazy-rows respect group boundaries** — load whole groups atomically OR show a "loading more in this group" hint when partial. Estimated 1 day.
6. **Print mode rows full-render** — drop `max-height` lock during print so all rows print. Currently rows past viewport are clipped. Estimated 0.5 day.
7. **Align `.shift-table` (`Pages/Chores/Calendar.cshtml`) and `.data-table--sticky-*` to new z-index tokens** — broader consistency pass. Estimated 0.5 day.
8. **Pre-existing app-header / app-sidebar / topbar z-index inconsistency** (100 / 1000 / 100 — loose discipline). Out of scope of calendar work; flag for a future tokens-cleanup PR.

---

## 16. Definition of done

- All sticky behavior in §5 visible in production across all 4 calendars, both themes, both languages, all viewport widths in §13.2.
- DL dropdown, justice drawer, filter modal, date picker popout all functional (z-index fix verified).
- Weekend column header passes WCAG AA contrast (6.1:1) in dark mode.
- All 13 new localization keys present in both `.resx` files; no hardcoded strings in modified CSHTML.
- Playwright baselines committed to `tests/visual/sticky-headers/`.
- xUnit token-order and localization-completeness tests pass.
- Phase-by-phase manual verification matrix executed and screenshot evidence attached to PR.
- No regression in iOS Safari 14+ smoke test.
- `MEMORY.md` updated with: (a) the new z-index tier convention (`--z-sticky-col/group/header/corner`); (b) the rule that sticky elements use `inset-inline-*` not `left/right`; (c) the `box-shadow` RTL flip discipline; (d) the calendar layout's flex-column invariant (every link needs `min-height: 0`).

---

## Appendix A — Why option 3 ("Full polish") and not a smaller scope

This spec was deliberately written at maximum scope after the brainstorm. The reasoning:

- **B and C are the same conceptual pattern** ("anything that names a row or band of rows is pinned"). Shipping B without C leaves an obvious gap users will ask for in the next sprint.
- **Toolbar pinning and flex-layout are coupled**: replacing the magic-number `max-height` is the *prerequisite* for the toolbar to pin cleanly without double-scroll. Doing them in separate PRs would mean reading the same file twice.
- **The DL-dropdown z-index bug becomes a regression the moment the toolbar pins.** It must ship in the same PR, not as a follow-up.
- **The weekend dark-mode contrast bug becomes more visible** once the header is always sticky. Same logic.
- **RTL discipline is a small line-count addition** in this code area but a large bug-prevention multiplier across the codebase.
- **Mobile toolbar collapse, auto-condense, and next-group jump** are each independently small (15–40 LOC) but compound into a coherent "the calendar is no longer overwhelming" experience.

Doing all of the above together is roughly ~6 days of focused work. The same scope split across three PRs would consume more total time (re-reading the same files, re-running the verification matrix, handling merge conflicts) for the same outcome, while shipping incomplete user experience in the intermediate states.

---

## Appendix B — Brainstorm artifacts

The four visual companion screens used during the brainstorm are at `.superpowers/brainstorm/213-1780050342/content/` (and the earlier session at `708-1779776186/content/`). Persisted for reference; not load-bearing for implementation. The four screens are:

1. `01-zones.html` — labeled diagram of the four sticky zones (A/B/C/D).
2. `02-root-cause.html` — side-by-side "today (broken)" vs "target (fixed)" of the inner-vs-page scroll model.
3. `03-disambiguate.html` — four interpretations of "by-users / by-shifts rows aren't sticky".
4. `04-scope.html` and `01-scope.html` — the four scope options.
