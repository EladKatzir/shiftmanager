# Multi-Company Membership — Epic 4: Admin/Users Membership UI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let admins see and manage a user's company memberships on `/Admin/Users` — company chips, an expander row listing memberships, and add / remove (with impact preview) / make-primary actions — fully localized (English + Hebrew).

**Architecture:** Backend first — a company-scoped removal that clears the user's records in the removed company (mirroring `UserCompanyTransferService`'s cleanup, filtered by that CompanyId) + an impact-count method, since Epic 1's `RemoveMembershipAsync` was soft-delete-only (orphan-cleanup was deferred to this epic). Then the page model (VM + handlers) and the Razor/JS UI, reusing the existing Move dialog's "fetch impact → confirm → POST" flow and the AuditLog expander-row pattern.

**Tech Stack:** ASP.NET Core 8.0 Razor Pages, EF Core + SQLite, xUnit + real-SQLite fixture, `IStringLocalizer<SharedResources>` (resx en + he-IL).

**Spec:** `docs/superpowers/specs/2026-06-10-multi-company-membership-design.md` §11.

**Scope (this epic):** chips, expander, add, remove-with-impact, set-primary, bilingual localization. **Deferred (disclosed):** per-membership inline EDITING of role/jobtype/doesShifts/categories (change via remove+re-add for now); applying role-template GRANTS on add (that's Epic 5 — Add here creates the membership row with role/jobtype, but grant application lands in Epic 5).

> **Build-lock + concurrency:** peer session also builds on `dev`. App not running before build/test; locked-exe → STOP. Commit each task separately with explicit paths; never `git add .`/`-A`; never stage `packages.lock.json`/`packages/`.

---

### Task 1: Company-scoped membership removal + impact (backend, TDD)

**Files:** Modify `Services/ICompanyMembershipService.cs` + `Services/CompanyMembershipService.cs`; Test `ShiftManager.Tests/UnitTests/Services/CompanyMembershipRemovalTests.cs`

Add two methods (the impact mirrors `IUserCompanyTransferService.MoveImpact` shape but is scoped to ONE company the user is leaving):

```csharp
// in ICompanyMembershipService
/// <summary>Counts of what removing the user's membership in this company would clear (read-only).</summary>
Task<MembershipRemovalImpact> GetRemovalImpactAsync(int userId, int companyId);

/// <summary>Clear the user's records scoped to this company, then soft-delete the membership.
/// Refuses to remove the primary membership (promote another first). Transactional.</summary>
Task<bool> RemoveMembershipWithCleanupAsync(int userId, int companyId, int actingAdminId);
```
with:
```csharp
public record MembershipRemovalImpact(
    int FutureShifts, int PendingOrFutureTimeOff, int FutureChores, int OpenSwapRequests,
    int FutureOnDuty, int GrantsRemoved);
```

- [ ] **Step 1 — failing tests** (real SQLite, FK-off harness as in `CompanyMembershipServiceTests`):
  - Seed user 1 with a primary membership in company 10 and an additional membership in company 20. Seed company-20-scoped records for the user: a future `ShiftAssignment` (CompanyId=20), a pending `TimeOffRequest` (CompanyId=20), and a `Grant` (UserId=1, CompanyId=20). Also seed company-10 records to PROVE they are NOT touched.
  - `GetRemovalImpactAsync(1, 20)` returns counts reflecting the company-20 records (e.g. FutureShifts=1, PendingOrFutureTimeOff=1, GrantsRemoved=1).
  - `RemoveMembershipWithCleanupAsync(1, 20, actingAdminId: 99)` returns true; afterward: the company-20 membership is soft-deleted (`IsMemberAsync(1,20)` false), the company-20 future shift/timeoff/grant are gone, and ALL company-10 records + the primary membership are INTACT.
  - `RemoveMembershipWithCleanupAsync(1, 10, ...)` (the primary) throws/returns false (cannot remove primary).
- [ ] **Step 2 — run, verify fail.**
- [ ] **Step 3 — implement.** Read `Services/UserCompanyTransferService.cs` `MoveUserToCompanyAsync` to mirror WHICH record types to clear, but filter every clear by `CompanyId == companyId` (the company being left) AND `UserId == userId`, inside a transaction. Clear: future `ShiftAssignments` (join `ShiftInstance.WorkDate >= today` AND the assignment/instance CompanyId == companyId), pending/future `TimeOffRequests` (CompanyId == companyId), future `Chores`/`OnDuty` (soft-cancel, CompanyId == companyId), open `SwapRequests` (CompanyId == companyId), `Grants` scoped to that company (CompanyId == companyId). Then soft-delete the `CompanyMembership` (reuse the Epic-1 soft-delete). Use `IgnoreQueryFilters()` throughout (cross-tenant). Guard: refuse if the target membership `IsPrimary`. The impact method runs the same counts WITHOUT mutating.
  - Inject whatever the cleanup needs (the service already has `AppDbContext`). Keep `RemoveMembershipAsync` (soft-delete only) for callers that don't want cleanup, OR have it delegate — your call, but don't break existing Epic-1 tests.
- [ ] **Step 4 — run tests, verify pass. Build. Commit** the interface, service, test. Message: `feat(membership): company-scoped membership removal + impact`.

---

### Task 2: Page model — VM + handlers

**Files:** Modify `Pages/Admin/Users.cshtml.cs` (inject `ICompanyMembershipService`; extend `UserVM`; batch-load memberships; add handlers). Consider a new partial `Pages/Admin/Users.cshtml.Membership.cs` for the new handlers (mirrors how Move lives in `Users.cshtml.Move.cs`).

- [ ] **Step 1 — extend the VM + loading.** Add to `UserVM`: `int CompanyId` and `IReadOnlyList<UserMembershipVM> Memberships` where:
```csharp
public record UserMembershipVM(int MembershipId, int CompanyId, string CompanyName,
    bool IsPrimary, string? RoleName, string? JobTypeName, bool DoesShifts);
```
Batch-load memberships for the page's users: after `userData` is materialized, query `_db.CompanyMemberships.IgnoreQueryFilters().Where(m => userIds.Contains(m.UserId) && !m.IsDeleted)` once, group by UserId, and resolve company names (reuse the company name lookup already used for the primary). Populate `Memberships` per `UserVM` (include the primary so the expander shows all). Set `CompanyId` from the user's `AppUser.CompanyId`.
- [ ] **Step 2 — handlers** (in the new partial), mirroring the Move handlers' structure (auth via the same `EditCompanyUsers`/`AdminAccess` checks, `RedirectToPage()` for POSTs, `JsonResult` for the impact GET):
  - `OnPostAddMembershipAsync(int userId, int companyId, int? roleTemplateId, int? jobTypeId, bool doesShifts)` → authorize (admin must have `EditCompanyUsers` for the DESTINATION company), call `_companyMembershipService.AddMembershipAsync(...)`, audit-log, `TempData` success/error, redirect. (Grant application is Epic 5 — do not apply grants here; leave a `// Epic 5` comment.)
  - `OnGetRemoveMembershipImpactAsync(int userId, int companyId)` → `_companyMembershipService.GetRemovalImpactAsync` → build a localized summary string (new resx key `Users_RemoveMembershipImpact_Summary` with the count placeholders) → `JsonResult(new { ok = true, summary })`.
  - `OnPostRemoveMembershipAsync(int userId, int companyId)` → authorize, `_companyMembershipService.RemoveMembershipWithCleanupAsync`, audit-log, redirect with TempData.
  - `OnPostSetPrimaryMembershipAsync(int userId, int companyId)` → authorize, `_companyMembershipService.SetPrimaryAsync`, audit-log, redirect.
- [ ] **Step 3 — build. Commit** `Users.cshtml.cs` + the new partial. Message: `feat(membership): Admin/Users membership VM + add/remove/set-primary handlers`.

(No new unit test file is strictly required here if Task 1 covers the service; if the page model exposes testable logic, add a focused test. Otherwise rely on build + full suite.)

---

### Task 3: Razor UI + localization

**Files:** Modify `Pages/Admin/Users.cshtml`; add keys to `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx`.

- [ ] **Step 1 — Company column → chips.** In the user row's Company cell (currently `<td>@u.CompanyName</td>`), render the primary company in bold plus a small chip per additional membership company, and a chevron button that toggles the expander when `u.Memberships.Count > 1`. Keep the single-company case visually unchanged (just `@u.CompanyName`).
- [ ] **Step 2 — expander row.** After each user `<tr>`, add a hidden `<tr class="details-row" id="memberships-@u.Id">` (AuditLog pattern) spanning the table columns, containing a small memberships table: one row per `u.Memberships` showing company / role / jobtype / DoesShifts / a Primary badge, with per-row action buttons:
  - "Make primary" (when `!m.IsPrimary`) → posts `?handler=SetPrimaryMembership` (hidden form, userId+companyId).
  - "Remove" (when `!m.IsPrimary`) → opens a `<dialog id="removeMembershipDialog">`, fetches `?handler=RemoveMembershipImpact&userId=&companyId=` for the summary (mirror the Move dialog JS exactly), then the confirm button posts `?handler=RemoveMembership`.
  - An "➕ Add to company" button at the bottom → opens `<dialog id="addMembershipDialog">` with a company `<select>` (from `Model.AvailableCompanies`, excluding companies the user already belongs to), a role-template `<select>` (from `Model.AssignableRoleTemplates`), a job-type `<select>` (from `Model.AvailableJobTypes`), and a DoesShifts checkbox → posts `?handler=AddMembership`.
- [ ] **Step 3 — localization.** Add to BOTH resx files (en + he-IL) every new key, e.g.: `Users_Companies`, `Users_Membership_Primary`, `Users_Membership_MakePrimary`, `Users_Membership_Remove`, `Users_Membership_Add`, `Users_Membership_AddTitle`, `Users_RemoveMembershipImpact_Summary` (with `{0}..{5}` counts), `Users_Membership_RemoveConfirm`, success/error TempData keys (`Users_Membership_Added`, `Users_Membership_Removed`, `Users_Membership_PrimarySet`). Use `<loc key="..."/>` or `@Localizer["..."]` consistently with the rest of `Users.cshtml`. Provide correct Hebrew translations (the app is RTL; match the tone of existing entries).
- [ ] **Step 4 — build (Razor compiles), manual sanity.** Commit `Users.cshtml` + both resx files. Message: `feat(membership): Admin/Users company chips + membership expander UI (bilingual)`.

---

### Task 4: Full-suite verification + bilingual check

- [ ] **Step 1 — app not running. Step 2 — `dotnet build`** → 0 errors. **Step 3 — full suite SEQUENTIAL** → all pass. **Step 4** — confirm there are NO hardcoded user-facing strings in the new markup (every label goes through `<loc>`/`@Localizer`), and every new resx key exists in BOTH `SharedResources.resx` and `SharedResources.he-IL.resx` (no missing-translation gaps). **Step 5 — final commit** if needed.

---

## Self-Review
- §11 chips → Task 3 step 1; expander with per-membership display → step 2; add → step 2/handler; remove with impact (reusing Move flow) → Task 1 (cleanup+impact) + Task 2 handler + Task 3 dialog; set-primary → Task 2/3.
- Remove reuses `UserCompanyTransferService` cleanup semantics but company-scoped (Task 1), closing the Epic-1 deferral.
- Deferred (disclosed): per-membership inline attribute editing; grant application on add (Epic 5).
- Localization parity enforced in Task 4 step 4.
- Placeholders: none.
