/**
 * Home Type Calendar Painter — interactive monthly calendar for painting home days.
 * Admin clicks/drags dates to mark them as home days. Stores as JSON array in hidden input.
 *
 * NOTE: All functions use const arrow expressions (not function declarations) to prevent
 * NUglify/WebOptimizer from hoisting them out of scope during minification.
 */
(function () {
    'use strict';

    const container = document.getElementById('calendar-painter');
    const hiddenInput = document.getElementById('pattern-json');
    if (!container || !hiddenInput) return;

    const selectedDates = new Set();
    let currentMonth = new Date();
    let isMouseDown = false;
    let paintMode = true; // true = adding, false = removing

    // Locale detection — dynamic from document lang attribute
    const lang = document.documentElement.lang || 'en';
    const locale = lang === 'he' ? 'he-IL' : 'en-US';

    // Generate localized day-of-week headers starting from Sunday
    const dayHeaders = [];
    const dayFormatter = new Intl.DateTimeFormat(locale, { weekday: 'short' });
    // Use a known Sunday (2026-01-04 is a Sunday) as reference
    for (let i = 0; i < 7; i++) {
        const refDate = new Date(2026, 0, 4 + i); // Sun=4, Mon=5, ..., Sat=10
        dayHeaders.push(dayFormatter.format(refDate));
    }

    // Localized selection count template
    const daysSelectedTemplate = window.AppLocalizer?.HomeType_DaysSelected || '{0} days selected';

    const formatDaysSelected = (count) => {
        return daysSelectedTemplate.replace('{0}', count);
    };

    const syncHiddenInput = () => {
        hiddenInput.value = JSON.stringify(Array.from(selectedDates).sort());
        // Update info
        const info = container.querySelector('.htc-info');
        if (info) info.textContent = formatDaysSelected(selectedDates.size);
    };

    const toggleDate = (cell) => {
        const date = cell.dataset.date;
        if (paintMode) {
            selectedDates.add(date);
            cell.classList.add('htc-day--selected');
        } else {
            selectedDates.delete(date);
            cell.classList.remove('htc-day--selected');
        }
        syncHiddenInput();
    };

    const render = () => {
        const year = currentMonth.getFullYear();
        const month = currentMonth.getMonth();
        const firstDay = new Date(year, month, 1);
        const lastDay = new Date(year, month + 1, 0);
        const startDow = firstDay.getDay(); // Sunday=0 (week starts Sunday)

        const monthName = firstDay.toLocaleDateString(locale, { year: 'numeric', month: 'long' });

        let html = `
            <div class="htc-header">
                <button type="button" class="htc-nav" data-dir="-1">&laquo;</button>
                <span class="htc-month">${monthName}</span>
                <button type="button" class="htc-nav" data-dir="1">&raquo;</button>
            </div>
            <div class="htc-grid">
                ${dayHeaders.map(d => `<div class="htc-dow">${d}</div>`).join('')}
        `;

        // Empty cells before first day (Sunday-based: Sunday=0)
        for (let i = 0; i < startDow; i++) {
            html += '<div class="htc-empty"></div>';
        }

        // Day cells
        for (let day = 1; day <= lastDay.getDate(); day++) {
            const dateStr = `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
            const isSelected = selectedDates.has(dateStr);
            html += `<div class="htc-day ${isSelected ? 'htc-day--selected' : ''}" data-date="${dateStr}">${day}</div>`;
        }

        html += '</div>';

        // Selection count
        html += `<div class="htc-info">${formatDaysSelected(selectedDates.size)}</div>`;

        container.innerHTML = html;

        // Nav buttons
        container.querySelectorAll('.htc-nav').forEach(btn => {
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                const dir = parseInt(this.dataset.dir);
                currentMonth.setMonth(currentMonth.getMonth() + dir);
                render();
            });
        });

        // Day click/drag
        container.querySelectorAll('.htc-day').forEach(cell => {
            cell.addEventListener('mousedown', function (e) {
                e.preventDefault();
                isMouseDown = true;
                paintMode = !selectedDates.has(this.dataset.date);
                toggleDate(this);
            });
            cell.addEventListener('mouseenter', function () {
                if (isMouseDown) toggleDate(this);
            });
        });
    };

    document.addEventListener('mouseup', () => { isMouseDown = false; });

    render();
})();
