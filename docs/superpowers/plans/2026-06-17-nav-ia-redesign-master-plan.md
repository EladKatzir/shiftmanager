# Navigation & IA Redesign — Master Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (inline) — this redesign edits shared files (`_Layout.cshtml`, `Program.cs`, `*.resx`) so tasks are executed **sequentially inline**, not by parallel subagents (which would conflict on those files). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Ship the domain-hub information architecture (Model B + command palette) from `2026-06-16-nav-ia-redesign-design.md` as an **additive, feature-flagged navigation layer**, so flag-off is byte-identical to today and the new IA can be enabled per company.

**Architecture:** A new sidebar + header rendered only when `FF_NEW_NAV` is enabled (resolved per user/company). The new nav is driven by a single **NavRegistry** (one source of truth for structure) whose every node declares the **same `Grant:` policy its destination page enforces**; visibility is **derived** from that policy via `IAuthorizationService` (P2 — kills the link-gate≠page-gate bug). The new nav points at existing routes (Phase 1 makes everything reachable); later phases progressively replace link-throughs with hosted/merged surfaces and build the one genuinely new page (the Eligibility home, Issue 3).

**Tech Stack:** ASP.NET Core 8 Razor Pages, SQLite/EF, grant-based `Grant:*` policies (`GrantPolicyProvider` + `GrantAuthorizationHandler`), `IFeatureFlagService.IsEnabled(flag, userId, companyId)`, `<loc key>` localization (resx ×2), xUnit (`ShiftManager.Tests`, run sequential).

---

## Strategy & safety rails

1. **Additive + flag-gated.** All new nav lives behind `FF_NEW_NAV` (seeded **disabled**). `_Layout.cshtml` branches: flag-on → new nav partials; flag-off → today's markup unchanged. **Flag-off must be byte-identical** (a test asserts the legacy path renders when the flag is off).
2. **One source of truth.** `NavRegistry` defines the tree once. Both rendering and the drift-prevention test read it.
3. **Visibility == access (P2).** Node visible iff `AuthorizeAsync(user, node.Policy)` succeeds (null policy = any authenticated; `Flag` gate also honored). Hub visible iff it has ≥1 visible child. Deep-link to an unauthorized child → redirect to first authorized child, never 403.
4. **Localization/RTL/dark are part of every task.** Every label is a `<loc key>` added to BOTH resx files; **grep the key first** (dup keys break localization tests). Verify each surface in Hebrew RTL + dark.
5. **Green at every commit.** Build + targeted tests pass before each commit; full suite (`xUnit.MaxParallelThreads=1`) after each phase. Never leave the build broken (CLAUDE.md rule 3).
6. **No `FinalProductPublish/` edits** (generated). Regen is a deploy step, out of scope here.

---

## Shared contracts (build these in Phase 1, reused everywhere)

### `Models/Navigation/NavNode.cs`
```csharp
public sealed record NavNode(
    string LocKey,                 // <loc key> for the label
    string? Route = null,          // destination URL; null = pure group/hub header
    string? Policy = null,         // "Grant:X" — MUST equal the destination page's policy; null = authenticated-only
    string? Icon = null,           // lucide icon name
    string? Flag = null,           // optional FeatureFlagSeed.Flags.* gate
    string? ActiveMatch = null,    // path prefix for active-state; defaults to Route
    IReadOnlyList<NavNode>? Children = null,
    NavRole MinRole = NavRole.Any  // reserved for role-aware label/depth (P4)
);
public enum NavRole { Any, Manager, Director, Admin, Owner }
```

### `Services/Navigation/INavigationService.cs` + `NavigationService.cs`
- `Task<IReadOnlyList<NavNode>> GetVisibleNavAsync(ClaimsPrincipal user)` — deep-filters `NavRegistry.Root`:
  - leaf kept iff (`Policy is null` ⇒ authenticated) OR `(await _authz.AuthorizeAsync(user, Policy)).Succeeded`, AND (`Flag is null` OR `_flags.IsEnabled(Flag, userId, companyId)`).
  - group kept iff it has ≥1 kept child OR (it has a Route and is itself authorized).
- `Task<string?> FirstAuthorizedRouteAsync(ClaimsPrincipal user, NavNode hub)` — for hub deep-linking + redirect-not-403.
- Injects `IAuthorizationService`, `IFeatureFlagService`, `ICurrentUserService`.

### `Services/Navigation/NavRegistry.cs`
- Static `NavNode Root` = the full domain-hub tree from design §4–§5 (Home, Scheduling, Requests, People, Organization, Access, Insights, System, Me) with each leaf's `Route` = existing page route and `Policy` = that page's actual policy (mapping table below).

### Feature flag
- Add `FF_NEW_NAV` to `FeatureFlagSeed.Flags` + `FDisabled(...)` entry (seeded OFF). Description: gating the domain-hub navigation.

### Drift-prevention test `NavRegistryPolicyParityTests`
- Walk `NavRegistry.Root`; for every leaf with a Razor-Page `Route`, resolve the page's configured authorization policy from `EndpointDataSource` and assert it equals `node.Policy` (authenticated-only pages ⇒ `node.Policy is null`). This is the structural guarantee for P2.

---

## Phase 1 — Nav foundation (the keystone; build first)

**Outcome:** With `FF_NEW_NAV` on, the new sidebar renders all domain hubs (children link to existing routes), policy-gated; header gains the context controls + command palette; flag-off is unchanged. Everything reachable; nothing physically moved yet.

### Task 1.1 — NavNode + flag
- Create `Models/Navigation/NavNode.cs`, `NavRole`. Add `FF_NEW_NAV` to `FeatureFlagSeed`.
- [ ] Test: `FeatureFlagSeedTests` asserts `FF_NEW_NAV` present and seeded disabled.
- [ ] Implement; build; commit `feat(nav): NavNode model + FF_NEW_NAV flag (off)`.

### Task 1.2 — NavRegistry (the tree)
- Create `Services/Navigation/NavRegistry.cs` with the full tree (mapping table below). Pure data; no DI.
- [ ] Test: `NavRegistryShapeTests` — Home/Scheduling/Requests/People/Organization/Access/Insights/System/Me all present; Scheduling has Calendars/Planning/Definitions/Eligibility/Rules; no duplicate routes; every non-group node has a Route.
- [ ] Implement; build; commit.

### Task 1.3 — NavigationService (policy-derived visibility)
- Create `INavigationService` + `NavigationService`; register in `Program.cs` DI (scoped).
- [ ] Tests (`NavigationServiceTests`, in-memory authz fakes): employee sees only Home/Scheduling(view)/Requests/Me; manager sees People/etc.; null-policy node always shown to authenticated; flag-gated child hidden when flag off; hub hidden when all children unauthorized.
- [ ] Implement; build; commit.

### Task 1.4 — Policy parity test (P2 guarantee)
- [ ] `NavRegistryPolicyParityTests` (described above). It will FAIL if any node's policy ≠ its page's policy — fix the registry until green. **This is the bug-class fix.**
- [ ] Commit `test(nav): registry policy parity == page policy`.

### Task 1.5 — New sidebar partial
- Create `Pages/Shared/_NavSidebar.cshtml` rendering `await Nav.GetVisibleNavAsync(User)` as collapsible hubs (children = sub-links), role-aware label for the Scheduling slot ("My Schedule" when the only authorized child is Calendars; "Scheduling" otherwise), active-state via `ActiveMatch`, `localStorage` collapse memory + last-tab memory (P4). Reuse existing `.app-sidebar*` classes + tokens; add minimal new CSS to `navigation.css`.
- [ ] Branch `_Layout.cshtml`: `@if (FeatureFlagService.IsEnabled(FF_NEW_NAV, userId, companyId)) { <partial _NavSidebar> } else { <existing nav> }`.
- [ ] Test `LayoutNavFlagTests`: flag-off renders legacy nav marker; flag-on renders `data-nav="v2"` marker.
- [ ] Build; **browser-check flag-on in light/dark/RTL**; commit.

### Task 1.6 — Header context controls + palette shell
- New header partial: **Active** context chip + **Viewing scope** ("N of M", only when user oversees >1 company) reusing `ContextSwitcher`/CompanyFilter services; keep bell/lang/theme; **persistent View-As exit banner** when impersonating. Promote the Ctrl-K stub to a working command palette over `GetVisibleNavAsync` results (authorized-only).
- [ ] Tests: palette lists only authorized routes; scope control hidden for single-company users.
- [ ] Build; browser-check; commit.

**Phase-1 mapping table (leaf → existing route → policy):** see Appendix A. Phase 1 uses existing routes verbatim; later phases swap routes to merged/hosted surfaces.

---

## Phase 2 — Scheduling hub (consolidation of the heaviest domain)
- Repoint Scheduling children to canonical surfaces; **rename** nav labels Programs→"Shift Plans", MasterPrograms→"Master Plans". Promote **Table** + **Overview** into `Scheduling ▸ Calendars`. Drop legacy Month/Week/Day + `/Schedule/Index` + `/Calendar/Index` from nav. Give **Distribution Lists** a real managed page (promote the AJAX modal) under `Scheduling ▸ Eligibility`. Add hub deep-link + last-tab memory.
- In-calendar Draft/Justice/quick-entry/DL-filter stay (P1); add hub *signpost* entries that deep-link into the calendar with the tool open.
- Tasks: label resx renames; new `Pages/Scheduling/DistributionLists` managed page (TDD against `IDistributionListService`); nav route updates; legacy-orphan removal from registry; calendar canonicalization (one chore/on-duty calendar — resolve the flag bait-and-switch by making the registry pick the canonical route). Tests + browser sweep per task.

## Phase 3 — Eligibility home (Issue 3) — the one genuinely new page
- New `Pages/Scheduling/Eligibility/Index` with two views:
  - **By-category:** pick category → rules (gender/officer/waiver — reuse ChoreTypes chip UI) + live "who's eligible" roster + inline membership add/remove.
  - **By-person:** search person → every shift/chore category they can/can't do + **reason** (rule failed / missing DoesShifts|DoesChores / not-a-member) + inline toggle.
- Assemble from existing services (shift-category defs, `EligibilityRule`, `DoesShifts`/`DoesChores`, `UserChoreCategory`, exemptions). New grant? No — reuse `ManageShiftCategories`(136)/`EditChoreTypes` (a combined policy). Move the per-user multiselect *out* of `/Admin/Users` (leave a read-only "Eligible for…" summary that deep-links here).
- Employee read-only "why am I (in)eligible?" surfaced on the shift/chore cell (additive).
- TDD: a `EligibilityQueryService` returning `(person, category, eligible, reason)` is the testable core; pages are thin. Full browser sweep.

## Phase 4 — People / Organization / Access
- **People:** Roster (slim — deep-links to Eligibility/Access instead of inline), Join-requests tab (split out), Companies tab (renamed from "Membership"). *Physical roster split is the largest lift — own sub-tasks, heavy tests.*
- **Organization:** Hierarchy-as-editor (inline CRUD on the tree, absorb read-only Hierarchy twin), Job Types; demote AreaPalette into the Area editor.
- **Access:** Roles/Grants/Templates/Simulator/Directors(rescued)/Locked-users tabbed; absorb `/Owner/Permissions` stats into Grants header.

## Phase 5 — Insights / System (operator hubs + danger zone)
- **Insights** (observe): Health (merge SystemHealth+Index stats), Telemetry, Audit (merge AuditLog+AuditSearch), Analytics.
- **System** (mutate; opens on first tab, no dashboard): Feature flags, Integrations (Email+SSO+Game-subsection), Localization, Content&config, **⚠ Data & ops** danger zone (segregated, typed-confirm for irreversible, DB console read-only default). Kill both legacy `/Owner` landings. Active-company banner.

## Phase 6 — Requests + company-context primitive + Home spine
- **Requests:** one role-aware slot — own (swaps folded, startable from a shift) + approvals queue (badge) + director "all companies" scope; hard page gate. Retire the link-swap split + `/Director/NotificationHub` rename.
- **Context primitive:** Active + Viewing-scope header controls (from Phase 1) become the single source; delete `/Director/CompanyFilter` page; View-As mode + persistent exit banner; unify the Owner Company Selector into the same Active primitive.
- **Home:** merge `/My` rich timeline into Home as the schedule-spine; role widgets stack; Director oversight variant; Operator → Insights▸Health.
- **Settings/Rules:** merge `/Admin/Config`+`/Admin/Settings`+ApprovalRules+ApprovalSettings into scope-aware `Scheduling ▸ Rules`.

## Phase 7 — `/Help` per-persona guides + persona findability verification
- Rebuild `/My/Help` hub into a **per-user-type guide**: for each persona (Employee, Lead, Assigner, Director, Admin, Owner), a "where everything lives" map — every feature → its hub▸tab path, with the role-aware labels. Localized; RTL/dark. (This is the user's explicit deliverable + doubles as the verification artifact.)
- **Persona verification:** re-run the six persona advocates against the *implemented* nav (browser snapshots) and have each confirm it can reach its top tasks easily/exactly; fix anything that fails.

---

## Self-review (spec coverage)
- Model B hubs → Phase 1 registry + Phases 2–6. Issue 3 → Phase 3. Policy bug (P2) → Tasks 1.3/1.4. Context primitive (P3) → Phase 1.6 + Phase 6. Calendar cockpit (P1) → Phase 2 (signposts, in-calendar kept). Danger zone (P6) → Phase 5. Home spine (P7-design) → Phase 6. Eligibility two-faces → Phase 3. /Help guides → Phase 7. Forks (Rules/Requests/Notifications) → Phases 6/6/5. Renames/merges/splits/orphans → Phases 2–6 (Appendix A). Localization/RTL/dark/multi-company/grants → every task. **No spec section without a phase.**
- Realism note: Phase 1 + Phase 3 + Phase 7 make the new IA fully navigable, discoverable, and documented (the user's core ask). Phases 2/4/5/6 deepen consolidation (physical merges/splits) and are sequenced after; each is independently shippable behind `FF_NEW_NAV`.

## Appendix A — leaf → route → policy mapping
*(Built and kept exact in `NavRegistry.cs`; the parity test enforces policy correctness. Authoritative routes/policies are read from the codebase during Task 1.2.)*
