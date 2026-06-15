from playwright.sync_api import sync_playwright
import os

ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
os.makedirs(ARTIFACTS, exist_ok=True)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width": 1400, "height": 900})
    page.goto("http://localhost:5000/Auth/Login")
    page.wait_for_load_state("networkidle")
    page.screenshot(path=f"{ARTIFACTS}/task9-login-recon.png", full_page=True)
    print("URL:", page.url)
    print("Title:", page.title())
    # Find all inputs
    inputs = page.locator("input").all()
    for inp in inputs:
        print("Input:", inp.get_attribute("name"), inp.get_attribute("type"), inp.get_attribute("id"))
    browser.close()
