"""
ShiftManager -- Phase D: Secondary/Edge-Case Role Navigation Tests
Tests: Assigner, Trainee, NoGrants, Locked, Deactivated
These roles verify security boundary correctness, not just happy-path access.

Results appended to nav_test_results/ (shared with Phase C report).
"""

import os
import re
import json
import time
from datetime import datetime
from playwright.sync_api import sync_playwright, Page

BASE_URL = "http://localhost:5000"
RESULTS_DIR = os.path.join(os.path.dirname(__file__), "nav_test_results")
os.makedirs(RESULTS_DIR, exist_ok=True)

PASS = "PASS"
FAIL = "FAIL"
WARN = "WARN"
SKIP = "SKIP"

all_results = []


# ============================================================
# HELPERS (same as nav_full_test.py)
# ============================================================

def take_screenshot(page: Page, name: str) -> str:
    safe = re.sub(r'[^a-zA-Z0-9_\-]', '_', name)[:80]
    path = os.path.join(RESULTS_DIR, f"phaseD_{safe}.png")
    try:
        page.screenshot(path=path, full_page=False, timeout=5000)
    except Exception:
        pass
    return path


def record(role: str, molecule: str, workflow: str, url: str, status: str,
           final_url: str = "", notes: str = "", screenshot_path: str = ""):
    all_results.append({
        "role": role,
        "molecule": molecule,
        "workflow": workflow,
        "url": url,
        "final_url": final_url or url,
        "status": status,
        "notes": notes,
        "screenshot": os.path.basename(screenshot_path) if screenshot_path else "",
    })


def classify_page(page: Page) -> tuple[str, str]:
    url = page.url
    title = page.title()

    if "/Auth/Login" in url:
        return FAIL, "Redirected to login"

    if "/AccessDenied" in url or "/StatusCode/403" in url or "/StatusCode?code=403" in url:
        return FAIL, "Access Denied (403)"

    if "/StatusCode/404" in url or "/StatusCode?code=404" in url or "404" in title:
        return FAIL, "404 Not Found"

    if "/StatusCode/500" in url or "/StatusCode?code=500" in url or "500" in title:
        return FAIL, "500 Server Error"

    try:
        body = page.locator("body").inner_text(timeout=3000)
        low = body.lower()
        if "access denied" in low and "you don" in low:
            return FAIL, "Access Denied (inline)"
        if "an unhandled exception" in low or "system.exception" in low:
            return FAIL, "Unhandled exception on page"
    except Exception:
        pass

    return PASS, "OK"


def attempt_login(page: Page, email: str, password: str) -> bool:
    """Attempt login. Returns True if landed somewhere other than login page."""
    try:
        page.goto(f"{BASE_URL}/Auth/Login", wait_until="networkidle", timeout=20000)
        page.fill('input[name="Email"]', email)
        page.fill('input[name="Password"]', password)
        page.click('button[type="submit"]')
        page.wait_for_load_state("networkidle", timeout=20000)
        return "/Auth/Login" not in page.url
    except Exception as e:
        print(f"    [LOGIN ERROR] {email}: {e}")
        return False


def logout(page: Page):
    try:
        page.goto(f"{BASE_URL}/Auth/Logout", wait_until="domcontentloaded", timeout=10000)
    except Exception:
        pass


def visit(page: Page, role: str, molecule: str, wf_id: str, wf_name: str, url: str) -> str:
    label = f"{role}_{molecule}_{wf_id}"
    print(f"    [{wf_id}] {wf_name} -> {url}")
    try:
        page.goto(f"{BASE_URL}{url}", wait_until="networkidle", timeout=18000)
        status, notes = classify_page(page)
        shot = take_screenshot(page, label)
        final = page.url.replace(BASE_URL, "")
        record(role, molecule, f"{wf_id}: {wf_name}", url, status, final, notes, shot)
        icon = "[OK]" if status == PASS else "[!!]"
        print(f"         {icon} {status} -- {notes}")
        return status
    except Exception as e:
        notes = str(e)[:120]
        record(role, molecule, f"{wf_id}: {wf_name}", url, FAIL, "", notes)
        print(f"         [!!] FAIL -- {notes}")
        return FAIL


def visit_expect_blocked(page: Page, role: str, molecule: str,
                          wf_id: str, wf_name: str, url: str) -> str:
    """Navigate to url expecting access to be DENIED. PASS = correctly blocked."""
    label = f"{role}_{molecule}_{wf_id}_blocked"
    print(f"    [{wf_id}] {wf_name} -> {url} (expect: blocked)")
    try:
        page.goto(f"{BASE_URL}{url}", wait_until="networkidle", timeout=18000)
        status, notes = classify_page(page)
        is_blocked = status == FAIL
        r_status = PASS if is_blocked else FAIL
        r_note = "correctly blocked" if is_blocked else f"INCORRECTLY accessible ({notes})"
        shot = take_screenshot(page, label)
        final = page.url.replace(BASE_URL, "")
        record(role, molecule, f"{wf_id}: {wf_name}", url, r_status, final, r_note, shot)
        icon = "[OK]" if r_status == PASS else "[!!]"
        print(f"         {icon} {r_status} -- {r_note}")
        return r_status
    except Exception as e:
        notes = str(e)[:120]
        record(role, molecule, f"{wf_id}: {wf_name}", url, FAIL, "", notes)
        print(f"         [!!] FAIL -- {notes}")
        return FAIL


# ============================================================
# ROLE: LOCKED (should not be able to log in)
# ============================================================

def run_locked(page: Page):
    role = "Locked"
    molecule = "Oren"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Account locked via LockoutEnd+24h")
    print(f"{'='*60}")

    logged_in = attempt_login(page, "locked@test", "Test1234!")
    on_login_page = "/Auth/Login" in page.url

    # Correct behavior: login BLOCKED (stays on login page)
    if not logged_in and on_login_page:
        shot = take_screenshot(page, f"{role}_{molecule}_login_blocked")
        record(role, molecule, "LOGIN_BLOCKED: Login correctly rejected",
               "/Auth/Login", PASS, "/Auth/Login",
               "Account locked — login correctly rejected", shot)
        print(f"    [OK] PASS -- Account correctly blocked from logging in")
    else:
        shot = take_screenshot(page, f"{role}_{molecule}_login_unexpected")
        record(role, molecule, "LOGIN_BLOCKED: Login should have been rejected",
               "/Auth/Login", FAIL, page.url.replace(BASE_URL, ""),
               "SECURITY ISSUE: Locked account was able to log in!", shot)
        print(f"    [!!] FAIL -- SECURITY ISSUE: locked account logged in!")
        logout(page)


# ============================================================
# ROLE: DEACTIVATED (IsActive=false, filtered out of login query)
# ============================================================

def run_deactivated(page: Page):
    role = "Deactivated"
    molecule = "Oren"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | IsActive=false")
    print(f"{'='*60}")

    logged_in = attempt_login(page, "deactivated@test", "Test1234!")
    on_login_page = "/Auth/Login" in page.url

    if not logged_in and on_login_page:
        shot = take_screenshot(page, f"{role}_{molecule}_login_blocked")
        record(role, molecule, "LOGIN_BLOCKED: Deactivated account correctly rejected",
               "/Auth/Login", PASS, "/Auth/Login",
               "Deactivated account — login correctly rejected", shot)
        print(f"    [OK] PASS -- Deactivated account correctly blocked")
    else:
        shot = take_screenshot(page, f"{role}_{molecule}_login_unexpected")
        record(role, molecule, "LOGIN_BLOCKED: Deactivated account should have been rejected",
               "/Auth/Login", FAIL, page.url.replace(BASE_URL, ""),
               "SECURITY ISSUE: Deactivated account was able to log in!", shot)
        print(f"    [!!] FAIL -- SECURITY ISSUE: deactivated account logged in!")
        logout(page)


# ============================================================
# ROLE: NO GRANTS (no role template; bare authenticated user)
# ============================================================

def run_nogrants(page: Page):
    role = "NoGrants"
    molecule = "Oren"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | No role template, zero grants")
    print(f"{'='*60}")

    if not attempt_login(page, "nogrants@test", "Test1234!"):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        print(f"    [!!] FAIL -- Could not log in")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in successfully", screenshot_path=shot)
    print(f"    [OK] PASS -- Logged in")

    # My/* pages: only [Authorize] — should be accessible
    print(f"\n    --- Personal pages (expect: accessible) ---")
    for wf_id, wf_name, url in [
        ("NG1", "My Profile",           "/My/Profile"),
        ("NG2", "My Settings",          "/My/Settings"),
        ("NG3", "My Help",              "/My/Help"),
        ("NG4", "My Requests",          "/My/Requests"),
        ("NG5", "Notification Center",  "/My/NotificationCenter"),
    ]:
        visit(page, role, molecule, wf_id, wf_name, url)

    # Calendar pages use [Authorize] only — any authenticated user can visit (data is filtered by grants/company)
    print(f"\n    --- Calendar pages ([Authorize] only, expect: accessible) ---")
    for wf_id, wf_name, url in [
        ("NG_B1", "Shift Calendar",     "/Calendar/Shifts"),
        ("NG_B2", "Company Overview",   "/Calendar/Overview"),
        ("NG_B3", "Chores Calendar",    "/Calendar/Chores"),
        ("NG_B4", "OnDuty Calendar",    "/Calendar/OnDuty"),
    ]:
        visit(page, role, molecule, wf_id, wf_name, url)

    # Grant-policy pages: should be blocked
    print(f"\n    --- Grant-policy pages (expect: blocked) ---")
    for wf_id, wf_name, url in [
        ("NG_B5", "Command Center",     "/Admin/Index"),
        ("NG_B6", "Admin Users",        "/Admin/Users"),
        ("NG_B7", "Owner Hub",          "/Owner/Index"),
        ("NG_B8", "Shift Table",        "/Calendar/Table"),
    ]:
        visit_expect_blocked(page, role, molecule, wf_id, wf_name, url)

    logout(page)


# ============================================================
# ROLE: TRAINEE (Employee-like, no RequestSwap)
# ============================================================

def run_trainee(page: Page):
    role = "Trainee"
    molecule = "Oren"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Employee grants minus RequestSwap")
    print(f"{'='*60}")

    if not attempt_login(page, "trainee.alhut@test", "Test1234!"):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        print(f"    [!!] FAIL -- Could not log in")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)
    print(f"    [OK] PASS -- Logged in")

    # Employee-equivalent access
    print(f"\n    --- Employee-level pages (expect: accessible) ---")
    for wf_id, wf_name, url in [
        ("T1",  "Shift Calendar",       "/Calendar/Shifts"),
        ("T2",  "Company Overview",     "/Calendar/Overview"),
        ("T3",  "My Requests",          "/My/Requests"),
        ("T4",  "Chores Calendar",      "/Calendar/Chores"),
        ("T5",  "OnDuty Calendar",      "/Calendar/OnDuty"),
        ("T6",  "My Profile",           "/My/Profile"),
        ("T7",  "My Settings",          "/My/Settings"),
        ("T8",  "My Help",              "/My/Help"),
        ("T9",  "Notification Center",  "/My/NotificationCenter"),
        ("T10", "My Groups/Team",       "/MyTeam/Index"),
    ]:
        visit(page, role, molecule, wf_id, wf_name, url)

    # Organization Hub requires ViewHierarchy — Trainee HAS this grant, so access is correct
    print(f"\n    --- ViewHierarchy-gated (Trainee has this grant, expect: accessible) ---")
    visit(page, role, molecule, "T_VH", "Organization Hub", "/Admin/Organization")

    # Admin pages: should be blocked (Trainee has no admin grants)
    print(f"\n    --- Admin pages (expect: blocked) ---")
    for wf_id, wf_name, url in [
        ("T_B1", "Command Center",      "/Admin/Index"),
        ("T_B2", "Admin Users",         "/Admin/Users"),
        ("T_B3", "Shift Table",         "/Calendar/Table"),
        ("T_B4", "Owner Hub",           "/Owner/Index"),
        ("T_B5", "Assignments Manager", "/Assignments/Manage"),
    ]:
        visit_expect_blocked(page, role, molecule, wf_id, wf_name, url)

    logout(page)


# ============================================================
# ROLE: ASSIGNER (Employee + AssignChores ETM)
# ============================================================

def run_assigner(page: Page):
    role = "Assigner"
    molecule = "Oren"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Employee grants + AssignChores (molecule-wide)")
    print(f"{'='*60}")

    if not attempt_login(page, "assigner.oren@test", "Test1234!"):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        print(f"    [!!] FAIL -- Could not log in")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)
    print(f"    [OK] PASS -- Logged in")

    # Employee-equivalent access
    print(f"\n    --- Employee-level pages (expect: accessible) ---")
    for wf_id, wf_name, url in [
        ("AS1",  "Shift Calendar",      "/Calendar/Shifts"),
        ("AS2",  "Company Overview",    "/Calendar/Overview"),
        ("AS3",  "My Requests",         "/My/Requests"),
        ("AS4",  "Chores Calendar",     "/Calendar/Chores"),
        ("AS5",  "OnDuty Calendar",     "/Calendar/OnDuty"),
        ("AS6",  "My Profile",          "/My/Profile"),
        ("AS7",  "My Settings",         "/My/Settings"),
        ("AS8",  "My Help",             "/My/Help"),
        ("AS9",  "Notification Center", "/My/NotificationCenter"),
        ("AS10", "My Groups/Team",      "/MyTeam/Index"),
    ]:
        visit(page, role, molecule, wf_id, wf_name, url)

    # Organization Hub requires ViewHierarchy — Assigner HAS this grant, so access is correct
    print(f"\n    --- ViewHierarchy-gated (Assigner has this grant, expect: accessible) ---")
    visit(page, role, molecule, "AS_VH", "Organization Hub", "/Admin/Organization")

    # Admin pages: should be blocked (Assigner has no admin grants)
    print(f"\n    --- Admin pages (expect: blocked) ---")
    for wf_id, wf_name, url in [
        ("AS_B1", "Command Center",     "/Admin/Index"),
        ("AS_B2", "Admin Users",        "/Admin/Users"),
        ("AS_B3", "Shift Table",        "/Calendar/Table"),
        ("AS_B4", "Owner Hub",          "/Owner/Index"),
        ("AS_B5", "Assignments Manager","/Assignments/Manage"),
    ]:
        visit_expect_blocked(page, role, molecule, wf_id, wf_name, url)

    logout(page)


# ============================================================
# HTML REPORT
# ============================================================

def generate_report():
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    total = len(all_results)
    passes = sum(1 for r in all_results if r["status"] == PASS)
    fails = sum(1 for r in all_results if r["status"] == FAIL)

    rows = ""
    for r in all_results:
        color = {"PASS": "#22c55e", "FAIL": "#ef4444", "WARN": "#f59e0b", "SKIP": "#6b7280"}.get(r["status"], "#6b7280")
        badge = f'<span style="background:{color};color:white;padding:2px 8px;border-radius:4px;font-size:12px;font-weight:600">{r["status"]}</span>'
        shot_link = f'<a href="{r["screenshot"]}" target="_blank">[img]</a>' if r["screenshot"] else ""
        rows += f"""
        <tr>
          <td>{r['role']}</td>
          <td>{r['molecule']}</td>
          <td>{r['workflow']}</td>
          <td><code>{r['url']}</code></td>
          <td>{badge}</td>
          <td style="color:#6b7280;font-size:13px">{r['notes']}</td>
          <td>{shot_link}</td>
        </tr>"""

    html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>ShiftManager Phase D — Edge Case Role Tests</title>
<style>
  body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 0; background: #f8fafc; color: #1e293b; }}
  .header {{ background: #7c3aed; color: white; padding: 2rem; }}
  .header h1 {{ margin: 0 0 0.5rem; font-size: 1.75rem; }}
  .stats {{ display: flex; gap: 1rem; padding: 1.5rem 2rem; }}
  .stat {{ background: white; border-radius: 12px; padding: 1rem 1.5rem; flex: 1; box-shadow: 0 1px 3px rgba(0,0,0,.1); }}
  .stat-val {{ font-size: 2rem; font-weight: 700; }}
  .stat-label {{ color: #6b7280; font-size: 0.875rem; }}
  .green {{ color: #22c55e; }} .red {{ color: #ef4444; }}
  .section {{ margin: 0 2rem 2rem; }}
  h2 {{ font-size: 1.25rem; margin: 0 0 1rem; padding-bottom: 0.5rem; border-bottom: 1px solid #e5e7eb; }}
  table {{ width: 100%; border-collapse: collapse; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,.1); }}
  th {{ background: #f1f5f9; padding: 0.75rem 1rem; text-align: left; font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.05em; color: #64748b; }}
  td {{ padding: 0.65rem 1rem; border-top: 1px solid #f1f5f9; font-size: 0.875rem; vertical-align: middle; }}
  tr:hover td {{ background: #f8fafc; }}
  code {{ background: #f1f5f9; padding: 2px 6px; border-radius: 4px; font-size: 12px; }}
</style>
</head>
<body>
<div class="header">
  <h1>Phase D — Edge Case Role Navigation Tests</h1>
  <p>Locked · Deactivated · NoGrants · Trainee · Assigner &nbsp;|&nbsp; {timestamp}</p>
</div>
<div class="stats">
  <div class="stat"><div class="stat-val">{total}</div><div class="stat-label">Total Checks</div></div>
  <div class="stat"><div class="stat-val green">{passes}</div><div class="stat-label">Pass</div></div>
  <div class="stat"><div class="stat-val red">{fails}</div><div class="stat-label">Fail</div></div>
</div>
<div class="section">
  <h2>All Results</h2>
  <table>
    <thead><tr><th>Role</th><th>Context</th><th>Workflow</th><th>URL</th><th>Status</th><th>Notes</th><th>Screenshot</th></tr></thead>
    <tbody>{rows}</tbody>
  </table>
</div>
</body>
</html>"""

    report_path = os.path.join(RESULTS_DIR, "report_phase_d.html")
    with open(report_path, "w", encoding="utf-8") as f:
        f.write(html)
    return report_path


# ============================================================
# MAIN
# ============================================================

def main():
    print(f"\n{'#'*60}")
    print(f"# ShiftManager Phase D Test -- {datetime.now().strftime('%H:%M:%S')}")
    print(f"# Roles: Locked, Deactivated, NoGrants, Trainee, Assigner")
    print(f"{'#'*60}\n")

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()

        run_locked(page)
        run_deactivated(page)
        run_nogrants(page)
        run_trainee(page)
        run_assigner(page)

        browser.close()

    passes = sum(1 for r in all_results if r["status"] == PASS)
    fails = sum(1 for r in all_results if r["status"] == FAIL)

    print(f"\n{'='*60}")
    print(f"  Total: {len(all_results)}  |  PASS: {passes}  |  FAIL: {fails}")

    report_path = generate_report()
    print(f"  HTML report: {report_path}")
    print(f"{'='*60}\n")

    failures = [r for r in all_results if r["status"] == FAIL]
    if failures:
        print(f"  FAILURES ({len(failures)}):")
        for r in failures:
            print(f"    [!!] [{r['role']}] {r['workflow']} -- {r['notes']}")

    json_path = os.path.join(RESULTS_DIR, "results_phase_d.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(all_results, f, indent=2)

    return 1 if fails > 0 else 0


if __name__ == "__main__":
    exit(main())
