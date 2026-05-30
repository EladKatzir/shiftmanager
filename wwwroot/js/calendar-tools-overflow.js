// calendar-tools-overflow.js
// Mobile (<768px) Tools overflow sheet. Clones the hidden toolbar controls
// into the sheet on open() so the original elements remain the source of
// truth (their event handlers, form bindings, and IDs are untouched).
//
// On close(), the clones are dropped — the originals stay hidden via CSS.
//
// Why clone instead of move? Moving the originals would (a) break their
// IDs once the move/restore races with other JS, (b) tear down event
// listeners that were attached imperatively (e.g. distribution-lists.js),
// and (c) leave the toolbar in an inconsistent state if the viewport
// resizes from <768px to >=769px while the sheet is open.
//
// The clones are inert reflections: clicking a cloned <a> still navigates
// because the href is intact; clicking a cloned <button> with an inline
// onclick still fires because cloneNode(true) copies attributes (and the
// onclick targets window-scoped globals, not the original DOM node).

(function () {
    'use strict';

    var SHEET_ID = 'calendarToolsSheet';
    var SHEET_BODY_ID = 'calendarToolsSheetBody';

    // Mirror of the mobile-CSS hide list (see calendar.css mobile block).
    // Keep these two lists in sync (with calendar.css @media (max-width: 768px))
    // — anything hidden by CSS but missing from this list will be invisible
    // AND missing from the overflow. Overview's toolbar uses .overview-calendar__*
    // class namespace (Task 6.5 aliased the wrapper but not the children), so
    // mobile selectors target BOTH namespaces.
    var HIDDEN_SELECTORS = [
        '.cal-toolbar__toggle-group:not(.cal-toolbar__primary)',
        '.cal-toolbar__selector:not(.cal-toolbar__primary)',
        '.cal-toolbar .quick-entry-toggle',
        '.cal-toolbar .quick-entry-help-btn',
        '.cal-toolbar .dl-control',
        // Overview-specific (overview-calendar__* namespace)
        '.overview-calendar__selector:not(.cal-toolbar__primary)',
        '.overview-calendar__company-badge'
    ];

    function open() {
        var sheet = document.getElementById(SHEET_ID);
        var body = document.getElementById(SHEET_BODY_ID);
        if (!sheet || !body) return;

        body.innerHTML = '';
        HIDDEN_SELECTORS.forEach(function (sel) {
            document.querySelectorAll(sel).forEach(function (el) {
                var clone = el.cloneNode(true);
                // Avoid duplicate IDs — server state stays on the original.
                clone.removeAttribute('id');
                // Also strip IDs from descendant elements (form controls,
                // dropdown anchors) for the same reason.
                clone.querySelectorAll('[id]').forEach(function (child) {
                    child.removeAttribute('id');
                });
                body.appendChild(clone);
            });
        });

        sheet.hidden = false;
        sheet.classList.add('bottom-sheet--open');
        document.body.classList.add('bottom-sheet-open');
    }

    function close() {
        var sheet = document.getElementById(SHEET_ID);
        var body = document.getElementById(SHEET_BODY_ID);
        if (!sheet) return;

        sheet.classList.remove('bottom-sheet--open');
        document.body.classList.remove('bottom-sheet-open');
        sheet.hidden = true;
        if (body) body.innerHTML = '';
    }

    window.CalendarToolsOverflow = { open: open, close: close };
})();
