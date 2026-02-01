# ShiftManager Risk Register

**Generated:** 2026-01-12
**Agent:** Antigravity Workspace Intake

---

## Risk Classification

| Severity | Description | Response Time |
|----------|-------------|---------------|
| 🔴 Critical | Security vulnerability, data leak, system down | Immediate |
| 🟠 High | Feature broken, significant UX impact | < 24 hours |
| 🟡 Medium | Minor feature issue, workaround available | < 1 week |
| 🟢 Low | Cosmetic, enhancement opportunity | Backlog |

---

## Active Risks

### 🔴 RISK-001: Limited Test Coverage
**Category:** Quality Assurance
**Status:** Active
**Severity:** Critical

**Description:**
The project has only 15 unit tests (DirectorService only), leaving ~95% of the codebase untested.

**Evidence:**
- `docs/context.md:29` — "Test Coverage: 15 unit tests (DirectorService only - 100% passing)"
- `ShiftManager.Tests/UnitTests/Services/` — Only contains `DirectorServiceTests.cs`

**Impact:**
- Bugs may reach production undetected
- Refactoring is risky without test safety net
- Multi-tenant isolation not verified by automated tests

**Mitigation:**
1. Prioritize tests for security-critical services (ApiKeyService, TenantResolver)
2. Add integration tests for API endpoints
3. Add multi-tenant isolation tests

**Owner:** Development Team

---

### 🔴 RISK-002: Multi-Tenant Query Filter Bypass
**Category:** Security
**Status:** Active
**Severity:** Critical

**Description:**
Use of `IgnoreQueryFilters()` can potentially expose cross-tenant data if not properly validated.

**Evidence:**
- `Data/AppDbContext.cs:436-511` — Query filters defined
- `docs/context.md:556-558` — Directors use `IgnoreQueryFilters()` with manual validation

**Impact:**
- Cross-tenant data leakage if validation fails
- Privacy/compliance violations

**Mitigation:**
1. Audit all uses of `IgnoreQueryFilters()`
2. Add service-layer validation for all cross-tenant queries
3. Add automated tests for tenant isolation

**Verification Step:**
```powershell
# Search for IgnoreQueryFilters usage
grep_search -Query "IgnoreQueryFilters" -SearchPath "c:\Users\katzi\Downloads\ShiftManager"
```

**Owner:** Security Lead

---

### 🔴 RISK-003: SQLite Concurrent Write Limitations
**Category:** Scalability
**Status:** Active
**Severity:** High

**Description:**
SQLite does not support true concurrent writes. Under high load, write operations may fail or queue.

**Evidence:**
- `ShiftManager.csproj:34` — Uses `Microsoft.EntityFrameworkCore.Sqlite`
- `appsettings.json:5-7` — `"Default": "Data Source=app.db"`

**Impact:**
- Performance degradation under load
- Potential write failures during peak usage
- Not suitable for high-concurrency scenarios

**Mitigation:**
1. Accept for air-gapped single-server deployments (current use case)
2. Document scalability limits
3. Consider SQL Server/PostgreSQL for future high-scale deployments

**Status:** Known limitation, acceptable for current use case

---

### 🟠 RISK-004: Non-Transactional Migration Operations
**Category:** Database Integrity
**Status:** Active
**Severity:** High

**Description:**
Some migrations contain SQLite PRAGMA operations that are non-transactional. If a migration fails mid-execution, the database may be left in an inconsistent state.

**Evidence:**
- `docs/context.md:714-718` — "Migrations #3, #8, #11 contain non-transactional PRAGMA operations"

**Impact:**
- Database corruption on failed migration
- Manual intervention required to fix
- Potential data loss

**Mitigation:**
1. **ALWAYS** backup database before migrations
2. Test migrations on copy of production data
3. Create rollback scripts for all migrations

**Owner:** Database Administrator

---

### 🟠 RISK-005: Griffin ADFS External Dependency
**Category:** Availability
**Status:** Active
**Severity:** Medium

**Description:**
Griffin ADFS authentication depends on external service. If Griffin is unavailable, users relying solely on ADFS cannot log in.

**Evidence:**
- `appsettings.json:87-94` — Griffin configuration
- `Middleware/GriffinAuthenticationMiddleware.cs` — ADFS integration

**Impact:**
- Users cannot authenticate via ADFS during outages
- Potential security gaps if fallback not properly configured

**Mitigation:**
1. ✅ Local email/password fallback always available
2. Login page shows warning when Griffin unavailable
3. Auto-provision disabled by default requires explicit enable

**Status:** Mitigated by design (dual authentication)

---

### 🟠 RISK-006: Plain Text API Keys Stored Temporarily
**Category:** Security
**Status:** Active
**Severity:** Medium

**Description:**
API keys are stored temporarily in plain text (`PlainTextKey` column) for Owner to copy after creation.

**Evidence:**
- `docs/context.md:194-200` — PlainTextKey stored temporarily for Owner-only retrieval
- `Data/AppDbContext.cs:61` — `ApiKeys` DbSet

**Impact:**
- If database is compromised, recently created API keys exposed
- Security audit concern

**Mitigation:**
1. Clear `PlainTextKey` after a short period (e.g., 24 hours)
2. Encourage Owners to copy keys immediately
3. Consider in-memory only (session-based) key display

**Owner:** Security Lead

---

### 🟡 RISK-007: Air-Gapped DLL Blocking
**Category:** Deployment
**Status:** Active
**Severity:** Medium

**Description:**
After USB transfer to air-gapped environments, Windows may block DLLs, causing application failure.

**Evidence:**
- `docs/README.md:462-467` — DLL blocking troubleshooting
- `UNBLOCK_FILES.bat` — Script to unblock files

**Impact:**
- Application fails to start after deployment
- Confusing error messages
- Deployment delays

**Mitigation:**
1. ✅ `UNBLOCK_FILES.bat` script provided
2. ✅ Documentation includes unblocking steps
3. ✅ `VERIFY_FILES.bat` checks for blocked files

**Status:** Mitigated with tooling

---

### 🟡 RISK-008: Background Job Single Instance Only
**Category:** Reliability
**Status:** Active
**Severity:** Medium

**Description:**
The `DailyNotificationJob` runs as a single instance. If multiple instances of the app run (e.g., load balancing), duplicate notifications may be sent.

**Evidence:**
- `Program.cs:164` — `AddHostedService<DailyNotificationJob>()`
- `Services/DailyNotificationJob.cs` — No distributed lock

**Impact:**
- Duplicate notifications in multi-instance deployments
- User annoyance

**Mitigation:**
1. Accept for single-instance deployment (current design)
2. Add distributed lock if multi-instance needed
3. Document limitation

**Status:** Known limitation for single-server deployment

---

### 🟡 RISK-009: Rollback Scripts Incomplete
**Category:** Operations
**Status:** Active
**Severity:** Medium

**Description:**
Not all migrations have corresponding rollback scripts.

**Evidence:**
- `docs/context.md:709-711` — "12 of 17 migrations have corresponding rollback SQL scripts (migrations 13, 14, 15, 16, 17 need rollback scripts)"

**Impact:**
- Cannot quickly rollback recent migrations
- Longer recovery time if issues found

**Mitigation:**
1. Create rollback scripts for migrations 13-17
2. Test rollback scripts before production deployment
3. Maintain rollback scripts for all future migrations

**Owner:** Development Team

---

### 🟡 RISK-010: Email Service Optional but Undocumented Failure
**Category:** User Experience
**Status:** Active
**Severity:** Low

**Description:**
Email service is disabled by default. If enabled but misconfigured, email failures may not be obvious to users.

**Evidence:**
- `appsettings.json:81-86` — `"Enabled": false`
- `Services/MailService.cs:63061 bytes` — Email service implementation

**Impact:**
- Users may not receive expected notifications
- Silent failures if email service misconfigured

**Mitigation:**
1. ✅ Log email send failures
2. Add admin notification for email failures
3. Add email health check in system diagnostics

**Owner:** Development Team

---

### 🟢 RISK-011: No Rate Limiting on Web UI
**Category:** Security
**Status:** Active
**Severity:** Low

**Description:**
Rate limiting is only applied to API endpoints (`/api/*`), not to web UI pages.

**Evidence:**
- `Program.cs:443-444` — API rate limiting middleware
- No equivalent for Razor pages

**Impact:**
- Potential brute force on login page
- Denial of service on heavy pages

**Mitigation:**
1. Add rate limiting middleware for login page
2. Consider WAF if deployed behind reverse proxy
3. Monitor for abuse patterns

**Owner:** Security Lead

---

### 🟢 RISK-012: Session/Cookie Security
**Category:** Security
**Status:** Active
**Severity:** Low

**Description:**
Cookie security settings are configured but should be verified in production.

**Evidence:**
- `Program.cs:74-78` — Cookie security settings
  - `HttpOnly = true`
  - `SecurePolicy = SameAsRequest`
  - `SameSite = Lax`

**Impact:**
- Minor risk if misconfigured

**Mitigation:**
1. ✅ Security settings already configured
2. Verify HTTPS in production
3. Consider `Strict` SameSite for additional protection

**Status:** Mitigated

---

## Closed Risks

### RISK-C001: Missing Query Filters (CLOSED)
**Closed Date:** Previous Release
**Resolution:** Fixed

**Description:**
Some entities were missing query filters, potentially leaking data.

**Evidence:**
- `Data/AppDbContext.cs:465-474` — "SECURITY FIX: Add query filters for previously missing entities"

**Resolution:**
Added query filters for: AppUser, AppConfig, RoleAssignmentAudit, UserJoinRequest, TeamCalendar, EmailConfig, Feedback, GameScore, CompanyLanguageSettings, CompanyLocalizationOverride

---

## Risk Monitoring

### Weekly Checks
- [ ] Review test results for failures
- [ ] Check application logs for errors
- [ ] Review audit log for suspicious activity
- [ ] Verify backup integrity

### Monthly Checks
- [ ] Review and update risk register
- [ ] Check for .NET security updates
- [ ] Review access control (who has Owner role?)
- [ ] Verify email delivery (if enabled)

### Quarterly Checks
- [ ] Full security audit
- [ ] Performance testing
- [ ] Disaster recovery test
- [ ] Documentation review

---

## Open Questions (Blocking Full Fluency)

### Architecture Questions
1. **Q:** How is CompanyId resolved for unauthenticated requests (e.g., signup)?
   **Verification:** Review `Pages/Auth/Signup.cshtml.cs` and `TenantResolver.cs`

2. **Q:** What happens if TenantResolver returns null/0?
   **Verification:** Check null handling in query filters and interceptor

3. **Q:** Are there any cross-tenant queries besides Director access?
   **Verification:** `grep IgnoreQueryFilters` across codebase

### Security Questions
4. **Q:** Is CSRF protection enabled on all forms?
   **Verification:** Check for `@Html.AntiForgeryToken()` or `[ValidateAntiForgeryToken]`

5. **Q:** How are API key scopes validated?
   **Verification:** Review controller scope checks

6. **Q:** What is the password policy (length, complexity)?
   **Verification:** Review `PasswordHasher.cs` and signup validation

### Operations Questions
7. **Q:** What is the database backup strategy?
   **Verification:** Review `Pages/Owner/Backup.cshtml.cs`

8. **Q:** How are application logs rotated/retained?
   **Verification:** Check logging configuration

9. **Q:** Is there a mechanism to revoke all sessions for a user?
   **Verification:** Review authentication cookie handling

### Feature Questions
10. **Q:** What notification types exist and when are they triggered?
    **Verification:** Review `NotificationType` enum and `NotificationService.cs`

11. **Q:** How does the Game (shift swap game) scoring work?
    **Verification:** Review `Pages/Game/` and game-related services

12. **Q:** What is the vacation "extension day" logic (until 1 PM)?
    **Verification:** Review `TeamCalendarEventAggregator.cs`

---

## Verification Commands

### Search for Security Patterns
```powershell
# Find all IgnoreQueryFilters usage
Select-String -Path "c:\Users\katzi\Downloads\ShiftManager\**\*.cs" -Pattern "IgnoreQueryFilters" -Recurse

# Find authorization attributes
Select-String -Path "c:\Users\katzi\Downloads\ShiftManager\**\*.cs" -Pattern "\[Authorize" -Recurse

# Find validation attributes
Select-String -Path "c:\Users\katzi\Downloads\ShiftManager\**\*.cs" -Pattern "\[Validate" -Recurse
```

### Search for Error Handling
```powershell
# Find try-catch blocks
Select-String -Path "c:\Users\katzi\Downloads\ShiftManager\**\*.cs" -Pattern "catch\s*\(" -Recurse

# Find logging statements
Select-String -Path "c:\Users\katzi\Downloads\ShiftManager\**\*.cs" -Pattern "_logger\." -Recurse
```

### Database State Verification
```powershell
# Check app.db exists and size
Get-Item "c:\Users\katzi\Downloads\ShiftManager\app.db" | Select-Object Name, Length, LastWriteTime
```
