# ShiftManager Entry Points & Trace Maps

**Generated:** 2026-01-12
**Agent:** Antigravity Workspace Intake

---

## Primary Entry Points (Ranked by Importance)

### 1. 🚀 Web Application Entry Point (CRITICAL)

**File:** `Program.cs`
**Lines:** 1-456
**Purpose:** Application bootstrap, DI configuration, middleware pipeline

**First-Hop Call Chain:**
```
Program.cs (line 12)
    │
    ├── WebApplication.CreateBuilder(args)
    │
    ├── Service Registration (lines 22-190)
    │   ├── AddLocalization() → LocalizationService
    │   ├── AddRazorPages() → Razor page routing
    │   ├── AddDbContext<AppDbContext>() → EF Core with SQLite
    │   ├── AddAuthentication() → Cookie auth
    │   ├── AddAuthorization() → 8 policies
    │   └── AddScoped<*Service>() → 40+ services
    │
    ├── Database Migration & Seeding (lines 195-352)
    │   ├── db.Database.MigrateAsync()
    │   └── Seed: Company, ShiftTypes, Owner user
    │
    ├── Middleware Pipeline (lines 354-451)
    │   ├── UseStaticFiles()
    │   ├── UseRouting()
    │   ├── UseRequestLogging() → custom middleware
    │   ├── UseMiddleware<GriffinAuthenticationMiddleware>()
    │   ├── UseMiddleware<CompanyContextMiddleware>()
    │   ├── UseAuthentication()
    │   ├── UseAuthorization()
    │   ├── UseMiddleware<ApiRequestLoggingMiddleware>()
    │   ├── UseMiddleware<ApiAuthenticationMiddleware>()
    │   └── UseMiddleware<ApiRateLimitingMiddleware>()
    │
    └── app.Run() → Kestrel web server
```

---

### 2. 🔐 Authentication Entry Point

**File:** `Pages/Auth/Login.cshtml.cs`
**Size:** 16,444 bytes
**Purpose:** User login, credential verification, session creation

**Call Chain:**
```
/Auth/Login (GET)
    └── OnGetAsync() → Render login form

/Auth/Login (POST)
    │
    └── OnPostAsync()
        │
        ├── Validate input (Email, Password)
        │
        ├── Query: _db.Users.FirstOrDefaultAsync(u => u.Email == Email)
        │   └── (Query filters automatically add CompanyId = current)
        │
        ├── PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt)
        │   └── PBKDF2, 100k iterations, SHA256
        │
        ├── Create ClaimsPrincipal with claims:
        │   ├── ClaimTypes.NameIdentifier → UserId
        │   ├── ClaimTypes.Email → Email
        │   ├── ClaimTypes.Role → UserRole
        │   ├── ClaimTypes.Name → DisplayName
        │   └── "CompanyId" → User's CompanyId
        │
        ├── HttpContext.SignInAsync() → Create auth cookie
        │
        └── Redirect to /Calendar/Month (or returnUrl)
```

**Griffin ADFS Alternative:**
```
/Auth/Login (Griffin enabled)
    │
    ├── GriffinService.GetLoginUrlAsync() → Redirect to ADFS
    │
    └── /Auth/GriffinCallback (return from ADFS)
        │
        ├── GriffinService.ValidateTokenAsync()
        │
        ├── Auto-provision user if enabled
        │
        └── SignInAsync() → Same as above
```

---

### 3. 📅 Calendar Entry Points (Main UI)

**Files:**
- `Pages/Calendar/Month.cshtml.cs`
- `Pages/Calendar/Week.cshtml.cs`
- `Pages/Calendar/Day.cshtml.cs`

**Month View Call Chain:**
```
/Calendar/Month?date=2026-01-01 (GET)
    │
    └── OnGetAsync()
        │
        ├── Parse date parameter (default: today)
        │
        ├── Calculate month boundaries (first/last day)
        │
        ├── Query ShiftInstances for date range:
        │   _db.ShiftInstances
        │       .Include(si => si.ShiftType)
        │       .Include(si => si.Assignments)
        │           .ThenInclude(a => a.User)
        │       .Where(si => si.WorkDate >= start && si.WorkDate <= end)
        │   └── (Query filters: CompanyId = current)
        │
        ├── Query TimeOffRequests for date range
        │
        ├── Query OnDuties for date range (global table, no filter)
        │
        ├── Group by date for calendar rendering
        │
        └── Render Month view with shift cards
```

---

### 4. 👥 Employee Self-Service Entry Points

**Files:**
- `Pages/My/Index.cshtml.cs` — Dashboard/Timeline
- `Pages/My/Profile.cshtml.cs` — Profile management
- `Pages/My/Requests.cshtml.cs` — Time-off/swap requests
- `Pages/My/NotificationCenter.cshtml.cs` — Notifications

**My Overview (Dashboard) Call Chain:**
```
/My/Index (GET)
    │
    └── OnGetAsync()
        │
        ├── Get current user from claims
        │
        ├── Query user's shifts:
        │   _db.ShiftAssignments
        │       .Include(a => a.ShiftInstance)
        │           .ThenInclude(si => si.ShiftType)
        │       .Where(a => a.UserId == userId && a.ShiftInstance.WorkDate >= today)
        │
        ├── Query user's time-off requests:
        │   _db.TimeOffRequests.Where(t => t.UserId == userId)
        │
        ├── Query user's chores:
        │   _db.Chores.Where(c => c.UserId == userId && c.CanceledAt == null)
        │
        ├── Query user's on-duty assignments (global):
        │   _db.OnDuties.Where(o => o.UserId == userId && o.CanceledAt == null)
        │
        └── Render unified timeline view
```

---

### 5. ⚙️ Admin Entry Points

**Files:**
- `Pages/Admin/Users.cshtml.cs` — User management (51KB)
- `Pages/Admin/Companies.cshtml.cs` — Company management
- `Pages/Admin/Config.cshtml.cs` — System configuration
- `Pages/Admin/ShiftTypes.cshtml.cs` — Shift type setup
- `Pages/Admin/Analytics.cshtml.cs` — Workforce analytics
- `Pages/Admin/AuditLog.cshtml.cs` — Audit trail

**User Management Call Chain:**
```
/Admin/Users (GET)
    │
    └── OnGetAsync()
        │
        ├── Authorization check: IsManagerOrAdmin policy
        │
        ├── Query users:
        │   _db.Users.Where(u => u.IsActive)
        │   └── (Query filter: CompanyId = current)
        │
        ├── Query pending join requests:
        │   _db.UserJoinRequests.Where(jr => jr.Status == JoinRequestStatus.Pending)
        │
        └── Render user list + join request queue

/Admin/Users (POST - Create User)
    │
    └── OnPostCreateUserAsync()
        │
        ├── Validate role permission (DirectorService.CanAssignRole)
        │
        ├── Hash password: PasswordHasher.CreateHash()
        │
        ├── Create AppUser entity (CompanyId auto-injected)
        │
        ├── Log: AuditLogService.LogAsync("UserCreated")
        │
        └── Redirect back
```

---

### 6. 👑 Owner Entry Points

**Files (34 total in `/Pages/Owner/`):**
- `Index.cshtml.cs` — Owner dashboard
- `GriffinConfig.cshtml.cs` — ADFS configuration
- `EmailConfig.cshtml.cs` — Email service setup
- `DataLifecycle.cshtml.cs` — Archive/purge data
- `LanguageManagement.cshtml.cs` — Localization overrides
- `Programs.cshtml.cs` — Shift programs (Ops Console)
- `MasterPrograms.cshtml.cs` — Master program scheduling
- `SystemHealth.cshtml.cs` — System diagnostics

**Owner Dashboard Call Chain:**
```
/Owner/Index (GET)
    │
    └── OnGetAsync()
        │
        ├── Authorization: IsAdmin policy (Owner only)
        │
        ├── Load company statistics:
        │   ├── Total users count
        │   ├── Pending requests count
        │   ├── Active shifts count
        │   └── System health metrics
        │
        └── Render owner dashboard with quick actions
```

---

### 7. 🌐 API Entry Points (REST)

**Location:** `Controllers/Api/V1/`

**API Authentication Flow:**
```
API Request with X-API-Key header
    │
    └── ApiAuthenticationMiddleware.InvokeAsync()
        │
        ├── Extract X-API-Key from headers
        │
        ├── ApiKeyService.ValidateApiKeyAsync()
        │   ├── Hash incoming key (SHA256)
        │   ├── Query: _db.ApiKeys.FirstOrDefault(k => k.KeyHash == hash)
        │   ├── Check: IsActive, ExpiresAt
        │   └── Return ApiKey entity or null
        │
        ├── If invalid → 401 Unauthorized
        │
        ├── ApiRateLimitingMiddleware.InvokeAsync()
        │   ├── RateLimitingService.CheckRateLimitAsync()
        │   └── If exceeded → 429 Too Many Requests
        │
        ├── Set CompanyId claim from ApiKey.CompanyId
        │
        └── Continue to controller
```

**Example: GET /api/v1/users**
```
UsersController.GetUsers() (GET)
    │
    ├── Check API key has "users:read" scope
    │
    ├── Query users with pagination:
    │   _db.Users.Where(u => u.IsActive)
    │       .Skip(offset).Take(limit)
    │
    └── Return JSON: { data: [...], pagination: {...} }
```

---

### 8. 🔔 Background Service Entry Points

**File:** `Services/DailyNotificationJob.cs`
**Registration:** `Program.cs:164`

**Hosted Service Call Chain:**
```
Application Startup
    │
    └── DailyNotificationJob.StartAsync()
        │
        ├── Schedule timer for configured time
        │
        └── Timer fires (daily)
            │
            ├── Query users with notification preferences:
            │   _db.DailyNotificationPreferences
            │       .Include(p => p.User)
            │       .Where(p => p.IsActive && p.PreferredTime <= now)
            │
            ├── For each user:
            │   ├── Get tomorrow's shifts
            │   ├── Build notification email
            │   └── MailService.SendMailAsync()
            │
            └── Log completion
```

---

### 9. 🕹️ Game Entry Point

**File:** `Pages/Game/Index.cshtml.cs`

**Game Call Chain:**
```
/Game/Index (GET)
    │
    └── OnGetAsync()
        │
        ├── Load game configuration:
        │   _db.Configs.Where(c => c.Key.StartsWith("Game"))
        │
        ├── Load user's game scores:
        │   _db.GameScores.Where(gs => gs.UserId == userId)
        │
        └── Render game UI with leaderboard
```

---

### 10. 📊 Public Pages Entry Points

**Files:**
- `Pages/Public/Chores.cshtml.cs` — Chore calendar
- `Pages/Public/OnDuty.cshtml.cs` — On-duty calendar

**Chores Calendar Call Chain:**
```
/Public/Chores (GET)
    │
    └── OnGetAsync()
        │
        ├── Authorization: CanViewChores policy (all authenticated)
        │
        ├── Parse month parameter
        │
        ├── Query chores:
        │   _db.Chores
        │       .Include(c => c.User)
        │       .Where(c => c.Date >= start && c.Date <= end && c.CanceledAt == null)
        │   └── (Query filter: CompanyId = current)
        │
        ├── Check CanEdit permission (CanEditChores policy)
        │
        └── Render calendar (view-only or editable based on role)
```

---

## Secondary Entry Points

### Health Check Endpoints
**Evidence:** `Program.cs:448-450`

```
GET /health → Liveness probe (is app running?)
GET /ready  → Readiness probe (is database connected?)
```

### Team Calendars API
**File:** `Controllers/TeamCalendarsController.cs`
**Size:** 12,081 bytes
**Auth:** Cookie-based (not API key)

```
GET    /api/team-calendars           → List user's calendars
GET    /api/team-calendars/{id}      → Get calendar details
POST   /api/team-calendars           → Create calendar
PUT    /api/team-calendars/{id}      → Rename calendar
DELETE /api/team-calendars/{id}      → Delete calendar
GET    /api/team-calendars/{id}/week → Get week view with statuses
GET    /api/team-calendars/{id}/members → Get member list
PUT    /api/team-calendars/{id}/members → Update members
```

---

## Entry Point Summary Table

| Entry Point | Path | Auth Required | Role Required | Evidence |
|-------------|------|---------------|---------------|----------|
| Login | `/Auth/Login` | ❌ No | None | `Pages/Auth/Login.cshtml.cs` |
| Signup | `/Auth/Signup` | ❌ No | None | `Pages/Auth/Signup.cshtml.cs` |
| Home | `/` | ✅ Yes | Any | `Pages/Index.cshtml.cs` |
| Calendar | `/Calendar/Month` | ✅ Yes | Any | `Pages/Calendar/Month.cshtml.cs` |
| My Dashboard | `/My/Index` | ✅ Yes | Any | `Pages/My/Index.cshtml.cs` |
| Admin Users | `/Admin/Users` | ✅ Yes | Manager+ | `Pages/Admin/Users.cshtml.cs` |
| Owner Panel | `/Owner/Index` | ✅ Yes | Owner | `Pages/Owner/Index.cshtml.cs` |
| API Users | `/api/v1/users` | ✅ API Key | Scope-based | `Controllers/Api/V1/UsersController.cs` |
| Chores | `/Public/Chores` | ✅ Yes | Any | `Pages/Public/Chores.cshtml.cs` |
| On-Duty | `/Public/OnDuty` | ✅ Yes | Any | `Pages/Public/OnDuty.cshtml.cs` |
| Health | `/health` | ❌ No | None | `Program.cs:448` |

---

## Diagnostic Entry Point

**File:** `Pages/Diagnostic.cshtml.cs`
**Authorization:** IsAdmin (Owner only)
**Purpose:** Debug multi-tenant data, inspect database state

```
/Diagnostic (GET)
    │
    └── OnGetAsync()
        │
        ├── Load all companies (IgnoreQueryFilters)
        ├── Load all users (IgnoreQueryFilters)
        ├── Show tenant resolution status
        └── Render debug information
```

**⚠️ Security Note:** This page bypasses query filters to show cross-tenant data. Restricted to Owner role.

---

## Middleware Execution Order

**Evidence:** `Program.cs:392-446`

```
1. UseStaticFiles()           → Serve wwwroot/* files
2. UseRouting()               → Match request to endpoint
3. UseRequestLogging()        → Log all requests
4. Security Headers           → X-Frame-Options, CSP, etc.
5. UseRequestLocalization()   → Set culture from cookie/header
6. GriffinAuthMiddleware      → ADFS token verification
7. CompanyContextMiddleware   → Resolve tenant from user
8. UseAuthentication()        → Cookie authentication
9. UseAuthorization()         → Policy-based authorization
10. ApiRequestLogging         → Log API requests (for /api only)
11. ApiAuthentication         → API key verification (for /api only)
12. ApiRateLimiting           → Rate limit check (for /api only)
13. MapControllers()          → Execute API controllers
14. MapRazorPages()           → Execute Razor pages
15. MapHealthChecks()         → Health check endpoints
```
