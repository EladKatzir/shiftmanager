/**
 * Calendar Bottom Sheet - Mobile Touch Assignment UI (QA Item 33)
 *
 * Provides a touch-friendly bottom-sheet modal for assigning/editing calendar items
 * on mobile and touch devices. Replaces the desktop inline-edit and fill-handle
 * interactions which are mouse-centric and unsuitable for touch.
 *
 * Features:
 * - Slide-up bottom sheet triggered by tapping a calendar cell on touch devices
 * - Shows current assignment, allows selecting a new user, removing assignment
 * - Swipe-down gesture to dismiss
 * - WCAG 2.5.5 compliant touch targets (minimum 44x44px)
 * - Respects prefers-reduced-motion
 * - RTL/LTR aware
 * - Integrates with existing quickAddChore, quickAddOnDuty, deleteItem functions
 */
(function () {
    'use strict';

    // --- Configuration ---
    var SWIPE_THRESHOLD = 80; // px to swipe down before dismissing
    var ANIMATION_DURATION = 300; // ms

    // --- State ---
    var sheetElement = null;
    var backdropElement = null;
    var contentElement = null;
    var isOpen = false;
    var touchStartY = 0;
    var touchCurrentY = 0;
    var isDragging = false;
    var currentCellData = null;

    // --- Localization helpers ---
    function loc(key, fallback) {
        if (window.AppLocalizer && window.AppLocalizer[key]) {
            return window.AppLocalizer[key];
        }
        return fallback || key;
    }

    function isHebrew() {
        var lang = document.documentElement.lang || 'en';
        return lang.startsWith('he');
    }

    // --- Touch detection ---
    function isTouchDevice() {
        return ('ontouchstart' in window) ||
               (navigator.maxTouchPoints > 0) ||
               (window.matchMedia && window.matchMedia('(pointer: coarse)').matches);
    }

    // --- Reduced motion ---
    function prefersReducedMotion() {
        return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    // --- DOM Creation ---
    function createSheet() {
        if (sheetElement) return;

        // Backdrop
        backdropElement = document.createElement('div');
        backdropElement.className = 'bottom-sheet__backdrop';
        backdropElement.setAttribute('aria-hidden', 'true');
        backdropElement.addEventListener('click', close);

        // Sheet container
        sheetElement = document.createElement('div');
        sheetElement.className = 'bottom-sheet';
        sheetElement.setAttribute('role', 'dialog');
        sheetElement.setAttribute('aria-modal', 'true');
        sheetElement.setAttribute('aria-labelledby', 'bottom-sheet-title');
        sheetElement.setAttribute('hidden', '');

        // Content wrapper
        contentElement = document.createElement('div');
        contentElement.className = 'bottom-sheet__content';

        // Drag handle
        var handle = document.createElement('div');
        handle.className = 'bottom-sheet__handle';
        handle.setAttribute('aria-hidden', 'true');
        var handleBar = document.createElement('div');
        handleBar.className = 'bottom-sheet__handle-bar';
        handle.appendChild(handleBar);

        // Close button (accessibility - keyboard users)
        var closeBtn = document.createElement('button');
        closeBtn.className = 'bottom-sheet__close-btn';
        closeBtn.type = 'button';
        closeBtn.setAttribute('aria-label', loc('Close', 'Close'));
        closeBtn.innerHTML = '&times;';
        closeBtn.addEventListener('click', close);

        // Title
        var title = document.createElement('h3');
        title.className = 'bottom-sheet__title';
        title.id = 'bottom-sheet-title';

        // Body (will be populated dynamically)
        var body = document.createElement('div');
        body.className = 'bottom-sheet__body';

        // Actions
        var actions = document.createElement('div');
        actions.className = 'bottom-sheet__actions';

        // Assemble
        contentElement.appendChild(handle);
        contentElement.appendChild(closeBtn);
        contentElement.appendChild(title);
        contentElement.appendChild(body);
        contentElement.appendChild(actions);
        sheetElement.appendChild(backdropElement);
        sheetElement.appendChild(contentElement);

        document.body.appendChild(sheetElement);

        // Touch gesture events on the content area
        contentElement.addEventListener('touchstart', onTouchStart, { passive: true });
        contentElement.addEventListener('touchmove', onTouchMove, { passive: false });
        contentElement.addEventListener('touchend', onTouchEnd, { passive: true });

        // Keyboard: Escape to close
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && isOpen) {
                close();
            }
        });
    }

    // --- Touch gesture handlers for swipe-to-dismiss ---
    function onTouchStart(e) {
        if (!isOpen) return;
        var touch = e.touches[0];
        touchStartY = touch.clientY;
        touchCurrentY = touch.clientY;
        isDragging = true;
        contentElement.style.transition = 'none';
    }

    function onTouchMove(e) {
        if (!isDragging || !isOpen) return;
        var touch = e.touches[0];
        touchCurrentY = touch.clientY;
        var deltaY = touchCurrentY - touchStartY;

        // Only allow dragging downward
        if (deltaY > 0) {
            e.preventDefault();
            contentElement.style.transform = 'translateY(' + deltaY + 'px)';
            // Fade backdrop proportionally
            var opacity = Math.max(0, 1 - (deltaY / 300));
            backdropElement.style.opacity = opacity;
        }
    }

    function onTouchEnd() {
        if (!isDragging || !isOpen) return;
        isDragging = false;

        var deltaY = touchCurrentY - touchStartY;

        if (deltaY > SWIPE_THRESHOLD) {
            // Dismiss
            close();
        } else {
            // Snap back
            var duration = prefersReducedMotion() ? '0ms' : ANIMATION_DURATION + 'ms';
            contentElement.style.transition = 'transform ' + duration + ' cubic-bezier(0.2, 0, 0, 1)';
            contentElement.style.transform = 'translateY(0)';
            backdropElement.style.opacity = '';
        }
    }

    // --- Open the sheet with cell data ---
    function open(cellData) {
        createSheet();
        currentCellData = cellData;

        populateContent(cellData);

        sheetElement.removeAttribute('hidden');
        document.body.classList.add('bottom-sheet-open');

        // Trigger animation
        requestAnimationFrame(function () {
            sheetElement.classList.add('bottom-sheet--open');
            isOpen = true;

            // Focus management for accessibility
            var firstFocusable = contentElement.querySelector('button, select, input, [tabindex="0"]');
            if (firstFocusable) {
                firstFocusable.focus();
            }
        });
    }

    // --- Close the sheet ---
    function close() {
        if (!isOpen) return;
        isOpen = false;

        sheetElement.classList.remove('bottom-sheet--open');
        sheetElement.classList.add('bottom-sheet--closing');

        var duration = prefersReducedMotion() ? 0 : ANIMATION_DURATION;

        setTimeout(function () {
            sheetElement.classList.remove('bottom-sheet--closing');
            sheetElement.setAttribute('hidden', '');
            document.body.classList.remove('bottom-sheet-open');
            contentElement.style.transform = '';
            contentElement.style.transition = '';
            backdropElement.style.opacity = '';
            currentCellData = null;
        }, duration);
    }

    // --- Populate sheet content based on cell data ---
    function populateContent(cellData) {
        var titleEl = contentElement.querySelector('.bottom-sheet__title');
        var bodyEl = contentElement.querySelector('.bottom-sheet__body');
        var actionsEl = contentElement.querySelector('.bottom-sheet__actions');

        // Clear previous content
        bodyEl.innerHTML = '';
        actionsEl.innerHTML = '';

        // Set title
        var dateStr = cellData.date || '';
        var rowLabel = cellData.rowLabel || '';
        titleEl.textContent = rowLabel + (dateStr ? ' — ' + formatDate(dateStr) : '');

        // Current assignments section
        if (cellData.assignments && cellData.assignments.length > 0) {
            var currentSection = document.createElement('div');
            currentSection.className = 'bottom-sheet__section';

            var sectionTitle = document.createElement('h4');
            sectionTitle.className = 'bottom-sheet__section-title';
            sectionTitle.textContent = isHebrew() ? 'שיבוצים נוכחיים' : 'Current Assignments';
            currentSection.appendChild(sectionTitle);

            var list = document.createElement('div');
            list.className = 'bottom-sheet__assignment-list';

            cellData.assignments.forEach(function (assignment) {
                var item = document.createElement('div');
                item.className = 'bottom-sheet__assignment-item';

                var nameSpan = document.createElement('span');
                nameSpan.className = 'bottom-sheet__assignment-name';
                nameSpan.textContent = assignment.name;
                item.appendChild(nameSpan);

                if (assignment.isTrainee) {
                    var badge = document.createElement('span');
                    badge.className = 'bottom-sheet__trainee-badge';
                    badge.textContent = isHebrew() ? 'מתמחה' : 'Trainee';
                    item.appendChild(badge);
                }

                // Remove button (only if not read-only)
                if (!cellData.isReadOnly && assignment.id) {
                    var removeBtn = document.createElement('button');
                    removeBtn.type = 'button';
                    removeBtn.className = 'bottom-sheet__remove-btn';
                    removeBtn.setAttribute('aria-label', (isHebrew() ? 'הסר ' : 'Remove ') + assignment.name);
                    removeBtn.innerHTML = '&times;';
                    removeBtn.addEventListener('click', function () {
                        handleRemoveAssignment(cellData, assignment);
                    });
                    item.appendChild(removeBtn);
                }

                list.appendChild(item);
            });

            currentSection.appendChild(list);
            bodyEl.appendChild(currentSection);
        } else {
            // Empty state
            var emptyEl = document.createElement('div');
            emptyEl.className = 'bottom-sheet__empty-state';
            emptyEl.textContent = isHebrew() ? 'אין שיבוצים עדיין' : 'No assignments yet';
            bodyEl.appendChild(emptyEl);
        }

        // Add assignment section (only if not read-only)
        if (!cellData.isReadOnly) {
            var addSection = document.createElement('div');
            addSection.className = 'bottom-sheet__section';

            var addTitle = document.createElement('h4');
            addTitle.className = 'bottom-sheet__section-title';
            addTitle.textContent = isHebrew() ? 'הוסף שיבוץ' : 'Add Assignment';
            addSection.appendChild(addTitle);

            // User selector
            var fieldGroup = document.createElement('div');
            fieldGroup.className = 'bottom-sheet__field';

            var selectLabel = document.createElement('label');
            selectLabel.className = 'bottom-sheet__field-label';
            selectLabel.setAttribute('for', 'bottom-sheet-user-select');

            // Detect if dropdown shows shift types (user-mode) or users (shift-mode)
            var hiddenSelect = document.querySelector('[data-role="assignee-select"]');
            var itemType = hiddenSelect ? (hiddenSelect.dataset.itemType || 'user') : 'user';
            if (itemType === 'shifttype') {
                selectLabel.textContent = isHebrew() ? 'בחר משמרת' : 'Select Shift';
            } else {
                selectLabel.textContent = isHebrew() ? 'בחר משתמש' : 'Select User';
            }
            fieldGroup.appendChild(selectLabel);

            var userSelect = document.createElement('select');
            userSelect.className = 'bottom-sheet__select';
            userSelect.id = 'bottom-sheet-user-select';

            // Default empty option
            var defaultOpt = document.createElement('option');
            defaultOpt.value = '';
            defaultOpt.textContent = isHebrew() ? '-- בחר --' : '-- Select --';
            userSelect.appendChild(defaultOpt);

            // Populate from available users (read from page data or cell context)
            var availableUsers = getAvailableUsers(cellData);
            availableUsers.forEach(function (user) {
                var opt = document.createElement('option');
                opt.value = user.id;
                opt.textContent = user.name;
                userSelect.appendChild(opt);
            });

            fieldGroup.appendChild(userSelect);
            addSection.appendChild(fieldGroup);
            bodyEl.appendChild(addSection);

            // Action buttons
            var assignBtn = document.createElement('button');
            assignBtn.type = 'button';
            assignBtn.className = 'btn btn-primary bottom-sheet__action-btn';
            assignBtn.textContent = isHebrew() ? 'שבץ' : 'Assign';
            assignBtn.addEventListener('click', function () {
                handleAssign(cellData, userSelect.value);
            });
            actionsEl.appendChild(assignBtn);
        }

        // Cancel button
        var cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'btn btn-ghost bottom-sheet__action-btn';
        cancelBtn.textContent = isHebrew() ? 'סגור' : 'Close';
        cancelBtn.addEventListener('click', close);
        actionsEl.appendChild(cancelBtn);
    }

    // --- Get available users for assignment ---
    function getAvailableUsers(cellData) {
        // Try to get users from the assignee selects already on the page
        // (these are populated by the server in quick-add forms)
        var users = [];

        // Strategy 1: look for an existing assignee select on the page
        var existingSelects = document.querySelectorAll('[id^="assigneeSelect-"], select[data-role="assignee-select"]');
        if (existingSelects.length > 0) {
            var select = existingSelects[0];
            for (var i = 0; i < select.options.length; i++) {
                var option = select.options[i];
                if (option.value) {
                    users.push({ id: option.value, name: option.textContent });
                }
            }
        }

        // Strategy 2: look for a global user list
        if (users.length === 0 && window.CalendarUsers) {
            users = window.CalendarUsers.map(function (u) {
                return { id: u.id || u.Id, name: u.name || u.Name || u.displayName || u.DisplayName };
            });
        }

        // Strategy 3: extract from existing assignment cells on the page
        if (users.length === 0) {
            var seenNames = {};
            document.querySelectorAll('.excel-calendar__assignment-name').forEach(function (el) {
                var name = el.textContent.trim();
                if (name && !seenNames[name]) {
                    seenNames[name] = true;
                    // We don't have IDs in this case, so this is view-only
                }
            });
        }

        return users;
    }

    // --- Handle assign action ---
    function handleAssign(cellData, userId) {
        if (!userId) {
            var msg = isHebrew() ? 'נא לבחור משתמש' : 'Please select a user';
            if (window.showToast) {
                window.showToast(msg, 'error');
            } else {
                alert(msg);
            }
            return;
        }

        var calendarType = detectCalendarType();

        if (calendarType === 'chores' && typeof window.quickAddChore === 'function') {
            // For chores, we need a title - prompt for it
            var title = prompt(isHebrew() ? 'כותרת התורנות:' : 'Chore title:');
            if (!title || !title.trim()) return;
            // Pick up ChoreTypeId from the page filter dropdown (if set)
            var choreTypeSelect = document.getElementById('choreTypeSelect');
            var choreTypeId = choreTypeSelect ? (choreTypeSelect.value || null) : null;
            window.quickAddChore(cellData.date, userId, title.trim(), false, choreTypeId);
            close();
        } else if (calendarType === 'oncall' && typeof window.quickAddOnDuty === 'function') {
            // Extract duty type from row ID (format: "dutytype-{typeValue}")
            var onDutyType = 0;
            if (cellData.rowId && cellData.rowId.indexOf('dutytype-') === 0) {
                onDutyType = parseInt(cellData.rowId.replace('dutytype-', ''), 10) || 0;
            }
            window.quickAddOnDuty(cellData.date, userId, onDutyType);
            close();
        } else if (calendarType === 'shifts' && typeof window.quickAddShift === 'function') {
            // Detect mode from row ID prefix
            if (cellData.rowId && cellData.rowId.indexOf('user-') === 0) {
                // User-mode: row is a user, dropdown value is the shift type
                var userIdFromRow = cellData.rowId.replace('user-', '');
                var shiftTypeFromSelect = userId; // "userId" here is actually the selected shift type ID
                if (shiftTypeFromSelect && userIdFromRow) {
                    window.quickAddShift(shiftTypeFromSelect, cellData.date, userIdFromRow);
                    close();
                } else {
                    var noShiftMsg = isHebrew() ? 'נא לבחור משמרת' : 'Please select a shift';
                    if (window.showToast) window.showToast(noShiftMsg, 'error');
                }
            } else if (cellData.rowId && cellData.rowId.indexOf('shift-') === 0) {
                // Shift-mode: row is a shift type, dropdown value is the user
                var shiftTypeId = cellData.rowId.replace('shift-', '');
                window.quickAddShift(shiftTypeId, cellData.date, userId);
                close();
            } else {
                var noActionMsg = isHebrew() ? 'לא ניתן להוסיף שיבוץ כאן' : 'Cannot add assignment here';
                if (window.showToast) {
                    window.showToast(noActionMsg, 'error');
                }
            }
        } else {
            var noActionMsg = isHebrew() ? 'לא ניתן להוסיף שיבוץ כאן' : 'Cannot add assignment here';
            if (window.showToast) {
                window.showToast(noActionMsg, 'error');
            }
        }
    }

    // --- Handle remove assignment ---
    function handleRemoveAssignment(cellData, assignment) {
        var confirmMsg = isHebrew()
            ? 'האם למחוק את השיבוץ של ' + assignment.name + '?'
            : 'Remove assignment for ' + assignment.name + '?';

        if (!confirm(confirmMsg)) return;

        var calendarType = detectCalendarType();

        if (calendarType === 'chores' && typeof window.deleteItem === 'function') {
            window.deleteItem('chore', assignment.id);
            close();
        } else if (calendarType === 'oncall' && typeof window.deleteItem === 'function') {
            window.deleteItem('onduty', assignment.id);
            close();
        } else {
            // Generic removal - try to find and click the delete button
            var assignmentEl = document.querySelector('[data-assignment-id="' + assignment.id + '"]');
            if (assignmentEl) {
                var deleteBtn = assignmentEl.querySelector('.btn-delete-item, [data-action="delete"]');
                if (deleteBtn) {
                    close();
                    deleteBtn.click();
                    return;
                }
            }
            var noRemoveMsg = isHebrew() ? 'לא ניתן למחוק שיבוץ כאן' : 'Cannot remove assignment here';
            if (window.showToast) {
                window.showToast(noRemoveMsg, 'error');
            }
        }
    }

    // --- Detect which calendar type we're on ---
    function detectCalendarType() {
        var calendarEl = document.querySelector('[data-calendar-type]');
        if (calendarEl) {
            return calendarEl.dataset.calendarType;
        }
        // Fallback: check URL
        var path = window.location.pathname.toLowerCase();
        if (path.includes('/chores')) return 'chores';
        if (path.includes('/oncall')) return 'oncall';
        if (path.includes('/overview')) return 'overview';
        return 'shifts';
    }

    // --- Format a date string for display ---
    function formatDate(dateStr) {
        try {
            var date = new Date(dateStr);
            var locale = isHebrew() ? 'he-IL' : 'en-US';
            return date.toLocaleDateString(locale, {
                weekday: 'short',
                month: 'short',
                day: 'numeric'
            });
        } catch (e) {
            return dateStr;
        }
    }

    // --- Extract cell data from a clicked cell element ---
    function extractCellData(cellEl) {
        var rowEl = cellEl.closest('tr');
        var rowId = cellEl.dataset.rowId || (rowEl ? rowEl.dataset.rowId : '');
        var date = cellEl.dataset.date || '';
        var rowLabel = '';
        if (rowEl) {
            var labelEl = rowEl.querySelector('.excel-calendar__row-label');
            if (labelEl) {
                rowLabel = labelEl.textContent.trim();
            }
        }

        var isReadOnly = cellEl.classList.contains('excel-calendar__cell--readonly');

        // Extract existing assignments
        var assignments = [];
        cellEl.querySelectorAll('.excel-calendar__assignment').forEach(function (assignEl) {
            var nameEl = assignEl.querySelector('.excel-calendar__assignment-name');
            var assignmentId = assignEl.dataset.assignmentId;
            var isTrainee = !!assignEl.querySelector('.excel-calendar__badge--trainee');

            assignments.push({
                id: assignmentId ? parseInt(assignmentId, 10) : null,
                name: nameEl ? nameEl.textContent.trim() : '',
                isTrainee: isTrainee
            });
        });

        // Check for overlay badges
        var hasVacation = !!cellEl.querySelector('.excel-calendar__badge--vacation');
        var hasChore = !!cellEl.querySelector('.excel-calendar__badge--chore');
        var hasOnDuty = !!cellEl.querySelector('.excel-calendar__badge--onduty');

        // Extract note
        var noteEl = cellEl.querySelector('.excel-calendar__note');
        var note = noteEl ? noteEl.textContent.trim() : '';

        return {
            rowId: rowId,
            date: date,
            rowLabel: rowLabel,
            isReadOnly: isReadOnly,
            assignments: assignments,
            hasVacation: hasVacation,
            hasChore: hasChore,
            hasOnDuty: hasOnDuty,
            note: note
        };
    }

    // --- Initialize ---
    function init() {
        // Only activate on touch devices
        if (!isTouchDevice()) {
            console.log('[BottomSheet] Not a touch device, skipping initialization');
            return;
        }

        console.log('[BottomSheet] Initializing for touch device');

        // Listen for taps on calendar cells
        document.addEventListener('click', function (e) {
            // Only respond to taps on excel calendar cells
            var cell = e.target.closest('.excel-calendar__cell');
            if (!cell) return;

            // Don't trigger if tapping the add button directly (let it handle itself)
            if (e.target.closest('.excel-calendar__add-btn')) return;

            // Don't trigger on assignment elements that have their own handlers
            if (e.target.closest('.excel-calendar__assignment[onclick]')) return;

            // Extract data and open sheet
            var cellData = extractCellData(cell);
            if (cellData.date) {
                e.preventDefault();
                e.stopPropagation();
                open(cellData);
            }
        }, true);

        // Also intercept taps on month/week/day view calendar cells
        document.addEventListener('click', function (e) {
            var shiftBadge = e.target.closest('.shift-badge');
            var calendarCell = e.target.closest('.calendar-cell');
            var calendarItem = e.target.closest('.calendar-item');

            if (shiftBadge || calendarItem) {
                // Let existing handlers work for shift badges and items
                return;
            }

            if (calendarCell && calendarCell.dataset.date) {
                // Only open bottom sheet if we're on a page with editable calendar cells
                if (document.querySelector('.excel-calendar')) return; // excel calendar handled above
            }
        });

        console.log('[BottomSheet] Initialized');
    }

    // --- Handle add-btn clicks (desktop + touch) ---
    function initAddButtonHandler() {
        document.addEventListener('click', function (e) {
            var addBtn = e.target.closest('.excel-calendar__add-btn');
            if (!addBtn) return;

            e.preventDefault();
            e.stopPropagation();

            var cell = addBtn.closest('.excel-calendar__cell');
            if (!cell) return;

            var cellData = extractCellData(cell);
            if (cellData.date) {
                open(cellData);
            }
        }, true);
    }

    // --- Initialize on DOM ready ---
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() { init(); initAddButtonHandler(); });
    } else {
        init();
        initAddButtonHandler();
    }

    // --- Expose for external use ---
    window.CalendarBottomSheet = {
        open: open,
        close: close,
        isOpen: function () { return isOpen; }
    };

})();
