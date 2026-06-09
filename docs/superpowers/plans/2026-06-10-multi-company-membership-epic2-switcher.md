# Multi-Company Membership — Epic 2: Active-Company Switcher — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let a non-Owner user who belongs to multiple companies (Epic 1 `CompanyMembership`) switch which company is their active tenant, reusing the Owner-selector pattern but membership-validated — so the whole app's CompanyId-scoped surface follows the switch.

**Architecture:** Bake a `MemberCompanyIds` claim at login (the user's active membership company-id set). A new `IActiveCompanySelectorService` writes a `member_selected_company` cookie after an authoritative async `IsMemberAsync` check. `TenantResolver` gains a rung between the Owner block and the `CompanyId` claim that returns the cookie value **iff** it's in the `MemberCompanyIds` claim (sync validation, no DB). The `ContextSwitcher` regular-user branch lists membership companies and POSTs to a new `/Api/SelectMemberCompany` endpoint.

**Tech Stack:** ASP.NET Core 8.0, EF Core + SQLite, xUnit + real-SQLite fixture.

**Spec:** `docs/superpowers/specs/2026-06-10-multi-company-membership-design.md` §5.

**Scope (this epic):** the switch *mechanism* — active CompanyId tenant follows the switch. **Out of scope (deferred to a dedicated hierarchy-staleness pass / Epic 7 QA, disclosed):** migrating stale `MoleculeId/AreaId/ProjectId` claim readers (e.g. `SaveQuickInfoConfig` IDOR, `OnCallWidgetViewComponent`) to follow the active company. The switch is correct for all CompanyId-filtered data/writes; molecule-scoped display under an active switch is handled separately.

> **Build-lock + concurrency:** a peer session also builds on `dev`. Before any build/test ensure the app isn't running; on a locked-exe error STOP and report. Commit only this epic's files with explicit paths; never `git add .`/`-A`; never stage `packages.lock.json`/`packages/`.

---

## File Structure

- **Create** `Services/IActiveCompanySelectorService.cs`, `Services/ActiveCompanySelectorService.cs` — membership-validated company selection (mirror of `OwnerCompanySelectorService`).
- **Modify** `Services/TenantResolver.cs` — new member-selected rung + a `MemberSelectedCookieName` const, reading the `MemberCompanyIds` claim for sync validation.
- **Modify** `Pages/Auth/Login.cshtml.cs` and `Services/GriffinService.cs` — bake the `MemberCompanyIds` claim at login.
- **Create** `Pages/Api/SelectMemberCompany.cshtml` + `.cshtml.cs` — POST endpoint (mirror `Pages/Api/SelectMolecule.cshtml.cs`).
- **Modify** `ViewComponents/ContextSwitcherViewComponent.cs` — add the regular-user multi-membership branch.
- **Modify** `Pages/Shared/Components/ContextSwitcher/Default.cshtml` — third `data-switch-mode` value + route in `selectOption`.
- **Modify** `Program.cs` — DI for `IActiveCompanySelectorService`.
- **Tests:** `ShiftManager.Tests/UnitTests/Services/ActiveCompanySelectorServiceTests.cs`, `.../Services/TenantResolverMemberSwitchTests.cs`, `.../Pages/SelectMemberCompanyTests.cs` (as feasible).

---

### Task 1: `MemberCompanyIds` claim at login

**Files:**
- Modify: `Services/GriffinService.cs` (claim-build block ~568-581, inject `ICompanyMembershipService`)
- Modify: `Pages/Auth/Login.cshtml.cs` (claim-build block ~365-381, inject `ICompanyMembershipService`)
- Test: extend `ShiftManager.Tests/UnitTests/Services/GriffinServiceTests.cs`

- [ ] **Step 1 — failing test (Griffin path).** Add a test asserting that when a user has ≥2 active memberships, `BuildPrincipalFromClaimsAsync` produces a `MemberCompanyIds` claim equal to the comma-joined active company ids; and that a single-company user does NOT get the claim. Use the existing GriffinServiceTests harness (real SQLite). Seed the user + two `CompanyMembership` rows.

- [ ] **Step 2 — run, verify fail.** `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~GriffinServiceTests.AuthenticateUserAsync_MultiCompany"` → FAIL.

- [ ] **Step 3 — implement.** In both claim-build sites, after the existing claims are assembled and before the principal is created, add (injecting `ICompanyMembershipService _companyMembershipService` via constructor in each class):

```csharp
// Multi-company: expose the user's active membership company-id set so TenantResolver can
// validate a member's active-company switch synchronously (no DB call in the resolver).
var memberships = await _companyMembershipService.GetMembershipsAsync(user.Id);
if (memberships.Count > 1)
{
    claims.Add(new Claim("MemberCompanyIds",
        string.Join(",", memberships.Select(m => m.CompanyId).Distinct())));
}
```
(Confirm the local list variable is named `claims` in each method; adapt to the actual local name.)

- [ ] **Step 4 — run, verify pass.**

- [ ] **Step 5 — build + commit** `Services/GriffinService.cs`, `Pages/Auth/Login.cshtml.cs`, the test file. Message: `feat(membership): bake MemberCompanyIds claim at login`.

---

### Task 2: `IActiveCompanySelectorService`

**Files:**
- Create: `Services/IActiveCompanySelectorService.cs`, `Services/ActiveCompanySelectorService.cs`
- Modify: `Program.cs` (DI beside `IOwnerCompanySelectorService`, ~line 283)
- Test: `ShiftManager.Tests/UnitTests/Services/ActiveCompanySelectorServiceTests.cs`

- [ ] **Step 1 — failing tests.** Using an `IHttpContextAccessor` with a fake `HttpContext` (DefaultHttpContext) and a real-SQLite `AppDbContext` + `CompanyMembershipService`, assert:
  - `SelectCompanyAsync(companyId)` returns true and writes the `member_selected_company` cookie when the user (NameIdentifier claim) is an active member of that company;
  - returns false and writes NO cookie when the user is not a member;
  - `GetSelectedCompanyId()` reads the cookie value back;
  - `ClearSelectionAsync()` deletes the cookie.

- [ ] **Step 2 — run, verify fail (types missing).**

- [ ] **Step 3 — interface:**
```csharp
namespace ShiftManager.Services;

/// <summary>
/// Lets a multi-company member choose which company is their active tenant. The authoritative
/// membership check happens here (async, DB) before the cookie is written; TenantResolver then
/// validates the cookie synchronously against the MemberCompanyIds claim. Mirrors
/// IOwnerCompanySelectorService but gated by CompanyMembership rather than the Owner role.
/// </summary>
public interface IActiveCompanySelectorService
{
    int? GetSelectedCompanyId();
    Task<bool> SelectCompanyAsync(int companyId);
    Task ClearSelectionAsync();
}
```

- [ ] **Step 4 — implementation** (mirror `OwnerCompanySelectorService` cookie options exactly: 12h MaxAge, HttpOnly, SameSite=Strict, Secure=IsHttps, Path="/"):
```csharp
using System.Security.Claims;
using ShiftManager.Services; // ICompanyMembershipService

namespace ShiftManager.Services;

public sealed class ActiveCompanySelectorService : IActiveCompanySelectorService
{
    public const string CookieName = "member_selected_company";
    private readonly IHttpContextAccessor _http;
    private readonly ICompanyMembershipService _memberships;

    public ActiveCompanySelectorService(IHttpContextAccessor http, ICompanyMembershipService memberships)
    {
        _http = http;
        _memberships = memberships;
    }

    public int? GetSelectedCompanyId()
    {
        var ctx = _http.HttpContext;
        if (ctx?.Request.Cookies.TryGetValue(CookieName, out var v) == true && int.TryParse(v, out var id))
            return id;
        return null;
    }

    public async Task<bool> SelectCompanyAsync(int companyId)
    {
        var ctx = _http.HttpContext;
        if (ctx == null) return false;
        var userId = GetUserId(ctx);
        if (userId == null) return false;

        if (!await _memberships.IsMemberAsync(userId.Value, companyId))
            return false; // authoritative gate — never trust the client

        ctx.Response.Cookies.Append(CookieName, companyId.ToString(), new CookieOptions
        {
            MaxAge = TimeSpan.FromHours(12),
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = ctx.Request.IsHttps,
            Path = "/"
        });
        return true;
    }

    public Task ClearSelectionAsync()
    {
        _http.HttpContext?.Response.Cookies.Delete(CookieName);
        return Task.CompletedTask;
    }

    private static int? GetUserId(HttpContext ctx)
    {
        var raw = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : (int?)null;
    }
}
```

- [ ] **Step 5 — DI:** `builder.Services.AddScoped<IActiveCompanySelectorService, ActiveCompanySelectorService>(); // Multi-company active-company switch`

- [ ] **Step 6 — run tests, verify pass. Build. Commit** the two service files, Program.cs, the test. Message: `feat(membership): IActiveCompanySelectorService (membership-validated)`.

---

### Task 3: `TenantResolver` member-selected rung

**Files:**
- Modify: `Services/TenantResolver.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/TenantResolverMemberSwitchTests.cs`

- [ ] **Step 1 — failing tests.** Build a `TenantResolver` with an `IHttpContextAccessor` whose `HttpContext.User` has claims `CompanyId=10` and `MemberCompanyIds=10,20`, and a `member_selected_company=20` cookie. Assert `GetCurrentTenantId()` returns 20. Then: cookie=`20` but `MemberCompanyIds=10` (not a member) → returns 10 (falls through to the CompanyId claim, ignoring the spoofed cookie). Then: no cookie → returns 10. Owner-selected still takes precedence (existing behavior unchanged).

- [ ] **Step 2 — run, verify fail.**

- [ ] **Step 3 — implement.** Add a const `private const string MemberSelectedCookieName = "member_selected_company";`. In `GetCurrentTenantId()`, AFTER the Owner block (priority 2) and BEFORE the `CompanyId` claim read (priority 3), insert:
```csharp
            // Priority 2.5: Multi-company member's selected active company.
            // Validate the cookie against the MemberCompanyIds claim (baked at login) so this
            // stays synchronous — never trust the cookie alone.
            if (httpContext?.Request.Cookies.TryGetValue(MemberSelectedCookieName, out var memberCookie) == true
                && int.TryParse(memberCookie, out var selectedCompanyId))
            {
                var allowed = user.FindFirst("MemberCompanyIds")?.Value;
                if (!string.IsNullOrEmpty(allowed)
                    && allowed.Split(',').Contains(selectedCompanyId.ToString()))
                {
                    _logger?.LogDebug("TenantResolver: member-selected CompanyId={CompanyId}", selectedCompanyId);
                    return selectedCompanyId;
                }
            }
```
(`user` is already in scope inside the authenticated branch. Place this inside that branch.)

- [ ] **Step 4 — run tests, verify pass. Build. Commit** TenantResolver.cs + test. Message: `feat(membership): TenantResolver honors validated member company switch`.

---

### Task 4: `POST /Api/SelectMemberCompany` endpoint

**Files:**
- Create: `Pages/Api/SelectMemberCompany.cshtml` (+ `.cshtml.cs`) — mirror `Pages/Api/SelectMolecule.cshtml(.cs)`
- Modify: `Program.cs` if the Api pages need `AllowAnonymousToPage`/registration (check how `/Api/SelectMolecule` is registered — match it; it is NOT anonymous, it requires auth)
- Test: `ShiftManager.Tests/UnitTests/Pages/SelectMemberCompanyTests.cs` (if the handler is unit-testable like SelectMolecule)

- [ ] **Step 1 — read `Pages/Api/SelectMolecule.cshtml.cs` fully** and mirror its structure (auth attribute, POST-only, anti-forgery, optional rate-limit, returnUrl `Url.IsLocalUrl` check).

- [ ] **Step 2 — failing test** (if SelectMolecule has a test sibling, mirror it): POST with a member company → calls `IActiveCompanySelectorService.SelectCompanyAsync` and redirects; POST with a non-member company → selection returns false, redirect with an error/no cookie. If no unit test is feasible for the page, write a focused test on the handler via constructor injection of a mocked `IActiveCompanySelectorService`.

- [ ] **Step 3 — implement** the page model:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api;

[Authorize] // any authenticated user; membership is validated inside the selector
public class SelectMemberCompanyModel : PageModel
{
    private readonly IActiveCompanySelectorService _selector;
    public SelectMemberCompanyModel(IActiveCompanySelectorService selector) => _selector = selector;

    public IActionResult OnGet() => RedirectToPage("/Index"); // POST-only (anti-CSRF), mirror SelectMolecule

    public async Task<IActionResult> OnPostAsync(int companyId, string? returnUrl = null)
    {
        await _selector.SelectCompanyAsync(companyId); // returns false if not a member; cookie only set when valid
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToPage("/Index");
    }
}
```
The `.cshtml` is `@page` + `@model ShiftManager.Pages.Api.SelectMemberCompanyModel` only (no markup), mirroring SelectMolecule.cshtml. Ensure anti-forgery matches the sibling (Razor Pages validate antiforgery on POST by default).

- [ ] **Step 4 — build, test, commit** the new page + test. Message: `feat(membership): POST /Api/SelectMemberCompany endpoint`.

---

### Task 5: `ContextSwitcher` regular-user multi-membership branch

**Files:**
- Modify: `ViewComponents/ContextSwitcherViewComponent.cs` (the `else` regular-user branch, ~lines 115-140)
- Modify: `Pages/Shared/Components/ContextSwitcher/Default.cshtml` (JS `selectOption` ~293-331 + `data-switch-mode`)
- Inject: `ICompanyMembershipService` into the view component

- [ ] **Step 1 — view component.** Replace the regular-user single-company branch so that when the user has >1 active membership, it lists each membership company as a company-mode option (reuse `CreateContextOption`, grouping by molecule name where available). Keep the single-context non-interactive display when the user has exactly one membership (no behavior change for ~99% of users). Set a new model flag (e.g. `IsMemberMode = true`) distinct from `IsMoleculeMode`. Resolve the current selection via `_tenantResolver.GetCurrentTenantId()`.

- [ ] **Step 2 — view + JS.** Add a `data-switch-mode="member-company"` case: in `selectOption`, when mode is `member-company`, set `form.action = '/Api/SelectMemberCompany'` and `input.name = 'companyId'` (mirror the existing company branch). Render the `data-switch-mode` from the new `IsMemberMode` flag.

- [ ] **Step 3 — manual + build verification.** Build; run the focused view-component test if one exists (otherwise rely on build + the full suite). Confirm a single-company user still renders the non-interactive single context (no regression).

- [ ] **Step 4 — commit** the view component + view. Message: `feat(membership): ContextSwitcher lists member companies for multi-company users`.

---

### Task 6: Full-suite verification

- [ ] **Step 1 — app not running.** Confirm before building.
- [ ] **Step 2 — `dotnet build`** → 0 errors.
- [ ] **Step 3 — full suite SEQUENTIAL:** `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -- xUnit.ParallelizeTestCollections=false` → all pass (expect prior count + new tests). Note: a peer session may be committing concurrently; if new unrelated failures appear, re-pull/rebuild and confirm they aren't from this epic.
- [ ] **Step 4 — final commit** if anything is uncommitted.

---

## Self-Review

- **Spec §5 coverage:** membership-validated selector (Task 2), TenantResolver rung (Task 3), endpoint (Task 4), ContextSwitcher regular-user branch (Task 5), the sync-validation claim (Task 1). The "single active company at a time" model is enforced because the resolver returns exactly one company and writes are CompanyId-stamped by the interceptor against it.
- **Security:** the switch cookie is never trusted alone — authoritative `IsMemberAsync` gate at selection time + claim re-validation every resolve. Single-company users are unaffected (no claim, no cookie, no switcher).
- **Deferred (disclosed):** stale `MoleculeId/AreaId/ProjectId` claim readers under an active switch (e.g. `SaveQuickInfoConfig` IDOR, `OnCallWidgetViewComponent`) — handled in the dedicated hierarchy-staleness pass (Epic 7 QA). The active *tenant* (CompanyId) is correct everywhere in this epic.
- **Placeholders:** none.
