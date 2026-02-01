/**
 * Partial Data Handler (B-017)
 *
 * Provides graceful handling when some data loads but not all:
 * - Section-level loading/error/success states
 * - Show available data, indicate what failed
 * - Retry mechanisms for failed sections
 * - User feedback for partial states
 * - Localized error messages
 *
 * Integrates with:
 * - error-states.js for toast notifications
 * - api-client.js for fetch operations
 * - calendar-skeleton.js for skeleton loading
 *
 * Usage:
 * // Create a section manager for a page
 * const manager = PartialData.createManager('calendar-page');
 *
 * // Register sections to track
 * manager.registerSection('shifts', {
 *   element: document.getElementById('shifts-container'),
 *   loadFn: () => ApiClient.get('/api/shifts'),
 *   onSuccess: (data) => renderShifts(data),
 *   onError: (error) => console.error(error)
 * });
 *
 * // Load all sections
 * await manager.loadAll();
 *
 * // Or load sections in parallel with partial success handling
 * const result = await manager.loadAllParallel();
 * if (result.isPartial) {
 *   // Some sections failed - UI already updated with inline errors
 * }
 */
(function() {
    'use strict';

    // Localization messages (fallbacks when server-side localization not available)
    const MESSAGES = {
        'en-US': {
            sectionLoading: 'Loading...',
            sectionError: 'Failed to load',
            sectionErrorDesc: 'This section could not be loaded. Please try again.',
            sectionRetry: 'Retry',
            sectionDismiss: 'Dismiss',
            sectionStale: 'Data may be outdated',
            sectionStaleRefresh: 'Refresh',
            partialSuccess: 'Some data could not be loaded',
            partialSuccessDesc: '{count} section(s) failed to load. Showing available data.',
            allFailed: 'Could not load data',
            allFailedDesc: 'All sections failed to load. Please check your connection and try again.',
            retryAll: 'Retry All',
            retryFailed: 'Retry Failed',
            lastUpdated: 'Last updated',
            justNow: 'just now',
            minutesAgo: '{n} minute(s) ago',
            hoursAgo: '{n} hour(s) ago'
        },
        'he-IL': {
            sectionLoading: 'טוען...',
            sectionError: 'הטעינה נכשלה',
            sectionErrorDesc: 'לא ניתן היה לטעון חלק זה. אנא נסה שוב.',
            sectionRetry: 'נסה שוב',
            sectionDismiss: 'סגור',
            sectionStale: 'הנתונים עשויים להיות לא מעודכנים',
            sectionStaleRefresh: 'רענן',
            partialSuccess: 'חלק מהנתונים לא נטענו',
            partialSuccessDesc: '{count} חלקים נכשלו בטעינה. מציג נתונים זמינים.',
            allFailed: 'לא ניתן לטעון נתונים',
            allFailedDesc: 'כל החלקים נכשלו בטעינה. אנא בדוק את החיבור שלך ונסה שוב.',
            retryAll: 'נסה הכל שוב',
            retryFailed: 'נסה שוב כשלונות',
            lastUpdated: 'עודכן לאחרונה',
            justNow: 'עכשיו',
            minutesAgo: 'לפני {n} דקות',
            hoursAgo: 'לפני {n} שעות'
        }
    };

    /**
     * Get current culture from HTML lang attribute
     */
    function getCurrentCulture() {
        const htmlLang = document.documentElement.lang || 'en-US';
        return htmlLang.startsWith('he') ? 'he-IL' : 'en-US';
    }

    /**
     * Get localized message
     */
    function getMessage(key, params = {}) {
        const culture = getCurrentCulture();
        let message = MESSAGES[culture]?.[key] || MESSAGES['en-US'][key] || key;

        // Replace placeholders
        Object.keys(params).forEach(param => {
            message = message.replace(`{${param}}`, params[param]);
        });

        return message;
    }

    /**
     * Escape HTML to prevent XSS
     */
    function escapeHtml(str) {
        if (typeof str !== 'string') return str;
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    /**
     * Check if user prefers reduced motion
     */
    function prefersReducedMotion() {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    /**
     * Section state enum
     */
    const SectionState = {
        IDLE: 'idle',
        LOADING: 'loading',
        SUCCESS: 'success',
        ERROR: 'error',
        STALE: 'stale'
    };

    /**
     * Create an inline error state element for a section
     * @param {Object} options - Options for the error state
     * @param {string} options.title - Error title
     * @param {string} options.message - Error description
     * @param {Function} options.onRetry - Retry callback
     * @param {Function} [options.onDismiss] - Dismiss callback
     * @param {boolean} [options.showDismiss=false] - Whether to show dismiss button
     * @returns {HTMLElement} The error state element
     */
    function createSectionError(options) {
        const {
            title = getMessage('sectionError'),
            message = getMessage('sectionErrorDesc'),
            onRetry,
            onDismiss,
            showDismiss = false
        } = options;

        const culture = getCurrentCulture();
        const isRtl = culture === 'he-IL';

        const errorEl = document.createElement('div');
        errorEl.className = 'section-error';
        errorEl.setAttribute('role', 'alert');
        errorEl.setAttribute('aria-live', 'polite');
        errorEl.dir = isRtl ? 'rtl' : 'ltr';

        errorEl.innerHTML = `
            <div class="section-error__icon" aria-hidden="true">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="10"></circle>
                    <line x1="12" y1="8" x2="12" y2="12"></line>
                    <line x1="12" y1="16" x2="12.01" y2="16"></line>
                </svg>
            </div>
            <div class="section-error__content">
                <div class="section-error__title">${escapeHtml(title)}</div>
                <div class="section-error__message">${escapeHtml(message)}</div>
            </div>
            <div class="section-error__actions">
                ${onRetry ? `
                    <button type="button" class="section-error__btn section-error__btn--retry">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                            <polyline points="23 4 23 10 17 10"></polyline>
                            <path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"></path>
                        </svg>
                        ${escapeHtml(getMessage('sectionRetry'))}
                    </button>
                ` : ''}
                ${showDismiss && onDismiss ? `
                    <button type="button" class="section-error__btn section-error__btn--dismiss">
                        ${escapeHtml(getMessage('sectionDismiss'))}
                    </button>
                ` : ''}
            </div>
        `;

        // Event handlers
        const retryBtn = errorEl.querySelector('.section-error__btn--retry');
        if (retryBtn && onRetry) {
            retryBtn.addEventListener('click', () => {
                onRetry();
            });
        }

        const dismissBtn = errorEl.querySelector('.section-error__btn--dismiss');
        if (dismissBtn && onDismiss) {
            dismissBtn.addEventListener('click', () => {
                onDismiss();
                errorEl.remove();
            });
        }

        return errorEl;
    }

    /**
     * Create a stale data indicator element
     * @param {Object} options - Options for the stale indicator
     * @param {Date} options.lastUpdated - When data was last updated
     * @param {Function} options.onRefresh - Refresh callback
     * @returns {HTMLElement} The stale indicator element
     */
    function createStaleIndicator(options) {
        const {
            lastUpdated,
            onRefresh
        } = options;

        const culture = getCurrentCulture();
        const isRtl = culture === 'he-IL';

        const staleEl = document.createElement('div');
        staleEl.className = 'section-stale';
        staleEl.setAttribute('role', 'status');
        staleEl.dir = isRtl ? 'rtl' : 'ltr';

        const timeAgo = formatTimeAgo(lastUpdated);

        staleEl.innerHTML = `
            <div class="section-stale__icon" aria-hidden="true">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="10"></circle>
                    <polyline points="12 6 12 12 16 14"></polyline>
                </svg>
            </div>
            <span class="section-stale__text">
                ${escapeHtml(getMessage('sectionStale'))}
                <span class="section-stale__time">(${escapeHtml(getMessage('lastUpdated'))}: ${escapeHtml(timeAgo)})</span>
            </span>
            ${onRefresh ? `
                <button type="button" class="section-stale__btn">
                    ${escapeHtml(getMessage('sectionStaleRefresh'))}
                </button>
            ` : ''}
        `;

        const refreshBtn = staleEl.querySelector('.section-stale__btn');
        if (refreshBtn && onRefresh) {
            refreshBtn.addEventListener('click', () => {
                onRefresh();
            });
        }

        return staleEl;
    }

    /**
     * Format a timestamp as a relative time string
     * @param {Date} date - The date to format
     * @returns {string} Formatted relative time
     */
    function formatTimeAgo(date) {
        if (!date) return getMessage('justNow');

        const now = new Date();
        const diffMs = now - date;
        const diffMinutes = Math.floor(diffMs / 60000);
        const diffHours = Math.floor(diffMs / 3600000);

        if (diffMinutes < 1) {
            return getMessage('justNow');
        } else if (diffMinutes < 60) {
            return getMessage('minutesAgo', { n: diffMinutes });
        } else {
            return getMessage('hoursAgo', { n: diffHours });
        }
    }

    /**
     * Create a loading skeleton for a section
     * @param {Object} options - Options for the skeleton
     * @param {string} [options.type='generic'] - Type of skeleton ('generic', 'list', 'card', 'table')
     * @param {number} [options.rows=3] - Number of skeleton rows
     * @returns {HTMLElement} The skeleton element
     */
    function createSectionSkeleton(options = {}) {
        const {
            type = 'generic',
            rows = 3
        } = options;

        const skeleton = document.createElement('div');
        skeleton.className = 'section-skeleton';
        skeleton.setAttribute('aria-busy', 'true');
        skeleton.setAttribute('aria-label', getMessage('sectionLoading'));

        let content = '';

        switch (type) {
            case 'list':
                for (let i = 0; i < rows; i++) {
                    content += `
                        <div class="section-skeleton__row">
                            <div class="section-skeleton__avatar skeleton"></div>
                            <div class="section-skeleton__text-group">
                                <div class="section-skeleton__text skeleton" style="width: ${60 + Math.random() * 30}%"></div>
                                <div class="section-skeleton__text-sm skeleton" style="width: ${40 + Math.random() * 20}%"></div>
                            </div>
                        </div>
                    `;
                }
                break;

            case 'card':
                for (let i = 0; i < rows; i++) {
                    content += `
                        <div class="section-skeleton__card skeleton"></div>
                    `;
                }
                break;

            case 'table':
                content = `
                    <div class="section-skeleton__table">
                        <div class="section-skeleton__table-header">
                            ${Array(4).fill('<div class="section-skeleton__cell skeleton"></div>').join('')}
                        </div>
                        ${Array(rows).fill(`
                            <div class="section-skeleton__table-row">
                                ${Array(4).fill('<div class="section-skeleton__cell skeleton"></div>').join('')}
                            </div>
                        `).join('')}
                    </div>
                `;
                break;

            default:
                for (let i = 0; i < rows; i++) {
                    content += `
                        <div class="section-skeleton__line skeleton" style="width: ${70 + Math.random() * 25}%"></div>
                    `;
                }
        }

        skeleton.innerHTML = content;
        return skeleton;
    }

    /**
     * Section class - manages state for a single data section
     */
    class Section {
        constructor(id, options) {
            this.id = id;
            this.element = options.element;
            this.loadFn = options.loadFn;
            this.onSuccess = options.onSuccess;
            this.onError = options.onError;
            this.skeletonType = options.skeletonType || 'generic';
            this.skeletonRows = options.skeletonRows || 3;
            this.staleAfterMs = options.staleAfterMs || 300000; // 5 minutes default
            this.retryCount = 0;
            this.maxRetries = options.maxRetries || 3;

            this.state = SectionState.IDLE;
            this.data = null;
            this.error = null;
            this.lastUpdated = null;

            this._originalContent = null;
            this._skeletonEl = null;
            this._errorEl = null;
            this._staleEl = null;
        }

        /**
         * Set the section state and update UI
         */
        setState(state, options = {}) {
            this.state = state;

            switch (state) {
                case SectionState.LOADING:
                    this._showLoading();
                    break;
                case SectionState.SUCCESS:
                    this._showSuccess(options.data);
                    break;
                case SectionState.ERROR:
                    this._showError(options.error);
                    break;
                case SectionState.STALE:
                    this._showStale();
                    break;
                case SectionState.IDLE:
                    this._restoreOriginal();
                    break;
            }
        }

        /**
         * Load data for this section
         */
        async load() {
            if (this.state === SectionState.LOADING) {
                return; // Already loading
            }

            this.setState(SectionState.LOADING);

            try {
                const response = await this.loadFn();

                // Handle Response objects (from ApiClient)
                let data;
                if (response instanceof Response) {
                    if (!response.ok) {
                        throw new Error(`HTTP ${response.status}`);
                    }
                    data = await response.json();
                } else {
                    data = response;
                }

                this.data = data;
                this.error = null;
                this.lastUpdated = new Date();
                this.retryCount = 0;
                this.setState(SectionState.SUCCESS, { data });

                if (this.onSuccess) {
                    this.onSuccess(data);
                }

                return { success: true, data };
            } catch (error) {
                this.error = error;
                this.retryCount++;
                this.setState(SectionState.ERROR, { error });

                if (this.onError) {
                    this.onError(error);
                }

                return { success: false, error };
            }
        }

        /**
         * Retry loading this section
         */
        async retry() {
            if (this.retryCount >= this.maxRetries) {
                console.warn(`[PartialData] Section ${this.id} exceeded max retries`);
                // Still try, but warn
            }
            return this.load();
        }

        /**
         * Check if section data is stale
         */
        isStale() {
            if (!this.lastUpdated) return false;
            return (Date.now() - this.lastUpdated.getTime()) > this.staleAfterMs;
        }

        /**
         * Mark section as stale and show indicator
         */
        markStale() {
            if (this.state === SectionState.SUCCESS) {
                this.setState(SectionState.STALE);
            }
        }

        /**
         * Show loading skeleton
         */
        _showLoading() {
            if (!this.element) return;

            // Save original content if not already saved
            if (!this._originalContent) {
                this._originalContent = this.element.innerHTML;
            }

            // Remove any existing error/stale indicators
            this._removeErrorEl();
            this._removeStaleEl();

            // Create and show skeleton
            this._skeletonEl = createSectionSkeleton({
                type: this.skeletonType,
                rows: this.skeletonRows
            });

            this.element.innerHTML = '';
            this.element.appendChild(this._skeletonEl);
            this.element.classList.add('section--loading');
            this.element.classList.remove('section--error', 'section--stale', 'section--success');
        }

        /**
         * Show success state (restore content or use callback result)
         */
        _showSuccess(data) {
            if (!this.element) return;

            // Remove skeleton
            this._removeSkeletonEl();
            this._removeErrorEl();
            this._removeStaleEl();

            // The onSuccess callback should handle rendering
            // If no callback, restore original content
            if (!this.onSuccess && this._originalContent) {
                this.element.innerHTML = this._originalContent;
            }

            this.element.classList.remove('section--loading', 'section--error', 'section--stale');
            this.element.classList.add('section--success');

            // Animate in with transition (respects reduced motion)
            if (!prefersReducedMotion()) {
                this.element.style.opacity = '0';
                requestAnimationFrame(() => {
                    this.element.style.transition = 'opacity 200ms ease-out';
                    this.element.style.opacity = '1';
                    setTimeout(() => {
                        this.element.style.transition = '';
                    }, 200);
                });
            }
        }

        /**
         * Show error state
         */
        _showError(error) {
            if (!this.element) return;

            // Remove skeleton
            this._removeSkeletonEl();

            // Create error element
            this._errorEl = createSectionError({
                title: getMessage('sectionError'),
                message: error?.message || getMessage('sectionErrorDesc'),
                onRetry: () => this.retry(),
                showDismiss: false
            });

            // Replace content with error
            this.element.innerHTML = '';
            this.element.appendChild(this._errorEl);
            this.element.classList.remove('section--loading', 'section--stale', 'section--success');
            this.element.classList.add('section--error');
        }

        /**
         * Show stale data indicator
         */
        _showStale() {
            if (!this.element) return;

            // Don't show stale if already showing error
            if (this.element.classList.contains('section--error')) return;

            // Remove existing stale indicator
            this._removeStaleEl();

            // Create stale indicator
            this._staleEl = createStaleIndicator({
                lastUpdated: this.lastUpdated,
                onRefresh: () => this.load()
            });

            // Insert at top of element
            this.element.insertBefore(this._staleEl, this.element.firstChild);
            this.element.classList.add('section--stale');
        }

        /**
         * Restore original content
         */
        _restoreOriginal() {
            if (!this.element) return;

            this._removeSkeletonEl();
            this._removeErrorEl();
            this._removeStaleEl();

            if (this._originalContent) {
                this.element.innerHTML = this._originalContent;
            }

            this.element.classList.remove('section--loading', 'section--error', 'section--stale', 'section--success');
        }

        _removeSkeletonEl() {
            if (this._skeletonEl && this._skeletonEl.parentNode) {
                this._skeletonEl.remove();
                this._skeletonEl = null;
            }
        }

        _removeErrorEl() {
            if (this._errorEl && this._errorEl.parentNode) {
                this._errorEl.remove();
                this._errorEl = null;
            }
        }

        _removeStaleEl() {
            if (this._staleEl && this._staleEl.parentNode) {
                this._staleEl.remove();
                this._staleEl = null;
            }
        }
    }

    /**
     * SectionManager class - manages multiple sections on a page
     */
    class SectionManager {
        constructor(pageId) {
            this.pageId = pageId;
            this.sections = new Map();
            this._staleCheckInterval = null;
        }

        /**
         * Register a section to track
         * @param {string} id - Unique section ID
         * @param {Object} options - Section options
         */
        registerSection(id, options) {
            const section = new Section(id, options);
            this.sections.set(id, section);
            return section;
        }

        /**
         * Get a section by ID
         */
        getSection(id) {
            return this.sections.get(id);
        }

        /**
         * Load a single section
         */
        async loadSection(id) {
            const section = this.sections.get(id);
            if (!section) {
                console.warn(`[PartialData] Section ${id} not found`);
                return { success: false, error: new Error('Section not found') };
            }
            return section.load();
        }

        /**
         * Load all sections sequentially
         */
        async loadAll() {
            const results = [];
            for (const [id, section] of this.sections) {
                const result = await section.load();
                results.push({ id, ...result });
            }
            return this._createLoadResult(results);
        }

        /**
         * Load all sections in parallel (preferred for partial load handling)
         */
        async loadAllParallel() {
            const loadPromises = Array.from(this.sections.entries()).map(
                async ([id, section]) => {
                    const result = await section.load();
                    return { id, ...result };
                }
            );

            const results = await Promise.all(loadPromises);
            const loadResult = this._createLoadResult(results);

            // Show toast for partial/full failures
            if (loadResult.isEmpty) {
                this._showAllFailedToast();
            } else if (loadResult.isPartial) {
                this._showPartialSuccessToast(loadResult.failedCount);
            }

            return loadResult;
        }

        /**
         * Retry failed sections only
         */
        async retryFailed() {
            const failedSections = Array.from(this.sections.values())
                .filter(s => s.state === SectionState.ERROR);

            const retryPromises = failedSections.map(async section => {
                const result = await section.retry();
                return { id: section.id, ...result };
            });

            const results = await Promise.all(retryPromises);
            return this._createLoadResult(results);
        }

        /**
         * Retry all sections
         */
        async retryAll() {
            return this.loadAllParallel();
        }

        /**
         * Refresh stale sections
         */
        async refreshStale() {
            const staleSections = Array.from(this.sections.values())
                .filter(s => s.isStale());

            const refreshPromises = staleSections.map(async section => {
                const result = await section.load();
                return { id: section.id, ...result };
            });

            const results = await Promise.all(refreshPromises);
            return this._createLoadResult(results);
        }

        /**
         * Start periodic stale checking
         * @param {number} intervalMs - Check interval in milliseconds
         */
        startStaleCheck(intervalMs = 60000) {
            this.stopStaleCheck();
            this._staleCheckInterval = setInterval(() => {
                for (const section of this.sections.values()) {
                    if (section.isStale() && section.state === SectionState.SUCCESS) {
                        section.markStale();
                    }
                }
            }, intervalMs);
        }

        /**
         * Stop periodic stale checking
         */
        stopStaleCheck() {
            if (this._staleCheckInterval) {
                clearInterval(this._staleCheckInterval);
                this._staleCheckInterval = null;
            }
        }

        /**
         * Get current state summary
         */
        getStateSummary() {
            const summary = {
                total: this.sections.size,
                loading: 0,
                success: 0,
                error: 0,
                stale: 0,
                idle: 0
            };

            for (const section of this.sections.values()) {
                summary[section.state]++;
            }

            return summary;
        }

        /**
         * Create a standardized load result object
         */
        _createLoadResult(results) {
            const successResults = results.filter(r => r.success);
            const failedResults = results.filter(r => !r.success);

            return {
                results,
                successCount: successResults.length,
                failedCount: failedResults.length,
                totalCount: results.length,
                isPartial: failedResults.length > 0 && successResults.length > 0,
                isComplete: failedResults.length === 0,
                isEmpty: successResults.length === 0 && failedResults.length > 0,
                failedSections: failedResults.map(r => r.id),
                successSections: successResults.map(r => r.id)
            };
        }

        /**
         * Show toast for partial success
         */
        _showPartialSuccessToast(failedCount) {
            if (window.ErrorStates) {
                window.ErrorStates.showWarning(
                    getMessage('partialSuccessDesc', { count: failedCount }),
                    getMessage('partialSuccess'),
                    {
                        duration: 8000,
                        onRetry: () => this.retryFailed()
                    }
                );
            }
        }

        /**
         * Show toast for all sections failed
         */
        _showAllFailedToast() {
            if (window.ErrorStates) {
                window.ErrorStates.showError(
                    getMessage('allFailedDesc'),
                    getMessage('allFailed'),
                    {
                        duration: 0, // Don't auto-dismiss
                        onRetry: () => this.retryAll()
                    }
                );
            }
        }

        /**
         * Cleanup - call when page is unmounted
         */
        destroy() {
            this.stopStaleCheck();
            this.sections.clear();
        }
    }

    /**
     * Inject CSS styles for partial data components
     */
    function injectStyles() {
        if (document.getElementById('partial-data-styles')) {
            return;
        }

        const style = document.createElement('style');
        style.id = 'partial-data-styles';
        style.textContent = `
            /* ========================================
               Partial Data Styles (B-017)
               ======================================== */

            /* Section States */
            .section--loading {
                position: relative;
                min-height: 100px;
            }

            .section--error,
            .section--stale {
                position: relative;
            }

            .section--success {
                position: relative;
            }

            /* Section Error Component */
            .section-error {
                display: flex;
                flex-direction: column;
                align-items: center;
                justify-content: center;
                padding: var(--space-6, 1.5rem);
                background: var(--surface-soft, #f8fafc);
                border: 1px dashed var(--border, #e2e8f0);
                border-radius: var(--radius-lg, 0.75rem);
                text-align: center;
                min-height: 120px;
                gap: var(--space-3, 0.75rem);
            }

            .section-error__icon {
                color: var(--warning, #D4A017);
            }

            .section-error__content {
                display: flex;
                flex-direction: column;
                gap: var(--space-1, 0.25rem);
            }

            .section-error__title {
                font-weight: var(--font-semibold, 600);
                color: var(--text, #1a1f2b);
                font-size: var(--text-body, 1rem);
            }

            .section-error__message {
                font-size: var(--text-small, 0.875rem);
                color: var(--text-muted, #64748b);
                max-width: 300px;
            }

            .section-error__actions {
                display: flex;
                gap: var(--space-2, 0.5rem);
                margin-top: var(--space-2, 0.5rem);
            }

            .section-error__btn {
                display: inline-flex;
                align-items: center;
                gap: var(--space-2, 0.5rem);
                padding: var(--space-2, 0.5rem) var(--space-4, 1rem);
                border-radius: var(--radius-md, 0.5rem);
                font-size: var(--text-small, 0.875rem);
                font-weight: var(--font-medium, 500);
                cursor: pointer;
                transition: all 0.15s ease;
            }

            .section-error__btn--retry {
                background: var(--primary, #1E3A5F);
                color: var(--primary-contrast, #fff);
                border: none;
            }

            .section-error__btn--retry:hover {
                background: var(--primary-hover, #2D4A6F);
            }

            .section-error__btn--retry:focus-visible {
                outline: 2px solid var(--primary, #1E3A5F);
                outline-offset: 2px;
            }

            .section-error__btn--dismiss {
                background: transparent;
                color: var(--text-muted, #64748b);
                border: 1px solid var(--border, #e2e8f0);
            }

            .section-error__btn--dismiss:hover {
                background: var(--surface, #fff);
                border-color: var(--border-strong, #cbd5e1);
            }

            /* Section Stale Indicator */
            .section-stale {
                display: flex;
                align-items: center;
                gap: var(--space-2, 0.5rem);
                padding: var(--space-2, 0.5rem) var(--space-3, 0.75rem);
                background: var(--warning-soft, #fef3c7);
                border-radius: var(--radius-md, 0.5rem);
                font-size: var(--text-small, 0.875rem);
                margin-bottom: var(--space-3, 0.75rem);
            }

            .section-stale__icon {
                color: var(--warning, #D4A017);
                flex-shrink: 0;
            }

            .section-stale__text {
                color: var(--text, #1a1f2b);
                flex: 1;
            }

            .section-stale__time {
                color: var(--text-muted, #64748b);
                font-size: var(--text-tiny, 0.75rem);
            }

            .section-stale__btn {
                padding: var(--space-1, 0.25rem) var(--space-3, 0.75rem);
                background: transparent;
                color: var(--primary, #1E3A5F);
                border: 1px solid var(--primary, #1E3A5F);
                border-radius: var(--radius-sm, 0.25rem);
                font-size: var(--text-tiny, 0.75rem);
                font-weight: var(--font-medium, 500);
                cursor: pointer;
                transition: all 0.15s ease;
            }

            .section-stale__btn:hover {
                background: var(--primary-soft, rgba(30, 58, 95, 0.1));
            }

            /* Section Skeleton */
            .section-skeleton {
                display: flex;
                flex-direction: column;
                gap: var(--space-3, 0.75rem);
                padding: var(--space-4, 1rem);
            }

            .section-skeleton__line {
                height: 1rem;
                border-radius: var(--radius-sm, 0.25rem);
            }

            .section-skeleton__row {
                display: flex;
                align-items: center;
                gap: var(--space-3, 0.75rem);
            }

            .section-skeleton__avatar {
                width: 40px;
                height: 40px;
                border-radius: var(--radius-full, 50%);
                flex-shrink: 0;
            }

            .section-skeleton__text-group {
                flex: 1;
                display: flex;
                flex-direction: column;
                gap: var(--space-2, 0.5rem);
            }

            .section-skeleton__text {
                height: 1rem;
            }

            .section-skeleton__text-sm {
                height: 0.75rem;
            }

            .section-skeleton__card {
                height: 120px;
                border-radius: var(--radius-lg, 0.75rem);
            }

            .section-skeleton__table {
                display: flex;
                flex-direction: column;
                gap: 1px;
                background: var(--border, #e2e8f0);
                border-radius: var(--radius-md, 0.5rem);
                overflow: hidden;
            }

            .section-skeleton__table-header,
            .section-skeleton__table-row {
                display: flex;
                background: var(--surface, #fff);
            }

            .section-skeleton__table-header {
                background: var(--surface-soft, #f8fafc);
            }

            .section-skeleton__cell {
                flex: 1;
                height: 1rem;
                margin: var(--space-3, 0.75rem);
            }

            /* Skeleton shimmer animation */
            .section-skeleton .skeleton {
                background: linear-gradient(
                    90deg,
                    var(--surface-soft, #f8fafc) 25%,
                    var(--surface, #fff) 50%,
                    var(--surface-soft, #f8fafc) 75%
                );
                background-size: 200% 100%;
                animation: section-skeleton-shimmer 1.5s ease-in-out infinite;
            }

            @keyframes section-skeleton-shimmer {
                0% { background-position: 200% 0; }
                100% { background-position: -200% 0; }
            }

            /* Reduced motion */
            @media (prefers-reduced-motion: reduce) {
                .section-skeleton .skeleton {
                    animation: none;
                    background: var(--surface-soft, #f8fafc);
                }
            }

            /* RTL Support */
            [dir="rtl"] .section-error__actions {
                flex-direction: row-reverse;
            }

            [dir="rtl"] .section-stale {
                flex-direction: row-reverse;
            }

            [dir="rtl"] .section-error__btn svg {
                order: 1;
            }

            /* Dark Mode */
            :root[data-theme="dark"] .section-error {
                background: var(--surface-soft, #1e293b);
                border-color: var(--border, #334155);
            }

            :root[data-theme="dark"] .section-stale {
                background: var(--warning-soft, rgba(212, 160, 23, 0.15));
            }

            :root[data-theme="dark"] .section-skeleton .skeleton {
                background: linear-gradient(
                    90deg,
                    var(--surface-soft, #1e293b) 25%,
                    var(--surface-elevated, #334155) 50%,
                    var(--surface-soft, #1e293b) 75%
                );
                background-size: 200% 100%;
            }

            /* High contrast mode */
            @media (forced-colors: active) {
                .section-error {
                    border-style: solid;
                    border-width: 2px;
                }

                .section-stale {
                    border: 1px solid CanvasText;
                }

                .section-skeleton .skeleton {
                    background: Canvas;
                    border: 1px solid CanvasText;
                }
            }
        `;

        document.head.appendChild(style);
    }

    // Active managers registry
    const managers = new Map();

    /**
     * Create a section manager for a page
     * @param {string} pageId - Unique page identifier
     * @returns {SectionManager} The manager instance
     */
    function createManager(pageId) {
        // Clean up existing manager if any
        if (managers.has(pageId)) {
            managers.get(pageId).destroy();
        }

        const manager = new SectionManager(pageId);
        managers.set(pageId, manager);
        return manager;
    }

    /**
     * Get an existing manager
     * @param {string} pageId - Page identifier
     * @returns {SectionManager|undefined} The manager instance
     */
    function getManager(pageId) {
        return managers.get(pageId);
    }

    /**
     * Destroy a manager
     * @param {string} pageId - Page identifier
     */
    function destroyManager(pageId) {
        const manager = managers.get(pageId);
        if (manager) {
            manager.destroy();
            managers.delete(pageId);
        }
    }

    /**
     * Initialize the partial data system
     */
    function initialize() {
        injectStyles();

        // Listen for api:retryfailed events from error-states.js
        window.addEventListener('api:retryfailed', (event) => {
            const { failedRequests } = event.detail;
            console.log('[PartialData] Received retry request for:', failedRequests);
            // Managers will handle their own retries through their registered sections
        });

        console.log('[PartialData] Initialized (B-017)');
    }

    // Public API
    window.PartialData = {
        // Manager lifecycle
        createManager,
        getManager,
        destroyManager,

        // Standalone component creation
        createSectionError,
        createSectionSkeleton,
        createStaleIndicator,

        // Utilities
        getMessage,
        formatTimeAgo,

        // State enum
        SectionState,

        // Classes for advanced usage
        Section,
        SectionManager
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }

})();
