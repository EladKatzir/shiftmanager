/**
 * Home Type Rule Preview — read-only preview of dates matching the rule
 * configuration the admin is editing. Reactively updates as the admin
 * changes cycleWeeks, homeDays, weekOffsets, or anchor.
 *
 * NOTE: All functions use const arrow expressions (not function declarations) to prevent
 * NUglify/WebOptimizer from hoisting them out of scope during minification.
 */
(function () {
    'use strict';

    const PREVIEW_DAYS_AHEAD = 60;

    const lang = document.documentElement.lang || 'en';
    const locale = lang === 'he' ? 'he-IL' : 'en-US';

    const parseLocalDate = (str) => {
        // Parse yyyy-MM-dd as a local date (no UTC shift)
        if (!str || typeof str !== 'string') return null;
        const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(str);
        if (!m) return null;
        return new Date(parseInt(m[1], 10), parseInt(m[2], 10) - 1, parseInt(m[3], 10));
    };

    const formatLocalDate = (date) => {
        const y = date.getFullYear();
        const m = String(date.getMonth() + 1).padStart(2, '0');
        const d = String(date.getDate()).padStart(2, '0');
        return y + '-' + m + '-' + d;
    };

    const readRule = () => {
        const cycleEl = document.querySelector('[name=RuleCycleWeeks]');
        const cycle = Math.max(1, Math.min(12, parseInt((cycleEl && cycleEl.value) || '4', 10) || 4));

        const days = Array.from(document.querySelectorAll('input[name=RuleHomeDays]:checked'))
            .map(c => parseInt(c.value, 10))
            .filter(n => !Number.isNaN(n));

        const offsets = Array.from(document.querySelectorAll('input[name=RuleWeekOffsets]:checked'))
            .map(c => parseInt(c.value, 10))
            .filter(n => !Number.isNaN(n));

        const anchorEl = document.querySelector('[name=RuleAnchor]');
        const anchor = parseLocalDate(anchorEl && anchorEl.value) || new Date();

        return { cycle: cycle, days: days, offsets: offsets, anchor: anchor };
    };

    const previewDates = (rule, daysAhead) => {
        const dates = [];
        if (rule.days.length === 0 || rule.offsets.length === 0) return dates;

        const today = new Date();
        const todayMidnight = new Date(today.getFullYear(), today.getMonth(), today.getDate());
        const anchorMidnight = new Date(rule.anchor.getFullYear(), rule.anchor.getMonth(), rule.anchor.getDate());
        const msPerDay = 1000 * 60 * 60 * 24;

        for (let i = 0; i < daysAhead; i++) {
            const d = new Date(todayMidnight);
            d.setDate(todayMidnight.getDate() + i);
            if (rule.days.indexOf(d.getDay()) === -1) continue;

            const dayDelta = Math.floor((d - anchorMidnight) / msPerDay);
            // Skip dates before the anchor — rule has no defined behaviour there
            if (dayDelta < 0) continue;

            const weekNum = Math.floor(dayDelta / 7);
            const cycleWeek = ((weekNum % rule.cycle) + rule.cycle) % rule.cycle;
            if (rule.offsets.indexOf(cycleWeek) !== -1) dates.push(d);
        }
        return dates;
    };

    const render = () => {
        const container = document.getElementById('rule-preview-calendar');
        if (!container) return;

        const rule = readRule();
        const dates = previewDates(rule, PREVIEW_DAYS_AHEAD);

        if (dates.length === 0) {
            const noMatch = (window.AppLocalizer && window.AppLocalizer.HomeType_Preview_NoMatch)
                || 'No dates match the current rule in the next 60 days.';
            container.innerHTML = '<p style="color: var(--text-muted);">' + noMatch + '</p>';
            return;
        }

        const dayFmt = new Intl.DateTimeFormat(locale, { weekday: 'short' });

        const html = '<div class="preview-grid" style="display: flex; flex-wrap: wrap; gap: 0.4rem;">'
            + dates.map(function (d) {
                return '<span class="preview-date" style="background: var(--surface-2, #f3f3f3); border: 1px solid var(--border, #ddd); border-radius: 4px; padding: 0.25rem 0.5rem; font-size: 0.85em; font-family: monospace;">'
                    + formatLocalDate(d) + ' <em style="color: var(--text-muted); font-style: normal; font-size: 0.85em;">' + dayFmt.format(d) + '</em></span>';
            }).join('')
            + '</div>';

        container.innerHTML = html;
    };

    // Re-render when any rule input changes
    const rebuildWeekOffsetCheckboxes = () => {
        const wrap = document.getElementById('week-offset-checkboxes');
        const cycleEl = document.querySelector('[name=RuleCycleWeeks]');
        if (!wrap || !cycleEl) return;
        const cycle = Math.max(1, Math.min(12, parseInt(cycleEl.value || '4', 10) || 4));

        // Preserve existing selections when shrinking/growing the cycle
        const currentlyChecked = new Set(
            Array.from(wrap.querySelectorAll('input[name=RuleWeekOffsets]:checked'))
                .map(c => parseInt(c.value, 10))
        );

        let html = '';
        for (let i = 0; i < cycle; i++) {
            const checked = currentlyChecked.has(i) ? 'checked="checked"' : '';
            html += '<label class="checkbox-inline" style="display: inline-flex; align-items: center; gap: var(--space-1);">'
                + '<input type="checkbox" name="RuleWeekOffsets" value="' + i + '" ' + checked + ' />'
                + ' Week ' + (i + 1)
                + '</label>';
        }
        wrap.innerHTML = html;
    };

    document.addEventListener('change', function (e) {
        if (!e.target || !e.target.matches) return;
        if (e.target.matches('input[name=RuleCycleWeeks], input[name="RuleCycleWeeks"]')) {
            rebuildWeekOffsetCheckboxes();
            render();
            return;
        }
        if (e.target.matches('input[name^=Rule], input[name^="Rule"]')) {
            render();
        }
    });
    document.addEventListener('input', function (e) {
        if (!e.target || !e.target.matches) return;
        if (e.target.matches('input[name=RuleCycleWeeks], input[name="RuleCycleWeeks"]')) {
            rebuildWeekOffsetCheckboxes();
            render();
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', render);
    } else {
        render();
    }
})();
