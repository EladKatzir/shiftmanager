"""Test Calendar Text Entry feature across Shifts, Chores, and OnCall pages."""
import sys
import os
from datetime import datetime, timedelta
from playwright.sync_api import sync_playwright

BASE = os.environ.get("E2E_BASE_URL", "http://localhost:5000")
E2E_EMAIL = os.environ.get("E2E_EMAIL", "test.manager@shifty.test")
E2E_PASSWORD = os.environ.get("E2E_PASSWORD", "TestManager123!")
SCREENSHOTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "test_screenshots")

# Future date for data entry (7 days from now)
FUTURE_DATE = (datetime.now() + timedelta(days=7)).strftime("%Y-%m-%d")

os.makedirs(SCREENSHOTS, exist_ok=True)

def screenshot(page, name):
    path = f"{SCREENSHOTS}/{name}.png"
    page.screenshot(path=path, full_page=False)
    print(f"  Screenshot: {path}")

def login(page):
    page.goto(f"{BASE}/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.fill('input[name="Email"]', E2E_EMAIL)
    page.fill('input[name="Password"]', E2E_PASSWORD)
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")
    print(f"Logged in as {E2E_EMAIL}")

def get_current_user_id(page):
    """Extract current user ID from the page."""
    uid = page.evaluate("""() => {
        // Try claims/meta or hidden fields
        var el = document.querySelector('[data-user-id]');
        if (el) return parseInt(el.dataset.userId);
        // Try from nav or sidebar
        var link = document.querySelector('a[href*="/My/Profile"]');
        // Fallback: check cookie or claim
        return null;
    }""")
    return uid

def test_api_validation(page):
    """Test 1: API endpoint validation."""
    print("\n=== TEST 1: API Validation ===")

    # Empty text
    r = page.evaluate(f"""async () => {{
        const resp = await fetch('/Api/Calendar/QuickAddTextEntry', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }},
            credentials: 'same-origin',
            body: JSON.stringify({{ date: '{FUTURE_DATE}', userId: 1, text: '' }})
        }});
        return {{ status: resp.status, body: await resp.json() }};
    }}""")
    status = "PASS" if r['status'] == 400 else "FAIL"
    print(f"  [{status}] Empty text rejected: {r['status']} {r['body'].get('message', '')}")

    # Too long
    r = page.evaluate(f"""async () => {{
        const resp = await fetch('/Api/Calendar/QuickAddTextEntry', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }},
            credentials: 'same-origin',
            body: JSON.stringify({{ date: '{FUTURE_DATE}', userId: 1, text: 'A'.repeat(201) }})
        }});
        return {{ status: resp.status, body: await resp.json() }};
    }}""")
    status = "PASS" if r['status'] == 400 else "FAIL"
    print(f"  [{status}] 201-char text rejected: {r['status']}")

    # Past date
    r = page.evaluate("""async () => {
        const resp = await fetch('/Api/Calendar/QuickAddTextEntry', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin',
            body: JSON.stringify({ date: '2020-01-01', userId: 1, text: 'Past' })
        });
        return { status: resp.status, body: await resp.json() };
    }""")
    status = "PASS" if r['status'] == 400 else "FAIL"
    print(f"  [{status}] Past date rejected: {r['status']}")

def test_create_delete_cycle(page):
    """Test 2: Create a text entry for current user, then delete it."""
    print("\n=== TEST 2: Create + Delete Cycle ===")

    # Find current user's ID from the Chores page (user rows)
    page.goto(f"{BASE}/Calendar/Chores")
    page.wait_for_load_state("networkidle")

    user_id = page.evaluate("""() => {
        var row = document.querySelector('tr[data-row-id^="user-"]');
        if (!row) return null;
        return parseInt(row.dataset.rowId.replace('user-', ''));
    }""")
    print(f"  First visible user ID: {user_id}")

    if not user_id:
        print("  SKIP: No user rows found")
        return None

    # Create entry
    r = page.evaluate(f"""async () => {{
        const resp = await fetch('/Api/Calendar/QuickAddTextEntry', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }},
            credentials: 'same-origin',
            body: JSON.stringify({{ date: '{FUTURE_DATE}', userId: {user_id}, text: 'E2E Test Holiday' }})
        }});
        return {{ status: resp.status, body: await resp.json() }};
    }}""")
    entry_id = r['body'].get('textEntryId')
    status = "PASS" if r['body'].get('success') else "FAIL"
    print(f"  [{status}] Created entry: id={entry_id}, status={r['status']}")

    if not entry_id:
        return None

    # Delete it
    r = page.evaluate(f"""async () => {{
        const resp = await fetch('/Api/Calendar/DeleteTextEntry', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }},
            credentials: 'same-origin',
            body: JSON.stringify({{ id: {entry_id} }})
        }});
        return {{ status: resp.status, body: await resp.json() }};
    }}""")
    status = "PASS" if r['body'].get('success') else "FAIL"
    print(f"  [{status}] Deleted entry {entry_id}: {r['status']}")

    # Delete non-existent
    r = page.evaluate("""async () => {
        const resp = await fetch('/Api/Calendar/DeleteTextEntry', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin',
            body: JSON.stringify({ id: 999999 })
        });
        return { status: resp.status, body: await resp.json() };
    }""")
    status = "PASS" if r['status'] == 404 else "FAIL"
    print(f"  [{status}] Non-existent delete returns 404: {r['status']}")

    return user_id

def test_cross_calendar_visibility(page, user_id):
    """Test 3: Create entry, verify it shows on both Shifts and Chores."""
    print("\n=== TEST 3: Cross-Calendar Visibility ===")

    # Create entry for tomorrow
    r = page.evaluate(f"""async () => {{
        const resp = await fetch('/Api/Calendar/QuickAddTextEntry', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }},
            credentials: 'same-origin',
            body: JSON.stringify({{ date: '{FUTURE_DATE}', userId: {user_id}, text: 'Cross-Cal Test' }})
        }});
        return {{ status: resp.status, body: await resp.json() }};
    }}""")
    entry_id = r['body'].get('textEntryId')
    print(f"  Created entry id={entry_id} for user {user_id} on {FUTURE_DATE}")

    if not entry_id:
        print("  SKIP: Could not create entry")
        return

    # Check Chores page
    page.goto(f"{BASE}/Calendar/Chores")
    page.wait_for_load_state("networkidle")
    chores_entries = page.locator('[data-role-color="text-entry"]')
    chores_count = chores_entries.count()
    status = "PASS" if chores_count > 0 else "FAIL"
    print(f"  [{status}] Text entries on Chores: {chores_count}")
    screenshot(page, "03_chores_with_entry")

    if chores_count > 0:
        # Check styling
        first = chores_entries.first
        has_dotted = page.evaluate("""(el) => {
            var style = window.getComputedStyle(el);
            return style.borderInlineStartStyle;
        }""", first.element_handle())
        print(f"  Border style: {has_dotted}")

        # Check data-entry-type
        entry_type = first.get_attribute("data-entry-type")
        status = "PASS" if entry_type == "text" else "FAIL"
        print(f"  [{status}] data-entry-type: '{entry_type}'")

        # Check × button exists
        remove_btn = first.locator('.excel-calendar__remove-btn')
        status = "PASS" if remove_btn.count() > 0 else "FAIL"
        print(f"  [{status}] Remove button present: {remove_btn.count() > 0}")

    # Check Shifts page (user mode)
    page.goto(f"{BASE}/Calendar/Shifts?Mode=user")
    page.wait_for_load_state("networkidle")
    shifts_entries = page.locator('[data-role-color="text-entry"]')
    shifts_count = shifts_entries.count()
    status = "PASS" if shifts_count > 0 else "FAIL"
    print(f"  [{status}] Text entries on Shifts (user mode): {shifts_count}")
    screenshot(page, "04_shifts_user_with_entry")

    # Check Shifts page (shift mode) — should see overlay badge
    page.goto(f"{BASE}/Calendar/Shifts")
    page.wait_for_load_state("networkidle")
    badges = page.locator('.excel-calendar__badge--text-entry')
    badge_count = badges.count()
    print(f"  Overlay badges on Shifts (shift mode): {badge_count}")
    screenshot(page, "05_shifts_shift_badges")

    # Clean up
    page.evaluate(f"""async () => {{
        await fetch('/Api/Calendar/DeleteTextEntry', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }},
            credentials: 'same-origin',
            body: JSON.stringify({{ id: {entry_id} }})
        }});
    }}""")
    print(f"  Cleaned up entry {entry_id}")

def test_quick_entry_shifts_user_mode(page):
    """Test 4: Quick Entry 'Save as text' on Shifts user mode."""
    print("\n=== TEST 4: Quick Entry on Shifts (User Mode) ===")

    page.goto(f"{BASE}/Calendar/Shifts?Mode=user")
    page.wait_for_load_state("networkidle")

    user_rows = page.locator('tr[data-row-id^="user-"]')
    print(f"  User rows: {user_rows.count()}")

    if user_rows.count() == 0:
        print("  SKIP: No user rows")
        return

    # Enable Quick Entry
    qe_btn = page.locator("#quickEntryToggle")
    if qe_btn.count() == 0:
        print("  SKIP: No Quick Entry toggle")
        return
    qe_btn.click()
    page.wait_for_timeout(300)

    # Click a future cell
    cells = page.locator('.excel-calendar__cell:not(.excel-calendar__cell--past):not(.excel-calendar__cell--readonly)')
    if cells.count() == 0:
        print("  SKIP: No editable cells")
        return

    cells.first.click()
    page.wait_for_timeout(300)

    qe_input = page.locator('.quick-entry-input')
    if qe_input.count() == 0:
        print("  SKIP: Input didn't appear")
        return

    # Type unmatched text
    qe_input.fill("Unique Holiday XYZ")
    page.wait_for_timeout(500)

    save_text = page.locator('.quick-entry-item--text-entry')
    status = "PASS" if save_text.count() > 0 else "FAIL"
    print(f"  [{status}] 'Save as text' option visible: {save_text.count() > 0}")
    screenshot(page, "06_save_as_text_shifts")

    if save_text.count() > 0:
        text = save_text.inner_text()
        print(f"  Option text: {text}")

    qe_input.press("Escape")

def test_quick_entry_chores(page):
    """Test 5: Quick Entry 'Save as text' on Chores."""
    print("\n=== TEST 5: Quick Entry on Chores ===")

    page.goto(f"{BASE}/Calendar/Chores")
    page.wait_for_load_state("networkidle")

    user_rows = page.locator('tr[data-row-id^="user-"]')
    print(f"  User rows: {user_rows.count()}")

    # Enable Quick Entry
    qe_btn = page.locator("#quickEntryToggle")
    if qe_btn.count() == 0:
        print("  SKIP: No Quick Entry toggle")
        return
    qe_btn.click()
    page.wait_for_timeout(300)

    cells = page.locator('.excel-calendar__cell:not(.excel-calendar__cell--past):not(.excel-calendar__cell--readonly)')
    if cells.count() == 0:
        print("  SKIP: No editable cells")
        return

    cells.first.click()
    page.wait_for_timeout(300)

    qe_input = page.locator('.quick-entry-input')
    if qe_input.count() == 0:
        print("  SKIP: Input didn't appear")
        return

    qe_input.fill("Chore Text Entry Test")
    page.wait_for_timeout(500)

    save_text = page.locator('.quick-entry-item--text-entry')
    status = "PASS" if save_text.count() > 0 else "FAIL"
    print(f"  [{status}] 'Save as text' option visible on Chores: {save_text.count() > 0}")
    screenshot(page, "07_save_as_text_chores")

    qe_input.press("Escape")

def test_oncall_no_text_entry(page):
    """Test 6: OnCall should NOT show Save as text."""
    print("\n=== TEST 6: OnCall No Text Entry Creation ===")

    page.goto(f"{BASE}/Calendar/OnCall")
    page.wait_for_load_state("networkidle")

    duty_rows = page.locator('tr[data-row-id^="dutytype-"]')
    print(f"  Duty type rows: {duty_rows.count()}")

    # Check for Quick Entry toggle
    qe_btn = page.locator("#quickEntryToggle")
    if qe_btn.count() == 0:
        print("  OnCall has no Quick Entry toggle (user lacks ManageOnDuty grant)")
        print("  [PASS] Text entry creation inherently blocked (no Quick Entry)")
        screenshot(page, "08_oncall_no_qe")
        return

    qe_btn.click()
    page.wait_for_timeout(300)

    cells = page.locator('.excel-calendar__cell:not(.excel-calendar__cell--past):not(.excel-calendar__cell--readonly)')
    if cells.count() == 0:
        print("  SKIP: No editable cells")
        return

    cells.first.click()
    page.wait_for_timeout(300)

    qe_input = page.locator('.quick-entry-input')
    if qe_input.count() == 0:
        print("  SKIP: Input didn't appear")
        return

    qe_input.fill("OnCall Text Test")
    page.wait_for_timeout(500)

    save_text = page.locator('.quick-entry-item--text-entry')
    status = "PASS" if save_text.count() == 0 else "FAIL"
    print(f"  [{status}] 'Save as text' NOT shown on OnCall: {save_text.count() == 0}")

    qe_input.press("Escape")

def test_js_functions_loaded(page):
    """Test 7: Verify JS functions on each calendar page."""
    print("\n=== TEST 7: JS Functions Availability ===")

    for cal_page, name in [("/Calendar/Shifts", "Shifts"), ("/Calendar/Chores", "Chores"), ("/Calendar/OnCall", "OnCall")]:
        page.goto(f"{BASE}{cal_page}")
        page.wait_for_load_state("networkidle")
        funcs = page.evaluate("""() => ({
            quickAddTextEntry: typeof window.quickAddTextEntry === 'function',
            deleteTextEntry: typeof window.deleteTextEntry === 'function',
            CalendarBottomSheet: typeof window.CalendarBottomSheet !== 'undefined'
        })""")
        all_ok = all(funcs.values())
        status = "PASS" if all_ok else "FAIL"
        print(f"  [{status}] {name}: {funcs}")

def test_bottom_sheet_entry_type(page, user_id):
    """Test 8: Create entry, verify bottom-sheet reads entryType."""
    print("\n=== TEST 8: Bottom Sheet entryType Field ===")

    # Create entry
    r = page.evaluate(f"""async () => {{
        const resp = await fetch('/Api/Calendar/QuickAddTextEntry', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }},
            credentials: 'same-origin',
            body: JSON.stringify({{ date: '{FUTURE_DATE}', userId: {user_id}, text: 'Bottom Sheet Test' }})
        }});
        return await resp.json();
    }}""")
    entry_id = r.get('textEntryId')
    if not entry_id:
        print("  SKIP: Could not create entry")
        return

    # Reload Chores to see the entry
    page.goto(f"{BASE}/Calendar/Chores")
    page.wait_for_load_state("networkidle")

    # Find cell with the text entry and test extractCellData
    result = page.evaluate("""() => {
        var textEl = document.querySelector('[data-entry-type="text"]');
        if (!textEl) return { found: false };

        var cell = textEl.closest('.excel-calendar__cell');
        if (!cell || !window.CalendarBottomSheet) return { found: true, canExtract: false };

        var data = window.CalendarBottomSheet.extractCellData(cell);
        var textAssignments = data.assignments.filter(a => a.entryType === 'text');
        return {
            found: true,
            canExtract: true,
            totalAssignments: data.assignments.length,
            textEntryAssignments: textAssignments.length,
            firstTextEntry: textAssignments.length > 0 ? textAssignments[0] : null
        };
    }""")
    if result.get('canExtract'):
        has_entry_type = result.get('textEntryAssignments', 0) > 0
        status = "PASS" if has_entry_type else "FAIL"
        print(f"  [{status}] extractCellData includes entryType='text': {has_entry_type}")
        print(f"  Total assignments in cell: {result.get('totalAssignments')}, text entries: {result.get('textEntryAssignments')}")
        if result.get('firstTextEntry'):
            print(f"  First text entry: {result['firstTextEntry']}")
    else:
        print(f"  Could not test: found={result.get('found')}, canExtract={result.get('canExtract')}")

    # Clean up
    page.evaluate(f"""async () => {{
        await fetch('/Api/Calendar/DeleteTextEntry', {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }},
            credentials: 'same-origin',
            body: JSON.stringify({{ id: {entry_id} }})
        }});
    }}""")

def main():
    print("=" * 60)
    print("Calendar Text Entry Feature - E2E Tests")
    print("=" * 60)

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1400, "height": 900})
        page = context.new_page()

        try:
            login(page)
            test_api_validation(page)
            user_id = test_create_delete_cycle(page)
            if user_id:
                test_cross_calendar_visibility(page, user_id)
                test_bottom_sheet_entry_type(page, user_id)
            test_quick_entry_shifts_user_mode(page)
            test_quick_entry_chores(page)
            test_oncall_no_text_entry(page)
            test_js_functions_loaded(page)
        except Exception as e:
            print(f"\nERROR: {e}")
            screenshot(page, "error_state")
            raise
        finally:
            browser.close()

    print("\n" + "=" * 60)
    print("Tests complete.")
    print("=" * 60)

if __name__ == "__main__":
    main()
