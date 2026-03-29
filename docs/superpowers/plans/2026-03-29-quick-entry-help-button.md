# Quick Entry Help Button Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `?` help button next to the Quick Entry toggle on all 3 calendar pages that opens a bilingual (English/Hebrew) modal with a complete reference guide.

**Architecture:** The help modal is built entirely in JS inside the existing `calendar-quick-entry.js` IIFE. Content is hardcoded bilingual (not via localization lookup). CSS added to `calendar-quick-entry.css`. The `?` button HTML is added to 3 Razor pages. Only 2 `.resx` keys needed for the button's server-side attributes.

**Tech Stack:** Vanilla JS (IIFE pattern), CSS custom properties, ASP.NET Razor Pages, `.resx` localization

**Spec:** `docs/superpowers/specs/2026-03-29-quick-entry-help-button-design.md`

---

### Task 1: Add CSS styles for the help button and modal

**Files:**
- Modify: `wwwroot/css/calendar-quick-entry.css`

- [ ] **Step 1: Add help button, modal, and print/mobile styles**

Append after the closing `}` of the `@media (prefers-color-scheme: dark)` block (line 225), at the very end of the file:

```css
/* ── Quick Entry Help Button ── */
.quick-entry-help-btn {
    font-weight: 700;
    font-size: 0.875rem;
    min-width: 28px;
    height: 28px;
    padding: 0;
    border-radius: 50%;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    color: var(--text-muted);
    transition: all 0.2s ease;
}

.quick-entry-help-btn:hover {
    color: var(--primary);
    background: var(--primary-soft, rgba(59, 130, 246, 0.1));
}

@media (pointer: coarse) {
    .quick-entry-help-btn {
        display: none !important;
    }
}

/* ── Quick Entry Help Modal ── */
.quick-entry-help-modal {
    position: fixed;
    inset: 0;
    z-index: 10001;
    background: rgba(0, 0, 0, 0.4);
    display: flex;
    align-items: center;
    justify-content: center;
    padding: var(--space-4);
}

.quick-entry-help-dialog {
    background: var(--surface, #fff);
    border-radius: var(--radius-lg, 12px);
    box-shadow: var(--shadow-xl, 0 8px 32px rgba(0,0,0,0.2));
    width: 100%;
    max-width: 480px;
    max-height: 85vh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.quick-entry-help-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: var(--space-3) var(--space-4);
    border-bottom: 1px solid var(--border, #e2e8f0);
}

.quick-entry-help-header h3 {
    margin: 0;
    font-size: 1rem;
    font-weight: 700;
    color: var(--text, #1e293b);
}

.quick-entry-help-close {
    background: none;
    border: none;
    cursor: pointer;
    color: var(--text-muted, #94a3b8);
    font-size: 1.25rem;
    line-height: 1;
    padding: var(--space-1);
    border-radius: var(--radius-sm);
}

.quick-entry-help-close:hover {
    color: var(--text, #1e293b);
    background: var(--surface-hover, #f1f5f9);
}

/* Tabs */
.quick-entry-help-tabs {
    display: flex;
    border-bottom: 1px solid var(--border, #e2e8f0);
    padding: 0;
    margin: 0;
    list-style: none;
}

.quick-entry-help-tab {
    flex: 1;
    text-align: center;
    padding: var(--space-2) var(--space-3);
    font-size: 0.8125rem;
    font-weight: 500;
    color: var(--text-muted, #94a3b8);
    cursor: pointer;
    background: none;
    border: none;
    border-bottom: 2px solid transparent;
    transition: all 0.15s ease;
}

.quick-entry-help-tab:hover {
    color: var(--text, #334155);
}

.quick-entry-help-tab[aria-selected="true"] {
    color: var(--primary, #3b82f6);
    font-weight: 600;
    border-bottom-color: var(--primary, #3b82f6);
}

/* Content panels */
.quick-entry-help-panel {
    padding: var(--space-3) var(--space-4);
    overflow-y: auto;
    flex: 1;
    font-size: 0.8125rem;
    line-height: 1.7;
    color: var(--text, #334155);
}

.quick-entry-help-section-title {
    font-weight: 700;
    font-size: 0.6875rem;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    color: var(--text-muted, #64748b);
    margin: var(--space-3) 0 var(--space-1) 0;
}

.quick-entry-help-section-title:first-child {
    margin-top: 0;
}

.quick-entry-help-commands {
    background: var(--surface-hover, #f8fafc);
    border-radius: var(--radius-md, 6px);
    padding: var(--space-2) var(--space-3);
}

.quick-entry-help-cmd-row {
    display: flex;
    gap: var(--space-2);
    align-items: center;
    padding: 2px 0;
}

.quick-entry-help-cmd-row code {
    background: var(--border, #e2e8f0);
    padding: 1px 6px;
    border-radius: 3px;
    font-size: 0.75rem;
    font-weight: 600;
    white-space: nowrap;
}

.quick-entry-help-cmd-icon {
    font-size: 1rem;
    width: 20px;
    text-align: center;
    flex-shrink: 0;
}

.quick-entry-help-cmd-unavail {
    opacity: 0.4;
}

.quick-entry-help-kbd-grid {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 4px 12px;
    align-items: center;
}

.quick-entry-help-kbd-grid kbd {
    background: var(--surface-hover, #f1f5f9);
    padding: 2px 8px;
    border-radius: 3px;
    border: 1px solid var(--border, #e2e8f0);
    font-size: 0.6875rem;
    font-family: inherit;
    text-align: center;
    white-space: nowrap;
    direction: ltr;
}

.quick-entry-help-avail {
    background: var(--surface-hover, #f8fafc);
    border-radius: var(--radius-md, 6px);
    padding: var(--space-2) var(--space-3);
    font-size: 0.8125rem;
}

.quick-entry-help-avail-row {
    display: flex;
    gap: var(--space-1);
    align-items: center;
    padding: 2px 0;
}

.quick-entry-help-avail-check {
    color: #22c55e;
    font-size: 0.8125rem;
    flex-shrink: 0;
}

/* Dark mode (correct selector) */
[data-theme="dark"] .quick-entry-help-modal {
    background: rgba(0, 0, 0, 0.6);
}

[data-theme="dark"] .quick-entry-help-dialog {
    background: var(--surface, #1e293b);
}

[data-theme="dark"] .quick-entry-help-commands,
[data-theme="dark"] .quick-entry-help-avail {
    background: var(--surface-hover, #334155);
}

[data-theme="dark"] .quick-entry-help-cmd-row code {
    background: var(--border, #475569);
}

[data-theme="dark"] .quick-entry-help-kbd-grid kbd {
    background: var(--surface-hover, #334155);
    border-color: var(--border, #475569);
}
```

- [ ] **Step 2: Add help button and modal to print and mobile hide rules**

Edit the existing `@media print` block (line 12-18) to include the new classes:

Replace:
```css
@media print {
    .quick-entry-toggle,
    .quick-entry-dropdown,
    .quick-entry-input {
        display: none !important;
    }
}
```

With:
```css
@media print {
    .quick-entry-toggle,
    .quick-entry-help-btn,
    .quick-entry-help-modal,
    .quick-entry-dropdown,
    .quick-entry-input {
        display: none !important;
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add wwwroot/css/calendar-quick-entry.css
git commit -m "feat: add CSS styles for quick entry help button and modal"
```

---

### Task 2: Add showHelp() to calendar-quick-entry.js

**Files:**
- Modify: `wwwroot/js/calendar-quick-entry.js`

- [ ] **Step 1: Add the help modal builder and public API**

In `wwwroot/js/calendar-quick-entry.js`, add the help modal functions BEFORE the `// --- Public API ---` comment (line 1127). Then update the public API to expose `showHelp`.

Insert before `// --- Public API ---` (line 1127):

```javascript
    // --- Help Modal ---

    var helpModal = null;

    function showHelp() {
        if (helpModal) return; // already open
        helpModal = createHelpModal();
        document.body.appendChild(helpModal);
        if (window.ModalFocus && window.ModalFocus.open) {
            window.ModalFocus.open(helpModal, {
                onClose: function () {
                    // Called by ModalFocus on ESC or backdrop click — clean up DOM + state
                    if (helpModal && helpModal.parentNode) {
                        helpModal.parentNode.removeChild(helpModal);
                    }
                    helpModal = null;
                }
            });
        }
    }

    function closeHelp() {
        if (!helpModal) return;
        var modal = helpModal;
        helpModal = null; // Clear ref first to prevent onClose callback from double-removing
        if (window.ModalFocus && window.ModalFocus.close) {
            window.ModalFocus.close(modal);
            // ModalFocus handles focus return to previousActiveElement (the ? button)
        } else {
            // Fallback: return focus manually when ModalFocus unavailable
            var btn = document.getElementById('quickEntryHelp');
            if (btn) btn.focus();
        }
        if (modal.parentNode) {
            modal.parentNode.removeChild(modal);
        }
    }

    function switchHelpTab(lang) {
        if (!helpModal) return;
        var tabs = helpModal.querySelectorAll('[role="tab"]');
        var panels = helpModal.querySelectorAll('[role="tabpanel"]');
        for (var i = 0; i < tabs.length; i++) {
            var isActive = tabs[i].getAttribute('data-lang') === lang;
            tabs[i].setAttribute('aria-selected', isActive ? 'true' : 'false');
            tabs[i].setAttribute('tabindex', isActive ? '0' : '-1');
        }
        for (var j = 0; j < panels.length; j++) {
            panels[j].style.display = panels[j].getAttribute('data-lang') === lang ? '' : 'none';
        }
    }

    function handleHelpTabKeydown(e) {
        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
            e.preventDefault();
            var current = e.target.getAttribute('data-lang');
            var next = current === 'en' ? 'he' : 'en';
            switchHelpTab(next);
            var nextTab = helpModal.querySelector('[role="tab"][data-lang="' + next + '"]');
            if (nextTab) nextTab.focus();
        }
    }

    function createHelpModal() {
        var isHe = getCulture() === 'he-IL';
        var defaultLang = isHe ? 'he' : 'en';

        var modal = document.createElement('div');
        modal.className = 'quick-entry-help-modal';
        modal.setAttribute('role', 'dialog');
        modal.setAttribute('aria-modal', 'true');
        modal.setAttribute('aria-labelledby', 'quickEntryHelpTitle');

        var dialog = document.createElement('div');
        dialog.className = 'quick-entry-help-dialog modal__content';

        // Header
        var header = document.createElement('div');
        header.className = 'quick-entry-help-header';
        var closeLabel = (window.AppLocalizer && window.AppLocalizer['Aria_CloseDialog']) || (isHe ? '\u05E1\u05D2\u05D9\u05E8\u05D4' : 'Close');
        header.innerHTML =
            '<h3 id="quickEntryHelpTitle">' + (isHe ? '\u05DE\u05D3\u05E8\u05D9\u05DA \u05D4\u05E7\u05E6\u05D0\u05D4 \u05DE\u05D4\u05D9\u05E8\u05D4' : 'Quick Entry Guide') + '</h3>' +
            '<button type="button" class="quick-entry-help-close" aria-label="' + closeLabel + '" onclick="window.CalendarQuickEntry.closeHelp()">&times;</button>';
        dialog.appendChild(header);

        // Tabs
        var tablist = document.createElement('div');
        tablist.className = 'quick-entry-help-tabs';
        tablist.setAttribute('role', 'tablist');
        tablist.innerHTML =
            '<button role="tab" class="quick-entry-help-tab" data-lang="en" id="qeHelpTabEn" ' +
                'aria-controls="qeHelpPanelEn" aria-selected="' + (defaultLang === 'en' ? 'true' : 'false') + '" ' +
                'tabindex="' + (defaultLang === 'en' ? '0' : '-1') + '" ' +
                'onclick="window.CalendarQuickEntry.switchTab(\'en\')" ' +
                'onkeydown="window.CalendarQuickEntry._tabKeydown(event)">English</button>' +
            '<button role="tab" class="quick-entry-help-tab" data-lang="he" id="qeHelpTabHe" ' +
                'aria-controls="qeHelpPanelHe" aria-selected="' + (defaultLang === 'he' ? 'true' : 'false') + '" ' +
                'tabindex="' + (defaultLang === 'he' ? '0' : '-1') + '" ' +
                'onclick="window.CalendarQuickEntry.switchTab(\'he\')" ' +
                'onkeydown="window.CalendarQuickEntry._tabKeydown(event)">\u05E2\u05D1\u05E8\u05D9\u05EA</button>';
        dialog.appendChild(tablist);

        // English panel
        var enPanel = document.createElement('div');
        enPanel.setAttribute('role', 'tabpanel');
        enPanel.setAttribute('id', 'qeHelpPanelEn');
        enPanel.setAttribute('aria-labelledby', 'qeHelpTabEn');
        enPanel.setAttribute('data-lang', 'en');
        enPanel.className = 'quick-entry-help-panel';
        enPanel.style.display = defaultLang === 'en' ? '' : 'none';
        enPanel.innerHTML = buildHelpContentEn();
        dialog.appendChild(enPanel);

        // Hebrew panel
        var hePanel = document.createElement('div');
        hePanel.setAttribute('role', 'tabpanel');
        hePanel.setAttribute('id', 'qeHelpPanelHe');
        hePanel.setAttribute('aria-labelledby', 'qeHelpTabHe');
        hePanel.setAttribute('data-lang', 'he');
        hePanel.className = 'quick-entry-help-panel';
        hePanel.dir = 'rtl';
        hePanel.style.display = defaultLang === 'he' ? '' : 'none';
        hePanel.innerHTML = buildHelpContentHe();
        dialog.appendChild(hePanel);

        modal.appendChild(dialog);
        return modal;
    }

    function buildHelpContentEn() {
        return '' +
            '<div class="quick-entry-help-section-title">Getting Started</div>' +
            '<p>Click any empty cell to start typing. Your input searches for employees to assign. ' +
            'Press <kbd>Enter</kbd> to confirm, <kbd>Esc</kbd> to cancel.</p>' +

            '<div class="quick-entry-help-section-title">Commands</div>' +
            '<div class="quick-entry-help-commands">' +
                '<div class="quick-entry-help-cmd-row"><span class="quick-entry-help-cmd-icon">\uD83D\uDCC5</span><code>/shift</code><span>Assign shift</span></div>' +
                '<div class="quick-entry-help-cmd-row"><span class="quick-entry-help-cmd-icon">\uD83E\uDDF9</span><code>/chore</code><span>Assign chore \u2192 select type \u2192 enter title</span></div>' +
                '<div class="quick-entry-help-cmd-row"><span class="quick-entry-help-cmd-icon">\uD83D\uDEE1\uFE0F</span><code>/duty</code><span>Assign on-call duty</span></div>' +
                '<div class="quick-entry-help-cmd-row"><span class="quick-entry-help-cmd-icon">\uD83C\uDFE0</span><code>/home</code><span>HOME shift</span></div>' +
            '</div>' +

            '<div class="quick-entry-help-section-title">Text Notes</div>' +
            '<p>Type any text without <code>/</code> to save it as a calendar note on that cell. ' +
            'Notes appear as \uD83D\uDCDD badges on other calendar views.</p>' +
            '<p style="color:var(--text-muted);font-size:0.75rem;font-style:italic;">Available in user-mode only (Shifts page).</p>' +

            '<div class="quick-entry-help-section-title">Keyboard</div>' +
            '<div class="quick-entry-help-kbd-grid">' +
                '<kbd>Tab</kbd><span>Next cell</span>' +
                '<kbd>Shift+Tab</kbd><span>Previous cell</span>' +
                '<kbd>Enter</kbd><span>Confirm selection</span>' +
                '<kbd>Esc</kbd><span>Cancel &amp; close</span>' +
                '<kbd>/</kbd><span>Open command palette</span>' +
            '</div>' +

            '<div class="quick-entry-help-section-title">Availability by Page</div>' +
            '<div class="quick-entry-help-avail">' +
                '<div class="quick-entry-help-avail-row"><span class="quick-entry-help-avail-check">\u2713</span><strong>Shifts</strong><span>\u2014 all commands + text notes</span></div>' +
                '<div class="quick-entry-help-avail-row"><span class="quick-entry-help-avail-check">\u2713</span><strong>Chores</strong><span>\u2014 <code>/chore</code> only</span></div>' +
                '<div class="quick-entry-help-avail-row"><span class="quick-entry-help-avail-check">\u2713</span><strong>On-Call</strong><span>\u2014 <code>/duty</code> only</span></div>' +
            '</div>';
    }

    function buildHelpContentHe() {
        return '' +
            '<div class="quick-entry-help-section-title">\u05EA\u05D7\u05D9\u05DC\u05EA \u05E2\u05D1\u05D5\u05D3\u05D4</div>' +
            '<p>\u05DC\u05D7\u05E6\u05D5 \u05E2\u05DC \u05EA\u05D0 \u05E8\u05D9\u05E7 \u05DB\u05D3\u05D9 \u05DC\u05D4\u05EA\u05D7\u05D9\u05DC \u05DC\u05D4\u05E7\u05DC\u05D9\u05D3. ' +
            '\u05D4\u05D4\u05E7\u05DC\u05D3\u05D4 \u05DE\u05D7\u05E4\u05E9\u05EA \u05E2\u05D5\u05D1\u05D3\u05D9\u05DD \u05DC\u05E9\u05D9\u05D1\u05D5\u05E5. ' +
            '\u05DC\u05D7\u05E6\u05D5 <kbd dir="ltr">Enter</kbd> \u05DC\u05D0\u05D9\u05E9\u05D5\u05E8, <kbd dir="ltr">Esc</kbd> \u05DC\u05D1\u05D9\u05D8\u05D5\u05DC.</p>' +

            '<div class="quick-entry-help-section-title">\u05E4\u05E7\u05D5\u05D3\u05D5\u05EA</div>' +
            '<div class="quick-entry-help-commands">' +
                '<div class="quick-entry-help-cmd-row"><span class="quick-entry-help-cmd-icon">\uD83D\uDCC5</span><code dir="ltr">/shift</code><span>\u05E9\u05D9\u05D1\u05D5\u05E5 \u05DE\u05E9\u05DE\u05E8\u05EA</span></div>' +
                '<div class="quick-entry-help-cmd-row"><span class="quick-entry-help-cmd-icon">\uD83E\uDDF9</span><code dir="ltr">/chore</code><span>\u05E9\u05D9\u05D1\u05D5\u05E5 \u05EA\u05D5\u05E8\u05E0\u05D5\u05EA \u2192 \u05D1\u05D7\u05D9\u05E8\u05EA \u05E1\u05D5\u05D2 \u2192 \u05D4\u05D6\u05E0\u05EA \u05DB\u05D5\u05EA\u05E8\u05EA</span></div>' +
                '<div class="quick-entry-help-cmd-row"><span class="quick-entry-help-cmd-icon">\uD83D\uDEE1\uFE0F</span><code dir="ltr">/duty</code><span>\u05E9\u05D9\u05D1\u05D5\u05E5 \u05DB\u05D5\u05E0\u05E0\u05D5\u05EA</span></div>' +
                '<div class="quick-entry-help-cmd-row"><span class="quick-entry-help-cmd-icon">\uD83C\uDFE0</span><code dir="ltr">/home</code><span>\u05DE\u05E9\u05DE\u05E8\u05EA \u05D1\u05D9\u05EA</span></div>' +
            '</div>' +

            '<div class="quick-entry-help-section-title">\u05D4\u05E2\u05E8\u05D5\u05EA \u05D8\u05E7\u05E1\u05D8</div>' +
            '<p>\u05D4\u05E7\u05DC\u05D9\u05D3\u05D5 \u05D8\u05E7\u05E1\u05D8 \u05D7\u05D5\u05E4\u05E9\u05D9 \u05DC\u05DC\u05D0 <code dir="ltr">/</code> ' +
            '\u05DB\u05D3\u05D9 \u05DC\u05E9\u05DE\u05D5\u05E8 \u05D4\u05E2\u05E8\u05D4 \u05E2\u05DC \u05D4\u05EA\u05D0. ' +
            '\u05D4\u05E2\u05E8\u05D5\u05EA \u05DE\u05D5\u05E4\u05D9\u05E2\u05D5\u05EA \u05DB\u05E1\u05DE\u05DC \uD83D\uDCDD \u05D1\u05EA\u05E6\u05D5\u05D2\u05D5\u05EA \u05DC\u05D5\u05D7 \u05E9\u05E0\u05D4 \u05D0\u05D7\u05E8\u05D5\u05EA.</p>' +
            '<p style="color:var(--text-muted);font-size:0.75rem;font-style:italic;">\u05D6\u05DE\u05D9\u05DF \u05E8\u05E7 \u05D1\u05EA\u05E6\u05D5\u05D2\u05EA \u05E2\u05D5\u05D1\u05D3\u05D9\u05DD (\u05D3\u05E3 \u05DE\u05E9\u05DE\u05E8\u05D5\u05EA).</p>' +

            '<div class="quick-entry-help-section-title">\u05DE\u05E7\u05E9\u05D9 \u05E7\u05D9\u05E6\u05D5\u05E8</div>' +
            '<div class="quick-entry-help-kbd-grid">' +
                '<kbd dir="ltr">Tab</kbd><span>\u05EA\u05D0 \u05D4\u05D1\u05D0</span>' +
                '<kbd dir="ltr">Shift+Tab</kbd><span>\u05EA\u05D0 \u05E7\u05D5\u05D3\u05DD</span>' +
                '<kbd dir="ltr">Enter</kbd><span>\u05D0\u05D9\u05E9\u05D5\u05E8 \u05D1\u05D7\u05D9\u05E8\u05D4</span>' +
                '<kbd dir="ltr">Esc</kbd><span>\u05D1\u05D9\u05D8\u05D5\u05DC \u05D5\u05E1\u05D2\u05D9\u05E8\u05D4</span>' +
                '<kbd dir="ltr">/</kbd><span>\u05E4\u05EA\u05D9\u05D7\u05EA \u05EA\u05E4\u05E8\u05D9\u05D8 \u05E4\u05E7\u05D5\u05D3\u05D5\u05EA</span>' +
            '</div>' +

            '<div class="quick-entry-help-section-title">\u05D6\u05DE\u05D9\u05E0\u05D5\u05EA \u05DC\u05E4\u05D9 \u05D3\u05E3</div>' +
            '<div class="quick-entry-help-avail">' +
                '<div class="quick-entry-help-avail-row"><span class="quick-entry-help-avail-check">\u2713</span><strong>\u05DE\u05E9\u05DE\u05E8\u05D5\u05EA</strong><span>\u2014 \u05DB\u05DC \u05D4\u05E4\u05E7\u05D5\u05D3\u05D5\u05EA + \u05D4\u05E2\u05E8\u05D5\u05EA \u05D8\u05E7\u05E1\u05D8</span></div>' +
                '<div class="quick-entry-help-avail-row"><span class="quick-entry-help-avail-check">\u2713</span><strong>\u05EA\u05D5\u05E8\u05E0\u05D5\u05D9\u05D5\u05EA</strong><span>\u2014 <code dir="ltr">/chore</code> \u05D1\u05DC\u05D1\u05D3</span></div>' +
                '<div class="quick-entry-help-avail-row"><span class="quick-entry-help-avail-check">\u2713</span><strong>\u05DB\u05D5\u05E0\u05E0\u05D9\u05D5\u05EA</strong><span>\u2014 <code dir="ltr">/duty</code> \u05D1\u05DC\u05D1\u05D3</span></div>' +
            '</div>';
    }
```

- [ ] **Step 2: Update the public API block**

Replace the existing public API block (line 1129-1132):

```javascript
    window.CalendarQuickEntry = {
        toggle: toggle,
        isActive: function () { return isActive; }
    };
```

With:

```javascript
    window.CalendarQuickEntry = {
        toggle: toggle,
        isActive: function () { return isActive; },
        showHelp: showHelp,
        closeHelp: closeHelp,
        switchTab: switchHelpTab,
        _tabKeydown: handleHelpTabKeydown
    };
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add wwwroot/js/calendar-quick-entry.js
git commit -m "feat: add showHelp() with bilingual help modal to CalendarQuickEntry"
```

---

### Task 3: Add ? button HTML to all 3 calendar pages

**Files:**
- Modify: `Pages/Calendar/Shifts.cshtml:139-147`
- Modify: `Pages/Calendar/Chores.cshtml:105-113`
- Modify: `Pages/Calendar/OnCall.cshtml:109-117`

- [ ] **Step 1: Add ? button to Shifts.cshtml**

In `Pages/Calendar/Shifts.cshtml`, find the `@if (Model.CanEdit)` block (line 139-147). Add the help button AFTER the quick entry toggle button, BEFORE the closing `}`:

Replace:
```html
            @if (Model.CanEdit)
            {
                <button type="button" id="quickEntryToggle" class="btn btn-ghost quick-entry-toggle"
                        onclick="window.CalendarQuickEntry && window.CalendarQuickEntry.toggle()"
                        title="@Localizer["QuickEntry_Toggle"]">
                    <span class="btn-icon">&#9889;</span>
                    <loc key="QuickEntry_Toggle" />
                </button>
            }
```

With:
```html
            @if (Model.CanEdit)
            {
                <button type="button" id="quickEntryToggle" class="btn btn-ghost quick-entry-toggle"
                        onclick="window.CalendarQuickEntry && window.CalendarQuickEntry.toggle()"
                        title="@Localizer["QuickEntry_Toggle"]">
                    <span class="btn-icon">&#9889;</span>
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

- [ ] **Step 2: Add ? button to Chores.cshtml**

In `Pages/Calendar/Chores.cshtml`, same pattern. Find the `@if (Model.CanEdit)` block (line 105-113):

Replace:
```html
            @if (Model.CanEdit)
            {
                <button type="button" id="quickEntryToggle" class="btn btn-ghost quick-entry-toggle"
                        onclick="window.CalendarQuickEntry && window.CalendarQuickEntry.toggle()"
                        title="@Localizer["QuickEntry_Toggle"]">
                    <span class="btn-icon">&#9889;</span>
                    <loc key="QuickEntry_Toggle" />
                </button>
            }
```

With:
```html
            @if (Model.CanEdit)
            {
                <button type="button" id="quickEntryToggle" class="btn btn-ghost quick-entry-toggle"
                        onclick="window.CalendarQuickEntry && window.CalendarQuickEntry.toggle()"
                        title="@Localizer["QuickEntry_Toggle"]">
                    <span class="btn-icon">&#9889;</span>
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

- [ ] **Step 3: Add ? button to OnCall.cshtml**

In `Pages/Calendar/OnCall.cshtml`, same pattern. Find the `@if (Model.CanEdit)` block (line 109-117):

Replace:
```html
            @if (Model.CanEdit)
            {
                <button type="button" id="quickEntryToggle" class="btn btn-ghost quick-entry-toggle"
                        onclick="window.CalendarQuickEntry && window.CalendarQuickEntry.toggle()"
                        title="@Localizer["QuickEntry_Toggle"]">
                    <span class="btn-icon">&#9889;</span>
                    <loc key="QuickEntry_Toggle" />
                </button>
            }
```

With:
```html
            @if (Model.CanEdit)
            {
                <button type="button" id="quickEntryToggle" class="btn btn-ghost quick-entry-toggle"
                        onclick="window.CalendarQuickEntry && window.CalendarQuickEntry.toggle()"
                        title="@Localizer["QuickEntry_Toggle"]">
                    <span class="btn-icon">&#9889;</span>
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

- [ ] **Step 4: Verify build**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add Pages/Calendar/Shifts.cshtml Pages/Calendar/Chores.cshtml Pages/Calendar/OnCall.cshtml
git commit -m "feat: add ? help button to Shifts, Chores, and OnCall calendar pages"
```

---

### Task 4: Add localization keys to .resx files

**Files:**
- Modify: `Resources/SharedResources.resx`
- Modify: `Resources/SharedResources.he-IL.resx`

- [ ] **Step 1: Add English keys to SharedResources.resx**

Add these entries before the closing `</root>` tag in `Resources/SharedResources.resx` (near the existing `QuickEntry_Toggle` key at line 13636):

```xml
  <data name="QuickEntry_HelpTitle" xml:space="preserve">
    <value>Quick Entry Help</value>
  </data>
```

- [ ] **Step 2: Add Hebrew key to SharedResources.he-IL.resx**

Add this entry before the closing `</root>` tag in `Resources/SharedResources.he-IL.resx` (near the existing `QuickEntry_Toggle` key at line 13602):

```xml
  <data name="QuickEntry_HelpTitle" xml:space="preserve">
    <value>עזרה להקצאה מהירה</value>
  </data>
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat: add QuickEntry_HelpTitle localization key"
```

---

### Task 5: Manual verification

**Files:** None (testing only)

- [ ] **Step 1: Start the application**

Run: `dotnet run`

Navigate to the application in a browser. Log in as a user with calendar edit permissions (e.g., `test.manager@shifty.test / TestManager123!`).

- [ ] **Step 2: Verify ? button appears on Shifts page**

Navigate to `/Calendar/Shifts`. Confirm:
- The `?` button appears next to the `⚡ Quick Entry` toggle
- The `?` button is compact, circular, and subtle
- Hovering shows the "Quick Entry Help" tooltip

- [ ] **Step 3: Verify modal opens with English tab**

Click the `?` button. Confirm:
- Modal opens centered with backdrop
- Title shows "Quick Entry Guide"
- English tab is selected by default (if browser is English)
- All 5 sections visible: Getting Started, Commands, Text Notes, Keyboard, Availability by Page
- All 4 slash commands shown with icons
- Keyboard shortcuts grid renders correctly

- [ ] **Step 4: Verify Hebrew tab**

Click the "עברית" tab. Confirm:
- Content switches to Hebrew RTL
- Slash commands remain in English (`/shift`, `/chore`, etc.)
- Keyboard keys remain LTR
- Arrows in chore description point right (`→`)
- All section titles in Hebrew

- [ ] **Step 5: Verify modal accessibility**

Test:
- Press `Esc` — modal should close, focus returns to `?` button
- Reopen, click backdrop — modal should close
- Reopen, press `Tab` — focus should cycle within modal (focus trap)
- On tab bar, press Left/Right arrow — should switch between English/Hebrew tabs

- [ ] **Step 6: Verify on Chores and OnCall pages**

Navigate to `/Calendar/Chores` and `/Calendar/OnCall`. Confirm the `?` button appears and the modal opens correctly on both pages.

- [ ] **Step 7: Verify dark mode**

Toggle the theme to dark mode. Reopen the help modal. Confirm:
- Dialog background is dark
- Command card and availability card backgrounds are slightly lighter
- Text is readable (no dark-on-dark issues)
- Kbd keys have appropriate dark borders

- [ ] **Step 8: Verify non-editor cannot see button**

Log out and log in as a regular Employee. Navigate to `/Calendar/Shifts`. Confirm the `?` button does NOT appear (since `CanEdit` is false for employees).
