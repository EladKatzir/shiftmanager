# Hand audit part 3 — `/Api/Hierarchy/*` has no target authorization on any endpoint

**Date:** 2026-08-04 · main-loop verification (no subagents) · **Severity: CRITICAL**

An agent pass flagged this surface; I confirmed it end to end and established the blast radius from
the seeded database.

## The finding

All four hierarchy-mutation endpoints gate ONLY at page level and perform **zero** authorization
against the entity they are about to mutate:

| Endpoint | Policy | `HasGrant`/`Accessible`/`scope` references in the file |
|---|---|---|
| `Pages/Api/Hierarchy/Delete.cshtml.cs` | `Grant:ManageHierarchy` | **0** |
| `Pages/Api/Hierarchy/Rename.cshtml.cs` | `Grant:ManageHierarchy` | **0** |
| `Pages/Api/Hierarchy/Create.cshtml.cs` | `Grant:ManageHierarchy` | **0** |
| `Pages/Api/Hierarchy/Move.cshtml.cs`   | `Grant:ReorderHierarchy` | **0** |

All are additionally `[IgnoreAntiforgeryToken]`.

Holding `Grant:ManageHierarchy` proves the caller may manage *some* hierarchy — it says nothing about
*which* project, area, molecule, company or department. Nothing downstream supplies that check.

## Delete, traced in full

`Pages/Api/Hierarchy/Delete.cshtml.cs` `OnPostAsync`:

1. Deserialize `{ entityType, entityId }` from the body; reject only if empty or `entityId <= 0`.
2. `int.TryParse` the caller's id — then **use it solely for logging** (`:174`
   `_logger.LogInformation("Hierarchy delete: {Result}, UserId={UserId}", ...)` and `:186` the audit
   record's `UserId = userId`). It is never passed to any authorization call.
3. `switch (request.EntityType.ToLower())` — each branch loads the target by **bare id with the tenant
   filter dropped**, e.g.
   ```csharp
   var company = await _db.Companies.IgnoreQueryFilters()
       .FirstOrDefaultAsync(c => c.Id == request.EntityId);
   ```
4. The only guards are **data-integrity child checks** ("Cannot delete project with areas",
   "Cannot delete company with users") — not authorization.

### Companies are HARD deleted

Projects, areas and molecules are soft-deactivated (`IsActive = false`). Companies are not:

```csharp
// Clean up DirectorCompany mappings before removing the company
var directorMappings = await _db.DirectorCompanies.Where(dc => dc.CompanyId == request.EntityId).ToListAsync();
if (directorMappings.Any()) _db.DirectorCompanies.RemoveRange(directorMappings);
// For companies, we actually remove since they don't have an IsActive flag
_db.Companies.Remove(company);
```

The row is **permanently destroyed**, together with its `DirectorCompany` mappings. The sole
precondition is that the company has no **active** users — which an empty or decommissioned desk, or
one whose users were just deactivated, satisfies.

## Blast radius (queried from the seeded DB)

`ManageHierarchy` is held by **6 users: 3 molecule-scoped, 3 project-scoped.** (`ReorderHierarchy`
gates Move separately.)

The three **molecule-scoped** holders — MoleculeAdmins, a mid-level seat — can therefore rename,
re-parent, or permanently delete any project, area, molecule, company or department **anywhere in the
deployment**, including entities in other areas and other projects. Their molecule scope is recorded
in the grant and never consulted.

## Failure scenario

A MoleculeAdmin of molecule *Tzafona* POSTs to `/Api/Hierarchy/Delete`:

```json
{"entityType":"company","entityId":<id of a decommissioned desk in a different area>}
```

The policy passes (they hold `ManageHierarchy`). The company row is loaded with the tenant filter
dropped, the active-user check passes because the desk is empty, and the row is **deleted from the
database**. The audit log faithfully records who did it — after the fact.

`Move` is comparably damaging without deleting anything: re-parenting a molecule into a different area
silently re-scopes every grant that cascades through that path
(`GetAccessibleCompanyIdsForGrantAsync` resolves Project→Area→Molecule→Company), changing who can see
and assign whom across two hierarchies at once.

## Why review missed it

`Delete.cshtml.cs:18` carries:

> `// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageHierarchy policy;`

This is the audit's recurring meta-pattern: a comment that justifies dropping tenant isolation by
citing a policy that does not carry target scope. The comment is literally true and provides no
protection, and it terminates review at exactly the point where review was needed.

## Proposed fix

Before mutating, resolve the target entity's position in the hierarchy and verify it falls within the
caller's grant scope — `GetAccessibleCompanyIdsForGrantAsync` / `GetAccessibleMoleculeIdsForGrantAsync`
already exist for this. Apply to all four endpoints. For `Move`, validate **both** the source and the
destination parent. Given the irreversibility, consider soft-deleting companies (add `IsActive`) so a
mistaken or malicious delete is recoverable, and require a stronger grant for hard deletion.
