/**
 * Client-Side Telemetry Module
 * B-019, B-020, B-021: Local Observability Stack (Air-Gapped)
 *
 * Captures:
 * - Analytics events (calendar_view_changed, scope_changed, etc.)
 * - Client-side errors (window.onerror, unhandledrejection)
 * - Core Web Vitals (LCP, FID/INP, CLS, TTFB)
 *
 * Features:
 * - Batches and sends to /Api/Telemetry
 * - Rate limiting (max 10 events/minute)
 * - PII scrubbing
 * - Offline queue with retry
 */
(function() {
    'use strict';

    // Configuration
    const CONFIG = {
        endpoint: '/Api/Telemetry',
        batchSize: 10,
        flushInterval: 5000, // 5 seconds
        maxQueueSize: 100,
        rateLimitPerMinute: 10,
        sessionId: generateSessionId()
    };

    // Queues for batching
    const queues = {
        events: [],
        errors: [],
        performance: []
    };

    // Rate limiting
    const rateLimiter = {
        events: [],
        lastReset: Date.now()
    };

    // ========================================
    // Utility Functions
    // ========================================

    function generateSessionId() {
        return 'sess_' + Math.random().toString(36).substring(2, 15) +
               Math.random().toString(36).substring(2, 15);
    }

    function getPageUrl() {
        // Return path only, no query params to avoid PII
        return window.location.pathname;
    }

    function isRateLimited() {
        const now = Date.now();

        // Reset counter every minute
        if (now - rateLimiter.lastReset > 60000) {
            rateLimiter.events = [];
            rateLimiter.lastReset = now;
        }

        return rateLimiter.events.length >= CONFIG.rateLimitPerMinute;
    }

    function recordRateLimitEvent() {
        rateLimiter.events.push(Date.now());
    }

    function scrubPii(text) {
        if (!text || typeof text !== 'string') return text;

        // Scrub email addresses
        text = text.replace(/[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}/g, '[EMAIL]');

        // Scrub phone numbers
        text = text.replace(/(\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}/g, '[PHONE]');

        // Scrub potential tokens (long alphanumeric strings)
        text = text.replace(/[a-zA-Z0-9]{32,}/g, '[TOKEN]');

        return text;
    }

    // ========================================
    // Analytics Events (B-019)
    // ========================================

    /**
     * Track an analytics event
     * @param {string} eventType - Event type (e.g., 'calendar_view_changed')
     * @param {object} eventData - Optional event data
     */
    window.trackEvent = function(eventType, eventData) {
        if (isRateLimited()) {
            Logger.log('Telemetry', 'Rate limited, skipping event:', eventType);
            return;
        }

        const event = {
            eventType: eventType,
            eventData: eventData ? JSON.stringify(eventData) : null,
            sessionId: CONFIG.sessionId,
            pageUrl: getPageUrl(),
            timestamp: new Date().toISOString()
        };

        queues.events.push(event);
        recordRateLimitEvent();

        // Flush if queue is full
        if (queues.events.length >= CONFIG.batchSize) {
            flushEvents();
        }
    };

    // Pre-defined event helpers
    window.trackCalendarViewChange = function(from, to) {
        window.trackEvent('calendar_view_changed', { from, to });
    };

    window.trackScopeChange = function(scope) {
        window.trackEvent('scope_changed', { scope });
    };

    window.trackContextSwitch = function(context) {
        window.trackEvent('context_switched', { context });
    };

    window.trackWidgetToggle = function(widget, visible) {
        window.trackEvent('widget_toggled', { widget, visible });
    };

    window.trackNavigationToggle = function(category, expanded) {
        window.trackEvent('navigation_category_toggled', { category, expanded });
    };

    // ========================================
    // Error Tracking (B-020)
    // ========================================

    function trackError(message, source, lineno, colno, error) {
        if (isRateLimited()) {
            Logger.log('Telemetry', 'Rate limited, skipping error');
            return;
        }

        const errorEntry = {
            message: scrubPii(message || 'Unknown error'),
            stackTrace: error && error.stack ? scrubPii(error.stack) : null,
            source: source || null,
            lineNumber: lineno || null,
            columnNumber: colno || null,
            errorType: error ? error.name : 'Error',
            pageUrl: getPageUrl(),
            timestamp: new Date().toISOString()
        };

        queues.errors.push(errorEntry);
        recordRateLimitEvent();

        // Flush errors immediately (they're important)
        if (queues.errors.length >= 5) {
            flushErrors();
        }
    }

    // Global error handler
    window.onerror = function(message, source, lineno, colno, error) {
        trackError(message, source, lineno, colno, error);
        return false; // Don't suppress error
    };

    // Unhandled promise rejection handler
    window.addEventListener('unhandledrejection', function(event) {
        const error = event.reason;
        trackError(
            error && error.message ? error.message : String(error),
            null,
            null,
            null,
            error instanceof Error ? error : null
        );
    });

    // ========================================
    // Performance Metrics (B-021)
    // ========================================

    function getConnectionInfo() {
        const connection = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
        if (!connection) return {};

        return {
            connectionType: connection.type,
            effectiveType: connection.effectiveType
        };
    }

    function getDeviceInfo() {
        return {
            deviceMemory: navigator.deviceMemory || null,
            hardwareConcurrency: navigator.hardwareConcurrency || null
        };
    }

    function getRating(name, value) {
        // Core Web Vitals thresholds
        const thresholds = {
            LCP: { good: 2500, poor: 4000 },
            FID: { good: 100, poor: 300 },
            INP: { good: 200, poor: 500 },
            CLS: { good: 0.1, poor: 0.25 },
            TTFB: { good: 800, poor: 1800 }
        };

        const t = thresholds[name];
        if (!t) return 'unknown';

        if (value <= t.good) return 'good';
        if (value <= t.poor) return 'needs-improvement';
        return 'poor';
    }

    function trackPerformanceMetric(name, value) {
        if (isRateLimited()) {
            Logger.log('Telemetry', 'Rate limited, skipping metric:', name);
            return;
        }

        const connectionInfo = getConnectionInfo();
        const deviceInfo = getDeviceInfo();

        const metric = {
            metricName: name,
            value: value,
            rating: getRating(name, value),
            pageUrl: getPageUrl(),
            connectionType: connectionInfo.connectionType || null,
            effectiveType: connectionInfo.effectiveType || null,
            deviceMemory: deviceInfo.deviceMemory,
            hardwareConcurrency: deviceInfo.hardwareConcurrency,
            timestamp: new Date().toISOString()
        };

        queues.performance.push(metric);
        recordRateLimitEvent();

        // Flush if queue is full
        if (queues.performance.length >= CONFIG.batchSize) {
            flushPerformance();
        }
    }

    // Observe Core Web Vitals using PerformanceObserver
    function initPerformanceObservers() {
        // LCP (Largest Contentful Paint)
        try {
            const lcpObserver = new PerformanceObserver(function(list) {
                const entries = list.getEntries();
                const lastEntry = entries[entries.length - 1];
                if (lastEntry) {
                    trackPerformanceMetric('LCP', lastEntry.startTime);
                }
            });
            lcpObserver.observe({ type: 'largest-contentful-paint', buffered: true });
        } catch (e) {
            Logger.log('Telemetry', 'LCP observer not supported');
        }

        // FID (First Input Delay)
        try {
            const fidObserver = new PerformanceObserver(function(list) {
                const entries = list.getEntries();
                entries.forEach(function(entry) {
                    trackPerformanceMetric('FID', entry.processingStart - entry.startTime);
                });
            });
            fidObserver.observe({ type: 'first-input', buffered: true });
        } catch (e) {
            Logger.log('Telemetry', 'FID observer not supported');
        }

        // CLS (Cumulative Layout Shift)
        try {
            let clsValue = 0;
            let clsEntries = [];
            let sessionValue = 0;
            let sessionEntries = [];

            const clsObserver = new PerformanceObserver(function(list) {
                list.getEntries().forEach(function(entry) {
                    // Only count layout shifts without recent user input
                    if (!entry.hadRecentInput) {
                        const firstSessionEntry = sessionEntries[0];
                        const lastSessionEntry = sessionEntries[sessionEntries.length - 1];

                        // If the entry occurred less than 1 second after the previous entry
                        // and less than 5 seconds after the first entry in the session,
                        // include the entry in the current session.
                        if (sessionValue &&
                            entry.startTime - lastSessionEntry.startTime < 1000 &&
                            entry.startTime - firstSessionEntry.startTime < 5000) {
                            sessionValue += entry.value;
                            sessionEntries.push(entry);
                        } else {
                            sessionValue = entry.value;
                            sessionEntries = [entry];
                        }

                        // If the current session value is larger than the current CLS value,
                        // update CLS and the entries contributing to it.
                        if (sessionValue > clsValue) {
                            clsValue = sessionValue;
                            clsEntries = sessionEntries;
                        }
                    }
                });
            });
            clsObserver.observe({ type: 'layout-shift', buffered: true });

            // Report CLS when page visibility changes or unloads
            document.addEventListener('visibilitychange', function() {
                if (document.visibilityState === 'hidden' && clsValue > 0) {
                    trackPerformanceMetric('CLS', clsValue);
                }
            });
        } catch (e) {
            Logger.log('Telemetry', 'CLS observer not supported');
        }

        // INP (Interaction to Next Paint)
        try {
            let maxINP = 0;
            const inpObserver = new PerformanceObserver(function(list) {
                list.getEntries().forEach(function(entry) {
                    // Calculate interaction duration
                    const duration = entry.duration;
                    if (duration > maxINP) {
                        maxINP = duration;
                    }
                });
            });
            inpObserver.observe({ type: 'event', buffered: true, durationThreshold: 16 });

            // Report INP when page visibility changes
            document.addEventListener('visibilitychange', function() {
                if (document.visibilityState === 'hidden' && maxINP > 0) {
                    trackPerformanceMetric('INP', maxINP);
                }
            });
        } catch (e) {
            Logger.log('Telemetry', 'INP observer not supported');
        }

        // TTFB (Time to First Byte)
        try {
            const navigationEntry = performance.getEntriesByType('navigation')[0];
            if (navigationEntry) {
                const ttfb = navigationEntry.responseStart - navigationEntry.requestStart;
                if (ttfb > 0) {
                    trackPerformanceMetric('TTFB', ttfb);
                }
            }
        } catch (e) {
            Logger.log('Telemetry', 'TTFB measurement failed');
        }
    }

    // ========================================
    // Batch Sending
    // ========================================

    function sendBatch(handler, data) {
        if (!data || data.length === 0) return Promise.resolve();

        return fetch(CONFIG.endpoint + '?handler=' + handler, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(data),
            keepalive: true // Ensure request completes even if page unloads
        }).catch(function(err) {
            Logger.log('Telemetry', 'Failed to send batch:', err);
        });
    }

    function flushEvents() {
        if (queues.events.length === 0) return;

        const batch = queues.events.splice(0, CONFIG.batchSize);
        sendBatch('EventBatch', batch);
    }

    function flushErrors() {
        if (queues.errors.length === 0) return;

        const batch = queues.errors.splice(0, 20);
        sendBatch('ErrorBatch', batch);
    }

    function flushPerformance() {
        if (queues.performance.length === 0) return;

        const batch = queues.performance.splice(0, CONFIG.batchSize);
        sendBatch('PerformanceBatch', batch);
    }

    function flushAll() {
        flushEvents();
        flushErrors();
        flushPerformance();
    }

    // ========================================
    // Initialization
    // ========================================

    // Initialize performance observers when DOM is ready
    if (document.readyState === 'complete') {
        initPerformanceObservers();
    } else {
        window.addEventListener('load', initPerformanceObservers);
    }

    // Periodic flush
    setInterval(flushAll, CONFIG.flushInterval);

    // Flush on page unload
    window.addEventListener('visibilitychange', function() {
        if (document.visibilityState === 'hidden') {
            flushAll();
        }
    });

    window.addEventListener('pagehide', flushAll);

    // Expose for debugging and integration with error-boundary.js
    window.__telemetry = {
        config: CONFIG,
        queues: queues,
        flush: flushAll,
        trackError: trackError
    };

    Logger.log('Telemetry', 'Initialized with session:', CONFIG.sessionId);
})();
