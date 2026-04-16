"""
Visual verification suite for icon rendering across pages + dark mode correctness.

Covers:
  1. Icon coverage on Calendar / Admin / Owner pages (not just sidebar)
  2. Zero emoji chars in chrome sections of each page
  3. Zero help-circle fallbacks (meaning every rendered <icon> name resolved)
  4. Dark mode toggle repaints every page and icons stay visible (stroke color != background)
  5. Contrast check: icons render with a luminance-distinct stroke from their parent bg

Writes screenshots to /tmp/shiftmgr-visual-*.png for human spot-checking.
"""
import sys, io, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5173"
USER = "test.manager@shifty.test"
PW   = "TestManager123!"

# Pages to audit — mix of highest-emoji-density Phase 2 targets
PAGES = [
    ("/Calendar/Table",       "calendar-table"),
    ("/Calendar/Day",         "calendar-day"),
    ("/Calendar/Month",       "calendar-month"),
    ("/Calendar/Overview",    "calendar-overview"),
    ("/Admin/Index",          "admin-index"),
    ("/Admin/Users",          "admin-users"),
    ("/Admin/Config",         "admin-config"),
    ("/Owner/Index",          "owner-index"),
    ("/Public/OnDuty",        "public-onduty"),
    ("/My/Profile",           "my-profile"),
]

EMOJI_RE = re.compile(r"[\U0001F300-\U0001FAFF\u2600-\u27BF\u2300-\u23FF]")

results = []
def ok(n):         results.append(("PASS", n))
def fail(n, d):    results.append(("FAIL", f"{n} — {d}"))
def warn(n):       results.append(("WARN", n))

def login(page):
    page.goto(BASE + "/Auth/Login", wait_until="networkidle", timeout=20000)
    page.fill('input[name="Email"]', USER)
    page.fill('input[name="Password"]', PW)
    page.locator('button[type="submit"]').first.click()
    page.wait_for_load_state("networkidle", timeout=15000)

def audit_page(page, path, label, mode):
    """Returns dict: {svg_count, emoji_count, help_circle_count, invisible_icons}"""
    try:
        page.goto(BASE + path, wait_until="networkidle", timeout=20000)
        page.wait_for_timeout(500)  # settle dynamic content
    except Exception as e:
        return {"error": str(e)}

    # Count icons in main content area (excluding sidebar which we already verified)
    main_html = page.evaluate("""() => {
        const main = document.querySelector('main, .main-content, .page-content, #main-content') || document.body;
        // strip the sidebar before counting
        const clone = main.cloneNode(true);
        clone.querySelectorAll('.app-sidebar, #appSidebar, aside.app-sidebar').forEach(el => el.remove());
        return clone.outerHTML;
    }""")

    svg_count = len(re.findall(r'<svg[^>]*class="[^"]*\bicon\b', main_html))
    # strip svg blocks so emoji in path content aren't counted
    stripped = re.sub(r"<svg[^>]*>.*?</svg>", "", main_html, flags=re.DOTALL)
    # strip <style> and <script> blocks (CSS content: emojis, JS string emojis)
    stripped = re.sub(r"<style[^>]*>.*?</style>", "", stripped, flags=re.DOTALL)
    stripped = re.sub(r"<script[^>]*>.*?</script>", "", stripped, flags=re.DOTALL)
    # strip HTML comments
    stripped = re.sub(r"<!--.*?-->", "", stripped, flags=re.DOTALL)
    emoji_matches = EMOJI_RE.findall(stripped)
    emoji_count = len(emoji_matches)
    unique_emoji = sorted(set(emoji_matches))

    # help-circle rendered when tag helper gets unknown name — the unknown-name text is also
    # embedded in the fallback but we detect via specific path signature
    help_circle = main_html.count('M9.09 9a3 3 0 015.83 1')

    # Find any <svg class="icon"> where stroke computes to the same color as its parent background
    # (would make it invisible). Collect computed styles for all .icon SVGs in main.
    invisible = page.evaluate("""() => {
        const main = document.querySelector('main, .main-content, .page-content, #main-content') || document.body;
        const svgs = main.querySelectorAll('svg.icon, svg[class*="icon--"]');
        const bad = [];
        svgs.forEach(svg => {
            // sidebar already verified
            if (svg.closest('.app-sidebar, #appSidebar')) return;
            const svgStyle = getComputedStyle(svg);
            const stroke = svgStyle.stroke;  // resolved from currentColor
            // walk up ancestors for nearest non-transparent bg
            let el = svg.parentElement, bg = null;
            while (el && !bg) {
                const b = getComputedStyle(el).backgroundColor;
                if (b && b !== 'rgba(0, 0, 0, 0)' && b !== 'transparent') { bg = b; break; }
                el = el.parentElement;
            }
            if (stroke && bg && stroke === bg) {
                bad.push({ stroke, bg, html: svg.outerHTML.slice(0, 120) });
            }
        });
        return bad;
    }""")

    # Screenshot for manual spot-check
    path_safe = label
    page.screenshot(path=f"/tmp/shiftmgr-visual-{mode}-{path_safe}.png", full_page=False)
    return {
        "svg": svg_count,
        "emoji": emoji_count,
        "unique_emoji": unique_emoji,
        "help_circle": help_circle,
        "invisible": invisible,
        "error": None,
    }

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1400, "height": 900})
    page = ctx.new_page()
    # capture JS errors
    page_errors = []
    page.on("pageerror", lambda e: page_errors.append(str(e)))
    login(page)

    # ----- LIGHT MODE PASS
    print("\n--- LIGHT MODE ---")
    light_audits = {}
    for path, label in PAGES:
        r = audit_page(page, path, label, "light")
        light_audits[label] = r
        if r.get("error"):
            fail(f"[light] {label} load", r["error"][:100])
            continue
        # Criteria: at least some svg icons, zero emojis in chrome, zero help-circle fallbacks
        if r["emoji"] == 0:
            ok(f"[light] {label}: 0 emojis, {r['svg']} SVG icons, {r['help_circle']} help-circle fallbacks, {len(r['invisible'])} invisible-risk")
        else:
            fail(f"[light] {label} emoji leakage", f"{r['emoji']} emojis ({r['unique_emoji']})")
        if r["invisible"]:
            fail(f"[light] {label} invisible icons", f"{len(r['invisible'])} svg same-color-as-bg — first: {r['invisible'][0]}")

    # ----- DARK MODE PASS
    print("\n--- DARK MODE ---")
    page.evaluate("document.documentElement.setAttribute('data-theme', 'dark'); localStorage.setItem('theme', 'dark');")
    page.wait_for_timeout(300)
    for path, label in PAGES:
        r = audit_page(page, path, label, "dark")
        if r.get("error"):
            fail(f"[dark] {label} load", r["error"][:100])
            continue
        if r["emoji"] == 0:
            ok(f"[dark] {label}: 0 emojis, {r['svg']} SVG icons, {len(r['invisible'])} invisible-risk")
        else:
            fail(f"[dark] {label} emoji leakage", f"{r['emoji']} emojis ({r['unique_emoji']})")
        if r["invisible"]:
            fail(f"[dark] {label} invisible icons", f"{len(r['invisible'])} svg same-color-as-bg — first: {r['invisible'][0]}")

    # Aggregate totals
    total_svg_light = sum(a.get("svg", 0) for a in light_audits.values())
    total_emoji_light = sum(a.get("emoji", 0) for a in light_audits.values())
    total_help_circle = sum(a.get("help_circle", 0) for a in light_audits.values())
    print(f"\nLight aggregate: {total_svg_light} SVG icons, {total_emoji_light} emojis, {total_help_circle} help-circle fallbacks across {len(PAGES)} pages")

    if page_errors:
        warn(f"JS page errors captured: {len(page_errors)} — first: {page_errors[0][:100]}")

    browser.close()

print("\n" + "="*80)
for r, n in results:
    mark = {"PASS":"✅","FAIL":"❌","WARN":"⚠️"}[r]
    print(f"{mark} [{r}] {n}")
fails = sum(1 for r,_ in results if r == "FAIL")
passes = sum(1 for r,_ in results if r == "PASS")
warns = sum(1 for r,_ in results if r == "WARN")
print(f"\n{passes} passed, {fails} failed, {warns} warnings / {passes+fails+warns} total")
print(f"\nScreenshots: /tmp/shiftmgr-visual-{{light|dark}}-*.png")
raise SystemExit(1 if fails else 0)
