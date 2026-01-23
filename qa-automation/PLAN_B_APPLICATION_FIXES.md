# Plan B: Application Fixes (APPLICATION ISSUES)

**Iteration:** 1
**Date:** 2026-01-20
**Status:** ACTIONABLE - REQUIRES BACKEND TEAM

---

## Overview

This plan addresses actual bugs in the application code that prevent it from meeting security requirements, data integrity constraints, and business logic expectations.

---

## Issue #1: XSS Vulnerability - Unsanitized Script Tags in API Responses (CRITICAL)

### Affected Test
- `multi-tenancy-isolation-network.spec.js:84` - P6-03: XSS attempts are sanitized in responses

### Severity
**🔴 CRITICAL - Security Vulnerability**

### Problem Statement
The application is returning unsanitized script tags and HTML in API/page responses, creating an XSS (Cross-Site Scripting) vulnerability.

### Evidence
**Test sends:**
```javascript
const xssPayload = '<script>alert("xss")</script>';
```

**Expected response (HTML-encoded):**
```
&lt;script&gt;alert("xss")&lt;/script&gt;
OR
&amp;lt;script&amp;gt;alert("xss")&amp;gt;
```

**Actual response:**
```
<script>alert("xss")</script>
```

**Additional evidence from page snapshot:**
The Companies page shows many company names with raw XSS payloads:
- `<script>alert("xss")</script>` - displayed multiple times
- `<img src=x onerror=alert("xss")>` - displayed multiple times
- `'; DROP TABLE Companies;--` - SQL injection attempts (also visible but separate issue)

These values are being stored in the database AND rendered without encoding.

### Impact
- **Security:** Attackers can inject malicious JavaScript that executes in other users' browsers
- **Data theft:** Session tokens, cookies, and sensitive data can be stolen
- **Account hijacking:** Attackers can perform actions as the victim user
- **Compliance:** Violates OWASP Top 10 #3 (Injection) and security best practices

### Root Cause Hypothesis
1. User input is not sanitized on entry (dangerous but not immediate XSS)
2. **Critical:** Output is not HTML-encoded when rendering to browser
3. API responses may be returning JSON without proper encoding
4. Razor views may not using proper encoding (`@Html.Raw()` instead of `@Model.Property`)

### Recommended Fix

#### Priority 1: Output Encoding (IMMEDIATE)
All user-generated content MUST be HTML-encoded when rendered:

**In Razor views (.cshtml):**
```csharp
// WRONG - renders raw HTML
@Html.Raw(Model.CompanyName)

// CORRECT - encodes HTML entities
@Model.CompanyName
```

**In API controllers:**
```csharp
// Ensure JSON serialization encodes properly
// Or use SecurityElement.Escape() for HTML output
using System.Security;

public string SanitizedName => SecurityElement.Escape(CompanyName);
```

#### Priority 2: Input Validation
Add server-side validation to reject or sanitize dangerous input:

```csharp
using System.Web;
using HtmlAgilityPack; // or AntiXss library

public class Company
{
    private string _name;
    public string CompanyName
    {
        get => _name;
        set => _name = HtmlSanitizer.Sanitize(value);
    }
}
```

#### Priority 3: Content Security Policy
Add CSP headers to prevent inline script execution:

```csharp
// In Startup.cs or Program.cs
app.Use(async (context, next) =>
{
    context.Response.Headers.Add(
        "Content-Security-Policy",
        "default-src 'self'; script-src 'self'"
    );
    await next();
});
```

### Files Likely Affected
Search for these patterns in codebase:

```bash
# Find Razor views with Raw HTML
rg "@Html.Raw" Views/ --type cshtml

# Find company name rendering
rg "CompanyName" Views/ --type cshtml

# Find API endpoints returning company data
rg "Company" Controllers/ --type cs | grep -i "return"
```

**Probable locations:**
- `Views/Admin/Companies.cshtml` (renders company list)
- `Views/Admin/Users.cshtml` (renders company filter)
- `Controllers/Admin/CompaniesController.cs` (company CRUD endpoints)
- Any view/controller that displays user-generated content

### Repro Steps
1. Navigate to `/Admin/Companies`
2. Create a company with name: `<script>alert('XSS')</script>`
3. Observe:
   - Company is created successfully
   - Name is displayed in table with script tag visible (not encoded)
4. Expected: Name should display as literal text `&lt;script&gt;alert('XSS')&lt;/script&gt;`

### Owner/Team Assignment
- **Primary:** Backend/Security team
- **Secondary:** Frontend team (for CSP implementation)
- **Reviewer:** Security architect/CISO

### Acceptance Criteria
1. ✅ All user-generated content is HTML-encoded on output
2. ✅ Test `P6-03` passes with XSS payloads properly encoded
3. ✅ Manual test: creating company with `<script>` doesn't execute JavaScript
4. ✅ Code review confirms no `@Html.Raw()` usage on user content
5. ✅ CSP headers are configured (optional but recommended)

---

## Issue #2: Data Integrity - Deleted Blueprint Allows Program Creation (HIGH)

### Affected Test
- `shift-assignment-workflow.spec.js:313` - Data integrity: Deleting blueprint prevents new program creation

### Severity
**🟠 HIGH - Data Integrity Bug**

### Problem Statement
The application allows creating programs that reference deleted blueprints, violating referential integrity and causing orphaned/invalid data.

### Evidence
**Test flow:**
1. Create a blueprint
2. Delete the blueprint
3. Attempt to create a program using the deleted blueprint
4. **Expected:** Error message "Blueprint not found" or "Failed to create program"
5. **Actual:** Program creation succeeds, page shows "Training Assignment Tracker"

### Impact
- **Data integrity:** Programs reference non-existent blueprints
- **Application errors:** Attempting to load/execute programs with missing blueprints will fail
- **User confusion:** Programs appear valid but can't function properly
- **Data corruption:** Orphaned programs pollute the database

### Root Cause Hypothesis
1. Program creation endpoint doesn't validate blueprint exists
2. Soft-delete on blueprints but no check for deleted status
3. Foreign key constraint missing or not enforced
4. UI doesn't update after blueprint deletion (stale options in dropdown)

### Recommended Fix

#### Option 1: Database Foreign Key Constraint (RECOMMENDED)
Add cascading foreign key constraint to prevent orphaned programs:

```sql
ALTER TABLE Programs
ADD CONSTRAINT FK_Programs_Blueprints
FOREIGN KEY (BlueprintId)
REFERENCES Blueprints(Id)
ON DELETE RESTRICT;  -- Prevent deletion if programs exist
-- OR
ON DELETE CASCADE;   -- Delete programs when blueprint deleted
```

#### Option 2: Application-Level Validation
Add validation in program creation endpoint:

```csharp
// In ProgramsController.cs or similar
public async Task<IActionResult> CreateProgram(ProgramCreateDto dto)
{
    // Validate blueprint exists
    var blueprint = await _context.Blueprints
        .FirstOrDefaultAsync(b => b.Id == dto.BlueprintId);

    if (blueprint == null)
    {
        return BadRequest(new { error = "Blueprint not found or has been deleted" });
    }

    // Continue with program creation
    var program = new Program
    {
        BlueprintId = dto.BlueprintId,
        Name = dto.Name
    };

    await _context.Programs.AddAsync(program);
    await _context.SaveChangesAsync();

    return Ok(program);
}
```

#### Option 3: Soft Delete Aware Queries
If using soft deletes, ensure queries filter out deleted blueprints:

```csharp
// Global query filter in DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Blueprint>()
        .HasQueryFilter(b => !b.IsDeleted);
}

// Or in specific query
var activeBlueprints = await _context.Blueprints
    .Where(b => !b.IsDeleted)
    .ToListAsync();
```

### Files Likely Affected
Search for these patterns:

```bash
# Find program creation code
rg "CreateProgram|AddProgram" --type cs

# Find blueprint reference
rg "BlueprintId" --type cs

# Find database context/models
rg "class Program" --type cs
rg "class Blueprint" --type cs
```

**Probable locations:**
- `Controllers/ProgramsController.cs` (or similar)
- `Models/Program.cs`
- `Models/Blueprint.cs`
- `Data/ApplicationDbContext.cs` (for EF Core configuration)
- `Migrations/*.cs` (may need new migration for FK constraint)

### Repro Steps
1. Navigate to Blueprint management
2. Create a blueprint named "Test Blueprint"
3. Navigate to Programs
4. Note: Blueprint appears in dropdown
5. Go back and delete "Test Blueprint"
6. Return to Programs
7. Attempt to create program with the deleted blueprint
8. **Expected:** Error message or blueprint not in dropdown
9. **Actual:** Program creation succeeds

### Owner/Team Assignment
- **Primary:** Backend/Core team
- **DBA:** For foreign key constraint implementation
- **QA:** For regression testing

### Acceptance Criteria
1. ✅ Foreign key constraint exists in database (or application validation)
2. ✅ Cannot create program with non-existent blueprint
3. ✅ Error message displayed: "Blueprint not found" or similar
4. ✅ Test `shift-assignment-workflow.spec.js:313` passes
5. ✅ UI dropdown only shows active/existing blueprints
6. ✅ Regression test: deleting blueprint with existing programs either:
   - Prevents deletion (with helpful message), OR
   - Cascades to delete programs (with warning to user)

---

## Summary Dashboard

| Priority | Issue | Severity | Effort | Affected Tests | Status |
|----------|-------|----------|--------|----------------|--------|
| **CRITICAL** | XSS Vulnerability | 🔴 Security | Medium (4-8 hours) | 1 test + security risk | NOT STARTED |
| **HIGH** | Blueprint Integrity | 🟠 Data Integrity | Medium (2-4 hours) | 1 test + data corruption | NOT STARTED |

**Total application bugs:** 2 confirmed
**Security issues:** 1 critical
**Data integrity issues:** 1 high

---

## Immediate Actions Required

### For Development Team

1. **TODAY:** Address XSS vulnerability
   - Quick fix: Find all views rendering CompanyName and ensure HTML encoding
   - Search and replace `@Html.Raw(Model.CompanyName)` → `@Model.CompanyName`
   - Test in staging environment

2. **THIS WEEK:** Fix blueprint referential integrity
   - Add foreign key constraint OR application validation
   - Test cascading behavior
   - Update UI to hide deleted blueprints from dropdowns

3. **VERIFICATION:**
   - Re-run affected Playwright tests
   - Perform manual security testing
   - Code review focusing on output encoding

### For Security Team

1. Conduct security audit of all user-generated content rendering
2. Review other potential injection points (SQL injection tests also showing in data)
3. Implement Content Security Policy headers
4. Add input validation/sanitization layer

### For QA Team

1. Expand XSS test coverage to other user input fields
2. Test all CRUD operations for data integrity violations
3. Create regression test suite for security issues

---

## Next Steps After Fixes

1. Backlog Sprint: Address SQL injection visible in test data (`'; DROP TABLE Companies;--`)
2. Security hardening: Implement input validation across all forms
3. Database audit: Check for other missing foreign key constraints
4. Code review: Establish secure coding standards for output encoding

---

## Risk Assessment

### If XSS Not Fixed
- **Probability:** 100% exploitable
- **Impact:** Account takeover, data theft, malware distribution
- **Risk Level:** CRITICAL - DO NOT DEPLOY TO PRODUCTION

### If Blueprint Integrity Not Fixed
- **Probability:** Medium (depends on user workflow)
- **Impact:** Data corruption, application errors, user frustration
- **Risk Level:** HIGH - Fix before next release

---

## Contact & Escalation

- **Security Issues:** Escalate to security@company.com
- **Blocker:** Notify product owner if fix timeline exceeds 1 week
- **Questions:** Slack #backend-team or #security-team
