// ✅ PHASE 20: Calendar inline editing and quick-add utilities
// This file provides client-side functionality for creating and deleting chores and on-duty assignments
// directly from the calendar views (Month, Week, Day)

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
        console.error('API Error:', error);
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
async function quickAddChore(date, assigneeId, title, forceAssign = false) {
    try {
        const response = await fetch('/Api/Calendar/QuickAddChore', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin',
            body: JSON.stringify({
                date: date,
                assigneeId: parseInt(assigneeId),
                title: title.trim(),
                notes: null,
                forceAssign: forceAssign
            })
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

                    if (confirm(confirmMessage)) {
                        // Retry with forceAssign=true
                        const retryResponse = await fetch('/Api/Calendar/QuickAddChore', {
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json',
                                'X-Requested-With': 'XMLHttpRequest'
                            },
                            credentials: 'same-origin',
                            body: JSON.stringify({
                                date: date,
                                assigneeId: parseInt(assigneeId),
                                title: title.trim(),
                                notes: null,
                                forceAssign: true
                            })
                        });

                        const retryResult = await retryResponse.json();

                        if (retryResult.success) {
                            showToast(retryResult.message || window.AppLocalizer.ChoreCreatedSuccessfully, 'success');

                            // Step 2: Ask if user wants to manage the conflicting vacation
                            const manageMessage = culture === 'he-IL'
                                ? 'האם ברצונך לנהל את החופשה המתנגשת?'
                                : 'Do you want to manage the conflicting vacation?';

                            if (confirm(manageMessage)) {
                                window.location.href = '/Requests/Index#approved';
                            } else {
                                // Just reload to show the new chore
                                setTimeout(() => location.reload(), 500);
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
        }

        const result = await response.json();

        if (result.success) {
            showToast(result.message || window.AppLocalizer.ChoreCreatedSuccessfully, 'success');
            // Reload the page to show the new chore
            setTimeout(() => location.reload(), 500);
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
async function quickAddOnDuty(date, assigneeId, onDutyType, forceAssign = false) {
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

                    if (confirm(confirmMessage)) {
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

                        const retryResult = await retryResponse.json();

                        if (retryResult.success) {
                            showToast(retryResult.message || window.AppLocalizer.OnDutyCreatedSuccessfully, 'success');

                            // Step 2: Ask if user wants to manage the conflicting vacation
                            const manageMessage = culture === 'he-IL'
                                ? 'האם ברצונך לנהל את החופשה המתנגשת?'
                                : 'Do you want to manage the conflicting vacation?';

                            if (confirm(manageMessage)) {
                                window.location.href = '/Requests/Index#approved';
                            } else {
                                // Just reload to show the new on-duty assignment
                                setTimeout(() => location.reload(), 500);
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
        }

        const result = await response.json();

        if (result.success) {
            showToast(result.message || window.AppLocalizer.OnDutyCreatedSuccessfully, 'success');
            // Reload the page to show the new on-duty assignment
            setTimeout(() => location.reload(), 500);
        } else {
            showToast(result.message || window.AppLocalizer.ErrorCreatingOnDuty, 'error');
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

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

            // Show undo toast (chores support undo via restore)
            if (itemType === 'chore') {
                showUndoToast(itemId);
            } else {
                showToast(result.message || window.AppLocalizer?.ItemDeletedSuccessfully || 'Deleted', 'success');
                setTimeout(() => location.reload(), 1500);
            }
        } else {
            showToast(result.message || window.AppLocalizer?.ErrorDeletingItem || 'Error', 'error');
        }
    } catch (error) {
        handleApiError(null, error);
    }
}

/**
 * Show an undo toast with countdown for chore deletion
 * @param {number} choreId - The deleted chore ID
 */
function showUndoToast(choreId) {
    // Remove any existing undo toasts
    document.querySelectorAll('.toast-undo').forEach(t => t.remove());

    var culture = getCurrentCulture();
    var undoLabel = culture === 'he-IL' ? 'בטל' : 'Undo';
    var deletedLabel = culture === 'he-IL' ? 'התורנות נמחקה.' : 'Item deleted.';

    var toast = document.createElement('div');
    toast.className = 'toast toast-undo show';
    toast.innerHTML =
        '<span class="toast-undo__text">' + deletedLabel + '</span>' +
        '<button type="button" class="toast-undo__btn" data-chore-id="' + choreId + '">' + undoLabel + '</button>' +
        '<span class="toast-undo__timer">5</span>';

    document.body.appendChild(toast);

    var seconds = 5;
    var timerEl = toast.querySelector('.toast-undo__timer');
    var undoBtn = toast.querySelector('.toast-undo__btn');
    var undone = false;

    var countdown = setInterval(function() {
        seconds--;
        if (timerEl) timerEl.textContent = seconds;
        if (seconds <= 0) {
            clearInterval(countdown);
            if (!undone) {
                toast.classList.remove('show');
                setTimeout(function() { toast.remove(); location.reload(); }, 300);
            }
        }
    }, 1000);

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
                body: JSON.stringify({ id: parseInt(choreId) })
            });
            var data = await response.json();
            if (data.success) {
                toast.remove();
                location.reload();
            } else {
                showToast(data.message || 'Could not undo', 'error');
                toast.remove();
            }
        } catch (err) {
            showToast('Could not undo', 'error');
            toast.remove();
        }
    });
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

        await quickAddChore(date, assigneeId, title);
    } else if (type === 'onduty') {
        const typeSelect = document.getElementById(`ondutyType-${date}`);
        const onDutyType = typeSelect ? typeSelect.value : '0';

        await quickAddOnDuty(date, assigneeId, onDutyType);
    }
}

// Make functions globally available
window.quickAddChore = quickAddChore;
window.quickAddOnDuty = quickAddOnDuty;
window.deleteItem = deleteItem;
window.showToast = showToast;
window.showUndoToast = showUndoToast;
window.toggleQuickAdd = toggleQuickAdd;
window.submitQuickAdd = submitQuickAdd;
