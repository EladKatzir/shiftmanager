# Chores↔ShiftType Parity — Phase 2 (Services) Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (- [ ]) syntax.
**Goal:** Land the service layer for chore↔shift parity on top of the Phase 1 entities: a pure `EligibilityEvaluator`, wire eligibility (officer/exemption = HARD, gender = overrideable WARNING) into `BusyService.ValidateChoreAsync`, a `ChoreCategoryService` (clone of `ShiftCategoryService`), a `ChoreEligibilityAdminService` (rule + exemption CRUD), `Chore.WeightMinutes` resolution frozen at create in EVERY create-path, a template `StampTemplateAsync`, the picker `GetEligibilityForCandidateAsync`, and a shared `DurationFormat` — all with real-SQLite + pure unit tests, touching NO shift runtime behavior.
**Architecture:** The eligibility primitive is a pure, stateless domain service (`IEligibilityEvaluator`) — no DB, no DI dependencies — so it is unit-testable in isolation and reusable by both `BusyService` (severity mapping) and `ChoreService` (picker hints). Severity is assigned by the CALLER (`BusyService`), not the evaluator: officer-rank and exemption violations become hard `ValidationIssue(Error)`; gender violations become overrideable `ValidationIssue(Warning)` folded into the existing HMAC-token warning block. Weight is frozen into `Chore.WeightMinutes` at create time across all three direct create-paths (`ChoreService.CreateChoreAsync`, `ChoreService.ReplaceShiftWithChoreAsync`, `ChoreApiService.CreateChoreAsync`). `ChoreCategoryService` mirrors `ShiftCategoryService` 1:1. New services are scoped DI registrations next to their shift siblings.
**Tech Stack:** ASP.NET Core 8.0, EF Core (SQLite), xUnit + FluentAssertions, real-SQLite tests.
**Depends on:** Phase 1 foundation (entities). **Spec:** docs/superpowers/specs/2026-06-14-chores-shifttype-parity-design.md

---

## Pre-flight (read once before any task)

- **Executable lock (CRITICAL):** every `dotnet build`/`dotnet test` fails or uses a stale binary if the dev app on `:5000` is running (the `bin/Debug` exe holds a file lock). Before any build/test, confirm the app is stopped. If a lock error appears, STOP and ask the user to terminate the run — do not retry over a lock.
- **Tests run sequentially:** always append `-- xUnit.ParallelizeTestCollections=false` (parallel `:memory:` SQLite runs produce ~spurious contention failures).
- **Branch:** work on `dev`. Do NOT edit `FinalProductPublish/` (generated).
- **Phase 1 must already be merged on `dev`** — this plan references `ChoreCategory`, `UserChoreCategory`, `EligibilityRule`, `UserChoreExemption`, `ChoreTemplate`, `AppUser.Gender`/`DoesChores`, `ChoreType.ChoreCategoryId`/`DefaultWeightMinutes`, `Chore.WeightMinutes`, and the extracted `Services/IChoreService.cs`. If any is missing, finish Phase 1 first.
- **Test idiom:** real SQLite via `new SqliteConnection("DataSource=:memory:")` + `new AppDbContext(options)` + `EnsureCreatedAsync` (mirror `ShiftManager.Tests/UnitTests/Migrations/ShiftCategoryBackfillTests.cs`), or the `SqliteDbContextFixture` helper where a fixture is cleaner (mirror `ShiftManager.Tests/UnitTests/Services/ShiftCategoryServiceTests.cs`). NEVER `UseInMemoryDatabase`. Always `IgnoreQueryFilters()` when re-reading molecule-scoped or cross-tenant rows.

---

## File Structure

**Create:**
- `Services/IEligibilityEvaluator.cs` — pure evaluator interface + `EligibilityViolation`/`EligibilityResult` result types.
- `Services/EligibilityEvaluator.cs` — the stateless implementation.
- `Services/IChoreCategoryService.cs` — chore-category CRUD + membership (clone of `IShiftCategoryService`).
- `Services/ChoreCategoryService.cs` — implementation (clone of `ShiftCategoryService`).
- `Services/IChoreEligibilityAdminService.cs` — `EligibilityRule` set-for-type + exemption add/remove.
- `Services/ChoreEligibilityAdminService.cs` — implementation.
- `Services/DurationFormat.cs` — static `FormatMinutes`/`FormatHours` (bilingual, single definition).
- `ShiftManager.Tests/UnitTests/Services/EligibilityEvaluatorTests.cs`
- `ShiftManager.Tests/UnitTests/Services/BusyServiceChoreEligibilityTests.cs`
- `ShiftManager.Tests/UnitTests/Services/ChoreCategoryServiceTests.cs`
- `ShiftManager.Tests/UnitTests/Services/ChoreEligibilityAdminServiceTests.cs`
- `ShiftManager.Tests/UnitTests/Services/ChoreWeightAndStampTests.cs`
- `ShiftManager.Tests/UnitTests/Services/DurationFormatTests.cs`

**Modify:**
- `Services/BusyService.cs` — inject `IEligibilityEvaluator`; add officer/exempt HARD gates + gender WARNING to `ValidateChoreAsync`.
- `Services/IChoreService.cs` — add `StampTemplateAsync`, `GetEligibilityForCandidateAsync`, the result DTO, the weight constant reference.
- `Services/ChoreService.cs` — `DEFAULT_CHORE_WEIGHT_MINUTES`, `ResolveWeightMinutes` helper, write `WeightMinutes` in `CreateChoreAsync` + `ReplaceShiftWithChoreAsync`, implement `StampTemplateAsync` + `GetEligibilityForCandidateAsync`.
- `Services/Api/ChoreApiService.cs` — write `WeightMinutes` in `CreateChoreAsync` (third direct create-path).
- `Program.cs` — register `IEligibilityEvaluator`, `IChoreCategoryService`, `IChoreEligibilityAdminService` (scoped).

**Resx (touched in Task 2):** add `Error_ChoreRequiresOfficerRank`, `Error_ChoreUserExempt`, `Warning_ChoreRequiresGenderMale`, `Warning_ChoreRequiresGenderFemale`, `Warning_ChoreGenderUnspecified` to `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx`. (Wording routed through a `localization-qa` pass per spec §13.8 — the keys are load-bearing; the exact Hebrew is not.)

---

## Task 1: `EligibilityEvaluator` (pure, stateless)

**Files:**
- Create: `Services/IEligibilityEvaluator.cs`, `Services/EligibilityEvaluator.cs`
- Create: `ShiftManager.Tests/UnitTests/Services/EligibilityEvaluatorTests.cs`

The evaluator is pure: it takes a materialized `AppUser`, the rules already loaded for one subject, and a boolean exemption flag; it returns the list of violations. It assigns NO severity — that is the caller's job (Task 2). It reuses `MilitaryRankExtensions.IsOfficer()` (`(int)rank >= 9`).

- [ ] **Step 1: Write the failing test** `ShiftManager.Tests/UnitTests/Services/EligibilityEvaluatorTests.cs`

```csharp
using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Pure unit coverage for <see cref="EligibilityEvaluator"/> — no DB, no DI. Mirrors the
/// <c>MilitaryRankExtensionsTests</c> style (in-memory objects, FluentAssertions). Severity
/// mapping is NOT tested here (it lives in BusyService); this proves WHICH violations fire.
/// </summary>
public class EligibilityEvaluatorTests
{
    private static readonly EligibilityEvaluator Sut = new();

    private static EligibilityRule GenderRule(Gender g) => new()
    {
        SubjectKind = EligibilitySubjectKind.ChoreType, SubjectId = 1,
        RuleKind = EligibilityRuleKind.RequiresGender, GenderValue = g
    };

    private static EligibilityRule OfficerRule() => new()
    {
        SubjectKind = EligibilitySubjectKind.ChoreType, SubjectId = 1,
        RuleKind = EligibilityRuleKind.RequiresOfficerRank
    };

    private static AppUser User(Gender g = Gender.Unspecified, MilitaryRank rank = MilitaryRank.Turai)
        => new() { Id = 5, Gender = g, Rank = rank };

    [Fact]
    public void No_Rules_No_Exemption_Is_Eligible()
    {
        var r = Sut.Evaluate(User(), Array.Empty<EligibilityRule>(), userHasExemptionForSubject: false);
        r.IsEligible.Should().BeTrue();
        r.Violations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Gender.Male)]
    [InlineData(Gender.Female)]
    public void Matching_Gender_Is_Eligible(Gender required)
    {
        var r = Sut.Evaluate(User(g: required), new[] { GenderRule(required) }, false);
        r.IsEligible.Should().BeTrue();
        r.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Definite_Gender_Mismatch_Violates_Gender()
    {
        var r = Sut.Evaluate(User(g: Gender.Female), new[] { GenderRule(Gender.Male) }, false);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().ContainSingle().Which.Should().Be(EligibilityViolation.RequiresGender);
    }

    [Fact]
    public void Unspecified_Gender_Also_Violates_Gender()
    {
        var r = Sut.Evaluate(User(g: Gender.Unspecified), new[] { GenderRule(Gender.Male) }, false);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().ContainSingle().Which.Should().Be(EligibilityViolation.RequiresGender);
    }

    [Fact]
    public void Officer_Passes_Officer_Rule()
    {
        var r = Sut.Evaluate(User(rank: MilitaryRank.Seren), new[] { OfficerRule() }, false);
        r.IsEligible.Should().BeTrue();
    }

    [Fact]
    public void Enlisted_Violates_Officer_Rule()
    {
        var r = Sut.Evaluate(User(rank: MilitaryRank.Turai), new[] { OfficerRule() }, false);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().ContainSingle().Which.Should().Be(EligibilityViolation.RequiresOfficerRank);
    }

    [Fact]
    public void Exemption_Violates_Even_With_No_Rules()
    {
        var r = Sut.Evaluate(User(), Array.Empty<EligibilityRule>(), userHasExemptionForSubject: true);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().ContainSingle().Which.Should().Be(EligibilityViolation.Exempt);
    }

    [Fact]
    public void Multiple_Violations_All_Reported()
    {
        // Female-required + officer-required, user is an enlisted male with an exemption → all three fire.
        var user = User(g: Gender.Male, rank: MilitaryRank.Turai);
        var rules = new[] { GenderRule(Gender.Female), OfficerRule() };
        var r = Sut.Evaluate(user, rules, userHasExemptionForSubject: true);
        r.IsEligible.Should().BeFalse();
        r.Violations.Should().BeEquivalentTo(new[]
        {
            EligibilityViolation.RequiresGender,
            EligibilityViolation.RequiresOfficerRank,
            EligibilityViolation.Exempt
        });
    }

    [Fact]
    public void Gender_Rule_With_Null_GenderValue_Is_Ignored()
    {
        // Defensive: a RequiresGender rule with no GenderValue is malformed config — never blocks.
        var rule = new EligibilityRule
        {
            SubjectKind = EligibilitySubjectKind.ChoreType, SubjectId = 1,
            RuleKind = EligibilityRuleKind.RequiresGender, GenderValue = null
        };
        var r = Sut.Evaluate(User(g: Gender.Unspecified), new[] { rule }, false);
        r.IsEligible.Should().BeTrue();
        r.Violations.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run it to verify it FAILS to compile** (the types don't exist yet)

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~EligibilityEvaluatorTests" -- xUnit.ParallelizeTestCollections=false`
Expected: build error — `EligibilityEvaluator`, `EligibilityViolation`, `EligibilityResult` are not defined.

- [ ] **Step 3: Create `Services/IEligibilityEvaluator.cs`**

```csharp
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// One distinct eligibility failure. Severity is intentionally NOT encoded here — the caller
/// (BusyService) maps kind → severity per the spec: RequiresGender → overrideable WARNING;
/// RequiresOfficerRank and Exempt → HARD errors.
/// </summary>
public enum EligibilityViolation
{
    RequiresGender = 0,
    RequiresOfficerRank = 1,
    Exempt = 2
}

/// <summary>
/// Outcome of an eligibility evaluation for one (user, subject) pair. <see cref="IsEligible"/> is
/// just <c>Violations.Count == 0</c>; <see cref="Violations"/> lists every distinct failure so the
/// caller can render and severity-map each one.
/// </summary>
public sealed record EligibilityResult(IReadOnlyList<EligibilityViolation> Violations)
{
    public bool IsEligible => Violations.Count == 0;

    public static readonly EligibilityResult Eligible = new(Array.Empty<EligibilityViolation>());
}

/// <summary>
/// Pure, stateless evaluator of <see cref="EligibilityRule"/> rows + a per-user exemption flag
/// against a materialized <see cref="AppUser"/>. No DB access, no DI dependencies — fully unit
/// testable. Reused by BusyService (assignment gate) and ChoreService (picker hints).
/// </summary>
public interface IEligibilityEvaluator
{
    /// <param name="user">The candidate. Only <c>Gender</c> and <c>Rank</c> are read.</param>
    /// <param name="rulesForSubject">All eligibility rules already loaded for the one subject
    /// (e.g. the ChoreType). The evaluator does NOT filter by subject — the caller batch-loads
    /// the correct subject's rows.</param>
    /// <param name="userHasExemptionForSubject">True iff a UserChoreExemption row exists for this
    /// (user, subject). Modeled as a flag (not a rule) because exemptions are chore-specific.</param>
    EligibilityResult Evaluate(
        AppUser user,
        IReadOnlyList<EligibilityRule> rulesForSubject,
        bool userHasExemptionForSubject);
}
```

- [ ] **Step 4: Create `Services/EligibilityEvaluator.cs`**

```csharp
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <inheritdoc cref="IEligibilityEvaluator"/>
public sealed class EligibilityEvaluator : IEligibilityEvaluator
{
    public EligibilityResult Evaluate(
        AppUser user,
        IReadOnlyList<EligibilityRule> rulesForSubject,
        bool userHasExemptionForSubject)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(rulesForSubject);

        var violations = new List<EligibilityViolation>();

        foreach (var rule in rulesForSubject)
        {
            switch (rule.RuleKind)
            {
                case EligibilityRuleKind.RequiresGender:
                    // A RequiresGender rule with no target value is malformed config — ignore it
                    // rather than block everyone. Unspecified-on-the-user always violates (fail to
                    // a WARNING, per spec — the manager can still override).
                    if (rule.GenderValue.HasValue && user.Gender != rule.GenderValue.Value)
                        AddOnce(violations, EligibilityViolation.RequiresGender);
                    break;

                case EligibilityRuleKind.RequiresOfficerRank:
                    if (!user.Rank.IsOfficer())
                        AddOnce(violations, EligibilityViolation.RequiresOfficerRank);
                    break;
            }
        }

        if (userHasExemptionForSubject)
            AddOnce(violations, EligibilityViolation.Exempt);

        return violations.Count == 0 ? EligibilityResult.Eligible : new EligibilityResult(violations);
    }

    private static void AddOnce(List<EligibilityViolation> list, EligibilityViolation v)
    {
        if (!list.Contains(v)) list.Add(v);
    }
}
```

- [ ] **Step 5: Run it to verify it PASSES**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~EligibilityEvaluatorTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (10 tests). If `Multiple_Violations_All_Reported` reports duplicates, `AddOnce` regressed; if `Gender_Rule_With_Null_GenderValue_Is_Ignored` fails, the `rule.GenderValue.HasValue` guard is missing.

- [ ] **Step 6: Commit**

```bash
git add Services/IEligibilityEvaluator.cs Services/EligibilityEvaluator.cs ShiftManager.Tests/UnitTests/Services/EligibilityEvaluatorTests.cs
git commit -m "feat(chores): pure EligibilityEvaluator (gender/officer/exempt) + unit tests"
```

---

## Task 2: Wire eligibility into `BusyService.ValidateChoreAsync`

**Files:**
- Modify: `Services/BusyService.cs` (ctor + field ~17-46; `ValidateChoreAsync` ~178-307)
- Modify: `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx` (add 5 keys)
- Create: `ShiftManager.Tests/UnitTests/Services/BusyServiceChoreEligibilityTests.cs`

Officer/exempt insert as HARD errors AFTER the `USER_NOT_IN_MOLECULE` block and BEFORE the warning block (so they short-circuit like the other hard gates). Gender joins the warnings (overrideable by the existing token path). The rule load and exemption-existence check are two cheap queries inside the already-`IgnoreQueryFilters()` method (rules + exemptions are not tenant-filtered).

- [ ] **Step 1: Add the 5 resx keys** (English in `Resources/SharedResources.resx`, Hebrew in `Resources/SharedResources.he-IL.resx`). Add each `<data>` block alongside the existing `Error_*`/`Warning_*` entries.

`Resources/SharedResources.resx`:
```xml
  <data name="Error_ChoreRequiresOfficerRank" xml:space="preserve">
    <value>This chore requires officer rank.</value>
  </data>
  <data name="Error_ChoreUserExempt" xml:space="preserve">
    <value>This user is exempt from this chore type.</value>
  </data>
  <data name="Warning_ChoreRequiresGenderMale" xml:space="preserve">
    <value>This chore is designated for male personnel.</value>
  </data>
  <data name="Warning_ChoreRequiresGenderFemale" xml:space="preserve">
    <value>This chore is designated for female personnel.</value>
  </data>
  <data name="Warning_ChoreGenderUnspecified" xml:space="preserve">
    <value>This chore is gender-restricted and this user's gender is not recorded.</value>
  </data>
```

`Resources/SharedResources.he-IL.resx`:
```xml
  <data name="Error_ChoreRequiresOfficerRank" xml:space="preserve">
    <value>מטלה זו דורשת דרגת קצונה.</value>
  </data>
  <data name="Error_ChoreUserExempt" xml:space="preserve">
    <value>משתמש זה פטור מסוג מטלה זה.</value>
  </data>
  <data name="Warning_ChoreRequiresGenderMale" xml:space="preserve">
    <value>מטלה זו מיועדת לגברים.</value>
  </data>
  <data name="Warning_ChoreRequiresGenderFemale" xml:space="preserve">
    <value>מטלה זו מיועדת לנשים.</value>
  </data>
  <data name="Warning_ChoreGenderUnspecified" xml:space="preserve">
    <value>מטלה זו מוגבלת לפי מין והמין של משתמש זה אינו רשום.</value>
  </data>
```

> The Hebrew strings above are a first pass routed through a `localization-qa` review during implementation (spec §13.8). The KEYS are the contract; the exact wording may be refined.

- [ ] **Step 2: Write the failing test** `ShiftManager.Tests/UnitTests/Services/BusyServiceChoreEligibilityTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for the chore-eligibility gates added to <see cref="BusyService.ValidateChoreAsync"/>:
/// officer-rank + exemption are HARD errors (NOT clearable by an override token); gender is an
/// overrideable WARNING (definite mismatch AND Unspecified); free-text (null ChoreTypeId) bypasses
/// eligibility entirely. Mirrors the ShiftCategoryBackfillTests SQLite setup idiom.
/// </summary>
public sealed class BusyServiceChoreEligibilityTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private BusyService _sut = null!;

    private const int Molecule = 1;
    private const int OfficerChoreType = 200;
    private const int FemaleChoreType = 201;
    private const int ExemptChoreType = 202;
    private static readonly DateOnly Date = new(2026, 7, 1);

    // A no-op IStringLocalizer that echoes the key back as the value (key-as-value).
    private sealed class EchoLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, false);
        public LocalizedString this[string name, params object[] arguments] => new(name, name, false);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    // Minimal membership service: a user is only in their own primary company (no extra memberships).
    // GetMembershipsAsync is the ONLY member BusyService.IsUserInMoleculeAsync touches; implement the
    // rest of ICompanyMembershipService per its real definition (no-op/false).
    private sealed class StubMembership : ICompanyMembershipService
    {
        public Task<IReadOnlyList<CompanyMembership>> GetMembershipsAsync(int userId)
            => Task.FromResult<IReadOnlyList<CompanyMembership>>(new List<CompanyMembership>());
        // Implement the remaining ICompanyMembershipService members per the real interface (no-op/false).
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = Molecule, AreaId = 1, Name = "M", DisplayName = "M" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = Molecule });

        // Users (all active Standard so they pass USER_*/CanDoChores; differ on Gender + Rank).
        _db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "officer@x.mil", DisplayName = "Officer",
                          IsActive = true, AccountType = AccountType.Standard, Rank = MilitaryRank.Seren, Gender = Gender.Male },
            new AppUser { Id = 11, CompanyId = 1, Email = "enlisted@x.mil", DisplayName = "Enlisted",
                          IsActive = true, AccountType = AccountType.Standard, Rank = MilitaryRank.Turai, Gender = Gender.Male },
            new AppUser { Id = 12, CompanyId = 1, Email = "female@x.mil", DisplayName = "Female",
                          IsActive = true, AccountType = AccountType.Standard, Rank = MilitaryRank.Turai, Gender = Gender.Female },
            new AppUser { Id = 13, CompanyId = 1, Email = "unspec@x.mil", DisplayName = "Unspec",
                          IsActive = true, AccountType = AccountType.Standard, Rank = MilitaryRank.Turai, Gender = Gender.Unspecified });

        _db.ChoreTypes.AddRange(
            new ChoreType { Id = OfficerChoreType, MoleculeId = Molecule, Name = "Guard", DisplayName = "Guard", CreatedByUserId = 10 },
            new ChoreType { Id = FemaleChoreType, MoleculeId = Molecule, Name = "FemaleOnly", DisplayName = "FemaleOnly", CreatedByUserId = 10 },
            new ChoreType { Id = ExemptChoreType, MoleculeId = Molecule, Name = "Heavy", DisplayName = "Heavy", CreatedByUserId = 10 });
        await _db.SaveChangesAsync();

        _db.EligibilityRules.AddRange(
            new EligibilityRule { SubjectKind = EligibilitySubjectKind.ChoreType, SubjectId = OfficerChoreType,
                                  RuleKind = EligibilityRuleKind.RequiresOfficerRank, CreatedBy = 10 },
            new EligibilityRule { SubjectKind = EligibilitySubjectKind.ChoreType, SubjectId = FemaleChoreType,
                                  RuleKind = EligibilityRuleKind.RequiresGender, GenderValue = Gender.Female, CreatedBy = 10 });
        // User 11 (enlisted male) is exempt from the Heavy chore type.
        _db.UserChoreExemptions.Add(new UserChoreExemption { UserId = 11, ChoreTypeId = ExemptChoreType, CreatedBy = 10 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKeyHmacSecret"] = "test-secret-please-change" })
            .Build();

        _sut = new BusyService(
            _db, new EchoLocalizer(), NullLogger<BusyService>.Instance,
            hierarchySettingsService: null!, configCache: null!, configuration: config,
            membershipService: new StubMembership(),
            eligibilityEvaluator: new EligibilityEvaluator());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private BusyTarget.Chore Target(int? choreTypeId) => new(Date, Molecule, choreTypeId);

    [Fact]
    public async Task Officer_Rule_Blocks_Enlisted_As_Hard_Error()
    {
        var v = await _sut.ValidateAsync(Target(OfficerChoreType), userId: 11, actorUserId: 0);
        v.CanProceed.Should().BeFalse();
        v.Errors.Should().ContainSingle(e => e.Key == "ELIG_OFFICER_RANK");
    }

    [Fact]
    public async Task Officer_Rule_Passes_Officer()
    {
        var v = await _sut.ValidateAsync(Target(OfficerChoreType), userId: 10, actorUserId: 0);
        v.CanProceed.Should().BeTrue();
        v.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Officer_Hard_Error_Is_Not_Cleared_By_Override_Token()
    {
        var token = _sut.GenerateOverrideToken(Target(OfficerChoreType), userId: 11, new[] { "ELIG_OFFICER_RANK" });
        var v = await _sut.ValidateAsync(Target(OfficerChoreType), userId: 11, actorUserId: 0, overrideToken: token);
        v.CanProceed.Should().BeFalse("officer-rank is a HARD error — tokens only clear warnings");
        v.Errors.Should().ContainSingle(e => e.Key == "ELIG_OFFICER_RANK");
    }

    [Fact]
    public async Task Exemption_Blocks_As_Hard_Error_Not_Cleared_By_Token()
    {
        var token = _sut.GenerateOverrideToken(Target(ExemptChoreType), userId: 11, new[] { "ELIG_EXEMPT" });
        var v = await _sut.ValidateAsync(Target(ExemptChoreType), userId: 11, actorUserId: 0, overrideToken: token);
        v.CanProceed.Should().BeFalse();
        v.Errors.Should().ContainSingle(e => e.Key == "ELIG_EXEMPT");
    }

    [Fact]
    public async Task Female_Rule_On_Male_Is_An_Overrideable_Warning()
    {
        // User 11 is male → mismatch against a female-only chore → WARNING, blocks WITHOUT a token.
        var v = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 11, actorUserId: 0);
        v.CanProceed.Should().BeTrue("gender is a warning, not a hard error — errors are empty");
        v.Warnings.Should().ContainSingle(w => w.Key == "ELIG_GENDER");
    }

    [Fact]
    public async Task Gender_Warning_Is_Cleared_By_A_Valid_Override_Token()
    {
        var token = _sut.GenerateOverrideToken(Target(FemaleChoreType), userId: 11, new[] { "ELIG_GENDER" });
        var v = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 11, actorUserId: 0, overrideToken: token);
        v.CanProceed.Should().BeTrue();
        v.Warnings.Should().BeEmpty("a valid token clears the gender warning");
    }

    [Fact]
    public async Task Unspecified_Gender_Also_Warns_And_Is_Overrideable()
    {
        var v = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 13, actorUserId: 0);
        v.Warnings.Should().ContainSingle(w => w.Key == "ELIG_GENDER");

        var token = _sut.GenerateOverrideToken(Target(FemaleChoreType), userId: 13, new[] { "ELIG_GENDER" });
        var cleared = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 13, actorUserId: 0, overrideToken: token);
        cleared.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Matching_Female_Passes_Clean()
    {
        var v = await _sut.ValidateAsync(Target(FemaleChoreType), userId: 12, actorUserId: 0);
        v.CanProceed.Should().BeTrue();
        v.Warnings.Should().NotContain(w => w.Key == "ELIG_GENDER");
    }

    [Fact]
    public async Task Free_Text_Chore_Bypasses_Eligibility_Entirely()
    {
        // No ChoreTypeId → no rules → no exemption lookup → enlisted user is eligible.
        var v = await _sut.ValidateAsync(Target(choreTypeId: null), userId: 11, actorUserId: 0);
        v.CanProceed.Should().BeTrue();
        v.Errors.Should().BeEmpty();
        v.Warnings.Should().BeEmpty();
    }
}
```

> If `ICompanyMembershipService`'s real interface differs from the `StubMembership` above, open `Services/ICompanyMembershipService.cs` and implement exactly its members (return empty/false). The only method `BusyService.IsUserInMoleculeAsync` calls is `GetMembershipsAsync`.

- [ ] **Step 3: Run it to verify it FAILS** (the ctor has no `eligibilityEvaluator` param yet, and the gates don't exist)

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~BusyServiceChoreEligibilityTests" -- xUnit.ParallelizeTestCollections=false`
Expected: build error (no such constructor) — or, after Step 4's ctor change, failing assertions because the gates aren't wired.

- [ ] **Step 4: Inject the evaluator into `BusyService`.** In `Services/BusyService.cs`, add a field and a ctor param.

Add the field after `private readonly ICompanyMembershipService _membershipService;` (line 24):
```csharp
    private readonly IEligibilityEvaluator _eligibilityEvaluator;
```

Add the ctor param (append to the parameter list after `ICompanyMembershipService membershipService`) and assign it. Replace the ctor signature + assignment block:
```csharp
    public BusyService(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<BusyService> logger,
        IHierarchySettingsService hierarchySettingsService,
        IAppConfigCacheService configCache,
        IConfiguration configuration,
        ICompanyMembershipService membershipService,
        IEligibilityEvaluator eligibilityEvaluator)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
        _hierarchySettingsService = hierarchySettingsService;
        _configCache = configCache;
        _membershipService = membershipService;
        _eligibilityEvaluator = eligibilityEvaluator;
        _hmacSecret = configuration["ApiKeyHmacSecret"]
            ?? Middleware.ApiAuthenticationMiddleware.HmacSecret;
    }
```

- [ ] **Step 5: Insert the eligibility block into `ValidateChoreAsync`.** In `Services/BusyService.cs`, locate the end of the `USER_NOT_IN_MOLECULE` block (the `}` after its `return new BusyValidation(false, errors, warnings);` at ~line 226) and insert the following BEFORE the `// Vacation conflict` comment (~line 228):

```csharp
        // ===== Chore eligibility (Phase 2) — only when targeting a real chore type =====
        // Free-text chores (null ChoreTypeId) carry no rules and are always eligible.
        if (target.ChoreTypeId.HasValue)
        {
            var choreTypeId = target.ChoreTypeId.Value;

            // Two cheap reads inside the already-IgnoreQueryFilters method (rules + exemptions are
            // global config, not tenant-filtered). Severity is assigned HERE, not in the evaluator.
            var rules = await _db.EligibilityRules.IgnoreQueryFilters()
                .Where(r => r.SubjectKind == EligibilitySubjectKind.ChoreType && r.SubjectId == choreTypeId)
                .ToListAsync();
            var hasExemption = await _db.UserChoreExemptions.IgnoreQueryFilters()
                .AnyAsync(e => e.UserId == userId && e.ChoreTypeId == choreTypeId);

            var eligibility = _eligibilityEvaluator.Evaluate(user, rules, hasExemption);
            foreach (var violation in eligibility.Violations)
            {
                switch (violation)
                {
                    case EligibilityViolation.RequiresOfficerRank:
                        errors.Add(new ValidationIssue(
                            "ELIG_OFFICER_RANK", _localizer["Error_ChoreRequiresOfficerRank"],
                            ValidationSeverity.Error, ValidationCategory.JobType));
                        break;

                    case EligibilityViolation.Exempt:
                        errors.Add(new ValidationIssue(
                            "ELIG_EXEMPT", _localizer["Error_ChoreUserExempt"],
                            ValidationSeverity.Error, ValidationCategory.JobType));
                        break;

                    case EligibilityViolation.RequiresGender:
                        // Overrideable warning. Message depends on the required gender (or Unspecified user).
                        var genderRule = rules.FirstOrDefault(r => r.RuleKind == EligibilityRuleKind.RequiresGender);
                        var messageKey = user.Gender == Gender.Unspecified
                            ? "Warning_ChoreGenderUnspecified"
                            : (genderRule?.GenderValue == Gender.Female
                                ? "Warning_ChoreRequiresGenderFemale"
                                : "Warning_ChoreRequiresGenderMale");
                        warnings.Add(new ValidationIssue(
                            "ELIG_GENDER", _localizer[messageKey],
                            ValidationSeverity.Warning, ValidationCategory.JobType));
                        break;
                }
            }

            // Hard eligibility errors short-circuit (like the USER_* gates above): no point loading
            // vacation/shift conflicts for an assignment that can never proceed.
            if (errors.Count > 0)
                return new BusyValidation(false, errors, warnings);
        }
```

> The required `using ShiftManager.Models.Validation;` is already present (the file uses `ValidationIssue`/`ValidationSeverity`/`ValidationCategory` throughout via `ShiftManager.Models.Support` + the `Validation` namespace — confirm `using ShiftManager.Models.Validation;` is in the using block; `ValidationIssue` lives there). `EligibilitySubjectKind`/`EligibilityRuleKind`/`Gender` live in `ShiftManager.Models.Support`, already imported (line 8). `EligibilityViolation` lives in `ShiftManager.Services` (same namespace as `BusyService`) — no import needed.

- [ ] **Step 6: Run it to verify it PASSES**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~BusyServiceChoreEligibilityTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (9 tests). If `Officer_Hard_Error_Is_Not_Cleared_By_Override_Token` fails, the hard-error short-circuit ran AFTER the warning-clear (it must `return` before reaching the token-clear at the bottom). If `Female_Rule_On_Male_Is_An_Overrideable_Warning` reports `CanProceed=false`, gender was wrongly added to `errors`.

> **Note (DI break is expected and fixed in Task 7):** the whole solution will not build until `Program.cs` registers `IEligibilityEvaluator` (Task 7). For this task, the `--filter`ed test still compiles+runs because the test constructs `BusyService` directly with `new EligibilityEvaluator()`. If the solution-wide build is needed sooner, do Task 7 Step 1 now.

- [ ] **Step 7: Commit**

```bash
git add Services/BusyService.cs Resources/SharedResources.resx Resources/SharedResources.he-IL.resx ShiftManager.Tests/UnitTests/Services/BusyServiceChoreEligibilityTests.cs
git commit -m "feat(chores): eligibility gates in BusyService (officer/exempt hard, gender overrideable warning)"
```

---

## Task 3: `ChoreCategoryService` (clone of `ShiftCategoryService`)

**Files:**
- Create: `Services/IChoreCategoryService.cs`, `Services/ChoreCategoryService.cs`
- Create: `ShiftManager.Tests/UnitTests/Services/ChoreCategoryServiceTests.cs`

A 1:1 clone of `IShiftCategoryService`/`ShiftCategoryService` over the chore entities: `ChoreCategory` (instead of `ShiftCategory`), `ChoreType.ChoreCategoryId` (instead of `ShiftType.CategoryId`), `UserChoreCategory` (instead of `UserShiftCategory`). Because `ChoreType` has a direct `MoleculeId` (no company-scope fallback), `AssignChoreTypeAsync`'s molecule resolution is simpler than the shift version.

- [ ] **Step 1: Create `Services/IChoreCategoryService.cs`**

```csharp
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Manages molecule-scoped <see cref="ChoreCategory"/> entities: CRUD, the chore-type↔category
/// assignment (<see cref="ChoreType.ChoreCategoryId"/>), and per-user membership
/// (<see cref="UserChoreCategory"/>). Mirrors <see cref="IShiftCategoryService"/>. Categories are the
/// "who does chores" axis (alongside <see cref="AppUser.DoesChores"/>).
/// </summary>
public interface IChoreCategoryService
{
    // ---- Category queries ----
    Task<List<ChoreCategory>> GetCategoriesForMoleculeAsync(int moleculeId, bool includeInactive = false);
    Task<ChoreCategory?> GetCategoryAsync(int categoryId);

    // ---- Category CRUD ----
    /// <summary>Creates a category. Returns null if the (molecule, name) pair already exists.</summary>
    Task<ChoreCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null);
    /// <summary>Renames/recolors a category. Returns false on not-found or a (molecule, name) collision.</summary>
    Task<bool> RenameAsync(int categoryId, string name, string displayName, string? color);
    /// <summary>Hard-deletes a category: cascades its memberships and nulls its chore types' ChoreCategoryId (FK SetNull).</summary>
    Task<bool> DeleteAsync(int categoryId);

    /// <summary>Counts the chore types owned by, and users mapped to, a category (for delete-impact preview).</summary>
    Task<(int ChoreTypeCount, int MemberCount)> GetUsageAsync(int categoryId);

    // ---- Chore-type ↔ category assignment ----
    /// <summary>
    /// Sets (or clears, when categoryId is null) a chore type's owning category. The category must live
    /// in the same molecule as the chore type. Returns false on not-found / cross-molecule mismatch.
    /// </summary>
    Task<bool> AssignChoreTypeAsync(int choreTypeId, int? categoryId);

    // ---- Per-user membership ----
    Task<List<int>> GetUserCategoryIdsAsync(int userId);
    /// <summary>Replaces a user's chore-category memberships with exactly the supplied set.</summary>
    Task SetUserCategoriesAsync(int userId, IReadOnlyCollection<int> categoryIds);
}
```

- [ ] **Step 2: Create `Services/ChoreCategoryService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — ChoreCategory/UserChoreCategory/ChoreType are
// molecule-scoped (no tenant filter); membership writes are scoped by explicit userId/categoryId params.
// Callers (ChoreTypes admin / Admin Users) re-verify the EditChoreTypes grant against the molecule.
public class ChoreCategoryService : IChoreCategoryService
{
    private readonly AppDbContext _db;

    public ChoreCategoryService(AppDbContext db) => _db = db;

    public async Task<List<ChoreCategory>> GetCategoriesForMoleculeAsync(int moleculeId, bool includeInactive = false)
    {
        var q = _db.ChoreCategories.Where(c => c.MoleculeId == moleculeId);
        if (!includeInactive)
            q = q.Where(c => c.IsActive);
        return await q.OrderBy(c => c.SortOrder).ThenBy(c => c.DisplayName).ToListAsync();
    }

    public Task<ChoreCategory?> GetCategoryAsync(int categoryId)
        => _db.ChoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId);

    public async Task<ChoreCategory?> CreateAsync(int moleculeId, string name, string displayName, string? color = null)
    {
        name = name.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var exists = await _db.ChoreCategories.AnyAsync(c => c.MoleculeId == moleculeId && c.Name == name);
        if (exists)
            return null;

        var nextSort = await _db.ChoreCategories.Where(c => c.MoleculeId == moleculeId)
            .Select(c => (int?)c.SortOrder).MaxAsync() ?? 0;

        var category = new ChoreCategory
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            SortOrder = nextSort + 1,
            IsActive = true
        };
        _db.ChoreCategories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task<bool> RenameAsync(int categoryId, string name, string displayName, string? color)
    {
        var category = await _db.ChoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category == null)
            return false;

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!string.Equals(category.Name, name, StringComparison.Ordinal))
        {
            var clash = await _db.ChoreCategories
                .AnyAsync(c => c.MoleculeId == category.MoleculeId && c.Name == name && c.Id != categoryId);
            if (clash)
                return false;
        }

        category.Name = name;
        category.DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        category.Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int categoryId)
    {
        var category = await _db.ChoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category == null)
            return false;

        // FK behavior handles the rest: UserChoreCategory cascades; ChoreType.ChoreCategoryId is set null.
        _db.ChoreCategories.Remove(category);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(int ChoreTypeCount, int MemberCount)> GetUsageAsync(int categoryId)
    {
        var choreTypeCount = await _db.ChoreTypes.CountAsync(ct => ct.ChoreCategoryId == categoryId);
        var memberCount = await _db.UserChoreCategories.CountAsync(m => m.ChoreCategoryId == categoryId);
        return (choreTypeCount, memberCount);
    }

    public async Task<bool> AssignChoreTypeAsync(int choreTypeId, int? categoryId)
    {
        // SECURITY-AUDITED: chore types are not tenant-filtered; load by explicit id.
        var choreType = await _db.ChoreTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == choreTypeId);
        if (choreType == null)
            return false;

        if (categoryId == null)
        {
            choreType.ChoreCategoryId = null;
            await _db.SaveChangesAsync();
            return true;
        }

        var category = await _db.ChoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId.Value);
        if (category == null)
            return false;

        // ChoreType has a direct MoleculeId (no company-scope fallback) — the category must match it.
        if (choreType.MoleculeId != category.MoleculeId)
            return false;

        choreType.ChoreCategoryId = categoryId;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<int>> GetUserCategoryIdsAsync(int userId)
        => await _db.UserChoreCategories
            .Where(m => m.UserId == userId)
            .Select(m => m.ChoreCategoryId)
            .ToListAsync();

    public async Task SetUserCategoriesAsync(int userId, IReadOnlyCollection<int> categoryIds)
    {
        var desired = categoryIds.Distinct().ToHashSet();

        // Only keep ids that actually exist (defensive against stale/forged ids).
        if (desired.Count > 0)
        {
            var valid = await _db.ChoreCategories
                .Where(c => desired.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync();
            desired = valid.ToHashSet();
        }

        var existing = await _db.UserChoreCategories
            .Where(m => m.UserId == userId)
            .ToListAsync();
        var existingIds = existing.Select(m => m.ChoreCategoryId).ToHashSet();

        var toRemove = existing.Where(m => !desired.Contains(m.ChoreCategoryId)).ToList();
        if (toRemove.Count > 0)
            _db.UserChoreCategories.RemoveRange(toRemove);

        foreach (var id in desired.Where(id => !existingIds.Contains(id)))
            _db.UserChoreCategories.Add(new UserChoreCategory { UserId = userId, ChoreCategoryId = id });

        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 3: Write the test** `ShiftManager.Tests/UnitTests/Services/ChoreCategoryServiceTests.cs` (cloned from `ShiftCategoryServiceTests`, using the `SqliteDbContextFixture` helper)

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for <see cref="ChoreCategoryService"/>: category CRUD + uniqueness, the
/// chore-type↔category assignment (incl. cross-molecule rejection), usage counts, per-user membership
/// replace semantics, and FK behavior on delete (SetNull for chore types, cascade for memberships).
/// Cloned from <c>ShiftCategoryServiceTests</c>.
/// </summary>
public sealed class ChoreCategoryServiceTests
{
    private static async Task SeedHierarchyAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Enforces_Name_Uniqueness_Within_Molecule_But_Allows_Reuse_Across_Molecules()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);

        var first = await svc.CreateAsync(1, "Physical", "Physical");
        first.Should().NotBeNull();

        var dup = await svc.CreateAsync(1, "Physical", "Physical");
        dup.Should().BeNull("a category with that name already exists in molecule 1");

        var otherMolecule = await svc.CreateAsync(2, "Physical", "Physical");
        otherMolecule.Should().NotBeNull("the same name is free in a different molecule");
    }

    [Fact]
    public async Task Rename_Rejects_A_Colliding_Name()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);

        var a = await svc.CreateAsync(1, "Physical", "Physical");
        var b = await svc.CreateAsync(1, "Computer", "Computer");

        (await svc.RenameAsync(b!.Id, "Physical", "Physical", null))
            .Should().BeFalse("renaming Computer to Physical collides within the molecule");
        (await svc.RenameAsync(b.Id, "Computer 2", "Computer 2", "#ff0000"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AssignChoreType_Honors_Molecule_Boundary()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        f.Db.ChoreTypes.Add(new ChoreType { Id = 100, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1 });
        await f.Db.SaveChangesAsync();
        var svc = new ChoreCategoryService(f.Db);

        var catM1 = await svc.CreateAsync(1, "Physical", "Physical");
        var catM2 = await svc.CreateAsync(2, "Other", "Other");

        (await svc.AssignChoreTypeAsync(100, catM2!.Id))
            .Should().BeFalse("category in molecule 2 cannot own a molecule-1 chore type");

        (await svc.AssignChoreTypeAsync(100, catM1!.Id)).Should().BeTrue();
        (await f.Db.ChoreTypes.FindAsync(100))!.ChoreCategoryId.Should().Be(catM1.Id);

        (await svc.AssignChoreTypeAsync(100, null)).Should().BeTrue("clearing is always allowed");
        (await f.Db.ChoreTypes.FindAsync(100))!.ChoreCategoryId.Should().BeNull();
    }

    [Fact]
    public async Task GetUsage_Counts_ChoreTypes_And_Members()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);
        var cat = await svc.CreateAsync(1, "Physical", "Physical");

        f.Db.ChoreTypes.AddRange(
            new ChoreType { Id = 100, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1, ChoreCategoryId = cat!.Id },
            new ChoreType { Id = 101, MoleculeId = 1, Name = "Guard", DisplayName = "Guard", CreatedByUserId = 1, ChoreCategoryId = cat.Id });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();
        await svc.SetUserCategoriesAsync(10, new[] { cat.Id });

        var (choreTypeCount, memberCount) = await svc.GetUsageAsync(cat.Id);
        choreTypeCount.Should().Be(2);
        memberCount.Should().Be(1);
    }

    [Fact]
    public async Task SetUserCategories_Replaces_The_Membership_Set()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);
        var physical = await svc.CreateAsync(1, "Physical", "Physical");
        var computer = await svc.CreateAsync(1, "Computer", "Computer");
        var extra = await svc.CreateAsync(1, "Extra", "Extra");
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();

        await svc.SetUserCategoriesAsync(10, new[] { physical!.Id, computer!.Id });
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEquivalentTo(new[] { physical.Id, computer.Id });

        await svc.SetUserCategoriesAsync(10, new[] { physical.Id, extra!.Id });
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEquivalentTo(new[] { physical.Id, extra.Id });

        await svc.SetUserCategoriesAsync(10, Array.Empty<int>());
        (await svc.GetUserCategoryIdsAsync(10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NullsChoreTypeFK_And_CascadesMembership()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedHierarchyAsync(f);
        var svc = new ChoreCategoryService(f.Db);
        var cat = await svc.CreateAsync(1, "Physical", "Physical");
        f.Db.ChoreTypes.Add(new ChoreType { Id = 100, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1, ChoreCategoryId = cat!.Id });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        await f.Db.SaveChangesAsync();
        await svc.SetUserCategoriesAsync(10, new[] { cat.Id });

        (await svc.DeleteAsync(cat.Id)).Should().BeTrue();

        f.Db.ChangeTracker.Clear();
        (await f.Db.ChoreTypes.FindAsync(100))!.ChoreCategoryId.Should().BeNull("FK SetNull un-categorizes the chore type");
        (await f.Db.UserChoreCategories.CountAsync(m => m.ChoreCategoryId == cat.Id))
            .Should().Be(0, "membership cascades on delete");
    }
}
```

- [ ] **Step 4: Run it to verify it PASSES**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ChoreCategoryServiceTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (6 tests). If `Delete_NullsChoreTypeFK_...` fails (type deleted instead of un-categorized), the Phase 1 FK config used Cascade/Restrict instead of SetNull — fix Phase 1, not here.

- [ ] **Step 5: Commit**

```bash
git add Services/IChoreCategoryService.cs Services/ChoreCategoryService.cs ShiftManager.Tests/UnitTests/Services/ChoreCategoryServiceTests.cs
git commit -m "feat(chores): ChoreCategoryService (CRUD + type assign + membership) cloned from ShiftCategoryService"
```

---

## Task 4: `ChoreEligibilityAdminService` (rule + exemption CRUD)

**Files:**
- Create: `Services/IChoreEligibilityAdminService.cs`, `Services/ChoreEligibilityAdminService.cs`
- Create: `ShiftManager.Tests/UnitTests/Services/ChoreEligibilityAdminServiceTests.cs`

`SetRulesForChoreTypeAsync` uses **replace semantics**: delete all existing `EligibilityRule` rows for the `(ChoreType, choreTypeId)` subject, then insert exactly the desired set (a gender requirement + an optional officer requirement — the shape the §7.1 admin fieldset persists). Exemptions are add/remove on `(userId, choreTypeId)`. The page layer gates these with `EditChoreTypes` (no grant logic here).

- [ ] **Step 1: Create `Services/IChoreEligibilityAdminService.cs`**

```csharp
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Admin operations over chore eligibility: the <see cref="EligibilityRule"/> set attached to a
/// chore type (replace semantics) and per-user <see cref="UserChoreExemption"/> waivers.
/// Gated at the PAGE layer by the existing <c>EditChoreTypes</c> grant — this service performs no
/// authorization. Only <see cref="EligibilitySubjectKind.ChoreType"/> subjects are written this cycle.
/// </summary>
public interface IChoreEligibilityAdminService
{
    /// <summary>All rules currently attached to a chore type.</summary>
    Task<List<EligibilityRule>> GetRulesForChoreTypeAsync(int choreTypeId);

    /// <summary>
    /// Replaces the chore type's eligibility rules with exactly the supplied requirements
    /// (delete-then-insert). <paramref name="requiredGender"/> null clears the gender rule;
    /// <paramref name="requiresOfficerRank"/> false clears the officer rule. <paramref name="createdBy"/>
    /// stamps the audit FK. Returns false if the chore type does not exist.
    /// </summary>
    Task<bool> SetRulesForChoreTypeAsync(int choreTypeId, Gender? requiredGender, bool requiresOfficerRank, int createdBy);

    /// <summary>Users currently exempt from a chore type.</summary>
    Task<List<UserChoreExemption>> GetExemptionsForChoreTypeAsync(int choreTypeId);

    /// <summary>
    /// Adds a waiver for (user, type). Idempotent: returns the existing row if one is already present
    /// (does not duplicate, does not overwrite the reason). Returns null if the chore type or user is missing.
    /// </summary>
    Task<UserChoreExemption?> AddExemptionAsync(int userId, int choreTypeId, string? reason, int createdBy);

    /// <summary>Removes the (user, type) waiver. Returns false if no such waiver exists.</summary>
    Task<bool> RemoveExemptionAsync(int userId, int choreTypeId);
}
```

- [ ] **Step 2: Create `Services/ChoreEligibilityAdminService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — EligibilityRule/UserChoreExemption/ChoreType are
// global/molecule-scoped config (no tenant filter). Authorization (EditChoreTypes) is enforced by the
// calling page against the chore type's molecule before invoking these methods.
public class ChoreEligibilityAdminService : IChoreEligibilityAdminService
{
    private readonly AppDbContext _db;

    public ChoreEligibilityAdminService(AppDbContext db) => _db = db;

    public async Task<List<EligibilityRule>> GetRulesForChoreTypeAsync(int choreTypeId)
        => await _db.EligibilityRules.IgnoreQueryFilters()
            .Where(r => r.SubjectKind == EligibilitySubjectKind.ChoreType && r.SubjectId == choreTypeId)
            .ToListAsync();

    public async Task<bool> SetRulesForChoreTypeAsync(int choreTypeId, Gender? requiredGender, bool requiresOfficerRank, int createdBy)
    {
        var typeExists = await _db.ChoreTypes.IgnoreQueryFilters().AnyAsync(ct => ct.Id == choreTypeId);
        if (!typeExists)
            return false;

        // Replace semantics: drop the existing rule set for this subject, then insert the desired one.
        var existing = await _db.EligibilityRules.IgnoreQueryFilters()
            .Where(r => r.SubjectKind == EligibilitySubjectKind.ChoreType && r.SubjectId == choreTypeId)
            .ToListAsync();
        if (existing.Count > 0)
            _db.EligibilityRules.RemoveRange(existing);

        if (requiredGender.HasValue)
        {
            _db.EligibilityRules.Add(new EligibilityRule
            {
                SubjectKind = EligibilitySubjectKind.ChoreType,
                SubjectId = choreTypeId,
                RuleKind = EligibilityRuleKind.RequiresGender,
                GenderValue = requiredGender.Value,
                CreatedBy = createdBy
            });
        }

        if (requiresOfficerRank)
        {
            _db.EligibilityRules.Add(new EligibilityRule
            {
                SubjectKind = EligibilitySubjectKind.ChoreType,
                SubjectId = choreTypeId,
                RuleKind = EligibilityRuleKind.RequiresOfficerRank,
                GenderValue = null,
                CreatedBy = createdBy
            });
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<UserChoreExemption>> GetExemptionsForChoreTypeAsync(int choreTypeId)
        => await _db.UserChoreExemptions.IgnoreQueryFilters()
            .Where(e => e.ChoreTypeId == choreTypeId)
            .ToListAsync();

    public async Task<UserChoreExemption?> AddExemptionAsync(int userId, int choreTypeId, string? reason, int createdBy)
    {
        var typeExists = await _db.ChoreTypes.IgnoreQueryFilters().AnyAsync(ct => ct.Id == choreTypeId);
        var userExists = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId);
        if (!typeExists || !userExists)
            return null;

        var existing = await _db.UserChoreExemptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.ChoreTypeId == choreTypeId);
        if (existing != null)
            return existing; // idempotent — the (UserId, ChoreTypeId) unique index would otherwise throw.

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is { Length: > 200 })
            trimmed = trimmed[..200];

        var exemption = new UserChoreExemption
        {
            UserId = userId,
            ChoreTypeId = choreTypeId,
            Reason = trimmed,
            CreatedBy = createdBy
        };
        _db.UserChoreExemptions.Add(exemption);
        await _db.SaveChangesAsync();
        return exemption;
    }

    public async Task<bool> RemoveExemptionAsync(int userId, int choreTypeId)
    {
        var exemption = await _db.UserChoreExemptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.ChoreTypeId == choreTypeId);
        if (exemption == null)
            return false;

        _db.UserChoreExemptions.Remove(exemption);
        await _db.SaveChangesAsync();
        return true;
    }
}
```

- [ ] **Step 3: Write the test** `ShiftManager.Tests/UnitTests/Services/ChoreEligibilityAdminServiceTests.cs`

```csharp
using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Real-SQLite coverage for <see cref="ChoreEligibilityAdminService"/>: rule replace-semantics
/// (gender + officer), idempotent exemption add, exemption remove, and not-found guards.
/// </summary>
public sealed class ChoreEligibilityAdminServiceTests
{
    private const int ChoreTypeId = 100;

    private static async Task SeedAsync(SqliteDbContextFixture f)
    {
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        f.Db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        f.Db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard });
        f.Db.ChoreTypes.Add(new ChoreType { Id = ChoreTypeId, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 10 });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task SetRules_Replaces_With_Gender_And_Officer()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        (await svc.SetRulesForChoreTypeAsync(ChoreTypeId, Gender.Female, requiresOfficerRank: true, createdBy: 10))
            .Should().BeTrue();

        var rules = await svc.GetRulesForChoreTypeAsync(ChoreTypeId);
        rules.Should().HaveCount(2);
        rules.Should().ContainSingle(r => r.RuleKind == EligibilityRuleKind.RequiresGender && r.GenderValue == Gender.Female);
        rules.Should().ContainSingle(r => r.RuleKind == EligibilityRuleKind.RequiresOfficerRank && r.GenderValue == null);
    }

    [Fact]
    public async Task SetRules_Is_Replace_Not_Append()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        await svc.SetRulesForChoreTypeAsync(ChoreTypeId, Gender.Male, requiresOfficerRank: true, createdBy: 10);
        // Now narrow to female-only, no officer.
        await svc.SetRulesForChoreTypeAsync(ChoreTypeId, Gender.Female, requiresOfficerRank: false, createdBy: 10);

        var rules = await svc.GetRulesForChoreTypeAsync(ChoreTypeId);
        rules.Should().ContainSingle().Which.RuleKind.Should().Be(EligibilityRuleKind.RequiresGender);
        rules[0].GenderValue.Should().Be(Gender.Female, "the prior Male + officer rules were replaced, not merged");
    }

    [Fact]
    public async Task SetRules_With_All_Cleared_Removes_Every_Rule()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        await svc.SetRulesForChoreTypeAsync(ChoreTypeId, Gender.Female, requiresOfficerRank: true, createdBy: 10);
        await svc.SetRulesForChoreTypeAsync(ChoreTypeId, requiredGender: null, requiresOfficerRank: false, createdBy: 10);

        (await svc.GetRulesForChoreTypeAsync(ChoreTypeId)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetRules_Returns_False_For_Missing_Type()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        (await svc.SetRulesForChoreTypeAsync(99999, Gender.Female, false, 10)).Should().BeFalse();
    }

    [Fact]
    public async Task AddExemption_Is_Idempotent()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        var first = await svc.AddExemptionAsync(10, ChoreTypeId, "knee injury", createdBy: 10);
        first.Should().NotBeNull();

        var second = await svc.AddExemptionAsync(10, ChoreTypeId, "different reason", createdBy: 10);
        second!.Id.Should().Be(first!.Id, "re-adding returns the existing waiver, no duplicate");

        (await svc.GetExemptionsForChoreTypeAsync(ChoreTypeId)).Should().ContainSingle();
    }

    [Fact]
    public async Task AddExemption_Returns_Null_For_Missing_User_Or_Type()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        (await svc.AddExemptionAsync(99999, ChoreTypeId, null, 10)).Should().BeNull("missing user");
        (await svc.AddExemptionAsync(10, 99999, null, 10)).Should().BeNull("missing chore type");
    }

    [Fact]
    public async Task RemoveExemption_Works_And_Reports_Missing()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        await SeedAsync(f);
        var svc = new ChoreEligibilityAdminService(f.Db);

        await svc.AddExemptionAsync(10, ChoreTypeId, null, 10);
        (await svc.RemoveExemptionAsync(10, ChoreTypeId)).Should().BeTrue();
        (await svc.RemoveExemptionAsync(10, ChoreTypeId)).Should().BeFalse("already removed");
        (await svc.GetExemptionsForChoreTypeAsync(ChoreTypeId)).Should().BeEmpty();
    }
}
```

- [ ] **Step 4: Run it to verify it PASSES**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ChoreEligibilityAdminServiceTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (7 tests). If `AddExemption_Is_Idempotent` throws a `DbUpdateException`, the early-return-on-existing guard regressed against the Phase 1 `(UserId, ChoreTypeId)` unique index.

- [ ] **Step 5: Commit**

```bash
git add Services/IChoreEligibilityAdminService.cs Services/ChoreEligibilityAdminService.cs ShiftManager.Tests/UnitTests/Services/ChoreEligibilityAdminServiceTests.cs
git commit -m "feat(chores): ChoreEligibilityAdminService (rule replace-semantics + exemption CRUD)"
```

---

## Task 5: `ChoreService` — weight resolution (all create-paths), stamp, eligibility-for-candidate

**Files:**
- Modify: `Services/IChoreService.cs` (add 2 methods + the result DTO)
- Modify: `Services/ChoreService.cs` (constant, helper, weight writes, 2 new methods)
- Modify: `Services/Api/ChoreApiService.cs` (weight write in its `CreateChoreAsync`)
- Create: `ShiftManager.Tests/UnitTests/Services/ChoreWeightAndStampTests.cs`

`DEFAULT_CHORE_WEIGHT_MINUTES = 480` lives on `ChoreService` as a `public const int` (single source of truth; JusticeService Phase 5 references it). Weight resolution is frozen at create: both `StartTime`+`EndTime` present and `EndTime > StartTime` → minutes between; else `ChoreType.DefaultWeightMinutes`; else 480. `StampTemplateAsync` loops the existing `CreateChoreAsync` per (date, assignee); `rotate=true` round-robins one assignee per date, `rotate=false` assigns every assignee every date; each call validates independently and a hard-error (or any failure) is recorded in the skipped list.

- [ ] **Step 1: Add the new contract to `Services/IChoreService.cs`.** Append inside the `IChoreService` interface (after `ValidateChoreAssignmentAsync`), and add the DTO + nested record below the interface in the same file.

```csharp
    /// <summary>
    /// Phase 2: stamps a <see cref="ChoreTemplate"/> across a date range. Loops the existing manual
    /// <see cref="CreateChoreAsync"/> per (date, assignee); each validates independently. A day that
    /// hard-errors (or otherwise fails) for an assignee is skipped and reported — never throws for a
    /// single bad day. Respects the one-active-chore-per-user-per-day unique index per stamp.
    /// </summary>
    /// <param name="rotate">true → round-robin: one assignee per date, cycling through
    /// <paramref name="assigneeIds"/>. false → fan-out: every assignee on every matching date.</param>
    Task<StampResult> StampTemplateAsync(
        int templateId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<DayOfWeek> weekdays,
        IReadOnlyList<int> assigneeIds,
        bool rotate);

    /// <summary>
    /// Phase 2: evaluates whether a candidate is eligible for a chore type (gender/officer/exempt),
    /// for the assignment picker's "greyed + reason" affordance. Pure eligibility only — does NOT
    /// check date conflicts (the picker layers BusyService on top). Free-text (choreTypeId resolving
    /// to no rules) returns eligible.
    /// </summary>
    Task<EligibilityResult> GetEligibilityForCandidateAsync(int userId, int choreTypeId);
```

Below the closing `}` of the interface (still inside `Services/IChoreService.cs`, after the `using`s are already present — add `using` for nothing new; `DayOfWeek` is in `System`):

```csharp
/// <summary>One chore that <see cref="IChoreService.StampTemplateAsync"/> created.</summary>
public sealed record StampCreated(DateOnly Date, int UserId, int ChoreId);

/// <summary>One (date, user) the stamp could NOT create, with a stable reason key for localization.</summary>
public sealed record StampSkipped(DateOnly Date, int UserId, string ReasonKey);

/// <summary>Summary of a template stamp: what was created and what was skipped (and why).</summary>
public sealed record StampResult(
    IReadOnlyList<StampCreated> Created,
    IReadOnlyList<StampSkipped> Skipped)
{
    public int CreatedCount => Created.Count;
    public int SkippedCount => Skipped.Count;
}
```

> `EligibilityResult` is already in `ShiftManager.Services` (Task 1), same namespace as `IChoreService` — no import needed.

- [ ] **Step 2: Add the constant + weight helper + weight write in `ChoreService.CreateChoreAsync`.** In `Services/ChoreService.cs`:

(a) Add the constant just inside the class, before the fields (~line 51):
```csharp
    /// <summary>Global fallback chore duration weight in minutes (8h). Single source of truth;
    /// JusticeService scales chore targets by this. Frozen into Chore.WeightMinutes at create.</summary>
    public const int DEFAULT_CHORE_WEIGHT_MINUTES = 480;
```

(b) Add the resolution helper as a private static method (anywhere in the class body, e.g. just after `GetCurrentUserId`):
```csharp
    /// <summary>
    /// Resolves a chore's frozen weight: explicit same-day [start,end) minutes →
    /// ChoreType.DefaultWeightMinutes → DEFAULT_CHORE_WEIGHT_MINUTES (480). EndTime &lt;= StartTime
    /// (midnight-crossing, out of scope) falls through to the type default / fallback.
    /// </summary>
    internal static int ResolveWeightMinutes(TimeOnly? startTime, TimeOnly? endTime, int? choreTypeDefaultWeight)
    {
        if (startTime.HasValue && endTime.HasValue && endTime.Value > startTime.Value)
        {
            var minutes = (int)(endTime.Value - startTime.Value).TotalMinutes;
            if (minutes > 0)
                return minutes;
        }
        return choreTypeDefaultWeight ?? DEFAULT_CHORE_WEIGHT_MINUTES;
    }
```

(c) In `CreateChoreAsync`, BEFORE constructing the `chore` object (the `chore = new Chore { ... }` block ~line 337), load the chore type's default weight and resolve. Insert just above `chore = new Chore`:
```csharp
                int? typeDefaultWeight = null;
                if (choreTypeId.HasValue)
                {
                    // ChoreType is molecule-scoped (not tenant-filtered); load by explicit id.
                    typeDefaultWeight = await _db.ChoreTypes.IgnoreQueryFilters()
                        .Where(ct => ct.Id == choreTypeId.Value)
                        .Select(ct => ct.DefaultWeightMinutes)
                        .FirstOrDefaultAsync();
                }
                var weightMinutes = ResolveWeightMinutes(startTime: null, endTime: null, typeDefaultWeight);
```

> `CreateChoreAsync`'s signature carries no `StartTime`/`EndTime` (untimed manual chores), so resolution falls through to the type default / 480. Then add `WeightMinutes = weightMinutes,` to the `chore = new Chore { ... }` initializer (after `Notes = notes?.Trim(),`).

- [ ] **Step 3: Write the weight in `ReplaceShiftWithChoreAsync`.** In the `chore = new Chore { ... }` initializer of `ReplaceShiftWithChoreAsync` (~line 554), add `WeightMinutes = DEFAULT_CHORE_WEIGHT_MINUTES,` (this path creates an untimed, type-less chore → the fallback). Add after `Notes = notes?.Trim(),`.

- [ ] **Step 4: Write the weight in `ChoreApiService.CreateChoreAsync`.** In `Services/Api/ChoreApiService.cs`, the `chore = new Chore { ... }` initializer (~line 195) creates an untimed, type-less chore. Add `WeightMinutes = ChoreService.DEFAULT_CHORE_WEIGHT_MINUTES,` after `Notes = ...`. Add `using ShiftManager.Services;` if not present (it is — line referencing `IBusyService`). This is the third and final direct create-path.

> The `QuickAddChore` page and `ChoresController` create through `ChoreService.CreateChoreAsync` / `ChoreApiService.CreateChoreAsync` respectively (verified) — they inherit the weight write, no separate edit.

- [ ] **Step 5: Implement `StampTemplateAsync` + `GetEligibilityForCandidateAsync`.** Append both to `ChoreService` (before the final `}`):

```csharp
    /// <inheritdoc/>
    public async Task<StampResult> StampTemplateAsync(
        int templateId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<DayOfWeek> weekdays,
        IReadOnlyList<int> assigneeIds,
        bool rotate)
    {
        var created = new List<StampCreated>();
        var skipped = new List<StampSkipped>();

        // ChoreTemplate is molecule-scoped (not tenant-filtered); load by explicit id.
        // SECURITY-AUDITED: SAFE — the page layer gates this with AssignChores before calling;
        // CreateChoreAsync re-checks CanUserManageChoreForAssigneeAsync per (date,user) below.
        var template = await _db.ChoreTemplates.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == templateId);
        if (template == null || !template.IsActive)
            return new StampResult(created, skipped);

        if (assigneeIds.Count == 0 || from > to)
            return new StampResult(created, skipped);

        var weekdaySet = weekdays.Count == 0
            ? null // empty = every day in range
            : weekdays.ToHashSet();

        // The template's resolved weight is frozen the same way a manual chore is; we pass the
        // template's start/end (or its WeightMinutesOverride) through the same resolver so the
        // stamped chore's WeightMinutes is consistent. Because CreateChoreAsync owns the persist,
        // we set the weight by resolving here and relying on CreateChoreAsync's own resolution for
        // the type default — but the template may carry its own times/override, so resolve explicitly
        // and only fall back to CreateChoreAsync when the template has neither.
        int rotateIndex = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (weekdaySet != null && !weekdaySet.Contains(date.DayOfWeek))
                continue;

            // rotate=true → one assignee this date (round-robin); rotate=false → all assignees.
            IEnumerable<int> assigneesForDate;
            if (rotate)
            {
                var userId = assigneeIds[rotateIndex % assigneeIds.Count];
                rotateIndex++;
                assigneesForDate = new[] { userId };
            }
            else
            {
                assigneesForDate = assigneeIds;
            }

            foreach (var userId in assigneesForDate)
            {
                var result = await CreateChoreAsync(
                    assigneeId: userId,
                    date: date,
                    title: template.DefaultTitle,
                    notes: template.Notes,
                    forceAssign: false,
                    moleculeId: template.MoleculeId,
                    choreTypeId: template.ChoreTypeId,
                    overrideToken: null);

                if (result.Success && result.Chore != null)
                {
                    created.Add(new StampCreated(date, userId, result.Chore.Id));
                }
                else
                {
                    // Surface the first hard-error key, or the warning sentinel, or the raw message.
                    var reasonKey = result.Validation?.Errors.FirstOrDefault()?.Key
                        ?? (result.Message == "BUSY_OVERRIDE_REQUIRED" ? "BUSY_OVERRIDE_REQUIRED" : result.Message);
                    skipped.Add(new StampSkipped(date, userId, reasonKey));
                }
            }
        }

        return new StampResult(created, skipped);
    }

    /// <inheritdoc/>
    public async Task<EligibilityResult> GetEligibilityForCandidateAsync(int userId, int choreTypeId)
    {
        // SECURITY-AUDITED: SAFE — read-only eligibility hint for the picker; user/rules/exemptions are
        // loaded by explicit id. The actual assignment gate is BusyService.ValidateChoreAsync.
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return EligibilityResult.Eligible; // no user → nothing to block on (picker won't list them anyway)

        var rules = await _db.EligibilityRules.IgnoreQueryFilters()
            .Where(r => r.SubjectKind == EligibilitySubjectKind.ChoreType && r.SubjectId == choreTypeId)
            .ToListAsync();
        var hasExemption = await _db.UserChoreExemptions.IgnoreQueryFilters()
            .AnyAsync(e => e.UserId == userId && e.ChoreTypeId == choreTypeId);

        return _eligibilityEvaluator.Evaluate(user, rules, hasExemption);
    }
```

> **Inject the evaluator into `ChoreService`** so `GetEligibilityForCandidateAsync` can call it. Add field `private readonly IEligibilityEvaluator _eligibilityEvaluator;`, add ctor param `IEligibilityEvaluator eligibilityEvaluator` (append to the parameter list), and assign `_eligibilityEvaluator = eligibilityEvaluator;`. Add `using ShiftManager.Models.Support;` — already present (line 4). `EligibilitySubjectKind` is in `ShiftManager.Models.Support` (imported).
>
> **Weight-resolution caveat (template times):** the snippet above passes `template.MoleculeId`/`ChoreTypeId` to `CreateChoreAsync`, which resolves weight from the **type default / 480** (it does not see the template's `StartTime`/`EndTime`/`WeightMinutesOverride`). That matches the manual flow exactly (manual chores are untimed). Honoring per-template times in the frozen weight is **deferred** (see Deferred Items) — flag this to the user; the stamped chore still gets a correct type-default/480 weight, just not the template's explicit window. If the user wants template times to drive weight now, `CreateChoreAsync` needs `startTime`/`endTime` params (a signature change touching all callers) — out of this task's scope.

- [ ] **Step 6: Write the test** `ShiftManager.Tests/UnitTests/Services/ChoreWeightAndStampTests.cs`. Weight resolution is tested directly on the pure helper (no DB); the stamp is tested by constructing a real `ChoreService` with stubbed collaborators.

```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Weight-resolution (pure, frozen) + template-stamp (rotate vs fan-out, skip-on-hard-error,
/// one-per-day index) coverage. Stamp runs a real ChoreService over real SQLite with permissive
/// grant/director stubs so CreateChoreAsync proceeds; eligibility hard-errors are exercised via
/// an officer-only chore type.
/// </summary>
public sealed class ChoreWeightAndStampTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private ChoreService _sut = null!;

    private const int Molecule = 1;
    private const int FreeTextTemplate = 300;
    private const int OfficerTemplate = 301;
    private const int OfficerChoreType = 200;

    // ----- pure resolver tests (no DB) -----
    [Fact]
    public void Weight_From_Times_When_Both_Present_Same_Day()
        => ChoreService.ResolveWeightMinutes(new TimeOnly(10, 0), new TimeOnly(14, 0), choreTypeDefaultWeight: 999)
            .Should().Be(240, "10:00-14:00 = 240m; explicit times win over the type default");

    [Fact]
    public void Weight_Falls_To_Type_Default_When_No_Times()
        => ChoreService.ResolveWeightMinutes(null, null, choreTypeDefaultWeight: 120).Should().Be(120);

    [Fact]
    public void Weight_Falls_To_480_When_No_Times_And_No_Type_Default()
        => ChoreService.ResolveWeightMinutes(null, null, choreTypeDefaultWeight: null)
            .Should().Be(ChoreService.DEFAULT_CHORE_WEIGHT_MINUTES).And.Be(480);

    [Fact]
    public void Weight_Falls_Through_When_End_Not_After_Start()
        => ChoreService.ResolveWeightMinutes(new TimeOnly(22, 0), new TimeOnly(2, 0), choreTypeDefaultWeight: 333)
            .Should().Be(333, "midnight-crossing is out of scope → fall through to the type default");

    // ----- stamp tests (real ChoreService over SQLite) -----
    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = Molecule, AreaId = 1, Name = "M", DisplayName = "M" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = Molecule });
        _db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "u1@x.mil", DisplayName = "U1", IsActive = true,
                          AccountType = AccountType.Standard, Rank = MilitaryRank.Turai },
            new AppUser { Id = 11, CompanyId = 1, Email = "u2@x.mil", DisplayName = "U2", IsActive = true,
                          AccountType = AccountType.Standard, Rank = MilitaryRank.Turai });
        _db.ChoreTypes.Add(new ChoreType { Id = OfficerChoreType, MoleculeId = Molecule, Name = "Guard", DisplayName = "Guard", CreatedByUserId = 10 });
        await _db.SaveChangesAsync();

        _db.EligibilityRules.Add(new EligibilityRule
        {
            SubjectKind = EligibilitySubjectKind.ChoreType, SubjectId = OfficerChoreType,
            RuleKind = EligibilityRuleKind.RequiresOfficerRank, CreatedBy = 10
        });
        _db.ChoreTemplates.AddRange(
            new ChoreTemplate { Id = FreeTextTemplate, MoleculeId = Molecule, Name = "Daily", DefaultTitle = "Sweep", IsActive = true, CreatedBy = 10 },
            new ChoreTemplate { Id = OfficerTemplate, MoleculeId = Molecule, Name = "Guard", DefaultTitle = "Guard", ChoreTypeId = OfficerChoreType, IsActive = true, CreatedBy = 10 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKeyHmacSecret"] = "test-secret-please-change" })
            .Build();

        var busy = new BusyService(
            _db, new TestSupport.EchoLocalizer(), NullLogger<BusyService>.Instance,
            hierarchySettingsService: null!, configCache: null!, configuration: config,
            membershipService: new TestSupport.StubMembership(),
            eligibilityEvaluator: new EligibilityEvaluator());

        _sut = new ChoreService(
            _db,
            new TestSupport.StubTenantResolver(),
            new TestSupport.StubHttpContextAccessor(currentUserId: 10),
            new TestSupport.StubDirectorService(),
            new TestSupport.PermissiveGrantService(),
            NullLogger<ChoreService>.Instance,
            new TestSupport.StubCompanyCache(moleculeId: Molecule),
            busy,
            new EligibilityEvaluator());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Stamp_RotateFalse_Assigns_Every_Assignee_Every_Matching_Date()
    {
        // Mon-Wed (2026-07-06..08), no weekday filter → 3 days × 2 users = 6 chores.
        var result = await _sut.StampTemplateAsync(
            FreeTextTemplate,
            from: new DateOnly(2026, 7, 6), to: new DateOnly(2026, 7, 8),
            weekdays: Array.Empty<DayOfWeek>(),
            assigneeIds: new[] { 10, 11 },
            rotate: false);

        result.CreatedCount.Should().Be(6);
        result.SkippedCount.Should().Be(0);
        result.Created.Select(c => c.UserId).Distinct().Should().BeEquivalentTo(new[] { 10, 11 });
    }

    [Fact]
    public async Task Stamp_RotateTrue_Round_Robins_One_Assignee_Per_Date()
    {
        // 4 days, rotate over [10,11] → 10,11,10,11.
        var result = await _sut.StampTemplateAsync(
            FreeTextTemplate,
            from: new DateOnly(2026, 7, 6), to: new DateOnly(2026, 7, 9),
            weekdays: Array.Empty<DayOfWeek>(),
            assigneeIds: new[] { 10, 11 },
            rotate: true);

        result.CreatedCount.Should().Be(4);
        result.Created.Select(c => c.UserId).Should().ContainInOrder(10, 11, 10, 11);
    }

    [Fact]
    public async Task Stamp_Respects_Weekday_Filter()
    {
        // 2026-07-06 is Monday. Filter to Monday only over a Mon-Wed range → 1 day.
        var result = await _sut.StampTemplateAsync(
            FreeTextTemplate,
            from: new DateOnly(2026, 7, 6), to: new DateOnly(2026, 7, 8),
            weekdays: new[] { DayOfWeek.Monday },
            assigneeIds: new[] { 10 },
            rotate: false);

        result.CreatedCount.Should().Be(1);
        result.Created[0].Date.Should().Be(new DateOnly(2026, 7, 6));
    }

    [Fact]
    public async Task Stamp_Skips_Hard_Error_Days_And_Reports_Them()
    {
        // Officer-only template; both users are enlisted → every (date,user) hard-errors and is skipped.
        var result = await _sut.StampTemplateAsync(
            OfficerTemplate,
            from: new DateOnly(2026, 7, 6), to: new DateOnly(2026, 7, 7),
            weekdays: Array.Empty<DayOfWeek>(),
            assigneeIds: new[] { 10 },
            rotate: false);

        result.CreatedCount.Should().Be(0);
        result.SkippedCount.Should().Be(2);
        result.Skipped.Should().OnlyContain(s => s.ReasonKey == "ELIG_OFFICER_RANK");
    }

    [Fact]
    public async Task Stamp_Honors_One_Active_Chore_Per_User_Per_Day()
    {
        // First stamp creates a chore for user 10 on day 1; second stamp on the same day must skip
        // (the second create raises a CHORE_CONFLICT warning → BUSY_OVERRIDE_REQUIRED → skipped).
        var day = new DateOnly(2026, 7, 6);
        var first = await _sut.StampTemplateAsync(FreeTextTemplate, day, day, Array.Empty<DayOfWeek>(), new[] { 10 }, rotate: false);
        first.CreatedCount.Should().Be(1);

        var second = await _sut.StampTemplateAsync(FreeTextTemplate, day, day, Array.Empty<DayOfWeek>(), new[] { 10 }, rotate: false);
        second.CreatedCount.Should().Be(0);
        second.SkippedCount.Should().Be(1);
        second.Skipped[0].ReasonKey.Should().Be("BUSY_OVERRIDE_REQUIRED",
            "a same-day second chore is an overrideable warning; the stamp passes no token, so it is skipped");
    }

    [Fact]
    public async Task Created_Chore_Has_Frozen_Default_Weight()
    {
        var day = new DateOnly(2026, 7, 6);
        await _sut.StampTemplateAsync(FreeTextTemplate, day, day, Array.Empty<DayOfWeek>(), new[] { 10 }, rotate: false);
        var chore = await _db.Chores.IgnoreQueryFilters().SingleAsync();
        chore.WeightMinutes.Should().Be(480, "free-text template → no times, no type default → 480 fallback frozen at create");
    }
}
```

- [ ] **Step 7: Create the shared test stubs** `ShiftManager.Tests/UnitTests/Services/TestSupport.cs` referenced above (echo localizer, membership stub, and the `ChoreService` collaborators). Implement each interface exactly per its definition — open `Services/ITenantResolver.cs`, `Services/IDirectorService.cs`, `Services/IGrantService.cs`, `Services/ICompanyCacheService.cs`, `Services/ICompanyMembershipService.cs` and implement every member (permissive: grants always true, tenant resolves `Molecule`, company cache returns a company with `MoleculeId = Molecule`, http accessor returns a principal whose `NameIdentifier` = 10).

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Shared permissive stubs for service-layer tests that need to drive ChoreService/BusyService end to
/// end without the real grant/tenant/director machinery. Authorization is deliberately permissive —
/// these tests assert business behavior (weight, stamp, eligibility), not authz (covered elsewhere).
/// </summary>
internal static class TestSupport
{
    internal sealed class EchoLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, false);
        public LocalizedString this[string name, params object[] arguments] => new(name, name, false);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    internal sealed class StubMembership : ICompanyMembershipService
    {
        public Task<IReadOnlyList<CompanyMembership>> GetMembershipsAsync(int userId)
            => Task.FromResult<IReadOnlyList<CompanyMembership>>(new List<CompanyMembership>());
        // Implement the remaining ICompanyMembershipService members per the real interface (no-op/false).
    }

    internal sealed class PermissiveGrantService : IGrantService
    {
        public Task<bool> HasGrantAsync(int userId, string grantKey) => Task.FromResult(true);
        public Task<bool> HasGrantForCompanyAsync(int userId, string grantKey, int companyId) => Task.FromResult(true);
        public Task<List<int>> GetAccessibleCompanyIdsForGrantAsync(int userId, string grantKey) => Task.FromResult(new List<int> { 1 });
        // Implement the remaining IGrantService members as permissive/no-op per the real interface.
    }

    internal sealed class StubTenantResolver : ITenantResolver
    {
        public int? GetCurrentTenantId() => 1;
        // Implement remaining ITenantResolver members per the real interface.
    }

    internal sealed class StubDirectorService : IDirectorService
    {
        // Implement IDirectorService members as no-op/false per the real interface.
    }

    internal sealed class StubCompanyCache : ICompanyCacheService
    {
        private readonly int _moleculeId;
        public StubCompanyCache(int moleculeId) => _moleculeId = moleculeId;
        public Task<Company?> GetCompanyAsync(int companyId)
            => Task.FromResult<Company?>(new Company { Id = companyId, Name = "Co", Slug = "co", MoleculeId = _moleculeId });
        // Implement remaining ICompanyCacheService members per the real interface.
    }

    internal sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public StubHttpContextAccessor(int currentUserId)
        {
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString()) }, "test");
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        }
        public HttpContext? HttpContext { get; set; }
    }
}
```

> The stub bodies above are skeletons: each real interface (`IGrantService`, `ITenantResolver`, `IDirectorService`, `ICompanyCacheService`) has more members than shown. When implementing, open each interface file and implement EVERY member (permissive booleans / empty lists / no-op tasks). Do not guess member lists — read the interfaces. This is the only place this plan cannot enumerate verbatim, because the collaborator interfaces are large and out of this phase's scope; the behavior required is uniformly "permissive".

- [ ] **Step 8: Run the weight+stamp tests**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~ChoreWeightAndStampTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (10 tests). If `Stamp_RotateTrue_...` order is wrong, the `rotateIndex` increments per assignee instead of per date. If `Stamp_Honors_One_Active_Chore_...` second stamp shows `CreatedCount=1`, the unique index or the existing-chore warning regressed.

- [ ] **Step 9: Commit**

```bash
git add Services/IChoreService.cs Services/ChoreService.cs Services/Api/ChoreApiService.cs ShiftManager.Tests/UnitTests/Services/ChoreWeightAndStampTests.cs ShiftManager.Tests/UnitTests/Services/TestSupport.cs
git commit -m "feat(chores): frozen WeightMinutes in all create-paths + StampTemplateAsync + GetEligibilityForCandidateAsync"
```

---

## Task 6: `DurationFormat` (shared bilingual formatter)

**Files:**
- Create: `Services/DurationFormat.cs`
- Create: `ShiftManager.Tests/UnitTests/Services/DurationFormatTests.cs`

Single definition reused by admin/calendar/justice (Phases 3-5). Language is detected from `CultureInfo.CurrentUICulture` (Hebrew = `he`/`he-IL`), the project's established idiom. `FormatMinutes(90)` → `"1h 30m"` / `"1ש׳ 30ד׳"`; `FormatHours(750)` → `"12.5h"` / `"12.5ש׳"`.

- [ ] **Step 1: Write the failing test** `ShiftManager.Tests/UnitTests/Services/DurationFormatTests.cs`

```csharp
using System.Globalization;
using FluentAssertions;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Pure formatting coverage for <see cref="DurationFormat"/>. Forces CurrentUICulture per-case so the
/// bilingual branch (en vs he) is deterministic regardless of the test host's locale.
/// </summary>
public class DurationFormatTests
{
    private static T WithCulture<T>(string culture, Func<T> f)
    {
        var prev = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = new CultureInfo(culture); return f(); }
        finally { CultureInfo.CurrentUICulture = prev; }
    }

    [Theory]
    [InlineData(90, "1h 30m")]
    [InlineData(60, "1h")]
    [InlineData(45, "45m")]
    [InlineData(0, "0m")]
    [InlineData(480, "8h")]
    [InlineData(125, "2h 5m")]
    public void FormatMinutes_English(int minutes, string expected)
        => WithCulture("en-US", () => DurationFormat.FormatMinutes(minutes)).Should().Be(expected);

    [Theory]
    [InlineData(90, "1ש׳ 30ד׳")]
    [InlineData(60, "1ש׳")]
    [InlineData(45, "45ד׳")]
    [InlineData(0, "0ד׳")]
    public void FormatMinutes_Hebrew(int minutes, string expected)
        => WithCulture("he-IL", () => DurationFormat.FormatMinutes(minutes)).Should().Be(expected);

    [Theory]
    [InlineData(750, "12.5h")]
    [InlineData(480, "8h")]
    [InlineData(720, "12h")]
    [InlineData(90, "1.5h")]
    public void FormatHours_English(int minutes, string expected)
        => WithCulture("en-US", () => DurationFormat.FormatHours(minutes)).Should().Be(expected);

    [Theory]
    [InlineData(750, "12.5ש׳")]
    [InlineData(480, "8ש׳")]
    public void FormatHours_Hebrew(int minutes, string expected)
        => WithCulture("he-IL", () => DurationFormat.FormatHours(minutes)).Should().Be(expected);
}
```

- [ ] **Step 2: Run it to verify it FAILS** (type doesn't exist)

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~DurationFormatTests" -- xUnit.ParallelizeTestCollections=false`
Expected: build error — `DurationFormat` is not defined.

- [ ] **Step 3: Create `Services/DurationFormat.cs`**

```csharp
using System.Globalization;

namespace ShiftManager.Services;

/// <summary>
/// Single, shared formatter for chore/shift durations in minutes. Bilingual via
/// <see cref="CultureInfo.CurrentUICulture"/> (Hebrew = he/he-IL). Used by admin weight inputs, the
/// chores calendar, and the Justice/Analytics fairness display. Defined ONCE here so the unit symbols
/// never drift across phases.
/// </summary>
public static class DurationFormat
{
    private static bool IsHebrew
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("he", StringComparison.OrdinalIgnoreCase);

    /// <summary>"1h 30m" / "1ש׳ 30ד׳". Whole hours drop the minutes; sub-hour drops the hours; 0 → "0m"/"0ד׳".</summary>
    public static string FormatMinutes(int totalMinutes)
    {
        if (totalMinutes < 0) totalMinutes = 0;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        var (h, m) = IsHebrew ? ("ש׳", "ד׳") : ("h", "m");

        if (hours > 0 && minutes > 0)
            return $"{hours}{h} {minutes}{m}";
        if (hours > 0)
            return $"{hours}{h}";
        return $"{minutes}{m}";
    }

    /// <summary>"12.5h" / "12.5ש׳". Trailing ".0" is dropped (12h, not 12.0h). Invariant decimal point.</summary>
    public static string FormatHours(int totalMinutes)
    {
        if (totalMinutes < 0) totalMinutes = 0;
        var hours = totalMinutes / 60.0;
        // Round to 1 decimal; invariant '.' so the symbol reads the same in both languages.
        var text = hours.ToString("0.#", CultureInfo.InvariantCulture);
        return IsHebrew ? $"{text}ש׳" : $"{text}h";
    }
}
```

- [ ] **Step 4: Run it to verify it PASSES**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~DurationFormatTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS (18 cases). If a Hebrew case fails with English symbols, the host culture override didn't take — confirm `TwoLetterISOLanguageName` reads `he` for `he-IL`.

- [ ] **Step 5: Commit**

```bash
git add Services/DurationFormat.cs ShiftManager.Tests/UnitTests/Services/DurationFormatTests.cs
git commit -m "feat(chores): shared bilingual DurationFormat (FormatMinutes/FormatHours) + tests"
```

---

## Task 7: DI registration + full-suite build/regression

**Files:**
- Modify: `Program.cs` (~line 297, beside `IChoreService`; ~line 336, beside `IShiftCategoryService`)

- [ ] **Step 1: Register the three new scoped services.** In `Program.cs`, add the evaluator next to the chore service registration (after line 297 `builder.Services.AddScoped<IChoreService, ChoreService>();`):

```csharp
builder.Services.AddScoped<IEligibilityEvaluator, EligibilityEvaluator>();
builder.Services.AddScoped<IChoreEligibilityAdminService, ChoreEligibilityAdminService>();
```

And add the chore category service next to its shift sibling (after line 336 `builder.Services.AddScoped<IShiftCategoryService, ShiftCategoryService>();`):

```csharp
builder.Services.AddScoped<IChoreCategoryService, ChoreCategoryService>();
```

> `IEligibilityEvaluator` is `Scoped` for consistency (it has no state — `Singleton` would also be correct — but Scoped matches the surrounding registrations and the constructor-injection into the scoped `BusyService`/`ChoreService` works either way).

- [ ] **Step 2: Build the whole solution** (this is the first point the solution-wide build must be green — `BusyService`/`ChoreService` ctors gained a required `IEligibilityEvaluator` param, now satisfiable by DI):

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded, 0 errors. If "Unable to resolve service for type 'IEligibilityEvaluator'" appears at app start, a registration is missing.

- [ ] **Step 3: Run the entire suite sequentially** to confirm nothing regressed (esp. existing `BusyService`/`ChoreService` consumers and any test that constructs `BusyService` directly — those call sites need the new ctor arg):

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -- xUnit.ParallelizeTestCollections=false`
Expected: all green. If a pre-existing test fails with "no constructor takes N arguments", it constructs `BusyService`/`ChoreService` directly and must be updated to pass `new EligibilityEvaluator()` (and any other new arg). Update those call sites — do NOT add a second parameterless ctor (that would let production code skip the evaluator).

- [ ] **Step 4: Commit**

```bash
git add Program.cs
git commit -m "feat(chores): register IEligibilityEvaluator + IChoreCategoryService + IChoreEligibilityAdminService (DI)"
```

---

## Self-Review (completed by author)

**Spec coverage (Phase 2 scope, spec §5-6, §11 Phase 2):**
- `IEligibilityEvaluator`/`EligibilityEvaluator` (pure, gender/officer/exempt) — Task 1. ✅ (spec §5)
- `BusyService.ValidateChoreAsync` eligibility wiring — officer/exempt HARD after `USER_NOT_IN_MOLECULE`, gender WARNING in the override-clearable block, free-text bypass — Task 2. ✅ (spec §5 "Where gates apply", §13.3)
- `IChoreCategoryService`/`ChoreCategoryService` (CRUD + type assign + membership) cloned from `ShiftCategoryService` — Task 3. ✅ (spec §6 "New services", D1)
- `IChoreEligibilityAdminService`/`ChoreEligibilityAdminService` (rule replace-semantics + exemption CRUD) — Task 4. ✅ (spec §6, §7.1/§7.2)
- `Chore.WeightMinutes` frozen at create in ALL three direct create-paths (`ChoreService.CreateChoreAsync`, `ReplaceShiftWithChoreAsync`, `ChoreApiService.CreateChoreAsync`); `QuickAddChore` + `ChoresController` delegate to those — Task 5. ✅ (spec §6, D4, §13.6). `DEFAULT_CHORE_WEIGHT_MINUTES = 480` single-defined on `ChoreService`.
- `StampTemplateAsync(templateId, from, to, weekdays, assigneeIds, rotate)` returning `StampResult{Created[],Skipped[]}` (rotate vs fan-out; skip-on-hard-error; one-per-day) — Task 5. ✅ (spec §6, D10, §13.1)
- `GetEligibilityForCandidateAsync(userId, choreTypeId)` for the picker — Task 5. ✅ (spec §6)
- `DurationFormat.FormatMinutes/FormatHours` single shared definition — Task 6. ✅ (spec §6, §7.6)
- DI registration of the three new services — Task 7. ✅ (spec §6 "DI")
- **No grant changes** — confirmed: zero edits to `GrantTypeSeed`/`RoleTemplateSeed`/`RoleTemplateAutoGrantTests`. ✅ (spec §9, D7)

**Placeholder scan:** No "TODO"/"similar to Task N"/"add validation" placeholders. Every code step is complete. The ONE explicitly-flagged non-verbatim area is the `TestSupport` collaborator stubs (Task 5 Step 7) for `IGrantService`/`ITenantResolver`/`IDirectorService`/`ICompanyCacheService` — these interfaces are large and out of Phase 2's scope, so the plan instructs the implementer to read each interface and implement every member permissively rather than guessing the member list. This is a deliberate, called-out instruction, not a silent gap.

**Type consistency:** `EligibilityViolation`/`EligibilityResult`/`IEligibilityEvaluator` live in `ShiftManager.Services` (same namespace as `BusyService`/`ChoreService`/`IChoreService`) → no cross-namespace imports needed for the wiring in Tasks 2 & 5. `EligibilityRule.GenderValue` is `Gender?` (matches Phase 1); the evaluator guards `HasValue` before comparing. `ChoreType.ChoreCategoryId` is `int?` throughout (CRUD assign in Task 3, FK SetNull asserted in tests). `Chore.WeightMinutes` is non-null `int`, frozen — the same `DEFAULT_CHORE_WEIGHT_MINUTES` constant referenced from `ChoreService` (definition), `ChoreApiService` (`ChoreService.DEFAULT_CHORE_WEIGHT_MINUTES`), and the weight test. `StampResult`/`StampCreated`/`StampSkipped` are `sealed record`s in `ShiftManager.Services` (`IChoreService.cs`). `BusyService`/`ChoreService` both gain exactly one ctor arg (`IEligibilityEvaluator`), appended last, satisfied by DI in Task 7.

**Severity-mapping correctness:** the evaluator is severity-agnostic (Task 1 tests assert violations only); BusyService (Task 2) maps `RequiresOfficerRank`/`Exempt` → `ValidationIssue(Error)` (hard, short-circuits before the token-clear) and `RequiresGender` → `ValidationIssue(Warning)` (reaches the existing `warnings.Clear()` token path). Tasks 2 tests prove tokens clear gender but NOT officer/exempt — exactly the binding decision.

**Known dependency for later phases (flagged):** Phase 3 (admin UI), Phase 4 (calendar roster + picker), Phase 5 (Justice weighting) consume the contract returned at the end of this plan. `DurationFormat` and `GetEligibilityForCandidateAsync` exist specifically for those phases. The template-times-→-weight gap (Deferred Items) only affects Phase 3's stamp UI if the user later wants template windows to drive frozen weight.

---

## Deferred Items

- **Template `StartTime`/`EndTime`/`WeightMinutesOverride` do NOT drive the stamped chore's frozen `WeightMinutes`.** `StampTemplateAsync` routes through `CreateChoreAsync`, which (matching the manual untimed flow) resolves weight from the chore type's `DefaultWeightMinutes` or the 480 fallback — it does not see the template's explicit window/override. **Why:** honoring template times in the frozen weight requires adding `startTime`/`endTime` params to `CreateChoreAsync`, a signature change touching every caller — out of this phase's scope. **Flagged for the user:** if template windows must drive weight now, that signature change is a small follow-up; otherwise stamped chores get a correct type-default/480 weight. (Called out inline in Task 5 Step 5.)
- **Resx Hebrew wording** for the 5 new keys (Task 2 Step 1) is a first-pass to be confirmed by a `localization-qa` pass per spec §13.8 — the KEYS are the contract, the exact Hebrew is not load-bearing.
- **`TestSupport` collaborator stubs** (Task 5 Step 7) for `IGrantService`/`ITenantResolver`/`IDirectorService`/`ICompanyCacheService` are skeletoned, not verbatim — the implementer must read each interface and implement every member permissively. (Deliberate: those interfaces are large and out of Phase 2's scope.)
