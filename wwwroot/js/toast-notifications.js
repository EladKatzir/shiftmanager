/**
 * Toast/Alert Pattern Standardization (B-052)
 *
 * Notification Patterns:
 * - Toast: Transient success messages (auto-dismiss 5s, max 1 visible, queue others)
 * - Alert/Banner: Persistent warnings/errors (dismiss manually)
 * - Modal: Blocking actions that require user decision
 *
 * Usage:
 *   // Show success toast (auto-dismisses)
 *   Toast.success('Shift saved successfully');
 *   Toast.success('Changes saved', 'Success');
 *
 *   // Show info toast
 *   Toast.info('Processing your request...');
 *
 *   // For errors/warnings, prefer ErrorBanner component or ErrorStates API
 *   // Toasts are for TRANSIENT SUCCESS messages only
 *
 * When to use each pattern:
 * - Toast: "Shift saved", "Profile updated", "Email sent" (success confirmations)
 * - Alert/Banner: "Network offline", "Session expiring", "API error" (persistent issues)
 * - Modal: "Delete this item?", "Discard changes?", "Session timeout" (blocking decisions)
 */

(function() {
    'use strict';

    // Configuration
    const CONFIG = {
        autoDismissMs: 5000,      // 5 seconds auto-dismiss
        maxVisible: 1,            // Only 1 toast visible at a time
        animationDurationMs: 300, // Animation duration
        position: 'top-right'     // Toast position
    };

    // Toast queue - only show one at a time
    const toastQueue = [];
    let activeToast = null;
    let toastContainer = null;

    // Localized messages (fallbacks)
    const MESSAGES = {
        'en-US': {
            dismiss: 'Dismiss',
            close: 'Close notification'
        },
        'he-IL': {
            dismiss: 'סגור',
            close: 'סגור התראה'
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
     * Get localized message
     */
    function getMessage(key) {
        const culture = getCurrentCulture();
        return MESSAGES[culture]?.[key] || MESSAGES['en-US'][key] || key;
    }

    /**
     * Check for reduced motion preference (B-001 accessibility)
     */
    function prefersReducedMotion() {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    /**
     * Get or create toast container
     */
    function getToastContainer() {
        if (!toastContainer || !document.body.contains(toastContainer)) {
            // Remove any existing containers
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

    /**
     * Get icon SVG for toast type
     */
    function getToastIcon(type) {
        const icons = {
            success: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>',
            info: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>',
            warning: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>',
            error: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>'
        };
        return icons[type] || icons.info;
    }

    /**
     * Escape HTML to prevent XSS
     */
    function escapeHtml(str) {
        if (typeof str !== 'string') return str;
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    /**
     * Create toast element
     */
    function createToastElement(options) {
        const { type, message, title } = options;
        const toastId = `toast-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

        // Map type to CSS class (success maps to success, others follow pattern)
        const typeClass = type === 'error' ? 'toast--danger' : `toast--${type}`;

        const toast = document.createElement('div');
        toast.id = toastId;
        toast.className = `toast ${typeClass} toast--with-progress`;
        toast.setAttribute('role', 'status');
        toast.setAttribute('aria-live', type === 'error' ? 'assertive' : 'polite');
        toast.setAttribute('aria-atomic', 'true');

        toast.innerHTML = `
            <div class="toast__icon">
                ${getToastIcon(type)}
            </div>
            <div class="toast__content">
                ${title ? `<div class="toast__title">${escapeHtml(title)}</div>` : ''}
                <div class="toast__message">${escapeHtml(message)}</div>
            </div>
            <button type="button" class="toast__close" aria-label="${getMessage('close')}">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <line x1="18" y1="6" x2="6" y2="18"></line>
                    <line x1="6" y1="6" x2="18" y2="18"></line>
                </svg>
            </button>
            <div class="toast__progress" style="animation-duration: ${CONFIG.autoDismissMs}ms;"></div>
        `;

        return toast;
    }

    /**
     * Display the next toast in the queue
     */
    function displayNextToast() {
        // If there's an active toast or queue is empty, do nothing
        if (activeToast || toastQueue.length === 0) {
            return;
        }

        const options = toastQueue.shift();
        const container = getToastContainer();
        const toast = createToastElement(options);

        // Add to container
        container.appendChild(toast);
        activeToast = toast;

        // Set up close button
        const closeBtn = toast.querySelector('.toast__close');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => dismissToast(toast));
        }

        // Auto-dismiss after configured time
        const autoDismissTimer = setTimeout(() => {
            dismissToast(toast);
        }, CONFIG.autoDismissMs);

        // Store timer reference for manual dismiss
        toast._autoDismissTimer = autoDismissTimer;

        // Announce to screen readers
        announceToScreenReader(options.message, options.title);
    }

    /**
     * Announce toast to screen readers
     */
    function announceToScreenReader(message, title) {
        // The toast container has aria-live, so content will be announced
        // This is a backup for more reliable announcement
        const announcement = title ? `${title}: ${message}` : message;

        // Create a temporary live region if needed
        let liveRegion = document.getElementById('toast-sr-announcer');
        if (!liveRegion) {
            liveRegion = document.createElement('div');
            liveRegion.id = 'toast-sr-announcer';
            liveRegion.setAttribute('aria-live', 'polite');
            liveRegion.setAttribute('aria-atomic', 'true');
            liveRegion.className = 'sr-only';
            document.body.appendChild(liveRegion);
        }

        // Update content to trigger announcement
        liveRegion.textContent = '';
        setTimeout(() => {
            liveRegion.textContent = announcement;
        }, 100);
    }

    /**
     * Dismiss a toast with animation
     */
    function dismissToast(toast) {
        if (!toast || !toast.parentElement) {
            activeToast = null;
            displayNextToast();
            return;
        }

        // Clear auto-dismiss timer
        if (toast._autoDismissTimer) {
            clearTimeout(toast._autoDismissTimer);
        }

        // Respect reduced motion preference
        const animationTime = prefersReducedMotion() ? 0 : CONFIG.animationDurationMs;

        // Add dismissing class for animation
        toast.classList.add('toast--dismissing');

        setTimeout(() => {
            if (toast.parentElement) {
                toast.remove();
            }
            activeToast = null;

            // Show next toast in queue
            displayNextToast();
        }, animationTime);
    }

    /**
     * Show a toast notification
     * @param {string} message - The toast message
     * @param {string} type - Toast type: 'success', 'info', 'warning', 'error'
     * @param {string} [title] - Optional title
     */
    function showToast(message, type = 'success', title = null) {
        // Add to queue
        toastQueue.push({ message, type, title });

        // Try to display (will only work if no active toast)
        displayNextToast();
    }

    /**
     * Clear all pending toasts
     */
    function clearQueue() {
        toastQueue.length = 0;
    }

    /**
     * Dismiss active toast immediately
     */
    function dismissActiveToast() {
        if (activeToast) {
            dismissToast(activeToast);
        }
    }

    // Public API
    const Toast = {
        /**
         * Show success toast (primary use case for toasts)
         * @param {string} message - Success message
         * @param {string} [title] - Optional title
         */
        success: function(message, title) {
            showToast(message, 'success', title);
        },

        /**
         * Show info toast
         * @param {string} message - Info message
         * @param {string} [title] - Optional title
         */
        info: function(message, title) {
            showToast(message, 'info', title);
        },

        /**
         * Show warning toast (consider using Alert/Banner for persistent warnings)
         * @param {string} message - Warning message
         * @param {string} [title] - Optional title
         */
        warning: function(message, title) {
            showToast(message, 'warning', title);
        },

        /**
         * Show error toast (consider using Alert/Banner for persistent errors)
         * @param {string} message - Error message
         * @param {string} [title] - Optional title
         */
        error: function(message, title) {
            showToast(message, 'error', title);
        },

        /**
         * Generic show method
         * @param {string} message - Toast message
         * @param {string} type - Type: 'success', 'info', 'warning', 'error'
         * @param {string} [title] - Optional title
         */
        show: function(message, type, title) {
            showToast(message, type, title);
        },

        /**
         * Dismiss the active toast
         */
        dismiss: dismissActiveToast,

        /**
         * Clear all pending toasts from queue
         */
        clearQueue: clearQueue,

        /**
         * Get queue length (for debugging/testing)
         */
        getQueueLength: function() {
            return toastQueue.length;
        },

        /**
         * Configuration
         */
        config: CONFIG
    };

    // Expose globally
    window.Toast = Toast;

    // Also expose as showToast for backward compatibility with existing code
    // This wraps the new Toast API
    window.showToast = function(message, type) {
        // Map legacy types
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

    // Initialize on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() {
            getToastContainer(); // Pre-create container
        });
    } else {
        getToastContainer();
    }

})();
