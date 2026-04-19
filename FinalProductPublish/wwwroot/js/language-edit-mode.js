/**
 * Language Edit Mode - In-app translation editing
 * Allows Owners to click on localized text to edit translations.
 * Drafts are stored in sessionStorage and persisted across page navigation.
 */

(function() {
    'use strict';

    // Localizer helper — falls back to the key name if AppLocalizer hasn't loaded.
    // `fmt` supports a single {0} placeholder (which is all our keys need).
    const L = (k) => (window.AppLocalizer && window.AppLocalizer[k]) || k;
    const fmt = (tpl, v) => tpl.replace('{0}', v);

    // Only run if edit mode is active
    if (!isEditModeActive()) {
        return;
    }

    Logger.log('LangEdit', 'Active - Click on any text to edit');

    // Initialize on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    function init() {
        const editModeData = getEditModeData();
        if (!editModeData) {
            Logger.error('LangEdit', 'Invalid edit mode data');
            return;
        }

        // Apply existing drafts
        applyDrafts();

        // Attach click handlers to all localized elements
        attachClickHandlers();

        // Update draft count in banner
        updateDraftCount();

        // Attach banner button handlers
        attachBannerHandlers();

        // Initialize freeze interactions toggle
        initFreezeToggle();

    }

    /**
     * Initialize freeze interactions toggle
     */
    function initFreezeToggle() {
        const toggle = document.getElementById('freeze-interactions-toggle');
        if (!toggle) {
            return;
        }

        // Set initial state (checked by default)
        if (toggle.checked) {
            document.body.classList.add('interactions-frozen');
        }

        // Handle toggle changes
        toggle.addEventListener('change', function() {
            if (this.checked) {
                document.body.classList.add('interactions-frozen');
                Logger.log('LangEdit', 'Interactions frozen');
            } else {
                document.body.classList.remove('interactions-frozen');
                Logger.log('LangEdit', 'Interactions enabled');
            }
        });
    }

    /**
     * Check if edit mode is active via cookies
     */
    function isEditModeActive() {
        return getCookie('language_edit_mode') === 'true';
    }

    /**
     * Get edit mode data from cookies
     */
    function getEditModeData() {
        const companyId = getCookie('language_edit_companyId');
        const culture = getCookie('language_edit_culture');

        if (!companyId || !culture) {
            return null;
        }

        return {
            companyId: parseInt(companyId, 10),
            culture: culture
        };
    }

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

    /**
     * Get storage key for drafts
     */
    function getDraftsStorageKey() {
        const data = getEditModeData();
        return `language-override-drafts:${data.companyId}:${data.culture}`;
    }

    /**
     * Load drafts from sessionStorage
     */
    function loadDrafts() {
        const key = getDraftsStorageKey();
        const stored = sessionStorage.getItem(key);

        if (!stored) {
            return {};
        }

        try {
            const data = JSON.parse(stored);
            return data.drafts || {};
        } catch (e) {
            Logger.error('LangEdit', 'Failed to parse drafts:', e);
            return {};
        }
    }

    /**
     * Save drafts to sessionStorage
     */
    function saveDrafts(drafts) {
        const key = getDraftsStorageKey();
        const data = {
            version: Date.now(),
            drafts: drafts
        };

        sessionStorage.setItem(key, JSON.stringify(data));
    }

    /**
     * Apply saved drafts to DOM elements
     */
    function applyDrafts() {
        const drafts = loadDrafts();
        const draftKeys = Object.keys(drafts);

        if (draftKeys.length === 0) {
            return;
        }

        Logger.log('LangEdit', `Applying ${draftKeys.length} draft(s)`);

        draftKeys.forEach(key => {
            const elements = document.querySelectorAll(`[data-loc-key="${key}"]`);
            elements.forEach(el => {
                el.textContent = drafts[key];
                el.classList.add('has-draft');
                el.setAttribute('title', 'This text has been edited (draft)');
            });
        });
    }

    /**
     * Attach click handlers to all [data-loc-key] elements
     */
    function attachClickHandlers() {
        const elements = document.querySelectorAll('[data-loc-key]');

        elements.forEach(el => {
            // Add hover effect
            el.style.cursor = 'pointer';

            // Hover effect
            el.addEventListener('mouseenter', function() {
                this.style.outline = '2px solid #fbbf24';
                this.style.outlineOffset = '2px';
            });

            el.addEventListener('mouseleave', function() {
                this.style.outline = '';
                this.style.outlineOffset = '';
            });
        });

        // Global click handler using capture phase (fires BEFORE other handlers)
        // This intercepts clicks on buttons, links, etc. that contain [data-loc-key] elements
        document.addEventListener('click', function(e) {
            // Find the closest [data-loc-key] element (could be the target itself or a parent)
            let locElement = null;

            // Check if the clicked element or any parent has data-loc-key
            let currentElement = e.target;
            while (currentElement && currentElement !== document.body) {
                if (currentElement.hasAttribute && currentElement.hasAttribute('data-loc-key')) {
                    locElement = currentElement;
                    break;
                }
                currentElement = currentElement.parentElement;
            }

            // If we found a [data-loc-key] element, open the editor
            if (locElement) {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
                openEditor(locElement);
                return false;
            }
        }, true); // USE CAPTURE PHASE (true) - fires before bubbling phase

        Logger.log('LangEdit', `Attached handlers to ${elements.length} element(s)`);
    }

    /**
     * Open editor modal for a specific element
     */
    function openEditor(element) {
        const key = element.getAttribute('data-loc-key');
        const currentText = element.textContent;
        const drafts = loadDrafts();
        const draftValue = drafts[key] || currentText;

        // Create modal
        const modal = document.createElement('div');
        modal.className = 'edit-mode-modal-overlay';
        modal.innerHTML = `
            <div class="edit-mode-modal">
                <div class="edit-mode-modal-header">
                    <h3>✏️ Edit Translation</h3>
                    <button class="edit-mode-modal-close" aria-label="Close">&times;</button>
                </div>
                <div class="edit-mode-modal-body">
                    <div class="edit-mode-form-group">
                        <label>Resource Key (read-only)</label>
                        <input type="text" value="${escapeHtml(key)}" readonly class="edit-mode-input-readonly">
                    </div>
                    <div class="edit-mode-form-group">
                        <label>Current Value</label>
                        <textarea readonly class="edit-mode-textarea-readonly" rows="2">${escapeHtml(currentText)}</textarea>
                    </div>
                    <div class="edit-mode-form-group">
                        <label>Draft Value</label>
                        <textarea id="edit-mode-draft-input" class="edit-mode-textarea" rows="3" autofocus>${escapeHtml(draftValue)}</textarea>
                        <small>Edit the text above. Press ESC to cancel or click Save Draft.</small>
                    </div>
                </div>
                <div class="edit-mode-modal-footer">
                    <button class="edit-mode-btn edit-mode-btn-cancel">Cancel</button>
                    <button class="edit-mode-btn edit-mode-btn-save">💾 Save Draft</button>
                </div>
            </div>
        `;

        document.body.appendChild(modal);

        // Focus the input
        const input = modal.querySelector('#edit-mode-draft-input');
        input.focus();
        input.setSelectionRange(input.value.length, input.value.length);

        // Handlers
        const closeModal = () => modal.remove();

        modal.querySelector('.edit-mode-modal-close').addEventListener('click', closeModal);
        modal.querySelector('.edit-mode-btn-cancel').addEventListener('click', closeModal);

        modal.querySelector('.edit-mode-btn-save').addEventListener('click', () => {
            const newValue = input.value.trim();

            if (!newValue) {
                alert(L('LangEdit_EmptyValue'));
                return;
            }

            // Save draft
            const drafts = loadDrafts();
            drafts[key] = newValue;
            saveDrafts(drafts);

            // Update DOM element
            element.textContent = newValue;
            element.classList.add('has-draft');
            element.setAttribute('title', L('LangEdit_DraftTitle'));

            // Update draft count
            updateDraftCount();

            Logger.log('LangEdit', `Saved draft for key: ${key}`);

            closeModal();
        });

        // ESC to close
        modal.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                closeModal();
            }
        });

        // Click overlay to close
        modal.addEventListener('click', (e) => {
            if (e.target === modal) {
                closeModal();
            }
        });
    }

    /**
     * Update draft count in banner
     */
    function updateDraftCount() {
        const drafts = loadDrafts();
        const count = Object.keys(drafts).length;
        const countElement = document.getElementById('draft-count');

        if (countElement) {
            countElement.textContent = count;
        }
    }

    /**
     * Attach handlers to banner buttons
     */
    function attachBannerHandlers() {
        // View Drafts button
        const viewDraftsBtn = document.getElementById('view-drafts-btn');
        if (viewDraftsBtn) {
            viewDraftsBtn.addEventListener('click', viewDrafts);
        }

        // Save & Exit button
        const saveExitBtn = document.getElementById('save-exit-btn');
        if (saveExitBtn) {
            saveExitBtn.addEventListener('click', saveAndExit);
        }

        // Discard button
        const discardBtn = document.getElementById('discard-btn');
        if (discardBtn) {
            discardBtn.addEventListener('click', discardDrafts);
        }
    }

    /**
     * View all drafts (modal)
     */
    function viewDrafts() {
        const drafts = loadDrafts();
        const draftKeys = Object.keys(drafts);

        if (draftKeys.length === 0) {
            alert(L('LangEdit_NoDraftChanges'));
            return;
        }

        let draftListHtml = '';
        draftKeys.forEach(key => {
            draftListHtml += `
                <div class="draft-item">
                    <strong>${escapeHtml(key)}</strong>
                    <div>${escapeHtml(drafts[key])}</div>
                </div>
            `;
        });

        const modal = document.createElement('div');
        modal.className = 'edit-mode-modal-overlay';
        modal.innerHTML = `
            <div class="edit-mode-modal edit-mode-modal-wide">
                <div class="edit-mode-modal-header">
                    <h3>📋 ${escapeHtml(fmt(L('LangEdit_DraftChangesHeader'), draftKeys.length))}</h3>
                    <button class="edit-mode-modal-close" aria-label="${L('Close')}">&times;</button>
                </div>
                <div class="edit-mode-modal-body">
                    <div class="draft-list">
                        ${draftListHtml}
                    </div>
                </div>
                <div class="edit-mode-modal-footer">
                    <button class="edit-mode-btn edit-mode-btn-cancel">${L('Close')}</button>
                </div>
            </div>
        `;

        document.body.appendChild(modal);

        const closeModal = () => modal.remove();
        modal.querySelector('.edit-mode-modal-close').addEventListener('click', closeModal);
        modal.querySelector('.edit-mode-btn-cancel').addEventListener('click', closeModal);
        modal.addEventListener('click', (e) => {
            if (e.target === modal) closeModal();
        });
    }

    /**
     * Save all drafts and exit edit mode
     */
    async function saveAndExit() {
        const drafts = loadDrafts();
        const draftKeys = Object.keys(drafts);

        if (draftKeys.length === 0) {
            if (confirm(L('LangEdit_NoChangesToSave'))) {
                exitEditMode();
            }
            return;
        }

        const saveConfirmMsg = draftKeys.length === 1
            ? L('LangEdit_SaveOneConfirm')
            : fmt(L('LangEdit_SaveManyConfirm'), draftKeys.length);
        if (!confirm(saveConfirmMsg)) {
            return;
        }

        const editModeData = getEditModeData();

        try {
            const response = await fetch('/Owner/LanguageManagement?handler=ApiSaveDrafts', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                credentials: 'same-origin',
                body: JSON.stringify({
                    companyId: editModeData.companyId,
                    culture: editModeData.culture,
                    drafts: drafts
                })
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`Server error: ${response.status} - ${errorText}`);
            }

            const result = await response.json();

            if (result.success) {
                const successMsg = draftKeys.length === 1
                    ? L('LangEdit_SaveOneSuccess')
                    : fmt(L('LangEdit_SaveManySuccess'), draftKeys.length);
                alert(successMsg);
                clearDrafts();
                exitEditMode();
                window.location.href = '/Owner/LanguageManagement';
            } else {
                alert(fmt(L('LangEdit_SaveError'), result.message || L('LangEdit_SaveFallback')));
            }
        } catch (error) {
            Logger.error('LangEdit', 'Save failed:', error);
            alert(fmt(L('LangEdit_SaveFailed'), error.message));
        }
    }

    /**
     * Discard all drafts and exit edit mode
     */
    function discardDrafts() {
        const drafts = loadDrafts();
        const count = Object.keys(drafts).length;

        if (count === 0) {
            if (confirm(L('LangEdit_ExitConfirm'))) {
                exitEditModeAndRedirect();
            }
            return;
        }

        const discardMsg = count === 1
            ? L('LangEdit_DiscardOne')
            : fmt(L('LangEdit_DiscardMany'), count);
        if (!confirm(discardMsg)) {
            return;
        }

        clearDrafts();
        exitEditModeAndRedirect();
    }

    /**
     * Exit edit mode and redirect to clean page
     */
    function exitEditModeAndRedirect() {
        // Clear cookies
        document.cookie = 'language_edit_mode=; Max-Age=0; path=/; SameSite=Strict';
        document.cookie = 'language_edit_companyId=; Max-Age=0; path=/; SameSite=Strict';
        document.cookie = 'language_edit_culture=; Max-Age=0; path=/; SameSite=Strict';

        // Add a small delay to ensure cookies are cleared, then navigate
        setTimeout(() => {
            window.location.href = window.location.pathname + '?editModeExited=true';
        }, 100);
    }

    /**
     * Clear all drafts from sessionStorage
     */
    function clearDrafts() {
        const key = getDraftsStorageKey();
        sessionStorage.removeItem(key);
    }

    /**
     * Exit edit mode (clear cookies)
     */
    function exitEditMode() {
        document.cookie = 'language_edit_mode=; Max-Age=0; path=/';
        document.cookie = 'language_edit_companyId=; Max-Age=0; path=/';
        document.cookie = 'language_edit_culture=; Max-Age=0; path=/';
    }

    /**
     * Escape HTML to prevent XSS
     */
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML.replace(/'/g, '&#39;');
    }

})();
