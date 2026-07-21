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

    // --- Error display helpers (uses ErrorStates API when available, falls back to showToast) ---
    // Optional `fix` ({ url, label }) — when the server supplies a "go fix it" remediation for an
    // assignment failure (officer rank -> profile, eligibility -> editor), show it via FeedbackModal
    // so the user gets the reason AND a one-click link to the place to change it.
    function showErrorMsg(msg, fix) {
        if (fix && fix.url && window.FeedbackModal && typeof window.FeedbackModal.show === 'function') {
            window.FeedbackModal.show('error', msg, { action: fix });
        } else if (window.ErrorStates) {
            window.ErrorStates.showError(msg);
        } else if (window.showToast) {
            window.showToast(msg, 'error');
        }
    }

    function showNetworkError() {
        if (window.ErrorStates) {
            window.ErrorStates.showError(
                window.ErrorStates.getMessage('networkError'),
                window.ErrorStates.getMessage('networkErrorTitle')
            );
        } else if (window.showToast) {
            window.showToast((window.AppLocalizer?.BottomSheet_NetworkError || 'Network error'), 'error');
        }
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

        // Phase B: auto-enhance [data-searchable] selects rendered into the sheet (rebuilt
        // per open). Scoped to the sheet element only — NOT document-wide (SEL-5/PF9).
        if (window.SearchableSelect) window.SearchableSelect.observe(sheetElement);

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
            sectionTitle.textContent = (window.AppLocalizer?.BottomSheet_CurrentAssignments || 'Current Assignments');
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
                    badge.textContent = (window.AppLocalizer?.BottomSheet_Trainee || 'Trainee');
                    item.appendChild(badge);
                }

                // Remove button with tap-to-confirm pattern
                if (!cellData.isReadOnly && assignment.id) {
                    var removeBtn = document.createElement('button');
                    removeBtn.type = 'button';
                    removeBtn.className = 'bottom-sheet__remove-btn';
                    removeBtn.setAttribute('aria-label', (window.AppLocalizer?.BottomSheet_RemoveButton || 'Remove ') + assignment.name);
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
                        (function (cd, asg) {
                            removeTraineeBtn.addEventListener('click', function () {
                                handleRemoveTrainee(cd, asg);
                            });
                        })(cellData, assignment);
                        traineeRow.appendChild(removeTraineeBtn);
                        list.appendChild(traineeRow);
                    } else if ((assignment.id || window.__draftSessionId) && !assignment.isTrainee) {
                        // Show "Add Trainee" dropdown for assignments without a trainee (staged chips have no id
                        // but ARE trainee-eligible in Draft Mode, keyed by coordinates).
                        var traineeAddRow = buildTraineeAddRow(cellData, assignment);
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
            addTitle.textContent = (window.AppLocalizer?.BottomSheet_AddAssignment || 'Add Assignment');
            addSection.appendChild(addTitle);

            // Chore-specific fields: title input + chore type dropdown
            var choreTitleInput = null;
            var choreTypeDropdown = null;
            // Eligibility hint element + refresh fn (chore parity Phase 4). Declared at function scope so
            // the seed call placed after assignBtn can reach refreshElig; assigned inside the chores block.
            var eligHint = null;
            var refreshElig = null;
            if (calendarType === 'chores') {
                // Chore title input
                var titleFieldGroup = document.createElement('div');
                titleFieldGroup.className = 'bottom-sheet__field';
                var titleLabel = document.createElement('label');
                titleLabel.className = 'bottom-sheet__field-label';
                titleLabel.setAttribute('for', 'bottom-sheet-chore-title');
                titleLabel.textContent = (window.AppLocalizer?.BottomSheet_ChoreTitle || 'Chore Title');
                titleFieldGroup.appendChild(titleLabel);
                choreTitleInput = document.createElement('input');
                choreTitleInput.type = 'text';
                choreTitleInput.className = 'bottom-sheet__input';
                choreTitleInput.id = 'bottom-sheet-chore-title';
                choreTitleInput.maxLength = 200;
                choreTitleInput.placeholder = (window.AppLocalizer?.BottomSheet_EnterTitle || 'Enter title...');
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
                    ctLabel.textContent = (window.AppLocalizer?.BottomSheet_ChoreType || 'Chore Type');
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

                    // Eligibility hint (chore parity Phase 4): when both a chore type and a user are chosen,
                    // fetch the (user × type) eligibility and render reason chips. Hard block (officer/exempt)
                    // disables Assign; a gender warning shows a chip but stays selectable (manager overrides
                    // at assign time). DISPLAY hint only — BusyService.ValidateChoreAsync is the real gate.
                    eligHint = document.createElement('div');
                    eligHint.className = 'bottom-sheet__elig-hint';
                    eligHint.id = 'bottom-sheet-elig-hint';
                    ctFieldGroup.appendChild(eligHint);

                    addSection.appendChild(ctFieldGroup);

                    // refreshElig references userSelect + assignBtn (declared lower in this function scope).
                    // It is only INVOKED after those vars execute (via change events / the seed call placed
                    // after assignBtn exists), so the closure resolves them correctly (CLAUDE.md §1 gate).
                    refreshElig = function () {
                        var cfg = window.CalendarPageConfig;
                        var typeId = choreTypeDropdown ? parseInt(choreTypeDropdown.value, 10) : NaN;
                        var uId = (typeof userSelect !== 'undefined' && userSelect) ? parseInt(userSelect.value, 10) : NaN;
                        if (eligHint) eligHint.innerHTML = '';
                        if (typeof assignBtn !== 'undefined' && assignBtn) assignBtn.disabled = false;
                        if (!cfg || !(cfg.moleculeId > 0) || isNaN(typeId) || typeId <= 0 || isNaN(uId) || uId <= 0) return;
                        fetch('/Api/Calendar/GetChoreEligibilityForCandidate?moleculeId=' + cfg.moleculeId +
                              '&userId=' + uId + '&choreTypeId=' + typeId, { credentials: 'same-origin' })
                            .then(function (r) { return r.ok ? r.json() : null; })
                            .then(function (data) {
                                if (!data || !data.success || !window.EligibilityChip) return;
                                if (eligHint) eligHint.innerHTML = window.EligibilityChip.renderChips(data);
                                if (typeof assignBtn !== 'undefined' && assignBtn && data.isHardBlocked) assignBtn.disabled = true;
                            })
                            .catch(function (err) { console.warn('Eligibility hint failed:', err); });
                    };
                    choreTypeDropdown.addEventListener('change', refreshElig);
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
                selectLabel.textContent = (window.AppLocalizer?.BottomSheet_SelectShift || 'Select Shift');
            } else {
                selectLabel.textContent = (window.AppLocalizer?.BottomSheet_SelectUser || 'Select User');
            }
            fieldGroup.appendChild(selectLabel);

            var userSelect = document.createElement('select');
            userSelect.className = 'bottom-sheet__select';
            userSelect.id = 'bottom-sheet-user-select';
            userSelect.setAttribute('data-searchable', '');

            // Default empty option
            var defaultOpt = document.createElement('option');
            defaultOpt.value = '';
            defaultOpt.textContent = (window.AppLocalizer?.BottomSheet_DefaultOption || '-- Select --');
            userSelect.appendChild(defaultOpt);

            // For shift-mode rows, fetch eligible users per shift type. Tech molecules always use the
            // endpoint (today's behavior); workforce molecules switch to it only when the 3b category
            // flag is on — so flag-off keeps the legacy page-list behavior byte-identical.
            var calPageConfig = window.CalendarPageConfig;
            if (calPageConfig && (calPageConfig.isTechMolecule || calPageConfig.categoryEligibilityEnabled)
                    && calPageConfig.moleculeId > 0
                    && itemType === 'user' && cellData.rowId && cellData.rowId.startsWith('shift-')) {
                var shiftTypeId = parseInt(cellData.rowId.replace('shift-', ''), 10);
                if (!isNaN(shiftTypeId) && shiftTypeId > 0) {
                    populateEligibleUsersAsync(userSelect, calPageConfig.moleculeId, shiftTypeId, cellData.date);
                } else {
                    // Malformed rowId — fall back to full molecule user list
                    var fallbackUsers = getAvailableUsers(cellData);
                    fallbackUsers.forEach(function (user) {
                        var opt = document.createElement('option');
                        opt.value = user.id;
                        opt.textContent = user.name;
                        opt.dataset.userName = user.name;
                        userSelect.appendChild(opt);
                    });
                    if (cellData.date && calPageConfig && calPageConfig.moleculeId > 0) {
                        decorateOptionsWithBusyAsync(userSelect, fallbackUsers.map(function (u) { return u.id; }), cellData.date, calPageConfig.moleculeId, null)
                            .catch(function (err) { console.warn('Busy decoration failed:', err); });
                    }
                }
            } else {
                // Populate from available users (read from page data or cell context)
                var availableUsers = getAvailableUsers(cellData);
                availableUsers.forEach(function (user) {
                    var opt = document.createElement('option');
                    opt.value = user.id;
                    // Disambiguate duplicate names across the area by suffixing the company (same
                    // "name — company" convention the eligibility path uses above).
                    opt.textContent = user.companyName ? (user.name + ' — ' + user.companyName) : user.name;
                    opt.dataset.userName = user.name;
                    userSelect.appendChild(opt);
                });

                // Busy decoration for chore/onduty modes (where rowId is user-N or dutytype-N)
                if (cellData.date && calPageConfig && calPageConfig.moleculeId > 0 && availableUsers.length > 0) {
                    var userIdList = availableUsers
                        .map(function (u) { return parseInt(u.id, 10); })
                        .filter(function (n) { return !isNaN(n) && n > 0; });
                    if (userIdList.length > 0) {
                        decorateOptionsWithBusyAsync(userSelect, userIdList, cellData.date, calPageConfig.moleculeId, null)
                            .catch(function (err) { console.warn('Busy decoration failed:', err); });
                    }
                }
            }

            fieldGroup.appendChild(userSelect);
            addSection.appendChild(fieldGroup);
            bodyEl.appendChild(addSection);

            // Creation-time hook: enhance now; async-populated options + busy decoration
            // re-sync via the per-select observer (SEL-3/SEL-4). Width is measured on open.
            if (window.SearchableSelect) window.SearchableSelect.enhance(userSelect);

            // Action buttons
            var assignBtn = document.createElement('button');
            assignBtn.type = 'button';
            assignBtn.className = 'btn btn-primary bottom-sheet__action-btn';
            assignBtn.textContent = (window.AppLocalizer?.BottomSheet_Assign || 'Assign');
            assignBtn.addEventListener('click', function () {
                // For chores, validate title inline instead of using prompt()
                if (calendarType === 'chores') {
                    handleChoreAssign(cellData, userSelect.value, choreTitleInput, choreTypeDropdown);
                } else {
                    handleAssign(cellData, userSelect.value);
                }
            });
            actionsEl.appendChild(assignBtn);

            // Wire the chore eligibility hint now that userSelect + assignBtn both exist (closure-safe).
            // The user dropdown changes the assignee; the chore-type dropdown change is wired above.
            if (calendarType === 'chores' && refreshElig) {
                userSelect.addEventListener('change', refreshElig);
                refreshElig(); // seed the hint for the row's default user + selected type
            }
        }

        // Time-off entry (Vacation / Day at / After) — available to calendar-editors on people
        // rows even on a read-only grid (Team). Independent of the shift-assign section above.
        if (cellData.rowId && cellData.rowId.indexOf('user-') === 0 && cellData.canEnterTimeOff) {
            addTimeOffSection(bodyEl, cellData);
        }

        // Cancel button
        var cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'btn btn-ghost bottom-sheet__action-btn';
        cancelBtn.textContent = (window.AppLocalizer?.Close || 'Close');
        cancelBtn.addEventListener('click', close);
        actionsEl.appendChild(cancelBtn);
    }

    // --- Build context label for the sheet ---
    function buildContextLabel(cellData, calendarType) {
        var date = cellData.date ? formatDate(cellData.date) : '';
        var label = cellData.rowLabel || '';
        if (calendarType === 'shifts') {
            if (cellData.rowId && cellData.rowId.indexOf('user-') === 0) {
                return (window.AppLocalizer?.BottomSheet_ShiftsFor || 'Shifts for: ') + label;
            }
            return (window.AppLocalizer?.BottomSheet_AssigningTo || 'Assigning to: ') + label;
        }
        if (calendarType === 'chores') {
            return (window.AppLocalizer?.BottomSheet_ChoresFor || 'Chores for: ') + label;
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
            return (window.AppLocalizer?.BottomSheet_NoShiftsFor || 'No one assigned to {0} on {1}').replace('{0}', label).replace('{1}', date);
        }
        if (calendarType === 'chores') {
            return (window.AppLocalizer?.BottomSheet_NoChoresFor || 'No chores for {0} on {1}').replace('{0}', label).replace('{1}', date);
        }
        if (calendarType === 'oncall') {
            return (window.AppLocalizer?.BottomSheet_NoOnCallFor || 'No {0} assigned on {1}').replace('{0}', label).replace('{1}', date);
        }
        return (window.AppLocalizer?.BottomSheet_NoAssignments || 'No assignments yet');
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
        btn.innerHTML = Icons.render('check', { size: 16 });
        btn.classList.add('bottom-sheet__remove-btn--confirming');
        btn.setAttribute('aria-label', (window.AppLocalizer?.BottomSheet_TapToConfirm || 'Tap again to confirm'));
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
    // POST headers for Table handlers, including the antiforgery token (mirrors getTablePostHeaders).
    function bottomSheetPostHeaders() {
        var headers = { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' };
        var csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (csrf) headers['RequestVerificationToken'] = csrf;
        return headers;
    }

    // Draft Mode: build the natural coordinates a trainee stage/clear needs from cellData + a chip assignment.
    // rowId is "shift-{id}" in shift-mode; primary user id comes off the chip (G7). Returns null if incomplete.
    function draftTraineeCoords(cellData, assignment) {
        if (!window.__draftSessionId) return null;
        var shiftTypeId = assignment.shiftTypeId;
        if (!shiftTypeId && cellData.rowId && cellData.rowId.indexOf('shift-') === 0) {
            shiftTypeId = parseInt(cellData.rowId.replace('shift-', ''), 10);
        }
        var primaryUserId = assignment.userId;
        if (!shiftTypeId || !primaryUserId || !cellData.date) return null;
        return { draftSessionId: parseInt(window.__draftSessionId, 10), shiftTypeId: shiftTypeId, date: cellData.date, primaryUserId: primaryUserId };
    }

    function buildTraineeAddRow(cellData, assignment) {
        var assignmentId = assignment.id;
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
        select.setAttribute('data-searchable', '');
        var defOpt = document.createElement('option');
        defOpt.value = '';
        defOpt.textContent = (window.AppLocalizer?.BottomSheet_AddTrainee || 'Add trainee...');
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
                handleAddTrainee(cellData, assignment, traineeId);
            }
        });
        row.appendChild(addBtn);

        return row;
    }

    // --- Handle add trainee ---
    function handleAddTrainee(cellData, assignment, traineeUserId) {
        // Draft Mode: stage by coordinates instead of the live AddTrainee handler.
        var dc = draftTraineeCoords(cellData, assignment);
        if (dc) {
            fetch('/Calendar/Table?handler=DraftAddTrainee', {
                method: 'POST',
                headers: bottomSheetPostHeaders(),
                credentials: 'same-origin',
                body: JSON.stringify({ draftSessionId: dc.draftSessionId, shiftTypeId: dc.shiftTypeId, date: dc.date, primaryUserId: dc.primaryUserId, traineeUserId: traineeUserId })
            })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result.success) {
                    if (window.showToast) window.showToast((window.AppLocalizer?.BottomSheet_TraineeAssigned || 'Trainee assigned'), 'success');
                    close();
                    if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
                } else {
                    showErrorMsg(result.error || 'Error');
                }
            })
            .catch(function () { showNetworkError(); });
            return;
        }
        var assignmentId = assignment.id;
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
                var msg = (window.AppLocalizer?.BottomSheet_TraineeAssigned || 'Trainee assigned');
                if (window.showToast) window.showToast(msg, 'success');
                close();
                if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
            } else if (result.requiresOverride) {
                // Show warnings via FeedbackModal.confirm (replaces native confirm())
                var confirmFn = (window.FeedbackModal && typeof window.FeedbackModal.confirm === 'function')
                    ? function () { return window.FeedbackModal.confirm('warning', { warnings: result.warnings || [] }); }
                    : function () {
                        var msgs = (result.warnings || []).map(function (w) { return w.message; }).join('\n');
                        var label = (window.AppLocalizer?.BottomSheet_Warnings || 'Warnings:') + '\n';
                        return Promise.resolve(confirm(label + msgs));
                    };
                confirmFn().then(function (proceed) {
                    if (!proceed) return;
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
                            if (window.showToast) window.showToast((window.AppLocalizer?.BottomSheet_TraineeAssigned || 'Trainee assigned'), 'success');
                            close();
                            if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
                        } else {
                            showErrorMsg(r2.error || 'Error', r2.fix);
                        }
                    });
                });
            } else {
                showErrorMsg(result.error || 'Error', result.fix);
            }
        })
        .catch(function () {
            showNetworkError();
        });
    }

    // --- Handle remove trainee ---
    function handleRemoveTrainee(cellData, assignment) {
        // Draft Mode: stage a trainee-clear by coordinates instead of the live RemoveTrainee handler.
        var dc = draftTraineeCoords(cellData, assignment);
        var url = dc ? '/Calendar/Table?handler=DraftRemoveTrainee' : '/Calendar/Table?handler=RemoveTrainee';
        var body = dc
            ? { draftSessionId: dc.draftSessionId, shiftTypeId: dc.shiftTypeId, date: dc.date, primaryUserId: dc.primaryUserId }
            : { assignmentId: assignment.id };
        fetch(url, {
            method: 'POST',
            headers: bottomSheetPostHeaders(),
            credentials: 'same-origin',
            body: JSON.stringify(body)
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            if (result.success) {
                var msg = (window.AppLocalizer?.BottomSheet_TraineeRemoved || 'Trainee removed');
                if (window.showToast) window.showToast(msg, 'success');
                close();
                if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
            } else {
                showErrorMsg(result.error || 'Error');
            }
        })
        .catch(function () {
            showNetworkError();
        });
    }

    // --- Handle chore assignment with inline form (replaces prompt()) ---
    function handleChoreAssign(cellData, userId, titleInput, choreTypeDropdown) {
        if (!userId) {
            var msg = (window.AppLocalizer?.BottomSheet_SelectUserRequired || 'Please select a user');
            showErrorMsg(msg);
            return;
        }
        var title = titleInput ? titleInput.value.trim() : '';
        if (!title) {
            var titleMsg = (window.AppLocalizer?.BottomSheet_TitleRequired || 'Please enter a title');
            showErrorMsg(titleMsg);
            if (titleInput) titleInput.focus();
            return;
        }
        if (title.length > 200) {
            var lenMsg = (window.AppLocalizer?.BottomSheet_TitleTooLong || 'Title too long (max 200 characters)');
            showErrorMsg(lenMsg);
            return;
        }
        var choreTypeId = choreTypeDropdown ? (choreTypeDropdown.value || null) : null;
        if (typeof window.quickAddChore === 'function') {
            // Close after initiating — quickAddChore handles its own toasts/confirms
            close();
            window.quickAddChore(cellData.date, userId, title, choreTypeId);
        }
    }

    // --- Fetch eligible users for a Tech molecule shift type and populate a <select> ---
    function appendDisabledOption(selectEl, text) {
        var opt = document.createElement('option');
        opt.value = '';
        opt.disabled = true;
        opt.textContent = text;
        selectEl.appendChild(opt);
    }

    // Remove every option EXCEPT a leading non-disabled empty default (the "-- Select --" placeholder).
    function clearRealOptions(selectEl) {
        Array.prototype.slice.call(selectEl.options).forEach(function (opt) {
            if (opt.value || opt.disabled) selectEl.removeChild(opt);
        });
    }

    // A native <select> can't host a button, so render the "Show all shift workers" escape hatch as a
    // sibling link right after it (3b: promotes a noCategory result to the all-DoesShifts fallback list).
    function appendFallbackButton(selectEl, onClick) {
        if (selectEl._fallbackBtn && selectEl._fallbackBtn.parentNode) {
            selectEl._fallbackBtn.parentNode.removeChild(selectEl._fallbackBtn);
        }
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-link bottom-sheet__fallback-btn';
        btn.textContent = loc('QuickEntry_ShowAllWorkers', 'Show all shift workers');
        btn.addEventListener('click', function () {
            if (btn.parentNode) btn.parentNode.removeChild(btn);
            selectEl._fallbackBtn = null;
            onClick();
        });
        selectEl._fallbackBtn = btn;
        if (selectEl.parentNode) selectEl.parentNode.insertBefore(btn, selectEl.nextSibling);
    }

    async function populateEligibleUsersAsync(selectEl, moleculeId, shiftTypeId, date, allowFallback) {
        var loadingOpt = document.createElement('option');
        loadingOpt.value = '';
        loadingOpt.disabled = true;
        loadingOpt.textContent = loc('QuickEntry_Loading', 'Loading…');
        selectEl.appendChild(loadingOpt);
        selectEl.setAttribute('aria-busy', 'true');
        try {
            var url = '/Api/Calendar/GetEligibleUsersForShift?moleculeId=' + moleculeId + '&shiftTypeId=' + shiftTypeId
                + (allowFallback ? '&allowFallback=true' : '');
            var response = await fetch(url, { credentials: 'same-origin' });
            if (!response.ok) throw new Error('Server returned ' + response.status);
            var data = await response.json();
            if (loadingOpt.parentNode === selectEl) selectEl.removeChild(loadingOpt);
            selectEl.removeAttribute('aria-busy');
            if (!data.success) throw new Error('unsuccessful');

            if (Array.isArray(data.users) && data.users.length > 0) {
                data.users.forEach(function (user) {
                    var opt = document.createElement('option');
                    opt.value = user.id;
                    opt.dataset.userName = user.name;
                    // Native <select>: append company as a " — {company}" suffix (3b disambiguation).
                    opt.textContent = user.companyName ? (user.name + ' — ' + user.companyName) : user.name;
                    selectEl.appendChild(opt);
                });

                if (date && moleculeId) {
                    // Decorate with busy badges (best-effort — failure is non-fatal)
                    decorateOptionsWithBusyAsync(selectEl, data.users.map(function (u) { return u.id; }), date, moleculeId, null)
                        .catch(function (err) { console.warn('Busy decoration failed:', err); });
                }
            } else if (data.reason === 'noCategory') {
                // Structural zero: assignable shift missing a category. Nudge + escape hatch.
                appendDisabledOption(selectEl, loc('QuickEntry_NoCategorySet', 'This shift has no category set'));
                appendFallbackButton(selectEl, function () {
                    clearRealOptions(selectEl);
                    populateEligibleUsersAsync(selectEl, moleculeId, shiftTypeId, date, true);
                });
            } else {
                appendDisabledOption(selectEl, loc('QuickEntry_NoEligibleUsers', 'No eligible users for this shift'));
            }
        } catch (e) {
            if (loadingOpt.parentNode === selectEl) selectEl.removeChild(loadingOpt);
            selectEl.removeAttribute('aria-busy');
            console.error('Failed to load eligible users:', e);
            appendDisabledOption(selectEl, loc('QuickEntry_LoadFailedFallback', "Couldn't load eligible users — try again"));
        }
    }

    // --- Decorate <option> labels with busy badges (text prefixes) and disable hard conflicts ---
    // Native <option> can't render rich HTML cross-browser, so we prefix the text with
    // single-character glyphs and append a parenthetical hint. data-busy-* attributes
    // are added so future enhancements (custom listbox) can pick up the same data.
    async function decorateOptionsWithBusyAsync(selectEl, userIds, date, moleculeId, target) {
        if (!userIds || userIds.length === 0) return;
        var resp = await fetch('/Api/Calendar/GetBusyStates', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin',
            body: JSON.stringify({ userIds: userIds, date: date, moleculeId: moleculeId, target: target })
        });
        if (!resp.ok) return;
        var json = await resp.json();
        if (!json.success || !json.busy) return;

        Array.prototype.forEach.call(selectEl.options, function (opt) {
            if (!opt.value) return;
            var state = json.busy[opt.value];
            if (!state) return;

            var name = opt.dataset.userName || opt.textContent;
            var badges = [];
            if (state.hasShift) {
                if (state.shift && state.shift.isHome) badges.push('\u{1F3E0}'); // 🏠
                else if (state.shift && state.shift.isOffline) badges.push('\u{1F4F4}'); // 📴
                else badges.push('⏱'); // ⏱
            }
            if (state.hasChore) badges.push('\u{1F9F9}'); // 🧹
            if (state.hasOnDuty) badges.push('\u{1F6E1}'); // 🛡
            if (state.hasVacation) badges.push('\u{1F334}'); // 🌴

            opt.dataset.busyHasShift = state.hasShift ? 'true' : 'false';
            opt.dataset.busyHasChore = state.hasChore ? 'true' : 'false';
            opt.dataset.busyHasOnDuty = state.hasOnDuty ? 'true' : 'false';
            opt.dataset.busyHasVacation = state.hasVacation ? 'true' : 'false';
            opt.dataset.busyHighest = state.highest;

            if (state.hasHardError) {
                opt.disabled = true;
                opt.dataset.busyHardError = state.hardErrorKey || 'true';
            }

            var prefix = badges.length > 0 ? badges.join('') + '  ' : '';
            var suffix = '';
            if (state.shift && state.shift.name) {
                suffix = '  • ' + state.shift.name;
                if (state.shift.start && state.shift.end && !state.shift.isHome && !state.shift.isOffline) {
                    suffix += ' ' + state.shift.start + '–' + state.shift.end;
                }
            } else if (state.hasChore && state.choreTitle) {
                suffix = '  • ' + state.choreTitle;
            } else if (state.hasOnDuty) {
                suffix = '  • ' + (state.onDutyType || 'on-duty');
            } else if (state.hasVacation) {
                suffix = '  • \u{1F334}';
            }

            opt.textContent = prefix + name + suffix;
        });
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
                    users.push({ id: option.value, name: option.textContent, companyName: option.dataset.company || null });
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

    // --- Time-off entry section (manual Vacation / Day at / After) ---
    function addTimeOffSection(bodyEl, cellData) {
        var uid = cellData.rowId.replace('user-', '');
        var sec = document.createElement('div');
        sec.className = 'bottom-sheet__section';

        var h = document.createElement('h4');
        h.className = 'bottom-sheet__section-title';
        h.textContent = (window.AppLocalizer && window.AppLocalizer.TimeOff_Section_Title) || 'Add time off';
        sec.appendChild(h);

        function mkBtn(labelKey, fallback, onClick) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'btn btn-ghost bottom-sheet__action-btn';
            b.textContent = (window.AppLocalizer && window.AppLocalizer[labelKey]) || fallback;
            b.addEventListener('click', onClick);
            sec.appendChild(b);
            return b;
        }

        mkBtn('QuickEntry_TimeOff_Vacation', 'Vacation', function () {
            window.quickAddTimeOff(cellData.date, uid, 'vacation', null);
            close();
        });
        mkBtn('QuickEntry_TimeOff_After', 'After', function () {
            window.quickAddTimeOff(cellData.date, uid, 'after', null);
            close();
        });

        // "Day at X" needs a location; first click reveals the input, second confirms.
        var locWrap = document.createElement('div');
        locWrap.className = 'bottom-sheet__field';
        locWrap.style.display = 'none';
        var locInput = document.createElement('input');
        locInput.type = 'text';
        locInput.className = 'bottom-sheet__input';
        locInput.maxLength = 50;
        locInput.placeholder = (window.AppLocalizer && window.AppLocalizer.QuickEntry_TimeOff_DayAtLocationPrompt) || 'Where?';
        locWrap.appendChild(locInput);

        mkBtn('QuickEntry_TimeOff_DayAt', 'Day at…', function () {
            if (locWrap.style.display === 'none') {
                locWrap.style.display = '';
                locInput.focus();
                return;
            }
            var v = locInput.value.trim();
            if (!v) { locInput.focus(); return; }
            window.quickAddTimeOff(cellData.date, uid, 'dayat', v);
            close();
        });
        sec.appendChild(locWrap);

        bodyEl.appendChild(sec);
    }

    // --- Handle assign action ---
    function handleAssign(cellData, userId) {
        if (!userId) {
            var msg = (window.AppLocalizer?.BottomSheet_SelectUserRequired || 'Please select a user');
            showErrorMsg(msg);
            return;
        }

        var calendarType = detectCalendarType();

        if (calendarType === 'chores') {
            // Chores are handled by handleChoreAssign via the inline form — should not reach here
            // Fallback: if somehow called from non-form path
            if (typeof window.quickAddChore === 'function') {
                var choreTypeSelect = document.getElementById('choreTypeSelect');
                var choreTypeId = choreTypeSelect ? (choreTypeSelect.value || null) : null;
                var fallbackTitle = (window.AppLocalizer?.BottomSheet_Chore || 'Chore');
                window.quickAddChore(cellData.date, userId, fallbackTitle, choreTypeId);
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
                    var noShiftMsg = (window.AppLocalizer?.BottomSheet_SelectShiftRequired || 'Please select a shift');
                    showErrorMsg(noShiftMsg);
                }
            } else if (cellData.rowId && cellData.rowId.indexOf('shift-') === 0) {
                // Shift-mode: row is a shift type, dropdown value is the user
                var shiftTypeId = cellData.rowId.replace('shift-', '');
                window.quickAddShift(shiftTypeId, cellData.date, userId);
                close();
            } else {
                var noActionMsg = (window.AppLocalizer?.BottomSheet_CannotAddAssignment || 'Cannot add assignment here');
                showErrorMsg(noActionMsg);
            }
        } else {
            var noActionMsg2 = (window.AppLocalizer?.BottomSheet_CannotAddAssignment || 'Cannot add assignment here');
            showErrorMsg(noActionMsg2);
        }
    }

    // --- Handle remove assignment (called after tap-to-confirm) ---
    function handleRemoveAssignment(cellData, assignment) {
        // Text entries route to dedicated handler regardless of calendar type
        if (assignment.entryType === 'text' && typeof window.deleteTextEntry === 'function') {
            window.deleteTextEntry(assignment.id);
            close();
            return;
        }

        var calendarType = detectCalendarType();

        if (calendarType === 'chores' && typeof window.deleteItem === 'function') {
            window.deleteItem('chore', assignment.id);
            close();
        } else if (calendarType === 'oncall' && typeof window.deleteItem === 'function') {
            window.deleteItem('onduty', assignment.id);
            close();
        } else if (calendarType === 'shifts') {
            // Draft Mode: stage a clear by natural coordinates (shiftType+date+primary); live mode clears by id.
            // A staged chip has a negative synthetic id and no persisted row, so it MUST take the draft path.
            var dcClear = draftTraineeCoords(cellData, assignment); // same coords: shiftType + date + primary user
            var clearUrl = dcClear ? '/Calendar/Table?handler=DraftClear' : '/Calendar/Table?handler=ClearAssignment';
            var clearBody = dcClear
                ? { draftSessionId: dcClear.draftSessionId, shiftTypeId: dcClear.shiftTypeId, date: dcClear.date, userId: dcClear.primaryUserId }
                : { assignmentId: assignment.id };

            fetch(clearUrl, {
                method: 'POST',
                headers: bottomSheetPostHeaders(),
                credentials: 'same-origin',
                body: JSON.stringify(clearBody)
            })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result.success) {
                    var msg = (window.AppLocalizer?.BottomSheet_AssignmentRemoved || 'Assignment removed');
                    if (window.showToast) window.showToast(msg, 'success');
                    close();
                    if (typeof triggerCalendarRefresh === 'function') { triggerCalendarRefresh(); } else { location.reload(); }
                } else {
                    showErrorMsg(result.error || 'Error');
                }
            })
            .catch(function () {
                showNetworkError();
            });
        } else {
            // Fallback for unknown calendar types
            var noRemoveMsg = (window.AppLocalizer?.BottomSheet_CannotRemoveAssignment || 'Cannot remove assignment here');
            showErrorMsg(noRemoveMsg);
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
            // Read the inner name element, not the whole label cell: the row-reorder grip
            // (`.excel-calendar__row-grip`, glyph "⋮⋮") is injected as the label cell's first
            // child, so scraping the cell leaked "⋮⋮ Hakam" into the sheet title.
            var labelEl = rowEl.querySelector('.excel-calendar__row-label-name')
                       || rowEl.querySelector('.excel-calendar__row-label');
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
            // G7: harvest the primary user id + shift type id so Draft Mode can key trainee/clear
            // actions by natural coordinates (a staged chip has no persisted assignment id).
            var chipUserId = assignEl.dataset.userId || '';
            var chipShiftTypeId = assignEl.dataset.shiftTypeId || '';

            var entryType = assignEl.dataset.entryType || '';

            assignments.push({
                id: assignmentId ? parseInt(assignmentId, 10) : null,
                name: nameEl ? nameEl.textContent.trim() : '',
                isTrainee: isTrainee,
                traineeId: traineeId ? parseInt(traineeId, 10) : null,
                traineeName: traineeName || null,
                userId: chipUserId ? parseInt(chipUserId, 10) : null,
                shiftTypeId: chipShiftTypeId ? parseInt(chipShiftTypeId, 10) : null,
                entryType: entryType
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
            canEnterTimeOff: cellEl.dataset.canEnterTimeoff === 'true',
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
