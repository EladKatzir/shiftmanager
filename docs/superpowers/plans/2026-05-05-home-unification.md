# HOME Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify vacation, after, and rotation HOME into one materialised data surface; rebuild approval routing as per-jobtype-vertical pools with optional dual approval; redesign HomeType to rule-first with deterministic cycle anchor; close all calendar visibility gaps so HOME shows identically on every surface.

**Architecture:** Vacation/After approval triggers a materialiser that writes `ShiftAssignment` rows tagged with `SourceTimeOffRequestId`, using one of three HOME `ShiftType` variants (`HOME`, `HOME_PM`, `HOME_AM`). Existing HOME exemption logic (capacity / hours / weekly cap / analytics) automatically applies via the `IsHome` flag. Rotation HOME uses a deterministic `DerivedRotationRule.Anchor` so vacation cancellation can faithfully restore. Calendar renders chips with shared home color, Lucide source icons distinguish source.

**Tech Stack:** ASP.NET Core 8.0, EF Core (SQLite), Razor Pages, SignalR (CalendarHub), Lucide icons, IIfe-wrapped JS, xUnit tests.

**Spec:** `docs/superpowers/specs/2026-05-05-home-unification-design.md`

---

## Phase 1 — Schema migrations & seed updates

### Task 1: Migration `AddSourceTimeOffRequestIdToShiftAssignments`

**Files:**
- Create: `Migrations/{timestamp}_AddSourceTimeOffRequestIdToShiftAssignments.cs`
- Modify: `Models/ShiftAssignment.cs`
- Modify: `Data/AppDbContext.cs` (model builder, OnModelCreating)

- [ ] **Step 1: Add property to `ShiftAssignment.cs`**

```csharp
public int? SourceTimeOffRequestId { get; set; }
public TimeOffRequest? SourceTimeOffRequest { get; set; }
```

- [ ] **Step 2: Add EF configuration in `AppDbContext.OnModelCreating`**

In the `ShiftAssignment` configuration block:

```csharp
modelBuilder.Entity<ShiftAssignment>()
    .HasOne(sa => sa.SourceTimeOffRequest)
    .WithMany()
    .HasForeignKey(sa => sa.SourceTimeOffRequestId)
    .OnDelete(DeleteBehavior.SetNull);

modelBuilder.Entity<ShiftAssignment>()
    .HasIndex(sa => sa.SourceTimeOffRequestId)
    .HasDatabaseName("IX_ShiftAssignments_SourceTimeOffRequestId")
    .HasFilter("[SourceTimeOffRequestId] IS NOT NULL");
```

- [ ] **Step 3: Generate migration**

```powershell
dotnet ef migrations add AddSourceTimeOffRequestIdToShiftAssignments
```

- [ ] **Step 4: Apply migration to dev DB**

```powershell
dotnet ef database update
```

- [ ] **Step 5: Verify schema**

```powershell
sqlite3 app.db ".schema ShiftAssignments" | Select-String "SourceTimeOffRequestId"
```

Expected: line containing `SourceTimeOffRequestId INTEGER` and a foreign key reference to `TimeOffRequests`.

- [ ] **Step 6: Commit**

```powershell
git add Migrations/ Models/ShiftAssignment.cs Data/AppDbContext.cs
git commit -m "feat: add SourceTimeOffRequestId to ShiftAssignments"
```

---

### Task 2: Migration `AddPrivateToTimeOffRequests`

**Files:**
- Create: `Migrations/{timestamp}_AddPrivateToTimeOffRequests.cs`
- Modify: `Models/TimeOffRequest.cs`

- [ ] **Step 1: Add property to `TimeOffRequest.cs`**

```csharp
public bool Private { get; set; } = false;
```

- [ ] **Step 2: Generate migration**

```powershell
dotnet ef migrations add AddPrivateToTimeOffRequests
```

- [ ] **Step 3: Inspect generated migration; ensure `defaultValue: false`**

Open `Migrations/{timestamp}_AddPrivateToTimeOffRequests.cs`; the `Up` should contain:

```csharp
migrationBuilder.AddColumn<bool>(
    name: "Private",
    table: "TimeOffRequests",
    nullable: false,
    defaultValue: false);
```

- [ ] **Step 4: Apply**

```powershell
dotnet ef database update
```

- [ ] **Step 5: Commit**

```powershell
git add Migrations/ Models/TimeOffRequest.cs
git commit -m "feat: add Private flag to TimeOffRequest"
```

---

### Task 3: Migration `AddDualApprovalTrackingToTimeOffRequests`

**Files:**
- Create: `Migrations/{timestamp}_AddDualApprovalTrackingToTimeOffRequests.cs`
- Modify: `Models/TimeOffRequest.cs`
- Modify: `Data/AppDbContext.cs`

- [ ] **Step 1: Add four properties to `TimeOffRequest.cs`**

```csharp
public int? FirstApprovalActorId { get; set; }
public DateTime? FirstApprovalActedAt { get; set; }
public int? SecondApprovalActorId { get; set; }
public DateTime? SecondApprovalActedAt { get; set; }

public AppUser? FirstApprovalActor { get; set; }
public AppUser? SecondApprovalActor { get; set; }
```

- [ ] **Step 2: Add FK config in `AppDbContext.OnModelCreating`**

```csharp
modelBuilder.Entity<TimeOffRequest>()
    .HasOne(t => t.FirstApprovalActor)
    .WithMany()
    .HasForeignKey(t => t.FirstApprovalActorId)
    .OnDelete(DeleteBehavior.SetNull);

modelBuilder.Entity<TimeOffRequest>()
    .HasOne(t => t.SecondApprovalActor)
    .WithMany()
    .HasForeignKey(t => t.SecondApprovalActorId)
    .OnDelete(DeleteBehavior.SetNull);
```

- [ ] **Step 3: Generate and apply migration**

```powershell
dotnet ef migrations add AddDualApprovalTrackingToTimeOffRequests
dotnet ef database update
```

- [ ] **Step 4: Commit**

```powershell
git add Migrations/ Models/TimeOffRequest.cs Data/AppDbContext.cs
git commit -m "feat: add dual approval tracking columns to TimeOffRequest"
```

---

### Task 4: Migration `RemoveHomeTypeDefaultTimes`

**Files:**
- Create: `Migrations/{timestamp}_RemoveHomeTypeDefaultTimes.cs`
- Modify: `Models/HomeType.cs`

- [ ] **Step 1: Verify zero callers**

```powershell
git grep -nE "DefaultStartTime|DefaultEndTime" -- '*.cs' '*.cshtml' | Select-Object -First 20
```

Expected: matches only in `Models/HomeType.cs` and possibly migration history. Confirm no production reads.

- [ ] **Step 2: Remove properties from `HomeType.cs`**

Delete the lines:

```csharp
public TimeOnly? DefaultStartTime { get; set; }
public TimeOnly? DefaultEndTime { get; set; }
```

- [ ] **Step 3: Generate and apply migration**

```powershell
dotnet ef migrations add RemoveHomeTypeDefaultTimes
dotnet ef database update
```

- [ ] **Step 4: Build to confirm no compile errors**

```powershell
dotnet build --no-restore
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Commit**

```powershell
git add Migrations/ Models/HomeType.cs
git commit -m "feat: remove unused HomeType.DefaultStart/EndTime"
```

---

### Task 5: Migration `AddLastGeneratedAtToHomeTypes`

**Files:**
- Create: `Migrations/{timestamp}_AddLastGeneratedAtToHomeTypes.cs`
- Modify: `Models/HomeType.cs`

- [ ] **Step 1: Add property to `HomeType.cs`**

```csharp
public DateTime? LastGeneratedAt { get; set; }
```

- [ ] **Step 2: Generate and apply**

```powershell
dotnet ef migrations add AddLastGeneratedAtToHomeTypes
dotnet ef database update
```

- [ ] **Step 3: Commit**

```powershell
git add Migrations/ Models/HomeType.cs
git commit -m "feat: add HomeType.LastGeneratedAt for regen banner detection"
```

---

### Task 6: New entity + migration `AddMoleculeApprovalSettings`

**Files:**
- Create: `Models/MoleculeApprovalSettings.cs`
- Create: `Migrations/{timestamp}_AddMoleculeApprovalSettings.cs`
- Modify: `Data/AppDbContext.cs`

- [ ] **Step 1: Create `Models/MoleculeApprovalSettings.cs`**

```csharp
namespace ShiftManager.Models;

public class MoleculeApprovalSettings
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public int DualApprovalDayThreshold { get; set; } = 7;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int UpdatedByUserId { get; set; }

    public Molecule? Molecule { get; set; }
    public AppUser? UpdatedBy { get; set; }
}
```

- [ ] **Step 2: Add `DbSet` and config to `AppDbContext.cs`**

```csharp
public DbSet<MoleculeApprovalSettings> MoleculeApprovalSettings { get; set; }

// in OnModelCreating:
modelBuilder.Entity<MoleculeApprovalSettings>(b => {
    b.HasOne(m => m.Molecule)
        .WithMany()
        .HasForeignKey(m => m.MoleculeId)
        .OnDelete(DeleteBehavior.Cascade);
    b.HasOne(m => m.UpdatedBy)
        .WithMany()
        .HasForeignKey(m => m.UpdatedByUserId)
        .OnDelete(DeleteBehavior.Restrict);
    b.HasIndex(m => m.MoleculeId).IsUnique();
});
```

- [ ] **Step 3: Generate and apply migration**

```powershell
dotnet ef migrations add AddMoleculeApprovalSettings
dotnet ef database update
```

- [ ] **Step 4: Backfill — insert default rows for every existing molecule**

Add a one-shot data migration via `Program.cs` startup (in the `EnsureCreated` / seed section):

```csharp
// Seed default approval settings for any molecule missing one
var moleculeIds = await db.Molecules.Select(m => m.Id).ToListAsync();
var existingIds = await db.MoleculeApprovalSettings.Select(s => s.MoleculeId).ToListAsync();
var systemUserId = await db.Users.Where(u => u.Email == "owner@test.com").Select(u => u.Id).FirstOrDefaultAsync();
foreach (var mid in moleculeIds.Except(existingIds))
{
    db.MoleculeApprovalSettings.Add(new MoleculeApprovalSettings
    {
        MoleculeId = mid,
        DualApprovalDayThreshold = 7,
        UpdatedAt = DateTime.UtcNow,
        UpdatedByUserId = systemUserId
    });
}
await db.SaveChangesAsync();
```

- [ ] **Step 5: Commit**

```powershell
git add Migrations/ Models/MoleculeApprovalSettings.cs Data/AppDbContext.cs Program.cs
git commit -m "feat: add MoleculeApprovalSettings entity for per-molecule dual-approval threshold"
```

---

### Task 7: Seed `HOME_PM` and `HOME_AM` ShiftType variants

**Files:**
- Modify: `Models/ShiftType.cs`
- Modify: `Services/HomeTypeService.cs` (auto-creation block)
- Test: `ShiftManager.Tests/UnitTests/Services/HomeTypeServiceTests.cs`

- [ ] **Step 1: Add constants to `ShiftType.cs`**

Below the existing `KEY_HOME` constant:

```csharp
public const string KEY_HOME_PM = "HOME_PM";
public const string KEY_HOME_AM = "HOME_AM";
```

- [ ] **Step 2: Write test for auto-seeding all three HOME variants**

In `HomeTypeServiceTests.cs`:

```csharp
[Fact]
public async Task GenerateHomeShifts_CreatesAllThreeHomeShiftTypes_WhenMissing()
{
    using var ctx = TestDb.Create();
    var molecule = new Molecule { Name = "M1" }; ctx.Molecules.Add(molecule);
    await ctx.SaveChangesAsync();
    var svc = TestDb.MakeHomeTypeService(ctx);

    // Act: trigger generation (which should auto-create the 3 HOME variants)
    var ht = new HomeType { MoleculeId = molecule.Id, Name = "Pattern A" };
    ctx.HomeTypes.Add(ht); await ctx.SaveChangesAsync();
    await svc.GenerateHomeShiftsAsync(ht.Id, DateOnly.FromDateTime(DateTime.Today),
        DateOnly.FromDateTime(DateTime.Today).AddDays(7), new List<int>(), 1);

    var keys = await ctx.ShiftTypes
        .Where(st => st.MoleculeId == molecule.Id)
        .Select(st => st.Key).ToListAsync();
    Assert.Contains(ShiftType.KEY_HOME, keys);
    Assert.Contains(ShiftType.KEY_HOME_PM, keys);
    Assert.Contains(ShiftType.KEY_HOME_AM, keys);
}
```

- [ ] **Step 3: Run test to verify failure**

```powershell
dotnet test --filter "FullyQualifiedName~HomeTypeServiceTests.GenerateHomeShifts_CreatesAllThreeHomeShiftTypes_WhenMissing"
```

Expected: FAIL — only HOME exists.

- [ ] **Step 4: Update `HomeTypeService.GenerateHomeShiftsAsync` auto-creation**

Locate the block that creates the HOME ShiftType (search `KEY_HOME` in `HomeTypeService.cs`). Replace with creating all three:

```csharp
async Task<int> EnsureHomeShiftTypeAsync(string key, TimeOnly start, TimeOnly end, string nameEn, string nameHe)
{
    var existing = await _db.ShiftTypes
        .Where(st => st.MoleculeId == moleculeId && st.Key == key)
        .Select(st => st.Id).FirstOrDefaultAsync();
    if (existing > 0) return existing;
    var st = new ShiftType
    {
        MoleculeId = moleculeId,
        Key = key,
        Start = start,
        End = end,
        NameEn = nameEn,
        NameHe = nameHe,
        RowColor = "#F8E7B1",
        Scope = ShiftScope.Molecule
    };
    _db.ShiftTypes.Add(st);
    await _db.SaveChangesAsync();
    return st.Id;
}

await EnsureHomeShiftTypeAsync(ShiftType.KEY_HOME,    new TimeOnly(0, 0),  new TimeOnly(23, 59), "Home",  "בית");
await EnsureHomeShiftTypeAsync(ShiftType.KEY_HOME_PM, new TimeOnly(16, 0), new TimeOnly(23, 59), "After", "אפטר");
await EnsureHomeShiftTypeAsync(ShiftType.KEY_HOME_AM, new TimeOnly(0, 0),  new TimeOnly(13, 0),  "After", "אפטר");
```

- [ ] **Step 5: Run test to verify pass**

```powershell
dotnet test --filter "FullyQualifiedName~HomeTypeServiceTests.GenerateHomeShifts_CreatesAllThreeHomeShiftTypes_WhenMissing"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add Models/ShiftType.cs Services/HomeTypeService.cs ShiftManager.Tests/UnitTests/Services/HomeTypeServiceTests.cs
git commit -m "feat: seed HOME_PM and HOME_AM ShiftType variants"
```

---

### Task 8: Add `RequestStatus.PendingSecondApproval` enum value

**Files:**
- Modify: `Models/Support/Enums.cs`

- [ ] **Step 1: Locate `RequestStatus` enum (currently has values 0-3)**

```powershell
git grep -n "enum RequestStatus" -- '*.cs'
```

Expected: one match in `Models/Support/Enums.cs`.

- [ ] **Step 2: Add new value**

```csharp
public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2,
    Canceled = 3,
    PendingSecondApproval = 4
}
```

- [ ] **Step 3: Build to confirm no compile errors**

```powershell
dotnet build --no-restore
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```powershell
git add Models/Support/Enums.cs
git commit -m "feat: add RequestStatus.PendingSecondApproval for dual-approval flow"
```

---

### Task 9: Generalise `ShiftType.IsHome` to recognise all HOME variants

**Files:**
- Modify: `Models/ShiftType.cs`
- Test: `ShiftManager.Tests/UnitTests/Models/ShiftTypeTests.cs` (create if missing)

- [ ] **Step 1: Write tests**

Create `ShiftManager.Tests/UnitTests/Models/ShiftTypeTests.cs`:

```csharp
using ShiftManager.Models;
using Xunit;

public class ShiftTypeTests
{
    [Theory]
    [InlineData(ShiftType.KEY_HOME, true)]
    [InlineData(ShiftType.KEY_HOME_PM, true)]
    [InlineData(ShiftType.KEY_HOME_AM, true)]
    [InlineData("MORNING", false)]
    [InlineData("OFFLINE", false)]
    public void IsHome_RecognisesAllHomeVariants(string key, bool expected)
    {
        var st = new ShiftType { Key = key };
        Assert.Equal(expected, st.IsHome);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

```powershell
dotnet test --filter "FullyQualifiedName~ShiftTypeTests"
```

Expected: 2 tests FAIL (HOME_PM, HOME_AM return false).

- [ ] **Step 3: Update `ShiftType.cs` IsHome computed property**

```csharp
[NotMapped]
public bool IsHome => Key == KEY_HOME || Key == KEY_HOME_PM || Key == KEY_HOME_AM;
```

- [ ] **Step 4: Run tests to confirm pass**

```powershell
dotnet test --filter "FullyQualifiedName~ShiftTypeTests"
```

Expected: 5/5 PASS.

- [ ] **Step 5: Commit**

```powershell
git add Models/ShiftType.cs ShiftManager.Tests/UnitTests/Models/ShiftTypeTests.cs
git commit -m "feat: ShiftType.IsHome recognises HOME, HOME_PM, HOME_AM variants"
```

---

### Task 10: Refactor `Key == KEY_HOME` literals to use `IsHome` property

**Files:**
- Modify: `Services/BusyService.cs:102, 408-409`
- Modify: `Services/HomeTypeService.cs:275, 352, 359`
- Modify: `Services/AnalyticsService.cs:425`

- [ ] **Step 1: Identify all literals**

```powershell
git grep -nE 'Key\s*==\s*ShiftType\.KEY_HOME|Key\s*==\s*"HOME"\b|exemptKeys\s*=' -- '*.cs'
```

Capture the list of files+lines. Verify they match the spec's §13.1.1 list.

- [ ] **Step 2: Update `Services/BusyService.cs`**

Replace literal comparisons with `IsHome` access. For example, line 102 area:

```csharp
// Before:
IsHome: userShift.Key == ShiftType.KEY_HOME,
// After:
IsHome: userShift.Key == ShiftType.KEY_HOME || userShift.Key == ShiftType.KEY_HOME_PM || userShift.Key == ShiftType.KEY_HOME_AM,
```

For lines 408-409 (where the projection reads `sa.ShiftInstance.ShiftType.Key`):

```csharp
IsOffline = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_OFFLINE,
IsHome = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
      || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
      || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM,
```

(Direct property access `sa.ShiftInstance.ShiftType.IsHome` won't translate to SQL because EF can't translate `[NotMapped]` properties; use the literal comparison.)

- [ ] **Step 3: Update `Services/HomeTypeService.cs`**

For line 275 (Select projection):

```csharp
.Select(sa => new {
    sa.UserId,
    sa.ShiftInstance.WorkDate,
    IsHome = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
          || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
          || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM
})
```

Lines 352, 359 use `existing.Any(s => s.IsHome)` against the projection — those work as long as the projection's `IsHome` is correct. No change needed there beyond Step 3 above.

- [ ] **Step 4: Update `Services/AnalyticsService.cs:425`**

```csharp
// Before:
var exemptKeys = new[] { ShiftManager.Models.ShiftType.KEY_HOME, ShiftManager.Models.ShiftType.KEY_OFFLINE };
// After:
var exemptKeys = new[] {
    ShiftType.KEY_HOME,
    ShiftType.KEY_HOME_PM,
    ShiftType.KEY_HOME_AM,
    ShiftType.KEY_OFFLINE
};
```

- [ ] **Step 5: Run all tests to confirm no regressions**

```powershell
dotnet test --filter "FullyQualifiedName~ShiftValidation|FullyQualifiedName~AnalyticsService|FullyQualifiedName~HomeTypeService"
```

Expected: All previously-passing tests still pass.

- [ ] **Step 6: Commit**

```powershell
git add Services/BusyService.cs Services/HomeTypeService.cs Services/AnalyticsService.cs
git commit -m "refactor: extend HOME exemption checks to cover HOME_PM and HOME_AM"
```

---

## Phase 2 — Anchor fix for DerivedRotationRule

### Task 11: Extend `DerivedRotationRule` record with `Anchor`

**Files:**
- Modify: `Services/IHomeTypeService.cs:11-17`

- [ ] **Step 1: Update record signature**

```csharp
public record DerivedRotationRule(
    int CycleWeeks,
    List<DayOfWeek> HomeDays,
    List<int> WeekOffsets,
    DateOnly Anchor,           // NEW — Monday of week 1 of the cycle
    TimeOnly? StartTime,
    TimeOnly? EndTime
);
```

- [ ] **Step 2: Build to surface call sites**

```powershell
dotnet build 2>&1 | Select-String "error CS"
```

Expected: errors at every callsite that constructs `DerivedRotationRule`. Note them down.

- [ ] **Step 3: Update `DeriveRuleFromPattern` (HomeTypeService.cs:140) to compute Anchor**

```csharp
// Before line 140:
var anchorMonday = paintedDates.Min().AddDays(-(((int)paintedDates.Min().DayOfWeek + 6) % 7));

// Line 140 becomes:
return new DerivedRotationRule(cycleWeeks, homeDays, weekOffsets, anchorMonday, null, null);
```

- [ ] **Step 4: Build clean**

```powershell
dotnet build --no-restore 2>&1 | Select-String "error CS"
```

Expected: zero errors.

- [ ] **Step 5: Commit**

```powershell
git add Services/IHomeTypeService.cs Services/HomeTypeService.cs
git commit -m "feat: add Anchor field to DerivedRotationRule for deterministic cycle phase"
```

---

### Task 12: Update `GenerateDatesFromRule` algorithm to use Anchor

**Files:**
- Modify: `Services/HomeTypeService.cs:465-487`
- Test: `ShiftManager.Tests/UnitTests/Services/HomeTypeServiceTests.cs`

- [ ] **Step 1: Write the determinism test**

In `HomeTypeServiceTests.cs`:

```csharp
[Fact]
public void GenerateDatesFromRule_IsDeterministic_AcrossDifferentRanges()
{
    // Cycle 3 weeks, weekOffsets [0], homeDays Sunday, anchor Mar 30 2026 (Monday)
    var rule = new DerivedRotationRule(
        CycleWeeks: 3,
        HomeDays: new List<DayOfWeek> { DayOfWeek.Sunday },
        WeekOffsets: new List<int> { 0 },
        Anchor: new DateOnly(2026, 3, 30),
        StartTime: null, EndTime: null);

    // Range 1: Apr 1 - Apr 30
    var dates1 = HomeTypeService.GenerateDatesFromRulePublicForTest(
        rule, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

    // Range 2: May 5 - May 12 (includes Sun May 10, which is cycleWeek 5%3=2 → SKIP)
    var dates2 = HomeTypeService.GenerateDatesFromRulePublicForTest(
        rule, new DateOnly(2026, 5, 5), new DateOnly(2026, 5, 12));

    // Range 3 (overlap test): Apr 1 - May 31
    var dates3 = HomeTypeService.GenerateDatesFromRulePublicForTest(
        rule, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31));

    // Determinism: dates2 should be empty (May 10 is cycleWeek 2)
    Assert.Empty(dates2);

    // dates1's matches must equal dates3's matches within Apr 1-30
    var dates3InApril = dates3.Where(d => d <= new DateOnly(2026, 4, 30)).ToList();
    Assert.Equal(dates1, dates3InApril);
}
```

- [ ] **Step 2: Expose `GenerateDatesFromRule` for testing**

In `HomeTypeService.cs`, change visibility:

```csharp
// Before: private static List<DateOnly> GenerateDatesFromRule(...)
// Add internal-test-friendly exposure:
internal static List<DateOnly> GenerateDatesFromRulePublicForTest(DerivedRotationRule rule, DateOnly start, DateOnly end)
    => GenerateDatesFromRule(rule, start, end);
```

(Optional: add `[InternalsVisibleTo("ShiftManager.Tests")]` in `Properties/AssemblyInfo.cs` if not already.)

- [ ] **Step 3: Run test to verify failure**

```powershell
dotnet test --filter "FullyQualifiedName~GenerateDatesFromRule_IsDeterministic"
```

Expected: FAIL. The current algorithm returns May 10 in dates2 because startMonday=May 4 makes May 10 cycleWeek 0.

- [ ] **Step 4: Update `GenerateDatesFromRule` to use `rule.Anchor`**

Replace lines 465-487 in `HomeTypeService.cs`:

```csharp
private static List<DateOnly> GenerateDatesFromRule(DerivedRotationRule rule, DateOnly start, DateOnly end)
{
    var dates = new List<DateOnly>();
    var homeDaySet = rule.HomeDays.ToHashSet();
    var anchorMonday = rule.Anchor;  // pre-normalised to a Monday at save time

    for (var date = start; date <= end; date = date.AddDays(1))
    {
        if (!homeDaySet.Contains(date.DayOfWeek)) continue;

        var weekNum = (date.DayNumber - anchorMonday.DayNumber) / 7;
        if (weekNum < 0) continue;  // before anchor → no rotation

        var cycleWeek = weekNum % rule.CycleWeeks;
        if (rule.WeekOffsets.Contains(cycleWeek))
            dates.Add(date);
    }

    return dates;
}
```

- [ ] **Step 5: Run test to verify pass**

```powershell
dotnet test --filter "FullyQualifiedName~GenerateDatesFromRule_IsDeterministic"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add Services/HomeTypeService.cs ShiftManager.Tests/UnitTests/Services/HomeTypeServiceTests.cs
git commit -m "fix: GenerateDatesFromRule uses fixed rule.Anchor for deterministic cycle phase"
```

---

### Task 13: Backfill `Anchor` in existing `HomeType.DerivedRule` JSONs

**Files:**
- Modify: `Program.cs` (startup data backfill block)

- [ ] **Step 1: Add backfill block to `Program.cs` (after the existing `EnsureCreated`/seed sections)**

```csharp
// Backfill DerivedRotationRule.Anchor for legacy HomeTypes
var legacyHomeTypes = await db.HomeTypes
    .Where(h => h.DerivedRule != null && h.DerivedRule != "")
    .ToListAsync();

foreach (var ht in legacyHomeTypes)
{
    var rule = JsonSerializer.Deserialize<DerivedRotationRule>(ht.DerivedRule!,
        new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
    if (rule == null) continue;

    // Detect "default Anchor" (= 0001-01-01) and replace
    if (rule.Anchor == default)
    {
        DateOnly anchor;
        if (!string.IsNullOrEmpty(ht.PatternJson))
        {
            var paintedDates = JsonSerializer.Deserialize<List<string>>(ht.PatternJson) ?? new();
            var parsed = paintedDates.Select(s => DateOnly.TryParse(s, out var d) ? d : default).Where(d => d != default).ToList();
            anchor = parsed.Any() ? parsed.Min().AddDays(-(((int)parsed.Min().DayOfWeek + 6) % 7))
                                  : new DateOnly(2026, 1, 5);
        }
        else
        {
            anchor = new DateOnly(2026, 1, 5);  // fallback Monday
        }

        var fixedRule = new DerivedRotationRule(rule.CycleWeeks, rule.HomeDays, rule.WeekOffsets, anchor, rule.StartTime, rule.EndTime);
        ht.DerivedRule = JsonSerializer.Serialize(fixedRule);
    }
}
await db.SaveChangesAsync();
```

- [ ] **Step 2: Run backfill against dev DB**

```powershell
dotnet run --project . --no-launch-profile --urls http://localhost:5000
# wait until "Now listening on" appears, then ctrl+c (the seed runs at startup)
```

- [ ] **Step 3: Verify the JSON now contains Anchor**

```powershell
sqlite3 app.db "SELECT Id, DerivedRule FROM HomeTypes WHERE DerivedRule IS NOT NULL LIMIT 3"
```

Expected: each `DerivedRule` JSON contains an `Anchor` field with a valid YYYY-MM-DD value.

- [ ] **Step 4: Commit**

```powershell
git add Program.cs
git commit -m "feat: backfill DerivedRotationRule.Anchor for legacy HomeTypes on startup"
```

---

## Phase 3 — Materialiser service

### Task 14: Create `IHomeMaterialiserService` interface

**Files:**
- Create: `Services/IHomeMaterialiserService.cs`

- [ ] **Step 1: Create interface file**

```csharp
namespace ShiftManager.Services;

public interface IHomeMaterialiserService
{
    /// <summary>
    /// Idempotent diff-and-sync: ensures ShiftAssignment rows for a TimeOffRequest
    /// match the request's current state (Approved → desired set; Declined/Canceled → empty).
    /// </summary>
    Task SyncMaterialisedHomeRowsAsync(int timeOffRequestId);

    /// <summary>
    /// Restore rotation HOME rows for a user across a date range, after vacation cancellation.
    /// </summary>
    Task RestoreRotationHomeAsync(int userId, DateOnly start, DateOnly end);
}
```

- [ ] **Step 2: Commit**

```powershell
git add Services/IHomeMaterialiserService.cs
git commit -m "feat: add IHomeMaterialiserService interface"
```

---

### Task 15: Implement `HomeMaterialiserService.SyncMaterialisedHomeRowsAsync`

**Files:**
- Create: `Services/HomeMaterialiserService.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/HomeMaterialiserServiceTests.cs`

- [ ] **Step 1: Write idempotency test**

Create `HomeMaterialiserServiceTests.cs`:

```csharp
public class HomeMaterialiserServiceTests
{
    [Fact]
    public async Task Sync_ApprovedAfter_CreatesTwoHomeRows_HOME_PM_AND_HOME_AM()
    {
        using var ctx = TestDb.Create();
        var (user, molecule, _) = TestDb.SeedBasicTenant(ctx);
        await TestDb.SeedAllHomeShiftTypesAsync(ctx, molecule.Id);

        var req = new TimeOffRequest
        {
            UserId = user.Id, CompanyId = user.CompanyId,
            Type = TimeOffType.After,
            StartDate = new DateOnly(2026, 5, 7),
            EndDate = new DateOnly(2026, 5, 7),
            Status = RequestStatus.Approved
        };
        ctx.TimeOffRequests.Add(req); await ctx.SaveChangesAsync();

        var svc = TestDb.MakeMaterialiserService(ctx);
        await svc.SyncMaterialisedHomeRowsAsync(req.Id);

        var rows = await ctx.ShiftAssignments
            .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
            .Where(sa => sa.SourceTimeOffRequestId == req.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
                                && r.ShiftInstance.WorkDate == new DateOnly(2026, 5, 7));
        Assert.Contains(rows, r => r.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM
                                && r.ShiftInstance.WorkDate == new DateOnly(2026, 5, 8));
    }

    [Fact]
    public async Task Sync_RunningTwice_IsIdempotent()
    {
        using var ctx = TestDb.Create();
        var (user, molecule, _) = TestDb.SeedBasicTenant(ctx);
        await TestDb.SeedAllHomeShiftTypesAsync(ctx, molecule.Id);
        var req = TestDb.MakeApprovedAfter(ctx, user, new DateOnly(2026, 5, 7));
        var svc = TestDb.MakeMaterialiserService(ctx);

        await svc.SyncMaterialisedHomeRowsAsync(req.Id);
        var firstCount = await ctx.ShiftAssignments.CountAsync(sa => sa.SourceTimeOffRequestId == req.Id);

        await svc.SyncMaterialisedHomeRowsAsync(req.Id);
        var secondCount = await ctx.ShiftAssignments.CountAsync(sa => sa.SourceTimeOffRequestId == req.Id);

        Assert.Equal(firstCount, secondCount);
        Assert.Equal(2, secondCount);
    }

    [Fact]
    public async Task Sync_CanceledRequest_DeletesAllMaterialisedRows()
    {
        using var ctx = TestDb.Create();
        var (user, molecule, _) = TestDb.SeedBasicTenant(ctx);
        await TestDb.SeedAllHomeShiftTypesAsync(ctx, molecule.Id);
        var req = TestDb.MakeApprovedAfter(ctx, user, new DateOnly(2026, 5, 7));
        var svc = TestDb.MakeMaterialiserService(ctx);

        await svc.SyncMaterialisedHomeRowsAsync(req.Id);
        Assert.Equal(2, await ctx.ShiftAssignments.CountAsync(sa => sa.SourceTimeOffRequestId == req.Id));

        req.Status = RequestStatus.Canceled;
        await ctx.SaveChangesAsync();

        await svc.SyncMaterialisedHomeRowsAsync(req.Id);
        Assert.Equal(0, await ctx.ShiftAssignments.CountAsync(sa => sa.SourceTimeOffRequestId == req.Id));
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test --filter "FullyQualifiedName~HomeMaterialiserService"
```

Expected: FAIL — `HomeMaterialiserService` doesn't exist yet.

- [ ] **Step 3: Implement `HomeMaterialiserService`**

Create `Services/HomeMaterialiserService.cs`:

```csharp
namespace ShiftManager.Services;

public class HomeMaterialiserService : IHomeMaterialiserService
{
    private readonly AppDbContext _db;
    private readonly ILogger<HomeMaterialiserService> _logger;
    private readonly IHubContext<CalendarHub>? _hub;
    private readonly IHomeTypeService _homeTypeService;

    public HomeMaterialiserService(
        AppDbContext db,
        ILogger<HomeMaterialiserService> logger,
        IHomeTypeService homeTypeService,
        IHubContext<CalendarHub>? hub = null)
    {
        _db = db;
        _logger = logger;
        _homeTypeService = homeTypeService;
        _hub = hub;
    }

    public async Task SyncMaterialisedHomeRowsAsync(int timeOffRequestId)
    {
        var req = await _db.TimeOffRequests.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == timeOffRequestId);
        if (req == null) return;

        var desired = ComputeDesiredRows(req);
        var existing = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.SourceTimeOffRequestId == timeOffRequestId)
            .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
            .ToListAsync();

        using var tx = await _db.Database.BeginTransactionAsync();

        // Delete: existing rows not in desired
        var desiredKeys = desired.Select(d => (d.WorkDate, d.ShiftTypeKey)).ToHashSet();
        var toDelete = existing
            .Where(sa => !desiredKeys.Contains((sa.ShiftInstance.WorkDate, sa.ShiftInstance.ShiftType.Key)))
            .ToList();
        _db.ShiftAssignments.RemoveRange(toDelete);

        // Insert: desired rows not in existing
        var existingKeys = existing.Select(sa => (sa.ShiftInstance.WorkDate, sa.ShiftInstance.ShiftType.Key)).ToHashSet();
        foreach (var d in desired.Where(d => !existingKeys.Contains((d.WorkDate, d.ShiftTypeKey))))
        {
            var instanceId = await EnsureInstanceAsync(req.UserId, d.WorkDate, d.ShiftTypeKey);
            _db.ShiftAssignments.Add(new ShiftAssignment
            {
                CompanyId = req.CompanyId,
                ShiftInstanceId = instanceId,
                UserId = req.UserId,
                CreatedAt = DateTime.UtcNow,
                SourceTimeOffRequestId = req.Id
            });
        }

        // Vacation supersedes rotation: delete rotation HOME rows in vacation's date range
        if (req.Status == RequestStatus.Approved && req.Type == TimeOffType.Vacation)
        {
            var rotationToRemove = await _db.ShiftAssignments.IgnoreQueryFilters()
                .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
                .Where(sa => sa.UserId == req.UserId
                          && sa.SourceTimeOffRequestId == null
                          && sa.ShiftInstance.WorkDate >= req.StartDate
                          && sa.ShiftInstance.WorkDate <= req.EndDate
                          && (sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
                           || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
                           || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM))
                .ToListAsync();
            _db.ShiftAssignments.RemoveRange(rotationToRemove);
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Broadcast SignalR
        if (_hub != null)
        {
            var moleculeId = await _db.Companies.Where(c => c.Id == req.CompanyId).Select(c => c.MoleculeId).FirstOrDefaultAsync();
            await _hub.Clients.Group($"shifts-{moleculeId}-*").SendAsync("ShiftsUpdated");
        }

        _logger.LogInformation("Materialised TimeOffRequest {Id}: {DesiredCount} desired, {ExistingCount} existing",
            timeOffRequestId, desired.Count, existing.Count);
    }

    private List<(DateOnly WorkDate, string ShiftTypeKey)> ComputeDesiredRows(TimeOffRequest req)
    {
        if (req.Status != RequestStatus.Approved) return new();
        var rows = new List<(DateOnly, string)>();
        if (req.Type == TimeOffType.After)
        {
            rows.Add((req.StartDate, ShiftType.KEY_HOME_PM));
            rows.Add((req.StartDate.AddDays(1), ShiftType.KEY_HOME_AM));
        }
        else if (req.Type == TimeOffType.Vacation)
        {
            for (var d = req.StartDate; d <= req.EndDate; d = d.AddDays(1))
                rows.Add((d, ShiftType.KEY_HOME));
            rows.Add((req.EndDate.AddDays(1), ShiftType.KEY_HOME_AM));
        }
        return rows;
    }

    private async Task<int> EnsureInstanceAsync(int userId, DateOnly workDate, string shiftTypeKey)
    {
        var moleculeId = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Join(_db.Companies, u => u.CompanyId, c => c.Id, (u, c) => c.MoleculeId)
            .FirstOrDefaultAsync();

        var stId = await _db.ShiftTypes
            .Where(st => st.MoleculeId == moleculeId && st.Key == shiftTypeKey)
            .Select(st => st.Id).FirstOrDefaultAsync();

        var existing = await _db.ShiftInstances.IgnoreQueryFilters()
            .FirstOrDefaultAsync(si => si.WorkDate == workDate && si.ShiftTypeId == stId);
        if (existing != null) return existing.Id;

        var inst = new ShiftInstance
        {
            CompanyId = await _db.Users.IgnoreQueryFilters()
                .Where(u => u.Id == userId).Select(u => u.CompanyId).FirstOrDefaultAsync(),
            WorkDate = workDate,
            ShiftTypeId = stId,
            StaffingRequired = 99,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ShiftInstances.Add(inst);
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    public async Task RestoreRotationHomeAsync(int userId, DateOnly start, DateOnly end)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.HomeTypeId == null) return;

        // Trigger HomeTypeService to (re-)materialise rotation HOME for this user/range.
        await _homeTypeService.GenerateHomeShiftsAsync(
            user.HomeTypeId.Value, start, end, new List<int> { userId },
            createdByUserId: 0, RegenerationMode.KeepManualChanges);
    }
}
```

- [ ] **Step 4: Register in DI**

In `Program.cs`, add:

```csharp
builder.Services.AddScoped<IHomeMaterialiserService, HomeMaterialiserService>();
```

- [ ] **Step 5: Run tests to verify pass**

```powershell
dotnet test --filter "FullyQualifiedName~HomeMaterialiserService"
```

Expected: 3/3 PASS.

- [ ] **Step 6: Commit**

```powershell
git add Services/HomeMaterialiserService.cs Services/IHomeMaterialiserService.cs Program.cs ShiftManager.Tests/UnitTests/Services/HomeMaterialiserServiceTests.cs
git commit -m "feat: HomeMaterialiserService syncs ShiftAssignments from TimeOffRequest state"
```

---

### Task 16: Wire materialiser into `IVacationApprovalService.ApproveAsync` / Decline / Cancel

**Files:**
- Modify: `Services/VacationApprovalService.cs`

- [ ] **Step 1: Inject `IHomeMaterialiserService`**

In the constructor:

```csharp
private readonly IHomeMaterialiserService _materialiser;

public VacationApprovalService(/* existing args */, IHomeMaterialiserService materialiser)
    : base(/* existing args */)
{
    /* existing assignments */
    _materialiser = materialiser;
}
```

- [ ] **Step 2: Call materialiser at the end of `ApproveAsync` (after status save)**

After `SaveWithConcurrencyHandlingAsync` confirms the status update:

```csharp
await _materialiser.SyncMaterialisedHomeRowsAsync(request.Id);
```

- [ ] **Step 3: Call materialiser in `DeclineAsync` and `CancelRequestAsync`**

Same pattern — after status persistence, call:

```csharp
await _materialiser.SyncMaterialisedHomeRowsAsync(request.Id);
```

- [ ] **Step 4: For Vacation cancellation: also restore rotation HOME**

In `CancelRequestAsync`, after the materialiser call:

```csharp
if (request.Type == TimeOffType.Vacation && request.Status == RequestStatus.Canceled)
{
    await _materialiser.RestoreRotationHomeAsync(request.UserId, request.StartDate, request.EndDate);
}
```

- [ ] **Step 5: Run all VacationApproval tests**

```powershell
dotnet test --filter "FullyQualifiedName~VacationApproval"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add Services/VacationApprovalService.cs
git commit -m "feat: wire HomeMaterialiser into approval/decline/cancel paths"
```

---

### Task 17: Add `UpdateRequestDatesAsync` for shorten-range action

**Files:**
- Modify: `Services/IVacationApprovalService.cs`
- Modify: `Services/VacationApprovalService.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/VacationApprovalShortenTests.cs` (create)

- [ ] **Step 1: Write the test**

```csharp
public class VacationApprovalShortenTests
{
    [Fact]
    public async Task UpdateRequestDates_Shorten_RemovesMaterialisedRowsOutsideNewRange()
    {
        using var ctx = TestDb.Create();
        var (user, molecule, _) = TestDb.SeedBasicTenant(ctx);
        await TestDb.SeedAllHomeShiftTypesAsync(ctx, molecule.Id);

        var req = TestDb.MakeApprovedVacation(ctx, user,
            new DateOnly(2026, 5, 17), new DateOnly(2026, 5, 21));
        var svc = TestDb.MakeApprovalService(ctx);
        await svc.UpdateRequestDatesAsync(req.Id, new DateOnly(2026, 5, 17), new DateOnly(2026, 5, 19), actorUserId: 1);

        var rows = await ctx.ShiftAssignments.IgnoreQueryFilters()
            .Include(sa => sa.ShiftInstance)
            .Where(sa => sa.SourceTimeOffRequestId == req.Id)
            .ToListAsync();
        // 3 days of HOME (May 17-19) + 1 HOME_AM tail (May 20) = 4 rows
        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain(rows, r => r.ShiftInstance.WorkDate == new DateOnly(2026, 5, 21));
    }
}
```

- [ ] **Step 2: Add interface method**

```csharp
Task<(bool Success, string Message)> UpdateRequestDatesAsync(
    int requestId, DateOnly newStart, DateOnly newEnd, int actorUserId);
```

- [ ] **Step 3: Run test to verify failure**

```powershell
dotnet test --filter "FullyQualifiedName~VacationApprovalShorten"
```

Expected: FAIL — method not implemented.

- [ ] **Step 4: Implement in `VacationApprovalService`**

```csharp
public async Task<(bool Success, string Message)> UpdateRequestDatesAsync(
    int requestId, DateOnly newStart, DateOnly newEnd, int actorUserId)
{
    var req = await _db.TimeOffRequests.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == requestId);
    if (req == null) return (false, "Request not found");
    if (req.Status != RequestStatus.Approved) return (false, "Only approved requests can be shortened");
    if (req.Type == TimeOffType.After) return (false, "After requests are single-day; shorten not applicable");
    if (newStart < req.StartDate || newEnd > req.EndDate) return (false, "Shorten can only narrow the range, not extend");

    using var tx = await _db.Database.BeginTransactionAsync();
    req.StartDate = newStart;
    req.EndDate = newEnd;
    await _db.SaveChangesAsync();

    await _materialiser.SyncMaterialisedHomeRowsAsync(req.Id);
    // The dates that were in the old range but not the new are now uncovered → restore rotation
    await _materialiser.RestoreRotationHomeAsync(req.UserId, req.StartDate, req.EndDate);

    await tx.CommitAsync();
    await _auditLogService.LogAsync("TimeOffRequestDatesUpdated", "TimeOffRequest", req.Id,
        $"Shortened to {newStart:yyyy-MM-dd}..{newEnd:yyyy-MM-dd} by user {actorUserId}");
    return (true, "Dates updated");
}
```

- [ ] **Step 5: Run test to verify pass**

```powershell
dotnet test --filter "FullyQualifiedName~VacationApprovalShorten"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add Services/IVacationApprovalService.cs Services/VacationApprovalService.cs ShiftManager.Tests/UnitTests/Services/VacationApprovalShortenTests.cs
git commit -m "feat: add UpdateRequestDatesAsync for shorten-range action"
```

---

## Phase 4 — Approval pool & dual-approval

### Task 18: Implement approver-pool query

**Files:**
- Modify: `Services/IVacationApprovalService.cs`
- Modify: `Services/VacationApprovalService.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/ApproverPoolTests.cs` (create)

- [ ] **Step 1: Add interface method**

```csharp
Task<List<AppUser>> GetApproverPoolAsync(int requestId);
```

- [ ] **Step 2: Write the tests**

```csharp
public class ApproverPoolTests
{
    [Fact]
    public async Task Pool_AlhutRequester_IncludesAlhutLeadsAndAlhutDirectorsInMolecule()
    {
        using var ctx = TestDb.Create();
        var molecule = TestDb.SeedMolecule(ctx, name: "M1");
        var company = TestDb.SeedCompany(ctx, molecule.Id);
        var alhut = await ctx.JobTypes.FirstAsync(j => j.Name == "Alhut");
        var requester = TestDb.SeedUser(ctx, company.Id, alhut.Id, roleTemplate: "Employee");

        var alhutLead = TestDb.SeedUser(ctx, company.Id, alhut.Id, roleTemplate: "Lead");
        var alhutDirector = TestDb.SeedUser(ctx, company.Id, alhut.Id, roleTemplate: "Director");
        var textLead = TestDb.SeedUser(ctx, company.Id, jobTypeId: 3 /* Text */, roleTemplate: "Lead");
        var brDirector = TestDb.SeedUser(ctx, company.Id, jobTypeId: 2 /* BR */, roleTemplate: "BRDirector");

        var req = TestDb.MakePendingVacation(ctx, requester, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3));
        var svc = TestDb.MakeApprovalService(ctx);

        var pool = await svc.GetApproverPoolAsync(req.Id);
        var ids = pool.Select(u => u.Id).ToHashSet();

        Assert.Contains(alhutLead.Id, ids);
        Assert.Contains(alhutDirector.Id, ids);
        Assert.DoesNotContain(textLead.Id, ids);    // wrong jobtype
        Assert.DoesNotContain(brDirector.Id, ids);  // not in alhut/text vertical
        Assert.DoesNotContain(requester.Id, ids);   // self-exclusion
    }

    [Fact]
    public async Task Pool_HakamRequester_IncludesBRDirectorsAndMoleculeAdmins()
    {
        using var ctx = TestDb.Create();
        var molecule = TestDb.SeedMolecule(ctx, "M1");
        var company = TestDb.SeedCompany(ctx, molecule.Id);
        var hakam = await ctx.JobTypes.FirstAsync(j => j.Name == "Hakam");
        var requester = TestDb.SeedUser(ctx, company.Id, hakam.Id, roleTemplate: "Employee");
        var brDirector = TestDb.SeedUser(ctx, company.Id, hakam.Id, roleTemplate: "BRDirector");
        var moleculeAdmin = TestDb.SeedUser(ctx, company.Id, hakam.Id, roleTemplate: "MoleculeAdmin");
        var alhutLead = TestDb.SeedUser(ctx, company.Id, jobTypeId: 1 /* Alhut */, roleTemplate: "Lead");

        var req = TestDb.MakePendingVacation(ctx, requester, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3));
        var svc = TestDb.MakeApprovalService(ctx);

        var pool = await svc.GetApproverPoolAsync(req.Id);
        var ids = pool.Select(u => u.Id).ToHashSet();

        Assert.Contains(brDirector.Id, ids);
        Assert.Contains(moleculeAdmin.Id, ids);
        Assert.DoesNotContain(alhutLead.Id, ids);   // alhut/text-only roles
    }

    [Fact]
    public async Task Pool_EmptyStrictPool_FallsBackToMoleculeAdmin()
    {
        using var ctx = TestDb.Create();
        var molecule = TestDb.SeedMolecule(ctx, "M1");
        var company = TestDb.SeedCompany(ctx, molecule.Id);
        var text = await ctx.JobTypes.FirstAsync(j => j.Name == "Text");
        var requester = TestDb.SeedUser(ctx, company.Id, text.Id, roleTemplate: "Employee");
        // No TextLead, no Director — only MoleculeAdmin exists
        var moleculeAdmin = TestDb.SeedUser(ctx, company.Id, text.Id, roleTemplate: "MoleculeAdmin");

        var req = TestDb.MakePendingVacation(ctx, requester, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3));
        var svc = TestDb.MakeApprovalService(ctx);

        var pool = await svc.GetApproverPoolAsync(req.Id);
        Assert.Single(pool);
        Assert.Equal(moleculeAdmin.Id, pool.Single().Id);
    }
}
```

- [ ] **Step 3: Run tests to verify failure**

```powershell
dotnet test --filter "FullyQualifiedName~ApproverPoolTests"
```

Expected: FAIL — `GetApproverPoolAsync` not implemented.

- [ ] **Step 4: Implement in `VacationApprovalService`**

```csharp
private static readonly HashSet<int> AlhutTextJobTypeIds = new() { 1, 3 };  // Alhut=1, Text=3

public async Task<List<AppUser>> GetApproverPoolAsync(int requestId)
{
    var req = await _db.TimeOffRequests.IgnoreQueryFilters()
        .FirstOrDefaultAsync(t => t.Id == requestId);
    if (req == null) return new();

    var requester = await _db.Users.IgnoreQueryFilters()
        .FirstOrDefaultAsync(u => u.Id == req.UserId);
    if (requester == null) return new();

    var moleculeId = await _db.Companies.IgnoreQueryFilters()
        .Where(c => c.Id == requester.CompanyId)
        .Select(c => c.MoleculeId).FirstOrDefaultAsync();

    bool isAlhutOrText = requester.JobTypeId.HasValue && AlhutTextJobTypeIds.Contains(requester.JobTypeId.Value);

    var query = _db.Users.IgnoreQueryFilters()
        .Include(u => u.RoleTemplate)
        .Where(u => u.IsActive && u.Id != requester.Id);

    var pool = await query
        .Join(_db.Companies.IgnoreQueryFilters(), u => u.CompanyId, c => c.Id, (u, c) => new { u, c.MoleculeId })
        .Where(x => x.MoleculeId == moleculeId)
        .Select(x => x.u)
        .ToListAsync();

    var filtered = pool.Where(u =>
    {
        var key = u.RoleTemplate?.Key;
        if (isAlhutOrText)
        {
            return (key == "Lead" || key == "Director") && u.JobTypeId == requester.JobTypeId;
        }
        else
        {
            return key == "BRDirector" || key == "MoleculeAdmin";
        }
    }).ToList();

    // Empty-pool fallback: MoleculeAdmin in molecule
    if (filtered.Count == 0)
    {
        filtered = pool.Where(u => u.RoleTemplate?.Key == "MoleculeAdmin").ToList();
    }

    return filtered;
}
```

- [ ] **Step 5: Run tests to verify pass**

```powershell
dotnet test --filter "FullyQualifiedName~ApproverPoolTests"
```

Expected: 3/3 PASS.

- [ ] **Step 6: Commit**

```powershell
git add Services/IVacationApprovalService.cs Services/VacationApprovalService.cs ShiftManager.Tests/UnitTests/Services/ApproverPoolTests.cs
git commit -m "feat: implement per-jobtype-vertical approver pool with empty-pool fallback"
```

---

### Task 19: Implement dual-approval state machine in `ApproveAsync`

**Files:**
- Modify: `Services/VacationApprovalService.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/DualApprovalTests.cs` (create)

- [ ] **Step 1: Write the dual-approval flow test**

```csharp
public class DualApprovalTests
{
    [Fact]
    public async Task Vacation_LongerThanThreshold_RequiresDualApproval()
    {
        using var ctx = TestDb.Create();
        var (requester, molecule, company) = TestDb.SeedBasicTenant(ctx);
        TestDb.SeedMoleculeApprovalSettings(ctx, molecule.Id, threshold: 7);
        var alhutLead = TestDb.SeedUser(ctx, company.Id, requester.JobTypeId, "Lead");
        var alhutDirector = TestDb.SeedUser(ctx, company.Id, requester.JobTypeId, "Director");

        var req = TestDb.MakePendingVacation(ctx, requester, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 12)); // 12 days > 7
        var svc = TestDb.MakeApprovalService(ctx);

        // Lead approves first
        var (s1, _) = await svc.ApproveAsync(req.Id, alhutLead.Id, null);
        Assert.True(s1);
        var afterLead = await ctx.TimeOffRequests.FindAsync(req.Id);
        Assert.Equal(RequestStatus.PendingSecondApproval, afterLead!.Status);

        // Director approves second
        var (s2, _) = await svc.ApproveAsync(req.Id, alhutDirector.Id, null);
        Assert.True(s2);
        var afterDir = await ctx.TimeOffRequests.FindAsync(req.Id);
        Assert.Equal(RequestStatus.Approved, afterDir!.Status);
        Assert.NotNull(afterDir.FirstApprovalActorId);
        Assert.NotNull(afterDir.SecondApprovalActorId);
    }

    [Fact]
    public async Task Vacation_AtOrBelowThreshold_SinglApproval()
    {
        using var ctx = TestDb.Create();
        var (requester, molecule, company) = TestDb.SeedBasicTenant(ctx);
        TestDb.SeedMoleculeApprovalSettings(ctx, molecule.Id, threshold: 7);
        var alhutLead = TestDb.SeedUser(ctx, company.Id, requester.JobTypeId, "Lead");

        var req = TestDb.MakePendingVacation(ctx, requester, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5)); // 5 days
        var svc = TestDb.MakeApprovalService(ctx);
        var (success, _) = await svc.ApproveAsync(req.Id, alhutLead.Id, null);
        Assert.True(success);
        var after = await ctx.TimeOffRequests.FindAsync(req.Id);
        Assert.Equal(RequestStatus.Approved, after!.Status);
    }

    [Fact]
    public async Task DualApproval_DeclineByEitherTier_FailsRequest()
    {
        using var ctx = TestDb.Create();
        var (requester, molecule, company) = TestDb.SeedBasicTenant(ctx);
        TestDb.SeedMoleculeApprovalSettings(ctx, molecule.Id, threshold: 7);
        var alhutLead = TestDb.SeedUser(ctx, company.Id, requester.JobTypeId, "Lead");
        var alhutDirector = TestDb.SeedUser(ctx, company.Id, requester.JobTypeId, "Director");

        var req = TestDb.MakePendingVacation(ctx, requester, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 12));
        var svc = TestDb.MakeApprovalService(ctx);

        await svc.ApproveAsync(req.Id, alhutLead.Id, null);  // first tier approves → PendingSecondApproval
        await svc.DeclineAsync(req.Id, alhutDirector.Id, "Workload high");

        var after = await ctx.TimeOffRequests.FindAsync(req.Id);
        Assert.Equal(RequestStatus.Declined, after!.Status);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test --filter "FullyQualifiedName~DualApprovalTests"
```

Expected: FAIL.

- [ ] **Step 3: Update `ApproveAsync` to handle dual-approval state machine**

The full method body is significant; see the spec's §6.4 state machine. Key logic:

```csharp
public async Task<(bool Success, string Message)> ApproveAsync(int requestId, int approverId, string? notes)
{
    using var trx = await _db.Database.BeginTransactionAsync();
    var request = await _db.TimeOffRequests.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == requestId);
    if (request == null) return (false, _localizer["VacationApproval_RequestNotFound"]);
    if (request.UserId == approverId) return (false, _localizer["VacationApproval_CannotApproveSelf"]);

    // Determine if dual approval is required
    var moleculeId = await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.MoleculeId).FirstOrDefaultAsync();
    var settings = await _db.MoleculeApprovalSettings
        .FirstOrDefaultAsync(s => s.MoleculeId == moleculeId);
    var threshold = settings?.DualApprovalDayThreshold ?? 7;
    var lengthDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
    bool requiresDual = request.Type == TimeOffType.Vacation && lengthDays > threshold;

    // Determine which tier this approver belongs to
    var approver = await _db.Users.IgnoreQueryFilters().Include(u => u.RoleTemplate)
        .FirstOrDefaultAsync(u => u.Id == approverId);
    if (approver == null) return (false, _localizer["VacationApproval_ApproverNotFound"]);

    bool approverIsFirstTier;  // Lead OR BRDirector
    bool approverIsSecondTier; // Director OR MoleculeAdmin
    var requester = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.UserId);
    bool requesterIsAlhutText = requester?.JobTypeId is 1 or 3;

    if (requesterIsAlhutText)
    {
        approverIsFirstTier  = approver.RoleTemplate?.Key == "Lead" && approver.JobTypeId == requester!.JobTypeId;
        approverIsSecondTier = approver.RoleTemplate?.Key == "Director" && approver.JobTypeId == requester!.JobTypeId;
    }
    else
    {
        approverIsFirstTier  = approver.RoleTemplate?.Key == "BRDirector";
        approverIsSecondTier = approver.RoleTemplate?.Key == "MoleculeAdmin";
    }

    // Validate approver in pool (re-check to prevent replay attacks via direct API)
    var pool = await GetApproverPoolAsync(requestId);
    if (!pool.Any(u => u.Id == approverId))
        return (false, _localizer["VacationApproval_NotInPool"]);

    if (request.Status == RequestStatus.Pending)
    {
        if (!requiresDual)
        {
            // Single-approval path
            request.Status = RequestStatus.Approved;
            request.FirstApprovalActorId = approverId;
            request.FirstApprovalActedAt = DateTime.UtcNow;
        }
        else
        {
            // First tier of dual approval
            if (approverIsFirstTier)
            {
                request.FirstApprovalActorId = approverId;
                request.FirstApprovalActedAt = DateTime.UtcNow;
                request.Status = RequestStatus.PendingSecondApproval;
            }
            else if (approverIsSecondTier)
            {
                request.SecondApprovalActorId = approverId;
                request.SecondApprovalActedAt = DateTime.UtcNow;
                request.Status = RequestStatus.PendingSecondApproval;
            }
            else
            {
                return (false, _localizer["VacationApproval_NotEligibleTier"]);
            }
        }
    }
    else if (request.Status == RequestStatus.PendingSecondApproval)
    {
        // Complementary tier needed
        if (approverIsFirstTier && request.FirstApprovalActorId == null)
        {
            request.FirstApprovalActorId = approverId;
            request.FirstApprovalActedAt = DateTime.UtcNow;
            request.Status = RequestStatus.Approved;
        }
        else if (approverIsSecondTier && request.SecondApprovalActorId == null)
        {
            request.SecondApprovalActorId = approverId;
            request.SecondApprovalActedAt = DateTime.UtcNow;
            request.Status = RequestStatus.Approved;
        }
        else
        {
            return (false, _localizer["VacationApproval_TierAlreadyApproved"]);
        }
    }
    else
    {
        return (false, _localizer["VacationApproval_AlreadyProcessed"]);
    }

    await _db.SaveChangesAsync();
    await trx.CommitAsync();

    if (request.Status == RequestStatus.Approved)
    {
        await _materialiser.SyncMaterialisedHomeRowsAsync(request.Id);
    }

    return (true, _localizer["VacationApproval_Approved"]);
}
```

- [ ] **Step 4: Run tests to verify pass**

```powershell
dotnet test --filter "FullyQualifiedName~DualApprovalTests"
```

Expected: 3/3 PASS.

- [ ] **Step 5: Commit**

```powershell
git add Services/VacationApprovalService.cs ShiftManager.Tests/UnitTests/Services/DualApprovalTests.cs
git commit -m "feat: dual-approval state machine with PendingSecondApproval status"
```

---

### Task 20: `MoleculeApprovalSettings` admin page

**Files:**
- Create: `Pages/Admin/Molecule/ApprovalSettings.cshtml`
- Create: `Pages/Admin/Molecule/ApprovalSettings.cshtml.cs`

- [ ] **Step 1: Create page model**

```csharp
namespace ShiftManager.Pages.Admin.Molecule;

[Authorize]
public class ApprovalSettingsModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;

    public ApprovalSettingsModel(IStringLocalizer<SharedResources> loc, AppDbContext db, IGrantService grant) : base(loc)
    {
        _db = db; _grantService = grant;
    }

    [BindProperty(SupportsGet = true)] public int MoleculeId { get; set; }
    [BindProperty] public int DualApprovalDayThreshold { get; set; }

    public List<SelectListItem> AvailableMolecules { get; set; } = new();
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanEditMoleculeAsync(MoleculeId)) return Forbid();
        AvailableMolecules = await GetEligibleMoleculesAsync();
        var settings = await _db.MoleculeApprovalSettings.FirstOrDefaultAsync(s => s.MoleculeId == MoleculeId);
        DualApprovalDayThreshold = settings?.DualApprovalDayThreshold ?? 7;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CanEditMoleculeAsync(MoleculeId)) return Forbid();
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var settings = await _db.MoleculeApprovalSettings.FirstOrDefaultAsync(s => s.MoleculeId == MoleculeId)
                       ?? new MoleculeApprovalSettings { MoleculeId = MoleculeId };
        settings.DualApprovalDayThreshold = Math.Max(1, DualApprovalDayThreshold);
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = userId;
        if (settings.Id == 0) _db.MoleculeApprovalSettings.Add(settings);
        await _db.SaveChangesAsync();
        StatusMessage = _localizer["Saved"];
        return RedirectToPage(new { MoleculeId });
    }

    private async Task<bool> CanEditMoleculeAsync(int moleculeId)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.IgnoreQueryFilters().Include(u => u.RoleTemplate)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;

        var userMoleculeId = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == user.CompanyId).Select(c => c.MoleculeId).FirstOrDefaultAsync();
        if (userMoleculeId != moleculeId) return false;

        var key = user.RoleTemplate?.Key;
        return key == "MoleculeAdmin" || key == "Director";
    }

    private async Task<List<SelectListItem>> GetEligibleMoleculesAsync()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return new();
        var moleculeId = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == user.CompanyId).Select(c => c.MoleculeId).FirstOrDefaultAsync();
        var molecule = await _db.Molecules.FirstOrDefaultAsync(m => m.Id == moleculeId);
        return molecule == null
            ? new List<SelectListItem>()
            : new List<SelectListItem> { new(molecule.Name, molecule.Id.ToString()) };
    }
}
```

- [ ] **Step 2: Create the Razor view**

```html
@page "{MoleculeId:int}"
@model ShiftManager.Pages.Admin.Molecule.ApprovalSettingsModel
@{
    Layout = "_Layout";
    ViewData["Title"] = Localizer["MoleculeApprovalSettings_Title"].Value;
}
<h2><loc key="MoleculeApprovalSettings_Title" /></h2>

@if (!string.IsNullOrEmpty(Model.StatusMessage))
{
    <div class="alert alert-success">@Model.StatusMessage</div>
}

<form method="post">
    <input type="hidden" asp-for="MoleculeId" />
    <div class="form-group">
        <label asp-for="DualApprovalDayThreshold"><loc key="DualApprovalDayThreshold" /></label>
        <input asp-for="DualApprovalDayThreshold" type="number" min="1" max="365" class="form-control" />
        <small><loc key="DualApprovalDayThreshold_Help" /></small>
    </div>
    <button type="submit" class="btn btn-primary"><loc key="Save" /></button>
</form>
```

- [ ] **Step 3: Add localization keys**

To `Resources/SharedResources.resx` (and `.he-IL.resx`):
- `MoleculeApprovalSettings_Title`: "Molecule Approval Settings" / "הגדרות אישור מולקולה"
- `DualApprovalDayThreshold`: "Dual approval threshold (days)" / "סף אישור כפול (ימים)"
- `DualApprovalDayThreshold_Help`: "Vacations longer than this number of days require approval from both Lead/BRDirector and Director/MoleculeAdmin." / Hebrew equivalent
- `Saved`: "Saved" / "נשמר"

- [ ] **Step 4: Build and smoke-test in browser**

```powershell
dotnet build --no-restore
dotnet run --urls http://localhost:5000
# Navigate to /Admin/Molecule/ApprovalSettings/1 as test.owner@shifty.test
# Change threshold, click Save, verify alert appears and value persists across reload
```

- [ ] **Step 5: Commit**

```powershell
git add Pages/Admin/Molecule/ Resources/
git commit -m "feat: add MoleculeApprovalSettings admin page"
```

---

### Task 21: Add `Private` checkbox to `/My/Requests`

**Files:**
- Modify: `Pages/My/Requests.cshtml`
- Modify: `Pages/My/Requests.cshtml.cs`

- [ ] **Step 1: Bind `Private` to the form model**

In `Requests.cshtml.cs`, ensure `TimeOffRequest.Private` is part of the `[BindProperty]` model. If `TimeOffRequest` is bound directly, the new property is bound automatically.

- [ ] **Step 2: Add checkbox to the Razor markup**

In `Requests.cshtml`, in the time-off submission form (after the approver dropdown):

```html
<div class="form-group">
    <label>
        <input asp-for="TimeOffRequest.Private" type="checkbox" />
        <loc key="TimeOffRequest_Private" />
    </label>
    <small><loc key="TimeOffRequest_Private_Help" /></small>
</div>
```

- [ ] **Step 3: Add localization keys**

- `TimeOffRequest_Private`: "Private (only chosen approver sees this request)" / "פרטי (רק הממונה הנבחר יראה את הבקשה)"
- `TimeOffRequest_Private_Help`: "Other eligible approvers in your pool will not see this request — only the person you select." / Hebrew equivalent

- [ ] **Step 4: Update queue rendering to respect Private**

In `Pages/Requests/Index.cshtml.cs`, in the `LoadPendingTimeOffAsync` (or equivalent) method, add filter:

```csharp
.Where(r => !r.Private || r.ApproverId == currentUserId)
```

- [ ] **Step 5: Smoke-test**

```powershell
dotnet run --urls http://localhost:5000
# Submit a time-off request with Private checked, choosing Test Manager
# Log in as Test Owner — verify the request does NOT appear in their queue
# Log in as Test Manager — verify it does appear
```

- [ ] **Step 6: Commit**

```powershell
git add Pages/My/Requests.cshtml Pages/My/Requests.cshtml.cs Pages/Requests/Index.cshtml.cs Resources/
git commit -m "feat: add Private checkbox to time-off request and respect it in queue rendering"
```

---

## Phase 5 — Calendar rendering

### Task 22: Update HOME chip composition with source icons

**Files:**
- Modify: `wwwroot/js/calendar-render.js` (or wherever chip HTML is generated; identify with grep below)
- Modify: `wwwroot/css/calendar.css`

- [ ] **Step 1: Identify the chip-rendering function**

```powershell
git grep -nE "shift-badge--home|excel-calendar__assignment.*home|class.*chip-home" -- 'wwwroot/**'
```

Identify the JS file that builds the assignment chip HTML.

- [ ] **Step 2: Add source-icon mapping in JS**

Add this helper near the top of the chip-rendering JS:

```javascript
function getSourceIcon(assignment) {
    if (!assignment.shiftType || !assignment.shiftType.isHome) return null;
    if (assignment.sourceTimeOffRequestType === 'Vacation' || assignment.sourceTimeOffRequestType === 0) return 'plane';
    if (assignment.sourceTimeOffRequestType === 'After'    || assignment.sourceTimeOffRequestType === 1) return 'sunrise';
    return 'repeat';  // rotation
}

function buildHomeChip(assignment) {
    const sourceIcon = getSourceIcon(assignment);
    const homeIcon = 'house';
    const label = assignment.shiftType.nameLocalized;       // "Home" or "After"
    const time = formatTimeRange(assignment.shiftType.start, assignment.shiftType.end);

    return `
        <div class="excel-calendar__assignment chip-home" data-assignment-id="${assignment.id}" data-source-request-id="${assignment.sourceTimeOffRequestId || ''}">
            <span class="chip-icon">${Icons.render(sourceIcon, { size: 12 })}</span>
            <span class="chip-icon">${Icons.render(homeIcon, { size: 12 })}</span>
            <span class="chip-label">${label}</span>
            <span class="chip-time">${time}</span>
            <button class="excel-calendar__remove-btn" data-assignment-id="${assignment.id}">×</button>
        </div>
    `;
}
```

- [ ] **Step 3: Update the API endpoint serialising assignments to include source info**

In `Pages/Calendar/Shifts.cshtml.cs` (and `Calendar/GetShiftsData` API), include in the assignment DTO:

```csharp
new {
    /* existing fields */
    sourceTimeOffRequestId = a.SourceTimeOffRequestId,
    sourceTimeOffRequestType = a.SourceTimeOffRequestId.HasValue
        ? (await _db.TimeOffRequests.Where(t => t.Id == a.SourceTimeOffRequestId).Select(t => (TimeOffType?)t.Type).FirstOrDefaultAsync())
        : null
}
```

(Optimise N+1 with a join in the actual implementation.)

- [ ] **Step 4: Update CSS for chip-home spacing**

In `wwwroot/css/calendar.css`:

```css
.chip-home {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    background: var(--shift-home-soft);
    color: var(--shift-home-text);
    border: 1px solid var(--shift-home-border);
    padding: 0.25rem 0.5rem;
    border-radius: 4px;
}
.chip-home .chip-icon { display: inline-flex; }
.chip-home .chip-label { font-weight: 500; }
.chip-home .chip-time { opacity: 0.7; font-size: 0.7rem; }
```

- [ ] **Step 5: Smoke test in browser**

Approve a vacation/after, check Calendar/Shifts visually for source icon presence.

- [ ] **Step 6: Commit**

```powershell
git add wwwroot/js/ wwwroot/css/calendar.css Pages/Calendar/Shifts.cshtml.cs
git commit -m "feat: HOME chips include Lucide source icons (plane/sunrise/repeat) + house"
```

---

### Task 23: Calendar/Overview HOME chip styling

**Files:**
- Modify: `Pages/Calendar/Overview.cshtml.cs:280-340`
- Modify: `Pages/Calendar/Overview.cshtml`

- [ ] **Step 1: Update `LoadShiftsAsync` to include HOME-source info**

Replace the simple shift-name string with a richer DTO:

```csharp
public record OverviewCellEntry(string Label, bool IsHome, string? SourceIcon, string TimeRange);

private async Task<Dictionary<(int UserId, DateOnly Date), List<OverviewCellEntry>>> LoadShiftsAsync()
{
    /* … existing query … */

    var result = new Dictionary<(int UserId, DateOnly Date), List<OverviewCellEntry>>();
    foreach (var assignment in assignments)
    {
        var date = assignment.ShiftInstance.WorkDate;
        var shiftType = assignment.ShiftInstance.ShiftType;
        var shiftName = await _companyLocalizationService.ResolveShiftTypeNameAsync(shiftType, companyId, culture);
        var entry = new OverviewCellEntry(
            Label: shiftName,
            IsHome: shiftType.IsHome,
            SourceIcon: assignment.SourceTimeOffRequestId.HasValue
                ? (await _db.TimeOffRequests.Where(t => t.Id == assignment.SourceTimeOffRequestId).Select(t => t.Type).FirstAsync()) switch
                  { TimeOffType.Vacation => "plane", TimeOffType.After => "sunrise", _ => "repeat" }
                : (shiftType.IsHome ? "repeat" : null),
            TimeRange: $"{shiftType.Start:HH:mm}-{shiftType.End:HH:mm}");

        if (assignment.UserId.HasValue && userIds.Contains(assignment.UserId.Value))
        {
            var key = (assignment.UserId.Value, date);
            if (!result.ContainsKey(key)) result[key] = new();
            result[key].Add(entry);
        }
    }
    return result;
}
```

- [ ] **Step 2: Update Overview.cshtml to render chips properly for HOME entries**

Where shift names are rendered in the cell:

```html
@foreach (var entry in entries)
{
    if (entry.IsHome)
    {
        <span class="chip-home">
            @if (entry.SourceIcon != null) { <i data-lucide="@entry.SourceIcon"></i> }
            <i data-lucide="house"></i>
            <span>@entry.Label</span>
            <span class="chip-time">@entry.TimeRange</span>
        </span>
    }
    else
    {
        <span>@entry.Label</span>
    }
}
```

- [ ] **Step 3: Smoke test**

```powershell
dotnet run --urls http://localhost:5000
# Navigate to /Calendar/Overview
# Verify HOME entries render with the chip styling, not as plain text
```

- [ ] **Step 4: Commit**

```powershell
git add Pages/Calendar/Overview.cshtml Pages/Calendar/Overview.cshtml.cs
git commit -m "feat: Calendar/Overview renders HOME with chip styling and source icons"
```

---

### Task 24: HOME read-only overlay on Calendar/Chores, Calendar/OnCall, MyTeam

**Files:**
- Modify: `Pages/Calendar/Chores.cshtml.cs`
- Modify: `Pages/Calendar/OnCall.cshtml.cs`
- Modify: `wwwroot/js/myteam.js`

- [ ] **Step 1: Chores — add HOME overlay query**

In `Pages/Calendar/Chores.cshtml.cs`, alongside the chore-loading query, add:

```csharp
public async Task<Dictionary<(int UserId, DateOnly Date), List<HomeOverlay>>> LoadHomeOverlaysAsync()
{
    var assignments = await _db.ShiftAssignments
        .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
        .Where(sa => sa.ShiftInstance.WorkDate >= StartDate && sa.ShiftInstance.WorkDate <= EndDate
                  && (sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
                   || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
                   || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM)
                  && sa.UserId.HasValue && Users.Select(u => u.Id).Contains(sa.UserId.Value))
        .ToListAsync();

    return assignments
        .GroupBy(sa => (sa.UserId!.Value, sa.ShiftInstance.WorkDate))
        .ToDictionary(g => g.Key, g => g.Select(sa => new HomeOverlay {
            Label = sa.ShiftInstance.ShiftType.NameEn,
            TimeRange = $"{sa.ShiftInstance.ShiftType.Start:HH:mm}-{sa.ShiftInstance.ShiftType.End:HH:mm}"
        }).ToList());
}

public record HomeOverlay { public string Label { get; init; } = ""; public string TimeRange { get; init; } = ""; }
```

In the cell rendering (Razor), prepend the overlay:

```html
@if (HomeOverlays.TryGetValue((user.Id, date), out var homes))
{
    foreach (var h in homes)
    {
        <span class="chip-home chip-home--overlay" title="At home">
            <i data-lucide="house"></i> @h.Label <span class="chip-time">@h.TimeRange</span>
        </span>
    }
}
```

The `--overlay` modifier dims the chip (it's read-only context info on a chores calendar):

```css
.chip-home--overlay { opacity: 0.65; pointer-events: none; }
```

- [ ] **Step 2: Repeat for OnCall**

Same logic applied in `Pages/Calendar/OnCall.cshtml.cs` and view.

- [ ] **Step 3: Repeat for MyTeam JS**

In `wwwroot/js/myteam.js`, the cell-render loop already accepts an array of "shifts" per user/date. Add HOME entries to that array for those days, with the chip-home styling.

- [ ] **Step 4: Smoke test all four surfaces**

```powershell
dotnet run --urls http://localhost:5000
# Approve a vacation
# Visit /Calendar/Chores, /Calendar/OnCall, /MyTeam
# Verify HOME overlay appears in each row for the vacation user, with home color and house icon
```

- [ ] **Step 5: Commit**

```powershell
git add Pages/Calendar/Chores.cshtml.cs Pages/Calendar/OnCall.cshtml.cs Pages/Calendar/*.cshtml wwwroot/js/myteam.js wwwroot/css/calendar.css
git commit -m "feat: HOME read-only overlay on Chores, OnCall, MyTeam calendars"
```

---

## Phase 6 — HomeType rule-first UI

### Task 25: Replace paint-dates UI with rule editor + read-only preview

**Files:**
- Modify: `Pages/Admin/HomeTypes/Index.cshtml`
- Modify: `Pages/Admin/HomeTypes/Index.cshtml.cs`
- Modify: `wwwroot/js/home-type-calendar.js` (transform from painter to read-only preview)

- [ ] **Step 1: Add rule-edit form fields to the page model**

In `Index.cshtml.cs`:

```csharp
[BindProperty] public int RuleCycleWeeks { get; set; } = 4;
[BindProperty] public List<DayOfWeek> RuleHomeDays { get; set; } = new();
[BindProperty] public List<int> RuleWeekOffsets { get; set; } = new() { 0 };
[BindProperty] public DateOnly RuleAnchor { get; set; } = NextMonday();
[BindProperty] public DateOnly? RuleActiveStart { get; set; }
[BindProperty] public DateOnly? RuleActiveEnd { get; set; }

private static DateOnly NextMonday()
{
    var t = DateOnly.FromDateTime(DateTime.Today);
    var daysUntilMonday = (8 - (int)t.DayOfWeek) % 7;
    return t.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday);
}
```

- [ ] **Step 2: Update `OnPostCreate` and `OnPostEdit` to write rule JSON directly**

```csharp
public async Task<IActionResult> OnPostEditAsync()
{
    var ht = await _homeTypeService.GetHomeTypeAsync(EditingId);
    if (ht == null) return NotFound();
    var rule = new DerivedRotationRule(
        CycleWeeks: RuleCycleWeeks,
        HomeDays: RuleHomeDays,
        WeekOffsets: RuleWeekOffsets,
        Anchor: RuleAnchor,
        StartTime: null, EndTime: null);
    ht.DerivedRule = JsonSerializer.Serialize(rule);
    ht.UpdatedAt = DateTime.UtcNow;
    await _homeTypeService.UpdateHomeTypeAsync(ht);
    return RedirectToPage(new { MoleculeId = ht.MoleculeId });
}
```

- [ ] **Step 3: Replace painter form HTML with rule form**

In `Index.cshtml`:

```html
<div class="rule-editor">
    <label><loc key="HomeType_CycleWeeks" /></label>
    <input asp-for="RuleCycleWeeks" type="number" min="1" max="12" />

    <label><loc key="HomeType_HomeDays" /></label>
    @foreach (var d in Enum.GetValues<DayOfWeek>())
    {
        <label><input type="checkbox" name="RuleHomeDays" value="@((int)d)" @(Model.RuleHomeDays.Contains(d) ? "checked" : "") /> @d</label>
    }

    <label><loc key="HomeType_WeekOffsets" /></label>
    @* a small grid of checkboxes for week 0..CycleWeeks-1 *@

    <label><loc key="HomeType_Anchor" /></label>
    <input asp-for="RuleAnchor" type="date" />
    <small><loc key="HomeType_Anchor_Help" /></small>
</div>

<div class="rule-preview">
    <h4><loc key="HomeType_Preview_Title" /></h4>
    <div id="rule-preview-calendar"></div>
</div>
```

- [ ] **Step 4: Update `home-type-calendar.js` to be a read-only preview**

Replace painter event handlers with a render function that reads the form values and shows the next 60 days, highlighting dates the rule would produce. No click-to-paint.

```javascript
(function() {
    'use strict';
    function readRuleFromForm() {
        return {
            cycleWeeks: parseInt(document.querySelector('[name=RuleCycleWeeks]').value, 10),
            homeDays: Array.from(document.querySelectorAll('input[name=RuleHomeDays]:checked')).map(c => parseInt(c.value, 10)),
            weekOffsets: Array.from(document.querySelectorAll('input[name=RuleWeekOffsets]:checked')).map(c => parseInt(c.value, 10)),
            anchor: document.querySelector('[name=RuleAnchor]').value
        };
    }

    function previewDates(rule, daysAhead) {
        var anchor = new Date(rule.anchor);
        var today = new Date();
        var dates = [];
        for (var i = 0; i < daysAhead; i++) {
            var d = new Date(today); d.setDate(today.getDate() + i);
            if (!rule.homeDays.includes(d.getDay())) continue;
            var weekNum = Math.floor((d - anchor) / (1000*60*60*24*7));
            if (weekNum < 0) continue;
            var cycleWeek = weekNum % rule.cycleWeeks;
            if (rule.weekOffsets.includes(cycleWeek)) dates.push(d);
        }
        return dates;
    }

    function render() {
        var rule = readRuleFromForm();
        var dates = previewDates(rule, 60);
        var container = document.getElementById('rule-preview-calendar');
        container.innerHTML = dates.map(d => '<span class="preview-date">' + d.toISOString().slice(0,10) + '</span>').join('');
    }

    document.addEventListener('change', function(e) {
        if (e.target.matches('[name^=Rule]')) render();
    });
    document.addEventListener('DOMContentLoaded', render);
})();
```

- [ ] **Step 5: Remove paint-the-dates UI**

Delete the existing painter form, drag-handlers, and submission of `PatternJson` from the form (the column stays in DB for legacy backwards compatibility but is no longer written by new submissions).

- [ ] **Step 6: Smoke test**

```powershell
dotnet run --urls http://localhost:5000
# Visit /Admin/HomeTypes; create a new HomeType
# Set cycle 2, days Sun, anchor next Monday
# Verify preview shows alternating Sundays
# Save, then verify the saved DerivedRule JSON contains the right fields
```

- [ ] **Step 7: Commit**

```powershell
git add Pages/Admin/HomeTypes/ wwwroot/js/home-type-calendar.js
git commit -m "feat: HomeType rule-first editor with read-only preview"
```

---

### Task 26: Persistent regenerate banner

**Files:**
- Modify: `Pages/Admin/HomeTypes/Index.cshtml.cs`
- Modify: `Pages/Admin/HomeTypes/Index.cshtml`

- [ ] **Step 1: Compute banner condition in page model**

```csharp
public class HomeTypeBannerInfo
{
    public int HomeTypeId { get; set; }
    public string Name { get; set; } = "";
    public bool NeedsRegen { get; set; }
    public DateTime? LastGeneratedAt { get; set; }
    public int UserCount { get; set; }
}

public List<HomeTypeBannerInfo> Banners { get; set; } = new();

// In OnGetAsync:
Banners = await _db.HomeTypes
    .Where(h => h.MoleculeId == MoleculeId)
    .Select(h => new HomeTypeBannerInfo
    {
        HomeTypeId = h.Id,
        Name = h.Name,
        NeedsRegen = h.LastGeneratedAt == null || h.UpdatedAt > h.LastGeneratedAt,
        LastGeneratedAt = h.LastGeneratedAt,
        UserCount = _db.Users.Count(u => u.HomeTypeId == h.Id)
    }).ToListAsync();
```

- [ ] **Step 2: Render banners**

```html
@foreach (var b in Model.Banners.Where(b => b.NeedsRegen))
{
    <div class="banner banner--warn">
        ⚠ <loc key="HomeType_BannerNeedsRegen" args="@(new[] { b.Name, b.UserCount.ToString(), b.LastGeneratedAt?.ToString("yyyy-MM-dd") ?? "(never)" })" />
        <form method="post" asp-page-handler="Generate" style="display:inline">
            <input type="hidden" name="GenerateHomeTypeId" value="@b.HomeTypeId" />
            <button class="btn btn-primary"><loc key="Regenerate" /></button>
        </form>
    </div>
}
```

- [ ] **Step 3: Update `OnPostGenerateAsync` to set `LastGeneratedAt`**

```csharp
// after a successful Generate:
var ht = await _db.HomeTypes.FirstAsync(h => h.Id == GenerateHomeTypeId);
ht.LastGeneratedAt = DateTime.UtcNow;
await _db.SaveChangesAsync();
```

- [ ] **Step 4: Smoke test**

```powershell
# Edit a HomeType rule, return to list — verify banner appears
# Click Regenerate — verify banner disappears
```

- [ ] **Step 5: Commit**

```powershell
git add Pages/Admin/HomeTypes/Index.cshtml Pages/Admin/HomeTypes/Index.cshtml.cs Resources/
git commit -m "feat: persistent regenerate banner driven by UpdatedAt > LastGeneratedAt"
```

---

### Task 27: Per-user HomeTypeOverride UI

**Files:**
- Modify: `Pages/Admin/HomeTypes/Index.cshtml`
- Modify: `Pages/Admin/HomeTypes/Index.cshtml.cs`

- [ ] **Step 1: Add UI section**

In `Index.cshtml` near the user-assignment section:

```html
<details class="user-overrides">
    <summary><loc key="HomeType_UserOverrides_Toggle" /></summary>
    <ul>
        @foreach (var user in Model.AssignedUsers)
        {
            <li>
                @user.DisplayName
                <button onclick="openOverrideEditor(@user.Id, @Model.HomeTypeId)" class="btn btn-sm">
                    <loc key="EditOverridePattern" />
                </button>
            </li>
        }
    </ul>
</details>
<div id="override-editor" style="display:none">
    <h5><loc key="OverrideForUser" /> <span id="override-user-name"></span></h5>
    <textarea id="override-dates" rows="6" placeholder="2026-05-17, 2026-05-24, ..."></textarea>
    <button onclick="saveOverride()" class="btn btn-primary"><loc key="Save" /></button>
    <button onclick="document.getElementById('override-editor').style.display='none'" class="btn btn-link"><loc key="Cancel" /></button>
</div>

<script>
async function openOverrideEditor(userId, homeTypeId) {
    const r = await fetch(`/Admin/HomeTypes?handler=GetOverride&userId=${userId}&homeTypeId=${homeTypeId}`);
    const data = await r.json();
    document.getElementById('override-dates').value = data.dates.join(', ');
    document.getElementById('override-editor').dataset.userId = userId;
    document.getElementById('override-editor').dataset.homeTypeId = homeTypeId;
    document.getElementById('override-editor').style.display = 'block';
}
async function saveOverride() {
    const editor = document.getElementById('override-editor');
    const dates = editor.querySelector('#override-dates').value.split(',').map(s => s.trim()).filter(Boolean);
    await fetch('/Admin/HomeTypes?handler=SaveOverride', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('[name=__RequestVerificationToken]').value },
        body: JSON.stringify({ userId: parseInt(editor.dataset.userId), homeTypeId: parseInt(editor.dataset.homeTypeId), dates })
    });
    location.reload();
}
</script>
```

- [ ] **Step 2: Add page handlers `OnGetGetOverride` and `OnPostSaveOverride`**

```csharp
public async Task<IActionResult> OnGetGetOverrideAsync(int userId, int homeTypeId)
{
    var dates = await _homeTypeService.GetUserOverrideDatesAsync(homeTypeId, userId);
    return new JsonResult(new { dates = dates.Select(d => d.ToString("yyyy-MM-dd")) });
}

public async Task<IActionResult> OnPostSaveOverrideAsync([FromBody] OverrideRequest req)
{
    var dates = req.Dates.Select(s => DateOnly.Parse(s)).ToList();
    var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    await _homeTypeService.SaveUserOverrideAsync(req.HomeTypeId, req.UserId, dates, userId);
    // bump UpdatedAt so banner appears
    var ht = await _db.HomeTypes.FirstAsync(h => h.Id == req.HomeTypeId);
    ht.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();
    return new JsonResult(new { success = true });
}

public record OverrideRequest(int UserId, int HomeTypeId, List<string> Dates);
```

- [ ] **Step 3: Smoke test**

Open `/Admin/HomeTypes`, click EditOverridePattern for a user, paste dates, save. Verify banner triggers.

- [ ] **Step 4: Commit**

```powershell
git add Pages/Admin/HomeTypes/
git commit -m "feat: per-user HomeTypeOverride editor UI"
```

---

## Phase 7 — X-button cancel-or-shorten dialog

### Task 28: Detect source on chip × click and route to dialog

**Files:**
- Modify: `wwwroot/js/calendar-inline-edit.js` (the existing X-button handler)
- Create: `wwwroot/js/cancel-or-shorten-dialog.js`

- [ ] **Step 1: In `calendar-inline-edit.js:executeRemoval`, add the source-detection branch**

After the existing `calendarType === 'shifts'` branch:

```javascript
if (calendarType === 'shifts') {
    var sourceRequestId = btn.closest('.excel-calendar__assignment')?.dataset.sourceRequestId;
    if (sourceRequestId) {
        // Vacation/After-derived HOME — open the cancel/shorten dialog instead of plain delete
        openCancelOrShortenDialog(parseInt(sourceRequestId, 10), assignmentId, btn);
        return;
    }
    // existing rotation HOME / regular shift removal path
    /* … unchanged … */
}
```

- [ ] **Step 2: Create `wwwroot/js/cancel-or-shorten-dialog.js`**

```javascript
(function() {
    'use strict';
    window.openCancelOrShortenDialog = async function(requestId, assignmentId, originBtn) {
        const culture = document.documentElement.lang || 'en';
        const isHebrew = culture.startsWith('he');

        // Fetch request details
        const r = await fetch(`/Api/TimeOffRequest/${requestId}`, { credentials: 'same-origin' });
        const req = await r.json();

        const isVacation = req.type === 'Vacation' || req.type === 0;
        const dialog = document.createElement('div');
        dialog.className = 'cancel-or-shorten-dialog';
        dialog.innerHTML = `
            <h4>${isHebrew ? 'בקשת חופש מאושרת' : 'Approved time-off request'}</h4>
            <p>${isHebrew ? 'יום זה הוא חלק מבקשת חופש מאושרת.' : `This day is part of approved request #${requestId} (${req.type}, ${req.startDate}${isVacation && req.startDate !== req.endDate ? ' – ' + req.endDate : ''}).`}</p>
            <div class="actions">
                <button class="btn btn-danger" data-action="cancel">${isHebrew ? 'בטל את כל הבקשה' : 'Cancel entire request'}</button>
                ${isVacation && req.startDate !== req.endDate ? `<button class="btn btn-secondary" data-action="shorten">${isHebrew ? 'קצר את הטווח' : 'Shorten range'}</button>` : ''}
                <button class="btn btn-link" data-action="close">${isHebrew ? 'סגור' : 'Close'}</button>
            </div>
        `;
        document.body.appendChild(dialog);

        dialog.addEventListener('click', async function(e) {
            const action = e.target.dataset.action;
            if (action === 'close') { dialog.remove(); return; }
            if (action === 'cancel') {
                await fetch('/Api/TimeOffRequest/' + requestId + '/cancel', {
                    method: 'POST', credentials: 'same-origin',
                    headers: { 'RequestVerificationToken': document.querySelector('[name=__RequestVerificationToken]').value }
                });
                triggerCalendarRefresh();
                dialog.remove();
            }
            if (action === 'shorten') {
                openShortenDialog(requestId, dialog);
            }
        });
    };

    function openShortenDialog(requestId, parent) {
        // Replace parent dialog content with date pickers
        parent.innerHTML = `
            <h4>Shorten request #${requestId}</h4>
            <label>New start: <input type="date" id="new-start" /></label>
            <label>New end: <input type="date" id="new-end" /></label>
            <button class="btn btn-primary" id="apply-shorten">Apply</button>
            <button class="btn btn-link" id="cancel-shorten">Cancel</button>
        `;
        parent.querySelector('#apply-shorten').addEventListener('click', async function() {
            await fetch(`/Api/TimeOffRequest/${requestId}/update-dates`, {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('[name=__RequestVerificationToken]').value },
                body: JSON.stringify({
                    newStart: parent.querySelector('#new-start').value,
                    newEnd: parent.querySelector('#new-end').value
                })
            });
            triggerCalendarRefresh();
            parent.remove();
        });
        parent.querySelector('#cancel-shorten').addEventListener('click', function() { parent.remove(); });
    }
})();
```

- [ ] **Step 3: Register the new script in `_Layout.cshtml`**

```html
<script src="~/js/cancel-or-shorten-dialog.js"></script>
```

- [ ] **Step 4: Add the API endpoints `Pages/Api/TimeOffRequest/`**

```csharp
namespace ShiftManager.Pages.Api;

[Authorize]
[IgnoreAntiforgeryToken]
public class TimeOffRequestApiModel : PageModel
{
    private readonly IVacationApprovalService _svc;
    public TimeOffRequestApiModel(IVacationApprovalService svc) { _svc = svc; }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var r = await _svc.GetRequestAsync(Id);  // add this method to interface
        if (r == null) return NotFound();
        return new JsonResult(new {
            id = r.Id, type = r.Type.ToString(), startDate = r.StartDate.ToString("yyyy-MM-dd"),
            endDate = r.EndDate.ToString("yyyy-MM-dd"), status = r.Status.ToString()
        });
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var (ok, msg) = await _svc.CancelRequestAsync(Id, userId);
        return new JsonResult(new { success = ok, message = msg });
    }

    [BindProperty] public UpdateDatesRequest? UpdateDatesBody { get; set; }
    public record UpdateDatesRequest(string NewStart, string NewEnd);

    public async Task<IActionResult> OnPostUpdateDatesAsync([FromBody] UpdateDatesRequest body)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var (ok, msg) = await _svc.UpdateRequestDatesAsync(Id, DateOnly.Parse(body.NewStart), DateOnly.Parse(body.NewEnd), userId);
        return new JsonResult(new { success = ok, message = msg });
    }
}
```

Routes: `/Api/TimeOffRequest/{id}` and `/Api/TimeOffRequest/{id}/cancel`. Add to `Program.cs` AllowAnonymous list ONLY if needed (these are auth-required, so skip). Add to `ApiAuthenticationMiddleware.IsInternalWebUiEndpoint()` whitelist.

- [ ] **Step 5: Smoke test**

```powershell
dotnet run --urls http://localhost:5000
# Approve a vacation; on Calendar/Shifts click × on a vacation HOME chip
# Verify dialog appears with Cancel/Shorten/Close
# Test Cancel → request canceled, chips disappear
# Test Shorten → date picker, dates updated, chips reflect new range
```

- [ ] **Step 6: Commit**

```powershell
git add wwwroot/js/calendar-inline-edit.js wwwroot/js/cancel-or-shorten-dialog.js Pages/Shared/_Layout.cshtml Pages/Api/TimeOffRequest.cshtml.cs Pages/Api/TimeOffRequest.cshtml Middleware/ApiAuthenticationMiddleware.cs Services/IVacationApprovalService.cs Services/VacationApprovalService.cs
git commit -m "feat: cancel-or-shorten dialog on × of vacation/after HOME chips"
```

---

## Phase 8 — SignalR

### Task 29: Inject `IHubContext<CalendarHub>` into HomeTypeService and broadcast on Generate

**Files:**
- Modify: `Services/HomeTypeService.cs`
- Modify: `Program.cs` (DI registration if not already)

- [ ] **Step 1: Inject hub**

In `HomeTypeService` constructor:

```csharp
private readonly IHubContext<CalendarHub>? _hub;

public HomeTypeService(/* existing args */, IHubContext<CalendarHub>? hub = null)
{
    /* … */
    _hub = hub;
}
```

- [ ] **Step 2: Broadcast at end of `GenerateHomeShiftsAsync` after transaction commit**

```csharp
if (_hub != null && newAssignments.Any())
{
    var moleculeId = (await _db.HomeTypes.FirstAsync(h => h.Id == homeTypeId)).MoleculeId;
    var jobTypeIds = await _db.Users.Where(u => userIds.Contains(u.Id)).Select(u => u.JobTypeId).Distinct().ToListAsync();
    foreach (var jobTypeId in jobTypeIds.Where(j => j.HasValue))
    {
        await _hub.Clients.Group($"shifts-{moleculeId}-{jobTypeId}").SendAsync("ShiftsUpdated");
    }
}
```

- [ ] **Step 3: Register IHubContext in DI if not already (likely already there for SignalR)**

In `Program.cs`, verify `builder.Services.AddSignalR();` is present and CalendarHub is mapped.

- [ ] **Step 4: Smoke test**

```powershell
dotnet run --urls http://localhost:5000
# Open two tabs of /Calendar/Shifts
# Click Generate on a HomeType in tab 1
# Verify tab 2 refreshes its chips within ~5 seconds
```

- [ ] **Step 5: Commit**

```powershell
git add Services/HomeTypeService.cs Program.cs
git commit -m "feat: HomeTypeService broadcasts SignalR on successful Generate"
```

---

## Phase 9 — Backfill & cleanup

### Task 30: Backfill materialised rows for existing approved requests

**Files:**
- Modify: `Program.cs` (startup)

- [ ] **Step 1: Add backfill block (after seed migrations)**

```csharp
var deploymentDate = new DateOnly(2026, 5, 5);
var horizon = deploymentDate.AddDays(-7);
var requestsToMaterialise = await db.TimeOffRequests
    .Where(t => t.Status == RequestStatus.Approved && t.EndDate >= horizon)
    .Where(t => !db.ShiftAssignments.Any(sa => sa.SourceTimeOffRequestId == t.Id))
    .Select(t => t.Id)
    .ToListAsync();

var materialiser = scope.ServiceProvider.GetRequiredService<IHomeMaterialiserService>();
foreach (var rid in requestsToMaterialise)
{
    await materialiser.SyncMaterialisedHomeRowsAsync(rid);
}
```

- [ ] **Step 2: Smoke test**

```powershell
dotnet run --urls http://localhost:5000
# verify in Calendar/Shifts that previously-approved requests now show HOME chips
```

- [ ] **Step 3: Commit**

```powershell
git add Program.cs
git commit -m "feat: startup backfill of materialised HOME rows for existing approved requests"
```

---

### Task 31: Remove `/Requests/TimeOff/Create` page

**Files:**
- Delete: `Pages/Requests/TimeOff/Create.cshtml`
- Delete: `Pages/Requests/TimeOff/Create.cshtml.cs`
- Modify: any sidebar links / `Program.cs` AllowAnonymous lists referencing it

- [ ] **Step 1: Audit for any references**

```powershell
git grep -nE "/Requests/TimeOff/Create" -- '*.cs' '*.cshtml' '*.js' '*.json'
```

- [ ] **Step 2: Replace any references with `/My/Requests`**

For sidebar nav links, breadcrumbs, redirects, etc.

- [ ] **Step 3: Delete the files**

```powershell
git rm Pages/Requests/TimeOff/Create.cshtml Pages/Requests/TimeOff/Create.cshtml.cs
```

- [ ] **Step 4: Build**

```powershell
dotnet build --no-restore
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Smoke test**

```powershell
# Visit /Requests/TimeOff/Create — expect 404
# Verify all sidebar links to time-off go to /My/Requests
```

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "refactor: remove deprecated /Requests/TimeOff/Create — use /My/Requests"
```

---

## Phase 10 — Feature flag & verification

### Task 32: Add feature flag `FF_HOME_UNIFICATION`

**Files:**
- Modify: `Data/SeedData/FeatureFlagSeed.cs`

- [ ] **Step 1: Add flag definition**

```csharp
F(Flags.HomeUnification, "Enables the unified HOME materialisation, dual-approval routing, and rule-first HomeType. Off = legacy behaviour.", now);
// in Flags class:
public const string HomeUnification = "FF_HOME_UNIFICATION";
```

- [ ] **Step 2: Gate the materialiser invocations behind the flag**

In `VacationApprovalService.ApproveAsync`, `DeclineAsync`, `CancelRequestAsync`, `UpdateRequestDatesAsync`:

```csharp
if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
{
    await _materialiser.SyncMaterialisedHomeRowsAsync(request.Id);
}
```

- [ ] **Step 3: Set flag to ON in development DB**

Via Owner > Feature Flags page, toggle FF_HOME_UNIFICATION on.

- [ ] **Step 4: Verify — full flow works**

```powershell
dotnet run --urls http://localhost:5000
# Submit After, approve, verify chips appear; cancel, verify chips disappear
# Submit 14-day Vacation, verify dual-approval fires
# Verify Calendar/Overview, Chores, OnCall all show HOME overlays
```

- [ ] **Step 5: Commit**

```powershell
git add Data/SeedData/FeatureFlagSeed.cs Services/VacationApprovalService.cs
git commit -m "feat: gate HOME unification behind FF_HOME_UNIFICATION feature flag"
```

---

### Task 33: Browser smoke checklist

**Files:** none (verification step)

- [ ] **Step 1: After day-N+1 morning conflict regression test**

- Submit After for a date X
- Approve (single-approver flow)
- Calendar/Shifts: try to assign a 08:00 morning shift on day X+1
- Expected: HOME_CONFLICT warning fires, override token offered. (The 2026-05-04 verified bug is structurally fixed.)

- [ ] **Step 2: Calendar/Overview visual regression**

- Approve a 5-day vacation
- Visit /Calendar/Overview
- Expected: HOME chips visible with home color, plane icon, "Home" / "בית" label, time range

- [ ] **Step 3: × on vacation chip opens dialog**

- Click × twice on a vacation HOME chip
- Expected: cancel-or-shorten dialog appears

- [ ] **Step 4: SignalR cross-tab refresh**

- Open Calendar/Shifts in two tabs
- Approve a request in tab 1
- Expected: tab 2 refreshes within 5 seconds without manual reload

- [ ] **Step 5: Dual-approval flow**

- Submit a 14-day vacation as test.member
- Log in as Lead — approve. Verify status = PendingSecondApproval
- Log in as Director — approve. Verify status = Approved, materialised chips appear

- [ ] **Step 6: Commit final verification record**

```powershell
echo "Browser smoke verified $(date)" > docs/superpowers/plans/2026-05-05-home-unification-smoke.md
git add docs/superpowers/plans/2026-05-05-home-unification-smoke.md
git commit -m "docs: HOME unification browser smoke verification record"
```

---

## Self-Review Pass

The following items in the spec must each map to at least one task above. Cross-check:

- §5.1 (3 ShiftType variants) → Tasks 7, 9, 10 ✓
- §5.2 (SourceTimeOffRequestId) → Task 1 ✓
- §5.3 (MoleculeApprovalSettings) → Task 6 ✓
- §5.4 (HomeType semantic clarification) → Tasks 25, 26 ✓
- §5.5 (Remove DefaultStart/EndTime) → Task 4 ✓
- §5.6 (Anchor on DerivedRotationRule) → Tasks 11, 12, 13 ✓
- §6.1 (Approver pool) → Task 18 ✓
- §6.2 (Primary + fallback) → Task 18 (default) + Task 21 (Private filter) ✓
- §6.3 (Private flag) → Tasks 2, 21 ✓
- §6.4 (Dual approval state machine) → Tasks 3, 8, 19 ✓
- §6.5 (Empty-pool fallback) → Task 18 ✓
- §6.6 (Threshold setting UI) → Task 20 ✓
- §6.7 (Single creation surface) → Task 31 ✓
- §6.8 (Approval service shape) → Tasks 16, 17, 19 ✓
- §7 (Materialiser) → Tasks 15, 16 ✓
- §7.3 (Vacation–rotation interaction) → Tasks 15 (vacation supersedes), 16 (cancel restoration) ✓
- §7.4 (HOME_AM dedup) → covered in Task 15's diff algorithm ✓
- §8 (Validation) → Tasks 9, 10 (IsHome generalisation; existing rules transparently apply) ✓
- §9 (Calendar rendering) → Tasks 22, 23, 24 ✓
- §10 (HomeType rework) → Tasks 25, 26, 27 ✓
- §10.5 (SignalR on Generate) → Task 29 ✓
- §11 (X-button) → Task 28 ✓
- §12 (Visibility fixes) → Tasks 23, 24, 29 ✓
- §13 (Migrations) → Tasks 1-6, 13 ✓
- §13.3 (Feature flag) → Task 32 ✓
- §13.4 (Removal of /Requests/TimeOff/Create) → Task 31 ✓
- §14 (Testing) → Tests embedded in Tasks 7, 9, 12, 15, 17, 18, 19; smoke in Task 33 ✓

All §15 implementation-order items have corresponding tasks. No gaps detected.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-05-home-unification.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Each subagent gets the spec + the task and produces a single commit. I verify each commit before launching the next.

**2. Inline Execution** — Execute tasks in this session using executing-plans. Batch execution with checkpoints for review.

Which approach?
