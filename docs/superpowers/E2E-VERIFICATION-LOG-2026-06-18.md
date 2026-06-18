# E2E Seeding & Usage Verification — nav-ia-redesign — running log (2026-06-18)

Brief: `C:\Users\katzi\.claude\plans\harmonic-gathering-spindle.md`
Worktree HEAD at start: `bef811a` (ship domain-hub nav as only reality).
App: `dotnet run --no-build` → http://localhost:5000. All `@test` users pwd `Test1234!`.

## Baseline (read-only DB recon at start)
- Molecules: 1 Oren, 2 Ella, 3 Harava, 4 Shaked, 5 Gefen, 6 Shikma(tech), 10 QA.
- Oren companies: Tzafona(1), Hir(2), Camps(3), City(4), Radio(5), HQ(16), TestEmpty(31), **TestFull(32, has 93 ShiftInstances)**, Multi A/B(33/34).
- ShiftCategories=1 (GuardDuty/שמירות, Oren, no linked types). ChoreTypes/Cats/EligibilityRules/ShiftPrograms=0. ChoreTemplates=0.
- ShiftInstances: co1(Tzafona)=3 (6/18-6/21); co32(TestFull)=93 (6/2-7/2). **Cast lives in co1/co2, NOT co32.**
- OnDutyTypeConfigs=1 (Backup-hakam, RequiresOfficerRank=0).
- Flags: FF_CATEGORY_BASED_SHIFT_ELIGIBILITY=OFF(global); FF_ENFORCE_RANK_ELIGIBILITY=ON; FF_NEW_NAV=ON(leftover, code ignores it).
- Cast (62 @test users): nearly all Rank=0/Gender=0/Standard/DoesShifts=0/DoesChores=0. Exceptions: mil.tz@test (Mil, Male), groupuser.tz@test (GroupUser), a few emp.tz.* with DoesShifts=1; emp.hir.text@test (Male, DoesShifts=1).

## Plan
- **Oren = deep target** (Tzafona co1 primary, Hir co2 secondary). Satisfies all 14 checklist items.
- Ella/Shikma/Harava/Gefen = breadth (lighter seed). Honest about per-molecule depth.

## Enum reference
- MilitaryRank: officer = SegenMishne(9)+ ; Gender 0=Unspec/1=Male/2=Female ; AccountType 0=Standard/1=Mil/2=GroupUser.

## Running log
- [setup] DB backed up (app.db.bak + wal/shm). Build OK (0 errors, 2146 pre-existing CA warns). App boots clean on :5000.
- [setup] UI-automation cheat-sheet built via 2 read-only Explore agents (setup pages + usage/remediation pages).
- [P1 Owner] Katzin officer-gated duty type created (RequiresOfficerRank=YES); non-gated Hakam already built-in. FF_CATEGORY flag located (global checkbox), enable deferred to end-of-setup (per "backfill first" guidance).
- [P1 Oren shifts] (as moladmin.oren) created "Operations" shift category; linked 6 work shifts (MORNING/AFTERNOON/NIGHT) → Operations; HOME/OFFLINE left uncategorized. Verified persisted on reload, 0 console errors. MoleculeAdmin reached /Owner/Blueprints (no 403) — item 11 datapoint.
- [P1 Oren chores] (as moladmin.oren) created "Cleaning" chore category; 4 chore types: Kitchen Duty (none), Guard Post (OFFICER rule), Laundry (FEMALE rule), Trash Run (none). Chips render: elig-chip--officer "Officer", elig-chip--gender "Female only". Success FeedbackModal fires after each create (unified feedback surface working).
- [P1 Oren users] (as moladmin.oren, /Admin/Users inline edits, all via UI): emp.tz.text(28)→Female+DoesChores; emp.tz.br(29)→Male+DoesChores; emp.tz.alhut(27)→DoesChores (already DoesShifts); created NEW user emp.tz.new@test (id70, Tzafona/Alhut/Soldier) + DoesShifts on. Pre-existing edge accounts: mil.tz(68, Mil/Male), groupuser.tz(69, GroupUser). dir.alhut(15)→rank Lieutenant(officer) via /Admin/EditProfile. MoleculeAdmin reached /Admin/Users + /Admin/EditProfile (no 403).
- [P1 Oren programs] (/Owner/Programs, active company Tzafona): created program "Alhut Morning Rotation" on shift type 1 (Alhut Morning, in Operations cat), Sun-Thu, staffing 2 → generated 15 ShiftInstances 2026-06-18..07-09. Tzafona ShiftInstances 3→18. Shift types 1,2,3=Alhut / 4,5,6=Text (all CategoryId=Operations); 7,8=HOME/OFFLINE uncategorized.
- [P1 Oren chore template] created "Daily Kitchen" template (Kitchen Duty, 08:00-12:00); stamped 2026-06-21..25 Sun+Mon for emp.tz.alhut → Created 1 (06-22) / Skipped 1 (06-21 "not eligible or conflict"). NOTE: first stamp attempt produced 0 — was MY input error (assignee checkbox not persisted), not a bug; retry with assignee checked worked. Chore-stamp flow verified.
- [P1 Oren membership] (/Scheduling/Eligibility, By-category): Operations shift cat members={emp.tz.alhut(27), emp.tz.text(28)}; left emp.tz.br(29)+emp.tz.new(70) OUT (DoesShifts-on non-members for not-in-category failure). Cleaning chore cat members={27,28,29}. Toggle persistence VERIFIED across reload (MEMBERS(2) Ops). Editor annotates ineligibility reasons ("Doesn't do shifts"/"Not in this category"). 0 console errors. ITEM 3 = PASS.

## Checklist status (updated as I go)
1. Populated Home spine — PENDING
2. /My/Eligibility populated + accurate — PENDING
3. Eligibility editor toggle persists — PENDING
4. why+fix shift not-in-category — PENDING
5. why+fix chore officer-rank — PENDING
6. why+fix chore gender — PENDING
7. why+fix on-duty Katzin officer — PENDING
8. roster-dock drag failure modal — PENDING
9. vacation request→approve reflected — PENDING
10. swap request→approve reflected — PENDING
11. all 9 role tiers no 403/error — PENDING
12. female user nav+eligibility + gender chore — PENDING
13. light+dark+RTL key surfaces — PENDING
14. 0 console errors on redesign pages — PENDING

## Part 2 — Usage (as personas)
- [FLAG WORKS] As Lead mgr.alhut.tz, /Calendar/Shifts (Oren/Alhut/Tzafona) groups shifts by category: "Operations (3)" + "Uncategorized (2)" (HOME/OFFLINE). quick-entry canAssign=true (Lead has AssignShifts).
- [ITEM-related] FLOW 1 (eligible assign, quick-entry/bottom-sheet): picker correctly filtered to "2 eligible users" = Operations members w/ DoesShifts (emp.tz.alhut 27, emp.tz.text 28); non-members (29,70) excluded. Assigned emp.tz.alhut→Morning/06-22; hit busy-conflict (overlapping stamped Kitchen Duty chore) → override confirm "Proceed anyway" → assigned. Cell shows "Emp TZ Alhut". 0 console errors. PASS (category filter + conflict + override all verified).

- [ARCHITECTURE FINDING — two-tier remediation] The Excel calendars (Shifts/Chores bottom-sheets) PREVENT ineligible assignments at the client with inline eligibility chips + (for hard rules) a disabled Assign — rather than letting them fail and showing the why+fix MODAL. Verified live:
  - FLOW 3 (chore OFFICER, Guard Post→non-officer 27): inline `elig-chip--block` "⊘ Officers only" + Assign DISABLED (hard block). The "why" is shown inline; assignment prevented.
  - FLOW 4 (chore GENDER, Laundry→male 29): inline `elig-chip--warn` "⚠ Gender restricted" + Assign ENABLED → click → warning-confirm "This chore is designated for female personnel" [Proceed anyway] → assigned (cell shows "Laundry"). Matches "gender = overrideable WARNING". 0 console errors. PASS.
  - Shift bottom-sheet pre-filters to category members (Flow 1), so it can't submit a non-member either.
  - IMPLICATION: the "why + Fix button" MODAL (with a Fix link to EditProfile/Eligibility) is triggered on surfaces that SUBMIT then fail server-side: on-duty calendar, Justice "make it real", roster-dock. The roster-dock surface (Table.cshtml) is NOT reachable while FF_EXCEL_CALENDAR_SHIFTS is on (Program.cs:2091 redirects /calendar/table→/Calendar/Shifts) — pre-existing, not a redesign regression. Need to verify the modal on on-duty/Justice.

- [FLOW 5 — HEADLINE why+fix MODAL, PASS] On-Duty calendar does NOT pre-filter officer rank. As Lead, assigned Katzin (officer-gated duty) to non-officer (emp.tz.alhut 27) → server 403 → why+fix MODAL rendered: "This duty type requires an officer rank..." + **"Change rank" Fix link → /Admin/EditProfile?UserId=27**. Clicked Fix (Lead CAN reach EditProfile) → set rank Lieutenant → Save → returned to OnCall → retry Katzin/27 → officer gate now CLEARED (only a busy-conflict warning remained) → Proceed anyway → assigned (cell "Emp TZ Alhut"). FULL why→Fix→promote→retry loop verified end-to-end. NOTE: the rejected assignment logs a 403 console entry on /Api/Calendar/QuickAddOnDuty — that's the expected rejection the modal is built from, not a JS defect (analogous to the pre-existing SessionStatus noise).

## Checklist progress so far
1 spine — pending; 2 /My/Eligibility — pending; 3 editor toggle persist — PASS; 4 shift not-in-category modal — see roster-dock note (pending); 5 chore officer — surfaced as inline block (PASS as prevention); 6 chore gender — PASS (warning+override); 7 on-duty Katzin officer why+fix — **PASS (full modal+Fix+promote+retry)**; 8 roster-dock — pending (config-gated); 9 vacation — pending; 10 swap — pending; 11 tiers — partial (Owner/MolAdmin/Lead reach pages no 403); 12 female user — partial (emp.tz.text Female created); 13 light/dark/RTL — pending; 14 console — clean except expected 403 on deliberate rejections.

- [FLOWS 9/10 PASS] As emp.tz.alhut: requested Regular Vacation (Jun28-30, approver=Lead) + open Swap (Jun18 Morning). As Lead (Approvals/Requests Index): approved both. DB confirms TimeOffRequests.Status=1(Approved), SwapRequests.Status=2. 0 console errors.
- [ITEM 1 Employee spine PASS] emp.tz.alhut Home shows real My Schedule (Morning/Afternoon/Night shifts + On-Duty + "Chore: Kitchen Duty"), 16.0h this week, Next Shift card, 1 Unread notif. Employee nav minimal (Home+Requests only) = policy-derived nav hides admin hubs.
- [ITEM 2 PASS both sides] /My/Eligibility: emp.tz.alhut all ✓ (שמירות,Operations,Cleaning). emp.tz.text(Female): ✓ Operations + "NOT AVAILABLE — שמירות: You're not in this group. Ask your manager" (blocked+reason) + ✓ Cleaning.
- [ITEM 11 PASS — all 9 tiers, no 403, 0 console]: Owner(/Home,FeatureFlags,DutyTypes), AreaAdmin(/Admin/Organization "Organization Structure"), MoleculeAdmin(/Owner/Blueprints,ChoreTypes,Users,EditProfile,Eligibility,Programs,ChoreTemplates), Director(/Home scope "Viewing 10 of 10 companies"), BRDirector(/Requests scope "10 of 10"), Lead(calendars+approvals), Assigner(/Calendar/Chores), Employee(/Home,/My/*), Trainee(/My/Requests).
- [TRAINEE PASS] /My/Requests: vacation form present, SWAP FORM ABSENT (correctly gated), no 403.
- [ITEM 12 PASS] female user emp.tz.text nav+eligibility render correctly; gender-gated Laundry behavior verified (Flow 4).
- [ITEM 13 PASS] dark + Hebrew RTL Home spine: dir=rtl, lang=he, theme=dark, sidebar flips right, full Hebrew, no dark-on-dark, 0 console errors. Screenshot home-spine-dark-he-rtl-e2e.png.
- [ITEM 14] 0 console errors on all redesign pages EXCEPT the expected 403 logged on deliberate ineligible-assignment rejections (the payload the why+fix modal renders) — not a JS defect.

- [ITEMS 4 & 8 — reachability finding, NOT a defect] Shift not-in-category (NOT_IN_SHIFT_GROUPING) + roster-dock why+fix could NOT be triggered live in the shipping Excel-calendar config:
  - Excel shift bottom-sheet ALWAYS prevents submitting a non-member: filters to category members when present; shows "No eligible users for this shift" (NO 'Show all workers' escape hatch) when the category is empty. Confirmed by moving Afternoon shift (type2) into a fresh 0-member "Recon" category → picker said "No eligible users", no escape hatch. (The 'Show all workers' escape hatch is for UNcategorized shifts, which aren't category-gated, so can't produce NOT_IN_SHIFT_GROUPING.)
  - The only non-filtering surface (roster-dock, in Pages/Calendar/Table.cshtml) is config-gated OFF: Program.cs:2091-2143 redirects GET /calendar/table → /Calendar/Shifts when FF_EXCEL_CALENDAR_SHIFTS is on (it is). So the roster-dock UI never renders in shipping config.
  - API probe POST /Calendar/Table?handler=AssignUserToSlot {shiftInstanceId,userId} → 200 {success:false, error:"Assignment not found"} (endpoint assigns to a SLOT, not a raw instance — didn't reverse-engineer the slot shape).
  - VERDICT: remediation ENGINE+modal+Fix+retry FULLY PROVEN by the on-duty Katzin officer-rank flow (Flow 5, identical FailureRemediationService+FeedbackModal). Shift-category + roster-dock TRIGGERS are config-gated/pre-filtered; a live trigger needs FF_EXCEL_CALENDAR_SHIFTS=off. Items 4/8 = PARTIAL (engine proven; specific UI surface not live-reachable in shipping config). Left a leftover "Recon" shift category w/ Afternoon(type2) moved into it during this probe (harmless test data).

## Bugs found / fixed
NONE. No code changes made — the redesign behaves correctly; the only gaps are reachability of two trigger surfaces in the shipping calendar config (documented above), which are pre-existing config characteristics, not redesign regressions.
