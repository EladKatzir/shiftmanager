# Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 35 issues identified in the v3.1.x pre-release audit across security, database, frontend, backend, localization, and new features.

**Architecture:** 6 independent agent domains working in parallel on non-overlapping files. Each domain handles a category of fixes. The only file overlap is `_Layout.cshtml` (Agent 4 first, then Agent 3) and `Program.cs` (Agents 1 and 4 touch different sections).

**Tech Stack:** ASP.NET Core 8.0, Razor Pages, SQLite, SignalR, JavaScript (vanilla), CSS

**Key References:**
- Audit report: `AUDIT-REPORT-v3.1.x.md`
- Memory: `MEMORY.md` (project patterns, pitfalls, conventions)
- CLAUDE.md rules (function reference, no workarounds, root cause only)

**Ordering constraints:**
- Agent 4 (Backend) MUST complete P1-5 (_Layout async fix) BEFORE Agent 3 (Frontend) touches `_Layout.cshtml` for P4-4 (meta tag removal).
- Agents 1 and 4 both touch `Program.cs` but in non-overlapping regions (Agent 1: ~line 470 for backup cleanup, Agent 4: ~line 1376 for HTTPS redirect). Safe to run in parallel.

---

## DOMAIN 1: Database & Infrastructure (10 items)

**Agent expertise:** SQLite internals, WAL, VACUUM, backup lifecycle, health checks, IIS config, publish pipeline

**Files to read first (understand fully before any changes):**
- `Services/DatabaseBackupService.cs` — auto-backup logic with VACUUM INTO
- `Pages/Owner/Backup.cshtml.cs` — manual backup + restore
- `Pages/Owner/Backup.cshtml` — backup UI
- `Pages/Owner/DatabaseConsole.cshtml.cs` — SQL console
- `Services/HealthChecks.cs` — health check endpoint
- `Pages/Owner/SystemHealth.cshtml.cs` — system health page
- `ViewComponents/SystemAlertsViewComponent.cs` — disk space alerts
- `Program.cs` lines 500-520 (SQLite startup config)
- `appsettings.json` (connection string)
- `FinalProductPublish/web.config`
- `scripts/Update-FinalProductPublish.ps1`

### Task 1.1: busy_timeout in Connection String (P1-2) -- DONE

**Files:**
- Modify: `appsettings.json` (connection string)

Note: There is no `appsettings.Development.json` in this project. Only `appsettings.json` and `appsettings.Production.template.json`.

- [x] **Step 1: Update connection string**

In `appsettings.json`, change:
```json
"Default": "Data Source=app.db"
```
to:
```json
"Default": "Data Source=app.db;Busy Timeout=5000"
```

Apply the same change to any other appsettings files that have a connection string.

- [x] **Step 2: Verify startup still works**

Run: `dotnet run`
Expected: App starts normally. Startup log should still show "SQLite busy_timeout set to 5000ms"

- [x] **Step 3: Verify the PRAGMA startup code is now redundant but harmless**

The existing `PRAGMA busy_timeout=5000` in Program.cs is now redundant (connection string applies it per-connection), but leave it as defense-in-depth. Add a comment noting the connection string is the primary mechanism.

- [x] **Step 4: Commit**

```bash
git add appsettings.json
git commit -m "fix: apply busy_timeout via connection string for all connections (P1-2)"
```

### Task 1.2: Database Restore Safety (P1-3) -- DONE

**Files:**
- Modify: `Pages/Owner/Backup.cshtml.cs` (restore handler)
- Modify: `Pages/Owner/Backup.cshtml` (restart instructions)

- [x] **Step 1: Read the current restore handler thoroughly**

Read `Pages/Owner/Backup.cshtml.cs` — find the `OnPostRestoreAsync` handler. Understand:
- How it currently copies the file
- What feedback it gives the user
- Whether it does any WAL checkpoint

- [x] **Step 2: Implement safe restore sequence**

In `OnPostRestoreAsync`, before the file copy:
1. Open a NEW raw SQLite connection (not from EF pool) for the checkpoint
2. Execute `PRAGMA wal_checkpoint(TRUNCATE)` to flush WAL
3. Close the raw connection
4. Call `Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()` to release ALL pooled file handles (critical on Windows — disposing DbContext alone does NOT release pooled connections)
5. Then perform the file overwrite
6. Set a success message with clear restart instructions

- [x] **Step 3: Add clear restart instructions to the UI**

In `Backup.cshtml`, update the success message and the "Important Information" section to include:
- "After restoring, restart the application by recycling the IIS App Pool or restarting the service"
- For development: "Stop and restart `dotnet run`"

- [x] **Step 4: Test restore flow**

Run the app, create a backup, make a data change, restore the backup, restart the app, verify the data change is gone.

- [x] **Step 5: Commit**

```bash
git add Pages/Owner/Backup.cshtml.cs Pages/Owner/Backup.cshtml
git commit -m "fix: safe database restore with WAL checkpoint and clear restart instructions (P1-3)"
```

### Task 1.3: DatabaseConsole Row Limit (P2-6) -- DONE

**Files:**
- Modify: `Pages/Owner/DatabaseConsole.cshtml.cs`
- Modify: `Pages/Owner/DatabaseConsole.cshtml` (show limit message)

- [x] **Step 1: Read the current query execution code**

Read `Pages/Owner/DatabaseConsole.cshtml.cs` lines 97-117. Understand how results are loaded.

- [x] **Step 2: Add row limit**

After the query is received but before execution, check if the query already has a LIMIT clause. If not, wrap or append `LIMIT 1001` (1001 to detect if limit was hit). After execution, if exactly 1001 rows returned, truncate to 1000 and set a flag `IsResultTruncated = true`.

- [x] **Step 3: Show truncation message in UI**

In `DatabaseConsole.cshtml`, when `IsResultTruncated` is true, show: "Results limited to 1,000 rows. Add a LIMIT clause to your query for specific ranges."

- [x] **Step 4: Test with broad query**

Run: `SELECT * FROM Users` — should return max 1000 rows with truncation message.

- [x] **Step 5: Commit**

```bash
git add Pages/Owner/DatabaseConsole.cshtml.cs Pages/Owner/DatabaseConsole.cshtml
git commit -m "fix: limit DatabaseConsole results to 1000 rows to prevent OOM (P2-6)"
```

### Task 1.4: Health Checks Use Connection String Path (P2-10) -- DONE

**Files:**
- Modify: `Services/HealthChecks.cs`
- Modify: `Pages/Owner/SystemHealth.cshtml.cs`
- Modify: `ViewComponents/SystemAlertsViewComponent.cs`

- [x] **Step 1: Create a helper method to extract DB path from connection string**

Add a static helper (or use existing pattern) that parses `Data Source=<path>` from the configured connection string. Use `IConfiguration` to read the connection string.

- [x] **Step 2: Update HealthChecks.cs**

Replace `Path.Combine(AppContext.BaseDirectory, "app.db")` with the parsed path from connection string.

- [x] **Step 3: Update SystemHealth.cshtml.cs**

Replace `Path.Combine(Directory.GetCurrentDirectory(), "app.db")` with the parsed path.

- [x] **Step 4: Update SystemAlertsViewComponent.cs**

Replace hardcoded `app.db` references with the parsed path.

- [x] **Step 5: Test**

Run the app, navigate to `/Owner/SystemHealth`. Verify DB size, disk space, and WAL size show correct values.

- [x] **Step 6: Commit**

```bash
git add Services/HealthChecks.cs Pages/Owner/SystemHealth.cshtml.cs ViewComponents/SystemAlertsViewComponent.cs
git commit -m "fix: health checks use connection string DB path instead of hardcoded app.db (P2-10)"
```

### Task 1.5: Backup Retention Max 2 (P2-12) -- DONE

**Files:**
- Modify: `Services/DatabaseBackupService.cs` (retention logic)
- Modify: `Program.cs` (pre-migration backup cleanup)

- [x] **Step 1: Read current retention logic**

Read `DatabaseBackupService.cs` — find the cleanup method. Understand: what pattern it matches, how it counts, what the current retention is (7 days).

- [x] **Step 2: Change retention to keep max 2 backups**

Modify the cleanup logic to: after creating a new backup, delete ALL backups except the 2 most recent (by file modification time). Apply this to ALL backup file patterns (startup, scheduled, manual).

- [x] **Step 3: Add cleanup for pre-migration backups**

In `Program.cs`, after the pre-migration backup is created, add cleanup logic that deletes all pre-migration backups except the 2 most recent. The pattern is `app.db.pre-migration-*`.

- [x] **Step 4: Test**

Restart the app multiple times. Verify only 2 backup files remain after each restart.

- [x] **Step 5: Commit**

```bash
git add Services/DatabaseBackupService.cs Program.cs
git commit -m "fix: backup retention keeps max 2 backups, cleanup pre-migration backups (P2-12)"
```

### Task 1.6: Backup Page Text + System Backup Visibility (P3-4/5) -- DONE

**Files:**
- Modify: `Pages/Owner/Backup.cshtml` (UI text and backup list)
- Modify: `Pages/Owner/Backup.cshtml.cs` (load system backups)

- [x] **Step 1: Fix the "Automatic Backups" text**

Change "Backups are created manually using the button above" to accurately describe: "Backups are created automatically on application startup and daily at 03:00. A maximum of 2 backups are retained. You can also create manual backups using the button above."

- [x] **Step 2: Show system-level backups in the list**

In the page model's `OnGetAsync`, also load system-level backup files (matching `app.db.backup-*` and `app.db.pre-migration-*` patterns from the Backups folder). Display them in a separate "System Backups" section, read-only (no restore/delete for system backups, or allow restore but not delete).

- [x] **Step 3: Test**

Navigate to `/Owner/Backup`. Verify:
- Accurate text about auto-backups
- System backups visible in the list with timestamps and sizes

- [x] **Step 4: Commit**

```bash
git add Pages/Owner/Backup.cshtml Pages/Owner/Backup.cshtml.cs
git commit -m "fix: backup page shows accurate auto-backup info and lists system backups (P3-4/5)"
```

### Task 1.7: Manual Backup VACUUM INTO (P3-10) -- DONE

**Files:**
- Modify: `Pages/Owner/Backup.cshtml.cs` (create backup handler)

- [x] **Step 1: Read current manual backup code**

Find the `OnPostCreateBackupAsync` handler. It currently uses `FileStream.CopyToAsync`.

- [x] **Step 2: Replace with VACUUM INTO**

Use the same `VACUUM INTO` approach as `DatabaseBackupService.cs`. Open a raw SQLite connection, execute `VACUUM INTO '<backup_path>'`. This ensures a consistent backup with WAL data included.

- [x] **Step 3: Test**

Create a manual backup via the Owner Backup page. Verify the file is created and has the correct size.

- [x] **Step 4: Commit**

```bash
git add Pages/Owner/Backup.cshtml.cs
git commit -m "fix: manual backup uses VACUUM INTO for consistency (P3-10)"
```

### Task 1.8: VERSION.txt Encoding (P3-11) -- DONE

**Files:**
- Modify: `FinalProductPublish/VERSION.txt`

- [x] **Step 1: Re-save with correct encoding**

Read the file, identify garbled characters, re-save as UTF-8 with BOM. Replace corrupted characters with their correct Unicode equivalents (checkmarks, emojis, copyright symbol).

- [x] **Step 2: Commit**

```bash
git add FinalProductPublish/VERSION.txt
git commit -m "fix: VERSION.txt re-saved with correct UTF-8 encoding (P3-11)"
```

### Task 1.9: stdout Logging in web.config (P3-15) -- DONE

**Files:**
- Modify: `FinalProductPublish/web.config`

- [x] **Step 1: Enable stdout logging**

Change `stdoutLogEnabled="false"` to `stdoutLogEnabled="true"`.

- [x] **Step 2: Commit**

```bash
git add FinalProductPublish/web.config
git commit -m "fix: enable stdout logging in web.config for IIS troubleshooting (P3-15)"
```

### Task 1.10: Publish Script Include Data Folder (NEW) -- DONE

**Files:**
- Modify: `scripts/Update-FinalProductPublish.ps1`

- [x] **Step 1: Read the current publish script**

Understand what it copies to `FinalProductPublish/`. Find where other folders are included.

- [x] **Step 2: Add Data folder copy**

Add a step that copies the `Data/` folder (containing required seed files) to `FinalProductPublish/Data/`.

- [x] **Step 3: Test**

Run the publish script. Verify `FinalProductPublish/Data/` exists with seed files.

- [x] **Step 4: Commit**

```bash
git add scripts/Update-FinalProductPublish.ps1
git commit -m "fix: publish script includes Data folder with required seed files"
```

---

## DOMAIN 2: Security & Authorization (3 items)

**Agent expertise:** Grant system, scope resolution, authorization policies, role templates

**Files to read first (understand fully before any changes):**
- `Services/GrantService.cs` — especially `GetAccessibleCompanyIdsForGrantAsync`, `HasGrantAsync`
- `Pages/Api/Calendar/GetShiftsData.cshtml.cs` — shift data API
- `Pages/Api/Calendar/GetChoresData.cshtml.cs` — chores data API
- `Pages/Api/Calendar/GetOnCallData.cshtml.cs` — on-call data API
- `Pages/Api/Calendar/GetOverviewData.cshtml.cs` — overview data API
- `Services/ITenantResolver.cs` and `Services/TenantResolver.cs` — how CompanyId/MoleculeId is resolved
- `Pages/Admin/HomeTypes/Index.cshtml.cs` — check authorization attribute
- `Pages/Requests/Index.cshtml.cs` — check authorization attribute
- `Data/SeedData/GrantTypeSeed.cs` — grant definitions
- `Data/SeedData/RoleTemplateSeed.cs` — role→grant mappings
- `Models/AppUser.cs` — user model (CompanyId, MoleculeId chain)

### Task 2.1: Calendar API Molecule-Scope Validation (P1-1) -- DONE

**Files:**
- Modify: `Pages/Api/Calendar/GetShiftsData.cshtml.cs`
- Modify: `Pages/Api/Calendar/GetChoresData.cshtml.cs`
- Modify: `Pages/Api/Calendar/GetOnCallData.cshtml.cs`
- Modify: `Pages/Api/Calendar/GetOverviewData.cshtml.cs`

- [x] **Step 1: Understand the current scope resolution**

Read each endpoint's `OnGetAsync`. Understand:
- What parameters they accept (moleculeId, areaId, companyId)
- How they currently query data
- What the user's molecule/company chain looks like (User → Company → Molecule)

- [x] **Step 2: Determine the right validation approach**

The user's `CompanyId` claim → their Company → Company.MoleculeId. The user should only be able to query:
- GetShiftsData/GetChoresData: molecules their company belongs to (or molecules they have grants for)
- GetOnCallData: areas their molecule belongs to
- GetOverviewData: companies within their accessible scope

**IMPORTANT:** The endpoints already use `IScopeFilterService.ResolveCompanyIdsForScopeAsync()` for data resolution. Understand what this service already does before adding new scope logic — it may provide the needed scope resolution. The validation should check that the user's company is within the resolved scope, not duplicate the scope logic.

- [x] **Step 3: Add validation to GetShiftsData**

In `OnGetAsync`, after parsing `moleculeId`:
1. Get the user's CompanyId from claims (use `int.TryParse` per project convention)
2. Look up the user's Company.MoleculeId
3. If the requested `moleculeId` doesn't match the user's molecule (and user doesn't have cross-molecule grants via `IScopeFilterService`), return `Forbid()` or `new JsonResult(new { error = "Access denied" }) { StatusCode = 403 }`

- [x] **Step 4: Apply same pattern to GetChoresData**

Same molecule validation.

- [x] **Step 5: Apply same pattern to GetOnCallData**

For area validation: get user's Company → Molecule → Area. Validate the requested `areaId` matches.

- [x] **Step 6: Apply same pattern to GetOverviewData**

For company validation: validate the requested companyId is within the user's accessible companies.

- [x] **Step 7: Test with browser**

Login as Employee, open the Calendar/Shifts page. Inspect the AJAX call URL. Try manually changing the `moleculeId` parameter to a different molecule. Expected: 403 response.

- [x] **Step 8: Commit**

```bash
git add Pages/Api/Calendar/GetShiftsData.cshtml.cs Pages/Api/Calendar/GetChoresData.cshtml.cs Pages/Api/Calendar/GetOnCallData.cshtml.cs Pages/Api/Calendar/GetOverviewData.cshtml.cs
git commit -m "fix: add molecule/area scope validation to calendar API endpoints (P1-1)"
```

### Task 2.2: HomeTypes 403 for Owner (P2-14) -- DONE

**Files:**
- Modify: `Pages/Admin/HomeTypes/Index.cshtml.cs` (authorization attribute)
- Possibly modify: `Data/SeedData/RoleTemplateSeed.cs` or `Data/SeedData/GrantTypeSeed.cs`

- [x] **Step 1: Check the authorization policy**

Read `HomeTypes/Index.cshtml.cs`. Find the `[Authorize(Policy = "...")]` attribute. Identify which grant it requires.

- [x] **Step 2: Add the grant to the Owner role template**

The `ManageHomeTypes` grant exists in `GrantTypeSeed.cs` but is NOT assigned to ANY role template in `RoleTemplateSeed.cs`. Add it to the Owner template (and consider Manager/Director templates depending on who should manage home types). Follow the existing pattern for adding grant mappings — ALWAYS append at the end, never insert in the middle (IDs use sequential `id++`).

- [x] **Step 3: Test**

Login as Owner. Navigate to `/Admin/HomeTypes`. Expected: 200 OK.

- [x] **Step 4: Commit**

```bash
git add Pages/Admin/HomeTypes/Index.cshtml.cs Data/SeedData/RoleTemplateSeed.cs
git commit -m "fix: Owner role can access HomeTypes page (P2-14)"
```

### Task 2.3: Employee Can View Own Requests (P3-8) -- DONE

**Files:**
- Modify: `Pages/Requests/Index.cshtml.cs` (authorization or logic)
- Possibly modify: `Data/SeedData/RoleTemplateSeed.cs`

- [x] **Step 1: Check why Requests/Index is denied for Employee**

Read `Requests/Index.cshtml.cs`. Find the authorization policy. Determine if it requires a manager-level grant for viewing ALL requests, and whether employees should see their OWN requests on this page.

- [x] **Step 2: Fix access**

Option A: Add a "ViewOwnRequests" grant to the Employee role template.
Option B: Change the page to allow any authenticated user, but filter to show only their own requests unless they have the manager grant.

Choose the approach that matches the existing pattern in the codebase.

- [x] **Step 3: Test**

Login as Employee. Navigate to `/Requests/Index`. Expected: page loads, shows only the employee's own requests (if any).

- [x] **Step 4: Commit**

```bash
git add Pages/Requests/Index.cshtml.cs
git commit -m "fix: Employee can view own requests on Requests page (P3-8)"
```

---

## DOMAIN 3: Frontend & Responsive UX (10 items)

**Agent expertise:** CSS responsive design, JavaScript modules, widget behavior, mobile breakpoints, accessibility

**Files to read first (understand fully before any changes):**
- `wwwroot/css/site.css` — main styles
- `wwwroot/css/tokens.css` — design tokens
- `wwwroot/css/navigation.css` — sidebar/nav styles
- `wwwroot/css/widgets.css` — widget styles
- `wwwroot/css/calendar.css` — calendar styles
- `wwwroot/js/site.js` — main JS (includes sidebar, dark mode, widget init)
- `wwwroot/js/widget-persistence.js` — widget state persistence
- `wwwroot/js/calendar-realtime.js` — SignalR connection management
- `wwwroot/js/shift-swap-game.js` — game init (has debug logging)
- `wwwroot/js/mobile-nav.js` — mobile navigation
- `Pages/Shared/_Layout.cshtml` — layout template
- `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` — calendar table component
- `Pages/Calendar/Shifts.cshtml` — shifts calendar page

**IMPORTANT: Wait for Agent 4 to complete Task 4.2 (P1-5 _Layout async fix) before modifying _Layout.cshtml**

### Task 3.1: Quick Info Widget Auto-Collapse + Affordances (P2-1) -- DONE

**Files:**
- Modify: `wwwroot/js/site.js` or `wwwroot/js/widget-persistence.js` (auto-collapse logic)
- Modify: `wwwroot/css/widgets.css` (affordance styling)

- [x] **Step 1: Read widget initialization code**

Find how the Quick Info widget is initialized and how its state (expanded/collapsed) is managed. Check if `localStorage` is already used for widget state.

- [x] **Step 2: Implement auto-collapse after first view**

On page load:
- Check `localStorage` for key `quickInfoSeen`
- If not set: show widget expanded, set `localStorage.quickInfoSeen = 'true'`
- If already set: show widget collapsed by default
- When user manually collapses/expands, remember that preference

- [x] **Step 3: Improve visual affordances**

Make the drag handle and collapse button more visually obvious:
- Larger grip indicator with hover effect
- Visible "minimize" icon/button with tooltip
- Subtle border or shadow to indicate interactivity

- [x] **Step 4: Test across pages**

Navigate to multiple pages. Verify:
- First visit: widget expanded
- After collapse: stays collapsed on navigation
- Clear localStorage, refresh: widget expanded again

- [x] **Step 5: Commit**

```bash
git add wwwroot/js/site.js wwwroot/css/widgets.css
git commit -m "fix: Quick Info widget auto-collapses after first view, improved affordances (P2-1)"
```

### Task 3.2: Debug Console Cleanup (P2-2) -- DONE

**Files:**
- Modify: Multiple JS files (search for `console.log`)

- [ ] **Step 1: Audit all console.log calls**

Search ALL JS files for `console.log`, including `calendar-inline-edit.js` and `shift-swap-game.js` (which has emoji-prefixed match checking logs at lines 612-644). Categorize each as:
- **REMOVE:** Init-spam (ReducedMotion, DateFormat, FormValidation, PartialData, ModalLoader, CacheManager, Localization counts, Sidebar CSS check, "PHASE 19 + SUPER-MERGE", "🐛 DEBUG MODE", game match-checking debug logs)
- **KEEP:** Meaningful status (SignalR connected/joined, CSRF initialized, session check, Easter egg, theme switched)

- [ ] **Step 2: Remove init-spam logs**

Delete or comment out the REMOVE-category console.log calls. Do NOT remove the KEEP-category ones.

- [ ] **Step 3: Add cool event logs for major events**

Add tasteful console.log for events like:
- Shift assigned/unassigned
- Theme switched
- Language changed
- Calendar view changed

Use consistent format: `console.log('[Shifty] Event description');`

- [ ] **Step 4: Test**

Open any page, check console. Should see only meaningful logs, not init-spam.

- [ ] **Step 5: Commit**

```bash
git add wwwroot/js/*.js
git commit -m "fix: clean up console output - remove init spam, keep meaningful logs (P2-2)"
```

### Task 3.3: Mobile Header Truncation (P2-13) -- DONE

**Files:**
- Modify: `wwwroot/css/site.css` (responsive header styles)

- [x] **Step 1: Find the header h1 styles**

Search for the header/banner h1 styling. Find what causes truncation at small viewports (likely `max-width`, `overflow: hidden`, `text-overflow: ellipsis`).

- [x] **Step 2: Add responsive rule**

Add a media query for small viewports (`max-width: 576px`) that either:
- Reduces font size to fit
- Removes max-width constraint
- Allows wrapping

- [x] **Step 3: Test at 375px viewport**

Verify "Home", "Shifts Calendar", "Admin Hub" all show fully without truncation.

- [x] **Step 4: Commit**

```bash
git add wwwroot/css/site.css
git commit -m "fix: prevent header title truncation on mobile viewports (P2-13)"
```

### Task 3.4: Sidebar Labels Hidden in Collapsed State (P3-1) -- DONE

**Files:**
- Modify: `wwwroot/css/navigation.css` or `wwwroot/css/site.css`

- [x] **Step 1: Find collapsed sidebar styles**

Find the CSS class applied when sidebar is collapsed. Identify why partial text ("MY SHI...") shows instead of being hidden.

- [x] **Step 2: Hide text labels in collapsed state**

When sidebar is collapsed, add `visibility: hidden` or `display: none` to the text labels, keeping only icons visible. Or set `width: 0; overflow: hidden` on the text span.

- [x] **Step 3: Test in both LTR and RTL**

Collapse sidebar in English (LTR) and Hebrew (RTL). Verify icons show cleanly without text fragments.

- [x] **Step 4: Commit**

```bash
git add wwwroot/css/navigation.css
git commit -m "fix: hide sidebar text labels in collapsed state, show icons only (P3-1)"
```

### Task 3.5: Remove Deprecated Meta Tag (P4-4) -- DONE

**Files:**
- Modify: `Pages/Shared/_Layout.cshtml`

**PREREQUISITE: Agent 4 must complete Task 4.2 first**

- [x] **Step 1: Find and remove the deprecated meta tag**

Search for `apple-mobile-web-app-capable` in `_Layout.cshtml`. Remove the entire `<meta name="apple-mobile-web-app-capable" content="yes">` line.

- [x] **Step 2: Test**

Load any page. Verify no more deprecation warning in console.

- [x] **Step 3: Commit**

```bash
git add Pages/Shared/_Layout.cshtml
git commit -m "fix: remove deprecated apple-mobile-web-app-capable meta tag (P4-4)"
```

### Task 3.6: SignalR Connection Warnings (P4-5) -- DONE

**Files:**
- Modify: `wwwroot/js/calendar-realtime.js`

- [x] **Step 1: Investigate the warning source**

Read `calendar-realtime.js`. Find where "Connection failed" is logged. Determine if it's fired during intentional page navigation (connection teardown) or unexpected disconnection.

- [x] **Step 2: Fix based on root cause**

If it's during intentional navigation: add a `isDisposing` flag set during `dispose()`, and suppress the warning when `isDisposing` is true.

If it's an actual reconnection issue: fix the reconnection logic.

- [x] **Step 3: Test**

Navigate between calendar pages. Verify no warning in console during normal navigation. Verify reconnection still works after network blip (if applicable).

- [x] **Step 4: Commit**

```bash
git add wwwroot/js/calendar-realtime.js
git commit -m "fix: suppress SignalR connection warning during intentional page navigation (P4-5)"
```

### Task 3.7: Missing Autocomplete Attributes (P4-8) -- DONE

**Files:**
- Modify: `Pages/Admin/Users.cshtml` (or the relevant page with missing autocomplete)

- [x] **Step 1: Find the 5 input elements**

The inputs are on Admin/Users page (email, display name, password fields in the Add User form and inline password set).

- [x] **Step 2: Add appropriate autocomplete attributes**

- Email input: `autocomplete="email"`
- Display name: `autocomplete="name"`
- Password inputs: `autocomplete="new-password"`

- [x] **Step 3: Commit**

```bash
git add Pages/Admin/Users.cshtml
git commit -m "fix: add autocomplete attributes to Admin/Users form inputs (P4-8)"
```

### Task 3.8: Calendar Filters Horizontal Compact on Mobile (M-2) -- DONE

**Files:**
- Modify: `wwwroot/css/calendar.css` or `wwwroot/css/site.css`
- Possibly modify: `Pages/Calendar/Shifts.cshtml` (filter layout)

- [ ] **Step 1: Analyze current filter layout**

Read the Shifts calendar page filter section. Find the CSS that causes filters to stack vertically on mobile.

- [ ] **Step 2: Create horizontal compact layout for mobile**

Add media query for small viewports. Make filters display in a compact horizontal row:
- Molecule and Job Type as smaller inline dropdowns
- View mode and date navigation on a second row
- Mode toggles (By Shift/By User) as small pills

- [ ] **Step 3: Test at 375px**

Verify calendar data is visible without excessive scrolling. Filters should take minimal vertical space.

- [ ] **Step 4: Test at 768px and 1280px**

Verify no regression at tablet and desktop sizes.

- [ ] **Step 5: Commit**

```bash
git add wwwroot/css/calendar.css
git commit -m "fix: horizontal compact calendar filters on mobile viewports (M-2)"
```

### Task 3.9: Breadcrumb Truncation (M-3) -- DONE

**Files:**
- Modify: `wwwroot/css/site.css` (breadcrumb styles)

- [x] **Step 1: Find breadcrumb truncation CSS**

Search for breadcrumb styles. Find what causes "Shifts Calen..." truncation.

- [x] **Step 2: Fix for small viewports**

Either allow wrapping, reduce font size, or use shorter text at small breakpoints. Ensure breadcrumb remains on one line if possible.

- [x] **Step 3: Test at 375px and 768px**

Verify breadcrumbs are readable at both sizes.

- [x] **Step 4: Commit**

```bash
git add wwwroot/css/site.css
git commit -m "fix: prevent breadcrumb truncation on small viewports (M-3)"
```

### Task 3.10: Combine and Final Test

- [ ] **Step 1: Full responsive test**

Test at 375px, 768px, 1280px:
- Home page
- Calendar/Shifts
- Admin/Index
- Owner/Hub

Verify no regressions in any viewport.

- [ ] **Step 2: Test RTL at all viewports**

Switch to Hebrew. Verify sidebar, header, breadcrumbs, calendar all work in RTL at all sizes.

- [ ] **Step 3: Test dark mode**

Verify all fixes work in dark mode too.

---

## DOMAIN 4: Backend & Middleware (6 items)

**Agent expertise:** ASP.NET Core middleware pipeline, async/await patterns, Razor Pages, EF Core, SignalR

**Files to read first (understand fully before any changes):**
- `Program.cs` — full middleware pipeline section (lines 1350-1450)
- `Pages/Shared/_Layout.cshtml` — line 45 (.Result call)
- `Services/ViewAsModeService.cs` — the async method being called with .Result
- `Pages/Owner/Hub/Index.cshtml.cs` — feature flag count
- `Services/FeatureFlagService.cs` — how flags are queried
- `Pages/Api/Hierarchy/Create.cshtml.cs` — hierarchy creation
- `Pages/Calendar/Shifts.cshtml` — shift calendar (for AJAX conversion)
- `Pages/Calendar/Shifts.cshtml.cs` — shift calendar page model
- `wwwroot/js/calendar-bottom-sheet.js` — assignment dialog JS
- `Hubs/CalendarHub.cs` — SignalR hub
- Company display patterns across pages (how company names appear in dropdowns)

### Task 4.1: HTTPS Redirect with Griffin Exclusion (P0-1) -- DONE

**Files:**
- Modify: `Program.cs` (middleware section)

- [x] **Step 1: Find the current HTTPS redirect setup**

Read `Program.cs` around line 1376. Find the `UseHttpsRedirection()` call and the `enableHttps` configuration.

- [x] **Step 2: Replace with custom middleware**

Replace `app.UseHttpsRedirection()` with:
```csharp
if (enableHttps)
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps
            && !context.Request.Path.StartsWithSegments("/Auth/GriffinCallback"))
        {
            var host = context.Request.Host;
            var url = $"https://{host}{context.Request.Path}{context.Request.QueryString}";
            context.Response.Redirect(url, permanent: false);
            return;
        }
        await next();
    });
}
```

- [x] **Step 3: Add a comment explaining the Griffin exclusion**

Add a comment explaining why `/Auth/GriffinCallback` is excluded and that this is self-resolving when Griffin migrates to HTTPS.

- [x] **Step 4: Test**

Verify HTTPS redirect still works for normal pages. Verify `/Auth/GriffinCallback` is excluded.

- [x] **Step 5: Commit**

```bash
git add Program.cs
git commit -m "fix: HTTPS redirect excludes Griffin callback path for HTTP ADFS compatibility (P0-1)"
```

### Task 4.2: _Layout.cshtml Async Fix (P1-5) -- DONE

**Files:**
- Modify: `Pages/Shared/_Layout.cshtml`
- Possibly modify: `ViewComponents/` or create a new ViewComponent

**CRITICAL: This must complete before Agent 3 touches _Layout.cshtml**

- [x] **Step 1: Read the current .Result call**

Read `_Layout.cshtml` line 45. Understand what `ViewAsModeService.GetViewAsCompanyNameAsync().Result` does and where its result is used in the layout.

- [x] **Step 2: Read ViewAsModeService.GetViewAsCompanyNameAsync**

Understand what this method does — does it hit the DB? How expensive is it?

- [x] **Step 3: Choose the safest async approach**

Options:
- A: Move to a ViewComponent (cleanest — ViewComponents support async natively)
- B: Use `@await` in the Razor view (requires the view to be async-capable)
- C: Pre-resolve in a middleware and store in `HttpContext.Items`

Choose the approach that matches existing patterns in the codebase. Check if other layout-level data is resolved via ViewComponents or middleware.

- [x] **Step 4: Implement the fix**

Apply the chosen approach. Ensure the company name is still available where it was used in the layout.

- [x] **Step 5: Test thoroughly**

Test all pages — every page uses _Layout. Verify:
- ViewAs mode works for directors
- Normal users see correct company name
- No deadlocks under rapid page loads
- Both English and Hebrew render correctly

- [x] **Step 6: Commit**

```bash
git add Pages/Shared/_Layout.cshtml
git commit -m "fix: replace .Result with async pattern in _Layout to prevent IIS deadlock (P1-5)"
```

### Task 4.3: Owner Hub Feature Flag Count (P2-5) -- DONE

**Files:**
- Modify: `Pages/Owner/Hub/Index.cshtml.cs`

- [x] **Step 1: Read the current flag count query**

Find where "flags on" count is calculated. Understand why it returns 0 when 55 flags are enabled.

- [x] **Step 2: Fix the query**

The current code queries the `Configs` table (`DbSet<AppConfig>`) for keys starting with `"Feature:"`, but feature flags are stored in the `FeatureFlags` table (`DbSet<FeatureFlag>`). These are DIFFERENT tables. First understand the relationship:
- `FeatureFlags` table = seed definitions with `IsEnabled` boolean
- `Configs` table = runtime key-value config (may or may not have Feature: entries)

Fix the count to query the correct table (`FeatureFlags`) and count where `IsEnabled == true`.

- [x] **Step 3: Test**

Navigate to Owner Hub. Verify the Settings card shows the correct number of enabled flags (should be 55 or similar).

- [x] **Step 4: Commit**

```bash
git add Pages/Owner/Hub/Index.cshtml.cs
git commit -m "fix: Owner Hub Settings card shows correct feature flag count (P2-5)"
```

### Task 4.4: Duplicate Company Names with Molecule (P3-6) -- DONE

**Files:**
- Modify: `ViewComponents/OwnerCompanySelectorViewComponent.cs`

- [x] **Step 1: Find how company dropdowns are populated**

Search for where company names appear in `<select>` dropdowns. Find the service or page model method that loads company lists. Check if there's a shared service or if each page loads companies independently.

- [x] **Step 2: Modify display name generation**

When loading companies for dropdowns, for companies named "כלל צוותי", append the molecule name: "כלל צוותי - {MoleculeName}". This could be done at the service level (affecting all dropdowns) or the display level.

- [x] **Step 3: Test**

Navigate to Owner Backup page. Verify the company dropdown shows "כלל צוותי - אורן", "כלל צוותי - גפן", etc. instead of multiple identical "כלל צוותי" entries.

- [x] **Step 4: Commit**

```bash
git add [affected files]
git commit -m "fix: disambiguate duplicate company names with molecule prefix (P3-6)"
```

### Task 4.5: Calendar Assignment In-Place Update (P3-7) -- DONE

**Files:**
- Modify: `wwwroot/js/calendar-inline-edit.js` (PRIMARY — contains the `fetch()` + `location.reload()` logic)
- Modify: `wwwroot/js/calendar-bottom-sheet.js` (calls `window.quickAddShift()` defined in inline-edit)
- Possibly modify: `wwwroot/js/calendar-realtime.js` (SignalR update handler)

**CRITICAL: Test extensively after this change. This is the most complex fix.**

**NOTE:** All assignment handlers now use `triggerCalendarRefresh()` which calls `CalendarRealtime.refresh()` for in-place AJAX DOM updates. The only remaining `location.reload()` is in the fallback path of `triggerCalendarRefresh()` for pages without CalendarRealtime (does not apply to calendar pages).

- [x] **Step 1: Read calendar-inline-edit.js thoroughly**

Understand:
- `quickAddShift()` function — the AJAX POST to `/Calendar/Table?handler=AssignEmployee`
- What the server response contains
- Where `location.reload()` is called (lines ~351, ~491, ~542, ~570)
- How the calendar DOM is structured (row IDs, cell structure)

- [x] **Step 2: Read calendar-realtime.js**

Understand the `AssignmentChanged` SignalR event handler. This already handles in-place updates from OTHER clients. We need to reuse this same DOM update logic for the assigning client's own response.

- [x] **Step 3: Replace location.reload() with in-place DOM update for shifts**

In `quickAddShift()` success handler (around line 351):
1. Parse the server response for assignment data
2. Update the calendar cell DOM to show the new assignment (add chip/name)
3. Remove `setTimeout(() => location.reload(), 500)`
4. Show a brief success toast instead

- [x] **Step 4: Apply same pattern to chore and on-duty assignments**

Replace `location.reload()` in chore assignment (~line 491) and on-duty assignment (~line 570) with similar in-place DOM updates.

- [x] **Step 5: Deduplicate with SignalR handler**

Ensure the assigning client doesn't get a double update (AJAX response + SignalR echo). Either:
- Mark the assignment as "own action" and skip the SignalR echo
- Or let SignalR handle ALL updates and just close the dialog on AJAX success

- [x] **Step 6: Test extensively**

Test ALL of these scenarios:
- Assign a user (shift) → cell updates in-place
- Assign a user (chore) → cell updates in-place
- Assign a user (on-duty) → cell updates in-place
- Assign same user twice → error toast
- Assign user with conflict → warning/error handled
- Two browser tabs → second tab gets SignalR update
- Unassign a user → cell updates
- Switch between shift/user mode → assignments persist
- Page refresh → assignments still there
- Hebrew RTL mode → assignment dialog works

- [x] **Step 7: Commit**

```bash
git add wwwroot/js/calendar-inline-edit.js wwwroot/js/calendar-bottom-sheet.js
git commit -m "fix: calendar assignment updates in-place instead of page reload (P3-7)"
```

### Task 4.6: Hierarchy Duplicate Returns 409 (P3-17) -- DONE

**Files:**
- Modify: `Pages/Api/Hierarchy/Create.cshtml.cs`

- [x] **Step 1: Read the current error handling**

Find the catch block that handles database exceptions during entity creation.

- [x] **Step 2: Catch unique constraint violation specifically**

Catch `Microsoft.Data.Sqlite.SqliteException` and check for unique constraint error code (SQLITE_CONSTRAINT_UNIQUE = 2067 or 19). Return a 409 Conflict with a user-friendly message like "An item with this name already exists."

- [x] **Step 3: Test**

Try creating a hierarchy entity with a duplicate name. Expected: 409 with clear message instead of 500.

- [x] **Step 4: Commit**

```bash
git add Pages/Api/Hierarchy/Create.cshtml.cs
git commit -m "fix: hierarchy create returns 409 for duplicate names instead of 500 (P3-17)"
```

---

## DOMAIN 5: Content, Localization & Branding (5 items)

**Agent expertise:** .resx resource files, localization patterns, cross-file text consistency

**Files to read first (understand fully before any changes):**
- `Resources/SharedResources.resx` — English resources
- `Resources/SharedResources.he-IL.resx` — Hebrew resources
- `Pages/Admin/Index.cshtml` — Admin Hub (jargon text)
- `Pages/Admin/Users.cshtml` — User Management (typo)
- `Pages/Shared/_Layout.cshtml` — branding references
- `Pages/Auth/Login.cshtml` — branding
- `Pages/Auth/Signup.cshtml` — branding
- `Pages/Error.cshtml` — branding
- `Pages/AccessDenied.cshtml` — branding
- `Pages/StatusCode.cshtml` — branding
- `appsettings.Production.template.json` — Linux paths

### Task 5.1: Standardize Branding to "Shifty" (P2-3) -- DONE

**Files:**
- Modify: Multiple `.cshtml` files, `.resx` files, possibly JS files

- [x] **Step 1: Search for all branding references**

Search entire codebase for: "Shift Manager", "ShiftManager" (in user-visible text), "shifty" (lowercase in titles). Do NOT change namespace/class names — only user-visible text.

- [x] **Step 2: Replace in English .resx**

Change all instances of "Shift Manager" to "Shifty" in `SharedResources.resx` (English values for AppName, page titles, etc.)

- [x] **Step 3: Fix Hebrew .resx too**

Verify `SharedResources.he-IL.resx` uses "מנהל משמרות" for the app name. Note: there are ~4 instances of "Shift Manager" in the Hebrew .resx (in email templates and logout messages at lines ~4352, 4357, 4363, 6810) — update these to "Shifty" as well.

- [x] **Step 4: Fix page titles and headers**

Update Login page, AccessDenied, Error page, StatusCode page to use the localized app name from .resx instead of hardcoded "Shift Manager".

- [x] **Step 5: Fix page `<title>` generation**

If page titles use a format like "Page - Shift Manager", update to "Page - Shifty".

- [x] **Step 6: Test both languages**

Verify:
- English: "Shifty" appears consistently
- Hebrew: "מנהל משמרות" appears consistently
- No instances of "Shift Manager" remain in English UI

- [x] **Step 7: Commit**

```bash
git add Resources/*.resx Pages/**/*.cshtml
git commit -m "fix: standardize branding to 'Shifty' (EN) / 'מנהל משמרות' (HE) (P2-3)"
```

### Task 5.2: Developer Jargon Text (P3-2) -- DONE

**Files:**
- Modify: `Pages/Admin/Index.cshtml`

- [x] **Step 1: Find the jargon text**

Locate "RestHours, WeeklyCap, OnDuty types" in the Company Settings card description.

- [x] **Step 2: Replace with user-friendly text**

Change to "Rest periods, weekly hour limits, duty types" (or localize via .resx key).

- [x] **Step 3: Commit**

```bash
git add Pages/Admin/Index.cshtml
git commit -m "fix: replace developer jargon in Company Settings description (P3-2)"
```

### Task 5.3: "Mapotz" → "Team Leader" (P3-3) -- DONE

**Files:**
- Modify: `Resources/SharedResources.resx`

- [x] **Step 1: Find the Mapotz key**

Search .resx for "Mapotz". Find the key and its current English value.

- [x] **Step 2: Change English value to "Team Leader"**

Update the English value from "Mapotz" to "Team Leader".

- [x] **Step 3: Check for other untranslated role template names**

Search for other Hebrew transliterations in the .resx that should have English equivalents. Fix any found.

- [x] **Step 4: Commit**

```bash
git add Resources/SharedResources.resx
git commit -m "fix: translate 'Mapotz' to 'Team Leader' in English resources (P3-3)"
```

### Task 5.4: Linux Paths in Production Template (P3-12) -- DONE

**Files:**
- Modify: `appsettings.Production.template.json`

- [x] **Step 1: Replace Linux paths with Windows paths**

Change `"Data Source=/var/lib/shiftmanager/production.db"` to `"Data Source=C:\\ShiftManager\\Data\\app.db"`.

- [x] **Step 2: Annotate Docker/K8s sections**

Add comment or note that Docker/Kubernetes sections are not applicable for Windows/IIS deployment.

- [x] **Step 3: Commit**

```bash
git add appsettings.Production.template.json
git commit -m "fix: production template uses Windows paths, annotate Docker sections (P3-12)"
```

### Task 5.5: Double Colon Typo (P4-1) -- DONE

**Files:**
- Modify: `Pages/Admin/Users.cshtml`

**NOTE:** This may already be fixed. The file is modified in the working tree. Verify first.

- [x] **Step 1: Search for the typo**

Search `Pages/Admin/Users.cshtml` for "permanently remove::" or similar double-punctuation. If NOT found, skip this task — it's already fixed.

- [x] **Step 2: Fix if found**

Change "permanently remove::" to "permanently remove:".

- [x] **Step 3: Commit (only if change was needed)**

```bash
git add Pages/Admin/Users.cshtml
git commit -m "fix: double colon typo in user deletion warning (P4-1)"
```

---

## DOMAIN 6: New Feature — Export User Data (1 item)

**Agent expertise:** Data export patterns, user model relationships, GDPR-style data compilation

**Files to read first (understand fully before any changes):**
- `Pages/Owner/Hub/Index.cshtml` — Owner Hub layout (for pattern matching)
- `Pages/Owner/Hub/ExportUserData.cshtml` — check if file exists (currently 404)
- `Models/AppUser.cs` — user model and relationships
- `Models/ShiftAssignment.cs` — shift assignments
- `Models/Chore.cs` — chore assignments
- `Models/OnDuty.cs` — on-duty assignments
- `Services/UserDataExportService.cs` — check if exists
- `Pages/Admin/Users.cshtml.cs` — existing CSV export (for pattern)
- `Data/AppDbContext.cs` — entity relationships

### Task 6.1: Enhance ExportUserData Page with Batch Support and Selection UI -- DONE

**NOTE:** The page ALREADY EXISTS at `Pages/Owner/Hub/ExportUserData.cshtml` and `.cshtml.cs`. The current implementation has a single-user JSON export via GET handler with `{userId:int}` route parameter. This task ENHANCES it with a multi-user selection UI and batch export.

**Files:**
- Modify: `Pages/Owner/Hub/ExportUserData.cshtml` (add selection UI)
- Modify: `Pages/Owner/Hub/ExportUserData.cshtml.cs` (add batch handler)
- Read: `Services/UserDataExportService.cs` (existing export service)

- [x] **Step 1: Read existing implementation**

Read both files thoroughly. Understand:
- The current route pattern (`@page "{userId:int}"`)
- The `UserDataExportService` — what data it already exports
- The authorization policy used (`Grant:AdminAccess`)

- [x] **Step 2: Add index route (no userId)**

Modify the `@page` directive to support both `"{userId:int}"` (existing single-user) and no parameter (new selection page). Add a GET handler that, when no userId is provided, renders a user selection form.

- [x] **Step 3: Create the selection UI**

Add to the view:
- User list with checkboxes (loaded from DB, show name + email + company)
- Search/filter input
- "Select All" / "Deselect All" buttons
- Export format selector (JSON / CSV)
- "Export" button

- [x] **Step 4: Add batch POST handler**

Add `OnPostExportBatchAsync` that:
- Accepts array of selected userIds
- Calls `UserDataExportService` for each user
- Bundles results into a single JSON array or multi-sheet CSV
- Returns as file download

- [x] **Step 5: Test**

- Navigate to `/Owner/Hub/ExportUserData` (no userId) — should show selection UI
- Select multiple users, export as JSON — should download complete data
- Navigate to `/Owner/Hub/ExportUserData/8` — existing single-user export still works
- Verify Quick Link from Owner Hub works (no more 404)

- [x] **Step 6: Commit**

```bash
git add Pages/Owner/Hub/ExportUserData.cshtml Pages/Owner/Hub/ExportUserData.cshtml.cs
git commit -m "feat: enhance Export User Data with batch selection UI and multi-user export (P4-6)"
```

---

## Execution Order & Dependencies

```
Parallel Group 1 (no dependencies):
  Agent 1: Database & Infrastructure (Tasks 1.1-1.10)
  Agent 2: Security & Authorization (Tasks 2.1-2.3)
  Agent 5: Content & Branding (Tasks 5.1-5.5)
  Agent 6: Export Feature (Task 6.1)

Sequential:
  Agent 4: Backend & Middleware (Tasks 4.1-4.6)
    ↓ (after Task 4.2 completes)
  Agent 3: Frontend & UX (Tasks 3.1-3.10)

Note: Agent 3 can start Tasks 3.1-3.4, 3.6-3.9 immediately.
Only Task 3.5 (meta tag removal in _Layout) requires Agent 4's Task 4.2 to complete first.
```

## Post-Implementation Verification

After all agents complete:

- [ ] Run full test suite: `dotnet test` — expect 363+ tests passing
- [ ] Run the app and verify each fix in browser
- [ ] Test with Owner, Manager, Employee roles
- [ ] Test in both English and Hebrew
- [ ] Test in both light and dark mode
- [ ] Test at 375px, 768px, and 1280px viewports
- [ ] Check console for clean output (no init-spam)
- [ ] Navigate to Owner Hub — verify flag count, backup page, export link
- [ ] Verify calendar assignment works without page reload
