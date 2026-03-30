/**
 * Localization Attributes - Dynamic HTML Attribute Localization
 *
 * Scans for data-loc-attr-* attributes and applies localized values
 * to actual HTML attributes (title, placeholder, aria-label, etc.)
 *
 * Usage:
 * <button data-loc-attr-title="Button_Save">Save</button>
 *
 * Will fetch localized value for "Button_Save" and apply to title attribute:
 * <button title="Save" data-loc-attr-title="Button_Save">Save</button>
 */

(function() {
    'use strict';

    // Check if Localization API is available
    if (!window.Localization) {
        Logger.warn('LocAttrs', 'window.Localization API not found. Attributes will not be localized.');
        return;
    }

    // Initialize on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    function init() {
        // Apply attribute localizations
        applyAttributeLocalizations();

        // In edit mode, make attributes editable
        if (isEditModeActive()) {
            initAttributeEditMode();
        }
    }

    /**
     * Check if edit mode is active
     */
    function isEditModeActive() {
        return getCookie('language_edit_mode') === 'true';
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
     * Apply localized values to HTML attributes
     */
    function applyAttributeLocalizations() {
        // Find all elements with data-loc-attr-* attributes
        const elements = document.querySelectorAll('[data-loc-attr-title], [data-loc-attr-placeholder], [data-loc-attr-aria-label], [data-loc-attr-aria-description]');

        if (elements.length === 0) {
            return;
        }

        // Collect all unique keys
        const keysToFetch = new Set();
        const elementAttributeMap = [];

        elements.forEach(el => {
            const mappings = [];

            // Check for each supported attribute type
            if (el.hasAttribute('data-loc-attr-title')) {
                const key = el.getAttribute('data-loc-attr-title');
                keysToFetch.add(key);
                mappings.push({ attr: 'title', key: key });
            }

            if (el.hasAttribute('data-loc-attr-placeholder')) {
                const key = el.getAttribute('data-loc-attr-placeholder');
                keysToFetch.add(key);
                mappings.push({ attr: 'placeholder', key: key });
            }

            if (el.hasAttribute('data-loc-attr-aria-label')) {
                const key = el.getAttribute('data-loc-attr-aria-label');
                keysToFetch.add(key);
                mappings.push({ attr: 'aria-label', key: key });
            }

            if (el.hasAttribute('data-loc-attr-aria-description')) {
                const key = el.getAttribute('data-loc-attr-aria-description');
                keysToFetch.add(key);
                mappings.push({ attr: 'aria-description', key: key });
            }

            if (mappings.length > 0) {
                elementAttributeMap.push({ element: el, mappings: mappings });
            }
        });

        // Fetch all localizations in batch
        const keysArray = Array.from(keysToFetch);
        window.Localization.getMany(keysArray).then(localizations => {
            // Apply localized values to elements
            elementAttributeMap.forEach(({ element, mappings }) => {
                mappings.forEach(({ attr, key }) => {
                    const value = localizations[key];
                    if (value) {
                        element.setAttribute(attr, value);
                    } else {
                        Logger.warn('LocAttrs', `No value found for key: ${key}`);
                    }
                });
            });

        }).catch(error => {
            Logger.error('LocAttrs', 'Failed to fetch localizations:', error);
        });
    }

    /**
     * Initialize attribute edit mode
     * Makes attributes editable when in edit mode
     */
    function initAttributeEditMode() {
        Logger.log('LocAttrs', 'Edit mode active - enabling attribute editing');

        // Add click handlers to elements with data-loc-attr-*
        const elements = document.querySelectorAll('[data-loc-attr-title], [data-loc-attr-placeholder], [data-loc-attr-aria-label], [data-loc-attr-aria-description]');

        elements.forEach(el => {
            // Add visual indicator on hover
            el.addEventListener('mouseenter', function() {
                // Check if this element already has a data-loc-key (avoid duplicate highlighting)
                if (!this.hasAttribute('data-loc-key')) {
                    this.style.outline = '2px dashed #3b82f6'; // Blue outline for attributes
                    this.style.outlineOffset = '2px';
                }
            });

            el.addEventListener('mouseleave', function() {
                if (!this.hasAttribute('data-loc-key')) {
                    this.style.outline = '';
                    this.style.outlineOffset = '';
                }
            });

            // Right-click to edit attributes
            el.addEventListener('contextmenu', function(e) {
                e.preventDefault();
                e.stopPropagation();
                openAttributeEditor(this);
            }, true);
        });

        Logger.log('LocAttrs', `Attached edit handlers to ${elements.length} elements`);
    }

    /**
     * Open attribute editor modal
     */
    function openAttributeEditor(element) {
        const attributes = [];

        // Collect all data-loc-attr-* attributes
        if (element.hasAttribute('data-loc-attr-title')) {
            attributes.push({
                name: 'title',
                key: element.getAttribute('data-loc-attr-title'),
                currentValue: element.getAttribute('title') || ''
            });
        }

        if (element.hasAttribute('data-loc-attr-placeholder')) {
            attributes.push({
                name: 'placeholder',
                key: element.getAttribute('data-loc-attr-placeholder'),
                currentValue: element.getAttribute('placeholder') || ''
            });
        }

        if (element.hasAttribute('data-loc-attr-aria-label')) {
            attributes.push({
                name: 'aria-label',
                key: element.getAttribute('data-loc-attr-aria-label'),
                currentValue: element.getAttribute('aria-label') || ''
            });
        }

        if (element.hasAttribute('data-loc-attr-aria-description')) {
            attributes.push({
                name: 'aria-description',
                key: element.getAttribute('data-loc-attr-aria-description'),
                currentValue: element.getAttribute('aria-description') || ''
            });
        }

        if (attributes.length === 0) {
            alert('No localizable attributes found on this element');
            return;
        }

        // Create modal
        const modal = document.createElement('div');
        modal.className = 'edit-mode-modal-overlay';

        let attributesHtml = '';
        attributes.forEach((attr, index) => {
            attributesHtml += `
                <div class="edit-mode-form-group">
                    <label>${escapeHtml(attr.name)} (${escapeHtml(attr.key)})</label>
                    <textarea id="attr-input-${index}" class="edit-mode-textarea" rows="2">${escapeHtml(attr.currentValue)}</textarea>
                </div>
            `;
        });

        modal.innerHTML = `
            <div class="edit-mode-modal">
                <div class="edit-mode-modal-header">
                    <h3>✏️ Edit Attribute Localization</h3>
                    <button class="edit-mode-modal-close" aria-label="Close">&times;</button>
                </div>
                <div class="edit-mode-modal-body">
                    <p style="margin-bottom: 1rem; color: var(--muted);">
                        Right-click an element to edit its HTML attribute translations (title, placeholder, aria-label).
                    </p>
                    ${attributesHtml}
                </div>
                <div class="edit-mode-modal-footer">
                    <button class="edit-mode-btn edit-mode-btn-cancel">Cancel</button>
                    <button class="edit-mode-btn edit-mode-btn-save">💾 Save Draft</button>
                </div>
            </div>
        `;

        document.body.appendChild(modal);

        // Handlers
        const closeModal = () => modal.remove();

        modal.querySelector('.edit-mode-modal-close').addEventListener('click', closeModal);
        modal.querySelector('.edit-mode-btn-cancel').addEventListener('click', closeModal);

        modal.querySelector('.edit-mode-btn-save').addEventListener('click', () => {
            // Save all attribute drafts
            attributes.forEach((attr, index) => {
                const input = document.getElementById(`attr-input-${index}`);
                const newValue = input.value.trim();

                if (newValue) {
                    // Direct application is the current behavior. Draft system integration
                    // will be added when draft workflow is implemented for attribute editing.
                    element.setAttribute(attr.name, newValue);

                    Logger.log('LocAttrs', `Updated ${attr.name}: ${attr.key} = ${newValue}`);
                }
            });

            alert(`Updated ${attributes.length} attribute(s). Use "Save & Exit" to commit changes.`);
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
     * Escape HTML to prevent XSS
     */
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML.replace(/'/g, '&#39;');
    }

})();
