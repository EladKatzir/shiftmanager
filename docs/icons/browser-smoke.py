"""
Browser smoke tests for the Lucide migration + theme picker features.

Prereq: ShiftManager running on http://localhost:5173

Verifies:
  1. Login page renders (basic liveness)
  2. Sidebar renders <svg> icons (not emoji chars)
  3. Shift+Click on .sidebar-brand opens the Appearance picker
  4. Picking a preset color recolors the page (--primary token changes)
  5. Saving the theme returns HTTP 200 and sets the `theme` cookie
  6. Ctrl+Click still opens the ShiftSwap game (regression check)

All screenshots are written to /tmp/shiftmgr-smoke-*.png.
"""
import sys, io, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

from playwright.sync_api import sync_playwright, TimeoutError as PWTimeout

BASE = "http://localhost:5173"
USER = "test.manager@shifty.test"
PW   = "TestManager123!"

results = []
def ok(name):   results.append(("PASS", name))
def fail(name, detail):
    results.append(("FAIL", f"{name} — {detail}"))

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()
    page.on("console", lambda msg: print(f"[console:{msg.type}] {msg.text}") if msg.type in ("error", "warning") else None)

    # 1. Login page loads
    try:
        page.goto(BASE + "/Auth/Login", wait_until="networkidle", timeout=20000)
        title = page.title()
        has_email = page.locator('input[type="email"], input[name="Input.Email"], input#Email').count() > 0
        if has_email:
            ok(f"login page renders (title={title!r})")
        else:
            page.screenshot(path="/tmp/shiftmgr-smoke-1-login.png", full_page=True)
            fail("login page renders", "no email input found")
    except Exception as e:
        fail("login page renders", str(e))

    # 2. Attempt login
    logged_in = False
    try:
        email_sel = 'input[name="Input.Email"]' if page.locator('input[name="Input.Email"]').count() else 'input[type="email"]'
        pw_sel    = 'input[name="Input.Password"]' if page.locator('input[name="Input.Password"]').count() else 'input[type="password"]'
        page.fill(email_sel, USER)
        page.fill(pw_sel, PW)
        page.locator('button[type="submit"], input[type="submit"]').first.click()
        page.wait_for_load_state("networkidle", timeout=15000)
        if "/Auth/Login" not in page.url:
            logged_in = True
            ok(f"login succeeds (landed on {page.url})")
        else:
            page.screenshot(path="/tmp/shiftmgr-smoke-2-loginfail.png", full_page=True)
            # Check for error banner text
            body_text = page.locator("body").inner_text()[:400]
            fail("login succeeds", f"still on /Auth/Login, body: {body_text!r}")
    except Exception as e:
        fail("login succeeds", str(e))

    if not logged_in:
        # Can still test static icon rendering on login page
        svg_count = page.locator('svg.icon, svg[class*="icon--"]').count()
        if svg_count > 0:
            ok(f"login page renders {svg_count} Lucide <svg> icons")
        else:
            fail("login page renders Lucide icons", "0 svg.icon found")
        page.screenshot(path="/tmp/shiftmgr-smoke-3-loginicons.png", full_page=True)
        browser.close()
        for r,n in results:
            print(f"[{r}] {n}")
        raise SystemExit(0 if not any(r=="FAIL" for r,_ in results) else 1)

    # 3. Sidebar renders <svg> icons, not emojis
    try:
        sidebar = page.locator(".app-sidebar, #appSidebar, aside")
        sidebar.first.wait_for(state="visible", timeout=5000)
        sidebar_html = sidebar.first.inner_html()
        svg_icons_in_sidebar = len(re.findall(r"<svg[^>]*class=\"[^\"]*icon", sidebar_html))
        # Count emoji codepoints in sidebar (excluding SVG paths)
        emoji_re = re.compile(r"[\U0001F300-\U0001FAFF\u2600-\u27BF\u2300-\u23FF]")
        # strip svg blocks first so path content isn't counted
        stripped = re.sub(r"<svg[^>]*>.*?</svg>", "", sidebar_html, flags=re.DOTALL)
        emoji_in_sidebar = len(emoji_re.findall(stripped))
        # Correctness criterion is "zero emojis in sidebar" — having some SVGs
        # is a bonus but not required (some nav sections are text-only).
        if emoji_in_sidebar == 0:
            ok(f"sidebar has 0 emojis (+ {svg_icons_in_sidebar} Lucide SVG icons)")
        else:
            fail("sidebar icon migration", f"svg={svg_icons_in_sidebar}, emoji={emoji_in_sidebar}")
        page.screenshot(path="/tmp/shiftmgr-smoke-4-sidebar.png", full_page=False)
    except Exception as e:
        fail("sidebar icon migration", str(e))

    # 4. Shift+Click on sidebar-brand opens the theme picker
    try:
        brand = page.locator(".sidebar-brand, .brand").first
        brand.wait_for(state="visible", timeout=3000)
        brand.click(modifiers=["Shift"])
        page.wait_for_selector(".theme-picker", state="visible", timeout=5000)
        ok("Shift+Click opens Appearance picker")
        page.screenshot(path="/tmp/shiftmgr-smoke-5-picker.png", full_page=False)
    except Exception as e:
        page.screenshot(path="/tmp/shiftmgr-smoke-5-pickerfail.png", full_page=True)
        fail("Shift+Click opens picker", str(e))
        browser.close()
        for r,n in results: print(f"[{r}] {n}")
        raise SystemExit(1)

    # 5. Pick a preset and verify --primary changes
    try:
        primary_before = page.evaluate("getComputedStyle(document.documentElement).getPropertyValue('--primary').trim()")
        presets = page.locator(".theme-picker__preset")
        preset_count = presets.count()
        if preset_count < 4:
            fail("preset swatches render", f"only {preset_count} presets found")
        else:
            ok(f"{preset_count} preset swatches render")
        # Pick a preset that's NOT whatever --primary currently is, so we can prove the recolor.
        # PRESETS order in theme-picker.js: Navy, Sky, Forest, Coral, Plum, Emerald, Sunset, Slate
        hex_to_idx = { '#1e3a5f': 0, '#5b9bd5': 1, '#2f6f4a': 2, '#e5735b': 3, '#7b3f7a': 4, '#0e9f6e': 5, '#d97706': 6, '#475569': 7 }
        cur_idx = hex_to_idx.get(primary_before.lower(), 0)
        target_idx = (cur_idx + 3) % 8
        presets.nth(target_idx).click()
        page.wait_for_timeout(400)
        primary_after = page.evaluate("getComputedStyle(document.documentElement).getPropertyValue('--primary').trim()")
        if primary_after and primary_after.lower() != primary_before.lower():
            ok(f"preset #{target_idx} recolored --primary ({primary_before} → {primary_after})")
        else:
            fail("preset recolors --primary", f"before={primary_before} after={primary_after} (clicked preset #{target_idx})")
    except Exception as e:
        fail("preset interaction", str(e))

    # 6. Save theme → POST /Api/My/Theme returns 200
    try:
        with page.expect_response(lambda r: "/Api/My/Theme" in r.url and r.request.method == "POST", timeout=10000) as resp_ctx:
            page.locator(".theme-picker__save").click()
        resp = resp_ctx.value
        if resp.status == 200:
            ok(f"POST /Api/My/Theme → 200")
        else:
            fail("theme save status", f"got {resp.status}")
        # Verify cookie set
        cookies = ctx.cookies()
        theme_cookie = next((c for c in cookies if c["name"] == "theme"), None)
        if theme_cookie:
            ok(f"theme cookie set: {theme_cookie['value'][:40]}…")
        else:
            fail("theme cookie set", "no `theme` cookie in context")
    except Exception as e:
        fail("theme save", str(e))

    # 7. Close picker + Ctrl+Click = ShiftSwap regression
    try:
        # Close modal (click close button or outside)
        close_btn = page.locator('.theme-picker__close')
        if close_btn.count() > 0:
            close_btn.click()
            page.wait_for_selector(".theme-picker", state="hidden", timeout=3000)
        # Ctrl+Click the brand
        page.locator(".sidebar-brand, .brand").first.click(modifiers=["Control"])
        page.wait_for_timeout(1500)  # lazy-load of game JS
        # ShiftSwap game appends a modal or canvas — check for any of its known selectors
        game_visible = page.evaluate("""() => {
            return !!document.querySelector('.shift-swap-game, #shiftSwapModal, .game-board, [class*="shift-swap"]');
        }""")
        if game_visible:
            ok("Ctrl+Click still triggers ShiftSwap (regression OK)")
        else:
            # Maybe game loaded but selector is different — check for window.ShiftSwapGame
            has_game = page.evaluate("!!window.ShiftSwapGame")
            if has_game:
                ok("Ctrl+Click loaded window.ShiftSwapGame")
            else:
                fail("ShiftSwap regression", "no game modal or window.ShiftSwapGame")
    except Exception as e:
        fail("ShiftSwap regression", str(e))

    browser.close()

# Summary
print("\n" + "="*60)
for r,n in results:
    print(f"[{r}] {n}")
fails = sum(1 for r,_ in results if r == "FAIL")
passes = sum(1 for r,_ in results if r == "PASS")
print(f"\n{passes} passed, {fails} failed")
raise SystemExit(1 if fails else 0)
