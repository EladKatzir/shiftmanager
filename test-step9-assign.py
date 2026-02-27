"""
Step 9 Final: Assign users to shift slots via the Calendar/Table API.

The program generated 5 shift instances under Camps (ID 3, Oren molecule).
Calendar/Table shows "Select Employee..." slots with assignment IDs.
The employee dropdown is empty because no users belong to Camps company.

Strategy:
1. Get assignment IDs from the Calendar/Table page
2. Try direct API assignment using users from Tzafona/City
3. If that fails (company filter), test what the employee dropdown looks like
   when a Tzafona user (AlhutLead) accesses the Table
"""
import sys, json, time, os, io
from playwright.sync_api import sync_playwright

if sys.platform == 'win32':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')

BASE = "http://localhost:5000"
SHOTS = "ProductionReady/step9-shifts"

def login(page, email, password="Test1234!"):
    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill('input#Email', email)
    page.fill('input#Password', password)
    page.click('button.auth-submit')
    page.wait_for_load_state("networkidle")
    ok = "/Auth/Login" not in page.url
    print(f"  {'OK' if ok else 'FAIL'}: {email} -> {page.url}")
    return ok

def logout(page):
    page.goto(f"{BASE}/Auth/Logout", wait_until="networkidle")

def shot(page, name):
    page.screenshot(path=f"{SHOTS}/{name}", full_page=True)
    print(f"  Screenshot: {name}")

def main():
    os.makedirs(SHOTS, exist_ok=True)

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        ctx = browser.new_context(viewport={"width": 1400, "height": 900})
        page = ctx.new_page()

        # ============================================================
        # STEP 1: Admin — Get assignment slot IDs from Calendar/Table
        # ============================================================
        print("\n=== STEP 1: Admin — Get assignment slot IDs ===")
        login(page, "admin@local", "admin123")
        page.goto(f"{BASE}/Calendar/Table?start=2026-03-02&view=week",
                   wait_until="networkidle")
        time.sleep(1)

        # Get all assignment slots with their IDs
        slots = page.evaluate("""() => {
            const allSlots = document.querySelectorAll('.assignment-slot');
            return Array.from(allSlots).map(s => ({
                assignmentId: s.getAttribute('data-assignment-id'),
                userId: s.getAttribute('data-user-id'),
                hasTrainee: s.getAttribute('data-has-trainee'),
                isUnassigned: !s.getAttribute('data-user-id') || s.getAttribute('data-user-id') === '',
                cellDate: s.closest('.assignment-cell')?.getAttribute('data-date'),
                text: s.textContent.trim().substring(0, 40)
            }));
        }""")
        print(f"  Assignment slots: {json.dumps(slots, indent=2, ensure_ascii=False)}")

        # Get employee dropdown contents
        employees_admin = page.evaluate("""() => {
            const dd = document.getElementById('employeeDropdown');
            if (!dd) return {error: 'no dropdown'};
            const opts = dd.querySelectorAll('.employee-option');
            return Array.from(opts).map(o => ({
                id: o.getAttribute('data-employee-id'),
                name: o.getAttribute('data-employee-name')
            }));
        }""")
        print(f"  Admin employees: {json.dumps(employees_admin, ensure_ascii=False)}")

        # Get CSRF token
        csrf = page.evaluate("() => document.querySelector('input[name=\"__RequestVerificationToken\"]')?.value")
        print(f"  CSRF token: {'found' if csrf else 'NOT FOUND'}")

        # ============================================================
        # STEP 2: Try direct API assignment as admin
        # Pick the first unassigned slot and try to assign a Tzafona user
        # ============================================================
        print("\n=== STEP 2: Try direct API assignment ===")

        unassigned = [s for s in slots if s.get('isUnassigned') and s.get('assignmentId')]
        if unassigned:
            target_slot = unassigned[0]
            print(f"  Target slot: assignment ID {target_slot['assignmentId']}, date {target_slot['cellDate']}")

            # Try assigning user ID 6 (alhut1.tz@local — an Alhut soldier in Tzafona)
            # We need to find the actual user ID first
            # Let's query the page for a list of known users
            result = page.evaluate(f"""async () => {{
                const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const headers = {{
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                }};
                if (csrfToken) headers['RequestVerificationToken'] = csrfToken;

                try {{
                    const response = await fetch('/Calendar/Table?handler=AssignUserToSlot', {{
                        method: 'POST',
                        headers: headers,
                        credentials: 'same-origin',
                        body: JSON.stringify({{
                            assignmentId: parseInt('{target_slot["assignmentId"]}'),
                            userId: 6
                        }})
                    }});

                    const text = await response.text();
                    let data;
                    try {{ data = JSON.parse(text); }} catch {{ data = {{ rawText: text.substring(0, 300) }}; }}
                    return {{ status: response.status, ok: response.ok, data: data }};
                }} catch (e) {{
                    return {{ error: e.message }};
                }}
            }}""")
            print(f"  Assignment API result: {json.dumps(result, indent=2, ensure_ascii=False)}")

            if result.get('data', {}).get('success'):
                print("  ✅ Assignment succeeded!")
                page.reload()
                page.wait_for_load_state("networkidle")
                time.sleep(1)
                shot(page, "20-assignment-success-admin.png")
            else:
                print(f"  Assignment failed: {result.get('data', {}).get('error', 'unknown')}")
        else:
            print("  No unassigned slots found")

        shot(page, "20-admin-table-state.png")

        # ============================================================
        # STEP 3: Check Calendar/Table as AlhutLead (Tzafona)
        # AlhutLead is in Tzafona — Table is company-scoped
        # Camps' shift instances won't show for Tzafona user
        # ============================================================
        print("\n=== STEP 3: Calendar/Table as AlhutLead (Tzafona) ===")
        logout(page)
        login(page, "alhutlead.tz@local")

        page.goto(f"{BASE}/Calendar/Table?start=2026-03-02&view=week",
                   wait_until="networkidle")
        time.sleep(1)

        tz_table = page.evaluate("""() => {
            const table = document.querySelector('table');
            if (!table) return {noTable: true, bodySnippet: document.body.innerText.substring(0, 300)};
            const rows = table.querySelectorAll('tbody tr');
            return {
                totalRows: rows.length,
                firstRowLabel: rows[0]?.querySelector('td')?.textContent?.trim()?.substring(0, 50),
                hasSlots: document.querySelectorAll('.assignment-slot').length,
                hasEmployeeDropdown: document.querySelectorAll('#employeeDropdown .employee-option').length
            };
        }""")
        print(f"  AlhutLead Table: {json.dumps(tz_table, ensure_ascii=False)}")
        shot(page, "21-alhutlead-table.png")

        # Get employees visible to AlhutLead
        tz_employees = page.evaluate("""() => {
            const dd = document.getElementById('employeeDropdown');
            if (!dd) return [];
            return Array.from(dd.querySelectorAll('.employee-option')).map(o => ({
                id: o.getAttribute('data-employee-id'),
                name: o.getAttribute('data-employee-name')
            }));
        }""")
        print(f"  AlhutLead employees: {json.dumps(tz_employees, indent=2, ensure_ascii=False)}")

        # ============================================================
        # STEP 4: If admin assignment worked, verify on Shifts page
        # ============================================================
        print("\n=== STEP 4: Verify assignments on Shifts page ===")
        logout(page)
        login(page, "admin@local", "admin123")

        page.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Start=2026-03-02&ViewMode=week&Mode=shift",
                   wait_until="networkidle")
        time.sleep(1)
        shot(page, "22-shifts-after-assignment.png")

        # Check "By User" mode for assignment marks
        page.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Start=2026-03-02&ViewMode=week&Mode=user",
                   wait_until="networkidle")
        time.sleep(1)

        user_shifts = page.evaluate("""() => {
            const table = document.querySelector('table');
            if (!table) return {error: 'no table'};
            const rows = [];
            table.querySelectorAll('tbody tr').forEach((tr, i) => {
                const label = tr.querySelector('td:first-child')?.textContent?.trim() || '';
                const cells = Array.from(tr.querySelectorAll('td'));
                let filledCount = 0;
                cells.forEach((td, ci) => {
                    if (ci > 0 && td.innerHTML.trim().length > 20) filledCount++;
                });
                if (filledCount > 0) {
                    rows.push({label: label.substring(0, 40), filled: filledCount});
                }
            });
            return {usersWithShifts: rows};
        }""")
        print(f"  Users with shifts: {json.dumps(user_shifts, indent=2, ensure_ascii=False)}")
        shot(page, "23-shifts-by-user-assigned.png")

        # ============================================================
        # STEP 5: Back to Table as admin — try assigning via UI click
        # ============================================================
        print("\n=== STEP 5: Admin Table — try UI assignment ===")
        page.goto(f"{BASE}/Calendar/Table?start=2026-03-02&view=week",
                   wait_until="networkidle")
        time.sleep(1)

        # Click the first "Select Employee..." slot
        click_result = page.evaluate("""() => {
            const unassigned = document.querySelectorAll('.assignment-slot:not([data-user-id])');
            const selects = document.querySelectorAll('.assignment-slot');

            // Find unassigned slots
            let target = null;
            for (const slot of selects) {
                const userId = slot.getAttribute('data-user-id');
                if (!userId || userId === '' || userId === 'null') {
                    target = slot;
                    break;
                }
            }

            if (!target) return {error: 'no unassigned slots', totalSlots: selects.length};

            // Click the slot to trigger handleUnassignedSlotClick
            target.click();

            return {
                clicked: true,
                assignmentId: target.getAttribute('data-assignment-id'),
                slotText: target.textContent.trim().substring(0, 40)
            };
        }""")
        print(f"  Click result: {json.dumps(click_result, ensure_ascii=False)}")
        time.sleep(0.5)

        # Check if employee dropdown appeared
        dropdown_state = page.evaluate("""() => {
            const dd = document.getElementById('employeeDropdown');
            if (!dd) return {error: 'no dropdown'};
            const style = window.getComputedStyle(dd);
            const hasShow = dd.classList.contains('show');
            const items = dd.querySelectorAll('.employee-option');
            const visibleItems = Array.from(items).filter(i => {
                const s = window.getComputedStyle(i);
                return s.display !== 'none';
            });
            return {
                hasShowClass: hasShow,
                display: style.display,
                totalItems: items.length,
                visibleItems: visibleItems.length,
                itemNames: visibleItems.slice(0, 10).map(i => i.getAttribute('data-employee-name'))
            };
        }""")
        print(f"  Dropdown state: {json.dumps(dropdown_state, indent=2, ensure_ascii=False)}")
        shot(page, "24-after-slot-click.png")

        # ============================================================
        # STEP 6: Summary of shift instance status
        # ============================================================
        print("\n=== STEP 6: Final state summary ===")
        page.goto(f"{BASE}/Calendar/Table?start=2026-03-02&view=week",
                   wait_until="networkidle")
        time.sleep(1)

        final_state = page.evaluate("""() => {
            const slots = document.querySelectorAll('.assignment-slot');
            let assigned = 0, unassigned = 0;
            const assignedNames = [];
            slots.forEach(s => {
                const uid = s.getAttribute('data-user-id');
                if (uid && uid !== '' && uid !== 'null') {
                    assigned++;
                    const name = s.querySelector('.primary-user')?.textContent?.trim() ||
                                 s.textContent?.trim()?.substring(0, 30);
                    assignedNames.push(name);
                } else {
                    unassigned++;
                }
            });
            return {
                totalSlots: slots.length,
                assigned: assigned,
                unassigned: unassigned,
                assignedNames: assignedNames
            };
        }""")
        print(f"  Final state: {json.dumps(final_state, indent=2, ensure_ascii=False)}")
        shot(page, "25-final-table-state.png")

        print("\n=== ALL DONE ===")
        browser.close()
        return 0

if __name__ == "__main__":
    sys.exit(main())
