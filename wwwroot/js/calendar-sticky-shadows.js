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

    const SCROLL_X_CLASS = 'is-scrolled-x';
    const SCROLL_Y_CLASS = 'is-scrolled-y';
    const GROUP_PIN_CLASS = 'is-group-pinned';
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

        const ioY = new IntersectionObserver(
            (entries) => calendar.classList.toggle(SCROLL_Y_CLASS, !entries[0].isIntersecting),
            { root: calendar, threshold: 0 }
        );
        ioY.observe(yToken);

        const ioX = new IntersectionObserver(
            (entries) => calendar.classList.toggle(SCROLL_X_CLASS, !entries[0].isIntersecting),
            { root: calendar, threshold: 0 }
        );
        ioX.observe(xToken);

        // Group bands — pin detection.
        const bands = calendar.querySelectorAll('.excel-calendar__group-header');
        bands.forEach((band) => {
            const headerHeight = parseInt(
                getComputedStyle(calendar).getPropertyValue('--excel-calendar-header-height') || '44',
                10
            );
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

        // Condense listener.
        let lastScrollTop = 0;
        calendar.addEventListener('scroll', () => {
            if (calendar.scrollTop === lastScrollTop) return;
            lastScrollTop = calendar.scrollTop;
            requestAnimationFrame(() => {
                const toolbar = calendar.closest('.cal-page')?.querySelector('.cal-toolbar');
                if (toolbar) {
                    toolbar.classList.toggle(
                        TOOLBAR_CONDENSE_CLASS,
                        calendar.scrollTop > CONDENSE_THRESHOLD
                    );
                }
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
})();
