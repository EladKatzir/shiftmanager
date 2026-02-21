# FINDING-009: Grant Recalculation Not Triggered on Role Change

| Field | Value |
|-------|-------|
| **ID** | FINDING-009 |
| **Date** | 2026-02-18 |
| **Category** | Authorization / Grants |
| **Severity** | **HIGH** |

## Expected Behavior

When an admin changes a user's role via `OnPostRoleAsync()`, the user's grants should be recalculated immediately — old role grants removed, new role grants assigned.

## Actual Behavior

`Admin/Users.cshtml.cs` `OnPostRoleAsync()` (lines 773-979):
- Updates `user.Role` enum value and `user.RoleTemplateId`
- Handles trainee → non-trainee transitions (removes trainee assignments)
- Creates/removes DirectorCompany mappings
- Does NOT touch the `Grants` table at all — no removal, no reassignment

**The login-time backfill does NOT fix this.** `Login.cshtml.cs` line 287:
```csharp
var hasAnyGrants = await _db.Grants.AnyAsync(g => g.UserId == user.Id);
if (!hasAnyGrants) { /* backfill */ }
```
The backfill only fires when the user has **zero grants**. A demoted Manager already has grant rows, so `!hasAnyGrants` is `false` and the backfill is skipped entirely.

**Result: A user demoted from Manager to Employee retains ALL Manager grants permanently** — not just until next login, but indefinitely, because no code path ever removes them.

## Evidence

**Admin/Users.cshtml.cs** `OnPostRoleAsync()`:
- Zero calls to `_grantService.AssignRoleTemplateGrantsAsync()`, `RemoveAutoGrantsAsync()`, or any grant-related method
- `AssignRoleTemplateGrantsAsync` is only called in: `OnPostAddAsync` (line 652), `OnPostApproveJoinRequestAsync` (line 1452), and batch join approval (line 1770)

**Login.cshtml.cs** (lines 276-291):
- `ApplyAutoGrantsAsync` gated by `!hasAnyGrants` — only fires for users with zero grants
- Does not remove stale grants, does not reconcile

**Grant authorization is live from DB:**
- `_grantService.HasGrantAsync()` reads the `Grants` table on every request
- Cookie only carries role claim, not grants — cookie expiry is irrelevant
- 7-day sliding cookie (`ExpireTimeSpan = TimeSpan.FromDays(7)`, `SlidingExpiration = true`)

## Root Cause

`OnPostRoleAsync` was built to update the user record and audit the change, but the grant lifecycle was never wired in. The login-time backfill was designed as a bootstrap for new users (zero grants), not as a reconciliation mechanism for role changes.

## Fix Recommendation

After role change in `OnPostRoleAsync`, add grant reconciliation:

```csharp
// 1. Remove old auto-grants from previous template
await _grantService.RemoveAutoGrantsAsync(user.Id);

// 2. Assign new auto-grants from new template
var grantScope = await BuildGrantScopeForTemplateAsync(
    selectedTemplate.Key, user.CompanyId, user.JobTypeId);
await _grantService.AssignRoleTemplateGrantsAsync(
    user.Id, selectedTemplate.Key, grantScope, currentUserId);
```

## Verification Plan

1. User A is Manager with Manager grants (verify grant rows exist)
2. Admin demotes User A to Employee
3. Query `Grants` table for User A
4. Before fix: Manager grant rows still present
5. After fix: Only Employee grant rows present

## Confidence

**98%** — Verified: no grant code in `OnPostRoleAsync`, login backfill gated by `!hasAnyGrants`, grants checked live from DB. This is a **permanent privilege escalation** on demotion.
