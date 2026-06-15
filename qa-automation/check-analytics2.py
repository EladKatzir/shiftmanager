"""Analytics page detailed structure check."""
import os, time
os.environ["PYTHONIOENCODING"] = "utf-8"
from playwright.sync_api import sync_playwright

BASE = "http://localhost:5000"
ARTIFACTS = r"C:\Users\katzi\Downloads\ShiftManager\docs\superpowers\plans\artifacts"

def login(page, email="owner2@test", pwd="Test1234!"):
    page.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    page.fill("input[name='Email']", email)
    page.fill("input[name='Password']", pwd)
    page.click("button[type='submit']")
    page.wait_for_load_state("networkidle")
    time.sleep(1)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(viewport={"width": 1440, "height": 1000})
    page = ctx.new_page()
    login(page)

    page.goto(f"{BASE}/Admin/Analytics?from=2024-01-01&to=2026-06-14", wait_until="networkidle")
    time.sleep(4)

    # Get full page content to find form fields
    content = page.content()

    # Check for ShiftCategory
    cat_present = "ShiftCategoryId" in content or "shiftCategoryId" in content or "shift-category" in content or "ShiftCategory" in content
    print(f"ShiftCategory in content: {cat_present}")
    if cat_present:
        # Find context around it
        idx = content.find("ShiftCategory")
        if idx < 0:
            idx = content.find("shiftCategory")
        print(f"  Context: ...{content[max(0,idx-100):idx+200]}...")

    # Check filter form
    filter_form_present = "filter" in content.lower() and ("molecule" in content.lower() or "Molecule" in content)
    print(f"Filter form present: {filter_form_present}")

    # Find all form elements
    all_selects = page.locator("select").all()
    all_inputs = page.locator("input").all()
    print(f"\nSelects: {len(all_selects)}, Inputs: {len(all_inputs)}")

    # Check for filter panel (collapsed?)
    filter_sections = page.locator("[id*='filter'], [class*='filter'], details, .collapsible").all()
    print(f"Filter sections: {len(filter_sections)}")

    # Check for molecule filter specifically
    mol_filter = page.locator("[name*='olecule'], [id*='olecule']").all()
    print(f"Molecule filters: {len(mol_filter)}")

    # Look at the actual analytics page source to understand structure
    analytics_url = page.url
    print(f"\nCurrent URL: {analytics_url}")

    # SVG-based charts
    donut_path = page.locator("circle[stroke-dasharray], .donut, [class*='donut']").all()
    print(f"Donut/circle elements: {len(donut_path)}")

    # The chart is likely rendered server-side as SVG or via inline SVG
    svg_circles = page.locator("circle").all()
    print(f"SVG circles: {len(svg_circles)}")

    # Screenshot at larger viewport
    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-analytics-inspect.png"), full_page=False)

    # Check if there's a scope selector (molecule/company dropdown)
    scope_items = page.locator("select, input[list]").all()
    for item in scope_items:
        try:
            tag = item.evaluate("e => e.tagName")
            name = item.get_attribute("name") or ""
            id_ = item.get_attribute("id") or ""
            type_ = item.get_attribute("type") or ""
            val = item.input_value() if tag == "SELECT" else ""
            print(f"  Form field: {tag} name={name!r} id={id_!r} type={type_!r} val={val!r}")
        except:
            pass

    # Check what params are accepted by the analytics page
    # Load the C# page model to understand
    print("\nLooking at form action...")
    forms = page.locator("form").all()
    print(f"Forms: {len(forms)}")
    for form in forms[:5]:
        action = form.get_attribute("action") or ""
        method = form.get_attribute("method") or ""
        print(f"  form action={action!r} method={method!r}")
        # List fields
        fields = form.locator("input, select").all()
        for f in fields[:5]:
            tag = f.evaluate("e => e.tagName")
            name = f.get_attribute("name") or ""
            type_ = f.get_attribute("type") or ""
            if type_ != "hidden" and name:
                print(f"    field: {tag} name={name!r}")

    # Try to navigate with molecule param
    page.goto(f"{BASE}/Admin/Analytics?moleculeId=10&from=2024-01-01&to=2026-06-14", wait_until="networkidle")
    time.sleep(4)
    page.screenshot(path=os.path.join(ARTIFACTS, "ab-final-analytics-molecule10.png"), full_page=False)

    selects2 = page.locator("select").all()
    print(f"\nWith moleculeId=10: selects={len(selects2)}")
    for sel in selects2:
        name = sel.get_attribute("name") or ""
        id_ = sel.get_attribute("id") or ""
        opts = [o.text_content().strip()[:20] for o in sel.locator("option").all()[:4]]
        print(f"  select name={name!r} id={id_!r}: {opts}")

    ctx.close()
    browser.close()
    print("Done.")
