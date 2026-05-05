// =====================================================================
// Justice Panel — slide-in drawer for the calendar pages (Phase 2).
//
// Vanilla JS. No framework. Lazy-loads the drawer payload on first open.
//
// Phase 2a: drawer open / close / fetch verdict + holes + ribbon.
// Phase 2b: hole-click → fetch ranked candidates from `?handler=JusticeEligibility`.
// Phase 2c: candidate-click → fetch impact preview from `?handler=JusticePreview`,
//           then "Make it real" routes to the existing assign endpoints.
// Phase 2d: branches "Make it real" by data-justice-kind so chore/onduty calendars
//           reuse the same flow when they expose holes.
// =====================================================================

(function () {
    'use strict';

    /** @type {HTMLElement|null} */
    var drawer = null;
    /** @type {string|null} */
    var endpoint = null;
    /** @type {string|null} */
    var calendarKind = null;
    /** @type {boolean} */
    var hasLoaded = false;
    /** @type {boolean} */
    var isLoading = false;
    /**
     * Candidate fetch state — populated by openForHole. Drives previewImpact + makeItReal.
     * Phase 2d: extended with `moleculeId` (required for chore eligibility + onduty token canonical)
     * and `dutyTypeValue` (required for onduty preview + Make it real).
     * @type {{shiftInstanceId:number, shiftTypeId:number, date:string, kind:string, moleculeId:number|null, dutyTypeValue:number|null}|null}
     */
    var holeContext = null;
    /**
     * Currently-expanded candidate row inside the candidate list — at most one open at a time.
     * @type {HTMLElement|null}
     */
    var expandedRow = null;
    /** Phase 2d: chore-only form state. Captured in the candidates view's chore form. */
    var choreTitle = '';
    var choreTypeId = null;

    function $(sel, root) {
        return (root || document).querySelector(sel);
    }
    function $all(sel, root) {
        return Array.prototype.slice.call((root || document).querySelectorAll(sel));
    }

    /**
     * Look up a localized string from the page's window.AppLocalizer dictionary
     * (populated by _LocalizationScript.cshtml). Falls back to the supplied default
     * when the page didn't include that script or the key is missing — this keeps
     * the drawer usable on stripped-down preview pages while still routing through
     * the canonical resx files in production.
     */
    function loc(key, fallback) {
        if (window.AppLocalizer && typeof window.AppLocalizer[key] === 'string') {
            return window.AppLocalizer[key];
        }
        return fallback;
    }

    function setHidden(el, hidden) {
        if (!el) return;
        if (hidden) el.setAttribute('hidden', '');
        else el.removeAttribute('hidden');
    }

    function open() {
        if (!drawer) return;
        drawer.classList.add('justice-drawer--open');
        drawer.removeAttribute('hidden');
        drawer.setAttribute('aria-hidden', 'false');
        var trigger = $('[data-justice-trigger]');
        if (trigger) trigger.setAttribute('aria-expanded', 'true');

        if (!hasLoaded && !isLoading) {
            fetchPayload();
        }
    }

    function close() {
        if (!drawer) return;
        drawer.classList.remove('justice-drawer--open');
        drawer.setAttribute('aria-hidden', 'true');
        var trigger = $('[data-justice-trigger]');
        if (trigger) trigger.setAttribute('aria-expanded', 'false');
        clearActiveHoleHighlight();
        // Phase 2d: clear chore-hole markers when drawer closes — they only make sense while
        // the drawer's focus list is visible, since they reflect that ranking.
        activateChoreHoles([]);
        // Always return to main view when closing — next open shouldn't surprise with stale candidates.
        showMainView();
    }

    function showLoading() {
        setHidden($('[data-justice-loading]', drawer), false);
        setHidden($('[data-justice-error]', drawer), true);
        setHidden($('[data-justice-content]', drawer), true);
        setHidden($('[data-justice-candidates-view]', drawer), true);
    }
    function showError(msg) {
        setHidden($('[data-justice-loading]', drawer), true);
        setHidden($('[data-justice-content]', drawer), true);
        setHidden($('[data-justice-candidates-view]', drawer), true);
        var errEl = $('[data-justice-error]', drawer);
        var msgEl = $('[data-justice-error-msg]', drawer);
        if (msgEl) msgEl.textContent = msg || 'Error';
        setHidden(errEl, false);
    }
    function showMainView() {
        setHidden($('[data-justice-loading]', drawer), true);
        setHidden($('[data-justice-error]', drawer), true);
        setHidden($('[data-justice-content]', drawer), false);
        setHidden($('[data-justice-candidates-view]', drawer), true);
    }
    function showCandidatesView() {
        setHidden($('[data-justice-loading]', drawer), true);
        setHidden($('[data-justice-error]', drawer), true);
        setHidden($('[data-justice-content]', drawer), true);
        setHidden($('[data-justice-candidates-view]', drawer), false);
    }

    function fetchPayload() {
        if (!endpoint) {
            showError('No data endpoint configured');
            return;
        }
        isLoading = true;
        showLoading();
        fetch(endpoint, {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        }).then(function (resp) {
            if (!resp.ok) throw new Error('HTTP ' + resp.status);
            return resp.json();
        }).then(function (data) {
            renderPayload(data);
            hasLoaded = true;
            isLoading = false;
            showMainView();
        }).catch(function (err) {
            isLoading = false;
            console.error('[Justice] fetch failed', err);
            showError(loc('Justice_Panel_LoadFailed', 'Couldn’t load Justice data.'));
        });
    }

    /** Refresh the main payload — used after "Make it real" succeeds so verdict reflects the new state. */
    function refreshPayload() {
        hasLoaded = false;
        fetchPayload();
    }

    function renderPayload(data) {
        if (!data) return;
        renderScopeChip(data);
        renderVerdict(data);
        renderCallouts(data);
        renderEquityRibbon(data);
        renderFocusList(data);
        renderFullViewLink(data);
    }

    function renderScopeChip(data) {
        var el = $('[data-justice-scope-chip]', drawer);
        if (!el) return;
        var chips = [];
        var q = data.query || {};
        if (q.scope) chips.push('<span class="scope-chip">' + escape(q.scope) + '</span>');
        if (q.workType) chips.push('<span class="scope-chip">' + escape(q.workType) + '</span>');
        if (q.periodStart && q.periodEnd) {
            chips.push('<span class="scope-chip" dir="ltr">' + escape(q.periodStart) + ' — ' + escape(q.periodEnd) + '</span>');
        }
        el.innerHTML = chips.join(' ');
    }

    function renderVerdict(data) {
        var num = $('[data-justice-spread-num]', drawer);
        if (num) num.textContent = (data.spreadIndex != null) ? Number(data.spreadIndex).toFixed(2) : '—';

        var glyph = $('[data-justice-spread-glyph]', drawer);
        if (glyph) {
            var sev = Number(data.spreadSeverity || 0);
            glyph.setAttribute('data-severity', String(sev));
            var dots = glyph.querySelectorAll('.dot');
            dots.forEach(function (d, i) {
                if (i <= sev) d.classList.add('dot--filled');
                else d.classList.remove('dot--filled');
            });
        }

        var label = $('[data-justice-spread-label]', drawer);
        if (label) label.textContent = data.spreadLabel || '';
    }

    function renderCallouts(data) {
        var over = $('[data-justice-most-over]', drawer);
        var under = $('[data-justice-most-under]', drawer);
        if (over) over.innerHTML = data.mostOver
            ? escape(data.mostOver.name) + ' <span dir="ltr">(' + formatDeviation(data.mostOver.deviationPercent) + ')</span>'
            : '<span class="text-subtle">' + escape(data.noneLabel || 'None') + '</span>';
        if (under) under.innerHTML = data.mostUnder
            ? escape(data.mostUnder.name) + ' <span dir="ltr">(' + formatDeviation(data.mostUnder.deviationPercent) + ')</span>'
            : '<span class="text-subtle">' + escape(data.noneLabel || 'None') + '</span>';
    }

    function renderEquityRibbon(data) {
        var el = $('[data-justice-equity-ribbon]', drawer);
        if (!el) return;
        var rows = data.rows || [];
        if (rows.length === 0) { el.innerHTML = ''; return; }
        var max = Number(data.maxRibbonValue || 1);
        var html = rows.map(function (r) {
            var weight = Math.max(1, Number(r.actual) || 0);
            var band = (r.band || 'NoTarget').toLowerCase().replace(/_/g, '');
            var name = (r.name || '') + ' ' + (r.actual || 0);
            return '<span class="equity-segment dev-band-' + escape(band) +
                   '" style="flex: ' + weight + '" title="' + escape(name) + '"></span>';
        }).join('');
        el.innerHTML = html;
        void max;
    }

    function renderFocusList(data) {
        var holes = data.whereToFocus || [];
        var list = $('[data-justice-focus-list]', drawer);
        var count = $('[data-justice-focus-count]', drawer);
        if (count) count.textContent = String(holes.length);
        if (!list) return;

        if (holes.length === 0) {
            list.innerHTML = '<li class="justice-focus-list__empty">' +
                '<div>✓</div>' +
                '<div>' + escape(data.noHolesLabel || 'No holes in this scope.') + '</div>' +
                '</li>';
            return;
        }

        list.innerHTML = holes.map(function (h) {
            // Phase 2d: each focus item carries enough info to drive openForHole() directly.
            // For shift kind, shiftInstanceId is sufficient (existing path). For chore/onduty,
            // we construct a rowId in the same prefix format the calendar grid uses so the
            // server's TryParseRowId helper accepts it.
            var rowId = h.kind === 'onduty' ? 'dutytype-' + h.dutyTypeValue
                      : h.kind === 'chore'  ? 'user-' + h.userId
                      : '';
            return '<li>' +
                '<button type="button" class="justice-focus-list__item" ' +
                'data-justice-hole-shift-instance="' + escape(String(h.shiftInstanceId || '')) + '" ' +
                'data-justice-hole-date="' + escape(String(h.date || '')) + '" ' +
                'data-justice-hole-kind="' + escape(h.kind) + '" ' +
                'data-justice-hole-rowid="' + escape(rowId) + '" ' +
                'data-justice-hole-molecule-id="' + escape(String(h.moleculeId != null ? h.moleculeId : '')) + '" ' +
                'data-justice-hole-duty-type="' + escape(String(h.dutyTypeValue != null ? h.dutyTypeValue : '')) + '">' +
                '<span class="justice-focus-list__label">' + escape(h.label) + '</span>' +
                '<span class="justice-focus-list__deficit" dir="ltr">' + escape(String(h.deficit)) + '</span>' +
                '</button>' +
                '</li>';
        }).join('');
        // Phase 2d: activate chore-hole markers on the calendar grid for cells that match
        // ranked focus items. This replaces the noisy "every empty cell is a hole" approach.
        activateChoreHoles(holes);
    }

    function renderFullViewLink(data) {
        var link = $('[data-justice-full-view]', drawer);
        if (!link) return;
        if (data.fullViewUrl) {
            link.setAttribute('href', data.fullViewUrl);
            setHidden(link, false);
        } else {
            setHidden(link, true);
        }
    }

    // -----------------------------------------------------------------
    // Phase 2b — eligibility ranking
    // -----------------------------------------------------------------

    /**
     * Open the candidates view for the given hole. `rowId` is the data-row-id from the
     * calendar cell (raw value, including any prefix like "user-42" or "dutytype-0").
     * The backend strips the prefix and resolves the appropriate entity.
     */
    function openForHole(rowId, date) {
        if (!drawer) return;
        // Phase 2d: reset chore-form state so a stale title from a previous hole can't slip through.
        choreTitle = '';
        choreTypeId = null;
        if (!drawer.classList.contains('justice-drawer--open')) open();

        // Highlight the originating cell so the user can correlate drawer ↔ calendar.
        clearActiveHoleHighlight();
        var srcCell = document.querySelector('[data-row-id="' + cssEscape(String(rowId)) + '"][data-date="' + cssEscape(String(date)) + '"]');
        if (srcCell) srcCell.classList.add('js-hole-active');

        var url = buildEligibilityUrl(rowId, date);
        if (!url) {
            showError('No eligibility endpoint');
            return;
        }
        showLoading();
        fetch(url, {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        }).then(function (resp) {
            if (!resp.ok) throw new Error('HTTP ' + resp.status);
            return resp.json();
        }).then(function (data) {
            if (data && data.error) {
                // No instance / no scope → fall back to main view with a banner.
                showMainView();
                return;
            }
            holeContext = {
                shiftInstanceId: Number(data.shiftInstanceId || 0),
                shiftTypeId: Number(data.shiftTypeId || 0),
                date: data.date,
                kind: data.kind || 'shift',
                moleculeId: data.moleculeId != null ? Number(data.moleculeId) : null,
                dutyTypeValue: data.dutyTypeValue != null ? Number(data.dutyTypeValue) : null
            };
            renderCandidates(data);
            showCandidatesView();
            showChoreFormIfNeeded();
        }).catch(function (err) {
            console.error('[Justice] eligibility fetch failed', err);
            showError(loc('Justice_Panel_CandidatesLoadFailed', 'Couldn’t load candidates.'));
        });
    }

    function buildEligibilityUrl(rowId, date) {
        if (!endpoint) return null;
        // Replace handler=Justice → handler=JusticeEligibility, preserve the rest.
        var base = endpoint.replace(/handler=Justice(?!Eligibility|Preview)/, 'handler=JusticeEligibility');
        if (base === endpoint) base += (base.indexOf('?') >= 0 ? '&' : '?') + 'handler=JusticeEligibility';
        return base + '&rowId=' + encodeURIComponent(String(rowId)) + '&date=' + encodeURIComponent(String(date));
    }

    /**
     * Phase 2d: builds the JusticePreview URL with kind-specific parameters.
     * - shift:  ?userId=&date=&shiftInstanceId=
     * - chore:  ?userId=&date=             (server derives moleculeId from page binding)
     * - onduty: ?userId=&date=&dutyTypeValue=
     */
    function buildPreviewUrl(userId, ctx) {
        if (!endpoint || !ctx) return null;
        var base = endpoint.replace(/handler=Justice(?!Eligibility|Preview)/, 'handler=JusticePreview');
        if (base === endpoint) base += (base.indexOf('?') >= 0 ? '&' : '?') + 'handler=JusticePreview';
        var qp = '&userId=' + encodeURIComponent(String(userId)) + '&date=' + encodeURIComponent(String(ctx.date));
        if (ctx.kind === 'shift' && ctx.shiftInstanceId) {
            qp += '&shiftInstanceId=' + encodeURIComponent(String(ctx.shiftInstanceId));
        }
        if (ctx.kind === 'onduty' && ctx.dutyTypeValue != null) {
            qp += '&dutyTypeValue=' + encodeURIComponent(String(ctx.dutyTypeValue));
        }
        return base + qp;
    }

    function renderCandidates(data) {
        var view = $('[data-justice-candidates-view]', drawer);
        if (!view) return;

        var headerLabel = $('[data-justice-candidates-header-label]', view);
        if (headerLabel) {
            // Date label kept LTR so e.g. "2026-05-08" doesn't reverse in Hebrew.
            headerLabel.innerHTML = '<span dir="ltr">' + escape(data.date || '') + '</span>';
        }

        var list = $('[data-justice-candidates-list]', view);
        if (!list) return;

        var candidates = data.candidates || [];
        var blocked = data.hardBlocked || [];

        if (candidates.length === 0 && blocked.length === 0) {
            list.innerHTML = '<li class="justice-candidates__empty">' +
                escape(loc('Justice_Panel_NoCandidates', 'No eligible candidates.')) + '</li>';
            return;
        }

        var html = candidates.map(function (c) { return candidateRowHtml(c, false); }).join('');
        if (blocked.length > 0) {
            html += '<li class="justice-candidates__divider">— ' +
                escape(loc('Justice_Panel_Blocked', 'Blocked')) + ' —</li>';
            html += blocked.map(function (c) { return candidateRowHtml(c, true); }).join('');
        }
        list.innerHTML = html;
    }

    function candidateRowHtml(c, isBlocked) {
        var dev = formatDeviation(c.deviationPercent);
        var band = (c.band || 'NoTarget').toLowerCase().replace(/_/g, '');
        var avatarHtml = c.avatarUrl
            ? '<img class="justice-candidate__avatar" src="' + escape(c.avatarUrl) + '" alt="" />'
            : '<span class="justice-candidate__avatar justice-candidate__avatar--initials">' + escape(initialsOf(c.displayName)) + '</span>';

        var statusGlyph = isBlocked
            ? '<span class="justice-candidate__status justice-candidate__status--blocked" title="' + escape(c.hardBlockReason || '') + '">⊘</span>'
            : ((c.warnings && c.warnings.length > 0)
                ? '<span class="justice-candidate__status justice-candidate__status--warn" title="' + escape((c.warnings || []).join(', ')) + '">⚠</span>'
                : '<span class="justice-candidate__status justice-candidate__status--ok">✓</span>');

        return '<li class="justice-candidate ' + (isBlocked ? 'justice-candidate--blocked' : '') + '" ' +
               'data-justice-candidate-user="' + escape(String(c.userId)) + '" ' +
               'data-justice-candidate-blocked="' + (isBlocked ? '1' : '0') + '">' +
            '<button type="button" class="justice-candidate__row" ' +
            (isBlocked ? 'disabled aria-disabled="true"' : '') + '>' +
                avatarHtml +
                '<span class="justice-candidate__main">' +
                    '<span class="justice-candidate__name">' + escape(c.displayName || '') + '</span>' +
                    '<span class="justice-candidate__meta">' +
                        '<span class="justice-candidate__pill dev-band-' + escape(band) + '" dir="ltr">' + dev + '</span>' +
                        statusGlyph +
                    '</span>' +
                '</span>' +
            '</button>' +
            '<div class="justice-candidate__preview" data-justice-preview hidden></div>' +
        '</li>';
    }

    // -----------------------------------------------------------------
    // Phase 2c — what-if preview + Make it real
    // -----------------------------------------------------------------

    function previewImpact(userId, rowEl) {
        if (!holeContext) return;
        if (expandedRow && expandedRow !== rowEl) {
            // Collapse the previously-expanded preview before opening a new one.
            collapsePreview(expandedRow);
        }
        expandedRow = rowEl;

        var pane = $('[data-justice-preview]', rowEl);
        if (!pane) return;
        pane.removeAttribute('hidden');
        pane.innerHTML = '<div class="justice-preview__loading">…</div>';

        var url = buildPreviewUrl(userId, holeContext);
        if (!url) { pane.innerHTML = '<div class="justice-preview__error">No preview endpoint</div>'; return; }

        fetch(url, {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        }).then(function (resp) {
            if (!resp.ok) throw new Error('HTTP ' + resp.status);
            return resp.json();
        }).then(function (data) {
            renderPreview(pane, userId, data);
        }).catch(function (err) {
            console.error('[Justice] preview fetch failed', err);
            pane.innerHTML = '<div class="justice-preview__error">' +
                escape(loc('Justice_Panel_PreviewLoadFailed', 'Couldn’t compute preview.')) + '</div>';
        });
    }

    function collapsePreview(rowEl) {
        var pane = $('[data-justice-preview]', rowEl);
        if (pane) pane.setAttribute('hidden', '');
        if (expandedRow === rowEl) expandedRow = null;
    }

    function renderPreview(pane, userId, data) {
        if (!data || data.error) {
            pane.innerHTML = '<div class="justice-preview__error">' +
                escape((data && data.error) || loc('Justice_Panel_PreviewLoadFailed', 'Preview unavailable')) + '</div>';
            return;
        }
        var sBefore = Number(data.spreadIndexBefore || 0);
        var sAfter = Number(data.spreadIndexAfter || 0);
        var diff = sAfter - sBefore;
        var diffSign = diff < 0 ? 'down' : (diff > 0 ? 'up' : 'flat');
        var diffLabel = diff < 0 ? '↓ ' : (diff > 0 ? '↑ ' : '');
        var diffWord = diff < 0
            ? loc('Justice_Panel_Better', 'better')
            : (diff > 0
                ? loc('Justice_Panel_Worse', 'worse')
                : loc('Justice_Panel_Unchanged', 'unchanged'));

        var actualBefore = Number(data.candidateActualBefore || 0);
        var actualAfter = Number(data.candidateActualAfter || 0);
        var devBefore = formatDeviation(data.candidateDeviationBefore);
        var devAfter = formatDeviation(data.candidateDeviationAfter);

        // Phase 2d: chore CTA is disabled until the title input has a non-empty value.
        // Shifts/onduty have no such gate (they don't need a user-supplied title).
        var choreBlocked = holeContext && holeContext.kind === 'chore' && !choreTitle.trim();
        var disabledAttrs = choreBlocked ? ' disabled aria-disabled="true"' : '';

        pane.innerHTML =
            '<div class="justice-preview">' +
                '<div class="justice-preview__row">' +
                    '<span class="justice-preview__label">' + escape(loc('Justice_Panel_Spread', 'Spread')) + '</span>' +
                    '<span class="justice-preview__value" dir="ltr">' +
                        sBefore.toFixed(2) + ' → ' + sAfter.toFixed(2) +
                        ' <span class="justice-preview__diff justice-preview__diff--' + diffSign + '">' + diffLabel + escape(diffWord) + '</span>' +
                    '</span>' +
                '</div>' +
                '<div class="justice-preview__row">' +
                    '<span class="justice-preview__label">' + escape(loc('Justice_Panel_Actual', 'Actual')) + '</span>' +
                    '<span class="justice-preview__value" dir="ltr">' + actualBefore + ' → ' + actualAfter +
                    ' <span class="justice-preview__pill" dir="ltr">' + devBefore + ' → ' + devAfter + '</span></span>' +
                '</div>' +
                '<div class="justice-preview__cta">' +
                    '<button type="button" class="btn btn-primary"' + disabledAttrs +
                    ' data-justice-make-real data-justice-user="' + escape(String(userId)) + '">' +
                        '<span aria-hidden="true">➜</span> ' + escape(loc('Justice_Panel_MakeItReal', 'Make it real')) +
                    '</button>' +
                '</div>' +
            '</div>';
    }

    /**
     * Phase 2d: Branch by calendarKind. All three kinds delegate to existing global quick-add
     * helpers (calendar-inline-edit.js) so notifications, override flow, and audit logs match
     * the rest of the calendar UI exactly — Justice never becomes a parallel write path.
     *
     * Double-submit protection: disable all "Make it real" buttons for the same user before
     * dispatching; re-enable on .catch() since the success path triggers a full page reload.
     */
    function makeItReal(userId) {
        if (!holeContext) return;

        var allMakeBtns = $all('[data-justice-make-real][data-justice-user="' + escape(String(userId)) + '"]', drawer);
        allMakeBtns.forEach(function (btn) {
            btn.disabled = true;
            btn.setAttribute('aria-disabled', 'true');
        });
        function reEnableMakeBtns() {
            allMakeBtns.forEach(function (btn) {
                btn.disabled = false;
                btn.removeAttribute('aria-disabled');
            });
        }

        if (calendarKind === 'shifts' && typeof window.quickAddShift === 'function') {
            window.quickAddShift(holeContext.shiftTypeId, holeContext.date, userId)
                .then(function () { refreshPayload(); showMainView(); })
                .catch(function () { reEnableMakeBtns(); });
            return;
        }

        if (calendarKind === 'onduty' && typeof window.quickAddOnDuty === 'function') {
            if (holeContext.dutyTypeValue == null) {
                reEnableMakeBtns();
                showError(loc('Justice_Panel_MissingDutyType', 'Duty type unavailable — open this cell on the calendar.'));
                return;
            }
            // Pass holeContext.moleculeId so the override-token canonical at QuickAddOnDuty
            // matches what Justice's eligibility handler signed.
            window.quickAddOnDuty(holeContext.date, userId, holeContext.dutyTypeValue, holeContext.moleculeId)
                .then(function () { refreshPayload(); showMainView(); })
                .catch(function () { reEnableMakeBtns(); });
            return;
        }

        if (calendarKind === 'chores' && typeof window.quickAddChore === 'function') {
            var title = choreTitle.trim();
            if (!title) {
                // Defense-in-depth — the button should be disabled when title is empty.
                reEnableMakeBtns();
                return;
            }
            window.quickAddChore(holeContext.date, userId, title, choreTypeId || null)
                .then(function () { refreshPayload(); showMainView(); })
                .catch(function () { reEnableMakeBtns(); });
            return;
        }

        // Unknown calendar kind — re-enable so the user can retry.
        reEnableMakeBtns();
    }

    // -----------------------------------------------------------------
    // Phase 2d — chore form (type select + title input)
    // -----------------------------------------------------------------

    /** Show the chore form when current hole is a chore; otherwise hide it. */
    function showChoreFormIfNeeded() {
        var form = $('[data-justice-chore-form]', drawer);
        if (!form) return;
        if (!holeContext || holeContext.kind !== 'chore') {
            setHidden(form, true);
            return;
        }

        // Populate chore-type <select> by cloning options from the page's hidden quickentry select.
        // The quickentry select carries molecule-scoped chore types — the same set users see in the
        // bottom-sheet chore creation flow. Idempotent: only populate once per drawer lifetime.
        var typeSelect = $('[data-justice-chore-type-select]', form);
        if (typeSelect && typeSelect.options.length <= 1) {
            var srcSelect = document.getElementById('choreTypeSelect-quickentry');
            if (srcSelect) {
                Array.prototype.slice.call(srcSelect.options).forEach(function (opt) {
                    if (!opt.value) return;  // skip the empty placeholder option
                    var o = document.createElement('option');
                    o.value = opt.value;
                    o.textContent = opt.textContent;
                    typeSelect.appendChild(o);
                });
            }
        }

        // Wire change/input handlers idempotently — flag instances to avoid duplicate listeners.
        if (typeSelect && !typeSelect._justiceWired) {
            typeSelect._justiceWired = true;
            typeSelect.addEventListener('change', function () {
                choreTypeId = typeSelect.value ? parseInt(typeSelect.value, 10) : null;
                var ti = $('[data-justice-chore-title-input]', form);
                if (ti) {
                    var selectedText = typeSelect.options[typeSelect.selectedIndex]
                        ? typeSelect.options[typeSelect.selectedIndex].textContent.trim() : '';
                    // Auto-fill title from selected type — same pattern the bottom sheet uses.
                    ti.value = selectedText;
                    choreTitle = selectedText;
                    syncMakeRealButtonState();
                }
            });
        }

        var titleInput = $('[data-justice-chore-title-input]', form);
        if (titleInput) {
            // Always reset the input value when the form is freshly shown.
            titleInput.value = '';
            if (!titleInput._justiceWired) {
                titleInput._justiceWired = true;
                titleInput.addEventListener('input', function () {
                    choreTitle = titleInput.value;
                    syncMakeRealButtonState();
                });
            }
        }

        // Reset the type select to "no type" each time we show the form for a new hole.
        if (typeSelect) typeSelect.selectedIndex = 0;

        setHidden(form, false);
    }

    /** Reflect choreTitle non-empty state on every Make-it-real button currently rendered. */
    function syncMakeRealButtonState() {
        if (!holeContext || holeContext.kind !== 'chore') return;
        var blocked = !choreTitle.trim();
        $all('[data-justice-make-real]', drawer).forEach(function (btn) {
            btn.disabled = blocked;
            if (blocked) btn.setAttribute('aria-disabled', 'true');
            else btn.removeAttribute('aria-disabled');
        });
    }

    /**
     * Phase 2d: promote chore-eligible cells to active "hole" state for the cells matching
     * the backend's whereToFocus list. Called after every `renderFocusList` to keep the
     * calendar's chore markers consistent with the drawer's ranked suggestions. This is the
     * fix for the visual-noise problem (every empty cell would otherwise be marked).
     */
    function activateChoreHoles(holes) {
        // Reset any previously-active chore hole markers.
        $all('[data-justice-row-kind="chore"][data-justice-hole="1"]').forEach(function (el) {
            el.setAttribute('data-justice-hole', '0');
        });
        if (!holes || holes.length === 0) return;
        holes.forEach(function (h) {
            if (h.kind !== 'chore' || h.userId == null) return;
            var rowId = 'user-' + h.userId;
            var cell = document.querySelector(
                '[data-row-id="' + cssEscape(rowId) + '"][data-date="' + cssEscape(h.date) + '"][data-justice-cell-eligible="1"]'
            );
            if (cell) cell.setAttribute('data-justice-hole', '1');
        });
    }

    function clearActiveHoleHighlight() {
        $all('.js-hole-active').forEach(function (el) { el.classList.remove('js-hole-active'); });
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    function formatDeviation(pct) {
        if (pct == null) return '—';
        var n = Number(pct);
        if (!isFinite(n)) return '—';
        var sign = n > 0 ? '+' : '';
        return sign + Math.round(n) + '%';
    }

    function initialsOf(name) {
        if (!name) return '?';
        var parts = String(name).trim().split(/\s+/).slice(0, 2);
        return parts.map(function (p) { return p.charAt(0).toUpperCase(); }).join('');
    }

    function escape(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    /**
     * CSS.escape polyfill subset — handles digits / dots / hyphens common in our row ids
     * and dates. Falls back to native CSS.escape when available.
     */
    function cssEscape(s) {
        if (typeof window.CSS !== 'undefined' && typeof window.CSS.escape === 'function') {
            return window.CSS.escape(s);
        }
        return String(s).replace(/([\\#.\[\]()*+?,'":!=<>|/])/g, '\\$1');
    }

    function init() {
        var trigger = document.querySelector('[data-justice-trigger]');
        drawer = document.querySelector('[data-justice-drawer]');
        if (!trigger || !drawer) return;

        endpoint = trigger.getAttribute('data-justice-endpoint');
        calendarKind = trigger.getAttribute('data-justice-kind');

        trigger.addEventListener('click', function (e) {
            e.preventDefault();
            if (drawer.classList.contains('justice-drawer--open')) close();
            else open();
        });

        var closeBtn = drawer.querySelector('[data-justice-close]');
        if (closeBtn) closeBtn.addEventListener('click', close);

        var retryBtn = drawer.querySelector('[data-justice-retry]');
        if (retryBtn) retryBtn.addEventListener('click', refreshPayload);

        // Esc closes the drawer (or returns from candidates view if open).
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            var candidatesEl = $('[data-justice-candidates-view]', drawer);
            if (candidatesEl && !candidatesEl.hasAttribute('hidden')) {
                showMainView();
                return;
            }
            if (drawer.classList.contains('justice-drawer--open')) close();
        });

        // Phase 2b — focus list items + calendar holes both route through openForHole.
        // Delegated click handlers; keep them on document so newly-rendered candidate rows work.
        document.addEventListener('click', function (e) {
            // Calendar cell hole click — only when the drawer is at-rest on its main view.
            var cell = e.target.closest && e.target.closest('[data-justice-hole="1"]');
            if (cell && !e.target.closest('[data-justice-drawer]')) {
                var rowId = cell.getAttribute('data-row-id');
                var date = cell.getAttribute('data-date');
                if (rowId && date) {
                    e.preventDefault();
                    openForHole(rowId, date);
                    return;
                }
            }

            // Focus-list item click (rendered inside the drawer). Phase 2d branches by kind.
            var focusItem = e.target.closest && e.target.closest('.justice-focus-list__item');
            if (focusItem) {
                e.preventDefault();
                var date2 = focusItem.getAttribute('data-justice-hole-date');
                var kind2 = focusItem.getAttribute('data-justice-hole-kind') || 'shift';
                if (!date2) return;
                if (kind2 === 'shift') {
                    var shiftInstanceId = focusItem.getAttribute('data-justice-hole-shift-instance');
                    if (shiftInstanceId) openForHoleByInstance(shiftInstanceId, date2);
                } else {
                    // chore + onduty: rowId stored explicitly with proper "user-N" / "dutytype-N" prefix.
                    var rowId3 = focusItem.getAttribute('data-justice-hole-rowid');
                    if (rowId3) openForHole(rowId3, date2);
                }
                return;
            }

            // Candidate row click → preview impact.
            var candRow = e.target.closest && e.target.closest('.justice-candidate__row');
            if (candRow && candRow.closest('[data-justice-drawer]')) {
                if (candRow.disabled || candRow.getAttribute('aria-disabled') === 'true') return;
                e.preventDefault();
                var liEl = candRow.closest('.justice-candidate');
                if (!liEl) return;
                if (expandedRow === liEl) {
                    // Toggle off if already expanded.
                    collapsePreview(liEl);
                    return;
                }
                var userId = parseInt(liEl.getAttribute('data-justice-candidate-user'), 10);
                if (!isNaN(userId)) previewImpact(userId, liEl);
                return;
            }

            // Make-it-real button click. Phase 2d: enforce disabled state to prevent double-submit
            // and to honor the chore-title "must be non-empty" guard.
            var makeBtn = e.target.closest && e.target.closest('[data-justice-make-real]');
            if (makeBtn) {
                e.preventDefault();
                if (makeBtn.disabled || makeBtn.getAttribute('aria-disabled') === 'true') return;
                var userId2 = parseInt(makeBtn.getAttribute('data-justice-user'), 10);
                if (!isNaN(userId2)) makeItReal(userId2);
                return;
            }

            // Back arrow inside candidates view.
            var back = e.target.closest && e.target.closest('[data-justice-candidates-back]');
            if (back) {
                e.preventDefault();
                showMainView();
                clearActiveHoleHighlight();
            }
        });
    }

    /**
     * Variant of openForHole that takes a ShiftInstanceId directly (shift focus list path).
     * Phase 2d: resets chore form state for parity with openForHole; extends holeContext shape.
     */
    function openForHoleByInstance(shiftInstanceId, date) {
        if (!drawer) return;
        choreTitle = '';
        choreTypeId = null;
        showLoading();
        var url = endpoint
            ? endpoint.replace(/handler=Justice(?!Eligibility|Preview)/, 'handler=JusticeEligibility')
            : null;
        if (!url || url === endpoint) url = (endpoint || '') + (((endpoint || '').indexOf('?') >= 0) ? '&' : '?') + 'handler=JusticeEligibility';
        url += '&shiftInstanceId=' + encodeURIComponent(String(shiftInstanceId)) + '&date=' + encodeURIComponent(String(date));

        fetch(url, { method: 'GET', credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (resp) { if (!resp.ok) throw new Error('HTTP ' + resp.status); return resp.json(); })
            .then(function (data) {
                if (data && data.error) { showMainView(); return; }
                holeContext = {
                    shiftInstanceId: Number(data.shiftInstanceId || 0),
                    shiftTypeId: Number(data.shiftTypeId || 0),
                    date: data.date,
                    kind: data.kind || 'shift',
                    moleculeId: data.moleculeId != null ? Number(data.moleculeId) : null,
                    dutyTypeValue: data.dutyTypeValue != null ? Number(data.dutyTypeValue) : null
                };
                renderCandidates(data);
                showCandidatesView();
                showChoreFormIfNeeded();
            })
            .catch(function (err) {
                console.error('[Justice] eligibility-by-instance fetch failed', err);
                showError(loc('Justice_Panel_CandidatesLoadFailed', 'Couldn’t load candidates.'));
            });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
