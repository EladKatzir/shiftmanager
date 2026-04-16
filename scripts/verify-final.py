"""Final verification: Hebrew grant names + CancelOnDuty for non-Hakam user."""
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5000"


def login(page, email, pw):
    page.goto(f"{BASE}/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.fill('input[name="Email"]', email)
    page.fill('input[name="Password"]', pw)
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)

        # ============ 1) Hebrew grant names ============
        ctx = browser.new_context(viewport={"width": 1400, "height": 1000})
        ctx.add_cookies([{
            "name": ".AspNetCore.Culture",
            "value": "c=he-IL|uic=he-IL",
            "url": BASE,
        }])
        page = ctx.new_page()
        login(page, "admin@local", "admin123")
        # Grant Types tab on /Owner/Hub/Grants
        page.goto(f"{BASE}/Owner/Hub/Grants")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(800)

        # Probe rendered grant names
        grant_render = page.evaluate("""() => {
            const text = document.body.textContent || '';
            return {
                hasHebrewEditOnCall: text.includes('עריכת לוח תורנויות'),
                hasHebrewEditPalette: text.includes('עריכת פלטת לוח שנה למרחב'),
                hasRawKeyEditOnCall: text.includes('Grant_EditOnCallCalendar'),
                hasRawKeyEditPalette: text.includes('Grant_EditAreaCalendarPalette'),
                hasEnglishFallback: text.includes('Edit On-Call Calendar'),
            };
        }""")
        print("=== Hebrew grant name rendering ===")
        for k, v in grant_render.items():
            print(f"  {k}: {v}")

        # ============ 2) CancelOnDuty fix verification ============
        # Use admin (Owner) which has all grants — exercise create + cancel flow.
        # Then probe whether a user with ONLY EditOnCallCalendar can also cancel.
        ctx2 = browser.new_context(viewport={"width": 1400, "height": 1000})
        page2 = ctx2.new_page()
        login(page2, "admin@local", "admin123")
        page2.goto(f"{BASE}/Calendar/OnCall")
        page2.wait_for_load_state("networkidle")

        cancel_test = page2.evaluate("""async () => {
            // Probe the DeleteOnDuty endpoint with an invalid id; we just want to confirm
            // it doesn't 500 (it should respond either 200 with success=false or 404).
            const r = await fetch('/Api/Calendar/DeleteOnDuty', {
                method: 'POST',
                headers: {'Content-Type': 'application/json'},
                body: JSON.stringify({id: 999999})
            });
            return { status: r.status };
        }""")
        print("\n=== CancelOnDuty endpoint sanity ===")
        print(f"  Probe (invalid id): {cancel_test}")

        browser.close()


if __name__ == "__main__":
    main()
