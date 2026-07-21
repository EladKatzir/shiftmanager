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
    if (w.multi) {
      o.el.selected = !o.el.selected; fire(w); renderChips(w);
      renderList(w, w.search.value); w.search.focus();
    } else {
      w.select.value = o.value; fire(w); renderChips(w); closePanel(w); w.control.focus();
    }
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
        case 'Escape': e.preventDefault(); closePanel(w); w.control.focus(); break;
      }
    });
  }
  function optByValue(w, v) { for (var i = 0; i < w.options.length; i++) if (w.options[i].value === v) return w.options[i]; return null; }

  function attachSelectObserver(w) {
    // Re-sync when options are appended (async populate) or mutate (busy decoration
    // rewrites text + toggles `disabled`). w.syncing guards our own programmatic writes.
    w.mo = new MutationObserver(function () { if (w.syncing) return; refresh(w.select); });
    w.mo.observe(w.select, { childList: true, subtree: true, characterData: true,
      attributes: true, attributeFilter: ['disabled'] });
  }

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
    buildOptions(w); wireEvents(w); renderChips(w); attachSelectObserver(w);
    return w;
  }

  function refresh(select) {
    var w = select && select._ssWidget; if (!w) return;
    buildOptions(w); renderChips(w);
    if (w.open) renderList(w, w.search.value);
  }
  function enhanceWithin(root) { if (!root) return; var s = root.querySelectorAll('select[data-searchable]'); for (var i = 0; i < s.length; i++) enhance(s[i]); }
  function observe(container) { /* B4 */ }
  function init() { enhanceWithin(document); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();

  window.SearchableSelect = { enhance: enhance, enhanceWithin: enhanceWithin, refresh: refresh, observe: observe, init: init };
})();
