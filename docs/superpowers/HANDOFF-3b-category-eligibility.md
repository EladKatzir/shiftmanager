# Handoff — Sub-Project 3b: Category-Based Shift Eligibility

> Paste this whole document as the opening prompt of a fresh session. It is self-contained.

You are picking up **sub-project 3b** on the ShiftManager codebase. The design is **finalized and committed**; your job is to write the implementation plan and execute it. Do NOT redo the design — read the spec, confirm the decisions, plan, build with TDD.

---

## 0. Repo facts you need immediately

- **Repo root:** `C:\Users\katzi\Downloads\ShiftManager` (ASP.NET Core 8 Razor Pages, EF Core + SQLite, multi-tenant, deployed air-gapped on Windows/IIS).
- **Branch:** `dev`. **HEAD at handoff:** `1b24f68`. Full test suite green: **1758/1758**.
- **Run tests SEQUENTIALLY** (parallel `:memory:` SQLite runs produce ~220 spurious contention failures):
  `dotnet test "ShiftManager.Tests/ShiftManager.Tests.csproj" -- xUnit.ParallelizeTestCollections=false`
- **Build-lock rule:** if a rebuild fails with `ShiftManager.exe ... used by another process`, stop the running dev app first:
  `Get-Process -Name ShiftManager | Where-Object { $_.Path -like '*Downloads\ShiftManager*' } | Stop-Process -Force`
- **NEVER hand-edit `FinalProductPublish/**`** — it is generated; regenerate via `scripts/Update-FinalProductPublish.ps1` (needs a mandatory `-Version`; invoke non-interactively as `powershell -ExecutionPolicy Bypass -Command "& { $ConfirmPreference='None'; & ./scripts/Update-FinalProductPublish.ps1 -Version '<x.y.z>' }"`). This is a deploy step — ask the user for the version; do not invent it.
- **Use TDD** (superpowers:test-driven-development) for every code change: failing test first, watch it fail, minimal code, watch it pass, commit. Frequent small commits.
- A peer session may occasionally touch `dev`; if the working tree is mid-edit or HEAD advances unexpectedly, pause and reconcile before committing.

## 1. What 3b delivers (the user's actual goal)

The user's words: *"limit the dropdown list (and autocomplete when in quick entry) results when assigning users — only users with Alhut jobtype should appear when assigning an Alhut shift, otherwise we scroll forever / get confused by similar names."*

3b makes the **assignable-user candidate list** — shown in three UIs (desktop dropdown, mobile bottom-sheet, quick-entry autocomplete) — driven by the modern **`DoesShifts` + `ShiftCategory` membership** model instead of the legacy job-type filter.

**Eligibility rule (canonical):** a user is eligible for shift X **iff** `IsActive && AccountType != GroupUser && DoesShifts && (X.CategoryId == null ? true : member-of(X.CategoryId))`, composed inside the grouping-derived company set, with officer-rank preserved for tech shifts.

This is the candidate-FILTERING half. The permission half (one `AssignShifts` grant) was already shipped as sub-project 3a — do not touch authorization.

## 2. Read these first (in order)

1. **Spec (authoritative, has all decisions):** `docs/superpowers/specs/2026-06-14-category-based-shift-eligibility-design.md` — especially **§6R** (10 resolved decisions from architect + UI-designer consults) and the **loc-key table** + **ordering** note.
2. **Memory file:** `C:\Users\katzi\.claude\projects\C--Users-katzi-Downloads-ShiftManager\memory\shift_assign_grant_and_eligibility_2026-06-14.md`.
3. **The canonical eligibility expression already in the codebase** (copy this exact shape): `Services/JusticeService.cs:214-218` — `u.DoesShifts && _db.UserShiftCategories.Any(m => m.UserId == u.Id && m.ShiftCategoryId == catId)`.
4. **The backfill pattern to mirror:** `Migrations/ShiftCategoryBackfillSql.cs` — note lines 12-13, 28, 48: category `Name` is stamped `"<DisplayName> [<sourceShiftTypeId>]"`, which is the deterministic reverse-map you'll use to backfill `ShiftType.CategoryId`.

## 3. The blocker you must solve FIRST (do not skip)

`ShiftType.CategoryId` is **nullable and mostly NULL**. The original category backfill created categories and mapped *users* into them, but **never set `ShiftType.CategoryId`**. If you flip eligibility to category membership without backfilling `CategoryId`, every uncategorized shift (most of them, plus shared HOME/OFFLINE) returns **zero candidates** — an empty assign picker, worse than today.

So the ordering is **load-bearing**: backfill `ShiftType.CategoryId` (Phase 1) and verify it **before** the eligibility rule is enabled (gated behind a default-off feature flag).

## 4. The 10 finalized decisions (from spec §6R — summarized; read the spec for full text)

1. **Null-CategoryId fallback:** shifts with `CategoryId == null` (HOME/OFFLINE/shared null-jobType) → eligible = **all `DoesShifts` users in the molecule**. Do NOT reintroduce the jobType filter; do NOT force a catch-all category.
2. **`ShiftType.CategoryId` backfill:** new idempotent migration `BackfillShiftTypeCategoryId` (mirror `ShiftCategoryBackfillSql` via a shared constants class). Derive `CategoryId` by matching `ShiftCategory.Name`'s `"[id]"` tag back to the exact shift type. HOME/OFFLINE and never-primary types stay null (resolve via decision 1's fallback). Do NOT derive from JobType (ambiguous); do NOT synthesize new categories.
3. **`DoesShifts` source:** read **per-company `CompanyMembership.DoesShifts`** for the (user, candidate-company); fall back to mirrored `AppUser.DoesShifts` only when no membership row exists. Materialize a `participantUserIds` set once and `Contains(...)` rather than a per-row correlated `.Any()`. (Note the intended behavior change: a user `DoesShifts=false` on their primary company but `true` on a secondary molecule membership IS now assignable — call this out in rollout.)
4. **`ShiftGrouping`:** KEEP it as a company pre-filter (orthogonal axis). Resolve candidate companies from the grouping first, then apply DoesShifts + category within that set. `EligibleUserDto.IsInShiftGrouping` stays.
5. **Endpoint:** generalize `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs` into the single per-shift endpoint all 3 UIs call. Dispatch by shift kind to the **two preserved service methods** (do NOT merge their bodies): workforce → `ShiftAssignmentService.GetEligibleUsersForShiftTypeAsync`; tech → `ShiftCalendarService.GetEligibleUsersForShiftTypeAsync` (add the category gate to both; keep officer-rank on tech). Keep the endpoint's molecule-scope authz (`ValidateScopeAccessAsync` + the shiftType-belongs-to-molecule guard). **Behavior-preserving first**, then switch via the flag.
6. **Rollout:** feature flag **`CategoryBasedShiftEligibility`**, resolved at **company** level (`IFeatureFlagService.IsEnabledAsync(flag, userId: null, companyId: current)`), **default OFF**. Flag read inside the service router (off → legacy jobType branch, on → category rule) so the 3 UIs need no mode branching. Retiring the legacy branch + removing the flag is explicitly OUT of 3b (post-rollout cleanup).
7. **Zero/empty UX:** distinguish **structural zero** (category empty / no category) from **filter zero** (typed text matches nobody). Three non-`role=option` rows (so arrow-nav + Enter skip them) driven by an endpoint `reason` field (`"category" | "sharedFallback" | "noCategory"`). The fallback case renders normally (it's the designed path, no warning). `aria-live` announces the structural-zero cause, not "0".
8. **Disambiguation line** (the real "similar names" fix): muted secondary line shows **company name** + a **busy-on-date glyph** (reuse `calendar-bottom-sheet.js`'s `GetBusyStates` vocabulary 🏠📴⏱🧹🛡🌴). **Omit category name** (redundant once filtered by it). Carry `hasHardError` → exclude hard-conflicted users from Enter auto-commit.
9. **Cross-UI consistency:** one projection `{ id, name, companyName, reason }`; busy stays a SEPARATE `GetBusyStates` call (never cached). Empty-query order = `DisplayName` for all 3 UIs. Drop the desktop dropdown's job-type label. Native `<select>` gets company as a ` — {company}` suffix; only quick-entry gets the two-line treatment.
10. **Admin affordance:** a `.badge-warning` "No category" on shift-type management (Blueprints) for assignable types with null `CategoryId`, + the endpoint `reason:"noCategory"` powers the end-user row. OUT of 3b: bulk-categorize tool + save-time validation (→ Category & Roster admin phase).

## 5. Quick-entry requirements (UI-designer review, all accepted)

- Fetch the per-shift eligible list **on cell focus** (not first keystroke); **two-tier cache** — eligibility cached per `molecule:shiftType` for the session; per-date busy metadata **never** cached across an assignment (invalidate on the `calendar:grid-refreshed` event quick-entry already listens to).
- **No silent fallback:** on fetch failure, fail **visibly** (don't silently revert to the unfiltered page list — that re-creates the bug). Server re-validates on assign anyway.
- **Buffer the Enter-commits-top-match** during the load window (else a fast typist commits against a stale/empty list).
- New states as non-`role=option` rows: loading / zero-eligible / no-category / load-failed.
- A11y: `aria-busy` on the combobox during fetch + a visually-hidden `aria-live="polite"` status region announcing loading / result count / empty / fallback.

### New loc keys to add (EN + HE) — `QuickEntry_*` convention; `Admin_ShiftType_*` for the badge
| Key | EN | HE |
|---|---|---|
| `QuickEntry_Loading` | Loading eligible users… | טוען עובדים זמינים… |
| `QuickEntry_NoEligibleUsers` | No eligible users for this shift | אין עובדים זמינים למשמרת זו |
| `QuickEntry_NoEligibleUsersHint` | No one is a member of this shift's category | אף עובד אינו משויך לקטגוריית המשמרת |
| `QuickEntry_NoCategorySet` | This shift has no category set | למשמרת זו לא הוגדרה קטגוריה |
| `QuickEntry_NoCategorySetHint` | Ask an admin to assign a category | פנו למנהל להגדרת קטגוריה |
| `QuickEntry_NoTextMatch` (`{0}` in `<bdi>`) | No eligible users match "{0}" | אין עובדים זמינים התואמים ל-"{0}" |
| `QuickEntry_LoadFailedFallback` | Couldn't load eligible users — try again | טעינת העובדים נכשלה — נסו שוב |
| `QuickEntry_ResultsAvailable` (`{0}`=count, aria-live) | {0} eligible users | {0} עובדים זמינים |
| `Admin_ShiftType_NoCategory_Badge` | No category | ללא קטגוריה |
| `Admin_ShiftType_NoCategory_Tooltip` | Users can't be assigned to this shift until it has a category | לא ניתן לשבץ עובדים למשמרת זו עד שתוגדר לה קטגוריה |

> Keep existing `QuickEntry_NoMatches` for non-eligibility groups (chores/duty/slash palette). Only the user/shift-assignment group adopts the richer triad. **ALWAYS grep a resx key before adding** — a duplicate key breaks ~6 localization tests.

## 6. Key files (verified present at handoff)

- `Services/ShiftAssignmentService.cs` — `GetEligibleUsersForShiftTypeAsync` (~line 63, workforce; filters jobType + ShiftGrouping). **The method to evolve.**
- `Services/ShiftCalendarService.cs` — `GetUsersForCalendarAsync` (~29, the legacy page-list source) + tech `GetEligibleUsersForShiftTypeAsync` (~242, EligibleCompanyIds + officer rank).
- `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs` — the per-shift endpoint to generalize (currently tech-only in practice; projects `{id, name}` — extend to `{id, name, companyName, reason}`).
- `Pages/Calendar/Shifts.cshtml` + `.cs` — page-level `[data-role="assignee-select"]` hidden `<select>` built from `Model.Users` (= `GetUsersForCalendarAsync`). Quick-entry reads this select today.
- `wwwroot/js/calendar-quick-entry.js` — `loadItems` (~136), `filterAndGroup` (~189), combobox/aria wiring (~600+), Enter top-match (~720+).
- `wwwroot/js/calendar-bottom-sheet.js` — `getAvailableUsers` (page-list fallback), `decorateOptionsWithBusyAsync` (the busy-glyph vocabulary to reuse for the disambiguation line).
- Models: `AppUser.DoesShifts`, `CompanyMembership.DoesShifts` (per-company), `ShiftCategory`, `UserShiftCategory` (N:N), `ShiftType.CategoryId` (nullable, mostly null).
- `Services/IFeatureFlagService.cs` — `IsEnabledAsync(flagName, userId?, companyId?)` resolves User+Company → Company → Global.
- `Resources/SharedResources.resx` + `SharedResources.he-IL.resx` — `QuickEntry_*` block for new keys.

## 7. Suggested phase order (write a real plan with superpowers:writing-plans before coding)

1. **Phase 1 — `ShiftType.CategoryId` backfill** (migration + shared SQL constants + idempotency test). MUST land + verify before anything flips. Define the HOME/OFFLINE/never-primary null behavior.
2. **Phase 2 — Eligibility query** (evolve the two service methods to the category rule behind the flag; behavior-preserving when flag off). Unit-test both branches: workforce category filter, tech category + officer rank, null-category fallback, per-company `DoesShifts`.
3. **Phase 3 — Unified endpoint** (router over the two methods; extend projection to `{id, name, companyName, reason}`; preserve the molecule-scope authz). Behavior-preserving first.
4. **Phase 4 — Wire all 3 UIs** to the endpoint (dropdown, bottom-sheet, quick-entry).
5. **Phase 5 — Quick-entry overhaul** (fetch-on-focus, two-tier cache, states, a11y, loc keys, disambiguation line, Enter buffering, no silent fallback).
6. **Phase 6 — Admin "no category" badge** + the feature flag seed (`CategoryBasedShiftEligibility`, default off).
7. **Phase 7 — Tests + docs + MEMORY + FinalProductPublish regen (ask user for version)**.

## 8. Definition of done

Eligibility for **all three** assign UIs is `DoesShifts` + category-membership driven (behind the per-company flag); `ShiftType.CategoryId` is backfilled (or the defined fallback covers gaps); quick-entry has the fetch/cache/states/a11y/disambiguation; no silent fallback; bilingual loc complete; full suite green sequentially; the eligibility behavior change ships behind the default-off flag so it can be enabled per-company after each company's category data is verified.

## 9. Gotchas (learned this session)

- `PrimaryShiftTypeId` is fully removed — don't reference it (except reading it inside the migration's backfill SQL, as the original did).
- `DoesShifts` lives on BOTH `AppUser` and `CompanyMembership`; today it drives only calendar grouping + leave fan-out, never eligibility — you're adding the eligibility consumer.
- `ShiftCategory`/`UserShiftCategory`/`ShiftType` are **molecule-scoped, NOT tenant-filtered** — every query path must use `IgnoreQueryFilters()` and re-scope by molecule/company (carry a `SECURITY-AUDITED` comment), exactly like `ShiftCalendarService`/`JusticeService`. The grouping company-set bound is what keeps it molecule-local.
- Keep the two eligibility service methods SEPARATE behind one endpoint — merging them is the tangle the architect warned against.
- A category with zero members yields a legitimately empty candidate list — that's the structural-zero UX, not a bug. Server re-validates on assign regardless.
- `ShiftType.CategoryId` FK is `SetNull` on category delete — treat a re-nulled CategoryId as "uncategorized" (falls into the fallback), not an error.
