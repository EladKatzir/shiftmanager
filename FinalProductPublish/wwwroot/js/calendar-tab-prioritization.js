/**
 * Calendar Tab Prioritization (Phase E, PF8) — the ONE shared grouping/ordering + off-tab warning
 * utility used by all three assignment pickers (quick-entry, bottom-sheet, trainee). A tab NEVER
 * hard-restricts: it only sorts "this tab" first and warns (once per tab/session) on an off-tab pick.
 * Ordering is applied at RENDER time (never baked into any cache — SEL-6).
 */
(function () {
    'use strict';

    // Which tabs have already fired their one-time off-tab warning (UD4). Backed by sessionStorage so the
    // "once per (tab, session)" contract survives navigations — every tab switch / week-nav is a full page
    // load, which would otherwise reset a plain in-memory object and re-fire the warning after each nav.
    // The in-memory map stays as a fast path; sessionStorage is the durable store. Access is guarded (it
    // can throw in some private-mode configs) — on failure we degrade to in-memory-only.
    var warnedTabs = {};

    function cfg() { return window.CalendarPageConfig || {}; }

    function warnKey(tabId) { return 'ss-offtabwarn-' + (tabId == null ? 'none' : tabId); }

    function hasWarned(key) {
        if (warnedTabs[key]) return true;
        try {
            if (window.sessionStorage && sessionStorage.getItem(key) === '1') {
                warnedTabs[key] = true; // hydrate the in-memory fast path
                return true;
            }
        } catch (e) { /* sessionStorage unavailable (private mode) — rely on in-memory only */ }
        return false;
    }

    function markWarned(key) {
        warnedTabs[key] = true;
        try { if (window.sessionStorage) sessionStorage.setItem(key, '1'); } catch (e) { /* ignore */ }
    }

    function isActive() {
        return !!cfg().tabPrioritize; // true only for a real tab that opts in AND has ≥1 company (server)
    }

    function label(which) {
        var L = window.AppLocalizer || {};
        return which === 'this'
            ? (L.CalendarTab_Group_ThisTab || 'This tab')
            : (L.CalendarTab_Group_Other || 'Other in molecule');
    }

    // users: [{ id, inTab, ... }] in the caller's current order. Returns two arrays, order preserved
    // within each group (stable). When inactive, everything is "other" (callers render a flat list).
    function partition(users) {
        var thisTab = [], other = [];
        (users || []).forEach(function (u) {
            if (isActive() && u && u.inTab) thisTab.push(u); else other.push(u);
        });
        return { thisTab: thisTab, other: other };
    }

    // Non-blocking, dismissible, at-most-once-per-(tab, session) warning on an off-tab pick (UD4).
    // Uses FeedbackModal (a separate surface from Toast) so it never stacks over / gets wiped by the
    // transient success toast. No-op when prioritization is inactive or the picked user is in-tab.
    function maybeWarnOffTab(user) {
        if (!isActive() || !user || user.inTab) return;
        var key = warnKey(cfg().activeTabId);
        if (hasWarned(key)) return;
        markWarned(key);
        var msg = (window.AppLocalizer && window.AppLocalizer.CalendarTab_OffTabWarning)
                  || "This user isn't from this tab's companies.";
        if (window.FeedbackModal && typeof window.FeedbackModal.show === 'function') {
            window.FeedbackModal.show('warning', msg);
        } else if (window.showToast) {
            window.showToast(msg, 'warning');
        }
    }

    window.CalendarTabPrioritization = {
        isActive: isActive,
        partition: partition,
        label: label,
        maybeWarnOffTab: maybeWarnOffTab
    };
})();
