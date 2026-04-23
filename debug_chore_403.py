"""Probe QuickAddChore for every user as admin@local — find who returns 403 vs 200."""
from playwright.sync_api import sync_playwright
import sys, io, json
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

BASE = "http://localhost:5000"

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(ignore_https_errors=True, viewport={"width": 1500, "height": 900})
    page = ctx.new_page()

    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill('input[name="Email"]', "admin@local")
    page.fill('input[name="Password"]', "admin123")
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")

    # 1. Probe a wide range of user IDs (scan 1..40) on a future date.
    print("=== Probe matrix: assigneeId 1..40 on 2026-04-30 ===")
    for uid in range(1, 41):
        resp = ctx.request.post(
            f"{BASE}/Api/Calendar/QuickAddChore",
            data=json.dumps({"date": "2026-04-30", "assigneeId": uid, "title": f"PROBE_{uid}", "notes": None, "forceAssign": False}),
            headers={"Content-Type": "application/json", "X-Requested-With": "XMLHttpRequest"},
        )
        body = resp.text()
        # Try to extract message
        try:
            j = json.loads(body)
            msg = j.get("message", "")
        except Exception:
            msg = body[:80]
        print(f"  uid={uid:>3}  status={resp.status}  msg={msg!r}")

    # 2. Check what /Calendar/Chores says about CanEdit for admin@local.
    page.goto(f"{BASE}/Calendar/Chores", wait_until="networkidle")
    page.wait_for_timeout(500)
    diag = page.evaluate("""() => {
        const sel = document.getElementById('bottom-sheet-user-select') || document.querySelector('select[data-role=assignee-select]');
        const visibleUsers = Array.from(document.querySelectorAll('[data-row-id^=user-]'))
            .map(e => e.getAttribute('data-row-id')).filter((v,i,a)=>a.indexOf(v)===i);
        const addBtns = document.querySelectorAll('.excel-calendar__add-btn').length;
        return {
            visibleUserRows: visibleUsers,
            addBtnCount: addBtns,
            quickEntryCanAssign: document.getElementById('quickEntryToggle') && document.getElementById('quickEntryToggle').dataset.canAssign,
        };
    }""")
    print("\n=== Page diagnostics ===")
    for k, v in diag.items():
        print(f"  {k}: {v}")

    browser.close()
