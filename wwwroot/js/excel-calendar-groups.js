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

        // Toggle chevron
        const header = document.querySelector(`.excel-calendar__group-header[data-group-id="${groupId}"]`);
        if (header) {
            const chevron = header.querySelector('.excel-calendar__group-chevron');
            if (chevron) {
                chevron.classList.toggle('excel-calendar__group-chevron--collapsed', isCollapsed);
            }
        }

        // Toggle rows
        const rows = document.querySelectorAll(`tr[data-group-id="${groupId}"]`);
        rows.forEach(row => {
            if (!row.classList.contains('excel-calendar__group-header')) {
                row.style.display = isCollapsed ? 'none' : '';
            }
        });
    }

    function initGroups() {
        const collapsed = getCollapsedGroups();

        // Apply persisted collapse state on load
        for (const groupId of Object.keys(collapsed)) {
            toggleGroup(groupId, true);
        }

        // Attach click handlers to group headers
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
})();
