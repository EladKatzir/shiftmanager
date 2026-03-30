// ✅ PHASE 20: Calendar inline editing and quick-add utilities
// This file provides client-side functionality for creating and deleting chores and on-duty assignments
// directly from the calendar views (Month, Week, Day)

/**
 * Trigger an in-place calendar refresh instead of a full page reload.
 * Uses the CalendarRealtime shadow refresh mechanism when available,
 * which fetches fresh data via AJAX and updates the DOM in place.
 * Falls back to full page reload if CalendarRealtime is not initialized.
 */
function triggerCalendarRefresh() {
    // Full page reload after mutations — the shadow refresh (CalendarRealtime.refresh)
    // only handles partial cell updates via selectors that may not match the current DOM.
    // A full reload guarantees the user sees the new state after assign/unassign.
    location.reload();
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

// Default confirm handler — backward-compatible (bottom sheet still uses confirm())
const defaultConfirm = (msg) => Promise.resolve(confirm(msg));

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
 * Quick-add a chore
 * @param {string} date - Date in yyyy-MM-dd format
 * @param {number} assigneeId - User ID to assign the chore to
 * @param {string} title - Chore title
 * @param {boolean} forceAssign - Force assignment despite vacation conflict
 */
async function quickAddChore(date, assigneeId, title, forceAssign = false, choreTypeId = null, confirmHandler = defaultConfirm) {
    try {
        var requestBody = {
            date: date,
            assigneeId: parseInt(assigneeId),
            title: title.trim(),
            notes: null,
            forceAssign: forceAssign
        };
        if (choreTypeId != null) {
            requestBody.choreTypeId = parseInt(choreTypeId);
        }
        const response = await fetch('/Api/Calendar/QuickAddChore', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify(requestBody)
        });

        // Check for auth/permission errors before parsing JSON
        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                handleApiError(response);
                return;
            }

            // Handle 409 Conflict (shift conflict or vacation)
            if (response.status === 409) {
                const result = await response.json();

                // Check if it's a vacation conflict
                if (result.conflictType === 'vacation') {
                    // Step 1: Ask if user wants to create anyway
                    const culture = getCurrentCulture();
                    const confirmMessage = culture === 'he-IL'
                        ? 'למשתמש זה חופשה מאושרת בתאריך זה. האם ברצונך להקצות בכל זאת?'
                        : 'This user has an approved vacation on this date. Do you want to assign anyway?';

                    if (await confirmHandler(confirmMessage)) {
                        // Retry with forceAssign=true
                        var retryBody = {
                            date: date,
                            assigneeId: parseInt(assigneeId),
                            title: title.trim(),
                            notes: null,
                            forceAssign: true
                        };
                        if (choreTypeId != null) {
                            retryBody.choreTypeId = parseInt(choreTypeId);
                        }
                        const retryResponse = await fetch('/Api/Calendar/QuickAddChore', {
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json',
                                'X-Requested-With': 'XMLHttpRequest'
                            },
                            credentials: 'same-origin',
                            body: JSON.stringify(retryBody)
                        });

                        if (!retryResponse.ok) {
                            showToast(window.AppLocalizer.ErrorCreatingChore, 'error');
                            return;
                        }
                        const retryResult = await retryResponse.json();

                        if (retryResult.success) {
                            showToast(retryResult.message || window.AppLocalizer.ChoreCreatedSuccessfully, 'success');

                            // Step 2: Ask if user wants to manage the conflicting vacation
                            const manageMessage = culture === 'he-IL'
                                ? 'האם ברצונך לנהל את החופשה המתנגשת?'
                                : 'Do you want to manage the conflicting vacation?';

                            if (await confirmHandler(manageMessage)) {
                                window.location.href = '/Requests/Index#approved';
                            } else {
                                // Refresh calendar to show the new chore
                                triggerCalendarRefresh();
                            }
                        } else {
                            showToast(retryResult.message || window.AppLocalizer.ErrorCreatingChore, 'error');
                        }
                    }
                } else {
                    // Shift conflict or other conflict
                    showToast(result.message || window.AppLocalizer.ConflictDetected, 'error');
                }
                return;
            }

            // Other error (400, 500, etc.)
            showToast(window.AppLocalizer.ErrorCreatingChore, 'error');
            return;
        }

        const result = await response.json();

        if (result.success) {
            showToast(result.message || window.AppLocalizer.ChoreCreatedSuccessfully, 'success');
            // Refresh calendar in-place to show the new chore
            triggerCalendarRefresh();
        } else {
            showToast(result.message || window.AppLocalizer.ErrorCreatingChore, 'error');
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

/**
 * Quick-add an on-duty assignment
 * @param {string} date - Date in yyyy-MM-dd format
 * @param {number} assigneeId - User ID to assign
 * @param {number} onDutyType - OnDutyType enum value (0=Hakam, 1=Lead, 2+=Custom)
 * @param {boolean} forceAssign - Force assignment despite vacation conflict
 */
async function quickAddOnDuty(date, assigneeId, onDutyType, forceAssign = false, confirmHandler = defaultConfirm) {
    try {
        const response = await fetch('/Api/Calendar/QuickAddOnDuty', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify({
                date: date,
                assigneeId: parseInt(assigneeId),
                onDutyType: parseInt(onDutyType),
                notes: null,
                forceAssign: forceAssign
            })
        });

        // Check for auth/permission errors before parsing JSON
        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                // Check for specific error keys before generic handling
                try {
                    var errorResult = await response.clone().json();
                    if (errorResult.error === 'OFFICER_RANK_REQUIRED') {
                        var officerMsg = getCurrentCulture() === 'he-IL'
                            ? 'סוג תורנות זה דורש דרגת קצין'
                            : 'This duty type requires officer rank';
                        showToast(officerMsg, 'error');
                        return;
                    }
                } catch (e) { /* fall through to generic handler */ }
                handleApiError(response);
                return;
            }

            // Handle 409 Conflict (vacation conflict)
            if (response.status === 409) {
                const result = await response.json();

                // Check if it's a vacation conflict
                if (result.conflictType === 'vacation') {
                    // Step 1: Ask if user wants to create anyway
                    const culture = getCurrentCulture();
                    const confirmMessage = culture === 'he-IL'
                        ? 'למשתמש זה חופשה מאושרת בתאריך זה. האם ברצונך להקצות בכל זאת?'
                        : 'This user has an approved vacation on this date. Do you want to assign anyway?';

                    if (await confirmHandler(confirmMessage)) {
                        // Retry with forceAssign=true
                        const retryResponse = await fetch('/Api/Calendar/QuickAddOnDuty', {
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json',
                                'X-Requested-With': 'XMLHttpRequest'
                            },
                            credentials: 'same-origin',
                            body: JSON.stringify({
                                date: date,
                                assigneeId: parseInt(assigneeId),
                                onDutyType: parseInt(onDutyType),
                                notes: null,
                                forceAssign: true
                            })
                        });

                        if (!retryResponse.ok) {
                            showToast(window.AppLocalizer.ErrorCreatingOnDuty, 'error');
                            return;
                        }
                        const retryResult = await retryResponse.json();

                        if (retryResult.success) {
                            showToast(retryResult.message || window.AppLocalizer.OnDutyCreatedSuccessfully, 'success');

                            // Step 2: Ask if user wants to manage the conflicting vacation
                            const manageMessage = culture === 'he-IL'
                                ? 'האם ברצונך לנהל את החופשה המתנגשת?'
                                : 'Do you want to manage the conflicting vacation?';

                            if (await confirmHandler(manageMessage)) {
                                window.location.href = '/Requests/Index#approved';
                            } else {
                                // Refresh calendar to show the new on-duty assignment
                                triggerCalendarRefresh();
                            }
                        } else {
                            showToast(retryResult.message || window.AppLocalizer.ErrorCreatingOnDuty, 'error');
                        }
                    }
                } else {
                    // Other conflict
                    showToast(result.message || window.AppLocalizer.ConflictDetected, 'error');
                }
                return;
            }

            // Other error (400, 500, etc.)
            showToast(window.AppLocalizer.ErrorCreatingOnDuty, 'error');
            return;
        }

        const result = await response.json();

        if (result.success) {
            showToast(result.message || window.AppLocalizer.OnDutyCreatedSuccessfully, 'success');
            // Refresh calendar in-place to show the new on-duty assignment
            triggerCalendarRefresh();
        } else {
            showToast(result.message || window.AppLocalizer.ErrorCreatingOnDuty, 'error');
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
async function quickAddShift(shiftTypeId, date, assigneeId, confirmHandler = defaultConfirm, _retried) {
    try {
        const response = await fetch('/Calendar/Table?handler=AssignEmployee', {
            method: 'POST',
            headers: getTablePostHeaders(),
            credentials: 'same-origin',
            body: JSON.stringify({
                shiftTypeId: parseInt(shiftTypeId),
                date: date,
                userId: parseInt(assigneeId)
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
        } else if (result.error && result.error.indexOf('SHIFT_FULLY_STAFFED') !== -1 ||
                   (result.errorKey === 'SHIFT_FULLY_STAFFED' && !_retried)) {
            // Shift is at capacity — ask the user if they want to expand it
            const culture = getCurrentCulture();
            const confirmMsg = culture === 'he-IL'
                ? 'המשמרת מלאה. להגדיל את התקן ולשבץ?'
                : 'Shift is fully staffed. Increase capacity and assign?';
            if (await confirmHandler(confirmMsg)) {
                await expandCapacityAndRetry(shiftTypeId, date, assigneeId, confirmHandler);
            }
        } else if (result.requiresOverride) {
            // Warnings require override — show them and ask to confirm
            const msgs = (result.warnings || []).map(function(w) { return w.message; }).join('\n');
            const culture = getCurrentCulture();
            const confirmLabel = culture === 'he-IL' ? 'אישורים נדרשים:\n' : 'Warnings:\n';
            if (await confirmHandler(confirmLabel + msgs)) {
                // Retry with override token
                const retryResponse = await fetch('/Calendar/Table?handler=AssignEmployee', {
                    method: 'POST',
                    headers: getTablePostHeaders(),
                    credentials: 'same-origin',
                    body: JSON.stringify({
                        shiftTypeId: parseInt(shiftTypeId),
                        date: date,
                        userId: parseInt(assigneeId),
                        overrideToken: result.overrideToken
                    })
                });
                const retryResult = await retryResponse.json();
                if (retryResult.success) {
                    const successMsg = culture === 'he-IL' ? 'שיבוץ בוצע בהצלחה' : 'Assignment created successfully';
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
        alert(window.AppLocalizer.PleaseSelectAssignee);
        return;
    }

    if (type === 'chore') {
        const titleInput = document.getElementById(`choreTitle-${date}`);
        const title = titleInput ? titleInput.value.trim() : '';

        // Validate title
        if (!title) {
            alert(window.AppLocalizer.PleaseEnterTitle);
            return;
        }

        if (title.length > 200) {
            alert(window.AppLocalizer.TitleMaxLengthExceeded);
            return;
        }

        var choreTypeSelect = document.getElementById('choreTypeSelect');
        var choreTypeId = choreTypeSelect ? (choreTypeSelect.value || null) : null;
        await quickAddChore(date, assigneeId, title, false, choreTypeId);
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
        btn.innerHTML = '✓';
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

        var calendarType = detectCalendarTypeForRemoval();

        if (calendarType === 'chores') {
            deleteItem('chore', assignmentId);
        } else if (calendarType === 'oncall') {
            deleteItem('onduty', assignmentId);
        } else {
            // Shifts — call ClearAssignment
            if (assignmentEl) {
                assignmentEl.style.opacity = '0.3';
                assignmentEl.style.pointerEvents = 'none';
            }

            fetch('/Calendar/Table?handler=ClearAssignment', {
                method: 'POST',
                headers: getTablePostHeaders(),
                credentials: 'same-origin',
                body: JSON.stringify({ assignmentId: assignmentId })
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
