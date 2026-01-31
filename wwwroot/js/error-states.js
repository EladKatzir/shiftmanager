/**
 * Error States Management System (B-004)
 *
 * Provides:
 * - Toast notifications for transient errors
 * - Network status banner for offline detection
 * - API error handling utilities
 * - Integration with localization system
 */

(function() {
    'use strict';

    // Configuration
    const CONFIG = {
        toastDuration: 5000,          // Default toast auto-dismiss in ms
        toastMaxVisible: 5,           // Maximum toasts visible at once
        networkCheckInterval: 30000,  // Check network every 30 seconds when offline
        networkCheckEndpoint: '/api/health' // Endpoint to check connectivity
    };

    // Localization messages (fallbacks when server-side localization not available)
    const MESSAGES = {
        'en-US': {
            networkError: 'Unable to connect. Please check your connection.',
            networkErrorTitle: 'Connection Error',
            serverError: 'Something went wrong. Please try again.',
            serverErrorTitle: 'Server Error',
            offline: 'You are currently offline. Some features may be unavailable.',
            offlineTitle: 'Offline',
            backOnline: 'Connection restored.',
            backOnlineTitle: 'Back Online',
            retry: 'Retry',
            dismiss: 'Dismiss',
            loadingFailed: 'Failed to load data. Please try again.',
            loadingFailedTitle: 'Loading Failed',
            saveFailed: 'Failed to save changes. Please try again.',
            saveFailedTitle: 'Save Failed',
            accessDenied: "You don't have access to this resource.",
            accessDeniedTitle: 'Access Denied',
            notFound: 'The requested resource was not found.',
            notFoundTitle: 'Not Found'
        },
        'he-IL': {
            networkError: 'לא ניתן להתחבר. אנא בדוק את החיבור שלך.',
            networkErrorTitle: 'שגיאת חיבור',
            serverError: 'משהו השתבש. אנא נסה שוב.',
            serverErrorTitle: 'שגיאת שרת',
            offline: 'אתה כרגע לא מחובר. חלק מהתכונות עשויות להיות לא זמינות.',
            offlineTitle: 'לא מחובר',
            backOnline: 'החיבור שוחזר.',
            backOnlineTitle: 'מחובר שוב',
            retry: 'נסה שוב',
            dismiss: 'סגור',
            loadingFailed: 'טעינת הנתונים נכשלה. אנא נסה שוב.',
            loadingFailedTitle: 'הטעינה נכשלה',
            saveFailed: 'שמירת השינויים נכשלה. אנא נסה שוב.',
            saveFailedTitle: 'השמירה נכשלה',
            accessDenied: 'אין לך גישה למשאב זה.',
            accessDeniedTitle: 'הגישה נדחתה',
            notFound: 'המשאב המבוקש לא נמצא.',
            notFoundTitle: 'לא נמצא'
        }
    };

    // State
    let isOnline = navigator.onLine;
    let networkCheckTimer = null;
    let toastContainer = null;
    let networkBanner = null;

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
    function getMessage(key, params = {}) {
        const culture = getCurrentCulture();
        let message = MESSAGES[culture]?.[key] || MESSAGES['en-US'][key] || key;

        // Replace placeholders
        Object.keys(params).forEach(param => {
            message = message.replace(`{${param}}`, params[param]);
        });

        return message;
    }

    /**
     * Get or create toast container
     */
    function getToastContainer() {
        if (!toastContainer) {
            toastContainer = document.querySelector('.toast-container');
            if (!toastContainer) {
                toastContainer = document.createElement('div');
                toastContainer.className = 'toast-container';
                toastContainer.setAttribute('role', 'region');
                toastContainer.setAttribute('aria-label', 'Notifications');
                document.body.appendChild(toastContainer);
            }
        }
        return toastContainer;
    }

    /**
     * Get icon SVG for toast level
     */
    function getToastIcon(level) {
        const icons = {
            error: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>',
            warning: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>',
            info: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>',
            success: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>'
        };
        return icons[level] || icons.info;
    }

    /**
     * Show a toast notification
     * @param {Object} options - Toast options
     * @param {string} options.level - 'error', 'warning', 'info', or 'success'
     * @param {string} options.message - Toast message
     * @param {string} [options.title] - Optional toast title
     * @param {number} [options.duration] - Auto-dismiss duration in ms (0 = no auto-dismiss)
     * @param {boolean} [options.showClose] - Whether to show close button
     * @param {Function} [options.onRetry] - Optional retry callback
     * @returns {HTMLElement} The toast element
     */
    function showToast(options) {
        const {
            level = 'info',
            message,
            title,
            duration = CONFIG.toastDuration,
            showClose = true,
            onRetry
        } = options;

        const container = getToastContainer();

        // Limit visible toasts
        const existingToasts = container.querySelectorAll('.toast');
        if (existingToasts.length >= CONFIG.toastMaxVisible) {
            existingToasts[0].remove();
        }

        // Map level to CSS class
        const levelClass = level === 'error' ? 'toast--danger' : `toast--${level}`;
        const toastId = `toast-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

        const toast = document.createElement('div');
        toast.id = toastId;
        toast.className = `toast ${levelClass}`;
        toast.setAttribute('role', 'alert');
        toast.setAttribute('aria-live', level === 'error' ? 'assertive' : 'polite');

        toast.innerHTML = `
            <div class="toast__icon" aria-hidden="true">
                ${getToastIcon(level)}
            </div>
            <div class="toast__content">
                ${title ? `<div class="toast__title">${escapeHtml(title)}</div>` : ''}
                <div class="toast__message">${escapeHtml(message)}</div>
            </div>
            ${onRetry ? `
                <button type="button" class="toast__retry btn btn--sm btn--ghost" aria-label="${getMessage('retry')}">
                    ${getMessage('retry')}
                </button>
            ` : ''}
            ${showClose ? `
                <button type="button" class="toast__close" aria-label="${getMessage('dismiss')}">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
                </button>
            ` : ''}
        `;

        // Add progress bar for auto-dismiss
        if (duration > 0) {
            const progress = document.createElement('div');
            progress.className = 'toast__progress';
            progress.style.animationDuration = `${duration}ms`;
            toast.appendChild(progress);
            toast.classList.add('toast--with-progress');
        }

        // Event listeners
        const closeBtn = toast.querySelector('.toast__close');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => dismissToast(toast));
        }

        const retryBtn = toast.querySelector('.toast__retry');
        if (retryBtn && onRetry) {
            retryBtn.addEventListener('click', () => {
                dismissToast(toast);
                onRetry();
            });
        }

        container.appendChild(toast);

        // Auto-dismiss
        if (duration > 0) {
            setTimeout(() => dismissToast(toast), duration);
        }

        return toast;
    }

    /**
     * Dismiss a toast with animation
     */
    function dismissToast(toast) {
        if (!toast || !toast.parentElement) return;

        toast.classList.add('toast--dismissing');
        setTimeout(() => {
            if (toast.parentElement) {
                toast.remove();
            }
        }, 300);
    }

    /**
     * Show error toast (convenience method)
     */
    function showError(message, title, options = {}) {
        return showToast({
            level: 'error',
            message,
            title: title || getMessage('serverErrorTitle'),
            ...options
        });
    }

    /**
     * Show warning toast (convenience method)
     */
    function showWarning(message, title, options = {}) {
        return showToast({
            level: 'warning',
            message,
            title,
            ...options
        });
    }

    /**
     * Show info toast (convenience method)
     */
    function showInfo(message, title, options = {}) {
        return showToast({
            level: 'info',
            message,
            title,
            ...options
        });
    }

    /**
     * Show success toast (convenience method)
     */
    function showSuccess(message, title, options = {}) {
        return showToast({
            level: 'success',
            message,
            title,
            ...options
        });
    }

    /**
     * Get or create network banner
     */
    function getNetworkBanner() {
        if (!networkBanner) {
            networkBanner = document.querySelector('.network-banner');
            if (!networkBanner) {
                networkBanner = document.createElement('div');
                networkBanner.className = 'network-banner';
                networkBanner.setAttribute('role', 'alert');
                networkBanner.setAttribute('aria-live', 'assertive');
                networkBanner.innerHTML = `
                    <span class="network-banner__icon" aria-hidden="true">
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="1" y1="1" x2="23" y2="23"></line><path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55"></path><path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39"></path><path d="M10.71 5.05A16 16 0 0 1 22.58 9"></path><path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88"></path><path d="M8.53 16.11a6 6 0 0 1 6.95 0"></path><line x1="12" y1="20" x2="12.01" y2="20"></line></svg>
                    </span>
                    <span class="network-banner__message">${getMessage('offline')}</span>
                    <button type="button" class="network-banner__retry">${getMessage('retry')}</button>
                `;

                const retryBtn = networkBanner.querySelector('.network-banner__retry');
                if (retryBtn) {
                    retryBtn.addEventListener('click', checkNetworkStatus);
                }

                document.body.insertBefore(networkBanner, document.body.firstChild);
            }
        }
        return networkBanner;
    }

    /**
     * Show network banner
     */
    function showNetworkBanner() {
        const banner = getNetworkBanner();
        banner.classList.add('is-visible');

        // Adjust page content if needed
        document.body.style.paddingTop = `${banner.offsetHeight}px`;
    }

    /**
     * Hide network banner
     */
    function hideNetworkBanner() {
        if (networkBanner) {
            networkBanner.classList.remove('is-visible');
            document.body.style.paddingTop = '';
        }
    }

    /**
     * Check network status
     */
    async function checkNetworkStatus() {
        try {
            const response = await fetch(CONFIG.networkCheckEndpoint, {
                method: 'HEAD',
                cache: 'no-store'
            });

            if (response.ok && !isOnline) {
                isOnline = true;
                hideNetworkBanner();
                showSuccess(getMessage('backOnline'), getMessage('backOnlineTitle'), {
                    duration: 3000
                });
                stopNetworkCheck();
            }
        } catch (error) {
            // Still offline
            if (isOnline) {
                isOnline = false;
                showNetworkBanner();
                startNetworkCheck();
            }
        }
    }

    /**
     * Start periodic network checking
     */
    function startNetworkCheck() {
        if (!networkCheckTimer) {
            networkCheckTimer = setInterval(checkNetworkStatus, CONFIG.networkCheckInterval);
        }
    }

    /**
     * Stop periodic network checking
     */
    function stopNetworkCheck() {
        if (networkCheckTimer) {
            clearInterval(networkCheckTimer);
            networkCheckTimer = null;
        }
    }

    /**
     * Handle online event
     */
    function handleOnline() {
        console.log('Browser reports online');
        checkNetworkStatus();
    }

    /**
     * Handle offline event
     */
    function handleOffline() {
        console.log('Browser reports offline');
        isOnline = false;
        showNetworkBanner();
        startNetworkCheck();
    }

    /**
     * Handle API errors and show appropriate toast
     * @param {Response|Error} error - Fetch response or Error object
     * @param {Object} options - Additional options
     * @param {Function} [options.onRetry] - Retry callback
     * @returns {HTMLElement} The toast element
     */
    function handleApiError(error, options = {}) {
        let message, title, level = 'error';

        if (error instanceof Response) {
            switch (error.status) {
                case 400:
                    message = getMessage('serverError');
                    title = getMessage('serverErrorTitle');
                    break;
                case 401:
                    // Session handling is done by session-check.js
                    return null;
                case 403:
                    message = getMessage('accessDenied');
                    title = getMessage('accessDeniedTitle');
                    break;
                case 404:
                    message = getMessage('notFound');
                    title = getMessage('notFoundTitle');
                    break;
                case 422:
                    message = options.message || getMessage('serverError');
                    title = options.title || getMessage('serverErrorTitle');
                    break;
                case 500:
                case 502:
                case 503:
                case 504:
                    message = getMessage('serverError');
                    title = getMessage('serverErrorTitle');
                    break;
                default:
                    message = getMessage('serverError');
                    title = getMessage('serverErrorTitle');
            }
        } else if (error instanceof TypeError || error.name === 'TypeError') {
            // Network error
            message = getMessage('networkError');
            title = getMessage('networkErrorTitle');
        } else {
            message = error.message || getMessage('serverError');
            title = getMessage('serverErrorTitle');
        }

        return showToast({
            level,
            message,
            title,
            onRetry: options.onRetry,
            ...options
        });
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
     * Initialize error states system
     */
    function initialize() {
        console.log('Initializing error states system');

        // Set up network status listeners
        window.addEventListener('online', handleOnline);
        window.addEventListener('offline', handleOffline);

        // Check initial state
        if (!navigator.onLine) {
            handleOffline();
        }

        // Inject required CSS if not already present
        ensureStyles();
    }

    /**
     * Ensure required styles are loaded
     */
    function ensureStyles() {
        if (document.getElementById('error-states-styles')) {
            return;
        }

        // Check if components.css is loaded (has .toast-container)
        const hasComponentStyles = Array.from(document.styleSheets).some(sheet => {
            try {
                return Array.from(sheet.cssRules || []).some(rule =>
                    rule.selectorText && rule.selectorText.includes('.toast-container')
                );
            } catch (e) {
                return false; // Cross-origin stylesheets
            }
        });

        if (!hasComponentStyles) {
            // Add minimal fallback styles
            const style = document.createElement('style');
            style.id = 'error-states-styles';
            style.textContent = `
                .toast-container {
                    position: fixed;
                    top: 16px;
                    right: 16px;
                    z-index: 1080;
                    display: flex;
                    flex-direction: column;
                    gap: 12px;
                    pointer-events: none;
                }
                [dir="rtl"] .toast-container {
                    right: auto;
                    left: 16px;
                }
                .toast {
                    display: flex;
                    align-items: flex-start;
                    gap: 12px;
                    max-width: 400px;
                    padding: 16px;
                    background-color: #fff;
                    border-radius: 8px;
                    box-shadow: 0 10px 15px rgba(0, 0, 0, 0.1);
                    pointer-events: auto;
                    animation: toast-slide-in 0.3s ease-out;
                }
                @keyframes toast-slide-in {
                    from { opacity: 0; transform: translateX(100%); }
                    to { opacity: 1; transform: translateX(0); }
                }
                .toast--dismissing {
                    opacity: 0;
                    transform: translateX(100%);
                    transition: all 0.3s ease-in;
                }
                .toast--danger { border-left: 4px solid #9B2C2C; }
                .toast--warning { border-left: 4px solid #D4A017; }
                .toast--info { border-left: 4px solid #1E3A5F; }
                .toast--success { border-left: 4px solid #2D6A4F; }
                .toast__icon { flex-shrink: 0; }
                .toast--danger .toast__icon { color: #9B2C2C; }
                .toast--warning .toast__icon { color: #D4A017; }
                .toast--info .toast__icon { color: #1E3A5F; }
                .toast--success .toast__icon { color: #2D6A4F; }
                .toast__content { flex: 1; min-width: 0; }
                .toast__title { font-weight: 500; }
                .toast__message { font-size: 14px; color: #64748B; margin-top: 4px; }
                .toast__close {
                    flex-shrink: 0;
                    padding: 4px;
                    background: transparent;
                    border: none;
                    color: #64748B;
                    cursor: pointer;
                    border-radius: 4px;
                }
                .toast__close:hover { background-color: #F0F4F8; color: #1A1F2B; }
                .toast__progress {
                    position: absolute;
                    bottom: 0;
                    left: 0;
                    height: 3px;
                    background-color: currentColor;
                    opacity: 0.3;
                    animation: toast-progress linear forwards;
                }
                @keyframes toast-progress { from { width: 100%; } to { width: 0; } }
                .network-banner {
                    position: fixed;
                    top: 0;
                    left: 0;
                    right: 0;
                    z-index: 1080;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    gap: 12px;
                    padding: 12px 16px;
                    background-color: #9B2C2C;
                    color: #fff;
                    font-size: 14px;
                    font-weight: 500;
                    text-align: center;
                    transform: translateY(-100%);
                    transition: transform 0.2s ease-out;
                }
                .network-banner.is-visible { transform: translateY(0); }
                .network-banner__retry {
                    padding: 4px 12px;
                    background-color: rgba(255, 255, 255, 0.2);
                    border: 1px solid rgba(255, 255, 255, 0.3);
                    border-radius: 4px;
                    color: inherit;
                    font-size: 12px;
                    font-weight: 500;
                    cursor: pointer;
                }
                .network-banner__retry:hover { background-color: rgba(255, 255, 255, 0.3); }
            `;
            document.head.appendChild(style);
        }
    }

    // Public API
    window.ErrorStates = {
        showToast,
        showError,
        showWarning,
        showInfo,
        showSuccess,
        dismissToast,
        handleApiError,
        showNetworkBanner,
        hideNetworkBanner,
        checkNetworkStatus,
        getMessage,
        // Configuration
        config: CONFIG
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
