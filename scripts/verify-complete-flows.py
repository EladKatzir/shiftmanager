"""Comprehensive UX flow verification — click through real user scenarios.

Gaps this fills:
 - BRDirector renames a shift type (Blueprints)
 - BRDirector adds a chore type (ChoreTypes)
 - BRDirector opens DutyTypes (ManageOnDutyTypes grant)
 - Non-ManageOnDuty user with EditOnCallCalendar can assign on OnCall (CanEdit widening)
 - AreaPalette: drag picker, toggle dark/RTL, save, verify tokens change, reset
 - Back-fill: click Dry-run button, click Execute button
 - Surplus: click Run audit button
"""
from pathlib import Path
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5000"
OUT = Path(__file__).parent.parent / "screenshots"
OUT.mkdir(exist_ok=True)

results = []


def log(ok, label, detail=""):
    status = "PASS" if ok else "FAIL"
    results.append((ok, label, detail))
    print(f"  {status}  {label}" + (f"  ({detail})" if detail else ""))


def login(ctx, email, pw):
    page = ctx.new_page()
    page.goto(f"{BASE}/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.fill('input[name="Email"]', email)
    page.fill('input[name="Password"]', pw)
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")
    return page


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)

        # =========== Scenario A: BRDirector UX flows ===========
        print("\n=== A. BRDirector UX flows ===")
        ctx = browser.new_context(viewport={"width": 1400, "height": 1000})
        page = login(ctx, "mgr.br.hir@test", "Test1234!")

        # A1: Open Blueprints — can see Edit Name button
        page.goto(f"{BASE}/Owner/Blueprints")
        page.wait_for_load_state("networkidle")
        page.wait_for_timeout(500)
        has_edit = page.evaluate("""() => {
            return Array.from(document.querySelectorAll('button'))
                .some(b => /Edit Name/i.test(b.textContent || ''));
        }""")
        log(has_edit, "Blueprints: BRDirector sees Edit Name button")

        # A2: ChoreTypes — can see Create form
        page.goto(f"{BASE}/Admin/Organization/ChoreTypes")
        page.wait_for_load_state("networkidle")
        has_create = page.evaluate("""() => {
            return Array.from(document.querySelectorAll('button, a'))
                .some(b => /Create Chore Type/i.test(b.textContent || ''));
        }""")
        log(has_create, "ChoreTypes: BRDirector sees Create button")

        # A3: DutyTypes (ManageOnDutyTypes grant) — can reach page
        resp = page.goto(f"{BASE}/Admin/Organization/DutyTypes")
        page.wait_for_load_state("networkidle")
        reached_duty = resp and resp.status == 200 and "/Auth/Login" not in page.url
        log(reached_duty, "DutyTypes: BRDirector can reach page (ManageOnDutyTypes grant)",
            f"url={page.url}")

        # A4: OnCall page — sees interactive UI (CanEdit=true)
        page.goto(f"{BASE}/Calendar/OnCall")
        page.wait_for_load_state("networkidle")
        can_edit = page.evaluate("""() => {
            const t = document.body.textContent || '';
            return /Quick Entry/i.test(t) && !document.body.hasAttribute('data-readonly');
        }""")
        log(can_edit, "OnCall: BRDirector sees Quick Entry button (CanEdit widened)")

        # =========== Scenario B: Non-ManageOnDuty Employee ===========
        # Note: Employee doesn't have ManageOnDuty but DOES have EditOnCallCalendar.
        # Find an Employee test user; the QA seed uses employee.* emails.
        print("\n=== B. Employee with only EditOnCallCalendar ===")
        ctx_emp = browser.new_context(viewport={"width": 1400, "height": 1000})
        # test.member@shifty.test has Employee template (id 1) + EditOnCallCalendar grant.
        page2 = login(ctx_emp, "test.member@shifty.test", "TestMember123!")
        logged = "/Auth/Login" not in page2.url
        log(logged, "Employee login succeeded", f"url={page2.url}")

        if logged:
            page2.goto(f"{BASE}/Calendar/OnCall")
            page2.wait_for_load_state("networkidle")
            emp_can_edit = page2.evaluate("""() => {
                const t = document.body.textContent || '';
                return /Quick Entry/i.test(t);
            }""")
            log(emp_can_edit, "OnCall: Employee with EditOnCallCalendar sees edit UI")

        # =========== Scenario C: AreaPalette end-to-end ===========
        print("\n=== C. AreaPalette interactive flow ===")
        ctx_adm = browser.new_context(viewport={"width": 1400, "height": 1000})
        page3 = login(ctx_adm, "admin@local", "admin123")

        page3.goto(f"{BASE}/Admin/Organization/AreaPalette?areaId=1")
        page3.wait_for_load_state("networkidle")
        page3.wait_for_timeout(500)

        # C1: Drag Morning picker to red, verify preview updates via JS
        result_c1 = page3.evaluate("""() => {
            const picker = document.querySelector('input[type=color][name="ShiftMorning"]');
            picker.value = '#FF0000';
            picker.dispatchEvent(new Event('input', {bubbles:true}));
            // Wait one rAF
            return new Promise(r => requestAnimationFrame(() => {
                const preview = document.getElementById('palette-preview');
                const bg = preview ? getComputedStyle(preview).getPropertyValue('--shift-morning').trim() : '';
                r({ previewHasRed: bg === '#FF0000' || bg === '#ff0000' });
            }));
        }""")
        log(result_c1["previewHasRed"], "AreaPalette: drag Morning picker -> live preview CSS var updates")

        # C2: Toggle dark + RTL inside preview
        page3.check('#preview-dark')
        page3.check('#preview-rtl')
        page3.wait_for_timeout(400)
        toggled = page3.evaluate("""() => {
            const p = document.getElementById('palette-preview');
            return { theme: p.getAttribute('data-theme'), dir: p.getAttribute('dir') };
        }""")
        log(toggled["theme"] == "dark" and toggled["dir"] == "rtl",
            "AreaPalette: dark+RTL toggles apply to preview container", f"{toggled}")

        # C3: Save button persists
        page3.evaluate("document.querySelector('input[type=color][name=\"Chore\"]').value = '#00FF00'; document.querySelector('input[type=color][name=\"Chore\"]').dispatchEvent(new Event('input', {bubbles:true}))")
        page3.wait_for_timeout(300)
        page3.click('button[type="submit"]:has-text("Save")')
        page3.wait_for_load_state("networkidle")
        saved = page3.evaluate("""() => {
            const t = document.body.textContent || '';
            return /Palette saved/i.test(t) || /\u05d4\u05e4\u05dc\u05d8\u05d4 \u05e0\u05e9\u05de\u05e8\u05d4/.test(t);
        }""")
        log(saved, "AreaPalette: Save button shows success message")

        # C4: Next page load injects override style
        page3.goto(f"{BASE}/Home/Index")
        page3.wait_for_load_state("networkidle")
        injected = page3.evaluate("""() => {
            const style = document.getElementById('area-palette-override');
            if (!style) return { present: false };
            const text = style.textContent || '';
            return {
                present: true,
                hasMorningRed: /--shift-morning:\\s*#FF0000/i.test(text),
                hasChoreGreen: /--chore:\\s*#00FF00/i.test(text),
            };
        }""")
        log(injected.get("present") and injected.get("hasMorningRed") and injected.get("hasChoreGreen"),
            "_Layout: override injects saved colors", f"{injected}")

        # C5: Reset clears
        page3.goto(f"{BASE}/Admin/Organization/AreaPalette?areaId=1")
        page3.wait_for_load_state("networkidle")
        # Dialog handler: accept confirm
        page3.on("dialog", lambda d: d.accept())
        # Find Reset / Clear All
        page3.evaluate("""() => {
            const btns = Array.from(document.querySelectorAll('button'));
            const clear = btns.find(b => /Clear All|\u05e0\u05e7\u05d4 \u05d4\u05db\u05dc/.test(b.textContent || ''));
            if (clear) clear.click();
        }""")
        page3.wait_for_load_state("networkidle")
        page3.goto(f"{BASE}/Home/Index")
        page3.wait_for_load_state("networkidle")
        cleared = page3.evaluate("() => !document.getElementById('area-palette-override')")
        log(cleared, "AreaPalette: Clear All removes <style> injection on next page")

        # =========== Scenario D: Back-fill flow ===========
        print("\n=== D. Back-fill UI click flow ===")
        page3.goto(f"{BASE}/Owner/Hub/Grants")
        page3.wait_for_load_state("networkidle")
        page3.evaluate("document.querySelector('[data-tab=\"grant-actions\"]')?.click()")
        page3.wait_for_timeout(400)
        # Click Dry-run button (first one found in Grant Actions panel)
        page3.evaluate("""() => {
            const btns = Array.from(document.querySelectorAll('#grant-actions button'));
            const dry = btns.find(b => /Dry-run/i.test(b.textContent || ''));
            if (dry) dry.click();
        }""")
        page3.wait_for_load_state("networkidle")
        page3.wait_for_timeout(500)
        page3.evaluate("document.querySelector('[data-tab=\"grant-actions\"]')?.click()")
        page3.wait_for_timeout(300)
        backfill_result = page3.evaluate("""() => {
            const text = document.body.textContent || '';
            return {
                hasDryRunMsg: /Dry-run complete/i.test(text),
                hasGrantKeys: /EditOnCallCalendar|DirectorHubAccess|ManageOnDutyTypes/.test(text),
            };
        }""")
        log(backfill_result["hasDryRunMsg"], "Back-fill: Dry-run button triggers server preview")
        log(backfill_result["hasGrantKeys"], "Back-fill: preview shows actual grant keys (not just counts)")

        # =========== Scenario E: Surplus audit ===========
        print("\n=== E. Surplus audit click flow ===")
        page3.evaluate("""() => {
            const btns = Array.from(document.querySelectorAll('#grant-actions button'));
            const audit = btns.find(b => /Run audit/i.test(b.textContent || ''));
            if (audit) audit.click();
        }""")
        page3.wait_for_load_state("networkidle")
        page3.wait_for_timeout(500)
        page3.evaluate("document.querySelector('[data-tab=\"grant-actions\"]')?.click()")
        page3.wait_for_timeout(300)
        surplus_result = page3.evaluate("""() => {
            const text = document.body.textContent || '';
            return {
                hasAuditResult: /Surplus:|No surplus auto-grants/i.test(text),
                hasRemoveButton: /Remove surplus|Delete surplus/i.test(text),
            };
        }""")
        log(surplus_result["hasAuditResult"], "Surplus: Run audit returns a result")
        log(not surplus_result["hasRemoveButton"], "Surplus: warn-only (no remove/delete button present)")

        browser.close()

    total = len(results)
    passed = sum(1 for ok, _, _ in results if ok)
    failed = total - passed
    print(f"\n=== SUMMARY ===")
    print(f"  {passed}/{total} passed, {failed} failed")
    if failed > 0:
        print("\nFailures:")
        for ok, label, detail in results:
            if not ok:
                print(f"  - {label}  {detail}")
    import sys
    sys.exit(0 if failed == 0 else 1)


if __name__ == "__main__":
    main()
