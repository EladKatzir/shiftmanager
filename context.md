# Project Context

## 1. Summary

**ShiftManager** is a multi-tenant shift scheduling and workforce management system built with ASP.NET Core 8.0 and Razor Pages. It enables companies to manage employee schedules, shift assignments, time-off requests, shift swaps, and trainee shadowing programs with full multi-tenant isolation.

### Key Features
- **Multi-Tenant Architecture**: Fully isolated company data with automatic CompanyId scoping via EF Core query filters
- **Role-Based Access Control**: 6 roles (Owner, Director, Manager, Assigner, Employee, Trainee) with granular permissions
- **Shift Management**: Month/Week/Day/Table calendar views with drag-and-drop assignments and conflict detection
- **Request Workflows**: Employee-initiated shift swap and time-off requests with manager approval (including batch approval)
- **Trainee Shadowing**: Assign trainees to shadow experienced employees on shifts
- **Chore Management**: Assign non-shift tasks to employees with calendar view, conflict detection, and shift replacement
- **On-Duty Management**: Schedule Hakam (🛡️) and Lead (⭐) responsibilities with calendar view and soft delete
- **My Team Calendars**: Personal week-view calendars for tracking team availability with priority-based status aggregation
- **API Key Management**: Full API key lifecycle with approval workflow, rate limiting, and scope-based access control
- **RESTful API Layer**: Additive API endpoints for users, shifts, time-off, notifications, and team calendars
- **Localization**: Full support for English (en-US) and Hebrew (he-IL) with RTL layout - 100% coverage
- **In-App Notifications**: Real-time notifications for shift changes, approvals, trainee assignments, and chore assignments
- **Employee Profiles**: Rich profiles with avatars, contact info, skills, certifications, and emergency contacts
- **Audit Logging**: Comprehensive audit trail tracking all system actions with IP address and user agent
- **Analytics Dashboard**: Workforce analytics with charts for employee hours, team metrics, swap/time-off statistics
- **Email Notifications**: Mail service infrastructure with SMTP support (optional)
- **Batch Operations**: Bulk approve/decline requests with audit logging

### Current Status
- **Version**: Production-ready+ (as of 2025-11-12)
- **Database**: SQLite with 22 migrations applied, 21 tables
- **Test Coverage**: 15 unit tests (DirectorService only - 0% for other services)
- **Production Readiness**: ✅ All critical security issues resolved, health checks added, audit logging enabled
- **Recent Updates**: My Team Calendars feature with priority-based event aggregation, On-Duty management, role-based navigation, modern card-based UI

### Technology Stack
- **Framework**: ASP.NET Core 8.0 (Razor Pages)
- **Database**: SQLite via Entity Framework Core 9.0.9
- **Authentication**: Cookie-based authentication (ASP.NET Core Identity patterns)
- **Frontend**: Server-rendered Razor Pages with vanilla JavaScript + Chart.js for analytics
- **Localization**: ASP.NET Core Resource Files (.resx) - Full English/Hebrew coverage
- **Image Processing**: SixLabors.ImageSharp 3.1.11 for avatar uploads and thumbnails
- **Email**: IHttpClientFactory with HTTP API integration for email notifications
- **Testing**: xUnit + FluentAssertions + Moq

---

## 2. File Overviews

### Core Application Files

#### **Program.cs**
- **Purpose**: Application entry point and service configuration
- **Key Functions**:
  - Service registration (lines 12-100): DbContext, authentication, authorization, services
  - Database seeding (lines 98-234): Creates default admin user, companies, shift types
  - Middleware pipeline configuration (lines 237-268)
- **Important Sections**:
  - Lines 23-34: Localization setup (en-US, he-IL)
  - Lines 49-62: Multi-tenant infrastructure (TenantResolver, CompanyContext, CompanyIdInterceptor)
  - Lines 64-97: Authorization policies
    - IsManagerOrAdmin, IsAdmin, IsDirector, IsOwnerOrDirector (existing policies)
    - CanViewChores, CanViewOnDuty: All authenticated users can view (added for employee access)
    - CanEditChores, CanEditOnDuty: Manager+ roles only (admin-level editing)
  - Lines 84-112: Service registrations (IMailService, IConflictChecker, INotificationService, IDirectorService, ITraineeService, ICompanyFilterService, IViewAsModeService, IAuditLogService, IAnalyticsService, IProfileService, IAvatarService, IChoreService, IApiKeyService, IRateLimitingService, IValidationService, ISecurityLogger, API layer services)
  - Lines 114-121: Controller configuration for API endpoints with JSON options
  - Lines 134-171: Database seeding with company-specific checks (fixed duplicate ShiftTypes issue)
  - Lines 104-121: Production password security (requires SEED_ADMIN_PASSWORD env var)
  - Lines 327-331: API middleware registration (ApiRequestLoggingMiddleware, ApiAuthenticationMiddleware, ApiRateLimitingMiddleware)

#### **ShiftManager.csproj**
- **Purpose**: Project configuration and NuGet package references
- **Dependencies**:
  - Microsoft.EntityFrameworkCore.Sqlite: 9.0.9
  - Microsoft.EntityFrameworkCore.Design: 9.0.9
  - Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore: 8.0.0
  - SixLabors.ImageSharp: 3.1.11
- **Configuration**:
  - Target Framework: net8.0 (line 3)
  - Nullable reference types enabled (line 4)
  - Test files excluded from compilation (lines 8-13)

---

### Data Layer

#### **Data/AppDbContext.cs**
- **Purpose**: Entity Framework Core database context with multi-tenant query filters
- **DbSets** (lines 19-30): 12 tables (Companies, Users, ShiftTypes, ShiftInstances, ShiftAssignments, TimeOffRequests, SwapRequests, UserNotifications, Configs, DirectorCompanies, UserJoinRequests, RoleAssignmentAudits)
- **Key Functions**:
  - `OnModelCreating` (lines 32-170): Entity configurations, indexes, relationships
  - Value converters for DateOnly/TimeOnly (lines 34-40)
  - Concurrency token for ShiftInstance (line 56)
  - Unique indexes (lines 59-83)
  - Multi-tenant query filters (lines 148-167): Auto-inject CompanyId filtering
- **Important Sections**:
  - Lines 148-167: Query filters for automatic tenant scoping
  - Lines 86-113: DirectorCompany relationship configuration
  - Lines 115-138: UserJoinRequest and RoleAssignmentAudit configuration

#### **Data/CompanyIdInterceptor.cs**
- **Purpose**: EF Core SaveChanges interceptor that auto-injects CompanyId on entity creation
- **Functionality**: Automatically sets CompanyId for all entities implementing IBelongsToCompany
- **Reference**: Used in Program.cs:53-61

---

### Models

#### **Models/AppUser.cs**
- **Purpose**: User/employee entity
- **Fields**:
  - Id, CompanyId, Email (unique), DisplayName
  - Role (UserRole enum), IsActive
  - PasswordHash, PasswordSalt (PBKDF2, 100k iterations)
- **Important**: Email is the username (unique constraint in AppDbContext.cs:63)

#### **Models/Company.cs**
- **Purpose**: Multi-tenant company entity
- **Fields**:
  - Id, Name, Slug (unique, for URL routing), DisplayName
  - SettingsJson (JSON column for company-specific overrides)

#### **Models/ShiftType.cs**
- **Purpose**: Template for recurring shift patterns (Morning, Afternoon, Night, Mid)
- **Fields**:
  - Id, CompanyId, Key (MORNING/NOON/NIGHT/MIDDLE), Start (TimeOnly), End (TimeOnly)
  - Name (computed property, lines 11-26): Maps Key to display name
- **Note**: End < Start indicates overnight shift (wraps to next day)

#### **Models/ShiftInstance.cs**
- **Purpose**: Specific occurrence of a shift on a date
- **Fields**:
  - Id, CompanyId, ShiftTypeId, WorkDate (DateOnly)
  - Name (custom name override), StaffingRequired
  - Concurrency (concurrency token, line 18): Prevents race conditions in staffing adjustments
  - UpdatedAt (timestamp)

#### **Models/ShiftAssignment.cs**
- **Purpose**: Assignment of an employee to a specific shift instance
- **Fields**:
  - Id, CompanyId, ShiftInstanceId, UserId
  - TraineeUserId (nullable, line 16): For trainee shadowing
  - CreatedAt
- **Relationships**:
  - ShiftInstance navigation (line 11)
  - User navigation (line 13)
  - Trainee navigation (line 17)

#### **Models/Support/Enums.cs**
- **Purpose**: Centralized enum definitions
- **Enums**:
  - UserRole (lines 3-10): Owner=0, Manager=1, Employee=2, Director=3, Trainee=4
  - RequestStatus (lines 12-17): Pending, Approved, Declined
  - NotificationType (lines 19-35): 14 notification types (ShiftAdded, TimeOffApproved, TraineeShadowingAdded, ChoreAssigned, ChoreCanceled, etc.)
  - JoinRequestStatus (lines 37-42): Pending, Approved, Rejected

#### **Models/TimeOffRequest.cs**, **Models/SwapRequest.cs**, **Models/UserNotification.cs**
- **Purpose**: Request workflow entities (time-off, shift swaps, notifications)
- **Common Pattern**: CompanyId, Status, CreatedAt, ReviewedBy, ReviewedAt

#### **Models/Chore.cs**
- **Purpose**: Non-shift task assignment entity
- **Fields**:
  - Id, CompanyId, UserId (FK to AppUser), Date (DateOnly)
  - Title (task description), Notes (optional details)
  - CreatedAt, CanceledAt (nullable, for soft delete)
- **Features**:
  - Soft delete pattern (CanceledAt timestamp instead of hard delete)
  - Integrated with shift conflict detection
  - Displayed in employee dashboard and chore calendar
- **Usage**: Alternative to shift assignments for non-regular work tasks (maintenance, special projects, etc.)

#### **Models/DirectorCompany.cs**
- **Purpose**: Many-to-many relationship for Director multi-company access
- **Fields**: UserId, CompanyId, GrantedBy, GrantedAt, IsDeleted (soft delete)
- **Usage**: Directors can manage multiple companies; validated in DirectorService

#### **Models/RoleAssignmentAudit.cs**
- **Purpose**: Audit trail for role changes
- **Fields**: TargetUserId, OldRole, NewRole, ChangedBy, Timestamp, Reason

#### **Models/IBelongsToCompany.cs**
- **Purpose**: Marker interface for multi-tenant entities
- **Implementation**: All tenant-scoped entities implement this interface
- **Usage**: CompanyIdInterceptor uses this to auto-inject CompanyId

#### **Models/Api/ApiKey.cs**
- **Purpose**: API key entity for external integrations and programmatic access
- **Fields**:
  - Id, CompanyId, KeyHash (SHA256), PlainTextKey (nullable, Owner-only)
  - Name, Scopes (comma-separated), IsActive, RateLimitPerMinute
  - CreatedBy, CreatedAt, ExpiresAt, LastUsedAt
- **Key Methods**:
  - `HasScope(string scope)`: Check if API key has specific permission scope
  - `IsValid()`: Validate active status and expiration date
- **Security**: PlainTextKey stored temporarily for Owner retrieval post-creation, KeyHash used for authentication

#### **Models/Api/ApiKeyRequest.cs**
- **Purpose**: API key approval workflow entity (request → review → approval)
- **Fields**:
  - Id, CompanyId, RequestedBy, Name, Description
  - RequestedScopes (comma-separated), Status (Pending/Approved/Rejected)
  - ReviewedBy, ReviewedAt, ReviewNotes, CreatedAt
  - GeneratedApiKeyId (FK, nullable), ApprovedScopes, ApprovedRateLimit, ApprovedExpiresAt
- **Workflow**: User submits request → Admin reviews → Approval generates ApiKey entity → User retrieves plain text key once

---

### Services

#### **Services/DirectorService.cs**
- **Purpose**: Multi-company access validation for Directors
- **Key Methods**:
  - `IsDirector()` (lines 30-34): Check if current user is Owner or Director
  - `IsDirectorOfAsync(int companyId)` (lines 36-49): Validate access to specific company
  - `GetDirectorCompanyIdsAsync()` (lines 51-65): Get list of companies Director manages
  - `CanManageCompanyAsync(int companyId)` (lines 67-92): Check if user can manage company
  - `CanAssignRole(UserRole targetRole)` (lines 110-137): Role assignment permission matrix
- **Authorization Rules** (lines 115-136):
  - Owner: Can assign ANY role
  - Director: Can assign Employee, Manager, Director, Trainee (NOT Owner)
  - Manager: Can assign Employee, Trainee
  - Employee: Cannot assign any role
- **Test Coverage**: 15 unit tests in ShiftManager.Tests/UnitTests/Services/DirectorServiceTests.cs

#### **Services/NotificationService.cs**
- **Purpose**: Create in-app notifications for users
- **Key Methods**:
  - `CreateNotificationAsync()` (lines 31-57): Generic notification creation
  - `CreateShiftAddedNotificationAsync()` (lines 59-65): Shift assignment notification
  - `CreateShiftRemovedNotificationAsync()` (lines 67-73): Shift removal notification
  - `CreateTimeOffNotificationAsync()` (lines 75-85): Time-off approval/decline notification
  - `CreateSwapRequestNotificationAsync()` (lines 87-96): Swap request approval/decline notification
  - `CreateChoreAssignedNotificationAsync()`: Chore assignment notification
  - `CreateChoreCanceledNotificationAsync()`: Chore cancellation notification
- **Important**: All notifications scoped to CompanyId via TenantResolver (line 37)

#### **Services/ChoreService.cs** (IChoreService)
- **Purpose**: Manage non-shift task assignments with conflict detection
- **Key Methods**:
  - `GetChoresAsync()`: Retrieve chores for date range with optional cancellation filtering
  - `CreateChoreAsync()`: Create new chore with shift conflict detection
  - `ReplaceShiftWithChoreAsync()`: Replace an existing shift assignment with a chore
  - `CancelChoreAsync()`: Soft-delete a chore (sets CanceledAt timestamp)
  - `GetChoreByIdAsync()`: Retrieve specific chore by ID
  - `GetEligibleAssigneesAsync()`: Get list of users who can be assigned chores (respects role hierarchy)
  - `CanUserManageChoresAsync()`: Permission check for chore management
  - `CanUserManageChoreForAssigneeAsync()`: Permission check for assigning to specific user
  - `GetShiftOnDateAsync()`: Check if user has shift on specific date
- **Features**:
  - Automatic shift conflict detection (prevents double-booking)
  - Shift replacement workflow (optional override)
  - Soft delete pattern (CanceledAt timestamp)
  - Role-based assignee filtering (Managers see own company, Directors see all companies)
  - Integration with audit logging and notifications
- **Authorization Rules**:
  - Manager/Owner: Can assign chores to any employee in their company
  - Director: Can assign chores across multiple companies
  - Employee/Trainee: Cannot manage chores

#### **Services/TraineeService.cs**
- **Purpose**: Manage trainee shadowing assignments
- **Key Methods**:
  - `AssignTraineeToShiftAsync()` (lines 28-93): Assign trainee to shadow employee shift
  - `RemoveTraineeFromShiftAsync()` (lines 95-152): Remove trainee from shift with reason
  - `ValidateTraineeAssignmentAsync()` (lines 154-213): Validate trainee can be assigned (no conflicts)
  - `GetTraineeShadowedShiftsAsync()` (lines 215-230): Get trainee's shadowing schedule
  - `CancelAllShadowingAssignmentsAsync()` (lines 232-296): Cancel all shadowing when role changes
  - `CancelShadowingForTimeOffAsync()` (lines 298-361): Cancel shadowing for approved time-off
- **Validation Rules** (lines 154-213):
  - Trainee must have UserRole.Trainee
  - Same CompanyId as shift
  - No time conflicts with existing shifts
  - Only one trainee per shift
- **Notifications**: Sends to both trainee and primary employee (lines 64-80)

#### **Services/AuditLogService.cs**
- **Purpose**: Comprehensive audit logging for all system actions
- **Key Methods**:
  - `LogAsync()`: Log current user's action with automatic IP/user agent capture
  - `LogUserActionAsync()`: Log specific user's action
  - `LogSystemActionAsync()`: Log automated system actions
- **Features**:
  - Automatic IP address and user agent tracking
  - JSON details storage for structured data
  - Error handling (never breaks main operations)
  - Multi-tenant isolation via CompanyId
- **Integration**: Used across all admin pages for compliance tracking

#### **Services/AnalyticsService.cs**
- **Purpose**: Workforce analytics and reporting
- **Key Methods** (12 analytics methods):
  - Employee analytics: GetEmployeeHoursAsync, GetEmployeeUpcomingShiftsAsync
  - Team analytics: GetTeamHoursByRoleAsync, GetUnderStaffedShiftsAsync, GetOverStaffedShiftsAsync
  - Swap analytics: GetSwapStatsAsync, GetTopSwappersAsync
  - Time-off analytics: GetTimeOffStatsAsync, GetAverageApprovalTimeAsync
  - Back-to-back shift detection: GetBackToBackShiftsAsync
- **Features**:
  - 5-minute response caching for performance
  - 7 DTO classes for typed results
  - Date range filtering (7/30/90/365 days)
  - Export-ready data structures

#### **Services/ProfileService.cs**
- **Purpose**: Employee profile management and change auditing
- **Key Methods**:
  - `UpdateProfileAsync()`: Update employee profile with audit logging
  - `GetProfileChangeHistoryAsync()`: Retrieve profile change audit trail
- **Features**:
  - Field-level change tracking
  - Before/after value storage
  - Integration with AuditLogService
  - Permission-based field access

#### **Services/AvatarService.cs**
- **Purpose**: Avatar image upload and processing
- **Key Methods**:
  - `SaveAvatarAsync()`: Process and save employee avatar
  - `DeleteAvatarAsync()`: Remove avatar file
- **Features**:
  - Image resizing (300x300px)
  - Format conversion to JPEG with 85% quality
  - Multi-tenant file isolation (wwwroot/avatars/{companyId}/)
  - Automatic cleanup of old avatars
  - Uses SixLabors.ImageSharp 3.1.11

#### **Services/MailService.cs** (IMailService)
- **Purpose**: HTTP API-based email notification infrastructure
- **Key Methods**:
  - `SendMailAsync(string recipient, string subject, string htmlBody)`: Send HTML email via HTTP API
- **Features**:
  - IHttpClientFactory integration for connection pooling
  - HTTP API integration (company-specific mail service)
  - Configuration via appsettings.json (Email:ApiKey, Email:ApiUrl, Email:FromAddress, Email:Enabled)
  - Async/await pattern for performance
  - HTML email support with JSON payload
  - Graceful degradation when disabled (Email:Enabled = false)
  - Validation and error handling with structured logging
- **Configuration**: Optional - system works without email configured (Email:Enabled = false)

#### **Services/TenantResolver.cs**, **Services/CompanyContext.cs**
- **Purpose**: Resolve current tenant (CompanyId) from authenticated user claims
- **Implementation**: ITenantResolver reads CompanyId claim, caches in HttpContext.Items
- **Usage**: Used by query filters (AppDbContext.cs:150-167) and interceptor

#### **Services/ConflictChecker.cs**
- **Purpose**: Detect scheduling conflicts (double-booking, time-off overlaps)
- **Reference**: Used in shift assignment and swap approval workflows

#### **Services/LocalizationService.cs**
- **Purpose**: Culture-aware formatting for dates, times, and currency
- **Localization**: Supports en-US and he-IL cultures

#### **Services/Api/ApiKeyService.cs** (IApiKeyService)
- **Purpose**: Manage API key lifecycle with approval workflow
- **Key Methods** (11 methods total):
  - **Request Management**: `CreateRequestAsync()`, `GetPendingRequestsAsync()`, `GetUserRequestsAsync()`
  - **Approval Workflow**: `ApproveRequestAsync()` (generates ApiKey + 32-char random key), `RejectRequestAsync()`
  - **Key Management**: `GetApiKeysAsync()`, `GetApiKeyByIdAsync()`, `RefreshApiKeyAsync()` (regenerate key), `RevokeApiKeyAsync()`, `DeleteApiKeyAsync()`
  - **Authentication**: `ValidateApiKeyAsync()` (SHA256 hash comparison)
- **Features**:
  - Role-based visibility (Owner sees PlainTextKey, others see masked version)
  - SHA256 key hashing for secure storage
  - Expiration date validation
  - Rate limit enforcement per key
  - Scope-based permission system
  - Audit logging for all operations

#### **Services/Api/RateLimitingService.cs** (IRateLimitingService)
- **Purpose**: Per-API-key rate limiting with sliding window
- **Key Methods**:
  - `CheckRateLimitAsync(int apiKeyId, int limit)`: Verify request within rate limit
  - `RecordRequestAsync(int apiKeyId)`: Log request timestamp
- **Features**:
  - In-memory request tracking (sliding 1-minute window)
  - Automatic cleanup of expired entries
  - Per-key limit enforcement
  - Returns remaining requests count

#### **Services/Api/ValidationService.cs** (IValidationService)
- **Purpose**: Input validation for API requests
- **Key Methods**:
  - `ValidateScopesAsync(string scopes)`: Validate requested permission scopes
  - `ValidateRateLimitAsync(int rateLimit)`: Ensure rate limit within bounds (1-1000 req/min)
- **Supported Scopes**: users:read, users:write, shifts:read, shifts:write, requests:read, requests:write, notifications:read

#### **Services/Api/SecurityLogger.cs** (ISecurityLogger)
- **Purpose**: Security event logging for API operations
- **Key Methods**:
  - `LogApiKeyCreatedAsync()`, `LogApiKeyRevokedAsync()`, `LogApiKeyRefreshedAsync()`
  - `LogUnauthorizedAccessAsync()`, `LogRateLimitExceededAsync()`
- **Features**:
  - Integration with AuditLogService
  - IP address and user agent tracking
  - JSON details for structured logging

#### **Services/TeamCalendarService.cs**
- **Purpose**: Manage personal team calendars and member lists
- **Key Methods**:
  - `GetCalendarsForOwnerAsync(int userId)`: Get all calendars owned by user
  - `GetCalendarByIdAsync(int calendarId, int userId)`: Get specific calendar with ownership validation
  - `CreateCalendarAsync(int userId, string name)`: Create new calendar
  - `RenameCalendarAsync(int calendarId, int userId, string newName)`: Rename existing calendar
  - `DeleteCalendarAsync(int calendarId, int userId)`: Delete calendar (cascade deletes members)
  - `GetMembersAsync(int calendarId, int userId)`: Get current members and available users
  - `SetMembersAsync(int calendarId, int userId, List<int> memberUserIds)`: Update member list
- **Features**:
  - Ownership validation (users can only manage their own calendars)
  - Name uniqueness per owner
  - Cascade deletion of members
  - Two-pane member management (current vs available)
- **Authorization**: User-owned resources, no role restrictions

#### **Services/TeamCalendarEventAggregator.cs**
- **Purpose**: Aggregate events from multiple sources and compute priority-based status for each member/day
- **Key Methods**:
  - `GetWeekViewAsync(List<int> memberUserIds, DateOnly weekStart)`: Get week view data with status badges
- **Priority System** (highest to lowest):
  1. Vacation (full day or partial "until 1 PM")
  2. After/אפטֵר (4 PM start → 1 PM next day)
  3. On-Duty
  4. Shift
  5. Chore
  6. Free (default when nothing scheduled)
- **Vacation Semantics**:
  - Full vacation days: `StartDate` through `EndDate`
  - Extension day: `EndDate + 1 day` marked as "Vacation until 1PM" (busy until 13:00)
- **After/אפטֵר Semantics**:
  - Start day: `StartDate` from 4 PM marked as "After from 4PM"
  - Extension day: `StartDate + 1 day` until 1 PM marked as "After until 1PM"
- **Features**:
  - Fetches data from 4 sources: TimeOffRequests, OnDuty, ShiftAssignments, Chores
  - Computes single highest-priority status per member per day
  - Returns targetUrl for navigation (later filtered by role)
  - Handles multiple shifts/chores with count indicators
- **Integration**: Used by TeamCalendarsController to build week view API response

#### **Services/OnDutyService.cs**
- **Purpose**: Manage on-duty/Hakam assignments
- **Key Methods**:
  - `GetOnDutiesAsync(DateOnly start, DateOnly end, bool? includeCanceled)`: Get on-duty assignments for date range
  - `CreateOnDutyAsync(int userId, DateOnly date, OnDutyType type)`: Create new on-duty assignment
  - `CancelOnDutyAsync(int onDutyId)`: Soft-delete on-duty assignment
  - `GetOnDutyByIdAsync(int onDutyId)`: Retrieve specific on-duty assignment
  - `CanUserManageOnDutyAsync()`: Permission check (Manager+ only)
- **Features**:
  - Soft delete pattern (CanceledAt timestamp)
  - Two types: Hakam (🛡️) and Lead (⭐)
  - Integrated with BusyUserService for conflict detection
  - Displayed in Public/OnDuty calendar and My Team view
- **Authorization**: Manager/Owner/Director can create, all users can view

#### **Services/BusyUserService.cs**
- **Purpose**: Check user availability across all event types (shifts, chores, on-duty, vacations)
- **Key Methods**:
  - `IsUserBusyAsync(int userId, DateOnly date)`: Check if user has any events on date
  - `GetBusyDatesAsync(int userId, DateOnly start, DateOnly end)`: Get all dates user is busy in range
- **Features**:
  - Aggregates across 4 event sources
  - Respects soft deletes (CanceledAt)
  - Handles vacation extension days (until 1 PM next day)
  - Handles after/אפטֵר semantics (4 PM → 1 PM next day)
- **Integration**: Used by conflict detection when creating chores and on-duty assignments

---

### Middleware

#### **Middleware/CompanyContextMiddleware.cs**
- **Purpose**: Ensure CompanyContext is resolved early in request pipeline
- **Functionality** (lines 19-26): Forces CompanyContext.CompanyId property access to cache tenant resolution
- **Registration**: Program.cs:262

#### **Middleware/Api/ApiRequestLoggingMiddleware.cs**
- **Purpose**: Log all API requests for audit trail
- **Functionality**:
  - Captures request method, path, query string, and IP address
  - Logs response status code and duration
  - Integration with AuditLogService
- **Registration**: Program.cs:327 (API endpoints only)

#### **Middleware/Api/ApiAuthenticationMiddleware.cs**
- **Purpose**: Authenticate API requests using X-API-Key header
- **Functionality**:
  - Reads X-API-Key header from request
  - Validates API key via ApiKeyService.ValidateApiKeyAsync()
  - Returns HTTP 401 (Unauthorized) if invalid
  - Returns HTTP 403 (Forbidden) if expired or inactive
  - Sets CompanyId claim for multi-tenant scoping
- **Registration**: Program.cs:328

#### **Middleware/Api/ApiRateLimitingMiddleware.cs**
- **Purpose**: Enforce per-API-key rate limits
- **Functionality**:
  - Checks rate limit via RateLimitingService.CheckRateLimitAsync()
  - Returns HTTP 429 (Too Many Requests) if limit exceeded
  - Adds X-RateLimit-Limit and X-RateLimit-Remaining headers
  - Logs rate limit violations
- **Registration**: Program.cs:329

---

### Pages (Razor Pages)

#### Authentication Pages
- **Pages/Auth/Login.cshtml.cs**: Cookie-based login with PBKDF2 password verification
- **Pages/Auth/Signup.cshtml.cs**: New company signup with join request approval workflow
- **Pages/Auth/Logout.cshtml.cs**: Sign out and clear authentication cookie

#### Calendar Views
- **Pages/Calendar/Month.cshtml**: Monthly calendar grid with all shifts
- **Pages/Calendar/Week.cshtml**: Weekly view with daily columns
- **Pages/Calendar/Day.cshtml**: Single day detailed view
- **Pages/Calendar/Table.cshtml**: Table view with filters for managers

#### Shift Assignment
- **Pages/Assignments/Manage.cshtml**: Adjust staffing levels (+/- buttons) with concurrency control, trainee assignment

#### My Team Pages
- **Pages/MyTeam/Index.cshtml**: Personal team calendars feature
  - **Features**:
    - Create/rename/delete named calendars (e.g., "Project X Team", "QA Crew")
    - Calendar switcher dropdown with inline rename and delete
    - Two-pane member management modal (current members ↔ available users)
    - Week navigation (previous/next/this week buttons)
    - Modern card-based layout with member avatars showing initials
    - Status badges with icons: 🧳 vacation, 🌙 after, 🛡️ on-duty, ⏱️ shift, 🔧 chore, － free
    - Role-based click navigation (Regular: view-only, Assigner: chores, Manager+: all)
    - Informative tooltips showing event details and navigation info
    - Responsive design (desktop grid → mobile stacked cards)
  - **Authorization**: All authenticated users (user-owned calendars)
  - **API Integration**: Uses /api/team-calendars endpoints with cookie authentication
  - **Visual Design**: Card-based with hover effects, smooth transitions, and CSS Grid layout
  - **Mobile**: Adaptive layout at 968px breakpoint with day labels inside cards

#### Employee Self-Service
- **Pages/My/Index.cshtml**: My Overview dashboard - unified timeline feed showing all user's Vacations, On-duty, Shifts, and Chores
  - **Features**:
    - Sticky top bar with time range switcher (Week/Month)
    - Client-side type filters with color-coded legend (Vacations=Blue, On-duty=Red, Shifts=Purple, Chores=Yellow)
    - Grouped timeline: Today, Tomorrow, This Week, Later
    - View modes: Upcoming, All, Past 30 days
    - Interactive stats bar chart with Count/Hours toggle
    - Read-only detail drawer for item inspection
    - Conflict detection with warning badges (⚠) on overlapping items
    - Full Hebrew localization with dark mode support
  - **Visual Design**: Modern card-based layout with smooth animations and hover effects
- **Pages/My/Requests.cshtml**: Submit and view time-off and swap requests
- **Pages/My/NotificationCenter.cshtml**: In-app notification inbox with mark as read (accessible to all roles)
- **Pages/My/Profile.cshtml**: Employee profile page with avatar upload, personal info, and change history
- **Pages/My/_TimelineItem.cshtml**: Partial view for rendering timeline items in My Overview feed

#### Request Workflows
- **Pages/Requests/Index.cshtml**: Manager view of pending requests (time-off, swaps)
- **Pages/Requests/TimeOff/Create.cshtml**: Submit time-off request
- **Pages/Requests/Swaps/Create.cshtml**: Initiate shift swap request

#### Public Pages (All Authenticated Users)
- **Pages/Public/Chores.cshtml**: Monthly calendar view for chore assignments
  - **Authorization**: CanViewChores policy (all authenticated users can view)
  - **Manager+ Features**:
    - Create chores with assignee selection and date picker
    - Shift conflict detection with optional replacement workflow
    - Cancel/delete chores with audit logging
    - Full edit controls in modals and table view
  - **Employee Experience**:
    - View-only calendar with all chores visible
    - Clicking on dates with chores shows details modal (no delete button for employees)
    - Clicking on empty dates shows info modal: "There isn't a chore for this day. Contact your team manager if you think that's wrong."
    - Delete buttons hidden from employees in table view and detail modals
  - **Visual Design**: Green styling with 🧹 icon, gradients, and hover effects

- **Pages/Public/OnDuty.cshtml**: Monthly calendar view for on-duty assignments
  - **Authorization**: CanViewOnDuty policy (all authenticated users can view)
  - **Manager+ Features**:
    - Create on-duty assignments with assignee and type selection
    - Cancel/delete on-duty assignments with audit logging
    - Full edit controls in modals and table view
  - **Employee Experience**:
    - View-only calendar with all on-duty assignments visible
    - Clicking on dates with assignments shows details modal (no delete button for employees)
    - Clicking on empty dates shows info modal: "There isn't an on-duty assignment for this day. Contact your team manager if you think that's wrong."
    - Delete buttons hidden from employees in table view and detail modals
  - **Visual Design**: Purple/amber gradients by type (Hakam 🛡️ / Lead ⭐)

#### Admin Pages (Manager+ only)
- **Pages/Admin/Users.cshtml**: User management (create, edit roles, deactivate, batch approval)
- **Pages/Admin/ShiftTypes.cshtml**: Configure shift types (times, names)
- **Pages/Admin/TimeOff.cshtml**: Approve/decline time-off requests (batch operations supported)
- **Pages/Admin/Config.cshtml**: Company settings (RestHours, WeeklyHoursCap)
- **Pages/Admin/Companies.cshtml**: Company management (Owner only)
- **Pages/Admin/Directors.cshtml**: Assign/revoke Director multi-company access
- **Pages/Admin/EditProfile.cshtml**: Manager full-edit employee profile page (all fields editable)
- **Pages/Admin/AuditLog.cshtml**: Comprehensive audit log viewer with filtering and CSV export
- **Pages/Admin/Analytics.cshtml**: Workforce analytics dashboard with charts and reports
- **Pages/Admin/ApiKeys.cshtml**: API key management (request, approve, revoke, refresh)

#### Director Pages
- **Pages/Director/CompanyFilter.cshtml**: Select active company for cross-tenant operations
- **Pages/Director/ViewAsMode.cshtml**: Impersonate user view (for support/debugging)
- **Pages/Director/NotificationHub.cshtml**: Cross-company notification aggregation

#### Diagnostic Page
- **Pages/Diagnostic.cshtml**: Admin-only page showing cross-tenant data (SECURED: line 8 has [Authorize(Policy = "IsAdmin")])

#### API Controllers (RESTful Endpoints)
- **Controllers/Api/UsersController.cs**: User management API endpoints
  - GET /api/users - List users with pagination and filtering
  - GET /api/users/{id} - Get user details
  - POST /api/users - Create new user
  - PUT /api/users/{id} - Update user
  - DELETE /api/users/{id} - Deactivate user
  - Required scopes: users:read, users:write

- **Controllers/Api/ShiftsController.cs**: Shift management API endpoints
  - GET /api/shifts - List shifts for date range
  - GET /api/shifts/{id} - Get shift details with assignments
  - POST /api/shifts - Create shift instance
  - PUT /api/shifts/{id} - Update shift
  - DELETE /api/shifts/{id} - Delete shift
  - POST /api/shifts/{id}/assign - Assign employee to shift
  - DELETE /api/shifts/{id}/assign/{userId} - Remove assignment
  - Required scopes: shifts:read, shifts:write

- **Controllers/Api/RequestsController.cs**: Request workflow API endpoints
  - GET /api/requests/timeoff - List time-off requests with filtering
  - GET /api/requests/swaps - List swap requests with filtering
  - POST /api/requests/timeoff - Submit time-off request
  - POST /api/requests/swaps - Submit swap request
  - PUT /api/requests/timeoff/{id}/approve - Approve time-off
  - PUT /api/requests/timeoff/{id}/decline - Decline time-off
  - PUT /api/requests/swaps/{id}/approve - Approve swap
  - PUT /api/requests/swaps/{id}/decline - Decline swap
  - Required scopes: requests:read, requests:write

- **Controllers/Api/NotificationsController.cs**: Notification API endpoints
  - GET /api/notifications - List notifications for authenticated user
  - PUT /api/notifications/{id}/read - Mark notification as read
  - DELETE /api/notifications/{id} - Delete notification
  - Required scopes: notifications:read

- **Controllers/TeamCalendarsController.cs**: My Team Calendars API endpoints (cookie auth, not API keys)
  - GET /api/team-calendars - List user's calendars with member counts
  - GET /api/team-calendars/{id} - Get calendar with members
  - POST /api/team-calendars - Create new calendar
  - PUT /api/team-calendars/{id} - Rename calendar
  - DELETE /api/team-calendars/{id} - Delete calendar (cascade deletes members)
  - GET /api/team-calendars/{id}/week?date=YYYY-MM-DD - Get week view with status badges
  - GET /api/team-calendars/{id}/members - Get current and available members for two-pane selector
  - PUT /api/team-calendars/{id}/members - Update member list (replaces existing)
  - **Authentication**: Cookie-based (not API keys) - for logged-in web users only
  - **Authorization**: User ownership validation - users can only access their own calendars
  - **Features**:
    - Priority-based event aggregation (Vacation > After > On-Duty > Shift > Chore > Free)
    - Role-based targetUrl filtering (Regular: no nav, Assigner: chores, Manager+: all)
    - Vacation semantics (until 1 PM next day)
    - After/אפטֵר semantics (4 PM → 1 PM next day)
    - Member search and filtering
    - Week start calculation (always Sunday)

- **Features** (All API Controllers):
  - RFC-7807 Problem Details for error responses
  - Multi-tenant scoping via ApiAuthenticationMiddleware (or cookie auth for team-calendars)
  - Rate limiting per API key (external APIs only)
  - Audit logging for all operations
  - JSON responses with consistent structure
  - Scope-based authorization (external APIs) or ownership validation (team-calendars)

---

### View Components

#### **ViewComponents/UnreadNotificationCountViewComponent.cs**
- **Purpose**: Display unread notification badge count in navigation
- **Usage**: Rendered in _Layout.cshtml

#### **ViewComponents/LanguageToggleViewComponent.cs**
- **Purpose**: Language switcher (English ↔ Hebrew)
- **Usage**: Sets culture cookie and redirects

---

### Migrations

#### Migration History (17 migrations)
1. **20250927202116_InitialCreate**: Base schema (Users, Companies, ShiftTypes, ShiftInstances, ShiftAssignments)
2. **20250928195641_AddUserNotifications**: UserNotifications table
3. **20250928201142_AddNavigationProperties**: Foreign key relationships (⚠️ contains PRAGMA operations)
4. **20250930114957_MultitenancyPhase1**: Add CompanyId to all entities, query filters
5. **20250930194706_AddCompanyIdToShiftTypes**: ShiftType multi-tenant scoping
6. **20250930210753_AddDirectorRole**: DirectorCompany table, UserRole.Director enum
7. **20250930221748_AddUserJoinRequests**: UserJoinRequest table for signup workflow
8. **20250930222145_UpdateJoinRequestPasswordTypes**: Fix password field types (⚠️ contains PRAGMA operations)
9. **20251003185234_FixShiftTypeKeys**: Composite index on (CompanyId, Key)
10. **20251003232434_AuditRoleAssignments**: RoleAssignmentAudit table
11. **20251004215820_AddTraineeRoleAndShadowing**: Add UserRole.Trainee, ShiftAssignment.TraineeUserId (⚠️ contains PRAGMA operations)
12. **20251019195032_AddAuditLogAndAnalytics**: AuditLog table and ProfileChangeAudit table
13. **20251019202014_AddEmployeeProfileEnhancements**: Extended AppUser fields (15+ new fields for profiles, avatar support)
14. **20251021055915_MakeUserIdNullableInShiftAssignment**: Allow null UserId in ShiftAssignment for unassigned shifts
15. **20251023003948_AddChoresFeature**: Chores table with soft delete support (CanceledAt)
16. **20251029XXXXXX_AddApiKeyManagement**: ApiKeys and ApiKeyRequests tables for API key lifecycle management
17. **20251029XXXXXX_AddPlainTextKeyToApiKey**: Add PlainTextKey column to ApiKeys table (nullable, Owner-only visibility)

#### Rollback Scripts
- **Location**: `Migrations/rollback/`
- **Coverage**: 12 of 17 migrations have corresponding rollback SQL scripts (migrations 13, 14, 15, 16, 17 need rollback scripts)
- **Documentation**: `Migrations/rollback/README.md` contains complete rollback procedures

#### Known Issue (ISSUE-005)
- **Severity**: Medium
- **Issue**: Migrations #3, #8, #11 contain non-transactional PRAGMA operations
- **Impact**: If migration fails mid-execution, database may be left inconsistent
- **Mitigation**: Always backup before migrations; documented in MIGRATIONS.md

---

## 3. Settings and Configuration

### **appsettings.json**
- **Connection Strings** (lines 2-4):
  - Default: "Data Source=app.db" (SQLite database file)
- **Logging** (lines 5-10):
  - Default: Information
  - Microsoft.AspNetCore: Warning
- **AllowedHosts** (line 11): "*" (all hosts allowed)
- **Features** (lines 12-15):
  - EnforceCompanyScope: false (development override)
  - EnableDirectorRole: true (enables Director role seeding)
- **Email** (lines 16-21):
  - Enabled: false (email service disabled by default)
  - ApiKey: API key for company mail service
  - ApiUrl: HTTP endpoint for mail API
  - FromAddress: Default sender email address

### **appsettings.Development.json**
- Inherits from appsettings.json
- Development-specific logging overrides

### **Properties/launchSettings.json**
- **Profiles**: Development server configurations
- **applicationUrl**: http://localhost:5000, https://localhost:5001
- **Environment Variables**: ASPNETCORE_ENVIRONMENT=Development

### Environment Variables (Production)

#### Required in Production
- **SEED_ADMIN_PASSWORD**: Initial admin password (required, Program.cs:104-115)
  - Default in dev: "admin123"
  - Production: MUST be set via environment variable or throws exception

#### Optional
- **SEED_DIRECTOR_PASSWORD**: Initial director password (for testing)
  - Default in dev: "director123"
- **EnableHttpsRedirection**: Force HTTPS redirect (default: true in production, false in dev)

### Feature Flags
- **Features:EnforceCompanyScope**: Enable strict multi-tenant enforcement (default: false)
- **Features:EnableDirectorRole**: Enable Director role and multi-company seeding (default: true)

---

## 4. Database Structure

### Schema Overview (21 Tables)

#### Core Tables
1. **Companies**: Multi-tenant company entities
   - Columns: Id (PK), Name, Slug (unique), DisplayName, SettingsJson
   - Indexes: Unique on Slug (AppDbContext.cs:60)

2. **Users**: Employee/user accounts
   - Columns: Id (PK), CompanyId (FK), Email (unique), DisplayName, Role, IsActive, PasswordHash, PasswordSalt
   - **Profile Fields** (added in migration #13): PreferredName, Phone, City, DateOfBirth, Department, JobTitle, HireDate, ManagerUserId (FK), Skills, Certifications, EmergencyContactName, EmergencyContactPhone, Notes, ProfilePictureUrl
   - Indexes: Unique on Email (AppDbContext.cs:63)
   - Relationships: Belongs to Company, optional Manager (self-referencing FK)

3. **ShiftTypes**: Shift templates (Morning, Noon, Night, Middle)
   - Columns: Id (PK), CompanyId (FK), Key, Start (TimeOnly), End (TimeOnly)
   - Indexes: Composite on (CompanyId, Key) (AppDbContext.cs:67)
   - Seed Data: 4 default shift types per company (Program.cs:134-140)

4. **ShiftInstances**: Specific shift occurrences on dates
   - Columns: Id (PK), CompanyId (FK), ShiftTypeId (FK), WorkDate (DateOnly), Name, StaffingRequired, Concurrency, UpdatedAt
   - Concurrency: Concurrency token for optimistic locking (AppDbContext.cs:56)

5. **ShiftAssignments**: Employee-to-shift assignments
   - Columns: Id (PK), CompanyId (FK), ShiftInstanceId (FK), UserId (FK), TraineeUserId (FK, nullable), CreatedAt
   - Indexes: Unique on (CompanyId, ShiftInstanceId, UserId) (AppDbContext.cs:71)
   - Relationships: ShiftInstance, User (employee), Trainee (optional)

#### Request Workflow Tables
6. **TimeOffRequests**: PTO request workflow
   - Columns: Id (PK), CompanyId (FK), UserId (FK), StartDate, EndDate, Reason, Status, ReviewedBy (FK), ReviewedAt
   - Indexes: Composite on (CompanyId, UserId, StartDate) (AppDbContext.cs:75)

7. **SwapRequests**: Shift swap request workflow
   - Columns: Id (PK), CompanyId (FK), RequesterId (FK), TargetShiftId (FK), OfferedShiftId (FK), Status, ReviewedBy (FK), ReviewedAt, CreatedAt
   - Indexes: Composite on (CompanyId, Status, CreatedAt) (AppDbContext.cs:79)

8. **UserNotifications**: In-app notification system
   - Columns: Id (PK), CompanyId (FK), UserId (FK), Type (NotificationType), Title, Message, IsRead, CreatedAt, RelatedEntityId, RelatedEntityType
   - Indexes: Composite on (CompanyId, UserId, CreatedAt) (AppDbContext.cs:83)

#### Multi-Tenant & Authorization Tables
9. **DirectorCompanies**: Director multi-company access mapping
   - Columns: Id (PK), UserId (FK), CompanyId (FK), GrantedBy (FK), GrantedAt, IsDeleted (soft delete)
   - Indexes: Unique on (UserId, CompanyId) where IsDeleted=0 (AppDbContext.cs:87-89)
   - Relationships: User, Company, GrantedByUser (AppDbContext.cs:98-113)

10. **UserJoinRequests**: Signup approval workflow
    - Columns: Id (PK), Email, CompanyId (FK), DisplayName, PasswordHash, PasswordSalt, Status, ReviewedBy (FK), ReviewedAt, CreatedAt, CreatedUserId (FK)
    - Indexes: Composite on (Email, CompanyId, Status), (CompanyId, Status, CreatedAt) (AppDbContext.cs:117-120)

11. **RoleAssignmentAudits**: Role change audit trail
    - Columns: Id (PK), CompanyId (FK), TargetUserId (FK), OldRole, NewRole, ChangedBy (FK), Timestamp, Reason
    - Indexes: Composite on (CompanyId, TargetUserId, Timestamp), (ChangedBy, Timestamp) (AppDbContext.cs:142-145)

12. **Configs**: Company-specific configuration key-value store
    - Columns: Id (PK), CompanyId (FK), Key, Value
    - Seed Data: RestHours=8, WeeklyHoursCap=40 (Program.cs:146-150)

#### Audit & Analytics Tables
13. **AuditLog**: Comprehensive system action audit trail
    - Columns: Id (PK), CompanyId (FK), UserId (FK, nullable), UserEmail, UserDisplayName, Action, EntityType, EntityId, Description, Details (JSON), Timestamp, IpAddress, UserAgent
    - Indexes: Composite on (CompanyId, Timestamp), (CompanyId, UserId, Timestamp), (CompanyId, Action, Timestamp)
    - Purpose: Compliance tracking, security auditing, debugging
    - Denormalized: UserEmail and UserDisplayName stored to preserve audit trail after user deletion

14. **ProfileChangeAudit**: Employee profile change history
    - Columns: Id (PK), CompanyId (FK), UserId (FK), ChangedBy (FK), FieldName, OldValue, NewValue, Timestamp
    - Indexes: Composite on (CompanyId, UserId, Timestamp)
    - Purpose: Track who changed what fields in employee profiles
    - Integration: Used by ProfileService for change tracking

15. **Chores**: Non-shift task assignments
    - Columns: Id (PK), CompanyId (FK), UserId (FK), Date (DateOnly), Title, Notes, CreatedAt, CanceledAt (nullable)
    - Indexes: Composite on (CompanyId, UserId, Date), (CompanyId, Date)
    - Purpose: Assign one-off tasks to employees (maintenance, projects, etc.)
    - Features: Soft delete (CanceledAt), shift conflict detection, calendar view
    - Integration: Displayed in My/Index dashboard and Chores/Calendar page

#### Team Collaboration Tables
16. **TeamCalendar**: Personal week-view calendars for tracking team availability
    - Columns: Id (PK), CompanyId (FK), OwnerUserId (FK), Name, CreatedAt, UpdatedAt
    - Indexes: Composite on (CompanyId, OwnerUserId), (CompanyId, Name)
    - Purpose: Allow users to create multiple named calendars for different teams/projects
    - Features: Private to owner (unshareable), supports multiple calendars per user
    - Integration: Used by My Team Calendars feature at /MyTeam/Index

17. **TeamCalendarMember**: Members included in a team calendar
    - Columns: Id (PK), TeamCalendarId (FK), MemberUserId (FK), AddedAt
    - Indexes: Composite on (TeamCalendarId, MemberUserId), unique constraint
    - Purpose: Many-to-many relationship between calendars and users
    - Features: Track when members were added, prevent duplicates

18. **OnDuty**: On-duty/Hakam assignments
    - Columns: Id (PK), CompanyId (FK), UserId (FK), Date (DateOnly), Type (OnDutyType enum), CreatedAt, CanceledAt (nullable)
    - Indexes: Composite on (CompanyId, UserId, Date), (CompanyId, Date)
    - Purpose: Assign on-duty responsibilities (Hakam, Lead) to employees
    - Features: Soft delete (CanceledAt), type-based differentiation, calendar view
    - Integration: Displayed in Public/OnDuty page and My Team Calendars

19. **OnDutyTypeConfig**: Configurable on-duty type definitions
    - Columns: Id (PK), CompanyId (FK), TypeKey, DisplayName, IconEmoji, ColorHex, SortOrder, IsActive
    - Indexes: Composite on (CompanyId, TypeKey)
    - Purpose: Allow companies to customize on-duty types with names, icons, and colors
    - Seed Data: Hakam (🛡️, #8B0000) and Lead (⭐, #FF8C00)

#### API & Integration Tables
20. **ApiKeys**: API key entities for external integrations
    - Columns: Id (PK), CompanyId (FK), KeyHash (SHA256), PlainTextKey (nullable), Name, Scopes, IsActive, RateLimitPerMinute, CreatedBy (FK), CreatedAt, ExpiresAt, LastUsedAt
    - Indexes: Unique on KeyHash, Composite on (CompanyId, IsActive), (CompanyId, CreatedBy)
    - Purpose: Authenticate external API requests with scope-based permissions
    - Features: SHA256 hashing, rate limiting, expiration dates, scope-based authorization
    - Security: PlainTextKey visible to Owner only (role-based visibility)

21. **ApiKeyRequests**: API key approval workflow
    - Columns: Id (PK), CompanyId (FK), RequestedBy (FK), Name, Description, RequestedScopes, Status (Pending/Approved/Rejected), ReviewedBy (FK), ReviewedAt, ReviewNotes, CreatedAt, GeneratedApiKeyId (FK, nullable), ApprovedScopes, ApprovedRateLimit, ApprovedExpiresAt
    - Indexes: Composite on (CompanyId, Status, CreatedAt), (CompanyId, RequestedBy)
    - Purpose: Request-approval workflow for API key creation
    - Workflow: User request → Admin review → Approval generates ApiKey entity

### Entity Relationships

```
Company (1) ──< (many) Users
Company (1) ──< (many) ShiftTypes
Company (1) ──< (many) ShiftInstances
Company (1) ──< (many) ShiftAssignments
Company (1) ──< (many) TimeOffRequests
Company (1) ──< (many) SwapRequests
Company (1) ──< (many) UserNotifications
Company (1) ──< (many) DirectorCompanies
Company (1) ──< (many) UserJoinRequests
Company (1) ──< (many) RoleAssignmentAudits
Company (1) ──< (many) Configs
Company (1) ──< (many) AuditLog
Company (1) ──< (many) ProfileChangeAudit
Company (1) ──< (many) Chores
Company (1) ──< (many) TeamCalendar
Company (1) ──< (many) OnDuty
Company (1) ──< (many) OnDutyTypeConfig
Company (1) ──< (many) ApiKeys
Company (1) ──< (many) ApiKeyRequests

ShiftType (1) ──< (many) ShiftInstances
ShiftInstance (1) ──< (many) ShiftAssignments

TeamCalendar (1) ──< (many) TeamCalendarMember

Users (1) ──< (many) ShiftAssignments (as employee)
Users (1) ──< (many) ShiftAssignments (as trainee, optional)
Users (1) ──< (many) TimeOffRequests
Users (1) ──< (many) SwapRequests
Users (1) ──< (many) UserNotifications
Users (1) ──< (many) DirectorCompanies
Users (1) ──< (many) AuditLog
Users (1) ──< (many) ProfileChangeAudit (as subject)
Users (1) ──< (many) ProfileChangeAudit (as changer)
Users (1) ──< (many) Users (as manager - self-referencing FK)
Users (1) ──< (many) Chores
Users (1) ──< (many) TeamCalendar (as owner)
Users (1) ──< (many) TeamCalendarMember (as member)
Users (1) ──< (many) OnDuty
Users (1) ──< (many) ApiKeys (as creator)
Users (1) ──< (many) ApiKeyRequests (as requester)

ApiKeyRequest (1) ──> (optional) ApiKey (via GeneratedApiKeyId)
```

### Notable Indexes & Constraints

#### Unique Constraints
- Companies.Slug (for URL routing)
- Users.Email (for authentication)
- ShiftAssignments: (CompanyId, ShiftInstanceId, UserId) - prevent duplicate assignments
- DirectorCompanies: (UserId, CompanyId) where IsDeleted=0 - one active mapping per user-company pair

#### Composite Indexes (for query performance)
- ShiftTypes: (CompanyId, Key) - fast lookup by company and shift type
- TimeOffRequests: (CompanyId, UserId, StartDate) - fast user time-off queries
- SwapRequests: (CompanyId, Status, CreatedAt) - pending request queries
- UserNotifications: (CompanyId, UserId, CreatedAt) - user notification feed
- RoleAssignmentAudits: (CompanyId, TargetUserId, Timestamp) - audit log queries

---

## 5. Ongoing Processes and Ideas

### Completed Work (as of 2025-10-23)

#### Phase 1: Multi-Tenant Architecture ✅
- CompanyId added to all tenant-scoped entities
- EF Core query filters for automatic tenant scoping
- CompanyIdInterceptor for auto-injection on SaveChanges
- Director role for cross-company management

#### Phase 2: Testing & Documentation ✅
- 15 unit tests for DirectorService
- Complete migration rollback scripts (12 of 15 migrations)
- Comprehensive project documentation (context.md, implementation summaries)
- Security audit and vulnerability fixes

#### Phase 3: Security Hardening ✅
- Fixed ISSUE-003: Hardcoded seed credentials (now uses environment variables)
- Fixed ISSUE-006: Diagnostic page authorization (admin-only)
- Added health check endpoint (/health)
- PBKDF2 password hashing (100k iterations, SHA256)
- Comprehensive security audit (COMPREHENSIVE_SECURITY_AUDIT_REPORT.md)

#### Phase 4: Trainee Role Feature ✅ (Complete)
- **Design**: TRAINEE_ROLE_DESIGN.md
- **Implementation**: TRAINEE_ROLE_IMPLEMENTATION_COMPLETE.md
- **Features**:
  - TraineeService for shadowing management (Services/TraineeService.cs)
  - Conflict detection (prevent double-booking)
  - Automatic cancellation on time-off approval
  - Automatic cancellation on role change to Employee
  - Notification system integration (4 new notification types)

#### Phase 5: Employee Profile Enhancements ✅ (Complete - 2025-10-19)
- **Design**: EMPLOYEE_PROFILE_ENHANCEMENTS_DESIGN.md
- **Implementation**: EMPLOYEE_PROFILE_ENHANCEMENTS_IMPLEMENTATION_SUMMARY.md
- **Migration**: #13 AddEmployeeProfileEnhancements
- **Features**:
  - Avatar upload with SixLabors.ImageSharp (300x300px resizing)
  - 15 new profile fields (PreferredName, Phone, City, DateOfBirth, Department, JobTitle, HireDate, etc.)
  - Employee self-service profile page (/My/Profile)
  - Manager full-edit profile page (/Admin/EditProfile)
  - ProfileService with change auditing
  - AvatarService with multi-tenant file isolation
  - ProfileChangeAudit table for change history

#### Phase 6: Audit Log & Analytics Dashboard ✅ (Complete - 2025-10-19)
- **Design**: AUDIT_AND_ANALYTICS_DESIGN.md
- **Implementation**: AUDIT_AND_ANALYTICS_IMPLEMENTATION_SUMMARY.md
- **Migration**: #12 AddAuditLogAndAnalytics
- **Features**:
  - AuditLogService with IP address and user agent tracking
  - AuditLog table with JSON details storage
  - Audit Log viewer page (/Admin/AuditLog) with filtering and CSV export
  - AnalyticsService with 12 analytics methods and 5-minute caching
  - Analytics Dashboard (/Admin/Analytics) with Chart.js visualizations
  - Employee, team, swap, and time-off analytics
  - Back-to-back shift detection and warnings

#### Phase 7: Mail Service Infrastructure ✅ (Complete - 2025-10-19)
- **Documentation**: MAIL_SERVICE_DOCUMENTATION.md, MAIL_SERVICE_IMPLEMENTATION_SUMMARY.md
- **Features**:
  - MailService (IMailService) with MailKit + MimeKit
  - SMTP configuration support
  - Plain text, HTML, and templated email support
  - Async/await pattern for performance
  - Optional configuration (system works without email)

#### Phase 8: Batch Approval & UI Enhancements ✅ (Complete - 2025-10-20)
- **Documentation**: BATCH_APPROVAL_SUMMARY.md, UI_UX_ENHANCEMENTS_IMPLEMENTATION.md
- **Features**:
  - Batch approve/decline for time-off and swap requests
  - Enhanced breadcrumb navigation with Schema.org markup
  - Table view for calendar (/Calendar/Table)
  - Dark mode support (already present, verified)
  - Improved accessibility and keyboard navigation

#### Phase 9: Complete Localization ✅ (Complete - 2025-10-23)
- **Coverage**: 100% localization coverage across all pages
- **Languages**: English (en-US) and Hebrew (he-IL)
- **Pages Localized**: Home, My Shifts, My Requests, My Notification Center, Error page, Access Denied, Assignment Management
- **RTL Support**: Full right-to-left layout for Hebrew
- **Resources**: 1,600+ localized strings in SharedResources.resx

#### Phase 10: API Key Management & RESTful API Layer ✅ (Complete - 2025-10-29)
- **Features**:
  - API key lifecycle management (request → approval → activation → refresh → revoke)
  - Role-based key visibility (Owner sees PlainTextKey, others see masked version)
  - SHA256 key hashing for secure storage
  - Per-key rate limiting with sliding window (1-1000 req/min)
  - Scope-based authorization (users:read/write, shifts:read/write, requests:read/write, notifications:read)
  - RESTful API endpoints (Users, Shifts, Requests, Notifications)
  - Three-tier API middleware (logging, authentication, rate limiting)
  - RFC-7807 Problem Details for error responses
  - Comprehensive audit logging for all API operations
- **Migrations**: #16 (AddApiKeyManagement), #17 (AddPlainTextKeyToApiKey)
- **Models**: ApiKey, ApiKeyRequest
- **Services**: ApiKeyService (11 methods), RateLimitingService, ValidationService, SecurityLogger
- **Controllers**: UsersController, ShiftsController, RequestsController, NotificationsController
- **Admin Page**: /Admin/ApiKeys for request approval and key management
- **Bug Fixes**: Fixed duplicate ShiftTypes seeding issue, added shift type deletion functionality

### Upcoming Features (Planned)

#### 1. Export & Print Schedules (24-32 hours)
- **Goal**: Enable schedule sharing via Excel/PDF exports
- **Features**:
  - Export to Excel (ClosedXML library)
  - Export to PDF (QuestPDF or iTextSharp)
  - Print-friendly web view
  - Manager exports (any scope), Employee exports (own schedule only)
- **Status**: ⏳ Planned, not started
- **Priority**: High

#### 2. Shift Reminder Notifications (16-24 hours)
- **Goal**: Automated pre-shift reminders (6h and 2h before shift start)
- **Features**:
  - Hangfire background job scheduler
  - Two recurring jobs: 6-hour reminder (every 30 min), 2-hour reminder (every 15 min)
  - User preferences (enable/disable reminders)
  - Duplicate prevention (ShiftReminder tracking table)
  - Timezone-aware calculations
- **Migration**: AddShiftReminders
- **Status**: ⏳ Planned, not started
- **Priority**: Medium

#### 3. Announcements Feed (24-32 hours)
- **Goal**: Top-down communication from leadership to employees
- **Features**:
  - Announcement creation (title, message, priority, expiry date)
  - File attachments (PDF, images, max 5MB)
  - Multi-company targeting (Directors choose companies)
  - Acknowledgement tracking ("X of Y employees acknowledged")
  - Daily expiry job (mark old announcements inactive)
- **Migration**: AddAnnouncementsFeed
- **Status**: ⏳ Planned, not started
- **Priority**: Medium

### Open Issues & Tasks

Documented in `tasks.md`:

#### Resolved Issues (All Critical/High Priority Fixed)
- ✅ ISSUE-001: EF Core version mismatch (fixed: upgraded to 9.0.9)
- ✅ ISSUE-002: Missing rollback scripts (fixed: all 11 migrations have rollback SQL)
- ✅ ISSUE-003: Hardcoded seed credentials (fixed: environment variables)
- ✅ ISSUE-004: No health check endpoint (fixed: added /health)
- ✅ ISSUE-005: Non-transactional PRAGMA operations (documented: MIGRATIONS.md)
- ✅ ISSUE-006: Diagnostic page accessible to all (fixed: admin-only authorization)

#### Future Enhancements (Not Scheduled)
- ENHANCEMENT-001: Add CI/CD pipeline (GitHub Actions workflow)
- ENHANCEMENT-002: Add integration tests (0% coverage for Razor Pages)
- Security improvements: Rate limiting, security headers (X-Frame-Options, CSP, HSTS)
- Database encryption (SQLCipher for production)
- Email notifications (currently in-app only)
- Mobile app (iOS/Android)
- Auto-scheduling algorithm (AI-based)
- Time tracking & attendance (clock in/out)

### Development Workflow

#### Git Branching Strategy
- **Main branch**: `release` (protected, production-ready)
- **Feature branches**: `feature/<feature-name>` (short-lived, 3-5 days max)
- **Merge strategy**: Pull Request with approval required
- **Commit format**: `[Feature] Description` with bullet points

#### Migration Workflow
- One migration per feature branch
- Test cycle: up → down → up
- Generate rollback script: `dotnet ef migrations script <Previous> <Current> --output Migrations/rollback/...`
- Never modify existing migrations (always create new ones)

#### Testing Protocol
- Unit tests: 80% coverage target for services
- Integration tests: 60% coverage target for Razor Pages
- Manual testing checklist before PR merge
- Performance benchmarks: <5s for exports, <3s for analytics queries

---

## 6. FAQ / Common Tasks

### How do I run the application locally?

```bash
# Clone repository
git clone <repository-url>
cd ShiftManager

# Restore dependencies
dotnet restore

# Run database migrations
dotnet ef database update

# Run application
dotnet run

# Navigate to http://localhost:5000
# Default login: admin@local / admin123
```

### How do I create a new migration?

```bash
# Create migration
dotnet ef migrations add <MigrationName>

# Review generated migration
# Edit Migrations/<Timestamp>_<MigrationName>.cs if needed

# Apply migration
dotnet ef database update

# Test rollback
dotnet ef database update <PreviousMigrationName>

# Re-apply
dotnet ef database update

# Generate rollback script
dotnet ef migrations script <Previous> <Current> --output Migrations/rollback/<Number>_<Name>_Rollback.sql
```

**Reference**: MIGRATIONS.md (complete migration safety guide)

### How do I reset the database to clean state?

**Windows (PowerShell):**
```powershell
rm app.db
cp seed.db app.db
```

**Linux/macOS:**
```bash
rm app.db
cp seed.db app.db
```

**Alternative (rebuild from migrations):**
```bash
dotnet ef database drop
dotnet ef database update
```

### How does multi-tenant isolation work?

**Automatic Scoping via EF Core Query Filters:**
- TenantResolver reads CompanyId from authenticated user claims (Services/TenantResolver.cs)
- Query filters in AppDbContext.cs:148-167 auto-inject `WHERE CompanyId = @currentCompanyId`
- CompanyIdInterceptor auto-sets CompanyId on SaveChanges for new entities (Data/CompanyIdInterceptor.cs)

**Example Flow:**
```
User Login → CompanyId claim → TenantResolver → Query Filter
  → SELECT * FROM ShiftInstances WHERE CompanyId = 1
  → INSERT INTO ShiftAssignments (CompanyId, ...) VALUES (1, ...)
```

**Manual Override (Director role):**
- Directors bypass query filters using `IgnoreQueryFilters()` in LINQ queries
- DirectorService validates access via DirectorCompanies table

**Reference**: Program.cs:49-62, AppDbContext.cs:148-167

### How do I add a new user role?

1. **Add enum value**: Models/Support/Enums.cs (UserRole enum, line 9)
2. **Update authorization policies**: Program.cs:75-82
3. **Update DirectorService.CanAssignRole()**: Services/DirectorService.cs:110-137
4. **Create migration** if database schema changes
5. **Update UI** to show new role in dropdowns
6. **Add tests** for new role permissions

### How do I add a new notification type?

1. **Add enum value**: Models/Support/Enums.cs (NotificationType, line 33)
2. **Create notification method** in NotificationService.cs (follow pattern at lines 59-96)
3. **Update notification rendering** in Pages/My/NotificationCenter.cshtml
4. **Add localization strings** in Resources/SharedResources.resx (en-US, he-IL)

### How do I run tests?

```bash
# Run all tests
dotnet test

# Run with verbosity
dotnet test --verbosity detailed

# Run specific test class
dotnet test --filter "FullyQualifiedName~DirectorServiceTests"

# Run with coverage (requires coverlet)
dotnet test /p:CollectCoverage=true
```

**Current Coverage**: 15 tests (DirectorService only)
**Test Location**: ShiftManager.Tests/UnitTests/Services/DirectorServiceTests.cs

### How do I change the database from SQLite to PostgreSQL?

1. **Install package**: `dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL`
2. **Update connection string** in appsettings.json:
   ```json
   "ConnectionStrings": {
     "Default": "Host=localhost;Database=shiftmanager;Username=postgres;Password=***"
   }
   ```
3. **Update DbContext registration** in Program.cs:58:
   ```csharp
   opt.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
   ```
4. **Drop and recreate migrations** (SQLite migrations incompatible):
   ```bash
   rm -rf Migrations/
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

### How do I deploy to production?

**Pre-Deployment Checklist:**
- [ ] Set `SEED_ADMIN_PASSWORD` environment variable (required, Program.cs:104)
- [ ] Backup production database
- [ ] Test migrations on staging environment
- [ ] Run all tests (`dotnet test`)
- [ ] Review security settings (HTTPS, HSTS, security headers)

**Deployment Steps:**
```bash
# 1. Backup database
cp app.db app.db.backup.$(date +%Y%m%d)

# 2. Pull latest code
git pull origin main

# 3. Build
dotnet build --configuration Release

# 4. Apply migrations
dotnet ef database update

# 5. Run application
dotnet run --configuration Release

# 6. Verify health check
curl http://localhost:5000/health
# Expected: "Healthy"
```

**Reference**: FINAL_DEPLOYMENT_SUMMARY.txt, README.md:178-192

### Where is the authentication logic?

**Login**: Pages/Auth/Login.cshtml.cs
- Password verification: PBKDF2 with 100k iterations (Models/PasswordHasher.cs)
- Claims creation: CompanyId, UserId, Role, Email (Login.cshtml.cs:~60-80)
- Cookie authentication: CookieAuthenticationDefaults (Program.cs:64-73)

**Signup**: Pages/Auth/Signup.cshtml.cs
- Creates UserJoinRequest (pending approval)
- Managers approve via Admin/Users.cshtml

**Authorization Policies**: Program.cs:75-82
- IsManagerOrAdmin: Manager, Owner, Director
- IsAdmin: Owner only
- IsDirector: Owner, Director
- IsOwnerOrDirector: Owner, Director

**Page-Level Authorization**:
- All pages require authentication (Program.cs:38)
- Exceptions: Login, Signup (Program.cs:39-40)
- Policy enforcement: `[Authorize(Policy = "IsAdmin")]` attribute on PageModel class

### How do I add a new localization language?

1. **Add culture** to Program.cs:25:
   ```csharp
   var supportedCultures = new[] { "en-US", "he-IL", "es-ES" };
   ```
2. **Create resource file**: Resources/SharedResources.es-ES.resx
3. **Translate strings** for all keys (copy from SharedResources.en-US.resx)
4. **Update language toggle** in ViewComponents/LanguageToggleViewComponent.cs
5. **Test RTL layout** if language is right-to-left (like Hebrew)

**Current Languages**: en-US (English), he-IL (Hebrew with RTL support)

---

## 7. Error Handling and Limitations

### Known Limitations

#### 1. SQLite Database Limitations
- **Max Concurrent Connections**: SQLite supports single-writer mode
- **Production Use**: Not recommended for high-traffic production (migrate to PostgreSQL)
- **Encryption**: Database stored unencrypted (use SQLCipher for sensitive data)
- **Max Database Size**: Practical limit ~140 TB (sufficient for 10+ years of shift data)

#### 2. Test Coverage Gaps
- **Service Layer**: 17% coverage (only DirectorService has tests)
  - Missing: NotificationService, TraineeService, ConflictChecker, LocalizationService
- **Presentation Layer**: 0% coverage (no Razor Page tests)
- **Integration Tests**: 0% (WebApplicationFactory configured but unused)

**Reference**: tasks.md:269-296 (ENHANCEMENT-002)

#### 3. Migration Warnings (ISSUE-005)
- **Affected Migrations**: #3, #8, #11
- **Issue**: PRAGMA operations cannot be executed in transactions
- **Impact**: If migration fails mid-execution, database may be left inconsistent
- **Mitigation**: Always backup before migrations, tested rollback procedures

**Reference**: MIGRATIONS.md:10-40, tasks.md:152-190

#### 4. Security Gaps
- **Rate Limiting**: No rate limiting on login attempts (brute-force vulnerability)
- **Security Headers**: Missing X-Frame-Options, CSP, HSTS
- **Session Timeout**: Cookie expiration 7 days (may be too long for sensitive environments)
- **Password Policy**: No complexity requirements (minimum 6 characters)

**Reference**: tasks.md:254-258, README.md:254-258

#### 5. Performance Bottlenecks
- **N+1 Queries**: Several pages load related entities without `.Include()` (potential N+1)
- **Unbounded Queries**: No pagination on large result sets (e.g., all notifications)
- **Missing Indexes**: Some query patterns not optimized (e.g., SwapRequest by Status)

**Reference**: project.md (section on performance optimization)

### Unreadable Files

#### Binary Files (Excluded from Analysis)
- **Location**: `bin/`, `obj/` directories
- **Reason**: Compiled assemblies and build artifacts (not source code)
- **Handling**: Excluded via .gitignore

#### Database Files
- **app.db**: Active development database (176 KB, SQLite binary format)
- **seed.db**: Clean seed database for reset (176 KB, SQLite binary format)
- **Reason**: Binary SQLite format, not human-readable
- **Inspection**: Use `sqlite3 app.db` or DB Browser for SQLite

#### Generated Files
- **obj/Debug/net8.0/\*.cs**: Auto-generated Razor view code
- **Reason**: Generated by ASP.NET Core at build time
- **Handling**: Ignored (regenerated on every build)

### Files with Warnings

#### PRAGMA Migration Files
- **Migrations/20250928201142_AddNavigationProperties.cs**
- **Migrations/20250930222145_UpdateJoinRequestPasswordTypes.cs**
- **Migrations/20251004215820_AddTraineeRoleAndShadowing.cs**
- **Warning**: Contains non-transactional PRAGMA operations
- **Handling**: Documented in MIGRATIONS.md with safe migration procedures

### Corrupted or Missing Files

**None identified** - All expected source files are present and readable.

### Environment-Specific Files

#### Local Development Files (Git-Ignored)
- **app.db**: Local database (use seed.db to reset)
- **wwwroot/avatars/\***: User-uploaded avatars (not committed)
- **wwwroot/announcements/\***: Announcement attachments (not committed, future feature)

**Note**: These files are excluded via .gitignore to prevent committing user data.

---

## Validation Notes

### Section 1 (Summary): ✅ Complete
- Comprehensive project overview with key features, tech stack, and current status
- All references to README.md, ShiftManager.csproj, and Program.cs validated

### Section 2 (File Overviews): ✅ Complete
- All 120+ C# files catalogued with purpose, key functions, and line references
- Core application files, data layer, models, services, middleware, pages, and view components documented
- 15 migrations listed with known issues flagged
- New services added: AuditLogService, AnalyticsService, ProfileService, AvatarService, MailService

### Section 3 (Settings and Configuration): ✅ Complete
- appsettings.json and environment variables fully documented
- Production requirements clearly stated (SEED_ADMIN_PASSWORD)
- Feature flags and configuration options explained

### Section 4 (Database Structure): ✅ Complete
- All 15 tables documented with columns, relationships, and indexes
- New tables added: AuditLog, ProfileChangeAudit, Chores
- Extended Users table with 15 new profile fields
- Composite indexes and unique constraints listed with references to AppDbContext.cs
- ERD-style relationship diagram provided in text format

### Section 5 (Ongoing Processes and Ideas): ✅ Complete
- Completed work (9 phases) documented with references
- Remaining planned features summarized (3 features)
- Open issues from tasks.md included (all critical issues resolved)
- Git workflow and testing protocols documented
- Recent completions: Employee profiles, audit log, analytics, mail service, batch approval, full localization

### Section 6 (FAQ / Common Tasks): ✅ Complete
- 12+ common developer tasks with exact commands and code references
- Multi-tenant isolation explained with flow diagram
- Database reset, migration creation, testing, and deployment procedures
- All references include file paths and line numbers

### Section 7 (Error Handling and Limitations): ✅ Complete
- 5 known limitations documented (SQLite, test coverage, migrations, security, performance)
- Binary and generated files catalogued with explanations
- Migration warnings cross-referenced to MIGRATIONS.md and tasks.md
- No corrupted files identified

---

## Quick Reference Card

### Project Stats
- **Lines of Code**: ~20,000+ (C# source only)
- **Files**: 120+ C# files, 40+ Razor views
- **Database**: 15 tables, 15 migrations
- **Tests**: 15 unit tests (DirectorService only - needs expansion)
- **Documentation**: 15+ comprehensive docs (design docs, implementation summaries, context.md)
- **Localization**: 1,600+ resource strings (English/Hebrew)

### Key Commands
```bash
# Run app
dotnet run

# Run tests
dotnet test

# Create migration
dotnet ef migrations add <Name>

# Apply migrations
dotnet ef database update

# Reset database
rm app.db && cp seed.db app.db

# Health check
curl http://localhost:5000/health
```

### Default Credentials
- **Owner**: admin@local / admin123
- **Director**: director@local / director123 (dev only)

### Important File Locations
- **Database**: `app.db` (SQLite)
- **Seed Data**: `seed.db` (clean baseline)
- **Migrations**: `Migrations/` (17 migrations)
- **Rollback Scripts**: `Migrations/rollback/` (12 files, 5 pending)
- **Services**: `Services/` (20 services, including 4 API services)
- **Models**: `Models/` (21 entities + Analytics DTOs + API models)
- **Pages**: `Pages/` (40+ Razor Pages)
- **Controllers**: `Controllers/Api/` (4 API controllers)
- **Middleware**: `Middleware/` (4 middleware components)
- **Resources**: `Resources/` (English + Hebrew .resx files)
- **View Components**: `ViewComponents/` + `Views/Shared/Components/`
- **Avatars**: `wwwroot/avatars/{companyId}/` (user-uploaded, git-ignored)

### Multi-Tenant Architecture
- **Tenant Resolver**: `Services/TenantResolver.cs` (reads CompanyId claim)
- **Query Filters**: `Data/AppDbContext.cs:148-167` (auto-inject WHERE CompanyId=X)
- **Interceptor**: `Data/CompanyIdInterceptor.cs` (auto-set CompanyId on insert)
- **Override**: `IgnoreQueryFilters()` for Director cross-company queries

### Authorization Roles
- **Owner** (0): Full system access, all companies
- **Manager** (1): Company admin, manage users/shifts
- **Employee** (2): View shifts, request time-off/swaps
- **Director** (3): Multi-company manager, cross-tenant access
- **Trainee** (4): Shadow employees, limited permissions

### Next Steps for AI Onboarding
1. Review this context.md file completely
2. Read implementation summary docs for recently completed features
3. Read MIGRATIONS.md for database safety guidelines
4. Examine Program.cs and AppDbContext.cs for architecture understanding
5. Review AuditLogService.cs and AnalyticsService.cs for new service patterns
6. Explore localization in Resources/SharedResources.resx (1,600+ strings)
7. Ready to answer questions or contribute to development

---

## Changelog (Recent)

### 2025-10-29 - API Key Management & RESTful API Layer
**Added:**
- ✅ API key lifecycle management system (request → approval → activation → refresh → revoke)
- ✅ RESTful API endpoints for Users, Shifts, Requests, and Notifications
- ✅ Three-tier API middleware (request logging, authentication, rate limiting)
- ✅ ApiKeyService with 11 methods for complete key management
- ✅ RateLimitingService with sliding window rate limiting (1-1000 req/min)
- ✅ ValidationService for scope and rate limit validation
- ✅ SecurityLogger for API security event tracking
- ✅ ApiKeys and ApiKeyRequests database tables
- ✅ Admin page for API key management (/Admin/ApiKeys)
- ✅ Role-based key visibility (Owner sees PlainTextKey, others see masked)
- ✅ SHA256 key hashing for secure storage
- ✅ Scope-based authorization system (users, shifts, requests, notifications)
- ✅ RFC-7807 Problem Details for standardized error responses
- ✅ Comprehensive audit logging for all API operations

**Fixed:**
- ✅ Duplicate ShiftTypes seeding bug (added company-specific validation in Program.cs:134-171)
- ✅ Added shift type deletion functionality to Admin/ShiftTypes page
- ✅ Fixed HTTP 400 error from nested forms (separate delete form for shift types)

**Database:**
- ✅ Migration #16: AddApiKeyManagement (ApiKeys and ApiKeyRequests tables)
- ✅ Migration #17: AddPlainTextKeyToApiKey (nullable PlainTextKey column)

**Technical Details:**
- 4 new API controllers with comprehensive CRUD operations
- 4 new API services (ApiKeyService, RateLimitingService, ValidationService, SecurityLogger)
- 3 new middleware components (ApiRequestLoggingMiddleware, ApiAuthenticationMiddleware, ApiRateLimitingMiddleware)
- 2 new database tables (ApiKeys with 11 columns, ApiKeyRequests with 13 columns)
- 7 supported scopes (users:read/write, shifts:read/write, requests:read/write, notifications:read)
- Multi-tenant isolation via X-API-Key header authentication
- Rate limit headers (X-RateLimit-Limit, X-RateLimit-Remaining)

### 2025-11-02 - Public Section Employee Access & Authorization Updates
**Added:**
- ✅ Employee access to Public section (Chores and OnDuty pages)
- ✅ Separate view/edit authorization policies (CanViewChores, CanViewOnDuty, CanEditChores, CanEditOnDuty)
- ✅ Employee-friendly info modals for empty calendar dates
- ✅ Conditional UI rendering based on user permissions (delete buttons hidden for employees)
- ✅ JavaScript permission checks with canEdit variable
- ✅ Localization strings for employee info messages (English & Hebrew)

**Changed:**
- ✅ Pages/Public/OnDuty.cshtml: Changed authorization from CanEditOnDuty to CanViewOnDuty
- ✅ Pages/Public/Chores.cshtml: Changed authorization from CanEditChores to CanViewChores
- ✅ Program.cs: Added 4 new authorization policies (CanViewChores, CanViewOnDuty, CanEditChores, CanEditOnDuty)
- ✅ Calendar cell onclick handlers: Changed from direct modal opening to permission-aware handleCellClick()
- ✅ Resources/SharedResources.resx: Added Information, OK, NoOnDutyContactManager, NoChoreContactManager keys
- ✅ Resources/SharedResources.he-IL.resx: Added Hebrew translations for new localization keys

**Features:**
- Employee users can now view Chores and OnDuty calendars (previously got Access Denied)
- Clicking on empty dates shows informational message instead of create modal for employees
- Clicking on existing chores/on-duty shows details modal without delete buttons for employees
- Manager+ users retain full create/edit/delete capabilities
- Graceful UX degradation based on role permissions

**Technical Details:**
- Authorization: Implemented view/edit policy separation pattern
- UI: Added IAuthorizationService injection in views for permission-based rendering
- JavaScript: Added canEdit boolean variable and conditional modal opening logic
- Localization: 4 new resource strings with English and Hebrew translations
- Message: "There isn't a chore/on-duty for this day. Contact your team manager if you think that's wrong."

### 2025-11-12 - My Team Calendars Feature (Complete Implementation)
**Added:**
- ✅ **My Team Calendars** - Personal week-view calendars for tracking team member availability:
  - Create/rename/delete named calendars (e.g., "Project X Team", "QA Crew")
  - Two-pane member management with search functionality
  - Week navigation (previous/next/this week)
  - Modern card-based UI with member avatars and initials
  - Status priority system: Vacation > After > On-Duty > Shift > Chore > Free
  - Role-based navigation restrictions (Regular users: no clicks, Assigners: chores only, Manager+: full access)
  - Visual icons for status types (🧳 vacation, 🌙 after, 🛡️ on-duty, ⏱️ shift, 🔧 chore)
  - Informative tooltips showing event details and navigation destination
  - Responsive design (desktop to mobile with adaptive layouts)
  - Full localization support (English/Hebrew)
- ✅ **On-Duty Management**: Public/OnDuty.cshtml page with calendar view
  - Two types: Hakam (🛡️) and Lead (⭐)
  - Manager+ create/edit/delete capabilities
  - Employee view-only access
  - OnDutyTypeConfig table for configurable on-duty types
- ✅ **Database Tables**:
  - TeamCalendar (Id, OwnerUserId, Name, CompanyId, CreatedAt, UpdatedAt)
  - TeamCalendarMember (Id, TeamCalendarId, MemberUserId, AddedAt)
  - OnDuty (Id, CompanyId, UserId, Date, Type, CreatedAt, CanceledAt)
  - OnDutyTypeConfig (Id, CompanyId, TypeKey, DisplayName, IconEmoji, ColorHex, SortOrder, IsActive)
- ✅ **Services**:
  - TeamCalendarService: CRUD operations for calendars and member management
  - TeamCalendarEventAggregator: Priority-based event aggregation with vacation/after semantics
  - OnDutyService: On-duty assignment management
  - BusyUserService: User availability checking across all event types
- ✅ **API Endpoints** (TeamCalendarsController):
  - GET /api/team-calendars - List user's calendars
  - POST /api/team-calendars - Create calendar
  - PUT /api/team-calendars/{id} - Rename calendar
  - DELETE /api/team-calendars/{id} - Delete calendar
  - GET /api/team-calendars/{id}/week?date=YYYY-MM-DD - Get week view with status badges
  - GET /api/team-calendars/{id}/members - Get current/available members
  - PUT /api/team-calendars/{id}/members - Update member list
  - Uses cookie authentication (not API keys)

**Features:**
- **Vacation Semantics**: Extends until 1 PM the day after end date
- **After/אפטֵר Semantics**: 4 PM start day → 1 PM next day (two-day span)
- **Priority System**: Vacation > After > On-Duty > Shift > Chore > Free
- **Role-Based Navigation Matrix**:
  - Regular User: No navigation (view-only)
  - Assigner: Can only navigate to /Public/Chores
  - Manager/Director/Owner: Full navigation (all event types)
- **Target URLs**:
  - Vacations/After → /Admin/TimeOff
  - On-Duty → /Public/OnDuty
  - Shifts → /Calendar/Table
  - Chores → /Public/Chores
- **Modern UI**: Card-based design with member avatars, hover effects, and smooth transitions
- **Mobile Responsive**: Adaptive layout that switches to mobile-friendly grid on smaller screens

**Changed:**
- ✅ Pages/Shared/_Layout.cshtml: Added "👥 My Team" navigation for all users
- ✅ Middleware/ApiRequestLoggingMiddleware.cs: Skip logging for /api/team-calendars
- ✅ Middleware/ApiAuthenticationMiddleware.cs: Allow cookie auth for /api/team-calendars
- ✅ Program.cs: Added TeamCalendarService, TeamCalendarEventAggregator, OnDutyService, BusyUserService

**Technical Details:**
- 4 new database tables with multi-tenant isolation
- 4 new services with full business logic
- 1 API controller with 7 RESTful endpoints
- Priority-based event aggregation algorithm
- Vacation "until 1 PM next day" rule correctly implemented
- After/אפטֵר two-day span (4 PM → 1 PM next day)
- Role-based targetUrl filtering in GetTargetUrlForRole()
- Client-side tooltip generation with role-aware messaging
- CSS Grid layout with responsive breakpoints (1200px, 968px, 768px, 480px)

**Migrations:**
- ✅ 20251101000000_AddCustomNameToShiftType: Added CustomName and SortOrder to ShiftType
- ✅ 20251101160411_AddTimeOffTypeToTimeOffRequest: Added Type column to TimeOffRequest
- ✅ 20251101165051_AddOnDutyAndAssignerRole: Added OnDuty table and Assigner role
- ✅ 20251101221554_AddOnDutyTypeConfig: Added OnDutyTypeConfig table
- ✅ 20251101232438_AddApproverIdToTimeOffRequest: Added ApproverId to TimeOffRequest

### 2025-11-11 - My Overview Page Redesign + Calendar Bug Fixes + UI Improvements
**Added:**
- ✅ **Complete My Overview Page Redesign** (/My/Index):
  - Modern unified timeline feed showing Vacations, On-duty, Shifts, and Chores in one view
  - Sticky top bar with time range switcher (Week/Month) and type filters
  - Color-coded legend: Vacations=Blue, On-duty=Red, Shifts=Purple, Chores=Yellow
  - Grouped timeline sections: Today, Tomorrow, This Week, Later
  - View mode toggles: Upcoming, All, Past 30 days
  - Interactive stats bar chart with Count ↔ Hours toggle
  - Read-only detail drawer for item inspection (no edit affordances)
  - Conflict detection with warning badges (⚠) on overlapping items
  - Client-side filtering with empty state handling
  - Full Hebrew localization (40+ new keys)
  - Dark mode compatible design with smooth animations
  - Responsive layout with mobile support
- ✅ New partial view: Pages/My/_TimelineItem.cshtml for timeline rendering
- ✅ Complete backend rewrite: Pages/My/Index.cshtml.cs with 300 lines of query logic
  - TimelineItem record type with conflict detection
  - StatsData record type for analytics
  - Support for all 4 data types (Vacations, OnDuty, Shifts, Chores)
  - Trainee shadowing shift support
  - Date range and view mode filtering

**Fixed:**
- ✅ **Zero-Assignment Shift Visibility Bug** (all calendar views):
  - Shifts with 0 required staff now remain visible in Month, Week, and Day calendars
  - Changed visibility filter from `Required > 0 || Assigned > 0` to `InstanceId > 0`
  - Users can now bump shift counts back up without recreating shifts
  - Fixed "Concurrent update detected" error caused by invisible ghost shifts
  - Applied fix consistently across Pages/Calendar/Month.cshtml, Week.cshtml, and Day.cshtml

- ✅ **Shift Type Localization Bug** (all calendar views):
  - Fixed custom shift types showing localization keys (e.g., "ShiftType_CUSTOM") instead of names
  - Implemented conditional logic: Built-in types use Localizer, custom types use ShiftTypeName
  - Applied to all shift type displays in Month, Week, Day, Table calendars and Assignments page
  - Fixed tooltip titles, badge displays, and shift name displays across all views

- ✅ **Dropdown Menu Z-Index Issue** (global fix):
  - Fixed admin dropdown menu being hidden behind page content (overview-topbar)
  - Updated z-index hierarchy: topbar=1000, dropdown=10001, dropdown menu=10002
  - Dropdowns now always visible above all page content including sticky elements
  - Fixed stacking context conflicts across entire application

- ✅ **Code Quality**:
  - Fixed Razor syntax error in Index.cshtml (removed nested @{ } in else block)
  - Removed unused variables in Services/ConflictChecker.cs (hasOverlap, overlappingShiftName)
  - Clean build: 0 warnings, 0 errors

**Changed:**
- ✅ Pages/My/Index.cshtml: Complete redesign (814 lines with styling & JavaScript)
- ✅ Pages/My/Index.cshtml.cs: Complete rewrite (300 lines of backend logic)
- ✅ Pages/Calendar/Month.cshtml: Fixed shift visibility filter (line 109)
- ✅ Pages/Calendar/Week.cshtml: Fixed shift visibility filter (line 87)
- ✅ Pages/Calendar/Day.cshtml: Fixed shift visibility filter (line 77)
- ✅ Pages/Calendar/Month.cshtml: Fixed shift type localization (lines 117, 122, 130, 134)
- ✅ Pages/Calendar/Week.cshtml: Fixed shift type localization (lines 95, 100, 108, 112)
- ✅ Pages/Calendar/Day.cshtml: Fixed shift type localization (lines 87, 92, 100, 104, 168)
- ✅ Pages/Calendar/Table.cshtml: Fixed shift type localization (line 563)
- ✅ Pages/Assignments/Manage.cshtml: Fixed shift type localization (line 17)
- ✅ wwwroot/css/site.css: Updated z-index hierarchy (topbar=1000, dropdown=10001/10002)
- ✅ Services/ConflictChecker.cs: Removed unused variables (lines 54-55)
- ✅ Resources/SharedResources.resx: Added 40 new localization keys for My Overview
- ✅ Resources/SharedResources.he-IL.resx: Added 40 Hebrew translations

**New Localization Keys:**
- MyOverview, Week, Custom, TimeRangeSwitcher, Filters, Vacations, OnDuty, Chores
- Today, Tomorrow, ThisWeek, Later, Past, Upcoming, All, Past30Days
- MyStatsForThisPeriod, Count, Hours, ToggleCountHours
- NothingHereYet, NoItemsMatch, ClearFilters, Details, Conflict, ViewDetails
- Legend, Showing, Items, InDateRange, Vacation, After
- OnDutyHakam, OnDutyLead, Shadowing, Days, Day

**Technical Details:**
- Backend: Complete data aggregation for 4 entity types with conflict detection
- Frontend: Canvas-based chart rendering with dark mode support
- JavaScript: Client-side filtering, drawer functionality, responsive chart resizing
- Localization: Full bilingual support (English/Hebrew) for all new UI elements
- Performance: Efficient LINQ queries with proper includes and date range filtering
- Security: User ID validation with TryParse to prevent crashes
- UX: Grouped timeline, empty states, loading indicators, responsive design

---

**Document Generated**: 2025-11-12
**Project Version**: Production-Ready+ (Enhanced with My Team Calendars, On-Duty management, and priority-based event aggregation)
**Total Context Lines**: 1,650+
**Database Tables**: 21 tables
**Migrations**: 22 applied
**Validation Status**: ✅ All sections complete and cross-referenced
**Last Updated**: 2025-11-12 - My Team Calendars feature complete (priority aggregation, role-based nav, modern UI, responsive design)
