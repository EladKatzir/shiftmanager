using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class UserCompanyTransferServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IConcurrencyService> _concurrency = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IWebHostEnvironment> _env = new();
    private readonly UserCompanyTransferService _svc;

    /// <summary>
    /// Minimal in-memory ITenantResolver so tests can build an AppDbContext WITH the tenant
    /// query filters registered (they only register when tenantResolver != null). The
    /// no-resolver fixture below runs with NO filters; this stub lets the CRIT-1 test verify
    /// the move still works when the active tenant differs from the moved user's company.
    /// </summary>
    private sealed class StubTenantResolver : ITenantResolver
    {
        public int Tenant;
        public int GetCurrentTenantId() => Tenant;
        public void SetCurrentTenantId(int companyId) => Tenant = companyId;
        public bool HasTenant() => true;
        public int? GetDirectorSelectedMoleculeId() => null;
    }

    public UserCompanyTransferServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _concurrency
            .Setup(c => c.SaveWithConcurrencyHandlingAsync(It.IsAny<Func<Task<int>>>(), It.IsAny<string>(), It.IsAny<int?>()))
            .Returns<Func<Task<int>>, string, int?>(async (save, _, __) => { await save(); return new ConcurrencySaveResult(true); });

        _grants.Setup(g => g.BuildRoleTemplateScopeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new GrantScope(CompanyId: 2));
        _grants.Setup(g => g.AssignRoleTemplateGrantsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<GrantScope>(), It.IsAny<int?>()))
            .ReturnsAsync(1);

        _env.SetupGet(e => e.WebRootPath).Returns(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smtest_" + System.Guid.NewGuid()));

        _svc = new UserCompanyTransferService(_db, _grants.Object, _concurrency.Object, _audit.Object, _env.Object, NullLogger<UserCompanyTransferService>.Instance);
    }

    private UserCompanyTransferService MakeService(AppDbContext db) =>
        new UserCompanyTransferService(db, _grants.Object, _concurrency.Object, _audit.Object, _env.Object, NullLogger<UserCompanyTransferService>.Instance);

    private async Task<(int userId, int srcCompany, int destCompany)> SeedAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" }; _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" }; _db.Areas.Add(area); await _db.SaveChangesAsync();
        var mol = new Molecule { AreaId = area.Id, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce }; _db.Molecules.Add(mol); await _db.SaveChangesAsync();
        var src = new Company { MoleculeId = mol.Id, Name = "Src", DisplayName = "Src" };
        var dest = new Company { MoleculeId = mol.Id, Name = "Dest", DisplayName = "Dest" };
        _db.Companies.AddRange(src, dest); await _db.SaveChangesAsync();

        var user = new AppUser { CompanyId = src.Id, Email = "u@x.com", DisplayName = "U",
            JobTypeId = 5, DepartmentId = 6, PrimaryShiftTypeId = 7, HomeTypeId = 8, RoleTemplateId = null,
            Role = UserRole.Employee, PasswordHash = System.Array.Empty<byte>(), PasswordSalt = System.Array.Empty<byte>() };
        _db.Users.Add(user); await _db.SaveChangesAsync();

        var today = System.DateOnly.FromDateTime(System.DateTime.UtcNow);
        var instance = new ShiftInstance { CompanyId = src.Id, ShiftTypeId = 1, WorkDate = today.AddDays(3) };
        _db.ShiftInstances.Add(instance); await _db.SaveChangesAsync();
        _db.ShiftAssignments.Add(new ShiftAssignment { CompanyId = src.Id, UserId = user.Id, ShiftInstanceId = instance.Id });
        _db.TimeOffRequests.Add(new TimeOffRequest { CompanyId = src.Id, UserId = user.Id, Status = RequestStatus.Pending, StartDate = today.AddDays(2), EndDate = today.AddDays(4), Type = TimeOffType.Vacation });
        // Chore requires CreatedBy (non-nullable int)
        _db.Chores.Add(new Chore { CompanyId = src.Id, UserId = user.Id, Date = today.AddDays(2), Title = "c", CanceledAt = null, CreatedBy = user.Id });
        // OnDuty is global (no CompanyId); requires CreatedBy (non-nullable int)
        _db.OnDuties.Add(new OnDuty { UserId = user.Id, Date = today.AddDays(2), CanceledAt = null, CreatedBy = user.Id, Type = OnDutyType.Hakam });
        // GameScore requires Score, PlayedAt, CurrentMonth
        _db.GameScores.Add(new GameScore { CompanyId = src.Id, UserId = user.Id, Score = 0, PlayedAt = System.DateTime.UtcNow, CurrentMonth = "2026-05" });
        _db.UserNotifications.Add(new UserNotification { CompanyId = src.Id, UserId = user.Id, Type = NotificationType.ShiftAdded, Title = "t", Message = "m" });
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

        (await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(s => s.UserId == userId)).Should().Be(0);
        (await _db.TimeOffRequests.IgnoreQueryFilters().Where(t => t.UserId == userId).AllAsync(t => t.Status == RequestStatus.Canceled)).Should().BeTrue();
        (await _db.Chores.IgnoreQueryFilters().Where(c => c.UserId == userId).AllAsync(c => c.CanceledAt != null)).Should().BeTrue();
        (await _db.OnDuties.IgnoreQueryFilters().Where(o => o.UserId == userId).AllAsync(o => o.CanceledAt != null)).Should().BeTrue();
        (await _db.GameScores.IgnoreQueryFilters().CountAsync(g => g.UserId == userId)).Should().Be(0);
        (await _db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == userId)).Should().Be(0);
    }

    /// <summary>
    /// CRIT-1 verification: with the tenant query filters ACTIVE and the active tenant set to a
    /// company that owns NONE of the seeded rows, the move must still delete the user's FUTURE
    /// shift assignment (proving IgnoreQueryFilters() disables the ShiftInstance nav filter too)
    /// while leaving the PAST shift assignment intact (proving the future-only scoping holds).
    /// </summary>
    [Fact]
    public async Task Move_WithActiveFilters_DifferentTenant_StillClearsFutureShift()
    {
        // Separate context built WITH a resolver so the query filters are registered.
        var resolver = new StubTenantResolver();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        await using var db = new AppDbContext(options, resolver);

        var today = System.DateOnly.FromDateTime(System.DateTime.UtcNow);

        var project = new Project { Name = "P2", DisplayName = "P2" }; db.Projects.Add(project); await db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A2", DisplayName = "A2" }; db.Areas.Add(area); await db.SaveChangesAsync();
        var mol = new Molecule { AreaId = area.Id, Name = "M2", DisplayName = "M2", Type = MoleculeType.Workforce }; db.Molecules.Add(mol); await db.SaveChangesAsync();
        var src = new Company { MoleculeId = mol.Id, Name = "Src2", DisplayName = "Src2" };
        var dest = new Company { MoleculeId = mol.Id, Name = "Dest2", DisplayName = "Dest2" };
        db.Companies.AddRange(src, dest); await db.SaveChangesAsync();

        // Seed under the source tenant so any interceptor uses src; we also set CompanyId explicitly.
        resolver.Tenant = src.Id;

        var user = new AppUser { CompanyId = src.Id, Email = "u2@x.com", DisplayName = "U2",
            Role = UserRole.Employee, PasswordHash = System.Array.Empty<byte>(), PasswordSalt = System.Array.Empty<byte>() };
        db.Users.Add(user); await db.SaveChangesAsync();

        var futureInstance = new ShiftInstance { CompanyId = src.Id, ShiftTypeId = 1, WorkDate = today.AddDays(3) };
        var pastInstance = new ShiftInstance { CompanyId = src.Id, ShiftTypeId = 1, WorkDate = today.AddDays(-3) };
        db.ShiftInstances.AddRange(futureInstance, pastInstance); await db.SaveChangesAsync();

        var futureAssignment = new ShiftAssignment { CompanyId = src.Id, UserId = user.Id, ShiftInstanceId = futureInstance.Id };
        var pastAssignment = new ShiftAssignment { CompanyId = src.Id, UserId = user.Id, ShiftInstanceId = pastInstance.Id };
        db.ShiftAssignments.AddRange(futureAssignment, pastAssignment); await db.SaveChangesAsync();

        // Verify the seeds actually landed under src (read back, filters bypassed).
        (await db.ShiftAssignments.IgnoreQueryFilters().CountAsync(s => s.UserId == user.Id)).Should().Be(2);

        // Activate filters against a tenant that owns NONE of these rows.
        resolver.Tenant = 999999;

        var svc = MakeService(db);
        var result = await svc.MoveUserToCompanyAsync(user.Id, dest.Id, actingAdminId: 1234567);

        result.Success.Should().BeTrue();

        // FUTURE assignment deleted; PAST assignment preserved (read via IgnoreQueryFilters).
        (await db.ShiftAssignments.IgnoreQueryFilters().CountAsync(s => s.UserId == user.Id && s.ShiftInstanceId == futureInstance.Id)).Should().Be(0);
        (await db.ShiftAssignments.IgnoreQueryFilters().CountAsync(s => s.UserId == user.Id && s.ShiftInstanceId == pastInstance.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Move_CancelsOpenSwapRequests_FromOrTo()
    {
        var (userId, srcCompany, destCompany) = await SeedAsync();

        _db.SwapRequests.Add(new SwapRequest { CompanyId = srcCompany, FromUserId = userId, FromAssignmentId = 1, Status = RequestStatus.Pending });
        _db.SwapRequests.Add(new SwapRequest { CompanyId = srcCompany, FromUserId = userId + 1, ToUserId = userId, FromAssignmentId = 2, Status = RequestStatus.Pending });
        await _db.SaveChangesAsync();

        var result = await _svc.MoveUserToCompanyAsync(userId, destCompany, actingAdminId: userId + 999);

        result.Success.Should().BeTrue();
        (await _db.SwapRequests.IgnoreQueryFilters()
            .Where(sr => sr.FromUserId == userId || sr.ToUserId == userId)
            .AllAsync(sr => sr.Status == RequestStatus.Canceled)).Should().BeTrue();
    }

    [Fact]
    public async Task Move_NullsTraineeUserIdLink()
    {
        var (userId, srcCompany, destCompany) = await SeedAsync();

        var today = System.DateOnly.FromDateTime(System.DateTime.UtcNow);
        var instance = new ShiftInstance { CompanyId = srcCompany, ShiftTypeId = 1, WorkDate = today.AddDays(5) };
        _db.ShiftInstances.Add(instance); await _db.SaveChangesAsync();
        // Another user owns the shift; the moved user is the shadowing trainee on it.
        var trainerAssignment = new ShiftAssignment { CompanyId = srcCompany, UserId = userId + 1, ShiftInstanceId = instance.Id, TraineeUserId = userId };
        _db.ShiftAssignments.Add(trainerAssignment); await _db.SaveChangesAsync();
        var assignmentId = trainerAssignment.Id;

        var result = await _svc.MoveUserToCompanyAsync(userId, destCompany, actingAdminId: userId + 999);

        result.Success.Should().BeTrue();
        // ExecuteUpdateAsync mutates the DB row directly but does not refresh EF's change tracker,
        // so the previously-tracked assignment instance would still report the stale TraineeUserId.
        // Clear the tracker to force a fresh read from the database.
        _db.ChangeTracker.Clear();
        var reloaded = await _db.ShiftAssignments.IgnoreQueryFilters().FirstAsync(s => s.Id == assignmentId);
        reloaded.TraineeUserId.Should().BeNull();
    }

    [Fact]
    public async Task Move_PreservesPastChoresAndOnDuty()
    {
        var (userId, srcCompany, destCompany) = await SeedAsync();

        var today = System.DateOnly.FromDateTime(System.DateTime.UtcNow);
        var pastChore = new Chore { CompanyId = srcCompany, UserId = userId, Date = today.AddDays(-3), Title = "past", CanceledAt = null, CreatedBy = userId };
        _db.Chores.Add(pastChore);
        var pastOnDuty = new OnDuty { UserId = userId, Date = today.AddDays(-3), CanceledAt = null, CreatedBy = userId, Type = OnDutyType.Hakam };
        _db.OnDuties.Add(pastOnDuty);
        await _db.SaveChangesAsync();
        var choreId = pastChore.Id;
        var onDutyId = pastOnDuty.Id;

        var result = await _svc.MoveUserToCompanyAsync(userId, destCompany, actingAdminId: userId + 999);

        result.Success.Should().BeTrue();
        (await _db.Chores.IgnoreQueryFilters().FirstAsync(c => c.Id == choreId)).CanceledAt.Should().BeNull();
        (await _db.OnDuties.IgnoreQueryFilters().FirstAsync(o => o.Id == onDutyId)).CanceledAt.Should().BeNull();
    }

    [Fact]
    public async Task Move_RevokesApiKeys_AndClearsApproverRules()
    {
        var (userId, src, dest) = await SeedAsync();
        _db.ApiKeys.Add(new ApiKey { CompanyId = src, CreatedBy = userId, IsActive = true });
        _db.VacationApprovalRules.Add(new VacationApprovalRule { CompanyId = src, ApproverUserId = userId, CreatedBy = userId + 1 });
        await _db.SaveChangesAsync();

        var result = await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);
        result.Success.Should().BeTrue();

        (await _db.ApiKeys.IgnoreQueryFilters().CountAsync(k => k.CreatedBy == userId)).Should().Be(0);
        (await _db.VacationApprovalRules.IgnoreQueryFilters().Where(r => r.ApproverUserId == userId).CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Move_ReassignsOwnedTeamCalendars_ToActingAdmin()
    {
        var (userId, src, dest) = await SeedAsync();
        var admin = userId + 999;
        _db.TeamCalendars.Add(new TeamCalendar { CompanyId = src, OwnerId = userId, IsDeleted = false, Name = "tc" });
        await _db.SaveChangesAsync();

        var result = await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: admin);
        result.Success.Should().BeTrue();

        // Calendar kept (not deleted) but ownership transferred off the moved user to the admin.
        (await _db.TeamCalendars.IgnoreQueryFilters().CountAsync(t => t.OwnerId == userId)).Should().Be(0);
        (await _db.TeamCalendars.IgnoreQueryFilters().CountAsync(t => t.OwnerId == admin)).Should().Be(1);
    }

    [Fact]
    public async Task Move_ResetsScalarFks_AndSetsDestCompany()
    {
        var (userId, _, dest) = await SeedAsync(); // seeds JobTypeId=5, DepartmentId=6, PrimaryShiftTypeId=7, HomeTypeId=8

        var result = await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);
        result.Success.Should().BeTrue();

        _db.ChangeTracker.Clear();
        var after = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        after.JobTypeId.Should().BeNull();
        after.DepartmentId.Should().BeNull();
        after.PrimaryShiftTypeId.Should().BeNull();
        after.HomeTypeId.Should().BeNull();
        after.CompanyId.Should().Be(dest);
    }

    [Fact]
    public async Task Move_RevokesGrants_ReappliesForDest_AndCleansDirectorAndRoleAssignment()
    {
        var (userId, src, dest) = await SeedAsync();
        _db.Grants.Add(new Grant { UserId = userId, GrantTypeId = 1, CompanyId = src, CanOwn = true });
        _db.DirectorCompanies.Add(new DirectorCompany { UserId = userId, CompanyId = src, GrantedBy = 1, IsDeleted = false });
        _db.UserRoleAssignments.Add(new UserRoleAssignment { UserId = userId, RoleTemplateId = 1, CompanyId = src, AssignedByUserId = 1, AssignedAt = DateTime.UtcNow, IsActive = true });
        await _db.SaveChangesAsync();

        var result = await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);
        result.Success.Should().BeTrue();

        // Old-company grants / director mappings / role-assignment records removed.
        (await _db.Grants.IgnoreQueryFilters().CountAsync(g => g.UserId == userId && g.CompanyId == src)).Should().Be(0);
        (await _db.DirectorCompanies.IgnoreQueryFilters().CountAsync(d => d.UserId == userId && d.CompanyId == src)).Should().Be(0);
        (await _db.UserRoleAssignments.IgnoreQueryFilters().CountAsync(a => a.UserId == userId && a.CompanyId == src)).Should().Be(0);
        // Grants re-applied for the destination via the grant service.
        _grants.Verify(g => g.AssignRoleTemplateGrantsAsync(userId, It.IsAny<string>(), It.IsAny<GrantScope>(), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task Move_MigratesAvatarFiles_ToDestFolder()
    {
        var (userId, src, dest) = await SeedAsync();

        // Set AvatarFileName via the tracked entity (so the move's load sees it, not a stale null).
        var u = await _db.Users.FirstAsync(x => x.Id == userId);
        u.AvatarFileName = $"{userId}.jpg";
        await _db.SaveChangesAsync();

        var root = _env.Object.WebRootPath;
        var srcDir = System.IO.Path.Combine(root, "avatars", src.ToString());
        System.IO.Directory.CreateDirectory(srcDir);
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(srcDir, $"{userId}.jpg"), "full");
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(srcDir, $"{userId}_thumb.jpg"), "thumb");

        var result = await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);
        result.Success.Should().BeTrue();

        var destDir = System.IO.Path.Combine(root, "avatars", dest.ToString());
        System.IO.File.Exists(System.IO.Path.Combine(destDir, $"{userId}.jpg")).Should().BeTrue();
        System.IO.File.Exists(System.IO.Path.Combine(destDir, $"{userId}_thumb.jpg")).Should().BeTrue();
        // Old-folder copies removed after commit.
        System.IO.File.Exists(System.IO.Path.Combine(srcDir, $"{userId}.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task Move_WritesAuditForBothCompanies()
    {
        var (userId, src, dest) = await SeedAsync();
        var admin = userId + 999;

        var result = await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: admin);
        result.Success.Should().BeTrue();

        _audit.Verify(a => a.LogUserActionAsync(admin, src, "UserMovedOut", "User", userId, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _audit.Verify(a => a.LogUserActionAsync(admin, dest, "UserMovedIn", "User", userId, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetMoveImpact_CountsMatchSeededData()
    {
        var (userId, _, dest) = await SeedAsync(); // 1 future shift, 1 pending time-off, 1 chore, 1 on-duty, 1 game score

        var impact = await _svc.GetMoveImpactAsync(userId, dest);

        impact.FutureShifts.Should().Be(1);
        impact.PendingOrFutureTimeOff.Should().Be(1);
        impact.FutureChores.Should().Be(1);
        impact.FutureOnDuty.Should().Be(1);
        impact.GameScores.Should().Be(1);
        impact.WillResetJobType.Should().BeTrue();
        impact.WillResetHomeType.Should().BeTrue();
    }

    [Fact]
    public async Task GetMoveImpact_UnknownUser_ReturnsZeroWithWarning()
    {
        var impact = await _svc.GetMoveImpactAsync(userId: 999999, destCompanyId: 1);
        impact.FutureShifts.Should().Be(0);
        impact.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Move_WithRoleTemplate_RecreatesRoleAssignmentScopedToDest()
    {
        var (userId, _, dest) = await SeedAsync();
        var u = await _db.Users.FirstAsync(x => x.Id == userId);
        u.RoleTemplateId = 1; // having a template fires the recreation path
        await _db.SaveChangesAsync();

        var result = await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);
        result.Success.Should().BeTrue();

        var assignments = await _db.UserRoleAssignments.IgnoreQueryFilters().Where(a => a.UserId == userId).ToListAsync();
        assignments.Should().ContainSingle();
        assignments[0].RoleTemplateId.Should().Be(1);
        assignments[0].IsActive.Should().BeTrue();
        // CompanyId comes from the mocked BuildRoleTemplateScopeAsync (GrantScope(CompanyId: 2)) — proves dest scope was used.
        assignments[0].CompanyId.Should().Be(2);
    }

    [Fact]
    public async Task Move_DirectorRole_RecreatesDirectorCompanyAtDestHq()
    {
        var (userId, _, dest) = await SeedAsync();
        var destMoleculeId = (await _db.Companies.IgnoreQueryFilters().FirstAsync(c => c.Id == dest)).MoleculeId;
        var hq = new Company { MoleculeId = destMoleculeId, Name = "HQ", DisplayName = "HQ", IsHeadquarters = true };
        _db.Companies.Add(hq); await _db.SaveChangesAsync();

        var u = await _db.Users.FirstAsync(x => x.Id == userId);
        u.Role = UserRole.Director;
        await _db.SaveChangesAsync();

        var result = await _svc.MoveUserToCompanyAsync(userId, dest, actingAdminId: userId + 999);
        result.Success.Should().BeTrue();

        var dc = await _db.DirectorCompanies.IgnoreQueryFilters().Where(d => d.UserId == userId).ToListAsync();
        dc.Should().ContainSingle();
        dc[0].CompanyId.Should().Be(hq.Id); // recreated at the destination molecule's HQ
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
