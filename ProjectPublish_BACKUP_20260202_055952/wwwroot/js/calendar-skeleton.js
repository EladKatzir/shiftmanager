/**
 * Calendar Skeleton Loading Handler (B-007)
 *
 * Manages skeleton loading states for calendar components with:
 * - Smooth transitions from skeleton to content
 * - Respect for reduced-motion preference
 * - Support for both server-rendered and AJAX-loaded content
 */
(function() {
    'use strict';

    /**
     * Configuration
     */
    const CONFIG = {
        // Minimum time to show skeleton (prevents flash for fast loads)
        minSkeletonTime: 200,
        // Maximum time before forcing content display (prevents infinite loading)
        maxSkeletonTime: 5000,
        // Transition duration in ms
        transitionDuration: 200,
        // CSS classes
        classes: {
            skeleton: 'calendar-skeleton',
            content: 'calendar-content',
            shimmer: 'calendar-skeleton-shimmer',
            hidden: 'is-hidden',
            loaded: 'is-loaded'
        },
        // Selectors
        selectors: {
            skeleton: '[data-calendar-skeleton]',
            content: '[data-calendar-content]'
        }
    };

    /**
     * Check if user prefers reduced motion
     */
    function prefersReducedMotion() {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    /**
     * Get effective transition duration
     */
    function getTransitionDuration() {
        return prefersReducedMotion() ? 0 : CONFIG.transitionDuration;
    }

    /**
     * Hide skeleton and show content
     * @param {HTMLElement} skeletonEl - The skeleton element
     * @param {HTMLElement} contentEl - The content element
     */
    function transitionToContent(skeletonEl, contentEl) {
        if (!skeletonEl || !contentEl) return;

        const duration = getTransitionDuration();

        // Update aria attributes
        skeletonEl.setAttribute('aria-hidden', 'true');
        contentEl.setAttribute('aria-hidden', 'false');

        if (duration === 0) {
            // Instant transition for reduced motion
            skeletonEl.classList.add(CONFIG.classes.hidden);
            contentEl.classList.add(CONFIG.classes.loaded);
        } else {
            // Smooth transition
            // First fade out skeleton
            skeletonEl.style.transition = `opacity ${duration}ms ease-out`;
            skeletonEl.classList.add(CONFIG.classes.hidden);

            // Then fade in content
            setTimeout(function() {
                contentEl.classList.add(CONFIG.classes.loaded);
            }, duration / 2);
        }

        // Clean up after transition
        setTimeout(function() {
            // Remove skeleton from DOM to free up resources
            // (optional - can be kept for re-use)
            // skeletonEl.remove();
        }, duration);
    }

    /**
     * Show skeleton and hide content (for refresh/reload scenarios)
     * @param {HTMLElement} skeletonEl - The skeleton element
     * @param {HTMLElement} contentEl - The content element
     */
    function transitionToSkeleton(skeletonEl, contentEl) {
        if (!skeletonEl || !contentEl) return;

        const duration = getTransitionDuration();

        // Update aria attributes
        skeletonEl.setAttribute('aria-hidden', 'false');
        contentEl.setAttribute('aria-hidden', 'true');

        if (duration === 0) {
            // Instant transition
            skeletonEl.classList.remove(CONFIG.classes.hidden);
            contentEl.classList.remove(CONFIG.classes.loaded);
        } else {
            // First fade out content
            contentEl.classList.remove(CONFIG.classes.loaded);

            // Then fade in skeleton
            setTimeout(function() {
                skeletonEl.classList.remove(CONFIG.classes.hidden);
            }, duration / 2);
        }
    }

    /**
     * Initialize skeleton loading for a calendar component
     * @param {HTMLElement} container - Container element with both skeleton and content
     */
    function initializeCalendarSkeleton(container) {
        const skeletonEl = container.querySelector(CONFIG.selectors.skeleton);
        const contentEl = container.querySelector(CONFIG.selectors.content);

        if (!skeletonEl || !contentEl) {
            // If no skeleton, just show content
            if (contentEl) {
                contentEl.classList.add(CONFIG.classes.loaded);
            }
            return;
        }

        // Track when skeleton was first shown
        const skeletonStartTime = Date.now();

        // Function to handle the transition
        function handleContentReady() {
            const elapsed = Date.now() - skeletonStartTime;
            const remainingMinTime = Math.max(0, CONFIG.minSkeletonTime - elapsed);

            // Wait for minimum skeleton time before transitioning
            setTimeout(function() {
                transitionToContent(skeletonEl, contentEl);
            }, remainingMinTime);
        }

        // If content is already ready (server-rendered), transition immediately
        // Check for a data attribute or class that indicates content is ready
        if (contentEl.dataset.ready === 'true' || contentEl.classList.contains('is-ready')) {
            handleContentReady();
        } else {
            // Set up event listener for when content becomes ready
            contentEl.addEventListener('calendar:content-ready', handleContentReady, { once: true });

            // Also set a maximum wait time
            setTimeout(function() {
                // Force transition if content hasn't signaled ready
                if (!contentEl.classList.contains(CONFIG.classes.loaded)) {
                    console.warn('Calendar content took too long to load, forcing display');
                    transitionToContent(skeletonEl, contentEl);
                }
            }, CONFIG.maxSkeletonTime);
        }
    }

    /**
     * Signal that calendar content is ready
     * Call this from your calendar page after data is loaded
     * @param {HTMLElement} contentEl - The content element
     */
    function signalContentReady(contentEl) {
        if (!contentEl) return;

        contentEl.dataset.ready = 'true';
        contentEl.dispatchEvent(new CustomEvent('calendar:content-ready', {
            bubbles: true
        }));
    }

    /**
     * Initialize all calendar skeletons on the page
     */
    function initializeAllSkeletons() {
        const containers = document.querySelectorAll('[data-calendar-skeleton-container]');
        containers.forEach(initializeCalendarSkeleton);

        // Also handle standalone skeleton/content pairs
        const standalonePairs = document.querySelectorAll('.calendar-skeleton-container');
        standalonePairs.forEach(initializeCalendarSkeleton);
    }

    /**
     * Auto-transition for server-rendered content
     * This handles the case where content is already present in the HTML
     */
    function handleServerRenderedContent() {
        // Wait for DOM to be fully parsed
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', function() {
                // Small delay to allow CSS to apply
                requestAnimationFrame(function() {
                    initializeAllSkeletons();
                });
            });
        } else {
            // DOM is already ready
            requestAnimationFrame(function() {
                initializeAllSkeletons();
            });
        }
    }

    /**
     * Create a skeleton element programmatically
     * @param {string} type - Type of skeleton ('month', 'week', 'day', 'table')
     * @returns {HTMLElement} The skeleton element
     */
    function createSkeleton(type) {
        const skeleton = document.createElement('div');
        skeleton.className = CONFIG.classes.skeleton;
        skeleton.setAttribute('data-calendar-skeleton', '');
        skeleton.setAttribute('aria-busy', 'true');
        // Use aria-label from the DOM if available (server-rendered skeleton has localized label)
        // For programmatic skeletons, use a minimal description - the visually hidden span provides the text
        skeleton.setAttribute('aria-label', '');

        switch (type) {
            case 'month':
                skeleton.innerHTML = createMonthSkeletonHTML();
                break;
            case 'week':
                skeleton.innerHTML = createWeekSkeletonHTML();
                break;
            case 'day':
                skeleton.innerHTML = createDaySkeletonHTML();
                break;
            case 'table':
                skeleton.innerHTML = createTableSkeletonHTML();
                break;
            default:
                skeleton.innerHTML = createGenericSkeletonHTML();
        }

        return skeleton;
    }

    // Skeleton HTML generators
    function createMonthSkeletonHTML() {
        let cellsHTML = '';
        for (let i = 0; i < 35; i++) {
            cellsHTML += `
                <div class="skeleton-calendar-cell">
                    <div class="skeleton-cell-date calendar-skeleton-shimmer"></div>
                    <div class="skeleton-cell-items">
                        <div class="skeleton-calendar-item calendar-skeleton-shimmer"></div>
                        ${i % 3 === 0 ? '<div class="skeleton-calendar-item skeleton-calendar-item--short calendar-skeleton-shimmer"></div>' : ''}
                    </div>
                </div>
            `;
        }

        return `
            <div class="skeleton-calendar-header">
                <div class="skeleton-header-title calendar-skeleton-shimmer"></div>
                <div class="skeleton-header-nav">
                    <div class="skeleton-nav-btn skeleton-nav-btn--icon calendar-skeleton-shimmer"></div>
                    <div class="skeleton-nav-btn calendar-skeleton-shimmer"></div>
                    <div class="skeleton-nav-btn skeleton-nav-btn--icon calendar-skeleton-shimmer"></div>
                </div>
            </div>
            <div class="skeleton-view-switcher">
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
            </div>
            <div class="skeleton-weekday-header">
                ${Array(7).fill('<div class="skeleton-weekday"><div class="skeleton-weekday-text calendar-skeleton-shimmer"></div></div>').join('')}
            </div>
            <div class="skeleton-calendar-grid">
                ${cellsHTML}
            </div>
        `;
    }

    function createWeekSkeletonHTML() {
        let columnsHTML = '';
        for (let i = 0; i < 7; i++) {
            columnsHTML += `
                <div class="skeleton-week-column">
                    <div class="skeleton-column-header">
                        <div class="skeleton-day-name calendar-skeleton-shimmer"></div>
                        <div class="skeleton-day-date calendar-skeleton-shimmer"></div>
                    </div>
                    <div class="skeleton-column-items">
                        <div class="skeleton-week-item calendar-skeleton-shimmer"></div>
                        ${i % 2 === 0 ? '<div class="skeleton-week-item calendar-skeleton-shimmer"></div>' : ''}
                    </div>
                </div>
            `;
        }

        return `
            <div class="skeleton-calendar-header">
                <div class="skeleton-header-title calendar-skeleton-shimmer"></div>
                <div class="skeleton-header-nav">
                    <div class="skeleton-nav-btn skeleton-nav-btn--icon calendar-skeleton-shimmer"></div>
                    <div class="skeleton-nav-btn calendar-skeleton-shimmer"></div>
                    <div class="skeleton-nav-btn skeleton-nav-btn--icon calendar-skeleton-shimmer"></div>
                </div>
            </div>
            <div class="skeleton-view-switcher">
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
            </div>
            <div class="skeleton-week-grid">
                ${columnsHTML}
            </div>
        `;
    }

    function createDaySkeletonHTML() {
        let itemsHTML = '';
        for (let i = 0; i < 4; i++) {
            itemsHTML += `
                <div class="skeleton-day-item">
                    <div class="skeleton-day-item-main">
                        <div class="skeleton-day-item-header">
                            <div class="skeleton-item-icon calendar-skeleton-shimmer"></div>
                            <div class="skeleton-item-title calendar-skeleton-shimmer"></div>
                        </div>
                        <div class="skeleton-day-item-details">
                            <div class="skeleton-detail-row">
                                <div class="skeleton-detail-label calendar-skeleton-shimmer"></div>
                                <div class="skeleton-detail-value calendar-skeleton-shimmer"></div>
                            </div>
                            <div class="skeleton-detail-row">
                                <div class="skeleton-detail-label calendar-skeleton-shimmer"></div>
                                <div class="skeleton-detail-value calendar-skeleton-shimmer"></div>
                            </div>
                        </div>
                    </div>
                    <div class="skeleton-day-item-actions">
                        <div class="skeleton-action-btn calendar-skeleton-shimmer"></div>
                    </div>
                </div>
            `;
        }

        return `
            <div class="skeleton-calendar-header">
                <div class="skeleton-header-title calendar-skeleton-shimmer"></div>
                <div class="skeleton-header-nav">
                    <div class="skeleton-nav-btn skeleton-nav-btn--icon calendar-skeleton-shimmer"></div>
                    <div class="skeleton-nav-btn calendar-skeleton-shimmer"></div>
                    <div class="skeleton-nav-btn skeleton-nav-btn--icon calendar-skeleton-shimmer"></div>
                </div>
            </div>
            <div class="skeleton-view-switcher">
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
                <div class="skeleton-view-btn calendar-skeleton-shimmer"></div>
            </div>
            <div class="skeleton-day-container">
                ${itemsHTML}
            </div>
        `;
    }

    function createTableSkeletonHTML() {
        let headerCells = '<th class="skeleton-table-cell"><div class="skeleton-shift-name calendar-skeleton-shimmer"></div></th>';
        for (let i = 0; i < 7; i++) {
            headerCells += '<th class="skeleton-table-header-cell"><div class="skeleton-header-text"></div></th>';
        }

        let bodyRows = '';
        for (let r = 0; r < 4; r++) {
            let cells = '<td class="skeleton-table-cell"><div class="skeleton-shift-name calendar-skeleton-shimmer"></div></td>';
            for (let c = 0; c < 7; c++) {
                cells += `
                    <td class="skeleton-table-cell">
                        <div class="skeleton-assignment-slots">
                            <div class="skeleton-assignment-slot calendar-skeleton-shimmer"></div>
                        </div>
                    </td>
                `;
            }
            bodyRows += `<tr class="skeleton-table-row">${cells}</tr>`;
        }

        return `
            <div class="skeleton-calendar-header">
                <div class="skeleton-header-title calendar-skeleton-shimmer"></div>
                <div class="skeleton-header-nav">
                    <div class="skeleton-nav-btn skeleton-nav-btn--icon calendar-skeleton-shimmer"></div>
                    <div class="skeleton-nav-btn calendar-skeleton-shimmer"></div>
                    <div class="skeleton-nav-btn skeleton-nav-btn--icon calendar-skeleton-shimmer"></div>
                </div>
            </div>
            <div class="skeleton-table-container">
                <table class="skeleton-table">
                    <thead class="skeleton-table-header">
                        <tr>${headerCells}</tr>
                    </thead>
                    <tbody>
                        ${bodyRows}
                    </tbody>
                </table>
            </div>
        `;
    }

    function createGenericSkeletonHTML() {
        return `
            <div class="skeleton-calendar-header">
                <div class="skeleton-header-title calendar-skeleton-shimmer"></div>
            </div>
            <div style="padding: 2rem; text-align: center;">
                <div class="spinner" style="margin: 0 auto;"></div>
            </div>
        `;
    }

    // Export to global scope
    window.CalendarSkeleton = {
        init: initializeAllSkeletons,
        initContainer: initializeCalendarSkeleton,
        signalReady: signalContentReady,
        showSkeleton: transitionToSkeleton,
        showContent: transitionToContent,
        create: createSkeleton,
        config: CONFIG
    };

    // Auto-initialize on page load
    handleServerRenderedContent();
})();
