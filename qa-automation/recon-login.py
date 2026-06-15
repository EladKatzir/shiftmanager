"""Quick recon of the login page to get correct selectors."""
from playwright.sync_api import sync_playwright
import os

ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"
os.makedirs(ARTIFACTS, exist_ok=True)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width": 1400, "height": 900})
    page.goto("http://localhost:5000/Auth/Login", wait_until="networkidle")
    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-login-recon.png"), full_page=True)

    # List all inputs
    inputs = page.locator("input").all()
    print(f"Inputs on login page: {len(inputs)}")
    for inp in inputs:
        try:
            name = inp.get_attribute("name") or ""
            id_ = inp.get_attribute("id") or ""
            type_ = inp.get_attribute("type") or ""
            placeholder = inp.get_attribute("placeholder") or ""
            print(f"  input: name={name!r}, id={id_!r}, type={type_!r}, placeholder={placeholder!r}")
        except:
            pass

    # List all buttons
    buttons = page.locator("button, input[type='submit']").all()
    for btn in buttons:
        try:
            txt = btn.inner_text()[:60] if btn.get_attribute("type") != "hidden" else ""
            type_ = btn.get_attribute("type") or ""
            print(f"  button: type={type_!r}, text={txt!r}")
        except:
            pass

    browser.close()
    print("Done recon.")
