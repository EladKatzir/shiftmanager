"""Find Tzafona company and navigate to find target users."""
import os, time, sys
os.environ["PYTHONIOENCODING"] = "utf-8"
sys.stdout.reconfigure(encoding='utf-8')

from playwright.sync_api import sync_playwright

ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
BASE = "http://localhost:5000"
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
    page = browser.new_page(viewport={"width": 1400, "height": 900})

    login(page)
    page.goto(f"{BASE}/Admin/Users", wait_until="networkidle")
    time.sleep(2)

    # Get all company options
    company_sel = page.locator("select[name='UserFilterCompanyId']")
    opts = company_sel.first.locator("option").all()
    companies = []
    for opt in opts:
        val = opt.get_attribute("value") or ""
        txt = opt.text_content() or ""
        companies.append((val, txt.strip()))
        print(f"Company: val={val!r} text={txt.strip()!r}")

    # Search for emp.tz.alhut using the search box
    search = page.locator("input[placeholder='Search by name or email...']")
    print(f"\nSearch boxes: {search.count()}")
    if search.count() > 0:
        search.first.fill("emp.tz.alhut")
        # Wait for dynamic filtering (likely JS-powered)
        time.sleep(2)
        page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-search-result.png"), full_page=True)

        content = page.content()
        found = "alhut" in content.lower() and "emp.tz" in content
        print(f"Found after search: {found}")

        rows = page.locator("tbody tr").all()
        visible_rows = []
        for row in rows:
            try:
                if row.is_visible():
                    txt = row.inner_text().replace('\n', ' | ').strip()
                    visible_rows.append(txt)
            except:
                pass
        print(f"Visible rows after search: {len(visible_rows)}")
        for r in visible_rows[:10]:
            print(f"  {r[:200]}")

    # Try iterating through companies to find Tzafona users
    print("\nTrying each company to find Tzafona users...")
    for val, name in companies[1:]:  # skip empty/All
        if not val:
            continue
        # Select company
        company_sel.first.select_option(val)
        time.sleep(1.5)
        page.wait_for_load_state("networkidle")
        content = page.content()
        if "emp.tz.alhut" in content:
            print(f"Found Tzafona users in company val={val!r} name={name!r}")
            rows = page.locator("tbody tr").all()
            print(f"  Total rows: {len(rows)}")
            for row in rows[:5]:
                try:
                    print(f"  Row: {row.inner_text().replace(chr(10),' | ')[:200]}")
                except:
                    pass
            break

    browser.close()
