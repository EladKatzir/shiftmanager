# Batch 2: Admin Pages Audit

> **Audited**: 2026-03-03
> **Scope**: All 25 pages under `Pages/Admin/` (Index, Analytics, Announcements, AuditLog, Companies, Config, Directors, DutyRotation/Index, EditProfile, Users, Organization/Index, Organization/Areas, Organization/Departments, Organization/Grants/Index, Organization/Grants/Assign, Organization/Hierarchy, Organization/JobTypes, Organization/Molecules, Organization/Projects, Organization/Roles/Index, Organization/Roles/Assign, Organization/ShiftGroupings, Settings/Index, Settings/ApprovalRules, SetupTasks/Index)

---

## 1. Admin/Index (Admin Hub Dashboard)

### 1.1 Identity & Routing
- **Route**: `/Admin/Index` (default page for `/Admin/`)
- **File**: `Pages/Admin/Index.cshtml` + `Pages/Admin/Index.cshtml.cs`
- **Purpose**: Central admin dashboard showing system stats and quick-navigation cards to all admin sub-pages.
- **Query params**: None.
- **Redirects**: None (landing page).

### 1.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]`
- **Runtime grant checks**:
  - `AdminAccess` -- determines `IsOwner` flag for stat scoping.
  - `DirectorHubAccess` -- determines `IsDirector` flag for stat scoping.
  - Per-card visibility: `ViewHierarchy`, `EditCompany`, `ManageJobTypes`, `ManageDepartments`, `ViewSettings`, `ManageAnnouncements`, `SystemConfiguration`, `ManageOnDuty`.
- **Tenant scope**: Stats are scoped by Owner/Director/Manager: Owner sees all (IgnoreQueryFilters), Director sees molecule-scoped companies, Manager sees own company.
- **IgnoreQueryFilters**: Used for Owner/Director stat queries. Audited safe -- requires ManagerHomeAccess + AdminAccess/DirectorHubAccess.
- **Gaps**: None identified.

### 1.3 Localization
- **Pattern**: `<loc key="...">` tag helper + `@Localizer["..."]` inline.
- **Key patterns**: `Admin_Title`, `Dashboard_*`, `Card_*`, `ManageUsers`, `ManageCompanies`, etc.
- **RTL/LTR**: Inherits from `_Layout` direction. No page-specific RTL handling.

### 1.4 UI & Design Inventory
- **Layout**: `_Layout` with `ViewData["Title"] = Localizer["Admin"]`.
- **Structure**: Stats bar (4 stat pills: Users, Shifts, Chores, Assignments) + grid of navigation cards.
- **Interactive elements**: Navigation cards (links to sub-pages). No forms on this page.
- **UI states**: Cards conditionally rendered based on grant checks. Feature-flagged cards (DutyRotation, SetupTasks, VacationApproval).
- **Shared components**: Breadcrumb ViewComponent (`Admin` label, active).
- **CSS**: Inline `<style>` block with card grid, stat pills, responsive breakpoints at 768px.

### 1.5 Navigation Map
- **Nav targets**: `/Admin/Users`, `/Admin/Companies`, `/Admin/Organization`, `/Admin/Organization/JobTypes`, `/Admin/Organization/Departments`, `/Admin/Settings`, `/Admin/Announcements`, `/Admin/Config`, `/Admin/Analytics`, `/Admin/AuditLog`, `/Admin/Directors`, `/Admin/Organization/Hierarchy`, `/Admin/DutyRotation`, `/Admin/SetupTasks`, `/Admin/Settings/ApprovalRules`.
- **How users reach this page**: Via main nav/sidebar "Admin" link, or direct URL.
- **Breadcrumb**: `Admin` (active, terminal).

### 1.6 Data Dependencies & Side Effects
- **Data read**: Users count, Shifts count, Chores count, ShiftAssignments count (all via AppDbContext). Feature flags: `DutyRotationEnabled`, `SetupTasksEnabled`, `VacationApprovalEnabled`.
- **Data written**: None (read-only dashboard).
- **Error handling**: Silent fail if user not found or claims invalid (returns empty stats).
- **Services**: `AppDbContext`, `ITenantResolver`, `IDirectorService`, `IGrantService`, `IFeatureFlagService`.

### 1.7 Forms & Submissions
- None. Read-only page.

### 1.8 Interesting Behaviors
- **Feature flags**: DutyRotation, SetupTasks, VacationApproval cards only rendered if corresponding flag is enabled.
- **Performance**: Multiple DB queries per stat. No caching.
- **Gotcha**: Owner/Director/Manager scoping logic is complex; must match the same logic used in Users page.

### 1.9 Traceability
- **Key services**: `IGrantService`, `IDirectorService`, `IFeatureFlagService`, `AppDbContext`.
- **Key models**: `AppUser`, `Shift`, `Chore`, `ShiftAssignment`.

---

## 2. Admin/Analytics

### 2.1 Identity & Routing
- **Route**: `/Admin/Analytics`
- **File**: `Pages/Admin/Analytics.cshtml` + `Pages/Admin/Analytics.cshtml.cs`
- **Purpose**: Analytics dashboard with date-range filterable metrics and CSV export.
- **Query params**: `dateRange` (int: 7, 30, 90, 365 days, default 30).
- **Redirects**: None.

### 2.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]`
- **Tenant scope**: Data scoped by `ICompanyCacheService` (company-level analytics).
- **IgnoreQueryFilters**: Not used directly; analytics service handles scoping.
- **Gaps**: No additional grant check beyond ManagerHomeAccess; any manager sees full analytics.

### 2.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `Analytics_*`, `CoverageRate`, `TimeOff`, `Swaps`, `UnderstaffingReport`, `TopSwappers`, `ChoresMetrics`, `OnDutyMetrics`.
- **RTL/LTR**: Inherited from layout.

### 2.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Date range selector (7/30/90/365 days with auto-submit), multiple analytics sections: Coverage Rate, Time Off, Swaps, Employee Hours table, Understaffing Report table, Top Swappers, Chores Metrics, On-Duty Metrics.
- **Interactive elements**: Date range `<select>` with `onchange="this.form.submit()"`. CSV export button (GET handler).
- **CSS**: Inline styles with stats cards, data tables, responsive grid.

### 2.5 Navigation Map
- **Nav targets**: None outbound (terminal analytics page).
- **How users reach this page**: Admin Hub card or sidebar.
- **Breadcrumb**: `Admin > Analytics`.

### 2.6 Data Dependencies & Side Effects
- **Data read**: `IAnalyticsService` provides all metrics. `ICompanyCacheService` for company context.
- **Data written**: None.
- **Error handling**: Empty state messages for each section when no data.
- **Services**: `IAnalyticsService`, `ICompanyCacheService`.

### 2.7 Forms & Submissions
- **Date range form**: GET form with `dateRange` select, auto-submits on change.
- **CSV export**: `OnGetExportCsvAsync` handler. Returns `FileContentResult` with UTF-8 BOM (`\uFEFF` prefix for Hebrew Excel compatibility). Content-Type: `text/csv; charset=utf-8`.

### 2.8 Interesting Behaviors
- **UTF-8 BOM**: Added for Hebrew Excel compatibility -- ensures Excel opens CSV with correct encoding.
- **10,000 record limit**: Not explicitly stated in Analytics but follows the pattern from AuditLog.
- **Performance**: Multiple analytics queries per page load; no caching evident.

### 2.9 Traceability
- **Key services**: `IAnalyticsService`, `ICompanyCacheService`.

---

## 3. Admin/Announcements

### 3.1 Identity & Routing
- **Route**: `/Admin/Announcements`
- **File**: `Pages/Admin/Announcements.cshtml` + `Pages/Admin/Announcements.cshtml.cs`
- **Purpose**: CRUD for system announcements with scope targeting (All/Department/Role).
- **Query params**: None.

### 3.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManageAnnouncements")]`
- **Tenant scope**: Announcements scoped via `IAnnouncementService` (company-level).
- **Gaps**: None identified.

### 3.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `Announcements_*`, `CreateAnnouncement`, `Title`, `Content`, `Scope`, `ExpiresAt`, `IsPinned`.

### 3.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Create form card + existing announcements table.
- **Interactive elements**: Scope selector with JS `updateScopeFields()` toggling Department/Role fields. ToggleActive and Delete buttons per announcement.
- **CSS**: Inline styles following standard section-card pattern.

### 3.5 Navigation Map
- **Breadcrumb**: `Admin > Announcements`.
- **How users reach**: Admin Hub card.

### 3.6 Data Dependencies & Side Effects
- **Data read**: `IAnnouncementService` for list; `ICurrentUserService` for current user; `IRoleService` for role dropdown.
- **Data written**: Announcements (create, toggle active, delete).
- **Services**: `IAnnouncementService`, `ICurrentUserService`, `IRoleService`.

### 3.7 Forms & Submissions
- **Create form**: Fields: Title (required), Content (required), Scope (All/Department/Role), TargetDepartmentId (conditional), TargetRole (conditional), ExpiresAt (datetime-local), IsPinned (checkbox).
- **Handlers**: `OnPostCreateAsync`, `OnPostToggleActiveAsync(int id)`, `OnPostDeleteAsync(int id)`.
- **Validation**: Required fields only; no XSS sanitization visible.

### 3.8 Interesting Behaviors
- **Conditional fields**: JS toggles Department/Role dropdowns based on Scope selection.
- **Scope targeting**: Announcements can target All users, specific Department, or specific Role.

### 3.9 Traceability
- **Key services**: `IAnnouncementService`, `ICurrentUserService`, `IRoleService`.

---

## 4. Admin/AuditLog

### 4.1 Identity & Routing
- **Route**: `/Admin/AuditLog`
- **File**: `Pages/Admin/AuditLog.cshtml` + `Pages/Admin/AuditLog.cshtml.cs`
- **Purpose**: Searchable, filterable audit log viewer with CSV export.
- **Query params**: `StartDate`, `EndDate`, `UserId`, `Action`, `EntityType`, `SearchTerm`, `CurrentPage`.

### 4.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]`
- **Tenant scope**: Uses `ICompanyCacheService` for company scoping.
- **IgnoreQueryFilters**: Not used directly; audit log service handles scoping.

### 4.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `AuditLog_*`, `Filter_*`, `ExportCsv`.

### 4.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Filter bar (StartDate, EndDate, UserId dropdown, Action dropdown, EntityType dropdown, SearchTerm text) + audit log table with expandable detail rows.
- **Interactive elements**: Filter dropdowns with auto-submit. Expandable detail rows via inline JS `toggleDetails{Id}()`. CSV export button. Pagination controls.
- **CSS**: Inline styles.

### 4.5 Navigation Map
- **Breadcrumb**: `Admin > AuditLog`.
- **How users reach**: Admin Hub card.

### 4.6 Data Dependencies & Side Effects
- **Data read**: `AppDbContext.AuditLogs` with filters; `ICompanyCacheService`.
- **Data written**: None (read-only).
- **Services**: `AppDbContext`, `ICompanyCacheService`.

### 4.7 Forms & Submissions
- **Filter form**: GET form with multiple filter fields. Auto-submit on dropdown change.
- **CSV export**: `OnGetExportCsvAsync` with 10,000 record limit. UTF-8 BOM.

### 4.8 Interesting Behaviors
- **Pagination**: 50 records per page.
- **CSV limit**: Capped at 10,000 records.
- **Expandable rows**: Per-entry detail toggle via per-row JS function.
- **Performance**: Query with multiple optional WHERE clauses.

### 4.9 Traceability
- **Key services**: `AppDbContext`, `ICompanyCacheService`.

---

## 5. Admin/Companies

### 5.1 Identity & Routing
- **Route**: `/Admin/Companies`
- **File**: `Pages/Admin/Companies.cshtml` + `Pages/Admin/Companies.cshtml.cs`
- **Purpose**: Company CRUD with full lifecycle management (create with manager/director, rename, delete with cascade).
- **Query params**: None.

### 5.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:EditCompany")]`
- **Tenant scope**: Uses IgnoreQueryFilters for cross-company admin operations.
- **IgnoreQueryFilters**: Extensively used for company/molecule/director queries. Audited safe -- requires EditCompany grant.

### 5.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageCompanies`, `AddCompany`, `CompanyDetails`, `ExistingDirector`, `ManagerAccount`.

### 5.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Create company form (multi-fieldset: Company Details, Director assignment, Manager Account) + companies table with Rename modal and Delete button.
- **Interactive elements**: Rename modal dialog (CSS `.modal.active` pattern), confirm dialogs for delete, molecule dropdown.
- **CSS**: Inline styles with modal overlay pattern.

### 5.5 Navigation Map
- **Breadcrumb**: `Admin > Companies`.
- **How users reach**: Admin Hub card.

### 5.6 Data Dependencies & Side Effects
- **Data read**: Companies, Molecules, Users (Directors), Shifts, Chores, etc.
- **Data written**: Company creation is transactional -- creates company + manager user + director assignment + default shift types + default configs + setup tasks. Delete performs 11-step cascade.
- **Error handling**: XSS validation via `InputSanitizer.ContainsDangerousContent()`. Slug validation. Length constraints.
- **Services**: `ISetupTaskService`, `ICompanyCacheService`, `IRoleService`, `IConcurrencyService`, `AppDbContext`.

### 5.7 Forms & Submissions
- **Create form**: Fields: MoleculeId (required), CompanyName (required, 2-50 chars), Slug (auto-generated or manual), DisplayName, ExistingDirectorId (optional), ManagerEmail, ManagerName, ManagerPassword.
- **Rename form**: Modal with new DisplayName field.
- **Handlers**: `OnPostAddCompanyAsync`, `OnPostRenameCompanyAsync`, `OnPostDeleteCompanyAsync`.
- **Validation**: XSS check, slug format, unique slug, field length constraints.

### 5.8 Interesting Behaviors
- **Transactional create**: Uses `BeginTransactionAsync` + multiple SaveChanges for atomicity.
- **BackfillMissingSlugsAsync**: Legacy slug backfill on page load.
- **11-step cascade delete**: ShiftAssignments, Shifts, ShiftTypes, Chores, ChoreAssignments, DirectorCompanies, OnDutyShifts, Notifications, UserJoinRequests, Users, Company.
- **Default configs**: Auto-creates default company configs and shift types on company creation.

### 5.9 Traceability
- **Key services**: `ISetupTaskService`, `ICompanyCacheService`, `IRoleService`, `IConcurrencyService`.

---

## 6. Admin/Config

### 6.1 Identity & Routing
- **Route**: `/Admin/Config`
- **File**: `Pages/Admin/Config.cshtml` + `Pages/Admin/Config.cshtml.cs`
- **Purpose**: Company-level configuration (RestHours, WeeklyCap) and OnDuty type management.
- **Query params**: None.

### 6.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]`
- **Tenant scope**: Company-scoped via `ICompanyContext`.

### 6.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `Config_*`, `RestHours`, `WeeklyCap`, `OnDutyTypes`.

### 6.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Company settings card (RestHours 0-24, WeeklyCap 0-168) + OnDuty types management (default types read-only, custom types CRUD) + Add OnDuty Type modal dialog.
- **Interactive elements**: Number inputs with min/max, modal for adding custom OnDuty types.
- **CSS**: Inline styles.

### 6.5 Navigation Map
- **Breadcrumb**: `Admin > Configuration`.
- **How users reach**: Admin Hub card.

### 6.6 Data Dependencies & Side Effects
- **Data read**: CompanyConfig, OnDutyTypes (default: Hakam=0, Lead=1).
- **Data written**: Company config updates, OnDuty type CRUD.
- **Services**: `ICompanyContext`, `IAuditLogService`, `IEmailConfigService`.

### 6.7 Forms & Submissions
- **Settings form**: RestHours (number, 0-24), WeeklyCap (number, 0-168).
- **Add OnDuty Type**: Modal with Name field. Auto-increment TypeValue.
- **Handlers**: POST for save settings, add/delete OnDuty type.

### 6.8 Interesting Behaviors
- **Default OnDuty types**: Hakam(0) and Lead(1) are read-only defaults that cannot be deleted.
- **Auto-increment**: New custom OnDuty types get TypeValue = max(existing) + 1.

### 6.9 Traceability
- **Key services**: `ICompanyContext`, `IAuditLogService`, `IEmailConfigService`.

---

## 7. Admin/Directors

### 7.1 Identity & Routing
- **Route**: `/Admin/Directors`
- **File**: `Pages/Admin/Directors.cshtml` + `Pages/Admin/Directors.cshtml.cs`
- **Purpose**: Director-to-company assignment management.
- **Query params**: None.

### 7.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:AssignRoles")]`
- **Tenant scope**: Uses IgnoreQueryFilters for cross-company director/company lookups.
- **IgnoreQueryFilters**: Audited safe -- requires AssignRoles grant.

### 7.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `Directors_*`, `AssignDirector`, `Revoke`, `Reassign`.

### 7.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Assign director form (Director dropdown, Company dropdown) + current assignments table with Revoke and Reassign actions.
- **CSS**: Inline styles.

### 7.5 Navigation Map
- **Breadcrumb**: `Admin > Directors`.
- **How users reach**: Admin Hub card.

### 7.6 Data Dependencies & Side Effects
- **Data read**: Directors (users with Director role), Companies, DirectorCompany assignments.
- **Data written**: DirectorCompany entries (create, soft-delete for revoke, soft-delete old + create new for reassign).
- **Services**: `AppDbContext` only (direct DB access, no service abstraction).

### 7.7 Forms & Submissions
- **Assign form**: DirectorId (dropdown), CompanyId (dropdown).
- **Revoke/Reassign**: Per-row actions on assignments table.

### 7.8 Interesting Behaviors
- **Reassign pattern**: Soft-deletes old assignment, creates new one (not an in-place update).
- **No service layer**: Uses direct AppDbContext access rather than a service interface.

### 7.9 Traceability
- **Key services**: `AppDbContext` (direct access).

---

## 8. Admin/DutyRotation/Index

### 8.1 Identity & Routing
- **Route**: `/Admin/DutyRotation`
- **File**: `Pages/Admin/DutyRotation/Index.cshtml` + `Pages/Admin/DutyRotation/Index.cshtml.cs`
- **Purpose**: Duty rotation schedule management with queue-based user ordering.
- **Query params**: `selectedId` (rotation ID for editing).
- **Feature gate**: Returns `NotFound()` if `DutyRotationEnabled` flag is disabled.

### 8.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManageOnDuty")]`
- **Feature flag**: `DutyRotationEnabled` (hard gate, not just UI hide).

### 8.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `DutyRotation_*`, `CreateRotation`, `EditRotation`, `Queue`.

### 8.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Create rotation form + rotation list with inline edit form for selected rotation + queue management (add/remove/reorder users).
- **Interactive elements**: Reorder via JS `moveQueueItem()` + `submitReorder()` with comma-separated user IDs.
- **CSS**: Inline styles.

### 8.5 Navigation Map
- **Breadcrumb**: `Admin > DutyRotation`.
- **How users reach**: Admin Hub card (conditionally shown via feature flag).

### 8.6 Data Dependencies & Side Effects
- **Data read**: DutyRotations, DutyRotationQueue entries, Users.
- **Data written**: Rotation CRUD, queue member add/remove/reorder.
- **Services**: `IDutyRotationService`, `IFeatureFlagService`.

### 8.7 Forms & Submissions
- **Create rotation**: Name, DutyType (Hakam/Lead), Frequency (Daily/Weekly/Biweekly/Monthly), MaxConsecutiveDays, IncludeWeekends.
- **Edit rotation**: Same fields, inline in selected rotation card.
- **Queue management**: Add user (dropdown), remove user (button), reorder (JS drag simulation with comma-separated IDs POST).
- **Handlers**: Create, Edit, Delete, AddQueueMember, RemoveQueueMember, ReorderQueue.

### 8.8 Interesting Behaviors
- **Feature flag gate**: Hard 404 when disabled (not just UI hiding).
- **Queue reorder**: Passes comma-separated user ID string via hidden input, server parses and updates SortOrder.

### 8.9 Traceability
- **Key services**: `IDutyRotationService`, `IFeatureFlagService`.

---

## 9. Admin/EditProfile

### 9.1 Identity & Routing
- **Route**: `/Admin/EditProfile`
- **File**: `Pages/Admin/EditProfile.cshtml` + `Pages/Admin/EditProfile.cshtml.cs`
- **Purpose**: Comprehensive user profile editor with multi-card form layout.
- **Query params**: `UserId` (required, int).

### 9.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]`
- **Tenant scope**: Profile loaded via direct DB query with grant-based access check.
- **IgnoreQueryFilters**: Used for cross-company profile editing.

### 9.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `EditProfile_*`, `PersonalInfo`, `AccountSettings`, `EmergencyContact`, `ProfessionalInfo`, `SkillsCertifications`.
- **RTL/LTR**: Military rank uses locale-aware display names.

### 9.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Multi-card form: Personal Info (avatar, display name, preferred name, phone, DOB, city), Account Settings (email, RoleTemplate, IsActive, grants link), Emergency Contact, Professional Info (JobType for Workforce, Department for Tech, Rank, Molecule, HireDate), Skills & Certifications.
- **Interactive elements**: Avatar upload/delete, RoleTemplate dropdown, conditional field visibility based on molecule type (Workforce vs Tech).
- **CSS**: Inline styles with avatar upload area, card grid.

### 9.5 Navigation Map
- **Breadcrumb**: `Admin > EditProfile`.
- **How users reach**: Click user row in Users page, or direct link.

### 9.6 Data Dependencies & Side Effects
- **Data read**: User profile, Companies, Molecules, Areas, JobTypes, Departments, RoleTemplates, AuditLogs (last 30 days for profile change history).
- **Data written**: User profile fields, avatar (via IAvatarService), RoleTemplate sync (via IRoleService).
- **Error handling**: Extensive field length validation (10+ fields). JobType validation via `IJobTypeService.ValidateJobTypeForUserAsync`.
- **Services**: `IProfileService`, `IAvatarService`, `IRoleService`, `IJobTypeService`, `IAuditLogService`.

### 9.7 Forms & Submissions
- **Profile form**: ~20 fields across 5 cards. Avatar upload (file input). JobType dropdown (Workforce) or Department dropdown (Tech).
- **Handlers**: `OnPostAsync` (save profile), `OnPostDeleteAvatarAsync`.
- **Validation**: Email format, field lengths, JobType-Molecule compatibility, RoleTemplate existence.

### 9.8 Interesting Behaviors
- **Molecule type branching**: Workforce molecules show JobType field; Tech molecules show Department field.
- **Military rank**: Locale-aware rank display names.
- **Profile change audit**: Shows last 30 days of profile change audit logs at bottom of page.
- **RoleTemplate sync**: Changing RoleTemplate triggers auto-grant assignment/cleanup via `IRoleService.AssignRoleAsync`.

### 9.9 Traceability
- **Key services**: `IProfileService`, `IAvatarService`, `IRoleService`, `IJobTypeService`, `IAuditLogService`.

---

## 10. Admin/Users

### 10.1 Identity & Routing
- **Route**: `/Admin/Users`
- **File**: `Pages/Admin/Users.cshtml` + `Pages/Admin/Users.cshtml.cs`
- **Purpose**: User management hub -- user listing with filters, join request management, new user creation, batch approval.
- **Query params**: `CurrentPage`, `JoinRequestsPage`, `FilterStatus`, `FilterCompanyId`, `FilterRole`, `UserFilterCompanyId`, `UserFilterRole`, `UserFilterMoleculeId`, `UserFilterJobTypeId`.

### 10.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]`
- **Runtime grant checks**:
  - `AdminAccess` -- determines `IsOwner` (sees all users across all companies).
  - `ManageJoinRequests` -- scopes join request visibility.
  - `DirectorHubAccess` -- fallback scope for directors without ManageJoinRequests.
- **Tenant scope**: Owner sees all (IgnoreQueryFilters), Directors see molecule-scoped companies (via `GetAccessibleCompanyIdsForGrantAsync`), Managers see own company.
- **IgnoreQueryFilters**: Extensively used. Audited safe -- scoped by accessibleCompanyIds from grant resolution.

### 10.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `UserManagement`, `JoinRequests`, `CreateUser`, `ApproveRequest`, `RejectRequest`, `BatchApprove`.

### 10.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Three sections: (1) Filter bar + users table with pagination, (2) Join requests section with status filter + batch approval, (3) Create new user form (conditional on Owner).
- **Interactive elements**: Filter dropdowns with auto-submit, batch approval checkboxes, editable cells in user table (inline role/display name editing), pagination controls, confirm dialogs.
- **CSS**: Extensive inline styles (~500 lines) covering tables, filters, batch bar, pagination, editable cells, responsive breakpoints.

### 10.5 Navigation Map
- **Breadcrumb**: `Admin` (active, terminal -- this is the "home" of admin).
- **Nav targets**: `/Admin/EditProfile?UserId={id}` per user row.
- **How users reach**: Main nav/sidebar, or from Admin Hub.

### 10.6 Data Dependencies & Side Effects
- **Data read**: Users with filters, JoinRequests, Companies, Molecules, JobTypes, Grants (count per user), DirectorCompany mappings.
- **Data written**: User creation, join request approval/rejection/batch approval, user activation/deactivation, lockout toggle, role change.
- **Side effects of deactivation**: Nulls TraineeUserId refs, removes active shift assignments, cleans grants/DirectorCompany entries.
- **Services**: `IDirectorService`, `ITraineeService`, `IAuditLogService`, `IMailService`, `INotificationService`, `IGrantService`, `IRoleService`, `IJobTypeService`, `IConcurrencyService`, `ICompanyContext`.

### 10.7 Forms & Submissions
- **Create user form**: NewEmail, NewDisplayName, NewPassword, NewRole/NewRoleTemplateId, NewUserCompanyId (Owner only), NewJobTypeId, NewMoleculeId (for Directors).
- **Join request actions**: Approve (with role assignment), Reject, Batch Approve (SelectedRequests list).
- **User actions**: Toggle active, Toggle lockout, Change role, Edit inline.
- **Handlers**: `OnPostCreateUserAsync`, `OnPostApproveJoinRequestAsync`, `OnPostRejectJoinRequestAsync`, `OnPostBatchApproveAsync`, `OnPostToggleActiveAsync`, `OnPostToggleLockoutAsync`, `OnPostChangeRoleAsync`.
- **Validation**: Email format, password requirements, role assignment permissions via `IDirectorService.CanAssignRole()`.

### 10.8 Interesting Behaviors
- **Complex scoping logic**: Three-tier fallback for company scoping: ManageJoinRequests grant -> DirectorHubAccess grant -> own company.
- **HQ exclusion**: Director company lists exclude HQ (headquarters) companies.
- **Batch approval**: Supports selecting multiple join requests and approving them in one action.
- **Director pre-loading**: Pre-loads director-company mappings to avoid N+1 queries.
- **Pagination**: Both users and join requests have independent pagination (50 per page each).
- **RoleTemplate integration**: Both legacy enum-based roles and new template-based roles are supported in dropdowns.

### 10.9 Traceability
- **Key services**: `IDirectorService`, `IGrantService`, `IRoleService`, `IJobTypeService`, `IConcurrencyService`, `ITraineeService`, `IAuditLogService`, `IMailService`, `INotificationService`.

---

## 11. Admin/Organization/Index

### 11.1 Identity & Routing
- **Route**: `/Admin/Organization`
- **File**: `Pages/Admin/Organization/Index.cshtml` + `Pages/Admin/Organization/Index.cshtml.cs`
- **Purpose**: Full organizational hierarchy visualization with inline CRUD for Companies and Departments.
- **Query params**: None.

### 11.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ViewHierarchy")]`
- **Runtime grant checks**: `CurrentUserCanManageMoleculeAsync` for POST handlers (scoped authorization).
- **IgnoreQueryFilters**: Used for hierarchy tree queries. Audited safe -- requires ViewHierarchy.

### 11.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `Organization_*`, `Hierarchy`, `AddCompany`, `AddDepartment`, `RenameCompany`.

### 11.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Very large page (~52KB). Stats bar (TotalUsers, TotalActiveProjects/Areas/Molecules) + full hierarchy tree (Project -> Area -> Molecule -> Company -> Department) with inline CRUD forms + quick-link navigation cards.
- **Interactive elements**: Expand/collapse tree nodes, inline Add Company/Department forms per molecule, Rename Company modal, inline edit buttons.
- **CSS**: Extensive inline styles (~1000+ lines) for tree visualization.

### 11.5 Navigation Map
- **Breadcrumb**: `Admin > Organization`.
- **Nav targets**: Quick-links to all Organization sub-pages (Projects, Areas, Molecules, JobTypes, Departments, Hierarchy, Grants, Roles, ShiftGroupings).
- **How users reach**: Admin Hub card or sidebar.

### 11.6 Data Dependencies & Side Effects
- **Data read**: Full hierarchy tree via `IHierarchyService`. User stats.
- **Data written**: Company create/rename within hierarchy. Department create.
- **Services**: `IHierarchyService`, `ICompanyCacheService`, `IGrantService`.

### 11.7 Forms & Submissions
- **Inline forms**: Add Company (per molecule), Add Department (per company), Rename Company (modal).
- **Handlers**: Multiple POST handlers for inline CRUD.
- **Validation**: Scoped authorization via `CurrentUserCanManageMoleculeAsync`.

### 11.8 Interesting Behaviors
- **Very large page**: ~52KB of HTML/CSS; potential performance concern.
- **Scoped authorization**: POST handlers check that the current user can manage the specific molecule being modified.
- **Inline CRUD**: Company and Department creation happens within the tree view itself.

### 11.9 Traceability
- **Key services**: `IHierarchyService`, `ICompanyCacheService`, `IGrantService`.

---

## 12. Admin/Organization/Areas/Index

### 12.1 Identity & Routing
- **Route**: `/Admin/Organization/Areas`
- **File**: `Pages/Admin/Organization/Areas/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Area CRUD management.
- **Query params**: None.

### 12.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:EditArea")]`
- **IgnoreQueryFilters**: Used for area/project queries. Audited safe -- requires EditArea.

### 12.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageAreas`, `CreateNewArea`, `AllAreas`, `SelectProject`, `ConfirmDeleteArea`.

### 12.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Create form (Project dropdown, Name, DisplayName) + areas table with ToggleActive/Delete actions.
- **Table columns**: Project, Name (DisplayName + key), Molecules count, JobTypes count, Status badge, Actions.
- **Interactive elements**: Confirm dialog for delete. Delete only shown when MoleculeCount==0 && JobTypeCount==0.
- **CSS**: Inline styles following standard section-card pattern.

### 12.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Areas`.
- **How users reach**: Organization hub or quick-links.

### 12.6 Data Dependencies & Side Effects
- **Data read**: Areas with Project, molecule/jobtype counts.
- **Data written**: Area create, toggle active, delete.
- **Dependency check**: Delete blocked if area has molecules or job types.
- **Services**: `AppDbContext`, `IJobTypeService` (for job type count).

### 12.7 Forms & Submissions
- **Create form**: SelectedProjectId (required), AreaName (required), AreaDisplayName (optional).
- **Handlers**: `OnPostCreateAsync`, `OnPostToggleActiveAsync(int id)`, `OnPostDeleteAsync(int id)`.
- **Validation**: Name required, project must exist.

### 12.8 Interesting Behaviors
- **Empty state**: Shows "NoProjectsAvailable" message when no projects exist, preventing area creation.
- **Dependency guard**: Delete button only rendered when molecule and job type counts are both zero.

### 12.9 Traceability
- **Key services**: `AppDbContext`.

---

## 13. Admin/Organization/Departments/Index

### 13.1 Identity & Routing
- **Route**: `/Admin/Organization/Departments`
- **File**: `Pages/Admin/Organization/Departments/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Department CRUD, restricted to Tech-type molecules only.
- **Query params**: None.

### 13.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManageDepartments")]`
- **IgnoreQueryFilters**: Used for molecule/department queries. Audited safe.
- **Restriction**: Only Tech-type molecules can have departments.

### 13.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageDepartments`, `CreateNewDepartment`, `DepartmentsAreForTechMolecules`, `AllDepartments`, `ConfirmDeleteDepartment`.

### 13.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Info box explaining departments are for Tech molecules + Create form (TechMolecule dropdown, Name, DisplayName) + departments table.
- **Table columns**: Area, Molecule (with Tech badge), Department (DisplayName + key), Users count, Status badge, Actions.
- **Interactive elements**: Confirm dialog for delete. Delete only shown when UserCount==0.
- **Responsive**: Mobile breakpoint at 768px collapses form grid and table actions.

### 13.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Departments`.
- **How users reach**: Organization hub.

### 13.6 Data Dependencies & Side Effects
- **Data read**: Departments with molecule/area info, user counts.
- **Data written**: Department create, toggle active, delete.
- **Dependency check**: Delete blocked if department has users.
- **Services**: `AppDbContext`.

### 13.7 Forms & Submissions
- **Create form**: SelectedMoleculeId (required, Tech molecules only), DepartmentName (required), DepartmentDisplayName (optional).
- **Handlers**: `OnPostCreateAsync`, `OnPostToggleActiveAsync(int id)`, `OnPostDeleteAsync(int id)`.

### 13.8 Interesting Behaviors
- **Tech-only restriction**: Molecule dropdown only shows Tech-type molecules.
- **Info box**: Explains the Tech-molecule restriction to users.

### 13.9 Traceability
- **Key services**: `AppDbContext`.

---

## 14. Admin/Organization/Grants/Index

### 14.1 Identity & Routing
- **Route**: `/Admin/Organization/Grants`
- **File**: `Pages/Admin/Organization/Grants/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Grant viewer with dual view modes (by user summary, or all grants detail).
- **Query params**: `ViewMode` ("users" or "grants"), `FilterGrantTypeId`, `FilterUserId`.

### 14.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ViewGrants")]`
- **IgnoreQueryFilters**: Used for cross-company grant queries. Audited safe.

### 14.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageGrants`, `ViewAndManageUserGrants`, `ByUser`, `AllGrants`, `GrantType`, `Scope`, `Capabilities`, `Own`, `Give`, `Auto`, `Manual`, `ManagedByRole`, `ConfirmRevokeGrant`.

### 14.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: View tabs (By User / All Grants) + conditional content.
  - **Users view**: User summary table (User, Email, Grant count badge, ViewGrants/AssignGrant buttons).
  - **Grants view**: Filter bar (GrantType dropdown with auto-submit, clear filters link) + detailed grants table (User link, GrantType, Scope, Capabilities badges, Source badge, GrantedAt date, Revoke button).
- **Badge system**: Auto (blue), Manual (green), Own (purple), Give (amber).
- **Interactive elements**: View mode tabs, filter dropdown with auto-submit, revoke button (only for non-auto grants, with confirm dialog).
- **CSS**: Inline styles.

### 14.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Grants`.
- **Nav targets**: `/Admin/EditProfile?UserId={id}` from user links, `/Admin/Organization/Grants/Assign?userId={id}` for assign.
- **How users reach**: Organization hub.

### 14.6 Data Dependencies & Side Effects
- **Data read**: Grants with user/grant type info, user grant summaries.
- **Data written**: Grant revocation.
- **Services**: `AppDbContext`, `IGrantService`.

### 14.7 Forms & Submissions
- **Filter form**: GET with ViewMode + FilterGrantTypeId. Auto-submit on dropdown change.
- **Revoke handler**: `OnPostRevokeAsync(int id)` with confirm dialog. Only available for non-auto grants.
- **Bottom card**: Link to `/Admin/Organization/Grants/Assign` for assigning new grants.

### 14.8 Interesting Behaviors
- **Auto-grant protection**: Auto-grants (IsAutoGrant=true) cannot be revoked manually; shows "ManagedByRole" label instead.
- **Dual view mode**: Seamless toggle between user-summary and detailed grant views.
- **Cross-linking**: User names link to EditProfile; ViewGrants filters to that user's grants.

### 14.9 Traceability
- **Key services**: `AppDbContext`, `IGrantService`.

---

## 15. Admin/Organization/Grants/Assign

### 15.1 Identity & Routing
- **Route**: `/Admin/Organization/Grants/Assign`
- **File**: `Pages/Admin/Organization/Grants/Assign.cshtml` + `Assign.cshtml.cs`
- **Purpose**: Assign a specific grant to a user with full scope selection.
- **Query params**: `userId` (pre-selects user).
- **Redirects**: Redirects to `/Admin/Organization/Grants` after successful assignment.

### 15.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:AssignGrants")]`
- **IgnoreQueryFilters**: Used for loading all users, grant types, and hierarchy entities for dropdowns. Audited safe.

### 15.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `AssignGrant`, `AssignGrantToUser`, `SelectUserAndGrant`, `GrantType`, `Capabilities`, `CanOwn`, `CanGive`, `Scope`, `ScopeDescription`, `Notes`, `GrantNotesPlaceholder`.

### 15.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Three cards: (1) User & Grant selection (user dropdown or pre-selected badge, grant type dropdown with description display, CanOwn/CanGive checkboxes), (2) Scope selection (6 dropdowns: Project, Area, Molecule, Company, Department, JobType), (3) Notes textarea. Action buttons at bottom.
- **Interactive elements**: Grant type dropdown fires JS to show/hide description below. User pre-selection via query param shows user badge instead of dropdown.
- **CSS**: Inline styles with scope section background, user-selected badge.

### 15.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Grants > Assign Grant`.
- **Nav targets**: Cancel goes to `/Admin/Organization/Grants`.
- **How users reach**: "Assign Grant" button from Grants Index page, or per-user "Assign Grant" link.

### 15.6 Data Dependencies & Side Effects
- **Data read**: Users, GrantTypes (with category and description), Projects, Areas, Molecules, Companies, Departments, JobTypes.
- **Data written**: New Grant record.
- **Duplicate check**: Verifies no existing grant with same user + grant type + scope before creating.
- **Services**: `AppDbContext`, `IGrantService`.

### 15.7 Forms & Submissions
- **Form fields**: SelectedUserId (required), SelectedGrantTypeId (required), CanOwn (checkbox), CanGive (checkbox), ScopeProjectId, ScopeAreaId, ScopeMoleculeId, ScopeCompanyId, ScopeDepartmentId, ScopeJobTypeId, Notes (textarea).
- **Handler**: `OnPostAsync` with duplicate grant check.
- **Grant type description**: JS listens to dropdown change and displays the selected option's `title` attribute in a div below.

### 15.8 Interesting Behaviors
- **Duplicate prevention**: Checks for existing grant with matching user + type + scope before creating.
- **Grant description display**: Interactive JS shows grant type description when selected (comment: `I-03: Show grant description below dropdown for discoverability`).
- **Six-dimensional scope**: Full hierarchy scope selection (Project/Area/Molecule/Company/Department/JobType).

### 15.9 Traceability
- **Key services**: `AppDbContext`, `IGrantService`.

---

## 16. Admin/Organization/Hierarchy/Index

### 16.1 Identity & Routing
- **Route**: `/Admin/Organization/Hierarchy`
- **File**: `Pages/Admin/Organization/Hierarchy/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Read-only organizational hierarchy tree view with stats.
- **Query params**: None.

### 16.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ViewHierarchy")]`
- **IgnoreQueryFilters**: Used for hierarchy tree queries. Audited safe.

### 16.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `Owner_Hierarchy`, `Owner_Hierarchy_Desc`, `Projects`, `Areas`, `Molecules`, `Companies`, `HierarchyTree`, `ManageEntities`.

### 16.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Page header with icon + stats bar (4 stat pills: Projects, Areas, Molecules, Companies) + quick-links to management pages + HierarchyTree ViewComponent + legacy quick-links section.
- **Interactive elements**: Expand/collapse handled by HierarchyTree component toolbar.
- **Shared components**: `HierarchyTree` ViewComponent with `mode = "full"` and `expandedByDefault = true`.
- **CSS**: Inline styles (bottom of page, not in `@section Styles`) with max-width 1200px container, stat pills, quick-links, responsive breakpoint at 768px.

### 16.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Hierarchy`.
- **Nav targets**: Quick-links to `/Admin/Organization/Projects`, `/Admin/Organization/Areas`, `/Admin/Organization/Molecules`, `/Admin/Companies`.
- **How users reach**: Organization hub.

### 16.6 Data Dependencies & Side Effects
- **Data read**: Hierarchy tree data (ProjectNode -> AreaNode -> MoleculeNode), stats (TotalProjects, TotalAreas, TotalMolecules, TotalCompanies).
- **Data written**: None (read-only).
- **Services**: `AppDbContext`.

### 16.7 Forms & Submissions
- None. Read-only page.

### 16.8 Interesting Behaviors
- **Duplicate quick-links**: Both near top and in a "Legacy Quick Links" card at bottom (possibly to be cleaned up).
- **HierarchyTree component**: Encapsulates tree rendering and expand/collapse logic; drag-drop support mentioned in comment.
- **Emoji icons**: Uses emoji for stat pills and quick-links (unlike most other admin pages).

### 16.9 Traceability
- **Key services**: `AppDbContext` (via view models built in code-behind).

---

## 17. Admin/Organization/JobTypes/Index

### 17.1 Identity & Routing
- **Route**: `/Admin/Organization/JobTypes`
- **File**: `Pages/Admin/Organization/JobTypes/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Job type CRUD with area/molecule scoping.
- **Query params**: None.

### 17.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManageJobTypes")]`
- **IgnoreQueryFilters**: Used for user counts, area/molecule queries. Audited safe.
- **Security audit comment**: `SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE -- requires Grant:ManageJobTypes policy; job type management is inherently cross-company hierarchy data`.

### 17.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageJobTypes`, `CreateNewJobType`, `JobTypesAreAreaScoped`, `AllJobTypes`, `AllMolecules_AreaWide`, `ConfirmDeleteJobType`.

### 17.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Info box explaining area-scoping + Create form (Area dropdown, Molecule dropdown (dynamic), Name, DisplayName, Color picker, SortOrder) + job types table.
- **Table columns**: Project, Area, Molecule (or "Area-wide"), JobType (DisplayName + key), Color swatch, Order, Users count, Status badge, Actions.
- **Interactive elements**: Dynamic molecule dropdown filtered by selected area (JS IIFE). Color picker input. Confirm dialog for delete. Delete only shown when UserCount==0.
- **CSS**: Inline styles with color-swatch display.

### 17.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > JobTypes`.
- **How users reach**: Organization hub or quick-links.

### 17.6 Data Dependencies & Side Effects
- **Data read**: Job types with hierarchy info via `IJobTypeService.GetAllJobTypesWithHierarchyAsync()`. User counts per job type. Areas with projects. Molecules with areas.
- **Data written**: Job type create (via `IJobTypeService.CreateJobTypeAsync` + additional property updates), toggle active (via `IJobTypeService.ToggleActiveAsync`), delete (via `IJobTypeService.DeleteJobTypeAsync`).
- **Dependency check**: Delete blocked if job type has users.
- **Services**: `AppDbContext`, `IJobTypeService`.

### 17.7 Forms & Submissions
- **Create form**: SelectedAreaId (required), SelectedMoleculeId (optional, filtered by area), JobTypeName (required), JobTypeDisplayName (optional), JobTypeColor (color picker), SortOrder (number, default 0).
- **Handlers**: `OnPostCreateAsync`, `OnPostToggleActiveAsync(int id)`, `OnPostDeleteAsync(int id)`.
- **Validation**: Name required, area must exist, molecule-area relationship validated.

### 17.8 Interesting Behaviors
- **Dynamic molecule dropdown**: JS IIFE filters molecules by selected area using `Json.Serialize(Model.AvailableMolecules)`.
- **Molecule-area validation**: Server validates that selected molecule belongs to selected area (if molecule is specified).
- **Two-step create**: First calls `IJobTypeService.CreateJobTypeAsync` for base properties, then updates additional properties (DisplayName, Color, SortOrder, MoleculeId) and saves again.

### 17.9 Traceability
- **Key services**: `IJobTypeService`, `AppDbContext`.

---

## 18. Admin/Organization/Molecules/Index

### 18.1 Identity & Routing
- **Route**: `/Admin/Organization/Molecules`
- **File**: `Pages/Admin/Organization/Molecules/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Molecule CRUD with type selection (Workforce/Tech/Helper).
- **Query params**: None.

### 18.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:EditMolecule")]`
- **IgnoreQueryFilters**: Used for area/molecule/company queries. Audited safe.

### 18.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageMolecules`, `CreateNewMolecule`, `AllMolecules`, `Workforce`, `Tech`, `Helper`, `ConfirmDeleteMolecule`.

### 18.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Create form (Area dropdown, Name, DisplayName, Type dropdown) + molecules table.
- **Table columns**: Project, Area, Name (DisplayName + key), Type badge (Workforce/Tech/Helper with distinct colors), Companies count, Departments count, Status badge, Actions.
- **Badge colors**: Workforce = primary-soft/primary, Tech = accent-soft/accent, Helper = warning-soft/warning.
- **Interactive elements**: Confirm dialog for delete. Delete only shown when CompanyCount==0 && DepartmentCount==0.
- **CSS**: Inline styles.

### 18.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Molecules`.
- **How users reach**: Organization hub or quick-links.

### 18.6 Data Dependencies & Side Effects
- **Data read**: Molecules with area/project info, company/department counts.
- **Data written**: Molecule create (auto-creates HQ company), toggle active, delete.
- **Side effects**: Creating a molecule auto-creates a headquarters (HQ) company. Auto-generates setup tasks via `ISetupTaskService`.
- **Dependency check**: Delete blocked if molecule has companies or departments.
- **Services**: `AppDbContext`, `ISetupTaskService`.

### 18.7 Forms & Submissions
- **Create form**: SelectedAreaId (required), MoleculeName (required), MoleculeDisplayName (optional), SelectedType (required: Workforce/Tech/Helper).
- **Handlers**: `OnPostCreateAsync`, `OnPostToggleActiveAsync(int id)`, `OnPostDeleteAsync(int id)`.

### 18.8 Interesting Behaviors
- **Auto-HQ company**: Creating a molecule automatically creates a headquarters company for it.
- **Auto-setup tasks**: Creation triggers setup task generation via `ISetupTaskService`.
- **Type-specific badges**: Helper method `GetMoleculeTypeBadgeClass` maps type to CSS class.

### 18.9 Traceability
- **Key services**: `AppDbContext`, `ISetupTaskService`.

---

## 19. Admin/Organization/Projects/Index

### 19.1 Identity & Routing
- **Route**: `/Admin/Organization/Projects`
- **File**: `Pages/Admin/Organization/Projects/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Project CRUD management (top of hierarchy).
- **Query params**: None.

### 19.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:EditArea")]`
- **IgnoreQueryFilters**: Used for project queries. Audited safe.
- **Note**: Uses `EditArea` policy (not a separate EditProject), as projects and areas share the same management permission.

### 19.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageProjects`, `CreateNewProject`, `AllProjects`, `ConfirmDeleteProject`.

### 19.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Create form (Name, DisplayName) + projects table.
- **Table columns**: Name (DisplayName + key), Areas count, Status badge, Created date, Actions.
- **Interactive elements**: Confirm dialog for delete. Delete only shown when AreaCount==0.
- **CSS**: Inline styles, standard section-card pattern.

### 19.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Projects`.
- **How users reach**: Organization hub or quick-links.

### 19.6 Data Dependencies & Side Effects
- **Data read**: Projects with area counts.
- **Data written**: Project create, toggle active, delete.
- **Dependency check**: Delete blocked if project has areas.
- **Services**: `AppDbContext`.

### 19.7 Forms & Submissions
- **Create form**: ProjectName (required), ProjectDisplayName (optional).
- **Handlers**: `OnPostCreateAsync`, `OnPostToggleActiveAsync(int id)`, `OnPostDeleteAsync(int id)`.
- **Validation**: Name required.

### 19.8 Interesting Behaviors
- **Simplest CRUD page**: Minimal form with just name fields. No complex validation.
- **Shared policy with Areas**: Uses `Grant:EditArea` rather than a dedicated project grant.

### 19.9 Traceability
- **Key services**: `AppDbContext`.

---

## 20. Admin/Organization/Roles/Index

### 20.1 Identity & Routing
- **Route**: `/Admin/Organization/Roles`
- **File**: `Pages/Admin/Organization/Roles/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Role template management with dual view modes (role templates / user assignments).
- **Query params**: `ViewMode` ("roles" or "assignments"), `FilterRoleId`, `FilterUserId`.

### 20.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:AssignRoles")]`
- **IgnoreQueryFilters**: Used for role/assignment queries. Audited safe.

### 20.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageRoles`, `ViewAndManageRoleAssignments`, `RoleTemplates`, `UserAssignments`, `System`, `Custom`, `ScopeLevel`, `ConfirmRevokeRole`.

### 20.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: View tabs (Role Templates / User Assignments) + conditional content.
  - **Roles view**: Role templates table (Role name + key, ScopeLevel badge, Type badge System/Custom, Assignment count, Auto-grant count, Status badge, ViewAssignments link).
  - **Assignments view**: Filter bar (Role dropdown with auto-submit, clear filters) + user role assignments table (User link, Role, Scope, Status, AssignedAt, AssignedBy, Revoke button).
- **Badge system**: System (accent), Custom (success), Active (success), Inactive (danger).
- **Interactive elements**: View mode tabs, filter dropdown, revoke button with confirm dialog (only for active assignments).
- **CSS**: Inline styles.

### 20.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Roles`.
- **Nav targets**: `/Admin/EditProfile?UserId={id}` from user links. ViewAssignments filters by role.
- **How users reach**: Organization hub.

### 20.6 Data Dependencies & Side Effects
- **Data read**: RoleTemplates with assignment/grant counts, UserRoles with scope descriptions.
- **Data written**: Role revocation via `IRoleService.RemoveRoleAsync` (soft delete + auto-grant cleanup).
- **Services**: `IRoleService`, `AppDbContext`.

### 20.7 Forms & Submissions
- **Filter form**: GET with ViewMode + FilterRoleId. Auto-submit on dropdown change.
- **Revoke handler**: `OnPostRevokeAsync(int id)` with confirm dialog. Triggers soft delete + auto-grant cleanup.
- **Bottom card**: Link to `/Admin/Organization/Roles/Assign`.

### 20.8 Interesting Behaviors
- **Role revocation cascade**: `IRoleService.RemoveRoleAsync` not only soft-deletes the role assignment but also cleans up auto-grants that were associated with that role.
- **System vs Custom**: System roles are seeded and cannot be deleted; Custom roles are user-created.
- **Dual view**: Seamless toggle between role catalog and assignment management.

### 20.9 Traceability
- **Key services**: `IRoleService`, `AppDbContext`.

---

## 21. Admin/Organization/Roles/Assign

### 21.1 Identity & Routing
- **Route**: `/Admin/Organization/Roles/Assign`
- **File**: `Pages/Admin/Organization/Roles/Assign.cshtml` + `Assign.cshtml.cs`
- **Purpose**: Assign a role template to a user with scope selection based on role's scope level.
- **Query params**: `userId` (pre-selects user).
- **Redirects**: Redirects to `/Admin/Organization/Roles?ViewMode=assignments` after success.

### 21.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:AssignRoles")]`
- **IgnoreQueryFilters**: Used for user/role/hierarchy loading. Audited safe.

### 21.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `AssignRole`, `AssignRoleToUser`, `SelectUserAndRole`, `RoleScope`, `RoleScopeExplanation`, `RoleDescriptionHint`, scope hints per level.

### 21.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Two cards: (1) User & Role selection (user dropdown or pre-selected badge, role dropdown with scope level badge and description tooltip), (2) Scope selection (5 dropdowns: Area, Molecule, Company, Department, JobType with dynamic required indicators). Action buttons.
- **Interactive elements**: Role dropdown fires JS `updateScopeVisibility()` which dynamically adds/removes required indicators on scope fields based on the selected role's `data-scope-level` attribute.
- **CSS**: Inline styles with scope-info box, scope-badge.

### 21.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > Roles > Assign Role`.
- **Nav targets**: Cancel goes to `/Admin/Organization/Roles`.
- **How users reach**: "Assign Role" button from Roles Index page.

### 21.6 Data Dependencies & Side Effects
- **Data read**: Users, RoleTemplates (with scope level), Areas, Molecules, Companies, Departments, JobTypes.
- **Data written**: UserRole assignment via `IRoleService.AssignRoleAsync` (triggers auto-grant creation).
- **Scope validation**: Server validates scope fields match the role's `RoleScopeLevel`.
- **Services**: `IRoleService`, `IJobTypeService`, `AppDbContext`.

### 21.7 Forms & Submissions
- **Form fields**: SelectedUserId (required), SelectedRoleId (required), ScopeAreaId, ScopeMoleculeId, ScopeCompanyId, ScopeDepartmentId, ScopeJobTypeId.
- **Handler**: `OnPostAsync`.
- **Dynamic validation**: JS updates which scope fields are required based on role's scope level (Company, CompanyJobType, Department, Molecule, MoleculeJobType, Area, Project, Implicit).

### 21.8 Interesting Behaviors
- **Scope level mapping**: JS object `scopeRequirements` maps each scope level to required scope groups:
  - `Implicit`: no scopes required
  - `Company`: Company required
  - `CompanyJobType`: Company + JobType required
  - `Department`: Department required
  - `Molecule`: Molecule required
  - `MoleculeJobType`: Molecule + JobType required
  - `Area`: Area required
  - `Project`: no scopes required (project-wide)
- **Auto-grant cascade**: Assigning a role triggers `IRoleService.AssignRoleAsync` which auto-creates grants defined in the role template.

### 21.9 Traceability
- **Key services**: `IRoleService`, `IJobTypeService`.

---

## 22. Admin/Organization/ShiftGroupings/Index

### 22.1 Identity & Routing
- **Route**: `/Admin/Organization/ShiftGroupings`
- **File**: `Pages/Admin/Organization/ShiftGroupings/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Shift grouping CRUD -- combines companies and job types into logical groups for scheduling.
- **Query params**: None.

### 22.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ManageShiftGroupings")]`
- **IgnoreQueryFilters**: Used for molecule/company/jobtype queries. Audited safe.

### 22.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ManageShiftGroupings`, `CreateNewShiftGrouping`, `ShiftGroupingsCombineCompaniesAndJobTypes`, `AllShiftGroupings`, `HoldCtrlToSelectMultiple`, `ConfirmDeleteGrouping`.

### 22.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Info box explaining groupings concept + Create form (Molecule dropdown, Name, DisplayName, multi-select Companies, multi-select JobTypes) + groupings table.
- **Table columns**: Molecule, Grouping (DisplayName + key), Companies count badge, JobTypes count badge, Status badge, Actions.
- **Interactive elements**: Multi-select `<select multiple>` for companies and job types with "Hold Ctrl to select multiple" hint. No delete dependency guard (delete always available).
- **CSS**: Inline styles with multi-select height.

### 22.5 Navigation Map
- **Breadcrumb**: `Admin > Organization > ShiftGroupings`.
- **How users reach**: Organization hub.

### 22.6 Data Dependencies & Side Effects
- **Data read**: ShiftGroupings with molecule info, company/jobtype counts. Available molecules, companies, job types.
- **Data written**: ShiftGrouping create (with company/jobtype associations), toggle active, delete.
- **Services**: `IShiftGroupingService`, `IJobTypeService`, `AppDbContext`.

### 22.7 Forms & Submissions
- **Create form**: SelectedMoleculeId (required), GroupingName (required), GroupingDisplayName (optional), SelectedCompanyIds (multi-select), SelectedJobTypeIds (multi-select).
- **Handlers**: `OnPostCreateAsync`, `OnPostToggleActiveAsync(int id)`, `OnPostDeleteAsync(int id)`.

### 22.8 Interesting Behaviors
- **Multi-select pattern**: Uses native HTML `<select multiple>` (no JS enhancement), with `HoldCtrlToSelectMultiple` hint.
- **No delete guard**: Unlike other entity pages, shift groupings can always be deleted (no dependency check).
- **Association pattern**: Creates grouping entity plus associated ShiftGroupingCompany and ShiftGroupingJobType junction records.

### 22.9 Traceability
- **Key services**: `IShiftGroupingService`, `IJobTypeService`.

---

## 23. Admin/Settings/Index

### 23.1 Identity & Routing
- **Route**: `/Admin/Settings`
- **File**: `Pages/Admin/Settings/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Hierarchical settings management (Area/Molecule/Company level) with cascading inheritance.
- **Query params**: `Level` ("area", "molecule", "company"), `SelectedId` (entity ID).

### 23.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:ViewSettings")]`
- **IgnoreQueryFilters**: Used for area/molecule/company dropdown queries. Audited safe.
- **Security audit comment**: `SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE -- requires Grant:ViewSettings policy; hierarchy dropdowns (Areas, Molecules, Companies) are reference data for settings navigation`.

### 23.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `HierarchySettings`, `HierarchySettingsDescription`, `AreaSettings`, `MoleculeSettings`, `CompanySettings`, `SettingsCascadeInfo`, `EffectiveSettings`, `RestHoursBetweenShifts`, `WeeklyHoursCap`, `ClearOverrideInheritFromParent`, `SaveSettings`.

### 23.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: View tabs (Area / Molecule / Company) + info box about cascade + entity selector dropdown (auto-submit on change). When entity selected: Effective Settings card (for molecule/company only, showing resolved values with source badges) + Edit Settings card (RestHours, WeeklyCap inputs with clear-override checkboxes for molecule/company, required for area). Last-updated metadata.
- **Source badges**: Area (accent), Molecule (primary), Company (success), Default (border/muted).
- **Interactive elements**: Level tabs, entity selector with auto-submit, clear-override checkboxes.
- **CSS**: Inline styles with settings-grid, setting-card, source-badge system.

### 23.5 Navigation Map
- **Breadcrumb**: `Admin > Settings`.
- **How users reach**: Admin Hub card.

### 23.6 Data Dependencies & Side Effects
- **Data read**: Areas, Molecules, Companies (for dropdowns). Area/Molecule/Company settings. Effective settings (resolved through cascade).
- **Data written**: Area settings (RestHours, WeeklyCap). Molecule settings override (nullable). Company settings override (nullable).
- **Services**: `IHierarchySettingsService`, `AppDbContext`.

### 23.7 Forms & Submissions
- **Entity selector**: GET form with Level + SelectedId. Auto-submit on dropdown change.
- **Settings form**: POST with Level + SelectedId + EditRestHours + EditWeeklyCap + ClearRestHoursOverride + ClearWeeklyCapOverride.
- **Handler**: `OnPostAsync` with switch on Level.
- **Validation**: Area level requires both RestHours and WeeklyCap. Molecule/Company levels allow null (inherits from parent).

### 23.8 Interesting Behaviors
- **Cascading settings**: Settings flow Area -> Molecule -> Company. Lower levels can override or inherit from parent.
- **Clear override**: Checkboxes allow clearing molecule/company overrides to inherit from parent.
- **Effective settings display**: Shows resolved values with source attribution badges (which level the value comes from).
- **Last updated**: Shows timestamp and user who last updated the settings.
- **Razor `@functions` block**: Contains `GetSourceClass(string source)` helper for source badge CSS class mapping.

### 23.9 Traceability
- **Key services**: `IHierarchySettingsService`, `AppDbContext`.

---

## 24. Admin/Settings/ApprovalRules

### 24.1 Identity & Routing
- **Route**: `/Admin/Settings/ApprovalRules`
- **File**: `Pages/Admin/Settings/ApprovalRules.cshtml` + `ApprovalRules.cshtml.cs`
- **Purpose**: Vacation approval rule CRUD management with orphaned rule detection.
- **Query params**: None.

### 24.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:SystemConfiguration")]`
- **Tenant scope**: Rules scoped by CompanyId from user claims. Approvers list scoped to same company.
- **IgnoreQueryFilters**: Used internally by `IJobTypeService` for job type dropdown. Audited safe.
- **Security audit comment**: `SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE -- requires Grant:SystemConfiguration policy; rule CRUD operations are scoped by CompanyId from user claims; orphaned rule detection is company-scoped`.

### 24.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `ApprovalRules`, `ApprovalRulesDescription`, `CreateRule`, `ExistingRules`, `OrphanedRulesWarning`, `JobType`, `Approver`, `ApprovalRoute`, `AutoApprove`, `Priority`, `ExtendedLeaveThreshold`, `SecondApproverGrant`, `RequiresSecondApproval`, `ConfirmDeleteRule`.

### 24.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: Orphaned rules warning banner (conditionally shown) + Create rule form card (8 fields in grid) + Existing rules table card.
- **Warning styles**: `.alert-warning` for orphaned rules with itemized list.
- **Table columns**: #, JobType (or "Default All" badge), ApprovalRoute, Approver, AutoApprove (days), Priority, Status badge, Actions (Delete with confirm).
- **Interactive elements**: Delete with confirm dialog. Explicit `@Html.AntiForgeryToken()` in forms.
- **CSS**: Inline styles with `.rules-table`, `.alert-warning`.

### 24.5 Navigation Map
- **Breadcrumb**: `Admin > Settings > ApprovalRules`.
- **How users reach**: Admin Hub card (conditionally via VacationApprovalEnabled feature flag on Index), or Settings page link.

### 24.6 Data Dependencies & Side Effects
- **Data read**: `IVacationApprovalService.GetRulesForCompanyAsync(companyId)`, `DetectOrphanedRulesAsync(companyId)`. Job types. Approvers (Manager/Director/Owner users in company).
- **Data written**: Rule CRUD (create, update, delete) via `IVacationApprovalService`.
- **Services**: `IVacationApprovalService`, `IJobTypeService`, `AppDbContext`.

### 24.7 Forms & Submissions
- **Create rule form**: JobTypeId (optional, 0 = default all), ApproverUserId (optional, 0 = any with grant), ApproverGrantKey (default "ApproveVacations"), MaxAutoApproveDays (0-365), Priority (0-100), ExtendedLeaveDaysThreshold (1-365), SecondApproverGrantKey (optional), RequiresSecondApproval (checkbox).
- **Handlers**: `OnPostCreateAsync`, `OnPostUpdateAsync`, `OnPostDeleteAsync(int ruleId)`.
- **Explicit anti-forgery**: Uses `@Html.AntiForgeryToken()` explicitly (unlike most other pages that rely on `asp-page-handler` auto-generation).

### 24.8 Interesting Behaviors
- **Orphaned rule detection**: Automatically detects rules referencing deleted/invalid job types or users, showing a warning banner.
- **Approval workflow complexity**: Rules support auto-approval thresholds, specific vs grant-based approver routing, second approval for extended leave, priority ordering.
- **Company-scoped**: Rules are strictly company-scoped via `GetCompanyId()` from claims. No cross-company rule management.
- **Default grant key**: If ApproverGrantKey is empty, defaults to "ApproveVacations".

### 24.9 Traceability
- **Key services**: `IVacationApprovalService`, `IJobTypeService`.

---

## 25. Admin/SetupTasks/Index

### 25.1 Identity & Routing
- **Route**: `/Admin/SetupTasks`
- **File**: `Pages/Admin/SetupTasks/Index.cshtml` + `Index.cshtml.cs`
- **Purpose**: Setup task tracking with three view modes (pending tasks, all tasks, progress by molecule).
- **Query params**: `ViewMode` ("pending", "all", "progress"), `FilterMoleculeId`.
- **Feature gate**: Returns `NotFound()` if `SetupTasksEnabled` flag is disabled.

### 25.2 Access Control & Scope
- **Policy**: `[Authorize(Policy = "Grant:SystemConfiguration")]`
- **Feature flag**: `SetupTasksEnabled` (hard gate, returns NotFound).
- **IgnoreQueryFilters**: Used for molecule/task queries. Audited safe.
- **Security audit comment**: `SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE -- requires Grant:SystemConfiguration policy; molecule list and setup task tracking are system-wide administrative data`.

### 25.3 Localization
- **Pattern**: `<loc key="...">` + `@Localizer["..."]`.
- **Key patterns**: `SetupTasks`, `SetupTasksDescription`, `MyPendingTasks`, `AllTasks`, `Progress`, `SetupProgressByMolecule`, `Pending`, `InProgress`, `Completed`, `Skipped`, `Start`, `MarkComplete`, `Skip`, `SuggestedUser`, `AssignedTo`, `CompletedAt`.

### 25.4 UI & Design Inventory
- **Layout**: `_Layout`.
- **Structure**: View tabs (My Pending Tasks / All Tasks / Progress) + conditional content:
  - **Pending view**: Task cards with title, context badges (molecule/company/jobtype), status badge, description, suggested user, action buttons (Start, MarkComplete, Skip).
  - **All tasks view**: Molecule filter dropdown + task cards with full status and completion info.
  - **Progress view**: Progress cards per molecule with progress bar, completion percentage, stat breakdown (completed/pending/in-progress/skipped/total), "View Tasks" link.
- **Badge system**: Pending (warning), InProgress (primary), Completed (success), Skipped (muted).
- **Interactive elements**: View mode tabs, molecule filter with auto-submit, Start/Complete/Skip action buttons per task.
- **CSS**: Inline styles with task-card, progress-card, progress-bar, context-badge.

### 25.5 Navigation Map
- **Breadcrumb**: `Admin > SetupTasks`.
- **Nav targets**: Progress view links to `?ViewMode=all&FilterMoleculeId={id}`.
- **How users reach**: Admin Hub card (conditionally shown via feature flag).

### 25.6 Data Dependencies & Side Effects
- **Data read**: Setup tasks (pending for user, all for molecule, progress summaries). Molecules.
- **Data written**: Task status updates (Pending -> InProgress -> Completed/Skipped).
- **Services**: `ISetupTaskService`, `IFeatureFlagService`, `AppDbContext`.

### 25.7 Forms & Submissions
- **Filter form**: GET with ViewMode + FilterMoleculeId. Auto-submit on dropdown change.
- **Task actions**: POST handlers per task: `OnPostCompleteAsync(int id)`, `OnPostSkipAsync(int id)`, `OnPostStartAsync(int id)`.
- **State transitions**: Pending -> InProgress (Start), Pending/InProgress -> Completed (MarkComplete), Pending/InProgress -> Skipped (Skip).

### 25.8 Interesting Behaviors
- **Feature flag gate**: Hard 404 when SetupTasksEnabled is disabled.
- **Three view modes**: Personal pending queue, administrative all-tasks browser, and progress dashboard.
- **Suggested user**: Tasks can have a suggested user with reason, displayed in pending view.
- **Progress visualization**: Per-molecule progress bars with percentage completion.
- **Context badges**: Task cards show molecule, company, and job type context as small inline badges.

### 25.9 Traceability
- **Key services**: `ISetupTaskService`, `IFeatureFlagService`.

---

## Cross-Cutting Observations

### Shared Patterns Across All 25 Pages

1. **Base class**: All pages extend `LocalizedPageModel` (provides `_localizer`, `Success`, `Error` properties).
2. **Localization**: Universal use of `<loc key="...">` tag helper and `@Localizer["..."]` inline. No direct Hebrew strings in code.
3. **Breadcrumb**: All pages use `@await Component.InvokeAsync("Breadcrumb", ...)` ViewComponent.
4. **Layout**: All pages use `_Layout` with `ViewData["Title"]`.
5. **Message pattern**: TempData["SuccessMessage"]/TempData["ErrorMessage"] for POST-Redirect-GET message passing. `Success`/`Error` properties from base class for display.
6. **Alert styling**: Consistent `.alert-success` (green) and `.alert-error` (red) with icon + text.
7. **Section cards**: `.section-card` pattern with `.section-header` for content grouping.
8. **CSS tokens**: All pages use CSS custom properties (`--primary`, `--text`, `--border`, `--surface`, `--success`, `--danger`, etc.).
9. **Responsive**: Most pages include `@media (max-width: 768px)` breakpoints.
10. **Grant-based authorization**: Every page uses `[Authorize(Policy = "Grant:...")]`. Runtime grant checks for advanced scoping.
11. **IgnoreQueryFilters**: Used in admin pages for cross-company data access. All instances have `SECURITY-AUDITED` comments.
12. **Inline CSS**: All pages embed CSS via `@section Styles { <style>... }` rather than external stylesheets. This results in significant CSS duplication across pages (shared patterns like `.btn`, `.data-table`, `.form-input` etc. are redefined in each page).

### Feature-Flagged Pages
- `DutyRotation/Index` -- gated by `DutyRotationEnabled`
- `SetupTasks/Index` -- gated by `SetupTasksEnabled`
- `Settings/ApprovalRules` -- accessible but contextually linked via `VacationApprovalEnabled`

### Pages With Complex Scoping Logic
- `Admin/Index` -- three-tier stat scoping (Owner/Director/Manager)
- `Admin/Users` -- three-tier user/join-request scoping with fallback chain
- `Admin/Organization/Index` -- per-molecule scoped authorization for POST handlers

### Pages With Transactional Operations
- `Admin/Companies` -- multi-step company creation and 11-step cascade delete
- `Admin/Users` -- user deactivation with multi-table cleanup

### Service Usage Frequency
Most referenced services across all 25 pages:
- `AppDbContext` (direct usage): 20+ pages
- `IGrantService`: 5 pages
- `IRoleService`: 5 pages
- `IJobTypeService`: 5 pages
- `IFeatureFlagService`: 3 pages
- `IAuditLogService`: 3 pages
- `ISetupTaskService`: 3 pages
- `IConcurrencyService`: 2 pages
- `IHierarchyService`: 2 pages
- `ICompanyCacheService`: 3 pages
