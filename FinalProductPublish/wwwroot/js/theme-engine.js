/*
 * theme-engine.js — loaded synchronously in <head> before any stylesheet paints.
 * Reads the "theme" cookie the server sets when the user saves their appearance,
 * derives all --primary-* tokens from a single input hue, and injects a
 * <style id="theme-override"> into <head> before site.css is applied.
 *
 * Token strategy:
 *   --primary           = user's hex
 *   --primary-hover     = slightly darker (L - 8%)
 *   --primary-soft      = desaturated, light wash (used as soft fill / focus ring)
 *   --primary-contrast  = WCAG-picked white or near-black
 *   --primary-rgb       = comma triplet for rgba() usage
 *   --accent family     = hue + 30° with same derivation applied
 *   --focus-ring        = box-shadow style referencing --primary-soft
 *
 * Semantic (success/warning/danger/info), domain (--shift-*, --camo-*, --onduty-*),
 * and surface tokens are NEVER overridden.
 */
(function () {
  'use strict';

  const STYLE_ID = 'theme-override';
  const COOKIE = 'theme';

  // ---------------------------------------------------------------- public API
  const api = {
    apply: applyTheme,
    derive: derivePalette,
    readCookie: readCookie,
    setPreview: setPreview,          // used by picker for live preview (no save)
    clearPreview: clearPreview
  };

  // ------------------------------------------------------------ initial paint
  const initial = readCookie();
  if (initial && initial.color) {
    applyTheme(initial.color, initial.mode);
  } else if (initial && initial.mode) {
    applyMode(initial.mode);
  }

  // ------------------------------------------------------ public implementations
  function applyTheme(hex, mode) {
    const html = document.documentElement;
    html.setAttribute('data-theme-color', hex || '');
    if (mode) html.setAttribute('data-theme-mode', mode);
    const css = derivePalette(hex);
    upsertStyle(css);
    if (mode) applyMode(mode);
  }

  function setPreview(hex, mode) {
    // Live preview — same effect as apply, but does not persist and does not
    // mutate data-theme-color (so Reset still goes back to the saved value).
    const css = derivePalette(hex);
    upsertStyle(css);
    if (mode) applyMode(mode);
  }

  function clearPreview() {
    const el = document.getElementById(STYLE_ID);
    if (el) el.remove();
    const initial = readCookie();
    if (initial && initial.color) applyTheme(initial.color, initial.mode);
  }

  // -------------------------------------------------------- derivation
  function derivePalette(hex) {
    const base = hexToHsl(hex);
    if (!base) return '';

    const primary       = hslToHex(base.h, base.s, base.l);
    const primaryHover  = hslToHex(base.h, base.s, clamp(base.l - 0.08, 0.05, 0.95));
    const primarySoft   = hslToHex(base.h, Math.max(0.25, base.s - 0.30), clamp(base.l + 0.38, 0.05, 0.95));
    const primaryRgb    = hexToRgbTriplet(primary);
    const primaryTxt    = pickContrast(primary);

    const accentH       = (base.h + 30) % 360;
    const accentL       = clamp(base.l, 0.35, 0.65);
    const accent        = hslToHex(accentH, base.s, accentL);
    const accentHover   = hslToHex(accentH, base.s, clamp(accentL - 0.08, 0.05, 0.95));
    const accentSoft    = hslToHex(accentH, Math.max(0.25, base.s - 0.30), clamp(accentL + 0.32, 0.05, 0.95));
    const accentRgb     = hexToRgbTriplet(accent);

    // Custom properties written with !important so they win over any stylesheet
    // (including site.css) regardless of load order — we inject into <head> BEFORE
    // stylesheets have loaded to kill FOUC, so we can't rely on cascade order.
    return `:root{
  --primary:${primary} !important;
  --primary-hover:${primaryHover} !important;
  --primary-soft:${primarySoft} !important;
  --primary-contrast:${primaryTxt} !important;
  --primary-rgb:${primaryRgb} !important;
  --accent:${accent} !important;
  --accent-hover:${accentHover} !important;
  --accent-soft:${accentSoft} !important;
  --accent-rgb:${accentRgb} !important;
  --info:${primary} !important;
  --info-hover:${primaryHover} !important;
  --info-soft:${primarySoft} !important;
  --info-rgb:${primaryRgb} !important;
  --focus-ring:0 0 0 3px ${primarySoft} !important;
}`;
  }

  function applyMode(mode) {
    const html = document.documentElement;
    if (mode === 'auto') {
      const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
      html.setAttribute('data-theme', prefersDark ? 'dark' : 'light');
    } else if (mode === 'dark' || mode === 'light') {
      html.setAttribute('data-theme', mode);
    }
  }

  // ----------------------------------------------------- DOM + cookie helpers
  function upsertStyle(css) {
    let el = document.getElementById(STYLE_ID);
    if (!el) {
      el = document.createElement('style');
      el.id = STYLE_ID;
      document.head.appendChild(el);
    }
    el.textContent = css;
  }

  function readCookie() {
    const match = document.cookie.match(/(?:^|;\s*)theme=([^;]+)/);
    if (!match) return null;
    try {
      return JSON.parse(decodeURIComponent(match[1]));
    } catch (_) {
      return null;
    }
  }

  // ------------------------------------------------------ color math
  function hexToHsl(hex) {
    const m = /^#?([0-9a-f]{6})$/i.exec(hex || '');
    if (!m) return null;
    const n = parseInt(m[1], 16);
    const r = ((n >> 16) & 255) / 255;
    const g = ((n >> 8) & 255) / 255;
    const b = (n & 255) / 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    let h = 0, s = 0; const l = (max + min) / 2;
    if (max !== min) {
      const d = max - min;
      s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
      switch (max) {
        case r: h = ((g - b) / d + (g < b ? 6 : 0)); break;
        case g: h = ((b - r) / d + 2); break;
        case b: h = ((r - g) / d + 4); break;
      }
      h *= 60;
    }
    return { h, s, l };
  }

  function hslToHex(h, s, l) {
    h = ((h % 360) + 360) % 360;
    s = clamp(s, 0, 1); l = clamp(l, 0, 1);
    const c = (1 - Math.abs(2 * l - 1)) * s;
    const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
    const m = l - c / 2;
    let r = 0, g = 0, b = 0;
    if (h < 60)       { r = c; g = x; }
    else if (h < 120) { r = x; g = c; }
    else if (h < 180) { g = c; b = x; }
    else if (h < 240) { g = x; b = c; }
    else if (h < 300) { r = x; b = c; }
    else              { r = c; b = x; }
    return '#' + [r + m, g + m, b + m]
      .map(v => Math.round(v * 255).toString(16).padStart(2, '0'))
      .join('').toUpperCase();
  }

  function hexToRgbTriplet(hex) {
    const m = /^#?([0-9a-f]{6})$/i.exec(hex || '');
    if (!m) return '0, 0, 0';
    const n = parseInt(m[1], 16);
    return `${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}`;
  }

  function pickContrast(hex) {
    // WCAG relative-luminance — not sRGB L, but close enough to avoid miscalls.
    const m = /^#?([0-9a-f]{6})$/i.exec(hex || '');
    if (!m) return '#FFFFFF';
    const n = parseInt(m[1], 16);
    const rs = ((n >> 16) & 255) / 255;
    const gs = ((n >> 8) & 255) / 255;
    const bs = (n & 255) / 255;
    const lin = (c) => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4));
    const L = 0.2126 * lin(rs) + 0.7152 * lin(gs) + 0.0722 * lin(bs);
    return L > 0.45 ? '#1A1F2B' : '#FFFFFF';
  }

  function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

  // ---------------------------------------------------- expose
  window.ThemeEngine = api;
})();
