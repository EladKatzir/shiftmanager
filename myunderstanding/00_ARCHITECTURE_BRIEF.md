# ShiftManager Architecture & Concepts Brief

**Generated:** 2026-01-12
**Agent:** Antigravity Workspace Intake
**Status:** Fluency Assessment Complete

---

## Executive Summary

ShiftManager is an **enterprise-grade multi-tenant shift scheduling system** built with ASP.NET Core 8.0 Razor Pages. It is designed for **air-gapped Windows environments** with complete offline operation capability.

### Evidence Sources
- `Program.cs` — Main entry point and DI configuration (456 lines)
- `Data/AppDbContext.cs` — EF Core DbContext with 34 DbSets (714 lines)
- `docs/README.md` — Project overview (616 lines)
- `docs/context.md` — Comprehensive context documentation (1782+ lines)
- `ShiftManager.csproj` — .NET 8.0 project file

---

## Technology Stack (Evidenced)

| Component | Technology | Version | Evidence |
|-----------|------------|---------|----------|
| **Runtime** | .NET | 8.0 | `ShiftManager.csproj:3` — `<TargetFramework>net8.0</TargetFramework>` |
| **Framework** | ASP.NET Core Razor Pages | 8.0 | `Program.cs:36` — `builder.Services.AddRazorPages()` |
| **Database** | SQLite | 3.x | `ShiftManager.csproj:34` — `Microsoft.EntityFrameworkCore.Sqlite 9.0.9` |
| **ORM** | Entity Framework Core | 9.0.9 | `ShiftManager.csproj:30,34` |
| **Image Processing** | SixLabors.ImageSharp | 3.1.11 | `ShiftManager.csproj:36` |
| **Test Framework** | xUnit | 2.5.3 | `ShiftManager.Tests/ShiftManager.Tests.csproj` |
| **Authentication** | Cookie-based | ASP.NET Core | `Program.cs:64-90` |

---

## Core Architectural Patterns

### 1. Multi-Tenant Architecture (Row-Level Isolation)
**Evidence:** `Data/AppDbContext.cs:436-511` — Query filters for tenant scoping

**Implementation:**
- Every tenant-scoped entity implements `IBelongsToCompany` interface
- `ITenantResolver` resolves `CompanyId` from authenticated user claims
- `CompanyIdInterceptor` auto-injects `CompanyId` on `SaveChanges()`
- EF Core global query filters automatically add `WHERE CompanyId = @current`

**Key Files:**
- `Data/AppDbContext.cs:436-511` — 20+ entity query filters
- `Data/CompanyIdInterceptor.cs` — 4865 bytes, auto-injects CompanyId
- `Services/TenantResolver.cs` — Resolves tenant from claims
- `Services/CompanyContext.cs` — Scoped service holding current CompanyId
- `Middleware/CompanyContextMiddleware.cs` — Middleware setup

### 2. Razor Pages MVC Pattern
**Evidence:** `Pages/` directory — 153 files across 16 subdirectories

**UI Layers:**
- **Public Pages:** `/Pages/Public/` — Chores, OnDuty (all authenticated users)
- **Admin Pages:** `/Pages/Admin/` — 18 files (Manager+ roles)
- **Owner Pages:** `/Pages/Owner/` — 34 files (Owner role only)
- **User Pages:** `/Pages/My/` — 13 files (personal dashboard)
- **Auth Pages:** `/Pages/Auth/` — 10 files (login, signup, logout)
- **Calendar Pages:** `/Pages/Calendar/` — 8 files (Month, Week, Day views)

### 3. Service Layer Pattern
**Evidence:** `Services/` directory — 74 files, 81 services registered

**Key Service Categories:**
| Category | Example Services | Evidence |
|----------|------------------|----------|
| **Core Domain** | `IChoreService`, `IOnDutyService` | `Program.cs:142-143` |
| **Multi-Tenancy** | `ITenantResolver`, `ICompanyContext` | `Program.cs:49-50` |
| **Notifications** | `INotificationService`, `IMailService` | `Program.cs:117,131` |
| **Security** | `IEncryptionService`, `ISecurityLogger` | `Program.cs:114,148` |
| **Analytics** | `IAnalyticsService`, `IAuditLogService` | `Program.cs:138-139` |
| **API Services** | `UserApiService`, `ShiftApiService` | `Program.cs:171-178` |
| **Caching** | `IShiftTypeCacheService`, `ICompanyCacheService` | `Program.cs:126-128` |

### 4. API Controller Pattern
**Evidence:** `Controllers/Api/V1/` — 10 controllers with REST endpoints

**Controllers:**
- `UsersController.cs` — User CRUD (17KB)
- `ShiftsController.cs` — Shift management (9KB)
- `TimeOffController.cs` — Time-off requests (23KB)
- `ChoresController.cs` — Chore assignments (21KB)
- `OnDutyController.cs` — On-duty scheduling (20KB)
- `SwapRequestsController.cs` — Shift swaps (26KB)
- `NotificationsController.cs` — User notifications (15KB)
- `FeedbackController.cs` — User feedback (19KB)
- `AnalyticsController.cs` — Workforce analytics (7KB)
- `AuditLogsController.cs` — Audit trail (7KB)

---

## Domain Models (Evidenced)

### Core Entities (34 DbSets)
**Evidence:** `Data/AppDbContext.cs:20-66`

| Entity | Purpose | CompanyId Scoped | Evidence |
|--------|---------|------------------|----------|
| `Company` | Multi-tenant root | N/A (is root) | `AppDbContext.cs:20` |
| `AppUser` | Employee accounts | ✅ Yes | `AppDbContext.cs:21`, Query filter :466 |
| `ShiftType` | Shift templates (MORNING, NOON, NIGHT) | ✅ Yes | `AppDbContext.cs:22`, Query filter :438 |
| `ShiftInstance` | Specific shift occurrences | ✅ Yes | `AppDbContext.cs:23`, Query filter :441 |
| `ShiftAssignment` | User-to-shift assignments | ✅ Yes | `AppDbContext.cs:24`, Query filter :444 |
| `TimeOffRequest` | PTO requests | ✅ Yes | `AppDbContext.cs:25`, Query filter :447 |
| `SwapRequest` | Shift swap requests | ✅ Yes | `AppDbContext.cs:26`, Query filter :450 |
| `UserNotification` | In-app notifications | ✅ Yes | `AppDbContext.cs:27`, Query filter :453 |
| `Chore` | Non-shift task assignments | ✅ Yes | `AppDbContext.cs:36`, Query filter :462 |
| `OnDuty` | On-duty schedule (🛡️ Hakam, ⭐ Lead) | ❌ Global | `AppDbContext.cs:57` (no filter) |
| `TeamCalendar` | Personal team calendars | ✅ Yes | `AppDbContext.cs:37`, Query filter :478 |
| `ApiKey` | External API authentication | ❌ Sidecar | `AppDbContext.cs:61` (no filter) |
| `GameScore` | Shift swap game leaderboard | ✅ Yes | `AppDbContext.cs:66`, Query filter :488 |

### User Roles (6 roles)
**Evidence:** `Models/Support/Enums.cs` referenced in `docs/context.md:156-159`

| Role | Value | Permissions | Evidence |
|------|-------|-------------|----------|
| **Owner** | 0 | Full system control | `Program.cs:96` — `policy.RequireRole(nameof(UserRole.Owner))` |
| **Manager** | 1 | Company management | `Program.cs:94-95` — `IsManagerOrAdmin` policy |
| **Employee** | 2 | Self-service only | Default authenticated role |
| **Director** | 3 | Multi-company access | `Program.cs:97-98` — `IsDirector`, `IsOwnerOrDirector` policies |
| **Trainee** | 4 | Shadow shifts | Referenced in `docs/context.md:156` |
| **Assigner** | 5 | Chore assignment | `Program.cs:107` — `CanEditChores` policy |

---

## Data Flow Patterns

### Request → Response Flow
```
HTTP Request
    ↓
Middleware Pipeline (`Program.cs:392-446`)
    ├── RequestLoggingMiddleware
    ├── GriffinAuthenticationMiddleware
    ├── CompanyContextMiddleware
    ├── UseAuthentication()
    ├── UseAuthorization()
    └── API Middleware (for /api routes only)
    ↓
Razor Page / API Controller
    ↓
Service Layer (business logic)
    ↓
AppDbContext + Query Filters
    ↓
SQLite Database (app.db)
```

### Multi-Tenant Query Flow
**Evidence:** `Data/AppDbContext.cs:436-511`

```csharp
// Example: Any query on ShiftInstances
_db.ShiftInstances.ToListAsync()

// EF Core automatically adds:
// WHERE CompanyId = <current_tenant_id>

// To bypass (for Directors):
_db.ShiftInstances.IgnoreQueryFilters().Where(...)
```

---

## Authentication & Authorization

### Cookie Authentication
**Evidence:** `Program.cs:64-90`

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt => {
        opt.LoginPath = "/Auth/Login";
        opt.LogoutPath = "/Auth/Logout";
        opt.Cookie.Name = "shiftmgr.auth";
        opt.ExpireTimeSpan = TimeSpan.FromDays(7);
    });
```

### Griffin ADFS Integration (Optional)
**Evidence:** `Program.cs:119-123`, `appsettings.json:87-94`

- Middleware: `GriffinAuthenticationMiddleware` (`Program.cs:432`)
- Configuration: `Griffin:Enabled`, `Griffin:BaseUrl`, `Griffin:AutoProvisionUsers`
- Fallback: Local email/password authentication always available

### Authorization Policies
**Evidence:** `Program.cs:92-110`

| Policy | Roles Required | Evidence |
|--------|----------------|----------|
| `IsManagerOrAdmin` | Manager, Owner, Director | `Program.cs:94-95` |
| `IsAdmin` | Owner | `Program.cs:96` |
| `IsDirector` | Owner, Director | `Program.cs:97` |
| `IsOwnerOrDirector` | Owner, Director | `Program.cs:98` |
| `CanViewChores` | Any authenticated | `Program.cs:102` |
| `CanViewOnDuty` | Any authenticated | `Program.cs:103` |
| `CanEditChores` | Manager, Owner, Director, Assigner | `Program.cs:106-107` |
| `CanEditOnDuty` | Manager, Owner, Director | `Program.cs:108-109` |

---

## External Integrations

### Email Service (Optional)
**Evidence:** `appsettings.json:81-86`, `Program.cs:112,117`

```json
"Email": {
    "Enabled": false,  // Disabled by default
    "ApiKey": "",
    "ApiUrl": "https://api.yourcompany.com/v1/mail/send",
    "FromAddress": "noreply@example.com"
}
```

### API Layer (27+ endpoints)
**Evidence:** `Controllers/Api/V1/` — 10 controllers

- API Key Authentication: `Middleware/ApiAuthenticationMiddleware.cs` (10KB)
- Rate Limiting: `Middleware/ApiRateLimitingMiddleware.cs` (6KB)
- Request Logging: `Middleware/ApiRequestLoggingMiddleware.cs` (5KB)

---

## Localization

### Supported Cultures
**Evidence:** `Program.cs:25-34`

- English (en-US) — Default
- Hebrew (he-IL) — With RTL support

### Resource Files
**Evidence:** `Resources/` directory

- `SharedResources.resx` — English strings
- `SharedResources.he-IL.resx` — Hebrew translations

---

## Database Schema Summary

### Tables Count: 34 DbSets
**Evidence:** `Data/AppDbContext.cs:20-66`

### Key Indexes (Performance Critical)
**Evidence:** `Data/AppDbContext.cs:104-139`

| Index | Purpose | Evidence |
|-------|---------|----------|
| `(CompanyId, WorkDate)` on ShiftInstance | Calendar queries | `AppDbContext.cs:106-107` |
| `(CompanyId, Key)` on ShiftType | Shift type lookups | `AppDbContext.cs:148-150` |
| `(CompanyId, UserId, Date)` on Chore | Chore calendar | `AppDbContext.cs:300-301` |
| `KeyHash` on ApiKey (unique) | API authentication | `AppDbContext.cs:570-571` |

---

## Background Services

### Daily Notification Job
**Evidence:** `Program.cs:164`

```csharp
builder.Services.AddHostedService<DailyNotificationJob>();
```

**Implementation:** `Services/DailyNotificationJob.cs` (6KB)
- Sends daily shift notification emails
- Runs on configurable schedule

---

## Health Checks

### Endpoints
**Evidence:** `Program.cs:188-190, 448-450`

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

app.MapHealthChecks("/health");   // Liveness probe
app.MapHealthChecks("/ready");    // Readiness probe
```

---

## Security Headers

**Evidence:** `Program.cs:398-425`

- `X-Frame-Options: DENY` — Prevent clickjacking
- `X-Content-Type-Options: nosniff` — Prevent MIME sniffing
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Content-Security-Policy` — Restricts resource loading

---

## Key Sharp Edges ⚠️

1. **SQLite Limitations**: No true concurrent writes, single-file database
2. **Air-Gapped Deployment**: DLL unblocking required after USB transfer
3. **Griffin ADFS**: External dependency, optional but complex configuration
4. **Multi-Tenant Bypasses**: `IgnoreQueryFilters()` must be used carefully
5. **Background Job**: Single instance only, no distributed lock

---

## Version & Status

- **Version:** v1.0.0 (Release Candidate)
- **Status:** Production Ready with known issues for v1.0.1 hotfix
- **Test Coverage:** 15 unit tests (DirectorService only)
- **Migrations:** 94 migration files in `Migrations/` directory
