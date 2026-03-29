# Quick Entry Help Button — Design Spec

**Date:** 2026-03-29
**Status:** Approved
**Scope:** Add a `?` help button next to the Quick Entry toggle on Calendar/Shifts, Calendar/Chores, and Calendar/OnCall pages. The button opens a bilingual (English/Hebrew) modal with a complete reference guide for the quick entry feature.

---

## Problem

The quick entry feature has poor discoverability:
- A first-use tooltip appears 3 times then disappears forever (localStorage counter)
- No way to re-trigger the tooltip or find help after dismissal
- Slash commands (`/shift`, `/chore`, `/duty`, `/home`) are undocumented in-app
- Hebrew users don't know the commands are English words
- Text entry creation (type without `/`) is not explained anywhere
- Per-page command availability differs but is never communicated
- Keyboard shortcuts (Tab, Shift+Tab, Esc, Enter) are only mentioned in the ephemeral tooltip

## Solution

A `?` button that opens a modal dialog with language tabs (English / עברית), containing 5 reference sections. The modal auto-selects the tab matching the current locale.

---

## UI Specification

### Button Placement

The `?` button appears **immediately after** the `⚡ Quick Entry` toggle button in the toolbar, inside the same `@if (Model.CanEdit)` conditional block. It renders on all 3 calendar pages: Shifts, Chores, OnCall.

**HTML structure:**
```html
@if (Model.CanEdit)
{
    <button type="button" id="quickEntryToggle" ...>
        <span class="btn-icon">⚡</span>
        <loc key="QuickEntry_Toggle" />
    </button>
    <button type="button" id="quickEntryHelp" class="btn btn-ghost quick-entry-help-btn"
            onclick="window.CalendarQuickEntry && window.CalendarQuickEntry.showHelp()"
            loc-title="QuickEntry_HelpTitle"
            loc-aria-label="QuickEntry_HelpTitle">
        <span class="btn-icon">?</span>
    </button>
}
```

**Button styling:**
- Same `btn btn-ghost` base as the Quick Entry toggle
- Compact: icon-only, no label text
- `?` character as the icon (no external icon library dependency)
- Subtle — doesn't compete with the Quick Entry toggle visually

### Modal Structure

**Pattern:** Uses `window.ModalFocus.open/close()` for focus trap, ESC-to-close, and backdrop click handling. This is an intentional upgrade from the manual `cal-filter-modal` pattern (which uses inline `onclick` + `display` toggling). The inner dialog element MUST use class `modal__content` so that `ModalFocus`'s backdrop click detection works (see `modal-focus.js:262-273`). The close button uses the existing `loc-aria-label="Aria_CloseDialog"` key.

**Layout:**
```
┌─────────────────────────────────────────┐
│  Quick Entry Guide              [×]     │
├─────────────────────────────────────────┤
│  [ English ]  [ עברית ]                 │
├─────────────────────────────────────────┤
│                                         │
│  GETTING STARTED                        │
│  Click any empty cell to start...       │
│                                         │
│  COMMANDS                               │
│  📅 /shift  — Assign shift              │
│  🧹 /chore  — Assign chore → type →... │
│  🛡️ /duty   — Assign on-call duty       │
│  🏠 /home   — HOME shift               │
│                                         │
│  TEXT NOTES                             │
│  Type without / to save as a note...    │
│                                         │
│  KEYBOARD                               │
│  Tab        Next cell                   │
│  Shift+Tab  Previous cell               │
│  Enter      Confirm                     │
│  Esc        Cancel                      │
│  /          Command palette             │
│                                         │
│  AVAILABILITY BY PAGE                   │
│  ✓ Shifts — all commands + text notes   │
│  ✓ Chores — /chore only                │
│  ✓ On-Call — /duty only                │
│                                         │
└─────────────────────────────────────────┘
```

### Language Tabs

- Two tabs: **English** and **עברית**
- Auto-selects based on `document.documentElement.lang` (Hebrew → עברית tab, else → English tab)
- User can switch manually; preference is NOT persisted (resets to locale default on next open)
- Tab switching is instant (both content panels rendered, toggle `display: none/block`)
- Slash commands (`/shift`, `/chore`, etc.) remain in English in BOTH tabs — that's what the user types
- Keyboard key labels (`Tab`, `Enter`, `Esc`, `Shift+Tab`) remain in English LTR in both tabs (`dir="ltr"` on `<kbd>` elements)
- Hebrew tab uses `dir="rtl"` on the content panel

---

## Content Specification

### Section 1: Getting Started

**English:**
> Click any empty cell to start typing. Your input searches for employees to assign. Press `Enter` to confirm, `Esc` to cancel.

**Hebrew:**
> לחצו על תא ריק כדי להתחיל להקליד. ההקלדה מחפשת עובדים לשיבוץ. לחצו `Enter` לאישור, `Esc` לביטול.

### Section 2: Commands

Table format with icon, command, and description:

| Icon | Command | English | Hebrew |
|------|---------|---------|--------|
| 📅 | `/shift` | Assign shift | שיבוץ משמרת |
| 🧹 | `/chore` | Assign chore → select type → enter title | שיבוץ תורנות → בחירת סוג → הזנת כותרת |
| 🛡️ | `/duty` | Assign on-call duty | שיבוץ כוננות |
| 🏠 | `/home` | HOME shift | משמרת בית |

Commands are displayed in a shaded card/box for visual grouping. Each row has the emoji icon, the command in a `<code>` block, and the description.

### Section 3: Text Notes

**English:**
> Type any text without `/` to save it as a calendar note on that cell. Notes appear as 📝 badges on other calendar views.
> *Available in user-mode only (Shifts page).*

**Hebrew:**
> הקלידו טקסט חופשי ללא `/` כדי לשמור הערה על התא. הערות מופיעות כסמל 📝 בתצוגות לוח שנה אחרות.
> *זמין רק בתצוגת עובדים (דף משמרות).*

### Section 4: Keyboard Shortcuts

Grid format with `<kbd>` styled keys:

| Key | English | Hebrew |
|-----|---------|--------|
| `Tab` | Next cell | תא הבא |
| `Shift+Tab` | Previous cell | תא קודם |
| `Enter` | Confirm selection | אישור בחירה |
| `Esc` | Cancel & close | ביטול וסגירה |
| `/` | Open command palette | פתיחת תפריט פקודות |

### Section 5: Availability by Page

Static list showing all 3 pages with checkmarks:

**English:**
- ✓ **Shifts** — all commands + text notes
- ✓ **Chores** — `/chore` only
- ✓ **On-Call** — `/duty` only

**Hebrew:**
- ✓ **משמרות** — כל הפקודות + הערות טקסט
- ✓ **תורנויות** — `/chore` בלבד
- ✓ **כוננויות** — `/duty` בלבד

---

## Implementation Details

### Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `wwwroot/js/calendar-quick-entry.js` | Modify | Add `showHelp()` method, modal creation/teardown, tab switching logic |
| `wwwroot/css/calendar-quick-entry.css` | Modify | Add modal styles (`.quick-entry-help-modal`, tabs, sections) |
| `Pages/Calendar/Shifts.cshtml` | Modify | Add `?` button HTML inside `@if (Model.CanEdit)` block |
| `Pages/Calendar/Chores.cshtml` | Modify | Same button HTML |
| `Pages/Calendar/OnCall.cshtml` | Modify | Same button HTML |
| `Resources/SharedResources.resx` | Modify | Add 2 English localization keys (button title/aria) |
| `Resources/SharedResources.he-IL.resx` | Modify | Add 2 Hebrew localization keys (button title/aria) |

### Localization Keys to Add (.resx only)

Only 2 keys are needed in `.resx` files — for the button's server-side `loc-title` and `loc-aria-label` attributes. All modal body content is hardcoded bilingual in JavaScript (see JS Architecture above).

| Key | English | Hebrew |
|-----|---------|--------|
| `QuickEntry_HelpTitle` | Quick Entry Help | עזרה להקצאה מהירה |
| `QuickEntry_HelpGuideTitle` | Quick Entry Guide | מדריך הקצאה מהירה |

### Bilingual Content Reference (hardcoded in JS)

The `createHelpModal()` function embeds all content directly. The full bilingual text is specified in the Content Specification section above. Key translations:

| English | Hebrew |
|---------|--------|
| Getting Started | תחילת עבודה |
| Commands | פקודות |
| Assign shift | שיבוץ משמרת |
| Assign chore → select type → enter title | שיבוץ תורנות → בחירת סוג → הזנת כותרת |
| Assign on-call duty | שיבוץ כוננות |
| HOME shift | משמרת בית |
| Text Notes | הערות טקסט |
| Keyboard | מקשי קיצור |
| Next cell | תא הבא |
| Previous cell | תא קודם |
| Confirm selection | אישור בחירה |
| Cancel & close | ביטול וסגירה |
| Open command palette | פתיחת תפריט פקודות |
| Availability by Page | זמינות לפי דף |
| all commands + text notes | כל הפקודות + הערות טקסט |

### JavaScript Architecture

The help modal is implemented inside `calendar-quick-entry.js` as part of the `CalendarQuickEntry` module (IIFE pattern). No new JS file.

**Public API addition:**
```javascript
window.CalendarQuickEntry = {
    // existing:
    toggle: function() { ... },
    isActive: function() { ... },
    // new:
    showHelp: function() { ... }
};
```

**Internal functions:**
- `createHelpModal()` — builds modal DOM with hardcoded bilingual content (both English and Hebrew panels are built directly, NOT via `getLocalizedLabel()`). Since the modal is explicitly bilingual with both tabs rendered simultaneously, there is no need for the localization lookup pattern — all strings are embedded in the builder function.
- `destroyHelpModal()` — removes modal from DOM, calls `ModalFocus.close()`
- `switchHelpTab(lang)` — toggles `display: none/block` between English/Hebrew content panels, updates `aria-selected` on tabs
- Uses `window.ModalFocus.open(modal)` for focus trap, ESC-to-close, and backdrop click
- `showHelp()` works regardless of quick entry `isActive` state — users can read help before activating

**Note on `_LocalizationScript.cshtml`:** Only the button's `loc-title` and `loc-aria-label` attributes use server-side localization (via `.resx` keys). The modal body content is hardcoded bilingual in JS — no `_LocalizationScript.cshtml` changes needed.

### CSS Architecture

New styles added to `calendar-quick-entry.css`, using flat naming to match existing conventions (`.quick-entry-toggle`, `.quick-entry-dropdown`, etc.):

- `.quick-entry-help-btn` — compact `?` button styling
- `.quick-entry-help-modal` — modal backdrop (full-screen overlay)
- `.quick-entry-help-dialog` — inner dialog container (MUST also have class `modal__content` for `ModalFocus` backdrop detection)
- `.quick-entry-help-tabs` — tab bar with active/inactive states
- `.quick-entry-help-section` — section heading (uppercase, small, muted)
- `.quick-entry-help-commands` — shaded command list card
- `.quick-entry-help-kbd` — keyboard key styling
- RTL support via `[dir="rtl"]` selectors and logical CSS properties
- Dark mode: use `[data-theme="dark"]` selectors (NOT `@media (prefers-color-scheme: dark)` — the existing `prefers-color-scheme` usage in this file is a pre-existing bug)
- Print: add `.quick-entry-help-btn, .quick-entry-help-modal` to the existing `@media print { display: none !important }` block

### Accessibility

- Modal uses `role="dialog"`, `aria-modal="true"`, and `aria-labelledby="quickEntryHelpTitle"` pointing to the title element
- Focus trapped inside modal via `ModalFocus.open()`
- ESC key closes modal and returns focus to `?` button
- Backdrop click closes modal (requires inner dialog to have `modal__content` class)
- Close button uses `loc-aria-label="Aria_CloseDialog"` (existing key)
- Tab bar: `role="tablist"` container; each tab has `role="tab"`, `aria-selected`, `aria-controls="panelId"`, and `id="tabId"`
- Tab panels: `role="tabpanel"`, `aria-labelledby="tabId"` pointing back to controlling tab
- Arrow key navigation between tabs (Left/Right switches active tab per WAI-ARIA tabs pattern)
- `<kbd>` elements always render LTR (`dir="ltr"`) for correct key label display in Hebrew
- Icon-only `?` button uses `loc-aria-label="QuickEntry_HelpTitle"` for screen reader accessibility

### Mobile

The `?` button follows the same mobile behavior as the Quick Entry toggle:
```css
@media (pointer: coarse) {
    .quick-entry-help-btn {
        display: none !important;
    }
}
```
Quick entry is hidden on touch devices, so the help button is too.

---

## Out of Scope

- Notes system unification (CalendarTextEntry vs UserDayNote) — deferred, tracked in memory
- Quick entry on Calendar/Overview — not applicable (different purpose, company-scoped)
- Persisting language tab preference — resets to locale default each time
- Interactive tutorial / step-by-step walkthrough — simple reference is sufficient
