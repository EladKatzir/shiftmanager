# Move User Between Companies — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Move to company" action on the Admin Users page that relocates a user to another company within the acting admin's scope (molecule admin → within molecule, area admin → within area, owner → within project), performing a transactional "clean transfer" of the user's operational footprint.

**Architecture:** A new scoped service `IUserCompanyTransferService` owns the transactional data work (clear/cancel old-company operational data, reset stale FKs, re-scope grants, migrate avatar, write audit). Two thin Razor handlers in a new partial `Pages/Admin/Users.cshtml.Move.cs` own authorization (reusing `EditCompanyUsers` on both sides + `CanAssignRoleAsync`) and the impact-preview/confirm UX. No new grant type.

**Tech Stack:** ASP.NET Core 8.0, EF Core (SQLite), Razor Pages, xUnit + FluentAssertions + Moq, real SQLite `:memory:` test fixtures.

**Spec:** `docs/superpowers/specs/2026-05-23-user-company-move-design.md`

**Conventions to honor (from CLAUDE.md / project MEMORY):**
- Every cross-tenant query uses `.IgnoreQueryFilters()` scoped to the specific `userId`/company, with a `// SECURITY-AUDITED:` comment.
- Broad async catches: `catch (Exception ex) when (ex is not OperationCanceledException)`.
- EF queries stay SQLite-translatable (no `ToLowerInvariant()`/`Contains(StringComparison)` inside `IQueryable`).
- New tests use real SQLite (`DataSource=:memory:;Foreign Keys=False`), NOT EF InMemory.
- New user-facing strings go in BOTH `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx`.
- Do NOT edit `FinalProductPublish/` (generated).

**"Today" for future-predicate:** use `DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);` consistently.

---

## File Structure

**Create:**
- `Services/IUserCompanyTransferService.cs` — interface + `MoveImpact` / `MoveResult` records.
- `Services/UserCompanyTransferService.cs` — implementation (the transactional clean transfer + read-only preview).
- `Pages/Admin/Users.cshtml.Move.cs` — `partial class UsersModel` with `OnGetMoveImpactAsync` + `OnPostMoveUserAsync`.
- `ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs` — unit tests.

**Modify:**
- `Services/IGrantService.cs` + `Services/GrantService.cs` — add public `BuildRoleTemplateScopeAsync` (promoted from the page's private copy).
- `Pages/Admin/Users.cshtml.cs:2457` — make the private `BuildGrantScopeForTemplateAsync` delegate to the new public method (DRY).
- `Services/IAuditLogService.cs` + `Services/AuditLogService.cs` — add explicit-companyId `LogUserActionAsync` overload.
- `Program.cs` (~line 322) — register `IUserCompanyTransferService`.
- `Pages/Admin/Users.cshtml` — add the "Move" action + modal + JS.
- `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx` — new strings.

> NOTE: confirm the test project path on first run. The fixtures live at `ShiftManager.Tests/Helpers/SqliteDbContextFixture.cs`; existing service tests at `ShiftManager.Tests/Services/*Tests.cs`. If the csproj is elsewhere, adjust the `dotnet test` path in every command below.

---

## Task 1: Service scaffolding (interface, DTOs, DI) — compiles, no logic

**Files:**
- Create: `Services/IUserCompanyTransferService.cs`
- Create: `Services/UserCompanyTransferService.cs`
- Modify: `Program.cs` (~line 322, after `IConcurrencyService`/`IWidgetService` registrations)

- [ ] **Step 1: Create the interface + DTOs**

```csharp
// Services/IUserCompanyTransferService.cs
namespace ShiftManager.Services;

public interface IUserCompanyTransferService
{
    /// <summary>Read-only: counts of what a move of <paramref name="userId"/> to
    /// <paramref name="destCompanyId"/> would clear/affect. Does not mutate anything.</summary>
    Task<MoveImpact> GetMoveImpactAsync(int userId, int destCompanyId);

    /// <summary>Transactional clean transfer. Assumes the caller has already authorized the move.</summary>
    Task<MoveResult> MoveUserToCompanyAsync(int userId, int destCompanyId, int actingAdminId);
}

public record MoveImpact(
    int FutureShifts,
    int PendingOrFutureTimeOff,
    int FutureChores,
    int OpenSwapRequests,
    int FutureOnDuty,
    int GameScores,
    int OwnedTeamCalendars,
    int ApiKeys,
    int ApproverRules,
    bool WillResetJobType,
    bool WillResetDepartment,
    bool WillResetPrimaryShiftType,
    bool WillResetHomeType,
    IReadOnlyList<string> DirectorCompaniesRemoved,
    IReadOnlyList<string> Warnings);

public record MoveResult(bool Success, string? ErrorKey = null, string? ErrorArg = null);
```

- [ ] **Step 2: Create a stub implementation that compiles**

```csharp
// Services/UserCompanyTransferService.cs
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Services;

public class UserCompanyTransferService : IUserCompanyTransferService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IConcurrencyService _concurrencyService;
    private readonly IAuditLogService _auditLogService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<UserCompanyTransferService> _logger;

    public UserCompanyTransferService(
        AppDbContext db,
        IGrantService grantService,
        IConcurrencyService concurrencyService,
        IAuditLogService auditLogService,
        IWebHostEnvironment env,
        ILogger<UserCompanyTransferService> logger)
    {
        _db = db;
        _grantService = grantService;
        _concurrencyService = concurrencyService;
        _auditLogService = auditLogService;
        _env = env;
        _logger = logger;
    }

    public Task<MoveImpact> GetMoveImpactAsync(int userId, int destCompanyId)
        => throw new NotImplementedException();

    public Task<MoveResult> MoveUserToCompanyAsync(int userId, int destCompanyId, int actingAdminId)
        => throw new NotImplementedException();
}
```

- [ ] **Step 3: Register in DI**

In `Program.cs`, immediately after the `builder.Services.AddScoped<IConcurrencyService, ConcurrencyService>();` group (~line 320-322), add:

```csharp
builder.Services.AddScoped<IUserCompanyTransferService, UserCompanyTransferService>();
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build ShiftManager.csproj`
Expected: Build succeeded, 0 errors.

> If the executable is locked (app running), STOP and resolve per CLAUDE.md §3 before rebuilding.

- [ ] **Step 5: Commit**

```bash
git add Services/IUserCompanyTransferService.cs Services/UserCompanyTransferService.cs Program.cs
git commit -m "feat(user-move): scaffold IUserCompanyTransferService + DI"
```

---

## Task 2: Promote `BuildGrantScopeForTemplateAsync` to IGrantService (DRY refactor)

**Files:**
- Modify: `Services/IGrantService.cs` (add signature near line 58)
- Modify: `Services/GrantService.cs` (add public method)
- Modify: `Pages/Admin/Users.cshtml.cs:2457` (delegate)

- [ ] **Step 1: Write a failing test for the new public method**

Add `ShiftManager.Tests/Services/GrantServiceScopeTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.Services;

public class GrantServiceScopeTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly GrantService _service;

    public GrantServiceScopeTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _service = new GrantService(_db, new Mock<IHierarchyService>().Object, new Mock<IAuditLogService>().Object);
    }

    [Fact]
    public async Task BuildRoleTemplateScopeAsync_Owner_ReturnsProjectScope()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var molecule = new Molecule { AreaId = area.Id, Name = "M", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule); await _db.SaveChangesAsync();
        var company = new Company { MoleculeId = molecule.Id, Name = "C", DisplayName = "C" };
        _db.Companies.Add(company); await _db.SaveChangesAsync();

        var scope = await _service.BuildRoleTemplateScopeAsync("Owner", company.Id, null);

        scope.ProjectId.Should().Be(project.Id);
        scope.CompanyId.Should().BeNull();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run it; verify it fails to compile (method missing)**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~GrantServiceScopeTests"`
Expected: FAIL — `'IGrantService' does not contain a definition for 'BuildRoleTemplateScopeAsync'`.

- [ ] **Step 3: Add the interface signature**

In `Services/IGrantService.cs`, after line 58 (`AssignRoleTemplateGrantsAsync`), add:

```csharp
    /// <summary>Builds the GrantScope for a role template key against a target company's hierarchy.</summary>
    Task<GrantScope> BuildRoleTemplateScopeAsync(string roleTemplateKey, int companyId, int? jobTypeId);
```

- [ ] **Step 4: Implement in GrantService (move the body from the page verbatim)**

In `Services/GrantService.cs`, add the public method (body copied verbatim from `Pages/Admin/Users.cshtml.cs:2457-2502`):

```csharp
public async Task<GrantScope> BuildRoleTemplateScopeAsync(string roleTemplateKey, int companyId, int? jobTypeId)
{
    var company = await _db.Companies
        .IgnoreQueryFilters() // SECURITY-AUDITED: scope resolution by explicit companyId
        .Include(c => c.Molecule)
            .ThenInclude(m => m!.Area)
                .ThenInclude(a => a!.Project)
        .FirstOrDefaultAsync(c => c.Id == companyId);

    if (company == null)
        return GrantScope.Company(companyId);

    return roleTemplateKey switch
    {
        "BRDirector" => new GrantScope(CompanyId: companyId, MoleculeId: company.MoleculeId),
        "Employee" => new GrantScope(CompanyId: companyId, MoleculeId: company.MoleculeId),
        "Lead" => new GrantScope(CompanyId: companyId, MoleculeId: company.MoleculeId, JobTypeId: jobTypeId),
        "MoleculeAdmin" or "Assigner" => company.MoleculeId.HasValue
            ? GrantScope.Molecule(company.MoleculeId.Value)
            : GrantScope.Company(companyId),
        "AreaAdmin" => company.Molecule?.Area?.Id != null
            ? GrantScope.Area(company.Molecule.Area.Id)
            : GrantScope.Company(companyId),
        "Director" => company.MoleculeId.HasValue
            ? new GrantScope(MoleculeId: company.MoleculeId.Value, JobTypeId: jobTypeId)
            : GrantScope.Company(companyId),
        "Owner" => company.Molecule?.Area?.ProjectId != null
            ? GrantScope.Project(company.Molecule.Area.ProjectId)
            : GrantScope.Company(companyId),
        _ => GrantScope.Company(companyId)
    };
}
```

- [ ] **Step 5: Make the page delegate (DRY — no behavior change)**

Replace the body of `Pages/Admin/Users.cshtml.cs:2457` private method with a delegate:

```csharp
private Task<GrantScope> BuildGrantScopeForTemplateAsync(string roleTemplateKey, int companyId, int? jobTypeId)
    => _grantService.BuildRoleTemplateScopeAsync(roleTemplateKey, companyId, jobTypeId);
```

- [ ] **Step 6: Run tests + build**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~GrantServiceScopeTests"`
Expected: PASS.
Run: `dotnet build ShiftManager.csproj` → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Services/IGrantService.cs Services/GrantService.cs Pages/Admin/Users.cshtml.cs ShiftManager.Tests/Services/GrantServiceScopeTests.cs
git commit -m "refactor(grants): promote BuildRoleTemplateScopeAsync to IGrantService"
```

---

## Task 3: Add explicit-companyId audit overload

**Files:**
- Modify: `Services/IAuditLogService.cs` (add after line 14)
- Modify: `Services/AuditLogService.cs` (add overload)

- [ ] **Step 1: Add interface signature**

In `Services/IAuditLogService.cs`, after the existing `LogUserActionAsync` (line 14), add:

```csharp
    /// <summary>Writes an audit entry under an EXPLICIT company (not the current tenant).
    /// Used by cross-company operations like user moves.</summary>
    Task LogUserActionAsync(int userId, int companyId, string action, string entityType, int? entityId, string description, string? details = null);
```

- [ ] **Step 2: Implement the overload**

In `Services/AuditLogService.cs`, add a method that mirrors the existing `LogUserActionAsync` body (lines 74-117) but sets `CompanyId = companyId` instead of `_tenantResolver.GetCurrentTenantId()`. Reuse the same denormalized-field lookups (UserEmail/UserDisplayName) and the same IP/UserAgent capture. Concretely:

```csharp
public async Task LogUserActionAsync(int userId, int companyId, string action, string entityType, int? entityId, string description, string? details = null)
{
    var user = await _db.Users.IgnoreQueryFilters() // SECURITY-AUDITED: denormalize actor by explicit userId
        .Where(u => u.Id == userId)
        .Select(u => new { u.Email, u.DisplayName })
        .FirstOrDefaultAsync();

    var http = _httpContextAccessor.HttpContext;
    var log = new AuditLog
    {
        CompanyId = companyId,
        UserId = userId,
        UserEmail = user?.Email ?? string.Empty,
        UserDisplayName = user?.DisplayName ?? string.Empty,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        Description = description,
        Details = details,
        Timestamp = DateTime.UtcNow,
        IpAddress = http?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
        UserAgent = http?.Request.Headers.UserAgent.ToString() ?? string.Empty
    };
    _db.AuditLogs.Add(log);
    await _db.SaveChangesAsync();
}
```

> Verify the exact field-population pattern in the existing method (lines 74-117) and match it (e.g. how it reads IP/UserAgent and the `_httpContextAccessor` field name). Adjust the snippet to match the real dependencies on `AuditLogService`.

- [ ] **Step 3: Build**

Run: `dotnet build ShiftManager.csproj` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Services/IAuditLogService.cs Services/AuditLogService.cs
git commit -m "feat(audit): add explicit-companyId LogUserActionAsync overload"
```

---

## Task 4: `MoveUserToCompanyAsync` — clear bucket-1 personal operational data

This is the core. Use soft-cancel where the model supports it; delete transient rows. All queries `IgnoreQueryFilters()` + `// SECURITY-AUDITED` scoped to `userId`.

**Files:**
- Modify: `Services/UserCompanyTransferService.cs`
- Test: `ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs` (create)

- [ ] **Step 1: Write the failing test (comprehensive seed + assertions)**

Create `ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.Services;

public class UserCompanyTransferServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IConcurrencyService> _concurrency = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IWebHostEnvironment> _env = new();
    private readonly UserCompanyTransferService _svc;

    public UserCompanyTransferServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        // Make SaveWithConcurrencyHandlingAsync actually invoke the save action and report success.
        _concurrency
            .Setup(c => c.SaveWithConcurrencyHandlingAsync(It.IsAny<Func<Task<int>>>(), It.IsAny<string>(), It.IsAny<int?>()))
            .Returns<Func<Task<int>>, string, int?>(async (save, _, __) => { await save(); return new ConcurrencySaveResult(true); });

        _grants.Setup(g => g.BuildRoleTemplateScopeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new GrantScope(CompanyId: 2));
        _grants.Setup(g => g.AssignRoleTemplateGrantsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<GrantScope>(), It.IsAny<int?>()))
            .ReturnsAsync(1);

        _env.SetupGet(e => e.WebRootPath).Returns(Path.Combine(Path.GetTempPath(), "smtest_" + Guid.NewGuid()));

        _svc = new UserCompanyTransferService(_db, _grants.Object, _concurrency.Object, _audit.Object, _env.Object, NullLogger<UserCompanyTransferService>.Instance);
    }

    private async Task<(int userId, int srcCompany, int destCompany)> SeedAsync()
    {
        // Two companies in the same molecule (intra-molecule move).
        var project = new Project { Name = "P", DisplayName = "P" }; _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" }; _db.Areas.Add(area); await _db.SaveChangesAsync();
        var mol = new Molecule { AreaId = area.Id, Name = "M", Type = MoleculeType.Workforce }; _db.Molecules.Add(mol); await _db.SaveChangesAsync();
        var src = new Company { MoleculeId = mol.Id, Name = "Src", DisplayName = "Src" };
        var dest = new Company { MoleculeId = mol.Id, Name = "Dest", DisplayName = "Dest" };
        _db.Companies.AddRange(src, dest); await _db.SaveChangesAsync();

        var user = new AppUser { CompanyId = src.Id, Email = "u@x.com", DisplayName = "U",
            JobTypeId = 5, DepartmentId = 6, PrimaryShiftTypeId = 7, HomeTypeId = 8, RoleTemplateId = null,
            Role = UserRole.Employee, PasswordHash = Array.Empty<byte>(), PasswordSalt = Array.Empty<byte>() };
        _db.Users.Add(user); await _db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Future shift assignment (via ShiftInstance.WorkDate).
        var instance = new ShiftInstance { CompanyId = src.Id, WorkDate = today.AddDays(3) };
        _db.ShiftInstances.Add(instance); await _db.SaveChangesAsync();
        _db.ShiftAssignments.Add(new ShiftAssignment { CompanyId = src.Id, UserId = user.Id, ShiftInstanceId = instance.Id });
        _db.TimeOffRequests.Add(new TimeOffRequest { CompanyId = src.Id, UserId = user.Id, Status = RequestStatus.Pending, StartDate = today.AddDays(2), EndDate = today.AddDays(4), Type = TimeOffType.Vacation });
        _db.Chores.Add(new Chore { CompanyId = src.Id, UserId = user.Id, Date = today.AddDays(2), Title = "c", CanceledAt = null });
        _db.OnDuties.Add(new OnDuty { CompanyId = src.Id, UserId = user.Id, Date = today.AddDays(2), CanceledAt = null });
        _db.GameScores.Add(new GameScore { CompanyId = src.Id, UserId = user.Id });
        _db.UserNotifications.Add(new UserNotification { CompanyId = src.Id, UserId = user.Id });
        await _db.SaveChangesAsync();
        return (user.Id, src.Id, dest.Id);
    }

    [Fact]
    public async Task Move_ClearsFutureOperationalData_AndSetsCompanyId()
    {
        var (userId, _, destCompany) = await SeedAsync();

        var result = await _svc.MoveUserToCompanyAsync(userId, destCompany, actingAdminId: userId + 999);

        result.Success.Should().BeTrue();
        var moved = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        moved.CompanyId.Should().Be(destCompany);

        // Future shift assignment removed.
        (await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(s => s.UserId == userId)).Should().Be(0);
        // Pending time-off canceled.
        (await _db.TimeOffRequests.IgnoreQueryFilters().Where(t => t.UserId == userId).AllAsync(t => t.Status == RequestStatus.Canceled)).Should().BeTrue();
        // Chore + OnDuty soft-canceled.
        (await _db.Chores.IgnoreQueryFilters().Where(c => c.UserId == userId).AllAsync(c => c.CanceledAt != null)).Should().BeTrue();
        (await _db.OnDuties.IgnoreQueryFilters().Where(o => o.UserId == userId).AllAsync(o => o.CanceledAt != null)).Should().BeTrue();
        // Game scores + notifications deleted.
        (await _db.GameScores.IgnoreQueryFilters().CountAsync(g => g.UserId == userId)).Should().Be(0);
        (await _db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == userId)).Should().Be(0);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
```

- [ ] **Step 2: Run it; verify it fails**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~UserCompanyTransferServiceTests"`
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Implement the clear logic + transaction skeleton**

Replace `MoveUserToCompanyAsync` in `Services/UserCompanyTransferService.cs`. Implement the transaction skeleton and bucket-1 clearing (later tasks fill in buckets 2/3, scalar reset, grants, avatar, audit at the marked points):

```csharp
public async Task<MoveResult> MoveUserToCompanyAsync(int userId, int destCompanyId, int actingAdminId)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var now = DateTime.UtcNow;

    var user = await _db.Users.IgnoreQueryFilters() // SECURITY-AUDITED: load move target by explicit id
        .FirstOrDefaultAsync(u => u.Id == userId);
    if (user == null) return new MoveResult(false, "Error_UserNotFound");

    var dest = await _db.Companies.IgnoreQueryFilters() // SECURITY-AUDITED: validate dest by explicit id
        .FirstOrDefaultAsync(c => c.Id == destCompanyId);
    if (dest == null) return new MoveResult(false, "Error_CompanyNotFound");
    if (dest.IsHeadquarters) return new MoveResult(false, "Error_MoveDestHq");
    if (user.CompanyId == destCompanyId) return new MoveResult(false, "Error_MoveSameCompany");

    var sourceCompanyId = user.CompanyId;

    using var tx = await _db.Database.BeginTransactionAsync();
    try
    {
        // --- BUCKET 1: personal operational data (soft-cancel where supported, else delete) ---

        // Future shift assignments: delete to free the slot (matches delete-handler semantics).
        // SECURITY-AUDITED: scoped to userId; future via ShiftInstance.WorkDate.
        await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.UserId == userId && sa.ShiftInstance!.WorkDate >= today)
            .ExecuteDeleteAsync();

        // This user as a trainer of others -> null the link (keep the assignment).
        // SECURITY-AUDITED: scoped to TraineeUserId == userId.
        await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.TraineeUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.TraineeUserId, (int?)null));

        // Pending / future-approved time-off -> Canceled (auditable).
        // SECURITY-AUDITED: scoped to userId.
        await _db.TimeOffRequests.IgnoreQueryFilters()
            .Where(t => t.UserId == userId
                && (t.Status == RequestStatus.Pending
                    || t.Status == RequestStatus.PendingSecondApproval
                    || (t.Status == RequestStatus.Approved && t.EndDate >= today)))
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.Status, RequestStatus.Canceled));

        // Future chores -> soft cancel. SECURITY-AUDITED: scoped to userId.
        await _db.Chores.IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.Date >= today && c.CanceledAt == null)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.CanceledAt, (DateTime?)now));

        // Future on-duty -> soft cancel. SECURITY-AUDITED: scoped to userId.
        await _db.OnDuties.IgnoreQueryFilters()
            .Where(o => o.UserId == userId && o.Date >= today && o.CanceledAt == null)
            .ExecuteUpdateAsync(o => o.SetProperty(x => x.CanceledAt, (DateTime?)now));

        // Open swap requests involving the user (From or To) -> Canceled.
        // SECURITY-AUDITED: scoped to userId via FromUserId/ToUserId.
        await _db.SwapRequests.IgnoreQueryFilters()
            .Where(sr => (sr.FromUserId == userId || sr.ToUserId == userId)
                && (sr.Status == RequestStatus.Pending || sr.Status == RequestStatus.PendingSecondApproval))
            .ExecuteUpdateAsync(sr => sr.SetProperty(x => x.Status, RequestStatus.Canceled));

        // Delete transient/company-specific rows. SECURITY-AUDITED: each scoped to userId.
        await _db.UserNotifications.IgnoreQueryFilters().Where(n => n.UserId == userId).ExecuteDeleteAsync();
        await _db.GameScores.IgnoreQueryFilters().Where(g => g.UserId == userId).ExecuteDeleteAsync();
        await _db.OnDutyRoleSubscriptions.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync();
        await _db.DailyNotificationPreferences.IgnoreQueryFilters().Where(p => p.UserId == userId).ExecuteDeleteAsync();
        await _db.HomeTypeOverrides.IgnoreQueryFilters().Where(h => h.UserId == userId).ExecuteDeleteAsync();
        await _db.FeatureFlags.IgnoreQueryFilters().Where(f => f.UserId == userId).ExecuteDeleteAsync();
        await _db.DutyRotationEntries.IgnoreQueryFilters().Where(d => d.UserId == userId).ExecuteDeleteAsync();
        await _db.CalendarTextEntries.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync();
        await _db.TeamCalendarMembers.IgnoreQueryFilters().Where(m => m.MemberUserId == userId).ExecuteDeleteAsync();

        // --- BUCKET 2 (Task 5) ---
        // --- BUCKET 3 (Task 6) ---
        // --- SCALAR FK RESET + CompanyId (Task 7) ---
        user.CompanyId = destCompanyId; // (Task 7 also resets JobType/Dept/ShiftType/Home)
        // --- GRANTS + DirectorCompany + UserRoleAssignment (Task 8) ---
        // --- AVATAR (Task 9) ---
        // --- AUDIT (Task 10) ---

        var save = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
            () => _db.SaveChangesAsync(), "UserCompanyMove", userId);
        if (!save.Success)
        {
            await tx.RollbackAsync();
            return new MoveResult(false, "Error_ConcurrencyConflict");
        }

        await tx.CommitAsync();
        return new MoveResult(true);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogError(ex, "User move failed: user {UserId} -> company {DestCompanyId}", userId, destCompanyId);
        try { await tx.RollbackAsync(); } catch { /* tx already done */ }
        return new MoveResult(false, "Error_MoveFailed");
    }
}
```

> Verify each `DbSet` name against `AppDbContext` (e.g. `OnDuties`, `OnDutyRoleSubscriptions`, `DailyNotificationPreferences`, `HomeTypeOverrides`, `FeatureFlags`, `DutyRotationEntries`, `CalendarTextEntries`, `TeamCalendarMembers`). Fix any name mismatch the compiler reports — do not guess.

- [ ] **Step 4: Run the test; verify it passes**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~UserCompanyTransferServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/UserCompanyTransferService.cs ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs
git commit -m "feat(user-move): clear bucket-1 personal operational data in transaction"
```

---

## Task 5: Revoke bucket-2 credentials / active obligations

**Files:** Modify `Services/UserCompanyTransferService.cs`; extend the test.

- [ ] **Step 1: Add a failing test**

Add to `UserCompanyTransferServiceTests` (and seed an `ApiKey` + `VacationApprovalRule` in `SeedAsync`, or a dedicated seed):

```csharp
[Fact]
public async Task Move_RevokesApiKeys_AndClearsApproverRules()
{
    var (userId, src, dest) = await SeedAsync();
    _db.ApiKeys.Add(new ApiKey { CompanyId = src, CreatedBy = userId, IsActive = true });
    _db.VacationApprovalRules.Add(new VacationApprovalRule { CompanyId = src, ApproverUserId = userId, CreatedBy = userId + 1 });
    await _db.SaveChangesAsync();

    await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);

    (await _db.ApiKeys.IgnoreQueryFilters().CountAsync(k => k.CreatedBy == userId)).Should().Be(0);
    (await _db.VacationApprovalRules.IgnoreQueryFilters().Where(r => r.ApproverUserId == userId).CountAsync()).Should().Be(0);
}
```

- [ ] **Step 2: Run; verify it fails** (ApiKey still present).

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --filter "FullyQualifiedName~UserCompanyTransferServiceTests"`

- [ ] **Step 3: Implement at the `// --- BUCKET 2 ---` marker**

```csharp
// BUCKET 2: credentials & active obligations.
// API keys are credentials for the OLD company — revoke. SECURITY-AUDITED: scoped to CreatedBy == userId.
await _db.ApiKeys.IgnoreQueryFilters().Where(k => k.CreatedBy == userId).ExecuteDeleteAsync();
// Pending API key requests by the user -> delete. SECURITY-AUDITED: scoped to RequestedBy == userId.
await _db.ApiKeyRequests.IgnoreQueryFilters()
    .Where(r => r.RequestedBy == userId && r.Status == ApiKeyRequestStatus.Pending)
    .ExecuteDeleteAsync();
// User is a named approver in old-company rules -> null the named approver (rule itself stays).
// SECURITY-AUDITED: scoped to ApproverUserId == userId.
await _db.VacationApprovalRules.IgnoreQueryFilters()
    .Where(r => r.ApproverUserId == userId)
    .ExecuteUpdateAsync(r => r.SetProperty(x => x.ApproverUserId, (int?)null));
```

> NOTE: the test asserts `ApproverUserId == userId` count is 0 — nulling satisfies that (the row no longer matches). Confirm `ApproverUserId` is nullable (it is, per `VacationApprovalRule.cs:8`).

- [ ] **Step 4: Run; verify PASS.**

- [ ] **Step 5: Commit**

```bash
git add Services/UserCompanyTransferService.cs ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs
git commit -m "feat(user-move): revoke API keys + clear named approver rules"
```

---

## Task 6: Reassign bucket-3 owned TeamCalendars

**Files:** Modify `Services/UserCompanyTransferService.cs`; extend the test.

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task Move_ReassignsOwnedTeamCalendars_ToActingAdmin()
{
    var (userId, src, dest) = await SeedAsync();
    var admin = userId + 999;
    _db.TeamCalendars.Add(new TeamCalendar { CompanyId = src, OwnerId = userId, IsDeleted = false });
    await _db.SaveChangesAsync();

    await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: admin);

    (await _db.TeamCalendars.IgnoreQueryFilters().CountAsync(t => t.OwnerId == userId)).Should().Be(0);
    (await _db.TeamCalendars.IgnoreQueryFilters().CountAsync(t => t.OwnerId == admin)).Should().Be(1);
}
```

- [ ] **Step 2: Run; verify it fails.**

- [ ] **Step 3: Implement at `// --- BUCKET 3 ---`**

```csharp
// BUCKET 3: shared assets the old company still needs — keep the calendar, transfer ownership to the admin.
// SECURITY-AUDITED: scoped to OwnerId == userId.
await _db.TeamCalendars.IgnoreQueryFilters()
    .Where(t => t.OwnerId == userId)
    .ExecuteUpdateAsync(t => t.SetProperty(x => x.OwnerId, actingAdminId));
```

- [ ] **Step 4: Run; verify PASS. Step 5: Commit**

```bash
git add Services/UserCompanyTransferService.cs ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs
git commit -m "feat(user-move): reassign owned team calendars to acting admin"
```

---

## Task 7: Reset scalar FKs + set CompanyId

**Files:** Modify `Services/UserCompanyTransferService.cs`; extend the test.

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task Move_ResetsScalarFks_AndKeepsRoleTemplate()
{
    var (userId, _, dest) = await SeedAsync();
    var before = await _db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == userId);
    before.JobTypeId.Should().NotBeNull(); // seeded as 5

    await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);

    var after = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
    after.JobTypeId.Should().BeNull();
    after.DepartmentId.Should().BeNull();
    after.PrimaryShiftTypeId.Should().BeNull();
    after.HomeTypeId.Should().BeNull();
    after.CompanyId.Should().Be(dest);
}
```

- [ ] **Step 2: Run; verify it fails** (FKs not reset yet).

- [ ] **Step 3: Replace the `// --- SCALAR FK RESET + CompanyId ---` marker line**

Replace the single `user.CompanyId = destCompanyId;` line with:

```csharp
// Scalar FKs point at old-molecule/area-scoped rows — reset (admin re-assigns in dest).
user.JobTypeId = null;
user.DepartmentId = null;
user.PrimaryShiftTypeId = null;
user.HomeTypeId = null;
// RoleTemplateId is global/cross-tenant — KEEP it.
user.CompanyId = destCompanyId;
```

- [ ] **Step 4: Run; verify PASS. Step 5: Commit**

```bash
git add Services/UserCompanyTransferService.cs ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs
git commit -m "feat(user-move): reset stale scalar FKs and set destination CompanyId"
```

---

## Task 8: Re-scope grants + DirectorCompany + UserRoleAssignment

**Files:** Modify `Services/UserCompanyTransferService.cs`; extend the test.

> Resolves spec open-item #1: delete old-company `UserRoleAssignment` rows and create one destination-scoped row mirroring `RoleService.cs:160-172`.

- [ ] **Step 1: Failing test (grants re-applied; old director/role-assignment rows gone)**

```csharp
[Fact]
public async Task Move_RevokesGrants_ReappliesForDest_AndCleansDirectorCompany()
{
    var (userId, src, dest) = await SeedAsync();
    _db.Grants.Add(new Grant { UserId = userId, GrantTypeId = 1, CompanyId = src, CanOwn = true });
    _db.DirectorCompanies.Add(new DirectorCompany { UserId = userId, CompanyId = src, GrantedBy = 1, IsDeleted = false });
    _db.UserRoleAssignments.Add(new UserRoleAssignment { UserId = userId, RoleTemplateId = 1, CompanyId = src, AssignedByUserId = 1, AssignedAt = DateTime.UtcNow, IsActive = true });
    await _db.SaveChangesAsync();

    await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);

    (await _db.Grants.IgnoreQueryFilters().CountAsync(g => g.UserId == userId && g.CompanyId == src)).Should().Be(0);
    (await _db.DirectorCompanies.IgnoreQueryFilters().CountAsync(d => d.UserId == userId && d.CompanyId == src)).Should().Be(0);
    (await _db.UserRoleAssignments.IgnoreQueryFilters().CountAsync(a => a.UserId == userId && a.CompanyId == src)).Should().Be(0);
    // Grants re-applied (mock returns 1).
    _grants.Verify(g => g.AssignRoleTemplateGrantsAsync(userId, It.IsAny<string>(), It.IsAny<GrantScope>(), It.IsAny<int?>()), Times.Once);
}
```

- [ ] **Step 2: Run; verify it fails.**

- [ ] **Step 3: Implement at the `// --- GRANTS ... ---` marker**

Place this AFTER `user.CompanyId = destCompanyId;` is set, so scope reflects the destination. It uses the user's existing role-template key (kept), falling back to the role→template mapper:

```csharp
// Resolve the role-template key (RoleTemplateId is kept). Fall back to the role mapper for legacy users.
string templateKey;
if (user.RoleTemplateId.HasValue)
{
    templateKey = await _db.RoleTemplates.IgnoreQueryFilters()
        .Where(rt => rt.Id == user.RoleTemplateId.Value)
        .Select(rt => rt.Key)
        .FirstOrDefaultAsync() ?? Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(user.Role, null);
}
else
{
    templateKey = Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(user.Role, null);
}

// Revoke all grants (Grant has no query filter; ExecuteDelete executes immediately within the tx).
// SECURITY-AUDITED: scoped to UserId == userId.
await _db.Grants.IgnoreQueryFilters().Where(g => g.UserId == userId).ExecuteDeleteAsync();

// Remove old DirectorCompany mappings. SECURITY-AUDITED: scoped to UserId == userId.
await _db.DirectorCompanies.IgnoreQueryFilters().Where(d => d.UserId == userId).ExecuteDeleteAsync();

// Remove old role-assignment records. SECURITY-AUDITED: scoped to UserId == userId.
await _db.UserRoleAssignments.IgnoreQueryFilters().Where(a => a.UserId == userId).ExecuteDeleteAsync();

// Build dest scope and re-apply the template's grants.
var destScope = await _grantService.BuildRoleTemplateScopeAsync(templateKey, destCompanyId, jobTypeId: null);
await _grantService.AssignRoleTemplateGrantsAsync(userId, templateKey, destScope, grantedByUserId: actingAdminId);

// Record a destination-scoped role assignment (mirrors RoleService.cs:160-172).
if (user.RoleTemplateId.HasValue)
{
    _db.UserRoleAssignments.Add(new Models.UserRoleAssignment
    {
        UserId = userId,
        RoleTemplateId = user.RoleTemplateId.Value,
        CompanyId = destScope.CompanyId,
        DepartmentId = destScope.DepartmentId,
        MoleculeId = destScope.MoleculeId,
        AreaId = destScope.AreaId,
        JobTypeId = destScope.JobTypeId,
        AssignedByUserId = actingAdminId,
        AssignedAt = DateTime.UtcNow,
        IsActive = true
    });
}

// If destination role is a director-type, recreate a DirectorCompany at the dest molecule HQ.
if (user.Role == UserRole.Director || user.Role == UserRole.AreaAdmin)
{
    var destHqId = await _db.Companies.IgnoreQueryFilters() // SECURITY-AUDITED: HQ lookup by dest molecule
        .Where(c => c.MoleculeId == dest.MoleculeId && c.IsHeadquarters)
        .Select(c => c.Id).FirstOrDefaultAsync();
    if (destHqId != 0)
    {
        _db.DirectorCompanies.Add(new Models.DirectorCompany
        {
            UserId = userId, CompanyId = destHqId, GrantedBy = actingAdminId,
            GrantedAt = DateTime.UtcNow, IsDeleted = false
        });
    }
}
```

> Confirm `Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey` namespace/signature (`Pages/Admin/Users.cshtml.cs:2451` calls it). Confirm `GrantScope` exposes `CompanyId/DepartmentId/MoleculeId/AreaId/JobTypeId` (it does — `IGrantService.cs:84-100`).

- [ ] **Step 4: Run; verify PASS. Step 5: Commit**

```bash
git add Services/UserCompanyTransferService.cs ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs
git commit -m "feat(user-move): re-scope grants, clean director/role-assignment, recreate dest director mapping"
```

---

## Task 9: Avatar file migration (copy-before-commit, delete-after, rollback-safe)

**Files:** Modify `Services/UserCompanyTransferService.cs`; extend the test.

- [ ] **Step 1: Failing test (file moved to dest folder; src removed)**

```csharp
[Fact]
public async Task Move_MigratesAvatarFiles_ToDestFolder()
{
    var (userId, src, dest) = await SeedAsync();
    var root = _env.Object.WebRootPath;
    var srcDir = Path.Combine(root, "avatars", src.ToString());
    Directory.CreateDirectory(srcDir);
    await File.WriteAllTextAsync(Path.Combine(srcDir, $"{userId}.jpg"), "full");
    await File.WriteAllTextAsync(Path.Combine(srcDir, $"{userId}_thumb.jpg"), "thumb");
    await _db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
        .ExecuteUpdateAsync(u => u.SetProperty(x => x.AvatarFileName, $"{userId}.jpg"));

    await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);

    var destDir = Path.Combine(root, "avatars", dest.ToString());
    File.Exists(Path.Combine(destDir, $"{userId}.jpg")).Should().BeTrue();
    File.Exists(Path.Combine(destDir, $"{userId}_thumb.jpg")).Should().BeTrue();
    File.Exists(Path.Combine(srcDir, $"{userId}.jpg")).Should().BeFalse();
}
```

- [ ] **Step 2: Run; verify it fails.**

- [ ] **Step 3: Implement.** Add a private helper and call it. Track copied dest paths so the catch can clean them on rollback.

Add field/locals: declare `var copiedDestPaths = new List<string>();` near the top of the method (before the transaction). At the `// --- AVATAR ---` marker (inside the try, before the save), copy:

```csharp
// Copy avatar files to the dest folder BEFORE commit (GetAvatarUrl resolves via current tenant).
if (!string.IsNullOrEmpty(user.AvatarFileName))
{
    var srcDir = Path.Combine(_env.WebRootPath, "avatars", sourceCompanyId.ToString());
    var destDir = Path.Combine(_env.WebRootPath, "avatars", destCompanyId.ToString());
    Directory.CreateDirectory(destDir);
    foreach (var name in new[] { $"{userId}.jpg", $"{userId}_thumb.jpg" })
    {
        var from = Path.Combine(srcDir, name);
        var to = Path.Combine(destDir, name);
        if (File.Exists(from))
        {
            File.Copy(from, to, overwrite: true);
            copiedDestPaths.Add(to);
        }
    }
}
```

After `await tx.CommitAsync();` (commit succeeded) delete the old files:

```csharp
        await tx.CommitAsync();

        // Post-commit: remove old-folder avatar copies (best-effort; DB is already correct).
        if (!string.IsNullOrEmpty(user.AvatarFileName))
        {
            var srcDir = Path.Combine(_env.WebRootPath, "avatars", sourceCompanyId.ToString());
            foreach (var name in new[] { $"{userId}.jpg", $"{userId}_thumb.jpg" })
            {
                var p = Path.Combine(srcDir, name);
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _logger.LogWarning(ex, "Failed deleting old avatar {Path}", p); }
            }
        }
        return new MoveResult(true);
```

In the `catch` block, before returning, clean up the dest copies so a rolled-back move leaves no orphan:

```csharp
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogError(ex, "User move failed: user {UserId} -> company {DestCompanyId}", userId, destCompanyId);
        try { await tx.RollbackAsync(); } catch { /* tx already done */ }
        foreach (var p in copiedDestPaths)
        { try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ } }
        return new MoveResult(false, "Error_MoveFailed");
    }
```

Also handle the concurrency-failure early return (after `if (!save.Success)`): clean `copiedDestPaths` there too before returning.

- [ ] **Step 4: Run; verify PASS. Step 5: Commit**

```bash
git add Services/UserCompanyTransferService.cs ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs
git commit -m "feat(user-move): migrate avatar files with rollback-safe cleanup"
```

---

## Task 10: Write the two audit entries (source + dest)

**Files:** Modify `Services/UserCompanyTransferService.cs`; extend the test.

- [ ] **Step 1: Failing test (verifies both audit calls)**

```csharp
[Fact]
public async Task Move_WritesAuditForBothCompanies()
{
    var (userId, src, dest) = await SeedAsync();
    var admin = userId + 999;

    await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: admin);

    _audit.Verify(a => a.LogUserActionAsync(admin, src, "UserMovedOut", "User", userId, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    _audit.Verify(a => a.LogUserActionAsync(admin, dest, "UserMovedIn", "User", userId, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
}
```

- [ ] **Step 2: Run; verify it fails.**

- [ ] **Step 3: Implement at the `// --- AUDIT ---` marker (inside try, before save)**

```csharp
var detail = $"userId={userId};from={sourceCompanyId};to={destCompanyId};by={actingAdminId}";
await _auditLogService.LogUserActionAsync(actingAdminId, sourceCompanyId, "UserMovedOut", "User", userId,
    $"User {userId} moved to company {destCompanyId}", detail);
await _auditLogService.LogUserActionAsync(actingAdminId, destCompanyId, "UserMovedIn", "User", userId,
    $"User {userId} moved from company {sourceCompanyId}", detail);
```

- [ ] **Step 4: Run; verify PASS. Step 5: Commit**

```bash
git add Services/UserCompanyTransferService.cs ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs
git commit -m "feat(user-move): write source+dest audit entries for the move"
```

---

## Task 11: Implement `GetMoveImpactAsync` (read-only preview)

**Files:** Modify `Services/UserCompanyTransferService.cs`; extend the test.

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task GetMoveImpact_CountsMatchWhatMoveClears()
{
    var (userId, _, dest) = await SeedAsync(); // seeds 1 future shift, 1 pending TO, 1 chore, 1 onduty, 1 gamescore

    var impact = await _svc.GetMoveImpactAsync(userId, dest);

    impact.FutureShifts.Should().Be(1);
    impact.PendingOrFutureTimeOff.Should().Be(1);
    impact.FutureChores.Should().Be(1);
    impact.FutureOnDuty.Should().Be(1);
    impact.GameScores.Should().Be(1);
    impact.WillResetJobType.Should().BeTrue();
}
```

- [ ] **Step 2: Run; verify it fails (NotImplementedException).**

- [ ] **Step 3: Implement (mirror the move predicates, read-only, no mutation)**

```csharp
public async Task<MoveImpact> GetMoveImpactAsync(int userId, int destCompanyId)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var user = await _db.Users.IgnoreQueryFilters().AsNoTracking() // SECURITY-AUDITED: preview by explicit id
        .FirstOrDefaultAsync(u => u.Id == userId);
    if (user == null)
        return new MoveImpact(0,0,0,0,0,0,0,0,0,false,false,false,false, Array.Empty<string>(), new[] { "User not found" });

    var futureShifts = await _db.ShiftAssignments.IgnoreQueryFilters()
        .CountAsync(sa => sa.UserId == userId && sa.ShiftInstance!.WorkDate >= today);
    var timeOff = await _db.TimeOffRequests.IgnoreQueryFilters()
        .CountAsync(t => t.UserId == userId && (t.Status == RequestStatus.Pending
            || t.Status == RequestStatus.PendingSecondApproval
            || (t.Status == RequestStatus.Approved && t.EndDate >= today)));
    var chores = await _db.Chores.IgnoreQueryFilters().CountAsync(c => c.UserId == userId && c.Date >= today && c.CanceledAt == null);
    var swaps = await _db.SwapRequests.IgnoreQueryFilters().CountAsync(sr => (sr.FromUserId == userId || sr.ToUserId == userId)
        && (sr.Status == RequestStatus.Pending || sr.Status == RequestStatus.PendingSecondApproval));
    var onDuty = await _db.OnDuties.IgnoreQueryFilters().CountAsync(o => o.UserId == userId && o.Date >= today && o.CanceledAt == null);
    var games = await _db.GameScores.IgnoreQueryFilters().CountAsync(g => g.UserId == userId);
    var ownedCals = await _db.TeamCalendars.IgnoreQueryFilters().CountAsync(t => t.OwnerId == userId);
    var apiKeys = await _db.ApiKeys.IgnoreQueryFilters().CountAsync(k => k.CreatedBy == userId);
    var approverRules = await _db.VacationApprovalRules.IgnoreQueryFilters().CountAsync(r => r.ApproverUserId == userId);

    var directorCompanies = await _db.DirectorCompanies.IgnoreQueryFilters()
        .Where(d => d.UserId == userId)
        .Join(_db.Companies.IgnoreQueryFilters(), d => d.CompanyId, c => c.Id, (d, c) => c.Name)
        .ToListAsync();

    return new MoveImpact(
        futureShifts, timeOff, chores, swaps, onDuty, games, ownedCals, apiKeys, approverRules,
        WillResetJobType: user.JobTypeId.HasValue,
        WillResetDepartment: user.DepartmentId.HasValue,
        WillResetPrimaryShiftType: user.PrimaryShiftTypeId.HasValue,
        WillResetHomeType: user.HomeTypeId.HasValue,
        DirectorCompaniesRemoved: directorCompanies,
        Warnings: Array.Empty<string>());
}
```

- [ ] **Step 4: Run; verify PASS. Step 5: Commit**

```bash
git add Services/UserCompanyTransferService.cs ShiftManager.Tests/Services/UserCompanyTransferServiceTests.cs
git commit -m "feat(user-move): implement read-only GetMoveImpactAsync preview"
```

---

## Task 12: Razor handlers + authorization

**Files:**
- Create: `Pages/Admin/Users.cshtml.Move.cs`

The handlers own authorization. `UsersModel` already injects `_grantService`, `_directorService`, `_localizer`, and reads the current user via claims (see existing handlers). Confirm the exact injected field names in `Users.cshtml.cs` and match them.

- [ ] **Step 1: Create the partial with both handlers**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin;

public partial class UsersModel
{
    public async Task<IActionResult> OnGetMoveImpactAsync(int userId, int destCompanyId)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return new JsonResult(new { ok = false, error = "unauthorized" }) { StatusCode = 401 };

        var auth = await AuthorizeMoveAsync(adminId, userId, destCompanyId);
        if (auth != null) return new JsonResult(new { ok = false, error = _localizer[auth].Value });

        var impact = await _userCompanyTransferService.GetMoveImpactAsync(userId, destCompanyId);
        return new JsonResult(new { ok = true, impact });
    }

    public async Task<IActionResult> OnPostMoveUserAsync(int userId, int destCompanyId)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return Forbid();

        var auth = await AuthorizeMoveAsync(adminId, userId, destCompanyId);
        if (auth != null)
        {
            TempData["ErrorMessage"] = _localizer[auth].Value;
            return RedirectToPage();
        }

        var result = await _userCompanyTransferService.MoveUserToCompanyAsync(userId, destCompanyId, adminId);
        if (result.Success)
            TempData["SuccessMessage"] = _localizer["Users_MoveSuccess"].Value;
        else
            TempData["ErrorMessage"] = _localizer[result.ErrorKey ?? "Error_MoveFailed"].Value;
        return RedirectToPage();
    }

    /// <summary>Returns a localization key on failure, or null when authorized.</summary>
    private async Task<string?> AuthorizeMoveAsync(int adminId, int userId, int destCompanyId)
    {
        if (userId == adminId) return "Error_MoveSelf";

        var target = await _db.Users.IgnoreQueryFilters() // SECURITY-AUDITED: load move target by id
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (target == null) return "Error_UserNotFound";
        if (target.CompanyId == destCompanyId) return "Error_MoveSameCompany";

        var dest = await _db.Companies.IgnoreQueryFilters() // SECURITY-AUDITED: validate dest by id
            .FirstOrDefaultAsync(c => c.Id == destCompanyId);
        if (dest == null) return "Error_CompanyNotFound";
        if (dest.IsHeadquarters) return "Error_MoveDestHq";

        var isAdmin = await _grantService.HasGrantAsync(adminId, "AdminAccess");
        if (!isAdmin)
        {
            if (!await _grantService.HasGrantForCompanyAsync(adminId, "EditCompanyUsers", target.CompanyId))
                return "Error_NoPermissionSourceCompany";
            if (!await _grantService.HasGrantForCompanyAsync(adminId, "EditCompanyUsers", destCompanyId))
                return "Error_NoPermissionDestCompany";
        }

        // Privilege gate: must be allowed to assign the target's role (mirrors OnPostRoleAsync:1042).
        if (!await _directorService.CanAssignRoleAsync(target.Role))
            return "Error_NoPermissionMoveRole";

        return null;
    }
}
```

- [ ] **Step 2: Add the service field to `UsersModel`**

In `Pages/Admin/Users.cshtml.cs`, add `IUserCompanyTransferService` to the constructor + a `private readonly IUserCompanyTransferService _userCompanyTransferService;` field, assigned in the ctor. Confirm the existing field names `_db`, `_grantService`, `_directorService`, `_localizer` match (adjust the partial if they differ).

- [ ] **Step 3: Build**

Run: `dotnet build ShiftManager.csproj` → 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Pages/Admin/Users.cshtml.Move.cs Pages/Admin/Users.cshtml.cs
git commit -m "feat(user-move): add move/impact handlers with two-sided + role-gate authorization"
```

---

## Task 13: UI — Move action, modal, impact preview JS

**Files:** Modify `Pages/Admin/Users.cshtml`

- [ ] **Step 1: Add a "Move" button to each user row's actions** (near the existing Delete button). Use the same icon/button conventions already present in the file. Pass `data-user-id` and `data-user-name`.

```html
<button type="button" class="btn btn--sm btn--ghost js-move-user"
        data-user-id="@user.Id" data-user-name="@user.DisplayName"
        title="@Localizer["Users_MoveTitle"]">
    <i class="bi bi-arrow-left-right"></i> @Localizer["Users_Move"]
</button>
```

- [ ] **Step 2: Add the move modal markup** (mirror the existing delete-confirm modal structure in this file). It contains: a destination `<select>` populated from `Model.AvailableCompanies` (already built for the page, HQ excluded), a `#move-impact` summary container, and a confirm `<form method="post" asp-page-handler="MoveUser">` with hidden `userId` + `destCompanyId`.

```html
<div id="moveUserModal" class="modal" hidden>
  <div class="modal__dialog">
    <h3 id="move-modal-title"></h3>
    <label for="move-dest">@Localizer["Users_MoveDestination"]</label>
    <select id="move-dest" class="form-select">
      <option value="">@Localizer["Users_MoveSelectCompany"]</option>
      @foreach (var c in Model.AvailableCompanies)
      { <option value="@c.Id">@c.Name</option> }
    </select>
    <div id="move-impact" class="alert" aria-live="polite"></div>
    <form method="post" asp-page-handler="MoveUser">
      <input type="hidden" name="userId" id="move-user-id" />
      <input type="hidden" name="destCompanyId" id="move-dest-id" />
      <button type="submit" class="btn btn--danger" id="move-confirm" disabled>@Localizer["Users_MoveConfirm"]</button>
      <button type="button" class="btn btn--ghost" id="move-cancel">@Localizer["Common_Cancel"]</button>
    </form>
  </div>
</div>
```

- [ ] **Step 3: Add the JS** (in the page's existing `@section Scripts` / script block). On Move click → open modal; on destination change → fetch impact and render summary; enable confirm only when a destination is chosen and impact loaded.

```javascript
(function () {
  const modal = document.getElementById('moveUserModal');
  let userId = null;
  document.querySelectorAll('.js-move-user').forEach(b => b.addEventListener('click', () => {
    userId = b.dataset.userId;
    document.getElementById('move-user-id').value = userId;
    document.getElementById('move-modal-title').textContent = b.dataset.userName;
    document.getElementById('move-dest').value = '';
    document.getElementById('move-impact').textContent = '';
    document.getElementById('move-confirm').disabled = true;
    modal.hidden = false;
  }));
  document.getElementById('move-cancel').addEventListener('click', () => { modal.hidden = true; });
  document.getElementById('move-dest').addEventListener('change', async (e) => {
    const dest = e.target.value;
    document.getElementById('move-dest-id').value = dest;
    if (!dest) { document.getElementById('move-confirm').disabled = true; return; }
    const res = await fetch(`?handler=MoveImpact&userId=${userId}&destCompanyId=${dest}`);
    const data = await res.json();
    const box = document.getElementById('move-impact');
    if (!data.ok) { box.textContent = data.error; document.getElementById('move-confirm').disabled = true; return; }
    const i = data.impact;
    box.textContent = `Future shifts: ${i.futureShifts} · Time-off: ${i.pendingOrFutureTimeOff} · Chores: ${i.futureChores} · Swaps: ${i.openSwapRequests} · On-duty: ${i.futureOnDuty} · Game scores: ${i.gameScores} · Team calendars reassigned: ${i.ownedTeamCalendars} · API keys revoked: ${i.apiKeys}`;
    document.getElementById('move-confirm').disabled = false;
  });
})();
```

> Match the project's actual modal CSS classes + open/close mechanism (some modals use a class toggle, not the `hidden` attribute). Use whatever the existing delete modal uses for consistency. The impact summary text will be localized via the resx-driven labels in Task 14 if you prefer; the minimal version above is acceptable for first pass but prefer building the string from `Localizer` keys.

- [ ] **Step 4: Commit**

```bash
git add Pages/Admin/Users.cshtml
git commit -m "feat(user-move): add Move action, destination modal, and impact preview"
```

---

## Task 14: Localization (en-US + he-IL)

**Files:** Modify `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx`

- [ ] **Step 1: Add these keys to BOTH resx files** (English values in `SharedResources.resx`, Hebrew in `SharedResources.he-IL.resx`):

| Key | English | Hebrew |
|---|---|---|
| `Users_Move` | Move | העברה |
| `Users_MoveTitle` | Move user to another company | העברת משתמש לחברה אחרת |
| `Users_MoveDestination` | Destination company | חברת יעד |
| `Users_MoveSelectCompany` | Select a company… | בחר חברה… |
| `Users_MoveConfirm` | Move user | העבר משתמש |
| `Users_MoveSuccess` | User moved successfully. | המשתמש הועבר בהצלחה. |
| `Error_MoveSelf` | You cannot move yourself. | לא ניתן להעביר את עצמך. |
| `Error_MoveSameCompany` | The user is already in that company. | המשתמש כבר נמצא בחברה זו. |
| `Error_MoveDestHq` | Cannot move a user into a headquarters company. | לא ניתן להעביר משתמש לחברת מטה. |
| `Error_CompanyNotFound` | Destination company not found. | חברת היעד לא נמצאה. |
| `Error_NoPermissionSourceCompany` | You don't have permission over the user's current company. | אין לך הרשאה לחברה הנוכחית של המשתמש. |
| `Error_NoPermissionDestCompany` | You don't have permission over the destination company. | אין לך הרשאה לחברת היעד. |
| `Error_NoPermissionMoveRole` | You don't have permission to assign this user's role. | אין לך הרשאה להקצות את תפקיד המשתמש הזה. |
| `Error_MoveFailed` | The move could not be completed. | לא ניתן היה להשלים את ההעברה. |

> Reuse `Error_UserNotFound` and `Error_ConcurrencyConflict` if they already exist (grep the resx first; do not duplicate). Add only the missing ones.

- [ ] **Step 2: Build (resx compiles into the assembly)**

Run: `dotnet build ShiftManager.csproj` → 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat(user-move): add en-US + he-IL strings for move flow"
```

---

## Task 15: Full suite + manual verification

- [ ] **Step 1: Run the whole test suite**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj`
Expected: all green (previous count + the new `UserCompanyTransferServiceTests` + `GrantServiceScopeTests`).

- [ ] **Step 2: Add the authorization regression test** (molecule admin cannot move an Owner — verifies the `CanAssignRoleAsync` gate at the handler level is the contract). Because the gate uses HTTP context, assert it via the service-level invariant test plus a note that handler auth is covered by the gate reuse. Add a service test that a same-company / HQ-dest / self move returns the right `ErrorKey`:

```csharp
[Fact]
public async Task Move_ToSameCompany_Fails()
{
    var (userId, src, _) = await SeedAsync();
    var r = await _svc.MoveUserToCompanyAsync(userId, src, actingAdminId: userId + 1);
    r.Success.Should().BeFalse();
    r.ErrorKey.Should().Be("Error_MoveSameCompany");
}
```

Run the filter; verify PASS.

- [ ] **Step 3: Manual smoke test** (per `/run` or the project's launch skill): start the app, log in as a molecule admin, open `/Admin/Users`, click Move on a user in one company, pick another company in the same molecule, confirm the impact preview renders, confirm the move, and verify (a) the user now appears under the destination company, (b) their old future shifts are gone from the source calendar, (c) check `/Admin/Users` in Hebrew RTL + dark mode for the modal contrast (per MEMORY recurring-bug checklist).

- [ ] **Step 4: Final commit (if any manual-fix tweaks)**

```bash
git add -A
git commit -m "test(user-move): full-suite green + edge-case guards"
```

---

## Self-review notes (author)

- **Spec coverage:** §5 buckets → Tasks 4/5/6; §6 auth → Task 12; §7 architecture → Tasks 1/2/3/12; §8 entity table → Tasks 4-10; §9 avatar → Task 9; §10 transaction → Task 4 skeleton; §11 UX → Tasks 12/13; §14 testing → every task + 15; §15 localization → Task 14. Open-items #1/#2/#3 resolved in Tasks 8/3/12.
- **Verify-at-code-time flags (do not skip):** exact `DbSet` property names (Task 4 note), `AuditLogService` field/IP pattern (Task 3 note), `RoleTemplateMapper` namespace (Task 8 note), `UsersModel` injected field names + modal CSS mechanism (Tasks 12/13 notes), test project csproj path (file-structure note).
- **Not covered intentionally (bucket 4 = leave as history):** AuditLog/ProfileChangeAudit/RoleAssignmentAudit, UserDayNote, completed shifts, authorship `CreatedBy`/`UpdatedBy`, `ReviewedBy`, friendships, `UserJoinRequest` — no task touches these by design.
