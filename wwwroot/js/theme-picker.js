/*
 * theme-picker.js — Appearance modal exposed via window.ThemePicker.open().
 * Activated by Shift+Click on .brand / .sidebar-brand / .page-loader__brand.
 * Depends on ThemeEngine (theme-engine.js) for live derivation + cookie reading.
 */
(function () {
  'use strict';

  const PRESETS = [
    { name: 'Navy',    color: '#1E3A5F' },
    { name: 'Sky',     color: '#5B9BD5' },
    { name: 'Forest',  color: '#2F6F4A' },
    { name: 'Coral',   color: '#E5735B' },
    { name: 'Plum',    color: '#7B3F7A' },
    { name: 'Emerald', color: '#0E9F6E' },
    { name: 'Sunset',  color: '#D97706' },
    { name: 'Slate',   color: '#475569' }
  ];

  const MODES = [
    { key: 'light', label: 'Light' },
    { key: 'dark',  label: 'Dark'  },
    { key: 'auto',  label: 'Auto'  }
  ];

  let root = null;
  let state = { color: '#1E3A5F', mode: 'light' };
  let initial = { color: '#1E3A5F', mode: 'light' };

  window.ThemePicker = { open, close };

  async function open() {
    if (root) return;

    // Pull current from cookie/engine, fallback to defaults.
    const saved = (window.ThemeEngine && window.ThemeEngine.readCookie()) || {};
    state = {
      color: (saved.color || getComputedVar('--primary') || '#1E3A5F').toUpperCase(),
      mode: saved.mode || document.documentElement.getAttribute('data-theme') || 'light'
    };
    initial = { ...state };
    render();
  }

  function close() {
    if (!root) return;
    // Revert live preview to saved value on close (unless Save was pressed).
    if (window.ThemeEngine) window.ThemeEngine.clearPreview();
    root.remove();
    root = null;
  }

  // ------------------------------------------------------------- render
  function render() {
    root = document.createElement('div');
    root.className = 'theme-picker-backdrop';
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    root.setAttribute('aria-labelledby', 'theme-picker-title');
    root.innerHTML = `
      <div class="theme-picker" role="document">
        <header class="theme-picker__header">
          <h2 class="theme-picker__title" id="theme-picker-title">Appearance</h2>
          <button class="theme-picker__close" aria-label="Close" data-action="close">&times;</button>
        </header>

        <div class="theme-picker__grid">
          <div class="theme-picker__canvas-wrap">
            <canvas class="theme-picker__canvas" width="180" height="180" aria-label="Hue / saturation picker"></canvas>
            <input type="range" class="theme-picker__lightness" min="0" max="100" value="50" aria-label="Lightness" />
          </div>

          <div class="theme-picker__inputs">
            <label>Hex<input type="text" data-input="hex" maxlength="7" /></label>
            <label>R<input type="number" min="0" max="255" data-input="r" /></label>
            <label>G<input type="number" min="0" max="255" data-input="g" /></label>
            <label>B<input type="number" min="0" max="255" data-input="b" /></label>
          </div>
        </div>

        <div>
          <p class="theme-picker__section-title">Quick pick</p>
          <div class="theme-picker__presets" role="radiogroup" aria-label="Preset colors"></div>
        </div>

        <div>
          <p class="theme-picker__section-title">Mode</p>
          <div class="theme-picker__modes" role="radiogroup" aria-label="Color mode"></div>
        </div>

        <div>
          <p class="theme-picker__section-title">Preview</p>
          <div class="theme-picker__preview">
            <div class="theme-picker__preview-row">
              <button class="theme-picker__preview-btn">Save</button>
              <button class="theme-picker__preview-btn theme-picker__preview-btn--outline">Cancel</button>
              <span class="theme-picker__preview-badge">Badge</span>
            </div>
            <div class="theme-picker__preview-row">
              <div class="theme-picker__preview-bar"><span></span></div>
              <input class="theme-picker__preview-input" value="Focus me" />
            </div>
          </div>
        </div>

        <footer class="theme-picker__footer">
          <button data-action="reset">Reset to default</button>
          <button class="theme-picker__save" data-action="save">Save</button>
        </footer>
      </div>`;
    document.body.appendChild(root);
    wire();
    drawCanvas();
    syncInputs();
    applyPreview();
  }

  function wire() {
    root.addEventListener('click', (e) => {
      const act = e.target.getAttribute('data-action');
      if (act === 'close' || e.target === root) return close();
      if (act === 'save') return save();
      if (act === 'reset') return reset();
    });

    // Hex/RGB inputs
    root.querySelectorAll('[data-input]').forEach(el => {
      el.addEventListener('input', onInputChange);
    });

    // Lightness slider
    root.querySelector('.theme-picker__lightness').addEventListener('input', onLightnessChange);

    // Presets
    const presetsHost = root.querySelector('.theme-picker__presets');
    PRESETS.forEach(p => {
      const btn = document.createElement('button');
      btn.className = 'theme-picker__preset';
      btn.title = p.name;
      btn.style.background = p.color;
      btn.setAttribute('role', 'radio');
      btn.setAttribute('aria-label', p.name);
      btn.addEventListener('click', () => {
        state.color = p.color;
        syncInputs();
        applyPreview();
        markPressed();
      });
      presetsHost.appendChild(btn);
    });

    // Modes
    const modesHost = root.querySelector('.theme-picker__modes');
    MODES.forEach(m => {
      const btn = document.createElement('button');
      btn.className = 'theme-picker__mode';
      btn.textContent = m.label;
      btn.setAttribute('role', 'radio');
      btn.dataset.mode = m.key;
      btn.addEventListener('click', () => {
        state.mode = m.key;
        markPressed();
        applyPreview();
      });
      modesHost.appendChild(btn);
    });

    markPressed();
  }

  // ----------------------------------------------------- interactions
  function onInputChange(e) {
    const type = e.target.dataset.input;
    if (type === 'hex') {
      const v = e.target.value.trim();
      if (/^#?[0-9a-f]{6}$/i.test(v)) {
        state.color = (v.startsWith('#') ? v : '#' + v).toUpperCase();
        syncInputs({ skipHex: true });
        applyPreview();
      }
    } else {
      const r = parseInt(root.querySelector('[data-input="r"]').value, 10) || 0;
      const g = parseInt(root.querySelector('[data-input="g"]').value, 10) || 0;
      const b = parseInt(root.querySelector('[data-input="b"]').value, 10) || 0;
      state.color = '#' + [r, g, b]
        .map(n => Math.max(0, Math.min(255, n)).toString(16).padStart(2, '0'))
        .join('').toUpperCase();
      syncInputs({ skipRgb: true });
      applyPreview();
    }
  }

  function onLightnessChange(e) {
    // Re-derive state.color with a new lightness — hue + sat preserved.
    const hsl = hexToHsl(state.color);
    if (!hsl) return;
    hsl.l = parseInt(e.target.value, 10) / 100;
    state.color = hslToHex(hsl.h, hsl.s, hsl.l);
    syncInputs();
    applyPreview();
  }

  // ----------------------------------------------------- rendering helpers
  function drawCanvas() {
    const canvas = root.querySelector('.theme-picker__canvas');
    const ctx = canvas.getContext('2d');
    const { width: w, height: h } = canvas;
    const cx = w / 2, cy = h / 2, rad = Math.min(cx, cy) - 2;

    const img = ctx.createImageData(w, h);
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const dx = x - cx, dy = y - cy;
        const d = Math.sqrt(dx * dx + dy * dy);
        if (d > rad) continue;
        const hue = (Math.atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        const sat = Math.min(1, d / rad);
        const [r, g, b] = hslToRgb(hue, sat, 0.5);
        const i = (y * w + x) * 4;
        img.data[i]     = r;
        img.data[i + 1] = g;
        img.data[i + 2] = b;
        img.data[i + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);

    canvas.addEventListener('click', (e) => {
      const rect = canvas.getBoundingClientRect();
      const x = e.clientX - rect.left, y = e.clientY - rect.top;
      const dx = x - cx, dy = y - cy;
      const d = Math.sqrt(dx * dx + dy * dy);
      if (d > rad) return;
      const hue = (Math.atan2(dy, dx) * 180 / Math.PI + 360) % 360;
      const sat = Math.min(1, d / rad);
      const cur = hexToHsl(state.color);
      state.color = hslToHex(hue, sat, cur ? cur.l : 0.5);
      syncInputs();
      applyPreview();
    });
  }

  function syncInputs(opts) {
    opts = opts || {};
    const hex = state.color;
    if (!opts.skipHex) root.querySelector('[data-input="hex"]').value = hex;
    if (!opts.skipRgb) {
      const n = parseInt(hex.slice(1), 16);
      root.querySelector('[data-input="r"]').value = (n >> 16) & 255;
      root.querySelector('[data-input="g"]').value = (n >> 8) & 255;
      root.querySelector('[data-input="b"]').value = n & 255;
    }
    const hsl = hexToHsl(hex);
    if (hsl) root.querySelector('.theme-picker__lightness').value = Math.round(hsl.l * 100);
    markPressed();
  }

  function markPressed() {
    root.querySelectorAll('.theme-picker__preset').forEach(b => {
      b.setAttribute('aria-pressed', b.style.background.toLowerCase().includes(state.color.toLowerCase()) ? 'true' : 'false');
    });
    root.querySelectorAll('.theme-picker__mode').forEach(b => {
      b.setAttribute('aria-pressed', b.dataset.mode === state.mode ? 'true' : 'false');
    });
  }

  function applyPreview() {
    if (window.ThemeEngine) window.ThemeEngine.setPreview(state.color, state.mode);
  }

  // ----------------------------------------------------- persistence
  async function save() {
    try {
      const res = await fetch('/Api/My/Theme', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          // Server-side CSRF gate for /Api/* — rejects 403 without this header.
          'X-Requested-With': 'XMLHttpRequest'
        },
        body: JSON.stringify({ color: state.color, mode: state.mode })
      });
      if (!res.ok) throw new Error('Save failed');
      if (window.ThemeEngine) window.ThemeEngine.apply(state.color, state.mode);
      toast('Saved');
      initial = { ...state };
    } catch (err) {
      toast('Could not save');
      if (window.Logger) window.Logger.error('ThemePicker', 'Save failed', err);
    }
  }

  async function reset() {
    try {
      const res = await fetch('/Api/My/Theme', {
        method: 'DELETE',
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
      });
      if (!res.ok) throw new Error('Reset failed');
      const el = document.getElementById('theme-override');
      if (el) el.remove();
      document.documentElement.removeAttribute('data-theme-color');
      document.documentElement.removeAttribute('data-theme-mode');
      toast('Reset');
      close();
    } catch (err) {
      toast('Could not reset');
      if (window.Logger) window.Logger.error('ThemePicker', 'Reset failed', err);
    }
  }

  function toast(msg) {
    const t = document.createElement('div');
    t.className = 'theme-picker__toast';
    t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 1800);
  }

  // ----------------------------------------------------- color math (shared w/ engine)
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
    const [r, g, b] = hslToRgb(h, s, l);
    return '#' + [r, g, b].map(v => v.toString(16).padStart(2, '0')).join('').toUpperCase();
  }

  function hslToRgb(h, s, l) {
    h = ((h % 360) + 360) % 360;
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
    return [r + m, g + m, b + m].map(v => Math.round(v * 255));
  }

  function getComputedVar(name) {
    const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return v || null;
  }
})();
