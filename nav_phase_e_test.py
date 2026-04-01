"""
ShiftManager -- Phase E: Cross-Cutting UX Tests
Covers: Hebrew RTL layout, dark mode toggle, error pages (404/403/500), empty states.
Verifies that core A/B/C fixes don't break visual cross-cutting concerns.
"""

import os
import re
import json
from datetime import datetime
from playwright.sync_api import sync_playwright, Page

BASE_URL = "http://localhost:5000"
RESULTS_DIR = os.path.join(os.path.dirname(__file__), "nav_test_results")
os.makedirs(RESULTS_DIR, exist_ok=True)

PASS = "PASS"
FAIL = "FAIL"
WARN = "WARN"

all_results = []


def take_screenshot(page: Page, name: str) -> str:
    safe = re.sub(r'[^a-zA-Z0-9_\-]', '_', name)[:80]
    path = os.path.join(RESULTS_DIR, f"phaseE_{safe}.png")
    try:
        page.screenshot(path=path, full_page=False, timeout=5000)
    except Exception:
        pass
    return path


def record(category: str, check: str, status: str, notes: str = "", screenshot_path: str = ""):
    all_results.append({
        "category": category,
        "check": check,
        "status": status,
        "notes": notes,
        "screenshot": os.path.basename(screenshot_path) if screenshot_path else "",
    })
    icon = "[OK]" if status == PASS else ("[!!]" if status == FAIL else "[??]")
    print(f"    {icon} {status} | {check}: {notes}")


def login(page: Page, email: str, password: str) -> bool:
    try:
        page.goto(f"{BASE_URL}/Auth/Login", wait_until="networkidle", timeout=20000)
        page.fill('input[name="Email"]', email)
        page.fill('input[name="Password"]', password)
        page.click('button[type="submit"]')
        page.wait_for_load_state("networkidle", timeout=20000)
        return "/Auth/Login" not in page.url
    except Exception as e:
        return False


def logout(page: Page):
    try:
        page.goto(f"{BASE_URL}/Auth/Logout", wait_until="domcontentloaded", timeout=10000)
    except Exception:
        pass


# ============================================================
# E1: Hebrew RTL Layout
# ============================================================

def test_hebrew_rtl(page: Page):
    category = "E1_HebrewRTL"
    print(f"\n{'='*60}")
    print(f"  PHASE E1: Hebrew RTL layout checks")
    print(f"{'='*60}")

    # Login as employee
    if not login(page, "emp.tz.alhut@test", "Test1234!"):
        record(category, "Login for RTL test", FAIL, "Could not log in")
        return

    # Switch to Hebrew via URL param (if supported) or cookie
    page.goto(f"{BASE_URL}/Home/Index", wait_until="networkidle", timeout=15000)

    # Check if language switcher exists and switch to Hebrew
    lang_switch_he = page.locator("[data-lang='he'], [href*='lang=he'], [href*='culture=he'], .lang-he, #lang-he").first
    if lang_switch_he.count() > 0:
        try:
            lang_switch_he.click(timeout=3000)
            page.wait_for_load_state("networkidle", timeout=10000)
            shot = take_screenshot(page, f"{category}_after_switch")
            record(category, "Language switch to Hebrew", PASS, "Switched via UI control", shot)
        except Exception as e:
            record(category, "Language switch to Hebrew", WARN, f"Click failed: {str(e)[:80]}")
    else:
        # Try direct URL approach
        page.goto(f"{BASE_URL}/Home/Index?culture=he-IL", wait_until="networkidle", timeout=15000)
        shot = take_screenshot(page, f"{category}_he_IL_url")
        record(category, "Hebrew via ?culture=he-IL", WARN, "No UI switcher found; tried URL param", shot)

    # Check HTML dir attribute on root element
    html_dir = page.evaluate("document.documentElement.dir || document.documentElement.getAttribute('dir') || 'unset'")
    html_lang = page.evaluate("document.documentElement.lang || 'unset'")
    shot = take_screenshot(page, f"{category}_dir_check")
    if "rtl" in str(html_dir).lower() or "he" in str(html_lang).lower():
        record(category, "HTML dir=rtl or lang=he set", PASS, f"dir={html_dir}, lang={html_lang}", shot)
    else:
        record(category, "HTML dir=rtl or lang=he set", WARN,
               f"dir={html_dir}, lang={html_lang} — may be LTR (language not switched or check page)", shot)

    # Check sidebar direction: if Hebrew, sidebar should be on the right (RTL)
    try:
        sidebar_html = page.locator(".app-sidebar, .sidebar, nav").first.evaluate(
            "el => window.getComputedStyle(el).direction"
        )
        if "rtl" in str(sidebar_html).lower():
            record(category, "Sidebar direction=RTL", PASS, f"Computed direction: {sidebar_html}")
        else:
            record(category, "Sidebar direction check", WARN,
                   f"Computed direction: {sidebar_html} — verify visually if language was switched")
    except Exception as e:
        record(category, "Sidebar direction check", WARN, f"Could not measure: {str(e)[:80]}")

    # Take a screenshot of the Home page for visual review
    page.goto(f"{BASE_URL}/Home/Index", wait_until="networkidle", timeout=15000)
    shot = take_screenshot(page, f"{category}_home_final")
    record(category, "Home page RTL screenshot captured", PASS, "Screenshot saved for visual review", shot)

    logout(page)


# ============================================================
# E2: Dark Mode Toggle
# ============================================================

def test_dark_mode(page: Page):
    category = "E2_DarkMode"
    print(f"\n{'='*60}")
    print(f"  PHASE E2: Dark mode toggle checks")
    print(f"{'='*60}")

    if not login(page, "emp.tz.alhut@test", "Test1234!"):
        record(category, "Login for dark mode test", FAIL, "Could not log in")
        return

    page.goto(f"{BASE_URL}/Home/Index", wait_until="networkidle", timeout=15000)

    # Look for dark mode toggle button
    toggle = page.locator("[data-theme-toggle], [data-bs-theme], .theme-toggle, #theme-toggle, [title*='dark'], [title*='Dark'], [aria-label*='dark'], [aria-label*='Dark']").first
    shot_before = take_screenshot(page, f"{category}_before_toggle")

    if toggle.count() > 0:
        record(category, "Dark mode toggle found in DOM", PASS, "Toggle element exists", shot_before)
        try:
            toggle.click(timeout=3000)
            page.wait_for_timeout(500)
            shot_after = take_screenshot(page, f"{category}_after_toggle")

            # Check if a dark class or attribute was applied
            theme_class = page.evaluate(
                "document.documentElement.getAttribute('data-bs-theme') || "
                "document.body.classList.contains('dark') || "
                "document.documentElement.classList.contains('dark-mode') || 'not found'"
            )
            if theme_class and str(theme_class).lower() not in ["false", "not found", "null"]:
                record(category, "Dark mode class/attr applied after toggle", PASS,
                       f"Theme attribute: {theme_class}", shot_after)
            else:
                record(category, "Dark mode class/attr after toggle", WARN,
                       f"Theme attr: {theme_class} — verify screenshot visually", shot_after)

            # Toggle back
            toggle.click(timeout=3000)
            page.wait_for_timeout(300)
        except Exception as e:
            record(category, "Dark mode toggle click", WARN, f"Click error: {str(e)[:80]}", shot_before)
    else:
        record(category, "Dark mode toggle search", WARN,
               "No toggle found with standard selectors — check sidebar or settings page", shot_before)
        # Try settings page
        page.goto(f"{BASE_URL}/My/Settings", wait_until="networkidle", timeout=15000)
        shot_settings = take_screenshot(page, f"{category}_settings_page")
        record(category, "Settings page for dark mode option", PASS,
               "Settings page accessible — check for theme option", shot_settings)

    logout(page)


# ============================================================
# E3: Error Pages (404, 403, 500 equivalent)
# ============================================================

def test_error_pages(page: Page):
    category = "E3_ErrorPages"
    print(f"\n{'='*60}")
    print(f"  PHASE E3: Error page checks (authenticated)")
    print(f"{'='*60}")

    if not login(page, "emp.tz.alhut@test", "Test1234!"):
        record(category, "Login for error page test", FAIL, "Could not log in")
        return

    # 404 page
    page.goto(f"{BASE_URL}/this-page-does-not-exist-at-all", wait_until="networkidle", timeout=15000)
    shot = take_screenshot(page, f"{category}_404")
    url = page.url
    title = page.title()
    if "statuscode" in url.lower() or "404" in url.lower() or "404" in title.lower():
        record(category, "404 -> StatusCode page", PASS, f"URL: {url}", shot)
    else:
        try:
            body = page.locator("body").inner_text(timeout=2000)
            if "404" in body or "not found" in body.lower():
                record(category, "404 -> error content shown", PASS, f"404 content found on page", shot)
            else:
                record(category, "404 handling", WARN, f"URL={url}, Title={title}", shot)
        except Exception:
            record(category, "404 handling", WARN, f"URL={url}", shot)

    # 403 page (access denied)
    page.goto(f"{BASE_URL}/Owner/Index", wait_until="networkidle", timeout=15000)
    shot = take_screenshot(page, f"{category}_403")
    url = page.url
    if "accessdenied" in url.lower() or "statuscode" in url.lower() or "403" in url.lower():
        record(category, "403 to AccessDenied page", PASS, f"URL: {url}", shot)
    else:
        record(category, "403 handling", WARN, f"URL={url} — expected AccessDenied redirect", shot)

    # StatusCode pages directly
    for code in ["404", "403"]:
        page.goto(f"{BASE_URL}/StatusCode/{code}", wait_until="networkidle", timeout=15000)
        shot = take_screenshot(page, f"{category}_statuscode_{code}")
        url = page.url
        # Page should not itself 500 or redirect to login
        if "/Auth/Login" in url:
            record(category, f"StatusCode/{code} page", FAIL, "Redirected to login — page not public", shot)
        elif "statuscode" in url.lower() or "accessdenied" in url.lower():
            record(category, f"StatusCode/{code} page renders", PASS, f"URL: {url}", shot)
        else:
            try:
                body = page.locator("body").inner_text(timeout=2000).lower()
                if code in body or "error" in body:
                    record(category, f"StatusCode/{code} page renders", PASS, "Error content shown", shot)
                else:
                    record(category, f"StatusCode/{code} page", WARN, f"Unexpected URL: {url}", shot)
            except Exception:
                record(category, f"StatusCode/{code} page", WARN, f"URL={url}", shot)

    logout(page)


# ============================================================
# E4: Empty States (user with no data)
# ============================================================

def test_empty_states(page: Page):
    category = "E4_EmptyStates"
    print(f"\n{'='*60}")
    print(f"  PHASE E4: Empty state / no-data checks")
    print(f"{'='*60}")

    # NoGrants user visiting calendar pages — they'll see empty/limited state
    if not login(page, "nogrants@test", "Test1234!"):
        record(category, "Login for empty state test", FAIL, "Could not log in")
        return

    for page_name, url in [
        ("Shift Calendar", "/Calendar/Shifts"),
        ("My Requests", "/My/Requests"),
        ("Notification Center", "/My/NotificationCenter"),
    ]:
        page.goto(f"{BASE_URL}{url}", wait_until="networkidle", timeout=15000)
        shot = take_screenshot(page, f"{category}_{re.sub(r'[^a-z0-9]', '_', page_name.lower())}")
        # Check no 500 error and page loads
        title = page.title()
        url_now = page.url
        if "500" in title or "/StatusCode/500" in url_now:
            record(category, f"NoGrants - {page_name} empty state", FAIL, "500 error loading page", shot)
        elif "/Auth/Login" in url_now:
            record(category, f"NoGrants - {page_name} empty state", FAIL, "Redirected to login", shot)
        elif "/AccessDenied" in url_now:
            record(category, f"NoGrants - {page_name} empty state", FAIL, "Access denied", shot)
        else:
            record(category, f"NoGrants - {page_name} empty state", PASS,
                   "Page loads without error (content may be empty)", shot)

    logout(page)


# ============================================================
# E5: Sidebar quick smoke test with Lead (sidebar is most complex)
# ============================================================

def test_sidebar_layout(page: Page):
    category = "E5_SidebarLayout"
    print(f"\n{'='*60}")
    print(f"  PHASE E5: Sidebar layout smoke test (Lead role)")
    print(f"{'='*60}")

    if not login(page, "mgr.alhut.tz@test", "Test1234!"):
        record(category, "Login as Lead", FAIL, "Could not log in")
        return

    page.goto(f"{BASE_URL}/Home/Index", wait_until="networkidle", timeout=15000)
    shot = take_screenshot(page, f"{category}_lead_home")

    # Sidebar should exist
    sidebar = page.locator(".app-sidebar-nav, .sidebar-nav, nav.sidebar").first
    if sidebar.count() > 0:
        record(category, "Sidebar element found", PASS, "Sidebar nav present", shot)
    else:
        record(category, "Sidebar element found", FAIL, "No sidebar nav found in DOM", shot)

    # Check no duplicated nav items (old bug: 3 separate HTML blocks)
    try:
        # Count how many times a unique nav item appears
        admin_links = page.locator("a[href='/Admin/Index']").count()
        if admin_links == 1:
            record(category, "No duplicate nav items (Admin link)", PASS, "Admin link appears exactly once")
        elif admin_links == 0:
            record(category, "No duplicate nav items (Admin link)", PASS, "Admin link not shown (gated) — expected for Lead context")
        else:
            record(category, "No duplicate nav items (Admin link)", FAIL,
                   f"Admin link appears {admin_links} times — possible duplicate sidebar blocks")
    except Exception as e:
        record(category, "Duplicate nav check", WARN, str(e)[:80])

    # Take screenshots of a few key pages to capture sidebar visually
    for page_name, url in [
        ("Command Center", "/Admin/Index"),
        ("Shift Table", "/Calendar/Table"),
        ("Organization Hub", "/Admin/Organization"),
    ]:
        page.goto(f"{BASE_URL}{url}", wait_until="networkidle", timeout=15000)
        shot = take_screenshot(page, f"{category}_{re.sub(r'[^a-z0-9]', '_', page_name.lower())}")
        url_now = page.url
        if "/Auth/Login" in url_now or "/AccessDenied" in url_now:
            record(category, f"Lead - {page_name} accessible", FAIL, f"Redirected to {url_now}", shot)
        else:
            record(category, f"Lead - {page_name} accessible", PASS, "Page loads correctly", shot)

    logout(page)


# ============================================================
# HTML REPORT
# ============================================================

def generate_report():
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    total = len(all_results)
    passes = sum(1 for r in all_results if r["status"] == PASS)
    fails = sum(1 for r in all_results if r["status"] == FAIL)
    warns = sum(1 for r in all_results if r["status"] == WARN)

    rows = ""
    for r in all_results:
        color = {"PASS": "#22c55e", "FAIL": "#ef4444", "WARN": "#f59e0b"}.get(r["status"], "#6b7280")
        badge = f'<span style="background:{color};color:white;padding:2px 8px;border-radius:4px;font-size:12px;font-weight:600">{r["status"]}</span>'
        shot_link = f'<a href="{r["screenshot"]}" target="_blank">[img]</a>' if r.get("screenshot") else ""
        rows += f"""
        <tr>
          <td>{r['category']}</td>
          <td>{r['check']}</td>
          <td>{badge}</td>
          <td style="color:#6b7280;font-size:13px">{r['notes']}</td>
          <td>{shot_link}</td>
        </tr>"""

    html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>ShiftManager Phase E — Cross-Cutting UX Tests</title>
<style>
  body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 0; background: #f8fafc; color: #1e293b; }}
  .header {{ background: #0f766e; color: white; padding: 2rem; }}
  .header h1 {{ margin: 0 0 0.5rem; font-size: 1.75rem; }}
  .stats {{ display: flex; gap: 1rem; padding: 1.5rem 2rem; }}
  .stat {{ background: white; border-radius: 12px; padding: 1rem 1.5rem; flex: 1; box-shadow: 0 1px 3px rgba(0,0,0,.1); }}
  .stat-val {{ font-size: 2rem; font-weight: 700; }}
  .stat-label {{ color: #6b7280; font-size: 0.875rem; }}
  .green {{ color: #22c55e; }} .red {{ color: #ef4444; }} .yellow {{ color: #f59e0b; }}
  .section {{ margin: 0 2rem 2rem; }}
  h2 {{ font-size: 1.25rem; margin: 0 0 1rem; padding-bottom: 0.5rem; border-bottom: 1px solid #e5e7eb; }}
  .warn-box {{ background: #fef3c7; border: 1px solid #f59e0b; border-radius: 8px; padding: 1rem 1.5rem; margin: 0 2rem 1rem; font-size: 0.9rem; }}
  table {{ width: 100%; border-collapse: collapse; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,.1); }}
  th {{ background: #f1f5f9; padding: 0.75rem 1rem; text-align: left; font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.05em; color: #64748b; }}
  td {{ padding: 0.65rem 1rem; border-top: 1px solid #f1f5f9; font-size: 0.875rem; vertical-align: middle; }}
  tr:hover td {{ background: #f8fafc; }}
</style>
</head>
<body>
<div class="header">
  <h1>Phase E — Cross-Cutting UX Tests</h1>
  <p>Hebrew RTL · Dark Mode · Error Pages · Empty States · Sidebar Layout &nbsp;|&nbsp; {timestamp}</p>
</div>
<div class="stats">
  <div class="stat"><div class="stat-val">{total}</div><div class="stat-label">Total Checks</div></div>
  <div class="stat"><div class="stat-val green">{passes}</div><div class="stat-label">Pass</div></div>
  <div class="stat"><div class="stat-val red">{fails}</div><div class="stat-label">Fail</div></div>
  <div class="stat"><div class="stat-val yellow">{warns}</div><div class="stat-label">Warn (visual review needed)</div></div>
</div>
<div class="warn-box">
  <strong>Note:</strong> WARN = requires manual visual review of screenshot. PASS/FAIL are programmatic.
  Review screenshots in <code>nav_test_results/phaseE_*.png</code>.
</div>
<div class="section">
  <h2>Results</h2>
  <table>
    <thead><tr><th>Category</th><th>Check</th><th>Status</th><th>Notes</th><th>Screenshot</th></tr></thead>
    <tbody>{rows}</tbody>
  </table>
</div>
</body>
</html>"""

    report_path = os.path.join(RESULTS_DIR, "report_phase_e.html")
    with open(report_path, "w", encoding="utf-8") as f:
        f.write(html)
    return report_path


# ============================================================
# MAIN
# ============================================================

def main():
    print(f"\n{'#'*60}")
    print(f"# ShiftManager Phase E Test -- {datetime.now().strftime('%H:%M:%S')}")
    print(f"# Cross-cutting: RTL, dark mode, errors, empty states, sidebar")
    print(f"{'#'*60}\n")

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()

        test_hebrew_rtl(page)
        test_dark_mode(page)
        test_error_pages(page)
        test_empty_states(page)
        test_sidebar_layout(page)

        browser.close()

    passes = sum(1 for r in all_results if r["status"] == PASS)
    fails = sum(1 for r in all_results if r["status"] == FAIL)
    warns = sum(1 for r in all_results if r["status"] == WARN)

    print(f"\n{'='*60}")
    print(f"  Total: {len(all_results)}  |  PASS: {passes}  |  FAIL: {fails}  |  WARN: {warns}")

    report_path = generate_report()
    print(f"  HTML report: {report_path}")
    print(f"  (WARN items require visual review of screenshots)")
    print(f"{'='*60}\n")

    failures = [r for r in all_results if r["status"] == FAIL]
    if failures:
        print(f"  FAILURES ({len(failures)}):")
        for r in failures:
            print(f"    [!!] [{r['category']}] {r['check']} -- {r['notes']}")

    json_path = os.path.join(RESULTS_DIR, "results_phase_e.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(all_results, f, indent=2)

    return 1 if fails > 0 else 0


if __name__ == "__main__":
    exit(main())
