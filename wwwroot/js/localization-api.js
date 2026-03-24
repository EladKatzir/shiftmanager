/**
 * Localization API - Client-side localization system
 *
 * Provides JavaScript access to localized strings with company override support.
 * Caches results to minimize server requests.
 *
 * Usage:
 * const text = await window.Localization.get("Button_Save");
 * const texts = await window.Localization.getMany(["Button_Save", "Button_Cancel"]);
 * const formatted = window.Localization.format("Welcome_Message", userName);
 */

(function() {
    'use strict';

    // In-memory cache
    const cache = {};
    const API_ENDPOINT = '/Api/Localization';

    // Global API
    window.Localization = {
        get: get,
        getMany: getMany,
        format: format,
        clearCache: clearCache
    };

    /**
     * Get a single localized string
     * @param {string} key - Resource key
     * @returns {Promise<string>} Localized value
     */
    async function get(key) {
        if (!key) {
            Logger.warn('LocAPI', 'Empty key provided to get()');
            return '';
        }

        // Check cache first
        if (cache[key]) {
            return cache[key];
        }

        // Fetch from server
        try {
            const result = await getMany([key]);
            return result[key] || key; // Fallback to key if not found
        } catch (error) {
            Logger.error('LocAPI', `Failed to fetch key: ${key}`, error);
            return key; // Fallback to key on error
        }
    }

    /**
     * Get multiple localized strings in a single request
     * @param {string[]} keys - Array of resource keys
     * @returns {Promise<Object>} Object mapping keys to values
     */
    async function getMany(keys) {
        if (!keys || keys.length === 0) {
            return {};
        }

        // Filter out keys that are already cached
        const uncachedKeys = keys.filter(k => !cache[k]);

        // If all keys are cached, return immediately
        if (uncachedKeys.length === 0) {
            const result = {};
            keys.forEach(k => {
                result[k] = cache[k];
            });
            return result;
        }

        // Fetch uncached keys from server
        try {
            const response = await fetch(`${API_ENDPOINT}?keys=${uncachedKeys.join(',')}`, {
                method: 'GET',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) {
                throw new Error(`Server returned ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();

            // Update cache
            Object.keys(data).forEach(key => {
                cache[key] = data[key];
            });

            // Merge cached + newly fetched
            const result = {};
            keys.forEach(k => {
                result[k] = cache[k] || k; // Fallback to key if not found
            });

            return result;
        } catch (error) {
            Logger.error('LocAPI', 'Failed to fetch localizations:', error);

            // Return keys as fallback
            const fallback = {};
            keys.forEach(k => {
                fallback[k] = cache[k] || k;
            });
            return fallback;
        }
    }

    /**
     * Format a localized string with parameters
     * @param {string} key - Resource key
     * @param {...any} params - Parameters to replace {0}, {1}, etc.
     * @returns {Promise<string>} Formatted string
     */
    async function format(key, ...params) {
        const template = await get(key);

        if (!template || params.length === 0) {
            return template;
        }

        // Replace {0}, {1}, {2}, etc. with parameters
        return template.replace(/\{(\d+)\}/g, (match, index) => {
            const paramIndex = parseInt(index, 10);
            return paramIndex < params.length ? params[paramIndex] : match;
        });
    }

    /**
     * Clear the localization cache
     * Useful when switching languages or updating overrides
     */
    function clearCache() {
        Object.keys(cache).forEach(key => delete cache[key]);
    }

    // Clear cache when language changes (culture cookie changes)
    // Monitor culture cookie changes via MutationObserver or periodic check
    let lastCulture = getCookie('.AspNetCore.Culture');
    setInterval(() => {
        const currentCulture = getCookie('.AspNetCore.Culture');
        if (currentCulture !== lastCulture) {
            clearCache();
            lastCulture = currentCulture;

            // Reload attribute localizations
            if (window.location.reload) {
                // In some cases, we might want to reload the page
                // For now, just clear cache and let next fetch re-populate
            }
        }
    }, 1000); // Check every second

    /**
     * Get cookie value by name
     */
    function getCookie(name) {
        const value = `; ${document.cookie}`;
        const parts = value.split(`; ${name}=`);
        if (parts.length === 2) {
            return parts.pop().split(';').shift();
        }
        return null;
    }

})();
