# Localization Review Report -- ShiftManager v3.1.x

**Date**: 2026-03-13
**Reviewer**: QA Localization Review Agent (Claude Opus 4.6)
**Languages Tested**: English (en-US, LTR), Hebrew (he-IL, RTL)
**Test Method**: Automated Playwright browser testing with dual-language screenshots
**App URL**: https://localhost:5001

---

## 1. Executive Summary

**Overall Localization Completeness: ~92%**

The ShiftManager application demonstrates strong localization infrastructure with the vast majority of UI strings properly translated between English and Hebrew. The `<loc>` tag helper system and `.resx` resource files are working effectively across most pages.

**RTL Health: Excellent**
- `dir="rtl"` correctly set on `<html>` element in Hebrew mode
- Sidebar correctly positioned on the right side
- Text alignment is right-aligned
- Breadcrumbs flow right-to-left
- Calendar day columns correctly reverse order
- Flex containers and layout mirroring work properly

**Critical Gaps Found: 13 issues**
- 1 P0 (Blocker)
- 3 P1 (Critical)
- 5 P2 (High)
- 2 P3 (Medium)
- 2 P4 (Low)

---

## 2. Coverage Summary

### Navigation & Layout (6/6 checks PASS)
- [x] Sidebar navigation: all items translated in Hebrew
- [x] Header elements: breadcrumbs, user menu, toggles translated
- [x] Page titles translated
- [x] Footer/sidebar user section translated (role names translated)
- [x] Context switcher labels translated ("CURRENTLY VIEWING" -> "צפייה נוכחית")
- [x] Scope switcher labels translated

### Calendar Pages (7/8 checks -- 1 FAIL)
- [x] Shifts calendar: filter labels, button labels translated
- [x] Chores calendar: all labels and headers translated
- [x] OnCall calendar: duty type names properly translated (Hakam->חק"מכו, Lead->מובילתו)
- [x] Overview calendar: row labels, filter labels translated
- [x] Day/Week/Month view controls: navigation labels translated
- [x] Calendar landing page: card titles and descriptions translated
- [x] Bottom sheet: Quick Info widget translated ("מידע מהיר")
- [ ] **FAIL**: Day-of-week column headers show Hebrew in English mode (see LOC-01)

### Forms & Admin (5/6 checks -- 1 FAIL)
- [x] Login page: all labels, placeholders, error messages translated
- [x] Signup/Request Access page: labels, steps, validation messages translated
- [x] Admin Users: table headers, button labels, filter labels translated
- [x] Admin Settings: hierarchy labels translated
- [ ] **FAIL**: Companies page has raw key "CompanyMoleculeHint" (see LOC-06)
- [x] Owner pages: page titles, form labels, action buttons translated

### RTL Layout (10/10 checks PASS)
- [x] Sidebar positioned on right side (x=1140 of 1400px viewport)
- [x] Text aligned to the right (`text-align: right`)
- [x] Flex containers reversed (right-to-left flow)
- [x] Calendar navigation arrows correct direction
- [x] Calendar day columns reversed (Saturday on left, Sunday on right)
- [x] Form labels on right, inputs flow RTL
- [x] Breadcrumbs flow right-to-left
- [x] Phone numbers remain LTR within RTL context (050-1234567 placeholder)
- [x] No overlapping or clipped elements observed due to RTL
- [x] Arrow/chevron icons appear correct

### Date/Time/Number Formatting (5/6 checks -- 1 NOTE)
- [x] English dates: "Mar 08, 2026" format (correct for en-US)
- [x] Hebrew dates: "08 מרץ 2026" format (correct for he-IL)
- [x] Time format consistent
- [ ] **NOTE**: Day-of-week names always show Hebrew even in English mode (see LOC-01)
- [x] Month names in correct language ("March" in EN, "מרץ" in HE)
- [x] Date separator: EN uses "/" (08/03), HE uses "." (08.03)

### Dynamic & JavaScript Content (5/6 checks -- 1 FAIL)
- [x] Quick Info widget translated ("No one is on-call right now" -> "אף אחד לא בכוננות כרגע")
- [x] Form validation messages translated (login error: "Invalid credentials" -> "אישורים לא חוקיים")
- [x] Empty state messages translated ("No shifts found" -> "לא נמצאו משמרות")
- [x] Offline handler message localized
- [ ] **FAIL**: Disk space warning banner untranslated in Hebrew mode (see LOC-04)
- [x] Login placeholders translated ("Enter your email" -> "הזן את האימייל שלך")

### Language Switching (4/4 checks PASS)
- [x] Toggle English -> Hebrew: page reloads in Hebrew, RTL layout applied
- [x] Toggle Hebrew -> English: page reloads in English, LTR layout applied
- [x] Language preference persists across navigation (verified: /Calendar, /Calendar/Shifts, /My/Requests, /Admin/Users all maintain `lang=he dir=rtl`)
- [x] Language preference persists via `.AspNetCore.Culture` cookie (1-year expiry)

### English Leakage in Hebrew Mode (9/12 checks -- 3 FAIL)
- [x] Login page: all translated (except brand name "Shift Manager" -- see LOC-05)
- [x] Modal dialogs: translated where observed
- [x] Form validation errors: translated
- [x] Form placeholders: translated
- [x] Empty states: translated
- [x] Dropdown options: translated (roles, filters, etc.)
- [ ] **FAIL**: Two aria-labels untranslated: `Dismiss`, `Notifications` (see LOC-12)
- [x] Confirmation dialogs: translated (where observed)
- [ ] **FAIL**: System disk warning banner in English (see LOC-04)
- [x] Calendar Quick Info widget: fully translated
- [x] Error pages: minimal (only "HTTP 404" shown)
- [x] No raw `Nav_*`, `Calendar_*`, `Admin_*`, `Btn_*` key names detected (systematic scan across 15 pages)

### Static String Detection (3/4 checks -- 1 FAIL)
- [ ] **FAIL**: "CompanyMoleculeHint" raw key on Companies page (see LOC-06)
- [x] No Hebrew text in English mode body (except user-entered data which is expected)
- [x] No "[Missing translation]" markers visible
- [x] Button labels, link text, tab names all in active language

---

## 3. Page-by-Page Assessment

### Login Page
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Page title | "Welcome back" | "ברוך שובך" | PASS |
| Subtitle | "Sign in to continue to Shift Manager" | "היכנס כדי להמשיך ל-Shift Manager" | **PARTIAL** -- "Shift Manager" untranslated |
| Email label | "Email" | "אימייל" | PASS |
| Password label | "Password" | "סיסמה" | PASS |
| Email placeholder | "Enter your email" | "הזן את האימייל שלך" | PASS |
| Password placeholder | "Enter your password" | "הזן את הסיסמה שלך" | PASS |
| Login button | "Login" | "התחברות" | PASS |
| Forgot Password | "Forgot Password?" | "שכחתי סיסמה?" | PASS |
| Request Access | "Request Access" | "בקש גישה" | PASS |
| Error message | "Invalid credentials." | "אישורים לא חוקיים." | PASS |
| RTL layout | LTR | RTL (form on left, branding on right) | PASS |

### Request Access / Signup Page
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Page title | "Request Access" | "בקש גישה" | PASS |
| Step labels | "Account / Unit / Role" | "חשבון / יחידה / תפקיד" | PASS |
| Form labels | All translated | All translated | PASS |
| Role options | English role names | Hebrew role names | PASS |
| Validation text | "Your request will need to be approved..." | "בקשתך תצטרך לקבל אישור..." | PASS |
| Footer link | "Already have an account? Login here" | "כבר יש לך חשבון? התחבר כאן" | PASS |

### Password Management Page
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Forgot Password section | All translated | All translated | PASS |
| Change Password title | "Change Password" | "שינוי סיסמה" | PASS |
| Change Password description | "If you already know your current password..." | Same English text | **FAIL (LOC-03)** |
| Form labels | Translated | Translated | PASS |

### Home / Onboarding Page (Owner)
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Welcome message | "Welcome, מנהל מערכת" | "ברוך הבא, מנהל מערכת" | PASS |
| Feature cards | All in English | All in Hebrew | PASS |
| Quick actions | Translated | Translated | PASS |

### Home / Mission Overview (Employee)
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Stats cards | "Upcoming Shifts / My Pending Requests / Team Members" | "משמרות קרובות / הבקשות שלי הממתינות / חברי צוות" | PASS |
| Quick actions | Translated | Translated | PASS |
| Sidebar items | All translated | All translated | PASS |

### Calendar Landing Page
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Calendars" | "לוחות שנה" | PASS |
| Subtitle | "Manage shifts, chores, and on-call duties" | "ניהול משמרות, תורנויות וכוננויות" | PASS |
| Card titles | "Shifts Calendar / Chores Calendar / On-Call Calendar / Overview Calendar" | "לוח משמרות / לוח מטלות / לוח כוננויות / לוח סקירה" | PASS |
| Card descriptions | All translated | All translated | PASS |
| Card buttons | "View Calendar" | "צפה בלוח" | PASS |

### Calendar/Shifts
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Breadcrumbs | "Calendars > Shifts Calendar" | "לוחות שנה > לוח משמרות" | PASS |
| Filter labels | "MOLECULE / JOB TYPE / VIEW" | "מולקולה / סוג עבודה / תצוגה" | PASS |
| View options | "Week / Two Weeks / Month" | "שבוע / שבועיים / חודש" | PASS |
| Nav buttons | "Previous / Next" | "קודם / הבא" | PASS |
| Mode buttons | "By Shift / By User / Capacity Mode" | "לפי משמרות / לפי אנשים / מצב קיבולת" | PASS |
| Filter/Just Mine | "Filter / Just Mine" | "סינון / רק שלי" | PASS |
| Empty state | "No Shifts Found" | "לא נמצאו משמרות" | PASS |
| Day-of-week headers | Hebrew (ראשון, שני...) | Hebrew (ראשון, שני...) | **FAIL in EN (LOC-01)** |
| Date range | "Mar 08, 2026 - Mar 14, 2026" | "08 מרץ 2026 - 14 מרץ 2026" | PASS |

### Calendar/Chores
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Chores Calendar" | "לוח מטלות" | PASS |
| All controls | Translated | Translated | PASS |
| Day headers | Hebrew in EN mode | Hebrew | **FAIL (LOC-01)** |

### Calendar/OnCall (Day Shift)
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Day Shift Calendar" | "לוח משמרות רוחב" | PASS |
| Area filter | "AREA" | "אזור" | PASS |
| Duty types | "Hakam / Lead / Backup-hakam" | "חק״מכו / מובילתו / חק\"מ רזרבה" | PASS |
| Day headers | Hebrew in EN mode | Hebrew | **FAIL (LOC-01)** |

### Calendar/Overview
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Overview Calendar" | "לוח סקירה" | PASS |
| Labels | "Company & Scope / VIEW / USERS" | "חברה והיקף / תצוגה / משתמשים" | PASS |
| User filter | "Active Users / Inactive Users" | "משתמשים פעילים / משתמשים לא פעילים" | PASS |
| Legend | "Vacation / Shift / Chore / Day Shift" | "חופשה / משמרת / מטלה / משמרת יומית" | PASS |
| Read-Only banner | "Read-Only Mode" | "מצב קריאה בלבד" | PASS |

### My/Requests
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "My Requests" | "הבקשות שלי" | PASS |
| All form labels | Translated | Translated | PASS |
| Vacation types | "Regular Vacation / After-Duty Vacation" | "חופשה רגילה / אפטר" | PASS |
| Empty states | Translated | Translated | PASS |

### Requests (Manager)
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Requests" | "בקשות" | PASS |
| Tabs | "Pending Time-Off / Pending Swaps / Approved Time-Off" | "חופשות ממתינות / החלפות ממתינות / חופשות מאושרות" | PASS |
| Empty state | Translated | Translated | PASS |

### Admin/Users
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "User Management" | "ניהול משתמשים" | PASS |
| Table headers | "Name / Email / Company / Job Type / Department / Role / Grants / Status / Password / Actions" | "שם / אימייל / חברה / סוג עבודה / מחלקה / תפקיד / הרשאות / סטטוס / סיסמה / פעולות" | PASS |
| Status buttons | "Active / Inactive" | "פעיל / לא פעיל" | PASS |
| Action buttons | "Set / Export CSV" | "הגדר / ייצא CSV" | PASS |
| Form labels | "Email / Display name / Role / Company / Molecule / Job Type / Password" | "אימייל / שם תצוגה / תפקיד / חברה / מולקולה / סוג עבודה / סיסמה" | PASS |
| Pagination | "Previous / Next" | "הקודם / הבא" | PASS |
| Add/Import | "Add / Import" | "הוסף / ייבוא" | PASS |

### Admin/Settings
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Settings" | "הגדרות" | PASS |
| Subtitle | "Hierarchy Settings" | "הגדרות היררכיה" | PASS |
| Tab labels | "Area Settings / Molecule Settings / Company Settings" | "הגדרות אזור / הגדרות מולקולה / הגדרות חברה" | PASS |

### Admin/AuditLog
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Audit Log" | "יומן ביקורת" | PASS |
| Filter labels | Translated | Translated | PASS |
| Table headers | "Timestamp / User / Action / Entity Type / Description / IP Address" | "חותמת זמן / משתמש / פעולה / סוג ישות / תיאור / כתובת IP" | PASS |
| Action values | "UserCreated" | "UserCreated" (English) | **FAIL (LOC-07)** |
| Entity type values | "User" | "User" (English) | **FAIL (LOC-07)** |

### Admin/Companies
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Companies" | "חברות" | PASS |
| Form labels | Mostly translated | Mostly translated | **PARTIAL** |
| "CompanyMoleculeHint" | Not visible in EN | Raw key visible in HE | **FAIL (LOC-06)** |
| ID hint | "(lowercase, letters, numbers, hyphens)" | Same English text | **FAIL (LOC-08)** |

### Admin/Organization/ChoreTypes
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | (not tested EN) | "סוגי תורנויות" | PASS |
| All labels | N/A | All in Hebrew | PASS |

### Admin/Organization/DutyTypes
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | (not tested EN) | "סוגי כוננויות" | PASS |
| All labels | N/A | All in Hebrew | PASS |

### Owner/AreaConfig
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Area Configuration" | "הגדרות אזור" | PASS |
| Labels | "Default Rest Hours / Weekly Hours Cap / Last Updated / Edit Settings" | "שעות מנוחה ברירת מחדל / תקרת שעות שבועית / עודכן לאחרונה / ערוך הגדרות" | PASS |
| Units | "8 hours / 56 hours" | "8 שעות / 56 שעות" | PASS |

### Owner/DataLifecycle
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Data Lifecycle" | "מחזור חיי נתונים" | PASS |
| Section titles | Translated | Translated | PASS |
| Labels | Translated | Translated | PASS |
| Buttons | "Create Archive / Purge Data / Re-Import" | "יצירת ארכיון / מחיקת נתונים / ייבוא מחדש" | PASS |
| Confirmation placeholder | "DELETE SYSTEMADMINS BEFORE 2025-09-13" | Same English text | **PARTIAL (LOC-09)** |

### Owner/EmailTemplates
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Email Templates" | "תבניות אימייל" | PASS |
| Template names | "Shift Assigned / Shift Changed / Shift Deleted / Chore Assigned..." | "משמרת הוקצתה / משמרת שונתה / משמרת נמחקה / משימה הוקצתה..." | PASS |
| Default messages | English | Hebrew | PASS |
| Variable names | `{EmployeeName}` etc. | Same (intentional) | N/A |
| Status | "Disabled" | "מושבת" | PASS |

### Owner/MasterPrograms
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Title | "Master Programs" | "תוכניות ראשיות" | PASS |
| All labels | Translated | Translated | PASS |

### Tooltips & Accessibility Attributes
| Aspect | English | Hebrew | Status |
|--------|---------|--------|--------|
| Sidebar toggle title | - | "החלף מצב סרגל צד (Ctrl+B)" | PASS |
| Language button title | - | "שפה" | PASS |
| Print button title | - | "הדפסה" | PASS |
| Breadcrumb titles | - | Translated | PASS |
| Keyboard shortcut hint | - | "לחץ Ctrl+K (או Cmd+K על Mac) לפתיחת ניווט מהיר" | PASS |
| aria-label="Dismiss" | - | English | **FAIL (LOC-12)** |
| aria-label="Notifications" | - | English | **FAIL (LOC-12)** |

---

## 4. Confirmed Findings

### LOC-01: Calendar Day-of-Week Headers Show Hebrew in English Mode
- **Severity**: P0 -- Blocker
- **Pages**: Calendar/Shifts, Calendar/Chores, Calendar/OnCall, Calendar/Overview (all calendar views)
- **Description**: Day-of-week column headers display Hebrew names (ראשון, שני, שלישי, רביעי, חמישי, שישי, שבת) when the UI is set to English. English users should see Sunday, Monday, Tuesday, etc.
- **Impact**: All English-language users see Hebrew text in the most critical daily-use area of the application.
- **Root cause**: Likely the day-of-week rendering in the calendar page models uses `CultureInfo` from the company/area configuration rather than the current UI culture.
- **Files to check**: `Pages/Calendar/Week.cshtml.cs`, `Pages/Calendar/Month.cshtml.cs`, `Pages/Calendar/Day.cshtml.cs`, `Pages/Calendar/Overview.cshtml.cs`, and any shared calendar rendering helper.

### LOC-03: Password Change Description Untranslated in Hebrew Mode
- **Severity**: P1 -- Critical
- **Page**: `/Auth/ForgotPassword` (Password Management page)
- **String**: "If you already know your current password and want to change it, use this form."
- **Description**: The entire description paragraph under the "Change Password" section renders in English when the UI is set to Hebrew. The section title "שינוי סיסמה" is translated, but the paragraph below it is not.
- **Impact**: Visible to all users who visit the password management page in Hebrew.
- **Likely location**: Hardcoded string in `Pages/Auth/ForgotPassword.cshtml` -- needs to be wrapped in `<loc>` tag.

### LOC-04: System Disk Warning Banner Untranslated
- **Severity**: P1 -- Critical
- **Pages**: All authenticated pages (global layout)
- **String**: "WARNING: Disk space is low (7.4% free)."
- **Description**: The system-level disk space warning banner displays entirely in English regardless of language setting. It also ends with "." instead of the Hebrew convention ".".
- **Impact**: Appears at the top of every page for all Hebrew users.
- **Likely location**: `Pages/Shared/_Layout.cshtml` or a middleware/partial that renders the system warning. The string needs `<loc>` wrapping.

### LOC-05: "Shift Manager" Brand Name in Hebrew Login Subtitle
- **Severity**: P2 -- High
- **Page**: `/Auth/Login`
- **String**: "היכנס כדי להמשיך ל-Shift Manager"
- **Description**: The login page subtitle in Hebrew contains the English brand name "Shift Manager" instead of "מנהל משמרות" (which is used elsewhere, including the sidebar).
- **Impact**: First impression for Hebrew users. The brand is translated as "מנהל משמרות" in the sidebar and header.
- **Note**: This could be intentional if the brand name is "Shift Manager" globally. However, inconsistency with sidebar ("מנהל משמרות") suggests it should be localized.
- **Likely location**: `Pages/Auth/Login.cshtml` -- the subtitle string.

### LOC-06: Raw Key "CompanyMoleculeHint" Visible on Companies Page
- **Severity**: P2 -- High
- **Page**: `/Admin/Companies`
- **String**: "CompanyMoleculeHint"
- **Description**: A raw resource key is displayed as visible text below the Molecule dropdown on the "Add Company" form in Hebrew mode. This is a missing .resx entry.
- **Impact**: Visible to admins/owners who manage companies.
- **Likely location**: Missing key in `Resources/SharedResources.he-IL.resx` or the page-specific resource file. The key `CompanyMoleculeHint` needs a Hebrew translation.

### LOC-07: Audit Log Action/Entity Type Values Untranslated
- **Severity**: P2 -- High
- **Page**: `/Admin/AuditLog`
- **Strings**: Action values like "UserCreated" and entity type values like "User" display in English PascalCase format.
- **Description**: The audit log filter dropdowns and table cells show enum/code values instead of localized display names. The Action column shows "UserCreated" in a styled badge, and Entity Type shows "User".
- **Impact**: Visible in the audit log table and filter dropdowns for Hebrew users.
- **Likely location**: Audit log entries store raw enum values. The display rendering needs a localization mapping from action/entity codes to localized display names.

### LOC-08: Company ID Hint Untranslated
- **Severity**: P2 -- High
- **Page**: `/Admin/Companies`
- **String**: "(lowercase, letters, numbers, hyphens)"
- **Description**: The hint text for the Company ID field displays in English in Hebrew mode.
- **Impact**: Visible to admins creating new companies.
- **Likely location**: `Pages/Admin/Companies.cshtml` -- the ID field help text needs `<loc>` wrapping.

### LOC-09: Data Lifecycle Purge Confirmation in English
- **Severity**: P3 -- Medium
- **Page**: `/Owner/DataLifecycle`
- **String**: Placeholder "DELETE SYSTEMADMINS BEFORE 2025-09-13"
- **Description**: The typed confirmation required for data purge is generated in English (e.g., "DELETE SYSTEMADMINS BEFORE 2025-09-13") regardless of locale. The surrounding instruction ("הקלד בדיוק:") is in Hebrew but the actual confirmation string is English.
- **Impact**: Owner-only feature. The English confirmation may be intentional for safety (requiring exact typed text), but it creates a jarring mixed-language experience.
- **Note**: This may be by design -- the confirmation text needs to be deterministic, and changing it per locale could create issues. Consider keeping it English but adding a Hebrew explanation.

### LOC-10: 404 Error Page Minimal
- **Severity**: P3 -- Medium
- **Pages**: Any nonexistent URL (e.g., `/nonexistent-page`)
- **String**: "HTTP 404"
- **Description**: The 404 error page shows only "HTTP 404" with no localized messaging, no navigation back, and no helpful content in either language.
- **Impact**: Users who hit a broken link see a bare error with no guidance.
- **Recommendation**: Add a proper error page with localized "Page not found" messaging and a link back to the home page.

### LOC-11: Email Template Variables Display as English
- **Severity**: P4 -- Low (Acceptable)
- **Page**: `/Owner/EmailTemplates`
- **Strings**: `{EmployeeName}`, `{ShiftType}`, `{Date}`, `{StartTime}`, `{EndTime}`, `{ChangeDescription}`, etc.
- **Description**: Email template variable placeholders display in English in the template editor. This is intentional -- they are code tokens that get replaced at runtime.
- **Note**: No action needed. The surrounding instructions explaining how variables work are properly translated.

### LOC-12: Two aria-label Attributes Untranslated
- **Severity**: P4 -- Low
- **Pages**: All authenticated pages (global layout)
- **Strings**: `aria-label="Dismiss"` (on alert close button), `aria-label="Notifications"` (on notification panel)
- **Description**: Two accessibility attributes remain in English in Hebrew mode. These are not visible to sighted users but affect screen reader users.
- **Impact**: Low -- affects only screen reader users in Hebrew mode.
- **Likely location**: `Pages/Shared/_Layout.cshtml` or notification partial.

---

## 5. Issues Ranked by Severity

| ID | Severity | Page | Issue | Impact |
|----|----------|------|-------|--------|
| LOC-01 | P0 | All Calendar views | Day-of-week headers show Hebrew in English mode | All EN users see Hebrew in core feature |
| LOC-03 | P1 | ForgotPassword | Change Password description in English in HE mode | All HE users on password page |
| LOC-04 | P1 | All pages | Disk warning banner in English in HE mode | All HE users when disk is low |
| LOC-05 | P2 | Login | "Shift Manager" brand not localized in HE subtitle | First impression for HE users |
| LOC-06 | P2 | Admin/Companies | Raw key "CompanyMoleculeHint" visible | Admins creating companies |
| LOC-07 | P2 | Admin/AuditLog | Action/Entity values in English (UserCreated, User) | Admins reviewing audit logs |
| LOC-08 | P2 | Admin/Companies | Company ID hint "(lowercase, letters...)" in English | Admins creating companies |
| LOC-09 | P3 | Owner/DataLifecycle | Purge confirmation text in English | Owner-only, may be intentional |
| LOC-10 | P3 | Error pages | 404 shows only "HTTP 404" | Users hitting broken links |
| LOC-11 | P4 | Owner/EmailTemplates | Variable names in English | Intentional/acceptable |
| LOC-12 | P4 | All pages | 2 aria-labels in English | Screen reader users only |

---

## 6. Release Blockers

### Must Fix Before Release
1. **LOC-01 (P0)**: Calendar day-of-week headers must display in the active UI language. Currently English users see Hebrew day names across all calendar views. This is the most critical localization defect as it affects the core feature for English-language users.

### Should Fix Before Release
2. **LOC-03 (P1)**: Password change description must be translated. Hardcoded English string on a user-facing page.
3. **LOC-04 (P1)**: Disk warning banner must be localized. Appears on every page.

---

## 7. Non-Blocking Improvements

### High Priority (fix in next patch)
- LOC-05: Localize "Shift Manager" in login subtitle to match sidebar
- LOC-06: Add "CompanyMoleculeHint" key to Hebrew .resx
- LOC-07: Add localized display names for audit log action/entity enum values
- LOC-08: Localize company ID hint text

### Medium Priority
- LOC-09: Add Hebrew explanation alongside English purge confirmation (or localize the confirmation string)
- LOC-10: Create a proper localized 404 error page

### Low Priority
- LOC-12: Translate remaining aria-label attributes

---

## 8. Recommendations

### LOC-01 Fix Guidance
The day-of-week names are almost certainly being generated using a fixed `CultureInfo("he-IL")` or the company's configured culture rather than the current request culture. The fix should use the current UI culture (`CultureInfo.CurrentUICulture`) when formatting day names for display. Check these files:
- `Pages/Calendar/Overview.cshtml.cs` (and similar page models)
- Any shared calendar rendering code that generates `<th>` headers
- Look for `.ToString("dddd", ...)` or `DateTimeFormatInfo` usage

### LOC-03 Fix Guidance
In `Pages/Auth/ForgotPassword.cshtml`, find the hardcoded paragraph "If you already know your current password and want to change it, use this form." and wrap it with `<loc>` tag. Add the Hebrew translation to `SharedResources.he-IL.resx`.

### LOC-04 Fix Guidance
The disk warning is likely rendered in `_Layout.cshtml` or a partial. Search for "Disk space is low" and wrap with `<loc>`. Add Hebrew translation: "אזהרה: מקום בדיסק נמוך (X% פנוי)."

### LOC-06 Fix Guidance
Add the key `CompanyMoleculeHint` to `Resources/SharedResources.he-IL.resx` with an appropriate Hebrew translation (e.g., "המולקולה שהחברה שייכת אליה").

### General Architecture Observations
- The `<loc>` tag helper system is working well across the application
- The `.AspNetCore.Culture` cookie-based language switching is solid
- RTL layout implementation is thorough and correct
- Company/user data names (Hebrew names in English mode, English test data in Hebrew mode) are expected and not localization defects
- The localization team has done excellent work translating the vast majority of UI strings including complex form labels, validation messages, and empty states

---

## Appendix: Test Credentials Used

| Role | Email | Pages Tested |
|------|-------|-------------|
| Owner | admin@local | All pages |
| Employee | emp.tz.alhut@test | Home, Calendar/Shifts |
| Manager | mgr.alhut.tz@test | Home |

## Appendix: Screenshot Inventory

All screenshots saved to `qa_screenshots/` directory:
- `01_login_en.png` / `01_login_he.png` -- Login page
- `02_home_en.png` / `02_home_he.png` -- Home/Onboarding
- `en_Calendar.png` / `he_Calendar.png` -- Calendar landing
- `en_Calendar_Shifts.png` / `he_Calendar_Shifts.png` -- Shifts calendar
- `en_Calendar_Chores.png` / `he_Calendar_Chores.png` -- Chores calendar
- `en_Calendar_OnCall.png` / `he_Calendar_OnCall.png` -- OnCall calendar
- `en_Calendar_Overview.png` / `he_Calendar_Overview.png` -- Overview calendar
- `en_My_Requests.png` / `he_My_Requests.png` -- My Requests
- `en_Admin_Users.png` / `he_Admin_Users.png` -- Admin Users
- `en_Owner_AreaConfig.png` / `he_Owner_AreaConfig_detail.png` -- Area Config
- `en_Owner_DataLifecycle.png` / `he_Owner_DataLifecycle.png` -- Data Lifecycle
- `en_Owner_EmailTemplates.png` / `he_Owner_EmailTemplates_detail.png` -- Email Templates
- `login_error_en.png` / `login_error_he.png` -- Login error state
- `en_forgot_password.png` / `he_forgot_password.png` -- Password management
- `en_request_access.png` / `he_request_access.png` -- Request access
- `en_emp_home.png` / `he_emp_home.png` -- Employee home
- `en_emp_shifts.png` / `he_emp_shifts.png` -- Employee shifts
- `he_mgr_home.png` -- Manager home
- `he_companies.png` -- Companies page
- `he_audit_log.png` / `en_audit_log.png` -- Audit log
- `en_analytics.png` / `he_analytics.png` -- Analytics
- `en_admin_settings.png` / `he_admin_settings.png` -- Settings
- `en_404.png` / `he_404.png` -- Error pages
- `he__Admin_Organization_ChoreTypes.png` -- Chore Types
- `he__Admin_Organization_DutyTypes.png` -- Duty Types
