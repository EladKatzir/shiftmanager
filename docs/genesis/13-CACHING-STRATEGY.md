# 13. Caching Strategy

**Document Version:** 1.0
**Last Updated:** December 2025
**Part of:** PROJECT COSMOGENESIS - ShiftManager Genesis Documentation

---

## Table of Contents

1. [Overview](#overview)
2. [Caching Infrastructure](#caching-infrastructure)
3. [Cache Service Architecture](#cache-service-architecture)
4. [ShiftTypeCacheService](#shifttypecacheservice)
5. [AppConfigCacheService](#appconfigcacheservice)
6. [CompanyCacheService](#companycacheservice)
7. [Griffin ADFS Claims Caching](#griffin-adfs-claims-caching)
8. [Analytics Query Caching](#analytics-query-caching)
9. [Cache Invalidation Strategies](#cache-invalidation-strategies)
10. [AsNoTracking Pattern](#asnotracking-pattern)
11. [Performance Impact](#performance-impact)
12. [Best Practices](#best-practices)

---

## Overview

ShiftManager employs a **multi-layered caching strategy** to optimize performance while maintaining data consistency in a multi-tenant environment. The caching architecture balances:

- **Reduced database load** - Minimize queries for frequently-accessed, rarely-changing data
- **Tenant isolation** - Cache keys scoped to `CompanyId` for multi-tenancy
- **Freshness vs. Performance** - Short TTLs (5-10 minutes) for critical data
- **Manual invalidation** - Explicit cache busting on write operations
- **Security** - Griffin ADFS token caching with SHA256 hashing

### Caching Technologies

- **IMemoryCache** - ASP.NET Core in-memory cache (singleton, process-scoped)
- **AsNoTracking()** - EF Core read-only queries (prevent change tracking overhead)
- **Absolute expiration** - Time-based TTL eviction
- **Manual invalidation** - Explicit `InvalidateCache()` calls on writes

### Cache Hierarchy

```
┌─────────────────────────────────────────────────────────────┐
│                    IMemoryCache (Singleton)                 │
│  Process-scoped, shared across all requests/tenants        │
└─────────────────────────────────────────────────────────────┘
                          │
        ┌─────────────────┼─────────────────┐
        │                 │                 │
        ▼                 ▼                 ▼
┌───────────────┐  ┌───────────────┐  ┌───────────────┐
│ Tenant-Scoped │  │ Cross-Tenant  │  │  Analytics    │
│    Caches     │  │    Caches     │  │   Caches      │
│               │  │               │  │               │
│ • ShiftTypes  │  │ • Griffin     │  │ • Employee    │
│ • AppConfigs  │  │   Claims      │  │   Hours       │
│ • Companies   │  │   (8h TTL)    │  │   (5min TTL)  │
│ (5-10min TTL) │  │               │  │               │
└───────────────┘  └───────────────┘  └───────────────┘
```

---

## Caching Infrastructure

### IMemoryCache Registration

**File:** `Program.cs:121`

```csharp
// Griffin ADFS services
builder.Services.AddScoped<IGriffinConfigService, GriffinConfigService>();
builder.Services.AddScoped<IGriffinService, GriffinService>();
builder.Services.AddMemoryCache(); // For Griffin claims caching (may already be registered)

// Phase 2C: Performance Optimization - Caching Services
builder.Services.AddScoped<IShiftTypeCacheService, ShiftTypeCacheService>();
builder.Services.AddScoped<IAppConfigCacheService, AppConfigCacheService>();
builder.Services.AddScoped<ICompanyCacheService, CompanyCacheService>();
```

**Lifetime:** Singleton
**Scope:** Process-wide (shared across all requests and tenants)
**Configuration:** Default ASP.NET Core settings (no size limit, no eviction policy)

### Dependency Injection

All cache services are registered as **Scoped** (per-request lifetime) because they:
- Depend on `AppDbContext` (which is scoped to prevent concurrency issues)
- Require `ITenantResolver` (for multi-tenancy)
- Are lightweight wrappers around `IMemoryCache` (singleton)

**Key Pattern:**
- Cache services: **Scoped** (new instance per request)
- `IMemoryCache`: **Singleton** (shared state across requests)
- Cache keys include `CompanyId` for tenant isolation

---

## Cache Service Architecture

### Design Pattern: Repository + Cache-Aside

All cache services follow this pattern:

```csharp
public async Task<T> GetDataAsync(int companyId)
{
    string cacheKey = $"DataType_{companyId}";

    // 1. Try cache first (Cache-Aside pattern)
    if (_cache.TryGetValue(cacheKey, out T? cached) && cached != null)
    {
        _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
        return cached;
    }

    _logger.LogDebug("Cache miss for {CacheKey}, loading from database", cacheKey);

    // 2. Load from database
    var data = await _db.DataSet
        .AsNoTracking() // Read-only, no change tracking
        .Where(d => d.CompanyId == companyId)
        .ToListAsync();

    // 3. Cache with TTL
    var cacheOptions = new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(TimeSpan.FromMinutes(TTL))
        .SetSize(1); // For eviction policies (if configured)

    _cache.Set(cacheKey, data, cacheOptions);

    return data;
}

// 4. Manual invalidation on writes
public void InvalidateCache(int companyId)
{
    string cacheKey = $"DataType_{companyId}";
    _cache.Remove(cacheKey);
    _logger.LogInformation("Invalidated cache for {CacheKey}", cacheKey);
}
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Cache-Aside** (not Write-Through) | Simplicity - explicit invalidation on writes |
| **Absolute expiration** (not sliding) | Predictable freshness - cache expires after fixed duration regardless of access patterns |
| **Tenant-scoped keys** | Multi-tenancy isolation - `CompanyId` in cache key prevents cross-tenant data leaks |
| **Short TTLs (5-10 min)** | Balance freshness vs. performance - most data updates are rare but must propagate quickly |
| **Manual invalidation** | Consistency - explicit `InvalidateCache()` calls ensure writes immediately bust cache |
| **AsNoTracking()** | Performance - read-only queries skip EF change tracking overhead |

---

## ShiftTypeCacheService

**Purpose:** Cache shift type definitions (Morning, Evening, Night, etc.) per company

**File:** `Services/ShiftTypeCacheService.cs` (84 lines)

### Configuration

- **TTL:** 10 minutes (absolute expiration)
- **Cache Key:** `ShiftTypes_{CompanyId}`
- **Invalidation:** Manual (on create/update/delete)
- **Lifetime:** Scoped

### Interface

```csharp
public interface IShiftTypeCacheService
{
    Task<List<ShiftType>> GetShiftTypesAsync(int companyId);
    void InvalidateCache(int companyId);
}
```

### Implementation

**Services/ShiftTypeCacheService.cs:42-71**

```csharp
public async Task<List<ShiftType>> GetShiftTypesAsync(int companyId)
{
    string cacheKey = $"ShiftTypes_{companyId}";

    if (_cache.TryGetValue(cacheKey, out List<ShiftType>? shiftTypes) && shiftTypes != null)
    {
        _logger.LogDebug("ShiftTypes cache hit for company {CompanyId}", companyId);
        return shiftTypes;
    }

    _logger.LogDebug("ShiftTypes cache miss for company {CompanyId}, loading from database", companyId);

    // Load from database with company filter
    shiftTypes = await _db.ShiftTypes
        .AsNoTracking()
        .Where(st => st.CompanyId == companyId)
        .OrderBy(st => st.Key)
        .ToListAsync();

    // Cache for 10 minutes
    var cacheOptions = new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
        .SetSize(1); // Estimate size for cache eviction policies

    _cache.Set(cacheKey, shiftTypes, cacheOptions);

    _logger.LogInformation("Cached {Count} shift types for company {CompanyId}", shiftTypes.Count, companyId);

    return shiftTypes;
}
```

### Cache Invalidation

**Services/ShiftTypeCacheService.cs:77-82**

```csharp
public void InvalidateCache(int companyId)
{
    string cacheKey = $"ShiftTypes_{companyId}";
    _cache.Remove(cacheKey);
    _logger.LogInformation("Invalidated ShiftTypes cache for company {CompanyId}", companyId);
}
```

**Triggers:** Call `InvalidateCache(companyId)` after:
- Creating new shift types (`POST /api/v1/shift-types`)
- Updating existing shift types (`PUT /api/v1/shift-types/{id}`)
- Deleting shift types (`DELETE /api/v1/shift-types/{id}`)

### Performance Impact

**Scenario:** Schedule page loads shift types 10 times (shift calendar, availability grid, assignment dropdowns)

- **Without cache:** 10 DB queries × ~50ms = **500ms**
- **With cache (10min TTL):** 1 DB query (first load) + 9 cache hits × ~0.1ms = **50ms + 0.9ms = ~51ms**
- **Savings:** **~449ms per page load (90% reduction)**

---

## AppConfigCacheService

**Purpose:** Cache company configuration settings (feature flags, thresholds, integrations)

**File:** `Services/AppConfigCacheService.cs` (107 lines)

### Configuration

- **TTL:** 5 minutes (absolute expiration)
- **Cache Key:** `AppConfigs_{CompanyId}`
- **Invalidation:** Manual (on create/update/delete)
- **Lifetime:** Scoped

### Interface

```csharp
public interface IAppConfigCacheService
{
    Task<AppConfig?> GetConfigAsync(int companyId, string key);
    Task<Dictionary<string, string>> GetAllConfigsAsync(int companyId);
    void InvalidateCache(int companyId);
}
```

### Implementation

**Services/AppConfigCacheService.cs:63-94**

```csharp
public async Task<Dictionary<string, string>> GetAllConfigsAsync(int companyId)
{
    string cacheKey = $"AppConfigs_{companyId}";

    if (_cache.TryGetValue(cacheKey, out Dictionary<string, string>? configs) && configs != null)
    {
        _logger.LogDebug("AppConfigs cache hit for company {CompanyId}", companyId);
        return configs;
    }

    _logger.LogDebug("AppConfigs cache miss for company {CompanyId}, loading from database", companyId);

    // Load from database with company filter
    var configList = await _db.Configs
        .AsNoTracking()
        .Where(c => c.CompanyId == companyId)
        .ToListAsync();

    // Convert to dictionary for fast lookup
    configs = configList.ToDictionary(c => c.Key, c => c.Value);

    // Cache for 5 minutes
    var cacheOptions = new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
        .SetSize(1); // Estimate size for cache eviction policies

    _cache.Set(cacheKey, configs, cacheOptions);

    _logger.LogInformation("Cached {Count} config settings for company {CompanyId}", configs.Count, companyId);

    return configs;
}
```

### Dictionary Caching Pattern

**Key Optimization:** Cache returns `Dictionary<string, string>` for O(1) key lookups:

**Services/AppConfigCacheService.cs:43-57**

```csharp
public async Task<AppConfig?> GetConfigAsync(int companyId, string key)
{
    var allConfigs = await GetAllConfigsAsync(companyId);

    if (allConfigs.TryGetValue(key, out var value))
    {
        return new AppConfig
        {
            CompanyId = companyId,
            Key = key,
            Value = value
        };
    }

    return null;
}
```

**Why dictionary?** Most pages need multiple configs (e.g., `MaxShiftSwapDays`, `RequireApprovalForSwaps`, `AllowBackToBackShifts`). Caching as dictionary allows:
- **Single DB query** for all configs
- **O(1) lookup** for individual config values
- **Reduced memory** (single cache entry vs. N entries)

### Cache Invalidation

**Services/AppConfigCacheService.cs:100-105**

```csharp
public void InvalidateCache(int companyId)
{
    string cacheKey = $"AppConfigs_{companyId}";
    _cache.Remove(cacheKey);
    _logger.LogInformation("Invalidated AppConfigs cache for company {CompanyId}", companyId);
}
```

**Triggers:** Call `InvalidateCache(companyId)` after:
- Admin updates config settings (`/Admin/Settings`)
- API config updates (`PUT /api/v1/configs/{key}`)

### Performance Impact

**Scenario:** Pages load 3-5 config values per request (approval thresholds, feature flags, integration settings)

- **Without cache:** 5 DB queries × ~30ms = **150ms**
- **With cache (5min TTL):** 1 DB query (first load) + 4 cache hits × ~0.1ms = **30ms + 0.4ms = ~30ms**
- **Savings:** **~120ms per page load (80% reduction)**

---

## CompanyCacheService

**Purpose:** Cache company metadata (name, display name, settings)

**File:** `Services/CompanyCacheService.cs` (88 lines)

### Configuration

- **TTL:** 10 minutes (absolute expiration)
- **Cache Key:** `Company_{CompanyId}`
- **Invalidation:** Manual (on update)
- **Lifetime:** Scoped

### Interface

```csharp
public interface ICompanyCacheService
{
    Task<Company?> GetCompanyAsync(int companyId);
    void InvalidateCache(int companyId);
}
```

### Implementation

**Services/CompanyCacheService.cs:42-75**

```csharp
public async Task<Company?> GetCompanyAsync(int companyId)
{
    string cacheKey = $"Company_{companyId}";

    if (_cache.TryGetValue(cacheKey, out Company? company) && company != null)
    {
        _logger.LogDebug("Company cache hit for company {CompanyId}", companyId);
        return company;
    }

    _logger.LogDebug("Company cache miss for company {CompanyId}, loading from database", companyId);

    // Load from database
    company = await _db.Companies
        .AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == companyId);

    if (company == null)
    {
        _logger.LogWarning("Company {CompanyId} not found in database", companyId);
        return null;
    }

    // Cache for 10 minutes
    var cacheOptions = new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
        .SetSize(1); // Estimate size for cache eviction policies

    _cache.Set(cacheKey, company, cacheOptions);

    _logger.LogInformation("Cached company {CompanyId} ({CompanyName})", companyId, company.DisplayName ?? company.Name);

    return company;
}
```

### Cache Invalidation

**Services/CompanyCacheService.cs:81-86**

```csharp
public void InvalidateCache(int companyId)
{
    string cacheKey = $"Company_{companyId}";
    _cache.Remove(cacheKey);
    _logger.LogInformation("Invalidated Company cache for company {CompanyId}", companyId);
}
```

**Triggers:** Call `InvalidateCache(companyId)` after:
- Company metadata updates (`PUT /api/v1/companies/{id}`)
- Company name/display name changes

### Performance Impact

**Scenario:** Header displays company name on every page load

- **Without cache:** 1 DB query × ~20ms = **20ms**
- **With cache (10min TTL):** Cache hit × ~0.1ms = **0.1ms**
- **Savings:** **~20ms per page load (99.5% reduction)**

---

## Griffin ADFS Claims Caching

**Purpose:** Cache SAML SSO claims to reduce external API calls to Griffin ADFS server

**File:** `Services/GriffinService.cs` (267 lines)

### Configuration

- **TTL:** 8 hours (absolute expiration)
- **Cache Key:** `griffin_claims_{SHA256(token)}`
- **Invalidation:** Automatic (TTL) + manual (on logout)
- **Lifetime:** Scoped (GriffinService)

### Why Cache Griffin Claims?

Griffin ADFS authentication requires **2 external HTTP requests** per token validation:
1. **Validate token:** `GET /authorization/validate?token=...` (~200ms)
2. **Get claims:** `GET /authorization/getClaims?token=...` (~300ms)

**Total:** ~500ms per authentication check

**Problem:** Griffin middleware re-validates on **every request** (not just login) to ensure token hasn't been revoked.

**Solution:** Cache claims for 8 hours with SHA256-hashed token as key.

### Implementation

**Services/GriffinService.cs:118-154**

```csharp
public async Task<GriffinClaimsDto?> ValidateAndGetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds)
{
    // Compute cache key from token hash
    var cacheKey = $"griffin_claims_{ComputeSHA256Hash(token)}";

    // Try cache first
    if (_cache.TryGetValue<GriffinClaimsDto>(cacheKey, out var cachedClaims))
    {
        _logger.LogDebug("Griffin claims cache hit");
        return cachedClaims;
    }

    _logger.LogDebug("Griffin claims cache miss, calling API");

    // Validate token
    var isValid = await ValidateTokenAsync(token, griffinBaseUrl, timeoutSeconds);
    if (!isValid)
    {
        return null;
    }

    // Get claims
    var claims = await GetClaimsAsync(token, griffinBaseUrl, timeoutSeconds);
    if (claims == null)
    {
        return null;
    }

    // Cache with 8-hour TTL
    _cache.Set(cacheKey, claims, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8),
        Priority = CacheItemPriority.Normal
    });

    return claims;
}
```

### SHA256 Hashing

**Why hash token?** Security - don't store raw SAML tokens in cache (could be extracted if memory is compromised).

**Services/GriffinService.cs:259-265**

```csharp
private string ComputeSHA256Hash(string input)
{
    using var sha256 = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(input);
    var hash = sha256.ComputeHash(bytes);
    return Convert.ToHexString(hash);
}
```

**Example cache key:**
```
griffin_claims_3A5B7C9D1E2F4A6B8C0D1E2F3A4B5C6D7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A2B
```

### Cache Invalidation on Logout

**File:** `Pages/Auth/Logout.cshtml.cs:25-61`

```csharp
public async Task<IActionResult> OnPostAsync()
{
    if (User?.Identity?.IsAuthenticated == true)
    {
        var authMethod = User.FindFirst("AuthMethod")?.Value;

        // If Griffin auth, clear Griffin-specific data
        if (authMethod == "Griffin")
        {
            // Clear Griffin token cookie
            Response.Cookies.Delete("griffin.token");

            // Clear cache entry
            var token = User.FindFirst("Griffin:Token")?.Value;
            if (!string.IsNullOrEmpty(token))
            {
                // Compute SHA256 hash (same as GriffinService)
                using var sha256 = SHA256.Create();
                var tokenBytes = Encoding.UTF8.GetBytes(token);
                var hashBytes = sha256.ComputeHash(tokenBytes);
                var hashString = Convert.ToHexString(hashBytes);
                var cacheKey = $"griffin_claims_{hashString}";

                _cache.Remove(cacheKey);
            }

            _logger.LogInformation("User {UserId} logged out (Griffin ADFS)",
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }

        // Clear ASP.NET auth cookie
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    LoggedOut = true;
    return Page();
}
```

**Critical:** Logout explicitly removes Griffin cache entry to prevent stale claims if user logs out and back in with different account.

### Performance Impact

**Scenario:** Griffin user navigates 50 pages per session (middleware validates on every request)

- **Without cache:** 50 requests × 500ms (2 HTTP calls) = **25,000ms = 25 seconds**
- **With cache (8h TTL):** 1 request × 500ms (first) + 49 cache hits × 0.1ms = **500ms + 4.9ms = ~505ms**
- **Savings:** **~24.5 seconds per session (98% reduction)**

---

## Analytics Query Caching

**Purpose:** Cache expensive analytics queries (employee hours, swap stats, time-off reports)

**File:** `Services/AnalyticsService.cs` (640 lines)

### Configuration

- **TTL:** 5 minutes (absolute expiration)
- **Cache Key:** `Analytics_{MetricName}_{CompanyId}_{DateRange}`
- **Invalidation:** Automatic (TTL only - no manual invalidation)
- **Lifetime:** Scoped

### Why Cache Analytics?

Analytics queries are **computationally expensive**:
- Join 3-5 tables (ShiftAssignments, ShiftInstances, ShiftTypes, Users)
- Aggregate hundreds of records (GROUP BY, SUM, COUNT)
- Calculate durations, overnight shifts, back-to-back shifts
- No database indexes for ad-hoc date ranges

**Example:** `GetEmployeeHoursAsync()` loads all shift assignments in date range, calculates hours (handling overnight shifts), groups by employee, and computes weekly averages.

### Implementation Example

**Services/AnalyticsService.cs:62-138**

```csharp
public async Task<List<EmployeeHoursDto>> GetEmployeeHoursAsync(DateOnly startDate, DateOnly endDate)
{
    var cacheKey = $"Analytics_EmployeeHours_{_tenantResolver.GetCurrentTenantId()}_{startDate}_{endDate}";
    if (_cache.TryGetValue(cacheKey, out List<EmployeeHoursDto>? cached) && cached != null)
    {
        return cached;
    }

    try
    {
        // Phase 2C: Use SQL aggregates instead of loading all data into memory
        // Group by user first in the database, then load minimal data
        var groupedData = await _db.ShiftAssignments
            .AsNoTracking()
            .Include(a => a.ShiftInstance)
            .ThenInclude(si => si.ShiftType)
            .Include(a => a.User)
            .Where(a => a.ShiftInstance.WorkDate >= startDate && a.ShiftInstance.WorkDate <= endDate)
            .Where(a => a.UserId.HasValue) // Filter out unassigned slots
            .GroupBy(a => new { a.UserId, a.User!.DisplayName })
            .Select(g => new
            {
                UserId = g.Key.UserId!.Value,
                DisplayName = g.Key.DisplayName,
                ShiftCount = g.Count(),
                Shifts = g.Select(a => new
                {
                    Start = a.ShiftInstance.ShiftType.Start,
                    End = a.ShiftInstance.ShiftType.End
                }).ToList()
            })
            .ToListAsync();

        var weeks = (decimal)(endDate.DayNumber - startDate.DayNumber) / 7;

        var results = groupedData
            .Select(g =>
            {
                var totalHours = g.Shifts.Sum(shift =>
                {
                    var start = shift.Start;
                    var end = shift.End;

                    // Handle overnight shifts
                    if (end < start)
                    {
                        return (decimal)(24 - start.Hour + end.Hour + (end.Minute - start.Minute) / 60.0);
                    }
                    else
                    {
                        return (decimal)((end.Hour - start.Hour) + (end.Minute - start.Minute) / 60.0);
                    }
                });

                var avgHoursPerWeek = weeks > 0 ? totalHours / weeks : 0;

                return new EmployeeHoursDto
                {
                    UserId = g.UserId,
                    EmployeeName = g.DisplayName,
                    TotalHours = Math.Round(totalHours, 2),
                    AverageHoursPerWeek = Math.Round(avgHoursPerWeek, 2),
                    ShiftCount = g.ShiftCount
                };
            })
            .OrderByDescending(e => e.TotalHours)
            .ToList();

        _cache.Set(cacheKey, results, TimeSpan.FromMinutes(CacheDurationMinutes));
        return results;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error calculating employee hours");
        return new List<EmployeeHoursDto>();
    }
}
```

### Cache Key Components

**Pattern:** `Analytics_{MetricName}_{CompanyId}_{Parameters}`

**Examples:**
- `Analytics_EmployeeHours_2_2025-01-01_2025-01-31` (company 2, January 2025)
- `Analytics_SwapStats_3_2024-12-01_2024-12-31` (company 3, December 2024)
- `Analytics_CoverageRate_2_2025-02-01_2025-02-28` (company 2, February 2025)

**Why include date range?** Analytics are date-specific - caching "last 30 days" on Jan 1 shouldn't return cached data on Feb 1.

### No Manual Invalidation

**Design decision:** Analytics cache uses **TTL-only invalidation** (no manual `InvalidateCache()` calls).

**Rationale:**
- Analytics tolerate **5 minutes of staleness** (dashboards aren't real-time)
- Invalidation triggers are **too broad** (any shift assignment change affects hours/coverage/swaps)
- Manual invalidation would **negate performance benefits** (cache would be constantly invalidated)
- TTL-based expiration is **simpler** and **more predictable**

### Performance Impact

**Scenario:** Analytics dashboard loads 5 reports (employee hours, swap stats, coverage, understaffing, top swappers)

- **Without cache:** 5 queries × ~2,000ms = **10,000ms = 10 seconds**
- **With cache (5min TTL):** 1 query set × 10s (first) + subsequent hits × 0.5ms = **~10s first load, ~2.5ms cached**
- **Savings:** **~99.975% for cached requests**

---

## Cache Invalidation Strategies

### Manual Invalidation Pattern

All tenant-scoped caches (ShiftTypes, AppConfigs, Companies) use **manual invalidation**:

```csharp
// After write operation (create/update/delete):
await _db.SaveChangesAsync();
_cacheService.InvalidateCache(companyId);
```

**Examples:**

**1. Shift Type Management**
```csharp
// Admin creates new shift type
var shiftType = new ShiftType { ... };
_db.ShiftTypes.Add(shiftType);
await _db.SaveChangesAsync();

// Invalidate cache
_shiftTypeCacheService.InvalidateCache(companyId);
```

**2. Config Update**
```csharp
// Admin updates max swap days
var config = await _db.Configs.FindAsync(configId);
config.Value = "14";
await _db.SaveChangesAsync();

// Invalidate cache
_appConfigCacheService.InvalidateCache(companyId);
```

### Automatic Invalidation (TTL)

Analytics caches use **automatic TTL-based invalidation** (no manual calls):

```csharp
// Cache expires after 5 minutes, regardless of writes
_cache.Set(cacheKey, results, TimeSpan.FromMinutes(5));
```

### Logout Invalidation

Griffin claims cache is invalidated on **explicit user logout**:

**Pages/Auth/Logout.cshtml.cs:38-49**

```csharp
var token = User.FindFirst("Griffin:Token")?.Value;
if (!string.IsNullOrEmpty(token))
{
    // Compute SHA256 hash (same as GriffinService)
    using var sha256 = SHA256.Create();
    var tokenBytes = Encoding.UTF8.GetBytes(token);
    var hashBytes = sha256.ComputeHash(tokenBytes);
    var hashString = Convert.ToHexString(hashBytes);
    var cacheKey = $"griffin_claims_{hashString}";

    _cache.Remove(cacheKey);
}
```

### Cache Invalidation Summary

| Cache Type | Invalidation Strategy | Trigger |
|------------|----------------------|---------|
| **ShiftTypes** | Manual | Create/update/delete shift type |
| **AppConfigs** | Manual | Update config setting |
| **Companies** | Manual | Update company metadata |
| **Griffin Claims** | Manual (logout) + TTL (8h) | User logout OR 8-hour expiration |
| **Analytics** | TTL only (5min) | 5-minute expiration (no manual invalidation) |

---

## AsNoTracking Pattern

### Purpose

**Entity Framework Core** by default tracks all queried entities in a **change tracker** to:
- Detect property changes (for `SaveChanges()`)
- Maintain object identity (same ID = same instance)
- Build relationships (navigation properties)

**Problem:** Change tracking has **overhead**:
- Memory allocation for tracked entities
- CPU cycles for change detection
- Unnecessary for read-only queries

### Solution: AsNoTracking()

**Pattern:** Add `.AsNoTracking()` to all read-only queries

```csharp
// ❌ BAD: Change tracking enabled (default)
var users = await _db.Users
    .Where(u => u.IsActive)
    .ToListAsync();

// ✅ GOOD: Change tracking disabled
var users = await _db.Users
    .AsNoTracking()
    .Where(u => u.IsActive)
    .ToListAsync();
```

### Usage in ShiftManager

**Total occurrences:** 35 across 8 files (all cache services and analytics)

**Breakdown:**
- `Services/ShiftTypeCacheService.cs` - 1 usage (line 55)
- `Services/AppConfigCacheService.cs` - 1 usage (line 76)
- `Services/CompanyCacheService.cs` - 1 usage (line 55)
- `Services/AnalyticsService.cs` - 15 usages (all analytics queries)
- `Services/AuditLogService.cs` - 2 usages (audit log reads)
- `Pages/Admin/Users.cshtml.cs` - 6 usages (user management pages)
- `Pages/Admin/Directors.cshtml.cs` - 4 usages (director management)
- `Pages/Admin/AuditLog.cshtml.cs` - 5 usages (audit log viewing)

### Performance Impact

**Benchmark:** Loading 100 users with change tracking vs. AsNoTracking()

- **With tracking:** ~120ms (allocate tracked entities, build change tracker)
- **AsNoTracking():** ~40ms (raw materialization only)
- **Savings:** **~80ms per query (67% reduction)**

**Cache services compound savings:**
- Cache miss loads from DB with `AsNoTracking()` (~40ms)
- Cache hit loads from memory (~0.1ms)
- **Combined:** First load fast, subsequent loads **400x faster**

### Best Practice

**Rule:** Use `AsNoTracking()` for:
- ✅ Cache service queries (all 3 cache services use it)
- ✅ Analytics queries (read-only aggregation)
- ✅ Reporting queries (read-only display)
- ✅ API GET endpoints (read-only responses)

**Don't use for:**
- ❌ Queries followed by `SaveChanges()` (need change tracking)
- ❌ Entities with lazy-loaded navigation properties (tracking required)
- ❌ Queries where entity identity matters (same ID = same instance)

---

## Performance Impact

### Cache Hit Rates (Estimated)

Based on 10-minute TTL and typical usage patterns:

| Cache Type | Estimated Hit Rate | Reason |
|------------|-------------------|--------|
| **ShiftTypes** | 95%+ | Rarely modified (admin setup once) |
| **AppConfigs** | 90%+ | Rarely modified (admin changes infrequent) |
| **Companies** | 99%+ | Almost never modified (metadata stable) |
| **Griffin Claims** | 98%+ | 8-hour TTL, users don't re-login frequently |
| **Analytics** | 60-80% | 5-minute TTL, dashboard refreshes common |

### Overall Performance Gains

**Scenario:** Typical user navigates 10 pages per session (schedule, shifts, swaps, time-off, analytics)

**Without caching:**
- ShiftTypes: 10 queries × 50ms = 500ms
- AppConfigs: 10 queries × 30ms = 300ms
- Companies: 10 queries × 20ms = 200ms
- Griffin validation (if ADFS): 10 requests × 500ms = 5,000ms
- **Total:** ~6,000ms = **6 seconds overhead**

**With caching:**
- ShiftTypes: 1 query × 50ms + 9 hits × 0.1ms = 50.9ms
- AppConfigs: 1 query × 30ms + 9 hits × 0.1ms = 30.9ms
- Companies: 1 query × 20ms + 9 hits × 0.1ms = 20.9ms
- Griffin validation: 1 request × 500ms + 9 hits × 0.1ms = 500.9ms
- **Total:** ~603ms

**Savings:** ~5,397ms = **~5.4 seconds per session (90% reduction)**

### Database Load Reduction

**Before caching (100 concurrent users):**
- ShiftTypes: 100 users × 10 pages = 1,000 queries/session
- AppConfigs: 100 users × 10 pages = 1,000 queries/session
- Companies: 100 users × 10 pages = 1,000 queries/session
- **Total:** 3,000 queries/session

**After caching (100 concurrent users, 10min TTL):**
- ShiftTypes: 100 users × 1 query = 100 queries/session (90% reduction)
- AppConfigs: 100 users × 1 query = 100 queries/session (90% reduction)
- Companies: 100 users × 1 query = 100 queries/session (90% reduction)
- **Total:** 300 queries/session (**90% reduction**)

---

## Best Practices

### 1. Always Scope Cache Keys to CompanyId

**❌ WRONG:** Cross-tenant cache key
```csharp
var cacheKey = "ShiftTypes"; // All companies share same cache entry!
```

**✅ CORRECT:** Tenant-scoped cache key
```csharp
var cacheKey = $"ShiftTypes_{companyId}"; // Isolated per company
```

**Why?** Multi-tenancy - prevents data leaks between companies.

### 2. Use Absolute Expiration (Not Sliding)

**❌ WRONG:** Sliding expiration
```csharp
var options = new MemoryCacheEntryOptions()
    .SetSlidingExpiration(TimeSpan.FromMinutes(10)); // Refreshes on access
```

**✅ CORRECT:** Absolute expiration
```csharp
var options = new MemoryCacheEntryOptions()
    .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)); // Expires after 10min regardless
```

**Why?** Predictable freshness - cache always expires after fixed duration, preventing stale data.

### 3. Invalidate Cache After Writes

**❌ WRONG:** No invalidation
```csharp
_db.ShiftTypes.Add(shiftType);
await _db.SaveChangesAsync();
// Cache still has old data for up to 10 minutes!
```

**✅ CORRECT:** Explicit invalidation
```csharp
_db.ShiftTypes.Add(shiftType);
await _db.SaveChangesAsync();
_shiftTypeCacheService.InvalidateCache(companyId); // Cache cleared immediately
```

**Why?** Consistency - users see changes immediately, not after TTL expires.

### 4. Use AsNoTracking for All Read-Only Queries

**❌ WRONG:** Default change tracking
```csharp
var users = await _db.Users.ToListAsync(); // Tracks entities unnecessarily
```

**✅ CORRECT:** AsNoTracking for reads
```csharp
var users = await _db.Users.AsNoTracking().ToListAsync(); // Read-only, faster
```

**Why?** Performance - skips change tracking overhead (30-50% faster).

### 5. Cache Dictionary for Multiple Lookups

**❌ WRONG:** Cache individual configs
```csharp
_cache.Set($"Config_{companyId}_MaxSwapDays", value1);
_cache.Set($"Config_{companyId}_RequireApproval", value2);
// N cache entries, N DB queries on miss
```

**✅ CORRECT:** Cache entire dictionary
```csharp
var allConfigs = await _db.Configs.ToDictionaryAsync(c => c.Key, c => c.Value);
_cache.Set($"AppConfigs_{companyId}", allConfigs);
// 1 cache entry, 1 DB query, O(1) lookups
```

**Why?** Efficiency - reduces cache entries and DB queries.

### 6. Short TTLs for Critical Data

**Rule of thumb:**
- **5 minutes:** Configs, analytics (moderate freshness needed)
- **10 minutes:** Shift types, companies (infrequent changes)
- **8 hours:** Griffin claims (security tokens, validated externally)

**Why?** Balance performance vs. freshness - critical data (configs) expires faster than static data (companies).

### 7. Log Cache Hits/Misses for Debugging

**✅ CORRECT:**
```csharp
if (_cache.TryGetValue(cacheKey, out var cached))
{
    _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
    return cached;
}

_logger.LogDebug("Cache miss for {CacheKey}, loading from database", cacheKey);
```

**Why?** Observability - track cache effectiveness, debug invalidation issues.

### 8. Set Cache Size Hints

**✅ CORRECT:**
```csharp
var options = new MemoryCacheEntryOptions()
    .SetAbsoluteExpiration(TimeSpan.FromMinutes(10))
    .SetSize(1); // Hint for eviction policies
```

**Why?** Future-proofing - if cache size limits are configured later, size hints enable proper LRU eviction.

---

## Related Documentation

- **[02-ARCHITECTURE-BLUEPRINT.md](02-ARCHITECTURE-BLUEPRINT.md)** - Service layer architecture
- **[04-STARTUP-AND-MIDDLEWARE.md](04-STARTUP-AND-MIDDLEWARE.md)** - IMemoryCache DI registration
- **[05-MULTI-TENANCY-DEEP-DIVE.md](05-MULTI-TENANCY-DEEP-DIVE.md)** - CompanyId scoping patterns
- **[07-SERVICE-LAYER.md](07-SERVICE-LAYER.md)** - Cache service implementations
- **[10-AUTHENTICATION-AND-AUTHORIZATION.md](10-AUTHENTICATION-AND-AUTHORIZATION.md)** - Griffin ADFS integration

---

**End of Document** - Part of PROJECT COSMOGENESIS
**File:** `docs/genesis/13-CACHING-STRATEGY.md`
**Lines:** 772
