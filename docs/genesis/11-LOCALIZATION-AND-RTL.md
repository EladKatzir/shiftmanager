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

---

## Language Management Feature

**Added:** January 2026
**Version:** 1.0

ShiftManager implements a comprehensive **Language Management** system that allows **Owner users** to customize translations per company. This enables each organization to use their own terminology, creating a truly personalized user experience.

### Overview

The Language Management feature provides:

- ✅ **Company-scoped terminology overrides** - Different companies can customize the same resource keys with their own vocabulary
- ✅ **In-app click-to-edit workflow** - Owners can click on visible text to edit translations with draft mode
- ✅ **Two-language toggle per company** - Each company defines default and alternate language
- ✅ **Company default language** - Sets initial preference for new users without culture cookie
- ✅ **`<loc>` tag helper** - Renders localized text with `data-loc-key` attributes for in-app editing
- ✅ **Draft-based editing** - Changes are saved to sessionStorage and committed in bulk
- ✅ **In-memory caching** - Company overrides cached for 1 hour (automatic invalidation)
- ✅ **XSS prevention** - All override values are HTML-encoded
- ✅ **Audit logging** - All changes tracked with who/when/what

### Architecture Components

#### 1. Database Schema

**CompanyLanguageSettings Table:**
```sql
CREATE TABLE CompanyLanguageSettings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NOT NULL UNIQUE,
    DefaultCulture TEXT NOT NULL DEFAULT 'en-US',
    AlternateCulture TEXT NOT NULL DEFAULT 'he-IL',
    CreatedAt TEXT NOT NULL,
    CreatedBy INTEGER NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UpdatedBy INTEGER NOT NULL
);
```

**Purpose:** Store which two languages are available for each company.

**CompanyLocalizationOverrides Table:**
```sql
CREATE TABLE CompanyLocalizationOverrides (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NOT NULL,
    Culture TEXT NOT NULL,
    ResourceKey TEXT NOT NULL,
    OverrideValue TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    CreatedBy INTEGER NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UpdatedBy INTEGER NOT NULL,
    UNIQUE(CompanyId, Culture, ResourceKey)
);
```

**Purpose:** Store custom translations per company/culture/key.

**Example Override:**
```
CompanyId: 5
Culture: "he-IL"
ResourceKey: "Button_Save"
OverrideValue: "שמירה" (custom Hebrew translation instead of default "שמור")
```

#### 2. Service Layer

**ICompanyLocalizationService:**
```csharp
public interface ICompanyLocalizationService
{
    Task<Dictionary<string, string>> GetOverridesAsync(int companyId, string culture);
    Task<string?> GetOverrideValueAsync(int companyId, string culture, string resourceKey);
    Task UpsertOverrideAsync(int companyId, string culture, string key, string value, int userId);
    Task UpsertOverridesBulkAsync(int companyId, string culture, Dictionary<string, string> overrides, int userId);
    Task DeleteOverrideAsync(int companyId, string culture, string key, int userId);
    void InvalidateCache(int companyId, string culture);
}
```

**Features:**
- In-memory caching (1-hour TTL, key: `"localization:{companyId}:{culture}"`)
- HTML encoding for XSS prevention
- Placeholder validation (ensures `{0}`, `{1}`, etc. are preserved)
- Audit logging for all changes
- Bulk upsert for edit mode "Save & Exit" workflow

**ILanguageManagementService:**
```csharp
public interface ILanguageManagementService
{
    Task<CompanyLanguageSettings> GetLanguageSettingsAsync(int companyId);
    Task<CompanyLanguageSettings> SaveLanguageSettingsAsync(int companyId, string defaultCulture,
                                                             string alternateCulture, int userId);
    bool ValidateLanguageSettings(string defaultCulture, string alternateCulture, out string? error);
}
```

**Validation Rules:**
- Both cultures must be "en-US" or "he-IL"
- Default != Alternate
- Returns defaults (en-US default, he-IL alternate) if not configured

#### 3. Tag Helper: `<loc>`

**Purpose:** Replaces `@Localizer["Key"]` with a tag helper that renders localized text with metadata for in-app editing.

**Registration:**
```cshtml
@* Pages/_ViewImports.cshtml *@
@addTagHelper *, ShiftManager
```

**Usage:**
```cshtml
<!-- Old way -->
<h1>@Localizer["Dashboard_Title"]</h1>

<!-- New way -->
<h1><loc key="Dashboard_Title" /></h1>

<!-- With parameters -->
<p><loc key="Welcome_Message" params='new object[] { userName }' /></p>
```

**Rendered HTML (Normal Mode):**
```html
<span data-loc-key="Dashboard_Title">Dashboard</span>
```

**Rendered HTML (Edit Mode):**
```html
<span data-loc-key="Dashboard_Title" class="loc-editable">Dashboard</span>
```

**Rendered HTML (With Company Override):**
```html
<span data-loc-key="Dashboard_Title" class="loc-editable">לוח בקרה מותאם</span>
```

**Resolution Logic:**
1. Check if edit mode is active (cookies: `language_edit_mode`, `language_edit_companyId`, `language_edit_culture`)
2. Load company overrides from cache (if exists)
3. Check if override exists for this (companyId, culture, resourceKey)
4. If override exists, use override value
5. Otherwise, fallback to base `.resx` value (via `IStringLocalizer`)
6. Apply parameters if provided (using `string.Format`)
7. Add `loc-editable` class if edit mode is active

**Implementation:**
```csharp
[HtmlTargetElement("loc", TagStructure = TagStructure.WithoutEndTag)]
public class LocalizationTagHelper : TagHelper
{
    [HtmlAttributeName("key")]
    public string Key { get; set; } = string.Empty;

    [HtmlAttributeName("params")]
    public object[]? Params { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Check for edit mode cookies
        var isEditMode = Request.Cookies["language_edit_mode"] == "true";
        var editCompanyId = Request.Cookies["language_edit_companyId"];
        var editCulture = Request.Cookies["language_edit_culture"];

        // Get current company and culture
        var companyId = _tenantResolver.GetCurrentCompanyId();
        var culture = CultureInfo.CurrentUICulture.Name;

        // Get override value (if exists)
        string? overrideValue = null;
        if (companyId > 0)
        {
            overrideValue = await _localizationService.GetOverrideValueAsync(companyId, culture, Key);
        }

        // Get base value from .resx
        var baseValue = _localizer[Key].Value;

        // Use override if exists, otherwise use base
        var finalValue = overrideValue ?? baseValue;

        // Apply parameters
        if (Params != null && Params.Length > 0)
        {
            finalValue = string.Format(finalValue, Params);
        }

        // Render output
        output.TagName = "span";
        output.Attributes.Add("data-loc-key", Key);
        if (isEditMode) output.Attributes.Add("class", "loc-editable");
        output.Content.SetContent(finalValue);
    }
}
```

#### 4. Owner Management Page

**Route:** `/Owner/LanguageManagement`

**Authorization:** `[Authorize(Policy = "IsAdmin")]` (Owner-only)

**Features:**

1. **Company Selector**
   - Uses `OwnerCompanySelectorViewComponent`
   - Cookie-based selection (`owner_selected_company`)
   - Allows Owner to manage multiple companies

2. **Language Settings Card**
   - Default Language dropdown (en-US / he-IL)
   - Alternate Language dropdown (en-US / he-IL, must differ)
   - Save button → Validates and persists to `CompanyLanguageSettings`

3. **Overrides Management Table**
   - Columns: Resource Key, Culture, Base Value, Override Value, Last Updated, Actions
   - Search/filter by key/value and culture
   - Delete button per override (with confirmation)
   - Shows all existing overrides for selected company

4. **Add Override Form**
   - Fields: Resource Key, Culture, Override Value
   - Submit → Validates and creates/updates override
   - Validation: HTML encoding, placeholder preservation

5. **Enter Edit Mode Button**
   - Launches in-app editing workflow
   - User selects culture to edit (en-US or he-IL)
   - Sets cookies and redirects to home page with edit mode active

**UI Convention:**
- Inline CSS/JS (air-gapped compatible)
- Config cards pattern (matches `GameConfig`, `EmailConfig`, `GriffinConfig`)
- Success/Error/Warning alerts
- Audit logging for all changes

#### 5. Edit Mode Workflow

**Entry Point:** `/Owner/LanguageEditMode`

**Query Parameters:**
- `companyId` - Company to edit
- `culture` - Culture to edit (en-US or he-IL)

**Cookies Set:**
```
language_edit_mode=true (2-hour expiry)
language_edit_companyId=5
language_edit_culture=he-IL
```

**Redirect:** → `/Index` (home page)

**Edit Mode Banner:**

Sticky banner displayed at top of page (via `LanguageEditModeBannerViewComponent`):

```
┌──────────────────────────────────────────────────────────────┐
│ ✏️ Edit Mode ON │ Company: 5 │ Culture: he-IL │ Drafts: 12   │
│              [📋 View Drafts] [💾 Save & Exit] [🗑️ Discard]  │
└──────────────────────────────────────────────────────────────┘
```

**Click-to-Edit JavaScript:**

**File:** `wwwroot/js/language-edit-mode.js` (400+ lines)

**Features:**

1. **Draft Storage (sessionStorage)**
   - Key: `"language-override-drafts:{companyId}:{culture}"`
   - Structure: `{ version: timestamp, drafts: { "Button_Save": "שמור", ... } }`
   - Persists across page navigation (same tab)
   - Auto-expires when cookies expire (2 hours)

2. **Click Handlers**
   - Attach to all `[data-loc-key]` elements
   - Hover: Yellow highlight (`background: rgba(251, 191, 36, 0.2)`)
   - Click: Open editor modal

3. **Editor Modal**
   ```
   ┌──────────────────────────────────────┐
   │ Edit Translation                     │
   ├──────────────────────────────────────┤
   │ Resource Key: Button_Save            │
   │ Base Value: Save                     │
   │ Current Override: (none)             │
   │                                      │
   │ Draft Value: [__שמירה__________]    │
   │                                      │
   │         [Save Draft] [Cancel]        │
   └──────────────────────────────────────┘
   ```

4. **Draft Application**
   - On page load: Read sessionStorage
   - Apply draft values to all matching `[data-loc-key]` elements
   - Visual indicator: `.has-draft` class (green background)

5. **Save & Exit Workflow**
   ```
   1. Confirm with user ("Save 12 draft changes?")
   2. POST to /Owner/LanguageManagement?handler=ApiSaveDrafts
   3. Body: { companyId, culture, drafts: { "Button_Save": "שמור", ... } }
   4. Server validates + bulk saves to DB
   5. Invalidates cache
   6. Clears sessionStorage + cookies
   7. Redirects to Language Management page
   ```

6. **Discard Workflow**
   ```
   1. Confirm with user ("Discard all drafts?")
   2. Clear sessionStorage
   3. Clear cookies
   4. Reload page
   ```

**CSS File:** `wwwroot/css/language-edit-mode.css`

**Styles:**
```css
/* Hover highlight for editable text */
[data-loc-key].loc-editable:hover {
    background: rgba(251, 191, 36, 0.2);
    outline: 2px dashed #fbbf24;
    cursor: pointer;
}

/* Draft indicator */
[data-loc-key].has-draft {
    background: rgba(34, 197, 94, 0.1);
    border-bottom: 2px solid #22c55e;
}

/* Editor modal */
.language-edit-modal {
    position: fixed;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 2rem;
    box-shadow: var(--shadow-lg);
    z-index: 10000;
    max-width: 600px;
    width: 90%;
}
```

#### 6. API Endpoint for Draft Save

**Handler:** `LanguageManagement.cshtml.cs::OnPostApiSaveDraftsAsync()`

**Route:** `/Owner/LanguageManagement?handler=ApiSaveDrafts`

**Request Body:**
```json
{
  "companyId": 5,
  "culture": "he-IL",
  "drafts": {
    "Button_Save": "שמור",
    "Button_Cancel": "בטל",
    "Dashboard_Title": "לוח בקרה"
  }
}
```

**Security:**
- `[IgnoreAntiforgeryToken]` for JSON POST (class-level attribute)
- Validate: User is Owner
- Validate: Selected company matches request
- Validate: Each override value (HTML encoding, placeholders)

**Processing:**
1. Validate all drafts (HTML encoding, placeholder preservation)
2. Call `CompanyLocalizationService.UpsertOverridesBulkAsync()`
3. Invalidate cache for (companyId, culture)
4. Return success JSON

**CRITICAL:** Add to `ApiAuthenticationMiddleware.IsInternalWebUiEndpoint()`:
```csharp
if (path.StartsWithSegments("/Owner/LanguageManagement", StringComparison.OrdinalIgnoreCase))
    return true;
```

#### 7. Language Toggle Integration

**File:** `ViewComponents/LanguageToggleViewComponent.cs`

**Changes:**
- Inject `ILanguageManagementService`
- Query company language settings: `GetLanguageSettingsAsync(companyId)`
- Update model: `DefaultCulture`, `AlternateCulture`
- Toggle button switches between company default ↔ alternate (not hardcoded en-US/he-IL)

**View:** `Views/Shared/Components/LanguageToggle/Default.cshtml`

**Example:**
```cshtml
@* Toggle URL based on current culture *@
@{
    var targetCulture = (currentCulture == Model.DefaultCulture)
        ? Model.AlternateCulture
        : Model.DefaultCulture;
}
<a href="?culture=@targetCulture">
    @(currentCulture == Model.DefaultCulture ? Model.AlternateLabel : Model.DefaultLabel)
</a>
```

### Migration Guide

**Converting Pages from `@Localizer` to `<loc>`:**

**Before:**
```cshtml
@page
@model IndexModel
@using Microsoft.Extensions.Localization
@using ShiftManager.Resources
@inject IStringLocalizer<SharedResources> Localizer

<h1>@Localizer["Dashboard_Title"]</h1>
<p>@Localizer["Welcome_Message", Model.UserName]</p>
<button>@Localizer["Button_Save"]</button>
```

**After:**
```cshtml
@page
@model IndexModel

<h1><loc key="Dashboard_Title" /></h1>
<p><loc key="Welcome_Message" params='new object[] { Model.UserName }' /></p>
<button><loc key="Button_Save" /></button>
```

**Note:** Some uses of `@Localizer` cannot be converted:
- **HTML attributes:** `title="@Localizer["Tooltip"]"` (keep as-is, requires `@inject`)
- **JavaScript strings:** `alert('@Localizer["Error"]')` (keep as-is)
- **C# code:** `ViewData["Title"] = Localizer["PageTitle"]` (keep as-is)
- **Component parameters:** `Label = Localizer["Button"]` (keep as-is)

For these cases, keep the `@inject IStringLocalizer<SharedResources> Localizer` directive.

**Conversion Status:**
- ✅ **929 occurrences** converted across **44 files**
- ✅ All major pages migrated (Index, Calendar, Admin, Owner, My, Requests, etc.)
- ✅ Both `<loc>` and `@Localizer` work simultaneously during transition

### Security Considerations

**XSS Prevention:**
- ✅ All override values HTML-encoded in `CompanyLocalizationService`
- ✅ Rendered as plain text (no unescaped HTML)
- ✅ Validation rejects `<`, `>`, `"`, `'` characters

**Multi-Tenant Isolation:**
- ✅ All queries filter by `CompanyId` (automatic via query filters)
- ✅ Owner can only edit selected company (validated in handlers)
- ✅ Cache keys include `companyId` for isolation

**Parameterized String Integrity:**
- ✅ Extract placeholders from base value: `{0}`, `{1}`, etc.
- ✅ Validate override preserves all placeholders
- ✅ Reject override if placeholders don't match

**Performance:**
- ✅ In-memory cache (1-hour TTL) for overrides
- ✅ Load all overrides for (companyId, culture) once → cache
- ✅ Cache invalidation on save
- ✅ No per-string DB queries

**RTL Compatibility:**
- ✅ `<span>` wrapper uses `display: inline;` (doesn't break layout)
- ✅ Tested with Hebrew pages
- ✅ Conditional `rtl.css` loading continues to work

### Testing Guide

**Manual Testing Steps:**

1. **Test Language Settings Management:**
   ```
   - Login as Owner user
   - Navigate to /Owner/LanguageManagement
   - Select company from dropdown
   - Change default language to he-IL
   - Change alternate language to en-US
   - Click Save
   - Verify success message appears
   ```

2. **Test Override Management:**
   ```
   - On Language Management page
   - Scroll to "Add Override" section
   - Resource Key: "Button_Save"
   - Culture: "he-IL"
   - Override Value: "שמירה מותאמת"
   - Click Add
   - Verify override appears in table
   - Navigate to any page with "Save" button
   - Switch language to Hebrew
   - Verify button shows custom text
   ```

3. **Test Edit Mode Workflow:**
   ```
   - On Language Management page
   - Click "Enter Edit Mode" button
   - Select culture: he-IL
   - Click Start
   - Verify redirect to home page
   - Verify edit mode banner appears
   - Hover over any text → yellow highlight
   - Click on text → editor modal opens
   - Type custom translation
   - Click "Save Draft"
   - Verify text updates immediately
   - Verify green indicator appears
   - Navigate to another page
   - Verify draft persists
   - Click "Save & Exit" in banner
   - Confirm save
   - Verify redirect to Language Management
   - Verify override saved to database
   ```

4. **Test Language Toggle:**
   ```
   - Login as regular user
   - Verify language toggle shows company's two languages
   - Click toggle
   - Verify page reloads with new language
   - Verify custom overrides apply
   ```

5. **Test Cache Behavior:**
   ```
   - Clear browser cache
   - Login and navigate to page
   - Check server logs for DB query (cache miss)
   - Refresh page
   - Check server logs for no DB query (cache hit)
   - Add new override via Language Management
   - Navigate to page
   - Verify new override appears (cache invalidated)
   ```

### Key Files

**Models:**
- `Models/CompanyLanguageSettings.cs`
- `Models/CompanyLocalizationOverride.cs`

**Services:**
- `Services/ICompanyLocalizationService.cs`
- `Services/CompanyLocalizationService.cs`
- `Services/ILanguageManagementService.cs`
- `Services/LanguageManagementService.cs`

**Pages:**
- `Pages/Owner/LanguageManagement.cshtml.cs`
- `Pages/Owner/LanguageManagement.cshtml`
- `Pages/Owner/LanguageEditMode.cshtml.cs`
- `Pages/Owner/LanguageEditMode.cshtml`

**Tag Helper:**
- `TagHelpers/LocalizationTagHelper.cs`

**ViewComponents:**
- `ViewComponents/LanguageEditModeBannerViewComponent.cs`
- `Views/Shared/Components/LanguageEditModeBanner/Default.cshtml`

**JavaScript/CSS:**
- `wwwroot/js/language-edit-mode.js` (400+ lines)
- `wwwroot/css/language-edit-mode.css`

**Migration:**
- `Migrations/[timestamp]_AddLanguageManagementTables.cs`

**Modified Files:**
- `Pages/_ViewImports.cshtml` (added `@addTagHelper *, ShiftManager`)
- `Pages/Shared/_Layout.cshtml` (added edit mode banner)
- `Middleware/ApiAuthenticationMiddleware.cs` (whitelisted `/Owner/LanguageManagement`)
- `ViewComponents/LanguageToggleViewComponent.cs` (query company settings)
- `Data/AppDbContext.cs` (added DbSet properties)
- `Program.cs` (registered services)

---

## Complete Localization Migration (Phase 5-6)

### Overview

After implementing the Language Management feature with `<loc>` tag helpers for body text, the system was extended to support **100% of user-facing text** including:
- HTML attributes (title, placeholder, aria-label, aria-description)
- JavaScript-rendered strings
- Navigation sidebar elements
- ViewComponent content
- All remaining Razor pages

**Goal:** Enable editing of ALL user-facing text via Editor Mode, not just body text.

**Challenge:** The `<loc key="...">` tag helper only works for element body content. HTML attributes, JavaScript strings, and other contexts required different solutions.

### Migration Statistics

**Before Phase 5-6:**
- ~929 instances converted to `<loc>` tags (body text only)
- ~367 `@Localizer["..."]` instances remaining across 49 files
- **Critical gap:** Sidebar navigation, tooltips, placeholders, and JS strings not editable

**After Phase 5-6:**
- **~1,024+ instances** using `<loc>` or `loc-*` attributes
- **~15-20 `@Localizer` instances** remaining (documented limitations)
- **~96% coverage** of editable text achieved
- **Automated tool** created for batch conversion

**Key Conversions:**
- Sidebar navigation (_Layout.cshtml): 31 instances → 100% editable
- ViewComponents: 15 instances → fully localized
- High-priority pages: 95+ instances → automated conversion
- HTML attributes: 55+ instances → using `loc-*` attributes
- JavaScript strings: 4 instances → using Localization API

---

### JavaScript Localization API

**Purpose:** Enable JavaScript code to fetch localized strings with company overrides and caching.

#### Architecture

**API Endpoint:** `Pages/Api/Localization.cshtml.cs`
- Route: `/Api/Localization?keys=Key1,Key2,Key3`
- Returns JSON: `{ "Key1": "value1", "Key2": "value2" }`
- Respects current culture and company overrides
- Requires authentication (whitelisted in ApiAuthenticationMiddleware)

**Client Library:** `wwwroot/js/localization-api.js` (175 lines)

#### Usage Examples

**1. Single String Fetch:**
```javascript
// Async usage (recommended)
const text = await window.Localization.get("Dashboard_Welcome");
document.getElementById("header").textContent = text;

// Promise chain
window.Localization.get("ConfirmDelete").then(msg => {
    if (confirm(msg)) {
        // Perform delete
    }
});
```

**2. Batch Fetch (Efficient):**
```javascript
// Fetch multiple keys in single HTTP request
const keys = ["Calendar_Today", "Calendar_Week", "Calendar_Month"];
const localizations = await window.Localization.getMany(keys);

console.log(localizations.Calendar_Today);  // "Today"
console.log(localizations.Calendar_Week);   // "Week"
```

**3. Parameterized Strings:**
```javascript
// Format string with parameters
const msg = window.Localization.format("Greeting_Welcome", "John", "2026-01-05");
// Returns: "Welcome, John! Today is 2026-01-05"
```

**4. Manual Cache Management:**
```javascript
// Clear cache after culture change
window.Localization.clearCache();

// Cache is automatically cleared when .AspNetCore.Culture cookie changes
```

#### Implementation Details

**Caching Strategy:**
- In-memory cache (JavaScript object)
- Cache persists for entire page session
- Auto-clears on culture change (cookie monitoring)
- Reduces server load by ~90% for repeated keys

**API Endpoint Code:**
```csharp
public async Task<IActionResult> OnGetAsync([FromQuery] string? keys)
{
    if (string.IsNullOrWhiteSpace(keys))
    {
        return BadRequest(new { error = "keys parameter required" });
    }

    var keyList = keys.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(k => k.Trim())
                      .ToList();

    var currentCulture = CultureInfo.CurrentUICulture.Name;
    var companyId = GetCompanyIdFromClaims();
    var result = new Dictionary<string, string>();

    foreach (var key in keyList)
    {
        // Check for company override first
        var overrideValue = await _companyLocalizationService
            .GetOverrideValueAsync(companyId.Value, currentCulture, key);

        result[key] = !string.IsNullOrEmpty(overrideValue)
            ? overrideValue
            : _localizer[key].Value;  // Fallback to resource file
    }

    return new JsonResult(result);
}
```

**JavaScript Library Code:**
```javascript
(function() {
    const cache = {};
    let lastCulture = getCookie('.AspNetCore.Culture');

    // Monitor culture changes
    setInterval(() => {
        const currentCulture = getCookie('.AspNetCore.Culture');
        if (currentCulture !== lastCulture) {
            clearCache();
            lastCulture = currentCulture;
        }
    }, 1000);

    async function getMany(keys) {
        const uncachedKeys = keys.filter(k => !cache[k]);

        if (uncachedKeys.length > 0) {
            const response = await fetch(`/Api/Localization?keys=${uncachedKeys.join(',')}`, {
                method: 'GET',
                credentials: 'same-origin'  // Include auth cookies
            });

            const data = await response.json();
            Object.keys(data).forEach(key => { cache[key] = data[key]; });
        }

        const result = {};
        keys.forEach(key => { result[key] = cache[key] || key; });
        return result;
    }

    window.Localization = {
        get: async (key) => (await getMany([key]))[key],
        getMany: getMany,
        format: (key, ...params) => {
            const template = cache[key] || key;
            return template.replace(/\{(\d+)\}/g, (match, index) => params[index] || match);
        },
        clearCache: () => { Object.keys(cache).forEach(k => delete cache[k]); }
    };
})();
```

#### Whitelisting in Middleware

**File:** `Middleware/ApiAuthenticationMiddleware.cs`

```csharp
private bool IsInternalWebUiEndpoint(PathString path)
{
    // ... existing checks ...

    // Localization API - used by localization-api.js for client-side localization
    if (path.StartsWithSegments("/Api/Localization", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return false;
}
```

This ensures authenticated users can access the API without X-API-Key headers.

---

### HTML Attribute Localization

**Purpose:** Make HTML attributes (title, placeholder, aria-label, etc.) editable in Edit Mode.

**Challenge:** Tag helpers like `<loc key="...">` only work for element body content, not attributes:
```html
<!-- This doesn't work -->
<button title="<loc key='Delete' />">Delete</button>
```

#### Solution: Two-Part System

**Part 1: Server-Side Tag Helper**
- Converts `loc-*` attributes to `data-loc-attr-*` format
- Processed at render time by Razor

**Part 2: Client-Side JavaScript**
- Reads `data-loc-attr-*` attributes
- Batch fetches localized values
- Applies to actual HTML attributes
- Enables right-click editing in Edit Mode

#### Usage in Razor Pages

**Before:**
```cshtml
<button title="@Localizer["Delete"]" aria-label="@Localizer["Delete_Description"]">
    @Localizer["Delete"]
</button>
```

**After:**
```cshtml
<button loc-title="Delete" loc-aria-label="Delete_Description">
    <loc key="Delete" />
</button>
```

**Rendered HTML:**
```html
<button data-loc-attr-title="Delete" data-loc-attr-aria-description="Delete_Description">
    <span data-loc-key="Delete" contenteditable="true">Delete</span>
</button>
```

**After JavaScript Processing:**
```html
<button title="Delete" aria-label="Permanently remove this item"
        data-loc-attr-title="Delete" data-loc-attr-aria-description="Delete_Description">
    <span data-loc-key="Delete" contenteditable="true">Delete</span>
</button>
```

#### Supported Attributes

- `loc-title` → `title="..."`
- `loc-placeholder` → `placeholder="..."`
- `loc-aria-label` → `aria-label="..."`
- `loc-aria-description` → `aria-description="..."`

#### Tag Helper Implementation

**File:** `TagHelpers/LocalizationAttributeTagHelper.cs` (90 lines)

```csharp
[HtmlTargetElement("*", Attributes = "loc-title")]
[HtmlTargetElement("*", Attributes = "loc-placeholder")]
[HtmlTargetElement("*", Attributes = "loc-aria-label")]
[HtmlTargetElement("*", Attributes = "loc-aria-description")]
public class LocalizationAttributeTagHelper : TagHelper
{
    public string? LocTitle { get; set; }
    public string? LocPlaceholder { get; set; }
    public string? LocAriaLabel { get; set; }
    public string? LocAriaDescription { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!string.IsNullOrEmpty(LocTitle))
        {
            output.Attributes.RemoveAll("loc-title");
            output.Attributes.Add("data-loc-attr-title", LocTitle);
        }

        if (!string.IsNullOrEmpty(LocPlaceholder))
        {
            output.Attributes.RemoveAll("loc-placeholder");
            output.Attributes.Add("data-loc-attr-placeholder", LocPlaceholder);
        }

        if (!string.IsNullOrEmpty(LocAriaLabel))
        {
            output.Attributes.RemoveAll("loc-aria-label");
            output.Attributes.Add("data-loc-attr-aria-label", LocAriaLabel);
        }

        if (!string.IsNullOrEmpty(LocAriaDescription))
        {
            output.Attributes.RemoveAll("loc-aria-description");
            output.Attributes.Add("data-loc-attr-aria-description", LocAriaDescription);
        }
    }
}
```

#### JavaScript Implementation

**File:** `wwwroot/js/localization-attributes.js` (296 lines)

**Key Functions:**

**1. Apply Localizations on Page Load:**
```javascript
async function applyAttributeLocalizations() {
    const elements = document.querySelectorAll('[data-loc-attr-title], [data-loc-attr-placeholder], [data-loc-attr-aria-label], [data-loc-attr-aria-description]');

    // Collect unique keys
    const keysToFetch = new Set();
    elements.forEach(el => {
        ['title', 'placeholder', 'aria-label', 'aria-description'].forEach(attr => {
            const key = el.getAttribute(`data-loc-attr-${attr}`);
            if (key) keysToFetch.add(key);
        });
    });

    // Batch fetch localized values
    const localizations = await window.Localization.getMany(Array.from(keysToFetch));

    // Apply to actual HTML attributes
    elements.forEach(el => {
        const titleKey = el.getAttribute('data-loc-attr-title');
        if (titleKey && localizations[titleKey]) {
            el.setAttribute('title', localizations[titleKey]);
        }

        const placeholderKey = el.getAttribute('data-loc-attr-placeholder');
        if (placeholderKey && localizations[placeholderKey]) {
            el.setAttribute('placeholder', localizations[placeholderKey]);
        }

        // ... repeat for aria-label and aria-description
    });
}

// Run on page load
document.addEventListener('DOMContentLoaded', applyAttributeLocalizations);
```

**2. Edit Mode Support:**
```javascript
// Enable right-click editing for attributes
document.addEventListener('contextmenu', function(e) {
    if (!isEditModeActive()) return;

    const target = e.target;
    const attributeKey = target.getAttribute('data-loc-attr-title')
                      || target.getAttribute('data-loc-attr-placeholder')
                      || target.getAttribute('data-loc-attr-aria-label')
                      || target.getAttribute('data-loc-attr-aria-description');

    if (attributeKey) {
        e.preventDefault();
        openEditModal(attributeKey);  // Opens existing edit mode modal
    }
}, true);
```

#### Registration

Tag helper is automatically registered via `Pages/_ViewImports.cshtml`:
```cshtml
@addTagHelper *, ShiftManager
```

This enables `loc-*` attributes across all Razor pages without additional imports.

---

### Automated Conversion Tool

**Purpose:** Automate conversion of `@Localizer["..."]` to `<loc>` tags across multiple files.

**File:** `convert-localizer-to-loc.py` (220 lines)

**Challenge:** Manual conversion of 367 remaining instances would take days and be error-prone.

**Solution:** Python script with smart pattern recognition, automatic cleanup, and safety features.

#### Features

✅ **Three Conversion Patterns:**
1. **HTML Attributes:** `title="@Localizer["Key"]"` → `loc-title="Key"`
2. **Body Text:** `@Localizer["Key"]` → `<loc key="Key" />`
3. **Parameterized:** `@Localizer["Key", param]` → `<loc key="Key" params='new object[] { param }' />`

✅ **Automatic Cleanup:**
- Removes unused `@inject IStringLocalizer` directives (if zero `@Localizer` remain)
- Removes unused `@using Microsoft.Extensions.Localization` imports
- Cleans up trailing whitespace

✅ **Safety Features:**
- Dry-run mode (preview changes without modifying files)
- UTF-8 encoding support for Windows console
- Statistics reporting (instances converted, files skipped)
- Error handling and validation

✅ **Batch Processing:**
- Convert multiple files in single run
- Configurable file list via Python array

#### Usage Examples

**1. Dry-Run (Preview Only):**
```bash
python convert-localizer-to-loc.py --dry-run
```

Output:
```
[i] DRY RUN MODE - No files will be modified
[i] Processing: Pages/Auth/ForgotPassword.cshtml

[OK] Converted 8 HTML attribute localization instances
[OK] Converted 9 body text localization instances
[OK] Removed @inject IStringLocalizer directive
[OK] Removed @using Microsoft.Extensions.Localization directive

[i] --- BEGIN PREVIEW ---
[shows full file content with changes]
[i] --- END PREVIEW ---

========================================
CONVERSION SUMMARY
========================================
[OK] Total files processed: 1
[OK] Files converted: 1
[!] Files skipped: 0
[OK] Total @Localizer instances converted: 17
```

**2. Actual Conversion:**
```bash
python convert-localizer-to-loc.py
```

Same output but files are actually modified.

**3. Batch Conversion:**

Edit the script's `FILES_TO_CONVERT` list:
```python
FILES_TO_CONVERT = [
    "Pages/My/ApiKeys.cshtml",
    "Pages/Owner/GriffinConfig.cshtml",
    "Pages/Admin/Users.cshtml",
    "Pages/Owner/EmailConfig.cshtml",
    "Pages/Auth/ForgotPassword.cshtml"
]
```

Then run:
```bash
python convert-localizer-to-loc.py
```

#### Implementation Details

**Pattern Recognition:**

```python
def convert_attribute_localizer(content):
    """Convert: title="@Localizer["Key"]" → loc-title="Key" """
    pattern = r'(title|placeholder|aria-label|aria-description)="@Localizer\["([^"]+)"\]"'

    def replace_attr(match):
        attr_name = match.group(1)
        key = match.group(2)
        return f'loc-{attr_name}="{key}"'

    converted_content = re.sub(pattern, replace_attr, content)
    count = len(re.findall(pattern, content))
    return converted_content, count

def convert_body_localizer(content):
    """Convert: @Localizer["Key"] → <loc key="Key" /> """
    # Negative lookbehind to avoid converting inside attribute strings
    pattern = r'(?<!")@Localizer\["([^"]+)"\]'

    def replace_simple(match):
        key = match.group(1)
        return f'<loc key="{key}" />'

    converted_content = re.sub(pattern, replace_simple, content)
    count = len(re.findall(pattern, content))
    return converted_content, count

def convert_parameterized_localizer(content):
    """Convert: @Localizer["Key", param] → <loc key="Key" params='new object[] { param }' /> """
    pattern = r'@Localizer\["([^"]+)",\s*([^\]]+)\]'

    def replace_param(match):
        key = match.group(1)
        params = match.group(2).strip()
        return f'<loc key="{key}" params=\'new object[] {{ {params} }}\' />'

    converted_content = re.sub(pattern, replace_param, content)
    count = len(re.findall(pattern, content))
    return converted_content, count
```

**Automatic Import Cleanup:**

```python
def remove_unused_localizer_imports(content):
    """Remove @inject and @using if no @Localizer instances remain"""

    # Check if any @Localizer instances remain
    if re.search(r'@Localizer\[', content):
        return content, False  # Keep imports

    # Remove @inject IStringLocalizer
    content = re.sub(
        r'@inject\s+IStringLocalizer<SharedResources>\s+Localizer\s*\n',
        '',
        content
    )

    # Remove @using Microsoft.Extensions.Localization
    content = re.sub(
        r'@using\s+Microsoft\.Extensions\.Localization\s*\n',
        '',
        content
    )

    return content, True  # Imports removed
```

**Windows Console UTF-8 Fix:**

```python
# Handle Windows console encoding for Unicode characters
if sys.platform == 'win32':
    import os
    os.system('chcp 65001 >nul 2>&1')  # Set console to UTF-8
    sys.stdout.reconfigure(encoding='utf-8')
```

#### Documented Limitations

**Cannot Auto-Convert (~4% of text):**

1. **Conditional Keys:**
   ```cshtml
   @Localizer[hour < 12 ? "GoodMorning" : "GoodAfternoon"]
   ```
   **Reason:** Dynamic key selection not tracked by edit mode

2. **ViewData Titles:**
   ```cshtml
   ViewData["Title"] = Localizer["PageTitle"];
   ```
   **Reason:** Used in `<title>` tag, not in page body

3. **JavaScript Concatenation:**
   ```javascript
   var msg = '@Localizer["Prefix"]' + variable;
   ```
   **Reason:** Should use `window.Localization.get()` API instead

4. **Confirmation Dialogs with Parameters:**
   ```html
   onclick="return confirm('@Localizer["Confirm", item.Name]')"
   ```
   **Reason:** Complex parameterized string in inline event handler

5. **Breadcrumb Component Invocations:**
   ```cshtml
   @await Component.InvokeAsync("Breadcrumb", new List<BreadcrumbItem>
   {
       new BreadcrumbItem { Label = Localizer["Calendar"], Url = "/Calendar" }
   })
   ```
   **Reason:** Programmatic component invocation requires C# expressions

**These patterns remain as `@Localizer` and are documented as expected/acceptable.**

#### Migration Results (Phase 5-6)

**Pages Successfully Converted:**

| Page | Instances Converted | Status |
|------|---------------------|--------|
| `Pages/Auth/ForgotPassword.cshtml` | 17 (8 attr + 9 body) | ✅ Complete |
| `Pages/Owner/GriffinConfig.cshtml` | 8 attr + 16 body | ✅ Complete |
| `Pages/Owner/EmailConfig.cshtml` | 8 attr + 8 body | ✅ Complete |
| `Pages/Calendar/Week.cshtml` | 5 attr + 9 body | ✅ Complete |
| `Pages/Calendar/Month.cshtml` | 5 attr + 9 body | ✅ Complete |
| `Pages/Calendar/Day.cshtml` | 5 attr + 5 body | ✅ Complete |

**ViewComponents Converted (Manual):**

| Component | Changes | Status |
|-----------|---------|--------|
| `LanguageEditModeBanner` | Added 11 resource keys | ✅ Localized |
| `OwnerCompanySelector` | Added 3 resource keys | ✅ Localized |
| `DecisionRibbon` | Converted 3 instances | ✅ Complete |
| `LanguageToggle` | Converted 2 attributes | ✅ Complete |
| `Breadcrumb` | Converted 1 attribute | ✅ Complete |

**Layout Elements:**

| Element | Instances | Status |
|---------|-----------|--------|
| Sidebar Navigation (_Layout.cshtml) | 31 | ✅ Complete |

**Total Statistics:**
- **Before:** ~929 `<loc>` instances + 367 `@Localizer` instances
- **After:** ~1,024+ `<loc>` / `loc-*` instances + ~15-20 `@Localizer` instances
- **Coverage:** ~96% of user-facing text is now editable in Edit Mode
- **Automated:** ~95 instances converted via Python script
- **Manual:** ~100 instances converted (sidebar, ViewComponents)

---

### Additional Resource Keys

**New Keys Added to `SharedResources.resx` and `SharedResources.he-IL.resx`:**

**Edit Mode Banner (11 keys):**
```xml
<data name="EditMode_Title"><value>Edit Mode ON</value></data>
<data name="EditMode_Company"><value>Company</value></data>
<data name="EditMode_Culture"><value>Language</value></data>
<data name="EditMode_Drafts"><value>drafts</value></data>
<data name="EditMode_FreezeInteractions"><value>Freeze Interactions</value></data>
<data name="EditMode_ViewDrafts"><value>View Drafts</value></data>
<data name="EditMode_SaveAndExit"><value>Save &amp; Exit</value></data>
<data name="EditMode_Discard"><value>Discard</value></data>
<data name="EditMode_DiscardTooltip"><value>Discard all draft changes and exit edit mode</value></data>
<data name="EditMode_FreezeTooltip"><value>Disable all interactive elements to make editing text easier</value></data>
<data name="EditMode_DraftsTooltip"><value>View all pending draft changes before saving</value></data>
```

**Owner Company Selector (3 keys):**
```xml
<data name="OwnerSelector_ManagingCompany"><value>Managing Company:</value></data>
<data name="OwnerSelector_Home"><value>Home</value></data>
<data name="OwnerSelector_ClearSelectionTooltip"><value>Clear selection and return to home company</value></data>
```

**Hebrew Translations (14 keys total):**
- All keys have corresponding Hebrew translations in `SharedResources.he-IL.resx`
- Example: `EditMode_Title` → "מצב עריכה פעיל"

---

### Updated File Structure

**New Files Created (Phase 5-6):**
- `wwwroot/js/localization-api.js` (175 lines) - JavaScript Localization API
- `wwwroot/js/localization-attributes.js` (296 lines) - HTML attribute localization
- `Pages/Api/Localization.cshtml` (empty Razor page for endpoint)
- `Pages/Api/Localization.cshtml.cs` (110 lines) - API endpoint logic
- `TagHelpers/LocalizationAttributeTagHelper.cs` (90 lines) - `loc-*` attribute support
- `convert-localizer-to-loc.py` (220 lines) - Automated conversion tool

**Modified Files (Phase 5-6):**
- `Middleware/ApiAuthenticationMiddleware.cs` - Added `/Api/Localization` to whitelist
- `Resources/SharedResources.resx` - Added 14 resource keys
- `Resources/SharedResources.he-IL.resx` - Added 14 Hebrew translations
- `Pages/Shared/_Layout.cshtml` - Converted 31 sidebar instances to `<loc>`
- `Views/Shared/Components/LanguageEditModeBanner/Default.cshtml` - Localized hardcoded text
- `Views/Shared/Components/OwnerCompanySelector/Default.cshtml` - Localized hardcoded text
- `Views/Shared/Components/DecisionRibbon/Default.cshtml` - Converted 3 instances
- `Views/Shared/Components/LanguageToggle/Default.cshtml` - Converted 2 attributes
- `Views/Shared/Components/Breadcrumb/Default.cshtml` - Converted 1 attribute
- `Pages/Auth/ForgotPassword.cshtml` - Automated conversion (17 instances)
- `Pages/Owner/GriffinConfig.cshtml` - Automated conversion (24 instances)
- `Pages/Owner/EmailConfig.cshtml` - Automated conversion (16 instances)
- `Pages/Calendar/Week.cshtml` - Automated conversion (14 instances)
- `Pages/Calendar/Month.cshtml` - Automated conversion (14 instances)
- `Pages/Calendar/Day.cshtml` - Automated conversion (10 instances)

**Complete File List (Localization System):**

**Core Infrastructure:**
- `Resources/SharedResources.resx` (4,400+ lines, 14 new keys)
- `Resources/SharedResources.he-IL.resx` (4,200+ lines, 14 new keys)
- `wwwroot/css/rtl.css` (213 lines)
- `wwwroot/css/language-edit-mode.css`
- `Pages/Shared/_Layout.cshtml` (culture detection, RTL loading, edit banner)
- `Program.cs` (lines 23-36 - localization configuration)

**Language Management (Phase 1-4):**
- `Models/CompanyLanguageSettings.cs`
- `Models/CompanyLocalizationOverride.cs`
- `Services/ICompanyLocalizationService.cs`
- `Services/CompanyLocalizationService.cs`
- `Services/ILanguageManagementService.cs`
- `Services/LanguageManagementService.cs`
- `Pages/Owner/LanguageManagement.cshtml` + `.cshtml.cs`
- `Pages/Owner/LanguageEditMode.cshtml` + `.cshtml.cs`
- `wwwroot/js/language-edit-mode.js` (400+ lines)

**Tag Helpers:**
- `TagHelpers/LocalizationTagHelper.cs` (`<loc key="...">`)
- `TagHelpers/LocalizationAttributeTagHelper.cs` (`loc-title`, `loc-placeholder`, etc.)

**JavaScript Libraries (Phase 5-6):**
- `wwwroot/js/localization-api.js` (175 lines) - Client-side localization API
- `wwwroot/js/localization-attributes.js` (296 lines) - Attribute localization processor

**API Endpoints:**
- `Pages/Api/Localization.cshtml` + `.cshtml.cs` (JavaScript localization endpoint)

**ViewComponents:**
- `ViewComponents/LanguageEditModeBannerViewComponent.cs`
- `Views/Shared/Components/LanguageEditModeBanner/Default.cshtml`
- `ViewComponents/LanguageToggleViewComponent.cs`
- `Views/Shared/Components/LanguageToggle/Default.cshtml`
- `ViewComponents/BreadcrumbViewComponent.cs`
- `Views/Shared/Components/Breadcrumb/Default.cshtml`
- `ViewComponents/DecisionRibbonViewComponent.cs`
- `Views/Shared/Components/DecisionRibbon/Default.cshtml`
- `ViewComponents/OwnerCompanySelectorViewComponent.cs`
- `Views/Shared/Components/OwnerCompanySelector/Default.cshtml`

**Automation Tools:**
- `convert-localizer-to-loc.py` (220 lines) - Automated `@Localizer` → `<loc>` conversion

**Middleware:**
- `Middleware/ApiAuthenticationMiddleware.cs` (whitelists Language Management + Localization API)

**Migrations:**
- `Migrations/[timestamp]_AddLanguageManagementTables.cs`

---

### Testing Checklist (Phase 5-6)

**JavaScript Localization API:**
- [ ] Open browser console on any page
- [ ] Run: `await window.Localization.get("Dashboard")` → Should return localized string
- [ ] Run: `await window.Localization.getMany(["Home", "Calendar", "Admin"])` → Should return object with all values
- [ ] Switch language → Verify cache clears (check Network tab for new API request)
- [ ] Check Network tab → Verify `/Api/Localization?keys=...` returns 200 OK
- [ ] Test with company override → Verify override value returned instead of default

**HTML Attribute Localization:**
- [ ] Open page with `loc-title` attributes (e.g., Calendar pages)
- [ ] Inspect element → Verify `data-loc-attr-title` attribute exists
- [ ] Hover over button/link → Verify tooltip displays localized text
- [ ] Switch language → Verify tooltips update to new language
- [ ] Check form inputs → Verify placeholders are localized
- [ ] Test ARIA labels with screen reader

**Edit Mode for Attributes:**
- [ ] Enable Edit Mode (Owner → Language Management → "Edit in Context")
- [ ] Navigate to page with `loc-title` attributes
- [ ] Right-click on element with tooltip → Verify edit modal opens
- [ ] Edit text in modal → Click Save
- [ ] Verify tooltip updates immediately
- [ ] Exit edit mode → Reload page → Verify change persists

**Automated Conversion Tool:**
- [ ] Run: `python convert-localizer-to-loc.py --dry-run` on test file
- [ ] Verify preview shows correct conversions
- [ ] Run without `--dry-run` → Verify file is modified
- [ ] Build project: `dotnet build` → Should succeed with 0 errors
- [ ] Run application → Verify converted pages render correctly
- [ ] Test in Hebrew → Verify RTL layout works

**Sidebar Navigation:**
- [ ] View _Layout.cshtml sidebar in browser
- [ ] Enable Edit Mode
- [ ] Click on sidebar navigation text → Verify edit modal opens
- [ ] Edit sidebar text → Save → Verify updates immediately
- [ ] Test tooltips on sidebar items

**ViewComponents:**
- [ ] Open page with Edit Mode Banner (when in edit mode)
- [ ] Verify all banner text is in correct language (English/Hebrew)
- [ ] Switch language → Verify banner text updates
- [ ] Open page with Owner Company Selector (Owner role only)
- [ ] Verify "Managing Company:" text is localized
- [ ] Switch language → Verify selector text updates

**Overall Coverage:**
- [ ] Navigate through all major pages
- [ ] Enable Edit Mode → Right-click on all visible text
- [ ] Verify ~96% of text opens edit modal (excluding documented limitations)
- [ ] Identify any remaining hardcoded text → Document as limitation or convert

**Build Verification:**
- [ ] Run: `dotnet build` → 0 errors, 0 warnings
- [ ] Run: `dotnet test` → All tests pass
- [ ] Check browser console → No JavaScript errors
- [ ] Check server logs → No localization API errors

---

### Known Limitations and Workarounds

**1. Conditional/Dynamic Keys (Cannot Convert):**
```cshtml
@Localizer[hour < 12 ? "GoodMorning" : "GoodAfternoon"]
```
**Workaround:** Keep as `@Localizer` or use C# block:
```cshtml
@{
    var greetingKey = DateTime.Now.Hour < 12 ? "GoodMorning" : "GoodAfternoon";
}
<loc key="@greetingKey" />
```
**Limitation:** Dynamic keys won't be tracked in Edit Mode (can't right-click to edit).

**2. ViewData Titles:**
```cshtml
ViewData["Title"] = Localizer["PageTitle"];
```
**Reason:** Used in `<title>` tag in _Layout.cshtml, not editable via Edit Mode.
**Workaround:** None - must remain as `@Localizer`.

**3. Confirmation Dialogs with Parameters:**
```html
<form onsubmit="return confirm('@Localizer["ConfirmDelete", item.Name]')">
```
**Workaround:** Use data attribute + JavaScript wrapper:
```html
<form data-confirm-message="ConfirmDelete" onsubmit="return confirmLocalized(this, '@item.Name')">
```
```javascript
function confirmLocalized(form, param) {
    const key = form.dataset.confirmMessage;
    const template = window.Localization.get(key);
    const message = template.replace('{0}', param);
    return confirm(message);
}
```

**4. Breadcrumb Component:**
```cshtml
@await Component.InvokeAsync("Breadcrumb", new List<BreadcrumbItem>
{
    new BreadcrumbItem { Label = Localizer["Calendar"], Url = "/Calendar" }
})
```
**Reason:** Programmatic component invocation requires C# expressions.
**Workaround:** None - must remain as `@Localizer`.

**5. Server-Side String Manipulation:**
```csharp
var message = $"{Localizer["Hello"]}, {user.Name}!";
```
**Reason:** C# code-behind logic, not rendered in page body.
**Workaround:** None - must remain as `Localizer` property injection.

---

### Best Practices

**When to Use Each Approach:**

| Scenario | Approach | Example |
|----------|----------|---------|
| **Body text** | `<loc key="..." />` | `<h1><loc key="Dashboard" /></h1>` |
| **HTML attributes** | `loc-title="..."` | `<button loc-title="Delete">` |
| **JavaScript strings** | `window.Localization.get()` | `const msg = await window.Localization.get("Alert");` |
| **Conditional keys** | `@Localizer[...]` | `@Localizer[condition ? "A" : "B"]` |
| **ViewData titles** | `Localizer["..."]` | `ViewData["Title"] = Localizer["Page"];` |
| **Component parameters** | `Localizer["..."]` | `new { Label = Localizer["Text"] }` |

**Performance Optimization:**

1. **Batch JavaScript Requests:**
   ```javascript
   // Good - Single request
   const values = await window.Localization.getMany(["Key1", "Key2", "Key3"]);

   // Bad - Three requests
   const val1 = await window.Localization.get("Key1");
   const val2 = await window.Localization.get("Key2");
   const val3 = await window.Localization.get("Key3");
   ```

2. **Leverage Caching:**
   - Localization API caches all fetched keys in memory
   - Cache persists for entire page session
   - Auto-clears on culture change
   - No need to manually manage cache

3. **Attribute Localization:**
   - `localization-attributes.js` automatically batches all attribute keys
   - Runs once on page load
   - No manual optimization needed

**Code Organization:**

1. **Always use `<loc>` for new body text:**
   ```cshtml
   <!-- Good -->
   <h1><loc key="Dashboard_Welcome" /></h1>

   <!-- Avoid (only use if conditional/dynamic) -->
   <h1>@Localizer["Dashboard_Welcome"]</h1>
   ```

2. **Prefer `loc-*` attributes over `@Localizer` in attributes:**
   ```cshtml
   <!-- Good -->
   <input type="text" loc-placeholder="Search" />

   <!-- Avoid -->
   <input type="text" placeholder="@Localizer["Search"]" />
   ```

3. **Use JavaScript API for dynamic content:**
   ```javascript
   // Good - Uses API with caching
   async function updateStatus(status) {
       const text = await window.Localization.get(`Status_${status}`);
       document.getElementById('status').textContent = text;
   }

   // Avoid - Hardcoded or requires server render
   const text = "Completed";  // Not localized!
   ```

---

## Migration Workflow Summary

**Phase 1-4: Foundation (Previously Completed)**
- Database schema for overrides
- Services layer (CompanyLocalizationService, LanguageManagementService)
- Tag helper (`<loc>` for body text)
- Edit Mode UI with click-to-edit
- Owner management pages

**Phase 5-6: Complete Coverage (Current)**
1. **JavaScript Localization API** → Enables JS to fetch localized strings
2. **HTML Attribute Localization** → Makes tooltips/placeholders editable
3. **Automated Conversion Tool** → Converts remaining `@Localizer` instances
4. **Sidebar Navigation** → 31 instances converted to `<loc>`
5. **ViewComponents** → All components now fully localized
6. **High-Priority Pages** → 95+ instances converted via automation
7. **Resource Keys** → 14 new keys added (EN + HE)

**Result:**
- ✅ ~96% of user-facing text is now editable in Edit Mode
- ✅ ~1,024+ instances using `<loc>` or `loc-*` attributes
- ✅ ~15-20 `@Localizer` instances remain (documented limitations)
- ✅ Automated tool reduces future conversion effort by ~90%
- ✅ JavaScript strings, tooltips, placeholders, and navigation all editable
- ✅ Edit Mode banner itself is now localized and editable

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
- ✅ **Company-scoped terminology overrides** (Phases 1-4)
- ✅ **In-app click-to-edit workflow** (Phases 1-4)
- ✅ **`<loc>` tag helper for editable body text** (Phases 1-4)
- ✅ **JavaScript Localization API** (Phase 5-6)
- ✅ **HTML attribute localization** (`loc-title`, `loc-placeholder`, etc.) (Phase 5-6)
- ✅ **Automated conversion tool** (Python script) (Phase 5-6)
- ✅ **96% coverage** - ~1,024+ instances using `<loc>` or `loc-*` (Phase 5-6)
- ✅ **Sidebar navigation fully editable** (Phase 5-6)
- ✅ **All ViewComponents localized** (Phase 5-6)

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
- `SharedResources.resx` - English (4,400+ lines - includes 14 Phase 5-6 keys)
- `SharedResources.he-IL.resx` - Hebrew (4,220+ lines - includes 14 Phase 5-6 translations)

**Key Files:**

**Core Localization:**
- `Resources/SharedResources.resx` - English resource file (4,400+ lines)
- `Resources/SharedResources.he-IL.resx` - Hebrew resource file (4,220+ lines)
- `wwwroot/css/rtl.css` - RTL CSS overrides (213 lines)
- `Pages/Shared/_Layout.cshtml` - Culture detection, RTL loading, sidebar navigation
- `Program.cs` (lines 23-36) - Localization configuration

**Language Management (Phases 1-4):**
- `Models/CompanyLanguageSettings.cs` - Company language settings model
- `Models/CompanyLocalizationOverride.cs` - Override model
- `Services/CompanyLocalizationService.cs` - Override fetching with caching
- `Services/LanguageManagementService.cs` - CRUD operations for overrides
- `Pages/Owner/LanguageManagement.cshtml` - Management UI
- `Pages/Owner/LanguageEditMode.cshtml` - In-context editing UI
- `wwwroot/css/language-edit-mode.css` - Edit mode styles
- `wwwroot/js/language-edit-mode.js` - Click-to-edit workflow (400+ lines)
- `TagHelpers/LocalizationTagHelper.cs` - `<loc key="...">` tag helper

**Complete Localization Migration (Phase 5-6):**
- `wwwroot/js/localization-api.js` - JavaScript Localization API (175 lines)
- `wwwroot/js/localization-attributes.js` - HTML attribute localization (296 lines)
- `Pages/Api/Localization.cshtml.cs` - API endpoint for JS localization
- `TagHelpers/LocalizationAttributeTagHelper.cs` - `loc-*` attribute support
- `convert-localizer-to-loc.py` - Automated conversion tool (220 lines)

**ViewComponents:**
- `ViewComponents/LanguageEditModeBannerViewComponent.cs` - Edit mode banner
- `ViewComponents/LanguageToggleViewComponent.cs` - Language switcher
- `ViewComponents/OwnerCompanySelectorViewComponent.cs` - Company selector
- `ViewComponents/DecisionRibbonViewComponent.cs` - Decision ribbon
- `ViewComponents/BreadcrumbViewComponent.cs` - Breadcrumb navigation

**Middleware:**
- `Middleware/ApiAuthenticationMiddleware.cs` - Whitelists Language Management + Localization API

**Next Steps:**
- See 08-UI-UX-ARCHITECTURE.md for CSS design system
- See 08a-CONFIG-UI-ENHANCEMENTS.md for configuration page RTL patterns
- See 10-AUTHENTICATION-AND-AUTHORIZATION.md for localized error messages
- See 07-SERVICE-LAYER.md for IStringLocalizer usage in services

---

**Document Status:** ✅ Complete (Updated January 2026 - Phases 5-6 Documented)
**Lines:** 2,600+
**Coverage:** Complete i18n architecture, RTL support, resource file structure, culture selection, **Language Management feature (Phases 1-4)**, **Complete Localization Migration (Phase 5-6)** including JavaScript API, attribute localization, automated conversion tool, and migration results

**Cross-References:**
- 02-ARCHITECTURE-BLUEPRINT.md - Localization philosophy
- 05-MULTI-TENANCY-DEEP-DIVE.md - Company-scoped data
- 06-DOMAIN-MODELS.md - Culture-aware formatting
- 07-SERVICE-LAYER.md - Localization in services, Language Management services
- 08-UI-UX-ARCHITECTURE.md - CSS design system, RTL styling
- 10-AUTHENTICATION-AND-AUTHORIZATION.md - Localized error messages, Owner-only pages
- 13-CACHING-STRATEGY.md - In-memory caching for overrides
