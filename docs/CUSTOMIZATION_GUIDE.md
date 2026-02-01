# ShiftManager - Customization Guide

This guide explains how to customize and modify the redesigned UI to match your preferences or requirements.

---

## Quick Customization

### Changing Colors

All colors are defined using CSS variables. To change colors, edit `wwwroot/css/site.css` lines 1-75.

**Light Theme Colors** (lines 6-28):
```css
:root {
  --bg: #f5f7fb;              /* Page background */
  --surface: #ffffff;          /* Card/container background */
  --surface-soft: #f0f2f8;     /* Subtle backgrounds */
  --text: #111318;             /* Primary text color */
  --muted: #6b7280;            /* Secondary text color */
  --primary: #2563eb;          /* Primary brand color */
  --primary-soft: #e0ecff;     /* Primary background tint */
  --danger: #dc2626;           /* Destructive actions */
  --success: #16a34a;          /* Success states */
  --warning: #f59e0b;          /* Warning states */
  --border: #e2e8f0;           /* Borders and dividers */
}
```

**Dark Theme Colors** (lines 30-52):
```css
:root[data-theme="dark"] {
  --bg: #020617;
  --surface: #0b1120;
  --surface-soft: #0f1629;
  --text: #e5e7eb;
  --muted: #9ca3af;
  --primary: #60a5fa;
  /* ... etc ... */
}
```

**Example**: To change the primary brand color from blue to purple:
```css
/* Light theme */
--primary: #7c3aed;          /* Purple-600 */
--primary-soft: #ede9fe;     /* Purple-100 */

/* Dark theme */
--primary: #a78bfa;          /* Purple-400 */
```

---

### Changing Typography

**Font Family** (line 3):
```css
--font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
```

**Typography Scale** (lines 77-110):
```css
.text-display { font-size: 2rem; }     /* Large headings */
.text-title { font-size: 1.5rem; }     /* Section titles */
.text-body { font-size: 1rem; }        /* Body text */
.text-subtle { font-size: 0.875rem; }  /* Secondary text */
.text-caption { font-size: 0.75rem; }  /* Small text */
```

**Example**: To use a custom font:
```css
/* Add at top of site.css */
@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap');

:root {
  --font-family: 'Inter', sans-serif;
}
```

---

### Changing Spacing

**Shadows** (lines 22-25 for light, 46-49 for dark):
```css
--shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.05);
--shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
--shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
--shadow-xl: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
```

**Border Radius**:
The design uses consistent border radius values:
- Small elements: `0.25rem` (4px)
- Medium elements: `0.5rem` (8px)
- Large elements: `0.75rem` (12px)
- Cards: `1rem` (16px)

To change globally, search and replace in `site.css`.

---

### Changing Component Styles

#### Buttons

Located in `site.css` around lines 1500-1650:

```css
.btn {
  /* Base button styles */
}

.btn-primary {
  background: var(--primary);
  color: white;
}

.btn-ghost {
  background: transparent;
  color: var(--text);
}

.btn-danger {
  background: var(--danger);
  color: white;
}
```

**Example**: Add a new button variant:
```css
.btn-success {
  background: var(--success);
  color: white;
  border: none;
}

.btn-success:hover {
  background: #15803d; /* Darker green */
}
```

#### Cards

Located in `site.css` around lines 194-274:

```css
.card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 1rem;
  box-shadow: var(--shadow-sm);
  overflow: hidden;
}
```

**Example**: Add more spacing to cards:
```css
.card {
  padding: 2rem; /* Instead of 1.25rem */
}
```

#### Forms

Located in `site.css` around lines 1800-2000:

```css
.form-input {
  padding: 0.75rem 1rem;
  border: 1px solid var(--border);
  border-radius: 0.5rem;
  background: var(--surface);
  color: var(--text);
}
```

**Example**: Make inputs larger:
```css
.form-input {
  padding: 1rem 1.25rem;
  font-size: 1.0625rem;
}
```

---

## Advanced Customization

### Adding a New Theme

To add a third theme (e.g., "sepia"):

1. Add theme data attribute selector in `site.css`:
```css
:root[data-theme="sepia"] {
  --bg: #f5f0e8;
  --surface: #faf7f2;
  --text: #5a4a3a;
  --primary: #8b6f47;
  /* ... define all variables ... */
}
```

2. Add theme toggle option in `Pages/Shared/_Layout.cshtml`:
```html
<button class="action-btn" id="themeToggle" onclick="cycleTheme()">
  <span>🎨</span>
</button>
```

3. Update JavaScript in `wwwroot/js/site.js`:
```javascript
function cycleTheme() {
  const current = root.getAttribute('data-theme') || 'light';
  const themes = ['light', 'dark', 'sepia'];
  const currentIndex = themes.indexOf(current);
  const nextIndex = (currentIndex + 1) % themes.length;
  const newTheme = themes[nextIndex];

  root.setAttribute('data-theme', newTheme);
  localStorage.setItem('theme', newTheme);
}
```

---

### Customizing the Command Palette

#### Adding Pages

Edit `wwwroot/js/site.js`, find the `commandPalettePages` array (around line 753):

```javascript
const commandPalettePages = [
  // Add your page here:
  {
    title: 'My Custom Page',
    subtitle: 'Description of what this page does',
    url: '/Controller/Action',
    icon: '📄',  // Any emoji
    roles: ['Manager', 'Director', 'Owner'] // or ['all']
  },
  // ... existing pages ...
];
```

#### Changing Icons

Replace the emoji in the `icon` field:
```javascript
icon: '🏠'  // Home
icon: '📅'  // Calendar
icon: '👥'  // People
icon: '⚙️'  // Settings
icon: '📊'  // Analytics
icon: '🏢'  // Companies
```

#### Changing Role Filtering

Modify the `roles` array:
- `['all']` - Everyone can see it
- `['Manager', 'Director', 'Owner']` - Admin roles only
- `['Employee', 'Trainee']` - Non-admin roles only
- `['Owner']` - Owners only

---

### Customizing the Sidebar

The sidebar is in `Pages/Shared/_Layout.cshtml` (lines 48-159).

#### Adding a New Menu Item

Add to the appropriate section (Manager or Employee):

```html
<!-- For Managers -->
<a href="/YourController/YourAction" class="app-sidebar-nav-item @(currentPath.StartsWith("/YourController") ? "active" : "")">
    <span class="app-sidebar-nav-icon">📋</span>
    <span>@Localizer["YourMenuLabel"]</span>
</a>
```

#### Removing a Menu Item

Simply comment out or delete the corresponding `<a>` tag.

#### Reordering Menu Items

Cut and paste the `<a>` tags in the desired order.

---

### Customizing Page Headers

All redesigned pages use this pattern:

```html
<div class="page-header">
    <div class="page-header-content">
        <h1 class="page-title">@Localizer["Title"]</h1>
        <p class="page-subtitle">@Localizer["Subtitle"]</p>
    </div>
</div>
```

#### Adding Actions to Page Header

```html
<div class="page-header">
    <div class="page-header-content">
        <h1 class="page-title">@Localizer["Title"]</h1>
        <p class="page-subtitle">@Localizer["Subtitle"]</p>
    </div>
    <div class="page-header-actions">
        <a href="/Action" class="btn btn-primary">
            <span>➕</span>
            <span>Add New</span>
        </a>
    </div>
</div>
```

Then add CSS for `.page-header-actions`:
```css
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}
```

---

### Responsive Breakpoints

Current breakpoints in `site.css`:

```css
/* Mobile */
@media (max-width: 768px) {
  /* Styles for phones and small tablets */
}

/* Desktop */
@media (min-width: 768px) {
  /* Styles for tablets and larger */
}

/* Wide screens */
@media (min-width: 1024px) {
  /* Styles for desktops */
}
```

**Example**: Add a breakpoint for very large screens:
```css
@media (min-width: 1440px) {
  .app-content {
    max-width: 1400px;
    margin: 0 auto;
  }
}
```

---

## Localization

### Adding New Localization Keys

1. **English**: Edit `Resources/SharedResources.resx`
```xml
<data name="YourNewKey" xml:space="preserve">
  <value>Your English Text</value>
</data>
```

2. **Hebrew**: Edit `Resources/SharedResources.he-IL.resx`
```xml
<data name="YourNewKey" xml:space="preserve">
  <value>הטקסט שלך בעברית</value>
</data>
```

3. **Use in Razor Pages**:
```html
<h1>@Localizer["YourNewKey"]</h1>
```

### Modifying Existing Keys

Search for the key name in both `.resx` files and update the `<value>` content.

---

## Adding New Pages with Consistent Design

### Step 1: Create the Page Files

**PageModel** (`YourPage.cshtml.cs`):
```csharp
public class YourPageModel : PageModel
{
    public void OnGet()
    {
        ViewData["Title"] = Localizer["YourPageTitle"];
    }
}
```

**View** (`YourPage.cshtml`):
```html
@page
@model YourNamespace.YourPageModel
@using Microsoft.Extensions.Localization
@using ShiftManager.Resources
@inject IStringLocalizer<SharedResources> Localizer
@{
    Layout = "_Layout";
}

@await Component.InvokeAsync("Breadcrumb", new List<BreadcrumbItem>
{
    new BreadcrumbItem { Label = Localizer["Parent"], Url = "/Parent" },
    new BreadcrumbItem { Label = Localizer["YourPage"], IsActive = true }
})

<div class="page-header">
    <div class="page-header-content">
        <h1 class="page-title">@Localizer["YourPageTitle"]</h1>
        <p class="page-subtitle">@Localizer["YourPageDescription"]</p>
    </div>
</div>

<div class="section-card">
    <h3 class="section-header">@Localizer["SectionTitle"]</h3>
    <!-- Your content here -->
</div>
```

### Step 2: Add to Sidebar (if needed)

Edit `Pages/Shared/_Layout.cshtml` and add menu item.

### Step 3: Add to Command Palette (if needed)

Edit `wwwroot/js/site.js` and add to `commandPalettePages` array.

### Step 4: Add Localization Keys

Add all necessary keys to both `.resx` files.

---

## Common Customization Scenarios

### Scenario 1: Change Primary Color to Red

**File**: `wwwroot/css/site.css`

```css
:root {
  --primary: #dc2626;          /* Red-600 */
  --primary-rgb: 220, 38, 38;
  --primary-soft: #fee2e2;     /* Red-100 */
}

:root[data-theme="dark"] {
  --primary: #f87171;          /* Red-400 */
  --primary-rgb: 248, 113, 113;
  --primary-soft: #7f1d1d;     /* Red-900 */
}
```

### Scenario 2: Add Company Logo to Sidebar

**File**: `Pages/Shared/_Layout.cshtml` (around line 52)

```html
<div class="app-sidebar-brand brand">
    <img src="/images/logo.png" alt="Logo" style="width: 32px; height: 32px;">
    <span>@Localizer["ShiftManager"]</span>
</div>
```

### Scenario 3: Change Card Border Radius

**File**: `wwwroot/css/site.css` (around line 198)

```css
.card {
  border-radius: 0.5rem; /* Change from 1rem to 0.5rem */
}

.section-card {
  border-radius: 0.5rem; /* Change from 0.75rem to 0.5rem */
}
```

### Scenario 4: Make Command Palette Open with Different Shortcut

**File**: `wwwroot/js/site.js` (around line 800)

```javascript
// Change from Ctrl+K to Ctrl+P
if ((e.ctrlKey || e.metaKey) && e.key === 'p') {
  e.preventDefault();
  toggleCommandPalette();
}
```

Update hint in `_Layout.cshtml` (line 237):
```html
<span class="command-palette-hint">Ctrl+P</span>
```

### Scenario 5: Disable Dark Mode

**Option A - Remove Toggle Button**:
Comment out lines 182-184 in `Pages/Shared/_Layout.cshtml`:
```html
<!--
<button class="action-btn" id="themeToggle" aria-label="@Localizer["ToggleDarkMode"]">
    <span>🌓</span>
</button>
-->
```

**Option B - Force Light Mode**:
In `wwwroot/js/site.js`, change line 8:
```javascript
// Force light theme
root.setAttribute('data-theme', 'light');
```

### Scenario 6: Change Sidebar Width

**File**: `wwwroot/css/site.css` (around line 143)

```css
.app-sidebar {
  width: 280px; /* Change from 260px to 280px */
}

.app-main {
  margin-left: 280px; /* Match sidebar width */
}
```

### Scenario 7: Add Footer to Pages

**File**: `Pages/Shared/_Layout.cshtml` (after line 226, before closing `</div>`):

```html
<footer class="app-footer" style="
  padding: 2rem;
  text-align: center;
  color: var(--muted);
  border-top: 1px solid var(--border);
  background: var(--surface);
">
    <p>&copy; 2025 ShiftManager. All rights reserved.</p>
</footer>
```

---

## CSS Class Reference

### Layout Classes
- `.app-shell` - Main application container
- `.app-sidebar` - Left sidebar
- `.app-main` - Main content area
- `.app-header` - Top header
- `.app-content` - Content container

### Typography Classes
- `.text-display` - Large display text (2rem)
- `.text-title` - Section titles (1.5rem)
- `.text-body` - Body text (1rem)
- `.text-subtle` - Secondary text (0.875rem)
- `.text-caption` - Small text (0.75rem)

### Component Classes
- `.btn`, `.btn-primary`, `.btn-ghost`, `.btn-danger` - Buttons
- `.card`, `.card-header`, `.card-body` - Cards
- `.section-card`, `.section-header` - Section containers
- `.form-group`, `.form-label`, `.form-input` - Forms
- `.data-table` - Tables
- `.modal`, `.modal-content`, `.modal-header` - Modals
- `.status-badge` - Status indicators

### Utility Classes
- `.page-header` - Page header container
- `.page-title` - Page titles
- `.page-subtitle` - Page descriptions
- `.breadcrumb-nav`, `.breadcrumb` - Breadcrumbs

---

## JavaScript API Reference

### Theme Functions
- `toggleTheme()` - Switch between themes
- `localStorage.getItem('theme')` - Get saved theme
- `localStorage.setItem('theme', 'dark')` - Save theme

### Command Palette Functions
- `openCommandPalette()` - Open the palette
- `closeCommandPalette()` - Close the palette
- `toggleCommandPalette()` - Toggle open/close
- `filterCommandPalette(query)` - Filter pages
- `navigateToPage(url)` - Navigate to a page

### Modal Functions
- `openShiftModal(date)` - Open shift creation modal
- `closeShiftModal()` - Close shift modal

### Toast Functions
- `showToast(message, type)` - Show toast notification
  - Types: 'info', 'success', 'error'

---

## Tips for Customization

1. **Always Test in Both Themes**: Changes should work in both light and dark modes
2. **Use CSS Variables**: Use `var(--variable-name)` instead of hard-coded colors
3. **Maintain Consistency**: Follow existing patterns for new components
4. **Test Responsively**: Check on mobile, tablet, and desktop sizes
5. **Test RTL**: If you support Hebrew, test with RTL layout
6. **Keep Accessibility**: Maintain ARIA labels and keyboard navigation
7. **Document Changes**: Add comments to explain custom modifications
8. **Version Control**: Use git to track changes and enable rollback

---

## Getting Help

If you encounter issues:

1. **Check Build Output**: `dotnet build` should show 0 errors
2. **Check Browser Console**: Look for JavaScript errors
3. **Check CSS Syntax**: Validate CSS in browser dev tools
4. **Test Incremental**: Make small changes and test often
5. **Refer to Examples**: Look at existing pages for patterns
6. **Keep Backups**: Save copies before major changes

---

## Next Steps

After customization:

1. ✅ Build the project: `dotnet build`
2. ✅ Test thoroughly in browser
3. ✅ Test responsive design
4. ✅ Test both themes
5. ✅ Test all interactive features
6. ✅ Test with real data
7. ✅ Get user feedback
8. ✅ Deploy to staging
9. ✅ Final testing
10. ✅ Deploy to production

---

**Remember**: All files are preserved and ready for your review. No cleanup or deletions have been performed. You can make any changes you want, and all documentation is here to help guide you.
