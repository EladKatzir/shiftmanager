"""
Final verification sweep for Account Types + Analytics feature.
Steps A (Mil flow), B (GroupUser flow), C (Analytics), D (Restore).
"""
import time
import os
from playwright.sync_api import sync_playwright, expect

BASE_URL = "http://localhost:5000"
OWNER = "owner2@test"
PWD = "Test1234!"
ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"

os.makedirs(ARTIFACTS, exist_ok=True)

def login(page, email, password):
    page.goto(f"{BASE_URL}/Auth/Login", wait_until="networkidle")
    page.fill("input[name='Input.Email']", email)
    page.fill("input[name='Input.Password']", password)
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    time.sleep(1)

def logout(page):
    # Try clicking logout link/button
    try:
        page.goto(f"{BASE_URL}/Auth/Logout", wait_until="networkidle")
    except:
        pass
    time.sleep(0.5)

def screenshot(page, name):
    path = os.path.join(ARTIFACTS, f"ab-final-{name}.png")
    page.screenshot(path=path, full_page=True)
    print(f"  [screenshot] {path}")
    return path

def find_user_and_set_account_type(page, email_fragment, account_type_value):
    """Navigate to Admin/Users, find a user by email fragment, set their AccountType."""
    page.goto(f"{BASE_URL}/Admin/Users", wait_until="networkidle")
    time.sleep(1)

    # Search for user
    search_input = page.locator("input[type='search'], input[placeholder*='חיפוש'], input[placeholder*='search'], input[name*='search'], input[name*='Search'], #search")
    if search_input.count() == 0:
        search_input = page.locator("input").first

    # Try to find a search box
    all_inputs = page.locator("input[type='text'], input[type='search'], input:not([type='hidden'])").all()
    print(f"  Found {len(all_inputs)} inputs on Admin/Users")

    # Look for filter/search
    page.screenshot(path=os.path.join(ARTIFACTS, f"ab-final-users-page-{email_fragment.replace('@','_')}.png"), full_page=True)

    # Try to find user row by text
    user_row = page.locator(f"tr:has-text('{email_fragment}'), .user-row:has-text('{email_fragment}'), [data-email*='{email_fragment}']")
    if user_row.count() == 0:
        print(f"  [INFO] User '{email_fragment}' not directly visible, checking pagination/filter")
        # Try search field if present
        search = page.locator("input[name='q'], input[name='filter'], input[name='search'], input[placeholder*='@']")
        if search.count() > 0:
            search.first.fill(email_fragment)
            search.first.press("Enter")
            page.wait_for_load_state("networkidle")
            time.sleep(1)
            user_row = page.locator(f"tr:has-text('{email_fragment}')")

    if user_row.count() == 0:
        print(f"  [WARN] Could not find user row for '{email_fragment}' - dumping visible user content")
        content = page.locator("table, .user-list, [class*='user']").first.inner_text()
        print(f"  First 500 chars: {content[:500]}")
        return False

    # Find the AccountType editor for this user — inline select/button
    account_type_select = user_row.first.locator("select[name*='AccountType'], select[name*='accountType'], select[name*='account']")
    if account_type_select.count() > 0:
        account_type_select.first.select_option(account_type_value)
        time.sleep(0.5)
        # Submit if there's a save button in the row
        save_btn = user_row.first.locator("button[type='submit'], button:has-text('שמור'), button:has-text('Save')")
        if save_btn.count() > 0:
            save_btn.first.click()
            page.wait_for_load_state("networkidle")
            time.sleep(1)
        print(f"  [OK] Set {email_fragment} AccountType to {account_type_value} via select")
        return True

    # Maybe it's a button that opens a modal/form
    edit_btn = user_row.first.locator("button[data-account-type], [data-action*='account'], button:has-text('סוג'), button[title*='סוג']")
    if edit_btn.count() > 0:
        edit_btn.first.click()
        time.sleep(0.5)
        # Modal might open
        modal_select = page.locator(".modal select[name*='AccountType'], dialog select, [role='dialog'] select")
        if modal_select.count() > 0:
            modal_select.first.select_option(account_type_value)
            page.locator(".modal button[type='submit'], dialog button[type='submit']").first.click()
            page.wait_for_load_state("networkidle")
            time.sleep(1)
        print(f"  [OK] Set {email_fragment} AccountType to {account_type_value} via modal")
        return True

    print(f"  [WARN] No AccountType editor found in row for '{email_fragment}'")
    print(f"  Row HTML (first 500): {user_row.first.inner_html()[:500]}")
    return False


def deep_find_and_set_account_type(page, email_fragment, account_type_value):
    """More thorough approach: search, find inline badge/select, set account type."""
    page.goto(f"{BASE_URL}/Admin/Users", wait_until="networkidle")
    time.sleep(1.5)
    screenshot(page, f"users-before-{account_type_value}-{email_fragment.split('.')[1]}")

    page_text = page.content()
    if email_fragment not in page_text:
        print(f"  '{email_fragment}' not found in page, looking for search")
        # Look for filter inputs
        for inp in page.locator("input").all():
            try:
                placeholder = inp.get_attribute("placeholder") or ""
                name = inp.get_attribute("name") or ""
                type_ = inp.get_attribute("type") or ""
                if type_ not in ("hidden", "checkbox", "radio", "submit"):
                    print(f"    Input: name={name}, placeholder={placeholder}, type={type_}")
            except:
                pass
        # Try typing in first visible text input
        visible_inputs = page.locator("input[type='text'], input[type='search'], input:not([type])").all()
        for inp in visible_inputs:
            try:
                if inp.is_visible():
                    inp.fill(email_fragment)
                    inp.press("Enter")
                    page.wait_for_load_state("networkidle")
                    time.sleep(1.5)
                    break
            except:
                pass

    # Now look for the user row
    rows = page.locator(f"tr:has-text('{email_fragment}')").all()
    if not rows:
        # Try broader match - maybe the email contains some portion
        email_local = email_fragment.split("@")[0]
        rows = page.locator(f"tr:has-text('{email_local}')").all()

    if not rows:
        print(f"  [ERROR] Cannot find any row matching '{email_fragment}'")
        screenshot(page, f"users-missing-{email_fragment.replace('@','_')}")
        return False

    row = rows[0]
    print(f"  Found row: {row.inner_text()[:200]}")

    # Look for inline account type badge/button that triggers editing
    # The feature built inline editing - look for spans/badges with account type data
    inline_editor = row.locator("[data-account-type], [data-bs-toggle='dropdown'][class*='badge'], select[name*='ccount']")
    if inline_editor.count() == 0:
        # Look for any button or select in the row
        inline_editor = row.locator("select, button:not([class*='delete']):not([class*='danger'])")

    if inline_editor.count() > 0:
        elem = inline_editor.first
        tag = elem.evaluate("el => el.tagName")
        print(f"  Found editor element: {tag}")
        if tag == "SELECT":
            elem.select_option(account_type_value)
            time.sleep(0.3)
            # Look for save button nearby
            save = row.locator("button[type='submit']")
            if save.count() > 0:
                save.first.click()
                page.wait_for_load_state("networkidle")
                time.sleep(1)
        else:
            elem.click()
            time.sleep(0.5)
            screenshot(page, f"dropdown-open-{account_type_value}")
            # Look for dropdown items
            option = page.locator(f"[data-value='{account_type_value}'], option[value='{account_type_value}'], li:has-text('{account_type_value}'), a:has-text('{account_type_value}')")
            if option.count() > 0:
                option.first.click()
                page.wait_for_load_state("networkidle")
                time.sleep(1)
            else:
                # Look for option by numeric value
                val_map = {"Standard": "0", "Mil": "1", "GroupUser": "2"}
                numeric = val_map.get(account_type_value, account_type_value)
                option2 = page.locator(f"[data-value='{numeric}'], li[data-account-type='{numeric}']")
                if option2.count() > 0:
                    option2.first.click()
                    page.wait_for_load_state("networkidle")
                    time.sleep(1)
        return True

    print(f"  [WARN] No editor found. Row HTML (500): {row.inner_html()[:500]}")
    return False


with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()

    results = {}

    # =====================
    # STEP A: MIL FLOW
    # =====================
    print("\n=== STEP A: MIL FLOW ===")
    login(page, OWNER, PWD)
    screenshot(page, "owner-home")

    # Navigate to Admin/Users to understand the page structure
    page.goto(f"{BASE_URL}/Admin/Users", wait_until="networkidle")
    time.sleep(2)
    screenshot(page, "admin-users-initial")

    # Get the page HTML to understand the structure
    html_snippet = page.content()

    # Look for any molecule/company filter first
    print("  Looking at Admin/Users page structure...")

    # Check if there are company/molecule filters
    filters = page.locator("select[name*='company'], select[name*='molecule'], select[name*='Company'], select[name*='Molecule']").all()
    for f in filters:
        try:
            name = f.get_attribute("name")
            print(f"  Filter select: {name}")
        except:
            pass

    # Let's look at what users are visible
    rows = page.locator("tbody tr").all()
    print(f"  Found {len(rows)} user rows")
    for i, row in enumerate(rows[:5]):
        try:
            print(f"  Row {i}: {row.inner_text()[:150].strip()}")
        except:
            pass

    # Try to find emp.tz.alhut@test
    MIL_USER = "emp.tz.alhut@test"
    GROUP_USER = "emp.tz.text@test"

    # Try searching for the user - look for search/filter inputs
    search_inputs = page.locator("input[type='text'], input[type='search'], input:not([type='hidden']):not([type='checkbox'])").all()
    print(f"  Visible text inputs: {len(search_inputs)}")

    mil_found_in_page = MIL_USER in page.content()
    group_found_in_page = GROUP_USER in page.content()
    print(f"  {MIL_USER} in page: {mil_found_in_page}")
    print(f"  {GROUP_USER} in page: {group_found_in_page}")

    # If not found, maybe we need to filter by company
    if not mil_found_in_page:
        # Look for company selector
        company_sel = page.locator("select").all()
        for sel in company_sel:
            opts = sel.locator("option").all()
            for opt in opts:
                try:
                    text = opt.inner_text()
                    val = opt.get_attribute("value")
                    if "tzafona" in text.lower() or "צפונה" in text:
                        print(f"  Found Tzafona option: val={val}, text={text}")
                        sel.select_option(val)
                        page.wait_for_load_state("networkidle")
                        time.sleep(1.5)
                        screenshot(page, "admin-users-tzafona-filter")
                        break
                except:
                    pass

    mil_found_in_page = MIL_USER in page.content()
    group_found_in_page = GROUP_USER in page.content()
    print(f"  After filter - {MIL_USER} in page: {mil_found_in_page}")
    print(f"  After filter - {GROUP_USER} in page: {group_found_in_page}")
    screenshot(page, "admin-users-after-filter")

    # Look at the actual structure around account type
    if mil_found_in_page:
        row = page.locator(f"tr:has-text('{MIL_USER}')").first
        row_html = row.inner_html()
        print(f"  MIL user row HTML (800 chars): {row_html[:800]}")

    # Try to find account type UI elements
    acct_elements = page.locator("[data-account-type], [class*='account-type'], [id*='account-type'], select[id*='AccountType']").all()
    print(f"  AccountType elements found: {len(acct_elements)}")
    for el in acct_elements[:3]:
        try:
            tag = el.evaluate("e => e.tagName")
            cls = el.get_attribute("class") or ""
            txt = el.inner_text()[:80]
            print(f"    {tag}.{cls}: {txt}")
        except:
            pass

    screenshot(page, "admin-users-structure-inspect")

    browser.close()

print("\n=== RECON COMPLETE - Now running full E2E ===")
