# Role Permissions Audit Report

**Date:** 2025-11-28
**Status:** ✅ VERIFIED - All permissions correctly configured

---

## Summary

This audit verifies that the **Assigner** role has been properly configured with limited permissions:
- ✅ **CAN** edit chores (via `/Public/Chores` and calendar API)
- ✅ **CANNOT** edit shifts (shift table requires Manager+)
- ✅ **CANNOT** edit day shifts/on-duty (requires Manager+)
- ✅ Manager/Director/Owner maintain full access to all features

---

## Authorization Policies (Program.cs)

### Core Role Policies

| Policy | Allowed Roles | Purpose |
|--------|--------------|---------|
| `IsAdmin` | Owner | Owner-only features |
| `IsDirector` | Owner, Director | Director-level features |
| `IsManagerOrAdmin` | Manager, Owner, Director | Management features (excludes Assigner) |

### View Policies (Read-Only Access)

| Policy | Allowed Roles | Purpose |
|--------|--------------|---------|
| `CanViewChores` | All authenticated users | View chores page |
| `CanViewOnDuty` | All authenticated users | View day shifts page |

### Edit Policies (Write Access)

| Policy | Allowed Roles | Assigner Included? |
|--------|--------------|-------------------|
| `CanEditChores` | Manager, Owner, Director, **Assigner** | ✅ YES |
| `CanEditOnDuty` | Manager, Owner, Director | ❌ NO |

---

## Page-Level Authorization

### Shift Management
**File:** `Pages/Calendar/Table.cshtml.cs`
**Policy:** `[Authorize(Policy = "IsManagerOrAdmin")]`
**Result:** ❌ Assigner **CANNOT** access shift table

### Chores Management
**File:** `Pages/Public/Chores.cshtml.cs`
**Policy:** `[Authorize(Policy = "CanViewChores")]`
**Edit Check:** Calls `ChoreService.CanUserManageChoresAsync()`
**Result:** ✅ Assigner **CAN** edit chores

### Day Shifts (On-Duty) Management
**File:** `Pages/Public/OnDuty.cshtml.cs`
**Policy:** `[Authorize(Policy = "CanViewOnDuty")]`
**Edit Check:** Calls `OnDutyService.CanUserManageOnDutyAsync()`
**Result:** ❌ Assigner **CANNOT** edit day shifts

### Admin Pages
**Files:** `Pages/Admin/*.cshtml.cs`
**Policy:** `[Authorize(Policy = "IsManagerOrAdmin")]`
**Result:** ❌ Assigner **CANNOT** access admin features

---

## API Endpoint Authorization

### Calendar Quick-Add/Delete APIs

| Endpoint | File | Policy | Assigner Access |
|----------|------|--------|----------------|
| `/Api/Calendar/QuickAddChore` | `QuickAddChore.cshtml.cs` | `CanEditChores` | ✅ YES |
| `/Api/Calendar/DeleteChore` | `DeleteChore.cshtml.cs` | `CanEditChores` | ✅ YES |
| `/Api/Calendar/QuickAddOnDuty` | `QuickAddOnDuty.cshtml.cs` | `CanEditOnDuty` | ❌ NO |
| `/Api/Calendar/DeleteOnDuty` | `DeleteOnDuty.cshtml.cs` | `CanEditOnDuty` | ❌ NO |

**Result:** Assigner can create/delete chores via calendar but NOT day shifts ✅

---

## Service-Level Permission Checks

### ChoreService.CanUserManageChoresAsync()
**File:** `Services/ChoreService.cs` (lines 61-70)

```csharp
public async Task<bool> CanUserManageChoresAsync(int userId)
{
    var user = await _db.Users.FindAsync(userId);
    if (user == null) return false;

    return user.Role == UserRole.Owner ||
           user.Role == UserRole.Director ||
           user.Role == UserRole.Manager ||
           user.Role == UserRole.Assigner;  // ✅ ASSIGNER INCLUDED
}
```

**Result:** ✅ Assigner **CAN** manage chores

---

### OnDutyService.CanUserManageOnDutyAsync()
**File:** `Services/OnDutyService.cs` (lines 79-89)

```csharp
public async Task<bool> CanUserManageOnDutyAsync(int userId)
{
    var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
    if (user == null) return false;

    // Only Manager, Director, and Owner can manage OnDuty
    // Assigner role is explicitly excluded
    return user.Role == UserRole.Owner ||
           user.Role == UserRole.Director ||
           user.Role == UserRole.Manager;  // ❌ ASSIGNER EXCLUDED
}
```

**Result:** ❌ Assigner **CANNOT** manage day shifts

---

## Role Capabilities Matrix

| Feature | Employee | Assigner | Manager | Director | Owner |
|---------|----------|----------|---------|----------|-------|
| **View Calendar** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **View Chores** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **View Day Shifts** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Edit Chores** | ❌ | ✅ | ✅ | ✅ | ✅ |
| **Edit Shifts** | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Edit Day Shifts** | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Manage Users** | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Analytics** | ❌ | ❌ | ✅ | ✅ | ✅ |
| **System Config** | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Manage Companies** | ❌ | ❌ | ❌ | ❌ | ✅ |

---

## Security Layers

The system implements **defense in depth** with multiple authorization layers:

1. **Policy-Level Authorization** (ASP.NET Core Policies)
   - Applied via `[Authorize(Policy = "...")]` attributes
   - Enforced before page/API handler execution
   - Returns 403 Forbidden if user lacks required role

2. **Service-Level Checks** (Business Logic)
   - `CanUserManageChoresAsync()`, `CanUserManageOnDutyAsync()`
   - Called within page handlers and API endpoints
   - Provides fine-grained permission validation

3. **UI-Level Rendering** (Conditional Display)
   - Edit buttons/forms hidden based on user role
   - Improves UX by not showing unavailable actions
   - **NOT** a security boundary (backed by layers 1 & 2)

---

## Testing Recommendations

To verify these permissions in production/staging:

### Test Case 1: Assigner CAN Edit Chores
1. Log in as user with Assigner role
2. Navigate to `/Public/Chores`
3. Verify "Add Chore" button is visible
4. Create a chore → Should succeed ✅
5. Delete a chore → Should succeed ✅
6. Use calendar quick-add for chore → Should succeed ✅

### Test Case 2: Assigner CANNOT Edit Shifts
1. Log in as user with Assigner role
2. Navigate to `/Calendar/Table`
3. Should receive **403 Forbidden** or redirect ❌
4. Attempt to access shift management → Should fail ❌

### Test Case 3: Assigner CANNOT Edit Day Shifts
1. Log in as user with Assigner role
2. Navigate to `/Public/OnDuty`
3. Verify "Add On-Duty" button is **NOT** visible
4. Attempt API call to `/Api/Calendar/QuickAddOnDuty`
5. Should receive **403 Forbidden** ❌

### Test Case 4: Manager Has Full Access
1. Log in as user with Manager role
2. Verify access to:
   - Shifts (`/Calendar/Table`) ✅
   - Chores (`/Public/Chores`) ✅
   - Day Shifts (`/Public/OnDuty`) ✅
   - Admin features (`/Admin/*`) ✅

---

## Conclusion

✅ **All role permissions are correctly configured**

The Assigner role has been properly implemented with:
- **Chore management** capabilities (as intended)
- **No access** to shift or day shift management (as intended)
- Proper authorization at policy, service, and API levels
- Defense-in-depth security architecture

No changes or fixes are required for role permissions.

---

## Related Documentation

- `TERMINOLOGY.md` - UI terminology vs. backend naming
- `Program.cs` (lines 92-110) - Authorization policy definitions
- `Services/ChoreService.cs` - Chore permission logic
- `Services/OnDutyService.cs` - Day shift permission logic
