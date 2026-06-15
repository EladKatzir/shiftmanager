# Category-Based Shift Eligibility (candidate-list redesign) — Scope & Design Intent

**Date:** 2026-06-14 (design finalized 2026-06-15) · **Branch:** `dev` · **Status:** **DESIGN FINALIZED** via architect + UI-designer consults; pending implementation plan. The open questions (§6) are now resolved decisions (§6R).

**Paired sub-project (BOTH required — "we need both done"):** [Shift-assignment grant collapse](2026-06-14-shift-assign-grant-collapse-design.md) (3a). 3a = authorization; this (3b) = candidate filtering. Do 3a first; 3b depends on nothing in 3a but must follow.

---

## 1. Goal (owner intent)

The assignable-user list (assign dropdown, mobile bottom-sheet, **and** quick-entry autocomplete) should be driven by the **modern Category & Roster model**: a user is an eligible assignee for shift X **iff `DoesShifts == true` AND the user is a member of X's `ShiftCategory`** (via `UserShiftCategory`). This replaces the legacy job-type filter and stops the "endless / confusing similar-names" list.

## 2. Why this is its own sub-project (the critical finding from the code map)

The data model is **in place and backfilled** (`AppUser.DoesShifts` + `CompanyMembership.DoesShifts`; `ShiftCategory`; `UserShiftCategory` N:N; `ShiftType.CategoryId`; EF config), and the redesign already wired it into **calendar grouping** and **leave fan-out** — but **eligibility was never switched over**. It is still 100% job-type/company-based:

- `ShiftCalendarService.GetUsersForCalendarAsync` → molecule + page-level `JobTypeId`, one flat list for the whole page (the real workforce source).
- `ShiftAssignmentService.GetEligibleUsersForShiftTypeAsync` → molecule/grouping companies + `IsActive` + `JobTypeId`.
- `ShiftCalendarService.GetEligibleUsersForShiftTypeAsync` (Tech) → molecule + `EligibleCompanyIds` + officer rank (wired only to the bottom-sheet).

**The blocker:** `ShiftType.CategoryId` is **nullable and mostly unpopulated** — the category backfill created categories and mapped *users*, but **never set `ShiftType.CategoryId`** (it's opt-in per shift type via the admin UI). Flipping eligibility to "members of the shift's category" **today** would make any shift with a null `CategoryId` (most of them, plus shared HOME/OFFLINE) match **zero** candidates. So a data backfill + a shared-shift fallback rule are prerequisites.

## 3. Scope of work (the complete list — so nothing is half-done)

1. **Data backfill:** populate `ShiftType.CategoryId` for all assignable shift types; define + implement a fallback for intentionally-uncategorized shared shifts (HOME / OFFLINE / null-jobType shared types).
2. **Eligibility rule switch:** rewrite the eligible-users query to `IsActive && DoesShifts && member-of(shiftType.CategoryId)` (read `DoesShifts` from `CompanyMembership` per-company where applicable, not only the mirrored `AppUser` copy). Keep workforce vs tech as distinct internal branches if their rules genuinely differ.
3. **Unified per-shift endpoint:** one router endpoint over the two service methods (do **not** merge the method bodies). **Behavior-preserving first** (return today's set), then switch to category rule — so the dropdown/bottom-sheet change is a reviewed, deliberate step, not a silent regression.
4. **Wire all 3 UIs** to the unified endpoint: dropdown, bottom-sheet, and **quick-entry** (`calendar-quick-entry.js`).
5. **Quick-entry behavior (UI-designer review, P0/P1):**
   - Fetch the per-shift eligible list on **cell focus** (not first keystroke); **two-tier cache** — eligibility per `molecule:shiftType` for the session; per-date busy metadata **never** cached across an assignment (invalidate on `calendar:grid-refreshed`).
   - **No silent fallback:** on failure, fail **visibly** (don't silently revert to the unfiltered page list, which re-creates the bug); server re-validates on assign anyway.
   - **Buffer the Enter-commits-top-match** during the load window (else a fast typist commits against a stale/empty list).
   - **New states** as non-`role=option` rows (so arrow-nav + Enter skip them): loading / zero-eligible / fallback-error.
   - **A11y:** `aria-busy` on the combobox during fetch + a visually-hidden `aria-live="polite"` status region announcing loading / result count / empty / fallback.
   - **Loc keys (EN + HE, `QuickEntry_*` convention):** `QuickEntry_Loading`, `QuickEntry_NoEligibleUsers`, `QuickEntry_LoadFailedFallback`, `QuickEntry_ResultsAvailable` ("{0} eligible users"). Verify RTL.
6. **Disambiguation (owner approved — the actual "similar names" fix):** muted secondary line on quick-entry items showing **company** (extend the endpoint projection) + a **busy-on-this-date badge** (reuse `GetBusyStates`). Carry `hasHardError` and **exclude hard-conflicted users from Enter auto-commit** (don't auto-select them).
7. **Consistency:** empty-query ordering falls back to `DisplayName` to match the bottom-sheet.

## 4. Affected code (from the map)

- `Services/ShiftCalendarService.cs` (`GetUsersForCalendarAsync` ~29; Tech `GetEligibleUsersForShiftTypeAsync` ~242)
- `Services/ShiftAssignmentService.cs` (`GetEligibleUsersForShiftTypeAsync` ~63)
- `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs` (the per-shift endpoint to generalize)
- `Pages/Calendar/Shifts.cshtml(.cs)` (page-level `[data-role="assignee-select"]`, `Model.Users`)
- `wwwroot/js/calendar-quick-entry.js` (`loadItems`, `filterAndGroup`, combobox/aria, Enter top-match, `openInput`) + `calendar-bottom-sheet.js` (`getAvailableUsers`, `decorateOptionsWithBusyAsync`)
- Models: `AppUser.DoesShifts`, `CompanyMembership.DoesShifts`, `ShiftCategory`, `UserShiftCategory`, `ShiftType.CategoryId`
- A new migration/backfill for `ShiftType.CategoryId`

## 5. Dependencies / sequencing

- Sequenced **after 3a** (no code dependency, but avoids two concurrent grant/calendar changes colliding).
- Coordinates with the in-progress **Category & Roster Redesign** (phases B–G). This sub-project effectively completes that redesign's **eligibility layer**.

## 6R. Resolved design decisions (architect + UI-designer consult, 2026-06-15)

**Eligibility rule (canonical, copy from `JusticeService`):** `u.IsActive && u.AccountType != GroupUser && DoesShifts && (shiftType.CategoryId == null ? true : UserShiftCategories.Any(m => m.UserId == u.Id && m.ShiftCategoryId == shiftType.CategoryId))`, composed **inside** the grouping-derived company set, with officer-rank preserved for tech.

1. **Shared-shift fallback (arch D1):** `CategoryId == null` (HOME/OFFLINE/shared null-jobType) → **all `DoesShifts` users in the molecule** (company set). Do NOT reintroduce the jobType filter; do NOT force a catch-all category.
2. **`ShiftType.CategoryId` backfill (arch D2):** new idempotent migration `BackfillShiftTypeCategoryId` (mirror `ShiftCategoryBackfillSql` in a shared constants class). Derive `CategoryId` by matching `ShiftCategory.Name` (which the original backfill stamped as `"<DisplayName> [<sourceShiftTypeId>]"`) back to the exact shift type — deterministic, same edge the user-mapping used. Genuinely-shared types (HOME/OFFLINE) and never-primary types stay null and resolve via the D1 fallback. Do NOT derive from JobType (ambiguous); do NOT synthesize new categories.
3. **`DoesShifts` source (arch D3):** read **`CompanyMembership.DoesShifts` per (user, candidate-company)**; fall back to the mirrored `AppUser.DoesShifts` only when the user has no membership row in the candidate companies. Materialize `participantUserIds` once and `Contains(...)` rather than a per-row correlated `.Any()`. (A user `DoesShifts=false` on primary but `true` on a secondary molecule membership IS assignable — intended behavior change, call out in rollout.)
4. **`ShiftGrouping` (arch D4):** KEEP — orthogonal axis. Resolve candidate **companies** from the grouping first (existing `ShiftAssignmentService` 84–106 logic), then apply DoesShifts + category **within** that set. `EligibleUserDto.IsInShiftGrouping` stays.
5. **Endpoint (arch D5):** generalize `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs` into the single per-shift endpoint all 3 UIs call; dispatch to the **two preserved service methods** (workforce `ShiftAssignmentService`, tech `ShiftCalendarService` — add the category gate to both, keep officer-rank on tech). Keep its molecule-scope authz (`ValidateScopeAccessAsync` + shiftType-belongs-to-molecule). **Behavior-preserving first**, switch via the flag below.
6. **Rollout (design D1):** feature flag **`CategoryBasedShiftEligibility`**, resolved at **company** level (`IsEnabledAsync(flag, userId:null, companyId:current)`), default **off**. Flag read inside the service router (off→legacy jobType branch, on→category rule) so the 3 UIs need no mode branching. Sequence: ship behavior-preserving → run backfill → per-company verify+enable → later (out of 3b) collapse to global + retire legacy branch.
7. **Zero/empty UX (design D2):** distinguish **structural zero** (category empty / no category) from **filter zero** (text matches nobody). Three non-`role=option` rows driven by an endpoint `reason` field (`"category"|"sharedFallback"|"noCategory"`). Fallback (sharedFallback) is the designed path — render normally, no warning. `aria-live` announces the structural-zero cause, not "0".
8. **Disambiguation line (design D3):** show **company name** + **busy-on-date glyph** (reuse `GetBusyStates` vocabulary 🏠📴⏱🧹🛡🌴). **Omit category name** (redundant once filtered by it). Carry `hasHardError` → exclude from Enter auto-commit.
9. **Cross-UI consistency (design D4):** one projection `{ id, name, companyName, reason }`; busy stays a SEPARATE `GetBusyStates` call (never cached). Empty-query order = `DisplayName` for all 3. **Drop the desktop dropdown's job-type label** (no longer the filter). Native `<select>` gets company as ` — {company}` suffix; only quick-entry gets the two-line treatment.
10. **Admin affordance (design D5):** IN SCOPE — a `.badge-warning` "No category" on shift-type management for assignable types with null `CategoryId` (+ the endpoint `reason:"noCategory"` powers the end-user row). OUT of 3b: bulk-categorize tool + save-time validation (→ Category & Roster admin phase).

### New loc keys (EN + HE, `QuickEntry_*` convention; `Admin_ShiftType_*` for the badge)
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

> Keep existing `QuickEntry_NoMatches` for the non-eligibility groups (chores/duty/slash); only the user/shift-assignment group adopts the richer triad.

### Ordering (load-bearing)
The `CategoryId` backfill migration MUST run + be verified before the `CategoryBasedShiftEligibility` flag is enabled for a company — else categorized-but-unbackfilled shifts return zero candidates. The default-off flag protects against an accidental early flip.

## 7. Definition of done (so 3b is not "half done")

Eligibility for **all three** assign UIs is `DoesShifts` + category-membership driven; `ShiftType.CategoryId` is fully populated (or a defined fallback covers the gaps); quick-entry has the fetch/cache/states/a11y/disambiguation above; no silent fallback; bilingual loc complete; tests cover the new eligibility rule + the backfill + quick-entry states.
