# Bottom Dock — Mini Mode (three-state collapse)

**Status:** Design approved (Approach B), implementation pending plan.
**Author:** Claude + Elad Katzir
**Date:** 2026-04-17

## Problem

The bottom dock (`.bottom-dock` in `Pages/Shared/_Layout.cshtml`) has two states today:

| State           | Visibility                                | Footprint                      |
|-----------------|-------------------------------------------|--------------------------------|
| Full            | Header + widget content                   | 320 × ~400 px                  |
| `is-collapsed`  | Header only (drag handle + toggle label)  | 320 × ~44 px, primary gradient |

Even when collapsed, the header bar remains visually loud — a full-width primary gradient
with the "מידע מהיר" label. Users want a way to tuck the dock away more aggressively
without losing access to it.

## Goals

1. Add a third "Mini" state that reduces the dock to a single small circular pill.
2. Each control has **one** job — no overloaded clicks / long-press gestures.
3. Persist state across reloads.
4. Work at the dock's current position (it may have been dragged anywhere via the existing drag handle).
5. No regression for users who never use the new button.

## Non-goals

- Re-theming the existing expanded/collapsed look.
- Changing the drag-to-move behaviour.
- Adding per-widget visibility controls (OnCallWidget is the sole occupant today).

## State model

Three states, stored as an enum in a single `localStorage` key:

```
┌──────────┐   click ▼      ┌───────────┐
│   FULL   │◄──────────────►│ COLLAPSED │
└──────────┘                └───────────┘
      │    click –      click –    │
      ▼                            ▼
      ┌──────────────────────────────┐
      │          MINI (pill)         │
      │  click pill → restore prev   │
      └──────────────────────────────┘
```

### Storage

| Key                                  | Values                          | Purpose                                    |
|--------------------------------------|---------------------------------|--------------------------------------------|
| `shifty_bottom_dock_state`           | `'full' \| 'collapsed' \| 'mini'` | Current visible state                     |
| `shifty_bottom_dock_prev_state`      | `'full' \| 'collapsed'`         | State to restore to when leaving Mini      |
| `shifty_bottom_dock_position` *(existing)* | `{x, y}` JSON                 | Dragged position, unchanged                |

### Migration

On first load after deploy:

```
if (legacy shifty_bottom_dock_collapsed exists) {
    state = (legacy === 'true') ? 'collapsed' : 'full';
    localStorage.removeItem('shifty_bottom_dock_collapsed');
    localStorage.setItem('shifty_bottom_dock_state', state);
}
```

This preserves each returning user's prior expanded/collapsed preference. New users
default to `'collapsed'` (matches current default — no surprise regressions).

## Markup changes

`Pages/Shared/_Layout.cshtml` around line 832:

```razor
<div class="bottom-dock" id="bottomDock">
    <div class="bottom-dock__header" style="display: flex;">
        <span class="bottom-dock__drag-handle" id="bottomDockDragHandle" title="@Localizer["DragToMove"]">⋮⋮</span>
        <button class="bottom-dock__toggle"
                id="bottomDockToggle"
                type="button"
                aria-expanded="false"
                aria-controls="bottomDockContent"
                title="@Localizer["BottomDock_Toggle"]"
                style="flex: 1;">
            <span class="bottom-dock__toggle-icon">▼</span>
            <loc key="Widget_QuickInfo" />
        </button>
        <button class="bottom-dock__minimize"
                id="bottomDockMinimize"
                type="button"
                aria-label="@Localizer["BottomDock_Minimize"]"
                title="@Localizer["BottomDock_Minimize"]">−</button>
    </div>
    <div class="bottom-dock__content" id="bottomDockContent">
        @await Component.InvokeAsync("OnCallWidget", new { showInSidebar = false })
    </div>
    <button class="bottom-dock__pill"
            id="bottomDockPill"
            type="button"
            aria-label="@Localizer["BottomDock_Restore"]"
            title="@Localizer["BottomDock_Restore"]"
            hidden>
        <icon name="phone" />
    </button>
</div>
```

Notes:
- The `.bottom-dock__header` wrapper (replaces the inline `<div style="display:flex">`) gives us a clean selector to hide in Mini state.
- `<icon name="phone" />` matches OnCallWidget today. If more widgets are added to the dock, update the icon to a generic one (e.g., `bell` or `layout-grid`).

## CSS changes

`wwwroot/css/components.css` — extend the Bottom Dock block near line 3493:

```css
.bottom-dock__minimize {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 28px;
    min-height: 28px;
    padding: 0;
    margin-block-start: var(--space-2);
    margin-inline-end: var(--space-2);           /* logical property — mirrors correctly in RTL */
    background: transparent;
    color: var(--primary-contrast) !important;   /* dark-on-dark guard per MEMORY.md */
    border: 1px solid rgba(255, 255, 255, 0.35);
    border-radius: var(--radius-sm);
    font-size: var(--text-lg);
    font-weight: var(--font-bold);
    line-height: 1;
    cursor: pointer;
    transition: background var(--duration-fast) var(--ease-out);
}
.bottom-dock__minimize:hover     { background: rgba(255, 255, 255, 0.12); }
.bottom-dock__minimize:focus-visible {
    outline: 2px solid var(--primary-contrast);
    outline-offset: 2px;
}

/* Mini mode: hide header + content, show pill */
.bottom-dock.is-mini              { width: auto; background: transparent; box-shadow: none; }
.bottom-dock.is-mini .bottom-dock__header,
.bottom-dock.is-mini .bottom-dock__content { display: none; }
.bottom-dock.is-mini .bottom-dock__pill    { display: inline-flex; }

.bottom-dock__pill {
    display: none;                               /* shown only via .is-mini */
    align-items: center;
    justify-content: center;
    width: 44px;
    height: 44px;
    border-radius: 50%;
    background: linear-gradient(135deg, var(--primary), var(--primary-hover));
    color: var(--primary-contrast) !important;   /* dark-on-dark guard */
    border: none;
    box-shadow: var(--shadow-lg);
    cursor: pointer;
    transition: transform var(--duration-fast) var(--ease-out);
}
.bottom-dock__pill:hover         { transform: scale(1.06); }
.bottom-dock__pill:focus-visible {
    outline: 2px solid var(--primary-contrast);
    outline-offset: 2px;
}

/* Mobile override — even at <640px the pill stays 44×44, not full-width */
@media (max-width: 640px) {
    .bottom-dock.is-mini { width: auto; }
}

/* Print — pill hidden along with the rest of the dock via existing rule at line 3481 */
```

The `[hidden]` attribute on `.bottom-dock__pill` handles the non-mini case at page load
before JS runs; the `display: none` CSS rule handles the case after JS toggles classes.
This keeps the pill invisible even during the brief pre-JS flash.

Add `.bottom-dock__pill` to the existing print-hide rule at line 3481:

```css
@media print {
    .bottom-dock,
    .bottom-dock__toggle,
    .bottom-dock__pill,          /* NEW */
    .system-alerts { display: none !important; }
}
```

## JS changes

`Pages/Shared/_Layout.cshtml` around line 1027 — replace the existing IIFE:

```js
(function() {
    'use strict';

    var STATE_KEY      = 'shifty_bottom_dock_state';
    var PREV_KEY       = 'shifty_bottom_dock_prev_state';
    var LEGACY_KEY     = 'shifty_bottom_dock_collapsed';

    var dock       = document.getElementById('bottomDock');
    var toggle     = document.getElementById('bottomDockToggle');
    var minimize   = document.getElementById('bottomDockMinimize');
    var pill       = document.getElementById('bottomDockPill');
    if (!dock || !toggle || !minimize || !pill) return;

    // --- Migration from legacy boolean key ---------------------------------
    var legacy = localStorage.getItem(LEGACY_KEY);
    if (legacy !== null && localStorage.getItem(STATE_KEY) === null) {
        localStorage.setItem(STATE_KEY, legacy === 'false' ? 'full' : 'collapsed');
        localStorage.removeItem(LEGACY_KEY);
    }

    // --- Apply initial state -----------------------------------------------
    var state = localStorage.getItem(STATE_KEY) || 'collapsed';
    applyState(state);

    function applyState(next) {
        dock.classList.remove('is-collapsed', 'is-mini');
        pill.hidden = true;

        if (next === 'collapsed') {
            dock.classList.add('is-collapsed');
            toggle.setAttribute('aria-expanded', 'false');
        } else if (next === 'mini') {
            dock.classList.add('is-mini');
            pill.hidden = false;
            dock.setAttribute('aria-hidden', 'true');        // SR: treat dock body as hidden
        } else { /* full */
            toggle.setAttribute('aria-expanded', 'true');
        }
        if (next !== 'mini') dock.removeAttribute('aria-hidden');

        state = next;
        localStorage.setItem(STATE_KEY, next);
    }

    // --- Toggle: Full ↔ Collapsed ------------------------------------------
    toggle.addEventListener('click', function() {
        if (state === 'mini') return;                         // safety: shouldn't be clickable
        applyState(state === 'collapsed' ? 'full' : 'collapsed');
    });

    // --- Minimize: enter Mini ----------------------------------------------
    minimize.addEventListener('click', function() {
        if (state === 'mini') return;
        localStorage.setItem(PREV_KEY, state);                // remember where to restore to
        applyState('mini');
    });

    // --- Pill: exit Mini back to previous state ----------------------------
    pill.addEventListener('click', function() {
        var prev = localStorage.getItem(PREV_KEY);
        if (prev !== 'full' && prev !== 'collapsed') prev = 'collapsed';
        applyState(prev);
    });

    // --- Escape closes Full → Collapsed (unchanged); from Mini stays -------
    document.addEventListener('keydown', function(e) {
        if (e.key === 'Escape' && state === 'full') applyState('collapsed');
    });
})();
```

Behaviour preserved:
- Existing `Escape` semantics (Full → Collapsed) unchanged.
- Existing drag IIFE (lines 1062-1134) untouched — dock position logic is independent.

## Localization

Add to `Resources/SharedResources.resx`:

```xml
<data name="BottomDock_Minimize" xml:space="preserve">
  <value>Minimize</value>
</data>
<data name="BottomDock_Restore" xml:space="preserve">
  <value>Restore widget</value>
</data>
```

Add to `Resources/SharedResources.he-IL.resx`:

```xml
<data name="BottomDock_Minimize" xml:space="preserve">
  <value>מזער</value>
</data>
<data name="BottomDock_Restore" xml:space="preserve">
  <value>שחזר ווידג'ט</value>
</data>
```

## Accessibility

| Concern        | Handling                                                                   |
|----------------|----------------------------------------------------------------------------|
| Toggle         | Keeps `aria-expanded` true/false semantics.                                 |
| Minimize       | Plain labeled button — it's a state transition, not a binary toggle.        |
| Pill           | Labeled button (`aria-label="Restore widget"`).                             |
| Mini mode SR   | `.bottom-dock` gets `aria-hidden="true"` in Mini so SR doesn't read hidden header content. |
| Keyboard focus | All three controls are `<button>` — Tab order natural, `focus-visible` outlines provided. |
| Contrast       | Minimize and Pill both pin `color: var(--primary-contrast) !important` per the project's dark-on-dark contrast rule in `MEMORY.md`. |

## RTL

- Minimize button uses logical margin properties (`margin-inline-end`) so it mirrors automatically in RTL without an explicit override.
- Pill sits at the dock's current `left`/`top` inline styles — mirroring handled by existing `[dir="rtl"] .bottom-dock` rules at line 3638.

## Edge cases

| Case                                                | Behaviour                                                 |
|-----------------------------------------------------|-----------------------------------------------------------|
| User drags dock, then minimizes                     | Pill appears at the dragged position (same inline styles). |
| Reload while in Mini                                | Restored as Mini; `prev` key drives next restore.          |
| First-time user with no legacy key and no new key   | Defaults to `'collapsed'` (matches today).                 |
| `PREV_KEY` missing / tampered                        | Defaults to `'collapsed'` on restore.                      |
| `WidgetsEnabled` feature flag off                    | Whole block gated at `_Layout.cshtml:830`, no change.      |
| Mobile <640px                                        | In Full/Collapsed: full-width (today). In Mini: 44×44 pill. |
| Print                                                | Pill added to print-hide rule at line 3481.                |

## Testing plan (for implementation phase)

1. Playwright: cycle Full → Collapsed → Full via toggle; verify `aria-expanded` and `localStorage` after each.
2. Playwright: enter Mini from Collapsed, click pill, verify returns to Collapsed (not Full).
3. Playwright: enter Mini from Full, click pill, verify returns to Full.
4. Playwright: reload mid-Mini, verify state persists and `aria-hidden` applied.
5. Playwright: set legacy key `shifty_bottom_dock_collapsed='true'`, load page, verify migration to `'collapsed'` + legacy key removed.
6. Playwright: drag dock to custom position, minimize, verify pill at same position.
7. Visual: Hebrew RTL, both light and dark themes — confirm pill contrast and no dark-on-dark text.
8. Print preview: pill hidden.
9. Keyboard: Tab reaches minimize button; `Enter`/`Space` triggers it.
10. Screen reader smoke test: in Mini, only pill is announced — dock body suppressed by `aria-hidden`.

## Files touched

| File                                          | Change                                      |
|-----------------------------------------------|---------------------------------------------|
| `Pages/Shared/_Layout.cshtml`                 | Markup (~line 832) + JS IIFE (~line 1027)   |
| `wwwroot/css/components.css`                  | New `__minimize`, `__pill`, `.is-mini` rules; add pill to print rule at line 3481 |
| `Resources/SharedResources.resx`              | +2 keys (`BottomDock_Minimize`, `BottomDock_Restore`) |
| `Resources/SharedResources.he-IL.resx`        | +2 keys (Hebrew)                            |

No migrations, no service changes, no grant changes.

## Open decisions captured

All three open questions from brainstorming resolved with explicit defaults:
1. **Pill icon** → Lucide `phone` (matches current OnCallWidget; revisit if dock gains more widgets).
2. **Default for new users** → `'collapsed'` (preserves today's behaviour).
3. **Restore target from Mini** → previous state via `shifty_bottom_dock_prev_state` (respects user intent).
