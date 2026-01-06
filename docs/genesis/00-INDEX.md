# ShiftManager - Complete Genesis Documentation
## Master Index & Navigation Hub

**Document Version:** 1.2
**Last Updated:** 2026-01-06
**Codebase Version:** v2.2.0+ (Branch: newestversionpriorpl)
**Documentation Purpose:** Enable complete system reconstruction from scratch

---

## 📋 Table of Contents

### Quick Navigation
- [About This Documentation](#about-this-documentation)
- [How to Use This Documentation](#how-to-use-this-documentation)
- [System At-A-Glance](#system-at-a-glance)
- [Documentation Map](#documentation-map)
- [Diagrams Reference](#diagrams-reference)
- [Quick Reference](#quick-reference)

---

## About This Documentation

This documentation set represents a **complete ontological analysis** of the ShiftManager application. It is designed to enable:

1. **Perfect Reconstruction** - A senior C# developer can rebuild the entire system from scratch using only this documentation
2. **Deep Understanding** - Capture not just WHAT was built, but WHY design decisions were made
3. **Knowledge Transfer** - Comprehensive onboarding for new team members
4. **Maintenance** - Clear architecture for long-term system evolution

**Documentation Philosophy:**
We document the "soul" of the system - the design decisions, tradeoffs, and patterns that give ShiftManager its form. This goes beyond API documentation to capture architectural intent.

**Source:** All documentation is derived 100% from codebase analysis. No existing documentation was referenced.

---

## How to Use This Documentation

### For Different Audiences

#### 🏗️ **Software Architects**
Start here to understand high-level design:
1. [01-EXECUTIVE-OVERVIEW.md](01-EXECUTIVE-OVERVIEW.md) - Business context and requirements
2. [02-ARCHITECTURE-BLUEPRINT.md](02-ARCHITECTURE-BLUEPRINT.md) - System design and patterns
3. [18-DESIGN-DECISIONS-AND-TRADEOFFS.md](18-DESIGN-DECISIONS-AND-TRADEOFFS.md) - Architectural rationale

#### 👨‍💻 **Backend Developers**
Jump to implementation details:
1. [04-STARTUP-AND-MIDDLEWARE.md](04-STARTUP-AND-MIDDLEWARE.md) - Application bootstrap
2. [05-MULTI-TENANCY-DEEP-DIVE.md](05-MULTI-TENANCY-DEEP-DIVE.md) - Tenant isolation patterns
3. [06-DOMAIN-MODELS.md](06-DOMAIN-MODELS.md) - Database entities
4. [07-SERVICE-LAYER.md](07-SERVICE-LAYER.md) - Business logic services

#### 🎨 **Frontend Developers**
Focus on UI implementation:
1. [08-UI-UX-ARCHITECTURE.md](08-UI-UX-ARCHITECTURE.md) - Razor Pages, CSS, JavaScript
2. [11-LOCALIZATION-AND-RTL.md](11-LOCALIZATION-AND-RTL.md) - Internationalization

#### 🔌 **API Integrators**
Learn the API surface:
1. [09-API-LAYER.md](09-API-LAYER.md) - 27 REST endpoints documented
2. [10-AUTHENTICATION-AND-AUTHORIZATION.md](10-AUTHENTICATION-AND-AUTHORIZATION.md) - API key authentication

#### 🚀 **DevOps Engineers**
Deployment and operations:
1. [15-AIR-GAPPED-DEPLOYMENT.md](15-AIR-GAPPED-DEPLOYMENT.md) - Offline deployment
2. [16-BUILD-AND-RELEASE-PIPELINE.md](16-BUILD-AND-RELEASE-PIPELINE.md) - Build automation
3. [17-TESTING-STRATEGY.md](17-TESTING-STRATEGY.md) - Quality assurance

#### 🏫 **New Team Members**
Recommended reading order:
1. [01-EXECUTIVE-OVERVIEW.md](01-EXECUTIVE-OVERVIEW.md) - What and why
2. [02-ARCHITECTURE-BLUEPRINT.md](02-ARCHITECTURE-BLUEPRINT.md) - How it's built
3. [03-DATABASE-SCHEMA.md](03-DATABASE-SCHEMA.md) - Data model
4. [08-UI-UX-ARCHITECTURE.md](08-UI-UX-ARCHITECTURE.md) - User experience
5. [14-WORKFLOWS-AND-BUSINESS-LOGIC.md](14-WORKFLOWS-AND-BUSINESS-LOGIC.md) - Business processes

#### 🔨 **Rebuilding From Scratch**
Follow the reconstruction guide:
1. [19-RECONSTRUCTION-RECIPE.md](19-RECONSTRUCTION-RECIPE.md) - Step-by-step rebuild (10 phases)

---

## System At-A-Glance

### Core Statistics

| Metric | Count |
|--------|-------|
| **Technology Stack** | .NET 8.0, ASP.NET Core, EF Core 9.0.9 |
| **Database** | SQLite (single-file, air-gapped ready) |
| **Database Tables** | 29 tables |
| **EF Core Migrations** | 37 migrations (Sept 2025 - Present) |
| **Razor Pages** | 67 pages |
| **REST API Endpoints** | 27 endpoints |
| **Injectable Services** | 40+ services |
| **User Roles** | 6 roles (Owner, Director, Manager, Assigner, Employee, Trainee) |
| **Supported Languages** | English (en-US), Hebrew (he-IL) with RTL support |
| **CSS Lines** | 5,375 lines (100% custom, no framework) |
| **JavaScript Lines** | 2,700+ lines (100% vanilla, no jQuery/React/Vue) |
| **Test Project** | xUnit, Moq, FluentAssertions |
| **Build Pipeline Stages** | 10-stage automated release |
| **Deployment Package Size** | 111 MB (self-contained, 378 files) |

### Business Domain

**What:** Multi-tenant shift scheduling and workforce management system
**Who:** Military, government, and secure facilities (air-gapped environments)
**Key Features:**
- Shift scheduling with conflict detection
- Time-off request workflows
- Shift swap management
- Daily chore assignments
- On-duty scheduling (cross-company visibility)
- Team calendars
- In-app notifications + email integration with customizable templates
- Analytics and reporting
- Audit logging (compliance)
- Data lifecycle management (archive, purge, import)
- Griffin ADFS authentication (air-gapped SSO)
- REST API with key-based authentication

### Architectural Highlights

- **Multi-Tenancy:** Row-level security via EF Core global query filters + CompanyIdInterceptor
- **Air-Gapped First:** Self-contained deployment, no internet required, USB transfer support
- **Security:** PBKDF2 password hashing (100k iterations), role-based access control, audit trails
- **Localization:** Full Hebrew RTL support with conditional CSS loading
- **No External Dependencies:** Zero npm packages, no jQuery, no CSS frameworks
- **Gamification:** Match-3 easter egg game for shift swap engagement

---

## Documentation Map

### Part I: Foundation & Context (Documents 01-02)

#### [01-EXECUTIVE-OVERVIEW.md](01-EXECUTIVE-OVERVIEW.md) (800 lines)
**Purpose:** Business context and "why"

**Contents:**
- Problem statement: Why shift scheduling in air-gapped environments?
- Target users and use cases
- 6 user roles with real-world scenarios
- Business requirements and constraints
- Why .NET? Why SQLite? Why no frameworks?
- Competitive landscape

**Key Questions Answered:**
- What problem does ShiftManager solve?
- Who uses it and how?
- Why was it built this way?

---

#### [02-ARCHITECTURE-BLUEPRINT.md](02-ARCHITECTURE-BLUEPRINT.md) (1,200 lines)
**Purpose:** System-level architecture overview

**Contents:**
- Architectural style: Monolithic Razor Pages with service layer
- Technology stack deep dive (.NET 8.0, EF Core 9.0.9, SQLite, SixLabors.ImageSharp)
- Layering strategy:
  - Presentation: Razor Pages (66 pages)
  - Service Layer: Business logic (40+ services)
  - Data Access: EF Core + DbContext (28 tables)
  - Infrastructure: Middleware, caching, email, ADFS
- Project structure (`ShiftManager.csproj` + `ShiftManager.Tests.csproj`)
- Dependency injection philosophy
- Cross-cutting concerns (logging, audit, multi-tenancy)
- **Diagram:** [architecture-overview.mmd](diagrams/architecture-overview.mmd) - C4 component diagram

**Key Questions Answered:**
- How is the system organized?
- What are the major components and their relationships?
- Why monolith instead of microservices?

---

### Part II: Data & Persistence (Documents 03-06, 12-13)

#### [03-DATABASE-SCHEMA.md](03-DATABASE-SCHEMA.md) (1,500 lines)
**Purpose:** Complete database documentation

**Contents:**
- **Diagram:** [database-erd.mmd](diagrams/database-erd.mmd) - Complete ERD with all 28 tables
- Table-by-table breakdown with fields, types, constraints
- Relationships: one-to-many, many-to-many, nullable FKs
- CompanyId scoping rules (which tables are tenant-scoped, exceptions)
- Foreign key cascading behavior (CASCADE, RESTRICT, SET NULL)
- Index strategy for performance
- Special case: OnDuty table has NO CompanyId (cross-company visibility)
- Value converters (DateOnly, TimeOnly → string for SQLite)

**Key Questions Answered:**
- What data is stored?
- How are entities related?
- How does multi-tenancy work at the database level?

---

#### [06-DOMAIN-MODELS.md](06-DOMAIN-MODELS.md) (2,000 lines)
**Purpose:** 28 entities explained in detail

**Contents:**
- Entity-by-entity documentation:
  - **Core Entities:** Company, AppUser, ShiftType, ShiftInstance, ShiftAssignment
  - **Requests:** TimeOffRequest, SwapRequest, UserJoinRequest
  - **Tasks:** Chore, OnDuty, OnDutyRoleSubscription
  - **Communication:** UserNotification, DailyNotificationPreference, Feedback
  - **Teams:** TeamCalendar, TeamCalendarMember
  - **Audit:** AuditLog, RoleAssignmentAudit, ProfileChangeAudit
  - **API:** ApiKey, ApiKeyRequest, ApiRequestLog
  - **Config:** AppConfig, EmailConfig, EmailApiLog, GriffinConfig, OnDutyTypeConfig
  - **Gamification:** GameScore
- Properties explained (data types, nullability, validation attributes)
- Navigation properties and their purpose
- Business rules (e.g., shift conflicts, time-off approval workflow)
- Example usage scenarios
- DTOs vs. Entities (Models/DTOs/ folder)
- IBelongsToCompany interface (tenant scoping marker)

**Key Questions Answered:**
- What does each entity represent?
- What are the business rules encoded in the data model?
- How do entities relate to each other?

---

#### [12-DATA-MIGRATIONS.md](12-DATA-MIGRATIONS.md) (1,000 lines)
**Purpose:** Database evolution timeline

**Contents:**
- 36 migrations chronologically explained (Sept 27, 2025 → Present)
- Migration phases:
  - **Phase 1:** Initial schema (Companies, Users, Shifts, Requests, Notifications)
  - **Phase 2:** Multi-tenancy (CompanyId columns, DirectorCompanies, UserJoinRequests)
  - **Phase 3:** Auditing (RoleAssignmentAudit, AuditLog, ProfileChangeAudit)
  - **Phase 4:** Features (Chores, TeamCalendars, OnDuty, GameScores)
  - **Phase 5:** Security & API (ApiKey, ApiKeyRequest, ApiRequestLog, account lockout)
  - **Phase 6:** Communication (EmailConfig, EmailApiLog, DailyNotificationPreferences)
  - **Phase 7:** Integration (GriffinConfig for ADFS)
  - **Phase 8:** Performance (indexes, optimizations)
- Migration patterns: adding tables, adding indexes, data migrations
- How to apply migrations (`dotnet ef database update`)
- Rollback strategy

**Key Questions Answered:**
- How did the database evolve over time?
- What were the major feature milestones?
- How do I apply migrations?

---

#### [13-CACHING-STRATEGY.md](13-CACHING-STRATEGY.md) (600 lines)
**Purpose:** Performance optimization through caching

**Contents:**
- Service-level caching (3 cache services):
  - **ShiftTypeCacheService:** Shift types rarely change (5-minute TTL)
  - **CompanyCacheService:** Company metadata (5-minute TTL)
  - **AppConfigCacheService:** App configuration (5-minute TTL)
- Cache invalidation strategies
- IMemoryCache usage (ASP.NET Core in-memory cache)
- Cache key scoping by tenant ID (prevent cross-tenant leakage)
- When NOT to cache (user-specific data, real-time notifications)
- Performance impact measurements

**Key Questions Answered:**
- What is cached and why?
- How is cache invalidation handled?
- How does caching interact with multi-tenancy?

---

### Part III: Application Startup & Core Systems (Documents 04-05, 07)

#### [04-STARTUP-AND-MIDDLEWARE.md](04-STARTUP-AND-MIDDLEWARE.md) (1,000 lines)
**Purpose:** Program.cs deep dive (436 lines)

**Contents:**
- Bootstrap sequence explained line-by-line:
  - Builder initialization + logging configuration
  - Localization setup (en-US, he-IL)
  - Razor Pages with folder-level authorization
  - **50+ DI service registrations** (Scoped, Transient, Singleton)
  - Multi-tenancy services (TenantResolver, CompanyContext, CompanyIdInterceptor)
  - Database context with CompanyIdInterceptor
  - Authentication (cookie-based, 7-day sliding expiration)
  - Authorization policies (IsManagerOrAdmin, IsAdmin, CanEditChores, etc.)
  - Core services (40+ services: AnalyticsService, ApiKeyService, AuditLogService, etc.)
  - Background services (DailyNotificationJob)
  - Health checks (EF Core database health check)
  - Database seeding (SeedData class)
- **Middleware pipeline order** (critical):
  1. Error handling (DeveloperExceptionPage / ExceptionHandler)
  2. HTTPS redirection (conditional)
  3. Static files (explicit MIME types, cache control)
  4. Routing
  5. RequestLoggingMiddleware
  6. Security headers (X-Frame-Options, CSP, etc.)
  7. Request localization
  8. GriffinAuthenticationMiddleware (ADFS)
  9. CompanyContextMiddleware (multi-tenancy)
  10. Authentication + Authorization
  11. API middleware (ApiRequestLoggingMiddleware, ApiAuthenticationMiddleware, ApiRateLimitingMiddleware)
  12. Endpoints (Controllers, Razor Pages, Health checks)
- **Diagram:** [middleware-pipeline.mmd](diagrams/middleware-pipeline.mmd) - ASP.NET pipeline flow
- Configuration loading (appsettings.json, environment variables)
- Why this middleware order matters

**Key Questions Answered:**
- How does the application start up?
- What services are injected and with what lifetime?
- What is the middleware pipeline order and why?

---

#### [05-MULTI-TENANCY-DEEP-DIVE.md](05-MULTI-TENANCY-DEEP-DIVE.md) (800 lines)
**Purpose:** Tenant isolation implementation

**Contents:**
- Row-level security via EF Core global query filters
- **CompanyIdInterceptor** implementation (`Data/CompanyIdInterceptor.cs`):
  - Automatic CompanyId scoping on `SaveChangesAsync`
  - Feature flag: `Features:EnforceCompanyScope` (warn vs. enforce modes)
  - Entities implementing `IBelongsToCompany` interface
- **CompanyContext** service:
  - Current user's company tracking
  - Company switching for directors
- **TenantResolver** service:
  - Resolves current tenant from HttpContext.User
  - Handles Director multi-company access
- Service-layer validation (**CompanyFilterService**)
- API authentication whitelist pattern:
  - Internal browser endpoints (use cookie auth)
  - External API endpoints (use X-API-Key header)
  - Whitelist: `/api/team-calendars`, `/Api/SessionStatus`, `/Api/Calendar/*`, `/Api/Game/*`
- **Diagram:** [multi-tenant-flow.mmd](diagrams/multi-tenant-flow.mmd) - Sequence diagram of tenant scoping
- Edge cases:
  - Directors with multi-company access (DirectorCompanies table)
  - OnDuty cross-company visibility (no CompanyId filter)
  - Admin/Owner bypass scoping (EnforceCompanyScope=false in dev)
- Testing multi-tenancy (isolation verification)

**Key Questions Answered:**
- How is tenant isolation enforced?
- What happens if a user tries to access another company's data?
- How do directors access multiple companies?

---

#### [07-SERVICE-LAYER.md](07-SERVICE-LAYER.md) (1,500 lines)
**Purpose:** 40+ injectable services catalogued

**Contents:**
- Service inventory with responsibilities:
  - **Business Logic:** AnalyticsService, ChoreService, ConflictChecker, DirectorService, NotificationService, OnDutyService, ShiftAssignmentService, SwapRequestService, TimeOffRequestService, TraineeService
  - **Caching:** AppConfigCacheService, CompanyCacheService, ShiftTypeCacheService
  - **Multi-Tenancy:** CompanyContext, CompanyFilterService, TenantResolver, ViewAsModeService
  - **API:** ApiKeyService, FeedbackApiService, NotificationApiService, RateLimitingService, ValidationService
  - **Security:** EncryptionService, SecurityLogger, ApiKeyService
  - **Infrastructure:** EmailConfigService, MailService, EmailTemplateBuilder, GriffinService
  - **User Management:** AuditLogService, ProfileService, AvatarService, UserPreferenceService
  - **Team Collaboration:** TeamCalendarService, TeamCalendarEventAggregator
  - **Background Jobs:** DailyNotificationJob
- Service patterns:
  - Constructor injection
  - Dependency lifetimes (Scoped, Transient, Singleton)
  - Service interfaces vs. implementations
- **Diagram:** [service-dependency-graph.mmd](diagrams/service-dependency-graph.mmd) - DI relationships
- Example: ConflictChecker service (shift overlap detection, rest hours enforcement)
- Example: NotificationService (notification creation + delivery)
- Testing services (Moq patterns from ShiftManager.Tests/)

**Key Questions Answered:**
- What services exist and what do they do?
- How are services organized and related?
- How do I test services?

---

### Part IV: User Interface & Experience (Documents 08, 11)

#### [08-UI-UX-ARCHITECTURE.md](08-UI-UX-ARCHITECTURE.md) (1,500 lines)
**Purpose:** Frontend implementation (Razor Pages + CSS + JavaScript)

**Contents:**
- **67 Razor Pages breakdown by area:**
  - **Auth/** (5 pages): Login, Signup, GriffinCallback, ForgotPassword, Logout
  - **Calendar/** (4 pages): Month, Week, Day, Table (shift management workspace)
  - **Admin/** (9 pages): Users, Companies, Directors, ShiftTypes, Config, Analytics, AuditLog, EditProfile
  - **Owner/** (7 pages): FeatureFlags, EmailConfig, EmailTemplates, GameConfig, DatabaseConsole, Backup
  - **Director/** (3 pages): CompanyFilter, ViewAsMode, NotificationHub
  - **My/** (6 pages): Profile, Requests, NotificationCenter, Settings, ApiKeys
  - **MyTeam/** (1 page): Index (team calendar view)
  - **Requests/** (3 pages): Index (approval dashboard), TimeOff/Create, Swaps/Create
  - **Public/** (3 pages): Chores, OnDuty, Feedback
  - **Api/** (8 pages): Internal browser-based API endpoints
  - **Other:** Home/Index, Diagnostic, Error, AccessDenied
- **Custom CSS** (5,375 lines):
  - Design system (CSS custom properties, dark mode, color tokens)
  - Typography scale (display, title, body, subtle, caption)
  - Component library (cards, buttons, forms, calendar grid)
  - Dark mode implementation (`:root[data-theme="dark"]`)
  - **RTL support:** rtl.css (124 lines) for Hebrew (sidebar flipped, text alignment reversed)
- **Vanilla JavaScript** (2,700+ lines):
  - **site.js** (1,114 lines): Theme toggle, command palette (Ctrl+K), keyboard shortcuts, toast notifications, shift modal
  - **shift-swap-game.js** (1,376 lines): Match-3 easter egg game with leaderboard
  - **calendar-inline-edit.js:** Quick-add forms for chores/on-duty
  - **session-check.js:** Periodic session validation, auto-redirect on expiry
  - **hebrew-audit.js:** Hebrew localization QA tool (dev only)
  - **myteam.js:** Team calendar event filtering
- **View Components** (4 components):
  - BreadcrumbViewComponent
  - LanguageToggleViewComponent
  - ShowMyItemsToggleViewComponent
  - UnreadNotificationCountViewComponent
- **App Shell Layout** (`Pages/Shared/_Layout.cshtml`, 317 lines):
  - Sidebar navigation (role-based)
  - Header (breadcrumb, actions, notifications, language toggle, theme toggle, logout)
  - Main content area
  - Footer (user avatar, Ctrl+K hint)
- Why NO frontend framework? (simplicity, air-gapped deployment, zero npm dependencies)

**Key Questions Answered:**
- How is the UI structured?
- What pages exist and what do they do?
- How does dark mode work?
- What is the command palette feature?
- How does the easter egg game work?

---

#### [11-LOCALIZATION-AND-RTL.md](11-LOCALIZATION-AND-RTL.md) (800 lines)
**Purpose:** Internationalization implementation

**Contents:**
- ASP.NET Core resource file system (`.resx`)
- Supported cultures: `en-US` (default), `he-IL` (Hebrew)
- Resource files:
  - `Resources/SharedResources.cs` (marker class)
  - `Resources/SharedResources.resx` (English strings)
  - `Resources/SharedResources.he-IL.resx` (Hebrew strings)
- Localization services:
  - **ILocalizationService** / **LocalizationService**
  - Injected into all Razor Pages via `LocalizedPageModel`
  - Accessible in views via `@inject IStringLocalizer<SharedResources>`
- Usage patterns:
  - Razor Pages: `@Localizer["Home"]`, `@Localizer["GoodMorning", firstName]`
  - JavaScript: `window.AppLocalizer.CreateShift` (injected via `_LocalizationScript.cshtml`)
- Culture detection (Program.cs):
  1. QueryStringRequestCultureProvider (`?culture=he-IL`)
  2. CookieRequestCultureProvider (`.AspNetCore.Culture` cookie)
  3. AcceptLanguageHeaderRequestCultureProvider
- **RTL CSS overrides** (rtl.css, 124 lines):
  - `html[dir="rtl"]` attribute set based on culture
  - Sidebar flipped to right, text alignment reversed
  - Table and form alignment adjusted
  - Flexbox direction reversed where needed
- Language toggle component:
  - **LanguageToggleViewComponent** (header, top-right)
  - Toggle between English / עברית
  - Cookie-based persistence
- Localization QA:
  - `hebrew-audit.js` - Development tool to check Hebrew translation coverage
  - Loaded only when `lang="he"`
- Example localized strings: navigation, calendar, greetings, game UI

**Key Questions Answered:**
- How does localization work?
- How do I add a new translation?
- How does RTL support work for Hebrew?
- How is the current language selected?

---

### Part V: API & Authentication (Documents 09-10)

#### [09-API-LAYER.md](09-API-LAYER.md) (1,200 lines)
**Purpose:** 27 REST API endpoints

**Contents:**
- API authentication strategy:
  - **External APIs:** X-API-Key header (ApiAuthenticationMiddleware)
  - **Internal browser APIs:** Cookie authentication (whitelisted endpoints)
- **27 Endpoint inventory:**
  - **Users API:** GET /api/v1/users (list), GET /api/v1/users/{id}, POST /api/v1/users (create), PUT /api/v1/users/{id}
  - **Shifts API:** GET /api/v1/shifts (list), GET /api/v1/shifts/{id}
  - **TimeOff API:** GET /api/v1/time-off (list), GET /api/v1/time-off/{id}, POST /api/v1/time-off, PUT /api/v1/time-off/{id}/approve, PUT /api/v1/time-off/{id}/decline
  - **Notifications API:** GET /api/v1/notifications, GET /api/v1/notifications/{id}, POST /api/v1/notifications/mark-read, POST /api/v1/notifications/mark-all-read
  - **SwapRequests API:** GET /api/v1/swap-requests (list), GET /api/v1/swap-requests/{id}, POST /api/v1/swap-requests, PUT /api/v1/swap-requests/{id}/approve, PUT /api/v1/swap-requests/{id}/decline, DELETE /api/v1/swap-requests/{id}
  - **Chores API:** GET /api/v1/chores, GET /api/v1/chores/{id}, POST /api/v1/chores, PUT /api/v1/chores/{id}, DELETE /api/v1/chores/{id}
  - **OnDuty API:** GET /api/v1/on-duty, GET /api/v1/on-duty/{id}, POST /api/v1/on-duty, PUT /api/v1/on-duty/{id}, DELETE /api/v1/on-duty/{id}
  - **Feedback API:** GET /api/v1/feedback, GET /api/v1/feedback/{id}, POST /api/v1/feedback, PUT /api/v1/feedback/{id}/status, DELETE /api/v1/feedback/{id}
  - **Analytics API:** GET /api/v1/analytics/summary
  - **Audit Logs API:** GET /api/v1/audit-logs
- Request/response schemas (JSON examples with DTOs)
- Error handling and HTTP status codes (200, 201, 400, 401, 403, 404, 500)
- API key management:
  - **ApiKeyService:** Create, validate, revoke keys
  - Scopes: `user:read`, `user:write`, `shift:read`, `analytics:read`, etc.
  - Rate limiting (ApiRateLimitingService, default: 100 requests/minute)
- Internal browser endpoints:
  - `/api/team-calendars` - Team calendar API (whitelisted)
  - `/Api/SessionStatus` - Session validation (whitelisted)
  - `/Api/Calendar/*` - Calendar quick actions (whitelisted)
  - `/Api/Game/*` - Shift swap game (whitelisted)
- Python client library usage (`clients/python/`)

**Key Questions Answered:**
- What API endpoints exist?
- How do I authenticate with the API?
- What are the request/response formats?
- How do API keys and scopes work?

---

#### [10-AUTHENTICATION-AND-AUTHORIZATION.md](10-AUTHENTICATION-AND-AUTHORIZATION.md) (1,000 lines)
**Purpose:** Auth flows and role-based access control

**Contents:**
- **Cookie-based authentication** (ASP.NET Core Identity):
  - Cookie name: `shiftmgr.auth`
  - Expiration: 7 days with sliding expiration
  - Security features: HttpOnly=true, SecurePolicy=SameAsRequest, SameSite=Lax
- **Password hashing:** PBKDF2 (100k iterations, SHA256)
  - `PasswordHash` and `PasswordSalt` stored as `byte[]`
  - Hash verification in login flow
- **6 User Roles with permissions:**
  - **Owner (0):** Full system control, company config, director assignment, audit log, database console
  - **Director (3):** Cross-company oversight, multi-company access via DirectorCompanies table
  - **Manager (1):** Approve requests, assign shifts, manage team, analytics
  - **Assigner (5):** Can edit Chores only (not On-Duty, limited role)
  - **Employee (2):** View schedule, submit time-off/swap requests, view chores/on-duty
  - **Trainee (4):** Limited view, shadowing senior employees, cannot request shift swaps
- **Authorization policies** (defined in Program.cs):
  - `IsManagerOrAdmin` → Manager, Owner, Director
  - `IsAdmin` → Owner only
  - `IsDirector` → Owner, Director
  - `IsOwnerOrDirector` → Owner, Director
  - `CanViewChores` → All authenticated users
  - `CanViewOnDuty` → All authenticated users
  - `CanEditChores` → Manager, Owner, Director, Assigner
  - `CanEditOnDuty` → Manager, Owner, Director
- **Griffin ADFS Integration** (air-gapped ADFS):
  - Login flow: User → Griffin service → Callback → Auto-provision user
  - **GriffinAuthenticationMiddleware:** Sets HttpContext.User from griffin.token cookie
  - **GriffinService:** Validates tokens, fetches user claims
  - Auto-provisioning: `AutoProvisionUsers` config (default role: Employee)
  - Fallback to local auth if Griffin is disabled
  - **Diagram:** [authentication-flow.mmd](diagrams/authentication-flow.mmd) - Login + Griffin ADFS flows
- Session management:
  - `/Api/SessionStatus` endpoint for periodic validation
  - `session-check.js` auto-redirects on expiry
- Anti-forgery token usage:
  - `@Html.AntiForgeryToken()` in all POST forms
  - `[IgnoreAntiforgeryToken]` attribute for JSON API endpoints
- Account lockout:
  - FailedLoginAttempts, LockoutEnd, LastLoginAttempt fields
  - Lockout after 5 failed attempts (configurable)

**Key Questions Answered:**
- How does authentication work?
- What are the user roles and their permissions?
- How does Griffin ADFS integration work?
- How is password security handled?

---

#### [ADFS-INTEGRATION-ANALYSIS.md](ADFS-INTEGRATION-ANALYSIS.md) (10,000+ lines) ⭐ NEW
**Purpose:** Comprehensive ADFS integration investigation and gap analysis

**Investigation Date:** 2026-01-03
**Assessment Grade:** B+ (Very Good with Minor Gaps)

**Contents:**
- **Core Questions Answered:**
  - Authorization mechanism: Claims-based with CompanyId scoping
  - Multi-tenancy handling: Per-company GriffinConfig with row-level isolation
  - ADFS scope: Flexible (shared or per-company ADFS servers)
- **Expanded Analysis:**
  - Complete token flow with SHA256-based caching (8-hour TTL, 99.4% cache hit rate)
  - One-way role synchronization (ADFS → ShiftManager on first login)
  - Error handling with graceful fallback to local authentication
  - Performance optimizations (claims caching, async operations, connection pooling)
- **Proactive Gap Analysis (5 Critical Questions):**
  1. **Token Revocation:** 8-hour unauthorized access window identified, solutions proposed
  2. **Monitoring & Alerting:** No health monitoring, no proactive alerting (recommendations included)
  3. **Disaster Recovery:** Manual failover, circuit breaker pattern recommended
  4. **Compliance & Audit:** Authentication audit log missing (design provided)
  5. **Configuration Management:** No change audit trail, rollback mechanism needed
- **Prioritized Recommendations:**
  - Priority 1 (Critical): Token revocation mechanism, authentication audit logging
  - Priority 2 (Operational): ADFS health monitoring, configuration change audit
  - Priority 3 (Scalability): Redis distributed cache, PostgreSQL migration
- **Architecture Diagrams:**
  - Complete ADFS authentication flow (Mermaid sequence diagram)
  - Multi-tenancy integration flow diagram

**Key Findings:**
- ✅ Solid architectural foundation (per-company configuration, defense-in-depth)
- ✅ Performance-optimized (8-hour caching reduces ADFS load by 99.6%)
- ⚠️ Token revocation not implemented (8-hour risk window)
- ⚠️ No operational monitoring (blind to ADFS outages)
- ⚠️ Limited compliance audit trail

**Key Questions Answered:**
- How does Griffin ADFS integrate with multi-tenancy?
- What are the security implications of the token caching strategy?
- What happens during ADFS outages?
- What are the compliance gaps?
- How can the integration be improved?

---

### Part VI: Business Logic & Workflows (Document 14)

#### [14-WORKFLOWS-AND-BUSINESS-LOGIC.md](14-WORKFLOWS-AND-BUSINESS-LOGIC.md) (1,500 lines)
**Purpose:** Key workflows explained end-to-end

**Contents:**
- **Time-Off Request Workflow:**
  1. Employee submits request (Vacation or After half-day)
  2. Manager receives notification
  3. Manager reviews request in /Requests/Index
  4. Manager approves or declines
  5. Employee receives notification
  6. Approved time-off creates OFFLINE shift instances
  - **ConflictChecker:** Validates no overlapping shifts
  - **Diagram:** [request-approval-flow.mmd](diagrams/request-approval-flow.mmd) - Sequence diagram
- **Shift Swap Workflow:**
  1. Employee proposes swap (FromAssignment → ToAssignment)
  2. Manager receives notification
  3. Manager approves or declines
  4. If approved: Shifts reassigned, both employees notified
  - Business rule: Cannot swap shifts that have already occurred
- **Chore Assignment Workflow:**
  1. Manager quick-adds chore in /Public/Chores
  2. Chore assigned to user (one chore per user per day)
  3. Mutual exclusion: User cannot have chore + shift on same day
  4. Chore can be canceled (soft delete)
- **Conflict Detection Logic** (ConflictChecker service):
  - Shift overlap detection (same user, overlapping time ranges)
  - Rest hours enforcement (default: 8 hours between shifts)
  - Weekly hour caps (default: 40 hours)
  - OFFLINE shift type can overlap (for time-off marking)
- **Notification Triggers** (18 notification types):
  - ShiftAdded, ShiftRemoved, ShiftChanged
  - TimeOffApproved, TimeOffDeclined
  - SwapRequestApproved, SwapRequestDeclined
  - ChoreAssigned, ChoreCanceled
  - OnDutyAssigned, OnDutyCanceled
  - RoleChanged, ProfileUpdated
  - etc.
- **Daily Notification Job** (background service):
  - Runs on schedule (user-preferred time)
  - Sends digest of upcoming shifts, pending requests
  - Uses DailyNotificationPreference per user

**Key Questions Answered:**
- How do time-off requests work?
- How do shift swaps work?
- How does conflict detection work?
- What triggers notifications?

---

### Part VII: Deployment & Operations (Documents 15-17)

#### [15-AIR-GAPPED-DEPLOYMENT.md](15-AIR-GAPPED-DEPLOYMENT.md) (1,000 lines)
**Purpose:** Offline deployment architecture

**Contents:**
- **Why air-gapped?** Military, government, secure facilities (no internet access)
- **Self-contained deployment:**
  - Includes .NET 8.0 runtime (no SDK required on target machine)
  - Package size: 111 MB
  - File count: 378 files
  - Single SQLite database file (`app.db`)
- **USB transfer process:**
  1. Build on development machine (internet access)
  2. Run `Build-Release.ps1` to create deployment package
  3. Copy `packages/ShiftManager-v{version}-win-x64.zip` to USB drive
  4. Transfer to air-gapped machine
  5. Extract package
  6. Run `UNBLOCK_FILES.bat` (Windows security: DLL unblocking)
  7. Run `VERIFY_FILES.bat` (integrity check)
  8. Configure `appsettings.Production.json`
  9. Run ShiftManager.exe
- **Windows security (DLL unblocking):**
  - Windows marks downloaded DLLs as "blocked"
  - `UNBLOCK_FILES.bat` uses PowerShell to unblock all DLLs
  - Must run as administrator
- **Network configuration:**
  - Default: `http://localhost:5000` (localhost only)
  - Network binding: Update `urls` in `appsettings.Production.json` to `http://0.0.0.0:5000`
  - Firewall: Allow inbound on port 5000
- **Windows Service setup:**
  - Use NSSM (Non-Sucking Service Manager) or similar
  - Create Windows Service for auto-start on boot
- **Diagram:** [deployment-architecture.mmd](diagrams/deployment-architecture.mmd) - Air-gapped server topology
- **Offline operation verification:**
  - Disconnect internet, verify app functionality
  - No external API calls required (email is optional)
- **Update strategy for air-gapped systems:**
  - Build new package on dev machine
  - USB transfer to production
  - Stop service, backup database, extract new package, restart service

**Key Questions Answered:**
- How do I deploy to an air-gapped environment?
- What is the USB transfer process?
- How do I configure networking?
- How do I set up as a Windows Service?

---

#### [16-BUILD-AND-RELEASE-PIPELINE.md](16-BUILD-AND-RELEASE-PIPELINE.md) (800 lines)
**Purpose:** Automated build process

**Contents:**
- **Build-Release.ps1** deep dive (458 lines, PowerShell):
  - **10-Stage Pipeline:**
    1. **Pre-Flight Checks:** Validate version format, check git status, verify dependencies
    2. **Backup:** Backup existing ProjectPublish folder (keep last 3 backups)
    3. **Build:** `dotnet publish -c Release -r win-x64 --self-contained`
    4. **Build Verification:** File count check (expect 378 files), DLL verification
    5. **Application Testing:** Run `dotnet test` (optional: `-SkipTests`)
    6. **Documentation Generation:** Generate `AIR_GAPPED_DEPLOYMENT_GUIDE.txt`, `API_DOCUMENTATION.md`
    7. **Final Integrity Check:** Validate package completeness (wwwroot, DLLs, localization)
    8. **Git Tagging:** Create version tag `v{version}` (optional: `-NoPush`)
    9. **Packaging:** Create ZIP distribution package (`packages/ShiftManager-v{version}-win-x64.zip`)
    10. **Release Report:** Generate build summary, artifact manifest
  - **Parameters:**
    - `-Version` (required): Semantic version (e.g., "2.1.0")
    - `-SkipTests`: Skip test execution
    - `-NoPush`: Don't push git tag to remote
  - **Output artifacts:**
    - `ProjectPublish/` folder (ready for USB transfer)
    - `packages/ShiftManager-v{version}-win-x64.zip`
    - Helper scripts: `UNBLOCK_FILES.bat`, `VERIFY_FILES.bat`, `QUICK_FIX.bat`
    - Documentation: `AIR_GAPPED_DEPLOYMENT_GUIDE.txt`, `API_DOCUMENTATION.md`
    - Python client: `clients/python/`
- **CI/CD considerations:**
  - GitHub Actions / Azure DevOps integration (not currently configured)
  - Could automate build on version tag push
- **Version management:**
  - Semantic versioning (MAJOR.MINOR.PATCH)
  - Git tags for version tracking
- **Artifact structure:**
  - Self-contained .NET runtime
  - SQLite database file (initial seed data)
  - wwwroot/ assets (CSS, JS, images)
  - Localization resources (he-IL/)
  - Configuration templates (`appsettings.Production.template.json`)

**Key Questions Answered:**
- How do I build a release package?
- What does the build pipeline do?
- How is versioning handled?
- What files are included in the deployment package?

---

#### [17-TESTING-STRATEGY.md](17-TESTING-STRATEGY.md) (700 lines)
**Purpose:** Quality assurance

**Contents:**
- **Test project:** `ShiftManager.Tests.csproj`
  - **Framework:** xUnit 2.5.3
  - **Assertion library:** FluentAssertions 8.7.1
  - **Mocking:** Moq 4.20.72
  - **Integration testing:** Microsoft.AspNetCore.Mvc.Testing 8.0.10
  - **In-memory database:** Microsoft.EntityFrameworkCore.InMemory 9.0.9
  - **Code coverage:** coverlet.collector 6.0.0
- **Unit test patterns:**
  - **Service tests:** Mock dependencies, test business logic in isolation
    - Example: `ConflictCheckerTests` - Test shift overlap detection
    - Example: `NotificationServiceTests` - Test notification creation
  - **Controller tests:** Test API endpoint behavior
  - **Validation tests:** Test DataAnnotations, FluentValidation
- **Integration test patterns:**
  - WebApplicationFactory for end-to-end tests
  - In-memory database for isolated testing
  - Test entire request/response flow
- **Test coverage goals:**
  - Target: 80% code coverage
  - Focus on critical business logic (ConflictChecker, TimeOffRequestService, etc.)
- **Mocking patterns with Moq:**
  ```csharp
  var mockDbContext = new Mock<AppDbContext>();
  var mockService = new Mock<INotificationService>();
  mockService.Setup(s => s.CreateNotificationAsync(...)).ReturnsAsync(new Notification());
  ```
- **Manual testing checklist:**
  - Multi-tenancy isolation (cannot see other company data)
  - Shift conflict detection (overlapping shifts rejected)
  - Time-off approval workflow (notifications sent)
  - Griffin ADFS login flow (auto-provision user)
  - Dark mode toggle (theme persistence)
  - Hebrew RTL rendering (sidebar flipped, text alignment)
  - Air-gapped deployment (offline operation)

**Key Questions Answered:**
- How is the application tested?
- What testing frameworks are used?
- How do I write unit tests for services?
- What is the test coverage?

---

### Part VIII: Data Management & Lifecycle (Document 20)

#### [20-DATA-LIFECYCLE-MANAGEMENT.md](20-DATA-LIFECYCLE-MANAGEMENT.md) (2,000+ lines) ⭐ NEW
**Purpose:** Historical data archival, purge, and re-import

**Implementation Date:** 2026-01-06
**Feature Status:** ✅ Complete

**Contents:**
- **Three-Service Architecture:**
  - **ArchiveService:** Export data in CSV (human-readable) + NDJSON (re-importable) formats
  - **PurgeService:** Delete historical data with FK-safe deletion order
  - **ImportService:** Re-import archived data with duplicate detection
- **Safety Mechanisms:**
  - Fresh archive requirement (must archive before purge)
  - Typed confirmation (`DELETE {COMPANY} BEFORE {DATE}`)
  - Pre-purge automatic database backup
  - Transaction rollback on error
  - Complete audit logging
- **Export Formats:**
  - **CSV ZIP:** Human-readable, compliance-friendly (Excel/analysis)
  - **NDJSON ZIP:** Machine-readable, re-importable (disaster recovery)
  - **SHA-256 hashing:** Archive integrity verification
- **Database Deletion Order** (FK-safe):
  1. SwapRequest (by related shift date)
  2. TimeOffRequest + OFFLINE shifts auto-cleanup
  3. ShiftInstance → ShiftAssignment (cascade)
  4. Chore
  5. OnDuty (soft or hard delete)
- **UI Implementation:**
  - Single page with 3 tabs (Archive | Purge | Import)
  - Owner-only access (`[Authorize(Policy = "IsAdmin")]`)
  - Bootstrap nav-tabs pattern (same as EmailConfig)
- **Natural Key Strategy** (import):
  - User matching by email (not database ID)
  - ShiftType matching by key (e.g., "MORNING")
  - Composite keys for shifts (CompanyId, WorkDate, ShiftTypeKey)
- **Use Cases:**
  - Annual data archival (compliance + performance)
  - Disaster recovery (restore from archive)
  - Company merger data migration
  - Air-gapped USB transfer workflow
- **Technical Implementation:**
  - System.IO.Compression.ZipFile for archive creation
  - SHA256 hashing for integrity verification
  - EF Core transactions for atomic deletion
  - SQLite VACUUM for space reclamation
  - Streaming NDJSON parsing (low memory footprint)
- **File Storage:**
  - `Archives/` - CSV + NDJSON exports (90-day retention recommended)
  - `Backups/` - Pre-purge database backups (indefinite retention)
  - `Uploads/` - Temp import files (auto-cleanup)
- **Edge Cases Handled:**
  - OnDuty global table (no CompanyId) - scoped by UserId
  - OFFLINE shifts auto-cleanup with TimeOff
  - Cascade deletion counting (ShiftInstance → ShiftAssignment)
  - Missing users on import (modal dialog: Skip or Cancel)

**Key Questions Answered:**
- How do I archive historical data for compliance?
- How do I safely purge old data to reclaim disk space?
- How do I restore data from an archive (disaster recovery)?
- How does the FK-safe deletion order work?
- What are the safety mechanisms to prevent accidental data loss?
- How do I migrate data between ShiftManager instances?

---

### Part IX: The "Soul" - Design Decisions (Document 18)

#### [18-DESIGN-DECISIONS-AND-TRADEOFFS.md](18-DESIGN-DECISIONS-AND-TRADEOFFS.md) (1,200 lines)
**Purpose:** Architectural rationale and "why"

**Contents:**
- **Critical design decisions:**
  - **Why Razor Pages over MVC or Blazor?**
    - Simpler page-focused model (less ceremony than MVC)
    - Server-side rendering (no JavaScript framework complexity)
    - Better for air-gapped deployment (no npm, no webpack)
    - Easier for junior developers to understand
  - **Why SQLite over SQL Server?**
    - Zero-config deployment (no separate database server)
    - Single-file database (easy backup/restore)
    - Perfect for air-gapped (no network dependencies)
    - Sufficient for single-server deployment (<1000 concurrent users)
  - **Why NO frontend framework (React, Vue, Angular)?**
    - Reduce deployment complexity (no npm install, no node_modules)
    - Avoid npm dependencies in air-gapped environment
    - Faster page loads (no large JS bundles)
    - Easier maintenance (vanilla JS is timeless)
  - **Why service-level caching over distributed cache (Redis)?**
    - Single-server deployment (no Redis dependency)
    - IMemoryCache sufficient for workload
    - Simpler deployment in air-gapped environment
  - **Why cookie auth over JWT?**
    - Simpler session management (server-side)
    - Better for browser-based UI (automatic cookie handling)
    - Secure httpOnly cookies (XSS protection)
    - JWT better for mobile apps (not our use case)
  - **Why EF Core global query filters over manual WHERE clauses?**
    - Safety: Impossible to forget CompanyId filter
    - DRY principle: Filter logic in one place (DbContext)
    - Automatic tenant scoping
  - **Why OnDuty has NO CompanyId?**
    - Business requirement: Cross-company visibility for coordination
    - Example: Military base with multiple units, shared on-duty roster
- **Tradeoffs documented:**
  - **Monolith vs. Microservices:** Chose monolith
    - ✅ Simpler deployment, debugging, development
    - ✅ No distributed transaction complexity
    - ⚠️ Limited horizontal scaling (acceptable for workload)
  - **HTTP vs. HTTPS default:** Chose HTTP for localhost, HTTPS for production
    - ✅ Easier local development
    - ⚠️ Must remember to enable HTTPS in production
  - **SQLite limitations:**
    - ⚠️ No built-in encryption (use file-system encryption)
    - ⚠️ Single-writer (acceptable for workload)
    - ⚠️ No stored procedures (business logic in C#)
  - **Vanilla JS vs. Framework:**
    - ✅ Zero npm dependencies, air-gapped ready
    - ⚠️ More verbose than React components
    - ⚠️ Manual DOM manipulation
- **Patterns avoided and why:**
  - **CQRS (Command Query Responsibility Segregation):** Overkill for CRUD-heavy app
  - **Event sourcing:** Complexity not justified by business requirements
  - **Repository pattern over DbContext:** EF Core is already a repository (unnecessary abstraction)
  - **Microservices:** Deployment complexity not justified for single-server workload

**Key Questions Answered:**
- Why was the system designed this way?
- What alternatives were considered?
- What are the tradeoffs?
- What patterns were intentionally avoided?

---

### Part X: Reconstruction Guide (Document 19)

#### [19-RECONSTRUCTION-RECIPE.md](19-RECONSTRUCTION-RECIPE.md) (1,500 lines)
**Purpose:** Step-by-step rebuild from scratch (10 phases)

**Contents:**
- **Phase 1: Environment Setup**
  - Install .NET SDK 8.0 (`dotnet --version` → 8.0.x)
  - Install Git, Visual Studio Code / Visual Studio
  - Clone repository structure (or create from scratch)
  - Create solution and project files:
    ```bash
    dotnet new sln -n ShiftManager
    dotnet new webapp -o ShiftManager -f net8.0
    dotnet new xunit -o ShiftManager.Tests -f net8.0
    dotnet sln add ShiftManager/ShiftManager.csproj
    dotnet sln add ShiftManager.Tests/ShiftManager.Tests.csproj
    ```
  - Add NuGet packages (exact versions):
    ```bash
    cd ShiftManager
    dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.9
    dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 9.0.9
    dotnet add package SixLabors.ImageSharp --version 3.1.11
    dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore --version 8.0.0
    ```
- **Phase 2: Database Layer**
  - Create `Models/` folder with all 28 entities
  - Create `Data/AppDbContext.cs` (592 lines, copy from [06-DOMAIN-MODELS.md](#06-domain-modelsmd))
  - Create `Data/CompanyIdInterceptor.cs` (copy from [05-MULTI-TENANCY-DEEP-DIVE.md](#05-multi-tenancy-deep-divemd))
  - Configure EF Core in `Program.cs`:
    ```csharp
    services.AddDbContext<AppDbContext>((serviceProvider, opt) => {
        var interceptor = serviceProvider.GetRequiredService<CompanyIdInterceptor>();
        opt.UseSqlite("Data Source=app.db")
           .EnableDetailedErrors()
           .AddInterceptors(interceptor);
    });
    ```
- **Phase 3: Multi-Tenancy Foundation**
  - Implement `IBelongsToCompany` interface (marker for tenant-scoped entities)
  - Create `Services/CompanyContext.cs` (current user's company tracking)
  - Create `Data/CompanyIdInterceptor.cs` (automatic CompanyId scoping on SaveChanges)
  - Add global query filters to `AppDbContext.OnModelCreating`:
    ```csharp
    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
    {
        if (typeof(IBelongsToCompany).IsAssignableFrom(entityType.ClrType))
        {
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(...);
        }
    }
    ```
- **Phase 4: Service Layer**
  - Create `Services/` folder
  - Implement 40+ services (copy from [07-SERVICE-LAYER.md](#07-service-layermd))
  - Register DI in `Program.cs` (copy from [04-STARTUP-AND-MIDDLEWARE.md](#04-startup-and-middlewaremd))
- **Phase 5: Authentication**
  - Configure ASP.NET Core Identity (cookie authentication)
  - Implement password hashing (PBKDF2, 100k iterations)
  - Create auth pages: Login, Signup, Logout, ForgotPassword
  - Implement role-based authorization (6 roles, 8 policies)
  - Add Griffin ADFS integration (GriffinService, GriffinAuthenticationMiddleware)
- **Phase 6: UI Layer**
  - Create `Pages/` folder structure (66 Razor Pages)
  - Implement `Pages/Shared/_Layout.cshtml` (317 lines, copy from [08-UI-UX-ARCHITECTURE.md](#08-ui-ux-architecturemd))
  - Create `wwwroot/` assets:
    - `css/site.css` (3,800 lines)
    - `css/rtl.css` (124 lines)
    - `css/shift-swap-game.css` (775 lines)
    - `js/site.js` (1,114 lines)
    - `js/shift-swap-game.js` (1,376 lines)
  - Add localization resources (`Resources/SharedResources.*.resx`)
- **Phase 7: API Layer**
  - Create `Controllers/Api/` folder
  - Implement 27 API endpoints (copy from [09-API-LAYER.md](#09-api-layermd))
  - Add `Middleware/ApiAuthenticationMiddleware.cs` (X-API-Key validation)
  - Add `Middleware/ApiRateLimitingMiddleware.cs` (rate limiting)
  - Create API client libraries (`clients/python/`)
- **Phase 8: Business Logic**
  - Implement workflows: time-off, swaps, chores (copy from [14-WORKFLOWS-AND-BUSINESS-LOGIC.md](#14-workflows-and-business-logicmd))
  - Add conflict detection (ConflictChecker service)
  - Implement notification system (NotificationService, DailyNotificationJob)
- **Phase 9: Deployment**
  - Create `Build-Release.ps1` (458 lines, copy from [16-BUILD-AND-RELEASE-PIPELINE.md](#16-build-and-release-pipelinemd))
  - Add deployment helper scripts:
    - `UNBLOCK_FILES.bat`
    - `VERIFY_FILES.bat`
    - `QUICK_FIX.bat`
  - Configure `appsettings.json` and `appsettings.Production.json`
  - Create deployment documentation
- **Phase 10: Testing**
  - Create `ShiftManager.Tests` project
  - Write unit tests (xUnit, Moq, FluentAssertions)
  - Implement integration tests (WebApplicationFactory)
  - Manual testing checklist (copy from [17-TESTING-STRATEGY.md](#17-testing-strategymd))

**Verification Checklist:**
- [ ] Application starts (`dotnet run`)
- [ ] Database seeding successful (Demo Co, admin@local user created)
- [ ] Login works (email: admin@local, password: from seed config)
- [ ] Multi-tenancy enforced (cannot see other company data)
- [ ] Shift conflict detection works
- [ ] Time-off approval workflow works
- [ ] Notifications sent
- [ ] Dark mode toggle works
- [ ] Hebrew RTL rendering works
- [ ] Command palette works (Ctrl+K)
- [ ] API key authentication works
- [ ] Build pipeline works (`Build-Release.ps1 -Version "1.0.0"`)
- [ ] Air-gapped deployment works (USB transfer to offline machine)

**Key Questions Answered:**
- How do I rebuild ShiftManager from scratch?
- What is the dependency installation order?
- What are the critical configuration steps?
- How do I verify the rebuild is successful?

---

## Diagrams Reference

All diagrams are in Mermaid format (render in GitHub, VS Code, or any Mermaid-compatible viewer).

### Diagram Index

| Diagram | Purpose | Referenced In |
|---------|---------|---------------|
| [architecture-overview.mmd](diagrams/architecture-overview.mmd) | C4 component diagram (presentation → service → data → infrastructure) | [02-ARCHITECTURE-BLUEPRINT.md](#02-architecture-blueprintmd) |
| [database-erd.mmd](diagrams/database-erd.mmd) | Complete ERD with all 28 tables, relationships, cardinality | [03-DATABASE-SCHEMA.md](#03-database-schemamd) |
| [multi-tenant-flow.mmd](diagrams/multi-tenant-flow.mmd) | Sequence diagram of tenant scoping (request → interceptor → query filter) | [05-MULTI-TENANCY-DEEP-DIVE.md](#05-multi-tenancy-deep-divemd) |
| [request-approval-flow.mmd](diagrams/request-approval-flow.mmd) | Time-off and swap request workflows | [14-WORKFLOWS-AND-BUSINESS-LOGIC.md](#14-workflows-and-business-logicmd) |
| [authentication-flow.mmd](diagrams/authentication-flow.mmd) | Login flows (local auth + Griffin ADFS) | [10-AUTHENTICATION-AND-AUTHORIZATION.md](#10-authentication-and-authorizationmd) |
| [middleware-pipeline.mmd](diagrams/middleware-pipeline.mmd) | ASP.NET middleware order (error handling → static files → routing → auth → endpoints) | [04-STARTUP-AND-MIDDLEWARE.md](#04-startup-and-middlewaremd) |
| [service-dependency-graph.mmd](diagrams/service-dependency-graph.mmd) | DI relationships between 40+ services | [07-SERVICE-LAYER.md](#07-service-layermd) |
| [deployment-architecture.mmd](diagrams/deployment-architecture.mmd) | Air-gapped deployment topology (dev machine → USB → production server) | [15-AIR-GAPPED-DEPLOYMENT.md](#15-air-gapped-deploymentmd) |

---

## Quick Reference

### Key File Locations

| File | Lines | Purpose |
|------|-------|---------|
| `Program.cs` | 436 | Application bootstrap, DI, middleware pipeline |
| `Data/AppDbContext.cs` | 592 | Database schema, 28 DbSets, global query filters |
| `Data/CompanyIdInterceptor.cs` | ~100 | Multi-tenancy interceptor (automatic CompanyId scoping) |
| `Build-Release.ps1` | 458 | 10-stage automated release pipeline |
| `Pages/Shared/_Layout.cshtml` | 317 | App shell (sidebar, header, main content) |
| `wwwroot/css/site.css` | 3,800 | Main design system, dark mode, components |
| `wwwroot/css/rtl.css` | 124 | Hebrew RTL overrides |
| `wwwroot/js/site.js` | 1,114 | Core JS features (theme, command palette, keyboard shortcuts) |
| `wwwroot/js/shift-swap-game.js` | 1,376 | Match-3 easter egg game |
| `appsettings.json` | ~200 | Application configuration |

### Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| **Runtime** | .NET | 8.0 |
| **Framework** | ASP.NET Core Razor Pages | 8.0 |
| **ORM** | Entity Framework Core | 9.0.9 |
| **Database** | SQLite | (via EF Core provider) |
| **Image Processing** | SixLabors.ImageSharp | 3.1.11 |
| **Testing Framework** | xUnit | 2.5.3 |
| **Assertion Library** | FluentAssertions | 8.7.1 |
| **Mocking Library** | Moq | 4.20.72 |
| **Integration Testing** | Microsoft.AspNetCore.Mvc.Testing | 8.0.10 |
| **CSS** | Custom (no framework) | 5,375 lines |
| **JavaScript** | Vanilla (no framework) | 2,700+ lines |

### Database Tables (28 Total)

**Core Domain:**
- Company (tenant root)
- AppUser (users, 6 roles)
- ShiftType → ShiftInstance → ShiftAssignment (shift scheduling hierarchy)
- TimeOffRequest, SwapRequest (request workflows)
- Chore, OnDuty, OnDutyRoleSubscription (task assignments)

**Collaboration:**
- TeamCalendar, TeamCalendarMember (team calendars)
- UserNotification, DailyNotificationPreference (notifications)
- Feedback (user feedback)

**Audit & Compliance:**
- AuditLog, RoleAssignmentAudit, ProfileChangeAudit (audit trails)

**Multi-Tenancy:**
- DirectorCompany (cross-tenant director access)
- UserJoinRequest (self-service signup)

**API & Infrastructure:**
- ApiKey, ApiKeyRequest, ApiRequestLog (API management)
- EmailConfig, EmailApiLog, EmailTemplateCustomization (email integration)
- GriffinConfig (ADFS integration)
- OnDutyTypeConfig (global on-duty types)
- AppConfig (key-value config store)

**Gamification:**
- GameScore (leaderboard)

### User Roles & Permissions

| Role | Code | Key Permissions |
|------|------|-----------------|
| **Owner** | 0 | Full system, company config, director assignment, audit log, database console, backup |
| **Director** | 3 | Cross-company access, view all companies, analytics |
| **Manager** | 1 | Approve requests, assign shifts, manage team, analytics, edit chores/on-duty |
| **Assigner** | 5 | Edit chores only (limited role) |
| **Employee** | 2 | View schedule, submit time-off/swap requests, view chores/on-duty |
| **Trainee** | 4 | Limited view, shadowing, cannot request swaps |

### API Endpoint Categories (27 Total)

- **Users:** 4 endpoints (list, get, create, update)
- **Shifts:** 2 endpoints (list, get)
- **TimeOff:** 5 endpoints (list, get, create, approve, decline)
- **Notifications:** 4 endpoints (list, get, mark-read, mark-all-read)
- **SwapRequests:** 6 endpoints (list, get, create, approve, decline, delete)
- **Chores:** 5 endpoints (list, get, create, update, delete)
- **OnDuty:** 5 endpoints (list, get, create, update, delete)
- **Feedback:** 5 endpoints (list, get, create, update-status, delete)
- **Analytics:** 1 endpoint (summary)
- **AuditLogs:** 1 endpoint (list)

### Localization Support

| Language | Culture Code | RTL | Status |
|----------|--------------|-----|--------|
| English | en-US | No | Default |
| Hebrew | he-IL | Yes | Full support (rtl.css, 124 lines) |

### Build & Deployment

**Build Command:**
```bash
pwsh Build-Release.ps1 -Version "2.1.0"
```

**Output:**
- `ProjectPublish/` folder (378 files, 111 MB)
- `packages/ShiftManager-v2.1.0-win-x64.zip`

**Deployment Steps:**
1. Copy ZIP to USB drive
2. Transfer to air-gapped machine
3. Extract package
4. Run `UNBLOCK_FILES.bat`
5. Run `VERIFY_FILES.bat`
6. Configure `appsettings.Production.json`
7. Run `ShiftManager.exe`

---

## Navigation Tips

### Cross-References

All documents include cross-references to related sections. Example:
- "See [05-MULTI-TENANCY-DEEP-DIVE.md](#05-multi-tenancy-deep-divemd) for tenant isolation details"
- "Refer to diagram [database-erd.mmd](diagrams/database-erd.mmd)"

### Search Strategy

**By Topic:**
- **Multi-Tenancy:** [05-MULTI-TENANCY-DEEP-DIVE.md](#05-multi-tenancy-deep-divemd)
- **Database:** [03-DATABASE-SCHEMA.md](#03-database-schemamd), [06-DOMAIN-MODELS.md](#06-domain-modelsmd), [12-DATA-MIGRATIONS.md](#12-data-migrationsmd)
- **Authentication:** [10-AUTHENTICATION-AND-AUTHORIZATION.md](#10-authentication-and-authorizationmd)
- **API:** [09-API-LAYER.md](#09-api-layermd)
- **UI:** [08-UI-UX-ARCHITECTURE.md](#08-ui-ux-architecturemd)
- **Deployment:** [15-AIR-GAPPED-DEPLOYMENT.md](#15-air-gapped-deploymentmd), [16-BUILD-AND-RELEASE-PIPELINE.md](#16-build-and-release-pipelinemd)
- **Data Lifecycle:** [20-DATA-LIFECYCLE-MANAGEMENT.md](#20-data-lifecycle-managementmd)
- **Why Decisions:** [18-DESIGN-DECISIONS-AND-TRADEOFFS.md](#18-design-decisions-and-tradeoffsmd)
- **Rebuild:** [19-RECONSTRUCTION-RECIPE.md](#19-reconstruction-recipemd)

**By Component:**
- **Startup:** [04-STARTUP-AND-MIDDLEWARE.md](#04-startup-and-middlewaremd)
- **Services:** [07-SERVICE-LAYER.md](#07-service-layermd)
- **Workflows:** [14-WORKFLOWS-AND-BUSINESS-LOGIC.md](#14-workflows-and-business-logicmd)
- **Testing:** [17-TESTING-STRATEGY.md](#17-testing-strategymd)
- **Caching:** [13-CACHING-STRATEGY.md](#13-caching-strategymd)
- **Localization:** [11-LOCALIZATION-AND-RTL.md](#11-localization-and-rtlmd)

---

## Document Status

| Document | Status | Last Updated |
|----------|--------|--------------|
| 00-INDEX.md | ✅ Complete | 2026-01-06 |
| 01-EXECUTIVE-OVERVIEW.md | 🚧 Pending | - |
| 02-ARCHITECTURE-BLUEPRINT.md | 🚧 Pending | - |
| 03-DATABASE-SCHEMA.md | 🚧 Pending | - |
| 04-STARTUP-AND-MIDDLEWARE.md | 🚧 Pending | - |
| 05-MULTI-TENANCY-DEEP-DIVE.md | 🚧 Pending | - |
| 06-DOMAIN-MODELS.md | 🚧 Pending | - |
| 07-SERVICE-LAYER.md | 🚧 Pending | - |
| 08-UI-UX-ARCHITECTURE.md | 🚧 Pending | - |
| 09-API-LAYER.md | 🚧 Pending | - |
| 10-AUTHENTICATION-AND-AUTHORIZATION.md | 🚧 Pending | - |
| 11-LOCALIZATION-AND-RTL.md | 🚧 Pending | - |
| 12-DATA-MIGRATIONS.md | 🚧 Pending | - |
| 13-CACHING-STRATEGY.md | 🚧 Pending | - |
| 14-WORKFLOWS-AND-BUSINESS-LOGIC.md | 🚧 Pending | - |
| 15-AIR-GAPPED-DEPLOYMENT.md | 🚧 Pending | - |
| 16-BUILD-AND-RELEASE-PIPELINE.md | 🚧 Pending | - |
| 17-TESTING-STRATEGY.md | 🚧 Pending | - |
| 18-DESIGN-DECISIONS-AND-TRADEOFFS.md | 🚧 Pending | - |
| 19-RECONSTRUCTION-RECIPE.md | 🚧 Pending | - |
| 20-DATA-LIFECYCLE-MANAGEMENT.md | ✅ Complete | 2026-01-06 |

---

## Contact & Contribution

**Primary Documentation Author:** OmniCortex Architect (Claude Code)
**Documentation Date:** 2026-01-06 (Updated)
**Codebase Version:** v2.2.0+ (Branch: newestversionpriorpl)
**Documentation Repository:** `docs/genesis/`

**How to Contribute:**
1. Read existing documentation thoroughly
2. Follow the same structure and level of detail
3. Update cross-references when adding new sections
4. Maintain Mermaid diagrams alongside code changes
5. Update this index when adding new documents

---

## Glossary

**Air-Gapped:** A network security measure where a computer/network is physically isolated from unsecured networks (no internet).

**Multi-Tenancy:** A software architecture where a single instance serves multiple tenants (companies), with complete data isolation.

**Row-Level Security:** Database security technique where access control is enforced at the row level (CompanyId filtering).

**Global Query Filter:** EF Core feature that automatically applies WHERE clauses to all queries for specific entities.

**CompanyId Interceptor:** EF Core SaveChanges interceptor that automatically sets CompanyId on entities implementing IBelongsToCompany.

**PBKDF2:** Password-Based Key Derivation Function 2, a secure password hashing algorithm (100k iterations with SHA256).

**RTL (Right-to-Left):** Text direction for languages like Hebrew and Arabic.

**Razor Pages:** ASP.NET Core page-focused web UI framework (alternative to MVC).

**Self-Contained Deployment:** .NET deployment that includes the runtime (no SDK required on target machine).

**Griffin ADFS:** Air-gapped Active Directory Federation Services integration for single sign-on.

---

**End of Index**

---

**Next Steps:**
- For business context: Read [01-EXECUTIVE-OVERVIEW.md](01-EXECUTIVE-OVERVIEW.md)
- For architecture: Read [02-ARCHITECTURE-BLUEPRINT.md](02-ARCHITECTURE-BLUEPRINT.md)
- For database: Read [03-DATABASE-SCHEMA.md](03-DATABASE-SCHEMA.md)
- For complete rebuild: Read [19-RECONSTRUCTION-RECIPE.md](19-RECONSTRUCTION-RECIPE.md)
