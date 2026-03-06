# ShiftManager Component Library

> **Design System:** "Shifty" Brand System v3.0 (UI Overhaul)
> **Framework:** ASP.NET Core Razor Pages with ViewComponents
> **Last Updated:** 2026-02-01

## Table of Contents

1. [Overview](#overview)
2. [Design Tokens](#design-tokens)
3. [Navigation Components](#navigation-components)
   - [Breadcrumb](#breadcrumb)
   - [ContextSwitcher](#contextswitcher)
   - [ScopeSwitcher](#scopeswitcher)
4. [Feedback Components](#feedback-components)
   - [ErrorBanner](#errorbanner)
   - [ErrorToast](#errortoast)
   - [LoadingSpinner](#loadingspinner)
   - [LoadingSkeleton](#loadingskeleton)
   - [CalendarSkeleton](#calendarskeleton)
5. [Data Display Components](#data-display-components)
   - [Pagination](#pagination)
   - [UnreadNotificationCount](#unreadnotificationcount)
   - [DecisionRibbon](#decisionribbon)
   - [OnCallWidget](#oncallwidget)
6. [User Preference Components](#user-preference-components)
   - [LanguageToggle](#languagetoggle)
   - [ShowMyItemsToggle](#showmyitemstoggle)
   - [OwnerCompanySelector](#ownercompanyselector)
7. [Layout Banners](#layout-banners)
   - [LanguageEditModeBanner](#languageeditmodebanner)
8. [Accessibility Guidelines](#accessibility-guidelines)
9. [CSS Class Reference](#css-class-reference)

---

## Overview

The ShiftManager Component Library is built on ASP.NET Core ViewComponents, providing reusable, server-rendered UI elements that integrate seamlessly with Razor Pages. All components:

- Support **RTL (Right-to-Left)** layouts for Hebrew
- Are **fully localized** using `IStringLocalizer<SharedResources>`
- Meet **WCAG AA accessibility** standards
- Work in both **light and dark modes**
- Use the **Shifty design token system** (`tokens.css`)

### Component Locations

| Type | Location |
|------|----------|
| ViewComponent Classes | `ViewComponents/*.cs` |
| Razor Views | `Pages/Shared/Components/{ComponentName}/Default.cshtml` |
| Component Styles | `wwwroot/css/components.css` |
| Design Tokens | `wwwroot/css/tokens.css` |

---

## Design Tokens

All components reference design tokens from `tokens.css`. Key token categories:

### Colors

```css
/* Primary Palette */
--primary: #1E3A5F;          /* Deep Navy - primary actions */
--primary-hover: #2C4A73;    /* Hover state */
--primary-soft: #E8F1F8;     /* Backgrounds */
--primary-contrast: #FFFFFF; /* Text on primary */

/* Semantic Colors */
--success: #2D6A4F;          /* Success states */
--warning: #D4A017;          /* Warning states */
--danger: #9B2C2C;           /* Error/danger states */
--info: #1E3A5F;             /* Informational */

/* Surfaces */
--bg: #F8F9FB;               /* Page background */
--surface: #FFFFFF;          /* Card/panel background */
--surface-soft: #F0F4F8;     /* Subtle backgrounds */
```

### Typography

```css
--text-display: 2rem;     /* 32px - Page titles */
--text-title: 1.5rem;     /* 24px - Section headers */
--text-heading: 1.25rem;  /* 20px - Card titles */
--text-body: 1rem;        /* 16px - Body text */
--text-small: 0.875rem;   /* 14px - Captions, labels */
--text-tiny: 0.75rem;     /* 12px - Badges, metadata */
```

### Spacing (8px base)

```css
--space-1: 0.25rem;  /* 4px */
--space-2: 0.5rem;   /* 8px */
--space-3: 0.75rem;  /* 12px */
--space-4: 1rem;     /* 16px */
--space-6: 1.5rem;   /* 24px */
--space-8: 2rem;     /* 32px */
```

### Border Radius

```css
--radius-sm: 4px;    /* Buttons, inputs */
--radius-md: 8px;    /* Cards, dropdowns */
--radius-lg: 12px;   /* Modals, panels */
--radius-full: 9999px; /* Pills, avatars */
```

---

## Navigation Components

### Breadcrumb

Displays hierarchical navigation path.

**Location:** `ViewComponents/BreadcrumbViewComponent.cs`

#### Usage

```razor
@await Component.InvokeAsync("Breadcrumb", new { items = new List<BreadcrumbItem>
{
    new BreadcrumbItem { Label = "Home", Url = "/" },
    new BreadcrumbItem { Label = "Employees", Url = "/Manager/Employees" },
    new BreadcrumbItem { Label = "John Doe", IsActive = true }
}})
```

#### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `items` | `List<BreadcrumbItem>` | Yes | List of breadcrumb items |

#### BreadcrumbItem Properties

| Property | Type | Description |
|----------|------|-------------|
| `Label` | `string` | Display text |
| `Url` | `string?` | Navigation URL (null for non-clickable) |
| `IsActive` | `bool` | True for current page (last item) |

#### CSS Classes

```css
.breadcrumb              /* Container */
.breadcrumb__item        /* Individual item */
.breadcrumb__separator   /* Separator between items */
```

#### Accessibility

- Uses `<nav>` semantic element
- Active item has `aria-current="page"`

---

### ContextSwitcher

Allows users to switch between organizational contexts (companies/molecules).

**Location:**
- `ViewComponents/ContextSwitcherViewComponent.cs`
- `Pages/Shared/Components/ContextSwitcher/Default.cshtml`

#### Usage

```razor
@await Component.InvokeAsync("ContextSwitcher")
```

#### Parameters

None - automatically detects user role and available contexts.

#### Features

- **Search filtering** with debounced input (150ms)
- **Grouped by molecule** with collapsible sections
- **Keyboard navigation** (Arrow keys, Enter, Escape)
- **Long name truncation** with tooltip on hover
- **Edge case handling:**
  - Error state with retry button
  - No contexts available
  - Single context (non-interactive display)
  - 50+ contexts (performance hints)

#### CSS Classes

```css
.context-switcher                    /* Container */
.context-switcher--error             /* Error state */
.context-switcher--empty             /* No contexts */
.context-switcher--single            /* Single context */
.context-switcher--many              /* 50+ contexts */
.context-switcher__trigger           /* Dropdown trigger button */
.context-switcher__dropdown          /* Dropdown panel */
.context-switcher__search-input      /* Search field */
.context-switcher__group             /* Molecule group */
.context-switcher__option            /* Individual option */
.context-switcher__option.is-active  /* Selected option */
.context-switcher__option.is-focused /* Keyboard focused */
```

#### Accessibility

- `role="listbox"` on dropdown
- `role="option"` on each item
- `aria-selected` for current selection
- `aria-expanded` on trigger
- Screen reader announcements for context changes

---

### ScopeSwitcher

Toggle between viewing scopes (Mine/Company/Molecule/Area).

**Location:**
- `ViewComponents/ScopeSwitcherViewComponent.cs`
- `Pages/Shared/Components/ScopeSwitcher/Default.cshtml`

#### Usage

```razor
@await Component.InvokeAsync("ScopeSwitcher", new {
    currentScope = "company",
    calendarType = "shifts"
})
```

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `currentScope` | `string?` | Auto-detected | Current scope (mine/company/molecule/area) |
| `calendarType` | `string` | "shifts" | Calendar type context (shifts/chores) |

#### Scope Options

| Scope | Icon | Description |
|-------|------|-------------|
| `mine` | User | Only user's own items |
| `company` | Building | Company-wide view |
| `molecule` | Group | Cross-company molecule view (grant-based) |
| `area` | Globe | Regional area view (grant-based) |

#### CSS Classes

```css
.scope-switcher                     /* Container */
.scope-switcher__label              /* "View:" label */
.scope-switcher__buttons            /* Button group */
.scope-switcher__btn                /* Individual button */
.scope-switcher__btn--active        /* Selected scope */
.scope-switcher__btn--loading       /* Loading state */
.scope-switcher__btn-icon           /* Icon span */
.scope-switcher__btn-label          /* Label span */
```

#### Accessibility

- `role="tablist"` on button container
- `role="tab"` on each button
- `aria-selected` for active tab
- Roving tabindex for keyboard navigation
- Arrow key navigation (Left/Right, Up/Down)

---

## Feedback Components

### ErrorBanner

Persistent error/warning/info banner for page-level messages.

**Location:**
- `ViewComponents/ErrorBannerViewComponent.cs`
- `Pages/Shared/Components/ErrorBanner/Default.cshtml`

#### Usage

```razor
@* Error with retry *@
@await Component.InvokeAsync("ErrorBanner", new {
    level = "error",
    messageKey = "Error_LoadFailed",
    fallbackMessage = "Failed to load data. Please try again.",
    showRetry = true,
    retryAction = "location.reload()"
})

@* Warning (dismissible) *@
@await Component.InvokeAsync("ErrorBanner", new {
    level = "warning",
    titleKey = "Warning_Title",
    fallbackTitle = "Warning",
    messageKey = "Warning_SessionExpiring",
    fallbackMessage = "Your session will expire in 5 minutes.",
    dismissible = true
})

@* Info (non-dismissible) *@
@await Component.InvokeAsync("ErrorBanner", new {
    level = "info",
    messageKey = "Info_Maintenance",
    fallbackMessage = "Scheduled maintenance tonight.",
    dismissible = false
})
```

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `level` | `string` | "error" | Severity: "error", "warning", "info" |
| `messageKey` | `string` | "" | Localization key for message |
| `fallbackMessage` | `string` | "" | Fallback if key not found |
| `dismissible` | `bool` | `true` | Show close button |
| `showRetry` | `bool` | `false` | Show retry button |
| `retryAction` | `string?` | `null` | JavaScript for retry (e.g., "location.reload()") |
| `titleKey` | `string?` | `null` | Localization key for title |
| `fallbackTitle` | `string?` | `null` | Fallback title |

#### CSS Classes

```css
.error-banner                /* Container */
.error-banner--error         /* Error variant (red) */
.error-banner--warning       /* Warning variant (yellow) */
.error-banner--info          /* Info variant (blue) */
.error-banner__icon          /* Icon container */
.error-banner__content       /* Text content area */
.error-banner__title         /* Optional title */
.error-banner__message       /* Message text */
.error-banner__actions       /* Button container */
.error-banner__retry         /* Retry button */
.error-banner__close         /* Close/dismiss button */
```

#### Accessibility

- `role="alert"` for screen reader announcement
- `aria-live="polite"` for non-intrusive updates
- Focus-visible styles on interactive elements

---

### ErrorToast

Transient toast notification with auto-dismiss.

**Location:**
- `ViewComponents/ErrorToastViewComponent.cs`
- `Pages/Shared/Components/ErrorToast/Default.cshtml`

#### Usage

```razor
@await Component.InvokeAsync("ErrorToast", new {
    level = "success",
    messageKey = "Toast_SaveSuccess",
    fallbackMessage = "Changes saved successfully!",
    autoDismissMs = 3000
})

@await Component.InvokeAsync("ErrorToast", new {
    level = "error",
    titleKey = "Toast_ErrorTitle",
    fallbackTitle = "Error",
    messageKey = "Toast_SaveFailed",
    fallbackMessage = "Failed to save changes.",
    autoDismissMs = 0,  // Don't auto-dismiss errors
    showCloseButton = true
})
```

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `level` | `string` | "error" | Severity: "error", "warning", "info", "success" |
| `messageKey` | `string` | "" | Localization key for message |
| `fallbackMessage` | `string` | "" | Fallback if key not found |
| `titleKey` | `string?` | `null` | Localization key for title |
| `fallbackTitle` | `string?` | `null` | Fallback title |
| `autoDismissMs` | `int` | `5000` | Auto-dismiss delay (0 = never) |
| `showCloseButton` | `bool` | `true` | Show manual close button |

#### CSS Classes

```css
.toast                  /* Container */
.toast--success         /* Success variant (green border) */
.toast--warning         /* Warning variant (yellow border) */
.toast--danger          /* Error variant (red border) */
.toast--info            /* Info variant (blue border) */
.toast--dismissing      /* Exit animation state */
.toast__icon            /* Icon container */
.toast__content         /* Text content area */
.toast__title           /* Optional title */
.toast__message         /* Message text */
.toast__close           /* Close button */
```

#### Accessibility

- `role="alert"` for immediate announcement
- `aria-live="assertive"` for important messages

---

### LoadingSpinner

Animated loading indicator with optional label.

**Location:**
- `ViewComponents/LoadingSpinnerViewComponent.cs`
- `Pages/Shared/Components/LoadingSpinner/Default.cshtml`

#### Usage

```razor
@* Default medium spinner *@
@await Component.InvokeAsync("LoadingSpinner")

@* Small spinner without label *@
@await Component.InvokeAsync("LoadingSpinner", new {
    size = "small",
    showLabel = false
})

@* Large spinner with custom label *@
@await Component.InvokeAsync("LoadingSpinner", new {
    size = "large",
    label = "Processing your request..."
})
```

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `size` | `string` | "medium" | Size: "small", "medium", "large" |
| `label` | `string?` | `null` | Custom label (uses "Loading..." if null) |
| `showLabel` | `bool` | `true` | Whether to display label text |

#### CSS Classes

```css
.loading-spinner        /* Container */
.spinner                /* Animated spinner element */
.spinner--sm            /* Small size (16px) */
.spinner--lg            /* Large size (32px) */
.loading-spinner__label /* Label text */
```

#### Accessibility

- `role="status"` for loading announcement
- `aria-live="polite"` for non-intrusive updates
- `.visually-hidden` span for screen readers

---

### LoadingSkeleton

Content placeholder skeleton for loading states.

**Location:**
- `ViewComponents/LoadingSkeletonViewComponent.cs`
- `Pages/Shared/Components/LoadingSkeleton/Default.cshtml`

#### Usage

```razor
@* Single text line skeleton *@
@await Component.InvokeAsync("LoadingSkeleton")

@* Multiple heading skeletons *@
@await Component.InvokeAsync("LoadingSkeleton", new {
    variant = "heading",
    count = 3
})

@* Custom size avatar skeleton *@
@await Component.InvokeAsync("LoadingSkeleton", new {
    variant = "avatar",
    width = "64px",
    height = "64px"
})

@* Card skeleton *@
@await Component.InvokeAsync("LoadingSkeleton", new {
    variant = "card"
})
```

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `variant` | `string` | "text" | Type: "text", "text-sm", "heading", "avatar", "btn", "card", "row" |
| `count` | `int` | `1` | Number of skeleton items to render |
| `width` | `string?` | `null` | Custom CSS width |
| `height` | `string?` | `null` | Custom CSS height |

#### Skeleton Variants

| Variant | Default Size | Use Case |
|---------|--------------|----------|
| `text` | 100% width, 1em height | Body text |
| `text-sm` | 80% width, 0.875em height | Small text |
| `heading` | 60% width, 1.5em height | Headings |
| `avatar` | 40x40px, circular | User avatars |
| `btn` | 100px x 40px | Buttons |
| `card` | 100% x 150px | Card containers |
| `row` | 100% x 48px | Table rows |

#### CSS Classes

```css
.skeleton-container    /* Container for multiple skeletons */
.skeleton              /* Base skeleton element */
.skeleton--text        /* Text line */
.skeleton--text-sm     /* Small text line */
.skeleton--heading     /* Heading */
.skeleton--avatar      /* Circular avatar */
.skeleton--btn         /* Button shape */
.skeleton--card        /* Card shape */
.skeleton--row         /* Table row shape */
```

#### Accessibility

- `role="status"` on container
- `aria-busy="true"` during loading
- `aria-hidden="true"` on skeleton elements
- `.visually-hidden` text for screen readers

---

### CalendarSkeleton

Specialized skeleton for calendar loading states.

**Location:**
- `ViewComponents/CalendarSkeletonViewComponent.cs`
- `Pages/Shared/Components/CalendarSkeleton/Default.cshtml`
- Partials: `_MonthSkeleton.cshtml`, `_WeekSkeleton.cshtml`, `_DaySkeleton.cshtml`, `_TableSkeleton.cshtml`

#### Usage

```razor
@* Month view skeleton (default) *@
@await Component.InvokeAsync("CalendarSkeleton")

@* Week view skeleton *@
@await Component.InvokeAsync("CalendarSkeleton", new {
    viewType = "week"
})

@* Day view with custom item count *@
@await Component.InvokeAsync("CalendarSkeleton", new {
    viewType = "day",
    itemCount = 8
})

@* Table view skeleton *@
@await Component.InvokeAsync("CalendarSkeleton", new {
    viewType = "table"
})
```

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `viewType` | `string` | "month" | Calendar view: "month", "week", "day", "table" |
| `itemCount` | `int` | `5` | Items per cell/column |
| `cellCount` | `int` | `35` | Month view: number of cells (5 weeks) |

#### View Types

| View | Description |
|------|-------------|
| `month` | 7-column grid with 35 cells, weekday headers |
| `week` | 7 columns with day headers and items |
| `day` | Single column with multiple item cards |
| `table` | Employee/shift type matrix table |

#### CSS Classes

```css
.calendar-skeleton              /* Main container */
.skeleton-calendar-header       /* Header area */
.skeleton-calendar-metrics      /* Metrics bar */
.skeleton-view-switcher         /* View toggle buttons */
.skeleton-calendar-grid         /* Month grid */
.skeleton-calendar-cell         /* Month cell */
.skeleton-week-grid             /* Week grid */
.skeleton-week-column           /* Week column */
.skeleton-day-container         /* Day view container */
.skeleton-day-item              /* Day item card */
.skeleton-table                 /* Table view */
.calendar-skeleton-shimmer      /* Shimmer animation class */
```

#### Accessibility

- `role="status"` on container
- `aria-busy="true"` during loading
- `aria-label` with localized "Loading" text

---

## Data Display Components

### Pagination

Table pagination with page numbers, navigation, and per-page selector.

**Location:**
- `ViewComponents/PaginationViewComponent.cs`
- `Pages/Shared/Components/Pagination/Default.cshtml`

#### Usage

```razor
@await Component.InvokeAsync("Pagination", new {
    currentPage = Model.CurrentPage,
    totalPages = Model.TotalPages,
    totalItems = Model.TotalItems,
    pageSize = Model.PageSize
})

@* With custom options *@
@await Component.InvokeAsync("Pagination", new {
    currentPage = 3,
    totalPages = 15,
    totalItems = 147,
    pageSize = 10,
    baseUrl = "/Manager/Employees",
    pageSizeOptions = new[] { 10, 25, 50 },
    queryStringKey = "p"
})
```

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `currentPage` | `int` | Required | Current page (1-based) |
| `totalPages` | `int` | Required | Total number of pages |
| `totalItems` | `int` | Required | Total item count |
| `pageSize` | `int` | Required | Items per page |
| `baseUrl` | `string?` | Current path | Base URL for links |
| `pageSizeOptions` | `int[]?` | `[10, 25, 50, 100]` | Page size dropdown options |
| `queryStringKey` | `string?` | "page" | Query parameter name |

#### Features

- "Showing X-Y of Z" info always visible
- Page numbers: first, last, current +/- 1 with ellipsis
- Previous/Next buttons disabled at boundaries
- Per-page dropdown resets to page 1
- Preserves existing query parameters

#### CSS Classes

```css
.pagination              /* Container */
.pagination__info        /* "Showing X-Y of Z" text */
.pagination__controls    /* Page number buttons container */
.pagination__btn         /* Individual page button */
.pagination__btn.is-active /* Current page */
.pagination__btn--nav    /* Prev/Next buttons */
.pagination__ellipsis    /* "..." between page numbers */
.pagination__per-page    /* Per-page selector container */
```

#### Accessibility

- `role="navigation"` on container
- `aria-label` for navigation region
- `aria-current="page"` on active page
- `aria-label` on prev/next buttons
- `aria-disabled` on boundary buttons
- Localized text throughout

---

### UnreadNotificationCount

Badge showing unread notification count.

**Location:**
- `ViewComponents/UnreadNotificationCountViewComponent.cs`
- `Pages/Shared/Components/UnreadNotificationCount/Default.cshtml`

#### Usage

```razor
<div class="notification-icon-container">
    <span class="notification-icon">Notifications</span>
    @await Component.InvokeAsync("UnreadNotificationCount")
</div>
```

#### Parameters

None - automatically fetches count for current user.

#### Features

- Shows badge only when count > 0
- Displays "99+" for counts over 99
- Returns empty for unauthenticated users

#### CSS Classes

```css
.notification-badge  /* Badge element (inline styles in component) */
```

---

### DecisionRibbon

Banner showing pending approval count for managers/directors.

**Location:** `ViewComponents/DecisionRibbonViewComponent.cs`

#### Usage

```razor
@await Component.InvokeAsync("DecisionRibbon")
```

#### Parameters

None - automatically detects user role and fetches counts.

#### Visibility Rules

| Role | Sees |
|------|------|
| Employee/Trainee | Hidden |
| Manager | Pending time-off requests |
| Director/Owner/Assigner | All pending requests (time-off + swap) |
| On /Owner/* routes | Always hidden |

---

### OnCallWidget

Collapsible widget showing current on-call contacts.

**Location:**
- `ViewComponents/OnCallWidgetViewComponent.cs`
- `Pages/Shared/Components/OnCallWidget/Default.cshtml`

#### Usage

```razor
@* In sidebar *@
@await Component.InvokeAsync("OnCallWidget", new { showInSidebar = true })

@* Standalone *@
@await Component.InvokeAsync("OnCallWidget", new { showInSidebar = false })
```

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `showInSidebar` | `bool` | `true` | Apply sidebar widget styles |

#### Contact Types

| Type | Description |
|------|-------------|
| `Hakam` | On-call commander (molecule-wide) |
| `CompanyOnCall` | Company-specific BR/Katzin |
| `Friend` | (Reserved for future use) |

#### Features

- Phone number click-to-call links
- Avatar initials
- Contact role and company display
- Collapsible with state persistence
- Empty state with helpful message

#### CSS Classes

```css
.widget                  /* Generic widget container */
.oncall-widget           /* OnCall-specific styles */
.sidebar-widget          /* Sidebar positioning */
.widget__header          /* Collapsible header */
.widget__body            /* Content area */
.widget__badge           /* Contact count badge */
.oncall-contact          /* Individual contact card */
.oncall-contact__avatar  /* Avatar circle */
.oncall-contact__info    /* Contact details */
.oncall-contact__actions /* Call button */
.widget-empty            /* Empty state container */
```

#### Accessibility

- Collapsible header has `role="button"` and `tabindex="0"`
- `aria-expanded` state tracking
- Phone links use `tel:` protocol
- `.visually-hidden` labels for icon-only buttons

---

## User Preference Components

### LanguageToggle

Button to switch between company's default and alternate languages.

**Location:**
- `ViewComponents/LanguageToggleViewComponent.cs`
- `Pages/Shared/Components/LanguageToggle/Default.cshtml`

#### Usage

```razor
@await Component.InvokeAsync("LanguageToggle")
```

#### Parameters

None - automatically detects current language and company settings.

#### Features

- Shows current language indicator (En/Hebrew letters)
- Respects company's language configuration
- Sets `.AspNetCore.Culture` cookie on toggle
- Automatic page reload on change

#### CSS Classes

```css
.language-toggle      /* Container */
.language-btn         /* Toggle button */
.language-icon        /* Globe icon */
.language-text        /* Current language indicator */
```

---

### ShowMyItemsToggle

Toggle to filter calendar between "My Items Only" and "All Items".

**Location:**
- `ViewComponents/ShowMyItemsToggleViewComponent.cs`
- `Pages/Shared/Components/ShowMyItemsToggle/Default.cshtml`

#### Usage

```razor
@await Component.InvokeAsync("ShowMyItemsToggle")
```

#### Parameters

None - reads preference from `IUserPreferenceService`.

#### Features

- Two-state toggle with descriptive labels
- Icon changes based on state (user icon vs. group icon)
- Persists preference in cookie (30 days)
- Automatic page reload on toggle

#### CSS Classes

```css
.show-my-items-toggle    /* Container */
.show-items-btn          /* Toggle button */
.show-items-btn.active   /* "My Items Only" active */
.toggle-icon             /* Icon span */
.toggle-labels           /* Label container */
.toggle-label-primary    /* Main state label */
.toggle-label-secondary  /* Action hint label */
```

---

### OwnerCompanySelector

Dropdown for Owners to select which company to manage.

**Location:** `ViewComponents/OwnerCompanySelectorViewComponent.cs`

#### Usage

```razor
@await Component.InvokeAsync("OwnerCompanySelector")
```

#### Parameters

None - visible only to Owner role users.

#### Features

- Lists all companies alphabetically
- Highlights home company
- Shows current selection
- Persists selection in session

---

## Layout Banners

### LanguageEditModeBanner

Banner displayed when language edit mode is active.

**Location:** `ViewComponents/LanguageEditModeBannerViewComponent.cs`

#### Usage

```razor
@await Component.InvokeAsync("LanguageEditModeBanner")
```

#### Parameters

None - reads state from cookies.

#### Visibility

Only renders when all cookies are present:
- `language_edit_mode = "true"`
- `language_edit_companyId` (valid int)
- `language_edit_culture` (culture code)

---

## Accessibility Guidelines

All components follow these accessibility standards:

### WCAG AA Compliance

1. **Color Contrast**
   - Text on backgrounds: minimum 4.5:1 ratio
   - Large text: minimum 3:1 ratio
   - Focus indicators: minimum 3:1 ratio

2. **Keyboard Navigation**
   - All interactive elements focusable with Tab
   - Arrow key navigation in dropdowns/tabs
   - Escape key closes modals/dropdowns
   - Enter/Space activates buttons

3. **Screen Reader Support**
   - Semantic HTML elements (`<nav>`, `<button>`, etc.)
   - ARIA roles where needed (`role="alert"`, `role="status"`)
   - Live regions for dynamic updates (`aria-live`)
   - Descriptive labels (`aria-label`, `aria-labelledby`)

4. **Focus Management**
   - Visible focus indicators (2px solid primary color)
   - Focus trapped in modals
   - Focus restored after modal close

5. **Reduced Motion**
   - `@media (prefers-reduced-motion: reduce)` respected
   - Animations reduced to 0.01ms
   - Essential feedback maintained

### Helper Classes

```css
.sr-only           /* Screen reader only text */
.visually-hidden   /* Equivalent to .sr-only */
.skip-link         /* Skip to main content link */
.keyboard-focused  /* Keyboard focus indicator */
```

---

## CSS Class Reference

### Button Classes

```css
/* Base */
.btn                 /* Base button styles */
.btn--primary        /* Primary action */
.btn--secondary      /* Secondary action */
.btn--outline        /* Outlined button */
.btn--ghost          /* Ghost/text button */
.btn--danger         /* Destructive action */
.btn--success        /* Positive action */

/* Sizes */
.btn--sm             /* Small (32px) */
.btn--lg             /* Large (48px) */
.btn--icon           /* Icon-only button */

/* States */
.btn.is-loading      /* Loading spinner */
.btn.is-disabled     /* Disabled state */
```

### Badge Classes

```css
.badge               /* Base badge */
.badge--primary      /* Primary color */
.badge--success      /* Success color */
.badge--warning      /* Warning color */
.badge--danger       /* Danger color */
.badge--info         /* Info color */
.badge--neutral      /* Neutral/gray */
.badge--sm           /* Small size */
.badge--lg           /* Large size */
.badge--solid        /* Solid background variant */
```

### Card Classes

```css
.card                /* Base card */
.card--elevated      /* Elevated shadow */
.card--interactive   /* Hover effects */
.card--clickable     /* Clickable card */
.card__header        /* Card header */
.card__title         /* Card title */
.card__body          /* Card content */
.card__footer        /* Card footer */
```

### Form Classes

```css
.form-field          /* Field wrapper */
.form-field__label   /* Field label */
.form-field__input   /* Input element */
.form-field__error   /* Error message */
.form-field__helper  /* Helper text */
.form-field.has-error /* Error state */
.input--error        /* Input error state */
.input--success      /* Input success state */
.validation-message  /* Validation message */
```

### Alert Classes

```css
.alert               /* Base alert */
.alert--info         /* Info alert */
.alert--success      /* Success alert */
.alert--warning      /* Warning alert */
.alert--danger       /* Danger alert */
.alert__icon         /* Alert icon */
.alert__content      /* Alert content */
.alert__title        /* Alert title */
.alert__message      /* Alert message */
```

### Shift Color Classes

```css
.shift-morning       /* Morning shift (yellow) */
.shift-middle        /* Afternoon shift (olive) */
.shift-noon          /* Noon shift */
.shift-night         /* Night shift (navy) */
.chore-green         /* Chore items (green) */
.onduty-hakam        /* Hakam duty (brown) */
.onduty-lead         /* Lead duty (orange) */
```

---

## Component Usage Examples

### Complete Page Example

```razor
@page "/Manager/Dashboard"
@using ShiftManager.ViewComponents

<div class="page-header">
    @await Component.InvokeAsync("Breadcrumb", new { items = breadcrumbItems })

    <div class="page-actions">
        @await Component.InvokeAsync("ScopeSwitcher", new { calendarType = "shifts" })
        @await Component.InvokeAsync("ShowMyItemsToggle")
        @await Component.InvokeAsync("LanguageToggle")
    </div>
</div>

@if (hasError)
{
    @await Component.InvokeAsync("ErrorBanner", new {
        level = "error",
        messageKey = "Dashboard_LoadError",
        fallbackMessage = "Failed to load dashboard data.",
        showRetry = true
    })
}

@if (isLoading)
{
    @await Component.InvokeAsync("CalendarSkeleton", new { viewType = "month" })
}
else
{
    <!-- Actual calendar content -->
}

<div class="sidebar">
    @await Component.InvokeAsync("OnCallWidget", new { showInSidebar = true })
</div>

@if (showPagination)
{
    @await Component.InvokeAsync("Pagination", new {
        currentPage = Model.Page,
        totalPages = Model.TotalPages,
        totalItems = Model.TotalCount,
        pageSize = Model.PageSize
    })
}
```

---

## Changelog

| Version | Date | Changes |
|---------|------|---------|
| 3.0 | 2026-02-01 | Initial component library documentation (B-025) |
