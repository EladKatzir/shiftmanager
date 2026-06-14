# Chores↔ShiftType Parity — Phase 1 (Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the decision-stable data-model foundation for chore↔shift parity — new entities (`ChoreCategory`, `UserChoreCategory`, `EligibilityRule`, `UserChoreExemption`, `ChoreTemplate`), the `AppUser`/`ChoreType`/`Chore` column additions, their EF config, and the additive migration + idempotent backfill — with full regression tests, touching **no** shift runtime behavior.

**Architecture:** Mirror the existing shift spine (`ShiftCategory`/`UserShiftCategory` + `DoesShifts`) into chore-local entities. New config entities are molecule-scoped (no `IBelongsToCompany`, no tenant query filter — same posture as `ShiftCategory`/`ChoreType`). `Chore` keeps its existing tenant scoping and its one-active-chore-per-user-per-day unique index unchanged. Backfill follows the project's `*BackfillSql.cs` constants + thin-migration + identical-SQL-regression-test idiom.

**Tech Stack:** ASP.NET Core 8.0, EF Core (SQLite), xUnit + FluentAssertions, real-SQLite tests (`UseSqlite(":memory:")`, never `UseInMemoryDatabase`).

**Spec:** `docs/superpowers/specs/2026-06-14-chores-shifttype-parity-design.md`. This plan implements **Phase 0 + Phase 1** only (§11 of the spec). Phases 2–6 (services/eligibility, admin UI, calendar, fairness, grants) get their own plans and **several depend on the §13 open product decisions** — do not start them until those are resolved.

---

## Planning-time corrections to the spec (flagged)

1. **`ChoreType.ChoreCategoryId` stays NULLABLE with `OnDelete(SetNull)`** — NOT `NOT NULL` as spec §4/D6 stated. Rationale: matches `ShiftType.CategoryId` exactly, matches the design agent's "Uncategorized" admin UI option/group, and keeps the backfill regression-testable. This **removes the planned Migration #2** (no tighten step). The backfill still assigns existing types to a per-molecule "General" category as a sensible default; the column merely *permits* uncategorized types going forward.
2. **`ChoreCategory` omits `CreatedByUserId`** — the structural sibling `ShiftCategory` has no creator field, and a backfilled "General" category has no natural creating user (a non-null FK would force a sentinel). Drop it; keep `NameEn`/`NameHe` (the design agent's UI wants bilingual category names, and `ChoreType` already models them).

---

## Pre-flight (read once before any task)

- **Executable lock (CRITICAL):** EF migration/build/test commands fail or use stale binaries if the dev app is running. Before any `dotnet build`/`dotnet ef`/`dotnet test`, confirm the app on `:5000` is stopped (the `bin/Debug` exe holds a file lock). If a lock error appears, STOP and terminate the process — do not retry over a lock.
- **Tests run sequentially:** always `dotnet test -- xUnit.ParallelizeTestCollections=false` (parallel runs produce ~spurious `:memory:` SQLite-contention failures).
- **Branch:** work on `dev` (project convention). Do **not** edit `FinalProductPublish/` (generated).
- **`dotnet ef` is run from the repo root** (`C:\Users\katzi\Downloads\ShiftManager`), the web project. If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`.

---

## File Structure

**Create:**
- `Services/IChoreService.cs` — the extracted `IChoreService` interface (convention fix; currently inline in `ChoreService.cs:9-40`).
- `Models/Support/Gender.cs` — `Gender` enum.
- `Models/Support/EligibilityEnums.cs` — `EligibilitySubjectKind`, `EligibilityRuleKind`.
- `Models/ChoreCategory.cs` — molecule-scoped category (mirrors `ShiftCategory`).
- `Models/UserChoreCategory.cs` — user↔category N:N (mirrors `UserShiftCategory`).
- `Models/EligibilityRule.cs` — polymorphic, type-agnostic rule (chore-wired only).
- `Models/UserChoreExemption.cs` — per-person waiver.
- `Models/ChoreTemplate.cs` — reusable stamp-out definition.
- `Migrations/ChoreFoundationBackfillSql.cs` — backfill SQL constants (`Forward[]`).
- `Migrations/<ts>_AddChoreFoundation.cs` — additive schema (generated).
- `Migrations/<ts>_BackfillChoreFoundation.cs` — runs `ChoreFoundationBackfillSql.Forward`.
- `ShiftManager.Tests/UnitTests/Migrations/ChoreFoundationBackfillTests.cs` — backfill regression.
- `ShiftManager.Tests/UnitTests/Models/ChoreFoundationSchemaTests.cs` — entity/index round-trip.

**Modify:**
- `Services/ChoreService.cs` — remove the inline interface (now in its own file).
- `Models/AppUser.cs` — `Gender`, `DoesChores`, `ChoreCategories`, `ChoreExemptions`.
- `Models/ChoreType.cs` — `ChoreCategoryId?`, `DefaultWeightMinutes?`, `ChoreCategory` nav.
- `Models/Chore.cs` — `WeightMinutes`.
- `Data/AppDbContext.cs` — 5 `DbSet`s + config blocks; `ChoreType→ChoreCategory` FK.

---

## Phase 0 — Convention fix + enums

### Task 1: Extract `IChoreService` to its own file

**Files:**
- Create: `Services/IChoreService.cs`
- Modify: `Services/ChoreService.cs:9-40` (remove inline interface)

- [ ] **Step 1: Create `Services/IChoreService.cs`** with the exact interface currently at `ChoreService.cs:9-40` (copy it verbatim, including the XML doc on `ValidateChoreAssignmentAsync`):

```csharp
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Models.Validation;

namespace ShiftManager.Services;

public interface IChoreService
{
    Task<(bool Success, string Message, Chore? Chore, BusyValidation? Validation, string? OverrideToken)> CreateChoreAsync(int assigneeId, DateOnly date, string title, string? notes = null, bool forceAssign = false, int? moleculeId = null, int? choreTypeId = null, string? overrideToken = null);
    Task<(bool Success, string Message)> CancelChoreAsync(int choreId, string? reason = null);
    Task<(bool Success, string Message)> RestoreChoreAsync(int choreId);
    Task<(bool Success, string Message, Chore? Chore)> ReplaceShiftWithChoreAsync(int shiftAssignmentId, string title, string? notes = null);
    Task<(bool Success, string Message)> ReplaceChoreWithShiftAsync(int choreId, int shiftInstanceId);
    Task<List<Chore>> GetChoresAsync(DateOnly? startDate = null, DateOnly? endDate = null, int? userId = null, bool? includeCancel = false, int? moleculeId = null);
    Task<Chore?> GetChoreByIdAsync(int choreId);
    Task<bool> HasActiveChoreOnDateAsync(int userId, DateOnly date);
    Task<bool> HasShiftOnDateAsync(int userId, DateOnly date);
    Task<ShiftAssignment?> GetShiftOnDateAsync(int userId, DateOnly date);
    Task<bool> HasVacationConflictAsync(int userId, DateOnly date);
    Task<(bool HasConflict, DateOnly? StartDate, DateOnly? EndDate, TimeOffType? Type)> GetVacationConflictDetailsAsync(int userId, DateOnly date);
    Task<bool> CanUserManageChoresAsync(int userId);
    Task<bool> CanUserManageChoreForAssigneeAsync(int managerId, int assigneeId);
    Task<List<AppUser>> GetEligibleAssigneesAsync();

    Task<ChoreAssignmentValidation> ValidateChoreAssignmentAsync(
        int userId, DateOnly date, int moleculeId, int? choreTypeId = null,
        string? overrideToken = null, CancellationToken ct = default);
}
```

- [ ] **Step 2: Remove the inline `public interface IChoreService { ... }` block** from `Services/ChoreService.cs` (lines 9-40), leaving the `// SECURITY-AUDITED:` comment and `public class ChoreService : IChoreService`. Keep all `using`s on the class file.

- [ ] **Step 3: Build to verify no behavior change**

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded, 0 errors (pure move).

- [ ] **Step 4: Commit**

```bash
git add Services/IChoreService.cs Services/ChoreService.cs
git commit -m "refactor(chores): extract IChoreService to its own file (convention fix)"
```

### Task 2: Add the new enums

**Files:**
- Create: `Models/Support/Gender.cs`, `Models/Support/EligibilityEnums.cs`

- [ ] **Step 1: Create `Models/Support/Gender.cs`**

```csharp
namespace ShiftManager.Models.Support;

/// <summary>
/// Sensitive personal attribute used ONLY for gender-segregated chore eligibility
/// (male-only / female-only chores). Unspecified is the backfill default and means
/// "not recorded" — it FAILS gender-restricted chores (fail-closed).
/// </summary>
public enum Gender
{
    Unspecified = 0,
    Male = 1,
    Female = 2
}
```

- [ ] **Step 2: Create `Models/Support/EligibilityEnums.cs`**

```csharp
namespace ShiftManager.Models.Support;

/// <summary>Which entity an EligibilityRule attaches to. Only ChoreType is wired this cycle;
/// ShiftType is shaped-for but intentionally not validated against yet.</summary>
public enum EligibilitySubjectKind
{
    ChoreType = 0,
    ShiftType = 1
}

/// <summary>The kind of hard requirement an EligibilityRule expresses.</summary>
public enum EligibilityRuleKind
{
    RequiresGender = 0,
    RequiresOfficerRank = 1
}
```

- [ ] **Step 3: Build**

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Models/Support/Gender.cs Models/Support/EligibilityEnums.cs
git commit -m "feat(chores): add Gender + Eligibility enums"
```

---

## Phase 1 — Data model

### Task 3: `ChoreCategory` + `UserChoreCategory` entities

**Files:**
- Create: `Models/ChoreCategory.cs`, `Models/UserChoreCategory.cs`

- [ ] **Step 1: Create `Models/ChoreCategory.cs`** (mirrors `ShiftCategory`; no `CreatedByUserId` per planning correction #2)

```csharp
namespace ShiftManager.Models;

/// <summary>
/// A molecule-scoped, functional grouping of chore types (e.g. "Physical", "Computer").
/// A ChoreType belongs to at most one category via <see cref="ChoreType.ChoreCategoryId"/>.
/// Users participate in chores (AppUser.DoesChores) and are mapped to one or more categories
/// via <see cref="UserChoreCategory"/>. Mirrors <see cref="ShiftCategory"/>; NOT tenant-filtered
/// (visibility is molecule-scoped via MoleculeId).
/// </summary>
public class ChoreCategory
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;          // unique per (MoleculeId, Name)
    public string DisplayName { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? NameHe { get; set; }
    public string? Color { get; set; }                         // hex; rendered as a dot/accent, never a text bg
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<ChoreType> ChoreTypes { get; set; } = new();
    public List<UserChoreCategory> Members { get; set; } = new();
}
```

- [ ] **Step 2: Create `Models/UserChoreCategory.cs`** (mirrors `UserShiftCategory`)

```csharp
namespace ShiftManager.Models;

/// <summary>
/// Many-to-many join between a user and the chore categories they participate in. Only meaningful
/// when AppUser.DoesChores = true. Mirrors <see cref="UserShiftCategory"/>.
/// </summary>
public class UserChoreCategory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ChoreCategoryId { get; set; }

    public AppUser User { get; set; } = null!;
    public ChoreCategory ChoreCategory { get; set; } = null!;
}
```

- [ ] **Step 3: Build** — `dotnet build ShiftManager.csproj` → succeeded (navs to `ChoreType`/`AppUser` resolve; config comes in Task 8).
- [ ] **Step 4: Commit**

```bash
git add Models/ChoreCategory.cs Models/UserChoreCategory.cs
git commit -m "feat(chores): add ChoreCategory + UserChoreCategory entities"
```

### Task 4: `EligibilityRule` entity

**Files:**
- Create: `Models/EligibilityRule.cs`

- [ ] **Step 1: Create `Models/EligibilityRule.cs`**

```csharp
using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// A type-agnostic, polymorphic eligibility requirement attached to a subject (ChoreType now;
/// ShiftType shaped-for-later, NOT wired this cycle). A subject may carry several rules (e.g.
/// female-only AND officer-only). Global config table — no tenant filter. Evaluated by
/// IEligibilityEvaluator as a HARD block at assignment time (Phase 2).
/// </summary>
public class EligibilityRule
{
    public int Id { get; set; }
    public EligibilitySubjectKind SubjectKind { get; set; }   // ChoreType this cycle
    public int SubjectId { get; set; }                        // the ChoreType.Id
    public EligibilityRuleKind RuleKind { get; set; }
    public Gender? GenderValue { get; set; }                  // set iff RuleKind == RequiresGender
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public AppUser? Creator { get; set; }
}
```

- [ ] **Step 2: Build** → succeeded.
- [ ] **Step 3: Commit**

```bash
git add Models/EligibilityRule.cs
git commit -m "feat(chores): add EligibilityRule entity (polymorphic, chore-wired)"
```

### Task 5: `UserChoreExemption` entity

**Files:**
- Create: `Models/UserChoreExemption.cs`

- [ ] **Step 1: Create `Models/UserChoreExemption.cs`**

```csharp
namespace ShiftManager.Models;

/// <summary>
/// A per-person negative override: this user is waived from a specific chore type (e.g. a
/// disability accommodation). NOT an EligibilityRule (those are positive requirements on the type).
/// Reason is optional, sensitive, capped at 200 chars; never logged in plaintext audit.
/// </summary>
public class UserChoreExemption
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ChoreTypeId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public AppUser User { get; set; } = null!;
    public ChoreType ChoreType { get; set; } = null!;
    public AppUser? Creator { get; set; }
}
```

- [ ] **Step 2: Build** → succeeded.
- [ ] **Step 3: Commit**

```bash
git add Models/UserChoreExemption.cs
git commit -m "feat(chores): add UserChoreExemption (per-person waiver)"
```

### Task 6: `ChoreTemplate` entity

**Files:**
- Create: `Models/ChoreTemplate.cs`

- [ ] **Step 1: Create `Models/ChoreTemplate.cs`**

```csharp
namespace ShiftManager.Models;

/// <summary>
/// A reusable chore definition a manager can "stamp" across a date range. Carries NO schedule —
/// stamping (Phase 2) loops the existing manual CreateChoreAsync per (date, assignee). Molecule-scoped.
/// </summary>
public class ChoreTemplate
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ChoreTypeId { get; set; }
    public string DefaultTitle { get; set; } = string.Empty;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public int? WeightMinutesOverride { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public Molecule Molecule { get; set; } = null!;
    public ChoreType? ChoreType { get; set; }
    public AppUser? Creator { get; set; }
}
```

- [ ] **Step 2: Build** → succeeded.
- [ ] **Step 3: Commit**

```bash
git add Models/ChoreTemplate.cs
git commit -m "feat(chores): add ChoreTemplate (reusable stamp definition)"
```

### Task 7: Modify `AppUser`, `ChoreType`, `Chore`

**Files:**
- Modify: `Models/AppUser.cs`, `Models/ChoreType.cs`, `Models/Chore.cs`

- [ ] **Step 1: `Models/AppUser.cs`** — add after the `ShiftCategories` nav (line 118). `using ShiftManager.Models.Support;` is already present (line 1).

```csharp
    /// <summary>Sensitive: gender-segregated chore eligibility only. Default Unspecified (fail-closed).</summary>
    public Gender Gender { get; set; } = Gender.Unspecified;

    /// <summary>Whether this user participates in chore scheduling. Mirrors <see cref="DoesShifts"/>.</summary>
    public bool DoesChores { get; set; }

    /// <summary>Chore categories this user participates in (N:N). Meaningful only when DoesChores is true.</summary>
    public List<UserChoreCategory> ChoreCategories { get; set; } = new();

    /// <summary>Per-type chore exemptions (waivers) for this user.</summary>
    public List<UserChoreExemption> ChoreExemptions { get; set; } = new();
```

- [ ] **Step 2: `Models/ChoreType.cs`** — add fields after `CreatedByUserId` (line 15) and a nav after the `Molecule` nav (line 18):

```csharp
    // Parity additions
    public int? ChoreCategoryId { get; set; }       // nullable: types may be uncategorized (mirrors ShiftType.CategoryId)
    public int? DefaultWeightMinutes { get; set; }  // null → global fallback (240) at chore-create time

    public ChoreCategory? ChoreCategory { get; set; }
```

- [ ] **Step 3: `Models/Chore.cs`** — add after `Notes` (line 55):

```csharp
    /// <summary>Fairness weight in minutes, frozen at create time (times → ChoreType.DefaultWeightMinutes → 240).</summary>
    public int WeightMinutes { get; set; }
```

- [ ] **Step 4: Build** → succeeded.
- [ ] **Step 5: Commit**

```bash
git add Models/AppUser.cs Models/ChoreType.cs Models/Chore.cs
git commit -m "feat(chores): add Gender/DoesChores/chore navs, ChoreType category+weight, Chore.WeightMinutes"
```

### Task 8: `AppDbContext` DbSets + config

**Files:**
- Modify: `Data/AppDbContext.cs` (DbSets near line 41; config blocks after the `ChoreType` block at ~608)

- [ ] **Step 1: Add DbSets** after `public DbSet<ChoreType> ChoreTypes => Set<ChoreType>();` (line 41):

```csharp
    public DbSet<ChoreCategory> ChoreCategories => Set<ChoreCategory>();
    public DbSet<UserChoreCategory> UserChoreCategories => Set<UserChoreCategory>();
    public DbSet<EligibilityRule> EligibilityRules => Set<EligibilityRule>();
    public DbSet<UserChoreExemption> UserChoreExemptions => Set<UserChoreExemption>();
    public DbSet<ChoreTemplate> ChoreTemplates => Set<ChoreTemplate>();
```

- [ ] **Step 2: Add config** immediately after the `ChoreType` config block (which ends at line ~608, the `});`). The `timeConverter` local is already defined earlier in `OnModelCreating` (used by `Chore.StartTime/EndTime` at ~201) and is in scope here.

```csharp
        // ===== Chore↔ShiftType parity (Phase 1 foundation) =====
        // ChoreCategory: molecule-scoped, NOT tenant-filtered (mirrors ShiftCategory).
        modelBuilder.Entity<ChoreCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Molecule)
                .WithMany()
                .HasForeignKey(e => e.MoleculeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.MoleculeId, e.Name }).IsUnique();
        });

        // ChoreType → ChoreCategory: nullable (uncategorized allowed), SetNull on category delete
        // (mirrors ShiftType.CategoryId). WithMany(cc => cc.ChoreTypes) prevents a shadow FK.
        modelBuilder.Entity<ChoreType>()
            .HasOne(ct => ct.ChoreCategory)
            .WithMany(cc => cc.ChoreTypes)
            .HasForeignKey(ct => ct.ChoreCategoryId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ChoreType>()
            .HasIndex(ct => ct.ChoreCategoryId);

        // UserChoreCategory: N:N user↔category, unique per pair (mirrors UserShiftCategory).
        modelBuilder.Entity<UserChoreCategory>()
            .HasIndex(ucc => new { ucc.UserId, ucc.ChoreCategoryId }).IsUnique();
        modelBuilder.Entity<UserChoreCategory>()
            .HasOne(ucc => ucc.User).WithMany(u => u.ChoreCategories)
            .HasForeignKey(ucc => ucc.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserChoreCategory>()
            .HasOne(ucc => ucc.ChoreCategory).WithMany(cc => cc.Members)
            .HasForeignKey(ucc => ucc.ChoreCategoryId).OnDelete(DeleteBehavior.Cascade);

        // EligibilityRule: global config, no tenant filter; multiple rules per subject.
        modelBuilder.Entity<EligibilityRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SubjectKind, e.SubjectId });
            entity.HasOne(e => e.Creator).WithMany()
                .HasForeignKey(e => e.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        // UserChoreExemption: per-person waiver, unique per (user, type).
        modelBuilder.Entity<UserChoreExemption>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ChoreTypeId }).IsUnique();
            entity.HasOne(e => e.User).WithMany(u => u.ChoreExemptions)
                .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ChoreType).WithMany()
                .HasForeignKey(e => e.ChoreTypeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Creator).WithMany()
                .HasForeignKey(e => e.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        // ChoreTemplate: molecule-scoped reusable definition; TimeOnly props need the converter.
        modelBuilder.Entity<ChoreTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MoleculeId, e.IsActive });
            entity.Property(e => e.StartTime).HasConversion(timeConverter);
            entity.Property(e => e.EndTime).HasConversion(timeConverter);
            entity.HasOne(e => e.Molecule).WithMany()
                .HasForeignKey(e => e.MoleculeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ChoreType).WithMany()
                .HasForeignKey(e => e.ChoreTypeId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Creator).WithMany()
                .HasForeignKey(e => e.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });
```

> If the build reports `timeConverter` is not in scope at this position, move these blocks to just below the `Chore` TimeOnly conversion lines (~205) where it is defined, or hoist `timeConverter` — do NOT redefine it.

- [ ] **Step 3: Build** — `dotnet build ShiftManager.csproj` → succeeded (no shadow-FK warnings about `MoleculeId1`/`ChoreCategoryId1`).
- [ ] **Step 4: Commit**

```bash
git add Data/AppDbContext.cs
git commit -m "feat(chores): register chore-parity entities + ChoreType↔ChoreCategory FK"
```

### Task 9: Generate the additive schema migration

**Files:**
- Create (generated): `Migrations/<ts>_AddChoreFoundation.cs` (+ `.Designer.cs`, snapshot update)

- [ ] **Step 1: Confirm the app is stopped** (executable-lock pre-flight). Then add the migration:

Run: `dotnet ef migrations add AddChoreFoundation`
Expected: "Done." Three files touched (migration, designer, `AppDbContextModelSnapshot.cs`).

- [ ] **Step 2: Inspect the generated `Up()`** — verify it ONLY: creates tables `ChoreCategories`, `UserChoreCategories`, `EligibilityRules`, `UserChoreExemptions`, `ChoreTemplates`; adds columns `Users.Gender`(int, default 0), `Users.DoesChores`(int/bool, default 0), `ChoreTypes.ChoreCategoryId`(int, **nullable**), `ChoreTypes.DefaultWeightMinutes`(int, nullable), `Chores.WeightMinutes`(int). **Confirm it does NOT touch the existing `Chores` unique index** (`IX_Chores_CompanyId_UserId_Date_CanceledAt`) or any `ShiftType`/`ShiftCategory` table.

- [ ] **Step 3: Set the `Chores.WeightMinutes` default to 240.** In the generated `Up()`, find the `AddColumn<int>(name: "WeightMinutes", table: "Chores", ...)` and ensure `defaultValue: 240` is present (add it if EF emitted `defaultValue: 0`):

```csharp
migrationBuilder.AddColumn<int>(
    name: "WeightMinutes",
    table: "Chores",
    type: "INTEGER",
    nullable: false,
    defaultValue: 240);
```

- [ ] **Step 4: Apply + build to verify the schema is valid**

Run: `dotnet build ShiftManager.csproj`
Expected: succeeded. (Migration applies at app startup via the project's `Database.Migrate()`; do not hand-run `database update` against the live air-gapped DB.)

- [ ] **Step 5: Commit**

```bash
git add Migrations/
git commit -m "feat(chores): migration AddChoreFoundation (additive schema, nullable category)"
```

### Task 10: Backfill SQL constants + backfill migration

**Files:**
- Create: `Migrations/ChoreFoundationBackfillSql.cs`
- Create (generated, then edited): `Migrations/<ts>_BackfillChoreFoundation.cs`

- [ ] **Step 1: Create `Migrations/ChoreFoundationBackfillSql.cs`** (mirrors `ShiftCategoryBackfillSql` — same constants-reused-by-test idiom). Note `TimeOnly` is stored as `"HH:mm"` strings, so `substr(t,1,2)`=hours, `substr(t,4,2)`=minutes.

```csharp
namespace ShiftManager.Migrations;

/// <summary>
/// Raw SQLite statements that seed the chore-parity foundation onto an existing database. Held as
/// shared constants so the <c>BackfillChoreFoundation</c> migration and its regression test run the
/// EXACT same SQL — no drift. Idempotent (safe to re-run): category creation is guarded by NOT EXISTS,
/// type assignment only touches NULLs, weight backfill only touches timed chores, participant marking
/// is a stable predicate.
/// </summary>
public static class ChoreFoundationBackfillSql
{
    /// <summary>1) One "General" category per molecule that has ≥1 chore type and no existing 'General'.</summary>
    public const string CreateGeneralCategories = @"
        INSERT INTO ChoreCategories (MoleculeId, Name, DisplayName, NameEn, NameHe, SortOrder, IsActive, CreatedAt)
        SELECT DISTINCT ct.MoleculeId, 'General', 'General', 'General', 'כללי', 0, 1,
               strftime('%Y-%m-%d %H:%M:%S', 'now')
        FROM ChoreTypes ct
        WHERE NOT EXISTS (
            SELECT 1 FROM ChoreCategories cc
            WHERE cc.MoleculeId = ct.MoleculeId AND cc.Name = 'General');";

    /// <summary>2) Assign each uncategorized chore type to its molecule's General category.</summary>
    public const string AssignTypesToGeneral = @"
        UPDATE ChoreTypes
        SET ChoreCategoryId = (
            SELECT cc.Id FROM ChoreCategories cc
            WHERE cc.MoleculeId = ChoreTypes.MoleculeId AND cc.Name = 'General')
        WHERE ChoreCategoryId IS NULL;";

    /// <summary>3) Freeze WeightMinutes from explicit times where both present (else keep the 240 default).
    /// Guards EndTime > StartTime so midnight-crossing chores (out of scope) keep the default.</summary>
    public const string BackfillChoreWeights = @"
        UPDATE Chores
        SET WeightMinutes =
            ((CAST(substr(EndTime,1,2)   AS INTEGER) * 60 + CAST(substr(EndTime,4,2)   AS INTEGER))
           - (CAST(substr(StartTime,1,2) AS INTEGER) * 60 + CAST(substr(StartTime,4,2) AS INTEGER)))
        WHERE StartTime IS NOT NULL AND EndTime IS NOT NULL AND EndTime > StartTime;";

    /// <summary>4) Existing active Standard users become chore participants (preserves the roster under
    /// the new DoesChores predicate). AccountType 0 = Standard.</summary>
    public const string MarkChoreParticipants =
        "UPDATE Users SET DoesChores = 1 WHERE IsActive = 1 AND AccountType = 0;";

    /// <summary>The forward backfill, in order.</summary>
    public static readonly string[] Forward =
    {
        CreateGeneralCategories, AssignTypesToGeneral, BackfillChoreWeights, MarkChoreParticipants
    };
}
```

- [ ] **Step 2: Generate an empty migration** for the backfill (so it gets a correct timestamp AFTER `AddChoreFoundation`):

Run: `dotnet ef migrations add BackfillChoreFoundation`
Expected: "Done." (The generated `Up`/`Down` are empty — we fill them next.)

- [ ] **Step 3: Replace the generated migration body** in `Migrations/<ts>_BackfillChoreFoundation.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class BackfillChoreFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DATA BACKFILL: seed General categories, categorize existing types, freeze chore weights,
            // mark existing active Standard users as chore participants. Runs AFTER AddChoreFoundation so
            // the new columns/tables exist. SQL lives in ChoreFoundationBackfillSql so this migration and
            // its regression test run identical SQL.
            foreach (var sql in ChoreFoundationBackfillSql.Forward)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Users SET DoesChores = 0;");
            migrationBuilder.Sql("UPDATE Chores SET WeightMinutes = 240;");
            migrationBuilder.Sql("UPDATE ChoreTypes SET ChoreCategoryId = NULL;");
            migrationBuilder.Sql("DELETE FROM ChoreCategories WHERE Name = 'General';");
        }
    }
}
```

- [ ] **Step 4: Build** → succeeded.
- [ ] **Step 5: Commit**

```bash
git add Migrations/ChoreFoundationBackfillSql.cs Migrations/*BackfillChoreFoundation*.cs
git commit -m "feat(chores): backfill General categories, weights, DoesChores participants"
```

### Task 11: Backfill regression test

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Migrations/ChoreFoundationBackfillTests.cs`

- [ ] **Step 1: Write the failing test** (mirrors `ShiftCategoryBackfillTests`; runs `ChoreFoundationBackfillSql.Forward` over a seed exercising every branch):

```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Migrations;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Migrations;

/// <summary>
/// Regression guard for the BackfillChoreFoundation migration — runs the EXACT SQL it ships
/// (ChoreFoundationBackfillSql.Forward) against real SQLite over a seed that exercises:
///   * a molecule WITH chore types (gets a General category) and one WITHOUT (none created),
///   * a timed chore (weight from times), a differently-timed chore, an untimed chore (keeps 240),
///   * active Standard / active Mil / active GroupUser / inactive Standard users.
/// </summary>
public sealed class ChoreFoundationBackfillTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Hierarchy: Project→Area→Molecule(1 has types, 2 has none). Company(1) in molecule 1.
        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });

        // Two chore types in molecule 1, both uncategorized; molecule 2 has none.
        _db.ChoreTypes.AddRange(
            new ChoreType { Id = 100, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1 },
            new ChoreType { Id = 101, MoleculeId = 1, Name = "Guard",   DisplayName = "Guard",   CreatedByUserId = 1 });

        _db.Users.AddRange(
            new AppUser { Id = 1, CompanyId = 1, Email = "s@x.mil",  DisplayName = "Std",    IsActive = true,  AccountType = AccountType.Standard },
            new AppUser { Id = 2, CompanyId = 1, Email = "m@x.mil",  DisplayName = "Mil",    IsActive = true,  AccountType = AccountType.Mil },
            new AppUser { Id = 3, CompanyId = 1, Email = "g@x.mil",  DisplayName = "Grp",    IsActive = true,  AccountType = AccountType.GroupUser },
            new AppUser { Id = 4, CompanyId = 1, Email = "i@x.mil",  DisplayName = "StdOff", IsActive = false, AccountType = AccountType.Standard });
        await _db.SaveChangesAsync();

        // Chores: timed 10:00-14:00 (=240), timed 08:00-12:30 (=270), untimed (keeps default 240).
        _db.Chores.AddRange(
            new Chore { Id = 500, CompanyId = 1, MoleculeId = 1, UserId = 1, Date = new DateOnly(2026, 6, 20),
                        Title = "T1", StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(14, 0),
                        CreatedBy = 1, CreatedAt = DateTime.UtcNow, WeightMinutes = 240 },
            new Chore { Id = 501, CompanyId = 1, MoleculeId = 1, UserId = 1, Date = new DateOnly(2026, 6, 21),
                        Title = "T2", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 30),
                        CreatedBy = 1, CreatedAt = DateTime.UtcNow, WeightMinutes = 240 },
            new Chore { Id = 502, CompanyId = 1, MoleculeId = 1, UserId = 1, Date = new DateOnly(2026, 6, 22),
                        Title = "T3", CreatedBy = 1, CreatedAt = DateTime.UtcNow, WeightMinutes = 240 });
        await _db.SaveChangesAsync();

        foreach (var sql in ChoreFoundationBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);

        _db.ChangeTracker.Clear(); // raw SQL bypasses the tracker; re-read fresh DB state
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Creates_General_Category_Only_For_Molecules_With_Types()
    {
        var cats = await _db.ChoreCategories.IgnoreQueryFilters().ToListAsync();
        cats.Should().ContainSingle("only molecule 1 has chore types");
        cats[0].MoleculeId.Should().Be(1);
        cats[0].Name.Should().Be("General");
        cats[0].NameHe.Should().Be("כללי");
    }

    [Fact]
    public async Task Assigns_All_Existing_Types_To_General()
    {
        var general = await _db.ChoreCategories.IgnoreQueryFilters().SingleAsync();
        var types = await _db.ChoreTypes.IgnoreQueryFilters().ToListAsync();
        types.Should().OnlyContain(t => t.ChoreCategoryId == general.Id);
    }

    [Fact]
    public async Task Freezes_Weight_From_Times_Else_Keeps_Default()
    {
        var chores = await _db.Chores.IgnoreQueryFilters().ToDictionaryAsync(c => c.Id);
        chores[500].WeightMinutes.Should().Be(240, "10:00-14:00 = 240m");
        chores[501].WeightMinutes.Should().Be(270, "08:00-12:30 = 270m");
        chores[502].WeightMinutes.Should().Be(240, "untimed keeps the column default");
    }

    [Fact]
    public async Task Marks_Only_Active_Standard_Users_As_Chore_Participants()
    {
        var users = await _db.Users.IgnoreQueryFilters().ToDictionaryAsync(u => u.Id);
        users[1].DoesChores.Should().BeTrue("active Standard");
        users[2].DoesChores.Should().BeFalse("Mil");
        users[3].DoesChores.Should().BeFalse("GroupUser");
        users[4].DoesChores.Should().BeFalse("inactive");
    }

    [Fact]
    public async Task Backfill_Is_Idempotent()
    {
        foreach (var sql in ChoreFoundationBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);
        _db.ChangeTracker.Clear();
        (await _db.ChoreCategories.IgnoreQueryFilters().CountAsync())
            .Should().Be(1, "re-running must not create a second General category");
    }
}
```

- [ ] **Step 2: Run it to verify it PASSES** (the backfill SQL from Task 10 already exists):

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ChoreFoundationBackfillTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (5 tests). If `Creates_General_..._For_Molecules_With_Types` fails with 2 categories, the `NOT EXISTS` guard regressed; if weights are wrong, check the `substr` offsets against the stored `"HH:mm"` format.

- [ ] **Step 3: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Migrations/ChoreFoundationBackfillTests.cs
git commit -m "test(chores): regression guard for ChoreFoundation backfill SQL"
```

### Task 12: Entity + index persistence tests

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Models/ChoreFoundationSchemaTests.cs`

- [ ] **Step 1: Write the test** (round-trips each entity + proves the unique indexes bite):

```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Models;

/// <summary>Schema-level guards for the chore-parity entities: round-trip persistence,
/// the (MoleculeId,Name) / (UserId,ChoreCategoryId) / (UserId,ChoreTypeId) unique indexes,
/// and ChoreType↔ChoreCategory SetNull behavior. Real SQLite (exercises SQL translation).</summary>
public sealed class ChoreFoundationSchemaTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        _db.Users.Add(new AppUser { Id = 1, CompanyId = 1, Email = "u@x.mil", DisplayName = "U", AccountType = AccountType.Standard });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ChoreCategory_Name_Is_Unique_Per_Molecule()
    {
        _db.ChoreCategories.Add(new ChoreCategory { MoleculeId = 1, Name = "Physical", DisplayName = "Physical" });
        await _db.SaveChangesAsync();
        _db.ChoreCategories.Add(new ChoreCategory { MoleculeId = 1, Name = "Physical", DisplayName = "Physical 2" });

        var act = async () => await _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("(MoleculeId, Name) is unique");
    }

    [Fact]
    public async Task ChoreType_Category_SetNull_On_Category_Delete()
    {
        var cat = new ChoreCategory { MoleculeId = 1, Name = "Computer", DisplayName = "Computer" };
        _db.ChoreCategories.Add(cat);
        await _db.SaveChangesAsync();
        var type = new ChoreType { MoleculeId = 1, Name = "Backups", DisplayName = "Backups", CreatedByUserId = 1, ChoreCategoryId = cat.Id };
        _db.ChoreTypes.Add(type);
        await _db.SaveChangesAsync();

        _db.ChoreCategories.Remove(cat);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var reloaded = await _db.ChoreTypes.IgnoreQueryFilters().SingleAsync(t => t.Id == type.Id);
        reloaded.ChoreCategoryId.Should().BeNull("SetNull un-categorizes the type, does not delete it");
    }

    [Fact]
    public async Task UserChoreCategory_Is_Unique_Per_Pair()
    {
        var cat = new ChoreCategory { MoleculeId = 1, Name = "Phys", DisplayName = "Phys" };
        _db.ChoreCategories.Add(cat);
        await _db.SaveChangesAsync();
        _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 1, ChoreCategoryId = cat.Id });
        await _db.SaveChangesAsync();
        _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 1, ChoreCategoryId = cat.Id });

        var act = async () => await _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("(UserId, ChoreCategoryId) is unique");
    }

    [Fact]
    public async Task UserChoreExemption_Is_Unique_Per_User_And_Type()
    {
        var type = new ChoreType { MoleculeId = 1, Name = "Heavy", DisplayName = "Heavy", CreatedByUserId = 1 };
        _db.ChoreTypes.Add(type);
        await _db.SaveChangesAsync();
        _db.UserChoreExemptions.Add(new UserChoreExemption { UserId = 1, ChoreTypeId = type.Id, CreatedBy = 1 });
        await _db.SaveChangesAsync();
        _db.UserChoreExemptions.Add(new UserChoreExemption { UserId = 1, ChoreTypeId = type.Id, CreatedBy = 1 });

        var act = async () => await _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("(UserId, ChoreTypeId) is unique");
    }

    [Fact]
    public async Task EligibilityRule_And_ChoreTemplate_RoundTrip()
    {
        var type = new ChoreType { MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1 };
        _db.ChoreTypes.Add(type);
        await _db.SaveChangesAsync();

        _db.EligibilityRules.Add(new EligibilityRule
        {
            SubjectKind = EligibilitySubjectKind.ChoreType, SubjectId = type.Id,
            RuleKind = EligibilityRuleKind.RequiresGender, GenderValue = Gender.Female, CreatedBy = 1
        });
        _db.ChoreTemplates.Add(new ChoreTemplate
        {
            MoleculeId = 1, Name = "Daily Kitchen", ChoreTypeId = type.Id, DefaultTitle = "Clean kitchen",
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(13, 0), CreatedBy = 1
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        (await _db.EligibilityRules.IgnoreQueryFilters().SingleAsync()).GenderValue.Should().Be(Gender.Female);
        var tpl = await _db.ChoreTemplates.IgnoreQueryFilters().SingleAsync();
        tpl.StartTime.Should().Be(new TimeOnly(9, 0), "TimeOnly converter round-trips HH:mm");
        tpl.EndTime.Should().Be(new TimeOnly(13, 0));
    }

    [Fact]
    public async Task AppUser_Gender_Defaults_To_Unspecified()
    {
        var reloaded = await _db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == 1);
        reloaded.Gender.Should().Be(Gender.Unspecified);
        reloaded.DoesChores.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ChoreFoundationSchemaTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (6 tests). A `DbUpdateException` not thrown → the corresponding unique index is missing from Task 8.

- [ ] **Step 3: Commit**

```bash
git add ShiftManager.Tests/UnitTests/Models/ChoreFoundationSchemaTests.cs
git commit -m "test(chores): schema guards for chore-parity entities + indexes"
```

### Task 13: Full-suite regression + finalize

- [ ] **Step 1: Run the entire suite sequentially** to confirm nothing else regressed (esp. shift/justice tests, since `AppDbContext` + `AppUser`/`ChoreType` changed):

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -- xUnit.ParallelizeTestCollections=false`
Expected: all green (the pre-change baseline per project memory is ~1595/1595; the new tests add 11). If a shift/justice test fails, a parity change leaked into shared behavior — investigate before proceeding (do not mask).

- [ ] **Step 2: Confirm no shift behavior changed** — spot-check that `ShiftCategoryBackfillTests`, any `JusticeService*Tests`, and `ShiftCalendarServiceAccountTypeTests` are green. Phase 1 must be inert for shifts.

- [ ] **Step 3: Final commit (if any uncommitted tidy)**

```bash
git status   # expect clean except intentional doc/test files already committed
```

---

## Self-Review (completed by author)

**Spec coverage (Phase 0+1 scope):** IChoreService extraction (D8) → Task 1. Enums (Gender/eligibility) → Task 2. All five new entities (§4) → Tasks 3-6. AppUser/ChoreType/Chore columns (§4) → Task 7. AppDbContext registration + FK posture (§4) → Task 8. Migration #1 additive (§8.2) → Task 9. Backfill: General categories, type assignment, weight freeze, DoesChores (§8.3) → Tasks 10-11. Tests (§10 items 5/6/10 foundation subset) → Tasks 11-12. Migration #2 → **intentionally removed** (planning correction #1). Eligibility *evaluation*, services, UI, fairness, grants (§5-7, §9) → **deferred to Phase 2-6 plans** (out of this plan's scope by design).

**Placeholder scan:** none — every code step shows complete code; every command shows expected output.

**Type consistency:** `ChoreCategoryId` is `int?` everywhere (Task 7 model, Task 8 SetNull config, Task 9 nullable migration, Task 11 backfill, Task 12 SetNull test). `WeightMinutes` is non-null `int` default 240 (Task 7, Task 9 step 3, Task 11). `Forward[]` constant name matches between `ChoreFoundationBackfillSql` (Task 10) and both its consumers (migration Task 10, test Task 11). Enum values (`AccountType.Standard == 0`) used consistently in SQL (`AccountType = 0`) and tests.

**Known dependency for later phases (flagged):** Phases 3-5 task code depends on the spec §13 open product decisions (stamp all-vs-round-robin, DoesChores-OFF semantics, gender visibility/fail-closed). Resolve those before writing the Phase 2-6 plans.
