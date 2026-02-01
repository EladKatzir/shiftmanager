# UI Permission Alignment Audit Report

**Date:** 2026-02-01
**Task:** B-035 - UI Permission Alignment Audit
**Status:** Audit Complete - Issues Fixed

---

## Executive Summary

This audit examines the alignment between UI elements and user permissions in the ShiftManager application. The application implements a multi-layered authorization system combining:

1. **Grant-Based Authorization** - Fine-grained permissions via `GrantRequirement` and `<require-grant>` tag helpers
2. **Role-Based Policies** - Coarse-grained access via `[Authorize(Policy = "...")]` attributes
3. **Service-Level Checks** - Business logic permission validation
4. **UI Conditional Rendering** - Visual hiding of unauthorized elements

---

## Authorization Architecture Overview

### 1. Grant System (Fine-Grained Permissions)

**Files:**
- `Authorization/GrantAuthorizationHandler.cs`
- `Authorization/GrantPolicyProvider.cs`
- `Authorization/GrantRequirement.cs`
- `TagHelpers/RequireGrantTagHelper.cs`

**Mechanism:**
- Custom `IAuthorizationPolicyProvider` creates policies on-demand for `Grant:*` patterns
- `GrantAuthorizationHandler` checks user grants via `IGrantService.HasGrantAsync()`
- Grants are scoped to Project/Area/Molecule/Company hierarchy

**Usage Patterns:**
```csharp
// Page-level protection
[Authorize(Policy = "Grant:AdminAccess")]

// UI-level protection (Razor)
<require-grant key="ViewAnalytics">
    <!-- Only visible to users with ViewAnalytics grant -->
</require-grant>

// Multiple grants with OR logic
<require-grant key="ViewShifts,ViewChores,ViewDuties" mode="any">
    <!-- Visible if user has ANY of these grants -->
</require-grant>
```

### 2. Role-Based Policies (Coarse-Grained Access)

**Defined in:** `Program.cs` (lines 110-125)

| Policy | Allowed Roles | Purpose |
|--------|--------------|---------|
| `IsManagerOrAdmin` | Manager, Owner, Director | Management features |
| `IsAdmin` | Owner | Owner-only features |
| `IsDirector` | Owner, Director | Director-level features |
| `IsOwnerOrDirector` | Owner, Director | Same as IsDirector |
| `CanViewChores` | All authenticated | View chores |
| `CanViewOnDuty` | All authenticated | View on-duty |
| `CanEditChores` | Manager, Owner, Director, Assigner | Edit chores |
| `CanEditOnDuty` | Manager, Owner, Director | Edit on-duty |

### 3. API Authentication (V1 API)

**Middleware:** `Middleware/ApiAuthenticationMiddleware.cs`

- External API endpoints use X-API-Key header authentication
- Separate from cookie-based session authentication
- Scopes defined per API key for granular access control

---

## Audit Results by Page Category

### Admin Pages (`/Admin/*`)

| Page | Page-Level Auth | UI Permission Checks | Status |
|------|-----------------|---------------------|--------|
| `/Admin/Index` | `IsManagerOrAdmin` | Role-based (`Model.IsOwner`, `Model.IsDirector`) | Partial - Should use grants |
| `/Admin/Analytics` | `IsManagerOrAdmin` | None (page-level only) | OK |
| `/Admin/AuditLog` | `IsManagerOrAdmin` | None (page-level only) | OK |
| `/Admin/Companies` | `Grant:EditCompany` | None (page-level only) | OK |
| `/Admin/Config` | `IsManagerOrAdmin` | None (page-level only) | OK |
| `/Admin/Directors` | `Grant:AssignRoles` | None (page-level only) | OK |
| `/Admin/EditProfile` | `IsManagerOrAdmin` | None (page-level only) | OK |
| `/Admin/ShiftTypes` | `IsManagerOrAdmin` | None (page-level only) | OK |
| `/Admin/Users` | `IsManagerOrAdmin` | `Model.IsOwner` for company selector | OK |

### Admin Organization Pages (`/Admin/Organization/*`)

| Page | Page-Level Auth | Status |
|------|-----------------|--------|
| `/Admin/Organization/Index` | `Grant:ViewHierarchy` | OK |
| `/Admin/Organization/Areas` | `Grant:EditArea` | OK |
| `/Admin/Organization/Departments` | `Grant:ManageDepartments` | OK |
| `/Admin/Organization/Grants/Index` | `Grant:ViewGrants` | OK |
| `/Admin/Organization/Grants/Assign` | `Grant:AssignGrants` | OK |
| `/Admin/Organization/JobTypes` | `Grant:ManageJobTypes` | OK |
| `/Admin/Organization/Molecules` | `Grant:EditMolecule` | OK |
| `/Admin/Organization/Projects` | `Grant:EditArea` | OK |
| `/Admin/Organization/Roles/Index` | `Grant:AssignRoles` | OK |
| `/Admin/Organization/Roles/Assign` | `Grant:AssignRoles` | OK |
| `/Admin/Organization/Settings` | `Grant:ViewSettings` | OK |
| `/Admin/Organization/SetupTasks` | `Grant:SystemConfiguration` | OK |
| `/Admin/Organization/ShiftGroupings` | `Grant:ManageShiftGroupings` | OK |

### Owner Pages (`/Owner/*`)

| Page | Page-Level Auth | Status |
|------|-----------------|--------|
| `/Owner/Index` | `Grant:AdminAccess` | OK |
| `/Owner/Backup` | `Grant:AdminAccess` | OK |
| `/Owner/Blueprints` | `IsManagerOrAdmin` | OK (Shared access) |
| `/Owner/ClearCompanySelection` | `Grant:AdminAccess` | OK |
| `/Owner/DatabaseConsole` | `Grant:SystemConfiguration` | OK |
| `/Owner/DataLifecycle` | `Grant:SystemConfiguration` | OK |
| `/Owner/EmailConfig` | `Grant:ConfigureEmailSettings` | OK |
| `/Owner/EmailTemplates` | `Grant:AdminAccess` | OK |
| `/Owner/FeatureFlags` | `Grant:SystemConfiguration` | OK |
| `/Owner/GameConfig` | `Grant:AdminAccess` | OK |
| `/Owner/GriffinConfig` | `Grant:AdminAccess` | OK |
| `/Owner/LanguageEditMode` | `Grant:AdminAccess` | OK |
| `/Owner/LanguageManagement` | `Grant:AdminAccess` | OK |
| `/Owner/MasterPrograms` | `IsManagerOrAdmin` | OK (Shared access) |
| `/Owner/Programs` | `IsManagerOrAdmin` | OK (Shared access) |
| `/Owner/SelectCompany` | `Grant:AdminAccess` | OK |
| `/Owner/SystemHealth` | `Grant:AdminAccess` | OK |

### Calendar Pages (`/Calendar/*`)

| Page | Page-Level Auth | UI Permission Checks | Status |
|------|-----------------|---------------------|--------|
| `/Calendar/Day` | `[Authorize]` | Role checks for edit buttons | OK |
| `/Calendar/Week` | `[Authorize]` | Role checks for edit buttons | OK |
| `/Calendar/Month` | `[Authorize]` | Role checks for edit buttons, Grant checks for Quick-Add | OK |
| `/Calendar/Table` | `IsManagerOrAdmin` | None (restricted to managers+) | OK |

### Director Pages (`/Director/*`)

| Page | Page-Level Auth | Status |
|------|-----------------|--------|
| `/Director/Index` | `IsDirector` | OK |
| `/Director/CompanyFilter` | `IsDirector` | OK |
| `/Director/NotificationHub` | `IsDirector` | OK |
| `/Director/ViewAsMode` | `IsDirector` | OK |

### Public/User Pages

| Page | Page-Level Auth | UI Permission Checks | Status |
|------|-----------------|---------------------|--------|
| `/Public/Chores` | `CanViewChores` | `AuthorizationService` for edit | OK |
| `/Public/OnDuty` | `CanViewOnDuty` | `AuthorizationService` for edit | OK |
| `/Public/Feedback` | `[Authorize]` | `Model.IsOwner` for admin features | OK |
| `/My/*` pages | `[Authorize]` | User sees only their data | OK |

### API Endpoints

| Endpoint Category | Auth Method | Status |
|-------------------|-------------|--------|
| `/Api/Calendar/*` | Cookie + Policy | OK |
| `/Api/Game/*` | Mixed (some AllowAnonymous) | OK |
| `/Api/ScopeSwitcher` | `[Authorize]` | OK |
| `/api/v1/*` | X-API-Key header | OK |
| `/api/team-calendars` | `[Authorize]` | OK |

---

## Layout Navigation (`_Layout.cshtml`)

The main navigation uses `<require-grant>` extensively:

**Properly Protected:**
- Analytics link: `<require-grant key="ViewAnalytics">`
- Audit Log link: `<require-grant key="ViewAuditLog">`
- Companies link: `<require-grant key="EditCompany">`
- Admin Hub link: `<require-grant key="AdminAccess" mode="any">`
- Settings link: `<require-grant key="ViewSettings">`
- Owner Panel: `<require-grant key="AdminAccess">`
- Shift/Chore/Duty Management: `<require-grant key="ViewShifts,ViewChores,ViewDuties" mode="any">`

**Role-Based (Acceptable):**
- My Team vs People link: Uses `@if (User.IsInRole("Manager"))` - appropriate for role distinction

---

## Identified Issues (FIXED)

### Issue 1: Admin Hub Uses Role Checks Instead of Grants

**Location:** `Pages/Admin/Index.cshtml` (lines 59-270)

**Original Behavior:**
```razor
@if (Model.IsOwner || Model.IsDirector)
{
    <!-- Company & Scope Section -->
}
```

**Fix Applied:** Replaced role-based checks with grant-based `<require-grant>` tags:
```razor
<require-grant key="EditCompany,AssignRoles" mode="any">
    <!-- Company & Scope Section - only visible to users with these grants -->
</require-grant>
```

**Status:** FIXED

### Issue 2: Admin Hub Links Not Grant-Protected

**Location:** `Pages/Admin/Index.cshtml`

**Original Behavior:** Links to grant-protected pages were visible to all managers, leading to "Access Denied" when clicked.

**Fix Applied:** Added `<require-grant>` wrappers around specific links:
- `/Admin/Companies` now wrapped with `<require-grant key="EditCompany">`
- `/Admin/Directors` now wrapped with `<require-grant key="AssignRoles">`
- `/Owner/*` Advanced Tools section wrapped with `<require-grant key="AdminAccess">`
- Specific tools wrapped with appropriate grants:
  - Feature Flags, Database Console, Data Lifecycle: `<require-grant key="SystemConfiguration">`
  - Email Configuration: `<require-grant key="ConfigureEmailSettings">`

**Status:** FIXED

---

## Security Layers Summary

The application implements **defense in depth**:

| Layer | Mechanism | Enforcement Point |
|-------|-----------|-------------------|
| 1. Route-Level | `[Authorize]` attributes | Before page handler |
| 2. Policy-Level | `[Authorize(Policy = "...")]` | Before page handler |
| 3. Grant-Level | `GrantAuthorizationHandler` | Before page handler |
| 4. Service-Level | `CanUserManage*Async()` methods | During business logic |
| 5. UI-Level | `<require-grant>`, `@if (User.IsInRole(...))` | Render time |
| 6. Query-Level | EF Core Global Query Filters | Database query |

---

## Recommendations

### Completed

1. **Admin Hub UX Improved** - Links to grant-protected pages now wrapped with `<require-grant>`.

2. **Pattern Standardized** - Migrated `@if (Model.IsOwner)` checks to `<require-grant key="AdminAccess">` in Admin Hub.

### Remaining (Low Priority)

3. **Document Grant Keys** - Consider creating a reference document listing all available grant keys and their purposes.

---

## Conclusion

The ShiftManager application demonstrates a **well-designed authorization system** with:

- Multiple layers of protection
- Grant-based fine-grained permissions
- Consistent UI permission hiding via `<require-grant>` tag helpers
- Proper API authentication for external integrations

**No critical permission leakage issues were identified.** The Admin Hub page has been updated to hide links to pages the user cannot access using grant-based permission checks.

---

## Related Documentation

- `docs/ROLE_PERMISSIONS_AUDIT.md` - Role capabilities matrix
- `Authorization/` - Grant system implementation
- `TagHelpers/RequireGrantTagHelper.cs` - UI permission tag helper
- `Program.cs` (lines 110-130) - Policy definitions
