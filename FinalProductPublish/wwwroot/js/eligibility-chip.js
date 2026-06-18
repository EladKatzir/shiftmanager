// Shared eligibility-reason chip renderer. Consumed by calendar-bottom-sheet.js (chore picker)
// and justice-panel.js (justice drawer). Pure render — no fetch, no DOM mutation outside the returned string.
// MARKER: eligibility-chip.js v1 (chores-parity phase 4)
(function () {
    'use strict';

    function loc(key, fallback) {
        if (window.AppLocalizer && typeof window.AppLocalizer[key] === 'string') return window.AppLocalizer[key];
        return fallback || key;
    }
    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    // c: { hardReasons: string[], warnings: string[] }
    // Returns an HTML string of zero or more <span class="elig-chip ..."> chips (already-localized text).
    function renderChips(c) {
        if (!c) return '';
        var html = '';
        (c.hardReasons || []).forEach(function (r) {
            html += '<span class="elig-chip elig-chip--block" title="' + esc(r) + '">⊘ ' + esc(r) + '</span>';
        });
        (c.warnings || []).forEach(function (r) {
            html += '<span class="elig-chip elig-chip--warn" title="' + esc(r) + '">⚠ ' + esc(r) + '</span>';
        });
        return html;
    }

    // For native <option> contexts (bottom-sheet user/type <select>): prefix glyph + disable on hard block.
    // Returns { prefix: string, disabled: bool, title: string }.
    function decorateOption(c) {
        if (!c) return { prefix: '', disabled: false, title: '' };
        var blocked = (c.hardReasons || []).length > 0;
        var warn = (c.warnings || []).length > 0;
        var glyph = blocked ? '⊘ ' : (warn ? '⚠ ' : '');
        var title = ((c.hardReasons || []).concat(c.warnings || [])).join(', ');
        return { prefix: glyph, disabled: blocked, title: title };
    }

    window.EligibilityChip = { renderChips: renderChips, decorateOption: decorateOption, loc: loc };
})();
