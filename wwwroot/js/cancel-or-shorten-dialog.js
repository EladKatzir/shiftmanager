// Task 28: Cancel-or-shorten dialog for vacation/after-derived HOME chips
//
// When the user clicks the × button on a HOME chip whose data-source-request-id is set,
// this dialog opens instead of deleting the single ShiftAssignment row. Choices:
//   * Cancel entire request → POST /Api/TimeOffRequest/{id}?handler=Cancel
//   * Shorten range (Vacation only) → POST /Api/TimeOffRequest/{id}?handler=UpdateDates
//   * Close
//
// Razor Pages route page handlers via ?handler= query string, NOT URL segments,
// so we use the query-string form throughout.
(function () {
    'use strict';

    var STYLE_ID = 'cancel-or-shorten-dialog-styles';

    function injectStylesOnce() {
        if (document.getElementById(STYLE_ID)) return;
        var style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent =
            '.cancel-or-shorten-dialog{position:fixed;inset:0;background:rgba(0,0,0,0.5);' +
            'z-index:1100;display:flex;align-items:center;justify-content:center;padding:1rem;}' +
            '.cancel-or-shorten-dialog .modal-content{background:var(--surface,#fff);' +
            'color:var(--text,#222);border-radius:8px;box-shadow:0 8px 32px rgba(0,0,0,.25);' +
            'padding:1.25rem;max-width:480px;width:100%;max-height:90vh;overflow:auto;}' +
            '.cancel-or-shorten-dialog h3{margin:0 0 .75rem 0;font-size:1.125rem;}' +
            '.cancel-or-shorten-dialog p{margin:0 0 1rem 0;line-height:1.4;}' +
            '.cancel-or-shorten-dialog .form-group{margin-bottom:.75rem;display:flex;' +
            'flex-direction:column;gap:.25rem;}' +
            '.cancel-or-shorten-dialog .form-group label{font-weight:600;font-size:.875rem;}' +
            '.cancel-or-shorten-dialog .form-group input[type="date"]{padding:.5rem;' +
            'border:1px solid var(--border,#ccc);border-radius:4px;font-size:.95rem;}' +
            '.cancel-or-shorten-dialog .modal-actions{display:flex;flex-wrap:wrap;gap:.5rem;' +
            'justify-content:flex-end;margin-top:1rem;}';
        document.head.appendChild(style);
    }

    function isHebrew() {
        var lang = document.documentElement.lang || 'en';
        return lang.indexOf('he') === 0;
    }

    function t(en, he) { return isHebrew() ? he : en; }

    function getCsrfToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function jsonHeaders() {
        return {
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': getCsrfToken()
        };
    }

    function postHeaders() {
        return {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': getCsrfToken()
        };
    }

    function escapeHtml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function refreshCalendar() {
        if (typeof window.triggerCalendarRefresh === 'function') {
            window.triggerCalendarRefresh();
        } else {
            location.reload();
        }
    }

    function showFeedback(message, type) {
        if (window.FeedbackModal && typeof window.FeedbackModal.show === 'function') {
            window.FeedbackModal.show(type || 'info', message);
        } else if (typeof window.showToast === 'function') {
            window.showToast(message, type || 'info');
        } else {
            alert(message);
        }
    }

    window.openCancelOrShortenDialog = async function (requestId, assignmentId, originBtn) {
        injectStylesOnce();

        // Fetch the request details (GET — no CSRF token strictly required, but header is harmless)
        var resp;
        try {
            resp = await fetch('/Api/TimeOffRequest/' + requestId, {
                method: 'GET',
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
        } catch (err) {
            showFeedback(t('Network error loading request', 'שגיאת רשת בטעינת הבקשה'), 'error');
            return;
        }
        if (!resp.ok) {
            showFeedback(t('Could not load request details', 'לא ניתן לטעון את פרטי הבקשה'), 'error');
            return;
        }

        var req;
        try { req = await resp.json(); }
        catch (e) {
            showFeedback(t('Invalid response loading request', 'תגובה לא תקינה בטעינת הבקשה'), 'error');
            return;
        }

        // Type comes back as the enum name string from C# (.ToString())
        var typeStr = req.type != null ? String(req.type) : '';
        var isVacation = typeStr === 'Vacation' || typeStr === '0' || req.type === 0;
        var canShorten = isVacation && req.startDate !== req.endDate;

        var dialog = document.createElement('div');
        dialog.className = 'cancel-or-shorten-dialog';
        dialog.setAttribute('role', 'dialog');
        dialog.setAttribute('aria-modal', 'true');

        var rangeText = isVacation && canShorten ? ', ' + req.startDate + ' → ' + req.endDate : '';
        var typeLabel = isVacation ? t('Vacation', 'חופש') : t('After', 'אחרי');

        var bodyEn = 'This day is part of approved request #' + req.id + ' (' + typeLabel + rangeText + '). What would you like to do?';
        var bodyHe = 'יום זה הוא חלק מבקשה מאושרת #' + req.id + ' (' + typeLabel + rangeText + '). מה ברצונך לעשות?';

        dialog.innerHTML =
            '<div class="modal-content">' +
                '<h3>' + escapeHtml(t('Approved time-off request', 'בקשת חופש מאושרת')) + '</h3>' +
                '<p>' + escapeHtml(t(bodyEn, bodyHe)) + '</p>' +
                '<div class="modal-actions">' +
                    '<button type="button" class="btn btn--danger" data-action="cancel">' +
                        escapeHtml(t('Cancel entire request', 'בטל את כל הבקשה')) +
                    '</button>' +
                    (canShorten
                        ? '<button type="button" class="btn btn--secondary" data-action="shorten">' +
                            escapeHtml(t('Shorten range', 'קצר את הטווח')) +
                          '</button>'
                        : '') +
                    '<button type="button" class="btn btn--link" data-action="close">' +
                        escapeHtml(t('Close', 'סגור')) +
                    '</button>' +
                '</div>' +
            '</div>';

        document.body.appendChild(dialog);

        dialog.addEventListener('click', async function (e) {
            // Backdrop click closes
            if (e.target === dialog) {
                dialog.remove();
                return;
            }

            var actionEl = e.target.closest('[data-action]');
            if (!actionEl) return;
            var action = actionEl.getAttribute('data-action');

            if (action === 'close') {
                dialog.remove();
                return;
            }

            if (action === 'cancel') {
                actionEl.disabled = true;
                var result = await cancelRequest(requestId);
                dialog.remove();
                if (result.success) {
                    showFeedback(t('Request cancelled', 'הבקשה בוטלה'), 'success');
                    refreshCalendar();
                } else {
                    showFeedback(result.message || t('Cancel failed', 'הביטול נכשל'), 'error');
                }
                return;
            }

            if (action === 'shorten') {
                renderShortenForm(dialog, req, requestId);
                return;
            }
        });

        // Esc closes
        var escHandler = function (e) {
            if (e.key === 'Escape') {
                document.removeEventListener('keydown', escHandler);
                if (dialog.parentNode) dialog.remove();
            }
        };
        document.addEventListener('keydown', escHandler);
    };

    function renderShortenForm(parent, req, requestId) {
        var content = parent.querySelector('.modal-content');
        if (!content) return;
        content.innerHTML =
            '<h3>' + escapeHtml(t('Shorten request', 'קצר בקשה') + ' #' + req.id) + '</h3>' +
            '<div class="form-group">' +
                '<label for="cs-new-start">' + escapeHtml(t('New start', 'תחילה חדשה')) + '</label>' +
                '<input type="date" id="cs-new-start" value="' + escapeHtml(req.startDate) + '" min="' + escapeHtml(req.startDate) + '" max="' + escapeHtml(req.endDate) + '" />' +
            '</div>' +
            '<div class="form-group">' +
                '<label for="cs-new-end">' + escapeHtml(t('New end', 'סיום חדש')) + '</label>' +
                '<input type="date" id="cs-new-end" value="' + escapeHtml(req.endDate) + '" min="' + escapeHtml(req.startDate) + '" max="' + escapeHtml(req.endDate) + '" />' +
            '</div>' +
            '<div class="modal-actions">' +
                '<button type="button" class="btn btn--primary" id="cs-apply">' + escapeHtml(t('Apply', 'החל')) + '</button>' +
                '<button type="button" class="btn btn--link" id="cs-cancel">' + escapeHtml(t('Cancel', 'בטל')) + '</button>' +
            '</div>';

        content.querySelector('#cs-apply').addEventListener('click', async function () {
            var startEl = content.querySelector('#cs-new-start');
            var endEl = content.querySelector('#cs-new-end');
            var newStart = startEl ? startEl.value : '';
            var newEnd = endEl ? endEl.value : '';
            if (!newStart || !newEnd) {
                showFeedback(t('Please pick both dates', 'יש לבחור את שני התאריכים'), 'warning');
                return;
            }
            if (newStart > newEnd) {
                showFeedback(t('Start must be before end', 'תחילה חייבת להיות לפני הסיום'), 'warning');
                return;
            }
            var applyBtn = content.querySelector('#cs-apply');
            if (applyBtn) applyBtn.disabled = true;
            var result = await updateDates(requestId, newStart, newEnd);
            parent.remove();
            if (result.success) {
                showFeedback(t('Request shortened', 'הבקשה קוצרה'), 'success');
                refreshCalendar();
            } else {
                showFeedback(result.message || t('Update failed', 'העדכון נכשל'), 'error');
            }
        });

        content.querySelector('#cs-cancel').addEventListener('click', function () {
            parent.remove();
        });
    }

    async function cancelRequest(requestId) {
        try {
            var resp = await fetch('/Api/TimeOffRequest/' + requestId + '?handler=Cancel', {
                method: 'POST',
                credentials: 'same-origin',
                headers: postHeaders()
            });
            if (!resp.ok) {
                return { success: false, message: 'HTTP ' + resp.status };
            }
            return await resp.json();
        } catch (err) {
            return { success: false, message: 'Network error' };
        }
    }

    async function updateDates(requestId, newStart, newEnd) {
        try {
            var resp = await fetch('/Api/TimeOffRequest/' + requestId + '?handler=UpdateDates', {
                method: 'POST',
                credentials: 'same-origin',
                headers: jsonHeaders(),
                body: JSON.stringify({ newStart: newStart, newEnd: newEnd })
            });
            if (!resp.ok) {
                return { success: false, message: 'HTTP ' + resp.status };
            }
            return await resp.json();
        } catch (err) {
            return { success: false, message: 'Network error' };
        }
    }
})();
