"""Test analytics at UsersInMolecule level + Shifts worktype for ShiftCategory dropdown."""
import os, time
os.environ["PYTHONIOENCODING"] = "utf-8"
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5000"
ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"

def login(page, email, pwd="Test1234!"):
    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill("input[name='Email']", email)
    page.fill("input[name='Password']", pwd)
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    time.sleep(1)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1440, "height": 900})
    page = ctx.new_page()
    login(page, "moladmin.oren@test")

    # Navigate to UsersInMolecule view with Shifts worktype (most likely to show ShiftCategory)
    analytics_url = f"{BASE}/Admin/Analytics?scope=Molecule&scopeId=1&level=UsersInMolecule&workType=Shift&excludeExempt=True&basis=BySize&from=2024-01-01&to=2026-06-14"
    print(f"Testing URL: {analytics_url}")
    page.goto(analytics_url, wait_until="networkidle")
    time.sleep(4)

    content = page.content()
    cat_present = "cat-select" in content or "shiftCategoryId" in content.lower() or "CategoryOptions" in content
    selects = page.locator("select").all()
    print(f"ShiftCategory in content: {cat_present}")
    print(f"Total selects: {len(selects)}")
    for sel in selects:
        name = sel.get_attribute("name") or sel.get_attribute("id") or ""
        cls = sel.get_attribute("class") or ""
        opts = [o.text_content().strip()[:25] for o in sel.locator("option").all()[:6]]
        print(f"  select name={name!r} class={cls!r}: {opts}")

    # Screenshot
    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-analytics-usersInMol-shifts.png"), full_page=False)

    # Also try workType=All
    analytics_url2 = f"{BASE}/Admin/Analytics?scope=Molecule&scopeId=1&level=UsersInMolecule&workType=All&excludeExempt=True&basis=BySize&from=2024-01-01&to=2026-06-14"
    print(f"\nTesting URL (All): {analytics_url2}")
    page.goto(analytics_url2, wait_until="networkidle")
    time.sleep(4)

    content2 = page.content()
    cat_present2 = "cat-select" in content2 or "shiftCategoryId" in content2.lower()
    selects2 = page.locator("select").all()
    print(f"ShiftCategory in content: {cat_present2}")
    print(f"Total selects: {len(selects2)}")
    for sel in selects2:
        name = sel.get_attribute("name") or sel.get_attribute("id") or ""
        opts = [o.text_content().strip()[:25] for o in sel.locator("option").all()[:6]]
        print(f"  select name={name!r}: {opts}")

    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-analytics-usersInMol-all.png"), full_page=False)

    # Check SVG chart status
    circles = page.locator("circle[stroke-dasharray]").count()
    svgs = page.locator("svg").count()
    print(f"\nChart elements: circle[stroke-dasharray]={circles}, svg={svgs}")

    # Check for tooltips (look for title elements in SVG or tooltip divs)
    tooltips = page.locator(".tooltip, [role='tooltip'], title").count()
    print(f"Tooltip elements: {tooltips}")

    # Try hovering over a chart element to trigger tooltip
    circle_el = page.locator("circle[stroke-dasharray]").first
    if circle_el.count() > 0:
        try:
            box = circle_el.bounding_box()
            if box:
                page.mouse.move(box["x"] + box["width"]/2, box["y"] + box["height"]/2)
                time.sleep(0.5)
                page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-analytics-tooltip-hover.png"), full_page=False)
                print("  Hovered over donut chart")
        except Exception as e:
            print(f"  Hover error: {e}")

    ctx.close()
    browser.close()
    print("Done.")
