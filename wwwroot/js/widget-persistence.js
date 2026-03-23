/**
 * ShiftManager Widget Persistence System
 * Version: 3.0 (UI Overhaul)
 *
 * Manages widget collapse/expand state persistence across page navigations.
 * Features:
 * - localStorage persistence with unique widget identifiers
 * - Smooth CSS-based animations respecting prefers-reduced-motion
 * - Full keyboard accessibility
 * - Support for nested widgets
 */

(function() {
  'use strict';

  const STORAGE_PREFIX = 'shifty_widget_';
  const COLLAPSED_CLASS = 'is-collapsed';
  const QUICK_INFO_SEEN_KEY = STORAGE_PREFIX + 'quickInfoSeen';
  const QUICK_INFO_WIDGET_ID = 'oncall';

  /**
   * Check if reduced motion is preferred
   */
  function prefersReducedMotion() {
    return window.matchMedia &&
           window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }

  /**
   * Get the storage key for a widget
   * @param {string} widgetId - The unique widget identifier
   * @returns {string} The localStorage key
   */
  function getStorageKey(widgetId) {
    return STORAGE_PREFIX + widgetId + '_collapsed';
  }

  /**
   * Get the collapsed state from localStorage
   * @param {string} widgetId - The unique widget identifier
   * @returns {boolean|null} True if collapsed, false if expanded, null if not set
   */
  function getCollapsedState(widgetId) {
    try {
      const key = getStorageKey(widgetId);
      const value = localStorage.getItem(key);
      if (value === null) return null;
      return value === 'true';
    } catch (e) {
      return null;
    }
  }

  /**
   * Save the collapsed state to localStorage
   * @param {string} widgetId - The unique widget identifier
   * @param {boolean} isCollapsed - Whether the widget is collapsed
   */
  function saveCollapsedState(widgetId, isCollapsed) {
    try {
      const key = getStorageKey(widgetId);
      localStorage.setItem(key, isCollapsed.toString());
    } catch (e) {
      // localStorage unavailable (private browsing, quota exceeded, etc.)
    }
  }

  /**
   * Toggle widget collapse state with animation
   * @param {HTMLElement} widget - The widget element
   * @param {HTMLElement} header - The widget header element
   * @param {boolean} [force] - Optional forced state (true = collapsed)
   */
  function toggleWidget(widget, header, force) {
    const widgetId = widget.dataset.widgetId;
    if (!widgetId) {
      console.warn('Widget missing data-widget-id attribute:', widget);
      return;
    }

    const shouldCollapse = force !== undefined
      ? force
      : !widget.classList.contains(COLLAPSED_CLASS);

    // Update class
    if (shouldCollapse) {
      widget.classList.add(COLLAPSED_CLASS);
    } else {
      widget.classList.remove(COLLAPSED_CLASS);
    }

    // Update ARIA
    header.setAttribute('aria-expanded', (!shouldCollapse).toString());

    // Persist state
    saveCollapsedState(widgetId, shouldCollapse);

    // Dispatch custom event for other components to listen to
    widget.dispatchEvent(new CustomEvent('widget:toggle', {
      bubbles: true,
      detail: { widgetId, isCollapsed: shouldCollapse }
    }));
  }

  /**
   * Initialize a single widget
   * @param {HTMLElement} widget - The widget element to initialize
   */
  function initWidget(widget) {
    const widgetId = widget.dataset.widgetId;
    if (!widgetId) {
      console.warn('Widget missing data-widget-id attribute:', widget);
      return;
    }

    // Find the direct header (not nested widget headers)
    const header = widget.querySelector(':scope > .widget__header');
    if (!header) {
      console.warn('Widget missing header:', widget);
      return;
    }

    // Restore state from localStorage (with slight delay to ensure DOM is ready)
    const savedState = getCollapsedState(widgetId);
    if (savedState !== null) {
      // User has an explicit preference — apply it
      toggleWidget(widget, header, savedState);
    } else if (widgetId === QUICK_INFO_WIDGET_ID) {
      // Quick Info widget auto-collapse: collapse after first view
      try {
        var seen = localStorage.getItem(QUICK_INFO_SEEN_KEY);
        if (seen) {
          // Not first visit — auto-collapse
          toggleWidget(widget, header, true);
        } else {
          // First visit — show expanded, mark as seen
          localStorage.setItem(QUICK_INFO_SEEN_KEY, 'true');
        }
      } catch (e) {
        // localStorage unavailable
      }
    }

    // Click handler
    header.addEventListener('click', function(e) {
      // Don't toggle if clicking on an interactive element inside the header
      if (e.target.closest('a, button:not(.widget__toggle), input, select')) {
        return;
      }

      // Stop propagation for nested widgets
      e.stopPropagation();

      toggleWidget(widget, header);
    });

    // Keyboard handler
    header.addEventListener('keydown', function(e) {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        e.stopPropagation();
        toggleWidget(widget, header);
      }
    });

    // Mark as initialized
    widget.dataset.widgetInitialized = 'true';
  }

  /**
   * Initialize all widgets on the page
   */
  function initAllWidgets() {
    const widgets = document.querySelectorAll('.widget[data-widget-id]:not([data-widget-initialized])');
    widgets.forEach(initWidget);
  }

  /**
   * Clear all widget persistence data
   */
  function clearAllWidgetStates() {
    const keys = [];
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i);
      if (key && key.startsWith(STORAGE_PREFIX)) {
        keys.push(key);
      }
    }
    keys.forEach(key => localStorage.removeItem(key));
  }

  /**
   * Get all stored widget states
   * @returns {Object} Map of widgetId to collapsed state
   */
  function getAllWidgetStates() {
    const states = {};
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i);
      if (key && key.startsWith(STORAGE_PREFIX) && key.endsWith('_collapsed')) {
        const widgetId = key.slice(STORAGE_PREFIX.length, -10); // Remove prefix and '_collapsed'
        states[widgetId] = localStorage.getItem(key) === 'true';
      }
    }
    return states;
  }

  // Initialize on DOM ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initAllWidgets);
  } else {
    initAllWidgets();
  }

  // Also reinitialize after AJAX content loads (for SPA-like behavior)
  document.addEventListener('htmx:afterSwap', initAllWidgets);
  document.addEventListener('turbo:load', initAllWidgets);

  // Expose API globally
  window.ShiftyWidgets = {
    init: initAllWidgets,
    initWidget: initWidget,
    toggle: toggleWidget,
    getState: getCollapsedState,
    saveState: saveCollapsedState,
    clearAll: clearAllWidgetStates,
    getAllStates: getAllWidgetStates,
    STORAGE_PREFIX: STORAGE_PREFIX
  };

})();
