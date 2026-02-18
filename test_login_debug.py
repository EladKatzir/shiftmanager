# -*- coding: utf-8 -*-
import sys, os
os.environ["PYTHONIOENCODING"] = "utf-8"
from playwright.sync_api import sync_playwright

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width": 1280, "height": 900}, ignore_https_errors=True)
    page.goto("http://localhost:5000/Auth/Login", wait_until="networkidle")

    # Target the SECOND form (local login, not Griffin)
    local_form = page.locator('form[action="/Auth/Login"]')
    print(f"Local login form found: {local_form.count()}")

    # Fill the local login form specifically
    local_form.locator('input[name="Email"]').fill("owner@test.com")
    local_form.locator('input[name="Password"]').fill("123456")

    # Click the submit button WITHIN the local login form
    local_form.locator('button[type="submit"]').click()
    page.wait_for_load_state("networkidle")

    print(f"After login URL: {page.url}")
    page.screenshot(path="test_screenshots/debug_local_login.png")

    if "/Auth/Login" not in page.url:
        print("SUCCESS: Logged in!")

        # Quick smoke test of authenticated pages
        test_pages = [
            "/Admin/Users",
            "/Owner/Hub/RoleTemplates",
            "/Owner/Hub/RoleTemplates/Edit?id=1",
            "/Calendar/Table",
            "/Owner/Hub/Grants",
            "/Owner/GriffinConfig",
            "/My",
        ]
        for path in test_pages:
            page.goto(f"http://localhost:5000{path}", wait_until="networkidle")
            ok = "Login" not in page.url and "AccessDenied" not in page.url
            status = "OK" if ok else "REDIRECT"
            print(f"  [{status}] {path} -> {page.url}")
    else:
        print("FAIL: Still on login page")
        # Check for errors
        body_text = page.locator('body').text_content()
        print(f"Page text snippet: {body_text[:200]}")

    browser.close()
