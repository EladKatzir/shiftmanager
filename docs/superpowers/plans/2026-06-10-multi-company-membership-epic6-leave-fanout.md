# Multi-Company Membership — Epic 6: Leave Fan-Out + Union Approvers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** When a multi-company user files time-off, create one linked copy per company they do shifts in (sharing a `LeaveGroupId`); let any eligible manager from any of their companies approve; a single decision cascades to the whole group. Single-company users are unaffected.

**Architecture:** Add `Guid? LeaveGroupId` to `TimeOffRequest`. Fan out at the web filing site. Widen the approver pool + approver authorization to the union of the requester's membership companies. Cascade approve/decline across the group. Dedup the two correctness-critical read sites (My/Requests list, AnalyticsService).

**Tech Stack:** ASP.NET Core 8.0, EF Core + SQLite, xUnit + real-SQLite.

**Spec:** `docs/superpowers/specs/2026-06-10-multi-company-membership-design.md` §8.

### SCOPE (read carefully — fan-out has app-wide implications):
**In scope:** `LeaveGroupId` migration; fan-out in `Pages/My/Requests.cshtml.cs OnPostTimeOffAsync`; union approver pool (My/Requests) + union approver authorization (`VacationApprovalService.CanUserApproveInternalAsync`); cascade approve/decline (`VacationApprovalService`); dedup My/Requests list + `AnalyticsService` time-off stats by `LeaveGroupId`.
**DEFERRED (disclosed — lower-risk for the rare multi-company user, NOT filer-facing correctness):** fan-out on `Services/Api/TimeOffApiService.cs` + `Services/ImportService.cs` (keep single-row there); manager-inbox dedup in `Pages/Requests/Index.cshtml.cs` (eventual UX = a "spans N companies" badge); director cross-company calendar-overlay dedup (`GetOverviewData`, `ShiftCalendarService`); `ArchiveService` count dedup. Each is tracked for a follow-up pass.

> **Invariant:** single-company users (or multi-company users who do shifts in only one company) get NO fan-out — `LeaveGroupId` stays null, exactly one row, existing behavior byte-for-byte. Fan-out triggers ONLY when the filer has ≥2 active membership companies with `DoesShifts == true`.

> **Build-lock + concurrency:** peer session builds/tests on `dev`. App not running before build. **Run the full suite SOLO** (peer's concurrent `dotnet test` causes ~220 spurious failures — if you see that, re-run alone). When adding a ctor param, build the SOLUTION (`dotnet build`) so the TEST project is checked too. Commit per task, explicit paths, never `git add .`/`-A`/packages.

---

### Task 1: `LeaveGroupId` column + migration

**Files:** `Models/TimeOffRequest.cs`; new `Migrations/*_AddLeaveGroupIdToTimeOffRequest.cs`; snapshot.

- [ ] **Step 1 — add the field.** In `Models/TimeOffRequest.cs` add:
```csharp
/// <summary>
/// Groups the fan-out copies of one logical leave across the companies a multi-company user
/// does shifts in. NULL for ordinary single-company leaves. Approving/declining any copy
/// cascades the decision to all copies sharing this id. Aggregate reads (lists, analytics)
/// dedup by this id to count one logical leave.
/// </summary>
public Guid? LeaveGroupId { get; set; }
```
- [ ] **Step 2 — migration.** `dotnet ef migrations add AddLeaveGroupIdToTimeOffRequest`. Confirm `Up()` adds a nullable `TEXT` column `LeaveGroupId` to `TimeOffRequests` (SQLite stores Guid as TEXT). No data backfill (existing rows stay null). Build the SOLUTION.
- [ ] **Step 3 — commit** model + migration. Message: `feat(membership): add LeaveGroupId to TimeOffRequest`.

---

### Task 2: Fan-out at filing + dedup the filer's own list

**Files:** `Pages/My/Requests.cshtml.cs` (`OnPostTimeOffAsync` create block ~287-310, and `OnGetAsync` MyTimeOffRequests load ~78-91); inject `ICompanyMembershipService`; Test: `ShiftManager.Tests/UnitTests/Pages/LeaveFanoutTests.cs` (or a service-seam test — see below).

- [ ] **Step 1 — failing test.** This is page-handler logic; prefer extracting the fan-out into a TESTABLE service method. Add to `ICompanyMembershipService` (or a small new helper) is overkill — instead extract a private async helper in the page OR (cleaner + testable) add a method on a service. RECOMMENDED: put the fan-out in a new injectable `ILeaveFanoutService` with `Task<Guid?> FanOutAsync(TimeOffRequest primary, int userId)` that, given the saved primary request, looks up `GetMembershipsAsync(userId)` filtered to `DoesShifts && CompanyId != primary.CompanyId`, and if any exist: assigns a new `LeaveGroupId` to the primary, creates a clone per company (explicit `CompanyId`, same dates/type/reason/status, same `LeaveGroupId`), saves, returns the group id; if none, returns null and leaves the primary untouched. Test it with real SQLite: a user with shift-memberships in companies 10 (primary) + 20 + 30 → filing one request produces 3 rows sharing one non-null `LeaveGroupId`, each with the right `CompanyId`; a single-company user → 1 row, `LeaveGroupId == null`.
- [ ] **Step 2 — run, verify fail.**
- [ ] **Step 3 — implement** `ILeaveFanoutService`/`LeaveFanoutService` (inject `AppDbContext` + `ICompanyMembershipService`), DI-register it. The primary request keeps its own `CompanyId` (set by interceptor/tenant); clones use explicit `CompanyId` (the interceptor won't overwrite a non-zero value). Call it from `OnPostTimeOffAsync` right after the primary `SaveChangesAsync` and BEFORE the `SubmitForApprovalAsync` call — then call `SubmitForApprovalAsync` for EACH copy (so each company's approval route is set up). Use a transaction so the primary + clones commit together.
- [ ] **Step 4 — dedup the filer's list.** In `OnGetAsync`, `MyTimeOffRequests` (~78-91): when a request has a `LeaveGroupId`, show only ONE row per group (e.g. the lowest-Id copy, or the primary-company copy) — group by `LeaveGroupId ?? Id` and take one. Optionally annotate it as spanning N companies (a count), but at minimum do not show N duplicates.
- [ ] **Step 5 — run tests, verify pass. Build solution. Commit** the service, DI, page changes, test. Message: `feat(membership): fan out leave across shift companies + dedup filer list`.

---

### Task 3: Union approver pool + union approver authorization

**Files:** `Pages/My/Requests.cshtml.cs` (approver pool ~151-198); `Services/VacationApprovalService.cs` (`CanUserApproveInternalAsync` ~543-563); Tests in `VacationApprovalServiceTests`/`VacationApprovalApproveTests`.

- [ ] **Step 1 — failing test (authorization).** In `VacationApprovalApproveTests`, seed a requester (user 10) with shift-memberships in companies 1 AND 2, and a request (the company-1 copy). Seed an approver (user 20) who holds `ApproveVacations` ONLY in company 2 (mock `_grantService` to return true for `HasGrantWithScopeAsync(20, key, companyId: 2, ...)` and false for company 1). Assert `CanUserApproveAsync(20, requestId)` returns TRUE (because user 20 holds the grant in company 2, one of the requester's membership companies). Before the fix it returns false (only checks `request.CompanyId == 1`).
- [ ] **Step 2 — run, verify fail.**
- [ ] **Step 3 — implement union authorization.** In `CanUserApproveInternalAsync`, replace the single `HasGrantWithScopeAsync(userId, grantKey, companyId: request.CompanyId, jobTypeId: ...)` with a check across the REQUESTER's membership companies: load `GetMembershipsAsync(request.UserId)` (inject `ICompanyMembershipService` into `VacationApprovalService`), build the set of company ids (always include `request.CompanyId`), and return true if the approver holds the grant in ANY of them (loop / OR). Keep the specific-approver short-circuit (`userId == specificApproverId`) intact. (When adding the ctor param, build the SOLUTION — existing VacationApproval test ctors will need the new arg; update them with a stub returning the request user's company only, so existing single-company tests are unaffected.)
- [ ] **Step 4 — union approver POOL (UI).** In `OnGetAsync` (My/Requests), widen the approver-pool query: collect company ids + molecule ids from `GetMembershipsAsync(userId).Where(m => m.DoesShifts)` (always include the user's own) and change the `Where` clauses from the single `currentUser.CompanyId`/`companyMoleculeId` to `memberCompanyIds.Contains(...)` / `memberMoleculeIds.Contains(...)`. Single-company users get the identical pool as before.
- [ ] **Step 5 — run tests, verify pass. Build solution. Commit.** Message: `feat(membership): union approver pool + cross-company approver authorization`.

---

### Task 4: Cascade approve / decline across the group

**Files:** `Services/VacationApprovalService.cs` (`ApproveAsync` after final-approval commit ~402; `DeclineAsync` after commit ~494); Tests in `VacationApprovalApproveTests`.

- [ ] **Step 1 — failing tests.** Seed TWO requests sharing one `LeaveGroupId` (company-1 copy + company-2 copy), both `Pending`, requester user 10, approver user 20 authorized for both. 
  - Approve test: `ApproveAsync(company1CopyId, 20)` → BOTH copies become `Approved` (or `PendingSecondApproval` consistently if dual-approval applies — but for a single-approval rule, both → Approved). Assert the sibling's `Status == Approved` and its approval-actor fields are set.
  - Decline test: `DeclineAsync(company1CopyId, 20)` → BOTH copies become `Declined`.
  - Also assert a request with NULL `LeaveGroupId` does NOT touch any other request (no accidental cascade).
- [ ] **Step 2 — run, verify fail.**
- [ ] **Step 3 — implement cascade.** In `ApproveAsync`, after the primary request reaches a FINAL state (`finalApproved == true` → `Approved`), if `request.LeaveGroupId != null`, load sibling requests (`LeaveGroupId == request.LeaveGroupId && Id != request.Id && Status NOT terminal`) and set each to the SAME terminal status, copying the approval-actor fields, in the same or a follow-on transaction; then fire `ProcessApprovalSideEffectsAsync` for each sibling (so each company's shift materialiser/notifications run). IMPORTANT dual-approval nuance: only cascade when THIS request reached a TERMINAL state (`Approved`/`Declined`) — do NOT cascade an intermediate `PendingSecondApproval` (each copy advances its own tiers; cascade only the final outcome). Mirror the same pattern in `DeclineAsync` for `Declined`. Guard against infinite loops (only act on siblings, never re-enter for the current id).
- [ ] **Step 4 — run tests, verify pass (incl. the null-LeaveGroupId no-cascade test and a dual-approval-doesn't-prematurely-cascade test if feasible). Build solution. Commit.** Message: `feat(membership): cascade leave approval/decline across the LeaveGroupId group`.

---

### Task 5: Dedup AnalyticsService time-off stats by LeaveGroupId

**Files:** `Services/AnalyticsService.cs` (`GetTimeOffStatsAsync` ~569-588, `GetAverageDaysOffPerEmployeeAsync` ~603-621, `GetTimeOffByMonthAsync` ~636+); Test: `ShiftManager.Tests/UnitTests/Services/AnalyticsServiceTests.cs` (if exists; else focused).

- [ ] **Step 1 — failing test.** Seed a leave fanned out into 2 companies (2 rows, same `LeaveGroupId`, both Approved). Assert `GetTimeOffStatsAsync` counts it as ONE request (not two) and `GetAverageDaysOffPerEmployeeAsync` counts the days ONCE.
- [ ] **Step 2 — run, verify fail (counts doubled).**
- [ ] **Step 3 — implement.** In each affected aggregate, dedup by `LeaveGroupId`: count each non-null `LeaveGroupId` group once (e.g. filter to the canonical copy per group: rows where `LeaveGroupId == null OR Id == (min Id within the group)`), keeping all single-row (null group) requests as-is. A clean approach: project to a deduped set in memory (`GroupBy(r => r.LeaveGroupId ?? Guid.NewGuid()-equivalent)` won't translate; instead compute `canonicalIds = rows.Where(LeaveGroupId != null).GroupBy(LeaveGroupId).Select(g => g.Min(Id))` then filter `LeaveGroupId == null || canonicalIds.Contains(Id)`). Apply consistently across the three aggregates.
- [ ] **Step 4 — run tests, verify pass. Build solution. Commit.** Message: `fix(membership): dedup time-off analytics by LeaveGroupId`.

---

### Task 6: Full-suite verification (SOLO)

- [ ] **Step 1 — app not running; ensure the peer session is NOT running `dotnet test` (else ~220 spurious failures). Step 2 — `dotnet build`** (solution) → 0 errors. **Step 3 — full suite SEQUENTIAL** → all pass; if ~220 failures appear, re-run SOLO. **Step 4 — final commit** if needed.

---

## Self-Review
- §8: LeaveGroupId (Task 1); fan-out per shift company (Task 2); union pool (Task 3 step 4); union authorization (Task 3 step 3); cascade single-decision (Task 4). High-risk dedup (My/Requests, Analytics) Tasks 2/5.
- Invariant: fan-out only when ≥2 shift-companies; single-company unchanged (tested).
- Deferred (disclosed): API/Import fan-out, manager-inbox dedup, calendar-overlay dedup, archive dedup.
- Dual-approval interaction explicitly handled (cascade only on TERMINAL state) — Task 4 step 3.
- Placeholders: none.
