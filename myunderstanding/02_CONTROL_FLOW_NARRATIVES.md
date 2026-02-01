# ShiftManager Control Flow Narratives

**Generated:** 2026-01-12
**Agent:** Antigravity Workspace Intake

---

## 1. User Authentication Flow

### Login with Email/Password

**Narrative:**
1. User navigates to `/Auth/Login`
2. System checks if already authenticated → redirect to home if yes
3. User enters email and password
4. `OnPostAsync()` validates credentials:
   - Query user by email (with tenant filter)
   - Verify password using PBKDF2 (100k iterations)
   - If valid, create `ClaimsPrincipal` with user info
   - Sign in via `HttpContext.SignInAsync()`
5. Create auth cookie (`shiftmgr.auth`, 7-day expiry)
6. Redirect to `/Calendar/Month` or `returnUrl`

**Evidence:** `Pages/Auth/Login.cshtml.cs:16444 bytes`

### Login with Griffin ADFS

**Narrative:**
1. User clicks "Login with ADFS" button on login page
2. System redirects to Griffin ADFS service
3. User authenticates with organizational credentials
4. Griffin redirects back to `/Auth/GriffinCallback` with token
5. `GriffinCallback.OnGetAsync()` validates token
6. If user doesn't exist and auto-provision enabled:
   - Create new `AppUser` with default role (Employee)
7. Sign in and redirect to home

**Evidence:** `Pages/Auth/GriffinCallback.cshtml.cs:4467 bytes`

---

## 2. Shift Assignment Flow

### Assigning Employee to Shift

**Narrative:**
1. Manager navigates to `/Assignments/Manage?id={shiftInstanceId}`
2. Page loads:
   - Current shift instance details
   - Existing assignments
   - Available employees (same company, active)
3. Manager selects employee from dropdown
4. `OnPostAssignAsync()`:
   - Validate employee belongs to same company
   - Check for conflicts (time-off, existing shifts)
   - Create `ShiftAssignment` record
   - `CompanyIdInterceptor` auto-injects CompanyId
5. `NotificationService.CreateShiftAddedNotificationAsync()`
6. Redirect back to assignment page

**Evidence:** `Pages/Assignments/Manage.cshtml.cs` (referenced in `docs/context.md:521`)

### Assigning Trainee to Shadow

**Narrative:**
1. Manager clicks "Assign Trainee" on existing assignment
2. `TraineeService.ValidateTraineeAssignmentAsync()`:
   - Verify user has Trainee role
   - Verify same company
   - Check no time conflicts
   - Check shift doesn't already have trainee
3. Update `ShiftAssignment.TraineeUserId`
4. Create notifications for:
   - Trainee: "You're shadowing [Employee] on [Date]"
   - Primary employee: "[Trainee] will shadow you on [Date]"

**Evidence:** `Services/TraineeService.cs:16686 bytes`

---

## 3. Time-Off Request Flow

### Employee Submits Request

**Narrative:**
1. Employee navigates to `/My/Requests` or `/Requests/TimeOff/Create`
2. Fills form: Start Date, End Date, Reason
3. System validates:
   - End date >= Start date
   - No overlapping approved requests
4. Create `TimeOffRequest` with Status = Pending
5. Create notification for managers
6. Redirect with success message

### Manager Approves/Declines

**Narrative:**
1. Manager navigates to `/Admin/TimeOff` or `/Requests/Index`
2. Sees pending requests filtered by company
3. Clicks "Approve" or "Decline":
   - Update Status to Approved/Declined
   - Set ReviewedBy, ReviewedAt
   - If declining, add DeclineReason
4. If approved:
   - `TraineeService.CancelShadowingForTimeOffAsync()` — cancel any trainee shadowing during the period
5. Create notification for employee
6. If email enabled, send email notification

**Evidence:** `Pages/Requests/Index.cshtml.cs` (referenced in `docs/context.md:558`)

### Batch Approval

**Narrative:**
1. Manager selects multiple pending requests (checkboxes)
2. Clicks "Batch Approve" or "Batch Decline"
3. For each selected request:
   - Same approval logic as individual
   - Audit log entry
4. Show summary: "Approved 5 requests"

**Evidence:** `docs/BATCH_APPROVAL_FEATURE_DOCUMENTATION.md`

---

## 4. Shift Swap Flow

### Employee Initiates Swap

**Narrative:**
1. Employee navigates to `/Requests/Swaps/Create`
2. Selects their shift to swap
3. Selects target employee (same company, different person)
4. System creates `SwapRequest` with Status = Pending
5. Notification sent to target employee
6. Notification sent to managers

### Manager Approves Swap

**Narrative:**
1. Manager views pending swaps in `/Requests/Index` or `/Admin/SwapRequests`
2. Clicks "Approve":
   - Validate both users still available
   - Check no conflicting time-off
   - Update original `ShiftAssignment.UserId` to target user
   - Update status to Approved
3. Create notifications for both employees
4. Audit log entry

**Evidence:** `docs/context.md:7-8` — "Shift Swap Requests with manager approval"

---

## 5. Chore Management Flow

### Creating a Chore

**Narrative:**
1. Manager/Assigner navigates to `/Public/Chores`
2. Clicks on a date to open create modal
3. Fills: Title, Assignee, Notes
4. `ChoreService.CreateChoreAsync()`:
   - Check if user has shift on that date
   - If conflict and replaceShift = false → return conflict error
   - If conflict and replaceShift = true:
     - Remove shift assignment
     - Create chore
   - Create `Chore` record
5. Create notification for assignee
6. Refresh calendar view

### Shift Replacement Workflow

**Narrative:**
1. Manager tries to create chore for user with existing shift
2. System returns: "User has MORNING shift on this date"
3. Modal shows option: "Replace shift with chore?"
4. If confirmed:
   - `ChoreService.ReplaceShiftWithChoreAsync()`:
     - Delete `ShiftAssignment`
     - Create `Chore`
     - Audit log entry
   - Notifications to user about both changes

**Evidence:** `Services/ChoreService.cs:25190 bytes`

### Canceling a Chore (Soft Delete)

**Narrative:**
1. Manager clicks "Cancel" on chore
2. `ChoreService.CancelChoreAsync()`:
   - Set `CanceledAt = DateTime.UtcNow`
   - Set `CanceledBy = currentUserId`
   - (Record NOT deleted, just marked)
3. Chore disappears from calendar
4. Notification sent to assignee
5. Audit log entry

---

## 6. On-Duty Management Flow

### Creating On-Duty Assignment

**Narrative:**
1. Manager navigates to `/Public/OnDuty`
2. Clicks on a date
3. Selects: User, Type (Hakam 🛡️ or Lead ⭐)
4. `OnDutyService.CreateOnDutyAsync()`:
   - Validate user doesn't already have on-duty that day/type
   - Create `OnDuty` record (global table, no CompanyId filter)
5. Notification sent to assigned user
6. Calendar refreshes

**Note:** OnDuty is a **global table** (no CompanyId) — visible across all companies.

**Evidence:** `Services/OnDutyService.cs:17913 bytes`

---

## 7. Multi-Company Director Flow

### Director Switching Companies

**Narrative:**
1. Director logs in (has access to multiple companies)
2. System loads `DirectorCompanies` associations
3. Director navigates to `/Director/CompanyFilter`
4. Sees list of accessible companies
5. Selects company to work in
6. `CompanyFilterService` updates session/cookie with selected CompanyId
7. All subsequent queries use selected company's filters

### Director "View As" Mode

**Narrative:**
1. Director wants to see what a manager sees
2. Navigates to `/Director/ViewAsMode`
3. Selects target user role/user
4. `ViewAsModeService` sets temporary role context
5. Director sees UI with manager permissions
6. "Exit View As" button available to return

**Evidence:** `Services/ViewAsModeService.cs:2836 bytes`, `Services/CompanyFilterService.cs:4190 bytes`

---

## 8. User Onboarding Flow

### Public Signup (Join Request)

**Narrative:**
1. New user navigates to `/Auth/Signup`
2. Selects company from dropdown
3. Enters: Email, Display Name, Password, Requested Role
4. System creates `UserJoinRequest` with Status = Pending
5. Notification sent to company managers/owners
6. User sees "Request submitted" message

### Admin Approves Join Request

**Narrative:**
1. Admin navigates to `/Admin/Users`
2. Sees "Pending Join Requests" section
3. Reviews request details
4. Clicks "Approve":
   - Create `AppUser` from request data
   - Copy hashed password from request
   - Set role to approved role (may differ from requested)
   - Update request status to Approved
   - Link `CreatedUserId` to new user
5. Email sent to user: "Your account is ready"
6. User can now log in

**Evidence:** `Pages/Auth/Signup.cshtml.cs:6970 bytes`, `Models/UserJoinRequest.cs`

---

## 9. API Authentication Flow

### API Key Lifecycle

**Narrative:**
1. User requests API key via UI
   - `ApiKeyRequest` created with Status = Pending
2. Owner reviews in `/Admin/ApiKeys`:
   - Sees requested scopes, reason
   - Approves with modifications (scopes, rate limit, expiry)
3. On approval:
   - Generate 32-character random key
   - Store SHA256 hash in `ApiKey.KeyHash`
   - Store plain text (temporarily) for owner to copy
4. User retrieves plain text key (one-time copy)
5. User makes API requests with `X-API-Key: <key>`

### API Request Flow

**Narrative:**
1. External system sends `GET /api/v1/users` with `X-API-Key` header
2. `ApiAuthenticationMiddleware`:
   - Extract key from header
   - Hash key with SHA256
   - Query: `ApiKeys.First(k => k.KeyHash == hash)`
   - Validate: IsActive, ExpiresAt
   - If invalid → 401 Unauthorized
3. `ApiRateLimitingMiddleware`:
   - Check requests in last minute
   - If > RateLimitPerMinute → 429 Too Many Requests
   - Add `X-RateLimit-Remaining` header
4. Controller executes:
   - Validate scope: `HasScope("users:read")`
   - Query data (CompanyId from ApiKey)
   - Return JSON response
5. `ApiRequestLoggingMiddleware` logs request/response

**Evidence:** `Middleware/ApiAuthenticationMiddleware.cs:10803 bytes`, `Services/ApiKeyService.cs:13623 bytes`

---

## 10. Notification Flow

### Creating In-App Notifications

**Narrative:**
1. Service action triggers notification (e.g., shift assigned)
2. `NotificationService.CreateShiftAddedNotificationAsync()`:
   - Build notification message
   - Create `UserNotification` record
   - CompanyId auto-injected
3. User sees badge in header (UnreadNotificationCountViewComponent)
4. User clicks bell icon → `/My/NotificationCenter`
5. User marks as read → update `IsRead`, `ReadAt`

### Email Notifications (If Enabled)

**Narrative:**
1. Same trigger as in-app notification
2. Check `Email:Enabled` config
3. If enabled:
   - `EmailTemplateService` builds HTML email
   - `MailService.SendMailAsync()`:
     - HTTP POST to configured API endpoint
     - Include recipient, subject, HTML body
4. Log result in `EmailApiLog`

**Evidence:** `Services/NotificationService.cs:51101 bytes`, `Services/MailService.cs:63061 bytes`

---

## 11. My Team Calendar Flow

### Creating a Team Calendar

**Narrative:**
1. User navigates to `/MyTeam/Index`
2. Clicks "Create Calendar"
3. Enters name (e.g., "QA Team")
4. API call: `POST /api/team-calendars { name: "QA Team" }`
5. `TeamCalendarService.CreateCalendarAsync()`:
   - Validate unique name per user
   - Create `TeamCalendar` record (CompanyId from user)
6. Calendar appears in sidebar

### Adding Members

**Narrative:**
1. User clicks gear icon on calendar
2. Modal opens with two panes:
   - Left: Current members
   - Right: Available users (same company)
3. User drags/clicks to move users between panes
4. Click "Save"
5. API call: `PUT /api/team-calendars/{id}/members`
6. `TeamCalendarService.SetMembersAsync()`:
   - Delete all `TeamCalendarMember` for this calendar
   - Re-create with new member list

### Viewing Week

**Narrative:**
1. User selects calendar from dropdown
2. API call: `GET /api/team-calendars/{id}/week?date=2026-01-12`
3. `TeamCalendarEventAggregator.GetWeekViewAsync()`:
   - Query all member events (shifts, chores, on-duty, time-off)
   - For each member/day, compute highest priority status:
     - Vacation > After > On-Duty > Shift > Chore > Free
   - Return grid data with status icons
4. UI renders week grid with status badges

**Evidence:** `Services/TeamCalendarService.cs:11138 bytes`, `Services/TeamCalendarEventAggregator.cs:15680 bytes`

---

## 12. Audit & Analytics Flow

### Audit Logging

**Narrative:**
1. Any significant action triggers audit log
2. `AuditLogService.LogAsync("Action", details)`:
   - Capture: UserId, Action, Details (JSON), IP, UserAgent
   - Create `AuditLog` record
3. Owner views in `/Admin/AuditLog`:
   - Filter by date, action type, user
   - Export to CSV

### Analytics Dashboard

**Narrative:**
1. Admin navigates to `/Admin/Analytics`
2. `AnalyticsService` methods called:
   - `GetEmployeeHoursAsync()` — Hours per employee
   - `GetTeamHoursByRoleAsync()` — Hours by role
   - `GetSwapStatsAsync()` — Swap request statistics
   - `GetTimeOffStatsAsync()` — Time-off patterns
3. Data cached for 5 minutes (performance optimization)
4. Charts rendered with Chart.js

**Evidence:** `Services/AuditLogService.cs:7036 bytes`, `Services/AnalyticsService.cs:25130 bytes`

---

## 13. Data Lifecycle Flow

### Archiving Old Data

**Narrative:**
1. Owner navigates to `/Owner/DataLifecycle`
2. Selects date range for archiving
3. `ArchiveService.ArchiveShiftsAsync(beforeDate)`:
   - Export old `ShiftInstances` and `ShiftAssignments` to JSON
   - Store in archive table or file
   - Delete original records
4. Audit log entry

### Purging Archived Data

**Narrative:**
1. Owner selects archived data to purge
2. `PurgeService.PurgeArchivedDataAsync()`:
   - Permanently delete archived records
   - Cannot be recovered
3. Confirmation dialog required
4. Audit log entry

### Importing Data

**Narrative:**
1. Owner navigates to import section
2. Uploads CSV/JSON file
3. `ImportService.ImportUsersAsync(file)`:
   - Parse file
   - Validate data
   - Create users in batch
   - Report successes/failures

**Evidence:** `Services/ArchiveService.cs:28068 bytes`, `Services/PurgeService.cs:17192 bytes`, `Services/ImportService.cs:35064 bytes`

---

## 14. Background Job Flow

### Daily Notification Job

**Narrative:**
1. Application starts → `DailyNotificationJob.StartAsync()`
2. Job registers timer for configured time (e.g., 7:00 AM)
3. Timer fires:
   - Query users with daily notification preferences
   - For each:
     - Get tomorrow's shifts
     - Build email content
     - Send via `MailService`
4. Log completion
5. Timer resets for next day

**Evidence:** `Services/DailyNotificationJob.cs:5998 bytes`

---

## 15. Griffin ADFS Configuration Flow

### Owner Configures Griffin

**Narrative:**
1. Owner navigates to `/Owner/GriffinConfig`
2. Fills configuration:
   - Base URL (Griffin service endpoint)
   - Token Consumer URL
   - Auto-provision toggle
   - Default role for new users
   - Timeout
3. Saves configuration:
   - `GriffinConfigService.SaveConfigAsync()`
   - Encrypted API keys if any
4. Test connection button:
   - `GriffinService.TestConnectionAsync()`
   - Shows success/failure message

**Evidence:** `Pages/Owner/GriffinConfig.cshtml.cs:12314 bytes`, `Services/GriffinConfigService.cs:15986 bytes`

---

## Error Handling Patterns

### Database Errors

**Narrative:**
1. EF Core throws exception (e.g., unique constraint violation)
2. Try-catch in page model/controller
3. Log error: `_logger.LogError(ex, "...")`
4. Return user-friendly error message
5. Do NOT expose stack traces in production

### Concurrency Conflicts

**Narrative:**
1. User A and B both load same shift
2. User A saves changes → Concurrency token updated
3. User B saves changes:
   - EF Core throws `DbUpdateConcurrencyException`
   - Catch and show: "This record was modified. Please refresh."
4. `ShiftInstance.Concurrency` token verified

**Evidence:** `Data/AppDbContext.cs:101-102` — Concurrency token configuration

### Authorization Failures

**Narrative:**
1. User tries to access unauthorized page
2. `[Authorize(Policy = "...")]` attribute fails
3. Redirect to `/AccessDenied` (Program.cs:69)
4. Show appropriate error message

---

## State Transitions

### Request Status
```
Pending → Approved (with ReviewedBy, ReviewedAt)
Pending → Declined (with ReviewedBy, ReviewedAt, DeclineReason)
```

### Join Request Status
```
Pending → Approved (user created, CreatedUserId set)
Pending → Rejected (RejectionReason set)
```

### API Key Request Status
```
Pending → Approved (ApiKey created, GeneratedApiKeyId set)
Pending → Rejected (ReviewNotes set)
```

### Soft Delete Pattern (Chores, OnDuty)
```
Active: CanceledAt = null
Canceled: CanceledAt = DateTime, CanceledBy = UserId
```

---

## Caching Patterns

### Memory Cache (5-minute TTL)
**Used by:**
- `AnalyticsService` — Dashboard statistics
- `AppConfigCacheService` — Configuration values
- `ShiftTypeCacheService` — Shift type lookups
- `CompanyCacheService` — Company metadata

### Cache Invalidation
- On configuration change → Clear related cache
- On shift type change → `ShiftTypeCacheService.InvalidateAsync()`
- No distributed cache — single instance only
