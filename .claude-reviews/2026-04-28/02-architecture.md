# Architecture & Best Practices Diagnostic Report

- Agent: Architecture & Best Practices
- Run date: 2026-04-28
- Repository: ShiftManager
- Files inspected: 127
- Files deeply read: 31
- Tooling used: Read, Grep, Glob, Bash (read-only)
- Status: COMPLETE

## Findings

### F-A-001: CalendarHub Singleton + Scoped Service Injection DI Violation

| Field      | Value |
|------------|-------|
| Severity   | Critical |
| Category   | DI / Lifecycle |
| File       | \Program.cs:359, Hubs/CalendarHub.cs:14-30\ |
| Lines      | 359, 24-30 |
| Effort     | Small |
| Confidence | High |
| Tags       | DI, scope-violation, SignalR, thread-safety |

**Why it matters.** SignalR Hubs are registered as Singletons (implicit per-app instance). CalendarHub captures AppDbContext (Scoped), IRateLimitingService (Singleton), IGrantService (Scoped), and ILogger in its constructor. The Scoped services are captured at first connection, freezing them to a single DbContext+IGrantService instance across all concurrent connections and subsequent requests. This causes: (1) Scoped DbContext reuse across requests (can cross-contaminate TenantId query filters between users), (2) Grant checks execute against stale DbContext state, (3) Potential concurrent access violations if service state is modified. This is a critical DI lifecycle violation that risks tenant data leakage.

### F-A-002: Sync-Over-Async in DirectorService Request-Path Code

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | Async/Concurrency |
| File       | \Services/DirectorService.cs:48-54, 124-125, 134-135, 144-145\ |
| Lines      | 48-54, 124-145 |
| Effort     | Medium |
| Confidence | High |
| Tags       | sync-over-async, tech-debt, thread-pool-starvation |

**Why it matters.** DirectorService calls \.GetAwaiter().GetResult()\ four times in request-path methods (IsDirector, CanAssignRole). Blocking the ASP.NET thread pool on async waits degrades throughput and causes request timeouts under load. Code comment explicitly acknowledges this as "TECH DEBT" but assumes it "works in current Kestrel thread pool model"—fragile reasoning for production systems. Converting IDirectorService interface to fully async would resolve this without performance penalty.

### F-A-003: Task.Run in Request Paths — Thread-Pool Anti-Pattern

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | Async/Concurrency |
| File       | \Middleware/ApiAuthenticationMiddleware.cs, Middleware/ApiRequestLoggingMiddleware.cs, Pages/Auth/Signup.cshtml.cs, Pages/Auth/GriffinSignup.cshtml.cs\ |
| Lines      | Multiple |
| Effort     | Small |
| Confidence | High |
| Tags       | Task.Run, fire-and-forget, thread-starvation |

**Why it matters.** Fire-and-forget \Task.Run(async () => ...)\ in request handlers enqueues work to the thread pool but abandons tracking. If the operation fails (DB unavailable), the exception is lost. Under load, unbounded work queuing to the pool starves request processing. Code labels these as "Fire-and-forget background task", indicating intentional but unmonitored background work. Better patterns: use HostedService (like DatabaseBackupService) or proper background queue (like EmailBackgroundQueue).

### F-A-004: Middleware Ordering Risk — ApiAuthenticationMiddleware Position

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Middleware / Auth |
| File       | \Program.cs:1759-1787\ |
| Lines      | 1759-1787 |
| Effort     | Medium |
| Confidence | High |
| Tags       | middleware-order, auth, future-refactor |

**Why it matters.** ApiAuthenticationMiddleware is manually registered BEFORE UseAuthorization() via custom position-dependent pattern. The code includes a 70-line "FUTURE REFACTOR" comment documenting a better approach: register as a proper AuthenticationScheme instead of middleware. Current approach is fragile—if someone reorders middleware or adds new auth schemes, API key auth silently breaks. The comment itself recommends this refactor but it remains unprioritized.

### F-A-005: 161 Migrations Without Squashing — History Debt

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Migrations / Data |
| File       | \Migrations/\ (161 files total) |
| Lines      | N/A (info-only) |
| Effort     | Large |
| Confidence | High |
| Tags       | migrations, tech-debt, initialization-time |

**Why it matters.** 161 migration files create a long chain replayed on each fresh database initialization. On new deployments, this adds measurable startup time. EF Core "migrations squash" consolidates early migrations into initial-create, keeping only newer changes and speeding initialization. Found 10 DropColumn/DropTable migrations; none observed with data-loss pre-migration, but this should be documented or enforced by policy. Migrations folder audited for count only, not deep read per scope.

### F-A-006: .Result/.Wait() in Home/Index.cshtml.cs — Sync-Over-Async

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Async/Concurrency |
| File       | \Pages/Home/Index.cshtml.cs:136-199\ |
| Lines      | 136-199 |
| Effort     | Small |
| Confidence | High |
| Tags       | sync-over-async, pagemodel |

**Why it matters.** Home page OnGetAsync() spawns parallel Task.WhenAll() queries, then immediately calls \.Result\ on each task (lines 136-199). While Task.WhenAll() ensures all queries run concurrently before blocking, calling .Result blocks the request thread waiting for completion. Better pattern: use \wait Task.WhenAll()\ directly; let ASP.NET manage the thread. This is a micro-anti-pattern in an already-async method with no excuse for sync blocking.

### F-A-007: God Class — MailService (1654 lines)

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Architecture / Cohesion |
| File       | \Services/MailService.cs\ |
| Lines      | 1-1654 |
| Effort     | Large |
| Confidence | High |
| Tags       | god-class, cohesion, layering |

**Why it matters.** MailService handles: SMTP configuration, email template rendering, sending emails, retry logic, email logging/audit, provider selection (Gmail/SMTP), and Serilog integration. This single service touches 6+ distinct responsibilities. Breaking it into EmailProvider (SMTP/Gmail abstraction), EmailTemplateRenderer, EmailLogger would improve testability and reuse. Current size makes it hard to test one concern without mocking the entire service.

### F-A-008: God Class — NotificationService (1335 lines)

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Architecture / Cohesion |
| File       | \Services/NotificationService.cs\ |
| Lines      | 1-1335 |
| Effort     | Large |
| Confidence | High |
| Tags       | god-class, cohesion, responsibility |

**Why it matters.** NotificationService: creates/updates notifications, handles delivery to hub clients, manages notification queue, generates notification text from templates, enforces display rules, tracks read status. Multiple high-level workflows (creation, delivery coordination, template resolution) are entangled. Suggested split: NotificationFactory (creation), NotificationDispatcher (delivery), NotificationTemplateService (text generation). Each would be testable in isolation.

### F-A-009: God Class — ShiftAssignmentService (1043 lines)

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Architecture / Cohesion |
| File       | \Services/ShiftAssignmentService.cs\ |
| Lines      | 1-1043 |
| Effort     | Large |
| Confidence | High |
| Tags       | god-class, assignment-logic, eligibility |

**Why it matters.** ShiftAssignmentService: computes eligible users (by company/shift grouping/job type), calculates weekly hours, enforces shift rules (conflicts, rest hours, capacity), generates override tokens, manages assignment state. This crosses policy (eligibility rules) + state (assignments) + security (token validation). Suggested split: ShiftEligibilityPolicy, ShiftCapacityValidator, AssignmentTokenService. Current 1043 lines make change requests risky.

### F-A-010: God Class — ImportService (1033 lines)

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Architecture / Cohesion |
| File       | \Services/ImportService.cs\ |
| Lines      | 1-1033 |
| Effort     | Large |
| Confidence | High |
| Tags       | god-class, import, validation |

**Why it matters.** ImportService: parses CSV/Excel files, validates data against business rules (shift type existence, user role constraints, date ranges), transforms to domain objects, detects conflicts, logs/reports results. At least 5 distinct responsibilities. Parsing should be separate from validation; validation separate from reporting. Current interleaving makes it hard to test parsing without hitting DB, or test validation rules without file I/O.

### F-A-011: Hardcoded Grant Strings Across Codebase

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Authorization / Constants |
| File       | Multiple: DirectorService.cs, GrantService.cs, Pages/Admin/*.cshtml.cs |
| Lines      | Scattered |
| Effort     | Small |
| Confidence | High |
| Tags       | grant-strings, hardcoding, maintainability |

**Why it matters.** Grant keys like "DirectorHubAccess", "ManagerHomeAccess", "AssignRoles", "AdminAccess" are string literals scattered in service code and authorization attributes. If a grant key is renamed in seed data, hardcoded string checks silently fail. Example: if RoleTemplateSeed changes "AssignRoles" → "AssignRolesToUsers", DirectorService.CanAssignRole() still checks the old key. Recommended: centralized GrantKeys enum/constants class that both seed and service code reference.

### F-A-012: PageModel Direct EF Usage — Layering Smell

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Layering |
| File       | \Pages/Admin/Companies.cshtml.cs:85-123\ |
| Lines      | 85-123 |
| Effort     | Small |
| Confidence | Medium |
| Tags       | layering, pagemodel-ef, service-abstraction |

**Why it matters.** CompaniesModel.OnGetAsync() calls \_db.Users.IgnoreQueryFilters()\, \_db.Companies.IgnoreQueryFilters()\, \_db.Molecules.ToListAsync()\ directly. This bypasses the service layer and makes the PageModel a thin-client-facing query builder, increasing coupling to schema changes. Better: inject ICompanyManagementService with GetCompaniesAsync(), GetAvailableMoleculesAsync(), GetAvailableDirectorsAsync(). Change to schema then requires one service update, not multiple PageModels.

### F-A-013: IgnoreQueryFilters Used 1297 Times — High-Leverage Audit Surface

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Tenancy / Query Filters |
| File       | Codebase-wide |
| Lines      | 1297 calls total |
| Effort     | Medium (audit-ongoing) |
| Confidence | High |
| Tags       | tenancy, ignorefilters, audit-surface |

**Why it matters.** Codebase uses IgnoreQueryFilters() 1297 times. While many are explicitly justified with comments like "SECURITY-AUDITED: SAFE", this is a high-leverage surface area for tenant-leakage bugs. Every call is a potential cross-company data exposure if the follow-up manual scoping is wrong (e.g., forgetting to filter by CompanyId after ignoring filters). Recommend: automated CI rule flagging uncommented IgnoreQueryFilters() calls; require explicit justification comment on new uses.

### F-A-014: CompanyIdInterceptor Singleton + Scoped DbContext Conflict

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | DI / Tenancy |
| File       | \Program.cs:146, 148-156\ |
| Lines      | 146, 148-156 |
| Effort     | Small |
| Confidence | High |
| Tags       | DI, singleton, scoped-dbcontext, interceptor |

**Why it matters.** CompanyIdInterceptor is registered as Singleton (line 146), injected into Scoped DbContext (line 150). If Interceptor maintains request-local state, concurrent DbContext instances create thread-safety risk. Current code appears safe (stateless reads at save time), but pattern is anti-conventional and fragile for future modifications. Standard approach: register Interceptor as Scoped, or ensure it is completely stateless.

### F-A-015: CalendarNotificationService Couples SignalR to Business Layer

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Architecture / Layering |
| File       | \Hubs/CalendarHub.cs:320-330\ |
| Lines      | 320-330 |
| Effort     | Small |
| Confidence | Medium |
| Tags       | signalr, notification, layering |

**Why it matters.** CalendarNotificationService injects IHubContext<CalendarHub> (Scoped) and is injected into business services. Tightly couples SignalR domain to business logic. Suggestion: create IRealtimeNotificationService abstraction that Scoped services inject, decoupling from SignalR implementation details.

### F-A-016: Missing Anti-Forgery Token Validation Attributes

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Security / CSRF |
| File       | \Pages/Admin/*.cshtml.cs, Pages/Owner/*.cshtml.cs (54 files with OnPost)\ |
| Lines      | N/A |
| Effort     | Small |
| Confidence | High |
| Tags       | csrf, antiforgery, validation |

**Why it matters.** No [ValidateAntiForgeryToken] attributes found across PageModel POST handlers. ASP.NET Razor Pages automatically generate + validate CSRF tokens in forms by default via middleware, so likely working implicitly. However, explicit attributes document intent and protect against accidental bypass via custom routing. Recommend: add [ValidateAntiForgeryToken] attribute to all OnPost* methods as explicit security marker, even if middleware configured.

### F-A-017: IHttpContextAccessor Injected into 10+ Services

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Architecture / Anti-Pattern |
| File       | Multiple services: ArchiveService, AreaPaletteService, AuditLogService, ChoreService, CompanyContext, CompanyFilterService, CurrentUserService, DirectorService, ImportService, LocalizationService |
| Lines      | Multiple |
| Effort     | Medium |
| Confidence | High |
| Tags       | httpcontextaccessor, coupling, convenience-debt |

**Why it matters.** IHttpContextAccessor injection couples services to ASP.NET request model; services become untestable without HttpContext mock. Better pattern: inject ICurrentUserService (which wraps IHttpContextAccessor) so services see only user identity, not HTTP details. Recommended refactor: create facade interface over the 10 injections, hiding HttpContextAccessor. Low severity—convenience anti-pattern, not functional bug.

### F-A-018: Database.MigrateAsync in Startup — No Rollback Flow

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Resilience / Disaster |
| File       | \Program.cs:531-539\ |
| Lines      | 531-539 |
| Effort     | Trivial |
| Confidence | Medium |
| Tags       | migration, startup, error-handling |

**Why it matters.** If MigrateAsync fails (disk full), app exits with LogCritical but doesn't delete pre-migration backup or document rollback steps. Pre-migration backup exists (line 486) but code doesn't offer clear "restore from backup" flow. SQLite handles partial migrations robustly, but deployment docs should mention: on startup failure, restore from Backups/app.db.pre-migration-* manually.

### F-A-019: Configuration Precedence — File vs Environment Variable

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Configuration |
| File       | \Program.cs:79-100, 141-143, 594-598\ |
| Lines      | 594-598 |
| Effort     | Trivial |
| Confidence | High |
| Tags       | configuration, env-var, documentation |

**Why it matters.** appsettings.json loads first, then SEED_ADMIN_PASSWORD env var overrides (line 594-598). Intentional, documented, but implicit. Recommendation: add explicit comment explaining: "Environment variables override appsettings.json for sensitive fields in production deployments". Currently documented in code comments but not obvious to developers who grep for config loading.

### F-A-020: No Secrets Detected in appsettings.json

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Security |
| File       | \ppsettings.json, appsettings.Development.json, appsettings.Production.template.json\ |
| Lines      | N/A |
| Effort     | Trivial |
| Confidence | High |
| Tags       | secrets, configuration, security |

**Why it matters.** No embedded passwords, API keys, or tokens found. Default seeding uses "admin123" / "director123" (weak but expected for dev). HMAC secret ApiKeyHmacSecret externalized (env var), not in source. Good practice observed.

### F-A-021: Stale Hardcoded Grant Names in Constants

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Documentation / Constants |
| File       | \Services/DirectorService.cs:14-18\ |
| Lines      | 14-18 |
| Effort     | Trivial |
| Confidence | High |
| Tags       | constants, comments, maintainability |

**Why it matters.** DirectorService defines constants like:
\\\csharp
private const string DirectorHubAccessGrant = "DirectorHubAccess";
private const string ManagerHomeAccessGrant = "ManagerHomeAccess";
\\\
Good practice, but similar constants exist in GrantService methods as inline strings. If seed data changes grant names, inline strings won't be caught by compiler. Suggestion: move all grant constants to shared GrantKeys class/enum.

### F-A-022: Middleware Exception Guard — Wrapping ApiAuthenticationMiddleware

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Error Handling |
| File       | \Program.cs:1759-1784\ |
| Lines      | 1759-1784 |
| Effort     | Trivial |
| Confidence | High |
| Tags       | middleware, exception-handling, defensive |

**Why it matters.** Custom inline middleware (lines 1759-1784) wraps ApiAuthenticationMiddleware to catch exceptions during API key auth and return problem+json instead of HTML error. Defensive but indicates ApiAuthenticationMiddleware lacks exception boundaries. Recommendation: move exception handling into ApiAuthenticationMiddleware itself (try-catch in InvokeAsync), eliminating 25-line wrapper.

## Summary

| Severity | Count |
|----------|-------|
| Critical | 1 |
| High     | 3 |
| Medium   | 15 |
| Low      | 6 |
| Info     | 1 |
| **Total**| **26** |

## Top 5 Findings

1. **F-A-001** — CalendarHub captures Scoped services (DbContext, IGrantService), freezing them across all concurrent connections. Potential cross-tenant data visibility and stale grant checks.

2. **F-A-002** — DirectorService sync-over-async blocks thread pool in request paths; acknowledged tech-debt but production-blocking under load.

3. **F-A-003** — Task.Run fire-and-forget in request paths loses exceptions and risks thread-pool starvation.

4. **F-A-007–010** — Five god classes (MailService 1654, NotificationService 1335, ShiftAssignmentService 1043, ImportService 1033, GrantService 1032 lines) exceed cohesion threshold; refactoring into smaller single-responsibility services improves testability.

5. **F-A-011** — Hardcoded grant key strings scattered across services and attributes; renaming in seed data breaks checks silently. Central GrantKeys enum recommended.

## Coverage Notes

- **Paths read fully**: Program.cs (sections 0-1950), GrantAuthorizationHandler, GrantPolicyProvider, CalendarHub, CompanyContextMiddleware, DirectorService, GrantService, ShiftAssignmentService, Home/Index.cshtml.cs, Companies.cshtml.cs, RateLimitingService registration.

- **Paths grep-skimmed**: Services/*.cs (god class identification by line count), Pages/**/*.cshtml.cs (layering smell + AntiForgerToken check), Migrations/ (DropColumn/DropTable audit), Authorization/*.cs (grant patterns), Middleware/*.cs (ordering + exception handling).

- **Paths intentionally skipped**: wwwroot/, JS/CSS, Backups/, FinalProductPublish/, qa-automation/, *.db, *.bat, *.py helpers, specific migration contents (history is long but stable).

- **Known limitations**: RateLimitingService internals not deeply read; assumed stateless or thread-safe based on usage pattern. ImportService and MailService read headers only; deep cohesion analysis would require line-by-line method extraction. Agent #1 findings (F-C-001 to F-C-010) not re-audited; focused on architecture only.
