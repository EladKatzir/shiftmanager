/**
 * Calendar Radar Mode - Conflict Detection Overlay
 * Highlights understaffed and overstaffed shifts in the calendar table
 */

(function() {
    'use strict';

    // State
    let radarModeActive = false;
    let conflicts = [];
    let refreshInterval = null;

    /**
     * Initialize Radar Mode functionality
     */
    function initRadarMode() {
        const radarToggle = document.getElementById('radarToggle');

        if (!radarToggle) {
            console.warn('[Radar Mode] Toggle button not found, skipping initialization');
            return;
        }

        // Already has onclick="toggleRadarMode()" in HTML, so we just need to define the function
        console.log('[Radar Mode] Initialized');
    }

    /**
     * Toggle Radar Mode on/off (called from HTML onclick)
     */
    window.toggleRadarMode = async function() {
        radarModeActive = !radarModeActive;

        const radarBtn = document.getElementById('radarToggle');

        if (radarModeActive) {
            // Enable Radar Mode
            if (radarBtn) {
                radarBtn.classList.add('active');
                radarBtn.textContent = '📡 ' + (window.AppLocalizer?.Radar_Active || 'Radar (Active)');
            }

            await loadConflicts();
            applyConflictHighlights();

            // Refresh conflicts every 30 seconds
            refreshInterval = setInterval(async () => {
                await loadConflicts();
                applyConflictHighlights();
            }, 30000);

            console.log('[Radar Mode] Activated');
        } else {
            // Disable Radar Mode
            if (radarBtn) {
                radarBtn.classList.remove('active');
                radarBtn.textContent = '📡 ' + (window.AppLocalizer?.Radar || 'Radar');
            }

            clearConflictHighlights();

            if (refreshInterval) {
                clearInterval(refreshInterval);
                refreshInterval = null;
            }

            console.log('[Radar Mode] Deactivated');
        }
    };

    /**
     * Load conflicts from the server
     */
    async function loadConflicts() {
        try {
            // Get current view parameters from URL
            const urlParams = new URLSearchParams(window.location.search);
            const start = urlParams.get('start') || '';
            const view = urlParams.get('view') || 'week';

            // Build API URL with parameters
            const apiUrl = `/Calendar/Table?handler=GetConflicts&start=${encodeURIComponent(start)}&view=${encodeURIComponent(view)}`;

            const response = await fetch(apiUrl, {
                method: 'GET',
                credentials: 'same-origin'
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const data = await response.json();
            conflicts = data.conflicts || [];

            console.log(`[Radar Mode] Loaded ${conflicts.length} conflicts`);

        } catch (error) {
            console.error('[Radar Mode] Error loading conflicts:', error);
            showToast(window.AppLocalizer?.Radar_FailedToLoad || 'Failed to load conflict data', 'error');
        }
    }

    /**
     * Apply conflict highlights to the calendar cells
     */
    function applyConflictHighlights() {
        // First, clear any existing highlights
        clearConflictHighlights();

        if (conflicts.length === 0) {
            console.log('[Radar Mode] No conflicts to display');
            return;
        }

        conflicts.forEach(conflict => {
            const cell = findCellByConflict(conflict);

            if (!cell) {
                console.warn('[Radar Mode] Could not find cell for conflict:', conflict);
                return;
            }

            applyConflictStyle(cell, conflict);
        });

        console.log(`[Radar Mode] Applied highlights to ${conflicts.length} cells`);
    }

    /**
     * Find the calendar cell matching a conflict
     */
    function findCellByConflict(conflict) {
        // Find cell by data attributes
        const cells = document.querySelectorAll('.assignment-cell');

        for (const cell of cells) {
            const instanceId = parseInt(cell.dataset.instanceId, 10);
            const cellDate = cell.dataset.date;
            const shiftTypeId = parseInt(cell.dataset.shiftType, 10);

            // Match by instance ID (most reliable)
            if (instanceId === conflict.instanceId) {
                return cell;
            }

            // Fallback: match by date and shift type
            if (cellDate === conflict.date && shiftTypeId === conflict.shiftTypeId) {
                return cell;
            }
        }

        return null;
    }

    /**
     * Apply conflict styling to a cell
     */
    function applyConflictStyle(cell, conflict) {
        const conflictType = conflict.type;

        // Add conflict class
        cell.classList.add('radar-conflict', `radar-${conflictType}`);

        // Add or update conflict badge
        let badge = cell.querySelector('.conflict-badge');

        if (!badge) {
            badge = document.createElement('div');
            badge.className = 'conflict-badge';
            cell.appendChild(badge);
        }

        // Set badge content based on conflict type
        switch (conflictType) {
            case 'underfilled':
                badge.textContent = `⚠️ ${conflict.filled}/${conflict.required}`;
                badge.title = `Understaffed: ${conflict.required - conflict.filled} needed`;
                badge.classList.add('badge-warning');
                break;

            case 'overfilled':
                badge.textContent = `⚠️ ${conflict.filled}/${conflict.required}`;
                badge.title = `Overstaffed: ${conflict.filled - conflict.required} extra`;
                badge.classList.add('badge-danger');
                break;

            default:
                badge.textContent = '⚠️';
                badge.title = window.AppLocalizer?.Radar_ConflictDetected || 'Conflict detected';
                badge.classList.add('badge-warning');
        }

        // Add tooltip with details
        addConflictTooltip(cell, conflict);
    }

    /**
     * Add a tooltip with conflict details
     */
    function addConflictTooltip(cell, conflict) {
        // Remove existing tooltip if any
        const existingTooltip = cell.querySelector('.conflict-tooltip');
        if (existingTooltip) {
            existingTooltip.remove();
        }

        // Create tooltip element
        const tooltip = document.createElement('div');
        tooltip.className = 'conflict-tooltip';

        let content = `
            <div class="tooltip-header">${getConflictTitle(conflict)}</div>
            <div class="tooltip-body">
                <p><strong>Shift:</strong> ${escapeHtml(conflict.shiftTypeName)}</p>
                <p><strong>Date:</strong> ${formatDate(conflict.date)}</p>
                <p><strong>Staffing:</strong> ${conflict.filled} / ${conflict.required}</p>
        `;

        if (conflict.type === 'underfilled') {
            const needed = conflict.required - conflict.filled;
            content += `<p><strong>Needed:</strong> ${needed} more employee${needed !== 1 ? 's' : ''}</p>`;
        } else if (conflict.type === 'overfilled') {
            const extra = conflict.filled - conflict.required;
            content += `<p><strong>Extra:</strong> ${extra} employee${extra !== 1 ? 's' : ''}</p>`;
        }

        content += `</div>`;

        tooltip.innerHTML = content;
        cell.appendChild(tooltip);

        // Show tooltip on hover
        cell.addEventListener('mouseenter', () => {
            tooltip.classList.add('show');
        });

        cell.addEventListener('mouseleave', () => {
            tooltip.classList.remove('show');
        });
    }

    /**
     * Get conflict title
     */
    function getConflictTitle(conflict) {
        switch (conflict.type) {
            case 'underfilled':
                return '⚠️ Understaffed';
            case 'overfilled':
                return '⚠️ Overstaffed';
            default:
                return '⚠️ Conflict';
        }
    }

    /**
     * Clear all conflict highlights
     */
    function clearConflictHighlights() {
        // Remove conflict classes
        const cells = document.querySelectorAll('.radar-conflict');
        cells.forEach(cell => {
            cell.classList.remove('radar-conflict', 'radar-underfilled', 'radar-overfilled');
        });

        // Remove conflict badges
        const badges = document.querySelectorAll('.conflict-badge');
        badges.forEach(badge => badge.remove());

        // Remove tooltips
        const tooltips = document.querySelectorAll('.conflict-tooltip');
        tooltips.forEach(tooltip => tooltip.remove());
    }

    /**
     * Format date for display
     */
    function formatDate(dateString) {
        try {
            const date = new Date(dateString);
            return date.toLocaleDateString('en-US', {
                weekday: 'short',
                month: 'short',
                day: 'numeric'
            });
        } catch {
            return dateString;
        }
    }

    /**
     * Escape HTML to prevent XSS
     */
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML.replace(/'/g, '&#39;');
    }

    /**
     * Show a toast notification
     */
    function showToast(message, type = 'info') {
        if (window.showToast) {
            window.showToast(message, type);
            return;
        }

        console.log(`[Toast ${type}]`, message);
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initRadarMode);
    } else {
        initRadarMode();
    }

    // Clean up on page unload
    window.addEventListener('beforeunload', () => {
        if (refreshInterval) {
            clearInterval(refreshInterval);
        }
    });

})();
