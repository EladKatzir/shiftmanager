# 08 - UI/UX Architecture

**Document Status:** Genesis Documentation - Complete System Architecture
**Last Updated:** 2025
**Part of:** Phase 3 - User-Facing Systems Documentation

---

## Table of Contents

1. [Overview](#overview)
2. [Frontend Technology Stack](#frontend-technology-stack)
3. [Page Organization](#page-organization)
4. [CSS Design System](#css-design-system)
5. [JavaScript Architecture](#javascript-architecture)
6. [Layout & App Shell](#layout--app-shell)
7. [View Components](#view-components)
8. [Styling Patterns](#styling-patterns)
9. [Interactivity Patterns](#interactivity-patterns)
10. [Dark Mode Implementation](#dark-mode-implementation)
11. [RTL Support](#rtl-support)
12. [Command Palette](#command-palette)
13. [Configuration UI Enhancements](#configuration-ui-enhancements)
14. [Easter Egg: Shift Swap Game](#easter-egg-shift-swap-game)
15. [Performance Considerations](#performance-considerations)
16. [No-Build Philosophy](#no-build-philosophy)
17. [Reconstruction Notes](#reconstruction-notes)

---

## Overview

ShiftManager's frontend architecture is built on **ASP.NET Core Razor Pages** with a **zero-build, vanilla JavaScript** approach. The application delivers a modern, responsive UI without any JavaScript frameworks, build tools, or node_modules dependencies.

### Key Statistics

- **66 Razor Pages** organized across 11 functional folders
- **4,698 lines of CSS** (3,800 main + 123 RTL + 775 game)
- **4,248 lines of vanilla JavaScript** across 6 files
- **3 View Components** for reusable UI elements
- **0 npm packages** - no build process required
- **111 MB deployment** - includes everything (runtime, assets, database)

### Architectural Principles

1. **Server-Side Rendering**: Razor Pages generate HTML on the server
2. **Progressive Enhancement**: Core functionality works without JavaScript
3. **Vanilla JavaScript**: No frameworks (React, Vue, Angular) - pure ES6+
4. **CSS Custom Properties**: Design system built on CSS variables (dark mode support)
5. **No Build Process**: No webpack, Vite, or Rollup - files are served as-is
6. **Air-Gapped Ready**: All assets bundled, no CDN dependencies
7. **Responsive Design**: Mobile-first, works on all screen sizes
8. **Accessibility First**: Semantic HTML, ARIA labels, keyboard navigation

---

## Frontend Technology Stack

### Core Technologies

| Technology | Version | Purpose |
|-----------|---------|---------|
| **ASP.NET Core Razor Pages** | .NET 8.0 | Server-side rendering, page-based routing |
| **HTML5** | - | Semantic markup |
| **CSS3** | - | Styling with custom properties (CSS variables) |
| **Vanilla JavaScript** | ES6+ | Client-side interactivity |
| **Emoji Icons** | Unicode | No icon library dependency |

### What's NOT Used (Intentional Omissions)

❌ **No JavaScript Frameworks**: No React, Vue, Angular, Svelte
❌ **No UI Libraries**: No Bootstrap, Tailwind, Material-UI
❌ **No Build Tools**: No webpack, Vite, Rollup, Parcel
❌ **No Package Manager**: No npm, yarn, pnpm
❌ **No Icon Libraries**: No Font Awesome, Material Icons
❌ **No CSS Preprocessors**: No Sass, Less, Stylus (native CSS is enough)
❌ **No CDN Dependencies**: Everything self-hosted for air-gapped deployment

### Why This Approach?

1. **Air-Gapped Deployment**: No external dependencies means the application works completely offline
2. **Simplicity**: Fewer moving parts = easier to debug, maintain, and reconstruct
3. **Performance**: Native browser APIs are faster than framework abstractions
4. **Portability**: Pure standards-based code works everywhere
5. **Long-Term Stability**: No framework churn, breaking changes, or deprecation cycles

---

## Page Organization

### Folder Structure (11 Functional Areas)

```
Pages/
├── Shared/
│   ├── _Layout.cshtml                 # Main app shell (sidebar + header + content)
│   ├── _LocalizationScript.cshtml     # JavaScript localization helper
│   ├── Components/                    # View Components (3 total)
│   │   ├── UnreadNotificationCount/
│   │   ├── LanguageToggle/
│   │   └── ShowMyItemsToggle/
│
├── Auth/                              # Authentication (4 pages)
│   ├── Login.cshtml                   # Login page
│   ├── Signup.cshtml                  # User registration
│   ├── Logout.cshtml                  # Logout handler
│   ├── GriffinCallback.cshtml         # Griffin ADFS callback
│   └── ForgotPassword.cshtml          # Password reset
│
├── Home/                              # Landing page (1 page)
│   └── Index.cshtml                   # Dashboard/home
│
├── My/                                # Employee self-service (7 pages)
│   ├── Index.cshtml                   # Personal timeline/overview
│   ├── Profile.cshtml                 # Edit own profile
│   ├── Settings.cshtml                # User settings
│   ├── Requests.cshtml                # My requests (time-off, swaps)
│   ├── NotificationCenter.cshtml      # Notification inbox
│   ├── ApiKeys.cshtml                 # Personal API keys
│   └── _TimelineItem.cshtml           # Timeline item partial
│
├── Schedule/                          # Schedule viewing (1 page)
│   └── Index.cshtml                   # Schedule entry point
│
├── Calendar/                          # Calendar views (4 pages)
│   ├── Month.cshtml                   # Month calendar grid
│   ├── Week.cshtml                    # Week calendar view
│   ├── Day.cshtml                     # Day detail view
│   └── Table.cshtml                   # Table view (shift management)
│
├── MyTeam/                            # Team calendars (1 page)
│   └── Index.cshtml                   # Custom calendar management
│
├── Requests/                          # Request management (4 pages)
│   ├── Index.cshtml                   # All requests overview
│   ├── TimeOff/
│   │   └── Create.cshtml              # Time-off request form
│   └── Swaps/
│       └── Create.cshtml              # Shift swap request form
│
├── Assignments/                       # Shift assignment (1 page)
│   └── Manage.cshtml                  # Assign shifts UI
│
├── Public/                            # Public views (3 pages)
│   ├── Chores.cshtml                  # Chore calendar (all users)
│   ├── OnDuty.cshtml                  # On-duty calendar (all users)
│   └── Feedback.cshtml                # User feedback form
│
├── Chores/                            # Chore management (1 page)
│   └── Calendar.cshtml                # Chore assignment calendar
│
├── Admin/                             # Administrative pages (9 pages)
│   ├── Users.cshtml                   # User management
│   ├── EditProfile.cshtml             # Edit user profiles
│   ├── Companies.cshtml               # Company management (Owner only)
│   ├── Directors.cshtml               # Director assignment (Owner only)
│   ├── ShiftTypes.cshtml              # Shift type configuration
│   ├── Config.cshtml                  # System configuration
│   ├── Analytics.cshtml               # Reports and analytics
│   └── AuditLog.cshtml                # Audit log viewer
│
├── Owner/                             # Owner-only pages (8 pages)
│   ├── Index.cshtml                   # Owner admin panel
│   ├── EmailConfig.cshtml             # Email configuration
│   ├── GriffinConfig.cshtml           # Griffin ADFS config
│   ├── GameConfig.cshtml              # Game feature toggle
│   ├── FeatureFlags.cshtml            # Feature flag management
│   ├── SystemHealth.cshtml            # System diagnostics
│   ├── Backup.cshtml                  # Database backup
│   └── DatabaseConsole.cshtml         # SQL console (diagnostic)
│
├── Director/                          # Director-only pages (3 pages)
│   ├── NotificationHub.cshtml         # Cross-company notifications
│   ├── CompanyFilter.cshtml           # Company switcher
│   └── ViewAsMode.cshtml              # View-as debugging tool
│
├── Game/                              # Gamification (1 page)
│   └── Leaderboard.cshtml             # Game leaderboard
│
├── Api/                               # Internal API endpoints (9 pages)
│   ├── SessionStatus.cshtml           # Session health check
│   ├── Game/
│   │   ├── SaveScore.cshtml           # Save game score
│   │   ├── GetLeaderboard.cshtml      # Retrieve leaderboard
│   │   ├── GetConfiguration.cshtml    # Game config
│   │   └── GetLocalization.cshtml     # Game translations
│   └── Calendar/
│       ├── QuickAddChore.cshtml       # Quick chore creation
│       ├── QuickAddOnDuty.cshtml      # Quick on-duty creation
│       ├── DeleteChore.cshtml         # Chore deletion
│       └── DeleteOnDuty.cshtml        # On-duty deletion
│
├── Index.cshtml                       # Root landing page
├── Error.cshtml                       # Global error handler
├── AccessDenied.cshtml                # 403 page
├── Diagnostic.cshtml                  # System diagnostic page
├── _ViewImports.cshtml                # Global Razor imports
└── _ViewStart.cshtml                  # Default layout

TOTAL: 66 Razor Pages
```

### Page Naming Conventions

- **Index.cshtml**: Default page for a folder (e.g., `/Home/Index` → `/Home`)
- **PascalCase**: All page names use PascalCase (e.g., `NotificationCenter.cshtml`)
- **Folder organization**: Pages grouped by functional area (Auth, My, Admin, Owner)

---

## CSS Design System

### File Structure (3 CSS Files, 4,698 lines total)

```
wwwroot/css/
├── site.css                  # 3,800 lines - Main stylesheet
├── rtl.css                   # 123 lines - RTL overrides for Hebrew
└── shift-swap-game.css       # 775 lines - Easter egg game styles
```

### Design Tokens (CSS Custom Properties)

ShiftManager uses **CSS custom properties** (CSS variables) for theming. All color, spacing, and shadow values are defined as tokens.

**site.css:1-75** - Design System Tokens:

```css
:root {
  /* Background & Surfaces */
  --bg: #f5f7fb;
  --surface: #ffffff;
  --surface-soft: #f0f2f8;
  --surface-elevated: #ffffff;
  --surface-strong: #0f172a;

  /* Text Colors */
  --text: #111318;
  --muted: #6b7280;

  /* Brand & Interactive */
  --primary: #2563eb;
  --primary-rgb: 37, 99, 235;
  --primary-soft: #e0ecff;

  /* Status Colors */
  --danger: #dc2626;
  --danger-text: #ffffff;
  --success: #16a34a;
  --success-text: #ffffff;
  --warning: #f59e0b;
  --warning-text: #ffffff;

  /* UI Elements */
  --focus: #ff9800;
  --border: #e2e8f0;

  /* Shadows */
  --shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.05);
  --shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);
  --shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05);
  --shadow-xl: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04);
}
```

### Dark Mode Theme

Dark mode is implemented via **attribute selectors** on the `<html>` element: `<html data-theme="dark">`.

**site.css:41-75** - Dark Mode Overrides:

```css
:root[data-theme="dark"] {
  /* Background & Surfaces */
  --bg: #020617;
  --surface: #0b1120;
  --surface-soft: #0f1629;
  --surface-elevated: #0f1629;
  --surface-strong: #1e293b;

  /* Text Colors */
  --text: #e5e7eb;
  --muted: #9ca3af;

  /* Brand & Interactive */
  --primary: #60a5fa;
  --primary-rgb: 96, 165, 250;
  --primary-soft: #1d2736;

  /* Status Colors (lighter for dark backgrounds) */
  --danger: #f87171;
  --danger-text: #1a1a1a;
  --success: #4ade80;
  --success-text: #1a1a1a;
  --warning: #facc15;
  --warning-text: #1a1a1a;

  /* UI Elements */
  --focus: #ffd54f;
  --border: #1f2937;

  /* Shadows (more subtle in dark mode) */
  --shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.3);
  --shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.4), 0 2px 4px -1px rgba(0, 0, 0, 0.3);
  --shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.4), 0 4px 6px -2px rgba(0, 0, 0, 0.3);
  --shadow-xl: 0 20px 25px -5px rgba(0, 0, 0, 0.5), 0 10px 10px -5px rgba(0, 0, 0, 0.4);
}
```

**Why This Approach:**
- **Single source of truth**: Change `--primary` and the entire app updates
- **Automatic dark mode**: No duplicate CSS rules for dark mode
- **Browser support**: CSS custom properties supported in all modern browsers (IE11 not supported)

### Typography Scale

**site.css:81-110** - Typography Classes:

```css
.text-display {
  font-size: 1.75rem;    /* ~28px */
  font-weight: 600;
  line-height: 1.2;
  letter-spacing: -0.02em;
}

.text-title {
  font-size: 1.25rem;    /* ~20px */
  font-weight: 600;
  line-height: 1.3;
}

.text-body {
  font-size: 0.9375rem;  /* ~15px */
  font-weight: 400;
  line-height: 1.5;
}

.text-subtle {
  font-size: 0.875rem;   /* ~14px */
  color: var(--muted);
  line-height: 1.5;
}

.text-caption {
  font-size: 0.75rem;    /* ~12px */
  color: var(--muted);
  line-height: 1.4;
}
```

### Component Styles

ShiftManager's CSS follows a **component-based architecture** with scoped class names:

```css
/* App Shell */
.app-shell {}
.app-sidebar {}
.app-sidebar-brand {}
.app-sidebar-nav {}
.app-sidebar-nav-item {}
.app-sidebar-footer {}
.app-main {}
.app-header {}
.app-content {}

/* Buttons */
.btn {}
.btn-primary {}
.btn-secondary {}
.btn-danger {}
.action-btn {}

/* Cards */
.card {}
.card-header {}
.card-body {}
.card-footer {}

/* Modals */
.modal {}
.modal-backdrop {}
.modal-content {}
.modal-header {}
.modal-body {}
.modal-footer {}

/* Forms */
.form-group {}
.form-label {}
.form-input {}
.form-select {}
.form-error {}

/* Calendar */
.calendar-grid {}
.calendar-day {}
.calendar-event {}
.shift-card {}
.shift-badge {}

/* Command Palette */
.command-palette {}
.command-palette-backdrop {}
.command-palette-content {}
.command-palette-search {}
.command-palette-results {}
```

### Spacing Scale

ShiftManager uses **rem-based spacing** for consistency:

```css
/* Spacing tokens (not explicitly defined as variables, but used consistently) */
0.25rem  /* 4px */
0.5rem   /* 8px */
0.75rem  /* 12px */
1rem     /* 16px */
1.5rem   /* 24px */
2rem     /* 32px */
3rem     /* 48px */
4rem     /* 64px */
```

---

## JavaScript Architecture

### File Structure (6 Files, 4,248 lines total)

```
wwwroot/js/
├── site.js                    # 1,113 lines - Core app logic
├── myteam.js                  # 660 lines - Team calendar management
├── shift-swap-game.js         # 1,376 lines - Easter egg game
├── calendar-inline-edit.js    # 450 lines - Calendar interaction
├── session-check.js           # 614 lines - Session validation
└── hebrew-audit.js            # 35 lines - RTL diagnostics
```

### Module Organization

ShiftManager uses **vanilla JavaScript modules** without a build system. Each file is a self-contained module with:

1. **No imports/exports**: Files don't use ES6 modules (for IE11 compatibility)
2. **Global namespace**: Functions attached to `window` object when needed
3. **IIFE pattern**: Code wrapped in immediately-invoked function expressions when isolation needed
4. **Event delegation**: Listeners attached to document/parent elements
5. **DOM-ready pattern**: Code runs after `DOMContentLoaded` event

### site.js (Core Application Logic)

**File**: `wwwroot/js/site.js` (1,113 lines)

**Purpose**: Core application functionality including dark mode, command palette, and easter egg game trigger.

**Key Features**:

1. **Dark Mode Toggle with System Preference**

```javascript
// Dark mode toggle with system preference support
document.addEventListener('DOMContentLoaded', function() {
  const root = document.documentElement;
  const saved = localStorage.getItem('theme');

  // Apply saved theme or detect system preference
  if (saved) {
    root.setAttribute('data-theme', saved);
  } else {
    // Detect system preference
    const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    const systemTheme = prefersDark ? 'dark' : 'light';
    root.setAttribute('data-theme', systemTheme);
  }

  // Listen for system theme changes
  if (window.matchMedia) {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function(e) {
      // Only auto-switch if user hasn't manually set a preference
      if (!localStorage.getItem('theme')) {
        const newTheme = e.matches ? 'dark' : 'light';
        root.setAttribute('data-theme', newTheme);
      }
    });
  }

  // Set up toggle button
  const btn = document.getElementById('themeToggle');
  if (btn) {
    btn.addEventListener('click', function(e) {
      e.preventDefault();
      const current = root.getAttribute('data-theme');
      const newTheme = current === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-theme', newTheme);
      localStorage.setItem('theme', newTheme);
    });
  }
});
```

**Dark Mode Features**:
- Respects `prefers-color-scheme` media query
- Manual override stored in `localStorage`
- Automatic system theme sync (unless manually overridden)
- Instant theme switching (no page reload)

2. **Easter Egg: Shift Swap Game Trigger**

```javascript
// Easter egg: Shift Swap game (Ctrl+Click on .brand)
document.addEventListener('click', async function(e) {
  const brandElement = e.target.closest('.brand');
  if (!brandElement) return;

  // Only trigger game if Ctrl/Cmd key is pressed
  if (e.ctrlKey || e.metaKey) {
    e.preventDefault();
    e.stopPropagation();

    // Dynamically load game assets if not already loaded
    if (!window.ShiftSwapGame) {
      // Load CSS
      if (!document.querySelector('link[href*="shift-swap-game.css"]')) {
        const cssLink = document.createElement('link');
        cssLink.rel = 'stylesheet';
        cssLink.href = '/css/shift-swap-game.css?v=' + Date.now();
        document.head.appendChild(cssLink);
      }

      // Load JavaScript
      const script = document.createElement('script');
      script.src = '/js/shift-swap-game.js?v=' + Date.now();
      document.head.appendChild(script);

      // Wait for script to load
      await new Promise((resolve) => {
        script.onload = resolve;
        script.onerror = () => {
          console.error('Failed to load Shift Swap game script');
          resolve();
        };
      });
    }

    // Open the Shift Swap game
    if (window.ShiftSwapGame) {
      window.ShiftSwapGame.open();
    }
  }
});
```

**Easter Egg Design**:
- **Trigger**: Ctrl+click (or Cmd+click on Mac) on brand logo in sidebar
- **Lazy loading**: Game assets only loaded when activated (reduces initial page load)
- **Non-blocking**: Game failure doesn't break the main app

3. **Command Palette (Ctrl+K)**

```javascript
// Command Palette - Ctrl+K to open
document.addEventListener('keydown', function(e) {
  // Ctrl+K or Cmd+K to open command palette
  if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
    e.preventDefault();
    openCommandPalette();
  }

  // Escape to close
  if (e.key === 'Escape') {
    closeCommandPalette();
  }
});

function openCommandPalette() {
  const palette = document.getElementById('commandPalette');
  const input = document.getElementById('commandPaletteInput');

  palette.style.display = 'flex';
  input.focus();
  loadCommandOptions();
}

function closeCommandPalette() {
  const palette = document.getElementById('commandPalette');
  palette.style.display = 'none';
  document.getElementById('commandPaletteInput').value = '';
  document.getElementById('commandPaletteResults').innerHTML = '';
}

function loadCommandOptions() {
  const commands = [
    { icon: '🏠', label: 'Home', url: '/Home/Index', keywords: ['home', 'dashboard'] },
    { icon: '📅', label: 'Schedule', url: '/Schedule/Index', keywords: ['schedule', 'calendar', 'shifts'] },
    { icon: '📝', label: 'Requests', url: '/Requests/Index', keywords: ['requests', 'time off', 'swap'] },
    { icon: '👥', label: 'People', url: '/Admin/Users', keywords: ['users', 'people', 'team'] },
    { icon: '⚙️', label: 'Settings', url: '/Admin/Config', keywords: ['settings', 'config', 'configuration'] },
    { icon: '👤', label: 'My Profile', url: '/My/Profile', keywords: ['profile', 'me', 'account'] },
    { icon: '🔔', label: 'Notifications', url: '/My/NotificationCenter', keywords: ['notifications', 'alerts'] },
    // ... more commands
  ];

  renderCommandResults(commands);
}

// Search/filter commands as user types
document.getElementById('commandPaletteInput').addEventListener('input', function(e) {
  const query = e.target.value.toLowerCase();
  const commands = getAllCommands();

  const filtered = commands.filter(cmd =>
    cmd.label.toLowerCase().includes(query) ||
    cmd.keywords.some(k => k.includes(query))
  );

  renderCommandResults(filtered);
});
```

**Command Palette Features**:
- **Fuzzy search**: Matches label or keywords
- **Keyboard navigation**: Arrow keys to navigate, Enter to select
- **Context-aware**: Available commands based on user role
- **Fast access**: No mouse required

---

### myteam.js (Team Calendar Management)

**File**: `wwwroot/js/myteam.js` (660 lines)

**Purpose**: Manage custom team calendars (create, rename, delete, add/remove members, week view).

**Architecture**:

```javascript
// State management (no framework, just plain variables)
let currentCalendarId = null;
let currentWeekStart = null;
let calendars = [];
let currentMembers = [];
let availableUsers = [];
let selectedMemberIds = new Set();

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    initializeWeek();
    loadCalendars();
    setupEventListeners();
});

// Event listeners
function setupEventListeners() {
    document.getElementById('btnPrevWeek').addEventListener('click', () => navigateWeek(-7));
    document.getElementById('btnNextWeek').addEventListener('click', () => navigateWeek(7));
    document.getElementById('btnThisWeek').addEventListener('click', () => {
        currentWeekStart = getThisWeeksSunday();
        loadWeekView();
    });
    // ... more listeners
}

// API calls (using fetch)
async function loadCalendars() {
    try {
        const response = await fetch('/api/team-calendars', {
            credentials: 'same-origin'  // Include auth cookies
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        calendars = await response.json();
        renderCalendarList();
    } catch (error) {
        console.error('Failed to load calendars:', error);
        showError('Failed to load calendars');
    }
}

// DOM manipulation (vanilla JS, no jQuery)
function renderCalendarList() {
    const container = document.getElementById('calendarList');
    container.innerHTML = '';

    calendars.forEach(calendar => {
        const item = document.createElement('div');
        item.className = 'calendar-list-item';
        item.dataset.id = calendar.id;
        item.innerHTML = `
            <span class="calendar-icon">📆</span>
            <span class="calendar-name">${escapeHtml(calendar.name)}</span>
            <span class="calendar-member-count">${calendar.memberCount} members</span>
        `;
        item.addEventListener('click', () => selectCalendar(calendar.id));
        container.appendChild(item);
    });
}

// Utility functions
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatDate(date) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
}
```

**Key Patterns**:
1. **State management**: Plain JavaScript variables (no Redux, Vuex, etc.)
2. **API calls**: Native `fetch` API with `credentials: 'same-origin'` for cookie auth
3. **DOM manipulation**: `document.createElement`, `appendChild`, `innerHTML`
4. **XSS protection**: `escapeHtml()` helper for user-generated content
5. **Error handling**: Try-catch blocks with user-friendly messages

---

### session-check.js (Session Validation)

**File**: `wwwroot/js/session-check.js` (614 lines)

**Purpose**: Periodically check session validity and redirect to login if expired.

**Architecture**:

```javascript
// Session validation - Check every 60 seconds
(function() {
  const CHECK_INTERVAL = 60 * 1000; // 60 seconds
  const SESSION_ENDPOINT = '/Api/SessionStatus';

  let intervalId = null;

  function checkSession() {
    fetch(SESSION_ENDPOINT, {
      method: 'GET',
      credentials: 'same-origin',
      headers: {
        'Accept': 'application/json'
      }
    })
    .then(response => {
      if (response.status === 401) {
        // Session expired - redirect to login
        console.log('Session expired, redirecting to login...');
        window.location.href = '/Auth/Login?returnUrl=' + encodeURIComponent(window.location.pathname);
      } else if (response.ok) {
        // Session valid
        console.log('Session valid');
      } else {
        console.warn('Session check returned unexpected status:', response.status);
      }
    })
    .catch(error => {
      // Network error - don't redirect (could be temporary)
      console.error('Session check failed:', error);
    });
  }

  // Start checking when DOM is ready
  document.addEventListener('DOMContentLoaded', function() {
    // Check immediately on load
    checkSession();

    // Then check every 60 seconds
    intervalId = setInterval(checkSession, CHECK_INTERVAL);

    // Stop checking when page is hidden (browser tab backgrounded)
    document.addEventListener('visibilitychange', function() {
      if (document.hidden) {
        clearInterval(intervalId);
      } else {
        checkSession();
        intervalId = setInterval(checkSession, CHECK_INTERVAL);
      }
    });
  });
})();
```

**Session Check Features**:
- **Periodic validation**: Check every 60 seconds
- **Automatic redirect**: Navigate to login if session expired
- **Network-aware**: Don't redirect on network errors (temporary failures)
- **Background-aware**: Pause checks when browser tab is hidden (save battery)
- **Return URL**: Preserve current page for post-login redirect

---

## Layout & App Shell

### _Layout.cshtml (Main App Shell)

**File**: `Pages/Shared/_Layout.cshtml` (318 lines)

The layout file defines the **app shell** architecture with sidebar navigation, header, and content area.

**Structure**:

```html
<!DOCTYPE html>
<html lang="@lang" dir="@direction" data-theme="light">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>@ViewData["Title"]</title>

    <!-- CSS -->
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
    @if (isHebrew) {
        <link rel="stylesheet" href="~/css/rtl.css" asp-append-version="true" />
    }

    <!-- JavaScript (deferred) -->
    <script defer src="~/js/site.js"></script>
    @if (User?.Identity?.IsAuthenticated ?? false) {
        <script defer src="~/js/session-check.js" asp-append-version="true"></script>
    }
</head>
<body>
    <div class="app-shell">
        <!-- Sidebar Navigation -->
        <aside class="app-sidebar">
            <div class="app-sidebar-brand brand">
                <span class="app-sidebar-brand-icon">📊</span>
                <span>@Localizer["ShiftManager"]</span>
            </div>

            <nav class="app-sidebar-nav">
                <!-- Navigation items (role-based) -->
            </nav>

            <div class="app-sidebar-footer">
                <!-- User avatar and Ctrl+K hint -->
            </div>
        </aside>

        <!-- Main Content Area -->
        <div class="app-main">
            <!-- Header -->
            <header class="app-header">
                <div class="app-header-left">
                    <h1 class="page-title">@ViewData["Title"]</h1>
                </div>

                <div class="app-header-right">
                    <!-- Notifications, Language Toggle, Theme Toggle, Logout -->
                </div>
            </header>

            <!-- Main Content -->
            <main class="app-content">
                @RenderBody()
            </main>
        </div>
    </div>

    <!-- Command Palette (hidden by default) -->
    <div id="commandPalette" class="command-palette" style="display: none;">
        <!-- Palette content -->
    </div>
</body>
</html>
```

**App Shell Features**:

1. **Sidebar Navigation**: Persistent left sidebar with role-based menu items
2. **Fixed Header**: Page title, notifications, actions
3. **Scrollable Content**: Main content area with independent scrolling
4. **Command Palette**: Hidden overlay activated by Ctrl+K
5. **Role-Based UI**: Navigation items change based on user role

**Role-Based Navigation**:

```csharp
@if (isAdmin)
{
    <!-- Admin/Manager/Director Navigation -->
    <a href="/Home/Index" class="app-sidebar-nav-item">🏠 Home</a>
    <a href="/Calendar/Month" class="app-sidebar-nav-item">📅 Schedule</a>
    <a href="/Requests/Index" class="app-sidebar-nav-item">📝 Requests</a>
    <a href="/Admin/Analytics" class="app-sidebar-nav-item">📊 Analytics</a>

    @if (User.IsInRole("Manager")) {
        <a href="/MyTeam/Index" class="app-sidebar-nav-item">👥 My Team</a>
    } else {
        <a href="/Admin/Users" class="app-sidebar-nav-item">👥 People</a>
    }
}
else
{
    <!-- Employee/Trainee Navigation -->
    <a href="/Home/Index" class="app-sidebar-nav-item">🏠 Home</a>
    <a href="/Calendar/Month" class="app-sidebar-nav-item">📅 Schedule</a>
    <a href="/My/Requests" class="app-sidebar-nav-item">📝 Requests</a>
    <a href="/MyTeam/Index" class="app-sidebar-nav-item">👥 My Team</a>
    <a href="/My/Profile" class="app-sidebar-nav-item">👤 My Profile</a>
}
```

**Security Headers** (inline script):

```html
<script>
// Offline deployment diagnostic: Verify sidebar CSS loads correctly
(function() {
    window.addEventListener('DOMContentLoaded', function() {
        var sidebar = document.querySelector('.app-sidebar');
        if (sidebar) {
            var computed = window.getComputedStyle(sidebar);
            if (computed.display !== 'flex') {
                console.error('[OFFLINE ERROR] Sidebar CSS not loaded!');
                console.error('Expected display: flex, Got:', computed.display);
                console.error('Check: 1) UNBLOCK_FILES.bat run? 2) Hard refresh browser (Ctrl+F5)');

                // Show visual warning banner
                var warning = document.createElement('div');
                warning.style.cssText = 'position:fixed;top:0;left:0;right:0;background:#dc2626;color:white;padding:12px;text-align:center;z-index:9999;font-family:system-ui;font-size:14px';
                warning.textContent = '⚠️ CSS Loading Issue - Press Ctrl+F5 to refresh. If issue persists, run UNBLOCK_FILES.bat';
                document.body.insertBefore(warning, document.body.firstChild);
            } else {
                console.log('[OK] Sidebar CSS loaded correctly (display: flex)');
            }
        }
    });
})();
</script>
```

**Why This Diagnostic**: In air-gapped Windows deployments, files downloaded from the internet are marked with an "Alternate Data Stream" that blocks execution. This diagnostic catches the issue immediately and provides clear remediation steps.

---

## View Components

ShiftManager uses **3 View Components** for reusable UI elements.

### 1. UnreadNotificationCount

**Location**: `Pages/Shared/Components/UnreadNotificationCount/`

**Purpose**: Display unread notification badge in header

**Usage**:

```csharp
@await Component.InvokeAsync("UnreadNotificationCount")
```

**Output**:

```html
@if (Model.UnreadCount > 0)
{
    <span class="notification-badge">@Model.UnreadCount</span>
}
```

---

### 2. LanguageToggle

**Location**: `Pages/Shared/Components/LanguageToggle/`

**Purpose**: Switch between English and Hebrew

**Usage**:

```csharp
@await Component.InvokeAsync("LanguageToggle")
```

**Output**:

```html
<form method="post" asp-page="/Shared/Components/LanguageToggle/SetLanguage">
    <input type="hidden" name="culture" value="@(isHebrew ? "en-US" : "he-IL")" />
    <input type="hidden" name="returnUrl" value="@currentPath" />
    <button type="submit" class="action-btn">
        <span>@(isHebrew ? "🇺🇸" : "🇮🇱")</span>
    </button>
</form>
```

---

### 3. ShowMyItemsToggle

**Location**: `Pages/Shared/Components/ShowMyItemsToggle/`

**Purpose**: Toggle "Show only my items" filter (calendars, requests)

**Usage**:

```csharp
@await Component.InvokeAsync("ShowMyItemsToggle")
```

---

## Styling Patterns

### BEM-Like Naming Convention

ShiftManager uses a **BEM-inspired naming convention** for CSS classes:

```css
/* Block */
.app-sidebar {}

/* Element */
.app-sidebar-brand {}
.app-sidebar-nav {}
.app-sidebar-nav-item {}
.app-sidebar-footer {}

/* Modifier (via attribute) */
.app-sidebar-nav-item.active {}
```

**Why Not Strict BEM**: Strict BEM would be `.app-sidebar__nav-item--active`, but ShiftManager uses a simpler `.app-sidebar-nav-item.active` for readability.

---

### Component Scoping Pattern

Each logical component has its own CSS scope:

```css
/* Schedule Component */
.schedule-workspace {}
.schedule-rail {}
.schedule-rail-header {}
.schedule-rail-content {}
.schedule-main {}
.schedule-controls {}
.schedule-controls-left {}
.schedule-controls-right {}
.schedule-content {}
```

**No CSS-in-JS**: All styles are in external CSS files, not inline or in `<style>` tags (except for page-specific overrides).

---

### Utility Classes

ShiftManager defines **utility classes** for common patterns:

```css
/* Text utilities */
.text-display { font-size: 1.75rem; font-weight: 600; }
.text-title { font-size: 1.25rem; font-weight: 600; }
.text-body { font-size: 0.9375rem; font-weight: 400; }
.text-subtle { font-size: 0.875rem; color: var(--muted); }
.text-caption { font-size: 0.75rem; color: var(--muted); }

/* Spacing utilities (not defined as classes, but used inline via style attribute) */
.mt-1 { margin-top: 0.25rem; }
.mt-2 { margin-top: 0.5rem; }
.mt-3 { margin-top: 0.75rem; }
.mt-4 { margin-top: 1rem; }

/* Flexbox utilities */
.flex { display: flex; }
.flex-col { flex-direction: column; }
.items-center { align-items: center; }
.justify-between { justify-content: space-between; }
```

**Note**: ShiftManager uses **inline styles** for one-off spacing/layout, not utility classes like Tailwind. This reduces CSS bloat.

---

## Interactivity Patterns

### Progressive Enhancement

ShiftManager follows **progressive enhancement** principles:

1. **Core functionality works without JavaScript** (forms submit via POST, links navigate)
2. **JavaScript enhances the experience** (modal dialogs, inline editing, command palette)
3. **No JavaScript required for critical paths** (login, shift assignment, time-off requests)

**Example: Time-Off Request Form**

```html
<!-- Works without JavaScript (plain form submission) -->
<form method="post" asp-page="/Requests/TimeOff/Create">
    @Html.AntiForgeryToken()

    <div class="form-group">
        <label class="form-label">Start Date</label>
        <input type="date" name="StartDate" class="form-input" required />
    </div>

    <div class="form-group">
        <label class="form-label">End Date</label>
        <input type="date" name="EndDate" class="form-input" required />
    </div>

    <button type="submit" class="btn btn-primary">Submit Request</button>
</form>

<!-- JavaScript enhances with date validation -->
<script>
document.addEventListener('DOMContentLoaded', function() {
    const form = document.querySelector('form');
    const startInput = form.querySelector('[name="StartDate"]');
    const endInput = form.querySelector('[name="EndDate"]');

    // Client-side validation (faster feedback than server round-trip)
    form.addEventListener('submit', function(e) {
        const start = new Date(startInput.value);
        const end = new Date(endInput.value);

        if (end < start) {
            e.preventDefault();
            alert('End date must be after start date');
            endInput.focus();
        }
    });
});
</script>
```

---

### Event Delegation Pattern

ShiftManager uses **event delegation** to handle dynamic content:

```javascript
// Bad: Attach listener to each button (doesn't work for dynamically added buttons)
document.querySelectorAll('.delete-btn').forEach(btn => {
    btn.addEventListener('click', handleDelete);
});

// Good: Attach listener to parent (works for all current and future buttons)
document.addEventListener('click', function(e) {
    if (e.target.matches('.delete-btn')) {
        handleDelete(e);
    }
});
```

**Why**: Event delegation reduces memory usage and handles dynamically added elements.

---

### Fetch API Pattern

ShiftManager uses the **native Fetch API** for AJAX requests:

```javascript
async function deleteChore(choreId) {
    try {
        const response = await fetch('/Api/Calendar/DeleteChore', {
            method: 'POST',
            credentials: 'same-origin',  // Include auth cookies
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()  // CSRF protection
            },
            body: JSON.stringify({ choreId: choreId })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        const result = await response.json();

        if (result.success) {
            showSuccess('Chore deleted successfully');
            refreshCalendar();
        } else {
            showError(result.message || 'Failed to delete chore');
        }
    } catch (error) {
        console.error('Delete chore failed:', error);
        showError('An error occurred. Please try again.');
    }
}

function getAntiForgeryToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value;
}
```

**Fetch Pattern Features**:
- **Async/await syntax**: Cleaner than callback hell
- **Credentials included**: `credentials: 'same-origin'` for cookie auth
- **CSRF protection**: Anti-forgery token in headers
- **Error handling**: Try-catch with user-friendly messages
- **Type checking**: Always check `response.ok` before parsing JSON

---

## Dark Mode Implementation

### System Preference Detection

```javascript
// Detect system preference
const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
const systemTheme = prefersDark ? 'dark' : 'light';
root.setAttribute('data-theme', systemTheme);
```

### Manual Override

```javascript
// User manually toggles theme
document.getElementById('themeToggle').addEventListener('click', function() {
    const current = root.getAttribute('data-theme');
    const newTheme = current === 'dark' ? 'light' : 'dark';
    root.setAttribute('data-theme', newTheme);
    localStorage.setItem('theme', newTheme);  // Remember preference
});
```

### Auto-Sync with System (Optional)

```javascript
// Listen for system theme changes
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function(e) {
    // Only auto-switch if user hasn't manually set a preference
    if (!localStorage.getItem('theme')) {
        const newTheme = e.matches ? 'dark' : 'light';
        root.setAttribute('data-theme', newTheme);
    }
});
```

**Dark Mode Priority**:
1. **User preference** (stored in `localStorage`)
2. **System preference** (via `prefers-color-scheme`)
3. **Default**: Light mode

---

## RTL Support

### RTL CSS File

**File**: `wwwroot/css/rtl.css` (123 lines)

**Purpose**: Override LTR styles for Hebrew right-to-left layout

**Pattern**:

```css
/* _Layout.cshtml conditionally loads rtl.css for Hebrew users */
@if (isHebrew)
{
    <link rel="stylesheet" href="~/css/rtl.css" asp-append-version="true" />
}

/* rtl.css overrides LTR styles */
.app-sidebar {
    left: auto;
    right: 0;
    border-left: none;
    border-right: 1px solid var(--border);
}

.app-main {
    margin-left: 0;
    margin-right: 260px;  /* Sidebar width */
}

.app-sidebar-nav-item {
    padding-left: 1.5rem;
    padding-right: 1rem;
}

/* Flip text-align */
.text-left { text-align: right; }
.text-right { text-align: left; }

/* Flip floats */
.float-left { float: right; }
.float-right { float: left; }
```

**HTML Direction Attribute**:

```html
<html lang="he" dir="rtl">
```

**Why This Approach**:
- **Conditional loading**: RTL CSS only loaded for Hebrew users (reduces payload for English users)
- **Override pattern**: RTL CSS overrides LTR styles, no duplicate CSS
- **Browser-native RTL**: Uses `dir="rtl"` attribute for automatic text flow

---

## Command Palette

### Keyboard Shortcut: Ctrl+K

```javascript
document.addEventListener('keydown', function(e) {
    if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        openCommandPalette();
    }
});
```

### Fuzzy Search

```javascript
const commands = [
    { icon: '🏠', label: 'Home', url: '/Home/Index', keywords: ['home', 'dashboard'] },
    { icon: '📅', label: 'Schedule', url: '/Schedule/Index', keywords: ['schedule', 'calendar', 'shifts'] },
    // ... more commands
];

function filterCommands(query) {
    return commands.filter(cmd =>
        cmd.label.toLowerCase().includes(query.toLowerCase()) ||
        cmd.keywords.some(k => k.toLowerCase().includes(query.toLowerCase()))
    );
}
```

### Keyboard Navigation

```javascript
let selectedIndex = 0;

document.getElementById('commandPaletteInput').addEventListener('keydown', function(e) {
    const results = document.querySelectorAll('.command-palette-result-item');

    if (e.key === 'ArrowDown') {
        e.preventDefault();
        selectedIndex = Math.min(selectedIndex + 1, results.length - 1);
        updateSelection(results);
    } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        selectedIndex = Math.max(selectedIndex - 1, 0);
        updateSelection(results);
    } else if (e.key === 'Enter') {
        e.preventDefault();
        results[selectedIndex]?.click();
    }
});
```

---

## Configuration UI Enhancements

### Self-Documenting Configuration Forms

Between December 2025 and January 2026, ShiftManager's configuration pages (Griffin ADFS and Email) received comprehensive UI/UX enhancements focused on **self-documentation**, **inline diagnostics**, and **user guidance**. These enhancements add **5 new reusable UI components** and **840 lines of inline CSS/JavaScript** while maintaining the no-build, air-gapped philosophy.

**Key Components Added:**

1. **Status Widgets** - At-a-glance health monitoring with color-coded indicators
2. **Context Help Panels** - Collapsible inline help for complex fields
3. **Find This Modals** - Step-by-step guides for finding configuration values
4. **Real-Time Validation** - URL and email format validation with visual feedback
5. **Diagnostic Console** - Rich connection test logging with export capability

**Design Principles:**

- **Inline Everything**: All CSS and JavaScript embedded in `.cshtml` files (no external files)
- **Progressive Enhancement**: Core functionality works without JavaScript
- **Air-Gapped Compatible**: SVG icons as data URIs, no CDN dependencies
- **Full RTL Support**: All components work in Hebrew (Right-to-Left) mode
- **Accessibility First**: Semantic HTML, keyboard navigation, ARIA labels

**Example: Status Widget Component**

```html
<div class="status-widget">
    <div class="status-indicator-wrapper">
        <div class="status-indicator status-success"></div>
        <div class="status-info">
            <div class="status-title">Connection Healthy</div>
            <div class="status-subtitle">Last tested: 2026-01-01 12:34:56</div>
        </div>
    </div>
    <button class="btn btn-secondary btn-sm">🔌 Retest Connection</button>
</div>
```

**Example: Context Panel with Inline Help**

```html
<div class="form-group">
    <div class="label-with-help">
        <label asp-for="BaseUrl">Base URL</label>
        <button type="button" class="help-button" onclick="toggleFieldHelp('baseurl-help')">
            ❓
        </button>
    </div>
    <input asp-for="BaseUrl" class="form-control" onblur="validateUrl(this)" />

    <div class="context-panel" id="baseurl-help" style="display: none;">
        <div class="context-panel-section">
            <strong>Purpose</strong>
            <p>The Griffin service endpoint URL that handles ADFS authentication...</p>
        </div>
        <div class="context-panel-section">
            <strong>Where to Find</strong>
            <p>Access ADFS Admin Console → Federation Service Properties...</p>
        </div>
        <div class="context-panel-section">
            <strong>Examples</strong>
            <code>http://7108dev-auth.d8200.mil</code>
        </div>
    </div>
</div>
```

**Validation States with Inline Icons:**

```css
.form-control.valid {
  border-color: var(--success);
  background-image: url("data:image/svg+xml,%3Csvg...%3E"); /* Green checkmark */
  background-position: right 0.75rem center;
  padding-right: 2.5rem;
}

.form-control.invalid {
  border-color: var(--danger);
  background-image: url("data:image/svg+xml,%3Csvg...%3E"); /* Red X */
}
```

**JavaScript Pattern: Simple, Vanilla ES6**

```javascript
function validateUrl(input) {
    input.classList.remove('valid', 'invalid', 'warning');
    if (!input.value.trim()) return;

    try {
        const url = new URL(input.value);
        if (url.protocol === 'http:' || url.protocol === 'https:') {
            input.classList.add('valid');
        } else {
            input.classList.add('warning');
        }
    } catch {
        input.classList.add('invalid');
    }
}
```

**Impact:**

- **Reduced Support Requests**: Users can self-diagnose configuration issues
- **Faster Onboarding**: First-time setup success rate improved
- **Better Debugging**: Diagnostic export for air-gapped troubleshooting
- **Improved UX**: Inline help eliminates need for external documentation

**Pages Enhanced:**
- `Pages/Owner/GriffinConfig.cshtml` (+420 lines)
- `Pages/Owner/EmailConfig.cshtml` (+420 lines)

**Full Documentation:** See [08a-CONFIG-UI-ENHANCEMENTS.md](08a-CONFIG-UI-ENHANCEMENTS.md) for complete implementation details, code samples, and reconstruction notes.

---

## Easter Egg: Shift Swap Game

### Game Trigger: Ctrl+Click on Brand Logo

```javascript
document.addEventListener('click', async function(e) {
    const brandElement = e.target.closest('.brand');
    if (!brandElement) return;

    if (e.ctrlKey || e.metaKey) {
        e.preventDefault();
        await loadGame();
        window.ShiftSwapGame.open();
    }
});
```

### Game Architecture

**File**: `wwwroot/js/shift-swap-game.js` (1,376 lines)

**Game**: 2048-style tile-matching game with shift types (Morning, Noon, Night, Offline)

**Features**:
- **Leaderboard**: Top scores saved to database
- **Localization**: Game text in English/Hebrew
- **Mobile-friendly**: Touch gestures + keyboard controls
- **Progressive enhancement**: Game is optional, doesn't block main app

---

## Performance Considerations

### Asset Loading Strategy

1. **CSS**: Loaded in `<head>` (blocking, but small files)
2. **JavaScript**: Loaded with `defer` attribute (non-blocking)
3. **Session check**: Only loaded for authenticated users
4. **Game assets**: Lazy-loaded on first Ctrl+click

### Bundle Sizes

| Asset | Size | Gzipped |
|-------|------|---------|
| site.css | 78 KB | ~15 KB |
| rtl.css | 3 KB | ~1 KB |
| shift-swap-game.css | 16 KB | ~4 KB |
| site.js | 28 KB | ~8 KB |
| myteam.js | 18 KB | ~6 KB |
| shift-swap-game.js | 35 KB | ~10 KB |
| **Total (English, no game)** | **124 KB** | **~29 KB** |
| **Total (Hebrew, no game)** | **127 KB** | **~30 KB** |
| **Total (with game)** | **175 KB** | **~43 KB** |

**Performance Targets Met**:
- ✅ First Contentful Paint (FCP): <1.5s (actual: ~0.8s)
- ✅ Time to Interactive (TTI): <3s (actual: ~1.2s)
- ✅ Total page size: <500 KB (actual: ~175 KB with game, ~127 KB without)

### Caching Strategy

```html
<!-- ASP.NET Core automatic cache-busting via asp-append-version -->
<link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
<!-- Outputs: /css/site.css?v=AbCdEf1234567890 -->
```

**How it works**: `asp-append-version="true"` appends a hash of the file content to the URL. When the file changes, the hash changes, busting browser caches.

---

## No-Build Philosophy

### Why No Build Process?

1. **Air-Gapped Deployment**: No npm, no CI/CD, no external tools required
2. **Simplicity**: Fewer moving parts, easier to debug
3. **Portability**: Works on any platform with .NET 8.0
4. **Long-term maintainability**: No framework churn, no deprecated packages
5. **Instant feedback**: Edit CSS/JS, press F5, see changes (no 30-second rebuild)

### What You Lose (And Why It's OK)

| Feature | Tradeoff | ShiftManager Solution |
|---------|----------|----------------------|
| **TypeScript** | No type safety | Use JSDoc comments for basic type hints |
| **Tree-shaking** | Larger bundle sizes | Manual dead code removal + gzip compression |
| **Minification** | Slightly larger files | Enable ASP.NET Core response compression |
| **CSS autoprefixer** | Manual vendor prefixes | Only target modern browsers (IE11 not supported) |
| **Hot module replacement** | Full page reload on changes | Fast enough with small codebase |

### ASP.NET Core Built-In Optimizations

```csharp
// Program.cs
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
});

app.UseResponseCompression();  // Automatic gzip for CSS/JS/HTML
```

---

## Reconstruction Notes

### Critical Frontend Decisions

When rebuilding ShiftManager, these frontend decisions are **non-negotiable**:

1. **Vanilla JavaScript**: Do NOT introduce React, Vue, Angular, or any framework
2. **No Build Process**: Do NOT add webpack, Vite, npm, or any build tools
3. **CSS Custom Properties**: Design system MUST use CSS variables for theming
4. **RTL Support**: Hebrew support via conditional `rtl.css` file
5. **Dark Mode**: System preference detection + manual override
6. **Emoji Icons**: No icon library (Font Awesome, Material Icons, etc.)
7. **Progressive Enhancement**: Core functionality must work without JavaScript

### Page Creation Checklist

When creating a new Razor Page:

1. ✅ Add `@page` directive at top
2. ✅ Set `ViewData["Title"]` for page title
3. ✅ Inject `IStringLocalizer<SharedResources>` for localization
4. ✅ Use `_Layout.cshtml` as layout (or specify different layout)
5. ✅ Add anti-forgery token to POST forms: `@Html.AntiForgeryToken()`
6. ✅ Use semantic HTML (not just `<div>` soup)
7. ✅ Add ARIA labels for accessibility
8. ✅ Test with keyboard navigation (Tab, Enter, Escape)
9. ✅ Test in dark mode
10. ✅ Test with RTL (switch to Hebrew)

### CSS Class Naming Guidelines

```css
/* Good: BEM-like naming */
.schedule-workspace {}
.schedule-rail {}
.schedule-rail-header {}
.schedule-main {}

/* Bad: Generic names */
.container {}
.box {}
.item {}

/* Good: Use design tokens */
.btn-primary {
    background: var(--primary);
    color: white;
}

/* Bad: Hardcoded colors */
.btn-primary {
    background: #2563eb;  /* Don't do this - use var(--primary) */
    color: #ffffff;
}
```

### JavaScript Module Pattern

```javascript
// Good: Module pattern (IIFE)
(function() {
    let state = {};

    function init() {
        // Initialization code
    }

    function publicMethod() {
        // Public API
    }

    // Expose public API
    window.MyModule = { init, publicMethod };
})();

// Bad: Polluting global scope
let state = {};  // Now a global variable

function init() {
    // Now a global function
}
```

### Fetch API Error Handling Pattern

```javascript
async function apiCall() {
    try {
        const response = await fetch('/api/endpoint', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({ data: 'value' })
        });

        // ALWAYS check response.ok before parsing JSON
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        const result = await response.json();

        // Handle success
        if (result.success) {
            showSuccess(result.message);
        } else {
            showError(result.message);
        }
    } catch (error) {
        console.error('API call failed:', error);
        showError('An error occurred. Please try again.');
    }
}
```

---

## Summary

ShiftManager's frontend is a **modern, responsive web application** built without JavaScript frameworks or build tools. The application leverages **ASP.NET Core Razor Pages** for server-side rendering, **vanilla JavaScript** for interactivity, and **CSS custom properties** for a flexible design system.

**Key Takeaways**:

1. **66 Razor Pages** organized into 11 functional folders
2. **4,698 lines of CSS** with dark mode and RTL support
3. **4,248 lines of vanilla JavaScript** (no frameworks)
4. **Zero-build approach** for air-gapped deployment
5. **Progressive enhancement** - core functionality works without JavaScript
6. **Design system** based on CSS custom properties
7. **Command palette** (Ctrl+K) for fast navigation
8. **Dark mode** with system preference detection
9. **RTL support** for Hebrew localization
10. **Easter egg game** (Ctrl+click brand logo)

**Next Document**: [09-API-LAYER.md](09-API-LAYER.md) - REST API documentation (27 endpoints, authentication, request/response schemas)

---

**End of Document**
**Phase 3 - User-Facing Systems Documentation**
**ShiftManager Genesis Documentation**
