"""
Step 9 Continuation: Generate instances from the already-created program,
then check Calendar/Shifts and test assignment via Calendar/Table.

The program "City Morning Sun-Thu" already exists under Camps (ID 3, Oren molecule).
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
        # STEP 1: Login as admin, go to Programs
        # ============================================================
        print("\n=== STEP 1: Login + verify program exists ===")
        login(page, "admin@local", "admin123")
        page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")
        time.sleep(0.5)

        # Check existing programs
        programs = page.evaluate("""() => {
            const cards = document.querySelectorAll('.program-card');
            return Array.from(cards).map(c => {
                const genBtn = c.querySelector('.generate-btn');
                return {
                    name: c.querySelector('.program-header h3')?.textContent?.trim(),
                    badge: c.querySelector('.badge')?.textContent?.trim(),
                    programId: genBtn?.dataset?.programId
                };
            });
        }""")
        print(f"  Programs found: {json.dumps(programs, ensure_ascii=False)}")

        if not programs:
            print("  ERROR: No programs exist!")
            browser.close()
            return 1

        program_id = programs[0].get('programId')
        print(f"  Using program ID: {program_id}")

        # ============================================================
        # STEP 2: Generate instances via JS (bypass modal visibility)
        # The form in the modal POSTs to ?handler=GenerateInstances
        # with: GenerateProgramId, GenerateStartDate, GenerateEndDate
        # ============================================================
        print("\n=== STEP 2: Generate instances (Mar 2-8, 2026) ===")

        # Use JavaScript to fill the hidden form values and submit directly
        gen_result = page.evaluate(f"""() => {{
            // Set the hidden program ID
            const pidInput = document.getElementById('generateProgramId');
            if (!pidInput) return {{error: 'no program ID input'}};
            pidInput.value = '{program_id}';

            // Set start date
            const startInput = document.querySelector('#GenerateStartDate, input[name="GenerateStartDate"]');
            if (!startInput) return {{error: 'no start date input'}};
            startInput.value = '2026-03-02';

            // Set end date
            const endInput = document.querySelector('#GenerateEndDate, input[name="GenerateEndDate"]');
            if (!endInput) return {{error: 'no end date input'}};
            endInput.value = '2026-03-08';

            // Find and submit the generate form
            const form = document.querySelector('#generateModal form');
            if (!form) return {{error: 'no generate form'}};

            form.submit();
            return {{ok: true, programId: pidInput.value, start: startInput.value, end: endInput.value}};
        }}""")
        print(f"  Generate form result: {json.dumps(gen_result, ensure_ascii=False)}")

        page.wait_for_load_state("networkidle")
        time.sleep(1)

        print(f"  After generate, URL: {page.url}")

        # Navigate back to Programs if redirected
        if "/Programs" not in page.url:
            page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")
            time.sleep(0.5)

        # Check for success/error
        alerts = page.evaluate("""() => {
            const success = document.querySelector('.alert-success');
            const error = document.querySelector('.alert-danger');
            return {
                success: success?.textContent?.trim() || null,
                error: error?.textContent?.trim() || null
            };
        }""")
        print(f"  Alerts: {json.dumps(alerts, ensure_ascii=False)}")
        shot(page, "10-after-generate.png")

        # ============================================================
        # STEP 3: Check Calendar/Shifts for generated instances
        # Camps is in Oren molecule (ID 1), Alhut JobType (ID 1)
        # ============================================================
        print("\n=== STEP 3: Check Calendar/Shifts for instances ===")

        # First as admin — Shifts page is molecule-scoped
        page.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Start=2026-03-02&ViewMode=week&Mode=shift",
                   wait_until="networkidle")
        time.sleep(1)

        # Analyze the shift table
        shift_info = page.evaluate("""() => {
            const table = document.querySelector('table');
            if (!table) return {error: 'no table'};

            const headers = Array.from(table.querySelectorAll('thead th'))
                .map(th => th.textContent.trim().substring(0, 30));

            const rows = [];
            table.querySelectorAll('tbody tr').forEach((tr, i) => {
                const cells = Array.from(tr.querySelectorAll('td'));
                const label = cells[0]?.textContent?.trim() || '';
                const cellData = cells.slice(1).map((td, ci) => {
                    const html = td.innerHTML.trim();
                    const hasContent = html.length > 20;
                    const hasSvg = html.includes('svg');
                    const hasPlus = html.includes('+');
                    const assignNames = Array.from(td.querySelectorAll('.assignment-name, [class*="assign"]'))
                        .map(el => el.textContent.trim());
                    return {
                        col: ci + 1,
                        hasContent,
                        hasSvg,
                        hasPlus,
                        assignments: assignNames,
                        preview: html.substring(0, 80)
                    };
                });
                rows.push({
                    row: i,
                    label: label.substring(0, 60),
                    cells: cellData.filter(c => c.hasContent)
                });
            });

            return {headers, totalRows: rows.length, rows};
        }""")
        print(f"  Shift table: {json.dumps(shift_info, indent=2, ensure_ascii=False)}")
        shot(page, "11-shifts-calendar-admin.png")

        # ============================================================
        # STEP 4: Check as AlhutLead (Tzafona)
        # ============================================================
        print("\n=== STEP 4: Check Calendar/Shifts as AlhutLead ===")
        logout(page)
        login(page, "alhutlead.tz@local")

        page.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Start=2026-03-02&ViewMode=week&Mode=shift",
                   wait_until="networkidle")
        time.sleep(1)
        shot(page, "12-shifts-alhutlead.png")

        # Also check "By User" mode
        page.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Start=2026-03-02&ViewMode=week&Mode=user",
                   wait_until="networkidle")
        time.sleep(1)

        user_mode_info = page.evaluate("""() => {
            const table = document.querySelector('table');
            if (!table) return {error: 'no table'};
            const rows = [];
            table.querySelectorAll('tbody tr').forEach((tr, i) => {
                const label = tr.querySelector('td:first-child')?.textContent?.trim() || '';
                if (label) rows.push({row: i, label: label.substring(0, 50)});
            });
            return {totalRows: rows.length, rows};
        }""")
        print(f"  By User mode: {json.dumps(user_mode_info, indent=2, ensure_ascii=False)}")
        shot(page, "13-shifts-user-mode.png")

        # ============================================================
        # STEP 5: Try assignment via Calendar/Table
        # Calendar/Table is company-scoped — AlhutLead.tz is in Tzafona
        # But the program was created under Camps (ID 3)
        # So we need to use someone in Camps, or the admin with Camps context
        # ============================================================
        print("\n=== STEP 5: Assignment via Calendar/Table ===")
        logout(page)
        login(page, "admin@local", "admin123")

        # Navigate to Calendar/Table (admin's company is Camps)
        page.goto(f"{BASE}/Calendar/Table?start=2026-03-02&view=week",
                   wait_until="networkidle")
        time.sleep(1)

        # Check what we see
        table_state = page.evaluate("""() => {
            const table = document.querySelector('table');
            if (!table) return {noTable: true, bodyText: document.body.innerText.substring(0, 500)};

            const headers = Array.from(table.querySelectorAll('thead th'))
                .map(th => th.textContent.trim().substring(0, 30));

            const rows = [];
            table.querySelectorAll('tbody tr').forEach((tr, i) => {
                const cells = Array.from(tr.querySelectorAll('td'));
                const label = cells[0]?.textContent?.trim() || '';
                let buttonCount = 0;
                let svgCount = 0;
                let assignmentCount = 0;
                cells.forEach(td => {
                    buttonCount += td.querySelectorAll('button').length;
                    svgCount += td.querySelectorAll('svg').length;
                    assignmentCount += td.querySelectorAll('[data-assignment-id], .shift-assignment').length;
                });
                rows.push({
                    row: i,
                    label: label.substring(0, 60),
                    cellCount: cells.length,
                    buttons: buttonCount,
                    svgs: svgCount,
                    assignments: assignmentCount
                });
            });

            return {headers, totalRows: rows.length, rows: rows.slice(0, 15)};
        }""")
        print(f"  Calendar/Table state: {json.dumps(table_state, indent=2, ensure_ascii=False)}")
        shot(page, "14-calendar-table-admin.png")

        # Check employee dropdown
        emp_dropdown = page.evaluate("""() => {
            const dd = document.getElementById('employeeDropdown');
            if (!dd) return {error: 'no employeeDropdown'};
            const items = dd.querySelectorAll('[data-user-id]');
            return {
                itemCount: items.length,
                items: Array.from(items).slice(0, 15).map(el => ({
                    userId: el.dataset.userId,
                    name: el.textContent.trim().substring(0, 40)
                }))
            };
        }""")
        print(f"  Employee dropdown: {json.dumps(emp_dropdown, indent=2, ensure_ascii=False)}")

        # Try clicking a + button or add button in a shift cell
        add_result = page.evaluate("""() => {
            // Look for add-assignment buttons, + circles, or clickable cells
            const addBtns = document.querySelectorAll(
                'td .add-assignment, td button[onclick*="assign"], td svg, ' +
                'td [class*="add"], td [class*="plus"], td button.btn-sm'
            );

            if (addBtns.length === 0) {
                // Check for any buttons or interactive elements in the table body
                const allBtns = document.querySelectorAll('table tbody button, table tbody a.btn');
                return {
                    addButtons: 0,
                    allTableButtons: allBtns.length,
                    buttonTexts: Array.from(allBtns).slice(0, 10).map(b => ({
                        text: b.textContent.trim().substring(0, 30),
                        class: b.className.substring(0, 40),
                        onclick: b.getAttribute('onclick')?.substring(0, 60)
                    }))
                };
            }

            return {
                addButtons: addBtns.length,
                first: {
                    tag: addBtns[0].tagName,
                    class: addBtns[0].className?.substring(0, 50),
                    parent: addBtns[0].parentElement?.tagName,
                    onclick: addBtns[0].getAttribute('onclick')?.substring(0, 60)
                }
            };
        }""")
        print(f"  Add buttons: {json.dumps(add_result, indent=2, ensure_ascii=False)}")

        # If we found shift cells with + buttons, try clicking one
        if add_result.get('addButtons', 0) > 0 or add_result.get('allTableButtons', 0) > 0:
            # Click the first interactive element
            clicked = page.evaluate("""() => {
                const btn = document.querySelector(
                    'table tbody button, table tbody svg, table tbody [onclick]'
                );
                if (btn) {
                    btn.click();
                    return {clicked: true, tag: btn.tagName, text: btn.textContent?.trim()?.substring(0, 30)};
                }
                return {clicked: false};
            }""")
            print(f"  Click result: {json.dumps(clicked)}")
            time.sleep(1)
            shot(page, "15-after-click.png")

            # Check for modal or dropdown after click
            post_click = page.evaluate("""() => {
                return {
                    visibleModals: document.querySelectorAll('.modal[style*="block"], .modal.show').length,
                    dropdowns: document.querySelectorAll('.dropdown-menu.show, .dropdown.show').length,
                    employeeDropdownVisible: (() => {
                        const dd = document.getElementById('employeeDropdown');
                        if (!dd) return false;
                        const style = window.getComputedStyle(dd);
                        return style.display !== 'none' && style.visibility !== 'hidden';
                    })()
                };
            }""")
            print(f"  Post-click UI state: {json.dumps(post_click)}")

        # ============================================================
        # STEP 6: Direct API test — try assigning via POST
        # Calendar/Table uses POST handlers like OnPostAssignEmployeeAsync
        # ============================================================
        print("\n=== STEP 6: Check if direct assignment is possible ===")

        # Get the anti-forgery token
        token = page.evaluate("""() => {
            const input = document.querySelector('input[name="__RequestVerificationToken"]');
            return input?.value || null;
        }""")
        print(f"  Anti-forgery token: {'found' if token else 'NOT FOUND'}")

        # Check shift instances in the table
        instances = page.evaluate("""() => {
            // Look for data attributes on cells that identify shift instances
            const cells = document.querySelectorAll('td[data-instance-id], td[data-shift-id], [data-instance-id]');
            if (cells.length > 0) {
                return Array.from(cells).slice(0, 5).map(c => ({
                    instanceId: c.dataset.instanceId || c.dataset.shiftId,
                    date: c.dataset.date
                }));
            }

            // Fallback: scan the full page HTML for instance references
            const html = document.body.innerHTML;
            const instanceMatches = html.match(/data-instance-id="(\d+)"/g);
            const shiftIdMatches = html.match(/data-shift-instance-id="(\d+)"/g);
            return {
                instanceAttrCount: instanceMatches?.length || 0,
                shiftIdAttrCount: shiftIdMatches?.length || 0,
                sampleHtml: document.querySelector('table tbody td:nth-child(2)')?.innerHTML?.substring(0, 300)
            };
        }""")
        print(f"  Shift instances: {json.dumps(instances, indent=2, ensure_ascii=False)}")

        # ============================================================
        # FINAL: Summary screenshot
        # ============================================================
        print("\n=== FINAL: Summary ===")
        shot(page, "16-final-summary.png")

        # Quick check: how many shift instances exist in the database?
        # Navigate to diagnostic or use the Shifts page data
        page.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Start=2026-03-02&ViewMode=week&Mode=shift",
                   wait_until="networkidle")
        time.sleep(1)

        # Count visible shift rows
        final_count = page.evaluate("""() => {
            const table = document.querySelector('table');
            if (!table) return {error: 'no table'};
            const rows = table.querySelectorAll('tbody tr');
            let filledRows = 0;
            rows.forEach(tr => {
                const cells = tr.querySelectorAll('td');
                cells.forEach((td, i) => {
                    if (i > 0 && td.innerHTML.trim().length > 30) filledRows++;
                });
            });
            return {totalRows: rows.length, filledCellCount: filledRows};
        }""")
        print(f"  Final shift count: {json.dumps(final_count)}")
        shot(page, "17-final-shifts-view.png")

        print("\n=== DONE ===")
        browser.close()
        return 0

if __name__ == "__main__":
    sys.exit(main())
