"""Browser verification for unified calendar palette.

Captures screenshots of each palette-affected page in:
  - Light mode + English
  - Dark mode + English
  - Light mode + Hebrew RTL
  - Dark mode + Hebrew RTL

Also reads computed CSS tokens from a calendar page to confirm the
retuned palette is live.
"""
import sys
from pathlib import Path
from playwright.sync_api import sync_playwright, Page

BASE = "http://localhost:5000"
OUT = Path(__file__).parent.parent / "screenshots"
OUT.mkdir(exist_ok=True)

USERS = [
    ("admin@local", "admin123", "owner"),       # Owner: full access
    ("mgr.br.hir@test", "Test1234!", "brdir"),  # BRDirector: verify collaborative OnCall
]


def login(page: Page, email: str, pw: str):
    page.goto(f"{BASE}/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.fill('input[name="Email"]', email)
    page.fill('input[name="Password"]', pw)
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")


def set_theme(page: Page, dark: bool):
    page.evaluate(
        "(isDark) => { document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light'); "
        "localStorage.setItem('theme', isDark ? 'dark' : 'light'); }",
        dark,
    )


def set_culture(page: Page, he: bool):
    # Cookie-based culture switching per MEMORY
    page.context.add_cookies([{
        "name": ".AspNetCore.Culture",
        "value": "c=he-IL|uic=he-IL" if he else "c=en-US|uic=en-US",
        "url": BASE,
    }])


def snap(page: Page, url: str, name: str, dark: bool = False):
    page.goto(f"{BASE}{url}")
    page.wait_for_load_state("networkidle")
    # Sync desired theme by clicking the site's actual themeToggle button if needed.
    current = page.evaluate("document.documentElement.getAttribute('data-theme') || 'light'")
    want = "dark" if dark else "light"
    if current != want:
        try:
            page.click("#themeToggle", timeout=2000)
            page.wait_for_timeout(400)
        except Exception:
            page.evaluate(
                "(t) => document.documentElement.setAttribute('data-theme', t)",
                want,
            )
    page.wait_for_timeout(400)
    shot = OUT / f"{name}.png"
    page.screenshot(path=str(shot), full_page=False)
    print(f"  -> {shot.name}")


def dump_tokens(page: Page) -> dict:
    return page.evaluate("""() => {
        const s = getComputedStyle(document.documentElement);
        const names = [
            '--shift-morning','--shift-morning-soft',
            '--shift-afternoon','--shift-afternoon-soft',
            '--shift-night','--shift-night-soft',
            '--shift-home','--shift-home-soft',
            '--shift-hakam','--shift-hakam-soft',
            '--onduty','--onduty-soft',
            '--chore','--chore-soft',
            '--vacation','--vacation-soft'
        ];
        const out = {};
        for (const n of names) out[n] = s.getPropertyValue(n).trim();
        return out;
    }""")


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1400, "height": 900})
        page = context.new_page()

        # ========== Owner: verify tokens + capture all pages ==========
        print("=== Owner session ===")
        login(page, "admin@local", "admin123")

        # Confirm the live CSS tokens match the retuned palette
        page.goto(f"{BASE}/Calendar/Shifts")
        page.wait_for_load_state("networkidle")
        light_tokens = dump_tokens(page)
        print("\nLight-mode tokens:")
        for k, v in light_tokens.items():
            print(f"  {k}: {v}")

        set_theme(page, dark=True)
        page.reload()
        page.wait_for_load_state("networkidle")
        dark_tokens = dump_tokens(page)
        print("\nDark-mode tokens:")
        for k, v in dark_tokens.items():
            print(f"  {k}: {v}")

        set_theme(page, dark=False)

        pages = [
            ("/Calendar/Shifts", "shifts"),
            ("/Calendar/Chores", "chores"),
            ("/Calendar/OnCall", "oncall"),
            ("/Calendar/Overview", "overview"),
            ("/Admin/Organization/AreaPalette?areaId=1", "areapalette"),
            ("/Owner/Hub/Grants", "grants"),
        ]

        # light + en
        set_culture(page, he=False)
        set_theme(page, dark=False)
        print("\n--- light + en ---")
        for url, name in pages:
            snap(page, url, f"{name}_light_en")

        # dark + en
        print("--- dark + en ---")
        for url, name in pages:
            snap(page, url, f"{name}_dark_en", dark=True)

        # light + he
        set_culture(page, he=True)
        set_theme(page, dark=False)
        print("--- light + he ---")
        for url, name in pages:
            snap(page, url, f"{name}_light_he")

        # dark + he
        print("--- dark + he ---")
        for url, name in pages:
            snap(page, url, f"{name}_dark_he", dark=True)

        # ========== BRDirector: OnCall editability ==========
        print("\n=== BRDirector session (OnCall edit rights) ===")
        context2 = browser.new_context(viewport={"width": 1400, "height": 900})
        page2 = context2.new_page()
        login(page2, "mgr.br.hir@test", "Test1234!")
        set_culture(page2, he=False)
        set_theme(page2, dark=False)
        snap(page2, "/Calendar/OnCall", "oncall_brdir_light")
        # Check CanEdit rendered as editable (not read-only)
        editable = page2.evaluate("""() => {
            const all = Array.from(document.querySelectorAll('button, a'));
            const quickEntry = all.find(el => (el.textContent || '').includes('Quick Entry'));
            const filter = all.find(el => (el.textContent || '').includes('Filter'));
            return {
                hasQuickEntry: !!quickEntry,
                hasFilter: !!filter,
                isReadOnly: document.body.classList.contains('read-only') || document.body.hasAttribute('data-readonly'),
                url: location.pathname
            };
        }""")
        print(f"BRDirector OnCall editable markers: {editable}")

        browser.close()

        # Expected retuned palette values
        expected_light = {
            "--shift-morning": "#FFB547",
            "--shift-afternoon": "#66B2A0",
            "--shift-night": "#3D5A80",
            "--shift-home": "#B088C7",
            "--onduty": "#C97064",
            "--chore": "#E6C447",
            "--vacation": "#7FA8C9",
        }
        print("\n=== Palette correctness vs expected ===")
        ok = True
        for k, exp in expected_light.items():
            actual = light_tokens.get(k, "").lower()
            expl = exp.lower()
            # --onduty is an alias; may resolve via var() chain, accept either expected or hakam fallback
            match = actual == expl or (k == "--onduty" and actual in (expl, "#c97064"))
            status = "OK " if match else "FAIL"
            print(f"  {status} {k}: got {actual!r}, expected {expl!r}")
            if not match:
                ok = False
        sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
