"""
Deep-dive diagnostics on the donut chart and the users table wrapper.
"""
import json
import os
from playwright.sync_api import sync_playwright

ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
BASE_URL = "http://localhost:5000"
LOGIN_EMAIL = "owner2@test"
LOGIN_PASSWORD = "Test1234!"

def login(page):
    page.goto(f"{BASE_URL}/Auth/Login", wait_until="networkidle")
    page.fill('input[type="email"]', LOGIN_EMAIL)
    page.fill('input[type="password"]', LOGIN_PASSWORD)
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")

def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)

        # ---- DONUT DEEP-DIVE ----
        print("\n=== DONUT DEEP-DIVE ===")
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()
        login(page)

        # Dark mode
        page.goto(f"{BASE_URL}/", wait_until="networkidle")
        page.evaluate("() => document.querySelector('#themeToggle').click()")
        page.wait_for_timeout(300)

        page.goto(f"{BASE_URL}/Admin/Analytics", wait_until="networkidle")
        page.wait_for_timeout(1500)

        # Get donut center element details
        donut_detail = page.evaluate("""() => {
            // Find all donuts
            const wraps = document.querySelectorAll('.donut-wrap');
            const results = [];

            wraps.forEach((w, i) => {
                const svg = w.querySelector('.donut-svg');
                const r = el => el ? {
                    top: Math.round(el.getBoundingClientRect().top),
                    left: Math.round(el.getBoundingClientRect().left),
                    width: Math.round(el.getBoundingClientRect().width),
                    height: Math.round(el.getBoundingClientRect().height),
                    right: Math.round(el.getBoundingClientRect().right),
                    bottom: Math.round(el.getBoundingClientRect().bottom),
                } : null;

                // Look for center text INSIDE this donut-wrap
                const centerVal = w.nextElementSibling && w.nextElementSibling.classList.contains('donut-center-val')
                    ? w.nextElementSibling
                    : w.parentElement && w.parentElement.querySelector('.donut-center-val');
                const centerLab = w.nextElementSibling && w.nextElementSibling.classList.contains('donut-center-label')
                    ? w.nextElementSibling
                    : w.parentElement && w.parentElement.querySelector('.donut-center-label');

                // Get the parent structure
                let parent = w.parentElement;
                const parentStructure = [];
                let el = w;
                for (let j = 0; j < 5; j++) {
                    if (!el) break;
                    const cs = getComputedStyle(el);
                    parentStructure.push({
                        tag: el.tagName,
                        cls: el.className.substring(0, 80),
                        rect: r(el),
                        position: cs.position,
                        display: cs.display,
                        overflow: cs.overflow,
                    });
                    el = el.parentElement;
                }

                results.push({
                    index: i,
                    wrapRect: r(w),
                    svgRect: r(svg),
                    parentStructure,
                    // innerHTML of the wrap's parent to see the full structure
                    parentInnerHTML: w.parentElement ? w.parentElement.innerHTML.substring(0, 1000) : null,
                });
            });
            return results;
        }""")
        print("DONUT WRAP DETAILS:")
        print(json.dumps(donut_detail, indent=2))

        # Get the full donut section HTML
        donut_section_html = page.evaluate("""() => {
            const shell = document.querySelector('.hero-chart-shell');
            return shell ? shell.outerHTML.substring(0, 3000) : 'NOT FOUND';
        }""")
        print("\nDONUT SECTION HTML (first 3000 chars):")
        print(donut_section_html)

        # Also get the donut-center elements
        center_details = page.evaluate("""() => {
            const vals = document.querySelectorAll('.donut-center-val');
            const labs = document.querySelectorAll('.donut-center-label');
            const r = el => el ? {
                top: Math.round(el.getBoundingClientRect().top),
                left: Math.round(el.getBoundingClientRect().left),
                width: Math.round(el.getBoundingClientRect().width),
                height: Math.round(el.getBoundingClientRect().height),
            } : null;

            const result = [];
            vals.forEach((v, i) => {
                const cs = getComputedStyle(v);
                result.push({
                    type: 'val',
                    index: i,
                    text: v.textContent.trim().substring(0, 30),
                    rect: r(v),
                    position: cs.position,
                    zIndex: cs.zIndex,
                    color: cs.color,
                    fontSize: cs.fontSize,
                    parentTag: v.parentElement ? v.parentElement.tagName : null,
                    parentCls: v.parentElement ? v.parentElement.className.substring(0, 80) : null,
                    parentPosition: v.parentElement ? getComputedStyle(v.parentElement).position : null,
                });
            });
            labs.forEach((l, i) => {
                const cs = getComputedStyle(l);
                result.push({
                    type: 'lab',
                    index: i,
                    text: l.textContent.trim().substring(0, 30),
                    rect: r(l),
                    position: cs.position,
                    color: cs.color,
                    parentTag: l.parentElement ? l.parentElement.tagName : null,
                    parentCls: l.parentElement ? l.parentElement.className.substring(0, 80) : null,
                });
            });
            return result;
        }""")
        print("\nCENTER ELEMENTS:")
        print(json.dumps(center_details, indent=2))

        # Take a zoomed screenshot of the donut area
        page.screenshot(path=os.path.join(ARTIFACTS, "analytics-donut-zoom-dark.png"), clip={"x": 800, "y": 400, "width": 640, "height": 550})
        print("\nSaved zoomed donut screenshot")

        context.close()

        # ---- LIGHT MODE DONUT ----
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()
        login(page)
        page.goto(f"{BASE_URL}/Admin/Analytics", wait_until="networkidle")
        page.wait_for_timeout(1500)
        page.screenshot(path=os.path.join(ARTIFACTS, "analytics-donut-zoom-light.png"), clip={"x": 800, "y": 400, "width": 640, "height": 550})
        print("Saved light mode zoomed donut screenshot")
        context.close()

        # ---- USERS TABLE DEEP-DIVE ----
        print("\n=== USERS TABLE DEEP-DIVE ===")
        context = browser.new_context(viewport={"width": 820, "height": 900})
        page = context.new_page()
        login(page)
        page.goto(f"{BASE_URL}/Admin/Users", wait_until="networkidle")
        page.wait_for_timeout(1000)

        # Scroll down to the users table and screenshot
        page.evaluate("() => { const t = document.querySelector('#usersTable'); if (t) t.scrollIntoView(); }")
        page.wait_for_timeout(500)
        page.screenshot(path=os.path.join(ARTIFACTS, "users-table-820-scrolled.png"), full_page=True)
        print("Saved users table scrolled view at 820px")

        # Get the exact anonymous DIV wrapper
        wrapper_details = page.evaluate("""() => {
            const table = document.querySelector('#usersTable');
            if (!table) return {error: 'no table'};

            const wrapper = table.parentElement;
            const r = el => ({
                top: Math.round(el.getBoundingClientRect().top),
                left: Math.round(el.getBoundingClientRect().left),
                width: Math.round(el.getBoundingClientRect().width),
                height: Math.round(el.getBoundingClientRect().height),
            });

            // Get wrapper's full computed style relevant properties
            const cs = getComputedStyle(wrapper);

            return {
                wrapperTag: wrapper.tagName,
                wrapperId: wrapper.id || null,
                wrapperCls: wrapper.className,
                wrapperRect: r(wrapper),
                wrapperScrollWidth: wrapper.scrollWidth,
                wrapperClientWidth: wrapper.clientWidth,
                wrapperOverflow: cs.overflow,
                wrapperOverflowX: cs.overflowX,
                wrapperOverflowY: cs.overflowY,
                wrapperWidth: cs.width,
                wrapperMinWidth: cs.minWidth,
                wrapperPosition: cs.position,
                // Also check if there's a .table-responsive or similar
                isTableResponsive: wrapper.classList.contains('table-responsive'),
                // Check the section-card parent
                sectionCard: wrapper.parentElement ? {
                    tag: wrapper.parentElement.tagName,
                    cls: wrapper.parentElement.className.substring(0, 80),
                    scrollWidth: wrapper.parentElement.scrollWidth,
                    clientWidth: wrapper.parentElement.clientWidth,
                    overflowX: getComputedStyle(wrapper.parentElement).overflowX,
                } : null,
                // What is the table's actual wrapper in the HTML?
                tableWrapperOuterHTML: wrapper.outerHTML.substring(0, 200),
            };
        }""")
        print("USERS TABLE WRAPPER:")
        print(json.dumps(wrapper_details, indent=2))

        context.close()
        browser.close()

if __name__ == "__main__":
    main()
