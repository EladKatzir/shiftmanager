# FINDING-008: Missing Audit Log on User Disable/Enable Toggle

| Field | Value |
|-------|-------|
| **ID** | FINDING-008 |
| **Date** | 2026-02-18 |
| **Category** | Audit / Compliance |
| **Severity** | MEDIUM |

## Expected Behavior

When an admin toggles a user's active/disabled status via `OnPostToggleAsync()`, an audit log entry should be created recording who disabled/enabled whom and when.

## Actual Behavior

`Admin/Users.cshtml.cs` `OnPostToggleAsync()` (lines 730-771):
- Toggles `user.IsDeleted` flag (soft delete)
- Calls `SaveChangesAsync()`
- Does NOT create an audit log entry

## Evidence

**Admin/Users.cshtml.cs** `OnPostToggleAsync()`:
```csharp
public async Task<IActionResult> OnPostToggleAsync(int id)
{
    var user = await _context.Users.FindAsync(id);
    // ... null check ...
    user.IsDeleted = !user.IsDeleted;
    await _context.SaveChangesAsync();
    // No audit log entry
    return RedirectToPage();
}
```

**Contrast — OnPostRoleAsync()** and **OnPostAddAsync()**:
- Both create `AuditLog` entries for role changes and user creation
- The audit table tracks who did what and when

## Root Cause

The toggle handler was implemented as a simple flip operation and the audit logging step was overlooked.

## Fix Recommendation

```csharp
await _auditLogService.LogAsync(new AuditLog
{
    Action = user.IsDeleted ? "UserDisabled" : "UserEnabled",
    PerformedBy = int.Parse(User.FindFirst("UserId")!.Value),
    TargetUserId = user.Id,
    Details = $"User {user.Email} {(user.IsDeleted ? "disabled" : "enabled")}",
    CompanyId = user.CompanyId
});
```

## Verification Plan

1. Disable a user via admin panel
2. Check audit log table for corresponding entry (after fix)
3. Enable the user again and verify second audit entry

## Confidence

**95%** — Code review confirms no audit logging in the toggle handler.
