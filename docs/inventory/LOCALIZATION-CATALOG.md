# Localization Key Catalog

This document catalogs all localization keys used in the ShiftManager application.

## Overview

- **Supported Languages**: English (en-US), Hebrew (he-IL)
- **Resource Files**:
  - `Resources/SharedResources.resx` (English - base)
  - `Resources/SharedResources.he-IL.resx` (Hebrew with RTL support)
- **Localization Mechanism**: Custom `<loc key="...">` tag helper in Razor Pages

## RTL (Right-to-Left) Considerations

The Hebrew localization includes RTL-specific settings:
- `Dir` key: `rtl` (Hebrew) vs `ltr` (English)
- `Culture` key: `he-IL` (Hebrew) vs `en-US` (English)
- UI components automatically adjust text alignment and layout direction based on these values

---

## Key Catalog by Category

### Application Core

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| ShiftManager | Shift Manager | מנהל משמרות | Layout, Branding |
| Culture | en-US | he-IL | System Configuration |
| Dir | ltr | rtl | Layout Direction |
| Language | Language | שפה | Language Switcher |
| English | English | English | Language Switcher |
| Hebrew | עברית | עברית | Language Switcher |

### Navigation & Menu

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Home | Home | בית | Sidebar, Navigation |
| Calendar | Calendar | לוח שנה | Sidebar, Navigation |
| Calendars | Calendars | לוחות שנה | Sidebar |
| MySchedule | My Schedule | לוח הזמנים שלי | Dashboard |
| Schedule | Schedule | לוח זמנים | Navigation |
| Requests | Requests | בקשות | Sidebar, Navigation |
| MyRequests | My Requests | הבקשות שלי | Requests Page |
| Admin | Admin | ניהול | Sidebar |
| Users | Users | משתמשים | Admin Section |
| Settings | Settings | הגדרות | Sidebar |
| Config | Config | הגדרות | Admin Section |
| Notifications | Notifications | התראות | Header, Sidebar |
| Logout | Logout | התנתקות | Header |
| People | People | אנשים | Sidebar |
| Analytics | Analytics | אנליטיקה | Sidebar |
| AuditLog | Audit Log | יומן ביקורת | Admin Section |

### Calendar Views

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| MonthView | Month View | תצוגת חודש | Calendar Page |
| WeekView | Week View | תצוגת שבוע | Calendar Page |
| DayView | Day View | תצוגת יום | Calendar Page |
| TableView | Table View | תצוגת טבלה | Calendar Page |
| Month | Month | חודש | Calendar Navigation |
| Week | Week | שבוע | Calendar Navigation |
| Day | Day | יום | Calendar Navigation |
| Table | Table | טבלה | Calendar Navigation |
| Today | Today | היום | Calendar Navigation |
| Tomorrow | Tomorrow | מחר | Calendar |
| Yesterday | Yesterday | אתמול | Calendar |
| ThisWeek | This Week | השבוע | Calendar |
| NextWeek | Next Week | השבוע הבא | Calendar |
| ThisMonth | This Month | החודש | Calendar |
| NextMonth | Next Month | החודש הבא | Calendar |
| PreviousWeek | Previous Week | שבוע קודם | Calendar Navigation |
| CurrentWeek | Current Week | שבוע נוכחי | Calendar Navigation |
| PreviousTwoWeeks | Previous 2 Weeks | שבועיים קודמים | Table View |
| NextTwoWeeks | Next 2 Weeks | שבועיים הבאים | Table View |

### Days of Week

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Sunday | Sunday | יום ראשון | Calendar |
| Monday | Monday | יום שני | Calendar |
| Tuesday | Tuesday | יום שלישי | Calendar |
| Wednesday | Wednesday | יום רביעי | Calendar |
| Thursday | Thursday | יום חמישי | Calendar |
| Friday | Friday | יום שישי | Calendar |
| Saturday | Saturday | יום שבת | Calendar |
| Sun | Sun | יום א' | Calendar (Short) |
| Mon | Mon | יום ב' | Calendar (Short) |
| Tue | Tue | יום ג' | Calendar (Short) |
| Wed | Wed | יום ד' | Calendar (Short) |
| Thu | Thu | יום ה' | Calendar (Short) |
| Fri | Fri | יום ו' | Calendar (Short) |
| Sat | Sat | שבת | Calendar (Short) |
| SunShort | Sun | א׳ | Calendar (Very Short) |
| MonShort | Mon | ב׳ | Calendar (Very Short) |
| TueShort | Tue | ג׳ | Calendar (Very Short) |
| WedShort | Wed | ד׳ | Calendar (Very Short) |
| ThuShort | Thu | ה׳ | Calendar (Very Short) |
| FriShort | Fri | ו׳ | Calendar (Very Short) |
| SatShort | Sat | ש׳ | Calendar (Very Short) |

### Months

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| January | January | ינואר | Calendar |
| February | February | פברואר | Calendar |
| March | March | מרץ | Calendar |
| April | April | אפריל | Calendar |
| May | May | מאי | Calendar |
| June | June | יוני | Calendar |
| July | July | יולי | Calendar |
| August | August | אוגוסט | Calendar |
| September | September | ספטמבר | Calendar |
| October | October | אוקטובר | Calendar |
| November | November | נובמבר | Calendar |
| December | December | דצמבר | Calendar |
| Jan | Jan | ינו׳ | Calendar (Short) |
| Feb | Feb | פבר׳ | Calendar (Short) |
| Mar | Mar | מרץ | Calendar (Short) |
| Apr | Apr | אפר׳ | Calendar (Short) |
| Jun | Jun | יוני | Calendar (Short) |
| Jul | Jul | יולי | Calendar (Short) |
| Aug | Aug | אוג׳ | Calendar (Short) |
| Sep | Sep | ספט׳ | Calendar (Short) |
| Oct | Oct | אוק׳ | Calendar (Short) |
| Nov | Nov | נוב׳ | Calendar (Short) |
| Dec | Dec | דצמ׳ | Calendar (Short) |

### Shift Types

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| MORNING | Morning | בוקר | Shift Assignment |
| NOON | Noon | צהריים | Shift Assignment |
| NIGHT | Night | לילה | Shift Assignment |
| MIDDLE | Afternoon | אחר הצהריים | Shift Assignment |
| ShiftType_MORNING | Morning | בוקר | Shift Types |
| ShiftType_NOON | Noon | צהריים | Shift Types |
| ShiftType_NIGHT | Night | לילה | Shift Types |
| ShiftType_MIDDLE | Middle | אמצע | Shift Types |
| ShiftType_OFFLINE | Offline | אופליין | Shift Types |
| ShiftTypes | Scheduled Shift Types | סוגי משמרות הפקה וב"ר | Admin |
| ScheduledShifts | Scheduled Shifts | משמרות הפקה וב"ר | Admin |
| DayShifts | Day Shifts | משמרות רוחב | Admin |

### Shifts & Assignments

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Shift | Shift | משמרת | General |
| ShiftName | Shift Name | שם המשמרת | Assignment Modal |
| AssignShift | Assign Shift | הקצה משמרת | Calendar |
| AssignShifts | Assign Shifts | שבץ משמרות | Calendar |
| AddShift | Add Shift | הוספת משמרת | Calendar |
| DeleteShift | Delete shift | מחיקת משמרת | Table View |
| Assignments | Assignments | שיבוצים | Manage Page |
| AssignedUsers | Assigned Users | משתמשים שהוקצו | Modal |
| Assigned | Assigned | משובץ | Analytics |
| SelectUser | Select User | בחר משתמש | Assignment |
| AssignUser | Assign User | שבץ משתמש | Table View |
| RemoveUser | Remove User | הסר משתמש | Table View |
| ClickToAssign | Click to assign | לחץ לשיבוץ | Table View |
| StaffingRequired | Staffing Required | צוות נדרש | Assignment |
| Staffing | Staffing | כוח אדם | Calendar |
| FullyStaffed | ✓ Fully Staffed | ✓ מאויש במלואו | Calendar |
| NeedsMore | ⚠ Needs {0} more | ⚠ נדרשים {0} נוספים | Calendar |
| NoStaffingRequired | No staffing required | אין צורך בכוח אדם | Calendar |
| Staffed | staffed | מאוישים | Calendar |

### Trainee System

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Trainee | Trainee | מתלמד | Table View |
| TraineeLabel | Trainee | מתלמד | My Shifts |
| AddTrainee | Add Trainee | הוסף מתלמד | Table View |
| ChangeTrainee | Change Trainee | שנה מתלמד | Table View |
| RemoveTrainee | Remove Trainee | הסר מתלמד | Table View |
| AssignTrainee | Assign Trainee | שבץ מתלמד | Assignments |
| AttachTrainee | -- Attach Trainee -- | -- צרף מתלמד -- | Assignments |
| Shadowing | Shadowing | צל | My Shifts |

### Time Off & Requests

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| TimeOffRequest | Time Off Request | בקשת חופשה | Requests |
| TimeOffRequests | Time-Off Requests | בקשות חופש | Analytics |
| RequestTimeOff | Request Time Off | בקשת חופשה | Requests Page |
| PendingRequests | Pending Requests | בקשות ממתינות | Dashboard |
| ApprovedRequests | Approved Requests | בקשות מאושרות | Dashboard |
| DeclinedRequests | Declined Requests | בקשות שנדחו | Dashboard |
| PendingTimeOff | Pending Time Off | בקשות חופש ממתינות | Dashboard |
| PendingSwaps | Pending Swaps | החלפות ממתינות | Dashboard |
| SwapRequests | Swap Requests | בקשות החלפה | Analytics |
| RequestShiftSwap | Request Shift Swap | בקשת החלפת משמרת | Requests |
| MyTimeOffRequests | My Time Off Requests | בקשות החופשה שלי | Requests |
| MySwapRequests | My Swap Requests | בקשות ההחלפה שלי | Requests |
| NewRequest | New Request | בקשה חדשה | Requests |
| Approve | Approve | אשר | Requests |
| Decline | Decline | דחה | Requests |
| Approvals | Approvals | אישורים | Dashboard |
| VacationType | Vacation Type | סוג חופשה | Requests |
| RegularVacation | Regular Vacation | חופשה רגילה | Requests |
| AfterDutyVacation | After-Duty Vacation | אפטר | Requests |
| Vacation | Vacation | חופשה | Overview |
| Vacations | Vacations | חופשות | Overview |
| TimeOff | Time-Off | חופשה | Breadcrumb |
| DaysOff | Days Off | ימי חופש | Dashboard |
| NextDayOff | Next Day Off | יום חופש הבא | Dashboard |

### User Roles

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Role | Role | תפקיד | User Management |
| Employee | Employee | עובד | Roles |
| Manager | Manager | מנהל | Roles |
| AdminRole | Admin | מנהל מערכת | Roles |
| Director | Director | מנהל אזורי | Roles |
| Directors | Directors | מנהלים אזוריים | Admin |
| Owner | Owner | בעלים | Roles |
| Trainee | Trainee | מתלמד | Roles |

### Status Values

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Status | Status | סטטוס | General |
| Active | Active | פעיל | Status |
| Inactive | Inactive | לא פעיל | Status |
| Pending | Pending | ממתין | Status |
| Approved | Approved | מאושר | Status |
| Declined | Declined | נדחה | Status |
| Submitted | Submitted | נשלח | Status |
| Rejected | Rejected | נדחה | Status |

### Common Actions

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Save | Save | שמור | Forms |
| SaveChanges | Save Changes | שמור שינויים | Forms |
| Cancel | Cancel | ביטול | Forms, Modals |
| Delete | Delete | מחק | Actions |
| Edit | Edit | עריכה | Actions |
| Add | Add | הוסף | Actions |
| Remove | Remove | הסר | Actions |
| Submit | Submit | שלח | Forms |
| Back | Back | חזור | Navigation |
| Close | Close | סגור | Modals |
| Search | Search | חיפוש | Search |
| Filter | Filter | סינון | Filters |
| Apply | Apply | החל | Filters |
| Clear | Clear | נקה | Filters |
| Confirm | Confirm | אישור | Dialogs |
| OK | OK | אישור | Dialogs |
| Rename | Rename | שנה שם | Actions |
| Revoke | Revoke | בטל | Actions |
| Set | Set | הגדר | Actions |

### Date & Time

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Date | Date | תאריך | Forms |
| Dates | Dates | תאריכים | Requests |
| Time | Time | שעה | Forms |
| StartTime | Start Time | שעת התחלה | Shifts |
| EndTime | End Time | שעת סיום | Shifts |
| StartDate | Start Date | תאריך התחלה | Requests |
| EndDate | End Date | תאריך סיום | Requests |
| Duration | Duration | משך זמן | Shifts |
| DateRange | Date Range | טווח תאריכים | Filters |
| Timestamp | Timestamp | חותמת זמן | Audit Log |

### Form Labels

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Name | Name | שם | Forms |
| Email | Email | אימייל | Forms |
| Password | Password | סיסמה | Forms |
| DisplayName | Display name | שם תצוגה | Forms |
| PreferredName | Preferred Name | שם מועדף | Profile |
| Phone | Phone | טלפון | Profile |
| City | Living Place | מגורים | Profile |
| DateOfBirth | Date of Birth | תאריך לידה | Profile |
| Department | Department | מחלקה | Profile |
| JobTitle | Job Title | תפקיד | Profile |
| HireDate | Hire Date | תאריך תחילת עבודה | Profile |
| Reason | Reason | סיבה | Requests |
| Description | Description | תיאור | General |
| Message | Message | הודעה | Notifications |
| Type | Type | סוג | General |
| Details | Details | פרטים | General |
| Notes | Notes | הערות | General |

### Profile & Account

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| MyProfile | My Profile | הפרופיל שלי | Sidebar |
| EditProfile | Edit Profile | ערוך פרופיל | Profile Page |
| PersonalInformation | Personal Information | מידע אישי | Profile |
| ProfessionalInformation | Professional Information | מידע מקצועי | Profile |
| EmergencyContact | Emergency Contact | איש קשר לחירום | Profile |
| EmergencyContactName | Emergency Contact Name | שם איש קשר לחירום | Profile |
| EmergencyContactPhone | Emergency Contact Phone | טלפון איש קשר לחירום | Profile |
| EmergencyContactRelation | Relationship | קרבה | Profile |
| Avatar | Avatar | תמונת פרופיל | Profile |
| UploadAvatar | Upload Avatar | העלה תמונה | Profile |
| DeleteAvatar | Delete Avatar | מחק תמונה | Profile |
| AccountSettings | Account Settings | הגדרות חשבון | Profile |
| ActiveAccount | Active Account | חשבון פעיל | Profile |
| Skills | Skills | כישורים | Profile |
| Certifications | Certifications | הסמכות | Profile |

### Authentication

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Login | Login | התחברות | Auth Page |
| Logout | Logout | התנתקות | Header |
| AccessDenied | Access Denied | גישה נדחתה | Error Page |
| Unauthorized | You don't have permission... | אין לך הרשאה לגשת... | Error Page |
| ForgotPassword | Forgot Password | שכחתי סיסמה | Auth |
| ResetPassword | Reset Password | איפוס סיסמה | Auth |
| ChangePassword | Change Password | שינוי סיסמה | Profile |
| OldPassword | Current Password | סיסמה נוכחית | Password Change |
| NewPassword | New Password | סיסמה חדשה | Password Change |
| RequestAccess | Request Access | בקש גישה | Signup |
| LoginWithADFS | Login with ADFS | התחברות עם ADFS | Login |

### Notifications

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| NotificationCenter | Notification Center | מרכז התראות | Header |
| NotificationHub | Notification Hub | מרכז התראות | Director Panel |
| NoNotifications | No Notifications | אין התראות | Notifications |
| AllCaughtUp | You're all caught up!... | אתה מעודכן!... | Notifications |
| MarkAsRead | Mark as Read | סמן כנקרא | Notifications |
| MarkAllAsRead | Mark All as Read ({0}) | סמן הכל כנקרא ({0}) | Notifications |
| Unread | Unread | לא נקרא | Notifications |
| Read | Read | נקרא | Notifications |
| ViewAllNotifications | View all notifications | הצג את כל ההתראות | Dashboard |

### Companies

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Company | Company | חברה | General |
| Companies | Companies | חברות | Owner Panel |
| CompanyName | Company Name | שם החברה | Companies |
| CompanyDetails | Company Details | פרטי החברה | Companies |
| AddNewCompany | Add New Company | הוסף חברה חדשה | Companies |
| AllCompanies | All Companies | כל החברות | Companies |
| TotalCompanies | Total Companies | סך כל החברות | Dashboard |
| ActiveCompanies | Active Companies | חברות פעילות | Dashboard |
| ManageCompanies | Manage Companies | נהל חברות | Dashboard |
| RenameCompany | Rename Company | שנה שם חברה | Companies |
| DeleteCompany | Delete Company | מחק חברה | Companies |
| Slug | Slug | מזהה | Companies |
| CompanyId | Company ID | מזהה חברה | Diagnostics |

### Director Management

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| DirectorAssignments | Director Assignments | הקצאות מנהלים | Owner Panel |
| AssignDirectorToCompany | Assign Director to Company | הקצה מנהל לחברה | Directors |
| SelectDirector | -- Select Director -- | -- בחר מנהל -- | Directors |
| CurrentDirectorAssignments | Current Director Assignments | הקצאות מנהלים נוכחיות | Directors |
| GrantedBy | Granted By | הוענק על ידי | Directors |
| GrantedAt | Granted At | הוענק ב | Directors |
| NoDirectorAssignments | No director assignments yet... | אין הקצאות מנהלים עדיין... | Directors |
| AboutDirectorRole | About Director Role: | אודות תפקיד המנהל: | Directors |
| DirectorsCanOversee | Directors can oversee multiple... | מנהלים יכולים לפקח על מספר... | Directors |

### Chores

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Chores | Chores | מטלות | Sidebar |
| Chore | Chore | מטלה | Calendar |
| ChoresCalendar | Chores Calendar | לוח מטלות | Calendar |
| CreateChore | Create Chore | צור מטלה | Calendar |
| ChoreDetails | Chore Details | פרטי מטלה | Modal |
| ChoresList | Chores List | רשימת מטלות | Calendar |
| NoChoresThisMonth | No chores for this month | אין מטלות לחודש זה | Calendar |

### On-Duty / Day Shifts

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| OnDuty | Day Shift | משמרת יומית | Calendar |
| OnDutyCalendar | Day Shift Calendar | לוח משמרות רוחב | Calendar |
| OnDutyType | Day Shift Type | סוג משמרת יומית | Calendar |
| OnDutyTypes | Day Shift Types | סוגי משמרות רוחב | Config |
| CreateOnDuty | Create Day Shift | יצירת משמרת יומית | Calendar |
| CancelOnDuty | Cancel Day Shift | ביטול משמרת יומית | Calendar |
| OnDutyList | Day Shift List | רשימת משמרות רוחב | Calendar |
| NoOnDutyThisMonth | No day shift assignments this month | אין משמרות רוחב החודש | Calendar |
| Hakam | Hakam | חק״מכו | On-Duty Types |
| Lead | Lead | מובילתו | On-Duty Types |

### Analytics & Reports

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| AnalyticsDashboard | Analytics Dashboard | לוח בקרה אנליטי | Analytics |
| CoverageRate | Coverage Rate | שיעור כיסוי | Analytics |
| HoursWorked | Hours Worked | שעות עבודה | Analytics |
| TotalHours | Total Hours | סה"כ שעות | Analytics |
| ShiftCount | Shift Count | מספר משמרות | Analytics |
| ApprovalRate | Approval Rate | שיעור אישור | Analytics |
| UnderstaffingIssues | Understaffing Issues | בעיות תת-איוש | Analytics |
| OverstaffingIssues | Overstaffing Issues | בעיות איוש-יתר | Analytics |
| ExportReport | Export Report | ייצא דוח | Analytics |
| ExportCSV | Export CSV | ייצא CSV | Analytics |
| Last7Days | Last 7 Days | 7 ימים אחרונים | Filters |
| Last30Days | Last 30 Days | 30 ימים אחרונים | Filters |
| Last90Days | Last 90 Days | 90 ימים אחרונים | Filters |
| LastYear | Last Year | שנה אחרונה | Filters |
| NoDataAvailable | No data available | אין נתונים זמינים | Analytics |

### Pagination

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Previous | Previous | קודם | Pagination |
| Next | Next | הבא | Pagination |
| First | First | ראשון | Pagination |
| Last | Last | אחרון | Pagination |
| Page | Page | עמוד | Pagination |
| Of | of | מתוך | Pagination |
| Showing | Showing | מציג | Pagination |
| Records | records | רשומות | Pagination |

### Feedback System

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Feedback | Feedback | משוב | Sidebar |
| SubmitFeedback | Submit Feedback | שליחת משוב | Feedback |
| FeedbackType | Feedback Type | סוג המשוב | Feedback |
| FeedbackTypeError | Error | שגיאה | Feedback |
| FeedbackTypeSuggestion | Suggestion / Improvement | הצעה לייעול | Feedback |
| FeedbackContent | Feedback Content | תוכן המשוב | Feedback |
| FeedbackSubmitted | Feedback submitted successfully... | המשוב נשלח בהצלחה... | Feedback |
| FeedbackList | Feedback List | רשימת משובים | Feedback |
| NoFeedback | No feedback submitted yet. | עדיין לא נשלחו משובים. | Feedback |

### My Team Calendars

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| MyTeam | My Team | הצוות שלי | Calendar |
| MyCalendars | My Calendars | הלוחות שלי | Calendar |
| TeamMember | Team Member | חבר צוות | Calendar |
| NoTeamMembers | No Team Members | אין חברים בלוח זה | Calendar |
| ConfigureMembers | Configure Members | הגדרת חברי צוות | Calendar |
| SwitchCalendar | Switch Calendar | החלפת לוח | Calendar |
| NewCalendar | New Calendar | לוח צוות חדש | Calendar |
| CreateCalendar | Create Calendar | צור לוח | Calendar |
| DeleteCalendar | Delete Calendar | מחק לוח | Calendar |
| RenameCalendar | Rename Calendar | שנה שם לוח | Calendar |
| CalendarName | Calendar Name | שם הלוח | Calendar |
| CurrentMembers | Current Members | חברי הצוות הנוכחיים | Calendar |
| AvailableUsers | Available Users | משתמשי החברה שלא בצוות | Calendar |

### Game (Easter Egg)

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Game_Title | Shift Swap | החלפת משמרות | Game |
| Game_Instructions | Swap adjacent icons to line up 3+ | החלף אייקונים סמוכים... | Game |
| Game_Score | Score: | ניקוד: | Game |
| Game_Trophy | Leaderboard | לוח מובילים | Game |
| Game_PlayAgain | Play Again | שחק שוב | Game |
| Game_LeaderboardTitle | Game Leaderboard | לוח מובילי המשחק | Game |
| Game_AllTime | All-Time | כל הזמנים | Game |
| Game_Monthly | This Month | החודש | Game |

### Owner Panel

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| Owner_AdminPanel | Owner Administration | ניהול מערכת - בעלים | Owner |
| Owner_TotalUsers | Total Users | סה"כ משתמשים | Owner |
| Owner_TotalCompanies | Total Companies | סה"כ חברות | Owner |
| Owner_DatabaseSize | Database Size | גודל מסד נתונים | Owner |
| Owner_Uptime | Uptime | זמן פעילות | Owner |
| Owner_FeatureFlags | Feature Flags | דגלי תכונות | Owner |
| Owner_DatabaseConsole | Database Console | קונסולת מסד נתונים | Owner |
| Owner_EmailConfig | Email Configuration | הגדרות דוא"ל | Owner |
| Owner_SystemHealth | System Health | בריאות המערכת | Owner |
| Owner_Backup | Backup & Restore | גיבוי & שחזור | Owner |
| Owner_SecurityAudit | Security Audit | ביקורת אבטחה | Owner |
| Owner_GriffinConfig | Griffin ADFS | Griffin ADFS | Owner |
| Owner_GameConfig | Game Configuration | הגדרות משחק | Owner |

### Settings Page

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| My_Settings | My Settings | הגדרות שלי | Settings |
| Settings_DailyDigest | Daily Digest Notifications | התראות סיכום יומי | Settings |
| Settings_ReceiveDailyDigest | Receive Daily Digest Emails | קבלת אימיילים יומיים | Settings |
| Settings_PreferredTime | Preferred Delivery Time (UTC) | שעת משלוח מועדפת | Settings |
| Settings_IncludeShifts | Upcoming Shifts | משמרות קרובות | Settings |
| Settings_IncludeRequests | Pending Requests | בקשות ממתינות | Settings |
| Settings_IncludeChores | Assigned Chores | מטלות מוקצות | Settings |
| Settings_IncludeOnDuty | OnDuty Assignments | משמרות יומיות | Settings |
| SaveSettings | Save Settings | שמור הגדרות | Settings |
| Settings_DayBeforeReminders | Day-Before Reminders | תזכורות יום לפני | Settings |

### Error Messages

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| ErrorOccurred | An error occurred. Please try again. | אירעה שגיאה. אנא נסה שנית. | General |
| SomethingWentWrong | Something went wrong | משהו השתבש | Error Page |
| UnexpectedErrorOccurred | Sorry—an unexpected error occurred. | מצטערים - אירעה שגיאה בלתי צפויה. | Error Page |
| Error_InvalidInput | Invalid input. | קלט לא תקין. | Validation |
| Error_InvalidEmailFormat | Invalid email format. | פורמט אימייל לא תקין. | Validation |
| Error_UserNotFound | User not found | משתמש לא נמצא | Auth |
| Error_Login_InvalidCredentials | Invalid credentials. | פרטי התחברות שגויים. | Login |
| Error_Login_AccountLocked | Account is locked... | החשבון נעול... | Login |

### Success Messages

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| SuccessMessage | Operation completed successfully. | הפעולה הושלמה בהצלחה. | General |
| ProfileUpdatedSuccessfully | Profile updated successfully! | הפרופיל עודכן בהצלחה! | Profile |
| PasswordChangedSuccessfully | Password changed successfully! | הסיסמה שונתה בהצלחה! | Password |
| CompanyDeleted | Company deleted successfully | החברה נמחקה בהצלחה | Companies |
| CompanyRenamed | Company renamed successfully | החברה שונתה בהצלחה | Companies |

### Greeting Messages

| Key | English Text | Hebrew Text | Used In |
|-----|--------------|-------------|---------|
| WelcomeBack | Welcome back, {0}! | ברוך שובך, {0}! | Dashboard |
| GoodMorning | Good morning, {0}! | בוקר טוב, {0}! | Dashboard |
| GoodAfternoon | Good afternoon, {0}! | צהריים טובים, {0}! | Dashboard |
| GoodEvening | Good evening, {0}! | ערב טוב, {0}! | Dashboard |
| WelcomeToShiftManager | Welcome to Shift Manager | ברוכים הבאים למנהל משמרות | Home |

---

## Missing Translations

Both resource files are comprehensive and maintain parity. No missing translations were identified between the English and Hebrew files.

## Notes

1. **Format Placeholders**: Keys with `{0}`, `{1}`, etc. are format strings that accept runtime parameters
2. **HTML Content**: Some keys (e.g., `Login_RequestAccessPrompt`) contain HTML markup for links
3. **Emoji Usage**: Some keys include emoji characters (e.g., `Game_MilestoneReached` contains emoji)
4. **Domain-Specific Terms**: Certain Hebrew translations use domain-specific terminology (e.g., "חק״מכו" for Hakam, "מובילתו" for Lead)

## File Locations

- English (Base): `C:\Users\katzi\Downloads\ShiftManager\Resources\SharedResources.resx`
- Hebrew: `C:\Users\katzi\Downloads\ShiftManager\Resources\SharedResources.he-IL.resx`
- Usage in Pages: `Pages/**/*.cshtml` using `<loc key="KeyName" />` tag helper

---

*Generated: 2026-02-01*
*Task: B-043 Localization Key Catalog*
