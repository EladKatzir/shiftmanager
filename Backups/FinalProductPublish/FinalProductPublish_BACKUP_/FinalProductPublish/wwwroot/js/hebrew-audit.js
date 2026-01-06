// Hebrew Language Audit and RTL Support
// This file provides utilities for RTL (Right-to-Left) layout debugging

document.addEventListener('DOMContentLoaded', function() {
  console.log('Hebrew RTL support loaded');

  // Log RTL status for debugging
  const html = document.documentElement;
  const dir = html.getAttribute('dir');
  const lang = html.getAttribute('lang');

  console.log('Document direction:', dir);
  console.log('Document language:', lang);

  // Debug: Check if RTL CSS is loaded
  const rtlStylesheet = Array.from(document.styleSheets).find(sheet => {
    try {
      return sheet.href && sheet.href.includes('rtl.css');
    } catch (e) {
      return false;
    }
  });

  if (rtlStylesheet) {
    console.log('✓ RTL stylesheet loaded successfully');
  } else {
    console.warn('⚠ RTL stylesheet not found');
  }

  // Utility: Add visual debugging for RTL layout (development only)
  if (window.location.hostname === 'localhost' && window.location.search.includes('debug=rtl')) {
    document.body.style.outline = '2px dashed red';
    console.log('RTL Debug mode enabled');
  }
});
