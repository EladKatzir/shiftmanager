# Category-Based Shift Eligibility (candidate-list redesign) — Scope & Design Intent

**Date:** 2026-06-14 · **Branch:** `dev` · **Status:** **SCOPE CAPTURED — design NOT finalized.** Needs its own brainstorm to resolve the open questions in §6 before a plan. Tracked here so it is not lost.

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

## 6. Open design questions (resolve in 3b's brainstorm BEFORE planning)

1. **Shared-shift fallback:** how do HOME / OFFLINE / null-`CategoryId` shared shifts resolve candidates (everyone with `DoesShifts` in molecule? a default category? keep job-type for these)?
2. **`CategoryId` backfill strategy:** automatic mapping (from job type? from existing user-category memberships?) vs. an admin pass vs. hybrid. What happens to shift types an admin never categorizes?
3. **`DoesShifts` source:** per-company `CompanyMembership.DoesShifts` vs. the mirrored `AppUser.DoesShifts` — which governs eligibility for cross-company / multi-membership users?
4. **ShiftGrouping interaction:** does the geographic `ShiftGrouping` filter still apply on top of category membership, or is it subsumed?
5. **Behavior-change rollout:** dropdown/bottom-sheet candidate sets will change for real users — staged behind a flag, or direct with QA?

## 7. Definition of done (so 3b is not "half done")

Eligibility for **all three** assign UIs is `DoesShifts` + category-membership driven; `ShiftType.CategoryId` is fully populated (or a defined fallback covers the gaps); quick-entry has the fetch/cache/states/a11y/disambiguation above; no silent fallback; bilingual loc complete; tests cover the new eligibility rule + the backfill + quick-entry states.
