/**
 * Session Management System with Two-Tier Warning
 *
 * Features:
 * - WARNING state: Shows yellow banner when ≤30 minutes remaining
 * - EXPIRED state: Shows red banner when session truly expired (401)
 * - Adaptive polling: 5 min (ok) → 1 min (warning) → stop (expired)
 * - Session extension: "Extend Session" button triggers sliding expiration
 * - Tab visibility optimization: Pauses when hidden
 * - Bilingual: Hebrew and English
 */

(function () {
    'use strict';

    // Configuration
    const POLLING_INTERVALS = {
        OK: 5 * 60 * 1000,      // 5 minutes when session is healthy
        WARNING: 60 * 1000,     // 1 minute when approaching expiration
        EXPIRED: 0              // Stop polling when expired
    };

    const WARNING_THRESHOLD = 30 * 60; // 30 minutes in seconds
    const SESSION_STATUS_URL = '/Api/SessionStatus';

    // Session state machine
    let sessionState = 'ok';        // ok | warning | expired
    let pollTimer = null;
    let currentPollInterval = POLLING_INTERVALS.OK;
    let lastCheckTime = null;
    let secondsRemaining = null;
    let warningDismissed = false;   // Track if user dismissed warning

    // Localization
    const MESSAGES = {
        'he-IL': {
            warningTitle: 'אזהרת פג תוקף',
            warningMessage: 'פג תוקף ההתחברות שלך בעוד {minutes} דקות.',
            expiredTitle: 'פג תוקף ההתחברות',
            expiredMessage: 'פג תוקף ההתחברות. נא להתחבר מחדש.',
            extendSession: 'הארך התחברות',
            loginAgain: 'התחבר מחדש',
            dismiss: 'ביטול'
        },
        'en-US': {
            warningTitle: 'Session Expiring Soon',
            warningMessage: 'Your session will expire in {minutes} minutes.',
            expiredTitle: 'Session Expired',
            expiredMessage: 'Your session has expired. Please log in again.',
            extendSession: 'Extend Session',
            loginAgain: 'Log In',
            dismiss: 'Dismiss'
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
     * Get localized message with placeholder replacement
     */
    function getMessage(key, params = {}) {
        const culture = getCurrentCulture();
        let message = MESSAGES[culture][key] || MESSAGES['en-US'][key];

        // Replace placeholders
        Object.keys(params).forEach(param => {
            message = message.replace(`{${param}}`, params[param]);
        });

        return message;
    }

    /**
     * Check session status
     */
    async function checkSession() {
        try {
            const response = await fetch(SESSION_STATUS_URL, {
                method: 'GET',
                credentials: 'same-origin',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            lastCheckTime = Date.now();

            if (response.status === 401) {
                handleExpiredState();
            } else if (response.ok) {
                const data = await response.json();
                handleAuthenticatedState(data);
            } else {
                console.error('Session check failed with status:', response.status);
            }
        } catch (error) {
            console.error('Session check failed:', error);
            // Don't show notification for network errors
            // Keep current state and try again on next poll
        }
    }

    /**
     * Handle authenticated state (ok or warning)
     */
    function handleAuthenticatedState(data) {
        secondsRemaining = data.secondsRemaining;
        const minutesRemaining = data.minutesRemaining;

        console.log(`Session check: ${data.state}, ${minutesRemaining} minutes remaining`);

        if (data.state === 'warning' && sessionState !== 'warning') {
            // Transition from OK to WARNING
            transitionToWarning(minutesRemaining);
        } else if (data.state === 'ok' && sessionState === 'warning') {
            // Transition from WARNING back to OK (session was extended)
            transitionToOk();
        } else if (sessionState === 'warning') {
            // Already in warning, update the notification
            updateWarningNotification(minutesRemaining);
        }
    }

    /**
     * Transition to WARNING state
     */
    function transitionToWarning(minutesRemaining) {
        console.log('Transitioning to WARNING state');
        sessionState = 'warning';
        currentPollInterval = POLLING_INTERVALS.WARNING;

        // Only show notification if not previously dismissed
        if (!warningDismissed) {
            showWarningNotification(minutesRemaining);
        }

        restartPolling();
    }

    /**
     * Transition to OK state
     */
    function transitionToOk() {
        console.log('Transitioning to OK state (session extended)');
        sessionState = 'ok';
        currentPollInterval = POLLING_INTERVALS.OK;
        warningDismissed = false; // Reset dismissal flag
        hideNotification();
        restartPolling();
    }

    /**
     * Handle EXPIRED state
     */
    function handleExpiredState() {
        if (sessionState === 'expired') {
            return; // Already handled
        }

        console.log('Transitioning to EXPIRED state');
        sessionState = 'expired';
        stopPolling();
        showExpiredNotification();
    }

    /**
     * Show warning notification (yellow, dismissable)
     */
    function showWarningNotification(minutesRemaining) {
        hideNotification(); // Remove any existing notification

        const notification = createNotification({
            id: 'session-warning-notification',
            type: 'warning',
            icon: '⚠️',
            title: getMessage('warningTitle'),
            message: getMessage('warningMessage', { minutes: minutesRemaining }),
            actions: [
                {
                    label: getMessage('extendSession'),
                    className: 'btn-primary',
                    onClick: extendSession
                },
                {
                    label: getMessage('dismiss'),
                    className: 'btn-ghost',
                    onClick: dismissNotification
                }
            ],
            dismissable: true
        });

        document.body.appendChild(notification);
    }

    /**
     * Update warning notification with new time
     */
    function updateWarningNotification(minutesRemaining) {
        const notification = document.getElementById('session-warning-notification');
        if (notification) {
            const messageElement = notification.querySelector('.session-notification-message');
            if (messageElement) {
                messageElement.textContent = getMessage('warningMessage', { minutes: minutesRemaining });
            }
        }
    }

    /**
     * Show expired notification (red, not dismissable)
     */
    function showExpiredNotification() {
        hideNotification(); // Remove any existing notification

        const notification = createNotification({
            id: 'session-expired-notification',
            type: 'expired',
            icon: '🚫',
            title: getMessage('expiredTitle'),
            message: getMessage('expiredMessage'),
            actions: [
                {
                    label: getMessage('loginAgain'),
                    className: 'btn-primary',
                    onClick: redirectToLogin
                }
            ],
            dismissable: false
        });

        document.body.appendChild(notification);
    }

    /**
     * Create notification element
     */
    function createNotification(config) {
        const notification = document.createElement('div');
        notification.id = config.id;
        notification.className = `session-notification session-notification-${config.type}`;

        // Build actions HTML
        const actionsHtml = config.actions.map((action, index) => `
            <button class="btn btn-sm ${action.className}" data-action-index="${index}">
                ${action.label}
            </button>
        `).join('');

        notification.innerHTML = `
            <div class="session-notification-content">
                <span class="session-notification-icon">${config.icon}</span>
                <div class="session-notification-text">
                    <div class="session-notification-title">${config.title}</div>
                    <div class="session-notification-message">${config.message}</div>
                </div>
                <div class="session-notification-actions">
                    ${actionsHtml}
                </div>
            </div>
        `;

        // Attach event listeners
        config.actions.forEach((action, index) => {
            const button = notification.querySelector(`[data-action-index="${index}"]`);
            if (button) {
                button.addEventListener('click', action.onClick);
            }
        });

        // Add styles if not already added
        ensureStyles();

        return notification;
    }

    /**
     * Extend session (makes request to trigger sliding expiration)
     */
    async function extendSession() {
        console.log('Extending session...');

        // Show loading state
        const notification = document.getElementById('session-warning-notification');
        if (notification) {
            const button = notification.querySelector('.btn-primary');
            if (button) {
                button.disabled = true;
                button.textContent = '...';
            }
        }

        try {
            const response = await fetch(SESSION_STATUS_URL, {
                method: 'GET',
                credentials: 'same-origin',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            if (response.ok) {
                const data = await response.json();
                console.log('Session extended successfully');

                // Update state based on response
                handleAuthenticatedState(data);
            } else {
                console.error('Failed to extend session:', response.status);
                // If we got 401, transition to expired
                if (response.status === 401) {
                    handleExpiredState();
                } else {
                    // Restore button state
                    if (notification) {
                        const button = notification.querySelector('.btn-primary');
                        if (button) {
                            button.disabled = false;
                            button.textContent = getMessage('extendSession');
                        }
                    }
                }
            }
        } catch (error) {
            console.error('Error extending session:', error);
            // Restore button state
            if (notification) {
                const button = notification.querySelector('.btn-primary');
                if (button) {
                    button.disabled = false;
                    button.textContent = getMessage('extendSession');
                }
            }
        }
    }

    /**
     * Redirect to login with return URL
     */
    function redirectToLogin() {
        // Don't redirect if already on login page (prevents infinite loop)
        const currentPath = window.location.pathname.toLowerCase();
        if (currentPath.includes('/auth/login')) {
            console.warn('Already on login page, skipping redirect to prevent infinite loop');
            return;
        }

        const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
        window.location.href = `/Auth/Login?reason=sessionExpired&returnUrl=${returnUrl}`;
    }

    /**
     * Dismiss notification
     */
    function dismissNotification() {
        hideNotification();
        warningDismissed = true;
    }

    /**
     * Hide notification
     */
    function hideNotification() {
        const warningNotification = document.getElementById('session-warning-notification');
        const expiredNotification = document.getElementById('session-expired-notification');

        if (warningNotification) {
            warningNotification.remove();
        }
        if (expiredNotification) {
            expiredNotification.remove();
        }
    }

    /**
     * Restart polling with current interval
     */
    function restartPolling() {
        stopPolling();
        if (currentPollInterval > 0) {
            console.log(`Restarting polling with ${currentPollInterval / 1000}s interval`);
            pollTimer = setInterval(checkSession, currentPollInterval);
        }
    }

    /**
     * Stop polling
     */
    function stopPolling() {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
    }

    /**
     * Ensure styles are loaded
     */
    function ensureStyles() {
        if (document.getElementById('session-notification-styles')) {
            return;
        }

        const style = document.createElement('style');
        style.id = 'session-notification-styles';
        style.textContent = `
            .session-notification {
                position: fixed;
                top: 20px;
                left: 50%;
                transform: translateX(-50%);
                padding: 1rem 1.5rem;
                border-radius: 8px;
                box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
                z-index: 10000;
                max-width: 90%;
                min-width: 400px;
                animation: slideDown 0.3s ease-out;
            }

            .session-notification-warning {
                background: #f59e0b;
                color: #78350f;
                border: 2px solid #d97706;
            }

            .session-notification-expired {
                background: #dc3545;
                color: white;
                border: 2px solid #b91c1c;
            }

            @keyframes slideDown {
                from {
                    opacity: 0;
                    transform: translateX(-50%) translateY(-20px);
                }
                to {
                    opacity: 1;
                    transform: translateX(-50%) translateY(0);
                }
            }

            .session-notification-content {
                display: flex;
                align-items: center;
                gap: 1rem;
            }

            .session-notification-icon {
                font-size: 1.5rem;
                flex-shrink: 0;
            }

            .session-notification-text {
                flex: 1;
                min-width: 200px;
            }

            .session-notification-title {
                font-weight: 600;
                font-size: 1rem;
                margin-bottom: 0.25rem;
            }

            .session-notification-message {
                font-size: 0.875rem;
                opacity: 0.9;
            }

            .session-notification-actions {
                display: flex;
                gap: 0.5rem;
                flex-shrink: 0;
            }

            .session-notification-actions .btn {
                font-size: 0.875rem;
                padding: 0.5rem 1rem;
                border-radius: 6px;
                cursor: pointer;
                border: none;
                font-weight: 500;
                transition: all 0.2s;
            }

            .session-notification-warning .btn-primary {
                background: white;
                color: #f59e0b;
            }

            .session-notification-warning .btn-primary:hover {
                background: rgba(255, 255, 255, 0.9);
                transform: scale(1.05);
            }

            .session-notification-warning .btn-ghost {
                background: transparent;
                color: inherit;
                border: 1px solid currentColor;
            }

            .session-notification-warning .btn-ghost:hover {
                background: rgba(255, 255, 255, 0.2);
            }

            .session-notification-expired .btn-primary {
                background: white;
                color: #dc3545;
            }

            .session-notification-expired .btn-primary:hover {
                background: rgba(255, 255, 255, 0.9);
                transform: scale(1.05);
            }

            .session-notification-actions .btn:disabled {
                opacity: 0.5;
                cursor: not-allowed;
            }

            /* Responsive */
            @media (max-width: 640px) {
                .session-notification {
                    min-width: auto;
                    width: 90%;
                }

                .session-notification-content {
                    flex-direction: column;
                    align-items: flex-start;
                }

                .session-notification-actions {
                    width: 100%;
                }

                .session-notification-actions .btn {
                    flex: 1;
                }
            }
        `;
        document.head.appendChild(style);
    }

    /**
     * Handle page visibility changes (pause checking when tab hidden)
     */
    function handleVisibilityChange() {
        if (document.hidden) {
            console.log('Tab hidden, pausing session checks');
            stopPolling();
        } else {
            console.log('Tab visible, resuming session checks');
            checkSession(); // Check immediately
            restartPolling();
        }
    }

    /**
     * Initialize session checking
     */
    function initialize() {
        console.log('Initializing session management system');

        // Don't run on login/signup pages (defense in depth)
        const currentPath = window.location.pathname.toLowerCase();
        if (currentPath.includes('/auth/login') ||
            currentPath.includes('/auth/signup') ||
            currentPath.includes('/auth/forgotpassword')) {
            console.log('Skipping session check on auth page:', currentPath);
            return;
        }

        // Check immediately on load
        checkSession();

        // Start polling
        restartPolling();

        // Handle visibility changes
        document.addEventListener('visibilitychange', handleVisibilityChange);
    }

    /**
     * Expose functions for manual control and debugging
     */
    window.sessionManager = {
        check: checkSession,
        extend: extendSession,
        getState: () => ({
            state: sessionState,
            secondsRemaining: secondsRemaining,
            pollInterval: currentPollInterval,
            lastCheckTime: lastCheckTime,
            warningDismissed: warningDismissed
        })
    };

    // Expose legacy function for compatibility
    window.checkSessionStatus = checkSession;

    // Start when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
