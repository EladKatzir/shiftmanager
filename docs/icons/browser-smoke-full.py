"""
Comprehensive browser smoke suite for the theme picker + Lucide migration.

Prereq: ShiftManager running on http://localhost:5173.

Scenarios:
  A. Happy path              — preset click → save → 200 → cookie
  B. Persistence across refresh
  C. Persistence across logout/login (login-time cookie seed)
  D. Reset button (DELETE endpoint)
  E. Mode toggle (light → dark → auto)
  F. Hex text input typing
  G. RGB number input typing
  H. Lightness slider
  I. HSL canvas click-to-pick
  J. Close button dismisses modal
  K. Backdrop click dismisses modal
  L. Saved toast renders
  M. Dependent token propagation (primary-hover, primary-soft, accent, focus-ring)
  N. Per-user isolation (user B's theme not mutated when user A saves)
  O. Hebrew RTL smoke — picker still opens + works
  P. Ctrl+Click ShiftSwap regression
"""
import sys, io, re, sqlite3, json, time
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
USER_A = "test.manager@shifty.test"
USER_B = "test.member@shifty.test"
PW     = "TestManager123!"
PW_B   = "TestMember123!"
DB     = "C:/Users/katzi/Downloads/ShiftManager/app.db"

results = []
def ok(name):         results.append(("PASS", name))
def fail(name, detail): results.append(("FAIL", f"{name} — {detail}"))

def reset_user_theme(email):
    c = sqlite3.connect(DB)
    c.execute("UPDATE Users SET ThemeColor = NULL, ThemeMode = NULL WHERE Email = ?", (email,))
    c.commit(); c.close()

def get_user_theme(email):
    c = sqlite3.connect(DB)
    r = c.execute("SELECT ThemeColor, ThemeMode FROM Users WHERE Email = ?", (email,)).fetchone()
    c.close()
    return r

def login(page, email, pw):
    page.goto(BASE + "/Auth/Login", wait_until="networkidle", timeout=20000)
    page.fill('input[name="Email"]', email)
    page.fill('input[name="Password"]', pw)
    page.locator('button[type="submit"]').first.click()
    page.wait_for_load_state("networkidle", timeout=15000)
    return "/Auth/Login" not in page.url

def open_picker(page):
    page.locator(".sidebar-brand, .brand").first.click(modifiers=["Shift"])
    page.wait_for_selector(".theme-picker", state="visible", timeout=5000)

def read_primary(page):
    return page.evaluate("getComputedStyle(document.documentElement).getPropertyValue('--primary').trim()")

def read_token(page, name):
    return page.evaluate(f"getComputedStyle(document.documentElement).getPropertyValue('{name}').trim()")

# ==========================================================================
# Pre-flight: reset state
reset_user_theme(USER_A)
reset_user_theme(USER_B)
assert get_user_theme(USER_A) == (None, None)
assert get_user_theme(USER_B) == (None, None)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)

    # ----------------------------------------------------- A. Happy path + M. tokens
    ctxA = browser.new_context(viewport={"width": 1400, "height": 900})
    pageA = ctxA.new_page()
    if not login(pageA, USER_A, PW):
        fail("login user A", "stayed on /Auth/Login")
        print_results_and_exit()
    ok("A. Login user A")

    try:
        open_picker(pageA)
        before = read_primary(pageA)
        # click Plum (index 4)
        pageA.locator(".theme-picker__preset").nth(4).click()
        pageA.wait_for_timeout(400)
        after = read_primary(pageA)
        if after.lower() != before.lower():
            ok(f"A. Preset click recolors --primary ({before} → {after})")
        else:
            fail("A. Preset recolor", f"no change: {before}")
        # M. Dependent tokens should also change
        hover   = read_token(pageA, '--primary-hover')
        soft    = read_token(pageA, '--primary-soft')
        contrast= read_token(pageA, '--primary-contrast')
        accent  = read_token(pageA, '--accent')
        focus   = read_token(pageA, '--focus-ring')
        if all([hover, soft, contrast, accent, focus]):
            ok(f"M. All derived tokens set (hover={hover}, soft={soft}, accent={accent}, contrast={contrast}, focus-ring set)")
        else:
            fail("M. Derived tokens", f"missing: hover={hover} soft={soft} accent={accent} contrast={contrast} focus={focus}")
    except Exception as e:
        fail("A. Happy path", str(e))

    # ----------------------------------------------------- Save + cookie
    try:
        with pageA.expect_response(lambda r: "/Api/My/Theme" in r.url and r.request.method == "POST", timeout=10000) as rc:
            pageA.locator(".theme-picker__save").click()
        if rc.value.status == 200:
            ok("A. POST /Api/My/Theme → 200")
        else:
            fail("A. Save status", f"{rc.value.status}")
        db = get_user_theme(USER_A)
        if db[0] and db[0].lower() == '#7b3f7a':
            ok(f"A. DB persisted theme: {db}")
        else:
            fail("A. DB persisted", f"got {db}, expected Plum")
    except Exception as e:
        fail("A. Save flow", str(e))

    # ----------------------------------------------------- L. Saved toast
    try:
        toast = pageA.locator(".theme-picker__toast")
        if toast.count() > 0 and "Saved" in toast.inner_text():
            ok("L. Saved toast renders")
        else:
            fail("L. Saved toast", f"toast.count={toast.count()}")
    except Exception as e:
        fail("L. Toast", str(e))

    # ----------------------------------------------------- B. Persist across refresh
    try:
        pageA.reload(wait_until="networkidle")
        primary_after_reload = read_primary(pageA)
        if primary_after_reload.lower() == '#7b3f7a':
            ok(f"B. Theme persists across page refresh ({primary_after_reload})")
        else:
            fail("B. Persist across refresh", f"got {primary_after_reload}, expected #7b3f7a")
    except Exception as e:
        fail("B. Persist across refresh", str(e))

    # ----------------------------------------------------- C. Persist across logout/login
    try:
        # Logout and login fresh context → cookie should be seeded from DB
        ctxA.close()
        ctxA = browser.new_context(viewport={"width": 1400, "height": 900})
        pageA = ctxA.new_page()
        login(pageA, USER_A, PW)
        primary_after_relogin = read_primary(pageA)
        if primary_after_relogin.lower() == '#7b3f7a':
            ok(f"C. Theme persists across logout/login (login seed works) ({primary_after_relogin})")
        else:
            fail("C. Persist across login", f"got {primary_after_relogin}, expected #7b3f7a")
    except Exception as e:
        fail("C. Persist across login", str(e))

    # ----------------------------------------------------- E. Mode toggle
    try:
        open_picker(pageA)
        # Click Dark mode
        modes = pageA.locator(".theme-picker__mode")
        modes.nth(1).click()  # Dark
        pageA.wait_for_timeout(300)
        theme_attr = pageA.evaluate("document.documentElement.getAttribute('data-theme')")
        if theme_attr == 'dark':
            ok("E. Dark mode toggle → data-theme='dark'")
        else:
            fail("E. Dark mode toggle", f"data-theme={theme_attr}")
        # Click Auto
        modes.nth(2).click()
        pageA.wait_for_timeout(300)
        theme_attr2 = pageA.evaluate("document.documentElement.getAttribute('data-theme')")
        if theme_attr2 in ('light', 'dark'):
            ok(f"E. Auto mode → data-theme resolves to {theme_attr2!r}")
        else:
            fail("E. Auto mode", f"data-theme={theme_attr2}")
        # Back to Light
        modes.nth(0).click()
        pageA.wait_for_timeout(300)
    except Exception as e:
        fail("E. Mode toggle", str(e))

    # ----------------------------------------------------- F. Hex input
    try:
        hex_input = pageA.locator('[data-input="hex"]')
        hex_input.fill("#0E9F6E")  # Emerald
        pageA.wait_for_timeout(300)
        after_hex = read_primary(pageA)
        if after_hex.lower() == '#0e9f6e':
            ok(f"F. Hex input updates --primary ({after_hex})")
        else:
            fail("F. Hex input", f"after={after_hex}, expected #0e9f6e")
    except Exception as e:
        fail("F. Hex input", str(e))

    # ----------------------------------------------------- G. RGB inputs
    try:
        pageA.locator('[data-input="r"]').fill("255")
        pageA.locator('[data-input="g"]').fill("128")
        pageA.locator('[data-input="b"]').fill("0")  # bright orange
        pageA.wait_for_timeout(300)
        after_rgb = read_primary(pageA)
        if after_rgb.lower() == '#ff8000':
            ok(f"G. RGB inputs update --primary ({after_rgb})")
        else:
            fail("G. RGB inputs", f"after={after_rgb}, expected #ff8000")
    except Exception as e:
        fail("G. RGB inputs", str(e))

    # ----------------------------------------------------- H. Lightness slider
    try:
        slider = pageA.locator(".theme-picker__lightness")
        before_l = read_primary(pageA)
        slider.fill("30")  # make darker
        pageA.wait_for_timeout(300)
        after_l = read_primary(pageA)
        if after_l.lower() != before_l.lower():
            ok(f"H. Lightness slider updates --primary ({before_l} → {after_l})")
        else:
            fail("H. Lightness slider", f"no change at L=30")
    except Exception as e:
        fail("H. Lightness slider", str(e))

    # ----------------------------------------------------- I. HSL canvas click
    try:
        canvas = pageA.locator(".theme-picker__canvas")
        box = canvas.bounding_box()
        before_c = read_primary(pageA)
        # Click at a point offset from center — picks a hue
        canvas.click(position={"x": 40, "y": 40})
        pageA.wait_for_timeout(300)
        after_c = read_primary(pageA)
        if after_c.lower() != before_c.lower():
            ok(f"I. HSL canvas click updates --primary ({before_c} → {after_c})")
        else:
            fail("I. HSL canvas click", f"no change")
    except Exception as e:
        fail("I. HSL canvas click", str(e))

    # ----------------------------------------------------- J. Close button
    try:
        pageA.locator(".theme-picker__close").click()
        pageA.wait_for_selector(".theme-picker", state="hidden", timeout=3000)
        ok("J. Close button dismisses modal")
    except Exception as e:
        fail("J. Close button", str(e))

    # ----------------------------------------------------- K. Backdrop click dismisses
    try:
        open_picker(pageA)
        # Click the backdrop (outside .theme-picker inner element) — use coord
        bd = pageA.locator(".theme-picker-backdrop")
        box = bd.bounding_box()
        pageA.mouse.click(box['x'] + 5, box['y'] + 5)  # far corner
        pageA.wait_for_timeout(500)
        is_visible = pageA.locator(".theme-picker").count() > 0 and pageA.locator(".theme-picker").first.is_visible()
        if not is_visible:
            ok("K. Backdrop click dismisses modal")
        else:
            fail("K. Backdrop click", "modal still visible")
    except Exception as e:
        fail("K. Backdrop click", str(e))

    # ----------------------------------------------------- D. Reset button
    try:
        open_picker(pageA)
        with pageA.expect_response(lambda r: "/Api/My/Theme" in r.url and r.request.method == "DELETE", timeout=10000) as rc:
            pageA.locator('[data-action="reset"]').click()
        if rc.value.status == 200:
            ok("D. Reset → DELETE /Api/My/Theme → 200")
        else:
            fail("D. Reset status", f"{rc.value.status}")
        pageA.wait_for_timeout(500)
        db_after_reset = get_user_theme(USER_A)
        if db_after_reset == (None, None):
            ok("D. Reset nulled DB theme")
        else:
            fail("D. Reset DB state", f"got {db_after_reset}")
    except Exception as e:
        fail("D. Reset", str(e))

    # ----------------------------------------------------- N. Per-user isolation
    try:
        # Save a theme for user A
        ctxA.close()
        ctxA = browser.new_context()
        pageA = ctxA.new_page()
        login(pageA, USER_A, PW)
        open_picker(pageA)
        pageA.locator(".theme-picker__preset").nth(2).click()  # Forest
        pageA.wait_for_timeout(300)
        with pageA.expect_response(lambda r: "/Api/My/Theme" in r.url and r.request.method == "POST"):
            pageA.locator(".theme-picker__save").click()
        pageA.wait_for_timeout(500)
        b_theme = get_user_theme(USER_B)
        if b_theme == (None, None):
            ok(f"N. User A's save did NOT mutate user B's theme (B still {b_theme})")
        else:
            fail("N. Per-user isolation", f"B was mutated: {b_theme}")
    except Exception as e:
        fail("N. Per-user isolation", str(e))

    # ----------------------------------------------------- O. Hebrew RTL smoke
    try:
        ctxHe = browser.new_context(
            viewport={"width": 1400, "height": 900},
            locale="he-IL"
        )
        # Set culture cookie (ShiftManager uses CookieRequestCultureProvider)
        ctxHe.add_cookies([{
            "name": ".AspNetCore.Culture",
            "value": "c=he-IL|uic=he-IL",
            "domain": "localhost",
            "path": "/",
        }])
        pageHe = ctxHe.new_page()
        login(pageHe, USER_A, PW)
        dir_attr = pageHe.evaluate("document.documentElement.getAttribute('dir')")
        if dir_attr == 'rtl':
            ok(f"O. Hebrew RTL mode active (dir={dir_attr})")
        else:
            fail("O. Hebrew RTL mode", f"dir={dir_attr}")
        # Open picker in RTL
        open_picker(pageHe)
        ok("O. Picker opens in Hebrew RTL mode")
        ctxHe.close()
    except Exception as e:
        fail("O. Hebrew RTL", str(e))

    # ----------------------------------------------------- P. Ctrl+Click regression
    try:
        pageA.goto(BASE + "/", wait_until="networkidle")
        pageA.locator(".sidebar-brand").first.click(modifiers=["Control"])
        pageA.wait_for_timeout(1500)
        has_game = pageA.evaluate("!!window.ShiftSwapGame")
        if has_game:
            ok("P. Ctrl+Click still loads ShiftSwapGame")
        else:
            fail("P. ShiftSwap regression", "window.ShiftSwapGame missing")
    except Exception as e:
        fail("P. ShiftSwap regression", str(e))

    browser.close()

# Cleanup DB state
reset_user_theme(USER_A)
reset_user_theme(USER_B)

# Summary
print("\n" + "="*70)
for r,n in results:
    prefix = "✅" if r == "PASS" else "❌"
    print(f"{prefix} [{r}] {n}")
fails = sum(1 for r,_ in results if r == "FAIL")
passes = sum(1 for r,_ in results if r == "PASS")
print(f"\n{passes} passed, {fails} failed / {passes+fails} total")
raise SystemExit(1 if fails else 0)
