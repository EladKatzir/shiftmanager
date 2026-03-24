# ShiftManager - Architecture Blueprint
## System Design and Technical Foundation

**Document Version:** 1.0
**Last Updated:** 2025-12-30
**Architecture Style:** Monolithic Layered Architecture with Service-Oriented Design

[⬅️ Back to Index](00-INDEX.md) | [➡️ Next: Database Schema](03-DATABASE-SCHEMA.md)

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Technology Stack Deep Dive](#technology-stack-deep-dive)
3. [Architectural Style](#architectural-style)
4. [Layering Strategy](#layering-strategy)
5. [Project Structure](#project-structure)
6. [Dependency Injection Philosophy](#dependency-injection-philosophy)
7. [Cross-Cutting Concerns](#cross-cutting-concerns)
8. [Deployment Architecture](#deployment-architecture)
9. [Scalability Considerations](#scalability-considerations)
10. [Security Architecture](#security-architecture)
11. [Performance Architecture](#performance-architecture)
12. [Data Flow Patterns](#data-flow-patterns)
13. [Design Patterns Used](#design-patterns-used)
14. [Why NOT Microservices](#why-not-microservices)

---

## Architecture Overview

### High-Level Architecture

**Diagram:** [View architecture-overview.mmd](diagrams/architecture-overview.mmd)

ShiftManager follows a **classic layered monolithic architecture** optimized for air-gapped Windows environments:

```
┌─────────────────────────────────────────────────────────┐
│                    Presentation Layer                    │
│  Razor Pages (66) + Static Assets (CSS, JS)             │
│  View Components (4) + Layouts                           │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│                      API Layer                           │
│  REST Controllers (10) + 27 Endpoints                    │
│  API Middleware (Auth, Rate Limiting, Logging)           │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│                    Service Layer                         │
│  Business Logic Services (40+)                           │
│  Multi-Tenancy + Caching + Infrastructure Services       │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│                  Data Access Layer                       │
│  EF Core DbContext + CompanyIdInterceptor                │
│  Global Query Filters + Migrations                       │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│                   Data Storage                           │
│  SQLite Database (app.db) - Single File                  │
└─────────────────────────────────────────────────────────┘
```

### Key Architectural Decisions

1. **Monolithic Over Microservices:** Single deployment unit, simplified operations, perfect for air-gapped environments
2. **Razor Pages Over SPA Frameworks:** Server-side rendering, no npm dependencies, faster time-to-interactive
3. **SQLite Over SQL Server:** Zero-config deployment, single-file database, air-gapped friendly
4. **Service-Oriented Layering:** Business logic encapsulated in injectable services, testable and maintainable
5. **Multi-Tenancy at Database Level:** Row-level security via EF Core query filters, complete data isolation

---

## Technology Stack Deep Dive

### Core Framework

**ASP.NET Core 8.0**
- **Why:** Long-term support (LTS) release, supported until November 2026
- **Benefits:** Mature ecosystem, high performance, cross-platform (though targeting Windows)
- **Self-Contained Deployment:** Includes .NET 8.0 runtime, no SDK required on target machine
- **File:** `ShiftManager.csproj` targets `net8.0`

### Web Framework

**Razor Pages**
- **Why:** Page-focused model simpler than MVC for CRUD-heavy applications
- **Benefits:** Less ceremony, faster development, easier for junior developers
- **Alternative Considered:** Blazor (rejected due to larger payload, SignalR dependency for Blazor Server)
- **Page Count:** 66 Razor Pages across Auth, Calendar, Admin, Owner, Director, My, Public areas

### ORM & Database

**Entity Framework Core 9.0.9**
- **Why:** Latest stable EF Core, superior performance over 8.x
- **Provider:** `Microsoft.EntityFrameworkCore.Sqlite` 9.0.9
- **Features Used:**
  - Global Query Filters (multi-tenancy)
  - SaveChanges Interceptors (CompanyIdInterceptor)
  - Code-First Migrations (36 migrations)
  - Value Converters (DateOnly/TimeOnly → string for SQLite)
  - Optimistic Concurrency (Concurrency tokens)

**SQLite 3.x**
- **Why:** Single-file database, zero configuration, perfect for air-gapped deployment
- **File Location:** `app.db` (root directory)
- **Size:** ~50-200 MB typical (1000 users, 100k shift assignments)
- **Limitations Accepted:**
  - No built-in encryption (mitigated: file-system encryption)
  - Single-writer concurrency (acceptable for read-heavy workload)
  - No stored procedures (business logic in C# services)

### Image Processing

**SixLabors.ImageSharp 3.1.11**
- **Why:** Pure C# image library, no native dependencies (GDI+ alternatives have Windows dependencies)
- **Usage:** Avatar upload, resize, format conversion
- **Benefits:** Cross-platform compatible, no external DLL dependencies

### Testing Stack

**xUnit 2.5.3** (Test Framework)
- **Why:** Industry standard for .NET, excellent parallel test execution
- **Alternatives Considered:** NUnit (rejected, xUnit more modern)

**FluentAssertions 8.7.1** (Assertion Library)
- **Why:** Readable, expressive assertions (`result.Should().Be(expected)`)
- **Benefits:** Better error messages than Assert.Equal()

**Moq 4.20.72** (Mocking Framework)
- **Why:** Most popular .NET mocking library, simple API
- **Usage:** Mock services, DbContext for unit tests

**Microsoft.AspNetCore.Mvc.Testing 8.0.10** (Integration Testing)
- **Why:** Test entire HTTP request/response pipeline
- **Benefits:** WebApplicationFactory for end-to-end tests

**Microsoft.EntityFrameworkCore.InMemory 9.0.9** (In-Memory Database)
- **Why:** Fast, isolated database for integration tests
- **Alternatives:** SQLite in-memory (also viable, InMemory chosen for simplicity)

### Frontend Stack

**Vanilla JavaScript** (2,700+ lines across 6 files)
- **Why:** No npm dependencies, no webpack, perfect for air-gapped deployment
- **Alternatives Considered:** jQuery (obsolete), Alpine.js (still a dependency)
- **Key Files:**
  - `site.js` (1,114 lines) - Core features (theme, command palette, keyboard shortcuts)
  - `shift-swap-game.js` (1,376 lines) - Match-3 easter egg game
  - `calendar-inline-edit.js` - Quick-add forms
  - `session-check.js` - Session validation
  - `hebrew-audit.js` - Localization QA (dev only)
  - `myteam.js` - Team calendar filtering

**Custom CSS** (5,375 lines across 3 files)
- **Why:** No Bootstrap, no Tailwind, complete control over design
- **Alternatives Considered:** Bootstrap (bloated), Tailwind (requires PostCSS build step)
- **Key Files:**
  - `site.css` (3,800 lines) - Design system, dark mode, components
  - `rtl.css` (124 lines) - Hebrew RTL overrides
  - `shift-swap-game.css` (775 lines) - Game UI

**No Build Step Required**
- CSS/JS served directly (no webpack, no npm install)
- Cache busting via ASP.NET Core `asp-append-version="true"`
- Faster deployment in air-gapped environments

---

## Architectural Style

### Monolithic Layered Architecture

**Definition:** Single deployment unit with clear separation of concerns across horizontal layers.

**Layers (from top to bottom):**
1. **Presentation Layer:** User interface (Razor Pages, static assets)
2. **API Layer:** REST endpoints for external integrations
3. **Service Layer:** Business logic encapsulation
4. **Data Access Layer:** EF Core DbContext, entity mappings
5. **Data Storage Layer:** SQLite database

**Benefits:**
- ✅ Simple deployment (one executable, one database file)
- ✅ Easy debugging (single process, step through entire stack)
- ✅ Transactional consistency (ACID guarantees within single database)
- ✅ Low latency (in-process method calls, no network hops)
- ✅ Perfect for air-gapped environments (no distributed system complexity)

**Tradeoffs:**
- ❌ Limited horizontal scaling (vertical scaling sufficient for workload)
- ❌ Entire application redeployed for any change (acceptable for monthly update cycle)
- ❌ Technology lock-in (entire stack must be .NET - acceptable, mature ecosystem)

### Service-Oriented Design

**Definition:** Business logic encapsulated in loosely-coupled, highly-cohesive services.

**Service Categories:**
1. **Core Business Services:** ShiftAssignmentService, NotificationService, AnalyticsService, etc.
2. **Multi-Tenancy Services:** TenantResolver, CompanyContext, CompanyFilterService
3. **Infrastructure Services:** MailService, EncryptionService, GriffinService
4. **API Services:** UserApiService, ShiftApiService, TimeOffApiService, etc.
5. **Caching Services:** ShiftTypeCacheService, CompanyCacheService, AppConfigCacheService
6. **Background Services:** DailyNotificationJob (IHostedService)

**Service Design Principles:**
- **Single Responsibility:** Each service has one clear purpose
- **Dependency Injection:** All services registered in DI container
- **Interface Segregation:** Services depend on abstractions (interfaces) where testability matters
- **Constructor Injection:** Dependencies injected via constructors

**Example Service:**
```csharp
public class ShiftAssignmentService
{
    private readonly AppDbContext _db;
    private readonly IAppConfigCacheService _configCache;

    public ShiftAssignmentService(AppDbContext db, IAppConfigCacheService configCache)
    {
        _db = db;
        _configCache = configCache;
    }

    public async Task<bool> HasConflictAsync(int userId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        // Business logic: Check for overlapping shifts
        // Enforce rest hours policy (from config cache)
        // Return true if conflict detected
    }
}
```

---

## Layering Strategy

### Layer 1: Presentation Layer

**Location:** `Pages/` folder (66 Razor Pages)

**Responsibilities:**
- Render HTML views
- Handle user input (form submissions, button clicks)
- Client-side validation (HTML5 + JavaScript)
- Invoke service layer methods
- Display results to user

**Technology:**
- ASP.NET Core Razor Pages (`.cshtml` files)
- Tag Helpers (`asp-for`, `asp-action`, etc.)
- Model Binding (automatic form → C# model mapping)
- Anti-forgery tokens (CSRF protection)

**Page Structure:**
```
Pages/
├── Shared/
│   ├── _Layout.cshtml                 # App shell (sidebar, header)
│   ├── _LocalizationScript.cshtml     # Inject localized strings to JS
│   └── Components/                    # View Components
├── Auth/                              # Authentication (5 pages)
├── Calendar/                          # Calendar views (4 pages)
├── Admin/                             # Admin pages (9 pages)
├── Owner/                             # Owner panel (6 pages)
├── Director/                          # Director tools (3 pages)
├── My/                                # Employee self-service (6 pages)
├── MyTeam/                            # Team calendars (1 page)
├── Public/                            # Public pages (3 pages)
├── Requests/                          # Request management (3 pages)
└── Api/                               # Internal browser API (8 pages)
```

**Design Pattern:** Page Model Pattern (Razor Pages inherent pattern)
- Each page has a `PageModel` class (code-behind)
- Separation of concerns: `.cshtml` (view) + `.cshtml.cs` (logic)

**Example:**
```csharp
// Pages/Calendar/Month.cshtml.cs
public class MonthModel : LocalizedPageModel
{
    private readonly IShiftTypeCacheService _shiftTypeCache;
    private readonly AppDbContext _db;

    public MonthModel(IShiftTypeCacheService shiftTypeCache, AppDbContext db)
    {
        _shiftTypeCache = shiftTypeCache;
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync(int year, int month)
    {
        // Fetch shifts for month
        // Return view
    }
}
```

---

### Layer 2: API Layer

**Location:** `Controllers/Api/` folder (10 controllers, 27 endpoints)

**Responsibilities:**
- Expose REST API for external integrations
- Authenticate API requests (X-API-Key header)
- Validate input (model validation)
- Invoke API service layer
- Return JSON responses

**Technology:**
- ASP.NET Core MVC Controllers (API controllers)
- JSON serialization (System.Text.Json, camelCase)
- Model validation (DataAnnotations, FluentValidation)
- HTTP status codes (200, 201, 400, 401, 403, 404, 500)

**Controller Structure:**
```
Controllers/
└── Api/
    ├── UsersApiController.cs          # User CRUD (4 endpoints)
    ├── ShiftsApiController.cs         # Shift queries (2 endpoints)
    ├── TimeOffApiController.cs        # Time-off mgmt (5 endpoints)
    ├── NotificationsApiController.cs  # Notifications (4 endpoints)
    ├── SwapRequestsApiController.cs   # Shift swaps (6 endpoints)
    ├── ChoresApiController.cs         # Chores CRUD (5 endpoints)
    ├── OnDutyApiController.cs         # On-duty CRUD (5 endpoints)
    ├── FeedbackApiController.cs       # Feedback mgmt (5 endpoints)
    ├── AnalyticsApiController.cs      # Analytics (1 endpoint)
    └── AuditLogsApiController.cs      # Audit logs (1 endpoint)
```

**Design Pattern:** RESTful API Pattern
- Resource-based URLs (`/api/v1/users`, `/api/v1/shifts`)
- HTTP verbs map to CRUD (GET=Read, POST=Create, PUT=Update, DELETE=Delete)
- Status codes convey semantics (201 Created, 204 No Content, 404 Not Found)

**Example:**
```csharp
[ApiController]
[Route("api/v1/users")]
public class UsersApiController : ControllerBase
{
    private readonly UserApiService _userApiService;

    public UsersApiController(UserApiService userApiService)
    {
        _userApiService = userApiService;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsersAsync([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await _userApiService.GetUsersAsync(page, pageSize);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUserAsync([FromBody] CreateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = await _userApiService.CreateUserAsync(dto);
        return CreatedAtAction(nameof(GetUserByIdAsync), new { id = userId }, null);
    }
}
```

---

### Layer 3: Service Layer

**Location:** `Services/` folder (40+ services)

**Responsibilities:**
- Business logic implementation
- Workflow orchestration
- Data validation (business rules)
- Transaction management
- Caching
- External service integration (email, ADFS)

**Technology:**
- Plain C# classes (POCOs)
- Constructor injection
- Dependency inversion (depend on interfaces where needed)
- Async/await patterns

**Service Organization:**
```
Services/
├── Business/                          # Core business logic
│   ├── ShiftAssignmentService.cs
│   ├── NotificationService.cs
│   ├── AnalyticsService.cs
│   ├── ChoreService.cs
│   ├── OnDutyService.cs
│   ├── TimeOffRequestService.cs
│   ├── SwapRequestService.cs
│   └── ShiftAssignmentService.cs
├── MultiTenancy/                      # Multi-tenancy
│   ├── TenantResolver.cs
│   ├── CompanyContext.cs
│   ├── CompanyFilterService.cs
│   └── ViewAsModeService.cs
├── UserManagement/                    # User management
│   ├── ProfileService.cs
│   ├── AvatarService.cs
│   ├── AuditLogService.cs
│   ├── DirectorService.cs
│   └── TraineeService.cs
├── Caching/                           # Performance caching
│   ├── ShiftTypeCacheService.cs
│   ├── CompanyCacheService.cs
│   └── AppConfigCacheService.cs
├── Infrastructure/                    # Infrastructure
│   ├── MailService.cs
│   ├── EmailConfigService.cs
│   ├── EncryptionService.cs
│   ├── GriffinService.cs
│   ├── ApiKeyService.cs
│   ├── RateLimitingService.cs
│   └── SecurityLogger.cs
├── ApiLayer/                          # API-specific services
│   ├── UserApiService.cs
│   ├── ShiftApiService.cs
│   ├── TimeOffApiService.cs
│   ├── NotificationApiService.cs
│   ├── SwapRequestApiService.cs
│   ├── ChoreApiService.cs
│   ├── OnDutyApiService.cs
│   └── FeedbackApiService.cs
├── BackgroundJobs/                    # Hosted services
│   └── DailyNotificationJob.cs
└── TeamCollaboration/                 # Team features
    ├── TeamCalendarService.cs
    └── TeamCalendarEventAggregator.cs
```

**Design Patterns:**
- **Service Layer Pattern:** Encapsulate business logic in services
- **Repository Pattern (Implicit):** EF Core DbContext serves as repository
- **Unit of Work Pattern (Implicit):** EF Core DbContext tracks changes, SaveChanges commits
- **Strategy Pattern:** ShiftAssignmentService uses different validation strategies for shift types

**Example Service with Business Logic:**
```csharp
public class TimeOffRequestService
{
    private readonly AppDbContext _db;
    private readonly IShiftAssignmentService _shiftAssignmentService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TimeOffRequestService> _logger;

    public TimeOffRequestService(
        AppDbContext db,
        IShiftAssignmentService shiftAssignmentService,
        INotificationService notificationService,
        ILogger<TimeOffRequestService> logger)
    {
        _db = db;
        _shiftAssignmentService = shiftAssignmentService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<int> CreateTimeOffRequestAsync(int userId, DateOnly startDate, DateOnly endDate, string reason, TimeOffType type)
    {
        // Validation
        if (startDate > endDate)
            throw new ArgumentException("Start date must be before end date");

        if (startDate < DateOnly.FromDateTime(DateTime.Today))
            throw new ArgumentException("Cannot request time-off for past dates");

        // Business rule: Check for conflicts with existing shifts
        var hasConflict = await _conflictChecker.HasTimeOffConflictAsync(userId, startDate, endDate);
        if (hasConflict)
            throw new InvalidOperationException("Time-off conflicts with existing shifts");

        // Create request
        var request = new TimeOffRequest
        {
            UserId = userId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = reason,
            TimeOffType = type,
            Status = RequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.TimeOffRequests.Add(request);
        await _db.SaveChangesAsync();

        // Notify manager
        await _notificationService.NotifyManagerOfTimeOffRequestAsync(request.Id);

        _logger.LogInformation("Time-off request {RequestId} created by user {UserId}", request.Id, userId);

        return request.Id;
    }

    public async Task ApproveTimeOffRequestAsync(int requestId, int approverId)
    {
        var request = await _db.TimeOffRequests.FindAsync(requestId);
        if (request == null)
            throw new NotFoundException("Request not found");

        if (request.Status != RequestStatus.Pending)
            throw new InvalidOperationException("Request already reviewed");

        request.Status = RequestStatus.Approved;
        request.ReviewerId = approverId;
        request.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Create OFFLINE shift instances to block calendar
        await CreateOfflineShiftsAsync(request.UserId, request.StartDate, request.EndDate);

        // Notify employee
        await _notificationService.NotifyTimeOffApprovedAsync(request.Id);

        _logger.LogInformation("Time-off request {RequestId} approved by {ApproverId}", requestId, approverId);
    }

    private async Task CreateOfflineShiftsAsync(int userId, DateOnly startDate, DateOnly endDate)
    {
        // Implementation: Create OFFLINE shift type instances
    }
}
```

---

### Layer 4: Data Access Layer

**Location:** `Data/` folder

**Responsibilities:**
- EF Core DbContext configuration
- Entity mappings (Fluent API + Data Annotations)
- Global query filters (multi-tenancy)
- SaveChanges interception (CompanyIdInterceptor)
- Migrations (schema evolution)

**Technology:**
- Entity Framework Core 9.0.9
- Fluent API for entity configuration
- Data Annotations for validation
- Code-First Migrations

**Key Files:**
```
Data/
├── AppDbContext.cs                    # DbContext (592 lines)
├── CompanyIdInterceptor.cs            # Multi-tenancy interceptor
└── Migrations/                        # 36 migration files
```

**AppDbContext (Simplified):**
```csharp
public class AppDbContext : DbContext
{
    private readonly ITenantResolver _tenantResolver;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantResolver tenantResolver)
        : base(options)
    {
        _tenantResolver = tenantResolver;
    }

    // DbSets (28 total)
    public DbSet<Company> Companies { get; set; }
    public DbSet<AppUser> Users { get; set; }
    public DbSet<ShiftType> ShiftTypes { get; set; }
    public DbSet<ShiftInstance> ShiftInstances { get; set; }
    public DbSet<ShiftAssignment> ShiftAssignments { get; set; }
    // ... (23 more DbSets)

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global query filters for multi-tenancy
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IBelongsToCompany).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDbContext).GetMethod(nameof(SetGlobalQueryFilter), BindingFlags.NonPublic | BindingFlags.Static);
                var genericMethod = method.MakeGenericMethod(entityType.ClrType);
                genericMethod.Invoke(null, new object[] { modelBuilder, _tenantResolver });
            }
        }

        // Entity configurations
        ConfigureCompanies(modelBuilder);
        ConfigureAppUsers(modelBuilder);
        ConfigureShiftTypes(modelBuilder);
        // ... (25 more configurations)

        // Value converters for SQLite (DateOnly, TimeOnly)
        ConfigureValueConverters(modelBuilder);

        // Seed data
        SeedShiftTypes(modelBuilder);
    }

    private static void SetGlobalQueryFilter<T>(ModelBuilder modelBuilder, ITenantResolver tenantResolver) where T : class, IBelongsToCompany
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.CompanyId == tenantResolver.GetCurrentTenantId());
    }
}
```

**CompanyIdInterceptor:**
```csharp
public class CompanyIdInterceptor : SaveChangesInterceptor
{
    private readonly ICompanyContext _companyContext;
    private readonly IConfiguration _config;
    private readonly ILogger<CompanyIdInterceptor> _logger;

    public CompanyIdInterceptor(ICompanyContext companyContext, IConfiguration config, ILogger<CompanyIdInterceptor> logger)
    {
        _companyContext = companyContext;
        _config = config;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        SetCompanyId(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        SetCompanyId(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SetCompanyId(DbContext context)
    {
        var enforceScope = _config.GetValue<bool>("Features:EnforceCompanyScope", true);
        var currentCompanyId = _companyContext.GetCurrentCompanyId();

        foreach (var entry in context.ChangeTracker.Entries<IBelongsToCompany>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CompanyId == 0)
                {
                    entry.Entity.CompanyId = currentCompanyId;
                }
                else if (entry.Entity.CompanyId != currentCompanyId)
                {
                    if (enforceScope)
                        throw new InvalidOperationException($"Cannot add entity to CompanyId {entry.Entity.CompanyId}, current context is CompanyId {currentCompanyId}");
                    else
                        _logger.LogWarning("Adding entity to CompanyId {EntityCompanyId}, current context is CompanyId {CurrentCompanyId}", entry.Entity.CompanyId, currentCompanyId);
                }
            }
        }
    }
}
```

---

### Layer 5: Data Storage Layer

**SQLite Database** (`app.db`)

**Characteristics:**
- **Single-file database:** Entire database in one file (portable, easy backup)
- **Embedded:** No separate database server process
- **ACID compliant:** Full transactional support
- **File size:** ~50-200 MB typical (1000 users, 100k shift assignments)
- **Location:** Application root directory

**Schema Management:**
- **Code-First Migrations:** Schema defined in C# (EF Core)
- **Migration files:** 36 migrations tracking schema evolution
- **Apply migrations:** `dotnet ef database update` (automated in deployment)

**Performance Optimization:**
- **Indexes:** Critical indexes on CompanyId, WorkDate, UserId, foreign keys
- **Connection pooling:** EF Core manages connection lifecycle
- **Read-mostly workload:** SQLite excels at read-heavy scenarios
- **Write serialization:** Single-writer limitation acceptable (short transactions)

---

## Project Structure

### Solution File Structure

```
ShiftManager.sln
├── ShiftManager/                      # Main application project
│   ├── Controllers/                   # MVC API controllers
│   ├── Data/                          # DbContext, migrations
│   ├── Middleware/                    # Custom middleware (6 files)
│   ├── Models/                        # Domain entities (28 entities)
│   │   ├── DTOs/                      # Data transfer objects
│   │   └── Support/                   # Enums, interfaces
│   ├── Pages/                         # Razor Pages (66 pages)
│   │   └── Shared/                    # Layouts, partials, components
│   ├── Resources/                     # Localization .resx files
│   ├── Services/                      # Business logic services (40+)
│   ├── ViewComponents/                # View Components (4 files)
│   ├── wwwroot/                       # Static assets
│   │   ├── css/                       # Stylesheets (3 files, 5,375 lines)
│   │   ├── js/                        # JavaScript (6 files, 2,700+ lines)
│   │   ├── feedback/                  # User feedback screenshots
│   │   └── avatars/                   # User avatar images
│   ├── appsettings.json               # Configuration
│   ├── appsettings.Development.json   # Dev overrides
│   ├── appsettings.Production.json    # Prod config
│   ├── Program.cs                     # Application entry point (436 lines)
│   └── ShiftManager.csproj            # Project file
└── ShiftManager.Tests/                # Test project
    ├── Unit/                          # Unit tests
    ├── Integration/                   # Integration tests
    └── ShiftManager.Tests.csproj      # Test project file
```

### File Naming Conventions

**Controllers:** `{Entity}ApiController.cs` (e.g., `UsersApiController.cs`)
**Services:** `{Entity}Service.cs` (e.g., `NotificationService.cs`)
**Models:** `{Entity}.cs` (e.g., `AppUser.cs`, `ShiftInstance.cs`)
**DTOs:** `{Action}{Entity}Dto.cs` (e.g., `CreateUserDto.cs`)
**Razor Pages:** `{PageName}.cshtml` + `{PageName}.cshtml.cs` (e.g., `Month.cshtml` + `Month.cshtml.cs`)
**View Components:** `{Component}ViewComponent.cs` (e.g., `BreadcrumbViewComponent.cs`)
**Middleware:** `{Purpose}Middleware.cs` (e.g., `ApiAuthenticationMiddleware.cs`)

### Namespace Organization

```
ShiftManager
├── Controllers.Api                    # API controllers
├── Data                               # Data access
├── Middleware                         # Custom middleware
├── Models                             # Domain entities
│   ├── DTOs                           # Data transfer objects
│   └── Support                        # Shared types (enums, interfaces)
├── Pages                              # Razor Pages (no namespace, convention-based)
├── Services.Business                  # Business logic
├── Services.MultiTenancy              # Multi-tenancy
├── Services.UserManagement            # User management
├── Services.Caching                   # Caching services
├── Services.Infrastructure            # Infrastructure services
├── Services.ApiLayer                  # API services
├── Services.BackgroundJobs            # Hosted services
└── ViewComponents                     # View Components
```

---

## Dependency Injection Philosophy

### DI Container Registration

**Location:** `Program.cs` lines 45-162

**Service Lifetimes:**
1. **Singleton:** Single instance for application lifetime (RateLimitingService, ValidationService)
2. **Scoped:** One instance per HTTP request (most services, DbContext)
3. **Transient:** New instance every time requested (rarely used, only for stateless utilities)

**Registration Strategy:**
```csharp
// Multi-Tenancy (Scoped - per-request tenant context)
services.AddHttpContextAccessor();
services.AddScoped<ILocalizationService, LocalizationService>();
services.AddScoped<ITenantResolver, TenantResolver>();
services.AddScoped<ICompanyContext, CompanyContext>();
services.AddSingleton<CompanyIdInterceptor>();

// DbContext (Scoped - per-request)
services.AddDbContext<AppDbContext>((serviceProvider, opt) => {
    var interceptor = serviceProvider.GetRequiredService<CompanyIdInterceptor>();
    opt.UseSqlite(ConnectionString)
       .EnableDetailedErrors()
       .EnableSensitiveDataLogging(IsDevelopment)
       .AddInterceptors(interceptor);
});

// Business Logic Services (Scoped)
services.AddScoped<IShiftAssignmentService, ShiftAssignmentService>();
services.AddScoped<INotificationService, NotificationService>();
services.AddScoped<IDirectorService, DirectorService>();
// ... (35+ more scoped services)

// Caching Services (Scoped - cache is IMemoryCache which is Singleton)
services.AddScoped<IShiftTypeCacheService, ShiftTypeCacheService>();
services.AddScoped<IAppConfigCacheService, AppConfigCacheService>();
services.AddScoped<ICompanyCacheService, CompanyCacheService>();

// Infrastructure Services
services.AddHttpClient<IMailService, MailService>();  // Transient with HttpClientFactory
services.AddScoped<IEncryptionService, EncryptionService>();
services.AddScoped<IGriffinService, GriffinService>();

// API Services (Scoped)
services.AddScoped<IApiKeyService, ApiKeyService>();
services.AddSingleton<IRateLimitingService, RateLimitingService>();
services.AddSingleton<IValidationService, ValidationService>();
services.AddScoped<ISecurityLogger, SecurityLogger>();

// Background Services (Singleton Hosted Service)
services.AddHostedService<DailyNotificationJob>();

// Memory Cache (Singleton)
services.AddMemoryCache();
```

### Why Scoped Lifetime for Most Services?

**Rationale:**
- **DbContext is Scoped:** Most services depend on DbContext, must be same lifetime
- **Multi-Tenancy Context:** CompanyContext is scoped (per-request), services depend on it
- **Thread Safety:** Each HTTP request gets isolated service instances, no concurrency issues
- **Dispose Pattern:** Scoped services auto-disposed at end of request

**Exceptions:**
- **Singleton:** Services with no state, shared caches (RateLimitingService uses ConcurrentDictionary)
- **Transient:** HttpClient-based services (MailService) use HttpClientFactory (transient with singleton HttpClient)

---

## Cross-Cutting Concerns

### 1. Logging

**Framework:** ASP.NET Core Logging Abstractions
**Providers:** Console (Development), Debug (Development), File (Production - via 3rd party)

**Configuration:**
```csharp
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
```

**Usage:**
```csharp
public class NotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public async Task CreateNotificationAsync(int userId, NotificationType type, string message)
    {
        _logger.LogInformation("Creating notification for user {UserId}, type {Type}", userId, type);
        // Implementation
        _logger.LogDebug("Notification created: {NotificationId}", notificationId);
    }
}
```

**Log Levels:**
- **Trace:** Detailed debugging (rarely used)
- **Debug:** Development diagnostics
- **Information:** General application flow (production)
- **Warning:** Unexpected behavior, not errors
- **Error:** Exceptions, failures
- **Critical:** Application crash scenarios

---

### 2. Exception Handling

**Global Exception Handling:**
```csharp
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();  // Detailed error page with stack trace
}
else
{
    app.UseExceptionHandler("/Error"); // User-friendly error page
    app.UseHsts();                     // Enforce HTTPS
}
```

**Custom Exception Types:**
```csharp
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
}
```

**Service Layer Exception Handling:**
```csharp
public async Task<ShiftInstance> GetShiftInstanceAsync(int id)
{
    var shift = await _db.ShiftInstances.FindAsync(id);
    if (shift == null)
        throw new NotFoundException($"Shift instance {id} not found");
    return shift;
}
```

**API Controller Exception Handling:**
```csharp
[HttpGet("{id}")]
public async Task<IActionResult> GetShiftByIdAsync(int id)
{
    try
    {
        var shift = await _shiftApiService.GetShiftByIdAsync(id);
        return Ok(shift);
    }
    catch (NotFoundException)
    {
        return NotFound();
    }
    catch (UnauthorizedException)
    {
        return Forbid();
    }
}
```

---

### 3. Security

**Handled in Multiple Layers:**

**Security Headers Middleware:**
```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline';";
    context.Response.Headers.Remove("Server");
    context.Response.Headers.Remove("X-Powered-By");
    context.Response.Headers.Remove("X-AspNet-Version");
    await next();
});
```

**Authentication Middleware:**
- Cookie-based authentication for browser users
- API key authentication for REST API clients

**Authorization Middleware:**
- Role-based access control (6 roles)
- Policy-based authorization (8 policies)

**Anti-Forgery Tokens:**
- Automatic CSRF protection on POST forms
- `@Html.AntiForgeryToken()` in Razor Pages
- `[IgnoreAntiforgeryToken]` for JSON API endpoints

---

### 4. Caching

**Caching Strategy:** Service-level in-memory caching (IMemoryCache)

**Cached Data:**
- ShiftTypes (rarely change)
- Company metadata (rarely change)
- AppConfig key-value pairs (rarely change)

**Example:**
```csharp
public class ShiftTypeCacheService : IShiftTypeCacheService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ITenantResolver _tenantResolver;

    public async Task<List<ShiftType>> GetShiftTypesAsync()
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        var cacheKey = $"ShiftTypes_{companyId}";

        if (!_cache.TryGetValue(cacheKey, out List<ShiftType> shiftTypes))
        {
            shiftTypes = await _db.ShiftTypes
                .Where(st => st.CompanyId == companyId)
                .OrderBy(st => st.SortOrder)
                .ToListAsync();

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

            _cache.Set(cacheKey, shiftTypes, cacheOptions);
        }

        return shiftTypes;
    }

    public void InvalidateCache(int companyId)
    {
        _cache.Remove($"ShiftTypes_{companyId}");
    }
}
```

**Cache Invalidation:**
- Manual invalidation on create/update/delete operations
- Time-based expiration (5-minute TTL)
- Scoped by tenant (CompanyId) to prevent cross-tenant data leakage

---

### 5. Localization

**Framework:** ASP.NET Core Localization
**Supported Cultures:** en-US (English), he-IL (Hebrew)

**Configuration:**
```csharp
services.AddLocalization(options => options.ResourcesPath = "Resources");

services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("en-US"), new CultureInfo("he-IL") };
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

app.UseRequestLocalization();
```

**Resource Files:**
- `Resources/SharedResources.resx` (English)
- `Resources/SharedResources.he-IL.resx` (Hebrew)

**Usage:**
```csharp
public class MonthModel : LocalizedPageModel
{
    public string PageTitle => Localizer["Calendar_MyCalendar"];
}
```

**RTL Support:**
- Conditional CSS loading (`rtl.css` for Hebrew)
- `html[dir="rtl"]` attribute set based on culture
- Comprehensive CSS overrides (sidebar flip, text alignment)

---

### 6. Multi-Tenancy

**Implementation:** Row-level security via EF Core global query filters

**Key Components:**
1. **ITenantResolver:** Resolves current tenant from HttpContext.User
2. **ICompanyContext:** Stores current company ID for request
3. **CompanyIdInterceptor:** Automatically sets CompanyId on SaveChanges
4. **Global Query Filters:** Automatically filter by CompanyId on all queries

**Flow:**
```
HTTP Request → CompanyContextMiddleware → TenantResolver → CompanyContext
                                                                  ↓
                                                         DbContext (Query Filter)
```

**Detailed Coverage:** See [05-MULTI-TENANCY-DEEP-DIVE.md](05-MULTI-TENANCY-DEEP-DIVE.md)

---

## Deployment Architecture

### Self-Contained Deployment

**Build Command:**
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

**Output:**
- **Folder:** `bin/Release/net8.0/win-x64/publish/`
- **File Count:** 378 files
- **Total Size:** ~111 MB
- **Contents:**
  - ShiftManager.exe (entry point)
  - ShiftManager.dll (application assembly)
  - All .NET 8.0 runtime DLLs (coreclr, System.*, Microsoft.*)
  - SQLite native library (e_sqlite3.dll)
  - Static assets (wwwroot folder)
  - Localization resources (he-IL folder)

**Benefits:**
- ✅ No .NET SDK required on target machine
- ✅ No shared framework dependencies
- ✅ Predictable deployment (includes exact runtime version)
- ✅ Perfect for air-gapped environments (no runtime download)

**Tradeoffs:**
- ❌ Larger deployment size (~111 MB vs. ~10 MB framework-dependent)
- ❌ Must rebuild for different OS/architecture (acceptable, Windows-only target)

---

### Air-Gapped Deployment Process

**Detailed Coverage:** See [15-AIR-GAPPED-DEPLOYMENT.md](15-AIR-GAPPED-DEPLOYMENT.md)

**High-Level Steps:**
1. **Build on dev machine** (internet access)
2. **Run Build-Release.ps1** (creates deployment package)
3. **Copy to USB drive** (`ShiftManager-v{version}-win-x64.zip`)
4. **Transfer to air-gapped machine**
5. **Extract package**
6. **Run UNBLOCK_FILES.bat** (Windows security)
7. **Configure appsettings.Production.json**
8. **Run ShiftManager.exe**

**Helper Scripts Included:**
- `UNBLOCK_FILES.bat` - Unblock downloaded DLLs (PowerShell Unblock-File)
- `VERIFY_FILES.bat` - Verify package integrity
- `QUICK_FIX.bat` - Common troubleshooting
- `AIR_GAPPED_DEPLOYMENT_GUIDE.txt` - Deployment instructions

---

## Scalability Considerations

### Current Architecture (Monolithic)

**Designed For:**
- **User Scale:** 50 - 2,000 users per installation
- **Concurrent Users:** 10 - 100 simultaneous users
- **Request Rate:** ~10 requests/second (peak)
- **Data Volume:** 10,000 - 100,000 shift assignments per year

**Vertical Scaling:**
- ✅ Increase server CPU/RAM (primary scaling strategy)
- ✅ SQLite handles up to ~1TB database size (far exceeds needs)
- ✅ .NET 8.0 Kestrel web server handles 10k+ requests/second on modern hardware

**Bottlenecks (Theoretical):**
- **SQLite Write Concurrency:** Single-writer limitation (acceptable for read-heavy workload)
- **Memory:** IMemoryCache grows with cached data (limited by RAM)
- **CPU:** Complex LINQ queries on large datasets (mitigated by indexes, caching)

### Horizontal Scaling (If Needed)

**Not Implemented, But Possible:**
1. **Database Migration:** SQLite → PostgreSQL/SQL Server (multi-writer support)
2. **Distributed Cache:** IMemoryCache → Redis (shared cache across instances)
3. **Load Balancer:** Multiple app instances behind nginx/IIS ARR
4. **Session State:** Cookie-based auth already stateless (no sticky sessions needed)

**Why Not Implemented:**
- ❌ Target market doesn't need horizontal scaling (single-server workload)
- ❌ Adds deployment complexity (multiple servers in air-gapped environment)
- ❌ SQLite sufficient for 1000+ users with proper indexing

---

## Security Architecture

### Defense in Depth

**Layer 1: Network Security**
- Air-gapped deployment (no internet access)
- Firewall rules (only allow port 5000 inbound)
- VPN access only (if networked)

**Layer 2: Transport Security**
- HTTPS optional (configured via `appsettings.json`)
- Self-signed certificates acceptable in air-gapped environment

**Layer 3: Authentication**
- PBKDF2 password hashing (100k iterations, SHA256)
- Cookie-based authentication (HttpOnly, SameSite, Secure)
- Griffin ADFS integration (air-gapped SSO)

**Layer 4: Authorization**
- Role-based access control (6 roles)
- Policy-based authorization (8 policies)
- Resource-based authorization (user can only edit own profile)

**Layer 5: Multi-Tenancy Isolation**
- Row-level security (CompanyId filtering)
- Global query filters (impossible to forget WHERE clause)
- CompanyIdInterceptor (automatic scoping)

**Layer 6: Input Validation**
- Model validation (DataAnnotations)
- Anti-forgery tokens (CSRF protection)
- SQL injection prevention (EF Core parameterized queries)
- XSS prevention (Razor HTML encoding)

**Layer 7: Audit Logging**
- Comprehensive audit trail (AuditLog table)
- Denormalized user data (survives user deletion)
- IP address, User-Agent tracking

**Layer 8: Secure Configuration**
- Encrypted API keys (EncryptionService using Data Protection)
- Secrets in environment variables (not appsettings.json)
- SEED_ADMIN_PASSWORD required in production

---

## Performance Architecture

### Performance Targets

**Page Load Time:**
- ✅ Target: <2 seconds (95th percentile)
- ✅ Achieved: ~500ms typical (server-side rendering + caching)

**API Response Time:**
- ✅ Target: <200ms (simple queries)
- ✅ Target: <1 second (complex analytics queries)

**Database Query Performance:**
- ✅ Critical queries (calendar month view) optimized with composite indexes
- ✅ Query complexity: O(n) where n = days in month (30), constant for users

### Performance Optimizations

**1. Server-Side Rendering (Razor Pages)**
- Faster time-to-interactive vs. SPA frameworks
- No large JavaScript bundles to download
- HTML generated on server, streamed to browser

**2. Caching Strategy**
- Service-level caching (ShiftTypes, Companies, AppConfig)
- 5-minute TTL (balance freshness vs. performance)
- Scoped by tenant (cache key includes CompanyId)

**3. Database Indexes**
- Composite index on `ShiftInstances(CompanyId, WorkDate)` - **critical**
- Foreign key indexes on all FK columns
- Unique indexes on email, slug, keyHash

**4. Async/Await Patterns**
- All I/O operations async (database, HTTP, file system)
- Non-blocking I/O maximizes thread pool efficiency
- Kestrel async pipeline

**5. EF Core Optimizations**
- `.AsNoTracking()` for read-only queries
- `.Include()` / `.ThenInclude()` for eager loading (avoid N+1 queries)
- Projection (`.Select()`) to fetch only needed columns
- Compiled queries for hot paths (not yet implemented, future optimization)

**6. Static Asset Optimization**
- Browser caching (`Cache-Control: public, must-revalidate, max-age=0`)
- Content hashing (`asp-append-version="true"`)
- No minification/bundling (acceptable, files already small)

---

## Data Flow Patterns

### Read Flow (Calendar Month View)

```
User clicks "Calendar" →
  Browser GET /Calendar/Month?year=2025&month=12 →
    ASP.NET Core Routing →
      CompanyContextMiddleware (set tenant context) →
        MonthModel.OnGetAsync() →
          ShiftTypeCacheService.GetShiftTypesAsync() → [Cache Hit] → Return cached
          DbContext.ShiftInstances.Where(CompanyId=1, WorkDate BETWEEN ...) → [Query Filter Applied] →
            SQLite Query (uses IX_ShiftInstances_CompanyId_WorkDate index) →
              Return List<ShiftInstance> →
                Razor Page renders HTML →
                  Browser receives HTML →
                    JavaScript enhances UI (dark mode, tooltips) →
                      User sees calendar
```

**Performance:** ~50ms database query + ~20ms rendering = ~70ms total

---

### Write Flow (Create Shift Assignment)

```
User assigns shift →
  Browser POST /Calendar/Table?handler=Assign →
    ASP.NET Core Routing →
      Anti-Forgery Validation →
        CompanyContextMiddleware (set tenant context) →
          TableModel.OnPostAssignAsync() →
            ShiftAssignmentService.ValidateShiftAssignmentAsync() → [Validate no overlaps] →
              ShiftAssignmentService.AssignShiftAsync() →
                DbContext.ShiftAssignments.Add(assignment) →
                  CompanyIdInterceptor.SavingChangesAsync() → [Auto-set CompanyId] →
                    DbContext.SaveChangesAsync() →
                      SQLite INSERT →
                        NotificationService.NotifyShiftAssignedAsync() →
                          DbContext.UserNotifications.Add(notification) →
                            DbContext.SaveChangesAsync() →
                              SQLite INSERT →
                                Return Success →
                                  Redirect to Calendar →
                                    Browser receives 302 Redirect →
                                      User sees updated calendar
```

**Performance:** ~15ms validation + ~10ms insert + ~5ms notification = ~30ms total

---

## Design Patterns Used

### Architectural Patterns

1. **Layered Architecture:** Presentation → Service → Data Access → Storage
2. **Service Layer Pattern:** Business logic encapsulated in services
3. **Repository Pattern (Implicit):** EF Core DbContext serves as repository
4. **Unit of Work Pattern (Implicit):** EF Core DbContext tracks changes, SaveChanges commits

### Creational Patterns

5. **Dependency Injection:** Constructor injection throughout application
6. **Factory Pattern:** HttpClientFactory for MailService
7. **Singleton Pattern:** RateLimitingService (thread-safe ConcurrentDictionary)

### Structural Patterns

8. **Adapter Pattern:** GriffinService adapts external ADFS to internal auth model
9. **Decorator Pattern:** CompanyIdInterceptor decorates EF Core SaveChanges
10. **Facade Pattern:** AnalyticsService provides simple API over complex queries

### Behavioral Patterns

11. **Strategy Pattern:** ShiftAssignmentService uses different validation strategies for shift types (OFFLINE/HOME exempt from some checks)
12. **Observer Pattern:** NotificationService notifies users on events
13. **Template Method Pattern:** LocalizedPageModel base class for Razor Pages

### Concurrency Patterns

14. **Optimistic Concurrency:** ShiftInstance.Concurrency token prevents lost updates
15. **Async/Await Pattern:** Non-blocking I/O throughout

---

## Why NOT Microservices

### Microservices Considered and Rejected

**Hypothetical Microservice Architecture:**
- **User Service:** User management, authentication
- **Shift Service:** Shift scheduling, conflicts
- **Notification Service:** Notifications, email
- **API Gateway:** Aggregate services

**Why Rejected:**

**1. Deployment Complexity**
- ❌ Multiple services → multiple deployments
- ❌ Service discovery (Consul, Eureka) adds complexity
- ❌ Air-gapped environment → manual service registration

**2. Operational Overhead**
- ❌ Multiple databases (or shared DB defeats purpose)
- ❌ Distributed logging (Elasticsearch, Splunk)
- ❌ Distributed tracing (Jaeger, Zipkin)
- ❌ All add dependencies incompatible with air-gapped deployment

**3. Latency**
- ❌ Network calls between services (1-10ms per hop)
- ❌ In-process method calls (nanoseconds)
- ❌ Unnecessary latency for single-server workload

**4. Transactional Consistency**
- ❌ Distributed transactions (2-phase commit, Saga pattern) complex
- ❌ Eventual consistency harder to reason about
- ❌ ACID guarantees easier in monolith

**5. Testing Complexity**
- ❌ Integration testing requires all services running
- ❌ Contract testing (Pact, etc.) adds overhead
- ❌ Monolith: single debug session, step through entire flow

### When Microservices WOULD Make Sense

**Scenarios (Not Applicable to ShiftManager):**
- Independent scaling needs (e.g., notification service needs 10x instances)
- Independent deployment schedules (different teams, different release cycles)
- Polyglot requirements (services in different languages)
- Massive scale (millions of users, distributed globally)

**Conclusion:** Monolithic architecture is **correct choice** for ShiftManager's requirements.

---

## Summary

ShiftManager's architecture is a **well-designed monolithic application** optimized for:
- **Air-gapped deployment** (zero external dependencies)
- **Operational simplicity** (single deployment unit)
- **Multi-tenancy** (row-level security, complete data isolation)
- **Performance** (server-side rendering, caching, optimized indexes)
- **Security** (defense in depth, audit logging, RBAC)
- **Maintainability** (service-oriented design, dependency injection, testability)

**Technology Choices:**
- ✅ .NET 8.0 (mature, high-performance, Windows-friendly)
- ✅ Razor Pages (simple, server-side rendering, no npm)
- ✅ SQLite (zero-config, single-file, air-gapped perfect)
- ✅ Vanilla JS/CSS (no dependencies, timeless)
- ✅ EF Core (powerful ORM, code-first migrations)

**Architectural Patterns:**
- ✅ Layered monolith (clear separation of concerns)
- ✅ Service-oriented design (testable, maintainable)
- ✅ Multi-tenancy at database level (secure, efficient)

**The Result:** A production-ready, enterprise-grade shift scheduling system that solves real problems for users in high-security environments.

---

**Next Steps:**
- [➡️ Read Database Schema](03-DATABASE-SCHEMA.md) for complete data model
- [➡️ Read Startup & Middleware](04-STARTUP-AND-MIDDLEWARE.md) for application bootstrap
- [➡️ Read Multi-Tenancy Deep Dive](05-MULTI-TENANCY-DEEP-DIVE.md) for tenant isolation
- [➡️ Read Design Decisions](18-DESIGN-DECISIONS-AND-TRADEOFFS.md) for architectural rationale
- [⬅️ Return to Index](00-INDEX.md) for full documentation map

---

**Document Status:** ✅ Complete
**Cross-References:** [architecture-overview.mmd](diagrams/architecture-overview.mmd), [03-DATABASE-SCHEMA.md](03-DATABASE-SCHEMA.md), [18-DESIGN-DECISIONS-AND-TRADEOFFS.md](18-DESIGN-DECISIONS-AND-TRADEOFFS.md)
