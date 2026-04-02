// wwwroot/js/confirm-modal.js
// Intercepts data-confirm-modal forms and buttons, shows a styled confirmation modal.
// Replaces native confirm() for attribute-based patterns.

(function () {
  'use strict';

  const MODAL_ID = 'js-confirm-modal';

  function getOrCreateModal() {
    let modal = document.getElementById(MODAL_ID);
    if (modal) return modal;

    modal = document.createElement('div');
    modal.id = MODAL_ID;
    modal.setAttribute('role', 'dialog');
    modal.setAttribute('aria-modal', 'true');
    modal.setAttribute('aria-labelledby', MODAL_ID + '-title');
    modal.className = 'modal modal--sm';
    // C-1 fix: close button uses data-action="close", footer cancel uses data-action="cancel"
    // This prevents querySelector('[data-action="cancel"]') from returning the wrong element.
    modal.innerHTML =
      '<div class="modal__header">' +
        '<h2 class="modal__title" id="' + MODAL_ID + '-title"></h2>' +
        '<button class="modal__close" aria-label="Close" data-action="close">\u00d7</button>' +
      '</div>' +
      '<div class="modal__body" id="' + MODAL_ID + '-body"></div>' +
      '<div class="modal__footer">' +
        '<button class="btn btn-ghost" data-action="cancel"></button>' +
        '<button class="btn btn-danger" data-action="confirm"></button>' +
      '</div>';

    var backdrop = document.createElement('div');
    backdrop.className = 'modal-backdrop';
    backdrop.id = MODAL_ID + '-backdrop';

    document.body.appendChild(backdrop);
    document.body.appendChild(modal);
    return modal;
  }

  function showConfirmModal(opts) {
    var title = opts.title;
    var message = opts.message;
    var action = opts.action;
    var level = opts.level;
    var onConfirm = opts.onConfirm;
    var triggerEl = opts.triggerEl;

    var modal = getOrCreateModal();
    var backdrop = document.getElementById(MODAL_ID + '-backdrop');

    modal.querySelector('#' + MODAL_ID + '-title').textContent = title || 'Confirm';
    modal.querySelector('#' + MODAL_ID + '-body').textContent = message || '';

    // C-1 fix: select close and cancel buttons independently
    var closeBtn = modal.querySelector('[data-action="close"]');
    var cancelBtn = modal.querySelector('[data-action="cancel"]');
    var confirmBtn = modal.querySelector('[data-action="confirm"]');

    // Localize cancel text based on page language; close button keeps its × character
    cancelBtn.textContent = document.documentElement.lang === 'he' ? '\u05d1\u05d9\u05d8\u05d5\u05dc' : 'Cancel';
    confirmBtn.textContent = action || 'Confirm';

    // M-2 fix: support danger/warning/primary severity levels
    var btnClass = 'btn-primary';
    if (level === 'danger') btnClass = 'btn-danger';
    else if (level === 'warning') btnClass = 'btn-warning';
    confirmBtn.className = 'btn ' + btnClass;

    // Show
    modal.classList.add('is-open');
    backdrop.classList.add('is-open');
    document.body.style.overflow = 'hidden';
    confirmBtn.focus();

    function close(confirmed) {
      modal.classList.remove('is-open');
      backdrop.classList.remove('is-open');
      document.body.style.overflow = '';
      if (triggerEl) triggerEl.focus();
      cleanup();
      if (confirmed) onConfirm();
    }

    function handleKey(e) {
      if (e.key === 'Escape') close(false);
      if (e.key === 'Enter' && document.activeElement === confirmBtn) close(true);
    }

    function cleanup() {
      // C-1 fix: clean up listeners on both close and cancel buttons
      closeBtn.removeEventListener('click', cancelHandler);
      cancelBtn.removeEventListener('click', cancelHandler);
      confirmBtn.removeEventListener('click', confirmHandler);
      backdrop.removeEventListener('click', cancelHandler);
      document.removeEventListener('keydown', handleKey);
    }

    var cancelHandler = function () { close(false); };
    var confirmHandler = function () { close(true); };

    // C-1 fix: wire both close (×) and cancel buttons to the cancel action
    closeBtn.addEventListener('click', cancelHandler);
    cancelBtn.addEventListener('click', cancelHandler);
    confirmBtn.addEventListener('click', confirmHandler);
    backdrop.addEventListener('click', cancelHandler);
    document.addEventListener('keydown', handleKey);
  }

  function intercept(el) {
    var isForm = el.tagName === 'FORM';
    var eventName = isForm ? 'submit' : 'click';

    // Named handler so we can remove it before triggering the real action
    function handler(e) {
      e.preventDefault();
      e.stopImmediatePropagation();

      var dataset = el.dataset;
      var title = dataset.confirmTitle || '';
      var message = dataset.confirmMessage || '';
      var action = dataset.confirmAction || '';
      var level = dataset.confirmLevel || 'danger';

      showConfirmModal({
        title: title,
        message: message,
        action: action,
        level: level,
        triggerEl: e.target,
        onConfirm: function () {
          el.removeEventListener(eventName, handler);
          if (isForm) {
            // C-2 fix: dispatch a real DOM submit event so other submit listeners fire
            // (e.g. batchApprovalForm bridge that redirects to batchDataForm.submit()).
            // dispatchEvent returns false if any listener called e.preventDefault().
            // If cancelled by another handler, skip native submit — that handler owns the submission.
            // If not cancelled, call el.submit() for the native browser form submission.
            var submitEvent = new Event('submit', { bubbles: true, cancelable: true });
            var notCancelled = el.dispatchEvent(submitEvent);
            if (notCancelled) {
              el.submit();
            }
          } else {
            el.click();
          }
          // Re-attach for subsequent interactions on pages that don't navigate away
          el.addEventListener(eventName, handler);
        }
      });
    }

    el.addEventListener(eventName, handler);
  }

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-confirm-modal]').forEach(intercept);
  });

  // Expose for dynamically added elements
  window.ConfirmModal = { intercept: intercept };
})();
