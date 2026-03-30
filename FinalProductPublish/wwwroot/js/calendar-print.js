/**
 * Calendar Print Functionality (B-006)
 * Provides print button and print optimization for calendar/schedule pages
 */
(function() {
  'use strict';

  // Only initialize on calendar pages
  var calendarPages = ['/Calendar/', '/Schedule/'];
  var isCalendarPage = calendarPages.some(function(path) {
    return window.location.pathname.indexOf(path) !== -1;
  });

  if (!isCalendarPage) return;

  // Skip if toolbar already has a print button (avoids duplicate)
  if (document.querySelector('.cal-toolbar__print, .cal-toolbar [onclick*="print"]')) return;

  /**
   * Create and inject the print button
   */
  function createPrintButton() {
    var isHebrew = (document.documentElement.lang || 'en').indexOf('he') === 0;
    var button = document.createElement('button');
    button.className = 'btn--print-calendar';
    button.setAttribute('type', 'button');
    button.setAttribute('aria-label', isHebrew ? '\u05D4\u05D3\u05E4\u05E1\u05EA \u05DC\u05D5\u05D7 \u05E9\u05E0\u05D4' : 'Print calendar');
    button.setAttribute('title', isHebrew ? '\u05D4\u05D3\u05E4\u05E1 \u05EA\u05E6\u05D5\u05D2\u05EA \u05DC\u05D5\u05D7 \u05E9\u05E0\u05D4 \u05D6\u05D5' : 'Print this calendar view');
    button.innerHTML = '<span class="btn--print-calendar__icon" aria-hidden="true">\uD83D\uDDA8\uFE0F</span><span>' + (isHebrew ? '\u05D4\u05D3\u05E4\u05E1\u05D4' : 'Print') + '</span>';

    button.addEventListener('click', handlePrint);

    document.body.appendChild(button);
  }

  /**
   * Handle print button click
   */
  function handlePrint() {
    // Add print preparation class
    document.body.classList.add('preparing-print');

    // Expand any collapsed items before printing
    expandAllItems();

    // Small delay to allow CSS transitions
    setTimeout(function() {
      window.print();
      document.body.classList.remove('preparing-print');
    }, 100);
  }

  /**
   * Expand all hidden items and collapsed sections
   */
  function expandAllItems() {
    // Expand "show more" items
    var hiddenItems = document.querySelectorAll('.hidden-items:not(.is-visible)');
    hiddenItems.forEach(function(item) {
      item.classList.add('is-visible');
    });

    // Expand collapsed widgets
    var collapsedWidgets = document.querySelectorAll('.widget.is-collapsed');
    collapsedWidgets.forEach(function(widget) {
      widget.classList.remove('is-collapsed');
    });
  }

  /**
   * Add print date stamp to document
   */
  function addPrintDateStamp() {
    // Check if stamp already exists
    if (document.querySelector('.print-date-stamp')) return;

    var stamp = document.createElement('div');
    stamp.className = 'print-only print-date-stamp';

    var now = new Date();
    var dateStr = now.toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });

    stamp.textContent = dateStr;

    // Insert at the start of main content
    var mainContent = document.querySelector('.app-content') || document.querySelector('main');
    if (mainContent && mainContent.firstChild) {
      mainContent.insertBefore(stamp, mainContent.firstChild);
    }
  }

  /**
   * Initialize print functionality
   */
  function init() {
    // Wait for DOM to be ready
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', function() {
        createPrintButton();
        addPrintDateStamp();
      });
    } else {
      createPrintButton();
      addPrintDateStamp();
    }

    // Handle Ctrl+P to use our print handler
    document.addEventListener('keydown', function(e) {
      if ((e.ctrlKey || e.metaKey) && e.key === 'p') {
        // Let browser handle print, but prepare content first
        expandAllItems();
      }
    });

    // After print, restore collapsed state if needed
    if (window.matchMedia) {
      window.matchMedia('print').addEventListener('change', function(e) {
        if (!e.matches) {
          // Print dialog closed - could restore state here if needed
          // Print completed or cancelled
        }
      });
    }
  }

  init();
})();
