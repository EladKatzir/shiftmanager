/**
 * Mobile Navigation (B-011)
 * Handles sidebar open/close on mobile devices with:
 * - Hamburger to X animation
 * - Tap outside to close
 * - Smooth animations
 * - Focus trap when open
 * - Keyboard navigation (Escape to close)
 */
(function() {
    'use strict';

    let sidebar = null;
    let toggle = null;
    let overlay = null;
    let isOpen = false;
    let previousActiveElement = null;
    let focusTrapHandler = null;

    // Selector for all focusable elements
    const FOCUSABLE_SELECTOR = [
        'button:not([disabled])',
        '[href]',
        'input:not([disabled])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ].join(', ');

    /**
     * Initialize mobile navigation
     */
    function init() {
        sidebar = document.getElementById('appSidebar');
        toggle = document.getElementById('mobileNavToggle');
        overlay = document.getElementById('mobileNavOverlay');

        if (!sidebar || !toggle) {
            return;
        }

        // Ensure overlay exists
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'mobileNavOverlay';
            overlay.className = 'mobile-nav-overlay';
            overlay.setAttribute('aria-hidden', 'true');
            document.body.appendChild(overlay);
        }

        // Update toggle button structure for hamburger animation
        updateToggleIcon();

        // Event listeners
        toggle.addEventListener('click', toggleNav);
        overlay.addEventListener('click', closeNav);

        // Keyboard handling
        document.addEventListener('keydown', handleKeyDown);

        // Close on resize to desktop (above 768px)
        let resizeTimeout;
        window.addEventListener('resize', function() {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(function() {
                if (window.innerWidth > 768 && isOpen) {
                    closeNav();
                }
            }, 100);
        });

    }

    /**
     * Update toggle button to have hamburger icon structure
     */
    function updateToggleIcon() {
        // Check if already has the new structure
        if (toggle.querySelector('.mobile-nav-toggle__icon')) {
            return;
        }

        // Clear existing content
        toggle.innerHTML = '';

        // Create hamburger icon structure
        var icon = document.createElement('span');
        icon.className = 'mobile-nav-toggle__icon';
        icon.innerHTML = '<span></span><span></span><span></span>';
        toggle.appendChild(icon);
    }

    /**
     * Toggle navigation state
     */
    function toggleNav() {
        if (isOpen) {
            closeNav();
        } else {
            openNav();
        }
    }

    /**
     * Open navigation sidebar
     */
    function openNav() {
        if (isOpen) return;

        // Store current focus for restoration
        previousActiveElement = document.activeElement;
        isOpen = true;

        // Update UI states
        sidebar.classList.add('is-mobile-open');
        toggle.classList.add('is-open');
        toggle.setAttribute('aria-expanded', 'true');
        overlay.classList.add('is-visible');
        overlay.setAttribute('aria-hidden', 'false');

        // Prevent body scroll
        document.body.style.overflow = 'hidden';

        // Set up focus trap
        setupFocusTrap();

        // Focus first focusable element in sidebar after animation
        setTimeout(function() {
            var firstFocusable = sidebar.querySelector(FOCUSABLE_SELECTOR);
            if (firstFocusable) {
                firstFocusable.focus();
            }
        }, 50);

    }

    /**
     * Close navigation sidebar
     */
    function closeNav() {
        if (!isOpen) return;

        isOpen = false;

        // Update UI states
        sidebar.classList.remove('is-mobile-open');
        toggle.classList.remove('is-open');
        toggle.setAttribute('aria-expanded', 'false');
        overlay.classList.remove('is-visible');
        overlay.setAttribute('aria-hidden', 'true');

        // Restore body scroll
        document.body.style.overflow = '';

        // Remove focus trap
        removeFocusTrap();

        // Return focus to toggle button or previous element
        if (previousActiveElement && typeof previousActiveElement.focus === 'function') {
            previousActiveElement.focus();
        } else {
            toggle.focus();
        }

    }

    /**
     * Handle keyboard events
     */
    function handleKeyDown(e) {
        if (!isOpen) return;

        // Close on Escape
        if (e.key === 'Escape') {
            e.preventDefault();
            closeNav();
            return;
        }
    }

    /**
     * Set up focus trap within sidebar
     */
    function setupFocusTrap() {
        focusTrapHandler = function(e) {
            if (e.key !== 'Tab' || !isOpen) return;

            var focusableElements = Array.from(sidebar.querySelectorAll(FOCUSABLE_SELECTOR));
            if (focusableElements.length === 0) return;

            var firstElement = focusableElements[0];
            var lastElement = focusableElements[focusableElements.length - 1];

            // Shift+Tab from first element -> go to last
            if (e.shiftKey && document.activeElement === firstElement) {
                e.preventDefault();
                lastElement.focus();
            }
            // Tab from last element -> go to first
            else if (!e.shiftKey && document.activeElement === lastElement) {
                e.preventDefault();
                firstElement.focus();
            }
            // If focus is outside sidebar, bring it back
            else if (!sidebar.contains(document.activeElement)) {
                e.preventDefault();
                firstElement.focus();
            }
        };

        document.addEventListener('keydown', focusTrapHandler);
    }

    /**
     * Remove focus trap
     */
    function removeFocusTrap() {
        if (focusTrapHandler) {
            document.removeEventListener('keydown', focusTrapHandler);
            focusTrapHandler = null;
        }
    }

    // Initialize on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Expose public API for programmatic control
    window.MobileNav = {
        open: openNav,
        close: closeNav,
        toggle: toggleNav,
        isOpen: function() { return isOpen; }
    };
})();
