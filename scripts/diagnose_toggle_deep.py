"""Deep diagnostic: why does sidebarToggle fail on some pages?

Tests both collapsed and expanded states on working vs broken pages.
Captures: z-index stack, transform ancestry, pointer-events, actual click result.
"""
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

DIAGNOSTIC_JS = """() => {
    const toggle = document.getElementById('sidebarToggle');
    const sidebar = document.getElementById('appSidebar');
    if (!toggle || !sidebar) return {error: 'toggle or sidebar missing'};

    const ts = window.getComputedStyle(toggle);
    const tr = toggle.getBoundingClientRect();
    const sr = sidebar.getBoundingClientRect();

    const cx = tr.left + tr.width / 2;
    const cy = tr.top + tr.height / 2;

    const elementsAtPoint = document.elementsFromPoint(cx, cy).slice(0, 6).map(e => {
        const cs = window.getComputedStyle(e);
        return {
            tag: e.tagName,
            id: e.id || '',
            cls: (typeof e.className === 'string' ? e.className : '').slice(0, 80),
            zIndex: cs.zIndex,
            position: cs.position,
            pointerEvents: cs.pointerEvents,
        };
    });

    // Walk ancestors looking for containing-block breakers
    const breakers = [];
    let el = toggle.parentElement;
    while (el && el !== document.documentElement) {
        const cs = window.getComputedStyle(el);
        const hasTransform = cs.transform !== 'none';
        const hasFilter = cs.filter !== 'none';
        const hasPerspective = cs.perspective !== 'none';
        const hasWillChange = cs.willChange.includes('transform') || cs.willChange.includes('filter') || cs.willChange.includes('perspective');
        const hasContain = cs.contain.includes('paint') || cs.contain.includes('layout') || cs.contain.includes('strict') || cs.contain.includes('content');
        const hasBackdropFilter = cs.backdropFilter && cs.backdropFilter !== 'none';
        if (hasTransform || hasFilter || hasPerspective || hasWillChange || hasContain || hasBackdropFilter) {
            breakers.push({
                tag: el.tagName,
                id: el.id || '',
                cls: (typeof el.className === 'string' ? el.className : '').slice(0, 60),
                transform: cs.transform,
                filter: cs.filter,
                perspective: cs.perspective,
                willChange: cs.willChange,
                contain: cs.contain,
                backdropFilter: cs.backdropFilter,
            });
        }
        el = el.parentElement;
    }

    return {
        toggleRect: {left: tr.left, top: tr.top, right: tr.right, bottom: tr.bottom, width: tr.width, height: tr.height},
        toggleStyle: {
            position: ts.position, top: ts.top, left: ts.left, right: ts.right,
            zIndex: ts.zIndex, display: ts.display, visibility: ts.visibility,
            pointerEvents: ts.pointerEvents, opacity: ts.opacity, transform: ts.transform,
        },
        sidebarRect: {left: sr.left, right: sr.right, width: sr.width},
        sidebarIsCollapsed: sidebar.classList.contains('is-collapsed'),
        htmlClasses: document.documentElement.className,
        htmlDir: document.documentElement.getAttribute('dir'),
        elementsAtPoint: elementsAtPoint,
        containingBlockBreakers: breakers,
        viewport: {w: window.innerWidth, h: window.innerHeight},
    };
}"""

def dump_state(label, info):
    if 'error' in info:
        print(f"  ERROR: {info['error']}")
        return
    tr = info['toggleRect']
    ts = info['toggleStyle']
    sr = info['sidebarRect']
    print(f"  Toggle rect: L={tr['left']:.1f} T={tr['top']:.1f} W={tr['width']:.1f} H={tr['height']:.1f}")
    print(f"  Toggle style: pos={ts['position']} top={ts['top']} left={ts['left']} right={ts['right']} z={ts['zIndex']} pe={ts['pointerEvents']}")
    print(f"  Sidebar: L={sr['left']:.1f} R={sr['right']:.1f} W={sr['width']:.1f} collapsed={info['sidebarIsCollapsed']}")
    print(f"  HTML classes: '{info['htmlClasses']}' dir={info['htmlDir']}")
    if info['containingBlockBreakers']:
        print(f"  !!! Containing-block breakers (force fixed->absolute):")
        for b in info['containingBlockBreakers']:
            print(f"    {b['tag']}#{b['id']} .{b['cls']}")
            for k in ('transform','filter','perspective','willChange','contain','backdropFilter'):
                if b[k] and b[k] not in ('none','auto',''):
                    print(f"      {k}: {b[k]}")
    else:
        print(f"  No containing-block breakers in ancestry [OK]")
    print(f"  Elements at toggle center:")
    for i, e in enumerate(info['elementsAtPoint']):
        marker = '  <-- TOP' if i == 0 else ''
        print(f"    [{i}] {e['tag']}#{e['id']} .{e['cls']} | z:{e['zIndex']} pos:{e['position']} pe:{e['pointerEvents']}{marker}")

def try_click(page, label):
    before = page.evaluate("(document.getElementById('appSidebar') || {}).className || 'MISSING'")
    if before == 'MISSING':
        print(f"  {label}: SKIPPED (sidebar missing - probably redirected)")
        return False
    try:
        # Use raw mouse click at center to simulate real user click
        bbox = page.locator('#sidebarToggle').bounding_box()
        if not bbox:
            print(f"  {label}: SKIPPED (toggle has no bbox)")
            return False
        cx = bbox['x'] + bbox['width']/2
        cy = bbox['y'] + bbox['height']/2
        page.mouse.click(cx, cy)
        page.wait_for_timeout(400)
        after = page.evaluate("(document.getElementById('appSidebar') || {}).className || 'MISSING'")
        changed = before != after
        print(f"  {label}: {'PASS' if changed else 'FAIL'} mouse@({cx:.0f},{cy:.0f}) before='{before}', after='{after}'")
        return changed
    except Exception as e:
        print(f"  {label}: CLICK ERROR: {e}")
        return False

PAGES = [
    ('/Calendar/Shifts', 'WORKS (reference)'),
    ('/Calendar/Chores', 'BROKEN'),
    ('/Director/CompanyFilter', 'BROKEN'),
    ('/Home', 'unknown'),
]

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    context = browser.new_context(viewport={'width': 1400, 'height': 900})
    page = context.new_page()

    login(page)

    for url, tag in PAGES:
        for collapsed in (True, False):
            print(f"\n{'='*78}")
            print(f"URL: {url}  ({tag})  sidebar={'COLLAPSED' if collapsed else 'EXPANDED'}")
            print('='*78)
            page.evaluate(f"localStorage.setItem('shifty_sidebar_collapsed', '{str(collapsed).lower()}')")
            page.goto(f'http://localhost:5000{url}')
            page.wait_for_load_state('networkidle')
            page.wait_for_timeout(600)

            final_url = page.url
            if final_url != f'http://localhost:5000{url}':
                print(f"  REDIRECTED to: {final_url}")

            info = page.evaluate(DIAGNOSTIC_JS)
            dump_state(f'{url} collapsed={collapsed}', info)

            # Try to click
            try_click(page, f'CLICK on {url} collapsed={collapsed}')

            safe = url.replace('/', '_').lstrip('_')
            page.screenshot(path=f'/tmp/deep_{safe}_c{int(collapsed)}.png', full_page=False)

    browser.close()
    print('\nDone.')
