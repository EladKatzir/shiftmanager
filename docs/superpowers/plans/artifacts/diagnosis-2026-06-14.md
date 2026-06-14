# Admin UI Bugs — Baseline Diagnosis
**Date:** 2026-06-14  
**Task:** Sub-project C, Task 1 — no production code changes

---

## Bug 1: /Admin/Users — Table Breakout

### Determination: **(A) Wrapper-clip only**

The `document.documentElement.scrollWidth` does NOT exceed `document.documentElement.clientWidth` (both 820px at the 820px viewport). The **body is NOT wider than the viewport**. The layout chain (`.app-shell` → `.app-main` → `.app-content` (overflow-x:auto) → `.section-card` → anonymous DIV) is intact and contained.

The table breakout is a **usability/scrollability problem, not a DOM-overflow breakout**:

- The table `#usersTable` has `min-width: 900px` (inline style) and a computed width of **1779px** at 820px viewport — much wider than the container.
- Its direct parent is a nameless `<div style="overflow-x: clip; overflow-y: visible;">` which is **454px** wide (client). `overflow-x: clip` means the overflowing table content is silently clipped — **no scrollbar is offered to the user**, and the content beyond 454px is invisible and unreachable.
- The fix is: change the wrapper from `overflow-x: clip` to `overflow-x: auto` (or replace the raw inline `style=` wrapper with a `div class="table-responsive"` that already has `overflow-x: auto`). This gives the user a horizontal scrollbar within the section-card to see all columns.

### No offending ancestor for layout breakout
There is no ancestor causing body-level overflow. The `MAIN.app-content` correctly has `overflow-x: auto` at the page level. The problem is purely the innermost clip wrapper preventing the user from scrolling within the table section.

### Raw Measurement JSON (820px, light-ltr)

```json
{
  "viewportWidth": 820,
  "scrollWidthOverflows": false,
  "ancestorChain": [
    {
      "tag": "TABLE",
      "cls": "data-table data-table--sticky-header data-table--sticky-col",
      "id": "usersTable",
      "scrollWidth": 1779,
      "clientWidth": 1779,
      "offsetWidth": 1779,
      "overflowX": "visible",
      "minWidth": "900px",
      "display": "table",
      "width": "1779.27px",
      "maxWidth": "none"
    },
    {
      "tag": "DIV",
      "cls": "",
      "id": null,
      "scrollWidth": 1779,
      "clientWidth": 454,
      "offsetWidth": 454,
      "overflowX": "clip",
      "minWidth": "0px",
      "display": "block",
      "width": "454px",
      "maxWidth": "none"
    },
    {
      "tag": "DIV",
      "cls": "section-card",
      "id": null,
      "scrollWidth": 510,
      "clientWidth": 510,
      "offsetWidth": 512,
      "overflowX": "visible",
      "minWidth": "auto",
      "display": "block",
      "width": "512px",
      "maxWidth": "none"
    },
    {
      "tag": "MAIN",
      "cls": "app-content",
      "id": "main-content",
      "scrollWidth": 560,
      "clientWidth": 560,
      "offsetWidth": 560,
      "overflowX": "auto",
      "minWidth": "auto",
      "display": "flex",
      "width": "560px",
      "maxWidth": "none"
    },
    {
      "tag": "DIV",
      "cls": "app-main",
      "id": null,
      "scrollWidth": 560,
      "clientWidth": 560,
      "offsetWidth": 560,
      "overflowX": "visible",
      "minWidth": "0px",
      "display": "flex",
      "width": "560px",
      "maxWidth": "none"
    },
    {
      "tag": "DIV",
      "cls": "app-shell",
      "id": null,
      "scrollWidth": 820,
      "clientWidth": 820,
      "offsetWidth": 820,
      "overflowX": "visible",
      "minWidth": "0px",
      "display": "flex",
      "width": "820px",
      "maxWidth": "none"
    },
    {
      "tag": "BODY",
      "cls": "",
      "id": null,
      "scrollWidth": 820,
      "clientWidth": 820,
      "offsetWidth": 820,
      "overflowX": "visible",
      "minWidth": "0px",
      "display": "block",
      "width": "820px",
      "maxWidth": "none"
    }
  ]
}
```

### Source location
`Pages/Admin/Users.cshtml` line 901:
```html
<div style="overflow-x: clip; overflow-y: visible;">
<table id="usersTable" class="data-table data-table--sticky-header data-table--sticky-col" style="min-width: 900px;">
```

---

## Bug 2: /Admin/Analytics — Donut Chart

### Structural Determination

The donut structural elements are **correctly sized and positioned**:
- `.donut-wrap`: 200×200px, correctly rendered
- `.donut-svg`: 200×200px, overlapping the wrap exactly (same rect)
- `.donut-center` (absolute, inset:0): correctly overlaid on the SVG
- `.donut-center-val` and `.donut-center-label` exist, have correct rects

The `.hero-chart-shell` has `overflow:hidden` and clips the donut, BUT the donut is fully within the shell bounds (shell: 558×466px, donut positioned at left=179px offset within shell, well within bounds).

**No structural collapse or clipping bug.** The donut IS rendering correctly in terms of DOM layout.

### Contrast Determination: **YES — contrast problem in light mode**

In **light mode** (`data-theme="light"`):
- `.donut-center-val` color: `rgb(26, 31, 43)` — near-black text
- `.donut-center-label` color: `rgb(100, 116, 139)` — medium-gray text
- The donut SVG segments use `.donut-seg.dev-band-under { stroke: var(--primary) }` which in light mode is a deep **navy blue** (the primary brand color)
- The track circle (`--surface-soft`) behind the center provides a mid-gray/off-white, but the **primary-colored arc strokes completely fill the visible center area visually when the donut is "all under" data** 
- Near-black text on deep navy = **insufficient contrast** in light mode

In **dark mode** (`data-theme="dark"`):
- `.donut-center-val` color: `rgb(232, 237, 245)` — near-white text
- `.donut-center-label` color: `rgb(148, 163, 184)` — light gray text  
- Background is dark → **contrast IS acceptable** in dark mode

The underlying CSS issue: `.donut-center-val { color: var(--text) }` and `.donut-center-label { color: var(--text-muted) }` use **body text colors**, not colors calibrated for overlaying a colored SVG ring. In light mode, `var(--text)` is dark (same dark family as the navy primary ring), making the center text near-invisible against the donut arc. The fix is to set the center text to a color that contrasts against the donut background regardless of theme — e.g., `color: var(--primary-contrast)` (which is `#FFFFFF`) for `donut-center-val` and a light version for `donut-center-label` in light mode, or use white text + text-shadow for both themes.

### Raw Measurement JSON (dark-ltr, 1440px)

```json
{
  "wrap": { "top": 491, "left": 1038, "width": 200, "height": 200 },
  "svg": { "top": 491, "left": 1038, "width": 200, "height": 200 },
  "inner": { "top": 435, "left": 289, "width": 546, "height": 454 },
  "wrapComputedWidth": "200px",
  "wrapComputedHeight": "200px",
  "innerDisplay": "flex",
  "innerFlex": "column",
  "innerAlignItems": "center",
  "innerJustifyContent": "flex-start",
  "valColor": "rgb(232, 237, 245)",
  "valFontSize": "28.5px",
  "valBg": "rgba(0, 0, 0, 0)",
  "labColor": "rgb(148, 163, 184)",
  "labFontSize": "9.6px",
  "bodyBg": "rgb(15, 20, 25)",
  "donutWrapExists": true,
  "svgExists": true,
  "innerExists": true,
  "valExists": true,
  "labExists": true,
  "svgClippedBy": {
    "tag": "DIV",
    "cls": "hero-chart-shell anim-fade-up anim-d300",
    "id": "",
    "overflow": "hidden",
    "overflowX": "hidden",
    "overflowY": "hidden",
    "rect": { "top": 429, "left": 859, "width": 558, "height": 466 }
  },
  "donutSvgViewBox": "0 0 200 200",
  "donutSvgWidth": "200",
  "donutSvgHeight": "200",
  "canvasExists": false,
  "chartJsExists": false
}
```

**Light-ltr additional readings:**
- valColor: `rgb(26, 31, 43)` (near-black — LOW CONTRAST on navy donut)
- labColor: `rgb(100, 116, 139)` (medium gray — LOW CONTRAST on navy donut)
- bodyBg: `rgb(248, 249, 251)`

**Center element positions (dark mode):**
```json
[
  {
    "type": "val",
    "text": "0",
    "rect": { "top": 568, "left": 1131, "width": 14, "height": 29 },
    "position": "static",
    "color": "rgb(232, 237, 245)",
    "fontSize": "28.5px",
    "parentTag": "DIV",
    "parentCls": "donut-center",
    "parentPosition": "absolute"
  },
  {
    "type": "lab",
    "text": "Total",
    "rect": { "top": 600, "left": 1124, "width": 29, "height": 14 },
    "position": "static",
    "color": "rgb(148, 163, 184)",
    "parentTag": "DIV",
    "parentCls": "donut-center"
  }
]
```

---

## Summary for Tasks 2 & 3

| Page | Bug classification | Specific fix target |
|------|-------------------|---------------------|
| /Admin/Users | **(A) Wrapper-clip only** | `Pages/Admin/Users.cshtml` line 901: change `overflow-x: clip` to `overflow-x: auto` (or wrap in `div.table-responsive`) |
| /Admin/Analytics donut | **Contrast (light mode)** — structural layout is correct | `wwwroot/css/justice.css` lines 693–700: `.donut-center-val` and `.donut-center-label` need `color: white` / `color: rgba(255,255,255,.8)` instead of `var(--text)` / `var(--text-muted)` |

---

## Screenshots captured

| File | Mode | Width |
|------|------|-------|
| users-before-light-ltr-1440.png | Light/LTR | 1440px |
| users-before-light-ltr-820.png  | Light/LTR | 820px  |
| users-before-light-ltr-375.png  | Light/LTR | 375px  |
| users-before-dark-ltr-1440.png  | Dark/LTR  | 1440px |
| users-before-dark-ltr-820.png   | Dark/LTR  | 820px  |
| users-before-dark-ltr-375.png   | Dark/LTR  | 375px  |
| users-before-hebrew-rtl-1440.png | Hebrew/RTL | 1440px |
| users-before-hebrew-rtl-820.png  | Hebrew/RTL | 820px  |
| users-before-hebrew-rtl-375.png  | Hebrew/RTL | 375px  |
| analytics-before-light-ltr.png  | Light/LTR  | 1440px |
| analytics-before-dark-ltr.png   | Dark/LTR   | 1440px |
| analytics-before-hebrew-rtl.png | Hebrew/RTL | 1440px |
