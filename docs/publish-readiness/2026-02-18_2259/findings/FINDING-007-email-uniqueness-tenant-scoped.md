# FINDING-007: Email Uniqueness Check in Admin User Creation is Tenant-Scoped

| Field | Value |
|-------|-------|
| **ID** | FINDING-007 |
| **Date** | 2026-02-18 |
| **Category** | Data Integrity / Multi-Tenancy |
| **Severity** | MEDIUM |

## Expected Behavior

When creating a user via `Admin/Users.cshtml.cs` `OnPostAddAsync()`, the email uniqueness check should verify across ALL tenants (companies) to prevent duplicate email addresses system-wide, since email is used as the login identifier.

## Actual Behavior

The email check on line ~507 uses a standard query that is subject to EF Core's tenant query filter (`WHERE CompanyId = @currentTenantId`). This means it only checks for email uniqueness within the current company.

## Evidence

**Admin/Users.cshtml.cs** `OnPostAddAsync()` (line ~507):
```csharp
var existingUser = await _context.Users
    .FirstOrDefaultAsync(u => u.Email == Input.Email);
```

This query is filtered by the global query filter: `.HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId())`.

**Contrast — Signup.cshtml.cs** (line 222):
```csharp
var existingUser = await _context.Users
    .IgnoreQueryFilters()  // Global email check
    .FirstOrDefaultAsync(u => u.Email == model.Email);
```

The public signup page correctly uses `IgnoreQueryFilters()` for global uniqueness.

**Database constraint:** `AppDbContext` has a unique index on `AppUser.Email`, which would catch this at the DB level with a raw exception. But the user-facing error message would be a generic 500 rather than a friendly "email already in use" message.

## Root Cause

The admin user creation was built within the tenant-scoped context and the developer did not add `IgnoreQueryFilters()`. The signup page was built later with this awareness.

## Fix Recommendation

```csharp
var existingUser = await _context.Users
    .IgnoreQueryFilters()  // SECURITY-AUDITED: Global email uniqueness check for login identifier
    .FirstOrDefaultAsync(u => u.Email == Input.Email);
```

## Verification Plan

1. Create user `test@example.com` in Company A
2. Attempt to create user `test@example.com` in Company B via admin page
3. Before fix: succeeds at query level but fails at DB constraint (500 error)
4. After fix: returns friendly error message

## Confidence

**95%** — Code review clearly shows the gap. DB unique constraint provides a safety net but with poor UX.
