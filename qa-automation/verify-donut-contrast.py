"""
Verify donut center text readability in light mode, dark mode, and Hebrew RTL.
Logs into /Admin/Analytics as owner2@test, finds non-zero data, measures what's behind
the center text, and takes screenshots.
"""
import os
import json
from playwright.sync_api import sync_playwright

BASE_URL = "http://localhost:5000"
EMAIL = "owner2@test"
PASSWORD = "Test1234!"
ARTIFACTS_DIR = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"

os.makedirs(ARTIFACTS_DIR, exist_ok=True)

def login(page):
    page.goto(f"{BASE_URL}/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.fill("input[name='Email']", EMAIL)
    page.fill("input[name='Password']", PASSWORD)
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    print(f"After login: {page.url}")

def get_donut_total(page):
    """Return the integer value shown in .donut-center-val, or -1 if not found."""
    try:
        val = page.locator(".donut-center-val").first.inner_text().strip()
        return int(val.replace(",", ""))
    except Exception as e:
        print(f"  Could not read donut val: {e}")
        return -1

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

def try_find_nonzero_data(page):
    """
    Navigate to /Admin/Analytics and try different parameter combos to get total > 0.
    Returns the total found and description of what worked, or (0, 'none worked').
    """
    # Try different scopes and periods
    combos = [
        # scope, year, month (0=all), worktype
        ("area", None, None, "0"),
        ("molecule", None, None, "0"),
        ("company", None, None, "0"),
    ]

    # First just load the page with defaults
    page.goto(f"{BASE_URL}/Admin/Analytics")
    page.wait_for_load_state("networkidle")
    page.screenshot(path=os.path.join(ARTIFACTS_DIR, "analytics-initial.png"))
    print("Screenshot: analytics-initial.png")

    total = get_donut_total(page)
    print(f"  Default page donut total: {total}")
    if total > 0:
        return total, "default parameters"

    # Check the current URL to understand available params
    print(f"  Current URL: {page.url}")

    # Try to find scope/period selects
    content = page.content()

    # Try changing the period dropdown to wider range
    period_select = page.locator("select[name='Period']")
    scope_select = page.locator("select[name='Scope']")
    worktype_select = page.locator("select[name='WorkType']")

    has_period = period_select.count() > 0
    has_scope = scope_select.count() > 0
    has_worktype = worktype_select.count() > 0

    print(f"  Has period select: {has_period}, scope: {has_scope}, worktype: {has_worktype}")

    if has_period:
        periods = period_select.locator("option").all()
        print(f"  Period options: {[p.get_attribute('value') for p in periods]}")
    if has_scope:
        scopes = scope_select.locator("option").all()
        print(f"  Scope options: {[s.get_attribute('value') for s in scopes]}")
    if has_worktype:
        worktypes = worktype_select.locator("option").all()
        print(f"  WorkType options: {[w.get_attribute('value') for w in worktypes]}")

    # Try each combo
    if has_scope:
        scope_vals = [s.get_attribute("value") for s in scope_select.locator("option").all()]
        print(f"  Trying scope combos: {scope_vals}")
        for sv in scope_vals:
            scope_select.select_option(sv)
            page.wait_for_load_state("networkidle")
            total = get_donut_total(page)
            print(f"  scope={sv} => total={total}")
            if total > 0:
                return total, f"scope={sv}"

    # Try worktype=All with each scope
    if has_worktype and has_scope:
        wt_vals = [w.get_attribute("value") for w in worktype_select.locator("option").all()]
        scope_vals = [s.get_attribute("value") for s in scope_select.locator("option").all()]
        for wv in wt_vals:
            for sv in scope_vals:
                worktype_select.select_option(wv)
                scope_select.select_option(sv)
                page.wait_for_load_state("networkidle")
                total = get_donut_total(page)
                print(f"  worktype={wv} scope={sv} => total={total}")
                if total > 0:
                    return total, f"worktype={wv} scope={sv}"

    # Try directly with query params
    test_urls = [
        f"{BASE_URL}/Admin/Analytics?Scope=molecule&WorkType=0",
        f"{BASE_URL}/Admin/Analytics?Scope=area&WorkType=0",
        f"{BASE_URL}/Admin/Analytics?Scope=company&WorkType=0",
        f"{BASE_URL}/Admin/Analytics?WorkType=0",
    ]
    for url in test_urls:
        page.goto(url)
        page.wait_for_load_state("networkidle")
        total = get_donut_total(page)
        print(f"  URL {url} => total={total}")
        if total > 0:
            return total, f"URL: {url}"

    return 0, "none worked"


def enable_dark_mode(page):
    page.evaluate("""
        () => {
            document.documentElement.classList.add('dark');
            document.documentElement.setAttribute('data-theme', 'dark');
        }
    """)

def enable_light_mode(page):
    page.evaluate("""
        () => {
            document.documentElement.classList.remove('dark');
            document.documentElement.setAttribute('data-theme', 'light');
        }
    """)

def enable_rtl(page):
    page.evaluate("""
        () => {
            document.documentElement.setAttribute('dir', 'rtl');
            document.documentElement.setAttribute('lang', 'he');
        }
    """)

def enable_ltr(page):
    page.evaluate("""
        () => {
            document.documentElement.setAttribute('dir', 'ltr');
            document.documentElement.setAttribute('lang', 'en');
        }
    """)


with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)

    # ---- LIGHT MODE VERIFICATION ----
    print("\n=== LIGHT MODE ===")
    page = browser.new_page(viewport={"width": 1280, "height": 900})
    login(page)

    total, combo_desc = try_find_nonzero_data(page)
    print(f"\nFound non-zero data: total={total}, combo={combo_desc}")

    if total > 0:
        # Ensure light mode
        enable_light_mode(page)
        page.wait_for_timeout(300)

        # Measure what's behind the center
        result = measure_stack(page)
        print("\n=== Light mode stack measurement ===")
        print(json.dumps(result, indent=2))

        # Screenshot
        page.screenshot(
            path=os.path.join(ARTIFACTS_DIR, "analytics-review-light-data.png"),
            full_page=False
        )
        print("Screenshot saved: analytics-review-light-data.png")

        # Focus screenshot on just the donut area
        donut_wrap = page.locator(".donut-wrap").first
        if donut_wrap.count() > 0:
            donut_wrap.screenshot(
                path=os.path.join(ARTIFACTS_DIR, "analytics-donut-light-closeup.png")
            )
            print("Screenshot saved: analytics-donut-light-closeup.png")
    else:
        print("WARNING: Could not find non-zero data!")
        page.screenshot(path=os.path.join(ARTIFACTS_DIR, "analytics-no-data.png"))

    # ---- DARK MODE VERIFICATION ----
    print("\n=== DARK MODE ===")
    enable_dark_mode(page)
    page.wait_for_timeout(500)

    dark_result = measure_stack(page)
    print("Dark mode stack measurement:")
    print(json.dumps(dark_result, indent=2))

    page.screenshot(
        path=os.path.join(ARTIFACTS_DIR, "analytics-review-dark-data.png"),
        full_page=False
    )
    print("Screenshot saved: analytics-review-dark-data.png")

    donut_wrap = page.locator(".donut-wrap").first
    if donut_wrap.count() > 0:
        donut_wrap.screenshot(
            path=os.path.join(ARTIFACTS_DIR, "analytics-donut-dark-closeup.png")
        )
        print("Screenshot saved: analytics-donut-dark-closeup.png")

    # ---- RTL (Hebrew) VERIFICATION ----
    print("\n=== HEBREW RTL MODE ===")
    enable_light_mode(page)
    enable_rtl(page)
    page.wait_for_timeout(500)

    rtl_result = measure_stack(page)
    print("RTL light mode stack measurement:")
    print(json.dumps(rtl_result, indent=2))

    page.screenshot(
        path=os.path.join(ARTIFACTS_DIR, "analytics-review-rtl-data.png"),
        full_page=False
    )
    print("Screenshot saved: analytics-review-rtl-data.png")

    donut_wrap = page.locator(".donut-wrap").first
    if donut_wrap.count() > 0:
        donut_wrap.screenshot(
            path=os.path.join(ARTIFACTS_DIR, "analytics-donut-rtl-closeup.png")
        )
        print("Screenshot saved: analytics-donut-rtl-closeup.png")

    browser.close()

    # Summary
    print("\n=== SUMMARY ===")
    print(f"Non-zero data found: {total > 0} (combo: {combo_desc})")
    if total > 0:
        val_color = result.get("valColor", "N/A")
        print(f"Light mode val color: {val_color}")
        stack = result.get("stackBehindCenter", [])
        if len(stack) > 1:
            behind = stack[1]  # index 0 is the text element itself
            print(f"Element immediately behind center text: tag={behind.get('tag')} cls={behind.get('cls')} fill={behind.get('fill')} bg={behind.get('bg')}")

        # Judge readability
        print("\nREADABILITY JUDGMENT:")
        # White text = rgb(255, 255, 255)
        is_white_text = "255, 255, 255" in val_color or "255,255,255" in val_color

        # Find the element directly behind (SVG circle or panel)
        behind_bg = None
        for el in stack[1:]:
            if el.get("bg") not in ("rgba(0, 0, 0, 0)", "transparent", None):
                behind_bg = el.get("bg")
                break
            if el.get("fill") and el.get("fill") not in ("none", None):
                behind_bg = f"fill:{el.get('fill')}"
                break

        print(f"  Text color: {val_color}")
        print(f"  Background behind text: {behind_bg}")

        if is_white_text:
            # Check if background behind is light
            if behind_bg and ("255, 255, 255" in str(behind_bg) or "rgb(255" in str(behind_bg)):
                print("  FAIL: WHITE text on WHITE background — INVISIBLE in light mode!")
            else:
                print(f"  Text is white; background behind is: {behind_bg}")
                print("  CHECKING if this is safe...")
        else:
            print(f"  Text color is NOT white: {val_color}")
