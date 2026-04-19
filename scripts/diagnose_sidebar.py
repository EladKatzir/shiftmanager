"""Capture current sidebar state for diagnosis."""
from playwright.sync_api import sync_playwright

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(viewport={'width': 1400, 'height': 900})

    # Login
    page.goto('http://localhost:5000/Auth/Login')
    page.wait_for_load_state('networkidle')
    page.fill('input[name="Email"]', 'test.manager@shifty.test')
    page.fill('input[name="Password"]', 'TestManager123!')
    page.click('button[type="submit"]')
    page.wait_for_load_state('networkidle')

    # Force sidebar COLLAPSED
    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'true')")

    # --- Screenshot 1: Calendar/Chores with collapsed sidebar ---
    page.goto('http://localhost:5000/Calendar/Chores')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(800)
    page.screenshot(path='/tmp/diag1_chores_collapsed.png', full_page=False)

    # Collect toggle hit-test info
    toggle = page.locator('#sidebarToggle')
    bbox = toggle.bounding_box()
    print(f"Toggle bounding box: {bbox}")

    if bbox:
        cx = bbox['x'] + bbox['width'] / 2
        cy = bbox['y'] + bbox['height'] / 2
        top_elements = page.evaluate(f"""() => {{
            return document.elementsFromPoint({cx}, {cy}).slice(0, 5).map(e => ({{
                tag: e.tagName, id: e.id, cls: e.className.toString().slice(0, 60),
                zIndex: window.getComputedStyle(e).zIndex,
                position: window.getComputedStyle(e).position,
            }}));
        }}""")
        print("Elements at toggle center:")
        for el in top_elements:
            print(f"  {el['tag']}#{el['id']} .{el['cls']} | z:{el['zIndex']} pos:{el['position']}")

    # Sidebar info
    sidebar_info = page.evaluate("""() => {
        const s = document.getElementById('appSidebar');
        const cs = window.getComputedStyle(s);
        const r = s.getBoundingClientRect();
        return {
            rect: {left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width},
            overflow: cs.overflow,
            overflowX: cs.overflowX,
            overflowY: cs.overflowY,
            position: cs.position,
            zIndex: cs.zIndex,
            isCollapsed: s.classList.contains('is-collapsed'),
        };
    }""")
    print(f"Sidebar info: {sidebar_info}")

    # Toggle info
    toggle_info = page.evaluate("""() => {
        const t = document.getElementById('sidebarToggle');
        const cs = window.getComputedStyle(t);
        const r = t.getBoundingClientRect();
        return {
            rect: {left: r.left, right: r.right, top: r.top, bottom: r.bottom},
            position: cs.position,
            left: cs.left,
            right: cs.right,
            top: cs.top,
            zIndex: cs.zIndex,
            parentId: t.parentElement ? t.parentElement.id : 'none',
            parentClass: t.parentElement ? t.parentElement.className.slice(0, 60) : 'none',
        };
    }""")
    print(f"Toggle info: {toggle_info}")

    # --- Screenshot 2: Calendar/Shifts for comparison ---
    page.goto('http://localhost:5000/Calendar/Shifts')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(500)
    page.screenshot(path='/tmp/diag2_shifts_collapsed.png', full_page=False)

    toggle_info2 = page.evaluate("""() => {
        const t = document.getElementById('sidebarToggle');
        if (!t) return {error: 'no toggle'};
        const cs = window.getComputedStyle(t);
        const r = t.getBoundingClientRect();
        return {
            rect: {left: r.left, right: r.right, top: r.top, bottom: r.bottom},
            position: cs.position,
            left: cs.left, right: cs.right, zIndex: cs.zIndex,
        };
    }""")
    print(f"Toggle on Shifts: {toggle_info2}")

    browser.close()
    print("Done. Screenshots: /tmp/diag1_chores_collapsed.png, /tmp/diag2_shifts_collapsed.png")
