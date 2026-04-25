/**
 * Toast Notifications - unified on the toast-undo visual pattern.
 *
 * All variants (success / info / warning / error) render the same DOM shape:
 *   <div class="toast toast-undo toast-undo--{variant} show" role="status|alert">
 *     <span class="toast-undo__text">...</span>
 *     <span class="toast-undo__error-id">abc123</span>          (optional)
 *     <button type="button" class="toast-undo__btn" data-action="dismiss">Dismiss</button>
 *     <span class="toast-undo__timer">5</span>
 *   </div>
 *
 * Usage:
 *   Toast.success('Shift saved successfully');
 *   Toast.error('Could not reach the server');
 *   Toast.error('Could not reach the server', 'Network');           // legacy: title as 2nd arg
 *   Toast.error('Could not reach the server', { errorId: 'abc' });  // new: options object as 2nd arg
 *   Toast.error('Could not reach the server', { title: 'Network', errorId: 'abc' });
 *   Toast.info('Processing your request...');
 *   Toast.warning('Heads up.');
 */

(function() {
    'use strict';

    const CONFIG = {
        autoDismissMs: 5000,
        maxVisible: 1,
        animationDurationMs: 300,
        position: 'top-right'
    };

    // Severity-based auto-dismiss timing. 0 = manual dismiss only.
    const AUTO_DISMISS_BY_TYPE = {
        success: 5000,
        info:    10000,
        warning: 15000,
        error:   0
    };

    const toastQueue = [];
    let activeToast = null;
    let toastContainer = null;

    const MESSAGES = {
        'en-US': {
            dismiss: 'Dismiss',
            close: 'Close notification',
            errorId: 'Error ID'
        },
        'he-IL': {
            dismiss: 'סגור',
            close: 'סגור התראה',
            errorId: 'מזהה שגיאה'
        }
    };

    function getCurrentCulture() {
        const htmlLang = document.documentElement.lang || 'en-US';
        return htmlLang.startsWith('he') ? 'he-IL' : 'en-US';
    }

    function getMessage(key) {
        const culture = getCurrentCulture();
        return MESSAGES[culture]?.[key] || MESSAGES['en-US'][key] || key;
    }

    function prefersReducedMotion() {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function getToastContainer() {
        if (!toastContainer || !document.body.contains(toastContainer)) {
            const existing = document.querySelector('.toast-container');
            if (existing) {
                toastContainer = existing;
            } else {
                toastContainer = document.createElement('div');
                toastContainer.className = 'toast-container';
                toastContainer.setAttribute('role', 'region');
                toastContainer.setAttribute('aria-label', 'Notifications');
                toastContainer.setAttribute('aria-live', 'polite');
                document.body.appendChild(toastContainer);
            }
        }
        return toastContainer;
    }

    function escapeHtml(str) {
        if (typeof str !== 'string') return str;
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML.replace(/'/g, '&#39;');
    }

    // Map 'error' -> 'danger' for the CSS modifier, keep others as-is.
    function variantClass(type) {
        return type === 'error' ? 'toast-undo--danger' : `toast-undo--${type}`;
    }

    function createToastElement(options) {
        const { type, message, title, errorId } = options;
        const toastId = `toast-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

        const dismissMs = AUTO_DISMISS_BY_TYPE[type] ?? CONFIG.autoDismissMs;
        const hasTimer = dismissMs > 0;
        const initialSeconds = hasTimer ? Math.ceil(dismissMs / 1000) : 0;

        // Merge title+message into a single text span - toast-undo has only one text slot.
        const text = title ? `${title}: ${message}` : message;

        const toast = document.createElement('div');
        toast.id = toastId;
        toast.className = `toast toast-undo ${variantClass(type)} show`;
        toast.setAttribute('role', type === 'error' ? 'alert' : 'status');
        toast.setAttribute('aria-live', type === 'error' ? 'assertive' : 'polite');
        toast.setAttribute('aria-atomic', 'true');

        let html = `<span class="toast-undo__text">${escapeHtml(text)}</span>`;
        if (errorId) {
            // Monospace inline correlation ID. Title attribute exposes the full label for
            // hover/screen-reader context without enlarging the toast layout.
            html += `<span class="toast-undo__error-id" title="${escapeHtml(getMessage('errorId'))}">${escapeHtml(errorId)}</span>`;
        }
        html += `<button type="button" class="toast-undo__btn" data-action="dismiss" aria-label="${escapeHtml(getMessage('close'))}">${escapeHtml(getMessage('dismiss'))}</button>`;
        html += `<span class="toast-undo__timer" aria-hidden="true">${hasTimer ? initialSeconds : ''}</span>`;

        toast.innerHTML = html;
        return toast;
    }

    function displayNextToast() {
        if (activeToast || toastQueue.length === 0) return;

        const options = toastQueue.shift();
        const container = getToastContainer();
        const toast = createToastElement(options);

        container.appendChild(toast);
        activeToast = toast;

        const dismissBtn = toast.querySelector('.toast-undo__btn');
        if (dismissBtn) {
            dismissBtn.addEventListener('click', () => dismissToast(toast));
        }

        const dismissMs = AUTO_DISMISS_BY_TYPE[options.type] ?? CONFIG.autoDismissMs;
        if (dismissMs > 0) {
            startCountdown(toast, dismissMs);
        }

        announceToScreenReader(options.message, options.title);
    }

    /**
     * Start the visible countdown + auto-dismiss. Supports pause-on-hover via
     * elapsed/remaining bookkeeping that applies to both the interval and the
     * auto-dismiss timeout.
     */
    function startCountdown(toast, dismissMs) {
        const timerSpan = toast.querySelector('.toast-undo__timer');
        let remainingMs = dismissMs;
        let tickStart = Date.now();

        function renderSeconds() {
            if (!timerSpan) return;
            timerSpan.textContent = String(Math.max(0, Math.ceil(remainingMs / 1000)));
        }

        function scheduleTick() {
            const msToNextSecond = remainingMs % 1000 || 1000;
            toast._tickTimer = setTimeout(function tick() {
                const elapsed = Date.now() - tickStart;
                remainingMs = Math.max(0, remainingMs - elapsed);
                tickStart = Date.now();
                renderSeconds();
                if (remainingMs <= 0) {
                    dismissToast(toast);
                    return;
                }
                toast._tickTimer = setTimeout(tick, 1000);
            }, msToNextSecond);
        }

        renderSeconds();
        scheduleTick();

        toast.addEventListener('mouseenter', function() {
            if (toast._tickTimer) {
                const elapsed = Date.now() - tickStart;
                remainingMs = Math.max(0, remainingMs - elapsed);
                clearTimeout(toast._tickTimer);
                toast._tickTimer = null;
                renderSeconds();
            }
        });

        toast.addEventListener('mouseleave', function() {
            if (toast.classList.contains('toast--dismissing')) return;
            if (remainingMs <= 0) {
                dismissToast(toast);
                return;
            }
            if (!toast._tickTimer) {
                tickStart = Date.now();
                scheduleTick();
            }
        });
    }

    function announceToScreenReader(message, title) {
        const announcement = title ? `${title}: ${message}` : message;

        let liveRegion = document.getElementById('toast-sr-announcer');
        if (!liveRegion) {
            liveRegion = document.createElement('div');
            liveRegion.id = 'toast-sr-announcer';
            liveRegion.setAttribute('aria-live', 'polite');
            liveRegion.setAttribute('aria-atomic', 'true');
            liveRegion.className = 'sr-only';
            document.body.appendChild(liveRegion);
        }

        liveRegion.textContent = '';
        setTimeout(() => { liveRegion.textContent = announcement; }, 100);
    }

    function dismissToast(toast) {
        if (!toast || !toast.parentElement) {
            activeToast = null;
            displayNextToast();
            return;
        }

        if (toast._tickTimer) {
            clearTimeout(toast._tickTimer);
            toast._tickTimer = null;
        }

        const animationTime = prefersReducedMotion() ? 0 : CONFIG.animationDurationMs;
        toast.classList.add('toast--dismissing');

        setTimeout(() => {
            if (toast.parentElement) toast.remove();
            activeToast = null;
            displayNextToast();
        }, animationTime);
    }

    /**
     * Normalize the third argument of the public Toast.* methods.
     * Accepts either a legacy title string OR an options object {title?, errorId?}.
     */
    function normalizeOptions(titleOrOptions) {
        if (titleOrOptions && typeof titleOrOptions === 'object') {
            return {
                title: titleOrOptions.title || null,
                errorId: titleOrOptions.errorId || null
            };
        }
        return { title: titleOrOptions || null, errorId: null };
    }

    function showToast(message, type = 'success', titleOrOptions = null) {
        const opts = normalizeOptions(titleOrOptions);
        toastQueue.push({ message, type, title: opts.title, errorId: opts.errorId });
        displayNextToast();
    }

    function clearQueue() { toastQueue.length = 0; }

    function dismissActiveToast() {
        if (activeToast) dismissToast(activeToast);
    }

    const Toast = {
        success: function(message, titleOrOptions) { showToast(message, 'success', titleOrOptions); },
        info:    function(message, titleOrOptions) { showToast(message, 'info',    titleOrOptions); },
        warning: function(message, titleOrOptions) { showToast(message, 'warning', titleOrOptions); },
        error:   function(message, titleOrOptions) { showToast(message, 'error',   titleOrOptions); },
        show:    function(message, type, titleOrOptions) { showToast(message, type, titleOrOptions); },
        dismiss: dismissActiveToast,
        clearQueue: clearQueue,
        getQueueLength: function() { return toastQueue.length; },
        config: CONFIG
    };

    window.Toast = Toast;

    // Backward-compatible wrapper - some legacy code calls window.showToast(message, type).
    window.showToast = function(message, type) {
        const typeMap = {
            'success': 'success',
            'info': 'info',
            'warning': 'warning',
            'error': 'error',
            'danger': 'error'
        };
        const mappedType = typeMap[type] || 'success';
        showToast(message, mappedType);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() { getToastContainer(); });
    } else {
        getToastContainer();
    }

})();
