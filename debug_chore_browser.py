"""Full browser test: admin@local UI flow assigning chore to a cross-company user."""
from playwright.sync_api import sync_playwright
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

BASE = "http://localhost:5000"

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(ignore_https_errors=True, viewport={"width": 1500, "height": 900})
    page = ctx.new_page()

    network = []
    def on_resp(r):
        if "QuickAddChore" in r.url:
            try: body = r.text()[:600]
            except: body = "<binary>"
            network.append((r.status, body))
    page.on("response", on_resp)
    console_log = []
    page.on("console", lambda m: console_log.append(f"[{m.type}] {m.text}"))
    page.on("pageerror", lambda e: console_log.append(f"[PAGEERROR] {e}"))

    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill('input[name="Email"]', "admin@local")
    page.fill('input[name="Password"]', "admin123")
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")

    # Switch to molecule 2 ("Ella") so cross-company users are visible.
    page.goto(f"{BASE}/Calendar/Chores?MoleculeId=2&Start=2026-04-26&ViewMode=2weeks", wait_until="networkidle")
    page.wait_for_timeout(800)

    # Verify the dropdown now shows cross-company users
    diag = page.evaluate("""() => ({
        userRows: Array.from(document.querySelectorAll('[data-row-id^=user-]'))
            .map(e => e.getAttribute('data-row-id'))
            .filter((v,i,a) => a.indexOf(v)===i),
    })""")
    print("Visible user rows in molecule 2:", len(diag['userRows']))

    # Cross-company assignment via UI (mgr.hakam.ella@test = uid 22, in CompanyId=6, MoleculeId=2)
    print("\n--- Cross-company UI assignment: uid=22 (mgr.hakam.ella, CompanyId=6) ---")
    page.evaluate("""async () => {
        await window.quickAddChore('2026-05-02', 22, 'CROSS_COMPANY_UI_TEST', false, null);
    }""")
    page.wait_for_timeout(2000)

    print("Network:")
    for r in network: print(" ", r)

    # Verify chore is visible after page reload
    page.goto(f"{BASE}/Calendar/Chores?MoleculeId=2&Start=2026-04-26&ViewMode=2weeks", wait_until="networkidle")
    page.wait_for_timeout(500)
    found = page.evaluate("""() => {
        const cells = document.querySelectorAll('.excel-calendar__cell');
        for (const c of cells) {
            if ((c.textContent || '').includes('CROSS_COMPANY_UI_TEST')) {
                return {
                    date: c.getAttribute('data-date'),
                    rowId: c.getAttribute('data-row-id'),
                };
            }
        }
        return null;
    }""")
    print(f"\nChore appears in calendar after reload: {found}")

    print("\n--- Console (last 8) ---")
    for c in console_log[-8:]: print(c)

    browser.close()
