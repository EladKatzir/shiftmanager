// Justice Analytics page — Phase 1 client-side glue.
// No framework, no chart library. Just:
//   1. Auto-submit the filter form when any select/input changes (with a small debounce
//      so typing dates doesn't fire on every keystroke).
//   2. Detect Level changes and reset ScopeId so the picker re-populates from server-side
//      defaults (otherwise switching from "Companies in Molecule" to "Users in Company"
//      keeps the now-invalid molecule id and the page would show empty rows).
(function () {
    'use strict';

    const form = document.getElementById('justice-filter-form');
    if (!form) return;

    let debounceTimer = null;
    function scheduleSubmit(immediate) {
        if (debounceTimer) clearTimeout(debounceTimer);
        debounceTimer = setTimeout(function () {
            form.submit();
        }, immediate ? 0 : 250);
    }

    form.querySelectorAll('[data-justice-onchange]').forEach(function (el) {
        // Detect the Level <select>. When Level changes, the previously-chosen ScopeId is
        // no longer meaningful (e.g., a molecule id when the user switched to Area level).
        // Clear it so the server falls back to the first accessible option for the new level.
        if (el.name === 'level') {
            el.addEventListener('change', function () {
                const scopeId = form.querySelector('[name="scopeId"]');
                if (scopeId) scopeId.value = '';
                scheduleSubmit(true);
            });
            return;
        }

        // Date inputs debounce; everything else submits immediately.
        const isDate = el.type === 'date';
        const evt = isDate ? 'change' : 'change';
        el.addEventListener(evt, function () {
            scheduleSubmit(false);
        });
    });
})();
