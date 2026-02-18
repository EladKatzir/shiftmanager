# -*- coding: utf-8 -*-
import sys, os
os.environ["PYTHONIOENCODING"] = "utf-8"
from playwright.sync_api import sync_playwright

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    context = browser.new_context(viewport={"width": 1280, "height": 900}, ignore_https_errors=True)
    page = context.new_page()

    # Enable request/response logging
    responses = []
    page.on("response", lambda r: responses.append((r.url, r.status)))

    # Login
    page.goto("http://localhost:5000/Auth/Login", wait_until="networkidle")
    local_form = page.locator('form[action="/Auth/Login"]')
    local_form.locator('input[name="Email"]').fill("owner@test.com")
    local_form.locator('input[name="Password"]').fill("123456")
    local_form.locator('button[type="submit"]').click()
    page.wait_for_load_state("networkidle")
    print(f"Logged in: {page.url}")

    # Clear responses from login
    responses.clear()

    # Navigate to Calendar/Table
    resp = page.goto("http://localhost:5000/Calendar/Table", wait_until="networkidle", timeout=15000)
    print(f"\nCalendar/Table final URL: {page.url}")
    print(f"Initial response status: {resp.status if resp else 'None'}")

    print(f"\nAll responses during Calendar/Table navigation:")
    for url, status in responses:
        if "localhost:5000" in url:
            print(f"  [{status}] {url}")

    if "/Error" in page.url:
        # Check if ASPNETCORE_ENVIRONMENT=Development shows the exception
        content = page.content()
        if "exception" in content.lower() or "stack trace" in content.lower():
            print(f"\nException found in page!")
        else:
            print(f"\nCustom error page (no exception details exposed)")

    context.close()
    browser.close()
