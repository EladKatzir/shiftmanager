# ShiftManager UI Redesign - Localization Audit

**Date**: 2025-11-17
**Purpose**: Comprehensive audit of existing localization keys and identification of new keys needed for the redesign.

---

## Summary

- **Total Existing Keys**: 800 keys in SharedResources.resx
- **Languages Supported**: English (en-US), Hebrew (he-IL)
- **New Keys Required**: ~60-80 new keys for redesign UI

---

## Existing Keys Analysis

### ✅ EXISTING - Navigation & Core UI

These keys **already exist** and can be reused in the new design:

| Key | English Value | Usage in Redesign |
|-----|---------------|-------------------|
| `ShiftManager` | "Shift Manager" | App title (admin) |
| `MySchedule` | "My Schedule" | App title (employee) |
| `Calendar` | "Calendar" | View toggle |
| `Schedule` | "Schedule" | Navigation, breadcrumb |
| `MonthView` | "Month View" | Calendar mode toggle |
| `WeekView` | "Week View" | Calendar mode toggle |
| `DayView` | "Day View" | Calendar mode toggle |
| `TableView` | "Table View" | View toggle |
| `MyShifts` | "My Shifts" | Navigation, calendar selector |
| `Requests` | "Requests" | Navigation |
| `Analytics` | "Analytics" | Navigation |
| `Admin` | "Admin" | Breadcrumb |
| `Users` | "Users" | Navigation |
| `ShiftTypes` | "Shift Types" | Navigation |
| `TimeOffManagement` | "Time-Off Management" | Navigation |
| `Config` | "Config" | Navigation |
| `AuditLog` | "Audit Log" | Navigation |
| `Notifications` | "Notifications" | Navigation, dashboard card |
| `Logout` | "Logout" | User menu |
| `Save` | "Save" | Form actions |
| `Cancel` | "Cancel" | Form actions |
| `Delete` | "Delete" | Form actions |
| `Edit` | "Edit" | Form actions |
| `GoodMorning` | "Good morning, {0}!" | Employee greeting |
| `GoodAfternoon` | "Good afternoon, {0}!" | Employee greeting |
| `GoodEvening` | "Good evening, {0}!" | Employee greeting |
| `ScheduleSubtitle` | "Here's your schedule and shift information" | Header subtitle |
| `Approved` | "Approved" | Status label |
| `Pending` | "Pending" | Status label |
| `Declined` | "Declined" | Status label |
| `Active` | "Active" | User status |
| `Inactive` | "Inactive" | User status |

### ✅ EXISTING - Roles

| Key | English Value | Usage |
|-----|---------------|-------|
| `OwnerRole` | "Owner" | Role display |
| `DirectorRole` | "Director" | Role display |
| `ManagerRole` | "Manager" | Role display |
| `EmployeeRole` | "Employee" | Role display |
| `TraineeRole` | "Trainee" | Role display |

### ✅ EXISTING - Time & Dates

| Key | English Value | Usage |
|-----|---------------|-------|
| `Today` | "Today" | Date navigation |
| `StartDate` | "Start Date" | Forms |
| `EndDate` | "End Date" | Forms |
| `Date` | "Date" | General |
| `Hours` | "Hours" | Time display |
| `Days` | "Days" | Time display |

### ✅ EXISTING - Chores & Public

| Key | English Value | Usage |
|-----|---------------|-------|
| `Chores` | "Chores" | Navigation, calendar selector |
| `OnDuty` | "On Duty" | Calendar selector |
| `Feedback` | "Feedback" | Navigation |

### ✅ EXISTING - Requests & Approvals

| Key | English Value | Usage |
|-----|---------------|-------|
| `TimeOffRequest` | "Time-Off Request" | Request type |
| `SwapRequest` | "Swap Request" | Request type |
| `Approve` | "Approve" | Action button |
| `Decline` | "Decline" | Action button |
| `ApprovalRate` | "Approval Rate" | Analytics |
| `AverageApprovalTime` | "Average Approval Time" | Analytics |

---

## ❌ MISSING - New Keys Required for Redesign

### Priority 1: Core Navigation & Layout

These keys are **critical** for Phase 2 (Layout & Navigation):

| Key | Proposed English | Proposed Hebrew | Usage |
|-----|------------------|-----------------|-------|
| `Home` | "Home" | "בית" | Sidebar navigation, breadcrumb |
| `People` | "People" | "אנשים" | Sidebar (Manager+) |
| `Settings` | "Settings" | "הגדרות" | Sidebar (Manager+) |
| `Companies` | "Companies" | "חברות" | Sidebar (Director/Owner) |
| `MyTeam` | "My Team" | "הצוות שלי" | Sidebar navigation |
| `MyProfile` | "My Profile" | "הפרופיל שלי" | Sidebar navigation |

### Priority 2: Schedule Workspace

These keys are needed for Phase 4 (Schedule Workspace):

| Key | Proposed English | Proposed Hebrew | Usage |
|-----|------------------|-----------------|-------|
| `Calendars` | "Calendars" | "לוחות שנה" | Calendar rail title |
| `CompanyShifts` | "Company Shifts" | "משמרות החברה" | Calendar selector |
| `MyShiftsCalendar` | "My Shifts" | "המשמרות שלי" | Calendar selector (duplicate of MyShifts?) |
| `ChoresCalendar` | "Chores" | "מטלות" | Calendar selector |
| `OnDutyCalendar` | "On Duty" | "תורנות" | Calendar selector |
| `Table` | "Table" | "טבלה" | View toggle |
| `Month` | "Month" | "חודש" | Mode toggle |
| `Week` | "Week" | "שבוע" | Mode toggle |
| `Day` | "Day" | "יום" | Mode toggle |
| `Previous` | "Previous" | "הקודם" | Date navigation |
| `Next` | "Next" | "הבא" | Date navigation |
| `RequestTimeOff` | "Request Time Off" | "בקש חופש" | Header action (Employee) |
| `AssignShifts` | "Assign Shifts" | "שבץ משמרות" | Header action (Manager) |
| `OpenSchedule` | "Open Schedule" | "פתח לוח זמנים" | CTA button |

### Priority 3: Home Page Dashboard

These keys are needed for Phase 3 (Home Page):

| Key | Proposed English | Proposed Hebrew | Usage |
|-----|------------------|-----------------|-------|
| `NextShift` | "Next Shift" | "המשמרת הבאה" | Dashboard card title |
| `NoUpcomingShifts` | "No upcoming shifts" | "אין משמרות קרובות" | Empty state |
| `ViewAllNotifications` | "View all notifications" | "צפה בכל ההתראות" | CTA |
| `Unread` | "Unread" | "לא נקראו" | Notification badge |
| `HoursThisWeek` | "Hours This Week" | "שעות השבוע" | Stat label |
| `DaysWithShifts` | "Days with Shifts" | "ימים עם משמרות" | Stat label |
| `DaysOff` | "Days Off" | "ימי חופש" | Stat label |
| `ViewFullSchedule` | "View Full Schedule" | "צפה בלוח זמנים מלא" | CTA |
| `MyRequests` | "My Requests" | "הבקשות שלי" | Card title |
| `GoToMyRequests` | "Go to My Requests" | "עבור לבקשות שלי" | CTA |
| `PendingRequests` | "Pending Requests" | "בקשות ממתינות" | Stat label |
| `ApprovedRequests` | "Approved Requests" | "בקשות מאושרות" | Stat label |
| `DeclinedRequests` | "Declined Requests" | "בקשות נדחות" | Stat label |
| `StaffingOverview` | "Staffing Overview" | "סקירת כוח אדם" | Manager card title |
| `UnassignedShifts` | "Unassigned Shifts" | "משמרות לא משובצות" | Stat label |
| `UnderstaffedDays` | "Understaffed Days" | "ימים עם מחסור בכוח אדם" | Stat label |
| `OpenSchedulingWorkspace` | "Open Scheduling Workspace" | "פתח סביבת תזמון" | CTA |
| `Approvals` | "Approvals" | "אישורים" | Card title |
| `PendingTimeOff` | "Pending Time Off" | "בקשות חופש ממתינות" | Stat label |
| `PendingSwaps` | "Pending Swaps" | "בקשות החלפה ממתינות" | Stat label |
| `OpenApprovals` | "Open Approvals" | "פתח אישורים" | CTA |
| `CompaniesOverview` | "Companies Overview" | "סקירת חברות" | Director/Owner card |
| `NextDayOff` | "Next Day Off" | "יום החופש הבא" | Stat label |

### Priority 4: Command Palette

These keys are needed for Phase 13 (Command Palette):

| Key | Proposed English | Proposed Hebrew | Usage |
|-----|------------------|-----------------|-------|
| `SearchPlaceholder` | "Type to search pages and people..." | "הקלד לחיפוש דפים ואנשים..." | Input placeholder |
| `RecentPages` | "Recent Pages" | "דפים אחרונים" | Section title |
| `NoResults` | "No results found" | "לא נמצאו תוצאות" | Empty state |
| `QuickNavigation` | "Quick Navigation" | "ניווט מהיר" | Modal title |

### Priority 5: General Actions & States

These keys might already exist (verify) or need to be added:

| Key | Proposed English | Proposed Hebrew | Usage |
|-----|------------------|-----------------|-------|
| `Create` | "Create" | "צור" | Action button |
| `Search` | "Search" | "חפש" | Action button |
| `Filter` | "Filter" | "סנן" | Action button |
| `ViewDetails` | "View Details" | "צפה בפרטים" | Action link |
| `Back` | "Back" | "חזור" | Navigation |
| `Assigned` | "Assigned" | "משובץ" | Status |
| `Unassigned` | "Unassigned" | "לא משובץ" | Status |
| `Loading` | "Loading..." | "טוען..." | Loading state |
| `Error` | "Error" | "שגיאה" | Error state |
| `Success` | "Success" | "הצלחה" | Success state |

---

## Verification Needed

### Existing Keys to Double-Check

Before adding new keys, verify if these already exist with different names:

1. **"Home"** - Might exist as "HomePage" or "Dashboard"
2. **"Table"** - Verify if distinct from "TableView"
3. **"Month" / "Week" / "Day"** - Verify if exist standalone (not just "MonthView")
4. **"Companies"** - Might exist in a different form
5. **"People"** - Check if "Users" can be reused
6. **"Settings"** - Might exist as "Configuration"

### Keys That Might Be Combined

Consider if we can reuse existing keys:

- `Chores` (existing) can be used for calendar selector instead of new `ChoresCalendar`
- `MyShifts` (existing) can be used for calendar selector
- `OnDuty` (existing) can be used for calendar selector
- `Users` (existing) might work instead of `People`
- `Config` (existing) might work instead of `Settings`

---

## Implementation Strategy

### Phase-by-Phase Key Addition

**Phase 0 (Current)**: Audit complete

**Phase 1**: No new keys needed (CSS only)

**Phase 2**: Add Priority 1 keys (Navigation & Layout)
- Home, People, Settings, Companies, MyTeam, MyProfile

**Phase 3**: Add Priority 3 keys (Home Page Dashboard)
- All dashboard-related keys (NextShift, StaffingOverview, etc.)

**Phase 4**: Add Priority 2 keys (Schedule Workspace)
- Calendars, view toggles, actions

**Phase 13**: Add Priority 4 keys (Command Palette)
- SearchPlaceholder, RecentPages, NoResults

**Other Phases**: Add general action keys as needed

### Adding New Keys Process

1. **For each new key**:
   - Add to `Resources/SharedResources.resx` (English)
   - Add to `Resources/SharedResources.he-IL.resx` (Hebrew)
   - Use consistent PascalCase naming
   - Include descriptive comment if needed

2. **Example .resx entry**:
```xml
<data name="Home" xml:space="preserve">
  <value>Home</value>
</data>
```

3. **Hebrew equivalent**:
```xml
<data name="Home" xml:space="preserve">
  <value>בית</value>
</data>
```

---

## Hebrew Translation Guidelines

### Key Principles

1. **RTL Consideration**: Hebrew text will automatically flow right-to-left
2. **Formal vs. Informal**: Use formal language (אתה form where needed)
3. **Gender Neutrality**: Where possible, use gender-neutral forms
4. **Technical Terms**: Some English terms (like "Dashboard", "Analytics") may be kept in English or transliterated

### Common Translations Reference

| English | Hebrew | Notes |
|---------|--------|-------|
| Home | בית | Literal "house/home" |
| Schedule | לוח זמנים | "Time table" |
| Calendar | לוח שנה | "Year table" |
| Requests | בקשות | Plural of "request" |
| Shifts | משמרות | Plural of "shift" |
| My... | ...שלי | Possessive suffix |
| The... | ה... | Definite article prefix |
| View | צפה / תצוגה | Verb / Noun |
| Create | צור / יצירה | Verb / Noun |
| Edit | ערוך / עריכה | Verb / Noun |
| Delete | מחק / מחיקה | Verb / Noun |
| Save | שמור / שמירה | Verb / Noun |
| Cancel | בטל / ביטול | Verb / Noun |
| Approve | אשר / אישור | Verb / Noun |
| Decline | דחה / דחייה | Verb / Noun |
| Pending | ממתין | "Waiting" |
| Approved | מאושר | "Confirmed" |
| Active | פעיל | "Active/Operating" |
| Assigned | משובץ | "Scheduled/Placed" |
| Unassigned | לא משובץ | "Not scheduled" |

### Placeholder Syntax in Hebrew

When translating strings with placeholders like `{0}`:
- English: `"Good morning, {0}!"`
- Hebrew: `"!{0} ,בוקר טוב"` (Note: Order reverses due to RTL)

---

## Quality Checklist

Before marking localization complete for each phase:

- [ ] All new keys added to SharedResources.resx (English)
- [ ] All new keys added to SharedResources.he-IL.resx (Hebrew)
- [ ] No hardcoded English strings in .cshtml files
- [ ] No hardcoded English strings in .cs files (PageModels, services)
- [ ] Tested page in Hebrew (he-IL culture)
- [ ] Verified RTL layout works with localized text
- [ ] Verified no truncation issues (Hebrew text might be longer/shorter)
- [ ] Verified pluralization handles correctly (if applicable)
- [ ] Verified date/time formatting respects culture

---

## Next Steps

1. ✅ **Phase 0 Complete**: Audit finished, missing keys identified
2. **Phase 1** (CSS): No localization changes needed
3. **Phase 2** (Layout): Add Priority 1 keys before starting layout work
4. **Phase 3** (Home): Add Priority 3 keys before creating Home page
5. **Phase 4** (Schedule): Add Priority 2 keys before creating Schedule workspace

---

**Last Updated**: 2025-11-17
**Reviewed By**: Development Team
**Status**: Audit Complete - Ready for key addition
