"""Check analytics for molecule-scoped user (Lead) and verify ShiftCategory dropdown."""
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

    # === Test 1: As mgr.alhut.tz@test (Lead) -> Justice/Analytics page ===
    for user_email in ["mgr.alhut.tz@test", "moladmin.oren@test", "dir.alhut@test"]:
        print(f"\n=== Testing as {user_email} ===")
        ctx = browser.new_context(viewport={"width": 1440, "height": 900})
        page = ctx.new_page()
        login(page, user_email)
        print(f"  Login URL: {page.url}")

        # Navigate to analytics (might be /Admin/Analytics or /My/Justice)
        page.goto(f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14", wait_until="networkidle")
        time.sleep(4)
        print(f"  Analytics URL: {page.url}")

        content = page.content()
        selects = page.locator("select").all()
        cat_present = "ShiftCategoryId" in content or "shiftCategoryId" in content or "cat-select" in content
        print(f"  ShiftCategory in content: {cat_present}")
        print(f"  Selects on page: {len(selects)}")
        for sel in selects:
            name = sel.get_attribute("name") or sel.get_attribute("id") or ""
            opts = [o.text_content().strip()[:20] for o in sel.locator("option").all()[:4]]
            print(f"    select={name!r}: {opts}")

        # Canvas/chart elements
        charts = page.locator("canvas, .apexcharts-canvas").count()
        circles = page.locator("circle[stroke-dasharray]").count()
        svgs = page.locator("svg").count()
        print(f"  canvas={charts}, circle[stroke-dasharray]={circles}, svg={svgs}")

        page.screenshot(path=os.path.join(ARTIFACTS, f"ab-final-analytics-{user_email.split('@')[0]}.png"), full_page=False)
        ctx.close()

    browser.close()
    print("\nDone.")
