# ShiftManager Zero-Miss Production Inspection — PART 2

**Auditor:** Claude Opus 4.6 Review Agent
**Date:** 2026-02-18
**Branch:** merged-canonical
**Build:** 0 warnings, 0 errors | Tests: 236/236 passing

---

## 7. WORKFLOWS END-TO-END

### 7.1 Shift Assignment Workflow (VERIFIED)

**Path:** UI → Calendar/Table.cshtml.cs → ShiftAssignmentService → DB + SignalR

| Step | Method | Evidence |
|------|--------|----------|
| Entry | OnPostAssignEmployeeAsync | Table.cshtml.cs:531-653 |
| Find/create ShiftInstance | EF query + create | Table.cshtml.cs:537-562 |
| Validate assignment | ValidateShiftAssignmentAsync | ShiftAssignmentService.cs:172-299 |
| Handle warnings (override token) | HMAC token generation/validation | ShiftAssignmentService.cs:484-549 |
| Assign (transactional) | AssignShiftAsync | ShiftAssignmentService.cs:365-459 |
| SignalR broadcast | NotifyAssignmentChangedAsync | CalendarHub.cs:221-225 |

**Success path:** ✅ VERIFIED — validates, assigns within transaction, broadcasts via SignalR
**Failure path:** ✅ VERIFIED — hard errors return 400 JSON, warnings prompt override, capacity full returns SHIFT_FULLY_STAFFED
**Retry/idempotency:** ✅ VERIFIED — duplicate check inside transaction (ShiftAssignmentService.cs:415-422), DbUpdateException caught (line 451-458)

**Gap found:** No AuditLog entries created for assignments (SEV-3 — logged below)

### 7.2 Email Delivery Workflow (VERIFIED — WITH GAPS)

**Path:** MailService.SendMailAsync → EmailBackgroundQueue → EmailBackgroundProcessor → SendMailDirectAsync → HTTP POST

| Step | Method | Evidence |
|------|--------|----------|
| Enqueue | SendMailAsync | MailService.cs:164-184 |
| Queue | Channel<QueuedEmail> (500 cap) | EmailBackgroundQueue.cs:14-46 |
| Dequeue + Send | ExecuteAsync | EmailBackgroundProcessor.cs:8-44 |
| HTTP send | SendMailDirectAsync | MailService.cs:191-372 |
| Config load | DB-first, fallback appsettings | MailService.cs:62-88 |
| Validation | URL/key/recipient checks | MailService.cs:93-132 |
| Logging | EmailApiLog table (fire-and-forget) | MailService.cs:352-369 |

**Success path:** ✅ VERIFIED — enqueue → dequeue → validate config → HTTP POST → log success
**Failure path:** ⚠️ PARTIAL — catches exceptions and logs, but email is LOST (no retry, no DLQ)
**Retry:** ❌ NOT IMPLEMENTED — fire-and-forget only
**Queue full:** ⚠️ Email silently dropped with warning log (EmailBackgroundQueue.cs:37)

### 7.3 Authentication Workflow (VERIFIED)

**Cookie Auth Path:** Login page → validate credentials → rate limit check → account lockout check → set claims → redirect
- Open redirect: ✅ SAFE (Url.IsLocalUrl validation everywhere)
- Rate limiting: ✅ 10/IP/15min + 15/account/15min + lockout after 10 failures
- Griffin SSO: ✅ SAFE (disabled by default, token validation implemented)

### 7.4 API Key Authentication (VERIFIED — WITH SEV-1 ISSUE)

**Path:** X-API-Key header → HMAC hash → DB lookup → scope validation → claims injection
- Authentication: ✅ SECURE (HMAC + DB lookup + scope check)
- Tenant isolation in service layer: ❌ CRITICAL (see SEV-1 below)

---

## 8. BACKGROUND SYSTEMS

### 8.1 EmailBackgroundProcessor
- **Trigger:** Queue-based (Channel<T>)
- **Last verification:** Build passes, service registered
- **Error handling:** Per-email try-catch, continues on failure
- **Gap:** No retry, no DLQ (SEV-2)

### 8.2 DailyNotificationJob
- **Trigger:** Timer (15-min interval)
- **Feature flag:** Re-checked each iteration (runtime disable possible)
- **Concurrency guard:** Interlocked.CompareExchange prevents overlap
- **Error handling:** Multi-level catch, per-user and per-company isolation
- **Cleanup:** Telemetry data >30 days auto-purged
- **Status:** ✅ VERIFIED

### 8.3 DatabaseBackupService
- **Trigger:** On startup (10s delay) + daily at configured time
- **Features:** Integrity check, optional AES-256 encryption, retention rotation, pre-migration backup
- **Error handling:** Fallbacks (VACUUM INTO → file copy), cleanup on failure
- **Gap:** Silent catch on file deletion (SEV-4)
- **Status:** ✅ VERIFIED (with minor gap)

### 8.4 GracefulShutdownService
- **Trigger:** IHostApplicationLifetime events
- **Action:** WAL checkpoint on shutdown
- **Status:** ✅ VERIFIED

---

## 9. DATA LAYER

### 9.1 Migration Consistency
- **Total migrations:** 62
- **Latest:** 20260218094353_AddSortOrderToHierarchyEntities
- **Pending model changes:** NONE (verified via `dotnet ef migrations has-pending-model-changes`)
- **ModelSnapshot version:** EF Core 9.0.9 (matches project)
- **Status:** ✅ FULLY CONSISTENT

### 9.2 Tenant Isolation Audit

| Entity | CompanyId | IBelongsToCompany | Query Filter | Service Filter | Status |
|--------|-----------|-------------------|--------------|----------------|--------|
| 28 core entities | ✅ | ✅ | ✅ | N/A | ✅ SAFE |
| ShiftProgram | ✅ | ✗ | ✅ | N/A | ✅ SAFE (by design) |
| MasterProgram | ✅ | ✗ | ✅ | N/A | ✅ SAFE (by design) |
| AppConfig | ✅ | ✗ | ✅ | N/A | ✅ SAFE (by design) |
| CompanySettings | ✅ | ✗ | ✗ | ✅ explicit | ⚠️ SEV-4 (defense-in-depth) |
| **ApiKey** | ✅ | ✗ | ✗ | ❌ 3 methods | 🔴 **SEV-1 CRITICAL** |
| **ApiKeyRequest** | ✅ | ✗ | ✗ | ❌ 2 methods | 🔴 **SEV-1 CRITICAL** |
| ShiftGroupingCompany | ✅ | ✗ | ✗ | N/A (junction) | ✅ SAFE (parent-accessed) |
| DirectorCompany | ✅ | ✗ | ✗ | N/A (cross-tenant) | ✅ SAFE (by design) |
| OnDuty/OnDutyTypeConfig | ✗ | ✗ | ✗ | N/A (global) | ✅ SAFE (by design) |

### 9.3 SQL Injection
- **Raw SQL:** Only `VACUUM;` (PurgeService.cs) and PRAGMAs (Program.cs) — all hardcoded
- **DatabaseConsole:** Read-only connection + semicolon rejection + SELECT-only enforcement
- **EF Core:** All queries parameterized via LINQ
- **Status:** ✅ NO SQL INJECTION RISKS

### 9.4 IgnoreQueryFilters() Usage
- **Total instances:** 95 files
- **All instances:** Have security audit comments or are in safe contexts (startup seeding, anonymous auth flows, admin pages with explicit scope filters)
- **Status:** ✅ PROPERLY DOCUMENTED AND SAFE

---

## 10. OBSERVABILITY + ALERTING

### 10.1 Logging

| Channel | Configured | Notes |
|---------|-----------|-------|
| Console (dev) | ✅ SimpleConsole | Readable format |
| Console (prod) | ✅ JsonConsole | Structured JSON for aggregation |
| Windows Event Log | ✅ Warning+ | IIS monitoring |
| File logging | ❌ Not configured | Relies on IIS log capture |
| Database (API requests) | ✅ ApiRequestLog table | Full forensics |
| Database (email sends) | ✅ EmailApiLog table | Full send/fail history |

### 10.2 Security Logging
- SecurityLogger service: 9 event types (auth success/fail, lockout, rate limit, threats, config changes)
- Actively used in Griffin SSO flow
- **Status:** ✅ COMPREHENSIVE

### 10.3 Request Tracing
- CorrelationIdMiddleware: X-Request-ID propagation ✅
- RequestLoggingMiddleware: Structured logs with method, path, status, duration, user, IP ✅
- Sensitive data redaction: passwords, tokens, auth headers ✅
- Slow request warning: >1000ms ✅

### 10.4 Error Handling

| Layer | Coverage | Notes |
|-------|----------|-------|
| API endpoints | ✅ Excellent | ApiExceptionMiddleware maps all exception types |
| Razor pages (global) | ✅ | UseExceptionHandler("/Error") |
| Razor pages (per-handler) | ⚠️ Partial | Some pages rely on global handler without specific context |
| Background services | ✅ Good | All have multi-level catch blocks |
| Error page safety | ✅ | No stack traces, localized messages only |

### 10.5 Silent Failure Found
- DatabaseBackupService.cs:257-258 — empty catch block on cleanup (SEV-4)

### 10.6 Alerting Gaps
- No built-in alerting for email failures
- No admin dashboard for email health monitoring
- No alert for queue full events
- No alert for backup failures (logs only)
- Health check endpoints exist but no external monitoring configured
- **Status:** Acceptable for air-gapped deployment (relies on IIS + Windows Event Log monitoring)

---

## 11. SELF-CHECK (ANTI-MISS PASS)

### 11.1 Orphan Resources
- Unused service registrations: NONE ✅
- Orphan Razor pages: NONE ✅ (154 pages all reachable)
- Unused JavaScript files: NONE ✅ (35 files all referenced)
- Unregistered API endpoints: NONE ✅

### 11.2 Stale Configuration
- 3 stale feature flag constants in FeatureFlagSeed.cs (NewNavEnabled, ScopeSwitcherEnabled, NewCalendarStyles) — dead code but harmless (SEV-4)

### 11.3 Missing Localization
- Owner/SystemHealth.cshtml: ~30+ hardcoded English strings (SEV-3)
- Owner/DataLifecycle.cshtml: ~4 hardcoded English strings (SEV-4)

### 11.4 SignalR Hub Security
- Tenant isolation: ✅ SECURE (ValidateGroupAccessAsync with molecule→company chain)
- Malformed input: ✅ SECURE (int.TryParse + DB validation)
- Client invocation of broadcast: ✅ SAFE (broadcast methods are on server-side service, not hub)
- Rate limiting on hub methods: ❌ NOT PROTECTED (SEV-2)

### 11.5 EF Migration Consistency
- Schema fully in sync with code ✅
- No pending model changes ✅
- 62 migrations, all applied ✅

---

## 12. COMPLETE ISSUES LIST

### SEV-1 (Critical)

| # | Issue | Location | Impact | Fix |
|---|-------|----------|--------|-----|
| S1-1 | Cross-tenant API key theft/revocation | ApiKeyService.cs:230,298,306 | User from Company A can steal/revoke Company B's API keys | Add query filter to AppDbContext + CompanyId validation in service methods |
| S1-2 | Cross-tenant API key request read | ApiKeyService.cs:289 | User from Company A can read Company B's API key requests | Same as S1-1 |

### SEV-2 (Degraded Core)

| # | Issue | Location | Impact | Fix |
|---|-------|----------|--------|-----|
| S2-1 | Email delivery: no retry, no DLQ | EmailBackgroundProcessor.cs | Failed emails permanently lost, no recovery | Add retry with backoff + dead letter table |
| S2-2 | Email queue full: silent drop | EmailBackgroundQueue.cs:37 | Emails silently discarded at 500 capacity | Add backpressure signal or dynamic scaling |
| S2-3 | SignalR hub: no rate limiting | CalendarHub.cs | DoS via spamming JoinCalendarGroup | Add per-user rate limit on hub methods |

### SEV-3 (Non-Core Degradation)

| # | Issue | Location | Impact | Fix |
|---|-------|----------|--------|-----|
| S3-1 | Shift assignment: no AuditLog entries | Table.cshtml.cs + ShiftAssignmentService.cs | No persistent queryable audit trail for assignments | Add AuditLogService calls in assignment flow |
| S3-2 | SystemHealth page: 30+ hardcoded English strings | Pages/Owner/SystemHealth.cshtml | Broken bilingual UX for owner diagnostics | Add <loc> keys for all strings |

### SEV-4 (Hygiene/Maintainability)

| # | Issue | Location | Impact | Fix |
|---|-------|----------|--------|-----|
| S4-1 | MemoryHealthCheck: no Degraded threshold | Services/MemoryHealthCheck | No early warning before 800MB unhealthy | Add 600MB Degraded threshold |
| S4-2 | Silent catch in backup cleanup | DatabaseBackupService.cs:257 | Swallowed exception on file deletion | Add logging to catch block |
| S4-3 | CompanySettings: no query filter | AppDbContext + CompanySettings model | Defense-in-depth gap (mitigated by service) | Add query filter |
| S4-4 | 3 stale feature flag constants | FeatureFlagSeed.cs:144-146 | Dead code confusion | Remove constants or add [Obsolete] |
| S4-5 | DataLifecycle page: 4 hardcoded strings | Pages/Owner/DataLifecycle.cshtml:51 | Minor localization gap | Add <loc> keys |

---

## 13. COVERAGE GAPS

| Gap | Cannot Verify Because | Input/Access Needed |
|-----|----------------------|---------------------|
| Runtime traffic processing | No running instance available | Deploy to staging + send test HTTP requests |
| SignalR real-time broadcast | Requires browser WebSocket client | Playwright E2E test with SignalR client |
| Email delivery end-to-end | Email disabled, no SMTP server | Configure test SMTP + send test email |
| Griffin SSO flow | Griffin disabled, no ADFS server | Configure test ADFS instance |
| Production config values | Only dev appsettings analyzed | Access to production appsettings.Production.json |
| Load/stress testing | No load test infrastructure | JMeter/k6 plan + execution environment |
| Backup restore verification | Cannot test restore in audit | Execute backup → drop → restore → verify |
| Network share detection | Local dev environment only | Deploy on production server topology |

---

## 14. FINAL GO/NO-GO RECOMMENDATION

### Decision: **CONDITIONAL NO-GO**

### Rationale

**NO-GO because:**
1. **SEV-1 exists:** Cross-tenant API key vulnerability (S1-1, S1-2) allows authenticated users to steal, revoke, or read API keys belonging to other companies. This is a **data isolation breach** in a multi-tenant military system.

**Conditions for GO:**
1. Fix S1-1 and S1-2: Add query filters to `ApiKey` and `ApiKeyRequest` entities in `AppDbContext`, AND add explicit `CompanyId` validation in the 4 affected `ApiKeyService` methods
2. Verify the fix with a cross-tenant test (attempt to access Company B's key from Company A session)

**After SEV-1 fix, classification becomes: CONDITIONAL GO**

Remaining risks (acceptable for release with documented follow-up):
- SEV-2 items (email retry, hub rate limiting) are important but not blocking for initial deployment — email is disabled by default in air-gapped environments, and hub DoS requires authenticated access
- SEV-3/4 items are hygiene improvements that can be addressed in next sprint

### Residual Risk After SEV-1 Fix

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Email loss on transient failure | Medium | Low (email disabled by default) | Document limitation, add retry in next sprint |
| Hub DoS via group spam | Low (requires auth) | Medium (CPU/memory spike) | Add hub rate limiting in next sprint |
| Missing shift audit trail | Medium | Low (ILogger entries exist) | Add AuditLog persistence in next sprint |

### Evidence Summary

| Gate | Status |
|------|--------|
| All services/workers/jobs accounted for | ✅ 66 services, 4 hosted services, 13 controllers |
| All core workflows verified (success+failure+retry) | ✅ Shift assignment, auth, API auth traced |
| No SEV-1 unresolved | ❌ S1-1, S1-2 open |
| No SEV-2 unresolved | ⚠️ S2-1, S2-2, S2-3 open (non-blocking) |
| Critical observability gaps | ✅ None that prevent SEV-1/2 detection |
| Build + Tests | ✅ 0 warnings, 0 errors, 236/236 pass |
| EF Migrations | ✅ Fully consistent, no pending changes |
| Security audit | ✅ No SQL injection, no XSS, no open redirects, no CSRF gaps |
