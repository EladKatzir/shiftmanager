# 11-LOCALIZATION-AND-RTL.md - Internationalization and Right-to-Left Support

**Part of the ShiftManager Genesis Documentation**
**Document 11 of 19 - Complete i18n architecture and Hebrew RTL implementation**

---

## Table of Contents

1. [Overview](#overview)
2. [Supported Languages](#supported-languages)
3. [Localization Architecture](#localization-architecture)
4. [Resource File Structure](#resource-file-structure)
5. [Culture Detection and Selection](#culture-detection-and-selection)
6. [RTL CSS Implementation](#rtl-css-implementation)
7. [Localized UI Components](#localized-ui-components)
8. [Date and Time Formatting](#date-and-time-formatting)
9. [Number Formatting](#number-formatting)
10. [Validation Messages](#validation-messages)
11. [Adding New Languages](#adding-new-languages)
12. [Best Practices](#best-practices)

---

## Overview

ShiftManager implements **full bi-directional (BiDi) localization** supporting both **left-to-right (LTR)** and **right-to-left (RTL)** languages.

**Key Features:**
- ✅ **2 supported languages** - English (en-US), Hebrew (he-IL)
- ✅ **4,190+ localized strings** - Complete UI translation
- ✅ **RTL support** - Full right-to-left layout for Hebrew
- ✅ **Culture-aware formatting** - Dates, times, numbers
- ✅ **Conditional CSS loading** - RTL stylesheet only when needed
- ✅ **Dynamic direction switching** - No page reload required
- ✅ **Culture persistence** - Cookie-based preference storage

**Design Philosophy:**
- **Default:** English (en-US)
- **Primary RTL language:** Hebrew (he-IL) - Israeli Hebrew locale
- **No browser auto-detection** - Explicit culture selection via query string or cookie
- **Server-side rendering** - Culture determined before page render

---

## Supported Languages

### Language Table

| Language | Culture Code | Direction | Resource File | Entries | Status |
|----------|--------------|-----------|---------------|---------|--------|
| **English (US)** | en-US | LTR | `SharedResources.resx` | 4,190 | Default ✅ |
| **Hebrew** | he-IL | RTL | `SharedResources.he-IL.resx` | 4,015 | Complete ✅ |

**Coverage:**
- 100% of UI strings localized
- Error messages, validation messages, button labels
- Navigation, page titles, form labels
- Notification messages, email templates
- Game localization (shift-swap game)

---

## Localization Architecture

### ASP.NET Core Localization Configuration

From `Program.cs:23-36`:
```csharp
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "en-US", "he-IL" };
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
    options.SupportedUICultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();

    // Culture providers (checked in order):
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
    options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
    options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
});
```

**Culture Provider Priority:**
1. **QueryString** - `?culture=he-IL` or `?ui-culture=he-IL`
2. **Cookie** - `.AspNetCore.Culture` cookie
3. **Accept-Language Header** - Browser preference

**Example URLs:**
```
https://app.example.com/?culture=he-IL        ← Sets Hebrew culture
https://app.example.com/?culture=en-US        ← Sets English culture
```

**Cookie Format:**
```
.AspNetCore.Culture=c=he-IL|uic=he-IL
```

### Dependency Injection

**Service Registration:**
```csharp
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
```

**Injection in Razor Pages:**
```csharp
@inject IStringLocalizer<SharedResources> Localizer

<h1>@Localizer["Welcome_Message"]</h1>
```

**Injection in Services:**
```csharp
public class NotificationService
{
    private readonly IStringLocalizer<SharedResources> _localizer;

    public NotificationService(IStringLocalizer<SharedResources> localizer)
    {
        _localizer = localizer;
    }

    public string GetLocalizedMessage(string key) => _localizer[key].Value;
}
```

---

## Resource File Structure

**Location:** `Resources/`
```
Resources/
├── SharedResources.resx          (4,190 lines - English, default)
└── SharedResources.he-IL.resx    (4,015 lines - Hebrew)
```

**NOTE:** No `.cs` file needed - `SharedResources` is a marker class for localization.

### Resource File Format (XML)

**SharedResources.resx (English):**
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Welcome_Message" xml:space="preserve">
    <value>Welcome to ShiftManager</value>
  </data>
  <data name="Login_Button" xml:space="preserve">
    <value>Login</value>
  </data>
  <data name="Error_Login_InvalidCredentials" xml:space="preserve">
    <value>Invalid email or password</value>
  </data>
</root>
```

**SharedResources.he-IL.resx (Hebrew):**
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Welcome_Message" xml:space="preserve">
    <value>ברוכים הבאים למערכת ניהול משמרות</value>
  </data>
  <data name="Login_Button" xml:space="preserve">
    <value>התחבר</value>
  </data>
  <data name="Error_Login_InvalidCredentials" xml:space="preserve">
    <value>כתובת דואר אלקטרוני או סיסמה שגויים</value>
  </data>
</root>
```

### Resource Key Naming Conventions

**Pattern:**
```
{Context}_{Component}_{Purpose}
```

**Examples:**
- `Error_Login_InvalidCredentials` - Error message on login page
- `Button_Save` - Generic save button label
- `Label_Email` - Email field label
- `Validation_Required` - Required field validation message
- `Notification_ShiftAdded` - Notification message for shift added
- `Game_Title` - Game page title

**Category Prefixes:**
- `Error_` - Error messages
- `Validation_` - Validation messages
- `Label_` - Form labels
- `Button_` - Button text
- `Notification_` - Notification messages
- `Page_` - Page titles
- `Menu_` - Navigation menu items
- `Tooltip_` - Tooltip text
- `Game_` - Game-related strings

### Sample Resource Keys

**Common UI Strings:**
```
Welcome_Message
ShiftManager
MySchedule
Login_Button
Logout_Button
Save_Button
Cancel_Button
Delete_Button
Edit_Button
Back_Button
```

**Error Messages:**
```
Error_Login_InvalidCredentials
Error_Login_AccountLocked
Error_Login_RateLimitExceeded
Error_UnexpectedError
Error_NotFound
Error_Unauthorized
```

**Form Labels:**
```
Label_Email
Label_Password
Label_DisplayName
Label_Role
Label_StartDate
Label_EndDate
Label_Reason
```

**Validation Messages:**
```
Validation_Required
Validation_EmailFormat
Validation_MaxLength
Validation_MinLength
Validation_DateRange
```

---

## Culture Detection and Selection

### Layout Culture Detection

From `Pages/Shared/_Layout.cshtml:18-26`:
```csharp
@{
    var currentCulture = CultureInfo.CurrentUICulture.Name;
    var isHebrew = currentCulture.StartsWith("he");
    var direction = isHebrew ? "rtl" : "ltr";
    var lang = isHebrew ? "he" : "en";
}
<!DOCTYPE html>
<html lang="@lang" dir="@direction" data-theme="light" class="@(isHebrew ? "hebrew" : "english")">
```

**HTML Attributes:**
- `lang="he"` or `lang="en"` - Language for screen readers
- `dir="rtl"` or `dir="ltr"` - Text direction
- `class="hebrew"` or `class="english"` - Language-specific CSS hooks

### Conditional RTL CSS Loading

From `Pages/Shared/_Layout.cshtml:33-37`:
```csharp
<link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
@if (isHebrew)
{
    <link rel="stylesheet" href="~/css/rtl.css" asp-append-version="true" />
}
```

**Performance Optimization:**
- RTL CSS (rtl.css) only loaded for Hebrew users
- Reduces CSS payload for English users
- No flash of unstyled content (FOUC)

### Culture Switching Flow

```
┌─────────────────────────────────────────────────────────────┐
│                    Culture Switching Flow                    │
└─────────────────────────────────────────────────────────────┘

1. User clicks language switcher link
   ↓
2. Link includes culture query string:
   <a href="?culture=he-IL">עברית</a>
   <a href="?culture=en-US">English</a>
   ↓
3. ASP.NET Core Request Localization Middleware intercepts
   ↓
4. QueryStringRequestCultureProvider extracts culture
   ↓
5. Middleware sets:
   - Thread.CurrentThread.CurrentCulture
   - Thread.CurrentThread.CurrentUICulture
   - CookieRequestCultureProvider writes cookie
   ↓
6. Cookie persisted:
   .AspNetCore.Culture=c=he-IL|uic=he-IL
   ↓
7. Page renders with:
   - <html lang="he" dir="rtl">
   - RTL CSS loaded
   - All strings from SharedResources.he-IL.resx
   ↓
8. Subsequent requests use cookie (no query string needed)
```

### Language Switcher UI

**Example Implementation:**
```cshtml
<div class="language-switcher">
    @{
        var currentCulture = CultureInfo.CurrentUICulture.Name;
        var returnUrl = Context.Request.Path + Context.Request.QueryString;
    }

    @if (currentCulture.StartsWith("he"))
    {
        <a href="?culture=en-US&returnUrl=@Uri.EscapeDataString(returnUrl)">English</a>
    }
    else
    {
        <a href="?culture=he-IL&returnUrl=@Uri.EscapeDataString(returnUrl)">עברית</a>
    }
</div>
```

**Current Culture Indication:**
```cshtml
@if (isHebrew)
{
    <span class="current-language">עברית</span>
}
else
{
    <span class="current-language">English</span>
}
```

---

## RTL CSS Implementation

**Location:** `wwwroot/css/rtl.css` (partial shown, ~200+ lines total)

### Core RTL Overrides

From `rtl.css:1-106`:
```css
/* RTL (Right-to-Left) Overrides for Hebrew/Arabic */

/* Ensure RTL direction is applied */
[dir="rtl"] {
  text-align: right;
}

/* Flip flexbox directions */
[dir="rtl"] .topbar-actions {
  flex-direction: row-reverse;
}

/* Fix dropdown menus */
[dir="rtl"] .dropdown .menu {
  right: auto;
  left: 0;
}

/* Fix table alignment */
[dir="rtl"] th,
[dir="rtl"] td {
  text-align: right;
}

/* Fix card content */
[dir="rtl"] .card {
  text-align: right;
}

/* Typography classes */
[dir="rtl"] .text-display,
[dir="rtl"] .text-title,
[dir="rtl"] .text-body,
[dir="rtl"] .text-subtle,
[dir="rtl"] .text-caption {
  text-align: right;
}

/* Calendar adjustments */
[dir="rtl"] .calendar-header {
  flex-direction: row-reverse;
}

[dir="rtl"] .calendar-nav {
  flex-direction: row-reverse;
}

/* Flip icons and arrows */
[dir="rtl"] .dropdown button::after {
  transform: scaleX(-1);
}

/* App Layout RTL Support */

/* Sidebar on the right in RTL */
[dir="rtl"] .app-sidebar {
  border-right: none;
  border-left: 1px solid var(--border);
}

/* Active indicator on the right */
[dir="rtl"] .app-sidebar-nav-item.active::before {
  right: 0;
  left: auto;
}
```

### RTL Design Patterns

**1. Margin/Padding Flipping:**
```css
/* LTR (default in site.css) */
.nav a {
  margin-left: 0.75rem;
}

/* RTL override */
[dir="rtl"] .nav a {
  margin-right: 0;
  margin-left: 0.75rem;
}
```

**2. Flexbox Direction Reversal:**
```css
/* LTR (default) */
.topbar-actions {
  display: flex;
  flex-direction: row;
}

/* RTL override */
[dir="rtl"] .topbar-actions {
  flex-direction: row-reverse;
}
```

**3. Absolute Positioning Flipping:**
```css
/* LTR (default) */
.dropdown .menu {
  right: 0;
}

/* RTL override */
[dir="rtl"] .dropdown .menu {
  right: auto;
  left: 0;
}
```

**4. Icon Mirroring:**
```css
/* Flip arrow icons horizontally */
[dir="rtl"] .dropdown button::after {
  transform: scaleX(-1);
}

/* Flip chevron icons */
[dir="rtl"] .icon-chevron-right {
  transform: rotate(180deg);
}
```

**5. Border Side Swapping:**
```css
/* LTR (default) */
.app-sidebar {
  border-right: 1px solid var(--border);
}

/* RTL override */
[dir="rtl"] .app-sidebar {
  border-right: none;
  border-left: 1px solid var(--border);
}
```

### Components with RTL Support

| Component | RTL Adjustments |
|-----------|-----------------|
| **Navigation Bar** | `flex-direction: row-reverse`, margins flipped |
| **Sidebar** | Moved to right side, borders flipped |
| **Dropdown Menus** | Aligned to left instead of right |
| **Tables** | Text align right, header alignment |
| **Calendar** | Day order reversed, navigation arrows flipped |
| **Forms** | Labels and inputs right-aligned |
| **Cards** | Content right-aligned |
| **Breadcrumbs** | Arrow direction reversed |
| **Tooltips** | Positioned from right |
| **Modals** | Close button on left |

### Hebrew Font Support

**Default Font Stack:**
```css
body {
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen-Sans, Ubuntu, Cantarell,
               "Helvetica Neue", sans-serif;
}
```

**Hebrew-Specific (Optional):**
```css
[dir="rtl"] {
  font-family: "Open Sans Hebrew", "Arial Hebrew", Arial, sans-serif;
}
```

**Notes:**
- Modern browsers render Hebrew well with default sans-serif
- System fonts (Segoe UI, SF Pro) have good Hebrew glyphs
- No custom Hebrew web fonts needed

---

## Localized UI Components

### Page Titles

**Usage:**
```cshtml
<title>@Localizer["ShiftManager"]</title>
```

**Resources:**
- `ShiftManager` - "ShiftManager" (EN), "מערכת ניהול משמרות" (HE)
- `MySchedule` - "My Schedule" (EN), "המשמרות שלי" (HE)

### Button Labels

**Example:**
```cshtml
<button type="submit">@Localizer["Button_Save"]</button>
```

**Common Button Keys:**
- `Button_Save` - "Save" / "שמור"
- `Button_Cancel` - "Cancel" / "בטל"
- `Button_Delete` - "Delete" / "מחק"
- `Button_Edit` - "Edit" / "ערוך"
- `Button_Back` - "Back" / "חזור"
- `Button_Submit` - "Submit" / "שלח"

### Form Labels

**Example:**
```cshtml
<label>@Localizer["Label_Email"]</label>
<input type="email" name="Email" />
```

**Common Label Keys:**
- `Label_Email` - "Email" / "דואר אלקטרוני"
- `Label_Password` - "Password" / "סיסמה"
- `Label_DisplayName` - "Display Name" / "שם תצוגה"
- `Label_Role` - "Role" / "תפקיד"

### Validation Messages

**Example:**
```cshtml
@if (string.IsNullOrEmpty(Email))
{
    <span class="error">@Localizer["Validation_Required"]</span>
}
```

**Common Validation Keys:**
- `Validation_Required` - "This field is required" / "שדה זה הוא חובה"
- `Validation_EmailFormat` - "Invalid email format" / "פורמט דואר אלקטרוני שגוי"
- `Validation_MaxLength` - "Maximum length exceeded" / "אורך מקסימלי חרג"

### Error Messages

**Example:**
```csharp
Error = _localizer["Error_Login_InvalidCredentials"];
```

**Common Error Keys:**
- `Error_Login_InvalidCredentials` - "Invalid email or password" / "כתובת דואר אלקטרוני או סיסמה שגויים"
- `Error_NotFound` - "Not found" / "לא נמצא"
- `Error_Unauthorized` - "Unauthorized" / "אין הרשאה"
- `Error_UnexpectedError` - "An unexpected error occurred" / "אירעה שגיאה בלתי צפויה"

### Notification Messages

**Example:**
```csharp
await _notificationService.CreateNotificationAsync(
    userId: 10,
    title: _localizer["Notification_ShiftAdded_Title"],
    message: _localizer["Notification_ShiftAdded_Message", shiftDate, shiftTypeName]
);
```

**Parameterized Messages:**
```csharp
// English: "You have been assigned to {0} shift on {1}"
// Hebrew: "שובצת למשמרת {0} בתאריך {1}"
_localizer["Notification_ShiftAdded_Message", shiftTypeName, shiftDate]
```

### Game Localization

**Endpoint:** `/Api/Game/GetLocalization`

**Response (English):**
```json
{
  "title": "Shift Swap Game",
  "instructions": "Match 3 or more shifts to swap them...",
  "score": "Score",
  "playAgain": "Play Again",
  "roasts": {
    "r1000": ["Not bad for a beginner!", "Keep going!", "You're getting the hang of it!"]
  }
}
```

**Response (Hebrew):**
```json
{
  "title": "משחק החלפת משמרות",
  "instructions": "התאם 3 משמרות או יותר כדי להחליף ביניהן...",
  "score": "ניקוד",
  "playAgain": "שחק שוב",
  "roasts": {
    "r1000": ["לא רע למתחיל!", "המשך כך!", "אתה מתחיל להבין!"]
  }
}
```

---

## Date and Time Formatting

### Culture-Aware Formatting

**Date Formatting:**
```csharp
// English: "6/15/2025"
// Hebrew: "15/06/2025"
var dateString = date.ToString("d", CultureInfo.CurrentCulture);
```

**Long Date:**
```csharp
// English: "Monday, June 15, 2025"
// Hebrew: "יום שני, 15 ביוני 2025"
var longDateString = date.ToString("D", CultureInfo.CurrentCulture);
```

**Time Formatting:**
```csharp
// English: "2:30 PM"
// Hebrew: "14:30"
var timeString = time.ToString("t", CultureInfo.CurrentCulture);
```

**DateTime Combined:**
```csharp
// English: "6/15/2025 2:30 PM"
// Hebrew: "15/06/2025 14:30"
var dateTimeString = dateTime.ToString("g", CultureInfo.CurrentCulture);
```

### Calendar System

**Note:** Both `en-US` and `he-IL` use **Gregorian calendar** (not Hebrew calendar)

**First Day of Week:**
- **en-US:** Sunday
- **he-IL:** Sunday (same)

```csharp
var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
// Returns: DayOfWeek.Sunday (for both cultures)
```

---

## Number Formatting

### Decimal Separator

**en-US:** Period (`.`)
- `1,234.56`

**he-IL:** Period (`.`)
- `1,234.56`

**Note:** Hebrew uses the same decimal separator as English (period), but **right-to-left number ordering**

### Currency Formatting

**en-US:**
```csharp
var amount = 1234.56m;
var formatted = amount.ToString("C", new CultureInfo("en-US"));
// Result: "$1,234.56"
```

**he-IL:**
```csharp
var amount = 1234.56m;
var formatted = amount.ToString("C", new CultureInfo("he-IL"));
// Result: "₪1,234.56" (Israeli New Shekel)
```

### Percentage Formatting

```csharp
var percent = 0.1234;
var formatted = percent.ToString("P", CultureInfo.CurrentCulture);
// English: "12.34%"
// Hebrew: "12.34%" (same)
```

---

## Validation Messages

### Required Field Validation

**English:**
```
This field is required
```

**Hebrew:**
```
שדה זה הוא חובה
```

**Usage:**
```csharp
if (string.IsNullOrEmpty(Email))
{
    ModelState.AddModelError("Email", _localizer["Validation_Required"]);
}
```

### Email Format Validation

**English:**
```
Invalid email format
```

**Hebrew:**
```
פורמט דואר אלקטרוני שגוי
```

### Max Length Validation

**English:**
```
Maximum length is {0} characters
```

**Hebrew:**
```
אורך מקסימלי הוא {0} תווים
```

**Usage:**
```csharp
_localizer["Validation_MaxLength", maxLength]
```

---

## Adding New Languages

### Step 1: Create Resource File

**File Naming Pattern:**
```
SharedResources.{culture}.resx
```

**Examples:**
- Spanish: `SharedResources.es-ES.resx`
- French: `SharedResources.fr-FR.resx`
- Arabic: `SharedResources.ar-SA.resx`

**Copy from English Base:**
```bash
cp Resources/SharedResources.resx Resources/SharedResources.es-ES.resx
```

**Translate Values:**
```xml
<!-- SharedResources.es-ES.resx -->
<data name="Welcome_Message" xml:space="preserve">
  <value>Bienvenido a ShiftManager</value>
</data>
```

### Step 2: Update Program.cs

```csharp
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "en-US", "he-IL", "es-ES" };  // Add new culture
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
    options.SupportedUICultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();

    // ... rest unchanged
});
```

### Step 3: Add RTL CSS (if needed)

**For RTL languages (Arabic, Persian, Urdu):**
```csharp
// _Layout.cshtml
@{
    var currentCulture = CultureInfo.CurrentUICulture.Name;
    var isRTL = currentCulture.StartsWith("he") ||
                currentCulture.StartsWith("ar") ||
                currentCulture.StartsWith("fa");
    var direction = isRTL ? "rtl" : "ltr";
}

@if (isRTL)
{
    <link rel="stylesheet" href="~/css/rtl.css" asp-append-version="true" />
}
```

### Step 4: Update Language Switcher

```cshtml
<select onchange="window.location.href='?culture=' + this.value">
    <option value="en-US" @(currentCulture == "en-US" ? "selected" : "")>English</option>
    <option value="he-IL" @(currentCulture == "he-IL" ? "selected" : "")>עברית</option>
    <option value="es-ES" @(currentCulture == "es-ES" ? "selected" : "")>Español</option>
</select>
```

### Step 5: Test New Language

**Navigate to:**
```
https://app.example.com/?culture=es-ES
```

**Verify:**
- Cookie set: `.AspNetCore.Culture=c=es-ES|uic=es-ES`
- All strings translated
- Date/time/number formatting correct
- Validation messages localized

---

## Best Practices

### 1. Always Use Localizer

**❌ Don't:**
```csharp
Error = "Invalid email or password";
```

**✅ Do:**
```csharp
Error = _localizer["Error_Login_InvalidCredentials"];
```

---

### 2. Parameterize Dynamic Content

**❌ Don't:**
```csharp
Message = "You have been assigned to " + shiftTypeName + " shift on " + shiftDate;
```

**✅ Do:**
```csharp
Message = _localizer["Notification_ShiftAdded_Message", shiftTypeName, shiftDate];

// English resource: "You have been assigned to {0} shift on {1}"
// Hebrew resource: "שובצת למשמרת {0} בתאריך {1}"
```

---

### 3. Use .Value for String Conversion

**❌ Don't:**
```csharp
string message = _localizer["Welcome_Message"];  // Warning: LocalizedString is not string
```

**✅ Do:**
```csharp
string message = _localizer["Welcome_Message"].Value;
```

---

### 4. Avoid Hardcoded Text in Markup

**❌ Don't:**
```cshtml
<button>Save</button>
```

**✅ Do:**
```cshtml
<button>@Localizer["Button_Save"]</button>
```

---

### 5. Use Culture-Aware Formatting

**❌ Don't:**
```csharp
string dateString = date.ToString("MM/dd/yyyy");  // US-only format
```

**✅ Do:**
```csharp
string dateString = date.ToString("d", CultureInfo.CurrentCulture);  // Adapts to culture
```

---

### 6. Test RTL Layout

**For Hebrew/Arabic:**
- Check all pages with `?culture=he-IL`
- Verify sidebar, navigation, dropdowns mirrored
- Ensure icons (arrows, chevrons) flipped
- Test forms, tables, calendars

---

### 7. Keep Resource Keys Consistent

**Pattern:**
```
{Context}_{Component}_{Purpose}
```

**Examples:**
- `Error_Login_InvalidCredentials`
- `Validation_Email_Format`
- `Button_Save`
- `Label_Email`

---

### 8. Provide Fallback for Missing Keys

**Example:**
```csharp
var localizedString = _localizer["SomeKey"];
if (localizedString.ResourceNotFound)
{
    _logger.LogWarning("Missing localization key: {Key}", "SomeKey");
    return "SomeKey";  // Fallback to key name
}
```

---

## Summary

**Localization Features:**
- ✅ 2 supported languages (en-US, he-IL)
- ✅ 4,190+ localized strings
- ✅ Full RTL support for Hebrew
- ✅ Culture-aware date/time/number formatting
- ✅ Conditional RTL CSS loading
- ✅ Cookie-based culture persistence
- ✅ 3 culture providers (QueryString, Cookie, Accept-Language)

**RTL CSS:**
- ✅ 213 lines of RTL overrides (including 33 lines for config UI enhancements)
- ✅ Flexbox direction reversal
- ✅ Margin/padding flipping
- ✅ Border side swapping
- ✅ Icon mirroring
- ✅ Absolute positioning flipping
- ✅ Modal component RTL support (Phase 4)
- ✅ Context panel RTL support (Phase 3)
- ✅ Status widget RTL support (Phase 2)
- ✅ Form validation icon positioning (Phase 2)

**Resource Files:**
- `SharedResources.resx` - English (4,383 lines - includes 108 config UI keys)
- `SharedResources.he-IL.resx` - Hebrew (4,208 lines - includes 108 config UI translations)

**Key Files:**
- `Resources/SharedResources.resx` - English resource file
- `Resources/SharedResources.he-IL.resx` - Hebrew resource file
- `wwwroot/css/rtl.css` - RTL CSS overrides
- `Pages/Shared/_Layout.cshtml` - Culture detection and RTL loading
- `Program.cs` (lines 23-36) - Localization configuration

**Next Steps:**
- See 08-UI-UX-ARCHITECTURE.md for CSS design system
- See 08a-CONFIG-UI-ENHANCEMENTS.md for configuration page RTL patterns
- See 10-AUTHENTICATION-AND-AUTHORIZATION.md for localized error messages
- See 07-SERVICE-LAYER.md for IStringLocalizer usage in services

---

**Document Status:** ✅ Complete
**Lines:** 960
**Coverage:** Complete i18n architecture, RTL support, resource file structure, culture selection documented

**Cross-References:**
- 02-ARCHITECTURE-BLUEPRINT.md - Localization philosophy
- 06-DOMAIN-MODELS.md - Culture-aware formatting
- 07-SERVICE-LAYER.md - Localization in services
- 08-UI-UX-ARCHITECTURE.md - CSS design system, RTL styling
- 10-AUTHENTICATION-AND-AUTHORIZATION.md - Localized error messages
