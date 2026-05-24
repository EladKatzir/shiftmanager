/*
 * distribution-lists.js — by-user calendar "Lists" control + manager/editor modals.
 *
 * Filter dropdown (toggle/apply/clear) is available to everyone in the by-user view: it just reads the
 * server-rendered checkboxes and navigates with ?DistributionListIds=... (URLSearchParams preserves the
 * rest of the query). The manager/editor modals are manager-only; they call the AJAX handlers on
 * /Calendar/ManageDistributionLists. After any mutation the page reloads so the toolbar dropdown and any
 * active list-grouping reflect the change.
 */
(function () {
    'use strict';

    var HANDLER = '/Calendar/ManageDistributionLists';
    var moleculeId = null;
    var LOC = {};
    var selectedUserIds = new Set();   // current selection in the editor
    var editorListId = 0;              // 0 = creating, else editing

    function init() {
        var ctl = document.getElementById('dlControl');
        if (ctl) moleculeId = parseInt(ctl.getAttribute('data-molecule-id'), 10) || null;

        var locEl = document.getElementById('dlLoc');
        if (locEl) { try { LOC = JSON.parse(locEl.textContent) || {}; } catch (e) { LOC = {}; } }

        document.addEventListener('click', onDocClick);
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') { closeEditor(); closeManager(); } });
    }

    // ---------- helpers ----------
    function t(key, fallback) { return (LOC && LOC[key]) || fallback || ''; }
    function fmt(s, v) { return String(s || '').replace('{0}', v); }
    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    function postHeaders() {
        var h = { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' };
        var tok = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tok) h['RequestVerificationToken'] = tok.value;
        return h;
    }
    function toast(kind, msg) {
        if (window.Toast && typeof window.Toast[kind] === 'function') window.Toast[kind](msg);
        else if (window.FeedbackModal) window.FeedbackModal.show(kind === 'error' ? 'error' : 'success', msg);
    }

    // ---------- filter dropdown (open to all) ----------
    function toggleDropdown() {
        var menu = document.getElementById('dlDropdownMenu');
        var toggle = document.getElementById('dlDropdownToggle');
        if (!menu) return;
        var willOpen = menu.hidden;
        menu.hidden = !willOpen;
        if (toggle) toggle.setAttribute('aria-expanded', String(willOpen));
    }

    function onDocClick(e) {
        var menu = document.getElementById('dlDropdownMenu');
        if (!menu || menu.hidden) return;
        if (!(e.target.closest && e.target.closest('.dl-dropdown'))) {
            menu.hidden = true;
            var tg = document.getElementById('dlDropdownToggle');
            if (tg) tg.setAttribute('aria-expanded', 'false');
        }
    }

    function applySelection() {
        var ids = Array.prototype.slice
            .call(document.querySelectorAll('#dlDropdownMenu .dl-checkbox:checked'))
            .map(function (c) { return c.value; });
        var params = new URLSearchParams(window.location.search);
        if (ids.length) params.set('DistributionListIds', ids.join(',')); else params.delete('DistributionListIds');
        params.set('Mode', 'user');
        window.location.search = params.toString();
    }

    function clearSelection() {
        var params = new URLSearchParams(window.location.search);
        params.delete('DistributionListIds');
        params.set('Mode', 'user');
        window.location.search = params.toString();
    }

    // ---------- modal show/hide (components.css .modal uses the .is-open class) ----------
    function showModal(id) {
        var b = document.getElementById('dlBackdrop');
        var m = document.getElementById(id);
        if (b) b.classList.add('is-open');
        if (m) m.classList.add('is-open');
        document.body.style.overflow = 'hidden';
    }
    function hideModal(id) {
        var m = document.getElementById(id);
        if (m) m.classList.remove('is-open');
        var anyOpen = ['dlManagerModal', 'dlEditorModal'].some(function (x) {
            var el = document.getElementById(x); return el && el.classList.contains('is-open');
        });
        if (!anyOpen) {
            var b = document.getElementById('dlBackdrop');
            if (b) b.classList.remove('is-open');
            document.body.style.overflow = '';
        }
    }

    // ---------- manager modal ----------
    function openManager() { showModal('dlManagerModal'); renderManagerList(); }
    function closeManager() { hideModal('dlManagerModal'); }

    // Reads the lists from the server-rendered dropdown (always current on page load; refreshed by reload after mutations).
    function renderManagerList() {
        var host = document.getElementById('dlManagerList');
        var empty = document.getElementById('dlManagerEmpty');
        if (!host) return;
        var items = Array.prototype.slice.call(document.querySelectorAll('#dlDropdownMenu .dl-dropdown__item'));
        host.innerHTML = '';
        if (items.length === 0) { if (empty) empty.hidden = false; return; }
        if (empty) empty.hidden = true;

        items.forEach(function (item) {
            var cb = item.querySelector('.dl-checkbox');
            var id = cb ? cb.value : '';
            var name = (item.querySelector('.dl-dropdown__item-name') || {}).textContent || '';
            var count = (item.querySelector('.dl-dropdown__item-count') || {}).textContent || '';
            var row = document.createElement('div');
            row.className = 'dl-manager__row';
            row.setAttribute('data-list-id', id);
            row.innerHTML =
                '<div class="dl-manager__info">' +
                '<span class="dl-manager__name">' + escapeHtml(name) + '</span>' +
                '<span class="dl-manager__count">' + escapeHtml(count) + '</span>' +
                '</div>' +
                '<div class="dl-manager__actions">' +
                '<button type="button" class="btn btn-ghost btn--sm dl-edit">' + escapeHtml(t('edit', 'Edit')) + '</button>' +
                '<button type="button" class="btn btn-ghost btn--sm dl-del">' + escapeHtml(t('delete', 'Delete')) + '</button>' +
                '</div>';
            row.querySelector('.dl-edit').addEventListener('click', function () { openEditor(parseInt(id, 10)); });
            row.querySelector('.dl-del').addEventListener('click', function () { confirmDelete(row, parseInt(id, 10)); });
            host.appendChild(row);
        });
    }

    // Inline (no native confirm) two-step delete confirmation within the row.
    function confirmDelete(row, listId) {
        if (!row || row.querySelector('.dl-manager__confirm')) return;
        var actions = row.querySelector('.dl-manager__actions');
        if (actions) actions.style.display = 'none';
        var box = document.createElement('div');
        box.className = 'dl-manager__confirm';
        box.innerHTML =
            '<span class="dl-manager__confirm-text">' + escapeHtml(t('confirmDelete', 'Delete this distribution list?')) + '</span>' +
            '<button type="button" class="btn btn-danger btn--sm dl-confirm-yes">' + escapeHtml(t('delete', 'Delete')) + '</button>' +
            '<button type="button" class="btn btn-ghost btn--sm dl-confirm-no">' + escapeHtml(t('cancel', 'Cancel')) + '</button>';
        box.querySelector('.dl-confirm-yes').addEventListener('click', function () { doDelete(listId); });
        box.querySelector('.dl-confirm-no').addEventListener('click', function () {
            box.remove(); if (actions) actions.style.display = '';
        });
        row.appendChild(box);
    }

    async function doDelete(listId) {
        try {
            var res = await fetch(HANDLER + '?handler=Delete', {
                method: 'POST', headers: postHeaders(), credentials: 'same-origin',
                body: JSON.stringify({ listId: listId })
            });
            await handleMutationResponse(res);
        } catch (e) { toast('error', t('saveFailed', 'Could not save the distribution list.')); }
    }

    // ---------- editor modal ----------
    async function openEditor(listId) {
        editorListId = listId || 0;
        selectedUserIds = new Set();
        var nameInput = document.getElementById('dlEditorName');
        var search = document.getElementById('dlEditorSearch');
        var title = document.getElementById('dlEditorTitle');
        if (nameInput) nameInput.value = '';
        if (search) search.value = '';
        if (title) title.textContent = editorListId ? t('editTitle', 'Edit list') : t('createTitle', 'New list');

        if (editorListId) {
            try {
                var res = await fetch(HANDLER + '?handler=List&listId=' + editorListId + '&moleculeId=' + moleculeId, { credentials: 'same-origin' });
                if (res.ok) {
                    var data = await res.json();
                    if (data && data.ok) {
                        if (nameInput) nameInput.value = data.name || '';
                        (data.memberUserIds || []).forEach(function (id) { selectedUserIds.add(id); });
                    }
                }
            } catch (e) { /* fall through to empty editor */ }
        }

        showModal('dlEditorModal');
        await loadUsers('');
    }
    function closeEditor() { hideModal('dlEditorModal'); }

    var searchTimer = null;
    function onUserSearch(value) {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(function () { loadUsers(value); }, 200);
    }

    async function loadUsers(search) {
        var host = document.getElementById('dlEditorUserList');
        var empty = document.getElementById('dlEditorNoUsers');
        if (!host) return;
        try {
            var url = HANDLER + '?handler=MoleculeUsers&moleculeId=' + moleculeId + '&search=' + encodeURIComponent(search || '');
            var res = await fetch(url, { credentials: 'same-origin' });
            if (!res.ok) { host.innerHTML = ''; if (empty) empty.hidden = false; return; }
            var data = await res.json();
            var users = (data && data.users) || [];
            host.innerHTML = '';
            if (users.length === 0) { if (empty) empty.hidden = false; updateSelectedCount(); return; }
            if (empty) empty.hidden = true;

            users.forEach(function (u) {
                var row = document.createElement('label');
                row.className = 'dl-editor__user';
                var avatar = u.avatarUrl
                    ? '<img class="dl-editor__avatar" src="' + escapeHtml(u.avatarUrl) + '" alt="" loading="lazy" />'
                    : '<span class="dl-editor__avatar dl-editor__avatar--initials">' + escapeHtml(initials(u.displayName)) + '</span>';
                row.innerHTML =
                    '<input type="checkbox" class="dl-user-checkbox" value="' + u.id + '"' + (selectedUserIds.has(u.id) ? ' checked' : '') + ' />' +
                    avatar +
                    '<span class="dl-editor__user-info">' +
                    '<span class="dl-editor__user-name">' + escapeHtml(u.displayName) + '</span>' +
                    '<span class="dl-editor__user-company">' + escapeHtml(u.companyName) + '</span>' +
                    '</span>';
                var cb = row.querySelector('.dl-user-checkbox');
                cb.addEventListener('change', function () {
                    if (cb.checked) selectedUserIds.add(u.id); else selectedUserIds.delete(u.id);
                    updateSelectedCount();
                });
                host.appendChild(row);
            });
            updateSelectedCount();
        } catch (e) {
            host.innerHTML = ''; if (empty) empty.hidden = false;
        }
    }

    function initials(name) {
        var parts = String(name || '').trim().split(/\s+/).filter(Boolean);
        if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
        if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
        return '?';
    }

    function updateSelectedCount() {
        var el = document.getElementById('dlEditorSelectedCount');
        if (el) el.textContent = fmt(t('selectedCount', '{0} selected'), selectedUserIds.size);
    }

    async function save() {
        var nameInput = document.getElementById('dlEditorName');
        var name = nameInput ? nameInput.value.trim() : '';
        if (!name) { toast('error', t('nameRequired', 'List name is required.')); if (nameInput) nameInput.focus(); return; }
        if (selectedUserIds.size === 0) { toast('error', t('selectAtLeastOne', 'Select at least one member.')); return; }

        var saveBtn = document.getElementById('dlEditorSave');
        if (saveBtn) saveBtn.disabled = true;

        var body, handler;
        if (editorListId) {
            handler = 'Update';
            body = { listId: editorListId, name: name, userIds: Array.from(selectedUserIds) };
        } else {
            handler = 'Create';
            body = { moleculeId: moleculeId, name: name, userIds: Array.from(selectedUserIds) };
        }

        try {
            var res = await fetch(HANDLER + '?handler=' + handler, {
                method: 'POST', headers: postHeaders(), credentials: 'same-origin', body: JSON.stringify(body)
            });
            await handleMutationResponse(res);
        } catch (e) {
            toast('error', t('saveFailed', 'Could not save the distribution list.'));
        } finally {
            if (saveBtn) saveBtn.disabled = false;
        }
    }

    // Shared handling for create/update/delete responses: on success reload (refresh dropdown + grouping).
    async function handleMutationResponse(res) {
        var data = null;
        try { data = await res.json(); } catch (e) { /* ignore */ }
        if (res.ok && data && data.ok) {
            if (data.message) try { sessionStorage.setItem('dlToast', data.message); } catch (e) { }
            window.location.reload();
            return;
        }
        if (res.status === 403) { toast('error', t('saveFailed', 'Could not save the distribution list.')); return; }
        var msg = (data && data.error) || t('saveFailed', 'Could not save the distribution list.');
        toast('error', msg);
    }

    // Show a queued success toast after the post-mutation reload.
    function flushQueuedToast() {
        try {
            var m = sessionStorage.getItem('dlToast');
            if (m) { sessionStorage.removeItem('dlToast'); toast('success', m); }
        } catch (e) { }
    }

    window.DistributionLists = {
        toggleDropdown: toggleDropdown,
        applySelection: applySelection,
        clearSelection: clearSelection,
        openManager: openManager,
        closeManager: closeManager,
        openEditor: openEditor,
        closeEditor: closeEditor,
        onUserSearch: onUserSearch,
        save: save
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(); flushQueuedToast(); });
    } else {
        init(); flushQueuedToast();
    }
})();
