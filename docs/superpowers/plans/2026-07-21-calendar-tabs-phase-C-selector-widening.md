# Calendar Tabs — Phase C: Selector Baseline Widening (the ORIGIN-BUG FIX)

- **Date:** 2026-07-21
- **Branch:** `feat/calendar-tabs-selectors`
- **Spec:** `docs/superpowers/specs/2026-07-21-calendar-tabs-selectors-and-desync-design.md` (§1 D1/D6, §2.4, §6, §11 PF2/PF12/PF14)
- **Checklist (definition of done for this phase):** `docs/superpowers/specs/2026-07-21-calendar-tabs-perfect-state-checklist.md` — **ORG-1..8, REG-1, REG-3**

---

## Goal

Fix the origin bug: a Lead in company *City* cannot pick a trainee (or an assignee) from company *Tzafona* even though both are in the **same molecule** and the **same job type**, and the shifts calendar is molecule-scoped. The three assignment selectors (bottom-sheet, quick-entry, trainee picker) are scoped **narrower than the molecule**. Phase C widens all three selectors' candidate universe to **molecule + job type**, and verifies the save paths don't independently hard-block a cross-company-within-molecule pick (which would reproduce the bug as a pick-then-reject).

**Phase C does NOT introduce tabs, prioritization, or the warning UX** — those are Phases D/E. It only widens the candidate universe and closes the save-path gaps.

## Architecture

The bottom-sheet and quick-entry selectors both call one endpoint → one router → two leaves:

```
calendar-bottom-sheet.js / calendar-quick-entry.js
  └─> GET /Api/Calendar/GetEligibleUsersForShift  (Pages/Api/Calendar/GetEligibleUsersForShift.cshtml.cs)
        └─> IShiftCandidateService.GetEligibleCandidatesAsync   (Services/ShiftCandidateService.cs)
              reads FF_CATEGORY_BASED_SHIFT_ELIGIBILITY, HOME/OFFLINE + null-category fork, then dispatches:
              ├─ Workforce → ShiftAssignmentService.GetEligibleUsersForShiftTypeAsync   ← PF2 target
              └─ Tech      → ShiftCalendarService.GetEligibleUsersForShiftTypeAsync      ← PF2 (Tech) target
```

The trainee picker is server-rendered on page load (not the endpoint):

```
Pages/Calendar/Shifts.cshtml.cs:356  Trainees = _traineeService.GetCompanyTraineesAsync(companyId)   ← PF12 target
Pages/Assignments/Manage.cshtml.cs:160 (same call)                                                     ← PF12 target
```

Save paths that must not hard-block a cross-company-within-molecule pick:

```
Shift assign  : Table.OnPostAssignEmployeeAsync → ShiftAssignmentService.AssignShiftAsync
                  → ValidateShiftAssignmentAsync → BusyService  (grouping/jobtype = WARNING, overridable)   ← PF14
Trainee (cal) : Table.OnPostAddTrainee → ShiftAssignmentService.ValidateTraineeAssignmentAsync
                  → ValidateTraineeCoreAsync                    (same-molecule cross-company = NO warning)   ← PF14 (confirm)
Trainee (Manage): Manage.OnPost… → TraineeService.AssignTraineeToShiftAsync
                  → TraineeService.ValidateTraineeAssignmentAsync:238  (company != company → HARD BLOCK)     ← PF14 (relax)
```

**Key design decision (widening is unconditional).** The candidate `companyIds` set becomes **all molecule companies in every branch**, not only the two fallback branches named in PF2. Checklist **ORG-2** requires users from companies *outside every ShiftGrouping* to appear; that is only satisfiable by widening the main grouping branch too. `ShiftGrouping` keeps its two other roles — calendar row-banding and the `IsInShiftGrouping` DTO flag (computed separately from `groupingCompanyIdsSet`) — so **REG-1** holds: grouping no longer restricts *eligibility*, only *display*.

**Participant filter is untouched (PF2 / ORG-4).** Phase C widens the **company set only**. The two participant branches inside `GetEligibleUsersForShiftTypeAsync` are preserved exactly: `categoryFilter == true` → DoesShifts + single-category (job type NOT applied); `categoryFilter == false` → legacy job-type filter. The flag (`FF_CATEGORY_BASED_SHIFT_ELIGIBILITY`) is resolved upstream in `ShiftCandidateService`; this phase does not change how it maps to a participant filter.

## Tech Stack

- ASP.NET Core 8.0, Razor Pages, EF Core + **SQLite** (real `:memory:` fixture, `Foreign Keys=False`), xUnit + FluentAssertions + Moq.
- Cross-tenant reads use `IgnoreQueryFilters()` + a `SECURITY-AUDITED` comment; molecule access is gated by the calling endpoint/page before the service runs (IDOR).

## Global Constraints

- **Tests are serialized.** Parallel runs produce spurious `:memory:` failures. Always:
  `dotnet test -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
  Single-class filter form: `dotnet test --filter FullyQualifiedName~ShiftAssignmentServiceTests -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
- **Build in a git worktree with `-c Release`** to dodge the running :5000 Debug-exe file lock. Do not rebuild while the app is running and locking the exe.
- After any build/restore: **`git checkout -- packages.lock.json`** (the build mutates it; it must stay clean for publish).
- Real-SQLite fixtures only — never `UseInMemoryDatabase` (it hides the EF→SQLite translation bug class).
- Branch: `feat/calendar-tabs-selectors`. One commit per task (red → green → commit). Do NOT touch `FinalProductPublish/` (generated).
- No new grants, no schema changes, no migration in Phase C. (Trainee `JobTypeId` **backfill** is Phase D / PF6; Phase C's null-job-type inclusion is the safety net that covers ORG-8 without it — see Task 4.)

---

## Task 1 — PF2: widen workforce eligibility to the whole molecule

Make `ShiftAssignmentService.GetEligibleUsersForShiftTypeAsync` return candidates from **all molecule companies unconditionally**, keeping the participant filters and the `IsInShiftGrouping` flag.

**Files**
- `Services/ShiftAssignmentService.cs` (`GetEligibleUsersForShiftTypeAsync`, ~:63-222)
- `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/ShiftAssignmentServiceTests.cs` (modify one test, add one)

**Interfaces** — signature unchanged:
```csharp
Task<List<EligibleUserDto>> GetEligibleUsersForShiftTypeAsync(
    int shiftTypeId, int? jobTypeId = null, int? shiftGroupingId = null, bool categoryFilter = false);
```

**Steps**

- [ ] **Rewrite the existing grouping test to the new expected behavior (RED).** In `ShiftAssignmentServiceTests.cs`, replace `GetEligibleUsersForShiftType_WithShiftGrouping_ReturnsOnlyGroupingCompanies` (~:202-235) with:
  ```csharp
  [Fact]
  public async Task GetEligibleUsersForShiftType_OutsideGroupingCompany_NowMoleculeWide_ButFlaggedNotInGrouping()
  {
      // Arrange
      var hierarchy = await SetupTestHierarchyAsync();

      // Company inside the molecule but NOT in the shift grouping.
      var company3 = new Company { MoleculeId = hierarchy.Molecule.Id, Name = "Company3" };
      _db.Companies.Add(company3);
      await _db.SaveChangesAsync();

      var outsideUser = new AppUser
      {
          CompanyId = company3.Id,
          JobTypeId = hierarchy.JobTypes["Alhut"].Id,
          Email = "outside@test.com",
          DisplayName = "Outside User",
          PasswordHash = Array.Empty<byte>(),
          PasswordSalt = Array.Empty<byte>()
      };
      _db.Users.Add(outsideUser);
      await _db.SaveChangesAsync();

      // Act
      var result = await _service.GetEligibleUsersForShiftTypeAsync(
          hierarchy.ShiftType.Id,
          hierarchy.JobTypes["Alhut"].Id,
          hierarchy.ShiftGrouping.Id);

      // Assert — PF2/D1: molecule-wide eligibility (origin-bug fix)…
      result.Should().Contain(u => u.UserId == outsideUser.Id,
          "a same-molecule + same-jobtype user is now eligible even outside the grouping");
      // …but REG-1: the grouping still drives the display flag.
      result.Single(u => u.UserId == outsideUser.Id).IsInShiftGrouping.Should().BeFalse();
      result.Single(u => u.UserId == hierarchy.Users["alhut1"].Id).IsInShiftGrouping.Should().BeTrue();
  }
  ```

- [ ] **Add the single-company-branch test (RED).** This covers the `shiftType.CompanyId` narrowing (`:104-106`), i.e. checklist ORG-2's "shift type whose own `CompanyId` is set":
  ```csharp
  [Fact]
  public async Task GetEligibleUsersForShiftType_SingleCompanyShiftType_ReturnsMoleculeWide()
  {
      // Arrange
      var hierarchy = await SetupTestHierarchyAsync();

      // A shift type with NO grouping but a concrete CompanyId (company1) — the :104-106 branch.
      var cityShiftType = new ShiftType
      {
          Scope = ShiftManager.Models.Support.ShiftScope.Molecule,
          MoleculeId = hierarchy.Molecule.Id,
          CompanyId = hierarchy.Companies[0].Id,   // "City"
          JobTypeId = hierarchy.JobTypes["Alhut"].Id,
          ShiftGroupingId = null,
          Key = ShiftType.KEY_MORNING,
          Start = new TimeOnly(8, 0),
          End = new TimeOnly(16, 0)
      };
      _db.ShiftTypes.Add(cityShiftType);
      await _db.SaveChangesAsync();

      // Act — alhut2 lives in company2 ("Tzafona"), same molecule + same jobtype.
      var result = await _service.GetEligibleUsersForShiftTypeAsync(
          cityShiftType.Id, hierarchy.JobTypes["Alhut"].Id, null);

      // Assert — the Tzafona user is eligible on a City-owned shift type.
      result.Should().Contain(u => u.UserId == hierarchy.Users["alhut2"].Id);
  }
  ```

- [ ] **Run both (expect FAIL):**
  `dotnet test --filter "FullyQualifiedName~ShiftAssignmentServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
  Expect the two new assertions to fail (outsideUser / alhut2 not returned — code still narrows to grouping / single company).

- [ ] **Implement the widening.** In `Services/ShiftAssignmentService.cs`, keep `effectiveGroupingId`/`effectiveJobTypeId` (:79-80). Replace the entire company-resolution block (the `List<int> companyIds; if (effectiveGroupingId.HasValue) { … } else { … }`, ~:82-107) with an unconditional molecule-wide query:
  ```csharp
  // PF2 (D1): the candidate universe is the WHOLE molecule, unconditionally. The origin bug was that
  // ShiftGrouping / single-company narrowing hid same-molecule + same-jobtype users (a City lead could
  // not see a Tzafona user). ShiftGroupings keep their OTHER roles — calendar row-banding and the
  // IsInShiftGrouping flag below — but no longer restrict who is ELIGIBLE.
  // SECURITY-AUDITED: SAFE — molecule-scoped (shiftType.MoleculeId); the calling endpoint gates molecule
  // access (ValidateScopeAccessAsync + shiftType-belongs-to-molecule) before this runs. IgnoreQueryFilters
  // is required so molecule companies outside the caller's own tenant are included (the whole point).
  var companyIds = await _db.Companies
      .IgnoreQueryFilters()
      .Where(c => c.MoleculeId == shiftType.MoleculeId)
      .Select(c => c.Id)
      .ToListAsync();
  ```
  Leave everything below unchanged: the `categoryFilter` branch (:110-147), the legacy job-type branch (:148-158), the `users` projection (:160-171), `groupingCompanyIdsSet` (:205-211), and the `IsInShiftGrouping` computation (:218 — `!effectiveGroupingId.HasValue || groupingCompanyIdsSet.Contains(u.CompanyId)`). Only the company set widens.

- [ ] **Run the whole test class (expect PASS):**
  `dotnet test --filter "FullyQualifiedName~ShiftAssignmentServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
  Confirm the two new tests pass **and** `GetEligibleUsersForShiftType_WithJobTypeFilter_ReturnsOnlyMatchingJobType` still passes (base fixture has both companies in the grouping, so its count of 2 is unchanged).

- [ ] **Run the eligibility-leaf + router suites (expect PASS, no regressions):**
  `dotnet test --filter "FullyQualifiedName~CategoryEligibilityLeafTests|FullyQualifiedName~ShiftCandidateServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
  These molecules are single-company or both-companies-in-grouping, so molecule-wide == their old company set; counts are unchanged.

- [ ] **Commit:** `git commit -am "PF2: widen workforce shift eligibility to molecule-wide (origin-bug fix)"`

**Covers:** ORG-2, ORG-3 (same endpoint feeds quick-entry), ORG-4 (participant filter untouched), ORG-7 (distinct-by-Id projection → multi-company user listed once), REG-1, PF2 (workforce).

---

## Task 2 — PF2 (Tech): confirm + guard the Tech eligibility company scope

The Tech leaf (`ShiftCalendarService.GetEligibleUsersForShiftTypeAsync`) is **already molecule-wide at its base** (`_db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId)`, :253-254). Its only company narrowing is the shift type's **explicit** `EligibleCompanyIds` list plus the officer-rank filter — intentional eligibility rules, not the incidental grouping/single-company narrowing that caused the origin bug. Per checklist **ORG-5**, `EligibleCompanyIds` + officer rank **must still apply**. So: **no company-scope change to the Tech path**; add a guard test proving the molecule-wide base and documenting the decision. Tabs/prioritization don't apply to null-job-type Tech.

**Files**
- `Services/ShiftCalendarService.cs` — **no code change** (add a one-line comment noting the decision above the method, optional).
- `ShiftManager.Tests/UnitTests/Services/ShiftCalendarServiceTests.cs` (add one guard test)

**Steps**

- [ ] **Add a guard test (RED only if the base ever narrows).** In `ShiftCalendarServiceTests.cs`, mirror the existing fixture used by `GetEligibleUsersForShiftTypeAsync_ReturnsAllUsers_WhenNoEligibilityFilters` (:375-383). Add a Tech molecule with **two** companies and a shift type with **no** `EligibleCompanyIds`:
  ```csharp
  [Fact]
  public async Task GetEligibleUsersForShiftType_TechNoEligibilityList_ReturnsAllMoleculeCompanies()
  {
      // Arrange: Tech molecule, two companies, one user each, a shift type with no company eligibility list.
      var (moleculeId, shiftTypeId, userA, userB) = await SeedTechTwoCompanyMoleculeAsync();

      // Act
      var result = await _service.GetEligibleUsersForShiftTypeAsync(moleculeId, shiftTypeId);

      // Assert — molecule-wide base is preserved across companies (ORG-5).
      result.Select(u => u.Id).Should().Contain(new[] { userA.Id, userB.Id });
  }
  ```
  Implement `SeedTechTwoCompanyMoleculeAsync` locally in the test class following the file's existing seed style (Project → Area → `Molecule { Type = MoleculeType.Tech }` → two `Company` rows → two `AppUser` rows → one `ShiftType { Scope = Molecule, MoleculeId, TechShiftType = ShiftType.TECH_HANAVA, RequiresOfficerRank = false }` with **no** eligible-company list).

- [ ] **Run (expect PASS immediately — this is a guard, not a change):**
  `dotnet test --filter "FullyQualifiedName~ShiftCalendarServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`

- [ ] **Commit:** `git commit -am "PF2 (Tech): guard molecule-wide base of Tech eligibility; EligibleCompanyIds/rank intentional (ORG-5)"`

**Covers:** ORG-5, PF2 (Tech reconciliation — documented "why no change": the Tech base is already molecule-wide; its remaining company filter is deliberate config, and tabs/jobtype context don't apply to null-jobtype Tech).

---

## Task 3 — PF14: prove the shift-assign save path allows cross-company-within-molecule

`AssignShiftAsync` does **not** hard-block on ShiftGrouping/company/job type. The existing tests already prove those are **warnings**: `ValidateShiftAssignment_UserNotInShiftGrouping_ReturnsWarning` (:293-334) and `ValidateShiftAssignment_WithJobTypeMismatch_ReturnsInvalid` (:238-263) both assert `CanAssign == true`. `AssignShiftAsync` (:353-477) blocks only on hard errors; warnings require a valid **override token** (:374-391) — the existing calendar override UX, not a new block. Add a test proving a City-owned shift + a Tzafona same-jobtype user **saves**.

**Files**
- `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/ShiftAssignmentServiceTests.cs` (add one)

**Steps**

- [ ] **Add the save-path test (RED until it compiles/runs green — it should pass on current code; it is a regression lock proving Phase C doesn't break it and documents the intended behavior).** Use a molecule-wide (no-grouping) Alhut shift so no `NOT_IN_SHIFT_GROUPING` warning fires and the save is clean (no override token needed):
  ```csharp
  [Fact]
  public async Task AssignShift_CrossCompanyWithinMolecule_SameJobType_Succeeds()
  {
      // Arrange
      var hierarchy = await SetupTestHierarchyAsync();

      // City-owned shift type, no grouping, Alhut jobtype (no grouping ⇒ no NOT_IN_SHIFT_GROUPING warning).
      var cityShift = new ShiftType
      {
          Scope = ShiftManager.Models.Support.ShiftScope.Molecule,
          MoleculeId = hierarchy.Molecule.Id,
          CompanyId = hierarchy.Companies[0].Id,   // City
          JobTypeId = hierarchy.JobTypes["Alhut"].Id,
          ShiftGroupingId = null,
          Key = ShiftType.KEY_MORNING,
          Start = new TimeOnly(8, 0),
          End = new TimeOnly(16, 0),
          StaffingRequired = 2
      };
      _db.ShiftTypes.Add(cityShift);
      await _db.SaveChangesAsync();

      var instance = new ShiftInstance
      {
          CompanyId = hierarchy.Companies[0].Id,   // City-owned instance
          ShiftTypeId = cityShift.Id,
          WorkDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
          StaffingRequired = 2,
          Name = "City Morning"
      };
      _db.ShiftInstances.Add(instance);
      await _db.SaveChangesAsync();

      // Act — assign the Tzafona (company2) Alhut user onto the City-owned instance.
      var result = await _service.AssignShiftAsync(
          userId: hierarchy.Users["alhut2"].Id,
          shiftInstanceId: instance.Id,
          assignedByUserId: hierarchy.Users["alhut1"].Id);

      // Assert — no hard block; the cross-company-within-molecule assignment persists.
      result.Success.Should().BeTrue(result.ErrorKey);
      result.AssignmentId.Should().NotBeNull();
  }
  ```

- [ ] **Run (expect PASS):**
  `dotnet test --filter "FullyQualifiedName~ShiftAssignmentServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
  If it fails on `WARNINGS_REQUIRE_OVERRIDE`, that means a warning fired (e.g. `StaffingRequired`/rest) — the plan's assumption (no-grouping ⇒ clean save) is wrong; investigate the warning source before adjusting (do not paper over with an override token unless the warning is genuinely expected, in which case supply `ValidateOverrideToken`-minted token and assert success).

- [ ] **Commit:** `git commit -am "PF14: regression lock — cross-company-within-molecule shift assign succeeds (no hard block)"`

**Covers:** ORG-2 (the *save* half), PF14 (shift-assign path). Documents that the calendar trainee path (`ShiftAssignmentService.ValidateTraineeCoreAsync`, :287-351) is a molecule **warning** — same-molecule cross-company trainee produces **no** issue at all (`TRAINEE_DIFFERENT_MOLECULE` fires only when the trainee is in a *different* molecule), so ORG-1's calendar save is clean without any change here.

---

## Task 4 — PF12: widen the trainee selector to molecule + job type

Replace the company-scoped `GetCompanyTraineesAsync(int companyId)` with a molecule + job-type method, and repoint both callers and the four tests. Null-job-type trainees are included as a universal-relevance safety net (D6 / UD4) — this is also what makes **ORG-8** pass without the Phase-D `JobTypeId` backfill.

**Files**
- `Services/ITraineeService.cs` (:41 — replace the method)
- `Services/TraineeService.cs` (:461-467 — replace the method)
- `Pages/Calendar/Shifts.cshtml.cs` (:356 — caller)
- `Pages/Assignments/Manage.cshtml.cs` (:160 — caller)
- `ShiftManager.Tests/UnitTests/Services/TraineeServiceTests.cs` (:594-654 — repoint 4 tests + add 2)

**Interfaces** — explicit signature change (compile-time break forces every caller to update; not a silent behavior change):
```csharp
// ITraineeService.cs — replaces: Task<List<AppUser>> GetCompanyTraineesAsync(int companyId);
Task<List<Models.AppUser>> GetMoleculeTraineesAsync(int moleculeId, int? jobTypeId);
```

**Steps**

- [ ] **Repoint + extend the 4 existing tests (RED — won't compile).** In `TraineeServiceTests.cs`, the current 4 tests (:596-654) assume a bare `CompanyId==1` filter with no hierarchy. The new method joins `Company.MoleculeId`, so add a tiny local seed and rewrite:
  ```csharp
  // Seeds one molecule with two companies; returns (moleculeId, companyA, companyB).
  private async Task<(int MoleculeId, int CompanyA, int CompanyB)> SeedMoleculeTwoCompaniesAsync()
  {
      var project = new Project { Name = "P", DisplayName = "P" };
      _db.Projects.Add(project); await _db.SaveChangesAsync();
      var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
      _db.Areas.Add(area); await _db.SaveChangesAsync();
      var molecule = new Molecule { AreaId = area.Id, Name = "M", Type = MoleculeType.Workforce };
      _db.Molecules.Add(molecule); await _db.SaveChangesAsync();
      var cA = new Company { Id = CompanyId, MoleculeId = molecule.Id, Name = "CA", DisplayName = "CA" };
      var cB = new Company { Id = 2, MoleculeId = molecule.Id, Name = "CB", DisplayName = "CB" };
      _db.Companies.AddRange(cA, cB); await _db.SaveChangesAsync();
      return (molecule.Id, cA.Id, cB.Id);
  }

  private async Task<AppUser> SeedTraineeAsync(int id, int companyId, int? jobTypeId, string name)
  {
      var u = new AppUser
      {
          Id = id, CompanyId = companyId, JobTypeId = jobTypeId,
          Email = $"t{id}@test.com", DisplayName = name, Role = UserRole.Trainee, IsActive = true
      };
      _db.Users.Add(u); await _db.SaveChangesAsync();
      return u;
  }

  [Fact]
  public async Task GetMoleculeTrainees_ReturnsOnlyTraineeRole()
  {
      var (mol, cA, _) = await SeedMoleculeTwoCompaniesAsync();
      await SeedUserAsync(1, UserRole.Employee);           // CompanyId == cA
      await SeedTraineeAsync(2, cA, jobTypeId: null, "Trainee A");
      await SeedTraineeAsync(3, cA, jobTypeId: null, "Trainee B");
      await SeedUserAsync(4, UserRole.Manager);

      var result = await _service.GetMoleculeTraineesAsync(mol, jobTypeId: null);

      result.Should().OnlyContain(u => u.Role == UserRole.Trainee);
      result.Should().HaveCount(2);
  }

  [Fact]
  public async Task GetMoleculeTrainees_ReturnsAllMoleculeCompanies_ExcludesOtherMolecule()
  {
      var (mol, cA, cB) = await SeedMoleculeTwoCompaniesAsync();
      var inA = await SeedTraineeAsync(1, cA, jobTypeId: null, "In A");
      var inB = await SeedTraineeAsync(2, cB, jobTypeId: null, "In B");   // same molecule, other company

      // Trainee in a company that belongs to NO molecule row (foreign) — must be excluded.
      var foreign = await SeedTraineeAsync(3, companyId: 999, jobTypeId: null, "Foreign");

      var result = await _service.GetMoleculeTraineesAsync(mol, jobTypeId: null);

      result.Select(u => u.Id).Should().BeEquivalentTo(new[] { inA.Id, inB.Id });
      result.Should().NotContain(u => u.Id == foreign.Id);
  }

  [Fact]
  public async Task GetMoleculeTrainees_JobTypeScoped_IncludesMatchingAndNull_ExcludesOtherJobType()
  {
      var (mol, cA, cB) = await SeedMoleculeTwoCompaniesAsync();
      var alhut = 10; var other = 20;   // JobType ids; the method never joins JobType, only compares the fk
      var matching = await SeedTraineeAsync(1, cA, jobTypeId: alhut, "Matching");
      var nullJob  = await SeedTraineeAsync(2, cB, jobTypeId: null,  "Null Job");   // ORG-6 safety net
      var otherJob = await SeedTraineeAsync(3, cA, jobTypeId: other, "Other Job");

      var result = await _service.GetMoleculeTraineesAsync(mol, jobTypeId: alhut);

      result.Select(u => u.Id).Should().BeEquivalentTo(new[] { matching.Id, nullJob.Id });
      result.Should().NotContain(u => u.Id == otherJob.Id);
  }

  [Fact]
  public async Task GetMoleculeTrainees_NullJobTypeCalendar_ReturnsAllMoleculeTrainees()
  {
      // Tech / null-jobtype calendar context ⇒ no job-type restriction (ORG-5 parity for trainees).
      var (mol, cA, cB) = await SeedMoleculeTwoCompaniesAsync();
      var t1 = await SeedTraineeAsync(1, cA, jobTypeId: 10, "T1");
      var t2 = await SeedTraineeAsync(2, cB, jobTypeId: null, "T2");

      var result = await _service.GetMoleculeTraineesAsync(mol, jobTypeId: null);

      result.Select(u => u.Id).Should().BeEquivalentTo(new[] { t1.Id, t2.Id });
  }

  [Fact]
  public async Task GetMoleculeTrainees_ReturnsSortedByDisplayName()
  {
      var (mol, cA, cB) = await SeedMoleculeTwoCompaniesAsync();
      await SeedTraineeAsync(1, cA, jobTypeId: null, "Zara");
      await SeedTraineeAsync(2, cB, jobTypeId: null, "Alice");
      await SeedTraineeAsync(3, cA, jobTypeId: null, "Moe");

      var result = await _service.GetMoleculeTraineesAsync(mol, jobTypeId: null);

      result.Select(u => u.DisplayName).Should().ContainInOrder("Alice", "Moe", "Zara");
  }

  [Fact]
  public async Task GetMoleculeTrainees_EmptyMolecule_ReturnsEmpty()
  {
      var (mol, _, _) = await SeedMoleculeTwoCompaniesAsync();
      var result = await _service.GetMoleculeTraineesAsync(mol, jobTypeId: null);
      result.Should().BeEmpty();
  }
  ```
  Delete the four old `GetCompanyTrainees_*` tests (:596-654). (Add `using ShiftManager.Models;` is already present; `MoleculeType` lives in `ShiftManager.Models` / `Models.Support` — match the file's existing imports.)

- [ ] **Run (expect FAIL to compile):**
  `dotnet test --filter "FullyQualifiedName~TraineeServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
  Expect a compile error: `GetMoleculeTraineesAsync` does not exist.

- [ ] **Implement the new method.** In `ITraineeService.cs` replace the `GetCompanyTraineesAsync` declaration (:41) with the new signature above. In `Services/TraineeService.cs` replace `GetCompanyTraineesAsync` (:461-467) with:
  ```csharp
  public async Task<List<AppUser>> GetMoleculeTraineesAsync(int moleculeId, int? jobTypeId)
  {
      // PF12 (D1/D6): trainees are molecule-wide — the origin bug was a City lead unable to pick a Tzafona
      // trainee. Job-type scoped with a NULL-inclusion safety net: a trainee whose JobTypeId is null (mis-
      // seeded, or pre-backfill on an existing DB — ORG-8) is universally relevant so it is never hidden,
      // the exact origin-bug failure. When there is no job-type context (jobTypeId == null, e.g. Tech),
      // show every molecule trainee.
      // SECURITY-AUDITED: SAFE — molecule-scoped (Company.MoleculeId == moleculeId). The calling page gates
      // molecule access before this runs (IDOR); only trainees inside the requested molecule are returned.
      return await _db.Users
          .IgnoreQueryFilters()
          .Where(u => u.Role == UserRole.Trainee
                      && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId)
                      && (jobTypeId == null || u.JobTypeId == jobTypeId || u.JobTypeId == null))
          .OrderBy(u => u.DisplayName)
          .ToListAsync();
  }
  ```
  (The `_db.Companies.Any(...)` subquery inherits the top-level `IgnoreQueryFilters()` — the exact proven pattern from `ShiftCalendarService.GetEligibleUsersForShiftTypeAsync:253-254`, which translates cleanly on real SQLite.)

- [ ] **Update the calendar caller.** `Pages/Calendar/Shifts.cshtml.cs:356` — replace:
  ```csharp
  Trainees = await _traineeService.GetCompanyTraineesAsync(companyId);
  ```
  with (page already has `MoleculeId` (int?) and `JobTypeId` (int?)):
  ```csharp
  // Trainee picker is molecule + jobtype wide (PF12 / origin-bug fix), gated by the same molecule access
  // the calendar body already validated. Null jobtype (Tech) ⇒ every molecule trainee.
  Trainees = MoleculeId.HasValue
      ? await _traineeService.GetMoleculeTraineesAsync(MoleculeId.Value, JobTypeId)
      : new List<AppUser>();
  ```

- [ ] **Update the Manage caller.** `Pages/Assignments/Manage.cshtml.cs:160` — resolve the molecule from the shift's company and the job type from the shift type (`companyId` is set at :97 via `Type.GetEffectiveCompanyId(...)`; `Type` is the `ShiftType`):
  ```csharp
  // Trainee list is molecule + jobtype wide (PF12). Molecule resolved from the shift's company; page
  // access is already gated above (hasAccess). IgnoreQueryFilters: the company may be cross-tenant.
  var moleculeId = await _db.Companies.IgnoreQueryFilters()
      .Where(c => c.Id == companyId)
      .Select(c => (int?)c.MoleculeId)
      .FirstOrDefaultAsync();
  Trainees = moleculeId.HasValue
      ? await _traineeService.GetMoleculeTraineesAsync(moleculeId.Value, Type.JobTypeId)
      : new List<AppUser>();
  ```
  (Confirm `Type.JobTypeId` exists on the `ShiftType` bound as `Type`; if the page exposes the id differently, use the shift type's `JobTypeId`. Do not fall back to the old company-scoped list.)

- [ ] **Run the trainee tests (expect PASS):**
  `dotnet test --filter "FullyQualifiedName~TraineeServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`

- [ ] **Build the web project in the worktree (Release) to confirm both callers compile; then restore the lockfile:**
  `dotnet build ShiftManager.csproj -c Release` then `git checkout -- packages.lock.json`

- [ ] **Commit:** `git commit -am "PF12: widen trainee selector to molecule+jobtype (GetMoleculeTraineesAsync); update both callers"`

**Covers:** ORG-1 (trainee picker lists cross-company trainees), ORG-6 (null-jobtype trainee always listed), ORG-8 (null-inclusion safety net → pre-backfill trainees still appear), PF12. REG-3 needs Task 5 (save path) + the browser check in Task 6.

---

## Task 5 — PF14 (extended): relax the Manage trainee save path from a company hard-block to a molecule check

`TraineeService.ValidateTraineeAssignmentAsync` (:202-264) — the save path behind `AssignTraineeToShiftAsync` used by `/Assignments/Manage` (Manage.cshtml.cs:371) — **hard-blocks** on `trainee.CompanyId != assignment.CompanyId` (:238-241). With Task 4 widening the Manage picker to molecule-wide, that block reproduces the origin bug as a pick-then-reject on the Manage surface. Relax it to a **molecule membership** check (mirroring the calendar path `ShiftAssignmentService.ValidateTraineeCoreAsync:327-347`: same molecule → allowed; different molecule → blocked).

> This extends PF14 beyond the two methods it names (`AssignShiftAsync`, `ValidateTraineeCoreAsync`). It is required for correctness: PF12 explicitly widens the Manage caller, and leaving this hard-block would be a pick-then-reject bug (a "No-Workarounds" violation). Flagged for reviewer sign-off.

**Files**
- `Services/TraineeService.cs` (`ValidateTraineeAssignmentAsync`, :202-264)
- `ShiftManager.Tests/UnitTests/Services/TraineeServiceTests.cs` (add tests)

**Steps**

- [ ] **Add tests (RED).** In `TraineeServiceTests.cs`, seed a shadow across two companies of the same molecule and assert the assignment validates; and a different-molecule trainee still blocks:
  ```csharp
  [Fact]
  public async Task ValidateTraineeAssignment_CrossCompanySameMolecule_IsValid()
  {
      var (mol, cA, cB) = await SeedMoleculeTwoCompaniesAsync();
      var shiftType = CreateShiftType();               // MoleculeId = 1 == mol (fixture uses molecule id 1)
      await SeedShiftTypeAsync(shiftType);
      await SeedUserAsync(10, UserRole.Employee);       // primary, company cA
      await SeedTraineeAsync(20, cB, jobTypeId: null, "Cross Trainee");   // same molecule, other company

      var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);   // instance company == cA

      var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 20);

      isValid.Should().BeTrue(error);
  }

  [Fact]
  public async Task ValidateTraineeAssignment_DifferentMolecule_ReturnsInvalid()
  {
      var (mol, cA, _) = await SeedMoleculeTwoCompaniesAsync();
      // A second molecule + company, foreign to the shift's molecule.
      var area2 = _db.Areas.First();
      var mol2 = new Molecule { AreaId = area2.Id, Name = "M2", Type = MoleculeType.Workforce };
      _db.Molecules.Add(mol2); await _db.SaveChangesAsync();
      var cForeign = new Company { Id = 77, MoleculeId = mol2.Id, Name = "CF", DisplayName = "CF" };
      _db.Companies.Add(cForeign); await _db.SaveChangesAsync();

      var shiftType = CreateShiftType();
      await SeedShiftTypeAsync(shiftType);
      await SeedUserAsync(10, UserRole.Employee);
      await SeedTraineeAsync(30, cForeign.Id, jobTypeId: null, "Foreign Trainee");
      var (_, assignment) = await SeedShiftWithAssignmentAsync(employeeId: 10);

      var (isValid, error) = await _service.ValidateTraineeAssignmentAsync(assignment.Id, 30);

      isValid.Should().BeFalse();
      error.Should().Contain("Error_TraineeAssignment_DifferentCompany");   // reused localized key
  }
  ```
  (The fixture's `CreateShiftType` sets `MoleculeId = 1`, and `SeedMoleculeTwoCompaniesAsync` pins the fixture company to `CompanyId == 1` with `molecule.Id == 1` because it is the first molecule created — verify the molecule id is 1 in the fixture; if EF assigns a different id, resolve the shift type's molecule from the seeded `mol` instead of relying on the constant.)

- [ ] **Run (expect FAIL):** the cross-company test fails today (hard company block returns `DifferentCompany`).
  `dotnet test --filter "FullyQualifiedName~TraineeServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`

- [ ] **Implement the relaxation.** In `TraineeService.ValidateTraineeAssignmentAsync`, replace the company hard-block (:238-241):
  ```csharp
  if (trainee.CompanyId != assignment.CompanyId)
  {
      return (false, _localizer["Error_TraineeAssignment_DifferentCompany"].Value);
  }
  ```
  with a molecule membership check (allow same-molecule cross-company; block only different molecule), keyed off the shift type's molecule already loaded via `assignment.ShiftInstance.ShiftType`:
  ```csharp
  // PF14 (extended): trainees shadow cross-company WITHIN a molecule (mirrors ShiftAssignmentService
  // .ValidateTraineeCoreAsync). Only a different molecule blocks. Fall back to the company check when the
  // shift type has no molecule.
  var cellMoleculeId = assignment.ShiftInstance.ShiftType?.MoleculeId;
  if (cellMoleculeId is int moleculeId)
  {
      // SECURITY-AUDITED: SAFE — molecule membership probe scoped by the trainee's company + the cell molecule.
      var sameMolecule = await _db.Companies.IgnoreQueryFilters()
          .AnyAsync(c => c.Id == trainee.CompanyId && c.MoleculeId == moleculeId);
      if (!sameMolecule)
          return (false, _localizer["Error_TraineeAssignment_DifferentCompany"].Value);
  }
  else if (trainee.CompanyId != assignment.CompanyId)
  {
      return (false, _localizer["Error_TraineeAssignment_DifferentCompany"].Value);
  }
  ```
  (Reuses the existing `Error_TraineeAssignment_DifferentCompany` resx key — no new localization. `assignment.ShiftInstance.ShiftType` is already `Include`d at :204-206.)

- [ ] **Run (expect PASS):**
  `dotnet test --filter "FullyQualifiedName~TraineeServiceTests" -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`

- [ ] **Commit:** `git commit -am "PF14 (extended): relax Manage trainee save path to molecule check (unblocks widened picker)"`

**Covers:** PF14 (trainee save path), REG-3 (Manage picker works end-to-end after the contract change), ORG-1 parity on the Manage surface.

---

## Task 6 — Full serialized suite + checklist mapping

**Files** — none (verification only).

**Steps**

- [ ] **Full suite, serialized, in the Release worktree:**
  `dotnet test -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`
  All green. Then `git checkout -- packages.lock.json`.

- [ ] **Confirm the reshaped tests compile/pass (REG-5 subset for this phase):** `ShiftAssignmentServiceTests`, `ShiftCalendarServiceTests`, `ShiftCandidateServiceTests`, `CategoryEligibilityLeafTests`, `TraineeServiceTests`.

- [ ] **Browser E2E on the seeded app (Oren molecule / Alhut) — map each to a checklist item:**
  - **ORG-1** `[flag]` City lead → trainee picker on a City assignment lists Tzafona Alhut trainees **and** null-jobtype trainees; selecting one **saves** (calendar path is warning-free; Manage path relaxed).
  - **ORG-2** `[flag]` Bottom-sheet on an Alhut shift lists users from **all** Oren companies (incl. outside every ShiftGrouping **and** a shift type whose own `CompanyId` is set); cross-company pick saves.
  - **ORG-3** `[flag]` Quick-entry typing a Tzafona name lists them; Enter assigns.
  - **ORG-4** Flag ON → DoesShifts+category (jobtype NOT applied); flag OFF → jobtype matches. Sets differ; company set is molecule-wide in both.
  - **ORG-5** Tech molecule → Tech path (EligibleCompanyIds + officer rank) still applies, molecule-wide base, no tab prioritization.
  - **ORG-6** Trainee with `JobTypeId=null` listed in every trainee picker.
  - **ORG-7** Multi-company user listed exactly once.
  - **ORG-8** Existing dev DB (trainees seeded before any `JobTypeId` backfill) → still appear (null-inclusion safety net).
  - **REG-1** ShiftGroupings still band rows; `IsInShiftGrouping` display unchanged.
  - **REG-3** `/Assignments/Manage` trainee picker works (its tests + a browser check).
  Run flagged items under **both** `FF_CATEGORY_BASED_SHIFT_ELIGIBILITY` states. Build `-c Release` to avoid the Debug-exe lock; if the app is running and locks the exe, ask the user to stop it before rebuilding.

- [ ] **No commit** (verification task). If any item fails, return to the owning task; do not paper over.

**Covers:** ORG-1..8, REG-1, REG-3 (browser confirmation), REG-5 (serialized suite, lockfile restored).

---

## Self-review — checklist coverage

| Item | Covered by | Notes |
|------|-----------|-------|
| ORG-1 | Task 4 (picker) + Task 5 (Manage save) + Task 3 note (calendar save is warning-free) | calendar trainee path already a warning |
| ORG-2 | Task 1 (both branches widened + grouping branch) + Task 3 (save) | unconditional molecule-wide, ORG-2's "outside every grouping" satisfied |
| ORG-3 | Task 1 | quick-entry shares the endpoint/router |
| ORG-4 | Task 1 (participant filter untouched) + Task 6 (flag both states) | company set only widens |
| ORG-5 | Task 2 | Tech base already molecule-wide; EligibleCompanyIds/rank intentional |
| ORG-6 | Task 4 (`JobTypeId == null` inclusion) | |
| ORG-7 | Task 1 (distinct-by-Id projection) | |
| ORG-8 | Task 4 (null-inclusion safety net) | covered **without** the Phase-D `JobTypeId` backfill (PF6) |
| REG-1 | Task 1 (`groupingCompanyIdsSet` + `IsInShiftGrouping` kept) | grouping only affects display now |
| REG-3 | Task 4 + Task 5 + Task 6 browser check | |
| PF2 | Tasks 1 + 2 | |
| PF12 | Task 4 | |
| PF14 | Task 3 (shift) + Task 5 (trainee, extended) | |

**Out of scope for Phase C (correctly deferred to later phases):** tabs data model/service/page (PF6/PF10/PF11/PF13, Phase D), tab strip + prioritization + off-tab warning (§2.4, PF5/PF7/PF8, Phase E), Bug 2/Bug 4 (Phases A), searchable-select (Phase B), trainee `JobTypeId` **backfill/seed** (PF6, Phase D — ORG-8 is already met here via null-inclusion).

**One item beyond the literal spec, flagged:** Task 5 relaxes `TraineeService.ValidateTraineeAssignmentAsync` (the Manage save path), which PF14 did not name — required because PF12 widens the Manage caller and the existing company hard-block would otherwise pick-then-reject.
