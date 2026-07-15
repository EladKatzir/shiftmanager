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

    async function addTeamTable() {
        var input = document.getElementById('newTableName');
        var name = (input && input.value ? input.value : '').trim();
        if (!name) {
            showError((window.AppLocalizer && window.AppLocalizer.Team_NameRequired) || 'Please enter a name for the table.');
            return;
        }

        var companyId = document.getElementById('companySelect') ? document.getElementById('companySelect').value : '';
        var jobTypeId = document.getElementById('jobTypeSelect') ? document.getElementById('jobTypeSelect').value : '';

        try {
            var result = await postForm('/Calendar/Team?handler=AddView', {
                name: name,
                SelectedCompanyId: companyId,
                SelectedJobTypeId: jobTypeId
            });
            if (result.success) {
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
                location.reload();
            } else {
                showError(result.error);
            }
        } catch (err) {
            showError(null);
        }
    }

    // Exposed as globals — mirrors the existing calendar-page convention (Overview's
    // saveNote()/deleteNote() etc.) of plain global functions wired via inline onclick=.
    window.addTeamTable = addTeamTable;
    window.deleteTeamTable = deleteTeamTable;

    // Enter key in the "new table name" field submits, matching form-like expectations.
    document.addEventListener('DOMContentLoaded', function () {
        var input = document.getElementById('newTableName');
        if (input) {
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    addTeamTable();
                }
            });
        }
    });
})();
