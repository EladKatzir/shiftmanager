// ✅ PHASE 20: Calendar inline editing and quick-add utilities
// This file provides client-side functionality for creating and deleting chores and on-duty assignments
// directly from the calendar views (Month, Week, Day)

/**
 * Trigger an in-place calendar refresh instead of a full page reload.
 * Uses the CalendarRealtime shadow refresh mechanism when available,
 * which fetches fresh data via AJAX and updates the DOM in place.
 * Falls back to full page reload if CalendarRealtime is not initialized.
 */
async function triggerCalendarRefresh() {
    const grid = document.querySelector('.excel-calendar');

    // Legacy Month/Week/Day calendars render .calendar-cell (not .excel-calendar) and have no
    // in-place swap target — fall back to a full reload there to preserve existing behavior.
    if (!grid) {
        location.reload();
        return;
    }

    // Capture scroll position + keyboard focus BEFORE the swap. This is the entire point of the
    // fix: assigning a user near the bottom/right of the grid must not jump the view to the top.
    const scrollLeft = grid.scrollLeft;
    const scrollTop = grid.scrollTop;
    const windowY = window.scrollY;
    const active = document.activeElement;
    const focusedCell = active && active.closest ? active.closest('.excel-calendar__cell') : null;
    const focusRowId = focusedCell ? focusedCell.getAttribute('data-row-id') : null;
    const focusDate = focusedCell ? focusedCell.getAttribute('data-date') : null;

    try {
        // Re-fetch the current calendar view (all state lives in the query string) and swap only the
        // grid. Full-grid replacement is robust where the abandoned per-cell shadow refresh was not,
        // and click handlers are delegated on document so they keep working on the new DOM.
        const response = await fetch(window.location.href, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin',
            cache: 'no-store'
        });
        if (!response.ok) { location.reload(); return; }

        const html = await response.text();
        const doc = new DOMParser().parseFromString(html, 'text/html');
        const fresh = doc.querySelector('.excel-calendar');
        if (!fresh) { location.reload(); return; }

        grid.replaceWith(fresh);

        // Restore scroll position (.excel-calendar is the overflow:auto scroll container) + window.
        fresh.scrollLeft = scrollLeft;
        fresh.scrollTop = scrollTop;
        window.scrollTo(0, windowY);

        // Restore keyboard focus to the edited cell if it still exists after the refresh.
        if (focusRowId && focusDate && window.CSS && CSS.escape) {
            const cell = fresh.querySelector(
                '.excel-calendar__cell[data-row-id="' + CSS.escape(focusRowId) +
                '"][data-date="' + CSS.escape(focusDate) + '"]');
            if (cell) cell.focus({ preventScroll: true });
        }

        // Let load-time modules (lazy rows, group toggles, keyboard nav) re-bind to the new grid.
        document.dispatchEvent(new CustomEvent('calendar:grid-refreshed', { detail: { grid: fresh } }));
    } catch (err) {
        // Network/parse failure → guarantee correctness by falling back to a full reload.
        if (window.console && console.warn) console.warn('[calendar] in-place refresh failed; reloading', err);
        location.reload();
    }
}

// Localized error messages
const ERROR_MESSAGES = {
    'he-IL': {
        sessionExpired: 'פג תוקף ההתחברות. נא לרענן את העמוד.',
        unauthorized: 'אין לך הרשאה לבצע פעולה זו.',
        networkError: 'שגיאת רשת. נא לבדוק את החיבור שלך.',
        serverError: 'שגיאת שרת. נא לנסות שוב מאוחר יותר.',
        confirmDelete: 'האם אתה בטוח שברצונך למחוק פריט זה?'
    },
    'en-US': {
        sessionExpired: 'Your session has expired. Please refresh the page.',
        unauthorized: 'You do not have permission to perform this action.',
        networkError: 'Network error. Please check your connection.',
        serverError: 'Server error. Please try again later.',
        confirmDelete: 'Are you sure you want to delete this item?'
    }
};

/**
 * Get current culture from HTML lang attribute
 */
function getCurrentCulture() {
    const htmlLang = document.documentElement.lang || 'en-US';
    return htmlLang.startsWith('he') ? 'he-IL' : 'en-US';
}

/**
 * Read-only calendar "?" help. Builds a localized message naming the grant(s) the user is missing
 * (rendered server-side into the hidden .excel-calendar__readonly-help-data node) and shows it via
 * FeedbackModal, so the user knows exactly which permission to request from an administrator.
 */
window.showReadOnlyGrantHelp = function (btn) {
    const banner = btn && btn.closest('.excel-calendar__readonly-banner');
    const data = banner && banner.querySelector('.excel-calendar__readonly-help-data');
    if (!data) return;
    const intro = (data.getAttribute('data-intro') || '').trim();
    const ask = (data.getAttribute('data-ask') || '').trim();
    const grants = Array.prototype.slice
        .call(data.querySelectorAll('.excel-calendar__readonly-help-grant'))
        .map(function (el) { return el.textContent.trim(); })
        .filter(Boolean);
    const parts = [];
    if (intro) parts.push(intro);
    if (grants.length) parts.push(grants.join(', '));
    if (ask) parts.push(ask);
    const message = parts.join(' ');
    if (window.FeedbackModal && typeof window.FeedbackModal.show === 'function') {
        window.FeedbackModal.show('info', message);
    } else if (window.console && console.warn) {
        console.warn('[calendar] FeedbackModal unavailable; read-only help:', message);
    }
};

// Default confirm handler routes through FeedbackModal.confirm so warnings render
// with category-colored badges and a localized OK/Cancel button pair instead of a
// native browser confirm() dialog. Accepts either:
//   - an array of structured warnings ({key, category, resourceType, resourceName, date, startTime, endTime})
//   - a plain string message (legacy callers; preserved during migration)
const defaultConfirm = (warningsOrMessage) => {
    if (window.FeedbackModal && typeof window.FeedbackModal.confirm === 'function') {
        const opts = Array.isArray(warningsOrMessage)
            ? { warnings: warningsOrMessage }
            : { message: typeof warningsOrMessage === 'string' ? warningsOrMessage : '' };
        return window.FeedbackModal.confirm('warning', opts);
    }
    // Fallback only fires if feedback-modal.js failed to load
    const msg = typeof warningsOrMessage === 'string'
        ? warningsOrMessage
        : (Array.isArray(warningsOrMessage)
            ? warningsOrMessage.map(w => w.message || w.key || '').join('\n')
            : '');
    return Promise.resolve(confirm(msg));
};

/**
 * Build fetch headers for Calendar/Table POST handlers (includes CSRF anti-forgery token).
 * Razor Pages auto-validate antiforgery — without this token, POSTs return 400.
 */
function getTablePostHeaders() {
    var headers = {
        'Content-Type': 'application/json',
        'X-Requested-With': 'XMLHttpRequest'
    };
    var csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (csrfToken) headers['RequestVerificationToken'] = csrfToken;
    return headers;
}

/**
 * Get localized error message
 */
function getErrorMessage(key) {
    const culture = getCurrentCulture();
    return ERROR_MESSAGES[culture][key] || ERROR_MESSAGES['en-US'][key];
}

/**
 * Handle API errors with appropriate user feedback
 */
function handleApiError(response, error = null) {
    if (response) {
        switch (response.status) {
            case 401:
                // Session expired
                showToast(getErrorMessage('sessionExpired'), 'error');
                // Optionally trigger session check
                if (typeof window.checkSessionStatus === 'function') {
                    setTimeout(() => window.checkSessionStatus(), 1000);
                }
                return;
            case 403:
                // Unauthorized
                showToast(getErrorMessage('unauthorized'), 'error');
                return;
            case 500:
            case 502:
            case 503:
                // Server error
                showToast(getErrorMessage('serverError'), 'error');
                return;
        }
    }

    if (error) {
        // Network error
        Logger.error('InlineEdit', 'API Error:', error);
        showToast(getErrorMessage('networkError'), 'error');
    }
}

/**
 * Show an error the user must acknowledge via the global FeedbackModal.
 * FeedbackModal is loaded in _Layout.cshtml so it is always available.
 */
function showAcknowledgedError(message, fix) {
    // fix = { label, url } from the server — renders a "go fix it" button in the modal.
    window.FeedbackModal.show('error', message, (fix && fix.url) ? { action: fix } : undefined);
}

/**
 * Extract the server's localized `message` from a fetch Response and show it as
 * an acknowledged error. Falls back to the supplied generic text on parse failure.
 */
async function showServerErrorAsync(response, fallbackMessage) {
    var serverMessage = null;
    var fix = null;
    try {
        var parsed = await response.clone().json();
        if (parsed && typeof parsed.message === 'string' && parsed.message.length > 0) {
            serverMessage = parsed.message;
        }
        if (parsed && parsed.fix && parsed.fix.url) {
            fix = { label: parsed.fix.label, url: parsed.fix.url };
        }
    } catch (e) { /* non-JSON body — fall back */ }
    showAcknowledgedError(serverMessage || fallbackMessage, fix);
}

/**
 * Quick-add a chore. Uses the unified busy-validation envelope:
 *   - success:true                                          → toast + refresh
 *   - success:false, requiresOverride, warnings[], token    → show FeedbackModal.confirm; on OK retry with token
 *   - success:false, message                                → blocking error modal
 */
async function quickAddChore(date, assigneeId, title, choreTypeId = null, confirmHandler = defaultConfirm) {
    async function postChore(body) {
        return fetch('/Api/Calendar/QuickAddChore', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin',
            body: JSON.stringify(body)
        });
    }

    try {
        var requestBody = {
            date: date,
            assigneeId: parseInt(assigneeId),
            title: title.trim(),
            notes: null
        };
        if (choreTypeId != null) {
            requestBody.choreTypeId = parseInt(choreTypeId);
        }
        const response = await postChore(requestBody);

        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                handleApiError(response);
                return;
            }
            await showServerErrorAsync(response, window.AppLocalizer.ErrorCreatingChore);
            return;
        }

        const result = await response.json();

        if (result.success) {
            showToast(result.message || window.AppLocalizer.ChoreCreatedSuccessfully, 'success');
            triggerCalendarRefresh();
        } else if (result.requiresOverride) {
            // Warnings — show structured confirm modal, retry with override token if accepted.
            if (await confirmHandler(result.warnings || [])) {
                var retryBody = Object.assign({}, requestBody, { overrideToken: result.overrideToken });
                const retryResponse = await postChore(retryBody);
                if (!retryResponse.ok) {
                    await showServerErrorAsync(retryResponse, window.AppLocalizer.ErrorCreatingChore);
                    return;
                }
                const retryResult = await retryResponse.json();
                if (retryResult.success) {
                    showToast(retryResult.message || window.AppLocalizer.ChoreCreatedSuccessfully, 'success');
                    triggerCalendarRefresh();
                } else {
                    showAcknowledgedError(retryResult.message || window.AppLocalizer.ErrorCreatingChore, retryResult.fix);
                }
            }
        } else {
            showAcknowledgedError(result.message || window.AppLocalizer.ErrorCreatingChore, result.fix);
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

/**
 * Quick-add an on-duty assignment.
 * @param {string} date - Date in yyyy-MM-dd format
 * @param {number} assigneeId - User ID to assign
 * @param {number} onDutyType - OnDutyType enum value (0=Hakam, 1=Lead, 2+=Custom)
 * @param {number|undefined} moleculeId - Optional molecule for HMAC override-token canonical
 *   matching. Justice "Make it real" supplies this so the override token signed at
 *   eligibility time validates at POST time. Other callers omit it and the server falls
 *   back to assignee-company resolution.
 * @param {Function|undefined} confirmHandler - Override confirmation handler.
 */
async function quickAddOnDuty(date, assigneeId, onDutyType, moleculeId, confirmHandler = defaultConfirm) {
    try {
        async function postOnDuty(body) {
            return fetch('/Api/Calendar/QuickAddOnDuty', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin',
                body: JSON.stringify(body)
            });
        }

        var requestBody = {
            date: date,
            assigneeId: parseInt(assigneeId),
            onDutyType: parseInt(onDutyType),
            notes: null
        };
        if (moleculeId != null && moleculeId !== undefined) {
            requestBody.moleculeId = parseInt(moleculeId);
        }

        const response = await postOnDuty(requestBody);

        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                try {
                    var errorResult = await response.clone().json();
                    if (errorResult.error === 'OFFICER_RANK_REQUIRED') {
                        showAcknowledgedError(errorResult.message || (getCurrentCulture() === 'he-IL'
                            ? 'סוג תורנות זה דורש דרגת קצין'
                            : 'This duty type requires officer rank'), errorResult.fix);
                        return;
                    }
                } catch (e) { /* fall through */ }
                handleApiError(response);
                return;
            }
            await showServerErrorAsync(response, window.AppLocalizer.ErrorCreatingOnDuty);
            return;
        }

        const result = await response.json();

        if (result.success) {
            showToast(result.message || window.AppLocalizer.OnDutyCreatedSuccessfully, 'success');
            triggerCalendarRefresh();
        } else if (result.requiresOverride) {
            if (await confirmHandler(result.warnings || [])) {
                var retryBody = Object.assign({}, requestBody, { overrideToken: result.overrideToken });
                const retryResponse = await postOnDuty(retryBody);
                if (!retryResponse.ok) {
                    await showServerErrorAsync(retryResponse, window.AppLocalizer.ErrorCreatingOnDuty);
                    return;
                }
                const retryResult = await retryResponse.json();
                if (retryResult.success) {
                    showToast(retryResult.message || window.AppLocalizer.OnDutyCreatedSuccessfully, 'success');
                    triggerCalendarRefresh();
                } else {
                    showAcknowledgedError(retryResult.message || window.AppLocalizer.ErrorCreatingOnDuty);
                }
            }
        } else {
            showAcknowledgedError(result.message || window.AppLocalizer.ErrorCreatingOnDuty);
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

/**
 * Quick-add a shift assignment
 * @param {number} shiftTypeId - ShiftType ID to assign
 * @param {string} date - Date in yyyy-MM-dd format
 * @param {number} assigneeId - User ID to assign
 */
// shiftInstanceId (optional): when the caller already knows the exact hole to fill (Justice
// "make it real"), pass it so the server targets that instance instead of re-resolving by
// ShiftType+Date (ambiguous across companies sharing the ShiftType). Omitted by the
// bottom-sheet / quick-entry callers, which keep the legacy ShiftType+Date behavior.
async function quickAddShift(shiftTypeId, date, assigneeId, confirmHandler = defaultConfirm, _retried, shiftInstanceId) {
    try {
        const response = await fetch('/Calendar/Table?handler=AssignEmployee', {
            method: 'POST',
            headers: getTablePostHeaders(),
            credentials: 'same-origin',
            body: JSON.stringify({
                shiftTypeId: parseInt(shiftTypeId),
                date: date,
                userId: parseInt(assigneeId),
                shiftInstanceId: shiftInstanceId ? parseInt(shiftInstanceId) : null,
                // Draft Mode (Epic 4): when set, the assignment is staged into the private sandbox.
                draftSessionId: window.__draftSessionId || null
            })
        });

        if (!response.ok) {
            const culture = getCurrentCulture();
            const errorMsg = culture === 'he-IL' ? 'שגיאה בשיבוץ עובד' : 'Error assigning employee';
            showToast(errorMsg, 'error');
            return;
        }

        const result = await response.json();

        if (result.success) {
            const culture = getCurrentCulture();
            const successMsg = culture === 'he-IL' ? 'שיבוץ בוצע בהצלחה' : 'Assignment created successfully';
            showToast(result.message || successMsg, 'success');
            triggerCalendarRefresh();
        } else if (!shiftInstanceId && (
                   (result.error && result.error.indexOf('SHIFT_FULLY_STAFFED') !== -1) ||
                   (result.errorKey === 'SHIFT_FULLY_STAFFED' && !_retried))) {
            // Only auto-expand capacity on the legacy ShiftType+Date path. When a specific
            // instance was named (Justice make-it-real), don't grow an arbitrary re-resolved
            // instance — surface the error instead (a "hole" shouldn't be fully staffed anyway).
            // Shift is at capacity — ask the user if they want to expand it
            const culture = getCurrentCulture();
            const confirmMsg = culture === 'he-IL'
                ? 'המשמרת מלאה. להגדיל את התקן ולשבץ?'
                : 'Shift is fully staffed. Increase capacity and assign?';
            if (await confirmHandler(confirmMsg)) {
                await expandCapacityAndRetry(shiftTypeId, date, assigneeId, confirmHandler);
            }
        } else if (result.requiresOverride) {
            // Warnings require override — pass the structured array so FeedbackModal.confirm
            // renders one row per conflict with category-colored badge instead of \n-joined text.
            if (await confirmHandler(result.warnings || [])) {
                // Retry with override token
                const retryResponse = await fetch('/Calendar/Table?handler=AssignEmployee', {
                    method: 'POST',
                    headers: getTablePostHeaders(),
                    credentials: 'same-origin',
                    body: JSON.stringify({
                        shiftTypeId: parseInt(shiftTypeId),
                        date: date,
                        userId: parseInt(assigneeId),
                        shiftInstanceId: shiftInstanceId ? parseInt(shiftInstanceId) : null,
                        overrideToken: result.overrideToken
                    })
                });
                const retryResult = await retryResponse.json();
                if (retryResult.success) {
                    const retryCulture = getCurrentCulture();
                    const successMsg = retryCulture === 'he-IL' ? 'שיבוץ בוצע בהצלחה' : 'Assignment created successfully';
                    showToast(retryResult.message || successMsg, 'success');
                    triggerCalendarRefresh();
                } else {
                    showToast(retryResult.message || retryResult.error || 'Error', 'error');
                }
            }
        } else {
            showToast(result.message || result.error || 'Error', 'error');
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

async function expandCapacityAndRetry(shiftTypeId, date, assigneeId, confirmHandler = defaultConfirm) {
    try {
        // First, find or create the shift instance to get its ID and current staffing
        const lookupResponse = await fetch('/Calendar/Table?handler=EnsureShiftInstance', {
            method: 'POST',
            headers: getTablePostHeaders(),
            credentials: 'same-origin',
            body: JSON.stringify({
                shiftTypeId: parseInt(shiftTypeId),
                date: date,
                staffingRequired: 1
            })
        });
        const lookupResult = await lookupResponse.json();
        if (!lookupResult.success || !lookupResult.instanceId) {
            showToast(lookupResult.error || 'Could not find shift instance', 'error');
            return;
        }

        // Increase staffing by 1
        const newCapacity = (lookupResult.staffingRequired || 1) + 1;
        const updateResponse = await fetch('/Calendar/Table?handler=UpdateShiftStaffing', {
            method: 'POST',
            headers: getTablePostHeaders(),
            credentials: 'same-origin',
            body: JSON.stringify({
                shiftInstanceId: lookupResult.instanceId,
                staffingRequired: newCapacity
            })
        });
        const updateResult = await updateResponse.json();
        if (!updateResult.success) {
            showToast(updateResult.error || 'Could not update capacity', 'error');
            return;
        }

        // Now retry the assignment
        await quickAddShift(shiftTypeId, date, assigneeId, confirmHandler, true);
    } catch (error) {
        handleApiError(null, error);
    }
}

// Expose quickAddShift globally for bottom sheet integration
window.quickAddShift = quickAddShift;

/**
 * Quick-add a text entry (free-text calendar annotation)
 * @param {string} date - Date in yyyy-MM-dd format
 * @param {number} userId - User ID for the text entry
 * @param {string} text - Free-text content
 */
async function quickAddTextEntry(date, userId, text) {
    try {
        const response = await fetch('/Api/Calendar/QuickAddTextEntry', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify({
                date: date,
                userId: parseInt(userId),
                text: text.trim()
            })
        });

        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                handleApiError(response);
                return;
            }
            showToast(getErrorMessage('serverError'), 'error');
            return;
        }

        const result = await response.json();
        if (result.success) {
            var msg = window.AppLocalizer?.QuickEntry_TextSaved || 'Text saved successfully';
            showToast(msg, 'success');
            triggerCalendarRefresh();
        } else {
            showToast(result.message || 'Error', 'error');
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

window.quickAddTextEntry = quickAddTextEntry;

/**
 * Save (or clear, when text is empty) a day-scoped note via Quick Entry.
 * Unlike quickAddTextEntry, this attaches to the DATE (company-wide), not a specific user,
 * so it works in any calendar view (shift-mode or user-mode).
 * @param {string} date - ISO date (YYYY-MM-DD)
 * @param {string} text - Free-text content; empty clears the day note
 */
async function quickAddDayNote(date, text) {
    try {
        const response = await fetch('/Api/Calendar/QuickAddDayNote', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify({
                date: date,
                text: (text || '').trim()
            })
        });

        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                handleApiError(response);
                return;
            }
            showToast(getErrorMessage('serverError'), 'error');
            return;
        }

        const result = await response.json();
        if (result.success) {
            var isDelete = !(text || '').trim();
            var msg = isDelete
                ? (window.AppLocalizer?.QuickEntry_DayNoteDeleted || 'Note deleted')
                : (window.AppLocalizer?.QuickEntry_DayNoteSaved || 'Note saved');
            showToast(msg, 'success');
            triggerCalendarRefresh();
        } else {
            showToast(result.message || 'Error', 'error');
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

window.quickAddDayNote = quickAddDayNote;

/**
 * Delete a day-scoped note by upserting empty text (the endpoint treats empty text as a delete).
 * @param {string} date - ISO date (YYYY-MM-DD)
 */
async function deleteDayNote(date) {
    if (!date) return;
    await quickAddDayNote(date, '');
}

window.deleteDayNote = deleteDayNote;

// Delegated so it survives calendar re-renders: the header day-note × removes the note.
document.addEventListener('click', function (e) {
    var btn = e.target.closest('.excel-calendar__day-note-delete');
    if (!btn) return;
    e.preventDefault();
    e.stopPropagation();
    deleteDayNote(btn.dataset.date);
});

/**
 * Issue 4: inline "+" trainee picker on a shift chip. Reuses the page's hidden #traineeSelect
 * (the same source the mobile bottom-sheet uses) and the /Calendar/Table?handler=AddTrainee endpoint.
 */
function openInlineTraineePicker(btn) {
    var chip = btn.closest('.excel-calendar__assignment');
    if (!chip) return;
    // Toggle: a second click removes an open picker.
    var existing = chip.parentNode && chip.parentNode.querySelector('.excel-calendar__trainee-picker');
    if (existing) { existing.remove(); return; }

    var source = document.getElementById('traineeSelect');
    if (!source || source.options.length === 0) {
        showToast(window.AppLocalizer?.Calendar_NoTraineesAvailable || 'No trainees available', 'info');
        return;
    }

    var assignmentId = parseInt(btn.dataset.assignmentId, 10);
    if (isNaN(assignmentId)) return;

    var select = document.createElement('select');
    select.className = 'excel-calendar__trainee-picker';
    var def = document.createElement('option');
    def.value = '';
    def.textContent = window.AppLocalizer?.BottomSheet_AddTrainee || 'Add trainee...';
    select.appendChild(def);
    for (var i = 0; i < source.options.length; i++) {
        var o = document.createElement('option');
        o.value = source.options[i].value;
        o.textContent = source.options[i].textContent;
        select.appendChild(o);
    }
    select.addEventListener('change', function () {
        var traineeId = parseInt(select.value, 10);
        if (select.value && !isNaN(traineeId)) {
            select.disabled = true;
            addTraineeToAssignment(assignmentId, traineeId);
        }
    });
    // Dismiss on Escape or when focus leaves.
    select.addEventListener('keydown', function (ev) { if (ev.key === 'Escape') select.remove(); });
    select.addEventListener('blur', function () { setTimeout(function () { if (select.parentNode) select.remove(); }, 150); });

    // Insert after the chip (not inside — keeps the chip layout intact).
    chip.insertAdjacentElement('afterend', select);
    select.focus();
}

function addTraineeToAssignment(assignmentId, traineeUserId, overrideToken) {
    var body = { assignmentId: assignmentId, traineeUserId: traineeUserId };
    if (overrideToken) body.overrideToken = overrideToken;
    fetch('/Calendar/Table?handler=AddTrainee', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        credentials: 'same-origin',
        body: JSON.stringify(body)
    })
    .then(function (r) { return r.json(); })
    .then(function (result) {
        if (result.success) {
            showToast(window.AppLocalizer?.BottomSheet_TraineeAssigned || 'Trainee assigned', 'success');
            triggerCalendarRefresh();
        } else if (result.requiresOverride) {
            var confirmFn = (window.FeedbackModal && typeof window.FeedbackModal.confirm === 'function')
                ? function () { return window.FeedbackModal.confirm('warning', { warnings: result.warnings || [] }); }
                : function () { return Promise.resolve(confirm((result.warnings || []).map(function (w) { return w.message; }).join('\n'))); };
            confirmFn().then(function (proceed) {
                if (proceed) addTraineeToAssignment(assignmentId, traineeUserId, result.overrideToken);
            });
        } else {
            showToast(result.error || getErrorMessage('serverError'), 'error');
        }
    })
    .catch(function (error) { handleApiError(null, error); });
}

// Delegated so it survives calendar re-renders: the chip "+" opens the inline trainee picker.
document.addEventListener('click', function (e) {
    var btn = e.target.closest('.excel-calendar__add-trainee-btn');
    if (!btn) return;
    e.preventDefault();
    e.stopPropagation();
    openInlineTraineePicker(btn);
});

/**
 * Delete a text entry
 * @param {number} id - CalendarTextEntry ID
 */
async function deleteTextEntry(id) {
    try {
        const response = await fetch('/Api/Calendar/DeleteTextEntry', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify({ id: parseInt(id) })
        });

        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                handleApiError(response);
                return;
            }
            showToast(getErrorMessage('serverError'), 'error');
            return;
        }

        const result = await response.json();
        if (result.success) {
            var msg = window.AppLocalizer?.QuickEntry_TextDeleted || 'Text entry deleted';
            showToast(msg, 'success');
            triggerCalendarRefresh();
        } else {
            showToast(result.message || 'Error', 'error');
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

window.deleteTextEntry = deleteTextEntry;

/**
 * Delete an item (chore or on-duty) with undo toast
 * @param {string} itemType - 'chore' or 'onduty'
 * @param {number} itemId - Entity ID to delete
 */
async function deleteItem(itemType, itemId) {
    try {
        const endpoint = itemType === 'chore'
            ? '/Api/Calendar/DeleteChore'
            : '/Api/Calendar/DeleteOnDuty';

        const response = await fetch(endpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify({ id: parseInt(itemId) })
        });

        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                handleApiError(response);
                return;
            }
            showToast(window.AppLocalizer?.ErrorDeletingItem || 'Error deleting item', 'error');
            return;
        }

        const result = await response.json();

        if (result.success) {
            // Hide the deleted element visually
            var deletedEl = document.querySelector(`[data-${itemType}-id="${itemId}"]`) ||
                            document.querySelector(`[data-assignment-id="${itemId}"]`);
            if (deletedEl) {
                deletedEl.style.opacity = '0.3';
                deletedEl.style.textDecoration = 'line-through';
            }

            // Show undo toast (chores support undo via restore, on-duty uses timed toast)
            if (itemType === 'chore') {
                showUndoToast(itemId, 'chore');
            } else if (itemType === 'onduty') {
                showUndoToast(itemId, 'onduty');
            } else {
                showToast(result.message || window.AppLocalizer?.ItemDeletedSuccessfully || 'Deleted', 'success');
                triggerCalendarRefresh();
            }
        } else {
            showToast(result.message || window.AppLocalizer?.ErrorDeletingItem || 'Error', 'error');
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

/**
 * Show an undo toast with countdown for item deletion
 * @param {number} itemId - The deleted item ID
 * @param {string} itemType - 'chore' or 'onduty'
 */
function showUndoToast(itemId, itemType) {
    // Remove any existing undo toasts
    document.querySelectorAll('.toast-undo').forEach(t => t.remove());

    var culture = getCurrentCulture();
    var undoLabel = culture === 'he-IL' ? 'בטל' : 'Undo';
    var deletedLabel = itemType === 'onduty'
        ? (culture === 'he-IL' ? 'התורנות נמחקה.' : 'On-duty deleted.')
        : (culture === 'he-IL' ? 'התורנות נמחקה.' : 'Chore deleted.');

    // On-duty has no server-side restore, so no undo button
    var hasUndo = (itemType === 'chore');

    var toast = document.createElement('div');
    toast.className = 'toast toast-undo show';
    var html = '<span class="toast-undo__text">' + deletedLabel + '</span>';
    if (hasUndo) {
        html += '<button type="button" class="toast-undo__btn" data-item-id="' + itemId + '">' + undoLabel + '</button>';
    }
    html += '<span class="toast-undo__timer">5</span>';
    toast.innerHTML = html;

    document.body.appendChild(toast);

    var seconds = 5;
    var timerEl = toast.querySelector('.toast-undo__timer');
    var undoBtn = hasUndo ? toast.querySelector('.toast-undo__btn') : null;
    var undone = false;

    var countdown = setInterval(function() {
        seconds--;
        if (timerEl) timerEl.textContent = seconds;
        if (seconds <= 0) {
            clearInterval(countdown);
            if (!undone) {
                toast.classList.remove('show');
                setTimeout(function() { toast.remove(); triggerCalendarRefresh(); }, 300);
            }
        }
    }, 1000);

    if (undoBtn) {
        undoBtn.addEventListener('click', async function() {
            undone = true;
            clearInterval(countdown);
            undoBtn.disabled = true;
            undoBtn.textContent = '...';

            try {
                var response = await fetch('/Api/Calendar/RestoreChore', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-Requested-With': 'XMLHttpRequest'
                    },
                    credentials: 'same-origin',
                    body: JSON.stringify({ id: parseInt(itemId) })
                });
                if (!response.ok) {
                    throw new Error('Request failed: ' + response.status);
                }
                var data = await response.json();
                if (data.success) {
                    toast.remove();
                    triggerCalendarRefresh();
                } else {
                    showToast(data.message || window.AppLocalizer?.InlineEdit_CouldNotUndo || 'Could not undo', 'error');
                    toast.remove();
                }
            } catch (err) {
                showToast(window.AppLocalizer?.InlineEdit_CouldNotUndo || 'Could not undo', 'error');
                toast.remove();
            }
        });
    }
}

/**
 * Show a toast notification (B-001-EXT: Respects prefers-reduced-motion)
 * @param {string} message - Message to display
 * @param {string} type - 'success' or 'error'
 */
function showToast(message, type = 'success') {
    // Remove any existing toasts
    const existingToasts = document.querySelectorAll('.toast');
    existingToasts.forEach(toast => toast.remove());

    // Check for reduced motion preference
    const reducedMotion = window.ReducedMotion && window.ReducedMotion.isEnabled();
    const animationTime = reducedMotion ? 0 : 300;

    // Create new toast
    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;
    toast.textContent = message;

    // Add to document
    document.body.appendChild(toast);

    // Trigger animation (skip delay if reduced motion)
    if (reducedMotion) {
        toast.classList.add('show');
    } else {
        setTimeout(() => toast.classList.add('show'), 10);
    }

    // Remove after 3 seconds
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), animationTime);
    }, 3000);
}

/**
 * Toggle quick-add form visibility
 * @param {string} type - 'chore' or 'onduty' or empty string to hide
 * @param {string} date - Date in yyyy-MM-dd format
 */
function toggleQuickAdd(type, date) {
    const form = document.getElementById(`quickAddForm-${date}`);
    if (!form) return;

    const choreFields = document.getElementById(`choreFields-${date}`);
    const ondutyFields = document.getElementById(`ondutyFields-${date}`);

    if (type === 'chore') {
        form.classList.remove('hidden');
        if (choreFields) choreFields.classList.remove('hidden');
        if (ondutyFields) ondutyFields.classList.add('hidden');
        form.dataset.type = 'chore';

        // Focus on the title input
        setTimeout(() => {
            const titleInput = document.getElementById(`choreTitle-${date}`);
            if (titleInput) titleInput.focus();
        }, 100);
    } else if (type === 'onduty') {
        form.classList.remove('hidden');
        if (choreFields) choreFields.classList.add('hidden');
        if (ondutyFields) ondutyFields.classList.remove('hidden');
        form.dataset.type = 'onduty';
    } else {
        // Hide form
        form.classList.add('hidden');
        if (choreFields) choreFields.classList.add('hidden');
        if (ondutyFields) ondutyFields.classList.add('hidden');

        // Reset fields
        const assigneeSelect = document.getElementById(`assigneeSelect-${date}`);
        const titleInput = document.getElementById(`choreTitle-${date}`);
        const typeSelect = document.getElementById(`ondutyType-${date}`);

        if (assigneeSelect) assigneeSelect.value = '';
        if (titleInput) titleInput.value = '';
        if (typeSelect) typeSelect.value = '0';
    }
}

/**
 * Submit quick-add form
 * @param {string} date - Date in yyyy-MM-dd format
 */
async function submitQuickAdd(date) {
    const form = document.getElementById(`quickAddForm-${date}`);
    if (!form) return;

    const type = form.dataset.type;
    const assigneeSelect = document.getElementById(`assigneeSelect-${date}`);
    const assigneeId = assigneeSelect ? assigneeSelect.value : '';

    // Validate assignee
    if (!assigneeId) {
        window.FeedbackModal.show('warning', window.AppLocalizer.PleaseSelectAssignee);
        return;
    }

    if (type === 'chore') {
        const titleInput = document.getElementById(`choreTitle-${date}`);
        const title = titleInput ? titleInput.value.trim() : '';

        // Validate title
        if (!title) {
            window.FeedbackModal.show('warning', window.AppLocalizer.PleaseEnterTitle);
            return;
        }

        if (title.length > 200) {
            window.FeedbackModal.show('warning', window.AppLocalizer.TitleMaxLengthExceeded);
            return;
        }

        var choreTypeSelect = document.getElementById('choreTypeSelect');
        var choreTypeId = choreTypeSelect ? (choreTypeSelect.value || null) : null;
        await quickAddChore(date, assigneeId, title, choreTypeId);
    } else if (type === 'onduty') {
        const typeSelect = document.getElementById(`ondutyType-${date}`);
        const onDutyType = typeSelect ? typeSelect.value : '0';

        await quickAddOnDuty(date, assigneeId, onDutyType);
    }
}

// --- Desktop assignment removal (hover × button) ---
(function initDesktopRemoveButtons() {
    var culture = document.documentElement.lang || 'en';
    var isHebrew = culture === 'he' || culture === 'he-IL';

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.excel-calendar__remove-btn');
        if (!btn) return;

        e.stopPropagation(); // Don't trigger cell click / bottom sheet

        // Second click — confirmed
        if (btn.dataset.confirming === 'true') {
            if (btn._confirmTimeout) clearTimeout(btn._confirmTimeout);
            executeRemoval(btn);
            return;
        }

        // First click — enter confirm state
        btn.dataset.confirming = 'true';
        btn._originalHtml = btn.innerHTML;
        btn.innerHTML = Icons.render('check', { size: 16 });
        btn.classList.add('excel-calendar__remove-btn--confirming');
        btn.setAttribute('title', isHebrew ? 'לחץ שוב לאישור' : 'Click again to confirm');

        btn._confirmTimeout = setTimeout(function () {
            btn.dataset.confirming = '';
            btn.innerHTML = btn._originalHtml;
            btn.classList.remove('excel-calendar__remove-btn--confirming');
            btn.setAttribute('title', isHebrew ? 'הסר' : 'Remove');
        }, 3000);
    });

    function executeRemoval(btn) {
        var assignmentId = parseInt(btn.dataset.assignmentId, 10);
        if (isNaN(assignmentId)) return;

        var assignmentEl = btn.closest('.excel-calendar__assignment');

        // Text entries have data-entry-type="text" — route to dedicated handler
        if (assignmentEl && assignmentEl.dataset.entryType === 'text') {
            deleteTextEntry(assignmentId);
            return;
        }

        // Task 28: Vacation/After-derived HOME chips have data-source-request-id set.
        // Instead of deleting a single ShiftAssignment row, open a dialog that lets
        // the user cancel the entire request or shorten the date range.
        // Rotation HOME chips (no source-request-id) keep the existing single-row delete.
        var sourceRequestIdAttr = assignmentEl ? assignmentEl.dataset.sourceRequestId : '';
        var sourceRequestId = sourceRequestIdAttr ? parseInt(sourceRequestIdAttr, 10) : 0;
        if (sourceRequestId > 0) {
            if (typeof window.openCancelOrShortenDialog === 'function') {
                window.openCancelOrShortenDialog(sourceRequestId, assignmentId, btn);
                return;
            }
            // Dialog script not loaded — fall through to existing delete (best-effort)
        }

        var calendarType = detectCalendarTypeForRemoval();

        if (calendarType === 'chores') {
            deleteItem('chore', assignmentId);
        } else if (calendarType === 'oncall') {
            deleteItem('onduty', assignmentId);
        } else {
            // Shifts — in Draft Mode (Epic 4) the × stages a clear into the sandbox (by shiftType+date+user);
            // in live mode it clears the slot by assignment id.
            if (assignmentEl) {
                assignmentEl.style.opacity = '0.3';
                assignmentEl.style.pointerEvents = 'none';
            }

            var draftId = window.__draftSessionId;
            var shiftTypeIdAttr = btn.dataset.shiftTypeId || (assignmentEl && assignmentEl.dataset.shiftTypeId) || '';
            var userIdAttr = btn.dataset.userId || (assignmentEl && assignmentEl.dataset.userId) || '';
            var cellEl = assignmentEl ? assignmentEl.closest('.excel-calendar__cell') : null;
            var dateAttr = cellEl ? cellEl.dataset.date : null;
            var isDraftClear = draftId && shiftTypeIdAttr && userIdAttr && dateAttr;

            fetch(isDraftClear ? '/Calendar/Table?handler=DraftClear' : '/Calendar/Table?handler=ClearAssignment', {
                method: 'POST',
                headers: getTablePostHeaders(),
                credentials: 'same-origin',
                body: JSON.stringify(isDraftClear
                    ? { draftSessionId: parseInt(draftId, 10), shiftTypeId: parseInt(shiftTypeIdAttr, 10), date: dateAttr, userId: parseInt(userIdAttr, 10) }
                    : { assignmentId: assignmentId })
            })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result.success) {
                    showToast(isHebrew ? 'השיבוץ הוסר בהצלחה' : 'Assignment removed', 'success');
                    if (assignmentEl) {
                        assignmentEl.style.transition = 'opacity 0.3s, transform 0.3s';
                        assignmentEl.style.opacity = '0';
                        assignmentEl.style.transform = 'scale(0.8)';
                        setTimeout(function () { assignmentEl.remove(); }, 300);
                    }
                } else {
                    showToast(result.error || 'Error', 'error');
                    if (assignmentEl) {
                        assignmentEl.style.opacity = '';
                        assignmentEl.style.pointerEvents = '';
                    }
                }
            })
            .catch(function () {
                showToast(isHebrew ? 'שגיאת רשת' : 'Network error', 'error');
                if (assignmentEl) {
                    assignmentEl.style.opacity = '';
                    assignmentEl.style.pointerEvents = '';
                }
            });
        }
    }

    function detectCalendarTypeForRemoval() {
        var el = document.querySelector('[data-calendar-type]');
        if (el) return el.dataset.calendarType;
        var path = window.location.pathname.toLowerCase();
        if (path.includes('/chores')) return 'chores';
        if (path.includes('/oncall')) return 'oncall';
        return 'shifts';
    }
})();

// Make functions globally available
window.quickAddChore = quickAddChore;
window.quickAddOnDuty = quickAddOnDuty;
window.deleteItem = deleteItem;
window.showToast = showToast;
window.showUndoToast = showUndoToast;
window.toggleQuickAdd = toggleQuickAdd;
window.submitQuickAdd = submitQuickAdd;
