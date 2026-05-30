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
