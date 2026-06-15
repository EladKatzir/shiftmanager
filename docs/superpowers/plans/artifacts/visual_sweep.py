"""
Final verification visual sweep for sub-project C (Admin UI fixes).
Tests: /Admin/Users (table-responsive, no breakout, sticky header)
       /Admin/Analytics (donut center text readable, hole-fill disc)
       /Calendar/Shifts (control page — layout not broken)
"""
import sys
import json
from playwright.sync_api import sync_playwright

BASE_URL = "http://localhost:5000"
ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
LOGIN_EMAIL = "owner2@test"
LOGIN_PASSWORD = "Test1234!"

results = []

def log(msg):
    print(msg, flush=True)
    results.append(msg)

def login(page):
    page.goto(f"{BASE_URL}/Auth/Login", wait_until="networkidle")
    page.fill('input[name="email"], input[type="email"], #email, [name="Email"]', LOGIN_EMAIL)
    page.fill('input[type="password"], #password, [name="Password"]', LOGIN_PASSWORD)
    page.click('button[type="submit"], input[type="submit"]')
    page.wait_for_load_state("networkidle")
    log(f"  Login done — URL: {page.url}")

def set_theme(page, dark=False):
    if dark:
        page.evaluate("""
            document.documentElement.setAttribute('data-bs-theme','dark');
            document.documentElement.classList.add('dark');
            document.documentElement.setAttribute('data-theme','dark');
        """)
    else:
        page.evaluate("""
            document.documentElement.removeAttribute('data-bs-theme');
            document.documentElement.classList.remove('dark');
            document.documentElement.setAttribute('data-theme','light');
        """)
    page.wait_for_timeout(300)

def set_rtl(page, rtl=False):
    if rtl:
        page.evaluate("""
            document.documentElement.setAttribute('dir','rtl');
            document.documentElement.setAttribute('lang','he');
        """)
    else:
        page.evaluate("""
            document.documentElement.setAttribute('dir','ltr');
            document.documentElement.setAttribute('lang','en');
        """)
    page.wait_for_timeout(300)

def check_no_breakout(page, label):
    result = page.evaluate("""
        () => ({
            scrollWidth: document.documentElement.scrollWidth,
            clientWidth: document.documentElement.clientWidth,
            diff: document.documentElement.scrollWidth - document.documentElement.clientWidth
        })
    """)
    ok = result['diff'] <= 1
    status = "PASS" if ok else "FAIL"
    log(f"    [{status}] No-breakout check ({label}): scrollWidth={result['scrollWidth']} clientWidth={result['clientWidth']} diff={result['diff']}")
    return ok

def check_table_responsive(page):
    present = page.evaluate("() => document.querySelector('.table-responsive') !== null")
    status = "PASS" if present else "FAIL"
    log(f"    [{status}] .table-responsive present: {present}")
    return present

def check_sticky_header(page):
    pos = page.evaluate("""
        () => {
            const th = document.querySelector('thead th, .sticky-header, th[style*="sticky"]');
            if (!th) return null;
            return getComputedStyle(th).position;
        }
    """)
    # Also check via class
    sticky_any = page.evaluate("""
        () => {
            const els = document.querySelectorAll('th');
            for (const el of els) {
                if (getComputedStyle(el).position === 'sticky') return true;
            }
            return false;
        }
    """)
    status = "PASS" if sticky_any else "WARN"
    log(f"    [{status}] Sticky header th found: {sticky_any} (first th position: {pos})")
    return sticky_any

def check_internal_scroll(page, label):
    result = page.evaluate("""
        () => {
            const tr = document.querySelector('.table-responsive');
            if (!tr) return {found: false};
            return {
                found: true,
                scrollWidth: tr.scrollWidth,
                clientWidth: tr.clientWidth,
                overflowX: getComputedStyle(tr).overflowX,
                canScroll: tr.scrollWidth > tr.clientWidth
            };
        }
    """)
    if not result.get('found'):
        log(f"    [FAIL] .table-responsive not found for internal scroll check ({label})")
        return False
    status = "PASS" if result.get('canScroll') else "INFO"
    log(f"    [{status}] Internal scroll ({label}): scrollWidth={result['scrollWidth']} clientWidth={result['clientWidth']} canScroll={result['canScroll']} overflowX={result['overflowX']}")
    return True

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    context = browser.new_context(viewport={"width": 1440, "height": 900})
    page = context.new_page()

    # --- LOGIN ---
    log("=== LOGIN ===")
    login(page)

    # ============================================================
    # /Admin/Users checks
    # ============================================================
    log("\n=== /Admin/Users ===")

    modes = [
        ("light-ltr", False, False, 1440),
        ("dark-ltr",  True,  False, 1440),
        ("heb-rtl",   False, True,  1440),
        ("light-ltr", False, False, 820),
        ("light-ltr", False, False, 375),
    ]

    for (label, dark, rtl, width) in modes:
        log(f"\n  -- /Admin/Users {label} width={width} --")
        page.set_viewport_size({"width": width, "height": 900})
        page.goto(f"{BASE_URL}/Admin/Users", wait_until="networkidle")
        set_theme(page, dark=dark)
        set_rtl(page, rtl=rtl)
        page.wait_for_timeout(500)

        fname = f"final-admin-users-{label}-{width}.png"
        fpath = f"{ARTIFACTS}\\{fname}"
        page.screenshot(path=fpath, full_page=True)
        log(f"    Screenshot: {fname}")

        check_no_breakout(page, f"{label}/{width}")
        check_table_responsive(page)
        if width == 1440:
            check_sticky_header(page)
        if width in (820, 375):
            check_internal_scroll(page, f"{label}/{width}")

    # ============================================================
    # /Admin/Analytics with data
    # ============================================================
    log("\n=== /Admin/Analytics ===")
    page.set_viewport_size({"width": 1440, "height": 900})

    # Navigate with wide date range + molecule scope
    analytics_url = f"{BASE_URL}/Admin/Analytics?from=2024-01-01&to=2026-06-14&level=MoleculesInArea"

    analytics_modes = [
        ("light-ltr", False, False),
        ("dark-ltr",  True,  False),
        ("heb-rtl",   False, True),
    ]

    for (label, dark, rtl) in analytics_modes:
        log(f"\n  -- /Admin/Analytics {label} --")
        page.goto(analytics_url, wait_until="networkidle")
        set_theme(page, dark=dark)
        set_rtl(page, rtl=rtl)
        page.wait_for_timeout(800)

        # Check donut SVG exists
        donut_exists = page.evaluate("() => document.querySelector('svg .donut-ring, svg circle, .donut-chart, svg[class*=\"donut\"], .chart-donut') !== null")
        # More general: any svg with circles
        svg_exists = page.evaluate("() => document.querySelector('svg') !== null")
        circles = page.evaluate("() => document.querySelectorAll('svg circle').length")
        log(f"    SVG found: {svg_exists}, circles: {circles}, donut selector: {donut_exists}")

        # Check center value element
        center_info = page.evaluate("""
            () => {
                // Try multiple selectors for the center text
                const selectors = [
                    '.donut-center-val',
                    '.donut-center',
                    '[class*="donut-center"]',
                    'text.donut',
                    '.chart-center-value'
                ];
                for (const sel of selectors) {
                    const el = document.querySelector(sel);
                    if (el) {
                        const style = getComputedStyle(el);
                        return {
                            selector: sel,
                            text: el.textContent.trim(),
                            color: style.color,
                            fill: style.fill,
                            tagName: el.tagName
                        };
                    }
                }
                // Fallback: look in all text-ish elements near SVG
                const texts = document.querySelectorAll('text, .center-val, [data-donut]');
                if (texts.length > 0) {
                    const el = texts[0];
                    return { selector: 'fallback-text', text: el.textContent.trim(), tagName: el.tagName };
                }
                return null;
            }
        """)
        log(f"    Center element: {json.dumps(center_info)}")

        # Check hole-fill circle
        hole_fill = page.evaluate("""
            () => {
                const circles = document.querySelectorAll('svg circle');
                for (const c of circles) {
                    const fill = c.getAttribute('fill') || '';
                    const style = c.getAttribute('style') || '';
                    if (fill.includes('surface') || style.includes('surface') || fill.includes('var(--')) {
                        return { found: true, fill: fill, r: c.getAttribute('r') };
                    }
                }
                return { found: false, totalCircles: circles.length };
            }
        """)
        log(f"    Hole-fill circle: {json.dumps(hole_fill)}")

        # Total value non-zero check
        total_text = page.evaluate("""
            () => {
                const els = document.querySelectorAll('.donut-center-val, .donut-center, [class*="total"], .chart-total');
                for (const el of els) {
                    const t = el.textContent.trim();
                    if (t && /\\d/.test(t)) return t;
                }
                // Try SVG text elements
                const texts = document.querySelectorAll('text');
                for (const t of texts) {
                    const content = t.textContent.trim();
                    if (/^\\d+$/.test(content) && parseInt(content) > 0) return content;
                }
                return null;
            }
        """)
        log(f"    Total text value found: {total_text}")

        fname = f"final-admin-analytics-{label}-1440.png"
        fpath = f"{ARTIFACTS}\\{fname}"
        page.screenshot(path=fpath, full_page=True)
        log(f"    Screenshot: {fname}")

    # ============================================================
    # /Calendar/Shifts (control page)
    # ============================================================
    log("\n=== /Calendar/Shifts (control) ===")
    page.set_viewport_size({"width": 1440, "height": 900})
    set_theme(page, dark=False)
    set_rtl(page, rtl=False)
    page.goto(f"{BASE_URL}/Calendar/Shifts", wait_until="networkidle")
    page.wait_for_timeout(1000)

    cal_exists = page.evaluate("""
        () => {
            return {
                excelCalendar: document.querySelector('.excel-calendar') !== null,
                calPage: document.querySelector('.cal-page') !== null,
                anyTable: document.querySelector('table') !== null,
                title: document.title
            };
        }
    """)
    log(f"  Calendar elements: {json.dumps(cal_exists)}")

    # Check no JS errors by capturing console — we already took screenshot approach
    broken = page.evaluate("""
        () => {
            // Page is broken if it has an error message or no content
            const body = document.body.innerText;
            return body.includes('An unhandled exception') || body.includes('500') || body.length < 100;
        }
    """)
    log(f"  Page broken: {broken}")

    fname = "final-calendar-shifts-light-ltr-1440.png"
    fpath = f"{ARTIFACTS}\\{fname}"
    page.screenshot(path=fpath, full_page=True)
    log(f"  Screenshot: {fname}")

    browser.close()

log("\n=== SWEEP COMPLETE ===")
for r in results:
    pass  # already printed

# Summary
print("\n\n--- RESULT SUMMARY ---")
fails = [r for r in results if "[FAIL]" in r]
warns = [r for r in results if "[WARN]" in r]
passes = [r for r in results if "[PASS]" in r]
print(f"PASS: {len(passes)}  WARN: {len(warns)}  FAIL: {len(fails)}")
if fails:
    print("FAILURES:")
    for f in fails:
        print(f"  {f}")
if warns:
    print("WARNINGS:")
    for w in warns:
        print(f"  {w}")
