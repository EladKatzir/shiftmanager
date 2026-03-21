/**
 * Calendar Real-Time Updates with Shadow Refresh Fallback
 *
 * Architecture:
 * Layer 1: SignalR (Primary) - WebSocket push updates
 * Layer 2: Shadow Refresh - On cell click, date nav, filter change, tab return
 * Layer 3: Periodic Polling - If SignalR disconnected >30s, poll every 60s
 */
(function() {
    'use strict';

    // Configuration
    const CONFIG = {
        hubUrl: '/hubs/calendar',
        reconnectDelays: [0, 2000, 5000, 10000, 30000], // Exponential backoff
        maxReconnectAttempts: 10,
        pollingInterval: 60000, // 60 seconds
        pollingActivationDelay: 30000, // Start polling if disconnected for 30s
        shadowRefreshDebounce: 250, // ms
        connectionTimeout: 10000, // 10 seconds
    };

    // State
    let connection = null;
    let connectionState = 'disconnected'; // disconnected, connecting, connected, reconnecting
    let reconnectAttempts = 0;
    let currentGroup = null;
    let pollingTimer = null;
    let disconnectedSince = null;
    let lastRefreshTime = Date.now();
    let shadowRefreshPending = false;
    let shadowRefreshTimer = null;
    let eventHandlers = {};
    let refreshCallback = null;
    let isPageVisible = true;
    let elapsedUpdateTimer = null;

    /**
     * Initialize calendar real-time updates.
     * @param {Object} options - Configuration options
     * @param {string} options.calendarType - 'shifts', 'chores', 'oncall', 'overview'
     * @param {Object} options.scope - Scope parameters (moleculeId, jobTypeId, areaId, companyId)
     * @param {Function} options.onRefresh - Callback to refresh calendar data
     * @param {Object} options.handlers - Event handlers for each event type
     */
    function initialize(options) {
        const { calendarType, scope, onRefresh, handlers } = options;

        // Store refresh callback
        refreshCallback = onRefresh;
        eventHandlers = handlers || {};

        // Build group name
        currentGroup = buildGroupName(calendarType, scope);

        // Initialize SignalR
        initializeSignalR();

        // Set up shadow refresh triggers
        setupShadowRefreshTriggers();

        // Set up visibility change handler
        setupVisibilityHandler();

        console.log('[CalendarRealtime] Initialized for group:', currentGroup);
    }

    /**
     * Build group name from calendar type and scope.
     */
    function buildGroupName(calendarType, scope) {
        switch (calendarType) {
            case 'shifts':
                return `shifts-${scope.moleculeId}-${scope.jobTypeId ?? 0}`;
            case 'chores':
                return `chores-${scope.moleculeId}`;
            case 'oncall':
                return `oncall-${scope.areaId}`;
            case 'overview':
                return `overview-${scope.companyId}`;
            default:
                throw new Error(`Unknown calendar type: ${calendarType}`);
        }
    }

    /**
     * Initialize SignalR connection.
     */
    function initializeSignalR() {
        if (typeof signalR === 'undefined') {
            console.warn('[CalendarRealtime] SignalR library not loaded, falling back to polling');
            startPolling();
            return;
        }

        connection = new signalR.HubConnectionBuilder()
            .withUrl(CONFIG.hubUrl)
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: (retryContext) => {
                    const index = Math.min(retryContext.previousRetryCount, CONFIG.reconnectDelays.length - 1);
                    const baseDelay = CONFIG.reconnectDelays[index];
                    // Add random jitter (0-50% of base delay) to prevent thundering herd
                    const jitter = Math.floor(Math.random() * baseDelay * 0.5);
                    return baseDelay + jitter;
                }
            })
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        // Set up connection state handlers
        connection.onreconnecting((error) => {
            connectionState = 'reconnecting';
            disconnectedSince = disconnectedSince || Date.now();
            console.log('[CalendarRealtime] Reconnecting...', error?.message);
            updateConnectionIndicator();
            checkPollingNeeded();
        });

        connection.onreconnected((connectionId) => {
            connectionState = 'connected';
            disconnectedSince = null;
            reconnectAttempts = 0;
            console.log('[CalendarRealtime] Reconnected:', connectionId);
            updateConnectionIndicator();
            stopPolling();
            rejoinGroup();
            triggerShadowRefresh(); // Refresh after reconnection
        });

        connection.onclose((error) => {
            connectionState = 'disconnected';
            disconnectedSince = disconnectedSince || Date.now();
            console.log('[CalendarRealtime] Connection closed:', error?.message);
            updateConnectionIndicator();
            checkPollingNeeded();
            attemptReconnect();
        });

        // Set up event handlers
        setupEventHandlers();

        // Start connection
        startConnection();
    }

    /**
     * Set up SignalR event handlers.
     */
    function setupEventHandlers() {
        if (!connection) return;

        connection.on('AssignmentChanged', (evt) => {
            console.log('[CalendarRealtime] AssignmentChanged:', evt);
            if (eventHandlers.onAssignmentChanged) {
                eventHandlers.onAssignmentChanged(evt);
            } else {
                triggerShadowRefresh();
            }
        });

        connection.on('CapacityChanged', (evt) => {
            console.log('[CalendarRealtime] CapacityChanged:', evt);
            if (eventHandlers.onCapacityChanged) {
                eventHandlers.onCapacityChanged(evt);
            } else {
                triggerShadowRefresh();
            }
        });

        connection.on('NoteChanged', (evt) => {
            console.log('[CalendarRealtime] NoteChanged:', evt);
            if (eventHandlers.onNoteChanged) {
                eventHandlers.onNoteChanged(evt);
            } else {
                triggerShadowRefresh();
            }
        });

        connection.on('ChoreChanged', (evt) => {
            console.log('[CalendarRealtime] ChoreChanged:', evt);
            if (eventHandlers.onChoreChanged) {
                eventHandlers.onChoreChanged(evt);
            } else {
                triggerShadowRefresh();
            }
        });

        connection.on('OnCallChanged', (evt) => {
            console.log('[CalendarRealtime] OnCallChanged:', evt);
            if (eventHandlers.onOnCallChanged) {
                eventHandlers.onOnCallChanged(evt);
            } else {
                triggerShadowRefresh();
            }
        });
    }

    /**
     * Start SignalR connection with timeout.
     */
    async function startConnection() {
        if (connectionState === 'connecting' || connectionState === 'connected') {
            return;
        }

        connectionState = 'connecting';

        try {
            const timeoutPromise = new Promise((_, reject) => {
                setTimeout(() => reject(new Error('Connection timeout')), CONFIG.connectionTimeout);
            });

            await Promise.race([connection.start(), timeoutPromise]);

            connectionState = 'connected';
            disconnectedSince = null;
            reconnectAttempts = 0;
            console.log('[CalendarRealtime] Connected to hub');

            updateConnectionIndicator();
            stopPolling();
            await joinGroup();

        } catch (error) {
            connectionState = 'disconnected';
            disconnectedSince = disconnectedSince || Date.now();
            console.warn('[CalendarRealtime] Connection failed:', error.message);
            updateConnectionIndicator();
            checkPollingNeeded();
            attemptReconnect();
        }
    }

    /**
     * Join the current calendar group.
     */
    async function joinGroup() {
        if (connectionState !== 'connected' || !currentGroup) return;

        try {
            await connection.invoke('JoinCalendarGroup', currentGroup);
            console.log('[CalendarRealtime] Joined group:', currentGroup);
        } catch (error) {
            console.error('[CalendarRealtime] Failed to join group:', error);
        }
    }

    /**
     * Rejoin group after reconnection.
     */
    async function rejoinGroup() {
        await joinGroup();
    }

    /**
     * Leave current group (e.g., when changing filters).
     */
    async function leaveGroup() {
        if (connectionState !== 'connected' || !currentGroup) return;

        try {
            await connection.invoke('LeaveCalendarGroup', currentGroup);
            console.log('[CalendarRealtime] Left group:', currentGroup);
        } catch (error) {
            console.error('[CalendarRealtime] Failed to leave group:', error);
        }
    }

    /**
     * Change the calendar group (e.g., when filters change).
     */
    async function changeGroup(newCalendarType, newScope) {
        await leaveGroup();
        currentGroup = buildGroupName(newCalendarType, newScope);
        await joinGroup();
        triggerShadowRefresh(); // Refresh with new scope
    }

    /**
     * Attempt to reconnect with exponential backoff.
     */
    function attemptReconnect() {
        if (reconnectAttempts >= CONFIG.maxReconnectAttempts) {
            console.warn('[CalendarRealtime] Max reconnect attempts reached, using polling only');
            return;
        }

        const baseDelay = CONFIG.reconnectDelays[Math.min(reconnectAttempts, CONFIG.reconnectDelays.length - 1)];
        // Add random jitter (0-50% of base delay) to prevent thundering herd on server restart
        const jitter = Math.floor(Math.random() * baseDelay * 0.5);
        const delay = baseDelay + jitter;
        reconnectAttempts++;

        setTimeout(() => {
            if (connectionState === 'disconnected') {
                startConnection();
            }
        }, delay);
    }

    /**
     * Check if polling should be activated.
     */
    function checkPollingNeeded() {
        if (pollingTimer) return; // Already polling

        if (disconnectedSince && (Date.now() - disconnectedSince) >= CONFIG.pollingActivationDelay) {
            startPolling();
        } else if (disconnectedSince) {
            // Check again after the remaining time
            const remaining = CONFIG.pollingActivationDelay - (Date.now() - disconnectedSince);
            setTimeout(checkPollingNeeded, remaining + 100);
        }
    }

    /**
     * Start periodic polling as fallback.
     */
    function startPolling() {
        if (pollingTimer) return;

        console.log('[CalendarRealtime] Starting polling fallback');
        pollingTimer = setInterval(() => {
            if (isPageVisible) {
                triggerShadowRefresh();
            }
        }, CONFIG.pollingInterval);
    }

    /**
     * Stop periodic polling.
     */
    function stopPolling() {
        if (pollingTimer) {
            console.log('[CalendarRealtime] Stopping polling');
            clearInterval(pollingTimer);
            pollingTimer = null;
        }
    }

    // Track event handlers for cleanup
    let shadowRefreshHandlers = [];

    /**
     * Set up shadow refresh triggers for user interactions.
     */
    function setupShadowRefreshTriggers() {
        // Remove any previous listeners first
        removeShadowRefreshTriggers();

        function addTrackedListener(target, event, handler) {
            target.addEventListener(event, handler);
            shadowRefreshHandlers.push({ target, event, handler });
        }

        // Cell click - refresh to get latest data before editing
        addTrackedListener(document, 'click', (e) => {
            const cell = e.target.closest('.excel-cell[data-editable="true"]');
            if (cell) {
                triggerShadowRefresh();
            }
        });

        // Date navigation buttons
        addTrackedListener(document, 'click', (e) => {
            const navButton = e.target.closest('[data-date-nav], .date-nav-btn, .calendar-nav-btn');
            if (navButton) {
                // Slight delay to let the page update
                setTimeout(() => triggerShadowRefresh(), 100);
            }
        });

        // Filter/scope changes
        addTrackedListener(document, 'change', (e) => {
            const scopeSelector = e.target.closest('[data-scope-filter], .scope-switcher select, .view-mode-toggle');
            if (scopeSelector) {
                triggerShadowRefresh();
            }
        });

        // Form submissions that modify data
        addTrackedListener(document, 'submit', (e) => {
            const form = e.target;
            if (form.closest('.excel-calendar') || form.matches('[data-calendar-form]')) {
                // Refresh after form completes (handled by fetch response typically)
                setTimeout(() => triggerShadowRefresh(), 500);
            }
        });
    }

    /**
     * Remove shadow refresh event listeners to prevent memory leaks.
     */
    function removeShadowRefreshTriggers() {
        for (const { target, event, handler } of shadowRefreshHandlers) {
            target.removeEventListener(event, handler);
        }
        shadowRefreshHandlers = [];
    }

    /**
     * Set up page visibility change handler.
     */
    function setupVisibilityHandler() {
        document.addEventListener('visibilitychange', () => {
            isPageVisible = document.visibilityState === 'visible';

            if (isPageVisible) {
                console.log('[CalendarRealtime] Page became visible, refreshing');

                // Check if we've been away for a while
                const hiddenDuration = Date.now() - lastRefreshTime;
                if (hiddenDuration > 30000) { // Away for more than 30 seconds
                    triggerShadowRefresh();
                }

                // Re-establish connection if needed
                if (connectionState === 'disconnected') {
                    startConnection();
                }
            }
        });

        // Also trigger on window focus (for tab switching)
        window.addEventListener('focus', () => {
            const timeSinceRefresh = Date.now() - lastRefreshTime;
            if (timeSinceRefresh > 15000) { // 15 seconds since last refresh
                triggerShadowRefresh();
            }
        });
    }

    /**
     * Trigger a shadow refresh with debouncing.
     */
    function triggerShadowRefresh() {
        if (shadowRefreshPending) return;

        shadowRefreshPending = true;

        if (shadowRefreshTimer) {
            clearTimeout(shadowRefreshTimer);
        }

        shadowRefreshTimer = setTimeout(async () => {
            shadowRefreshPending = false;
            shadowRefreshTimer = null;

            if (refreshCallback && isPageVisible) {
                try {
                    console.log('[CalendarRealtime] Executing shadow refresh');
                    await refreshCallback();
                    lastRefreshTime = Date.now();
                } catch (error) {
                    console.error('[CalendarRealtime] Shadow refresh failed:', error);
                }
            }
        }, CONFIG.shadowRefreshDebounce);
    }

    /**
     * Update the visual connection status indicator (Item 64: stale data indicator).
     * Shows a banner when SignalR is disconnected so users know data may be stale.
     */
    function updateConnectionIndicator() {
        const dir = document.documentElement.dir || 'ltr';
        const disconnectedMsg = window.AppLocalizer?.Realtime_Disconnected || 'Connection lost. Retrying...';
        const reconnectedMsg = window.AppLocalizer?.Realtime_Reconnected || 'Connection restored.';

        let indicator = document.getElementById('calendar-connection-indicator');
        if (!indicator) {
            indicator = document.createElement('div');
            indicator.id = 'calendar-connection-indicator';
            indicator.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:9999;text-align:center;padding:4px 12px;font-size:0.85rem;transition:transform 0.3s ease;direction:' + dir + ';';
            document.body.appendChild(indicator);
        }

        if (connectionState === 'connected') {
            indicator.style.transform = 'translateY(-100%)';
            indicator.textContent = '';
        } else if (connectionState === 'reconnecting') {
            indicator.style.background = '#fff3cd';
            indicator.style.color = '#856404';
            indicator.style.borderBottom = '1px solid #ffc107';
            indicator.style.transform = 'translateY(0)';
            indicator.textContent = '\u26A0 ' + disconnectedMsg;
        } else {
            indicator.style.background = '#f8d7da';
            indicator.style.color = '#721c24';
            indicator.style.borderBottom = '1px solid #f5c6cb';
            indicator.style.transform = 'translateY(0)';
            const elapsed = disconnectedSince ? Math.floor((Date.now() - disconnectedSince) / 1000) : 0;
            indicator.textContent = '\u26A0 ' + disconnectedMsg + ' (' + elapsed + 's)';
        }
    }

    /**
     * Get current connection status.
     */
    function getStatus() {
        return {
            connectionState,
            currentGroup,
            isPolling: !!pollingTimer,
            disconnectedSince,
            reconnectAttempts,
            lastRefreshTime: new Date(lastRefreshTime).toISOString()
        };
    }

    /**
     * Manually trigger a refresh.
     */
    function refresh() {
        triggerShadowRefresh();
    }

    /**
     * Cleanup - call when leaving the page.
     */
    async function dispose() {
        stopPolling();
        removeShadowRefreshTriggers();

        if (elapsedUpdateTimer) {
            clearInterval(elapsedUpdateTimer);
            elapsedUpdateTimer = null;
        }

        if (shadowRefreshTimer) {
            clearTimeout(shadowRefreshTimer);
        }

        if (connection) {
            await leaveGroup();
            await connection.stop();
            connection = null;
        }

        connectionState = 'disconnected';
        currentGroup = null;
        console.log('[CalendarRealtime] Disposed');
    }

    // Update stale indicator elapsed time every 10s while disconnected
    elapsedUpdateTimer = setInterval(() => {
        if (connectionState !== 'connected' && disconnectedSince) {
            updateConnectionIndicator();
        }
    }, 10000);

    // Expose public API
    window.CalendarRealtime = {
        initialize,
        changeGroup,
        refresh,
        getStatus,
        dispose
    };

    // Auto-dispose on page unload
    window.addEventListener('beforeunload', dispose);

})();
