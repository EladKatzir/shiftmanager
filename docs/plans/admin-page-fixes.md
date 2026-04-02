# Plan: 5 Admin Page Fixes

## Context
Five issues across admin pages need fixing: Announcements scope (Department→Molecule), HomeTypes explanation, JobTypes editing, Hierarchy delete error display, and Stores day localization.

---

## Fix 1: Stores — Day names in English despite Hebrew mode
**Root cause:** Hardcoded `static readonly string[] DayNames` at `Index.cshtml.cs:59`.

**Files:**
- `Pages/Admin/Organization/Stores/Index.cshtml.cs` — Replace `DayNames[day]` with `CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[day]`
- `Pages/Admin/Organization/Stores/Index.cshtml` — Remove `.Substring(0, 3)` from line 183 (abbreviation already handled by culture)

---

## Fix 2: Hierarchy — Molecule delete shows generic error
**Root cause:** `deleteEntity()` at `Default.cshtml:683` throws before reading response body. The API returns a useful message but JS never parses it.

**File:** `Pages/Shared/Components/HierarchyTree/Default.cshtml`
- Lines 676-685: Read `response.json()` BEFORE checking `response.ok`. On failure, throw error with `data.message`.
- Lines 579-581: Update `.catch` to show `err.apiMessage` if available, fallback to localized generic string.

Note: `Company` has no `IsActive` property (confirmed), so the molecule children check at `Delete.cshtml.cs:107` is correct as-is.

---

## Fix 3: JobTypes — Allow editing existing JobTypes
**Reference pattern:** ChoreTypes page (inline edit row with `js-edit-toggle`/`js-edit-cancel`).

**Files:**
- `Pages/Admin/Organization/JobTypes/Index.cshtml.cs`:
  - Add `[BindProperty]` fields: `EditJobTypeId`, `EditJobTypeDisplayName`, `EditJobTypeColor`, `EditJobTypeSortOrder`, `EditJobTypeMoleculeId`, `EditJobTypeIsWorkforceOnly`
  - Add `OnPostEditAsync()` handler — load JobType, update editable fields (DisplayName, Color, SortOrder, MoleculeId, IsWorkforceOnly), save. NOT editable: AreaId, Name.
  - Add `AreaId`, `MoleculeId`, `IsWorkforceOnly` to the `JobTypeVM` record so edit row can populate values
- `Pages/Admin/Organization/JobTypes/Index.cshtml`:
  - Add Edit button in actions column
  - Add collapsible inline edit `<tr>` below each row (same pattern as ChoreTypes)
  - Add JS toggle/cancel script
  - Add `.edit-row` CSS

---

## Fix 4: HomeTypes — Better explain pattern detection
**Current state:** Only a `<loc key="PaintHomeDays" />` label, no explanation of detection algorithm.

**Files:**
- `Pages/Admin/HomeTypes/Index.cshtml`:
  - Add help text paragraph below the "Paint Home Days" label explaining: "Paint dates in a typical cycle. The system detects the repeating pattern automatically."
  - Add `<details>` expandable with more specifics: 1-week → 4-week cycle assumed, equal gaps → gap = cycle, etc.
  - Add `#rule-preview` div below the calendar painter showing detected pattern live
- `Resources/SharedResources.resx` + `SharedResources.he-IL.resx`: Add `HomeType_PaintHelp_*` keys (3-4 keys)
- JS in `@section Scripts`: Listen to `#pattern-json` changes, derive cycle description client-side, update `#rule-preview`

---

## Fix 5: Announcements — Replace Department scope with Molecule
**Largest change — requires migration.**

**Files:**
- `Models/Announcement.cs`:
  - Rename enum value `Department = 1` → `Molecule = 1`
  - Replace `TargetDepartmentId`/`TargetDepartment` → `TargetMoleculeId`/`TargetMolecule`
- `Services/AnnouncementService.cs`:
  - `GetActiveAnnouncementsAsync`: filter `a.TargetMoleculeId == user.Company.MoleculeId` (Company has MoleculeId)
  - `GetAllAnnouncementsAsync`/`GetByIdAsync`: `.Include(a => a.TargetMolecule)`
- `Pages/Admin/Announcements.cshtml.cs`:
  - Replace `DepartmentOption` record → query `_db.Molecules` directly
  - Rename bound property `TargetDepartmentId` → `TargetMoleculeId`
  - Update validation and `AnnouncementVM`
- `Pages/Admin/Announcements.cshtml`:
  - Molecule dropdown (name=TargetMoleculeId), scope badge, JS field toggle
- `Resources/SharedResources.resx` + `he-IL`: Add `AnnouncementScope_Molecule` key
- **EF Migration**: `dotnet ef migrations add AnnouncementsReplaceDepartmentWithMolecule`

---

## Execution Order
1. Fix 1 (Stores) — smallest, isolated
2. Fix 2 (Hierarchy) — JS-only
3. Fix 3 (JobTypes) — no migration, follows existing pattern
4. Fix 4 (HomeTypes) — UI + localization
5. Fix 5 (Announcements) — migration last

## Verification
- Run app, switch to Hebrew, check Stores page day tabs render Hebrew abbreviations
- Try deleting a molecule with children → should see specific error message
- Edit a JobType's DisplayName/Color/SortOrder → verify save persists
- Open HomeTypes create form → verify help text visible, pattern preview updates on paint
- Create an announcement with Molecule scope → verify molecule dropdown, verify filtered delivery
- Test all pages in both English and Hebrew
