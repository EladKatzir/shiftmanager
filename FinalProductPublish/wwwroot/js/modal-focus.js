/**
 * Modal Focus Management Utility (B-013)
 * Provides focus trapping and keyboard handling for accessible modals.
 *
 * Requirements:
 * 1. Focus moves to first focusable element when modal opens
 * 2. Tab cycles within modal only (focus trap)
 * 3. Focus returns to trigger element on close
 * 4. ESC key closes modal
 * 5. Backdrop click closes modal
 */
(function() {
    'use strict';

    var FOCUSABLE_SELECTORS = [
        'button:not([disabled])',
        'input:not([disabled])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        'a[href]',
        '[tabindex]:not([tabindex="-1"])'
    ].join(', ');

    var activeModal = null;
    var previousActiveElement = null;
    var keydownHandler = null;
    var clickHandler = null;

    /**
     * Gets all focusable elements within a container
     * @param {HTMLElement} container - The container element
     * @returns {HTMLElement[]} Array of focusable elements
     */
    function getFocusableElements(container) {
        var elements = container.querySelectorAll(FOCUSABLE_SELECTORS);
        return Array.prototype.filter.call(elements, function(el) {
            // Filter out hidden elements
            return el.offsetParent !== null &&
                   window.getComputedStyle(el).visibility !== 'hidden';
        });
    }

    /**
     * Opens a modal with proper focus management
     * @param {HTMLElement|string} modal - The modal element or its ID
     * @param {Object} options - Configuration options
     * @param {Function} options.onClose - Callback when modal is closed
     * @param {boolean} options.closeOnBackdrop - Whether backdrop click closes modal (default: true)
     * @param {boolean} options.closeOnEscape - Whether ESC key closes modal (default: true)
     */
    function openModal(modal, options) {
        options = options || {};

        // Allow passing modal by ID
        if (typeof modal === 'string') {
            modal = document.getElementById(modal);
        }

        if (!modal) {
            console.warn('[ModalFocus] Modal element not found');
            return;
        }

        // Close any currently open modal first
        if (activeModal && activeModal !== modal) {
            closeModal();
        }

        // Store the element that triggered the modal
        previousActiveElement = document.activeElement;
        activeModal = modal;

        // Store options on the modal for later use
        modal._modalFocusOptions = {
            onClose: options.onClose || null,
            closeOnBackdrop: options.closeOnBackdrop !== false,
            closeOnEscape: options.closeOnEscape !== false
        };

        // Make modal visible
        modal.setAttribute('aria-hidden', 'false');
        modal.setAttribute('aria-modal', 'true');
        modal.setAttribute('role', 'dialog');

        // Add open classes (support both naming conventions)
        modal.classList.add('modal--open');
        modal.classList.add('is-open');

        // Prevent body scroll
        document.body.style.overflow = 'hidden';

        // Focus first focusable element after a brief delay for transitions
        setTimeout(function() {
            var focusableElements = getFocusableElements(modal);
            if (focusableElements.length > 0) {
                // Prefer elements with autofocus attribute
                var autofocus = modal.querySelector('[autofocus]');
                if (autofocus && autofocus.offsetParent !== null) {
                    autofocus.focus();
                } else {
                    focusableElements[0].focus();
                }
            } else {
                // If no focusable elements, focus the modal itself
                modal.setAttribute('tabindex', '-1');
                modal.focus();
            }
        }, 50);

        // Create event handlers
        keydownHandler = function(event) {
            handleKeyDown(event, modal);
        };

        clickHandler = function(event) {
            handleBackdropClick(event, modal);
        };

        // Add event listeners
        document.addEventListener('keydown', keydownHandler);
        modal.addEventListener('click', clickHandler);

        // Dispatch custom event
        modal.dispatchEvent(new CustomEvent('modal:open', {
            bubbles: true,
            detail: { modal: modal }
        }));
    }

    /**
     * Closes the currently active modal
     * @param {HTMLElement|string} modal - Optional specific modal to close
     */
    function closeModal(modal) {
        // Allow passing modal by ID
        if (typeof modal === 'string') {
            modal = document.getElementById(modal);
        }

        // If no modal specified, close the active one
        if (!modal) {
            modal = activeModal;
        }

        if (!modal) {
            return;
        }

        var options = modal._modalFocusOptions || {};

        // Remove event listeners
        if (keydownHandler) {
            document.removeEventListener('keydown', keydownHandler);
            keydownHandler = null;
        }
        if (clickHandler) {
            modal.removeEventListener('click', clickHandler);
            clickHandler = null;
        }

        // Hide modal
        modal.setAttribute('aria-hidden', 'true');
        modal.removeAttribute('aria-modal');
        modal.classList.remove('modal--open');
        modal.classList.remove('is-open');

        // Restore body scroll
        document.body.style.overflow = '';

        // Return focus to trigger element
        if (previousActiveElement && typeof previousActiveElement.focus === 'function') {
            // Small delay to ensure modal animation completes
            setTimeout(function() {
                try {
                    previousActiveElement.focus();
                } catch (e) {
                    // Element may have been removed from DOM
                    console.warn('[ModalFocus] Could not return focus to trigger element');
                }
            }, 50);
        }

        // Call onClose callback if provided
        if (typeof options.onClose === 'function') {
            options.onClose();
        }

        // Dispatch custom event
        modal.dispatchEvent(new CustomEvent('modal:close', {
            bubbles: true,
            detail: { modal: modal }
        }));

        // Clean up
        if (modal === activeModal) {
            activeModal = null;
            previousActiveElement = null;
        }
        delete modal._modalFocusOptions;
    }

    /**
     * Handles keyboard events within the modal
     * @param {KeyboardEvent} event - The keyboard event
     * @param {HTMLElement} modal - The modal element
     */
    function handleKeyDown(event, modal) {
        if (!modal) return;

        var options = modal._modalFocusOptions || {};

        // ESC closes modal
        if (event.key === 'Escape' && options.closeOnEscape !== false) {
            event.preventDefault();
            event.stopPropagation();
            closeModal(modal);
            return;
        }

        // Tab trapping
        if (event.key === 'Tab') {
            var focusableElements = getFocusableElements(modal);

            if (focusableElements.length === 0) {
                event.preventDefault();
                return;
            }

            var firstElement = focusableElements[0];
            var lastElement = focusableElements[focusableElements.length - 1];

            if (event.shiftKey) {
                // Shift+Tab: if on first element, go to last
                if (document.activeElement === firstElement) {
                    event.preventDefault();
                    lastElement.focus();
                }
            } else {
                // Tab: if on last element, go to first
                if (document.activeElement === lastElement) {
                    event.preventDefault();
                    firstElement.focus();
                }
            }
        }
    }

    /**
     * Handles clicks on the modal backdrop
     * @param {MouseEvent} event - The click event
     * @param {HTMLElement} modal - The modal element
     */
    function handleBackdropClick(event, modal) {
        var options = modal._modalFocusOptions || {};

        if (options.closeOnBackdrop === false) {
            return;
        }

        // Only close if clicking the modal backdrop itself, not modal content
        // Check for common backdrop class names
        var target = event.target;
        var isBackdrop = target === modal ||
                        target.classList.contains('modal') ||
                        target.classList.contains('modal__backdrop') ||
                        target.classList.contains('modal-backdrop') ||
                        target.classList.contains('shift-creation-modal');

        // Also check if click is on the modal overlay (not inside content)
        var modalContent = modal.querySelector('.modal-content, .modal__content');
        if (modalContent && !modalContent.contains(target) && modal.contains(target)) {
            isBackdrop = true;
        }

        if (isBackdrop) {
            closeModal(modal);
        }
    }

    /**
     * Checks if a modal is currently open
     * @param {HTMLElement|string} modal - Optional specific modal to check
     * @returns {boolean} Whether the modal (or any modal) is open
     */
    function isOpen(modal) {
        if (modal) {
            if (typeof modal === 'string') {
                modal = document.getElementById(modal);
            }
            return modal && (modal.classList.contains('modal--open') || modal.classList.contains('is-open'));
        }
        return activeModal !== null;
    }

    /**
     * Gets the currently active modal
     * @returns {HTMLElement|null} The active modal or null
     */
    function getActiveModal() {
        return activeModal;
    }

    // Expose functions globally
    window.ModalFocus = {
        open: openModal,
        close: closeModal,
        isOpen: isOpen,
        getActive: getActiveModal
    };

    // Also expose as standalone functions for convenience
    window.openModalWithFocus = openModal;
    window.closeModalWithFocus = closeModal;

})();
