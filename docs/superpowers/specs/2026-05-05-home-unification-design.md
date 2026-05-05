# HOME Unification — Vacation, After, and Rotation as One Surface

- **Status:** Design (awaiting approval)
- **Date:** 2026-05-05
- **Author:** Brainstormed with the team
- **Affects:** TimeOffRequest, ShiftAssignment, ShiftType, HomeType, BusyService, ShiftAssignmentService, AnalyticsService, HomeTypeService, several Razor pages and JS calendars

## 1. Goals

1. Make "if a person is at vacation/after, they are at home" a literal data fact, visible identically across every calendar surface.
2. Replace HomeType's paint-the-dates UX with a clear rotation rule that admins can read and edit.
3. Fix three propagation gaps that make Generate feel broken today: missing SignalR, plain-text HOME on Overview, and HOME-blind calendars (Chores / OnCall / MyTeam).
4. Clarify approval routing — every vacation/after needs a real approval, the pool is per-jobtype within the molecule, with a configurable threshold above which dual approval is required.
5. Make HOME's "doesn't count as a shift" promise structural and complete — capacity, hours, weekly cap, analytics, justice ribbon, all wired to a single `IsHome` flag that recognises every HOME variant.

## 2. Non-goals

- Redesigning the swap-request system. The verification surfaced real bugs there (silent warning bypass, wrong grant), but those are a separate spec.
- Changing the existing TimeOffRequest table layout beyond a single nullable column. We are not splitting Vacation and After into separate tables.
- Rewriting the calendar rendering engine. We extend it; we do not replace it.
- Localisation polish on existing approved-paths UI. The design adds new strings; existing untranslated keys (per the static review) are out of scope.

## 3. Background

The November 2026 architectural review and follow-up browser verification surfaced four problem clusters:

- **Vacation/After validation gap.** Approved After misses morning-of-day-N+1 because shift validation queries the stored `StartDate..EndDate` range only. Confirmed live: a 08:00 shift on day-N+1 was assigned with no warning despite the After's logical window covering 00:00-13:00 of that day.
- **Generate is partially broken.** HomeType service has no SignalR injection, Overview renders HOME as plain text, and Chores/OnCall/MyTeam don't render HOME at all. The DB writes succeed but only the Shifts calendar reflects them in real time.
- **Approval flow is half-built.** `RequiresSecondApproval` is computed but never enforced; `SecondApproverGrantKey` is stored but never read; two parallel creation paths produce divergent state (`/Requests/TimeOff/Create` skips the approval pipeline entirely).
- **Mental-model mismatch on rotation.** HomeType is conceptually a rotation but the UI is paint-the-dates with no rule editor; admins describe Generate as unreliable.

This spec addresses all four clusters in one coordinated change.

## 4. Architecture overview

The unifying idea is that **vacation, after, and rotation all materialise the same kind of row in the same table** — `ShiftAssignment` rows linked to a HOME-family `ShiftType`. Differences are encoded in two places only:

- **ShiftType key** picks the time window. `HOME` (00:00-23:59), `HOME_PM` (16:00-23:59), `HOME_AM` (00:00-13:00).
- **`SourceTimeOffRequestId`** (new nullable column on `ShiftAssignment`) links a row to its origin TimeOffRequest. Null = rotation. Non-null = vacation or after (read the request to find which).

Everything downstream — validation, rendering, analytics, X-button — reads from these two facts. The chip color is the same for every HOME variant (existing `--shift-home-soft`); the source icon is computed from `SourceTimeOffRequestId` / `TimeOffType`; the chip label comes from the ShiftType. No bespoke per-feature data plumbing.

```
TimeOffRequest (Vacation/After) ─┐
                                 ├─► ShiftAssignment rows ─► render + validate + analytics
HomeType (rotation) ─────────────┘   (HOME / HOME_PM / HOME_AM,
                                      with SourceTimeOffRequestId
                                      for the request-derived ones)
```

## 5. Data model changes

### 5.1 ShiftType — three HOME variants

Seed three rows. Existing `HOME` row stays; the other two are new.

| Key | Start | End | NameKey (en) | NameKey (he) | IsHome |
|---|---|---|---|---|---|
| `HOME`    | 00:00 | 23:59 | "Home"  | "בית"   | true |
| `HOME_PM` | 16:00 | 23:59 | "After" | "אפטר"  | true |
| `HOME_AM` | 00:00 | 13:00 | "After" | "אפטר"  | true |

`ShiftType.IsHome` (currently `[NotMapped] => Key == KEY_HOME`) becomes `Key == KEY_HOME || Key == KEY_HOME_PM || Key == KEY_HOME_AM`. Two new constants alongside `KEY_HOME`. All existing exemption logic (capacity skip, validation exemptions, analytics exclusion) automatically picks up the new variants.

Per-molecule HOME ShiftType seeding still happens; the auto-creation in `HomeTypeService.GenerateHomeShiftsAsync` extends to ensure all three variants exist for the molecule before generating.

### 5.2 ShiftAssignment — one new column

```sql
ALTER TABLE ShiftAssignments ADD COLUMN SourceTimeOffRequestId INTEGER NULL
    REFERENCES TimeOffRequests(Id) ON DELETE SET NULL;
CREATE INDEX IX_ShiftAssignments_SourceTimeOffRequestId
    ON ShiftAssignments(SourceTimeOffRequestId)
    WHERE SourceTimeOffRequestId IS NOT NULL;
```

- Null → rotation HOME (or any non-HOME assignment).
- Non-null → materialised from a TimeOffRequest. The request's `Type` field tells us Vacation vs After.
- `ON DELETE SET NULL`: deleting a TimeOffRequest is rare; if it happens, the rows survive with their source unlinked rather than cascading to data loss.

### 5.3 MoleculeApprovalSettings — new entity

Per-molecule configuration for the dual-approval threshold.

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `MoleculeId` | int | FK, unique |
| `DualApprovalDayThreshold` | int | Default 7 |
| `UpdatedAt` | DateTime | UTC |
| `UpdatedByUserId` | int | FK to Users |

`IBelongsToCompany` is **not** appropriate here (molecule-scoped, not company-scoped). The existing tenant-resolver pattern doesn't apply. Instead the page guarding edits checks: caller is `MoleculeAdmin` of this molecule **or** holds `Director` role-template scoped to a JobType within this molecule.

### 5.4 HomeType — schema unchanged, semantics clarified

No new columns. The redesign uses the same `HomeType` and `HomeTypeOverride` rows, but the input UX changes from paint-dates to rule-config (see §10). The existing `DerivedRule` JSON column becomes the **input**, not a derived value: admins edit it directly via a rule form, and the system uses it verbatim. The `PatternJson` column stays for backwards compatibility (legacy painted patterns continue to work) but is no longer how new HomeTypes are configured.

### 5.5 Removal of `HomeType.DefaultStartTime / DefaultEndTime`

These fields exist on the model but are never read. Migration removes them. (Verified: zero callers in the repo.)

## 6. Approval flow

### 6.1 Approver pool by requester

The seed has a generic `Lead` and `Director` role-template. JobType-specific roles like "AlhutLead" or "TextDirector" are formed by combining a template with `User.JobTypeId`. There is no separate `AlhutLead` row.

Pool is computed from the requester's JobType + Molecule:

| Requester JobType | Eligible approvers (same molecule) |
|---|---|
| Alhut | `Lead` users with JobTypeId = Alhut, **or** `Director` users with JobTypeId = Alhut |
| Text  | `Lead` users with JobTypeId = Text,  **or** `Director` users with JobTypeId = Text |
| Hakam, BR, Other | `BRDirector` users (no JobType filter) **or** `MoleculeAdmin` users |

**Implementation notes for the pool query:**

- `User` has no direct `MoleculeId`. The molecule is derived: `User.CompanyId → Company.MoleculeId`.
- `User.RoleTemplateId` is the FK to `RoleTemplates`; we resolve to `RoleTemplates.Key`.
- `User.JobTypeId` is the FK to `JobTypes` (Id 1=Alhut, 2=BR, 3=Text, 4=Hakam, 5=ProjectManager). The pool rule branches on the numeric Id of the requester's JobType.

Pool query (logical):

```
SELECT u.Id
FROM Users u
JOIN Companies c ON c.Id = u.CompanyId
JOIN RoleTemplates rt ON rt.Id = u.RoleTemplateId
WHERE c.MoleculeId = requesterMoleculeId
  AND u.IsActive = 1
  AND u.Id != requesterUserId
  AND (
    (requesterJobTypeId IN (1, 3)            -- Alhut, Text
     AND rt.Key IN ('Lead', 'Director')
     AND u.JobTypeId = requesterJobTypeId)
    OR
    (requesterJobTypeId NOT IN (1, 3)
     AND rt.Key IN ('BRDirector', 'MoleculeAdmin'))
  )
```

Self-exclusion at the pool level (`u.Id != requesterUserId`) is the first line of self-approval defence; the existing self-approval check in `ApproveAsync` is the second.

### 6.2 Routing default

Primary + fallback. The chosen approver is the primary recipient (notification, queue badge); every other user in the pool sees the request in their queue as "claimable" and can also act. First action wins.

### 6.3 Privacy escape

A `Private` boolean field on `TimeOffRequest`. When true, only the chosen approver sees the request. No fallback peers. Trade-off accepted by the requester: if the chosen person is unavailable the request stalls.

### 6.4 Length-based dual approval

Vacations strictly longer than `MoleculeApprovalSettings.DualApprovalDayThreshold` days require dual approval. After is always single-approval (it's by definition 1 day).

Dual-approval rule: **parallel** (both approvers see it the moment it's submitted, both must approve, either can decline).

| Requester JobType | First approver pool | Second approver pool |
|---|---|---|
| Alhut | `Lead` users with JobTypeId = Alhut | `Director` users with JobTypeId = Alhut |
| Text  | `Lead` users with JobTypeId = Text  | `Director` users with JobTypeId = Text  |
| Hakam, BR, Other | `BRDirector` users | `MoleculeAdmin` users |

The two pools are disjoint by template. Either pool's approval is independent. Both approvals must arrive before the request transitions to `Approved`; any decline immediately fails the request.

**TimeOffRequest field additions for dual approval.** Existing `ApproverId` keeps its meaning: the *chosen* primary at submission time. Two new nullable fields track who actually acted at each tier:

- `FirstApprovalActorId int?`, `FirstApprovalActedAt DateTime?` — first tier's recorded approval (Lead pool, or BRDirector pool for non-alhut/non-text)
- `SecondApprovalActorId int?`, `SecondApprovalActedAt DateTime?` — second tier's approval (Director pool, or MoleculeAdmin pool)

In single-approval cases (vacation ≤ threshold or any After), only `FirstApprovalActorId` is set on approval; `SecondApprovalActorId` stays null. The pre-existing `ApproverId` field stays as the chosen-at-submission value for fallback routing logic.

**Status state-machine for dual approval.** Add an enum value `RequestStatus.PendingSecondApproval` (between `Pending` and `Approved`). Transitions:

```
Pending ─[first tier approves]─> PendingSecondApproval
Pending ─[any tier declines]──> Declined
PendingSecondApproval ─[second tier approves]──> Approved
PendingSecondApproval ─[second tier declines]──> Declined
PendingSecondApproval ─[first tier acts again]──> ignored (already counted)
```

The materialiser fires only on the `→ Approved` transition. Rendering shows `PendingSecondApproval` requests in the second tier's queue with a "Lead has already approved" badge.

### 6.5 Empty-pool fallback

If a requester's strict pool is empty (e.g., a Text employee in a molecule with no TextLead and no Director-Text), MoleculeAdmin becomes eligible. The request UI shows: *"No leads or directors are available in your job type. This request will be reviewed by the molecule admin instead."*

### 6.6 Threshold setting UI

A new admin page: `/Admin/Molecule/ApprovalSettings`. Visible only to users with `MoleculeAdmin` template **or** `Director` template scoped within the current molecule. Single editable field: dual-approval day threshold. Saves to `MoleculeApprovalSettings`.

### 6.7 Single creation surface

Both `/Requests/TimeOff/Create` and `/My/Requests` currently create TimeOffRequests but only the latter calls the approval pipeline. The redesign collapses to one path: `/My/Requests` keeps its form (combined with swaps), and `/Requests/TimeOff/Create` is removed. All submissions run through `IVacationApprovalService.SubmitForApprovalAsync`. The `VacationApprovalEnabled` feature flag stays as a global kill-switch but is on by default.

### 6.8 Approval service shape

`IVacationApprovalService` keeps its existing surface, with these changes:

- `SubmitForApprovalAsync` now consults the per-jobtype pool rule (above) instead of `VacationApprovalRule` rows. The `VacationApprovalRule` table can be deprecated or removed in a follow-up — out of scope for this spec.
- `ApproveAsync` adds a check for dual-approval state. If the request requires dual approval and only one tier has approved, status moves to `PendingSecondApproval` (see §6.4 state machine); the second approval from the *other* tier transitions to `Approved`. Once `Approved`, the materialiser fires (§7).
- `DeclineAsync` and `CancelRequestAsync` trigger the materialiser to remove rows.

## 7. Materialisation

### 7.1 Trigger points

The materialiser runs synchronously in three cases:

1. `IVacationApprovalService.ApproveAsync` — after status transitions to `Approved`.
2. `IVacationApprovalService.DeclineAsync` and `CancelRequestAsync` — after status transitions to `Declined` or `Canceled`.
3. `IVacationApprovalService.UpdateRequestDatesAsync` (new method) — when an admin shortens or edits an Approved request's date range.

### 7.2 Algorithm

`SyncMaterialisedHomeRowsAsync(int requestId)`:

1. Load the TimeOffRequest. If status is not Approved, the desired set is empty.
2. If approved, compute the desired set of `(UserId, WorkDate, ShiftTypeKey)` tuples:
   - Type = After: `[ (UserId, StartDate, HOME_PM), (UserId, StartDate + 1, HOME_AM) ]`
   - Type = Vacation: `[ (UserId, d, HOME) for d in StartDate..EndDate ] + [ (UserId, EndDate + 1, HOME_AM) ]`
3. Load existing rows where `SourceTimeOffRequestId = requestId`.
4. Diff:
   - Insert tuples in desired-not-existing.
   - Delete rows in existing-not-desired.
5. Within a single transaction, apply the diff.
6. Broadcast SignalR `shifts-{moleculeId}-{jobTypeId}` group event so open calendars refresh.

The algorithm is idempotent: re-running on the same request reaches the same state.

### 7.3 Rotation–vacation interaction

When a vacation overlaps existing rotation HOME rows (rule from Q13-B):

- On vacation approval: rotation HOME rows in the vacation's date range are **deleted** (not just marked). The new vacation HOME rows take their place.
- On vacation cancel/decline: the materialiser re-runs the rotation rule for the affected user across the formerly-covered date range and **re-creates** the rotation HOME rows. The deterministic rule makes this safe.

To make rotation auto-restoration possible, the rotation rule must be re-derivable from `HomeType`. This already works (`HomeTypeService.GenerateDatesFromRule`).

### 7.4 HOME_AM dedup with rotation HOME

When materialising a vacation's `HOME_AM` tail (on EndDate+1), if the user already has a rotation HOME on that day, **skip** the HOME_AM materialisation for that day. The rotation HOME (full day) covers the morning anyway. This prevents two HOME chips on the same day.

### 7.5 Transactionality

All materialiser operations run inside `BeginTransactionAsync`. The pre-existing `SaveWithConcurrencyHandlingAsync` pattern wraps the diff. SignalR broadcast happens after commit.

## 8. Validation

### 8.1 Existing rules unchanged

`BusyService.ValidateAsync` keeps:

- `HOME_CONFLICT` (warning, overrideable) — assigning a real shift on a date with HOME.
- `SHIFT_EXISTS_CONFLICT` (warning, overrideable) — assigning HOME on a date with a real shift.
- `DUPLICATE_HOME` (error, hard block) — assigning HOME on a date with HOME.

All three apply to all three HOME variants identically because they're all `IsHome = true`.

### 8.2 Vacation/After source surfacing in warnings

`ValidationIssue` gains an optional `SourceTimeOffRequestId` and `SourceTimeOffType` field. When a HOME_CONFLICT or SHIFT_EXISTS_CONFLICT involves a HOME row whose `SourceTimeOffRequestId` is non-null, the validation result includes those fields. The frontend's `confirmHandler` recognises this and renders an enriched modal with a "Go to Request" deep link instead of a plain warning.

### 8.3 Validation gap fix

The `VACATION_CONFLICT` query in `BusyService` (currently using stored `StartDate..EndDate` only) becomes redundant under the new design — once vacations and afters materialise as HOME rows, the `HOME_CONFLICT` warning naturally covers what `VACATION_CONFLICT` was meant to catch. The original query stays as a safety net for any TimeOffRequests created before the materialiser was deployed (legacy data); a one-off migration runs the materialiser for every approved request older than the deployment date.

### 8.4 The After day-N+1 morning is now covered

Because After's HOME_AM materialises on day-N+1 with time window 00:00-13:00, an attempted shift at 08:00 day-N+1 hits the HOME row directly via `HOME_CONFLICT`. The bug verified in browser testing on 2026-05-04 is structurally fixed.

## 9. Calendar rendering

### 9.1 Chip composition

Every HOME chip on every calendar surface composes from:

- **Color**: `--shift-home-soft` (existing CSS token) — invariant across all variants.
- **Source icon (Lucide)**: `repeat` for rotation (no SourceTimeOffRequestId), `plane` for Vacation, `sunrise` for After.
- **Type icon (Lucide)**: `house` (always present, anchors the "this is home" message).
- **Label**: localized ShiftType.NameKey — "בית" for HOME, "אפטר" for HOME_PM and HOME_AM.
- **Time window**: derived from ShiftType.Start/End (`all day`, `16:00-23:59`, `00:00-13:00`).
- **× button**: existing two-click confirm pattern; click handler routes by source (§11).

Hover tooltip: "Rotation: {HomeType.Name}" or "Vacation request #{id}, approved by {ApproverName}" with a "Go to Request →" link.

### 9.2 Surfaces touched

| Page / view | Current state | After this spec |
|---|---|---|
| `Calendar/Shifts` | Renders HOME chip | Adopts new chip composition; supports HOME_PM/HOME_AM partial-day rendering |
| `Calendar/Overview` | Renders HOME as plain text | Renders the chip with full styling |
| `Calendar/Chores` | Ignores HOME | Renders HOME as a read-only overlay so admins know the user is unavailable |
| `Calendar/OnCall` | Ignores HOME | Same as above |
| `MyTeam` | Ignores HOME | Same as above |
| Bottom-sheet quick-add | Has HOME slash command | Updated to optionally pick HOME_AM/HOME_PM, but in practice partial-day HOME is only created via vacation/after approval, not manually |

### 9.3 Time-window rendering on the Excel-style cell

The Excel calendar already lays chips horizontally within a day cell. For partial-day HOME (HOME_PM, HOME_AM), the chip's width is proportional to its time slice (16:00-23:59 ≈ 33%, 00:00-13:00 ≈ 54%). Existing chip-rendering code already reads `ShiftType.Start/End` for time labels; the partial-width adjustment is a small CSS extension on the chip class.

## 10. HomeType (rotation) rework

### 10.1 Rule-first input

Replace the paint-dates UI on `/Admin/HomeTypes` with a rule editor:

- **Cycle length** (weeks): integer input, default 4.
- **Days of week**: checkboxes Sun-Sat, multi-select.
- **Anchor date**: date input — the first day of week 1 of the cycle. Defaults to the next Sunday after creation.
- **Active range** (optional): start date and end date. If end is null, the rule is open-ended.

Saving the rule writes it to `HomeType.DerivedRule` JSON (existing column, repurposed as the source of truth).

### 10.2 Visual confirmation calendar

Below the rule editor, a read-only calendar shows the next 60 days computed from the rule. Painted dates are highlighted with the home color. Admins cannot click cells to edit; the only way to change the pattern is to edit the rule. This kills the inference step.

### 10.3 Per-user override

A new section on the same page: "Per-user overrides". For each user assigned to this HomeType, the admin can paint specific dates that *override* the rule (either adding extra HOME days or excluding rule-derived days). The existing `HomeTypeOverride` table backs this; the only addition is the UI.

### 10.4 Generation timing

Stay on-demand. To detect "pattern was edited but generation hasn't run yet," add a `LastGeneratedAt DateTime?` column on `HomeType`. The materialiser sets it on every successful Generate. The banner condition is simply `HomeType.UpdatedAt > LastGeneratedAt OR LastGeneratedAt IS NULL`.

When the rule is edited, when users are added/removed, or when a HomeTypeOverride is changed, a **persistent banner** appears at the top of `/Admin/HomeTypes` for that HomeType:

> ⚠ This HomeType has 12 users with HOME shifts generated through 2026-08-31. Your changes won't apply until you regenerate.
>
> [ Regenerate ]

The banner does not auto-dismiss; it disappears only when the admin clicks Regenerate and the regen succeeds (which sets `LastGeneratedAt = now`). Adding/removing a user, editing a HomeTypeOverride, and editing the rule all set `UpdatedAt = now` so the banner naturally resurfaces.

This adds one more migration: `AddLastGeneratedAtToHomeTypes`.

### 10.5 SignalR broadcast on Generate

`HomeTypeService` gets `IHubContext<CalendarHub>` injected. After a successful Generate, the service broadcasts `shifts-{moleculeId}-{jobTypeId}` events for every (moleculeId, jobTypeId) pair affected by the generated rows. Open calendars hear the event and refetch.

## 11. X-button semantics on HOME chips

| Source | × click behavior |
|---|---|
| Rotation HOME (`SourceTimeOffRequestId` is null) | Two-click confirm, then delete the single row. Existing behaviour. |
| Vacation/After HOME (`SourceTimeOffRequestId` non-null) | Two-click confirm, then open a dialog: "Cancel entire request" or "Shorten range". |

The dialog:

- **Cancel entire request**: routes to `IVacationApprovalService.CancelRequestAsync(requestId, currentUserId)`. The materialiser runs, removing all HOME rows for this request. Rotation HOME rows that were displaced by the vacation are auto-restored (§7.3).
- **Shorten range**: opens an inline date trimmer (Vacation only — disabled for single-day After). The admin selects new StartDate / EndDate. On save, `IVacationApprovalService.UpdateRequestDatesAsync` runs the materialiser to diff and apply.
- **Close**: dismisses the dialog without action.

The materialised rows always reflect the request's current state. There is **no single-day partial drift** allowed — the admin cannot delete one day's HOME without changing the request itself.

## 12. Visibility fixes (consequences)

These are not new behaviour; they are gaps the new design closes:

- **SignalR on HomeType generation** — `IHubContext` injection in `HomeTypeService`, broadcast after every successful Generate.
- **SignalR on materialiser run** — `IHubContext` injection in `IVacationApprovalService`, broadcast after every materialiser run.
- **Calendar/Overview HOME styling** — replace the plain-text shift-name rendering with the chip composition from §9.1.
- **Calendar/Chores, /OnCall, MyTeam** — render HOME as a read-only overlay in each user's row for the day. Admins on these calendars instantly see the user is unavailable without switching to /Calendar/Shifts.
- **Justice / fairness ribbon** — already excludes HOME (verified). No change needed; just add HOME_PM/HOME_AM to the exempt list (handled automatically via `IsHome`).

## 13. Migration & backwards compatibility

### 13.1 Schema migrations

- `AddSourceTimeOffRequestIdToShiftAssignments` — adds the nullable FK column and index on `ShiftAssignments`.
- `AddPrivateToTimeOffRequests` — adds `Private BIT NOT NULL DEFAULT 0` column to `TimeOffRequests` (backs §6.3 privacy escape).
- `AddDualApprovalTrackingToTimeOffRequests` — adds `FirstApprovalActorId int NULL`, `FirstApprovalActedAt DateTime NULL`, `SecondApprovalActorId int NULL`, `SecondApprovalActedAt DateTime NULL` columns. FKs to Users; SetNull on delete (preserves history if approver deactivates).
- `SeedHomePartialShiftTypes` — inserts `HOME_PM` and `HOME_AM` rows for every existing molecule that has a `HOME` row.
- `AddMoleculeApprovalSettings` — creates the new table with default rows (DualApprovalDayThreshold = 7) for every existing molecule.
- `RemoveHomeTypeDefaultTimes` — drops `DefaultStartTime` and `DefaultEndTime` columns from `HomeTypes`.
- `AddLastGeneratedAtToHomeTypes` — adds `LastGeneratedAt DateTime NULL` to `HomeTypes` (backs §10.4 banner).

### 13.1.1 Code-only changes (no migration)

- Extend `RequestStatus` enum (in `Models/Support/Enums.cs`) with `PendingSecondApproval = 4` (preserving existing values: Pending=0, Approved=1, Declined=2, Canceled=3). Persisted as int — adding an enum value doesn't require a DDL migration; existing rows keep their current values.
- Update `ShiftType.IsHome` (`[NotMapped]`) from `Key == KEY_HOME` to `Key == KEY_HOME || Key == KEY_HOME_PM || Key == KEY_HOME_AM`. Add the two new constants.
- Update every existing `Key == KEY_HOME` literal across the codebase (per the audit: `BusyService.cs:102, 408-409`, `HomeTypeService.cs:275, 352, 359`, `AnalyticsService.cs:425`) to use `IsHome` instead — guarantees HOME_PM and HOME_AM inherit every existing exemption.

### 13.2 Data backfill

A one-off backfill runs after deployment:

1. For every TimeOffRequest with `Status = Approved` and `EndDate >= deployment_date - 7 days`, run the materialiser. (Older requests are not back-materialised; that data has rolled off operational interest.)
2. Existing manual HOME assignments (from before this spec) keep `SourceTimeOffRequestId = null`. They render as rotation HOME and behave the same as they did before.

### 13.3 Feature flag

The whole change is gated behind a single feature flag: `FF_HOME_UNIFICATION`. When off, the system behaves as it does today (legacy validation path, legacy rendering, legacy approval pipeline). Default off in development; enabled in deployment after smoke testing.

### 13.4 Removing the secondary creation surface

`/Requests/TimeOff/Create` page and its handler are deleted. Any existing routes pointing to it (sidebar links, redirects) are updated to point to `/My/Requests`.

## 14. Testing strategy

### 14.1 Unit tests

- `BusyServiceTests` — add cases for HOME_PM and HOME_AM in mutual-exclusion warnings.
- `ShiftAssignmentServiceTests` — capacity / weekly-hours exemption verified for all three HOME keys.
- `VacationApprovalServiceTests` — extend with dual-approval cases (above threshold, parallel both-approve, parallel one-decline).
- New `HomeMaterialisationServiceTests` — idempotency, vacation→rotation displacement, rotation auto-restore on cancel, HOME_AM dedup.

### 14.2 Integration tests

- End-to-end: submit After, approve, verify two HOME rows exist with correct ShiftType keys and SourceTimeOffRequestId.
- Submit Vacation > 7 days, verify dual-approval routing.
- Cancel Approved Vacation, verify HOME rows are deleted and rotation rows are restored.
- Approve a Vacation that overlaps rotation HOME, verify rotation rows are deleted in vacation's range.

### 14.3 Browser smoke checklist

- The After day-N+1 morning shift assignment, repeated post-deployment with the materialiser active. Expected: HOME_CONFLICT fires (the bug we verified on 2026-05-04 is gone).
- Calendar/Overview shows full HOME chip styling for an approved vacation.
- × on a vacation-derived HOME chip opens the cancel-or-shorten dialog.
- Two open Calendar/Shifts tabs: approve a request in one, verify the other refreshes via SignalR within seconds.

## 15. Implementation order

The brainstorming phase intentionally stops here; the writing-plans skill takes this design and decomposes it into ordered steps. As a sketch:

1. Schema migrations and seed updates (3 ShiftType variants, ShiftAssignment column, MoleculeApprovalSettings).
2. `IsHome` flag generalisation (one-line change with broad downstream effects — must land before any HOME-variant rows exist).
3. Materialisation service + integration into `IVacationApprovalService`.
4. Approval pool / dual-approval rule / `MoleculeApprovalSettings` admin UI.
5. Calendar rendering: chip composition, partial-day widths, surface coverage.
6. HomeType rule-first UI.
7. X-button cancel-or-shorten dialog.
8. SignalR injections and broadcasts.
9. Backfill of existing approved requests.
10. Removal of `/Requests/TimeOff/Create`.

## 16. Out of scope (acknowledged but deferred)

- Swap-request redesign (separate spec).
- `VacationApprovalRule` table cleanup — the per-jobtype pool rule replaces it; deletion is a follow-up.
- Self-approval prevention audit — already enforced by `ApproveAsync`'s self-block, but verifying it across all four entry points is a follow-up.
- Email-template overhaul — current notification emails work; refining the language is non-blocking.
