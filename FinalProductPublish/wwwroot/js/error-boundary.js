/**
 * JavaScript Error Boundary System (B-051)
 *
 * Provides:
 * - Global error boundary with fallback UI
 * - Component-level error boundaries for non-critical components
 * - Integration with telemetry.js for error logging
 * - Integration with error-states.js for toast notifications
 * - Graceful degradation when JS errors occur
 *
 * Usage:
 * // Wrap a function with error boundary
 * ErrorBoundary.wrap(myRiskyFunction, { componentName: 'MyComponent' });
 *
 * // Wrap a DOM element with error boundary
 * ErrorBoundary.wrapElement(element, { componentName: 'MyWidget' });
 *
 * // Mark a component as critical (will show full-page error on failure)
 * ErrorBoundary.markCritical(element);
 */

(function() {
    'use strict';

    // Configuration
    const CONFIG = {
        maxErrorsBeforeFullPage: 5,     // Show full-page error after this many errors
        errorResetInterval: 60000,       // Reset error count after 1 minute of no errors
        showStackInDev: true,            // Show stack trace in development mode
        retryDelayMs: 1000               // Delay before retry
    };

    // Localized messages with fallbacks
    const MESSAGES = {
        'en-US': {
            somethingWentWrong: 'Something went wrong',
            somethingWentWrongDesc: 'An unexpected error occurred. Please try again.',
            retry: 'Retry',
            reload: 'Reload Page',
            dismiss: 'Dismiss',
            componentError: 'This section encountered an error',
            componentErrorDesc: 'Click retry to reload this section.',
            criticalError: 'Critical Error',
            criticalErrorDesc: 'A critical error occurred. Please reload the page to continue.',
            multipleErrors: 'Multiple errors detected',
            multipleErrorsDesc: 'The page is experiencing issues. Please reload.',
            errorDetails: 'Error Details',
            hideDetails: 'Hide Details'
        },
        'he-IL': {
            somethingWentWrong: 'משהו השתבש',
            somethingWentWrongDesc: 'אירעה שגיאה בלתי צפויה. אנא נסה שוב.',
            retry: 'נסה שוב',
            reload: 'טען מחדש את הדף',
            dismiss: 'סגור',
            componentError: 'חלק זה נתקל בשגיאה',
            componentErrorDesc: 'לחץ על נסה שוב כדי לטעון מחדש את החלק הזה.',
            criticalError: 'שגיאה קריטית',
            criticalErrorDesc: 'אירעה שגיאה קריטית. אנא טען מחדש את הדף כדי להמשיך.',
            multipleErrors: 'זוהו שגיאות מרובות',
            multipleErrorsDesc: 'הדף חווה בעיות. אנא טען מחדש.',
            errorDetails: 'פרטי השגיאה',
            hideDetails: 'הסתר פרטים'
        }
    };

    // State
    let errorCount = 0;
    let lastErrorTime = 0;
    let fullPageErrorShown = false;
    let componentErrors = new Map(); // Track errors by component
    let originalErrorHandler = null;
    let originalRejectionHandler = null;

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
     * Check if we're in development mode
     */
    function isDevelopment() {
        return window.location.hostname === 'localhost' ||
               window.location.hostname === '127.0.0.1' ||
               window.location.hostname.includes('.local');
    }

    /**
     * Log error to telemetry (if available)
     */
    function logToTelemetry(error, context = {}) {
        // Use the existing telemetry trackError if available
        if (window.__telemetry && typeof window.__telemetry.trackError === 'function') {
            window.__telemetry.trackError(error, context);
        }

        // Also track as an event for error boundary specific tracking
        if (typeof window.trackEvent === 'function') {
            window.trackEvent('error_boundary_triggered', {
                errorMessage: error?.message || String(error),
                componentName: context.componentName || 'unknown',
                isCritical: context.isCritical || false,
                url: window.location.pathname
            });
        }

        // Always log to console in development
        if (isDevelopment()) {
            console.error('[ErrorBoundary]', error, context);
        }
    }

    /**
     * Show toast notification for error (uses error-states.js if available)
     */
    function showErrorToast(message, options = {}) {
        if (window.ErrorStates && typeof window.ErrorStates.showError === 'function') {
            window.ErrorStates.showError(message, getMessage('somethingWentWrong'), {
                onRetry: options.onRetry,
                duration: options.duration || 8000
            });
        } else if (typeof showToast === 'function') {
            // Fallback to site.js showToast
            showToast(message, 'error');
        } else {
            // Last resort: console
            console.error('[ErrorBoundary Toast]', message);
        }
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
     * Create component-level fallback UI
     */
    function createComponentFallback(element, error, options = {}) {
        const { componentName = 'Component', onRetry } = options;
        const culture = getCurrentCulture();
        const isRtl = culture === 'he-IL';

        const fallback = document.createElement('div');
        fallback.className = 'error-boundary-fallback error-boundary-fallback--component';
        fallback.setAttribute('role', 'alert');
        fallback.setAttribute('aria-live', 'polite');
        fallback.dir = isRtl ? 'rtl' : 'ltr';

        fallback.innerHTML = `
            <div class="error-boundary-fallback__icon" aria-hidden="true">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="10"></circle>
                    <line x1="12" y1="8" x2="12" y2="12"></line>
                    <line x1="12" y1="16" x2="12.01" y2="16"></line>
                </svg>
            </div>
            <div class="error-boundary-fallback__content">
                <div class="error-boundary-fallback__title">${escapeHtml(getMessage('componentError'))}</div>
                <div class="error-boundary-fallback__desc">${escapeHtml(getMessage('componentErrorDesc'))}</div>
            </div>
            <div class="error-boundary-fallback__actions">
                <button type="button" class="error-boundary-fallback__btn error-boundary-fallback__btn--retry">
                    ${escapeHtml(getMessage('retry'))}
                </button>
                <button type="button" class="error-boundary-fallback__btn error-boundary-fallback__btn--dismiss">
                    ${escapeHtml(getMessage('dismiss'))}
                </button>
            </div>
        `;

        // Add error details in development mode
        if (isDevelopment() && CONFIG.showStackInDev && error) {
            const details = document.createElement('details');
            details.className = 'error-boundary-fallback__details';
            details.innerHTML = `
                <summary>${escapeHtml(getMessage('errorDetails'))}</summary>
                <pre>${escapeHtml(error.stack || error.message || String(error))}</pre>
            `;
            fallback.appendChild(details);
        }

        // Event handlers
        const retryBtn = fallback.querySelector('.error-boundary-fallback__btn--retry');
        const dismissBtn = fallback.querySelector('.error-boundary-fallback__btn--dismiss');

        retryBtn.addEventListener('click', () => {
            fallback.remove();
            if (onRetry) {
                onRetry();
            } else {
                // Default: try to restore original element
                if (element && element._originalContent) {
                    element.innerHTML = element._originalContent;
                    element.style.display = element._originalDisplay || '';
                }
            }
        });

        dismissBtn.addEventListener('click', () => {
            fallback.remove();
        });

        return fallback;
    }

    /**
     * Create full-page error fallback UI
     */
    function createFullPageFallback(error, options = {}) {
        const { isCritical = false, showReload = true } = options;
        const culture = getCurrentCulture();
        const isRtl = culture === 'he-IL';

        // Remove any existing full-page fallback
        const existing = document.getElementById('error-boundary-fullpage');
        if (existing) {
            existing.remove();
        }

        const overlay = document.createElement('div');
        overlay.id = 'error-boundary-fullpage';
        overlay.className = 'error-boundary-fullpage';
        overlay.setAttribute('role', 'alertdialog');
        overlay.setAttribute('aria-modal', 'true');
        overlay.setAttribute('aria-labelledby', 'error-boundary-title');
        overlay.dir = isRtl ? 'rtl' : 'ltr';

        const title = isCritical ? getMessage('criticalError') :
                      errorCount >= CONFIG.maxErrorsBeforeFullPage ? getMessage('multipleErrors') :
                      getMessage('somethingWentWrong');

        const desc = isCritical ? getMessage('criticalErrorDesc') :
                     errorCount >= CONFIG.maxErrorsBeforeFullPage ? getMessage('multipleErrorsDesc') :
                     getMessage('somethingWentWrongDesc');

        overlay.innerHTML = `
            <div class="error-boundary-fullpage__content">
                <div class="error-boundary-fullpage__icon" aria-hidden="true">
                    <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
                        <circle cx="12" cy="12" r="10"></circle>
                        <line x1="15" y1="9" x2="9" y2="15"></line>
                        <line x1="9" y1="9" x2="15" y2="15"></line>
                    </svg>
                </div>
                <h1 id="error-boundary-title" class="error-boundary-fullpage__title">${escapeHtml(title)}</h1>
                <p class="error-boundary-fullpage__desc">${escapeHtml(desc)}</p>
                <div class="error-boundary-fullpage__actions">
                    ${showReload ? `
                        <button type="button" class="error-boundary-fullpage__btn error-boundary-fullpage__btn--primary" data-action="reload">
                            ${escapeHtml(getMessage('reload'))}
                        </button>
                    ` : ''}
                    ${!isCritical ? `
                        <button type="button" class="error-boundary-fullpage__btn error-boundary-fullpage__btn--secondary" data-action="dismiss">
                            ${escapeHtml(getMessage('dismiss'))}
                        </button>
                    ` : ''}
                </div>
            </div>
        `;

        // Add error details in development mode
        if (isDevelopment() && CONFIG.showStackInDev && error) {
            const detailsContainer = overlay.querySelector('.error-boundary-fullpage__content');
            const details = document.createElement('details');
            details.className = 'error-boundary-fullpage__details';
            details.innerHTML = `
                <summary>${escapeHtml(getMessage('errorDetails'))}</summary>
                <pre>${escapeHtml(error.stack || error.message || String(error))}</pre>
            `;
            detailsContainer.appendChild(details);
        }

        // Event handlers
        overlay.addEventListener('click', (e) => {
            const action = e.target.dataset?.action;
            if (action === 'reload') {
                window.location.reload();
            } else if (action === 'dismiss') {
                overlay.remove();
                fullPageErrorShown = false;
            }
        });

        // Handle escape key (only for non-critical errors)
        if (!isCritical) {
            const handleEscape = (e) => {
                if (e.key === 'Escape') {
                    overlay.remove();
                    fullPageErrorShown = false;
                    document.removeEventListener('keydown', handleEscape);
                }
            };
            document.addEventListener('keydown', handleEscape);
        }

        return overlay;
    }

    /**
     * Show full-page error fallback
     */
    function showFullPageError(error, options = {}) {
        if (fullPageErrorShown && !options.force) {
            return; // Don't stack multiple full-page errors
        }

        fullPageErrorShown = true;
        const fallback = createFullPageFallback(error, options);
        document.body.appendChild(fallback);

        // Focus the primary button for accessibility
        const primaryBtn = fallback.querySelector('.error-boundary-fullpage__btn--primary');
        if (primaryBtn) {
            setTimeout(() => primaryBtn.focus(), 100);
        }

        // Prevent body scroll
        document.body.style.overflow = 'hidden';

        logToTelemetry(error, {
            componentName: 'FullPageError',
            isCritical: options.isCritical || false
        });
    }

    /**
     * Show component-level error fallback
     */
    function showComponentError(element, error, options = {}) {
        if (!element) return;

        // Store original content for potential restoration
        element._originalContent = element.innerHTML;
        element._originalDisplay = element.style.display;

        // Create and insert fallback
        const fallback = createComponentFallback(element, error, options);

        // Replace element content with fallback
        element.innerHTML = '';
        element.appendChild(fallback);

        // Track component error
        componentErrors.set(element, {
            error,
            timestamp: Date.now(),
            componentName: options.componentName
        });

        logToTelemetry(error, {
            componentName: options.componentName || 'UnknownComponent',
            isCritical: false
        });
    }

    /**
     * Handle global error
     */
    function handleGlobalError(message, source, lineno, colno, error) {
        const now = Date.now();

        // Reset error count if enough time has passed
        if (now - lastErrorTime > CONFIG.errorResetInterval) {
            errorCount = 0;
        }

        errorCount++;
        lastErrorTime = now;

        // Log to telemetry
        logToTelemetry(error || new Error(message), {
            source,
            lineno,
            colno
        });

        // Check if we should show full-page error
        if (errorCount >= CONFIG.maxErrorsBeforeFullPage) {
            showFullPageError(error || new Error(message), {
                force: true
            });
            return true; // Prevent default
        }

        // Show toast for single errors
        showErrorToast(getMessage('somethingWentWrongDesc'));

        return false; // Allow default error handling to continue
    }

    /**
     * Handle unhandled promise rejection
     */
    function handleUnhandledRejection(event) {
        const error = event.reason;
        const errorObj = error instanceof Error ? error : new Error(String(error));

        const now = Date.now();

        // Reset error count if enough time has passed
        if (now - lastErrorTime > CONFIG.errorResetInterval) {
            errorCount = 0;
        }

        errorCount++;
        lastErrorTime = now;

        // Log to telemetry
        logToTelemetry(errorObj, {
            type: 'unhandledrejection'
        });

        // Check if we should show full-page error
        if (errorCount >= CONFIG.maxErrorsBeforeFullPage) {
            showFullPageError(errorObj, {
                force: true
            });
        } else {
            // Show toast for single errors
            showErrorToast(getMessage('somethingWentWrongDesc'));
        }
    }

    /**
     * Wrap a function with error boundary
     */
    function wrap(fn, options = {}) {
        const { componentName = 'WrappedFunction', onError, element } = options;

        return function(...args) {
            try {
                const result = fn.apply(this, args);

                // Handle promise return
                if (result && typeof result.catch === 'function') {
                    return result.catch((error) => {
                        if (onError) {
                            onError(error);
                        } else if (element) {
                            showComponentError(element, error, { componentName });
                        } else {
                            showErrorToast(getMessage('somethingWentWrongDesc'));
                        }
                        logToTelemetry(error, { componentName });
                    });
                }

                return result;
            } catch (error) {
                if (onError) {
                    onError(error);
                } else if (element) {
                    showComponentError(element, error, { componentName });
                } else {
                    showErrorToast(getMessage('somethingWentWrongDesc'));
                }
                logToTelemetry(error, { componentName });
            }
        };
    }

    /**
     * Wrap a DOM element with error boundary
     * Catches errors from event handlers and mutation observers
     */
    function wrapElement(element, options = {}) {
        if (!element) return;

        const { componentName = element.id || 'WrappedElement' } = options;

        // Store reference to the element
        element._errorBoundaryOptions = {
            componentName,
            isCritical: options.isCritical || false,
            onRetry: options.onRetry
        };

        // Wrap all event handlers on the element
        const originalAddEventListener = element.addEventListener.bind(element);
        element.addEventListener = function(type, handler, options) {
            const wrappedHandler = wrap(handler, {
                componentName: `${componentName}.${type}`,
                element: element,
                onError: (error) => {
                    showComponentError(element, error, {
                        componentName,
                        onRetry: element._errorBoundaryOptions.onRetry
                    });
                }
            });
            return originalAddEventListener(type, wrappedHandler, options);
        };

        return element;
    }

    /**
     * Mark an element as critical (will show full-page error on failure)
     */
    function markCritical(element) {
        if (!element) return;
        element.dataset.errorBoundaryCritical = 'true';
    }

    /**
     * Initialize error boundary handlers
     */
    function initialize() {
        // Store original handlers
        originalErrorHandler = window.onerror;
        originalRejectionHandler = window.onunhandledrejection;

        // Install our enhanced error handler
        window.onerror = function(message, source, lineno, colno, error) {
            // Call our handler first
            const shouldPreventDefault = handleGlobalError(message, source, lineno, colno, error);

            // Then call original handler (from telemetry.js)
            if (originalErrorHandler) {
                originalErrorHandler(message, source, lineno, colno, error);
            }

            return shouldPreventDefault;
        };

        // Install our enhanced rejection handler
        window.addEventListener('unhandledrejection', handleUnhandledRejection);

        // Inject styles
        injectStyles();

        console.log('[ErrorBoundary] Initialized');
    }

    /**
     * Inject required CSS styles
     */
    function injectStyles() {
        if (document.getElementById('error-boundary-styles')) {
            return;
        }

        const style = document.createElement('style');
        style.id = 'error-boundary-styles';
        style.textContent = `
            /* Error Boundary Styles (B-051) */

            /* Component-level fallback */
            .error-boundary-fallback {
                display: flex;
                flex-direction: column;
                align-items: center;
                justify-content: center;
                padding: 1.5rem;
                background: var(--surface-soft, #f8fafc);
                border: 1px solid var(--border, #e2e8f0);
                border-radius: 0.5rem;
                text-align: center;
                min-height: 120px;
            }

            .error-boundary-fallback--component {
                gap: 0.75rem;
            }

            .error-boundary-fallback__icon {
                color: var(--warning, #D4A017);
            }

            .error-boundary-fallback__content {
                display: flex;
                flex-direction: column;
                gap: 0.25rem;
            }

            .error-boundary-fallback__title {
                font-weight: 600;
                color: var(--text, #1a1f2b);
                font-size: 0.875rem;
            }

            .error-boundary-fallback__desc {
                font-size: 0.8125rem;
                color: var(--muted, #64748b);
            }

            .error-boundary-fallback__actions {
                display: flex;
                gap: 0.5rem;
                margin-top: 0.5rem;
            }

            .error-boundary-fallback__btn {
                padding: 0.5rem 1rem;
                border-radius: 0.375rem;
                font-size: 0.8125rem;
                font-weight: 500;
                cursor: pointer;
                transition: all 0.15s ease;
            }

            .error-boundary-fallback__btn--retry {
                background: var(--primary, #1E3A5F);
                color: white;
                border: none;
            }

            .error-boundary-fallback__btn--retry:hover {
                background: var(--primary-hover, #2D4A6F);
            }

            .error-boundary-fallback__btn--dismiss {
                background: transparent;
                color: var(--muted, #64748b);
                border: 1px solid var(--border, #e2e8f0);
            }

            .error-boundary-fallback__btn--dismiss:hover {
                background: var(--surface-hover, #f0f4f8);
            }

            .error-boundary-fallback__details {
                margin-top: 1rem;
                width: 100%;
                text-align: left;
            }

            .error-boundary-fallback__details summary {
                cursor: pointer;
                font-size: 0.75rem;
                color: var(--muted, #64748b);
            }

            .error-boundary-fallback__details pre {
                margin-top: 0.5rem;
                padding: 0.75rem;
                background: var(--surface, #fff);
                border: 1px solid var(--border, #e2e8f0);
                border-radius: 0.25rem;
                font-size: 0.6875rem;
                overflow-x: auto;
                white-space: pre-wrap;
                word-break: break-word;
                color: var(--danger, #9B2C2C);
            }

            /* Full-page fallback */
            .error-boundary-fullpage {
                position: fixed;
                inset: 0;
                z-index: 10000;
                display: flex;
                align-items: center;
                justify-content: center;
                background: rgba(0, 0, 0, 0.6);
                backdrop-filter: blur(4px);
                animation: error-boundary-fade-in 0.2s ease-out;
            }

            @keyframes error-boundary-fade-in {
                from { opacity: 0; }
                to { opacity: 1; }
            }

            .error-boundary-fullpage__content {
                max-width: 420px;
                width: calc(100% - 2rem);
                padding: 2rem;
                background: var(--surface, #fff);
                border-radius: 1rem;
                text-align: center;
                box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
                animation: error-boundary-scale-in 0.2s ease-out;
            }

            @keyframes error-boundary-scale-in {
                from { opacity: 0; transform: scale(0.95) translateY(-10px); }
                to { opacity: 1; transform: scale(1) translateY(0); }
            }

            .error-boundary-fullpage__icon {
                display: flex;
                justify-content: center;
                margin-bottom: 1rem;
                color: var(--danger, #9B2C2C);
            }

            .error-boundary-fullpage__title {
                margin: 0 0 0.5rem;
                font-size: 1.5rem;
                font-weight: 700;
                color: var(--text, #1a1f2b);
            }

            .error-boundary-fullpage__desc {
                margin: 0 0 1.5rem;
                font-size: 0.9375rem;
                color: var(--muted, #64748b);
                line-height: 1.5;
            }

            .error-boundary-fullpage__actions {
                display: flex;
                gap: 0.75rem;
                justify-content: center;
                flex-wrap: wrap;
            }

            .error-boundary-fullpage__btn {
                padding: 0.75rem 1.5rem;
                border-radius: 0.5rem;
                font-size: 0.875rem;
                font-weight: 600;
                cursor: pointer;
                transition: all 0.15s ease;
            }

            .error-boundary-fullpage__btn--primary {
                background: var(--primary, #1E3A5F);
                color: white;
                border: none;
            }

            .error-boundary-fullpage__btn--primary:hover {
                background: var(--primary-hover, #2D4A6F);
            }

            .error-boundary-fullpage__btn--primary:focus {
                outline: none;
                box-shadow: 0 0 0 3px rgba(30, 58, 95, 0.3);
            }

            .error-boundary-fullpage__btn--secondary {
                background: transparent;
                color: var(--muted, #64748b);
                border: 1px solid var(--border, #e2e8f0);
            }

            .error-boundary-fullpage__btn--secondary:hover {
                background: var(--surface-hover, #f0f4f8);
            }

            .error-boundary-fullpage__details {
                margin-top: 1.5rem;
                text-align: left;
            }

            .error-boundary-fullpage__details summary {
                cursor: pointer;
                font-size: 0.8125rem;
                color: var(--muted, #64748b);
            }

            .error-boundary-fullpage__details pre {
                margin-top: 0.75rem;
                padding: 1rem;
                background: var(--surface-soft, #f8fafc);
                border: 1px solid var(--border, #e2e8f0);
                border-radius: 0.5rem;
                font-size: 0.75rem;
                overflow-x: auto;
                white-space: pre-wrap;
                word-break: break-word;
                max-height: 200px;
                color: var(--danger, #9B2C2C);
            }

            /* Respect reduced motion preference (B-001-EXT) */
            @media (prefers-reduced-motion: reduce) {
                .error-boundary-fullpage,
                .error-boundary-fullpage__content {
                    animation: none;
                }
            }

            /* RTL Support */
            [dir="rtl"] .error-boundary-fallback__details,
            [dir="rtl"] .error-boundary-fullpage__details {
                text-align: right;
            }

            /* Dark mode support */
            [data-theme="dark"] .error-boundary-fallback {
                background: var(--surface-soft, #1e293b);
            }

            [data-theme="dark"] .error-boundary-fullpage__content {
                background: var(--surface, #0f172a);
            }

            [data-theme="dark"] .error-boundary-fallback__details pre,
            [data-theme="dark"] .error-boundary-fullpage__details pre {
                background: var(--surface, #0f172a);
            }
        `;

        document.head.appendChild(style);
    }

    // Public API
    window.ErrorBoundary = {
        // Core methods
        wrap: wrap,
        wrapElement: wrapElement,
        markCritical: markCritical,

        // UI methods
        showComponentError: showComponentError,
        showFullPageError: showFullPageError,

        // Utility methods
        getMessage: getMessage,
        isDevelopment: isDevelopment,

        // Configuration
        config: CONFIG,

        // State inspection (for debugging)
        getErrorCount: () => errorCount,
        getComponentErrors: () => new Map(componentErrors),
        resetErrorCount: () => { errorCount = 0; }
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }

})();
