(function () {
    'use strict';

    var dragState = null; // { kind:'row'|'category', id, groupId, prevOrder }

    function getGrid() { return document.querySelector('.excel-calendar[data-reorder-context]'); }
    function ctxKey() { var g = getGrid(); return g ? g.getAttribute('data-reorder-context') : null; }

    function csrf() {
        var t = document.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }

    function postOrder(groupId, itemIds, revert) {
        var key = ctxKey();
        if (!key) return;
        function fail() {
            // #7: the DOM was moved optimistically; on a failed save, revert it so the UI never shows an
            // order the server rejected (previously it silently snapped back only on a full page reload).
            if (typeof revert === 'function') { try { revert(); } catch (e) { /* best-effort revert */ } }
            if (window.showToast) window.showToast('Could not save order', 'error');
        }
        fetch('/Api/Calendar/SaveRowOrder', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest', 'RequestVerificationToken': csrf() },
            credentials: 'same-origin',
            body: JSON.stringify({ contextKey: key, groupId: groupId, itemIds: itemIds })
        }).then(function (r) {
            if (!r.ok) fail();
        }).catch(function () {
            fail();
        });
    }

    // --- collect current DOM order ---
    function rowIdsInGroup(groupId) {
        var g = getGrid(); if (!g) return [];
        var sel = groupId
            ? 'tr[data-row-id][data-group-id="' + (window.CSS && CSS.escape ? CSS.escape(groupId) : groupId) + '"]'
            : 'tr[data-row-id]';
        return Array.prototype.slice.call(g.querySelectorAll(sel)).map(function (tr) { return tr.getAttribute('data-row-id'); });
    }
    function groupIdsInOrder() {
        var g = getGrid(); if (!g) return [];
        return Array.prototype.slice.call(g.querySelectorAll('tr.excel-calendar__group-header[data-group-id]'))
            .map(function (tr) { return tr.getAttribute('data-group-id'); });
    }

    // --- DOM move helpers ---
    function categoryBlock(groupId) {
        var g = getGrid(); var esc = (window.CSS && CSS.escape) ? CSS.escape(groupId) : groupId;
        var header = g.querySelector('tr.excel-calendar__group-header[data-group-id="' + esc + '"]');
        var nodes = header ? [header] : [];
        var n = header ? header.nextElementSibling : null;
        while (n && !(n.classList && n.classList.contains('excel-calendar__group-header'))) {
            if (n.matches && n.matches('tr[data-row-id]')) nodes.push(n);
            n = n.nextElementSibling;
        }
        return nodes;
    }

    // --- revert helpers (#7): re-apply a known id order to the live DOM ---
    function reorderRows(groupId, ids) {
        var g = getGrid(); if (!g || !ids) return;
        var esc = (window.CSS && CSS.escape) ? CSS.escape(groupId) : groupId;
        var sel = groupId ? 'tr[data-row-id][data-group-id="' + esc + '"]' : 'tr[data-row-id]';
        var nodes = Array.prototype.slice.call(g.querySelectorAll(sel));
        if (!nodes.length) return;
        var byId = {}; nodes.forEach(function (n) { byId[n.getAttribute('data-row-id')] = n; });
        var anchor = nodes[nodes.length - 1].nextSibling; // fixed slot after the group's rows
        ids.forEach(function (id) { var n = byId[id]; if (n) n.parentNode.insertBefore(n, anchor); });
    }
    function reorderCategories(ids) {
        var g = getGrid(); if (!g || !ids) return;
        var heads = Array.prototype.slice.call(g.querySelectorAll('tr.excel-calendar__group-header[data-group-id]'));
        if (!heads.length) return;
        var lastBlock = categoryBlock(heads[heads.length - 1].getAttribute('data-group-id'));
        var anchor = lastBlock.length ? lastBlock[lastBlock.length - 1].nextSibling : null;
        var parent = heads[0].parentNode;
        ids.forEach(function (id) {
            categoryBlock(id).forEach(function (n) { parent.insertBefore(n, anchor); });
        });
    }

    // --- enable on a grid (idempotent) ---
    function enable() {
        var grid = getGrid();
        if (!grid || grid._reorderBound) { if (grid) injectGrips(grid); return; }
        grid._reorderBound = true;
        injectGrips(grid);

        grid.addEventListener('dragstart', function (e) {
            var rowGrip = e.target.closest('.excel-calendar__row-grip');
            var catGrip = e.target.closest('.excel-calendar__group-grip');
            if (rowGrip) {
                var tr = rowGrip.closest('tr[data-row-id]');
                dragState = { kind: 'row', id: tr.getAttribute('data-row-id'), groupId: tr.getAttribute('data-group-id') || '' };
            } else if (catGrip) {
                var hr = catGrip.closest('tr.excel-calendar__group-header');
                dragState = { kind: 'category', id: hr.getAttribute('data-group-id'), groupId: '' };
            } else { return; }
            // #7: snapshot the pre-drag order so a failed save can be reverted.
            dragState.prevOrder = dragState.kind === 'row' ? rowIdsInGroup(dragState.groupId) : groupIdsInOrder();
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', dragState.id);
        });

        grid.addEventListener('dragover', function (e) {
            if (!dragState) return;
            var overRow = e.target.closest('tr[data-row-id]');
            var overHeader = e.target.closest('tr.excel-calendar__group-header');
            if (dragState.kind === 'row') {
                if (!overRow || (overRow.getAttribute('data-group-id') || '') !== dragState.groupId) { e.dataTransfer.dropEffect = 'none'; return; }
                e.preventDefault(); e.dataTransfer.dropEffect = 'move';
                moveBefore(currentRow(), overRow, e);
            } else { // category
                if (!overHeader) { e.dataTransfer.dropEffect = 'none'; return; }
                e.preventDefault(); e.dataTransfer.dropEffect = 'move';
            }
        });

        grid.addEventListener('drop', function (e) {
            if (!dragState) return;
            e.preventDefault();
            if (dragState.kind === 'row') {
                var gid = dragState.groupId, prevRows = dragState.prevOrder;
                postOrder(gid, rowIdsInGroup(gid), function () { reorderRows(gid, prevRows); });
            } else {
                var overHeader = e.target.closest('tr.excel-calendar__group-header');
                if (overHeader && overHeader.getAttribute('data-group-id') !== dragState.id) {
                    var block = categoryBlock(dragState.id);
                    block.forEach(function (node) { overHeader.parentNode.insertBefore(node, overHeader); });
                }
                var prevCats = dragState.prevOrder;
                postOrder('', groupIdsInOrder(), function () { reorderCategories(prevCats); });
            }
            dragState = null;
        });

        grid.addEventListener('dragend', function () { dragState = null; });
    }

    function currentRow() {
        var g = getGrid(); var esc = (window.CSS && CSS.escape) ? CSS.escape(dragState.id) : dragState.id;
        return g.querySelector('tr[data-row-id="' + esc + '"]');
    }
    function moveBefore(node, target, e) {
        if (!node || node === target) return;
        var rect = target.getBoundingClientRect();
        var after = e.clientY > rect.top + rect.height / 2;
        target.parentNode.insertBefore(node, after ? target.nextSibling : target);
    }

    function injectGrips(grid) {
        // row grips
        grid.querySelectorAll('tr[data-row-id] .excel-calendar__row-label').forEach(function (cell) {
            if (cell.querySelector('.excel-calendar__row-grip')) return;
            var grip = document.createElement('span');
            grip.className = 'excel-calendar__row-grip';
            grip.setAttribute('draggable', 'true');
            grip.setAttribute('role', 'button');
            grip.setAttribute('tabindex', '0');
            grip.setAttribute('aria-label', (window.AppLocalizer && window.AppLocalizer.Calendar_DragRow) || 'Drag to reorder');
            grip.textContent = '⋮⋮';
            cell.insertBefore(grip, cell.firstChild);
        });
        // category grips already in markup (.excel-calendar__group-grip) — make draggable
        grid.querySelectorAll('.excel-calendar__group-grip').forEach(function (grip) {
            grip.setAttribute('draggable', 'true');
            grip.setAttribute('role', 'button');
            grip.setAttribute('tabindex', '0');
        });
    }

    // --- keyboard a11y: Alt+Arrow on focused grip ---
    document.addEventListener('keydown', function (e) {
        if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
        var rowGrip = e.target.closest && e.target.closest('.excel-calendar__row-grip');
        var catGrip = e.target.closest && e.target.closest('.excel-calendar__group-grip');
        if (!rowGrip && !catGrip) return;
        e.preventDefault();
        var dir = e.key === 'ArrowUp' ? -1 : 1;
        if (rowGrip) {
            var tr = rowGrip.closest('tr[data-row-id]');
            var gid = tr.getAttribute('data-group-id') || '';
            var prevRows = rowIdsInGroup(gid); // #7: snapshot before the move
            var sibs = Array.prototype.slice.call(getGrid().querySelectorAll('tr[data-row-id][data-group-id="' + ((window.CSS&&CSS.escape)?CSS.escape(gid):gid) + '"]'));
            var i = sibs.indexOf(tr), j = i + dir;
            if (j < 0 || j >= sibs.length) return;
            tr.parentNode.insertBefore(dir < 0 ? tr : sibs[j], dir < 0 ? sibs[j] : tr);
            rowGrip.focus();
            postOrder(gid, rowIdsInGroup(gid), function () { reorderRows(gid, prevRows); });
        } else {
            var hr = catGrip.closest('tr.excel-calendar__group-header');
            var prevCats = groupIdsInOrder(); // #7: snapshot before the move
            var heads = Array.prototype.slice.call(getGrid().querySelectorAll('tr.excel-calendar__group-header'));
            var ci = heads.indexOf(hr), cj = ci + dir;
            if (cj < 0 || cj >= heads.length) return;
            var block = categoryBlock(hr.getAttribute('data-group-id'));
            var anchor = heads[cj];
            if (dir < 0) block.forEach(function (n) { anchor.parentNode.insertBefore(n, anchor); });
            else { var afterBlock = categoryBlock(anchor.getAttribute('data-group-id')); var ref = afterBlock[afterBlock.length-1].nextSibling; block.forEach(function (n) { anchor.parentNode.insertBefore(n, ref); }); }
            catGrip.focus();
            postOrder('', groupIdsInOrder(), function () { reorderCategories(prevCats); });
        }
    });

    function init() { if (getGrid()) enable(); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
    document.addEventListener('calendar:grid-refreshed', function () {
        var g = getGrid(); if (g) { g._reorderBound = false; enable(); }
    });
})();
