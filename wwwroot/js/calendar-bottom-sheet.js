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
        var calendarType = detectCalendarType();

        // Clear previous content
        bodyEl.innerHTML = '';
        actionsEl.innerHTML = '';

        // Set title with RTL-aware separator
        var dateStr = cellData.date || '';
        var rowLabel = cellData.rowLabel || '';
        var formattedDate = dateStr ? formatDate(dateStr) : '';
        if (isHebrew()) {
            titleEl.textContent = formattedDate + (rowLabel ? ' — ' + rowLabel : '');
        } else {
            titleEl.textContent = rowLabel + (formattedDate ? ' — ' + formattedDate : '');
        }

        // Context label below title
        var contextLabel = buildContextLabel(cellData, calendarType);
        if (contextLabel) {
            var contextEl = document.createElement('div');
            contextEl.className = 'bottom-sheet__context';
            contextEl.textContent = contextLabel;
            bodyEl.appendChild(contextEl);
        }

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

                // Remove button with tap-to-confirm pattern
                if (!cellData.isReadOnly && assignment.id) {
                    var removeBtn = document.createElement('button');
                    removeBtn.type = 'button';
                    removeBtn.className = 'bottom-sheet__remove-btn';
                    removeBtn.setAttribute('aria-label', (isHebrew() ? 'הסר ' : 'Remove ') + assignment.name);
                    removeBtn.innerHTML = '&times;';
                    removeBtn.addEventListener('click', function () {
                        tapToConfirmRemove(removeBtn, cellData, assignment);
                    });
                    item.appendChild(removeBtn);
                }

                list.appendChild(item);

                // Trainee sub-row (shifts only)
                if (calendarType === 'shifts' && !cellData.isReadOnly) {
                    if (assignment.traineeName) {
                        // Show existing trainee with remove button
                        var traineeRow = document.createElement('div');
                        traineeRow.className = 'bottom-sheet__trainee-row';
                        traineeRow.innerHTML =
                            '<span class="bottom-sheet__trainee-indicator">🎓</span>' +
                            '<span class="bottom-sheet__trainee-name">' + escapeText(assignment.traineeName) + '</span>';
                        var removeTraineeBtn = document.createElement('button');
                        removeTraineeBtn.type = 'button';
                        removeTraineeBtn.className = 'bottom-sheet__remove-btn bottom-sheet__remove-btn--small';
                        removeTraineeBtn.innerHTML = '&times;';
                        removeTraineeBtn.addEventListener('click', function () {
                            handleRemoveTrainee(assignment.id);
                        });
                        traineeRow.appendChild(removeTraineeBtn);
                        list.appendChild(traineeRow);
                    } else if (assignment.id && !assignment.isTrainee) {
                        // Show "Add Trainee" dropdown for assignments without a trainee
                        var traineeAddRow = buildTraineeAddRow(assignment.id);
                        if (traineeAddRow) {
                            list.appendChild(traineeAddRow);
                        }
                    }
                }
            });

            currentSection.appendChild(list);
            bodyEl.appendChild(currentSection);
        } else {
            // Contextual empty state
            var emptyEl = document.createElement('div');
            emptyEl.className = 'bottom-sheet__empty-state';
            emptyEl.textContent = getEmptyStateMessage(cellData, calendarType);
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

            // Chore-specific fields: title input + chore type dropdown
            var choreTitleInput = null;
            var choreTypeDropdown = null;
            if (calendarType === 'chores') {
                // Chore title input
                var titleFieldGroup = document.createElement('div');
                titleFieldGroup.className = 'bottom-sheet__field';
                var titleLabel = document.createElement('label');
                titleLabel.className = 'bottom-sheet__field-label';
                titleLabel.setAttribute('for', 'bottom-sheet-chore-title');
                titleLabel.textContent = isHebrew() ? 'כותרת התורנות' : 'Chore Title';
                titleFieldGroup.appendChild(titleLabel);
                choreTitleInput = document.createElement('input');
                choreTitleInput.type = 'text';
                choreTitleInput.className = 'bottom-sheet__input';
                choreTitleInput.id = 'bottom-sheet-chore-title';
                choreTitleInput.maxLength = 200;
                choreTitleInput.placeholder = isHebrew() ? 'הזן כותרת...' : 'Enter title...';
                titleFieldGroup.appendChild(choreTitleInput);
                addSection.appendChild(titleFieldGroup);

                // Chore type dropdown (if chore types exist on page)
                var pageChoreTypes = document.getElementById('choreTypeSelect');
                if (pageChoreTypes && pageChoreTypes.options.length > 1) {
                    var ctFieldGroup = document.createElement('div');
                    ctFieldGroup.className = 'bottom-sheet__field';
                    var ctLabel = document.createElement('label');
                    ctLabel.className = 'bottom-sheet__field-label';
                    ctLabel.setAttribute('for', 'bottom-sheet-chore-type');
                    ctLabel.textContent = isHebrew() ? 'סוג תורנות' : 'Chore Type';
                    ctFieldGroup.appendChild(ctLabel);
                    choreTypeDropdown = document.createElement('select');
                    choreTypeDropdown.className = 'bottom-sheet__select';
                    choreTypeDropdown.id = 'bottom-sheet-chore-type';
                    // Copy options from page dropdown
                    for (var ci = 0; ci < pageChoreTypes.options.length; ci++) {
                        var ctOpt = document.createElement('option');
                        ctOpt.value = pageChoreTypes.options[ci].value;
                        ctOpt.textContent = pageChoreTypes.options[ci].textContent;
                        ctOpt.selected = pageChoreTypes.options[ci].selected;
                        choreTypeDropdown.appendChild(ctOpt);
                    }
                    ctFieldGroup.appendChild(choreTypeDropdown);
                    addSection.appendChild(ctFieldGroup);
                }
            }

            // User/ShiftType selector
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
                // For chores, validate title inline instead of using prompt()
                if (calendarType === 'chores') {
                    handleChoreAssign(cellData, userSelect.value, choreTitleInput, choreTypeDropdown);
                } else {
                    handleAssign(cellData, userSelect.value);
                }
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

    // --- Build context label for the sheet ---
    function buildContextLabel(cellData, calendarType) {
        var date = cellData.date ? formatDate(cellData.date) : '';
        var label = cellData.rowLabel || '';
        if (calendarType === 'shifts') {
            if (cellData.rowId && cellData.rowId.indexOf('user-') === 0) {
                return (isHebrew() ? 'משמרות של: ' : 'Shifts for: ') + label;
            }
            return (isHebrew() ? 'שיבוץ ל: ' : 'Assigning to: ') + label;
        }
        if (calendarType === 'chores') {
            return (isHebrew() ? 'תורנויות של: ' : 'Chores for: ') + label;
        }
        if (calendarType === 'oncall') {
            return label;
        }
        return '';
    }

    // --- Get contextual empty state message ---
    function getEmptyStateMessage(cellData, calendarType) {
        var date = cellData.date ? formatDate(cellData.date) : '';
        var label = cellData.rowLabel || '';
        if (calendarType === 'shifts') {
            return isHebrew()
                ? 'אין שיבוצים ל' + label + ' ב-' + date
                : 'No one assigned to ' + label + ' on ' + date;
        }
        if (calendarType === 'chores') {
            return isHebrew()
                ? 'אין תורנויות ל' + label + ' ב-' + date
                : 'No chores for ' + label + ' on ' + date;
        }
        if (calendarType === 'oncall') {
            return isHebrew()
                ? 'אין תורנים ב-' + date
                : 'No ' + label + ' assigned on ' + date;
        }
        return isHebrew() ? 'אין שיבוצים עדיין' : 'No assignments yet';
    }

    // --- Tap-to-confirm pattern for remove buttons ---
    function tapToConfirmRemove(btn, cellData, assignment) {
        if (btn.dataset.confirming === 'true') {
            // Second tap — execute; clear the reset timer
            if (btn._confirmTimeout) clearTimeout(btn._confirmTimeout);
            handleRemoveAssignment(cellData, assignment);
            return;
        }
        // First tap — show confirmation state
        btn.dataset.confirming = 'true';
        var originalHtml = btn.innerHTML;
        btn.innerHTML = isHebrew() ? '✓' : '✓';
        btn.classList.add('bottom-sheet__remove-btn--confirming');
        btn.setAttribute('aria-label', isHebrew() ? 'לחץ שוב לאישור' : 'Tap again to confirm');
        // Reset after 3 seconds
        var timeout = setTimeout(function () {
            btn.dataset.confirming = '';
            btn.innerHTML = originalHtml;
            btn.classList.remove('bottom-sheet__remove-btn--confirming');
        }, 3000);
        btn._confirmTimeout = timeout;
    }

    // --- Escape text for safe DOM insertion ---
    function escapeText(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // --- Build trainee add row for a shift assignment ---
    function buildTraineeAddRow(assignmentId) {
        var traineeSelect = document.getElementById('traineeSelect');
        if (!traineeSelect || traineeSelect.options.length === 0) return null;

        var row = document.createElement('div');
        row.className = 'bottom-sheet__trainee-row bottom-sheet__trainee-row--add';

        var indicator = document.createElement('span');
        indicator.className = 'bottom-sheet__trainee-indicator';
        indicator.textContent = '🎓';
        row.appendChild(indicator);

        var select = document.createElement('select');
        select.className = 'bottom-sheet__select bottom-sheet__select--small';
        var defOpt = document.createElement('option');
        defOpt.value = '';
        defOpt.textContent = isHebrew() ? 'הוסף מתמחה...' : 'Add trainee...';
        select.appendChild(defOpt);
        for (var i = 0; i < traineeSelect.options.length; i++) {
            var opt = document.createElement('option');
            opt.value = traineeSelect.options[i].value;
            opt.textContent = traineeSelect.options[i].textContent;
            select.appendChild(opt);
        }
        row.appendChild(select);

        var addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.className = 'btn btn-ghost bottom-sheet__trainee-add-btn';
        addBtn.textContent = '+';
        addBtn.addEventListener('click', function () {
            var traineeId = parseInt(select.value, 10);
            if (select.value && !isNaN(traineeId)) {
                handleAddTrainee(assignmentId, traineeId);
            }
        });
        row.appendChild(addBtn);

        return row;
    }

    // --- Handle add trainee ---
    function handleAddTrainee(assignmentId, traineeUserId) {
        fetch('/Calendar/Table?handler=AddTrainee', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify({ assignmentId: assignmentId, traineeUserId: traineeUserId })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            if (result.success) {
                var msg = isHebrew() ? 'מתמחה שובץ בהצלחה' : 'Trainee assigned';
                if (window.showToast) window.showToast(msg, 'success');
                close();
                if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
            } else if (result.requiresOverride) {
                // Show warnings and ask to confirm
                var msgs = (result.warnings || []).map(function (w) { return w.message; }).join('\n');
                var confirmLabel = isHebrew() ? 'אישורים נדרשים:\n' : 'Warnings:\n';
                if (confirm(confirmLabel + msgs)) {
                    // Retry with override
                    fetch('/Calendar/Table?handler=AddTrainee', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
                        credentials: 'same-origin',
                        body: JSON.stringify({ assignmentId: assignmentId, traineeUserId: traineeUserId, overrideToken: result.overrideToken })
                    })
                    .then(function (r2) { return r2.json(); })
                    .then(function (r2) {
                        if (r2.success) {
                            if (window.showToast) window.showToast(isHebrew() ? 'מתמחה שובץ בהצלחה' : 'Trainee assigned', 'success');
                            close();
                            if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
                        } else {
                            if (window.showToast) window.showToast(r2.error || 'Error', 'error');
                        }
                    });
                }
            } else {
                if (window.showToast) window.showToast(result.error || 'Error', 'error');
            }
        })
        .catch(function () {
            if (window.showToast) window.showToast(isHebrew() ? 'שגיאת רשת' : 'Network error', 'error');
        });
    }

    // --- Handle remove trainee ---
    function handleRemoveTrainee(assignmentId) {
        fetch('/Calendar/Table?handler=RemoveTrainee', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify({ assignmentId: assignmentId })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            if (result.success) {
                var msg = isHebrew() ? 'מתמחה הוסר בהצלחה' : 'Trainee removed';
                if (window.showToast) window.showToast(msg, 'success');
                close();
                if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
            } else {
                if (window.showToast) window.showToast(result.error || 'Error', 'error');
            }
        })
        .catch(function () {
            if (window.showToast) window.showToast(isHebrew() ? 'שגיאת רשת' : 'Network error', 'error');
        });
    }

    // --- Handle chore assignment with inline form (replaces prompt()) ---
    function handleChoreAssign(cellData, userId, titleInput, choreTypeDropdown) {
        if (!userId) {
            var msg = isHebrew() ? 'נא לבחור משתמש' : 'Please select a user';
            if (window.showToast) window.showToast(msg, 'error');
            return;
        }
        var title = titleInput ? titleInput.value.trim() : '';
        if (!title) {
            var titleMsg = isHebrew() ? 'נא להזין כותרת' : 'Please enter a title';
            if (window.showToast) window.showToast(titleMsg, 'error');
            if (titleInput) titleInput.focus();
            return;
        }
        if (title.length > 200) {
            var lenMsg = isHebrew() ? 'הכותרת ארוכה מדי (מקסימום 200 תווים)' : 'Title too long (max 200 characters)';
            if (window.showToast) window.showToast(lenMsg, 'error');
            return;
        }
        var choreTypeId = choreTypeDropdown ? (choreTypeDropdown.value || null) : null;
        if (typeof window.quickAddChore === 'function') {
            // Close after initiating — quickAddChore handles its own toasts/confirms
            close();
            window.quickAddChore(cellData.date, userId, title, false, choreTypeId);
        }
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

        if (calendarType === 'chores') {
            // Chores are handled by handleChoreAssign via the inline form — should not reach here
            // Fallback: if somehow called from non-form path
            if (typeof window.quickAddChore === 'function') {
                var choreTypeSelect = document.getElementById('choreTypeSelect');
                var choreTypeId = choreTypeSelect ? (choreTypeSelect.value || null) : null;
                var fallbackTitle = isHebrew() ? 'תורנות' : 'Chore';
                window.quickAddChore(cellData.date, userId, fallbackTitle, false, choreTypeId);
                close();
            }
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

    // --- Handle remove assignment (called after tap-to-confirm) ---
    function handleRemoveAssignment(cellData, assignment) {
        var calendarType = detectCalendarType();

        if (calendarType === 'chores' && typeof window.deleteItem === 'function') {
            window.deleteItem('chore', assignment.id);
            close();
        } else if (calendarType === 'oncall' && typeof window.deleteItem === 'function') {
            window.deleteItem('onduty', assignment.id);
            close();
        } else if (calendarType === 'shifts') {
            // Shift removal via ClearAssignment handler on Calendar/Table
            fetch('/Calendar/Table?handler=ClearAssignment', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                credentials: 'same-origin',
                body: JSON.stringify({ assignmentId: assignment.id })
            })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result.success) {
                    var msg = isHebrew() ? 'השיבוץ הוסר בהצלחה' : 'Assignment removed';
                    if (window.showToast) window.showToast(msg, 'success');
                    close();
                    if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
                } else {
                    if (window.showToast) window.showToast(result.error || 'Error', 'error');
                }
            })
            .catch(function () {
                if (window.showToast) window.showToast(isHebrew() ? 'שגיאת רשת' : 'Network error', 'error');
            });
        } else {
            // Fallback for unknown calendar types
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
            var isTrainee = !!assignEl.querySelector(':scope > .excel-calendar__badge--trainee');
            var traineeId = assignEl.dataset.traineeId || '';
            var traineeName = assignEl.dataset.traineeName || '';

            assignments.push({
                id: assignmentId ? parseInt(assignmentId, 10) : null,
                name: nameEl ? nameEl.textContent.trim() : '',
                isTrainee: isTrainee,
                traineeId: traineeId ? parseInt(traineeId, 10) : null,
                traineeName: traineeName || null
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
            return;
        }

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

    }

    // --- Handle add-btn clicks (desktop + touch) ---
    function initAddButtonHandler() {
        document.addEventListener('click', function (e) {
            if (window.quickEntryActive) return;
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
        isOpen: function () { return isOpen; },
        extractCellData: extractCellData
    };

})();
