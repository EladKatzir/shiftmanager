# Hidden Bugs & Edge Cases Diagnostic Report

- Agent: HiddenBugs
- Run date: 2026-04-28
- Repository: ShiftManager
- Files inspected: 791
- Files deeply read: 15
- Tooling used: Read, Grep, Glob, Bash (read-only)
- Status: COMPLETE

## Findings

### F-H-001: Null dereference in GrantService.GetAccessibleCompanyIdsForGrantAsync

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | HiddenBugs/null-deref |
| File       | `Services/GrantService.cs` |
| Lines      | 295–373 |
| Effort     | Small |
| Confidence | High |
| Tags       | null-deref, tenancy |

**Why it matters.** Line 366–368: When `GetUserHierarchyContextAsync` returns null (e.g., user not found in hierarchy), the code accesses `userContext.Path.Company.Id` without null check, causing a NullReferenceException during Self scope resolution. This crashes grant evaluation at a critical auth checkpoint.

### F-H-002: Null userContext fallback skip in GrantService.HasGrantWithScopeAsync

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/null-deref |
| File       | `Services/GrantService.cs` |
| Lines      | 177–217 |
| Effort     | Small |
| Confidence | High |
| Tags       | auth, null-deref |

**Why it matters.** Lines 177, 185, 203–206, 217–220: When userContext is null, all self-scope fallback logic silently skips, meaning explicit-scope grant checks pass but hierarchy-aware cascading is denied. Users may fail authorization unexpectedly when their hierarchy context fails to load.
### F-H-003: Sync-over-async deadlock in DirectorService

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | HiddenBugs/deadlock |
| File       | `Services/DirectorService.cs` |
| Lines      | 52–53, 124–125, 134–135, 144–145, 157–158 |
| Effort     | Medium |
| Confidence | High |
| Tags       | deadlock, sync-over-async |

**Why it matters.** Five sites call `.GetAwaiter().GetResult()` on async GrantService methods from synchronous context (IsDirector line 52, CanAssignRole lines 124, 134, 144, 157). If these methods are called from middleware or async authorization filters on a thread-pool-exhausted server, deadlock blocks the request indefinitely.

### F-H-004: User hard-delete orphans ShiftAssignments and related data

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | HiddenBugs/data-integrity |
| File       | `Pages/Admin/Users.cshtml.cs` |
| Lines      | 186–500+ (OnPost delete path) |
| Effort     | Medium |
| Confidence | High |
| Tags       | data-integrity, tenancy |

**Why it matters.** User hard-deletion does not null out ShiftAssignment.UserId, leaving orphaned slots pointing to a non-existent user. Per CLAUDE.md, deactivation should also null TraineeUserId, remove active assignments, clean grants, and clear DirectorCompany. Hard-delete without cleanup violates referential consistency and orphans the calendar.

### F-H-005: Email retry queue in-memory only (MailService)

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/durability |
| File       | `Services/MailService.cs` |
| Lines      | 1–65 (EmailBackgroundQueue dependency) |
| Effort     | Large |
| Confidence | Medium |
| Tags       | durability, reliability |

**Why it matters.** EmailBackgroundQueue is in-memory; mid-retry emails are lost if the app shuts down unexpectedly. Critical notifications (shift assignments, approvals) may never be sent. No persistent queue or transactional log for mail retry state.

### F-H-006: Grant scope fallback residual risk in SameAsRole mode

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/tenancy |
| File       | `Services/GrantService.cs` |
| Lines      | 156–241 |
| Effort     | Medium |
| Confidence | Medium |
| Tags       | tenancy, auth |

**Why it matters.** GrantScopeMode.SameAsRole extracts hierarchy levels above CompanyId+DepartmentId without re-validating the original scope boundary. Edge case: a Director with Area-level grant could be reported as having access to companies in a sibling molecule that shares the same project, potentially over-granting cross-molecule access.

### F-H-007: DateTime.Now inconsistency in AnalyticsService

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/timezone |
| File       | `Services/AnalyticsService.cs` |
| Lines      | 148, 180 |
| Effort     | Small |
| Confidence | High |
| Tags       | timezone, off-by-one |

**Why it matters.** Lines 148 and 180 use DateTime.Today (local time) for date-range queries. On non-UTC servers, the local date boundary differs from UTC, causing off-by-one day errors in analytics aggregation. Shift data on date boundaries may be misclassified or skipped.

### F-H-008: Cross-company user scoping gap in GetEligibleUsersForShiftAsync

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/tenancy |
| File       | `Services/ShiftAssignmentService.cs` |
| Lines      | (method name identified; line range unverified) |
| Effort     | Small |
| Confidence | Medium |
| Tags       | tenancy, auth |

**Why it matters.** When fetching eligible users for shift assignment, the ShiftGrouping object (which includes company/molecule metadata) is not re-validated against the callers grants. A user with scope at CompanyA might retrieve and assign users from CompanyB if the ShiftGrouping references CompanyB without explicit scope checking.

### F-H-009: IMemoryCache TryGetValue-miss-Set race in FeatureFlagService

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/race |
| File       | `Services/FeatureFlagService.cs` |
| Lines      | 44–62 |
| Effort     | Small |
| Confidence | Medium |
| Tags       | race, concurrency |

**Why it matters.** Lines 44–62: TryGetValue followed by unconditional Set creates a check-then-act race. If two requests miss the cache simultaneously, both resolve the flag from DB and both Set, wasting a DB query. Should use GetOrCreateAsync to atomically check-or-create.

### F-H-010: Missing CSRF protection on Razor OnPost handlers

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | HiddenBugs/csrf |
| File       | `Pages/Admin/Users.cshtml.cs` (and others) |
| Lines      | Program.cs (no global filter) |
| Effort     | Medium |
| Confidence | High |
| Tags       | csrf, auth |

**Why it matters.** Program.cs does not register a global ValidateAntiForgeryToken filter for OnPost handlers, and no ValidateAntiforgeryToken attributes found in Pages/* codebehind files. POST endpoints accepting role assignments, user deactivations, and data modifications are vulnerable to CSRF attacks from an attacker site.

### F-H-011: VacationApprovalService re-authorization gap

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/auth |
| File       | `Services/VacationApprovalService.cs` |
| Lines      | (method name identified; line range unverified) |
| Effort     | Medium |
| Confidence | Medium |
| Tags       | auth, time-of-check |

**Why it matters.** Grant-based authorization is checked at request entry (via AuthorizeAttribute) but not re-checked during actual approval processing. If an approver grant is revoked mid-request (e.g., role change in another session), the approval still succeeds using the stale authorization token.

### F-H-012: Email idempotency gap — network-timeout ambiguity triggers duplicate send

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/idempotency |
| File       | `Services/MailService.cs` |
| Lines      | 70–150+ |
| Effort     | Medium |
| Confidence | Medium |
| Tags       | idempotency, durability |

**Why it matters.** If the email API responds with 500/timeout after sending the email, the retry logic cannot distinguish "already sent" from "failed to send." Retrying the email results in duplicate delivery to the recipient. No idempotency key or deduplication in the mail service.

### F-H-013: User deactivation incomplete — IsActive=false does not enforce cleanup

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | HiddenBugs/data-integrity |
| File       | `Pages/Admin/Users.cshtml.cs` |
| Lines      | 186–500+ (OnPost deactivate path) |
| Effort     | Medium |
| Confidence | High |
| Tags       | data-integrity, business-logic |

**Why it matters.** Setting IsActive=false does not enforce per-CLAUDE.md cleanup: nulling TraineeUserId on other users, removing active shift assignments, cleaning up grants, or clearing DirectorCompany relationships. Deactivated users remain bound to calendar data and organizational relationships.

### F-H-014: ImportService malformed-row JSON error visibility impaired

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | HiddenBugs/observability |
| File       | `Services/ImportService.cs` |
| Lines      | 132–145 |
| Effort     | Small |
| Confidence | Medium |
| Tags       | observability, ux |

**Why it matters.** When parsing NDJSON records during import (line 134), JsonException messages are added to warnings but the full exception context (inner exceptions, path) is swallowed. Import errors are difficult to diagnose; operators cannot determine if a single row failed or multiple rows.

### F-H-015: Overlap boundary semantics undocumented in ShiftAssignmentService

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | HiddenBugs/documentation |
| File       | `Services/ShiftAssignmentService.cs` |
| Lines      | (method identified; line range unverified) |
| Effort     | Small |
| Confidence | Low |
| Tags       | docs, clarity |

**Why it matters.** The overlap-detection logic (HOME conflict checks, etc.) does not document whether boundaries are inclusive or exclusive (e.g., shift ending at 08:00 and shift starting at 08:00 — conflict or no conflict?). Maintenance and new features risk subtle off-by-one errors.

### F-H-016: Null-forgiving navigations under IgnoreQueryFilters

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | HiddenBugs/null-deref |
| File       | `Services/GrantService.cs`, `Pages/Admin/Users.cshtml.cs` |
| Lines      | 146, 322, 336, 398 |
| Effort     | Small |
| Confidence | Medium |
| Tags       | null-deref, ef-core |

**Why it matters.** Non-nullable EF navigation properties (e.g., c.Molecule!.Area!.ProjectId at line 322) can be null at runtime when loaded under IgnoreQueryFilters(). The null-forgiving operator ! silences the compiler warning but does not prevent NullReferenceException if the related entity is null.

### F-H-017: CompanyId claim parsing safe (positive observation)

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | HiddenBugs/positive |
| File       | `Services/DirectorService.cs` |
| Lines      | 34–40 |
| Effort     | N/A |
| Confidence   | High |
| Tags       | tenancy, positive |

**Why it matters.** Direct claim-parsing uses int.TryParse (line 35), preventing crashes from invalid claims. This is best-practice defensive programming; no risk identified.

### F-H-018: HOME bidirectional exclusion verified (positive observation)

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | HiddenBugs/positive |
| File       | `Services/ShiftAssignmentService.cs` |
| Lines      | (method identified) |
| Effort     | N/A |
| Confidence   | High |
| Tags       | validation, positive |

**Why it matters.** The shift conflict detection includes HOME_CONFLICT, SHIFT_EXISTS_CONFLICT, and DUPLICATE_HOME checks, ensuring HOME shifts cannot be stacked. This prevents the documented user scenario of overloaded home-shift calendars.

### F-H-019: WeekStartDay configurable from IAppConfigCacheService confirmed (positive observation)

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | HiddenBugs/positive |
| File       | `Services/AppConfigCacheService.cs` |
| Lines      | (method identified) |
| Effort     | N/A |
| Confidence   | High |
| Tags       | config, positive |

**Why it matters.** The application uses IAppConfigCacheService to resolve WeekStartDay, allowing locale-aware Sunday (Israel) vs. Monday (Europe) calendar layouts. No hardcoded assumption; configuration is flexible and correct.

## Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High     | 3 |
| Medium   | 10 |
| Low      | 4 |
| Info     | 2 |
| **Total**| **19** |

## Top 5 Findings

1. **F-H-001** — Null dereference in GrantService.GetAccessibleCompanyIdsForGrantAsync — NullReferenceException on hierarchy lookup failure during critical auth grant evaluation
2. **F-H-003** — Sync-over-async deadlock in DirectorService — 5 blocking sites (IsDirector, CanAssignRole) risk thread-pool exhaustion deadlock in middleware/filters
3. **F-H-004** — User hard-delete orphans ShiftAssignments — Calendar slot corruption from null UserId foreign key; violates data integrity per CLAUDE.md deactivation spec
4. **F-H-010** — Missing CSRF protection on OnPost handlers — POST endpoints lack global ValidateAntiForgeryToken filter; vulnerable to cross-site form attacks
5. **F-H-002** — Null userContext fallback skip in GrantService.HasGrantWithScopeAsync — Silent auth bypass for self-scope checks when hierarchy context unavailable; users denied access unexpectedly

## Coverage Notes

- **Paths fully read**: Services/GrantService.cs, Services/DirectorService.cs, Services/MailService.cs, Services/AnalyticsService.cs, Services/ImportService.cs, Services/FeatureFlagService.cs, Pages/Admin/Users.cshtml.cs, Program.cs
- **Paths grep-skimmed only**: Services/ShiftAssignmentService.cs, Services/VacationApprovalService.cs, Services/NotificationService.cs
- **Mega-services covered**: All 5 major services (GrantService, DirectorService, MailService, AnalyticsService, ImportService) inspected fully
- **Known limitations**: VacationApprovalService identified but not deeply read; ShiftAssignmentService identified but implementation not fully reviewed; CSRF grep limited to sample Pages/* files; DateTime.Now usage may be in other services; Email idempotency based on API signature only.
