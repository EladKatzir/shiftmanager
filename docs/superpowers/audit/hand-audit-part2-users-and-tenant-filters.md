# Hand audit part 2 — `/Admin/Users` privilege guard + the `IgnoreQueryFilters()` inspection list

**Date:** 2026-08-04 · main-loop verification (no subagents).

---

## H-F01 (HIGH) — no handler on `/Admin/Users` compares the TARGET's privilege level

A prior agent pass reported this page's password-reset / deactivate / delete handlers as having
"no scope check" and claimed sibling "Membership/Move" handlers enforce a guard they omit. **Both
halves of that framing are wrong; the underlying defect is real but narrower.** Verified by hand:

### REFUTED — the company-scope check IS present and correct

`Pages/Admin/Users.cshtml.cs` `OnPostResetPasswordAsync` does all of this before writing:

```csharp
var u = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
if (u != null)
{
    ...
    if (!int.TryParse(resetUserIdClaim, out var resetCurrentUserId)) { ...; return RedirectToPage(); }
    var isAdmin = await _grantService.HasGrantAsync(resetCurrentUserId, "AdminAccess");
    var hasResetGrant = isAdmin
        || await _grantService.HasGrantForCompanyAsync(resetCurrentUserId, "EditCompanyUsers", u.CompanyId);
    if (!hasResetGrant) { LogUnauthorizedUserActionWithGrant(...); return RedirectToPage(); }
```

`HasGrantForCompanyAsync(..., u.CompanyId)` authorizes against the **TARGET's** company — the correct
target-based pattern. It also uses `int.TryParse` on the claim (per the codebase contract),
`PasswordHasher.CreateHash` (PBKDF2, not raw HMAC), `SaveWithConcurrencyHandlingAsync`, and audit
logging. **Cross-company password reset is properly blocked.**

### UNSUPPORTED — the cited sibling handlers do not exist

Grepping for `OnPostMembership*` / `OnPostMove*` on this page returns nothing. There is no handler
that enforces the guard the finding claimed as precedent.

### CONFIRMED — but the defect is *privilege comparison*, not scope

Every privilege check on the page is of the form
`HasGrantAsync(currentUserId, "AdminAccess")` — **the CALLER's** privilege — at lines 224, 935, 1110,
1273, 1473, 1569, 1721, 1985, 2059, 2137, 2515, 2694, 2824, 3040. **Not one of them examines the
target's role, template, or grants.**

**Failure scenario.** An Owner or AreaAdmin account sits in company X (the seeded DB has exactly this
shape — privileged accounts share desks with ordinary staff). A Manager who holds `EditCompanyUsers`
for company X and no `AdminAccess` opens `/Admin/Users`, selects that privileged account, and:
- **resets its password** (`OnPostResetPasswordAsync`) — then logs in as an Owner. Full takeover.
- or **deactivates / deletes** it (`OnPostToggleAsync`, `OnPostDeleteUserAsync`) — denial of service
  against the most privileged account in the deployment.

Every one of those handlers passes its company-scope check, because the target genuinely IS in the
Manager's company. Company scoping is not the control that should stop this — privilege comparison is,
and it does not exist.

**Proposed fix.** Add a target-privilege guard shared by every mutating handler on this page: resolve
the target's effective privilege (role template / `AdminAccess` / Owner) and refuse when it is greater
than or equal to the caller's unless the caller holds `AdminAccess`. Apply it to reset-password,
toggle/deactivate, delete, role change, account-type change and job-type change alike.

---

## H-F02 (informational, but it is the fix-phase work list) — 115 tenant-filter drops inside mutating handlers

`IgnoreQueryFilters()` removes EF's tenant isolation; the codebase contract is that each call is
paired with an explicit predicate restoring scope. A mechanical sweep
(`scratchpad/ignorefilters_sweep.py`) found:

| | Count |
|---|---|
| `IgnoreQueryFilters()` call sites (Services, Pages, Data, Middleware, Authorization, ViewComponents) | **1413** |
| …with a scope predicate in the same LINQ chain | 331 |
| …with **no** scope predicate in the chain | 1082 |
| …of those, a bare `.Id ==` lookup (the classic IDOR shape) | 305 |
| …of those, **inside a mutating handler**, excluding seeders | **115** |

### This shape is NOT itself a defect — and that is the important part

`Pages/Requests/Index.cshtml.cs` accounts for **14** of the 115, and I verified by hand that its
approve handler is **correct**: it loads the entity unscoped by id and *then* calls
`ValidateAccessToRequestAsync(currentUser, r.CompanyId)`. "Load unscoped, then authorize against the
loaded entity's company" is a legitimate and arguably better pattern than relying on the query filter.

So the discriminator is **not** the presence of `IgnoreQueryFilters()` — it is whether a
**target-scoped** check follows the load. `Table.cshtml.cs` loads and then authorizes against the
**caller's** company; `Requests/Index.cshtml.cs` authorizes against the **target's**. Same shape,
opposite correctness.

### The list, ranked (use as the fix-phase work queue)

| Sites | File |
|---|---|
| 22 | `Pages/Calendar/Table.cshtml.cs` — **known bad** (authorizes against caller) |
| 14 | `Pages/Requests/Index.cshtml.cs` — **known good** (authorizes against target) |
| 11 | `Pages/Admin/Users.cshtml.cs` — company scope OK, **privilege comparison missing** (H-F01) |
| 8 | `Pages/Api/Hierarchy/Move.cshtml.cs` |
| 5 | `Pages/Api/Hierarchy/Delete.cshtml.cs` |
| 5 | `Pages/Api/Hierarchy/Rename.cshtml.cs` |
| 4 each | `Admin/Organization/Index`, `…/Departments/Index`, `…/Molecules/Index`, `Api/Hierarchy/Create` |
| 3 each | `Admin/Companies`, `Admin/Directors`, `Admin/HomeTypes/Index`, `…/Areas/Index`, `…/JobTypes/Index`, `…/Projects/Index`, `My/Requests` |
| 2 each | `Admin/EditProfile`, `…/DutyTypes/Index`, `…/ShiftGroupings/Index` |

The `Api/Hierarchy/*` cluster (22 sites across Move/Delete/Rename/Create) is the highest-priority
unverified group: an agent pass reported those endpoints mutate any hierarchy entity by id, including
hard-deleting companies, and this sweep independently shows every one of them loading by bare id with
the tenant filter dropped. **Not yet hand-verified — treat as strong-suspicion, not fact.**

### Method caveat

The sweep inspects only the LINQ chain up to the terminating `;`. A scope check performed *before* the
query (as `Pages/Calendar/Team.cshtml.cs` does, validating `SelectedCompanyId` against an accessible
set first) is invisible to it. The 1082 figure is therefore an upper bound on suspicion, **not** a
count of defects. Its value is the ranked 115-site shortlist above.
