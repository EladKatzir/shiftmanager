"""
Step 9 Extended: Create shifts via Programs under City (Oren molecule),
generate instances, then assign users from both Tzafona and City.

Flow:
1. Login as admin → navigate to Programs page
2. Use OwnerCompanySelector to switch to City (ID 4) — this sets a cookie
3. Navigate BACK to Programs — now ShiftTypes for City are loaded
4. Create a "City Morning Sun-Thu" program
5. Generate shift instances for 2026-03-02 to 2026-03-08
6. Login as AlhutLead → check Calendar/Shifts for generated instances
7. Try assignment via Calendar/Table
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
        # STEP 1: Login as admin
        # ============================================================
        print("\n=== STEP 1: Login as admin ===")
        login(page, "admin@local", "admin123")

        # ============================================================
        # STEP 2: Navigate to Programs page first (to see current state)
        # ============================================================
        print("\n=== STEP 2: Go to Programs page ===")
        page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")
        time.sleep(0.5)
        shot(page, "01-programs-before-switch.png")

        # Check current company selector state
        selector_info = page.evaluate("""() => {
            const sel = document.querySelector('select[name="companyId"]');
            if (!sel) return {error: 'no company selector'};
            const opts = Array.from(sel.options).map(o => ({
                value: o.value, text: o.textContent.trim(), selected: o.selected
            }));
            return {
                currentValue: sel.value,
                currentText: sel.options[sel.selectedIndex]?.textContent?.trim(),
                optionCount: opts.length,
                options: opts.slice(0, 10)
            };
        }""")
        print(f"  Company selector: {json.dumps(selector_info, ensure_ascii=False)}")

        # Check if any ShiftTypes are loaded currently
        shift_types_count = page.evaluate("""() => {
            const sel = document.querySelector('select[name="ShiftTypeId"]');
            if (!sel) return 0;
            return Array.from(sel.options).filter(o => o.value !== '').length;
        }""")
        print(f"  Current ShiftTypes count: {shift_types_count}")

        # ============================================================
        # STEP 3: Switch to City (ID 4) via the OwnerCompanySelector form
        # The form POSTs to /Owner/SelectCompany with anti-forgery token
        # We need to add returnUrl to redirect back to Programs
        # ============================================================
        print("\n=== STEP 3: Switch to City (ID 4) ===")

        # Find City option value in the dropdown
        city_value = page.evaluate("""() => {
            const sel = document.querySelector('select[name="companyId"]');
            if (!sel) return null;
            for (const opt of sel.options) {
                // City is in Hebrew: העיר or might just be "City"
                if (opt.value === '4' || opt.textContent.includes('City') || opt.textContent.includes('העיר')) {
                    return {value: opt.value, text: opt.textContent.trim()};
                }
            }
            return null;
        }""")
        print(f"  City option: {json.dumps(city_value, ensure_ascii=False)}")

        if not city_value:
            print("  ERROR: Could not find City (ID 4) in company selector")
            # List all options for debugging
            all_opts = page.evaluate("""() => {
                const sel = document.querySelector('select[name="companyId"]');
                if (!sel) return [];
                return Array.from(sel.options).map(o => ({value: o.value, text: o.textContent.trim()}));
            }""")
            print(f"  All options: {json.dumps(all_opts, ensure_ascii=False)}")
            browser.close()
            return 1

        # Modify the form to add returnUrl and select City, then submit
        page.evaluate(f"""() => {{
            const form = document.querySelector('form[action="/Owner/SelectCompany"]');
            if (!form) return false;

            // Set the select value to City
            const sel = form.querySelector('select[name="companyId"]');
            sel.value = '{city_value["value"]}';

            // Add returnUrl hidden input so it redirects back to Programs
            const hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = 'returnUrl';
            hidden.value = '/Owner/Programs';
            form.appendChild(hidden);

            // Submit the form
            form.submit();
            return true;
        }}""")

        page.wait_for_load_state("networkidle")
        time.sleep(1)

        # Check where we landed
        print(f"  After switch, URL: {page.url}")

        # If we're on the Hub, navigate to Programs
        if "/Programs" not in page.url:
            print("  Redirected to Hub, navigating back to Programs...")
            page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")
            time.sleep(0.5)

        shot(page, "02-programs-after-switch.png")

        # Verify City is now selected and ShiftTypes are loaded
        switch_result = page.evaluate("""() => {
            const companySel = document.querySelector('select[name="companyId"]');
            const stSel = document.querySelector('select[name="ShiftTypeId"]');
            const companyText = companySel?.options[companySel.selectedIndex]?.textContent?.trim();
            const companyVal = companySel?.value;
            const stOpts = stSel ? Array.from(stSel.options).filter(o => o.value !== '').map(o => ({
                value: o.value, text: o.textContent.trim()
            })) : [];
            return {
                selectedCompany: companyText,
                companyValue: companyVal,
                shiftTypeCount: stOpts.length,
                shiftTypes: stOpts
            };
        }""")
        print(f"  After switch: {json.dumps(switch_result, ensure_ascii=False)}")

        if switch_result.get('shiftTypeCount', 0) == 0:
            print("  ERROR: No ShiftTypes after switching to City")
            shot(page, "02-ERROR-no-shift-types.png")
            browser.close()
            return 1

        # ============================================================
        # STEP 4: Create a "City Morning Sun-Thu" Program
        # ============================================================
        print("\n=== STEP 4: Create Morning Program ===")

        # Pick first shift type (should be Morning)
        first_st = switch_result['shiftTypes'][0]
        print(f"  Using ShiftType: {first_st['text']} (ID: {first_st['value']})")

        # Fill the form
        # 1. Select ShiftType
        page.select_option('select[name="ShiftTypeId"]', first_st['value'])

        # 2. Fill program name
        page.fill('input[name="ProgramName"]', 'City Morning Sun-Thu')

        # 3. Check Sun-Thu days (Sunday=0, Monday=1, Tue=2, Wed=3, Thu=4)
        day_checkboxes = page.locator('input[name="SelectedDays"]')
        total_days = day_checkboxes.count()
        print(f"  Day checkboxes: {total_days}")

        # Check the values of each checkbox to pick the right days
        day_values = page.evaluate("""() => {
            const cbs = document.querySelectorAll('input[name="SelectedDays"]');
            return Array.from(cbs).map(cb => ({value: cb.value, label: cb.closest('label')?.textContent?.trim()}));
        }""")
        print(f"  Day values: {json.dumps(day_values, ensure_ascii=False)}")

        # Check Sun through Thu
        for dv in day_values:
            val = dv['value']
            # DayOfWeek enum: Sunday=0, Monday=1, Tuesday=2, Wednesday=3, Thursday=4
            if val in ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday']:
                page.check(f'input[name="SelectedDays"][value="{val}"]')
                print(f"    Checked: {val}")

        # 4. Set staffing to 2
        page.fill('input[name="DefaultStaffing"]', '2')

        shot(page, "03-program-form-filled.png")

        # 5. Submit — need to trigger collectPerDayStaffing() then submit
        page.evaluate("""() => {
            // Call the JS function to collect per-day staffing
            if (typeof collectPerDayStaffing === 'function') collectPerDayStaffing();

            // Submit the create form
            const form = document.querySelector('form[action*="CreateProgram"]');
            if (form) {
                form.submit();
                return true;
            }
            // Fallback: find the form with the CreateProgram handler
            const forms = document.querySelectorAll('form');
            for (const f of forms) {
                const action = f.action || '';
                const btn = f.querySelector('button[type="submit"]');
                if (btn && btn.textContent.includes('Create')) {
                    f.submit();
                    return true;
                }
            }
            return false;
        }""")

        page.wait_for_load_state("networkidle")
        time.sleep(1)

        print(f"  After create, URL: {page.url}")

        # If redirected away from Programs, go back
        if "/Programs" not in page.url:
            page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")
            time.sleep(0.5)

        shot(page, "04-program-created.png")

        # Check for success/error alerts
        alerts = page.evaluate("""() => {
            const success = document.querySelector('.alert-success');
            const error = document.querySelector('.alert-danger');
            return {
                success: success?.textContent?.trim() || null,
                error: error?.textContent?.trim() || null
            };
        }""")
        print(f"  Alerts: {json.dumps(alerts, ensure_ascii=False)}")

        # Check for programs in the list
        programs = page.evaluate("""() => {
            const cards = document.querySelectorAll('.program-card');
            return Array.from(cards).map(c => ({
                name: c.querySelector('.program-header h3')?.textContent?.trim(),
                badge: c.querySelector('.badge')?.textContent?.trim()
            }));
        }""")
        print(f"  Programs: {json.dumps(programs, ensure_ascii=False)}")

        if not programs:
            print("  ERROR: No programs created!")

            # Check if we need to look for the Create button differently
            # The form uses asp-page-handler="CreateProgram"
            # Let's try submitting via the button click
            page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")
            time.sleep(0.5)

            # Re-fill the form
            page.select_option('select[name="ShiftTypeId"]', first_st['value'])
            page.fill('input[name="ProgramName"]', 'City Morning Sun-Thu')

            for dv in day_values:
                val = dv['value']
                if val in ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday']:
                    page.check(f'input[name="SelectedDays"][value="{val}"]')

            page.fill('input[name="DefaultStaffing"]', '2')

            # Click the Create button directly
            create_btn = page.locator('button:has-text("Create")')
            if create_btn.count() > 0:
                create_btn.first.click()
                page.wait_for_load_state("networkidle")
                time.sleep(1)
                print(f"  Retry URL: {page.url}")

                if "/Programs" not in page.url:
                    page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")

                alerts2 = page.evaluate("""() => {
                    const success = document.querySelector('.alert-success');
                    const error = document.querySelector('.alert-danger');
                    return {
                        success: success?.textContent?.trim() || null,
                        error: error?.textContent?.trim() || null
                    };
                }""")
                print(f"  Retry alerts: {json.dumps(alerts2, ensure_ascii=False)}")

                programs2 = page.evaluate("""() => {
                    const cards = document.querySelectorAll('.program-card');
                    return Array.from(cards).map(c => ({
                        name: c.querySelector('.program-header h3')?.textContent?.trim(),
                        badge: c.querySelector('.badge')?.textContent?.trim()
                    }));
                }""")
                print(f"  Retry programs: {json.dumps(programs2, ensure_ascii=False)}")
                programs = programs2

            shot(page, "04-retry-program.png")

        if not programs:
            print("  FATAL: Could not create program")
            browser.close()
            return 1

        # ============================================================
        # STEP 5: Generate instances for March 2-8, 2026
        # ============================================================
        print("\n=== STEP 5: Generate instances ===")

        # Find the Generate button for our program
        gen_btns = page.locator('.generate-btn')
        print(f"  Generate buttons: {gen_btns.count()}")

        if gen_btns.count() > 0:
            # Click the Generate button to open the modal
            gen_btns.first.click()
            time.sleep(0.5)

            # Check if modal is visible
            modal_visible = page.evaluate("""() => {
                const modal = document.getElementById('generateModal');
                return modal ? modal.style.display : 'not found';
            }""")
            print(f"  Modal display: {modal_visible}")

            shot(page, "05-generate-modal.png")

            # Fill dates
            page.fill('input[name="GenerateStartDate"]', '2026-03-02')
            page.fill('input[name="GenerateEndDate"]', '2026-03-08')

            shot(page, "05-generate-dates-filled.png")

            # Submit the generate form
            modal_submit = page.locator('#generateModal button[type="submit"]')
            if modal_submit.count() > 0:
                modal_submit.click()
                page.wait_for_load_state("networkidle")
                time.sleep(1)
            else:
                print("  No submit button in modal, submitting form directly")
                page.evaluate("""() => {
                    const form = document.querySelector('#generateModal form');
                    if (form) form.submit();
                }""")
                page.wait_for_load_state("networkidle")
                time.sleep(1)

            print(f"  After generate, URL: {page.url}")

            if "/Programs" not in page.url:
                page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")

            alerts3 = page.evaluate("""() => {
                const success = document.querySelector('.alert-success');
                const error = document.querySelector('.alert-danger');
                return {
                    success: success?.textContent?.trim() || null,
                    error: error?.textContent?.trim() || null
                };
            }""")
            print(f"  Generate alerts: {json.dumps(alerts3, ensure_ascii=False)}")
            shot(page, "06-after-generate.png")
        else:
            print("  No generate buttons found!")
            shot(page, "05-no-generate.png")

        # ============================================================
        # STEP 6: Check Calendar/Shifts as AlhutLead for instances
        # ============================================================
        print("\n=== STEP 6: Check shifts as AlhutLead ===")
        logout(page)
        login(page, "alhutlead.tz@local")

        # Navigate to Calendar/Shifts for Oren/Alhut, week of Mar 2
        page.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Start=2026-03-02&ViewMode=week&Mode=shift",
                   wait_until="networkidle")
        time.sleep(1)
        shot(page, "07-shifts-after-generate.png")

        # Count shift instances visible
        shift_analysis = page.evaluate("""() => {
            const table = document.querySelector('table');
            if (!table) return {error: 'no table'};
            const rows = table.querySelectorAll('tbody tr');
            const rowData = [];
            rows.forEach((tr, i) => {
                const label = tr.querySelector('td:first-child')?.textContent?.trim() || '';
                const cells = tr.querySelectorAll('td');
                let filledCount = 0;
                cells.forEach((td, ci) => {
                    if (ci > 0) {
                        const html = td.innerHTML.trim();
                        if (html.length > 10) filledCount++;
                    }
                });
                if (label) rowData.push({row: i, label: label.substring(0, 60), filledCells: filledCount});
            });
            return {totalRows: rows.length, rows: rowData};
        }""")
        print(f"  Shift table analysis: {json.dumps(shift_analysis, indent=2, ensure_ascii=False)}")

        # ============================================================
        # STEP 7: Try assignment via Calendar/Table as admin
        # Calendar/Table is company-scoped — needs to be accessed
        # by someone in City's company context
        # ============================================================
        print("\n=== STEP 7: Try assignment via Calendar/Table ===")
        logout(page)
        login(page, "admin@local", "admin123")

        # First switch admin context to City
        page.goto(f"{BASE}/Owner/Programs", wait_until="networkidle")
        time.sleep(0.3)

        # Check if City is still selected
        current_company = page.evaluate("""() => {
            const sel = document.querySelector('select[name="companyId"]');
            return sel ? {value: sel.value, text: sel.options[sel.selectedIndex]?.textContent?.trim()} : null;
        }""")
        print(f"  Current company context: {json.dumps(current_company, ensure_ascii=False)}")

        # If not City, switch again
        if current_company and current_company.get('value') != '4':
            page.evaluate("""() => {
                const form = document.querySelector('form[action="/Owner/SelectCompany"]');
                const sel = form.querySelector('select[name="companyId"]');
                sel.value = '4';
                const hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = 'returnUrl';
                hidden.value = '/Calendar/Table?start=2026-03-02&view=week';
                form.appendChild(hidden);
                form.submit();
            }""")
            page.wait_for_load_state("networkidle")
            time.sleep(1)

        # Navigate to Calendar/Table
        page.goto(f"{BASE}/Calendar/Table?start=2026-03-02&view=week", wait_until="networkidle")
        time.sleep(1)
        shot(page, "08-calendar-table.png")

        # Check table state
        table_info = page.evaluate("""() => {
            const table = document.querySelector('table');
            if (!table) return {error: 'no table'};
            const rows = table.querySelectorAll('tbody tr');
            const headerCells = table.querySelectorAll('thead th');
            const headers = Array.from(headerCells).map(th => th.textContent.trim().substring(0, 30));
            const rowData = [];
            rows.forEach((tr, i) => {
                const cells = tr.querySelectorAll('td');
                const label = cells[0]?.textContent?.trim() || '';
                let btnCount = 0;
                let assignmentCount = 0;
                cells.forEach(td => {
                    const btns = td.querySelectorAll('button, a.btn, svg');
                    btnCount += btns.length;
                    const assigns = td.querySelectorAll('.assignment, [data-assignment-id]');
                    assignmentCount += assigns.length;
                });
                rowData.push({
                    row: i,
                    label: label.substring(0, 50),
                    buttons: btnCount,
                    assignments: assignmentCount,
                    cellCount: cells.length
                });
            });
            return {
                headers: headers,
                totalRows: rows.length,
                rows: rowData.slice(0, 10)
            };
        }""")
        print(f"  Calendar/Table info: {json.dumps(table_info, indent=2, ensure_ascii=False)}")

        # Check employee dropdown availability
        employee_info = page.evaluate("""() => {
            const dd = document.getElementById('employeeDropdown');
            if (!dd) return {error: 'no employee dropdown'};
            const items = dd.querySelectorAll('[data-user-id], .employee-item, option');
            return {
                found: true,
                itemCount: items.length,
                items: Array.from(items).slice(0, 10).map(el => ({
                    userId: el.dataset?.userId || el.value,
                    name: el.textContent?.trim()?.substring(0, 40)
                }))
            };
        }""")
        print(f"  Employee dropdown: {json.dumps(employee_info, indent=2, ensure_ascii=False)}")

        # Try clicking a + button or shift cell to assign
        plus_elements = page.evaluate("""() => {
            const els = document.querySelectorAll('td .add-btn, td .shift-add, td button, td svg.plus-icon, td [class*="plus"]');
            return {
                count: els.length,
                elements: Array.from(els).slice(0, 5).map(el => ({
                    tag: el.tagName,
                    class: el.className?.substring(0, 50),
                    text: el.textContent?.trim()?.substring(0, 30),
                    onclick: el.getAttribute('onclick')?.substring(0, 50)
                }))
            };
        }""")
        print(f"  Plus/Add elements: {json.dumps(plus_elements, indent=2, ensure_ascii=False)}")

        # Take a final overall screenshot
        shot(page, "09-final-state.png")

        print("\n=== DONE ===")
        browser.close()
        return 0

if __name__ == "__main__":
    sys.exit(main())
