// wwwroot/js/feedback-modal.js
// Post-action acknowledgment modal for TempData success/error/warning/info messages.
// Uses a separate DOM element from confirm-modal.js to avoid state conflicts.
//
// API:
//   FeedbackModal.show(type, message)
//   FeedbackModal.show(type, message, { detail, errorId, retry })
//
// Options:
//   detail   string   Collapsible secondary text (e.g. validation breakdown, dev-only stack).
//   errorId  string   Correlation/request ID. Rendered monospace with a copy-to-clipboard button.
//   retry    function Optional retry callback. If provided, a "Retry" secondary button appears.

(function () {
  'use strict';

  var MODAL_ID = 'js-feedback-modal';

  // Localized strings by language. Mirrors the Feedback_* keys in SharedResources.resx /
  // SharedResources.he-IL.resx so Razor pages can render the same labels server-side.
  var STRINGS = {
    en: {
      ok: 'OK',
      success: 'Success',
      error: 'Error',
      warning: 'Warning',
      info: 'Info',
      showDetails: 'Show details',
      hideDetails: 'Hide details',
      errorId: 'Error ID',
      copy: 'Copy',
      copied: 'Copied',
      retry: 'Retry'
    },
    he: {
      ok: 'אישור',
      success: 'הצלחה',
      error: 'שגיאה',
      warning: 'אזהרה',
      info: 'מידע',
      showDetails: 'הצג פרטים',
      hideDetails: 'הסתר פרטים',
      errorId: 'מזהה שגיאה',
      copy: 'העתק',
      copied: 'הועתק',
      retry: 'נסה שוב'
    }
  };

  // Severity icons (same SVGs as toast-notifications.js, but larger)
  var ICONS = {
    success: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>',
    info: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>',
    warning: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>',
    error: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>'
  };

  function getStrings() {
    var lang = document.documentElement.lang || 'en';
    return lang.startsWith('he') ? STRINGS.he : STRINGS.en;
  }

  function escapeHtml(str) {
    if (typeof str !== 'string') return '';
    var div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
  }

  // Clipboard write with a same-document <textarea> + execCommand fallback for browsers
  // without a secure context (air-gapped IIS sites may serve over HTTP).
  function copyToClipboard(text, sourceBtn, strings) {
    var notify = function (success) {
      if (!success || !sourceBtn) return;
      var orig = sourceBtn.textContent;
      sourceBtn.textContent = strings.copied;
      sourceBtn.disabled = true;
      setTimeout(function () {
        sourceBtn.textContent = orig;
        sourceBtn.disabled = false;
      }, 1500);
    };

    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(
        function () { notify(true); },
        function () { notify(false); }
      );
      return;
    }

    var ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    ta.style.left = '-9999px';
    document.body.appendChild(ta);
    ta.select();
    var ok = false;
    try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
    document.body.removeChild(ta);
    notify(ok);
  }

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
      '<div class="modal__footer"></div>';

    var backdrop = document.createElement('div');
    backdrop.className = 'modal-backdrop';
    backdrop.id = MODAL_ID + '-backdrop';

    document.body.appendChild(backdrop);
    document.body.appendChild(modal);
    return modal;
  }

  /**
   * Show the feedback modal.
   * @param {string} type - 'success' | 'error' | 'warning' | 'info'
   * @param {string} message - The feedback message (already localized from server)
   * @param {{detail?: string, errorId?: string, retry?: function}} [options]
   */
  function show(type, message, options) {
    options = options || {};
    var validTypes = { success: 1, error: 1, warning: 1, info: 1 };
    if (!validTypes[type]) type = 'info';

    var strings = getStrings();
    var modal = getOrCreateModal();
    var backdrop = document.getElementById(MODAL_ID + '-backdrop');

    modal.querySelector('#' + MODAL_ID + '-title').textContent = strings[type] || strings.info;

    // Body: icon + message + optional errorId block + optional collapsible detail panel.
    var icon = ICONS[type] || ICONS.info;
    var bodyHtml = '<div class="feedback-icon">' + icon + '</div>' +
                   '<div class="feedback-message">' + escapeHtml(message) + '</div>';

    if (options.errorId) {
      bodyHtml +=
        '<div class="feedback-error-id">' +
          '<span class="feedback-error-id__label">' + escapeHtml(strings.errorId) + ':</span> ' +
          '<code class="feedback-error-id__value">' + escapeHtml(options.errorId) + '</code> ' +
          '<button type="button" class="feedback-error-id__copy btn btn-sm" data-action="copy-error-id">' +
            escapeHtml(strings.copy) +
          '</button>' +
        '</div>';
    }

    if (options.detail) {
      bodyHtml +=
        '<details class="feedback-detail">' +
          '<summary class="feedback-detail__summary">' + escapeHtml(strings.showDetails) + '</summary>' +
          '<pre class="feedback-detail__content">' + escapeHtml(options.detail) + '</pre>' +
        '</details>';
    }

    var bodyEl = modal.querySelector('#' + MODAL_ID + '-body');
    bodyEl.innerHTML = bodyHtml;

    // Footer: optional retry first (so OK stays the rightmost/default action), then OK.
    var footerEl = modal.querySelector('.modal__footer');
    var footerHtml = '';
    if (typeof options.retry === 'function') {
      footerHtml += '<button type="button" class="btn btn-secondary" data-action="retry">' +
                      escapeHtml(strings.retry) +
                    '</button>';
    }
    footerHtml += '<button type="button" class="btn btn-primary" data-action="ok">' +
                    escapeHtml(strings.ok) +
                  '</button>';
    footerEl.innerHTML = footerHtml;

    var okBtn = footerEl.querySelector('[data-action="ok"]');
    var retryBtn = footerEl.querySelector('[data-action="retry"]');
    var copyBtn = bodyEl.querySelector('[data-action="copy-error-id"]');
    var detailsEl = bodyEl.querySelector('.feedback-detail');

    modal.className = 'modal modal--sm modal--feedback modal--feedback-' + type;

    modal.classList.add('is-open');
    backdrop.classList.add('is-open');
    document.body.style.overflow = 'hidden';

    okBtn.focus();

    function close() {
      modal.classList.remove('is-open');
      backdrop.classList.remove('is-open');
      document.body.style.overflow = '';
      cleanup();
    }

    function getFocusables() {
      return modal.querySelectorAll(
        'button:not([disabled]), summary, [href], [tabindex]:not([tabindex="-1"])'
      );
    }

    function handleKey(e) {
      if (e.key === 'Escape') { close(); return; }
      // Enter triggers OK only when OK has focus — avoids accidental dismissal
      // while interacting with retry, copy, or the details summary.
      if (e.key === 'Enter' && document.activeElement === okBtn) {
        close();
        return;
      }
      if (e.key === 'Tab') {
        var focusables = getFocusables();
        if (focusables.length === 0) return;
        e.preventDefault();
        var idx = Array.prototype.indexOf.call(focusables, document.activeElement);
        var nextIdx;
        if (e.shiftKey) {
          nextIdx = idx <= 0 ? focusables.length - 1 : idx - 1;
        } else {
          nextIdx = (idx + 1) % focusables.length;
        }
        focusables[nextIdx].focus();
      }
    }

    function handleBackdropClick(e) {
      if (e.target === backdrop) { close(); }
    }

    function handleCopyClick() {
      copyToClipboard(options.errorId, copyBtn, strings);
    }

    function handleRetryClick() {
      close();
      try { options.retry(); } catch (err) { /* swallow — caller decides what to do on failure */ }
    }

    function handleDetailsToggle() {
      var summary = detailsEl.querySelector('summary');
      if (summary) {
        summary.textContent = detailsEl.open ? strings.hideDetails : strings.showDetails;
      }
    }

    function cleanup() {
      okBtn.removeEventListener('click', close);
      modal.removeEventListener('keydown', handleKey);
      backdrop.removeEventListener('click', handleBackdropClick);
      if (copyBtn) copyBtn.removeEventListener('click', handleCopyClick);
      if (retryBtn) retryBtn.removeEventListener('click', handleRetryClick);
      if (detailsEl) detailsEl.removeEventListener('toggle', handleDetailsToggle);
    }

    okBtn.addEventListener('click', close);
    modal.addEventListener('keydown', handleKey);
    backdrop.addEventListener('click', handleBackdropClick);
    if (copyBtn) copyBtn.addEventListener('click', handleCopyClick);
    if (retryBtn) retryBtn.addEventListener('click', handleRetryClick);
    if (detailsEl) detailsEl.addEventListener('toggle', handleDetailsToggle);
  }

  window.FeedbackModal = { show: show };
})();
