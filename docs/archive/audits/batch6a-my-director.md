# Batch 6a Audit: My/* and Director/* Pages

Audited: 2026-03-03
Model: claude-opus-4-6 (strongest model, per CLAUDE.md policy -- final validation tier)

---

## Pages/My/Index.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/My/Index.cshtml`, `Pages/My/Index.cshtml.cs`
- **Route template**: `@page` (default: `/My` or `/My/Index`)
- **Page handler methods**: `OnGetAsync(string? view, string? range, DateOnly? customStart, DateOnly? customEnd)` (line 49)
- **Partial view**: `Pages/My/_TimelineItem.cshtml` used via `Html.PartialAsync` (lines 505, 517, 529, 543)

### 2. Access Control & Scope
- `[Authorize]` on class (line 12 .cs) -- any authenticated user
- User ID from `ClaimTypes.NameIdentifier` with `int.TryParse` guard (line 56-60 .cs)
- All DB queries filter by `userId` -- no cross-tenant data exposure
- Trainee shadowing query also scoped by `TraineeUserId == userId` (line 153 .cs)

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 6 .cshtml)
- `<loc>` tag helper used extensively: `MyOverview`, `TimeRangeSwitcher`, `Week`, `Month`, `Legend`, `Vacations`, `OnDuty`, `Shifts`, `Chores`, `Filters`, `Upcoming`, `All`, `Past30Days`, `Today`, `Tomorrow`, `ThisWeek`, `NothingHereYet`, `InDateRange`, `NoItemsMatch`, `ClearFilters`, `MyStatsForThisPeriod`, `ToggleCountHours`, `Details`, `Conflict`, `Aria_CloseDialog`
- JS localizer object with `.Value.Trim()` pattern (lines 587-601 .cshtml)

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` (line 15)
- **CSS**: Embedded `<style>` block (~400 lines of custom CSS for overview-topbar, timeline, drawer, stats chart, responsive)
- **Key components**: Sticky overview-topbar with time-range switcher and type filters; timeline feed with sections (today/tomorrow/thisWeek/later); stats bar chart rendered via Canvas 2D API (no Chart.js dependency); slide-in detail drawer
- **Design tokens**: Uses `var(--surface)`, `var(--border)`, `var(--primary)`, `var(--primary-contrast)`, `var(--text)`, `var(--muted)`, `var(--info)`, `var(--danger)`, `var(--accent)`, `var(--warning)`, `var(--warning-text)`, `var(--bg)`
- **Responsive**: `@media (max-width: 768px)` adjustments for topbar and drawer

### 5. Navigation Map
- **Links TO this page**: Cancel button on Settings (`/My`), Cancel button on Profile (`/My`), sidebar/nav presumably
- **Links FROM this page**: Time-range and view-mode links are self-referencing with query parameters (`?range=week&view=upcoming`, etc.)
- **Redirects**: None

### 6. Data Dependencies & Side Effects
- **DB queries** (all read-only):
  - `ShiftAssignments` JOIN `ShiftInstances` JOIN `ShiftTypes` (with optional `Users` for trainee name) -- lines 107-122
  - Shadowing shifts (if user is Trainee) -- lines 149-163
  - `TimeOffRequests` (approved only) -- lines 189-194
  - `OnDuties` (non-canceled) -- lines 217-222
  - `Chores` (non-canceled) -- lines 247-252
  - `Users.FindAsync(userId)` -- line 62
- **Services**: Only `AppDbContext` injected (line 16 .cs)
- **No SignalR, no external calls**
- **Side effects**: None (read-only page)

### 7. Forms & Submissions
- No forms, no POST handlers. Pure read-only page.
- JS `onclick` handlers on timeline items call `openDrawer()` (client-side only)

### 8. Interesting Behaviors
- **Conflict detection** (lines 271-276 .cs): Items on the same date are marked `HasConflict=true`. This is a simplistic heuristic -- two shifts on the same date are flagged even if they don't overlap in time.
- **Stats chart** rendered purely with Canvas 2D API (lines 680-810 .cshtml) -- no third-party chart library dependency. Handles DPR scaling, resize debouncing.
- **`escapeHtml` function** called in drawer code (lines 652-657) but NOT defined in this page's script block. It must come from `site.js` (confirmed via Grep). This is an implicit cross-file dependency -- if `site.js` is not loaded, the drawer will throw a ReferenceError.
- **_TimelineItem partial**: Uses `JavaScriptEncoder.Default.Encode()` for safe inline JS string generation (line 17 of `_TimelineItem.cshtml`). This is correct XSS prevention.
- **Hardcoded English strings** in .cs: "Shift --", "Training:", "Shadowing:", "Vacation", "After", "On-Duty Hakam", "On-Duty Lead", "Chore:", "1 day", "{days} days", "{hours:F1} hours" (lines 128-267 .cs). These should use localization.
- **`TimeHelpers.Hours(new ShiftType { Start, End })`** creates a throwaway object to calculate hours (lines 126, 167). Not a bug but somewhat unusual.

### 9. Traceability
- No logging (`ILogger` not injected)
- No error handling -- if DB queries fail, unhandled exception propagates
- No audit trail (read-only page)

---

## Pages/My/Help.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/My/Help.cshtml`, `Pages/My/Help.cshtml.cs`
- **Route template**: `@page` (default: `/My/Help`)
- **Page handler methods**: `OnGet()` (line 9 .cs) -- synchronous, empty body

### 2. Access Control & Scope
- `[Authorize]` on class (line 6 .cs) -- any authenticated user
- No data access, no scope filtering needed

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 6 .cshtml)
- `<loc>` tag helper used for all FAQ questions and answers: `Help_PageTitle`, `Help_Subtitle`, `Help_Section_Calendar`, `Help_FAQ_ViewSchedule_Q`, `Help_FAQ_ViewSchedule_A`, and ~30 more keys
- All content is localized via resource keys

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` (line 12)
- **CSS**: Embedded in `@section Styles` (~130 lines). Styled FAQ accordion using native `<details>/<summary>` elements
- **Design tokens**: `var(--text)`, `var(--muted)`, `var(--border)`, `var(--surface)`, `var(--primary)`, `var(--hover)`
- **Responsive**: `@media (max-width: 768px)` adjustments
- **Structure**: 6 FAQ sections (Calendar, Requests, Account, Troubleshooting, Chores, Shortcuts) + conditional Management section + Support Contact

### 5. Navigation Map
- **Links TO this page**: Presumably from nav sidebar or footer
- **Links FROM this page**: None (purely informational)
- **Redirects**: None

### 6. Data Dependencies & Side Effects
- **None** -- purely static content page
- No DB access, no services injected

### 7. Forms & Submissions
- No forms, no POST handlers

### 8. Interesting Behaviors
- **Role-based content visibility** (line 129 .cshtml): Management FAQ section only rendered for `User.IsInRole("Manager") || User.IsInRole("Owner") || User.IsInRole("Director") || User.IsInRole("AreaAdmin")`. This uses role-based checks rather than grant-based checks, which is inconsistent with the project's grant-based authorization model. However, since this is just FAQ content visibility (not security-critical), the impact is low.
- Uses native `<details>/<summary>` HTML elements for accordion -- no JavaScript needed, good for accessibility.

### 9. Traceability
- No logging
- No error handling needed (static content)

---

## Pages/My/NotificationCenter.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/My/NotificationCenter.cshtml`, `Pages/My/NotificationCenter.cshtml.cs`
- **Route template**: `@page` (default: `/My/NotificationCenter`)
- **Page handler methods**:
  - `OnGetAsync()` (line 32 .cs)
  - `OnPostMarkAsReadAsync(int id)` (line 78 .cs)
  - `OnPostMarkAllAsReadAsync()` (line 111 .cs)
  - `OnPostDeleteAsync(int id)` (line 149 .cs)

### 2. Access Control & Scope
- `[Authorize]` on class (line 15 .cs)
- Extends `LocalizedPageModel` (line 16 .cs)
- All DB queries filter by `userId` from `ClaimTypes.NameIdentifier` with `int.TryParse` guard
- **Ownership verification on mutations**: Mark-as-read and delete both check `n.UserId == userId` (lines 90, 161 .cs) -- correct IDOR protection

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 6 .cshtml)
- `@inject ILocalizationService Localization` for date/time formatting (line 7 .cshtml)
- Keys: `NotificationCenter`, `MarkAllAsRead`, `Read`, `View`, `MarkAsRead`, `Delete`, `DeleteConfirm`, `NoNotifications`, `AllCaughtUp`
- Notification titles/messages come from DB, not localized at render time

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` (line 13)
- **CSS**: Embedded `<style>` block (~35 lines) for notification styling with `.unread`/`.read` states, type-specific border colors
- **Design tokens**: `var(--primary)`, `var(--success)`, `var(--danger)`, `var(--info)`, `var(--warning)`, `var(--border)`, `var(--surface)`, `var(--muted)`
- **Max width**: 1200px centered container

### 5. Navigation Map
- **Links TO this page**: Notification bell in nav/header presumably
- **Links FROM this page**: Deep links from notifications to `/Requests?tab=swap&highlight={id}` and `/Requests?tab=timeoff&highlight={id}` (lines 210-213 .cs)
- **Redirects**: All POST handlers redirect to self via `RedirectToPage()` (PRG pattern)

### 6. Data Dependencies & Side Effects
- **DB queries**:
  - `UserNotifications.Where(n.UserId == userId).OrderByDescending.Take(50)` (lines 46-49 .cs) -- hardcoded limit of 50
  - Individual notification fetches for mark/delete
- **Side effects**:
  - `OnPostMarkAsReadAsync`: Sets `IsRead=true`, `ReadAt=DateTime.UtcNow`, calls `SaveChangesAsync` (lines 93-96 .cs)
  - `OnPostMarkAllAsReadAsync`: Batch updates all unread notifications (lines 122-135 .cs)
  - `OnPostDeleteAsync`: Removes notification from DB (line 165 .cs) -- hard delete, not soft delete

### 7. Forms & Submissions
- **MarkAllAsRead form**: POST with `asp-page-handler="MarkAllAsRead"`, includes `@Html.AntiForgeryToken()` (line 24-29 .cshtml)
- **MarkAsRead form** (per notification): POST with hidden `id` field, anti-forgery token (lines 97-103 .cshtml)
- **Delete form** (per notification): POST with hidden `id` field, anti-forgery token, `confirm()` dialog (lines 106-113 .cshtml)

### 8. Interesting Behaviors
- **Notification limit**: Only last 50 notifications loaded (line 49 .cs). No pagination. Users with many notifications lose access to older ones.
- **Deep link generation** (lines 203-214 .cs): Only supports `SwapRequest` and `TimeOff` notification types. Other types get `null` deep link.
- **Delete confirmation** uses inline `onsubmit="return confirm()"` with localized text injected via `.Value.Trim()` (line 107 .cshtml). If the localized text contains a single quote, this could break the JS string.
- **Error property shadowing**: `public new string? Error` (line 30 .cs) shadows a base class property with `new` keyword. This could cause confusion if the base class `Error` is accessed polymorphically.

### 9. Traceability
- **Logging**: `ILogger<NotificationCenterModel>` injected (line 19 .cs)
- Logs: user load info, notification count, mark-as-read events with notification ID and user ID, delete events
- **Error handling**: try/catch blocks around all operations with localized error messages
- Error logged at Error level for authentication failures, load failures, update failures, delete failures

---

## Pages/My/Onboarding.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/My/Onboarding.cshtml`, `Pages/My/Onboarding.cshtml.cs`
- **Route template**: `@page` (default: `/My/Onboarding`)
- **Page handler methods**:
  - `OnGet()` (line 25 .cs) -- synchronous
  - `OnPostCompleteAsync()` (line 32 .cs)

### 2. Access Control & Scope
- `[Authorize]` on class (line 10 .cs) -- any authenticated user
- POST handler validates `ClaimTypes.NameIdentifier` with `TryParse` guard (lines 34-37 .cs)
- User can only mark their own onboarding as complete (scoped by userId from claims)

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 5 .cshtml)
- `<loc>` tag helper used: `Onboarding_Title`, `Onboarding_Welcome`, `Onboarding_WelcomeSubtitle`, `Onboarding_Step1_Title`, `Onboarding_Step1_Description`, `Onboarding_Feature_Calendar/Requests/Notifications/Profile` (with Desc variants), `Onboarding_Step2_*`, `Onboarding_Tip_*`, `Onboarding_Step3_*`, `Onboarding_Previous`, `Onboarding_Next`, `Onboarding_GetStarted`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **No Breadcrumb** -- unique among My/* pages
- **CSS**: Embedded `<style>` block (~35 lines) with BEM-like naming `.onboarding__card`, `.onboarding__header`, etc.
- **Design**: 3-step wizard with dot navigation, card-based layout, centered 700px max-width
- **Design tokens**: `var(--primary)`, `var(--card-bg)`, `var(--heading-color)`, `var(--text-secondary)`, `var(--border-color)`, `var(--info-bg)`, `var(--info-text)`, `var(--hover-bg)` -- note these use different token names than other pages (e.g., `--card-bg` vs `--surface`, `--text-secondary` vs `--muted`). Potential inconsistency.

### 5. Navigation Map
- **Links TO this page**: Presumably from login flow redirect for users with `HasCompletedOnboarding == false`
- **Links FROM this page**: None visible. Complete action redirects to `/` (root)
- **Redirects**: `OnPostComplete` redirects to `/` (line 48 .cs) or `/Auth/Login` on auth failure (line 37 .cs)

### 6. Data Dependencies & Side Effects
- **DB queries**: `Users.FirstOrDefaultAsync(u => u.Id == userId)` (line 40 .cs)
- **Side effects**: Sets `user.HasCompletedOnboarding = true` and saves (lines 43-44 .cs)
- Claims read: `ClaimTypes.Name` for display name, `ClaimTypes.Role` for role (lines 28-29 .cs)

### 7. Forms & Submissions
- **Complete form**: POST with `asp-page-handler="Complete"` (line 138 .cshtml). Hidden initially, shown on step 3 via JS
- **Anti-forgery**: NOT explicitly included via `@Html.AntiForgeryToken()`. However, the `<form method="post" asp-page-handler="Complete">` tag helper should auto-generate it. Worth verifying.

### 8. Interesting Behaviors
- **Wizard navigation is purely client-side JS** (lines 147-168 .cshtml): Steps are shown/hidden via CSS class toggling. No server round-trips between steps.
- **No validation on OnGet**: `UserDisplayName` reads from claims without sanitization, but it is rendered via Razor which auto-HTML-encodes.
- **ViewData["Title"] set twice**: Once in .cshtml (line 8) as `Localizer["Onboarding_Title"]` and again in OnGet (line 27 .cs) as hardcoded `"Welcome"`. The .cs value wins at runtime, so the localized title is overridden. This is a bug -- the page title will always show "Welcome" in English.
- **No duplicate-completion guard**: If user POSTs Complete multiple times, it sets `HasCompletedOnboarding = true` repeatedly (idempotent, no harm).

### 9. Traceability
- **Logging**: `ILogger<OnboardingModel>` injected (line 14 .cs)
- Logs: onboarding completion with userId (line 45 .cs)
- No error handling around DB operations -- unhandled exception possible

---

## Pages/My/Settings.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/My/Settings.cshtml`, `Pages/My/Settings.cshtml.cs`
- **Route template**: `@page` (default: `/My/Settings`)
- **Page handler methods**:
  - `OnGetAsync()` (line 95 .cs)
  - `OnPostAsync()` (line 192 .cs)

### 2. Access Control & Scope
- `[Authorize]` on class (line 19 .cs) -- any authenticated user
- User ID from `ClaimTypes.NameIdentifier` via `GetCurrentUserId()` helper with `int.TryParse` (lines 89-93 .cs)
- Tenant scoping via `_tenantResolver.GetCurrentTenantId()` (line 98, 200 .cs)
- Preferences queried with `userId + companyId + IsActive` filter (line 101-102 .cs)

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 6 .cshtml)
- `<loc>` tag helper used extensively for section titles, labels, descriptions, help text
- Keys include: `My_Settings`, `My_Settings_Subtitle`, `Profile_Rank`, `Profile_Rank_InfoTitle/InfoDesc/Help`, `Settings_DailyDigest`, `Settings_ReceiveDailyDigest`, `Settings_PreferredTime`, `Settings_IncludeContent`, `Settings_IncludeShifts/Requests/Chores/OnDuty` (with Desc variants), `Settings_DayBeforeReminders`, `Settings_RemindBefore*`, `Settings_OnDutySubscriptions`, `Settings_SelectOnDutyRoles`, `Settings_NoOnDutyTypes`, `SaveSettings`, `Cancel`
- Rank options use culture-aware display: `r.GetDisplayName(language)` with `he`/`en` detection (lines 149-154 .cs)

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` (line 12)
- **CSS**: Embedded in `@section Styles` (~300 lines). Card-based form layout, custom toggle switch, checkbox groups, info banners
- **Design tokens**: `var(--text)`, `var(--muted)`, `var(--surface)`, `var(--border)`, `var(--primary)`, `var(--primary-contrast)`, `var(--primary-hover)`, `var(--primary-soft)`, `var(--success)`, `var(--danger)`
- **Form sections**: Military Rank, Daily Digest, Day-Before Reminders, OnDuty Role Subscriptions

### 5. Navigation Map
- **Links TO this page**: Presumably from nav sidebar or profile page
- **Links FROM this page**: Cancel button links to `/My` (line 572 .cshtml)
- **Redirects**: On invalid user ID, redirects to `/Auth/Login` (line 197 .cs)

### 6. Data Dependencies & Side Effects
- **DB queries** (OnGet):
  - `DailyNotificationPreferences.FirstOrDefaultAsync` (line 101-102 .cs)
  - `OnDutyRoleSubscriptions.Where(userId, companyId, IsActive)` (lines 129-132 .cs)
  - `OnDutyTypeConfigs.Where(IsActive)` (lines 175-178 .cs)
  - `Users.FindAsync(userId)` for rank (line 137 .cs)
- **Side effects** (OnPost):
  - Creates or updates `DailyNotificationPreference` (lines 216-251 .cs)
  - Updates `OnDutyRoleSubscription` records: soft-deletes unselected, creates new (lines 291-321 .cs)
  - Updates `user.Rank`, `user.ProfileLastUpdated`, `user.ProfileLastUpdatedBy` (lines 266-268 .cs)
  - Single `SaveChangesAsync()` call (line 270 .cs) -- atomic
- **Services**: `ITenantResolver`, `IStringLocalizer`, `ILogger`

### 7. Forms & Submissions
- Single `<form method="post">` wrapping all settings (line 373 .cshtml)
- **Bound properties**: `ReceiveDailyDigest`, `PreferredTime`, `IncludeUpcomingShifts`, `IncludePendingRequests`, `IncludeChores`, `IncludeOnDuty`, `RemindBeforeShifts`, `RemindBeforeChores`, `RemindBeforeOnDuty`, `SelectedOnDutyRoleTypes`, `Rank`
- **Anti-forgery**: Implicitly handled by Razor Pages framework (asp-page tag helper in form)
- **Enum validation**: `Enum.IsDefined(typeof(MilitaryRank), Rank)` check (line 203 .cs)

### 8. Interesting Behaviors
- **`GetCurrentUserId()` returns 0 on failure** (line 92 .cs) rather than null or throwing. The OnPost checks for 0 and redirects to login (line 195-198 .cs), but OnGet does NOT check for 0 -- it proceeds with `userId=0`, which would query for a nonexistent preference (harmless but wasteful).
- **OnDuty role subscriptions use manual checkbox binding** (lines 548-551 .cshtml): `name="SelectedOnDutyRoleTypes"` with `value="@typeOption.TypeValue"`. This works with ASP.NET model binding for `List<int>`.
- **No anti-forgery token explicitly rendered** in the form tag, but `<form method="post">` in Razor Pages auto-generates it.

### 9. Traceability
- **Logging**: `ILogger<SettingsModel>` injected (line 26 .cs)
- Error-level logging in catch block (line 283 .cs)
- No success logging for settings save (only error case logged)

---

## Pages/My/ApiKeys.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/My/ApiKeys.cshtml`, `Pages/My/ApiKeys.cshtml.cs`
- **Route template**: `@page` (default: `/My/ApiKeys`)
- **Page handler methods**:
  - `OnGetAsync()` (line 61 .cs)
  - `OnPostRequestAsync(string name, string description, string[] scopes)` (line 95 .cs)
  - `OnPostRevokeAsync(int keyId)` (line 129 .cs)
  - `OnPostRefreshAsync(int keyId)` (line 148 .cs)
  - `OnPostApproveAsync(int requestId, string? reviewNotes, string? approvedScopes, int rateLimitPerMinute, int? expiresInDays)` (line 169 .cs)
  - `OnPostRejectAsync(int requestId, string reviewNotes)` (line 210 .cs)
  - `OnPostRevokeAdminAsync(int keyId, string? reason)` (line 235 .cs)

### 2. Access Control & Scope
- `[Authorize]` on class (line 13 .cs)
- Extends `LocalizedPageModel` (line 14 .cs)
- **Feature flag gate**: `IsEnabled(EnableApiKeyManagement)` -- redirects to `/Home/Index` if disabled (lines 63-64 .cs)
- **Grant-based admin checks**: `HasGrantAsync(userId, "AccessAdminNavigation")` for admin features, `HasGrantAsync(userId, "AdminAccess")` for owner-level features (lines 51-58 .cs)
- Admin handlers (Approve, Reject, RevokeAdmin) re-check admin grant before executing (lines 179, 215, 240 .cs) -- defense in depth
- User-level operations (Request, Revoke, Refresh) scope by userId from claims
- **Potential issue**: `OnPostRevokeAsync` calls `_apiKeyService.RevokeApiKeyAsync(keyId, userId, ...)` (line 134 .cs) where `userId` is the revoker. The service must internally verify the key belongs to this user's company -- this is delegated to the service layer.

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 7 .cshtml)
- `@inject ILocalizationService Localization` for date formatting (line 8 .cshtml)
- Extensive localization keys: `MyApiKeys`, `RequestNewApiKey`, `ApiKeyGenerated`, `ApiKeySaveWarning`, `CopyToClipboard`, `ActiveApiKeys`, `NoActiveApiKeys`, `Name`, `Scopes`, `Created`, `LastUsed`, `Actions`, `Never`, `Revoke`, `ConfirmRevokeKey`, `PendingRequests`, `NoPendingRequests`, `RequestHistory`, `NoRequestHistory`, `Approved`, `Rejected`, `Pending`, `AdminPanel`, `PendingRequestsReview`, `ApproveApiKeyRequest`, `RejectApiKeyRequest`, `RevokeApiKey`, and many more

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` (line 14)
- **No separate CSS section** -- all styles are inline
- **Design**: Tables for active keys, pending requests, request history. Admin section with card-based pending reviews. Four modal dialogs (Request, Approve, Reject, RevokeAdmin).
- **Modals**: Custom-built fixed-position overlays with JS show/hide functions
- **Max width**: 1200px centered container

### 5. Navigation Map
- **Links TO this page**: Presumably from nav sidebar or admin panel
- **Links FROM this page**: None external
- **Redirects**: All POST handlers use `RedirectToPage()` with TempData (PRG pattern). Feature flag redirect to `/Home/Index`.

### 6. Data Dependencies & Side Effects
- **Services**: `IApiKeyService`, `IGrantService`, `IFeatureFlagService`, `ILogger`
- **DB queries** (delegated to services):
  - `ListUserKeysAsync`, `ListUserRequestsAsync` (lines 78-79 .cs)
  - Admin: `ListPendingRequestsAsync`, `ListAllKeysAsync`, `ListAllRequestsAsync` (lines 87-89 .cs)
- **Side effects** (delegated to services):
  - `RequestApiKeyAsync` -- creates request (line 110 .cs)
  - `RevokeApiKeyAsync` -- revokes key (line 134 .cs)
  - `RegenerateApiKeyAsync` -- generates new key (line 153 .cs)
  - `ApproveRequestAsync` -- approves and generates key (line 189 .cs)
  - `RejectRequestAsync` -- rejects request (line 221 .cs)

### 7. Forms & Submissions
- **Request form** (in modal): POST `asp-page-handler="Request"`, fields: `name`, `description`, `scopes[]` (checkboxes). Anti-forgery token included (line 396 .cshtml).
- **Revoke form** (per key): POST `asp-page-handler="Revoke"`, hidden `keyId`. Anti-forgery token (line 117 .cshtml). Confirm dialog.
- **Refresh form** (per key): POST `asp-page-handler="Refresh"`, hidden `keyId`. Anti-forgery token (line 110 .cshtml).
- **Approve form** (in modal): POST `asp-page-handler="Approve"`, fields: `requestId`, `approvedScopes`, `rateLimitPerMinute`, `expiresInDays`, `reviewNotes`. Anti-forgery token (line 465 .cshtml).
- **Reject form** (in modal): POST `asp-page-handler="Reject"`, fields: `requestId`, `reviewNotes` (required). Anti-forgery token (line 513 .cshtml).
- **RevokeAdmin form** (in modal): POST `asp-page-handler="RevokeAdmin"`, fields: `keyId`, `reason`. Anti-forgery token (line 544 .cshtml).

### 8. Interesting Behaviors
- **Generated API key displayed temporarily via TempData** (lines 42-53 .cshtml, line 49 .cs): Key shown once after generation with a warning that it won't be shown again. Good security practice.
- **PlainTextKey visibility**: Only shown to Owner users (`Model.IsOwner`) in both the active keys table (line 82 .cshtml) and admin all-keys table (line 342 .cshtml). Non-owners see masked `sk_........`.
- **Scope selection is hardcoded** in the request modal (lines 410-442 .cshtml): `user:read`, `user:write`, `shift:read`, `time-off-request:read`, `time-off-request:write`, `notification:read`, `notification:write`, `analytics:read`. Adding new scopes requires editing the cshtml.
- **Escape key handler** closes all modals (lines 621-627 .cshtml) -- good UX, annotated as "MED-025 FIX".
- **clipboard API** used for copy-to-clipboard with fallback alert (lines 612-618 .cshtml).
- **XSS concern** (line 86 .cshtml): `onclick="copyToClipboard('@key.PlainTextKey')"` -- if `PlainTextKey` contains a single quote, this breaks. Should use `Html.Raw(JsonSerializer.Serialize(...))` or `JavaScriptEncoder`.
- **`IsAdmin` vs `IsOwner` semantics**: `IsAdmin` = has `AccessAdminNavigation` grant; `IsOwner` = has `AdminAccess` grant. The page uses `IsAdmin` to show the admin panel and `IsOwner` to show plain-text API keys.

### 9. Traceability
- **Logging**: `ILogger<ApiKeysModel>` injected (line 19 .cs) -- but not actually used in any handler. All logging delegated to service layer.
- No direct error logging in the page model
- Error messages surfaced via TempData

---

## Pages/My/Profile.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/My/Profile.cshtml`, `Pages/My/Profile.cshtml.cs`
- **Route template**: `@page` (default: `/My/Profile`)
- **Page handler methods**:
  - `OnGetAsync()` (line 91 .cs)
  - `OnPostAsync()` (line 114 .cs)
  - `OnPostDeleteAvatarAsync()` (line 266 .cs)

### 2. Access Control & Scope
- `[Authorize]` on class (line 16 .cs)
- Extends `LocalizedPageModel` (line 17 .cs)
- User ID from `ClaimTypes.NameIdentifier` with `int.TryParse` (lines 93-98 .cs)
- **Grant-based check**: `HasGrantAsync(userId, "AdminAccess")` determines `CanEditProfessionalInfo` (line 107 .cs) and `isOwner` for HireDate editing (line 213 .cs)
- All operations scoped to own user profile (by userId from claims)

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 6 .cshtml)
- `<loc>` tag helper: `MyProfile`, `ManageYourPersonalAndProfessionalInformation`, `Avatar`, `UploadNewAvatar`, `MaxFileSizeAnd`, `DeleteAvatar`, `PersonalInformation`, `DisplayName`, `PreferredName`, `HowYouPreferToBeAddressed`, `Phone`, `City`, `DateOfBirth`, `ProfessionalInformation`, `ManagerOnlyFields`, `Molecule`, `JobTitle`, `HireDate`, `Skills`, `SkillsPlaceholder`, `Certifications`, `CertificationsPlaceholder`, `CommaSeparated`, `EmergencyContact`, `EmergencyContactName/Phone/Relation`, `SaveChanges`, `Cancel`
- JS uses `@Localizer["FileSelected"].Value.Trim()` and `@Localizer["Error_PleaseUploadValidImage"].Value.Trim()` in script section

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` (line 12)
- **CSS**: Embedded in `@section Styles` (~340 lines). Sections for avatar upload, form inputs, info banner, action buttons
- **Design**: Card-based sections (Avatar, Personal Information, Professional Information, Emergency Contact)
- **Wrapper**: `<div data-ui-version="v2">` (line 17 .cshtml)
- **Avatar**: Circular display with either image or initials fallback
- **Responsive**: `@media (max-width: 768px)` and `@media (max-width: 480px)`

### 5. Navigation Map
- **Links TO this page**: Presumably from nav sidebar, profile dropdown
- **Links FROM this page**: Cancel button links to `/My` (line 548 .cshtml)
- **Redirects**: On auth failure, redirects to `/Auth/Login` (lines 97, 103, 120, 126 .cs)

### 6. Data Dependencies & Side Effects
- **Services**: `IProfileService`, `IAvatarService`, `ITenantResolver`, `IGrantService`
- **DB queries**:
  - `Users.FindAsync(userId)` (line 99 .cs)
  - `Companies.IgnoreQueryFilters().Include(c => c.Molecule)` for molecule name (lines 305-308 .cs) -- security comment present
  - `JobTypes.IgnoreQueryFilters()` for computed job title (lines 316-318 .cs)
- **Side effects** (OnPost):
  - Avatar upload via `_avatarService.UploadAvatarAsync` (line 203 .cs)
  - Profile update via `_profileService.UpdateProfileAsync` (line 246 .cs)
  - Avatar delete via `_avatarService.DeleteAvatarAsync` (line 274 .cs)
- **Form encoding**: `enctype="multipart/form-data"` for file upload (line 393 .cshtml)

### 7. Forms & Submissions
- Main form: `<form method="post" enctype="multipart/form-data">` (line 393 .cshtml)
- **Bound properties**: `DisplayName`, `PreferredName`, `Phone`, `City`, `DateOfBirth`, `Skills`, `Certifications`, `EmergencyContactName/Phone/Relation`, `AvatarFile`, `HireDate`
- **Delete avatar**: `asp-page-handler="DeleteAvatar"` button within the main form (line 421 .cshtml)
- Anti-forgery: Implicit via Razor Pages

### 8. Interesting Behaviors
- **Extensive input validation** (lines 130-198 .cs): Length checks for all text fields (DisplayName max 200, PreferredName max 100, Phone max 50, City max 100, Skills/Certifications max 5000, emergency contact fields various limits). Good defensive coding.
- **`IgnoreQueryFilters()`** used for Company and JobType queries (lines 305, 317 .cs) with security audit comments. This is necessary because the profile page shows cross-hierarchy data (molecule name, job type) for display purposes only.
- **JSON or comma-separated parsing** for Skills and Certifications (lines 354-377 .cs): Dual-format input handling (JSON array or comma-separated string). Robust but complex.
- **HireDate protection**: Only users with `AdminAccess` grant can modify HireDate (line 243 .cs). Other users see a read-only field (line 504 .cshtml).
- **Future HireDate validation**: Rejects HireDate in the future (line 216-220 .cs).
- **Drag-and-drop avatar upload** enhancement in JS (lines 556-639 .cshtml): Client-side only, validates file type (JPEG/PNG), provides visual feedback. The actual upload happens on form submit, not via AJAX.
- **`_localizer` used in .cs** (inherited from `LocalizedPageModel`) -- consistent usage.

### 9. Traceability
- **No explicit logging** (`ILogger` not injected)
- Error handling via ErrorMessage/SuccessMessage properties displayed on page
- No audit trail for profile changes (though `ProfileLastUpdated` and `ProfileLastUpdatedBy` are set in Settings page, not here -- the `.cs` delegates to `_profileService.UpdateProfileAsync` which may handle it)

---

## Pages/My/Requests.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/My/Requests.cshtml`, `Pages/My/Requests.cshtml.cs`
- **Route template**: `@page` (default: `/My/Requests`)
- **Page handler methods**:
  - `OnGetAsync()` (line 55 .cs)
  - `OnPostTimeOffAsync()` (line 194 .cs)
  - `OnPostSwapAsync()` (line 299 .cs)
  - `OnPostCancelRequestAsync(int requestId)` (line 382 .cs)

### 2. Access Control & Scope
- `[Authorize]` on class (line 18 .cs)
- Extends `LocalizedPageModel` (line 19 .cs)
- User ID from `ClaimTypes.NameIdentifier` with `int.TryParse` (lines 60-67 .cs in OnGet, similar in all POST handlers)
- All queries scoped to own user's data
- **Ownership verification**: Swap request validates `sa.UserId == userId` (line 334 .cs); cancel request validates `r.UserId == userId` (line 397 .cs)
- **Approver validation** (lines 235-258 .cs): When a specific approver is selected, verifies the approver holds `ApproveVacations` or `ApproveExtendedLeave` grant scoped to the requesting user's company/jobtype. This is thorough grant-based validation.

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 6 .cshtml)
- `@inject ILocalizationService Localization` for date/time formatting (line 7 .cshtml)
- Keys: `MyRequests`, `ManageYourTimeOffAndShiftSwaps`, `NewRequest`, `RequestTimeOff`, `VacationType`, `RegularVacation`, `AfterDutyVacation`, `StartDate`, `EndDate`, `SpecificApprover`, `Optional`, `AnyManager`, `Reason`, `TimeOffReasonPlaceholder`, `SubmitTimeOffRequest`, `RequestShiftSwap`, `SelectYourShiftToSwap`, `ChooseAShift`, `SwapRequestDescription`, `NoUpcomingShiftsForSwapping`, `RequestHistory`, `MyTimeOffRequests`, `MySwapRequests`, `NoTimeOffRequestsYet`, `NoSwapRequestsYet`, `ConfirmCancelRequest`, `CancelRequest`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` (line 13)
- **CSS**: Embedded in `@section Styles` (~380 lines). Grid-based forms section, request history list, status badges, validation styles
- **Wrapper**: `<div data-ui-version="v2">` (line 18 .cshtml)
- **Design**: Two-column grid for forms (Time Off + Shift Swap), two-column grid for history (Time Off Requests + Swap Requests)
- **Status badges**: `.status-pending`, `.status-approved`, `.status-declined`, `.status-canceled`
- **Responsive**: `@media (max-width: 768px)` collapses grids to single column

### 5. Navigation Map
- **Links TO this page**: Deep links from NotificationCenter (`/Requests?tab=...`), nav sidebar presumably
- **Links FROM this page**: None external
- **Redirects**: POST handlers redirect to self via `RedirectToPage()` (PRG pattern)

### 6. Data Dependencies & Side Effects
- **Services**: `IVacationApprovalService`, `IFeatureFlagService`, `IGrantService`
- **DB queries** (OnGet):
  - `TimeOffRequests.Where(r.UserId == userId)` (lines 72-84 .cs)
  - `ShiftAssignments.Where(sa.UserId == userId)` then `SwapRequests.Where(sr.FromAssignmentId in ids)` (lines 89-108 .cs)
  - `ShiftAssignments` JOIN `ShiftInstances` JOIN `ShiftTypes` for available shifts (lines 113-132 .cs)
  - Complex grant-based approver query (lines 147-170 .cs): Queries `Grants` table with hierarchical scope matching (Company -> Molecule -> Area -> Project) to find users with `ApproveVacations` or `ApproveExtendedLeave` grants
- **Side effects**:
  - `OnPostTimeOffAsync`: Creates `TimeOffRequest`, optionally submits for approval via `_vacationApprovalService.SubmitForApprovalAsync` (line 282 .cs)
  - `OnPostSwapAsync`: Creates `SwapRequest` (lines 356-367 .cs)
  - `OnPostCancelRequestAsync`: Cancels via `_vacationApprovalService.CancelRequestAsync` (line 413 .cs)

### 7. Forms & Submissions
- **Time Off form**: POST `asp-page-handler="TimeOff"`, anti-forgery token, validation summary. Fields: `TimeOffRequest.Type` (select), `TimeOffRequest.StartDate/EndDate` (date inputs), `TimeOffRequest.ApproverId` (select), `TimeOffRequest.Reason` (textarea)
- **Swap form**: POST `asp-page-handler="Swap"`, anti-forgery token, validation summary. Fields: `SwapRequest.ShiftId` (select)
- **Cancel form** (per pending request): POST `asp-page-handler="CancelRequest"`, hidden `requestId`, confirm dialog
- **JS behavior** (lines 653-672 .cshtml): Toggles EndDate visibility based on vacation type (hidden for AfterDutyVacation)

### 8. Interesting Behaviors
- **Swap request data is incomplete** (lines 96-108 .cs): `ShiftDate`, `ShiftTypeName`, and `ToUserName` are hardcoded to `DateTime.Today`, `"Swap Request"`, and `"Target User"` respectively, with TODO comments "Will fix with proper join later". This is a known incomplete feature -- swap history displays incorrect data.
- **Cross-form validation isolation** (lines 201, 306 .cs): `ModelState.ClearValidationState(nameof(SwapRequest))` in TimeOff handler and vice versa. This is necessary because both forms are on the same page and both models are bound.
- **Approver scope matching** (lines 147-170 .cs): The query performs hierarchical scope matching against the grant system. It correctly handles Company-scoped, Molecule-scoped, Area-scoped, Project-scoped, and self-scoped (all-null) grants. For self-scoped grants, it checks that the approver is in the same company as the requester (line 167 .cs).
- **Feature flag check for approval workflow** (line 280 .cs): `FeatureFlagSeed.Flags.VacationApprovalEnabled` -- if disabled, the time-off request is still created but not submitted for approval.
- **After-duty vacation single-day enforcement** (lines 204-206 .cs): EndDate is forcibly set equal to StartDate for AfterDutyVacation type.

### 9. Traceability
- **Logging**: `ILogger<RequestsModel>` injected (line 22 .cs)
- **Extensive logging**: Every step of OnGetAsync logged with Information level (start, user ID, counts of loaded data, completion). Error-level for failures.
- POST handlers log submission start, user/shift IDs, success with IDs, approval results
- Cancel handler logs at Warning level for not-found and already-processed cases
- **Error handling**: try/catch blocks around all handlers with localized error messages

---

## Pages/My/Profile.cshtml(.cs)

(Already audited above -- this entry was already covered)

---

## Pages/Director/ViewAsMode.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/Director/ViewAsMode.cshtml`, `Pages/Director/ViewAsMode.cshtml.cs`
- **Route template**: `@page` (default: `/Director/ViewAsMode`)
- **Page handler methods**:
  - `OnGetAsync()` (line 36 .cs)
  - `OnPostEnterAsync(int companyId)` (line 51 .cs)
  - `OnPostExitAsync()` (line 67 .cs)

### 2. Access Control & Scope
- `[Authorize(Policy = "Grant:DirectorHubAccess")]` (line 13 .cs) -- grant-based policy, not just any auth user
- Extends `LocalizedPageModel` (line 14 .cs)
- Company list scoped via `_directorService.GetDirectorCompanyIdsAsync()` (line 38 .cs)
- ViewAs mode entry delegated to `_viewAsModeService.EnterViewAsModeAsync(companyId)` -- the service must validate that the companyId is in the director's assigned companies

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 5 .cshtml)
- `<loc>` tag helper: `ViewAsManager`, `ViewAsModeDescription`, `CurrentlyInViewAsManagerMode`, `YouAreViewingAsManagerOf`, `ViewScopedToCompany`, `ExitViewAsManagerMode`, `SelectCompanyToView`, `ChooseCompanyViewAsManager`, `OnlyThisCompanyDataVisible`, `PermissionsMatchManagerLevel`, `SeeWhatManagerSees`, `AllOtherCompaniesHidden`, `NotAssignedToAnyCompanies`, `Important`, `ViewAsModeInfo`
- TempData messages use `_localizer[...].Value` pattern (lines 57, 62, 70 .cs)

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` with parent link to `/Director` (lines 12-16 .cshtml)
- **No separate CSS section** -- inline styles throughout
- **Design**: Conditional rendering -- if currently viewing, shows warning card with exit button; otherwise shows company list with enter buttons
- **Design tokens**: `var(--muted)`, `var(--success)`, `var(--success-text)`, `var(--danger)`, `var(--danger-text)`, `var(--warning)`, `var(--surface)`, `var(--primary)`

### 5. Navigation Map
- **Links TO this page**: Director Index page's "View As Manager" tool card (`/Director/ViewAsMode`) (line 165, Director/Index.cshtml)
- **Links FROM this page**: Enter mode redirects to `/Calendar/Month` (line 58 .cs). Exit redirects to self.
- **Redirects**: Enter success -> `/Calendar/Month`; Enter failure -> self; Exit -> self

### 6. Data Dependencies & Side Effects
- **Services**: `IDirectorService`, `IViewAsModeService`
- **DB queries**:
  - `_directorService.GetDirectorCompanyIdsAsync()` (line 38 .cs)
  - `Companies.Where(companyIds.Contains(c.Id))` (lines 39-42 .cs)
  - `_viewAsModeService.IsViewingAsManager()` (line 44 .cs)
  - `_viewAsModeService.GetViewAsCompanyNameAsync()` (line 47 .cs)
- **Side effects**:
  - `EnterViewAsModeAsync(companyId)` -- sets session state for manager impersonation (line 53 .cs)
  - `ExitViewAsModeAsync()` -- clears session state (line 69 .cs)

### 7. Forms & Submissions
- **Enter form** (per company): POST `asp-page-handler="Enter"`, hidden `companyId`, anti-forgery token (lines 80-85 .cshtml)
- **Exit form**: POST `asp-page-handler="Exit"`, anti-forgery token (lines 45-48 .cshtml)

### 8. Interesting Behaviors
- **ViewAs mode is a session-level impersonation feature**: When a Director enters ViewAs mode, they see the system as a Manager of the selected company. This has broad security implications -- the `_viewAsModeService` must correctly scope all subsequent data access.
- **No confirmation dialog** for entering ViewAs mode. However, the action is non-destructive (just changes session state).
- **Border-inline-start** used for RTL-compatible info banner (line 100 .cshtml) -- good i18n practice.
- **Company slug displayed** in `<code>` tag (line 77 .cshtml).

### 9. Traceability
- **No logging** (`ILogger` not injected)
- Success/error messages via TempData
- No audit trail for ViewAs mode enter/exit events (should consider logging these for security audit)

---

## Pages/Director/CompanyFilter.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/Director/CompanyFilter.cshtml`, `Pages/Director/CompanyFilter.cshtml.cs`
- **Route template**: `@page` (default: `/Director/CompanyFilter`)
- **Page handler methods**:
  - `OnGetAsync()` (line 37 .cs)
  - `OnPostSetFilterAsync()` (line 48 .cs)
  - `OnPostClearFilterAsync()` (line 55 .cs)

### 2. Access Control & Scope
- `[Authorize(Policy = "Grant:DirectorHubAccess")]` (line 13 .cs) -- grant-based policy
- Extends `LocalizedPageModel` (line 14 .cs)
- Company list scoped via `_directorService.GetDirectorCompanyIdsAsync()` (line 39 .cs)
- Filter operations delegated to `ICompanyFilterService` -- persisted in browser (per info text on page)

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 5 .cshtml)
- `<loc>` tag helper: `CompanyFilter`, `CompanyFilterDescription`, `YourAssignedCompanies`, `ApplyFilter`, `ShowAllCompanies`, `Note`, `CompanyFilterAppliesTo`, `CalendarViewsOnlySelected`, `RequestListsOnlySelected`, `DashboardsAndReports`, `FilterPersistsInBrowser`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` with parent link to `/Director` (lines 12-16 .cshtml)
- **No separate CSS section** -- inline styles
- **Design**: Checkbox list of assigned companies inside a card, with Apply and Clear buttons
- **Info note** at bottom with `border-inline-start` (RTL-aware)

### 5. Navigation Map
- **Links TO this page**: Director Index page's "Manage Company Filter" button (`/Director/CompanyFilter`) (line 56, Director/Index.cshtml)
- **Links FROM this page**: None external
- **Redirects**: Both POST handlers redirect to self via `RedirectToPage()` (PRG pattern)

### 6. Data Dependencies & Side Effects
- **Services**: `IDirectorService`, `ICompanyFilterService`
- **DB queries**:
  - `_directorService.GetDirectorCompanyIdsAsync()` (line 39 .cs)
  - `Companies.Where(companyIds.Contains(c.Id))` (lines 40-43 .cs)
  - `_filterService.GetSelectedCompanyIdsAsync()` (line 45 .cs)
- **Side effects**:
  - `SetSelectedCompanyIdsAsync(CompanyIds)` -- persists filter selection (line 50 .cs)
  - `ClearFilterAsync()` -- clears filter (line 57 .cs)

### 7. Forms & Submissions
- **SetFilter form**: POST `asp-page-handler="SetFilter"`, anti-forgery token, checkbox inputs `name="CompanyIds"` with company ID values (lines 33-63 .cshtml)
- **ClearFilter**: Submit button with `formaction="?handler=ClearFilter"` within the same form (line 61 .cshtml)
- **`[BindProperty] public List<int> CompanyIds`** (line 35 .cs)

### 8. Interesting Behaviors
- **No server-side validation** that selected CompanyIds are within the director's assigned companies. A malicious user could POST arbitrary company IDs. The `ICompanyFilterService` should validate against the director's assignments. This is a potential security concern -- depends on the service implementation.
- **`formaction` attribute** on the Clear button (line 61 .cshtml): This overrides the form's default handler. It also uses a `btn-ghost` class not defined in the page's styles.
- **`DisplayName` vs `Name`**: The page shows `company.DisplayName` if it differs from `company.Name` (line 50 .cshtml). Good UX.

### 9. Traceability
- **No logging** (`ILogger` not injected)
- Success messages via TempData
- No audit trail for filter changes

---

## Pages/Director/NotificationHub.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/Director/NotificationHub.cshtml`, `Pages/Director/NotificationHub.cshtml.cs`
- **Route template**: `@page` (default: `/Director/NotificationHub`)
- **Page handler methods**: `OnGetAsync()` (line 46 .cs)

### 2. Access Control & Scope
- `[Authorize(Policy = "Grant:DirectorHubAccess")]` (line 12 .cs) -- grant-based policy
- **Security audit comment**: "SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE -- requires Grant:DirectorHubAccess policy; cross-company queries by design" (lines 10-11 .cs)
- Company IDs scoped via `_directorService.GetDirectorCompanyIdsAsync()` (line 48 .cs)
- All queries explicitly filter by `companyIds.Contains(...)` (lines 57, 77, 98 .cs)

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 5 .cshtml)
- `<loc>` tag helper: `NotificationHub`, `NotificationHubDescription`, `PendingTimeOffRequests`, `Company`, `User`, `Details`, `Requested`, `NoPendingTimeOffRequests`, `PendingSwapRequests`, `NoPendingSwapRequests`, `RecentNotificationsLast50`, `Type`, `Message`, `Date`, `Status`, `Read`, `Unread`, `NoNotificationsYet`, `Note`, `NotificationHubInfo`

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` with parent link to `/Director` (lines 12-16 .cshtml)
- **No separate CSS section** -- inline styles
- **Design**: Two-column grid (Pending Time Off + Pending Swap Requests), full-width Recent Notifications table
- **Info note** at bottom with `border-inline-start`
- **Read/unread styling**: Inline `style="opacity: 0.6"` for read, `font-weight: 600` for unread (line 114 .cshtml)

### 5. Navigation Map
- **Links TO this page**: Director Index page's "Notification Hub" tool card (`/Director/NotificationHub`) (line 174, Director/Index.cshtml)
- **Links FROM this page**: None
- **Redirects**: None

### 6. Data Dependencies & Side Effects
- **DB queries** (all read-only, all using `IgnoreQueryFilters()`):
  - `UserNotifications.IgnoreQueryFilters()` JOIN `Companies.IgnoreQueryFilters()` -- top 50 notifications across assigned companies (lines 55-69 .cs)
  - `TimeOffRequests.IgnoreQueryFilters()` JOIN `Users.IgnoreQueryFilters()` JOIN `Companies.IgnoreQueryFilters()` -- pending time-off requests (lines 74-88 .cs)
  - `SwapRequests.IgnoreQueryFilters()` with joins to `ShiftAssignments`, `Users` (from/to), `Companies` -- pending swap requests (lines 93-109 .cs)
- **No side effects** (read-only page)

### 7. Forms & Submissions
- No forms, no POST handlers. Pure read-only page.

### 8. Interesting Behaviors
- **All queries use `IgnoreQueryFilters()`** -- this is by design for cross-company director view. Each query has an explicit security audit comment explaining the justification.
- **Swap request query assumes `ToUserId` is not null** (line 96 .cs): The INNER JOIN `on s.ToUserId equals toUser.Id` will exclude open swap requests (where `ToUserId` is null). This means open swap requests are silently hidden from the director's notification hub.
- **Date formatting** uses hardcoded `ToString("yyyy-MM-dd HH:mm")` (lines 48, 84 .cshtml) rather than the `ILocalizationService`. Inconsistent with other pages.
- **No pagination** on any of the tables. Time-off and swap requests load ALL pending requests. For organizations with many companies, this could be a performance concern.
- **Notification limit**: Only last 50 notifications via `.Take(50)` (line 68 .cs).

### 9. Traceability
- **No logging** (`ILogger` not injected)
- No error handling -- unhandled exceptions propagate
- No audit trail (read-only page)

---

## Pages/Director/Index.cshtml(.cs)

### 1. Identity & Routing
- **Files**: `Pages/Director/Index.cshtml`, `Pages/Director/Index.cshtml.cs`
- **Route template**: `@page` (default: `/Director` or `/Director/Index`)
- **Page handler methods**: `OnGetAsync()` (line 43 .cs)

### 2. Access Control & Scope
- `[Authorize(Policy = "Grant:DirectorHubAccess")]` (line 17 .cs) -- grant-based policy
- **Security audit comment**: "SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE -- requires Grant:DirectorHubAccess policy; cross-company queries by design" (lines 15-16 .cs)
- Company IDs scoped via `_directorService.GetDirectorCompanyIdsAsync()` (line 46 .cs)
- Stats queries filter by `targetCompanyIds.Contains(...)` (lines 65-70 .cs)

### 3. Localization
- `@inject IStringLocalizer<SharedResources> Localizer` (line 6 .cshtml)
- `<loc>` tag helper with fallback text: `DirectorHub_Title` ("Director Hub"), `DirectorHub_Subtitle` ("Cross-Company Management Center"), `Director_AssignedCompanies`, `Director_FilterActive`, `Active`, `Director_ManageCompanyFilter`, `Director_NoCompaniesAssigned`, `Director_ContactOwnerForCompanies`, `Stat_ActiveUsers`, `Stat_TotalShifts`, `Stat_PendingRequests`, `Section_QuickActions`, `Section_Calendar`, `Section_ManageUsers`, `Section_Requests`, `Section_ShiftManagement`, `Section_Analytics`, `Section_AdminHub`, `Section_DirectorUtilities`, `Section_ViewAsManager`, `Section_NotificationHub` (and their Desc variants)

### 4. UI & Design Inventory
- **Layout**: `_Layout`
- **Breadcrumb**: ViewComponent `Breadcrumb` (line 13)
- **CSS**: Embedded `<style>` block (~85 lines) at bottom. Company list grid, badge styles, empty state, section titles
- **Wrapper class**: `owner-panel-container` (line 18 .cshtml) -- reuses owner panel styling
- **Design**: Company selection grid, stats cards (3 metrics), quick actions grid (6 tool cards), director utilities grid (2 tool cards)
- **Design tokens**: `var(--border)`, `var(--card-bg)`, `var(--primary)`, `var(--success)`, `var(--success-soft)`, `var(--info-soft)`, `var(--info)`, `var(--text-muted)`

### 5. Navigation Map
- **Links TO this page**: Nav sidebar presumably, other Director pages' breadcrumbs point to `/Director`
- **Links FROM this page**:
  - `/Director/CompanyFilter` (Manage Company Filter button, line 56)
  - `/Calendar/Month` (Calendar tool card, line 104)
  - `/Admin/Users` (Manage Users tool card, line 113)
  - `/Requests/Index` (Requests tool card, line 122)
  - `/Calendar/Table` (Shift Management tool card, line 131)
  - `/Admin/Analytics` (Analytics tool card, line 140)
  - `/Admin/Index` (Admin Hub tool card, line 149)
  - `/Director/ViewAsMode` (View As Manager tool card, line 165)
  - `/Director/NotificationHub` (Notification Hub tool card, line 174)

### 6. Data Dependencies & Side Effects
- **Services**: `IDirectorService`, `ICompanyFilterService` (optional injection via `= null`, line 27 .cs)
- **DB queries** (all using `IgnoreQueryFilters()`, all read-only):
  - `Companies.Where(companyIds.Contains(c.Id))` (lines 47-50 .cs)
  - `Users.IgnoreQueryFilters().CountAsync` for active users (line 65 .cs)
  - `ShiftInstances.IgnoreQueryFilters().CountAsync` for total shifts (lines 67-68 .cs)
  - `TimeOffRequests.IgnoreQueryFilters().CountAsync` for pending requests (lines 69-70 .cs)
- **No side effects** (read-only page)

### 7. Forms & Submissions
- No forms, no POST handlers. Pure dashboard/navigation page.

### 8. Interesting Behaviors
- **`ICompanyFilterService` is optional** (line 27 .cs): `ICompanyFilterService? filterService = null`. The constructor uses a default parameter. This suggests the service might not always be registered -- if not, the filter functionality is silently disabled (lines 53-56 .cs check for null before using it).
- **Stats error suppression** (lines 71-77 .cs): A bare `catch` block swallows all exceptions and sets counts to 0. This means database errors during stat calculation are silently ignored. No logging.
- **`TotalShifts` counts ALL shift instances** (line 68 .cs) -- not just upcoming ones. For organizations with years of history, this number could be very large and not very meaningful.
- **Admin Hub link** (line 149 .cshtml): Links to `/Admin/Index` which may require separate grants. A director without admin grants would see a 403. The page should ideally check grants before showing admin links.
- **`owner-panel-container` CSS class** (line 18 .cshtml): This class name suggests the styling was copied from the Owner panel. Reuse is fine but naming is misleading for a Director page.

### 9. Traceability
- **No logging** (`ILogger` not injected)
- **Silent error suppression** in stats calculation (bare `catch` with no logging, line 72 .cs) -- bugs here would be very hard to diagnose
- No audit trail (read-only page)

---

## Summary of Cross-Cutting Findings

### Security
1. **All My/* pages use `[Authorize]`** -- any authenticated user can access. All Director/* pages use `[Authorize(Policy = "Grant:DirectorHubAccess")]` -- grant-based access.
2. **All user ID parsing uses `int.TryParse`** consistently -- no crash risk from malformed claims.
3. **IDOR protection** is correct in NotificationCenter (userId checked on mutations) and Requests (ownership verified before swap/cancel).
4. **CompanyFilter** does not server-side validate that submitted CompanyIds belong to the director's assigned companies -- relies on service layer.
5. **ViewAs mode** security depends entirely on `IViewAsModeService` implementation -- no validation visible at page level.
6. **Director pages use `IgnoreQueryFilters()`** with explicit security audit comments -- correct for cross-company queries.

### Localization
7. **My/Index has hardcoded English strings** in the .cs model (shift labels, time-off labels, chore labels). These should be localized.
8. **Onboarding ViewData["Title"] bug**: Set to localized value in .cshtml but overridden to hardcoded `"Welcome"` in .cs.
9. **NotificationHub uses hardcoded date formatting** (`yyyy-MM-dd HH:mm`) instead of `ILocalizationService`.
10. **Help page uses role-based checks** (`User.IsInRole`) instead of grant-based checks for FAQ section visibility.

### Logging
11. **NotificationCenter and Requests** have thorough logging.
12. **My/Index, Help, Onboarding, Profile** have minimal or no logging.
13. **All Director pages** lack logging entirely.
14. **Director/Index silently swallows stats errors** with a bare `catch` and no logging.

### Data
15. **NotificationHub swap query** excludes open swap requests (null `ToUserId`) due to INNER JOIN.
16. **Requests swap history** shows placeholder data instead of real swap details (known TODO).
17. **NotificationCenter** has a hard limit of 50 notifications with no pagination.

### CSS
18. **Onboarding page uses different design tokens** (`--card-bg`, `--text-secondary`, `--heading-color`) than the rest of the app. Should be normalized.
19. **ApiKeys page XSS risk**: `copyToClipboard('@key.PlainTextKey')` could break if the key contains single quotes. Should use proper JS encoding.
