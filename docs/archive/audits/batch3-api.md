# Batch 3 - API Pages Audit

Audited: 2026-03-03
Model: claude-opus-4-6
Scope: 29 API-style Razor Pages under `Pages/Api/`

---

## 1. Pages/Api/Calendar/GetShiftsData

**Files:** `Pages/Api/Calendar/GetShiftsData.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/GetShiftsData`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Shadow-refresh endpoint for Shifts calendar. Returns cell-level shift data (instances, assignments, overlays, capacities) for a date range without full page reload.
- **Query Params:** `moleculeId` (int, required), `jobTypeId` (int, required), `startDate` (string, required), `endDate` (string, required)

### 2) Access Control & Scope
- `[Authorize]` -- requires authenticated user
- `[IgnoreAntiforgeryToken]` -- CSRF protection disabled (API pattern)
- Validates `ClaimTypes.NameIdentifier` via `int.TryParse`
- **Tenant scope:** Uses `IScopeFilterService.ResolveCompanyIdsForScopeAsync("molecule", moleculeId)` to resolve company IDs for the molecule. Assignments are loaded via `IgnoreQueryFilters()` but scoped by `instanceIds` which are already molecule+jobType-filtered.
- **Gaps:** No explicit check that the current user belongs to the requested molecule. Any authenticated user can query any moleculeId/jobTypeId combination. Relies on molecule-scoped data not leaking sensitive cross-tenant info.

### 3) Data Dependencies
- `IShiftCalendarService.GetUsersForCalendarAsync(moleculeId, jobTypeId)` -- users
- `IShiftCalendarService.GetShiftInstancesAsync(moleculeId, jobTypeId, start, end)` -- instances
- `IShiftCalendarService.GetOverlaysAsync(moleculeId, start, end)` -- vacation/chore/onDuty overlays
- `IShiftCalendarService.GetCapacitiesBatchAsync(moleculeId, jobTypeId, start, end)` -- staffing capacity
- `AppDbContext.ShiftAssignments` with `IgnoreQueryFilters()` -- assignments joined to instances

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 401: Not authenticated (JSON `{ success: false, message }`)
- 400: Invalid date format or scope params <= 0
- 500: Catch-all exception handler
- All errors return JSON with `success: false`

### 6) Interesting Behaviors
- C-07 optimization: batch-loads capacities in 2 queries instead of N+1
- Uses projection (`Select`) for assignments to avoid loading full User entities
- Security audit comment present confirming `IgnoreQueryFilters()` is safe

### 7) Traceability
- **Services:** `IShiftCalendarService`, `IScopeFilterService`
- **DbContext:** `AppDbContext` (ShiftAssignments)
- **SignalR:** None

---

## 2. Pages/Api/Calendar/GetChoresData

**Files:** `Pages/Api/Calendar/GetChoresData.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/GetChoresData`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Shadow-refresh endpoint for Chores calendar. Returns chores, chore types, and users for a molecule within a date range.
- **Query Params:** `moleculeId` (int, required), `startDate` (string, required), `endDate` (string, required)

### 2) Access Control & Scope
- `[Authorize]` -- requires authenticated user
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Tenant scope:** `IScopeFilterService.ResolveCompanyIdsForScopeAsync("molecule", moleculeId)` resolves company IDs. Chores and users filtered by `companyIds.Contains(...)` with `IgnoreQueryFilters()`.
- **Gaps:** Same as GetShiftsData -- no check that the user belongs to the requested molecule.

### 3) Data Dependencies
- `IChoreTypeService.GetChoreTypesForMoleculeAsync(moleculeId)` -- chore types
- `AppDbContext.Chores` with `IgnoreQueryFilters()` -- chores filtered by companyIds, date range, active only (`CanceledAt == null`)
- `AppDbContext.Users` with `IgnoreQueryFilters()` -- active users in companyIds

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 401: Not authenticated
- 400: Invalid dates or moleculeId <= 0
- 500: Catch-all
- All JSON with `success: false`

### 6) Interesting Behaviors
- Only returns active chores (`CanceledAt == null`)
- Only returns active chore types (`IsActive`)
- Includes chore type color for calendar rendering

### 7) Traceability
- **Services:** `IChoreTypeService`, `IScopeFilterService`
- **DbContext:** `AppDbContext` (Chores, Users)
- **SignalR:** None

---

## 3. Pages/Api/Calendar/GetOnCallData

**Files:** `Pages/Api/Calendar/GetOnCallData.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/GetOnCallData`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Shadow-refresh endpoint for On-Call (Day Shifts) calendar. Returns on-duty entries, on-duty types, and users for an area.
- **Query Params:** `areaId` (int, required), `startDate` (string, required), `endDate` (string, required)

### 2) Access Control & Scope
- `[Authorize]`
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Tenant scope:** `IScopeFilterService.ResolveCompanyIdsForScopeAsync("area", areaId)` resolves company IDs. OnDuties filtered by `companyIds.Contains(o.User.CompanyId)`.
- **Gaps:** No check that user belongs to the requested area.

### 3) Data Dependencies
- `AppDbContext.OnDutyTypeConfigs` -- active on-duty type configs (global, no IgnoreQueryFilters)
- `AppDbContext.OnDuties` with `IgnoreQueryFilters()` -- on-duties filtered by date, active only, user's company in companyIds
- `AppDbContext.Users` with `IgnoreQueryFilters()` -- active users in companyIds

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 401: Not authenticated
- 400: Invalid dates or areaId <= 0
- 500: Catch-all
- All JSON with `success: false`

### 6) Interesting Behaviors
- OnDutyTypeConfigs are global (not scoped by company/area)
- `requiresOfficerRank` exposed in type config for client-side UI

### 7) Traceability
- **Services:** `IScopeFilterService`
- **DbContext:** `AppDbContext` (OnDutyTypeConfigs, OnDuties, Users)
- **SignalR:** None

---

## 4. Pages/Api/Calendar/GetOverviewData

**Files:** `Pages/Api/Calendar/GetOverviewData.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/GetOverviewData`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Shadow-refresh endpoint for Overview calendar. Returns a combined view of users, notes, vacations, chores, on-duties, and shift assignments for a company.
- **Query Params:** `companyId` (int?, optional -- defaults to current user's company), `startDate` (string, required), `endDate` (string, required)

### 2) Access Control & Scope
- `[Authorize]`
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Tenant scope:** Uses `ICompanyContext.CompanyId` as fallback for companyId. Users query relies on tenant query filter (no `IgnoreQueryFilters()` on Users). Vacations, chores, and shifts also rely on tenant filters.
- **Gaps:** The `companyId` query parameter can be supplied by any authenticated user. If the tenant query filter does not restrict by companyId (it filters by the logged-in user's company), then the `companyId` param may not actually filter data correctly -- the user list uses the tenant filter, but notes are fetched for the explicit `effectiveCompanyId`. This could cause a mismatch.

### 3) Data Dependencies
- `AppDbContext.Users` (tenant-filtered, `.Take(2000)` cap)
- `IUserDayNoteService.GetNotesForCompanyAsync(companyId, start, end)` -- notes
- `AppDbContext.TimeOffRequests` (tenant-filtered) -- approved vacations
- `AppDbContext.Chores` (tenant-filtered) -- active chores
- `AppDbContext.OnDuties` (tenant-filtered) -- active on-duties, filtered by userIds
- `AppDbContext.ShiftAssignments` (tenant-filtered, includes ShiftInstance/ShiftType)

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 401: Not authenticated
- 400: Invalid dates or effectiveCompanyId <= 0
- 500: Catch-all

### 6) Interesting Behaviors
- Users query is capped at `Take(2000)` for memory safety
- Uses tenant query filters (no `IgnoreQueryFilters()`) for most queries
- `ICompanyContext` provides the current user's company as default

### 7) Traceability
- **Services:** `IUserDayNoteService`, `ICompanyContext`
- **DbContext:** `AppDbContext` (Users, TimeOffRequests, Chores, OnDuties, ShiftAssignments)
- **SignalR:** None

---

## 5. Pages/Api/Calendar/QuickAddChore

**Files:** `Pages/Api/Calendar/QuickAddChore.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/QuickAddChore`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Quick-create a chore from calendar views.
- **Body Params (JSON):** `AssigneeId` (int), `Date` (string), `Title` (string), `Notes` (string?), `ForceAssign` (bool, default false), `MoleculeId` (int?)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:AssignChores")]` -- requires AssignChores grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Additional service-level checks:**
  - `IChoreService.CanUserManageChoresAsync(currentUserId)` -- 403 if no permission
  - `IChoreService.CanUserManageChoreForAssigneeAsync(currentUserId, assigneeId)` -- 403 if unauthorized for this assignee

### 3) Data Dependencies
- `IChoreService` for creation and permission checks

### 4) Mutations & Side Effects
- **Creates:** A new Chore via `IChoreService.CreateChoreAsync`
- **Notifications:** `INotificationService.CreateChoreAssignedNotificationAsync`
- **Audit:** `IAuditLogService.LogAsync` with action `"ChoreCreatedQuick"`, source `"CalendarQuickAdd"`
- **Success response:** `{ success: true, choreId, message }`

### 5) Error Handling
- 400: Invalid request data, missing title, title > 200 chars, notes > 1000 chars, invalid date, date > 2 years future, date in past, general service failure
- 401: Not authenticated
- 403: No permission or unauthorized assignee
- 409: Shift conflict (`SHIFT_CONFLICT`) or vacation conflict (`VACATION_CONFLICT|...`)
- 500: Catch-all

### 6) Interesting Behaviors
- Input validation: title max 200 chars, notes max 1000 chars
- Date validation: no past dates, no dates > 2 years in future
- Conflict detection with structured response (`conflictType`, vacation details)
- ForceAssign flag allows overriding some conflict checks

### 7) Traceability
- **Services:** `IChoreService`, `INotificationService`, `IAuditLogService`
- **SignalR:** None (notifications are in-app)

---

## 6. Pages/Api/Calendar/QuickAddOnDuty

**Files:** `Pages/Api/Calendar/QuickAddOnDuty.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/QuickAddOnDuty`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Quick-create an on-duty assignment from calendar views.
- **Body Params (JSON):** `AssigneeId` (int), `Date` (string), `OnDutyType` (int -- enum value), `Notes` (string?), `ForceAssign` (bool, default false)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:ManageOnDuty")]` -- requires ManageOnDuty grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Additional service-level check:** `IOnDutyService.CanUserManageOnDutyAsync(currentUserId)` -- 403 if no permission

### 3) Data Dependencies
- `IOnDutyService` for creation and permission checks

### 4) Mutations & Side Effects
- **Creates:** A new OnDuty via `IOnDutyService.CreateOnDutyAsync`
- **Notifications:** `INotificationService.CreateOnDutyAssignedNotificationAsync`
- **Audit:** `IAuditLogService.LogAsync` with action `"OnDutyCreatedQuick"`, source `"CalendarQuickAdd"`
- **Success response:** `{ success: true, onDutyId, message }`

### 5) Error Handling
- 400: Invalid request data, missing assignee, notes > 1000 chars, invalid date, date > 2 years future, date in past, invalid enum value, general service failure
- 401: Not authenticated
- 403: No permission or `OFFICER_RANK_REQUIRED`
- 409: Vacation conflict (`VACATION_CONFLICT|...`)
- 500: Catch-all

### 6) Interesting Behaviors
- Validates `OnDutyType` is a defined enum value
- Notes max 1000 chars
- Officer rank requirement handled as distinct 403 error
- Parallels QuickAddChore in structure and validation

### 7) Traceability
- **Services:** `IOnDutyService`, `INotificationService`, `IAuditLogService`
- **SignalR:** None

---

## 7. Pages/Api/Calendar/DeleteChore

**Files:** `Pages/Api/Calendar/DeleteChore.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/DeleteChore`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Delete (cancel) a chore from calendar views.
- **Body Params (JSON):** `Id` (int -- chore ID)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:AssignChores")]` -- requires AssignChores grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Additional service-level check:** `IChoreService.CanUserManageChoresAsync(currentUserId)`

### 3) Data Dependencies
- `IChoreService.GetChoreByIdAsync(id)` -- loads chore before deletion for notification data
- `IChoreService.CancelChoreAsync(id)` -- soft-delete

### 4) Mutations & Side Effects
- **Mutates:** Sets `CanceledAt` on the Chore (soft-delete via `CancelChoreAsync`)
- **Notifications:** `INotificationService.CreateChoreCanceledNotificationAsync`
- **Audit:** `IAuditLogService.LogAsync` with action `"ChoreDeletedQuick"`, source `"CalendarQuickDelete"`

### 5) Error Handling
- 400: Invalid chore ID, service failure
- 401: Not authenticated
- 403: No permission
- 404: Chore not found
- 500: Catch-all

### 6) Interesting Behaviors
- Loads chore before canceling to capture notification data (title, userId, date)
- Uses soft-delete pattern (CancelChoreAsync sets CanceledAt)

### 7) Traceability
- **Services:** `IChoreService`, `INotificationService`, `IAuditLogService`
- **SignalR:** None

---

## 8. Pages/Api/Calendar/DeleteOnDuty

**Files:** `Pages/Api/Calendar/DeleteOnDuty.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/DeleteOnDuty`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Delete (cancel) an on-duty assignment from calendar views.
- **Body Params (JSON):** `Id` (int -- on-duty ID)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:ManageOnDuty")]` -- requires ManageOnDuty grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Additional service-level check:** `IOnDutyService.CanUserManageOnDutyAsync(currentUserId)`

### 3) Data Dependencies
- `IOnDutyService.GetOnDutyByIdAsync(id)` -- loads on-duty before deletion
- `IOnDutyService.CancelOnDutyAsync(id)` -- soft-delete

### 4) Mutations & Side Effects
- **Mutates:** Sets `CanceledAt` on the OnDuty (soft-delete)
- **Notifications:** `INotificationService.CreateOnDutyCanceledNotificationAsync`
- **Audit:** `IAuditLogService.LogAsync` with action `"OnDutyDeletedQuick"`, source `"CalendarQuickDelete"`

### 5) Error Handling
- 400: Invalid on-duty ID, service failure
- 401: Not authenticated
- 403: No permission
- 404: On-duty not found
- 500: Catch-all

### 6) Interesting Behaviors
- Parallels DeleteChore in structure
- Loads on-duty before canceling for notification data

### 7) Traceability
- **Services:** `IOnDutyService`, `INotificationService`, `IAuditLogService`
- **SignalR:** None

---

## 9. Pages/Api/Calendar/RestoreChore

**Files:** `Pages/Api/Calendar/RestoreChore.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/RestoreChore`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Undo/restore a previously canceled chore.
- **Body Params (JSON):** `Id` (int -- chore ID)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:AssignChores")]` -- requires AssignChores grant
- `[IgnoreAntiforgeryToken]`
- No explicit user ID validation in this handler (relies on policy attribute)

### 3) Data Dependencies
- `IChoreService.RestoreChoreAsync(id)`

### 4) Mutations & Side Effects
- **Mutates:** Restores the chore (clears CanceledAt via service)
- **No notifications or audit logging** -- contrast with DeleteChore which logs both

### 5) Error Handling
- 400: Invalid chore ID or service failure (returns `{ success: false, message }`)
- 200: On success (returns `{ success: true, message }`)
- 500: Catch-all

### 6) Interesting Behaviors
- Very lightweight handler -- no notifications, no audit log
- No explicit current-user permission check beyond the `[Authorize]` policy attribute
- Missing audit trail for restore operations

### 7) Traceability
- **Services:** `IChoreService`
- **SignalR:** None

---

## 10. Pages/Api/Calendar/ShiftHistory

**Files:** `Pages/Api/Calendar/ShiftHistory.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Calendar/ShiftHistory`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Returns shift assignment history from AuditLog entries. Used for "what happened to my shifts" support triage.
- **Query Params:** `userId` (int?, optional), `instanceId` (int?, optional), `limit` (int, default 50). At least one of userId/instanceId required.

### 2) Access Control & Scope
- `[Authorize]`
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Tenant scoping (3-tier):**
  1. Admins (`AdminAccess` grant) -- see all logs
  2. Directors (`DirectorHubAccess` grant) -- see logs for their managed companies + own company
  3. Regular users -- see only their own company's logs
- Company scoping applied via `al.CompanyId` filter on AuditLogs

### 3) Data Dependencies
- `AppDbContext.Users` with `IgnoreQueryFilters()` -- lookup current user's CompanyId and Role
- `AppDbContext.AuditLogs` with `IgnoreQueryFilters()` -- filtered by entity type, tenant scope, userId/instanceId
- `IGrantService.HasGrantAsync` -- AdminAccess, DirectorHubAccess checks
- `IDirectorService.GetDirectorCompanyIdsAsync` -- director's managed companies

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 400: Neither userId nor instanceId provided
- 401: Not authenticated or user not found
- Returns raw JSON array (not wrapped in `{ success, data }`)

### 6) Interesting Behaviors
- Filters AuditLogs for entity types: `ShiftAssignment`, `ShiftInstance`, `Chore`, `OnDuty`
- User-based filtering uses string matching: `al.Description.Contains($"UserId={userId}")` -- fragile if description format changes
- Limit parameter caps results (default 50)
- No 500 catch-all (unhandled exceptions will produce framework-default errors)

### 7) Traceability
- **Services:** `IGrantService`, `IDirectorService`
- **DbContext:** `AppDbContext` (Users, AuditLogs)
- **SignalR:** None

---

## 11. Pages/Api/Friends/Ids

**Files:** `Pages/Api/Friends/Ids.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Friends/Ids`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Returns the current user's friend IDs for calendar friend-highlighting toggle.
- **Query Params:** None

### 2) Access Control & Scope
- `[Authorize]`
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- Scoped to current user only (reads own friend list)

### 3) Data Dependencies
- `IFriendshipService.GetFriendIdsAsync(userId)` -- returns list of friend user IDs

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 401: Invalid user claim
- 500: Catch-all

### 6) Interesting Behaviors
- Simple, single-purpose endpoint
- Returns `{ success: true, friendIds: [...] }`

### 7) Traceability
- **Services:** `IFriendshipService`
- **SignalR:** None

---

## 12. Pages/Api/Game/GetConfiguration

**Files:** `Pages/Api/Game/GetConfiguration.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Game/GetConfiguration`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Returns game configuration (grid size, scoring, milestones) for the current user's company.
- **Query Params:** None

### 2) Access Control & Scope
- `[AllowAnonymous]` -- accessible without authentication
- `[IgnoreAntiforgeryToken]`
- For authenticated users, reads `CompanyId` claim to load company-specific config
- For anonymous users, returns hardcoded defaults

### 3) Data Dependencies
- `AppDbContext.Configs` -- company-specific config entries with `Key.StartsWith("Game")`

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 500: Catch-all (returns `{ error: "Internal server error" }`)

### 6) Interesting Behaviors
- Anonymous users get sensible defaults (game config is cosmetic)
- Config keys: `GameEnabled`, `GameGridSize`, `GamePointsPer3Match`, `GamePointsPer4Match`, `GamePointsPer5PlusMatch`, `GameMegaComboMultiplier`, `GameMegaCombo{3,4,5}MatchMinLines`, `GameMilestones`
- Milestones parsed from comma-separated string with robust int parsing
- Local helper functions `GetConfig`, `GetInt`, `GetBool` for fallback chain

### 7) Traceability
- **Services:** None
- **DbContext:** `AppDbContext` (Configs)
- **SignalR:** None

---

## 13. Pages/Api/Game/GetLeaderboard

**Files:** `Pages/Api/Game/GetLeaderboard.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Game/GetLeaderboard`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Returns top-10 leaderboard data (all-time or monthly) for the user's company.
- **Query Params:** `type` (string, default `"all-time"`, also accepts `"monthly"`)

### 2) Access Control & Scope
- `[Authorize]` -- requires authentication
- No `[IgnoreAntiforgeryToken]` (not needed -- GET only, no custom attributes)
- Validates both `CompanyId` claim and `ClaimTypes.NameIdentifier`
- **Tenant scope:** GameScores filtered by `CompanyId == companyId`

### 3) Data Dependencies
- `AppDbContext.GameScores` -- grouped by UserId, max score per user, top 10
- `AppDbContext.Users` -- display names for leaderboard entries
- For monthly: filters by `CurrentMonth == DateTime.UtcNow.ToString("yyyy-MM")`

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 401: Missing or invalid CompanyId/UserId claims
- 400: Invalid user data (non-parseable claims)
- 500: Catch-all

### 6) Interesting Behaviors
- If current user not in top 10, calculates their rank separately and returns as `userBest`
- Uses `_db.Users.FindAsync(userId)` for current user display name (could be optimized to use projection)
- Leaderboard is company-scoped (not cross-company)

### 7) Traceability
- **Services:** None
- **DbContext:** `AppDbContext` (GameScores, Users)
- **SignalR:** None

---

## 14. Pages/Api/Game/GetLocalization

**Files:** `Pages/Api/Game/GetLocalization.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Game/GetLocalization`)
- **HTTP Methods:** GET only (synchronous `OnGet`)
- **Purpose:** Returns all game-related localization strings for the current UI culture.
- **Query Params:** None

### 2) Access Control & Scope
- `[AllowAnonymous]` -- accessible without authentication
- `[IgnoreAntiforgeryToken]`

### 3) Data Dependencies
- `IStringLocalizer<SharedResources>` -- reads ~40+ localization keys (Game_Title, Game_Instructions, Game_Score, roast messages for each milestone, leaderboard labels)

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 500: Catch-all

### 6) Interesting Behaviors
- Returns a deeply nested JSON object with game UI strings, roast messages (per milestone), and leaderboard labels
- Synchronous handler (`OnGet`, not `OnGetAsync`)
- Roast messages are organized by milestone threshold (1000, 2500, 5000, 7500, 10000, 15000, 20000) with 3 variants each

### 7) Traceability
- **Services:** `IStringLocalizer<SharedResources>`
- **SignalR:** None

---

## 15. Pages/Api/Game/SaveScore

**Files:** `Pages/Api/Game/SaveScore.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Game/SaveScore`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Saves a game score to the leaderboard and returns the user's rank.
- **Body Params (JSON):** `Score` (int)

### 2) Access Control & Scope
- `[Authorize]`
- `[IgnoreAntiforgeryToken]`
- Validates both `ClaimTypes.NameIdentifier` and `CompanyId` claim

### 3) Data Dependencies
- `AppDbContext.GameScores` for rank calculation with `IgnoreQueryFilters()` (global leaderboard by design)

### 4) Mutations & Side Effects
- **Creates:** `GameScore` entity with CompanyId, UserId, Score, PlayedAt, CurrentMonth
- **Saves** via `_db.SaveChangesAsync()`
- **Success response:** `{ success: true, rank, score }`

### 5) Error Handling
- 400: Null data or score <= 0, invalid user data
- 401: Missing claims
- 500: Catch-all

### 6) Interesting Behaviors
- Rank calculation uses `IgnoreQueryFilters()` and counts distinct users with score >= submitted score (global rank, cross-company)
- `CurrentMonth` stored as `"yyyy-MM"` for monthly leaderboard filtering
- No cap on score value -- client-submitted score is trusted
- No duplicate detection -- every POST creates a new GameScore entry

### 7) Traceability
- **Services:** None
- **DbContext:** `AppDbContext` (GameScores)
- **SignalR:** None

---

## 16. Pages/Api/Hierarchy/Create

**Files:** `Pages/Api/Hierarchy/Create.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Hierarchy/Create`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Creates new hierarchy entities (Project, Area, Molecule, Company, Department).
- **Body Params (JSON):** `EntityType` (string), `Name` (string), `ParentId` (int?)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:CreateHierarchy")]` -- requires CreateHierarchy grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`

### 3) Data Dependencies
- `AppDbContext` -- Projects, Areas, Molecules, Companies, Departments with `IgnoreQueryFilters()` for parent lookups
- `ISetupTaskService` -- auto-generates setup tasks for new molecules and companies

### 4) Mutations & Side Effects
- **Creates one of:** Project, Area, Molecule, Company, Department
- **Auto-generates setup tasks** for new Molecules (`GenerateTasksForMoleculeAsync`) and Companies (`GenerateTasksForCompanyAsync`) with duplicate guard
- **Audit:** `IAuditLogService.LogAsync` with action `"HierarchyEntityCreated"`
- **Slug generation:** `GenerateSlug` converts name to lowercase-hyphenated

### 5) Error Handling
- 400: Invalid request data, name > 100 chars, missing ParentId for child entities, invalid entity type
- 401: Not authenticated
- 404: Parent entity not found
- 500: Catch-all

### 6) Interesting Behaviors
- Project creation requires no ParentId; all others require ParentId
- New Molecule defaults to `MoleculeType.Workforce`
- Company's `Slug` is set from name (same as `Name` field)
- Department's parent is Molecule (not Company)
- Name validation: max 100 characters

### 7) Traceability
- **Services:** `IAuditLogService`, `ISetupTaskService`
- **DbContext:** `AppDbContext` (Projects, Areas, Molecules, Companies, Departments)
- **SignalR:** None

---

## 17. Pages/Api/Hierarchy/Delete

**Files:** `Pages/Api/Hierarchy/Delete.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Hierarchy/Delete`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Soft-deletes hierarchy entities (sets `IsActive = false` for most; hard-deletes Companies).
- **Body Params (JSON):** `EntityType` (string), `EntityId` (int)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:DeleteHierarchy")]` -- requires DeleteHierarchy grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`

### 3) Data Dependencies
- `AppDbContext` -- all hierarchy entities with `IgnoreQueryFilters()` for lookups and child checks
- `AppDbContext.DirectorCompanies` -- cleaned up when deleting a Company

### 4) Mutations & Side Effects
- **Project, Area, Molecule, Department:** Soft-delete (`IsActive = false`)
- **Company:** Hard-delete (`_db.Companies.Remove`), with DirectorCompany mappings cleaned up first
- **Audit:** `IAuditLogService.LogAsync` with action `"HierarchyEntityDeleted"`
- **Child existence checks (MED-011):** Only active children block deletion

### 5) Error Handling
- 400: Invalid request data, entity has active children/users, invalid entity type
- 401: Not authenticated
- 404: Entity not found
- 500: Catch-all

### 6) Interesting Behaviors
- MED-011 fix: Only active children block deletion (deactivated entities do not block)
- Company deletion is a **hard delete** (unlike all others which are soft-delete)
- Company deletion also cleans up `DirectorCompanies` mapping table
- Molecule child check does NOT filter by IsActive for Companies (`_db.Companies.IgnoreQueryFilters().AnyAsync(c => c.MoleculeId == ...)` without IsActive check) -- potential inconsistency with other checks

### 7) Traceability
- **Services:** `IAuditLogService`
- **DbContext:** `AppDbContext` (Projects, Areas, Molecules, Companies, Departments, DirectorCompanies, Users)
- **SignalR:** None

---

## 18. Pages/Api/Hierarchy/Move

**Files:** `Pages/Api/Hierarchy/Move.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Hierarchy/Move`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Moves hierarchy entities to a new parent (Area->Project, Molecule->Area, Company->Molecule, Department->Molecule).
- **Body Params (JSON):** `EntityType` (string), `EntityId` (int), `NewParentId` (int?)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:ReorderHierarchy")]` -- requires ReorderHierarchy grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`

### 3) Data Dependencies
- `AppDbContext` -- all hierarchy entities with `IgnoreQueryFilters()` for entity and target lookups

### 4) Mutations & Side Effects
- **Updates:** The parent FK of the entity (e.g., `area.ProjectId = newParentId`)
- **Audit:** `IAuditLogService.LogAsync` with action `"HierarchyEntityMoved"`, captures old and new parent IDs
- **Note:** Moving a Company changes its `MoleculeId`, which cascades into scope filter results

### 5) Error Handling
- 400: Invalid request data, missing NewParentId, invalid entity type
- 401: Not authenticated
- 404: Entity or target parent not found
- 500: Catch-all

### 6) Interesting Behaviors
- Projects cannot be moved (no `case "project"`)
- Captures `oldParentId` before update for audit trail
- Company's `MoleculeId` is nullable (`company.MoleculeId ?? 0`)
- No validation that target is in same tenant/org -- `IgnoreQueryFilters()` allows cross-tenant move if grant is held

### 7) Traceability
- **Services:** `IAuditLogService`
- **DbContext:** `AppDbContext` (Projects, Areas, Molecules, Companies, Departments)
- **SignalR:** None

---

## 19. Pages/Api/Hierarchy/MoveTargets

**Files:** `Pages/Api/Hierarchy/MoveTargets.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Hierarchy/MoveTargets`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Returns valid move targets for a hierarchy entity (e.g., for an Area, returns other active Projects).
- **Query Params:** `entityType` (string), `entityId` (int)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:ReorderHierarchy")]` -- requires ReorderHierarchy grant
- Validates `ClaimTypes.NameIdentifier`

### 3) Data Dependencies
- `AppDbContext` -- entity lookup and target list with `IgnoreQueryFilters()`
- Area targets: other active Projects (excluding current parent)
- Molecule targets: other active Areas (excluding current parent)
- Company/Department targets: other active Molecules (excluding current parent)

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 400: Invalid entityType or entityId <= 0
- 401: Not authenticated
- 404: Entity not found
- 500: Catch-all
- All errors include `targets: []` in response for client convenience

### 6) Interesting Behaviors
- Projects return empty targets (top-level, cannot be moved)
- Excludes current parent from targets to prevent no-op moves
- Only returns active entities as targets

### 7) Traceability
- **Services:** None
- **DbContext:** `AppDbContext` (Projects, Areas, Molecules, Companies, Departments)
- **SignalR:** None

---

## 20. Pages/Api/Hierarchy/Rename

**Files:** `Pages/Api/Hierarchy/Rename.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Hierarchy/Rename`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Renames a hierarchy entity's DisplayName (inline edit).
- **Body Params (JSON):** `EntityType` (string), `EntityId` (int), `Name` (string)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:EditHierarchy")]` -- requires EditHierarchy grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`

### 3) Data Dependencies
- `AppDbContext` -- entity lookup with `IgnoreQueryFilters()`

### 4) Mutations & Side Effects
- **Updates:** `DisplayName` property of the entity
- **Audit:** `IAuditLogService.LogAsync` with action `"HierarchyEntityRenamed"`, captures old and new name

### 5) Error Handling
- 400: Invalid request data, name > 100 chars, invalid entity type
- 401: Not authenticated
- 404: Entity not found
- 500: Catch-all

### 6) Interesting Behaviors
- Only updates `DisplayName`, not `Name` or `Slug`
- Name max 100 characters
- Applies `.Trim()` to new name

### 7) Traceability
- **Services:** `IAuditLogService`
- **DbContext:** `AppDbContext` (Projects, Areas, Molecules, Companies, Departments)
- **SignalR:** None

---

## 21. Pages/Api/Hierarchy/Reorder

**Files:** `Pages/Api/Hierarchy/Reorder.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Hierarchy/Reorder`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Reorders hierarchy entities by updating `SortOrder` (drag-drop reordering).
- **Body Params (JSON):** `EntityType` (string), `OrderedIds` (List<int>), `ParentId` (int?)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:ReorderHierarchy")]` -- requires ReorderHierarchy grant
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- Valid entity types: `area`, `molecule`, `company`, `department` (static HashSet)

### 3) Data Dependencies
- `AppDbContext` -- entities with `IgnoreQueryFilters()` filtered by `OrderedIds.Contains(id)`

### 4) Mutations & Side Effects
- **Updates:** `SortOrder` property on each entity to match array position
- **Audit:** `IAuditLogService.LogAsync` with action `"HierarchyReorderApplied"`

### 5) Error Handling
- 400: Invalid request data, invalid entity type, invalid IDs, entities from different parents
- 401: Not authenticated
- 500: Catch-all

### 6) Interesting Behaviors
- Validates that all entities in the ordered list belong to the same parent (prevents cross-parent reordering)
- Validates count of loaded entities matches count of requested IDs
- Does not support Project reordering
- Uses `ApplyReorderAsync` helper method with per-type switch

### 7) Traceability
- **Services:** `IAuditLogService`
- **DbContext:** `AppDbContext` (Areas, Molecules, Companies, Departments)
- **SignalR:** None

---

## 22. Pages/Api/Localization

**Files:** `Pages/Api/Localization.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Localization`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Client-side JS localization endpoint. Returns localized strings for given keys, respecting current culture and company overrides.
- **Query Params:** `keys` (string, comma-separated list of localization keys)
- **Layout:** `null`

### 2) Access Control & Scope
- `[AllowAnonymous]` -- accessible without authentication
- `[IgnoreAntiforgeryToken]`
- Reads `SelectedCompanyId` or `CompanyId` claim if available for company-specific overrides

### 3) Data Dependencies
- `IStringLocalizer<SharedResources>` -- base localization
- `ICompanyLocalizationService.GetOverrideValueAsync(companyId, culture, key)` -- company-specific overrides

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- Returns empty dict on empty/whitespace keys (200 OK)
- 500: Catch-all (returns empty dict with 500 status)

### 6) Interesting Behaviors
- Company localization overrides take priority over base localization
- Uses `SelectedCompanyId` claim first, falls back to `CompanyId`
- Returns `Dictionary<string, string>` (key -> localized value)
- Keys are deduped and trimmed
- On error, returns empty dict (JS client uses key names as fallback)

### 7) Traceability
- **Services:** `IStringLocalizer<SharedResources>`, `ICompanyLocalizationService`
- **SignalR:** None

---

## 23. Pages/Api/OnDuty/GetEligibleUsers

**Files:** `Pages/Api/OnDuty/GetEligibleUsers.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/OnDuty/GetEligibleUsers`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Returns users eligible for a specific on-duty type, with rank information. Used for dropdown population.
- **Query Params:** `dutyType` (int?, optional -- if null, returns all eligible users)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:ManageOnDuty")]` -- requires ManageOnDuty grant
- `[IgnoreAntiforgeryToken]`

### 3) Data Dependencies
- `IOnDutyService.GetEligibleAssigneesAsync()` -- all eligible users (no type filter)
- `IOnDutyService.RequiresOfficerForDutyTypeAsync(onDutyType)` -- check if officer rank required
- `IOnDutyService.GetEligibleUsersForDutyAsync(onDutyType, requiresOfficer)` -- filtered users

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 400: Invalid duty type (undefined enum value)
- 500: Catch-all

### 6) Interesting Behaviors
- Returns rich rank information per user: `rank`, `rankAbbreviation`, `rankDisplayName`, `rankBadgeClass`, `isOfficer`
- Includes user email in response (may be a privacy concern depending on context)
- `requiresOfficer` flag returned for client-side UI logic

### 7) Traceability
- **Services:** `IOnDutyService`
- **SignalR:** None

---

## 24. Pages/Api/ScheduleExport

**Files:** `Pages/Api/ScheduleExport.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/ScheduleExport`)
- **HTTP Methods:** POST only (`OnPostAsync`)
- **Purpose:** Exports schedule data as PDF, Excel, or CSV file download.
- **Body Params:** `[FromBody] ScheduleExportRequest` (model-bound; includes StartDate, EndDate, Format, and likely scope filters)
- **Layout:** `null`

### 2) Access Control & Scope
- `[Authorize]`
- `[IgnoreAntiforgeryToken]`
- Relies on `IScheduleExportService` for scope/tenant filtering

### 3) Data Dependencies
- `IScheduleExportService.CollectExportDataAsync(request)` -- collects schedule data
- `IScheduleExportService.GeneratePdfAsync/GenerateExcelAsync/GenerateCsvAsync` -- generates file bytes

### 4) Mutations & Side Effects
- **None** (generates file download)
- **Returns:** `File(fileBytes, contentType, fileName)` -- binary file response

### 5) Error Handling
- 400: Null request, date range > 365 days, unsupported format
- Date range validation returns localized key: `"ScheduleExport_DateRangeExceeded"`
- No 500 catch-all (unhandled exceptions produce framework errors)

### 6) Interesting Behaviors
- Date range capped at 365 days to prevent excessive resource consumption
- Supports 3 formats: PDF, Excel (.xlsx), CSV
- File name pattern: `schedule-{startDate:yyyy-MM-dd}.{ext}`
- No explicit pagination or size limits beyond the date range check

### 7) Traceability
- **Services:** `IScheduleExportService`
- **SignalR:** None

---

## 25. Pages/Api/ScopeSwitcher

**Files:** `Pages/Api/ScopeSwitcher.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/ScopeSwitcher`)
- **HTTP Methods:** GET with two handlers:
  - `OnGetAsync(calendarType?)` -- default handler, returns user's available scopes
  - `OnGetHierarchyAsync()` -- handler `?handler=Hierarchy`, returns full org hierarchy
- **Query Params:** `calendarType` (string?, optional for default handler)
- **Layout:** `null`

### 2) Access Control & Scope
- `[Authorize]`
- `[IgnoreAntiforgeryToken]`
- Validates `ClaimTypes.NameIdentifier`
- **Scope logic:** Multi-level grant checks to determine what scopes user can access:
  - All users get "mine" scope
  - Directors see all non-HQ companies in their molecule
  - Regular users see their own company
  - Molecule/area grants checked dynamically based on `calendarType`
  - Admins/Directors see full org hierarchy

### 3) Data Dependencies
- `AppDbContext.Users` (with Department include)
- `ICompanyCacheService.GetCompanyAsync`
- `IHierarchyService.GetUserHierarchyContextAsync`
- `IGrantService.HasGrantAsync` -- checks AdminAccess, DirectorHubAccess, ViewShiftsMolecule, ViewShiftsArea (or dynamic variants)
- `AppDbContext.Companies` -- for directors' molecule companies
- `AppDbContext.Projects` with eager loading (Areas->Molecules->Companies/Departments) -- for full hierarchy

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 401: Invalid user ID claim
- 500: Catch-all
- **Cache-Control:** `no-store, no-cache, must-revalidate` + `Pragma: no-cache` headers set

### 6) Interesting Behaviors
- Dynamically constructs grant keys from calendar type: `View{CalendarType}Molecule`, `View{CalendarType}Area`
- Directors see non-HQ companies only (`!c.IsHeadquarters`)
- Full hierarchy handler eager-loads Project->Area->Molecule->Company/Department tree
- Regular users in hierarchy handler only see their own path
- `UserContextDto` exposed: isWorkforce, isTech, isOwner, isDirector
- Response DTOs defined in same file (`ScopeSwitcherResponse`, `ScopeDto`, `CurrentScopeDto`, etc.)

### 7) Traceability
- **Services:** `IGrantService`, `IHierarchyService`, `ICompanyCacheService`
- **DbContext:** `AppDbContext` (Users, Companies, Projects with deep includes)
- **SignalR:** None

---

## 26. Pages/Api/SessionStatus

**Files:** `Pages/Api/SessionStatus.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/SessionStatus`)
- **HTTP Methods:** GET only (`OnGet`)
- **Purpose:** Checks session authentication status. Returns state (ok/warning/expired) and time remaining.
- **Query Params:** None
- **Layout:** `null`

### 2) Access Control & Scope
- `[AllowAnonymous]` -- must be accessible to check if session expired
- No CSRF attributes (GET-only, no [IgnoreAntiforgeryToken])

### 3) Data Dependencies
- `HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)` -- reads auth ticket
- `ClaimTypes.NameIdentifier` -- validates user ID from claims

### 4) Mutations & Side Effects
- **Implicit:** Calling this endpoint triggers sliding expiration on the auth cookie (extends session)
- Response indicates `slidingExpirationTriggered: true`

### 5) Error Handling
- 401: Not authenticated (multiple paths: no identity, auth ticket failed, invalid user ID, expired)
- 500: Catch-all
- Non-AJAX unauthenticated requests redirect to `/Auth/Login?reason=authRequired`

### 6) Interesting Behaviors
- **Warning threshold:** 60 minutes remaining triggers `state: "warning"`
- **AJAX detection:** Uses `X-Requested-With: XMLHttpRequest` header
- **Cache prevention:** `Cache-Control: no-store, no-cache, must-revalidate`, `Pragma: no-cache`, `Expires: 0`
- **Minimized response:** Does not expose userId, username, issuedAt, expiresAt to avoid leaking session details
- Extensive logging at every step (IP, cookie presence, auth state)
- Default timeRemaining is 7 days if ExpiresUtc not set

### 7) Traceability
- **Services:** None (uses ASP.NET Core authentication directly)
- **SignalR:** None

---

## 27. Pages/Api/Signup/GetSignupOptions

**Files:** `Pages/Api/Signup/GetSignupOptions.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Signup/GetSignupOptions`)
- **HTTP Methods:** GET with 6 handlers:
  - `OnGetMoleculesAsync()` -- `?handler=Molecules`
  - `OnGetCompaniesAsync(moleculeId)` -- `?handler=Companies&moleculeId=X`
  - `OnGetJobTypesAsync(moleculeId)` -- `?handler=JobTypes&moleculeId=X`
  - `OnGetDepartmentsAsync(moleculeId)` -- `?handler=Departments&moleculeId=X`
  - `OnGetAllJobTypesAsync()` -- `?handler=AllJobTypes`
  - `OnGetRoleTemplatesAsync()` -- `?handler=RoleTemplates`
- **Query Params:** `moleculeId` (int, required for Companies/JobTypes/Departments handlers)

### 2) Access Control & Scope
- `[AllowAnonymous]` -- accessible without authentication (for public signup form)
- `[IgnoreAntiforgeryToken]`
- **Feature flag gate:** Every handler checks `IsPublicSignupEnabledAsync()` which reads `AllowPublicSignup` flag. Returns 403 if disabled.

### 3) Data Dependencies
- `AppDbContext.Molecules` -- active molecules
- `AppDbContext.Companies` -- companies by molecule (excludes HQ)
- `IJobTypeService.GetJobTypesForMoleculeAsync` / `GetAllJobTypesAsync` -- job types
- `AppDbContext.Departments` -- departments by molecule
- `IRoleService.GetSignupRoleTemplatesAsync` -- role templates for signup
- `IStringLocalizer<SharedResources>` -- resolves NameKey to localized display name
- `IFeatureFlagService.IsEnabledAsync` -- public signup feature flag

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 403: Public signup disabled
- 400: Invalid moleculeId
- 500: Catch-all (returns `{ error: "Error fetching ..." }`)

### 6) Interesting Behaviors
- Cascading dropdown pattern: Molecules -> Companies/Departments/JobTypes filtered by moleculeId
- Companies exclude HQ companies (`!c.IsHeadquarters`)
- Role templates include `ScopeLevel` for client-side filtering by molecule type
- DTOs defined in same file: `MoleculeOption`, `CompanyOption`, `DepartmentOption`, `JobTypeOption`, `RoleTemplateOption`
- `MoleculeOption` includes `Type` (Workforce=0, Tech=1, Helper=2, System=3)
- RoleTemplates localization resolved server-side

### 7) Traceability
- **Services:** `IFeatureFlagService`, `IJobTypeService`, `IRoleService`, `IStringLocalizer<SharedResources>`
- **DbContext:** `AppDbContext` (Molecules, Companies, Departments)
- **SignalR:** None

---

## 28. Pages/Api/TechShift/Eligible

**Files:** `Pages/Api/TechShift/Eligible.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/TechShift/Eligible`)
- **HTTP Methods:** GET only (`OnGetAsync`)
- **Purpose:** Returns users eligible for a specific tech shift type (e.g., HANAVA -> CanBeAssignedHanava grant).
- **Query Params:** `type` (string?, required -- tech shift type), `companyId` (int?, optional)

### 2) Access Control & Scope
- `[Authorize(Policy = "Grant:ManageShifts")]` -- requires ManageShifts grant
- `[IgnoreAntiforgeryToken]`

### 3) Data Dependencies
- `ITechShiftService.GetEligibleUsersForTechShiftAsync(type, companyId)` -- returns eligible users

### 4) Mutations & Side Effects
- **None** -- read-only endpoint

### 5) Error Handling
- 400: Missing or whitespace `type` parameter
- 500: Catch-all

### 6) Interesting Behaviors
- Explicit security comment: "NEVER return raw AppUser (exposes PasswordHash, etc.)" -- projects to `{ Id, DisplayName }` only
- Optional `companyId` filter for scoping within a specific company

### 7) Traceability
- **Services:** `ITechShiftService`
- **SignalR:** None

---

## 29. Pages/Api/Telemetry

**Files:** `Pages/Api/Telemetry.cshtml` + `.cshtml.cs`

### 1) Identity & Routing
- **Route:** `@page` (default: `/Api/Telemetry`)
- **HTTP Methods:** POST only, with 6 handlers:
  - `OnPostEventAsync([FromBody] AnalyticsEventDto)` -- `?handler=Event`
  - `OnPostEventBatchAsync([FromBody] List<AnalyticsEventDto>)` -- `?handler=EventBatch`
  - `OnPostErrorAsync([FromBody] ClientErrorDto)` -- `?handler=Error`
  - `OnPostErrorBatchAsync([FromBody] List<ClientErrorDto>)` -- `?handler=ErrorBatch`
  - `OnPostPerformanceAsync([FromBody] PerformanceMetricDto)` -- `?handler=Performance`
  - `OnPostPerformanceBatchAsync([FromBody] List<PerformanceMetricDto>)` -- `?handler=PerformanceBatch`

### 2) Access Control & Scope
- `[AllowAnonymous]` -- allows telemetry from unauthenticated pages (login, etc.)
- `[IgnoreAntiforgeryToken]` -- client-side telemetry needs to work without CSRF tokens
- **User identification:** Uses hashed user ID (SHA256) for authenticated users, hashed IP for anonymous users -- avoids storing PII

### 3) Data Dependencies
- `IClientTelemetryService` -- LogEventAsync, LogEventBatchAsync, LogErrorAsync, LogErrorBatchAsync, LogPerformanceAsync, LogPerformanceBatchAsync

### 4) Mutations & Side Effects
- **Creates:** ClientAnalyticsEvent, ClientError, PerformanceMetric entries
- All data persisted via `IClientTelemetryService`

### 5) Error Handling
- 429: Rate limit exceeded (30 requests/minute per IP)
- 400: Missing required fields (EventType, Message, MetricName)
- Returns `{ success: true, id/count }` on success

### 6) Interesting Behaviors
- **Rate limiting:** In-memory `ConcurrentDictionary<string, Queue<DateTime>>` per IP, 30 req/min, periodic cleanup every 5 minutes or when > 1000 tracked IPs
- **D-08 field truncation:** All string fields truncated to max lengths to prevent data stuffing:
  - EventType/MetricName: 100 chars
  - EventData: 2000 chars
  - StackTrace: 5000 chars
  - Source/PageUrl/Rating: various limits
- **Batch limits:** Events 50 max, Errors 20 max, Performance 50 max
- **Browser info parsing:** Simple UA string matching (Chrome, Firefox, Edge, Safari, IE)
- DTOs defined in same file: `AnalyticsEventDto`, `ClientErrorDto`, `PerformanceMetricDto`
- Uses `static readonly` for rate limit state -- shared across all requests (thread-safe via lock)
- Lock-based thread safety for Queue operations within ConcurrentDictionary

### 7) Traceability
- **Services:** `IClientTelemetryService`
- **SignalR:** None

---

## Cross-Cutting Observations

### Common Patterns
1. **All API pages** use `[IgnoreAntiforgeryToken]` for JSON API compatibility (except GetLeaderboard which is GET-only with no attribute).
2. **Standard error envelope:** Most endpoints return `{ success: bool, message: string }` with appropriate HTTP status codes.
3. **User validation:** Almost all authenticated endpoints validate `ClaimTypes.NameIdentifier` via `int.TryParse`.
4. **IgnoreQueryFilters usage:** Calendar data endpoints, hierarchy management, and game scores all use `IgnoreQueryFilters()` with documented security audit comments.

### Security Observations
1. **Calendar data endpoints (GetShiftsData, GetChoresData, GetOnCallData)** do not verify the requesting user belongs to the requested molecule/area. Any authenticated user can query any scope.
2. **GetOverviewData** accepts a `companyId` parameter that may bypass the tenant filter mismatch between users (tenant-filtered) and notes (explicit companyId).
3. **SaveScore** trusts client-submitted scores with no server-side validation or anti-cheat measures.
4. **RestoreChore** lacks audit logging and notification, unlike its counterpart DeleteChore.
5. **ShiftHistory** uses fragile string matching on AuditLog descriptions for user filtering.
6. **Hierarchy Delete** has inconsistent child-check behavior: Company children under Molecule are checked without IsActive filter, while other child checks filter by IsActive.
7. **GetSignupOptions** exposes organizational structure data (molecules, companies, departments, job types, role templates) to anonymous users when public signup is enabled.

### Missing Error Handlers
- **ShiftHistory** and **ScheduleExport** lack global try/catch blocks.
- **SessionStatus** has catch-all but logs extensively, which could leak info in logs.

### Performance Notes
- **GetOverviewData** caps user list at 2000 to prevent memory issues.
- **Telemetry** uses in-memory rate limiting (30 req/min/IP) with periodic cleanup.
- **GetShiftsData** uses C-07 batch capacity optimization.
- **ScopeSwitcher Hierarchy** eager-loads the entire Project->Area->Molecule->Company/Department tree for admins/directors.
