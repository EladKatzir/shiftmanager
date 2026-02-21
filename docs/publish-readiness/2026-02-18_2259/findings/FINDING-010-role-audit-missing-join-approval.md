# FINDING-010: RoleAssignmentAudit Missing on Join Request Approval

| Field | Value |
|-------|-------|
| **ID** | FINDING-010 |
| **Date** | 2026-02-18 |
| **Category** | Audit / Compliance |
| **Severity** | MEDIUM |

## Expected Behavior

When a user join request is approved and the user is created with their requested role, a `RoleAssignmentAudit` entry should be created to record the initial role assignment.

## Actual Behavior

The join request approval flow creates the user account and assigns the role but does not create a `RoleAssignmentAudit` entry for the initial role assignment. The first audit entry only appears if the role is later changed.

## Evidence

**Join request approval path** — user creation flow:
- Creates `AppUser` with the requested role
- Assigns grants from RoleTemplate
- Does NOT create `RoleAssignmentAudit` entry

**Contrast — Admin/Users.cshtml.cs** `OnPostRoleAsync()`:
- Creates `RoleAssignmentAudit` entry when changing an existing user's role
- Records old role, new role, changed by, timestamp

## Root Cause

The `RoleAssignmentAudit` was designed for role *changes*, not initial role *assignments*. The join request approval creates the user directly without going through the role change handler.

## Fix Recommendation

After creating the user from a join request, create an initial audit entry:

```csharp
_context.RoleAssignmentAudits.Add(new RoleAssignmentAudit
{
    UserId = newUser.Id,
    OldRole = null, // or "None"
    NewRole = newUser.Role.ToString(),
    ChangedBy = approvingManagerId,
    ChangedAt = DateTime.UtcNow,
    CompanyId = newUser.CompanyId
});
```

## Verification Plan

1. Submit a join request as a new user
2. Approve the join request as a manager
3. Check `RoleAssignmentAudits` table for initial assignment entry (after fix)

## Confidence

**85%** — Code review identifies the gap. The exact join request approval code path needs confirmation of which handler processes it.
