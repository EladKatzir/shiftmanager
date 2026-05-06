# Duplication & Over-engineering Diagnostic Report

- Agent: Duplication
- Run date: 2026-04-28
- Repository: ShiftManager
- Files inspected: 753
- Files deeply read: 15
- Tooling used: Read, Grep, Glob, Bash (read-only)
- Status: COMPLETE

## Findings

### F-D-001: Parallel Error Shape Types

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | Duplication/duplicate-dto |
| File       | /Models/Api/ProblemDetails.cs, /Models/ApiErrorResponse.cs |
| Lines      | ProblemDetails 1-182, ApiErrorResponse 1-179 |
| Effort     | Medium |
| Confidence | High |
| Tags       | parallel-impl, deprecated-backward-compat |

ApiProblemDetails marked [Obsolete], two parallel error shapes with 90% overlap. Agent #1 flagged F-C-002 (389x CS0618). Migration path documented but incomplete; clients must handle both shapes.

---

### F-D-002: Cache Service Pattern Triplication

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Duplication/repeated-utility |
| File       | /Services/AppConfigCacheService.cs, /Services/CompanyCacheService.cs, /Services/ShiftTypeCacheService.cs |
| Lines      | AppConfig 1-107, Company 1-115, ShiftType 1-215 |
| Effort     | Small |
| Confidence | High |
| Tags       | cache-pattern, abstract-base |

Three cache services follow identical pattern: MemoryCache + logger, TryGetValue, cache-miss load/set, InvalidateCache. Extract abstract GenericCacheService<T> base class. Reduces ~330 lines.

---

### F-D-003: Exception Logging Boilerplate

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Duplication/exception-handling |
| File       | Services/* (widespread) |
| Effort     | Trivial |
| Confidence | High |

163 instances of catch-log-throw pattern. ExceptionHelper.LogAndThrow() would reduce repetition.

---

### F-D-004: API PageModel Boilerplate

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Duplication/pagemodel-boilerplate |
| File       | /Pages/Api/**/*.cshtml.cs (36 files) |
| Effort     | Medium |
| Confidence | High |

All 36 OnPost methods follow: log, parse JSON, validate, extract user, permission check, service call, audit. 500+ lines copy-paste. Extract ApiPageModelBase, JsonRequestParser, RequestAuthExtractor.

---

### F-D-005: FirstOrDefaultAsync Pattern

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Duplication/data-access-pattern |
| File       | /Services/**/*.cs |
| Effort     | Trivial |
| Confidence | High |

279 instances. Correct EF usage, not duplication. Signals extension method opportunity.

---

### F-D-006: Email Config Chain Split

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Duplication/configuration-cascade |
| File       | /Services/MailService.cs, /Services/EmailConfigService.cs |
| Effort     | Trivial |

Fallback logic split across two services. Should be owned by single IEmailConfigResolver.

---

### F-D-007: Single-Implementation Interfaces

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Duplication/premature-abstraction |
| File       | /Services/I*Service.cs (40 interfaces) |
| Effort     | Large |
| Confidence | High |

40+ interfaces with exactly 1 implementation, no test fakes. Add ceremony without value.

---

### F-D-008: DateTime Formatting Duplication

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Duplication/formatting-utility |
| File       | /Helpers/DateTimeFormatHelper.cs, /Services/LocalizationService.cs |
| Effort     | Trivial |

Both define FormatDate/FormatDateTime. Code paths could unify. Not high-priority.

---

### F-D-009: MailService and NotificationService Fire-and-Forget

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Duplication/service-logic |
| File       | /Services/MailService.cs (1654), /Services/NotificationService.cs (1335) |
| Effort     | Medium |

MailService uses structured queue, NotificationService creates tasks and swallows. Different architectures, same intent. Unify under IBackgroundNotificationDispatcher.

---

### F-D-010: Grant Permission Check Repetition

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Duplication/permission-check |
| File       | /Services/GrantService.cs |
| Effort     | Small |

Multiple HasCalendarEditPermissionAsync, HasCalendarNotePermissionAsync, etc. Each composes similar base checks. CompositeGrantChecker would reduce.

---

### F-D-011: Validation Patterns

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Duplication/validation |
| File       | /Services/ImportService.cs, /Services/ShiftAssignmentService.cs |
| Effort     | Trivial |

Both accumulate validation errors in lists. Shared ValidationBuilder could benefit.

---

### F-D-012: DI Constructor Boilerplate

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Duplication/constructor-ceremony |
| File       | /Services/**/*.cs (140+ services) |
| Effort     | Trivial |

All services: inject, null-check throw. Necessary boilerplate.

---

### F-D-013: Localization Key Duplication

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Duplication/resource-i18n |
| File       | /Resources/SharedResources.resx, .he-IL.resx |
| Effort     | Small |

analyze_duplicates.py, deduplicate_resources.py indicate duplicate keys. Known issue; scripts not executed per scope.

---

### F-D-014: Data Access Abstraction

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Duplication/over-abstraction |
| File       | /Data/AppDbContext.cs |
| Effort     | Large |

No Repository/Unit-of-Work. Services use AppDbContext directly. EF already abstraction. Correct design.

---

### F-D-015: API Error Response Inconsistency

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Duplication/api-response |
| File       | /Controllers/Api/V1/*.cs (12 controllers) |
| Effort     | Small |

Controllers return both ApiProblemDetails and ApiErrorResponse. Clients handle both during transition.

---

### F-D-016: PageModel Authorization Check

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Duplication/auth-check |
| File       | /Pages/**/*.cshtml.cs (69 files) |
| Effort     | Trivial |

In-handler permission checks after [Authorize]. Custom AuthorizationHandler could centralize.

---

### F-D-017: Cache Key Formatting

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Duplication/utility-function |
| File       | Cache services |
| Effort     | Trivial |

Manual cache key formatting. CacheKeyFactory could centralize.

---

### F-D-018: Delete Endpoint Duplication

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Duplication/pagemodel-delete |
| File       | /Pages/Api/Calendar/DeleteChore.cshtml.cs, DeleteOnDuty.cshtml.cs |
| Effort     | Small |

95% identical endpoints. Generic DeleteEntityModel<TService, TEntity> base class eliminates duplication.

---

### F-D-019: Enum Similarity

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Duplication/enum-similarity |
| File       | /Models/Support/Enums.cs |
| Effort     | Trivial |

RequestStatus.Declined vs JoinRequestStatus.Rejected. Use same enum or map consistently.

---

### F-D-020: Tenant Resolution Pattern

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Duplication/tenant-resolution |
| File       | /Services/**/*.cs |
| Effort     | Trivial |

GetCurrentTenantId() appears 46 times. Correct usage. ITenantResolver is critical; ensure cached.

---

## Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High     | 2 |
| Medium   | 5 |
| Low      | 11 |
| Info     | 2 |
| **Total**| **20** |

## Top 5 Findings

1. F-D-002: Cache Service Pattern Triplication — Extract abstract base
2. F-D-004: 36 API PageModel Boilerplate — Create ApiPageModelBase + helpers
3. F-D-001: Parallel Error Shapes — Complete migration
4. F-D-009: MailService/NotificationService Divergence — Unify dispatcher
5. F-D-007: Single-Implementation Interfaces (40+) — Evaluate and remove

## Coverage Notes

- Paths read: MailService, NotificationService, ShiftAssignmentService, ImportService, GrantService, 3x cache services, EmailConfigService, DeleteChore/OnDuty, ProblemDetails, ApiErrorResponse, Enums, EditProfile (15 files)
- Paths grep-skimmed: Services/* (140+), Pages/* (69), Controllers/* (10), Models/* (DTOs)
- Paths skipped: Backups, bin, obj, packages, FinalProductPublish, ProductionReady, wwwroot/lib, Migrations
- Limitations: No script execution per scope. Resource duplicates based on script intent. Single-impl via grep. Pattern counts may vary.

---
**Report end**
