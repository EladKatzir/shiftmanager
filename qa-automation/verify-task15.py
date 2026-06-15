"""
Task 15 verification: chart tooltips + descriptive captions on /Admin/Analytics
- Login as owner2@test / Test1234!
- Navigate to /Admin/Analytics with molecule scope + wide date range
- Verify: donut segments have SVG <title> elements
- Verify: gauge segments have SVG <title> elements
- Verify: ribbon segments have title attributes
- Verify: .chart-caption elements exist under both charts
- Verify: .help-dot exists near SpreadIndex
- Screenshot light mode, dark mode, and Hebrew RTL
"""
import os
from playwright.sync_api import sync_playwright

SCREENSHOTS_DIR = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
os.makedirs(SCREENSHOTS_DIR, exist_ok=True)

BASE_URL = "http://localhost:5000"
# Wide date range so charts have data
ANALYTICS_URL = f"{BASE_URL}/Admin/Analytics?scope=Molecule&scopeId=1&level=UsersInCompany&from=2025-01-01&to=2026-12-31"


def login(page):
    page.goto(f"{BASE_URL}/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.fill("input[name='Email']", "owner2@test")
    page.fill("input[name='Password']", "Test1234!")
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    print(f"After login: {page.url}")


def navigate_analytics(page):
    page.goto(ANALYTICS_URL)
    page.wait_for_load_state("networkidle")
    print(f"Analytics URL: {page.url}")


def verify_tooltips_and_captions(page, label="light"):
    # Check SVG <title> in donut segments
    donut_titles = page.locator(".donut-seg title").all()
    print(f"[{label}] Donut <title> count: {len(donut_titles)}")
    if donut_titles:
        print(f"  First donut title: '{donut_titles[0].text_content()}'")

    # Check SVG <title> in gauge segments
    gauge_titles = page.locator(".gauge-seg title").all()
    print(f"[{label}] Gauge <title> count: {len(gauge_titles)}")
    if gauge_titles:
        print(f"  First gauge title: '{gauge_titles[0].text_content()}'")

    # Check ribbon title attributes
    ribbon_segs = page.locator(".eq-seg").all()
    ribbon_with_title = [s for s in ribbon_segs if s.get_attribute("title")]
    print(f"[{label}] Ribbon segments with title: {len(ribbon_with_title)} / {len(ribbon_segs)}")
    if ribbon_with_title:
        print(f"  First ribbon title: '{ribbon_with_title[0].get_attribute('title')}'")

    # Check .chart-caption elements
    captions = page.locator(".chart-caption").all()
    print(f"[{label}] .chart-caption count: {len(captions)}")
    for i, cap in enumerate(captions):
        print(f"  Caption {i+1}: '{cap.text_content()}'")

    # Check .help-dot element
    help_dot = page.locator(".help-dot").all()
    print(f"[{label}] .help-dot count: {len(help_dot)}")
    if help_dot:
        print(f"  Help-dot title: '{help_dot[0].get_attribute('title')}'")


with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)

    # --- Light mode ---
    page = browser.new_page(viewport={"width": 1400, "height": 900})
    login(page)
    navigate_analytics(page)

    # Try different scope options if current one has no data
    content = page.content()
    if "donut-seg" not in content:
        # Try scope=Molecule scopeId=2
        print("Trying scopeId=2...")
        page.goto(f"{BASE_URL}/Admin/Analytics?scope=Molecule&scopeId=2&level=UsersInCompany&from=2025-01-01&to=2026-12-31")
        page.wait_for_load_state("networkidle")
    if "donut-seg" not in page.content():
        # Try without scope to see what scopes are available
        print("Trying without scopeId...")
        page.goto(f"{BASE_URL}/Admin/Analytics")
        page.wait_for_load_state("networkidle")
        # Look for molecule options
        scope_links = page.locator("a[href*='Analytics']").all()
        for link in scope_links[:5]:
            print(f"  Scope link: {link.get_attribute('href')} / {link.inner_text()}")
        # Try clicking first scope option
        scope_opts = page.locator("select[name='scopeId'] option, .scope-option").all()
        for opt in scope_opts[:5]:
            print(f"  Scope opt: {opt.inner_text()} value={opt.get_attribute('value')}")

    page.screenshot(path=os.path.join(SCREENSHOTS_DIR, "task15-tooltips-light.png"), full_page=True)
    print("\n=== LIGHT MODE VERIFICATION ===")
    verify_tooltips_and_captions(page, "light")

    # --- Dark mode ---
    page.evaluate("document.documentElement.setAttribute('data-theme', 'dark')")
    page.wait_for_timeout(500)
    page.screenshot(path=os.path.join(SCREENSHOTS_DIR, "task15-tooltips-dark.png"), full_page=True)
    print("\n=== DARK MODE VERIFICATION ===")
    verify_tooltips_and_captions(page, "dark")

    # --- Hebrew RTL ---
    page.evaluate("document.documentElement.setAttribute('dir', 'rtl'); document.documentElement.setAttribute('lang', 'he')")
    page.wait_for_timeout(500)
    page.screenshot(path=os.path.join(SCREENSHOTS_DIR, "task15-tooltips-rtl.png"), full_page=True)
    print("\n=== HEBREW RTL VERIFICATION ===")
    verify_tooltips_and_captions(page, "rtl")

    browser.close()

print("\nDone. Screenshots saved to:", SCREENSHOTS_DIR)
