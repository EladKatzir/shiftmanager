/**
 * Friends Highlight - Calendar friend highlighting toggle
 * Fetches friend IDs from /Api/Friends/Ids and toggles .is-friend
 * class on matching [data-user-id] elements.
 */
(function () {
    'use strict';

    var _friendIds = null;
    var _isActive = false;

    window.toggleFriendsHighlight = function () {
        _isActive = !_isActive;

        var btn = document.getElementById('friendsToggle');
        if (btn) {
            btn.classList.toggle('btn-primary', _isActive);
            btn.classList.toggle('btn-ghost', !_isActive);
        }

        if (_isActive) {
            if (_friendIds !== null) {
                applyHighlight(_friendIds);
            } else {
                fetchFriendIds(function (ids) {
                    _friendIds = ids;
                    applyHighlight(ids);
                });
            }
        } else {
            removeHighlight();
        }
    };

    function fetchFriendIds(callback) {
        fetch('/Api/Friends/Ids', { credentials: 'same-origin' })
            .then(function (res) { return res.json(); })
            .then(function (data) {
                if (data.success && data.friendIds) {
                    var idSet = {};
                    data.friendIds.forEach(function (id) { idSet[id] = true; });
                    callback(idSet);
                } else {
                    callback({});
                }
            })
            .catch(function (err) {
                console.error('[Friends] Failed to fetch friend IDs:', err);
                callback({});
            });
    }

    function applyHighlight(idSet) {
        document.querySelectorAll('[data-user-id]').forEach(function (el) {
            var uid = el.getAttribute('data-user-id');
            if (uid && idSet[uid]) {
                el.classList.add('is-friend');
            }
        });
    }

    function removeHighlight() {
        document.querySelectorAll('.is-friend').forEach(function (el) {
            el.classList.remove('is-friend');
        });
    }
})();
