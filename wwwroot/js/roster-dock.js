/**
 * Roster Dock - Employee Drag-and-Drop Assignment
 * Provides quick employee assignment via drag-and-drop interface with real-time availability status
 */

(function() {
    'use strict';

    // State
    let employees = [];
    let isDragging = false;

    /**
     * Initialize the Roster Dock functionality
     */
    function initRosterDock() {
        const dockToggle = document.getElementById('rosterDockToggle');
        const dock = document.getElementById('rosterDock');
        const closeBtn = dock?.querySelector('.close-roster-btn');
        const searchInput = document.getElementById('rosterSearch');

        if (!dock || !dockToggle) {
            console.warn('[Roster Dock] Elements not found, skipping initialization');
            return;
        }

        // Toggle dock open/close
        dockToggle.addEventListener('click', toggleRosterDock);

        if (closeBtn) {
            closeBtn.addEventListener('click', closeRosterDock);
        }

        // Search functionality
        if (searchInput) {
            searchInput.addEventListener('input', handleSearch);
        }

        // Load employees when dock is opened
        loadEmployees();

    }

    /**
     * Toggle the roster dock open/closed
     */
    window.toggleRosterDock = function() {
        const dock = document.getElementById('rosterDock');
        const toggle = document.getElementById('rosterDockToggle');

        if (!dock) return;

        const isOpen = dock.classList.contains('open');

        if (isOpen) {
            closeRosterDock();
        } else {
            openRosterDock();
        }
    };

    /**
     * Open the roster dock
     */
    function openRosterDock() {
        const dock = document.getElementById('rosterDock');
        const toggle = document.getElementById('rosterDockToggle');

        if (!dock) return;

        dock.classList.add('open');
        if (toggle) {
            toggle.classList.add('active');
        }

        // Load employees if not already loaded
        if (employees.length === 0) {
            loadEmployees();
        }
    }

    /**
     * Close the roster dock
     */
    function closeRosterDock() {
        const dock = document.getElementById('rosterDock');
        const toggle = document.getElementById('rosterDockToggle');

        if (!dock) return;

        dock.classList.remove('open');
        if (toggle) {
            toggle.classList.remove('active');
        }
    }

    /**
     * Load employees from the API with 7-day availability
     */
    async function loadEmployees() {
        const employeeList = document.getElementById('rosterEmployeeList');

        if (!employeeList) {
            console.error('[Roster Dock] Employee list element not found');
            return;
        }

        // Show loading state
        employeeList.innerHTML = '<p class="loading-text">' + (window.AppLocalizer?.Roster_Loading || 'Loading employees...') + '</p>';

        try {
            // Use the EmployeeAvailability endpoint for 7-day availability cubes
            // Forward molecule params from URL if present (for cross-company visibility)
            const urlParams = new URLSearchParams(window.location.search);
            let availUrl = '/Calendar/Table?handler=EmployeeAvailability';
            if (urlParams.get('MoleculeId')) availUrl += '&moleculeId=' + encodeURIComponent(urlParams.get('MoleculeId'));
            if (urlParams.get('JobTypeId')) availUrl += '&jobTypeId=' + encodeURIComponent(urlParams.get('JobTypeId'));
            const response = await fetch(availUrl, {
                method: 'GET',
                credentials: 'same-origin'
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            employees = await response.json();

            renderEmployeeList(employees);
        } catch (error) {
            console.error('[Roster Dock] Error loading employees:', error);
            employeeList.innerHTML = '<p class="error-text">' + (window.AppLocalizer?.Roster_FailedToLoad || 'Failed to load employees. Please try again.') + '</p>';
        }
    }

    /**
     * Render the employee list with 7-day availability cubes
     */
    function renderEmployeeList(employeesToRender) {
        const employeeList = document.getElementById('rosterEmployeeList');

        if (!employeeList) return;

        if (employeesToRender.length === 0) {
            employeeList.innerHTML = '<p class="empty-text">' + (window.AppLocalizer?.Roster_NoEmployees || 'No employees found.') + '</p>';
            return;
        }

        employeeList.innerHTML = employeesToRender.map(emp => {
            const initials = getInitials(emp.name);

            // Build availability cubes HTML
            let cubesHtml = '<div class="availability-cubes">';
            if (emp.availability && emp.availability.length > 0) {
                emp.availability.forEach(day => {
                    cubesHtml += `
                        <div class="availability-cube ${escapeHtml(day.status)}" title="${escapeHtml(day.tooltip)}">
                            <span class="cube-date">${escapeHtml(day.date)}</span>
                            <span class="cube-tooltip">${escapeHtml(day.dayName)}: ${escapeHtml(day.tooltip)}</span>
                        </div>
                    `;
                });
            }
            cubesHtml += '</div>';

            return `
                <div class="roster-employee-item"
                     draggable="true"
                     data-user-id="${emp.id}"
                     data-user-name="${escapeHtml(emp.name)}">
                    <div class="employee-avatar">${escapeHtml(initials)}</div>
                    <div class="employee-info">
                        <div class="employee-name">${escapeHtml(emp.name)}</div>
                        ${cubesHtml}
                    </div>
                </div>
            `;
        }).join('');

        // Attach drag event listeners
        attachDragListeners();
    }

    /**
     * Get employee initials for avatar
     */
    function getInitials(name) {
        if (!name) return '?';

        const parts = name.trim().split(/\s+/);
        if (parts.length >= 2) {
            return (parts[0][0] + parts[1][0]).toUpperCase();
        }
        return name.substring(0, 2).toUpperCase();
    }

    /**
     * Get employee status text
     */
    function getEmployeeStatusKey(emp) {
        if (emp.onVacation) return 'vacation';
        if (emp.hasShift) return 'shift';
        if (emp.hasChore) return 'chore';
        return 'available';
    }

    function getEmployeeStatus(emp) {
        var key = getEmployeeStatusKey(emp);
        var labels = {
            vacation: window.AppLocalizer?.Roster_Vacation || 'Vacation',
            shift: window.AppLocalizer?.Roster_OnShift || 'On Shift',
            chore: window.AppLocalizer?.Roster_HasChore || 'Has Chore',
            available: window.AppLocalizer?.Roster_Available || 'Available'
        };
        return labels[key];
    }

    /**
     * Get CSS class for status key
     */
    function getStatusClass(statusKey) {
        switch (statusKey) {
            case 'vacation': return 'status-vacation';
            case 'shift': return 'status-shift';
            case 'chore': return 'status-chore';
            default: return 'status-available';
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
     * Handle search input
     */
    function handleSearch(event) {
        const query = event.target.value.toLowerCase().trim();

        if (!query) {
            renderEmployeeList(employees);
            return;
        }

        const filtered = employees.filter(emp =>
            emp.name.toLowerCase().includes(query)
        );

        renderEmployeeList(filtered);
    }

    /**
     * Attach drag event listeners to employee items
     */
    function attachDragListeners() {
        const items = document.querySelectorAll('.roster-employee-item');

        items.forEach(item => {
            item.addEventListener('dragstart', handleDragStart);
            item.addEventListener('dragend', handleDragEnd);
        });

        // Attach drop zones
        const dropZones = document.querySelectorAll('.add-assignment-btn, .assignment-slot.unassigned');

        dropZones.forEach(zone => {
            zone.addEventListener('dragover', handleDragOver);
            zone.addEventListener('dragleave', handleDragLeave);
            zone.addEventListener('drop', handleDrop);
        });
    }

    /**
     * Handle drag start
     */
    function handleDragStart(event) {
        isDragging = true;

        const userId = event.currentTarget.dataset.userId;
        const userName = event.currentTarget.dataset.userName;

        event.dataTransfer.effectAllowed = 'copy';
        event.dataTransfer.setData('text/plain', JSON.stringify({ userId, userName }));

        event.currentTarget.classList.add('dragging');

        // Highlight drop zones
        highlightDropZones(true);
    }

    /**
     * Handle drag end
     */
    function handleDragEnd(event) {
        isDragging = false;

        event.currentTarget.classList.remove('dragging');

        // Remove drop zone highlights
        highlightDropZones(false);
    }

    /**
     * Handle drag over drop zone
     */
    function handleDragOver(event) {
        if (!isDragging) return;

        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';

        event.currentTarget.classList.add('drag-over');
    }

    /**
     * Handle drag leave drop zone
     */
    function handleDragLeave(event) {
        event.currentTarget.classList.remove('drag-over');
    }

    /**
     * Handle drop on assignment slot
     */
    async function handleDrop(event) {
        event.preventDefault();
        event.stopPropagation(); // Prevent onclick handler from firing
        event.currentTarget.classList.remove('drag-over');

        if (!isDragging) return;

        try {
            const data = JSON.parse(event.dataTransfer.getData('text/plain'));
            const userId = parseInt(data.userId, 10);

            // Get the assignment ID from the dropped slot
            const slot = event.currentTarget.closest('.assignment-slot');

            if (!slot) {
                console.error('[Roster Dock] Could not find assignment slot');
                return;
            }

            const assignmentId = parseInt(slot.dataset.assignmentId, 10);

            if (!assignmentId) {
                console.error('[Roster Dock] Invalid assignment ID');
                return;
            }

            // Call the API to assign the user
            await assignUserToSlot(assignmentId, userId);

        } catch (error) {
            console.error('[Roster Dock] Error handling drop:', error);
            showToast(window.AppLocalizer?.Roster_FailedToAssign || 'Failed to assign employee', 'error');
        }

        highlightDropZones(false);
    }

    /**
     * Assign user to shift slot via API
     */
    async function assignUserToSlot(assignmentId, userId) {
        try {
            const response = await fetch('/Calendar/Table?handler=AssignUserToSlot', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value
                },
                credentials: 'same-origin',
                body: JSON.stringify({
                    assignmentId,
                    userId
                })
            });

            if (!response.ok) {
                const errorData = await response.json();
                throw new Error(errorData.error || (window.AppLocalizer?.Roster_FailedToAssign || 'Failed to assign user'));
            }

            const result = await response.json();

            if (result.success) {
                showToast(window.AppLocalizer?.Roster_AssignedSuccess || 'Employee assigned successfully', 'success');

                // Refresh calendar data or reload as fallback
                if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { window.location.reload(); }
            } else {
                throw new Error(result.error || (window.AppLocalizer?.Roster_AssignmentFailed || 'Assignment failed'));
            }

        } catch (error) {
            console.error('[Roster Dock] Assignment error:', error);
            showToast(error.message || (window.AppLocalizer?.Roster_FailedToAssign || 'Failed to assign employee'), 'error');
        }
    }

    /**
     * Highlight or remove highlights from drop zones
     */
    function highlightDropZones(highlight) {
        const dropZones = document.querySelectorAll('.add-assignment-btn, .assignment-slot.unassigned');

        dropZones.forEach(zone => {
            if (highlight) {
                zone.classList.add('drop-zone-active');
            } else {
                zone.classList.remove('drop-zone-active', 'drag-over');
            }
        });
    }

    /**
     * Show a toast notification
     */
    function showToast(message, type = 'info') {
        // Use existing toast system if available
        if (window.showToast) {
            window.showToast(message, type);
            return;
        }

        // Fallback: console log
        console.log(`[Toast ${type}]`, message);
        alert(message);
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initRosterDock);
    } else {
        initRosterDock();
    }

})();
