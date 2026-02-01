# ShiftManager Change Playbook

**Generated:** 2026-01-12
**Agent:** Antigravity Workspace Intake

---

## Quick Reference Commands

### Development Commands
```powershell
# Run application (development mode)
dotnet run

# Run with hot-reload
dotnet watch

# Build project
dotnet build

# Restore dependencies
dotnet restore

# Run tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Database Commands
```powershell
# Create new migration
dotnet ef migrations add <MigrationName>

# Apply pending migrations
dotnet ef database update

# Rollback to specific migration
dotnet ef database update <MigrationName>

# Drop database
dotnet ef database drop --force

# Generate SQL script for migration
dotnet ef migrations script <FromMigration> <ToMigration>
```

### Build & Deploy Commands
```powershell
# Build release package
.\Build-Release.ps1 -Version "2.0.0"

# Verify deployment files
.\VERIFY_FILES.bat

# Unblock DLLs after USB transfer
.\UNBLOCK_FILES.bat
```

---

## Common Change Patterns

### 1. Adding a New Entity

**Steps:**
1. Create model in `Models/` directory
2. Add DbSet to `Data/AppDbContext.cs`
3. Configure relationships and indexes in `OnModelCreating()`
4. Add query filter if tenant-scoped
5. Create migration
6. Update services as needed

**Example: Adding a new entity**
```csharp
// 1. Models/NewEntity.cs
public class NewEntity : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public Company? Company { get; set; }
}

// 2. Data/AppDbContext.cs - Add DbSet
public DbSet<NewEntity> NewEntities => Set<NewEntity>();

// 3. Data/AppDbContext.cs - In OnModelCreating()
modelBuilder.Entity<NewEntity>()
    .HasIndex(e => new { e.CompanyId, e.Name });

modelBuilder.Entity<NewEntity>()
    .HasOne(e => e.Company)
    .WithMany()
    .HasForeignKey(e => e.CompanyId)
    .OnDelete(DeleteBehavior.Restrict);

// Add query filter for tenant scoping
if (_tenantResolver != null)
{
    modelBuilder.Entity<NewEntity>()
        .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());
}

// 4. Create migration
// dotnet ef migrations add AddNewEntity
```

**Blast Radius:** Low-Medium (database schema change)
**Rollback:** Apply rollback migration script

---

### 2. Adding a New Service

**Steps:**
1. Create interface in `Services/INewService.cs`
2. Create implementation in `Services/NewService.cs`
3. Register in `Program.cs`
4. Inject where needed

**Example:**
```csharp
// 1. Services/INewService.cs
public interface INewService
{
    Task<NewEntity?> GetByIdAsync(int id);
    Task<NewEntity> CreateAsync(string name);
}

// 2. Services/NewService.cs
public class NewService : INewService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<NewService> _logger;
    
    public NewService(AppDbContext db, ITenantResolver tenantResolver, ILogger<NewService> logger)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _logger = logger;
    }
    
    public async Task<NewEntity?> GetByIdAsync(int id)
    {
        return await _db.NewEntities.FindAsync(id);
    }
    
    public async Task<NewEntity> CreateAsync(string name)
    {
        var entity = new NewEntity { Name = name };
        _db.NewEntities.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }
}

// 3. Program.cs - Add registration (after line ~150)
builder.Services.AddScoped<INewService, NewService>();
```

**Blast Radius:** Low (additive change)
**Rollback:** Remove registration and files

---

### 3. Adding a New Razor Page

**Steps:**
1. Create `.cshtml` file in appropriate `Pages/` subdirectory
2. Create `.cshtml.cs` page model
3. Add authorization if needed
4. Add navigation link if needed
5. Add localization strings

**Example:**
```csharp
// Pages/Admin/NewPage.cshtml.cs
[Authorize(Policy = "IsManagerOrAdmin")]
public class NewPageModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly INewService _newService;
    
    public NewPageModel(AppDbContext db, INewService newService)
    {
        _db = db;
        _newService = newService;
    }
    
    public List<NewEntity> Items { get; set; } = new();
    
    public async Task OnGetAsync()
    {
        Items = await _db.NewEntities.ToListAsync();
    }
}
```

```html
<!-- Pages/Admin/NewPage.cshtml -->
@page
@model ShiftManager.Pages.Admin.NewPageModel
@{
    ViewData["Title"] = "New Page";
}

<h1>@ViewData["Title"]</h1>

<!-- Page content -->
```

**Blast Radius:** Low (additive change)
**Rollback:** Delete page files

---

### 4. Adding a New API Endpoint

**Steps:**
1. Add method to existing controller OR create new controller
2. Define route, HTTP method, authorization
3. Implement logic (use services)
4. Add feature flag if needed
5. Update API documentation

**Example: Adding to existing controller**
```csharp
// Controllers/Api/V1/NewController.cs
[ApiController]
[Route("api/v1/new-entities")]
public class NewEntitiesController : ControllerBase
{
    private readonly INewService _service;
    private readonly IConfiguration _config;
    
    public NewEntitiesController(INewService service, IConfiguration config)
    {
        _service = service;
        _config = config;
    }
    
    [HttpGet]
    public async Task<IActionResult> List()
    {
        // Check feature flag
        if (!_config.GetValue<bool>("Features:Api:NewEntities:ListEnabled", true))
            return NotFound();
        
        var items = await _service.GetAllAsync();
        return Ok(new { data = items });
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item == null)
            return NotFound();
        return Ok(item);
    }
}
```

**Feature Flag (add to appsettings.json):**
```json
"Features": {
    "Api": {
        "NewEntities": {
            "ListEnabled": true,
            "GetEnabled": true
        }
    }
}
```

**Blast Radius:** Low-Medium (new endpoint, needs testing)
**Rollback:** Disable feature flag or remove controller

---

### 5. Modifying Database Schema

**Steps:**
1. Modify entity model
2. Update `AppDbContext.OnModelCreating()` if needed
3. Create migration
4. Test migration up/down locally
5. Create rollback script
6. Apply migration

**Example: Adding column to existing table**
```csharp
// 1. Modify model (Models/AppUser.cs)
public class AppUser
{
    // ... existing fields ...
    
    // New field
    public string? NewField { get; set; }
}

// 2. Create migration
// dotnet ef migrations add AddNewFieldToAppUser

// 3. Test locally
// dotnet ef database update
// dotnet ef database update <PreviousMigration>  # Rollback test
// dotnet ef database update  # Re-apply

// 4. Create rollback script (Migrations/rollback/AddNewFieldToAppUser_rollback.sql)
ALTER TABLE Users DROP COLUMN NewField;
```

**Blast Radius:** Medium-High (database schema change)
**Rollback:** Apply rollback migration or script

⚠️ **Warning:** Some migrations contain PRAGMA operations that are non-transactional in SQLite. Always backup before applying.

---

### 6. Adding Localization Strings

**Steps:**
1. Add key to `Resources/SharedResources.resx` (English)
2. Add translation to `Resources/SharedResources.he-IL.resx` (Hebrew)
3. Use in code with `IStringLocalizer` or `<loc>` tag helper

**Example:**
```xml
<!-- Resources/SharedResources.resx -->
<data name="NewFeature_Title" xml:space="preserve">
    <value>New Feature</value>
</data>

<!-- Resources/SharedResources.he-IL.resx -->
<data name="NewFeature_Title" xml:space="preserve">
    <value>תכונה חדשה</value>
</data>
```

**Usage in Razor:**
```html
@inject IStringLocalizer<SharedResources> Localizer
<h1>@Localizer["NewFeature_Title"]</h1>
```

**Or with tag helper:**
```html
<loc key="NewFeature_Title" />
```

**Blast Radius:** Low (additive)
**Rollback:** Remove keys from both resource files

---

### 7. Adding Authorization Policy

**Steps:**
1. Define policy in `Program.cs`
2. Apply with `[Authorize(Policy = "...")]`
3. Update documentation

**Example:**
```csharp
// Program.cs - Add to authorization options (around line 92)
builder.Services.AddAuthorization(options =>
{
    // ... existing policies ...
    
    options.AddPolicy("CanManageNewFeature",
        policy => policy.RequireRole(
            nameof(UserRole.Manager), 
            nameof(UserRole.Owner), 
            nameof(UserRole.Director)));
});

// Apply to page
[Authorize(Policy = "CanManageNewFeature")]
public class NewFeaturePageModel : PageModel { }
```

**Blast Radius:** Low (additive, but may lock out users if misconfigured)
**Rollback:** Remove policy or change to less restrictive

---

### 8. Adding Background Service

**Steps:**
1. Create hosted service class
2. Implement `IHostedService` or inherit `BackgroundService`
3. Register in `Program.cs`

**Example:**
```csharp
// Services/NewBackgroundJob.cs
public class NewBackgroundJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<NewBackgroundJob> _logger;
    
    public NewBackgroundJob(IServiceProvider services, ILogger<NewBackgroundJob> logger)
    {
        _services = services;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Do work...
            _logger.LogInformation("Background job executed at {Time}", DateTime.UtcNow);
            
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

// Program.cs - Add registration
builder.Services.AddHostedService<NewBackgroundJob>();
```

**Blast Radius:** Medium (runs automatically, could affect performance)
**Rollback:** Remove registration

---

## Change Validation Checklist

### Before Making Changes
- [ ] Understand the feature/bug scope
- [ ] Identify affected files and services
- [ ] Check for multi-tenant implications
- [ ] Review related tests
- [ ] Backup database if schema change

### During Development
- [ ] Follow existing code conventions
- [ ] Add XML documentation
- [ ] Handle errors gracefully
- [ ] Log appropriately
- [ ] Consider localization (both languages)

### After Making Changes
- [ ] Run all tests: `dotnet test`
- [ ] Verify build: `dotnet build`
- [ ] Test manually in browser
- [ ] Check multi-tenant isolation
- [ ] Verify authorization works
- [ ] Test in both English and Hebrew
- [ ] Update documentation if needed

### Before Deployment
- [ ] Create migration rollback script
- [ ] Test migration on copy of production database
- [ ] Update version number if applicable
- [ ] Generate release package
- [ ] Verify package contents

---

## Blast Radius Assessment

| Change Type | Blast Radius | Key Concerns |
|-------------|--------------|--------------|
| New Entity | Medium | Schema migration, query filters |
| New Service | Low | DI registration only |
| New Razor Page | Low | No database changes |
| New API Endpoint | Low-Medium | Security, rate limiting |
| Schema Change | High | Data migration, rollback |
| Authorization Change | Medium | User access disruption |
| Background Job | Medium | Performance, resource usage |
| Multi-tenant Change | Critical | Data isolation |
| Query Filter Change | Critical | Cross-tenant data leak risk |

---

## Emergency Rollback Procedures

### Application Rollback
1. Stop the application
2. Restore previous application files
3. Restart the application

### Database Rollback (Schema)
```powershell
# Option 1: Use EF Core
dotnet ef database update <PreviousMigrationName>

# Option 2: Use rollback script
sqlite3 app.db < Migrations/rollback/<MigrationName>_rollback.sql
```

### Database Rollback (Data)
1. Stop the application
2. Restore from backup: `Copy-Item .\Backups\app.db.bak .\app.db`
3. Restart the application

### Configuration Rollback
1. Edit `appsettings.json`
2. Revert changes
3. Restart application (changes take effect on restart)

---

## Critical Files to Never Edit Carelessly

| File | Reason | Risk Level |
|------|--------|------------|
| `Program.cs` | Application bootstrap | 🔴 Critical |
| `Data/AppDbContext.cs` | All database access | 🔴 Critical |
| `Data/CompanyIdInterceptor.cs` | Multi-tenant isolation | 🔴 Critical |
| `Services/TenantResolver.cs` | Tenant resolution | 🔴 Critical |
| `Middleware/*` | Request pipeline | 🟡 High |
| `appsettings.json` | Application configuration | 🟡 High |
| `Models/PasswordHasher.cs` | Security | 🔴 Critical |
| Query filters in `AppDbContext.cs` | Data isolation | 🔴 Critical |

---

## Code Conventions

### Naming Conventions
- **Services:** `ISomethingService` / `SomethingService`
- **Controllers:** `SomethingController`
- **Pages:** Grouped by feature in subdirectories
- **Models:** Singular names (`AppUser`, not `AppUsers`)

### Code Style
- Use `async/await` for database operations
- Use `ILogger<T>` for logging
- Use constructor injection for dependencies
- Document public methods with XML comments
- Use nullable reference types (`string?`)

### Multi-Tenant Patterns
- Always use `IBelongsToCompany` for tenant-scoped entities
- Add query filter in `AppDbContext.OnModelCreating()`
- Use `IgnoreQueryFilters()` only when necessary (Directors, diagnostics)
- Validate company access in service layer

---

## Development Environment Setup

### Prerequisites
1. .NET SDK 8.0+
2. SQLite (included with .NET)
3. Visual Studio / VS Code / Rider

### Initial Setup
```powershell
cd c:\Users\katzi\Downloads\ShiftManager
.\setup.ps1
# OR manually:
Copy-Item seed.db app.db
dotnet restore
dotnet build
```

### First Run
```powershell
dotnet run
# Navigate to http://localhost:5000
# Login: admin@local / admin123
```

### Reset Database
```powershell
Remove-Item app.db, app.db-wal, app.db-shm -ErrorAction SilentlyContinue
Copy-Item seed.db app.db
```
