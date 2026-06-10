# Multi-Company Membership — Epic 3: Four Must-Fix Bugs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Fix four bugs that multi-company membership exposes (and that are latent even today): avatar URL uses the wrong company, the home-page notification count drops a member's secondary-company notifications, BusyService rejects valid cross-company assignments, and profile-change audits are filed under the wrong company.

**Architecture:** Each fix is independent and individually shippable. Three are person-/owner-centric one-liners; BusyService needs the membership set (primary ∪ memberships) so it must stay backward-compatible.

**Tech Stack:** ASP.NET Core 8.0, EF Core + SQLite, xUnit (+ real-SQLite fixture where EF is touched).

**Spec:** `docs/superpowers/specs/2026-06-10-multi-company-membership-design.md` §10.

> **Build-lock + concurrency:** peer session also builds on `dev`. Ensure app not running before build/test; on a locked-exe error STOP and report. Commit each bug separately with explicit paths; never `git add .`/`-A`; never stage `packages.lock.json`/`packages/`.

---

### Task 1: Bug 4 — ProfileService audit filed under the editor's tenant

**Files:** Modify `Services/ProfileService.cs:125`; Test `ShiftManager.Tests/UnitTests/Services/ProfileServiceTests.cs`

- [ ] **Step 1 — failing regression test.** Add a test where the editor's tenant (`_tenantResolverMock.GetCurrentTenantId()` returns 1) differs from the TARGET user's `CompanyId` (= 2). Call `UpdateProfileAsync` changing a field, then assert the persisted `ProfileChangeAudit` row has `CompanyId == 2` (the target user's company), NOT 1. (The existing test setup hides the bug because both are 1 — use a distinct target company here. Use real SQLite if the test reads the audit back via EF; otherwise assert on the captured entity.)
- [ ] **Step 2 — run, verify fail** (audit CompanyId comes back 1).
- [ ] **Step 3 — fix.** In `Services/ProfileService.cs`, replace line 125:
```csharp
var companyId = _tenantResolver.GetCurrentTenantId();
```
with:
```csharp
// Audit must be filed under the EDITED user's company, not the editor's active tenant —
// otherwise cross-company/admin edits land in the wrong company's audit ledger.
var companyId = targetUser.CompanyId;
```
- [ ] **Step 4 — run, verify pass.** Confirm no other ProfileService test regressed (the resolver is otherwise unused in this method).
- [ ] **Step 5 — build + commit** `Services/ProfileService.cs` + test. Message: `fix(membership): file profile-change audit under edited user's company`.

---

### Task 2: Bug 2 — Home page drops a member's secondary-company notifications

**Files:** Modify `Pages/Home/Index.cshtml.cs:~127-129`; Test: add to an appropriate page/notification test (or a focused new test) — see step 1.

- [ ] **Step 1 — failing test.** With a user who has notifications under TWO different `CompanyId`s (e.g. one row CompanyId=1, one CompanyId=2, both `UserId=42`, both unread) and the active tenant = 1, assert the home page's unread count includes BOTH (count = 2). If `Pages/Home/Index.cshtml.cs` has no unit-test sibling, write a focused test that runs the same query shape against real SQLite, OR test via the page model if constructable. (If the page model is not unit-testable, write a minimal real-SQLite test that reproduces the query `UserNotifications.Where(n => n.UserId == userId).CountAsync()` returns 2 across companies and pin it as the contract.)
- [ ] **Step 2 — run, verify fail** (count = 1 with the CompanyId predicate).
- [ ] **Step 3 — fix.** In `Pages/Home/Index.cshtml.cs`, remove the `&& n.CompanyId == companyId` predicate from the notifications query so it filters by `n.UserId == userId` only (a user owns all their notifications regardless of which company generated them — consistent with the already-correct bell widget `UnreadNotificationCountViewComponent` and `NotificationCenter`). Leave the `companyId` variable if still used elsewhere in the method; otherwise remove it if now unused (fix any resulting warning).
- [ ] **Step 4 — run, verify pass.**
- [ ] **Step 5 — build + commit** `Pages/Home/Index.cshtml.cs` + test. Message: `fix(membership): home notification count spans all the user's companies`.

---

### Task 3: Bug 1 — Avatar URL uses the viewer's tenant, not the owner's company

**Files:** Modify `Services/AvatarService.cs` (`GetAvatarUrl` + interface `Services/IAvatarService.cs`); callers `Pages/Admin/EditProfile.cshtml.cs:~623`, `Pages/My/Profile.cshtml.cs:~351`; Test: new `ShiftManager.Tests/UnitTests/Services/AvatarServiceUrlTests.cs`

- [ ] **Step 1 — failing test.** Construct `AvatarService` with an `ITenantResolver` returning 99 (the viewer's tenant). Call `GetAvatarUrl(userId: 42, avatarFileName: "42.jpg", thumbnail: false, ownerCompanyId: 7)` and assert the URL is `/avatars/7/42.jpg` (owner's company), NOT `/avatars/99/...`. Also assert `thumbnail: true` → `/avatars/7/42_thumb.jpg`. (No DB needed; this is pure string building. Mock the resolver.)
- [ ] **Step 2 — run, verify fail** (method has no `ownerCompanyId` param yet → compile fail).
- [ ] **Step 3 — fix.** Add an optional owner-company parameter (backward compatible). In `Services/IAvatarService.cs` change the signature to:
```csharp
string GetAvatarUrl(int userId, string? avatarFileName, bool thumbnail = false, int? ownerCompanyId = null);
```
In `Services/AvatarService.cs` `GetAvatarUrl`, replace:
```csharp
var companyId = _tenantResolver.GetCurrentTenantId();
```
with:
```csharp
// Avatars are stored under the OWNER's company folder (UploadAvatarAsync uses user.CompanyId).
// Resolve to the owner's company when provided; fall back to the active tenant only for
// same-company callers that don't pass it.
var companyId = ownerCompanyId ?? _tenantResolver.GetCurrentTenantId();
```
- [ ] **Step 4 — update the two callers** to pass the loaded user's company: `Pages/Admin/EditProfile.cshtml.cs:~623` and `Pages/My/Profile.cshtml.cs:~351` — pass `ownerCompanyId: user.CompanyId` (the full `AppUser` is loaded immediately above each call). For `My/Profile` (the user viewing themselves) this is a no-op behaviorally but correct; for `Admin/EditProfile` (admin may be cross-company) it fixes the bug.
- [ ] **Step 5 — run test, verify pass. Build. Commit** AvatarService.cs, IAvatarService.cs, the two callers, the test. Message: `fix(membership): build avatar URL from owner's company, not viewer's tenant`.

---

### Task 4: Bug 3 — BusyService rejects valid cross-company assignments

**Files:** Modify `Services/BusyService.cs` (constructor + the two `IsUserInMoleculeAsync(user.CompanyId, ...)` sites ~205 and ~353, and the helper ~871); Modify `ShiftManager.Tests/Helpers/BusyServiceMockFactory.cs`; Test `ShiftManager.Tests/UnitTests/Services/BusyServiceTests.cs`

- [ ] **Step 1 — failing test.** In `BusyServiceTests`, seed a molecule with two companies (A primary-of-user, B not) using the existing `SeedMoleculeWithCompaniesAsync` helper pattern — but make the TARGET shift/chore's molecule one the user reaches ONLY via a SECONDARY `CompanyMembership` (user's primary company is in a DIFFERENT molecule; user has a `CompanyMembership` in a company that IS in the target molecule). Assert validation does NOT produce `USER_NOT_IN_MOLECULE`. Also keep/confirm an existing test where the user's PRIMARY company is in the molecule still passes (backward compatibility — no membership rows needed).
- [ ] **Step 2 — run, verify fail** (new cross-membership case rejected).
- [ ] **Step 3 — fix, backward-compatible.** Inject `ICompanyMembershipService` into `BusyService` (add constructor param after the existing ones). Replace the helper `IsUserInMoleculeAsync(int userCompanyId, int moleculeId)` with one that checks the user's PRIMARY company id **union** their membership companies:
```csharp
// Accept the assignment if ANY company the user belongs to (primary OR an additional
// CompanyMembership) is in the target molecule. Always include user.CompanyId so users
// without explicit membership rows (pre-backfill / unit tests) keep working.
private async Task<bool> IsUserInMoleculeAsync(AppUser user, int moleculeId)
{
    var companyIds = new HashSet<int> { user.CompanyId };
    foreach (var m in await _membershipService.GetMembershipsAsync(user.Id))
        companyIds.Add(m.CompanyId);
    return await _db.Companies.IgnoreQueryFilters()
        .AnyAsync(c => companyIds.Contains(c.Id) && c.MoleculeId == moleculeId);
}
```
Update both call sites (~205, ~353) to pass `user` instead of `user.CompanyId`.
- [ ] **Step 4 — update the test factory.** In `ShiftManager.Tests/Helpers/BusyServiceMockFactory.cs` (the `Real(...)` builder ~lines 93-99), construct a real `CompanyMembershipService(db, NullLogger<CompanyMembershipService>.Instance)` internally and pass it to the new `BusyService` parameter, so the ~15 existing callers of `BusyServiceMockFactory.Real` do NOT change. (If the factory has a mock/stub style instead, add the new dependency there consistently.)
- [ ] **Step 5 — run BusyServiceTests, verify pass** (both the new cross-membership case AND all existing tests). Build.
- [ ] **Step 6 — commit** `Services/BusyService.cs`, `ShiftManager.Tests/Helpers/BusyServiceMockFactory.cs`, `BusyServiceTests.cs`. Message: `fix(membership): BusyService accepts any membership company for molecule check`.

---

### Task 5: Full-suite verification

- [ ] **Step 1 — app not running.** **Step 2 — `dotnet build`** → 0 errors. **Step 3 — full suite SEQUENTIAL:** `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -- xUnit.ParallelizeTestCollections=false` → all pass. **Step 4 — final commit** if anything uncommitted.

---

## Self-Review
- §10 bug #1 → Task 3; #2 → Task 2 (corrected to Home/Index, the real person-centric leak; AnalyticsController aggregate left intentional); #3 → Task 4 (primary ∪ memberships, backward-compatible, factory-injected); #4 → Task 1.
- Each task ships an independent fix + a regression test that fails before and passes after.
- BusyService change is backward-compatible (always includes `user.CompanyId`), so the ~15 factory callers and no-membership-row tests keep passing.
- Placeholders: none.
