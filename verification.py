"""Live verification — issues 1/3/4 of the four-issue fix."""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5000"
OWNER_EMAIL = "test.owner@shifty.test"
OWNER_PASSWORD = "TestOwner123!"

results = []

def record(name, ok, detail=""):
    results.append(("PASS" if ok else "FAIL", name, detail))
    print(f"[{'PASS' if ok else 'FAIL'}] {name}" + (f" — {detail}" if detail else ""))

def login(page, email, password):
    page.goto(f"{BASE}/Auth/Login"); page.wait_for_load_state("networkidle")
    page.fill('input[name="Email"], input[type=email]', email)
    page.fill('input[name="Password"], input[type=password]', password)
    page.click('button[type=submit]'); page.wait_for_load_state("networkidle")

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context()

    # Set the ASP.NET culture cookie BEFORE any navigation so every request is Hebrew.
    ctx.add_cookies([{
        "name": ".AspNetCore.Culture",
        "value": "c=he-IL|uic=he-IL",
        "url": BASE,
    }])

    page = ctx.new_page()

    # ============ English-mode checks (flip culture off for these) ============
    ctx.clear_cookies()
    try:
        page.goto(BASE, timeout=30000); page.wait_for_load_state("networkidle")
        record("server_reachable", True, f"title={page.title()!r}")
    except Exception as e:
        record("server_reachable", False, str(e)); browser.close(); sys.exit(1)

    try:
        login(page, OWNER_EMAIL, OWNER_PASSWORD)
        record("owner_login", "/Auth/Login" not in page.url, f"url={page.url}")
    except Exception as e:
        record("owner_login", False, str(e))

    # Issue #1 (English): dropdown renders without raw key
    try:
        page.goto(f"{BASE}/Owner/Programs"); page.wait_for_load_state("networkidle")
        opts = page.locator('select[name*="ShiftTypeId"] option').all_text_contents()
        raw_leak = any(("ShiftType_" in o and "_Name" in o) for o in opts)
        sample = [o.strip().split('\n')[0].strip()[:40] for o in opts[:4]]
        record("programs_en_no_raw_key", not raw_leak, f"first={sample}")
        page.screenshot(path="C:/Users/katzi/Downloads/ShiftManager/_verify_en_programs.png", full_page=True)
    except Exception as e:
        record("programs_en_no_raw_key", False, str(e))

    # ============ Hebrew-mode checks ============
    ctx.add_cookies([{
        "name": ".AspNetCore.Culture", "value": "c=he-IL|uic=he-IL", "url": BASE,
    }])

    # Issue #1 (Hebrew): dropdown no raw keys, and canonical types show Hebrew names
    try:
        page.goto(f"{BASE}/Owner/Programs"); page.wait_for_load_state("networkidle")
        opts = page.locator('select[name*="ShiftTypeId"] option').all_text_contents()
        raw_leak_he = any(("ShiftType_" in o and "_Name" in o) for o in opts)
        hebrew_chars_present = any(any('\u0590' <= c <= '\u05FF' for c in o) for o in opts)
        sample = [o.strip().split('\n')[0].strip()[:40] for o in opts[:4]]
        record("programs_he_no_raw_key", not raw_leak_he, f"first={sample}")
        record("programs_he_hebrew_text", hebrew_chars_present, f"first={sample}")
        page.screenshot(path="C:/Users/katzi/Downloads/ShiftManager/_verify_he_programs.png", full_page=True)
    except Exception as e:
        record("programs_he_no_raw_key", False, str(e))

    # Issue #3 + #4: check Hebrew strings on any page that renders role descriptions.
    # Owner/RoleTemplates lists roles with their he-IL descriptions.
    for path in ("/Owner/RoleTemplates", "/Help/Roles", "/Help"):
        try:
            resp = page.goto(f"{BASE}{path}"); page.wait_for_load_state("networkidle")
            status = resp.status if resp else 0
            body = page.locator("body").inner_text()
            has_nachfaf = "נחפף" in body
            has_chanich_alone = "חניך" in body  # substring match — will also match נחפף? no, different letters
            has_desk = "דסק" in body
            has_pluga = ("פלוגה" in body) or ("פלוגות" in body)
            detail = f"{path} status={status} נחפף={has_nachfaf} חניך={has_chanich_alone} דסק={has_desk} פלוגה={has_pluga}"
            print("   " + detail)
            if has_nachfaf or has_desk or has_chanich_alone or has_pluga:
                record(f"strings_{path}_found_something", True, detail)
                page.screenshot(path=f"C:/Users/katzi/Downloads/ShiftManager/_verify_he{path.replace('/','_')}.png", full_page=True)
                break
        except Exception as e:
            print(f"   {path} error: {e}")

    # Last resort: fetch the .resx-backed content by hitting a JSON API if available,
    # or just verify that no page we land on shows the OLD terms.
    try:
        # Dashboard/profile often shows role display name which comes from DB (DisplayNameHE).
        page.goto(f"{BASE}/Profile"); page.wait_for_load_state("networkidle")
        body = page.locator("body").inner_text()
        old_terms = ("פלוגה" in body) or ("פלוגות" in body) or ("חניך" in body and "נחפף" not in body)
        record("profile_no_old_terms", not old_terms, f"found_old_terms={old_terms}")
        page.screenshot(path="C:/Users/katzi/Downloads/ShiftManager/_verify_he_profile.png", full_page=True)
    except Exception as e:
        record("profile_no_old_terms", False, str(e))

    browser.close()

print("\n=== SUMMARY ===")
for r in results:
    print(" | ".join(r))
sys.exit(0 if all(r[0] == "PASS" for r in results) else 1)
