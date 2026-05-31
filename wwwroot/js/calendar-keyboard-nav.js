/**
 * Calendar keyboard navigation (v2 Excel calendars: Shifts / Chores / On-Call).
 *
 * Arrow keys move focus between grid cells ("cubes"):
 *   Left / Right  — move within the row (RTL-aware). At the first/last day of the visible period,
 *                   they cross into the previous/next period and land on the matching edge cell.
 *   Up / Down     — move between shift/user rows (skipping group headers and collapsed/lazy-hidden
 *                   rows). They stop at the top/bottom row (no period change).
 *   Enter / Space — open the assignment picker on an editable cell.
 *
 * Implemented as a single delegated keydown listener on document, so it survives the in-place grid
 * refresh without any per-cell re-binding. Alt+Arrow is intentionally left to the existing toolbar
 * period-nav handler, so this handler ignores Alt/Ctrl/Meta combinations.
 */
(function () {
    'use strict';

    // Navigable cells carry data-date. The weekly-total cell has class excel-calendar__cell but no
    // data-date, so this selector naturally excludes it.
    var CELL_SELECTOR = '.excel-calendar__cell[data-date]';
    var FOCUS_HINT_KEY = 'calKbdFocus';

    function isRtl() {
        return (document.documentElement.getAttribute('dir') || '').toLowerCase() === 'rtl';
    }

    function cssEscape(value) {
        return (window.CSS && CSS.escape) ? CSS.escape(value) : value;
    }

    // Navigable cells within a row, in DOM order (earliest -> latest date).
    function rowCells(tr) {
        return Array.prototype.slice.call(tr.querySelectorAll(CELL_SELECTOR));
    }

    // Visible data rows (skip group headers and rows hidden by collapse / lazy-load).
    function visibleDataRows() {
        var tbody = document.querySelector('.excel-calendar__table tbody');
        if (!tbody) return [];
        return Array.prototype.slice.call(tbody.querySelectorAll('tr[data-row-id]'))
            .filter(function (tr) {
                if (tr.classList.contains('excel-calendar__group-header')) return false;
                if (tr.style.display === 'none') return false;
                return true;
            });
    }

    function focusCell(cell) {
        if (!cell) return;
        cell.focus();

        // behavior: 'auto' (instant) is deliberate — smooth scrolling during rapid
        // arrow-key navigation creates visible lag that fights the user's input
        // rhythm. Use 'auto' for focus-restoration scrolls, 'smooth' only for
        // discrete user-initiated jumps (e.g. next-group button in Task 18).
        // Pull the cell fully into view inside the calendar's scroll container.
        // Without this, arrow-key navigation can leave the focused cell behind a
        // sticky element (date row, row label, or group band).
        cell.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'auto' });

        // Nudge for sticky obstruction. scrollIntoView('nearest') doesn't account
        // for the sticky elements covering the leading edges of the viewport.
        var container = cell.closest('.excel-calendar');
        if (!container) return;

        var containerRect = container.getBoundingClientRect();
        var cellRect = cell.getBoundingClientRect();
        var styles = getComputedStyle(container);
        var headerHeight = parseInt(styles.getPropertyValue('--excel-calendar-header-height') || '44', 10);
        var rowLabelEl = container.querySelector('.excel-calendar__row-label');
        var rowLabelWidth = rowLabelEl ? rowLabelEl.getBoundingClientRect().width : 150;
        // 150 matches calendar.css { .excel-calendar__row-label { min-width: 150px } } —
        // keep these two values in sync.

        // Vertical nudge: if cell top is within `headerHeight` of container top.
        var topGap = cellRect.top - (containerRect.top + headerHeight);
        if (topGap < 0) {
            container.scrollBy({ top: topGap, behavior: 'auto' });
        }

        // Horizontal nudge: respect writing direction.
        if (isRtl()) {
            // Row label sticks on the RIGHT in RTL; expose cells hidden behind it.
            var rightGap = (containerRect.right - rowLabelWidth) - cellRect.right;
            if (rightGap < 0) {
                container.scrollBy({ left: -rightGap, behavior: 'auto' });
            }
        } else {
            // Row label sticks on the LEFT in LTR; expose cells hidden behind it.
            var leftGap = cellRect.left - (containerRect.left + rowLabelWidth);
            if (leftGap < 0) {
                container.scrollBy({ left: leftGap, behavior: 'auto' });
            }
        }
    }

    // Navigate to the prev/next period; remember which edge cell to focus once the new page loads.
    function crossPeriod(navDir, rowId, edge) {
        var anchor = document.querySelector('[data-cal-nav="' + navDir + '"]');
        if (!anchor || !anchor.getAttribute('href')) return;
        try {
            sessionStorage.setItem(FOCUS_HINT_KEY, JSON.stringify({ rowId: rowId || null, edge: edge }));
        } catch (e) { /* private mode / quota — navigation still works, just no focus restore */ }
        window.location.assign(anchor.href);
    }

    function handleHorizontal(cell, key) {
        var tr = cell.closest('tr[data-row-id]');
        if (!tr) return;
        var cells = rowCells(tr);
        var idx = cells.indexOf(cell);
        if (idx === -1) return;

        // logicalNext = later date (DOM index + 1). In RTL the later date is visually to the LEFT,
        // so ArrowLeft advances and ArrowRight goes back.
        var goNext = isRtl() ? (key === 'ArrowLeft') : (key === 'ArrowRight');
        var targetIdx = goNext ? idx + 1 : idx - 1;

        if (targetIdx >= 0 && targetIdx < cells.length) {
            focusCell(cells[targetIdx]);
            return;
        }

        // Out of range -> cross into the adjacent period, landing on the opposite edge of the row.
        var rowId = tr.getAttribute('data-row-id');
        if (goNext) {
            crossPeriod('next', rowId, 'first'); // next period: focus the earliest-date cell
        } else {
            crossPeriod('prev', rowId, 'last');  // prev period: focus the latest-date cell
        }
    }

    function handleVertical(cell, key) {
        var tr = cell.closest('tr[data-row-id]');
        if (!tr) return;
        var rows = visibleDataRows();
        var rIdx = rows.indexOf(tr);
        if (rIdx === -1) return;

        var targetRow = key === 'ArrowUp' ? rows[rIdx - 1] : rows[rIdx + 1];
        if (!targetRow) return; // stop at the top/bottom row — no period change

        // Land on the same date column in the target row (robust against the row-label offset).
        var date = cell.getAttribute('data-date');
        var target = targetRow.querySelector('.excel-calendar__cell[data-date="' + cssEscape(date) + '"]');
        if (!target) {
            var cells = rowCells(targetRow);
            target = cells.length ? cells[0] : null;
        }
        focusCell(target);
    }

    function onKeydown(e) {
        if (e.altKey || e.ctrlKey || e.metaKey) return; // leave Alt+Arrow to the toolbar period-nav

        var cell = (e.target && e.target.closest) ? e.target.closest(CELL_SELECTOR) : null;
        if (!cell) return;
        // Act only when the cell itself is focused — not an input/button/chip inside it (e.g. quick entry).
        if (e.target !== cell) return;

        switch (e.key) {
            case 'ArrowLeft':
            case 'ArrowRight':
                e.preventDefault();
                handleHorizontal(cell, e.key);
                break;
            case 'ArrowUp':
            case 'ArrowDown':
                e.preventDefault();
                handleVertical(cell, e.key);
                break;
            case 'Enter':
            case ' ': {
                var addBtn = cell.querySelector('.excel-calendar__add-btn');
                if (addBtn) {
                    e.preventDefault();
                    addBtn.click();
                }
                break;
            }
            default:
                break;
        }
    }

    // After a keyboard-initiated period change, focus the requested edge cell of the same row.
    function consumeFocusHint() {
        var raw;
        try { raw = sessionStorage.getItem(FOCUS_HINT_KEY); } catch (e) { return; }
        if (!raw) return;
        try { sessionStorage.removeItem(FOCUS_HINT_KEY); } catch (e) { /* ignore */ }

        var hint;
        try { hint = JSON.parse(raw); } catch (e) { return; }
        if (!hint) return;

        var row = null;
        if (hint.rowId) {
            row = document.querySelector('tr[data-row-id="' + cssEscape(hint.rowId) + '"]');
        }
        if (!row) {
            var rows = visibleDataRows();
            row = rows.length ? rows[0] : null;
        }
        if (!row) return;

        var cells = rowCells(row);
        if (!cells.length) return;
        focusCell(hint.edge === 'last' ? cells[cells.length - 1] : cells[0]);
    }

    document.addEventListener('keydown', onKeydown);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', consumeFocusHint);
    } else {
        consumeFocusHint();
    }
})();
