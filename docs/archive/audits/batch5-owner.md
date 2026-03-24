# Batch 5 - Owner Pages Audit

**Audited:** 2026-03-03
**Pages audited:** 29 (Pages/Owner/* and Pages/Owner/Hub/*)

---

## 1. Pages/Owner/Index (Owner Dashboard / Legacy Panel)

### 1.1 Identity & Routing
- **File:** `Pages/Owner/Index.cshtml` + `Pages/Owner/Index.cshtml.cs`
- **Route:** `/Owner/Index` (default `@page`)
- **Namespace:** `ShiftManager.Pages.Owner`
- **Class:** `IndexModel : PageModel`
- **Purpose:** Owner dashboard landing page; redirects to `/Owner/Hub/Index` by default. Legacy panel accessible via `?handler=Legacy`.

### 1.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (uses `IgnoreQueryFilters()` for user count)
- **SECURITY-AUDITED comment:** Yes
- **Tenant isolation:** Bypassed intentionally for global stats

### 1.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Tag helpers:** `<loc key="..." />`
- **Title:** `Localizer["Owner_AdminPanel"]`
- **Keys used:** `Owner_AdminPanel`, `Owner_TotalUsers`, `Owner_TotalCompanies`, `Owner_DatabaseSize`, `Owner_Uptime`, various link labels

### 1.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Stats cards:** TotalUsers, TotalCompanies, DatabaseSize, Uptime
- **Recent activities:** List of latest audit log entries
- **Navigation links:** All Owner pages listed as card links
- **CSS:** Inline `<style>` block, card-based layout with CSS variables

### 1.5 Navigation Map
- **Parent:** Breadcrumb root
- **Children:** Links to all Owner pages (Backup, DatabaseConsole, EmailConfig, FeatureFlags, etc.)
- **Legacy redirect:** GET handler `Legacy` renders the old dashboard view
- **Default redirect:** To `/Owner/Hub/Index`

### 1.6 Data Dependencies & Side Effects
- **Read:** `AppDbContext` (Users count via IgnoreQueryFilters, Companies count, AuditLogs, database file size, process uptime)
- **Write:** None (read-only dashboard)
- **Services:** None (direct DB access)

### 1.7 Forms & Submissions
- No POST handlers (read-only page)

### 1.8 Interesting Behaviors
- Calculates database size by reading `app.db` file info directly
- Computes uptime from `Process.GetCurrentProcess().StartTime`
- Legacy handler preserves backward compatibility

### 1.9 Traceability
- No audit logging on GET (display-only)

---

## 2. Pages/Owner/AreaConfig

### 2.1 Identity & Routing
- **File:** `Pages/Owner/AreaConfig.cshtml` + `Pages/Owner/AreaConfig.cshtml.cs`
- **Route:** `/Owner/AreaConfig`
- **Class:** `AreaConfigModel : PageModel`
- **Purpose:** Configure area-level settings (rest hours, weekly hour cap)

### 2.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (uses `IgnoreQueryFilters()` to load all areas)
- **SECURITY-AUDITED comment:** Yes

### 2.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `Owner_AreaConfig`, `AreaConfig_*` family

### 2.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb, OwnerCompanySelector
- **Design:** Cards per area with settings display, modal for editing
- **CSS:** Inline styles with CSS variables

### 2.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Links:** Back to `/Owner/Index`

### 2.6 Data Dependencies & Side Effects
- **Read:** Areas (IgnoreQueryFilters), AreaSettings
- **Write:** Creates/updates `AreaSettings` entities
- **Services:** `IAuditLogService`, `ITenantResolver`

### 2.7 Forms & Submissions
| Handler | Method | Parameters | Validation |
|---------|--------|------------|------------|
| `OnPostUpdateSettingsAsync` | POST | areaId, restHours (0-24), weeklyHourCap (0-168) | Range validation |

### 2.8 Interesting Behaviors
- Creates `AreaSettings` on first save if it does not already exist (upsert pattern)
- Settings apply globally across tenants for a given area

### 2.9 Traceability
- Audit logs via `IAuditLogService`

---

## 3. Pages/Owner/Backup

### 3.1 Identity & Routing
- **File:** `Pages/Owner/Backup.cshtml` + `Pages/Owner/Backup.cshtml.cs`
- **Route:** `/Owner/Backup`
- **Class:** `BackupModel : PageModel`
- **Purpose:** Database backup, restore, download, and delete

### 3.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** System-wide (operates on `app.db` file directly)

### 3.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `Backup_*` family

### 3.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb, OwnerCompanySelector
- **Design:** Backup list table, action buttons (Create, Download, Restore, Delete)
- **CSS:** Inline styles

### 3.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Links:** Back to `/Owner/Index`

### 3.6 Data Dependencies & Side Effects
- **Read:** File system (`backups/` directory listing)
- **Write:** File copy (`app.db` to backup), file delete, file restore (copy backup over `app.db`)
- **Services:** `IAuditLogService`

### 3.7 Forms & Submissions
| Handler | Method | Parameters | Notes |
|---------|--------|------------|-------|
| `OnPostCreateAsync` | POST | - | Copies `app.db` to timestamped backup |
| `OnGetDownloadAsync` | GET | fileName | Downloads backup file |
| `OnPostRestoreAsync` | POST | fileName | Pre-creates safety backup, then copies backup over `app.db` |
| `OnPostDeleteAsync` | POST | fileName | Deletes backup file |

### 3.8 Interesting Behaviors
- **Path traversal protection:** `GetValidatedBackupPath()` validates filename contains no `..` or path separators
- Pre-restore backup created automatically before overwrite
- All operations audit-logged with timestamps

### 3.9 Traceability
- Full audit logging for all backup operations (create, restore, delete)

---

## 4. Pages/Owner/Blueprints

### 4.1 Identity & Routing
- **File:** `Pages/Owner/Blueprints.cshtml` + `Pages/Owner/Blueprints.cshtml.cs`
- **Route:** `/Owner/Blueprints`
- **Class:** `BlueprintsModel : PageModel`
- **Purpose:** Manage ShiftTypes (shift definitions) with bilingual names

### 4.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:ManagerHomeAccess")]` (expanded from Owner-only per P1-1)
- **Scope:** Tenant-scoped (uses `_tenantResolver.GetCurrentTenantId()`)
- **No IgnoreQueryFilters:** Tenant-safe queries

### 4.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`, `ICompanyLocalizationService`
- **Keys:** `Blueprints_*`, `ShiftType_*` families
- **Bilingual names:** NameKey system for EN/HE

### 4.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb, OwnerCompanySelector
- **Modals:** Edit Name, Edit Time, Delete (with async usage check), Publish to Molecule
- **CSS:** Inline styles, card-based grid

### 4.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Links:** Back to `/Owner/Index`

### 4.6 Data Dependencies & Side Effects
- **Read:** ShiftTypes (tenant-scoped), Molecules, JobTypes
- **Write:** ShiftType CRUD, publish/unpublish to molecule
- **Services:** `IShiftTypeCacheService` (invalidation), `IConcurrencyService`, `ICompanyLocalizationService`, `IAuditLogService`, `IJobTypeService`

### 4.7 Forms & Submissions
| Handler | Method | Notes |
|---------|--------|-------|
| `OnPostCreateAsync` | POST | Create new ShiftType |
| `OnPostUpdateNameAsync` | POST | Update bilingual name |
| `OnPostUpdateTimeAsync` | POST | Update start/end time |
| `OnPostDeleteAsync` | POST | Delete with usage check |
| `OnPostPublishAsync` | POST | Publish to molecule |
| `OnPostUnpublishAsync` | POST | Unpublish from molecule |
| `OnGetCheckUsageAsync` | GET/AJAX | Check ShiftType usage count |
| `OnPostMigrateNameKeysAsync` | POST | Migration helper for NameKey population |

### 4.8 Interesting Behaviors
- Concurrency handling via `IConcurrencyService.SaveWithConcurrencyHandlingAsync()`
- Cache invalidation after every write operation
- Migration helper for populating NameKey on existing records
- Usage check before delete to warn user of dependent records

### 4.9 Traceability
- Audit logging for all CRUD operations

---

## 5. Pages/Owner/ClearCompanySelection

### 5.1 Identity & Routing
- **File:** `Pages/Owner/ClearCompanySelection.cshtml` + `Pages/Owner/ClearCompanySelection.cshtml.cs`
- **Route:** `/Owner/ClearCompanySelection`
- **Class:** `ClearCompanySelectionModel : PageModel`
- **Purpose:** POST-only endpoint to clear owner's company selection cookie

### 5.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`

### 5.3 Localization
- Not applicable (POST-only redirect)

### 5.4 UI & Design Inventory
- No rendered UI (POST handler only, redirects)

### 5.5 Navigation Map
- **Redirect:** To `returnUrl` or `/Owner/Index`

### 5.6 Data Dependencies & Side Effects
- **Write:** Clears company selection cookie via `IOwnerCompanySelectorService.ClearSelectionAsync()`
- **Services:** `IOwnerCompanySelectorService`

### 5.7 Forms & Submissions
| Handler | Method | Parameters |
|---------|--------|------------|
| `OnPostAsync` | POST | returnUrl (optional) |

### 5.8 Interesting Behaviors
- POST-only with redirect (no GET rendering)

### 5.9 Traceability
- No audit logging (cookie clear only)

---

## 6. Pages/Owner/DataLifecycle

### 6.1 Identity & Routing
- **File:** `Pages/Owner/DataLifecycle.cshtml` + `Pages/Owner/DataLifecycle.cshtml.cs`
- **Route:** `/Owner/DataLifecycle`
- **Class:** `DataLifecycleModel : LocalizedPageModel`
- **Purpose:** Archive, purge, and re-import historical data

### 6.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:SystemConfiguration")]`
- **Scope:** System-wide

### 6.3 Localization
- **Base class:** `LocalizedPageModel` (provides `_localizer`, `Success`, `Error`)
- **Keys:** `DataLifecycle_*` family

### 6.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Three tabs:** Archive, Purge, Import
- **Archive tab:** Preview, Create, Download (CSV/NDJSON)
- **Purge tab:** Destructive operation with typed confirmation, optional VACUUM
- **Import tab:** File upload, validation, conflict policy selection
- **CSS:** Inline styles, tabbed interface

### 6.5 Navigation Map
- **Parent:** Owner Admin Panel

### 6.6 Data Dependencies & Side Effects
- **Read:** Archive previews, existing archives
- **Write:** Creates archives (with SHA256 hash), purges data, imports data
- **Services:** `IArchiveService`, `IPurgeService`, `IImportService`

### 6.7 Forms & Submissions
| Handler | Method | Notes |
|---------|--------|-------|
| `OnPostPreviewArchiveAsync` | POST | Preview what would be archived |
| `OnPostCreateArchiveAsync` | POST | Create archive with date range |
| `OnGetDownloadArchiveAsync` | GET | Download archive as CSV or NDJSON |
| `OnPostPurgeAsync` | POST | Destructive purge with typed confirmation + optional VACUUM |
| `OnPostImportAsync` | POST | Upload + validate + import with conflict policy |

### 6.8 Interesting Behaviors
- SHA256 hash for archive integrity verification
- Typed confirmation for destructive purge (user must type exact string)
- Archive check before purge (warns if data not archived first)
- Optional SQLite VACUUM after purge

### 6.9 Traceability
- Audit logging for all archive/purge/import operations

---

## 7. Pages/Owner/DatabaseConsole

### 7.1 Identity & Routing
- **File:** `Pages/Owner/DatabaseConsole.cshtml` + `Pages/Owner/DatabaseConsole.cshtml.cs`
- **Route:** `/Owner/DatabaseConsole`
- **Class:** `DatabaseConsoleModel : PageModel`
- **Purpose:** Execute SQL queries against the database

### 7.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:SystemConfiguration")]`
- **Scope:** System-wide

### 7.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `DatabaseConsole_*` family

### 7.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Design:** SQL editor textarea, sidebar with table names, quick query buttons, results table
- **CSS:** Inline styles

### 7.5 Navigation Map
- **Parent:** Owner Admin Panel

### 7.6 Data Dependencies & Side Effects
- **Read:** SQLite database via `SqliteOpenMode.ReadOnly` connection
- **Write:** None (read-only connection enforced)

### 7.7 Forms & Submissions
| Handler | Method | Parameters | Validation |
|---------|--------|------------|------------|
| `OnPostAsync` | POST | Query (SQL string) | SELECT-only, semicolon rejection, read-only connection |

### 7.8 Interesting Behaviors
- **Security:** Read-only SQLite connection (`SqliteOpenMode.ReadOnly`)
- **Security:** Semicolon rejection prevents multi-statement injection
- **Security:** Only SELECT queries allowed (blocks INSERT/UPDATE/DELETE/DROP etc.)
- Sidebar shows table names from `sqlite_master`
- Quick query buttons for common diagnostic queries
- Results rendered as HTML table

### 7.9 Traceability
- Query execution logged

---

## 8. Pages/Owner/EmailConfig

### 8.1 Identity & Routing
- **File:** `Pages/Owner/EmailConfig.cshtml` + `Pages/Owner/EmailConfig.cshtml.cs`
- **Route:** `/Owner/EmailConfig`
- **Class:** `EmailConfigModel : LocalizedPageModel`
- **Purpose:** Configure email notification service

### 8.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:ConfigureEmailSettings")]`

### 8.3 Localization
- **Base class:** `LocalizedPageModel`
- **Keys:** `EmailConfig_*`, `Success_EmailConfigSaved`, `Error_*` family

### 8.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Fields:** API URL, API key (masked if existing), from address, enabled toggle
- **Status widget:** Last test result (timestamp, success/fail, error)
- **Statistics dashboard:** Total sent, today, failed, last sent
- **Diagnostics output:** Formatted request/response details
- **Export:** JSON and CSV log export
- **CSS:** Inline styles

### 8.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Links:** To EmailTemplates page

### 8.6 Data Dependencies & Side Effects
- **Read:** `IEmailConfigService`, `IEmailApiLogService` (logs, stats)
- **Write:** Email configuration (API key encrypted), test email sending
- **Services:** `IEmailConfigService`, `IMailService` (`SendMailDirectAsync`), `IEmailApiLogService`, `IAuditLogService`

### 8.7 Forms & Submissions
| Handler | Method | Parameters | Notes |
|---------|--------|------------|-------|
| `OnPostAsync` | POST | EmailEnabled, EmailApiKey, EmailApiUrl, EmailFromAddress | Save config |
| `OnPostSendTestAsync` | POST | testEmail | Send test email synchronously |
| `OnGetExportLogsJsonAsync` | GET | count (default 100) | Export logs as JSON |
| `OnGetExportLogsCsvAsync` | GET | count (default 100) | Export logs as CSV with UTF-8 BOM for Hebrew Excel |

### 8.8 Interesting Behaviors
- Uses `SendMailDirectAsync` (synchronous) for test emails to avoid race condition with async `SendMailAsync`
- CSV export includes UTF-8 BOM for Hebrew Excel compatibility
- Diagnostics show full request/response including headers, body, status code
- API key is encrypted at rest via `IEmailConfigService`

### 8.9 Traceability
- Audit logging for config changes and test email sends

---

## 9. Pages/Owner/EmailTemplates

### 9.1 Identity & Routing
- **File:** `Pages/Owner/EmailTemplates.cshtml` + `Pages/Owner/EmailTemplates.cshtml.cs`
- **Route:** `/Owner/EmailTemplates`
- **Class:** `EmailTemplatesModel : PageModel`
- **Purpose:** Manage email notification templates

### 9.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`

### 9.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `EmailTemplates_*` family

### 9.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Design:** Card per template type, modal for editing
- **Fields per template:** Custom message (max 2000 chars), enabled toggle, available variables, default message
- **CSS:** Inline styles

### 9.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Links:** Back to EmailConfig page

### 9.6 Data Dependencies & Side Effects
- **Read:** All `EmailTemplateType` enum values, stored templates
- **Write:** Template custom messages, enabled/disabled state, reset to default
- **Services:** `IEmailTemplateService`

### 9.7 Forms & Submissions
| Handler | Method | Notes |
|---------|--------|-------|
| `OnPostSaveAsync` | POST | Save template message and enabled state |
| `OnPostResetAsync` | POST | Reset template to default message |

### 9.8 Interesting Behaviors
- Iterates over all `EmailTemplateType` enum values to show complete list
- Template variables are type-specific (e.g., `{UserName}`, `{ShiftDate}`)
- Bootstrap modal for editing

### 9.9 Traceability
- Changes logged via service layer

---

## 10. Pages/Owner/FeatureFlags

### 10.1 Identity & Routing
- **File:** `Pages/Owner/FeatureFlags.cshtml` + `Pages/Owner/FeatureFlags.cshtml.cs`
- **Route:** `/Owner/FeatureFlags`
- **Class:** `FeatureFlagsModel : PageModel`
- **Purpose:** View and toggle global feature flags

### 10.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:SystemConfiguration")]`
- **Scope:** Global flags only (filters to `CompanyId==null, UserId==null`)

### 10.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `FeatureFlags_*` family

### 10.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Design:** Collapsible categories, toggle switches per flag
- **Categories:** Operations, UI Features, Excel Calendars, Features, API Endpoints
- **CSS:** Inline styles with CSS variables

### 10.5 Navigation Map
- **Parent:** Owner Admin Panel

### 10.6 Data Dependencies & Side Effects
- **Read:** `IFeatureFlagService` (global flags)
- **Write:** `IFeatureFlagService.SetFlagAsync()` (invalidates cache)
- **Cache:** Immediate invalidation on toggle

### 10.7 Forms & Submissions
| Handler | Method | Parameters |
|---------|--------|------------|
| `OnPostToggleAsync` | POST | flagName, enabled |

### 10.8 Interesting Behaviors
- Changes take effect immediately via cache invalidation
- Only shows global flags (filters out company/user-specific overrides)
- Grouped by category for organization

### 10.9 Traceability
- Flag changes logged via service

---

## 11. Pages/Owner/GameConfig

### 11.1 Identity & Routing
- **File:** `Pages/Owner/GameConfig.cshtml` + `Pages/Owner/GameConfig.cshtml.cs`
- **Route:** `/Owner/GameConfig`
- **Class:** `GameConfigModel : PageModel`
- **Purpose:** Configure match-3 game settings

### 11.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Company-scoped (stored in `AppConfig` table)

### 11.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `GameConfig_*` family

### 11.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Settings:** Game enabled, scoring (3/4/5-match points), mega combo (multiplier, min lines), grid size (4-10), milestones
- **CSS:** Inline styles

### 11.5 Navigation Map
- **Parent:** Owner Admin Panel

### 11.6 Data Dependencies & Side Effects
- **Read/Write:** `AppConfig` table entries (company-scoped)
- **Services:** None (direct DB)

### 11.7 Forms & Submissions
| Handler | Method | Notes |
|---------|--------|-------|
| `OnPostAsync` | POST | Save all game settings with extensive validation |

### 11.8 Interesting Behaviors
- Extensive input validation (range checks for all numeric fields)
- Settings stored as key-value pairs in `AppConfig` table
- Company-scoped configuration

### 11.9 Traceability
- Changes logged

---

## 12. Pages/Owner/GriffinConfig

### 12.1 Identity & Routing
- **File:** `Pages/Owner/GriffinConfig.cshtml` + `Pages/Owner/GriffinConfig.cshtml.cs`
- **Route:** `/Owner/GriffinConfig`
- **Class:** `GriffinConfigModel : LocalizedPageModel`
- **Purpose:** ADFS/Griffin SSO configuration

### 12.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** System-wide (not company-scoped)

### 12.3 Localization
- **Base class:** `LocalizedPageModel`
- **Keys:** `GriffinConfig_*`, `Success_*`, `Error_*` families

### 12.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Fields:** Enabled toggle, base URL, callback URL, timeout, auto-provision toggle, default role
- **Status widget:** Last test result
- **Connection test:** Diagnostic output
- **Logs:** Recent connection logs, failure history
- **"Find This" modal:** Guided UI for base URL discovery
- **CSS:** Inline styles

### 12.5 Navigation Map
- **Parent:** Owner Admin Panel

### 12.6 Data Dependencies & Side Effects
- **Read/Write:** `GriffinConfig` entity
- **Services:** `IGriffinConfigService`, `IGriffinApiLogService`

### 12.7 Forms & Submissions
| Handler | Method | Notes |
|---------|--------|-------|
| `OnPostAsync` | POST | Save config |
| `OnPostTestConnectionAsync` | POST | Test saves config first, then tests connection |

### 12.8 Interesting Behaviors
- Test connection saves config before testing (critical fix noted in comments)
- Diagnostic output shows full request/response
- System-wide config (single instance, not per-company)

### 12.9 Traceability
- Audit logging for config changes and test attempts

---

## 13. Pages/Owner/LanguageEditMode

### 13.1 Identity & Routing
- **File:** `Pages/Owner/LanguageEditMode.cshtml` + `Pages/Owner/LanguageEditMode.cshtml.cs`
- **Route:** `/Owner/LanguageEditMode`
- **Class:** `LanguageEditModeModel : PageModel`
- **Purpose:** Entry point for in-app translation editing

### 13.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`

### 13.3 Localization
- Not applicable (redirect-only page)

### 13.4 UI & Design Inventory
- No rendered UI (GET-only, sets cookies and redirects)

### 13.5 Navigation Map
- **Redirect:** To home page after setting cookies

### 13.6 Data Dependencies & Side Effects
- **Write:** Sets cookies: `language_edit_mode`, `language_edit_companyId`, `language_edit_culture`
- Cookie expiry: 2 hours
- **Security note:** Cookies are `HttpOnly=false` (JS needs to read them)

### 13.7 Forms & Submissions
- No forms (GET-only)

### 13.8 Interesting Behaviors
- Validates companyId + culture query params
- Security check validates owner has selected the target company
- Cookies are intentionally non-HttpOnly for JavaScript access

### 13.9 Traceability
- No audit logging

---

## 14. Pages/Owner/LanguageManagement

### 14.1 Identity & Routing
- **File:** `Pages/Owner/LanguageManagement.cshtml` + `Pages/Owner/LanguageManagement.cshtml.cs`
- **Route:** `/Owner/LanguageManagement`
- **Class:** `LanguageManagementModel : PageModel`
- **Purpose:** Company language settings and translation overrides

### 14.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Special:** `[IgnoreAntiforgeryToken]` (for JSON POST from JavaScript)

### 14.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `LanguageMgmt_*` family

### 14.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb, OwnerCompanySelector
- **Design:** Language settings (default + alternate culture), override table with search/filter, add/delete overrides
- **CSS:** Inline styles

### 14.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Links:** To LanguageEditMode page

### 14.6 Data Dependencies & Side Effects
- **Read/Write:** Company language settings, translation overrides
- **Services:** `ILanguageManagementService`, `ICompanyLocalizationService`, `IOwnerCompanySelectorService`

### 14.7 Forms & Submissions
| Handler | Method | Notes |
|---------|--------|-------|
| `OnPostSaveSettingsAsync` | POST | Save language settings |
| `OnPostAddOverrideAsync` | POST | Add translation override |
| `OnPostDeleteOverrideAsync` | POST | Delete translation override |
| `OnPostBulkSaveDraftsAsync` | POST/JSON | AJAX bulk save for edit mode drafts |

### 14.8 Interesting Behaviors
- `[IgnoreAntiforgeryToken]` for JSON API endpoints from JavaScript
- Bulk save drafts API endpoint for in-app edit mode
- Company-scoped overrides

### 14.9 Traceability
- Changes logged via service

---

## 15. Pages/Owner/LockedUsers

### 15.1 Identity & Routing
- **File:** `Pages/Owner/LockedUsers.cshtml` + `Pages/Owner/LockedUsers.cshtml.cs`
- **Route:** `/Owner/LockedUsers`
- **Class:** `LockedUsersModel : PageModel`
- **Purpose:** View and unlock locked user accounts

### 15.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (`IgnoreQueryFilters()`)
- **SECURITY-AUDITED comment:** Yes

### 15.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `LockedUsers_*` family

### 15.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** None (standard page)
- **Sections:** Locked users table, rate-limited signup IPs table
- **Table columns (users):** User, Email, Failed Attempts, Last Attempt, Source IP, Status (locked/expired), Unlock button
- **Table columns (IPs):** IP Address, Attempts, Last Attempt
- **CSS:** Bootstrap-based

### 15.5 Navigation Map
- **Parent:** Owner Hub
- **Links:** Back to `/Owner/Hub`

### 15.6 Data Dependencies & Side Effects
- **Read:** Users (IgnoreQueryFilters, locked + recently locked), AuditLogs (IgnoreQueryFilters for IP enrichment), `IRateLimitingService`
- **Write:** User unlock (reset `FailedLoginAttempts`, null `LockoutEnd`), clear rate limits

### 15.7 Forms & Submissions
| Handler | Method | Parameters | Notes |
|---------|--------|------------|-------|
| `OnPostUnlockAsync` | POST | userId | Unlock a locked account |
| `OnPostClearSignupRateLimitsAsync` | POST | - | Clear all signup rate limit entries |

### 15.8 Interesting Behaviors
- Shows both actively locked users AND recently locked (within 1 hour, expired)
- Enriches with source IP from audit logs (most recent `LoginFailed` per user)
- Rate-limited signups section uses `IRateLimitingService.GetActiveEntries()`
- TempData for flash messages across redirect

### 15.9 Traceability
- Audit logging for unlock and rate limit clear actions
- Logger output for all operations

---

## 16. Pages/Owner/MasterPrograms

### 16.1 Identity & Routing
- **File:** `Pages/Owner/MasterPrograms.cshtml` + `Pages/Owner/MasterPrograms.cshtml.cs`
- **Route:** `/Owner/MasterPrograms`
- **Class:** `MasterProgramsModel : PageModel`
- **Purpose:** Manage collections of Programs (complete weekly schedules)

### 16.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:ManagerHomeAccess")]` (expanded per P1-3)
- **Scope:** Tenant-scoped via `ITenantResolver`

### 16.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `MasterPrograms_*` family

### 16.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb, OwnerCompanySelector
- **Design:** Cards for master programs, modal for generation with date range
- **CSS:** Inline styles

### 16.5 Navigation Map
- **Parent:** Owner Admin Panel

### 16.6 Data Dependencies & Side Effects
- **Read/Write:** MasterProgram CRUD, shift instance generation
- **Services:** `IMasterProgramService`, `IShiftProgramService`, `IAuditLogService`

### 16.7 Forms & Submissions
| Handler | Method | Notes |
|---------|--------|-------|
| `OnPostCreateAsync` | POST | Create master program with selected programs |
| `OnPostDeleteAsync` | POST | Delete master program |
| `OnPostGenerateAsync` | POST | Generate shift instances from master program over date range |

### 16.8 Interesting Behaviors
- MasterProgram is a convenience wrapper for applying multiple Programs at once
- Generation modal with date range and overwrite option

### 16.9 Traceability
- Audit logging via `IAuditLogService`

---

## 17. Pages/Owner/Permissions

### 17.1 Identity & Routing
- **File:** `Pages/Owner/Permissions.cshtml` + `Pages/Owner/Permissions.cshtml.cs`
- **Route:** `/Owner/Permissions`
- **Class:** `PermissionsModel : PageModel`
- **Purpose:** Permissions dashboard with summary stats and links to grant management

### 17.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (`IgnoreQueryFilters()`)
- **SECURITY-AUDITED comment:** Yes

### 17.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`, `ILocalizationService`
- **Keys:** `Owner_Permissions`, `Permissions_*`, `QuickActions`, `ViewAllGrants`, etc.

### 17.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Stats grid:** TotalGrants, UsersWithGrants, AutoGrants, ManualGrants
- **Quick actions:** Links to View All Grants, Assign Grant, Manage Roles
- **Two-column grid:** Grant Type Breakdown (top 10 by count), Recent Grants (last 10)
- **CSS:** Inline `<style>` block, custom card/grid design with CSS variables
- **Responsive:** Media query at 768px

### 17.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Links to:** `/Admin/Organization/Grants`, `/Admin/Organization/Grants/Assign`, `/Admin/Organization/Roles`

### 17.6 Data Dependencies & Side Effects
- **Read:** Grants (IgnoreQueryFilters), GrantTypes (IgnoreQueryFilters), Users/GrantedByUser via Include
- **Write:** None (read-only dashboard)

### 17.7 Forms & Submissions
- No POST handlers (read-only page)

### 17.8 Interesting Behaviors
- Two separate DB queries for grant types and counts, joined in-memory for EF Core compatibility
- Top 10 grant types by usage count
- Recent grants show auto vs. manual badge, grantor name, date

### 17.9 Traceability
- No audit logging (display-only)

---

## 18. Pages/Owner/Programs

### 18.1 Identity & Routing
- **File:** `Pages/Owner/Programs.cshtml` + `Pages/Owner/Programs.cshtml.cs`
- **Route:** `/Owner/Programs`
- **Class:** `ProgramsModel : PageModel`
- **Purpose:** Manage weekly schedule templates (Programs) and generate shift instances

### 18.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:ManagerHomeAccess")]` (expanded per P1-2)
- **Scope:** Tenant-scoped via `ITenantResolver`

### 18.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`, `ILocalizationService`
- **Keys:** `Programs_*` family

### 18.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb, OwnerCompanySelector
- **Create form:** ShiftType dropdown, program name, day-of-week checkboxes, default staffing, per-day overrides
- **Existing programs:** Card grid with day badges, shift type, staffing details
- **Generate modal:** Date range, overwrite checkbox
- **CSS:** Inline styles, card grid, modal, weekly mask selector
- **JavaScript:** `collectPerDayStaffing()` for per-day JSON, generate modal management

### 18.5 Navigation Map
- **Parent:** Owner Admin Panel

### 18.6 Data Dependencies & Side Effects
- **Read:** ShiftPrograms (company-scoped), ShiftTypes (company-scoped with SortOrder)
- **Write:** Program CRUD, shift instance generation via `IShiftProgramService.ApplyProgramToDateRangeAsync()`
- **Services:** `IShiftProgramService`, `IAuditLogService`, `ITenantResolver`

### 18.7 Forms & Submissions
| Handler | Method | Parameters | Validation |
|---------|--------|------------|------------|
| `OnPostCreateProgramAsync` | POST | ShiftTypeId, ProgramName, SelectedDays, DefaultStaffing, PerDayStaffingJson | Name required, days required, staffing >= 1 |
| `OnPostUpdateProgramAsync` | POST | EditProgramId, fields | Same validation |
| `OnPostDeleteProgramAsync` | POST | programId | Confirm dialog |
| `OnPostGenerateInstancesAsync` | POST | GenerateProgramId, GenerateStartDate, GenerateEndDate, OverwriteExisting | End > Start, max 365 days |

### 18.8 Interesting Behaviors
- Per-day staffing overrides submitted as JSON string, parsed server-side
- Date range validation caps at 365 days
- ShiftTypes sorted by `SortOrder` (NotMapped property, sorted in-memory after load)
- Success/error messages via query string redirect params

### 18.9 Traceability
- Logger for all CRUD and generation operations
- Audit log service injected but CRUD calls noted as missing explicit audit log writes (logger only)

---

## 19. Pages/Owner/SelectCompany

### 19.1 Identity & Routing
- **File:** `Pages/Owner/SelectCompany.cshtml` + `Pages/Owner/SelectCompany.cshtml.cs`
- **Route:** `/Owner/SelectCompany`
- **Class:** `SelectCompanyModel : PageModel`
- **Purpose:** POST-only handler for owner to select which company to manage

### 19.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Security:** D-03 fixes - POST-only to prevent CSRF via link, rate-limited, IP logged

### 19.3 Localization
- Minimal (TempData messages only)

### 19.4 UI & Design Inventory
- **Razor:** Minimal (`@page` only, POST-only handling)
- **GET:** Redirects to `/Owner/Index`

### 19.5 Navigation Map
- **Redirect:** To `returnUrl` (validated with `Url.IsLocalUrl()`) or `/Owner/Index`

### 19.6 Data Dependencies & Side Effects
- **Write:** Sets company selection via `IOwnerCompanySelectorService.SelectCompanyAsync()`
- **Services:** `IOwnerCompanySelectorService`, `IAuditLogService`, `IRateLimitingService`
- **Rate limiting:** 10 switches per 15 minutes per user

### 19.7 Forms & Submissions
| Handler | Method | Parameters | Notes |
|---------|--------|------------|-------|
| `OnGet` | GET | - | Redirect to Index (D-03 fix) |
| `OnPostAsync` | POST | companyId, returnUrl | Rate-limited, IP-logged, validated redirect |

### 19.8 Interesting Behaviors
- **D-03 fix:** GET removed for CSRF prevention; company switching is POST-only
- Rate limiting: 10 switches per 15 minutes per user via `IRateLimitingService`
- IP address logging for security audit trail
- `Url.IsLocalUrl()` validation on returnUrl to prevent open redirect
- Context-switch confirmation message via TempData (I-02 fix)

### 19.9 Traceability
- Full audit logging with company name, user ID, and IP address

---

## 20. Pages/Owner/SystemHealth

### 20.1 Identity & Routing
- **File:** `Pages/Owner/SystemHealth.cshtml` + `Pages/Owner/SystemHealth.cshtml.cs`
- **Route:** `/Owner/SystemHealth`
- **Class:** `SystemHealthModel : PageModel`
- **Purpose:** System health dashboard - monitor application health and performance

### 20.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (`IgnoreQueryFilters()`)
- **SECURITY-AUDITED comment:** Yes

### 20.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `Owner_SystemHealth`, `Owner_SystemHealth_*` family

### 20.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Overall status banner:** Healthy/Warning/Critical with color coding
- **Security warnings section:** Red-bordered card with action-required items
- **Health check cards (4):** Database, Memory, Uptime, Configuration
- **Logs section:** Error/Warning/Info counts with alert for high error count
- **CSS:** Inline styles with status colors, responsive grid

### 20.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Refresh:** Button reloads page

### 20.6 Data Dependencies & Side Effects
- **Read:** Database health (CanConnect, file size, record counts via IgnoreQueryFilters), Process memory/GC stats, Process uptime, Email/ADFS config status, Feature flags (DB-backed), AuditLogs (errors/failures), Disk space, User credentials check
- **Services:** `IFeatureFlagService`, `IConfiguration`, `IWebHostEnvironment`

### 20.7 Forms & Submissions
- No POST handlers (read-only)

### 20.8 Interesting Behaviors
- **Security warning D-01/B-06:** Checks if owner account uses default password `admin123` via `PasswordHasher.Verify()`
- **Security warning H-07/B-10:** Checks if public signup is enabled via feature flag
- Memory threshold: 500MB (above = unhealthy)
- Disk space threshold: 100MB free (below = warning)
- Error threshold: >10 errors = warning
- Table count hardcoded at 25 for performance
- Overall status computed from multiple health indicators

### 20.9 Traceability
- No audit logging (display-only)

---

## 21. Pages/Owner/Telemetry

### 21.1 Identity & Routing
- **File:** `Pages/Owner/Telemetry.cshtml` + `Pages/Owner/Telemetry.cshtml.cs`
- **Route:** `/Owner/Telemetry`
- **Class:** `TelemetryModel : LocalizedPageModel`
- **Purpose:** Client-side error, analytics, performance (Web Vitals), and data retention dashboard

### 21.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:SystemConfiguration")]`

### 21.3 Localization
- **Base class:** `LocalizedPageModel`
- **Keys:** `Owner_Telemetry`, `Telemetry_*` family

### 21.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **Tab bar:** Errors, Performance, Events, Cleanup
- **Errors tab:** Error counts by type (chips), recent errors table (timestamp, type, message, URL, source)
- **Performance tab:** Web Vitals summary cards (LCP, FID, CLS, etc. with good/needs-improvement/poor rating), recent metrics table
- **Events tab:** Event counts by type, recent events table
- **Cleanup tab:** Retention days input (1-365), destructive cleanup with warning
- **CSS:** Extensive inline styles, Lucide icons, responsive design
- **Data range:** Last 7 days for summaries

### 21.5 Navigation Map
- **Parent:** Owner Admin Panel
- **Tab navigation:** Query string `?tab=errors|performance|events|cleanup`

### 21.6 Data Dependencies & Side Effects
- **Read:** `IClientTelemetryService` (errors, events, metrics, Web Vitals summary)
- **Write:** Telemetry cleanup (delete old data)
- **Services:** `IClientTelemetryService`

### 21.7 Forms & Submissions
| Handler | Method | Parameters | Validation |
|---------|--------|------------|------------|
| `OnPostCleanupAsync` | POST | retentionDays (1-365) | Range validation |

### 21.8 Interesting Behaviors
- Lazy tab loading: only queries data for the active tab
- Web Vitals rating system: good (>=75%), needs improvement, poor (>=25%)
- Cleanup reports counts of deleted events, errors, and metrics
- B-019/B-020/B-021 reference: Local Observability Stack implementation

### 21.9 Traceability
- Cleanup action logged via `ILogger`

---

## 22. Pages/Owner/Hub/Index (Owner Hub)

### 22.1 Identity & Routing
- **File:** `Pages/Owner/Hub/Index.cshtml` + `Pages/Owner/Hub/Index.cshtml.cs`
- **Route:** `/Owner/Hub` (or `/Owner/Hub/Index`)
- **Namespace:** `ShiftManager.Pages.Owner.Hub`
- **Class:** `IndexModel : PageModel`
- **Purpose:** Consolidated system-wide administration dashboard with 7 category cards

### 22.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (`IgnoreQueryFilters()` throughout)
- **SECURITY-AUDITED comment:** Yes

### 22.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `OwnerHub_*` family (Title, Subtitle, Hierarchy, People, Grants, Scheduling, Settings, Analytics, SeedData, QuickLinks, and all card labels)

### 22.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb
- **7 Dashboard Cards:** Hierarchy (projects/areas/molecules/companies), People (users/active/pending), Grants (types/templates/assignments), Scheduling (programs/blueprints/groupings), Settings (email/ADFS/flags), Analytics (total/recent logs), Seed Data (entities/health)
- **Quick Links section:** 19 quick-link buttons to all Owner pages
- **CSS:** `@section Styles` block, 3-column responsive grid, animated card hover with color accents per category, quick-link grid
- **Responsive:** 3-col -> 2-col (1200px) -> 1-col (768px)

### 22.5 Navigation Map
- **Parent:** Breadcrumb root (top-level hub)
- **Children (cards):** `/Admin/Organization/Hierarchy`, `/Admin/Users`, `/Owner/Hub/Grants`, `/Owner/Programs`, `/Owner/FeatureFlags`, `/Admin/Analytics`, `/Owner/Hub/SeedData`
- **Quick Links (19):** Legacy Panel, SystemHealth, DatabaseConsole, AuditLog, AuditSearch, RoleTemplates, Backup, Languages, EmailConfig, EmailTemplates, GriffinConfig, GameConfig, Telemetry, DataLifecycle, LockedUsers, MasterPrograms, AreaConfig, Permissions, ExportUserData

### 22.6 Data Dependencies & Side Effects
- **Read:** Projects, Areas, Molecules, Companies, Users, UserJoinRequests, GrantTypes, RoleTemplates (via IRoleService), Grants, ShiftPrograms, ShiftTypes, ShiftGroupings, EmailConfigs, GriffinConfigs, Configs (feature flags), AuditLogs, FeatureFlags -- all via `IgnoreQueryFilters()`
- **Orphan check:** `ICompanyCacheService.HasOrphanedCompaniesAsync()`
- **Services:** `IRoleService`, `ICompanyCacheService`

### 22.7 Forms & Submissions
- No POST handlers (read-only dashboard)

### 22.8 Interesting Behaviors
- Seed data health determined by orphaned companies check
- Feature flags count queries `Configs` table (not `FeatureFlags`) for "Feature:*" keys
- Each dashboard card has a unique color accent on hover

### 22.9 Traceability
- No audit logging (display-only)

---

## 23. Pages/Owner/Hub/AuditSearch

### 23.1 Identity & Routing
- **File:** `Pages/Owner/Hub/AuditSearch.cshtml` + `Pages/Owner/Hub/AuditSearch.cshtml.cs`
- **Route:** `/Owner/Hub/AuditSearch`
- **Class:** `AuditSearchModel : PageModel`
- **Purpose:** Cross-tenant audit log search with filtering, pagination, and CSV export

### 23.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (`IgnoreQueryFilters()`)
- **SECURITY-AUDITED comment:** Yes

### 23.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`, `ILocalizationService`
- **Keys:** `AuditSearch_*`, `Filters`, `StartDate`, `EndDate`, `User`, `Action`, `EntityType`, `Search`, pagination keys

### 23.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb (parent: Owner Hub)
- **Stats bar:** Matching records count, action types count, entity types count
- **Filter form (GET):** Date range, User dropdown, Action dropdown, Entity Type dropdown, Search text
- **Results table:** Timestamp, User (name+email), Action (badge), Entity Type, Entity ID, Description, IP Address
- **Expandable detail rows:** Additional details (pre-formatted), User Agent, Company ID
- **Pagination:** First/Previous/numbered/Next/Last
- **Export:** CSV button with all current filters
- **CSS:** `@section Styles` block, extensive custom table/filter/pagination styles, responsive
- **JavaScript:** Per-row `toggleDetails()` functions

### 23.5 Navigation Map
- **Parent:** Owner Hub
- **Export:** Same page GET handler with CSV download

### 23.6 Data Dependencies & Side Effects
- **Read:** AuditLogs (IgnoreQueryFilters, with User include), Users (IgnoreQueryFilters for dropdown)
- **Write:** None

### 23.7 Forms & Submissions
| Handler | Method | Parameters | Notes |
|---------|--------|------------|-------|
| `OnGetAsync` | GET | StartDate, EndDate, UserId, Action, EntityType, SearchTerm, PageNumber | Paginated search (50/page) |
| `OnGetExportCsvAsync` | GET | Same filters | CSV export (max 10,000 rows), UTF-8 BOM, formula injection protection |

### 23.8 Interesting Behaviors
- CSV export includes formula injection protection (`SanitizeCsvField` prefixes dangerous chars with `'`)
- UTF-8 BOM for Hebrew Excel compatibility
- Export capped at 10,000 rows
- Dropdown data loaded from actual distinct values in audit logs
- Search spans Description, UserDisplayName, UserEmail, Details fields

### 23.9 Traceability
- Page itself is a traceability tool (reads audit logs)

---

## 24. Pages/Owner/Hub/ExportUserData

### 24.1 Identity & Routing
- **File:** `Pages/Owner/Hub/ExportUserData.cshtml` + `Pages/Owner/Hub/ExportUserData.cshtml.cs`
- **Route:** `/Owner/Hub/ExportUserData/{userId:int}`
- **Class:** `ExportUserDataModel : PageModel`
- **Purpose:** GDPR-style user data export as JSON

### 24.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (via `UserDataExportService` with IgnoreQueryFilters)
- **SECURITY-AUDITED comment:** Yes (notes IgnoreQueryFilters in service)

### 24.3 Localization
- Not applicable (JSON file download)

### 24.4 UI & Design Inventory
- **Razor:** Minimal (`@page "{userId:int}"` only, no rendered UI)
- **Output:** JSON file download

### 24.5 Navigation Map
- **Access:** Via link from Owner Hub ExportUserData quick link

### 24.6 Data Dependencies & Side Effects
- **Read:** All user data via `UserDataExportService.ExportUserDataAsync()`
- **Write:** None
- **Services:** `UserDataExportService`

### 24.7 Forms & Submissions
| Handler | Method | Parameters | Notes |
|---------|--------|------------|-------|
| `OnGetAsync` | GET | userId (route param) | Returns JSON file or 404/400/500 |

### 24.8 Interesting Behaviors
- QA Item 65 / E-06 reference
- Returns `BadRequest` for invalid user ID, `NotFound` for missing user
- JSON formatted with indentation and camelCase naming
- File named `UserData_{userId}_{timestamp}.json`

### 24.9 Traceability
- Logger output for export action

---

## 25. Pages/Owner/Hub/Grants

### 25.1 Identity & Routing
- **File:** `Pages/Owner/Hub/Grants.cshtml` + `Pages/Owner/Hub/Grants.cshtml.cs`
- **Route:** `/Owner/Hub/Grants`
- **Class:** `GrantsModel : PageModel`
- **Purpose:** Unified grant management UI - grant types, role templates, and user grants with CanGive delegation

### 25.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (`IgnoreQueryFilters()` throughout)
- **SECURITY-AUDITED comment:** Yes

### 25.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `Grants_*` family (Title, Subtitle, GrantTypes, RoleTemplates, etc.)

### 25.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb (parent: Owner Hub)
- **Stats grid:** TotalGrantTypes, TotalRoleTemplates, TotalGrants, UsersWithGrants
- **Three sections (tabs implied):**
  1. Grant Types by Category (collapsible, with usage count)
  2. Role Templates (list with grant counts)
  3. Grant Actions (Apply Owner Grants, Apply User Management Grants buttons)
- **User search + grant assignment UI:** Search users, view current grants, assign new grants with scope selection (Project->Area->Molecule->Company->Department hierarchy)
- **CSS:** Extensive inline styles (67KB+ file)
- **JavaScript:** Fetch-based AJAX for user search, grant assignment/revocation, role template editing

### 25.5 Navigation Map
- **Parent:** Owner Hub
- **Links:** To RoleTemplates pages

### 25.6 Data Dependencies & Side Effects
- **Read:** GrantTypes, Grants (IgnoreQueryFilters), RoleTemplates (via IRoleService), Projects/Areas/Molecules/Companies hierarchy (IgnoreQueryFilters)
- **Write:** Grant assignment, grant revocation, grant delegation updates, role template grant CRUD
- **Services:** `IGrantService`, `IRoleService`, `IConcurrencyService`

### 25.7 Forms & Submissions
| Handler | Method | Type | Notes |
|---------|--------|------|-------|
| `OnPostApplyOwnerGrantsAsync` | POST | Form | Apply owner template grants to user #1 |
| `OnPostApplyUserManagementGrantsAsync` | POST | Form | Apply user mgmt grants to user #1 |
| `OnGetSearchUsersAsync` | GET | AJAX/JSON | Search users by name/email (min 2 chars) |
| `OnGetUserGrantsAsync` | GET | AJAX/JSON | Get user's current grants with full scope |
| `OnPostAssignGrantAsync` | POST | AJAX/JSON | Assign grant with scope, checks CanGive permission |
| `OnPostRevokeGrantAsync` | POST | AJAX/JSON | Revoke grant by ID |
| `OnPostUpdateGrantDelegationAsync` | POST | AJAX/JSON | Toggle CanGive on existing grant |
| `OnGetRoleTemplateAsync` | GET | AJAX/JSON | Get role template details with grants |
| `OnPostAddRoleTemplateGrantAsync` | POST | AJAX/JSON | Add grant to role template |
| `OnPostUpdateRoleTemplateGrantAsync` | POST | AJAX/JSON | Update role template grant |
| `OnPostRemoveRoleTemplateGrantAsync` | POST | AJAX/JSON | Remove grant from role template |
| `OnGetGrantTypeUsageAsync` | GET | AJAX/JSON | Get which templates include a grant type |

### 25.8 Interesting Behaviors
- CanGive delegation check before grant assignment (`CanUserGrantAsync`), bypassed for AdminAccess users
- Concurrency handling via `IConcurrencyService` on all write operations
- Grant assignment creates scope from hierarchy (Project/Area/Molecule/Company/Department)
- 14 view model and request model inner classes
- One of the largest code-behind files in the Owner section

### 25.9 Traceability
- Logger for all grant operations (assign, revoke, delegate, template grant CRUD)

---

## 26. Pages/Owner/Hub/SeedData

### 26.1 Identity & Routing
- **File:** `Pages/Owner/Hub/SeedData.cshtml` + `Pages/Owner/Hub/SeedData.cshtml.cs`
- **Route:** `/Owner/Hub/SeedData`
- **Class:** `SeedDataModel : PageModel`
- **Purpose:** Database seed status, seeding actions, and diagnostics

### 26.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (`IgnoreQueryFilters()`)
- **SECURITY-AUDITED comment:** Yes

### 26.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `SeedData_*` family

### 26.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb (parent: Owner Hub)
- **Status dashboard:** 10-item grid (GrantTypes, RoleTemplates, RoleTemplateGrants, Projects, Areas, Molecules, Companies, Departments, ShiftGroupings, FeatureFlags) with seeded/not-seeded status
- **Seeding actions:** Seed All, Seed Grant Types, Seed Role Templates, Seed Organization, Seed Feature Flags (buttons disabled when already seeded)
- **Diagnostics (3 tables):** Company diagnostics (orphan detection), User diagnostics (first 50, orphan detection), Admin Grants (user #1)
- **CSS:** `@section Styles` block, status grid, action buttons, diagnostic tables

### 26.5 Navigation Map
- **Parent:** Owner Hub

### 26.6 Data Dependencies & Side Effects
- **Read:** All entity counts (IgnoreQueryFilters), Companies with Molecule join, Users with Company enrichment, Grants for user #1
- **Write:** Seed data creation (GrantTypes, RoleTemplates + RoleTemplateGrants, Organization hierarchy, FeatureFlags)
- **Services:** `IJobTypeService`, seed data classes (`GrantTypeSeed`, `RoleTemplateSeed`, `ShiftyOrganizationSeed`, `FeatureFlagSeed`)

### 26.7 Forms & Submissions
| Handler | Method | Notes |
|---------|--------|-------|
| `OnPostSeedAllAsync` | POST | Run all seed operations |
| `OnPostSeedGrantTypesAsync` | POST | Seed grant types only |
| `OnPostSeedRoleTemplatesAsync` | POST | Seed role templates + grants |
| `OnPostSeedOrganizationAsync` | POST | Seed organization hierarchy |
| `OnPostSeedFeatureFlagsAsync` | POST | Seed feature flags |

### 26.8 Interesting Behaviors
- Idempotent seeding: checks for existing records by Key/Name before inserting
- Expected counts compared against actual counts from seed data classes
- Orphan detection: companies without MoleculeId, users without valid company
- RoleTemplateGrant seeding matches by ID (assumes seed IDs are stable)
- Grant type IDs cleared to 0 before insert (EF auto-generates)
- TempData for success/error messages

### 26.9 Traceability
- Logger for seeding operations with counts

---

## 27. Pages/Owner/Hub/RoleTemplates/Index

### 27.1 Identity & Routing
- **File:** `Pages/Owner/Hub/RoleTemplates/Index.cshtml` + `Pages/Owner/Hub/RoleTemplates/Index.cshtml.cs`
- **Route:** `/Owner/Hub/RoleTemplates` (or `/Owner/Hub/RoleTemplates/Index`)
- **Class:** `IndexModel : PageModel` (namespace `ShiftManager.Pages.Owner.Hub.RoleTemplates`)
- **Purpose:** List all role templates with stats

### 27.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant (`IgnoreQueryFilters()` for user counting)
- **SECURITY-AUDITED comment:** Yes

### 27.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `RoleTemplates_*` family

### 27.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb (parent: Owner Hub)
- **Stats grid:** Total, System, Custom, Users Assigned
- **Actions:** Create Custom Role button, Manage Grants link
- **Table columns:** Key (with System badge), Display Name, Derived Role (color-coded badge per role), Scope, Grants count, Users count, Labels count, Status (active/inactive dot, signup/assignable mini badges), Edit link
- **CSS:** `@section Styles` block, monospace key display, role-specific color badges, status dots
- **Responsive:** 4-col -> 2-col stats grid at 768px

### 27.5 Navigation Map
- **Parent:** Owner Hub
- **Children:** Create (`/Owner/Hub/RoleTemplates/Create`), Edit (`/Owner/Hub/RoleTemplates/Edit?id=X`)
- **Links:** To `/Owner/Hub/Grants`

### 27.6 Data Dependencies & Side Effects
- **Read:** RoleTemplates (with AutoGrants, JobTypeLabels), Users grouped by RoleTemplateId (IgnoreQueryFilters)
- **Write:** None (read-only list)

### 27.7 Forms & Submissions
- No POST handlers

### 27.8 Interesting Behaviors
- User counts per template via cross-tenant query
- Inactive templates shown with reduced opacity
- Mini badges for signup visibility and default assignability

### 27.9 Traceability
- No audit logging (display-only)

---

## 28. Pages/Owner/Hub/RoleTemplates/Create

### 28.1 Identity & Routing
- **File:** `Pages/Owner/Hub/RoleTemplates/Create.cshtml` + `Pages/Owner/Hub/RoleTemplates/Create.cshtml.cs`
- **Route:** `/Owner/Hub/RoleTemplates/Create`
- **Class:** `CreateModel : LocalizedPageModel`
- **Purpose:** Create a new custom role template with optional job-type labels

### 28.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`

### 28.3 Localization
- **Base class:** `LocalizedPageModel`
- **Keys:** `RoleTemplates_Create`, `RoleTemplates_*` family, `Error_RoleTemplate_*`

### 28.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb (parent: Owner Hub -> RoleTemplates)
- **Form sections:**
  1. Basic Information: Key (pattern validated), Derived Role dropdown, Display Name EN/HE, Scope Level, Sort Order
  2. Job Type Labels: Dynamic rows (JS) with Job Type dropdown, English name, Hebrew name
  3. Checkboxes: Can Be Assigned By Default, Visible In Public Signup
- **CSS:** `@section Styles` block, form grid, label rows, responsive
- **JavaScript:** `addLabelRow()` for dynamic job type label rows

### 28.5 Navigation Map
- **Parent:** Owner Hub -> RoleTemplates
- **On success:** Redirects to Edit page for the new template

### 28.6 Data Dependencies & Side Effects
- **Read:** JobTypes (via `IJobTypeService`)
- **Write:** Creates `RoleTemplate` and `RoleTemplateJobTypeLabel` entities in a transaction
- **Services:** `IJobTypeService`

### 28.7 Forms & Submissions
| Handler | Method | Parameters | Validation |
|---------|--------|------------|------------|
| `OnPostAsync` | POST | Key, DerivedUserRole, DisplayNameEN/HE, ScopeLevel, SortOrder, CanBeAssignedByDefault, IsVisibleInSignup, LabelJobTypeIds[], LabelDisplayNamesEN[], LabelDisplayNamesHE[] | Key required + regex `[A-Za-z_][A-Za-z0-9_]*`, uniqueness check |

### 28.8 Interesting Behaviors
- Key validation: alphanumeric + underscore, regex enforced both client and server
- Key uniqueness checked against database
- Transaction wraps template + labels creation
- Auto-generates `NameKey` and `DescriptionKey` from Key (`Role_{Key}`, `RoleDesc_{Key}`)
- `IsSystem = false` for all custom templates
- Job type labels submitted as parallel arrays (LabelJobTypeIds, LabelDisplayNamesEN, LabelDisplayNamesHE)

### 28.9 Traceability
- Logger for template creation

---

## 29. Pages/Owner/Hub/RoleTemplates/Edit

### 29.1 Identity & Routing
- **File:** `Pages/Owner/Hub/RoleTemplates/Edit.cshtml` + `Pages/Owner/Hub/RoleTemplates/Edit.cshtml.cs`
- **Route:** `/Owner/Hub/RoleTemplates/Edit?id=X`
- **Class:** `EditModel : PageModel`
- **Purpose:** Edit role template metadata, job-type labels, and grant assignment

### 29.2 Access Control & Scope
- **Policy:** `[Authorize(Policy = "Grant:AdminAccess")]`
- **Scope:** Cross-tenant for user count (`IgnoreQueryFilters()`)
- **SECURITY-AUDITED comment:** Yes

### 29.3 Localization
- **Localizer:** `IStringLocalizer<SharedResources>`
- **Keys:** `RoleTemplates_*` family, `Edit`, `Save`, `Cancel`, `Remove`, `Search`, `Add`, `None`

### 29.4 UI & Design Inventory
- **Layout:** `_Layout`
- **Components:** Breadcrumb (parent: Owner Hub -> RoleTemplates -> [Key])
- **Three tabs:**
  1. **Metadata:** Display names EN/HE, Derived Role (locked for system), Scope (locked for system), Sort Order, checkboxes (assignable, signup visible, active)
  2. **Grants:** Add grant panel (type dropdown grouped by category, scope mode, CanOwn/CanGive/UseOwnJobType checkboxes, target job type), assigned grants table grouped by category (collapsible), search filter, inline editing with debounced auto-save
  3. **Labels:** Job type label management (parallel arrays form)
- **CSS:** `@section Styles` block, extensive (tabs, grant tables, add-grant card, category sections, toast notifications, save animations)
- **JavaScript:** Tab switching, category toggle, grant search filter, debounced AJAX `updateGrant()` (300ms), `addGrant()`, `removeGrant()`, toast notifications, label row management
- **Toast:** Fixed-position success/error notifications

### 29.5 Navigation Map
- **Parent:** Owner Hub -> RoleTemplates
- **Stay on page:** After all operations (metadata save, grant CRUD, label save)

### 29.6 Data Dependencies & Side Effects
- **Read:** RoleTemplate with AutoGrants (including GrantType), JobTypeLabels (including JobType), all GrantTypes, all JobTypes, cross-tenant user count
- **Write:** Template metadata, RoleTemplateGrants (add/update/remove via AJAX), RoleTemplateJobTypeLabels (delete-and-recreate)
- **Services:** `IGrantService`, `IJobTypeService`

### 29.7 Forms & Submissions
| Handler | Method | Type | Notes |
|---------|--------|------|-------|
| `OnPostMetadataAsync` | POST | Form | Save metadata (system templates lock Key/ScopeLevel/DerivedUserRole) |
| `OnPostLabelsAsync` | POST | Form | Replace all job-type labels (delete+add) |
| `OnPostAddGrantAsync` | POST | AJAX/JSON | Add grant with full options (ScopeMode, CanOwn, CanGive, UseOwnJobType, TargetJobTypeId) |
| `OnPostUpdateGrantAsync` | POST | AJAX/JSON | Update grant (debounced auto-save, 300ms) |
| `OnPostRemoveGrantAsync` | POST | AJAX/JSON | Remove grant with confirmation |

### 29.8 Interesting Behaviors
- System templates lock Key, ScopeLevel, and DerivedUserRole (display-only)
- Grant editing uses debounced auto-save (300ms delay after last change)
- Grant rows show saving/saved CSS animations for feedback
- Duplicate detection with null-safe comparison for TargetJobTypeId
- Category-based collapsible grant sections with search filter
- Validates enum values (`GrantScopeMode`) before accepting
- Template ownership verified on grant operations (`grant.RoleTemplateId != Id` check)
- Labels use delete-then-add strategy (not upsert)

### 29.9 Traceability
- Logger for all grant and label operations with template ID and details

---

## Summary Statistics

| Metric | Count |
|--------|-------|
| Total pages audited | 29 |
| Pages using `Grant:AdminAccess` | 21 |
| Pages using `Grant:SystemConfiguration` | 4 |
| Pages using `Grant:ManagerHomeAccess` | 3 |
| Pages using `Grant:ConfigureEmailSettings` | 1 |
| Pages using `IgnoreQueryFilters()` | 16 |
| Pages with SECURITY-AUDITED comments | 12 |
| Pages extending `LocalizedPageModel` | 5 |
| Pages extending `PageModel` | 24 |
| Pages with AJAX/JSON handlers | 4 |
| Read-only pages (no POST) | 7 |
| POST-only/redirect pages | 2 |

### Authorization Policy Distribution

| Policy | Pages |
|--------|-------|
| `Grant:AdminAccess` | Index, AreaConfig, Backup, ClearCompanySelection, EmailTemplates, GameConfig, GriffinConfig, LanguageEditMode, LanguageManagement, LockedUsers, Permissions, SelectCompany, SystemHealth, Hub/Index, Hub/AuditSearch, Hub/ExportUserData, Hub/Grants, Hub/SeedData, Hub/RoleTemplates/Index, Hub/RoleTemplates/Create, Hub/RoleTemplates/Edit |
| `Grant:SystemConfiguration` | DataLifecycle, DatabaseConsole, FeatureFlags, Telemetry |
| `Grant:ManagerHomeAccess` | Blueprints, Programs, MasterPrograms |
| `Grant:ConfigureEmailSettings` | EmailConfig |
