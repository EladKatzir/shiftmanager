/*
 * Excel-style drag-to-resize for calendar columns.
 *
 * Widths are written to a single <col> element rather than to every <td>, which is why the table
 * runs under `table-layout: fixed` (.excel-calendar--resizable) — under auto layout a <col> width
 * is only a suggestion and the browser re-derives columns from content.
 *
 * Self-contained IIFE with no exports, matching every other calendar module: nothing here may
 * assume a helper defined in another file is visible.
 */
(function () {
    'use strict';

    // Mirror of the widths currently applied. triggerCalendarRefresh() in calendar-inline-edit.js
    // replaces the entire <table> DOM, so this is re-applied on 'calendar:grid-refreshed'.
    var applied = {};
    var drag = null;

    function getGrid() {
        var g = document.querySelector('.excel-calendar[data-width-context]');
        return (g && g.getAttribute('data-width-context')) ? g : null;
    }

    function esc(v) { return (window.CSS && CSS.escape) ? CSS.escape(v) : v; }

    /* Returns EVERY <col> with this key. The day columns deliberately share the key "day" so one
       drag sizes them all and the width survives navigation to another date range. */
    function colsFor(grid, key) {
        return grid.querySelectorAll('col[data-col-key="' + esc(key) + '"]');
    }

    function setWidth(cols, px) {
        Array.prototype.forEach.call(cols, function (col) { col.style.width = px + 'px'; });
    }

    function csrf() {
        var t = document.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }

    /* Only widths that DIFFER from the column's default are persisted, so resetting a column to its
       default is expressed as the absence of a row rather than as a stored duplicate of the default. */
    function collectWidths(grid) {
        var out = {};
        Array.prototype.forEach.call(grid.querySelectorAll('col[data-col-key]'), function (col) {
            var def = parseInt(col.getAttribute('data-default-width'), 10);
            var w = parseInt(col.style.width, 10);
            if (!isNaN(w) && w !== def) out[col.getAttribute('data-col-key')] = w;
        });
        return out;
    }

    function save(grid) {
        var key = grid.getAttribute('data-width-context');
        if (!key) return;
        var widths = collectWidths(grid);
        applied = widths;

        function fail() {
            if (window.showToast) {
                window.showToast(
                    (window.AppLocalizer && window.AppLocalizer.Calendar_ResizeSaveFailed)
                        || 'Could not save column width', 'error');
            }
        }

        fetch('/Api/Calendar/SaveColumnWidths', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest',
                'RequestVerificationToken': csrf()
            },
            credentials: 'same-origin',
            body: JSON.stringify({ contextKey: key, widths: widths })
        }).then(function (r) {
            if (!r.ok) fail();
        }).catch(fail);
    }

    function onPointerDown(e) {
        if (e.button !== 0) return;
        var handle = e.target.closest && e.target.closest('.excel-calendar__col-resizer');
        if (!handle) return;
        var grid = getGrid();
        if (!grid) return;
        var cols = colsFor(grid, handle.getAttribute('data-col-key'));
        var th = handle.parentElement;
        if (!cols.length || !th) return;

        // The corner cell doubles as the row-mode dropdown trigger, and cells below carry
        // quick-entry handlers — neither should see this gesture. preventDefault also suppresses
        // the compatibility mouse events so no click is synthesised on release.
        e.preventDefault();
        e.stopPropagation();

        drag = {
            grid: grid,
            handle: handle,
            cols: cols,
            startX: e.clientX,
            // Measure the rendered <th>, not the <col>: a <col> has no reliable box in every
            // browser. Under fixed layout the two are the same width by definition.
            startWidth: Math.round(th.getBoundingClientRect().width),
            // In Hebrew RTL the inline axis is mirrored, so dragging left must WIDEN the column.
            sign: window.getComputedStyle(grid).direction === 'rtl' ? -1 : 1,
            min: parseInt(grid.getAttribute('data-min-col-width'), 10) || 40,
            max: parseInt(grid.getAttribute('data-max-col-width'), 10) || 600
        };

        handle.classList.add('is-dragging');
        document.documentElement.classList.add('excel-calendar-resizing');
        try { handle.setPointerCapture(e.pointerId); } catch (err) { /* capture is best-effort */ }
    }

    function onPointerMove(e) {
        if (!drag) return;
        var dx = (e.clientX - drag.startX) * drag.sign;
        var w = Math.max(drag.min, Math.min(drag.max, drag.startWidth + dx));
        setWidth(drag.cols, w);
    }

    function endDrag(e, commit) {
        if (!drag) return;
        var d = drag;
        drag = null;
        d.handle.classList.remove('is-dragging');
        document.documentElement.classList.remove('excel-calendar-resizing');
        if (e && e.pointerId !== undefined) {
            try { d.handle.releasePointerCapture(e.pointerId); } catch (err) { /* already released */ }
        }
        if (commit) save(d.grid);
    }

    function onDoubleClick(e) {
        var handle = e.target.closest && e.target.closest('.excel-calendar__col-resizer');
        if (!handle) return;
        var grid = getGrid();
        if (!grid) return;
        var cols = colsFor(grid, handle.getAttribute('data-col-key'));
        if (!cols.length) return;
        e.preventDefault();
        e.stopPropagation();
        setWidth(cols, parseInt(cols[0].getAttribute('data-default-width'), 10));
        save(grid);
    }

    /* Re-apply after a shadow refresh swaps the <table> out. The server also renders saved widths,
       but a drag whose POST is still in flight would otherwise snap back. */
    function reapply(grid) {
        Object.keys(applied).forEach(function (key) {
            setWidth(colsFor(grid, key), applied[key]);
        });
    }

    /* Under `table-layout: fixed` a column can no longer grow to fit its content, so the sticky
       label column would clip names that auto layout used to show in full — a visible regression
       for anyone with long row labels. When the user has NOT chosen a width, measure the widest
       label and adopt it as this render's default: auto-fit is restored, and the column stays
       draggable. The fitted value is written back to data-default-width so collectWidths() does
       not mistake a computed fit for a deliberate user choice and persist it. */
    function autoFitLabel(grid) {
        var cols = colsFor(grid, 'label');
        if (!cols.length) return;
        var col = cols[0];
        var def = parseInt(col.getAttribute('data-default-width'), 10);
        if (parseInt(col.style.width, 10) !== def) return; // user has a saved width — respect it

        var widest = 0;
        Array.prototype.forEach.call(
            grid.querySelectorAll('.excel-calendar__row-label-name, .excel-calendar__row-sublabel'),
            function (el) { if (el.scrollWidth > widest) widest = el.scrollWidth; });
        if (!widest) return;

        var pad = 0;
        var cell = grid.querySelector('.excel-calendar__row-label');
        if (cell) {
            var cs = getComputedStyle(cell);
            pad = (parseFloat(cs.paddingLeft) || 0) + (parseFloat(cs.paddingRight) || 0) + 2;
        }

        var min = parseInt(grid.getAttribute('data-min-col-width'), 10) || 40;
        var max = parseInt(grid.getAttribute('data-max-col-width'), 10) || 600;
        var fitted = Math.min(max, Math.max(min, def, Math.ceil(widest + pad)));
        if (fitted === def) return;

        setWidth(cols, fitted);
        col.setAttribute('data-default-width', String(fitted));
    }

    function enable(grid) {
        if (!grid || grid._colResizeBound) return;
        grid._colResizeBound = true;
        grid.addEventListener('pointerdown', onPointerDown);
        grid.addEventListener('dblclick', onDoubleClick);
    }

    // Move/up live on document: pointer capture retargets the events to the handle, and they
    // still bubble up to here even when the pointer leaves the grid.
    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', function (e) { endDrag(e, true); });
    document.addEventListener('pointercancel', function (e) { endDrag(e, false); });

    function init() {
        var g = getGrid();
        if (!g) return;
        enable(g);
        autoFitLabel(g);
        applied = collectWidths(g); // seed once from what the server rendered
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();

    document.addEventListener('calendar:grid-refreshed', function () {
        var g = getGrid();
        if (!g) return;
        // Deliberately does NOT re-seed `applied` from the new DOM: a drag whose POST is still in
        // flight is not in the server's render yet, and re-seeding would discard it.
        g._colResizeBound = false;
        enable(g);
        autoFitLabel(g);
        reapply(g);
    });
})();
