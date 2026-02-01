# ShiftManager UI Redesign - Migration Notes

**Purpose**: Technical documentation for the redesign project, including route mappings, localization keys, architectural decisions, and migration guidance.

---

## Route Mapping

### Strategy
Both old and new routes will coexist during migration. Old routes remain functional to avoid breaking bookmarks and external links.

### New Routes

| New Route | Purpose | Authorization | Maps From Old Route |
|-----------|---------|---------------|---------------------|
| `/Home/Index` | New landing page, role-aware dashboard | `[Authorize]` | N/A (new) |
| `/Schedule` | Consolidated calendar/table workspace | `[Authorize]` | `/Calendar/Month`, `/Calendar/Week`, `/Calendar/Day`, `/Calendar/Table` |

### Old Routes (Preserved)

| Old Route | Status | Notes |
|-----------|--------|-------|
| `/Calendar/Month` | ACTIVE | Still works, may add redirect later |
| `/Calendar/Week` | ACTIVE | Still works, may add redirect later |
| `/Calendar/Day` | ACTIVE | Still works, may add redirect later |
| `/Calendar/Table` | ACTIVE | Still works, may add redirect later |
| `/My/Index` | ACTIVE | Preserved for "My Shifts" functionality |
| `/My/Requests` | ACTIVE | Preserved |
| `/My/Profile` | ACTIVE | Preserved, UI updated |
| `/My/NotificationCenter` | ACTIVE | Preserved, UI updated |
| `/My/ApiKeys` | ACTIVE | Preserved, UI updated |
| `/MyTeam/Index` | ACTIVE | Preserved, UI updated |
| `/Requests/Index` | ACTIVE | Preserved, UI updated (unified inbox) |
| `/Requests/TimeOff/Create` | ACTIVE | Preserved |
| `/Requests/Swaps/Create` | ACTIVE | Preserved |
| `/Assignments/Manage` | ACTIVE | Preserved, may become panel within Schedule |
| `/Public/Chores` | ACTIVE | Preserved, UI updated |
| `/Public/OnDuty` | ACTIVE | Preserved, UI updated |
| `/Public/Feedback` | ACTIVE | Preserved, UI updated |
| `/Chores/Calendar` | ACTIVE | Preserved, UI updated |
| `/Admin/Users` | ACTIVE | Preserved, UI updated |
| `/Admin/Companies` | ACTIVE | Preserved, UI updated (Owner only) |
| `/Admin/Directors` | ACTIVE | Preserved, UI updated (Owner only) |
| `/Admin/ShiftTypes` | ACTIVE | Preserved, UI updated |
| `/Admin/TimeOff` | ACTIVE | Preserved, UI updated |
| `/Admin/Config` | ACTIVE | Preserved, UI updated |
| `/Admin/Analytics` | ACTIVE | Preserved, UI updated |
| `/Admin/AuditLog` | ACTIVE | Preserved |
| `/Admin/EditProfile` | ACTIVE | Preserved |
| `/Director/NotificationHub` | ACTIVE | Preserved, UI updated |
| `/Director/CompanyFilter` | ACTIVE | Preserved, UI updated |
| `/Director/ViewAsMode` | ACTIVE | Preserved |
| `/Diagnostic` | ACTIVE | Preserved, UI updated (Owner only) |
| `/Auth/Login` | ACTIVE | Preserved, UI updated, redirects to `/Home/Index` |
| `/Auth/Signup` | ACTIVE | Preserved, UI updated |
| `/Auth/ForgotPassword` | ACTIVE | Preserved, UI updated |
| `/Auth/Logout` | ACTIVE | Preserved |

### Query Parameters for /Schedule

| Parameter | Values | Default | Purpose |
|-----------|--------|---------|---------|
| `calendar` | `company-shifts`, `my-shifts`, `chores`, `on-duty` | `company-shifts` | Which calendar to display |
| `view` | `calendar`, `table` | `calendar` | Primary view mode |
| `mode` | `month`, `week`, `day` | `month` | Calendar sub-mode (only when view=calendar) |
| `date` | ISO date string | Today | Current viewing date |

**Examples**:
- `/Schedule` → Company shifts, calendar view, month mode
- `/Schedule?view=table` → Table view for assignments
- `/Schedule?calendar=my-shifts&view=calendar&mode=week` → My shifts, week view
- `/Schedule?calendar=chores&view=calendar&mode=month&date=2025-12-01` → Chores for December 2025

---

## Localization Keys

### Key Naming Convention
- Use PascalCase for multi-word keys: `MyShifts`, `RequestTimeOff`
- Use singular for page names: `Home`, `Schedule`, `Request`
- Use plural for navigation items when appropriate: `Requests`, `Users`
- Use descriptive names for actions: `OpenSchedule`, `ViewAllNotifications`

### New Keys Required

#### Navigation
| Key | English | Hebrew | Used In |
|-----|---------|--------|---------|
| `Home` | Home | בית | Sidebar, breadcrumbs |
| `Schedule` | Schedule | לוח זמנים | Sidebar, breadcrumbs |
| `Calendars` | Calendars | לוחות שנה | Schedule page calendar rail |
| `CompanyShifts` | Company Shifts | משמרות החברה | Schedule calendar selector |
| `MyShifts` | My Shifts | המשמרות שלי | Sidebar, Schedule selector |
| `Chores` | Chores | מטלות | Sidebar, Schedule selector |
| `OnDuty` | On Duty | תורנות | Schedule selector |
| `MyRequests` | My Requests | הבקשות שלי | Sidebar |
| `Requests` | Requests | בקשות | Sidebar (Manager) |
| `Analytics` | Analytics | ניתוחים | Sidebar |
| `People` | People | אנשים | Sidebar |
| `Settings` | Settings | הגדרות | Sidebar |
| `Companies` | Companies | חברות | Sidebar (Director/Owner) |

#### View Controls
| Key | English | Hebrew | Used In |
|-----|---------|--------|---------|
| `Calendar` | Calendar | לוח שנה | Schedule view toggle |
| `Table` | Table | טבלה | Schedule view toggle |
| `Month` | Month | חודש | Calendar mode toggle |
| `Week` | Week | שבוע | Calendar mode toggle |
| `Day` | Day | יום | Calendar mode toggle |
| `Previous` | Previous | הקודם | Date navigation |
| `Next` | Next | הבא | Date navigation |
| `Today` | Today | היום | Date navigation |

#### Home Page Dashboard
| Key | English | Hebrew | Used In |
|-----|---------|--------|---------|
| `NextShift` | Next Shift | המשמרת הבאה | Home dashboard card |
| `NoUpcomingShifts` | No upcoming shifts | אין משמרות קרובות | Home dashboard |
| `Notifications` | Notifications | התראות | Home dashboard card |
| `ViewAllNotifications` | View all notifications | צפה בכל ההתראות | Home dashboard CTA |
| `Unread` | Unread | לא נקראו | Notification badge |
| `HoursThisWeek` | Hours This Week | שעות השבוע | My Shifts Summary |
| `DaysOff` | Days Off | ימי חופש | My Shifts Summary |
| `ViewFullSchedule` | View Full Schedule | צפה בלוח זמנים מלא | My Shifts Summary CTA |
| `GoToMyRequests` | Go to My Requests | עבור לבקשות שלי | My Requests card CTA |
| `PendingRequests` | Pending Requests | בקשות ממתינות | Requests card |
| `ApprovedRequests` | Approved Requests | בקשות מאושרות | Requests card |
| `DeclinedRequests` | Declined Requests | בקשות נדחות | Requests card |
| `StaffingOverview` | Staffing Overview | סקירת כוח אדם | Manager dashboard |
| `UnassignedShifts` | Unassigned Shifts | משמרות לא משובצות | Staffing card |
| `UnderstaffedDays` | Understaffed Days | ימים עם מחסור בכוח אדם | Staffing card |
| `OpenSchedulingWorkspace` | Open Scheduling Workspace | פתח סביבת תזמון | Staffing CTA |
| `Approvals` | Approvals | אישורים | Manager dashboard |
| `PendingTimeOff` | Pending Time Off | בקשות חופש ממתינות | Approvals card |
| `PendingSwaps` | Pending Swaps | בקשות החלפה ממתינות | Approvals card |
| `OpenApprovals` | Open Approvals | פתח אישורים | Approvals CTA |
| `CompaniesOverview` | Companies Overview | סקירת חברות | Director/Owner dashboard |

#### Actions & Buttons
| Key | English | Hebrew | Used In |
|-----|---------|--------|---------|
| `RequestTimeOff` | Request Time Off | בקש חופש | Schedule header (Employee) |
| `AssignShifts` | Assign Shifts | שבץ משמרות | Schedule header (Manager) |
| `OpenSchedule` | Open Schedule | פתח לוח זמנים | Various CTAs |
| `CreateRequest` | Create Request | צור בקשה | Request pages |
| `Approve` | Approve | אשר | Approval actions |
| `Decline` | Decline | דחה | Approval actions |
| `Save` | Save | שמור | Form actions |
| `Cancel` | Cancel | בטל | Form actions |
| `Edit` | Edit | ערוך | General actions |
| `Delete` | Delete | מחק | General actions |
| `Create` | Create | צור | General actions |
| `Search` | Search | חפש | Search/filter |
| `Filter` | Filter | סנן | Filtering |
| `ViewDetails` | View Details | צפה בפרטים | Various |

#### Status & States
| Key | English | Hebrew | Used In |
|-----|---------|--------|---------|
| `Pending` | Pending | ממתין | Request status |
| `Approved` | Approved | מאושר | Request status |
| `Declined` | Declined | נדחה | Request status |
| `Active` | Active | פעיל | User status |
| `Inactive` | Inactive | לא פעיל | User status |
| `Assigned` | Assigned | משובץ | Shift status |
| `Unassigned` | Unassigned | לא משובץ | Shift status |

#### Time & Dates
| Key | English | Hebrew | Used In |
|-----|---------|--------|---------|
| `StartDate` | Start Date | תאריך התחלה | Forms |
| `EndDate` | End Date | תאריך סיום | Forms |
| `Date` | Date | תאריך | General |
| `Time` | Time | שעה | General |
| `Duration` | Duration | משך | General |
| `Hours` | Hours | שעות | Analytics, summaries |
| `Days` | Days | ימים | Analytics, summaries |

#### Roles
| Key | English | Hebrew | Used In |
|-----|---------|--------|---------|
| `Owner` | Owner | בעלים | Role display |
| `Director` | Director | מנהל | Role display |
| `Manager` | Manager | מנהל | Role display |
| `Employee` | Employee | עובד | Role display |
| `Trainee` | Trainee | מתאמן | Role display |

#### Command Palette
| Key | English | Hebrew | Used In |
|-----|---------|--------|---------|
| `SearchPlaceholder` | Type to search pages and people... | הקלד לחיפוש דפים ואנשים... | Command palette |
| `RecentPages` | Recent Pages | דפים אחרונים | Command palette |
| `NoResults` | No results found | לא נמצאו תוצאות | Command palette |

### Existing Keys to Verify

Ensure these existing keys are used correctly in the new UI:
- `ShiftManager` - App title (admin view)
- `MySchedule` - App title (employee view)
- `Login`, `Logout`, `Signup`, `ForgotPassword`
- `Email`, `Password`, `DisplayName`, `Phone`
- `TimeOff`, `SwapRequest`
- `Users`, `ShiftTypes`, `Config`, `AuditLog`
- Language toggle and theme toggle labels

---

## CSS Architecture

### Variable Naming Convention

**Color Variables**:
- `--bg` - Main background
- `--text` - Primary text color
- `--muted` - Muted/secondary text
- `--surface` - Card/panel background
- `--surface-soft` - Slightly different surface (headers)
- `--surface-elevated` - Elevated surface (modals, dropdowns)
- `--surface-strong` - Strong background (if needed)
- `--primary` - Primary brand color
- `--primary-soft` - Soft primary background
- `--primary-rgb` - RGB values for alpha blending
- `--border` - Border color
- `--danger` - Error/delete color
- `--success` - Success color
- `--warning` - Warning color
- `--focus` - Focus indicator color

**Typography Variables** (if added):
- `--font-family-base`
- `--font-size-display`
- `--font-size-title`
- `--font-size-body`
- `--font-size-caption`

### Class Naming Convention

**Utility Classes**:
- `.text-display`, `.text-title`, `.text-body`, `.text-subtle`, `.text-caption` - Typography
- `.card`, `.card--status-success`, `.card--metric`, `.card--centered` - Cards
- `.app-shell`, `.app-sidebar`, `.app-main`, `.app-header`, `.app-content` - Layout

**Component Classes**:
- `.btn`, `.btn-primary`, `.btn-secondary`, `.btn-danger` - Buttons
- `.breadcrumb`, `.breadcrumb-nav`, `.breadcrumb-item` - Breadcrumbs (existing, do not change)
- `.modal`, `.modal-overlay`, `.modal-content` - Modals

### RTL Overrides

All RTL-specific overrides go in `rtl.css` using `[dir="rtl"]` selector:
```css
[dir="rtl"] .app-sidebar {
  left: auto;
  right: 0;
}
```

---

## JavaScript Architecture

### Theme Management

**localStorage Keys**:
- `theme` - User's theme preference ("light" or "dark")

**HTML Attributes**:
- `data-theme` on `<html>` element - Controls active theme

**Functions** (site.js):
- Theme toggle listener on `#themeToggle`
- System preference detection: `window.matchMedia("(prefers-color-scheme: dark)")`

### Command Palette

**localStorage Keys**:
- `recentPages` - Array of recently visited pages (JSON)

**Keyboard Shortcut**:
- `Ctrl+K` (Windows/Linux) or `Cmd+K` (Mac) - Open command palette
- `Escape` - Close command palette

**Functions**:
- `openCommandPalette()` - Show modal
- `closeCommandPalette()` - Hide modal
- `addToRecentPages(pageName, pageUrl)` - Update recent history
- `searchPages(query)` - Filter page list

---

## Database & Backend Considerations

### No Schema Changes
The redesign is UI-only. No database migrations required.

### Service Layer
Reuse existing services:
- `INotificationService` - For notification counts
- `IShiftService` - For shift data
- `IRequestService` - For request data
- `IAnalyticsService` - For dashboard metrics
- `IViewAsModeService` - For director features

### New Methods Needed

**For Home Dashboard**:
- `GetNextShiftAsync(userId)` - Next upcoming shift
- `GetMyShiftsSummaryAsync(userId, startDate, endDate)` - Hours, days summary
- `GetMyRequestsSummaryAsync(userId)` - Counts by status
- `GetStaffingOverviewAsync(companyId)` - Unassigned shifts, understaffed days
- `GetApprovalsSummaryAsync(companyId)` - Pending counts
- `GetCompaniesOverviewAsync(directorId)` - Company metrics for directors

These may be implemented as:
1. New methods in existing services
2. Queries directly in PageModel (simpler approach)

---

## Breadcrumb Component

### Critical Rule
**DO NOT MODIFY**:
- `ViewComponents/BreadcrumbViewComponent.cs`
- `Views/Shared/Components/Breadcrumb/Default.cshtml`

### What CAN Change
- **Placement**: Where `@await Component.InvokeAsync("Breadcrumb", ...)` is called
- **Items**: What breadcrumb items are passed to the component

### Standard Placement (New Layout)
Breadcrumbs should appear in `app-header-left` section, below or next to page title:

```html
<div class="app-header-left">
    <h1 class="page-title">@ViewData["Title"]</h1>
    @await Component.InvokeAsync("Breadcrumb", new List<BreadcrumbItem> {
        new BreadcrumbItem { Label = Localizer["Home"], Url = "/Home/Index" },
        new BreadcrumbItem { Label = Localizer["Schedule"], IsActive = true }
    })
</div>
```

### Example Breadcrumb Patterns

**Home Page**:
```csharp
new List<BreadcrumbItem> {
    new BreadcrumbItem { Label = Localizer["Home"], IsActive = true }
}
```

**Schedule Page**:
```csharp
new List<BreadcrumbItem> {
    new BreadcrumbItem { Label = Localizer["Home"], Url = "/Home/Index" },
    new BreadcrumbItem { Label = Localizer["Schedule"], IsActive = true }
}
```

**Admin Users Page**:
```csharp
new List<BreadcrumbItem> {
    new BreadcrumbItem { Label = Localizer["Home"], Url = "/Home/Index" },
    new BreadcrumbItem { Label = Localizer["Admin"], Url = "#" },
    new BreadcrumbItem { Label = Localizer["Users"], IsActive = true }
}
```

**My Profile Page**:
```csharp
new List<BreadcrumbItem> {
    new BreadcrumbItem { Label = Localizer["Home"], Url = "/Home/Index" },
    new BreadcrumbItem { Label = Localizer["MyProfile"], IsActive = true }
}
```

---

## Testing Strategy

### Phase Testing
After each phase:
1. Visual inspection (light + dark mode)
2. Test in English
3. Test in Hebrew (full page)
4. Verify RTL layout
5. Check console for errors
6. Verify localization complete

### Role-Based Testing
For each major feature (Home, Schedule, Requests):
- Test as Employee
- Test as Trainee
- Test as Manager
- Test as Director
- Test as Owner

### Browser Testing
Test on:
- Chrome (primary)
- Firefox
- Safari
- Edge

### Device Testing
Test responsive behavior:
- Desktop (1920x1080)
- Tablet (1024x768)
- Mobile (375x667)

---

## Rollback Plan

If critical issues are discovered:

1. **CSS Rollback**: Revert `site.css` to previous version
2. **Layout Rollback**: Revert `_Layout.cshtml` to previous version
3. **Route Rollback**: Since old routes are preserved, can redirect users back to old URLs
4. **Full Rollback**: Git revert to commit before redesign started

**Recommendation**: Create git branches for each major phase to allow partial rollbacks.

---

## Performance Considerations

### CSS Optimization
- Minimize CSS variables (only what's needed)
- Avoid deep nesting in CSS
- Use CSS containment where appropriate

### JavaScript Optimization
- Debounce command palette search
- Lazy load heavy components
- Use event delegation for dynamic content

### Page Load Optimization
- Keep _Layout.cshtml lean
- Defer non-critical JavaScript
- Use `asp-append-version` for cache busting

### Rendering Optimization
- Use CSS Grid/Flexbox efficiently
- Avoid unnecessary re-renders (React-like patterns)
- Optimize calendar rendering (already handled)

---

## Accessibility Considerations

### ARIA Labels
Maintain existing ARIA labels:
- Navigation landmarks
- Button labels
- Form labels
- Modal dialogs

### Keyboard Navigation
Preserve and enhance:
- Tab order logical
- Escape closes modals
- Arrow keys for calendar navigation
- Command palette shortcut (Ctrl/Cmd+K)

### Screen Reader Support
- Semantic HTML (nav, main, article, aside)
- Breadcrumb navigation uses existing accessible component
- Form labels properly associated

### Color Contrast
- Verify WCAG AA compliance in both themes
- Test with contrast checking tools
- Ensure focus indicators visible

---

## Deployment Strategy

### Staging Deployment
1. Deploy to staging environment first
2. Full testing checklist completion
3. User acceptance testing (if applicable)

### Production Deployment
1. Deploy during low-traffic period
2. Monitor error logs closely
3. Be ready to rollback if needed
4. Announce changes to users (if appropriate)

### Feature Flags (Optional)
Consider adding feature flag for new layout:
- `Features:NewUIEnabled` in appsettings.json
- Allows gradual rollout or A/B testing
- Can be per-user or per-company

---

## Known Issues & Limitations

### Browser Limitations
- CSS Grid in older browsers (IE11) - not supported, acceptable
- CSS variables in older browsers - not supported, acceptable

### RTL Limitations
- Some icons may not flip automatically - may need manual `scaleX(-1)` in rtl.css
- Chart libraries may need RTL configuration

### Performance Limitations
- Large calendar grids (many shift types × many days) may be slow - existing issue, not made worse
- Command palette search on very large datasets - limit to 50 results

---

## Future Enhancements (Out of Scope)

Features not included in this redesign but could be added later:
- Full-text search with backend API (Command palette currently static)
- Advanced filtering in Schedule workspace
- Drag-and-drop shift assignments
- Real-time collaborative editing
- Push notifications
- Progressive Web App (PWA) features
- Mobile native apps
- Advanced analytics dashboards
- Gantt chart view for schedules

---

## Questions & Decisions Log

| Date | Question | Decision | Rationale |
|------|----------|----------|-----------|
| 2025-11-17 | Should old routes redirect or coexist? | Coexist temporarily | Allows gradual migration, doesn't break bookmarks |
| 2025-11-17 | Command palette scope? | Pages + recent history | Balances complexity and value |
| 2025-11-17 | Progress tracking? | Multiple files | Better organization for large project |
| 2025-11-17 | Implementation priority? | Design system → Navigation → User journeys | Build foundation, then iterate by role |
| 2025-11-17 | Localization requirement? | ALL text must be localized | Critical for i18n, no hardcoded strings |

---

**Last Updated**: 2025-11-17
**Maintained By**: Development Team
**Review Frequency**: After each phase completion
