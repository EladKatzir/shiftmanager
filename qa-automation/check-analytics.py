"""
Detailed check of Analytics page: shifts calendar 'By User' mode + analytics selectors.
"""
import os, time
os.environ["PYTHONIOENCODING"] = "utf-8"
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5000"
ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
os.makedirs(ARTIFACTS, exist_ok=True)

def login(page, email="owner2@test", pwd="Test1234!"):
    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill("input[name='Email']", email)
    page.fill("input[name='Password']", pwd)
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    time.sleep(1)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)

    # === CHECK 1: Shifts calendar By User mode ===
    print("=== Check 1: Shifts Calendar By User mode ===")
    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()
    login(page, "owner2@test")

    # Log in as a user in Tzafona to see the Shifts calendar for that molecule
    # Actually log in as mgr.alhut.tz@test who is Lead in Tzafona/Alhut
    page.goto(f"{BASE}/Auth/Logout", wait_until="domcontentloaded")
    time.sleep(0.5)
    login(page, "mgr.alhut.tz@test")
    print(f"  Logged in as mgr.alhut.tz@test, URL: {page.url}")

    page.goto(f"{BASE}/Calendar/Shifts", wait_until="networkidle")
    time.sleep(3)

    # Check if there's a By User tab/button
    by_user_btn = page.locator("button:has-text('By User'), [data-view='user'], button[onclick*='user']")
    print(f"  'By User' button count: {by_user_btn.count()}")

    # Switch to By User view
    if by_user_btn.count() > 0:
        by_user_btn.first.click()
        page.wait_for_load_state("networkidle")
        time.sleep(2)
        page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-shifts-by-user-mgr.png"), full_page=True)

    # Check content
    content = page.content()
    print(f"  'Emp TZ Alhut' in Shifts By User: {'Emp TZ Alhut' in content}")
    print(f"  'emp.tz.alhut' in Shifts By User: {'emp.tz.alhut' in content}")

    # Also try as owner2 and check the shift calendar for the Tzafona molecule
    page.goto(f"{BASE}/Auth/Logout", wait_until="domcontentloaded")
    time.sleep(0.5)
    login(page, "owner2@test")

    page.goto(f"{BASE}/Calendar/Shifts", wait_until="networkidle")
    time.sleep(3)

    # Check molecule selector
    mol_sel = page.locator("select[name='molecule'], select[name='MoleculeId'], #moleculeId, select[id*='molecule']")
    print(f"\n  Molecule selector count: {mol_sel.count()}")
    if mol_sel.count() > 0:
        opts = mol_sel.first.locator("option").all()
        print(f"  Molecule options:")
        for opt in opts[:10]:
            val = opt.get_attribute("value") or ""
            txt = opt.text_content() or ""
            print(f"    {val}: {txt.strip()}")

    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-shifts-owner-initial.png"), full_page=True)

    # Check if there's "By User" button
    by_user_btn2 = page.locator("button:has-text('By User')")
    print(f"\n  By User button (owner): {by_user_btn2.count()}")
    if by_user_btn2.count() > 0:
        by_user_btn2.first.click()
        time.sleep(2)
        page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-shifts-owner-by-user.png"), full_page=True)
        content2 = page.content()
        print(f"  'Emp TZ Alhut' in Shifts By User (owner): {'Emp TZ Alhut' in content2}")

    ctx.close()

    # === CHECK 2: Analytics selectors deep dive ===
    print("\n=== Check 2: Analytics page deep inspect ===")
    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()
    login(page)

    page.goto(f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14", wait_until="networkidle")
    time.sleep(4)

    # List ALL selects on analytics page
    selects = page.locator("select").all()
    print(f"  Total selects on Analytics: {len(selects)}")
    for sel in selects:
        name = sel.get_attribute("name") or ""
        id_ = sel.get_attribute("id") or ""
        cls = sel.get_attribute("class") or ""
        opts = [o.text_content().strip()[:20] for o in sel.locator("option").all()[:4]]
        print(f"    select name={name!r} id={id_!r}: {opts}")

    # List canvas elements
    canvases = page.locator("canvas").all()
    print(f"\n  Canvas elements: {len(canvases)}")
    for cv in canvases[:5]:
        id_ = cv.get_attribute("id") or ""
        cls = cv.get_attribute("class") or ""
        print(f"    canvas id={id_!r} class={cls!r}")

    # SVG elements
    svgs = page.locator("svg").all()
    print(f"  SVG elements: {len(svgs)}")

    # Chart.js / ApexCharts
    apex = page.locator(".apexcharts-canvas").all()
    print(f"  ApexCharts: {len(apex)}")

    # Look for chart containers by common patterns
    chart_divs = page.locator("[id*='chart'], [class*='chart'], [id*='Chart'], [class*='Chart']").all()
    print(f"  Chart divs: {len(chart_divs)}")
    for d in chart_divs[:5]:
        id_ = d.get_attribute("id") or ""
        cls = (d.get_attribute("class") or "")[:40]
        print(f"    div id={id_!r} class={cls!r}")

    # Full page screenshot at full resolution
    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-C1-analytics-full.png"), full_page=True)

    # Check for donut charts or any chart.js rendered content
    # Look for any canvas rendered content
    chart_js_check = page.evaluate("typeof Chart !== 'undefined'")
    print(f"\n  Chart.js global: {chart_js_check}")
    apex_check = page.evaluate("typeof ApexCharts !== 'undefined'")
    print(f"  ApexCharts global: {apex_check}")

    # Get list of form fields on the analytics page
    inputs = page.locator("input, select").all()
    print(f"\n  All form fields ({len(inputs)}):")
    for field in inputs[:20]:
        tag = field.evaluate("e => e.tagName")
        name = field.get_attribute("name") or ""
        id_ = field.get_attribute("id") or ""
        type_ = field.get_attribute("type") or ""
        if type_ not in ("hidden",) and name:
            print(f"    {tag} name={name!r} id={id_!r} type={type_!r}")

    ctx.close()
    browser.close()
    print("\nDone.")
