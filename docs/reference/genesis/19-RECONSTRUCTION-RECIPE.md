# 19. Reconstruction Recipe

**Document Version:** 1.0
**Last Updated:** December 2025
**Part of:** ShiftManager Genesis Documentation

---

## Table of Contents

1. [Introduction](#introduction)
2. [Prerequisites](#prerequisites)
3. [Phase 1: Project Foundation](#phase-1-project-foundation)
4. [Phase 2: Database Schema](#phase-2-database-schema)
5. [Phase 3: Multi-Tenancy Infrastructure](#phase-3-multi-tenancy-infrastructure)
6. [Phase 4: Authentication & Authorization](#phase-4-authentication--authorization)
7. [Phase 5: Service Layer](#phase-5-service-layer)
8. [Phase 6: UI Layer (Razor Pages)](#phase-6-ui-layer-razor-pages)
9. [Phase 7: Business Workflows](#phase-7-business-workflows)
10. [Phase 8: Localization & RTL](#phase-8-localization--rtl)
11. [Phase 9: API Layer & External Integration](#phase-9-api-layer--external-integration)
12. [Phase 10: Build Pipeline & Deployment](#phase-10-build-pipeline--deployment)
13. [Verification Steps](#verification-steps)
14. [Common Pitfalls & Solutions](#common-pitfalls--solutions)

---

## Introduction

This document provides a **step-by-step reconstruction recipe** for rebuilding ShiftManager from scratch using the genesis documentation.

**Audience:**
- Senior C# developer with ASP.NET Core experience
- Someone recreating ShiftManager after total code loss
- Team building a similar shift management system

**Time Estimate:**
- **Solo Developer:** 8-12 weeks (full-time)
- **Team of 3:** 4-6 weeks
- **With AI Assistance:** 6-8 weeks (solo)

**Prerequisites:**
- Read 00-INDEX.md (master navigation)
- Read 01-EXECUTIVE-OVERVIEW.md (understand business context)
- Read 02-ARCHITECTURE-BLUEPRINT.md (system design)

**Reconstruction Approach:**
1. Build foundation (database, auth, multi-tenancy)
2. Implement core workflows (shift assignment, time-off)
3. Add UI layer (Razor Pages)
4. Polish (localization, API, deployment)

---

## Prerequisites

### Development Environment

**Required Software:**
- **Windows 10/11** (target platform)
- **Visual Studio 2022** (Community Edition or higher) OR **VS Code + C# Dev Kit**
- **.NET 8.0 SDK** (LTS version) - Download from https://dot.net
- **Git** - Version control
- **SQLite Browser** (optional, for database inspection) - https://sqlitebrowser.org/

**Verify Installation:**
```bash
# Check .NET version
dotnet --version
# Expected: 8.0.x

# Check installed SDKs
dotnet --list-sdks
# Expected: 8.0.xxx [C:\Program Files\dotnet\sdk]
```

**Required Skills:**
- C# 12 (primary classes, records, pattern matching)
- ASP.NET Core 8.0 (Razor Pages, middleware, DI)
- Entity Framework Core 9.0 (migrations, LINQ, query filters)
- SQL basics (SELECT, JOIN, WHERE)
- HTML/CSS/JavaScript (vanilla, no frameworks)
- Git (branching, committing, merging)

**Reference Documentation:**
- `docs/genesis/02-ARCHITECTURE-BLUEPRINT.md` - Technology stack details
- `docs/genesis/04-STARTUP-AND-MIDDLEWARE.md` - Program.cs deep dive

---

## Phase 1: Project Foundation

**Time Estimate:** 2-3 hours

**Objective:** Create ASP.NET Core project with correct structure and dependencies.

### Step 1.1: Create Solution & Project

```bash
# Create solution directory
mkdir ShiftManager
cd ShiftManager

# Create ASP.NET Core Razor Pages project
dotnet new webapp -n ShiftManager -f net8.0

# Create solution file
dotnet new sln -n ShiftManager

# Add project to solution
dotnet sln add ShiftManager/ShiftManager.csproj

# Open in Visual Studio
start ShiftManager.sln
```

### Step 1.2: Add NuGet Packages

**Edit ShiftManager.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Database -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.9" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.9">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>

    <!-- Image Processing -->
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.5" />

    <!-- Localization -->
    <PackageReference Include="Microsoft.Extensions.Localization" Version="8.0.10" />
  </ItemGroup>
</Project>
```

**Restore Packages:**
```bash
dotnet restore
```

### Step 1.3: Create Directory Structure

```bash
# Create folders
mkdir Models
mkdir Models/Support
mkdir Data
mkdir Services
mkdir Middleware
mkdir Pages/Auth
mkdir Pages/Assignments
mkdir Pages/Requests
mkdir Pages/Reports
mkdir Pages/Admin
mkdir Pages/MyTeam
mkdir Pages/Chores
mkdir Pages/OnDuty
mkdir Pages/Game
mkdir wwwroot/css
mkdir wwwroot/js
mkdir wwwroot/images
mkdir wwwroot/feedback
```

**Reference:**
- `docs/genesis/02-ARCHITECTURE-BLUEPRINT.md` - Complete folder structure

### Step 1.4: Create appsettings.json

**appsettings.json:**
```json
{
  "ConnectionStrings": {
    "Default": "Data Source=ShiftManager.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "*",
  "EnableHttpsRedirection": false,
  "Features": {
    "EnableDirectorRole": true
  }
}
```

**appsettings.Production.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "EnableHttpsRedirection": true
}
```

**Verification:**
```bash
dotnet build
# Expected: Build succeeded. 0 Warning(s). 0 Error(s).
```

---

## Phase 2: Database Schema

**Time Estimate:** 1-2 days

**Objective:** Create complete database schema with 70+ entities (DbSets), relationships, and migrations.

### Step 2.1: Create Domain Models

**Create Models/Support/Enums.cs:**
```csharp
namespace ShiftManager.Models.Support;

public enum UserRole
{
    Owner = 0,
    Manager = 1,
    Employee = 2,
    Director = 3,
    Trainee = 4,
    Assigner = 5,
    AreaAdmin = 6
}

public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2
}

public enum NotificationType
{
    ShiftAdded = 0,
    ShiftRemoved = 1,
    TimeOffRequested = 2,
    TimeOffApproved = 3,
    TimeOffDeclined = 4,
    SwapRequested = 5,
    SwapApproved = 6,
    SwapDeclined = 7,
    ChoreAssigned = 8,
    OnDutyAssigned = 9,
    ReminderShiftTomorrow = 10
}

public enum RequestType
{
    TimeOff = 0,
    SwapShift = 1
}
```

**Create Models/Company.cs:**
```csharp
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

public class Company
{
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Slug { get; set; }

    [MaxLength(255)]
    public string? DisplayName { get; set; }

    // Navigation properties
    public List<AppUser> Users { get; set; } = new();
    public List<ShiftType> ShiftTypes { get; set; } = new();
}
```

**Create 27 more models following docs/genesis/06-DOMAIN-MODELS.md**

**Complete model list:**
1. Company
2. AppUser
3. ShiftType
4. ShiftAssignment
5. TimeOffRequest
6. SwapRequest
7. ChoreType
8. ChoreAssignment
9. OnDutyType
10. OnDutyAssignment
11. Notification
12. AppConfig
13. DirectorCompany
14. ApiKey
15. ApiRequestLog
16. RateLimitEntry
17. GriffinConfig
18. GameLeaderboard
19. FeedbackFile
20. CompanyFile
21. AuditLog
22. Session
23. EmailLog
24. NotificationSetting
25. Holiday
26. ShiftTemplate
27. RecurringChore
28. Theme

**Reference:**
- `docs/genesis/03-DATABASE-SCHEMA.md` - Complete schema with relationships
- `docs/genesis/06-DOMAIN-MODELS.md` - All 70+ entities explained

### Step 2.2: Create AppDbContext

**Data/AppDbContext.cs:**
```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data;

public class AppDbContext : DbContext
{
    private readonly ICompanyContext _companyContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICompanyContext companyContext)
        : base(options)
    {
        _companyContext = companyContext;
    }

    // DbSets (28 total)
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ShiftType> ShiftTypes => Set<ShiftType>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<TimeOffRequest> TimeOffRequests => Set<TimeOffRequest>();
    public DbSet<SwapRequest> SwapRequests => Set<SwapRequest>();
    public DbSet<ChoreType> ChoreTypes => Set<ChoreType>();
    public DbSet<ChoreAssignment> ChoreAssignments => Set<ChoreAssignment>();
    public DbSet<OnDutyType> OnDutyTypes => Set<OnDutyType>();
    public DbSet<OnDutyAssignment> OnDutyAssignments => Set<OnDutyAssignment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AppConfig> Configs => Set<AppConfig>();
    public DbSet<DirectorCompany> DirectorCompanies => Set<DirectorCompany>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiRequestLog> ApiRequestLogs => Set<ApiRequestLog>();
    public DbSet<RateLimitEntry> RateLimitEntries => Set<RateLimitEntry>();
    public DbSet<GriffinConfig> GriffinConfigs => Set<GriffinConfig>();
    public DbSet<GameLeaderboard> GameLeaderboard => Set<GameLeaderboard>();
    public DbSet<FeedbackFile> FeedbackFiles => Set<FeedbackFile>();
    public DbSet<CompanyFile> CompanyFiles => Set<CompanyFile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<ShiftTemplate> ShiftTemplates => Set<ShiftTemplate>();
    public DbSet<RecurringChore> RecurringChores => Set<RecurringChores>();
    public DbSet<Theme> Themes => Set<Theme>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global query filters (multi-tenancy)
        modelBuilder.Entity<AppUser>()
            .HasQueryFilter(u => u.CompanyId == _companyContext.GetCompanyId());
        modelBuilder.Entity<ShiftType>()
            .HasQueryFilter(st => st.CompanyId == _companyContext.GetCompanyId());
        // ... (add filters for all 26 company-scoped entities)

        // Relationships (see docs/genesis/03-DATABASE-SCHEMA.md for complete relationships)
        modelBuilder.Entity<ShiftAssignment>()
            .HasOne(sa => sa.User)
            .WithMany(u => u.ShiftAssignments)
            .HasForeignKey(sa => sa.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ... (configure all relationships)
    }
}
```

**Reference:**
- `docs/genesis/03-DATABASE-SCHEMA.md` - Complete ERD with relationships
- Source file: `Data/AppDbContext.cs:1-592`

### Step 2.3: Create Initial Migration

```bash
# Add migration
dotnet ef migrations add InitialCreate

# Apply migration (creates ShiftManager.db)
dotnet ef database update
```

**Verification:**
```bash
# Check database file exists
dir ShiftManager.db

# Open with SQLite Browser (optional)
sqlitebrowser ShiftManager.db
```

**Expected Tables:** 70+ entities created (Companies, Users, ShiftTypes, etc.)

**Reference:**
- `docs/genesis/12-DATA-MIGRATIONS.md` - All 36 migrations documented

---

## Phase 3: Multi-Tenancy Infrastructure

**Time Estimate:** 1 day

**Objective:** Implement row-level security with CompanyId scoping.

### Step 3.1: Create Multi-Tenancy Interfaces

**Data/ICompanyContext.cs:**
```csharp
namespace ShiftManager.Data;

public interface ICompanyContext
{
    int GetCompanyId();
    void SetCompanyId(int companyId);
}

public class CompanyContext : ICompanyContext
{
    private int _companyId;

    public int GetCompanyId() => _companyId;
    public void SetCompanyId(int companyId) => _companyId = companyId;
}
```

**Data/ITenantResolver.cs:**
```csharp
namespace ShiftManager.Data;

public interface ITenantResolver
{
    int ResolveCompanyId();
}

public class TenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AppDbContext _db;

    public TenantResolver(IHttpContextAccessor httpContextAccessor, AppDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    public int ResolveCompanyId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return 1; // Default company for anonymous users

        var userIdClaim = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out int userId))
        {
            var appUser = _db.Users.IgnoreQueryFilters().FirstOrDefault(u => u.Id == userId);
            return appUser?.CompanyId ?? 1;
        }

        return 1;
    }
}
```

**Reference:**
- `docs/genesis/05-MULTI-TENANCY-DEEP-DIVE.md` - Complete multi-tenancy architecture

### Step 3.2: Create CompanyIdInterceptor

**Data/CompanyIdInterceptor.cs:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ShiftManager.Data;

public interface IHasCompanyId
{
    int CompanyId { get; set; }
}

public class CompanyIdInterceptor : SaveChangesInterceptor
{
    private readonly ICompanyContext _companyContext;

    public CompanyIdInterceptor(ICompanyContext companyContext)
    {
        _companyContext = companyContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        SetCompanyId(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetCompanyId(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SetCompanyId(DbContext? context)
    {
        if (context == null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is IHasCompanyId entity)
            {
                if (entity.CompanyId == 0)
                {
                    entity.CompanyId = _companyContext.GetCompanyId();
                }
            }
        }
    }
}
```

**Update Models to Implement IHasCompanyId:**
```csharp
public class AppUser : IHasCompanyId
{
    public int Id { get; set; }
    public int CompanyId { get; set; } // ← Required for IHasCompanyId
    // ... other properties
}
```

**Reference:**
- Source file: `Data/CompanyIdInterceptor.cs:1-68`

### Step 3.3: Create CompanyContextMiddleware

**Middleware/CompanyContextMiddleware.cs:**
```csharp
using ShiftManager.Data;

namespace ShiftManager.Middleware;

public class CompanyContextMiddleware
{
    private readonly RequestDelegate _next;

    public CompanyContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantResolver tenantResolver, ICompanyContext companyContext)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var companyId = tenantResolver.ResolveCompanyId();
            companyContext.SetCompanyId(companyId);
        }

        await _next(context);
    }
}
```

**Reference:**
- `docs/genesis/05-MULTI-TENANCY-DEEP-DIVE.md` - Middleware flow

---

## Phase 4: Authentication & Authorization

**Time Estimate:** 2 days

**Objective:** Implement cookie authentication, role hierarchy, and Griffin ADFS integration.

### Step 4.1: Create PasswordHasher Service

**Services/PasswordHasher.cs:**
```csharp
using System.Security.Cryptography;

namespace ShiftManager.Services;

public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int HashSize = 32;
    private const int SaltSize = 32;

    public static (byte[] hash, byte[] salt) CreateHash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);

        byte[] hash = pbkdf2.GetBytes(HashSize);
        return (hash, salt);
    }

    public static bool VerifyPassword(string password, byte[] hash, byte[] salt)
    {
        var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        byte[] testHash = pbkdf2.GetBytes(HashSize);

        // Timing-safe comparison
        return CryptographicOperations.FixedTimeEquals(hash, testHash);
    }
}
```

**Reference:**
- `docs/genesis/10-AUTHENTICATION-AND-AUTHORIZATION.md` - Password hashing details

### Step 4.2: Configure Authentication in Program.cs

**Program.cs (Authentication Section):**
```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Auth/Login";
        opt.LogoutPath = "/Auth/Logout";
        opt.AccessDeniedPath = "/AccessDenied";
        opt.Cookie.Name = "shiftmgr.auth";
        opt.ExpireTimeSpan = TimeSpan.FromDays(7);
        opt.SlidingExpiration = true;
        opt.Cookie.HttpOnly = true;
        opt.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opt.Cookie.SameSite = SameSiteMode.Lax;
    });

// Add authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("IsManagerOrAdmin",
        policy => policy.RequireRole("Manager", "Owner", "Director"));
    options.AddPolicy("IsAdmin", policy => policy.RequireRole("Owner"));
    options.AddPolicy("IsDirector", policy => policy.RequireRole("Owner", "Director"));
    // ... (add 15 more policies, see docs/genesis/10-AUTHENTICATION-AND-AUTHORIZATION.md)
});

// Add Razor Pages with authorization
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Auth/Login");
    options.Conventions.AllowAnonymousToPage("/Auth/Signup");
});
```

**Reference:**
- `docs/genesis/04-STARTUP-AND-MIDDLEWARE.md` - Complete Program.cs (436 lines)
- `docs/genesis/10-AUTHENTICATION-AND-AUTHORIZATION.md` - All authorization policies

### Step 4.3: Create Login Page

**Pages/Auth/Login.cshtml.cs:**
```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Auth;

public class LoginModel : PageModel
{
    private readonly AppDbContext _db;

    public LoginModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Email and password are required.";
            return Page();
        }

        // Find user by email
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == Email && u.IsActive);

        if (user == null)
        {
            ErrorMessage = "Invalid credentials.";
            return Page();
        }

        // Verify password
        if (!PasswordHasher.VerifyPassword(Password, user.PasswordHash, user.PasswordSalt))
        {
            ErrorMessage = "Invalid credentials.";
            return Page();
        }

        // Create claims
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("CompanyId", user.CompanyId.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // Sign in
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);

        return RedirectToPage("/Index");
    }
}
```

**Pages/Auth/Login.cshtml:**
```html
@page
@model LoginModel

<h2>Login</h2>

@if (!string.IsNullOrEmpty(Model.ErrorMessage))
{
    <div class="alert alert-danger">@Model.ErrorMessage</div>
}

<form method="post">
    <div class="form-group">
        <label asp-for="Email"></label>
        <input asp-for="Email" class="form-control" type="email" required />
    </div>
    <div class="form-group">
        <label asp-for="Password"></label>
        <input asp-for="Password" class="form-control" type="password" required />
    </div>
    <button type="submit" class="btn btn-primary">Login</button>
</form>
```

**Reference:**
- `docs/genesis/diagrams/authentication-flow.mmd` - Authentication sequence diagram

---

## Phase 5: Service Layer

**Time Estimate:** 3-4 days

**Objective:** Implement 130+ services with business logic.

### Step 5.1: Create Core Services

**Services/DirectorService.cs:**
```csharp
using ShiftManager.Data;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class DirectorService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DirectorService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsDirector()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var role = user?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        return role == UserRole.Owner.ToString() || role == UserRole.Director.ToString();
    }

    public async Task<bool> IsDirectorOfAsync(int companyId)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var role = user?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        // Owner has access to all companies
        if (role == UserRole.Owner.ToString())
            return true;

        // Director must have explicit assignment
        if (role == UserRole.Director.ToString())
        {
            var userIdClaim = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int userId))
            {
                return await _db.DirectorCompanies
                    .AnyAsync(dc => dc.UserId == userId && dc.CompanyId == companyId && !dc.IsDeleted);
            }
        }

        return false;
    }

    public bool CanAssignRole(UserRole targetRole)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var role = user?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        // Role assignment hierarchy (see docs/genesis/10-AUTHENTICATION-AND-AUTHORIZATION.md)
        if (role == UserRole.Owner.ToString())
            return true; // Owner can assign any role

        if (role == UserRole.Director.ToString())
            return targetRole != UserRole.Owner; // Director cannot assign Owner

        if (role == UserRole.Manager.ToString())
            return targetRole == UserRole.Employee || targetRole == UserRole.Trainee;

        return false; // Employee/Trainee cannot assign roles
    }
}
```

**Create remaining services following docs/genesis/07-SERVICE-LAYER.md:**
- ShiftAssignmentService.cs - Shift assignment and validation (includes `ValidateShiftAssignmentAsync`)
- NotificationService.cs (808 lines) - Notification creation & email
- ChoreService.cs (640 lines) - Chore assignment logic
- OnDutyService.cs (448 lines) - On-duty assignment
- TimeOffService.cs - Time-off requests
- SwapService.cs - Shift swaps
- AssignmentService.cs - Shift assignment
- ... (33 more services)

**Reference:**
- `docs/genesis/07-SERVICE-LAYER.md` - All 130+ services documented

### Step 5.2: Register Services in Program.cs

**Program.cs (Service Registration):**
```csharp
// Register services
builder.Services.AddScoped<DirectorService>();
builder.Services.AddScoped<IShiftAssignmentService, ShiftAssignmentService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ChoreService>();
builder.Services.AddScoped<OnDutyService>();
// ... (add all 130+ services)

// Register cache services (singleton for lifetime)
builder.Services.AddSingleton<ShiftTypeCacheService>();
builder.Services.AddSingleton<AppConfigCacheService>();
builder.Services.AddSingleton<CompanyCacheService>();

// Register memory cache
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024; // Max 1024 cache entries
});
```

**Reference:**
- `docs/genesis/04-STARTUP-AND-MIDDLEWARE.md` - Complete DI registrations

---

## Phase 6: UI Layer (Razor Pages)

**Time Estimate:** 4-5 days

**Objective:** Create 66 Razor Pages for shift management UI.

### Step 6.1: Create Layout & Navigation

**Pages/Shared/_Layout.cshtml:**
```html
<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - ShiftManager</title>
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
    @if (ViewData["IsRtl"] as bool? == true)
    {
        <link rel="stylesheet" href="~/css/rtl.css" asp-append-version="true" />
    }
</head>
<body data-theme="@ViewData["Theme"]">
    <header>
        <nav class="navbar">
            <a asp-page="/Index" class="navbar-brand">ShiftManager</a>
            <ul class="navbar-nav">
                @if (User.Identity?.IsAuthenticated == true)
                {
                    <li><a asp-page="/Assignments/Manage">Assignments</a></li>
                    <li><a asp-page="/Requests/Index">Requests</a></li>
                    <li><a asp-page="/Reports/Index">Reports</a></li>
                    <li><a asp-page="/Auth/Logout">Logout</a></li>
                }
                else
                {
                    <li><a asp-page="/Auth/Login">Login</a></li>
                }
            </ul>
        </nav>
    </header>

    <main>
        @RenderBody()
    </main>

    <script src="~/js/site.js" asp-append-version="true"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

**Reference:**
- `docs/genesis/08-UI-UX-ARCHITECTURE.md` - All 66 Razor Pages documented

### Step 6.2: Create Key Pages

**Create these critical pages:**
1. Pages/Index.cshtml - Dashboard
2. Pages/Assignments/Manage.cshtml - Shift assignment
3. Pages/Requests/Index.cshtml - Request approval (Manager view)
4. Pages/Requests/Mine.cshtml - My requests (Employee view)
5. Pages/Requests/Create.cshtml - Create time-off/swap request
6. Pages/Chores/Index.cshtml - Chore management
7. Pages/OnDuty/Index.cshtml - On-duty assignments
8. Pages/MyTeam/Calendar.cshtml - Team calendar
9. Pages/Reports/Index.cshtml - Analytics
10. Pages/Admin/Users.cshtml - User management

**Reference:**
- `docs/genesis/08-UI-UX-ARCHITECTURE.md` - Page-by-page documentation

### Step 6.3: Create CSS Styles

**wwwroot/css/site.css (1,882 lines):**
```css
/* Global CSS Variables */
:root {
    --primary-color: #007bff;
    --secondary-color: #6c757d;
    --success-color: #28a745;
    --danger-color: #dc3545;
    --warning-color: #ffc107;
    --info-color: #17a2b8;
    --background: white;
    --text-color: #212529;
    --border-color: #dee2e6;
}

/* Dark Mode */
[data-theme="dark"] {
    --background: #1a1a1a;
    --text-color: #e0e0e0;
    --border-color: #444;
}

/* Layout */
body {
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
    background-color: var(--background);
    color: var(--text-color);
}

/* ... (1,882 lines total, see docs/genesis/08-UI-UX-ARCHITECTURE.md) */
```

**wwwroot/css/rtl.css (270 lines):**
```css
/* RTL-specific overrides */
[dir="rtl"] body {
    direction: rtl;
    text-align: right;
}

[dir="rtl"] .navbar-brand {
    margin-right: 0;
    margin-left: 1rem;
}

/* ... (270 lines total) */
```

**Reference:**
- `docs/genesis/08-UI-UX-ARCHITECTURE.md` - CSS architecture

---

## Phase 7: Business Workflows

**Time Estimate:** 3-4 days

**Objective:** Implement 7 major workflows (shift assignment, time-off, swaps, etc.)

### Step 7.1: Implement Shift Assignment Workflow

**Pages/Assignments/Manage.cshtml.cs:**
```csharp
public class ManageModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IShiftAssignmentService _shiftAssignmentService;

    public ManageModel(AppDbContext db, IShiftAssignmentService shiftAssignmentService)
    {
        _db = db;
        _shiftAssignmentService = shiftAssignmentService;
    }

    public async Task<IActionResult> OnPostAsync(int userId, DateTime workDate, string shiftTypeKey)
    {
        // Validate input
        if (userId <= 0 || workDate < DateTime.Today)
            return BadRequest("Invalid input");

        // Get shift type
        var shiftType = await _db.ShiftTypes
            .FirstOrDefaultAsync(st => st.Key == shiftTypeKey);
        if (shiftType == null)
            return BadRequest("Shift type not found");

        // Check conflicts
        var conflictResult = await _conflictChecker.CanAssignAsync(userId, new ShiftInstance
        {
            WorkDate = workDate,
            StartTime = shiftType.Start,
            EndTime = shiftType.End
        });

        if (conflictResult.HasConflict)
        {
            TempData["ErrorMessage"] = conflictResult.Message;
            return RedirectToPage();
        }

        // Create shift assignment
        var assignment = new ShiftAssignment
        {
            UserId = userId,
            WorkDate = workDate,
            ShiftTypeId = shiftType.Id,
            CreatedAt = DateTime.UtcNow
        };

        _db.ShiftAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Shift assigned successfully";
        return RedirectToPage();
    }
}
```

**Reference:**
- `docs/genesis/14-WORKFLOWS-AND-BUSINESS-LOGIC.md` - All 7 workflows documented

### Step 7.2: Implement Time-Off Request Workflow

**Pages/Requests/Create.cshtml.cs:**
```csharp
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notificationService;

    [BindProperty]
    public DateTime StartDate { get; set; }

    [BindProperty]
    public DateTime EndDate { get; set; }

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    public async Task<IActionResult> OnPostAsync()
    {
        // Validate dates
        if (StartDate > EndDate)
        {
            ModelState.AddModelError("", "End date must be after start date");
            return Page();
        }

        if (StartDate < DateTime.Today)
        {
            ModelState.AddModelError("", "Cannot create request for past dates");
            return Page();
        }

        // Create time-off request
        var request = new TimeOffRequest
        {
            UserId = GetCurrentUserId(),
            StartDate = StartDate,
            EndDate = EndDate,
            Reason = Reason,
            Status = RequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.TimeOffRequests.Add(request);
        await _db.SaveChangesAsync();

        // Send notification to manager
        await _notificationService.NotifyTimeOffRequested(request.Id);

        TempData["SuccessMessage"] = "Request submitted successfully";
        return RedirectToPage("/Requests/Mine");
    }
}
```

**Reference:**
- `docs/genesis/diagrams/request-approval-flow.mmd` - Request workflow sequence diagram

---

## Phase 8: Localization & RTL

**Time Estimate:** 2 days

**Objective:** Implement dual-language support (en-US, he-IL) with RTL.

### Step 8.1: Configure Localization in Program.cs

**Program.cs (Localization Section):**
```csharp
// Configure localization
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "en-US", "he-IL" };
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
    options.SupportedUICultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();

    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
    options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
    options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
});

builder.Services.AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();
```

### Step 8.2: Create Resource Files

**Create Resources/Pages/Index.en-US.resx:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Welcome">
    <value>Welcome to ShiftManager</value>
  </data>
  <data name="YourShifts">
    <value>Your Shifts</value>
  </data>
</root>
```

**Create Resources/Pages/Index.he-IL.resx:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Welcome">
    <value>ברוכים הבאים למערכת ניהול משמרות</value>
  </data>
  <data name="YourShifts">
    <value>המשמרות שלך</value>
  </data>
</root>
```

**Reference:**
- `docs/genesis/11-LOCALIZATION-AND-RTL.md` - Complete localization guide

---

## Phase 9: API Layer & External Integration

**Time Estimate:** 2 days

**Objective:** Implement 27 REST endpoints and Griffin ADFS integration.

### Step 9.1: Create API Controllers

**Controllers/CalendarApiController.cs:**
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Controllers;

[ApiController]
[Route("api/calendar")]
[Authorize]
public class CalendarApiController : ControllerBase
{
    private readonly AppDbContext _db;

    public CalendarApiController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("shifts")]
    public async Task<IActionResult> GetShifts(DateTime startDate, DateTime endDate)
    {
        var shifts = await _db.ShiftAssignments
            .Include(sa => sa.User)
            .Include(sa => sa.ShiftType)
            .Where(sa => sa.WorkDate >= startDate && sa.WorkDate <= endDate)
            .Select(sa => new
            {
                id = sa.Id,
                userId = sa.UserId,
                userName = sa.User.DisplayName,
                workDate = sa.WorkDate,
                shiftType = sa.ShiftType.Key,
                startTime = sa.ShiftType.Start,
                endTime = sa.ShiftType.End
            })
            .ToListAsync();

        return Ok(shifts);
    }
}
```

**Reference:**
- `docs/genesis/09-API-LAYER.md` - All 27 endpoints documented

### Step 9.2: Implement Griffin ADFS Integration

**Services/GriffinService.cs:**
```csharp
public class GriffinService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;

    public async Task<ClaimsPrincipal?> AuthenticateUserAsync(string token)
    {
        // Check cache (SHA256 hash of token)
        string cacheKey = $"Griffin_{HashToken(token)}";
        if (_cache.TryGetValue(cacheKey, out ClaimsPrincipal? cachedPrincipal))
            return cachedPrincipal;

        // Validate token with Griffin API
        var isValid = await ValidateTokenAsync(token);
        if (!isValid)
            return null;

        // Fetch claims from Griffin API
        var claims = await GetClaimsAsync(token);
        if (claims == null)
            return null;

        // Find or create user
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == claims.UPN);

        if (user == null && ShouldAutoProvision())
        {
            // Auto-provision user from Griffin claims
            user = await CreateUserFromGriffinAsync(claims);
        }

        if (user == null)
            return null;

        // Create claims principal
        var principal = CreateClaimsPrincipal(user, claims);

        // Cache for 8 hours
        _cache.Set(cacheKey, principal, TimeSpan.FromHours(8));

        return principal;
    }

    private string HashToken(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    // ... (implement ValidateTokenAsync, GetClaimsAsync, CreateUserFromGriffinAsync)
}
```

**Reference:**
- `docs/genesis/diagrams/authentication-flow.mmd` - Griffin ADFS flow

---

## Phase 10: Build Pipeline & Deployment

**Time Estimate:** 2 days

**Objective:** Create automated build pipeline for air-gapped deployment.

### Step 10.1: Create Build-Release.ps1

**Build-Release.ps1:**
```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [switch]$SkipTests,
    [switch]$NoPush
)

# Validate version format (semantic versioning)
if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9]+)?$') {
    throw "Invalid version format. Use: 1.0.0 or 1.0.0-beta"
}

Write-Host "=== ShiftManager Build Pipeline ===" -ForegroundColor Cyan
Write-Host "Version: $Version"

# Stage 1: Pre-Flight Checks
Write-Host "[Stage 1/10] Pre-Flight Checks..." -ForegroundColor Yellow
if (-not (Test-Path "ShiftManager.csproj")) {
    throw "ShiftManager.csproj not found in current directory"
}

# Stage 2: Clean
Write-Host "[Stage 2/10] Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean -c Release

# Stage 3: Restore
Write-Host "[Stage 3/10] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore

# Stage 4: Build
Write-Host "[Stage 4/10] Building application..." -ForegroundColor Yellow
dotnet build -c Release --no-restore

# Stage 5: Test (if not skipped)
if (-not $SkipTests) {
    Write-Host "[Stage 5/10] Running unit tests..." -ForegroundColor Yellow
    dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed"
    }
}

# Stage 6: Publish
Write-Host "[Stage 6/10] Publishing self-contained build..." -ForegroundColor Yellow
$outputFolder = "ProjectPublish"
dotnet publish -c Release -r win-x64 --self-contained true -o $outputFolder

# Stage 7: Copy deployment scripts
Write-Host "[Stage 7/10] Copying deployment scripts..." -ForegroundColor Yellow
Copy-Item "UNBLOCK_FILES.bat" $outputFolder
Copy-Item "VERIFY_FILES.bat" $outputFolder
Copy-Item "AIR_GAPPED_DEPLOYMENT_GUIDE.txt" $outputFolder

# Stage 8: Verify build
Write-Host "[Stage 8/10] Verifying build artifacts..." -ForegroundColor Yellow
& ".\build\Verify-Build.ps1" -PublishFolder $outputFolder

# Stage 9: Test application
Write-Host "[Stage 9/10] Testing application startup..." -ForegroundColor Yellow
& ".\build\Test-Application.ps1" -PublishFolder $outputFolder

# Stage 10: Package
Write-Host "[Stage 10/10] Creating deployment package..." -ForegroundColor Yellow
$zipName = "ShiftManager_v$Version.zip"
Compress-Archive -Path "$outputFolder\*" -DestinationPath $zipName -Force

# Generate SHA256 checksum
$hash = (Get-FileHash $zipName -Algorithm SHA256).Hash
"$hash  $zipName" | Out-File "$zipName.sha256" -Encoding utf8

Write-Host "✅ Build completed successfully!" -ForegroundColor Green
Write-Host "Package: $zipName"
Write-Host "Checksum: $zipName.sha256"
```

**Reference:**
- `docs/genesis/16-BUILD-AND-RELEASE-PIPELINE.md` - Complete build pipeline documentation

### Step 10.2: Create Deployment Scripts

**UNBLOCK_FILES.bat:**
```batch
@echo off
echo Unblocking all files (removing Zone.Identifier)...
powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File"
echo Done! Files are now unblocked.
pause
```

**VERIFY_FILES.bat:**
```batch
@echo off
echo Verifying critical files exist...

set CRITICAL_FILES=ShiftManager.exe ShiftManager.dll e_sqlite3.dll SixLabors.ImageSharp.dll appsettings.json

for %%F in (%CRITICAL_FILES%) do (
    if exist "%%F" (
        echo [OK] %%F
    ) else (
        echo [ERROR] %%F - MISSING!
    )
)

echo.
echo Verification complete.
pause
```

**Reference:**
- `docs/genesis/15-AIR-GAPPED-DEPLOYMENT.md` - Complete deployment guide

---

## Verification Steps

### Functional Verification Checklist

**1. Authentication ✓**
- [ ] Login with email/password works
- [ ] Password hashing (100,000 iterations PBKDF2)
- [ ] Cookie authentication (7-day expiration)
- [ ] Logout clears session
- [ ] Account lockout (10 failed attempts)

**2. Multi-Tenancy ✓**
- [ ] Users only see data from their company
- [ ] CompanyId automatically assigned on insert
- [ ] Global query filters enforce row-level security
- [ ] Directors can access multiple companies (with assignment)
- [ ] Owners can access all companies

**3. Shift Management ✓**
- [ ] Create shift assignment
- [ ] Conflict detection (time-off, overlaps, rest period, weekly cap)
- [ ] OFFLINE shift type (special overlap rules)
- [ ] Delete shift assignment
- [ ] View team calendar

**4. Request Workflows ✓**
- [ ] Create time-off request
- [ ] Manager approves time-off → auto-delete overlapping shifts
- [ ] Manager declines time-off → no shifts deleted
- [ ] Create swap request
- [ ] ShiftAssignmentService.ValidateShiftAssignmentAsync validates before swap approval
- [ ] Notifications sent on approval/decline

**5. Authorization ✓**
- [ ] Owner can access Admin pages
- [ ] Director can manage assigned companies
- [ ] Manager can manage own company
- [ ] Employee can create requests
- [ ] Trainee has limited access
- [ ] Unauthorized access returns 403

**6. Localization ✓**
- [ ] Switch to Hebrew (he-IL) → RTL layout
- [ ] Switch to English (en-US) → LTR layout
- [ ] Date formatting culture-aware
- [ ] Resource strings translated

**7. API Endpoints ✓**
- [ ] GET /api/calendar/shifts returns shifts
- [ ] POST /api/time-off creates request
- [ ] API authentication (cookie or API key)
- [ ] Rate limiting (10 requests/15 min per IP)

**8. Build & Deployment ✓**
- [ ] Build-Release.ps1 completes successfully
- [ ] Self-contained publish includes .NET runtime
- [ ] UNBLOCK_FILES.bat removes Zone.Identifier
- [ ] Application starts offline (no internet)
- [ ] Database created automatically (ShiftManager.db)
- [ ] OFFLINE shift type seeded

---

## Common Pitfalls & Solutions

### Pitfall 1: Global Query Filters Not Applied

**Symptom:** Users see data from all companies, not just their own.

**Solution:**
```csharp
// ❌ WRONG: Query filters bypassed
var users = await _db.Users.IgnoreQueryFilters().ToListAsync();

// ✅ CORRECT: Query filters applied
var users = await _db.Users.ToListAsync(); // Only current company
```

**When to use IgnoreQueryFilters():**
- Seeding data (Program.cs)
- Admin operations (Owner viewing all companies)
- Authentication (finding user by email across companies)

---

### Pitfall 2: CompanyId Not Set on Insert

**Symptom:** Database constraint error: "CompanyId cannot be null"

**Solution:**
Ensure CompanyIdInterceptor registered in Program.cs:
```csharp
builder.Services.AddSingleton<CompanyIdInterceptor>();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, opt) =>
{
    var interceptor = serviceProvider.GetRequiredService<CompanyIdInterceptor>();
    opt.UseSqlite(connectionString)
       .AddInterceptors(interceptor); // ← Required!
});
```

---

### Pitfall 3: Zone.Identifier Blocking DLL Loads

**Symptom:** Application fails to start with error: "Could not load file or assembly 'SixLabors.ImageSharp'"

**Solution:**
Run UNBLOCK_FILES.bat **BEFORE** first execution:
```batch
UNBLOCK_FILES.bat
```

**Why this happens:**
Windows marks files from USB/internet as "untrusted" (Zone.Identifier alternate data stream).

**Reference:**
- `docs/genesis/15-AIR-GAPPED-DEPLOYMENT.md` - Complete troubleshooting

---

### Pitfall 4: Missing Middleware Order

**Symptom:** Authentication doesn't work, 401 errors on authenticated pages.

**Solution:**
Ensure correct middleware order in Program.cs:
```csharp
app.UseStaticFiles();
app.UseRouting();
app.UseRequestLogging();               // 1. Request logging
app.UseRequestLocalization();          // 2. Localization
app.UseMiddleware<GriffinAuthenticationMiddleware>(); // 3. Griffin auth (BEFORE UseAuthentication)
app.UseMiddleware<CompanyContextMiddleware>();        // 4. Company context (BEFORE UseAuthentication)
app.UseAuthentication();               // 5. ASP.NET authentication
app.UseAuthorization();                // 6. ASP.NET authorization
app.MapRazorPages();
```

**Reference:**
- `docs/genesis/diagrams/middleware-pipeline.mmd` - Middleware order diagram

---

### Pitfall 5: N+1 Query Problem

**Symptom:** Slow page load, hundreds of database queries.

**Solution:**
Use `.Include()` for eager loading:
```csharp
// ❌ WRONG: N+1 queries (1 query for shifts + N queries for each user)
var shifts = await _db.ShiftAssignments.ToListAsync();
foreach (var shift in shifts)
{
    var userName = shift.User.DisplayName; // ← N additional queries
}

// ✅ CORRECT: Single query with JOIN
var shifts = await _db.ShiftAssignments
    .Include(sa => sa.User)
    .Include(sa => sa.ShiftType)
    .ToListAsync();
```

---

### Pitfall 6: Hardcoded CompanyId

**Symptom:** Multi-tenancy broken, users see wrong data.

**Solution:**
```csharp
// ❌ WRONG: Hardcoded CompanyId
var users = await _db.Users.Where(u => u.CompanyId == 1).ToListAsync();

// ✅ CORRECT: Use current company from context
var users = await _db.Users.ToListAsync(); // Query filter applies current CompanyId
```

---

## Summary: Reconstruction Checklist

**Phase Completion Checklist:**

- [ ] **Phase 1:** Project foundation (solution, packages, folder structure)
- [ ] **Phase 2:** Database schema (70+ entities, migrations, relationships)
- [ ] **Phase 3:** Multi-tenancy (CompanyId interceptor, query filters, middleware)
- [ ] **Phase 4:** Authentication (cookie auth, password hashing, login page, 7 roles)
- [ ] **Phase 5:** Service layer (130+ services, business logic)
- [ ] **Phase 6:** UI layer (66 Razor Pages, layout, navigation, CSS)
- [ ] **Phase 7:** Business workflows (shift assignment, time-off, swaps, notifications)
- [ ] **Phase 8:** Localization (en-US, he-IL, RTL CSS)
- [ ] **Phase 9:** API layer (27 endpoints, Griffin ADFS integration)
- [ ] **Phase 10:** Build pipeline (Build-Release.ps1, deployment scripts)

**Final Verification:**
- [ ] All tests pass (if implemented)
- [ ] Application runs offline
- [ ] Multi-tenancy enforced
- [ ] Authentication works (standard + Griffin ADFS)
- [ ] Workflows complete (create shift → approve time-off → swap)
- [ ] Localization switches correctly (en-US ↔ he-IL)
- [ ] API endpoints return data
- [ ] Build pipeline packages successfully

**Documentation Cross-Reference:**
Read these documents in order for complete understanding:
1. `00-INDEX.md` - Navigation hub
2. `01-EXECUTIVE-OVERVIEW.md` - Business context
3. `02-ARCHITECTURE-BLUEPRINT.md` - System design
4. `03-DATABASE-SCHEMA.md` - Complete ERD
5. `04-STARTUP-AND-MIDDLEWARE.md` - Program.cs deep dive
6. `05-MULTI-TENANCY-DEEP-DIVE.md` - Row-level security
7. `06-DOMAIN-MODELS.md` - All 70+ entities
8. `07-SERVICE-LAYER.md` - 130+ services
9. `08-UI-UX-ARCHITECTURE.md` - 66 Razor Pages
10. `09-API-LAYER.md` - 27 REST endpoints
11. `10-AUTHENTICATION-AND-AUTHORIZATION.md` - Auth system
12. `11-LOCALIZATION-AND-RTL.md` - Internationalization
13. `12-DATA-MIGRATIONS.md` - Database evolution
14. `13-CACHING-STRATEGY.md` - Performance optimization
15. `14-WORKFLOWS-AND-BUSINESS-LOGIC.md` - Request flows
16. `15-AIR-GAPPED-DEPLOYMENT.md` - Deployment guide
17. `16-BUILD-AND-RELEASE-PIPELINE.md` - Build automation
18. `17-TESTING-STRATEGY.md` - Test infrastructure
19. `18-DESIGN-DECISIONS-AND-TRADEOFFS.md` - The "soul"
20. `19-RECONSTRUCTION-RECIPE.md` - This document

---

**Document End** - ShiftManager Reconstruction Recipe

**Estimated Total Lines:** 24,629 lines across 19 documents
**Estimated Rebuild Time:** 8-12 weeks (solo), 4-6 weeks (team of 3)
**Success Criteria:** Fully functional shift management system matching original architecture

**Good luck with your reconstruction! 🚀**
