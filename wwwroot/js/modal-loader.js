/**
 * Modal Lazy Loader (B-030)
 * Loads modal content on demand to reduce initial page weight
 *
 * Usage:
 * <button data-modal-lazy="editModal" data-modal-url="/modals/edit">Edit</button>
 *
 * Features:
 * - Fetches modal content via AJAX on first trigger
 * - Caches loaded modals for subsequent opens
 * - Shows loading state during fetch
 * - Integrates with ModalFocus for accessibility
 * - Supports CSRF tokens for POST-triggered modals
 */
(function() {
    'use strict';

    // Cache for loaded modal content
    const modalCache = new Map();

    // Loading state tracking
    const loadingModals = new Set();

    /**
     * Initialize modal lazy loader
     */
    function init() {
        // Use event delegation for lazy modal triggers
        document.addEventListener('click', handleTriggerClick);

    }

    /**
     * Handle click on lazy modal triggers
     * @param {Event} e - Click event
     */
    function handleTriggerClick(e) {
        const trigger = e.target.closest('[data-modal-lazy]');
        if (!trigger) return;

        e.preventDefault();
        e.stopPropagation();

        const modalId = trigger.dataset.modalLazy;
        const modalUrl = trigger.dataset.modalUrl;

        if (!modalId || !modalUrl) {
            console.error('[ModalLoader] Missing data-modal-lazy or data-modal-url');
            return;
        }

        // Check if already loading
        if (loadingModals.has(modalId)) {
            console.log('[ModalLoader] Modal already loading:', modalId);
            return;
        }

        // Check cache first
        if (modalCache.has(modalId)) {
            openModal(modalId, modalCache.get(modalId));
            return;
        }

        // Fetch modal content
        loadModal(trigger, modalId, modalUrl);
    }

    /**
     * Load modal content from server
     * @param {HTMLElement} trigger - The trigger element
     * @param {string} modalId - Modal identifier
     * @param {string} modalUrl - URL to fetch modal content
     */
    async function loadModal(trigger, modalId, modalUrl) {
        // Show loading state
        trigger.classList.add('is-loading');
        trigger.setAttribute('aria-busy', 'true');
        loadingModals.add(modalId);

        // Store original content for restoration
        const originalContent = trigger.innerHTML;
        const loadingHtml = trigger.dataset.loadingText || '<span class="modal-loading-spinner"></span>';
        trigger.innerHTML = loadingHtml;

        try {
            // Build fetch options
            const fetchOptions = {
                method: 'GET',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'text/html'
                },
                credentials: 'same-origin'
            };

            // Add CSRF token if available
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (csrfToken) {
                fetchOptions.headers['RequestVerificationToken'] = csrfToken;
            }

            const response = await fetch(modalUrl, fetchOptions);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const html = await response.text();

            // Cache the content
            modalCache.set(modalId, html);

            // Open the modal
            openModal(modalId, html);

        } catch (error) {
            console.error('[ModalLoader] Failed to load modal:', error);

            // Show error notification
            if (window.ErrorStates) {
                ErrorStates.showError(
                    window.AppLocalizer?.FailedToLoadContent || 'Failed to load content',
                    window.AppLocalizer?.LoadingFailedTitle || 'Loading Failed'
                );
            } else {
                alert(window.AppLocalizer?.FailedToLoadContent || 'Failed to load content. Please try again.');
            }
        } finally {
            // Reset trigger state
            trigger.classList.remove('is-loading');
            trigger.removeAttribute('aria-busy');
            trigger.innerHTML = originalContent;
            loadingModals.delete(modalId);
        }
    }

    /**
     * Open modal with content
     * @param {string} modalId - Modal identifier
     * @param {string} html - Modal HTML content
     */
    function openModal(modalId, html) {
        // Find or create modal container
        let modal = document.getElementById(modalId);

        if (!modal) {
            modal = document.createElement('div');
            modal.id = modalId;
            modal.className = 'modal lazy-modal';
            modal.setAttribute('role', 'dialog');
            modal.setAttribute('aria-modal', 'true');
            document.body.appendChild(modal);
        }

        // Set content
        modal.innerHTML = html;

        // Initialize any scripts in the modal content
        executeModalScripts(modal);

        // Open modal with accessibility support
        if (window.ModalFocus) {
            ModalFocus.open(modal);
        } else {
            modal.classList.add('modal--open');
            modal.style.display = 'flex';
            document.body.style.overflow = 'hidden';

            // Basic focus management
            const firstFocusable = modal.querySelector('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
            if (firstFocusable) {
                firstFocusable.focus();
            }
        }

        // Set up close handlers
        setupCloseHandlers(modal, modalId);

        // Dispatch custom event
        modal.dispatchEvent(new CustomEvent('modal:opened', {
            detail: { modalId },
            bubbles: true
        }));

        console.log('[ModalLoader] Opened modal:', modalId);
    }

    /**
     * Execute scripts found in modal content
     * @param {HTMLElement} modal - Modal element
     */
    function executeModalScripts(modal) {
        const scripts = modal.querySelectorAll('script');
        scripts.forEach(oldScript => {
            const newScript = document.createElement('script');

            // Copy attributes
            Array.from(oldScript.attributes).forEach(attr => {
                newScript.setAttribute(attr.name, attr.value);
            });

            // Copy content
            newScript.textContent = oldScript.textContent;

            // Replace old script with new one to execute it
            oldScript.parentNode.replaceChild(newScript, oldScript);
        });

        // Re-initialize Lucide icons if present
        if (typeof lucide !== 'undefined' && lucide.createIcons) {
            lucide.createIcons({ scope: modal });
        }
    }

    /**
     * Set up close handlers for modal
     * @param {HTMLElement} modal - Modal element
     * @param {string} modalId - Modal identifier
     */
    function setupCloseHandlers(modal, modalId) {
        // Close button handler
        const closeButtons = modal.querySelectorAll('[data-modal-close], .modal-close, .modal__close');
        closeButtons.forEach(btn => {
            btn.addEventListener('click', () => closeModal(modalId));
        });

        // Backdrop click handler
        modal.addEventListener('click', (e) => {
            if (e.target === modal || e.target.classList.contains('modal-backdrop')) {
                closeModal(modalId);
            }
        });

        // Escape key handler — remove previous listener before adding new one to prevent leak
        if (modal._escapeHandler) {
            document.removeEventListener('keydown', modal._escapeHandler);
        }
        const escapeHandler = (e) => {
            if (e.key === 'Escape' && modal.classList.contains('modal--open')) {
                closeModal(modalId);
            }
        };
        modal._escapeHandler = escapeHandler;
        document.addEventListener('keydown', escapeHandler);
    }

    /**
     * Close a modal
     * @param {string} modalId - Modal identifier
     */
    function closeModal(modalId) {
        const modal = document.getElementById(modalId);
        if (!modal) return;

        // Clean up escape handler to prevent listener leak
        if (modal._escapeHandler) {
            document.removeEventListener('keydown', modal._escapeHandler);
            modal._escapeHandler = null;
        }

        if (window.ModalFocus) {
            ModalFocus.close(modal);
        } else {
            modal.classList.remove('modal--open');
            modal.style.display = 'none';
            document.body.style.overflow = '';
        }

        // Dispatch custom event
        modal.dispatchEvent(new CustomEvent('modal:closed', {
            detail: { modalId },
            bubbles: true
        }));

        console.log('[ModalLoader] Closed modal:', modalId);
    }

    /**
     * Preload a modal (fetch but don't open)
     * @param {string} modalId - Modal identifier
     * @param {string} modalUrl - URL to fetch modal content
     * @returns {Promise}
     */
    async function preloadModal(modalId, modalUrl) {
        if (modalCache.has(modalId)) {
            return Promise.resolve();
        }

        try {
            const response = await fetch(modalUrl, {
                method: 'GET',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'text/html'
                },
                credentials: 'same-origin'
            });

            if (response.ok) {
                const html = await response.text();
                modalCache.set(modalId, html);
                console.log('[ModalLoader] Preloaded modal:', modalId);
            }
        } catch (error) {
            console.warn('[ModalLoader] Failed to preload modal:', modalId, error);
        }
    }

    /**
     * Clear modal cache
     * @param {string} modalId - Optional specific modal to clear
     */
    function clearCache(modalId) {
        if (modalId) {
            modalCache.delete(modalId);
        } else {
            modalCache.clear();
        }
    }

    /**
     * Check if modal is cached
     * @param {string} modalId - Modal identifier
     * @returns {boolean}
     */
    function isCached(modalId) {
        return modalCache.has(modalId);
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Expose public API
    window.ModalLoader = {
        open: openModal,
        close: closeModal,
        preload: preloadModal,
        clearCache: clearCache,
        isCached: isCached
    };
})();
