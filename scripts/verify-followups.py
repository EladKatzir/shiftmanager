"""Verification of the three follow-ups:
  1) AreaPalette localized to Hebrew (RTL)
  2) Back-fill preview shows specific grant keys per user
  3) Surplus audit card present and functional (warn-only)
"""
from pathlib import Path
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5000"
OUT = Path(__file__).parent.parent / "screenshots"
OUT.mkdir(exist_ok=True)


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        ctx = browser.new_context(viewport={"width": 1400, "height": 1000})
        page = ctx.new_page()

        # Login as Owner
        page.goto(f"{BASE}/Auth/Login")
        page.wait_for_load_state("networkidle")
        page.fill('input[name="Email"]', "admin@local")
        page.fill('input[name="Password"]', "admin123")
        page.click('button[type="submit"]')
        page.wait_for_load_state("networkidle")

        # =========== 1) HEBREW AreaPalette ===========
        ctx.add_cookies([{
            "name": ".AspNetCore.Culture",
            "value": "c=he-IL|uic=he-IL",
            "url": BASE,
        }])
        page.goto(f"{BASE}/Admin/Organization/AreaPalette?areaId=1")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(800)
        page.screenshot(path=str(OUT / "areapalette_hebrew_FINAL.png"), full_page=False)

        # Probe localized strings
        hebrew_check = page.evaluate("""() => {
            const text = document.body.textContent || '';
            return {
                hasHebrewTitle: text.includes('פלטת'),
                hasHebrewSlot: text.includes('משמרת בוקר'),
                hasHebrewDuty: text.includes('תורנות'),
                hasHebrewSave: text.includes('שמור פלטה'),
                hasHebrewLivePreview: text.includes('תצוגה מקדימה'),
                hasEnglishLeak: text.includes('Morning shift') || text.includes('Save Palette'),
            };
        }""")
        print("=== AreaPalette Hebrew localization ===")
        for k, v in hebrew_check.items():
            print(f"  {k}: {v}")

        # =========== 2) Back-fill preview shows grant keys ===========
        ctx.add_cookies([{
            "name": ".AspNetCore.Culture",
            "value": "c=en-US|uic=en-US",
            "url": BASE,
        }])
        page.goto(f"{BASE}/Owner/Hub/Grants")
        page.wait_for_load_state("networkidle")

        # Click "Grant Actions" tab
        page.evaluate("""() => {
            const tab = document.querySelector('[data-tab=\"grant-actions\"]');
            if (tab) tab.click();
        }""")
        page.wait_for_timeout(400)

        # Submit dry-run form (the first one — back-fill)
        token = page.evaluate("document.querySelector('input[name=\"__RequestVerificationToken\"]').value")
        # Run dry-run via fetch (avoid clicking specific button)
        result = page.evaluate(f"""async () => {{
            const fd = new FormData();
            fd.append('__RequestVerificationToken', '{token}');
            fd.append('roleTemplateId', '');
            const r = await fetch('/Owner/Hub/Grants?handler=BackfillPreview', {{ method: 'POST', body: fd }});
            return r.status;
        }}""")
        print(f"\n=== Back-fill dry-run via fetch: HTTP {result} ===")
        # Reload to see report
        page.reload()
        page.wait_for_load_state("networkidle")
        page.evaluate("document.querySelector('[data-tab=\"grant-actions\"]').click()")
        page.wait_for_timeout(400)

        # Submit synchronously via form click
        page.evaluate("""() => {
            const tabs = document.querySelectorAll('[data-tab=\"grant-actions\"]');
            tabs[0].click();
        }""")
        page.wait_for_timeout(300)
        # Find back-fill dry-run button by text
        page.evaluate("""() => {
            const btns = Array.from(document.querySelectorAll('button'));
            const dry = btns.find(b => /Dry-run/i.test(b.textContent || ''));
            if (dry) dry.click();
        }""")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(800)
        # Make sure tab is active again
        page.evaluate("document.querySelector('[data-tab=\"grant-actions\"]').click()")
        page.wait_for_timeout(300)
        page.screenshot(path=str(OUT / "grants_backfill_FINAL.png"), full_page=True)

        # Check grant keys present in HTML
        backfill_check = page.evaluate("""() => {
            const text = document.body.textContent || '';
            return {
                hasDryRunSummary: /Dry-run complete/i.test(text),
                hasGrantKeyText: /EditOnCallCalendar|ManageOnDutyTypes|EditAreaCalendarPalette|DirectorHubAccess/.test(text),
                showAffectedDetails: !!document.querySelector('details summary'),
            };
        }""")
        print("\n=== Back-fill preview grant keys ===")
        for k, v in backfill_check.items():
            print(f"  {k}: {v}")

        # =========== 3) Surplus audit card ===========
        page.evaluate("""() => {
            const btns = Array.from(document.querySelectorAll('button'));
            const audit = btns.find(b => /Run audit/i.test(b.textContent || ''));
            if (audit) audit.click();
        }""")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(800)
        page.evaluate("document.querySelector('[data-tab=\"grant-actions\"]').click()")
        page.wait_for_timeout(300)
        page.screenshot(path=str(OUT / "grants_surplus_FINAL.png"), full_page=True)

        surplus_check = page.evaluate("""() => {
            const text = document.body.textContent || '';
            return {
                hasSurplusCard: /Surplus Auto-Grant Audit/i.test(text),
                hasAuditResult: /Surplus:|No surplus auto-grants found/i.test(text),
                hasNoRemoveButton: !text.match(/Remove surplus|Delete surplus/i),
            };
        }""")
        print("\n=== Surplus audit card ===")
        for k, v in surplus_check.items():
            print(f"  {k}: {v}")

        browser.close()


if __name__ == "__main__":
    main()
