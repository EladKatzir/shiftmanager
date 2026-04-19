"""Comprehensive sidebar test across all authenticated pages.

For each page, tests four scenarios:
  [C]  Load with collapsed state    -> verify sidebar is COLLAPSED
  [E]  Load with expanded state     -> verify sidebar is EXPANDED
  [CE] Load collapsed, click toggle -> verify sidebar EXPANDS
  [EC] Load expanded, click toggle  -> verify sidebar COLLAPSES

Uses raw mouse coordinates (page.mouse.click) to simulate actual user clicks
rather than Playwright's element-centered targeting.
"""
from playwright.sync_api import sync_playwright
import sys

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

# All pages reachable from the sidebar nav + pages historically involved in sidebar bugs
PAGES = [
    '/Home',
    '/Home/Index',
    '/Admin/Index',
    '/Calendar/Shifts',
    '/Calendar/Month',
    '/Calendar/Table',
    '/Calendar/Overview',
    '/Calendar/Chores',
    '/Calendar/OnCall',
    '/Public/Chores',
    '/Public/OnDuty',
    '/Schedule/Index',
    '/Requests/Index',
    '/My/Requests',
    '/My/Profile',
    '/My/Settings',
    '/My/Help',
    '/My/NotificationCenter',
    '/My/ApiKeys',
    '/MyTeam/Index',
    '/Friends',
    '/Admin/Users',
    '/Admin/Companies',
    '/Admin/Analytics',
    '/Admin/AuditLog',
    '/Admin/Config',
    '/Admin/Announcements',
    '/Admin/Organization',
    '/Admin/Organization/Areas',
    '/Admin/Organization/ChoreTypes',
    '/Admin/Organization/Departments',
    '/Admin/Organization/DutyTypes',
    '/Admin/Organization/JobTypes',
    '/Admin/Organization/Molecules',
    '/Admin/Organization/Projects',
    '/Admin/Organization/ShiftGroupings',
    '/Admin/DutyRotation',
    '/Owner/Index',
    '/Owner/Programs',
    '/Owner/Blueprints',
    '/Owner/Telemetry',
    '/Owner/FeatureFlags',
    '/Owner/SystemHealth',
    '/Director/ViewAsMode',
    '/Director/NotificationHub',
    '/Director/CompanyFilter',
    '/Assignments/Manage',
]

def test_page(page, url):
    """Run all four scenarios for a single page. Returns (results_dict, skipped_reason)."""
    results = {}

    # Scenario [C] — load collapsed, verify collapsed
    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'true')")
    page.goto(f'http://localhost:5000{url}')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(500)

    final_url = page.url.replace('http://localhost:5000', '')
    if final_url.startswith('/AccessDenied') or final_url.startswith('/Auth/Login'):
        return None, f'NO_ACCESS ({final_url})'

    state = page.evaluate("(document.getElementById('appSidebar')||{}).className || 'MISSING'")
    if state == 'MISSING':
        return None, 'NO_SIDEBAR (page probably uses different layout)'
    results['C'] = 'is-collapsed' in state

    # Verify toggle exists + clickable-looking
    toggle_info = page.evaluate("""() => {
        const t = document.getElementById('sidebarToggle');
        if (!t) return null;
        const r = t.getBoundingClientRect();
        const cs = window.getComputedStyle(t);
        if (cs.display === 'none' || cs.visibility === 'hidden' || r.width === 0) return {visible: false};
        return {visible: true, x: r.left + r.width/2, y: r.top + r.height/2, left: r.left, width: r.width};
    }""")
    if not toggle_info or not toggle_info.get('visible'):
        return results, 'NO_TOGGLE'

    # Scenario [CE] — from collapsed, click to expand
    before = state
    page.mouse.click(toggle_info['x'], toggle_info['y'])
    page.wait_for_timeout(400)
    after = page.evaluate("document.getElementById('appSidebar').className")
    results['CE'] = ('is-collapsed' in before) and ('is-collapsed' not in after)

    # Scenario [E] — load expanded, verify expanded
    page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'false')")
    page.goto(f'http://localhost:5000{url}')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(500)
    state = page.evaluate("document.getElementById('appSidebar').className")
    results['E'] = 'is-collapsed' not in state

    # Scenario [EC] — from expanded, click to collapse
    toggle_info = page.evaluate("""() => {
        const t = document.getElementById('sidebarToggle');
        if (!t) return null;
        const r = t.getBoundingClientRect();
        return {x: r.left + r.width/2, y: r.top + r.height/2};
    }""")
    if not toggle_info:
        return results, 'TOGGLE_VANISHED'
    before = state
    page.mouse.click(toggle_info['x'], toggle_info['y'])
    page.wait_for_timeout(400)
    after = page.evaluate("document.getElementById('appSidebar').className")
    results['EC'] = ('is-collapsed' not in before) and ('is-collapsed' in after)

    return results, None


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={'width': 1400, 'height': 900})
        page = context.new_page()

        login(page)
        print(f"Logged in as {EMAIL}\n")
        print(f"{'Page':<45} {'C':>3} {'E':>3} {'CE':>3} {'EC':>3}  note")
        print('-' * 90)

        total_pages = 0
        total_pass = 0
        total_fail = 0
        skipped = 0
        all_fails = []

        for url in PAGES:
            results, note = test_page(page, url)

            if results is None:
                print(f"{url:<45} {'-':>3} {'-':>3} {'-':>3} {'-':>3}  {note}")
                skipped += 1
                continue

            total_pages += 1
            marks = {}
            for k in ('C', 'E', 'CE', 'EC'):
                v = results.get(k)
                if v is True:
                    marks[k] = 'OK '
                    total_pass += 1
                elif v is False:
                    marks[k] = 'FAIL'
                    total_fail += 1
                    all_fails.append(f"{url} [{k}]")
                else:
                    marks[k] = '-  '
            extra = f'  {note}' if note else ''
            print(f"{url:<45} {marks['C']:>3} {marks['E']:>3} {marks['CE']:>3} {marks['EC']:>3}{extra}")

        print('-' * 90)
        print(f"Pages tested: {total_pages}  |  Skipped: {skipped}  |  PASS: {total_pass}  FAIL: {total_fail}")
        if all_fails:
            print('\nFAILED TESTS:')
            for f in all_fails:
                print(f"  - {f}")
        else:
            print('\nALL TESTS PASS.')

        # Final screenshot of the notoriously-broken page
        page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'true')")
        page.goto('http://localhost:5000/Calendar/Chores')
        page.wait_for_load_state('networkidle')
        page.wait_for_timeout(700)
        page.screenshot(path='/tmp/final_chores_collapsed.png')
        page.evaluate("localStorage.setItem('shifty_sidebar_collapsed', 'false')")
        page.goto('http://localhost:5000/Calendar/Chores')
        page.wait_for_load_state('networkidle')
        page.wait_for_timeout(700)
        page.screenshot(path='/tmp/final_chores_expanded.png')

        # Also verify Razor JS cleanup landed
        html = page.content()
        razor_fix_live = "classList.remove('sidebar-initially-collapsed')" in html
        print(f"\nRazor JS cleanup live: {razor_fix_live}")

        browser.close()
        return 0 if total_fail == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
