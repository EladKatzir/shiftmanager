/**
 * Cache Management (B-016)
 * Ensures users see fresh data after mutations
 *
 * Features:
 * - Automatic cache invalidation after form submissions
 * - Browser back/forward navigation handling (bfcache)
 * - Cache busting for API requests
 * - Session storage persistence across tabs
 */
(function() {
    'use strict';

    // Track when data was last modified
    var lastModified = Date.now();

    /**
     * Mark data as stale (call after any mutation)
     */
    function invalidate() {
        lastModified = Date.now();
        sessionStorage.setItem('shifty_cache_version', lastModified.toString());

        // Dispatch event for listeners
        window.dispatchEvent(new CustomEvent('shifty:cache-invalidated'));

        Logger.log('Cache', 'Cache invalidated at', new Date(lastModified).toISOString());
    }

    /**
     * Check if cached data is stale
     * @param {number} cachedTimestamp - The timestamp when data was cached
     * @returns {boolean} True if the cached data is older than last modification
     */
    function isStale(cachedTimestamp) {
        return cachedTimestamp < lastModified;
    }

    /**
     * Add cache buster to URL
     * @param {string} url - The URL to add cache buster to
     * @returns {string} URL with cache buster query parameter
     */
    function bustUrl(url) {
        var separator = url.indexOf('?') !== -1 ? '&' : '?';
        return url + separator + '_=' + lastModified;
    }

    /**
     * Fetch with automatic cache busting
     * @param {string} url - The URL to fetch
     * @param {Object} options - Fetch options
     * @returns {Promise<Response>} The fetch response
     */
    function fetchFresh(url, options) {
        options = options || {};
        options.headers = options.headers || {};
        options.headers['Cache-Control'] = 'no-cache';

        return fetch(bustUrl(url), options);
    }

    /**
     * Handle browser back/forward navigation (bfcache)
     * Checks if data was modified in another tab/window
     */
    window.addEventListener('pageshow', function(event) {
        if (event.persisted) {
            // Page was restored from bfcache
            var savedVersion = sessionStorage.getItem('shifty_cache_version');
            if (savedVersion && parseInt(savedVersion, 10) > lastModified) {
                // Data was modified elsewhere, refresh the page
                Logger.log('Cache', 'Data modified in another tab, refreshing...');
                window.location.reload();
            }
        }
    });

    /**
     * Handle visibility change (tab becoming visible)
     * Check for stale data when user returns to tab
     */
    document.addEventListener('visibilitychange', function() {
        if (document.visibilityState === 'visible') {
            var savedVersion = sessionStorage.getItem('shifty_cache_version');
            if (savedVersion && parseInt(savedVersion, 10) > lastModified) {
                // Data was modified in another tab
                lastModified = parseInt(savedVersion, 10);
                Logger.log('Cache', 'Updated cache version from session storage');
                // Dispatch event so components can refresh their data
                window.dispatchEvent(new CustomEvent('shifty:cache-updated', {
                    detail: { version: lastModified }
                }));
            }
        }
    });

    // Initialize from session storage
    var saved = sessionStorage.getItem('shifty_cache_version');
    if (saved) {
        var savedTime = parseInt(saved, 10);
        if (!isNaN(savedTime) && savedTime > lastModified) {
            lastModified = savedTime;
        }
    }

    // Auto-invalidate on form submissions that modify data
    document.addEventListener('submit', function(e) {
        var form = e.target;
        if (form && form.method && form.method.toUpperCase() !== 'GET') {
            // POST/PUT/DELETE - invalidate cache after a short delay
            // to ensure the server has processed the request
            setTimeout(invalidate, 100);
        }
    });

    // Listen for successful AJAX mutations
    // Integration with fetch API wrapper
    var originalFetch = window.fetch;
    window.fetch = function(url, options) {
        return originalFetch.apply(this, arguments).then(function(response) {
            // Invalidate cache on successful mutation requests
            if (options && options.method &&
                ['POST', 'PUT', 'DELETE', 'PATCH'].indexOf(options.method.toUpperCase()) !== -1 &&
                response.ok) {
                setTimeout(invalidate, 50);
            }
            return response;
        });
    };

    // Public API
    window.CacheManager = {
        /**
         * Invalidate the cache (call after any data mutation)
         */
        invalidate: invalidate,

        /**
         * Check if cached data is stale
         * @param {number} cachedTimestamp - Timestamp when data was cached
         * @returns {boolean} True if stale
         */
        isStale: isStale,

        /**
         * Add cache buster query parameter to URL
         * @param {string} url - URL to bust
         * @returns {string} URL with cache buster
         */
        bustUrl: bustUrl,

        /**
         * Fetch with automatic cache busting headers
         * @param {string} url - URL to fetch
         * @param {Object} options - Fetch options
         * @returns {Promise<Response>} Fetch response
         */
        fetchFresh: fetchFresh,

        /**
         * Get current cache version timestamp
         * @returns {number} Current cache version
         */
        getVersion: function() {
            return lastModified;
        },

        /**
         * Subscribe to cache invalidation events
         * @param {Function} callback - Function to call when cache is invalidated
         * @returns {Function} Unsubscribe function
         */
        onInvalidate: function(callback) {
            var handler = function() { callback(); };
            window.addEventListener('shifty:cache-invalidated', handler);
            return function() {
                window.removeEventListener('shifty:cache-invalidated', handler);
            };
        },

        /**
         * Subscribe to cache update events (when another tab modifies data)
         * @param {Function} callback - Function to call with new version
         * @returns {Function} Unsubscribe function
         */
        onUpdate: function(callback) {
            var handler = function(e) { callback(e.detail.version); };
            window.addEventListener('shifty:cache-updated', handler);
            return function() {
                window.removeEventListener('shifty:cache-updated', handler);
            };
        }
    };

})();
