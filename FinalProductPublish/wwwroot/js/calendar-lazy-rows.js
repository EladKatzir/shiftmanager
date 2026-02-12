/**
 * Calendar Lazy Rows — Progressive rendering for large calendars
 * Hides rows beyond INITIAL_VISIBLE and reveals them in batches as the user scrolls.
 * This prevents DOM overload for calendars with 50-150+ rows.
 */
(function () {
    'use strict';

    var INITIAL_VISIBLE = 50;
    var BATCH_SIZE = 20;
    var THRESHOLD_PX = 300; // reveal next batch when sentinel is within 300px of viewport

    function init() {
        var tables = document.querySelectorAll('.excel-calendar__table');
        tables.forEach(function (table) {
            var tbody = table.querySelector('tbody');
            if (!tbody) return;

            var rows = tbody.querySelectorAll('tr:not(.excel-calendar__group-header)');
            if (rows.length <= INITIAL_VISIBLE) return; // no need to lazy-load

            console.log('[LazyRows] ' + rows.length + ' rows detected, lazy-loading after ' + INITIAL_VISIBLE);

            // Hide rows beyond initial visible count
            var hiddenRows = [];
            for (var i = INITIAL_VISIBLE; i < rows.length; i++) {
                rows[i].classList.add('calendar-lazy-hidden');
                rows[i].style.display = 'none';
                hiddenRows.push(rows[i]);
            }

            // Add a sentinel row at the end of the visible section
            var sentinel = document.createElement('tr');
            sentinel.className = 'calendar-lazy-sentinel';
            sentinel.innerHTML = '<td colspan="100" style="height:1px;padding:0;border:none;"></td>';
            // Insert sentinel after the last visible row
            var lastVisibleRow = rows[INITIAL_VISIBLE - 1];
            if (lastVisibleRow.nextSibling) {
                tbody.insertBefore(sentinel, lastVisibleRow.nextSibling);
            } else {
                tbody.appendChild(sentinel);
            }

            // Add loading indicator
            var loadingRow = document.createElement('tr');
            loadingRow.className = 'calendar-lazy-loading';
            loadingRow.innerHTML = '<td colspan="100" style="text-align:center;padding:0.5rem;color:var(--muted);font-size:0.85rem;">' +
                '<span class="calendar-lazy-loading__text">' + (rows.length - INITIAL_VISIBLE) + ' more rows...</span></td>';
            sentinel.parentNode.insertBefore(loadingRow, sentinel.nextSibling);

            // Use IntersectionObserver to reveal batches
            if ('IntersectionObserver' in window) {
                var revealedCount = 0;
                var observer = new IntersectionObserver(function (entries) {
                    entries.forEach(function (entry) {
                        if (!entry.isIntersecting) return;

                        // Reveal next batch
                        var end = Math.min(revealedCount + BATCH_SIZE, hiddenRows.length);
                        for (var j = revealedCount; j < end; j++) {
                            hiddenRows[j].classList.remove('calendar-lazy-hidden');
                            hiddenRows[j].style.display = '';
                        }
                        revealedCount = end;

                        // Update loading text
                        var remaining = hiddenRows.length - revealedCount;
                        if (remaining > 0) {
                            var loadingText = loadingRow.querySelector('.calendar-lazy-loading__text');
                            if (loadingText) {
                                loadingText.textContent = remaining + ' more rows...';
                            }
                        }

                        // Move sentinel after newly revealed rows
                        if (revealedCount < hiddenRows.length) {
                            var lastRevealed = hiddenRows[revealedCount - 1];
                            if (lastRevealed.nextSibling) {
                                tbody.insertBefore(sentinel, lastRevealed.nextSibling);
                            }
                            tbody.insertBefore(loadingRow, sentinel.nextSibling);
                        } else {
                            // All rows revealed — clean up
                            observer.disconnect();
                            sentinel.remove();
                            loadingRow.remove();
                            console.log('[LazyRows] All rows revealed');
                        }
                    });
                }, {
                    root: null,
                    rootMargin: THRESHOLD_PX + 'px',
                    threshold: 0
                });

                observer.observe(sentinel);
            } else {
                // Fallback: show all rows immediately if no IntersectionObserver
                hiddenRows.forEach(function (row) {
                    row.classList.remove('calendar-lazy-hidden');
                    row.style.display = '';
                });
                sentinel.remove();
                loadingRow.remove();
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
