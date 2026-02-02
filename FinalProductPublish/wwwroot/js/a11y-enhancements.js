/**
 * Accessibility Enhancements (Task 17)
 *
 * Provides automatic accessibility enhancements:
 * 1. Connects form fields with their help text via aria-describedby
 * 2. Provides aria-busy utilities for async operations
 * 3. Announces dynamic content changes to screen readers
 */
(function() {
    'use strict';

    // ============= FORM FIELD HELP TEXT CONNECTIONS =============

    /**
     * Auto-connect form fields with their help text elements.
     * Looks for .form-text, .help-text, or small elements near inputs.
     */
    function enhanceFormFields() {
        // Find all form groups/containers
        var formGroups = document.querySelectorAll('.form-group, .form-field, .mb-3, .mb-4, [class*="form-"]');
        var idCounter = 0;

        formGroups.forEach(function(group) {
            // Find input, select, or textarea in this group
            var input = group.querySelector('input, select, textarea');
            if (!input) return;

            // Skip if already has aria-describedby
            if (input.hasAttribute('aria-describedby')) return;

            // Find help text in same container
            var helpText = group.querySelector('.form-text, .help-text, .text-muted:not(label), small:not(.badge)');
            if (!helpText) return;

            // Ensure help text has an ID
            if (!helpText.id) {
                helpText.id = 'help-' + (input.id || 'field-' + (++idCounter));
            }

            // Connect input to help text
            input.setAttribute('aria-describedby', helpText.id);
        });

        // Also find standalone input + help text patterns
        var helpTexts = document.querySelectorAll('.form-text, .help-text');
        helpTexts.forEach(function(helpText) {
            // Find preceding input element
            var prevElement = helpText.previousElementSibling;
            while (prevElement) {
                if (prevElement.matches('input, select, textarea')) {
                    if (!prevElement.hasAttribute('aria-describedby')) {
                        if (!helpText.id) {
                            helpText.id = 'help-' + (prevElement.id || 'field-' + (++idCounter));
                        }
                        prevElement.setAttribute('aria-describedby', helpText.id);
                    }
                    break;
                }
                prevElement = prevElement.previousElementSibling;
            }
        });
    }

    // ============= ARIA-BUSY UTILITIES =============

    /**
     * Set aria-busy state on an element during async operations
     * @param {HTMLElement} element - The element to mark as busy
     * @param {boolean} isBusy - Whether the element is busy
     */
    function setAriaBusy(element, isBusy) {
        if (!element) return;

        if (isBusy) {
            element.setAttribute('aria-busy', 'true');
            element.classList.add('is-loading');
        } else {
            element.setAttribute('aria-busy', 'false');
            element.classList.remove('is-loading');
        }
    }

    /**
     * Wrap an async function to automatically set aria-busy
     * @param {HTMLElement} element - Element to mark busy during operation
     * @param {Function} asyncFn - Async function to wrap
     * @returns {Function} Wrapped function that manages aria-busy
     */
    function withAriaBusy(element, asyncFn) {
        return function() {
            var args = arguments;
            setAriaBusy(element, true);

            try {
                var result = asyncFn.apply(this, args);

                // Handle promises
                if (result && typeof result.then === 'function') {
                    return result.finally(function() {
                        setAriaBusy(element, false);
                    });
                }

                setAriaBusy(element, false);
                return result;
            } catch (e) {
                setAriaBusy(element, false);
                throw e;
            }
        };
    }

    // ============= LIVE REGION ANNOUNCEMENTS =============

    var liveRegion = null;

    /**
     * Get or create the live region for screen reader announcements
     */
    function getLiveRegion() {
        if (!liveRegion) {
            liveRegion = document.createElement('div');
            liveRegion.id = 'a11y-live-region';
            liveRegion.setAttribute('aria-live', 'polite');
            liveRegion.setAttribute('aria-atomic', 'true');
            liveRegion.className = 'sr-only';
            liveRegion.style.cssText = 'position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0;';
            document.body.appendChild(liveRegion);
        }
        return liveRegion;
    }

    /**
     * Announce a message to screen readers
     * @param {string} message - Message to announce
     * @param {string} priority - 'polite' (default) or 'assertive'
     */
    function announce(message, priority) {
        var region = getLiveRegion();
        region.setAttribute('aria-live', priority === 'assertive' ? 'assertive' : 'polite');

        // Clear and set message (triggers announcement)
        region.textContent = '';

        // Use setTimeout to ensure the change is detected
        setTimeout(function() {
            region.textContent = message;
        }, 100);
    }

    // ============= LANDMARK ROLE VERIFICATION =============

    /**
     * Ensure required landmark roles are present
     * Adds missing roles if semantic elements exist without explicit roles
     */
    function verifyLandmarks() {
        // Ensure main content has role
        var main = document.querySelector('main, [role="main"]');
        if (main && !main.hasAttribute('role')) {
            main.setAttribute('role', 'main');
        }

        // Ensure navigation has role
        var navs = document.querySelectorAll('nav, [role="navigation"]');
        navs.forEach(function(nav) {
            if (!nav.hasAttribute('role')) {
                nav.setAttribute('role', 'navigation');
            }
        });

        // Ensure aside has complementary role
        var asides = document.querySelectorAll('aside, [role="complementary"]');
        asides.forEach(function(aside) {
            if (!aside.hasAttribute('role')) {
                aside.setAttribute('role', 'complementary');
            }
        });

        // Ensure header has banner role (only top-level)
        var header = document.body.querySelector(':scope > header, :scope > [role="banner"]');
        if (header && !header.hasAttribute('role')) {
            header.setAttribute('role', 'banner');
        }

        // Ensure footer has contentinfo role (only top-level)
        var footer = document.body.querySelector(':scope > footer, :scope > [role="contentinfo"]');
        if (footer && !footer.hasAttribute('role')) {
            footer.setAttribute('role', 'contentinfo');
        }
    }

    // ============= REQUIRED FIELD INDICATORS =============

    /**
     * Ensure required fields have proper aria-required attribute
     */
    function enhanceRequiredFields() {
        var requiredFields = document.querySelectorAll('[required], .required input, .required select, .required textarea');

        requiredFields.forEach(function(field) {
            if (!field.hasAttribute('aria-required')) {
                field.setAttribute('aria-required', 'true');
            }
        });
    }

    // ============= ERROR STATE CONNECTIONS =============

    /**
     * Connect validation error messages to their form fields
     */
    function enhanceValidationMessages() {
        // Find validation messages
        var errorMessages = document.querySelectorAll('.field-validation-error, .validation-message, .text-danger[data-valmsg-for]');

        errorMessages.forEach(function(errorMsg) {
            var fieldName = errorMsg.getAttribute('data-valmsg-for') ||
                           errorMsg.getAttribute('for');

            if (fieldName) {
                var field = document.querySelector('[name="' + fieldName + '"], #' + fieldName);
                if (field) {
                    // Ensure error message has ID
                    if (!errorMsg.id) {
                        errorMsg.id = 'error-' + fieldName.replace(/[^a-zA-Z0-9]/g, '-');
                    }

                    // Add to aria-describedby (don't replace existing)
                    var existing = field.getAttribute('aria-describedby') || '';
                    if (!existing.includes(errorMsg.id)) {
                        field.setAttribute('aria-describedby',
                            existing ? existing + ' ' + errorMsg.id : errorMsg.id);
                    }

                    // Mark field as invalid
                    if (errorMsg.textContent.trim()) {
                        field.setAttribute('aria-invalid', 'true');
                    }
                }
            }
        });
    }

    // ============= INITIALIZATION =============

    /**
     * Run all accessibility enhancements
     */
    function enhanceAll() {
        verifyLandmarks();
        enhanceFormFields();
        enhanceRequiredFields();
        enhanceValidationMessages();
    }

    /**
     * Initialize accessibility enhancements
     */
    function initialize() {
        // Run enhancements immediately
        enhanceAll();

        // Re-run after dynamic content loads (with debounce)
        var debounceTimer = null;
        var observer = new MutationObserver(function() {
            if (debounceTimer) clearTimeout(debounceTimer);
            debounceTimer = setTimeout(enhanceAll, 200);
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    }

    // Public API
    window.A11y = {
        // Core methods
        enhanceAll: enhanceAll,
        enhanceFormFields: enhanceFormFields,
        verifyLandmarks: verifyLandmarks,
        enhanceRequiredFields: enhanceRequiredFields,
        enhanceValidationMessages: enhanceValidationMessages,

        // Utilities
        setAriaBusy: setAriaBusy,
        withAriaBusy: withAriaBusy,
        announce: announce,

        // Re-initialize (call after major DOM changes)
        refresh: enhanceAll
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }

})();
