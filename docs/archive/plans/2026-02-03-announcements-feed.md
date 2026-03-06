# Announcements Feed Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add an announcements feed allowing admins/directors to post company-wide or department-scoped announcements visible to employees on their dashboard.

**Architecture:** Follow the existing Feedback model pattern - create Announcement entity with IBelongsToCompany, service layer (IAnnouncementService), admin management page, and dashboard widget. Announcements have visibility scope (All, Department, Role) and expiration dates.

**Tech Stack:** ASP.NET Core 8, Razor Pages, EF Core, SQLite, existing design tokens

---

## Task 1: Create Announcement Model

**Files:**
- Create: `Models/Announcement.cs`

**Step 1: Create the Announcement model**

```csharp
namespace ShiftManager.Models;

/// <summary>
/// Announcement visibility scope
/// </summary>
public enum AnnouncementScope
{
    All = 0,           // Visible to all employees in company
    Department = 1,    // Visible to specific department
    Role = 2           // Visible to specific role
}

/// <summary>
/// Company announcements for the feed
/// </summary>
public class Announcement : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    /// <summary>
    /// Announcement title/headline
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Full announcement content (supports markdown)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Who created the announcement
    /// </summary>
    public int CreatedBy { get; set; }
    public AppUser? Creator { get; set; }

    /// <summary>
    /// When announcement was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optional expiration date (null = never expires)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Visibility scope
    /// </summary>
    public AnnouncementScope Scope { get; set; } = AnnouncementScope.All;

    /// <summary>
    /// Target department ID (when Scope = Department)
    /// </summary>
    public int? TargetDepartmentId { get; set; }
    public Department? TargetDepartment { get; set; }

    /// <summary>
    /// Target role (when Scope = Role)
    /// </summary>
    public string? TargetRole { get; set; }

    /// <summary>
    /// Whether announcement is pinned to top
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Whether announcement is active
    /// </summary>
    public bool IsActive { get; set; } = true;
}
```

**Step 2: Commit**

```bash
git add Models/Announcement.cs
git commit -m "feat: add Announcement model for company-wide announcements"
```

---

## Task 2: Add DbSet and Migration

**Files:**
- Modify: `Data/ApplicationDbContext.cs`
- Create: Migration via dotnet ef

**Step 1: Add DbSet to ApplicationDbContext**

In `Data/ApplicationDbContext.cs`, add:

```csharp
public DbSet<Announcement> Announcements { get; set; } = null!;
```

And in `OnModelCreating`, add query filter:

```csharp
modelBuilder.Entity<Announcement>().HasQueryFilter(a => a.CompanyId == _tenantId);
```

**Step 2: Create migration**

Run:
```bash
dotnet ef migrations add AddAnnouncements --project ShiftManager.csproj
```

**Step 3: Apply migration**

Run:
```bash
dotnet ef database update --project ShiftManager.csproj
```

**Step 4: Commit**

```bash
git add Data/ApplicationDbContext.cs Migrations/
git commit -m "feat: add Announcements DbSet and migration"
```

---

## Task 3: Create Announcement Service

**Files:**
- Create: `Services/IAnnouncementService.cs`
- Create: `Services/AnnouncementService.cs`

**Step 1: Create the interface**

```csharp
using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IAnnouncementService
{
    Task<List<Announcement>> GetActiveAnnouncementsAsync(int userId);
    Task<List<Announcement>> GetAllAnnouncementsAsync(bool includeExpired = false);
    Task<Announcement?> GetByIdAsync(int id);
    Task<Announcement> CreateAsync(Announcement announcement, int createdBy);
    Task UpdateAsync(Announcement announcement);
    Task DeleteAsync(int id);
    Task<int> GetUnreadCountAsync(int userId);
}
```

**Step 2: Create the service implementation**

```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class AnnouncementService : IAnnouncementService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantResolver _tenantResolver;

    public AnnouncementService(ApplicationDbContext db, ITenantResolver tenantResolver)
    {
        _db = db;
        _tenantResolver = tenantResolver;
    }

    public async Task<List<Announcement>> GetActiveAnnouncementsAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return new List<Announcement>();

        var now = DateTime.UtcNow;
        var query = _db.Announcements
            .Include(a => a.Creator)
            .Where(a => a.IsActive)
            .Where(a => a.ExpiresAt == null || a.ExpiresAt > now);

        // Filter by scope
        query = query.Where(a =>
            a.Scope == AnnouncementScope.All ||
            (a.Scope == AnnouncementScope.Department && a.TargetDepartmentId == user.DepartmentId) ||
            (a.Scope == AnnouncementScope.Role && a.TargetRole == user.Role));

        return await query
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.CreatedAt)
            .Take(20)
            .ToListAsync();
    }

    public async Task<List<Announcement>> GetAllAnnouncementsAsync(bool includeExpired = false)
    {
        var query = _db.Announcements
            .Include(a => a.Creator)
            .Include(a => a.TargetDepartment)
            .AsQueryable();

        if (!includeExpired)
        {
            var now = DateTime.UtcNow;
            query = query.Where(a => a.ExpiresAt == null || a.ExpiresAt > now);
        }

        return await query
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<Announcement?> GetByIdAsync(int id)
    {
        return await _db.Announcements
            .Include(a => a.Creator)
            .Include(a => a.TargetDepartment)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<Announcement> CreateAsync(Announcement announcement, int createdBy)
    {
        announcement.CreatedBy = createdBy;
        announcement.CreatedAt = DateTime.UtcNow;
        announcement.CompanyId = _tenantResolver.GetCurrentTenantId();

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();
        return announcement;
    }

    public async Task UpdateAsync(Announcement announcement)
    {
        _db.Announcements.Update(announcement);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var announcement = await _db.Announcements.FindAsync(id);
        if (announcement != null)
        {
            _db.Announcements.Remove(announcement);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        // For now, return count of recent announcements (last 7 days)
        // Future: track read status per user
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        return await GetActiveAnnouncementsAsync(userId)
            .ContinueWith(t => t.Result.Count(a => a.CreatedAt > weekAgo));
    }
}
```

**Step 3: Register service in DI**

In `Program.cs`, add:
```csharp
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
```

**Step 4: Commit**

```bash
git add Services/IAnnouncementService.cs Services/AnnouncementService.cs Program.cs
git commit -m "feat: add AnnouncementService for CRUD operations"
```

---

## Task 4: Create Admin Management Page

**Files:**
- Create: `Pages/Admin/Announcements.cshtml`
- Create: `Pages/Admin/Announcements.cshtml.cs`

**Step 1: Create the PageModel**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "CanManageAnnouncements")]
public class AnnouncementsModel : PageModel
{
    private readonly IAnnouncementService _announcementService;
    private readonly IDepartmentService _departmentService;
    private readonly IUserService _userService;

    public AnnouncementsModel(
        IAnnouncementService announcementService,
        IDepartmentService departmentService,
        IUserService userService)
    {
        _announcementService = announcementService;
        _departmentService = departmentService;
        _userService = userService;
    }

    public List<Announcement> Announcements { get; set; } = new();
    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Roles { get; set; } = new();

    [BindProperty]
    public Announcement NewAnnouncement { get; set; } = new();

    public async Task OnGetAsync()
    {
        Announcements = await _announcementService.GetAllAnnouncementsAsync(includeExpired: true);
        await LoadDropdownsAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var userId = _userService.GetCurrentUserId();
        await _announcementService.CreateAsync(NewAnnouncement, userId);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await _announcementService.DeleteAsync(id);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var announcement = await _announcementService.GetByIdAsync(id);
        if (announcement != null)
        {
            announcement.IsActive = !announcement.IsActive;
            await _announcementService.UpdateAsync(announcement);
        }
        return RedirectToPage();
    }

    private async Task LoadDropdownsAsync()
    {
        var departments = await _departmentService.GetAllAsync();
        Departments = departments.Select(d => new SelectListItem(d.Name, d.Id.ToString())).ToList();

        Roles = new List<SelectListItem>
        {
            new("Employee", "Employee"),
            new("Manager", "Manager"),
            new("Director", "Director")
        };
    }
}
```

**Step 2: Create the Razor view**

Create `Pages/Admin/Announcements.cshtml` with standard admin page layout using existing design tokens (--text, --text-muted, --surface, etc.). Include:
- List of existing announcements with edit/delete actions
- Create form with title, content, scope selector, expiration date
- Scope-dependent fields (department dropdown, role dropdown)
- Toggle active/inactive buttons

**Step 3: Add authorization policy**

In `Program.cs` authorization configuration, add:
```csharp
options.AddPolicy("CanManageAnnouncements", policy =>
    policy.RequireAssertion(context =>
        context.User.IsInRole("Owner") ||
        context.User.IsInRole("Director") ||
        context.User.HasClaim("Grant", "ManageAnnouncements")));
```

**Step 4: Add navigation link**

In admin sidebar/nav, add link to Announcements page.

**Step 5: Commit**

```bash
git add Pages/Admin/Announcements.cshtml Pages/Admin/Announcements.cshtml.cs
git commit -m "feat: add Admin/Announcements page for managing announcements"
```

---

## Task 5: Add Dashboard Widget

**Files:**
- Modify: `Pages/Index.cshtml`
- Modify: `Pages/Index.cshtml.cs`

**Step 1: Add announcements to Index PageModel**

In `Pages/Index.cshtml.cs`, inject IAnnouncementService and add:

```csharp
public List<Announcement> RecentAnnouncements { get; set; } = new();

// In OnGetAsync:
RecentAnnouncements = await _announcementService.GetActiveAnnouncementsAsync(userId);
```

**Step 2: Add announcements widget to dashboard**

In `Pages/Index.cshtml`, add a card section displaying recent announcements:

```html
<!-- Announcements Feed -->
<div class="card announcements-card">
    <div class="card-header">
        <div class="card-title">
            <i data-lucide="megaphone"></i>
            <loc key="Announcements" />
        </div>
    </div>
    <div class="card-body">
        @if (Model.RecentAnnouncements.Any())
        {
            @foreach (var announcement in Model.RecentAnnouncements.Take(5))
            {
                <div class="announcement-item @(announcement.IsPinned ? "pinned" : "")">
                    @if (announcement.IsPinned)
                    {
                        <i data-lucide="pin" class="pin-icon"></i>
                    }
                    <div class="announcement-title">@announcement.Title</div>
                    <div class="announcement-meta">
                        @announcement.Creator?.DisplayName • @announcement.CreatedAt.ToLocalTime().ToString("MMM d")
                    </div>
                    <div class="announcement-content">@announcement.Content</div>
                </div>
            }
        }
        else
        {
            <div class="empty-state">
                <i data-lucide="megaphone"></i>
                <p><loc key="NoAnnouncements" /></p>
            </div>
        }
    </div>
</div>
```

**Step 3: Add CSS for announcements widget**

Use existing design tokens. Add styles in site.css or components.css:

```css
.announcements-card .announcement-item {
    padding: 0.75rem;
    border-bottom: 1px solid var(--border);
}

.announcements-card .announcement-item.pinned {
    background: var(--surface-alt);
}

.announcements-card .announcement-title {
    font-weight: 600;
    color: var(--text);
}

.announcements-card .announcement-meta {
    font-size: 0.75rem;
    color: var(--text-muted);
    margin: 0.25rem 0;
}

.announcements-card .announcement-content {
    color: var(--text);
    font-size: 0.875rem;
}
```

**Step 4: Add localization keys**

Add to resource files:
- "Announcements"
- "NoAnnouncements"

**Step 5: Commit**

```bash
git add Pages/Index.cshtml Pages/Index.cshtml.cs wwwroot/css/site.css Resources/
git commit -m "feat: add announcements widget to dashboard"
```

---

## Task 6: Add Unit Tests

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/AnnouncementServiceTests.cs`

**Step 1: Write tests for AnnouncementService**

```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using Moq;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

public class AnnouncementServiceTests
{
    [Fact]
    public async Task GetActiveAnnouncementsAsync_FiltersExpiredAnnouncements()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // ... test implementation
    }

    [Fact]
    public async Task GetActiveAnnouncementsAsync_FiltersByDepartmentScope()
    {
        // Test department scoping
    }

    [Fact]
    public async Task CreateAsync_SetsCompanyIdFromTenant()
    {
        // Test tenant scoping on create
    }
}
```

**Step 2: Run tests**

```bash
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~AnnouncementService"
```

**Step 3: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Services/AnnouncementServiceTests.cs
git commit -m "test: add AnnouncementService unit tests"
```

---

## Verification Checklist

- [ ] Announcement model created with all fields
- [ ] Migration applied successfully
- [ ] Service registered in DI
- [ ] Admin page allows CRUD operations
- [ ] Dashboard shows active announcements
- [ ] Scope filtering works (All, Department, Role)
- [ ] Expiration filtering works
- [ ] Pinned announcements appear first
- [ ] Unit tests pass
- [ ] Design tokens used correctly (no --text-primary)

---

## Estimated Effort: 16-24 hours

