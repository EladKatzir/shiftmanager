# ShiftManager Fixes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix 12+ issues spanning data bugs, UI/UX improvements, permission/access fixes, and localization gaps in ShiftManager.

**Architecture:** ASP.NET Core 8.0 Razor Pages with EF Core 9.0.9, SQLite database, multi-tenant via CompanyId query filters, RBAC (Owner > Director > Manager > Assigner > Employee > Trainee).

**Tech Stack:** C# 12, Razor Pages, Entity Framework Core, IStringLocalizer for localization, SharedResources.resx files.

---

## Task 1: Fix Companies User Count Bug

**Problem:** Admin/Companies page shows user counts as 0 because EF query filter interferes with subquery.

**Files:**
- Modify: `Pages/Admin/Companies.cshtml.cs:57-66`

**Step 1: Read the current implementation**

The bug is at line 64:
```csharp
_db.Users.Count(u => u.CompanyId == c.Id)  // BUG: Query filter interferes
```

**Step 2: Fix by using IgnoreQueryFilters**

```csharp
// In Companies.cshtml.cs, update the OnGetAsync method
public async Task OnGetAsync()
{
    // Get success message from TempData if available
    if (TempData["SuccessMessage"] is string successMsg)
    {
        Success = successMsg;
    }

    // First, get user counts per company using IgnoreQueryFilters
    var userCountsByCompany = await _db.Users
        .IgnoreQueryFilters()
        .GroupBy(u => u.CompanyId)
        .Select(g => new { CompanyId = g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.CompanyId, x => x.Count);

    Companies = await _db.Companies
        .IgnoreQueryFilters()
        .OrderBy(c => c.Name)
        .Select(c => new CompanyVM(
            c.Id,
            c.Name,
            c.Slug,
            c.DisplayName,
            userCountsByCompany.GetValueOrDefault(c.Id, 0)
        ))
        .ToListAsync();

    // Load all Director users
    AvailableDirectors = await _db.Users
        .IgnoreQueryFilters()
        .Where(u => u.Role == UserRole.Director)
        .OrderBy(u => u.DisplayName)
        .Select(u => new DirectorVM(u.Id, u.DisplayName, u.Email))
        .ToListAsync();
}
```

**Step 3: Run the build to verify**

Run: `dotnet build`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add Pages/Admin/Companies.cshtml.cs
git commit -m "fix: Companies page user counts now use IgnoreQueryFilters

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 2: Roster 7-Day Cube Status Display

**Problem:** Replace text status in roster dock with 7 visual cubes showing availability for next 7 days.

**Files:**
- Modify: `wwwroot/css/site.css` (add cube styles)
- Modify: `wwwroot/js/roster-dock.js` (update employee rendering)
- Modify: `Pages/Calendar/Table.cshtml.cs` (add endpoint for 7-day availability)

**Step 1: Add CSS for availability cubes**

Add to `wwwroot/css/site.css`:
```css
/* 7-Day Availability Cubes */
.availability-cubes {
    display: flex;
    gap: 2px;
    margin-top: 4px;
}

.availability-cube {
    width: 20px;
    height: 20px;
    border-radius: 3px;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    font-size: 0.5rem;
    cursor: pointer;
    position: relative;
}

.availability-cube .cube-date {
    font-size: 0.5rem;
    line-height: 1;
    color: inherit;
    opacity: 0.8;
}

.availability-cube.free {
    background-color: #22c55e;
    color: white;
}

.availability-cube.busy {
    background-color: #ef4444;
    color: white;
}

.availability-cube.partial {
    background-color: #f59e0b;
    color: white;
}

/* Hover tooltip for cubes */
.availability-cube .cube-tooltip {
    display: none;
    position: absolute;
    bottom: 100%;
    left: 50%;
    transform: translateX(-50%);
    background: rgba(0, 0, 0, 0.85);
    color: white;
    padding: 6px 10px;
    border-radius: 4px;
    font-size: 0.75rem;
    white-space: nowrap;
    z-index: 1000;
    margin-bottom: 4px;
}

.availability-cube:hover .cube-tooltip {
    display: block;
}
```

**Step 2: Add API endpoint for 7-day availability**

Add to `Pages/Calendar/Table.cshtml.cs`:
```csharp
public async Task<IActionResult> OnGetEmployeeAvailabilityAsync()
{
    var startDate = DateOnly.FromDateTime(DateTime.Today);
    var endDate = startDate.AddDays(6);

    var employees = await _db.Users
        .Where(u => u.IsActive && (u.Role == UserRole.Employee || u.Role == UserRole.Trainee))
        .OrderBy(u => u.DisplayName)
        .Select(u => new { u.Id, u.DisplayName })
        .ToListAsync();

    var result = new List<object>();

    foreach (var emp in employees)
    {
        var availability = new List<object>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dateTime = date.ToDateTime(TimeOnly.MinValue);

            // Check for shifts
            var hasShift = await _db.ShiftAssignments
                .AnyAsync(sa => sa.UserId == emp.Id &&
                               sa.ShiftInstance!.Date == date);

            // Check for time-off
            var hasTimeOff = await _db.TimeOffRequests
                .AnyAsync(tor => tor.UserId == emp.Id &&
                                 tor.StartDate <= dateTime &&
                                 tor.EndDate >= dateTime &&
                                 tor.Status == TimeOffStatus.Approved);

            var status = "free";
            var tooltip = "Available";

            if (hasTimeOff)
            {
                status = "busy";
                tooltip = "Time Off";
            }
            else if (hasShift)
            {
                status = "partial";
                tooltip = "Has Shift";
            }

            availability.Add(new {
                date = date.ToString("d/M"),
                dayName = date.DayOfWeek.ToString().Substring(0, 3),
                status,
                tooltip
            });
        }

        result.Add(new {
            id = emp.Id,
            name = emp.DisplayName,
            availability
        });
    }

    return new JsonResult(result);
}
```

**Step 3: Update roster-dock.js to render cubes**

Modify `wwwroot/js/roster-dock.js` - update the employee rendering function:
```javascript
function renderEmployeeItem(employee) {
    const initials = employee.name.split(' ').map(n => n[0]).join('').substring(0, 2);

    let cubesHtml = '<div class="availability-cubes">';
    if (employee.availability) {
        employee.availability.forEach(day => {
            cubesHtml += `
                <div class="availability-cube ${day.status}" title="${day.tooltip}">
                    <span class="cube-date">${day.date}</span>
                    <span class="cube-tooltip">${day.dayName}: ${day.tooltip}</span>
                </div>
            `;
        });
    }
    cubesHtml += '</div>';

    return `
        <div class="roster-employee-item"
             draggable="true"
             data-employee-id="${employee.id}"
             data-employee-name="${employee.name}">
            <div class="roster-employee-avatar">${initials}</div>
            <div class="roster-employee-info">
                <div class="roster-employee-name">${employee.name}</div>
                ${cubesHtml}
            </div>
        </div>
    `;
}

// Update loadEmployees to use new endpoint
async function loadEmployees() {
    try {
        const response = await fetch('/Calendar/Table?handler=EmployeeAvailability');
        const employees = await response.json();

        const container = document.getElementById('rosterEmployeeList');
        container.innerHTML = employees.map(renderEmployeeItem).join('');

        // Re-attach drag handlers
        initDragHandlers();
    } catch (error) {
        console.error('Failed to load employees:', error);
    }
}
```

**Step 4: Run build and test**

Run: `dotnet build`
Expected: Build succeeds

**Step 5: Commit**

```bash
git add wwwroot/css/site.css wwwroot/js/roster-dock.js Pages/Calendar/Table.cshtml.cs
git commit -m "feat: Add 7-day availability cubes to roster dock

- 20x20px colored cubes (green=free, red=busy, amber=partial)
- Shows date above each cube
- Hover tooltip with details

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 3: Admin/Index Design Parity with Owner/Index

**Problem:** Admin/Index needs same card styling as Owner/Index.

**Files:**
- Modify: `Pages/Admin/Index.cshtml` (add missing styles)

**Step 1: Add the missing styles to Admin/Index.cshtml**

The Admin/Index.cshtml uses class `.owner-panel-container` but doesn't define the styles. Add at the bottom of Admin/Index.cshtml before the closing `</div>`:

```html
<style>
.owner-panel-container {
    max-width: 1400px;
    margin: 0 auto;
    padding: 2rem;
}

.page-header {
    margin-bottom: 2rem;
}

.page-header h1 {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 0 0 0.5rem 0;
    font-size: 2rem;
}

.page-icon {
    font-size: 2.5rem;
}

.page-subtitle {
    color: var(--muted);
    margin: 0;
}

.section-title {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 2rem 0 1rem 0;
    font-size: 1.25rem;
    color: var(--text);
}

.section-icon {
    font-size: 1.5rem;
}

.stats-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
    gap: 1.5rem;
    margin-bottom: 2rem;
}

.stat-card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 1.5rem;
    display: flex;
    align-items: center;
    gap: 1rem;
}

.stat-icon {
    font-size: 2.5rem;
}

.stat-value {
    font-size: 2rem;
    font-weight: bold;
    color: var(--primary);
}

.stat-label {
    color: var(--muted);
    font-size: 0.875rem;
}

.admin-tools-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
    gap: 1.5rem;
    margin-bottom: 2rem;
}

.admin-tool-card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 1.5rem;
    display: flex;
    align-items: center;
    gap: 1rem;
    text-decoration: none;
    color: inherit;
    transition: all 0.2s;
}

.admin-tool-card:hover {
    border-color: var(--primary);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
    transform: translateY(-2px);
}

.tool-icon {
    font-size: 2.5rem;
    flex-shrink: 0;
}

.tool-content {
    flex: 1;
}

.tool-content h3 {
    margin: 0 0 0.25rem 0;
    font-size: 1.125rem;
}

.tool-content p {
    margin: 0;
    color: var(--muted);
    font-size: 0.875rem;
}

.tool-arrow {
    font-size: 1.5rem;
    color: var(--muted);
    flex-shrink: 0;
}
</style>
```

**Step 2: Run build**

Run: `dotnet build`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add Pages/Admin/Index.cshtml
git commit -m "fix: Add missing CSS styles to Admin/Index for design parity

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 4: Fix metric-item Text Cutoff

**Problem:** metric-item containers cut off text content.

**Files:**
- Modify: `wwwroot/css/site.css`

**Step 1: Find and fix metric-item styles**

Search for `.metric-item` in site.css and update:

```css
.metric-item {
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 1rem;
    min-width: 120px;
    /* Fix text cutoff */
    overflow: visible;
    text-overflow: ellipsis;
}

.metric-item .metric-value {
    font-size: 1.5rem;
    font-weight: bold;
    white-space: nowrap;
}

.metric-item .metric-label {
    font-size: 0.875rem;
    color: var(--muted);
    text-align: center;
    word-wrap: break-word;
    max-width: 100%;
}
```

**Step 2: Commit**

```bash
git add wwwroot/css/site.css
git commit -m "fix: Prevent metric-item text cutoff with overflow visible

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 5: Owner Cross-Company User Visibility

**Problem:** Owner cannot see/manage users across all companies from Admin/Users.

**Files:**
- Modify: `Pages/Admin/Users.cshtml.cs` (line ~146-167)

**Step 1: Update OnGetAsync to show all companies for Owner**

The current logic at lines 146-167 is correct for filtering accessible companies. The issue is that the Users query needs to use `IgnoreQueryFilters()` for Owner role. Update the users query:

```csharp
// In Users.cshtml.cs, around line 180+ where users are loaded
if (currentUserRole == UserRole.Owner)
{
    // Owner sees ALL users across all companies
    usersQuery = _db.Users
        .IgnoreQueryFilters()
        .Include(u => u.Company)
        .AsQueryable();
}
else
{
    // Other roles see filtered by accessible companies
    usersQuery = _db.Users
        .Include(u => u.Company)
        .Where(u => accessibleCompanyIds.Contains(u.CompanyId))
        .AsQueryable();
}
```

**Step 2: Update Add User form to show company dropdown for Owner**

In `Pages/Admin/Users.cshtml`, add company selection dropdown in the Add User form:

```html
@if (Model.IsOwner)
{
    <div class="form-group">
        <label for="NewUserCompanyId">Company</label>
        <select asp-for="NewUserCompanyId" class="form-control">
            @foreach (var company in Model.Companies)
            {
                <option value="@company.Id">@company.Name</option>
            }
        </select>
    </div>
}
```

**Step 3: Update OnPostAddUserAsync to use selected company**

```csharp
// In Users.cshtml.cs OnPostAddUserAsync
int targetCompanyId;
if (currentUserRole == UserRole.Owner && NewUserCompanyId.HasValue)
{
    targetCompanyId = NewUserCompanyId.Value;
}
else
{
    targetCompanyId = _companyContext.GetCompanyIdOrThrow();
}
```

**Step 4: Add property for NewUserCompanyId**

```csharp
[BindProperty]
public int? NewUserCompanyId { get; set; }

public List<Company> Companies { get; set; } = new();
```

**Step 5: Load companies in OnGetAsync**

```csharp
if (currentUserRole == UserRole.Owner)
{
    Companies = await _db.Companies
        .IgnoreQueryFilters()
        .OrderBy(c => c.Name)
        .ToListAsync();
}
```

**Step 6: Build and verify**

Run: `dotnet build`
Expected: Build succeeds

**Step 7: Commit**

```bash
git add Pages/Admin/Users.cshtml Pages/Admin/Users.cshtml.cs
git commit -m "feat: Owner can now see/manage users across all companies

- Owner sees all users with IgnoreQueryFilters
- Add company dropdown in Add User form for Owner
- Company column shows in user list

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 6: Add Company Management to Owner/Index

**Problem:** Owner/Index needs entry point for company management.

**Files:**
- Modify: `Pages/Owner/Index.cshtml` (add Companies card)

**Step 1: Add Companies card to admin-tools-grid**

Insert after the FeatureFlags card (around line 68):

```html
<a href="/Admin/Companies" class="admin-tool-card">
    <div class="tool-icon">🏢</div>
    <div class="tool-content">
        <h3><loc key="Owner_Companies" /></h3>
        <p><loc key="Owner_Companies_Desc" /></p>
    </div>
    <div class="tool-arrow">→</div>
</a>

<a href="/Admin/Directors" class="admin-tool-card">
    <div class="tool-icon">👔</div>
    <div class="tool-content">
        <h3><loc key="Owner_Directors" /></h3>
        <p><loc key="Owner_Directors_Desc" /></p>
    </div>
    <div class="tool-arrow">→</div>
</a>
```

**Step 2: Add localization keys**

Add to `Resources/SharedResources.resx`:
```xml
<data name="Owner_Companies" xml:space="preserve">
    <value>Companies</value>
</data>
<data name="Owner_Companies_Desc" xml:space="preserve">
    <value>Manage tenants, create and edit companies</value>
</data>
<data name="Owner_Directors" xml:space="preserve">
    <value>Directors</value>
</data>
<data name="Owner_Directors_Desc" xml:space="preserve">
    <value>Assign directors to manage companies</value>
</data>
```

**Step 3: Add Hebrew translations**

Add to `Resources/SharedResources.he-IL.resx`:
```xml
<data name="Owner_Companies" xml:space="preserve">
    <value>חברות</value>
</data>
<data name="Owner_Companies_Desc" xml:space="preserve">
    <value>ניהול דיירים, יצירה ועריכת חברות</value>
</data>
<data name="Owner_Directors" xml:space="preserve">
    <value>מנהלים</value>
</data>
<data name="Owner_Directors_Desc" xml:space="preserve">
    <value>הקצאת מנהלים לניהול חברות</value>
</data>
```

**Step 4: Commit**

```bash
git add Pages/Owner/Index.cshtml Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat: Add Company and Director management cards to Owner/Index

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 7: Admin/Directors Improvements

**Problem:** Directors page needs ability to unlink/switch assignments and show all companies for Owner.

**Files:**
- Modify: `Pages/Admin/Directors.cshtml.cs`
- Modify: `Pages/Admin/Directors.cshtml`

**Step 1: Add reassign functionality to Directors.cshtml.cs**

Add new handler:
```csharp
public async Task<IActionResult> OnPostReassignAsync(int id, int newCompanyId)
{
    var assignment = await _db.DirectorCompanies
        .FirstOrDefaultAsync(dc => dc.Id == id && !dc.IsDeleted);

    if (assignment == null)
    {
        TempData["ErrorMessage"] = "Assignment not found.";
        return RedirectToPage();
    }

    // Check if new company exists
    var company = await _db.Companies.FindAsync(newCompanyId);
    if (company == null)
    {
        TempData["ErrorMessage"] = "Company not found.";
        return RedirectToPage();
    }

    // Check for duplicate
    var existingAssignment = await _db.DirectorCompanies
        .FirstOrDefaultAsync(dc => dc.UserId == assignment.UserId &&
                                   dc.CompanyId == newCompanyId &&
                                   !dc.IsDeleted);
    if (existingAssignment != null)
    {
        TempData["ErrorMessage"] = "Director is already assigned to that company.";
        return RedirectToPage();
    }

    // Soft delete old, create new
    assignment.IsDeleted = true;
    assignment.DeletedAt = DateTime.UtcNow;

    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    int.TryParse(userIdClaim, out var currentUserId);

    var newAssignment = new DirectorCompany
    {
        UserId = assignment.UserId,
        CompanyId = newCompanyId,
        GrantedBy = currentUserId,
        GrantedAt = DateTime.UtcNow
    };

    _db.DirectorCompanies.Add(newAssignment);
    await _db.SaveChangesAsync();

    _logger.LogInformation("Reassigned Director {UserId} from Company {OldCompanyId} to {NewCompanyId}",
        assignment.UserId, assignment.CompanyId, newCompanyId);

    TempData["SuccessMessage"] = $"Director reassigned to {company.Name}.";
    return RedirectToPage();
}
```

**Step 2: Update OnGetAsync to use IgnoreQueryFilters for Owner**

```csharp
// In OnGetAsync, load companies without query filter for Owner
AvailableCompanies = await _db.Companies
    .IgnoreQueryFilters()
    .OrderBy(c => c.Name)
    .ToListAsync();
```

**Step 3: Add reassign UI in Directors.cshtml**

Add a small dropdown next to each assignment row:
```html
<form method="post" asp-page-handler="Reassign" class="d-inline">
    <input type="hidden" name="id" value="@assignment.Id" />
    <select name="newCompanyId" class="form-select form-select-sm d-inline-block w-auto">
        @foreach (var company in Model.AvailableCompanies.Where(c => c.Id != assignment.CompanyId))
        {
            <option value="@company.Id">@company.Name</option>
        }
    </select>
    <button type="submit" class="btn btn-sm btn-outline-primary">Move</button>
</form>
```

**Step 4: Commit**

```bash
git add Pages/Admin/Directors.cshtml Pages/Admin/Directors.cshtml.cs
git commit -m "feat: Add reassign functionality to Directors page

- Dropdown to move director to different company
- Shows all companies for Owner role

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 8: EmailTemplates Button in EmailConfig

**Problem:** EmailConfig page needs a button to navigate to EmailTemplates.

**Files:**
- Modify: `Pages/Owner/EmailConfig.cshtml`

**Step 1: Add button to EmailConfig.cshtml**

Find the card footer section and add a button. Insert after the save button area:

```html
<div class="card mt-4">
    <div class="card-header">
        <h5 class="mb-0">📝 Email Templates</h5>
    </div>
    <div class="card-body">
        <p class="text-muted">Customize email templates for notifications, password resets, and other system emails.</p>
        <a href="/Owner/EmailTemplates" class="btn btn-primary">
            <span>📝</span> Manage Email Templates
        </a>
    </div>
</div>
```

**Step 2: Commit**

```bash
git add Pages/Owner/EmailConfig.cshtml
git commit -m "feat: Add Email Templates navigation button to EmailConfig

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 9: Localization Gaps (he-IL)

**Problem:** Multiple pages missing Hebrew translations.

**Files:**
- Modify: `Resources/SharedResources.he-IL.resx`

**Step 1: Identify missing keys by searching for English text**

Look for hardcoded English strings in:
- Admin/Index.cshtml
- Admin/Users.cshtml
- Calendar/Table.cshtml

**Step 2: Add Hebrew translations**

Add to `Resources/SharedResources.he-IL.resx`:
```xml
<!-- Admin Hub -->
<data name="AdminHub_Title" xml:space="preserve">
    <value>מרכז ניהול</value>
</data>
<data name="AdminHub_Subtitle" xml:space="preserve">
    <value>מרכז ניהול והגדרות</value>
</data>
<data name="CompanyScope" xml:space="preserve">
    <value>חברה והיקף</value>
</data>
<data name="PeopleManagement" xml:space="preserve">
    <value>ניהול אנשים</value>
</data>
<data name="SchedulingConfiguration" xml:space="preserve">
    <value>הגדרות שיבוץ</value>
</data>
<data name="OperationsManagement" xml:space="preserve">
    <value>ניהול תפעולי</value>
</data>
<data name="MonitoringAnalytics" xml:space="preserve">
    <value>ניטור וניתוח</value>
</data>
<data name="AdvancedTools" xml:space="preserve">
    <value>כלים מתקדמים</value>
</data>

<!-- Calendar/Table -->
<data name="AddCustomShiftRow" xml:space="preserve">
    <value>הוסף שורת משמרת מותאמת</value>
</data>
<data name="ShiftAssignmentTable" xml:space="preserve">
    <value>טבלת שיבוץ משמרות</value>
</data>
<data name="Week" xml:space="preserve">
    <value>שבוע</value>
</data>
<data name="TwoWeeks" xml:space="preserve">
    <value>שבועיים</value>
</data>
<data name="Month" xml:space="preserve">
    <value>חודש</value>
</data>

<!-- Users Page -->
<data name="Users_Title" xml:space="preserve">
    <value>משתמשים</value>
</data>
<data name="Users_AddNew" xml:space="preserve">
    <value>הוסף משתמש חדש</value>
</data>
<data name="Users_Edit" xml:space="preserve">
    <value>ערוך משתמש</value>
</data>
<data name="Users_Delete" xml:space="preserve">
    <value>מחק משתמש</value>
</data>
<data name="Users_Company" xml:space="preserve">
    <value>חברה</value>
</data>
```

**Step 3: Commit**

```bash
git add Resources/SharedResources.he-IL.resx
git commit -m "fix: Add missing Hebrew translations for Admin, Calendar, Users pages

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 10: Calendar/Table Empty on Air-Gapped

**Problem:** Shift table shows empty on air-gapped deployment.

**Files:**
- Investigate: `Pages/Calendar/Table.cshtml.cs`
- Investigate: `wwwroot/js/roster-dock.js`

**Step 1: Diagnose the issue**

The likely cause is JavaScript errors or missing data. Check:
1. Are ShiftTypes being loaded?
2. Is the AssignmentGrid populated?
3. Are there JavaScript CDN dependencies?

**Step 2: Add fallback for offline/air-gapped**

Check Table.cshtml.cs OnGetAsync to ensure data loads correctly:
```csharp
// Verify ShiftTypes are loaded with company context
ShiftTypes = await _db.ShiftTypes
    .OrderBy(st => st.Start)
    .ToListAsync();

_logger.LogInformation("Loaded {Count} shift types for company", ShiftTypes.Count);

if (!ShiftTypes.Any())
{
    _logger.LogWarning("No shift types found - creating defaults");
    // Create default shift types if none exist
}
```

**Step 3: Ensure no external dependencies**

Check `_Layout.cshtml` for CDN links that would fail offline. Replace with local files if needed.

**Step 4: Add diagnostic logging**

Add to Table.cshtml:
```html
@if (!Model.ShiftTypes.Any())
{
    <div class="alert alert-warning">
        <strong>No shift types configured.</strong>
        <a href="/Admin/ShiftTypes">Configure shift types</a> to start scheduling.
    </div>
}
```

**Step 5: Commit**

```bash
git add Pages/Calendar/Table.cshtml Pages/Calendar/Table.cshtml.cs
git commit -m "fix: Add fallback UI and logging for empty shift table

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 11: Air-Gapped Offline Validation

**Problem:** Need to verify all features work offline.

**Files:**
- Review: `wwwroot/**/*.js` for external dependencies
- Review: `Pages/Shared/_Layout.cshtml` for CDN links

**Step 1: Audit external dependencies**

Run: `grep -r "cdn\." wwwroot/ Pages/`
Run: `grep -r "googleapis\|cloudflare\|unpkg\|jsdelivr" wwwroot/ Pages/`

**Step 2: Replace any CDN links with local files**

If found, download and save locally:
```bash
# Example for a CDN link
curl -o wwwroot/lib/some-lib.min.js https://cdn.example.com/some-lib.min.js
```

**Step 3: Update script references**

Change:
```html
<script src="https://cdn.example.com/lib.js"></script>
```
To:
```html
<script src="~/lib/lib.min.js"></script>
```

**Step 4: Test offline**

1. Disconnect network
2. Run application
3. Verify all pages load
4. Check browser console for errors

**Step 5: Commit**

```bash
git add wwwroot/lib/ Pages/Shared/_Layout.cshtml
git commit -m "fix: Remove external CDN dependencies for air-gapped support

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 12: Final Build and Verification

**Step 1: Run full build**

Run: `dotnet build`
Expected: 0 errors, 0 warnings

**Step 2: Run application**

Run: `dotnet run`
Expected: Application starts successfully

**Step 3: Manual verification checklist**

- [ ] Admin/Companies shows correct user counts
- [ ] Roster dock shows 7-day cubes
- [ ] Admin/Index matches Owner/Index styling
- [ ] metric-item text doesn't cut off
- [ ] Owner can see users across all companies
- [ ] Owner can add users to any company
- [ ] Owner/Index has Companies and Directors cards
- [ ] Directors page has reassign functionality
- [ ] EmailConfig has EmailTemplates button
- [ ] Hebrew translations display correctly
- [ ] Calendar/Table loads on air-gapped
- [ ] No external CDN dependencies

**Step 4: Final commit**

```bash
git add .
git commit -m "chore: Complete ShiftManager fixes implementation

- Fixed Companies user count query filter bug
- Added 7-day availability cubes to roster
- Admin/Index design parity with Owner/Index
- Fixed metric-item text cutoff
- Owner cross-company user management
- Company/Director cards on Owner/Index
- Directors reassignment functionality
- EmailTemplates button in EmailConfig
- Hebrew localization gaps filled
- Air-gapped offline compatibility

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Execution Notes

- Each task can be executed independently
- Tasks 1-4 are quick fixes (5-10 min each)
- Tasks 5-7 involve more code changes (15-20 min each)
- Task 10-11 require investigation and may vary
- Test after each commit to catch regressions early
