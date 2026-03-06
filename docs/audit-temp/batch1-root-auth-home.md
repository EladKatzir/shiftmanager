# Batch 1 Audit: Root, Auth, Home Pages

Audited: 2026-03-03
Model: claude-opus-4-6 (Strongest model -- final validation audit)

---

## 1. Pages/Index.cshtml + Pages/Index.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/` (root)
- **Purpose:** Main dashboard page showing stat cards, next-shift highlight, announcements feed, and quick-action links. Serves as the landing page for non-Owner authenticated users.
- **Redirects inbound:** Login redirects non-Owner users here (`return Redirect("/")`) after successful authentication.
- **Redirects outbound:** None (no redirects from code-behind). Quick-action links navigate to `/Calendar/Month`, `/Requests/Index`, `/Admin/Users`, `/MyTeam/Index`, `/Admin/Analytics`, `/My/Requests`, `/My/Profile`.
- **Query params:** None.

### 2) Access Control & Scope
- **Auth:** `[Authorize]` attribute on `IndexModel`. Requires authenticated user.
- **Grant checks:** Uses `_grantService.HasGrantAsync(userId, "AccessAdminNavigation")` to set `IsAdmin` bool for UI differentiation (admin vs employee views). This is display-only, not security enforcement.
- **Tenant scope:** Queries are scoped by `user.CompanyId` (admin shift counts, pending requests, team members). Employee queries scoped by `userId`. No `IgnoreQueryFilters()` used -- relies on EF tenant query filters.
- **Potential gaps:** The `UserRole` property is set from `user.Role` and used for conditional navigation (Manager -> `/MyTeam/Index`, others -> `/Admin/Users`). This is role-based display logic but not authorization -- the target pages enforce their own access. The `TeamMembersCount` counts ALL users in the company regardless of IsAdmin -- this is correct for admin view but also returned for employee view (same metric, different label).

### 3) Localization
- **Patterns:** Uses both `<loc key="..." />` tag helper and `@Localizer["..."]` (IStringLocalizer<SharedResources>). Also uses `ILocalizationService` for date formatting (`Localization.FormatShortMonthDay`).
- **Key patterns:** `Dashboard_*` prefix for all dashboard-specific keys (e.g., `Dashboard_UpcomingShifts`, `Dashboard_PendingRequests`). Admin vs employee differentiated by key suffix (`Dashboard_PendingRequests` vs `Dashboard_MyPendingRequests`).
- **RTL/LTR:** No explicit RTL handling in this page -- relies on `_Layout` for direction. Date formatting via `NextShiftDate.Value.ToString("dddd, MMMM dd")` uses current culture automatically.

### 4) UI & Design Inventory
- **Layout:** `_Layout`. Content wrapped in `<div class="app-content" data-ui-version="v2">`.
- **Interactive elements:**
  - 4 stat cards (UpcomingShifts, PendingRequests, TeamMembers, Notifications) -- read-only displays
  - Next Shift highlight (employee only) -- read-only
  - Announcements feed (up to 5, pinned items marked) -- read-only
  - 4 quick-action cards (links) -- admin set: Calendar, Requests, Team, Analytics; employee set: Schedule, Submit Request, Groups, Profile
- **UI states:**
  - Admin view vs Employee view (controlled by `IsAdmin`)
  - Next shift present vs no shifts scheduled (employee only)
  - Announcements present vs empty state ("No announcements" with megaphone icon)
  - Unauthenticated: returns silently (blank page within layout)
- **Shared components:** Lucide icons (`data-lucide` attributes), `<loc>` tag helper, `_Layout`.

### 5) Navigation Map
- **Nav targets:**
  - `/Calendar/Month` (View Calendar / View Schedule)
  - `/Requests/Index` (Manage Requests -- admin)
  - `/Admin/Users` (View Team -- non-Manager admin)
  - `/MyTeam/Index` (View Team -- Manager; View Groups -- employee)
  - `/Admin/Analytics` (View Analytics -- admin)
  - `/My/Requests` (Submit Request -- employee)
  - `/My/Profile` (My Profile -- employee)
- **How users reach this page:** Post-login redirect for non-Owner users (`return Redirect("/")`). Also reachable via sidebar/nav.
- **Back/breadcrumb:** None (this is the root page).

### 6) Data Dependencies & Side Effects
- **Data read:**
  - `Users` table (user lookup by ID from claims)
  - `ShiftAssignments` + `ShiftInstances` (upcoming shift count, next shift details) -- joins with `ShiftType` for employee next shift
  - `TimeOffRequests` (pending count)
  - `SwapRequests` (pending count)
  - `Users` (team member count by CompanyId)
  - `UserNotifications` (unread count)
  - `IAnnouncementService.GetActiveAnnouncementsAsync(userId)` -- announcements
- **Data written:** None. This page is read-only.
- **Error handling:** Silent early return if user not authenticated or user not found (no error displayed -- blank dashboard).

### 7) Forms & Submissions
- None. This page has no forms.

### 8) Interesting Behaviors
- Uses `int.TryParse` for claim parsing (safe pattern per project memory).
- The `UserRole` property is used only for the admin quick-action link target (`Manager -> /MyTeam/Index`, others -> /Admin/Users`). Comment says "For display purposes only".
- The announcements feed calls `Model.RecentAnnouncements.Take(5)` even though the service likely already limits -- defensive double-take.
- `NextShiftDate` is stored as `DateTime?` but sourced from `DateOnly.ToDateTime(TimeOnly.MinValue)` -- could cause timezone issues in display since the ToString uses server-local formatting.

### 9) Traceability
- **Services:** `AppDbContext`, `IAnnouncementService`, `IGrantService`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Index.cshtml.cs`

---

## 2. Pages/AccessDenied.cshtml + Pages/AccessDenied.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/AccessDenied`
- **Purpose:** Displays a "403 Forbidden" page and auto-redirects user after 2 seconds.
- **Redirects inbound:** ASP.NET Core authorization middleware redirects here when a user fails a policy check. The `returnUrl` query parameter or `Referer` header indicates where the user came from.
- **Redirects outbound:** Auto-redirect via JavaScript `setTimeout` after 2 seconds to `ReturnUrl` (cleaned, defaults to `/Calendar/Month`).
- **Query params:** `returnUrl` (optional) -- the URL the user was trying to access.

### 2) Access Control & Scope
- **Auth:** `[AllowAnonymous]` -- accessible without authentication.
- **Tenant scope:** None -- standalone error page.
- **Potential gaps:** The `ReturnUrl` is cleaned (query params stripped) and checked against a protected paths list. However, the XSS mitigation uses `Json.Serialize` for the JS redirect URL, which is correct. The `ReturnUrl` is set as a public property rendered directly in JavaScript -- the `Json.Serialize` call properly escapes it. The protected path check uses `string.Contains` which catches substrings (e.g., `/Admin/Foo` matches `/Admin/`).

### 3) Localization
- **Patterns:** `<loc key="..." />` tag helper. Manual Hebrew detection via `CultureInfo.CurrentUICulture.Name.StartsWith("he")`.
- **Key patterns:** `AccessDenied`, `RedirectingBack`.
- **RTL/LTR:** Explicit `dir` and `lang` attributes set on `<html>` based on Hebrew detection. Also sets `class="hebrew"` or `class="english"`.

### 4) UI & Design Inventory
- **Layout:** `Layout = null` -- standalone full HTML page, no shared layout.
- **Interactive elements:** None (auto-redirect only).
- **UI states:** Single state: forbidden emoji, "Access Denied" heading, "Redirecting back" message, loading spinner.
- **Shared components:** Uses `site.css` for CSS variable theming.

### 5) Navigation Map
- **Nav targets:** Auto-redirect to `ReturnUrl` (default: `/Calendar/Month`). Protected paths redirect to `/Calendar/Month` instead.
- **How users reach this page:** Authorization failure redirect by ASP.NET middleware.
- **Back/breadcrumb:** None.

### 6) Data Dependencies & Side Effects
- **Data read:** None.
- **Data written:** None.
- **Error handling:** Sets HTTP 403 status code. Falls back to `/Calendar/Month` for empty/protected URLs. Logs original URL and final return URL.

### 7) Forms & Submissions
- None.

### 8) Interesting Behaviors
- Phase 8.5 fix: Removed `accessDenied=true` query parameter from redirect to prevent cascade redirects.
- Protected paths include `/AccessDenied` itself (prevents infinite loops), `/Admin/`, `/Owner/`, `/Director/`, `/Assignments/`, `/Requests/Index`.
- The 2-second redirect timeout is hardcoded in JavaScript with no user control.
- The page title "Access Denied - Shift Manager" is hardcoded in English (not localized).

### 9) Traceability
- **Services:** `ILogger<AccessDeniedModel>`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\AccessDenied.cshtml.cs`

---

## 3. Pages/Error.cshtml + Pages/Error.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/Error`
- **Purpose:** Generic error page for unhandled exceptions.
- **Redirects inbound:** ASP.NET exception handler middleware redirects here.
- **Redirects outbound:** None.
- **Query params:** None used.

### 2) Access Control & Scope
- **Auth:** `[AllowAnonymous]` -- accessible without authentication.
- **Tenant scope:** None.
- **Potential gaps:** None -- minimal error page.

### 3) Localization
- **Patterns:** `<loc key="..." />` tag helper.
- **Key patterns:** `SomethingWentWrong`, `UnexpectedErrorOccurred`.
- **RTL/LTR:** Handled by `_Layout`.

### 4) UI & Design Inventory
- **Layout:** `_Layout`.
- **Interactive elements:** None.
- **UI states:** Single state: card with error heading and message.
- **Shared components:** `_Layout`, card CSS class.

### 5) Navigation Map
- **Nav targets:** None (no navigation links).
- **How users reach this page:** Exception handler middleware.
- **Back/breadcrumb:** Via layout navigation.

### 6) Data Dependencies & Side Effects
- **Data read:** None.
- **Data written:** None.
- **Error handling:** This IS the error handler page. No additional error handling.

### 7) Forms & Submissions
- None.

### 8) Interesting Behaviors
- Extremely minimal implementation -- no error details shown (correct for production). No request ID or correlation displayed.
- The model class has no namespace qualification in the `@model` directive but works because of `_ViewImports`.
- The code-behind namespace is in the global namespace (no `namespace` declaration), which is unusual but functional.

### 9) Traceability
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Error.cshtml.cs`

---

## 4. Pages/Diagnostic.cshtml + Pages/Diagnostic.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/Diagnostic`
- **Purpose:** System diagnostic page showing cross-tenant data: all companies, all users, shift instances, and user assignments within a date range.
- **Redirects inbound:** Owner-accessible via Home/Index "Open Diagnostics" link.
- **Redirects outbound:** None.
- **Query params:** `SelectedUserId` (int?, GET-bound), `StartDate` (string, GET-bound), `EndDate` (string, GET-bound).

### 2) Access Control & Scope
- **Auth:** `[Authorize(Roles = nameof(UserRole.Owner))]` -- Owner role required.
- **Tenant scope:** Uses `IgnoreQueryFilters()` on ALL queries (Companies, Users, ShiftAssignments, ShiftInstances, ShiftTypes). SECURITY-AUDITED comment: "Owner-only diagnostic page (B-09: restricted from AdminAccess to Owner)".
- **Potential gaps:** Uses `Roles` attribute (role-based) rather than grant-based policy. This is noted as a known exception. The page exposes ALL companies across tenants, ALL users across tenants, and ALL shift data -- appropriate for Owner diagnostics but extremely sensitive.

### 3) Localization
- **Patterns:** Both `<loc key="..." />` tag helper and `@Localizer["..."]` (IStringLocalizer<SharedResources>).
- **Key patterns:** `SystemDiagnostics`, `Filter`, `SelectUser`, `CompaniesInSystem`, `UserInformation`, `ShiftInstancesForOctober2025` (note: hardcoded month reference in key name), `UserAssignments`, etc.
- **RTL/LTR:** Handled by `_Layout`. Uses `text-align: start` for RTL compatibility in inline styles.

### 4) UI & Design Inventory
- **Layout:** `_Layout`. Max-width 1200px container.
- **Interactive elements:**
  - Breadcrumb (via ViewComponent)
  - User dropdown (select) for filtering assignments
  - Start Date / End Date date pickers
  - "Apply" filter button (form submit)
- **UI states:**
  - No user selected: "Select a user to view assignments" message
  - User selected, no assignments: "No assignments found"
  - No shift instances: "No shift instances found"
  - Companies table always shown (populated from all tenants)
- **Shared components:** Breadcrumb ViewComponent, `_Layout`, card CSS.

### 5) Navigation Map
- **Nav targets:** None (no outbound links).
- **How users reach this page:** Owner Home dashboard "Open Diagnostics" link, direct URL.
- **Back/breadcrumb:** Breadcrumb shows "System Diagnostics" as active.

### 6) Data Dependencies & Side Effects
- **Data read (all cross-tenant):**
  - `Companies` (all, no filter)
  - `Users` (all, ordered by email -- for dropdown)
  - `Users` (single, by SelectedUserId -- for info display)
  - `ShiftAssignments` + `ShiftInstances` (by user and date range -- for assignment table)
  - `ShiftInstances` + `ShiftTypes` (by date range, limited to 500 -- for instance table)
- **Data written:** None. Read-only diagnostic page.
- **Error handling:** Date parsing uses `TryParse` with fallback defaults (1 month before/after now). No explicit error handling.

### 7) Forms & Submissions
- **Filter form:** `method="get"` -- submits filter parameters as query string.
  - Fields: `SelectedUserId` (select), `StartDate` (date), `EndDate` (date)
  - Defaults: No user selected; StartDate = 1 month ago; EndDate = 1 month from now
  - Validation: DateOnly.TryParse with fallback

### 8) Interesting Behaviors
- `Take(500)` limit on ShiftInstances query (C-05: prevents OOM on large deployments).
- Uses `border-inline-start` for RTL-compatible warning box styling.
- The localization key `ShiftInstancesForOctober2025` appears hardcoded to a specific month, which is misleading since the actual date range is dynamic.
- Embedded `<style>` block for diagnostic-row hover effects and form element styling.

### 9) Traceability
- **Services:** `AppDbContext`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Diagnostic.cshtml.cs`

---

## 5. Pages/GriffinDiagnostic.cshtml + Pages/GriffinDiagnostic.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/GriffinDiagnostic`
- **Purpose:** Comprehensive diagnostic page for Griffin ADFS SSO integration. Validates configuration, tests server connectivity, compares URL encoding approaches, and provides root cause analysis for authentication failures.
- **Redirects inbound:** Linked from itself ("Refresh Diagnostic"), and from admin navigation.
- **Redirects outbound:** Links to `/Owner/GriffinConfig`, `/Auth/Login`, self-refresh.
- **Query params:** None.

### 2) Access Control & Scope
- **Auth:** `[Authorize(Policy = "Grant:AdminAccess")]` -- requires AdminAccess grant (grant-based authorization).
- **Tenant scope:** Loads GriffinConfig via `_griffinConfigService.GetGriffinConfigAsync()` which resolves tenant-scoped config. Also checks `_dbContext.GriffinConfigs.FirstOrDefaultAsync()` directly (first config in DB -- may cross tenants). Uses `_griffinConfigService.GetAnyEnabledGriffinConfigAsync()` for unauthenticated availability check.
- **Potential gaps:** The direct `_dbContext.GriffinConfigs.FirstOrDefaultAsync()` call does not use `IgnoreQueryFilters()` explicitly, meaning it should be tenant-filtered. However, the `GetAnyEnabledGriffinConfigAsync()` call is designed to work across tenants for the login page. The `RootCauseExplanation` and `FixInstructions` use `Html.Raw()` with string concatenation using `HtmlEncoder.Default.Encode()` which is safe.

### 3) Localization
- **Patterns:** `@Localizer["..."]` (IStringLocalizer<SharedResources>) extensively. Also `<loc key="..." />` tag helper.
- **Key patterns:** `GriffinDiagnosticTitle`, `CriticalError`, `OverallStatus`, `ValidationScore`, `ServerStatus`, `DiagnosticChecks`, `CurrentConfiguration`, many URL-encoding comparison keys.
- **RTL/LTR:** No explicit RTL handling -- page uses `Layout = null` with standalone HTML. Uses `border-left` instead of `border-inline-start` in some places (RTL bug). `field` class uses `border-left: 4px solid var(--primary)` which won't flip in RTL.

### 4) UI & Design Inventory
- **Layout:** `Layout = null` -- standalone full HTML page with custom CSS. Links `tokens.css` only.
- **Interactive elements:**
  - "Griffin Configuration" link button
  - "Refresh Diagnostic" link button
  - "Login Page" link button
  - "Print Report" button (calls `window.print()`)
  - No forms or user input
- **UI states:**
  - Critical error state (HasError = true)
  - Summary cards: pass/fail/warning states with color coding
  - Warnings section (collapsible list)
  - Diagnostic checks list (checkmarks/crosses/warnings)
  - Configuration details (if GriffinConfig exists)
  - Generated authentication URL analysis
  - Root cause analysis (if detected)
  - Fix instructions
  - URL encoding comparison (Old vs New vs Doof approaches)
  - JSON diagnostic export
  - No config found state
- **Shared components:** None (standalone page). Uses CSS tokens.

### 5) Navigation Map
- **Nav targets:**
  - `/Owner/GriffinConfig` (Griffin Configuration)
  - `/GriffinDiagnostic` (self -- Refresh)
  - `/Auth/Login` (Login Page)
  - `window.print()` (Print Report)
- **How users reach this page:** Admin navigation, direct URL.
- **Back/breadcrumb:** None (standalone page).

### 6) Data Dependencies & Side Effects
- **Data read:**
  - `GriffinConfigs` table (direct DB query + service calls)
  - `IConfiguration` (appsettings.json fallback check)
  - `IGriffinConfigService.GetGriffinConfigAsync()` (tenant-scoped config)
  - `IGriffinConfigService.GetAnyEnabledGriffinConfigAsync()` (any enabled config)
  - `IGriffinConfigService.TestConnectionAsync()` (connectivity test to Griffin server)
  - `IGriffinService.BuildAuthenticationUrl()` (URL generation test)
  - File system check (`File.Exists` on GriffinCallback.cshtml)
- **Data written:** None. Read-only diagnostic page.
- **Error handling:** Top-level try/catch wraps entire `OnGetAsync`. Individual sections have nested try/catch for connectivity tests and file checks.

### 7) Forms & Submissions
- None.

### 8) Interesting Behaviors
- Generates a complete URL encoding comparison showing three approaches: Old (broken -- entire callback URL double-encoded), New (fixed -- clean tokenConsumerURL, returnUrl in cookie), and Doof (reference implementation -- working).
- Root cause analysis runs automatically based on validation results.
- Diagnostic JSON export is pre-rendered for copy/paste to support.
- The page makes an actual HTTP connection test to the Griffin server during load.
- The comparison table is designed for air-gapped debugging where browser dev tools may not be available.
- `Html.Raw()` is used for RootCauseExplanation and FixInstructions -- these are server-generated HTML with `HtmlEncoder.Default.Encode()` for user data.

### 9) Traceability
- **Services:** `IGriffinConfigService`, `IGriffinService`, `IHttpClientFactory`, `IConfiguration`, `AppDbContext`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\GriffinDiagnostic.cshtml.cs`

---

## 6. Pages/Auth/Login.cshtml + Pages/Auth/Login.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/Auth/Login`
- **Purpose:** User authentication page with email/password login and optional Griffin ADFS SSO.
- **Redirects inbound:**
  - Logout page auto-redirects here via `<meta http-equiv="refresh">`
  - ASP.NET auth middleware redirects unauthorized users here
  - `/Auth/Signup` links here
  - `/Auth/ForgotPassword` links here
- **Redirects outbound (POST success):**
  - Forced password change: `/Auth/ForgotPassword`
  - First login onboarding: `/My/Onboarding`
  - Custom returnUrl (if local URL)
  - Owner: `/Home/Index`
  - Non-Owner: `/` (root Index page)
  - Griffin ADFS: external redirect to Griffin server authentication URL
- **Query params:** `reason` (string, "authRequired"), `returnUrl` (string, URL to redirect after login).

### 2) Access Control & Scope
- **Auth:** `[AllowAnonymous]` -- accessible without authentication.
- **Tenant scope:** Login uses `IgnoreQueryFilters()` to search users across all companies (SECURITY-AUDITED: necessary for cross-tenant login). User lookup by email + IsActive.
- **Potential gaps:**
  - Rate limiting: 10 attempts per 15 minutes per IP, 15 per account per 15 minutes.
  - Account lockout after 10 failed attempts (3-minute lockout).
  - Input validation: email max 255 chars, password max 500 chars, regex email validation.
  - The `returnUrl` is validated with `Url.IsLocalUrl()` before redirect (prevents open redirect).
  - Griffin ADFS flow stores returnUrl in HttpOnly cookie with 5-minute TTL.

### 3) Localization
- **Patterns:** `<loc key="..." />` tag helper, `@Localizer["..."]`, `@_localizer["..."]` in code-behind (via `LocalizedPageModel` base class).
- **Key patterns:** `Auth_*` prefix for auth-specific keys, `Login_*` for login-specific messages, `Error_Login_*` for error messages, `LoginWithADFS`, `Email`, `Password`.
- **RTL/LTR:** Explicit `dir` and `lang` attributes on `<html>` element using `CultureInfo.CurrentUICulture`. Language toggle button switches between `en-US` and `he-IL` via `.AspNetCore.Culture` cookie.

### 4) UI & Design Inventory
- **Layout:** `Layout = null` -- standalone full HTML page using `tokens.css` and `auth.css`.
- **Interactive elements:**
  - Language toggle button (`authLanguageToggle`)
  - Theme toggle button (`authThemeToggle` -- light/dark)
  - Griffin ADFS login button (form POST to `Griffin` handler) -- conditional
  - Email input (type="email", required, autocomplete="email")
  - Password input (type="password", required, autocomplete="current-password")
  - "Forgot Password?" link
  - Submit button ("Login") -- disabled when account locked
  - "Request Access" link to signup
  - Email domain suggestion (auto-suggests `@d360.dom` when no @ present)
- **UI states:**
  - Normal login form
  - Auth required prompt (info alert, when `reason=authRequired`)
  - Account locked (error alert with countdown timer)
  - Failed attempt warning (after 3+ failed attempts)
  - Error message (generic error)
  - Griffin ADFS button visible (when configured + reachable)
  - Griffin ADFS unavailable warning (when configured but unreachable)
  - Griffin ADFS hidden (when not configured)
- **Shared components:** `auth.css`, `tokens.css`, hero panel, auth-layout structure.

### 5) Navigation Map
- **Nav targets:**
  - `/Auth/ForgotPassword` (Forgot Password link)
  - `/Auth/Signup` (Request Access link)
  - Post-login redirects (see routing section)
  - Griffin ADFS external redirect
- **How users reach this page:** Direct navigation, auth middleware redirect, logout redirect.
- **Back/breadcrumb:** None (standalone auth page).

### 6) Data Dependencies & Side Effects
- **Data read (OnGet):**
  - `IGriffinConfigService.GetGriffinConfigAsync()` -- check ADFS availability
  - `IGriffinConfigService.TestConnectionAsync()` -- test ADFS connectivity
- **Data read (OnPost):**
  - `Users` table (IgnoreQueryFilters, by email + IsActive, includes RoleTemplate + JobType)
  - `IHierarchyService.GetUserHierarchyContextAsync()` -- for claims and grant provisioning
- **Data written (OnPost):**
  - `User.LastLoginAttempt` (DateTime)
  - `User.FailedLoginAttempts` (increment on failure, reset on success)
  - `User.LockoutEnd` (set on lockout, cleared on success)
  - `User.RoleTemplateId` (backfill if null at login time)
  - `User.Role` (updated from RoleTemplate.DerivedUserRole during backfill)
  - Rate limit counters (via `IRateLimitingService`)
  - Auto-grants application (via `IGrantService.ApplyAutoGrantsAsync`)
  - ASP.NET auth cookie (via `HttpContext.SignInAsync`)
  - Griffin returnUrl cookie (OnPostGriffin)
- **Error handling:** Top-level try/catch on OnPost, localized error messages, PII masking in logs.

### 7) Forms & Submissions
- **Login form:** `method="post"` with anti-forgery token.
  - Fields: `Email` (email, required), `Password` (password, required)
  - Validation: Non-empty, length limits (255/500), regex email format
  - Rate limiting: per-IP (10/15min), per-account (15/15min)
- **Griffin form:** `method="post"` with handler `Griffin` and `returnUrl` route value.
  - No user input fields (button-only)
  - Validates Griffin config before redirect

### 8) Interesting Behaviors
- Login-time RoleTemplate backfill: if user has no `RoleTemplateId`, derives one from `Role + JobType` using `RoleTemplateMapper`.
- Login-time grant reconciliation: `ApplyAutoGrantsAsync` ensures grants match template (idempotent, picks up new grants).
- Claims include hierarchy context: `MoleculeId`, `AreaId`, `ProjectId`, `IsWorkforce`, `IsTech`, `JobTypeId`, `JobTypeName`, `DepartmentId`.
- The email domain suggestion hardcodes `@d360.dom` domain.
- Lockout countdown timer in JS updates every 10 seconds with Hebrew/English messages.
- No-cache headers set on GET to prevent stale page caching.
- `MustChangePassword` flag forces redirect to ForgotPassword on next login.
- `HasCompletedOnboarding` flag forces redirect to Onboarding wizard on first login.

### 9) Traceability
- **Services:** `AppDbContext`, `IRateLimitingService`, `IValidationService`, `IGriffinConfigService`, `IGriffinService`, `IHierarchyService`, `IGrantService`, `IRoleService`, `IStringLocalizer<SharedResources>`
- **Base class:** `LocalizedPageModel` (provides `_localizer` and `Error` property)
- **Helper:** `Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey()`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Auth\Login.cshtml.cs`

---

## 7. Pages/Auth/Logout.cshtml + Pages/Auth/Logout.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/Auth/Logout`
- **Purpose:** Logs user out, clears authentication cookies, and shows confirmation with auto-redirect to login.
- **Redirects inbound:** User clicks logout from navigation.
- **Redirects outbound:** Auto-redirect to `/Auth/Login` via `<meta http-equiv="refresh" content="3">` after 3 seconds. Also has a "Go to Login Now" link button.
- **Query params:** None.

### 2) Access Control & Scope
- **Auth:** `[AllowAnonymous]` -- accessible without authentication (needed for the logout flow).
- **Tenant scope:** None.
- **Potential gaps:** The page only handles POST (no OnGet handler that logs out), which is correct -- prevents CSRF logout via GET. However, if a user navigates to `/Auth/Logout` via GET, they'll see the "Logging out... Please wait" state indefinitely since `LoggedOut` defaults to `false` and there's no GET handler that changes it.

### 3) Localization
- **Patterns:** `<loc key="..." />` tag helper, `@Localizer["..."]`.
- **Key patterns:** `Auth_LoggedOutTitle`, `Auth_SuccessfullyLoggedOut`, `Auth_SecurelyLoggedOutMessage`, `Auth_RedirectingToLogin`, `Auth_GoToLoginNow`, `Auth_LoggingOut`, `Auth_PleaseWaitLoggingOut`.
- **RTL/LTR:** Explicit `dir` and `lang` attributes on `<html>` element.

### 4) UI & Design Inventory
- **Layout:** `Layout = null` -- standalone full HTML page using `site.css`.
- **Interactive elements:**
  - "Go to Login Now" button/link (only shown after logout)
- **UI states:**
  - Logged out state: success checkmark, confirmation message, "Redirecting to login..." text, login link
  - Not logged out state: "Logging out... Please wait" (shown on GET, which has no handler to change state)
- **Shared components:** `site.css`.

### 5) Navigation Map
- **Nav targets:** `/Auth/Login` (auto-redirect and manual link).
- **How users reach this page:** Logout action from navigation (POST).
- **Back/breadcrumb:** None.

### 6) Data Dependencies & Side Effects
- **Data read:** User claims (`AuthMethod`, `Griffin:TokenHash`, `ClaimTypes.NameIdentifier`).
- **Data written:**
  - Deletes `griffin.token` cookie (if Griffin auth)
  - Removes Griffin claims cache entry from `IMemoryCache`
  - Signs out via `HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)`
- **Error handling:** Checks `User?.Identity?.IsAuthenticated` before attempting sign-out. No try/catch.

### 7) Forms & Submissions
- POST only (no form on this page -- the form is on the navigation/layout that POSTs here).

### 8) Interesting Behaviors
- Differentiates between Griffin ADFS logout and regular logout: clears Griffin-specific token cookie and cache.
- The Griffin token is identified by hash (HIGH-007: raw token no longer in cookie).
- 3-second auto-redirect via HTML `<meta>` refresh.
- Uses `aria-live="polite"` on redirect message for screen reader accessibility.

### 9) Traceability
- **Services:** `IMemoryCache`, `ILogger<LogoutModel>`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Auth\Logout.cshtml.cs`

---

## 8. Pages/Auth/Signup.cshtml + Pages/Auth/Signup.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/Auth/Signup`
- **Purpose:** Self-service access request page. Users fill out account details, select organization (molecule -> company/department), job type, and role. Creates a `UserJoinRequest` record for admin approval.
- **Redirects inbound:** `/Auth/Login` "Request Access" link.
- **Redirects outbound:** None (stays on page with success/error messages). Links to `/Auth/Login`.
- **Query params:** None.

### 2) Access Control & Scope
- **Auth:** `[AllowAnonymous]` -- accessible without authentication.
- **Tenant scope:** Uses `IgnoreQueryFilters()` for email uniqueness check and HQ company lookup (SECURITY-AUDITED: anonymous flow, no tenant context). Molecules and Companies loaded with standard query filters (but these are cross-tenant entities).
- **Potential gaps:**
  - Gated by `AllowPublicSignup` feature flag. When disabled, shows a locked message and no form.
  - Rate limiting: 50 attempts per 10 minutes per IP.
  - Input validation: email (255), display name (200), password (128), regex email format.
  - Exposes organizational structure (molecules, companies, departments, job types) when enabled -- logged as a warning.
  - The API endpoints `/Api/Signup/GetSignupOptions` are called from JS for cascading dropdowns.

### 3) Localization
- **Patterns:** `<loc key="..." />` tag helper, `@Localizer["..."]`, `@_localizer["..."]` (code-behind). Also `@Localizer["..."].Value.Trim()` in inline JS strings.
- **Key patterns:** `Auth_*` prefix for auth-specific keys, `RequestAccess`, `Auth_StepAccount`, `Auth_StepOrganization`, `Auth_StepRole`, `Auth_Select*`, `Auth_Choose*`, `Error_Signup_*`.
- **RTL/LTR:** Explicit `dir` and `lang` attributes on `<html>`. Uses `margin-inline-start` for RTL-safe spacing.

### 4) UI & Design Inventory
- **Layout:** `Layout = null` -- standalone full HTML page using `tokens.css` and `auth.css`.
- **Interactive elements:**
  - 3-step progress indicator (Account, Organization, Role) -- updates dynamically
  - Email input (type="email", required, autocomplete="email") with domain suggestion
  - Display Name input (type="text", required)
  - Password input (type="password", required, minlength=6)
  - Molecule dropdown (select, required) -- populated server-side
  - Company/Department dropdown (select, required) -- populated via API cascade, label changes for Tech molecules
  - Hidden DepartmentId field (populated by JS for Tech molecules)
  - Job Type dropdown (select, required) -- populated via API, hidden for Tech molecules
  - Requested Role dropdown (select, required) -- populated via API, filtered by molecule type
  - Hidden RequestedRoleTemplateId field (synced by JS)
  - Submit button ("Submit Request")
  - "Already have account? Login here" link
- **UI states:**
  - Public signup disabled: locked warning card, no form
  - Public signup enabled: full form with cascading dropdowns
  - Success: pending request confirmation message (green alert)
  - Error: error message (red alert)
  - Director/AreaAdmin: company field hidden, auto-assigned to HQ
  - Tech molecule: department dropdown replaces company, job type hidden
  - Loading states for API-driven dropdowns
- **Shared components:** Auth hero panel, auth-layout, `auth.css`, `tokens.css`.

### 5) Navigation Map
- **Nav targets:** `/Auth/Login` (Login link).
- **How users reach this page:** Login page "Request Access" link, direct URL.
- **Back/breadcrumb:** None.

### 6) Data Dependencies & Side Effects
- **Data read (OnGet):**
  - `IFeatureFlagService.IsEnabledAsync("AllowPublicSignup")` -- gate check
  - `Molecules` table (active, ordered by display name)
  - `Companies` table (non-HQ, ordered by name) -- fallback
- **Data read (OnPost):**
  - Same as OnGet for page re-render
  - `IRoleService.GetRoleTemplateAsync()` (validate template ID)
  - `Molecules` (by ID for type detection)
  - `Companies` (HQ lookup with IgnoreQueryFilters for Director/Tech)
  - `Departments` (validate department for Tech molecule)
  - `Users` (IgnoreQueryFilters, email uniqueness check)
  - `UserJoinRequests` (IgnoreQueryFilters, duplicate request check)
  - `ICompanyCacheService.GetCompanyAsync()` (company existence + name)
- **Data written (OnPost):**
  - `UserJoinRequests.Add()` (new join request)
  - Rate limit counter via `IRateLimitingService`
- **Side effects:** `INotificationService.NotifyOwnersOfAccessRequestAsync()` -- fire-and-forget notification to owners.
- **Error handling:** Try/catch on save, localized error messages.

### 7) Forms & Submissions
- **Signup form:** `method="post"` with anti-forgery token.
  - Fields: `Email` (email, required), `DisplayName` (text, required), `Password` (password, required, minlength=6), `MoleculeId` (select), `CompanyId` (select), `DepartmentId` (hidden), `JobTypeId` (select), `RequestedRole` (select), `RequestedRoleTemplateId` (hidden)
  - Validation: Data annotations (`[Required]`, `[EmailAddress]`, `[MinLength(6)]`) + manual validation
  - Rate limiting: 50 attempts per 10 minutes per IP

### 8) Interesting Behaviors
- Cascading dropdown architecture: Molecule -> Company/Department + JobTypes, with API calls to `/Api/Signup/GetSignupOptions`.
- Role template filtering by molecule type: Tech molecules hide CompanyJobType and MoleculeJobType scoped roles; Workforce/Helper molecules hide Department-scoped roles.
- Director/AreaAdmin roles auto-assign to HQ company of the selected molecule.
- Tech molecules auto-assign to HQ company and clear JobTypeId.
- Bug fixes documented in JS comments: Bug 1 (disabled company select prevents empty POST), Bug 2 (saved company value when switching roles), Bug 3 (relaxed rate limit from 5/15min to 50/10min), Bug 4 (page state loaded before rate limit check).
- Email domain suggestion hardcodes `@d360.dom`.
- Password hashing uses `PasswordHasher.CreateHash()` (PBKDF2/SHA256/100k iterations per project memory).
- The notification to owners is fire-and-forget with `Task.Run` -- does not block the response.

### 9) Traceability
- **Services:** `AppDbContext`, `IFeatureFlagService`, `IValidationService`, `INotificationService`, `IRateLimitingService`, `ICompanyCacheService`, `IRoleService`, `IStringLocalizer<SharedResources>`
- **Base class:** `LocalizedPageModel`
- **API endpoints used (client-side):** `/Api/Signup/GetSignupOptions?handler=RoleTemplates`, `...?handler=Companies&moleculeId=`, `...?handler=Departments&moleculeId=`, `...?handler=JobTypes&moleculeId=`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Auth\Signup.cshtml.cs`

---

## 9. Pages/Auth/ForgotPassword.cshtml + Pages/Auth/ForgotPassword.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/Auth/ForgotPassword`
- **Purpose:** Password management page with two functions: (1) forgot password recovery via email+phone verification, and (2) direct password change with old password verification.
- **Redirects inbound:** Login page "Forgot Password?" link. Also: Login POST forces redirect here when `user.MustChangePassword` is true.
- **Redirects outbound:** "Back to Login" link to `/Auth/Login`.
- **Query params:** None.

### 2) Access Control & Scope
- **Auth:** `[AllowAnonymous]` -- accessible without authentication.
- **Tenant scope:** Uses `IgnoreQueryFilters()` for user lookup by email (SECURITY-AUDITED: anonymous flow, cross-tenant password recovery).
- **Potential gaps:**
  - Rate limiting: 3 attempts per 15 minutes per IP (forgot password), 5 per 15 minutes per IP (change password).
  - Input validation: email (255), phone (50), regex email format, regex phone format.
  - Constant-time delay (800ms) on failed lookup to prevent user enumeration via timing.
  - Password change validates old password before allowing change.
  - Generic error messages that don't reveal user existence.

### 3) Localization
- **Patterns:** `<loc key="..." />` tag helper, `@Localizer["..."]`, `@_localizer["..."]` (code-behind).
- **Key patterns:** `PasswordManagement`, `ForgotPassword`, `ChangePassword`, `TemporaryPasswordGenerated`, `SendTemporaryPassword`, `ChangePasswordButton`, `Error_*`.
- **RTL/LTR:** Handled by `_Layout`. Uses `border-inline-start` for RTL-compatible warning box. Email body sets `dir` attribute based on localization.
- **Note:** The "Change Password" card description "If you already know your current password..." is hardcoded in English (not localized).

### 4) UI & Design Inventory
- **Layout:** `_Layout`. Max-width 480px centered card.
- **Interactive elements:**
  - **Card 1 - Forgot Password:**
    - Email input (type="email", required)
    - Phone input (type="tel", required)
    - Submit button ("Send Temporary Password")
  - **Card 2 - Change Password:**
    - Email input (type="email", required)
    - Old Password input (type="password", required)
    - New Password input (type="password", required, minlength=6)
    - Submit button ("Change Password")
  - "Copy Password" button (shown only when temp password generated)
  - "Back to Login" link
- **UI states:**
  - Normal state: both cards visible
  - Temp password generated: success card with password display and copy button (only on email failure fallback)
  - Success message (green alert)
  - Error message (red alert)
  - Password change success/error (separate messages)
- **Shared components:** `_Layout`, card CSS, `<loc>` tag helper.

### 5) Navigation Map
- **Nav targets:** `/Auth/Login` (Back to Login link).
- **How users reach this page:** Login page "Forgot Password?" link, forced redirect when `MustChangePassword` is true.
- **Back/breadcrumb:** "Back to Login" link at bottom.

### 6) Data Dependencies & Side Effects
- **Data read:**
  - `Users` (IgnoreQueryFilters, by email + phone match for forgot password)
  - `Users` (IgnoreQueryFilters, by email for password change)
- **Data written (OnPost - forgot password):**
  - `User.PasswordHash` (new hash from temp password)
  - `User.PasswordSalt` (new salt from temp password)
  - `User.MustChangePassword = true` (A-07: forces change on next login)
- **Data written (OnPostChangePassword):**
  - `User.PasswordHash` (new hash)
  - `User.PasswordSalt` (new salt)
  - `User.MustChangePassword = false` (clears forced change flag)
- **Side effects:**
  - `IMailService.SendMailAsync()` -- sends temp password via email
  - If email fails: temp password displayed on screen (air-gapped fallback I-01)
- **Error handling:** Try/catch on both handlers, localized error messages, PII-safe logging via `RedactEmail()`.

### 7) Forms & Submissions
- **Forgot Password form:** `method="post"` (default handler) with anti-forgery token.
  - Fields: `Email` (email, required), `Phone` (tel, required)
  - Validation: non-empty, length limits, regex email and phone validation
  - Rate limiting: 3 per 15 minutes per IP
- **Change Password form:** `method="post"` with handler `ChangePassword` and anti-forgery token.
  - Fields: `ChangeEmail` (email, required), `OldPassword` (password, required), `NewPassword` (password, required, minlength=6)
  - Validation: non-empty, regex email, old password verification, new password min 12 chars (D-09: military environment)
  - Rate limiting: 5 per 15 minutes per IP

### 8) Interesting Behaviors
- Two-card design: forgot password (email+phone) and change password (email+old+new) on the same page.
- Minimum 12-character password requirement for change password (D-09) vs 6-character minimum in HTML `minlength` attribute -- HTML says 6 but server enforces 12. This is a UX inconsistency.
- Temp password generation uses `RandomNumberGenerator` with rejection sampling (D-11) to avoid modulus bias.
- Constant-time response (D-05): 800ms minimum delay on failed lookup.
- Email-first fallback: tries to send temp password via email. If email delivery fails (air-gapped environment I-01), displays password on screen.
- B-01 comment: "Do NOT display temp password on screen -- shoulder-surfing risk in military environment" -- but I-01 fallback DOES display it when email fails. These are competing requirements resolved by the email-first approach.
- The `copyTempPassword()` JS function uses Clipboard API with fallback to `document.execCommand('copy')`.
- No-cache headers set on GET to prevent temp password caching.
- The `_localizer["Copied"].Value.Trim()` call in JS uses Razor interpolation (not `Json.Serialize`) -- potential XSS if localization values contain quotes. However, `Trim()` alone doesn't add any injection vector.

### 9) Traceability
- **Services:** `AppDbContext`, `IMailService`, `IRateLimitingService`, `IValidationService`, `IStringLocalizer<SharedResources>`
- **Base class:** `LocalizedPageModel`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Auth\ForgotPassword.cshtml.cs`

---

## 10. Pages/Auth/GriffinCallback.cshtml + Pages/Auth/GriffinCallback.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/Auth/GriffinCallback`
- **Purpose:** Callback page for Griffin ADFS SSO authentication. Receives the authentication token from Griffin, validates it, creates the ASP.NET auth session, and redirects to the original page.
- **Redirects inbound:** Griffin ADFS server redirects here after authentication with `?token=...` query parameter.
- **Redirects outbound:**
  - Success: `returnUrl` (from query string or cookie) or `/Home/Index` (default)
  - Error/Pending: stays on page with message
- **Query params:** `token` (string, required -- the Griffin authentication token), `returnUrl` (string, optional).

### 2) Access Control & Scope
- **Auth:** `[AllowAnonymous]` -- accessible without authentication (callback from external SSO).
- **Tenant scope:** Handled by `IGriffinService.AuthenticateUserAsync()` which resolves the user internally.
- **Potential gaps:**
  - Token is validated via `_griffinService.AuthenticateUserAsync()`.
  - `returnUrl` is validated with `Url.IsLocalUrl()` before redirect (prevents open redirect).
  - Security events logged via `ISecurityLogger`.
  - The Griffin token is stored in a cookie (`griffin.token`) with HttpOnly, 8-hour expiry.

### 3) Localization
- **Patterns:** `<loc key="..." />` tag helper with inline fallback text (e.g., `<loc key="Auth_AuthenticationFailed">Authentication Failed</loc>`), `_localizer["..."].Value` in code-behind.
- **Key patterns:** `Auth_AuthenticationFailed`, `Auth_AccountPendingApproval`, `Auth_CompletingAuthentication`, `Error_MissingAuthToken`, `Error_GriffinNotEnabled`, `Error_AuthenticationFailed`, `Info_AccountPendingApproval`.
- **RTL/LTR:** Not explicitly handled -- this page uses Bootstrap classes (`container`, `alert`, `btn`) rather than the custom auth layout, suggesting it may be older. Relies on layout for direction.

### 4) UI & Design Inventory
- **Layout:** Uses default layout (no explicit `Layout` setting) -- inherits from `_ViewStart.cshtml` which likely sets `_Layout`.
- **Interactive elements:**
  - "Return to Login" button (shown on error/pending states)
- **UI states:**
  - Error state: red alert with error message and "Return to Login" button
  - Pending approval state: yellow/warning alert with pending message and "Return to Login" button
  - Processing state: spinner with "Completing authentication..." text
- **Shared components:** Bootstrap alert classes (`alert-danger`, `alert-warning`), spinner.

### 5) Navigation Map
- **Nav targets:**
  - `/Auth/Login` (Return to Login button -- error/pending states)
  - `returnUrl` or `/Home/Index` (success redirect)
- **How users reach this page:** Griffin ADFS server redirect after SSO authentication.
- **Back/breadcrumb:** None.

### 6) Data Dependencies & Side Effects
- **Data read:**
  - `IGriffinConfigService.GetGriffinConfigAsync()` -- config validation
  - `IGriffinService.AuthenticateUserAsync(token, config, ipAddress)` -- token validation and user authentication
  - `IGriffinService.ValidateAndGetClaimsAsync(token, baseUrl, timeout)` -- secondary check for pending users
  - `Request.Cookies["griffin.returnUrl"]` -- stored return URL
- **Data written:**
  - `griffin.token` cookie (8-hour, HttpOnly)
  - ASP.NET auth cookie (via `HttpContext.SignInAsync`, 8-hour, persistent)
  - Deletes `griffin.returnUrl` cookie
- **Side effects:**
  - `ISecurityLogger.LogAuthenticationSuccess()` or `LogAuthenticationFailure()` -- security audit logging
- **Error handling:** Try/catch on authentication call, localized error messages.

### 7) Forms & Submissions
- None. This is a GET-only callback page.

### 8) Interesting Behaviors
- Uses cookie-based returnUrl approach: Login page stores returnUrl in `griffin.returnUrl` cookie, callback reads it and cleans up.
- Falls back to query string `returnUrl` first, then cookie. This handles both approaches gracefully.
- The page checks for "pending approval" users separately: if `AuthenticateUserAsync` returns null, it calls `ValidateAndGetClaimsAsync` to see if the token is valid but the user is pending.
- The Griffin token cookie has `/` path (site-wide) while the returnUrl cookie has `/Auth` path.
- Uses Bootstrap classes inconsistently with the rest of the auth pages which use custom auth CSS.

### 9) Traceability
- **Services:** `IGriffinService`, `IGriffinConfigService`, `ISecurityLogger`, `IStringLocalizer<SharedResources>`
- **Base class:** `LocalizedPageModel`
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Auth\GriffinCallback.cshtml.cs`

---

## 11. Pages/Home/Index.cshtml + Pages/Home/Index.cshtml.cs

### 1) Identity & Routing
- **Route:** `@page` -- default route `/Home/Index` (also accessible as `/Home`)
- **Purpose:** Role-aware home dashboard with next shift, weekly summary, notifications, and role-specific metric cards (employee requests, manager staffing/approvals, director/owner company overview and analytics).
- **Redirects inbound:** Login redirects Owner here (`return RedirectToPage("/Home/Index")`). Griffin callback defaults here on success.
- **Redirects outbound:** None (links only).
- **Query params:** None.

### 2) Access Control & Scope
- **Auth:** `[Authorize]` -- requires authentication.
- **Grant checks:**
  - `IsAdmin` = `HasGrantAsync(userId, "AccessAdminNavigation")`
  - `IsDirector` = `HasGrantAsync(userId, "DirectorHubAccess")`
  - `IsOwner` = `HasGrantAsync(userId, "AdminAccess")`
- **Tenant scope:** Uses `_companyContext.GetCompanyIdOrThrow()` for company scoping. Owner analytics use `IgnoreQueryFilters()` (SECURITY-AUDITED: Owner-only, aggregate counts). All other queries respect EF tenant filters.
- **Potential gaps:** Director data uses `DirectorCompanies` table (which is tenant-filtered). Owner data uses `IgnoreQueryFilters()` for global counts.

### 3) Localization
- **Patterns:** `<loc key="..." />` tag helper, `@Localizer["..."]`, `@Localization.Format*()` (ILocalizationService).
- **Key patterns:** `Home`, `GoodMorning`/`GoodAfternoon`/`GoodEvening` (with `{0}` placeholder for first name), `NextShift`, `ThisWeek`, `Notifications`, `HoursThisWeek`, `DaysWithShifts`, `DaysOff`, `MyRequests`, `StaffingOverview`, `Approvals`, `CompaniesOverview`, `AnalyticsSummary`, `SystemHealth`.
- **RTL/LTR:** Handled by `_Layout`. Uses `margin-inline-start` for RTL-safe badge spacing.

### 4) UI & Design Inventory
- **Layout:** `_Layout`. Uses `Breadcrumb` ViewComponent.
- **Interactive elements (all are links, no forms):**
  - "View Full Schedule" button -> `/Calendar/Month`
  - "View All Notifications" button -> `/My/NotificationCenter`
  - "Go to My Requests" link -> `/My/Requests` (employee)
  - "Open Scheduling Workspace" link -> `/Calendar/Month` (manager/director)
  - "Open Approvals" link -> `/Requests/Index` (manager/director)
  - "Manage Companies" link -> `/Admin/Companies` (owner)
  - "View Full Analytics" link -> `/Admin/Analytics` (owner)
  - "Open Diagnostics" link -> `/Diagnostic` (owner)
- **UI states:**
  - Greeting changes by time of day (Morning/Afternoon/Evening) with first name
  - Next shift: present vs "No upcoming shifts"
  - Notifications: present (up to 3) vs "No notifications"; unread badge count
  - Employee view: My Requests cards (Pending/Approved/Declined counts)
  - Manager (admin, not director) view: Staffing cards (Unassigned/Understaffed) + Approvals cards (TimeOff/Swaps)
  - Director (non-owner) view: Staffing + Approvals + Companies Overview
  - Owner view: Staffing + Approvals + Companies Overview + Analytics Summary (users, shifts this month, staffing rate %) + System Health section
  - Staffing rate color coding: green (>=90%), yellow (>=75%), red (<75%)
- **Shared components:** Breadcrumb ViewComponent, `_Layout`, card CSS, badge CSS.

### 5) Navigation Map
- **Nav targets:**
  - `/Calendar/Month` (View Full Schedule, Open Scheduling Workspace)
  - `/My/NotificationCenter` (View All Notifications)
  - `/My/Requests` (Go to My Requests)
  - `/Requests/Index` (Open Approvals)
  - `/Admin/Companies` (Manage Companies)
  - `/Admin/Analytics` (View Full Analytics)
  - `/Diagnostic` (Open Diagnostics)
- **How users reach this page:** Post-login redirect for Owner users. Also accessible via navigation sidebar.
- **Back/breadcrumb:** Breadcrumb shows "Home" as active.

### 6) Data Dependencies & Side Effects
- **Data read (common - all users):**
  - `ShiftAssignments` + `ShiftInstances` + `ShiftTypes` (next shift, weekly shifts)
  - `UserNotifications` (recent 5, ordered by CreatedAt desc)
  - Computed: `HoursThisWeek`, `DaysWithShiftsThisWeek`, `DaysOffThisWeek`
- **Data read (employee):**
  - `TimeOffRequests` (pending/approved/declined counts by userId)
- **Data read (manager):**
  - `ShiftInstances` (next 30 days, with ShiftType)
  - `ShiftAssignments` (next 30 days)
  - `TimeOffRequests` (pending count by companyId)
  - `SwapRequests` (pending count by companyId)
  - Computed: `UnassignedShiftsCount`, `UnderstaffedDaysCount`
- **Data read (director - adds to manager):**
  - `DirectorCompanies` (company count by userId, non-deleted)
- **Data read (owner - adds to director):**
  - `Companies` (total count, with query filters for tenant scoping)
  - `Users` (IgnoreQueryFilters, active count)
  - `ShiftInstances` (IgnoreQueryFilters, this month count + list for staffing calc)
  - `ShiftAssignments` (IgnoreQueryFilters, grouped by instance for staffing calc)
  - Computed: `AverageStaffingRate`
- **Data written:** None. Read-only dashboard.
- **Error handling:** Early return if userId claim parsing fails. No explicit error handling for DB queries.

### 7) Forms & Submissions
- None. This page has no forms.

### 8) Interesting Behaviors
- Uses `Task.WhenAll` for parallel query execution (Phase 2C performance optimization): common data queries, manager data queries, and owner analytics queries all run in parallel.
- Time-of-day greeting: `DateTime.Now.Hour` (server local time) for Morning (<12), Afternoon (<18), Evening.
- Weekly calculation uses `DateTime.Today.DayOfWeek` for start of week (Sunday=0) -- assumes Sunday-start weeks.
- `DaysOff` is calculated as `7 - DaysWithShiftsThisWeek` which may be misleading (doesn't account for non-scheduled days vs actual days off).
- Owner analytics: `ActiveCompaniesCount = TotalCompaniesCount` -- all companies considered active (no inactive flag check).
- Director: `TotalCompaniesCount` comes from `DirectorCompanies` count, not actual Companies table.
- `UnreadNotificationsCount` is calculated from the fetched 5 notifications, not a separate count query. This means if all 5 are read but there's an unread 6th, it shows 0. However, the data is loaded with `Where(n => n.UserId == userId && n.CompanyId == companyId)` and takes top 5 by date -- the unread count is from those 5.
- Note: there is a subtle bug -- `UnreadNotificationsCount` counts unread items in the top-5 result set, but the badge says "@Model.UnreadNotificationsCount Unread" which understates the true unread count if there are more than 5 notifications.

### 9) Traceability
- **Services:** `AppDbContext`, `ICompanyContext`, `IGrantService`
- **View Components:** Breadcrumb
- **File:** `C:\Users\katzi\Downloads\ShiftManager\Pages\Home\Index.cshtml.cs`

---

## Cross-Page Observations

### Common Patterns
1. **Auth pages** (Login, Signup, ForgotPassword) use `Layout = null` with standalone HTML, `tokens.css` + `auth.css`, and explicit RTL handling on `<html>`.
2. **Error/Denied pages** (AccessDenied, Error) also use standalone layouts or `_Layout`.
3. **Dashboard pages** (Index, Home/Index) use `_Layout` with `[Authorize]`.
4. **Diagnostic pages** (Diagnostic, GriffinDiagnostic) require elevated access (Owner role or AdminAccess grant).
5. All anonymous auth flows use `IgnoreQueryFilters()` with SECURITY-AUDITED comments.
6. Rate limiting is consistently applied to all POST handlers on auth pages.

### Potential Issues Found
1. **ForgotPassword HTML minlength mismatch:** The New Password field has `minlength="6"` in HTML but server enforces 12 characters (D-09). Users get a confusing server-side error after passing client-side validation.
2. **GriffinCallback CSS inconsistency:** Uses Bootstrap alert classes while all other auth pages use custom `auth-alert` classes. Visually inconsistent.
3. **Home/Index UnreadNotificationsCount:** Counts unread only from the top-5 fetched notifications, not the total unread count. May understate actual unread count.
4. **Diagnostic page localization key:** `ShiftInstancesForOctober2025` is hardcoded to a month name that no longer matches the dynamic date range.
5. **GriffinDiagnostic RTL:** Uses `border-left` instead of `border-inline-start` in the `.field` CSS class, which won't flip in RTL mode.
6. **AccessDenied page title:** "Access Denied - Shift Manager" is hardcoded in English (not localized).
7. **ForgotPassword description text:** "If you already know your current password and want to change it, use this form." is hardcoded in English.
8. **Logout GET behavior:** Navigating to `/Auth/Logout` via GET shows "Logging out... Please wait" indefinitely since there's no GET handler -- only POST performs logout.
9. **ForgotPassword JS:** The `copyTempPassword()` function uses `@Localizer["Copied"].Value.Trim()` without `Json.Serialize` -- low-risk but inconsistent with XSS-safe patterns used elsewhere.
