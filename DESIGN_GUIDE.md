# ShiftManager UI/UX Design Guide
**"Soft Air, Tactical Truth" Design Philosophy**

Version: 2.0
Last Updated: January 2026
Status: Living Document

---

## Table of Contents

1. [Design Philosophy](#design-philosophy)
2. [Design Tokens](#design-tokens)
3. [Animation System](#animation-system)
4. [Color System](#color-system)
5. [Component Patterns](#component-patterns)
6. [Responsive Design](#responsive-design)
7. [RTL & Localization](#rtl--localization)
8. [Dark Mode](#dark-mode)
9. [Role-Based UI](#role-based-ui)
10. [Code Patterns](#code-patterns)
11. [Examples](#examples)

---

## Design Philosophy

### Core Principle: "Soft Air, Tactical Truth"

This dual philosophy balances aesthetic elegance with operational clarity.

#### **Soft Air** - The Aesthetic Layer
Soft Air creates a calm, premium user experience through:

- **Gentle Visual Movement**: Smooth transitions, subtle animations, floating effects
- **Atmospheric Backgrounds**: Gradient overlays with rgba transparency
- **Generous Whitespace**: Breathing room between elements
- **Blur Effects**: Backdrop-filter for depth and polish
- **Shimmer & Glow**: Subtle light effects on interaction
- **Curved Motion**: Cubic-bezier easing for organic feel

**Design Intent**: Users should feel calm, confident, and in control. The interface should never feel rushed, harsh, or overwhelming.

#### **Tactical Truth** - The Information Layer
Tactical Truth delivers actionable operational data:

- **At-a-Glance Metrics**: Critical numbers visible without clicking
- **Contextual Information**: Right data, right place, right time
- **Role-Based Display**: Admins see coverage %, employees see personal items
- **Clear Visual Hierarchy**: Important information stands out
- **Actionable Data**: Numbers that drive decisions

**Design Intent**: Users should immediately understand their operational status and know what actions to take.

### When to Apply Each Principle

| Scenario | Soft Air | Tactical Truth |
|----------|----------|----------------|
| Page transitions | ✅ Fade in/out with ease | ❌ No delays on load |
| Button hovers | ✅ Lift, glow, shimmer | ✅ Show action preview |
| Loading states | ✅ Gentle pulse animation | ✅ Show what's loading |
| Error messages | ✅ Soft red background | ✅ Clear error + fix action |
| Dashboard cards | ✅ Gradient backgrounds | ✅ Key metrics in header |
| Calendar headers | ✅ Blur, shimmer, bounce icons | ✅ Shift count, pending items |

---

## Design Tokens

### CSS Custom Properties

All v2 components use standardized design tokens defined in `:root`:

```css
:root {
  /* Spacing Scale (0.25rem base = 4px) */
  --spacing-xs: 0.5rem;    /* 8px */
  --spacing-sm: 0.75rem;   /* 12px */
  --spacing-md: 1rem;      /* 16px */
  --spacing-lg: 1.5rem;    /* 24px */
  --spacing-xl: 2rem;      /* 32px */
  --spacing-2xl: 3rem;     /* 48px */

  /* Border Radius */
  --radius-sm: 0.25rem;    /* 4px */
  --radius-md: 0.5rem;     /* 8px */
  --radius-lg: 0.75rem;    /* 12px */
  --radius-xl: 1rem;       /* 16px */
  --radius-full: 9999px;   /* Pill shape */

  /* Shadows */
  --shadow-xs: 0 1px 2px rgba(0, 0, 0, 0.05);
  --shadow-sm: 0 2px 4px rgba(0, 0, 0, 0.08);
  --shadow-md: 0 4px 8px rgba(0, 0, 0, 0.12);
  --shadow-lg: 0 8px 16px rgba(0, 0, 0, 0.15);
  --shadow-xl: 0 12px 24px rgba(0, 0, 0, 0.18);

  /* Colors - Primary */
  --primary-color: #007bff;
  --primary-color-light: #4da3ff;
  --primary-color-dark: #0056b3;
  --primary-rgb: 0, 123, 255; /* For rgba() */

  /* Animation Durations */
  --duration-fast: 0.15s;
  --duration-normal: 0.3s;
  --duration-slow: 0.5s;

  /* Animation Easing */
  --ease-standard: cubic-bezier(0.4, 0.0, 0.2, 1);
  --ease-bounce: cubic-bezier(0.68, -0.55, 0.265, 1.55);
  --ease-smooth: cubic-bezier(0.25, 0.1, 0.25, 1);
}
```

**Usage Pattern:**
```css
.my-component {
  padding: var(--spacing-md);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-md);
  transition: all var(--duration-normal) var(--ease-standard);
}
```

---

## Animation System

### Core Animations

#### 1. **Entrance Animations**

**slideDown** - For headers and important sections:
```css
@keyframes slideDown {
  from {
    opacity: 0;
    transform: translateY(-20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.calendar-header {
  animation: slideDown 0.4s cubic-bezier(0.68, -0.55, 0.265, 1.55);
}
```

**fadeIn** - For content that appears:
```css
@keyframes fadeIn {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

.metric-item {
  animation: fadeIn 0.5s ease-out 0.2s both; /* Delay for stagger */
}
```

**iconBounce** - For playful icon entrances:
```css
@keyframes iconBounce {
  0%, 100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-8px);
  }
}

.calendar-icon {
  animation: iconBounce 0.6s ease-out 0.4s both;
}
```

#### 2. **Hover Animations**

**Standard Button Hover:**
```css
.btn {
  transition: all 0.3s cubic-bezier(0.68, -0.55, 0.265, 1.55);
}

.btn:hover {
  transform: translateY(-3px) scale(1.05);
  box-shadow: 0 8px 16px rgba(var(--primary-rgb), 0.3);
}

.btn:active {
  transform: translateY(-1px) scale(1.02);
}
```

**Card Hover with Glow:**
```css
.card {
  transition: all 0.3s var(--ease-standard);
}

.card:hover {
  transform: translateY(-4px);
  box-shadow: 0 12px 24px rgba(0, 0, 0, 0.15),
              0 0 0 1px rgba(var(--primary-rgb), 0.1);
}
```

#### 3. **Shimmer Effect**

For premium polish on headers and cards:

```css
.element::before {
  content: '';
  position: absolute;
  top: 0;
  left: -100%;
  width: 100%;
  height: 100%;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.1), transparent);
  animation: shimmer 3s infinite;
  pointer-events: none;
}

@keyframes shimmer {
  0% { left: -100%; }
  100% { left: 100%; }
}
```

**RTL Version:**
```css
[dir="rtl"] .element::before {
  animation: shimmerRtl 3s infinite;
}

@keyframes shimmerRtl {
  0% { right: -100%; left: auto; }
  100% { right: 100%; left: auto; }
}
```

#### 4. **Staggered Animations**

For sequential entrance of multiple elements:

```css
.header-main {
  animation: fadeIn 0.5s ease-out 0.2s both;
}

.calendar-nav {
  animation: fadeIn 0.5s ease-out 0.3s both;
}

.calendar-metrics {
  animation: fadeIn 0.5s ease-out 0.4s both;
}

/* Individual metric items */
.metric-item:nth-child(1) .metric-icon { animation-delay: 0.1s; }
.metric-item:nth-child(2) .metric-icon { animation-delay: 0.2s; }
.metric-item:nth-child(3) .metric-icon { animation-delay: 0.3s; }
```

### Animation Guidelines

✅ **DO:**
- Use `transform` and `opacity` for 60fps performance
- Add `will-change: transform` for complex animations (sparingly)
- Use cubic-bezier for personality (bounce, smooth)
- Stagger entrance animations for visual interest
- Keep durations under 0.6s for interactions
- Use `animation-fill-mode: both` to prevent FOUC

❌ **DON'T:**
- Animate `width`, `height`, `left`, `right` (causes reflow)
- Use animations longer than 1 second for UI interactions
- Animate on scroll (performance issues)
- Use linear easing (feels robotic)
- Overuse `will-change` (memory issues)

---

## Color System

### Semantic Color Usage

#### **Primary Colors**
Used for actionable elements, primary CTAs, and brand identity:

```css
/* Solid Primary */
background: var(--primary-color);           /* #007bff */

/* With Transparency (Soft Air) */
background: rgba(var(--primary-rgb), 0.05); /* Very subtle */
background: rgba(var(--primary-rgb), 0.1);  /* Subtle */
background: rgba(var(--primary-rgb), 0.3);  /* Noticeable */

/* Gradient (Premium Touch) */
background: linear-gradient(135deg,
  rgba(var(--primary-rgb), 0.03) 0%,
  transparent 100%);
```

#### **Metric Color Variants**

**Neutral/Info (Blue)** - Standard metrics:
```css
.metric-item.metric-shifts {
  background: rgba(var(--primary-rgb), 0.05);
  border-color: rgba(var(--primary-rgb), 0.1);
}

.metric-item.metric-shifts .metric-value {
  color: var(--primary-color);
}
```

**Warning (Orange/Yellow)** - Pending/attention items:
```css
.metric-item.metric-pending {
  background: rgba(255, 193, 7, 0.05);
  border-color: rgba(255, 193, 7, 0.2);
}

.metric-item.metric-pending .metric-value {
  color: #f59e0b;
}
```

**Success (Green)** - Coverage/completion:
```css
.metric-item.metric-coverage {
  background: rgba(16, 185, 129, 0.05);
  border-color: rgba(16, 185, 129, 0.2);
}

.metric-item.metric-coverage .metric-value {
  color: #10b981;
}
```

**Danger (Red)** - Errors/critical items:
```css
.metric-item.metric-danger {
  background: rgba(220, 38, 38, 0.05);
  border-color: rgba(220, 38, 38, 0.2);
}

.metric-item.metric-danger .metric-value {
  color: #dc2626;
}
```

---

## Component Patterns

### Calendar Header Pattern

**Structure:**
```html
<div data-ui-version="v2">
  <div class="calendar-header">
    <div class="header-main">
      <h2>
        <span class="calendar-icon">📅</span>
        [Calendar Title]
      </h2>
      <div class="calendar-controls">
        [Toggle/Actions]
      </div>
    </div>

    <div class="calendar-nav">
      <a class="btn btn-ghost" href="[prev]">
        <span class="nav-icon">←</span>
        <span class="nav-text">[Previous]</span>
      </a>
      <a class="btn btn-primary" href="[today]">
        <span class="nav-icon">📅</span>
        <span class="nav-text">Today</span>
      </a>
      <a class="btn btn-ghost" href="[next]">
        <span class="nav-text">[Next]</span>
        <span class="nav-icon">→</span>
      </a>
    </div>

    @if (Model.TotalItemCount > 0)
    {
      <div class="calendar-metrics">
        [Contextual Metrics]
      </div>
    }
  </div>
</div>
```

**Key Features:**
- **Soft Air**: Gradient background, backdrop blur, shimmer effect
- **Tactical Truth**: Contextual metrics showing operational data
- **Responsive**: Stacks on mobile, horizontal on desktop
- **RTL Compatible**: Icons and layout flip for Hebrew

### Metric Item Pattern

**Structure:**
```html
<div class="metric-item metric-[variant]">
  <span class="metric-icon">[Emoji Icon]</span>
  <div class="metric-content">
    <span class="metric-value">[Number]</span>
    <span class="metric-label">[Label]</span>
  </div>
</div>
```

**Variants:**
- `metric-shifts` - Blue, standard items
- `metric-pending` - Orange/yellow, attention needed
- `metric-coverage` - Green, success/completion

**Behavior:**
- Hover: `scale(1.05)` + enhanced shadow
- Icon: Bounce animation on entrance
- Conditional: Only show if count > 0 (usually)

### Button Patterns

**Primary Action:**
```html
<a class="btn btn-primary" href="[url]">
  <span class="nav-icon">📅</span>
  <span class="nav-text">Today</span>
</a>
```

**Secondary Action:**
```html
<a class="btn btn-ghost" href="[url]">
  <span class="nav-icon">←</span>
  <span class="nav-text">Previous</span>
</a>
```

**Danger Action:**
```html
<button class="btn btn-danger" type="submit">
  <span class="btn-icon">🗑️</span>
  <span class="btn-text">Delete</span>
</button>
```

---

## Responsive Design

### Breakpoint System

```css
/* Mobile First Approach */

/* Base styles: Mobile (0-479px) */
.element {
  padding: var(--spacing-sm);
  font-size: 0.875rem;
}

/* Small tablets and large phones (480px+) */
@media (min-width: 480px) {
  .element {
    padding: var(--spacing-md);
  }
}

/* Tablets (768px+) */
@media (min-width: 768px) {
  .element {
    padding: var(--spacing-lg);
    font-size: 1rem;
  }
}

/* Desktop (1024px+) */
@media (min-width: 1024px) {
  .element {
    padding: var(--spacing-xl);
  }
}

/* Large Desktop (1440px+) */
@media (min-width: 1440px) {
  .element {
    max-width: 1400px;
    margin: 0 auto;
  }
}
```

### Calendar Header Responsive Pattern

```css
/* Mobile: Stack everything */
@media (max-width: 768px) {
  [data-ui-version="v2"] .calendar-header {
    padding: var(--spacing-md);
  }

  [data-ui-version="v2"] .calendar-header h2 {
    font-size: 1.5rem; /* Smaller title */
  }

  [data-ui-version="v2"] .calendar-header .header-main {
    flex-direction: column; /* Stack title and controls */
    gap: var(--spacing-sm);
    align-items: flex-start;
  }

  [data-ui-version="v2"] .calendar-metrics {
    gap: var(--spacing-sm);
  }

  [data-ui-version="v2"] .metric-item {
    flex: 1 1 auto;
    min-width: calc(50% - var(--spacing-xs)); /* 2 per row */
  }
}

/* Very small mobile: Full width metrics */
@media (max-width: 480px) {
  [data-ui-version="v2"] .metric-item {
    min-width: 100%; /* 1 per row */
  }

  [data-ui-version="v2"] .calendar-nav .btn {
    flex: 1;
    justify-content: center;
  }
}
```

---

## RTL & Localization

### RTL Layout Support

The application supports both LTR (English) and RTL (Hebrew) layouts using the `[dir="rtl"]` attribute.

#### **Automatic Flipping**

Most layout properties flip automatically:
- `margin-left` ↔ `margin-right`
- `padding-left` ↔ `padding-right`
- `text-align: left` ↔ `text-align: right`
- `float: left` ↔ `float: right`

#### **Manual RTL Adjustments**

For gradients, animations, and absolute positioning:

```css
/* LTR: Left to right gradient */
.element {
  background: linear-gradient(135deg,
    rgba(var(--primary-rgb), 0.03),
    transparent);
}

/* RTL: Flip gradient angle */
[dir="rtl"] .element {
  background: linear-gradient(-135deg,
    rgba(var(--primary-rgb), 0.03),
    transparent);
}
```

```css
/* LTR: Shimmer left to right */
@keyframes shimmer {
  0% { left: -100%; }
  100% { left: 100%; }
}

/* RTL: Shimmer right to left */
[dir="rtl"] .element::before {
  animation: shimmerRtl 3s infinite;
}

@keyframes shimmerRtl {
  0% { right: -100%; left: auto; }
  100% { right: 100%; left: auto; }
}
```

### Localization Pattern

**Backend (C#):**
```csharp
// Inject IStringLocalizer
private readonly IStringLocalizer<SharedResources> _localizer;

// Use in code
var title = _localizer["Calendar_Shifts"].Value;
```

**Frontend (Razor):**
```html
<!-- Use @Localizer[] -->
<span class="metric-label">@Localizer["Calendar_Shifts"]</span>
```

**Resource Files:**
- `Resources/SharedResources.resx` - English (fallback)
- `Resources/SharedResources.he-IL.resx` - Hebrew

**Naming Convention:**
```
[Page/Section]_[Element]_[Variant]

Examples:
Calendar_Shifts          // Calendar page, Shifts label
Calendar_Pending         // Calendar page, Pending label
Dashboard_NextShift_On   // Dashboard, Next Shift, "on" preposition
MyTeam_TeamMembers       // MyTeam page, Team Members heading
```

---

## Dark Mode

### Dark Mode Strategy

ShiftManager uses the `[data-theme="dark"]` attribute for dark mode styling.

#### **Color Adjustments**

```css
/* Light mode (default) */
.element {
  background: rgba(var(--primary-rgb), 0.05);
  border-color: rgba(var(--primary-rgb), 0.1);
  box-shadow: var(--shadow-sm);
}

/* Dark mode overrides */
[data-theme="dark"] .element {
  background: rgba(var(--primary-rgb), 0.1); /* More opacity */
  border-color: rgba(var(--primary-rgb), 0.2); /* More visible */
  box-shadow: var(--shadow-md),
              inset 0 1px 0 rgba(255, 255, 255, 0.05); /* Inner highlight */
}
```

#### **Dark Mode Principles**

1. **Increase Transparency**: Use higher opacity for backgrounds
2. **Enhance Borders**: Make borders more visible (0.1 → 0.2)
3. **Add Depth**: Use inset highlights for dimension
4. **Soften Shadows**: Adjust shadow intensity
5. **Maintain Contrast**: Ensure text remains readable

#### **Example: Calendar Header Dark Mode**

```css
/* Light mode */
[data-ui-version="v2"] .calendar-header {
  background: linear-gradient(135deg,
    rgba(var(--primary-rgb), 0.03) 0%,
    transparent 100%);
  border-color: rgba(var(--primary-rgb), 0.1);
  box-shadow: var(--shadow-sm),
              inset 0 1px 0 rgba(255, 255, 255, 0.1);
}

/* Dark mode */
[data-theme="dark"] [data-ui-version="v2"] .calendar-header {
  background: linear-gradient(135deg,
    rgba(var(--primary-rgb), 0.1) 0%,
    transparent 100%);
  border-color: rgba(var(--primary-rgb), 0.2);
  box-shadow: var(--shadow-md),
              inset 0 1px 0 rgba(255, 255, 255, 0.05);
}

[data-theme="dark"] [data-ui-version="v2"] .calendar-header h2 {
  filter: drop-shadow(0 2px 4px rgba(0, 0, 0, 0.3)); /* Stronger shadow */
}
```

---

## Role-Based UI

### Display Rules

Different user roles see different UI elements based on their permissions.

#### **Role Hierarchy**
1. **Owner** - Full access, sees all metrics
2. **Director** - Management access, sees coverage metrics
3. **Manager** - Management access, sees coverage metrics
4. **Employee** - Standard access, sees personal metrics

#### **Pattern: Admin-Only Metrics**

```html
@* Show coverage % only to admins *@
@if (User.IsInRole("Owner") || User.IsInRole("Manager") || User.IsInRole("Director"))
{
  <div class="metric-item metric-coverage">
    <span class="metric-icon">✅</span>
    <div class="metric-content">
      <span class="metric-value">@Model.CoveragePercent%</span>
      <span class="metric-label">@Localizer["Calendar_Coverage"]</span>
    </div>
  </div>
}
```

#### **Pattern: Employee-Only Metrics**

```html
@* Show "My Items" only to non-admin users *@
@if (!User.IsInRole("Owner") && !User.IsInRole("Manager") && !User.IsInRole("Director") && Model.MyChoresCount > 0)
{
  <div class="metric-item metric-coverage">
    <span class="metric-icon">👤</span>
    <div class="metric-content">
      <span class="metric-value">@Model.MyChoresCount</span>
      <span class="metric-label">@Localizer["Calendar_MyItems"]</span>
    </div>
  </div>
}
```

#### **Backend Role Calculation**

```csharp
// Calculate different metrics based on role
if (User.IsInRole("Owner") || User.IsInRole("Manager") || User.IsInRole("Director"))
{
    // Admin: Calculate coverage percentage
    var shiftsWithStaffing = allItems
        .Where(i => i.Type == CalendarItemType.Shift && !string.IsNullOrEmpty(i.StaffingInfo))
        .ToList();

    if (shiftsWithStaffing.Any())
    {
        int totalSlots = 0, filledSlots = 0;
        foreach (var shift in shiftsWithStaffing)
        {
            var parts = shift.StaffingInfo.Split('/');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var filled) &&
                int.TryParse(parts[1], out var total))
            {
                totalSlots += total;
                filledSlots += filled;
            }
        }
        CoveragePercent = totalSlots > 0 ? Math.Round((double)filledSlots / totalSlots * 100, 1) : 100.0;
    }
}
else
{
    // Employee: Calculate personal item count
    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (int.TryParse(userIdClaim, out var currentUserId))
    {
        MyChoresCount = Chores.Count(c => c.UserId == currentUserId);
    }
}
```

---

## Code Patterns

### Data Attribute Scoping

All v2 UI enhancements use `[data-ui-version="v2"]` scoping to avoid conflicts with legacy styles.

#### **HTML Structure**
```html
<div data-ui-version="v2">
  <div class="calendar-header">
    <!-- v2 enhanced content -->
  </div>
</div>
```

#### **CSS Scoping**
```css
/* Only applies to v2 components */
[data-ui-version="v2"] .calendar-header {
  background: linear-gradient(135deg, rgba(var(--primary-rgb), 0.03), transparent);
  backdrop-filter: blur(8px);
  animation: slideDown 0.4s cubic-bezier(0.68, -0.55, 0.265, 1.55);
}

/* Legacy components remain unchanged */
.calendar-header {
  /* Old styles */
}
```

### Contextual Metrics Pattern

#### **Backend Calculation**

```csharp
// Step 1: Add properties to PageModel
public int VisibleShiftCount { get; set; }
public int VisibleChoreCount { get; set; }
public int TotalItemCount { get; set; }
public int PendingItemsCount { get; set; }

// Step 2: Calculate from existing data
private void CalculateHeaderMetrics()
{
    var allItems = Weeks.SelectMany(w => w).SelectMany(d => d.Items).ToList();

    VisibleShiftCount = allItems.Count(i => i.Type == CalendarItemType.Shift);
    VisibleChoreCount = allItems.Count(i => i.Type == CalendarItemType.Chore);
    TotalItemCount = allItems.Count;

    // Derive pending from staffing info
    PendingItemsCount = allItems
        .Where(i => i.Type == CalendarItemType.Shift && !string.IsNullOrEmpty(i.StaffingInfo))
        .Count(i => {
            var parts = i.StaffingInfo.Split('/');
            return parts.Length == 2 &&
                   int.TryParse(parts[0], out var filled) &&
                   int.TryParse(parts[1], out var total) &&
                   filled < total;
        });
}

// Step 3: Call after data loading
public async Task OnGetAsync()
{
    // ... load data into Weeks ...

    CalculateHeaderMetrics(); // After Weeks populated

    return Page();
}
```

#### **Frontend Display**

```html
@* Only show metrics if there are items *@
@if (Model.TotalItemCount > 0)
{
  <div class="calendar-metrics">
    @* Always show total count *@
    <div class="metric-item metric-shifts">
      <span class="metric-icon">📅</span>
      <div class="metric-content">
        <span class="metric-value">@Model.VisibleShiftCount</span>
        <span class="metric-label">@Localizer["Calendar_Shifts"]</span>
      </div>
    </div>

    @* Conditionally show non-zero counts *@
    @if (Model.PendingItemsCount > 0)
    {
      <div class="metric-item metric-pending">
        <span class="metric-icon">⏳</span>
        <div class="metric-content">
          <span class="metric-value">@Model.PendingItemsCount</span>
          <span class="metric-label">@Localizer["Calendar_Pending"]</span>
        </div>
      </div>
    }
  </div>
}
```

### Icon Usage Pattern

Use emoji icons consistently:

| Category | Icon | Usage |
|----------|------|-------|
| Calendar | 📅 | Calendar pages, date navigation |
| Shifts | 📅 | Shift metrics |
| Chores | 🧹 | Chore metrics |
| On-Duty | 🎯 | On-duty metrics |
| Pending | ⏳ | Pending/waiting items |
| Coverage | ✅ | Coverage percentage (admin) |
| Personal | 👤 | "My Items" (employee) |
| Hakam | 🎓 | Hakam type on-duty |
| Lead | ⭐ | Lead type on-duty |
| Success | ✅ | Completed/success states |
| Warning | ⚠️ | Warning states |
| Error | ❌ | Error states |
| Info | ℹ️ | Information |

---

## Examples

### Example 1: Premium Card with Soft Air

```html
<div data-ui-version="v2" class="dashboard-card">
  <div class="card-header">
    <h3 class="card-title">
      <span class="card-icon">📊</span>
      Team Performance
    </h3>
  </div>
  <div class="card-body">
    <div class="metrics-grid">
      <div class="metric-item metric-shifts">
        <span class="metric-icon">✅</span>
        <div class="metric-content">
          <span class="metric-value">92%</span>
          <span class="metric-label">Coverage</span>
        </div>
      </div>
    </div>
  </div>
</div>
```

```css
[data-ui-version="v2"] .dashboard-card {
  background: linear-gradient(135deg,
    rgba(var(--primary-rgb), 0.03) 0%,
    transparent 100%);
  border: 1px solid rgba(var(--primary-rgb), 0.1);
  border-radius: var(--radius-lg);
  padding: var(--spacing-lg);
  box-shadow: var(--shadow-sm);
  backdrop-filter: blur(8px);
  transition: all 0.3s var(--ease-standard);
  position: relative;
  overflow: hidden;
}

[data-ui-version="v2"] .dashboard-card::before {
  content: '';
  position: absolute;
  top: 0;
  left: -100%;
  width: 100%;
  height: 100%;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.1), transparent);
  animation: shimmer 3s infinite;
  pointer-events: none;
}

[data-ui-version="v2"] .dashboard-card:hover {
  transform: translateY(-4px);
  box-shadow: var(--shadow-lg);
  border-color: rgba(var(--primary-rgb), 0.2);
}
```

### Example 2: Staggered Entrance Animation

```html
<div class="feature-list">
  <div class="feature-item" style="animation-delay: 0.1s">Feature 1</div>
  <div class="feature-item" style="animation-delay: 0.2s">Feature 2</div>
  <div class="feature-item" style="animation-delay: 0.3s">Feature 3</div>
  <div class="feature-item" style="animation-delay: 0.4s">Feature 4</div>
</div>
```

```css
.feature-item {
  opacity: 0;
  animation: fadeInUp 0.5s ease-out both;
}

@keyframes fadeInUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
```

### Example 3: Role-Based Metric Display

```csharp
// PageModel.cs
public class DashboardModel : PageModel
{
    public int TeamSize { get; set; }
    public int ActiveShifts { get; set; }
    public double CoveragePercent { get; set; }
    public int MyUpcomingShifts { get; set; }

    public async Task OnGetAsync()
    {
        if (User.IsInRole("Owner") || User.IsInRole("Manager"))
        {
            // Admin metrics
            TeamSize = await _db.Users.Where(u => u.CompanyId == companyId).CountAsync();
            ActiveShifts = await _db.ShiftInstances
                .Where(si => si.WorkDate >= DateOnly.FromDateTime(DateTime.Today))
                .CountAsync();
            CoveragePercent = await CalculateCoverageAsync();
        }
        else
        {
            // Employee metrics
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            MyUpcomingShifts = await _db.ShiftAssignments
                .Where(sa => sa.UserId == userId && sa.ShiftInstance.WorkDate >= DateOnly.FromDateTime(DateTime.Today))
                .CountAsync();
        }
    }
}
```

```html
<!-- Dashboard.cshtml -->
<div class="dashboard-metrics">
  @if (User.IsInRole("Owner") || User.IsInRole("Manager"))
  {
    <!-- Admin view -->
    <div class="metric-item metric-shifts">
      <span class="metric-icon">👥</span>
      <div class="metric-content">
        <span class="metric-value">@Model.TeamSize</span>
        <span class="metric-label">Team Members</span>
      </div>
    </div>

    <div class="metric-item metric-coverage">
      <span class="metric-icon">✅</span>
      <div class="metric-content">
        <span class="metric-value">@Model.CoveragePercent%</span>
        <span class="metric-label">Coverage</span>
      </div>
    </div>
  }
  else
  {
    <!-- Employee view -->
    <div class="metric-item metric-shifts">
      <span class="metric-icon">📅</span>
      <div class="metric-content">
        <span class="metric-value">@Model.MyUpcomingShifts</span>
        <span class="metric-label">My Upcoming Shifts</span>
      </div>
    </div>
  }
</div>
```

---

## Quick Reference Checklist

When creating a new v2 component, ensure:

- [ ] **Scoping**: Wrapped in `<div data-ui-version="v2">`
- [ ] **Soft Air**: Gradient background, blur, subtle animations
- [ ] **Tactical Truth**: Displays actionable metrics/data
- [ ] **Design Tokens**: Uses CSS custom properties (--spacing-*, --radius-*, etc.)
- [ ] **Animations**: Has entrance animation (slideDown, fadeIn)
- [ ] **Hover**: Lift + scale on interaction
- [ ] **RTL**: Gradients and animations have RTL variants
- [ ] **Dark Mode**: Has `[data-theme="dark"]` overrides
- [ ] **Responsive**: Mobile breakpoints defined
- [ ] **Role-Based**: Admin vs Employee views implemented
- [ ] **Localization**: All text uses `@Localizer["Key"]`
- [ ] **Accessibility**: Proper semantic HTML, ARIA labels
- [ ] **Performance**: Uses transform/opacity for animations

---

## File Structure

```
wwwroot/css/
  └── site.css
      ├── Design tokens (:root)
      ├── Phase 1: Core v2 styles
      ├── Phase 2: Button enhancements
      ├── Phase 3: Card patterns
      ├── Phase 4: Form controls
      ├── Phase 5: Navigation
      ├── Phase 6: MyTeam v2
      └── Phase 7: Calendar headers ← Latest

Resources/
  ├── SharedResources.resx          (English)
  └── SharedResources.he-IL.resx    (Hebrew)

Pages/
  ├── Calendar/
  │   ├── Month.cshtml + .cs
  │   ├── Week.cshtml + .cs
  │   └── Day.cshtml + .cs
  ├── Public/
  │   ├── Chores.cshtml + .cs
  │   └── OnDuty.cshtml + .cs
  └── Chores/
      └── Calendar.cshtml + .cs
```

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 2.0 | January 2026 | Phase 7: Calendar header contextual metrics |
| 1.6 | December 2025 | Phase 6: MyTeam v2 enhancements |
| 1.0 | November 2025 | Initial "Soft Air, Tactical Truth" philosophy |

---

## Conclusion

The "Soft Air, Tactical Truth" design philosophy creates a user experience that is both aesthetically pleasing and operationally effective. By balancing gentle visual polish with clear, actionable data, ShiftManager provides users with a professional tool they enjoy using.

When in doubt:
- **Soft Air**: Make it feel smooth and premium
- **Tactical Truth**: Make it useful and clear
- **Together**: Make it both beautiful and functional

Remember: **Every pixel should either delight the user or inform them. Ideally both.**
