"""Test analytics with explicit scope=Molecule&level=UsersInMolecule params."""
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

    # Try different scope/level combinations that should enable ShiftCategory
    urls_to_try = [
        f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14&scope=Molecule&level=UsersInMolecule",
        f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14&scope=0&level=1",
        f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14",
    ]

    for url in urls_to_try:
        print(f"\nURL: {url}")
        page.goto(url, wait_until="networkidle")
        time.sleep(4)
        content = page.content()
        cat_present = "cat-select" in content or "shiftCategoryId" in content.lower()
        selects = page.locator("select").count()
        print(f"  ShiftCategory select: {cat_present}, total selects: {selects}")

        # Check the available scope/level dropdowns or navigation pills
        scope_links = page.locator("[href*='scope='], [href*='level='], [onclick*='scope']").all()
        print(f"  Scope links/buttons: {len(scope_links)}")
        for link in scope_links[:5]:
            href = link.get_attribute("href") or link.get_attribute("onclick") or ""
            txt = link.inner_text()[:30]
            print(f"    {txt!r}: {href[:80]}")

        # Check if there are level selector pills/tabs
        level_pills = page.locator("[class*='pill'], [class*='tab'], [class*='level']").all()
        print(f"  Level pills/tabs: {len(level_pills)}")
        for pill in level_pills[:5]:
            txt = pill.inner_text()[:40]
            cls = pill.get_attribute("class") or ""
            print(f"    {txt!r} ({cls[:40]})")

    # Now inspect the full Analytics page HTML to find scope navigation
    page.goto(f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14", wait_until="networkidle")
    time.sleep(4)
    content = page.content()

    # Find level navigation
    level_idx = content.find("UsersInMolecule")
    if level_idx >= 0:
        print(f"\nUsersInMolecule context: {content[max(0,level_idx-200):level_idx+300]}")

    # Find all links containing 'level'
    level_links = page.locator("a[href*='level']").all()
    print(f"\nLevel links: {len(level_links)}")
    for lnk in level_links[:10]:
        href = lnk.get_attribute("href") or ""
        txt = lnk.inner_text()[:30]
        print(f"  {txt!r}: {href}")

    ctx.close()
    browser.close()
    print("Done.")
