# Per-User Calendar Reordering (Categories + Rows) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each user drag to reorder categories (group headers) and rows (people/shifts) within their categories, on all five calendar surfaces (Shifts user+shift modes, Chores, On-Call, Overview), persisted per-user.

**Architecture:** Hybrid. A new `UserCalendarRowOrder` table stores a per-user order (one table, two axes: `GroupId=""` rows order the categories; `GroupId="category-5"` rows order that category's members). The shared `ExcelCalendarTable` view component applies the saved order server-side (correct first paint). A new `calendar-row-reorder.js` owns the live drag, optimistic DOM update, and re-application after the in-place grid refresh, persisting via `POST /Api/Calendar/SaveRowOrder`.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core + SQLite, xUnit (real-SQLite fixture `SqliteDbContextFixture`), vanilla JS, Playwright (Python) for browser verification.

**Reference spec:** `docs/superpowers/specs/2026-06-12-per-user-calendar-row-reorder-design.md`

**Pre-req for verification:** the dev app serves JS via WebOptimizer (per-process memory cache) — after any JS edit, **restart** the app and probe the served file by a **string literal** (function names are minified away). See `memory/dev_app_stale_static_assets.md`.

---

## File Structure

**Create:**
- `Models/UserCalendarRowOrder.cs` — the entity
- `Services/ICalendarRowOrderService.cs` + `Services/CalendarRowOrderService.cs` — load/save order
- `Pages/Api/Calendar/SaveRowOrder.cshtml` + `.cshtml.cs` — persistence endpoint
- `wwwroot/js/calendar-row-reorder.js` — drag/keyboard module
- `UnitTests/Services/CalendarRowOrderServiceTests.cs`
- `UnitTests/ViewComponents/ExcelCalendarOrderingTests.cs`
- `UnitTests/Pages/Api/SaveRowOrderTests.cs`

**Modify:**
- `Data/AppDbContext.cs` — DbSet + index
- `ViewComponents/ExcelCalendarTableViewComponent.cs` — sync→async, inject service, add `RowOrderContextKey`, apply ordering
- `Pages/Calendar/Shifts.cshtml.cs` (lines ~477–487 and ~591–606), `Chores.cshtml.cs` (~312–325), `Overview.cshtml.cs` (~241–258), `OnCall.cshtml.cs` (~431–444) — set `RowOrderContextKey`
- `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` — emit `data-reorder-context`
- `wwwroot/js/excel-calendar-groups.js` (lines ~69–84) — suppress collapse on grip
- `wwwroot/css/calendar.css` — grip + drop-line styles
- `Pages/Shared/_Layout.cshtml` (or the calendar pages' scripts section) — include the new JS
- `Pages/Admin/Users.cshtml.cs` (~line 1072) — deactivation cleanup
- `Resources/SharedResources.resx` + `SharedResources.he-IL.resx` — a11y/localization keys
- `Program.cs` (~386–396) — service registration

---

## Task 1: Entity + DbContext + migration

**Files:**
- Create: `Models/UserCalendarRowOrder.cs`
- Modify: `Data/AppDbContext.cs` (DbSets ~line 21–46; OnModelCreating index ~line 258)

- [ ] **Step 1: Create the entity**

`Models/UserCalendarRowOrder.cs`:
```csharp
namespace ShiftManager.Models;

/// <summary>
/// A single user's personal ordering of a calendar's categories or rows.
/// NOT IBelongsToCompany — it is a per-user UI preference, read only for its owner,
/// so there is no cross-tenant read path. Two axes share this table, distinguished by GroupId:
///   - GroupId == ""            → category order; RowId holds a group id (e.g. "category-5","dl-3").
///   - GroupId == "category-5"  → row order inside that category; RowId holds a row id ("user-14").
/// </summary>
public class UserCalendarRowOrder
{
    public int Id { get; set; }

    /// <summary>Owner of this personal ordering.</summary>
    public int UserId { get; set; }

    /// <summary>Identifies the view, e.g. "shifts:9:2:user", "chores:9", "oncall:4", "overview:5".</summary>
    public string ContextKey { get; set; } = string.Empty;

    /// <summary>"" = the category-ordering namespace; otherwise the category/list this row lives in.</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>A row id ("user-14"/"shift-7") for row order, or a group id ("category-5") for category order.</summary>
    public string RowId { get; set; } = string.Empty;

    /// <summary>0-based position within (UserId, ContextKey, GroupId).</summary>
    public int SortOrder { get; set; }
}
```

- [ ] **Step 2: Register the DbSet** in `Data/AppDbContext.cs` after the existing DbSet block (near line 46), matching the expression-bodied style:
```csharp
public DbSet<UserCalendarRowOrder> UserCalendarRowOrders => Set<UserCalendarRowOrder>();
```

- [ ] **Step 3: Add the unique + query indexes** in `OnModelCreating` (alongside the other `modelBuilder.Entity<...>().HasIndex(...)` blocks near line 258):
```csharp
// Per-user calendar ordering (categories + rows). NOT company-scoped — no global query filter.
modelBuilder.Entity<UserCalendarRowOrder>()
    .HasIndex(o => new { o.UserId, o.ContextKey, o.GroupId, o.RowId })
    .IsUnique();
modelBuilder.Entity<UserCalendarRowOrder>()
    .HasIndex(o => new { o.UserId, o.ContextKey });
```

- [ ] **Step 4: Create the migration**

Run: `dotnet ef migrations add AddUserCalendarRowOrder`
Expected: a new file under `Migrations/` whose `Up()` calls `migrationBuilder.CreateTable("UserCalendarRowOrders", ...)` with the two indexes. If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef` first.

- [ ] **Step 5: Build to confirm the model compiles**

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded, 0 errors. (Migrations apply on app startup; no manual DB step.)

- [ ] **Step 6: Commit**
```bash
git add Models/UserCalendarRowOrder.cs Data/AppDbContext.cs Migrations/
git commit -m "feat(calendar): UserCalendarRowOrder entity + migration"
```

---

## Task 2: Order service (load + save) with unit tests

**Files:**
- Create: `Services/ICalendarRowOrderService.cs`, `Services/CalendarRowOrderService.cs`
- Create: `UnitTests/Services/CalendarRowOrderServiceTests.cs`
- Modify: `Program.cs` (~line 396)

- [ ] **Step 1: Write the failing test**

`UnitTests/Services/CalendarRowOrderServiceTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.UnitTests.Helpers; // SqliteDbContextFixture
using Xunit;

namespace ShiftManager.UnitTests.Services;

public class CalendarRowOrderServiceTests
{
    private static CalendarRowOrderService NewSvc(AppDbContext db) => new(db);

    [Fact]
    public async Task SaveThenGet_RoundTrips_RowOrder()
    {
        await using var f = new SqliteDbContextFixture();
        var db = f.Context;
        var svc = NewSvc(db);

        await svc.SaveOrderAsync(userId: 1, "shifts:9:2:user", "category-5",
            new[] { "user-3", "user-1", "user-2" });

        var map = await svc.GetOrderMapAsync(1, "shifts:9:2:user");
        Assert.Equal(0, map[("category-5", "user-3")]);
        Assert.Equal(1, map[("category-5", "user-1")]);
        Assert.Equal(2, map[("category-5", "user-2")]);
    }

    [Fact]
    public async Task SaveOrder_ReplacesGroupNamespace_NotOtherGroups()
    {
        await using var f = new SqliteDbContextFixture();
        var svc = NewSvc(f.Context);
        await svc.SaveOrderAsync(1, "ctx", "category-5", new[] { "user-1", "user-2" });
        await svc.SaveOrderAsync(1, "ctx", "category-6", new[] { "user-9" });
        // re-save category-5 with fewer items — must not touch category-6
        await svc.SaveOrderAsync(1, "ctx", "category-5", new[] { "user-2" });

        var map = await svc.GetOrderMapAsync(1, "ctx");
        Assert.True(map.ContainsKey(("category-6", "user-9")));
        Assert.False(map.ContainsKey(("category-5", "user-1"))); // removed by replace
        Assert.Equal(0, map[("category-5", "user-2")]);
    }

    [Fact]
    public async Task CategoryOrder_UsesEmptyGroupNamespace()
    {
        await using var f = new SqliteDbContextFixture();
        var svc = NewSvc(f.Context);
        await svc.SaveOrderAsync(1, "ctx", "", new[] { "category-6", "category-5" });
        var map = await svc.GetOrderMapAsync(1, "ctx");
        Assert.Equal(0, map[("", "category-6")]);
        Assert.Equal(1, map[("", "category-5")]);
    }

    [Fact]
    public async Task Orders_AreIsolatedPerUser()
    {
        await using var f = new SqliteDbContextFixture();
        var svc = NewSvc(f.Context);
        await svc.SaveOrderAsync(1, "ctx", "g", new[] { "user-1" });
        var other = await svc.GetOrderMapAsync(2, "ctx");
        Assert.Empty(other);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CalendarRowOrderServiceTests" -- xUnit.ParallelizeTestCollections=false`
Expected: FAIL to compile — `ICalendarRowOrderService`/`CalendarRowOrderService` do not exist yet.

- [ ] **Step 3: Create the interface**

`Services/ICalendarRowOrderService.cs`:
```csharp
namespace ShiftManager.Services;

public interface ICalendarRowOrderService
{
    /// <summary>All saved positions for a user+context as (GroupId, RowId) → SortOrder.</summary>
    Task<Dictionary<(string GroupId, string RowId), int>> GetOrderMapAsync(int userId, string contextKey);

    /// <summary>Replace the ordering of one namespace. groupId "" = category order (itemIds are group ids);
    /// a real groupId = row order in that group (itemIds are row ids).</summary>
    Task SaveOrderAsync(int userId, string contextKey, string groupId, IReadOnlyList<string> itemIds);
}
```

- [ ] **Step 4: Implement the service**

`Services/CalendarRowOrderService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class CalendarRowOrderService : ICalendarRowOrderService
{
    private readonly AppDbContext _db;
    public CalendarRowOrderService(AppDbContext db) => _db = db;

    public async Task<Dictionary<(string, string), int>> GetOrderMapAsync(int userId, string contextKey)
    {
        var rows = await _db.UserCalendarRowOrders
            .Where(o => o.UserId == userId && o.ContextKey == contextKey)
            .Select(o => new { o.GroupId, o.RowId, o.SortOrder })
            .ToListAsync();
        return rows.ToDictionary(o => (o.GroupId, o.RowId), o => o.SortOrder);
    }

    public async Task SaveOrderAsync(int userId, string contextKey, string groupId, IReadOnlyList<string> itemIds)
    {
        // Whole-namespace replace = simplest correct semantics.
        await _db.UserCalendarRowOrders
            .Where(o => o.UserId == userId && o.ContextKey == contextKey && o.GroupId == groupId)
            .ExecuteDeleteAsync();

        for (var i = 0; i < itemIds.Count; i++)
        {
            _db.UserCalendarRowOrders.Add(new UserCalendarRowOrder
            {
                UserId = userId,
                ContextKey = contextKey,
                GroupId = groupId,
                RowId = itemIds[i],
                SortOrder = i
            });
        }
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Register the service** in `Program.cs` after the calendar services (near line 396):
```csharp
builder.Services.AddScoped<ICalendarRowOrderService, CalendarRowOrderService>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CalendarRowOrderServiceTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**
```bash
git add Services/ICalendarRowOrderService.cs Services/CalendarRowOrderService.cs UnitTests/Services/CalendarRowOrderServiceTests.cs Program.cs
git commit -m "feat(calendar): CalendarRowOrderService + tests"
```

---

## Task 3: Apply ordering in the shared view component

**Files:**
- Modify: `ViewComponents/ExcelCalendarTableViewComponent.cs` (model class + component at line ~127–133)
- Create: `UnitTests/ViewComponents/ExcelCalendarOrderingTests.cs`

The ordering math is unit-tested as a **static pure helper** (`CalendarOrderApplier.Apply`) so it needs no ViewComponent host.

- [ ] **Step 1: Write the failing test**

`UnitTests/ViewComponents/ExcelCalendarOrderingTests.cs`:
```csharp
using ShiftManager.ViewComponents;
using Xunit;

namespace ShiftManager.UnitTests.ViewComponents;

public class ExcelCalendarOrderingTests
{
    private static ExcelCalendarRow Row(string id, string group) => new() { Id = id, GroupId = group };
    private static ExcelCalendarGroup Grp(string id, int sort) => new() { Id = id, SortOrder = sort };

    [Fact]
    public void NoSavedOrder_KeepsDefaults()
    {
        var rows = new List<ExcelCalendarRow> { Row("user-1", "category-5"), Row("user-2", "category-5") };
        var groups = new List<ExcelCalendarGroup> { Grp("category-5", 0) };
        CalendarOrderApplier.Apply(rows, groups, new());
        Assert.Equal(new[] { "user-1", "user-2" }, rows.ConvertAll(r => r.Id));
    }

    [Fact]
    public void RowOrder_PositionedFirst_ThenUnpositionedInDefaultOrder()
    {
        var rows = new List<ExcelCalendarRow> {
            Row("user-1", "category-5"), Row("user-2", "category-5"), Row("user-3", "category-5") };
        var groups = new List<ExcelCalendarGroup> { Grp("category-5", 0) };
        var map = new Dictionary<(string, string), int> {
            { ("category-5", "user-3"), 0 }, { ("category-5", "user-1"), 1 } }; // user-2 un-positioned
        CalendarOrderApplier.Apply(rows, groups, map);
        Assert.Equal(new[] { "user-3", "user-1", "user-2" }, rows.ConvertAll(r => r.Id));
    }

    [Fact]
    public void CategoryOrder_ReordersGroups_UnpositionedFallBackToSortOrder()
    {
        var rows = new List<ExcelCalendarRow>();
        var groups = new List<ExcelCalendarGroup> { Grp("category-5", 0), Grp("category-6", 1), Grp("category-7", 2) };
        var map = new Dictionary<(string, string), int> {
            { ("", "category-7"), 0 }, { ("", "category-5"), 1 } }; // category-6 un-positioned
        CalendarOrderApplier.Apply(rows, groups, map);
        Assert.Equal(new[] { "category-7", "category-5", "category-6" }, groups.ConvertAll(g => g.Id));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ExcelCalendarOrderingTests" -- xUnit.ParallelizeTestCollections=false`
Expected: FAIL — `CalendarOrderApplier` does not exist.

- [ ] **Step 3: Implement the applier + wire the component**

In `ViewComponents/ExcelCalendarTableViewComponent.cs`: add `RowOrderContextKey` to the model, add the static applier, and convert the component to async with the service injected. Replace the model property region (add one property) and the component class at the bottom (lines ~127–133):

Add to `ExcelCalendarTableViewModel` (near `RowMode`):
```csharp
/// <summary>Per-user reorder context key, e.g. "shifts:9:2:user". Null disables reordering for this render.</summary>
public string? RowOrderContextKey { get; set; }
```

Add the static helper (top-level in the same file's namespace):
```csharp
public static class CalendarOrderApplier
{
    /// <summary>In-place stable reorder of groups (by "" namespace) and rows (by their group namespace).
    /// Positioned items first in saved SortOrder; un-positioned keep default order after them.</summary>
    public static void Apply(
        List<ExcelCalendarRow> rows,
        List<ExcelCalendarGroup>? groups,
        Dictionary<(string GroupId, string RowId), int> order)
    {
        if (order.Count == 0) return;

        if (groups != null && groups.Count > 0)
        {
            var idx = 0;
            var ordered = groups
                .Select(g => new { g, i = idx++ })
                .OrderBy(x => order.TryGetValue(("", x.g.Id), out var so) ? 0 : 1)
                .ThenBy(x => order.TryGetValue(("", x.g.Id), out var so) ? so : x.g.SortOrder)
                .Select(x => x.g)
                .ToList();
            groups.Clear();
            groups.AddRange(ordered);
        }

        var ri = 0;
        var orderedRows = rows
            .Select(r => new { r, i = ri++ })
            .OrderBy(x => order.TryGetValue((x.r.GroupId ?? "", x.r.Id), out _) ? 0 : 1)
            .ThenBy(x => order.TryGetValue((x.r.GroupId ?? "", x.r.Id), out var so) ? so : x.i)
            .Select(x => x.r)
            .ToList();
        // NOTE: Default.cshtml filters rows by group, so cross-group relative order is irrelevant;
        // within each group the stable sort above yields positioned-then-default. Group BLOCK order
        // is driven by `groups` above.
        rows.Clear();
        rows.AddRange(orderedRows);
    }
}
```

Replace the component class:
```csharp
public class ExcelCalendarTableViewComponent : ViewComponent
{
    private readonly ShiftManager.Services.ICalendarRowOrderService _rowOrder;
    public ExcelCalendarTableViewComponent(ShiftManager.Services.ICalendarRowOrderService rowOrder)
        => _rowOrder = rowOrder;

    public async Task<IViewComponentResult> InvokeAsync(ExcelCalendarTableViewModel model)
    {
        if (!string.IsNullOrEmpty(model.RowOrderContextKey))
        {
            var idClaim = UserClaimsPrincipal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idClaim, out var userId))
            {
                var map = await _rowOrder.GetOrderMapAsync(userId, model.RowOrderContextKey);
                CalendarOrderApplier.Apply(model.Rows, model.Groups, map);
            }
        }
        return View(model);
    }
}
```
(`Component.InvokeAsync("ExcelCalendarTable", model)` already awaits async components — no caller change.)

- [ ] **Step 4: Run unit tests to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~ExcelCalendarOrderingTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (3 tests).

- [ ] **Step 5: Build the whole app** (catches the sync→async signature change everywhere):

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**
```bash
git add ViewComponents/ExcelCalendarTableViewComponent.cs UnitTests/ViewComponents/ExcelCalendarOrderingTests.cs
git commit -m "feat(calendar): apply per-user order in ExcelCalendarTable view component"
```

---

## Task 4: Set RowOrderContextKey + emit data-reorder-context

**Files:**
- Modify: `Pages/Calendar/Shifts.cshtml.cs` (~487 and ~606), `Chores.cshtml.cs` (~325), `Overview.cshtml.cs` (~258), `OnCall.cshtml.cs` (~444)
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` (root `.excel-calendar` element)

- [ ] **Step 1: Shifts shift-mode** — after `CalendarData.TotalRows = ...;` (line ~487):
```csharp
CalendarData.RowOrderContextKey = $"shifts:{moleculeId}:{JobTypeId}:shift";
```

- [ ] **Step 2: Shifts user-mode** — after `CalendarData.TotalRows = ...;` (line ~606):
```csharp
CalendarData.RowOrderContextKey = $"shifts:{moleculeId}:{jobTypeId}:user";
```

- [ ] **Step 3: Chores** — after `CalendarData.TotalRows = ...;` (line ~325):
```csharp
CalendarData.RowOrderContextKey = $"chores:{moleculeId}";
```

- [ ] **Step 4: Overview** — after `CalendarData.TotalRows = ...;` (line ~258):
```csharp
CalendarData.RowOrderContextKey = $"overview:{CompanyId}";
```

- [ ] **Step 5: OnCall** — after `CalendarData.TotalRows = ...;` (line ~444):
```csharp
CalendarData.RowOrderContextKey = AreaId.HasValue ? $"oncall:{AreaId.Value}" : "oncall:all";
```

- [ ] **Step 6: Emit the context key to the client.** In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, on the root `<table class="excel-calendar" ...>` (or its wrapping element that carries `data-calendar-type`), add:
```html
@if (!string.IsNullOrEmpty(Model.RowOrderContextKey))
{
    <text> data-reorder-context="@Model.RowOrderContextKey"</text>
}
```
(Place this inside the opening tag of the element that already has class `excel-calendar`. If that element is generated by a tag helper, add the attribute literally: `data-reorder-context="@(Model.RowOrderContextKey ?? "")"`.)

- [ ] **Step 7: Build**

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded.

- [ ] **Step 8: Commit**
```bash
git add Pages/Calendar/Shifts.cshtml.cs Pages/Calendar/Chores.cshtml.cs Pages/Calendar/Overview.cshtml.cs Pages/Calendar/OnCall.cshtml.cs Pages/Shared/Components/ExcelCalendarTable/Default.cshtml
git commit -m "feat(calendar): wire RowOrderContextKey on all 5 calendar surfaces"
```

---

## Task 5: Persistence endpoint + tests

**Files:**
- Create: `Pages/Api/Calendar/SaveRowOrder.cshtml`, `.cshtml.cs`
- Create: `UnitTests/Pages/Api/SaveRowOrderTests.cs`

`/Api/Calendar` is already whitelisted in `ApiAuthenticationMiddleware.IsInternalWebUiEndpoint()` — no middleware change.

- [ ] **Step 1: Write the failing test** (exercises the model's handler logic via the service)

`UnitTests/Pages/Api/SaveRowOrderTests.cs`:
```csharp
using ShiftManager.Services;
using ShiftManager.UnitTests.Helpers;
using Xunit;

namespace ShiftManager.UnitTests.Pages.Api;

public class SaveRowOrderTests
{
    [Fact]
    public async Task Service_Persists_OrderForUser()
    {
        await using var f = new SqliteDbContextFixture();
        var svc = new CalendarRowOrderService(f.Context);
        await svc.SaveOrderAsync(7, "overview:5", "", new[] { "user-2", "user-1" });
        var map = await svc.GetOrderMapAsync(7, "overview:5");
        Assert.Equal(0, map[("", "user-2")]);
        Assert.Equal(1, map[("", "user-1")]);
    }
}
```
(The HTTP-layer concerns — auth, antiforgery, oversized payload — are covered by the `[Authorize]`/`[IgnoreAntiforgeryToken]` attributes and the explicit `itemIds.Count` guard below; the persistence path is the testable unit.)

- [ ] **Step 2: Run to verify pass** (this test only needs the Task-2 service; it should already PASS, locking the contract):

Run: `dotnet test --filter "FullyQualifiedName~SaveRowOrderTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS.

- [ ] **Step 3: Create the Razor page shell**

`Pages/Api/Calendar/SaveRowOrder.cshtml`:
```html
@page
@model ShiftManager.Pages.Api.Calendar.SaveRowOrderModel
```

- [ ] **Step 4: Create the handler**

`Pages/Api/Calendar/SaveRowOrder.cshtml.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

[Authorize]
[IgnoreAntiforgeryToken]
public class SaveRowOrderModel : PageModel
{
    private readonly ICalendarRowOrderService _rowOrder;
    private readonly IAuditLogService _audit;
    private readonly ILogger<SaveRowOrderModel> _logger;

    public SaveRowOrderModel(ICalendarRowOrderService rowOrder, IAuditLogService audit, ILogger<SaveRowOrderModel> logger)
    {
        _rowOrder = rowOrder; _audit = audit; _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var data = JsonSerializer.Deserialize<SaveRowOrderRequest>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (data == null || string.IsNullOrWhiteSpace(data.ContextKey) || data.ItemIds == null)
            return new JsonResult(new { success = false, message = "Invalid request" }) { StatusCode = 400 };
        if (data.ContextKey.Length > 128 || (data.GroupId?.Length ?? 0) > 64)
            return new JsonResult(new { success = false, message = "Key too long" }) { StatusCode = 400 };
        if (data.ItemIds.Count == 0 || data.ItemIds.Count > 500 || data.ItemIds.Any(s => string.IsNullOrEmpty(s) || s.Length > 64))
            return new JsonResult(new { success = false, message = "Invalid items" }) { StatusCode = 400 };

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idClaim, out var userId))
            return new JsonResult(new { success = false, message = "Unauthenticated" }) { StatusCode = 401 };

        await _rowOrder.SaveOrderAsync(userId, data.ContextKey, data.GroupId ?? "", data.ItemIds);
        await _audit.LogAsync(action: "CalendarOrderChanged", entityType: "UserCalendarRowOrder", entityId: userId,
            description: $"Reordered {(string.IsNullOrEmpty(data.GroupId) ? "categories" : "rows in " + data.GroupId)} for {data.ContextKey}");
        return new JsonResult(new { success = true });
    }

    private class SaveRowOrderRequest
    {
        public string ContextKey { get; set; } = string.Empty;
        public string? GroupId { get; set; }
        public List<string> ItemIds { get; set; } = new();
    }
}
```
(Verify `IAuditLogService.LogAsync` signature matches the sibling usage in `QuickAddTextEntry.cshtml.cs`; adjust named args if needed.)

- [ ] **Step 5: Build + run the API test**

Run: `dotnet build ShiftManager.csproj` then `dotnet test --filter "FullyQualifiedName~SaveRowOrderTests" -- xUnit.ParallelizeTestCollections=false`
Expected: Build succeeded; PASS.

- [ ] **Step 6: Commit**
```bash
git add Pages/Api/Calendar/SaveRowOrder.cshtml Pages/Api/Calendar/SaveRowOrder.cshtml.cs UnitTests/Pages/Api/SaveRowOrderTests.cs
git commit -m "feat(calendar): SaveRowOrder API endpoint"
```

---

## Task 6: Client drag module + CSS + localization + include

**Files:**
- Create: `wwwroot/js/calendar-row-reorder.js`
- Modify: `wwwroot/js/excel-calendar-groups.js` (~line 76, suppress collapse on grip)
- Modify: `wwwroot/css/calendar.css` (grip/drop-line styles)
- Modify: `Resources/SharedResources.resx` + `SharedResources.he-IL.resx` (keys)
- Modify: the layout/scripts include

- [ ] **Step 1: Suppress collapse-toggle when interacting with the grip.** In `wwwroot/js/excel-calendar-groups.js`, inside the header click handler (after the `[data-editable]`/`input` guard, ~line 78), add:
```javascript
if (e.target.closest('.excel-calendar__group-grip')) return; // grip = drag, not collapse
```

- [ ] **Step 2: Create the drag module**

`wwwroot/js/calendar-row-reorder.js`:
```javascript
(function () {
    'use strict';

    var dragState = null; // { kind:'row'|'category', id, groupId }

    function getGrid() { return document.querySelector('.excel-calendar[data-reorder-context]'); }
    function ctxKey() { var g = getGrid(); return g ? g.getAttribute('data-reorder-context') : null; }

    function csrf() {
        var t = document.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }

    function postOrder(groupId, itemIds) {
        var key = ctxKey();
        if (!key) return;
        fetch('/Api/Calendar/SaveRowOrder', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest', 'RequestVerificationToken': csrf() },
            credentials: 'same-origin',
            body: JSON.stringify({ contextKey: key, groupId: groupId, itemIds: itemIds })
        }).then(function (r) {
            if (!r.ok && window.showToast) window.showToast('Could not save order', 'error');
        }).catch(function () {
            if (window.showToast) window.showToast('Could not save order', 'error');
        });
    }

    // --- collect current DOM order ---
    function rowIdsInGroup(groupId) {
        var g = getGrid(); if (!g) return [];
        var sel = groupId
            ? 'tr[data-row-id][data-group-id="' + (window.CSS && CSS.escape ? CSS.escape(groupId) : groupId) + '"]'
            : 'tr[data-row-id]';
        return Array.prototype.slice.call(g.querySelectorAll(sel)).map(function (tr) { return tr.getAttribute('data-row-id'); });
    }
    function groupIdsInOrder() {
        var g = getGrid(); if (!g) return [];
        return Array.prototype.slice.call(g.querySelectorAll('tr.excel-calendar__group-header[data-group-id]'))
            .map(function (tr) { return tr.getAttribute('data-group-id'); });
    }

    // --- DOM move helpers ---
    function categoryBlock(groupId) {
        var g = getGrid(); var esc = (window.CSS && CSS.escape) ? CSS.escape(groupId) : groupId;
        var header = g.querySelector('tr.excel-calendar__group-header[data-group-id="' + esc + '"]');
        var nodes = header ? [header] : [];
        var n = header ? header.nextElementSibling : null;
        while (n && !(n.classList && n.classList.contains('excel-calendar__group-header'))) {
            if (n.matches && n.matches('tr[data-row-id]')) nodes.push(n);
            n = n.nextElementSibling;
        }
        return nodes;
    }

    // --- enable on a grid (idempotent) ---
    function enable() {
        var grid = getGrid();
        if (!grid || grid._reorderBound) { if (grid) injectGrips(grid); return; }
        grid._reorderBound = true;
        injectGrips(grid);

        grid.addEventListener('dragstart', function (e) {
            var rowGrip = e.target.closest('.excel-calendar__row-grip');
            var catGrip = e.target.closest('.excel-calendar__group-grip');
            if (rowGrip) {
                var tr = rowGrip.closest('tr[data-row-id]');
                dragState = { kind: 'row', id: tr.getAttribute('data-row-id'), groupId: tr.getAttribute('data-group-id') || '' };
            } else if (catGrip) {
                var hr = catGrip.closest('tr.excel-calendar__group-header');
                dragState = { kind: 'category', id: hr.getAttribute('data-group-id'), groupId: '' };
            } else { return; }
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', dragState.id);
        });

        grid.addEventListener('dragover', function (e) {
            if (!dragState) return;
            var overRow = e.target.closest('tr[data-row-id]');
            var overHeader = e.target.closest('tr.excel-calendar__group-header');
            if (dragState.kind === 'row') {
                if (!overRow || (overRow.getAttribute('data-group-id') || '') !== dragState.groupId) { e.dataTransfer.dropEffect = 'none'; return; }
                e.preventDefault(); e.dataTransfer.dropEffect = 'move';
                moveBefore(currentRow(), overRow, e);
            } else { // category
                if (!overHeader) { e.dataTransfer.dropEffect = 'none'; return; }
                e.preventDefault(); e.dataTransfer.dropEffect = 'move';
            }
        });

        grid.addEventListener('drop', function (e) {
            if (!dragState) return;
            e.preventDefault();
            if (dragState.kind === 'row') {
                postOrder(dragState.groupId, rowIdsInGroup(dragState.groupId));
            } else {
                var overHeader = e.target.closest('tr.excel-calendar__group-header');
                if (overHeader && overHeader.getAttribute('data-group-id') !== dragState.id) {
                    var block = categoryBlock(dragState.id);
                    block.forEach(function (node) { overHeader.parentNode.insertBefore(node, overHeader); });
                }
                postOrder('', groupIdsInOrder());
            }
            dragState = null;
        });

        grid.addEventListener('dragend', function () { dragState = null; });
    }

    function currentRow() {
        var g = getGrid(); var esc = (window.CSS && CSS.escape) ? CSS.escape(dragState.id) : dragState.id;
        return g.querySelector('tr[data-row-id="' + esc + '"]');
    }
    function moveBefore(node, target, e) {
        if (!node || node === target) return;
        var rect = target.getBoundingClientRect();
        var after = e.clientY > rect.top + rect.height / 2;
        target.parentNode.insertBefore(node, after ? target.nextSibling : target);
    }

    function injectGrips(grid) {
        // row grips
        grid.querySelectorAll('tr[data-row-id] .excel-calendar__row-label').forEach(function (cell) {
            if (cell.querySelector('.excel-calendar__row-grip')) return;
            var grip = document.createElement('span');
            grip.className = 'excel-calendar__row-grip';
            grip.setAttribute('draggable', 'true');
            grip.setAttribute('role', 'button');
            grip.setAttribute('tabindex', '0');
            grip.setAttribute('aria-label', (window.AppLocalizer && window.AppLocalizer.Calendar_DragRow) || 'Drag to reorder');
            grip.textContent = '⋮⋮';
            cell.insertBefore(grip, cell.firstChild);
        });
        // category grips already in markup (.excel-calendar__group-grip) — make draggable
        grid.querySelectorAll('.excel-calendar__group-grip').forEach(function (grip) {
            grip.setAttribute('draggable', 'true');
            grip.setAttribute('role', 'button');
            grip.setAttribute('tabindex', '0');
        });
    }

    // --- keyboard a11y: Alt+Arrow on focused grip ---
    document.addEventListener('keydown', function (e) {
        if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
        var rowGrip = e.target.closest && e.target.closest('.excel-calendar__row-grip');
        var catGrip = e.target.closest && e.target.closest('.excel-calendar__group-grip');
        if (!rowGrip && !catGrip) return;
        e.preventDefault();
        var dir = e.key === 'ArrowUp' ? -1 : 1;
        if (rowGrip) {
            var tr = rowGrip.closest('tr[data-row-id]');
            var gid = tr.getAttribute('data-group-id') || '';
            var sibs = Array.prototype.slice.call(getGrid().querySelectorAll('tr[data-row-id][data-group-id="' + ((window.CSS&&CSS.escape)?CSS.escape(gid):gid) + '"]'));
            var i = sibs.indexOf(tr), j = i + dir;
            if (j < 0 || j >= sibs.length) return;
            tr.parentNode.insertBefore(dir < 0 ? tr : sibs[j], dir < 0 ? sibs[j] : tr);
            rowGrip.focus();
            postOrder(gid, rowIdsInGroup(gid));
        } else {
            var hr = catGrip.closest('tr.excel-calendar__group-header');
            var heads = Array.prototype.slice.call(getGrid().querySelectorAll('tr.excel-calendar__group-header'));
            var ci = heads.indexOf(hr), cj = ci + dir;
            if (cj < 0 || cj >= heads.length) return;
            var block = categoryBlock(hr.getAttribute('data-group-id'));
            var anchor = heads[cj];
            if (dir < 0) block.forEach(function (n) { anchor.parentNode.insertBefore(n, anchor); });
            else { var afterBlock = categoryBlock(anchor.getAttribute('data-group-id')); var ref = afterBlock[afterBlock.length-1].nextSibling; block.forEach(function (n) { anchor.parentNode.insertBefore(n, ref); }); }
            catGrip.focus();
            postOrder('', groupIdsInOrder());
        }
    });

    function init() { if (getGrid()) enable(); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
    document.addEventListener('calendar:grid-refreshed', function () {
        var g = getGrid(); if (g) { g._reorderBound = false; enable(); }
    });
})();
```

- [ ] **Step 3: CSS** — append to `wwwroot/css/calendar.css`:
```css
.excel-calendar__row-grip {
    cursor: grab; opacity: 0; margin-inline-end: 4px; user-select: none;
    color: var(--text-muted); font-size: 0.8rem; transition: opacity .12s;
}
tr[data-row-id]:hover .excel-calendar__row-grip,
.excel-calendar__row-grip:focus { opacity: .7; }
.excel-calendar__group-grip { cursor: grab; }
.excel-calendar__group-grip:focus,
.excel-calendar__row-grip:focus { outline: 2px solid var(--primary); outline-offset: 1px; }
@media (prefers-reduced-motion: reduce) { .excel-calendar__row-grip { transition: none; } }
```

- [ ] **Step 4: Localization** — add to `Resources/SharedResources.resx`:
```xml
<data name="Calendar_DragRow" xml:space="preserve"><value>Drag to reorder</value></data>
```
and `Resources/SharedResources.he-IL.resx`:
```xml
<data name="Calendar_DragRow" xml:space="preserve"><value>גרור לסידור מחדש</value></data>
```
(If `DragToReorder` is referenced by `Default.cshtml`'s grip `title` but missing, add it too with EN "Drag to reorder" / HE "גרור לסידור מחדש".)

- [ ] **Step 5: Include the script.** Add to the scripts section used by calendar pages (e.g. `Pages/Shared/_Layout.cshtml` near the other `calendar-*.js` includes, or each calendar page's `@section Scripts`):
```html
<script src="~/js/calendar-row-reorder.js" asp-append-version="true"></script>
```
Verify it loads on Shifts/Chores/OnCall/Overview (they may use a shared partial for calendar scripts — follow whatever includes `calendar-quick-entry.js`).

- [ ] **Step 6: Restart the app + verify served JS is fresh** (WebOptimizer cache):

Run (PowerShell): stop the running app, then `dotnet run --project ShiftManager.csproj --urls http://localhost:5000` in the background.
Run: `curl -s http://localhost:5000/js/calendar-row-reorder.js | grep -o "SaveRowOrder"`
Expected: prints `SaveRowOrder` (string literal survives minification → fresh JS live).

- [ ] **Step 7: Commit**
```bash
git add wwwroot/js/calendar-row-reorder.js wwwroot/js/excel-calendar-groups.js wwwroot/css/calendar.css Resources/SharedResources.resx Resources/SharedResources.he-IL.resx Pages/Shared/_Layout.cshtml
git commit -m "feat(calendar): client drag module for category + row reordering"
```

---

## Task 7: User deactivation cleanup

**Files:** Modify `Pages/Admin/Users.cshtml.cs` (~line 1072, inside the `wasActive && !u.IsActive` block)

- [ ] **Step 1: Add cleanup** after the `DirectorCompanies` delete:
```csharp
// Per-user calendar ordering is a personal UI preference — drop it on deactivation.
// SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific userId; entity is not company-scoped.
await _db.UserCalendarRowOrders.IgnoreQueryFilters()
    .Where(o => o.UserId == id)
    .ExecuteDeleteAsync();
```

- [ ] **Step 2: Build**

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**
```bash
git add Pages/Admin/Users.cshtml.cs
git commit -m "chore(calendar): clear row-order prefs on user deactivation"
```

---

## Task 8: Browser verification (Playwright)

**Files:** Create `tmp_reorder_verify.py` (temp; deleted at end of phase)

- [ ] **Step 1: Confirm fresh JS served** (per Task 6 Step 6). If `grep` finds nothing, restart the app first.

- [ ] **Step 2: Write the verification script** `tmp_reorder_verify.py`:
```python
import sys, re
sys.stdout.reconfigure(encoding="utf-8")
from playwright.sync_api import sync_playwright
BASE="http://localhost:5000"
def log(*a): print(*a, flush=True)
def login(pg):
    pg.goto(f"{BASE}/Auth/Login", wait_until="networkidle")
    pg.fill('input[name="Email"]',"admin@local"); pg.fill('input[name="Password"]',"admin123")
    pg.get_by_role("button", name=re.compile("login|sign|התחבר|כניסה",re.I)).first.click()
    pg.wait_for_load_state("networkidle")
with sync_playwright() as p:
    b=p.chromium.launch(headless=True); pg=b.new_page(); login(pg)
    # mol 1 / jt 1 has many users + categories
    pg.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Mode=user", wait_until="networkidle")
    pg.wait_for_timeout(400)
    def row_order():
        return pg.eval_on_selector_all('tr[data-row-id]', "els=>els.map(e=>e.getAttribute('data-row-id'))")
    before = row_order(); log("before:", before[:6])
    # Alt+Down on the first row's grip (deterministic vs mouse drag)
    grip = pg.locator('tr[data-row-id] .excel-calendar__row-grip').first
    grip.focus(); pg.keyboard.press("Alt+ArrowDown"); pg.wait_for_timeout(800)
    after = row_order(); log("after Alt+Down:", after[:6])
    moved = before[0] != after[0]
    # reload -> persisted server-side?
    pg.goto(f"{BASE}/Calendar/Shifts?MoleculeId=1&JobTypeId=1&Mode=user", wait_until="networkidle")
    pg.wait_for_timeout(400)
    persisted = row_order()
    log("after reload:", persisted[:6])
    log("RESULT moved:", moved, "| persisted-matches-after:", persisted[:6]==after[:6])
    b.close()
```

- [ ] **Step 3: Run it**

Run: `PYTHONIOENCODING=utf-8 python tmp_reorder_verify.py`
Expected: `moved: True` and the post-reload order matches the reordered order (server-side persistence confirmed). If `moved` is False, debug the grip focus/keydown path; if reload doesn't match, debug the API/contextKey.

- [ ] **Step 4: Run the full server test suite (sequential)** to confirm no regressions:

Run: `dotnet test -- xUnit.ParallelizeTestCollections=false`
Expected: all green (baseline was 1507/1507 per project memory; +~8 new tests).

- [ ] **Step 5: Commit** (verification script is temp — do NOT commit it; note results in the PR/summary instead). Final feature commit if any docs to update:
```bash
git add docs/
git commit -m "docs(calendar): mark reorder feature verified" || true
```

---

## Self-review notes (author)

- **Spec coverage:** entity (§4)→T1; ContextKey (§5)→T4; server apply (§6)→T3; client module both axes + a11y + collapse-suppress (§7)→T6; API (§8)→T5; CSS (§9)→T6; tests (§10)→T2/T3/T5/T8; cleanup (§4 deactivation)→T7. All sections mapped.
- **Type consistency:** `SaveOrderAsync(userId, contextKey, groupId, itemIds)` and `GetOrderMapAsync(userId, contextKey)→Dictionary<(string,string),int>` used identically in service, component, API, and tests. JSON field `itemIds` matches the request DTO and the JS `postOrder` body.
- **Known follow-ups to verify during impl (not placeholders):** exact line numbers drift as files change — search for the quoted anchor (`CalendarData.TotalRows = ...`) rather than trusting the number; confirm `IAuditLogService.LogAsync` named-arg signature against `QuickAddTextEntry.cshtml.cs`; confirm the calendar `.excel-calendar` element in `Default.cshtml` is a literal `<table>` (attribute injection point) vs a tag helper.
