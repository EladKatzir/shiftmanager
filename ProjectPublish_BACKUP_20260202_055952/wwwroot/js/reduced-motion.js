/**
 * Reduced Motion Utility (B-001-EXT)
 * Provides a centralized way to check and respond to the prefers-reduced-motion preference.
 *
 * This utility extends the CSS prefers-reduced-motion support to JavaScript animations.
 *
 * Usage:
 *
 * 1. Check current preference:
 *    if (ReducedMotion.isEnabled()) {
 *        // Skip or simplify animation
 *    }
 *
 * 2. Get animation duration (returns 0 when reduced motion is enabled):
 *    const duration = ReducedMotion.getDuration(300); // Returns 0 or 300
 *
 * 3. Listen for preference changes:
 *    ReducedMotion.onChange((prefersReduced) => {
 *        console.log('Reduced motion:', prefersReduced ? 'enabled' : 'disabled');
 *    });
 *
 * 4. Animate conditionally:
 *    ReducedMotion.animate(element, {
 *        transform: 'translateX(100px)',
 *        opacity: 0
 *    }, {
 *        duration: 300,
 *        easing: 'ease-out'
 *    }, () => {
 *        // Completion callback
 *    });
 *
 * 5. Run different code based on preference:
 *    ReducedMotion.run(
 *        function() { // Normal animated behavior },
 *        function() { // Instant/reduced behavior }
 *    );
 */

(function() {
    'use strict';

    // Media query for prefers-reduced-motion
    let mediaQuery = null;

    // Cache for current preference state
    let prefersReducedMotion = false;

    // Registered change listeners
    const listeners = [];

    /**
     * Initialize the media query and set up listener
     */
    function initialize() {
        // Check if matchMedia is supported
        if (!window.matchMedia) {
            console.warn('[ReducedMotion] matchMedia not supported, defaulting to full motion');
            prefersReducedMotion = false;
            return;
        }

        // Create media query
        mediaQuery = window.matchMedia('(prefers-reduced-motion: reduce)');

        // Set initial state
        prefersReducedMotion = mediaQuery.matches;

        // Log initial state
        console.log('[ReducedMotion] Initialized, reduced motion:', prefersReducedMotion ? 'enabled' : 'disabled');

        // Set up listener for changes (user can toggle in OS settings)
        // Use addEventListener with proper fallback for older browsers
        if (mediaQuery.addEventListener) {
            mediaQuery.addEventListener('change', handleMediaQueryChange);
        } else if (mediaQuery.addListener) {
            // Deprecated but needed for older Safari
            mediaQuery.addListener(handleMediaQueryChange);
        }

        // Also set a data attribute on documentElement for CSS integration
        updateDocumentAttribute();
    }

    /**
     * Handle media query change events
     * @param {MediaQueryListEvent} event - The change event
     */
    function handleMediaQueryChange(event) {
        const newValue = event.matches;

        // Only process if actually changed
        if (newValue !== prefersReducedMotion) {
            prefersReducedMotion = newValue;

            console.log('[ReducedMotion] Preference changed, reduced motion:', prefersReducedMotion ? 'enabled' : 'disabled');

            // Update document attribute
            updateDocumentAttribute();

            // Notify all registered listeners
            listeners.forEach(callback => {
                try {
                    callback(prefersReducedMotion);
                } catch (error) {
                    console.error('[ReducedMotion] Error in change listener:', error);
                }
            });
        }
    }

    /**
     * Update the data attribute on documentElement for CSS integration
     */
    function updateDocumentAttribute() {
        if (prefersReducedMotion) {
            document.documentElement.setAttribute('data-reduced-motion', 'true');
        } else {
            document.documentElement.removeAttribute('data-reduced-motion');
        }
    }

    /**
     * Check if reduced motion is currently enabled
     * @returns {boolean} True if user prefers reduced motion
     */
    function isEnabled() {
        return prefersReducedMotion;
    }

    /**
     * Get an animation duration that respects reduced motion preference
     * @param {number} normalDuration - The normal animation duration in ms
     * @param {number} [reducedDuration=0] - Optional alternative duration when reduced (default: 0)
     * @returns {number} The appropriate duration based on preference
     */
    function getDuration(normalDuration, reducedDuration = 0) {
        return prefersReducedMotion ? reducedDuration : normalDuration;
    }

    /**
     * Get a delay that respects reduced motion preference
     * @param {number} normalDelay - The normal animation delay in ms
     * @returns {number} The delay (0 when reduced motion is enabled)
     */
    function getDelay(normalDelay) {
        return prefersReducedMotion ? 0 : normalDelay;
    }

    /**
     * Register a callback for when reduced motion preference changes
     * @param {Function} callback - Called with (prefersReduced: boolean) when preference changes
     * @returns {Function} Unsubscribe function
     */
    function onChange(callback) {
        if (typeof callback !== 'function') {
            console.warn('[ReducedMotion] onChange expects a function');
            return () => {};
        }

        listeners.push(callback);

        // Return unsubscribe function
        return function unsubscribe() {
            const index = listeners.indexOf(callback);
            if (index > -1) {
                listeners.splice(index, 1);
            }
        };
    }

    /**
     * Run different functions based on motion preference
     * @param {Function} normalFn - Function to run with normal motion
     * @param {Function} [reducedFn] - Function to run with reduced motion (optional, defaults to normalFn)
     * @returns {*} Return value from whichever function was called
     */
    function run(normalFn, reducedFn) {
        if (prefersReducedMotion) {
            return reducedFn ? reducedFn() : normalFn();
        }
        return normalFn();
    }

    /**
     * Animate an element with Web Animations API, respecting reduced motion
     * When reduced motion is enabled, changes are applied instantly
     *
     * @param {HTMLElement} element - Element to animate
     * @param {Object|Object[]} keyframes - CSS properties to animate (or array of keyframes)
     * @param {Object} options - Animation options
     * @param {number} options.duration - Animation duration in ms
     * @param {string} [options.easing='ease'] - Easing function
     * @param {string} [options.fill='forwards'] - Fill mode
     * @param {Function} [onComplete] - Optional callback when animation completes
     * @returns {Animation|null} The Animation object, or null if instant
     */
    function animate(element, keyframes, options, onComplete) {
        if (!element || !keyframes) {
            console.warn('[ReducedMotion] animate requires element and keyframes');
            return null;
        }

        const { duration = 300, easing = 'ease', fill = 'forwards' } = options || {};

        if (prefersReducedMotion) {
            // Apply final state instantly
            const finalKeyframe = Array.isArray(keyframes)
                ? keyframes[keyframes.length - 1]
                : keyframes;

            Object.keys(finalKeyframe).forEach(prop => {
                // Handle both CSS property names (kebab-case) and JS property names (camelCase)
                element.style[prop] = finalKeyframe[prop];
            });

            // Call completion callback immediately
            if (typeof onComplete === 'function') {
                // Use microtask to maintain async behavior consistency
                Promise.resolve().then(onComplete);
            }

            return null;
        }

        // Check for Web Animations API support
        if (!element.animate) {
            console.warn('[ReducedMotion] Web Animations API not supported');
            // Fallback: apply styles directly
            const finalKeyframe = Array.isArray(keyframes)
                ? keyframes[keyframes.length - 1]
                : keyframes;

            Object.keys(finalKeyframe).forEach(prop => {
                element.style[prop] = finalKeyframe[prop];
            });

            if (typeof onComplete === 'function') {
                Promise.resolve().then(onComplete);
            }

            return null;
        }

        // Run animation normally
        const animation = element.animate(keyframes, {
            duration,
            easing,
            fill
        });

        if (typeof onComplete === 'function') {
            animation.onfinish = onComplete;
        }

        return animation;
    }

    /**
     * Animate with CSS classes, respecting reduced motion
     * @param {HTMLElement} element - Element to animate
     * @param {string} animationClass - CSS class that triggers animation
     * @param {number} duration - Expected animation duration in ms
     * @param {Function} [onComplete] - Optional callback when animation completes
     */
    function animateWithClass(element, animationClass, duration, onComplete) {
        if (!element || !animationClass) {
            console.warn('[ReducedMotion] animateWithClass requires element and animationClass');
            return;
        }

        element.classList.add(animationClass);

        if (prefersReducedMotion) {
            // For reduced motion, skip waiting for animation
            if (typeof onComplete === 'function') {
                Promise.resolve().then(onComplete);
            }
        } else {
            // Wait for animation to complete
            setTimeout(() => {
                if (typeof onComplete === 'function') {
                    onComplete();
                }
            }, duration);
        }
    }

    /**
     * Remove animation class after completion, respecting reduced motion
     * @param {HTMLElement} element - Element with animation
     * @param {string} animationClass - CSS class to remove
     * @param {number} duration - Animation duration in ms
     * @param {Function} [onComplete] - Optional callback after removal
     */
    function removeAnimationClass(element, animationClass, duration, onComplete) {
        if (!element || !animationClass) return;

        const actualDuration = getDuration(duration);

        setTimeout(() => {
            element.classList.remove(animationClass);
            if (typeof onComplete === 'function') {
                onComplete();
            }
        }, actualDuration);
    }

    /**
     * Sleep utility that respects reduced motion (returns immediately when reduced)
     * @param {number} ms - Duration to sleep in milliseconds
     * @returns {Promise} Promise that resolves after duration (or immediately if reduced motion)
     */
    function sleep(ms) {
        const duration = getDuration(ms);
        return new Promise(resolve => setTimeout(resolve, duration));
    }

    /**
     * Create a CSS transition string that respects reduced motion
     * @param {string} property - CSS property to transition (or 'all')
     * @param {number} duration - Duration in ms
     * @param {string} [easing='ease'] - Easing function
     * @returns {string} CSS transition value, or 'none' for reduced motion
     */
    function getTransition(property, duration, easing = 'ease') {
        if (prefersReducedMotion) {
            return 'none';
        }
        return `${property} ${duration}ms ${easing}`;
    }

    // Initialize on load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }

    // Expose public API
    window.ReducedMotion = {
        // Core methods
        isEnabled,
        getDuration,
        getDelay,
        onChange,
        run,

        // Animation helpers
        animate,
        animateWithClass,
        removeAnimationClass,
        sleep,
        getTransition,

        // Direct media query access (for advanced use)
        get mediaQuery() {
            return mediaQuery;
        }
    };

    console.log('[ReducedMotion] Utility loaded');

})();
