# Excel-Like Table Calendars Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace all existing calendar pages with 4 new Excel-like table calendars: Shifts (primary), Chores, On-Call, and Company Overview, plus a beautiful landing page.

**Architecture:** ASP.NET Core Razor Pages with ViewComponents for reusable UI, SignalR for real-time updates, EF Core for data access. Grant-based permissions throughout. RTL-aware responsive design using existing token system.

**Tech Stack:** .NET 8, EF Core, SignalR, Razor Pages, CSS custom properties (tokens.css), vanilla JavaScript

**Reference:** See `docs/plans/2026-02-05-excel-calendars-design.md` for full UI/UX specifications.

---

## Phase 1: Database Schema & Models

### Task 1.1: Create ChoreType Model

**Files:**
- Create: `Models/ChoreType.cs`
- Modify: `Data/AppDbContext.cs`

**Step 1: Create ChoreType model**

```csharp
// Models/ChoreType.cs
namespace ShiftManager.Models;

public class ChoreType
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }  // Hex color e.g. "#F0C14B"
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
    public List<Chore> Chores { get; set; } = new();
}
```

**Step 2: Add DbSet to AppDbContext**

In `Data/AppDbContext.cs`, add to DbSets section:
```csharp
public DbSet<ChoreType> ChoreTypes { get; set; }
```

**Step 3: Add EF configuration**

In `OnModelCreating`, add:
```csharp
modelBuilder.Entity<ChoreType>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.HasOne(e => e.Molecule)
        .WithMany()
        .HasForeignKey(e => e.MoleculeId)
        .OnDelete(DeleteBehavior.Restrict);
    entity.HasOne(e => e.CreatedByUser)
        .WithMany()
        .HasForeignKey(e => e.CreatedByUserId)
        .OnDelete(DeleteBehavior.Restrict);
    entity.HasIndex(e => new { e.MoleculeId, e.Name }).IsUnique();
});
```

**Step 4: Commit**

```bash
git add Models/ChoreType.cs Data/AppDbContext.cs
git commit -m "feat: add ChoreType model for structured chore categories"
```

---

### Task 1.2: Create ShiftCapacityOverride Model

**Files:**
- Create: `Models/ShiftCapacityOverride.cs`
- Modify: `Data/AppDbContext.cs`

**Step 1: Create model**

```csharp
// Models/ShiftCapacityOverride.cs
namespace ShiftManager.Models;

public class ShiftCapacityOverride
{
    public int Id { get; set; }
    public int ShiftTypeId { get; set; }
    public int MoleculeId { get; set; }
    public int JobTypeId { get; set; }
    public DateOnly Date { get; set; }
    public int Capacity { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ShiftType ShiftType { get; set; } = null!;
    public Molecule Molecule { get; set; } = null!;
    public JobType JobType { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
}
```

**Step 2: Add DbSet and configuration to AppDbContext**

```csharp
public DbSet<ShiftCapacityOverride> ShiftCapacityOverrides { get; set; }

// In OnModelCreating:
modelBuilder.Entity<ShiftCapacityOverride>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.HasIndex(e => new { e.ShiftTypeId, e.MoleculeId, e.JobTypeId, e.Date }).IsUnique();
    entity.HasOne(e => e.ShiftType).WithMany().HasForeignKey(e => e.ShiftTypeId).OnDelete(DeleteBehavior.Cascade);
    entity.HasOne(e => e.Molecule).WithMany().HasForeignKey(e => e.MoleculeId).OnDelete(DeleteBehavior.Restrict);
    entity.HasOne(e => e.JobType).WithMany().HasForeignKey(e => e.JobTypeId).OnDelete(DeleteBehavior.Restrict);
});
```

**Step 3: Commit**

```bash
git add Models/ShiftCapacityOverride.cs Data/AppDbContext.cs
git commit -m "feat: add ShiftCapacityOverride model for per-day capacity changes"
```

---

### Task 1.3: Create UserDayNote Model

**Files:**
- Create: `Models/UserDayNote.cs`
- Modify: `Data/AppDbContext.cs`

**Step 1: Create model**

```csharp
// Models/UserDayNote.cs
namespace ShiftManager.Models;

public class UserDayNote : IBelongsToCompany
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public int CompanyId { get; set; }
    public string Note { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public AppUser User { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
}
```

**Step 2: Add DbSet and configuration**

```csharp
public DbSet<UserDayNote> UserDayNotes { get; set; }

// In OnModelCreating:
modelBuilder.Entity<UserDayNote>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.HasIndex(e => new { e.UserId, e.Date, e.CompanyId }).IsUnique();
    entity.Property(e => e.Note).HasMaxLength(500);
    entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
});
```

**Step 3: Commit**

```bash
git add Models/UserDayNote.cs Data/AppDbContext.cs
git commit -m "feat: add UserDayNote model for Overview calendar free-text notes"
```

---

### Task 1.4: Modify Existing Models

**Files:**
- Modify: `Models/ShiftGrouping.cs`
- Modify: `Models/ShiftType.cs`
- Modify: `Models/Chore.cs`

**Step 1: Add JobTypeId and SortOrder to ShiftGrouping**

In `Models/ShiftGrouping.cs`, add:
```csharp
public int? JobTypeId { get; set; }
public int SortOrder { get; set; }

// Navigation
public JobType? JobType { get; set; }
```

**Step 2: Add RowColor to ShiftType**

In `Models/ShiftType.cs`, add:
```csharp
public string? RowColor { get; set; }  // Hex color for calendar row
```

**Step 3: Add ChoreTypeId to Chore**

In `Models/Chore.cs`, add:
```csharp
public int? ChoreTypeId { get; set; }

// Navigation
public ChoreType? ChoreType { get; set; }
```

**Step 4: Update AppDbContext configurations**

```csharp
// ShiftGrouping config update
modelBuilder.Entity<ShiftGrouping>(entity =>
{
    // existing config...
    entity.HasOne(e => e.JobType).WithMany().HasForeignKey(e => e.JobTypeId).OnDelete(DeleteBehavior.SetNull);
});

// Chore config update
modelBuilder.Entity<Chore>(entity =>
{
    // existing config...
    entity.HasOne(e => e.ChoreType).WithMany(ct => ct.Chores).HasForeignKey(e => e.ChoreTypeId).OnDelete(DeleteBehavior.SetNull);
});
```

**Step 5: Commit**

```bash
git add Models/ShiftGrouping.cs Models/ShiftType.cs Models/Chore.cs Data/AppDbContext.cs
git commit -m "feat: extend ShiftGrouping, ShiftType, and Chore for calendar features"
```

---

### Task 1.5: Create Migration

**Step 1: Generate migration**

Run:
```bash
dotnet ef migrations add AddExcelCalendarModels
```

**Step 2: Review and adjust migration if needed**

Check `Migrations/YYYYMMDD_AddExcelCalendarModels.cs` for correctness.

**Step 3: Apply migration**

Run:
```bash
dotnet ef database update
```

**Step 4: Commit**

```bash
git add Migrations/
git commit -m "chore: add migration for Excel calendar models"
```

---

### Task 1.6: Add New Grant Types

**Files:**
- Modify: `Data/SeedData/GrantTypeSeed.cs`
- Modify: `Resources/SharedResources.resx`
- Modify: `Resources/SharedResources.he-IL.resx`

**Step 1: Add new grants to GrantTypeSeed.cs**

After the existing grants, add:
```csharp
// ============================================
// CALENDAR GRANTS (Category.Shift)
// ============================================

grants.Add(new GrantType { Id = id++, Key = "ManageShiftCapacity", NameKey = "Grant_ManageShiftCapacity", DescriptionKey = "Grant_ManageShiftCapacity_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
grants.Add(new GrantType { Id = id++, Key = "WriteOverviewNotes", NameKey = "Grant_WriteOverviewNotes", DescriptionKey = "Grant_WriteOverviewNotes_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
grants.Add(new GrantType { Id = id++, Key = "ManageOnDutyTypes", NameKey = "Grant_ManageOnDutyTypes", DescriptionKey = "Grant_ManageOnDutyTypes_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
```

**Step 2: Add localization keys**

In `SharedResources.resx` and `SharedResources.he-IL.resx`, add:
```xml
<data name="Grant_ManageShiftCapacity" xml:space="preserve">
  <value>Manage Shift Capacity</value>
</data>
<data name="Grant_ManageShiftCapacity_Desc" xml:space="preserve">
  <value>Override shift capacity for specific days</value>
</data>
<data name="Grant_WriteOverviewNotes" xml:space="preserve">
  <value>Write Overview Notes</value>
</data>
<data name="Grant_WriteOverviewNotes_Desc" xml:space="preserve">
  <value>Add free-text notes in company overview calendar</value>
</data>
<data name="Grant_ManageOnDutyTypes" xml:space="preserve">
  <value>Manage On-Duty Types</value>
</data>
<data name="Grant_ManageOnDutyTypes_Desc" xml:space="preserve">
  <value>Create and edit on-call duty types</value>
</data>
```

Hebrew versions:
```xml
<data name="Grant_ManageShiftCapacity" xml:space="preserve">
  <value>ניהול קיבולת משמרות</value>
</data>
<data name="Grant_ManageShiftCapacity_Desc" xml:space="preserve">
  <value>שינוי קיבולת משמרות לימים ספציפיים</value>
</data>
<data name="Grant_WriteOverviewNotes" xml:space="preserve">
  <value>כתיבת הערות בסקירה</value>
</data>
<data name="Grant_WriteOverviewNotes_Desc" xml:space="preserve">
  <value>הוספת הערות חופשיות בלוח סקירת החברה</value>
</data>
<data name="Grant_ManageOnDutyTypes" xml:space="preserve">
  <value>ניהול סוגי כוננויות</value>
</data>
<data name="Grant_ManageOnDutyTypes_Desc" xml:space="preserve">
  <value>יצירה ועריכה של סוגי כוננויות</value>
</data>
```

**Step 3: Commit**

```bash
git add Data/SeedData/GrantTypeSeed.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat: add calendar-related grant types"
```

---

## Phase 2: Core Services

### Task 2.1: Create IShiftCalendarService Interface

**Files:**
- Create: `Services/IShiftCalendarService.cs`

**Step 1: Create interface**

```csharp
// Services/IShiftCalendarService.cs
using ShiftManager.Models;

namespace ShiftManager.Services;

public record FyiOverlayData(
    bool HasVacation,
    bool HasChore,
    bool HasOnDuty,
    List<string> OtherShifts
);

public record RestViolationWarning(
    string ShiftName,
    DateOnly Date,
    TimeSpan RestDuration
);

public record AssignmentResult(
    bool Success,
    string? ErrorMessage,
    List<RestViolationWarning> Warnings
);

public interface IShiftCalendarService
{
    // Data loading
    Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int jobTypeId);
    Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end);
    Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end);

    // Capacity management
    Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date);
    Task<int> GetDefaultCapacityAsync(int shiftTypeId);
    Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date, int capacity, int userId);
    Task RemoveCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date);

    // Assignment
    Task<AssignmentResult> AssignUserAsync(int shiftInstanceId, int userId, int assignedByUserId);
    Task<bool> UnassignUserAsync(int shiftAssignmentId, int unassignedByUserId);

    // Validation
    Task<List<RestViolationWarning>> CheckRestViolationsAsync(int userId, DateOnly date, int shiftTypeId);

    // FYI overlays
    Task<Dictionary<(int UserId, DateOnly Date), FyiOverlayData>> GetOverlaysAsync(int moleculeId, DateOnly start, DateOnly end);
}
```

**Step 2: Commit**

```bash
git add Services/IShiftCalendarService.cs
git commit -m "feat: add IShiftCalendarService interface"
```

---

### Task 2.2: Implement ShiftCalendarService

**Files:**
- Create: `Services/ShiftCalendarService.cs`
- Modify: `Program.cs` (register service)

**Step 1: Create service implementation**

```csharp
// Services/ShiftCalendarService.cs
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class ShiftCalendarService : IShiftCalendarService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ShiftCalendarService> _logger;
    private const int DEFAULT_REST_HOURS = 8;

    public ShiftCalendarService(AppDbContext db, ILogger<ShiftCalendarService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int jobTypeId)
    {
        // Get all companies in this molecule
        var companyIds = await _db.Companies
            .Where(c => c.MoleculeId == moleculeId)
            .Select(c => c.Id)
            .ToListAsync();

        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && companyIds.Contains(u.CompanyId) && u.JobTypeId == jobTypeId)
            .Include(u => u.JobType)
            .Include(u => u.Company)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    public async Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end)
    {
        return await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
            .Include(si => si.Company)
            .Where(si => si.ShiftType.MoleculeId == moleculeId
                && si.ShiftType.JobTypeId == jobTypeId
                && si.WorkDate >= start
                && si.WorkDate <= end)
            .ToListAsync();
    }

    public async Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end)
    {
        return await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(sa => sa.User)
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => sa.ShiftInstance.ShiftType.MoleculeId == moleculeId
                && sa.ShiftInstance.ShiftType.JobTypeId == jobTypeId
                && sa.ShiftInstance.WorkDate >= start
                && sa.ShiftInstance.WorkDate <= end)
            .ToListAsync();
    }

    public async Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date)
    {
        var overrideCapacity = await _db.ShiftCapacityOverrides
            .Where(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.JobTypeId == jobTypeId
                && o.Date == date)
            .Select(o => (int?)o.Capacity)
            .FirstOrDefaultAsync();

        if (overrideCapacity.HasValue)
            return overrideCapacity.Value;

        return await GetDefaultCapacityAsync(shiftTypeId);
    }

    public async Task<int> GetDefaultCapacityAsync(int shiftTypeId)
    {
        var shiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);
        return shiftType?.DefaultStaffingRequired ?? 1;
    }

    public async Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date, int capacity, int userId)
    {
        var existing = await _db.ShiftCapacityOverrides
            .FirstOrDefaultAsync(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.JobTypeId == jobTypeId
                && o.Date == date);

        if (existing != null)
        {
            existing.Capacity = capacity;
            existing.CreatedByUserId = userId;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.ShiftCapacityOverrides.Add(new ShiftCapacityOverride
            {
                ShiftTypeId = shiftTypeId,
                MoleculeId = moleculeId,
                JobTypeId = jobTypeId,
                Date = date,
                Capacity = capacity,
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task RemoveCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date)
    {
        var existing = await _db.ShiftCapacityOverrides
            .FirstOrDefaultAsync(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.JobTypeId == jobTypeId
                && o.Date == date);

        if (existing != null)
        {
            _db.ShiftCapacityOverrides.Remove(existing);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<AssignmentResult> AssignUserAsync(int shiftInstanceId, int userId, int assignedByUserId)
    {
        var shiftInstance = await _db.ShiftInstances
            .Include(si => si.ShiftType)
            .FirstOrDefaultAsync(si => si.Id == shiftInstanceId);

        if (shiftInstance == null)
            return new AssignmentResult(false, "Shift instance not found", new());

        // Check for rest violations
        var warnings = await CheckRestViolationsAsync(userId, shiftInstance.WorkDate, shiftInstance.ShiftTypeId);

        // Create assignment
        var assignment = new ShiftAssignment
        {
            ShiftInstanceId = shiftInstanceId,
            UserId = userId,
            AssignedByUserId = assignedByUserId,
            AssignedAt = DateTime.UtcNow
        };

        _db.ShiftAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        return new AssignmentResult(true, null, warnings);
    }

    public async Task<bool> UnassignUserAsync(int shiftAssignmentId, int unassignedByUserId)
    {
        var assignment = await _db.ShiftAssignments.FindAsync(shiftAssignmentId);
        if (assignment == null)
            return false;

        _db.ShiftAssignments.Remove(assignment);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<RestViolationWarning>> CheckRestViolationsAsync(int userId, DateOnly date, int shiftTypeId)
    {
        var warnings = new List<RestViolationWarning>();

        var targetShiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);
        if (targetShiftType == null)
            return warnings;

        // Check previous day's shifts
        var previousDate = date.AddDays(-1);
        var previousAssignments = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => sa.UserId == userId && sa.ShiftInstance.WorkDate == previousDate)
            .ToListAsync();

        foreach (var prevAssignment in previousAssignments)
        {
            var prevEnd = prevAssignment.ShiftInstance.ShiftType.End;
            var targetStart = targetShiftType.Start;

            // Calculate rest duration (handling overnight shifts)
            var restHours = CalculateRestHours(prevEnd, targetStart);

            if (restHours < DEFAULT_REST_HOURS)
            {
                warnings.Add(new RestViolationWarning(
                    prevAssignment.ShiftInstance.ShiftType.DisplayName,
                    previousDate,
                    TimeSpan.FromHours(restHours)
                ));
            }
        }

        return warnings;
    }

    private double CalculateRestHours(TimeOnly previousEnd, TimeOnly nextStart)
    {
        // Simple calculation - assumes next day
        var endDateTime = DateTime.Today.Add(previousEnd.ToTimeSpan());
        var startDateTime = DateTime.Today.AddDays(1).Add(nextStart.ToTimeSpan());
        return (startDateTime - endDateTime).TotalHours;
    }

    public async Task<Dictionary<(int UserId, DateOnly Date), FyiOverlayData>> GetOverlaysAsync(int moleculeId, DateOnly start, DateOnly end)
    {
        var result = new Dictionary<(int UserId, DateOnly Date), FyiOverlayData>();

        // Get all users in molecule
        var userIds = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Company.MoleculeId == moleculeId && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();

        // Get vacations
        var vacations = await _db.Vacations
            .IgnoreQueryFilters()
            .Where(v => userIds.Contains(v.UserId)
                && v.StartDate <= end
                && v.EndDate >= start
                && v.Status == VacationStatus.Approved)
            .ToListAsync();

        // Get chores
        var chores = await _db.Chores
            .IgnoreQueryFilters()
            .Where(c => userIds.Contains(c.UserId ?? 0)
                && c.Date >= start
                && c.Date <= end)
            .ToListAsync();

        // Get on-duties
        var onDuties = await _db.OnDuties
            .IgnoreQueryFilters()
            .Where(od => userIds.Contains(od.UserId ?? 0)
                && od.Date >= start
                && od.Date <= end)
            .ToListAsync();

        // Get all shifts for overlay display
        var shifts = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => userIds.Contains(sa.UserId ?? 0)
                && sa.ShiftInstance.WorkDate >= start
                && sa.ShiftInstance.WorkDate <= end)
            .ToListAsync();

        // Build overlay data for each user-date combination
        foreach (var userId in userIds)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                var hasVacation = vacations.Any(v => v.UserId == userId && v.StartDate <= date && v.EndDate >= date);
                var hasChore = chores.Any(c => c.UserId == userId && c.Date == date);
                var hasOnDuty = onDuties.Any(od => od.UserId == userId && od.Date == date);
                var otherShifts = shifts
                    .Where(s => s.UserId == userId && s.ShiftInstance.WorkDate == date)
                    .Select(s => s.ShiftInstance.ShiftType.DisplayName)
                    .ToList();

                if (hasVacation || hasChore || hasOnDuty || otherShifts.Any())
                {
                    result[(userId, date)] = new FyiOverlayData(hasVacation, hasChore, hasOnDuty, otherShifts);
                }
            }
        }

        return result;
    }
}
```

**Step 2: Register service in Program.cs**

Add after other service registrations:
```csharp
builder.Services.AddScoped<IShiftCalendarService, ShiftCalendarService>();
```

**Step 3: Commit**

```bash
git add Services/ShiftCalendarService.cs Program.cs
git commit -m "feat: implement ShiftCalendarService for calendar data operations"
```

---

### Task 2.3: Create ChoreTypeService

**Files:**
- Create: `Services/IChoreTypeService.cs`
- Create: `Services/ChoreTypeService.cs`

**Step 1: Create interface**

```csharp
// Services/IChoreTypeService.cs
using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IChoreTypeService
{
    Task<List<ChoreType>> GetChoreTypesForMoleculeAsync(int moleculeId);
    Task<ChoreType?> GetByIdAsync(int id);
    Task<ChoreType> CreateAsync(int moleculeId, string name, string displayName, string? color, int userId);
    Task<ChoreType> UpdateAsync(int id, string displayName, string? color, int sortOrder);
    Task<bool> DeactivateAsync(int id);
}
```

**Step 2: Create implementation**

```csharp
// Services/ChoreTypeService.cs
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class ChoreTypeService : IChoreTypeService
{
    private readonly AppDbContext _db;

    public ChoreTypeService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ChoreType>> GetChoreTypesForMoleculeAsync(int moleculeId)
    {
        return await _db.ChoreTypes
            .Where(ct => ct.MoleculeId == moleculeId && ct.IsActive)
            .OrderBy(ct => ct.SortOrder)
            .ThenBy(ct => ct.DisplayName)
            .ToListAsync();
    }

    public async Task<ChoreType?> GetByIdAsync(int id)
    {
        return await _db.ChoreTypes.FindAsync(id);
    }

    public async Task<ChoreType> CreateAsync(int moleculeId, string name, string displayName, string? color, int userId)
    {
        var maxSortOrder = await _db.ChoreTypes
            .Where(ct => ct.MoleculeId == moleculeId)
            .MaxAsync(ct => (int?)ct.SortOrder) ?? 0;

        var choreType = new ChoreType
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            Color = color,
            SortOrder = maxSortOrder + 1,
            CreatedByUserId = userId
        };

        _db.ChoreTypes.Add(choreType);
        await _db.SaveChangesAsync();
        return choreType;
    }

    public async Task<ChoreType> UpdateAsync(int id, string displayName, string? color, int sortOrder)
    {
        var choreType = await _db.ChoreTypes.FindAsync(id);
        if (choreType == null)
            throw new ArgumentException("ChoreType not found", nameof(id));

        choreType.DisplayName = displayName;
        choreType.Color = color;
        choreType.SortOrder = sortOrder;

        await _db.SaveChangesAsync();
        return choreType;
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        var choreType = await _db.ChoreTypes.FindAsync(id);
        if (choreType == null)
            return false;

        choreType.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }
}
```

**Step 3: Register in Program.cs**

```csharp
builder.Services.AddScoped<IChoreTypeService, ChoreTypeService>();
```

**Step 4: Commit**

```bash
git add Services/IChoreTypeService.cs Services/ChoreTypeService.cs Program.cs
git commit -m "feat: add ChoreTypeService for managing chore categories"
```

---

### Task 2.4: Create UserDayNoteService

**Files:**
- Create: `Services/IUserDayNoteService.cs`
- Create: `Services/UserDayNoteService.cs`

**Step 1: Create interface**

```csharp
// Services/IUserDayNoteService.cs
using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IUserDayNoteService
{
    Task<UserDayNote?> GetNoteAsync(int userId, DateOnly date, int companyId);
    Task<Dictionary<(int UserId, DateOnly Date), string>> GetNotesForCompanyAsync(int companyId, DateOnly start, DateOnly end);
    Task<UserDayNote> SetNoteAsync(int userId, DateOnly date, int companyId, string note, int createdByUserId);
    Task<bool> DeleteNoteAsync(int userId, DateOnly date, int companyId);
}
```

**Step 2: Create implementation**

```csharp
// Services/UserDayNoteService.cs
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class UserDayNoteService : IUserDayNoteService
{
    private readonly AppDbContext _db;

    public UserDayNoteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserDayNote?> GetNoteAsync(int userId, DateOnly date, int companyId)
    {
        return await _db.UserDayNotes
            .FirstOrDefaultAsync(n => n.UserId == userId && n.Date == date && n.CompanyId == companyId);
    }

    public async Task<Dictionary<(int UserId, DateOnly Date), string>> GetNotesForCompanyAsync(int companyId, DateOnly start, DateOnly end)
    {
        var notes = await _db.UserDayNotes
            .Where(n => n.CompanyId == companyId && n.Date >= start && n.Date <= end)
            .ToListAsync();

        return notes.ToDictionary(n => (n.UserId, n.Date), n => n.Note);
    }

    public async Task<UserDayNote> SetNoteAsync(int userId, DateOnly date, int companyId, string note, int createdByUserId)
    {
        var existing = await GetNoteAsync(userId, date, companyId);

        if (existing != null)
        {
            existing.Note = note;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new UserDayNote
            {
                UserId = userId,
                Date = date,
                CompanyId = companyId,
                Note = note,
                CreatedByUserId = createdByUserId
            };
            _db.UserDayNotes.Add(existing);
        }

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteNoteAsync(int userId, DateOnly date, int companyId)
    {
        var note = await GetNoteAsync(userId, date, companyId);
        if (note == null)
            return false;

        _db.UserDayNotes.Remove(note);
        await _db.SaveChangesAsync();
        return true;
    }
}
```

**Step 3: Register in Program.cs**

```csharp
builder.Services.AddScoped<IUserDayNoteService, UserDayNoteService>();
```

**Step 4: Commit**

```bash
git add Services/IUserDayNoteService.cs Services/UserDayNoteService.cs Program.cs
git commit -m "feat: add UserDayNoteService for Overview calendar notes"
```

---

## Phase 3: Shared UI Components

### Task 3.1: Create Excel Calendar Table CSS

**Files:**
- Modify: `wwwroot/css/calendar.css`

**Step 1: Add Excel calendar styles**

Append to `wwwroot/css/calendar.css`:

```css
/* ========================================
   EXCEL CALENDAR TABLE STYLES
   ======================================== */

.excel-calendar {
    overflow: auto;
    max-height: calc(100vh - 200px);
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    background: var(--surface);
}

.excel-calendar__table {
    border-collapse: separate;
    border-spacing: 0;
    min-width: 100%;
    font-size: var(--text-small);
}

/* Sticky header */
.excel-calendar__header {
    position: sticky;
    top: 0;
    z-index: var(--z-sticky);
    background: var(--primary);
    color: var(--primary-contrast);
}

.excel-calendar__header th {
    padding: var(--space-3);
    font-weight: var(--font-semibold);
    text-align: center;
    border-bottom: 2px solid var(--border-strong);
    white-space: nowrap;
}

.excel-calendar__header-day {
    min-width: 100px;
}

.excel-calendar__header-day--today {
    border-inline-start: 3px solid var(--accent);
}

.excel-calendar__header-day--weekend {
    background: var(--primary-hover);
}

/* Date display in header */
.excel-calendar__day-name {
    display: block;
    font-weight: var(--font-semibold);
}

.excel-calendar__day-date {
    display: block;
    font-size: var(--text-tiny);
    opacity: 0.9;
}

/* Sticky first column */
.excel-calendar__row-label {
    position: sticky;
    left: 0;
    z-index: calc(var(--z-sticky) - 1);
    background: var(--surface);
    font-weight: var(--font-medium);
    padding: var(--space-3);
    border-inline-end: 1px solid var(--border);
    min-width: 150px;
    white-space: nowrap;
}

/* Corner cell (sticky both ways) */
.excel-calendar__corner {
    position: sticky;
    top: 0;
    left: 0;
    z-index: calc(var(--z-sticky) + 1);
    background: var(--primary);
}

/* Data cells */
.excel-calendar__cell {
    padding: var(--space-2);
    border: 1px solid var(--border);
    vertical-align: top;
    min-height: 60px;
    position: relative;
    cursor: pointer;
    transition: background-color var(--transition-fast);
}

.excel-calendar__cell:hover {
    background: var(--surface-soft);
}

.excel-calendar__cell--today {
    border-inline-start: 3px solid var(--primary);
}

.excel-calendar__cell--readonly {
    cursor: default;
}

.excel-calendar__cell--readonly:hover {
    background: transparent;
}

/* Cell content */
.excel-calendar__cell-content {
    display: flex;
    flex-direction: column;
    gap: var(--space-1);
}

.excel-calendar__assignment {
    display: flex;
    align-items: center;
    gap: var(--space-1);
    padding: var(--space-1) var(--space-2);
    background: var(--accent-soft);
    border-radius: var(--radius-sm);
    font-size: var(--text-tiny);
}

.excel-calendar__assignment-name {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

/* FYI badges */
.excel-calendar__badges {
    display: flex;
    gap: 2px;
    flex-wrap: wrap;
}

.excel-calendar__badge {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 18px;
    height: 18px;
    border-radius: var(--radius-full);
    font-size: 10px;
}

.excel-calendar__badge--vacation {
    background: var(--info-soft);
    color: var(--info);
}

.excel-calendar__badge--chore {
    background: var(--warning-soft);
    color: var(--warning);
}

.excel-calendar__badge--onduty {
    background: var(--camo-sand-soft);
    color: var(--shift-hakam);
}

.excel-calendar__badge--shift {
    background: var(--surface-soft);
    color: var(--text-muted);
}

/* Empty state */
.excel-calendar__empty {
    color: var(--text-muted);
    font-style: italic;
    text-align: center;
    padding: var(--space-2);
}

/* Add button on hover */
.excel-calendar__add-btn {
    position: absolute;
    top: var(--space-1);
    left: var(--space-1);
    width: 20px;
    height: 20px;
    border-radius: var(--radius-full);
    background: var(--primary);
    color: var(--primary-contrast);
    border: none;
    font-size: 14px;
    cursor: pointer;
    opacity: 0;
    transition: opacity var(--transition-fast);
}

.excel-calendar__cell:hover .excel-calendar__add-btn {
    opacity: 1;
}

/* Group header rows */
.excel-calendar__group-header {
    background: var(--surface-soft);
}

.excel-calendar__group-header td {
    padding: var(--space-2) var(--space-3);
    font-weight: var(--font-semibold);
    border-top: 2px solid var(--border-strong);
}

.excel-calendar__group-toggle {
    cursor: pointer;
    user-select: none;
    display: flex;
    align-items: center;
    gap: var(--space-2);
}

.excel-calendar__group-chevron {
    transition: transform var(--transition-fast);
}

.excel-calendar__group-chevron--collapsed {
    transform: rotate(-90deg);
}

[dir="rtl"] .excel-calendar__group-chevron--collapsed {
    transform: rotate(90deg);
}

.excel-calendar__group-grip {
    cursor: grab;
    color: var(--text-muted);
}

/* Read-only mode banner */
.excel-calendar__readonly-banner {
    background: var(--warning-soft);
    color: var(--warning);
    padding: var(--space-2) var(--space-4);
    text-align: center;
    font-size: var(--text-small);
    border-bottom: 1px solid var(--warning);
}

/* Compact mode for month view */
.excel-calendar--compact .excel-calendar__cell {
    padding: var(--space-1);
    min-height: 40px;
}

.excel-calendar--compact .excel-calendar__assignment {
    padding: 2px var(--space-1);
}

.excel-calendar--compact .excel-calendar__header-day {
    min-width: 60px;
}

/* Toolbar */
.excel-calendar-toolbar {
    display: flex;
    align-items: center;
    gap: var(--space-4);
    padding: var(--space-3) 0;
    flex-wrap: wrap;
}

.excel-calendar-toolbar__group {
    display: flex;
    align-items: center;
    gap: var(--space-2);
}

.excel-calendar-toolbar__separator {
    width: 1px;
    height: 24px;
    background: var(--border);
}
```

**Step 2: Commit**

```bash
git add wwwroot/css/calendar.css
git commit -m "feat: add Excel calendar table CSS styles"
```

---

### Task 3.2: Create ExcelCalendarTable ViewComponent

**Files:**
- Create: `ViewComponents/ExcelCalendarTableViewComponent.cs`
- Create: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`

**Step 1: Create ViewComponent**

```csharp
// ViewComponents/ExcelCalendarTableViewComponent.cs
using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

public class ExcelCalendarTableViewModel
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month
    public bool IsReadOnly { get; set; }
    public string CalendarType { get; set; } = "shifts"; // shifts, chores, oncall, overview
    public List<ExcelCalendarRow> Rows { get; set; } = new();
    public List<ExcelCalendarGroup>? Groups { get; set; }
}

public class ExcelCalendarRow
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? GroupId { get; set; }
    public Dictionary<DateOnly, ExcelCalendarCell> Cells { get; set; } = new();
}

public class ExcelCalendarGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsCollapsed { get; set; }
    public int SortOrder { get; set; }
}

public class ExcelCalendarCell
{
    public List<ExcelCalendarAssignment> Assignments { get; set; } = new();
    public ExcelCalendarOverlay? Overlay { get; set; }
    public string? Note { get; set; }
    public int? Capacity { get; set; }
    public int? DefaultCapacity { get; set; }
}

public class ExcelCalendarAssignment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public bool IsTrainee { get; set; }
}

public class ExcelCalendarOverlay
{
    public bool HasVacation { get; set; }
    public bool HasChore { get; set; }
    public bool HasOnDuty { get; set; }
    public List<string> OtherItems { get; set; } = new();
}

public class ExcelCalendarTableViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(ExcelCalendarTableViewModel model)
    {
        return View(model);
    }
}
```

**Step 2: Create View**

```html
@* Pages/Shared/Components/ExcelCalendarTable/Default.cshtml *@
@model ShiftManager.ViewComponents.ExcelCalendarTableViewModel
@using Microsoft.Extensions.Localization
@using ShiftManager.Resources
@inject IStringLocalizer<SharedResources> Localizer

@{
    var days = Enumerable.Range(0, (Model.EndDate.DayNumber - Model.StartDate.DayNumber) + 1)
        .Select(i => Model.StartDate.AddDays(i))
        .ToList();
    var today = DateOnly.FromDateTime(DateTime.Today);
    var isCompact = Model.ViewMode == "month";
    var hebrewDays = new[] { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת" };
    var shortHebrewDays = new[] { "א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "ש׳" };
}

@if (Model.IsReadOnly)
{
    <div class="excel-calendar__readonly-banner">
        <loc key="Calendar_ReadOnlyMode" />
    </div>
}

<div class="excel-calendar @(isCompact ? "excel-calendar--compact" : "")"
     data-calendar-type="@Model.CalendarType"
     data-start-date="@Model.StartDate.ToString("yyyy-MM-dd")"
     data-end-date="@Model.EndDate.ToString("yyyy-MM-dd")"
     data-readonly="@Model.IsReadOnly.ToString().ToLower()">

    <table class="excel-calendar__table" role="grid">
        <thead class="excel-calendar__header">
            <tr>
                <th class="excel-calendar__corner" scope="col"></th>
                @foreach (var day in days)
                {
                    var dayIndex = (int)day.DayOfWeek;
                    var isToday = day == today;
                    var isWeekend = dayIndex == 5 || dayIndex == 6; // Friday or Saturday
                    var showWeekend = Model.ViewMode == "month" && isWeekend;
                    var dayName = isCompact ? shortHebrewDays[dayIndex] : hebrewDays[dayIndex];

                    <th class="excel-calendar__header-day @(isToday ? "excel-calendar__header-day--today" : "") @(showWeekend ? "excel-calendar__header-day--weekend" : "")"
                        scope="col"
                        data-date="@day.ToString("yyyy-MM-dd")">
                        <span class="excel-calendar__day-name">@dayName</span>
                        <span class="excel-calendar__day-date">@day.ToString("dd/MM")</span>
                    </th>
                }
            </tr>
        </thead>
        <tbody>
            @if (Model.Groups != null)
            {
                foreach (var group in Model.Groups.OrderBy(g => g.SortOrder))
                {
                    <tr class="excel-calendar__group-header" data-group-id="@group.Id">
                        <td colspan="@(days.Count + 1)">
                            <div class="excel-calendar__group-toggle">
                                <span class="excel-calendar__group-chevron @(group.IsCollapsed ? "excel-calendar__group-chevron--collapsed" : "")">▼</span>
                                <span class="excel-calendar__group-name" data-editable="true">@group.Name</span>
                                <span class="excel-calendar__group-grip" title="@Localizer["DragToReorder"]">⋮⋮</span>
                            </div>
                        </td>
                    </tr>

                    @foreach (var row in Model.Rows.Where(r => r.GroupId == group.Id))
                    {
                        @await Html.PartialAsync("_ExcelCalendarRow", new { Row = row, Days = days, Today = today, IsReadOnly = Model.IsReadOnly, IsCollapsed = group.IsCollapsed })
                    }
                }

                @* Rows without group *@
                foreach (var row in Model.Rows.Where(r => r.GroupId == null))
                {
                    @await Html.PartialAsync("_ExcelCalendarRow", new { Row = row, Days = days, Today = today, IsReadOnly = Model.IsReadOnly, IsCollapsed = false })
                }
            }
            else
            {
                foreach (var row in Model.Rows)
                {
                    <tr data-row-id="@row.Id" style="@(row.Color != null ? $"--row-color: {row.Color}" : "")">
                        <td class="excel-calendar__row-label">@row.Label</td>
                        @foreach (var day in days)
                        {
                            var cell = row.Cells.GetValueOrDefault(day);
                            var isToday = day == today;

                            <td class="excel-calendar__cell @(isToday ? "excel-calendar__cell--today" : "") @(Model.IsReadOnly ? "excel-calendar__cell--readonly" : "")"
                                data-row-id="@row.Id"
                                data-date="@day.ToString("yyyy-MM-dd")"
                                role="gridcell"
                                tabindex="0">

                                @if (!Model.IsReadOnly)
                                {
                                    <button type="button" class="excel-calendar__add-btn" aria-label="@Localizer["AddAssignment"]">⊕</button>
                                }

                                <div class="excel-calendar__cell-content">
                                    @if (cell?.Assignments?.Any() == true)
                                    {
                                        foreach (var assignment in cell.Assignments)
                                        {
                                            <div class="excel-calendar__assignment" data-assignment-id="@assignment.Id">
                                                <span class="excel-calendar__assignment-name">@assignment.Name</span>
                                                @if (assignment.IsTrainee)
                                                {
                                                    <span class="excel-calendar__badge excel-calendar__badge--trainee" title="@Localizer["Trainee"]">🎓</span>
                                                }
                                            </div>
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(cell?.Note))
                                    {
                                        <span class="excel-calendar__note">@cell.Note</span>
                                    }
                                    else
                                    {
                                        <span class="excel-calendar__empty">@Localizer["Empty"]</span>
                                    }

                                    @if (cell?.Overlay != null)
                                    {
                                        <div class="excel-calendar__badges">
                                            @if (cell.Overlay.HasVacation)
                                            {
                                                <span class="excel-calendar__badge excel-calendar__badge--vacation" title="@Localizer["Vacation"]">🏖️</span>
                                            }
                                            @if (cell.Overlay.HasChore)
                                            {
                                                <span class="excel-calendar__badge excel-calendar__badge--chore" title="@Localizer["Chore"]">🧹</span>
                                            }
                                            @if (cell.Overlay.HasOnDuty)
                                            {
                                                <span class="excel-calendar__badge excel-calendar__badge--onduty" title="@Localizer["OnDuty"]">📞</span>
                                            }
                                        </div>
                                    }
                                </div>
                            </td>
                        }
                    </tr>
                }
            }
        </tbody>
    </table>
</div>
```

**Step 3: Commit**

```bash
git add ViewComponents/ExcelCalendarTableViewComponent.cs Pages/Shared/Components/ExcelCalendarTable/
git commit -m "feat: add ExcelCalendarTable ViewComponent"
```

---

## Phase 4: Calendar Pages (Shifts - PRIMARY)

*Note: This phase implements the primary Shifts Calendar. Phases 5-7 follow similar patterns for Chores, On-Call, and Overview calendars.*

### Task 4.1: Create Shifts Calendar Page

**Files:**
- Create: `Pages/Calendar/Shifts.cshtml`
- Create: `Pages/Calendar/Shifts.cshtml.cs`

**Step 1: Create PageModel**

```csharp
// Pages/Calendar/Shifts.cshtml.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.ViewComponents;

namespace ShiftManager.Pages.Calendar;

[Authorize]
public class ShiftsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IShiftCalendarService _calendarService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;

    public ShiftsModel(
        AppDbContext db,
        IShiftCalendarService calendarService,
        IGrantService grantService,
        ICompanyContext companyContext)
    {
        _db = db;
        _calendarService = calendarService;
        _grantService = grantService;
        _companyContext = companyContext;
    }

    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? JobTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string View { get; set; } = "week";

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "shift"; // shift or user

    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEdit { get; set; }
    public bool IsCapacityMode { get; set; }
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public List<JobType> AvailableJobTypes { get; set; } = new();
    public Molecule? SelectedMolecule { get; set; }
    public JobType? SelectedJobType { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = _companyContext.UserId;
        var companyId = _companyContext.CompanyId;

        // Get user's molecule
        var user = await _db.Users
            .Include(u => u.Company)
                .ThenInclude(c => c.Molecule)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.Company?.Molecule == null)
            return RedirectToPage("/Index");

        // Set defaults
        MoleculeId ??= user.Company.MoleculeId;
        Start ??= GetStartOfWeek(DateOnly.FromDateTime(DateTime.Today));

        // Get available molecules and job types
        AvailableMolecules = await _db.Molecules
            .Where(m => m.AreaId == user.Company.Molecule.AreaId)
            .ToListAsync();

        SelectedMolecule = await _db.Molecules.FindAsync(MoleculeId);

        AvailableJobTypes = await _db.JobTypes
            .Where(jt => jt.AreaId == user.Company.Molecule.AreaId)
            .OrderBy(jt => jt.SortOrder)
            .ToListAsync();

        JobTypeId ??= user.JobTypeId ?? AvailableJobTypes.FirstOrDefault()?.Id;
        SelectedJobType = AvailableJobTypes.FirstOrDefault(jt => jt.Id == JobTypeId);

        if (MoleculeId == null || JobTypeId == null)
            return RedirectToPage("/Index");

        // Check edit permission
        CanEdit = await _grantService.HasGrantAsync(userId, $"Assign{SelectedJobType?.Name ?? ""}Shifts", MoleculeId.Value);

        // Calculate date range
        var endDate = View switch
        {
            "2weeks" => Start.Value.AddDays(13),
            "month" => Start.Value.AddMonths(1).AddDays(-1),
            _ => Start.Value.AddDays(6)
        };

        // Build calendar data
        if (Mode == "user")
        {
            await BuildUserBasedCalendarAsync(MoleculeId.Value, JobTypeId.Value, Start.Value, endDate);
        }
        else
        {
            await BuildShiftBasedCalendarAsync(MoleculeId.Value, JobTypeId.Value, Start.Value, endDate);
        }

        return Page();
    }

    private async Task BuildShiftBasedCalendarAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end)
    {
        var shiftTypes = await _db.ShiftTypes
            .Include(st => st.ShiftGrouping)
            .Where(st => st.MoleculeId == moleculeId && st.JobTypeId == jobTypeId)
            .OrderBy(st => st.ShiftGrouping != null ? st.ShiftGrouping.SortOrder : 999)
            .ThenBy(st => st.Start)
            .ToListAsync();

        var assignments = await _calendarService.GetAssignmentsAsync(moleculeId, jobTypeId, start, end);
        var groupings = await _db.ShiftGroupings
            .Where(sg => sg.MoleculeId == moleculeId && (sg.JobTypeId == null || sg.JobTypeId == jobTypeId))
            .ToListAsync();

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = start,
            EndDate = end,
            ViewMode = View,
            IsReadOnly = !CanEdit,
            CalendarType = "shifts",
            Groups = groupings.Select(g => new ExcelCalendarGroup
            {
                Id = g.Id.ToString(),
                Name = g.DisplayName,
                SortOrder = g.SortOrder
            }).ToList(),
            Rows = shiftTypes.Select(st => new ExcelCalendarRow
            {
                Id = st.Id.ToString(),
                Label = st.DisplayName,
                Color = st.RowColor,
                GroupId = st.ShiftGroupingId?.ToString(),
                Cells = BuildCellsForShiftType(st, assignments, start, end)
            }).ToList()
        };
    }

    private async Task BuildUserBasedCalendarAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end)
    {
        var users = await _calendarService.GetUsersForCalendarAsync(moleculeId, jobTypeId);
        var assignments = await _calendarService.GetAssignmentsAsync(moleculeId, jobTypeId, start, end);
        var overlays = await _calendarService.GetOverlaysAsync(moleculeId, start, end);

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = start,
            EndDate = end,
            ViewMode = View,
            IsReadOnly = !CanEdit,
            CalendarType = "shifts",
            Rows = users.Select(u => new ExcelCalendarRow
            {
                Id = u.Id.ToString(),
                Label = u.DisplayName,
                Cells = BuildCellsForUser(u.Id, assignments, overlays, start, end)
            }).ToList()
        };
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForShiftType(
        ShiftType shiftType,
        List<ShiftAssignment> assignments,
        DateOnly start,
        DateOnly end)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var dayAssignments = assignments
                .Where(a => a.ShiftInstance.ShiftTypeId == shiftType.Id && a.ShiftInstance.WorkDate == date)
                .ToList();

            cells[date] = new ExcelCalendarCell
            {
                Assignments = dayAssignments.Select(a => new ExcelCalendarAssignment
                {
                    Id = a.Id,
                    Name = a.User?.DisplayName ?? "Unknown",
                    IsTrainee = a.IsTrainee
                }).ToList()
            };
        }

        return cells;
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForUser(
        int userId,
        List<ShiftAssignment> assignments,
        Dictionary<(int UserId, DateOnly Date), FyiOverlayData> overlays,
        DateOnly start,
        DateOnly end)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var userAssignments = assignments
                .Where(a => a.UserId == userId && a.ShiftInstance.WorkDate == date)
                .ToList();

            var overlay = overlays.GetValueOrDefault((userId, date));

            cells[date] = new ExcelCalendarCell
            {
                Assignments = userAssignments.Select(a => new ExcelCalendarAssignment
                {
                    Id = a.Id,
                    Name = a.ShiftInstance.ShiftType?.DisplayName ?? "Unknown",
                    IsTrainee = a.IsTrainee
                }).ToList(),
                Overlay = overlay != null ? new ExcelCalendarOverlay
                {
                    HasVacation = overlay.HasVacation,
                    HasChore = overlay.HasChore,
                    HasOnDuty = overlay.HasOnDuty,
                    OtherItems = overlay.OtherShifts
                } : null
            };
        }

        return cells;
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Sunday)) % 7;
        return date.AddDays(-diff);
    }
}
```

**Step 2: Create View**

```html
@* Pages/Calendar/Shifts.cshtml *@
@page
@model ShiftManager.Pages.Calendar.ShiftsModel
@using Microsoft.Extensions.Localization
@using ShiftManager.Resources
@inject IStringLocalizer<SharedResources> Localizer
@{
    Layout = "_Layout";
    ViewData["Title"] = Localizer["ShiftsCalendar"];
}

@await Component.InvokeAsync("Breadcrumb", new List<BreadcrumbItem>
{
    new BreadcrumbItem { Label = Localizer["Calendars"], Url = "/Calendar" },
    new BreadcrumbItem { Label = Localizer["ShiftsCalendar"], IsActive = true }
})

<div class="page-header">
    <h1 class="page-title">@Localizer["ShiftsCalendar"]</h1>
</div>

@* Toolbar *@
<div class="excel-calendar-toolbar">
    <div class="excel-calendar-toolbar__group">
        @* Molecule selector *@
        <select asp-for="MoleculeId"
                asp-items="@(new SelectList(Model.AvailableMolecules, "Id", "DisplayName"))"
                class="form-select form-select--compact"
                onchange="updateCalendarParams()">
        </select>

        @* JobType selector *@
        <select asp-for="JobTypeId"
                asp-items="@(new SelectList(Model.AvailableJobTypes, "Id", "DisplayName"))"
                class="form-select form-select--compact"
                onchange="updateCalendarParams()">
        </select>
    </div>

    <div class="excel-calendar-toolbar__separator"></div>

    <div class="excel-calendar-toolbar__group">
        @* View mode dropdown *@
        <select asp-for="View" class="form-select form-select--compact" onchange="updateCalendarParams()">
            <option value="week">@Localizer["Week"]</option>
            <option value="2weeks">@Localizer["TwoWeeks"]</option>
            <option value="month">@Localizer["Month"]</option>
        </select>

        @* Date range display + picker *@
        <span class="excel-calendar-toolbar__date-range">
            @Model.CalendarData.StartDate.ToString("dd/MM/yy") - @Model.CalendarData.EndDate.ToString("dd/MM/yy")
        </span>
        <input type="date" asp-for="Start" class="form-input form-input--compact" onchange="updateCalendarParams()" style="width: auto;" />

        @* Navigation arrows *@
        <button type="button" class="btn btn--icon" onclick="navigateDate(-1)" title="@Localizer["Previous"]">◀</button>
        <button type="button" class="btn btn--icon" onclick="navigateDate(1)" title="@Localizer["Next"]">▶</button>

        @* Print button *@
        <button type="button" class="btn btn--icon" onclick="window.print()" title="@Localizer["Print"]">🖨️</button>
    </div>

    <div class="excel-calendar-toolbar__separator"></div>

    <div class="excel-calendar-toolbar__group">
        @* View mode toggle *@
        <button type="button"
                class="btn btn--outline @(Model.Mode == "user" ? "btn--active" : "")"
                onclick="toggleMode()">
            👥 @(Model.Mode == "user" ? Localizer["ByUser"] : Localizer["ByShift"]) ⟳
        </button>

        @if (Model.CanEdit)
        {
            @* Capacity mode toggle *@
            <button type="button"
                    class="btn btn--outline"
                    onclick="toggleCapacityMode()">
                📊 @Localizer["CapacityMode"]
            </button>
        }
    </div>

    <div class="excel-calendar-toolbar__separator"></div>

    <div class="excel-calendar-toolbar__group">
        @* Filter button *@
        <button type="button" class="btn btn--outline" onclick="openFilterPanel()">
            🔍 @Localizer["Filter"]
        </button>

        @* Just mine toggle *@
        <button type="button" class="btn btn--outline" onclick="toggleJustMine()">
            👤 @Localizer["JustMine"]
        </button>
    </div>
</div>

@* Calendar table *@
@await Component.InvokeAsync("ExcelCalendarTable", Model.CalendarData)

@section Scripts {
<script>
    function updateCalendarParams() {
        const params = new URLSearchParams(window.location.search);
        params.set('moleculeId', document.querySelector('[name="MoleculeId"]').value);
        params.set('jobTypeId', document.querySelector('[name="JobTypeId"]').value);
        params.set('view', document.querySelector('[name="View"]').value);
        params.set('start', document.querySelector('[name="Start"]').value);
        params.set('mode', '@Model.Mode');
        window.location.search = params.toString();
    }

    function navigateDate(direction) {
        const viewDays = { 'week': 7, '2weeks': 14, 'month': 30 };
        const days = viewDays['@Model.View'] || 7;
        const currentStart = new Date('@Model.CalendarData.StartDate.ToString("yyyy-MM-dd")');
        currentStart.setDate(currentStart.getDate() + (direction * days));
        document.querySelector('[name="Start"]').value = currentStart.toISOString().split('T')[0];
        updateCalendarParams();
    }

    function toggleMode() {
        const params = new URLSearchParams(window.location.search);
        params.set('mode', '@Model.Mode' === 'shift' ? 'user' : 'shift');
        window.location.search = params.toString();
    }

    function toggleCapacityMode() {
        // TODO: Implement capacity mode toggle
        alert('Capacity mode coming soon');
    }

    function openFilterPanel() {
        // TODO: Implement filter panel
        alert('Filter panel coming soon');
    }

    function toggleJustMine() {
        // TODO: Implement just mine toggle
        alert('Just mine filter coming soon');
    }
</script>
}
```

**Step 3: Commit**

```bash
git add Pages/Calendar/Shifts.cshtml Pages/Calendar/Shifts.cshtml.cs
git commit -m "feat: add Shifts Calendar page with shift/user view modes"
```

---

## Phase 5-8: Additional Calendars & Features

*Tasks for Chores Calendar, On-Call Calendar, Overview Calendar, Landing Page, SignalR Hub, and Redirects follow the same pattern as Phase 4. Each task includes:*

1. **Model/Service updates** (if needed)
2. **Page creation** (PageModel + View)
3. **Localization keys**
4. **Unit tests**
5. **Commit**

*These are outlined but not fully detailed to keep the plan manageable. The implementing engineer should follow the patterns established in Phases 1-4.*

---

### Task 5.1: Create Chores Calendar Page
- Route: `/Calendar/Chores`
- Scope: Molecule
- View: By people (primary)
- Files: `Pages/Calendar/Chores.cshtml[.cs]`

### Task 5.2: Create On-Call Calendar Page
- Route: `/Calendar/OnCall`
- Scope: Area
- View: By duty type with primary + backup
- Files: `Pages/Calendar/OnCall.cshtml[.cs]`

### Task 5.3: Create Overview Calendar Page
- Route: `/Calendar/Overview`
- Scope: Company
- View: By people, view-only with notes
- Files: `Pages/Calendar/Overview.cshtml[.cs]`

### Task 6.1: Create Calendar Landing Page
- Route: `/Calendar`
- Beautiful cards with gradients and illustrations
- Files: `Pages/Calendar/Index.cshtml[.cs]`, `wwwroot/images/calendar-*.svg`

### Task 7.1: Create CalendarHub for SignalR
- Files: `Hubs/CalendarHub.cs`
- Events: AssignmentChanged, CapacityChanged, NoteChanged
- Register in Program.cs

### Task 7.2: Add SignalR Client Script
- Files: `wwwroot/js/calendar-realtime.js`
- Connect to hub, handle updates

### Task 8.1: Add Feature Flags
- Add to `appsettings.json`: ExcelCalendars, ExcelCalendarShifts, etc.
- Check flags in pages

### Task 8.2: Add Redirects
- In `Program.cs`: Map old routes to new
- `/Calendar/Month` → `/Calendar/Shifts`
- `/Calendar/Table` → `/Calendar/Shifts`
- etc.

### Task 8.3: Add Localization Keys
- All Hebrew and English keys for calendar UI
- Files: `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx`

---

## Phase 9: Testing

### Task 9.1: Unit Tests for ShiftCalendarService

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Services/ShiftCalendarServiceTests.cs`

Test cases:
- `GetUsersForCalendarAsync_ReturnsCrossCompanyUsers`
- `GetCapacityAsync_ReturnsOverrideWhenExists`
- `GetCapacityAsync_ReturnsDefaultWhenNoOverride`
- `CheckRestViolationsAsync_DetectsViolation`
- `CheckRestViolationsAsync_NoViolationWhenSufficientRest`

### Task 9.2: Integration Tests for Calendar Pages

**Files:**
- Create: `ShiftManager.Tests/IntegrationTests/CalendarPagesTests.cs`

Test cases:
- `ShiftsCalendar_LoadsForAuthorizedUser`
- `ShiftsCalendar_RedirectsUnauthorizedUser`
- `ChoresCalendar_ShowsCrossCompanyData`

---

## Verification Checklist

Before considering implementation complete:

- [ ] All 4 calendar pages render correctly
- [ ] Landing page displays with premium card design
- [ ] Cell editor popover opens on click (RTL-aware)
- [ ] Shift/User view toggle works
- [ ] Date range navigation works
- [ ] Capacity mode allows editing (with permissions)
- [ ] Auto-unassign dialog shows when reducing capacity
- [ ] Rest violation warnings display but don't block
- [ ] FYI badges show vacation/chore/on-duty
- [ ] SignalR real-time updates work
- [ ] Read-only mode shows for users without edit grant
- [ ] All Hebrew localization keys present
- [ ] Feature flags control page visibility
- [ ] Old routes redirect to new pages
- [ ] All unit tests pass
- [ ] All integration tests pass

---

## Notes for Implementer

1. **Grant-Based Permissions:** Always use `_grantService.HasGrantAsync()`, never check roles directly
2. **Cross-Company Queries:** Use `IgnoreQueryFilters()` when querying across companies in a molecule
3. **RTL Support:** Test popover positioning in RTL mode, use logical properties (inline-start/end)
4. **Localization:** All user-facing text must use `<loc>` tags or `Localizer[]`
5. **Error Handling:** Use toast for network errors, inline for validation errors
6. **Accessibility:** Add ARIA labels, ensure keyboard navigation works
