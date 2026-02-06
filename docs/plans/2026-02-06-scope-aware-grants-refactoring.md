# Scope-Aware Grants Refactoring Plan

> **Status:** ✅ COMPLETED - 2026-02-06

## Problem Statement

The current authorization system has two types of checks:

1. **Simple authorization** (converted) - "Can this user access this feature?"
   - Uses `<require-grant>` tag helpers in views
   - Uses `IGrantService.HasGrantAsync()` in code-behind
   - Works well for yes/no access decisions

2. **Data-scoping authorization** (not yet converted) - "Which data can this user see/modify?"
   - Currently uses role-based switch statements
   - Determines accessible company IDs based on user role
   - Examples: `Pages/Admin/Users.cshtml.cs`, join request approval/rejection

## Current Role-Based Pattern

```csharp
// Lines 179-201 in Pages/Admin/Users.cshtml.cs
List<int> accessibleCompanyIds;

if (role == UserRole.Owner)
{
    // Owner: all companies
    accessibleCompanyIds = await _db.Companies.IgnoreQueryFilters().Select(c => c.Id).ToListAsync();
}
else if (role == UserRole.Director)
{
    // Director: companies they direct
    accessibleCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUserId);
}
else if (role == UserRole.Manager)
{
    // Manager: their company only
    accessibleCompanyIds = new List<int> { currentUser.CompanyId };
}
```

## Files Requiring Conversion

| File | Lines | Pattern |
|------|-------|---------|
| `Pages/Admin/Users.cshtml.cs` | 165-201 | Data scope calculation |
| `Pages/Admin/Users.cshtml.cs` | 1081-1093 | Permission check for approve |
| `Pages/Admin/Users.cshtml.cs` | 1205-1217 | Permission check for reject |
| `Pages/Admin/Users.cshtml.cs` | 1303-1315 | Permission check for batch |

## Proposed Solution Options

### Option A: Scope-Aware Grant Check Method

Add a new method to check if a user has a grant for a specific target entity:

```csharp
// New method in IGrantService
Task<bool> HasGrantForCompanyAsync(int userId, string grantKey, int targetCompanyId);
Task<bool> HasGrantForAreaAsync(int userId, string grantKey, int targetAreaId);
Task<bool> HasGrantForMoleculeAsync(int userId, string grantKey, int targetMoleculeId);

// Usage
var canApprove = await _grantService.HasGrantForCompanyAsync(
    currentUserId,
    "ManageJoinRequests",
    joinRequest.CompanyId
);
```

**Pros:** Clean API, single responsibility
**Cons:** Multiple methods needed, each call is a query

### Option B: Query-Time Scope Resolution

Add a method to resolve accessible entity IDs based on grants:

```csharp
// New method in IGrantService
Task<List<int>> GetAccessibleCompanyIdsForGrantAsync(int userId, string grantKey);
Task<List<int>> GetAccessibleAreaIdsForGrantAsync(int userId, string grantKey);

// Usage - replaces the role-based switch statement
var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(
    currentUserId,
    "ViewUsers"
);

// Permission check
var canApprove = accessibleCompanyIds.Contains(joinRequest.CompanyId);
```

**Pros:** Single query, cacheable, reusable
**Cons:** May load more data than needed

### Option C: Grant Scope Levels (Recommended)

Introduce explicit scope levels in grants:

```csharp
public enum GrantScopeLevel
{
    Self,              // Only own data
    Company,           // Own company
    AssignedCompanies, // Via DirectorCompanies table
    Area,              // All companies in area
    Molecule,          // All companies in molecule
    Global             // All companies (Owner-level)
}

// In GrantType
public GrantScopeLevel DefaultScopeLevel { get; set; }

// Resolution method
Task<List<int>> ResolveCompanyIdsForGrantAsync(int userId, string grantKey);
```

**Pros:** Explicit, self-documenting, flexible
**Cons:** Requires migration of existing grants

## Implementation Phases

### Phase 1: Design (This Document)
- [x] Document current state
- [x] Identify files requiring conversion
- [x] Propose solution options
- [x] Choose Option B (Query-Time Scope Resolution) - simplest approach
- [x] Define new grant types needed

### Phase 2: Infrastructure ✅ COMPLETED
- [x] `GrantScopeLevel` enum already exists in `Models/Support/GrantScopeLevel.cs`
- [x] Extended `IGrantService` with new methods:
  - `HasGrantForCompanyAsync(userId, grantKey, targetCompanyId)`
  - `GetAccessibleCompanyIdsForGrantAsync(userId, grantKey)`
- [x] Implemented scope resolution logic in `GrantService.cs`
- [ ] Add caching for scope resolution (performance) - deferred

### Phase 3: Create Grants ✅ COMPLETED
Grant types added to `GrantTypeSeed.cs` (IDs 112-114):

| Grant Key | Description | Default Scope |
|-----------|-------------|---------------|
| `ManageJoinRequests` | Approve/reject join requests | Company |
| `ViewCompanyUsers` | View users in scope | Company |
| `EditCompanyUsers` | Edit users in scope | Company |

### Phase 4: Data Migration ✅ COMPLETED
- [x] Grant types seeded in `GrantTypeSeed.cs`
- [x] Updated `RoleTemplateSeed.cs` with new grants per role:
  - Owner (Role 11): All grants with CanGive
  - Area Admin (Role 10): ManageJoinRequests, ViewCompanyUsers, EditCompanyUsers
  - Molecule Admin (Role 7): ManageJoinRequests, ViewCompanyUsers, EditCompanyUsers
  - BR Director (Role 2): ManageJoinRequests

### Phase 5: Convert Pages ✅ COMPLETED
- [x] `Pages/Admin/Users.cshtml.cs` - All role checks converted:
  - OnGetAsync: Uses `GetAccessibleCompanyIdsForGrantAsync("ManageJoinRequests")`
  - OnPostApproveJoinRequestAsync: Uses `HasGrantForCompanyAsync("ManageJoinRequests", companyId)`
  - OnPostRejectJoinRequestAsync: Uses `HasGrantForCompanyAsync("ManageJoinRequests", companyId)`
  - OnPostBatchApproveJoinRequestsAsync: Uses `GetAccessibleCompanyIdsForGrantAsync("ManageJoinRequests")`
  - OnPostRefreshJoinRequestsAsync: Uses `GetAccessibleCompanyIdsForGrantAsync("ManageJoinRequests")`

### Phase 6: Testing ✅ COMPLETED
- [x] Unit tests for new `IGrantService` methods (7 tests in `GrantScopeResolutionTests`)
- [x] Unit tests for scope resolution:
  - `GetAccessibleCompanyIdsForGrantAsync_WithCompanyScope_ReturnsOnlyThatCompany`
  - `GetAccessibleCompanyIdsForGrantAsync_WithMoleculeScope_ReturnsAllCompaniesInMolecule`
  - `GetAccessibleCompanyIdsForGrantAsync_WithSelfScope_ReturnsUserCompany`
  - `GetAccessibleCompanyIdsForGrantAsync_WithNoGrant_ReturnsEmptyList`
  - `HasGrantForCompanyAsync_WithMatchingScope_ReturnsTrue`
  - `HasGrantForCompanyAsync_WithNonMatchingScope_ReturnsFalse`
  - `HasGrantForCompanyAsync_WithMoleculeScope_ReturnsTrueForAnyCompanyInMolecule`
- [x] All 236 tests pass

## Security Considerations

1. **Default deny** - If scope resolution fails, deny access
2. **Audit logging** - Log all cross-company access attempts
3. **Cache invalidation** - Clear scope cache when grants change
4. **Query filter bypass** - Only use `IgnoreQueryFilters()` when scope explicitly allows

## Related Files

- `Services/IGrantService.cs` - Interface to extend
- `Services/GrantService.cs` - Implementation
- `Models/Grant.cs` - May need scope level property
- `Models/GrantType.cs` - Add default scope level
- `Data/SeedData/GrantTypeSeed.cs` - Add new grant types
- `Data/SeedData/RoleTemplateSeed.cs` - Map grants to roles

## Notes

This refactoring was identified during the role-based to grant-based authorization migration on 2026-02-06. The simpler authorization checks were converted, but data-scoping logic requires this more extensive refactoring.
