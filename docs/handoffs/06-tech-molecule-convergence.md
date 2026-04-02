# Session Handoff — Tech Molecule Convergence: Implement Remaining Gap

---

## Goal
The Tech Molecule Convergence is ~95% done. One functional gap remains: the bottom-sheet user picker doesn't filter by shift-type eligibility for Tech molecules in shift-mode.

Full design spec: `docs/superpowers/specs/2026-03-14-tech-molecule-convergence-design.md`

---

## Current Status (fully verified 2026-04-01 — see memory/project_tech_molecule_convergence.md)

### DONE — All confirmed in code
1. 6 Shikma Companies seeded (Yekev, Snir, Arbel, Pie, Samapkamia, Tao) + HQ
2. `ShiftType.EligibleCompanyIds` (JSON) + `RequiresOfficerRank` (bool) on model
3. `ShiftCapacityOverride.JobTypeId` is `int?` (nullable)
4. `IShiftCalendarService` all `int? jobTypeId` — done
5. `GetEligibleUsersForShiftTypeAsync` implemented in `ShiftCalendarService.cs:234`
6. TECH_SHIFT_INELIGIBLE → Hard Error in both single and batch validation
7. `IsMoleculeMode` fixed in `Table.cshtml.cs:100`
8. JobType dropdown hidden for Tech: `Shifts.cshtml:35` `@if (!Model.IsTechMolecule)`
9. Tech guards in `Shifts.cshtml.cs:170-196`
10. SignalR sentinel `shifts-{moleculeId}-0` in `CalendarHub.cs:375` + `calendar-realtime.js:75`
11. All `Table.cshtml.cs` SignalR callsites pass nullable `shiftType.JobTypeId`
12. `GetShiftsData` API accepts null jobTypeId
13. `TechShiftTypeSeed` + eligibility wiring done
14. `DepartmentLead.IsVisibleInSignup = false`
15. `ITechShiftService` deleted, no DI registration
16. `TECH_SUPPORT`/`TECH_ONCALL` constants removed
17. ProjectManager job type seeded

---

## The Only Functional Gap

**`GetEligibleUsersForShiftTypeAsync` is implemented but unconnected.**

In Tech shift-mode, `Shifts.cshtml` renders a single hidden `<select id="assigneeSelect-shifts">` containing ALL users in the molecule. `calendar-bottom-sheet.js`'s `getAvailableUsers()` Strategy 1 reads that static list. Result: clicking a Yekev row shows ALL molecule users (Tao, Pie, etc.) as assignable — ignoring `EligibleCompanyIds`.

### Fix Plan

**Option A — New API endpoint (recommended)**
1. Create `Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs`:
   - Accepts `moleculeId` + `shiftTypeId`
   - Calls `_shiftCalendarService.GetEligibleUsersForShiftTypeAsync(moleculeId, shiftTypeId)`
   - Returns JSON `[{id, name}]`
2. Register in `Middleware/ApiAuthenticationMiddleware.cs` `IsInternalWebUiEndpoint()` 
3. Register in `Program.cs` `AllowAnonymousToPage` if other `/Api/Calendar/` pages use it (check the pattern)

4. In `calendar-bottom-sheet.js` around `getAvailableUsers()` (Strategy 1, ~line 420):
   - Detect Tech mode: if `window.CalendarScope.isTechMolecule` (or equivalent page-level JS var — check how `Shifts.cshtml` exposes `IsTechMolecule` to JS)
   - Extract `shiftTypeId` from `cellData.rowId` — row ID is `shift-{shiftTypeId}`, so: `parseInt(cellData.rowId.replace('shift-', ''))`
   - Fetch `moleculeId` from `window.CalendarScope.moleculeId`
   - Call the new endpoint and populate the dropdown dynamically

Check `Pages/Calendar/Shifts.cshtml` for how `IsTechMolecule` and `MoleculeId` are exposed to JS (likely via `window.CalendarScope` or a `<script>` data block).

---

## Low-Priority Gaps (optional, do after main gap)

**Gap 2:** `Data/SeedData/QaTestUserSeed.cs:172` — `deptlead.pie@test` still uses `"DepartmentLead"` template. Spec says migrate to BRDirector. One-line change.

**Gap 3:** `Pages/Calendar/Shifts.cshtml.cs:193` still uses `"AssignTechShifts"` grant name. Functional but uses old pre-convergence name. Cosmetic.

---

## Key Files

- `docs/superpowers/specs/2026-03-14-tech-molecule-convergence-design.md` — full design spec
- `Services/ShiftCalendarService.cs:234` — `GetEligibleUsersForShiftTypeAsync` (done, unconnected)
- `Services/IShiftCalendarService.cs:59` — method declaration
- `Pages/Calendar/Shifts.cshtml:218-239` — hidden assignee select (currently all-users)
- `Pages/Calendar/Shifts.cshtml.cs:166-196` — IsTechMolecule + Tech guards
- `wwwroot/js/calendar-bottom-sheet.js:690-727` — `getAvailableUsers()` Strategy 1
- `Middleware/ApiAuthenticationMiddleware.cs` — add new endpoint to whitelist
- `Pages/Api/Calendar/` — location for new endpoint file
- `Data/SeedData/QaTestUserSeed.cs:172` — Gap 2 (deptlead.pie@test)
