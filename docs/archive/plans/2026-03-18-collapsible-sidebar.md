# Collapsible Sidebar Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the sidebar fully collapsible with a `»`/`«` tab, sliding off-screen as a fixed overlay so content always gets full viewport width.

**Architecture:** CSS-driven sidebar overlay using `position: fixed` + `transform: translateX`. Toggle button moved outside `<aside>` (sibling) to avoid transform stacking context issue. Anchor strip `<div>` sibling for collapsed visual hint. All existing JS (click, Ctrl+B, localStorage) reused with minimal additions.

**Tech Stack:** CSS (navigation.css, tokens.css, site.css), Razor (_Layout.cshtml inline JS + HTML)

**Spec:** `docs/superpowers/specs/2026-03-18-collapsible-sidebar-design.md`

---

## Task 1: HTML Restructure — Move Toggle Button + Add Anchor Strip

**Files:**
- Modify: `Pages/Shared/_Layout.cshtml:751-760`

- [ ] **Step 1: Move toggle button outside `<aside>` and add anchor strip**

Find the toggle button at lines 751-759 (inside `<aside class="app-sidebar">`). Cut it, and paste it AFTER the `</aside>` at line 760, with the anchor strip div added between them.

**Before** (lines 750-760):
```html
            @* Sidebar Toggle Button (A-002: Accessible keyboard toggle) *@
            <button class="sidebar-toggle"
                    id="sidebarToggle"
                    type="button"
                    loc-aria-label="Sidebar_Toggle"
                    aria-expanded="true"
                    aria-controls="appSidebar"
                    title="@Localizer["Sidebar_Toggle"]">
                <span class="sidebar-toggle__icon" aria-hidden="true">◀</span>
            </button>
        </aside>
```

**After** (remove the button from inside `<aside>`, add anchor strip + button after `</aside>`):
```html
        </aside>
        <div class="sidebar-anchor-strip" id="sidebarAnchor"></div>
        <button class="sidebar-toggle"
                id="sidebarToggle"
                type="button"
                loc-aria-label="Sidebar_Toggle"
                aria-expanded="true"
                aria-controls="appSidebar"
                title="@Localizer["Sidebar_Toggle"]">
            <span class="sidebar-toggle__icon" aria-hidden="true">»</span>
        </button>
```

Key changes:
- Button moved from inside `<aside>` to after `</aside>` (sibling, not child)
- Anchor strip `<div>` added between sidebar and button
- Icon changed from `◀` to `»`
- `type="button"` and `loc-aria-label="Sidebar_Toggle"` preserved from original

- [ ] **Step 2: Verify build compiles (Razor syntax)**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add Pages/Shared/_Layout.cshtml
git commit -m "refactor: move sidebar toggle button outside aside element

Moves toggle from child of <aside> to sibling to avoid CSS transform
stacking context issue. Adds anchor strip div. Changes icon to »."
```

---

## Task 2: Update JS — Icon Swap + Anchor Strip Toggle

**Files:**
- Modify: `Pages/Shared/_Layout.cshtml:937-946` (click handler)
- Modify: `Pages/Shared/_Layout.cshtml:919-922` (initial state in IIFE)

- [ ] **Step 1: Update the click handler to swap icon + toggle anchor strip**

Find the existing click handler at lines 937-946:
```js
            sidebarToggle.addEventListener('click', function() {
                sidebar.classList.toggle('is-collapsed');
                var collapsed = sidebar.classList.contains('is-collapsed');
                localStorage.setItem(SIDEBAR_COLLAPSED_KEY, collapsed);
                updateToggleAriaState();

                // Announce state change to screen readers
                var announcement = collapsed ? 'Sidebar collapsed' : 'Sidebar expanded';
                announceToScreenReader(announcement);
            });
```

Replace with (preserving `updateToggleAriaState` and `announceToScreenReader` calls):
```js
            sidebarToggle.addEventListener('click', function() {
                sidebar.classList.toggle('is-collapsed');
                var collapsed = sidebar.classList.contains('is-collapsed');
                this.querySelector('.sidebar-toggle__icon').textContent = collapsed ? '\u00AB' : '\u00BB';
                var anchor = document.getElementById('sidebarAnchor');
                if (anchor) anchor.classList.toggle('is-visible', collapsed);
                localStorage.setItem(SIDEBAR_COLLAPSED_KEY, collapsed);
                updateToggleAriaState();

                // Announce state change to screen readers
                var announcement = collapsed ? 'Sidebar collapsed' : 'Sidebar expanded';
                announceToScreenReader(announcement);
            });
```

Only two lines added — icon swap and anchor toggle. All existing accessibility code preserved.

- [ ] **Step 2: Add initial icon + anchor state in the IIFE (NOT the `<head>` script)**

Find lines 919-922 in the sidebar IIFE (the `<script>` block at ~line 906, NOT the `<head>` FOUC script):
```js
        var isCollapsed = document.documentElement.classList.contains('sidebar-initially-collapsed');
        if (isCollapsed) {
            sidebar.classList.add('is-collapsed');
        }
```

Add AFTER that block (before `updateToggleAriaState()`):
```js
        // Set initial toggle icon and anchor strip state
        if (sidebarToggle) {
            sidebarToggle.querySelector('.sidebar-toggle__icon').textContent = isCollapsed ? '\u00AB' : '\u00BB';
        }
        var anchor = document.getElementById('sidebarAnchor');
        if (anchor && isCollapsed) anchor.classList.add('is-visible');
```

**IMPORTANT:** This goes in the IIFE at ~line 906 (where DOM elements exist), NOT in the `<head>` FOUC script at line 180 (where `#sidebarToggle` doesn't exist yet).

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Pages/Shared/_Layout.cshtml
git commit -m "feat: add icon swap and anchor strip toggle to sidebar JS

Click handler now swaps » ↔ « and toggles anchor strip visibility.
FOUC script sets correct initial state before paint."
```

---

## Task 3: CSS — Sidebar Positioning + Collapsed Transform

**Files:**
- Modify: `wwwroot/css/navigation.css:10-24` (base sidebar)
- Modify: `wwwroot/css/navigation.css:27-35` (collapsed state)
- Modify: `wwwroot/css/navigation.css:44-76` (FOUC rules)

- [ ] **Step 1: Change sidebar from sticky to fixed overlay**

Replace `.app-sidebar` base rule (lines 10-24):

**Before:**
```css
.app-sidebar {
  position: sticky;
  top: 0;
  width: var(--sidebar-width);
  height: 100vh;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  overflow-y: auto;
  background-color: var(--sidebar-bg);
  border-inline-end: 1px solid var(--sidebar-border);
  z-index: var(--z-sticky);
  transition: width var(--duration-slow) var(--ease-in-out),
              transform var(--duration-slow) var(--ease-in-out);
}
```

**After:**
```css
.app-sidebar {
  position: fixed;
  inset-inline-start: 0;
  top: 0;
  width: var(--sidebar-width);
  height: 100vh;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  overflow-y: auto;
  background-color: var(--sidebar-bg);
  border-inline-end: 1px solid var(--sidebar-border);
  z-index: var(--z-fixed);
  transform: translateX(0);
  transition: transform var(--duration-slow) var(--ease-in-out),
              box-shadow var(--duration-slow) var(--ease-in-out);
  box-shadow: 4px 0 16px rgba(0,0,0,0.15);
}

[dir="rtl"] .app-sidebar {
  box-shadow: -4px 0 16px rgba(0,0,0,0.15);
}
```

- [ ] **Step 2: Replace collapsed state (width → transform)**

Replace `.app-sidebar.is-collapsed` block (lines 27-35):

**Before:**
```css
.app-sidebar.is-collapsed {
  width: var(--sidebar-width-collapsed);
}

.app-sidebar.is-collapsed .sidebar-text {
  opacity: 0;
  width: 0;
  overflow: hidden;
  display: none;
}
```

**After:**
```css
.app-sidebar.is-collapsed {
  transform: translateX(-100%);
  box-shadow: none;
}

[dir="rtl"] .app-sidebar.is-collapsed {
  transform: translateX(100%);
}
```

- [ ] **Step 3: Replace FOUC rules (lines 44-76)**

Replace the entire `sidebar-initially-collapsed` block:

**After:**
```css
.sidebar-initially-collapsed .app-sidebar {
  transform: translateX(-100%);
  box-shadow: none;
}

[dir="rtl"].sidebar-initially-collapsed .app-sidebar {
  transform: translateX(100%);
}

.sidebar-initially-collapsed .app-main {
  /* No adjustment needed — content is always full width */
}
```

- [ ] **Step 4: Commit**

```bash
git add wwwroot/css/navigation.css
git commit -m "feat: sidebar uses fixed position with transform collapse

Changes sidebar from position:sticky to position:fixed (overlay mode).
Collapsed state uses translateX(-100%) instead of width:64px.
Content area is always full viewport width."
```

---

## Task 4: CSS — Toggle Button Restyle + Anchor Strip + Cleanup

**Files:**
- Modify: `wwwroot/css/navigation.css:686-723` (delete icon-strip styles)
- Modify: `wwwroot/css/navigation.css:972-1022` (toggle button)
- Modify: `wwwroot/css/tokens.css:386` (remove token)

- [ ] **Step 1: Delete old icon-strip collapsed styles**

Delete ALL `.app-sidebar.is-collapsed` rules that styled the old 64px icon strip. These are now dead code since the sidebar is fully hidden when collapsed:

- Lines 686-694: `.app-sidebar.is-collapsed .app-sidebar-nav-item` (centering, badge hiding)
- Lines 696-723: `.app-sidebar.is-collapsed .app-sidebar-nav-item[data-tooltip]::after` (tooltip hover)
- Lines 799-806: `.app-sidebar.is-collapsed .sidebar-user__info`, `.sidebar-user__menu-icon`, `.sidebar-user` (avatar centering)
- Lines 896-901: `.app-sidebar.is-collapsed .sidebar-user-menu` (menu repositioning)
- Lines 964-966: `.app-sidebar.is-collapsed .sidebar-shortcut { display: none }` (shortcut hiding)

Search for remaining orphans: `grep -n "is-collapsed" wwwroot/css/navigation.css` — delete any that reference the old icon-strip layout.

- [ ] **Step 2: Replace toggle button styles (lines 972-1022)**

Replace the entire `.sidebar-toggle` block with:

```css
/* ========================================
   8. SIDEBAR TOGGLE BUTTON
   ======================================== */

.sidebar-toggle {
  position: fixed;
  inset-inline-start: var(--sidebar-width);
  top: 50%;
  transform: translateY(-50%);
  transition: inset-inline-start var(--duration-slow) var(--ease-in-out);
  z-index: calc(var(--z-fixed) + 1);

  background: var(--primary);
  color: var(--primary-contrast);
  border: none;
  padding: 14px 5px;
  border-radius: 0 6px 6px 0;
  font-size: 14px;
  font-weight: bold;
  cursor: pointer;
  line-height: 1;
}

[dir="rtl"] .sidebar-toggle {
  border-radius: 6px 0 0 6px;
}

.sidebar-toggle:hover {
  filter: brightness(1.1);
}

.sidebar-toggle:focus-visible {
  outline: none;
  box-shadow: var(--focus-ring);
}

/* Collapsed: tab moves to anchor strip edge */
.app-sidebar.is-collapsed ~ .sidebar-toggle,
.sidebar-initially-collapsed .sidebar-toggle {
  inset-inline-start: 4px;
}
```

- [ ] **Step 3: Add anchor strip styles**

Add after the toggle button section:

```css
/* ========================================
   8b. SIDEBAR ANCHOR STRIP
   ======================================== */

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

- [ ] **Step 4: Remove `--sidebar-width-collapsed` from tokens.css**

Delete line 386 in `wwwroot/css/tokens.css`:
```css
--sidebar-width-collapsed: 64px;
```

- [ ] **Step 5: Commit**

```bash
git add wwwroot/css/navigation.css wwwroot/css/tokens.css
git commit -m "feat: restyle toggle as » tab, add anchor strip, delete icon-strip styles

Toggle button restyled from ◀ circle to » tab with primary color.
Anchor strip shows 4px visual hint when collapsed.
Removed all 64px icon-strip collapsed styles (tooltip, centering, text hiding)."
```

---

## Task 5: Legacy Cleanup — site.css Conflicts

**Files:**
- Modify: `wwwroot/css/site.css:600-621`

- [ ] **Step 1: Review and remove conflicting legacy sidebar rules**

Read `site.css` lines 600-621. Remove or comment out any rules that set:
- `.app-sidebar` width at 1024px breakpoint (lines 601-603: `width: 220px`)
- `.app-sidebar` `left: -260px` mobile positioning (lines 607-612) — this conflicts with the new `transform: translateX` approach in navigation.css
- `.app-sidebar.open { left: 0 }` (line 609) — replaced by transform-based mobile nav
- `transition: left 0.3s ease` (line 611) — no longer needed

These are superseded by `navigation.css` which is the authoritative source for all sidebar positioning.

- [ ] **Step 2: Verify no other `site.css` references to sidebar width/position**

Search: `grep -n "app-sidebar\|sidebar-width" wwwroot/css/site.css`

Remove any remaining conflicts.

- [ ] **Step 3: Commit**

```bash
git add wwwroot/css/site.css
git commit -m "fix: remove legacy sidebar width/position rules from site.css

These rules conflicted with the new fixed-position overlay sidebar
in navigation.css."
```

---

## Task 6: Build + Manual Verification

**Files:** None (testing only)

- [ ] **Step 1: Full build**

Run: `dotnet build 2>&1 | tail -5`
Expected: 0 warnings, 0 errors

- [ ] **Step 2: Run unit tests**

Run: `dotnet test --filter "FullyQualifiedName!~IntegrationTests" 2>&1 | tail -5`
Expected: 259 passed, 0 failed

- [ ] **Step 3: Manual browser verification**

Start the app and verify all 12 items from the spec's verification plan:

1. Click `»` → sidebar slides away, `«` tab + 4px strip visible
2. Click `«` → sidebar slides in with shadow
3. Sidebar slides as full 260px panel (not narrow strip)
4. Ctrl+B toggles correctly
5. Reload page → collapsed state preserved, correct icon
6. Switch to Hebrew → tab on correct side, animation reversed
7. Dark mode → shadow and strip visible
8. Shikma calendar fills screen when collapsed
9. Mobile (<768px) → hamburger + overlay unchanged
10. No FOUC on page load in collapsed state
11. Open a modal → appears above sidebar
12. z-index stack: sidebar < modal backdrop < modal < toast

- [ ] **Step 4: Final commit if any adjustments needed**

```bash
git add -A
git commit -m "fix: address manual testing findings"
```
