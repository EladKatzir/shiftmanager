# Calendar Tabs — Phase B: Searchable / Type-to-Filter `<select>` Enhancer — Implementation Plan

- **Date:** 2026-07-21
- **Branch:** `feat/calendar-tabs-selectors`
- **Spec:** `docs/superpowers/specs/2026-07-21-calendar-tabs-selectors-and-desync-design.md` (§5 Feature 3, §11 PF8/PF9)
- **Acceptance:** `docs/superpowers/specs/2026-07-21-calendar-tabs-perfect-state-checklist.md` — items **SEL-1…SEL-6** (+ QE-3 native-exception clause)

**For agentic workers:** Execute the tasks in order, top to bottom. Each `- [ ]` is exactly **one** action — do the action, confirm the stated expected result, then check it off. Do **not** batch checkboxes. Stop and re-read at every **REVIEW** checkpoint. All code shown is literal — type it as written (it was authored against the real files at the line numbers cited). Do not invent element ids, function names, or CSS tokens beyond those shown here.

---

## Goal

Build one vanilla, air-gapped, RTL+LTR-aware widget — `wwwroot/js/searchable-select.js` + `wwwroot/css/searchable-select.css` — that upgrades any `<select>` carrying `data-searchable` into a **type-to-filter** dropdown, in **single** and **multi** (`<select multiple>`) modes, then tag the high-benefit selectors across the app with `data-searchable`. This is the picker-UX substrate that Phase E's tab-prioritization grouping renders into. **Phase B does NOT implement prioritization/grouping logic** — it only ships a *grouping-capable render* (`<optgroup>` → section headers) so Phase E can supply groups.

## Architecture

- **One file, one global namespace.** The widget is a single IIFE that publishes `window.SearchableSelect` with five methods: `enhance(select)`, `enhanceWithin(root)`, `refresh(select)`, `observe(container)`, `init()`. No build step, no dependency, no module loader (air-gapped).
- **Native `<select>` stays the source of truth.** `enhance()` wraps the native element in an `.ss-root`, visually hides the native `<select>` (kept in the DOM so form POST binding is byte-for-byte unchanged), and renders a custom `role="combobox"` control + `role="listbox"` panel beside it. Selecting/toggling writes back to the native `<option>.selected` and dispatches a bubbling `change` — so every existing change-listener and every model-bound POST keeps working with zero server changes.
- **Two scoped observers, never document-wide** (SEL-5 / PF9):
  1. *Per-widget:* a `MutationObserver` bound to the one enhanced `<select>` — re-syncs the widget when options are appended asynchronously or their text/`disabled` mutate (bottom-sheet async populate + busy decoration, SEL-4).
  2. *Per-container:* `observe(container)` watches a **specific** mount root (the bottom-sheet element) for newly-added `[data-searchable]` selects and auto-enhances them. The calendar grid — which churns during realtime refresh — is **never** observed.
- **Creation-time hook + observer, both** (spec §5 "Dynamic creation"): JS-built selects (bottom sheet) get an explicit `enhance()` call at build time; the container observer is the belt-and-suspenders fallback.
- **Grouping-capable render:** `renderList()` reads the native select's `<optgroup label>` structure and emits `.ss-group` section headers. Phase E supplies groups by wrapping options in `<optgroup>` — no widget change needed then.

## Tech Stack

- Vanilla ES2020 JS (target parity with the layout's `replaceAll`/`globalThis` feature-gate — no newer syntax), no libraries.
- CSS with **logical properties** (`margin-inline-start`, `inset-inline-start`, `text-align:start`, `padding-inline`) so a single ruleset mirrors correctly under `dir="rtl"` inherited from `<html>`; design tokens only (`--surface`, `--border`, `--text`, `--text-muted`, `--primary`, `--primary-soft`, `--surface-soft`, `--focus-ring`).
- Razor Pages markup edits (adding `data-searchable`, two inline `refresh()` calls) — no C#, no EF, no migration in this phase.

## Global Constraints

- **Air-gapped / no CDN.** No external scripts, fonts, or styles. Everything self-contained.
- **Bilingual RTL+LTR.** Must render and function identically under `he-IL` (`dir="rtl"`) and `en` (`dir="ltr"`). Set the culture via the `.AspNetCore.Culture` cookie (`c=he-IL|uic=he-IL`) — `?culture=` has no effect (cookie provider only).
- **Dev serves STALE static assets.** `/js/*` and `/css/*` are bundled/minified **at build time**; the running `:5000` Debug instance keeps serving the old bundle until it is **rebuilt and restarted**. After any `.js`/`.css` edit: rebuild + restart, then `curl` the *served* URL for your marker before trusting the browser. `.cshtml` markup edits are picked up per-request (runtime Razor) but restart once after all edits to be safe.
- **Executable lock.** Do not rebuild the app while the Debug `:5000` exe is locked (CLAUDE.md §3). To compile-gate, build `-c Release` in a **git worktree**. To browser-verify, the running instance must be restarted — if `:5000` is locked, ask the user to stop it, or run a fresh instance on an alternate port.
- **Restore the lockfile after any build:** `git checkout -- packages.lock.json`.
- **Branch:** all work on `feat/calendar-tabs-selectors`. IGNORE `FinalProductPublish\` (generated).
- **Phase boundary:** no prioritization ordering, no `inTab` grouping, no tab strip. The inline calendar trainee picker (`.excel-calendar__trainee-picker`, `calendar-inline-edit.js`) is **left native in Phase B** (its blur-dismiss / option-clone coupling is PF9/QE-3 and belongs with Phase E's grouping work) — see Task B5 which documents that exception per QE-3.

---

## Task B1 — Widget scaffold: single-select core + CSS + global load

Ship the file, the stylesheet, the global `<link>`/`<script>`, and prove the single-select path on one real static select in EN.

**Files**
- Create `wwwroot/js/searchable-select.js`
- Create `wwwroot/css/searchable-select.css`
- Modify `Pages/Shared/_Layout.cshtml:128` (add CSS `<link>` after `shift-swap-game.css`) and `Pages/Shared/_Layout.cshtml:239` (add `<script>` after `keyboard-nav.js`)
- Modify `Pages/Assignments/Manage.cshtml:122` (tag `SelectedUserId` for the verify step)

**Interfaces**
- Produces `window.SearchableSelect = { enhance, enhanceWithin, refresh, observe, init }`
  - `enhance(select: HTMLSelectElement): Widget|undefined` — idempotent; no-op unless `select` has `data-searchable`
  - `enhanceWithin(root: ParentNode): void`
  - `refresh(select: HTMLSelectElement): void`
  - `observe(container: Element): MutationObserver|undefined`
  - `init(): void` — enhances every `[data-searchable]` in `document`
- Consumes `window.AppLocalizer[key]` (optional string table; falls back to English literal)

Steps:

- [ ] Create `wwwroot/css/searchable-select.css` with the full ruleset below (logical properties handle RTL — one file, no `rtl.css` entry needed):
  ```css
  /* searchable-select.css — vanilla searchable <select> enhancer (single + multi).
     RTL/LTR via logical properties; theme via design tokens. SS_WIDGET_MARKER */
  .ss-native { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px;
    overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; border: 0; }
  .ss-root { position: relative; width: 100%; }
  .ss-control { display: flex; align-items: center; gap: 0.375rem; flex-wrap: wrap;
    min-height: 44px; width: 100%; padding: 0.5rem 0.75rem; border: 1px solid var(--border);
    border-radius: 8px; background: var(--surface); color: var(--text); font-size: 1rem;
    cursor: pointer; text-align: start; }
  .ss-control:focus { outline: none; border-color: var(--primary); box-shadow: var(--focus-ring); }
  .ss-control[aria-disabled="true"] { opacity: 0.6; cursor: not-allowed; }
  .ss-value { display: flex; align-items: center; gap: 0.375rem; flex-wrap: wrap;
    flex: 1 1 auto; min-width: 0; }
  .ss-single-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ss-placeholder { color: var(--text-muted); }
  .ss-arrow { margin-inline-start: auto; flex: 0 0 auto; color: var(--text-muted); pointer-events: none; }
  .ss-chip { display: inline-flex; align-items: center; gap: 0.25rem;
    padding: 0.125rem 0.25rem 0.125rem 0.5rem; background: var(--primary-soft);
    color: var(--primary); border-radius: 9999px; font-size: 0.8125rem; }
  .ss-chip__label { max-width: 14rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ss-chip__remove { border: 0; background: transparent; color: inherit; cursor: pointer;
    font-size: 1rem; line-height: 1; padding: 0 0.125rem; }
  .ss-panel { position: absolute; z-index: 1200; inset-inline-start: 0; top: calc(100% + 2px);
    min-width: 100%; max-width: min(92vw, 420px); background: var(--surface);
    border: 1px solid var(--border); border-radius: 8px;
    box-shadow: var(--shadow-md, 0 8px 24px rgba(0,0,0,0.18)); padding: 0.375rem;
    max-height: 320px; display: flex; flex-direction: column; }
  .ss-search { width: 100%; padding: 0.5rem 0.625rem; margin-bottom: 0.375rem;
    border: 1px solid var(--border); border-radius: 6px; background: var(--surface-soft);
    color: var(--text); font-size: 0.9375rem; text-align: start; }
  .ss-search:focus { outline: none; border-color: var(--primary); }
  .ss-options { overflow-y: auto; }
  .ss-group { padding: 0.375rem 0.5rem 0.25rem; font-size: 0.75rem; font-weight: 600;
    color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.03em; }
  .ss-option { display: flex; align-items: center; gap: 0.5rem; padding: 0.5rem 0.625rem;
    border-radius: 6px; cursor: pointer; color: var(--text); font-size: 0.9375rem; text-align: start; }
  .ss-option.is-active { background: var(--primary-soft); }
  .ss-option.is-selected { font-weight: 600; }
  .ss-option.is-disabled { opacity: 0.5; cursor: not-allowed; }
  .ss-check { flex: 0 0 1.1em; text-align: center; color: var(--primary); }
  .ss-empty { padding: 0.625rem; color: var(--text-muted); font-size: 0.9rem; text-align: center; }
  ```

- [ ] Create `wwwroot/js/searchable-select.js` with the core (single-select) implementation below. This is the complete file for B1; B2/B3/B4 add the multi/observer/dynamic functions in place.
  ```js
  /* searchable-select.js — vanilla, air-gapped searchable <select> enhancer.
     Opt-in via data-searchable. Single + multi modes. RTL/LTR + a11y.
     Public API: window.SearchableSelect. SS_WIDGET_MARKER */
  (function () {
    'use strict';
    var idSeq = 0;

    // Hebrew final-form normalization (self-contained; quick-entry's copy is IIFE-private).
    var HEBREW_FINAL_MAP = { 'ך':'כ','ם':'מ','ן':'נ','ף':'פ','ץ':'צ' };
    function ssNormalize(s) { var r=''; s=s||''; for (var i=0;i<s.length;i++) r+=HEBREW_FINAL_MAP[s[i]]||s[i]; return r.toLowerCase(); }
    function ssMatch(query, text) {
      if (!query) return true;
      var q = ssNormalize(query), t = ssNormalize(text);
      if (t.indexOf(q) !== -1) return true;
      var qi = 0; for (var ti = 0; ti < t.length && qi < q.length; ti++) if (t[ti] === q[qi]) qi++;
      return qi === q.length;
    }
    function loc(key, fallback) { return (window.AppLocalizer && window.AppLocalizer[key]) || fallback; }

    function ariaLabelFor(select) {
      if (select.getAttribute('aria-label')) return select.getAttribute('aria-label');
      if (select.id) { var l = document.querySelector('label[for="' + select.id + '"]'); if (l) return l.textContent.trim(); }
      var g = select.closest('.form-group, .bottom-sheet__field');
      if (g) { var gl = g.querySelector('label'); if (gl) return gl.textContent.trim(); }
      return '';
    }

    // Read native <select> (incl. <optgroup>) into a flat model with group labels.
    function buildOptions(w) {
      w.options = [];
      var kids = w.select.children;
      for (var i = 0; i < kids.length; i++) {
        var node = kids[i];
        if (node.tagName === 'OPTGROUP') {
          var gl = node.getAttribute('label') || '';
          var os = node.querySelectorAll('option');
          for (var j = 0; j < os.length; j++) pushOption(w, os[j], gl);
        } else if (node.tagName === 'OPTION') { pushOption(w, node, null); }
      }
    }
    function pushOption(w, opt, group) {
      w.options.push({ el: opt, value: opt.value, group: group, disabled: opt.disabled,
        isPlaceholder: opt.value === '', label: (opt.dataset.ssLabel || opt.textContent || '').trim() });
    }

    function placeholderSpan(w) {
      var ph = document.createElement('span'); ph.className = 'ss-placeholder';
      var first = w.select.options[0];
      ph.textContent = (first && first.value === '') ? first.textContent.trim() : loc('Select', 'Select…');
      return ph;
    }

    // Paint the control face: single label, or multi chips.
    function renderChips(w) {
      w.value.innerHTML = '';
      if (w.multi) { return; } // multi implemented in B2
      var sel = w.select.options[w.select.selectedIndex];
      if (!sel || sel.value === '') { w.value.appendChild(placeholderSpan(w)); return; }
      var s = document.createElement('span'); s.className = 'ss-single-label';
      s.textContent = (sel.dataset.ssLabel || sel.textContent || '').trim();
      w.value.appendChild(s);
    }

    function renderList(w, query) {
      w.list.innerHTML = ''; w.activeIndex = -1; w._rows = [];
      var lastGroup = '__none__';
      w.options.forEach(function (o) {
        if (o.isPlaceholder && !w.multi) return;
        if (!ssMatch(query || '', o.label)) return;
        if (o.group && o.group !== lastGroup) {
          lastGroup = o.group;
          var h = document.createElement('div'); h.className = 'ss-group';
          h.setAttribute('role', 'presentation'); h.textContent = o.group; w.list.appendChild(h);
        }
        var row = document.createElement('div'); row.className = 'ss-option';
        row.setAttribute('role', 'option'); row.id = w.uid + '-opt-' + w._rows.length; row.dataset.value = o.value;
        var selected = o.el.selected;
        row.setAttribute('aria-selected', selected ? 'true' : 'false');
        if (selected) row.classList.add('is-selected');
        if (o.disabled) { row.classList.add('is-disabled'); row.setAttribute('aria-disabled', 'true'); }
        if (w.multi) { var c = document.createElement('span'); c.className = 'ss-check';
          c.setAttribute('aria-hidden', 'true'); c.textContent = selected ? '✓' : ''; row.appendChild(c); }
        var txt = document.createElement('span'); txt.className = 'ss-option__label'; txt.textContent = o.label; row.appendChild(txt);
        (function (om, re) { re.addEventListener('mousedown', function (e) { e.preventDefault(); if (om.disabled) return; choose(w, om); }); })(o, row);
        w.list.appendChild(row); w._rows.push(row);
      });
      if (w._rows.length === 0) { var none = document.createElement('div'); none.className = 'ss-empty';
        none.textContent = loc('NoMatches', 'No matches'); w.list.appendChild(none); }
      else setActive(w, 0);
    }

    function setActive(w, idx) {
      if (!w._rows || !w._rows.length) return;
      if (idx < 0) idx = w._rows.length - 1; if (idx >= w._rows.length) idx = 0;
      if (w.activeIndex >= 0 && w._rows[w.activeIndex]) w._rows[w.activeIndex].classList.remove('is-active');
      w.activeIndex = idx; var row = w._rows[idx]; row.classList.add('is-active');
      row.scrollIntoView({ block: 'nearest' }); w.search.setAttribute('aria-activedescendant', row.id);
    }

    function fire(w) { w.select.dispatchEvent(new Event('change', { bubbles: true })); }

    function choose(w, o) {
      w.syncing = true;
      if (w.multi) { /* B2 */ }
      else { w.select.value = o.value; fire(w); renderChips(w); closePanel(w); w.control.focus(); }
      w.syncing = false;
    }

    function openPanel(w) {
      if (w.open || w.select.disabled) return;
      buildOptions(w); w.panel.hidden = false; w.open = true;
      w.control.setAttribute('aria-expanded', 'true');
      // Measure at open — works for selects inside hidden/animated modals (SEL-3).
      w.panel.style.minWidth = w.control.getBoundingClientRect().width + 'px';
      w.search.value = ''; renderList(w, ''); w.search.focus();
      document.addEventListener('mousedown', w._outside, true);
    }
    function closePanel(w) {
      if (!w.open) return;
      w.panel.hidden = true; w.open = false;
      w.control.setAttribute('aria-expanded', 'false'); w.control.removeAttribute('aria-activedescendant');
      document.removeEventListener('mousedown', w._outside, true);
    }

    function wireEvents(w) {
      w._outside = function (e) { if (!w.root.contains(e.target)) closePanel(w); };
      w.control.addEventListener('click', function () { w.open ? closePanel(w) : openPanel(w); });
      w.control.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openPanel(w); }
      });
      w.search.addEventListener('input', function () { renderList(w, w.search.value); });
      w.search.addEventListener('keydown', function (e) {
        switch (e.key) {
          case 'ArrowDown': e.preventDefault(); setActive(w, w.activeIndex + 1); break;
          case 'ArrowUp':   e.preventDefault(); setActive(w, w.activeIndex - 1); break;
          case 'Enter': e.preventDefault();
            if (w._rows && w._rows[w.activeIndex]) { var o = optByValue(w, w._rows[w.activeIndex].dataset.value);
              if (o && !o.disabled) choose(w, o); } break;
          case 'Escape': e.preventDefault(); closePanel(w); w.control.focus(); break;
          // Space/Backspace multi cases added in B2.
        }
      });
    }
    function optByValue(w, v) { for (var i = 0; i < w.options.length; i++) if (w.options[i].value === v) return w.options[i]; return null; }

    function enhance(select) {
      if (!select || select.tagName !== 'SELECT') return;
      if (select._ssWidget) return;                 // idempotent
      if (!select.hasAttribute('data-searchable')) return;

      var multi = select.multiple, uid = 'ss-' + (++idSeq);
      var root = document.createElement('div'); root.className = 'ss-root' + (multi ? ' ss-root--multi' : '');
      select.parentNode.insertBefore(root, select); root.appendChild(select);
      select.classList.add('ss-native'); select.setAttribute('tabindex', '-1'); select.setAttribute('aria-hidden', 'true');

      var control = document.createElement('div'); control.className = 'ss-control'; control.id = uid + '-control';
      control.setAttribute('role', 'combobox'); control.setAttribute('tabindex', '0');
      control.setAttribute('aria-haspopup', 'listbox'); control.setAttribute('aria-expanded', 'false');
      if (select.disabled) control.setAttribute('aria-disabled', 'true');
      var lbl = ariaLabelFor(select); if (lbl) control.setAttribute('aria-label', lbl);
      var value = document.createElement('span'); value.className = 'ss-value'; control.appendChild(value);
      var arrow = document.createElement('span'); arrow.className = 'ss-arrow'; arrow.setAttribute('aria-hidden', 'true');
      arrow.textContent = '▾'; control.appendChild(arrow); root.appendChild(control);

      var panel = document.createElement('div'); panel.className = 'ss-panel'; panel.id = uid + '-panel';
      panel.setAttribute('role', 'listbox'); if (multi) panel.setAttribute('aria-multiselectable', 'true'); panel.hidden = true;
      var search = document.createElement('input'); search.className = 'ss-search'; search.type = 'text';
      search.setAttribute('autocomplete', 'off'); search.setAttribute('role', 'searchbox');
      search.setAttribute('aria-label', loc('Search', 'Search')); search.dir = 'auto'; panel.appendChild(search);
      var list = document.createElement('div'); list.className = 'ss-options'; list.id = uid + '-list';
      search.setAttribute('aria-controls', list.id); panel.appendChild(list); root.appendChild(panel);
      control.setAttribute('aria-controls', panel.id);

      var w = { select: select, root: root, control: control, value: value, panel: panel, search: search,
        list: list, multi: multi, uid: uid, open: false, activeIndex: -1, options: [], _rows: [], syncing: false, mo: null };
      select._ssWidget = w;
      buildOptions(w); wireEvents(w); renderChips(w);
      // attachSelectObserver(w) added in B3.
      return w;
    }

    function refresh(select) { /* B3 */ }
    function enhanceWithin(root) { if (!root) return; var s = root.querySelectorAll('select[data-searchable]'); for (var i = 0; i < s.length; i++) enhance(s[i]); }
    function observe(container) { /* B4 */ }
    function init() { enhanceWithin(document); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();

    window.SearchableSelect = { enhance: enhance, enhanceWithin: enhanceWithin, refresh: refresh, observe: observe, init: init };
  })();
  ```

- [ ] Add the global CSS link. In `Pages/Shared/_Layout.cshtml`, immediately after line 128 (`<link rel="stylesheet" href="~/css/shift-swap-game.css" asp-append-version="true" />`) insert:
  ```html
      @* Searchable-select widget (global — used on admin, assignment & calendar pages) *@
      <link rel="stylesheet" href="~/css/searchable-select.css" asp-append-version="true" />
  ```

- [ ] Add the global script. In `Pages/Shared/_Layout.cshtml`, immediately after line 239 (`<script defer src="~/js/keyboard-nav.js" ...></script>`) insert:
  ```html
      @* Searchable/type-to-filter <select> enhancer (opt-in via data-searchable) *@
      <script defer src="~/js/searchable-select.js" asp-append-version="true"></script>
  ```

- [ ] Tag one real static single-select for the verify. In `Pages/Assignments/Manage.cshtml:122`, add `data-searchable`:
  ```html
          <select class="input" asp-for="SelectedUserId" data-searchable style="font-family: var(--font-sans);">
  ```

- [ ] Compile-gate (no C# changed, but confirm the app still builds and static bundling succeeds): in a worktree, `dotnet build -c Release`; expect `Build succeeded`. Then `git checkout -- packages.lock.json`.

- [ ] Restart the running app so the new bundle is served (if `:5000` is locked, ask the user to stop it first — CLAUDE.md §3). Then verify the served JS carries the marker:
  ```bash
  curl -s http://localhost:5000/js/searchable-select.js | grep -c "window.SearchableSelect"
  ```
  Expect `1` (or more). If `0`, the bundle is stale — the restart didn't take.

- [ ] Browser-verify (EN / LTR) on `/Assignments/Manage?...` (open any shift's Manage page that renders the "Add Person" card): the `SelectedUserId` select shows a custom control; clicking it opens a panel with a search box; typing part of a user's name filters the list live; `ArrowDown`/`ArrowUp` move the highlight; `Enter` selects and closes; the control shows the chosen name. Confirm in DevTools the native `<select id="SelectedUserId">` `.value` equals the chosen id and that submitting the form still posts that user (assignment succeeds).
- [ ] Verify **exactly one** `change` fires (SEL-1): in the console run
  ```js
  var s = document.getElementById('SelectedUserId'); s.addEventListener('change', () => console.count('ss-change'));
  ```
  then pick an option via the widget — `ss-change` prints `1`. Press `Escape` after reopening → panel closes, value unchanged (restores).
- [ ] Commit: `git add wwwroot/js/searchable-select.js wwwroot/css/searchable-select.css Pages/Shared/_Layout.cshtml Pages/Assignments/Manage.cshtml && git commit -m "Phase B: searchable-select single-select core + global wiring"`

**REVIEW:** single-select filter, keyboard nav, one-change, POST parity all green before B2.

---

## Task B2 — Multi-select mode (chips, toggle, Backspace) + tag the 4 ShiftGroupings selects

**Files**
- Modify `wwwroot/js/searchable-select.js` (fill the multi branches in `renderChips`, `choose`, add Space/Backspace keys)
- Modify `Pages/Admin/Organization/ShiftGroupings/Index.cshtml:125,135,245,255` (add `data-searchable`) and `:281-282` (refresh after preselect)

**Interfaces**
- No API change. Multi widgets keep the native `<select multiple>` `.selected` flags authoritative → the existing `SelectedCompanyIds` / `SelectedJobTypeIds` / `EditCompanyIds` / `EditJobTypeIds` model binding is unchanged.

Steps:

- [ ] Replace the `renderChips` multi stub (`if (w.multi) { return; }`) with the chip renderer. Change the body of `renderChips` to:
  ```js
    function renderChips(w) {
      w.value.innerHTML = '';
      if (w.multi) {
        var selected = w.options.filter(function (o) { return o.el.selected && !o.isPlaceholder; });
        if (selected.length === 0) { w.value.appendChild(placeholderSpan(w)); return; }
        selected.forEach(function (o) {
          var chip = document.createElement('span'); chip.className = 'ss-chip';
          var cl = document.createElement('span'); cl.className = 'ss-chip__label'; cl.textContent = o.label; chip.appendChild(cl);
          var x = document.createElement('button'); x.type = 'button'; x.className = 'ss-chip__remove';
          x.setAttribute('aria-label', loc('Remove', 'Remove') + ': ' + o.label); x.textContent = '×';
          x.addEventListener('mousedown', function (e) {
            e.preventDefault(); e.stopPropagation();
            w.syncing = true; o.el.selected = false; fire(w); renderChips(w);
            if (w.open) renderList(w, w.search.value); w.syncing = false;
          });
          chip.appendChild(x); w.value.appendChild(chip);
        });
        return;
      }
      var sel = w.select.options[w.select.selectedIndex];
      if (!sel || sel.value === '') { w.value.appendChild(placeholderSpan(w)); return; }
      var s = document.createElement('span'); s.className = 'ss-single-label';
      s.textContent = (sel.dataset.ssLabel || sel.textContent || '').trim(); w.value.appendChild(s);
    }
  ```

- [ ] Replace the `choose` multi stub (`if (w.multi) { /* B2 */ }`) with the toggle path (panel stays open):
  ```js
    function choose(w, o) {
      w.syncing = true;
      if (w.multi) {
        o.el.selected = !o.el.selected; fire(w); renderChips(w);
        renderList(w, w.search.value); w.search.focus();
      } else {
        w.select.value = o.value; fire(w); renderChips(w); closePanel(w); w.control.focus();
      }
      w.syncing = false;
    }
  ```

- [ ] Add Space-toggle and Backspace-removes-last to the `search` keydown switch (insert these cases before `case 'Escape':`):
  ```js
          case ' ':
            if (w.multi && w._rows && w._rows[w.activeIndex]) {
              e.preventDefault(); var o2 = optByValue(w, w._rows[w.activeIndex].dataset.value);
              if (o2 && !o2.disabled) choose(w, o2);
            } break;
          case 'Backspace':
            if (w.multi && w.search.value === '') {
              var sel = w.options.filter(function (o) { return o.el.selected && !o.isPlaceholder; });
              if (sel.length) { e.preventDefault(); w.syncing = true; sel[sel.length - 1].el.selected = false;
                fire(w); renderChips(w); renderList(w, ''); w.syncing = false; }
            } break;
  ```

- [ ] Tag the create-form multi-selects. In `Pages/Admin/Organization/ShiftGroupings/Index.cshtml`, line 125:
  ```html
                      <select class="form-select" asp-for="SelectedCompanyIds" data-searchable multiple>
  ```
  and line 135:
  ```html
                      <select class="form-select" asp-for="SelectedJobTypeIds" data-searchable multiple>
  ```

- [ ] Tag the edit-modal multi-selects. Same file, line 245:
  ```html
                  <select class="form-select" name="EditCompanyIds" id="editCompanyIds" data-searchable multiple>
  ```
  and line 255:
  ```html
                  <select class="form-select" name="EditJobTypeIds" id="editJobTypeIds" data-searchable multiple>
  ```

- [ ] Re-sync the edit widgets after `preselectOptions` mutates `.selected` (the modal is `display:none` at load — the widgets were enhanced by `init()` but need a refresh to paint the preselected chips + correct width on open). In the same file's inline `openEditModal` function, after the two `preselectOptions(...)` calls (lines 281-282) add:
  ```js
      if (window.SearchableSelect) {
          window.SearchableSelect.refresh(document.getElementById('editCompanyIds'));
          window.SearchableSelect.refresh(document.getElementById('editJobTypeIds'));
      }
  ```
  (`refresh` is implemented in B3; this call is a safe no-op until then.)

- [ ] Rebuild + restart; `curl -s http://localhost:5000/js/searchable-select.js | grep -c "ss-chip"` → expect ≥ `1`. `git checkout -- packages.lock.json`.
- [ ] Browser-verify (EN) on `/Admin/Organization/ShiftGroupings`: the **Companies** create-form select opens a searchable panel; typing filters; `Enter`/`Space`/click toggles multiple options; each selected option shows as a removable chip in the control; the panel stays open while toggling; chip `×` and `Backspace` (empty search) remove; submit **Create** → the created grouping's Companies/JobTypes counts match the chips you picked (native `<select multiple>` POSTed correctly, SEL-2).
- [ ] Browser-verify the **edit** flow: click **Edit** on an existing grouping → modal opens, both multi-widgets show the grouping's current companies/job-types as chips (refresh painted them), width correct on first open; change the set, **Save** → persisted set matches the chips.
- [ ] Commit: `git commit -am "Phase B: multi-select chip mode + tag ShiftGroupings selects"`

**REVIEW:** multi chips, POST round-trip, and hidden-modal preselect all green.

---

## Task B3 — `refresh()` + per-select scoped MutationObserver (async populate + busy resync)

Make the widget survive options that arrive/mutate **after** enhancement (bottom-sheet async populate; busy-glyph decoration that rewrites option text and toggles `disabled`).

**Files**
- Modify `wwwroot/js/searchable-select.js` (implement `refresh`, add `attachSelectObserver`, call it from `enhance`)

**Interfaces**
- `refresh(select)` — public hook (PF9): rebuild the option model, repaint chips/label, and if the panel is open re-render the filtered list preserving the current query.

Steps:

- [ ] Replace the `refresh` stub with:
  ```js
    function refresh(select) {
      var w = select && select._ssWidget; if (!w) return;
      buildOptions(w); renderChips(w);
      if (w.open) renderList(w, w.search.value);
    }
  ```

- [ ] Add `attachSelectObserver` (scoped to the single `<select>` — never document-wide, SEL-5). Insert above `enhance`:
  ```js
    function attachSelectObserver(w) {
      // Re-sync when options are appended (async populate) or mutate (busy decoration
      // rewrites text + toggles `disabled`). w.syncing guards our own programmatic writes.
      w.mo = new MutationObserver(function () { if (w.syncing) return; refresh(w.select); });
      w.mo.observe(w.select, { childList: true, subtree: true, characterData: true,
        attributes: true, attributeFilter: ['disabled'] });
    }
  ```

- [ ] Wire it in `enhance`: change the tail `// attachSelectObserver(w) added in B3.` to a real call, so the end of `enhance` reads:
  ```js
      buildOptions(w); wireEvents(w); renderChips(w); attachSelectObserver(w);
      return w;
  ```

- [ ] Rebuild + restart; `curl -s http://localhost:5000/js/searchable-select.js | grep -c "attachSelectObserver"` → expect ≥ `1`. `git checkout -- packages.lock.json`.
- [ ] Browser-verify resync (EN) on `/Admin/Organization/ShiftGroupings`: open the console and run — on the enhanced **Companies** create select — an option injection to simulate async mutation:
  ```js
  var s = document.getElementById('SelectedCompanyIds');
  var o = document.createElement('option'); o.value = '999999'; o.textContent = 'ZZ Sync Probe'; s.appendChild(o);
  ```
  Open the widget and type `Sync` → `ZZ Sync Probe` appears in the panel (the per-select observer refreshed the model without any manual `refresh()` call).
- [ ] Verify the sync-guard: pick/toggle options through the widget and confirm no console errors and no double-render flicker (our own writes set `w.syncing` so the observer no-ops).
- [ ] Commit: `git commit -am "Phase B: refresh() hook + per-select scoped observer (async/busy resync)"`

**REVIEW:** a mutation to the native `<select>` after enhancement reflects in the widget; programmatic writes don't loop.

---

## Task B4 — Dynamic enhancement: creation-time hook + scoped container observer; wire the bottom-sheet selects

Enhance selects that are **built by JS** and live inside a **hidden/animated** modal — the bottom-sheet user picker (rebuilt per open, populated async, then busy-decorated) and the bottom-sheet trainee-add picker.

**Files**
- Modify `wwwroot/js/searchable-select.js` (implement `observe`)
- Modify `wwwroot/js/calendar-bottom-sheet.js`: `:144` (register the container observer once), `:475` (tag + enhance the user select at creation), `:677` (tag the trainee-add select)

**Interfaces**
- `observe(container)` — scoped `MutationObserver` on **container** (not document); auto-enhances `[data-searchable]` selects added anywhere under it. Idempotent per container.
- Consumes `window.SearchableSelect.enhance` / `.observe` from `calendar-bottom-sheet.js` (guarded with `window.SearchableSelect &&` — CLAUDE.md §1: `SearchableSelect` is the global published by `searchable-select.js`, loaded via `_Layout`; runtime calls happen on user interaction, after the deferred script has executed).

Steps:

- [ ] Implement `observe` (replace the stub):
  ```js
    function observe(container) {
      if (!container || container._ssObserved) return; container._ssObserved = true;
      var mo = new MutationObserver(function (muts) {
        for (var i = 0; i < muts.length; i++) {
          var added = muts[i].addedNodes;
          for (var j = 0; j < added.length; j++) {
            var n = added[j]; if (n.nodeType !== 1) continue;
            if (n.tagName === 'SELECT' && n.hasAttribute('data-searchable')) enhance(n);
            else if (n.querySelectorAll) enhanceWithin(n);
          }
        }
      });
      mo.observe(container, { childList: true, subtree: true });
      return mo;
    }
  ```

- [ ] Register the container observer once, scoped to the bottom-sheet root. In `calendar-bottom-sheet.js`, immediately after line 144 (`document.body.appendChild(sheetElement);`) add:
  ```js
          // Phase B: auto-enhance [data-searchable] selects rendered into the sheet (rebuilt
          // per open). Scoped to the sheet element only — NOT document-wide (SEL-5/PF9).
          if (window.SearchableSelect) window.SearchableSelect.observe(sheetElement);
  ```

- [ ] Tag + enhance the user select at creation. In `calendar-bottom-sheet.js`, after line 475 (`userSelect.id = 'bottom-sheet-user-select';`) add:
  ```js
              userSelect.setAttribute('data-searchable', '');
  ```
  Then, after the `if/else` block that populates `userSelect` completes (the block spanning ~487-…; add this **after** the field is appended to the sheet body so the control can measure width — locate the line where `fieldGroup` is appended to `addSection`/body and add immediately after it):
  ```js
          // Creation-time hook: enhance now; async-populated options + busy decoration
          // re-sync via the per-select observer (SEL-3/SEL-4). Width is measured on open.
          if (window.SearchableSelect) window.SearchableSelect.enhance(userSelect);
  ```
  > If `userSelect` is appended to the DOM inside a branch, place the `enhance` call at the single join point after both branches, guaranteeing the element is in the document. The container observer registered above is the fallback if the explicit call is ever skipped.

- [ ] Tag the trainee-add select so the container observer enhances it. In `calendar-bottom-sheet.js`, after line 677 (`select.className = 'bottom-sheet__select bottom-sheet__select--small';`) add:
  ```js
          select.setAttribute('data-searchable', '');
  ```
  (No explicit `enhance` needed — `buildTraineeAddRow`'s `row` is appended into `sheetElement`, which the container observer covers.)

- [ ] Rebuild + restart; `curl -s http://localhost:5000/js/searchable-select.js | grep -c "_ssObserved"` → ≥ `1`; `curl -s http://localhost:5000/js/calendar-bottom-sheet.js | grep -c "SearchableSelect"` → ≥ `2`. `git checkout -- packages.lock.json`.
- [ ] Browser-verify (EN) on `/Calendar/Shifts` (by-shift view, a shift row): click an empty cell to open the bottom sheet → the **Select User** dropdown is an enhanced searchable widget on **first** open with correct width (SEL-3); it lists eligible users; the busy glyphs/`⏱🏠🌴` decoration appears on options a moment later and the widget shows them (the per-select observer refreshed after `decorateOptionsWithBusyAsync`); hard-conflict users render disabled/`is-disabled` (SEL-4). Type to filter; choose a user; **Assign** succeeds (native value synced).
- [ ] Verify **re-open** enhancement: close and reopen the sheet on another cell → the freshly-rebuilt select is enhanced again every time (SEL-3).
- [ ] Verify observer scoping under realtime churn (SEL-5): keep the Shifts calendar open while another action triggers a realtime grid refresh (or in console dispatch several no-op DOM mutations on `.excel-calendar`); confirm no jank and that `document`-level select enhancement is **not** happening — only the sheet's selects are widgets. Sanity check: `document.querySelectorAll('.excel-calendar .ss-root').length === 0`.
- [ ] Commit: `git commit -am "Phase B: dynamic enhancement (creation hook + scoped container observer) for bottom-sheet selects"`

**REVIEW:** bottom-sheet enhanced every open, async+busy resync, scoped observer — SEL-3/4/5 green.

---

## Task B5 — Tag the remaining high-benefit selectors + full a11y/RTL pass + document the native trainee-picker exception

Extend `data-searchable` to the verified high-benefit admin/assignment selectors, run the bilingual RTL pass, and record the Phase-B scoping decision.

**Files**
- Modify `Pages/Assignments/Manage.cshtml:66` (trainee `traineeUserId`)
- Modify `Pages/Admin/Companies.cshtml:466,506,689`
- Modify `Pages/Admin/AuditLog.cshtml:99`
- Modify `Pages/Admin/Users.cshtml:519,784,795,811,822,859,881,892,1386,1412,1421`
- Modify `docs/superpowers/plans/2026-07-21-calendar-tabs-phase-B-searchable-select.md` (this file — append the "Deferred / native exception" note; or record it in the branch's session notes)

**Interfaces** — none (markup only).

**Tagging rule (apply consistently, do NOT over-tag):** add `data-searchable` to a `<select>` **iff** it renders a `@foreach` over a *collection that can grow large* — users, companies, molecules, job types, directors, role templates. **Skip:** enum/fixed-list selects (UserRole, status, gender, account type, scope, view-mode, category filters) — they have no benefit and stay native. **Skip:** per-row grid cell-edit selects inside large tables (e.g. `Pages/Admin/Users.cshtml:998,1119` job-type/role per row) — enhancing hundreds per page is a perf cost with low benefit; leave native.

Steps:

- [ ] Tag the Assignments trainee picker. `Pages/Assignments/Manage.cshtml:66`:
  ```html
                              <select name="traineeUserId" class="input" data-searchable style="max-width: 300px;" required>
  ```
- [ ] Tag Companies selectors. `Pages/Admin/Companies.cshtml`:
  - `:466` `SelectedMoleculeId` → add `data-searchable`
  - `:506` `SelectedDirectorId` (`id="directorSelect"`) → add `data-searchable`
  - `:689` `moveTargetMolecule` → add `data-searchable`
- [ ] Tag AuditLog user filter. `Pages/Admin/AuditLog.cshtml:99` `auditUserId` → add `data-searchable`. (Leave `auditAction`/`auditEntityType` native — small fixed lists.)
- [ ] Tag Users selectors (add `data-searchable` to each):
  - `:519` `filterCompanyId`, `:859` `UserFilterCompanyId`, `:1386` `moveDestCompany`, `:1412` `addMembershipCompany` (companies)
  - `:811` `NewMoleculeId`, `:881` `UserFilterMoleculeId` (molecules)
  - `:822` `NewJobTypeId` (`id="addUserJobType"`), `:892` `UserFilterJobTypeId` (job types)
  - `:784` `NewRoleTemplateId`, `:1421` `addMembershipRole` (role templates)
  - `:795` `NewUserCompanyId` (companies)
  - **Skip** `:530/:870` `FilterRole`/`UserFilterRole` (UserRole enum), `:510` `FilterStatus`, `:998/:1021/:1041/:1119` per-row cell-edit selects.
  > These filter selects use `onchange="this.form.submit()"` — the widget fires a bubbling `change`, so auto-submit still works. Verify one in the browser step below.
- [ ] Rebuild + restart. `git checkout -- packages.lock.json`.
- [ ] Browser-verify (EN) a representative sample: `/Admin/Users` company filter (`filterCompanyId`) — pick a company via the widget → the page auto-submits and filters (bubbling `change` intact); `/Admin/Companies` director picker (`directorSelect`) filters and selects; `/Admin/AuditLog` user filter searches a long user list.
- [ ] Browser-verify (HE / RTL) — set culture cookie `.AspNetCore.Culture = c=he-IL|uic=he-IL`, reload `/Admin/Organization/ShiftGroupings`: the control, chips, search box, arrow, and option rows are **mirrored** (text starts at the right, chip `×` and arrow on the correct side); typing Hebrew filters (final-form normalization: typing `שלום` matches `שלוםם`-style variants); no horizontal page scroll (SEL-1 HE + LOC-3 spirit). Repeat a single-select check on `/Assignments/Manage` in HE.
- [ ] a11y spot-check (either culture): with the panel open, confirm `role="combobox"` on the control, `aria-expanded` flips true/false, `aria-controls` points at the panel, `role="listbox"` on the panel, `role="option"` + `aria-selected` on rows, and `aria-activedescendant` on the search input tracks the highlighted row. Keyboard-only: Tab to the control, `ArrowDown` opens, arrows move, `Enter` selects, `Escape` closes and returns focus to the control.
- [ ] Record the **native trainee-picker exception** (QE-3). The inline calendar trainee picker `.excel-calendar__trainee-picker` (`calendar-inline-edit.js:~877-897`) is **left native in Phase B**: enhancing it requires resolving the 150ms blur self-dismiss and the option-clone that must copy `data-company`/`data-jobtype` — both coupled to Phase E's prioritization/grouping and PF9. Add a one-line code comment at its creation site documenting this, e.g. in `calendar-inline-edit.js` above the trainee `<select>` build: `// NOTE: intentionally NOT searchable-enhanced in Phase B — see QE-3/PF9; enhanced in Phase E with data-company/data-jobtype + blur-dismiss guard.`
- [ ] Commit: `git commit -am "Phase B: tag high-benefit selectors; document native inline-trainee exception (QE-3)"`

**REVIEW:** high-benefit selectors enhanced, EN+HE/RTL verified, a11y confirmed, native exception documented.

---

## SEL checklist coverage

| Item | Requirement | Covered by |
|------|-------------|-----------|
| **SEL-1** | single `data-searchable` filters live EN(LTR)+HE(RTL); Arrow/Enter selects; Escape restores; exactly one `change` | **B1** (core, EN + one-change) → **B5** (HE/RTL) |
| **SEL-2** | 4 ShiftGroupings multi-selects: type-filter, Enter/Space toggle, chips with `×` + Backspace-removes-last (RTL logical order); POST persists the chip set via the underlying `<select multiple>` | **B2** |
| **SEL-3** | bottom-sheet select (rebuilt per open, incl. from hidden) enhanced every open with correct width on first open | **B4** (creation hook + width-on-open) |
| **SEL-4** | async busy decoration → widget shows busy glyphs + disables hard-conflict users (option-mutation resync / `refresh()`) | **B3** (`refresh` + per-select observer) → **B4** (verified on live busy decoration) |
| **SEL-5** | realtime refresh churn → no observer perf degradation (observer scoped, not document-wide) | **B3** (per-select scoped) + **B4** (`observe()` scoped to sheet root; calendar grid never observed) |
| **SEL-6** | quick-entry `eligibleCache` → prioritization ordering applied at render, never baked into cached order | **PARTIAL / Phase E.** SEL-6 targets the quick-entry *combobox* (not a `<select>`, so not a `searchable-select.js` client) and requires prioritization, which is Phase E. Phase B ships the **enabling capability**: the widget's `<optgroup>`-aware `renderList` derives display order from the live option/group structure at every render (never a cached snapshot). Full SEL-6 verification lands in Phase E when prioritization groups are supplied. |

**QE-3 (native exception):** documented in **B5** — inline trainee picker stays native in Phase B by design.

---

## Deferred / out-of-scope for Phase B (flagged per CLAUDE.md §5)

- **SEL-6 full verification** — Phase E (needs prioritization ordering; the quick-entry combobox is not a `<select>`). Phase B provides the grouping-capable render only.
- **Inline calendar trainee picker enhancement** (`.excel-calendar__trainee-picker`) — Phase E (coupled to PF9 blur-dismiss + `data-company`/`data-jobtype` clone). Left native, documented (QE-3).
- **Tab-prioritization grouping** (the "this tab / other in molecule" sections, PF8 shared ordering utility) — Phase E, by directive. The widget only exposes the `<optgroup>` render hook.
- **Per-row grid cell-edit selects** (`Pages/Admin/Users.cshtml:998,1119`) — intentionally left native (perf; low benefit).
