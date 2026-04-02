#!/usr/bin/env python3
"""
Verify all feature flags are displayed on the Feature Flags admin page.
"""
import sys
import io
from playwright.sync_api import sync_playwright

# Handle Unicode output on Windows
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

def test_feature_flags():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()

        try:
            # First, login as owner
            page.goto('http://localhost:5000/Auth/Login', wait_until='networkidle')

            # Debug: take screenshot and print HTML
            page.screenshot(path='login_page_debug.png')
            html = page.content()
            print(f"HTML length: {len(html)}")

            # Try to find all inputs
            inputs = page.locator('input').all()
            print(f"\nFound {len(inputs)} input fields")
            for i, inp in enumerate(inputs):
                inp_type = inp.get_attribute('type')
                inp_name = inp.get_attribute('name')
                inp_id = inp.get_attribute('id')
                inp_placeholder = inp.get_attribute('placeholder')
                print(f"  Input {i}: type={inp_type}, name={inp_name}, id={inp_id}, placeholder={inp_placeholder}")

            # Try to fill the fields by order (first input = email, second = password)
            if len(inputs) >= 2:
                inputs[0].fill('test.owner@shifty.test')
                inputs[1].fill('TestOwner123!')
                page.screenshot(path='before_login_submit.png')
                page.click('button[type="submit"]')
                page.wait_for_load_state('networkidle')
                print(f"After login, current URL: {page.url}")
                page.screenshot(path='after_login.png')
            else:
                print("ERROR: Expected at least 2 input fields")
                return False

            # Now navigate to Feature Flags page
            page.goto('http://localhost:5000/Owner/FeatureFlags', wait_until='networkidle')
            print(f"Feature flags page URL: {page.url}")

            # Take screenshot for debugging
            page.screenshot(path='feature_flags_page.png')

            # Debug: check if we're on the right page
            title_elem = page.locator('h1').first
            if title_elem:
                print(f"Page title: {title_elem.text_content()}")

            # Look for input[type=checkbox] with name="flag_*"
            checkboxes = page.locator('input[type=checkbox][name^="flag_"]').all()

            flag_names = []
            for checkbox in checkboxes:
                name_attr = checkbox.get_attribute('name')
                if name_attr and name_attr.startswith('flag_'):
                    flag_name = name_attr[5:]  # Remove "flag_" prefix
                    flag_names.append(flag_name)
            
            print(f"\nFound {len(flag_names)} feature flags on the page:")
            for name in sorted(flag_names):
                print(f"  - {name}")
            
            # Expected flags from FeatureFlagSeed.cs
            expected_flags = {
                # UI flags
                'FF_WIDGETS_ENABLED',
                
                # Excel Calendar flags
                'FF_EXCEL_CALENDARS',
                'FF_EXCEL_CALENDAR_SHIFTS',
                'FF_EXCEL_CALENDAR_CHORES',
                'FF_EXCEL_CALENDAR_ONCALL',
                'FF_EXCEL_CALENDAR_OVERVIEW',
                
                # Operational flags
                'FF_ALLOW_PUBLIC_SIGNUP',
                'FF_ENABLE_DAILY_NOTIFICATIONS',
                'FF_ENABLE_DIRECTOR_ROLE',
                'FF_ENABLE_API_KEY_MANAGEMENT',
                'FF_ENABLE_COMPANY_SWITCHER',
                'FF_ENFORCE_RANK_ELIGIBILITY',
                
                # API flags - Master
                'FF_API_ENABLED',
                
                # API - Users
                'FF_API_USERS_LIST',
                'FF_API_USERS_GET',
                'FF_API_USERS_CREATE',
                'FF_API_USERS_UPDATE',
                
                # API - Shifts
                'FF_API_SHIFTS_LIST',
                'FF_API_SHIFTS_GET',
                
                # API - Time Off
                'FF_API_TIMEOFF_LIST',
                'FF_API_TIMEOFF_GET',
                'FF_API_TIMEOFF_CREATE',
                'FF_API_TIMEOFF_APPROVE',
                'FF_API_TIMEOFF_DECLINE',
                
                # API - Notifications
                'FF_API_NOTIFICATIONS_LIST',
                'FF_API_NOTIFICATIONS_GET',
                'FF_API_NOTIFICATIONS_MARKREAD',
                'FF_API_NOTIFICATIONS_MARKALLREAD',
                
                # API - Chores
                'FF_API_CHORES_LIST',
                'FF_API_CHORES_GET',
                'FF_API_CHORES_CREATE',
                'FF_API_CHORES_UPDATE',
                'FF_API_CHORES_DELETE',
                
                # API - On-Duty
                'FF_API_ONDUTY_LIST',
                'FF_API_ONDUTY_GET',
                'FF_API_ONDUTY_CREATE',
                'FF_API_ONDUTY_UPDATE',
                'FF_API_ONDUTY_DELETE',
                
                # API - Swap Requests
                'FF_API_SWAPREQUESTS_LIST',
                'FF_API_SWAPREQUESTS_GET',
                'FF_API_SWAPREQUESTS_CREATE',
                'FF_API_SWAPREQUESTS_APPROVE',
                'FF_API_SWAPREQUESTS_DECLINE',
                'FF_API_SWAPREQUESTS_DELETE',
                
                # API - Feedback
                'FF_API_FEEDBACK_LIST',
                'FF_API_FEEDBACK_GET',
                'FF_API_FEEDBACK_CREATE',
                'FF_API_FEEDBACK_UPDATESTATUS',
                'FF_API_FEEDBACK_DELETE',
                
                # API - Audit Logs & Analytics
                'FF_API_AUDITLOGS_LIST',
                'FF_API_ANALYTICS_SUMMARY',
                
                # Feature flags
                'FF_FRIENDSHIPS_ENABLED',
                'FF_DUTY_ROTATION_ENABLED',
                'FF_SETUP_TASKS_ENABLED',
                'FF_VACATION_APPROVAL_ENABLED',
                'FF_STORE_HOURS_ENABLED',
                'FF_EMAIL_SERVICE_ENABLED',
            }
            
            found_set = set(flag_names)
            
            print(f"\n\nExpected {len(expected_flags)} flags total")
            print(f"Found {len(found_set)} flags on page\n")
            
            missing = expected_flags - found_set
            extra = found_set - expected_flags

            if missing:
                print(f"[MISSING FLAGS] ({len(missing)}):")
                for flag in sorted(missing):
                    print(f"  - {flag}")

            if extra:
                print(f"\n[EXTRA FLAGS] ({len(extra)}):")
                for flag in sorted(extra):
                    print(f"  - {flag}")

            if not missing and not extra:
                print(f"\n[OK] All {len(expected_flags)} feature flags are displayed correctly!")
                return True
            else:
                print(f"\n[FAIL] Mismatch detected!")
                return False

        finally:
            browser.close()

if __name__ == '__main__':
    success = test_feature_flags()
    exit(0 if success else 1)
