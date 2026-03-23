// Hebrew Language Audit and RTL Support
// This file provides utilities for RTL (Right-to-Left) layout debugging

document.addEventListener('DOMContentLoaded', function() {
  // Check if RTL CSS is loaded
  const rtlStylesheet = Array.from(document.styleSheets).find(sheet => {
    try {
      return sheet.href && sheet.href.includes('rtl.css');
    } catch (e) {
      return false;
    }
  });

  if (!rtlStylesheet) {
    console.warn('[Shifty] RTL stylesheet not found');
  }

  // Utility: Add visual debugging for RTL layout (development only)
  if (window.location.hostname === 'localhost' && window.location.search.includes('debug=rtl')) {
    document.body.style.outline = '2px dashed red';
  }
});
