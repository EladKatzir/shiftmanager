/**
 * Lazy Script Loader (B-030)
 * Loads scripts on demand to reduce initial payload
 *
 * ShiftManager uses this utility to implement code splitting:
 * - Calendar components loaded only on calendar pages
 * - Admin-only components not loaded for regular users
 * - Modals lazy-loaded on trigger
 * - Target: Initial JS payload <200KB gzipped
 */
window.LazyLoader = (function() {
    'use strict';

    const loadedScripts = new Set();
    const loadingPromises = new Map();
    const loadedStyles = new Set();

    /**
     * Load a script dynamically
     * @param {string} src - Script URL
     * @param {Object} options - Optional settings
     * @param {boolean} options.async - Whether script is async (default: true)
     * @param {boolean} options.defer - Whether script is deferred (default: false)
     * @param {string} options.integrity - Subresource integrity hash
     * @param {string} options.crossOrigin - CORS setting
     * @returns {Promise}
     */
    function loadScript(src, options = {}) {
        // Already loaded
        if (loadedScripts.has(src)) {
            return Promise.resolve();
        }

        // Currently loading
        if (loadingPromises.has(src)) {
            return loadingPromises.get(src);
        }

        const { async = true, defer = false, integrity, crossOrigin } = options;

        const promise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = src;

            if (async) script.async = true;
            if (defer) script.defer = true;
            if (integrity) script.integrity = integrity;
            if (crossOrigin) script.crossOrigin = crossOrigin;

            script.onload = () => {
                loadedScripts.add(src);
                loadingPromises.delete(src);
                console.log('[LazyLoader] Loaded script:', src);
                resolve();
            };

            script.onerror = () => {
                loadingPromises.delete(src);
                const error = new Error(`Failed to load script: ${src}`);
                console.error('[LazyLoader]', error.message);
                reject(error);
            };

            document.body.appendChild(script);
        });

        loadingPromises.set(src, promise);
        return promise;
    }

    /**
     * Load multiple scripts in order (sequential)
     * @param {string[]} srcs - Array of script URLs to load in order
     * @returns {Promise}
     */
    async function loadScripts(srcs) {
        for (const src of srcs) {
            await loadScript(src);
        }
    }

    /**
     * Load multiple scripts in parallel
     * @param {string[]} srcs - Array of script URLs to load simultaneously
     * @returns {Promise}
     */
    function loadScriptsParallel(srcs) {
        return Promise.all(srcs.map(src => loadScript(src)));
    }

    /**
     * Load a CSS stylesheet dynamically
     * @param {string} href - Stylesheet URL
     * @returns {Promise}
     */
    function loadStyle(href) {
        // Already loaded
        if (loadedStyles.has(href)) {
            return Promise.resolve();
        }

        return new Promise((resolve, reject) => {
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = href;

            link.onload = () => {
                loadedStyles.add(href);
                console.log('[LazyLoader] Loaded stylesheet:', href);
                resolve();
            };

            link.onerror = () => {
                const error = new Error(`Failed to load stylesheet: ${href}`);
                console.error('[LazyLoader]', error.message);
                reject(error);
            };

            document.head.appendChild(link);
        });
    }

    /**
     * Load script when element becomes visible (uses IntersectionObserver)
     * @param {string} selector - CSS selector for target elements
     * @param {string} scriptSrc - Script URL to load
     * @param {Function} callback - Optional callback after script loads
     * @param {Object} observerOptions - IntersectionObserver options
     */
    function loadOnVisible(selector, scriptSrc, callback, observerOptions = {}) {
        const elements = document.querySelectorAll(selector);
        if (elements.length === 0) return;

        const defaultOptions = {
            root: null,
            rootMargin: '50px', // Preload slightly before visible
            threshold: 0
        };

        const options = { ...defaultOptions, ...observerOptions };

        if ('IntersectionObserver' in window) {
            const observer = new IntersectionObserver((entries) => {
                entries.forEach(entry => {
                    if (entry.isIntersecting) {
                        loadScript(scriptSrc).then(() => {
                            if (callback) callback(entry.target);
                        }).catch(err => {
                            console.error('[LazyLoader] loadOnVisible error:', err);
                        });
                        observer.disconnect();
                    }
                });
            }, options);

            elements.forEach(el => observer.observe(el));
        } else {
            // Fallback: load immediately for browsers without IntersectionObserver
            loadScript(scriptSrc).then(() => {
                if (callback) callback(elements[0]);
            });
        }
    }

    /**
     * Load script on user interaction (first event only)
     * @param {string} selector - CSS selector for target elements
     * @param {string|string[]} events - Event type(s) to listen for
     * @param {string} scriptSrc - Script URL to load
     * @param {Function} callback - Optional callback after script loads, receives the element
     */
    function loadOnInteraction(selector, events, scriptSrc, callback) {
        const elements = document.querySelectorAll(selector);
        if (elements.length === 0) return;

        const eventList = Array.isArray(events) ? events : [events];
        let loaded = false;

        const handler = function(e) {
            if (loaded) return;
            loaded = true;

            // Remove listeners from all elements
            elements.forEach(el => {
                eventList.forEach(event => {
                    el.removeEventListener(event, handler);
                });
            });

            loadScript(scriptSrc).then(() => {
                if (callback) callback(e.currentTarget, e);
            }).catch(err => {
                console.error('[LazyLoader] loadOnInteraction error:', err);
            });
        };

        elements.forEach(el => {
            eventList.forEach(event => {
                el.addEventListener(event, handler, { once: false, passive: true });
            });
        });
    }

    /**
     * Load script after idle time (uses requestIdleCallback)
     * @param {string} scriptSrc - Script URL to load
     * @param {Object} options - requestIdleCallback options
     * @returns {Promise}
     */
    function loadWhenIdle(scriptSrc, options = { timeout: 2000 }) {
        return new Promise((resolve, reject) => {
            const load = () => {
                loadScript(scriptSrc).then(resolve).catch(reject);
            };

            if ('requestIdleCallback' in window) {
                requestIdleCallback(load, options);
            } else {
                // Fallback: use setTimeout
                setTimeout(load, 200);
            }
        });
    }

    /**
     * Preload a script without executing (uses link rel=preload)
     * @param {string} src - Script URL to preload
     */
    function preload(src) {
        if (document.querySelector(`link[rel="preload"][href="${src}"]`)) {
            return; // Already preloading
        }

        const link = document.createElement('link');
        link.rel = 'preload';
        link.as = 'script';
        link.href = src;
        document.head.appendChild(link);
        console.log('[LazyLoader] Preloading script:', src);
    }

    /**
     * Prefetch a script for future navigation (low priority)
     * @param {string} src - Script URL to prefetch
     */
    function prefetch(src) {
        if (document.querySelector(`link[rel="prefetch"][href="${src}"]`)) {
            return; // Already prefetching
        }

        const link = document.createElement('link');
        link.rel = 'prefetch';
        link.href = src;
        document.head.appendChild(link);
        console.log('[LazyLoader] Prefetching script:', src);
    }

    /**
     * Check if a script has been loaded
     * @param {string} src - Script URL
     * @returns {boolean}
     */
    function isLoaded(src) {
        return loadedScripts.has(src);
    }

    /**
     * Check if a script is currently loading
     * @param {string} src - Script URL
     * @returns {boolean}
     */
    function isLoading(src) {
        return loadingPromises.has(src);
    }

    /**
     * Get all loaded scripts
     * @returns {string[]}
     */
    function getLoadedScripts() {
        return Array.from(loadedScripts);
    }

    // Public API
    return {
        load: loadScript,
        loadAll: loadScripts,
        loadParallel: loadScriptsParallel,
        loadStyle: loadStyle,
        loadOnVisible: loadOnVisible,
        loadOnInteraction: loadOnInteraction,
        loadWhenIdle: loadWhenIdle,
        preload: preload,
        prefetch: prefetch,
        isLoaded: isLoaded,
        isLoading: isLoading,
        getLoaded: getLoadedScripts
    };
})();
