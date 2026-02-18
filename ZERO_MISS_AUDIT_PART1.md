# ShiftManager Zero-Miss Production Inspection — PART 1

**Auditor:** Claude Opus 4.6 Review Agent
**Date:** 2026-02-18
**Branch:** merged-canonical
**Build:** 0 warnings, 0 errors | Tests: 236/236 passing

---

## 1. SYSTEM MAP

### 1.1 Architecture Overview
- **Framework:** ASP.NET Core 8.0, Razor Pages + API Controllers
- **Database:** SQLite (WAL mode, busy_timeout=5000ms)
- **Real-time:** SignalR (CalendarHub)
- **Auth:** Cookie-based (shiftmgr.auth) + API Key (X-API-Key header) + Griffin ADFS SSO
- **Deployment:** Air-gapped Windows/IIS
- **Localization:** EN-US, HE-IL (bidirectional)

### 1.2 Service Inventory (66 registered services)

| Category | Count | Lifetime |
|----------|-------|----------|
| Interface-based (Scoped) | 49 | Scoped |
| Interface-based (Singleton) | 2 | Singleton |
| API Layer Services | 8 | Scoped |
| Calendar Services (no interface) | 3 | Scoped |
| Infrastructure Singletons | 3 | Singleton |
| Hosted Services | 4 | Hosted |
| Framework Integration | 3 | Mixed |

**Verification:** All 66 services registered in Program.cs have matching implementation files. Zero orphaned interfaces. Zero missing implementations.

### 1.3 Middleware Pipeline (ordered)

1. CorrelationIdMiddleware — request tracing
2. HTTPS Redirect (conditional)
3. Response Compression
4. WebOptimizer (JS/CSS minification)
5. Security Headers (CSP, X-Frame-Options, etc.)
6. Static Files
7. Routing
8. RequestLoggingMiddleware
9. Request Localization
10. GriffinAuthenticationMiddleware (ADFS SSO)
11. CompanyContextMiddleware (tenant resolution)
12. Authentication
13. Authorization
14. API Cache-Control headers
15. RateLimitingMiddleware (UI endpoints)
16. ApiExceptionMiddleware
17. ApiRequestLoggingMiddleware
18. ApiAuthenticationMiddleware
19. ApiRateLimitingMiddleware
20. Excel Calendar Redirects (feature-flag gated)

### 1.4 Background Services

| Service | Trigger | Error Handling |
|---------|---------|----------------|
| EmailBackgroundProcessor | Queue (Channel<T>, 500 cap) | Per-email try-catch, no retry |
| DailyNotificationJob | Timer (15-min interval) | Concurrency guard, multi-level catch |
| DatabaseBackupService | Timer (daily + startup) | Fallbacks, integrity check |
| GracefulShutdownService | Lifecycle events | WAL checkpoint on shutdown |

### 1.5 Data Stores
- **Primary:** SQLite (app.db) with 120 migrations
- **Entities:** 133 DbSet properties in AppDbContext
- **Tenant-scoped:** 28 entities implement IBelongsToCompany
- **Query filters:** 31 entities have global tenant filters

### 1.6 External Integrations
- **Email API:** HTTP POST to configurable SMTP-compatible endpoint (disabled by default)
- **Griffin ADFS:** Military SSO integration (disabled by default)
- **No other external dependencies** (air-gapped design)

### 1.7 API Surface
- **SignalR Hub:** 1 (CalendarHub — 5 broadcast event types, 4 group patterns)
- **API Controllers:** 13 (54+ REST endpoints)
- **Razor Pages:** Full application (auth-gated folder convention)

---

## 2. DEPLOYMENT + LIVENESS

### 2.1 Build Verification
- **Method:** `dotnet build --no-restore`
- **Result:** SUCCESS — 0 warnings, 0 errors
- **Evidence:** Build output captured

### 2.2 Test Suite
- **Method:** `dotnet test`
- **Result:** 236/236 PASS, 0 failures, 0 skipped
- **Evidence:** Test output captured

### 2.3 Health Checks

| Endpoint | Tags | Checks | Auth |
|----------|------|--------|------|
| /health | live | MemoryHealthCheck (800MB) | Anonymous (intentional) |
| /ready | ready | DbContext + DiskSpace + Memory | Anonymous (intentional) |

**Thresholds:**
- Disk: Degraded <20%, Unhealthy <10% OR <100MB, WAL >100MB warning
- Memory: Unhealthy >800MB (missing Degraded threshold — SEV-4)

### 2.4 Startup Safety Checks (Program.cs:1323-1406)
- ✅ Data Protection key verification
- ✅ SQLite network share detection
- ✅ Default credentials warning
- ✅ Public signup warning
- ✅ Timezone assertion (Israel Standard Time)
- ✅ HMAC secret enforcement (production refuses to start without it)
- ✅ Connection string validation (fail-fast)
- ✅ Owner email validation (fail-fast)

---

## 3. CONNECTIVITY + CONFIG INTEGRITY

### 3.1 Authentication Flows
- **Cookie Auth:** HttpOnly, SameSite=Lax, 7-day sliding expiry — ✅ SECURE
- **API Key Auth:** X-API-Key header → HMAC-SHA256 hash → DB lookup — ✅ SECURE
- **Griffin SSO:** Token cookie → middleware validation → auto-provision — ✅ SECURE (disabled by default)

### 3.2 Authorization
- **Razor Pages:** Folder-level `AuthorizeFolder("/")` with explicit anonymous exceptions
- **API Controllers:** `[Authorize]` + feature flag gates + scope validation
- **Grant System:** 107 grant types, hierarchical scope matching, dynamic policy provider

### 3.3 Anonymous Endpoints (verified cross-referenced)
| Endpoint | Program.cs | ApiAuthMiddleware |
|----------|------------|-------------------|
| /Auth/Login | ✅ | N/A (Razor) |
| /Auth/Signup | ✅ | N/A (Razor) |
| /Api/Signup/GetSignupOptions | ✅ | ✅ |
| /Public/Chores | ✅ | N/A (Razor) |
| /Public/OnDuty | ✅ | N/A (Razor) |
| /Api/Telemetry | ✅ | ✅ |
| /api/v1/version | ✅ | ✅ |
| /health, /ready | ✅ (MapHealthChecks) | ✅ (whitelisted) |

### 3.4 Security Headers
- X-Frame-Options: DENY ✅
- X-Content-Type-Options: nosniff ✅
- Referrer-Policy: strict-origin-when-cross-origin ✅
- CSP: self + unsafe-inline (required for 80+ inline handlers) ✅
- Server/X-Powered-By/X-AspNet-Version: removed ✅

### 3.5 Rate Limiting
- **Login:** 10 attempts/15min per IP, 15/15min per account, lockout after 10 failures
- **UI API:** Sliding window per endpoint+user
- **REST API:** Token bucket per API key

### 3.6 CSRF Protection
- Razor Pages: built-in AntiForgery tokens ✅
- API via cookie auth: X-Requested-With header validation ✅

---

## 4. CONTRACTS

### 4.1 API Controllers (13 total, 54+ endpoints)
All controllers verified with:
- Route attributes ✅
- Authorization (API key or claims-based) ✅
- Feature flag gates ✅
- Scope validation (e.g., shift:read, user:write) ✅
- Pagination (clamped to max 100) ✅
- Input validation ✅

### 4.2 SignalR Hub Contract
- 1 Hub (CalendarHub) with 5 broadcast events
- Group validation: CompanyId claim → molecule/area ownership verification ✅
- Events: AssignmentChanged, CapacityChanged, NoteChanged, ChoreChanged, OnCallChanged

---

## 5. ISSUES FOUND (PART 1)

### SEV-1: Cross-Tenant API Key Data Leak

**Affected:** ApiKey, ApiKeyRequest entities
**Root Cause:** No query filter + no CompanyId validation in 4 service methods
**Attack Vectors:**
1. User from Company A can revoke Company B's API keys
2. User from Company A can regenerate/steal Company B's API keys
3. User from Company A can read Company B's API key metadata

**Specific Methods:**
- `ApiKeyService.RevokeApiKeyAsync()` — queries by keyId only
- `ApiKeyService.RegenerateApiKeyAsync()` — queries by keyId only
- `ApiKeyService.GetKeyByIdAsync()` — queries by keyId only
- `ApiKeyService.GetRequestByIdAsync()` — queries by requestId only

**Fix:** Add query filters to AppDbContext + explicit CompanyId validation in service methods

### SEV-2: Email Delivery — No Retry, No Dead Letter

**Affected:** EmailBackgroundProcessor
**Root Cause:** Fire-and-forget design with no retry mechanism
**Impact:**
- Failed emails are permanently lost
- Queue full (500) → emails silently dropped
- No alerting when emails fail
- No dead letter queue for investigation

**Fix:** Add retry with exponential backoff, dead letter table, admin notification

### SEV-3: Missing Audit Trail for Shift Assignments

**Affected:** Calendar/Table.cshtml.cs → ShiftAssignmentService
**Root Cause:** Assignment flow logs to ILogger but NOT to AuditLog table
**Impact:** No persistent, queryable audit trail for who assigned/unassigned shifts

### SEV-4: MemoryHealthCheck Missing Degraded Threshold

**Affected:** MemoryHealthCheck
**Impact:** No early warning between healthy and 800MB unhealthy
**Fix:** Add 600MB Degraded threshold

### SEV-4: Silent Exception in DatabaseBackupService

**Affected:** DatabaseBackupService.cs:257-258
**Root Cause:** Empty catch block swallows file deletion errors
**Fix:** Add logging to catch block

### SEV-4: CompanySettings Missing Query Filter

**Affected:** CompanySettings entity
**Root Cause:** Has CompanyId but no query filter or IBelongsToCompany
**Mitigated:** Service layer (HierarchySettingsService) always filters by CompanyId explicitly
**Fix:** Add query filter for defense-in-depth

---

## 6. COVERAGE GAPS (PART 1)

| Gap | Why | Input Needed |
|-----|-----|-------------|
| Runtime traffic verification | No running instance to test | Deploy to staging + send test requests |
| SignalR real-time test | Cannot test WebSocket without browser client | Browser-based E2E test |
| Email delivery test | Email disabled by default, no SMTP configured | Configure test SMTP endpoint |
| Griffin SSO test | Griffin disabled by default, no ADFS server | Configure test ADFS server |
| Production config verification | Only dev appsettings analyzed | Access to production appsettings.Production.json |
| Load testing | No load test infrastructure | JMeter/k6 test plan + execution |

---

*PART 2 continues with: Remaining workflow traces, data layer deep-dive, self-check, and final Go/No-Go.*
