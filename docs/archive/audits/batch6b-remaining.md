# Batch 6b Audit: Friends, Game, MyTeam, Assignments, Requests, Public Pages

Audited: 2026-03-03
Model: claude-opus-4-6 (Strongest model, per Model Selection Policy section 2.3)

---

## Pages/Friends/Index.cshtml(.cs)

### 1. Identity & Routing
- **File paths**: `C:\Users\katzi\Downloads\ShiftManager\Pages\Friends\Index.cshtml`, `C:\Users\katzi\Downloads\ShiftManager\Pages\Friends\Index.cshtml.cs`
- **Route template**: `@page` (default: `/Friends`)
- **Page handler methods**:
  - `OnGetAsync()` (line 45)
  - `OnPostSendRequestAsync(int friendId)` (line 73)
  - `OnPostAcceptAsync(int id)` (line 96)
  - `OnPostRejectAsync(int id)` (line 119)
  - `OnPostRemoveAsync(int id)` (line 142)
  - `OnPostCancelRequestAsync(int id)` (line 165)

### 2. Access Control & Scope
- **Authorization**: `[Authorize]` (line 12 of .cs) -- any authenticated user
- **Feature flag gate**: `FeatureFlagSeed.Flags.FriendshipsEnabled` checked in OnGetAsync (line 47); returns `NotFound()` if disabled
- **No grant-based checks** -- any authenticated user can manage friendships
- **User scoping**: All operations pass `userId` (parsed from `ClaimTypes.NameIdentifier`) to `IFriendshipService` which presumably enforces ownership
- **Gap**: POST handlers do NOT re-check the `FriendshipsEnabled` feature flag. A user could POST to a handler even when the feature is disabled if they have the URL.

### 3. Localization
- **Pattern**: `IStringLocalizer<SharedResources>` injected in both .cshtml (line 6) and .cs (line 16)
- **`ILocalizationService`** injected in .cshtml (line 7) for date formatting (`FormatMediumDate`, `FormatMonthYear`)
- **`<loc>` tag helpers**: `MyFriends`, `FriendsDescription`, `AddFriend`, `Search`, `Clear`, `AlreadyFriends`, `RequestPending`, `SendRequest`, `PendingRequests`, `OutgoingRequests`, `RequestSentTo`, `Cancel`, `Accept`, `Reject`, `Friends`, `FriendsSince`, `Remove`, `NoFriendsYet`, `NoUsersFound`
- **Inline `@Localizer[]`**: `SearchByNameOrEmail` (placeholder), `ConfirmCancelRequest`, `ConfirmRemoveFriend`
- **Key samples (cs)**: `Error_NotAuthenticated`, `Success_RequestSent`, `Success_RequestAccepted`, `Success_RequestRejected`, `Success_FriendRemoved`, `Success_RequestCanceled`, `Error_Unknown`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` with single item "Friends"
- **Sections**: `@section Styles` with inline `<style>` block (lines 15-44)
- **CSS classes**: `.page-header`, `.page-title`, `.page-subtitle`, `.alert-success`, `.alert-error`, `.section-card`, `.section-header`, `.search-bar`, `.search-input`, `.btn`, `.btn-primary`, `.btn-success`, `.btn-danger`, `.btn-ghost`, `.badge`, `.friend-card`, `.friend-avatar`, `.friend-info`, `.friend-name`, `.friend-meta`, `.friend-actions`, `.empty-state`, `.request-card`, `.request-header`, `.request-time`
- **Design tokens**: Uses CSS custom properties (`--text`, `--text-muted`, `--surface`, `--border`, `--primary`, `--primary-contrast`, `--success`, `--success-text`, `--danger`, `--danger-text`, etc.)
- **No partial views or external CSS/JS files**

### 5. Navigation Map
- **Links TO this page**: Hardcoded `/Friends` link as "Clear" button (line 64)
- **Links FROM this page**: None (self-contained)
- **Redirects**: All POST handlers redirect to self via `RedirectToPage()`, `OnPostSendRequestAsync` preserves `SearchQuery` (line 93)

### 6. Data Dependencies & Side Effects
- **Services**:
  - `IFriendshipService` -- `GetFriendsAsync`, `GetPendingRequestsAsync`, `GetOutgoingRequestsAsync`, `SearchUsersAsync`, `SendRequestAsync`, `AcceptRequestAsync`, `RejectRequestAsync`, `RemoveFriendAsync`
  - `IFeatureFlagService` -- `IsEnabledAsync`
- **DTOs**: `FriendDto`, `FriendRequestDto`, `PotentialFriendDto` (properties: `DisplayName`, `CompanyName`, `JobTypeName`, `IsAlreadyFriend`, `HasPendingRequest`, `UserId`, `FriendshipId`, `FromUserName`, `ToUserName`, `RequestedAt`, `FriendsSince`)
- **No direct DB queries** -- entirely delegated to service layer
- **No SignalR**

### 7. Forms & Submissions
- **GET form**: Search bar (line 57-66) with `SearchQuery` parameter, `method="get"`
- **POST forms** (all with implicit anti-forgery from Razor Pages):
  - `SendRequest` (line 93-96): hidden `friendId`
  - `Accept` (line 124-127): hidden `id`
  - `Reject` (line 128-131): hidden `id`
  - `Remove` (line 185-189): hidden `id`, JavaScript `confirm()` dialog
  - `CancelRequest` (line 153-157): hidden `id`, JavaScript `confirm()` dialog
- **Anti-forgery**: Implicit via `asp-page-handler` tag helper; no explicit `@Html.AntiForgeryToken()` needed since auto-injected

### 8. Interesting Behaviors
- **Feature flag gating**: Page returns 404 when FriendshipsEnabled is off (line 47-48), but POST handlers do NOT re-check the feature flag. A user could theoretically POST to a handler even when the feature is "disabled" if they have the URL.
- **`OnPostCancelRequestAsync` reuses `RemoveFriendAsync`** (line 175, comment on line 174) -- cancel and remove share the same service method
- **TempData flow**: Success/error messages passed via TempData between POST (redirect) and GET
- **`confirm()` dialogs** use `@Localizer[...].Value.Trim()` pattern (lines 154, 186) -- safe for XSS since `.Value` is server-rendered into a JS string literal inside `onclick`
- **Avatar generation**: First character of DisplayName uppercased (lines 74, 175) with fallback to "?" for empty names

### 9. Traceability
- **Logger injected**: `ILogger<IndexModel>` (line 17), but **no logging statements in any handler** -- this is unusual. No audit trail for friendship operations.
- **Error handling**: Service results checked for `.Success`; errors surfaced via TempData
- **No try-catch blocks** -- exceptions will bubble up unhandled

---

## Pages/Game/Leaderboard.cshtml(.cs)

### 1. Identity & Routing
- **File paths**: `C:\Users\katzi\Downloads\ShiftManager\Pages\Game\Leaderboard.cshtml`, `C:\Users\katzi\Downloads\ShiftManager\Pages\Game\Leaderboard.cshtml.cs`
- **Route template**: `@page` (default: `/Game/Leaderboard`)
- **Page handler methods**:
  - `OnGetAsync()` (line 30)

### 2. Access Control & Scope
- **Authorization**: `[Authorize]` (line 12 of .cs) -- any authenticated user
- **Company scoping (E-08 fix)**: Leaderboard scoped to user's company via `CompanyId` claim (lines 33, 42-46). Only users from the same company appear.
- **No grant checks** -- any authenticated user in the company sees the leaderboard
- **Name privacy (B-11)**: Other users' names abbreviated to "First L." via `AbbreviateName` (lines 133-149); current user sees their full name

### 3. Localization
- **Pattern**: `IStringLocalizer<SharedResources>` injected in .cshtml (line 5)
- **`<loc>` tag helpers**: `Game_LeaderboardTitle`, `Game_Rank`, `Game_Player`, `Game_Score`, `Game_You`, `Game_NoScoresYet`
- **Inline `@Localizer[]`**: `Game_AllTime`, `Game_Monthly`, `Game_YourBest`, `Game_BackToGame`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **No Breadcrumb**
- **Sections**: `@section Styles` (lines 182-258), `@section Scripts` (lines 260-281)
- **CSS classes**: `.tab-nav`, `.tab-btn`, `.tab-content`, `.highlight-row`, `.table` (with custom styling)
- **Tab switching**: Two tabs -- "All-Time" and "Monthly" -- via JavaScript `switchTab()` function
- **Emojis**: Extensively used for medals (lines 43-52, 117-126) and headings (line 12)
- **"Back to Game" button**: Uses `window.close()` (line 176) -- assumes opened as popup

### 5. Navigation Map
- **Links TO this page**: Presumably from Game page (not audited here)
- **Links FROM this page**: None (self-contained, `window.close()` only)
- **No redirects**

### 6. Data Dependencies & Side Effects
- **Direct DB access**: `AppDbContext` injected (line 18), queries `GameScores`, `Users` tables directly
- **Key queries**:
  - Company user IDs (line 43-46): `_db.Users.Where(u => u.CompanyId == companyId)`
  - Leaderboard data (lines 54-91): GroupBy UserId, Max Score, Take(10)
  - User best (lines 94-131): Individual user's best score + rank calculation
- **No IgnoreQueryFilters** -- relies on natural query filters (Users table has company filter via tenant resolver)
- **Note**: The company user IDs query at line 43-46 uses `_db.Users.Where(u => u.CompanyId == companyId)`. The tenant query filter on Users already filters by company, so this is effectively a double filter. If the user's `CompanyId` claim does not match the resolved tenant, this could return empty results. In practice this should be fine since the CompanyId claim matches the tenant.

### 7. Forms & Submissions
- **Read-only page** -- no forms or POST handlers

### 8. Interesting Behaviors
- **`window.close()` on "Back to Game"** (line 176): Only works if the page was opened via `window.open()`. In other contexts, this will do nothing or show a browser warning.
- **Name abbreviation logic** (lines 138-149): Uses `parts[^1][0]` (C# index-from-end syntax) for last initial -- handles multi-word names correctly
- **Score formatting**: Uses `N0` format (thousands separator) at lines 66, 85, 141, 160
- **Rank calculation** (lines 115-119): Counts all scores >= user's score to determine rank. Correct but could be expensive for large datasets.
- **Monthly filter** (line 61): Uses `DateTime.UtcNow.ToString("yyyy-MM")` matched against `gs.CurrentMonth` string column -- fragile if time zones differ

### 9. Traceability
- **Logger injected**: `ILogger<LeaderboardModel>` (line 16), but **no logging statements**
- **Silent failures**: If claims are missing (lines 35-39), the page renders with empty data -- no error shown to user, no logging

---

## Pages/MyTeam/Index.cshtml(.cs)

### 1. Identity & Routing
- **File paths**: `C:\Users\katzi\Downloads\ShiftManager\Pages\MyTeam\Index.cshtml`, `C:\Users\katzi\Downloads\ShiftManager\Pages\MyTeam\Index.cshtml.cs`
- **Route template**: `@page` (default: `/MyTeam`)
- **Page handler methods**:
  - `OnGet()` (line 9 of .cs) -- synchronous, empty body

### 2. Access Control & Scope
- **Authorization**: `[Authorize]` (line 6 of .cs) -- any authenticated user
- **No grant checks** in the page model -- all data loading is deferred to client-side JavaScript via API calls
- **API-driven architecture**: Comment on line 11 of .cs: "Frontend will handle all data fetching via API calls"

### 3. Localization
- **Pattern**: `IStringLocalizer<SharedResources>` injected in .cshtml (line 6)
- **`<loc>` tag helpers**: Extensive -- `MyGroups`, `MyGroupsSubtitle`, `MyGroupsExplanation`, `Loading`, `SwitchCalendar`, `ConfigureMembers`, `NewCalendar`, `PreviousWeek`, `ThisWeek`, `NextWeek`, `NoGroupMembers`, `AddGroupMembersToStart`, `MyCalendars`, `CalendarName`, `Save`, `SaveChanges`, `ConfigureGroupMembers`, `DeleteCalendar`, `ConfirmDeleteCalendar`, `DeleteCalendarNote`, `Delete`
- **Localization window object** (lines 976-1043): `window.MyTeamLocalization` serializes 40+ localized strings for JavaScript consumption including day names, month names, status labels, error messages
- **ARIA labels**: `loc-aria-label="Aria_CloseDialog"` on modal close buttons

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent with "MyGroups" item
- **`data-ui-version="v2"`** wrapper div (line 17)
- **Sections**: `@section Styles` (lines 19-838) with extensive inline CSS, `@section Scripts` (lines 974-1046)
- **External JavaScript**: `<script src="~/js/myteam.js"></script>` (line 1045) -- main logic lives in external file
- **Complex CSS**: ~820 lines of inline styles covering:
  - Page header, top action bar, week navigation
  - Member cards with 8-column grid (name + 7 days)
  - Status badges: `.free`, `.vacation`, `.vacation-partial`, `.after-partial`, `.onduty`, `.shift`, `.chore`
  - Dark theme overrides (`[data-theme="dark"]`)
  - Modals (4 total): Switch calendar, Create/Rename calendar, Configure members, Delete confirmation
  - Member selector (two-pane design)
  - Responsive breakpoints: 1200px, 968px (mobile), 768px, 480px
- **Key UI components**:
  - Week view calendar grid with 7-day columns
  - Calendar switcher modal
  - Member configuration modal with search and checkboxes
  - Loading spinner

### 5. Navigation Map
- **Links TO this page**: Presumably from navigation menu
- **Links FROM this page**: None in the static HTML (API-driven navigation)
- **No server-side redirects**

### 6. Data Dependencies & Side Effects
- **No server-side data loading** -- the page model is empty
- **Client-side API calls** via `~/js/myteam.js` (not audited here but referenced at line 1045)
- **Expected API endpoints**: Calendar CRUD, member management, week view data

### 7. Forms & Submissions
- **No server-side forms** -- all interactions are JavaScript-driven
- **Modal forms**: Calendar name input (line 922-923), member selection -- all handled in `myteam.js`

### 8. Interesting Behaviors
- **Empty page model** (3 lines of code in .cs): All logic deferred to client-side JavaScript. The Razor page is essentially a template with no server-side data.
- **Massive inline CSS** (~820 lines): Could be extracted to a separate CSS file for maintainability
- **Localization injection pattern**: `window.MyTeamLocalization` object created by serializing 40+ Localizer values into JSON -- this is a good pattern for SPA-like pages but creates a large inline script block
- **`Json.Serialize` with `Html.Raw`** (lines 980-1042): Used to safely inject localized strings into JavaScript. `Json.Serialize` handles escaping, `Html.Raw` prevents double-encoding.
- **Responsive mobile layout** switches from grid to stacked cards at 968px
- **Four modals** defined in static HTML, shown/hidden via JavaScript

### 9. Traceability
- **No logging** in the page model (empty handler)
- **No error handling** server-side
- **All traceability would be in the API endpoints** called by `myteam.js`

---

## Pages/Assignments/Manage.cshtml(.cs)

### 1. Identity & Routing
- **File paths**: `C:\Users\katzi\Downloads\ShiftManager\Pages\Assignments\Manage.cshtml`, `C:\Users\katzi\Downloads\ShiftManager\Pages\Assignments\Manage.cshtml.cs`
- **Route template**: `@page` (default: `/Assignments/Manage`)
- **Query parameters**: `Date` (DateOnly), `ShiftTypeId` (int), `ReturnUrl` (string?) -- all `BindProperty(SupportsGet = true)`
- **Page handler methods**:
  - `OnGetAsync()` (line 62)
  - `OnPostAsync()` (line 179) -- add user to shift
  - `OnPostRemoveAsync(int assignmentId)` (line 260)
  - `OnPostAssignTraineeAsync(int assignmentId, int traineeUserId)` (line 324)
  - `OnPostRemoveTraineeAsync(int assignmentId)` (line 369)

### 2. Access Control & Scope
- **Authorization**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]` (line 17) -- requires manager-level grant
- **Security audit comment**: Line 15-16 confirms security review
- **Company access validation** (OnGetAsync, lines 80-102):
  1. Parse current user ID from `ClaimTypes.NameIdentifier` with `int.TryParse` (line 81)
  2. Check AdminAccess grant (line 95)
  3. Check DirectorOf relationship (line 96)
  4. Check same company (line 96)
  5. Redirect to `/AccessDenied` if no access (line 101)
- **Cross-company assignment prevention** (OnPostAsync, lines 193-199): Validates selected user's CompanyId matches shift's CompanyId
- **IDOR prevention** (OnPostRemoveAsync, lines 269-288): HIGH-001 fix validates company scope before removal
- **IDOR prevention** (OnPostAssignTraineeAsync, lines 334-353): HIGH-002 fix validates company scope
- **IDOR prevention** (OnPostRemoveTraineeAsync, lines 379-398): HIGH-002 fix validates company scope

### 3. Localization
- **Pattern**: Extends `LocalizedPageModel` with `IStringLocalizer<SharedResources>` (line 31-33)
- **`<loc>` tag helpers**: `BackToCalendar`, `Assigned`, `AddPerson`, `Remove`, `TraineeLabel`, `AssignTrainee`, `RemoveTrainee`, `ShiftNameOptional`, `Add`
- **Inline `@Localizer[]`**: `ManageAssignments`, `Calendar`, `Required`, `Assigned`, `ChooseActiveUser`, `AttachTrainee`, `RemoveUserConfirm`, `OnTimeOff`, `ShiftName`
- **CS error keys**: `Error_SelectUser`, `Error_SelectedUserNotFound`, `Error_CannotAssignCrossCompany`, `Error_CannotOverAssign`, `Error_ConcurrencyConflict`, `Error_InvalidUserClaim`, `Error_FailedToAssignTrainee`, `Error_FailedToRemoveTrainee`
- **Non-localized text** (line 106): `"This name will be displayed as..."` -- hardcoded English string not using Localizer

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent with Calendar > ManageAssignments path
- **No `@section Styles`** -- uses global card/list/btn classes
- **CSS classes used**: `.card`, `.btn`, `.btn-ghost`, `.btn-primary`, `.btn-danger`, `.btn-secondary`, `.list`, `.input`, `.small`
- **Inline script** (lines 149-166): localStorage integration for pending shift names
- **Busy user indicators**: Select options colored by status (vacation=info, shift=danger, chore=warning) with emoji prefixes

### 5. Navigation Map
- **Links TO this page**: From Calendar pages (breadcrumb shows Calendar > ManageAssignments)
- **Links FROM this page**: "BackToCalendar" links to `ReturnUrl` or `/Calendar/Month` (line 26)
- **Redirects**:
  - OnPostAsync: redirects to self with same query params (line 257)
  - OnPostRemoveAsync: redirects to ReturnUrl (if local) or self (line 321)
  - OnPostAssignTraineeAsync/RemoveTraineeAsync: redirect to self (lines 366, 411)
  - Error cases: redirect to `/Error`, `/AccessDenied`, `/Calendar/Month`

### 6. Data Dependencies & Side Effects
- **Direct DB access**: `AppDbContext` with extensive `IgnoreQueryFilters()` usage (lines 65-66, 104-106, 121-123, 131-133, 158-166, 202-204)
- **Services**:
  - `IConflictChecker` -- `CanAssignAsync` (line 211)
  - `INotificationService` -- `CreateShiftAddedNotificationAsync` (line 249), `CreateShiftRemovedNotificationAsync` (line 295)
  - `ICompanyContext` -- injected but not used in visible code (line 25)
  - `IDirectorService` -- `IsDirectorOfAsync` (lines 96, 279, 344, 389)
  - `ITraineeService` -- `GetCompanyTraineesAsync` (line 155), `AssignTraineeToShiftAsync` (line 355), `RemoveTraineeFromShiftAsync` (line 400)
  - `IBusyUserService` -- `GetBusyUsersAsync` (line 169)
  - `IGrantService` -- `HasGrantAsync` (lines 95, 276, 341, 386)
  - `IConcurrencyService` -- `SaveWithConcurrencyHandlingAsync` (lines 112, 239, 306)
- **ShiftInstance auto-creation** (lines 109-119): If no instance exists for the date/shift type, one is created with `StaffingRequired=0`
- **Notifications**: Shift added/removed notifications sent to assigned users (lines 249-255, 295-301)

### 7. Forms & Submissions
- **Remove form** (lines 46-50): POST to `Remove` handler, hidden `assignmentId`, `confirm()` dialog, anti-forgery implicit
- **Assign Trainee form** (lines 56-69): POST to `AssignTrainee`, select dropdown for trainee, hidden fields for context
- **Remove Trainee form** (lines 75-81): POST to `RemoveTrainee`, hidden `assignmentId`
- **Add Person form** (lines 95-146): Default POST handler, select dropdown for user with busy status indicators, optional `ShiftName` text input
- **Anti-forgery**: All forms use `asp-page-handler` which auto-generates anti-forgery tokens

### 8. Interesting Behaviors
- **`OnPostAsync` calls `OnGetAsync()` at line 181**: This reloads all data before processing the POST. While this ensures `Instance` and `Type` are loaded, it also re-runs the access validation -- good for security.
- **StaffingRequired=0 auto-creation** (line 107): When a shift instance doesn't exist, it's created with `StaffingRequired=0`, then `assigned >= Instance.StaffingRequired` check at line 205 would fail since 0 >= 0 is true. This means you CANNOT assign anyone to a newly auto-created instance until staffing is configured. Likely intentional but worth documenting.
- **ReturnUrl validation** (line 321): Uses `Url.IsLocalUrl(ReturnUrl)` to prevent open redirect -- good security practice
- **localStorage shift name** (lines 149-166): JavaScript reads a pending shift name from localStorage (set by a shift creation modal elsewhere) and pre-fills the name input. The key format is `pendingShiftName_{date}_{shiftTypeId}`.
- **Concurrency handling**: All save operations use `SaveWithConcurrencyHandlingAsync` (lines 112, 239, 306)
- **Non-localized display format** (line 18): `@Model.Date.ToString("ddd, MMM d")` uses system locale, not the Localization service -- inconsistent with rest of app

### 9. Traceability
- **Logging**: Extensive structured logging:
  - Line 71: ShiftType not found warning
  - Line 83: Invalid claim error
  - Line 89: User not found error
  - Line 100: Unauthorized access warning
  - Line 182: Assignment attempt info
  - Lines 214-215: Conflict warning
  - Lines 196-197: Cross-company warning
  - Lines 282-283: Unauthorized removal warning
  - Lines 290, 314: Successful removal info
  - Lines 347-348, 392-393: Unauthorized trainee operation warning
- **Error handling**: Concurrency conflicts surfaced via localized error messages; no try-catch blocks (exceptions bubble up)

---

## Pages/Requests/Index.cshtml(.cs)

### 1. Identity & Routing
- **File paths**: `C:\Users\katzi\Downloads\ShiftManager\Pages\Requests\Index.cshtml`, `C:\Users\katzi\Downloads\ShiftManager\Pages\Requests\Index.cshtml.cs`
- **Route template**: `@page` (default: `/Requests`)
- **Page handler methods**:
  - `OnGetAsync()` (line 72)
  - `OnPostApproveTimeOffAsync(int id)` (line 173)
  - `OnPostDeclineTimeOffAsync(int id)` (line 249)
  - `OnPostApproveSwapAsync(int id)` (line 317)
  - `OnPostDeclineSwapAsync(int id)` (line 419)
  - `OnPostDeleteTimeOffAsync(int id)` (line 498)

### 2. Access Control & Scope
- **Authorization**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]` (line 19) -- manager-level grant
- **Security audit comment**: Lines 17-18 confirm audit
- **Company access model** (OnGetAsync, lines 93-116):
  - Admin: sees all companies (line 100)
  - Director: sees director companies + own company (line 109)
  - Regular user: sees only own company (line 114)
- **Per-handler access validation**: `ValidateAccessToRequestAsync()` (lines 612-632) called in every POST handler:
  - Admin: full access (line 615-617)
  - Director: access to managed companies (lines 620-624)
  - Same company: access (lines 626-628)
- **Concurrency checks**: Every POST handler verifies `r.Status == RequestStatus.Pending` before acting (lines 215-222, 290-297, 359-367, 457-464)
- **Input validation**: Every POST handler validates `id > 0` (lines 177, 251, 319, 421)
- **Feature flag gating**: `VacationApprovalEnabled` checked in approve/decline time-off handlers (lines 182, 258)

### 3. Localization
- **Pattern**: Extends `LocalizedPageModel`
- **`ILocalizationService`** injected for date formatting: `FormatMediumDate`, `FormatDateTime`
- **`<loc>` tag helpers**: `Requests`, `ReviewAndApproveTeamRequests`, `PendingTimeOff`, `PendingSwaps`, `Approve`, `Decline`, `Dates`, `Duration`, `Days`, `Reason`, `Shift`, `ProposedTo`, `NoTimeOffRequestsYet`, `AllCaughtUp`, `NoSwapRequestsYet`, `ApprovedTimeOffRequests`, `RequestedOn`, `NoReasonProvided`, `Delete`, `Note`, `TimeOffDeletionNote`, `NoApprovedTimeOffRequestsTitle`, `NoApprovedTimeOffRequestsMessage`, `ConfirmDeleteTimeOff`, `ActiveToday`, `Past`, `Day`
- **Tab labels**: `PendingTimeOffTab`, `PendingSwapsTab`, `ApprovedTimeOffTab`
- **CS error/success keys**: `Error_UserNotAuthenticated`, `Error_UserNotFound`, `Error_AuthenticationError`, `Error_NoPermissionApproveRequest`, `Error_RequestAlreadyProcessed`, `Error_ConcurrencyConflict`, `Error_LoadingRequests`, `Error_CannotApproveSwap`, `Error_NoPermissionDeclineRequest`, `Error_NoPermissionDeleteTimeOff`, `Error_TimeOffRequestNotFound`, `Error_CanOnlyDeleteApprovedTimeOff`, `Error_CannotDeleteStartedTimeOff`, `Error_DeletingTimeOff`, `Success_TimeOffDeleted`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent with "Requests" item
- **`data-ui-version="v2"`** wrapper
- **Sections**: `@section Styles` (lines 20-386), `@section Scripts` (lines 726-821)
- **Tab navigation** (Phase 18): Three tabs with WCAG 2.1 compliance:
  - `role="tablist"`, `role="tab"`, `role="tabpanel"`, `aria-selected`, `aria-controls`, `tabindex`
  - Keyboard navigation: ArrowLeft/Right (RTL-aware), Home/End keys
  - URL hash support with history API
  - Focus management
- **Request cards**: User avatar (first 2 chars), date range, duration, reason, action buttons
- **Count badges** on tabs showing pending count
- **Responsive design**: Three breakpoints (768px, 480px)
- **CSS classes**: `.page-header`, `.page-header-title`, `.page-subtitle`, `.alert-error`, `.requests-section`, `.section-header`, `.section-title`, `.count-badge`, `.tab-navigation`, `.tab-button`, `.tab-panel`, `.request-card`, `.request-content`, `.request-details`, `.request-user`, `.user-icon`, `.request-info`, `.request-info-item`, `.request-reason`, `.request-actions`, `.btn-approve`, `.btn-decline`, `.empty-state`

### 5. Navigation Map
- **Links TO this page**: Navigation menu (manager-level)
- **Links FROM this page**: None (self-contained, all actions redirect to self)
- **Hash navigation**: `#timeoff`, `#swaps`, `#approved` URL hashes for tabs
- **Redirects**: All POST handlers redirect to self via `RedirectToPage()`

### 6. Data Dependencies & Side Effects
- **Direct DB access** with extensive `IgnoreQueryFilters()`:
  - Pending time-off (lines 121-126): Join TimeOffRequests + Users
  - Pending swaps (lines 132-147): 5-table join (SwapRequests, ShiftAssignments, Users, ShiftInstances, ShiftTypes)
  - Approved time-off (lines 156-160): Join TimeOffRequests + Users
- **Services**:
  - `IGrantService` -- `HasGrantAsync` for AdminAccess
  - `IDirectorService` -- `GetDirectorCompanyIdsAsync`
  - `INotificationService` -- `CreateTimeOffNotificationAsync`, `CreateSwapRequestNotificationAsync`, `CreateTimeOffDeletedNotificationAsync`
  - `IConflictChecker` -- `CanAssignAsync` for swap validation
  - `IConcurrencyService` -- `SaveWithConcurrencyHandlingAsync` for all mutations
  - `IVacationApprovalService` -- `ProcessApprovalSideEffectsAsync` for shift removal/trainee cleanup on approval
  - `IFeatureFlagService` -- `IsEnabledAsync` for VacationApprovalEnabled
- **Side effects on approval** (lines 237-244):
  - Time-off approval triggers `ProcessApprovalSideEffectsAsync` (shift removal, trainee cancel, notification)
  - Side effect failures are caught and logged but don't prevent approval -- good resilience pattern
- **Swap approval** (lines 370-407): Transaction-based (`BeginTransactionAsync`) with assignment reassignment

### 7. Forms & Submissions
- **Time-off approve/decline** (lines 503-516): Two forms per request card, hidden `id`
- **Swap approve/decline** (lines 576-590): Two forms per swap card, hidden `id`
- **Delete time-off** (lines 680-687): Form with `onsubmit` confirm dialog, data attributes for XSS-safe confirm message
- **Anti-forgery**: Implicit via `asp-page-handler`
- **Confirm dialog pattern** (line 681): `data-confirm-msg` and `data-user-name` attributes with `this.dataset.confirmMsg.replace('{0}', this.dataset.userName)` -- XSS-safe approach

### 8. Interesting Behaviors
- **ApprovedTimeOffVM.ApprovedAt** (line 160): Set to `r.CreatedAt` instead of actual approval timestamp -- the `ApprovedAt` field always equals `CreatedAt` because the query uses `r.CreatedAt` for both parameters in the VM constructor. The `ApprovedAt` field exists in the VM but is never correctly populated.
- **Delete time-off validation** (lines 554-568): Only future time-off can be deleted; started/past periods are protected
- **"Past" vs "ActiveToday" labels** (lines 691-700): Visual indicators for time-off status
- **Tab keyboard navigation** (lines 771-801): Fully WCAG 2.1 compliant with RTL support
- **Swap decline loads shift info for notification** (lines 469-475): Extra query to build notification message before declining
- **Admin sees ALL companies' pending requests** (line 100): Uses `IgnoreQueryFilters()` to get all company IDs, which could be a large dataset in multi-tenant scenarios

### 9. Traceability
- **Extensive structured logging**:
  - Page load (lines 76, 119, 127, 149-150, 154, 162, 164)
  - Security warnings (lines 207-208, 283-284, 351-352, 450-451, 547-548)
  - Concurrency warnings (lines 217-218, 293-294, 362-363, 460-461)
  - Delete operations (lines 502, 556, 565, 573-574, 594)
  - Errors (line 168, 601)
- **Error handling**: Global try-catch in OnGetAsync (lines 74-171) and OnPostDeleteTimeOffAsync (lines 499-606)
- **Side effect failure isolation** (lines 237-244): `ProcessApprovalSideEffectsAsync` failures logged but don't block approval

---

## Pages/Public/Feedback.cshtml(.cs)

### 1. Identity & Routing
- **File paths**: `C:\Users\katzi\Downloads\ShiftManager\Pages\Public\Feedback.cshtml`, `C:\Users\katzi\Downloads\ShiftManager\Pages\Public\Feedback.cshtml.cs`
- **Route template**: `@page` (default: `/Public/Feedback`)
- **Page handler methods**:
  - `OnGetAsync()` (line 68)
  - `OnPostSubmitAsync()` (line 84)
  - `OnPostMarkToWorkOnAsync(int feedbackId)` (line 146)
  - `OnPostDeleteAsync(int feedbackId)` (line 183)

### 2. Access Control & Scope
- **Authorization**: `[Authorize]` (line 16) -- any authenticated user
- **Owner vs non-owner split** (line 63-66): `CheckIsOwnerAsync` checks `AdminAccess` grant
  - Owner (admin): Sees feedback list, can mark as "to work on", can delete
  - Non-owner: Sees submit form only
- **MarkToWorkOn/Delete handlers**: Both check owner status before proceeding (lines 149, 185), return `Forbid()` if not owner
- **Tenant scoping**: Feedback created with `_tenantResolver.GetCurrentTenantId()` (line 98); feedback list query uses default tenant filter (no `IgnoreQueryFilters()`)
- **Note**: Owner feedback list (line 76) does NOT use `IgnoreQueryFilters()` -- admin/owner can only see feedback from their own tenant/company, not cross-company feedback. This may be intentional.

### 3. Localization
- **Pattern**: Extends `LocalizedPageModel`
- **`<loc>` tag helpers**: `Feedback`, `FeedbackList`, `SubmitFeedback`, `FeedbackType`, `FeedbackContent`, `AddPicture`, `MarkToWorkOn`, `DeleteFeedback`
- **Inline `@Localizer[]`**: `Select`, `FeedbackTypeError`, `FeedbackTypeSuggestion`, `NewFeedback`, `ToWorkOn`, `SubmittedBy`, `ConfirmDeleteFeedback`, `NoFeedback`, `Formats`, `MaxSize`, `Submit`, `FeedbackContentPlaceholder`
- **CS keys**: `Feedback_Error_ContentRequired`, `Feedback_Success_Submitted`, `Feedback_Error_SubmitFailed`, `Feedback_Error_NotFound`, `Feedback_Success_MarkedToWorkOn`, `Feedback_Error_StatusUpdateFailed`, `Feedback_Success_Deleted`, `Feedback_Error_DeleteFailed`, `Feedback_Error_ImageTooLarge`, `Feedback_Error_InvalidImageFormat`, `Feedback_Error_ImageSaveFailed`, `Feedback_Notification_Title`, `Feedback_Notification_Message`, `Feedback_TypeLabel_Error`, `Feedback_TypeLabel_Suggestion`, `Common_Unknown`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent with "Feedback" item
- **Inline `<style>`** (lines 19-146): In the body, NOT in a `@section Styles` block -- differs from other pages and could cause FOUC
- **CSS classes**: `.feedback-container`, `.feedback-form`, `.feedback-list`, `.feedback-item`, `.feedback-header`, `.feedback-meta`, `.feedback-type`, `.feedback-status`, `.feedback-content`, `.feedback-image`, `.feedback-footer`, `.feedback-actions`, `.image-modal`
- **Image modal**: Full-screen image viewer on click (lines 287-301)
- **Two views**: Owner sees feedback list; non-owner sees submission form
- **File upload**: Image upload with JPEG/PNG filter, 5MB max (line 273)

### 5. Navigation Map
- **Links TO this page**: Navigation menu
- **Links FROM this page**: None
- **No redirects** -- all POST handlers return `Page()` after re-calling `OnGetAsync()`

### 6. Data Dependencies & Side Effects
- **Direct DB access**:
  - Feedback list (lines 76-80): `_db.Feedbacks.Include(f => f.Submitter)` with ordering
  - Feedback CRUD: `_db.Feedbacks.FindAsync`, `_db.Feedbacks.Add`, `_db.Feedbacks.Remove`
  - Owner lookup for notification (lines 292-298): `_db.Users.Where(u => u.CompanyId == companyId && u.Role == UserRole.Owner)`
- **Services**:
  - `ITenantResolver` -- `GetCurrentTenantId()` for company scoping
  - `INotificationService` -- `CreateNotificationAsync` for owner notification
  - `IGrantService` -- `HasGrantAsync` for owner check
- **File system operations**:
  - Image save (lines 223-265): Saves to `wwwroot/feedback/{companyId}/{guid}.{ext}`
  - Image delete (lines 267-284): Deletes from same path
- **Notification**: Owner notified when feedback is submitted (lines 286-326)
- **Inconsistency**: Owner notification uses `UserRole.Owner` enum (line 293) to find the owner, not the grant system. If a user has `AdminAccess` grant but is not `UserRole.Owner`, they can view/manage feedback but won't receive notifications.

### 7. Forms & Submissions
- **Submit feedback form** (lines 247-283): `enctype="multipart/form-data"`, explicit `@Html.AntiForgeryToken()`, fields: `SelectedType` (select), `FeedbackContent` (textarea), `Picture` (file input)
- **Mark to work on form** (lines 194-200): Hidden `feedbackId`, explicit `@Html.AntiForgeryToken()`
- **Delete form** (lines 202-209): Hidden `feedbackId`, `confirm()` dialog, explicit `@Html.AntiForgeryToken()`
- **Anti-forgery**: Explicitly added in all forms with `@Html.AntiForgeryToken()` (redundant with Razor Pages auto-generation, but not harmful)

### 8. Interesting Behaviors
- **`white-space: pre-wrap`** on feedback content (line 98 of cshtml): Preserves user formatting
- **Image security**:
  - Extension validation: Only `.jpg`, `.jpeg`, `.png` (line 235)
  - Size validation: Max 5MB (line 229)
  - GUID filenames: Prevents path traversal (line 249)
  - Missing: No MIME type validation (only extension checked). An attacker could upload a file with a valid extension but non-image content. Since images are served as static files, this is low risk.
- **`IO = System.IO` alias** (line 12 of .cs): Used to avoid ambiguity with `System.IO.File`
- **Owner notification uses parameterized localizer** (line 311): `_localizer["...", typeLabel, submitterName, snippet]`
- **No concurrency handling**: `SaveChangesAsync()` called directly without `SaveWithConcurrencyHandlingAsync` -- inconsistent with other pages
- **Feedback type badge display bug** (cshtml lines 176, 181): Shows `"FeedbackTypeError (FeedbackTypeError)"` and `"FeedbackTypeSuggestion (FeedbackTypeSuggestion)"` -- the display renders the localized type label twice in the format `Label (Label)` which is redundant
- **Feedback statuses**: `FeedbackStatus.New` and `FeedbackStatus.ToWorkOn`

### 9. Traceability
- **Logger**: `ILogger<FeedbackModel>` with logging in:
  - Submit error (line 139)
  - Mark to work on error (line 176)
  - Delete error (line 216)
  - Image save error (line 262)
  - Image delete warning (line 281)
  - Owner not found warning (line 298)
  - Notification error (line 323)
- **Error handling**: Try-catch in all POST handlers; errors surfaced via `ErrorMessage` property
- **No audit logging** -- feedback operations are not in the audit trail (unlike Chores/OnDuty)

---

## Pages/Public/Chores.cshtml(.cs)

### 1. Identity & Routing
- **File paths**: `C:\Users\katzi\Downloads\ShiftManager\Pages\Public\Chores.cshtml`, `C:\Users\katzi\Downloads\ShiftManager\Pages\Public\Chores.cshtml.cs`
- **Route template**: `@page` (default: `/Public/Chores`)
- **Query parameters**: `year` (int?), `month` (int?), `moleculeId` (int?) -- OnGetAsync parameters
- **Page handler methods**:
  - `OnGetAsync(int? year, int? month, int? moleculeId)` (line 84)
  - `OnPostCreateChoreAsync()` (line 143)
  - `OnPostReplaceShiftWithChoreAsync()` (line 271)
  - `OnPostCancelChoreAsync()` (line 343)

### 2. Access Control & Scope
- **Authorization**: `[AllowAnonymous]` (line 13 of .cs) -- page is publicly accessible
- **Authenticated check in OnGetAsync** (line 98): Anonymous users see an empty calendar; authenticated users see data
- **Grant-based edit control** in .cshtml (line 18): `canEdit` determined by `AuthorizeAsync(User, "Grant:AssignChores")` -- only users with `AssignChores` grant see edit UI
- **POST handler permission checks**:
  - `OnPostCreateChoreAsync` (line 197): `CanUserManageChoresAsync(currentUserId)` -- delegates to service
  - `OnPostCreateChoreAsync` (line 204): `CanUserManageChoreForAssigneeAsync` -- validates target user
  - `OnPostReplaceShiftWithChoreAsync` (line 298): `CanUserManageChoresAsync`
  - `OnPostCancelChoreAsync` (line 362): `CanUserManageChoresAsync`
- **Security concern**: Page is `[AllowAnonymous]` but POST handlers only check `CanUserManageChoresAsync(currentUserId)` where `GetCurrentUserId()` returns 0 for unauthenticated users. If `CanUserManageChoresAsync(0)` returns true (unlikely but should be verified), anonymous users could create/cancel chores. The `AssigneeId <= 0` check (line 175) provides some protection.
- **`<require-grant>` tag helper** (line 269): `key="AccessAdminNavigation" negate="true"` -- shows "My Chores" count only to non-admin users

### 3. Localization
- **Pattern**: Extends `LocalizedPageModel`
- **`ILocalizationService`**: `FormatMonthYear`, `FormatMonthAbbreviation`
- **`<loc>` tag helpers**: `Chores`, `ManageChoresAndTaskAssignments`, `ChoresCalendar`, `Mon`-`Sun`, `More`, `ChoresList`, `Date`, `Assignee`, `Title`, `Notes`, `CreatedBy`, `Actions`, `NoChoresThisMonth`, `CreateChore`, `SelectAssignee`, `Optional`, `Create`, `Cancel`, `ConfirmDelete`, `AreYouSureDeleteChore`, `Delete`, `ChoreDetails`, `Close`, `Information`, `NoChoreContactManager`, `OK`, `ShiftConflictDetected`, `UserHasShiftOnThisDate`, `DoYouWantToReplaceShift`, `ReplaceShift`, `Calendar_Chores`, `Calendar_Unassigned`, `Calendar_MyItems`, `Today`
- **Inline `@Localizer[]`**: `OK`, `Cancel`, `Delete`, `Close`, `Optional`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent with "Chores" item
- **`data-ui-version="v2"`** wrapper
- **Inline `<style>`** (lines 35-221): In body, not `@section Styles`
- **Calendar grid**: 7-column CSS grid with day headers (Mon-Sun)
- **Chore items**: Green gradient background with broom emoji `::before` pseudo-element
- **Modals** (5 total):
  1. Create chore (lines 393-437)
  2. Delete confirmation (lines 440-456)
  3. Chore details (lines 459-479)
  4. Info modal for employees (lines 482-493)
  5. Conflict resolution modal (lines 496-517) -- conditional on TempData
- **Metrics bar** (Phase 7): Total chores, unassigned count, my chores count
- **Custom dropdown**: Custom select for assignee with busy user indicators
- **Table view**: Below calendar, showing all chores in list format
- **Keyboard navigation (B-037)**: `KeyboardNav.initDropdown`, `KeyboardNav.initCalendar` integration
- **Accessibility**: `role="combobox"`, `role="listbox"`, `role="option"`, `aria-labelledby`, `aria-haspopup`, `aria-expanded`

### 5. Navigation Map
- **Links TO this page**: Navigation menu
- **Links FROM this page**: Month navigation via query params (`/Public/Chores?year=X&month=Y`)
- **Redirects**: POST handlers redirect to self with year/month params

### 6. Data Dependencies & Side Effects
- **Services**:
  - `IChoreService` -- `GetChoresAsync`, `GetEligibleAssigneesAsync`, `CreateChoreAsync`, `GetShiftOnDateAsync`, `ReplaceShiftWithChoreAsync`, `CancelChoreAsync`, `GetChoreByIdAsync`, `CanUserManageChoresAsync`, `CanUserManageChoreForAssigneeAsync`
  - `INotificationService` -- `CreateChoreAssignedNotificationAsync`, `CreateChoreCanceledNotificationAsync`
  - `IAuditLogService` -- `LogAsync` for chore create/cancel/replace
  - `IBusyUserService` -- `GetBusyUsersAsync` for each day in month (lines 132-138)
- **Busy user data serialization** (cshtml line 522): `@Html.Raw(System.Text.Json.JsonSerializer.Serialize(Model.BusyUsersByDate))` -- serialized as JSON for JavaScript
- **Performance concern** (lines 132-138): Calls `GetBusyUsersAsync` once per day in the month (up to 31 calls). Consider batching.
- **Shift conflict resolution**: `ReplaceShiftWithChoreAsync` replaces a shift assignment with a chore

### 7. Forms & Submissions
- **Create chore form** (lines 399-436): Hidden date, custom dropdown for assignee (hidden input), title input, notes textarea, explicit `@Html.AntiForgeryToken()`
- **Delete/cancel form** (lines 447-454): Hidden `ChoreId`, explicit `@Html.AntiForgeryToken()`
- **Replace shift form** (lines 505-516): Hidden `ShiftAssignmentId`, `ChoreTitle`, `ChoreNotes`, explicit `@Html.AntiForgeryToken()`
- **XSS-safe event handling** (lines 528-557): Uses `data-*` attributes and event delegation instead of inline JS

### 8. Interesting Behaviors
- **`[AllowAnonymous]` on public page** (line 13 of .cs): Intentional for public calendar viewing. Data queries use service layer which presumably requires tenant context from authentication.
- **Monday-start week** (cshtml lines 15-16): Calendar starts on Monday, not Sunday -- `AddDays(-(int)firstDay.DayOfWeek + (firstDay.DayOfWeek == DayOfWeek.Sunday ? -6 : 1))`
- **Conflict resolution via TempData** (lines 496-517, cs lines 219-233): When a chore conflicts with a shift, TempData stores conflict info and a modal is shown on redirect asking if the user wants to replace the shift
- **`OnPostCancelChoreAsync` reads form manually** (line 349): `Request.Form["ChoreId"]` instead of model binding -- comment says "to avoid binding conflicts"
- **Date validation** (lines 182-193): Prevents past dates and dates more than 2 years in the future
- **Chore calendar shows max 2 items per cell** (cshtml line 313): Additional chores shown as "+N More" badge
- **`busyUsersByDate` serialized to client** (cshtml line 522): Full busy user data for the month is sent to the browser -- could expose user schedule information to anyone viewing page source (though the page requires auth for data to be populated)

### 9. Traceability
- **Audit logging**: All mutations logged via `IAuditLogService.LogAsync`:
  - ChoreCreated (lines 253-258)
  - ShiftReplacedWithChore (lines 325-330)
  - ChoreCanceled (lines 392-397)
- **Structured logging**: Error logging in catch blocks (lines 265, 336, 403)
- **Security logging**: Unauthorized cancel attempt (line 364)
- **Notification trail**: Chore assigned/canceled notifications sent

---

## Pages/Public/OnDuty.cshtml(.cs)

### 1. Identity & Routing
- **File paths**: `C:\Users\katzi\Downloads\ShiftManager\Pages\Public\OnDuty.cshtml`, `C:\Users\katzi\Downloads\ShiftManager\Pages\Public\OnDuty.cshtml.cs`
- **Route template**: `@page` (default: `/Public/OnDuty`)
- **Query parameters**: `year` (int?), `month` (int?)
- **Page handler methods**:
  - `OnGetAsync(int? year, int? month)` (line 88)
  - `OnPostCreateOnDutyAsync()` (line 152)
  - `OnPostCancelOnDutyAsync()` (line 245)

### 2. Access Control & Scope
- **Authorization**: `[AllowAnonymous]` (line 16 of .cs) -- page is publicly accessible (same pattern as Chores)
- **Authenticated check** (line 101): Anonymous users see empty calendar
- **Grant-based edit control** in .cshtml (line 19): `canEdit` from `AuthorizeAsync(User, "Grant:ManageOnDuty")`
- **POST handler permission checks**:
  - `OnPostCreateOnDutyAsync` (line 193): `CanUserManageOnDutyAsync(currentUserId)`
  - `OnPostCancelOnDutyAsync` (line 262): `CanUserManageOnDutyAsync(currentUserId)`
- **Same security concern as Chores**: `[AllowAnonymous]` + `GetCurrentUserId()` returning 0 for unauthenticated. Need to verify service rejects userId=0.
- **`<require-grant>` tag helper** (line 308): `key="AccessAdminNavigation" negate="true"` for "My On-Duty" count

### 3. Localization
- **Pattern**: Extends `LocalizedPageModel`
- **`ILocalizationService`**: `FormatMonthYear`, `FormatMonthAbbreviation`
- **`<loc>` tag helpers**: `OnDuty`, `ManageOnDutyRotationsAndAssignments`, `OnDutyCalendar`, `Mon`-`Sun`, `More`, `OnDutyList`, `Date`, `Assignee`, `Type`, `Notes`, `CreatedBy`, `Actions`, `NoOnDutyThisMonth`, `CreateOnDuty`, `SelectType`, `OnDutyHakam`, `OnDutyLead`, `SelectAssignee`, `Optional`, `Create`, `Cancel`, `ConfirmDelete`, `AreYouSureDeleteOnDuty`, `Delete`, `OnDutyDetails`, `Close`, `Information`, `NoOnDutyContactManager`, `OK`, `Calendar_OnDuty`, `Calendar_Hakam`, `Calendar_Lead`, `Calendar_MyItems`, `Today`, `Loading`, `NoEligibleAssignees`, `OnDutyRequiresOfficer`
- **Localized type names in create modal** (cshtml lines 470-471): Uses `CultureInfo.CurrentCulture.Name.StartsWith("he")` for Hebrew vs English name selection

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent with "OnDuty" item
- **`data-ui-version="v2"`** wrapper
- **Inline `<style>`** (lines 36-248): In body, not `@section Styles`
- **Calendar grid**: Same 7-column grid pattern as Chores
- **On-duty items**: Two visual styles:
  - `.hakam` -- accent gradient background with shield emoji
  - `.lead` -- warning gradient background with star emoji
- **Modals** (4 total):
  1. Create on-duty (lines 448-519)
  2. Delete confirmation (lines 522-538)
  3. On-duty details (lines 541-561)
  4. Info modal for employees (lines 564-575)
- **Metrics bar** (Phase 7): Total on-duty, Hakam count, Lead count, My on-duty count
- **Dynamic assignee dropdown**: Fetches eligible users from API based on duty type selection
- **Military rank badges**: `rank-badge`, `rank-badge--compact` CSS classes, abbreviations
- **Custom OnDuty types** (Phase 18): Database-configurable types beyond Hakam/Lead
- **Keyboard navigation (B-037)**: Same pattern as Chores

### 5. Navigation Map
- **Links TO this page**: Navigation menu
- **Links FROM this page**: Month navigation via query params
- **API call**: `/Api/OnDuty/GetEligibleUsers?dutyType=X` (cshtml line 701) -- dynamic assignee loading
- **Redirects**: POST handlers redirect to self with year/month params

### 6. Data Dependencies & Side Effects
- **Direct DB access**: `AppDbContext` for `OnDutyTypeConfigs` query (lines 124-127)
- **Services**:
  - `IOnDutyService` -- `GetOnDutiesAsync`, `GetEligibleAssigneesAsync`, `CreateOnDutyAsync`, `CancelOnDutyAsync`, `GetOnDutyByIdAsync`, `CanUserManageOnDutyAsync`
  - `INotificationService` -- `CreateOnDutyAssignedNotificationAsync`, `CreateOnDutyCanceledNotificationAsync`
  - `IAuditLogService` -- `LogAsync` for create/cancel
  - `IBusyUserService` -- `GetBusyUsersAsync` per day (same performance concern as Chores)
- **Busy user data serialization** (cshtml line 580): Same client-side JSON pattern as Chores
- **Custom type loading** (lines 124-127): `_db.OnDutyTypeConfigs.Where(t => t.IsActive).OrderBy(t => t.TypeValue)` -- no `IgnoreQueryFilters()`, so this is tenant-scoped

### 7. Forms & Submissions
- **Create on-duty form** (lines 454-518): Date input, type select (enum + custom types), custom dropdown for assignee, notes textarea, explicit `@Html.AntiForgeryToken()`
- **Cancel form** (lines 529-536): Hidden `OnDutyId`, explicit `@Html.AntiForgeryToken()`
- **XSS-safe event handling**: `data-*` attributes with event delegation (lines 804-833)
- **Dynamic assignee API call** (lines 684-766): Fetches eligible users via `/Api/OnDuty/GetEligibleUsers`, builds DOM elements safely (no `innerHTML` with user data -- uses `createElement` and `textContent`)

### 8. Interesting Behaviors
- **Dynamic assignee filtering** (cshtml lines 684-766): When duty type changes, eligible assignees are re-fetched from API. This handles officer-rank requirements for certain duty types.
- **Officer rank hint** (cshtml lines 505-507): Shows `OnDutyRequiresOfficer` hint when selected type requires officer rank
- **`OnPostCancelOnDutyAsync` reads form manually** (line 250): Same pattern as Chores -- `Request.Form["OnDutyId"]` to avoid binding conflicts
- **Date validation** (lines 177-189): Same pattern as Chores (no past dates, no dates > 2 years future)
- **Notes length validation** (lines 171-175): Max 1000 characters
- **Rank display**: Military rank abbreviations and badges shown next to user names throughout
- **Custom OnDuty type language selection** (cshtml line 470): Checks `CultureInfo.CurrentCulture.Name.StartsWith("he")` -- simple locale check. Using `StartsWith` correctly handles `he-IL` and similar variants.
- **`busyUsersByDate` exposed to client** (cshtml line 580): Same schedule information exposure concern as Chores (mitigated by auth check for data population)
- **Three items shown per calendar cell** (cshtml line 352): vs. 2 for Chores -- minor UI difference
- **No model validation annotations**: Form properties use `[BindProperty]` but no `[Required]`, `[MaxLength]`, etc. -- validation is manual in handlers

### 9. Traceability
- **Audit logging**: All mutations logged via `IAuditLogService.LogAsync`:
  - OnDutyCreated (lines 227-232)
  - OnDutyCanceled (lines 292-297)
- **Structured logging**: Error logging in catch blocks (lines 239, 304)
- **Security logging**: Unauthorized cancel attempt (line 264)
- **Notification trail**: On-duty assigned/canceled notifications sent

---

## Cross-Page Findings Summary

### Security Observations
1. **Chores and OnDuty are `[AllowAnonymous]`** (Chores.cshtml.cs line 13, OnDuty.cshtml.cs line 16) but have POST handlers that mutate data. While service-layer permission checks exist, `GetCurrentUserId()` returns 0 for anonymous users. The services should reject userId=0 but this should be explicitly verified.
2. **Friends page POST handlers don't re-check feature flag** (Index.cshtml.cs lines 73-187): If `FriendshipsEnabled` is disabled after page load, POST operations could still succeed.
3. **Feedback MarkToWorkOn/Delete** uses `AdminAccess` grant for access (Feedback.cshtml.cs line 65) but owner notification uses `UserRole.Owner` enum (Feedback.cshtml.cs line 293) -- inconsistent authorization model.

### Localization Gaps
1. **Assignments/Manage line 106**: Hardcoded English text `"This name will be displayed as..."` not using Localizer.
2. **Assignments/Manage line 18**: `Date.ToString("ddd, MMM d")` uses system locale instead of `ILocalizationService`.
3. **Feedback type badge display** (Feedback.cshtml lines 176, 181): Renders the type label twice as `"Label (Label)"` which appears redundant.

### Architecture Observations
1. **MyTeam page is a pure SPA shell**: Empty page model (Index.cshtml.cs has 3 lines of code), all logic in `myteam.js` + API endpoints.
2. **Chores/OnDuty N+1 performance**: Both pages call `GetBusyUsersAsync` once per day in the month (up to 31 calls). (Chores.cshtml.cs lines 132-138, OnDuty.cshtml.cs lines 141-147)
3. **Chores/OnDuty/Feedback inline styles**: CSS is in the body (`<style>` tag) rather than `@section Styles`, which differs from the pattern used by other pages (Friends, Requests, Leaderboard, MyTeam all use `@section Styles`).

### Missing Patterns
1. **No logging in Friends page**: Logger is injected (line 17) but never used in any handler.
2. **No logging in Leaderboard page**: Logger is injected (line 16) but never used.
3. **No concurrency handling in Feedback page**: Uses bare `SaveChangesAsync()` (lines 123, 168, 208) instead of `SaveWithConcurrencyHandlingAsync`.
4. **No audit logging for Feedback operations**: Unlike Chores/OnDuty which use `IAuditLogService`.
5. **Requests page ApprovedAt data bug**: `ApprovedTimeOffVM.ApprovedAt` always equals `CreatedAt` (Requests/Index.cshtml.cs line 160) because `r.CreatedAt` is passed for both VM constructor parameters.
