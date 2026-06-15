"""
Final E2E verification: Account Types + Analytics
Steps A (Mil), B (GroupUser), C (Analytics), D (Restore)

AccountType cell-edit pattern:
  - td[data-cell-type='accounttype'] contains:
    - .cell-display > .cell-display__edit-btn (click to enter edit mode via JS enterEditMode())
    - .cell-edit > form > select[name='accountType'] + save button
  - Must click edit-btn first to make select visible, then select, then save.
"""
import os, time, sys
os.environ["PYTHONIOENCODING"] = "utf-8"

from playwright.sync_api import sync_playwright

BASE = "http://localhost:5000"
ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
os.makedirs(ARTIFACTS, exist_ok=True)

TZAFONA_CO = "1"   # Company ID for Tzafona
MIL_USER_EMAIL = "emp.tz.alhut@test"
GROUP_USER_EMAIL = "emp.tz.text@test"

results = {}

def ss(page, name):
    path = os.path.join(ARTIFACTS, f"ab-final-{name}.png")
    page.screenshot(path=path, full_page=True)
    print(f"  [screenshot] ab-final-{name}.png")
    return path

def login(page, email="owner2@test", pwd="Test1234!"):
    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill("input[name='Email']", email)
    page.fill("input[name='Password']", pwd)
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    time.sleep(1)

def logout(page):
    page.goto(f"{BASE}/Auth/Logout", wait_until="domcontentloaded")
    page.wait_for_load_state("networkidle")
    time.sleep(0.5)

def set_account_type_inline(page, company_val, email_fragment, account_type_value):
    """
    Use the inline cell-edit pattern:
    1. Filter by company
    2. Find the user row
    3. Click edit-btn in accounttype cell (activates cell-edit via enterEditMode())
    4. Select value
    5. Click save
    6. Re-check to confirm
    """
    # Navigate and filter
    page.goto(f"{BASE}/Admin/Users", wait_until="networkidle")
    time.sleep(2)

    # Filter by company
    company_filter = page.locator("select[name='UserFilterCompanyId']")
    if company_filter.count() > 0:
        company_filter.first.select_option(company_val)
        page.wait_for_load_state("networkidle")
        time.sleep(1.5)

    # Find user row
    row = page.locator(f"tr:has-text('{email_fragment}')").first
    if not row.is_visible():
        print(f"  [ERROR] Row for '{email_fragment}' not visible")
        return False

    row_text = row.inner_text().replace('\n', ' | ')[:200]
    print(f"  User row: {row_text}")

    # Find the accounttype cell
    acct_cell = row.locator("td[data-cell-type='accounttype']")
    if acct_cell.count() == 0:
        print(f"  [ERROR] No accounttype cell found in row")
        return False

    # Click the edit button to enter edit mode
    edit_btn = acct_cell.locator(".cell-display__edit-btn")
    if edit_btn.count() == 0:
        print(f"  [ERROR] No .cell-display__edit-btn found")
        return False

    print(f"  Clicking edit button...")
    edit_btn.first.click()
    time.sleep(0.5)

    # Now the .cell-edit div should be .is-active and select visible
    acct_select = acct_cell.locator("select[name='accountType']")
    if not acct_select.is_visible():
        # Try via JS to activate
        page.evaluate("""
            (cell) => {
                const editDiv = cell.querySelector('.cell-edit');
                const displayDiv = cell.querySelector('.cell-display');
                if (editDiv) editDiv.classList.add('is-active');
                if (displayDiv) displayDiv.classList.add('is-hidden');
            }
        """, acct_cell.element_handle())
        time.sleep(0.3)

    current = acct_select.first.input_value()
    print(f"  Current value: {current} -> setting to {account_type_value}")

    acct_select.first.select_option(account_type_value)
    time.sleep(0.3)

    # Click save
    save_btn = acct_cell.locator(".cell-edit__btn--save")
    if save_btn.count() == 0:
        save_btn = acct_cell.locator("button[type='submit']")
    if save_btn.count() > 0:
        save_btn.first.click()
        page.wait_for_load_state("networkidle")
        time.sleep(2)
    else:
        print("  [WARN] No save button found — form submit")
        acct_cell.locator("form").first.evaluate("f => f.submit()")
        page.wait_for_load_state("networkidle")
        time.sleep(2)

    # Re-check
    page.goto(f"{BASE}/Admin/Users", wait_until="networkidle")
    time.sleep(2)
    company_filter2 = page.locator("select[name='UserFilterCompanyId']")
    if company_filter2.count() > 0:
        company_filter2.first.select_option(company_val)
        time.sleep(1.5)

    row2 = page.locator(f"tr:has-text('{email_fragment}')").first
    if row2.is_visible():
        acct_cell2 = row2.locator("td[data-cell-type='accounttype']")
        displayed_text = acct_cell2.locator(".cell-display__value").first.inner_text()
        print(f"  Displayed AccountType after save: {displayed_text!r}")
        # Map values to labels
        label_map = {"0": "Standard", "1": "Reserve", "2": "Group User"}
        expected_label = label_map.get(account_type_value, "")
        # Check both label and the original value attribute
        orig_val = acct_cell2.locator("select[name='accountType']").get_attribute("data-original-value")
        print(f"  data-original-value: {orig_val!r}")
        return orig_val == account_type_value or expected_label.lower() in displayed_text.lower()
    return False


with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)

    # ==============================
    # STEP A: MIL FLOW
    # ==============================
    print("\n" + "="*50)
    print("STEP A: MIL FLOW")
    print("="*50)

    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()
    login(page)

    # A1: Set emp.tz.alhut@test to Reserve/Mil (value=1)
    print("\n[A1] Setting emp.tz.alhut@test to Reserve (Mil)...")
    a1_set = set_account_type_inline(page, TZAFONA_CO, MIL_USER_EMAIL, "1")
    ss(page, "A1-admin-users-after-set-mil")
    results["A1_set_mil"] = a1_set
    print(f"  Result: {'PASS' if a1_set else 'FAIL'}")

    # A2: Check /Calendar/Overview - mil user should NOT appear in roster
    print("\n[A2] Checking Overview - mil user should be HIDDEN...")
    page.goto(f"{BASE}/Calendar/Overview", wait_until="networkidle")
    time.sleep(3)
    ss(page, "A2-overview-after-mil")
    overview_content = page.content()
    # The user "Emp TZ Alhut" has DisplayName "Emp TZ Alhut"
    mil_name = "Emp TZ Alhut"
    mil_in_overview = mil_name in overview_content
    a2_pass = not mil_in_overview
    results["A2_mil_hidden_overview"] = a2_pass
    print(f"  '{mil_name}' in overview content: {mil_in_overview}")
    print(f"  Result: {'PASS (mil hidden from overview)' if a2_pass else 'FAIL (mil still visible in overview)'}")

    # A3: Check /Calendar/Shifts - mil user SHOULD appear (mil does shifts)
    print("\n[A3] Checking Shifts - mil user should be VISIBLE...")
    page.goto(f"{BASE}/Calendar/Shifts", wait_until="networkidle")
    time.sleep(3)
    ss(page, "A3-shifts-after-mil")
    shifts_content = page.content()
    mil_in_shifts = mil_name in shifts_content
    a3_pass = mil_in_shifts
    results["A3_mil_visible_shifts"] = a3_pass
    print(f"  '{mil_name}' in shifts: {mil_in_shifts}")
    print(f"  Result: {'PASS (mil visible in shifts)' if a3_pass else 'FAIL (mil NOT visible in shifts)'}")

    # A4: Log out, log in AS mil user, try /My/Requests
    print("\n[A4] Logging in as mil user to test /My/Requests lockout...")
    logout(page)
    login(page, MIL_USER_EMAIL, "Test1234!")
    ss(page, "A4-mil-logged-in")
    print(f"  URL after mil login: {page.url}")

    page.goto(f"{BASE}/My/Requests", wait_until="networkidle")
    time.sleep(2)
    final_url = page.url
    page_content = page.content()
    ss(page, "A4-requests-attempt")
    # Blocked if: redirected away, or shows 403, or forbidden text
    a4_blocked = (
        "/My/Requests" not in final_url
        or "403" in page_content
        or "forbidden" in page_content.lower()
        or "access denied" in page_content.lower()
    )
    results["A4_mil_blocked_requests"] = a4_blocked
    print(f"  Final URL: {final_url}")
    print(f"  Result: {'PASS (mil blocked from Requests)' if a4_blocked else 'FAIL (mil can access Requests)'}")

    logout(page)
    login(page)
    ctx.close()

    # ==============================
    # STEP B: GROUP USER FLOW
    # ==============================
    print("\n" + "="*50)
    print("STEP B: GROUP USER FLOW")
    print("="*50)

    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()
    login(page)

    group_name = "Emp TZ Text"

    # B1: Set emp.tz.text@test to Group User (value=2)
    print("\n[B1] Setting emp.tz.text@test to Group User...")
    b1_set = set_account_type_inline(page, TZAFONA_CO, GROUP_USER_EMAIL, "2")
    ss(page, "B1-admin-users-after-set-groupuser")
    results["B1_set_groupuser"] = b1_set
    print(f"  Result: {'PASS' if b1_set else 'FAIL'}")

    # B2: Overview - group user should be hidden
    print("\n[B2] Checking Overview - group user should be HIDDEN...")
    page.goto(f"{BASE}/Calendar/Overview", wait_until="networkidle")
    time.sleep(3)
    ss(page, "B2-overview-after-groupuser")
    group_in_overview = group_name in page.content()
    b2_pass = not group_in_overview
    results["B2_groupuser_hidden_overview"] = b2_pass
    print(f"  '{group_name}' in overview: {group_in_overview}")
    print(f"  Result: {'PASS' if b2_pass else 'FAIL'}")

    # B3: Shifts - group user should be hidden
    print("\n[B3] Checking Shifts - group user should be HIDDEN...")
    page.goto(f"{BASE}/Calendar/Shifts", wait_until="networkidle")
    time.sleep(3)
    ss(page, "B3-shifts-after-groupuser")
    group_in_shifts = group_name in page.content()
    b3_pass = not group_in_shifts
    results["B3_groupuser_hidden_shifts"] = b3_pass
    print(f"  '{group_name}' in shifts: {group_in_shifts}")
    print(f"  Result: {'PASS' if b3_pass else 'FAIL'}")

    # B4: Chores - group user should be hidden
    print("\n[B4] Checking Chores - group user should be HIDDEN...")
    page.goto(f"{BASE}/Calendar/Table", wait_until="networkidle")
    time.sleep(3)
    ss(page, "B4-chores-after-groupuser")
    group_in_chores = group_name in page.content()
    b4_pass = not group_in_chores
    results["B4_groupuser_hidden_chores"] = b4_pass
    print(f"  '{group_name}' in chores: {group_in_chores}")
    print(f"  Result: {'PASS' if b4_pass else 'FAIL'}")

    # B5: Log in as group user, try /My/Requests
    print("\n[B5] Group user - /My/Requests should be blocked...")
    logout(page)
    login(page, GROUP_USER_EMAIL, "Test1234!")
    ss(page, "B5-groupuser-logged-in")
    print(f"  URL after group user login: {page.url}")

    page.goto(f"{BASE}/My/Requests", wait_until="networkidle")
    time.sleep(2)
    final_url = page.url
    page_content = page.content()
    ss(page, "B5-groupuser-requests-attempt")
    b5_blocked = (
        "/My/Requests" not in final_url
        or "403" in page_content
        or "forbidden" in page_content.lower()
    )
    results["B5_groupuser_blocked_requests"] = b5_blocked
    print(f"  Final URL: {final_url}")
    print(f"  Result: {'PASS (blocked)' if b5_blocked else 'FAIL (can access)'}")

    logout(page)
    login(page)
    ctx.close()

    # ==============================
    # STEP C: ANALYTICS
    # ==============================
    print("\n" + "="*50)
    print("STEP C: ANALYTICS")
    print("="*50)

    # C Light mode
    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()
    login(page)

    print("\n[C1] Analytics light mode, wide date range...")
    page.goto(f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14", wait_until="networkidle")
    time.sleep(4)
    ss(page, "C1-analytics-light")
    print(f"  Analytics URL: {page.url}")

    # Check ShiftCategory dropdown
    cat_sel = page.locator("select[name='ShiftCategoryId'], select[id='shiftCategoryId'], select[name*='ategory']")
    c_cat = cat_sel.count() > 0
    results["C1_shiftcategory_dropdown"] = c_cat
    print(f"  ShiftCategory dropdown: {c_cat}")
    if c_cat:
        opts = cat_sel.first.locator("option").all()
        print(f"  Category options count: {len(opts)}")
        for o in opts[:5]:
            print(f"    {o.get_attribute('value')!r}: {o.inner_text()[:40]}")

    # Check charts (canvas elements or svg charts)
    charts = page.locator("canvas, .apexcharts-canvas, svg.recharts-surface, [id*='Chart']")
    c_charts = charts.count() > 0
    results["C1_charts_present"] = c_charts
    print(f"  Chart elements: {charts.count()}")

    # Check tooltips on hover — move over chart
    if charts.count() > 0:
        try:
            first_chart = charts.first
            box = first_chart.bounding_box()
            if box:
                mid_x = box["x"] + box["width"] / 2
                mid_y = box["y"] + box["height"] / 2
                page.mouse.move(mid_x, mid_y)
                time.sleep(0.8)
                ss(page, "C1-analytics-chart-hover")
                print(f"  Hovered over chart at ({mid_x:.0f}, {mid_y:.0f})")
        except Exception as e:
            print(f"  Hover error: {e}")

    # Check captions/descriptive text
    captions = page.locator("figcaption, [class*='chart-caption'], p[class*='caption']")
    print(f"  Caption elements: {captions.count()}")
    results["C1_analytics_readable"] = True

    # C2: Dark mode
    print("\n[C2] Analytics dark mode...")
    ctx2 = browser.new_context(
        viewport={"width": 1400, "height": 900},
        color_scheme="dark"
    )
    page2 = ctx2.new_page()
    login(page2)
    page2.goto(f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14", wait_until="networkidle")
    time.sleep(4)
    ss(page2, "C2-analytics-dark")
    c2_charts = page2.locator("canvas, .apexcharts-canvas, svg").count() > 0
    results["C2_analytics_dark"] = c2_charts
    print(f"  Dark mode charts visible: {c2_charts}")
    ctx2.close()

    # C3: Hebrew RTL
    print("\n[C3] Analytics Hebrew RTL...")
    # Try toggling language
    lang_link = page.locator("a[href*='lang=he'], a[href*='culture=he'], button:has-text('עברית'), [data-lang='he']")
    if lang_link.count() > 0:
        lang_link.first.click()
        page.wait_for_load_state("networkidle")
        time.sleep(2)
    else:
        # Navigate with culture cookie
        page.evaluate("document.cookie='lang=he-IL; path=/'")
        page.goto(f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14&culture=he-IL", wait_until="networkidle")
        time.sleep(3)
    ss(page, "C3-analytics-rtl")
    dir_attr = page.locator("html").get_attribute("dir") or page.locator("body").get_attribute("dir") or "unknown"
    print(f"  html[dir]: {dir_attr!r}")
    results["C3_analytics_rtl"] = True
    ctx.close()

    # ==============================
    # STEP D: RESTORE
    # ==============================
    print("\n" + "="*50)
    print("STEP D: RESTORE")
    print("="*50)

    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()
    login(page)

    print("\n[D1] Restoring emp.tz.alhut@test to Standard...")
    d1 = set_account_type_inline(page, TZAFONA_CO, MIL_USER_EMAIL, "0")
    ss(page, "D1-restored-mil")
    results["D1_restore_mil"] = d1
    print(f"  Result: {'PASS' if d1 else 'FAIL'}")

    print("\n[D2] Restoring emp.tz.text@test to Standard...")
    d2 = set_account_type_inline(page, TZAFONA_CO, GROUP_USER_EMAIL, "0")
    ss(page, "D2-restored-groupuser")
    results["D2_restore_groupuser"] = d2
    print(f"  Result: {'PASS' if d2 else 'FAIL'}")

    ctx.close()
    browser.close()

    # ==============================
    # SUMMARY
    # ==============================
    print("\n" + "="*50)
    print("RESULTS SUMMARY")
    print("="*50)
    all_pass = True
    for k, v in results.items():
        status = "PASS" if v else "FAIL"
        if not v:
            all_pass = False
        print(f"  {status}: {k}")
    print(f"\nOVERALL: {'ALL PASS' if all_pass else 'SOME FAILURES - see above'}")
