/*
 * icon-runtime.js — JS-side Lucide icon renderer. Pairs with IconTagHelper.cs
 * so dynamically-built DOM (site.js navigation, calendar radar, myteam cards,
 * roster dock) can emit the same SVGs as the server tag helper.
 *
 * Usage:
 *   const html = Icons.render('calendar', { size: 20, class: 'nav-icon' });
 *   element.innerHTML = html;
 *
 * Size can be 'xs' | 'sm' | 'md' | 'lg' | 'xl' | '2xl' or a pixel number.
 * Unknown names render a muted help-circle + console.warn (never throws).
 */
(function () {
  'use strict';

  const SIZE_TOKENS = { xs: 12, sm: 16, md: 20, lg: 24, xl: 32, '2xl': 48 };

  // Paths mirror a subset of IconTagHelper.cs (the ones actually used from JS).
  // If a new icon is needed server-side AND client-side, add it to both.
  const ICONS = {
    'home':            '<path d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6"/>',
    'calendar':        '<path d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"/>',
    'calendar-days':   '<path d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"/><path d="M8 14h.01M12 14h.01M16 14h.01M8 18h.01M12 18h.01"/>',
    'clock':           '<path d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"/>',
    'user':            '<path d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"/>',
    'users':           '<path d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z"/>',
    'check':           '<path d="M5 13l4 4L19 7"/>',
    'x':               '<path d="M6 18L18 6M6 6l12 12"/>',
    'plus':            '<path d="M12 4v16m8-8H4"/>',
    'minus':           '<path d="M20 12H4"/>',
    'pencil':          '<path d="M17 3a2.85 2.83 0 114 4L7.5 20.5 2 22l1.5-5.5Z"/>',
    'trash-2':         '<path d="M3 6h18M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2m3 0v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6h14zM10 11v6M14 11v6"/>',
    'save':            '<path d="M19 21H5a2 2 0 01-2-2V5a2 2 0 012-2h11l5 5v11a2 2 0 01-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/>',
    'bell':            '<path d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"/>',
    'search':          '<path d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/>',
    'settings':        '<path d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z"/><circle cx="12" cy="12" r="3"/>',
    'alert-triangle':  '<path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"/>',
    'check-circle':    '<path d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"/>',
    'menu':            '<path d="M4 6h16M4 12h16M4 18h16"/>',
    'chevron-down':    '<path d="M19 9l-7 7-7-7"/>',
    'chevron-up':      '<path d="M5 15l7-7 7 7"/>',
    'chevron-left':    '<path d="M15 19l-7-7 7-7"/>',
    'chevron-right':   '<path d="M9 5l7 7-7 7"/>',
    'palette':         '<circle cx="13.5" cy="6.5" r=".5"/><circle cx="17.5" cy="10.5" r=".5"/><circle cx="8.5" cy="7.5" r=".5"/><circle cx="6.5" cy="12.5" r=".5"/><path d="M12 2a10 10 0 0010 10 4 4 0 01-4 4h-1.5a2 2 0 000 4 2 2 0 01-2 2 10 10 0 110-20z"/>',
    'help-circle':     '<circle cx="12" cy="12" r="10"/><path d="M9.09 9a3 3 0 015.83 1c0 2-3 3-3 3"/><line x1="12" y1="17" x2="12.01" y2="17"/>',
    'star':            '<polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>',
    'target':          '<circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="6"/><circle cx="12" cy="12" r="2"/>',
    'mail':            '<path d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"/>',
    'shield':          '<path d="M12 2l8 4v6c0 5-3.5 9-8 10-4.5-1-8-5-8-10V6l8-4z"/>',
    'zap':             '<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/>',
    'radio':           '<path d="M4.9 19.1C1 15.2 1 8.8 4.9 4.9M7.8 16.2c-2.3-2.3-2.3-6.1 0-8.5M16.2 7.8c2.3 2.3 2.3 6.1 0 8.5M19.1 4.9C23 8.8 23 15.2 19.1 19.1"/><circle cx="12" cy="12" r="2"/>'
  };

  function render(name, opts) {
    opts = opts || {};
    const size = typeof opts.size === 'number'
      ? opts.size
      : (SIZE_TOKENS[opts.size] || SIZE_TOKENS.md);
    const cls = ['icon', 'icon--' + size];
    if (opts.class) cls.push(opts.class);
    const paths = ICONS[name] || ICONS['help-circle'];
    if (!ICONS[name] && window.console && console.warn) {
      console.warn('[icon-runtime] Unknown icon:', name);
    }
    const ariaLabel = opts.ariaLabel ? ` role="img" aria-label="${escapeAttr(opts.ariaLabel)}"` : ' aria-hidden="true"';
    return `<svg class="${cls.join(' ')}" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"${ariaLabel}>${paths}</svg>`;
  }

  // NOTE: avoid arrow-fn with paren-wrapped object-return + index access.
  // NUglify (used by LigerShark.WebOptimizer) miscompiles
  //   c => ({ ... }[c])
  // into
  //   n => { ... }
  // stripping the parens, turning the object literal into a block body and
  // producing "Unexpected token ':'" at parse time. Use a hoisted lookup table
  // and a plain function expression instead — that survives minification cleanly.
  var ESCAPE_MAP = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
  function escapeAttr(s) {
    return String(s).replace(/[&<>"']/g, function (c) { return ESCAPE_MAP[c]; });
  }

  window.Icons = { render, has: (n) => n in ICONS };
})();
