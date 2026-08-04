# `/Calendar/Chores` — hand audit (the dimension that failed four times)

**Date:** 2026-08-03. **Method:** main-loop hand audit, after four agent attempts died (session
token limits ×3, network outage ×1). Read: `Pages/Api/Calendar/QuickAddChore.cshtml.cs`,
`Services/ChoreService.cs` (`CanUserManageChoresAsync`, `CanUserManageChoreForAssigneeAsync`,
`CreateChoreAsync`), `Pages/Calendar/Chores.cshtml.cs` (attribute + handler surface).

**Scope bound — this is PARTIAL.** Covered: the assign/create path, its authorization, and its scope
handling. **NOT covered:** the duration-weighted fairness algorithm's maths, the draft-chore path
(`OnPostEnterChoreDraft/Stage/Clear/Commit/Discard`), the `DeleteChore` / `RestoreChore` /
`GetEligibleUsersForChore` / `GetChoreEligibilityForCandidate` endpoints, the `chores-{moleculeId}`
SignalR group, the chore↔time-off interaction, and all RTL/contrast concerns.

---

## VERIFIED HEALTHY — the chore assign path authorizes correctly

This is worth stating plainly because the equivalent **shift** path does not.

`Pages/Api/Calendar/QuickAddChore.cshtml.cs`:
- `[Authorize(Policy = "Grant:AssignChores")]` on the page (`:16`);
- thorough input validation before any work: assignee id > 0, title required, title ≤ 200, notes
  ≤ 1000, date parseable, date not in the past, date not more than 2 years out;
- `CanUserManageChoresAsync(currentUserId)` — the caller's capability;
- **`CanUserManageChoreForAssigneeAsync(currentUserId, data.AssigneeId)`** — the target check, with a
  `SECURITY:` log line on failure.

And that method is a real check, not a name that promises one:

```csharp
public async Task<bool> CanUserManageChoreForAssigneeAsync(int managerId, int assigneeId)
{
    var assignee = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == assigneeId);
    if (assignee == null) return false;
    return await _grantService.HasGrantForCompanyAsync(managerId, "AssignChores", assignee.CompanyId);
}
```

The grant is evaluated against the **assignee's** company — the target, not the caller. This is the
correct pattern, and it is exactly what `Pages/Calendar/Table.cshtml.cs:811` omits for shifts
(where `_companyContext.GetCompanyIdOrThrow()` supplies the CALLER's company instead).

`ChoreService.CreateChoreAsync` re-checks the same gate internally (defense in depth) and wraps its
writes in a real transaction with `CommitAsync()`.

**Conclusion: chores are NOT affected by the caller-vs-target authorization defect that affects
shifts and on-duty.** Recording this so the area is not re-audited blind.

---

## C1-CH01 (MEDIUM-HIGH) — client-supplied `MoleculeId` and `ChoreTypeId` are never scope-validated

`Services/ChoreService.CreateChoreAsync`:

```csharp
// Resolve MoleculeId from the assignee's company if not explicitly provided.
var effectiveMoleculeId = moleculeId;                 // <-- client-supplied, used VERBATIM
if (!effectiveMoleculeId.HasValue)
{
    var company = await _companyCacheService.GetCompanyAsync(assignee.CompanyId);
    effectiveMoleculeId = company?.MoleculeId;         // safe fallback ONLY when absent
}
...
// ChoreType is molecule-scoped (not tenant-filtered); load by explicit id.
typeDefaultWeight = await _db.ChoreTypes.IgnoreQueryFilters()
    .Where(ct => ct.Id == choreTypeId.Value)           // <-- no molecule predicate
    .Select(ct => ct.DefaultWeightMinutes)
    .FirstOrDefaultAsync();
...
chore = new Chore {
    CompanyId = assignee.CompanyId,
    MoleculeId = effectiveMoleculeId,
    ChoreTypeId = choreTypeId,
    WeightMinutes = weightMinutes,
    ...
};
```

Both `moleculeId` and `choreTypeId` originate in the client JSON body
(`QuickAddChore`'s `CreateChoreRequest.MoleculeId` / `.ChoreTypeId`). Grepping `CreateChoreAsync` for
any molecule scope validation (`AccessibleMolecule`, `HasGrantWithScope`, `MoleculeId ==`) returns
**zero matches**. The safe company-derived fallback runs *only* when the client omits the value —
supplying one bypasses it.

### Failure scenario

A lead who legitimately holds `AssignChores` for the assignee's company POSTs
`/Api/Calendar/QuickAddChore` with a valid `assigneeId`, but sets `moleculeId` to a **different**
molecule (optionally with a `choreTypeId` belonging to that molecule). The assignee gate passes — the
assignee genuinely is in their company — and the chore is written with a foreign `MoleculeId`.

Consequences:
1. **Cross-molecule injection** — the chore renders on another molecule's chore calendar and is
   broadcast to `chores-{moleculeId}`.
2. **Conflict detection is evaded** — busy checking is built as
   `new BusyTarget.Chore(date, effectiveMoleculeId.Value, choreTypeId)`, so double-booking is
   evaluated against the wrong molecule and the assignee can be silently double-booked in their real one.
3. **Fairness accounting is corrupted in both molecules** — duration-weighted fairness is
   molecule-scoped, and `WeightMinutes` derives from the foreign chore type's `DefaultWeightMinutes`,
   making the fairness weight itself attacker-chosen.

### Classification

This is **not** privilege escalation — the caller must already hold `AssignChores` for that assignee.
It is cross-molecule data injection, fairness manipulation, and conflict-detection evasion.

Note that the comment *"ChoreType is molecule-scoped (not tenant-filtered)"* states the invariant on
the very line that fails to enforce it — the audit's recurring meta-pattern (a claim of protection
standing in for the protection).

### Proposed fix

Derive the molecule from the assignee's company in **all** cases, and treat a mismatched client-supplied
`moleculeId` as a 400 rather than trusting it. Scope the `ChoreTypes` lookup by `effectiveMoleculeId`
so a foreign chore type cannot be referenced. If a caller legitimately needs to assign across
molecules, validate `moleculeId` against their accessible-molecule set explicitly.

---

## Design observation — `Chore.UserId` is non-nullable

`Chore.UserId` is a non-nullable `int`, unlike `ShiftAssignment.UserId` (`int?`, where NULL means an
unfilled capacity slot). A chore therefore **cannot represent an unstaffed slot**. The practical
consequence for the real user: there is no way to express "this chore exists and nobody is on it
yet", so a commander cannot see chore gaps the way they see empty shift cells, and cannot plan chore
capacity ahead of assignment. Whether that is acceptable is a product decision, but it is a genuine
asymmetry with the shift spine that "parity" did not deliver. **Flagged for the user, not asserted as
a defect.**
