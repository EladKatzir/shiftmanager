/**
 * Offline/Flaky Network Handling (B-050)
 *
 * Provides:
 * - Offline banner with persistent state
 * - Form submission queue for offline mode
 * - Automatic retry on reconnection
 * - Queued actions visibility to user
 * - Conflict detection for offline edits
 * - Timeout error improvement (suggests offline)
 *
 * Integrates with:
 * - error-states.js for network banner
 * - api-client.js for API errors
 * - toast-notifications.js for notifications
 */

(function() {
    'use strict';

    // Constants
    const STORAGE_KEY = 'shifty_offline_queue';
    const MAX_QUEUE_SIZE = 50;
    const MAX_AGE_MS = 24 * 60 * 60 * 1000; // 24 hours
    const SYNC_RETRY_DELAY_MS = 2000;
    const MAX_SYNC_RETRIES = 3;

    // State
    let isOnline = navigator.onLine;
    let isSyncing = false;
    let offlineBanner = null;
    let queueBadge = null;

    // Localization messages (fallbacks when server-side not available)
    const MESSAGES = {
        'en-US': {
            offlineBanner: 'You are offline. Changes will sync when reconnected.',
            queuedActions: '{0} action(s) pending sync',
            syncing: 'Syncing...',
            syncComplete: 'All changes synced',
            syncFailed: 'Some changes could not be synced',
            connectionRestored: 'Connection restored',
            conflictDetected: 'This record was modified while you were offline. Your changes may need review.',
            formQueued: 'Your changes have been saved locally and will sync when online.',
            retryLater: 'Unable to sync. Will retry when connection is stable.',
            timeoutSuggestOffline: 'Request timed out. You may be offline or have a slow connection.',
            queueCleared: 'Offline queue cleared',
            viewQueue: 'View pending',
            dismissBanner: 'Dismiss'
        },
        'he-IL': {
            offlineBanner: 'אתה במצב לא מקוון. השינויים יסונכרנו כשהחיבור יחזור.',
            queuedActions: '{0} פעולה/ות ממתינות לסנכרון',
            syncing: 'מסנכרן...',
            syncComplete: 'כל השינויים סונכרנו',
            syncFailed: 'חלק מהשינויים לא ניתן היה לסנכרן',
            connectionRestored: 'החיבור שוחזר',
            conflictDetected: 'רשומה זו שונתה בזמן שהיית במצב לא מקוון. ייתכן שיש צורך לבדוק את השינויים שלך.',
            formQueued: 'השינויים שלך נשמרו מקומית ויסונכרנו כשתהיה מחובר.',
            retryLater: 'לא ניתן לסנכרן. ננסה שוב כשהחיבור יהיה יציב.',
            timeoutSuggestOffline: 'הבקשה נכשלה בגלל זמן קצוב. ייתכן שאתה במצב לא מקוון או שיש לך חיבור איטי.',
            queueCleared: 'תור הפעולות הלא מקוונות נוקה',
            viewQueue: 'הצג ממתינים',
            dismissBanner: 'סגור'
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
     * Get localized message with parameter substitution
     */
    function getMessage(key, params = {}) {
        const culture = getCurrentCulture();
        let message = MESSAGES[culture]?.[key] || MESSAGES['en-US'][key] || key;

        // Replace numbered placeholders {0}, {1}, etc.
        if (typeof params === 'object' && !Array.isArray(params)) {
            Object.keys(params).forEach(param => {
                message = message.replace(`{${param}}`, params[param]);
            });
        } else if (params !== undefined) {
            message = message.replace('{0}', params);
        }

        return message;
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

    // ================================
    // QUEUE MANAGEMENT
    // ================================

    /**
     * Load queue from localStorage
     * @returns {Array} Queued items
     */
    function loadQueue() {
        try {
            const data = localStorage.getItem(STORAGE_KEY);
            if (!data) return [];

            const queue = JSON.parse(data);
            // Filter out expired items
            const now = Date.now();
            return queue.filter(item => (now - item.timestamp) < MAX_AGE_MS);
        } catch (e) {
            console.error('[OfflineHandler] Failed to load queue:', e);
            return [];
        }
    }

    /**
     * Save queue to localStorage
     * @param {Array} queue - Queue items
     */
    function saveQueue(queue) {
        try {
            // Enforce max size by removing oldest items
            while (queue.length > MAX_QUEUE_SIZE) {
                queue.shift();
            }
            localStorage.setItem(STORAGE_KEY, JSON.stringify(queue));
            updateQueueBadge();
        } catch (e) {
            console.error('[OfflineHandler] Failed to save queue:', e);
        }
    }

    /**
     * Add item to offline queue
     * @param {Object} item - Queue item containing form data and metadata
     */
    function addToQueue(item) {
        const queue = loadQueue();

        const queueItem = {
            id: Date.now() + '-' + Math.random().toString(36).substr(2, 9),
            timestamp: Date.now(),
            url: item.url,
            method: item.method || 'POST',
            headers: item.headers || {},
            body: item.body,
            formId: item.formId || null,
            description: item.description || 'Form submission',
            retryCount: 0
        };

        queue.push(queueItem);
        saveQueue(queue);

        console.log('[OfflineHandler] Added to queue:', queueItem.id, queueItem.description);

        // Show feedback to user
        if (window.Toast) {
            window.Toast.info(getMessage('formQueued'));
        } else if (window.ErrorStates) {
            window.ErrorStates.showInfo(getMessage('formQueued'));
        }

        return queueItem.id;
    }

    /**
     * Remove item from queue
     * @param {string} itemId - Queue item ID
     */
    function removeFromQueue(itemId) {
        const queue = loadQueue();
        const filtered = queue.filter(item => item.id !== itemId);
        saveQueue(filtered);
    }

    /**
     * Get queue length
     * @returns {number} Number of items in queue
     */
    function getQueueLength() {
        return loadQueue().length;
    }

    /**
     * Clear the entire queue
     */
    function clearQueue() {
        try {
            localStorage.removeItem(STORAGE_KEY);
            updateQueueBadge();
            console.log('[OfflineHandler] Queue cleared');
        } catch (e) {
            console.error('[OfflineHandler] Failed to clear queue:', e);
        }
    }

    // ================================
    // OFFLINE BANNER
    // ================================

    /**
     * Get or create the offline banner element
     */
    function getOfflineBanner() {
        if (!offlineBanner) {
            offlineBanner = document.querySelector('.offline-banner');
            if (!offlineBanner) {
                offlineBanner = document.createElement('div');
                offlineBanner.className = 'offline-banner';
                offlineBanner.setAttribute('role', 'alert');
                offlineBanner.setAttribute('aria-live', 'assertive');

                offlineBanner.innerHTML = `
                    <div class="offline-banner__content">
                        <span class="offline-banner__icon" aria-hidden="true">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <line x1="1" y1="1" x2="23" y2="23"></line>
                                <path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55"></path>
                                <path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39"></path>
                                <path d="M10.71 5.05A16 16 0 0 1 22.58 9"></path>
                                <path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88"></path>
                                <path d="M8.53 16.11a6 6 0 0 1 6.95 0"></path>
                                <line x1="12" y1="20" x2="12.01" y2="20"></line>
                            </svg>
                        </span>
                        <span class="offline-banner__message">${escapeHtml(getMessage('offlineBanner'))}</span>
                        <span class="offline-banner__queue" id="offlineQueueBadge" style="display: none;">
                            <span class="offline-banner__queue-count">0</span>
                            <span class="offline-banner__queue-text">${escapeHtml(getMessage('queuedActions', '0'))}</span>
                        </span>
                    </div>
                    <div class="offline-banner__actions">
                        <button type="button" class="offline-banner__view-queue" id="offlineViewQueue" style="display: none;">
                            ${escapeHtml(getMessage('viewQueue'))}
                        </button>
                        <button type="button" class="offline-banner__retry" id="offlineRetryBtn">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <polyline points="23 4 23 10 17 10"></polyline>
                                <polyline points="1 20 1 14 7 14"></polyline>
                                <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
                            </svg>
                            <span>${window.AppLocalizer?.Offline_Retry || 'Retry'}</span>
                        </button>
                    </div>
                `;

                // Event listeners
                const retryBtn = offlineBanner.querySelector('#offlineRetryBtn');
                if (retryBtn) {
                    retryBtn.addEventListener('click', function() {
                        checkConnection();
                    });
                }

                const viewQueueBtn = offlineBanner.querySelector('#offlineViewQueue');
                if (viewQueueBtn) {
                    viewQueueBtn.addEventListener('click', function() {
                        showQueueStatus();
                    });
                }

                document.body.insertBefore(offlineBanner, document.body.firstChild);
                queueBadge = offlineBanner.querySelector('#offlineQueueBadge');
            }
        }
        return offlineBanner;
    }

    /**
     * Show the offline banner
     */
    function showOfflineBanner() {
        const banner = getOfflineBanner();
        banner.classList.add('is-visible');

        // Adjust page content if needed
        document.body.style.paddingTop = `${banner.offsetHeight}px`;

        updateQueueBadge();
    }

    /**
     * Hide the offline banner
     */
    function hideOfflineBanner() {
        if (offlineBanner) {
            offlineBanner.classList.remove('is-visible');
            document.body.style.paddingTop = '';
        }
    }

    /**
     * Update the queue badge display
     */
    function updateQueueBadge() {
        const queueLength = getQueueLength();
        const badge = document.querySelector('#offlineQueueBadge');
        const viewQueueBtn = document.querySelector('#offlineViewQueue');

        if (badge) {
            if (queueLength > 0) {
                badge.style.display = 'inline-flex';
                badge.querySelector('.offline-banner__queue-count').textContent = queueLength;
                badge.querySelector('.offline-banner__queue-text').textContent =
                    getMessage('queuedActions', queueLength);
                if (viewQueueBtn) {
                    viewQueueBtn.style.display = 'inline-flex';
                }
            } else {
                badge.style.display = 'none';
                if (viewQueueBtn) {
                    viewQueueBtn.style.display = 'none';
                }
            }
        }

        // Dispatch event for other components
        window.dispatchEvent(new CustomEvent('offline:queueupdate', {
            detail: { count: queueLength }
        }));
    }

    /**
     * Show queue status dialog/modal
     */
    function showQueueStatus() {
        const queue = loadQueue();

        if (queue.length === 0) {
            if (window.Toast) {
                window.Toast.info(window.AppLocalizer?.Offline_NoPendingActions || 'No pending actions');
            }
            return;
        }

        // Create a simple status display
        let statusHtml = '<div class="offline-queue-status">';
        statusHtml += `<h3>${escapeHtml(getMessage('queuedActions', queue.length))}</h3>`;
        statusHtml += '<ul class="offline-queue-list">';

        queue.forEach(item => {
            const age = Math.round((Date.now() - item.timestamp) / 60000);
            const ageText = age < 60 ? `${age}m ago` : `${Math.round(age/60)}h ago`;
            statusHtml += `<li>
                <span class="offline-queue-item__desc">${escapeHtml(item.description)}</span>
                <span class="offline-queue-item__age">${escapeHtml(ageText)}</span>
                <span class="offline-queue-item__retries">(${item.retryCount} retries)</span>
            </li>`;
        });

        statusHtml += '</ul></div>';

        // Use existing modal system if available, otherwise use alert
        if (window.Toast) {
            window.Toast.info(`${queue.length} actions pending sync`);
        }

        console.log('[OfflineHandler] Queue status:', queue);
    }

    // ================================
    // SYNC LOGIC
    // ================================

    /**
     * Attempt to sync all queued items
     */
    async function syncQueue() {
        if (isSyncing || !isOnline) {
            return;
        }

        const queue = loadQueue();
        if (queue.length === 0) {
            return;
        }

        isSyncing = true;
        console.log('[OfflineHandler] Starting sync of', queue.length, 'items');

        // Update banner to show syncing state
        const message = offlineBanner?.querySelector('.offline-banner__message');
        if (message) {
            message.textContent = getMessage('syncing');
        }

        let successCount = 0;
        let failCount = 0;
        const failedItems = [];

        for (const item of queue) {
            try {
                const response = await fetch(item.url, {
                    method: item.method,
                    headers: {
                        'Content-Type': 'application/json',
                        ...item.headers,
                        // Add anti-forgery token if available
                        ...(window.__RequestVerificationToken ? {
                            'RequestVerificationToken': window.__RequestVerificationToken
                        } : {})
                    },
                    body: item.body,
                    credentials: 'same-origin'
                });

                if (response.ok) {
                    removeFromQueue(item.id);
                    successCount++;
                    console.log('[OfflineHandler] Synced item:', item.id);
                } else if (response.status === 409) {
                    // Conflict - server version is newer
                    removeFromQueue(item.id);
                    failCount++;

                    // Notify user of conflict
                    if (window.ErrorStates) {
                        window.ErrorStates.showConcurrencyConflictDialog({
                            entityType: item.description,
                            onReload: () => window.location.reload()
                        });
                    } else if (window.Toast) {
                        window.Toast.warning(getMessage('conflictDetected'));
                    }
                } else if (response.status >= 400 && response.status < 500) {
                    // Client error - don't retry
                    removeFromQueue(item.id);
                    failCount++;
                    console.warn('[OfflineHandler] Client error for item:', item.id, response.status);
                } else {
                    // Server error - retry later
                    item.retryCount++;
                    if (item.retryCount >= MAX_SYNC_RETRIES) {
                        removeFromQueue(item.id);
                        failCount++;
                        console.error('[OfflineHandler] Max retries exceeded for item:', item.id);
                    } else {
                        failedItems.push(item);
                    }
                }
            } catch (error) {
                console.error('[OfflineHandler] Sync error for item:', item.id, error);
                item.retryCount++;
                if (item.retryCount >= MAX_SYNC_RETRIES) {
                    removeFromQueue(item.id);
                    failCount++;
                } else {
                    failedItems.push(item);
                }
            }

            // Small delay between requests to avoid overwhelming server
            await new Promise(resolve => setTimeout(resolve, 200));
        }

        // Save any items that need retry
        if (failedItems.length > 0) {
            const currentQueue = loadQueue().filter(q =>
                !failedItems.some(f => f.id === q.id)
            );
            saveQueue([...currentQueue, ...failedItems]);
        }

        isSyncing = false;

        // Show result
        if (failCount === 0 && successCount > 0) {
            if (window.Toast) {
                window.Toast.success(getMessage('syncComplete'));
            }
        } else if (failCount > 0) {
            if (window.Toast) {
                window.Toast.warning(getMessage('syncFailed'));
            }
        }

        updateQueueBadge();

        console.log('[OfflineHandler] Sync complete:', successCount, 'success,', failCount, 'failed');
    }

    // ================================
    // CONNECTION HANDLING
    // ================================

    /**
     * Check connection by making a lightweight request
     */
    async function checkConnection() {
        try {
            const response = await fetch('/api/health', {
                method: 'HEAD',
                cache: 'no-store',
                credentials: 'same-origin'
            });

            if (response.ok) {
                handleOnline();
                return true;
            }
        } catch (error) {
            handleOffline();
        }
        return false;
    }

    /**
     * Handle going online
     */
    function handleOnline() {
        if (!isOnline) {
            console.log('[OfflineHandler] Connection restored');
            isOnline = true;

            // Show connection restored message
            if (window.Toast) {
                window.Toast.success(getMessage('connectionRestored'));
            }

            // Hide offline banner
            hideOfflineBanner();

            // Attempt to sync queued items after a short delay
            setTimeout(syncQueue, SYNC_RETRY_DELAY_MS);

            // Dispatch event
            window.dispatchEvent(new CustomEvent('offline:connectionrestored'));
        }
    }

    /**
     * Handle going offline
     */
    function handleOffline() {
        if (isOnline) {
            console.log('[OfflineHandler] Connection lost');
            isOnline = false;

            // Show offline banner
            showOfflineBanner();

            // Dispatch event
            window.dispatchEvent(new CustomEvent('offline:connectionlost'));
        }
    }

    // ================================
    // FORM INTERCEPTION
    // ================================

    /**
     * Intercept form submissions when offline
     * @param {HTMLFormElement} form - The form element
     * @param {SubmitEvent} event - The submit event
     */
    function interceptFormSubmit(form, event) {
        if (isOnline) {
            return; // Let it proceed normally
        }

        // Prevent default submission
        event.preventDefault();

        // Collect form data
        const formData = new FormData(form);
        const data = {};
        formData.forEach((value, key) => {
            // Handle multiple values for same key
            if (data[key]) {
                if (Array.isArray(data[key])) {
                    data[key].push(value);
                } else {
                    data[key] = [data[key], value];
                }
            } else {
                data[key] = value;
            }
        });

        // Get form action and method
        const url = form.action || window.location.href;
        const method = form.method?.toUpperCase() || 'POST';

        // Determine description from form
        const description = form.dataset.offlineDescription ||
                          form.querySelector('[data-offline-description]')?.dataset.offlineDescription ||
                          form.querySelector('h1, h2, h3, legend')?.textContent?.trim() ||
                          'Form submission';

        // Add to queue
        addToQueue({
            url: url,
            method: method,
            body: JSON.stringify(data),
            formId: form.id || null,
            description: description,
            headers: {
                'Content-Type': 'application/json'
            }
        });

        // Visual feedback - disable form temporarily
        const submitBtn = form.querySelector('[type="submit"]');
        if (submitBtn) {
            const originalText = submitBtn.textContent;
            submitBtn.textContent = window.AppLocalizer?.Offline_SavedOffline || 'Saved offline';
            submitBtn.disabled = true;
            setTimeout(() => {
                submitBtn.textContent = originalText;
                submitBtn.disabled = false;
            }, 2000);
        }
    }

    /**
     * Set up form interception for offline handling
     */
    function setupFormInterception() {
        // Intercept all forms that opt-in to offline handling
        document.addEventListener('submit', function(event) {
            const form = event.target;
            if (form.tagName !== 'FORM') return;

            // Skip forms that explicitly opt out
            if (form.dataset.offlineSkip === 'true') {
                return;
            }

            // Only intercept POST/PUT/PATCH forms (data mutations)
            const method = form.method?.toUpperCase() || 'GET';
            if (method === 'GET') {
                return;
            }

            // Skip login/auth forms
            if (form.action?.includes('/Auth/') ||
                form.action?.includes('/Login') ||
                form.action?.includes('/Account/')) {
                return;
            }

            // Intercept if offline
            if (!isOnline) {
                interceptFormSubmit(form, event);
            }
        }, true); // Use capture to handle before other listeners
    }

    // ================================
    // API ERROR ENHANCEMENT
    // ================================

    /**
     * Enhance timeout errors to suggest offline status
     */
    function setupTimeoutEnhancement() {
        window.addEventListener('api:timeout', function(event) {
            // Check if actually offline
            if (!navigator.onLine) {
                handleOffline();
            }

            // Show enhanced message suggesting offline
            if (window.Toast) {
                window.Toast.warning(getMessage('timeoutSuggestOffline'));
            }
        });

        window.addEventListener('api:networkerror', function(event) {
            // Network errors likely mean offline
            if (!navigator.onLine) {
                handleOffline();
            }
        });
    }

    // ================================
    // INITIALIZATION
    // ================================

    /**
     * Initialize the offline handler
     */
    function initialize() {
        console.log('[OfflineHandler] Initializing (B-050)');

        // Set up event listeners for browser online/offline events
        window.addEventListener('online', function() {
            console.log('[OfflineHandler] Browser reports online');
            checkConnection(); // Verify with actual request
        });

        window.addEventListener('offline', function() {
            console.log('[OfflineHandler] Browser reports offline');
            handleOffline();
        });

        // Check initial state
        if (!navigator.onLine) {
            handleOffline();
        }

        // Set up form interception
        setupFormInterception();

        // Set up timeout enhancement
        setupTimeoutEnhancement();

        // Inject styles
        injectStyles();

        // Update queue badge on load
        updateQueueBadge();

        // Periodic connection check when offline
        setInterval(function() {
            if (!isOnline) {
                checkConnection();
            }
        }, 30000); // Check every 30 seconds when offline
    }

    /**
     * Inject required CSS styles
     */
    function injectStyles() {
        if (document.getElementById('offline-handler-styles')) {
            return;
        }

        const style = document.createElement('style');
        style.id = 'offline-handler-styles';
        style.textContent = `
            /* Offline Banner (B-050) */
            .offline-banner {
                position: fixed;
                top: 0;
                left: 0;
                right: 0;
                z-index: 1090;
                display: flex;
                align-items: center;
                justify-content: space-between;
                gap: 16px;
                padding: 12px 20px;
                background: linear-gradient(135deg, var(--warning, #D4A017) 0%, var(--warning-hover, #b8890f) 100%);
                color: var(--warning-text, #1A1F2B);
                font-size: 14px;
                font-weight: 500;
                transform: translateY(-100%);
                transition: transform 0.3s ease-out;
                box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
            }

            .offline-banner.is-visible {
                transform: translateY(0);
            }

            .offline-banner__content {
                display: flex;
                align-items: center;
                gap: 12px;
                flex-wrap: wrap;
            }

            .offline-banner__icon {
                flex-shrink: 0;
                display: flex;
                align-items: center;
                justify-content: center;
            }

            .offline-banner__icon svg {
                animation: offline-pulse 2s ease-in-out infinite;
            }

            @keyframes offline-pulse {
                0%, 100% { opacity: 1; }
                50% { opacity: 0.6; }
            }

            .offline-banner__message {
                flex: 1;
                min-width: 200px;
            }

            .offline-banner__queue {
                display: inline-flex;
                align-items: center;
                gap: 6px;
                padding: 4px 10px;
                background: rgba(0, 0, 0, 0.1);
                border-radius: 12px;
                font-size: 12px;
                font-weight: 600;
            }

            .offline-banner__queue-count {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                min-width: 20px;
                height: 20px;
                padding: 0 6px;
                background: var(--warning-text, #1A1F2B);
                color: var(--warning, #D4A017);
                border-radius: 10px;
                font-size: 11px;
                font-weight: 700;
            }

            .offline-banner__actions {
                display: flex;
                align-items: center;
                gap: 8px;
                flex-shrink: 0;
            }

            .offline-banner__retry,
            .offline-banner__view-queue {
                display: inline-flex;
                align-items: center;
                gap: 6px;
                padding: 6px 14px;
                background: rgba(255, 255, 255, 0.2);
                border: 1px solid rgba(255, 255, 255, 0.3);
                border-radius: 6px;
                color: inherit;
                font-size: 13px;
                font-weight: 500;
                cursor: pointer;
                transition: background-color 0.15s, border-color 0.15s;
            }

            .offline-banner__retry:hover,
            .offline-banner__view-queue:hover {
                background: rgba(255, 255, 255, 0.3);
                border-color: rgba(255, 255, 255, 0.4);
            }

            .offline-banner__retry:focus,
            .offline-banner__view-queue:focus {
                outline: 2px solid rgba(255, 255, 255, 0.5);
                outline-offset: 2px;
            }

            /* RTL Support */
            [dir="rtl"] .offline-banner {
                flex-direction: row-reverse;
            }

            [dir="rtl"] .offline-banner__content {
                flex-direction: row-reverse;
            }

            [dir="rtl"] .offline-banner__actions {
                flex-direction: row-reverse;
            }

            /* Dark mode adjustments */
            [data-theme="dark"] .offline-banner {
                background: linear-gradient(135deg, #8B6914 0%, #6b5010 100%);
                color: #FFF8E1;
            }

            [data-theme="dark"] .offline-banner__queue-count {
                background: #FFF8E1;
                color: #6b5010;
            }

            /* Queue status styles */
            .offline-queue-status {
                padding: 16px;
            }

            .offline-queue-status h3 {
                margin: 0 0 12px;
                font-size: 16px;
                font-weight: 600;
            }

            .offline-queue-list {
                list-style: none;
                padding: 0;
                margin: 0;
            }

            .offline-queue-list li {
                display: flex;
                align-items: center;
                gap: 12px;
                padding: 8px 0;
                border-bottom: 1px solid var(--border, #E5E7EB);
            }

            .offline-queue-list li:last-child {
                border-bottom: none;
            }

            .offline-queue-item__desc {
                flex: 1;
                font-weight: 500;
            }

            .offline-queue-item__age,
            .offline-queue-item__retries {
                font-size: 12px;
                color: var(--text-subtle, #64748B);
            }

            /* Reduced motion support */
            @media (prefers-reduced-motion: reduce) {
                .offline-banner {
                    transition: none;
                }
                .offline-banner__icon svg {
                    animation: none;
                }
            }

            /* Mobile responsive */
            @media (max-width: 640px) {
                .offline-banner {
                    flex-direction: column;
                    padding: 10px 16px;
                    gap: 10px;
                }

                .offline-banner__content {
                    width: 100%;
                    justify-content: center;
                    text-align: center;
                }

                .offline-banner__actions {
                    width: 100%;
                    justify-content: center;
                }

                .offline-banner__queue-text {
                    display: none;
                }
            }
        `;
        document.head.appendChild(style);
    }

    // Public API
    window.OfflineHandler = {
        // State
        isOnline: function() { return isOnline; },
        isSyncing: function() { return isSyncing; },

        // Queue management
        getQueue: loadQueue,
        getQueueLength: getQueueLength,
        addToQueue: addToQueue,
        removeFromQueue: removeFromQueue,
        clearQueue: clearQueue,

        // Sync operations
        sync: syncQueue,
        checkConnection: checkConnection,

        // Banner control
        showBanner: showOfflineBanner,
        hideBanner: hideOfflineBanner,

        // Status
        showQueueStatus: showQueueStatus,

        // Localization
        getMessage: getMessage
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
