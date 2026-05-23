# Design: Move a user between companies

- **Date:** 2026-05-23
- **Status:** Approved (design); pending implementation plan
- **Author:** Elad Katzir (with Claude / Opus 4.7)
- **Reviewed by:** expert architecture-review agent (findings incorporated; see §13)

## 1. Goal

Let admins relocate a user from one company to another, with authority scoped by tier:

- **Molecule admin** — move users between companies **within their molecule**.
- **Area admin** — between companies **within their area**.
- **Owner** — between any companies **within their project**.

The move performs a **clean transfer**: the user keeps their identity (account, profile,
credentials, friendships) but their *operational footprint* in the old company is severed so
they start fresh in the destination. Deleting users is already implemented and is confirmed in
scope-satisfied (see §3); this spec adds only the **move** capability.

## 2. Scope

**In scope**
- New "Move to company" action on the Users admin page.
- A dedicated, unit-testable transfer service that performs the clean transfer in one transaction.
- An impact-preview endpoint so the admin sees what will be cleared before committing.
- Bilingual UI strings + error messages (en-US + he-IL).
- SQLite-backed tests covering data effects and authorization.

**Out of scope / non-goals**
- Changing delete (`OnPostDeleteUserAsync`) or deactivate (`OnPostToggleAsync`) behavior.
- Any new grant type (the move reuses `EditCompanyUsers`).
- Migrating *historical* records (audit logs, completed shifts) to the new company — these stay
  in the old company as accurate history.
- Bulk / multi-user moves (single user per action).
- Cross-**project** moves for non-`AdminAccess` admins (inherently impossible — see §6).

## 3. Current state (verified)

- **Delete already exists and is correctly authorized for all three tiers.** `OnPostDeleteUserAsync`
  (`Pages/Admin/Users.cshtml.cs:1560-1745`) hard-deletes a user and cleans up ~50 related records in a
  transaction, reassigning non-nullable `CreatedBy`/`UpdatedBy` to the acting admin. It authorizes via
  `HasGrantForCompanyAsync(adminId, "EditCompanyUsers", user.CompanyId)` OR `AdminAccess` (`:1596`).
- **Deactivate** is `OnPostToggleAsync` (`:902`), soft-delete with minimal cleanup (`:935-957`).
- **Grants are already in place** for the three tiers (verified in `Data/SeedData/RoleTemplateSeed.cs`):
  - MoleculeAdmin (template 7): `EditCompanyUsers`(118) + `ManageJoinRequests`(116) at ExpandToMolecule (`:523-524`)
  - AreaAdmin (template 10): same two at ExpandToArea (`:716-717`)
  - Owner (template 11): same two at ExpandToProject (`:844-845`)
- **`HasGrantForCompanyAsync`** (`Services/IGrantService.cs:45`) delegates to
  `GetAccessibleCompanyIdsForGrantAsync` (`Services/GrantService.cs:322-400`), which cascades
  Project→Area→Molecule→Company. So the grant check naturally yields the tier matrix above.

## 4. Core problem

`CompanyId` is the tenant boundary. The app isolates tenants with `IBelongsToCompany` + EF Core
global query filters keyed on `_tenantResolver.GetCurrentTenantId()`. `CompanyIdInterceptor` sets
`CompanyId` only on **insert**, never on update. Therefore a naive `user.CompanyId = dest; SaveChanges()`:

1. **Silently orphans** every user-linked entity that also carries `CompanyId` + a query filter
   (shifts, vacations, chores, swaps, notifications, game scores, etc.) — the rows survive but become
   invisible to the moved user's new tenant context. `AppUser.cs` carries a standing warning about this.
2. **Leaves stale scalar FKs** on `AppUser` (`JobTypeId`/`DepartmentId`/`PrimaryShiftTypeId`/`HomeTypeId`)
   pointing at rows scoped to the old company's molecule/area.
3. **Breaks the avatar path** — stored at `wwwroot/avatars/{companyId}/{userId}.jpg`; the path embeds CompanyId.
4. **Leaves grants** scoped to the old company/molecule (and, for privileged roles, cross-tenant
   `DirectorCompany` rows that keep granting authority over the old company).

The move must handle each of these deliberately.

## 5. Governing rule (per-entity bucketing)

Every user-linked entity falls into exactly one of four buckets:

1. **Personal operational data tied to the old company → CLEAR** (delete/cancel). The user is leaving;
   this data belongs to the old company's live operations.
2. **Credentials / active obligations → REVOKE.** A departed user must not keep working credentials or
   named duties in the old company.
3. **Shared assets the old company still needs → KEEP, reassign only *active ownership*.** Don't destroy
   what the old team relies on; transfer the live owner role to the acting admin. Pure authorship
   (`CreatedBy`/`UpdatedBy`) stays as accurate history because the user row persists (FK stays valid).
4. **Historical records → LEAVE in the old company.** Completed shifts, audit trails, "who reviewed"
   fields. The move writes its own audit entry instead.

This replaces the simplistic "do what delete does." Delete reassigns authorship FKs because removing the
user row would violate non-nullable FKs; a move keeps the user row, so those FKs remain valid and rewriting
them would falsify history for no integrity gain.

## 6. Authorization

```
OnPostMoveUserAsync(userId, destCompanyId):
  reject if userId == actingAdminId                      // no self-move (would strip own access mid-request)
  reject if destCompanyId == user.CompanyId              // no-op
  load destCompany (IgnoreQueryFilters); reject if null or destCompany.IsHeadquarters
  isAdmin = HasGrantAsync(actingAdminId, "AdminAccess")
  require isAdmin OR HasGrantForCompanyAsync(actingAdminId, "EditCompanyUsers", user.CompanyId)   // source
  require isAdmin OR HasGrantForCompanyAsync(actingAdminId, "EditCompanyUsers", destCompanyId)    // destination
  require CanAssignRoleAsync(actingAdminId, user's RoleTemplate, destCompanyId)   // privilege gate
  // belt-and-suspenders: assert source.ProjectId == dest.ProjectId unless isAdmin
```

Why this is sufficient and safe:

- **Tier matrix falls out of the two-sided `EditCompanyUsers` check.** A molecule admin holds it only at
  molecule scope, so both checks pass only when source AND dest are in their molecule; area/owner analogously.
- **No cross-project move for non-admins.** An Owner's `EditCompanyUsers` is project-scoped, so the dest-side
  check fails for a company in another project. Only `AdminAccess` bypasses (intended super-admin power).
- **`CanAssignRoleAsync` closes the privilege-escalation hole** the review found: without it, a molecule
  admin could move an *Owner* between two companies in their molecule, and the grant re-application would
  silently downgrade that Owner. Because moving re-applies the role template in the destination (effectively
  assigning that role), the actor must be permitted to assign it. A molecule admin cannot assign Owner →
  cannot move an Owner.
- The destination dropdown is populated from `GetAccessibleCompanyIdsForGrantAsync(admin, "EditCompanyUsers")`
  (excluding HQ), so out-of-scope companies are never offered — the server checks are the authority.

## 7. Architecture

### 7.1 New service (isolated, testable)

`Services/IUserCompanyTransferService.cs` + `Services/UserCompanyTransferService.cs`, registered **scoped**
in `Program.cs`. Dependencies: `AppDbContext`, `IGrantService`, `IConcurrencyService`,
`IWebHostEnvironment` (avatar paths), `IAuditLogService`, `ILogger`.

```csharp
public interface IUserCompanyTransferService
{
    // Read-only preview: counts of what a move would clear/affect.
    Task<MoveImpact> GetMoveImpactAsync(int userId, int destCompanyId);

    // Transactional clean transfer. Returns a result describing what was done.
    Task<MoveResult> MoveUserToCompanyAsync(int userId, int destCompanyId, int actingAdminId);
}
```

`MoveImpact` carries counts (future shifts freed, vacations cancelled, chores cancelled, swaps cancelled,
on-duty cancelled, game scores removed, owned team-calendars reassigned, **all** `DirectorCompany` rows to be
removed, whether JobType/Dept/ShiftType/Home will reset) plus a list of human-readable warnings for the UI.

### 7.2 Handlers (thin)

New partial file `Pages/Admin/Users.cshtml.Move.cs` (matches the existing `Users.cshtml.Logging.cs`
convention; `UsersModel` is a `partial class`):

- `OnGetMoveImpactAsync(int userId, int destCompanyId)` → authorizes, returns `MoveImpact` as JSON for the modal.
- `OnPostMoveUserAsync(int userId, int destCompanyId)` → authorizes (§6), calls
  `MoveUserToCompanyAsync`, returns success/error with a localized message.

Both inherit the page's class-level `[Authorize(Policy = "Grant:ManagerHomeAccess")]`. No anonymous-page
registration needed (it is not under `/Api/`).

### 7.3 Grant-scope refactor (DRY, no behavior change)

Promote `UsersModel.BuildGrantScopeForTemplateAsync` (`Users.cshtml.cs:2457`, currently private) to a public
method on `IGrantService` (e.g. `BuildRoleTemplateScopeAsync(roleTemplateKey, companyId, jobTypeId)`). The
page's private copy becomes a thin delegate. The transfer service uses it to build the destination scope
(with `jobTypeId: null`, since JobType resets on move).

### 7.4 Audit overload

`IAuditLogService` currently writes to the acting admin's *current* tenant (`_tenantResolver`), which would
make a move audit invisible to both old and new company admins in a cross-company move. Add an overload that
accepts an explicit `companyId`. The move writes **two** entries: one under the **source** company
("user moved out to {dest} by {admin}") and one under the **destination** ("user moved in from {source} by
{admin}"), so each side's admins see it.

## 8. Entity action table (complete inventory)

`F` = future/active only; `IQF` = must query with `IgnoreQueryFilters()` (spans two tenants).
Every `IQF` query is scoped to the specific `userId`/company and carries a `// SECURITY-AUDITED` comment.

| Entity | User link | Bucket | Action on move |
|---|---|---|---|
| `AppUser.CompanyId` | — | — | Set to destination |
| `AppUser.JobTypeId / DepartmentId / PrimaryShiftTypeId / HomeTypeId` | — | 1 | Reset to `null` (re-assign in dest) |
| `AppUser.RoleTemplateId` | — | — | **Keep** (RoleTemplate is global/cross-tenant) |
| `ShiftAssignment` (assignee) | `UserId` | 1 | Delete **F** (frees slots for backfill); keep past (IQF) |
| `ShiftAssignment` (trainer of others) | `TraineeUserId` | 1 | Null `TraineeUserId` (IQF) |
| `TimeOffRequest` (vacations) | `UserId` | 1 | Cancel/delete **F & pending**; keep past (IQF) |
| `Chore` | `UserId` | 1 | Cancel **F**; keep past (IQF) |
| `SwapRequest` (open) | `FromUserId` OR `ToUserId` | 1 | Cancel/delete all open involving user (IQF) |
| `OnDuty` | `UserId` | 1 | Cancel **F**; keep past (global table) |
| `UserNotification` | `UserId` | 1 | Delete (IQF) |
| `OnDutyRoleSubscription` | `UserId` | 1 | Delete (IQF) |
| `DailyNotificationPreference` | `UserId` | 1 | Delete (IQF) |
| `HomeTypeOverride` | `UserId` | 1 | Delete |
| `FeatureFlag` (user override) | `UserId` | 1 | Delete rows where `UserId == user` |
| `GameScore` | `UserId` | 1 | Delete (per-company leaderboard) (IQF) |
| `DutyRotationEntry` | `UserId` | 1 | Delete (remove from old rotation queue) |
| `CalendarTextEntry` (own) | `UserId` | 1 | Delete (IQF) |
| `CalendarTextEntry` (authored on others) | `CreatedByUserId` | 4 | Keep as history |
| `UserDayNote` (own) | `UserId` | 4 | Keep as history (documented: invisible to moved user, visible to old admins) |
| `TeamCalendarMember` (membership) | `MemberUserId` | 1 | Delete the user's membership rows |
| `TeamCalendar` (owned) | `OwnerId` | 3 | **Reassign `OwnerId` to acting admin** (keep calendar for old team); do NOT delete |
| `ApiKey` | `CreatedBy` | 2 | **Delete** (credentials for old company) (IQF) |
| `ApiKeyRequest` (pending) | `RequestedBy` | 2 | Delete pending; keep `ReviewedBy` history |
| `VacationApprovalRule` (named approver) | `ApproverUserId` | 2 | Null `ApproverUserId` where it is the moved user |
| `Grant` | `UserId` | 1→re-apply | `ExecuteDeleteAsync` all (IQF) + re-apply role template scoped to dest |
| `DirectorCompany` | `UserId` | 1→re-apply | Delete all (IQF); recreate for dest molecule HQ if dest role is Director/AreaAdmin |
| `Announcement` | `CreatedBy` | 4 | Keep as history (FK valid) |
| `DutyRotation / ShiftProgram / MasterProgram / ChoreType` | `CreatedBy / UpdatedBy` | 4 | Keep as history (FK valid) |
| `DutyRotationLog` | `AssignedUserId / SkippedUserId` | 4 | Keep as history |
| `AuditLog / ProfileChangeAudit / RoleAssignmentAudit` | `UserId` / actor | 4 | Keep in old company; write new move audit (§7.4) |
| `UserFriendship` | `UserId / FriendId` | — | Keep (cross-tenant by design) |
| `UserJoinRequest` | `CreatedUserId` | 4 | Keep as history |
| Avatar files | filesystem | — | Migrate `{userId}.jpg` + `{userId}_thumb.jpg` (see §9) |

## 9. Avatar file migration

The path embeds CompanyId (`Services/AvatarService.cs` builds `Path.Combine(WebRootPath, "avatars",
companyId)`), and `AvatarService.GetAvatarUrl` resolves via the **current tenant**, so the file must live in
the destination folder by the time the move commits. Ordering:

1. Copy `avatars/{src}/{userId}.jpg` and `{userId}_thumb.jpg` → `avatars/{dest}/` **before** commit.
2. On successful `CommitAsync`: delete the old-folder copies.
3. On rollback/exception: delete the **dest** copies in a `finally` block (filesystem is not transactional —
   without this, rolled-back moves leak orphan files into the dest folder).

Missing source files are tolerated (user may have no avatar) — log and continue, don't fail the move.

## 10. Transaction & ordering

All DB mutation inside one `_db.Database.BeginTransactionAsync()`; persistence via
`IConcurrencyService.SaveWithConcurrencyHandlingAsync` (retry on `DbUpdateConcurrencyException`). Order:

1. Re-validate (user exists, dest valid, authorization re-checked server-side).
2. Clear bucket-1 personal data; revoke bucket-2 credentials/obligations; reassign bucket-3 ownership.
3. `ExecuteDeleteAsync` grants + `DirectorCompany` (IQF).
4. Reset scalar FKs; set `AppUser.CompanyId = dest`.
5. Re-apply role-template grants scoped to dest (`BuildRoleTemplateScopeAsync(destCompanyId, jobTypeId: null)`);
   recreate `DirectorCompany` for dest HQ if privileged role.
6. Copy avatar to dest folder (§9 step 1).
7. Write two audit entries (§7.4).
8. `SaveWithConcurrencyHandlingAsync` → `CommitAsync`.
9. After commit: delete old avatar files. `finally`: on failure, delete dest avatar copies.

Grant re-application ordering is safe: `BuildRoleTemplateScopeAsync` takes `destCompanyId` as an explicit
parameter and does not read `user.CompanyId`, and all reads share the one change-tracking `DbContext`.

## 11. UX flow

1. New **"Move"** action per user row → opens a modal.
2. Admin selects a destination company (list scoped to accessible companies, HQ excluded).
3. Modal AJAX-loads `OnGetMoveImpactAsync` and shows a plain-language summary:
   *"Moving {name} to {Company B} will: free 4 future shifts, cancel 1 pending vacation and 2 chores,
   remove 3 game scores, reassign 1 team calendar to you, reset their job type, and re-apply the
   {Role} role in {Company B}. Removes Director authority over: {list}."*
4. Danger-level confirmation → `OnPostMoveUserAsync`. Success toast + row refresh.

## 12. Error handling, validation, security

- Reject self-move, no-op move, HQ destination, missing/invalid user or company — each with a localized message.
- Authorization re-checked server-side in the POST handler (never trust the dropdown).
- Every cross-tenant query uses `IgnoreQueryFilters()` with a `// SECURITY-AUDITED` comment scoping it to the
  specific `userId`/company (per project convention).
- Broad async catches use `catch (Exception ex) when (ex is not OperationCanceledException)`.
- Any new EF query string-handling stays SQLite-translatable (no `ToLowerInvariant()`/`Contains(StringComparison)`
  inside `IQueryable`).

## 13. Review findings incorporated

From the expert architecture review (all 9 criticals closed):

- **Added missing entities:** `GameScore`, `DutyRotationEntry`, owned `TeamCalendar`, `UserRoleAssignment`,
  `CalendarTextEntry` (own).
- **Privilege gate:** `CanAssignRoleAsync` prevents a lower admin from moving (and downgrading) an Owner;
  cross-project blocked by the two-sided check; explicit same-project assertion as defense.
- **HQ destination** explicitly blocked; **self/no-op** blocked.
- **Avatar rollback** handled via `finally`.
- **Audit** written with explicit companyId on both sides.
- **Grant wipe** uses `ExecuteDeleteAsync` + `IgnoreQueryFilters`.
- **SwapRequest** cancels all open (From or To) with `IgnoreQueryFilters`.

Diverged from the review (with reasoning):

- **Did NOT reassign authorship `CreatedBy`/`UpdatedBy`/`ReviewedBy`** on old-company config/announcements/
  reviews. The user row persists in a move, so these FKs stay valid and the records are accurate history;
  rewriting them loses information for no integrity benefit (bucket 4).
- **Owned `TeamCalendar`: reassign owner, not delete** — deleting would destroy a calendar the old-company
  team still uses (bucket 3).
- **`ApiKey`/pending `ApiKeyRequest`: delete, not reassign** — they are credentials, not authorship (bucket 2).

## 14. Testing

SQLite `:memory:` fixtures (real provider, **not** EF InMemory — InMemory hides SQL-translation bugs and
does not exercise query filters the same way). Cases:

- CompanyId moves to destination; identity (email/password/profile/friendships) unchanged.
- Future shifts deleted (slots freed) but past shifts retained; trainer `TraineeUserId` nulled.
- Pending/future vacations, chores, swaps (From & To), on-duty cancelled; game scores, duty-rotation entries,
  notifications, subscriptions, prefs, own calendar entries, user's team-membership deleted.
- Owned team calendar reassigned to admin (not deleted); members preserved.
- Scalar FKs reset; `RoleTemplateId` retained.
- Grants revoked and re-applied scoped to the destination molecule/area/project; `DirectorCompany`
  removed and recreated for dest HQ on privileged roles.
- `ApiKey` deleted; `VacationApprovalRule.ApproverUserId` nulled where applicable.
- Avatar full+thumb moved to dest folder; old removed; rollback leaves no dest orphan.
- Two audit entries written under source and dest companies.
- **Authorization:** molecule admin denied cross-molecule, allowed within; HQ dest blocked; self-move blocked;
  no-op blocked; molecule admin denied moving an Owner (`CanAssignRoleAsync` gate).
- `GetMoveImpactAsync` counts match what `MoveUserToCompanyAsync` actually clears.
- Translation guard: any new EF query in the service is exercised against real SQLite.

## 15. Localization

All new UI strings + error messages added to **both** `SharedResources.resx` and
`SharedResources.he-IL.resx`, rendered via `<loc>`/localizer. The move modal and impact summary verified in
Hebrew RTL and in dark mode (per project's recurring contrast/RTL checklist).

## 16. Open implementation items (flagged, not silently deferred)

1. **`UserRoleAssignment` recreation:** confirm at code time whether deleting old-company rows requires
   creating a destination-scoped replacement, or whether the re-applied grants alone are authoritative.
   Resolve by reading how `AssignRoleTemplateGrantsAsync` / user-creation records role assignments.
2. **`IAuditLogService` overload shape:** confirm the minimal signature for the explicit-companyId overload
   against the existing interface so it is additive (no change to current callers).
3. **`CanAssignRoleAsync` exact signature/inputs** (`Users.cshtml.cs:1042` area) — confirm whether it takes a
   role template key/id and a target company, and wire the move gate to it accordingly.
