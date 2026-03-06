# Griffin ADFS - Complete Owner Guide

**Version**: 1.0
**Date**: 2025-12-12
**Project**: ShiftManager

---

## Table of Contents

1. [What is Griffin ADFS?](#1-what-is-griffin-adfs)
2. [What We Implemented](#2-what-we-implemented)
3. [Authentication Flows](#3-authentication-flows)
4. [User Lifecycle & Identity Mapping](#4-user-lifecycle--identity-mapping)
5. [Configuration & Owner Menu](#5-configuration--owner-menu)
6. [Local Development (Non-Air-Gapped)](#6-local-development-non-air-gapped)
7. [Air-Gapped Environment Setup](#7-air-gapped-environment-setup)
8. [FAQ - Everything an Owner Might Ask](#8-faq---everything-an-owner-might-ask)
9. [Troubleshooting](#9-troubleshooting)
10. [Open Questions & Assumptions](#10-open-questions--assumptions)

---

## 1. What is Griffin ADFS?

### Technical Explanation

Griffin is a **centralized authentication service** that acts as a wrapper around Active Directory Federation Services (ADFS) for air-gapped military/government environments. It provides:

- **OAuth 2.0 / OpenID Connect** authentication flow
- **Token-based authentication** using ADFS as the identity provider
- **Centralized credential management** - Griffin service holds the ADFS client credentials
- **HTTP API endpoints** for authentication, token validation, and claims retrieval

**Architecture**:
```
[Your App] → [Griffin Service] → [ADFS Server] → [Active Directory]
```

Griffin runs as a **separate service** (typically deployed via Helm on Kubernetes/OpenShift) and handles all communication with ADFS on behalf of your application.

### Plain-Language Explanation

**Griffin is a middle-man service that lets your employees log in using their military/government credentials (CAC card or domain username/password) instead of creating separate passwords for your app.**

Think of it like this:
- **Without Griffin**: Each person needs a username and password just for your app
- **With Griffin**: People use their existing work credentials (the same ones they use to log into their computer)

Griffin only works in secure, isolated networks (called "air-gapped" environments) where there's no internet connection. This is common in military and government settings for security reasons.

**Why we use it:**
- ✅ Employees don't need to remember another password
- ✅ When someone leaves the organization, their domain account is disabled and they automatically lose access
- ✅ Centralized security - IT controls who can access what
- ✅ Meets security requirements for air-gapped environments

---

## 2. What We Implemented

### Technical Explanation

We integrated Griffin ADFS authentication into ShiftManager using a **dual-authentication approach**:

**Components Added:**

| Component | Location | Purpose |
|-----------|----------|---------|
| `GriffinConfig` model | `Models/GriffinConfig.cs` | Database storage for Griffin settings (per company) |
| `GriffinService` | `Services/GriffinService.cs` | HTTP client for Griffin API calls |
| `GriffinConfigService` | `Services/GriffinConfigService.cs` | Configuration management service |
| `GriffinAuthenticationMiddleware` | `Middleware/GriffinAuthenticationMiddleware.cs` | Request pipeline authentication |
| `GriffinCallback` page | `Pages/Auth/GriffinCallback.cshtml.cs` | Handles redirect after ADFS login |
| Localization keys | `Resources/SharedResources*.resx` | English + Hebrew UI strings |

**Authentication Flow Integration:**

```
Request → GriffinAuthenticationMiddleware → CompanyContextMiddleware →
Authentication → Authorization → API Auth → Page Handler
```

**Key Features:**
- **Always-visible login button** - "Login with ADFS" / "הזדהות במערכת היחידה"
- **Automatic fallback** - Local email/password login still works if Griffin is unavailable
- **Per-company configuration** - Each company can enable/disable Griffin independently
- **Token caching** - Reduces API calls to Griffin (8-hour cache TTL)
- **Auto-provisioning** - New users can be created automatically on first login
- **Comprehensive logging** - All authentication events logged to audit trail

**Database Changes:**
- New table: `GriffinConfigs` with columns for base URL, callback URL, provisioning settings, timeout
- No changes to existing `AppUsers` table structure

### Plain-Language Explanation

**We added a "Login with ADFS" button to the login page that lets employees sign in with their work credentials.**

Here's what was added:
- ✅ A **new button** on the login page (always visible in both English and Hebrew)
- ✅ A **settings page** in the Owner menu to turn Griffin on/off and configure it
- ✅ **Background services** that talk to Griffin and verify users are who they say they are
- ✅ **Automatic user creation** - when someone logs in via ADFS for the first time, we can create their account automatically
- ✅ **Fallback safety** - the old email/password login still works, so if Griffin breaks, nobody is locked out

**What didn't change:**
- ❌ Your existing users and passwords are untouched
- ❌ Existing authentication still works exactly the same way
- ❌ No changes to user permissions or roles

Think of it as adding a **second door** to your building - the original entrance (email/password) still works, but now employees can also use their CAC card at the new entrance (Griffin ADFS).

---

## 3. Authentication Flows

### 3.1 Login via Griffin ADFS

#### Technical Flow

**Step-by-step process:**

1. **User clicks "Login with ADFS"** on `/Auth/Login`
2. **Browser redirects** to Griffin service:
   ```
   GET http://griffin-base-url/authentication?tokenConsumerURL=https://your-app/Auth/GriffinCallback
   ```
3. **Griffin redirects** user to ADFS login page
4. **User authenticates** with domain credentials (CAC or username/password)
5. **ADFS validates** credentials against Active Directory
6. **ADFS redirects** back to Griffin with authorization code
7. **Griffin exchanges** auth code for access token with ADFS
8. **Griffin generates** a token for our app and redirects to our callback:
   ```
   GET https://your-app/Auth/GriffinCallback?token=abc123xyz
   ```
9. **Our app validates** the token by calling:
   ```
   GET http://griffin-base-url/authorization/validate?token=abc123xyz
   ```
10. **Our app retrieves** user claims:
    ```
    GET http://griffin-base-url/authorization/getClaims?token=abc123xyz
    Response: { "UPN": "john.doe@domain.mil", "DisplayName": "Doe, John", ... }
    ```
11. **Our app looks up** user by email (`UPN` field)
12. **Our app creates user** if not found and auto-provisioning is enabled
13. **Our app sets cookies**:
    - `griffin.token` (HttpOnly, 8 hours)
    - ASP.NET authentication cookie (7 days)
14. **User redirected** to dashboard or original destination

**Token Caching:**
- Token validation and claims are cached in-memory for 8 hours
- Cache key: `griffin_claims_{SHA256(token)}`
- Reduces Griffin API calls from ~100/day to ~3/day per user

**Security Measures:**
- Token stored in HttpOnly cookie (XSS protection)
- Token validated on every request (or via cache)
- Claims retrieved only once per 8-hour period
- All authentication events logged to audit trail

#### Plain-Language Explanation

**What happens when someone clicks "Login with ADFS":**

1. **You're redirected** to a login screen (might be CAC card reader or username/password)
2. **You enter your work credentials** (the same ones you use for your computer)
3. **The system checks** if you're a real employee in the company directory
4. **If valid**, you're logged into the app automatically
5. **Your session lasts** for 7 days (unless you log out)

**Timeline:**
- ⏱️ Entire login process: ~5-10 seconds
- ⏱️ Session duration: 7 days
- ⏱️ Re-authentication required: After 7 days of inactivity

**What you'll see:**
- ✅ Login button in English: "Login with ADFS"
- ✅ Login button in Hebrew: "הזדהות במערכת היחידה"
- ✅ Redirect to ADFS login page (company-branded)
- ✅ Automatic return to the app after login
- ✅ Dashboard or page you were trying to access

---

### 3.2 Login via Email/Password (Fallback)

#### Technical Flow

This flow is **unchanged** from the original implementation:

1. User enters email and password on `/Auth/Login`
2. App validates credentials against `AppUsers` table using PBKDF2 hash
3. Rate limiting applied: 10 attempts per 15 minutes per IP
4. Account lockout after 10 failed attempts (3-minute lockout)
5. On success, ASP.NET authentication cookie set (7 days, sliding expiration)
6. User redirected to dashboard

**When this is used:**
- Griffin is disabled in Owner settings
- Griffin service is unreachable
- User prefers local login
- User doesn't have ADFS credentials

#### Plain-Language Explanation

**The original login method still works exactly the same:**

1. Enter your email address
2. Enter your password
3. Click "Login"
4. You're logged in

**When to use this:**
- ✅ Griffin ADFS is not working
- ✅ You don't have work credentials (external contractor, etc.)
- ✅ You prefer using a password instead of CAC

**Important:** Both login methods work at the same time. You can choose whichever you prefer.

---

### 3.3 Token/Session Lifecycle

#### Technical Details

**Token Creation:**
- **Griffin token**: Generated by Griffin service after ADFS authentication
- **App session cookie**: Generated by our app after token validation
- **Both stored** in browser cookies (HttpOnly, Secure when HTTPS available)

**Token Validation Flow:**

```
Every Request
    ↓
GriffinAuthenticationMiddleware extracts token from cookie
    ↓
Check in-memory cache (key: griffin_claims_{hash})
    ↓ HIT (within 8 hours)
       └→ Use cached claims, skip API call
    ↓ MISS (expired or new)
       └→ Call Griffin /authorization/validate?token=xyz
          ↓ Valid
             └→ Call Griffin /authorization/getClaims?token=xyz
             └→ Cache claims for 8 hours
             └→ Build ClaimsPrincipal
          ↓ Invalid
             └→ Clear cookies
             └→ Redirect to login
```

**Expiration Times:**
| Item | Duration | Renewal | Behavior |
|------|----------|---------|----------|
| Griffin token | Unknown (managed by Griffin) | No auto-renewal | Validate on each request |
| Claims cache | 8 hours | No renewal | Re-fetch after expiration |
| App session cookie | 7 days | Sliding (renewed on activity) | Expires after 7 days of inactivity |

**Session Termination:**
- User logs out manually
- 7 days of inactivity
- Griffin token becomes invalid
- Admin disables user account in ADFS

#### Plain-Language Explanation

**How long you stay logged in:**

- **Normal usage**: You stay logged in for **7 days**
- **After 7 days of not using the app**: You'll need to log in again
- **If you close the browser**: You stay logged in (cookies remember you)
- **If you log out**: Your session ends immediately

**What happens behind the scenes:**
- Every time you visit a page, the app checks if you're still a valid user
- For the first 8 hours, this check is instant (uses cached information)
- After 8 hours, the app double-checks with Griffin to make sure you're still authorized
- If Griffin says your credentials expired, you'll be asked to log in again

---

### 3.4 Logout Behavior

#### Technical Flow

**User-initiated logout:**

1. User clicks "Logout" button
2. `Pages/Auth/Logout.cshtml.cs` handler executes:
   ```csharp
   // Check authentication method
   var authMethod = User.FindFirst("AuthMethod")?.Value;

   if (authMethod == "Griffin")
   {
       // Clear Griffin token cookie
       Response.Cookies.Delete("griffin.token");

       // Clear claims cache
       var token = User.FindFirst("Griffin:Token")?.Value;
       var cacheKey = $"griffin_claims_{SHA256(token)}";
       _cache.Remove(cacheKey);
   }

   // Sign out from ASP.NET authentication
   await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
   ```
3. User redirected to `/Auth/Login`
4. All cookies cleared from browser

**Logout Scope:**
- ✅ **Local session** - App session terminated
- ✅ **Griffin token** - Cleared from browser
- ❌ **ADFS session** - Not terminated (see note below)

**NOTE:** Griffin ADFS documentation does **not provide a logout endpoint**. This means:
- Logging out of the app clears the app session
- The ADFS session in the browser may remain active
- If user immediately logs in again, ADFS may not prompt for credentials (SSO behavior)

**ASSUMPTION:** This is acceptable behavior for air-gapped environments where sessions are typically managed centrally by IT.

#### Plain-Language Explanation

**What happens when you log out:**

1. Click the "Logout" button
2. Your session is immediately ended
3. You're redirected to the login page
4. You can log in again anytime

**Important note about ADFS logout:**
- When you log out of the app, you're logged out of **this app only**
- If you use ADFS for other apps, those sessions might still be active
- If you log in again immediately, ADFS might remember you and log you in automatically (single sign-on)
- To completely log out of everything, you need to close your browser or use your organization's logout procedure

**Security tip:** If you're using a shared computer, **always close the browser** after logging out to fully end your session.

---

### 3.5 Failure Scenarios

#### Scenario 1: Griffin Service Unavailable

**Technical Behavior:**
```csharp
// In Login.cshtml.cs OnGetAsync()
var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();

if (griffinConfig?.Enabled == true)
{
    var isAvailable = await _griffinConfigService.TestConnectionAsync(
        griffinConfig.BaseUrl, griffinConfig.TimeoutSeconds);

    if (!isAvailable)
    {
        ShowGriffinUnavailableMessage = true;
        _logger.LogWarning("Griffin ADFS unavailable");
    }
}
```

**User Experience:**
- Login page loads normally
- ADFS button is visible
- Warning message appears: "Griffin ADFS is temporarily unavailable. Please use local login below."
- Local email/password login still works

**Logs:**
```
[WARNING] Griffin ADFS unavailable
[INFO] User authenticated via local login: user@example.com
```

**Plain-Language:** If Griffin breaks, you'll see a warning message but can still log in with email and password.

---

#### Scenario 2: Invalid Token

**Technical Behavior:**
```csharp
// In GriffinService.cs
var isValid = await ValidateTokenAsync(token, baseUrl, timeout);

if (!isValid)
{
    // Clear token cookie
    Response.Cookies.Delete("griffin.token");

    // Clear cache
    _cache.Remove($"griffin_claims_{hash}");

    // Log security event
    _securityLogger.LogAuthenticationFailure("Griffin ADFS", ipAddress, "Invalid token");

    // Redirect to login
    return RedirectToPage("/Auth/Login");
}
```

**User Experience:**
- User sees "Session expired, please login" message
- Automatically redirected to login page
- Must log in again

**Common Causes:**
- Token expired (managed by Griffin)
- Token was tampered with
- Griffin service restarted and invalidated tokens

**Plain-Language:** If your session expires or becomes invalid, you'll see a message and need to log in again. This is normal security behavior.

---

#### Scenario 3: Missing or Incomplete Claims

**Technical Behavior:**
```csharp
// In GriffinService.cs
var claims = await GetClaimsAsync(token, baseUrl, timeout);

if (string.IsNullOrEmpty(claims.UPN))
{
    _logger.LogError("Griffin claims missing UPN - cannot provision user");
    throw new Exception("Invalid claims: UPN required");
}

var displayName = claims.DisplayName;
if (string.IsNullOrEmpty(displayName))
{
    // Fallback to first + last name
    displayName = $"{claims.GivenName} {claims.Surname}";
}

if (string.IsNullOrEmpty(displayName))
{
    // Final fallback to email
    displayName = claims.UPN;
}
```

**User Experience:**
- Error message: "Authentication failed: incomplete information"
- User redirected to login page
- Must contact IT administrator

**Logs:**
```
[ERROR] Griffin claims missing UPN - cannot provision user
[ERROR] ADFS response: { "sAMAccountName": "jdoe", "DisplayName": "", "UPN": "" }
```

**Resolution:** IT administrator must configure ADFS to include required attributes (UPN, DisplayName) in token claims.

**Plain-Language:** If your ADFS profile is incomplete (missing email or name), login will fail. Contact your IT department to fix your profile.

---

#### Scenario 4: User Not Found (No Auto-Provisioning)

**Technical Behavior:**
```csharp
// In GriffinService.cs AuthenticateUserAsync()
var user = await _db.Users
    .FirstOrDefaultAsync(u => u.Email.ToLower() == claims.UPN.ToLower());

if (user == null)
{
    if (config.AutoProvisionUsers)
    {
        // Create user automatically
        user = new AppUser { ... };
        await _db.SaveChangesAsync();
    }
    else
    {
        // Show pending/error message
        _logger.LogInformation("Griffin user {Email} not found, auto-provision disabled", claims.UPN);
        return null;
    }
}
```

**User Experience (Auto-Provision Disabled):**
- Message: "Your account is pending administrator approval"
- User cannot access the app
- Admin must manually create the user account

**User Experience (Auto-Provision Enabled):**
- User account created automatically
- Default role assigned (configured in Owner settings)
- User logged in immediately

**Plain-Language:**
- **If auto-provision is ON**: New employees are automatically added when they first log in
- **If auto-provision is OFF**: Admins must manually add new employees before they can log in

---

## 4. User Lifecycle & Identity Mapping

### 4.1 How New Users Are Created

#### Technical Details

**Auto-Provisioning Flow:**

```csharp
// When auto-provisioning is enabled in Owner settings
if (user == null && griffinConfig.AutoProvisionUsers)
{
    user = new AppUser
    {
        CompanyId = griffinConfig.CompanyId,
        Email = claims.UPN,                      // From ADFS
        DisplayName = claims.DisplayName,        // From ADFS
        Role = griffinConfig.DefaultProvisionedRole, // From Owner settings
        IsActive = true,
        PasswordHash = Array.Empty<byte>(),      // No password for ADFS users
        PasswordSalt = Array.Empty<byte>()
    };

    _db.Users.Add(user);
    await _db.SaveChangesAsync();

    // Audit log
    await _auditLogService.LogSystemActionAsync(
        "UserAutoProvisioned",
        $"Griffin ADFS user auto-created: {claims.UPN}"
    );
}
```

**Field Mapping:**

| ADFS Claim | Database Field | Example | Notes |
|------------|----------------|---------|-------|
| `UPN` | `Email` | john.doe@domain.mil | Primary identifier |
| `DisplayName` | `DisplayName` | Doe, John A. | Shown in UI |
| `sAMAccountName` | (stored in claims) | jdoe | Windows username |
| (config) | `Role` | Employee | From Owner settings |
| (config) | `CompanyId` | 1 | From config |
| (hardcoded) | `IsActive` | true | Auto-provisioned users are active |
| (empty) | `PasswordHash` | [] | ADFS users have no password |

**Manual Provisioning Flow:**

If `AutoProvisionUsers = false`:
1. User attempts login via ADFS
2. Authentication succeeds with Griffin
3. App looks for user by email (`UPN`)
4. User not found → show "pending approval" message
5. Admin must manually create user via Admin → Users page
6. User can then log in on next attempt

#### Plain-Language Explanation

**How new employees get added:**

**Option 1: Automatic (Recommended)**
- New employee logs in with their work credentials
- System checks: "Do we have this person in our database?"
- If not found: **System creates their account automatically**
- They get the "Employee" role by default (or whatever you set in Owner settings)
- They can start using the app immediately

**Option 2: Manual Approval**
- New employee logs in with work credentials
- System checks: "Do we have this person in our database?"
- If not found: **System shows "pending approval" message**
- Admin gets notified (check audit logs)
- **Admin must manually add the user** in the Owner → Users page
- Employee can log in after admin adds them

**Which option to use:**
- ✅ **Automatic**: Best for most cases - faster, less admin work
- ✅ **Manual**: Best if you want tight control over who can access the app

**Configuration:** Go to Owner → Griffin ADFS → Toggle "Auto-Provision New Users"

---

### 4.2 Linking ADFS Identities to Existing Users

#### Technical Details

**Matching Logic:**

```csharp
// Case-insensitive email match
var user = await _db.Users
    .FirstOrDefaultAsync(u => u.Email.ToLower() == claims.UPN.ToLower() && u.IsActive);
```

**Primary Key:** Email address (`AppUser.Email` ⟷ ADFS `UPN`)

**Scenarios:**

| Scenario | Existing User | ADFS UPN | Outcome |
|----------|---------------|----------|---------|
| Exact match | john.doe@domain.mil | john.doe@domain.mil | ✅ User logs in successfully |
| Case difference | John.Doe@domain.mil | john.doe@domain.mil | ✅ Match (case-insensitive) |
| Email mismatch | john.doe@domain.mil | j.doe@domain.mil | ❌ New user created (if auto-provision) |
| User has password | john.doe@domain.mil (has password) | john.doe@domain.mil | ✅ Both auth methods work |

**Dual Authentication Support:**

Users can have **both** a password and ADFS credentials:
- `PasswordHash` is NOT empty → Can log in with email/password
- `Email` matches ADFS `UPN` → Can log in with ADFS
- User can choose either method on login page

**ASSUMPTION:** This is the desired behavior - allow flexibility for users to choose their authentication method.

#### Plain-Language Explanation

**How we match ADFS logins to existing accounts:**

The system matches by **email address**. Here's what happens:

**If you already have an account with email `john.doe@domain.mil`:**
1. You log in via ADFS with username `jdoe`
2. ADFS says your email is `john.doe@domain.mil`
3. System finds your existing account
4. ✅ You're logged in to your existing account

**Important notes:**
- ✅ If you have a password AND use ADFS, both login methods work
- ✅ Email matching is case-insensitive (`John.Doe@` = `john.doe@`)
- ⚠️ If your ADFS email doesn't match exactly, a new duplicate account will be created

**To prevent duplicates:**
- Make sure email addresses in the app match ADFS emails exactly
- Before enabling ADFS, check that all users have correct emails
- If you find duplicates, contact your admin to merge them

---

### 4.3 Email/Username Changes in ADFS

#### Technical Details

**What Happens:**

When a user's email changes in ADFS (e.g., name change, department transfer):

1. **Old email in app:** `john.doe@domain.mil`
2. **New email in ADFS:** `john.smith@domain.mil` (after marriage, for example)
3. **User logs in via ADFS** with new credentials
4. **System looks for:** `john.smith@domain.mil`
5. **Not found** → Auto-provision creates **new account** (if enabled)
6. **Result:** User has **two accounts** - old one (orphaned) and new one (active)

**PROBLEM:** Email is the primary key for matching. Changing email in ADFS breaks the link.

**Current Behavior:** No automatic migration. Admin must manually fix.

**RECOMMENDATION:** Add manual process for email changes:

```plaintext
When employee's email changes:
1. Admin updates email in app database directly:
   UPDATE AppUsers SET Email = 'new.email@domain.mil' WHERE Email = 'old.email@domain.mil'
2. Employee can now log in with new ADFS credentials
3. Old sessions remain valid until expiration
```

**OPEN QUESTION:** Should we add a secondary identifier (like `sAMAccountName` or employee ID) to make matching more robust?

#### Plain-Language Explanation

**If someone's email changes (marriage, etc.):**

**Current behavior:**
- Their old account still exists
- They log in with new email
- System creates a NEW account for them
- They lose access to their old data
- ⚠️ **This is a problem**

**What to do:**
1. **Before** the person's email changes:
   - Admin updates their email in the app database manually
   - Person can then log in with new credentials
2. **After** email already changed and duplicate created:
   - Admin must merge the accounts manually
   - Transfer data from old account to new account
   - Deactivate old account

**Best practice:**
- ✅ Notify admin BEFORE changing someone's email
- ✅ Admin updates email in app first
- ✅ Then update in ADFS
- ✅ Prevents duplicate accounts

---

### 4.4 Duplicate User Detection & Resolution

#### Technical Details

**How Duplicates Happen:**

1. **Email mismatch:** ADFS UPN doesn't match database email
2. **Email change:** User's ADFS email changed (see previous section)
3. **Manual creation:** Admin manually created user with different email
4. **Case sensitivity:** Database has `John@` but ADFS has `john@` (should NOT happen - we use case-insensitive matching)

**Detection Query:**

```sql
-- Find duplicate email addresses
SELECT Email, COUNT(*) as Count
FROM AppUsers
WHERE IsActive = 1
GROUP BY LOWER(Email)
HAVING COUNT(*) > 1;

-- Find users with similar names but different emails
SELECT DisplayName, Email
FROM AppUsers
WHERE IsActive = 1
ORDER BY DisplayName;
```

**Resolution Process:**

```plaintext
For each duplicate set:

1. Identify the PRIMARY account (usually the older one with data)
2. Identify the DUPLICATE account (usually newer, created by auto-provision)
3. Transfer data from duplicate to primary:
   - Shifts
   - Time-off requests
   - Swap requests
   - Chores
   - Notes
4. Update audit logs to reference primary account
5. Soft-delete duplicate: UPDATE AppUsers SET IsActive = 0 WHERE Id = duplicate_id
6. Update ADFS email if needed: UPDATE AppUsers SET Email = correct_adfs_upn WHERE Id = primary_id
```

**RECOMMENDATION:** Add a duplicate detection report in Owner menu:
- Shows potential duplicates based on similar names
- Allows admin to merge accounts with one click
- Logs all merge operations to audit trail

#### Plain-Language Explanation

**How to find and fix duplicate accounts:**

**Finding duplicates:**
1. Go to Owner → Users
2. Look for people with the same name but different emails
3. Examples:
   - "John Doe" with `john.doe@domain.mil`
   - "John Doe" with `j.doe@domain.mil`

**Fixing duplicates:**
1. Decide which account is the "real" one (usually the older one)
2. **Don't delete anything yet**
3. Contact your IT admin or developer to help merge the accounts
4. They will transfer all data from the duplicate to the real account
5. The duplicate account gets deactivated (not deleted - for audit purposes)

**Prevention:**
- ✅ Make sure everyone's email in the app matches their ADFS email BEFORE enabling Griffin
- ✅ When someone's email changes, update it in the app manually BEFORE they log in again
- ✅ Periodically check for duplicates (monthly is good)

---

### 4.5 Required Claims/Attributes

#### Technical Details

**Required ADFS Claims:**

| Claim Name | ADFS Attribute | Required? | Used For | Fallback |
|------------|----------------|-----------|----------|----------|
| `UPN` | `userPrincipalName` | ✅ Yes | Email / primary identifier | None - login fails |
| `DisplayName` | `displayName` | ⚠️ Recommended | UI display name | `GivenName + Surname` |
| `GivenName` | `givenName` | ⚠️ Recommended | Fallback for DisplayName | `UPN` |
| `Surname` | `sn` | ⚠️ Recommended | Fallback for DisplayName | Empty string |
| `sAMAccountAccount` | `sAMAccountName` | ⚠️ Recommended | Windows username | Not used for matching |
| `auth_time` | (timestamp) | ❌ Optional | Authentication timestamp | Current time |

**Validation Code:**

```csharp
// In GriffinService.cs
var claims = await GetClaimsAsync(token, baseUrl, timeout);

// REQUIRED: UPN
if (string.IsNullOrEmpty(claims.UPN))
{
    _logger.LogError("Griffin claims missing UPN - cannot provision user");
    throw new Exception("Invalid claims: UPN required");
}

// RECOMMENDED: DisplayName with fallback
var displayName = claims.DisplayName;
if (string.IsNullOrEmpty(displayName))
{
    displayName = $"{claims.GivenName} {claims.Surname}".Trim();
}
if (string.IsNullOrEmpty(displayName))
{
    displayName = claims.UPN; // Final fallback
}

// Optional: sAMAccountName
// Stored in claims but not used for matching
```

**Missing Claims Behavior:**

| Missing Claim | Impact | User Sees | Resolution |
|---------------|--------|-----------|------------|
| `UPN` | ❌ Login fails | "Authentication failed: incomplete information" | ADFS admin must add UPN attribute |
| `DisplayName` | ⚠️ Degraded UX | Username shows as "FirstName LastName" or email | Still works, but ugly |
| `GivenName` + `Surname` | ⚠️ Degraded UX | Username shows as email address | Still works |
| `sAMAccountName` | ✅ No impact | Normal operation | Optional field |

#### Plain-Language Explanation

**What information we need from ADFS:**

**Required (must have):**
- ✅ **Email address** (UPN) - We use this to find your account
  - Example: `john.doe@domain.mil`

**Highly recommended (should have):**
- ✅ **Full name** (DisplayName) - Shows up in the app UI
  - Example: "Doe, John A."
- ✅ **First name** - Backup if full name is missing
- ✅ **Last name** - Backup if full name is missing

**Optional (nice to have):**
- ✅ **Windows username** (sAMAccountName) - For logging/debugging
  - Example: `jdoe`

**What happens if something is missing:**

| Missing | What You'll See | Can You Still Log In? |
|---------|-----------------|----------------------|
| Email | Error message | ❌ No |
| Full name | Your email shows as your name | ✅ Yes, but looks weird |
| First/Last name | Your email shows as your name | ✅ Yes, but looks weird |
| Windows username | Nothing - you won't notice | ✅ Yes, works fine |

**If login fails with "incomplete information":**
- Your ADFS profile is missing required fields
- Contact your IT department
- They need to configure ADFS to include your email address

---

### 4.6 Role & Permission Mapping

#### Technical Details

**ADFS Does NOT Provide Roles:**

Griffin/ADFS returns user identity (who you are) but **not** user permissions (what you can do).

**Role Assignment Logic:**

```csharp
// Roles come from the database, NOT from ADFS
var user = await _db.Users
    .FirstOrDefaultAsync(u => u.Email.ToLower() == claims.UPN.ToLower());

// Build claims principal
var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Email, user.Email),
    new Claim(ClaimTypes.Name, user.DisplayName),
    new Claim(ClaimTypes.Role, user.Role.ToString()),  // ← From DATABASE
    new Claim("CompanyId", user.CompanyId.ToString()),
    new Claim("AuthMethod", "Griffin"),
    // ... other claims
}, "Griffin"));
```

**Role Sources:**

| When | Role Source | Who Assigns |
|------|-------------|-------------|
| New user, auto-provision enabled | `GriffinConfig.DefaultProvisionedRole` | Owner (in settings) |
| New user, manual approval | Admin assigns when creating user | Admin |
| Existing user | `AppUser.Role` in database | Admin (via Users page) |

**Available Roles:**
- `Employee` - Basic access
- `Manager` - Can manage their team
- `Admin` - Full company access
- `Owner` - System-wide access

**Role Changes:**
- Admin can change roles anytime via Owner → Users page
- Role changes take effect on next request (no logout needed)
- All role changes logged to audit trail

**ASSUMPTION:** ADFS group membership is NOT used for role assignment. This could be implemented as a future enhancement if needed.

#### Plain-Language Explanation

**How permissions work with ADFS login:**

**Important:** ADFS only tells us WHO you are, not WHAT you can do.

**When you log in via ADFS:**
1. ADFS confirms you're a real employee
2. Our app looks up your account in our database
3. **Your permissions come from our database**, not from ADFS

**For new employees (auto-provision):**
- They get the "Employee" role by default (or whatever the Owner set)
- Admin can change their role later if needed

**For existing employees:**
- Your role stays exactly the same
- ADFS login doesn't change your permissions
- Only admins can change your role

**Changing someone's role:**
1. Go to Owner → Users
2. Find the person
3. Click "Edit"
4. Change their role
5. Save
6. They get new permissions immediately (no need to log out)

**Note:** Even if you're a "Domain Admin" in ADFS, you might only be an "Employee" in this app. The two systems are separate.

---

## 5. Configuration & Owner Menu

### 5.1 Owner Menu Settings

#### Technical Details

**Configuration Location:** `/Owner/GriffinConfig`

**Authorization:** `[Authorize(Policy = "IsAdmin")]` - Only company admins can access

**Database Model:**

```csharp
public class GriffinConfig
{
    public int Id { get; set; }
    public int CompanyId { get; set; }              // Multi-tenant scoping

    // Service Configuration
    public bool Enabled { get; set; }               // Master toggle
    public string? BaseUrl { get; set; }            // Griffin service endpoint
    public string? TokenConsumerUrl { get; set; }   // Our callback URL
    public int TimeoutSeconds { get; set; }         // API timeout (1-60 seconds)

    // User Provisioning
    public bool AutoProvisionUsers { get; set; }    // Auto-create new users
    public UserRole DefaultProvisionedRole { get; set; } // Default role for new users

    // Audit
    public DateTime LastUpdated { get; set; }
    public string? LastUpdatedBy { get; set; }
}
```

**Settings Explained:**

1. **Enabled** (`bool`)
   - **Technical:** Master toggle for Griffin authentication
   - **Plain:** Turn ADFS login on/off for your company
   - **Default:** `false` (disabled)
   - **Impact:** If `false`, ADFS button shows warning message, login via email/password still works

2. **Griffin Base URL** (`string`)
   - **Technical:** HTTP endpoint of Griffin service (e.g., `http://7108dev-auth.d8200.mil`)
   - **Plain:** The web address where Griffin is running
   - **Format:** `http://hostname` or `https://hostname`
   - **Example:** `http://7108dev-auth.d8200.mil`
   - **Validation:** Must be valid HTTP/HTTPS URL
   - **Required:** Yes (if Enabled = true)

3. **Callback URL** (`string`)
   - **Technical:** Full URL of our `/Auth/GriffinCallback` endpoint
   - **Plain:** Where Griffin sends users back after they log in
   - **Format:** `{scheme}://{hostname}/Auth/GriffinCallback`
   - **Example:** `https://shiftmanager.d8200.mil/Auth/GriffinCallback`
   - **Validation:** Must be valid HTTPS URL (HTTP allowed in dev only)
   - **Required:** Yes (if Enabled = true)
   - **Note:** Must be accessible from user's browser

4. **Auto-Provision New Users** (`bool`)
   - **Technical:** Automatically create `AppUser` record on first ADFS login
   - **Plain:** Automatically add new employees when they first log in
   - **Default:** `true` (recommended)
   - **If true:** New user created with `DefaultProvisionedRole`
   - **If false:** New user sees "pending approval" message, admin must manually add them

5. **Default Role** (`UserRole enum`)
   - **Technical:** Role assigned to auto-provisioned users
   - **Plain:** What permission level new employees get by default
   - **Options:** Employee, Manager, Admin
   - **Default:** `Employee` (most restrictive)
   - **Impact:** Can be changed later by admin on per-user basis

6. **Timeout** (`int`, 1-60 seconds)
   - **Technical:** HTTP timeout for Griffin API calls (validate, getClaims)
   - **Plain:** How long to wait for Griffin to respond before giving up
   - **Default:** `10` seconds
   - **Range:** 1-60 seconds
   - **Recommendation:** 10 seconds is good for most cases, increase if Griffin is slow

#### Plain-Language Explanation

**Where to find settings:** Owner menu → Griffin ADFS

**What each setting means:**

| Setting | What It Does | Recommended Value |
|---------|--------------|-------------------|
| **Enable Griffin ADFS** | Turns ADFS login on or off | ✅ On (if you have Griffin) |
| **Griffin Base URL** | Where Griffin is running | Ask your IT department |
| **Callback URL** | Where to send users after login | `https://your-app-address/Auth/GriffinCallback` |
| **Auto-Provision New Users** | Automatically add new employees | ✅ On (saves admin time) |
| **Default Role** | Permission level for new employees | Employee (safest) |
| **Timeout** | How long to wait for Griffin | 10 seconds (good default) |

---

### 5.2 Safe Defaults

#### Technical Recommendations

```csharp
// Recommended default configuration
var safeDefaults = new GriffinConfig
{
    Enabled = false,                         // Start disabled, enable after testing
    BaseUrl = "",                            // Must be configured by admin
    TokenConsumerUrl = "",                   // Must be configured by admin
    AutoProvisionUsers = true,               // Reduces admin overhead
    DefaultProvisionedRole = UserRole.Employee, // Least privilege principle
    TimeoutSeconds = 10                      // Good balance of speed vs reliability
};
```

**Security Considerations:**

| Setting | Secure Value | Insecure Value | Risk |
|---------|--------------|----------------|------|
| `Enabled` | `false` (until configured) | `true` (without valid URLs) | Login failures |
| `DefaultProvisionedRole` | `Employee` | `Admin` | ⚠️ **Privilege escalation** |
| `AutoProvisionUsers` | `true` (in trusted environments) | `true` (in untrusted environments) | ⚠️ **Unauthorized access** |
| `TimeoutSeconds` | `10` | `1` (too short) | Frequent timeouts |
| `TimeoutSeconds` | `10` | `60` (too long) | Slow user experience |

**Multi-Tenant Isolation:**

Each company has its own `GriffinConfig` record:
- Company A can enable Griffin
- Company B can disable Griffin
- Settings don't affect each other

#### Plain-Language Explanation

**Best settings for most people:**

| Setting | Best Value | Why |
|---------|-----------|-----|
| Enable Griffin | ❌ **Off** at first, turn on after testing | Test with a few people first |
| Auto-Provision | ✅ **On** | Saves you from manually adding every employee |
| Default Role | 👤 **Employee** | Safest - you can always promote people later |
| Timeout | ⏱️ **10 seconds** | Good balance - not too fast, not too slow |

**Security tips:**
- ⚠️ Never set default role to "Admin" - new people would have full access
- ⚠️ Test Griffin with 2-3 people before enabling for everyone
- ⚠️ If your organization doesn't trust auto-provisioning, turn it off and manually approve each person

---

### 5.3 Common Misconfigurations

#### Scenario 1: Wrong Callback URL

**Symptoms:**
- User clicks "Login with ADFS"
- Redirected to ADFS login
- After entering credentials, browser shows "Cannot connect" or 404 error
- Never returns to app

**Root Cause:**
```csharp
// WRONG
TokenConsumerUrl = "http://localhost:5001/Auth/GriffinCallback"  // ← Local dev URL used in production

// CORRECT
TokenConsumerUrl = "https://shiftmanager.d8200.mil/Auth/GriffinCallback"  // ← Production URL
```

**How to Fix:**
1. Go to Owner → Griffin ADFS
2. Update "Callback URL" to match your production domain
3. Click "Save"
4. Test login again

**Prevention:** Use environment-specific configuration in `appsettings.json`

---

#### Scenario 2: Griffin Service Not Reachable

**Symptoms:**
- Login page shows: "Griffin ADFS is temporarily unavailable"
- Button is visible but warning message appears
- Clicking button shows error

**Root Cause:**
```csharp
// Possible causes
BaseUrl = "http://wrong-hostname"  // ← Typo in hostname
BaseUrl = "http://griffin-dev"     // ← Dev URL used in production
TimeoutSeconds = 1                 // ← Timeout too short
// Firewall blocking connection
// Griffin service actually down
```

**How to Diagnose:**
1. Check Owner → Griffin ADFS settings
2. Click "Test Connection" button
3. Check application logs for errors:
   ```
   [WARNING] Griffin ADFS unavailable
   [ERROR] HttpRequestException: Connection refused
   ```

**How to Fix:**
- Verify Griffin Base URL is correct (ask IT department)
- Verify firewall allows connection from app server to Griffin
- Increase timeout if network is slow
- Verify Griffin service is actually running

---

#### Scenario 3: Auto-Provision Disabled, No Manual Process

**Symptoms:**
- New employee tries to log in via ADFS
- Sees "Your account is pending administrator approval"
- Weeks go by, still can't access the app
- No notification sent to admin

**Root Cause:**
```csharp
// Config
AutoProvisionUsers = false  // ← Auto-provision disabled
// But no process in place for admin to review pending users
```

**How to Fix:**

**Short-term:** Enable auto-provisioning
```csharp
AutoProvisionUsers = true
DefaultProvisionedRole = UserRole.Employee
```

**Long-term:** Implement approval workflow
1. Admin checks audit logs daily for "pending" users
2. Admin manually creates users via Owner → Users page
3. Or: Implement notification email to admin when new user tries to log in

**Prevention:** Only disable auto-provision if you have a clear process for approving new users

---

#### Scenario 4: Mixed Authentication Confusion

**Symptoms:**
- User has account with password
- User logs in via ADFS
- Sees "account pending approval" even though they already have an account

**Root Cause:**
```csharp
// Database
AppUser { Email = "john.doe@company.com", ... }  // ← .com domain

// ADFS
UPN = "john.doe@company.mil"  // ← .mil domain

// Result: Email mismatch, treated as different user
```

**How to Fix:**
1. **Before enabling Griffin:** Audit all user emails
2. Update emails to match ADFS UPNs:
   ```sql
   UPDATE AppUsers
   SET Email = 'john.doe@company.mil'
   WHERE Email = 'john.doe@company.com'
   ```
3. Then enable Griffin

**Prevention:** Ensure database emails match ADFS UPNs before enabling Griffin

---

## 6. Local Development (Non-Air-Gapped)

### 6.1 Running the Project Locally

#### Technical Setup

**Prerequisites:**
- .NET 8.0 SDK installed
- SQL Server or SQLite for database
- Git repository cloned
- No Griffin service needed (will use fallback)

**Steps:**

```bash
# 1. Clone repository
git clone <repository-url>
cd ShiftManager

# 2. Restore dependencies
dotnet restore

# 3. Apply database migrations
dotnet ef database update

# 4. Run application
dotnet run

# 5. Open browser
# Navigate to: https://localhost:5001
```

**Configuration for Local Development:**

`appsettings.Development.json`:
```json
{
  "Griffin": {
    "Enabled": false,           // ← Disable Griffin by default
    "BaseUrl": "",
    "TokenConsumerUrl": "",
    "AutoProvisionUsers": true,
    "DefaultProvisionedRole": "Employee",
    "TimeoutSeconds": 10
  }
}
```

**Expected Behavior:**
- Login page loads normally
- ADFS button is visible
- Warning message shows: "Griffin ADFS is temporarily unavailable"
- Email/password login works normally
- No Griffin API calls are made

**IMPORTANT:** Griffin service is **not available** in local development (requires air-gapped network). All development and testing uses fallback authentication.

#### Plain-Language Explanation

**How to run the app on your computer:**

1. **Install .NET** (if you don't have it): Download from Microsoft
2. **Get the code**: Clone from Git or download ZIP
3. **Open terminal** in the project folder
4. **Run these commands:**
   ```
   dotnet restore
   dotnet ef database update
   dotnet run
   ```
5. **Open browser**: Go to `https://localhost:5001`

**What you'll see:**
- ✅ Login page with ADFS button
- ⚠️ Warning: "Griffin ADFS is temporarily unavailable"
- ✅ Regular email/password login works fine

**Why no Griffin?**
- Griffin only runs in the secure military network
- Your laptop doesn't have access to it
- This is normal - just use email/password to test

---

### 6.2 Testing Griffin Flows Locally

#### Option 1: Mock Griffin Service (Future Enhancement)

**Not currently implemented**, but here's how it could work:

```csharp
// Services/MockGriffinService.cs
#if DEBUG
public class MockGriffinService : IGriffinService
{
    public async Task<bool> ValidateTokenAsync(string token, string baseUrl, int timeout)
    {
        // Mock: Accept any token that starts with "mock_"
        return token.StartsWith("mock_");
    }

    public async Task<GriffinClaimsDto> GetClaimsAsync(string token, string baseUrl, int timeout)
    {
        // Mock: Return fake claims
        return new GriffinClaimsDto
        {
            UPN = "test.user@mock.mil",
            DisplayName = "Mock User",
            GivenName = "Mock",
            Surname = "User",
            sAMAccountName = "muser",
            auth_time = DateTime.UtcNow.ToString("o")
        };
    }
}
#endif

// Program.cs
#if DEBUG
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IGriffinService, MockGriffinService>();
}
else
#endif
{
    builder.Services.AddScoped<IGriffinService, GriffinService>();
}
```

**OPEN QUESTION:** Should we implement a mock Griffin service for local testing?

#### Option 2: Database Seeding with Test Users

**Current approach** (recommended for now):

```csharp
// Create test users with both password and ADFS-like emails
var testUsers = new[]
{
    new AppUser
    {
        Email = "test.employee@mock.mil",
        DisplayName = "Test Employee",
        Role = UserRole.Employee,
        PasswordHash = hasher.HashPassword("password123"),
        IsActive = true
    },
    new AppUser
    {
        Email = "test.admin@mock.mil",
        DisplayName = "Test Admin",
        Role = UserRole.Admin,
        PasswordHash = hasher.HashPassword("admin123"),
        IsActive = true
    }
};
```

**Testing Approach:**
1. Create test users with ADFS-style emails (`*.mil`)
2. Use email/password login in local dev
3. Test user lifecycle, permissions, etc.
4. Actual Griffin authentication can only be tested in air-gapped environment

#### Plain-Language Explanation

**How to test ADFS features on your laptop:**

**Current approach (what works now):**
1. Create test users with emails like `test@mock.mil`
2. Log in with email/password (not ADFS)
3. Test features like role assignment, user management, etc.
4. **Actual ADFS login can only be tested in the real environment**

**Why can't we test real ADFS login locally?**
- ADFS only exists in the secure military network
- Your laptop can't access it
- This is a security feature, not a bug

**What you CAN test locally:**
- ✅ Login page layout
- ✅ Button visibility
- ✅ Fallback authentication (email/password)
- ✅ User roles and permissions
- ✅ Database operations

**What you CAN'T test locally:**
- ❌ Actual ADFS authentication flow
- ❌ Token validation with Griffin
- ❌ Claims retrieval from ADFS

**For full testing:** You need access to the air-gapped environment where Griffin is running.

---

### 6.3 Verifying Fallback Still Works

#### Test Cases

**Test 1: Griffin Disabled, Email/Password Login**

```plaintext
1. Ensure Griffin.Enabled = false in config
2. Navigate to /Auth/Login
3. VERIFY: ADFS button shows with warning message
4. Enter email and password
5. Click "Login"
6. VERIFY: Logged in successfully
7. VERIFY: No Griffin API calls in logs
```

**Test 2: Griffin Enabled but Unreachable, Fallback Works**

```plaintext
1. Set Griffin.Enabled = true
2. Set Griffin.BaseUrl = "http://invalid-hostname"
3. Navigate to /Auth/Login
4. VERIFY: Warning message "Griffin ADFS is temporarily unavailable"
5. Enter email and password
6. Click "Login"
7. VERIFY: Logged in successfully
8. VERIFY: Logs show "Griffin ADFS unavailable"
```

**Test 3: Mixed Authentication - Both Methods Work**

```plaintext
1. Create user with email = "test@domain.mil" and password
2. Login via email/password
3. VERIFY: Successful login
4. Logout
5. Login via ADFS (if Griffin available)
6. VERIFY: Same user, successful login
7. VERIFY: User sees same data in both cases
```

**Test 4: Session Persistence Across Authentication Methods**

```plaintext
1. Login via email/password
2. Navigate to dashboard
3. Close browser tab (not logout)
4. Reopen app
5. VERIFY: Still logged in (cookie persists)
6. Check claims: VERIFY AuthMethod = "Password"
```

**Test 5: Role Assignment Independent of Auth Method**

```plaintext
1. Login via email/password as Employee
2. VERIFY: Cannot access Admin pages
3. Logout
4. Admin changes user role to Admin
5. Login via email/password again
6. VERIFY: Can now access Admin pages
```

#### Plain-Language Explanation

**How to make sure regular login still works:**

**Test checklist:**
- [ ] Can you log in with email/password when Griffin is disabled? ✅
- [ ] Can you log in with email/password when Griffin is broken? ✅
- [ ] Do both login methods work for the same user? ✅
- [ ] Do you stay logged in after closing the browser? ✅
- [ ] Do permissions work the same regardless of how you logged in? ✅

**If any test fails:**
- ⚠️ Something is broken
- 📝 Check application logs
- 📞 Contact developer

**Expected behavior:**
- Email/password login should **always** work
- Even if Griffin is completely broken
- Even if Griffin is not configured
- This is your safety net

---

### 6.4 Development Troubleshooting

#### Problem 1: "Build Failed - Cannot Find GriffinService"

**Error:**
```
Error CS0246: The type or namespace name 'IGriffinService' could not be found
```

**Cause:** Missing service registration

**Fix:**
```csharp
// Program.cs - ensure these lines exist
builder.Services.AddScoped<IGriffinConfigService, GriffinConfigService>();
builder.Services.AddScoped<IGriffinService, GriffinService>();
```

---

#### Problem 2: "Database Update Failed - Table Already Exists"

**Error:**
```
Microsoft.Data.SqlClient.SqlException: There is already an object named 'GriffinConfigs'
```

**Cause:** Migration already applied

**Fix:**
```bash
# Check migration status
dotnet ef migrations list

# If GriffinConfig migration is listed as "applied", you're good
# If it shows as "pending", run:
dotnet ef database update
```

---

#### Problem 3: "Login Page Doesn't Show ADFS Button"

**Symptoms:**
- Login page loads
- Only see email/password form
- No ADFS button visible

**Diagnosis:**
```csharp
// Check Login.cshtml - should have this:
<div class="card mb-4">  <!-- ← NOT wrapped in @if(Model.ShowGriffinButton) -->
    <h5 class="card-title">@Localizer["LoginWithADFS"]</h5>
    ...
</div>
```

**Fix:**
- Button should ALWAYS be visible (no conditional)
- Check that you have the latest code changes
- Run `dotnet build` to rebuild

---

#### Problem 4: "Localization Keys Not Found"

**Error in browser:** `"LoginWithADFS"` (literal string instead of translation)

**Cause:** Missing resource keys

**Fix:**
```bash
# Verify resource files exist
ls Resources/SharedResources.resx
ls Resources/SharedResources.he-IL.resx

# Search for keys
grep "LoginWithADFS" Resources/SharedResources.resx

# If missing, add the keys (see section 2.1)
```

---

## 7. Air-Gapped Environment Setup

### 7.1 Prerequisites

#### Infrastructure Requirements

**Griffin Service:**
- Griffin service must be deployed and running
- Deployed via Helm chart on Kubernetes/OpenShift
- Service endpoint accessible from app server
- Example: `http://7108dev-auth.d8200.mil`

**ADFS Server:**
- ADFS server configured and operational
- User attributes configured (UPN, DisplayName, etc.)
- Example: `https://8200adfs.sso.aman.idf`

**Network:**
- App server can reach Griffin service (HTTP/HTTPS)
- Griffin service can reach ADFS server (HTTPS)
- User browser can reach app server
- No internet access required (air-gapped)

**Diagram:**
```
User Browser
    ↓ HTTPS
[App Server: ShiftManager]
    ↓ HTTP
[Griffin Service: Kubernetes Pod]
    ↓ HTTPS
[ADFS Server]
    ↓
[Active Directory]
```

#### Application Requirements

**Files to Transfer:**
1. Published application binaries (from `dotnet publish`)
2. Database migration scripts
3. Configuration files (appsettings.json with secrets)
4. SSL certificates (if using HTTPS)

**Database:**
- SQL Server or SQLite
- Network accessible from app server
- Backup strategy in place

#### Plain-Language Explanation

**Before you can use Griffin, you need:**

**In the secure network:**
- ✅ Griffin service installed and running (IT department handles this)
- ✅ ADFS server working (IT department handles this)
- ✅ Network connections between all servers

**For your app:**
- ✅ App installed on the server (you handle this)
- ✅ Database set up (you handle this)
- ✅ Settings file with correct URLs (you handle this)

**Ask your IT department:**
- "What is the Griffin service URL?"
- "What is our app's public URL in the secure network?"
- "Do I need SSL certificates?"

---

### 7.2 Step-by-Step Installation

#### Step 1: Build on Connected Machine

**On a machine WITH internet access:**

```bash
# 1. Clone repository
git clone <repository-url>
cd ShiftManager

# 2. Restore dependencies (downloads from internet)
dotnet restore

# 3. Run tests
dotnet test

# 4. Publish for production
dotnet publish -c Release -o ./publish

# 5. Verify published files
ls ./publish
# Should see:
# - ShiftManager.dll
# - ShiftManager.deps.json
# - appsettings.json
# - wwwroot/ (static files)
# - Resources/ (localization)
```

**Create deployment package:**

```bash
# 6. Package for transfer
cd publish
zip -r ../ShiftManager-v1.0.0.zip .
cd ..

# 7. Verify package
unzip -l ShiftManager-v1.0.0.zip
```

---

#### Step 2: Transfer to Air-Gapped Machine

**Approved transfer methods (follow your org's policy):**
- USB drive (approved for classified transfer)
- Secure file transfer (SFT) via airgap-crossing appliance
- Physical media (CD/DVD)

```bash
# On air-gapped machine, verify transfer
unzip ShiftManager-v1.0.0.zip -d /var/www/shiftmanager
ls /var/www/shiftmanager
```

---

#### Step 3: Configure Application Settings

**Edit `appsettings.json`:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",

  "ConnectionStrings": {
    "DefaultConnection": "Data Source=shiftmanager.db"
  },

  "Griffin": {
    "Enabled": true,                                    // ← Enable Griffin
    "BaseUrl": "http://7108dev-auth.d8200.mil",        // ← Griffin service URL
    "TokenConsumerUrl": "https://shiftmanager.d8200.mil/Auth/GriffinCallback", // ← Your app URL
    "AutoProvisionUsers": true,
    "DefaultProvisionedRole": "Employee",
    "TimeoutSeconds": 10
  },

  "Kestrel": {
    "EndPoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      },
      "Https": {
        "Url": "https://0.0.0.0:5001",
        "Certificate": {
          "Path": "/etc/ssl/certs/shiftmanager.pfx",
          "Password": "your-cert-password"
        }
      }
    }
  }
}
```

**IMPORTANT:** Replace placeholder values with actual values from your environment.

---

#### Step 4: Apply Database Migrations

```bash
# 1. Navigate to app directory
cd /var/www/shiftmanager

# 2. Apply migrations
dotnet ef database update --connection "Data Source=/var/www/shiftmanager/shiftmanager.db"

# 3. Verify tables created
sqlite3 shiftmanager.db ".tables"
# Should include: GriffinConfigs

# 4. Check migration history
sqlite3 shiftmanager.db "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 5;"
# Should include: xxxxxxxx_AddGriffinConfig
```

---

#### Step 5: Start Application

**Option A: Run Directly (Testing)**

```bash
# Set environment
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS="http://0.0.0.0:5000;https://0.0.0.0:5001"

# Run application
dotnet ShiftManager.dll

# Expected output:
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: http://0.0.0.0:5000
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: https://0.0.0.0:5001
# info: Microsoft.Hosting.Lifetime[0]
#       Application started. Press Ctrl+C to shut down.
```

**Option B: Run as Service (Production)**

Create systemd service file:

```ini
# /etc/systemd/system/shiftmanager.service
[Unit]
Description=ShiftManager Application
After=network.target

[Service]
WorkingDirectory=/var/www/shiftmanager
ExecStart=/usr/bin/dotnet /var/www/shiftmanager/ShiftManager.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=shiftmanager
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

Enable and start service:

```bash
# Enable service
sudo systemctl enable shiftmanager.service

# Start service
sudo systemctl start shiftmanager.service

# Check status
sudo systemctl status shiftmanager.service

# View logs
sudo journalctl -u shiftmanager.service -f
```

---

#### Step 6: Configure Griffin in Owner Menu

**Via web browser:**

1. Navigate to `https://shiftmanager.d8200.mil`
2. Login with Owner account (email/password)
3. Go to **Owner** → **Griffin ADFS**
4. Configure settings:
   - ✅ **Enable Griffin ADFS**: Checked
   - 🌐 **Griffin Base URL**: `http://7108dev-auth.d8200.mil`
   - 🔗 **Callback URL**: `https://shiftmanager.d8200.mil/Auth/GriffinCallback`
   - 👤 **Auto-Provision New Users**: Checked (recommended)
   - 🎭 **Default Role**: Employee
   - ⏱️ **Timeout**: 10 seconds
5. Click **"Test Connection"**
   - ✅ Success: "Connection successful - Griffin service is reachable"
   - ❌ Failure: "Connection failed - Griffin service unreachable"
     - Check Griffin Base URL
     - Check network connectivity
     - Check firewall rules
6. Click **"Save"**
7. Verify in logs:
   ```
   [INFO] Griffin config updated by admin@company.mil
   [INFO] GriffinConfigUpdated: Enabled=true, BaseUrl=http://7108dev-auth.d8200.mil
   ```

---

#### Step 7: Test Authentication Flow

**Test 1: ADFS Login**

1. **Logout** from Owner account
2. Navigate to login page
3. **Verify** ADFS button visible: "Login with ADFS" / "הזדהות במערכת היחידה"
4. Click **"Login with ADFS"**
5. **Expected:** Redirect to ADFS login page
6. Enter **domain credentials** (CAC or username/password)
7. **Expected:** Redirect back to app, logged in
8. Check **audit logs**:
   ```
   [INFO] User authenticated via Griffin ADFS: john.doe@domain.mil
   [INFO] Auto-provisioned Griffin user: john.doe@domain.mil (Role: Employee)
   ```

**Test 2: Email/Password Login (Fallback)**

1. Navigate to login page
2. Enter **email and password**
3. Click **"Login"**
4. **Expected:** Logged in successfully
5. **Verify:** Both auth methods work side-by-side

**Test 3: Failure Scenarios**

1. Disable Griffin service temporarily
2. Navigate to login page
3. **Verify:** Warning message: "Griffin ADFS is temporarily unavailable"
4. **Verify:** Email/password login still works
5. Re-enable Griffin service
6. Refresh login page
7. **Verify:** Warning disappears, ADFS login works again

---

### 7.3 Health Checks & Validation

#### Application Health

**Endpoint:** `/health` (add to Program.cs if not exists)

```bash
curl http://localhost:5000/health
# Expected: HTTP 200 OK
```

**Database Connectivity:**

```bash
# Check database file
ls -lh /var/www/shiftmanager/shiftmanager.db

# Query Griffin config
sqlite3 shiftmanager.db "SELECT Id, CompanyId, Enabled, BaseUrl FROM GriffinConfigs;"
```

#### Griffin Connectivity

**From app server:**

```bash
# Test Griffin validate endpoint
curl "http://7108dev-auth.d8200.mil/authorization/validate?token=test"
# Expected: "false" (invalid token, but service is reachable)

# Test Griffin authentication endpoint
curl "http://7108dev-auth.d8200.mil/authentication?tokenConsumerURL=http://test"
# Expected: HTTP 302 redirect to ADFS
```

#### Log Monitoring

**Check logs for errors:**

```bash
# Systemd logs
sudo journalctl -u shiftmanager.service --since "1 hour ago" | grep -i error

# Application logs (if file logging enabled)
tail -f /var/log/shiftmanager/app.log | grep -i griffin

# Look for success messages
sudo journalctl -u shiftmanager.service | grep "Griffin authentication successful"
```

**Expected log patterns:**

| Event | Log Message | Level |
|-------|-------------|-------|
| App start | "Application started" | INFO |
| Griffin enabled | "Griffin config loaded: Enabled=true" | INFO |
| ADFS login success | "User authenticated via Griffin ADFS: user@domain.mil" | INFO |
| ADFS login failure | "Griffin authentication failed: Invalid token" | WARNING |
| Griffin unavailable | "Griffin ADFS unavailable" | WARNING |
| Auto-provision | "Auto-provisioned Griffin user: user@domain.mil" | INFO |

#### Plain-Language Explanation

**How to check if everything is working:**

**Quick checks:**
1. Can you open the website? ✅
2. Can you see the ADFS login button? ✅
3. Can you log in with ADFS? ✅
4. Can you log in with email/password? ✅

**If something is wrong:**

| Problem | How to Check | Where to Look |
|---------|--------------|---------------|
| Website not loading | `curl http://localhost:5000` | Application logs |
| Database errors | Check database file exists | Database logs |
| Griffin not working | Click "Test Connection" in Owner menu | Application logs for "Griffin unavailable" |
| ADFS login fails | Try logging in, check what error you see | Audit logs for authentication failures |

**Commands to check logs:**
```bash
# See last 100 lines of logs
sudo journalctl -u shiftmanager.service -n 100

# See only errors
sudo journalctl -u shiftmanager.service | grep ERROR

# See Griffin-related logs
sudo journalctl -u shiftmanager.service | grep Griffin
```

---

### 7.4 Operational Runbook

#### Daily Operations

**Morning Checklist:**
- [ ] Check service status: `sudo systemctl status shiftmanager.service`
- [ ] Check disk space: `df -h`
- [ ] Review overnight logs: `sudo journalctl -u shiftmanager.service --since yesterday`
- [ ] Verify login page accessible
- [ ] Check audit log for unusual activity

**Weekly Checklist:**
- [ ] Review authentication failures (Owner → Audit Log)
- [ ] Check for duplicate user accounts
- [ ] Review auto-provisioned users
- [ ] Database backup verification
- [ ] Certificate expiration check (if using HTTPS)

#### Restart Procedure

**Planned Restart:**

```bash
# 1. Notify users (via email/announcement)
"System maintenance in 15 minutes. Please save your work."

# 2. Wait for maintenance window
sleep 900  # 15 minutes

# 3. Stop service gracefully
sudo systemctl stop shiftmanager.service

# 4. Wait for all connections to close
sleep 10

# 5. Verify stopped
sudo systemctl status shiftmanager.service

# 6. Start service
sudo systemctl start shiftmanager.service

# 7. Verify started
sudo systemctl status shiftmanager.service
curl http://localhost:5000/health

# 8. Monitor logs for 5 minutes
sudo journalctl -u shiftmanager.service -f

# 9. Test login (ADFS and email/password)

# 10. Notify users
"System maintenance complete. Service is available."
```

**Emergency Restart:**

```bash
# 1. Force restart immediately
sudo systemctl restart shiftmanager.service

# 2. Check logs for errors
sudo journalctl -u shiftmanager.service -n 50

# 3. Test login
curl http://localhost:5000/health
```

#### Common Errors & Recovery

**Error 1: Service Won't Start**

```bash
# Symptoms
sudo systemctl status shiftmanager.service
# Shows: "failed" or "inactive (dead)"

# Diagnosis
sudo journalctl -u shiftmanager.service -n 100
# Look for error messages

# Common causes & fixes
# 1. Port already in use
sudo lsof -i :5000
sudo kill <PID>

# 2. Missing database file
ls /var/www/shiftmanager/shiftmanager.db
# If missing, restore from backup

# 3. Permission issues
sudo chown -R www-data:www-data /var/www/shiftmanager
sudo chmod -R 755 /var/www/shiftmanager

# 4. Configuration error
cat /var/www/shiftmanager/appsettings.json | jq .
# Check JSON syntax
```

**Error 2: Griffin Unavailable**

```bash
# Symptoms
# Login page shows: "Griffin ADFS is temporarily unavailable"

# Diagnosis
# Test Griffin connectivity
curl http://7108dev-auth.d8200.mil/authentication?tokenConsumerURL=test

# If fails:
# 1. Check Griffin service status (contact IT)
# 2. Check network connectivity
ping 7108dev-auth.d8200.mil

# 3. Check firewall rules
sudo iptables -L -n | grep 7108dev-auth

# Temporary workaround
# Users can still login via email/password (fallback)
```

**Error 3: Database Locked**

```bash
# Symptoms
# Error: "database is locked"

# Cause
# SQLite concurrent write limit reached

# Fix
# 1. Identify locking process
lsof /var/www/shiftmanager/shiftmanager.db

# 2. Restart application
sudo systemctl restart shiftmanager.service

# Long-term solution
# Migrate to SQL Server for production (supports concurrent writes)
```

**Error 4: Certificate Expired**

```bash
# Symptoms
# Browser shows: "SSL certificate expired"

# Diagnosis
openssl x509 -in /etc/ssl/certs/shiftmanager.pfx -text -noout | grep "Not After"

# Fix
# 1. Obtain new certificate from IT
# 2. Replace certificate file
sudo cp new-certificate.pfx /etc/ssl/certs/shiftmanager.pfx
sudo chmod 600 /etc/ssl/certs/shiftmanager.pfx

# 3. Restart service
sudo systemctl restart shiftmanager.service

# 4. Verify
curl -v https://shiftmanager.d8200.mil 2>&1 | grep "expire"
```

#### Backup & Recovery

**Backup Strategy:**

```bash
# Daily backup script (run via cron)
#!/bin/bash
DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_DIR=/backups/shiftmanager

# Backup database
sqlite3 /var/www/shiftmanager/shiftmanager.db ".backup '$BACKUP_DIR/db_$DATE.db'"

# Backup configuration
cp /var/www/shiftmanager/appsettings.json $BACKUP_DIR/config_$DATE.json

# Backup logs
cp /var/log/shiftmanager/app.log $BACKUP_DIR/logs_$DATE.log

# Compress
tar -czf $BACKUP_DIR/shiftmanager_backup_$DATE.tar.gz $BACKUP_DIR/{db_$DATE.db,config_$DATE.json,logs_$DATE.log}

# Cleanup old backups (keep 30 days)
find $BACKUP_DIR -name "*.tar.gz" -mtime +30 -delete

echo "Backup complete: $BACKUP_DIR/shiftmanager_backup_$DATE.tar.gz"
```

**Recovery Procedure:**

```bash
# 1. Stop service
sudo systemctl stop shiftmanager.service

# 2. Restore database from backup
cp /backups/shiftmanager/db_20251212_120000.db /var/www/shiftmanager/shiftmanager.db

# 3. Restore configuration
cp /backups/shiftmanager/config_20251212_120000.json /var/www/shiftmanager/appsettings.json

# 4. Set permissions
sudo chown www-data:www-data /var/www/shiftmanager/shiftmanager.db
sudo chown www-data:www-data /var/www/shiftmanager/appsettings.json

# 5. Start service
sudo systemctl start shiftmanager.service

# 6. Verify
sudo systemctl status shiftmanager.service
curl http://localhost:5000/health

# 7. Test login
# Try both ADFS and email/password
```

#### Plain-Language Explanation

**Daily tasks:**
- Check that the app is running
- Look at logs for errors
- Make sure login page works

**Weekly tasks:**
- Review who logged in
- Check for duplicate accounts
- Verify backups are working

**If the app crashes:**
1. Restart it: `sudo systemctl restart shiftmanager.service`
2. Check logs to see what went wrong
3. If you can't fix it, restore from backup
4. If that doesn't work, call for help

**If Griffin breaks:**
- Don't panic - email/password login still works
- Contact IT to fix Griffin
- Users can keep working in the meantime

**Backups:**
- Happen automatically every day
- Keep for 30 days
- Store in `/backups/shiftmanager/`
- To restore: copy backup files back, restart app

---

## 8. FAQ - Everything an Owner Might Ask

### 8.1 Enabling/Disabling Griffin

**Q: Can we disable Griffin temporarily?**

**Technical Answer:**
Yes. Navigate to `/Owner/GriffinConfig` and uncheck "Enable Griffin ADFS", then click "Save". This takes effect immediately on the next login attempt. Existing sessions remain active until they expire.

**Plain-Language Answer:**
Yes! Go to Owner → Griffin ADFS and turn it off. Users who are already logged in stay logged in. New logins will only see the email/password option.

---

**Q: What happens if Griffin is down?**

**Technical Answer:**
The application detects Griffin unavailability via `TestConnectionAsync()` timeout. Login page displays warning message. Email/password authentication remains fully functional as fallback. No data loss occurs. Users experience no interruption if they use local credentials.

**Plain-Language Answer:**
Email/password login still works! Users see a warning message but can log in normally. Nobody gets locked out. When Griffin comes back online, ADFS login works again automatically.

---

**Q: Can we disable it for one company but keep it for another?**

**Technical Answer:**
Yes. `GriffinConfig` is scoped per `CompanyId` (multi-tenant). Each company has independent configuration. Company A can have `Enabled = true` while Company B has `Enabled = false`. No interference between companies.

**Plain-Language Answer:**
Yes! Each company has its own Griffin settings. You can turn it on for some companies and off for others. They don't affect each other.

---

### 8.2 User Onboarding

**Q: How do we onboard a new employee?**

**Technical Answer:**

**Option A (Auto-Provision Enabled):**
1. Employee authenticates via ADFS on first login
2. System calls `/authorization/getClaims` to retrieve profile
3. Creates `AppUser` with `Role = DefaultProvisionedRole`
4. Employee logged in immediately, audit log entry created

**Option B (Manual Approval):**
1. Employee authenticates via ADFS
2. System shows "pending approval" message
3. Admin creates user manually via `/Owner/Users`
4. Employee can log in on next attempt

**Plain-Language Answer:**

**If auto-provision is ON:**
1. New employee goes to login page
2. Clicks "Login with ADFS"
3. Logs in with work credentials
4. Account created automatically
5. They can start using the app immediately

**If auto-provision is OFF:**
1. New employee tries to log in
2. Sees "pending approval" message
3. Admin gets notified (check audit logs)
4. Admin manually adds them via Owner → Users
5. Employee can log in after admin approves

**Recommended:** Turn auto-provision ON (less admin work)

---

**Q: How do we migrate existing users to ADFS?**

**Technical Answer:**

```sql
-- BEFORE enabling Griffin: Audit email mismatches
SELECT
    u.Email as AppEmail,
    ad.UserPrincipalName as ADFSEmail,
    CASE
        WHEN LOWER(u.Email) = LOWER(ad.UserPrincipalName) THEN 'Match'
        ELSE 'MISMATCH - NEEDS UPDATE'
    END as Status
FROM AppUsers u
LEFT JOIN (
    SELECT UserPrincipalName, DisplayName
    FROM AD_User_Export  -- ← Export from Active Directory
) ad ON LOWER(u.Email) = LOWER(ad.UserPrincipalName)
WHERE u.IsActive = 1;

-- Update mismatched emails
UPDATE AppUsers
SET Email = '<correct-upn-from-adfs>'
WHERE Id = <user-id>;

-- Enable Griffin
UPDATE GriffinConfigs
SET Enabled = 1
WHERE CompanyId = <your-company>;
```

**Migration Steps:**
1. Export user list from ADFS/Active Directory (IT helps)
2. Compare with app database emails
3. Update mismatched emails in app
4. Enable Griffin
5. Test with pilot group (5-10 users)
6. Roll out to everyone
7. Monitor for duplicate accounts

**Plain-Language Answer:**

**Before turning on Griffin:**
1. Get list of all employees from IT (with their ADFS emails)
2. Compare with emails in your app
3. Fix any mismatches (update app to match ADFS)
4. Turn on Griffin for 5-10 people first (pilot test)
5. If no problems, turn on for everyone
6. Watch for duplicate accounts in the first week

**Common mismatches:**
- App has `john@company.com`, ADFS has `john@company.mil`
- App has `john.doe@domain`, ADFS has `jdoe@domain`
- Fix these BEFORE enabling Griffin

---

### 8.3 Auditing & Compliance

**Q: How do we audit who logged in via Griffin?**

**Technical Answer:**

```csharp
// All Griffin logins logged via SecurityLogger
_securityLogger.LogAuthenticationSuccess(userId, email, role, ipAddress);

// Query audit logs
SELECT
    Timestamp,
    UserId,
    Email,
    Action,
    Details
FROM AuditLogs
WHERE Action = 'AuthenticationSuccess'
  AND Details LIKE '%Griffin%'
ORDER BY Timestamp DESC;

// Claims stored in ClaimsPrincipal include AuthMethod
var authMethod = User.FindFirst("AuthMethod")?.Value;  // "Griffin" or "Password"
```

**Audit Report Query:**

```sql
-- Griffin logins in last 30 days
SELECT
    DATE(Timestamp) as Date,
    COUNT(*) as LoginCount,
    COUNT(DISTINCT Email) as UniqueUsers
FROM AuditLogs
WHERE Action = 'AuthenticationSuccess'
  AND Details LIKE '%Griffin%'
  AND Timestamp >= datetime('now', '-30 days')
GROUP BY DATE(Timestamp)
ORDER BY Date DESC;

-- Users who have NEVER used Griffin
SELECT
    u.Email,
    u.DisplayName,
    MAX(a.Timestamp) as LastLogin
FROM AppUsers u
LEFT JOIN AuditLogs a ON a.Email = u.Email
WHERE u.IsActive = 1
  AND (a.Details NOT LIKE '%Griffin%' OR a.Details IS NULL)
GROUP BY u.Email;
```

**Plain-Language Answer:**

**To see who logged in via ADFS:**
1. Go to Owner → Audit Log
2. Filter by "AuthenticationSuccess"
3. Look for entries mentioning "Griffin"
4. You'll see: who, when, from what IP address

**Reports you can generate:**
- How many people used ADFS login today/this week/this month
- Who has never used ADFS login (still using passwords)
- What times people log in (detect unusual activity)

**All authentication events are logged forever** (unless you manually delete old logs)

---

**Q: What data do we store from ADFS?**

**Technical Answer:**

**Stored in Database:**

| Field | Source | Example | Retention |
|-------|--------|---------|-----------|
| `Email` | ADFS `UPN` | john.doe@domain.mil | Permanent |
| `DisplayName` | ADFS `DisplayName` | Doe, John A. | Permanent |
| (no password) | N/A | Empty | N/A |

**Stored in Claims (Session Only):**

| Claim | Source | Example | Retention |
|-------|--------|---------|-----------|
| `Griffin:sAMAccountName` | ADFS | jdoe | Session (7 days) |
| `Griffin:Token` | Griffin service | abc123xyz | Session (7 days) |
| `Griffin:AuthTime` | ADFS | 2025-01-15T14:30:00Z | Session (7 days) |
| `AuthMethod` | Our app | "Griffin" | Session (7 days) |

**NOT Stored:**
- ❌ ADFS password
- ❌ CAC PIN
- ❌ ADFS group membership
- ❌ Full token contents (only hash)

**Cached Temporarily (8 hours, in-memory only):**
- Token validation result
- User claims (UPN, DisplayName, etc.)

**Cache is NOT persisted** - cleared on app restart.

**Plain-Language Answer:**

**What we save forever:**
- Your email (from ADFS)
- Your name (from ADFS)
- When you logged in (audit log)

**What we save temporarily (while you're logged in):**
- Your session token
- Your Windows username
- When you authenticated

**What we NEVER save:**
- Your password
- Your CAC PIN
- Any sensitive ADFS data

**When you log out:** Temporary data is deleted immediately. Permanent data (email, name, audit logs) remains for record-keeping.

---

### 8.4 Troubleshooting

**Q: User says "I can't log in via ADFS"**

**Troubleshooting Steps:**

1. **Verify ADFS is working:**
   - Can they log into other ADFS apps?
   - If no: IT issue, contact ADFS admin
   - If yes: Continue

2. **Check Griffin service:**
   - Go to Owner → Griffin ADFS
   - Click "Test Connection"
   - If fails: Griffin is down, contact IT
   - If succeeds: Continue

3. **Check user account:**
   - Go to Owner → Users
   - Search for user's email
   - Does email match their ADFS UPN?
   - Is user marked as Active?

4. **Check auto-provision:**
   - Go to Owner → Griffin ADFS
   - Is "Auto-Provision" enabled?
   - If no: You must manually create the user
   - If yes: Continue

5. **Check logs:**
   - Go to Owner → Audit Log
   - Filter by user's email
   - Look for authentication failures
   - Error message will indicate issue

**Common Issues:**

| Error Message | Cause | Fix |
|---------------|-------|-----|
| "Your account is pending approval" | Auto-provision disabled | Enable auto-provision OR manually create user |
| "Authentication failed: incomplete information" | ADFS profile missing UPN | Contact IT to fix user's ADFS profile |
| "Griffin ADFS is temporarily unavailable" | Griffin down | Contact IT OR use email/password login |
| "Session expired" | Token expired | Log in again (normal) |
| Nothing happens after clicking button | JavaScript error | Clear browser cache, try different browser |

---

**Q: We have duplicate accounts for the same person**

**Diagnosis:**

```sql
-- Find potential duplicates by name
SELECT DisplayName, GROUP_CONCAT(Email) as Emails, COUNT(*) as Count
FROM AppUsers
WHERE IsActive = 1
GROUP BY DisplayName
HAVING COUNT(*) > 1;

-- Example result:
-- DisplayName: "John Doe"
-- Emails: "john.doe@company.com,john.doe@company.mil"
-- Count: 2
```

**Resolution:**

1. **Identify primary account** (usually older one with data)
2. **Identify duplicate** (usually newer, empty)
3. **DO NOT DELETE YET**
4. **Transfer data** from duplicate to primary:
   ```sql
   -- Transfer shifts
   UPDATE Shifts SET UserId = <primary-id> WHERE UserId = <duplicate-id>;

   -- Transfer time-off requests
   UPDATE TimeOffRequests SET UserId = <primary-id> WHERE UserId = <duplicate-id>;

   -- Transfer swap requests
   UPDATE SwapRequests SET RequesterId = <primary-id> WHERE RequesterId = <duplicate-id>;
   UPDATE SwapRequests SET TargetId = <primary-id> WHERE TargetId = <duplicate-id>;

   -- Update audit logs
   UPDATE AuditLogs SET UserId = <primary-id> WHERE UserId = <duplicate-id>;
   ```
5. **Deactivate duplicate:**
   ```sql
   UPDATE AppUsers SET IsActive = 0 WHERE Id = <duplicate-id>;
   ```
6. **Update email on primary** if needed:
   ```sql
   UPDATE AppUsers SET Email = '<correct-adfs-upn>' WHERE Id = <primary-id>;
   ```

**Prevention:**
- Ensure emails match ADFS UPNs BEFORE enabling Griffin
- Periodically audit for duplicates (monthly)
- When someone's email changes, update in app manually first

---

**Q: "Test Connection" fails in Owner menu**

**Diagnosis:**

1. **Check URL:**
   - Is Griffin Base URL correct?
   - Should be `http://hostname` or `https://hostname`
   - No trailing slash
   - Example: `http://7108dev-auth.d8200.mil`

2. **Check network:**
   ```bash
   # From app server, test connectivity
   curl -v http://7108dev-auth.d8200.mil/authentication?tokenConsumerURL=test
   ```
   - If connection refused: Griffin service is down
   - If timeout: Firewall blocking connection
   - If 404: Wrong URL path
   - If 302 redirect: Griffin is working!

3. **Check timeout:**
   - Is timeout too short (less than 5 seconds)?
   - Increase to 10 seconds
   - Try "Test Connection" again

4. **Check logs:**
   ```bash
   sudo journalctl -u shiftmanager.service | grep Griffin
   ```
   - Look for error messages
   - "Connection refused" = Griffin down
   - "Timeout" = Network issue or Griffin slow

**Resolution:**

| Symptom | Fix |
|---------|-----|
| Connection refused | Contact IT to start Griffin service |
| Timeout | Check firewall, increase timeout setting |
| Wrong hostname | Update Griffin Base URL in settings |
| SSL/TLS error | Check if URL should be HTTP or HTTPS |

---

### 8.5 Security & Compliance

**Q: Is it secure to store tokens in cookies?**

**Technical Answer:**

Yes, with proper safeguards:

```csharp
// Cookie configuration
Response.Cookies.Append("griffin.token", token, new CookieOptions
{
    HttpOnly = true,        // ✅ JavaScript cannot access (XSS protection)
    Secure = true,          // ✅ HTTPS only (MITM protection)
    SameSite = SameSiteMode.Lax,  // ✅ CSRF protection
    Expires = DateTimeOffset.UtcNow.AddHours(8),  // ✅ Limited lifetime
    Path = "/"              // ✅ App-wide scope
});
```

**Security Measures:**
- ✅ Tokens stored in `HttpOnly` cookies (not accessible via JavaScript)
- ✅ Tokens validated on every request
- ✅ Tokens expire after 8 hours (cached validation)
- ✅ Tokens cleared on logout
- ✅ All token operations logged
- ✅ Token treated as opaque string (no parsing/tampering)

**Comparison to Alternatives:**

| Storage Method | Security | Why Not Used |
|---------------|----------|--------------|
| Cookie (HttpOnly) | ✅ High | ← **We use this** |
| LocalStorage | ❌ Low | Vulnerable to XSS |
| SessionStorage | ⚠️ Medium | Lost on tab close |
| In-memory only | ✅ High | Lost on page refresh (bad UX) |

**Plain-Language Answer:**

Yes, it's secure! The token is stored in a special type of cookie that:
- ✅ Cannot be stolen by malicious scripts
- ✅ Only sent over encrypted connections (HTTPS)
- ✅ Automatically expires after 8 hours
- ✅ Deleted when you log out

This is the same way banks and other secure websites store session tokens.

---

**Q: Can users bypass ADFS login?**

**Technical Answer:**

No, if ADFS is the only authentication method. However, our implementation supports **dual authentication**:

```csharp
// Both methods are available simultaneously
if (Model.ShowGriffinButton)  // ← Always true now
{
    // ADFS login option
}

// Email/password login option (always available)
```

**Scenarios:**

| User Has | Can Log In Via |
|----------|----------------|
| ADFS credentials only | ✅ ADFS only |
| Password only | ✅ Email/password only |
| Both ADFS and password | ✅ Either method (user chooses) |

**To enforce ADFS-only:**
- Remove email/password login form from `Login.cshtml`
- Disable password-based authentication in `Login.cshtml.cs`
- **ASSUMPTION:** Current implementation allows both (flexibility for users)

**Plain-Language Answer:**

In our current setup: **Both login methods work**.

- Users can choose ADFS OR email/password
- If you want to force everyone to use ADFS only, we can change the code
- **Recommendation:** Keep both methods - it's safer (if ADFS breaks, people can still log in)

---

**Q: What happens if someone's ADFS account is disabled?**

**Technical Answer:**

**ADFS Account Disabled:**
1. User attempts ADFS login
2. Redirected to ADFS login page
3. ADFS rejects credentials (account disabled)
4. User sees ADFS error message (controlled by IT)
5. User cannot complete login
6. No token is issued by Griffin

**App Behavior:**
- User redirected back to login page (no token = failed auth)
- App user account remains Active (not automatically disabled)
- If user has password, they can still log in via email/password

**Recommended Process:**

```plaintext
When employee leaves organization:
1. IT disables ADFS account
2. Admin disables app account:
   - Go to Owner → Users
   - Find user
   - Click "Deactivate"
3. User cannot access app via any method
```

**Auto-Sync (Future Enhancement):**
- Could query ADFS/AD daily to sync account status
- Automatically disable app accounts when ADFS disabled
- **OPEN QUESTION:** Should this be implemented?

**Plain-Language Answer:**

**If someone is terminated:**

1. **IT disables their ADFS account** (standard procedure)
2. **They can't log in via ADFS anymore**
3. **BUT:** If they have a password, they can still log in!
4. **YOU must also deactivate them** in Owner → Users

**Best practice:**
- When IT disables someone in ADFS, also disable them in your app
- Check weekly: Are there ADFS-disabled users who are still active in the app?

---

## 9. Troubleshooting

### 9.1 Login Issues

**Problem: "ADFS button doesn't appear"**

**Check:**
1. Clear browser cache
2. Verify JavaScript not blocked
3. Check `Login.cshtml` - button should NOT be wrapped in conditional
4. Check browser console for errors (F12)

**Fix:**
- Button is now always visible (update to latest code)
- If still missing, check HTML source for `LoginWithADFS` string

---

**Problem: "Clicking ADFS button does nothing"**

**Check:**
1. Browser console for JavaScript errors (F12 → Console tab)
2. Network tab - is POST request sent? (F12 → Network tab)
3. Is form handler registered? Check `Login.cshtml.cs` for `OnPostGriffinAsync`

**Fix:**
- Clear browser cache
- Try different browser
- Check application logs for routing errors

---

**Problem: "Redirected to ADFS but then 404 error"**

**Check:**
1. Griffin Callback URL in Owner → Griffin ADFS settings
2. Should match: `https://your-actual-domain/Auth/GriffinCallback`
3. **NOT** `http://localhost` in production

**Fix:**
```
Correct URL format:
{protocol}://{actual-hostname}/Auth/GriffinCallback

Examples:
✅ https://shiftmanager.d8200.mil/Auth/GriffinCallback
❌ http://localhost:5001/Auth/GriffinCallback (wrong - local dev URL)
❌ https://shiftmanager.d8200.mil (wrong - missing /Auth/GriffinCallback)
```

---

**Problem: "Authentication failed: incomplete information"**

**Cause:** ADFS profile missing required field (UPN)

**Fix:**
1. Contact IT department
2. User's ADFS profile needs `userPrincipalName` attribute
3. IT admin must update ADFS configuration to include UPN in claims

---

**Problem: "Session expired, please login" appearing immediately after login**

**Cause:** Token validation failing

**Check:**
1. Application logs: `sudo journalctl -u shiftmanager.service | grep Griffin`
2. Look for "Invalid token" or "Griffin unavailable"
3. Test Griffin connectivity: Owner → Griffin ADFS → "Test Connection"

**Fix:**
- If Griffin down: Contact IT
- If Griffin working: Check token expiration settings
- Clear browser cookies and try again

---

### 9.2 Service Issues

**Problem: Application won't start**

```bash
# Check service status
sudo systemctl status shiftmanager.service

# Common issues:

# 1. Port already in use
sudo lsof -i :5000
# Fix: Kill process or change port

# 2. Database locked
lsof /var/www/shiftmanager/shiftmanager.db
# Fix: Restart service

# 3. Permission error
sudo chown -R www-data:www-data /var/www/shiftmanager
sudo chmod -R 755 /var/www/shiftmanager

# 4. Configuration error
cat /var/www/shiftmanager/appsettings.json | jq .
# Fix: Validate JSON syntax
```

---

**Problem: "Griffin ADFS is temporarily unavailable" message**

**Diagnosis:**

```bash
# Test from app server
curl -v http://7108dev-auth.d8200.mil/authentication?tokenConsumerURL=test

# Expected: HTTP 302 redirect
# If connection refused: Griffin service down
# If timeout: Network/firewall issue
```

**Fix:**
- Contact IT to check Griffin service status
- Check firewall rules
- Increase timeout in Owner → Griffin ADFS settings (if slow network)

---

**Problem: High memory usage**

**Diagnosis:**

```bash
# Check memory usage
ps aux | grep ShiftManager

# Check cache size (claims cache)
# No built-in tool - cache is in-memory only
```

**Cause:** Claims cache growing large (unlikely - 8 hour expiration)

**Fix:**
- Restart service to clear cache: `sudo systemctl restart shiftmanager.service`
- Monitor over time: `watch -n 10 'ps aux | grep ShiftManager'`

---

### 9.3 Database Issues

**Problem: Duplicate users appearing**

**Find duplicates:**

```sql
sqlite3 shiftmanager.db

SELECT DisplayName, GROUP_CONCAT(Email) as Emails, COUNT(*) as Count
FROM AppUsers
WHERE IsActive = 1
GROUP BY DisplayName
HAVING COUNT(*) > 1;
```

**Fix:** See FAQ section 8.4 "We have duplicate accounts for the same person"

---

**Problem: Griffin configuration not saving**

**Check:**
```sql
sqlite3 shiftmanager.db
SELECT * FROM GriffinConfigs;
```

**If empty:** Migration not applied

**Fix:**
```bash
dotnet ef database update
# Verify migration applied
sqlite3 shiftmanager.db "SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId LIKE '%Griffin%';"
```

---

### 9.4 Network/Connectivity Issues

**Problem: Cannot reach Griffin from app server**

**Test connectivity:**

```bash
# Ping hostname
ping 7108dev-auth.d8200.mil

# Test HTTP connection
curl -v http://7108dev-auth.d8200.mil/authentication?tokenConsumerURL=test

# Check DNS resolution
nslookup 7108dev-auth.d8200.mil

# Check firewall rules
sudo iptables -L -n -v | grep 7108dev-auth
```

**Common causes:**
- DNS not resolving hostname
- Firewall blocking port 80/443
- Griffin service not listening on expected port
- Wrong hostname in configuration

**Fix:**
- Work with IT to open firewall rules
- Verify Griffin service deployment
- Update hostname in Owner → Griffin ADFS if wrong

---

**Problem: Users can reach login page but not ADFS**

**Cause:** User's browser cannot reach ADFS (different network zone)

**Check:**
- From user's machine: Can they ping ADFS server?
- Is ADFS on different network segment?
- Does user need VPN connection?

**Fix:**
- Verify user's network connectivity
- Check with IT about network routing
- Ensure users are on correct network to access ADFS

---

## 10. Open Questions & Assumptions

### Assumptions Made

1. **ASSUMPTION:** Griffin does NOT provide a logout endpoint
   - **Impact:** Logout only clears app session, not ADFS session
   - **Mitigation:** Document that users should close browser for full logout
   - **Verification Needed:** Check Griffin API documentation for logout endpoint

2. **ASSUMPTION:** Email (UPN) is sufficient for user matching
   - **Impact:** If UPN changes, user gets duplicate account
   - **Alternative:** Use `sAMAccountName` or employee ID as secondary key
   - **Verification Needed:** How often do UPNs change in your organization?

3. **ASSUMPTION:** Auto-provisioning is acceptable for trusted ADFS environment
   - **Impact:** Anyone with valid ADFS credentials can create account
   - **Alternative:** Require manual approval for new users
   - **Verification Needed:** What is your org's security policy?

4. **ASSUMPTION:** Both authentication methods (ADFS + password) should coexist
   - **Impact:** Users can choose either method
   - **Alternative:** Force ADFS-only authentication
   - **Verification Needed:** Should password auth be disabled when Griffin enabled?

5. **ASSUMPTION:** Token validation via cache (8 hours) is acceptable
   - **Impact:** Disabled user might access app for up to 8 hours
   - **Alternative:** Validate token on every request (slower, more secure)
   - **Verification Needed:** What is acceptable security/performance trade-off?

6. **ASSUMPTION:** ADFS group membership is NOT used for role assignment
   - **Impact:** Roles must be assigned manually in app
   - **Alternative:** Map ADFS groups to app roles automatically
   - **Verification Needed:** Does ADFS include group membership in claims?

7. **ASSUMPTION:** Griffin is only available in production (air-gapped) environment
   - **Impact:** Local development uses fallback authentication only
   - **Alternative:** Mock Griffin service for local testing
   - **Verification Needed:** Do developers need to test Griffin flows locally?

8. **ASSUMPTION:** SQLite is acceptable for production database
   - **Impact:** Limited concurrent write performance
   - **Alternative:** Migrate to SQL Server for production
   - **Verification Needed:** How many concurrent users are expected?

### Open Questions

1. **Q:** Should we implement a mock Griffin service for local development?
   - **Why:** Developers could test full ADFS flow without air-gapped access
   - **How:** `#if DEBUG` conditional mock service returning fake claims
   - **Decision Needed:** Is this worth the development effort?

2. **Q:** Should we add duplicate user detection/merge tool to Owner menu?
   - **Why:** Automate finding and fixing duplicate accounts
   - **How:** Report page showing potential duplicates, one-click merge
   - **Decision Needed:** How often do duplicates occur?

3. **Q:** Should we sync account status with ADFS daily?
   - **Why:** Automatically disable app accounts when ADFS account disabled
   - **How:** Scheduled job queries ADFS/AD for account status
   - **Decision Needed:** Is this operationally necessary?

4. **Q:** Should we implement ADFS group-to-role mapping?
   - **Why:** Automatically assign roles based on ADFS group membership
   - **How:** Check Griffin claims for group membership, map to app roles
   - **Decision Needed:** Does Griffin/ADFS include group claims?

5. **Q:** Should we enforce ADFS-only authentication (remove password option)?
   - **Why:** Stronger security - single auth method
   - **How:** Remove email/password form when Griffin enabled
   - **Decision Needed:** What if Griffin is down - how do admins access app?

6. **Q:** Should we add health check endpoint for Griffin connectivity?
   - **Why:** Monitoring systems can detect Griffin outages
   - **How:** `/health/griffin` endpoint that tests connectivity
   - **Decision Needed:** Is this needed for ops monitoring?

7. **Q:** Should we migrate from SQLite to SQL Server for production?
   - **Why:** Better concurrent write performance, better for multi-user
   - **How:** Update connection string, re-run migrations
   - **Decision Needed:** How many concurrent users are expected?

8. **Q:** Should we add notification when new user tries to log in (auto-provision disabled)?
   - **Why:** Admin knows immediately when approval needed
   - **How:** Email notification to admin email address
   - **Decision Needed:** Is manual approval workflow used?

9. **Q:** Should we add secondary identifier (employee ID, SAM account name) for user matching?
   - **Why:** More robust matching when email changes
   - **How:** Store `sAMAccountName` from ADFS, use as fallback for matching
   - **Decision Needed:** What is immutable identifier in your ADFS?

10. **Q:** Should token validation cache be configurable (currently hardcoded to 8 hours)?
    - **Why:** Allow security/performance tuning per organization
    - **How:** Add `ClaimsCacheTtlSeconds` to GriffinConfig model
    - **Decision Needed:** Is 8 hours acceptable for all scenarios?

### Verification Checklist

Before going to production, verify:

- [ ] Griffin service endpoint is correct and reachable
- [ ] ADFS is configured to include UPN in claims
- [ ] Callback URL matches production domain (not localhost)
- [ ] SSL certificates are valid (if using HTTPS)
- [ ] All user emails in database match ADFS UPNs
- [ ] Auto-provision setting matches security policy
- [ ] Default role is appropriate (Employee recommended)
- [ ] Backup strategy is in place
- [ ] Monitoring/alerting configured
- [ ] Audit log retention policy defined
- [ ] User training completed (how to use ADFS login)
- [ ] Fallback plan if Griffin goes down
- [ ] Admin contact information documented

---

## Summary

This guide covers:

✅ **What Griffin is** - ADFS wrapper for air-gapped authentication
✅ **What we implemented** - Dual authentication, auto-provisioning, localization
✅ **Authentication flows** - Login, logout, token lifecycle, failure scenarios
✅ **User lifecycle** - Auto-provisioning, identity mapping, duplicate handling
✅ **Configuration** - Owner menu settings, safe defaults, common misconfigurations
✅ **Local development** - How to run and test locally without Griffin
✅ **Air-gapped setup** - Step-by-step installation, health checks, operational runbook
✅ **FAQ** - Every question an owner might ask
✅ **Troubleshooting** - Common problems and solutions
✅ **Open questions** - Assumptions and decisions needed

**For additional help:**
- Check application logs: `sudo journalctl -u shiftmanager.service`
- Check audit logs: Owner → Audit Log
- Contact your IT department for Griffin/ADFS issues
- Contact your development team for application issues

**Document Version:** 1.0
**Last Updated:** 2025-12-12
**Next Review:** (Set based on your update cycle)
