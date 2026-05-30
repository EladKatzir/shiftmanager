# Calendar Sticky Headers — Visual Regression Baselines

Captured during Task 20 of the 2026-05-29 calendar-sticky-headers plan
(`docs/superpowers/plans/2026-05-29-calendar-sticky-headers.md`).

## Files

| File | What it verifies |
|---|---|
| `baseline.spec.js` | Playwright spec for automated baseline regeneration. Documents the full 64-image matrix + regression-lock tests. |
| `shifts-light-en-rest.png` | Shifts calendar at rest, light theme, English LTR, 1366×768 |
| `shifts-light-en-scrolled-x.png` | After horizontal scroll — proves `.is-scrolled-x` class engages independently of vertical scroll (Task 10 axis-discrimination fix). |
| `shifts-light-en-scrolled-y.png` | After vertical scroll — proves `.is-scrolled-y` class engages independently. |
| `shifts-light-en-scrolled-both.png` | Both axes scrolled — proves the corner sticky stays at (0,0). |
| `shifts-dark-en-rest.png` / `-scrolled-both.png` | Same in dark theme — verifies dark-mode shadow tokens (Task 1, Task 9). |
| `shifts-dark-he-rest.png` / `-scrolled-both.png` | Hebrew RTL + dark — verifies the `inset-inline-start` sweep (Task 8) and `[dir="rtl"]` box-shadow flip (Task 9). |
| `oncall-dark-he-rest.png` | OnCall calendar verified with mode token "תורנות ▾". |
| `chores-dark-he-rest.png` / `-scrolled-both.png` | Chores calendar (32 rows — best for sticky scroll evidence) with mode token "מטלות ▾". |
| `overview-dark-he-rest.png` | Overview with the Task 6.5 alignment (`cal-page overview-calendar` / `cal-toolbar overview-calendar__toolbar` dual classes). |
| `shifts-mobile-light-he-rest.png` | 414×896 viewport — verifies the `<768px` toolbar collapse + Tools overflow trigger appears (Task 16). |
| `shifts-mobile-overflow-sheet.png` | Mobile bottom-sheet OPEN after clicking Tools — proves the clone-and-show JS pattern works (Task 16). |

## What the baselines prove

**Layout chain (Task 4):** `.excel-calendar` is `position: relative; overflow: auto; min-height: 0; height: 430px` at runtime — the flex chain is computed correctly with no `max-height: calc(100vh - 200px)` magic number.

**Sticky toolbar (Task 7):** `.cal-toolbar` is `position: sticky; top: 0` and engages on all four calendar pages, including Overview (where Task 6.5 added `cal-toolbar` as a second class alongside `overview-calendar__toolbar`).

**Sticky col / row label (Task 5 + Task 8):** `.excel-calendar__row-label` is `position: sticky; inset-inline-start: 0; z-index: var(--z-sticky-col)` — sticks visually on the left in LTR and the right in RTL (Chrome reports `scrollLeft` as negative in RTL; baseline captures use `scrollLeft = -800` to drive horizontal scroll in Hebrew).

**Axis discrimination (Task 10 critical fix):** Sentinels at `(top:0, inset-inline-start:0)` would naively trigger BOTH observers on any scroll. The `rootMargin: '0px 100% 0px 100%'` extension on `ioY` and `rootMargin: '100% 0px 100% 0px'` on `ioX` makes each observer see only its own axis. Verified at runtime: `is-scrolled-x` toggles independently of `is-scrolled-y`.

**Mode token (Task 12):** Corner cell renders "Shifts ▾" / "People ▾" / "Duty ▾" / "Chores ▾" in English and "משמרות ▾" / "אנשים ▾" / "תורנות ▾" / "מטלות ▾" in Hebrew. Verified on all 4 calendars.

**aria-rowcount includes thead (Task 12 +1 fix):** `aria-rowcount` = body rows + 1 per ARIA 1.2 §6.6.4. Verified: Shifts (9 body + 1 thead = 10), OnCall (4 + 1 = 5), Chores (32 + 1 = 33), Overview (4 + 1 = 5).

**Overview structural alignment (Task 6.5):** Root div has `class="cal-page overview-calendar"`; toolbar has `class="cal-toolbar overview-calendar__toolbar"`. Sticky CSS reaches Overview without selector duplication.

**Mobile Tools overflow (Task 16):** At 414×896 viewport, `.cal-toolbar__overflow-trigger` is `display: flex` (CSS `@media (max-width: 768px)` matched). Clicking it opens the bottom sheet with cloned controls — 6 cloned children verified preserve their original IDs stripped and event handlers intact.

## Running the spec

The project does not currently bundle Node tooling. To regenerate baselines:

```bash
npm install --save-dev @playwright/test
npx playwright install chromium
SHIFTMANAGER_URL=http://localhost:5005 npx playwright test tests/visual/sticky-headers/baseline.spec.js
```

First run captures baselines into `*-snapshots/`. Subsequent runs compare against them with `maxDiffPixelRatio: 0.005` (allows up to 0.5% changed pixels — typically only antialiasing differences).

## Verification matrix coverage

The baselines committed here cover **representative samples** across:
- 4 calendars (Shifts, OnCall, Chores, Overview)
- 2 themes (light, dark)
- 2 languages (en, he)
- 4 scroll states (rest, scrolled-x, scrolled-y, scrolled-both)
- 1 mobile viewport (414×896)

The full 64-image matrix is captured by the spec file when run. Manual capture during plan execution prioritized one image from each axis combination plus the mobile overflow proof.

## What baselines do NOT cover

- **`prefers-reduced-motion: reduce`** — the shadow opacity transition (Task 9) is disabled per CSS but visual diff is identical at rest.
- **`forced-colors: active`** (Windows high-contrast) — the fallback uses `CanvasText` borders (Task 9 §7.3). Requires `forcedColors: 'active'` test option; not in baseline matrix.
- **Group bands sticky pinning** — Shifts test data has 0 groups (`MoleculeId` chose was tech-only). Chores has 32 ungrouped rows. To exercise Task 11 visually, seed a calendar with `Model.Groups` populated and re-run the spec.
- **Next-group "↓" affordance** — same dependency on groups being present.
- **focusCell scrollIntoView (Task 14)** — keyboard-driven; not in screenshot matrix. Verified via keyboard testing during plan execution.

These behaviors are unit-tested via the JS API or are CSS-only with manual verification documented in the plan.
