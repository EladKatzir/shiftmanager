/**
 * Enhanced Form Validation (B-008)
 *
 * Provides:
 * - Accessible error handling with aria-live announcements
 * - Real-time field highlighting on error
 * - Integration with ASP.NET unobtrusive validation
 * - Localized error messages via the localization API
 * - Focus management for error states
 */

(function() {
    'use strict';

    // Configuration
    const CONFIG = {
        // Delay before clearing error state while typing (ms)
        clearDelay: 300,
        // Animation duration for error highlight (ms)
        highlightDuration: 500,
        // Selectors
        selectors: {
            form: 'form',
            input: 'input, select, textarea',
            validationMessage: '.field-validation-error, .field-validation-valid',
            validationSummary: '.validation-summary-errors, .validation-summary-valid',
            errorClass: 'input-validation-error',
            validClass: 'input-validation-valid'
        }
    };

    // Localization messages (fallbacks when server-side localization not available)
    const MESSAGES = {
        'en-US': {
            required: 'This field is required',
            email: 'Please enter a valid email address',
            minLength: 'Must be at least {0} characters',
            maxLength: 'Must be no more than {0} characters',
            range: 'Must be between {0} and {1}',
            date: 'Please enter a valid date',
            phone: 'Please enter a valid phone number',
            mismatch: 'Values do not match',
            number: 'Please enter a valid number',
            password: 'Password must contain at least one uppercase letter, one lowercase letter, and one number'
        },
        'he-IL': {
            required: 'שדה חובה',
            email: 'נא להזין כתובת דוא"ל תקינה',
            minLength: 'חייב להכיל לפחות {0} תווים',
            maxLength: 'חייב להכיל לכל היותר {0} תווים',
            range: 'חייב להיות בין {0} ל-{1}',
            date: 'נא להזין תאריך תקין',
            phone: 'נא להזין מספר טלפון תקין',
            mismatch: 'הערכים אינם תואמים',
            number: 'נא להזין מספר תקין',
            password: 'הסיסמה חייבת להכיל לפחות אות גדולה אחת, אות קטנה אחת ומספר אחד'
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
    function getMessage(key, params) {
        const culture = getCurrentCulture();
        let message = MESSAGES[culture]?.[key] || MESSAGES['en-US'][key] || key;

        // Substitute parameters {0}, {1}, etc.
        if (params && Array.isArray(params)) {
            params.forEach((param, index) => {
                message = message.replace(new RegExp('\\{' + index + '\\}', 'g'), param);
            });
        }

        return message;
    }

    /**
     * Enhance a single form with accessible validation
     */
    function enhanceFormValidation(form) {
        if (form.dataset.validationEnhanced === 'true') {
            return; // Already enhanced
        }
        form.dataset.validationEnhanced = 'true';

        // Add aria-live to validation message spans
        const validationSpans = form.querySelectorAll(CONFIG.selectors.validationMessage);
        validationSpans.forEach(function(span) {
            if (!span.hasAttribute('aria-live')) {
                span.setAttribute('aria-live', 'polite');
            }
        });

        // Enhance validation summary
        const summary = form.querySelector(CONFIG.selectors.validationSummary);
        if (summary && !summary.hasAttribute('role')) {
            summary.setAttribute('role', 'alert');
            summary.setAttribute('aria-live', 'polite');
        }

        // Handle input events
        const inputs = form.querySelectorAll(CONFIG.selectors.input);
        inputs.forEach(function(input) {
            // Blur: Trigger validation
            input.addEventListener('blur', function() {
                validateField(form, input);
            });

            // Input: Clear error state while typing
            input.addEventListener('input', function() {
                clearErrorOnInput(form, input);
            });

            // Focus: Add focus ring
            input.addEventListener('focus', function() {
                handleFieldFocus(input);
            });
        });

        // Handle form submission
        form.addEventListener('submit', function(e) {
            handleFormSubmit(e, form);
        });

        // Set up mutation observer to handle dynamically added fields
        setupMutationObserver(form);
    }

    /**
     * Validate a single field
     */
    function validateField(form, input) {
        // Trigger jQuery validation if available (ASP.NET unobtrusive validation)
        if (window.$ && $.validator && $.validator.unobtrusive) {
            var validator = $(form).validate();
            if (validator) {
                $(input).valid();
            }
        }

        // Update ARIA states
        updateAriaStates(input);
    }

    /**
     * Clear error state while user is typing
     */
    function clearErrorOnInput(form, input) {
        const inputName = input.name;
        if (!inputName) return;

        // Find associated error message
        const errorSpan = form.querySelector('[data-valmsg-for="' + inputName + '"]');

        // If error message is now empty, remove error class
        if (errorSpan) {
            const errorText = errorSpan.textContent.trim();
            if (!errorText && input.classList.contains(CONFIG.selectors.errorClass)) {
                // Delay slightly to prevent flicker
                setTimeout(function() {
                    const currentError = errorSpan.textContent.trim();
                    if (!currentError) {
                        input.classList.remove(CONFIG.selectors.errorClass);
                        input.removeAttribute('aria-invalid');
                    }
                }, CONFIG.clearDelay);
            }
        }
    }

    /**
     * Handle field focus
     */
    function handleFieldFocus(input) {
        // If field has error, ensure error message is visible
        if (input.classList.contains(CONFIG.selectors.errorClass)) {
            const form = input.closest('form');
            const inputName = input.name;
            if (form && inputName) {
                const errorSpan = form.querySelector('[data-valmsg-for="' + inputName + '"]');
                if (errorSpan && errorSpan.textContent.trim()) {
                    // Ensure error message is announced
                    errorSpan.setAttribute('aria-live', 'assertive');
                    setTimeout(function() {
                        errorSpan.setAttribute('aria-live', 'polite');
                    }, 100);
                }
            }
        }
    }

    /**
     * Update ARIA states based on validation status
     */
    function updateAriaStates(input) {
        if (input.classList.contains(CONFIG.selectors.errorClass)) {
            input.setAttribute('aria-invalid', 'true');

            // Find and link error message
            const form = input.closest('form');
            const inputName = input.name;
            if (form && inputName) {
                const errorSpan = form.querySelector('[data-valmsg-for="' + inputName + '"]');
                if (errorSpan && errorSpan.id) {
                    input.setAttribute('aria-describedby', errorSpan.id);
                } else if (errorSpan) {
                    // Generate an ID for the error span
                    const errorId = 'error-' + inputName.replace(/[^a-zA-Z0-9]/g, '-');
                    errorSpan.id = errorId;
                    input.setAttribute('aria-describedby', errorId);
                }
            }
        } else {
            input.removeAttribute('aria-invalid');
            // Don't remove aria-describedby as it might reference help text
        }
    }

    /**
     * Handle form submission
     */
    function handleFormSubmit(e, form) {
        // Let standard validation run first
        if (window.$ && $.validator) {
            var validator = $(form).validate();
            if (validator && !validator.form()) {
                e.preventDefault();

                // Focus first error field
                focusFirstError(form);

                // Announce errors to screen readers
                announceErrors(form);

                return false;
            }
        }
    }

    /**
     * Focus the first field with an error
     */
    function focusFirstError(form) {
        const firstErrorField = form.querySelector('.' + CONFIG.selectors.errorClass);
        if (firstErrorField) {
            firstErrorField.focus();

            // Add highlight animation
            firstErrorField.classList.add('input--error-highlight');
            setTimeout(function() {
                firstErrorField.classList.remove('input--error-highlight');
            }, CONFIG.highlightDuration * 2);
        }
    }

    /**
     * Announce errors to screen readers
     */
    function announceErrors(form) {
        const summary = form.querySelector('.validation-summary-errors');
        if (summary) {
            // Force re-announcement by temporarily removing and re-adding aria-live
            summary.setAttribute('aria-live', 'off');
            setTimeout(function() {
                summary.setAttribute('aria-live', 'assertive');
            }, 50);
        }
    }

    /**
     * Set up mutation observer to handle dynamically added fields
     */
    function setupMutationObserver(form) {
        const observer = new MutationObserver(function(mutations) {
            mutations.forEach(function(mutation) {
                mutation.addedNodes.forEach(function(node) {
                    if (node.nodeType === Node.ELEMENT_NODE) {
                        // Check if node is an input
                        if (node.matches && node.matches(CONFIG.selectors.input)) {
                            enhanceInput(form, node);
                        }

                        // Check children for inputs
                        const childInputs = node.querySelectorAll ?
                            node.querySelectorAll(CONFIG.selectors.input) : [];
                        childInputs.forEach(function(input) {
                            enhanceInput(form, input);
                        });

                        // Check for validation messages
                        const childMessages = node.querySelectorAll ?
                            node.querySelectorAll(CONFIG.selectors.validationMessage) : [];
                        childMessages.forEach(function(span) {
                            if (!span.hasAttribute('aria-live')) {
                                span.setAttribute('aria-live', 'polite');
                            }
                        });
                    }
                });
            });
        });

        observer.observe(form, {
            childList: true,
            subtree: true
        });
    }

    /**
     * Enhance a single input with validation handlers
     */
    function enhanceInput(form, input) {
        if (input.dataset.validationEnhanced === 'true') {
            return;
        }
        input.dataset.validationEnhanced = 'true';

        input.addEventListener('blur', function() {
            validateField(form, input);
        });

        input.addEventListener('input', function() {
            clearErrorOnInput(form, input);
        });

        input.addEventListener('focus', function() {
            handleFieldFocus(input);
        });
    }

    /**
     * Manually mark a field as invalid (for custom validation)
     */
    function setFieldError(input, message) {
        input.classList.add(CONFIG.selectors.errorClass);
        input.classList.remove(CONFIG.selectors.validClass);
        input.setAttribute('aria-invalid', 'true');

        // Find or create error message span
        const form = input.closest('form');
        const inputName = input.name || input.id;
        if (form && inputName) {
            let errorSpan = form.querySelector('[data-valmsg-for="' + inputName + '"]');

            if (!errorSpan) {
                // Create error span
                errorSpan = document.createElement('span');
                errorSpan.className = 'field-validation-error';
                errorSpan.setAttribute('data-valmsg-for', inputName);
                errorSpan.setAttribute('data-valmsg-replace', 'true');
                errorSpan.setAttribute('aria-live', 'polite');

                // Insert after input
                input.parentNode.insertBefore(errorSpan, input.nextSibling);
            }

            errorSpan.textContent = message;
            errorSpan.classList.remove('field-validation-valid');
            errorSpan.classList.add('field-validation-error');

            // Generate ID and link
            const errorId = 'error-' + inputName.replace(/[^a-zA-Z0-9]/g, '-');
            errorSpan.id = errorId;
            input.setAttribute('aria-describedby', errorId);
        }
    }

    /**
     * Clear field error (for custom validation)
     */
    function clearFieldError(input) {
        input.classList.remove(CONFIG.selectors.errorClass);
        input.removeAttribute('aria-invalid');

        const form = input.closest('form');
        const inputName = input.name || input.id;
        if (form && inputName) {
            const errorSpan = form.querySelector('[data-valmsg-for="' + inputName + '"]');
            if (errorSpan) {
                errorSpan.textContent = '';
                errorSpan.classList.remove('field-validation-error');
                errorSpan.classList.add('field-validation-valid');
            }
        }
    }

    /**
     * Initialize all forms on the page
     */
    function initialize() {
        console.log('[FormValidation] Initializing enhanced form validation (B-008)');

        // Enhance all existing forms
        document.querySelectorAll(CONFIG.selectors.form).forEach(function(form) {
            enhanceFormValidation(form);
        });

        // Watch for new forms added to the page
        const bodyObserver = new MutationObserver(function(mutations) {
            mutations.forEach(function(mutation) {
                mutation.addedNodes.forEach(function(node) {
                    if (node.nodeType === Node.ELEMENT_NODE) {
                        if (node.matches && node.matches(CONFIG.selectors.form)) {
                            enhanceFormValidation(node);
                        }
                        const childForms = node.querySelectorAll ?
                            node.querySelectorAll(CONFIG.selectors.form) : [];
                        childForms.forEach(function(form) {
                            enhanceFormValidation(form);
                        });
                    }
                });
            });
        });

        bodyObserver.observe(document.body, {
            childList: true,
            subtree: true
        });
    }

    // Public API
    window.FormValidation = {
        enhance: enhanceFormValidation,
        validateField: validateField,
        setFieldError: setFieldError,
        clearFieldError: clearFieldError,
        focusFirstError: focusFirstError,
        getMessage: getMessage,
        config: CONFIG
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
