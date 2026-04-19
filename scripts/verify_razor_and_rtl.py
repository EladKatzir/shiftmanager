"""Verify Razor JS update is live AND RTL Hebrew mode still works."""
from playwright.sync_api import sync_playwright

LOGIN_URL = 'http://localhost:5000/Auth/Login'
EMAIL = 'test.manager@shifty.test'
PASSWORD = 'TestManager123!'

def login(page):
    page.goto(LOGIN_URL)
    page.wait_for_load_state('networkidle')
    page.fill('input[name="Email"]', EMAIL)
    page.fill('input[name="Password"]', PASSWORD)
    page.click('button[type="submit"]')
    page.wait_for_load_state('networkidle')

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    context = browser.new_context(viewport={'width': 1400, 'height': 900})
    page = context.new_page()

    login(page)

    # 1. Check if my JS edit is in the served HTML
    page.goto('http://localhost:5000/Home')
    page.wait_for_load_state('networkidle')
    html = page.content()
    has_remove_fix = "classList.remove('sidebar-initially-collapsed')" in html
    print(f"[A] Razor JS fix present in served HTML: {has_remove_fix}")

    # 2. Check runtime state — does <html> still have the class after JS runs?
    page.wait_for_timeout(500)
    html_cls = page.evaluate("document.documentElement.className")
    print(f"[B] <html> classes after load: '{html_cls}'")
    has_leftover = 'sidebar-initially-collapsed' in html_cls
    print(f"    sidebar-initially-collapsed still present: {has_leftover}")

    # 3. Test RTL Hebrew — switch culture and re-test collapsed toggle click
    print("\n--- Switching to Hebrew (RTL) ---")
    # Set culture cookie (per memory: CookieRequestCultureProvider)
    context.add_cookies([{
        'name': '.AspNetCore.Culture',
        'value': 'c=he-IL|uic=he-IL',
        'domain': 'localhost',
        'path': '/',
    }])
    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'true')")
    page.goto('http://localhost:5000/Calendar/Chores')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(700)

    dir_attr = page.evaluate("document.documentElement.getAttribute('dir')")
    print(f"[C] <html> dir: {dir_attr}")

    toggle_info = page.evaluate("""() => {
        const t = document.getElementById('sidebarToggle');
        if (!t) return {error: 'missing'};
        const cs = window.getComputedStyle(t);
        const r = t.getBoundingClientRect();
        return {
            rect: {left: r.left, right: r.right, top: r.top, width: r.width},
            pos: cs.position, left: cs.left, right: cs.right, top: cs.top, z: cs.zIndex,
            viewportW: window.innerWidth,
        };
    }""")
    print(f"[D] Toggle (RTL collapsed): {toggle_info}")

    bbox = page.locator('#sidebarToggle').bounding_box()
    before = page.evaluate("document.getElementById('appSidebar').className")
    page.mouse.click(bbox['x'] + bbox['width']/2, bbox['y'] + bbox['height']/2)
    page.wait_for_timeout(400)
    after = page.evaluate("document.getElementById('appSidebar').className")
    rtl_collapsed_pass = before != after
    print(f"[E] RTL Hebrew /Calendar/Chores COLLAPSED click: {'PASS' if rtl_collapsed_pass else 'FAIL'}")
    print(f"    before='{before}' after='{after}'")

    # RTL expanded
    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'false')")
    page.goto('http://localhost:5000/Calendar/Chores')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(700)
    bbox = page.locator('#sidebarToggle').bounding_box()
    before = page.evaluate("document.getElementById('appSidebar').className")
    page.mouse.click(bbox['x'] + bbox['width']/2, bbox['y'] + bbox['height']/2)
    page.wait_for_timeout(400)
    after = page.evaluate("document.getElementById('appSidebar').className")
    rtl_expanded_pass = before != after
    print(f"[F] RTL Hebrew /Calendar/Chores EXPANDED click: {'PASS' if rtl_expanded_pass else 'FAIL'}")
    print(f"    before='{before}' after='{after}'")

    # Screenshots for visual confirmation
    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'true')")
    page.goto('http://localhost:5000/Calendar/Chores')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(700)
    page.screenshot(path='/tmp/final_rtl_collapsed_chores.png', full_page=False)

    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'false')")
    page.goto('http://localhost:5000/Calendar/Chores')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(700)
    page.screenshot(path='/tmp/final_rtl_expanded_chores.png', full_page=False)

    browser.close()
    print("\nDone. Screenshots: /tmp/final_rtl_*.png")
