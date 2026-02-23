# Navigation Completeness Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ensure every management/owner/admin page is reachable from its respective hub, with correct navigation and localization.

**Architecture:** Add 12 quick links to the Owner Hub (view-only change). Add 2 new sections (9 cards, 3 feature-flagged) to the Admin Hub, requiring `IFeatureFlagService` injection into the PageModel. All new text uses `<loc>` tags. No sidebar changes needed.

**Tech Stack:** ASP.NET Core 8.0 Razor Pages, `IFeatureFlagService`, `FeatureFlagSeed.Flags.*`, `<loc key="...">` localization.

---

### Task 1: Add Missing Quick Links to Owner Hub

**Files:**
- Modify: `Pages/Owner/Hub/Index.cshtml` (lines 226-261, the quick-links-grid section)

**Step 1: Add 12 new quick links after the existing 8**

Insert these inside the `<div class="quick-links-grid">` block, after the existing Languages quick link (line 259):

```html
            <a href="/Owner/EmailConfig" class="quick-link">
                <span class="quick-link-icon">&#x2709;</span>
                <span><loc key="OwnerHub_EmailConfig">Email Config</loc></span>
            </a>
            <a href="/Owner/EmailTemplates" class="quick-link">
                <span class="quick-link-icon">&#x1F4E7;</span>
                <span><loc key="OwnerHub_EmailTemplates">Email Templates</loc></span>
            </a>
            <a href="/Owner/GriffinConfig" class="quick-link">
                <span class="quick-link-icon">&#x1F510;</span>
                <span><loc key="OwnerHub_GriffinConfig">Griffin / ADFS</loc></span>
            </a>
            <a href="/Owner/GameConfig" class="quick-link">
                <span class="quick-link-icon">&#x1F3AE;</span>
                <span><loc key="OwnerHub_GameConfig">Game Config</loc></span>
            </a>
            <a href="/Owner/FeatureFlags" class="quick-link">
                <span class="quick-link-icon">&#x1F6A9;</span>
                <span><loc key="OwnerHub_FeatureFlags">Feature Flags</loc></span>
            </a>
            <a href="/Owner/Telemetry" class="quick-link">
                <span class="quick-link-icon">&#x1F4E1;</span>
                <span><loc key="OwnerHub_Telemetry">Telemetry</loc></span>
            </a>
            <a href="/Owner/DataLifecycle" class="quick-link">
                <span class="quick-link-icon">&#x267B;</span>
                <span><loc key="OwnerHub_DataLifecycle">Data Lifecycle</loc></span>
            </a>
            <a href="/Owner/LockedUsers" class="quick-link">
                <span class="quick-link-icon">&#x1F512;</span>
                <span><loc key="OwnerHub_LockedUsers">Locked Users</loc></span>
            </a>
            <a href="/Owner/MasterPrograms" class="quick-link">
                <span class="quick-link-icon">&#x1F4D0;</span>
                <span><loc key="OwnerHub_MasterPrograms">Master Programs</loc></span>
            </a>
            <a href="/Owner/AreaConfig" class="quick-link">
                <span class="quick-link-icon">&#x1F5FA;</span>
                <span><loc key="OwnerHub_AreaConfig">Area Config</loc></span>
            </a>
            <a href="/Owner/Permissions" class="quick-link">
                <span class="quick-link-icon">&#x1F6E1;</span>
                <span><loc key="OwnerHub_Permissions">Permissions</loc></span>
            </a>
            <a href="/Owner/Hub/ExportUserData" class="quick-link">
                <span class="quick-link-icon">&#x1F4E4;</span>
                <span><loc key="OwnerHub_ExportUserData">Export User Data</loc></span>
            </a>
```

**Step 2: Build and verify no errors**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

**Step 3: Commit**

```bash
git add Pages/Owner/Hub/Index.cshtml
git commit -m "feat(owner-hub): add 12 missing quick links for all owner pages"
```

---

### Task 2: Inject IFeatureFlagService into Admin Hub PageModel

**Files:**
- Modify: `Pages/Admin/Index.cshtml.cs`

**Step 1: Add IFeatureFlagService to constructor and expose flag properties**

Add the using at the top:
```csharp
using ShiftManager.Data.SeedData;
```

Add field and constructor parameter:
```csharp
private readonly IFeatureFlagService _featureFlagService;
```

Constructor becomes:
```csharp
public IndexModel(
    AppDbContext db,
    ITenantResolver tenantResolver,
    IGrantService grantService,
    IFeatureFlagService featureFlagService,
    IDirectorService? directorService = null)
{
    _db = db;
    _tenantResolver = tenantResolver;
    _grantService = grantService;
    _featureFlagService = featureFlagService;
    _directorService = directorService;
}
```

Add public properties after the existing `IsManager`:
```csharp
// Feature flag states for conditional hub cards
public bool DutyRotationEnabled { get; set; }
public bool SetupTasksEnabled { get; set; }
public bool VacationApprovalEnabled { get; set; }
```

At the end of `OnGetAsync()`, after the try/catch block, add:
```csharp
// Feature flags for conditional card rendering (sync cache-only check, same as _Layout.cshtml)
DutyRotationEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.DutyRotationEnabled);
SetupTasksEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.SetupTasksEnabled);
VacationApprovalEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.VacationApprovalEnabled);
```

**Step 2: Build and verify no errors**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

**Step 3: Commit**

```bash
git add Pages/Admin/Index.cshtml.cs
git commit -m "feat(admin-hub): inject IFeatureFlagService for conditional card rendering"
```

---

### Task 3: Add Organization & Structure Section to Admin Hub

**Files:**
- Modify: `Pages/Admin/Index.cshtml` (insert after People Management section, before Scheduling Configuration)

**Step 1: Insert the new section**

After the People Management `</div>` (line 82), insert:

```html
    @* Organization & Structure *@
    <h2 class="section-title">
        <span class="section-icon">&#x1F3D7;</span>
        <loc key="Section_OrganizationStructure">Organization & Structure</loc>
    </h2>
    <div class="admin-tools-grid">
        <a href="/Admin/Organization" class="admin-tool-card">
            <div class="tool-icon">&#x1F3E2;</div>
            <div class="tool-content">
                <h3><loc key="Section_OrganizationHub">Organization Hub</loc></h3>
                <p><loc key="Section_OrganizationHubDesc">Hierarchy tree and entity overview</loc></p>
            </div>
            <div class="tool-arrow">&rarr;</div>
        </a>

        <a href="/Admin/Companies" class="admin-tool-card">
            <div class="tool-icon">&#x1F3E2;</div>
            <div class="tool-content">
                <h3><loc key="Section_Companies">Companies</loc></h3>
                <p><loc key="Section_CompaniesDesc">Create, rename, and manage companies</loc></p>
            </div>
            <div class="tool-arrow">&rarr;</div>
        </a>

        <a href="/Admin/Organization/JobTypes" class="admin-tool-card">
            <div class="tool-icon">&#x1F3F7;</div>
            <div class="tool-content">
                <h3><loc key="Section_JobTypes">Job Types</loc></h3>
                <p><loc key="Section_JobTypesDesc">Manage job type definitions and colors</loc></p>
            </div>
            <div class="tool-arrow">&rarr;</div>
        </a>

        <a href="/Admin/Organization/Departments" class="admin-tool-card">
            <div class="tool-icon">&#x1F465;</div>
            <div class="tool-content">
                <h3><loc key="Section_Departments">Departments</loc></h3>
                <p><loc key="Section_DepartmentsDesc">Department management for Tech molecules</loc></p>
            </div>
            <div class="tool-arrow">&rarr;</div>
        </a>
    </div>
```

**Step 2: Build and verify no errors**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

**Step 3: Commit**

```bash
git add Pages/Admin/Index.cshtml
git commit -m "feat(admin-hub): add Organization & Structure section with 4 cards"
```

---

### Task 4: Add Configuration Section to Admin Hub

**Files:**
- Modify: `Pages/Admin/Index.cshtml` (insert after Scheduling Configuration section, before Operations Management)

**Step 1: Insert the Configuration section**

After the Scheduling Configuration `</div>`, insert:

```html
    @* Configuration *@
    <h2 class="section-title">
        <span class="section-icon">&#x2699;</span>
        <loc key="Section_Configuration">Configuration</loc>
    </h2>
    <div class="admin-tools-grid">
        <a href="/Admin/Settings" class="admin-tool-card">
            <div class="tool-icon">&#x1F4CA;</div>
            <div class="tool-content">
                <h3><loc key="Section_HierarchySettings">Hierarchy Settings</loc></h3>
                <p><loc key="Section_HierarchySettingsDesc">Rest hours and weekly caps by level</loc></p>
            </div>
            <div class="tool-arrow">&rarr;</div>
        </a>

        <a href="/Admin/Announcements" class="admin-tool-card">
            <div class="tool-icon">&#x1F4E2;</div>
            <div class="tool-content">
                <h3><loc key="Section_Announcements">Announcements</loc></h3>
                <p><loc key="Section_AnnouncementsDesc">Company announcements and notices</loc></p>
            </div>
            <div class="tool-arrow">&rarr;</div>
        </a>

        @if (Model.VacationApprovalEnabled)
        {
            <a href="/Admin/Settings/ApprovalRules" class="admin-tool-card">
                <div class="tool-icon">&#x2705;</div>
                <div class="tool-content">
                    <h3><loc key="Section_ApprovalRules">Approval Rules</loc></h3>
                    <p><loc key="Section_ApprovalRulesDesc">Vacation approval workflow configuration</loc></p>
                </div>
                <div class="tool-arrow">&rarr;</div>
            </a>
        }

        @if (Model.DutyRotationEnabled)
        {
            <a href="/Admin/DutyRotation" class="admin-tool-card">
                <div class="tool-icon">&#x1F504;</div>
                <div class="tool-content">
                    <h3><loc key="Section_DutyRotation">Duty Rotations</loc></h3>
                    <p><loc key="Section_DutyRotationDesc">On-duty rotation management</loc></p>
                </div>
                <div class="tool-arrow">&rarr;</div>
            </a>
        }

        @if (Model.SetupTasksEnabled)
        {
            <a href="/Admin/SetupTasks" class="admin-tool-card">
                <div class="tool-icon">&#x1F4CB;</div>
                <div class="tool-content">
                    <h3><loc key="Section_SetupTasks">Setup Tasks</loc></h3>
                    <p><loc key="Section_SetupTasksDesc">Company setup wizard and checklist</loc></p>
                </div>
                <div class="tool-arrow">&rarr;</div>
            </a>
        }
    </div>
```

**Step 2: Build and verify no errors**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

**Step 3: Commit**

```bash
git add Pages/Admin/Index.cshtml
git commit -m "feat(admin-hub): add Configuration section with 5 cards (3 feature-flagged)"
```

---

### Task 5: Final Build + Self-Review

**Step 1: Full build**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

**Step 2: Run tests**

Run: `dotnet test`
Expected: All tests pass (236+)

**Step 3: Manual audit checklist**

Verify in each modified file:
- [ ] All text in `<loc>` tags (no hardcoded English)
- [ ] All hrefs point to real pages (cross-check with Pages/ folder)
- [ ] Feature-flagged cards use `Model.XxxEnabled` (not direct service calls in view)
- [ ] No duplicate links (Owner Hub quick links don't repeat existing card destinations unless intentional)
- [ ] HTML entity codes are valid Unicode codepoints

**Step 4: Use superpowers:requesting-code-review to self-review all changes**

**Step 5: Final commit if review passes**

```bash
git add -A
git commit -m "feat: complete navigation hub audit - all pages reachable from hubs"
```
