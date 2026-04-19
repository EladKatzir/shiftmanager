from playwright.sync_api import sync_playwright
import json

LOGIN_URL = 'http://localhost:5000/Auth/Login'
EMAIL = 'test.manager@shifty.test'
PASSWORD = 'TestManager123!'

PAGES_TO_TEST = [
    '/Home',
    '/Calendar/Table',
    '/Calendar/Overview',
    '/Chores/Calendar',
    '/Assignments/Manage',
    '/Schedule/Index',
    '/Admin/Index',
]

def login(page):
    page.goto(LOGIN_URL)
    page.wait_for_load_state('networkidle')
    page.fill('input[name="Email"]', EMAIL)
    page.fill('input[name="Password"]', PASSWORD)
    page.click('button[type="submit"]')
    page.wait_for_load_state('networkidle')

def check_page(page, url):
    page.goto(f'http://localhost:5000{url}')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(800)

    # Get toggle button bounding rect
    toggle = page.locator('#sidebarToggle')
    if toggle.count() == 0:
        return {'url': url, 'error': 'toggle not found'}

    bbox = toggle.bounding_box()
    if not bbox:
        return {'url': url, 'error': 'toggle not visible'}

    cx = bbox['x'] + bbox['width'] / 2
    cy = bbox['y'] + bbox['height'] / 2

    # What element is at the toggle's center?
    element_info = page.evaluate(f'''() => {{
        const el = document.elementFromPoint({cx}, {cy});
        if (!el) return {{tag: 'none', id: '', cls: ''}};
        return {{
            tag: el.tagName,
            id: el.id,
            cls: el.className,
            isToggle: el.id === 'sidebarToggle' || el.closest('#sidebarToggle') !== null
        }};
    }}''')

    # Try clicking it
    page.screenshot(path=f'/tmp/toggle_{url.replace("/", "_")}_before.png')
    sidebar_class_before = page.evaluate("document.getElementById('appSidebar')?.className || 'not found'")

    toggle.click(force=False)
    page.wait_for_timeout(300)

    sidebar_class_after = page.evaluate("document.getElementById('appSidebar')?.className || 'not found'")
    page.screenshot(path=f'/tmp/toggle_{url.replace("/", "_")}_after.png')

    worked = ('is-collapsed' in sidebar_class_after) != ('is-collapsed' in sidebar_class_before)

    # Reset state
    if 'is-collapsed' in sidebar_class_after:
        toggle.click(force=False)
        page.wait_for_timeout(300)

    return {
        'url': url,
        'toggle_bbox': bbox,
        'toggle_center': {'x': round(cx), 'y': round(cy)},
        'element_at_center': element_info,
        'worked': worked,
        'sidebar_before': sidebar_class_before,
        'sidebar_after': sidebar_class_after,
    }

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(viewport={'width': 1400, 'height': 900})

    login(page)
    print(f"Logged in, now on: {page.url}")

    results = []
    for url in PAGES_TO_TEST:
        try:
            result = check_page(page, url)
            results.append(result)
            status = 'OK' if result.get('worked') else 'FAIL'
            el = result.get('element_at_center', {})
            print(f"{status} {url:30} | element at toggle: {el.get('tag','?')}#{el.get('id','')} .{el.get('cls','')[:40]} | worked={result.get('worked')}")
        except Exception as e:
            print(f"FAIL {url:30} | ERROR: {e}")

    browser.close()
    print("\nScreenshots saved to /tmp/toggle_*.png")
