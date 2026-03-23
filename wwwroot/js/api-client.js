/**
 * API Client with Rate Limit Handling (B-027) and Error Handling (B-017)
 *
 * Provides a fetch wrapper that automatically handles:
 * - 429 (Too Many Requests) responses with exponential backoff
 * - Network errors with automatic retry
 * - Timeout detection (10s threshold)
 * - Specific error handling for 403, 500, network errors
 * - Standard HTTP methods (GET, POST, PUT, DELETE)
 *
 * Rate limit headers are automatically parsed from responses:
 * - X-RateLimit-Limit: Maximum requests allowed
 * - X-RateLimit-Remaining: Requests remaining in window
 * - X-RateLimit-Reset: Unix timestamp when limit resets
 * - Retry-After: Seconds to wait before retrying
 *
 * Error Events (B-017):
 * - api:timeout - Request took longer than 10s
 * - api:accessdenied - 403 Forbidden response
 * - api:servererror - 500+ Server error response
 * - api:networkerror - Network connectivity issue
 * - api:partialload - Some data loaded, some failed
 */
window.ApiClient = (function() {
    'use strict';

    // Configuration
    const MAX_RETRIES = 3;
    const BASE_DELAY_MS = 1000;
    const MAX_RETRY_DELAY_MS = 60000; // 1 minute max wait
    const REQUEST_TIMEOUT_MS = 10000; // 10 second timeout (B-017)

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
     * Includes timeout detection (B-017) and specific error handling
     * @param {string} url - The URL to fetch
     * @param {Object} options - Fetch options
     * @param {number} retryCount - Current retry attempt
     * @returns {Promise<Response>} The fetch response
     */
    async function fetchWithRetry(url, options = {}, retryCount = 0) {
        // Set up abort controller for timeout (B-017)
        const controller = new AbortController();
        const existingSignal = options.signal;
        const timeoutId = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

        // If there's an existing signal, link it to our controller
        if (existingSignal) {
            existingSignal.addEventListener('abort', () => controller.abort());
        }

        try {
            const response = await fetch(url, {
                ...options,
                credentials: 'same-origin',
                signal: controller.signal,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    ...(options.headers || {})
                }
            });

            // Clear timeout on successful response
            clearTimeout(timeoutId);

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

            // Handle access denied (403) - B-017
            if (response.status === 403) {
                console.warn('[ApiClient] Access denied (403) for:', url);
                dispatchAccessDeniedEvent(url);
                throw new AccessDeniedError(url, response);
            }

            // Handle server errors (500+) - B-017
            if (response.status >= 500) {
                console.error(`[ApiClient] Server error (${response.status}) for:`, url);
                dispatchServerErrorEvent(url, response.status);
                throw new ServerError(url, response.status, response);
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
            // Clear timeout on error
            clearTimeout(timeoutId);

            // Handle timeout (AbortError from our controller) - B-017
            if (error.name === 'AbortError') {
                // Check if it was our timeout or user cancellation
                if (existingSignal && existingSignal.aborted) {
                    // User cancelled - don't retry
                    throw error;
                }
                // Our timeout fired
                console.warn('[ApiClient] Request timed out after 10s:', url);
                dispatchTimeoutEvent(url);
                throw new TimeoutError(url);
            }

            // Don't retry our custom errors (already handled)
            if (error instanceof AccessDeniedError) {
                throw error;
            }

            if (error instanceof ServerError || error instanceof TimeoutError) {
                // These are retryable but we've already dispatched events
                throw error;
            }

            // Don't retry other ApiErrors (already handled)
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

            // Max retries exceeded - B-017
            console.error('[ApiClient] Network error, max retries reached:', error);
            dispatchNetworkErrorEvent(url);
            throw new NetworkError(url, error);
        }
    }

    /**
     * Fetches multiple URLs and returns a PartialLoadResult (B-017)
     * Allows partial success when some requests fail
     * @param {Array<{url: string, options?: Object}>} requests - Array of request configs
     * @returns {Promise<PartialLoadResult>} Result with successful data and failed requests
     */
    async function fetchMultiple(requests) {
        const results = await Promise.allSettled(
            requests.map(async (req) => {
                const response = await fetchWithRetry(req.url, req.options || {});
                if (!response.ok) {
                    throw new ApiError(`Request failed with status ${response.status}`, response.status, response);
                }
                return {
                    url: req.url,
                    data: await parseJson(response)
                };
            })
        );

        const successfulData = [];
        const failedRequests = [];

        results.forEach((result, index) => {
            if (result.status === 'fulfilled') {
                successfulData.push(result.value);
            } else {
                failedRequests.push({
                    url: requests[index].url,
                    error: result.reason
                });
            }
        });

        const partialResult = new PartialLoadResult(successfulData, failedRequests, requests.length);

        // Dispatch event if partial load occurred
        if (partialResult.isPartial) {
            dispatchPartialLoadEvent(partialResult);
        }

        return partialResult;
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
     * Dispatches a custom event for timeout errors (B-017)
     * @param {string} url - The URL that timed out
     */
    function dispatchTimeoutEvent(url) {
        const event = new CustomEvent('api:timeout', {
            detail: {
                url: url,
                endpoint: getEndpointKey(url),
                retryable: true,
                localizationKey: 'Error_Timeout',
                titleKey: 'Error_Timeout_Title'
            }
        });
        window.dispatchEvent(event);
    }

    /**
     * Dispatches a custom event for access denied errors (B-017)
     * @param {string} url - The URL that returned 403
     */
    function dispatchAccessDeniedEvent(url) {
        const event = new CustomEvent('api:accessdenied', {
            detail: {
                url: url,
                endpoint: getEndpointKey(url),
                retryable: false,
                localizationKey: 'Error_AccessDenied',
                titleKey: 'Error_AccessDenied_Title'
            }
        });
        window.dispatchEvent(event);
    }

    /**
     * Dispatches a custom event for server errors (B-017)
     * @param {string} url - The URL that returned 500+
     * @param {number} status - The HTTP status code
     */
    function dispatchServerErrorEvent(url, status) {
        const event = new CustomEvent('api:servererror', {
            detail: {
                url: url,
                endpoint: getEndpointKey(url),
                status: status,
                retryable: true,
                localizationKey: 'Error_ServerError',
                titleKey: 'Error_ServerError_Title'
            }
        });
        window.dispatchEvent(event);
    }

    /**
     * Dispatches a custom event for network errors (B-017)
     * @param {string} url - The URL that failed
     */
    function dispatchNetworkErrorEvent(url) {
        const event = new CustomEvent('api:networkerror', {
            detail: {
                url: url,
                endpoint: getEndpointKey(url),
                retryable: true,
                localizationKey: 'Error_NetworkError',
                titleKey: 'Error_NetworkError_Title'
            }
        });
        window.dispatchEvent(event);
    }

    /**
     * Dispatches a custom event for partial load results (B-017)
     * @param {PartialLoadResult} result - The partial load result
     */
    function dispatchPartialLoadEvent(result) {
        const event = new CustomEvent('api:partialload', {
            detail: {
                successCount: result.successCount,
                failedCount: result.failedRequests.length,
                totalCount: result.totalRequests,
                failedRequests: result.failedRequests,
                localizationKey: 'Error_PartialLoad',
                titleKey: 'Error_PartialLoad_Title'
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
            this.retryable = false;
        }
    }

    /**
     * Error class for timeout errors (B-017)
     * Thrown when request exceeds REQUEST_TIMEOUT_MS
     */
    class TimeoutError extends ApiError {
        constructor(url) {
            super('Loading is taking longer than expected. Please try again.', 0, null);
            this.name = 'TimeoutError';
            this.url = url;
            this.retryable = true;
            this.localizationKey = 'Error_Timeout';
            this.titleKey = 'Error_Timeout_Title';
        }
    }

    /**
     * Error class for access denied errors (B-017)
     * Thrown on 403 Forbidden responses
     */
    class AccessDeniedError extends ApiError {
        constructor(url, response) {
            super('You do not have permission to access this resource.', 403, response);
            this.name = 'AccessDeniedError';
            this.url = url;
            this.retryable = false; // 403 should NOT be retried
            this.localizationKey = 'Error_AccessDenied';
            this.titleKey = 'Error_AccessDenied_Title';
        }
    }

    /**
     * Error class for server errors (B-017)
     * Thrown on 500+ responses
     */
    class ServerError extends ApiError {
        constructor(url, status, response) {
            super('Could not load the requested data. Please try again.', status, response);
            this.name = 'ServerError';
            this.url = url;
            this.retryable = true;
            this.localizationKey = 'Error_ServerError';
            this.titleKey = 'Error_ServerError_Title';
        }
    }

    /**
     * Error class for network errors (B-017)
     * Thrown when network connectivity fails
     */
    class NetworkError extends ApiError {
        constructor(url, originalError) {
            super('Network error. Please check your connection.', 0, null);
            this.name = 'NetworkError';
            this.url = url;
            this.originalError = originalError;
            this.retryable = true;
            this.localizationKey = 'Error_NetworkError';
            this.titleKey = 'Error_NetworkError_Title';
        }
    }

    /**
     * Partial load result for batch requests (B-017)
     * Contains both successful data and error information
     */
    class PartialLoadResult {
        constructor(successfulData, failedRequests, totalRequests) {
            this.data = successfulData;
            this.failedRequests = failedRequests;
            this.totalRequests = totalRequests;
            this.successCount = totalRequests - failedRequests.length;
            this.isPartial = failedRequests.length > 0 && this.successCount > 0;
            this.isComplete = failedRequests.length === 0;
            this.isEmpty = this.successCount === 0;
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
         * Fetches multiple URLs with partial load support (B-017)
         * @param {Array<{url: string, options?: Object}>} requests - Request configs
         * @returns {Promise<PartialLoadResult>}
         */
        fetchMultiple: fetchMultiple,

        /**
         * The ApiError class for error type checking
         */
        ApiError: ApiError,

        /**
         * Error class for timeout errors (B-017)
         */
        TimeoutError: TimeoutError,

        /**
         * Error class for access denied errors (B-017)
         */
        AccessDeniedError: AccessDeniedError,

        /**
         * Error class for server errors (B-017)
         */
        ServerError: ServerError,

        /**
         * Error class for network errors (B-017)
         */
        NetworkError: NetworkError,

        /**
         * Class for partial load results (B-017)
         */
        PartialLoadResult: PartialLoadResult,

        /**
         * Request timeout in milliseconds (B-017)
         */
        REQUEST_TIMEOUT_MS: REQUEST_TIMEOUT_MS
    };
})();

// ApiClient initialized
