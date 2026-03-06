# ShiftManager — Complete Feature Inventory
*Auto-generated from codebase analysis on 2026-02-25*

## How to Use This Document
This is a verification checklist. Each feature has a checkbox. A QA agent should go through each item and verify it works. File paths are absolute from the project root.

---

## 1. Authentication & Session Management

### 1.1 Local Password Login
- [ ] **What it does**: Users authenticate with email + password
- [ ] **Where in code**: `Pages/Auth/Login.cshtml`, `Pages/Auth/Login.cshtml.cs`
- [ ] **How it works**: Validates credentials against `AppUser` table using `PasswordHasher.Verify()`, creates cookie-based `ClaimsPrincipal` with `ClaimTypes.NameIdentifier`, `ClaimTypes.Name`, `ClaimTypes.Role`, `CompanyId`, `MoleculeId`, `AreaId`, `ProjectId`, `JobTypeId`, `JobTypeName`, `RoleTemplateKey`, `IsWorkforce`, `IsTech`, `DepartmentId`
- [ ] **User-facing behavior**: Login form with email/password fields; redirects to `/Home/Index` (Owner) or `/` (others) on success
- [ ] **Sub-features**:
  - [ ] Auth required prompt when redirected (`reason=authRequired` query param)
  - [ ] Return URL support after login
  - [ ] Login-time RoleTemplate backfill for users without a template
  - [ ] Login-time grant reconciliation (`ApplyAutoGrantsAsync`)
  - [ ] Redirect to `/Auth/ForgotPassword` if `MustChangePassword` is set
  - [ ] Redirect to `/My/Onboarding` if `HasCompletedOnboarding` is false

### 1.2 Account Lockout Protection
- [ ] **What it does**: Locks accounts after failed login attempts
- [ ] **Where in code**: `Pages/Auth/Login.cshtml.cs` (lines 190-244)
- [ ] **How it works**: Tracks `FailedLoginAttempts` per user; locks account for 3 minutes after 10 failed attempts; resets on successful login
- [ ] **User-facing behavior**: Warning message after 3+ failed attempts; locked account shows minutes remaining; Owner can unlock via `/Owner/LockedUsers`
- [ ] **Sub-features**:
  - [ ] Failed attempt counter display (e.g., "attempt 5/10")
  - [ ] Lockout end timestamp (`LockoutEnd` field)
  - [ ] Automatic reset on successful login

### 1.3 Rate Limiting on Login
- [ ] **What it does**: Prevents brute-force login attacks
- [ ] **Where in code**: `Pages/Auth/Login.cshtml.cs` (lines 137-159), `Services/RateLimitingService.cs`
- [ ] **How it works**: Per-IP rate limit (10 attempts/15 min) AND per-account rate limit (15 attempts/15 min)
- [ ] **User-facing behavior**: "Rate limit exceeded" error message
- [ ] **Sub-features**:
  - [ ] Per-IP rate limiting key `login:ip:{ip}`
  - [ ] Per-account rate limiting key `login:account:{email}`
  - [ ] Rate limit reset on successful login

### 1.4 Input Validation on Login
- [ ] **What it does**: Validates login form inputs
- [ ] **Where in code**: `Pages/Auth/Login.cshtml.cs` (lines 162-180)
- [ ] **How it works**: Checks for empty fields, oversized input (email > 255, password > 500), invalid email format via `IValidationService`
- [ ] **User-facing behavior**: Localized error messages for each validation failure

### 1.5 Griffin ADFS SSO
- [ ] **What it does**: Enterprise SSO via Griffin ADFS authentication server
- [ ] **Where in code**: `Pages/Auth/Login.cshtml.cs` (OnPostGriffinAsync), `Pages/Auth/GriffinCallback.cshtml.cs`, `Services/GriffinService.cs`, `Services/GriffinConfigService.cs`, `Middleware/GriffinAuthenticationMiddleware.cs`
- [ ] **How it works**: Redirects to Griffin ADFS with triple-encoded callback URL; GriffinCallback processes the token; GriffinAuthenticationMiddleware reads `griffin.token` cookie
- [ ] **User-facing behavior**: "Login with Griffin ADFS" button on login page (shown only if configured and reachable); unavailable message if configured but unreachable
- [ ] **Sub-features**:
  - [ ] Griffin config validation (BaseUrl, TokenConsumerUrl must have http/https scheme)
  - [ ] Connection test before showing button
  - [ ] Griffin diagnostic page at `/GriffinDiagnostic`
  - [ ] Griffin API logging (`GriffinApiLog` entity)

### 1.6 Logout
- [ ] **What it does**: Signs out the user and clears the auth cookie
- [ ] **Where in code**: `Pages/Auth/Logout.cshtml`, `Pages/Auth/Logout.cshtml.cs`
- [ ] **How it works**: Calls `HttpContext.SignOutAsync()`, redirects to `/Auth/Login`
- [ ] **User-facing behavior**: POST-only logout (CSRF protected)

### 1.7 Session Monitoring
- [ ] **What it does**: Checks session validity and redirects to login if expired
- [ ] **Where in code**: `wwwroot/js/session-check.js`, `Pages/Api/SessionStatus.cshtml.cs`
- [ ] **How it works**: JavaScript periodically calls `/Api/SessionStatus` to check if session is valid; redirects to login if session expired
- [ ] **User-facing behavior**: Automatic redirect to login when session expires; only loaded on authenticated pages

### 1.8 Forced Password Change
- [ ] **What it does**: Requires users to change their password
- [ ] **Where in code**: `Pages/Auth/ForgotPassword.cshtml`, `Pages/Auth/ForgotPassword.cshtml.cs`
- [ ] **How it works**: `MustChangePassword` flag on AppUser; redirect at login time; `OnPostChangePasswordAsync` handler
- [ ] **User-facing behavior**: Form to set new password; validates password strength
- [ ] **Sub-features**:
  - [ ] ForgotPassword page (OnGet)
  - [ ] Password reset request (OnPostAsync)
  - [ ] Change password for logged-in user (OnPostChangePasswordAsync)

### 1.9 Cookie Configuration
- [ ] **What it does**: Secure cookie settings for auth
- [ ] **Where in code**: `Program.cs` (lines 149-175)
- [ ] **How it works**: Cookie named `shiftmgr.auth`, 7-day expiration with sliding, HttpOnly, SameSite=Lax
- [ ] **User-facing behavior**: Persistent login for 7 days

---

## 2. User Registration & Signup

### 2.1 Public Signup Page
- [ ] **What it does**: Allows new users to register
- [ ] **Where in code**: `Pages/Auth/Signup.cshtml`, `Pages/Auth/Signup.cshtml.cs`
- [ ] **How it works**: Gated by `AllowPublicSignup` feature flag; creates `UserJoinRequest` (not an active user); cascading dropdowns for Molecule -> Company -> JobType -> RoleTemplate
- [ ] **User-facing behavior**: Form with email, display name, password, molecule, company, job type, and role selection
- [ ] **Sub-features**:
  - [ ] Feature flag gating (`FF_ALLOW_PUBLIC_SIGNUP`)
  - [ ] Duplicate email detection (existing user or pending request)
  - [ ] Rate limiting on signup
  - [ ] Cascading dropdown API: `/Api/Signup/GetSignupOptions` (OnGetMoleculesAsync, OnGetCompaniesAsync, OnGetJobTypesAsync, OnGetAllJobTypesAsync, OnGetRoleTemplatesAsync)
  - [ ] Role template selection with visibility filter (`IsVisibleInSignup`)
  - [ ] HQ companies excluded from signup

### 2.2 Join Request Approval
- [ ] **What it does**: Owners/admins approve or reject signup requests
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostApproveJoinRequestAsync, OnPostRejectJoinRequestAsync, OnPostBatchApproveJoinRequestsAsync)
- [ ] **How it works**: Approve creates an active AppUser from the join request; assigns RoleTemplate and auto-grants; reject sends notification
- [ ] **User-facing behavior**: Pending requests shown on Admin/Users page with Approve/Reject buttons
- [ ] **Sub-features**:
  - [ ] Single approve with optional role template override
  - [ ] Batch approve multiple requests
  - [ ] Rejection with reason text
  - [ ] Notification sent on approval/rejection (`AccessRequestApproved` type)
  - [ ] Audit logging of approval/rejection

---

## 3. User Management

### 3.1 User List & Search
- [ ] **What it does**: Lists all users in the company with search and pagination
- [ ] **Where in code**: `Pages/Admin/Users.cshtml`, `Pages/Admin/Users.cshtml.cs` (OnGetAsync)
- [ ] **How it works**: Loads users with company filter, search by name/email, pagination
- [ ] **User-facing behavior**: Paginated table with user details, role, job type, and action buttons
- [ ] **Sub-features**:
  - [ ] Search by name or email
  - [ ] Pagination
  - [ ] Filter by active/inactive
  - [ ] Shows pending join requests

### 3.2 Add User
- [ ] **What it does**: Admin creates a new user directly
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostAddAsync)
- [ ] **How it works**: Creates AppUser with email, display name, password, role, job type; assigns RoleTemplate and auto-grants
- [ ] **User-facing behavior**: Form in a modal or inline

### 3.3 Toggle User Active/Inactive
- [ ] **What it does**: Activates or deactivates a user
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostToggleAsync)
- [ ] **How it works**: Toggles `IsActive` flag; deactivating a user prevents login

### 3.4 Change User Role
- [ ] **What it does**: Changes a user's role template
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostRoleAsync)
- [ ] **How it works**: Updates `RoleTemplateId`, derives `UserRole` from template, applies auto-grants, creates `RoleAssignmentAudit`
- [ ] **User-facing behavior**: Role dropdown with available role templates

### 3.5 Change User JobType
- [ ] **What it does**: Changes a user's job type assignment
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostJobTypeAsync)
- [ ] **How it works**: Updates `JobTypeId` on the user

### 3.6 Reset User Password
- [ ] **What it does**: Admin resets a user's password
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostResetPasswordAsync)
- [ ] **How it works**: Creates new password hash/salt, optionally sets `MustChangePassword`
- [ ] **User-facing behavior**: Password reset form with new password field

### 3.7 Unlock User Account
- [ ] **What it does**: Manually unlocks a locked-out user
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostUnlockAccountAsync)
- [ ] **How it works**: Clears `FailedLoginAttempts` and `LockoutEnd`

### 3.8 Delete User
- [ ] **What it does**: Permanently deletes a user
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostDeleteUserAsync)
- [ ] **How it works**: Removes user and related data from database

### 3.9 Export Users to CSV
- [ ] **What it does**: Exports user list as CSV file
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnGetExportCsvAsync)
- [ ] **How it works**: Generates CSV with user data for download

### 3.10 Bulk Import Users
- [ ] **What it does**: Imports multiple users from a file
- [ ] **Where in code**: `Pages/Admin/Users.cshtml.cs` (OnPostBulkImportAsync), `Services/ImportService.cs`
- [ ] **How it works**: Parses uploaded file, creates users in batch

### 3.11 Edit User Profile (Admin)
- [ ] **What it does**: Admin edits another user's profile
- [ ] **Where in code**: `Pages/Admin/EditProfile.cshtml`, `Pages/Admin/EditProfile.cshtml.cs`
- [ ] **How it works**: Full profile editing form including personal info, professional info, emergency contacts, military rank, avatar
- [ ] **Sub-features**:
  - [ ] Personal info: preferred name, phone, city, date of birth
  - [ ] Professional info: department, job title, hire date, skills (JSON), certifications (JSON)
  - [ ] Military rank selection (`MilitaryRank` enum)
  - [ ] Emergency contact fields
  - [ ] Avatar upload/delete (OnPostDeleteAvatarAsync)
  - [ ] Profile change audit logging (`ProfileChangeAudit`)

---

## 4. Authorization & Grants

### 4.1 Grant-Based Authorization System
- [ ] **What it does**: Fine-grained permissions via 107+ grant types
- [ ] **Where in code**: `Services/GrantService.cs`, `Authorization/GrantAuthorizationHandler.cs`, `Authorization/GrantPolicyProvider.cs`, `Authorization/GrantRequirement.cs`
- [ ] **How it works**: `GrantPolicyProvider` dynamically resolves `Grant:{GrantTypeKey}` policies; `GrantAuthorizationHandler` checks `HasGrantAsync`; grants have hierarchical scope matching (Project > Area > Molecule > Company > Department)
- [ ] **User-facing behavior**: Pages and features are shown/hidden based on user's grants
- [ ] **Sub-features**:
  - [ ] Grant categories: Shift, Duty, Chore, Vacation, Swap, UserManagement, GrantManagement, Hierarchy, Admin, System, Notification, Data, Overview, Report, Calendar, Navigation, Config, Director
  - [ ] Grant scope levels: Self, Company, Molecule, Department, Area, Project
  - [ ] CanOwn and CanGive delegation flags
  - [ ] Auto-grants from RoleTemplate
  - [ ] Individual grant assignment

### 4.2 Role Templates
- [ ] **What it does**: Predefined bundles of grants for common roles
- [ ] **Where in code**: `Pages/Owner/Hub/RoleTemplates/Index.cshtml`, `Edit.cshtml`, `Create.cshtml`, `Data/SeedData/RoleTemplateSeed.cs`
- [ ] **How it works**: RoleTemplates have Key, DisplayNameEN/HE, DerivedUserRole, ScopeLevel, IsSystem, IsVisibleInSignup; each has associated RoleTemplateGrants
- [ ] **User-facing behavior**: Owner Hub > Role Templates page for viewing/editing templates
- [ ] **Sub-features**:
  - [ ] Create custom role templates (OnPostAsync in Create.cshtml.cs)
  - [ ] Edit template metadata (OnPostMetadataAsync)
  - [ ] Edit template labels (OnPostLabelsAsync)
  - [ ] Add/update/remove grants from template (OnPostAddGrantAsync, OnPostUpdateGrantAsync, OnPostRemoveGrantAsync)
  - [ ] JobType-specific labels (`RoleTemplateJobTypeLabel`)
  - [ ] System templates are read-only

### 4.3 Seeded Role Templates
- [ ] **What it does**: Pre-configured roles for the military org
- [ ] **Where in code**: `Data/SeedData/RoleTemplateSeed.cs`
- [ ] **How it works**: Seeds templates like AlhutLead, TextLead, AlhutSoldier, TextSoldier, BRDirector, HakamDirector, AreaAdmin, MoleculeAdmin, Owner, Trainee, Assigner, TechLead, TechSoldier
- [ ] **Sub-features**:
  - [ ] Each template maps to a DerivedUserRole
  - [ ] Each has pre-configured grant mappings
  - [ ] Some have dual-JobType grants (e.g., BRDirector has grants for both BR and Hakam)
  - [ ] TargetJobTypeId sentinel resolution at startup

### 4.4 Grant Management UI (Owner Hub)
- [ ] **What it does**: Manage individual user grants
- [ ] **Where in code**: `Pages/Owner/Hub/Grants.cshtml`, `Pages/Owner/Hub/Grants.cshtml.cs`
- [ ] **How it works**: Search users, view their grants, assign/revoke individual grants, update delegation flags
- [ ] **User-facing behavior**: Owner Hub > Grants page with user search, grant list, and management actions
- [ ] **Sub-features**:
  - [ ] Search users (OnGetSearchUsersAsync)
  - [ ] View user grants (OnGetUserGrantsAsync)
  - [ ] Assign individual grant (OnPostAssignGrantAsync)
  - [ ] Revoke grant (OnPostRevokeGrantAsync)
  - [ ] Update CanOwn/CanGive delegation (OnPostUpdateGrantDelegationAsync)
  - [ ] View role template details (OnGetRoleTemplateAsync)
  - [ ] Add/update/remove role template grants (OnPostAddRoleTemplateGrantAsync, OnPostUpdateRoleTemplateGrantAsync, OnPostRemoveRoleTemplateGrantAsync)
  - [ ] View grant type usage (OnGetGrantTypeUsageAsync)
  - [ ] Apply owner godmode grants (OnPostApplyOwnerGrantsAsync)
  - [ ] Apply user management grants (OnPostApplyUserManagementGrantsAsync)

### 4.5 Organization-Level Grant Management
- [ ] **What it does**: View and manage grants at the organization level
- [ ] **Where in code**: `Pages/Admin/Organization/Grants/Index.cshtml.cs`, `Pages/Admin/Organization/Grants/Assign.cshtml.cs`
- [ ] **How it works**: Lists grants for users in the organization, assign new grants, revoke existing
- [ ] **Sub-features**:
  - [ ] View all grants (OnGetAsync)
  - [ ] Assign grant to user (OnPostAsync in Assign.cshtml.cs)
  - [ ] Revoke grant (OnPostRevokeAsync)

### 4.6 Role Assignment (Organization Level)
- [ ] **What it does**: Assign and manage role templates at org level
- [ ] **Where in code**: `Pages/Admin/Organization/Roles/Index.cshtml.cs`, `Pages/Admin/Organization/Roles/Assign.cshtml.cs`
- [ ] **How it works**: Lists users with roles, assign role templates, revoke role assignments
- [ ] **Sub-features**:
  - [ ] View all role assignments (OnGetAsync)
  - [ ] Assign role template (OnPostAsync)
  - [ ] Revoke role assignment (OnPostRevokeAsync)

---

## 5. Organization Hierarchy

### 5.1 Hierarchy Structure
- [ ] **What it does**: Multi-level organizational structure
- [ ] **Where in code**: `Models/Project.cs`, `Models/Area.cs`, `Models/Molecule.cs`, `Models/Company.cs`, `Models/Department.cs`, `Services/HierarchyService.cs`
- [ ] **How it works**: Project -> Area -> Molecule -> Company -> Department; Molecules have types (Workforce, Tech, System, Support)
- [ ] **User-facing behavior**: Hierarchy tree visualization in Admin > Organization

### 5.2 Hierarchy Tree View
- [ ] **What it does**: Visual tree of the organizational hierarchy
- [ ] **Where in code**: `Pages/Admin/Organization/Hierarchy/Index.cshtml`, `ViewComponents/HierarchyTreeViewComponent.cs`, `Pages/Shared/Components/HierarchyTree/Default.cshtml`
- [ ] **How it works**: Renders a tree with Project > Area > Molecule nodes
- [ ] **Sub-features**:
  - [ ] Inline create/rename/delete via API endpoints
  - [ ] Drag-and-drop reordering (API: `/Api/Hierarchy/Reorder`)
  - [ ] Move entities between parents (API: `/Api/Hierarchy/Move`, `/Api/Hierarchy/MoveTargets`)
  - [ ] Quick actions per node

### 5.3 Hierarchy CRUD APIs
- [ ] **What it does**: Backend API for hierarchy manipulation
- [ ] **Where in code**: `Pages/Api/Hierarchy/Create.cshtml.cs`, `Delete.cshtml.cs`, `Move.cshtml.cs`, `Rename.cshtml.cs`, `Reorder.cshtml.cs`, `MoveTargets.cshtml.cs`
- [ ] **How it works**: POST-based JSON APIs for create, delete, move, rename, reorder operations
- [ ] **Sub-features**:
  - [ ] Create hierarchy node (Project, Area, Molecule, Company)
  - [ ] Delete node with dependency checks
  - [ ] Move node to new parent
  - [ ] Get valid move targets
  - [ ] Rename node
  - [ ] Reorder children

### 5.4 Project Management
- [ ] **What it does**: CRUD for Projects (top-level entity)
- [ ] **Where in code**: `Pages/Admin/Organization/Projects/Index.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Create project (OnPostCreateAsync)
  - [ ] Toggle active (OnPostToggleActiveAsync)
  - [ ] Delete project (OnPostDeleteAsync)

### 5.5 Area Management
- [ ] **What it does**: CRUD for Areas (under Projects)
- [ ] **Where in code**: `Pages/Admin/Organization/Areas/Index.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Create area (OnPostCreateAsync)
  - [ ] Toggle active (OnPostToggleActiveAsync)
  - [ ] Delete area (OnPostDeleteAsync)

### 5.6 Molecule Management
- [ ] **What it does**: CRUD for Molecules (under Areas)
- [ ] **Where in code**: `Pages/Admin/Organization/Molecules/Index.cshtml.cs`
- [ ] **How it works**: Molecules have types: Workforce, Tech, System, Support (`MoleculeType` enum)
- [ ] **Sub-features**:
  - [ ] Create molecule with type (OnPostCreateAsync)
  - [ ] Toggle active (OnPostToggleActiveAsync)
  - [ ] Delete molecule (OnPostDeleteAsync)

### 5.7 Company Management
- [ ] **What it does**: CRUD for Companies (under Molecules)
- [ ] **Where in code**: `Pages/Admin/Companies.cshtml`, `Pages/Admin/Companies.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Add company (OnPostAddCompanyAsync) with name, display name, slug
  - [ ] Rename company (OnPostRenameCompanyAsync)
  - [ ] Delete company (OnPostDeleteCompanyAsync) with dependency check
  - [ ] HQ companies (IsHeadquarters flag) for Director-level entities

### 5.8 Department Management
- [ ] **What it does**: CRUD for Departments (within Molecules, for Tech molecules)
- [ ] **Where in code**: `Pages/Admin/Organization/Departments/Index.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Create department (OnPostCreateAsync)
  - [ ] Toggle active (OnPostToggleActiveAsync)
  - [ ] Delete department (OnPostDeleteAsync)

### 5.9 Hierarchy Settings
- [ ] **What it does**: Cascading settings at Area/Molecule/Company level
- [ ] **Where in code**: `Services/HierarchySettingsService.cs`, `Models/AreaSettings.cs`, `Models/MoleculeSettings.cs`, `Models/CompanySettings.cs`
- [ ] **How it works**: Settings cascade from Area -> Molecule -> Company; child inherits from parent if not overridden

---

## 6. Context/Scope Switching

### 6.1 Scope Switcher (Molecule/JobType)
- [ ] **What it does**: Allows users to switch calendar context between molecules and job types
- [ ] **Where in code**: `ViewComponents/ScopeSwitcherViewComponent.cs`, `Pages/Shared/Components/ScopeSwitcher/Default.cshtml`, `Pages/Api/ScopeSwitcher.cshtml.cs`
- [ ] **How it works**: Dropdown with molecule + job type combinations; API returns available scopes based on user's grants; returns hierarchy-aware data
- [ ] **User-facing behavior**: Dropdown in calendar toolbar
- [ ] **Sub-features**:
  - [ ] Context-aware scope list per calendar type (OnGetAsync with calendarType param)
  - [ ] Full hierarchy endpoint (OnGetHierarchyAsync)
  - [ ] Feature flag: `FF_ENABLE_COMPANY_SWITCHER`

### 6.2 Owner Company Selector
- [ ] **What it does**: Owner selects which company context to operate in
- [ ] **Where in code**: `Pages/Owner/SelectCompany.cshtml`, `Pages/Owner/ClearCompanySelection.cshtml`, `Services/OwnerCompanySelectorService.cs`, `ViewComponents/OwnerCompanySelectorViewComponent.cs`
- [ ] **How it works**: Owner picks a company from a list; selection stored; clears via ClearCompanySelection
- [ ] **User-facing behavior**: Company selector in Owner navigation
- [ ] **Sub-features**:
  - [ ] Select company (OnPostAsync)
  - [ ] Clear selection (OnPostAsync)

### 6.3 Context Switcher (Legacy)
- [ ] **What it does**: Legacy company context switcher
- [ ] **Where in code**: `ViewComponents/ContextSwitcherViewComponent.cs`, `Pages/Shared/Components/ContextSwitcher/Default.cshtml`
- [ ] **How it works**: Sidebar component for switching company context

---

## 7. Blueprints (Shift Templates / Shift Types)

### 7.1 View Blueprints
- [ ] **What it does**: Lists all shift type definitions for a company
- [ ] **Where in code**: `Pages/Owner/Blueprints.cshtml`, `Pages/Owner/Blueprints.cshtml.cs` (OnGetAsync)
- [ ] **How it works**: Loads ShiftTypes per company with times, names, molecule associations
- [ ] **User-facing behavior**: Table of blueprints with edit/delete actions

### 7.2 Create Blueprint (Shift Type)
- [ ] **What it does**: Creates a new shift type definition
- [ ] **Where in code**: `Pages/Owner/Blueprints.cshtml.cs` (OnPostCreateShiftTypeAsync)
- [ ] **How it works**: Creates ShiftType with Key, Start/End times, NameEN/NameHE
- [ ] **Sub-features**:
  - [ ] Key, name (EN/HE), start time, end time

### 7.3 Edit Blueprint Name
- [ ] **What it does**: Updates shift type display names
- [ ] **Where in code**: `Pages/Owner/Blueprints.cshtml.cs` (OnPostUpdateShiftNameAsync)

### 7.4 Edit Blueprint Times
- [ ] **What it does**: Updates shift type start/end times
- [ ] **Where in code**: `Pages/Owner/Blueprints.cshtml.cs` (OnPostUpdateShiftTimesAsync)

### 7.5 Check Blueprint Usage
- [ ] **What it does**: Checks if a blueprint is in use before deletion
- [ ] **Where in code**: `Pages/Owner/Blueprints.cshtml.cs` (OnGetCheckShiftTypeUsageAsync)
- [ ] **How it works**: Counts shift instances and program days referencing this type

### 7.6 Delete Blueprint
- [ ] **What it does**: Deletes a shift type (with confirmation if in use)
- [ ] **Where in code**: `Pages/Owner/Blueprints.cshtml.cs` (OnPostDeleteShiftTypeAsync)
- [ ] **How it works**: Requires `confirmed=true` if in use; cascades to assignments

### 7.7 Publish/Unpublish to Molecule
- [ ] **What it does**: Associates a shift type with a specific molecule + job type for visibility on calendars
- [ ] **Where in code**: `Pages/Owner/Blueprints.cshtml.cs` (OnPostPublishToMoleculeAsync, OnPostUnpublishFromMoleculeAsync)
- [ ] **How it works**: Sets MoleculeId and JobTypeId on ShiftType

### 7.8 Populate Name Keys
- [ ] **What it does**: Backfills NameKeyEN/NameKeyHE from Key for existing blueprints
- [ ] **Where in code**: `Pages/Owner/Blueprints.cshtml.cs` (OnPostPopulateNameKeysAsync)

---

## 8. Programs (Shift Schedules)

### 8.1 View Programs
- [ ] **What it does**: Lists shift programs (recurring schedules)
- [ ] **Where in code**: `Pages/Owner/Programs.cshtml`, `Pages/Owner/Programs.cshtml.cs` (OnGetAsync)
- [ ] **How it works**: Loads ShiftPrograms with associated ProgramDays and staffing masks

### 8.2 Create Program
- [ ] **What it does**: Creates a new shift program
- [ ] **Where in code**: `Pages/Owner/Programs.cshtml.cs` (OnPostCreateProgramAsync)
- [ ] **How it works**: Creates ShiftProgram with name, associated ShiftType, staffing requirements per day of week via ProgramDays

### 8.3 Update Program
- [ ] **What it does**: Edits an existing program's configuration
- [ ] **Where in code**: `Pages/Owner/Programs.cshtml.cs` (OnPostUpdateProgramAsync)

### 8.4 Delete Program
- [ ] **What it does**: Deletes a shift program
- [ ] **Where in code**: `Pages/Owner/Programs.cshtml.cs` (OnPostDeleteProgramAsync)

### 8.5 Generate Shift Instances
- [ ] **What it does**: Generates concrete shift instances from a program for a date range
- [ ] **Where in code**: `Pages/Owner/Programs.cshtml.cs` (OnPostGenerateInstancesAsync), `Services/ShiftProgramService.cs`
- [ ] **How it works**: Creates ShiftInstance records for each day in range based on program's day-of-week staffing mask

### 8.6 Master Programs
- [ ] **What it does**: Organization-wide program templates that can be applied to multiple companies
- [ ] **Where in code**: `Pages/Owner/MasterPrograms.cshtml`, `Pages/Owner/MasterPrograms.cshtml.cs`, `Services/MasterProgramService.cs`
- [ ] **Sub-features**:
  - [ ] Create master program (OnPostCreateMasterProgramAsync)
  - [ ] Update master program (OnPostUpdateMasterProgramAsync)
  - [ ] Delete master program (OnPostDeleteMasterProgramAsync)
  - [ ] Generate instances from master (OnPostGenerateFromMasterAsync) - applies to specified companies
  - [ ] MasterProgramItems for template definition

---

## 9. Shift Calendar

### 9.1 Excel-Style Shifts Calendar (Primary)
- [ ] **What it does**: Excel-like weekly/bi-weekly/monthly shift grid
- [ ] **Where in code**: `Pages/Calendar/Shifts.cshtml`, `Pages/Calendar/Shifts.cshtml.cs`, `Services/ShiftCalendarService.cs`, `ViewComponents/ExcelCalendarTableViewComponent.cs`
- [ ] **How it works**: Builds grid with shift types as rows, dates as columns; supports shift-based and user-based view modes
- [ ] **User-facing behavior**: Interactive table with assignment cells, inline editing, real-time updates
- [ ] **Sub-features**:
  - [ ] View modes: week, 2weeks, month
  - [ ] Display modes: shift (shift types as rows), user (employees as rows)
  - [ ] Capacity mode (show staffing counts)
  - [ ] Just Mine toggle (filter to current user's assignments)
  - [ ] Molecule + JobType selector
  - [ ] Date navigation (previous/next)
  - [ ] Feature flag: `FF_EXCEL_CALENDAR_SHIFTS`

### 9.2 Table Calendar (Legacy)
- [ ] **What it does**: Original shift assignment table with full CRUD
- [ ] **Where in code**: `Pages/Calendar/Table.cshtml`, `Pages/Calendar/Table.cshtml.cs`
- [ ] **How it works**: Server-rendered table with POST handlers for all assignment operations
- [ ] **Sub-features**:
  - [ ] View modes: week, 2weeks, month
  - [ ] Molecule mode (MoleculeId + JobTypeId scoping)
  - [ ] Date navigation with week boundaries
  - [ ] Busy user status per date

### 9.3 Shift Assignment Operations (Table POST handlers)
- [ ] **What it does**: CRUD operations for shift assignments
- [ ] **Where in code**: `Pages/Calendar/Table.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Ensure shift instance exists (OnPostEnsureShiftInstanceAsync)
  - [ ] Create shift instance manually (OnPostCreateShiftInstanceAsync)
  - [ ] Assign user to slot (OnPostAssignUserToSlotAsync)
  - [ ] Assign employee (OnPostAssignEmployeeAsync)
  - [ ] Unassign employee (OnPostUnassignEmployeeAsync)
  - [ ] Clear assignment (OnPostClearAssignmentAsync)
  - [ ] Update shift staffing/capacity (OnPostUpdateShiftStaffingAsync)
  - [ ] Delete shift instance (OnPostDeleteShiftInstanceAsync)
  - [ ] Add trainee to assignment (OnPostAddTraineeAsync)
  - [ ] Remove trainee (OnPostRemoveTraineeAsync)
  - [ ] Change assigned user (OnPostChangeUserAsync)
  - [ ] Update shift metadata (OnPostUpdateShiftMetadataAsync)
  - [ ] Create custom shift type (OnPostCreateCustomShiftTypeAsync)
  - [ ] Detach instance from program (OnPostDetachInstanceAsync)
  - [ ] Reset instance to program (OnPostResetInstanceToProgramAsync)
  - [ ] Fill range (OnPostFillRangeAsync) - bulk copy assignments
  - [ ] Get employee availability (OnGetEmployeeAvailabilityAsync)
  - [ ] Get roster employees (OnGetGetRosterEmployeesAsync)
  - [ ] Get conflicts (OnGetGetConflictsAsync)

### 9.4 Roster Dock (Drag-and-Drop Assignment)
- [ ] **What it does**: Side panel for quick employee assignment
- [ ] **Where in code**: `wwwroot/js/roster-dock.js`
- [ ] **How it works**: Loads employee list with availability status; drag employees to assignment cells
- [ ] **User-facing behavior**: Toggle-able side panel with employee list, search, drag-and-drop

### 9.5 Fill Handle (Excel-Style Bulk Copy)
- [ ] **What it does**: Drag-to-fill functionality for copying assignments
- [ ] **Where in code**: `wwwroot/js/calendar-fill-handle.js`
- [ ] **How it works**: Click and drag fill handle on assignment cell to copy to adjacent days
- [ ] **User-facing behavior**: Small handle on cells; drag to fill multiple days; disabled on touch devices

### 9.6 Calendar Inline Edit
- [ ] **What it does**: Inline editing of shift assignments within calendar cells
- [ ] **Where in code**: `wwwroot/js/calendar-inline-edit.js`
- [ ] **How it works**: Click-to-edit cells; quick add chores and on-duty

### 9.7 Calendar Bottom Sheet (Mobile)
- [ ] **What it does**: Mobile-friendly bottom sheet for calendar interactions
- [ ] **Where in code**: `wwwroot/js/calendar-bottom-sheet.js`

### 9.8 Shift Assignments Page (Manage)
- [ ] **What it does**: Dedicated page for managing shift assignments with notifications
- [ ] **Where in code**: `Pages/Assignments/Manage.cshtml`, `Pages/Assignments/Manage.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] View assignments (OnGetAsync)
  - [ ] Create assignment (OnPostAsync) - sends notifications
  - [ ] Remove assignment (OnPostRemoveAsync)
  - [ ] Assign trainee (OnPostAssignTraineeAsync)
  - [ ] Remove trainee (OnPostRemoveTraineeAsync)

### 9.9 Calendar Data API
- [ ] **What it does**: JSON API for loading shift calendar data
- [ ] **Where in code**: `Pages/Api/Calendar/GetShiftsData.cshtml.cs`
- [ ] **How it works**: Returns shift data with assignments for date range, molecule, and job type

### 9.10 Shift History
- [ ] **What it does**: View audit trail of shift changes
- [ ] **Where in code**: `Pages/Api/Calendar/ShiftHistory.cshtml.cs`
- [ ] **How it works**: Returns history of changes for a user or instance

### 9.11 Month Calendar View (Legacy)
- [ ] **What it does**: Monthly calendar grid view
- [ ] **Where in code**: `Pages/Calendar/Month.cshtml`, `Pages/Calendar/Month.cshtml.cs`
- [ ] **How it works**: Displays shifts in monthly grid; supports JobType and ShiftGrouping filters

### 9.12 Week Calendar View (Legacy)
- [ ] **What it does**: Weekly calendar view
- [ ] **Where in code**: `Pages/Calendar/Week.cshtml`, `Pages/Calendar/Week.cshtml.cs`

### 9.13 Day Calendar View
- [ ] **What it does**: Single day detailed view
- [ ] **Where in code**: `Pages/Calendar/Day.cshtml`, `Pages/Calendar/Day.cshtml.cs`
- [ ] **How it works**: Shows all shifts for a specific day with detailed assignments, trainee badges

### 9.14 Calendar Index (Landing)
- [ ] **What it does**: Calendar landing/redirect page
- [ ] **Where in code**: `Pages/Calendar/Index.cshtml`, `Pages/Calendar/Index.cshtml.cs`

### 9.15 Shift Capacity Overrides
- [ ] **What it does**: Override default staffing levels per shift/date
- [ ] **Where in code**: `Models/ShiftCapacityOverride.cs`, handled in Table POST handlers

---

## 10. Chore Calendar

### 10.1 Excel-Style Chores Calendar
- [ ] **What it does**: Excel-like chore assignment calendar
- [ ] **Where in code**: `Pages/Calendar/Chores.cshtml`, `Pages/Calendar/Chores.cshtml.cs`
- [ ] **How it works**: Monthly grid with chore types as rows; molecule-scoped
- [ ] **User-facing behavior**: Interactive table with quick-add and assignment
- [ ] **Sub-features**:
  - [ ] Feature flag: `FF_EXCEL_CALENDAR_CHORES`

### 10.2 Chore CRUD (Legacy Calendar)
- [ ] **What it does**: Create, cancel chores in the legacy calendar
- [ ] **Where in code**: `Pages/Chores/Calendar.cshtml`, `Pages/Chores/Calendar.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Create chore (OnPostCreateChoreAsync)
  - [ ] Replace shift with chore (OnPostReplaceShiftWithChoreAsync)
  - [ ] Cancel chore (OnPostCancelChoreAsync)

### 10.3 Chore Data API
- [ ] **What it does**: JSON API for loading chore data
- [ ] **Where in code**: `Pages/Api/Calendar/GetChoresData.cshtml.cs`

### 10.4 Quick Add Chore
- [ ] **What it does**: Inline add chore via API
- [ ] **Where in code**: `Pages/Api/Calendar/QuickAddChore.cshtml.cs`

### 10.5 Delete Chore
- [ ] **What it does**: Hard delete a chore
- [ ] **Where in code**: `Pages/Api/Calendar/DeleteChore.cshtml.cs`

### 10.6 Restore Chore
- [ ] **What it does**: Restore a cancelled chore
- [ ] **Where in code**: `Pages/Api/Calendar/RestoreChore.cshtml.cs`

### 10.7 Chore Types
- [ ] **What it does**: Define types of chores
- [ ] **Where in code**: `Models/ChoreType.cs`, `Services/ChoreTypeService.cs`
- [ ] **How it works**: Each ChoreType has name, description, color; associated with a molecule

---

## 11. On-Duty Calendar

### 11.1 Excel-Style On-Call Calendar
- [ ] **What it does**: Excel-like on-duty/day shift assignment calendar
- [ ] **Where in code**: `Pages/Calendar/OnCall.cshtml`, `Pages/Calendar/OnCall.cshtml.cs`
- [ ] **How it works**: Monthly grid with duty types as rows; area-scoped
- [ ] **User-facing behavior**: Interactive table with duty assignment
- [ ] **Sub-features**:
  - [ ] Feature flag: `FF_EXCEL_CALENDAR_ONCALL`
  - [ ] Area and duty type selectors

### 11.2 On-Duty CRUD
- [ ] **What it does**: Create and cancel on-duty assignments
- [ ] **Where in code**: `Pages/Public/OnDuty.cshtml`, `Pages/Public/OnDuty.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Create on-duty (OnPostCreateOnDutyAsync)
  - [ ] Cancel on-duty (OnPostCancelOnDutyAsync)

### 11.3 On-Duty Data API
- [ ] **What it does**: JSON API for loading on-call data
- [ ] **Where in code**: `Pages/Api/Calendar/GetOnCallData.cshtml.cs`

### 11.4 Quick Add On-Duty
- [ ] **What it does**: Inline add on-duty via API
- [ ] **Where in code**: `Pages/Api/Calendar/QuickAddOnDuty.cshtml.cs`

### 11.5 Delete On-Duty
- [ ] **What it does**: Hard delete on-duty entry
- [ ] **Where in code**: `Pages/Api/Calendar/DeleteOnDuty.cshtml.cs`

### 11.6 Eligible Users for On-Duty
- [ ] **What it does**: Returns users eligible for on-duty assignment
- [ ] **Where in code**: `Pages/Api/OnDuty/GetEligibleUsers.cshtml.cs`
- [ ] **How it works**: Filters by duty type and rank eligibility

### 11.7 On-Duty Type Configuration
- [ ] **What it does**: Configure on-duty types (Hakam, Lead, custom)
- [ ] **Where in code**: `Pages/Admin/Config.cshtml.cs` (OnPostAddOnDutyTypeAsync, OnPostDeleteOnDutyTypeAsync), `Models/OnDutyTypeConfig.cs`

### 11.8 Duty Rotation
- [ ] **What it does**: Automated rotation queue for on-duty assignments
- [ ] **Where in code**: `Pages/Admin/DutyRotation/Index.cshtml.cs`, `Services/DutyRotationService.cs`
- [ ] **How it works**: Configurable rotation queues with user ordering; auto-fill with rotation logic
- [ ] **User-facing behavior**: Admin page for managing rotation queues
- [ ] **Sub-features**:
  - [ ] Create rotation (OnPostCreateAsync)
  - [ ] Update rotation (OnPostUpdateAsync)
  - [ ] Delete rotation (OnPostDeleteAsync)
  - [ ] Add user to queue (OnPostAddUserAsync)
  - [ ] Remove user from queue (OnPostRemoveUserAsync)
  - [ ] Reorder queue (OnPostReorderQueueAsync)
  - [ ] Rotation logging (`DutyRotationLog`)
  - [ ] Feature flag: `FF_DUTY_ROTATION_ENABLED`

### 11.9 Rank Eligibility Enforcement
- [ ] **What it does**: Military rank checks for duty assignment
- [ ] **Where in code**: Feature flag `FF_ENFORCE_RANK_ELIGIBILITY`, `Models/Support/MilitaryRank.cs`
- [ ] **How it works**: Certain duty types (e.g., Katzin) require officer rank

---

## 12. Overview Calendar

### 12.1 Excel-Style Overview Calendar
- [ ] **What it does**: Aggregated company-wide view of all calendar types
- [ ] **Where in code**: `Pages/Calendar/Overview.cshtml`, `Pages/Calendar/Overview.cshtml.cs`
- [ ] **How it works**: Shows shifts, chores, on-duty, vacation overlays, and user day notes in a single view
- [ ] **User-facing behavior**: Interactive table with all calendar data combined
- [ ] **Sub-features**:
  - [ ] Feature flag: `FF_EXCEL_CALENDAR_OVERVIEW`
  - [ ] View modes: week, 2weeks, month
  - [ ] Just Mine toggle
  - [ ] Date navigation

### 12.2 User Day Notes
- [ ] **What it does**: Add personal notes to calendar days
- [ ] **Where in code**: `Pages/Calendar/Overview.cshtml.cs` (OnPostSaveNoteAsync), `Services/UserDayNoteService.cs`, `Models/UserDayNote.cs`
- [ ] **How it works**: Users can add/edit/delete notes per day; notes shown in overview calendar

### 12.3 Overview Data API
- [ ] **What it does**: JSON API for loading overview data
- [ ] **Where in code**: `Pages/Api/Calendar/GetOverviewData.cshtml.cs`

---

## 13. Time-Off / Vacation Requests

### 13.1 Create Time-Off Request
- [ ] **What it does**: Submit vacation or half-day time-off requests
- [ ] **Where in code**: `Pages/Requests/TimeOff/Create.cshtml`, `Pages/Requests/TimeOff/Create.cshtml.cs`, `Pages/My/Requests.cshtml.cs` (OnPostTimeOffAsync)
- [ ] **How it works**: Creates `TimeOffRequest` with type (Vacation = full day, After = half day), start/end dates, optional reason
- [ ] **User-facing behavior**: Form with date range, type selector, notes
- [ ] **Sub-features**:
  - [ ] Full vacation (StartDate to EndDate)
  - [ ] Half day / "After" (StartDate 16:00 to next day 13:00)

### 13.2 Approve/Decline Time-Off
- [ ] **What it does**: Manager approves or declines time-off requests
- [ ] **Where in code**: `Pages/Requests/Index.cshtml.cs` (OnPostApproveTimeOffAsync, OnPostDeclineTimeOffAsync)
- [ ] **How it works**: Updates status; on approve, auto-unassigns affected shift assignments; sends notification
- [ ] **User-facing behavior**: Approve/Decline buttons on requests page
- [ ] **Sub-features**:
  - [ ] Auto-unassign from shifts on approval
  - [ ] Notification on approve/decline

### 13.3 Delete Time-Off Request
- [ ] **What it does**: Delete a time-off request
- [ ] **Where in code**: `Pages/Requests/Index.cshtml.cs` (OnPostDeleteTimeOffAsync)

### 13.4 Cancel Time-Off Request
- [ ] **What it does**: User cancels their own pending request
- [ ] **Where in code**: `Pages/My/Requests.cshtml.cs` (OnPostCancelRequestAsync)

### 13.5 Vacation Approval Workflow
- [ ] **What it does**: Rule-based approval routing
- [ ] **Where in code**: `Services/VacationApprovalService.cs`, `Models/VacationApprovalRule.cs`
- [ ] **How it works**: Configurable rules for auto-approve, multi-level approval
- [ ] **Sub-features**:
  - [ ] Feature flag: `FF_VACATION_APPROVAL_ENABLED`
  - [ ] Approval rules CRUD (`Pages/Admin/Settings/ApprovalRules.cshtml.cs`)

### 13.6 My Requests Page
- [ ] **What it does**: User views their own time-off and swap requests
- [ ] **Where in code**: `Pages/My/Requests.cshtml`, `Pages/My/Requests.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] View own requests
  - [ ] Create time-off (OnPostTimeOffAsync)
  - [ ] Create swap (OnPostSwapAsync)
  - [ ] Cancel request (OnPostCancelRequestAsync)

---

## 14. Swap Requests

### 14.1 Create Swap Request
- [ ] **What it does**: Request to swap a shift with another user
- [ ] **Where in code**: `Pages/Requests/Swaps/Create.cshtml`, `Pages/Requests/Swaps/Create.cshtml.cs`
- [ ] **How it works**: Creates SwapRequest with source and target shift assignments

### 14.2 Approve/Decline Swap
- [ ] **What it does**: Manager approves or declines swap requests
- [ ] **Where in code**: `Pages/Requests/Index.cshtml.cs` (OnPostApproveSwapAsync, OnPostDeclineSwapAsync)
- [ ] **How it works**: On approve, swaps the user assignments between two shifts; sends notifications

### 14.3 Requests Dashboard
- [ ] **What it does**: Central hub for all pending requests
- [ ] **Where in code**: `Pages/Requests/Index.cshtml`, `Pages/Requests/Index.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] View all pending time-off requests
  - [ ] View all pending swap requests
  - [ ] Approve/Decline time-off (OnPostApproveTimeOffAsync, OnPostDeclineTimeOffAsync)
  - [ ] Approve/Decline swap (OnPostApproveSwapAsync, OnPostDeclineSwapAsync)
  - [ ] Delete time-off (OnPostDeleteTimeOffAsync)

---

## 15. Notifications

### 15.1 Notification Center
- [ ] **What it does**: View all notifications
- [ ] **Where in code**: `Pages/My/NotificationCenter.cshtml`, `Pages/My/NotificationCenter.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] List notifications (OnGetAsync)
  - [ ] Mark as read (OnPostMarkAsReadAsync)
  - [ ] Mark all as read (OnPostMarkAllAsReadAsync)
  - [ ] Delete notification (OnPostDeleteAsync)

### 15.2 Notification Types
- [ ] **What it does**: 20 notification types covering all operations
- [ ] **Where in code**: `Models/Support/Enums.cs` (NotificationType enum)
- [ ] **Types**: ShiftAdded, ShiftRemoved, TimeOffApproved, TimeOffDeclined, SwapRequestApproved, SwapRequestDeclined, TraineeShadowingAdded/Removed, ChoreAssigned, ChoreCanceled, OnDutyAssigned, OnDutyCanceled, TimeOffDeleted, FeedbackSubmitted, AccessRequestSubmitted, AccessRequestApproved

### 15.3 Unread Notification Badge
- [ ] **What it does**: Shows count of unread notifications
- [ ] **Where in code**: `ViewComponents/UnreadNotificationCountViewComponent.cs`, `Pages/Shared/Components/UnreadNotificationCount/Default.cshtml`
- [ ] **How it works**: Bell icon with count badge in navigation

### 15.4 Daily Notification Digest
- [ ] **What it does**: Background job for daily notification emails
- [ ] **Where in code**: `Services/DailyNotificationJob.cs`
- [ ] **How it works**: Hosted service that runs daily; sends digest of notifications
- [ ] **Sub-features**:
  - [ ] Feature flag: `FF_ENABLE_DAILY_NOTIFICATIONS`
  - [ ] Daily notification preferences (`DailyNotificationPreference`)
  - [ ] On-duty role subscriptions (`OnDutyRoleSubscription`)

### 15.5 Director Notification Hub
- [ ] **What it does**: Director-level notification aggregation
- [ ] **Where in code**: `Pages/Director/NotificationHub.cshtml`, `Pages/Director/NotificationHub.cshtml.cs`
- [ ] **How it works**: Shows notifications across all assigned companies

---

## 16. Localization

### 16.1 Hebrew/English Support
- [ ] **What it does**: Full bilingual support with RTL layout
- [ ] **Where in code**: `Resources/SharedResources.*.resx`, `Pages/Shared/_LocalizationScript.cshtml`, `Services/LocalizationService.cs`
- [ ] **How it works**: Uses ASP.NET Core localization with `IStringLocalizer<SharedResources>`; culture set via cookie, query string, or Accept-Language header
- [ ] **User-facing behavior**: Language toggle in navigation; all UI text localized
- [ ] **Sub-features**:
  - [ ] Supported cultures: en-US, he-IL
  - [ ] RTL stylesheet: `wwwroot/css/rtl.css`
  - [ ] Hebrew audit script: `wwwroot/js/hebrew-audit.js`

### 16.2 Language Toggle
- [ ] **What it does**: Switch between Hebrew and English
- [ ] **Where in code**: `ViewComponents/LanguageToggleViewComponent.cs`, `Pages/Shared/Components/LanguageToggle/Default.cshtml`

### 16.3 Company-Level Language Overrides
- [ ] **What it does**: Per-company translation overrides
- [ ] **Where in code**: `Services/CompanyLocalizationService.cs`, `Models/CompanyLocalizationOverride.cs`, `Models/CompanyLanguageSettings.cs`
- [ ] **How it works**: Companies can override default translations for specific keys

### 16.4 Language Management (Owner)
- [ ] **What it does**: Owner manages translations and overrides
- [ ] **Where in code**: `Pages/Owner/LanguageManagement.cshtml`, `Pages/Owner/LanguageManagement.cshtml.cs`, `Services/LanguageManagementService.cs`
- [ ] **Sub-features**:
  - [ ] Search translations (OnGetAsync with searchTerm)
  - [ ] Save language settings (OnPostSaveLanguageSettingsAsync)
  - [ ] Add override (OnPostAddOverrideAsync)
  - [ ] Delete override (OnPostDeleteOverrideAsync)
  - [ ] Save drafts via API (OnPostApiSaveDraftsAsync)

### 16.5 Language Edit Mode
- [ ] **What it does**: Inline translation editing overlay
- [ ] **Where in code**: `Pages/Owner/LanguageEditMode.cshtml.cs`, `wwwroot/js/language-edit-mode.js`, `wwwroot/css/language-edit-mode.css`
- [ ] **How it works**: Cookie-based toggle; when active, UI shows editable translation keys inline

### 16.6 Localization API
- [ ] **What it does**: JavaScript API for client-side translations
- [ ] **Where in code**: `Pages/Api/Localization.cshtml.cs`, `wwwroot/js/localization-api.js`, `wwwroot/js/localization-attributes.js`
- [ ] **How it works**: Fetches translation keys via GET endpoint; `data-loc-*` attributes for automatic translation

---

## 17. Tenant Isolation (Multi-tenancy)

### 17.1 EF Core Query Filters
- [ ] **What it does**: Automatic CompanyId filtering on all queries
- [ ] **Where in code**: `Data/AppDbContext.cs` (OnModelCreating), `Data/CompanyIdInterceptor.cs`
- [ ] **How it works**: `IBelongsToCompany` interface triggers automatic query filter; `CompanyIdInterceptor` auto-sets CompanyId on new entities

### 17.2 Company Context Middleware
- [ ] **What it does**: Resolves current company context from user claims
- [ ] **Where in code**: `Middleware/CompanyContextMiddleware.cs`, `Services/CompanyContext.cs`, `Services/TenantResolver.cs`
- [ ] **How it works**: Reads CompanyId from user claims; sets ICompanyContext for DI-injected services

### 17.3 Scope-Based Data Filtering
- [ ] **What it does**: Filters data based on user's hierarchical scope
- [ ] **Where in code**: `Services/ScopeFilterService.cs`

---

## 18. REST API

### 18.1 API Authentication (API Keys)
- [ ] **What it does**: API key-based authentication for external access
- [ ] **Where in code**: `Middleware/ApiAuthenticationMiddleware.cs`, `Services/ApiKeyService.cs`
- [ ] **How it works**: X-API-Key header; HMAC-based key hashing; key scoping by CompanyId
- [ ] **Sub-features**:
  - [ ] Feature flag: `FF_API_ENABLED` (master), individual endpoint flags
  - [ ] HMAC secret configuration via `ApiKeyHmacSecret`

### 18.2 API Key Management
- [ ] **What it does**: Users request and manage API keys
- [ ] **Where in code**: `Pages/My/ApiKeys.cshtml`, `Pages/My/ApiKeys.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Request API key (OnPostRequestAsync)
  - [ ] Revoke key (OnPostRevokeAsync)
  - [ ] Refresh key (OnPostRefreshAsync)
  - [ ] Admin approve/reject (OnPostApproveAsync, OnPostRejectAsync)
  - [ ] Admin revoke (OnPostRevokeAdminAsync)
  - [ ] Feature flag: `FF_ENABLE_API_KEY_MANAGEMENT`

### 18.3 REST API Endpoints (Controllers)
- [ ] **What it does**: Full CRUD REST API for external integrations
- [ ] **Where in code**: `Controllers/Api/V1/`
- [ ] **Endpoints**:
  - [ ] **Users**: GET /api/v1/users, GET /api/v1/users/{id}, POST /api/v1/users, PATCH /api/v1/users/{id}
  - [ ] **Shifts**: GET /api/v1/shifts, GET /api/v1/shifts/{id}
  - [ ] **Time-Off**: GET /api/v1/time-off-requests, GET /{id}, POST, POST /{id}/approve, POST /{id}/decline
  - [ ] **Notifications**: GET /api/v1/notifications, GET /{id}, POST /{id}/mark-read, POST /mark-all-read
  - [ ] **Chores**: GET /api/v1/chores, GET /{id}, POST, PATCH /{id}, DELETE /{id}
  - [ ] **On-Duty**: GET /api/v1/on-duty, GET /{id}, POST, PATCH /{id}, DELETE /{id}
  - [ ] **Swap Requests**: GET /api/v1/swap-requests, GET /{id}, POST, POST /{id}/approve, POST /{id}/decline, DELETE /{id}
  - [ ] **Feedback**: GET /api/v1/feedback, GET /{id}, POST, PATCH /{id}/status, DELETE /{id}
  - [ ] **Audit Logs**: GET /api/v1/audit-logs
  - [ ] **Analytics**: GET /api/v1/analytics/summary
  - [ ] **Admin Grants**: GET/POST /api/v1/admin/grants (verify, repair)

### 18.4 API Rate Limiting
- [ ] **What it does**: Per-API-key rate limiting
- [ ] **Where in code**: `Middleware/ApiRateLimitingMiddleware.cs`

### 18.5 API Error Handling
- [ ] **What it does**: Standardized error responses for API
- [ ] **Where in code**: `Middleware/ApiExceptionMiddleware.cs`, `Models/Api/ProblemDetails.cs`

### 18.6 API Request Logging
- [ ] **What it does**: Logs all API requests for auditing
- [ ] **Where in code**: `Middleware/ApiRequestLoggingMiddleware.cs`, `Models/Api/ApiRequestLog.cs`

### 18.7 Swagger/OpenAPI
- [ ] **What it does**: API documentation in development
- [ ] **Where in code**: `Program.cs` (lines 357-404, 1136-1141)
- [ ] **User-facing behavior**: Available at `/swagger` in development mode

### 18.8 Version Endpoint
- [ ] **What it does**: Returns app version info
- [ ] **Where in code**: `Program.cs` (lines 1368-1380)
- [ ] **Endpoint**: GET /api/v1/version (anonymous)

---

## 19. Owner/Admin Tools

### 19.1 Owner Dashboard
- [ ] **What it does**: Owner's main control panel
- [ ] **Where in code**: `Pages/Owner/Index.cshtml`, `Pages/Owner/Index.cshtml.cs`
- [ ] **How it works**: Redirects to Owner Hub or shows legacy dashboard

### 19.2 Owner Hub
- [ ] **What it does**: Centralized admin hub with system overview
- [ ] **Where in code**: `Pages/Owner/Hub/Index.cshtml`, `Pages/Owner/Hub/Index.cshtml.cs`
- [ ] **User-facing behavior**: Dashboard with system stats, links to all admin tools

### 19.3 Database Console
- [ ] **What it does**: Direct SQL query execution
- [ ] **Where in code**: `Pages/Owner/DatabaseConsole.cshtml`, `Pages/Owner/DatabaseConsole.cshtml.cs`
- [ ] **How it works**: Read-only SQLite connection; semicolon rejection for SQL injection prevention; DB selector
- [ ] **User-facing behavior**: SQL editor with results table
- [ ] **Sub-features**:
  - [ ] Read-only queries only
  - [ ] SQL injection prevention (semicolon rejection)
  - [ ] Database selector (app.db, seed.db)

### 19.4 Database Backup
- [ ] **What it does**: Create, download, restore, and delete database backups
- [ ] **Where in code**: `Pages/Owner/Backup.cshtml`, `Pages/Owner/Backup.cshtml.cs`, `Services/DatabaseBackupService.cs`
- [ ] **Sub-features**:
  - [ ] Create backup (OnPostCreateBackupAsync)
  - [ ] Download backup (OnGetDownloadBackupAsync)
  - [ ] Restore backup (OnPostRestoreBackupAsync)
  - [ ] Delete backup (OnPostDeleteBackupAsync)
  - [ ] Automated backup service (hosted service)
  - [ ] Pre-migration backup at startup

### 19.5 System Health
- [ ] **What it does**: System health dashboard
- [ ] **Where in code**: `Pages/Owner/SystemHealth.cshtml`, `Pages/Owner/SystemHealth.cshtml.cs`
- [ ] **How it works**: Shows DB size, user counts, active sessions, disk space, memory usage
- [ ] **Sub-features**:
  - [ ] Health check endpoints: `/health` (liveness), `/ready` (readiness)
  - [ ] Disk space health check (`DiskSpaceHealthCheck`)
  - [ ] Memory health check (`MemoryHealthCheck`)
  - [ ] DB context health check

### 19.6 Feature Flags
- [ ] **What it does**: Toggle feature flags
- [ ] **Where in code**: `Pages/Owner/FeatureFlags.cshtml`, `Pages/Owner/FeatureFlags.cshtml.cs`, `Services/FeatureFlagService.cs`
- [ ] **How it works**: 50+ feature flags for UI features, API endpoints, operational settings
- [ ] **User-facing behavior**: Toggle switches for each flag with descriptions

### 19.7 Griffin ADFS Configuration
- [ ] **What it does**: Configure Griffin SSO settings
- [ ] **Where in code**: `Pages/Owner/GriffinConfig.cshtml`, `Pages/Owner/GriffinConfig.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Save config (OnPostAsync)
  - [ ] Test connection (OnPostTestConnectionAsync)

### 19.8 Email Configuration
- [ ] **What it does**: Configure email sending settings
- [ ] **Where in code**: `Pages/Owner/EmailConfig.cshtml`, `Pages/Owner/EmailConfig.cshtml.cs`, `Services/EmailConfigService.cs`, `Services/MailService.cs`
- [ ] **Sub-features**:
  - [ ] SMTP/API configuration (OnPostAsync)
  - [ ] Send test email (OnPostSendTestAsync)
  - [ ] Export email logs as JSON/CSV (OnGetExportLogsJsonAsync, OnGetExportLogsCsvAsync)
  - [ ] Email API logging (`EmailApiLog`)
  - [ ] Background email queue (`EmailBackgroundQueue`, `EmailBackgroundProcessor`)

### 19.9 Email Templates
- [ ] **What it does**: Customize email notification templates
- [ ] **Where in code**: `Pages/Owner/EmailTemplates.cshtml`, `Pages/Owner/EmailTemplates.cshtml.cs`, `Services/EmailTemplateService.cs`, `Services/EmailTemplateBuilder.cs`
- [ ] **Sub-features**:
  - [ ] Save template (OnPostSaveTemplateAsync)
  - [ ] Reset template to default (OnPostResetTemplateAsync)
  - [ ] 14 template types (ShiftAssigned through AccountApproved)

### 19.10 Locked Users
- [ ] **What it does**: View and unlock locked-out users
- [ ] **Where in code**: `Pages/Owner/LockedUsers.cshtml`, `Pages/Owner/LockedUsers.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] View locked users (OnGetAsync)
  - [ ] Unlock user (OnPostUnlockAsync)
  - [ ] Clear signup rate limits (OnPostClearSignupRateLimitsAsync)

### 19.11 Data Lifecycle (Archive/Purge/Import)
- [ ] **What it does**: Manage data lifecycle — archive, purge, and import
- [ ] **Where in code**: `Pages/Owner/DataLifecycle.cshtml`, `Pages/Owner/DataLifecycle.cshtml.cs`, `Services/ArchiveService.cs`, `Services/PurgeService.cs`, `Services/ImportService.cs`
- [ ] **Sub-features**:
  - [ ] Preview archive (OnPostPreviewAsync)
  - [ ] Create archive (OnPostCreateArchiveAsync)
  - [ ] Download CSV (OnGetDownloadCsvAsync)
  - [ ] Download NDJSON (OnGetDownloadNdjsonAsync)
  - [ ] Purge data (OnPostPurgeAsync)
  - [ ] Validate archive (OnPostValidateArchiveAsync)
  - [ ] Import data (OnPostImportAsync)

### 19.12 Area Configuration
- [ ] **What it does**: Configure area-level settings
- [ ] **Where in code**: `Pages/Owner/AreaConfig.cshtml`, `Pages/Owner/AreaConfig.cshtml.cs`

### 19.13 Game Configuration
- [ ] **What it does**: Configure the easter egg game settings
- [ ] **Where in code**: `Pages/Owner/GameConfig.cshtml`, `Pages/Owner/GameConfig.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Grid size, points per match, mega combo settings, milestones

### 19.14 Permissions Viewer
- [ ] **What it does**: View all permissions/grants in the system
- [ ] **Where in code**: `Pages/Owner/Permissions.cshtml`, `Pages/Owner/Permissions.cshtml.cs`

### 19.15 Telemetry Dashboard
- [ ] **What it does**: View client-side telemetry data
- [ ] **Where in code**: `Pages/Owner/Telemetry.cshtml`, `Pages/Owner/Telemetry.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] View analytics events, client errors, performance metrics
  - [ ] Cleanup old data (OnPostCleanupAsync)
  - [ ] Tab-based navigation (tab param)

### 19.16 Audit Search (Owner Hub)
- [ ] **What it does**: Search audit logs with advanced filters
- [ ] **Where in code**: `Pages/Owner/Hub/AuditSearch.cshtml`, `Pages/Owner/Hub/AuditSearch.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Search with filters (OnGetAsync)
  - [ ] Export to CSV (OnGetExportCsvAsync)

### 19.17 Export User Data
- [ ] **What it does**: Export all data for a specific user (GDPR-style)
- [ ] **Where in code**: `Pages/Owner/Hub/ExportUserData.cshtml`, `Pages/Owner/Hub/ExportUserData.cshtml.cs`, `Services/UserDataExportService.cs`

### 19.18 Seed Data Management
- [ ] **What it does**: Re-seed system data
- [ ] **Where in code**: `Pages/Owner/Hub/SeedData.cshtml`, `Pages/Owner/Hub/SeedData.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Seed all (OnPostSeedAllAsync)
  - [ ] Seed grant types (OnPostSeedGrantTypesAsync)
  - [ ] Seed role templates (OnPostSeedRoleTemplatesAsync)
  - [ ] Seed organization (OnPostSeedOrganizationAsync)
  - [ ] Seed feature flags (OnPostSeedFeatureFlagsAsync)

---

## 20. Admin Pages

### 20.1 Admin Dashboard
- [ ] **What it does**: Admin landing page with quick stats
- [ ] **Where in code**: `Pages/Admin/Index.cshtml`, `Pages/Admin/Index.cshtml.cs`

### 20.2 Analytics
- [ ] **What it does**: Operational analytics and reports
- [ ] **Where in code**: `Pages/Admin/Analytics.cshtml`, `Pages/Admin/Analytics.cshtml.cs`, `Services/AnalyticsService.cs`
- [ ] **Sub-features**:
  - [ ] View analytics (OnGetAsync)
  - [ ] Export CSV (OnGetExportCsvAsync)
  - [ ] Analytics DTOs: EmployeeHoursDto, EmployeeShiftCountDto, StaffingIssueDto, BackToBackShiftDto, SwapStatsDto, TimeOffStatsDto, TopSwapperDto

### 20.3 Audit Log
- [ ] **What it does**: View audit trail of system events
- [ ] **Where in code**: `Pages/Admin/AuditLog.cshtml`, `Pages/Admin/AuditLog.cshtml.cs`, `Services/AuditLogService.cs`
- [ ] **Sub-features**:
  - [ ] View logs with filters (OnGetAsync)
  - [ ] Export CSV (OnGetExportCsvAsync)

### 20.4 Announcements
- [ ] **What it does**: Manage system-wide announcements
- [ ] **Where in code**: `Pages/Admin/Announcements.cshtml`, `Pages/Admin/Announcements.cshtml.cs`, `Services/AnnouncementService.cs`
- [ ] **Sub-features**:
  - [ ] Create announcement (OnPostCreateAsync)
  - [ ] Toggle active/inactive (OnPostToggleActiveAsync)
  - [ ] Delete announcement (OnPostDeleteAsync)

### 20.5 App Configuration
- [ ] **What it does**: Configure company-level settings
- [ ] **Where in code**: `Pages/Admin/Config.cshtml`, `Pages/Admin/Config.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Save config (OnPostAsync): RestHours, WeeklyHoursCap
  - [ ] Add on-duty type (OnPostAddOnDutyTypeAsync)
  - [ ] Delete on-duty type (OnPostDeleteOnDutyTypeAsync)

### 20.6 Director Management
- [ ] **What it does**: Assign directors to companies
- [ ] **Where in code**: `Pages/Admin/Directors.cshtml`, `Pages/Admin/Directors.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Assign director to company (OnPostAssignAsync)
  - [ ] Revoke director access (OnPostRevokeAsync)
  - [ ] Reassign director (OnPostReassignAsync)

### 20.7 Settings
- [ ] **What it does**: Admin-level settings page
- [ ] **Where in code**: `Pages/Admin/Settings/Index.cshtml`, `Pages/Admin/Settings/Index.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] View settings (OnGetAsync)
  - [ ] Save settings (OnPostAsync)

### 20.8 Setup Tasks
- [ ] **What it does**: Onboarding/setup task tracking for new molecules/companies
- [ ] **Where in code**: `Pages/Admin/SetupTasks/Index.cshtml`, `Pages/Admin/SetupTasks/Index.cshtml.cs`, `Services/SetupTaskService.cs`
- [ ] **Sub-features**:
  - [ ] Complete task (OnPostCompleteAsync)
  - [ ] Skip task (OnPostSkipAsync)
  - [ ] Start task (OnPostStartAsync)
  - [ ] Feature flag: `FF_SETUP_TASKS_ENABLED`

### 20.9 Organization Management
- [ ] **What it does**: Central organization management page
- [ ] **Where in code**: `Pages/Admin/Organization/Index.cshtml`, `Pages/Admin/Organization/Index.cshtml.cs`

### 20.10 Job Types
- [ ] **What it does**: CRUD for job types (e.g., Alhut, Text, BR, Hakam)
- [ ] **Where in code**: `Pages/Admin/Organization/JobTypes/Index.cshtml.cs`, `Services/JobTypeService.cs`
- [ ] **Sub-features**:
  - [ ] Create job type (OnPostCreateAsync)
  - [ ] Toggle active (OnPostToggleActiveAsync)
  - [ ] Delete job type (OnPostDeleteAsync)

### 20.11 Shift Groupings
- [ ] **What it does**: Group shift types for filtering
- [ ] **Where in code**: `Pages/Admin/Organization/ShiftGroupings/Index.cshtml.cs`, `Services/ShiftGroupingService.cs`
- [ ] **Sub-features**:
  - [ ] Create grouping (OnPostCreateAsync)
  - [ ] Toggle active (OnPostToggleActiveAsync)
  - [ ] Delete grouping (OnPostDeleteAsync)
  - [ ] Company and JobType associations

---

## 21. Public Pages

### 21.1 Public Chores Calendar
- [ ] **What it does**: Publicly accessible chore calendar
- [ ] **Where in code**: `Pages/Public/Chores.cshtml`, `Pages/Public/Chores.cshtml.cs`
- [ ] **How it works**: AllowAnonymous page; when user is authenticated and Excel calendars enabled, redirects to `/Calendar/Chores`
- [ ] **Sub-features**:
  - [ ] Create chore (OnPostCreateChoreAsync)
  - [ ] Replace shift with chore (OnPostReplaceShiftWithChoreAsync)
  - [ ] Cancel chore (OnPostCancelChoreAsync)

### 21.2 Public On-Duty Calendar
- [ ] **What it does**: Publicly accessible on-duty calendar
- [ ] **Where in code**: `Pages/Public/OnDuty.cshtml`, `Pages/Public/OnDuty.cshtml.cs`
- [ ] **How it works**: AllowAnonymous page; redirects to `/Calendar/OnCall` for authenticated users when enabled
- [ ] **Sub-features**:
  - [ ] Create on-duty (OnPostCreateOnDutyAsync)
  - [ ] Cancel on-duty (OnPostCancelOnDutyAsync)

### 21.3 Feedback
- [ ] **What it does**: Submit and manage user feedback
- [ ] **Where in code**: `Pages/Public/Feedback.cshtml`, `Pages/Public/Feedback.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Submit feedback (OnPostSubmitAsync)
  - [ ] Mark to work on (OnPostMarkToWorkOnAsync) - admin
  - [ ] Delete feedback (OnPostDeleteAsync) - admin
  - [ ] Sends FeedbackSubmitted notification

---

## 22. Director Features

### 22.1 Director Dashboard
- [ ] **What it does**: Director's home page with cross-company overview
- [ ] **Where in code**: `Pages/Director/Index.cshtml`, `Pages/Director/Index.cshtml.cs`
- [ ] **How it works**: Shows data aggregated across director's assigned companies

### 22.2 Company Filter (Director)
- [ ] **What it does**: Filter data by company
- [ ] **Where in code**: `Pages/Director/CompanyFilter.cshtml`, `Pages/Director/CompanyFilter.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Set filter (OnPostSetFilterAsync)
  - [ ] Clear filter (OnPostClearFilterAsync)

### 22.3 View-As Mode
- [ ] **What it does**: Director views the system as if they were a manager of a specific company
- [ ] **Where in code**: `Pages/Director/ViewAsMode.cshtml`, `Pages/Director/ViewAsMode.cshtml.cs`, `Services/ViewAsModeService.cs`
- [ ] **Sub-features**:
  - [ ] Enter view-as mode (OnPostEnterAsync)
  - [ ] Exit view-as mode (OnPostExitAsync)
  - [ ] Visual indicator when in view-as mode (banner in layout)

---

## 23. My Pages (Personal)

### 23.1 Personal Dashboard
- [ ] **What it does**: Employee's personal dashboard
- [ ] **Where in code**: `Pages/My/Index.cshtml`, `Pages/My/Index.cshtml.cs`
- [ ] **How it works**: Shows timeline-style view of upcoming shifts, requests, and notifications

### 23.2 My Profile
- [ ] **What it does**: View and edit own profile
- [ ] **Where in code**: `Pages/My/Profile.cshtml`, `Pages/My/Profile.cshtml.cs`, `Services/ProfileService.cs`
- [ ] **Sub-features**:
  - [ ] Edit profile fields (OnPostAsync)
  - [ ] Avatar upload/delete (OnPostDeleteAvatarAsync)
  - [ ] Avatar service (`Services/AvatarService.cs`)

### 23.3 My Settings
- [ ] **What it does**: User preference settings
- [ ] **Where in code**: `Pages/My/Settings.cshtml`, `Pages/My/Settings.cshtml.cs`, `Services/UserPreferenceService.cs`
- [ ] **Sub-features**:
  - [ ] Notification preferences
  - [ ] Display preferences
  - [ ] Daily notification opt-in/out

### 23.4 Help Page
- [ ] **What it does**: Static help/documentation page
- [ ] **Where in code**: `Pages/My/Help.cshtml`, `Pages/My/Help.cshtml.cs`

### 23.5 Onboarding Wizard
- [ ] **What it does**: First-login walkthrough for new users
- [ ] **Where in code**: `Pages/My/Onboarding.cshtml`, `Pages/My/Onboarding.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Complete onboarding (OnPostCompleteAsync) - sets `HasCompletedOnboarding`

---

## 24. Friends System

### 24.1 Friendships
- [ ] **What it does**: Users can add friends for calendar highlighting
- [ ] **Where in code**: `Pages/Friends/Index.cshtml`, `Pages/Friends/Index.cshtml.cs`, `Services/FriendshipService.cs`
- [ ] **Sub-features**:
  - [ ] Send friend request (OnPostSendRequestAsync)
  - [ ] Accept request (OnPostAcceptAsync)
  - [ ] Reject request (OnPostRejectAsync)
  - [ ] Remove friend (OnPostRemoveAsync)
  - [ ] Cancel pending request (OnPostCancelRequestAsync)
  - [ ] Feature flag: `FF_FRIENDSHIPS_ENABLED`

### 24.2 Friends API
- [ ] **What it does**: API for getting friend IDs (for calendar highlighting)
- [ ] **Where in code**: `Pages/Api/Friends/Ids.cshtml.cs`

### 24.3 Friends Highlight
- [ ] **What it does**: Highlight friends' assignments on calendars
- [ ] **Where in code**: `wwwroot/js/friends-highlight.js`

---

## 25. Gamification

### 25.1 Match-3 Easter Egg Game ("Shift Swap")
- [ ] **What it does**: Hidden match-3 puzzle game triggered by Ctrl+Click on brand
- [ ] **Where in code**: `wwwroot/js/shift-swap-game.js`, `wwwroot/css/shift-swap-game.css`
- [ ] **How it works**: 6x6 grid of shift icons; match 3+ for points; mega combos; milestones with roasting messages
- [ ] **User-facing behavior**: Modal game overlay
- [ ] **Sub-features**:
  - [ ] Configurable grid size, point values, milestones
  - [ ] Localized (EN/HE)
  - [ ] Score persistence via API
  - [ ] Leaderboard

### 25.2 Game API
- [ ] **What it does**: Backend APIs for game functionality
- [ ] **Where in code**: `Pages/Api/Game/GetConfiguration.cshtml.cs`, `GetLeaderboard.cshtml.cs`, `GetLocalization.cshtml.cs`, `SaveScore.cshtml.cs`
- [ ] **Sub-features**:
  - [ ] Get configuration (grid size, points, milestones)
  - [ ] Get leaderboard (all-time or by period)
  - [ ] Get localization strings
  - [ ] Save score

### 25.3 Leaderboard Page
- [ ] **What it does**: Full leaderboard view
- [ ] **Where in code**: `Pages/Game/Leaderboard.cshtml`, `Pages/Game/Leaderboard.cshtml.cs`

---

## 26. Dark Mode / Theming

### 26.1 Theme Toggle
- [ ] **What it does**: Switch between light and dark themes
- [ ] **Where in code**: `wwwroot/js/site.js` (dark mode toggle), `wwwroot/css/tokens.css`
- [ ] **How it works**: Sets `data-theme` attribute on `<html>`; persists in localStorage; respects system `prefers-color-scheme`
- [ ] **User-facing behavior**: Moon/sun toggle button in navigation

### 26.2 CSS Custom Properties
- [ ] **What it does**: Design token system for theming
- [ ] **Where in code**: `wwwroot/css/tokens.css`, `wwwroot/css/site.css`
- [ ] **How it works**: CSS custom properties with `[data-theme="dark"]` overrides

### 26.3 System Preference Detection
- [ ] **What it does**: Automatically matches system dark mode preference
- [ ] **Where in code**: `wwwroot/js/site.js` (lines 26-43)
- [ ] **How it works**: Uses `window.matchMedia('(prefers-color-scheme: dark)')` with change listener

---

## 27. Print & Export

### 27.1 Calendar Print
- [ ] **What it does**: Print-friendly calendar views
- [ ] **Where in code**: `wwwroot/js/calendar-print.js`, `wwwroot/css/print.css`
- [ ] **How it works**: Print stylesheet hides navigation and non-essential elements

### 27.2 Schedule Export
- [ ] **What it does**: Export schedule data programmatically
- [ ] **Where in code**: `Pages/Api/ScheduleExport.cshtml.cs`, `Services/ScheduleExportService.cs`
- [ ] **How it works**: POST endpoint that accepts export parameters and returns formatted data

### 27.3 CSV Exports
- [ ] **What it does**: Export various data as CSV
- [ ] **Where in code**: Various pages' `OnGetExportCsvAsync` handlers
- [ ] **Sub-features**:
  - [ ] User list CSV export (Admin/Users)
  - [ ] Analytics CSV export (Admin/Analytics)
  - [ ] Audit log CSV export (Admin/AuditLog, Owner/Hub/AuditSearch)
  - [ ] Email log CSV export (Owner/EmailConfig)

---

## 28. Keyboard Shortcuts / Command Palette / Accessibility

### 28.1 Keyboard Navigation
- [ ] **What it does**: Full keyboard navigation for the application
- [ ] **Where in code**: `wwwroot/js/keyboard-nav.js`
- [ ] **Sub-features**:
  - [ ] Dropdown/menu navigation (Arrow keys, Enter, ESC)
  - [ ] Calendar grid navigation (Arrow keys)
  - [ ] Global ESC handler for overlays
  - [ ] Space toggles checkboxes
  - [ ] Enter activates buttons/links
  - [ ] TypeAhead support in dropdowns

### 28.2 Modal Focus Management
- [ ] **What it does**: Focus trap and keyboard handling for modals
- [ ] **Where in code**: `wwwroot/js/modal-focus.js`
- [ ] **How it works**: Traps Tab focus within modal; closes on ESC; returns focus on close

### 28.3 Accessibility Enhancements
- [ ] **What it does**: aria-describedby, landmarks, aria-busy utilities
- [ ] **Where in code**: `wwwroot/js/a11y-enhancements.js`

### 28.4 Skip Navigation Link
- [ ] **What it does**: Skip-to-content link for screen readers
- [ ] **Where in code**: `Pages/Shared/_Layout.cshtml` (skip link element)

### 28.5 Reduced Motion Support
- [ ] **What it does**: Respects user's reduced motion preference
- [ ] **Where in code**: `wwwroot/js/reduced-motion.js`
- [ ] **How it works**: Detects `prefers-reduced-motion: reduce` and adjusts animations

---

## 29. Offline Detection & Error Handling

### 29.1 Offline Handler
- [ ] **What it does**: Detects offline state and queues form submissions
- [ ] **Where in code**: `wwwroot/js/offline-handler.js`
- [ ] **How it works**: Monitors `navigator.onLine`; shows offline banner; queues failed requests
- [ ] **User-facing behavior**: Yellow/red banner when connection lost; retry when back online

### 29.2 Error States & Toast Notifications
- [ ] **What it does**: Handles API errors and displays toast notifications
- [ ] **Where in code**: `wwwroot/js/error-states.js`, `wwwroot/js/toast-notifications.js`
- [ ] **How it works**: Global error handler; API error interception; toast notification system with success/error/warning types

### 29.3 Error Boundary
- [ ] **What it does**: Graceful error handling with fallback UI
- [ ] **Where in code**: `wwwroot/js/error-boundary.js`

### 29.4 Partial Data Handler
- [ ] **What it does**: Section-level loading/error states with retry
- [ ] **Where in code**: `wwwroot/js/partial-data.js`

### 29.5 Error Pages
- [ ] **What it does**: Custom error pages
- [ ] **Where in code**: `Pages/Error.cshtml`, `Pages/AccessDenied.cshtml`
- [ ] **Sub-features**:
  - [ ] Generic error page (Error.cshtml)
  - [ ] Access denied page with return URL (AccessDenied.cshtml)

---

## 30. SignalR Real-Time Updates

### 30.1 Calendar Hub
- [ ] **What it does**: WebSocket push updates for calendar changes
- [ ] **Where in code**: `Hubs/CalendarHub.cs`, `wwwroot/js/calendar-realtime.js`
- [ ] **How it works**: Clients join groups based on scope; server pushes events on changes
- [ ] **Group patterns**:
  - [ ] `shifts-{moleculeId}-{jobTypeId}` for shift assignments
  - [ ] `chores-{moleculeId}` for chore assignments
  - [ ] `oncall-{areaId}` for on-duty assignments
  - [ ] `overview-{companyId}` for overview changes
- [ ] **Event types**:
  - [ ] AssignmentChanged (shift assignment create/delete)
  - [ ] CapacityChanged (staffing level change)
  - [ ] NoteChanged (user day note create/update/delete)
  - [ ] ChoreChanged (chore assignment create/delete/update)
  - [ ] OnCallChanged (on-duty assignment create/delete/update)

### 30.2 Reconnection & Fallback
- [ ] **What it does**: Automatic reconnection with polling fallback
- [ ] **Where in code**: `wwwroot/js/calendar-realtime.js`
- [ ] **Sub-features**:
  - [ ] Layer 1: SignalR WebSocket (primary)
  - [ ] Layer 2: Shadow refresh on user interaction (cell click, date nav, filter change, tab return)
  - [ ] Layer 3: Periodic polling (60s) if SignalR disconnected > 30s
  - [ ] Exponential backoff reconnection (0, 2s, 5s, 10s, 30s)
  - [ ] Max 10 reconnect attempts

### 30.3 Group Access Validation
- [ ] **What it does**: Prevents cross-tenant data leaks via SignalR
- [ ] **Where in code**: `Hubs/CalendarHub.cs` (ValidateGroupAccessAsync)
- [ ] **How it works**: Validates user's CompanyId against group scope before joining
- [ ] **Sub-features**:
  - [ ] Rate limiting on join/leave (30/min per user)

---

## 31. Security Features

### 31.1 CSRF Protection
- [ ] **What it does**: Anti-forgery token on all POST requests
- [ ] **Where in code**: `Pages/Shared/_Layout.cshtml` (script interceptor), `@Html.AntiForgeryToken()`
- [ ] **How it works**: Intercepts all fetch POST/PUT/DELETE to add RequestVerificationToken header

### 31.2 Content Security Policy
- [ ] **What it does**: CSP headers on all responses
- [ ] **Where in code**: `Program.cs` (lines 1166-1193)
- [ ] **Policy**: `default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' ws: wss:; frame-ancestors 'none'`

### 31.3 Security Headers
- [ ] **What it does**: X-Frame-Options, X-Content-Type-Options, Referrer-Policy
- [ ] **Where in code**: `Program.cs` (lines 1166-1193)
- [ ] **Headers**: X-Frame-Options: DENY, X-Content-Type-Options: nosniff, Referrer-Policy: strict-origin-when-cross-origin, Server/X-Powered-By/X-AspNet-Version removed

### 31.4 UI Rate Limiting
- [ ] **What it does**: Rate limits for calendar, context, and widget endpoints
- [ ] **Where in code**: `Middleware/RateLimitingMiddleware.cs`, `Services/RateLimitingService.cs`
- [ ] **How it works**: Uses user ID or IP for rate limiting; separate from API key limits

### 31.5 PII Masking
- [ ] **What it does**: Masks sensitive data in logs
- [ ] **Where in code**: `Services/PiiMasker.cs`
- [ ] **How it works**: Masks email addresses in log messages

### 31.6 Security Logger
- [ ] **What it does**: Dedicated security event logging
- [ ] **Where in code**: `Services/SecurityLogger.cs`

### 31.7 Encryption Service
- [ ] **What it does**: Encrypt/decrypt sensitive configuration data
- [ ] **Where in code**: `Services/EncryptionService.cs`
- [ ] **How it works**: Uses DataProtection API for encryption of email API keys etc.

### 31.8 Data Protection
- [ ] **What it does**: Key management for encryption
- [ ] **Where in code**: `Program.cs` (lines 190-199)
- [ ] **How it works**: Keys persisted to filesystem; DPAPI protection on Windows

---

## 32. Diagnostic / Debug Pages

### 32.1 Diagnostic Page
- [ ] **What it does**: System diagnostic information
- [ ] **Where in code**: `Pages/Diagnostic.cshtml`, `Pages/Diagnostic.cshtml.cs`
- [ ] **How it works**: Shows system info, database stats, user context, claims, configuration

### 32.2 Griffin Diagnostic Page
- [ ] **What it does**: Griffin ADFS diagnostic and troubleshooting
- [ ] **Where in code**: `Pages/GriffinDiagnostic.cshtml`, `Pages/GriffinDiagnostic.cshtml.cs`
- [ ] **How it works**: Shows Griffin config, connection status, URL comparison, token info

---

## 33. Job Types & Shift Groupings

### 33.1 Job Types
- [ ] **What it does**: Define job types for workforce categorization
- [ ] **Where in code**: `Models/JobType.cs`, `Services/JobTypeService.cs`, `Pages/Admin/Organization/JobTypes/Index.cshtml.cs`
- [ ] **How it works**: Job types (e.g., Alhut, Text, BR, Hakam) determine which shifts a user can be assigned to

### 33.2 Shift Groupings
- [ ] **What it does**: Group shift types for filtering on calendars
- [ ] **Where in code**: `Models/ShiftGrouping.cs`, `Models/ShiftGroupingCompany.cs`, `Models/ShiftGroupingJobType.cs`, `Services/ShiftGroupingService.cs`
- [ ] **How it works**: Groupings can span multiple companies and job types; used as calendar filter

---

## 34. Team Calendars

### 34.1 My Team Calendar
- [ ] **What it does**: Custom team calendar with aggregated view
- [ ] **Where in code**: `Pages/MyTeam/Index.cshtml`, `Pages/MyTeam/Index.cshtml.cs`, `Services/TeamCalendarService.cs`, `Services/TeamCalendarEventAggregator.cs`, `Controllers/TeamCalendarsController.cs`
- [ ] **How it works**: Users create custom team calendars with selected members; aggregates shift, chore, on-duty, and time-off data
- [ ] **Sub-features**:
  - [ ] Get my calendars (GET /api/team-calendars)
  - [ ] Get calendar (GET /api/team-calendars/{id})
  - [ ] Create calendar (POST /api/team-calendars)
  - [ ] Rename calendar (PUT /api/team-calendars/{id})
  - [ ] Delete calendar (DELETE /api/team-calendars/{id})
  - [ ] Get members (GET /api/team-calendars/{id}/members)
  - [ ] Set members (PUT /api/team-calendars/{id}/members)
  - [ ] Get week view (GET /api/team-calendars/{id}/week)

---

## 35. Tech Shift Features

### 35.1 Tech Shift Service
- [ ] **What it does**: Department-scoped tech shift eligibility and filtering
- [ ] **Where in code**: `Services/TechShiftService.cs`, `Pages/Api/TechShift/Eligible.cshtml.cs`
- [ ] **How it works**: For Tech molecules, shifts are scoped by department rather than company

### 35.2 Tech Shift Eligible API
- [ ] **What it does**: Get users eligible for tech shifts
- [ ] **Where in code**: `Pages/Api/TechShift/Eligible.cshtml.cs`

---

## 36. Conflict Detection

### 36.1 Conflict Checker
- [ ] **What it does**: Detects scheduling conflicts (rest violations, double-booking)
- [ ] **Where in code**: `Services/ConflictChecker.cs`
- [ ] **How it works**: Checks RestHours config between shifts; detects overlapping assignments

### 36.2 Busy User Service
- [ ] **What it does**: Determines user availability status per date
- [ ] **Where in code**: `Services/BusyUserService.cs`
- [ ] **How it works**: Checks vacation, existing assignments, on-duty to determine busy status

### 36.3 Concurrency Service
- [ ] **What it does**: Concurrent edit conflict detection
- [ ] **Where in code**: `Services/ConcurrencyService.cs`
- [ ] **How it works**: Detects when two users try to modify the same assignment simultaneously

---

## 37. Client-Side Telemetry

### 37.1 Client Telemetry
- [ ] **What it does**: Local observability for air-gapped environments
- [ ] **Where in code**: `wwwroot/js/telemetry.js`, `Pages/Api/Telemetry.cshtml.cs`, `Services/ClientTelemetryService.cs`
- [ ] **How it works**: Collects analytics events, client errors, and performance metrics; sends in batches
- [ ] **Sub-features**:
  - [ ] Event tracking (OnPostEventAsync, OnPostEventBatchAsync)
  - [ ] Error tracking (OnPostErrorAsync, OnPostErrorBatchAsync)
  - [ ] Performance metrics (OnPostPerformanceAsync, OnPostPerformanceBatchAsync)
  - [ ] AllowAnonymous endpoint

---

## 38. Calendar Skeleton Loading

### 38.1 Skeleton Loading States
- [ ] **What it does**: Skeleton UI during calendar loading
- [ ] **Where in code**: `wwwroot/js/calendar-skeleton.js`, `wwwroot/css/calendar-skeleton.css`, `ViewComponents/CalendarSkeletonViewComponent.cs`
- [ ] **Sub-features**:
  - [ ] Day skeleton
  - [ ] Week skeleton
  - [ ] Month skeleton
  - [ ] Table skeleton

---

## 39. Sidebar & Navigation

### 39.1 Sidebar Navigation
- [ ] **What it does**: Collapsible sidebar with role-based menu items
- [ ] **Where in code**: `Pages/Shared/_Layout.cshtml`, `wwwroot/css/navigation.css`
- [ ] **How it works**: Grant-based visibility for menu items; collapsed state persisted in localStorage
- [ ] **Sub-features**:
  - [ ] Sidebar collapse toggle with localStorage persistence (`shifty_sidebar_collapsed`)
  - [ ] Flash-of-wrong-state prevention (inline script)
  - [ ] Mobile hamburger menu (`wwwroot/js/mobile-nav.js`)
  - [ ] User menu dropdown
  - [ ] Active page highlighting

### 39.2 Sidebar Menu Items (Grant-Controlled)
- [ ] **What it does**: Navigation items shown based on user grants
- [ ] **Sub-features**:
  - [ ] Calendar section: Shifts, Chores, On-Duty, Overview
  - [ ] Requests section
  - [ ] My section: Dashboard, Profile, Settings, Notifications, API Keys, Help
  - [ ] Admin section: Users, Analytics, Audit Log, Config, Companies, Organization
  - [ ] Director section: Dashboard, Notification Hub, Company Filter, View-As Mode
  - [ ] Owner section: Dashboard, Hub, Blueprints, Programs, Master Programs, Database Console, Backup, System Health, Feature Flags, Griffin Config, Email Config, Email Templates, Language Management, Locked Users, Data Lifecycle, Area Config, Game Config, Permissions, Telemetry
  - [ ] Public section: Chores, On-Duty, Feedback
  - [ ] Friends section (feature flag gated)
  - [ ] Game/Leaderboard section

---

## 40. Widget System

### 40.1 Dashboard Widgets
- [ ] **What it does**: Collapsible widgets on dashboard
- [ ] **Where in code**: `wwwroot/js/widget-persistence.js`, `wwwroot/css/widgets.css`, `Services/WidgetService.cs`
- [ ] **How it works**: Widgets remember collapsed/expanded state in localStorage
- [ ] **Sub-features**:
  - [ ] On-Call Widget (`ViewComponents/OnCallWidgetViewComponent.cs`)
  - [ ] Feature flag: `FF_WIDGETS_ENABLED`

### 40.2 System Alerts
- [ ] **What it does**: Shows system-level alerts
- [ ] **Where in code**: `ViewComponents/SystemAlertsViewComponent.cs`

### 40.3 Decision Ribbon
- [ ] **What it does**: Quick action ribbon for pending decisions
- [ ] **Where in code**: `ViewComponents/DecisionRibbonViewComponent.cs`, `Models/ViewModels/DecisionRibbonViewModel.cs`

---

## 41. Lazy Loading & Performance

### 41.1 Code Splitting / Lazy Loading
- [ ] **What it does**: Deferred loading of non-critical JS
- [ ] **Where in code**: `wwwroot/js/lazy-loader.js`, `wwwroot/js/modal-loader.js`

### 41.2 Calendar Lazy Rows
- [ ] **What it does**: Lazy load calendar rows for large datasets
- [ ] **Where in code**: `wwwroot/js/calendar-lazy-rows.js`

### 41.3 Response Compression
- [ ] **What it does**: Gzip compression for responses
- [ ] **Where in code**: `Program.cs` (lines 74-80)

### 41.4 JS/CSS Minification
- [ ] **What it does**: Minifies JS and CSS files
- [ ] **Where in code**: `Program.cs` (lines 83-90)
- [ ] **How it works**: WebOptimizer pipeline for minification

### 41.5 Static File Caching
- [ ] **What it does**: Smart cache control for static assets
- [ ] **Where in code**: `Program.cs` (lines 1196-1218)
- [ ] **How it works**: Versioned assets get 7-day immutable cache; non-versioned require revalidation

### 41.6 Memory Cache
- [ ] **What it does**: In-memory caching for frequently accessed data
- [ ] **Where in code**: `Services/ShiftTypeCacheService.cs`, `Services/AppConfigCacheService.cs`, `Services/CompanyCacheService.cs`
- [ ] **How it works**: IMemoryCache with 5-min expiration scan, 25% compaction

---

## 42. Scheduling & Background Services

### 42.1 Email Background Processor
- [ ] **What it does**: Processes email queue asynchronously
- [ ] **Where in code**: `Services/EmailBackgroundProcessor.cs`, `Services/EmailBackgroundQueue.cs`

### 42.2 Daily Notification Job
- [ ] **What it does**: Sends daily digest notifications
- [ ] **Where in code**: `Services/DailyNotificationJob.cs`

### 42.3 Database Backup Service
- [ ] **What it does**: Automated periodic database backups
- [ ] **Where in code**: `Services/DatabaseBackupService.cs`

### 42.4 Graceful Shutdown Service
- [ ] **What it does**: WAL checkpoint on IIS app pool recycle
- [ ] **Where in code**: `Services/GracefulShutdownService.cs`

---

## 43. Calendar Radar Mode

### 43.1 Calendar Radar
- [ ] **What it does**: Visual indicator mode for calendar
- [ ] **Where in code**: `wwwroot/js/calendar-radar.js`

---

## 44. Schedule View

### 44.1 Schedule Index
- [ ] **What it does**: Alternative schedule view
- [ ] **Where in code**: `Pages/Schedule/Index.cshtml`, `Pages/Schedule/Index.cshtml.cs`
- [ ] **How it works**: Supports view and mode query parameters

---

## 45. Form Validation

### 45.1 Client-Side Form Validation
- [ ] **What it does**: Enhanced accessible form validation
- [ ] **Where in code**: `wwwroot/js/form-validation.js`
- [ ] **How it works**: Accessible error states, field highlighting, live validation

---

## 46. Seed Data

### 46.1 Organization Seed
- [ ] **What it does**: Seeds the Shifty organization hierarchy
- [ ] **Where in code**: `Data/SeedData/ShiftyOrganizationSeed.cs`
- [ ] **Seeds**: Project "Shifty", Area "190", Molecules (Alhut, Text, BR, Hakam, System), Companies per molecule, HQ companies, SystemAdmins company, JobTypes

### 46.2 Grant Type Seed
- [ ] **What it does**: Seeds 107+ grant types
- [ ] **Where in code**: `Data/SeedData/GrantTypeSeed.cs`
- [ ] **Categories**: Shift (11), Duty (4), Chore (4), Vacation (5), Swap (3), UserManagement (7), GrantManagement (4), Hierarchy (10+), Admin (10+), System (5+), Notification (4+), Data (4+), Overview (2+), Report (2+), Calendar (5+), Navigation (5+), Config (5+), Director (5+)

### 46.3 Role Template Seed
- [ ] **What it does**: Seeds predefined role templates
- [ ] **Where in code**: `Data/SeedData/RoleTemplateSeed.cs`
- [ ] **Templates**: Owner, AlhutLead, TextLead, AlhutSoldier, TextSoldier, BRDirector, HakamDirector, AreaAdmin, MoleculeAdmin, Trainee, Assigner, TechLead, TechSoldier

### 46.4 Feature Flag Seed
- [ ] **What it does**: Seeds all feature flags
- [ ] **Where in code**: `Data/SeedData/FeatureFlagSeed.cs`
- [ ] **Count**: 50+ flags covering UI, operational, and API features

### 46.5 Test Data Seed
- [ ] **What it does**: Seeds test users and data for QA
- [ ] **Where in code**: `Data/SeedData/TestDataSeed.cs`, `Data/TestDataSeeder.cs`

### 46.6 Tech Shift Type Seed
- [ ] **What it does**: Seeds shift types for tech molecules
- [ ] **Where in code**: `Data/SeedData/TechShiftTypeSeed.cs`

### 46.7 Default Shift Types
- [ ] **What it does**: Seeds MORNING, NOON, NIGHT, MIDDLE, OFFLINE shift types
- [ ] **Where in code**: `Program.cs` (lines 861-878)

### 46.8 Default App Config
- [ ] **What it does**: Seeds RestHours (8), WeeklyHoursCap (40), game configuration
- [ ] **Where in code**: `Program.cs` (lines 882-908)

---

## 47. Middleware Pipeline

### 47.1 Correlation ID Middleware
- [ ] **What it does**: Adds unique request ID for distributed tracing
- [ ] **Where in code**: `Middleware/CorrelationIdMiddleware.cs`

### 47.2 Request Logging Middleware
- [ ] **What it does**: Logs all HTTP requests
- [ ] **Where in code**: `Middleware/RequestLoggingMiddleware.cs`

### 47.3 Company Context Middleware
- [ ] **What it does**: Sets tenant context from user claims
- [ ] **Where in code**: `Middleware/CompanyContextMiddleware.cs`

---

## 48. View Components

### 48.1 Breadcrumb
- [ ] **What it does**: Breadcrumb navigation component
- [ ] **Where in code**: `ViewComponents/BreadcrumbViewComponent.cs`

### 48.2 Pagination
- [ ] **What it does**: Reusable pagination component
- [ ] **Where in code**: `ViewComponents/PaginationViewComponent.cs`, `Pages/Shared/Components/Pagination/Default.cshtml`

### 48.3 Loading States
- [ ] **What it does**: Loading spinner and skeleton components
- [ ] **Where in code**: `ViewComponents/LoadingSpinnerViewComponent.cs`, `ViewComponents/LoadingSkeletonViewComponent.cs`

### 48.4 Error Components
- [ ] **What it does**: Reusable error display components
- [ ] **Where in code**: `ViewComponents/ErrorBannerViewComponent.cs`, `ViewComponents/ErrorToastViewComponent.cs`

### 48.5 Show My Items Toggle
- [ ] **What it does**: "Just Mine" toggle component for calendars
- [ ] **Where in code**: `ViewComponents/ShowMyItemsToggleViewComponent.cs`, `Pages/Shared/Components/ShowMyItemsToggle/Default.cshtml`

### 48.6 Language Edit Mode Banner
- [ ] **What it does**: Banner indicating language edit mode is active
- [ ] **Where in code**: `ViewComponents/LanguageEditModeBannerViewComponent.cs`

---

## 49. Mobile Support

### 49.1 Mobile Navigation
- [ ] **What it does**: Hamburger menu with focus trap for mobile
- [ ] **Where in code**: `wwwroot/js/mobile-nav.js`

### 49.2 Responsive Layout
- [ ] **What it does**: Responsive CSS for all screen sizes
- [ ] **Where in code**: `wwwroot/css/site.css`

### 49.3 PWA Manifest
- [ ] **What it does**: Web app manifest for mobile install
- [ ] **Where in code**: Referenced in `_Layout.cshtml`: `~/site.webmanifest`
- [ ] **Sub-features**:
  - [ ] Apple touch icon
  - [ ] Theme color (#1E3A5F)
  - [ ] Apple mobile web app capable

---

## 50. Startup Safety Checks

### 50.1 Connection String Validation
- [ ] **What it does**: Fails fast if DB connection string missing
- [ ] **Where in code**: `Program.cs` (lines 128-130)

### 50.2 Owner Email Validation
- [ ] **What it does**: Fails fast if seeding owner email missing
- [ ] **Where in code**: `Program.cs` (lines 132-134)

### 50.3 SQLite Network Share Detection
- [ ] **What it does**: Warns if DB is on network share
- [ ] **Where in code**: `Program.cs` (lines 1419-1432)

### 50.4 Timezone Assertion
- [ ] **What it does**: Warns if server timezone is not Israel
- [ ] **Where in code**: `Program.cs` (lines 1434-1447)

### 50.5 Default Credentials Warning
- [ ] **What it does**: Warns if using default password in production
- [ ] **Where in code**: `Program.cs` (lines 1449-1456)

### 50.6 HMAC Secret Validation
- [ ] **What it does**: Refuses to start without HMAC secret in production
- [ ] **Where in code**: `Program.cs` (lines 1268-1279)

### 50.7 Data Protection Key Check
- [ ] **What it does**: Verifies encryption keys are available
- [ ] **Where in code**: `Program.cs` (lines 1389-1417)

### 50.8 Pre-Migration Backup
- [ ] **What it does**: Backs up database before running migrations
- [ ] **Where in code**: `Program.cs` (lines 421-442)

### 50.9 SQLite WAL Mode
- [ ] **What it does**: Enables WAL mode for concurrent reads
- [ ] **Where in code**: `Program.cs` (lines 451-479)

### 50.10 Feature Flag Cache Warming
- [ ] **What it does**: Preloads feature flags into memory cache
- [ ] **Where in code**: `Program.cs` (lines 1115-1119)

---

## 51. API Client Utilities

### 51.1 API Client with Rate Limit Handling
- [ ] **What it does**: Client-side API wrapper with retry
- [ ] **Where in code**: `wwwroot/js/api-client.js`
- [ ] **How it works**: Automatic retry with exponential backoff on rate limit (429) responses

### 51.2 Cache Management
- [ ] **What it does**: Ensures fresh data after mutations
- [ ] **Where in code**: `wwwroot/js/cache-management.js`

### 51.3 Date Format Localization
- [ ] **What it does**: Client-side date formatting for Hebrew/English
- [ ] **Where in code**: `wwwroot/js/date-format.js`

---

## 52. Seeded Configuration (appsettings.json)

### 52.1 Configurable Seeding
- [ ] **What it does**: Seed data from configuration file
- [ ] **Where in code**: `Configuration/SeedingOptions.cs`, `Program.cs` (lines 740-844)
- [ ] **Sub-features**:
  - [ ] Owner email/password/display name
  - [ ] Additional molecules from config
  - [ ] Additional companies from config
  - [ ] Additional departments from config

### 52.2 Environment-Specific Configuration
- [ ] **What it does**: Different settings per environment
- [ ] **Where in code**: `appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json`

---

## 53. Logging & Observability

### 53.1 Structured Logging
- [ ] **What it does**: JSON structured logging in production
- [ ] **Where in code**: `Program.cs` (lines 22-68)
- [ ] **Sub-features**:
  - [ ] Simple console in development
  - [ ] JSON console in production
  - [ ] Windows Event Log for critical events
  - [ ] EF Core noise reduction

### 53.2 Startup Timing
- [ ] **What it does**: Logs startup duration for diagnostics
- [ ] **Where in code**: `Program.cs` (lines 409, 1121-1128)

### 53.3 Startup Banner
- [ ] **What it does**: Displays application info at startup
- [ ] **Where in code**: `Program.cs` (DisplayStartupBanner method)

---

## Summary Statistics

- **Total Razor Pages**: 100+ .cshtml files
- **Total Page Handlers**: 200+ OnGet/OnPost handlers
- **REST API Endpoints**: 40+ across 11 controllers
- **Service Interfaces**: 60+ services
- **Entity Models**: 70+ database entities
- **JavaScript Files**: 35+ client-side scripts
- **Feature Flags**: 50+ configurable flags
- **Grant Types**: 107+ fine-grained permissions
- **Role Templates**: 13+ predefined roles
- **Notification Types**: 20 event types
- **Email Template Types**: 14 customizable templates
- **Supported Languages**: 2 (English, Hebrew with RTL)
- **SignalR Groups**: 4 types (shifts, chores, oncall, overview)
- **Background Services**: 4 (email, notifications, backup, shutdown)
- **Health Checks**: 3 (DB, disk, memory)
