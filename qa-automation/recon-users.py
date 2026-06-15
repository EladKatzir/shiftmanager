"""Recon Admin/Users page to find AccountType editor and user rows."""
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
    print(f"Current URL after login: {page.url}")
    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-post-login.png"), full_page=True)

    # Navigate to Admin/Users
    page.goto(f"{BASE}/Admin/Users", wait_until="networkidle")
    time.sleep(2)
    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-admin-users.png"), full_page=True)
    print(f"Admin/Users URL: {page.url}")

    # List selects
    selects = page.locator("select").all()
    print(f"\nSelects: {len(selects)}")
    for sel in selects:
        try:
            name = sel.get_attribute("name") or ""
            id_ = sel.get_attribute("id") or ""
            opts = [o.get_attribute("value") + "=" + o.inner_text() for o in sel.locator("option").all()[:5]]
            print(f"  select name={name!r} id={id_!r}: {opts}")
        except:
            pass

    # List all inputs
    inputs = page.locator("input").all()
    print(f"\nInputs: {len(inputs)}")
    for inp in inputs:
        try:
            name = inp.get_attribute("name") or ""
            type_ = inp.get_attribute("type") or ""
            placeholder = inp.get_attribute("placeholder") or ""
            if type_ not in ("hidden",):
                print(f"  input: name={name!r}, type={type_!r}, ph={placeholder!r}")
        except:
            pass

    # List rows in any table
    rows = page.locator("tbody tr, .user-item, [class*='user-row']").all()
    print(f"\nTable rows: {len(rows)}")
    for i, row in enumerate(rows[:8]):
        try:
            txt = row.inner_text().replace("\n", " | ").strip()
            print(f"  Row {i}: {txt[:200]}")
        except:
            pass

    # Check if emp.tz.alhut@test is in page
    content = page.content()
    print(f"\nemp.tz.alhut in page: {'emp.tz.alhut' in content}")
    print(f"emp.tz.text in page: {'emp.tz.text' in content}")

    # Check for AccountType badges/selects
    acct_els = page.locator("[data-account-type], [class*='account'], select[id*='AccountType'], select[name*='AccountType']").all()
    print(f"\nAccountType elements: {len(acct_els)}")
    for el in acct_els[:5]:
        try:
            tag = el.evaluate("e => e.tagName")
            cls = el.get_attribute("class") or ""
            dat = el.get_attribute("data-account-type") or ""
            print(f"  {tag} class={cls!r} data-account-type={dat!r}")
        except:
            pass

    browser.close()
    print("\nRecon done.")
