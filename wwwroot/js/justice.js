/**
 * justice.js — Justice Analytics page interactions.
 * Vanilla JS, IIFE-scoped, no globals, no external libraries.
 * Air-gapped safe.
 *
 * Implements:
 *   1. Fairness-basis toggle (client-side, no round-trip):
 *      swaps Expected, Expected %, deviation pill + band class,
 *      share-bar colour, donut-seg class, eq-seg class, basis-tag text.
 *   2. Drill-into dropdown: open/close, outside-click, Escape,
 *      aria-expanded, arrow-key nav of items.
 *   3. Period date inputs: auto-submit #justice-period-form on change.
 *   4. Drillable table rows: keyboard Enter/Space navigates href.
 *   5. Group-by toggle: NOT present in markup — skipped (see report).
 *
 * Reduced-motion: any CSS transitions/animations are already handled
 * in the stylesheet with @media (prefers-reduced-motion: reduce).
 * JS does not apply inline transitions; no guard needed here.
 *
 * DOM contract (derived from Pages/Admin/Analytics.cshtml):
 *   [data-basis-toggle]               — .basis-switch container
 *   .basis-opt[data-basis]            — "bysize" | "equal" toggle links
 *   tr > td[data-exp-bysize]          — Expected value for bysize basis
 *   tr > td[data-exp-equal]           — Expected value for equal basis
 *   tr > td[data-expshare-bysize]     — Expected-share % for bysize
 *   tr > td[data-expshare-equal]      — Expected-share % for equal
 *   tr > td[data-band-bysize]         — deviation band name for bysize
 *   tr > td[data-band-equal]          — deviation band name for equal
 *   tr > td[data-dev-bysize]          — deviation text for bysize
 *   tr > td[data-dev-equal]           — deviation text for equal
 *   .expected-cell                    — <span> wrapping number + .basis-tag
 *   .basis-tag                        — inline chip showing "by size"/"≈ equal"
 *   .expected-share                   — td showing expected % of total
 *   .share-fill.dev-band-*            — share-bar colour fill inside a row
 *   .dev-pill.dev-band-*              — deviation pill inside the deviation td
 *   .dev-icon                         — icon span inside .dev-pill
 *   .donut-seg.dev-band-*             — SVG circles in the share donut
 *   .eq-seg.eq-seg-*                  — spans in the distribution ribbon
 *   .dl-swatch.dev-band-*             — colour swatches in the donut legend
 *   .dl-dev.*                         — deviation text in the donut legend
 *   .gauge-seg.gauge-seg--*           — NOT swapped (gauge shows distribution,
 *                                       counts are server-computed — static)
 *   .drill-into#drillIntoCtrl         — dropdown wrapper
 *   .drill-into-btn                   — toggle button
 *   .drill-menu                       — dropdown panel (role=listbox)
 *   .drill-menu-item[role=option]     — <a> links inside menu
 *   #justice-period-form              — date range form (auto-submit on change)
 *   tr.drillable[data-drill-href]     — keyboard-navigable row links
 *   [data-groupby-toggle]             — NOT present in cshtml (skipped)
 */
(function () {
    'use strict';

    /* ─────────────────────────────────────────────────────────────
       Band icon map — must match BandIcon() in Analytics.cshtml
    ───────────────────────────────────────────────────────────── */
    var BAND_ICONS = {
        over:      '▲▲',
        softover:  '▲',
        balanced:  '■',
        softunder: '▼',
        under:     '▼▼'
    };

    /* ─────────────────────────────────────────────────────────────
       ALL band CSS suffixes (dev-band-* and eq-seg-*)
    ───────────────────────────────────────────────────────────── */
    var ALL_BANDS = ['over', 'softover', 'balanced', 'softunder', 'under', 'notarget'];

    /* ─────────────────────────────────────────────────────────────
       Utility: remove all dev-band-* classes from an element
    ───────────────────────────────────────────────────────────── */
    function clearBandClasses(el, prefix) {
        var p = prefix || 'dev-band-';
        ALL_BANDS.forEach(function (b) { el.classList.remove(p + b); });
    }

    /* ─────────────────────────────────────────────────────────────
       1. FAIRNESS-BASIS TOGGLE
    ───────────────────────────────────────────────────────────── */
    var basisSwitch = document.querySelector('[data-basis-toggle]');

    if (basisSwitch) {
        basisSwitch.querySelectorAll('.basis-opt[data-basis]').forEach(function (opt) {
            opt.addEventListener('click', function (e) {
                // Allow the <a> href fallback if JS is somehow broken; preventDefault here
                // since we handle it in JS. If the element lacks data-basis we skip.
                var basis = opt.getAttribute('data-basis');
                if (!basis) return;

                e.preventDefault();

                /* Update active state on the toggle options */
                basisSwitch.querySelectorAll('.basis-opt').forEach(function (o) {
                    o.classList.remove('active');
                    o.setAttribute('aria-pressed', 'false');
                });
                opt.classList.add('active');
                opt.setAttribute('aria-pressed', 'true');

                _applyBasis(basis);
            });
        });
    }

    /**
     * Apply a basis ("bysize" | "equal") to all data-driven cells in the table,
     * the donut slices, the distribution ribbon, and the donut legend.
     */
    function _applyBasis(basis) {
        var isBySize = (basis === 'bysize');

        /* ── Basis-tag text: "by size" (bysize) or "≈ equal" (equal) ── */
        /* We read the existing text from the active opt element itself.
           The server renders the text inside <loc> which resolves to
           the localised string. We swap between the two opt elements' texts. */
        var bysizeOpt = basisSwitch ? basisSwitch.querySelector('[data-basis="bysize"]') : null;
        var equalOpt  = basisSwitch ? basisSwitch.querySelector('[data-basis="equal"]')  : null;
        var bysizeText = bysizeOpt ? bysizeOpt.textContent.trim() : 'by size';
        var equalText  = equalOpt  ? equalOpt.textContent.trim()  : '≈ equal';
        var newTagText = isBySize ? bysizeText : equalText;

        /* ── Per-row updates ── */
        /* Each tbody row that participates carries the 6 data-* attrs on its tds.
           We iterate rows and update each cell in one pass. */
        var rows = document.querySelectorAll('table.jt tbody tr');
        rows.forEach(function (tr) {
            _updateRowBasis(tr, basis, newTagText);
        });

        /* ── Donut segments (share donut) ── */
        /* Each .donut-seg circle has a dev-band-* class derived from row order.
           The donut rows are ordered by Actual descending (server-side).
           We cannot reorder them client-side (no data), so we only recolour.
           Each donut circle is paired positionally with a tbody row.
           The donut legend .dl-swatch and .dl-dev are also paired. */
        _updateDonutByPosition(basis);

        /* ── Distribution ribbon (eq-seg spans) ── */
        /* The ribbon is sorted by band server-side; client-side we can only
           recolour each segment. Each eq-seg span maps to a tbody row by
           presence of its band class (no positional pairing is reliable).
           Instead we store the band update on each segment's title attr—
           but titles are read-only content.
           Safe approach: rebuild band from positional pairing with rows
           ordered by Actual descending (matching ribbon sort). */
        _updateRibbonByBand(basis);
    }

    /**
     * Update a single table row's Expected, Expected %, and deviation cells
     * for the given basis.
     */
    function _updateRowBasis(tr, basis, newTagText) {
        var isBySize = (basis === 'bysize');

        /* ── Expected cell ── */
        var expTd = tr.querySelector('[data-exp-bysize]');
        if (expTd) {
            var expVal = isBySize
                ? expTd.getAttribute('data-exp-bysize')
                : expTd.getAttribute('data-exp-equal');
            if (expVal !== null) {
                var expCell = expTd.querySelector('.expected-cell');
                if (expCell) {
                    /* Replace the leading text node (the number) before .basis-tag */
                    var tag = expCell.querySelector('.basis-tag');
                    if (tag) {
                        /* Set the text node that precedes the tag */
                        var firstNode = expCell.firstChild;
                        if (firstNode && firstNode.nodeType === Node.TEXT_NODE) {
                            firstNode.nodeValue = expVal + ' ';
                        } else {
                            /* No text node — insert one before tag */
                            expCell.insertBefore(document.createTextNode(expVal + ' '), tag);
                        }
                        tag.textContent = newTagText;
                    }
                }
            }
        }

        /* ── Expected % of total ── */
        var expShareTd = tr.querySelector('[data-expshare-bysize]');
        if (expShareTd) {
            var shareVal = isBySize
                ? expShareTd.getAttribute('data-expshare-bysize')
                : expShareTd.getAttribute('data-expshare-equal');
            if (shareVal !== null) {
                expShareTd.textContent = shareVal;
            }
        }

        /* ── Deviation pill ── */
        var devTd = tr.querySelector('[data-dev-bysize]');
        if (devTd) {
            var devText = isBySize
                ? devTd.getAttribute('data-dev-bysize')
                : devTd.getAttribute('data-dev-equal');
            var newBand = isBySize
                ? devTd.getAttribute('data-band-bysize')
                : devTd.getAttribute('data-band-equal');

            if (devText !== null && newBand !== null) {
                var pill = devTd.querySelector('.dev-pill');
                if (pill) {
                    /* Swap band class */
                    clearBandClasses(pill, 'dev-band-');
                    pill.classList.add('dev-band-' + newBand);

                    /* Update icon + text. Structure: <span class="dev-icon">ICON</span>TEXT */
                    var icon = pill.querySelector('.dev-icon');
                    if (icon) {
                        icon.textContent = BAND_ICONS[newBand] || '—';
                        /* Text node after icon */
                        var textNode = icon.nextSibling;
                        if (textNode && textNode.nodeType === Node.TEXT_NODE) {
                            textNode.nodeValue = devText;
                        } else {
                            pill.appendChild(document.createTextNode(devText));
                        }
                    } else {
                        /* Pill has no .dev-icon (notarget case) — leave text alone */
                    }

                    /* Update aria-label on pill if present */
                    /* We cannot re-localise aria-label without string keys, so we
                       leave it at the server-rendered value (it will become stale on
                       client-side toggle — acceptable; full accuracy requires a reload). */
                }
            }

            /* ── Share bar fill colour ── */
            /* The .share-fill lives in the % of total td (previous sibling of expTd).
               In the cshtml column order: Name | Actual | %total | Expected | ExpShare% | Dev | ...
               The % of total td holds .share-bar > .share-fill with dev-band-* class. */
            var shareFill = tr.querySelector('.share-fill');
            if (shareFill && newBand) {
                clearBandClasses(shareFill, 'dev-band-');
                shareFill.classList.add('dev-band-' + newBand);
            }
        }
    }

    /**
     * Update donut segments and legend items by position.
     * The donut renders rows ordered by Actual descending (donutRows in cshtml).
     * The legend .dl-item rows are rendered in the same order.
     * We gather the new band for each donut row from the tbody rows that
     * match by order-of-appearance in the donut (which matches
     * rows sorted by Actual descending).
     *
     * Since we cannot re-sort in JS (no Actual value exposed on donut segs),
     * we rebuild the band sequence by reading data-band-* from the tbody rows
     * sorted by their existing order, then apply positionally.
     *
     * The donut rows are sorted by Actual DESC.  The tbody rows are sorted
     * by the server's default (deviation order by default).  These orders may
     * differ.  However, the cshtml computes donutRows = v.Rows.OrderByDescending(r => r.Actual).
     * We cannot reconstruct that sort from the DOM without the Actual values
     * (which ARE present as the 2nd <td> in each row).
     */
    function _updateDonutByPosition(basis) {
        var isBySize = (basis === 'bysize');

        /* Build (actual, band) pairs from tbody rows */
        var rowData = [];
        document.querySelectorAll('table.jt tbody tr').forEach(function (tr) {
            var devTd   = tr.querySelector('[data-band-bysize]');
            var actualTd = tr.querySelectorAll('td')[1]; /* 2nd td = Actual */
            if (!devTd || !actualTd) return;
            var band    = isBySize
                ? devTd.getAttribute('data-band-bysize')
                : devTd.getAttribute('data-band-equal');
            var actual  = parseFloat(actualTd.textContent.trim());
            rowData.push({ band: band, actual: isNaN(actual) ? 0 : actual });
        });

        /* Sort by Actual DESC — mirrors the donut render order */
        rowData.sort(function (a, b) { return b.actual - a.actual; });

        /* Apply to donut segments (.donut-seg circles) */
        var donutSegs = document.querySelectorAll('.donut-seg');
        donutSegs.forEach(function (seg, i) {
            if (i >= rowData.length) return;
            clearBandClasses(seg, 'dev-band-');
            seg.classList.add('dev-band-' + rowData[i].band);
        });

        /* Apply to donut legend swatches (.dl-swatch) and deviation text (.dl-dev) */
        var dlSwatches = document.querySelectorAll('.donut-legend .dl-swatch');
        var dlDevs     = document.querySelectorAll('.donut-legend .dl-dev');
        dlSwatches.forEach(function (swatch, i) {
            if (i >= rowData.length) return;
            clearBandClasses(swatch, 'dev-band-');
            swatch.classList.add('dev-band-' + rowData[i].band);
        });
        dlDevs.forEach(function (devEl, i) {
            if (i >= rowData.length) return;
            /* Remove old band class and apply new one (dl-dev uses band name directly) */
            ALL_BANDS.forEach(function (b) { devEl.classList.remove(b); });
            devEl.classList.add(rowData[i].band);
        });
    }

    /**
     * Update equity ribbon segments.
     * The ribbon is sorted by band (OrderByDescending(r => r.Band/BandEqual)).
     * We cannot reorder segments client-side (no data).
     * We rebuild the new band sequence by reading from tbody rows,
     * sorting by the appropriate band enum order (Over > SoftOver > Balanced > SoftUnder > Under).
     */
    function _updateRibbonByBand(basis) {
        var isBySize = (basis === 'bysize');

        var BAND_ORDER = { over: 5, softover: 4, balanced: 3, softunder: 2, under: 1 };

        /* Build (actual, newBand) pairs from tbody rows */
        var rowData = [];
        document.querySelectorAll('table.jt tbody tr').forEach(function (tr) {
            var devTd   = tr.querySelector('[data-band-bysize]');
            var actualTd = tr.querySelectorAll('td')[1];
            if (!devTd || !actualTd) return;
            var newBand = isBySize
                ? devTd.getAttribute('data-band-bysize')
                : devTd.getAttribute('data-band-equal');
            var actual  = parseFloat(actualTd.textContent.trim());
            rowData.push({ band: newBand, actual: isNaN(actual) ? 0 : actual });
        });

        /* Sort by band descending (matches cshtml: OrderByDescending(r => r.BandEqual)) */
        rowData.sort(function (a, b) {
            var oa = BAND_ORDER[a.band] || 0;
            var ob = BAND_ORDER[b.band] || 0;
            return ob - oa;
        });

        /* Apply to eq-seg spans in the ribbon */
        var segs = document.querySelectorAll('.equity-ribbon .eq-seg');
        segs.forEach(function (seg, i) {
            if (i >= rowData.length) return;
            /* Remove old eq-seg-* class */
            ALL_BANDS.forEach(function (b) { seg.classList.remove('eq-seg-' + b); });
            seg.classList.add('eq-seg-' + rowData[i].band);
        });
    }

    /* ─────────────────────────────────────────────────────────────
       2. DRILL-INTO DROPDOWN
       The cshtml renders an inline onclick on .drill-into-btn for
       open/close, but we enhance it properly here by replacing that
       with delegated event management.
    ───────────────────────────────────────────────────────────── */
    var drillWrap = document.getElementById('drillIntoCtrl');

    if (drillWrap) {
        var drillBtn  = drillWrap.querySelector('.drill-into-btn');
        var drillMenu = drillWrap.querySelector('.drill-menu');

        function _openDrill() {
            drillWrap.classList.add('open');
            drillBtn.setAttribute('aria-expanded', 'true');
            /* Focus first focusable item */
            var firstItem = drillMenu ? drillMenu.querySelector('.drill-menu-item[role="option"]') : null;
            if (firstItem) firstItem.focus();
        }

        function _closeDrill(returnFocus) {
            drillWrap.classList.remove('open');
            drillBtn.setAttribute('aria-expanded', 'false');
            if (returnFocus && drillBtn) drillBtn.focus();
        }

        function _isDrillOpen() {
            return drillWrap.classList.contains('open');
        }

        if (drillBtn) {
            /* Override the inline onclick (it will still fire, but we preventDefault
               on clicks so the inline onclick's classList.toggle still happens.
               Safest: remove the inline handler by replacing it. */
            drillBtn.removeAttribute('onclick');

            drillBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                if (_isDrillOpen()) {
                    _closeDrill(false);
                } else {
                    _openDrill();
                }
            });
        }

        /* Items are real <a> links — navigation on click is native.
           We only close the menu so the page doesn't stay open after navigation. */
        if (drillMenu) {
            drillMenu.querySelectorAll('.drill-menu-item[role="option"]').forEach(function (item) {
                item.addEventListener('click', function () {
                    /* Close immediately; the <a> href will navigate */
                    _closeDrill(false);
                });
            });
        }

        /* Arrow-key navigation inside the menu */
        drillWrap.addEventListener('keydown', function (e) {
            if (!_isDrillOpen()) {
                /* Open on arrow-down when closed */
                if (e.key === 'ArrowDown') {
                    e.preventDefault();
                    _openDrill();
                }
                return;
            }

            var items = drillMenu
                ? Array.prototype.slice.call(
                    drillMenu.querySelectorAll('.drill-menu-item[role="option"]'))
                : [];
            var active = document.activeElement;
            var idx    = items.indexOf(active);

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                var next = idx < items.length - 1 ? items[idx + 1] : items[0];
                if (next) next.focus();
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                var prev = idx > 0 ? items[idx - 1] : items[items.length - 1];
                if (prev) prev.focus();
            } else if (e.key === 'Escape') {
                e.preventDefault();
                _closeDrill(true);
            } else if (e.key === 'Tab') {
                /* Let Tab close the menu naturally without trapping focus */
                _closeDrill(false);
            }
        });

        /* Outside-click: close */
        document.addEventListener('click', function (e) {
            if (_isDrillOpen() && !drillWrap.contains(e.target)) {
                _closeDrill(false);
            }
        });

        /* Escape anywhere on page */
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && _isDrillOpen()) {
                _closeDrill(true);
            }
        });
    }

    /* ─────────────────────────────────────────────────────────────
       3. PERIOD DATE INPUTS — auto-submit on change
       #justice-period-form contains two <input type="date">.
       We auto-submit when both have values (so partial edits don't fire).
    ───────────────────────────────────────────────────────────── */
    var periodForm = document.getElementById('justice-period-form');
    if (periodForm) {
        periodForm.querySelectorAll('input[type="date"]').forEach(function (dateInput) {
            dateInput.addEventListener('change', function () {
                /* Only submit if both date inputs have values */
                var allFilled = true;
                periodForm.querySelectorAll('input[type="date"]').forEach(function (d) {
                    if (!d.value) allFilled = false;
                });
                if (allFilled) {
                    periodForm.submit();
                }
            });
        });
    }

    /* A/B compare form — same auto-submit behaviour */
    var abForm = document.getElementById('justice-ab-form');
    if (abForm) {
        abForm.querySelectorAll('input[type="date"]').forEach(function (dateInput) {
            dateInput.addEventListener('change', function () {
                var allFilled = true;
                abForm.querySelectorAll('input[type="date"]').forEach(function (d) {
                    if (!d.value) allFilled = false;
                });
                if (allFilled) {
                    abForm.submit();
                }
            });
        });
    }

    /* ─────────────────────────────────────────────────────────────
       4. DRILLABLE TABLE ROWS — keyboard Enter/Space navigation
    ───────────────────────────────────────────────────────────── */
    document.querySelectorAll('tr.drillable[data-drill-href]').forEach(function (tr) {
        tr.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                var href = tr.getAttribute('data-drill-href');
                if (href) {
                    window.location.href = href;
                }
            }
        });
    });

    /* ─────────────────────────────────────────────────────────────
       5. GROUP-BY TOGGLE — NOT PRESENT
       The cshtml does not emit [data-groupby-toggle] for any level.
       Feature skipped. See report.
    ───────────────────────────────────────────────────────────── */

})();
