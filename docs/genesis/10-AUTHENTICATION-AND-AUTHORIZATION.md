# 10-AUTHENTICATION-AND-AUTHORIZATION.md - Authentication Flows and Authorization

**Part of the ShiftManager Genesis Documentation**
**Document 10 of 19 - Complete auth architecture, roles, and session management**

---

## Table of Contents

1. [Overview](#overview)
2. [Authentication Architecture](#authentication-architecture)
3. [Standard Login Flow](#standard-login-flow)
4. [Post-Login Routing (Role-Based Home Pages)](#post-login-routing-role-based-home-pages)
5. [Griffin ADFS Integration (SAML SSO)](#griffin-adfs-integration-saml-sso)
6. [Password Hashing (PBKDF2)](#password-hashing-pbkdf2)
7. [Session Management](#session-management)
8. [Role-Based Authorization](#role-based-authorization)
9. [Authorization Policies](#authorization-policies)
10. [Security Features](#security-features)
11. [Account Lockout](#account-lockout)
12. [Rate Limiting](#rate-limiting)
13. [Claims Structure](#claims-structure)
14. [Security Considerations](#security-considerations)

---

## Overview

ShiftManager implements a **dual authentication system**:

1. **Standard Login** - Email/password with PBKDF2 hashing
2. **Griffin ADFS** - SAML-based Single Sign-On (SSO) for enterprise

**Authentication Flow Summary:**

```
┌─────────────────┐
│ User navigates  │
│ to /Login       │
└────────┬────────┘
         │
    ┌────▼─────┐
    │ Choice:  │
    └────┬─────┘
         │
    ┌────▼────────────────────┐
    │                         │
┌───▼────────┐     ┌──────────▼──────┐
│ Standard   │     │ Griffin ADFS    │
│ Login      │     │ (SSO)           │
│            │     │                 │
│ Email/Pass │     │ SAML Redirect   │
└────┬───────┘     └────────┬────────┘
     │                      │
     │                      │
┌────▼────────┐    ┌────────▼────────┐
│ PBKDF2      │    │ Token Validation│
│ Verify      │    │ + Claims Fetch  │
└────┬────────┘    └────────┬────────┘
     │                      │
     └──────────┬───────────┘
                │
       ┌────────▼─────────┐
       │ Create Claims:   │
       │ • NameIdentifier │
       │ • CompanyId      │
       │ • Role           │
       │ • Email          │
       └────────┬─────────┘
                │
       ┌────────▼─────────┐
       │ Set Cookie:      │
       │ .AspNetCore.     │
       │ Cookies          │
       │ (7-day sliding)  │
       └────────┬─────────┘
                │
       ┌────────▼─────────┐
       │ Redirect to      │
       │ /Home/Index      │
       └──────────────────┘
```

**Key Characteristics:**
- ✅ **Cookie-based** - No JWT tokens
- ✅ **PBKDF2 hashing** - 100,000 iterations, SHA256
- ✅ **Account lockout** - 10 failed attempts = 30-minute lockout
- ✅ **Rate limiting** - 10 login attempts per 15 minutes per IP
- ✅ **Multi-tenancy** - CompanyId claim for tenant isolation
- ✅ **Role-based authorization** - 6 roles with granular policies
- ✅ **SAML SSO support** - Griffin ADFS integration
- ✅ **Session timeout** - 7 days sliding expiration with client-side warning
- ✅ **Auto-provisioning** - Optional user creation from ADFS claims

---

## Authentication Architecture

### Authentication Methods

| Method | Protocol | Storage | Expiration | Use Case |
|--------|----------|---------|------------|----------|
| **Standard Login** | Cookie | `.AspNetCore.Cookies` | 7 days (sliding) | Internal users, air-gapped deployments |
| **Griffin ADFS** | SAML 2.0 | `griffin.token` cookie | 8 hours (cached) | Enterprise SSO, Active Directory integration |

### Middleware Pipeline

From `Program.cs:215-222`:
```csharp
// AUTHENTICATION MIDDLEWARE PIPELINE
app.Use(async (context, next) => { /* Error handling */ });
app.UseGriffinAuthentication();         // 1. Griffin ADFS token validation (if present)
app.UseCompanyContext();                 // 2. Resolve CompanyId (from claims)
app.UseAuthentication();                 // 3. ASP.NET Core cookie authentication
app.UseAuthorization();                  // 4. Policy/role enforcement
```

**Execution Order:**
1. **GriffinAuthenticationMiddleware** - Validates Griffin token cookie, sets `context.User` if valid
2. **CompanyContextMiddleware** - Extracts CompanyId from claims, caches in `HttpContext.Items`
3. **UseAuthentication()** - Validates `.AspNetCore.Cookies`, deserializes claims
4. **UseAuthorization()** - Checks `[Authorize]` attributes and policies

---

## Standard Login Flow

**Location:** `Pages/Auth/Login.cshtml.cs` (234 lines)

### Step-by-Step Process

#### Step 1: User Submits Login Form

**POST /Auth/Login**
```csharp
[BindProperty] public string Email { get; set; } = string.Empty;
[BindProperty] public string Password { get; set; } = string.Empty;
```

#### Step 2: Rate Limiting Check

From `Login.cshtml.cs:89-98`:
```csharp
// SECURITY: Rate limiting (10 attempts per 15 minutes per IP)
var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
var rateLimitKey = $"login:{ipAddress}";

if (!_rateLimiting.IsAllowed(rateLimitKey, 10, 15))
{
    _logger.LogWarning("Rate limit exceeded for login from IP: {IP}", ipAddress);
    Error = _localizer["Error_Login_RateLimitExceeded"];
    return Page();
}
```

**Rate Limit:** 10 attempts per 15 minutes per IP address

#### Step 3: Input Validation

From `Login.cshtml.cs:100-119`:
```csharp
// Validate required fields
if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
{
    Error = _localizer["Error_Login_FieldsRequired"];
    return Page();
}

// Validate length
if (Email.Length > 255 || Password.Length > 500)
{
    _logger.LogWarning("Login attempt with oversized input from IP {IP}", ipAddress);
    Error = _localizer["Error_InvalidInput"];
    return Page();
}

// Validate email format (regex)
if (!_validation.IsValidEmail(Email))
{
    Error = _localizer["Error_InvalidEmailFormat"];
    return Page();
}
```

**Validation Rules:**
- Email: max 255 characters, valid email regex
- Password: max 500 characters
- Both required

#### Step 4: User Lookup

From `Login.cshtml.cs:121-139`:
```csharp
var user = await _db.Users
    .IgnoreQueryFilters() // IMPORTANT: Allow login across all companies
    .FirstOrDefaultAsync(u => u.Email == Email && u.IsActive);

// Check if account is locked out
if (user != null)
{
    if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
    {
        var remainingMinutes = (int)(user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes + 1;
        _logger.LogWarning("Login attempt for locked account: {Email}. Lockout ends in {Minutes} minutes",
            Email, remainingMinutes);
        Error = _localizer["Error_Login_AccountLocked", remainingMinutes];
        return Page();
    }

    user.LastLoginAttempt = DateTime.UtcNow;
}
```

**Key Points:**
- `IgnoreQueryFilters()` - Allows users from any company to login (multi-tenancy)
- `IsActive` check - Inactive users cannot login
- Account lockout enforced BEFORE password verification

#### Step 5: Password Verification

From `Login.cshtml.cs:142-170`:
```csharp
// Verify password using PBKDF2
if (user == null || !PasswordHasher.Verify(Password, user.PasswordHash, user.PasswordSalt))
{
    // Track failed login attempts
    if (user != null)
    {
        user.FailedLoginAttempts++;

        // Lock account after 10 failed attempts
        if (user.FailedLoginAttempts >= 10)
        {
            user.LockoutEnd = DateTime.UtcNow.AddMinutes(30);
            await _db.SaveChangesAsync();

            _logger.LogWarning("Account locked for {Email} after {Attempts} failed attempts",
                Email, user.FailedLoginAttempts);
            Error = _localizer["Error_Login_AccountLockedAfterAttempts"];
            return Page();
        }

        await _db.SaveChangesAsync();
        _logger.LogWarning("Login failed for {Email} (attempt {Attempt}/10)", Email, user.FailedLoginAttempts);
    }
    else
    {
        _logger.LogWarning("Login failed for {Email} (user not found)", Email);
    }

    Error = _localizer["Error_Login_InvalidCredentials"];
    return Page();
}
```

**Security:**
- **Constant-time comparison** - `PasswordHasher.Verify()` uses `CryptographicOperations.FixedTimeEquals()`
- **Failed attempt tracking** - Incremented even if user not found (logged separately)
- **Generic error message** - "Invalid credentials" (doesn't reveal if email exists)

#### Step 6: Successful Login - Create Claims

From `Login.cshtml.cs:172-196`:
```csharp
// Reset failed attempts and rate limit on successful login
user.FailedLoginAttempts = 0;
user.LockoutEnd = null;
await _db.SaveChangesAsync();

// Reset rate limit for this IP
_rateLimiting.Reset(rateLimitKey);

var claims = new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Name, user.DisplayName),
    new Claim(ClaimTypes.Role, user.Role.ToString()),
    new Claim("CompanyId", user.CompanyId.ToString())
};

var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
    new ClaimsPrincipal(identity));

_logger.LogInformation("User {UserId} ({Email}) signed in successfully. Role={Role}",
    user.Id, user.Email, user.Role);

if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
    return Redirect(returnUrl);

// ✅ PHASE 18: Role-based routing - Owner uses old home, others use new redesigned home
if (user.Role == UserRole.Owner)
    return RedirectToPage("/Home/Index");
else
    return Redirect("/");
```

**Claims Created:**
- `NameIdentifier` - User.Id (primary identifier)
- `Name` - DisplayName (shown in UI)
- `Role` - UserRole enum (Owner, Manager, etc.)
- `CompanyId` - Tenant isolation key

**Cookie Configuration:**

From `Program.cs:48-60`:
```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.HttpOnly = true;
    });
```

**Cookie Settings:**
- **Name:** `.AspNetCore.Cookies`
- **Expiration:** 7 days sliding (resets on each request)
- **SameSite:** Strict (CSRF protection)
- **HttpOnly:** true (prevents JavaScript access)
- **Secure:** SameAsRequest (HTTPS in production)

---

## Post-Login Routing (Role-Based Home Pages)

**Implemented:** Phase 18 (2026-01-06)

### Routing Strategy

ShiftManager implements **role-based post-login routing** to provide different user experiences:

| Role | Redirect Path | Page | Experience |
|------|---------------|------|------------|
| **Owner** | `/Home/Index` | `Pages/Home/Index.cshtml` | Classic dashboard (company config, analytics) |
| **All Others** | `/` | `Pages/Index.cshtml` | New redesigned dashboard (mission overview, quick actions) |

**Rationale:**
- **Owners** need quick access to system configuration, backups, Griffin setup → kept classic dashboard
- **Employees, Managers, Directors** benefit from simplified, mission-focused UI → new dashboard
- Maintains UX consistency for power users while modernizing experience for daily users

### Implementation

From `Pages/Auth/Login.cshtml.cs:290-294`:
```csharp
// ✅ PHASE 18: Role-based routing - Owner uses old home, others use new redesigned home
if (user.Role == UserRole.Owner)
    return RedirectToPage("/Home/Index");  // Classic dashboard
else
    return Redirect("/");                  // New redesigned dashboard
```

**Key Design Decision:** Uses `Redirect("/")` instead of `RedirectToPage("/Index")` for non-Owner users

**Why `Redirect("/")` vs `RedirectToPage("/Index")`?**

The implementation uses **HTTP redirect** (`Redirect("/")`) instead of **Razor Pages routing** (`RedirectToPage("/Index")`) for non-Owner users. This was necessary to avoid namespace resolution issues with the ASP.NET Core Razor Pages routing system.

**Technical Details:**
- `RedirectToPage("/Index")` relies on the Razor Pages routing convention to resolve page model namespaces
- With multiple `IndexModel` classes in the project (`ShiftManager.Pages.IndexModel`, `ShiftManager.Pages.Home.IndexModel`, `ShiftManager.Pages.My.IndexModel`, etc.), the routing engine requires fully-qualified model names in the `@model` directive
- `Redirect("/")` performs a standard HTTP 302 redirect to the root URL, which the routing middleware then maps to `Pages/Index.cshtml` unambiguously

**Files Modified:**
- `Pages/Index.cshtml.cs` - Added `namespace ShiftManager.Pages;` (line 7)
- `Pages/Index.cshtml` - Changed `@model IndexModel` → `@model ShiftManager.Pages.IndexModel` (line 2)
- `Pages/Auth/Login.cshtml.cs` - Added role-based redirect logic (lines 290-294)
- `Pages/Shared/_Layout.cshtml` - Conditional home button based on role (lines 108-123 for admins, 185-188 for employees)

### Sidebar Navigation

The sidebar home button also adapts based on role:

**For Owners:**
```cshtml
<a href="/Home/Index" class="app-sidebar-nav-item">
    <span class="app-sidebar-nav-icon">🏠</span>
    <span><loc key="Home" /></span>
</a>
```

**For Other Roles:**
```cshtml
<a href="/" class="app-sidebar-nav-item">
    <span class="app-sidebar-nav-icon">🏠</span>
    <span><loc key="Home" /></span>
</a>
```

From `Pages/Shared/_Layout.cshtml:108-123`:
```cshtml
@if (isOwner)
{
    @* Owner uses old home page *@
    <a href="/Home/Index" class="app-sidebar-nav-item @(currentPath.StartsWith("/Home") ? "active" : "")">
        <span class="app-sidebar-nav-icon">🏠</span>
        <span><loc key="Home" /></span>
    </a>
}
else
{
    @* Non-Owner admins (Manager, Director) use new redesigned home *@
    <a href="/" class="app-sidebar-nav-item @(currentPath == "/" || currentPath.StartsWith("/Index") ? "active" : "")">
        <span class="app-sidebar-nav-icon">🏠</span>
        <span><loc key="Home" /></span>
    </a>
}
```

### New Dashboard Features

**Location:** `Pages/Index.cshtml` (173 lines) + `Pages/Index.cshtml.cs` (130 lines)

The new redesigned dashboard (`/`) provides:

**Admin View (Manager, Director, Assigner):**
- **Stat Cards:**
  - Upcoming Shifts (next 7 days, team-wide)
  - Pending Requests (time-off + swap requests)
  - Team Members (count)
  - Unread Notifications
- **Quick Actions:**
  - View Calendar
  - Manage Requests
  - View Team
  - View Analytics

**Employee View:**
- **Stat Cards:**
  - My Upcoming Shifts (next 7 days)
  - My Pending Requests
  - Team Members (count)
  - Unread Notifications
- **Next Shift Highlight:**
  - Shows next scheduled shift with date and shift type
  - Empty state if no upcoming shifts
- **Quick Actions:**
  - View Schedule
  - Submit Request
  - View Team
  - My Profile

**UI Characteristics:**
- **Data-driven:** All metrics loaded from database via `IndexModel.OnGetAsync()`
- **Role-aware:** Conditional rendering based on `Model.IsAdmin` flag
- **Localized:** All labels use `<loc key="..." />` helper
- **v2 Design:** Uses `data-ui-version="v2"` for CSS scoping

### Routing Troubleshooting

**Common Issue:** "No page named '/' matches the supplied values"

**Root Causes:**
1. **Missing namespace** in `Pages/Index.cshtml.cs` - Must include `namespace ShiftManager.Pages;`
2. **Ambiguous @model directive** in `Pages/Index.cshtml` - Must use fully-qualified name `@model ShiftManager.Pages.IndexModel`
3. **Using RedirectToPage("/")** instead of `Redirect("/")` - Razor Pages routing can fail with namespace collisions

**Solution:**
```csharp
// ✅ CORRECT
return Redirect("/");

// ❌ INCORRECT (may fail with namespace resolution)
return RedirectToPage("/");
return RedirectToPage("/Index");
```

---

## Griffin ADFS Integration (SAML SSO)

**Why Griffin?**
- Enterprise SSO for large organizations
- Active Directory integration
- Centralized user management
- No password storage for Griffin users

### Griffin Flow Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Griffin ADFS Login Flow                       │
└─────────────────────────────────────────────────────────────────┘

1. User clicks "Login with Griffin ADFS"
   ↓
2. POST /Auth/Login?handler=Griffin
   ↓
3. Build authentication URL:
   https://griffin.example.com/authentication?tokenConsumerURL=...
   ↓
4. Redirect to Griffin ADFS
   ↓
5. User authenticates with Active Directory
   ↓
6. Griffin redirects back to callback:
   https://app.example.com/Auth/GriffinCallback?token=abc123
   ↓
7. Validate token via Griffin API:
   GET https://griffin.example.com/authorization/validate?token=abc123
   ↓
8. Fetch claims via Griffin API:
   GET https://griffin.example.com/authorization/getClaims?token=abc123
   ↓
9. Parse claims (UPN, DisplayName, sAMAccountName, etc.)
   ↓
10. Lookup user by UPN (email)
    ↓
11. If not found:
    - Auto-provision (if enabled)
    - OR show "pending approval" message
    ↓
12. Create ClaimsPrincipal with Griffin claims
    ↓
13. Set cookies:
    - .AspNetCore.Cookies (session)
    - griffin.token (for middleware re-validation)
    ↓
14. Redirect to /Home/Index
```

### Griffin Service Implementation

**Location:** `Services/GriffinService.cs` (267 lines)

#### Key Methods

**1. Build Authentication URL**

From `GriffinService.cs:39-44`:
```csharp
public string BuildAuthenticationUrl(string griffinBaseUrl, string tokenConsumerUrl)
{
    // Double URL-encode the token consumer URL as per Griffin documentation
    var encoded = Uri.EscapeDataString(Uri.EscapeDataString(tokenConsumerUrl));
    return $"{griffinBaseUrl.TrimEnd('/')}/authentication?tokenConsumerURL={encoded}";
}
```

**Example URL:**
```
https://griffin.example.com/authentication?tokenConsumerURL=https%253A%252F%252Fapp.example.com%252FAuth%252FGriffinCallback
```

**2. Validate Token**

From `GriffinService.cs:46-77`:
```csharp
public async Task<bool> ValidateTokenAsync(string token, string griffinBaseUrl, int timeoutSeconds)
{
    try
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        var url = $"{griffinBaseUrl.TrimEnd('/')}/authorization/validate?token={Uri.EscapeDataString(token)}";

        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Griffin token validation failed with status {StatusCode}", response.StatusCode);
            return false;
        }

        var content = await response.Content.ReadAsStringAsync();
        var isValid = content.Trim().Trim('"') == "true";

        return isValid;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error validating Griffin token");
        return false;
    }
}
```

**3. Get Claims**

From `GriffinService.cs:79-116`:
```csharp
public async Task<GriffinClaimsDto?> GetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds)
{
    using var client = _httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

    var url = $"{griffinBaseUrl.TrimEnd('/')}/authorization/getClaims?token={Uri.EscapeDataString(token)}";
    var response = await client.GetAsync(url);

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("Griffin getClaims failed with status {StatusCode}", response.StatusCode);
        return null;
    }

    var content = await response.Content.ReadAsStringAsync();
    var claims = JsonSerializer.Deserialize<GriffinClaimsDto>(content, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    return claims;
}
```

**GriffinClaimsDto Schema:**
```csharp
public class GriffinClaimsDto
{
    public string UPN { get; set; }                  // Email (user@domain.com)
    public string sAMAccountName { get; set; }       // Active Directory username
    public string DisplayName { get; set; }
    public string GivenName { get; set; }
    public string Surname { get; set; }
    public string auth_time { get; set; }            // Timestamp of authentication
}
```

**4. Authenticate User (Main Entry Point)**

From `GriffinService.cs:156-220`:
```csharp
public async Task<ClaimsPrincipal?> AuthenticateUserAsync(string token, GriffinConfig config, string ipAddress)
{
    // 1. Validate and get claims (with caching)
    var griffinClaims = await ValidateAndGetClaimsAsync(token, config.BaseUrl!, config.TimeoutSeconds);
    if (griffinClaims == null)
    {
        _logger.LogWarning("Griffin authentication failed: invalid token or claims");
        return null;
    }

    // 2. Validate required claims
    if (string.IsNullOrWhiteSpace(griffinClaims.UPN) || string.IsNullOrWhiteSpace(griffinClaims.sAMAccountName))
    {
        _logger.LogError("Griffin claims missing required fields (UPN or sAMAccountName)");
        return null;
    }

    // 3. Lookup user by UPN (email)
    var user = await _dbContext.Users
        .IgnoreQueryFilters() // Search across all companies
        .FirstOrDefaultAsync(u => u.Email.ToLower() == griffinClaims.UPN.ToLower() && u.IsActive);

    // 4. Handle auto-provisioning
    if (user == null)
    {
        if (config.AutoProvisionUsers)
        {
            user = await AutoProvisionUserAsync(griffinClaims, config);
            if (user == null)
            {
                _logger.LogError("Failed to auto-provision user for UPN: {UPN}", griffinClaims.UPN);
                return null;
            }
        }
        else
        {
            _logger.LogInformation("User {UPN} not found and auto-provisioning disabled", griffinClaims.UPN);
            return null; // Will show pending approval message
        }
    }

    // 5. Build ClaimsPrincipal
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, griffinClaims.DisplayName),
        new Claim(ClaimTypes.Email, griffinClaims.UPN),
        new Claim(ClaimTypes.GivenName, griffinClaims.GivenName),
        new Claim(ClaimTypes.Surname, griffinClaims.Surname),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        new Claim("CompanyId", user.CompanyId.ToString()),
        new Claim("AuthMethod", "Griffin"),
        new Claim("Griffin:sAMAccountName", griffinClaims.sAMAccountName),
        new Claim("Griffin:Token", token),
        new Claim("Griffin:AuthTime", griffinClaims.auth_time),
        new Claim("AuthTimestamp", DateTime.UtcNow.ToString("o"))
    };

    var identity = new ClaimsIdentity(claims, "Griffin");
    var principal = new ClaimsPrincipal(identity);

    _logger.LogInformation("Griffin authentication successful for user {UserId} ({Email})", user.Id, user.Email);

    return principal;
}
```

**5. Auto-Provisioning**

From `GriffinService.cs:222-257`:
```csharp
private async Task<AppUser?> AutoProvisionUserAsync(GriffinClaimsDto griffinClaims, GriffinConfig config)
{
    try
    {
        var user = new AppUser
        {
            CompanyId = config.CompanyId,
            Email = griffinClaims.UPN,
            DisplayName = griffinClaims.DisplayName,
            Role = config.DefaultProvisionedRole,    // e.g., Employee
            IsActive = true,
            PasswordHash = Array.Empty<byte>(),       // No password for Griffin users
            PasswordSalt = Array.Empty<byte>()
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Auto-provisioned Griffin user: {Email} with role {Role}",
            user.Email, user.Role);

        // Audit log
        await _auditLogService.LogSystemActionAsync(
            "GriffinAutoProvision",
            "AppUser",
            user.Id,
            $"Auto-provisioned Griffin user: {user.Email}",
            $"Role={user.Role}, CompanyId={user.CompanyId}, UPN={griffinClaims.UPN}");

        return user;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to auto-provision user for UPN: {UPN}", griffinClaims.UPN);
        return null;
    }
}
```

**Configuration:**
- `AutoProvisionUsers` - If true, creates user on first login
- `DefaultProvisionedRole` - Role assigned to new users (e.g., `Employee`)
- If false, user must be pre-created or join request approved

### Griffin Authentication Middleware

**Location:** `Middleware/GriffinAuthenticationMiddleware.cs` (127 lines)

**Purpose:** Automatically re-validate Griffin token on each request (optional, for session continuity)

From `GriffinAuthenticationMiddleware.cs:24-90`:
```csharp
public async Task InvokeAsync(
    HttpContext context,
    IGriffinConfigService griffinConfigService,
    IGriffinService griffinService)
{
    // 1. Skip anonymous paths
    if (IsAnonymousPath(context.Request.Path))
    {
        await _next(context);
        return;
    }

    // 2. Load Griffin config (try to extract CompanyId from existing cookie)
    GriffinConfig? griffinConfig = null;
    try
    {
        griffinConfig = await TryGetGriffinConfigAsync(context, griffinConfigService);
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "Unable to load Griffin config at middleware stage");
    }

    // 3. Skip if Griffin disabled or config unavailable
    if (griffinConfig?.Enabled != true)
    {
        await _next(context);
        return;
    }

    // 4. Extract Griffin token from cookie
    var token = context.Request.Cookies["griffin.token"];
    if (string.IsNullOrEmpty(token))
    {
        await _next(context);
        return;
    }

    // 5. Authenticate user
    try
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var principal = await griffinService.AuthenticateUserAsync(token, griffinConfig, ipAddress);

        if (principal != null)
        {
            context.User = principal;  // Override context.User
            _logger.LogDebug("Griffin authentication successful for user {UserId}",
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
        else
        {
            // Invalid token - clear cookie
            context.Response.Cookies.Delete("griffin.token");
            _logger.LogWarning("Invalid Griffin token, cookie cleared");
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Griffin authentication failed");
        context.Response.Cookies.Delete("griffin.token");
    }

    await _next(context);
}
```

**Why This Middleware?**
- **Session persistence** - Validates Griffin token on each request
- **Automatic re-auth** - If standard cookie expires but Griffin token valid, re-authenticates
- **Graceful fallback** - If token invalid, clears cookie and falls back to standard auth

### Griffin Configuration

**Table:** `GriffinConfigs` (one row per company)

```sql
CREATE TABLE GriffinConfigs (
    Id INTEGER PRIMARY KEY,
    CompanyId INTEGER UNIQUE,
    BaseUrl TEXT,                       -- https://griffin.example.com
    TokenConsumerUrl TEXT,              -- https://app.example.com/Auth/GriffinCallback
    AutoProvisionUsers BOOLEAN,         -- Auto-create users on first login
    DefaultProvisionedRole INTEGER,     -- Role for auto-created users (Employee=4)
    TimeoutSeconds INTEGER,             -- HTTP timeout (default: 30)
    Enabled BOOLEAN,
    CreatedAt DATETIME
);
```

**Admin UI:** `/Owner/GriffinConfig` (Owner-only page)

### Comprehensive ADFS Integration Analysis

**See Also:** [ADFS-INTEGRATION-ANALYSIS.md](ADFS-INTEGRATION-ANALYSIS.md) for comprehensive analysis including:
- ✅ **Core Questions Answered**: Authorization mechanism, Multi-tenancy handling, ADFS scope configuration
- ✅ **Expanded Analysis**: Token flow, role/profile synchronization, error handling, scalability
- ✅ **Proactive Gap Analysis**: 5 critical questions covering:
  - Token revocation and expiration management
  - Monitoring and alerting gaps
  - Disaster recovery scenarios
  - Compliance and audit trail requirements
  - Configuration management best practices
- ✅ **Prioritized Recommendations**: 6 actionable improvements with effort estimates
- ✅ **Architecture Diagrams**: Complete authentication flows and multi-tenancy integration

**Investigation Date:** 2026-01-03
**Assessment Grade:** B+ (Very Good with Minor Gaps)

---

## Password Hashing (PBKDF2)

**Location:** `Models/PasswordHasher.cs` (22 lines)

### PBKDF2 Implementation

**Algorithm:** PBKDF2 (Password-Based Key Derivation Function 2)
**Hash Function:** SHA-256
**Iterations:** 100,000
**Salt Size:** 16 bytes (128 bits)
**Hash Size:** 32 bytes (256 bits)

From `PasswordHasher.cs:7-14`:
```csharp
public static (byte[] hash, byte[] salt) CreateHash(string password)
{
    using var rng = RandomNumberGenerator.Create();
    byte[] salt = new byte[16];  // 128-bit salt
    rng.GetBytes(salt);           // Cryptographically secure random bytes

    using var derive = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
    return (derive.GetBytes(32), salt);  // 256-bit hash
}
```

**Why PBKDF2?**
- ✅ **NIST-approved** - Standardized in RFC 2898
- ✅ **Salted** - Each password has unique salt
- ✅ **Slow** - 100,000 iterations makes brute-force expensive
- ✅ **Built-in** - No external dependencies
- ✅ **.NET optimized** - Hardware acceleration on supported platforms

### Password Verification

From `PasswordHasher.cs:16-21`:
```csharp
public static bool Verify(string password, byte[] hash, byte[] salt)
{
    using var derive = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
    return CryptographicOperations.FixedTimeEquals(hash, derive.GetBytes(32));
}
```

**Security Features:**
- **Fixed-time comparison** - `CryptographicOperations.FixedTimeEquals()` prevents timing attacks
- **Constant iteration count** - Same 100,000 iterations regardless of password

### Password Storage Schema

**Table:** `AppUsers`
```sql
CREATE TABLE AppUsers (
    Id INTEGER PRIMARY KEY,
    Email TEXT UNIQUE,
    PasswordHash BLOB,       -- 32 bytes (PBKDF2 output)
    PasswordSalt BLOB,       -- 16 bytes (random)
    ...
);
```

**Example:**
```json
{
  "Email": "manager@company.com",
  "PasswordHash": "0x4A7D8F2E...",  // 32 bytes
  "PasswordSalt": "0xB1C8A9F2..."   // 16 bytes
}
```

---

## Session Management

### Cookie Configuration

From `Program.cs:48-60`:
```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.HttpOnly = true;
    });
```

**Cookie Settings:**
| Setting | Value | Purpose |
|---------|-------|---------|
| `ExpireTimeSpan` | 7 days | Max session duration |
| `SlidingExpiration` | true | Resets expiration on each request |
| `SameSite` | Strict | Prevents CSRF attacks |
| `HttpOnly` | true | Prevents JavaScript access |
| `Secure` | SameAsRequest | HTTPS-only in production |
| `LoginPath` | /Auth/Login | Redirect if unauthenticated |
| `AccessDeniedPath` | /AccessDenied | Redirect if unauthorized |

### Session Timeout Warning

**Client-Side:** `wwwroot/js/session-check.js` (polls every 60 seconds)

**API Endpoint:** `GET /Api/SessionStatus`

From `Pages/Api/SessionStatus.cshtml.cs:25-60`:
```csharp
public async Task<IActionResult> OnGet()
{
    Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";

    if (!User.Identity?.IsAuthenticated ?? true)
    {
        return new JsonResult(new { authenticated = false, state = "expired" })
        { StatusCode = 401 };
    }

    var authResult = await HttpContext.AuthenticateAsync(
        CookieAuthenticationDefaults.AuthenticationScheme);
    var expiresUtc = authResult.Properties.ExpiresUtc;
    var timeRemaining = expiresUtc.HasValue ? (expiresUtc.Value - now) : TimeSpan.FromDays(7);

    string state = timeRemaining <= TimeSpan.Zero ? "expired" :
                   timeRemaining <= WarningThreshold ? "warning" : "ok";

    return new JsonResult(new {
        authenticated = true,
        state,
        secondsRemaining = (int)timeRemaining.TotalSeconds
    });
}
```

**States:**
- `"ok"` - Session healthy (> 5 minutes remaining)
- `"warning"` - Session expiring soon (≤ 5 minutes remaining)
- `"expired"` - Session expired (returns 401)

**UI Behavior:**
- If `"warning"`, show modal: "Your session is about to expire. Click to continue."
- If user inactive, redirect to `/Auth/Login?reason=sessionExpired`

---

## Role-Based Authorization

### User Roles

**Enum:** `Models/Support/UserRole.cs`

```csharp
public enum UserRole
{
    Owner = 0,       // Full system access
    Director = 1,    // Cross-company access
    Manager = 2,     // Department management
    Assigner = 3,    // Shift assignment
    Employee = 4,    // Standard user
    Trainee = 5      // Limited access (shadowing)
}
```

**Role Hierarchy (permissions descending):**

| Role | Capabilities | Examples |
|------|--------------|----------|
| **Owner** | Full system access, company config, billing, Griffin setup | CEO, System Admin |
| **Director** | Cross-company access, on-duty management, reporting | Regional Director, COO |
| **Manager** | Shift creation, approval workflows, user management | Department Manager, Team Lead |
| **Assigner** | Shift assignment, chore creation | Shift Coordinator |
| **Employee** | View schedule, request time-off/swaps, submit feedback | Standard Staff |
| **Trainee** | View-only, shadowing shifts (trainee assignments) | New Hire, Intern |

**Role Assignment:**
- Set by Owner/Director on user creation or profile edit
- Stored in `AppUsers.Role` (integer)
- Enforced via `[Authorize(Roles = "...")]` or policies

---

## Authorization Policies

### Policy Configuration

From `Program.cs:94-110`:
```csharp
builder.Services.AddAuthorization(options =>
{
    // Role-based policies
    options.AddPolicy("IsAdmin", policy => policy.RequireRole(nameof(UserRole.Owner)));
    options.AddPolicy("IsDirector", policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Director)));
    options.AddPolicy("IsOwnerOrDirector", policy => policy.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Director)));
    options.AddPolicy("IsManagerOrAdmin", policy => policy.RequireRole(
        nameof(UserRole.Manager), nameof(UserRole.Owner), nameof(UserRole.Director)));

    // Feature-based policies
    options.AddPolicy("CanViewChores", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("CanEditChores", policy => policy.RequireRole(
        nameof(UserRole.Manager), nameof(UserRole.Owner), nameof(UserRole.Director), nameof(UserRole.Assigner)));

    options.AddPolicy("CanViewOnDuty", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("CanEditOnDuty", policy => policy.RequireRole(
        nameof(UserRole.Manager), nameof(UserRole.Owner), nameof(UserRole.Director)));
});
```

### Policy Catalog

| Policy | Roles | Use Case |
|--------|-------|----------|
| **IsAdmin** | Owner | Company config, billing, Griffin setup |
| **IsDirector** | Owner, Director | Cross-company access, reporting |
| **IsOwnerOrDirector** | Owner, Director | Director management |
| **IsManagerOrAdmin** | Manager, Owner, Director | User management, approvals |
| **CanViewChores** | All authenticated | View chore assignments |
| **CanEditChores** | Manager, Owner, Director, Assigner | Create/edit chores |
| **CanViewOnDuty** | All authenticated | View on-duty schedule |
| **CanEditOnDuty** | Manager, Owner, Director | Create/edit on-duty assignments |

### Usage in Razor Pages

**Example:**
```csharp
[Authorize(Policy = "CanEditChores")]
public class ChoresModel : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        // Only Manager, Owner, Director, Assigner can access
    }
}
```

**Example:**
```csharp
[Authorize(Roles = "Owner,Director")]
public class DirectorDashboardModel : PageModel
{
    // Only Owner or Director can access
}
```

---

## Security Features

### 1. CSRF Protection

**Mechanism:** `AntiForgeryToken` validation on POST requests

**Automatic for Razor Pages:**
```cshtml
<form method="post">
    <!-- Token automatically injected -->
    <input name="__RequestVerificationToken" type="hidden" value="..." />
</form>
```

**Exception:** API endpoints with `[IgnoreAntiforgeryToken]`
```csharp
[Authorize]
[IgnoreAntiforgeryToken]  // For AJAX JSON requests
public class SaveScoreModel : PageModel
```

**Mitigation:** `SameSite=Strict` cookie prevents cross-site requests

---

### 2. SQL Injection Protection

**Entity Framework prevents SQL injection:**
```csharp
// SAFE: Parameterized query
var user = await _db.Users
    .FirstOrDefaultAsync(u => u.Email == Email && u.IsActive);
```

**Raw SQL (if needed):**
```csharp
// SAFE: Parameterized
await _db.Database.ExecuteSqlRawAsync(
    "DELETE FROM Chores WHERE CompanyId = {0} AND Date < {1}",
    companyId, cutoffDate);
```

---

### 3. XSS Protection

**Razor Pages auto-encode:**
```cshtml
<!-- SAFE: Auto-encoded -->
<p>@Model.UserInput</p>

<!-- UNSAFE: Bypass encoding (avoid!) -->
<p>@Html.Raw(Model.UserInput)</p>
```

**Content-Security-Policy header (optional):**
```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("Content-Security-Policy",
        "default-src 'self'; script-src 'self' 'unsafe-inline'");
    await next();
});
```

---

### 4. Timing Attack Prevention

**Password verification uses fixed-time comparison:**
```csharp
return CryptographicOperations.FixedTimeEquals(hash, derive.GetBytes(32));
```

**Why?**
- Regular `==` comparison short-circuits on first mismatch
- Timing differences reveal partial password correctness
- Fixed-time comparison always compares all bytes

---

### 5. Secure Random Generation

**Salt generation:**
```csharp
using var rng = RandomNumberGenerator.Create();
byte[] salt = new byte[16];
rng.GetBytes(salt);  // Cryptographically secure random bytes
```

**Why not `Random`?**
- `Random` is predictable (pseudo-random)
- `RandomNumberGenerator` uses OS entropy (unpredictable)

---

## Account Lockout

**Mechanism:** Temporary lockout after failed login attempts

### Lockout Parameters

| Parameter | Value | Location |
|-----------|-------|----------|
| **Max Failed Attempts** | 10 | `Login.cshtml.cs:149` |
| **Lockout Duration** | 30 minutes | `Login.cshtml.cs:152` |
| **Reset on Success** | Yes | `Login.cshtml.cs:173` |

### Lockout Flow

From `Login.cshtml.cs:126-158`:
```csharp
// 1. Check if account locked
if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
{
    var remainingMinutes = (int)(user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes + 1;
    _logger.LogWarning("Login attempt for locked account: {Email}. Lockout ends in {Minutes} minutes",
        Email, remainingMinutes);
    Error = _localizer["Error_Login_AccountLocked", remainingMinutes];
    return Page();
}

// 2. On failed password
if (user == null || !PasswordHasher.Verify(Password, user.PasswordHash, user.PasswordSalt))
{
    if (user != null)
    {
        user.FailedLoginAttempts++;

        // Lock account after 10 failed attempts
        if (user.FailedLoginAttempts >= 10)
        {
            user.LockoutEnd = DateTime.UtcNow.AddMinutes(30);
            await _db.SaveChangesAsync();

            _logger.LogWarning("Account locked for {Email} after {Attempts} failed attempts",
                Email, user.FailedLoginAttempts);
            Error = _localizer["Error_Login_AccountLockedAfterAttempts"];
            return Page();
        }
    }
}

// 3. Reset on successful login
user.FailedLoginAttempts = 0;
user.LockoutEnd = null;
await _db.SaveChangesAsync();
```

### Lockout Table Fields

**Table:** `AppUsers`
```sql
CREATE TABLE AppUsers (
    ...
    FailedLoginAttempts INTEGER DEFAULT 0,
    LockoutEnd DATETIME NULL,
    LastLoginAttempt DATETIME NULL
);
```

### Manual Unlock

**Admin Tool:** `UNLOCK_ADMIN_ACCOUNT.bat` (for emergency access)

```sql
UPDATE AppUsers
SET FailedLoginAttempts = 0, LockoutEnd = NULL
WHERE Email = 'admin@company.com';
```

---

## Rate Limiting

**Service:** `IRateLimitingService` (in-memory sliding window)

### Login Rate Limiting

From `Login.cshtml.cs:89-98`:
```csharp
var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
var rateLimitKey = $"login:{ipAddress}";

if (!_rateLimiting.IsAllowed(rateLimitKey, 10, 15))
{
    _logger.LogWarning("Rate limit exceeded for login from IP: {IP}", ipAddress);
    Error = _localizer["Error_Login_RateLimitExceeded"];
    return Page();
}
```

**Parameters:**
- **Key:** `login:{IP address}`
- **Limit:** 10 requests
- **Window:** 15 minutes

**Reset:**
```csharp
// Reset on successful login
_rateLimiting.Reset(rateLimitKey);
```

**Why IP-based?**
- Prevents distributed brute-force from single IP
- Does NOT prevent distributed attacks from multiple IPs (would need CAPTCHA or cloud-based WAF)

---

## Claims Structure

### Standard Login Claims

```csharp
new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),       // "5"
    new Claim(ClaimTypes.Name, user.DisplayName),                   // "John Doe"
    new Claim(ClaimTypes.Role, user.Role.ToString()),               // "Manager"
    new Claim("CompanyId", user.CompanyId.ToString())               // "2"
}
```

### Griffin Login Claims

```csharp
new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),       // "5"
    new Claim(ClaimTypes.Name, griffinClaims.DisplayName),          // "John Doe"
    new Claim(ClaimTypes.Email, griffinClaims.UPN),                 // "john.doe@company.com"
    new Claim(ClaimTypes.GivenName, griffinClaims.GivenName),       // "John"
    new Claim(ClaimTypes.Surname, griffinClaims.Surname),           // "Doe"
    new Claim(ClaimTypes.Role, user.Role.ToString()),               // "Manager"
    new Claim("CompanyId", user.CompanyId.ToString()),              // "2"
    new Claim("AuthMethod", "Griffin"),                             // Indicates SSO login
    new Claim("Griffin:sAMAccountName", griffinClaims.sAMAccountName), // "jdoe"
    new Claim("Griffin:Token", token),                              // Griffin session token
    new Claim("Griffin:AuthTime", griffinClaims.auth_time),        // "2025-06-15T10:30:00Z"
    new Claim("AuthTimestamp", DateTime.UtcNow.ToString("o"))       // Login timestamp
}
```

### Accessing Claims in Code

```csharp
var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
var companyId = int.Parse(User.FindFirst("CompanyId")!.Value);
var role = Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);
var displayName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
```

---

## Security Considerations

### 1. Password Storage

✅ **What We Do:**
- PBKDF2 with 100,000 iterations
- SHA-256 hash function
- 16-byte random salt per password
- Salts stored alongside hashes

⚠️ **Considerations:**
- **Griffin users** - No password stored (empty byte arrays)
- **Migration** - If upgrading from MD5/SHA1, rehash on next login

---

### 2. Session Hijacking

✅ **Mitigations:**
- `HttpOnly` cookies (prevents JavaScript access)
- `Secure` cookies in production (HTTPS-only)
- `SameSite=Strict` (prevents CSRF)
- 7-day expiration with sliding window

⚠️ **Remaining Risks:**
- No IP binding (user can login from different IPs)
- No device fingerprinting
- No session revocation (logout requires cookie deletion)

**Recommendation:** Add `SessionId` claim and `ActiveSessions` table for revocation

---

### 3. Brute-Force Protection

✅ **Mitigations:**
- Account lockout (10 failed attempts = 30 minutes)
- Rate limiting (10 attempts per 15 minutes per IP)
- Fixed-time password comparison (timing attack prevention)

⚠️ **Limitations:**
- IP-based rate limiting (distributed attacks bypass)
- No CAPTCHA (automated bots can attempt 10 times)

**Recommendation:** Add CAPTCHA after 3 failed attempts

---

### 4. Griffin Token Security

✅ **Mitigations:**
- Token validation on each request (via middleware)
- 8-hour cache TTL (limits stale token usage)
- Token stored in `HttpOnly` cookie
- Claims cached by SHA-256 hash of token

⚠️ **Considerations:**
- Griffin token stored in cookie (if stolen, valid for 8 hours)
- No token revocation mechanism
- Relies on Griffin ADFS for token expiration

---

### 5. Multi-Tenancy Enforcement

✅ **Mitigations:**
- `CompanyId` claim validated on every request
- Global query filters enforce `WHERE CompanyId = {current}`
- `IgnoreQueryFilters()` only in authentication code

⚠️ **Exceptions:**
- `OnDuty` entities are global (no CompanyId)
- Directors can access multiple companies (via `DirectorCompanies` table)

**See:** 05-MULTI-TENANCY-DEEP-DIVE.md for details

---

## Summary

**Authentication Methods:**
- ✅ Standard Login (email/password, PBKDF2)
- ✅ Griffin ADFS (SAML SSO, Active Directory)

**Session Management:**
- ✅ Cookie-based (7 days sliding expiration)
- ✅ Client-side timeout warning (5 minutes)
- ✅ Session status API (`/Api/SessionStatus`)

**Authorization:**
- ✅ 6 roles (Owner, Director, Manager, Assigner, Employee, Trainee)
- ✅ 8 policies (IsAdmin, CanEditChores, etc.)
- ✅ Role-based and policy-based enforcement

**Security Features:**
- ✅ PBKDF2 password hashing (100,000 iterations, SHA-256)
- ✅ Account lockout (10 failed attempts = 30 minutes)
- ✅ Rate limiting (10 attempts per 15 minutes per IP)
- ✅ Fixed-time password comparison (timing attack prevention)
- ✅ CSRF protection (SameSite=Strict cookies)
- ✅ SQL injection prevention (Entity Framework)
- ✅ XSS protection (Razor auto-encoding)
- ✅ Secure random generation (RandomNumberGenerator)

**Griffin ADFS:**
- ✅ SAML-based SSO
- ✅ Token validation + claims fetch
- ✅ Auto-provisioning (optional)
- ✅ 8-hour cache TTL
- ✅ Middleware re-validation on each request

**Key Files:**
- `Pages/Auth/Login.cshtml.cs` (234 lines) - Standard login
- `Services/GriffinService.cs` (267 lines) - Griffin integration
- `Middleware/GriffinAuthenticationMiddleware.cs` (127 lines) - Token validation
- `Models/PasswordHasher.cs` (22 lines) - PBKDF2 implementation
- `Program.cs` (lines 48-110) - Cookie + policy configuration

**Next Steps:**
- See 11-LOCALIZATION-AND-RTL.md for culture-based UI adaptation
- See 09-API-LAYER.md for API key authentication
- See 14-WORKFLOWS-AND-BUSINESS-LOGIC.md for approval workflows

---

**Document Status:** ✅ Complete
**Last Updated:** 2026-01-06 (Added Post-Login Routing documentation)
**Lines:** 1,420+
**Coverage:** All authentication methods, authorization policies, security features, role-based routing documented

**Cross-References:**
- 05-MULTI-TENANCY-DEEP-DIVE.md - CompanyId enforcement
- 06-DOMAIN-MODELS.md - AppUser, GriffinConfig schemas
- 07-SERVICE-LAYER.md - GriffinService, RateLimitingService
- 09-API-LAYER.md - API key authentication
- 14-WORKFLOWS-AND-BUSINESS-LOGIC.md - Request approval workflows
