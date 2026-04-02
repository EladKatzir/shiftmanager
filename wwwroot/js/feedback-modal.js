// wwwroot/js/feedback-modal.js
// Post-action acknowledgment modal for TempData success/error/warning/info messages.
// Uses a separate DOM element from confirm-modal.js to avoid state conflicts.

(function () {
  'use strict';

  var MODAL_ID = 'js-feedback-modal';

  // Localized strings by language
  var STRINGS = {
    en: {
      ok: 'OK',
      success: 'Success',
      error: 'Error',
      warning: 'Warning',
      info: 'Info'
    },
    he: {
      ok: '\u05D0\u05D9\u05E9\u05D5\u05E8',
      success: '\u05D4\u05E6\u05DC\u05D7\u05D4',
      error: '\u05E9\u05D2\u05D9\u05D0\u05D4',
      warning: '\u05D0\u05D6\u05D4\u05E8\u05D4',
      info: '\u05DE\u05D9\u05D3\u05E2'
    }
  };

  // Severity icons (same SVGs as toast-notifications.js, but larger)
  var ICONS = {
    success: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>',
    info: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>',
    warning: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>',
    error: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>'
  };

  /**
   * Get localized strings based on page language
   */
  function getStrings() {
    var lang = document.documentElement.lang || 'en';
    return lang.startsWith('he') ? STRINGS.he : STRINGS.en;
  }

  /**
   * Escape HTML to prevent XSS in message text
   */
  function escapeHtml(str) {
    if (typeof str !== 'string') return '';
    var div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
  }

  /**
   * Get or create the feedback modal DOM element
   */
  function getOrCreateModal() {
    var modal = document.getElementById(MODAL_ID);
    if (modal) return modal;

    modal = document.createElement('div');
    modal.id = MODAL_ID;
    modal.setAttribute('role', 'dialog');
    modal.setAttribute('aria-modal', 'true');
    modal.setAttribute('aria-labelledby', MODAL_ID + '-title');
    modal.setAttribute('aria-describedby', MODAL_ID + '-body');
    modal.className = 'modal modal--sm modal--feedback';
    modal.setAttribute('tabindex', '-1');

    modal.innerHTML =
      '<div class="modal__header">' +
        '<h2 class="modal__title" id="' + MODAL_ID + '-title"></h2>' +
      '</div>' +
      '<div class="modal__body" id="' + MODAL_ID + '-body"></div>' +
      '<div class="modal__footer">' +
        '<button class="btn btn-primary" data-action="ok"></button>' +
      '</div>';

    var backdrop = document.createElement('div');
    backdrop.className = 'modal-backdrop';
    backdrop.id = MODAL_ID + '-backdrop';

    document.body.appendChild(backdrop);
    document.body.appendChild(modal);
    return modal;
  }

  /**
   * Show the feedback modal
   * @param {string} type - 'success' | 'error' | 'warning' | 'info'
   * @param {string} message - The feedback message (already localized from server)
   */
  function show(type, message) {
    var validTypes = { success: 1, error: 1, warning: 1, info: 1 };
    if (!validTypes[type]) type = 'info';

    var strings = getStrings();
    var modal = getOrCreateModal();
    var backdrop = document.getElementById(MODAL_ID + '-backdrop');

    // Set title
    modal.querySelector('#' + MODAL_ID + '-title').textContent = strings[type] || strings.info;

    // Set body with icon + message
    var icon = ICONS[type] || ICONS.info;
    modal.querySelector('#' + MODAL_ID + '-body').innerHTML =
      '<div class="feedback-icon">' + icon + '</div>' +
      '<div class="feedback-message">' + escapeHtml(message) + '</div>';

    // Set OK button text
    var okBtn = modal.querySelector('[data-action="ok"]');
    okBtn.textContent = strings.ok;

    // Remove any previous type class and add current
    modal.className = 'modal modal--sm modal--feedback modal--feedback-' + type;

    // Show
    modal.classList.add('is-open');
    backdrop.classList.add('is-open');
    document.body.style.overflow = 'hidden';

    // Focus the OK button for accessibility
    okBtn.focus();

    // --- Event handlers ---

    function close() {
      modal.classList.remove('is-open');
      backdrop.classList.remove('is-open');
      document.body.style.overflow = '';
      cleanup();
    }

    function handleKey(e) {
      if (e.key === 'Escape') {
        close();
        return;
      }
      if (e.key === 'Enter') {
        close();
        return;
      }
      // Focus trap: Tab must stay within the modal (only one focusable element: OK button)
      if (e.key === 'Tab') {
        e.preventDefault();
        okBtn.focus();
      }
    }

    function handleBackdropClick(e) {
      if (e.target === backdrop) {
        close();
      }
    }

    function cleanup() {
      okBtn.removeEventListener('click', close);
      modal.removeEventListener('keydown', handleKey);
      backdrop.removeEventListener('click', handleBackdropClick);
    }

    okBtn.addEventListener('click', close);
    modal.addEventListener('keydown', handleKey);
    backdrop.addEventListener('click', handleBackdropClick);
  }

  // Expose globally
  window.FeedbackModal = { show: show };
})();
