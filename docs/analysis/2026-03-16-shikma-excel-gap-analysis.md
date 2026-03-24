# Shikma Excel → ShiftManager Gap Analysis

**Date:** 2026-03-16
**Source:** `reconstructed_schedule_from_photos_v7.xlsx` (reconstructed from Shikma's operational schedule screenshots)
**Status:** Analysis complete. **G1-G9 all resolved.** G10 clarified — רחב = all-day 09:00-22:00, שלמש = typo, every tech shift has morning/afternoon variants. Implementation of sub-shift variants is the only remaining work item.

---

## 1. Excel Structure Overview

The Excel is a **מק"ש (Personnel Status Schedule)** — a single-sheet military block-rotation schedule for ~55 people over 18 days (16/03/2026 – 02/04/2026).

### Quantitative Summary

| Metric | Value |
|--------|-------|
| Total person-days in grid | 954 |
| Filled cells | 146 (15.3%) |
| "בית" (home) cells | 78 (53% of filled) |
| Shift assignment cells (pink) | 39 (27%) |
| Task/chore cells (teal) | 17 (12%) |
| Availability note cells (yellow, non-בית) | 12 (8%) |
| People with completely empty grids | 21 (38%) — confirmed home rotation, not missing data |

### Color Coding

| Color | Hex | Meaning | Examples |
|-------|-----|---------|----------|
| **Pink** | `#F4C2E0` | Active shift assignment | בוקר, צהריים, רחב, שלמש, מובילט בוקר, מאייש צהריים |
| **Yellow** | `#F8E7B1` | Home / off / availability note | בית, עולה ב13, עולה עד 16:00, אפשר |
| **Teal** | `#CDEFE8` | Task/chore (guard duty, etc.) | מטלה - ש"ג 10-14, מטלה - מעצ, מטלה - ליווי קבלים |

### Metadata Columns (A–F)

| Column | Header | Unique Values | Purpose |
|--------|--------|---------------|---------|
| A | מייל | (mostly empty) | Email address |
| B | שם | 53 names + 2 label rows | Person name |
| C | כללי | רבעונים, תלתון א/ג, סבב א/ב, מיל, מיוחד, יקב | Rotation frequency / classification |
| D | סבב | סבב א, סבב ב, חניכים | Rotation group membership |
| E | סבב חירום | א, ב, ג | Emergency callout tier |
| F | משמרת | מובילט, מדלז, מאייש, דלתא | Default tech shift type |

### "כללי" (General) Column — Rotation Frequency Patterns

| Value | Meaning |
|-------|---------|
| רבעונים | Quarterly rotation — comes to base ~once per quarter |
| תלתון א/ג | Tri-rotation group A/C — every 3rd cycle |
| סבב א/ב | Standard bi-rotation — alternating blocks |
| מיל | Military reserves — separate scheduling |
| מיוחד | Special status |
| יקב | Yekev team affiliation |

---

## 2. The 13 Sections

Sections are **heterogeneous** — they mix company, shift-type role, training status, and rotation phase groupings.

| # | Section | Type | People | Grid Activity | Maps To |
|---|---------|------|--------|---------------|---------|
| 1 | סבב א בבית | Rotation status | 7 + "כונני לילה" | All empty (confirmed home) | No equivalent — filter by rotation phase |
| 2 | נוספים/חפפים | Misc extras | 2 | All empty | Ad-hoc group |
| 3 | תקני מאייש | Shift-type role | 8 | Active (shifts + tasks + home) | **"מאייש" shift type — NOT in our system** |
| 4 | חפיפות מאייש | Handover status | 4 | Thursdays only, "עולה עד 16:00" | Weekly rotation changeover — NOT trainee shadowing |
| 5 | מחזיקי דלתא | Shift-type role | 7 | Most active section | DELTA shift eligible users |
| 6 | חפיפות דלתא | Handover status | 3 | All empty | Inactive reserve positions |
| 7 | פאי | Company | 4 | Some tasks + home | `shikPie` company |
| 8 | טאו | Company | 3 | Only home entries | `shikTao` company |
| 9 | תקני היקב | Shift-type role | 4 + "כ.לילה" | Active (shifts + tasks + home) | YEKEV shift eligible users |
| 10 | היקב | Company | 2 | All empty | `shikYekev` company (non-specialists) |
| 11 | ארבל | Company | 4 | Only home entries | `shikArbel` company |
| 12 | שניר | Company | 5 | Sparse (some home + 1 task) | `shikSnir` company |
| 13 | תקינת שקמה | Template header | 0 | Empty | Not operational |

### Two-Tier Grouping Logic

The sections follow a pattern:

1. **Shift-type specialists** (operator of a specific tech system) → grouped by shift type (תקני מאייש, מחזיקי דלתא, תקני היקב)
2. **Non-specialists** (no primary tech role) → grouped by company (פאי, טאו, ארבל, שניר)
3. **Special status** → grouped by status (סבב א בבית, חפיפות, נוספים)

סמפקמיה (Samapkamia) has no section in the Excel because all its users are DELTA/HANAVA specialists — they appear in shift-type sections instead.

---

## 3. Rotation Logic

### Pattern: Staggered Block Relay

The rotation is **NOT** a clean A/B alternation. It is a staggered relay:

- **סבב ב goes home first** (peak: 8 people home on 24/03)
- **סבב א goes home later** (peak: 3 people home on 27-29/03)
- Overlap days exist where members of BOTH groups are home simultaneously (17-18/03, 24-31/03)

### Key Properties

- Home leave is **always consecutive blocks** — typically 5-9 days, never scattered
- **Within a section**, same-סבב people roughly synchronize
- **Different sections** operate on independent schedules
- Empty grids (38% of people) = confirmed on home rotation, NOT missing data

### "בית" (Home) Is NOT Vacation

This is the most important semantic distinction. "בית" is the scheduled off-rotation period — a positive assertion that "this person is confirmed at home for this block." It is:

- NOT a vacation request that goes through an approval workflow
- NOT a one-off day off
- A **block of 5-9 consecutive days** as part of a repeating military rotation cycle
- The **single most common entry** in the entire schedule (78 of 146 filled cells)

---

## 4. "חפיפות" (Overlaps) — Weekly Handover Pattern

### חפיפות מאייש (Maish Overlaps)

All 4 people have entries on **exactly two dates: 19/03 and 26/03** — both are **Thursdays**. All have the identical value "עולה עד 16:00" (present until 16:00).

This is a **weekly Thursday handover pattern** — transition staff who come in every Thursday for rotation changeover, regardless of סבב membership. Thursday is the last workday before the weekend (Fri-Sat).

This is NOT the same as our Trainee system (per-shift shadowing via `TraineeUserId`).

### חפיפות דלתא (Delta Overlaps)

All 3 people have completely empty grids — unactivated reserve positions.

---

## 5. Task (מטלה) Patterns

### 17 total task assignments across 3 categories:

**Guard Duty (ש"ג)** — 10 assignments:
- 4-hour blocks covering 24 hours: 2-6, 6-10, 10-14, 14-18, 18-22, 22-2
- "מחפה ש"ג" = covering for another guard
- Guard duty consistently appears **before** a person goes home — it's a transition duty

**מעצ (Assembly/Parade)** — 6 assignments:
- Distributed across sections, one per section on different dates
- Inter-section duty rotation

**ליווי קבלים (Escort Contractors)** — 1 assignment:
- One-off task (אוריה פרץ, 26/03)

---

## 6. Shift Type Analysis

### Excel Values vs Our Seeded Keys

| Excel (Col F) | Our Key | Match? | Notes |
|---------------|---------|--------|-------|
| מובילט | MOVILTECH | **Yes** | Officers only (`RequiresOfficerRank = true`) |
| דלתא | DELTA | **Yes** | Eligible: [Tao, Pie, Samapkamia] |
| **מאייש** | ??? | **No match** | Possibly = HANAVA? Needs Shikma clarification |
| **מדלז** | ??? | **No match** | Possibly = YEKEV? Or additional type. Needs clarification |

### Sub-Shift Time Variants

All 4 seeded tech shift types have **identical time range: 08:00-16:00**. But the Excel uses time-of-day variants:

| Cell Value | Meaning | Implied Time |
|------------|---------|-------------|
| בוקר | Morning sub-shift | ~08:00-12:00? |
| צהריים | Afternoon sub-shift | ~12:00-16:00? |
| רחב | Extended (full day?) | ~08:00-16:00? |
| שלמש | Unknown abbreviation | Unknown |
| מובילט בוקר | Mobiltech morning | Compound: system + time |
| מאייש צהריים | Maish afternoon | Compound: system + time |

This suggests each tech shift type needs **morning/afternoon sub-variants** with specific time ranges.

---

## 7. Capability Mapping: What We CAN vs CAN'T Do

### Already Works

| Excel Feature | Our Implementation | Status |
|---------------|-------------------|--------|
| Grid: rows=users, cols=dates | `Calendar/Shifts` user mode (`BuildUserBasedCalendarAsync`) | Working |
| Shift type name in cells | Localized via `ResolveShiftTypeNameAsync` | Working |
| Tech shift types (MOVILTECH, DELTA) | ShiftType keys seeded with `MoleculeId` + `JobTypeId=null` | Working |
| Company-based shift eligibility | `EligibleCompanyIds` JSON on ShiftType | Working |
| Officer rank gating | `RequiresOfficerRank` on ShiftType | Working |
| Vacancy/chore/on-duty awareness | `FyiOverlayData` with boolean flags + emoji badges | Partial |
| Collapsible group headers | `ExcelCalendarGroup` + `Default.cshtml` rendering | Infrastructure ready, not wired in Shifts |
| Trainee badge | `TraineeUserId` → 🎓 emoji on assignments | Working (but different concept from חפיפות) |
| Time-off tracking | `TimeOffRequest` with approval workflow | Working (but different concept from "בית") |
| Per-user-per-day notes | `UserDayNote` in Overview calendar | Working (but not in Shifts grid) |

### Confirmed Gaps

| ID | Gap | Severity | Evidence |
|----|-----|----------|----------|
| **G1** | ~~"בית" not visible in shift grid~~ | **RESOLVED** | Home shift type created (HomeType migration exists). HOME shifts are now a first-class shift type with dedicated display. |
| **G2** | ~~No user-mode grouping in Calendar/Shifts~~ | **RESOLVED** | User-mode grouping is now implemented and wired in Calendar/Shifts. |
| **G3** | ~~Row metadata is only DisplayName~~ | **RESOLVED** | Company name badges render via `_CalendarRow.cshtml` (`CompanyName` property + company-badge span). Rotation group and emergency tier are both represented by the HomeType system — `AppUser.HomeTypeId` links to the rotation template, and the HomeType name (e.g., "סבב א") displays in the row SubLabel. |
| **G4** | ~~No cell-level notes on assignments~~ | **RESOLVED** | `ShiftAssignment.Note` field now exists and is rendered alongside assignment names. |
| **G5** | ~~Chores not visible in shift calendar~~ | **RESOLVED** | `Shifts.cshtml.cs` lines 726-745 render chore items as colored assignment chips in user-mode (`Id=0` for non-removable display). `GetOverlaysAsync` enriches `FyiOverlayData` with `ChoreItems` including name, color, and time range. |
| **G6** | ~~Chores have no time range~~ | **RESOLVED** | `Chore.cs` now has `StartTime` and `EndTime` as `TimeOnly?` properties. Supports timed chores like guard duty 10:00-14:00. |
| **G7** | ~~No rotation group concept~~ | **RESOLVED** | Rotation groups are HomeType entries (e.g., "סבב א", "סבב ב", "רבעונים"). `AppUser.HomeTypeId` links to the rotation template. `HomeType` model has `PatternJson`, `DerivedRule`, `DefaultStartTime`/`EndTime` for the rotation schedule. |
| **G8** | ~~No emergency callout tier~~ | **RESOLVED** | Emergency tiers are wartime HomeType variants. During emergency (war), rotation switches from peacetime pattern (Thu-Sun every 2 weeks) to emergency pattern (full week Thu-Thu every 3 weeks). These are simply different HomeType entries — no separate field needed. |
| **G9** | ~~Shift type name mismatch (מאייש, מדלז)~~ | **RESOLVED** | Confirmed via `shifty_org_hierarchy_v4.md` line 207: מאייש = HANAVA (הנבה). Already seeded as `TECH_HANAVA`. מדלז does not appear in the hierarchy doc and is likely a local abbreviation — not a real shift type. |
| **G10** | Sub-shift time variants | **CLARIFIED** | רחב (rachav) = all-day shift 09:00-22:00. שלמש = typo (appeared once, not a real shift type). Every tech shift type (HANAVA, DELTA, YEKEV, MOVILTECH) has morning (בוקר) and afternoon (צהריים) sub-variants. Implementation: create ShiftType variants per tech system with appropriate time ranges. Morning/afternoon boundary TBD (likely 09:00-15:30 / 15:30-22:00 or similar split of the 09:00-22:00 window). |

---

## 8. Architecture Notes

### Existing Infrastructure That Can Be Leveraged

**ExcelCalendarGroup (ready, just not wired):**
- `ExcelCalendarGroup` model: `Id`, `Name`, `IsCollapsed`, `SortOrder`
- `ExcelCalendarRow.GroupId` links rows to groups
- `Default.cshtml:53-80`: Full rendering with collapsible headers, chevrons, drag-to-reorder
- Currently only superficially used in Chores page

**FyiOverlayData (data loaded, details discarded):**
- `GetOverlaysAsync` in `ShiftCalendarService` loads full `Chore` and `OnDuty` entities
- But produces only `HasChore: bool`, `HasOnDuty: bool`
- Chore `Title`, `ChoreType.DisplayName`, and `ChoreType.Color` are already in memory — just thrown away
- Enriching this to include names/colors is nearly free

**Calendar/Overview multi-source pattern:**
- Overview already loads 5 data sources: shifts + chores + on-duty + vacations + notes
- Renders all as named assignment chips with role-based CSS
- This pattern can be adopted by Calendar/Shifts in user-mode

**Company data on users:**
- `GetUsersForCalendarAsync` returns `AppUser` with `CompanyId` present but Company nav not loaded
- Adding `.Include(u => u.Company)` is trivial — or load company names separately (same pattern as shift-mode)

### Tech Molecule Convergence Dependency

The convergence project (departments → companies) is a **prerequisite** for company-based grouping to work properly. Current state:

- 6 Shikma companies already seeded: Yekev, Snir, Arbel, Pie, Samapkamia, Tao
- `EligibleCompanyIds` and `RequiresOfficerRank` already in model and seed
- BUT: Department→Company data migration NOT done yet
- All Shikma users still have `CompanyId = hq-shikma` (HQ company)
- Until migration runs, grouping by company will show everyone in one group

---

## 9. Implementation Priorities

### Phase 0: Clarifications From Shikma

**Resolved** (via `shifty_org_hierarchy_v4.md`):

1. ~~**Shift type names**~~: **RESOLVED** — מאייש = HANAVA (הנבה), confirmed line 207. מדלז is not in the hierarchy doc — likely a local abbreviation, not a real shift type. Already seeded correctly as `TECH_HANAVA`.
2. ~~**סמפקמיה (Samapkamia)**~~: **RESOLVED** — Real department under Shikma (line 102). Eligible for Delta only (line 213).

**Still open:**

3. ~~**Sub-shift time ranges**~~: **CLARIFIED** — רחב (rachav) = all-day shift 09:00-22:00. שלמש = typo (ignore). Every tech shift has morning (בוקר) and afternoon (צהריים) sub-variants. Remaining: decide exact morning/afternoon boundary within the 09:00-22:00 window.
4. **"בית" tracking**: Is "home" explicitly marked by a manager, or derived from the rotation schedule?
5. **"כונני לילה" (Night Standby)**: How should this appear — as an OnDutyType? A designation row? A label?

### Phase 1: Make the Calendar Usable (~80% Excel Parity)

**Prerequisite:** Complete Tech Molecule Convergence (departments → companies migration).

| Step | Gap | Description | Scope | Status |
|------|-----|-------------|-------|--------|
| 1a | G2 | Wire grouping in user-mode — group by Company using existing `ExcelCalendarGroup` | `Shifts.cshtml.cs` | DONE |
| 1b | G1 | Show "בית" — create BAYIT/HOME shift type with yellow color, exempt from overlap/rest validation | Seed + validation | DONE |
| 1c | G5 | Show chores in shift grid — enrich `FyiOverlayData` with chore names/colors, render as chips | `ShiftCalendarService`, `Shifts.cshtml.cs` | DONE — chore items render as colored assignment chips in user-mode (lines 726-745) |
| 1d | G3 | Show company name + rotation info per user row | `_CalendarRow.cshtml`, `Shifts.cshtml.cs` | DONE — company badges + HomeType sublabel cover metadata needs |

### Phase 2: Data Richness (~95% Excel Parity)

| Step | Gap | Description | Status |
|------|-----|-------------|--------|
| 2a | G4 | Add `Note` field to `ShiftAssignment` + render alongside assignment name | DONE |
| 2b | G6 | Add optional `StartTime`/`EndTime` (TimeOnly?) to `Chore` + display | DONE |
| 2c | G7 | Rotation group concept | DONE — Implemented via HomeType system (`AppUser.HomeTypeId` → `HomeType.Name` e.g. "סבב א"). Displayed in calendar row SubLabel |
| 2d | G8 | Emergency callout tier | DONE — Implemented via HomeType system. Emergency tiers are wartime HomeType variants (full week Thu-Thu/3 weeks instead of peacetime Thu-Sun/2 weeks) |
| 2e | G10 | Create morning/afternoon/rachav ShiftType variants per tech system. בוקר = 09:00-16:00, צהריים = 16:00-00:00, רחב = 09:00-22:00. שלמש = typo (ignore) | READY — all time ranges confirmed, ready to implement |
| 2f | G9 | ~~Correct shift type names/keys per Shikma's clarification~~ | DONE — מאייש = HANAVA confirmed, already seeded correctly |

### Phase 3: Advanced (~100% Parity + Beyond)

- Rotation-based auto-marking of "בית" (derive from rotation schedule, not manual assignment)
- "חפיפות" as a personnel status category (weekly partial-day attendance pattern)
- Custom section grouping beyond Company-based (admin-defined groups)
- Additional configurable metadata columns per molecule
- Rotation frequency patterns (רבעונים / תלתון) as scheduling constraints

---

## 10. Key Special Rows

| Row | Name | Section | Purpose |
|-----|------|---------|---------|
| 12 | כונני לילה | סבב א בבית | Night standby designation — NOT a person |
| 72 | כ.לילה | תקני היקב | Night standby abbreviation — NOT a person |

These are placeholder/label rows for night standby. Could be modeled as `OnDutyTypeConfig` entries or as special designation rows in the calendar.

---

## Appendix: All Unique Grid Cell Values

### Shift Assignments (Pink `#F4C2E0`)

| Value | Count | Meaning |
|-------|-------|---------|
| צהריים | 13 | Afternoon sub-shift |
| בוקר | 11 | Morning sub-shift |
| רחב | 7 | Extended shift (full day?) |
| משמרת | 3 | Generic "shift" label |
| מובילט בוקר | 1 | Mobiltech morning |
| מאייש צהריים | 1 | Maish afternoon |
| מאייש בוקר | 1 | Maish morning |
| עולה ב13 + צהריים | 1 | Arrives at 13 + afternoon |
| שלמש | 1 | Unknown abbreviation |

### Home / Availability (Yellow `#F8E7B1`)

| Value | Count | Meaning |
|-------|-------|---------|
| בית | 78 | Confirmed at home (rotation leave) |
| עולה עד 16:00 | 8 | Present until 16:00 (handover staff) |
| עולה ב13 | 3 | Arrives at 13:00 |
| אפשר | 1 | Available (if needed) |

### Tasks / Chores (Teal `#CDEFE8`)

| Value | Count | Meaning |
|-------|-------|---------|
| מטלה - מעצ | 6 | Assembly/parade duty |
| מטלה - מחפה ש"ג 10-14 | 3 | Covering guard 10:00-14:00 |
| מטלה - ש"ג 14-18 | 2 | Guard duty 14:00-18:00 |
| מטלה - ש"ג 2-6 | 1 | Guard duty 02:00-06:00 |
| מטלה - ש"ג 6-10 | 1 | Guard duty 06:00-10:00 |
| מטלה - ש"ג 18-22 | 1 | Guard duty 18:00-22:00 |
| מטלה - מחפה ש"ג 22-2 | 1 | Covering guard 22:00-02:00 |
| מטלה - מחפה ש"ג 18-22 | 1 | Covering guard 18:00-22:00 |
| מטלה - ליווי קבלים | 1 | Escorting contractors |
