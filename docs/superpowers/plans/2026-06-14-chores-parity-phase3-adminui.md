# Chores↔ShiftType Parity — Phase 3 (Admin UI) Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (- [ ]) syntax.
**Goal:** Wire the Phase 1 entities + Phase 2 services into the admin surface: extend `ChoreTypes/Index` with a Category section, a promoted type editor (category + weight + eligibility), and exemptions; add `DoesChores` + chore-category multiselect + a 3-state Gender editor to `Admin/Users`; and ship a new `ChoreTemplates/Index` page with a Stamp modal. No assignment, no scheduler, no grant changes.
**Architecture:** Razor Pages, server-rendered with progressive-enhancement JS. POST handlers for full-page mutations (create/update/activate); `[FromBody]` JSON AJAX handlers for in-place toggles and dialogs (mirroring the existing `OnPostUserCategoriesAsync` pattern). Two contract gaps are closed inside this plan: (1) extend `IChoreCategoryService.CreateAsync/RenameAsync` to carry `nameEn`/`nameHe`; (2) author a new molecule-scoped `IChoreTemplateService`/`ChoreTemplateService` (the Phase 2 services wrote only `IChoreService.StampTemplateAsync`, no template CRUD). All admin handlers re-verify the molecule for IDOR via the existing `IsUserAuthorizedForMoleculeAsync` / `AuthorizeUserEditAsync` gates.
**Tech Stack:** ASP.NET Core 8.0 Razor Pages, EF Core (SQLite), bilingual he/en + RTL.
**Depends on:** Phase 1 (entities) + Phase 2 (services). **Spec:** docs/superpowers/specs/2026-06-14-chores-shifttype-parity-design.md
---

## Phase 2 Service Contract consumed here (verbatim — call these EXACT signatures, all in namespace `ShiftManager.Services`)

```csharp
// Eligibility (Phase 2 Task 1)
public enum EligibilityViolation { RequiresGender = 0, RequiresOfficerRank = 1, Exempt = 2 }
public sealed record EligibilityResult(IReadOnlyList<EligibilityViolation> Violations)
    { public bool IsEligible => Violations.Count == 0; public static readonly EligibilityResult Eligible; }

// Category admin (Phase 2 Task 3) — NOTE the gap-1 extension this plan applies to CreateAsync/RenameAsync.
public interface IChoreCategoryService {
    Task<List<ChoreCategory>> GetCategoriesForMoleculeAsync(int moleculeId, bool includeInactive = false);
    Task<ChoreCategory?> GetCategoryAsync(int categoryId);
    Task<ChoreCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null);   // → +nameEn/+nameHe (Task 2)
    Task<bool> RenameAsync(int categoryId, string name, string displayName, string? color);                    // → +nameEn/+nameHe (Task 2)
    Task<bool> DeleteAsync(int categoryId);
    Task<(int ChoreTypeCount, int MemberCount)> GetUsageAsync(int categoryId);
    Task<bool> AssignChoreTypeAsync(int choreTypeId, int? categoryId);
    Task<List<int>> GetUserCategoryIdsAsync(int userId);
    Task SetUserCategoriesAsync(int userId, IReadOnlyCollection<int> categoryIds);
}

// Eligibility-rule + exemption admin (Phase 2 Task 4) — gate the PAGE with Grant:EditChoreTypes.
public interface IChoreEligibilityAdminService {
    Task<List<EligibilityRule>> GetRulesForChoreTypeAsync(int choreTypeId);
    Task<bool> SetRulesForChoreTypeAsync(int choreTypeId, Gender? requiredGender, bool requiresOfficerRank, int createdBy);
    Task<List<UserChoreExemption>> GetExemptionsForChoreTypeAsync(int choreTypeId);
    Task<UserChoreExemption?> AddExemptionAsync(int userId, int choreTypeId, string? reason, int createdBy);
    Task<bool> RemoveExemptionAsync(int userId, int choreTypeId);
}

// Stamping (Phase 2 Task 5, on IChoreService)
Task<StampResult> StampTemplateAsync(int templateId, DateOnly from, DateOnly to,
    IReadOnlyList<DayOfWeek> weekdays, IReadOnlyList<int> assigneeIds, bool rotate);
public sealed record StampCreated(DateOnly Date, int UserId, int ChoreId);
public sealed record StampSkipped(DateOnly Date, int UserId, string ReasonKey);
public sealed record StampResult(IReadOnlyList<StampCreated> Created, IReadOnlyList<StampSkipped> Skipped)
    { public int CreatedCount; public int SkippedCount; }

// Shared (Phase 2): DurationFormat.FormatMinutes(int)->"1h 30m"; DurationFormat.FormatHours(int)->"12.5h"
// Weight constant: ChoreService.DEFAULT_CHORE_WEIGHT_MINUTES = 480
```

## Contract gaps resolved in this plan
1. **Bilingual category names (RESOLVED = option a).** The `ChoreCategory` entity has `NameEn`/`NameHe` (Phase 1 Task 3) but Phase 2's `IChoreCategoryService.CreateAsync/RenameAsync` omit them. The §7.1 UI wants bilingual category names (the existing ChoreTypes create/edit forms already have `NameEnglish`/`NameHebrew` inputs). **Task 2 extends both signatures** with `string? nameEn = null, string? nameHe = null` and persists them — a ~6-line service change + 1 test. Keeping them DisplayName-only (option b) would leave `NameEn`/`NameHe` permanently null with no UI to set them; rejected.
2. **ChoreTemplate has no CRUD service.** Phase 2 wrote only `IChoreService.StampTemplateAsync`. **Task 1 authors `IChoreTemplateService`/`ChoreTemplateService`** (molecule-scoped list/get/create/update/delete/activate), mirroring `ChoreTypeService` exactly, plus DI registration. The Stamp modal then loads templates via this service and stamps via `IChoreService.StampTemplateAsync`.

---

## File Structure

```
Services/
  IChoreTemplateService.cs              (NEW — Task 1, gap-2)
  ChoreTemplateService.cs               (NEW — Task 1, gap-2)
  IChoreCategoryService.cs              (MODIFY — Task 2, gap-1: +nameEn/nameHe on Create/Rename)
  ChoreCategoryService.cs               (MODIFY — Task 2, gap-1)
Program.cs                              (MODIFY — Task 1: register IChoreTemplateService)
Pages/Admin/Organization/
  ChoreTypes/Index.cshtml               (MODIFY — Tasks 3,4,5: Category section + promoted editor + exemptions)
  ChoreTypes/Index.cshtml.cs            (MODIFY — Tasks 3,4,5: category/eligibility/exemption handlers)
  ChoreTemplates/Index.cshtml          (NEW — Task 8: template CRUD + Stamp modal)
  ChoreTemplates/Index.cshtml.cs       (NEW — Task 8)
  Index.cshtml                          (MODIFY — Task 8: add "Manage Chore Templates" nav link)
Pages/Admin/
  Users.cshtml                          (MODIFY — Tasks 6,7: DoesChores cell + Gender cell + dialogs + JS)
  Users.cshtml.cs                       (MODIFY — Tasks 6,7: DoesChores/category/gender handlers + VM fields)
Resources/
  SharedResources.resx                  (MODIFY — Task 9: en keys)
  SharedResources.he-IL.resx            (MODIFY — Task 9: he keys)
ShiftManager.Tests/UnitTests/
  Services/ChoreTemplateServiceTests.cs           (NEW — Task 1)
  Services/ChoreCategoryServiceBilingualTests.cs  (NEW — Task 2)
  Pages/ChoreTypesCategoryHandlerTests.cs         (NEW — Task 3)
  Pages/ChoreTypesEligibilityHandlerTests.cs      (NEW — Task 4)
  Pages/ChoreTypesExemptionHandlerTests.cs        (NEW — Task 5)
  Pages/UsersDoesChoresHandlerTests.cs            (NEW — Task 6)
  Pages/UsersGenderHandlerTests.cs               (NEW — Task 7)
  Pages/ChoreTemplatesStampHandlerTests.cs        (NEW — Task 8)
```

**Reference files cloned (read these in the corresponding task):**
- `Pages/Admin/Organization/ChoreTypes/Index.cshtml[.cs]` — `[Authorize(Policy="Grant:EditChoreTypes")]`, molecule selector, `IsUserAuthorizedForMoleculeAsync` IDOR guard, create-form + inline `.edit-row` pattern, `SanitizeColor`, `ColorUtilities.SanitizeHexColor`, the `.js-edit-toggle`/`.js-edit-cancel` JS.
- `Services/ChoreTypeService.cs` + `IChoreTypeService.cs` — the exact shape to mirror for `ChoreTemplateService`.
- `Pages/Admin/Users.cshtml[.cs]` — `does-shifts-cell` markup (`Users.cshtml:1014-1037`), `doesShiftsConfirmDialog` (`:1446`), `userCategoriesDialog` (`:1459`), the DoesShifts/category JS (`:1640-1754`); `AuthorizeUserEditAsync` (`Users.cshtml.cs:1656`), `OnGetDoesShiftsImpactAsync` (`:1678`), `OnPostDoesShiftsAsync` (`:1695`), `OnPostUserCategoriesAsync` (`:1736`), `OnPostAccountTypeAsync` (`:1491`), the editable-cell badge pattern (`Users.cshtml:991-1012`).

---

## CONVENTIONS (apply to every task)
- **Verb sequencing:** never write "Let me do X:" before a tool call. State actions with a period.
- **Function-reference gate (per user global rule):** before referencing any JS function/handler, confirm it is defined in the same `<script>` block / file. The Users page JS is wrapped in IIFEs — keep new JS in its own IIFE; do not assume cross-IIFE visibility.
- **Localization:** every user-visible string uses `<loc key="X" />` (markup) or `@Localizer["X"]` (attributes/C#). **GREP the key in `Resources/SharedResources.resx` BEFORE adding it** — the project breaks loc tests on duplicate keys (MEMORY: Next7Days dup broke 6 tests).
- **Color/contrast/RTL (binding, §7.7 + MEMORY CSS rules):** category color is ALWAYS a dot/accent (`.color-swatch`), never a text background. `.elig-chip`/`.gender-badge` pair a colored background with explicit light text + `!important`; dark-mode overrides redeclare ONLY `color` (never structural props — preserves RTL flips). Use existing tokens (`--*-soft`, `--warning-text`, `--primary-contrast`); never invent a `var(--undefined)`.
- **Color sanitization:** every color that reaches inline CSS goes through `ShiftManager.Services.ColorUtilities.SanitizeHexColor(...)` (the ChoreTypes page wraps it as `SanitizeColor`).
- **IDOR:** every ChoreTypes/ChoreTemplates handler re-verifies the target's molecule via `IsUserAuthorizedForMoleculeAsync(moleculeId)`. Every Users handler rides `AuthorizeUserEditAsync(id)`.
- **Build between steps:** `dotnet build ShiftManager.csproj`. **Run tests sequentially:** `-- xUnit.ParallelizeTestCollections=false` (MEMORY: parallel `:memory:` SQLite contention yields spurious failures).
- **App-lock (per user global rule #3):** if a build fails on a locked exe, STOP and ask the user to close the running app (or taskkill) before rebuilding — never rebuild over a lock.

---

## Task 1 — `ChoreTemplateService` (gap-2) + DI + tests

**Files:**
- Create: `Services/IChoreTemplateService.cs`, `Services/ChoreTemplateService.cs`
- Create: `ShiftManager.Tests/UnitTests/Services/ChoreTemplateServiceTests.cs`
- Modify: `Program.cs` (register after line 388 `builder.Services.AddScoped<IChoreTypeService, ChoreTypeService>();`)

Mirror `ChoreTypeService` exactly (same `IgnoreQueryFilters()` posture — `ChoreTemplate` is molecule-scoped, NOT tenant-filtered; same security-audit comment). The Stamp modal (Task 8) calls these for list/get; `IChoreService.StampTemplateAsync` does the actual stamping.

- [ ] **Step 1: Failing test** `ShiftManager.Tests/UnitTests/Services/ChoreTemplateServiceTests.cs` (real SQLite, sequential). Asserts: create returns a row with `IsActive=true` + monotonic implicit ordering; get-for-molecule excludes inactive unless `includeInactive`; update mutates Name/DefaultTitle/ChoreTypeId/times/override/notes; cross-molecule get returns empty; deactivate flips `IsActive`; delete removes the row.

```csharp
using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>Real-SQLite CRUD coverage for ChoreTemplateService (molecule-scoped, mirrors ChoreTypeService).</summary>
public sealed class ChoreTemplateServiceTests
{
    private static async Task SeedAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" });
        f.Db.Molecules.Add(new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Then_List_And_Update()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreTemplateService(f.Db);

        var t = await svc.CreateAsync(moleculeId: 1, name: "Kitchen close", choreTypeId: null,
            defaultTitle: "Close kitchen", startTime: null, endTime: null,
            weightMinutesOverride: null, notes: null, userId: 10);
        t.Should().NotBeNull();
        t!.IsActive.Should().BeTrue();

        (await svc.GetTemplatesForMoleculeAsync(1)).Should().ContainSingle();
        (await svc.GetTemplatesForMoleculeAsync(2)).Should().BeEmpty("templates are molecule-scoped");

        (await svc.UpdateAsync(t.Id, "Kitchen open", choreTypeId: null, defaultTitle: "Open kitchen",
            startTime: new TimeOnly(8, 0), endTime: new TimeOnly(10, 0), weightMinutesOverride: 120, notes: "am"))
            .Should().BeTrue();
        var reread = await svc.GetByIdAsync(t.Id);
        reread!.Name.Should().Be("Kitchen open");
        reread.DefaultTitle.Should().Be("Open kitchen");
        reread.WeightMinutesOverride.Should().Be(120);
    }

    [Fact]
    public async Task Deactivate_Hides_From_Default_List_But_Visible_With_IncludeInactive()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreTemplateService(f.Db);
        var t = await svc.CreateAsync(1, "T", null, "title", null, null, null, null, 10);

        (await svc.DeactivateAsync(t!.Id)).Should().BeTrue();
        (await svc.GetTemplatesForMoleculeAsync(1)).Should().BeEmpty();
        (await svc.GetTemplatesForMoleculeAsync(1, includeInactive: true)).Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_Removes_Row()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreTemplateService(f.Db);
        var t = await svc.CreateAsync(1, "T", null, "title", null, null, null, null, 10);

        (await svc.DeleteAsync(t!.Id)).Should().BeTrue();
        (await svc.GetByIdAsync(t.Id)).Should().BeNull();
        (await svc.DeleteAsync(99999)).Should().BeFalse();
    }
}
```

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ChoreTemplateServiceTests" -- xUnit.ParallelizeTestCollections=false`
Expected: FAILS to compile (service does not exist).

- [ ] **Step 2: Create `Services/IChoreTemplateService.cs`**

```csharp
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Molecule-scoped CRUD over reusable <see cref="ChoreTemplate"/> definitions a manager stamps across a
/// date range. Carries NO schedule and does NOT stamp — stamping is <see cref="IChoreService.StampTemplateAsync"/>.
/// Mirrors <see cref="IChoreTypeService"/>. Page layer gates with the existing EditChoreTypes grant.
/// </summary>
public interface IChoreTemplateService
{
    Task<List<ChoreTemplate>> GetTemplatesForMoleculeAsync(int moleculeId, bool includeInactive = false);
    Task<ChoreTemplate?> GetByIdAsync(int id);
    Task<ChoreTemplate?> CreateAsync(int moleculeId, string name, int? choreTypeId, string defaultTitle,
        TimeOnly? startTime, TimeOnly? endTime, int? weightMinutesOverride, string? notes, int userId);
    Task<bool> UpdateAsync(int id, string name, int? choreTypeId, string defaultTitle,
        TimeOnly? startTime, TimeOnly? endTime, int? weightMinutesOverride, string? notes);
    Task<bool> DeactivateAsync(int id);
    Task<bool> ActivateAsync(int id);
    Task<bool> DeleteAsync(int id);
}
```

- [ ] **Step 3: Create `Services/ChoreTemplateService.cs`** (mirror `ChoreTypeService` — same audit comment + `IgnoreQueryFilters()`)

```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — chore templates are molecule-scoped
// configuration; queries scoped by explicit moleculeId/id parameter; called only from the authorized
// EditChoreTypes page layer which re-verifies the molecule for IDOR before mutating.
public class ChoreTemplateService : IChoreTemplateService
{
    private readonly AppDbContext _db;

    public ChoreTemplateService(AppDbContext db) => _db = db;

    public async Task<List<ChoreTemplate>> GetTemplatesForMoleculeAsync(int moleculeId, bool includeInactive = false)
    {
        var q = _db.ChoreTemplates.IgnoreQueryFilters().Where(t => t.MoleculeId == moleculeId);
        if (!includeInactive)
            q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<ChoreTemplate?> GetByIdAsync(int id)
        => await _db.ChoreTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);

    public async Task<ChoreTemplate?> CreateAsync(int moleculeId, string name, int? choreTypeId, string defaultTitle,
        TimeOnly? startTime, TimeOnly? endTime, int? weightMinutesOverride, string? notes, int userId)
    {
        name = name.Trim();
        defaultTitle = defaultTitle.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(defaultTitle))
            return null;

        var template = new ChoreTemplate
        {
            MoleculeId = moleculeId,
            Name = name,
            ChoreTypeId = choreTypeId,
            DefaultTitle = defaultTitle,
            StartTime = startTime,
            EndTime = endTime,
            WeightMinutesOverride = weightMinutesOverride,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IsActive = true,
            CreatedBy = userId
        };
        _db.ChoreTemplates.Add(template);
        await _db.SaveChangesAsync();
        return template;
    }

    public async Task<bool> UpdateAsync(int id, string name, int? choreTypeId, string defaultTitle,
        TimeOnly? startTime, TimeOnly? endTime, int? weightMinutesOverride, string? notes)
    {
        var t = await _db.ChoreTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null)
            return false;

        name = name.Trim();
        defaultTitle = defaultTitle.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(defaultTitle))
            return false;

        t.Name = name;
        t.ChoreTypeId = choreTypeId;
        t.DefaultTitle = defaultTitle;
        t.StartTime = startTime;
        t.EndTime = endTime;
        t.WeightMinutesOverride = weightMinutesOverride;
        t.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeactivateAsync(int id) => await SetActiveAsync(id, false);
    public async Task<bool> ActivateAsync(int id) => await SetActiveAsync(id, true);

    private async Task<bool> SetActiveAsync(int id, bool active)
    {
        var t = await _db.ChoreTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null)
            return false;
        t.IsActive = active;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var t = await _db.ChoreTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null)
            return false;
        _db.ChoreTemplates.Remove(t);
        await _db.SaveChangesAsync();
        return true;
    }
}
```

- [ ] **Step 4: Register DI in `Program.cs`** — add immediately after line 388 (`builder.Services.AddScoped<IChoreTypeService, ChoreTypeService>();`):

```csharp
builder.Services.AddScoped<IChoreTemplateService, ChoreTemplateService>();
```

- [ ] **Step 5: Run the test — PASS.** `dotnet test ... --filter "FullyQualifiedName~ChoreTemplateServiceTests" -- xUnit.ParallelizeTestCollections=false` → 3 tests pass.

- [ ] **Step 6: Commit**

```bash
git add Services/IChoreTemplateService.cs Services/ChoreTemplateService.cs Program.cs \
  ShiftManager.Tests/UnitTests/Services/ChoreTemplateServiceTests.cs
git commit -m "feat(chores): ChoreTemplateService CRUD (gap-2) + DI"
```

---

## Task 2 — Extend `IChoreCategoryService.CreateAsync/RenameAsync` with `nameEn`/`nameHe` (gap-1)

**Files:**
- Modify: `Services/IChoreCategoryService.cs` (the two signatures)
- Modify: `Services/ChoreCategoryService.cs` (`CreateAsync` ~line 743, `RenameAsync` ~line 771 of the Phase 2 plan's source)
- Create: `ShiftManager.Tests/UnitTests/Services/ChoreCategoryServiceBilingualTests.cs`

> **Why option (a):** the entity has `NameEn`/`NameHe`; the §7.1 admin UI (Task 3) exposes English/Hebrew inputs exactly like the existing ChoreTypes form. Without this, those columns stay null forever.

- [ ] **Step 1: Failing test** `ChoreCategoryServiceBilingualTests.cs`. Asserts create persists `NameEn`/`NameHe`; rename updates them; null/blank → null.

```csharp
using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

public sealed class ChoreCategoryServiceBilingualTests
{
    private static async Task SeedAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Persists_Bilingual_Names()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreCategoryService(f.Db);

        var c = await svc.CreateAsync(1, "Physical", "Physical", color: null, nameEn: "Physical", nameHe: "פיזי");
        c.Should().NotBeNull();
        c!.NameEn.Should().Be("Physical");
        c.NameHe.Should().Be("פיזי");
    }

    [Fact]
    public async Task Rename_Updates_Bilingual_Names_And_Blank_Becomes_Null()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreCategoryService(f.Db);
        var c = await svc.CreateAsync(1, "Physical", "Physical", null, "Physical", "פיזי");

        (await svc.RenameAsync(c!.Id, "Manual", "Manual", color: "#3b82f6", nameEn: "Manual", nameHe: "ידני"))
            .Should().BeTrue();
        var reread = await svc.GetCategoryAsync(c.Id);
        reread!.NameEn.Should().Be("Manual");
        reread.NameHe.Should().Be("ידני");

        (await svc.RenameAsync(c.Id, "Manual", "Manual", null, nameEn: "  ", nameHe: null)).Should().BeTrue();
        reread = await svc.GetCategoryAsync(c.Id);
        reread!.NameEn.Should().BeNull("blank trims to null");
        reread.NameHe.Should().BeNull();
    }
}
```

- [ ] **Step 2: Extend the interface** `Services/IChoreCategoryService.cs`:

```csharp
    Task<ChoreCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null, string? nameEn = null, string? nameHe = null);
    Task<bool> RenameAsync(int categoryId, string name, string displayName, string? color, string? nameEn = null, string? nameHe = null);
```

- [ ] **Step 3: Extend the impl** `Services/ChoreCategoryService.cs`. Add the params + persist. In `CreateAsync`, after building `category` add the two assignments before `_db.ChoreCategories.Add`:

```csharp
    public async Task<ChoreCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null, string? nameEn = null, string? nameHe = null)
    {
        // ... existing trim + duplicate-name guard + nextSort unchanged ...
        var category = new ChoreCategory
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim(),
            NameHe = string.IsNullOrWhiteSpace(nameHe) ? null : nameHe.Trim(),
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            SortOrder = nextSort + 1,
            IsActive = true
        };
        _db.ChoreCategories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }
```

In `RenameAsync`, after the existing `category.Color = ...` line and before `await _db.SaveChangesAsync()`:

```csharp
        category.NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim();
        category.NameHe = string.IsNullOrWhiteSpace(nameHe) ? null : nameHe.Trim();
```

> **Caller compatibility:** the new params are optional with `= null`, so the Phase 2 test `ChoreCategoryServiceTests` (which calls `CreateAsync(1, "Physical", "Physical")` and `RenameAsync(id, name, displayName, color)`) still compiles and passes unchanged. Verify both suites green in Step 4.

- [ ] **Step 4: Run** `dotnet test ... --filter "FullyQualifiedName~ChoreCategoryService" -- xUnit.ParallelizeTestCollections=false` → bilingual tests pass AND the prior `ChoreCategoryServiceTests` stay green.

- [ ] **Step 5: Commit**

```bash
git add Services/IChoreCategoryService.cs Services/ChoreCategoryService.cs \
  ShiftManager.Tests/UnitTests/Services/ChoreCategoryServiceBilingualTests.cs
git commit -m "feat(chores): bilingual NameEn/NameHe on ChoreCategoryService Create/Rename (gap-1)"
```

---

## Task 3 — `ChoreTypes/Index`: Category section (CRUD via `IChoreCategoryService`)

**Files:**
- Modify: `Pages/Admin/Organization/ChoreTypes/Index.cshtml.cs` (inject `IChoreCategoryService`; add `Categories` VM + create/update/deactivate/delete handlers)
- Modify: `Pages/Admin/Organization/ChoreTypes/Index.cshtml` (a new "Chore Categories" `.section-card` ABOVE the create-type card)
- Create: `ShiftManager.Tests/UnitTests/Pages/ChoreTypesCategoryHandlerTests.cs`

A top section-card mirroring the existing ChoreType create-form + `.data-table` + inline `.edit-row`, but for categories. Color renders as `.color-swatch` (dot, never text bg). Bilingual name inputs reuse the `NameEnglish`/`NameHebrew` keys (already in resx).

- [ ] **Step 1: Failing handler test** `ChoreTypesCategoryHandlerTests.cs` (real SQLite, page model constructed with stubbed grant service returning the molecule as accessible). Asserts: create persists a category in the selected molecule; create in an inaccessible molecule is rejected (IDOR); rename + delete round-trip; delete-impact uses `GetUsageAsync`. Mirror the construction pattern in `UsersAccountTypeHandlerTests.cs`.

```csharp
// Skeleton — fill collaborators to match UsersAccountTypeHandlerTests construction.
[Fact]
public async Task OnPostCreateCategory_Accessible_Molecule_Persists()
{
    // arrange: page with EditChoreTypes-accessible molecule 1; bind CategoryName/CategoryDisplayName/MoleculeId
    // act: await page.OnPostCreateCategoryAsync();
    // assert: f.Db.ChoreCategories.Single().Name == "Physical"; NameEn/NameHe persisted.
}

[Fact]
public async Task OnPostCreateCategory_Inaccessible_Molecule_Rejected()
{
    // grant stub returns NO accessible molecules; assert no row created + TempData ErrorMessage set.
}
```

- [ ] **Step 2: PageModel changes** `Index.cshtml.cs`.

(a) Inject the service — add to the ctor parameter list + field (after `_choreTypeService`):

```csharp
    private readonly IChoreCategoryService _choreCategoryService;
    // ctor param: IChoreCategoryService choreCategoryService
    // ctor body:  _choreCategoryService = choreCategoryService;
```

(b) Add a VM record + collection (next to `ChoreTypeVM`):

```csharp
    public record ChoreCategoryVM(int Id, string Name, string DisplayName, string? NameEn, string? NameHe, string? Color, int SortOrder, bool IsActive, int ChoreTypeCount, int MemberCount);
    public List<ChoreCategoryVM> Categories { get; set; } = new();

    // Category create form
    [BindProperty] public string CategoryName { get; set; } = string.Empty;
    [BindProperty] public string CategoryDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? CategoryNameEn { get; set; }
    [BindProperty] public string? CategoryNameHe { get; set; }
    [BindProperty] public string? CategoryColor { get; set; }

    // Category edit form
    [BindProperty] public int CategoryEditId { get; set; }
    [BindProperty] public string CategoryEditDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? CategoryEditNameEn { get; set; }
    [BindProperty] public string? CategoryEditNameHe { get; set; }
    [BindProperty] public string? CategoryEditColor { get; set; }
```

(c) In `LoadDataAsync`, inside `if (MoleculeId.HasValue)` after `ChoreTypes` is built, load categories + usage:

```csharp
            var cats = await _choreCategoryService.GetCategoriesForMoleculeAsync(MoleculeId.Value, includeInactive: true);
            var catVms = new List<ChoreCategoryVM>(cats.Count);
            foreach (var c in cats)
            {
                var (typeCount, memberCount) = await _choreCategoryService.GetUsageAsync(c.Id);
                catVms.Add(new ChoreCategoryVM(c.Id, c.Name, c.DisplayName, c.NameEn, c.NameHe, c.Color, c.SortOrder, c.IsActive, typeCount, memberCount));
            }
            Categories = catVms;
```

(d) Add handlers (mirror `OnPostCreateAsync`/`OnPostUpdateAsync`/`OnPostDeactivateAsync` exactly — same IDOR check via `IsUserAuthorizedForMoleculeAsync(MoleculeId.Value)` for create, and via the category's molecule for edit/delete; same `SanitizeColor`; same audit calls). `RenameAsync`/`DeleteAsync` return bool — map false to `Error_ChoreCategoryNotFound`/`Error_ChoreCategoryNameExists`:

```csharp
    public async Task<IActionResult> OnPostCreateCategoryAsync()
    {
        if (!MoleculeId.HasValue || MoleculeId.Value <= 0)
        { TempData["ErrorMessage"] = _localizer["Error_MoleculeRequired"].Value; return RedirectToPage(); }
        if (string.IsNullOrWhiteSpace(CategoryName) || string.IsNullOrWhiteSpace(CategoryDisplayName))
        { TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNameRequired"].Value; return RedirectToPage(new { MoleculeId }); }
        if (!await IsUserAuthorizedForMoleculeAsync(MoleculeId.Value))
        { TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value; return RedirectToPage(); }

        var created = await _choreCategoryService.CreateAsync(
            MoleculeId.Value, CategoryName.Trim(), CategoryDisplayName.Trim(),
            SanitizeColor(CategoryColor),
            string.IsNullOrWhiteSpace(CategoryNameEn) ? null : CategoryNameEn.Trim(),
            string.IsNullOrWhiteSpace(CategoryNameHe) ? null : CategoryNameHe.Trim());
        if (created == null)
        { TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNameExists"].Value; return RedirectToPage(new { MoleculeId }); }

        await _auditLogService.LogAsync("ChoreCategoryCreated", "ChoreCategory", created.Id,
            $"Created chore category '{created.DisplayName}' in molecule (MoleculeId={MoleculeId.Value})");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreCategoryCreated"], created.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostUpdateCategoryAsync()
    {
        var cat = await _choreCategoryService.GetCategoryAsync(CategoryEditId);
        if (cat == null || !await IsUserAuthorizedForMoleculeAsync(cat.MoleculeId))
        { TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNotFound"].Value; return RedirectToPage(new { MoleculeId }); }

        var ok = await _choreCategoryService.RenameAsync(CategoryEditId, cat.Name, CategoryEditDisplayName.Trim(),
            SanitizeColor(CategoryEditColor),
            string.IsNullOrWhiteSpace(CategoryEditNameEn) ? null : CategoryEditNameEn.Trim(),
            string.IsNullOrWhiteSpace(CategoryEditNameHe) ? null : CategoryEditNameHe.Trim());
        if (!ok)
        { TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNameExists"].Value; return RedirectToPage(new { MoleculeId }); }

        await _auditLogService.LogAsync("ChoreCategoryUpdated", "ChoreCategory", CategoryEditId, $"Updated chore category '{CategoryEditDisplayName}'");
        TempData["SuccessMessage"] = _localizer["Success_ChoreCategoryUpdated"].Value;
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        var cat = await _choreCategoryService.GetCategoryAsync(id);
        if (cat == null || !await IsUserAuthorizedForMoleculeAsync(cat.MoleculeId))
        { TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNotFound"].Value; return RedirectToPage(new { MoleculeId }); }

        await _choreCategoryService.DeleteAsync(id);   // FK SetNull un-categorizes types; membership cascades.
        await _auditLogService.LogAsync("ChoreCategoryDeleted", "ChoreCategory", id, $"Deleted chore category '{cat.DisplayName}'");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreCategoryDeleted"], cat.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }
```

- [ ] **Step 3: Markup** `Index.cshtml` — insert a category section-card immediately after the `molecule-selector` card and BEFORE the `@if (Model.MoleculeId.HasValue)` "Create New Chore Type" block. Wrap it in `@if (Model.MoleculeId.HasValue) { ... }`. Reuse the page's existing `.section-card`/`.form-grid`/`.data-table`/`.edit-row`/`.color-swatch` styles (no new CSS).

```html
@if (Model.MoleculeId.HasValue)
{
    <div class="section-card">
        <h3 class="section-header"><loc key="ChoreCategories" /> (@Model.Categories.Count)</h3>
        <div class="info-box"><loc key="ChoreCategoriesInfo" /></div>

        <form method="post" asp-page-handler="CreateCategory">
            @Html.AntiForgeryToken()
            <input type="hidden" name="MoleculeId" value="@Model.MoleculeId" />
            <div class="form-grid">
                <div class="form-group">
                    <label class="form-label"><loc key="Name" /> <span class="form-label-required">*</span></label>
                    <input class="form-input" asp-for="CategoryName" required maxlength="100" />
                </div>
                <div class="form-group">
                    <label class="form-label">@Localizer["DisplayName"] <span class="form-label-required">*</span></label>
                    <input class="form-input" asp-for="CategoryDisplayName" required maxlength="100" />
                </div>
                <div class="form-group">
                    <label class="form-label">@Localizer["NameEnglish"]</label>
                    <input class="form-input" asp-for="CategoryNameEn" maxlength="100" />
                </div>
                <div class="form-group">
                    <label class="form-label">@Localizer["NameHebrew"]</label>
                    <input class="form-input" asp-for="CategoryNameHe" maxlength="100" />
                </div>
                <div class="form-group">
                    <label class="form-label">@Localizer["Color"]</label>
                    <input class="form-input" type="color" asp-for="CategoryColor" value="#3b82f6" />
                </div>
            </div>
            <button class="btn btn-primary" type="submit"><loc key="CreateChoreCategory" /></button>
        </form>

        @if (Model.Categories.Any())
        {
            <div style="overflow-x:auto; margin-top:1.25rem;">
                <table class="data-table">
                    <thead>
                        <tr>
                            <th><loc key="DisplayName" /></th>
                            <th>@Localizer["NameEnglish"]</th>
                            <th>@Localizer["NameHebrew"]</th>
                            <th><loc key="Color" /></th>
                            <th><loc key="ChoreTypes" /></th>
                            <th><loc key="Members" /></th>
                            <th style="width:1%"><loc key="Actions" /></th>
                        </tr>
                    </thead>
                    <tbody>
                    @foreach (var c in Model.Categories)
                    {
                        <tr id="cat-row-@c.Id">
                            <td><strong>@c.DisplayName</strong></td>
                            <td><small>@(c.NameEn ?? "-")</small></td>
                            <td><small>@(c.NameHe ?? "-")</small></td>
                            <td>
                                @if (!string.IsNullOrEmpty(c.Color))
                                {
                                    <span class="color-swatch" style="background-color: @c.Color;"></span>
                                }
                                else { <span style="color: var(--text-muted);">-</span> }
                            </td>
                            <td>@c.ChoreTypeCount</td>
                            <td>@c.MemberCount</td>
                            <td>
                                <div class="table-actions">
                                    <button type="button" class="btn btn-ghost js-edit-toggle" data-target="cat-edit-@c.Id">@Localizer["Edit"]</button>
                                    <form method="post" asp-page-handler="DeleteCategory" style="display:inline"
                                          onsubmit="return confirm('@Localizer["ChoreCategoryDeleteConfirm"]');">
                                        @Html.AntiForgeryToken()
                                        <input type="hidden" name="id" value="@c.Id" />
                                        <input type="hidden" name="MoleculeId" value="@Model.MoleculeId" />
                                        <button type="submit" class="btn btn-danger">@Localizer["Delete"]</button>
                                    </form>
                                </div>
                            </td>
                        </tr>
                        <tr class="edit-row" id="cat-edit-@c.Id">
                            <td colspan="7">
                                <form method="post" asp-page-handler="UpdateCategory">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="CategoryEditId" value="@c.Id" />
                                    <input type="hidden" name="MoleculeId" value="@Model.MoleculeId" />
                                    <div class="edit-form-grid">
                                        <div class="form-group">
                                            <label class="form-label">@Localizer["DisplayName"]</label>
                                            <input class="form-input" name="CategoryEditDisplayName" value="@c.DisplayName" required />
                                        </div>
                                        <div class="form-group">
                                            <label class="form-label">@Localizer["NameEnglish"]</label>
                                            <input class="form-input" name="CategoryEditNameEn" value="@(c.NameEn ?? "")" maxlength="100" />
                                        </div>
                                        <div class="form-group">
                                            <label class="form-label">@Localizer["NameHebrew"]</label>
                                            <input class="form-input" name="CategoryEditNameHe" value="@(c.NameHe ?? "")" maxlength="100" />
                                        </div>
                                        <div class="form-group">
                                            <label class="form-label">@Localizer["Color"]</label>
                                            <input class="form-input" type="color" name="CategoryEditColor" value="@(c.Color ?? "#3b82f6")" />
                                        </div>
                                        <div class="form-group" style="align-self:end;">
                                            <button type="submit" class="btn btn-primary">@Localizer["Save"]</button>
                                            <button type="button" class="btn btn-ghost js-edit-cancel" data-target="cat-edit-@c.Id">@Localizer["Cancel"]</button>
                                        </div>
                                    </div>
                                </form>
                            </td>
                        </tr>
                    }
                    </tbody>
                </table>
            </div>
        }
        else { <p class="empty-state"><loc key="NoChoreCategoriesYet" /></p> }
    </div>
}
```

> The page's existing `.js-edit-toggle`/`.js-edit-cancel` delegated click handler (`Index.cshtml:288-308`) already toggles ANY `.edit-row` by `data-target` — the `cat-edit-@c.Id` rows work with zero JS changes (verified: the handler keys off `data-target`, not row id prefix).

- [ ] **Step 4: Build + run handler test** → pass.
- [ ] **Step 5: Browser-verify** at `http://localhost:5000/Admin/Organization/ChoreTypes?MoleculeId={id}` in **light/dark/RTL**: category card renders above the type card; create/edit/delete work; color swatch is a dot; Hebrew inputs accept RTL text; delete confirm fires.
- [ ] **Step 6: Commit** `feat(chores-ui): ChoreTypes category section (CRUD)`.

---

## Task 4 — `ChoreTypes/Index`: promoted type editor (category dropdown + weight h+m + eligibility fieldset)

**Files:**
- Modify: `Index.cshtml.cs` (inject `IChoreEligibilityAdminService`; load category options + per-type rules; extend create/update to set category + weight + rules)
- Modify: `Index.cshtml` (add Category `<select>`, DefaultWeight dual input, Eligibility fieldset to the create form + edit-row; eligibility reason chips in the type table)
- Create: `ShiftManager.Tests/UnitTests/Pages/ChoreTypesEligibilityHandlerTests.cs`

> **Severity reminder:** gender mismatch is a downstream *warning* (BusyService, Phase 2/4). The admin editor here only *persists* the rules; no severity logic lives in this page.

- [ ] **Step 1: Failing handler test** — after `OnPostUpdateAsync` with `EditCategoryId`, `EditWeightHours=2`, `EditWeightMinutes=0`, `EditRequiredGender=2` (Female), `EditRequiresOfficer=true`: the type's `ChoreCategoryId` is set via `AssignChoreTypeAsync`, `DefaultWeightMinutes==120`, and `GetRulesForChoreTypeAsync` returns a Female rule + an officer rule (replace-semantics). Cross-molecule category assignment is rejected.

- [ ] **Step 2: PageModel changes.**

(a) Inject `IChoreEligibilityAdminService _eligibilityAdmin` (ctor + field). `IChoreCategoryService` already injected (Task 3).

(b) Extend `ChoreTypeVM` to carry category + weight + a precomputed eligibility-chip list:

```csharp
    public record ChoreTypeVM(int Id, string Name, string DisplayName, string? Color, int SortOrder, string MoleculeName, bool IsActive, int ChoreCount, string? NameEn, string? NameHe,
        int? ChoreCategoryId, int? DefaultWeightMinutes, IReadOnlyList<string> EligibilityChipKeys);
    // EligibilityChipKeys ∈ {"Elig_GenderMale","Elig_GenderFemale","Elig_Officer"} — resolved in the view.
    public List<CategoryOption> CategoryOptions { get; set; } = new();    // reuse Users' CategoryOption shape or a local (Id,Name) record
    public record CategoryOption(int Id, string Name);

    // Create-form additions
    [BindProperty] public int? ChoreTypeCategoryId { get; set; }
    [BindProperty] public int CreateWeightHours { get; set; }
    [BindProperty] public int CreateWeightMinutes { get; set; }

    // Edit-form additions
    [BindProperty] public int? EditCategoryId { get; set; }
    [BindProperty] public int EditWeightHours { get; set; }
    [BindProperty] public int EditWeightMinutes { get; set; }
    [BindProperty] public int? EditRequiredGender { get; set; }   // 1=Male, 2=Female, null/0=none
    [BindProperty] public bool EditRequiresOfficer { get; set; }
```

(c) In `LoadDataAsync`, populate `CategoryOptions` from `_choreCategoryService.GetCategoriesForMoleculeAsync(MoleculeId.Value)` (active only), and for each type load its rules to derive chip keys:

```csharp
            CategoryOptions = (await _choreCategoryService.GetCategoriesForMoleculeAsync(MoleculeId.Value))
                .Select(c => new CategoryOption(c.Id, c.DisplayName)).ToList();
            // per-type chips
            foreach (var ctVm in ChoreTypes) { /* call _eligibilityAdmin.GetRulesForChoreTypeAsync(ctVm.Id) and map */ }
```
Build `EligibilityChipKeys` by mapping each rule: `RequiresGender + GenderValue==Male → "Elig_GenderMale"`, `Female → "Elig_GenderFemale"`, `RequiresOfficerRank → "Elig_Officer"`. (Restructure the existing `ChoreTypes = choreTypes.Select(...)` so the VM includes `ct.ChoreCategoryId`, `ct.DefaultWeightMinutes`, and the chip list — do the rule load in the same loop.)

(d) **Weight helper** (private static): `static int? ToWeightMinutes(int hours, int minutes) { var total = hours * 60 + minutes; return total > 0 ? total : (int?)null; }` (null → type carries no default → chore-create falls to 480).

(e) Extend `OnPostCreateAsync`: after the existing `CreateAsync(...)` returns `choreType`, set category + weight:

```csharp
        if (ChoreTypeCategoryId.HasValue)
            await _choreCategoryService.AssignChoreTypeAsync(choreType.Id, ChoreTypeCategoryId.Value);  // service rejects cross-molecule
        var createWeight = ToWeightMinutes(CreateWeightHours, CreateWeightMinutes);
        if (createWeight.HasValue)
        {
            // ChoreType is molecule-scoped; set DefaultWeightMinutes directly via the tracked entity.
            var ctEntity = await _db.ChoreTypes.IgnoreQueryFilters().FirstAsync(x => x.Id == choreType.Id);
            ctEntity.DefaultWeightMinutes = createWeight;
            await _db.SaveChangesAsync();
        }
```

(f) Extend `OnPostUpdateAsync` (after the existing `UpdateAsync(...)` succeeds, re-fetch `existing` which is already loaded + IDOR-checked):

```csharp
        await _choreCategoryService.AssignChoreTypeAsync(EditId, EditCategoryId);   // null clears the category
        var editWeight = ToWeightMinutes(EditWeightHours, EditWeightMinutes);
        var ctEntity = await _db.ChoreTypes.IgnoreQueryFilters().FirstAsync(x => x.Id == EditId);
        ctEntity.DefaultWeightMinutes = editWeight;
        await _db.SaveChangesAsync();

        // Replace-semantics eligibility rules. null/0 gender clears the gender rule.
        Gender? reqGender = EditRequiredGender switch { 1 => Gender.Male, 2 => Gender.Female, _ => null };
        await _eligibilityAdmin.SetRulesForChoreTypeAsync(EditId, reqGender, EditRequiresOfficer, currentUserIdForAudit);
        // Audit: log only WHICH rules (gender/officer present), never any user/reason text.
        await _auditLogService.LogAsync("ChoreTypeEligibilityUpdated", "ChoreType", EditId,
            $"Set eligibility (gender={reqGender?.ToString() ?? "none"}, officer={EditRequiresOfficer})");
```
(`currentUserIdForAudit` — parse the NameIdentifier claim as the page already does in `OnPostCreateAsync`.)

> `using ShiftManager.Models.Support;` for `Gender` — add to the .cs `using` block.

- [ ] **Step 3: Markup.**

In the **create form** `.form-grid`, add a category `<select>`, a weight dual-input, and an eligibility fieldset (the create form sets category + weight; full eligibility is most natural on the edit row, but expose a minimal officer/gender there too — or defer eligibility to edit-row only and note it). **Decision:** put **category + weight** on the create form; put **category + weight + full eligibility fieldset** on the edit row (a new type rarely needs eligibility at creation; editing is the type-centric mental model per §7.2). Flag this split inline.

Category select (create form):
```html
<div class="form-group">
    <label class="form-label"><loc key="ChoreCategory" /></label>
    <select class="form-select" asp-for="ChoreTypeCategoryId">
        <option value=""><loc key="Uncategorized" /></option>
        @foreach (var c in Model.CategoryOptions)
        { <option value="@c.Id">@c.Name</option> }
    </select>
</div>
<div class="form-group">
    <label class="form-label"><loc key="DefaultWeight" /></label>
    <div style="display:flex; gap:0.5rem; align-items:center;">
        <input class="form-input" type="number" asp-for="CreateWeightHours" min="0" max="24" style="width:5rem;" />
        <span><loc key="Hours_Short" /></span>
        <input class="form-input" type="number" asp-for="CreateWeightMinutes" min="0" max="59" style="width:5rem;" />
        <span><loc key="Minutes_Short" /></span>
    </div>
    <small style="color:var(--text-muted);"><loc key="DefaultWeightHint" /></small>
</div>
```

Edit-row additions (inside the existing edit `<form>`'s `.edit-form-grid`) — category select, weight dual input (prefill from `@(ct.DefaultWeightMinutes/60)` / `@(ct.DefaultWeightMinutes%60)`), plus an **Eligibility fieldset**:
```html
<fieldset class="form-group" style="border:1px solid var(--border); border-radius:8px; padding:0.75rem; min-width:240px;">
    <legend style="font-size:0.8125rem; color:var(--text-muted);"><loc key="Eligibility" /></legend>
    <div style="display:flex; gap:1rem; flex-wrap:wrap; align-items:center;">
        <label style="display:inline-flex; gap:0.35rem; align-items:center;">
            <input type="radio" name="EditRequiredGender" value="0" checked /> <loc key="Elig_GenderAny" />
        </label>
        <label style="display:inline-flex; gap:0.35rem; align-items:center;">
            <input type="radio" name="EditRequiredGender" value="1" /> <loc key="Elig_GenderMale" />
        </label>
        <label style="display:inline-flex; gap:0.35rem; align-items:center;">
            <input type="radio" name="EditRequiredGender" value="2" /> <loc key="Elig_GenderFemale" />
        </label>
        <label style="display:inline-flex; gap:0.35rem; align-items:center;">
            <input type="checkbox" name="EditRequiresOfficer" value="true" /> <loc key="Elig_RequiresOfficer" />
        </label>
    </div>
</fieldset>
```
> **Prefill the radios/checkbox** from the loaded rules: the loop in (c) already knows each type's chips — pass them to the row (e.g. `ct.EligibilityChipKeys.Contains("Elig_GenderFemale")`) and set `checked` accordingly. Because `name="EditRequiredGender"` repeats per row, only the OPEN edit row's form is submitted (the rows are separate `<form>`s) — verified safe (each `.edit-row` wraps its own form, exactly like the existing ChoreType edit row).

Eligibility **reason chips** in the type table — add a column header `<th><loc key="Eligibility" /></th>` and a cell rendering chips:
```html
<td>
    @foreach (var key in ct.EligibilityChipKeys)
    { <span class="elig-chip elig-chip--@(key.Contains("Officer") ? "officer" : "gender")">@Localizer[key]</span> }
    @if (!ct.EligibilityChipKeys.Any()) { <span style="color:var(--text-muted);">-</span> }
</td>
```
(Update the existing `<td colspan="9">` on the edit row to the new column count.)

- [ ] **Step 4: CSS** — add `.elig-chip` to the page `@section Styles` (sensitive≠alarming: muted, bordered; pair bg+text+`!important`; dark redeclares ONLY color):
```css
.elig-chip { display:inline-flex; align-items:center; padding:0.15rem 0.5rem; margin:0 0.2rem 0.2rem 0; border-radius:9999px;
    font-size:0.7rem; font-weight:600; border:1px solid var(--border); }
.elig-chip--gender { background: var(--info-soft); color: var(--info) !important; }
.elig-chip--officer { background: var(--warning-soft); color: var(--warning-text) !important; }
:root[data-theme="dark"] .elig-chip--gender { color: var(--info) !important; }
:root[data-theme="dark"] .elig-chip--officer { color: var(--warning-text) !important; }
```
> Confirm `--info-soft`/`--warning-soft`/`--warning-text` exist (GREP `:root` in the global CSS); if `--warning-text` is undefined, fall back to `#7a5b00` light / `#ffd479` dark. Do NOT introduce an undefined var.

- [ ] **Step 5: Build + run handler test** → pass.
- [ ] **Step 6: Browser-verify** the editor on **populated** data (per MEMORY: empty data hid a contrast bug) in **light/dark/RTL**: category dropdown lists Task-3 categories; weight prefills h+m; eligibility radios/checkbox prefill from saved rules; saving a Female+officer rule shows two chips; chips are readable (run the contrast check). 
- [ ] **Step 7: Commit** `feat(chores-ui): promoted ChoreType editor (category + weight + eligibility)`.

---

## Task 5 — `ChoreTypes/Index`: exemptions (AJAX via `IChoreEligibilityAdminService`)

**Files:**
- Modify: `Index.cshtml.cs` (`[IgnoreAntiforgeryToken]`? — NO: keep antiforgery; send the token header like `OnPostUserCategoriesAsync`. Add `OnGetExemptionsAsync`, `OnPostAddExemptionAsync`, `OnPostRemoveExemptionAsync`; a candidate-users query)
- Modify: `Index.cshtml` (a "Manage exemptions" button per type-row → a `<dialog>` exemption manager; JS IIFE)
- Create: `ShiftManager.Tests/UnitTests/Pages/ChoreTypesExemptionHandlerTests.cs`

Type-centric (§7.2): each chore-type row gets an "Exemptions" button opening a dialog that lists current waivers (user + optional reason) and an add form (user picker + optional reason). **Audit must NOT log the reason text** (sensitive).

- [ ] **Step 1: Failing handler test** — `OnPostAddExemptionAsync({choreTypeId, userId, reason})` by an EditChoreTypes-authorized admin for an accessible molecule adds a waiver; the audit description contains NO substring of the reason; remove deletes it; a caller without molecule access is rejected 403.

- [ ] **Step 2: Handlers** `Index.cshtml.cs` (mirror the `[FromBody]` JSON shape of `Users.OnPostUserCategoriesAsync`):

```csharp
    public class ExemptionRequest { public int ChoreTypeId { get; set; } public int UserId { get; set; } public string? Reason { get; set; } }

    public async Task<IActionResult> OnGetExemptionsAsync(int choreTypeId)
    {
        var ct = await _choreTypeService.GetByIdAsync(choreTypeId);
        if (ct == null || !await IsUserAuthorizedForMoleculeAsync(ct.MoleculeId))
            return new JsonResult(new { ok = false }) { StatusCode = 403 };

        var exemptions = await _eligibilityAdmin.GetExemptionsForChoreTypeAsync(choreTypeId);
        var userIds = exemptions.Select(e => e.UserId).ToList();
        var names = await _db.Users.IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        // Candidate users for the add-picker: active Standard users in this type's molecule, not already exempt.
        var moleculeCompanyIds = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == ct.MoleculeId).Select(c => c.Id).ToListAsync();
        var candidates = await _db.Users.IgnoreQueryFilters()
            .Where(u => moleculeCompanyIds.Contains(u.CompanyId) && u.IsActive && !userIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .Select(u => new { id = u.Id, name = u.DisplayName }).ToListAsync();

        return new JsonResult(new
        {
            ok = true,
            exemptions = exemptions.Select(e => new { e.UserId, name = names.GetValueOrDefault(e.UserId, "?"), e.Reason }),
            candidates
        });
    }

    public async Task<IActionResult> OnPostAddExemptionAsync([FromBody] ExemptionRequest req)
    {
        var ct = await _choreTypeService.GetByIdAsync(req.ChoreTypeId);
        if (ct == null || !await IsUserAuthorizedForMoleculeAsync(ct.MoleculeId))
            return new JsonResult(new { success = false }) { StatusCode = 403 };
        if (!int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var actor))
            return new JsonResult(new { success = false }) { StatusCode = 403 };

        var added = await _eligibilityAdmin.AddExemptionAsync(req.UserId, req.ChoreTypeId, req.Reason, actor);
        if (added == null)
            return new JsonResult(new { success = false, error = _localizer["Error_FailedToUpdate"].Value });

        // SECURITY: never log the reason text (sensitive PII). Log only the (user, type) pair.
        await _auditLogService.LogAsync("ChoreExemptionAdded", "ChoreType", req.ChoreTypeId,
            $"Added chore exemption: user {req.UserId} on chore type {req.ChoreTypeId}");
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostRemoveExemptionAsync([FromBody] ExemptionRequest req)
    {
        var ct = await _choreTypeService.GetByIdAsync(req.ChoreTypeId);
        if (ct == null || !await IsUserAuthorizedForMoleculeAsync(ct.MoleculeId))
            return new JsonResult(new { success = false }) { StatusCode = 403 };

        var removed = await _eligibilityAdmin.RemoveExemptionAsync(req.UserId, req.ChoreTypeId);
        if (removed)
            await _auditLogService.LogAsync("ChoreExemptionRemoved", "ChoreType", req.ChoreTypeId,
                $"Removed chore exemption: user {req.UserId} on chore type {req.ChoreTypeId}");
        return new JsonResult(new { success = removed });
    }
```

- [ ] **Step 3: Markup** — add an "Exemptions" button to each type row's `.table-actions`:
```html
<button type="button" class="btn btn-ghost js-exemptions" data-chore-type-id="@ct.Id" data-type-name="@ct.DisplayName">@Localizer["Exemptions"]</button>
```
Add a single shared `<dialog id="exemptionsDialog">` (clone `userCategoriesDialog`'s structure from `Users.cshtml:1459`): a header, a `<p id="exDialogTypeName">`, a `<div id="exList">` (current waivers, each with a remove button), and an add row (a `<select id="exUserPicker">` + an optional `<input id="exReason" maxlength="200">` + an Add button). Reason input has a hint: `<loc key="ExemptionReasonHint" />` ("not stored in audit logs").

- [ ] **Step 4: JS** — a new IIFE in `@section Scripts` (do NOT reuse the `.js-edit-toggle` IIFE; this is separate). Token helper mirrors `Users.cshtml`'s `token()`. On `.js-exemptions` click → `fetch('?handler=Exemptions&choreTypeId=' + id)`, render list + candidates; Add → POST `?handler=AddExemption` with `{ChoreTypeId,UserId,Reason}` + `RequestVerificationToken` header; Remove → POST `?handler=RemoveExemption`. On success re-fetch the list (don't full-reload — keeps the dialog open). Use `window.FeedbackModal.show('error', ...)` on failure (per MEMORY: no native alert in production).

> **Antiforgery:** this page has no form emitting `__RequestVerificationToken` at page scope unless a `<form>` is rendered. The category create form (Task 3) emits `@Html.AntiForgeryToken()`, so a token input exists. If categories are empty, add a standalone `@Html.AntiForgeryToken()` near the dialog so `token()` always finds one. Confirm the token query selector matches.

- [ ] **Step 5: Build + run handler test** (assert audit description has no reason substring) → pass.
- [ ] **Step 6: Browser-verify** in **light/dark/RTL**: open exemptions for a type, add a user with a reason, confirm it lists; remove it; confirm reason hint visible; verify (server log / audit table) the reason is absent from the audit row.
- [ ] **Step 7: Commit** `feat(chores-ui): per-type chore exemption manager (AJAX, reason not audited)`.

---

## Task 6 — `Admin/Users`: DoesChores toggle + impact + chore-category multiselect

**Files:**
- Modify: `Users.cshtml.cs` (VM: `DoesChores`, `ChoreCategoryNames`, `ChoreCategoryIds`; load chore categories per molecule + memberships; `OnGetDoesChoresImpactAsync`, `OnPostDoesChoresAsync`, `OnPostUserChoreCategoriesAsync`; inject `IChoreCategoryService`)
- Modify: `Users.cshtml` (a `does-chores-cell` cloned from `does-shifts-cell`; a `choreCategoriesDialog`; a `doesChoresConfirmDialog`; JS IIFE)
- Create: `ShiftManager.Tests/UnitTests/Pages/UsersDoesChoresHandlerTests.cs`

Direct clone of the DoesShifts surface. Impact preview counts FUTURE chores (`Chore.Date >= today && CanceledAt == null`); OFF prompts keep-or-cancel (mirror `doesShiftsConfirmDialog` per §13.2 / RESOLVED DECISIONS).

- [ ] **Step 1: Failing handler test** — `OnPostDoesChoresAsync(id, doesChores:true)` by an authorized admin sets `DoesChores=true`; with `doesChores:false, deleteFutureChores:true` it soft-cancels future chores (`CanceledAt` set); an unauthorized caller is rejected (rides `AuthorizeUserEditAsync`). `OnPostUserChoreCategoriesAsync` replaces memberships constrained to the user's molecule.

- [ ] **Step 2: PageModel.**

(a) Inject `IChoreCategoryService _choreCategoryService` (ctor + field).

(b) Extend `UserVM` (line 83) with three fields (append, defaults keep existing call sites compiling — but both `userList.Add` sites must pass them; do it):
```csharp
    bool DoesChores = false, string? ChoreCategoryNames = null, List<int>? ChoreCategoryIds = null
```
Plus a `Dictionary<int, List<CategoryOption>> AvailableChoreCategoriesByMolecule` (mirror `AvailableCategoriesByMolecule`).

(c) In `OnGetAsync`, after the shift-category load (`AvailableCategoriesByMolecule`, ~line 439), load chore categories the same way from `_db.ChoreCategories`; and after `userCategoryRows` (~line 524) batch-load `UserChoreCategories` into a `userChoreCategoryMap` (Ids + Names). Pass `u.DoesChores` + the chore-category names/ids into BOTH `userList.Add(new UserVM(...))` calls (the Director branch ~line 711 and the non-Director branch ~line 746).

(d) Handlers (clone `OnGetDoesShiftsImpactAsync`/`OnPostDoesShiftsAsync`/`OnPostUserCategoriesAsync`, swapping ShiftAssignment→Chore, ShiftCategory→ChoreCategory, `DoesShifts`→`DoesChores`):

```csharp
    public async Task<IActionResult> OnGetDoesChoresImpactAsync(int id)
    {
        var (ok, _, _, error) = await AuthorizeUserEditAsync(id);
        if (!ok) return new JsonResult(new { ok = false, error });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureChores = await _db.Chores.IgnoreQueryFilters()
            .CountAsync(c => c.UserId == id && c.Date >= today && c.CanceledAt == null);
        return new JsonResult(new { ok = true, futureChores });
    }

    public async Task<IActionResult> OnPostDoesChoresAsync(int id, bool doesChores, bool deleteFutureChores = false)
    {
        var (ok, u, currentUserId, error) = await AuthorizeUserEditAsync(id);
        if (!ok) { TempData["ErrorMessage"] = error; return RedirectToPage(); }

        u!.DoesChores = doesChores;
        int canceled = 0;
        if (!doesChores && deleteFutureChores)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;
            var future = await _db.Chores.IgnoreQueryFilters()
                .Where(c => c.UserId == id && c.Date >= today && c.CanceledAt == null).ToListAsync();
            foreach (var c in future) { c.CanceledAt = now; c.CanceledBy = currentUserId; }
            canceled = future.Count;
        }

        var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(() => _db.SaveChangesAsync(), "AppUser", id);
        if (!saveResult.Success) { TempData["ErrorMessage"] = _localizer["Error_ConcurrencyConflict"].Value; return RedirectToPage(); }

        await _auditLogService.LogUserActionAsync(userId: currentUserId, action: "DoesChoresChanged",
            entityType: "User", entityId: u.Id,
            description: $"Set DoesChores={doesChores} for {u.DisplayName}" + (canceled > 0 ? $"; canceled {canceled} future chore(s)" : ""));

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture,
            (doesChores ? _localizer["Users_DoesChoresOn"] : _localizer["Users_DoesChoresOff"]).Value, u.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUserChoreCategoriesAsync([FromBody] UserChoreCategoriesRequest request)
    {
        var (ok, u, currentUserId, error) = await AuthorizeUserEditAsync(request.UserId);
        if (!ok) return new JsonResult(new { success = false, error }) { StatusCode = 403 };

        var moleculeId = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == u!.CompanyId).Select(c => c.MoleculeId).FirstOrDefaultAsync();
        var requested = request.CategoryIds ?? new List<int>();
        var valid = moleculeId == null ? new List<int>()
            : await _db.ChoreCategories.Where(c => c.MoleculeId == moleculeId && requested.Contains(c.Id))
                .Select(c => c.Id).ToListAsync();

        await _choreCategoryService.SetUserCategoriesAsync(u!.Id, valid);
        await _auditLogService.LogUserActionAsync(userId: currentUserId, action: "UserChoreCategoriesChanged",
            entityType: "User", entityId: u.Id, description: $"Set chore categories for {u.DisplayName} to [{string.Join(",", valid)}]");
        return new JsonResult(new { success = true, count = valid.Count });
    }

    public class UserChoreCategoriesRequest { public int UserId { get; set; } public List<int>? CategoryIds { get; set; } }
```
> `Chore.CanceledBy` is an `int?` — confirmed by the existing account-type cleanup (`Users.cshtml.cs:1605` sets `c.CanceledBy = auditCurrentUserId`).

- [ ] **Step 3: Markup** — add a NEW `<td class="does-chores-cell">` column (clone `does-shifts-cell` at `Users.cshtml:1014-1037`, swapping handler `DoesChores`, classes `js-does-chores`/`js-edit-chore-cats`/`js-does-chores-form`, data `data-selected="@string.Join(",", u.ChoreCategoryIds ?? new())"`, names `Users_NoChoreCategories`). Add the matching `<th>` in the header. Add two dialogs cloning `doesShiftsConfirmDialog` (`:1446`) → `doesChoresConfirmDialog` and `userCategoriesDialog` (`:1459`) → `choreCategoriesDialog`. Emit the chore-category options JSON: `<script>window.__choreCategoriesByMolecule = @Json.Serialize(Model.AvailableChoreCategoriesByMolecule);</script>`.

- [ ] **Step 4: JS** — clone the DoesShifts/category IIFE (`Users.cshtml:1640-1754`) into a NEW IIFE using `window.__choreCategoriesByMolecule`, handler names `DoesChoresImpact`/`DoesChores`/`UserChoreCategories`, prompt key `Users_DoesChoresOffPrompt`, and the chore dialogs. Keep it a separate IIFE (function-reference gate: do not call the shift IIFE's locals).

- [ ] **Step 5: CSS** — reuse `.does-shifts-cell*` rules by adding `.does-chores-cell` to the same selectors (e.g. `.does-shifts-cell, .does-chores-cell { min-width:130px; }`), OR add `.does-chores-cell` mirrors. No new visual tokens.

- [ ] **Step 6: Build + run handler test** → pass.
- [ ] **Step 7: Browser-verify** at `http://localhost:5000/Admin/Users` in **light/dark/RTL**: a DoesChores toggle sits next to DoesShifts; turning OFF a user with future chores prompts keep/cancel; the chore-category dialog lists Task-3 categories and saves.
- [ ] **Step 8: Commit** `feat(chores-ui): Admin/Users DoesChores toggle + impact + chore-category multiselect`.

---

## Task 7 — `Admin/Users`: Gender editor (badge + dialog; rides `AuthorizeUserEditAsync`; 3-state; audited)

**Files:**
- Modify: `Users.cshtml.cs` (VM: `Gender`; expose `AvailableGenders`; `OnPostGenderAsync`)
- Modify: `Users.cshtml` (a Gender editable-cell — badge + dialog/inline select, cloning the AccountType cell at `:991-1012`)
- Create: `ShiftManager.Tests/UnitTests/Pages/UsersGenderHandlerTests.cs`

Per RESOLVED DECISIONS: visible+editable by ANY user-editor (rides `AuthorizeUserEditAsync` — AdminAccess/EditCompanyUsers); NO new grant; 3-state {Unspecified, Male, Female}; value change audited; gender mismatch is a downstream warning (not this page's concern).

- [ ] **Step 1: Failing handler test** — `OnPostGenderAsync(id, (int)Gender.Female)` by an authorized editor persists `Gender=Female` + writes an audit row; an unauthorized caller is rejected and no change persists; an out-of-range value is rejected.

- [ ] **Step 2: PageModel.**

(a) Add `Gender Gender` to `UserVM` (append, default `Gender.Unspecified`) and pass `u.Gender` in both `userList.Add` calls. Add `using ShiftManager.Models.Support;` (already present at `Users.cshtml.cs:10`).

(b) Expose options for the select:
```csharp
    public record GenderOption(int Value, string Label);
    public List<GenderOption> AvailableGenders { get; set; } = new();
    // in OnGetAsync, after AvailableAccountTypes:
    AvailableGenders = new()
    {
        new((int)Gender.Unspecified, _localizer["Gender_Unspecified"].Value),
        new((int)Gender.Male,        _localizer["Gender_Male"].Value),
        new((int)Gender.Female,      _localizer["Gender_Female"].Value),
    };
```

(c) Handler (clone `OnPostAccountTypeAsync` structure but gate via `AuthorizeUserEditAsync` — the shared edit gate — NOT a bespoke grant check; the spec says it rides the existing user-edit authz):
```csharp
    public async Task<IActionResult> OnPostGenderAsync(int id, int gender)
    {
        var (ok, u, currentUserId, error) = await AuthorizeUserEditAsync(id);
        if (!ok) { TempData["ErrorMessage"] = error; return RedirectToPage(); }
        if (!Enum.IsDefined(typeof(Gender), gender))
        { TempData["ErrorMessage"] = _localizer["Error_InvalidValue"].Value; return RedirectToPage(); }

        var oldGender = u!.Gender;
        u.Gender = (Gender)gender;
        var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(() => _db.SaveChangesAsync(), "AppUser", id);
        if (!saveResult.Success) { TempData["ErrorMessage"] = _localizer["Error_ConcurrencyConflict"].Value; return RedirectToPage(); }

        // Sensitive: log the value change (consistent with DateOfBirth/Phone mutations) — value itself is low-sensitivity vs reason text.
        await _auditLogService.LogUserActionAsync(userId: currentUserId, action: "GenderChanged",
            entityType: "User", entityId: u.Id, description: $"Changed gender for {u.DisplayName} from {oldGender} to {(Gender)gender}");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Users_GenderUpdated"].Value, u.DisplayName);
        return RedirectToPage();
    }
```

- [ ] **Step 3: Markup** — add a Gender editable-cell + `<th>` (clone the AccountType cell `:991-1012`, handler `Gender`, name `gender`, options from `Model.AvailableGenders`). Render the display value as a `.gender-badge` (muted, bordered):
```html
<td class="editable-cell" data-cell-type="gender" data-user-id="@u.Id">
    <div class="cell-display">
        <span class="cell-display__value gender-badge gender-badge--@(u.Gender.ToString().ToLowerInvariant())">@Localizer[$"Gender_{u.Gender}"]</span>
        <button type="button" class="cell-display__edit-btn" onclick="enterEditMode(this)" title="@Localizer["Users_Gender"]" aria-label="@Localizer["Users_Gender"]"><icon name="settings" /></button>
    </div>
    <div class="cell-edit">
        <form method="post" asp-page-handler="Gender" style="display:inline; margin:0;">
            @Html.AntiForgeryToken()
            <input type="hidden" name="id" value="@u.Id" />
            <select class="form-input cell-edit__select" name="gender" data-original-value="@((int)u.Gender)" aria-label="@Localizer["Users_Gender"]">
                @foreach (var g in Model.AvailableGenders)
                { <option value="@g.Value" selected="@((int)u.Gender == g.Value)">@g.Label</option> }
            </select>
            <button type="submit" class="cell-edit__btn cell-edit__btn--save" title="@Localizer["Save"]" aria-label="@Localizer["Save"]"><icon name="check" /></button>
            <button type="button" class="cell-edit__btn cell-edit__btn--cancel" onclick="exitEditMode(this)" title="@Localizer["Cancel"]" aria-label="@Localizer["Cancel"]"><icon name="x" /></button>
        </form>
    </div>
</td>
```
> Reuses the page's existing `enterEditMode`/`exitEditMode` helpers (already used by Role/JobType/AccountType cells) — function-reference gate satisfied (same page, same scope).

- [ ] **Step 4: CSS** — `.gender-badge` (sensitive≠alarming; muted bg + explicit text + `!important`; dark redeclares only color):
```css
.gender-badge { display:inline-flex; align-items:center; padding:0.1rem 0.5rem; border-radius:9999px; font-size:0.75rem; font-weight:600; border:1px solid var(--border); }
.gender-badge--unspecified { background: var(--surface-soft); color: var(--text-muted) !important; }
.gender-badge--male   { background: var(--info-soft);    color: var(--info) !important; }
.gender-badge--female { background: var(--primary-soft); color: var(--primary) !important; }
:root[data-theme="dark"] .gender-badge--male   { color: var(--info) !important; }
:root[data-theme="dark"] .gender-badge--female { color: var(--primary) !important; }
```
> GREP `--primary-soft`/`--info-soft` in the global CSS first; substitute an existing soft token if absent. Never an undefined var.

- [ ] **Step 5: Build + run handler test** → pass.
- [ ] **Step 6: Browser-verify** in **light/dark/RTL**: Gender badge shows "Unspecified" for a new user; editing to Female persists + shows the badge; badge readable in both themes (contrast check). Confirm an audit row "GenderChanged" exists.
- [ ] **Step 7: Commit** `feat(users): gender editor (3-state, rides user-edit authz, audited)`.

---

## Task 8 — `ChoreTemplates/Index` page (NEW) + Stamp modal

**Files:**
- Create: `Pages/Admin/Organization/ChoreTemplates/Index.cshtml`, `Index.cshtml.cs`
- Modify: `Pages/Admin/Organization/Index.cshtml` (add the nav link after the ChoreTypes link `:682-684`)
- Create: `ShiftManager.Tests/UnitTests/Pages/ChoreTemplatesStampHandlerTests.cs`

Clone the ChoreTypes skeleton: `[Authorize(Policy="Grant:EditChoreTypes")]`, the molecule selector, `IsUserAuthorizedForMoleculeAsync`, create-form + `.data-table` + `.edit-row`. Template fields: Name, ChoreType `<select>` (optional → free-text), DefaultTitle, StartTime/EndTime (`<input type="time">`), WeightMinutesOverride (h+m dual input → minutes), Notes, IsActive. The **Stamp modal** collects a date range + weekday chips + assignee multiselect + a rotate toggle, POSTs to `OnPostStampAsync`, then renders a result view (created/skipped with localized reason keys).

- [ ] **Step 1: Failing handler test** `ChoreTemplatesStampHandlerTests.cs` — construct the page model with `IChoreTemplateService` + a real/stub `IChoreService`. Assert: `OnPostStampAsync` for an accessible-molecule template with `rotate=false`, two assignees, a 1-day range returns a JSON result with `created==2, skipped==0`; an inaccessible molecule is rejected 403; CRUD handlers persist via `IChoreTemplateService`. (The deep stamp behavior — rotate, skip-on-hard-error, one-per-day — is already covered by Phase 2's `ChoreWeightAndStampTests`; here assert only the handler wiring + IDOR.)

- [ ] **Step 2: PageModel** `Index.cshtml.cs` — mirror `ChoreTypes/IndexModel`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.ChoreTemplates;

// SECURITY-AUDITED: All IgnoreQueryFilters() are SAFE — gated by Grant:EditChoreTypes; templates are
// molecule-scoped config; every handler re-verifies the molecule via IsUserAuthorizedForMoleculeAsync.
[Authorize(Policy = "Grant:EditChoreTypes")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IChoreTemplateService _templateService;
    private readonly IChoreService _choreService;
    private readonly IChoreTypeService _choreTypeService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(IStringLocalizer<SharedResources> localizer, AppDbContext db,
        IChoreTemplateService templateService, IChoreService choreService, IChoreTypeService choreTypeService,
        IGrantService grantService, ICompanyContext companyContext, IAuditLogService auditLogService) : base(localizer)
    { _db = db; _templateService = templateService; _choreService = choreService; _choreTypeService = choreTypeService;
      _grantService = grantService; _companyContext = companyContext; _auditLogService = auditLogService; }

    public record TemplateVM(int Id, string Name, int? ChoreTypeId, string? ChoreTypeName, string DefaultTitle,
        TimeOnly? StartTime, TimeOnly? EndTime, int? WeightMinutesOverride, string? Notes, bool IsActive);
    public record MoleculeOption(int Id, string Name);
    public record ChoreTypeOption(int Id, string Name);
    public record AssigneeOption(int Id, string Name);

    public List<TemplateVM> Templates { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();
    public List<ChoreTypeOption> ChoreTypeOptions { get; set; } = new();
    public List<AssigneeOption> AssigneeOptions { get; set; } = new();   // active Standard users in molecule (roster for stamp)

    [BindProperty(SupportsGet = true)] public int? MoleculeId { get; set; }

    // Create form
    [BindProperty] public string TemplateName { get; set; } = string.Empty;
    [BindProperty] public int? TemplateChoreTypeId { get; set; }
    [BindProperty] public string TemplateDefaultTitle { get; set; } = string.Empty;
    [BindProperty] public TimeOnly? TemplateStartTime { get; set; }
    [BindProperty] public TimeOnly? TemplateEndTime { get; set; }
    [BindProperty] public int TemplateWeightHours { get; set; }
    [BindProperty] public int TemplateWeightMinutes { get; set; }
    [BindProperty] public string? TemplateNotes { get; set; }

    // Edit form (mirror create, prefixed Edit*; + EditId)
    [BindProperty] public int EditId { get; set; }
    // ... EditName/EditChoreTypeId/EditDefaultTitle/EditStartTime/EditEndTime/EditWeightHours/EditWeightMinutes/EditNotes ...

    public async Task OnGetAsync() => await LoadDataAsync();

    private async Task LoadDataAsync()
    {
        // EXACT clone of ChoreTypes.LoadDataAsync molecule resolution (GetAccessibleMoleculeIdsForGrantAsync("EditChoreTypes")
        // + user's own molecule fallback + MoleculeId defaulting + accessibility validation). Then:
        if (MoleculeId.HasValue)
        {
            var templates = await _templateService.GetTemplatesForMoleculeAsync(MoleculeId.Value, includeInactive: true);
            var types = await _choreTypeService.GetChoreTypesForMoleculeAsync(MoleculeId.Value);
            var typeNames = types.ToDictionary(t => t.Id, t => t.DisplayName);
            ChoreTypeOptions = types.Select(t => new ChoreTypeOption(t.Id, t.DisplayName)).ToList();
            Templates = templates.Select(t => new TemplateVM(t.Id, t.Name, t.ChoreTypeId,
                t.ChoreTypeId.HasValue ? typeNames.GetValueOrDefault(t.ChoreTypeId.Value) : null,
                t.DefaultTitle, t.StartTime, t.EndTime, t.WeightMinutesOverride, t.Notes, t.IsActive)).ToList();

            // Stamp assignee roster: active Standard users in this molecule (display roster; Busy gate enforces real authz).
            var companyIds = await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.MoleculeId == MoleculeId.Value).Select(c => c.Id).ToListAsync();
            AssigneeOptions = await _db.Users.IgnoreQueryFilters()
                .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive
                    && u.AccountType == ShiftManager.Models.Support.AccountType.Standard)
                .OrderBy(u => u.DisplayName)
                .Select(u => new AssigneeOption(u.Id, u.DisplayName)).ToListAsync();
        }
    }

    private async Task<bool> IsUserAuthorizedForMoleculeAsync(int moleculeId)
    { /* EXACT clone of ChoreTypes/Index.cshtml.cs IsUserAuthorizedForMoleculeAsync */ return true; }

    private static int? ToWeightMinutes(int h, int m) { var t = h*60 + m; return t > 0 ? t : (int?)null; }

    // CRUD handlers: OnPostCreateAsync / OnPostUpdateAsync / OnPostActivateAsync / OnPostDeactivateAsync / OnPostDeleteAsync
    // — each validates MoleculeId/template molecule via IsUserAuthorizedForMoleculeAsync, calls _templateService, audits, RedirectToPage(new { MoleculeId }).

    public class StampRequest
    {
        public int TemplateId { get; set; }
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public List<int>? Weekdays { get; set; }   // 0=Sunday..6=Saturday (DayOfWeek ints)
        public List<int>? AssigneeIds { get; set; }
        public bool Rotate { get; set; }
    }

    public async Task<IActionResult> OnPostStampAsync([FromBody] StampRequest req)
    {
        var template = await _templateService.GetByIdAsync(req.TemplateId);
        if (template == null || !await IsUserAuthorizedForMoleculeAsync(template.MoleculeId))
            return new JsonResult(new { success = false }) { StatusCode = 403 };
        if (req.AssigneeIds == null || req.AssigneeIds.Count == 0 || req.From > req.To)
            return new JsonResult(new { success = false, error = _localizer["Error_StampInvalidInput"].Value });

        var weekdays = (req.Weekdays ?? new List<int>()).Select(d => (DayOfWeek)d).ToList();
        var result = await _choreService.StampTemplateAsync(req.TemplateId, req.From, req.To, weekdays, req.AssigneeIds, req.Rotate);

        await _auditLogService.LogAsync("ChoreTemplateStamped", "ChoreTemplate", req.TemplateId,
            $"Stamped template {req.TemplateId}: created {result.CreatedCount}, skipped {result.SkippedCount}");

        // Resolve names for the result table; map each StampSkipped.ReasonKey through the localizer in the view (here pass raw key).
        var ids = result.Created.Select(c => c.UserId).Concat(result.Skipped.Select(s => s.UserId)).Distinct().ToList();
        var names = await _db.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        return new JsonResult(new
        {
            success = true,
            created = result.CreatedCount,
            skipped = result.SkippedCount,
            createdRows = result.Created.Select(c => new { date = c.Date.ToString("yyyy-MM-dd"), user = names.GetValueOrDefault(c.UserId, "?") }),
            skippedRows = result.Skipped.Select(s => new { date = s.Date.ToString("yyyy-MM-dd"), user = names.GetValueOrDefault(s.UserId, "?"), reasonKey = s.ReasonKey })
        });
    }
}
```
> **Reason-key localization:** `StampSkipped.ReasonKey` is a raw validation key (e.g. `USER_NOT_IN_MOLECULE`, `Error_ChoreRequiresGenderFemale`, or `BUSY_OVERRIDE_REQUIRED`). The view's result JS looks each up in a small JS dictionary seeded from `@Localizer`; unknown keys fall back to a generic `Stamp_SkippedGeneric`. Enumerate the known keys in Task 9.

- [ ] **Step 3: Markup** `Index.cshtml` — clone the ChoreTypes page header/breadcrumb/styles/molecule-selector/create-card/table. Each template row gets a **Stamp** button (`.js-stamp` with `data-template-id`/`data-template-name`). Add ONE shared stamp `<dialog id="stampDialog">`:

```html
<dialog id="stampDialog" style="border:none; border-radius:0.75rem; padding:0; max-width:560px; width:92%; background:var(--surface); color:var(--text); box-shadow:0 10px 40px rgba(0,0,0,0.35);">
  <div style="padding:1.5rem;">
    <h2 class="section-header" style="margin-top:0;"><loc key="StampTemplate" />: <span id="stampTemplateName"></span></h2>
    <div id="stampForm">
      <div class="form-grid">
        <div class="form-group"><label class="form-label"><loc key="StampFrom" /></label><input class="form-input" type="date" id="stampFrom" /></div>
        <div class="form-group"><label class="form-label"><loc key="StampTo" /></label><input class="form-input" type="date" id="stampTo" /></div>
      </div>
      <div class="form-group">
        <label class="form-label"><loc key="StampWeekdays" /></label>
        <div id="stampWeekdays" style="display:flex; gap:0.35rem; flex-wrap:wrap;">
          @* 7 chips, value=DayOfWeek int 0..6, label from Weekday_Sun..Weekday_Sat *@
        </div>
        <small style="color:var(--text-muted);"><loc key="StampWeekdaysHint" /></small>
      </div>
      <div class="form-group">
        <label class="form-label"><loc key="StampAssignees" /></label>
        <div id="stampAssignees" style="max-height:200px; overflow-y:auto; border:1px solid var(--border); border-radius:8px; padding:0.5rem;">
          @foreach (var a in Model.AssigneeOptions)
          { <label style="display:flex; gap:0.5rem; align-items:center; padding:0.25rem;"><input type="checkbox" class="stamp-assignee" value="@a.Id" /> <span>@a.Name</span></label> }
        </div>
      </div>
      <label style="display:flex; gap:0.5rem; align-items:center; margin:0.75rem 0;">
        <input type="checkbox" id="stampRotate" />
        <span><loc key="StampRotate" /></span>
      </label>
      <small style="color:var(--text-muted);"><loc key="StampRotateHint" /></small>
      <div style="display:flex; gap:0.5rem; justify-content:flex-end; margin-top:1rem;">
        <button type="button" class="btn" id="stampCancelBtn"><loc key="Button_Cancel" /></button>
        <button type="button" class="btn btn-primary" id="stampSubmitBtn"><loc key="StampSubmit" /></button>
      </div>
    </div>
    <div id="stampResult" style="display:none;">
      <p id="stampResultSummary" style="font-weight:600;"></p>
      <div id="stampResultTable" style="max-height:280px; overflow:auto;"></div>
      <div style="display:flex; justify-content:flex-end; margin-top:1rem;">
        <button type="button" class="btn btn-primary" id="stampDoneBtn"><loc key="Button_Done" /></button>
      </div>
    </div>
  </div>
</dialog>
@Html.AntiForgeryToken()
<script>window.__stampReasonKeys = { /* key→localized, seeded in Task 9 */ };</script>
```

- [ ] **Step 4: JS** (one IIFE): `.js-stamp` click → reset the form view, set `stampTemplateName`, open dialog. Submit → gather from/to/weekdays(checked chips)/assignees(checked)/rotate → POST `?handler=Stamp` with the `RequestVerificationToken` header → on success hide `#stampForm`, show `#stampResult`, fill summary (`{created} created / {skipped} skipped`) + a table; map each `skippedRows[].reasonKey` via `window.__stampReasonKeys[key] || genericFallback`. Errors → `window.FeedbackModal.show('error', ...)`. Done/Cancel close the dialog (and `location.reload()` on Done so any created chores show if the admin navigates to the calendar).

- [ ] **Step 5: Nav link** `Pages/Admin/Organization/Index.cshtml` — after the ChoreTypes link (`:682-684`):
```html
<a href="/Admin/Organization/ChoreTemplates" class="btn btn-primary btn-sm">
    <loc key="ManageChoreTemplates" />
</a>
```

- [ ] **Step 6: Build + run handler test** → pass.
- [ ] **Step 7: Browser-verify** at `http://localhost:5000/Admin/Organization/ChoreTemplates?MoleculeId={id}` in **light/dark/RTL**: create a template; click Stamp; pick a 3-day range, 2 weekday chips, 2 assignees, toggle rotate; submit; the result view shows created/skipped with readable reason text; with `rotate` ON, created count ≈ matching-days (one per day) vs OFF ≈ days×assignees.
- [ ] **Step 8: Commit** `feat(chores-ui): ChoreTemplates page + Stamp modal (rotate/fan-out, result view)`.

---

## Task 9 — Localization (he + en) + contrast/RTL pass

**Files:**
- Modify: `Resources/SharedResources.resx` (en), `Resources/SharedResources.he-IL.resx` (he)

**GREP every key in `SharedResources.resx` BEFORE adding it** — reuse existing keys where present. Known reusable (already in resx, verified on ChoreTypes/Users pages): `Name`, `DisplayName`, `NameEnglish`, `NameHebrew`, `Color`, `SortOrder`, `Actions`, `Status`, `Active`, `Inactive`, `Edit`, `Save`, `Cancel`, `Delete`, `Button_Cancel`, `Button_Save`, `ChoreTypes`, `On`, `Off`, `Error_MoleculeRequired`, `Error_MoleculeNotFound`, `Error_UserNotFound`, `Error_InvalidUserId`, `Error_InvalidUserClaim`, `Error_NoPermissionForCompany`, `Error_ConcurrencyConflict`, `Error_FailedToUpdate`, `Users_AccountType`. Do NOT re-add these.

- [ ] **Step 1: Add NEW keys** (en + he). Consolidated list:

| Key | en | he |
|---|---|---|
| `ChoreCategories` | Chore Categories | קטגוריות תורנויות |
| `ChoreCategoriesInfo` | Functional groups above chore types (e.g. Physical, Computer). | קבוצות פונקציונליות מעל סוגי התורנויות (למשל פיזי, מחשב). |
| `CreateChoreCategory` | Create Category | צור קטגוריה |
| `ChoreCategory` | Chore Category | קטגוריית תורנות |
| `Uncategorized` | Uncategorized | ללא קטגוריה |
| `NoChoreCategoriesYet` | No chore categories yet. | אין עדיין קטגוריות תורנויות. |
| `ChoreCategoryDeleteConfirm` | Delete this category? Its chore types become uncategorized. | למחוק קטגוריה זו? סוגי התורנויות יהפכו ללא משויכים. |
| `Members` | Members | חברים |
| `Error_ChoreCategoryNameRequired` | Category name is required. | נדרש שם קטגוריה. |
| `Error_ChoreCategoryNameExists` | A category with that name already exists. | קטגוריה בשם זה כבר קיימת. |
| `Error_ChoreCategoryNotFound` | Chore category not found. | קטגוריית תורנות לא נמצאה. |
| `Success_ChoreCategoryCreated` | Category '{0}' created. | הקטגוריה '{0}' נוצרה. |
| `Success_ChoreCategoryUpdated` | Category updated. | הקטגוריה עודכנה. |
| `Success_ChoreCategoryDeleted` | Category '{0}' deleted. | הקטגוריה '{0}' נמחקה. |
| `DefaultWeight` | Default Weight | משקל ברירת מחדל |
| `DefaultWeightHint` | Used for fairness when the chore has no explicit times. Blank → 8h. | משמש לחישוב הוגנות כשאין שעות מפורשות. ריק → 8 שעות. |
| `Hours_Short` | h | ש' |
| `Minutes_Short` | m | ד' |
| `Eligibility` | Eligibility | זכאות |
| `Elig_GenderAny` | Any gender | כל מגדר |
| `Elig_GenderMale` | Male only | גברים בלבד |
| `Elig_GenderFemale` | Female only | נשים בלבד |
| `Elig_RequiresOfficer` | Officer rank required | נדרשת דרגת קצונה |
| `Elig_Officer` | Officer | קצונה |
| `Exemptions` | Exemptions | פטורים |
| `ExemptionReasonHint` | Optional. Not stored in audit logs. | אופציונלי. אינו נשמר ביומני הביקורת. |
| `ManageChoreTemplates` | Manage Chore Templates | ניהול תבניות תורנויות |
| `ChoreTemplates` | Chore Templates | תבניות תורנויות |
| `CreateChoreTemplate` | Create Template | צור תבנית |
| `NoChoreTemplatesYet` | No chore templates yet. | אין עדיין תבניות תורנויות. |
| `StampTemplate` | Stamp Template | החתם תבנית |
| `StampFrom` | From | מתאריך |
| `StampTo` | To | עד תאריך |
| `StampWeekdays` | Weekdays | ימי שבוע |
| `StampWeekdaysHint` | Leave all unselected to stamp every day in the range. | השאר ריק כדי להחתים כל יום בטווח. |
| `StampAssignees` | Assignees | משובצים |
| `StampRotate` | Rotate (one assignee per day) | סבב (משובץ אחד ליום) |
| `StampRotateHint` | On: round-robin one person per day. Off: everyone, every matching day. | פעיל: סבב אדם אחד ליום. כבוי: כולם בכל יום מתאים. |
| `StampSubmit` | Stamp | החתם |
| `Stamp_Created` | {0} created | {0} נוצרו |
| `Stamp_Skipped` | {0} skipped | {0} דולגו |
| `Stamp_SkippedGeneric` | Skipped (not eligible or conflict) | דולג (לא זכאי או התנגשות) |
| `Error_StampInvalidInput` | Pick a valid date range and at least one assignee. | בחר טווח תאריכים תקין ולפחות משובץ אחד. |
| `Button_Done` | Done | סיום |
| `Weekday_Sun` … `Weekday_Sat` | Sun…Sat | א'…ש' (GREP first — likely already present) |
| `Users_DoesChoresOn` | {0} now participates in chores. | {0} כעת משתתף/ת בתורנויות. |
| `Users_DoesChoresOff` | {0} no longer participates in chores. | {0} אינו/ה משתתף/ת בתורנויות. |
| `Users_DoesChoresOffTitle` | Stop participating in chores? | להפסיק השתתפות בתורנויות? |
| `Users_DoesChoresOffPrompt` | {0} has {1} future chore(s). Keep or cancel them? | ל-{0} יש {1} תורנויות עתידיות. לשמור או לבטל? |
| `Users_KeepChores` | Keep chores | שמור תורנויות |
| `Users_DeleteChores` | Cancel chores | בטל תורנויות |
| `Users_NoChoreCategories` | No chore categories | אין קטגוריות תורנויות |
| `Users_ChoreCategoriesTitle` | Chore categories | קטגוריות תורנויות |
| `Users_ParticipatesChoresHint` | Whether this user is on the chore roster. | האם המשתמש ברשימת התורנויות. |
| `Users_Gender` | Gender | מגדר |
| `Users_GenderUpdated` | Gender updated for {0}. | המגדר עודכן עבור {0}. |
| `Gender_Unspecified` | Unspecified | לא צוין |
| `Gender_Male` | Male | זכר |
| `Gender_Female` | Female | נקבה |
| `Error_InvalidValue` | Invalid value. | ערך לא תקין. |

> The `__stampReasonKeys` JS map (Task 8) seeds known `StampSkipped.ReasonKey`s → localized text: the chore eligibility error keys from spec §5 (`Error_ChoreRequiresGenderMale`, `Error_ChoreRequiresGenderFemale`, `Error_ChoreRequiresOfficerRank`, `Error_ChoreUserExempt`, `Error_ChoreGenderUnspecified` — **added in Phase 2/4, GREP before adding**), plus `USER_NOT_IN_MOLECULE`, `BUSY_OVERRIDE_REQUIRED`. Unknown → `Stamp_SkippedGeneric`.

- [ ] **Step 2: Run localization tests** — `dotnet test ... --filter "FullyQualifiedName~Localization" -- xUnit.ParallelizeTestCollections=false`. They fail on (a) duplicate keys, (b) en-key-without-he-counterpart. Fix any dup by reusing the existing key.

- [ ] **Step 3: Contrast/RTL pass** (MANDATORY, on POPULATED data — per MEMORY, empty "0" hid a 1.14:1 donut bug). For each new surface (category card, eligibility chips, gender badge, DoesChores cell, stamp dialog) inspect **light + dark + Hebrew RTL**: chips/badges readable (text-vs-bg ≥ 4.5:1); color swatches are dots not text backgrounds; RTL doesn't overlap; weekday chips wrap. Optionally run the localization-qa-inspector agent on `/Admin/Organization/ChoreTypes`, `/Admin/Organization/ChoreTemplates`, `/Admin/Users`.

- [ ] **Step 4: Commit** `feat(chores-ui): bilingual resx keys + contrast/RTL pass for Phase 3 admin UI`.

---

## Self-Review

**Spec coverage (§7.1-7.4):**
- §7.1 ChoreType + Category admin → Tasks 3 (category CRUD) + 4 (category dropdown via `AssignChoreTypeAsync`, weight h+m, eligibility fieldset via `SetRulesForChoreTypeAsync`, reason chips). ✅
- §7.2 Exemptions on the ChoreType editor, AJAX, reason never audited → Task 5. ✅
- §7.3 Admin/Users: DoesChores toggle + chore-category multiselect + gender (badge + dialog, any user-editor, 3-state, audited) → Tasks 6 + 7. ✅
- §7.4 ChoreTemplates page + Stamp modal (date range + weekday chips + assignee multiselect + rotate toggle → result view created/skipped with reason keys) → Task 8. ✅
- §7.7 contrast/RTL (color as dot/accent; `.elig-chip`/`.gender-badge` colored bg + explicit light text + `!important`; dark redeclares only color) → Tasks 4/7 CSS + Task 9 Step 3. ✅
- RESOLVED DECISIONS: gender = any user-editor, no grant, audited, 3-state (Task 7); DoesChores OFF → keep/cancel prompt (Task 6); stamp = `bool rotate` toggle, additive, result view (Task 8); all category/type/rule/exemption/template admin gated by existing `Grant:EditChoreTypes` (Tasks 1,3,4,5,8 — no `GrantTypeSeed`/`RoleTemplateSeed` edits); weight shown h+m, stored minutes (Task 4). ✅

**Contract-gap closure:** gap-1 (bilingual category names) = Task 2 (option a, signatures extended + test); gap-2 (no template CRUD) = Task 1 (`IChoreTemplateService`/`ChoreTemplateService` + DI + test). Both flagged at the top with rationale. ✅

**Type consistency against the Phase 2 contract:**
- `EligibilityResult` is `record(IReadOnlyList<EligibilityViolation> Violations)` with `IsEligible`/`Eligible` — this plan does NOT construct or destructure it (chips are derived from `EligibilityRule` rows via `GetRulesForChoreTypeAsync`, not from an `EligibilityResult`). No mismatch with the picker's `GetEligibilityForCandidateAsync` (that's Phase 4). ✅
- `IChoreCategoryService.AssignChoreTypeAsync(int, int?)`, `SetUserCategoriesAsync`, `GetUserCategoryIdsAsync` — called with exact signatures (Tasks 4, 6). ✅
- `IChoreEligibilityAdminService.SetRulesForChoreTypeAsync(choreTypeId, Gender?, bool, int)` + `AddExemptionAsync(userId, choreTypeId, string?, int)` + `RemoveExemptionAsync(userId, choreTypeId)` + `GetExemptionsForChoreTypeAsync`/`GetRulesForChoreTypeAsync` — exact (Tasks 4, 5). ✅
- `IChoreService.StampTemplateAsync(int, DateOnly, DateOnly, IReadOnlyList<DayOfWeek>, IReadOnlyList<int>, bool)` → `StampResult{Created,Skipped,CreatedCount,SkippedCount}`; `StampSkipped.ReasonKey` surfaced to the result view — exact (Task 8). ✅
- `DurationFormat.FormatMinutes/FormatHours` — not needed in Phase 3 admin (weight edited as raw h+m ints; FormatHours is Phase 5 fairness display). Flagged: no call here, intentional. ✅
- `ChoreService.DEFAULT_CHORE_WEIGHT_MINUTES = 480` — referenced only in the `DefaultWeightHint` copy ("blank → 8h"), not in code here. ✅

**Placeholder scan:** every Razor block shows full `<loc>`/`@Localizer`/data attributes; every handler shows a full body except where a step EXPLICITLY says "EXACT clone of <named existing method>" (molecule resolution in Task 8 Step 2, `IsUserAuthorizedForMoleculeAsync` clones, the create/update CRUD handlers in Task 8) — those name the precise source method + file:line to copy, which is a directed clone, not an unspecified placeholder. The Users `UserVM`/`userList.Add` edits name the exact lines (83, 711, 746) to touch.

**Authz/IDOR:** every ChoreTypes/ChoreTemplates handler re-verifies the molecule (`IsUserAuthorizedForMoleculeAsync`); every Users handler rides `AuthorizeUserEditAsync`; AJAX handlers send the antiforgery token header (matching `OnPostUserCategoriesAsync`); the exemption reason is never logged. No new grants; `GrantTypeSeed`/`RoleTemplateSeed`/`RoleTemplateAutoGrantTests` untouched (§9).

**Test surface:** each service/handler step writes a real-SQLite test; pure-markup steps verify by build + browser in light/dark/RTL (Razor is hard to unit-test). All runs use `-- xUnit.ParallelizeTestCollections=false`.

---

## Deferred Items

- **Per-template times driving frozen chore weight.** Phase 2's `StampTemplateAsync` passes only `moleculeId`/`choreTypeId` to `CreateChoreAsync`, so a stamped chore's `WeightMinutes` resolves from the type default / 480 — the template's `StartTime`/`EndTime`/`WeightMinutesOverride` do NOT drive the frozen weight (matches the manual untimed flow). Honoring template times in weight needs a `CreateChoreAsync` signature change (startTime/endTime params touching all callers) — Phase 2 already flagged this as deferred. This plan inherits that deferral; the ChoreTemplates editor still lets admins SET template times/override (stored on the template, usable later), they just don't affect the stamped weight yet. **Flag to user: confirm this is acceptable, or schedule the `CreateChoreAsync` signature change.**
- **Eligibility on the ChoreType CREATE form.** Task 4 puts the full eligibility fieldset on the EDIT row only (category + weight on both create and edit). A brand-new type is created uncategorized-eligible, then eligibility is set by editing it (type-centric mental model, §7.2). **Flag to user: if eligibility-at-creation is wanted, add the fieldset to the create form too (a ~15-line markup + handler addition).**
- **`Pages/Public/Chores.cshtml.cs` / `Controllers/Api/V1/ChoresController.cs` weight writes** — these are Phase 2 create-path concerns, not Phase 3 (admin UI). Out of scope here; verify they were handled in Phase 2.
- **Phase 4 (calendar roster + eligibility-in-picker)** and **Phase 5 (weighted fairness UI)** — out of scope; this plan is Admin UI only.
- **Stamp result persistence** — the stamp result view is transient (in the dialog). No "stamp history" is stored (spec D10: "no schedule persisted"). Intentional; not deferred work, just noting the boundary.
