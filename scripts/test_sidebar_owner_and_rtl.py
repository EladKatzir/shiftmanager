"""Second-pass tests:
  - Previously-inaccessible pages with test.owner account (broadest grants)
  - Full 4-state test of Hebrew RTL mode across representative pages
"""
from playwright.sync_api import sync_playwright
import sys

OWNER_EMAIL = 'test.owner@shifty.test'
OWNER_PASSWORD = 'TestOwner123!'

def login(page, email, password):
    page.goto('http://localhost:5000/Auth/Login')
    page.wait_for_load_state('networkidle')
    page.fill('input[name="Email"]', email)
    page.fill('input[name="Password"]', password)
    page.click('button[type="submit"]')
    page.wait_for_load_state('networkidle')

# Pages that test.manager couldn't access
OWNER_ONLY_PAGES = [
    '/Admin/Companies',
    '/Admin/Announcements',
    '/Admin/Organization/Areas',
    '/Admin/Organization/Departments',
    '/Admin/Organization/JobTypes',
    '/Admin/Organization/Molecules',
    '/Admin/Organization/Projects',
    '/Admin/Organization/ShiftGroupings',
    '/Admin/DutyRotation',
    '/Owner/Index',
    '/Owner/Telemetry',
    '/Owner/FeatureFlags',
    '/Owner/SystemHealth',
    '/Director/ViewAsMode',
    '/Director/NotificationHub',
    '/Director/CompanyFilter',
]

# Representative pages for RTL test (includes the ones originally reported broken)
RTL_PAGES = [
    '/Home',
    '/Calendar/Shifts',
    '/Calendar/Chores',
    '/Calendar/OnCall',
    '/Calendar/Overview',
    '/Admin/Users',
    '/Admin/Organization',
    '/My/Profile',
    '/My/Settings',
]

def run_four_state(page, url):
    """Returns dict with keys C, E, CE, EC and boolean values, or (None, reason)."""
    results = {}
    # [C] load collapsed
    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'true')")
    page.goto(f'http://localhost:5000{url}')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(500)
    final_url = page.url.replace('http://localhost:5000', '')
    if final_url.startswith('/AccessDenied') or final_url.startswith('/Auth/Login'):
        return None, f'NO_ACCESS ({final_url})'
    state_c = page.evaluate("(document.getElementById('appSidebar')||{}).className||'MISSING'")
    if state_c == 'MISSING':
        return None, 'NO_SIDEBAR'
    results['C'] = 'is-collapsed' in state_c
    # [CE] click toggle
    t = page.evaluate("""() => {
        const t = document.getElementById('sidebarToggle');
        if (!t) return null;
        const r = t.getBoundingClientRect();
        return {x: r.left + r.width/2, y: r.top + r.height/2};
    }""")
    if not t:
        return results, 'NO_TOGGLE'
    page.mouse.click(t['x'], t['y'])
    page.wait_for_timeout(400)
    after = page.evaluate("document.getElementById('appSidebar').className")
    results['CE'] = ('is-collapsed' in state_c) and ('is-collapsed' not in after)
    # [E] load expanded
    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'false')")
    page.goto(f'http://localhost:5000{url}')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(500)
    state_e = page.evaluate("document.getElementById('appSidebar').className")
    results['E'] = 'is-collapsed' not in state_e
    # [EC] click toggle
    t = page.evaluate("""() => {
        const t = document.getElementById('sidebarToggle');
        if (!t) return null;
        const r = t.getBoundingClientRect();
        return {x: r.left + r.width/2, y: r.top + r.height/2};
    }""")
    if not t:
        return results, 'TOGGLE_VANISHED'
    page.mouse.click(t['x'], t['y'])
    page.wait_for_timeout(400)
    after = page.evaluate("document.getElementById('appSidebar').className")
    results['EC'] = ('is-collapsed' not in state_e) and ('is-collapsed' in after)
    return results, None

def report(results, note):
    if results is None:
        return f"{'-':>3} {'-':>3} {'-':>3} {'-':>3}  {note}", 0, 0
    passes = 0
    fails = 0
    parts = []
    for k in ('C', 'E', 'CE', 'EC'):
        v = results.get(k)
        if v is True:
            parts.append('OK ')
            passes += 1
        elif v is False:
            parts.append('FAIL')
            fails += 1
        else:
            parts.append('-  ')
    extra = f'  {note}' if note else ''
    return f"{parts[0]:>3} {parts[1]:>3} {parts[2]:>3} {parts[3]:>3}{extra}", passes, fails

def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)

        # ----- PART 1: test.owner on previously-restricted pages -----
        context = browser.new_context(viewport={'width': 1400, 'height': 900})
        page = context.new_page()
        login(page, OWNER_EMAIL, OWNER_PASSWORD)
        print(f"=== PART 1: test.owner on previously-restricted pages ===")
        print(f"{'Page':<45} {'C':>3} {'E':>3} {'CE':>3} {'EC':>3}  note")
        print('-' * 90)
        p1_pass = p1_fail = p1_skip = 0
        for url in OWNER_ONLY_PAGES:
            r, n = run_four_state(page, url)
            line, passes, fails = report(r, n)
            print(f"{url:<45} {line}")
            if r is None:
                p1_skip += 1
            else:
                p1_pass += passes
                p1_fail += fails
        print('-' * 90)
        print(f"Part 1: PASS={p1_pass} FAIL={p1_fail} SKIPPED={p1_skip}\n")
        context.close()

        # ----- PART 2: RTL Hebrew mode with test.owner -----
        context = browser.new_context(viewport={'width': 1400, 'height': 900})
        context.add_cookies([{
            'name': '.AspNetCore.Culture',
            'value': 'c=he-IL|uic=he-IL',
            'domain': 'localhost',
            'path': '/',
        }])
        page = context.new_page()
        login(page, OWNER_EMAIL, OWNER_PASSWORD)

        # Verify dir=rtl is active
        dir_attr = page.evaluate("document.documentElement.getAttribute('dir')")
        print(f"=== PART 2: RTL Hebrew mode (dir={dir_attr}) ===")
        print(f"{'Page':<45} {'C':>3} {'E':>3} {'CE':>3} {'EC':>3}  note")
        print('-' * 90)
        p2_pass = p2_fail = p2_skip = 0
        for url in RTL_PAGES:
            r, n = run_four_state(page, url)
            line, passes, fails = report(r, n)
            print(f"{url:<45} {line}")
            if r is None:
                p2_skip += 1
            else:
                p2_pass += passes
                p2_fail += fails
        print('-' * 90)
        print(f"Part 2 (RTL): PASS={p2_pass} FAIL={p2_fail} SKIPPED={p2_skip}\n")

        # Final visual evidence
        page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'true')")
        page.goto('http://localhost:5000/Calendar/Chores')
        page.wait_for_load_state('networkidle')
        page.wait_for_timeout(700)
        page.screenshot(path='/tmp/rtl_chores_collapsed.png')

        page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'false')")
        page.goto('http://localhost:5000/Owner/Index')
        page.wait_for_load_state('networkidle')
        page.wait_for_timeout(700)
        page.screenshot(path='/tmp/rtl_owner_expanded.png')

        total_fail = p1_fail + p2_fail
        total_pass = p1_pass + p2_pass
        print(f"=== OVERALL: PASS={total_pass} FAIL={total_fail} ===")
        if total_fail == 0:
            print("ALL TESTS PASS.")

        context.close()
        browser.close()
        return 0 if total_fail == 0 else 1

if __name__ == '__main__':
    sys.exit(main())
