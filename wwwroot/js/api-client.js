/**
 * API Client with Rate Limit Handling (B-027)
 *
 * Provides a fetch wrapper that automatically handles:
 * - 429 (Too Many Requests) responses with exponential backoff
 * - Network errors with automatic retry
 * - Standard HTTP methods (GET, POST, PUT, DELETE)
 *
 * Rate limit headers are automatically parsed from responses:
 * - X-RateLimit-Limit: Maximum requests allowed
 * - X-RateLimit-Remaining: Requests remaining in window
 * - X-RateLimit-Reset: Unix timestamp when limit resets
 * - Retry-After: Seconds to wait before retrying
 */
window.ApiClient = (function() {
    'use strict';

    // Configuration
    const MAX_RETRIES = 3;
    const BASE_DELAY_MS = 1000;
    const MAX_RETRY_DELAY_MS = 60000; // 1 minute max wait

    /**
     * Rate limit state tracking per endpoint
     */
    const rateLimitState = {};

    /**
     * Gets the current rate limit state for an endpoint
     * @param {string} url - The API URL
     * @returns {Object|null} Rate limit info or null
     */
    function getRateLimitInfo(url) {
        const key = getEndpointKey(url);
        return rateLimitState[key] || null;
    }

    /**
     * Updates rate limit state from response headers
     * @param {string} url - The API URL
     * @param {Response} response - The fetch response
     */
    function updateRateLimitState(url, response) {
        const key = getEndpointKey(url);
        const limit = response.headers.get('X-RateLimit-Limit');
        const remaining = response.headers.get('X-RateLimit-Remaining');
        const reset = response.headers.get('X-RateLimit-Reset');

        if (limit || remaining || reset) {
            rateLimitState[key] = {
                limit: limit ? parseInt(limit) : null,
                remaining: remaining ? parseInt(remaining) : null,
                reset: reset ? parseInt(reset) : null,
                updatedAt: Date.now()
            };

            // Log when running low on requests
            if (remaining && parseInt(remaining) <= 5) {
                console.warn(`[ApiClient] Rate limit warning for ${key}: ${remaining} requests remaining`);
            }
        }
    }

    /**
     * Extracts a normalized endpoint key from a URL
     * @param {string} url - The full URL or path
     * @returns {string} Normalized endpoint key
     */
    function getEndpointKey(url) {
        const path = url.toLowerCase();
        if (path.includes('/api/calendar') || path.includes('/api/team-calendars')) {
            return 'calendar';
        }
        if (path.includes('/api/context')) {
            return 'context';
        }
        if (path.includes('/api/widget')) {
            return 'widget';
        }
        if (path.includes('/api/telemetry')) {
            return 'telemetry';
        }
        return 'default';
    }

    /**
     * Calculates delay before retry using exponential backoff
     * @param {number} retryCount - Current retry attempt (0-based)
     * @param {number|null} retryAfterHeader - Retry-After header value in seconds
     * @returns {number} Delay in milliseconds
     */
    function calculateRetryDelay(retryCount, retryAfterHeader) {
        // If server specified retry-after, respect it (convert to ms)
        if (retryAfterHeader && retryAfterHeader > 0) {
            return Math.min(retryAfterHeader * 1000, MAX_RETRY_DELAY_MS);
        }

        // Exponential backoff: 1s, 2s, 4s, etc.
        const exponentialDelay = BASE_DELAY_MS * Math.pow(2, retryCount);

        // Add jitter (0-500ms) to prevent thundering herd
        const jitter = Math.random() * 500;

        return Math.min(exponentialDelay + jitter, MAX_RETRY_DELAY_MS);
    }

    /**
     * Performs a fetch request with automatic retry on rate limit or network errors
     * @param {string} url - The URL to fetch
     * @param {Object} options - Fetch options
     * @param {number} retryCount - Current retry attempt
     * @returns {Promise<Response>} The fetch response
     */
    async function fetchWithRetry(url, options = {}, retryCount = 0) {
        try {
            const response = await fetch(url, {
                ...options,
                credentials: 'same-origin'
            });

            // Update rate limit state from headers
            updateRateLimitState(url, response);

            // Handle concurrency conflicts (409)
            if (response.status === 409) {
                const json = await parseJson(response);
                const errorCode = json?.error?.code || 'CONFLICT';
                
                if (errorCode === 'CONCURRENCY_CONFLICT') {
                    console.warn('[ApiClient] Concurrency conflict detected');
                    dispatchConcurrencyConflictEvent(url, json?.error?.details);
                }
                // Return the response for caller to handle (don't throw)
                return response;
            }

            // Handle rate limiting (429)
            if (response.status === 429) {
                const retryAfter = parseInt(response.headers.get('Retry-After') || '60');

                if (retryCount < MAX_RETRIES) {
                    const delay = calculateRetryDelay(retryCount, retryAfter);
                    console.warn(`[ApiClient] Rate limited (429). Retry ${retryCount + 1}/${MAX_RETRIES} in ${Math.round(delay/1000)}s...`);

                    // Dispatch event for UI components to show feedback
                    dispatchRateLimitEvent(url, delay, retryCount + 1);

                    await sleep(delay);
                    return fetchWithRetry(url, options, retryCount + 1);
                }

                // Max retries exceeded
                console.error('[ApiClient] Rate limit exceeded, max retries reached');
                dispatchRateLimitEvent(url, 0, -1); // -1 indicates failure
                throw new ApiError('Rate limit exceeded. Please try again later.', 429, response);
            }

            return response;
        } catch (error) {
            // Don't retry abort errors (user cancelled)
            if (error.name === 'AbortError') {
                throw error;
            }

            // Don't retry ApiErrors (already handled)
            if (error instanceof ApiError) {
                throw error;
            }

            // Retry network errors
            if (retryCount < MAX_RETRIES) {
                const delay = calculateRetryDelay(retryCount, null);
                console.warn(`[ApiClient] Network error. Retry ${retryCount + 1}/${MAX_RETRIES} in ${Math.round(delay/1000)}s...`, error.message);

                await sleep(delay);
                return fetchWithRetry(url, options, retryCount + 1);
            }

            // Max retries exceeded
            console.error('[ApiClient] Network error, max retries reached:', error);
            throw new ApiError('Network error. Please check your connection.', 0, null);
        }
    }

    /**
     * Dispatches a custom event for concurrency conflict handling
     * UI components can listen to this for feedback
     */
    function dispatchConcurrencyConflictEvent(url, details) {
        const event = new CustomEvent('api:concurrencyconflict', {
            detail: {
                url: url,
                endpoint: getEndpointKey(url),
                entityType: details?.entityType || 'unknown',
                entityId: details?.entityId || null
            }
        });
        window.dispatchEvent(event);
    }

    /**
     * Dispatches a custom event for rate limit handling
     * UI components can listen to this for feedback
     */
    function dispatchRateLimitEvent(url, delayMs, retryAttempt) {
        const event = new CustomEvent('api:ratelimit', {
            detail: {
                url: url,
                endpoint: getEndpointKey(url),
                delayMs: delayMs,
                retryAttempt: retryAttempt,
                isFinalFailure: retryAttempt === -1
            }
        });
        window.dispatchEvent(event);
    }

    /**
     * Sleep utility
     * @param {number} ms - Milliseconds to sleep
     * @returns {Promise<void>}
     */
    function sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    /**
     * Custom error class for API errors
     */
    class ApiError extends Error {
        constructor(message, status, response) {
            super(message);
            this.name = 'ApiError';
            this.status = status;
            this.response = response;
        }
    }

    /**
     * Parses JSON response, returns null on failure
     * @param {Response} response - Fetch response
     * @returns {Promise<Object|null>} Parsed JSON or null
     */
    async function parseJson(response) {
        try {
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('application/json')) {
                return await response.json();
            }
            return null;
        } catch (e) {
            console.warn('[ApiClient] Failed to parse JSON response:', e);
            return null;
        }
    }

    // Public API
    return {
        /**
         * Performs a GET request
         * @param {string} url - The URL to fetch
         * @param {Object} options - Additional fetch options
         * @returns {Promise<Response>}
         */
        get: (url, options = {}) => fetchWithRetry(url, { ...options, method: 'GET' }),

        /**
         * Performs a POST request with JSON body
         * @param {string} url - The URL to fetch
         * @param {Object} data - Data to send as JSON
         * @param {Object} options - Additional fetch options
         * @returns {Promise<Response>}
         */
        post: (url, data, options = {}) => fetchWithRetry(url, {
            ...options,
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(options.headers || {})
            },
            body: JSON.stringify(data)
        }),

        /**
         * Performs a PUT request with JSON body
         * @param {string} url - The URL to fetch
         * @param {Object} data - Data to send as JSON
         * @param {Object} options - Additional fetch options
         * @returns {Promise<Response>}
         */
        put: (url, data, options = {}) => fetchWithRetry(url, {
            ...options,
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                ...(options.headers || {})
            },
            body: JSON.stringify(data)
        }),

        /**
         * Performs a DELETE request
         * @param {string} url - The URL to fetch
         * @param {Object} options - Additional fetch options
         * @returns {Promise<Response>}
         */
        delete: (url, options = {}) => fetchWithRetry(url, { ...options, method: 'DELETE' }),

        /**
         * Performs a PATCH request with JSON body
         * @param {string} url - The URL to fetch
         * @param {Object} data - Data to send as JSON
         * @param {Object} options - Additional fetch options
         * @returns {Promise<Response>}
         */
        patch: (url, data, options = {}) => fetchWithRetry(url, {
            ...options,
            method: 'PATCH',
            headers: {
                'Content-Type': 'application/json',
                ...(options.headers || {})
            },
            body: JSON.stringify(data)
        }),

        /**
         * Performs a request with form data
         * @param {string} url - The URL to fetch
         * @param {FormData} formData - Form data to send
         * @param {Object} options - Additional fetch options
         * @returns {Promise<Response>}
         */
        postForm: (url, formData, options = {}) => fetchWithRetry(url, {
            ...options,
            method: 'POST',
            body: formData
            // Don't set Content-Type - browser will set it with boundary
        }),

        /**
         * Gets current rate limit info for an endpoint
         * @param {string} url - The API URL
         * @returns {Object|null} Rate limit state
         */
        getRateLimitInfo: getRateLimitInfo,

        /**
         * Utility to parse JSON from a response
         * @param {Response} response - Fetch response
         * @returns {Promise<Object|null>}
         */
        parseJson: parseJson,

        /**
         * The ApiError class for error type checking
         */
        ApiError: ApiError
    };
})();

// Log initialization
console.log('[ApiClient] Initialized with rate limit handling (B-027)');
