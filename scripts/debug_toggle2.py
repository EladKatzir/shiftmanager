"""Deeper inspection: what covers the toggle and what's wrong on Schedule/Index"""
import os
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
    page = browser.new_page(viewport={'width': 1400, 'height': 900})
    login(page)

    # -- Investigate Schedule/Index --
    page.goto('http://localhost:5000/Schedule/Index')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(1000)

    toggle = page.locator('#sidebarToggle')
    print(f"[Schedule/Index] toggle count: {toggle.count()}")

    sidebar = page.locator('#appSidebar')
    print(f"[Schedule/Index] sidebar count: {sidebar.count()}")

    # Check toggle visibility via computed style
    if toggle.count() > 0:
        visible = toggle.is_visible()
        bbox = toggle.bounding_box()
        style = page.evaluate("() => { const el = document.getElementById('sidebarToggle'); if (!el) return 'not found'; const cs = window.getComputedStyle(el); return { display: cs.display, visibility: cs.visibility, pointerEvents: cs.pointerEvents, zIndex: cs.zIndex, overflow: cs.overflow }; }")
        print(f"  visible={visible}, bbox={bbox}")
        print(f"  computed style: {style}")

    page.screenshot(path='/tmp/schedule_index.png')

    # Check if the page uses a custom layout or has a full-screen overlay
    overlays = page.evaluate("""() => {
        const candidates = [];
        document.querySelectorAll('*').forEach(el => {
            const cs = window.getComputedStyle(el);
            if ((cs.position === 'fixed' || cs.position === 'absolute') &&
                parseFloat(cs.width) > 300 && parseFloat(cs.height) > 300 &&
                cs.display !== 'none' && cs.visibility !== 'hidden') {
                const r = el.getBoundingClientRect();
                candidates.push({
                    tag: el.tagName,
                    id: el.id,
                    cls: el.className.toString().slice(0, 60),
                    rect: { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) },
                    zIndex: cs.zIndex,
                    pointerEvents: cs.pointerEvents,
                    opacity: cs.opacity,
                });
            }
        });
        return candidates.slice(0, 20);
    }""")
    print(f"\n[Schedule/Index] Large fixed/absolute elements:")
    for el in overlays:
        print(f"  {el['tag']}#{el['id']} .{el['cls'][:40]} | rect={el['rect']} | z={el['zIndex']} pe={el['pointerEvents']} opacity={el['opacity']}")

    # -- Check if toggle button is covered on Calendar/Table (works but shows ASIDE) --
    page.goto('http://localhost:5000/Calendar/Table')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(800)

    toggle = page.locator('#sidebarToggle')
    bbox = toggle.bounding_box()
    cx = bbox['x'] + bbox['width'] / 2
    cy = bbox['y'] + bbox['height'] / 2
    print(f"\n[Calendar/Table] toggle bbox={bbox}, center=({round(cx)},{round(cy)})")

    # Check z-index and stacking of elements at toggle area
    info = page.evaluate(f"""() => {{
        const els = document.elementsFromPoint({cx}, {cy});
        return els.slice(0,5).map(el => ({{
            tag: el.tagName, id: el.id,
            cls: el.className.toString().slice(0, 50),
            zIndex: window.getComputedStyle(el).zIndex,
            pointerEvents: window.getComputedStyle(el).pointerEvents,
        }}));
    }}""")
    print(f"  Elements at toggle center (top to bottom):")
    for el in info:
        print(f"    {el['tag']}#{el['id']} .{el['cls'][:40]} z={el['zIndex']} pe={el['pointerEvents']}")

    browser.close()
    print("\nDone. Screenshot at /tmp/schedule_index.png")
