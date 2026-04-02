#!/usr/bin/env python3
"""
smoke_test.py — Pre-handoff smoke test for ShiftManager.
Starts the dev server, logs in as Owner, and checks 9 critical routes for 500s,
blank pages, and JS console errors.

Usage:
  python scripts/smoke_test.py

Run from the project root.
"""

import subprocess
import sys
import time
import socket
import os

# Force UTF-8 output on Windows to avoid charmap encoding errors
if sys.stdout.encoding != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from playwright.sync_api import sync_playwright, Page

HOST = "localhost"
PORT = 5000
BASE_URL = f"http://{HOST}:{PORT}"
STARTUP_TIMEOUT = 60  # seconds to wait for server

CREDENTIALS = {"email": "admin@local", "password": "admin123"}

ROUTES = [
    ("/Auth/Login",                         False, "Login page"),
    ("/",                                   True,  "Home dashboard"),
    ("/Admin",                              True,  "Admin index"),
    ("/Admin/Users",                        True,  "Admin Users"),
    ("/Admin/Organization/ChoreTypes",      True,  "ChoreTypes (bilingual)"),
    ("/Calendar/Shifts",                    True,  "Calendar Shifts"),
    ("/Calendar/Chores",                    True,  "Calendar Chores"),
    ("/Assignments/Manage",                 True,  "Assignments Manage"),
    ("/Owner/FeatureFlags",                 True,  "Owner Feature Flags"),
]

GREEN = "\033[92m"
RED   = "\033[91m"
YELLOW= "\033[93m"
RESET = "\033[0m"
BOLD  = "\033[1m"

results = []


def wait_for_port(host, port, timeout):
    start = time.time()
    while time.time() - start < timeout:
        try:
            with socket.create_connection((host, port), timeout=1):
                return True
        except OSError:
            time.sleep(1)
    return False


def login(page: Page):
    page.goto(f"{BASE_URL}/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.fill('input[name="Email"], input[type="email"]', CREDENTIALS["email"])
    page.fill('input[name="Password"], input[type="password"]', CREDENTIALS["password"])
    page.click('button[type="submit"]')
    page.wait_for_load_state("networkidle")
    # Confirm we're past the login page
    if "/Auth/Login" in page.url and "?" not in page.url:
        raise RuntimeError(f"Login failed — still on login page: {page.url}")


def check_route(page: Page, path: str, needs_auth: bool, label: str):
    js_errors = []
    page.on("console", lambda msg: js_errors.append(msg.text) if msg.type == "error" else None)

    url = BASE_URL + path
    response = page.goto(url)
    page.wait_for_load_state("networkidle")

    status = response.status if response else 0
    final_url = page.url
    title = page.title()
    content = page.content()

    # Detect 500 / error pages
    is_500 = status == 500
    has_exception_text = any(t in content for t in [
        "An error occurred", "System.Exception", "500", "Internal Server Error",
        "ArgumentNullException", "NullReferenceException", "Object reference not set"
    ])
    is_blank = len(content.strip()) < 200
    # Redirected to login is OK for auth-required routes
    redirected_to_login = "/Auth/Login" in final_url

    if is_500 or (has_exception_text and status != 200):
        status_label = f"{RED}FAIL (HTTP {status} / exception in body){RESET}"
        ok = False
    elif is_blank and not redirected_to_login:
        status_label = f"{YELLOW}WARN (blank page){RESET}"
        ok = False
    elif redirected_to_login and needs_auth:
        status_label = f"{YELLOW}WARN (redirected to login — session lost?){RESET}"
        ok = False
    else:
        status_label = f"{GREEN}PASS (HTTP {status}){RESET}"
        ok = True

    errors_str = ""
    if js_errors:
        errors_str = f"\n      JS errors: {'; '.join(js_errors[:3])}"

    results.append({
        "label": label,
        "path": path,
        "status": status,
        "ok": ok,
        "js_errors": js_errors,
        "final_url": final_url,
    })

    print(f"  {status_label}  [{label}]  {path}{errors_str}")
    return ok


def main():
    print(f"\n{BOLD}ShiftManager Pre-Handoff Smoke Test{RESET}")
    print("=" * 50)

    # Start server
    print(f"\nStarting dev server on port {PORT}...")
    env = os.environ.copy()
    env["ASPNETCORE_ENVIRONMENT"] = "Development"
    env["ASPNETCORE_URLS"] = f"http://{HOST}:{PORT}"

    server = subprocess.Popen(
        ["dotnet", "run", "--no-build", "--project", "."],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        env=env,
        cwd=os.getcwd(),
    )

    ready = wait_for_port(HOST, PORT, STARTUP_TIMEOUT)
    if not ready:
        server.terminate()
        print(f"{RED}ERROR: Server did not start within {STARTUP_TIMEOUT}s{RESET}")
        sys.exit(1)

    print(f"{GREEN}Server ready at {BASE_URL}{RESET}\n")

    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True)
            context = browser.new_context()
            page = context.new_page()

            # Step 1: Check login page (no auth needed)
            print("Checking public routes...")
            check_route(page, "/Auth/Login", False, "Login page")

            # Step 2: Login
            print("\nLogging in as Owner...")
            try:
                login(page)
                print(f"{GREEN}Login successful -> {page.url}{RESET}\n")
            except Exception as e:
                print(f"{RED}Login FAILED: {e}{RESET}")
                browser.close()
                return

            # Step 3: Check authenticated routes
            print("Checking authenticated routes...")
            for path, needs_auth, label in ROUTES[1:]:
                check_route(page, path, needs_auth, label)

            browser.close()

    finally:
        server.terminate()
        try:
            server.wait(timeout=5)
        except subprocess.TimeoutExpired:
            server.kill()

    # Summary
    passed = sum(1 for r in results if r["ok"])
    failed = [r for r in results if not r["ok"]]
    js_error_routes = [r for r in results if r["js_errors"]]

    print(f"\n{'=' * 50}")
    print(f"{BOLD}Results: {passed}/{len(results)} routes passed{RESET}")

    if failed:
        print(f"\n{RED}FAILED routes:{RESET}")
        for r in failed:
            print(f"  FAIL [{r['label']}] {r['path']} (HTTP {r['status']}) -> {r['final_url']}")

    if js_error_routes:
        print(f"\n{YELLOW}Routes with JS console errors:{RESET}")
        for r in js_error_routes:
            print(f"  ! [{r['label']}] {r['path']}")
            for e in r["js_errors"][:3]:
                print(f"      {e}")

    if not failed and not js_error_routes:
        print(f"\n{GREEN}{BOLD}All clear - app is ready for handoff.{RESET}")
    elif not failed:
        print(f"\n{YELLOW}All routes passed but some JS errors were logged.{RESET}")
    else:
        print(f"\n{RED}Fix failed routes before handoff.{RESET}")
        sys.exit(1)


if __name__ == "__main__":
    main()
