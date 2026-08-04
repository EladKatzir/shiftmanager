# Fixes delivered 2026-08-04 — API key ownership + `/Calendar/Team` desk chooser

Both fixes were requested as the only criticals worth acting on, given an air-gapped deployment where
**under-access is a worse failure than over-access**. Both are root-cause fixes with real-SQLite
regression tests, a green full suite, and a runtime click-through.

**Full serialized suite: 1993/1993** (was 1985; +6 API-key ownership tests, +2 Team tests).

---

## CORRECTION to finding B-F01 — I overstated the severity

My audit reported that a non-owner could refresh a colleague's key and that **"the page renders the
new plaintext with a copy-to-clipboard button."** The runtime probe disproves the second half.

`Pages/My/ApiKeys.cshtml.cs` `OnPostRefreshAsync` does:

```csharp
GeneratedApiKey = newApiKey;
TempData["SuccessMessage"] = ...;
return RedirectToPage();     // <-- a redirect rebuilds the PageModel; GeneratedApiKey is lost
```

`GeneratedApiKey` is a plain page-model property, not TempData, so it does **not** survive the
redirect. The banner at `Pages/My/ApiKeys.cshtml:31`
(`@if (!string.IsNullOrEmpty(Model.GeneratedApiKey))`) never renders from this handler. Verified at
runtime: after a refresh, `#copyApiKeyBtn` is absent and `banner_key_value` is null — for the
attacker **and** for the legitimate owner.

### What was actually true (confirmed, and now fixed)
- **Unauthorized rotation — denial of service.** Any authenticated user in the company could rotate
  any colleague's key, instantly breaking that integration. `/My/ApiKeys` lists **every** key in the
  company (`ListAllKeysAsync(companyId)`), so the ids are handed to the caller by the UI.
- **Service-level credential disclosure.** `RegenerateApiKeyAsync` *returned* the new plaintext to an
  unauthorized caller. The page happens not to render it, but the service contract was wrong and any
  other consumer of that method would have received another user's secret.

### What was NOT true
- The plaintext never reached the attacker's screen through this handler. **"Credential theft via the
  UI" was my error**, caused by reading the render path without checking that the redirect discards
  the value first.

Severity is therefore **high (denial of service + a broken service contract)** rather than critical
credential theft. The fix is unchanged and still worth having.

---

## FIX 1 — API key ownership (`ApiKeyService`)

### The ownership trap
The obvious predicate — `k.CreatedBy == callerId` — would have been **actively harmful**.
`ApiKeyService` sets `CreatedBy = reviewerId, // Reviewer creates it on behalf of requester`, so
`CreatedBy` is the **approving admin**, not the owner. Keying ownership on it would have locked the
real owner out of their own key and left only the approver able to rotate it.

The true owner is the user whose approved request generated the key:
`ApiKeyRequest.GeneratedApiKeyId == keyId` → `ApiKeyRequest.RequestedBy`. That is what
`IsKeyOwnerAsync` uses. Every `ApiKey` is created from an approved request (single construction site),
so a key with no matching request is an orphan and is treated as owned by nobody — fail closed.

### Shape
- `RegenerateApiKeyAsync(keyId, regeneratedBy, bool callerIsAdmin = false)`
- `RevokeApiKeyAsync(keyId, revokedBy, reason = null, bool callerIsAdmin = false)`
- Default `false` = fail closed. The admin revoke handler passes `callerIsAdmin: true` (it has already
  run `CheckIsAdminAsync`), so administrative revocation keeps working.
- `RevokeApiKeyAsync` had the **same** company-only predicate and is fixed alongside.

### Verification
6 new tests in `ShiftManager.Tests/UnitTests/Services/ApiKeyOwnershipTests.cs`, **RED verified before
GREEN** — exactly the 2 attacker tests failed while the 4 availability tests already passed, which is
what proved the fix could not lock anyone out.

Runtime click-through (seeded key owned by user 118, `CreatedBy` deliberately set to a non-owner):

| | attacker (`dir.br@test`, non-owner) | owner (`lead.alhut.hir@test`) |
|---|---|---|
| Key row visible in list | yes (pre-existing enumeration) | yes |
| Stored plaintext visible in list | **no** (gated on `AdminAccess`) | no |
| Refresh rotated the key | **NO** — hash unchanged | **YES** — hash changed |
| Plaintext returned to screen | no | no (see the pre-existing bug below) |

6 existing `ApiKeyServiceTests` call sites were updated to pass `callerIsAdmin: true`; they exercise
revoke/regenerate **mechanics** as the reviewer, and authorization is now owned by the new file.

---

## NEW FINDING (pre-existing, fits the "under-access" priority) — a regenerated key is never shown to anyone

Both `OnPostRefreshAsync` and `OnPostApproveAsync` assign `GeneratedApiKey` and then
`return RedirectToPage()`, which discards it. Consequences:

- A user who regenerates their key **never sees the new value**. The rotation succeeds and the secret
  is lost to them.
- After approval, the admin never sees the generated key either.
- The stored `PlainTextKey` is rendered in the list **only** `@if (Model.IsOwner …)`, and
  `IsOwner == HasGrantAsync(userId, "AdminAccess")`. So a **non-admin who legitimately requested a key
  has no way to retrieve it at all** — not on generation, not on refresh, not from the list.

That makes the feature unusable for exactly the people it exists for. **Not fixed** — it is outside
the two items authorised, and the sensible remedy (carry the value in `TempData` for one render, or
return `Page()` instead of redirecting) is a deliberate product decision about how long a secret
should sit in a cookie.

---

## FIX 2 — `/Calendar/Team` desk chooser

### The defect
`Team.cshtml` offered a bare name box; `team.js` read `#companySelect` / `#jobTypeSelect` straight off
the `cal-toolbar__row`. There was **no selection model** — a saved table silently inherited whatever
was on screen, so a lead could not create a table for any other desk.

### The change
- The add button opens a dialog (`.modal--fullscreen-wrapper` + `.is-open`, the same pattern as
  Overview's note modal — deliberately not the bare `.modal` box or `[hidden]`) with **Desk +
  Job type + Name**, defaulting to the current toolbar selection.
- Job types are **molecule-scoped**, so `TeamModel.JobTypesByMolecule` is serialised into the page and
  changing the desk repopulates the job-type list without a round trip. It contains only molecules the
  caller can already reach.
- **Server-side:** `OnPostAddViewAsync` now validates that the chosen job type belongs to the chosen
  desk's molecule. It previously validated the company but accepted **any** job-type id — a mismatched
  pair would have saved a view that renders an empty roster forever.
- All four dialog functions are exported on `window`; `team.js` is inside an IIFE, so an inline
  `onclick` calling an unexported function would silently do nothing.
- 6 new resx keys added to **both** `SharedResources.resx` and `SharedResources.he-IL.resx`, with
  parity asserted at insertion time.

### A "fix" I made and reverted
I also changed the duplicate-name pre-check to scope by `TargetCompanyId`, believing it was stricter
than the DB. **It was not, and the change was a regression.** `DeskTeamView` has two company columns:
`CompanyId` (the OWNER'S tenant, set from the tenant resolver) and `TargetCompanyId` (the desk shown).
The unique index is `(CompanyId, OwnerId, Name)` — the *tenant* — so the original check matched the
constraint exactly. My version made the pre-check looser, turning a clean 400 into a
`DbUpdateException` round-trip. A failing test caught it; reverted, with a comment recording why.

### Verification
2 new tests in `TeamPageTests`: a job type from another molecule is rejected; and a table saved for a
desk **other than the one on screen** persists with `TargetCompanyId` = the chosen desk.

One existing test (`AddView_WithDuplicateName_ReturnsCleanError`) was updated: it never stubbed the
job-type mock, so after the new validation it would have returned 400 for the **wrong reason** and
silently stopped testing duplicate names.

Runtime click-through: the dialog opened with **10 desks** and 4 job types; selecting `מחנות` (id 3)
while the toolbar showed `חיר` (id 2) saved a view with `TargetCompanyId = 3`.

---

## Note on reaching your running app

Both fixes are in source and verified against a **Release** build on `:5080`. Your dev app on `:5000`
runs from `bin/Debug` and will not show either fix until it is rebuilt — including the earlier
`ViewAllShifts` scope fix that widens a lead's desk picker from 1 to 10. The Team dialog is far less
useful without it, since the picker would offer a single desk.
