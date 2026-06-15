"""Find Tzafona company and emp.tz.alhut user in Admin/Users."""
from playwright.sync_api import sync_playwright
import os, time

ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
BASE = "http://localhost:5000"
os.makedirs(ARTIFACTS, exist_ok=True)

def login(page):
    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill("input[name='Email']", "owner2@test")
    page.fill("input[name='Password']", "Test1234!")
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    time.sleep(1)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width": 1400, "height": 900})

    login(page)
    page.goto(f"{BASE}/Admin/Users", wait_until="networkidle")
    time.sleep(2)

    # List all companies in the filter
    company_sel = page.locator("select[name='UserFilterCompanyId'], select[id='filterCompanyId']")
    print("Company filter options:")
    for opt in company_sel.first.locator("option").all():
        val = opt.get_attribute("value") or ""
        txt = opt.inner_text()
        print(f"  val={val!r}: {txt!r}")

    # Try filtering by search
    search = page.locator("input[placeholder='Search by name or email...']")
    if search.count() > 0:
        search.first.fill("emp.tz.alhut")
        time.sleep(2)  # Wait for live search/filter
        page.wait_for_load_state("networkidle")
        time.sleep(1)
        page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-search-alhut.png"), full_page=True)
        content = page.content()
        print(f"\nAfter search 'emp.tz.alhut': in page = {'alhut' in content}")
        rows = page.locator("tbody tr").all()
        print(f"Rows after search: {len(rows)}")
        for i, row in enumerate(rows[:5]):
            try:
                print(f"  Row {i}: {row.inner_text().replace(chr(10),' | ')[:200]}")
            except:
                pass

    # Now try the company filter options
    all_company_opts = []
    for sel in page.locator("select").all():
        name = sel.get_attribute("name") or ""
        if "company" in name.lower() or "Company" in name:
            for opt in sel.locator("option").all():
                val = opt.get_attribute("value") or ""
                txt = opt.inner_text().strip()
                if val and val not in [o[0] for o in all_company_opts]:
                    all_company_opts.append((val, txt))

    print(f"\nAll unique company options:")
    for v, t in all_company_opts:
        print(f"  {v}: {t}")

    browser.close()
    print("Done.")
