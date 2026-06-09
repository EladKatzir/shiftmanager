# Multi-Company Membership — Design Spec

- **Date:** 2026-06-10
- **Status:** Approved (brainstorming complete) → proceeding to implementation plan
- **Author:** Elad Katzir (with Claude, Opus 4.8)
- **Related prior work:** `docs/superpowers/specs/2026-05-23-user-company-move-design.md` (single-company *move* — the conceptual opposite; its cleanup logic is reused here)

---

## 1. Motivation

Today a user belongs to exactly **one** company. `AppUser.CompanyId` is a non-nullable `int`, tenant isolation runs through ~40 EF query filters keyed on `ITenantResolver.GetCurrentTenantId()`, and that resolver reads a single `"CompanyId"` claim. Multi-company "switching" exists today **only as a privilege** (Owners via `IOwnerCompanySelectorService`; Directors/AreaAdmins via the `director_selected_molecule` cookie + `DirectorHubAccess` grant) — never as **membership**.

Two real-world scenarios require true membership:

- **Persona A — the dual-member:** one human genuinely does shifts in two companies (e.g. a reservist in two units) and must see/do shifts, requests, and profile in each.
- **Persona B — the kabar:** a single leader manages **two unrelated companies** (different molecules/areas) — a *member* in one, a *manager* in the other.

Both collapse into one model: a **N:N membership** where each `(user, company)` link carries its **own** role, job-type, and shift participation.

---

## 2. Guiding principle — "scope follows the subject"

The codebase already runs two scoping styles side by side, and the split is the design:

- **Person-centric pages** (*"things I am responsible for as a person"* — my shifts, my requests, notifications, approvals I owe) → **merged/aggregated** across all my companies, every row stamped with a company badge. Most of these already aggregate (e.g. `My/Requests` filters by `UserId` only; the approval inbox `Requests/Index` already unions `accessibleCompanyIds`).
- **Company-operational pages** (*"a company's surface I'm editing"* — its roster grid, users, settings, blueprints, any record I create) → **one active company at a time**, chosen via a switcher. The Shifts calendar's molecule dropdown is the read-only flavor of this and already works via grants.

**"Lean merged when safe"** has a precise test: merge is safe when the page only **reads** and rows carry a company badge (no write-target ambiguity); keep single-tenant where the page **writes** (which company owns the new row?).

**Rejected:** full global merge everywhere — tenant isolation (the ~40 query filters, `CompanyIdInterceptor` on writes, SignalR group names) is load-bearing on an air-gapped, 1400-test system. Global merge means every write needs an explicit target company, every read a multi-tenant union, and SignalR must join multiple group-sets. High regression risk for ~10% more value. The hybrid follows patterns already in the code.

---

## 3. Locked decisions

| Decision | Choice |
|---|---|
| Interaction model | Hybrid: merge person-centric, switch company-operational; lean merged where read-only + badged |
| Data model | **Strangler / mirror**: `CompanyMembership` authoritative for the membership set + additional companies; `AppUser` keeps identity + **primary-membership mirror** columns; `AppUser.CompanyId` = home pointer. Full normalization deferred. |
| Leave routing | **Fan out** a copy per company the user does shifts in, linked by `LeaveGroupId` |
| Leave approval | **Union approver pool** across membership companies; **either** eligible manager approves; a single decision **cascades** to the whole group |
| Per-company deactivation | Out of scope (V1) — `AppUser.IsActive` stays global |

---

## 4. Architecture

### 4.1 `CompanyMembership` entity

Models the precedent set by `DirectorCompany` (soft-delete, `(UserId, CompanyId)` unique, queried by `UserId` with `IgnoreQueryFilters`).

```
CompanyMembership
  Id
  UserId            → AppUser
  CompanyId         → Company
  RoleTemplateId?                 role IN this company
  JobTypeId? / DepartmentId?      org placement in this company
  DoesShifts                      participates in shifts here
  HomeTypeId?                     rotation here
  IsPrimary         (exactly one) → mirrors AppUser.CompanyId
  IsDeleted, DeletedAt            soft-delete (DirectorCompany pattern)
  JoinedAt, GrantedBy
```

- **NOT tenant-query-filtered** (must be readable before a tenant is chosen). Queried by `UserId` + `IgnoreQueryFilters()`, exactly like `DirectorCompany`.
- Composite unique index: `(UserId, CompanyId) HasFilter("IsDeleted = 0")`.
- `UserShiftCategory` N:N re-keys (or gains a `MembershipId`) so shift categories are per-membership; the primary membership reuses `AppUser`'s existing categories during the strangler phase.

### 4.2 `AppUser` as the primary membership (mirror)

`AppUser` keeps `CompanyId / RoleTemplateId / JobTypeId / DepartmentId / DoesShifts / HomeTypeId` and its `ShiftCategories` — these **represent the primary membership** and stay in sync with the `IsPrimary` `CompanyMembership` row via a single write path (`ICompanyMembershipService`). Benefits: **zero rewrite** of the ~40 query filters and the home-company claim; **zero conflict** with the recently-landed Category/Roster redesign (which reads these off `AppUser`). Cost: a contained dual-write on the primary row.

### 4.3 Backfill

Every existing user → exactly one `CompanyMembership` with `IsPrimary = true`, copying current `AppUser` columns.

---

## 5. Active-company switcher (the "switch" half)

- **`IActiveCompanySelectorService`** — superset of `IOwnerCompanySelectorService`. Cookie-stored selection (12h, `HttpOnly`/`Strict`), **validated against the caller's live membership set every resolve** (cached per request). Owner's "any company" power = special case.
- **`TenantResolver` priority chain** gains one rung: `override → Owner-selected → member-selected (membership-validated) → home CompanyId claim`.
- **Hierarchy follows the active company.** Switching also updates the effective `Molecule/Area/Project` context used by grant resolution (analogous to `director_selected_molecule`), so `GetUserHierarchyContextAsync`-style resolution is correct for the active company, not just home. *(Correctness edge surfaced in research — Self-scope + cascade resolution would otherwise reflect home.)*
- **UI:** reuse `ContextSwitcherViewComponent`; change the regular-user branch (currently emits one fixed option) to list the user's membership companies. Single-company users keep the non-interactive single-context display — no visual change for ~99% of users.

---

## 6. Grants & per-company role

- A membership's `RoleTemplateId` drives `AssignRoleTemplateGrantsAsync(userId, templateKey, scope, actor)` scoped to **that** company (dedup is built in). Mirrors how the primary works today.
- Removing a membership deletes that company's grants — reusing `UserCompanyTransferService` cleanup.
- `AppUser.Role` (the `UserRole` enum behind `IsInRole`) reflects the **primary** membership. Operational power in other companies comes from **grants**, not the enum — so a primary-Employee who is a manager elsewhere gets that power via grants while `IsInRole("Owner")` etc. stay correctly false.
- Composes with the existing Director model: a `DirectorCompany` row and a `CompanyMembership` row can coexist (both keyed `(UserId, CompanyId)`).

---

## 7. Merged person-centric pages

Aggregate across the membership set, each row company-badged:

- **My Shifts / My Requests** — already `UserId`-only; minimal change (add badges).
- **Approval inbox** (`Requests/Index`) — already unions `accessibleCompanyIds` via grants; already merged.
- **Notification bell / unread count** — see bug #2 below.

---

## 8. Leave fan-out + union approvers

- Add **`LeaveGroupId` (Guid?)** to `TimeOffRequest` (no grouping field exists today).
- Filing leave → **one copy per company the user does shifts in**, all sharing `LeaveGroupId`, each with explicit `CompanyId` (safe: `CompanyIdInterceptor` only fills when `0`).
- **Approver pool = union** of `ApproveVacations` / `ApproveExtendedLeave` grant-holders across the user's membership companies (widen the `My/Requests` approver query from `currentUser.CompanyId` to the membership set).
- **Single decision cascades:** any eligible approver (from either company) approving any copy flips the whole `LeaveGroupId` group to the same status and stamps the actor. `VacationApprovalService.CanUserApproveAsync` widens its company check to "approver holds the grant in **any** of the requester's membership companies."
- This is the one genuinely new approval behavior → dedicated tests (cascade, partial-failure, dual-tier interaction).

---

## 9. Real-time (SignalR)

No per-user real-time channel exists today (notifications are poll-on-load), so multi-company adds **no new hub**. The calendar grid joins the group for the company/molecule it renders — correct under the switch model (the kabar's molecule dropdown re-joins on switch). **Deferred:** live cross-company grid updates for the *inactive* company (bell-count merge covers obligation-visibility).

---

## 10. Must-fix bugs (multi-company would otherwise ship these)

| # | Bug | Fix |
|---|-----|-----|
| 1 | `AvatarService.GetAvatarUrl()` builds URLs from `GetCurrentTenantId()` not the avatar **owner's** company (AvatarService:~207) — pre-existing latent bug; multi-company → wrong-folder 404s/leaks. | Build from `user.CompanyId` (owner's primary). |
| 2 | Notification **bell/unread count** is tenant-filtered (`AnalyticsController:~114`). | Count by `UserId` across the membership set (person-centric merge). |
| 3 | `BusyService.ValidateUserBusyAsync` checks `user.CompanyId ∈ target.MoleculeId` (~205) → rejects valid cross-company assignments. | Accept if **any** membership company is in the target molecule. |
| 4 | Profile-change audit files under request tenant not the edited user's company (ProfileService:~125). | Use `targetUser.CompanyId`. |

Each is independently shippable and valuable on its own.

---

## 11. `/Admin/Users` membership UI

The users table is already 11 columns wide with inline-edit cells — do **not** widen it.

- **Company column → chips:** primary in bold + a chip per additional company (reusing the existing director-companies tooltip precedent on that cell).
- **Expander row:** a chevron reveals a memberships panel — one sub-row per `CompanyMembership` with its **own** inline Role / JobType / DoesShifts / Categories editors (the same cell components, bound to a membership).
- **Add:** "➕ Add to company" inside the expander → company → role template → job type → DoesShifts → `OnPostAddMembership`.
- **Remove:** per sub-row, with the same `MoveImpact`-style orphan preview the Move flow already shows.

---

## 12. Invariants & edge cases

1. `CompanyMembership` is **not** tenant-query-filtered; queried by `UserId` + `IgnoreQueryFilters`.
2. Composite unique `(UserId, CompanyId) WHERE IsDeleted = 0`.
3. **Exactly one** `IsPrimary` per user; `AppUser.CompanyId == primary.CompanyId` (single write path keeps them in sync).
4. **Removing the primary** is blocked unless another membership is promoted first (which rewrites `AppUser.CompanyId` + re-bakes claims on next login).
5. Switcher **trusts nothing** — active-company cookie validated against the live membership set every resolve.
6. **Move is re-expressed on membership ops:** `UserCompanyTransferService` cleanup *is* the "remove membership" routine; Move = add new primary + remove old. One cleanup path.
7. **Deactivation stays global** (`AppUser.IsActive`).

---

## 13. Build order (epics)

1. **Foundation** — `CompanyMembership` entity + migration + backfill, unique index, `ICompanyMembershipService` (add / remove / set-primary, sync `AppUser.CompanyId`).
2. **Switcher** — `IActiveCompanySelectorService` (membership-validated) + hierarchy-context follow + `TenantResolver` rung + `ContextSwitcher` regular-user branch.
3. **Bug-fix sweep** — must-fixes #1–4 with regression tests (shippable independently).
4. **Admin/Users UI** — chips + expander + add/remove membership.
5. **Per-company grants/role** — role-template application per membership; hierarchy-aware resolution.
6. **Leave fan-out** — `LeaveGroupId`, union approvers, cascade approval.
7. **QA sweep** — bilingual (he-IL / en) + RTL on switcher & Admin UI; tenant-isolation security tests; full suite run **sequentially** (`-- xUnit.ParallelizeTestCollections=false`; parallel `:memory:` SQLite contention produces spurious failures).

---

## 14. Out of scope / deferred

- **Per-company deactivation** (active in A, suspended in B) — `IsActive` stays global.
- **Live cross-company calendar-grid real-time** for the inactive company.
- **Friendships / on-call avatars across companies** — classified SAFE (userId-based); render from primary company; unchanged.
- **Full normalization** (stripping per-company columns off `AppUser`) — deferred cleanup; strangler ships first.

---

## 15. Testing strategy

- **Foundation:** backfill produces exactly one primary per user; unique-index enforcement; `AppUser.CompanyId` ↔ primary sync.
- **Switcher:** cookie validated against membership set (reject non-member company); Owner "any company" still works; hierarchy context follows active company.
- **Isolation (security):** active in A cannot read/write B's data except via merged person-centric pages; writes always land in the active tenant.
- **Grants:** per-company role applies correct grants; removing a membership removes only that company's grants.
- **Leave:** fan-out copies share `LeaveGroupId`; union approver authorization; single decision cascades; dual-tier interaction.
- **Bug-fixes #1–4:** dedicated regression tests.
- All EF-touching tests on **real SQLite** (`:memory:`), per the established fixture migration. Full suite run **sequentially**.
