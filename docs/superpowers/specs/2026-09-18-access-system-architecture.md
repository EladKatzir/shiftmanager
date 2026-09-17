# The Access System: how roles and grants actually work

**Written:** 2026-09-18 · **Branch:** `dev` · **Method:** code tracing + queries against the dev database.

This is the map requested as part of the access work: how permissions are wired, which pages change
them, and where the system lies to you. Everything here was read in the code or measured in the data;
where something is unverified it says so.

---

## 1. The five moving parts

```
GrantType  ──<  RoleTemplateGrant  >──  RoleTemplate
(catalog)        (what a role gives)     (a role)
                        │
                        │  materialised per user, at a scope
                        ▼
                     Grant  ────────────  AppUser.RoleTemplateId
              (a real permission row)      (which role a user has)
```

**1. `GrantType` — the catalog.** 138 rows, seeded in `Data/SeedData/GrantTypeSeed.cs`. IDs are
assigned by a running `id++`, and `RoleTemplateSeed.cs` refers to them **by number**, so a new type
must be appended, never inserted. `DefaultScope` is advisory and enforced nowhere.

**2. `RoleTemplate` — a role.** 10 system roles. Identified by `Key` (not a name). `SortOrder` is the
seniority ladder, lower being more senior:

| Owner | AreaAdmin (קב"ב) | MoleculeAdmin (מפק"מ) | Director (מ"מ) | BRDirector (קב"ר) | Lead (מפ"צ) | DepartmentLead | Assigner | Employee | Trainee |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 2 | 3 | 5 | 10 | 20 | 25 | 30 | 100 | 101 |

**3. `RoleTemplateGrant` — what a role gives.** One row per (role, grant type, job type). It carries
the **`ScopeMode`**, which is the key idea: it says how wide the permission should be *relative to the
user*, not an absolute place.

| ScopeMode | Meaning |
|---|---|
| `SameAsRole` (0) | the user's own company (+ department) |
| `ExpandToMolecule` (1) | the user's whole molecule |
| `ExpandToArea` (2) | the user's whole area |
| `ExpandToProject` (4) | everything |
| `Custom` (3) | exactly the scope supplied |

**4. `Grant` — a real permission row on a user.** Six nullable scope columns (Project, Area, Molecule,
Department, Company, JobType). Normally exactly one is set; `SameAsRole` sets two. **All six null means
"self"** — and that is the silent sink: any mis-resolved expansion lands here, and no database
constraint prevents it.

**5. `AppUser.RoleTemplateId` — the user's role.** This is the source of truth.
`UserRoleAssignment` is a second, scoped table that **nothing reads for authorization** (confirmed by
sweeping every usage: display, export, a startup migration, and deletes). Treat it as a trap.

### How a template becomes real permissions

`ScopeMode` × the user's hierarchy path → concrete scope columns, in
`GrantService.DetermineEffectiveScope`. Three callers build that hierarchy path, and **they do not
build it the same way** — see defect 6.

| Path | Entry point |
|---|---|
| New user created | `AssignRoleTemplateGrantsAsync` ← `BuildRoleTemplateScopeAsync` (partial) |
| Role changed | same |
| Startup repair | `RepairUserGrantsAsync` ← `BuildGrantScopeForUserAsync` (full) |
| Back-fill button | `ApplyAutoGrantsAsync` ← the back-fill's own builder (full) |

Note the repair path derives the template from **`user.Role` + job-type name**
(`Helpers/RoleTemplateMapper.cs`), not from `RoleTemplateId`. A Kabar whose job type is Alhut or Text
is repaired as a **Lead**.

### How a permission is checked

- `[Authorize(Policy = "Grant:X")]` → `GrantPolicyProvider` invents the policy on demand (no
  registration needed, but the key must exist in the seed) → `GrantAuthorizationHandler` evaluates it
  against the **caller's own login claims**.
- `HasGrantAsync(user, key)` — **scope-blind**: any row counts, including an all-null one.
- `HasGrantWithScopeAsync(...)` — the real cascade: self → project → area → molecule → department →
  company → job type.
- `GetAccessibleCompanyIdsForGrantAsync(...)` — expands a grant into the set of companies it reaches.
  This is what page lists should be built from.

### Rolling a change out to existing users

Changing the seed does not by itself change anybody's permissions.

- **New mapping added** → startup re-provisions Lead, Kabar and MoleculeAdmin users (`Program.cs`,
  guarded by `hasNewGrantMappings`). Add-only; it never deletes or narrows.
- **Scope of an existing mapping changed** → the **ROLE GRANT SCOPE MIGRATION** block in `Program.cs`.
  It deletes rows matching the old shape and re-runs repair. This is where the Lead/Kabar
  `EditCompanyUsers` widening was registered.
- **Manually** → Owner Hub ▸ Grants ▸ "Back-fill Template AutoGrants".
- System templates are **code-owned**: a startup pass resets `ScopeMode`, `CanOwn`, `CanGive` and
  `UseOwnJobType` on seeded rows every boot, so UI edits to them do not survive a restart. The
  `IsOverride` flag that exists to protect admin edits is not consulted. No row in the dev database has
  it set, so this changes nothing today — but it is why editing a system role in the UI "didn't work".

---

## 2. The pages that change access

| Page | Gate | What it does |
|---|---|---|
| `/Admin/Organization/Roles` | `AssignRoles` | Read-only list of roles + assignments; revoke |
| `/Admin/Organization/Roles/Assign` | `AssignRoles` | Give a user a role at a scope |
| `/Admin/Organization/Grants` | `ViewGrants` | All grants; revoke |
| `/Admin/Organization/Grants/Assign` | `AssignGrants` | Give a user one permission at a scope |
| `/Owner/Hub/Grants` | `AdminAccess` | **1,905 lines.** Catalog, template editor, per-user grants, back-fill, surplus audit. **Not in the menu** |
| `/Owner/Hub/RoleTemplates` (+ Create/Edit) | `AdminAccess` | The other template editor |
| `/Owner/Hub/PermissionSimulator` | `AdminAccess` | "Why can/can't X do Y" |
| `/Owner/Permissions` | `AdminAccess` | Read-only stats. No handlers. Orphan |
| `/Admin/Directors` | `AssignRoles` | Director assignments |
| `/Owner/LockedUsers` | `AdminAccess` | Account unlock (security, not permissions) |
| `/Admin/Users` | `ManagerHomeAccess` | Not an access page, but it creates users, changes roles and provisions grants |

**Seven duplications.** Role-template grants are editable in two places *that write different job-type
scoping*; assigning a grant exists twice *under different gates*; revoking twice; the template list
three times (two carry hand-written cross-links apologising for it); grant statistics three times;
account unlock twice.

---

## 3. Why the manual configuration failed

The attempt: Owner Hub ▸ Grants ▸ Role Templates → give Lead/Kabar `CreateUsers` expanded to molecule
→ Back-fill. Seven independent defects sat in that path.

| # | Defect | Status |
|---|---|---|
| 1 | **`CreateUsers` (id 30) is never checked anywhere.** Not one `[Authorize]`, not one `HasGrantAsync`. Its resx keys don't exist either. Configuring it cannot do anything. The live gate is `EditCompanyUsers` (118). | **Superseded** — `EditCompanyUsers` is now molecule-wide for Lead/Kabar. `CreateUsers` is still dead; see §5 |
| 2 | **The add-user company dropdown read a different grant than the gate** — built from `ManageJoinRequests` scope, authorised by `EditCompanyUsers`. A Lead saw one desk however the grant was scoped. | **Fixed** |
| 3 | **Startup reverts UI edits to system templates**, ignoring `IsOverride`. | **Documented**, unchanged (0 rows affected today) |
| 4 | **The dry run compared grant types only, never scope** → a scope change reported "0 users missing". Execute then netted removals against insertions and also reported 0. | **Fixed** |
| 5 | **`DetermineEffectiveScope` has no null guard** → an expansion with a missing level writes an all-null (self) row. 136 such rows exist. | **Documented**, unchanged — see §5 |
| 6 | **Create/role-change writes narrower scope than repair/back-fill** (`BuildRoleTemplateScopeAsync` omits Area/Project for most roles and everything for custom roles), so a good back-fill is undone by the next role change. | **Documented**, unchanged |
| 7 | **The scope dropdown omitted `ExpandToProject`**, silently downgrading a project-scoped row to `SameAsRole` on save. | **Fixed** |

---

## 4. Security findings and what was done

**Fixed**

- **Anyone with `AssignRoles` could make anyone an Owner.** `Roles/Assign` validated only that a scope
  field was filled in; Owner's scope level matched no case and fell through to "no scope", producing
  ~130 unscoped Owner grants, which the scope-blind `HasGrantAsync` accepts. Now bounded by
  `RoleRankGuard`: you may assign your own role or a less senior one, never a more senior one.
- **Anyone with `AssignGrants` could grant themselves anything**, including `AdminAccess`: the page
  wrote to `_db.Grants` directly, bypassing `CanUserGrantAsync`. Now you can only give a permission you
  hold yourself (`AdminAccess` unrestricted). No manual grant in the dev database would have been
  blocked by this rule.
- **No rank check on user management.** Reset-password, deactivate, delete and unlock only checked the
  company. With `EditCompanyUsers` now molecule-wide, a Lead could have reset their MoleculeAdmin's
  password and logged in as them. Same rank rule applied.

**Known, not fixed** (deliberately, to avoid removing access anyone might rely on)

- **The scope primitive leaks across molecules.** The last branch of `HasGrantWithScopeAsync` returns
  true on a job-type match whenever the grant has no company, *ignoring a molecule or area restriction
  that is present*. 268 rows / 44 users / 21 grant types have that shape.
  **Before fixing, run these on production** — all five are zero in dev, which is why the fix looked
  safe here:
  1. `{Molecule, JobType, no Company}` grants whose molecule is not the holder's home molecule.
  2. Grants with both `DepartmentId` and `JobTypeId` (only that branch matches them).
  3. `AssignShifts` / `ManageJoinRequests` rows with Molecule/Area **and** JobType.
  4. Shift types with no company and no molecule but a job type (they send only a job type).
  5. Companies with no molecule that have join or time-off requests.
- **57 seeded grants are enforced nowhere** and still render as real, assignable permissions. A
  permission audit of this system cannot be trusted until each is either wired or marked deprecated.
- `Grant:AdminAccess` policies accept the grant **at any scope**, so a company-scoped AdminAccess is
  effectively global.
- `/Admin/Directors` write handlers do no scope check; `/Owner/LockedUsers` can unlock across tenants.
- Deleting a user does not clean up their `CalendarDayNote` rows while `CreatedByUserId` is `Restrict`,
  so deleting an author likely fails. Not reproduced.

---

## 5. Things that are true and surprising

- **A grant row with no scope at all means "self"** — but only to the scoped checks. The scope-blind
  `HasGrantAsync` accepts it as a plain yes. 136 auto-grant rows are in this shape, including 3 users
  whose note-writing depends on one. Making the writer "fail closed" would delete them on the next
  role change or back-fill, which is why that defect is documented rather than fixed.
- **`CreateUsers` is still dead.** It is now superseded by `EditCompanyUsers` rather than wired. It
  should be marked `IsDeprecated` so the UI stops offering it, or wired if a create-only permission
  separate from edit is wanted — worth doing, since a Lead now has edit/delete/reset across the whole
  molecule when all that was asked for was "add users".
- **The Permission Simulator disagrees with reality.** It models the *fixed* cross-molecule behaviour,
  so for that case it already tells you the answer the code does not give.
- **The Owner Hub "AdminAccess bypass" does nothing.** `GrantAsync` re-runs the same check and throws,
  so that path already required CanGive + scope coverage.
- **`/Owner/Hub/Grants` ignores its own deep links.** `OnGetAsync` takes no parameters, so the
  simulator's "Open in Grants →" link lands on the default tab. The tab strip has no URL support at
  all, which is why `#roletemplates` in the address bar does nothing.
