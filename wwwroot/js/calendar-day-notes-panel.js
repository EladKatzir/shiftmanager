/**
 * Day-notes panel for the Shifts calendar.
 *
 * A date header shows at most two TRUNCATED PREVIEW chips (how many is decided server-side from the
 * day column's width). This module owns the dialog behind the "+N" / 📝 trigger, which holds every
 * note for that day in full, with its author, its tab badge, and a per-note delete.
 *
 * THREE THINGS SHAPE THIS FILE, all of them non-obvious:
 *
 * 1. THE PANEL MUST LIVE ON document.body, NOT IN THE HEADER.
 *    `.excel-calendar__header` is position:sticky WITH a z-index, which makes <thead> a stacking
 *    context: anything inside composites at that level, so a dialog there can never paint above a
 *    dropdown or a modal regardless of the z-index you give it. position:absolute inside the header
 *    is also clipped by `.excel-calendar { overflow: auto }`, and position:fixed escapes the clip
 *    but then stops following the column when the grid scrolls. Portaling to body solves all three
 *    and inherits the existing --z-modal / --z-modal-backdrop layering.
 *
 * 2. THE GRID IS DESTROYED AND REBUILT ON EVERY ADD OR DELETE.
 *    _doCalendarRefresh re-fetches the page and does liveGrid.replaceWith(fresh) — the whole
 *    `.excel-calendar` subtree — then dispatches `calendar:grid-refreshed`. Had the dialog been
 *    rendered in place, that would rip out the open dialog, its backdrop AND the focused element in
 *    one go, and because the close path never ran, document.body.style.overflow would stay 'hidden'
 *    and leave the page unscrollable with focus on <body>.
 *
 *    Portaled, the dialog survives but its DATA SOURCE does not: the <template> it was cloned from
 *    and the trigger that focus must return to are both replaced. So on refresh this module
 *    (a) detaches the previously portaled nodes BEFORE adopting the fresh ones — otherwise two
 *    elements share each id and getElementById resolves the wrong one; (b) re-renders an open panel
 *    in place instead of dismissing it, so deleting three notes is one panel open rather than three;
 *    and (c) closes only when the day has run out of notes.
 *
 * 3. NO USER-VISIBLE STRING IS BUILT HERE.
 *    The shell's chrome is Razor-localized, and each day's rows are cloned from an inert Razor
 *    <template>. If that ever changes, the string must be added to _LocalizationScript.cshtml, which
 *    is an explicit allowlist — otherwise window.AppLocalizer.TheKey is undefined at runtime.
 */
(function () {
    'use strict';

    var PANEL_ID = 'calDayNotesPanel';
    var BACKDROP_ID = 'calDayNotesBackdrop';

    // The date whose notes are on screen, or null when the panel is closed. Survives a grid refresh
    // so an open panel can be re-rendered rather than dismissed.
    var openDate = null;

    // The nodes this module has moved to body, tracked explicitly so they can be detached before the
    // refreshed grid's copies are adopted.
    var portaled = { panel: null, backdrop: null };

    function grid() {
        return document.querySelector('.excel-calendar');
    }

    /** The trigger is re-found BY DATE, never by a captured node: a refresh replaces the element. */
    function triggerFor(date) {
        if (!date) return null;
        var g = grid();
        if (!g || !window.CSS || !CSS.escape) return null;
        return g.querySelector('.excel-calendar__day-notes-trigger[data-date="' + CSS.escape(date) + '"]');
    }

    function templateFor(date) {
        if (!date) return null;
        var g = grid();
        if (!g || !window.CSS || !CSS.escape) return null;
        return g.querySelector('.excel-calendar__day-notes-template[data-date="' + CSS.escape(date) + '"]');
    }

    /**
     * Move the panel and backdrop out of the grid and onto body, detaching any copies this module
     * portaled earlier. Called on load and again after every grid refresh.
     */
    function portal() {
        var g = grid();
        if (!g) return;

        var freshPanel = g.querySelector('#' + PANEL_ID);
        var freshBackdrop = g.querySelector('#' + BACKDROP_ID);
        if (!freshPanel) return;   // a grid with no day notes renders no panel at all

        // Detach FIRST. Until this runs, the old portaled node and the fresh one share an id, and
        // getElementById would resolve whichever comes first in document order — the grid's copy.
        if (portaled.panel && portaled.panel.parentNode === document.body) {
            document.body.removeChild(portaled.panel);
        }
        if (portaled.backdrop && portaled.backdrop.parentNode === document.body) {
            document.body.removeChild(portaled.backdrop);
        }

        if (freshBackdrop) document.body.appendChild(freshBackdrop);
        document.body.appendChild(freshPanel);
        portaled.panel = freshPanel;
        portaled.backdrop = freshBackdrop;
    }

    function focusables(panel) {
        return Array.prototype.filter.call(
            panel.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'),
            function (el) { return !el.hasAttribute('hidden') && el.offsetParent !== null; });
    }

    /**
     * Fill the panel from the day's <template> and reveal it.
     * @returns {boolean} false when that day has no notes left, in which case nothing is shown.
     */
    function render(date) {
        var panel = portaled.panel;
        if (!panel) return false;

        var tpl = templateFor(date);
        var list = panel.querySelector('#calDayNotesList');
        var empty = panel.querySelector('#calDayNotesEmpty');
        var dateLabel = panel.querySelector('#calDayNotesDate');
        if (!list) return false;

        list.textContent = '';
        var count = 0;
        if (tpl && tpl.content) {
            var clone = tpl.content.cloneNode(true);
            count = clone.querySelectorAll('.cal-day-notes__item').length;
            list.appendChild(clone);
        }

        if (dateLabel) {
            // Taken from the header the user clicked rather than formatted here, so no date-format
            // logic (and no localization) is duplicated in JavaScript.
            var trig = triggerFor(date);
            var th = trig ? trig.closest('.excel-calendar__header-day') : null;
            var d = th ? th.querySelector('.excel-calendar__day-date') : null;
            var n = th ? th.querySelector('.excel-calendar__day-name') : null;
            dateLabel.textContent = (n && d) ? (n.textContent.trim() + ' ' + d.textContent.trim()) : '';
        }

        if (empty) empty.hidden = count > 0;
        return count > 0;
    }

    /**
     * Apply the visible state. Kept separate from render() because the refresh path has to re-apply
     * it: portal() adopts the panel from the REBUILT grid, and Razor renders that copy `hidden` and
     * without .is-open. Filling it with content is not enough — without this the dialog goes
     * invisible while body.style.overflow stays 'hidden', which freezes the page behind nothing.
     */
    function show(date) {
        var panel = portaled.panel;
        if (!panel) return;

        if (portaled.backdrop) {
            portaled.backdrop.hidden = false;
            portaled.backdrop.classList.add('is-open');
        }
        panel.hidden = false;
        panel.classList.add('is-open');
        document.body.style.overflow = 'hidden';

        var trig = triggerFor(date);
        if (trig) trig.setAttribute('aria-expanded', 'true');
    }

    /** Move focus into the dialog when it is not already there (e.g. the button just clicked is gone). */
    function focusInside() {
        var panel = portaled.panel;
        if (!panel) return;
        if (panel.contains(document.activeElement)) return;
        var close = panel.querySelector('#calDayNotesClose');
        if (close) close.focus();
    }

    function open(date) {
        if (!portaled.panel) portal();
        if (!render(date)) return;

        openDate = date;
        show(date);
        focusInside();
    }

    function close(restoreFocus) {
        var panel = portaled.panel;
        var date = openDate;
        openDate = null;

        if (panel) {
            panel.classList.remove('is-open');
            panel.hidden = true;
        }
        if (portaled.backdrop) {
            portaled.backdrop.classList.remove('is-open');
            portaled.backdrop.hidden = true;
        }
        // Always cleared, including on the refresh path — leaving it set is what would make the page
        // silently unscrollable.
        document.body.style.overflow = '';

        var trig = triggerFor(date);
        if (trig) {
            trig.setAttribute('aria-expanded', 'false');
            // Re-found by date, because the node captured at open time has been replaced. When the
            // deleted note was the day's last, the trigger is gone from the refreshed HTML and focus
            // falls back to <body> — a known limitation.
            if (restoreFocus) trig.focus();
        }
    }

    // Delegated: the trigger is replaced on every grid refresh, so nothing may be bound to it.
    document.addEventListener('click', function (e) {
        var trig = e.target.closest ? e.target.closest('.excel-calendar__day-notes-trigger') : null;
        if (trig) {
            e.preventDefault();
            e.stopPropagation();
            open(trig.dataset.date);
            return;
        }

        if (!openDate) return;

        if (e.target.closest('#calDayNotesClose')) {
            e.preventDefault();
            close(true);
            return;
        }
        if (e.target.id === BACKDROP_ID) {
            close(true);
        }
    });

    document.addEventListener('keydown', function (e) {
        if (!openDate || !portaled.panel) return;

        if (e.key === 'Escape') {
            e.preventDefault();
            close(true);
            return;
        }

        if (e.key !== 'Tab') return;

        // Keep focus inside the dialog, same shape as the feedback modal's loop.
        var items = focusables(portaled.panel);
        if (!items.length) return;
        var first = items[0];
        var last = items[items.length - 1];
        if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    });

    /**
     * After a refresh the dialog node is still on body but its template and trigger are new, so
     * re-portal and then re-render IN PLACE. Deleting several notes is therefore one open panel, not
     * one per deletion — and the panel closes only once the day has no notes left.
     */
    document.addEventListener('calendar:grid-refreshed', function () {
        var wasOpenOn = openDate;
        portal();
        if (!wasOpenOn) return;

        openDate = wasOpenOn;
        if (!render(wasOpenOn)) {
            close(false);   // that was the day's last note
            return;
        }

        // Re-apply the visible state, NOT just the content: portal() adopted the rebuilt grid's
        // copy, which Razor renders `hidden` and without .is-open. Skipping this leaves an invisible
        // dialog with body.style.overflow still 'hidden' — a frozen page behind nothing.
        show(wasOpenOn);
        // The row's delete button has just been removed from the DOM, so focus has fallen to <body>.
        focusInside();
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', portal);
    } else {
        portal();
    }
})();
