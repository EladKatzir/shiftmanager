# Collapsible Sidebar — Design Spec

**Date:** 2026-03-18
**Status:** Design reviewed and approved

---

## 1. Problem Statement

The sidebar toggle button exists in the HTML with full JS wiring (click handler, Ctrl+B shortcut, localStorage persistence, FOUC prevention) but is **invisible** because `.app-sidebar` has `overflow-y: auto` which clips the absolutely-positioned toggle button at `right: -12px`.

Additionally, the current collapsed state (`width: 64px` icon strip with emoji icons) is not desired. The sidebar should **fully disappear** when collapsed, giving maximum screen width for the data-dense calendar grids.

---

## 2. Design Decisions

| Topic | Decision |
|-------|----------|
| Toggle button style | `»` / `«` tab protruding from sidebar edge (replaces ◀ circle) |
| Collapsed state | Sidebar fully hidden (`transform: translateX(-100%)`), 4px dark anchor strip + `«` tab visible |
| Content behavior | Full width always — sidebar overlays content when expanded |
| Expand mode | Sidebar slides over content with box-shadow, **no backdrop** |
| Close mechanism | `»` tab button + Ctrl+B shortcut (both already wired) |
| Persistence | localStorage key `shifty_sidebar_collapsed` (already implemented) |
| Mobile | No change — existing hamburger + full-screen overlay + backdrop stays |

---

## 3. Visual States

### 3.1 Expanded (Default)

```
┌──────────────────────────┬─────────────────────────────────────────┐
│ SHIFTY                [»]│  Header: לוח משמרות                     │
│ ─────────────────────    │  ─────────────────────────────────────  │
│ 📅 Schedule              │  Calendar content (full width behind)   │
│ 👁️ Overview              │                                         │
│ 👥 People                │                                         │
│ ⚙️ Settings              │                                         │
│                          │                                         │
│ 👤 Test Owner         ▼  │                                         │
└──────────────────────────┴─────────────────────────────────────────┘
         260px                        remaining width
      (position: fixed)              (full viewport width)
       box-shadow: right
```

- Sidebar is `position: fixed` (overlays content, out of flow)
- Content area is **always full viewport width** (never shrinks)
- `»` button is a **sibling of `<aside>`**, positioned `fixed` at the sidebar's inline-end edge
- Box-shadow on the sidebar's inline-end edge for depth

### 3.2 Collapsed

```
┌┬──────────────────────────────────────────────────────────────────┐
│«│  Header: לוח משמרות                                             │
│ │  ─────────────────────────────────────────────────────────────  │
│ │  Calendar content (FULL WIDTH)                                  │
│ │  ▼ תקני משמרת דלתא                                              │
│ │  Emp Tech Pie 🏢פאי  │ ... │ דלתא │ ... │ דלתא │ ... │ ... │   │
│ │  ▼ תקני משמרת הנבה                                              │
│ │  Emp Tech Tao 🏢טאו  │ ... │ הנבה │ הנבה │ הנבה │ ... │ ... │   │
│ │                                                                 │
└┴──────────────────────────────────────────────────────────────────┘
4px                              full viewport width
anchor                          (minus 4px anchor)
strip
```

- Sidebar slides off-screen via `transform: translateX(-100%)` (full 260px width preserved during animation)
- 4px anchor strip element visible at the screen edge (visual hint)
- `«` tab protrudes from the anchor strip
- Content takes full viewport width minus the 4px strip

### 3.3 Transition Animation

- Sidebar slides in/out with `transform: translateX` (GPU-accelerated)
- Duration: `var(--duration-slow)` (~300ms), easing: `var(--ease-in-out)`
- Box-shadow fades in/out with the sidebar
- Toggle button `inset-inline-start` animates from `var(--sidebar-width)` to `4px`

---

## 4. Implementation Approach

### 4.1 Critical: Toggle Button Must Be a Sibling of `<aside>`

**Why:** `position: fixed` children inside an element with `transform` are positioned relative to the transformed ancestor, NOT the viewport. When `.app-sidebar` has `transform: translateX(-100%)`, any fixed-position child moves with it.

**Fix:** Move `<button class="sidebar-toggle">` from inside `<aside class="app-sidebar">` to after the `</aside>` closing tag, as a sibling. The existing JS `document.getElementById('sidebarToggle')` still finds it — ID selectors are global.

### 4.2 Anchor Strip: Sibling `<div>`, Not Pseudo-Element

**Why:** The CSS selector `.app-sidebar.is-collapsed ~ .app-main .app-shell::before` is invalid — `.app-shell` is an ancestor of `.app-main`, not a descendant. Using `:has()` is possible but risky for older military browsers.

**Fix:** Add a `<div class="sidebar-anchor-strip">` as a sibling after `</aside>`, toggled via the JS that already toggles `is-collapsed`. The strip is a fixed element with `inset-inline-start: 0`.

### 4.3 HTML Changes (`_Layout.cshtml`)

**Before:**
```html
<aside class="app-sidebar" id="appSidebar">
  <!-- nav content -->
  <button class="sidebar-toggle" id="sidebarToggle">
    <span class="sidebar-toggle__icon">◀</span>
  </button>
</aside>
<div class="app-main">...</div>
```

**After:**
```html
<aside class="app-sidebar" id="appSidebar">
  <!-- nav content (no toggle button) -->
</aside>
<div class="sidebar-anchor-strip" id="sidebarAnchor"></div>
<button class="sidebar-toggle" id="sidebarToggle"
        aria-expanded="true" aria-controls="appSidebar"
        title="Toggle sidebar (Ctrl+B)">
  <span class="sidebar-toggle__icon" aria-hidden="true">»</span>
</button>
<div class="app-main">...</div>
```

**JS click handler update** (add icon swap + anchor strip toggle):
```js
sidebarToggle.addEventListener('click', function() {
    sidebar.classList.toggle('is-collapsed');
    var collapsed = sidebar.classList.contains('is-collapsed');
    this.querySelector('.sidebar-toggle__icon').textContent = collapsed ? '\u00AB' : '\u00BB';
    sidebarToggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
    document.getElementById('sidebarAnchor').classList.toggle('is-visible', collapsed);
    localStorage.setItem(SIDEBAR_COLLAPSED_KEY, collapsed ? 'true' : 'false');
});
```

**FOUC inline script update** (in `<head>`, runs synchronously before paint):
```js
var isCollapsed = localStorage.getItem('shifty_sidebar_collapsed') === 'true';
if (isCollapsed) {
    document.documentElement.classList.add('sidebar-initially-collapsed');
}
// After DOM loads, set icon text synchronously:
// (add to the existing DOMContentLoaded handler)
var toggleIcon = document.querySelector('#sidebarToggle .sidebar-toggle__icon');
if (toggleIcon) toggleIcon.textContent = isCollapsed ? '\u00AB' : '\u00BB';
var anchor = document.getElementById('sidebarAnchor');
if (anchor && isCollapsed) anchor.classList.add('is-visible');
```

### 4.4 CSS Changes (`navigation.css`)

**Sidebar — change from sticky to fixed:**
```css
.app-sidebar {
  position: fixed;
  inset-inline-start: 0;
  top: 0;
  height: 100vh;
  width: var(--sidebar-width);  /* 260px */
  transform: translateX(0);
  transition: transform var(--duration-slow) var(--ease-in-out),
              box-shadow var(--duration-slow) var(--ease-in-out);
  box-shadow: 4px 0 16px rgba(0,0,0,0.15);
  z-index: var(--z-fixed);  /* 1030 — above sticky, below modals */
}

[dir="rtl"] .app-sidebar {
  box-shadow: -4px 0 16px rgba(0,0,0,0.15);
}
```

**Collapsed state — slide off-screen (replaces old width: 64px rule):**
```css
.app-sidebar.is-collapsed {
  transform: translateX(-100%);
  box-shadow: none;
}

[dir="rtl"] .app-sidebar.is-collapsed {
  transform: translateX(100%);
}
```

**FOUC collapsed state (replaces old width-based rule):**
```css
.sidebar-initially-collapsed .app-sidebar {
  transform: translateX(-100%);
  box-shadow: none;
}

[dir="rtl"].sidebar-initially-collapsed .app-sidebar {
  transform: translateX(100%);
}
```

**Anchor strip:**
```css
.sidebar-anchor-strip {
  position: fixed;
  inset-inline-start: 0;
  top: 0;
  bottom: 0;
  width: 4px;
  background: var(--sidebar-bg);
  border-inline-end: 1px solid var(--sidebar-border);
  z-index: var(--z-fixed);
  display: none;
}

.sidebar-anchor-strip.is-visible,
.sidebar-initially-collapsed .sidebar-anchor-strip {
  display: block;
}
```

**Toggle button — restyle as `»`/`«` tab:**
```css
.sidebar-toggle {
  position: fixed;
  inset-inline-start: var(--sidebar-width);  /* 260px — at sidebar edge */
  top: 50%;
  transform: translateY(-50%);
  transition: inset-inline-start var(--duration-slow) var(--ease-in-out);
  z-index: calc(var(--z-fixed) + 1);  /* 1031 — above sidebar, below modals */

  /* Tab styling */
  background: var(--primary);
  color: var(--primary-contrast);
  border: none;
  padding: 14px 5px;
  border-radius: 0 6px 6px 0;  /* LTR: rounded on right */
  font-size: 14px;
  font-weight: bold;
  cursor: pointer;
}

[dir="rtl"] .sidebar-toggle {
  border-radius: 6px 0 0 6px;  /* RTL: rounded on left */
}

/* Collapsed: tab moves to anchor strip edge */
.app-sidebar.is-collapsed ~ .sidebar-toggle,
.sidebar-initially-collapsed .sidebar-toggle {
  inset-inline-start: 4px;
}
```

**Delete old collapsed icon-strip styles:**
- Delete `.app-sidebar.is-collapsed { width: var(--sidebar-width-collapsed) }` — **MUST delete**, not just the token, or sidebar animates as 64px strip
- Delete all `.app-sidebar.is-collapsed .sidebar-text` opacity/width rules
- Delete tooltip hover `::after` styles for collapsed nav items
- Delete nav item centering in collapsed state
- Delete old `.sidebar-initially-collapsed .app-sidebar` width rule (lines 44-69)

**Remove `--sidebar-width-collapsed` from tokens.css** — no longer used.

### 4.5 Legacy Cleanup (`site.css`)

`site.css` lines 601-620 contain legacy sidebar rules that conflict:
- `@media (max-width: 1024px) { .app-sidebar { width: 220px } }` — update or remove
- Mobile `left: -260px` positioning — ensure it doesn't conflict with the new `transform` approach

These must be reviewed and aligned with the new `navigation.css` rules.

### 4.6 Browser Compatibility Note

`inset-inline-start` is supported since Chrome 87 (2020), Safari 14.1, Firefox 63. If military deployment targets older browsers, fall back to explicit `left`/`right` with `[dir="rtl"]` overrides (matching existing `navigation.css` patterns). Verify before implementation.

---

## 5. Page-by-Page Impact

Since the sidebar overlays instead of pushing content, ALL pages automatically get full width. No per-page changes needed.

| Page Type | Impact |
|-----------|--------|
| **Calendar/Shifts** | Calendar grid gets ~260px wider when sidebar hidden. |
| **Calendar/Overview** | Same — wider grid. |
| **Admin/Users** | Table columns have more breathing room. |
| **Admin/HomeTypes** | Calendar painter has more space. |
| **Forms (Requests, Settings)** | Centered form content — no visual change (max-width constrained). |
| **Home/Dashboard** | Cards reflow wider. |

---

## 6. Mobile — No Changes

The existing mobile implementation (hamburger + full-screen overlay + backdrop at `max-width: 768px`) is unaffected. The desktop toggle button is already hidden on mobile via `.sidebar-toggle { display: none }` at that breakpoint.

---

## 7. Files to Modify

| File | Change |
|------|--------|
| `wwwroot/css/navigation.css` | Sidebar positioning (sticky→fixed), collapsed state (width→transform), toggle restyle, anchor strip, delete icon-strip styles, update FOUC rules |
| `wwwroot/css/tokens.css` | Remove `--sidebar-width-collapsed` |
| `wwwroot/css/site.css` | Remove/update legacy sidebar width rules at 1024px breakpoint |
| `Pages/Shared/_Layout.cshtml` | Move toggle button outside `<aside>`, add anchor strip div, change icon ◀→», JS icon swap + anchor toggle, FOUC script update |

---

## 8. Verification Plan

1. **Toggle works:** Click `»` → sidebar slides away, `«` tab + 4px strip visible
2. **Toggle works (reverse):** Click `«` → sidebar slides in with shadow
3. **Full-width animation:** Sidebar slides as full 260px panel (not as a narrow strip)
4. **Ctrl+B:** Keyboard shortcut toggles correctly
5. **Persistence:** Reload page → collapsed state preserved, correct icon shown
6. **RTL:** Switch to Hebrew → tab on correct side, animation direction correct
7. **Dark mode:** Shadow and anchor strip visible in both themes
8. **Calendar full width:** Shikma calendar with 7 columns fills the screen when collapsed
9. **Mobile unchanged:** At <768px, hamburger + full overlay still works
10. **No FOUC:** Page load in collapsed state doesn't flash the expanded sidebar
11. **Modals work:** Opening a modal while sidebar is open → modal appears above sidebar
12. **z-index stack:** Sidebar (1030) < modal backdrop (1040) < modal (1050) < toast (1080)

---

## 9. Review Findings (Resolved)

| # | Finding | Resolution |
|---|---------|------------|
| 1 | `--z-overlay` doesn't exist | Use `--z-fixed` (1030) |
| 2 | `position: fixed` child moves with parent `transform` | Toggle button moved to sibling of `<aside>` |
| 3 | Old `width: 64px` collapsed rule conflicts with transform | Delete the rule entirely |
| 4 | FOUC CSS used old width-based approach | Replace with `transform: translateX(-100%)` |
| 5 | Anchor strip pseudo-element selector invalid | Use sibling `<div>` element instead |
| 6 | `site.css` has conflicting sidebar rules | Added to files-to-modify list |
| 7 | FOUC script needs icon character sync | Added to FOUC script update section |
| 8 | Anchor strip RTL border | Automatic via `border-inline-end` + logical properties |
| 9 | `inset-inline-start` browser support | Added compatibility note |
