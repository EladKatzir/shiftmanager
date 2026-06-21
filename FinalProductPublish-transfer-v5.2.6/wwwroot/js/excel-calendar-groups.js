/**
 * Excel Calendar Groups — collapse/expand with localStorage persistence.
 * Handles group header clicks to toggle row visibility within sections.
 */
(function () {
    'use strict';

    const STORAGE_KEY = 'excel-calendar-collapsed-groups';

    function getCollapsedGroups() {
        try {
            return JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
        } catch {
            return {};
        }
    }

    function saveCollapsedGroups(collapsed) {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(collapsed));
        } catch { /* quota exceeded — ignore */ }
    }

    function toggleGroup(groupId, forceState) {
        const collapsed = getCollapsedGroups();
        const isCollapsed = forceState !== undefined ? forceState : !collapsed[groupId];

        if (isCollapsed) {
            collapsed[groupId] = true;
        } else {
            delete collapsed[groupId];
        }
        saveCollapsedGroups(collapsed);

        // Toggle chevron and collapsed band class
        const header = document.querySelector(`.excel-calendar__group-header[data-group-id="${groupId}"]`);
        if (header) {
            const chevron = header.querySelector('.excel-calendar__group-chevron');
            if (chevron) {
                chevron.classList.toggle('excel-calendar__group-chevron--collapsed', isCollapsed);
            }
            // .is-collapsed disables sticky positioning on the band (prevents double-border
            // glitch when the band is the only visible row of its group).
            header.classList.toggle('is-collapsed', isCollapsed);
        }

        // Toggle rows
        const rows = document.querySelectorAll(`tr[data-group-id="${groupId}"]`);
        rows.forEach(row => {
            if (!row.classList.contains('excel-calendar__group-header')) {
                row.style.display = isCollapsed ? 'none' : '';
            }
        });
    }

    // applyPersistedCollapse is idempotent: toggleGroup() calls classList.toggle()
    // with an explicit boolean force-value, which is a no-op when the class is
    // already in the requested state. So this function is safe to call after a
    // full server re-render that already baked .is-collapsed into the HTML.
    // Re-apply persisted collapse state to the current DOM. Extracted so it can run both on load
    // and after an in-place grid refresh (the server re-renders all groups expanded).
    function applyPersistedCollapse() {
        const collapsed = getCollapsedGroups();
        for (const groupId of Object.keys(collapsed)) {
            toggleGroup(groupId, true);
        }
    }

    function initGroups() {
        applyPersistedCollapse();

        // Attach click handlers to group headers (delegated on document — survives grid refresh)
        document.addEventListener('click', function (e) {
            const header = e.target.closest('.excel-calendar__group-header');
            if (!header) return;

            // Don't toggle when clicking editable elements
            if (e.target.closest('[data-editable]') || e.target.closest('input')) return;
            if (e.target.closest('.excel-calendar__group-grip')) return; // grip = drag, not collapse

            const groupId = header.dataset.groupId;
            if (groupId) {
                toggleGroup(groupId);
            }
        });
    }

    // Global collapse/expand all functions
    window.collapseAllGroups = function() {
        var headers = document.querySelectorAll('.excel-calendar__group-header');
        headers.forEach(function(header) {
            var groupId = header.dataset.groupId;
            if (groupId) toggleGroup(groupId, true);
        });
    };

    window.expandAllGroups = function() {
        var headers = document.querySelectorAll('.excel-calendar__group-header');
        headers.forEach(function(header) {
            var groupId = header.dataset.groupId;
            if (groupId) toggleGroup(groupId, false);
        });
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initGroups);
    } else {
        initGroups();
    }

    // After an in-place grid refresh the click handler is still bound (delegated on document), but the
    // freshly server-rendered groups come back expanded — re-apply the user's collapse state.
    document.addEventListener('calendar:grid-refreshed', applyPersistedCollapse);
})();

/**
 * Recompute (visible/total) for each group band based on which child rows
 * are currently visible. Called from applyFilter / clearFilter in
 * Pages/Calendar/Shifts.cshtml (and siblings).
 *
 * - If all rows of a group are visible, show "(total)"
 * - If some rows are hidden, show "(visible/total)"
 */
window.updateGroupCounts = function () {
    const counts = document.querySelectorAll('.excel-calendar__group-count[data-group-total]');
    counts.forEach(function (countEl) {
        const band = countEl.closest('.excel-calendar__group-header');
        if (!band) return;
        const groupId = band.getAttribute('data-group-id');
        const total = parseInt(countEl.getAttribute('data-group-total'), 10);
        const rows = document.querySelectorAll(
            'tr[data-group-id="' + groupId + '"]:not(.excel-calendar__group-header)'
        );
        const visible = Array.prototype.filter.call(rows, function (r) {
            return r.style.display !== 'none' && !r.hasAttribute('hidden');
        }).length;
        countEl.textContent = visible === total ? '(' + total + ')' : '(' + visible + '/' + total + ')';
    });
};
