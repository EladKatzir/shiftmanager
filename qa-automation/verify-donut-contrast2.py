"""
Verify donut center text readability in light mode, dark mode, and Hebrew RTL.
Uses known-working URL with non-zero data: level=MoleculesInArea gives total=163.
"""
import os
import json
from playwright.sync_api import sync_playwright

BASE_URL = "http://localhost:5000"
EMAIL = "owner2@test"
PASSWORD = "Test1234!"
ARTIFACTS_DIR = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
# URL that produces non-zero data (total=163)
ANALYTICS_URL = f"{BASE_URL}/Admin/Analytics?from=2024-01-01&to=2026-06-14&level=MoleculesInArea"

os.makedirs(ARTIFACTS_DIR, exist_ok=True)

def login(page):
    page.goto(f"{BASE_URL}/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.fill("input[name='Email']", EMAIL)
    page.fill("input[name='Password']", PASSWORD)
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    print(f"After login: {page.url}")

def measure_stack(page):
    """Run the elementFromPoint probe at the center of .donut-center-val."""
    return page.evaluate("""
        () => {
            const val = document.querySelector('.donut-center-val');
            if (!val) return { error: 'no .donut-center-val' };
            const r = val.getBoundingClientRect();
            const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
            const center = document.querySelector('.donut-center');
            const prev = center.style.pointerEvents;
            center.style.pointerEvents = 'none';
            const stack = document.elementsFromPoint(cx, cy).map(el => ({
                tag: el.tagName,
                cls: el.getAttribute && el.getAttribute('class'),
                fill: el.getAttribute && el.getAttribute('fill'),
                bg: getComputedStyle(el).backgroundColor
            }));
            center.style.pointerEvents = prev;
            const valColor = getComputedStyle(val).color;
            const centerText = val.textContent.trim();
            return { valColor, centerText, stackBehindCenter: stack };
        }
    """)

def analyze_readability(result, mode_label):
    """Analyze whether text is readable given the stack measurement."""
    val_color = result.get("valColor", "")
    center_text = result.get("centerText", "")
    stack = result.get("stackBehindCenter", [])

    print(f"\n--- {mode_label} ---")
    print(f"  Center text: '{center_text}'")
    print(f"  Text color: {val_color}")

    # Find the first opaque background behind the text
    # Skip the text element itself (index 0 should be .donut-center or a text-level el)
    # We look for either an SVG fill or a CSS background that is not transparent
    behind_bg = None
    behind_el = None
    for el in stack:
        bg = el.get("bg", "")
        fill = el.get("fill", "")
        cls = el.get("cls", "") or ""

        # Skip fully transparent ones
        if bg in ("rgba(0, 0, 0, 0)", "", None) and fill in ("none", "", None):
            continue
        if "donut-center" in cls:
            # This is our own overlay, skip
            continue
        behind_bg = bg if bg not in ("rgba(0, 0, 0, 0)", "", None) else f"fill:{fill}"
        behind_el = el
        break

    print(f"  First opaque element behind: {behind_el}")
    print(f"  Effective background: {behind_bg}")

    # Parse RGB values
    def parse_rgb(s):
        if not s:
            return None
        import re
        m = re.search(r'rgb\((\d+),\s*(\d+),\s*(\d+)\)', s)
        if m:
            return int(m.group(1)), int(m.group(2)), int(m.group(3))
        return None

    text_rgb = parse_rgb(val_color)
    bg_rgb = parse_rgb(str(behind_bg))

    if text_rgb and bg_rgb:
        # Relative luminance
        def rel_lum(r, g, b):
            def linearize(v):
                v /= 255
                return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
            return 0.2126 * linearize(r) + 0.7152 * linearize(g) + 0.0722 * linearize(b)

        L_text = rel_lum(*text_rgb)
        L_bg = rel_lum(*bg_rgb)
        L_lighter = max(L_text, L_bg)
        L_darker = min(L_text, L_bg)
        contrast = (L_lighter + 0.05) / (L_darker + 0.05)
        print(f"  Contrast ratio: {contrast:.2f}:1 (WCAG AA requires 4.5:1 for normal text, 3:1 for large)")

        if contrast >= 4.5:
            readable = "PASS (WCAG AA)"
        elif contrast >= 3.0:
            readable = "MARGINAL (meets large-text AA, 3:1)"
        elif contrast < 1.5:
            readable = "FAIL - NEAR INVISIBLE (contrast < 1.5)"
        else:
            readable = f"FAIL (contrast {contrast:.2f} below 3:1)"
        print(f"  Readability: {readable}")
        return contrast, readable
    else:
        print(f"  Could not parse RGB values (text={text_rgb}, bg={bg_rgb})")
        # If text is white and bg contains 255,255,255 it's a fail
        is_white_text = text_rgb == (255, 255, 255) if text_rgb else "255, 255, 255" in val_color
        is_white_bg = "255, 255, 255" in str(behind_bg)
        if is_white_text and is_white_bg:
            print(f"  Readability: FAIL - white on white")
            return 1.0, "FAIL - white on white"
        return None, "UNKNOWN"


with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)

    # ============================================================
    # LIGHT MODE
    # ============================================================
    print("\n====== LIGHT MODE ======")
    page = browser.new_page(viewport={"width": 1280, "height": 900})
    login(page)

    page.goto(ANALYTICS_URL)
    page.wait_for_load_state("networkidle")

    donut_val = page.locator(".donut-center-val").first.inner_text().strip()
    print(f"Donut total: {donut_val}")

    # Ensure light mode (default)
    page.evaluate("""
        () => {
            document.documentElement.classList.remove('dark');
            document.documentElement.removeAttribute('data-theme');
        }
    """)
    page.wait_for_timeout(300)

    light_result = measure_stack(page)
    light_contrast, light_readable = analyze_readability(light_result, "LIGHT MODE")

    page.screenshot(
        path=os.path.join(ARTIFACTS_DIR, "analytics-review-light-data.png"),
        full_page=False
    )
    print("Screenshot: analytics-review-light-data.png")

    # Close-up of donut
    donut_wrap = page.locator(".donut-wrap").first
    if donut_wrap.count() > 0:
        donut_wrap.screenshot(
            path=os.path.join(ARTIFACTS_DIR, "analytics-donut-light-closeup.png")
        )
        print("Screenshot: analytics-donut-light-closeup.png")

    # ============================================================
    # DARK MODE
    # ============================================================
    print("\n====== DARK MODE ======")
    page.evaluate("""
        () => {
            document.documentElement.classList.add('dark');
            document.documentElement.setAttribute('data-theme', 'dark');
        }
    """)
    page.wait_for_timeout(500)

    dark_result = measure_stack(page)
    dark_contrast, dark_readable = analyze_readability(dark_result, "DARK MODE")

    page.screenshot(
        path=os.path.join(ARTIFACTS_DIR, "analytics-review-dark-data.png"),
        full_page=False
    )
    print("Screenshot: analytics-review-dark-data.png")
    donut_wrap = page.locator(".donut-wrap").first
    if donut_wrap.count() > 0:
        donut_wrap.screenshot(
            path=os.path.join(ARTIFACTS_DIR, "analytics-donut-dark-closeup.png")
        )
        print("Screenshot: analytics-donut-dark-closeup.png")

    # ============================================================
    # RTL (Hebrew) — Light base
    # ============================================================
    print("\n====== HEBREW RTL (Light) ======")
    page.evaluate("""
        () => {
            document.documentElement.classList.remove('dark');
            document.documentElement.removeAttribute('data-theme');
            document.documentElement.setAttribute('dir', 'rtl');
            document.documentElement.setAttribute('lang', 'he');
        }
    """)
    page.wait_for_timeout(500)

    rtl_result = measure_stack(page)
    rtl_contrast, rtl_readable = analyze_readability(rtl_result, "RTL (Hebrew, Light)")

    page.screenshot(
        path=os.path.join(ARTIFACTS_DIR, "analytics-review-rtl-data.png"),
        full_page=False
    )
    print("Screenshot: analytics-review-rtl-data.png")
    donut_wrap = page.locator(".donut-wrap").first
    if donut_wrap.count() > 0:
        donut_wrap.screenshot(
            path=os.path.join(ARTIFACTS_DIR, "analytics-donut-rtl-closeup.png")
        )
        print("Screenshot: analytics-donut-rtl-closeup.png")

    browser.close()

    # ============================================================
    # FINAL VERDICT
    # ============================================================
    print("\n" + "="*60)
    print("FINAL VERDICT")
    print("="*60)
    print(f"Data scope: {ANALYTICS_URL}")
    print(f"Donut total (non-zero): {donut_val}")
    print(f"Light mode contrast: {light_contrast} → {light_readable}")
    print(f"Dark mode contrast: {dark_contrast} → {dark_readable}")
    print(f"RTL light contrast: {rtl_contrast} → {rtl_readable}")

    all_pass = all(
        r and ("PASS" in r or "MARGINAL" in r)
        for r in [light_readable, dark_readable, rtl_readable]
    )

    if all_pass:
        print("\nOVERALL: PASS — donut center text is readable in all modes")
    else:
        print("\nOVERALL: FAIL — donut center text has contrast issues in one or more modes")
        print("Details of full light-mode stack:")
        print(json.dumps(light_result.get("stackBehindCenter", [])[:5], indent=2))
