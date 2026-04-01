"""
ShiftManager -- Full Navigation & Workflow Playwright Test
Covers Phase C (priority roles) + spot checks for Shikma molecule.
Tests Phase A fixes: A1 (Org hub Roles/Grants buttons), A2 (People sidebar for Leads).
Results: nav_test_results/ (screenshots + HTML report)
"""

import os
import re
import json
import time
from datetime import datetime
from playwright.sync_api import sync_playwright, Page, Browser

BASE_URL = "http://localhost:5000"
RESULTS_DIR = os.path.join(os.path.dirname(__file__), "nav_test_results")
os.makedirs(RESULTS_DIR, exist_ok=True)

PASS = "PASS"
FAIL = "FAIL"
WARN = "WARN"
SKIP = "SKIP"

all_results = []


# ============================================================
# HELPERS
# ============================================================

def take_screenshot(page: Page, name: str) -> str:
    safe = re.sub(r'[^a-zA-Z0-9_\-]', '_', name)[:80]
    path = os.path.join(RESULTS_DIR, f"{safe}.png")
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


def login(page: Page, email: str, password: str) -> bool:
    """Log in and return True on success."""
    try:
        page.goto(f"{BASE_URL}/Auth/Login", wait_until="networkidle", timeout=20000)
        page.fill('input[name="Email"]', email)
        page.fill('input[name="Password"]', password)
        page.click('button[type="submit"]')
        page.wait_for_load_state("networkidle", timeout=20000)
        # Success: not on login page anymore
        return "/Auth/Login" not in page.url and "/Auth/GriffinCallback" not in page.url
    except Exception as e:
        print(f"    [LOGIN ERROR] {email}: {e}")
        return False


def logout(page: Page):
    """Log out via the logout endpoint."""
    try:
        page.goto(f"{BASE_URL}/Auth/Logout", wait_until="domcontentloaded", timeout=10000)
    except Exception:
        pass


def classify_page(page: Page) -> tuple[str, str]:
    """
    Returns (status, notes) by examining the page for common error patterns.
    """
    url = page.url
    title = page.title()

    if "/Auth/Login" in url:
        return FAIL, "Redirected to login (unauthenticated or session expired)"

    # AccessDeniedPath = "/AccessDenied" in Program.cs (no /Auth/ prefix)
    # StatusCode uses path-based routing: /StatusCode/403 /StatusCode/404 etc.
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


def visit(page: Page, role: str, molecule: str, workflow_id: str, workflow_name: str,
          url: str) -> str:
    """Navigate to url, evaluate status, screenshot, record. Returns status."""
    label = f"{role}_{molecule}_{workflow_id}"
    print(f"    [{workflow_id}] {workflow_name} -> {url}")
    try:
        page.goto(f"{BASE_URL}{url}", wait_until="networkidle", timeout=18000)
        status, notes = classify_page(page)
        shot = take_screenshot(page, label)
        final = page.url.replace(BASE_URL, "")
        record(role, molecule, f"{workflow_id}: {workflow_name}", url, status, final, notes, shot)
        icon = "[OK]" if status == PASS else "[!!]"
        print(f"         {icon} {status} -- {notes}")
        return status
    except Exception as e:
        notes = str(e)[:120]
        record(role, molecule, f"{workflow_id}: {workflow_name}", url, FAIL, "", notes)
        print(f"         [!!] FAIL -- {notes}")
        return FAIL


def check_sidebar(page: Page, role: str, molecule: str,
                  expected_hrefs: list[str], forbidden_hrefs: list[str]):
    """Verify sidebar links present/absent after login. Records each check."""
    print(f"    [Sidebar checks]")
    try:
        page.goto(f"{BASE_URL}/Home/Index", wait_until="networkidle", timeout=15000)
        sidebar_html = page.locator(".app-sidebar-nav").inner_html(timeout=5000)
    except Exception as e:
        record(role, molecule, "Sidebar HTML fetch", "/Home/Index", FAIL, notes=str(e)[:80])
        return

    for href in expected_hrefs:
        present = href.lower() in sidebar_html.lower()
        status = PASS if present else FAIL
        note = "found" if present else "MISSING from sidebar"
        record(role, molecule, f"Sidebar [OK] {href}", "/Home/Index", status, notes=note)
        icon = "[OK]" if status == PASS else "[!!]"
        print(f"         {icon} {href}: {note}")

    for href in forbidden_hrefs:
        present = href.lower() in sidebar_html.lower()
        status = PASS if not present else FAIL
        note = "correctly absent" if not present else "INCORRECTLY present in sidebar"
        record(role, molecule, f"Sidebar_absent {href}", "/Home/Index", status, notes=note)
        icon = "[OK]" if status == PASS else "[!!]"
        print(f"         {icon} {href}: {note}")


def check_org_hub_actions(page: Page, role: str, molecule: str,
                           expect_roles_btn: bool, expect_grants_btn: bool):
    """A1 fix: verify Roles/Grants buttons in Org hub Quick Actions."""
    print(f"    [A1 check] Organization hub Quick Actions")
    try:
        page.goto(f"{BASE_URL}/Admin/Organization", wait_until="networkidle", timeout=15000)
        status, notes = classify_page(page)
        if status == FAIL:
            record(role, molecule, "A1: Org Hub accessible", "/Admin/Organization",
                   FAIL, notes=notes)
            return

        html = page.locator(".quick-actions").inner_html(timeout=5000)

        # Roles button
        roles_present = "/Admin/Organization/Roles" in html
        r_status = PASS if roles_present == expect_roles_btn else FAIL
        r_note = ("present" if roles_present else "absent") + (
            " (expected)" if roles_present == expect_roles_btn else " (UNEXPECTED)"
        )
        record(role, molecule, "A1: Manage Roles btn in Quick Actions",
               "/Admin/Organization", r_status, notes=r_note)
        print(f"         {'[OK]' if r_status == PASS else '[!!]'} Roles btn: {r_note}")

        # Grants button
        grants_present = "/Admin/Organization/Grants" in html
        g_status = PASS if grants_present == expect_grants_btn else FAIL
        g_note = ("present" if grants_present else "absent") + (
            " (expected)" if grants_present == expect_grants_btn else " (UNEXPECTED)"
        )
        record(role, molecule, "A1: Manage Grants btn in Quick Actions",
               "/Admin/Organization", g_status, notes=g_note)
        print(f"         {'[OK]' if g_status == PASS else '[!!]'} Grants btn: {g_note}")

    except Exception as e:
        record(role, molecule, "A1: Org Hub Quick Actions check", "/Admin/Organization",
               FAIL, notes=str(e)[:100])


# ============================================================
# ROLE WORKFLOW DEFINITIONS
# ============================================================

EMPLOYEE_WORKFLOWS = [
    ("E1",  "View shift schedule",         "/Calendar/Shifts"),
    ("E2",  "Company overview",            "/Calendar/Overview"),
    ("E3",  "Requests page",               "/My/Requests"),
    ("E5",  "Chores calendar",             "/Calendar/Chores"),
    ("E6",  "On-Duty calendar",            "/Calendar/OnDuty"),
    ("E7",  "My Profile",                  "/My/Profile"),
    ("E8",  "My Settings",                 "/My/Settings"),
    ("E9",  "Notification Center",         "/My/NotificationCenter"),
    ("E10", "Help hub",                    "/My/Help"),
    ("E13", "My Groups/Team",              "/MyTeam/Index"),
]

LEAD_WORKFLOWS = [
    ("L1",  "Command Center",              "/Admin/Index"),
    ("L2",  "Shift Table",                 "/Calendar/Table"),
    ("L3",  "Shift Plans (Programs)",      "/Owner/Programs"),
    ("L4",  "Blueprints",                  "/Owner/Blueprints"),
    ("L5",  "Requests (admin view)",       "/Requests/Index"),
    ("L7",  "Analytics",                   "/Admin/Analytics"),
    ("L8",  "Audit Log",                   "/Admin/AuditLog"),
    ("L9",  "People / Users",              "/Admin/Users"),
    ("L10", "Chores calendar",             "/Calendar/Chores"),
    ("L11", "Organization hub",            "/Admin/Organization"),
    ("L14", "Assignments Manager",         "/Assignments/Manage"),
    ("L15", "Manage Roles (via Org hub)",  "/Admin/Organization/Roles"),
]

BRDIRECTOR_WORKFLOWS = [
    ("B1",  "Command Center",              "/Admin/Index"),
    ("B2",  "Manage Users (People)",       "/Admin/Users"),
    ("B3",  "Companies",                   "/Admin/Companies"),
    ("B4",  "Shift Table",                 "/Calendar/Table"),
    ("B5",  "Requests (approval)",         "/Requests/Index"),
    ("B7",  "Analytics",                   "/Admin/Analytics"),
    ("B8",  "Audit Log",                   "/Admin/AuditLog"),
    ("B9",  "Blueprints",                  "/Owner/Blueprints"),
    ("B10", "Organization hub",            "/Admin/Organization"),
    ("B11", "Manage Roles",                "/Admin/Organization/Roles"),
    ("B12", "Manage Grants",               "/Admin/Organization/Grants"),
]

AREAADMIN_WORKFLOWS = [
    ("A_1", "Command Center",              "/Admin/Index"),
    ("A_2", "Director: View As Mode",      "/Director/ViewAsMode"),
    ("A_3", "Director: Notification Hub",  "/Director/NotificationHub"),
    ("A_4", "Director: Company Filter",    "/Director/CompanyFilter"),
    ("A_5", "Organization hub",            "/Admin/Organization"),
    ("A_6", "Hierarchy tree",             "/Admin/Organization"),
    ("A_7", "Job Types",                   "/Admin/Organization/JobTypes"),
    ("A_8", "Duty Types",                  "/Admin/Organization/DutyTypes"),
    ("A_9", "Home Types",                  "/Admin/HomeTypes"),
    ("A10", "Area settings",               "/Admin/Config"),
    ("A11", "Announcements",               "/Admin/Announcements"),
    ("A12", "Manage Areas",                "/Admin/Organization/Areas"),
    ("A13", "Manage Grants",               "/Admin/Organization/Grants"),
    ("A14", "Manage Roles",                "/Admin/Organization/Roles"),
    ("A15", "Shift Table",                 "/Calendar/Table"),
]

DEPTLEAD_WORKFLOWS = [
    ("D1",  "Command Center",              "/Admin/Index"),
    ("D2",  "Shift Table",                 "/Calendar/Table"),
    ("D3",  "Shift Plans (Programs)",      "/Owner/Programs"),
    ("D4",  "Blueprints",                  "/Owner/Blueprints"),
    ("D5",  "Requests (admin view)",       "/Requests/Index"),
    ("D6",  "Analytics",                   "/Admin/Analytics"),
    ("D7",  "Organization hub",            "/Admin/Organization"),
    ("D8",  "Chores calendar",             "/Calendar/Chores"),
    ("D9",  "People / Users",             "/Admin/Users"),
]

MOLADMIN_WORKFLOWS = [
    ("M1",  "Command Center",              "/Admin/Index"),
    ("M2",  "Shift Table",                 "/Calendar/Table"),
    ("M3",  "Shift Plans",                 "/Owner/Programs"),
    ("M4",  "Blueprints",                  "/Owner/Blueprints"),
    ("M5",  "Settings",                    "/Admin/Config"),
    ("M6",  "Announcements",              "/Admin/Announcements"),
    ("M7",  "Organization hub",            "/Admin/Organization"),
    ("M8",  "Manage Chore Types",          "/Admin/Organization/ChoreTypes"),
    # M9: DutyTypes requires ManageOnDutyTypes (area-level, AreaAdmin only) — MoleculeAdmin correctly denied
    ("M10", "Home Types",                  "/Admin/HomeTypes"),
    ("M11", "Analytics",                   "/Admin/Analytics"),
    ("M12", "Audit Log",                   "/Admin/AuditLog"),
    ("M13", "Manage Roles",                "/Admin/Organization/Roles"),
    ("M14", "Manage Grants",               "/Admin/Organization/Grants"),
]

DIRECTOR_WORKFLOWS = [
    ("Dir1", "Command Center",             "/Admin/Index"),
    ("Dir2", "Director: View As Mode",     "/Director/ViewAsMode"),
    ("Dir3", "Director: Notification Hub", "/Director/NotificationHub"),
    ("Dir4", "Director: Company Filter",   "/Director/CompanyFilter"),
    ("Dir5", "Announcements",              "/Admin/Announcements"),
    # Dir6: Areas requires EditArea (AreaAdmin only) — Director correctly denied; can use Org hub
    ("Dir6", "Organization hub",           "/Admin/Organization"),
    ("Dir7", "Analytics",                  "/Admin/Analytics"),
    ("Dir8", "People / Users",             "/Admin/Users"),
]

OWNER_WORKFLOWS = [
    ("O1",  "Command Center",              "/Admin/Index"),
    ("O2",  "Owner Hub",                   "/Owner/Index"),
    ("O3",  "Feature Flags",               "/Owner/FeatureFlags"),
    ("O4",  "System Health",               "/Owner/SystemHealth"),
    ("O5",  "Telemetry",                   "/Owner/Telemetry"),
    ("O6",  "Email Config",                "/Owner/EmailConfig"),
    ("O7",  "Role Templates",              "/Owner/Hub/RoleTemplates"),
    ("O8",  "Manage Roles (Org hub)",      "/Admin/Organization/Roles"),
    ("O9",  "Manage Grants (Org hub)",     "/Admin/Organization/Grants"),
    ("O10", "Organization hub",            "/Admin/Organization"),
    ("O11", "Shift Table",                 "/Calendar/Table"),
    ("O12", "People / Users",              "/Admin/Users"),
]


# ============================================================
# ROLE TEST RUNNERS
# ============================================================

def run_employee(page: Page, email: str, password: str, molecule: str):
    role = "Employee"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Molecule: {molecule} | {email}")
    print(f"{'='*60}")

    if not login(page, email, password):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)

    check_sidebar(page, role, molecule,
        expected_hrefs=["/Calendar/Shifts", "/Calendar/Overview", "/My/Requests",
                        "/My/Profile", "/My/Settings", "/My/Help"],
        forbidden_hrefs=["/Admin/Index", "/Admin/Users", "/Owner/Index",
                         "/Director/ViewAsMode"])

    for wf_id, wf_name, url in EMPLOYEE_WORKFLOWS:
        visit(page, role, molecule, wf_id, wf_name, url)

    # Verify Employee cannot access admin pages
    print(f"    [Access control checks]")
    for check_id, check_name, check_url in [
        ("EC1", "Blocked from Command Center", "/Admin/Index"),
        ("EC2", "Blocked from Admin Users",    "/Admin/Users"),
        ("EC3", "Blocked from Owner Hub",       "/Owner/Index"),
    ]:
        page.goto(f"{BASE_URL}{check_url}", wait_until="networkidle", timeout=15000)
        status, notes = classify_page(page)
        # For access control: we WANT a FAIL (denied) -- flip logic
        is_blocked = status == FAIL
        r_status = PASS if is_blocked else FAIL
        r_note = "correctly blocked" if is_blocked else f"INCORRECTLY accessible! {notes}"
        record(role, molecule, f"{check_id}: {check_name}", check_url,
               r_status, notes=r_note)
        icon = "[OK]" if r_status == PASS else "[!!]"
        print(f"         {icon} {check_url}: {r_note}")

    logout(page)


def run_lead(page: Page, email: str, password: str, molecule: str):
    role = "Lead"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Molecule: {molecule} | {email}")
    print(f"{'='*60}")

    if not login(page, email, password):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)

    # Note: /Calendar/Table is conditionally shown only when ExcelCalendarShifts FF is OFF.
    # When FF is ON, both shiftsTableUrl and shiftsCalendarUrl resolve to /Calendar/Shifts,
    # so the Shift Table link is intentionally hidden (replaced by unified Schedule link).
    check_sidebar(page, role, molecule,
        expected_hrefs=["/Admin/Index", "/Owner/Programs",
                        "/Owner/Blueprints", "/Requests/Index", "/Admin/Users",
                        "/Admin/Organization"],
        forbidden_hrefs=["/Owner/Index", "/Director/ViewAsMode"])

    # A2 fix: People link should be visible via ViewUsers grant
    page.goto(f"{BASE_URL}/Home/Index", wait_until="networkidle", timeout=15000)
    try:
        sidebar_html = page.locator(".app-sidebar-nav").inner_html(timeout=5000)
        people_visible = "/Admin/Users" in sidebar_html
        record(role, molecule, "A2: People link visible in sidebar (ViewUsers gate)",
               "/Home/Index", PASS if people_visible else FAIL,
               notes="People link found" if people_visible else "A2 fix: People link MISSING for Lead")
        print(f"    [A2] People link in sidebar: {'[OK] PASS' if people_visible else '[!!] FAIL'}")
    except Exception as e:
        record(role, molecule, "A2: People link check", "/Home/Index", FAIL, notes=str(e)[:80])

    for wf_id, wf_name, url in LEAD_WORKFLOWS:
        visit(page, role, molecule, wf_id, wf_name, url)

    # A1 fix: check Org hub Quick Actions
    # Lead has both AssignRoles (useOwnJobType) and ViewGrants in template seed (G(3,38) + G(3,35))
    check_org_hub_actions(page, role, molecule,
        expect_roles_btn=True, expect_grants_btn=True)

    logout(page)


def run_brdirector(page: Page, email: str, password: str, molecule: str):
    role = "BRDirector"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Molecule: {molecule} | {email}")
    print(f"{'='*60}")

    if not login(page, email, password):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)

    check_sidebar(page, role, molecule,
        expected_hrefs=["/Admin/Index", "/Admin/Users", "/Admin/Companies",
                        "/Admin/Organization"],
        forbidden_hrefs=["/Owner/Index", "/Director/ViewAsMode"])

    for wf_id, wf_name, url in BRDIRECTOR_WORKFLOWS:
        visit(page, role, molecule, wf_id, wf_name, url)

    # A1: BRDirector likely has ViewGrants & AssignRoles
    check_org_hub_actions(page, role, molecule,
        expect_roles_btn=True, expect_grants_btn=True)

    logout(page)


def run_areaadmin(page: Page, email: str, password: str, molecule: str):
    role = "AreaAdmin"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Molecule: {molecule} | {email}")
    print(f"{'='*60}")

    if not login(page, email, password):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)

    # AreaAdmin should see Director Tools
    check_sidebar(page, role, molecule,
        expected_hrefs=["/Admin/Index", "/Director/ViewAsMode", "/Admin/Users",
                        "/Admin/Companies", "/Admin/Organization", "/Admin/Config"],
        forbidden_hrefs=["/Owner/Index"])

    for wf_id, wf_name, url in AREAADMIN_WORKFLOWS:
        visit(page, role, molecule, wf_id, wf_name, url)

    check_org_hub_actions(page, role, molecule,
        expect_roles_btn=True, expect_grants_btn=True)

    logout(page)


def run_deptlead(page: Page, email: str, password: str, molecule: str):
    role = "DepartmentLead"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Molecule: {molecule} | {email}")
    print(f"{'='*60}")

    if not login(page, email, password):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)

    # /Calendar/Table is conditionally hidden when ExcelCalendarShifts FF is ON
    check_sidebar(page, role, molecule,
        expected_hrefs=["/Admin/Index", "/Owner/Programs", "/Owner/Blueprints"],
        forbidden_hrefs=["/Owner/Index", "/Director/ViewAsMode"])

    for wf_id, wf_name, url in DEPTLEAD_WORKFLOWS:
        visit(page, role, molecule, wf_id, wf_name, url)

    logout(page)


def run_moladmin(page: Page, email: str, password: str, molecule: str):
    role = "MoleculeAdmin"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Molecule: {molecule} | {email}")
    print(f"{'='*60}")

    if not login(page, email, password):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)

    for wf_id, wf_name, url in MOLADMIN_WORKFLOWS:
        visit(page, role, molecule, wf_id, wf_name, url)

    check_org_hub_actions(page, role, molecule,
        expect_roles_btn=True, expect_grants_btn=True)

    logout(page)


def run_director(page: Page, email: str, password: str, molecule: str):
    role = "Director"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Molecule: {molecule} | {email}")
    print(f"{'='*60}")

    if not login(page, email, password):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)

    check_sidebar(page, role, molecule,
        expected_hrefs=["/Director/ViewAsMode", "/Admin/Announcements"],
        forbidden_hrefs=["/Owner/Index"])

    for wf_id, wf_name, url in DIRECTOR_WORKFLOWS:
        visit(page, role, molecule, wf_id, wf_name, url)

    logout(page)


def run_owner(page: Page, email: str, password: str, molecule: str):
    role = "Owner"
    print(f"\n{'='*60}")
    print(f"  ROLE: {role} | Molecule: {molecule} | {email}")
    print(f"{'='*60}")

    if not login(page, email, password):
        record(role, molecule, "LOGIN", "/Auth/Login", FAIL, notes="Login failed")
        return

    shot = take_screenshot(page, f"{role}_{molecule}_home")
    record(role, molecule, "LOGIN", "/Auth/Login", PASS, notes="Logged in", screenshot_path=shot)

    # Owner should see EVERYTHING
    check_sidebar(page, role, molecule,
        expected_hrefs=["/Admin/Index", "/Admin/Users", "/Owner/Index",
                        "/Director/ViewAsMode", "/Owner/FeatureFlags",
                        "/Owner/SystemHealth", "/Admin/Organization"],
        forbidden_hrefs=[])

    for wf_id, wf_name, url in OWNER_WORKFLOWS:
        visit(page, role, molecule, wf_id, wf_name, url)

    check_org_hub_actions(page, role, molecule,
        expect_roles_btn=True, expect_grants_btn=True)

    logout(page)


# ============================================================
# HTML REPORT
# ============================================================

def generate_report():
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    total = len(all_results)
    passes = sum(1 for r in all_results if r["status"] == PASS)
    fails = sum(1 for r in all_results if r["status"] == FAIL)
    warns = sum(1 for r in all_results if r["status"] == WARN)

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

    # Summary by role+molecule
    summary_map = {}
    for r in all_results:
        key = f"{r['role']} / {r['molecule']}"
        if key not in summary_map:
            summary_map[key] = {"pass": 0, "fail": 0, "total": 0}
        summary_map[key]["total"] += 1
        if r["status"] == PASS:
            summary_map[key]["pass"] += 1
        elif r["status"] == FAIL:
            summary_map[key]["fail"] += 1

    summary_rows = ""
    for key, counts in summary_map.items():
        pct = round(counts["pass"] / counts["total"] * 100) if counts["total"] else 0
        bar_color = "#22c55e" if pct >= 90 else ("#f59e0b" if pct >= 70 else "#ef4444")
        summary_rows += f"""
        <tr>
          <td><strong>{key}</strong></td>
          <td>{counts['pass']}/{counts['total']}</td>
          <td>{counts['fail']}</td>
          <td>
            <div style="background:#e5e7eb;border-radius:4px;height:16px;width:150px">
              <div style="background:{bar_color};width:{pct}%;height:100%;border-radius:4px"></div>
            </div>
            {pct}%
          </td>
        </tr>"""

    html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>ShiftManager Navigation Test Report</title>
<style>
  body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 0; background: #f8fafc; color: #1e293b; }}
  .header {{ background: #1e293b; color: white; padding: 2rem; }}
  .header h1 {{ margin: 0 0 0.5rem; font-size: 1.75rem; }}
  .header p {{ margin: 0; opacity: 0.7; }}
  .stats {{ display: flex; gap: 1rem; padding: 1.5rem 2rem; }}
  .stat {{ background: white; border-radius: 12px; padding: 1rem 1.5rem; flex: 1; box-shadow: 0 1px 3px rgba(0,0,0,.1); }}
  .stat-val {{ font-size: 2rem; font-weight: 700; }}
  .stat-label {{ color: #6b7280; font-size: 0.875rem; }}
  .green {{ color: #22c55e; }} .red {{ color: #ef4444; }} .yellow {{ color: #f59e0b; }}
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
  <h1>ShiftManager -- Navigation & Workflow Test Report</h1>
  <p>Generated: {timestamp} | Total: {total} checks | Pass: {passes} | Fail: {fails} | Warn: {warns}</p>
</div>
<div class="stats">
  <div class="stat"><div class="stat-val green">{passes}</div><div class="stat-label">Passed</div></div>
  <div class="stat"><div class="stat-val red">{fails}</div><div class="stat-label">Failed</div></div>
  <div class="stat"><div class="stat-val yellow">{warns}</div><div class="stat-label">Warnings</div></div>
  <div class="stat"><div class="stat-val">{round(passes/total*100) if total else 0}%</div><div class="stat-label">Pass Rate</div></div>
</div>

<div class="section">
  <h2>Summary by Role</h2>
  <table>
    <thead><tr><th>Role / Molecule</th><th>Pass / Total</th><th>Failures</th><th>Pass Rate</th></tr></thead>
    <tbody>{summary_rows}</tbody>
  </table>
</div>

<div class="section">
  <h2>Full Results</h2>
  <table>
    <thead>
      <tr>
        <th>Role</th><th>Molecule</th><th>Workflow</th><th>URL</th>
        <th>Status</th><th>Notes</th><th>[img]</th>
      </tr>
    </thead>
    <tbody>{rows}</tbody>
  </table>
</div>
</body>
</html>"""

    report_path = os.path.join(RESULTS_DIR, "report.html")
    with open(report_path, "w", encoding="utf-8") as f:
        f.write(html)
    return report_path


# ============================================================
# MAIN
# ============================================================

def main():
    print(f"\n{'#'*60}")
    print(f"# ShiftManager Navigation Test -- {datetime.now().strftime('%H:%M:%S')}")
    print(f"# Base URL: {BASE_URL}")
    print(f"# Results: {RESULTS_DIR}")
    print(f"{'#'*60}\n")

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()

        # ?? OREN MOLECULE ??????????????????????????????????????
        print("\n\n>>> OREN MOLECULE <<<")
        run_employee(page, "emp.tz.alhut@test",  "Test1234!", "Oren")
        run_lead    (page, "mgr.alhut.tz@test",  "Test1234!", "Oren")
        run_brdirector(page, "mgr.br.hir@test",  "Test1234!", "Oren")
        run_areaadmin(page, "areaadmin@test",     "Test1234!", "Oren")
        run_moladmin (page, "moladmin.oren@test", "Test1234!", "Oren")
        run_director (page, "dir.alhut@test",     "Test1234!", "Oren")

        # ?? SHIKMA MOLECULE ????????????????????????????????????
        print("\n\n>>> SHIKMA MOLECULE <<<")
        run_employee  (page, "emp.tech.pie@test",  "Test1234!", "Shikma")
        run_deptlead  (page, "deptlead.pie@test",  "Test1234!", "Shikma")

        # ?? OWNER (cross-molecule) ?????????????????????????????
        print("\n\n>>> OWNER (system-wide) <<<")
        run_owner(page, "test.owner@shifty.test", "TestOwner123!", "System")

        browser.close()

    print(f"\n\n{'='*60}")
    print(f"  Total checks: {len(all_results)}")
    passes = sum(1 for r in all_results if r["status"] == PASS)
    fails  = sum(1 for r in all_results if r["status"] == FAIL)
    print(f"  PASS: {passes}  FAIL: {fails}")

    report_path = generate_report()
    print(f"\n  HTML report: {report_path}")
    print(f"{'='*60}\n")

    # Print failures for easy reading
    failures = [r for r in all_results if r["status"] == FAIL]
    if failures:
        print(f"\n  FAILURES ({len(failures)}):")
        for r in failures:
            print(f"    [!!] [{r['role']}/{r['molecule']}] {r['workflow']} -- {r['notes']}")

    # Save JSON
    json_path = os.path.join(RESULTS_DIR, "results.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(all_results, f, indent=2)

    return 1 if fails > 0 else 0


if __name__ == "__main__":
    exit(main())
