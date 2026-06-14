"""
Task 1: Baseline diagnosis for Admin UI fixes.
- /Admin/Users: table breakout bug
- /Admin/Analytics: donut chart rendering bug
No production code changes. Screenshots + measurements only.
"""
import json
import os
from playwright.sync_api import sync_playwright

ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
BASE_URL = "http://localhost:5000"
LOGIN_EMAIL = "owner2@test"
LOGIN_PASSWORD = "Test1234!"

WIDTHS = [1440, 820, 375]
MODES = [
    ("light-ltr", "en"),
    ("dark-ltr", "en"),
    ("hebrew-rtl", "he"),
]

def login(page):
    page.goto(f"{BASE_URL}/Auth/Login", wait_until="networkidle")
    # Fill login form
    page.fill('input[type="email"], input[name="Input.Email"], input[id*="email" i], input[placeholder*="email" i], input[name*="email" i]', LOGIN_EMAIL)
    page.fill('input[type="password"]', LOGIN_PASSWORD)
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")
    print(f"After login, URL: {page.url}")

def set_language(page, lang):
    """Set language cookie via the culture endpoint."""
    if lang == "he":
        page.goto(f"{BASE_URL}/Culture/SetCulture?culture=he-IL&redirectUri=/", wait_until="networkidle")
    else:
        page.goto(f"{BASE_URL}/Culture/SetCulture?culture=en-US&redirectUri=/", wait_until="networkidle")

def set_dark_mode(page, dark):
    """Toggle dark mode via the sidebar theme switch if needed."""
    # Navigate home first
    page.goto(f"{BASE_URL}/", wait_until="networkidle")
    # Check current theme
    current_theme = page.evaluate("() => document.documentElement.getAttribute('data-theme') || document.body.getAttribute('data-theme') || document.documentElement.className")
    print(f"  Current theme state: {current_theme!r}")

    is_dark = "dark" in str(current_theme).lower()
    if dark and not is_dark:
        # Click dark mode toggle
        toggle = page.query_selector('[data-theme-toggle], .theme-toggle, #themeToggle, [onclick*="theme"], [onclick*="dark"]')
        if toggle:
            toggle.click()
            page.wait_for_timeout(500)
        else:
            # Try via JS
            page.evaluate("() => { document.documentElement.setAttribute('data-theme','dark'); }")
    elif not dark and is_dark:
        toggle = page.query_selector('[data-theme-toggle], .theme-toggle, #themeToggle, [onclick*="theme"], [onclick*="dark"]')
        if toggle:
            toggle.click()
            page.wait_for_timeout(500)
        else:
            page.evaluate("() => { document.documentElement.setAttribute('data-theme','light'); }")

def screenshot(page, name):
    path = os.path.join(ARTIFACTS, name)
    page.screenshot(path=path, full_page=True)
    print(f"  Saved: {name}")
    return path

def run_users_diagnosis(page, width):
    """Run JS diagnostics on the Users page."""
    # Check if body overflows viewport
    overflows = page.evaluate("() => document.documentElement.scrollWidth > document.documentElement.clientWidth")

    # Walk up from #usersTable
    ancestor_chain = page.evaluate("""() => {
        let el = document.querySelector('#usersTable');
        if (!el) el = document.querySelector('table');
        if (!el) return [{error: 'No table found'}];
        const out = [];
        while (el) {
            const cs = getComputedStyle(el);
            out.push({
                tag: el.tagName,
                cls: el.className,
                id: el.id || null,
                scrollWidth: el.scrollWidth,
                clientWidth: el.clientWidth,
                offsetWidth: el.offsetWidth,
                overflowX: cs.overflowX,
                minWidth: cs.minWidth,
                display: cs.display,
                width: cs.width,
                maxWidth: cs.maxWidth
            });
            el = el.parentElement;
            if (out.length > 20) break;
        }
        return out;
    }""")
    return {"scrollWidthOverflows": overflows, "ancestorChain": ancestor_chain}

def run_donut_diagnosis(page):
    """Run JS diagnostics on the Analytics page."""
    result = page.evaluate("""() => {
        const w = document.querySelector('.donut-wrap');
        const svg = document.querySelector('.donut-svg');
        const inner = document.querySelector('.hero-chart-inner');
        const val = document.querySelector('.donut-center-val');
        const lab = document.querySelector('.donut-center-label');

        const r = el => el ? {
            top: el.getBoundingClientRect().top,
            left: el.getBoundingClientRect().left,
            width: el.getBoundingClientRect().width,
            height: el.getBoundingClientRect().height
        } : null;

        // Find first clipping ancestor of SVG
        const findClip = (startEl) => {
            let el = startEl;
            while (el) {
                const cs = getComputedStyle(el);
                const ov = cs.overflow;
                const ovX = cs.overflowX;
                const ovY = cs.overflowY;
                if (['hidden','clip','auto','scroll'].includes(ov) ||
                    ['hidden','clip'].includes(ovX) ||
                    ['hidden','clip'].includes(ovY)) {
                    return {tag: el.tagName, cls: el.className, id: el.id,
                            overflow: ov, overflowX: ovX, overflowY: ovY,
                            rect: r(el)};
                }
                el = el.parentElement;
                if (!el || el.tagName === 'BODY') break;
            }
            return null;
        };

        return {
            wrap: r(w),
            svg: r(svg),
            inner: r(inner),
            wrapComputedWidth: w ? getComputedStyle(w).width : null,
            wrapComputedHeight: w ? getComputedStyle(w).height : null,
            innerDisplay: inner ? getComputedStyle(inner).display : null,
            innerFlex: inner ? getComputedStyle(inner).flexDirection : null,
            valColor: val ? getComputedStyle(val).color : null,
            valFontSize: val ? getComputedStyle(val).fontSize : null,
            labColor: lab ? getComputedStyle(lab).color : null,
            labFontSize: lab ? getComputedStyle(lab).fontSize : null,
            bodyBg: getComputedStyle(document.body).backgroundColor,
            donutWrapExists: !!w,
            svgExists: !!svg,
            innerExists: !!inner,
            valExists: !!val,
            labExists: !!lab,
            svgClippedBy: svg ? findClip(svg) : null,
            wrapClippedBy: w ? findClip(w) : null,
            // Also check what's actually rendered inside the donut
            donutSvgViewBox: svg ? svg.getAttribute('viewBox') : null,
            donutSvgWidth: svg ? svg.getAttribute('width') : null,
            donutSvgHeight: svg ? svg.getAttribute('height') : null,
        };
    }""")
    return result

def main():
    measurements = {"users": {}, "donut": {}}

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)

        # ---- PHASE 1: /Admin/Users ----
        print("\n=== PHASE 1: /Admin/Users ===")

        for mode_name, lang in MODES:
            is_dark = "dark" in mode_name
            print(f"\n-- Mode: {mode_name} --")

            for width in WIDTHS:
                context = browser.new_context(viewport={"width": width, "height": 900})
                page = context.new_page()

                # Login
                login(page)

                # Set language
                set_language(page, lang)

                # Set dark mode
                set_dark_mode(page, is_dark)

                # Navigate to Users
                page.goto(f"{BASE_URL}/Admin/Users", wait_until="networkidle")
                page.wait_for_timeout(1000)

                fname = f"users-before-{mode_name}-{width}.png"
                screenshot(page, fname)

                # Run diagnostics at 820px (where breakout likely shows)
                if width == 820:
                    diag = run_users_diagnosis(page, width)
                    measurements["users"][mode_name] = diag
                    print(f"  scrollWidthOverflows: {diag['scrollWidthOverflows']}")
                    # Find first overflowing ancestor
                    for entry in diag["ancestorChain"]:
                        if isinstance(entry, dict) and entry.get("scrollWidth", 0) > width:
                            print(f"  OVERFLOW at: <{entry['tag']} class='{entry['cls']}'> scrollWidth={entry['scrollWidth']}")

                context.close()

        # ---- PHASE 2: /Admin/Analytics ----
        print("\n=== PHASE 2: /Admin/Analytics ===")

        for mode_name, lang in [("light-ltr", "en"), ("dark-ltr", "en"), ("hebrew-rtl", "he")]:
            is_dark = "dark" in mode_name
            print(f"\n-- Mode: {mode_name} --")

            context = browser.new_context(viewport={"width": 1440, "height": 900})
            page = context.new_page()

            login(page)
            set_language(page, lang)
            set_dark_mode(page, is_dark)

            page.goto(f"{BASE_URL}/Admin/Analytics", wait_until="networkidle")
            page.wait_for_timeout(1500)

            fname = f"analytics-before-{mode_name}.png"
            screenshot(page, fname)

            # Run donut diagnostics
            donut = run_donut_diagnosis(page)
            measurements["donut"][mode_name] = donut
            print(f"  wrapExists: {donut['donutWrapExists']}, wrap: {donut['wrap']}")
            print(f"  svgExists: {donut['svgExists']}, svg: {donut['svg']}")
            print(f"  valColor: {donut['valColor']}, labColor: {donut['labColor']}")
            print(f"  bodyBg: {donut['bodyBg']}")

            context.close()

        browser.close()

    # Save raw measurements
    measurements_path = os.path.join(ARTIFACTS, "raw-measurements.json")
    with open(measurements_path, "w", encoding="utf-8") as f:
        json.dump(measurements, f, indent=2)
    print(f"\nSaved measurements to {measurements_path}")

    return measurements

if __name__ == "__main__":
    main()
