# Account Types (mil / groupuser) + Analytics Logic — Design Spec (Sub-projects A + B)

- **Date:** 2026-06-14
- **Branch:** dev
- **Builds on:** sub-project C (Admin UI fixes), already shipped on `dev`.
- **Scope:** Two related sub-projects designed and built together as one effort, one spec, with a plan sequenced so A's foundation lands before B's analytics wiring depends on it.

---

## Part A — New account types: `mil` (מילואים) and `groupuser` (יוזר קיבוצי)

### A.0 Modeling decision (locked)
`mil` and `groupuser` are modeled as an **orthogonal `AccountType`**, NOT as JobType rows. JobType remains the organizational grouping that drives jobtype-scoped role grants (e.g. Lead/מפ"צ). `AccountType` layers capability/visibility restrictions on top. A `mil` user still has a real `JobType` (BR/Alhut/…) and a real `RoleTemplate` (e.g. kabar) — `AccountType` does not replace either. This keeps the org axis and the capability axis independent and avoids per-area seeding and jobtype-scoped-grant breakage.

### A.1 Data model
- New enum `Models/Support/AccountType.cs`: `Standard = 0, Mil = 1, GroupUser = 2`.
- New property `AppUser.AccountType` (type `AccountType`, default `Standard`), placed near `DoesShifts` (`Models/AppUser.cs` ~line 95).
- EF migration (naming pattern `yyyyMMddHHmmss_AddAccountTypeToAppUser`) adds the column with default `0`. All existing users backfill to `Standard` → zero behavior change for the current population.
- No new query filter needed (AppUser already has the tenant filter via `IBelongsToCompany`).

### A.2 Central capability gate — single source of truth
New file `Models/Support/UserCapabilities.cs`: static extension methods on `AppUser`. The 7 chokepoints call these; the business rules live ONLY here.

| Method | Standard | Mil | GroupUser | Definition |
|---|:--:|:--:|:--:|---|
| `CanBeAssignedShift()` | ✅ | ✅ | ❌ | `AccountType != GroupUser` |
| `CanDoChores()` | ✅ | ❌ | ❌ | `AccountType == Standard` |
| `CanBeAssignedAnything()` | ✅ | ✅* | ❌ | `AccountType != GroupUser` (*mil only via shifts) |
| `CanAccessRequests()` | ✅ | ❌ | ❌ | `AccountType == Standard` |
| `CanBeVacationApprover()` | ✅ | ❌ | ❌ | `AccountType == Standard` |
| `IsVisibleOnOverview()` | ✅ | ❌ | ❌ | `AccountType == Standard` |
| `IsVisibleOnShiftsCalendar()` | ✅ | ✅ | ❌ | `AccountType != GroupUser` |
| `IsVisibleOnChoresCalendar()` | ✅ | ❌ | ❌ | `AccountType == Standard` |
| `IsVisibleInAnalytics()` | ✅ | ✅ | ❌ | `AccountType != GroupUser` |

Role powers (kabar etc.) and email notifications are unaffected for all three — they flow through the existing grant/notification systems with no special-casing.

### A.3 Enforcement chokepoints (7) — each calls the gate
1. **Overview roster** — `Pages/Calendar/Overview.cshtml.cs` (LoadUsersAsync ~195–209): keep only `IsVisibleOnOverview()` (drops mil + groupuser). EF-translatable form: `.Where(u => u.AccountType == AccountType.Standard)`.
2. **Shifts roster** — `Services/ShiftCalendarService.cs:36` (GetUsersForCalendarAsync): drop groupuser → `.Where(u => u.AccountType != AccountType.GroupUser)`.
3. **Chores roster** — `Pages/Calendar/Chores.cshtml.cs:349` (GetUsersForMoleculeAsync): keep only Standard → `.Where(u => u.AccountType == AccountType.Standard)`.
4. **Requests lockout** — block mil + groupuser from the entire Requests surface:
   - `Pages/My/Requests.cshtml.cs` — in `OnGetAsync` redirect mil/groupuser away with a localized message; in `OnPostTimeOffAsync` (~182) return `Forbid()`/redirect if `!CanAccessRequests()`.
   - `Pages/Requests/Index.cshtml.cs` — same guard in `OnGetAsync` (the admin/approver view).
   - Hide the Requests nav entry for mil/groupuser (cosmetic, in the layout/nav).
5. **Approver options** — `Services/VacationApprovalService.cs:1508` (GetGrantBasedApproverOptionsAsync): exclude mil + groupuser from the approver pool → `.Where(u => u.AccountType == AccountType.Standard)` (i.e. `CanBeVacationApprover`).
6. **Assignment validation (hard block)** — `Services/BusyService.cs`:
   - `ValidateShiftAsync` (~325): if target user `!CanBeAssignedShift()` (groupuser) add a hard `ValidationIssue` error `GROUPUSER_CANNOT_BE_ASSIGNED`.
   - `ValidateChoreAsync` (~196): if target user `!CanDoChores()` (mil or groupuser) add a hard error `ACCOUNT_CANNOT_DO_CHORES`.
   - This is the backstop even if a roster filter is bypassed.
7. **Analytics rows** — `Services/JusticeService.cs` builders (lines 200 & 440 user enumeration): exclude groupuser → `.Where(u => u.AccountType != AccountType.GroupUser)` (mil stays). (Coordinated with Part B.)

### A.4 Admin/Users UI (account type is set here)
- New inline-editable **Account Type** cell mirroring the existing JobType cell pattern (`Pages/Admin/Users.cshtml` ~959–981): `.editable-cell` display/edit divs, a `<select>` with the three options, save/cancel buttons.
- New handler `OnPostAccountTypeAsync(int id, int accountType)` in `Pages/Admin/Users.cshtml.cs`, mirroring `OnPostJobTypeAsync` (1384): existence check, `EditCompanyUsers` grant check for the target's company, persist via `SaveWithConcurrencyHandlingAsync`, audit via `_auditLogService.LogUserActionAsync`, user notification, `RedirectToPage()`.
- A small badge next to the user's name renders `מילואים` / `יוזר קיבוצי` when `AccountType != Standard`, so shared accounts are visible at a glance. `groupuser`'s "indicative name" is just its `DisplayName` (admin-entered, e.g. `groupusertzafona`).
- The new-user creation form (`OnPostAddAsync`) defaults `AccountType = Standard`; admins change it via the inline cell afterward. (Optional: a creation-time dropdown — deferred unless requested.)

### A.5 Type-change cleanup (locked decision)
When `OnPostAccountTypeAsync` changes a user to a more restricted type, clean up assignments they can no longer hold — mirroring the existing user-deactivation cleanup (`Pages/Admin/Users.cshtml.cs` deactivation logic):
- → **Mil**: remove the user's FUTURE chore assignments.
- → **GroupUser**: remove the user's FUTURE shift, chore, and on-call assignments.
- "Future" = assignments dated >= today. Past assignments are history and stay.
- Each cleanup writes an audit entry and (where the existing pattern does) notifies. Reuse the existing assignment-removal helpers used by deactivation; do not hand-roll new deletion paths.

### A.6 Seed + tests
- Add example QA accounts to `Data/SeedData/QaTestUserSeed.cs`: e.g. `mil.tz@test` (AccountType=Mil, real JobType=Alhut, DoesShifts=true, role kabar) and `groupuser.tz@test` (AccountType=GroupUser, DisplayName "groupusertzafona", role kabar). Password `Test1234!`.
- Unit tests for `UserCapabilities` (the full matrix above).
- Integration/behavior tests for the chokepoints: mil hidden from Overview but present in Shifts + Analytics; groupuser hidden from all three rosters + analytics; both blocked from Requests create + excluded from approver options; groupuser hard-blocked from shift assignment; mil hard-blocked from chore assignment; type-change cleanup removes the right future assignments.

---

## Part B — Analytics logic & access

### B.1 Lead-and-above gate (minimal, root-cause)
The `ViewJusticeTable` grant (id 133) is already held by Lead, BRDirector, Director, MoleculeAdmin, AreaAdmin, Owner — and additionally by **Assigner** (template 8, SortOrder 30, which is *below* Lead). To make access exactly "Lead and above":
- **Remove grant 133 from template 8 (Assigner)** in `Data/SeedData/RoleTemplateSeed.cs` (the `Grant(8, 133, SAR)` line ~1019).
- Follow the grant-change checklist: update the Assigner per-template grant count in `RoleTemplateAutoGrantTests.cs` InlineData; no resx change (grant strings unchanged); note in MEMORY.
- No new role-rank helper or policy is introduced — authority already lives in which templates hold the grant. The page keeps `[Authorize(Policy = "Grant:ViewJusticeTable")]`.

### B.2 Exclude DoesShifts=OFF from shift metrics (locked decision)
DoesShifts=OFF users are removed from **Shift and All** work-type user rows so they don't skew shift fairness, but remain under **Chore/OnDuty** work types where they legitimately participate.
- In `Services/JusticeService.cs` `BuildUsersInCompanyAsync` (~200) and `BuildUsersInMoleculeAsync` (~440): when `q.WorkType` is `Shift` or `All`, add `.Where(u => u.DoesShifts)` to the user enumeration. For `Chore`/`OnDuty`, do not.
- Apply the same conditional in the headcount used by per-user expected/capacity division so expected shares recompute over the included population (avoid a denominator/numerator mismatch).

### B.3 ShiftCategory breakdown (e.g. "yekev shifts")
Add an optional category filter rather than a new drill Level (smaller, composable):
- Add `int? ShiftCategoryId` to `JusticeQuery` and a bound `ShiftCategoryId` param on `Pages/Admin/Analytics.cshtml.cs`.
- A category dropdown in the scope/filter toolbar, populated from the selected molecule's active `ShiftCategory` rows (only meaningful when Scope=Molecule / a molecule is in context).
- When set:
  - `CountActualPerUserAsync` (shift branch ~512) and `SumShiftCapacityPerCompanyAsync` (~595): filter shifts to `a.ShiftInstance.ShiftType.CategoryId == ShiftCategoryId`.
  - User enumeration: restrict to users with `DoesShifts == true` AND a `UserShiftCategory` membership in that category.
- Result: "metrics specifically for DoesShifts-ON users in category X" — exactly the "yekev shifts" view. Category filter implies shift work-type semantics.

### B.4 Chart clarity + tooltips + descriptive text
Scope = clarity, not new chart types:
- `<title>` child elements on the donut, fairness gauge, and equity-ribbon SVG segments for native hover tooltips (segment name + value + deviation).
- A one-line descriptive caption under each chart explaining what it represents (what SpreadIndex means; what a band/share means).
- A help affordance (small "?" with a `title`/tooltip) on the SpreadIndex value explaining the 0–1 scale and severity.
- All new strings as bilingual keys in `Resources/SharedResources.resx` + `SharedResources.he-IL.resx` (e.g. `Justice_Tooltip_*`, `Justice_Caption_*`, `Justice_Help_SpreadIndex`, `Justice_Filter_Category`). Search for an existing key before adding (a duplicate key broke localization tests before).

---

## Cross-cutting

- **Localization:** every new user-facing string (account-type labels, badges, Requests-lockout message, assignment-block errors, analytics tooltips/captions/category labels) gets keys in BOTH resx files. Grep the key first.
- **Migration discipline:** the AccountType migration is additive with a safe default; no data migration beyond the default. Never insert in the middle of GrantTypeSeed (n/a here — no new grant; we only remove a grant *assignment*, not a grant type).
- **Grant-change discipline:** removing 133 from Assigner touches seed + `RoleTemplateAutoGrantTests` count + MEMORY. Consult `grant_change_checklist.md`.
- **Build/serve:** dev app serves stale static assets until restart; restart + curl-marker before browser-verifying any cshtml/css change (Admin/Users UI, Analytics charts).

## Verification (maps to checklist)
- **3. mil:** shared account, normal email; does shifts + keeps role; NO chores, locked out of Requests (create+approve); shown in Analytics + /Calendar/Shifts; hidden from /Calendar/Overview. ✔ via A.2–A.4, B.
- **4. groupuser:** admin shared account w/ indicative name, normal email; keeps role; NO shifts/chores/assignment, locked out of Requests; hidden from ALL calendars + analytics. ✔ via A.2–A.5, B.
- **2. analytics:** lead+ gate (B.1), DoesShifts filter (B.2), category breakdown (B.3), chart clarity+tooltips (B.4).
- Full suite green (sequential: `dotnet test -- xUnit.ParallelizeTestCollections=false`); new tests for the capability matrix + chokepoints.
- Browser verification of: Admin/Users account-type editor (light/dark/RTL), mil/groupuser visibility across the 4 calendar/analytics surfaces, Requests lockout, analytics category filter + tooltips with populated data.

## Out of scope
- No change to the notification system (mil/groupuser use it normally).
- No profile-locking for mil (trusted shared account — explicitly none).
- No new chart *types* in B.4 (clarity only).
- Creation-time AccountType dropdown is optional/deferred (default Standard + inline edit covers it).

## Risks
- **Missed chokepoint** → a groupuser leaks into a surface. Mitigation: the BusyService hard-block (A.3 #6) is the assignment backstop; tests cover each surface.
- **DoesShifts conditional denominator mismatch** (B.2) — excluding users from numerator but not the expected-share denominator skews fairness the other way. Mitigation: apply the filter to both enumeration and headcount; assert in tests.
- **Category filter with no molecule context** — guard so the dropdown only applies when a molecule scope is resolved.
