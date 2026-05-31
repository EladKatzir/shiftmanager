// calendar-sticky-shadows.js
// Manages sticky-related CSS classes via IntersectionObserver:
//   .is-scrolled-x   — set when the calendar's inline-start edge has been scrolled past
//   .is-scrolled-y   — set when the calendar's block-start edge has been scrolled past
//   .is-group-pinned — set when any group header band is currently pinned
//   .is-pinned       — set on individual band <tr>s when they're pinned
//   .is-pinned       — set on .cal-toolbar when its sentinel leaves the viewport
//   .is-condensed    — set on .cal-toolbar after 200px of vertical scroll on .excel-calendar
//
// Also sets --cal-toolbar-height on .cal-page via ResizeObserver so the
// readonly banner's `top:` offset stays in sync with the actual toolbar height.

(function () {
    'use strict';

    // Full-page reload architecture — no SPA navigation, so no observer disconnect
    // is needed. If the project ever moves to SPA navigation, disconnect all
    // IntersectionObservers and ResizeObservers here on page unmount.

    const SCROLL_X_CLASS = 'is-scrolled-x';
    const SCROLL_Y_CLASS = 'is-scrolled-y';
    const GROUP_PIN_CLASS = 'is-group-pinned';
    // .is-pinned is a shared class name — used distinctly by:
    //   .cal-toolbar.is-pinned        — toolbar pinned to .cal-page top
    //   .excel-calendar__group-header.is-pinned — group band pinned to thead bottom
    // Disambiguated by selector context in calendar.css; named twice here for
    // semantic clarity even though both constants resolve to the same string.
    const BAND_PIN_CLASS = 'is-pinned';
    const TOOLBAR_PIN_CLASS = 'is-pinned';
    const TOOLBAR_CONDENSE_CLASS = 'is-condensed';
    const CONDENSE_THRESHOLD = 200; // px of vertical scroll on .excel-calendar

    function init() {
        document.querySelectorAll('.excel-calendar').forEach(wireCalendar);
        document.querySelectorAll('.cal-toolbar').forEach(wireToolbar);
    }

    function wireCalendar(calendar) {
        // Inject sentinels at the leading edges (1×1 absolute boxes).
        const yToken = document.createElement('div');
        yToken.className = 'excel-calendar__scroll-sentinel excel-calendar__scroll-sentinel--y';
        yToken.setAttribute('aria-hidden', 'true');
        const xToken = document.createElement('div');
        xToken.className = 'excel-calendar__scroll-sentinel excel-calendar__scroll-sentinel--x';
        xToken.setAttribute('aria-hidden', 'true');
        calendar.prepend(yToken, xToken);

        // Axis discrimination via rootMargin. Both sentinels share the same
        // (0, 0) position inside the calendar, so a naive observer with no
        // rootMargin fires on ANY scroll direction. The fix: extend the
        // observer's effective root horizontally (for ioY) and vertically
        // (for ioX) by 100% on each side, so the orthogonal axis can never
        // push the sentinel out of the root. Only the targeted axis matters.
        const ioY = new IntersectionObserver(
            (entries) => calendar.classList.toggle(SCROLL_Y_CLASS, !entries[0].isIntersecting),
            { root: calendar, rootMargin: '0px 100% 0px 100%', threshold: 0 }
        );
        ioY.observe(yToken);

        const ioX = new IntersectionObserver(
            (entries) => calendar.classList.toggle(SCROLL_X_CLASS, !entries[0].isIntersecting),
            { root: calendar, rootMargin: '100% 0px 100% 0px', threshold: 0 }
        );
        ioX.observe(xToken);

        // Group band pin detection — toggles .is-pinned on each band's <tr> when it
        // scrolls behind the sticky thead. Consumed by:
        //   .excel-calendar.is-group-pinned .excel-calendar__group-header.is-pinned td
        //     { box-shadow: var(--shadow-sticky-block) } in calendar.css
        // Read --excel-calendar-header-height once — it's a CSS custom property on
        // the calendar element, identical for every band. Was previously computed
        // inside the loop, costing one getComputedStyle call per group.
        const headerHeight = parseInt(
            getComputedStyle(calendar).getPropertyValue('--excel-calendar-header-height') || '44',
            10
        );

        const bands = calendar.querySelectorAll('.excel-calendar__group-header');
        bands.forEach((band) => {
            const ioBand = new IntersectionObserver(
                (entries) => {
                    const pinned = !entries[0].isIntersecting;
                    band.classList.toggle(BAND_PIN_CLASS, pinned);
                    const anyPinned = !!calendar.querySelector('.excel-calendar__group-header.is-pinned');
                    calendar.classList.toggle(GROUP_PIN_CLASS, anyPinned);
                },
                { root: calendar, rootMargin: `-${headerHeight + 1}px 0px 0px 0px`, threshold: 0 }
            );
            ioBand.observe(band);
        });

        // Toolbar reference stable for page lifetime (full-page-reload architecture).
        // Closed over by the scroll listener below to avoid querying the DOM per frame.
        const toolbar = calendar.closest('.cal-page')?.querySelector('.cal-toolbar');

        // Condense listener.
        let lastScrollTop = 0;
        calendar.addEventListener('scroll', () => {
            if (calendar.scrollTop === lastScrollTop) return;
            lastScrollTop = calendar.scrollTop;
            if (!toolbar) return;
            requestAnimationFrame(() => {
                toolbar.classList.toggle(
                    TOOLBAR_CONDENSE_CLASS,
                    calendar.scrollTop > CONDENSE_THRESHOLD
                );
            });
        }, { passive: true });
    }

    function wireToolbar(toolbar) {
        const page = toolbar.closest('.cal-page');
        if (!page) return;

        // Sentinel sits immediately above the toolbar — when it leaves the
        // viewport, the toolbar is pinned.
        const sentinel = document.createElement('div');
        sentinel.className = 'cal-toolbar-sentinel';
        sentinel.setAttribute('aria-hidden', 'true');
        toolbar.parentNode.insertBefore(sentinel, toolbar);

        const io = new IntersectionObserver(
            (entries) => toolbar.classList.toggle(TOOLBAR_PIN_CLASS, !entries[0].isIntersecting),
            { threshold: 0 }
        );
        io.observe(sentinel);

        // ResizeObserver: keep --cal-toolbar-height in sync.
        const ro = new ResizeObserver(() => {
            page.style.setProperty('--cal-toolbar-height', toolbar.offsetHeight + 'px');
        });
        ro.observe(toolbar);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Next-group jump. Delegated handler — survives bands being toggled.
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.excel-calendar__next-group-btn');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();   // don't trigger the band's chevron-toggle

        var nextId = btn.getAttribute('data-next-group');
        var target = document.querySelector(
            '.excel-calendar__group-header[data-group-id="' + nextId + '"]'
        );
        if (!target) return;

        var reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        target.scrollIntoView({
            block: 'start',
            behavior: reducedMotion ? 'auto' : 'smooth'
        });
    });
})();
