// team.js — /Calendar/Team saved-view ("private team table") add/delete UX.
//
// Both handlers POST form-encoded (not JSON) so the browser's default Razor Pages antiforgery
// validation (form-field __RequestVerificationToken, no [IgnoreAntiforgeryToken] needed) applies
// unmodified — see Pages/Calendar/Team.cshtml.cs OnPostAddViewAsync/OnPostDeleteViewAsync.
(function () {
    'use strict';

    function csrfToken() {
        var t = document.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }

    function showError(message) {
        var text = message || (window.AppLocalizer && window.AppLocalizer.Error_NetworkError) || 'Something went wrong';
        if (window.FeedbackModal && typeof window.FeedbackModal.show === 'function') {
            window.FeedbackModal.show('error', text);
        } else {
            // No native alert() per project convention — FeedbackModal/Toast are always loaded via
            // _Layout, this branch only guards against an unexpected load-order issue.
            console.error(text);
        }
    }

    async function postForm(url, fields) {
        var body = new URLSearchParams();
        Object.keys(fields).forEach(function (key) {
            if (fields[key] !== null && fields[key] !== undefined) body.set(key, fields[key]);
        });
        body.set('__RequestVerificationToken', csrfToken());

        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            credentials: 'same-origin',
            body: body.toString()
        });
        if (!response.ok && response.status !== 400 && response.status !== 403 && response.status !== 404) {
            throw new Error('Request failed: ' + response.status);
        }
        return response.json();
    }

    // ── "New team table" dialog ──────────────────────────────────────────────────────────────
    // Previously this was a bare name box that read #companySelect/#jobTypeSelect straight off the
    // toolbar, so the saved table silently inherited whatever was on screen and a lead had no way to
    // create a table for a different desk. The dialog makes the choice explicit; the toolbar values
    // are only the DEFAULT.

    function jobTypeMap() {
        var el = document.getElementById('jobTypesByMolecule');
        if (!el) return {};
        try { return JSON.parse(el.textContent || '{}'); } catch (e) { return {}; }
    }

    /** Repopulates the dialog's job-type list for the desk currently chosen IN THE DIALOG. */
    function onAddTableCompanyChanged(preferredJobTypeId) {
        var companySel = document.getElementById('addTableCompany');
        var jobSel = document.getElementById('addTableJobType');
        if (!companySel || !jobSel) return;

        var opt = companySel.options[companySel.selectedIndex];
        var moleculeId = opt ? opt.getAttribute('data-molecule') : '';
        var types = (jobTypeMap() || {})[moleculeId] || [];

        jobSel.innerHTML = '';
        types.forEach(function (t) {
            var o = document.createElement('option');
            // The server serialises the record as {Id, DisplayName}; System.Text.Json camel-cases by
            // default, so accept either shape rather than depending on the casing policy.
            o.value = (t.id !== undefined ? t.id : t.Id);
            o.textContent = (t.displayName !== undefined ? t.displayName : t.DisplayName);
            jobSel.appendChild(o);
        });

        if (preferredJobTypeId) jobSel.value = String(preferredJobTypeId);
        // If the preferred type doesn't exist in this desk's molecule the assignment above is a no-op
        // and the browser keeps the first option — which is a valid pairing, unlike the old behaviour.
    }

    function openAddTableDialog() {
        var modal = document.getElementById('addTableModal');
        if (!modal) return;

        // Default to what the user is looking at right now.
        var toolbarCompany = document.getElementById('companySelect');
        var toolbarJobType = document.getElementById('jobTypeSelect');
        var companySel = document.getElementById('addTableCompany');
        if (companySel && toolbarCompany) companySel.value = toolbarCompany.value;
        onAddTableCompanyChanged(toolbarJobType ? toolbarJobType.value : null);

        var nameInput = document.getElementById('addTableName');
        if (nameInput) nameInput.value = '';

        modal.classList.add('is-open');
        if (nameInput) nameInput.focus();
    }

    function closeAddTableDialog() {
        var modal = document.getElementById('addTableModal');
        if (modal) modal.classList.remove('is-open');
    }

    async function submitAddTable() {
        var nameInput = document.getElementById('addTableName');
        var name = (nameInput && nameInput.value ? nameInput.value : '').trim();
        if (!name) {
            showError((window.AppLocalizer && window.AppLocalizer.Team_NameRequired) || 'Please enter a name for the table.');
            return;
        }

        var companySel = document.getElementById('addTableCompany');
        var jobSel = document.getElementById('addTableJobType');
        var companyId = companySel ? companySel.value : '';
        var jobTypeId = jobSel ? jobSel.value : '';
        if (!companyId || !jobTypeId) {
            showError((window.AppLocalizer && window.AppLocalizer.Team_MissingSelection) || 'Choose a desk and a job type.');
            return;
        }

        try {
            var result = await postForm('/Calendar/Team?handler=AddView', {
                name: name,
                SelectedCompanyId: companyId,
                SelectedJobTypeId: jobTypeId
            });
            if (result.success) {
                closeAddTableDialog();
                location.reload();
            } else {
                showError(result.error);
            }
        } catch (err) {
            showError(null);
        }
    }

    async function deleteTeamTable(button) {
        var id = button.getAttribute('data-view-id');
        var wrapper = button.closest('.team-view-tab-wrapper');
        var name = wrapper ? wrapper.getAttribute('data-view-name') : '';
        var confirmMsg = (window.AppLocalizer && window.AppLocalizer.Team_DeleteTableConfirm) || ('Delete "' + name + '"?');
        if (!window.confirm(confirmMsg)) return;

        try {
            var result = await postForm('/Calendar/Team?handler=DeleteView', { id: id });
            if (result.success) {
                // If we're currently VIEWING the table being deleted (?ViewId=<id>), a plain reload would
                // re-request the now-deleted view and OnGet would surface the alarming "no longer accessible
                // / outside your permissions" banner for what is a normal delete. Drop ViewId so we fall
                // back cleanly to the default team view instead.
                var params = new URLSearchParams(window.location.search);
                if (params.get('ViewId') === String(id)) {
                    params.delete('ViewId');
                    var qs = params.toString();
                    window.location.href = window.location.pathname + (qs ? '?' + qs : '');
                } else {
                    location.reload();
                }
            } else {
                showError(result.error);
            }
        } catch (err) {
            showError(null);
        }
    }

    // Exposed as globals — mirrors the existing calendar-page convention (Overview's
    // saveNote()/deleteNote() etc.) of plain global functions wired via inline onclick=.
    // This file is inside an IIFE, so EVERY function referenced by an inline handler in
    // Team.cshtml must be assigned here or the button silently does nothing:
    //   openAddTableDialog / closeAddTableDialog / submitAddTable / onAddTableCompanyChanged / deleteTeamTable
    window.openAddTableDialog = openAddTableDialog;
    window.closeAddTableDialog = closeAddTableDialog;
    window.submitAddTable = submitAddTable;
    window.onAddTableCompanyChanged = onAddTableCompanyChanged;
    window.deleteTeamTable = deleteTeamTable;

    document.addEventListener('DOMContentLoaded', function () {
        // Enter in the name field saves; Escape closes. Matches form-like expectations now that the
        // add flow is a dialog rather than an inline box.
        var input = document.getElementById('addTableName');
        if (input) {
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') { e.preventDefault(); submitAddTable(); }
            });
        }
        var modal = document.getElementById('addTableModal');
        if (modal) {
            modal.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { e.preventDefault(); closeAddTableDialog(); }
            });
            // Clicking the backdrop (the wrapper itself, not the content) dismisses.
            modal.addEventListener('click', function (e) {
                if (e.target === modal) closeAddTableDialog();
            });
        }
    });
})();
