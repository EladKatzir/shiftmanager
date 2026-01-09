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

        console.log('[Roster Dock] Initialized');
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
     * Load employees from the API
     */
    async function loadEmployees() {
        const employeeList = document.getElementById('rosterEmployeeList');

        if (!employeeList) {
            console.error('[Roster Dock] Employee list element not found');
            return;
        }

        // Show loading state
        employeeList.innerHTML = '<p class="loading-text">Loading employees...</p>';

        try {
            const response = await fetch('/Calendar/Table?handler=GetRosterEmployees', {
                method: 'GET',
                credentials: 'same-origin'
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const data = await response.json();
            employees = data.employees || [];

            renderEmployeeList(employees);
        } catch (error) {
            console.error('[Roster Dock] Error loading employees:', error);
            employeeList.innerHTML = '<p class="error-text">Failed to load employees. Please try again.</p>';
        }
    }

    /**
     * Render the employee list
     */
    function renderEmployeeList(employeesToRender) {
        const employeeList = document.getElementById('rosterEmployeeList');

        if (!employeeList) return;

        if (employeesToRender.length === 0) {
            employeeList.innerHTML = '<p class="empty-text">No employees found.</p>';
            return;
        }

        employeeList.innerHTML = employeesToRender.map(emp => {
            const initials = getInitials(emp.name);
            const status = getEmployeeStatus(emp);
            const statusClass = getStatusClass(status);

            return `
                <div class="roster-employee-item"
                     draggable="true"
                     data-user-id="${emp.id}"
                     data-user-name="${emp.name}">
                    <div class="employee-avatar">${initials}</div>
                    <div class="employee-info">
                        <div class="employee-name">${escapeHtml(emp.name)}</div>
                        <div class="employee-status ${statusClass}">${status}</div>
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
    function getEmployeeStatus(emp) {
        if (emp.onVacation) return 'Vacation';
        if (emp.hasShift) return 'On Shift';
        if (emp.hasChore) return 'Has Chore';
        return 'Available';
    }

    /**
     * Get CSS class for status
     */
    function getStatusClass(status) {
        switch (status) {
            case 'Vacation': return 'status-vacation';
            case 'On Shift': return 'status-shift';
            case 'Has Chore': return 'status-chore';
            default: return 'status-available';
        }
    }

    /**
     * Escape HTML to prevent XSS
     */
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
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
        const dropZones = document.querySelectorAll('.add-assignment-btn, .assignment-slot-empty');

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
        event.currentTarget.classList.remove('drag-over');

        if (!isDragging) return;

        try {
            const data = JSON.parse(event.dataTransfer.getData('text/plain'));
            const userId = parseInt(data.userId, 10);

            // Find the shift instance ID from the cell
            const cell = event.currentTarget.closest('.assignment-cell');
            if (!cell) {
                console.error('[Roster Dock] Could not find assignment cell');
                return;
            }

            const shiftInstanceId = parseInt(cell.dataset.instanceId, 10);

            if (!shiftInstanceId) {
                console.error('[Roster Dock] Invalid shift instance ID');
                return;
            }

            // Find the assignment slot index
            const slot = event.currentTarget.closest('.assignment-slot');
            let slotIndex = 0;

            if (slot) {
                const allSlots = cell.querySelectorAll('.assignment-slot');
                slotIndex = Array.from(allSlots).indexOf(slot);
            }

            // Call the API to assign the user
            await assignUserToSlot(shiftInstanceId, userId, slotIndex);

        } catch (error) {
            console.error('[Roster Dock] Error handling drop:', error);
            showToast('Failed to assign employee', 'error');
        }

        highlightDropZones(false);
    }

    /**
     * Assign user to shift slot via API
     */
    async function assignUserToSlot(shiftInstanceId, userId, slotIndex) {
        try {
            const response = await fetch('/Calendar/Table?handler=AssignUserToSlot', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value
                },
                credentials: 'same-origin',
                body: JSON.stringify({
                    shiftInstanceId,
                    userId,
                    slotIndex
                })
            });

            if (!response.ok) {
                const errorData = await response.json();
                throw new Error(errorData.error || 'Failed to assign user');
            }

            const result = await response.json();

            if (result.success) {
                showToast('Employee assigned successfully', 'success');

                // Reload the page to show the updated assignment
                window.location.reload();
            } else {
                throw new Error(result.error || 'Assignment failed');
            }

        } catch (error) {
            console.error('[Roster Dock] Assignment error:', error);
            showToast(error.message || 'Failed to assign employee', 'error');
        }
    }

    /**
     * Highlight or remove highlights from drop zones
     */
    function highlightDropZones(highlight) {
        const dropZones = document.querySelectorAll('.add-assignment-btn, .assignment-slot-empty');

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
