# Chores ↔ ShiftType Parity — Design Spec

- **Date:** 2026-06-14
- **Branch:** `dev`
- **Status:** Design approved-by-delegation; the §13 product/privacy decisions were **resolved by the user on 2026-06-14** (see §13). This header section's decision record (D3/D4/D5/D7) has been updated to match; the resolved decisions are authoritative where any deeper section still reflects an agent default.
- **Authors:** Brainstormed with user; data-model/service decisions by `code-architect` (Opus); UI/UX by `shiftmanager-ui-designer`. Synthesized by the main session.

---

## 1. Context & Goal

Today a **chore** is a structurally flat unit: `Chore` = one user, one date, a free-text title (`Models/Chore.cs`), and `ChoreType` is a molecule-scoped **color label only** (`Models/ChoreType.cs`). Shifts, by contrast, have a 4-layer spine (`ShiftProgram → ShiftInstance → ShiftAssignment` + `ShiftCategory`/`ShiftGrouping`/`UserShiftCategory` membership) with rich eligibility (`EligibleCompanyIds`, `RequiresOfficerRank`) and a `DoesShifts` participation flag.

**Goal:** Promote chores to mirror the shift *type/category/membership/eligibility* spine, while **keeping assignment manual** (no scheduler, no auto-materialization), and add **duration-weighted fairness reporting**. Concretely the user wants:

1. **Predetermined chore definitions** (like ShiftTypes via Blueprints) instead of free text.
2. **Categories** above chore types (Physical, Computer, …) — the axis for "who does chores."
3. A per-user **`DoesChores`** flag + per-category membership, mirroring `DoesShifts` + `UserShiftCategory`.
4. **Length/duration** on chores so a 4h chore is worth **less** than a longer one → **duration-weighted fairness**.
5. **Eligibility constraints**: male-only / female-only / officer-rank (katzin) only chores, plus per-person **waivers/exemptions** (disabilities).

**Out of scope (explicit):** auto-assignment, auto-generation of slots, recurrence scheduling. "Recurrence" = **reusable templates a manager stamps across a date range**, each stamped chore still flowing through the existing manual `CreateChoreAsync`.

---

## 2. Confirmed Product Decisions (from brainstorming)

| # | Decision |
|---|---|
| P1 | Assignment stays **manual**; system **enforces eligibility + reports fairness**. No auto-assign. |
| P2 | "Recurrence" = **reusable stamp-out templates**, a convenience over `CreateChoreAsync`. No scheduler. |
| P3 | Structure mirrors the **shift spine** (Approach A), chore-local entities. |
| P4 | Rank + gender constraints use a **chore-scoped `EligibilityRule` (per ChoreType)**; shifts do NOT use it. |
| P5 | **Waivers stay chore-specific** (`UserChoreExemption`), NOT part of the generic rule. |
| P6 | **Full arc** designed in one spec: foundation + eligibility + gender + fairness. |
| P7 | Membership at **category level**; weight in **duration-minutes**; the **one-active-chore-per-user-per-day** unique index is preserved. |

---

## 3. Architecture Decisions (decision record)

> Each decision is grounded in real source; `code-architect` read the load-bearing files and the `shiftmanager-ui-designer` read the UI patterns. The earlier-reported "AssignChores = grant #52" was a line-number/ID confusion; verified IDs: `AssignChores` #17, `EditChoreTypes` #18, `CreateChoreTypes` #19 (seed uses `id++`; current max grant = #136 `ManageShiftCategories`).

- **D1 — Membership granularity:** category-level `UserChoreCategory` + `AppUser.DoesChores`, mirroring `UserShiftCategory`/`DoesShifts`. Per-type allow-listing is intentionally *not* a positive membership; finer restriction is handled by rules (gender/rank) and exemptions (negative).
- **D2 — `EligibilityRule` modeling:** one **chore-scoped table (per `ChoreType` via a direct `ChoreTypeId` FK)**. **Chore-specific by design — shifts do NOT use it.** `ShiftType.RequiresOfficerRank` and `ValidateShiftAsync` are **untouched** this cycle (no unrelated refactoring); shifts keep their own officer-rank flag. The evaluator is a **pure, stateless domain service**.
- **D3 — Gender field (UPDATED 2026-06-14):** new `AppUser.Gender` enum `{Unspecified=0, Male=1, Female=2}`, default `Unspecified`. Sensitive PII. **Gender mismatch is an overrideable WARNING, not a hard error** — for both a definite mismatch (e.g. Female on a male-only chore) and the `Unspecified` case, the manager may override via the existing HMAC-token warning path (alongside vacation/shift conflicts). Gender is **viewable and editable by any existing user-editor** (rides current user-edit authz); **no dedicated grant**; the value change is audited. (Officer-rank and waiver/exemption remain HARD, non-overrideable.)
- **D4 — Weight model (UPDATED 2026-06-14):** `Chore.WeightMinutes` (int), **frozen at create time** (not recomputed at read), resolved as: explicit `StartTime/EndTime` → `ChoreType.DefaultWeightMinutes` → global fallback `DEFAULT_CHORE_WEIGHT_MINUTES = 480` (8 hours, fixed constant). Free-text (null type) chores get the fallback. Frozen so editing a type's default never rewrites historical fairness. **Architect note (2026-06-15):** an untimed/typeless chore counting as 8h is heavy — duration-weighting only *differentiates* people when chore types set `DefaultWeightMinutes` or chores carry explicit times; otherwise fairness degenerates to count×8h. The Phase 3 type editor should encourage setting `DefaultWeightMinutes`.
- **D5 — Fairness:** **extend the existing Justice engine** — swap the chore `g.Count()` for `g.Sum(c => c.WeightMinutes)` in the two chore branches; add an optional `JusticeQuery.ChoreCategoryId` (null = today's behavior); reuse `ViewJusticeTable` #133 / `EditJusticeTargets` #134 (no new grant). Targets stay in **chore-equivalents**, scaled by 480 in code (no target data migration).
- **D6 — Migration/back-compat:** seed a per-molecule **"General"/"כללי"** `ChoreCategory` for molecules with existing types; backfill `ChoreType.ChoreCategoryId` then leave it **nullable + `SetNull`** (the Phase 1 plan dropped the NOT-NULL tighten — matches `ShiftType.CategoryId` + the "Uncategorized" UI); free-text chores keep `null ChoreTypeId`; backfill `WeightMinutes` from times where present (else the **480** default); backfill `DoesChores = true` for existing active Standard users (preserves roster). Unique chore index **unchanged**.
- **D7 — Grants (UPDATED 2026-06-14): NO new grants.** Reuse `EditChoreTypes`/`CreateChoreTypes` for category/type/rule/exemption/template admin, `AssignChores` for assignment + stamping, `ViewJusticeTable`/`EditJusticeTargets` for fairness. Gender view+edit and `DoesChores`/category/exemption handlers all ride existing user-edit authz. The previously-proposed `EditUserGender` grant #137 is **dropped** — this feature touches the grant seed **zero** times.
- **D8 — Convention fix:** extract the inline `IChoreService` (`ChoreService.cs:9-40`) into `Services/IChoreService.cs`.
- **D9 — Roster filter:** new predicate `AccountType == Standard && IsActive && (DoesChores || ChoreCategories.Any())`. Display-only; the BusyService hard-gate remains the authorization boundary.
- **D10 — Templates:** new molecule-scoped `ChoreTemplate` definition; "stamp" loops `CreateChoreAsync` per (date, assignee); each validates independently; hard-error days skipped + reported. No schedule persisted, no background job. **Architect requirement (2026-06-15): `StampTemplateAsync` MUST bound the operation** — reject if the date span exceeds **92 days** or the total prospective chores (`dates × assignees`, or `dates` in rotate mode) exceeds **500** — because each (date,user) is its own transaction + busy-validation, and an unbounded stamp would risk a request timeout. The Phase 3 stamp modal enforces the same limit client-side. Return a clear "stamp too large" error key, not a partial run.

---

## 4. Data Model

All new **config** entities are molecule-scoped (no `IBelongsToCompany`, no tenant query filter — same posture as `ShiftCategory`/`ChoreType`/`ShiftType`). Per-user join/exemption tables key on `UserId`. `Chore` keeps its existing tenant scoping unchanged.

### New enums
- `Models/Support/Gender.cs`: `{ Unspecified=0, Male=1, Female=2 }`
- `EligibilityRuleKind { RequiresGender=0, RequiresOfficerRank=1 }`

### `AppUser` (modified)
- `Gender Gender` (default `Unspecified`) — sensitive
- `bool DoesChores` (default `false`; backfilled `true` for existing active Standard users)
- `List<UserChoreCategory> ChoreCategories`
- `List<UserChoreExemption> ChoreExemptions`

### `ChoreCategory` (new — molecule-scoped, mirrors `ShiftCategory`)
`Id, MoleculeId(FK Restrict), Name, DisplayName, NameEn?, NameHe?, Color?, SortOrder, IsActive, CreatedAt` (no `CreatedByUserId` — mirrors `ShiftCategory`; the Phase 1 plan dropped it to avoid a backfill sentinel)
Nav: `Molecule`, `List<ChoreType> ChoreTypes`, `List<UserChoreCategory> Members`. Unique `(MoleculeId, Name)`.

### `ChoreType` (modified)
Add: `int ChoreCategoryId` (FK **Restrict**, NOT NULL after backfill), `int? DefaultWeightMinutes`. Nav add `ChoreCategory`.

### `UserChoreCategory` (new — mirrors `UserShiftCategory`)
`Id, UserId(FK Cascade), ChoreCategoryId(FK Cascade)`. Unique `(UserId, ChoreCategoryId)`.

### `EligibilityRule` (new — global config, chore-scoped per `ChoreType`)
`Id, ChoreTypeId(FK Cascade), RuleKind(enum), GenderValue(Gender?), CreatedAt, CreatedBy(FK Restrict)`. Index `(ChoreTypeId)` non-unique (a type may carry several rules). No tenant filter. **Chore-specific by design — shifts do NOT use this table.**

### `UserChoreExemption` (new — per-person waiver)
`Id, UserId(FK Cascade), ChoreTypeId(FK Cascade), Reason?(≤200, sensitive), CreatedAt, CreatedBy(FK Restrict)`. Unique `(UserId, ChoreTypeId)`.

### `Chore` (modified)
Add: `int WeightMinutes` (migration default 240, frozen at create). All existing indexes (incl. the unique one-per-day) and the tenant query filter **unchanged**.

### `ChoreTemplate` (new — molecule-scoped definition)
`Id, MoleculeId(FK Restrict), Name, ChoreTypeId?(FK SetNull), DefaultTitle, StartTime?, EndTime?, WeightMinutesOverride?, Notes?, IsActive, CreatedAt, CreatedBy(FK Restrict)`. Index `(MoleculeId, IsActive)`. No tenant filter.

**`AppDbContext`:** add `DbSet`s + config blocks following the `ChoreType` block; none of the new config entities get a tenant query filter; only `Chore` (already filtered) gains a column.

---

## 5. Eligibility Evaluation

**Evaluator** — `Services/IEligibilityEvaluator.cs` + `EligibilityEvaluator.cs`, pure/stateless:

```csharp
EligibilityResult Evaluate(
    AppUser user,
    IReadOnlyList<EligibilityRule> rulesForSubject,
    bool userHasExemptionForSubject);
// EligibilityResult(bool IsEligible, string? FailKey)  FailKey ∈ {ELIG_GENDER, ELIG_OFFICER_RANK, ELIG_EXEMPT}
```

Logic: for each rule — `RequiresGender` → `user.Gender != rule.GenderValue` violates; `RequiresOfficerRank` → `!user.Rank.IsOfficer()` violates (reuses `MilitaryRankExtensions.IsOfficer`, `>= 9`). Then exemption → violates. The evaluator returns the violated kind so the caller can assign **severity by kind** (below). Else eligible.

**Severity by kind (UPDATED 2026-06-14):**
- `RequiresGender` violation (definite mismatch OR `Unspecified`) → **overrideable WARNING** (HMAC token), folded in with the existing vacation/shift/chore warnings.
- `RequiresOfficerRank` violation → **HARD ERROR** (not overrideable).
- Exemption (waiver) → **HARD ERROR** (not overrideable — you don't override a disability accommodation).

**Where gates apply** — `BusyService.ValidateChoreAsync` (`Services/BusyService.cs:178-307`). The two HARD gates (officer, exemption) insert **after** `USER_NOT_IN_MOLECULE` and **before** the warnings; the gender check joins the **warning** block:
1. Only when `target.ChoreTypeId.HasValue` (free-text → no rules → eligible). Batch-load the subject's `EligibilityRule` rows + exemption existence (two cheap queries in the already-`IgnoreQueryFilters()` method).
2. Officer/exemption violation → `ValidationIssue(failKey, …, Error, JobType)` + `BusyValidation(false, …)`. Gender violation → `ValidationIssue(ELIG_GENDER, …, Warning, JobType)` added to warnings (clears with a valid override token).

Chore validation order: `USER_NOT_FOUND → USER_INACTIVE → ACCOUNT_CANNOT_DO_CHORES → USER_NOT_IN_MOLECULE → [officer/exempt HARD] → vacation/shift/chore/on-duty/**gender** WARNINGS`. Membership (`DoesChores`/category) is a **roster-display** filter, not a validation gate.

**New resx (errors):** `Error_ChoreRequiresGenderMale`, `Error_ChoreRequiresGenderFemale`, `Error_ChoreRequiresOfficerRank`, `Error_ChoreUserExempt`, `Error_ChoreGenderUnspecified` (he + en).

---

## 6. Service & Assignment Flow Changes

- **`IChoreService`/`ChoreService`:** move interface to its own file (D8). `CreateChoreAsync` computes + writes `WeightMinutes` (D4) before persist; mirror the weight write in `ReplaceShiftWithChoreAsync`, `ChoreApiService.CreateChoreAsync`, `Controllers/Api/V1/ChoresController.cs`, `Pages/Public/Chores.cshtml.cs`, and any other create path. The eligibility gates fire automatically because `BusyTarget.Chore` already carries `choreTypeId` — **no signature change for eligibility**. New: `StampTemplateAsync(templateId, from, to, weekdays, assigneeIds)` (loops `CreateChoreAsync`, returns a per-(date,user) result summary), and `GetEligibilityForCandidateAsync(userId, choreTypeId)` for the picker.
- **`BusyService.ValidateChoreAsync`:** inject `IEligibilityEvaluator`; add the rule/exemption loads + evaluator call (§5).
- **Roster query (`Chores.cshtml.cs`):** D9 predicate; row grouping mirrors `BuildCategoryGroupedRowsAsync` over `ChoreCategory` + `UserChoreCategory`.
- **`JusticeService`:** chore `g.Count()` → `g.Sum(c => c.WeightMinutes)` in `CountActualPerUserAsync` and `GetSparklineSeriesAsync` chore branches; add optional `JusticeQuery.ChoreCategoryId` applied in those two chore queries; scale chore `ExpectedCount` by `DEFAULT_CHORE_WEIGHT_MINUTES`. Shift/on-duty paths byte-for-byte unchanged.
- **New services:** `IChoreCategoryService`/`ChoreCategoryService` (clone of `ShiftCategoryService`: CRUD, type↔category assign with `Restrict`, `GetUserCategoryIdsAsync`/`SetUserCategoriesAsync`), `IEligibilityEvaluator`/`EligibilityEvaluator`, `IChoreEligibilityAdminService` (rule + exemption CRUD, gated by `EditChoreTypes`). Shared `Services/DurationFormat.cs` (`FormatMinutes`, `FormatHours`) — single definition consumed by admin/calendar/justice.
- **DI (`Program.cs`):** register the new scoped services.

---

## 7. UI / UX Design

> Decisions below are the binding summary. Razor sketches, exact handler names, and the full bilingual loc-key tables are produced during implementation (Phase 3/5) by cloning the cited existing files; the loc keys are enumerated in the implementation plan, not duplicated here.

- **§7.1 ChoreType + Category admin** — **extend** `Pages/Admin/Organization/ChoreTypes/Index` (gated by `Grant:EditChoreTypes`): a top "Chore Categories" section-card (inline CRUD, color dot, sort, bilingual names) + the "Chore Types" section reorganized as a **category accordion**. The type editor gains a **Category dropdown**, a **DefaultWeight** dual input (hours+minutes → minutes), and an **Eligibility** fieldset (gender radio none/male/female + officer-rank toggle, persisted as `EligibilityRule` rows via replace-semantics). Eligibility shows as **reason chips** in the table.
- **§7.2 Exemptions** — managed on the **ChoreType editor** (type-centric mental model), an AJAX user-picker + optional reason list. Audit logs **never** include the reason text (sensitive).
- **§7.3 `Admin/Users` additions** — clone the `does-shifts-cell`: a **DoesChores** toggle + **chore-category multiselect** (clone `OnPostUserCategoriesAsync`), and a **Gender** cell = badge + Edit button visible to **any user-editor** (UPDATED 2026-06-14 — no `EditUserGender` grant). Gender dialog is 3-state, gated by the existing `AuthorizeUserEditAsync`, audited.
- **§7.4 `ChoreTemplate` admin + Stamp** — **new page** `Pages/Admin/Organization/ChoreTemplates/Index` (clone the ChoreTypes skeleton, `Grant:EditChoreTypes`). The **Stamp modal**: pick date range (+ weekday chips) + assignee(s), submit → `OnPostStampAsync` loops `CreateChoreAsync`, then swaps to a **result view**: `{created} created / {skipped} skipped` with a per-(date,user) skip list showing the localized eligibility reason.
- **§7.5 Chores calendar** — roster becomes **category-accordion grouped** with **mirrored rows** (clone `BuildCategoryGroupedRowsAsync`); `data-row-group-id="chorecategory-{id}"`. The assignment picker surfaces blocked candidates **greyed + reason chip** (reuse the Justice drawer's existing `hardBlockReason`/`warnings` JSON via a shared `renderEligibleCandidate(c)` helper used by both the drawer and the bottom sheet).
- **§7.6 Fairness UI** — add a **ChoreCategory selector** to the drawer + `/Admin/Analytics`; absolute actual/expected render as **hours** (`DurationFormat.FormatHours`); deviation/spread/banding/sparkline are deviation-driven → unchanged. A `Justice_TargetUnitNote` clarifies "targets in chore-equivalents, load in weighted hours."
- **§7.7 Contrast/RTL** — category color is always a **dot/accent**, never a text background. Fixed semantic palettes for `.elig-chip` and `.gender-badge` (muted, bordered — sensitive ≠ alarming) pair colored backgrounds with explicit light/dark text + `!important`, reusing existing `--*-soft`/`--warning-text` tokens (no new tokens). Dark-mode overrides redeclare **only** `color` (preserves RTL flips per the project's RTL invariant). Mandatory pre-ship visual pass on **populated** data in light/dark/RTL.

---

## 8. Migration & Back-Compat (ordered)

1. **Code:** extract `IChoreService`; add model files/enums; add new props to `AppUser`/`ChoreType`/`Chore` (`ChoreCategoryId` **nullable** first).
2. **Migration #1 (additive):** create `ChoreCategory`, `UserChoreCategory`, `EligibilityRule`, `UserChoreExemption`, `ChoreTemplate`; add `AppUser.Gender`(0)/`DoesChores`(0), `ChoreType.ChoreCategoryId`(null)/`DefaultWeightMinutes`(null), `Chore.WeightMinutes`(**480**). Verify the `Chore` unique index is byte-identical. (Planning note: `ChoreCategoryId` stays nullable + `SetNull` — Migration #2 dropped; see the Phase 1 plan.)
3. **Backfill (idempotent):** (a) per molecule with ≥1 type → insert "General"/"כללי" category; (b) `ChoreTypes.ChoreCategoryId = General`; (c) `Chores.WeightMinutes` from `StartTime/EndTime` where both present; (d) `AppUsers.DoesChores = 1 WHERE IsActive AND AccountType = 0`.
4. **Migration #2 (tighten):** `ChoreType.ChoreCategoryId → NOT NULL` (FK Restrict).
5. **Grants:** **none** (decided 2026-06-14 — `EditUserGender` dropped). No `GrantTypeSeed`/`RoleTemplateSeed`/`RoleTemplateAutoGrantTests` changes.
6. **Targets:** no `JusticeTarget` data migration (chore-equivalent scaling is in code, ×480).

Invariants: categories before type-backfill; nullable→backfill→NOT NULL; never reorder the grant append.

---

## 9. Grant Changes — NONE (decided 2026-06-14)

This feature adds **no grants** and touches `GrantTypeSeed`/`RoleTemplateSeed`/`RoleTemplateAutoGrantTests` **zero** times. The grant-change checklist does not apply.

**Reused (no change):** `EditChoreTypes` #18, `CreateChoreTypes` #19 (now also gate ChoreCategory/EligibilityRule/exemption/template admin — re-verify `MoleculeId` for IDOR on every handler); `AssignChores` #17 (assignment + stamping); `ViewJusticeTable` #133 / `EditJusticeTargets` #134 (weighted fairness). **Gender view+edit** and the `DoesChores`/category-membership/exemption handlers all ride the **existing** user-edit authorization (`AuthorizeUserEditAsync` — AdminAccess / EditCompanyUsers). The gender value change is audited like any other `AppUser` mutation.

---

## 10. Test Surface

1. `EligibilityEvaluatorTests` (pure): gender male/female × {match → eligible; mismatch → violation(RequiresGender); Unspecified → violation(RequiresGender)}; officer × {officer pass; enlisted → violation}; exemption → violation; multi-rule; no rules → eligible. (Severity mapping lives in BusyService, not here.)
2. `BusyServiceChoreEligibilityTests` (real SQLite): ordering; free-text bypasses eligibility; **officer/exemption = hard errors, NOT clearable** by override token; **gender = warning, IS clearable** by a valid override token (definite mismatch and Unspecified alike).
3. `ChoreWeightTests`: resolution order (times→type default→480); free-text→480; frozen at create.
4. `JusticeChoreWeightingTests`: equal counts/unequal duration → unequal weighted Actual; sparkline sums minutes; category filter narrows; target scaling matches.
5. `ChoreRosterTests`: D9 predicate includes DoesChores/category Standard, excludes Standard-with-neither + Mil/GroupUser; backfill makes existing Standard appear.
6. `ChoreCategoryServiceTests`: CRUD, `(MoleculeId,Name)` unique, cross-molecule reject, `SetUserCategoriesAsync`, type→category `Restrict`.
7. `ChoreTemplateStampTests`: one chore/day/assignee; hard-error day skipped+reported; one-per-day index respected; weight resolved.
8. *(removed — no new grant, so no `RoleTemplateAutoGrantTests` change.)*
9. `UserGenderEditAuthzTests`: gender editable by any user-editor (rides `AuthorizeUserEditAsync`); audited; never in claims/public DTOs. Gender mismatch is a warning, overridable.
10. Migration/backfill test: General per molecule-with-types; types reassigned; weights backfilled; Standard→DoesChores; unique index intact.

Run full suite **sequentially** (`-- xUnit.ParallelizeTestCollections=false`) per the project's :memory: SQLite-contention note.

---

## 11. Build Sequence

- **Phase 0 — Convention + scaffolding:** extract `IChoreService`; add enums.
- **Phase 1 — Data model:** new entities; `AppUser`/`ChoreType`/`Chore` props; `AppDbContext` config; Migration #1 + backfill + Migration #2.
- **Phase 2 — Services:** `EligibilityEvaluator` (+tests); `BusyService` gates + DI; `ChoreCategoryService`; `IChoreEligibilityAdminService`; `ChoreService` weight + stamp + eligibility helper; `DurationFormat`; `Program.cs` DI.
- **Phase 3 — Admin UI:** extend `ChoreTypes/Index` (categories, weight, rules, exemptions); `Admin/Users` (DoesChores + category multiselect + gender); `ChoreTemplates/Index` + stamp.
- **Phase 4 — Calendar:** roster predicate + category-grouped mirrored rows; eligibility block reasons in the picker.
- **Phase 5 — Fairness:** `JusticeService` weighted sums + `ChoreCategoryId`; drawer + `/Admin/Analytics` category selector + hours display.
- **Phase 6 — Tests + docs:** **no grant changes**; all test suites; full sequential run; MEMORY/docs.

---

## 12. Risks / Shifts-Touching

- `EligibilityRule` is **chore-scoped (per `ChoreType`) by design** — shifts do NOT use it. `ShiftType.RequiresOfficerRank` + `ValidateShiftAsync` untouched; shifts keep their own officer-rank flag (no future-shift shaping).
- `JusticeService` is shared shift/chore code; the only edits are confined to the **chore branches** + a **nullable** `JusticeQuery.ChoreCategoryId` (null = current behavior). Verify existing `JusticeService*Tests` stay green.
- Roster-grouping reuses shift **patterns** in the chores page only; no shift page edits.
- Gender + exemption `Reason` are the most sensitive PII the app holds; stored plaintext (consistent with `DateOfBirth`/`Phone`; the app is air-gapped on-prem). Field-level encryption is a separate cross-cutting effort, out of scope unless requested.
- Chores crossing midnight (`EndTime < StartTime`) are out of scope for weight math (assumed same-day).

---

## 13. Resolved Decisions (user, 2026-06-14)

All previously-open product/privacy decisions are now settled:

1. **Stamp multi-assignee** → **manager chooses per stamp** (a toggle in the stamp dialog: *rotate one-per-day* vs *everyone-every-day*). Stamping is **additive** — the existing one-at-a-time manual assignment stays unchanged; stamping is an optional bulk shortcut over it.
2. **DoesChores OFF** → **prompt keep-or-cancel** (mirror the existing DoesShifts confirm-dialog, showing the future-chore count).
3. **Gender mismatch** → **overrideable WARNING everywhere** (both a definite mismatch and the `Unspecified` case). No fail-closed hard block for gender. Officer-rank + waiver remain hard.
4. **Gender visibility** → **visible to any user-editor** (no separate view gate).
5. **Gender editing** → **rides existing user-edit authz, no new grant**. `EditUserGender` #137 is dropped; the feature adds **zero** grants.
6. **Fallback weight** → **fixed 480 min (8 hours)** constant (`DEFAULT_CHORE_WEIGHT_MINUTES = 480`); not per-molecule configurable.
7. **Gender at-rest** → **plaintext** (consistent with `DateOfBirth`/`Phone`); no field-level encryption this cycle. Value change is audited.
8. **Hebrew strings** → route the new resx keys through a `localization-qa` pass during implementation (not a design blocker).

---

## 14. Deferred Items

- `EligibilityRule` is chore-scoped by design; shifts keep `ShiftType.RequiresOfficerRank` and do NOT use this table (no future-shift shaping).
- **Midnight-crossing chore duration** — out of scope; a chore with `EndTime < StartTime` keeps the fallback weight; flagged for a wrap rule if such chores ever exist.

Resolved (no longer deferred): gender at-rest encryption → **plaintext, decided** (§13.7); all §13 product/privacy questions → **decided**.
