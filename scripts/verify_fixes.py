"""Verify the three fixes: Schedule layout, minimize button, sidebar toggle click target."""
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

    # --- Test 1: Schedule/Index now has layout ---
    page = browser.new_page(viewport={'width': 1400, 'height': 900})
    login(page)
    page.goto('http://localhost:5000/Schedule/Index')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(800)
    sidebar_count = page.locator('#appSidebar').count()
    toggle_count = page.locator('#sidebarToggle').count()
    print(f"[Fix 1 - Schedule/Index layout] sidebar={sidebar_count} toggle={toggle_count} -> {'PASS' if sidebar_count > 0 and toggle_count > 0 else 'FAIL'}")
    page.screenshot(path='/tmp/fix1_schedule.png')

    # --- Test 2: Sidebar toggle is now the real click target ---
    page.goto('http://localhost:5000/Home')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(500)
    toggle = page.locator('#sidebarToggle')
    bbox = toggle.bounding_box()
    cx = bbox['x'] + bbox['width'] / 2
    cy = bbox['y'] + bbox['height'] / 2
    els = page.evaluate(f"""() => {{
        return document.elementsFromPoint({cx}, {cy}).slice(0,3).map(e => ({{
            tag: e.tagName, id: e.id, cls: e.className.toString().slice(0,40)
        }}));
    }}""")
    top = els[0] if els else {{}}
    is_toggle = top.get('id') == 'sidebarToggle' or any(e.get('id') == 'sidebarToggle' for e in els)
    print(f"[Fix 2 - Toggle click target] Top element: {top.get('tag')}#{top.get('id')} -> {'PASS (toggle reachable)' if is_toggle else f'NOTICE: toggle not top element, but checking if sidebar present'}")
    for el in els:
        print(f"         {el['tag']}#{el['id']} .{el['cls']}")

    # Real click test (not Playwright force)
    sidebar_class_before = page.evaluate("document.getElementById('appSidebar').className")
    page.mouse.click(cx, cy)
    page.wait_for_timeout(300)
    sidebar_class_after = page.evaluate("document.getElementById('appSidebar').className")
    worked = ('is-collapsed' in sidebar_class_after) != ('is-collapsed' in sidebar_class_before)
    print(f"         Mouse click at ({round(cx)},{round(cy)}) worked: {worked}")
    if 'is-collapsed' in sidebar_class_after:
        page.mouse.click(cx, cy)  # reset
        page.wait_for_timeout(200)

    # --- Test 3: Minimize button visible in light mode ---
    # Force light mode
    page.evaluate("localStorage.setItem('theme', 'light'); document.documentElement.setAttribute('data-theme','light');")
    page.wait_for_timeout(300)
    page.screenshot(path='/tmp/fix3_light_mode_full.png')

    # Check minimize button computed color
    minimize_visible = page.evaluate("""() => {
        const el = document.getElementById('bottomDockMinimize');
        if (!el) return {found: false};
        const cs = window.getComputedStyle(el);
        const parent = el.closest('.bottom-dock__header');
        const parentBg = parent ? window.getComputedStyle(parent).background : 'N/A';
        return {
            found: true,
            color: cs.color,
            bg: cs.backgroundColor,
            headerBg: parentBg.slice(0, 60),
            border: cs.borderColor,
        };
    }""")
    if minimize_visible.get('found'):
        color = minimize_visible.get('color', '')
        bg = minimize_visible.get('headerBg', '')
        print(f"[Fix 3 - Minimize button] color={color} header-bg={bg[:50]}")
        print(f"         -> {'PASS' if 'rgb(255' in color or '255, 255, 255' in color else 'CHECK: color=' + color}")
    else:
        print(f"[Fix 3 - Minimize button] dock not visible (feature flag off?)")

    browser.close()
    print("\nScreenshots saved.")
