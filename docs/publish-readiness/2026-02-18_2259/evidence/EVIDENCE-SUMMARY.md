# Evidence Summary

## Runtime Evidence

### Build & Tests
```
$ dotnet build --no-restore
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --no-restore
Passed!  - Failed:     0, Passed:   236, Skipped:     0, Total:   236
```

### Health Endpoints
```
$ curl -s http://localhost:5000/health
Healthy

$ curl -s http://localhost:5000/ready
# Returns 503 — DiskSpaceHealthCheck correctly reports:
# "Critical: 59375MB free (6.2%). Below 10.0% threshold."
# This is an ENVIRONMENT issue (test machine disk), not a code bug.

$ curl -s http://localhost:5000/api/v1/version
# Returns 401: {"detail":"Missing X-API-Key header"}
# → FINDING-001
```

### Application Startup (from console output)
- 4 background services started: EmailBackgroundProcessor, DailyNotificationJob, DatabaseBackupService, GracefulShutdownService
- 55 feature flags loaded from database
- SQLite WAL mode enabled, busy_timeout=5000ms
- Listening on http://localhost:5000

### Playwright Browser Testing
1. **Homepage**: Loaded successfully in Hebrew (RTL), navigation sidebar visible
2. **Calendar/Shifts**: Page loaded, shift type dropdowns populated by molecule/JobType
3. **Scope Switcher**: Showed all companies grouped by molecule, switching worked
4. **Vacation Request Form**: Loaded at /Requests/TimeOff/Create, all fields rendered
5. **Vacation Request Submit**: POST succeeded, redirected to /Requests#timeoff showing "1 pending vacation request" with Approve/Decline buttons

## Code Review Evidence

### FINDING-002 Verification
**File**: `Pages/Calendar/Table.cshtml.cs`, line 531
```csharp
public async Task<IActionResult> OnPostAssignEmployeeAsync([FromBody] AssignEmployeeRequest request)
{
    // Gets companyId, creates shift instance, validates, assigns
    // NO CALL to _grantService.HasGrantAsync() or any authorization check
}
```

### FINDING-007 Verification
**File**: `Pages/Admin/Users.cshtml.cs`, line 507
```csharp
if (await _db.Users.AnyAsync(u => u.Email == NewEmail))
// ← Subject to EF query filter: WHERE CompanyId = @currentTenantId
// Missing IgnoreQueryFilters()
```

**Contrast** — `Pages/Auth/Signup.cshtml.cs`, line 222:
```csharp
.IgnoreQueryFilters()  // Global email check ← Correct
```

### FINDING-011 Verification
**File**: `Services/Api/UserApiService.cs`, lines 155-159
```csharp
using var hmac = new System.Security.Cryptography.HMACSHA512();
user.PasswordSalt = hmac.Key;          // 128-byte HMAC key
user.PasswordHash = hmac.ComputeHash(...); // 64-byte HMAC-SHA512 hash
```

**File**: `Models/PasswordHasher.cs`, lines 7-13
```csharp
// CreateHash: PBKDF2-SHA256, 100k iterations → 32-byte hash, 16-byte salt
// Verify: Re-derives PBKDF2 hash and compares with FixedTimeEquals
```

**Login path** (`Pages/Auth/Login.cshtml.cs`, line 210):
```csharp
PasswordHasher.Verify(Password, user.PasswordHash, user.PasswordSalt)
// Attempts PBKDF2 on HMACSHA512 key → produces 32-byte hash
// Compares against 64-byte HMACSHA512 hash → length mismatch → always false
```

**Result**: Users created via API cannot authenticate via web login.

## Phase 0 Scan Evidence

### Stub/Placeholder Hunt
- **4 TODO comments found**: All benign (UI enhancement deferred, tech debt markers)
- **0 NotImplementedException**: None in production code
- **0 dead services**: All 40+ registered services actively injected
- **0 orphaned pages**: All 154 Razor pages routed and linked
- **0 commented-out code blocks**: Codebase well-maintained

### Feature Flags
- 55 flags seeded, all `IsEnabled: true`
- Override path exists via Owner > Feature Flags admin page
- Legacy `Features` section in appsettings.json deprecated (except `EnforceCompanyScope`)
