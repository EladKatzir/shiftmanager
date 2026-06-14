"""
Task 1: Baseline diagnosis for Admin UI fixes - Part 2 (completing after fix).
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
    page.wait_for_timeout(500)
    # Find all input fields
    inputs = page.query_selector_all("input")
    for inp in inputs:
        t = inp.get_attribute("type") or ""
        n = inp.get_attribute("name") or ""
        placeholder = inp.get_attribute("placeholder") or ""
        print(f"    input type={t!r} name={n!r} placeholder={placeholder!r}")
    # Fill using type selectors
    email_input = page.query_selector('input[type="email"]') or page.query_selector('input[name*="mail" i]') or page.query_selector('input[name*="Email"]')
    pw_input = page.query_selector('input[type="password"]')
    if email_input:
        email_input.fill(LOGIN_EMAIL)
    if pw_input:
        pw_input.fill(LOGIN_PASSWORD)
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")
    print(f"  After login, URL: {page.url}")

def set_language(page, lang):
    if lang == "he":
        page.goto(f"{BASE_URL}/Culture/SetCulture?culture=he-IL&redirectUri=/", wait_until="networkidle")
    else:
        page.goto(f"{BASE_URL}/Culture/SetCulture?culture=en-US&redirectUri=/", wait_until="networkidle")

def set_dark_mode_js(page, dark):
    """Set dark mode directly via JS to be reliable."""
    page.goto(f"{BASE_URL}/", wait_until="networkidle")
    page.wait_for_timeout(500)

    # Check current theme
    current = page.evaluate("""() => {
        return {
            htmlAttr: document.documentElement.getAttribute('data-theme'),
            htmlClass: document.documentElement.className,
            bodyAttr: document.body.getAttribute('data-theme'),
            bodyClass: document.body.className,
            localStorageTheme: localStorage.getItem('theme') || localStorage.getItem('colorTheme') || localStorage.getItem('dark-mode'),
        };
    }""")
    print(f"  Theme state: {current}")

    is_dark = False
    if current.get("htmlAttr") == "dark":
        is_dark = True
    elif "dark" in str(current.get("htmlClass", "")).lower():
        is_dark = True
    elif current.get("localStorageTheme") == "dark":
        is_dark = True

    if dark and not is_dark:
        # Try clicking theme toggle
        toggle_clicked = page.evaluate("""() => {
            const selectors = [
                '[data-theme-toggle]', '.theme-toggle', '#themeToggle',
                '[onclick*="theme"]', '[onclick*="dark"]', '.sidebar__theme-btn',
                '.theme-switch', '[aria-label*="theme" i]', '[aria-label*="dark" i]',
                'button.theme', '.dark-toggle', '.light-dark-toggle'
            ];
            for (const sel of selectors) {
                const el = document.querySelector(sel);
                if (el) { el.click(); return sel; }
            }
            return null;
        }""")
        print(f"  Dark toggle clicked: {toggle_clicked}")
        page.wait_for_timeout(500)

        # Check again
        new_state = page.evaluate("() => document.documentElement.getAttribute('data-theme') || document.documentElement.className")
        print(f"  Theme after click: {new_state}")
    elif not dark and is_dark:
        toggle_clicked = page.evaluate("""() => {
            const selectors = [
                '[data-theme-toggle]', '.theme-toggle', '#themeToggle',
                '[onclick*="theme"]', '[onclick*="dark"]', '.sidebar__theme-btn',
                '.theme-switch', '[aria-label*="theme" i]', '[aria-label*="dark" i]',
                'button.theme', '.dark-toggle', '.light-dark-toggle'
            ];
            for (const sel of selectors) {
                const el = document.querySelector(sel);
                if (el) { el.click(); return sel; }
            }
            return null;
        }""")
        print(f"  Light toggle clicked: {toggle_clicked}")
        page.wait_for_timeout(500)

def screenshot(page, name):
    path = os.path.join(ARTIFACTS, name)
    page.screenshot(path=path, full_page=True)
    print(f"  Saved: {name}")
    return path

def run_users_diagnosis(page, viewport_width):
    overflows = page.evaluate("() => document.documentElement.scrollWidth > document.documentElement.clientWidth")
    ancestor_chain = page.evaluate("""() => {
        let el = document.querySelector('#usersTable') || document.querySelector('table');
        if (!el) return [{error: 'No table found'}];
        const out = [];
        while (el) {
            const cs = getComputedStyle(el);
            out.push({
                tag: el.tagName,
                cls: el.className.substring(0, 120),
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
            if (out.length > 25) break;
        }
        return out;
    }""")
    return {"viewportWidth": viewport_width, "scrollWidthOverflows": overflows, "ancestorChain": ancestor_chain}

def run_donut_diagnosis(page):
    result = page.evaluate("""() => {
        const w = document.querySelector('.donut-wrap');
        const svg = document.querySelector('.donut-svg');
        const inner = document.querySelector('.hero-chart-inner');
        const val = document.querySelector('.donut-center-val');
        const lab = document.querySelector('.donut-center-label');

        const r = el => el ? {
            top: Math.round(el.getBoundingClientRect().top),
            left: Math.round(el.getBoundingClientRect().left),
            width: Math.round(el.getBoundingClientRect().width),
            height: Math.round(el.getBoundingClientRect().height)
        } : null;

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
                    return {tag: el.tagName, cls: el.className.substring(0, 120), id: el.id,
                            overflow: ov, overflowX: ovX, overflowY: ovY,
                            rect: r(el)};
                }
                el = el.parentElement;
                if (!el || el.tagName === 'HTML') break;
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
            innerAlignItems: inner ? getComputedStyle(inner).alignItems : null,
            innerJustifyContent: inner ? getComputedStyle(inner).justifyContent : null,
            valColor: val ? getComputedStyle(val).color : null,
            valFontSize: val ? getComputedStyle(val).fontSize : null,
            valBg: val ? getComputedStyle(val).backgroundColor : null,
            labColor: lab ? getComputedStyle(lab).color : null,
            labFontSize: lab ? getComputedStyle(lab).fontSize : null,
            bodyBg: getComputedStyle(document.body).backgroundColor,
            docElementBg: getComputedStyle(document.documentElement).backgroundColor,
            donutWrapExists: !!w,
            svgExists: !!svg,
            innerExists: !!inner,
            valExists: !!val,
            labExists: !!lab,
            svgClippedBy: svg ? findClip(svg) : null,
            wrapClippedBy: w ? findClip(w) : null,
            donutSvgViewBox: svg ? svg.getAttribute('viewBox') : null,
            donutSvgWidth: svg ? svg.getAttribute('width') : null,
            donutSvgHeight: svg ? svg.getAttribute('height') : null,
            // Check for any canvas fallback
            canvasExists: !!document.querySelector('canvas'),
            chartJsExists: typeof Chart !== 'undefined',
            // Check what's inside donut-wrap
            wrapInnerHTML: w ? w.innerHTML.substring(0, 500) : null,
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
                print(f"  viewport: {width}px")
                context = browser.new_context(viewport={"width": width, "height": 900})
                page = context.new_page()

                login(page)
                set_language(page, lang)
                set_dark_mode_js(page, is_dark)

                page.goto(f"{BASE_URL}/Admin/Users", wait_until="networkidle")
                page.wait_for_timeout(1000)

                fname = f"users-before-{mode_name}-{width}.png"
                # Skip if already exists (light-ltr-1440 and light-ltr-820 were captured)
                screenshot(page, fname)

                if width == 820:
                    diag = run_users_diagnosis(page, width)
                    measurements["users"][mode_name] = diag
                    print(f"  scrollWidthOverflows: {diag['scrollWidthOverflows']}")
                    for entry in diag["ancestorChain"]:
                        if isinstance(entry, dict) and not entry.get("error"):
                            sw = entry.get("scrollWidth", 0)
                            cw = entry.get("clientWidth", 0)
                            if sw > width + 5:
                                print(f"  [WIDE] <{entry['tag']} id={entry['id']!r} cls='{entry['cls'][:60]}'> scrollW={sw} clientW={cw} overflow={entry['overflowX']} minW={entry['minWidth']}")

                context.close()

        # ---- PHASE 2: /Admin/Analytics ----
        print("\n=== PHASE 2: /Admin/Analytics ===")

        for mode_name, lang in [("light-ltr", "en"), ("dark-ltr", "en"), ("hebrew-rtl", "he")]:
            is_dark = "dark" in mode_name
            print(f"\n-- Mode: {mode_name} (1440px) --")

            context = browser.new_context(viewport={"width": 1440, "height": 900})
            page = context.new_page()

            login(page)
            set_language(page, lang)
            set_dark_mode_js(page, is_dark)

            page.goto(f"{BASE_URL}/Admin/Analytics", wait_until="networkidle")
            page.wait_for_timeout(1500)

            fname = f"analytics-before-{mode_name}.png"
            screenshot(page, fname)

            donut = run_donut_diagnosis(page)
            measurements["donut"][mode_name] = donut
            print(f"  wrapExists: {donut['donutWrapExists']}, wrapRect: {donut['wrap']}")
            print(f"  svgExists: {donut['svgExists']}, svgRect: {donut['svg']}")
            print(f"  innerExists: {donut['innerExists']}, innerRect: {donut['inner']}")
            print(f"  valColor: {donut['valColor']}")
            print(f"  labColor: {donut['labColor']}")
            print(f"  bodyBg: {donut['bodyBg']}")
            print(f"  svgClippedBy: {donut['svgClippedBy']}")
            print(f"  chartJsExists: {donut['chartJsExists']}")
            print(f"  canvasExists: {donut['canvasExists']}")

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
