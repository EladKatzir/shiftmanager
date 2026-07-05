using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Issue 3: moving a company to another molecule. The move deletes shift scheduling data and chores,
/// resets molecule-scoped per-user config, rebuilds grants, and repoints Company.MoleculeId — while
/// KEEPING vacations (TimeOffRequest) and user accounts. Mirrors the user-move transactional idiom.
/// </summary>
public class CompanyMoleculeTransferServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IConcurrencyService> _concurrency = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly CompanyMoleculeTransferService _svc;

    public CompanyMoleculeTransferServiceTests()
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
            .ReturnsAsync(new GrantScope(CompanyId: 1));
        _grants.Setup(g => g.AssignRoleTemplateGrantsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<GrantScope>(), It.IsAny<int?>()))
            .ReturnsAsync(1);

        _svc = new CompanyMoleculeTransferService(_db, _grants.Object, _concurrency.Object, _audit.Object,
            NullLogger<CompanyMoleculeTransferService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private sealed record Seeded(int CompanyId, int SourceMoleculeId, int TargetMoleculeId, int UserId,
        int ShiftInstanceId, int VacationId, int ChoreId, int CategoryId);

    private async Task<Seeded> SeedAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" }; _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" }; _db.Areas.Add(area); await _db.SaveChangesAsync();
        var srcMol = new Molecule { AreaId = area.Id, Name = "Src", DisplayName = "Src", Type = MoleculeType.Workforce };
        var dstMol = new Molecule { AreaId = area.Id, Name = "Dst", DisplayName = "Dst", Type = MoleculeType.Workforce };
        _db.Molecules.AddRange(srcMol, dstMol); await _db.SaveChangesAsync();

        var company = new Company { MoleculeId = srcMol.Id, Name = "Co", DisplayName = "Co" };
        _db.Companies.Add(company); await _db.SaveChangesAsync();

        var user = new AppUser
        {
            CompanyId = company.Id, Email = "u@x.com", DisplayName = "U",
            JobTypeId = 5, DepartmentId = 6, HomeTypeId = 8, RoleTemplateId = null, Role = UserRole.Employee,
            PasswordHash = System.Array.Empty<byte>(), PasswordSalt = System.Array.Empty<byte>()
        };
        _db.Users.Add(user); await _db.SaveChangesAsync();

        var today = System.DateOnly.FromDateTime(System.DateTime.UtcNow);
        var st = new ShiftType { Key = "MORNING", Start = new System.TimeOnly(8, 0), End = new System.TimeOnly(16, 0), MoleculeId = srcMol.Id, CompanyId = company.Id };
        _db.ShiftTypes.Add(st); await _db.SaveChangesAsync();
        var inst = new ShiftInstance { CompanyId = company.Id, ShiftTypeId = st.Id, WorkDate = today.AddDays(3), StaffingRequired = 1 };
        _db.ShiftInstances.Add(inst); await _db.SaveChangesAsync();
        _db.ShiftAssignments.Add(new ShiftAssignment { CompanyId = company.Id, UserId = user.Id, ShiftInstanceId = inst.Id });

        var vacation = new TimeOffRequest { CompanyId = company.Id, UserId = user.Id, Status = RequestStatus.Approved, StartDate = today.AddDays(10), EndDate = today.AddDays(12), Type = TimeOffType.Vacation };
        _db.TimeOffRequests.Add(vacation);
        var chore = new Chore { CompanyId = company.Id, UserId = user.Id, Date = today.AddDays(2), MoleculeId = srcMol.Id, Title = "c", CreatedBy = user.Id, CreatedAt = System.DateTime.UtcNow };
        _db.Chores.Add(chore);
        var category = new ShiftCategory { MoleculeId = srcMol.Id, Name = "Cat", DisplayName = "Cat" };
        _db.ShiftCategories.Add(category); await _db.SaveChangesAsync();
        _db.UserShiftCategories.Add(new UserShiftCategory { UserId = user.Id, ShiftCategoryId = category.Id });
        _db.Grants.Add(new Grant { UserId = user.Id, GrantTypeId = 1, MoleculeId = srcMol.Id });
        await _db.SaveChangesAsync();

        return new Seeded(company.Id, srcMol.Id, dstMol.Id, user.Id, inst.Id, vacation.Id, chore.Id, category.Id);
    }

    [Fact]
    public async Task Move_DeletesShifts_KeepsVacations_AndSetsMolecule()
    {
        var s = await SeedAsync();

        var result = await _svc.MoveCompanyToMoleculeAsync(s.CompanyId, s.TargetMoleculeId, actingAdminId: 999);

        result.Success.Should().BeTrue();
        var company = await _db.Companies.IgnoreQueryFilters().FirstAsync(c => c.Id == s.CompanyId);
        company.MoleculeId.Should().Be(s.TargetMoleculeId, "the company now belongs to the target molecule");

        (await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(a => a.CompanyId == s.CompanyId)).Should().Be(0, "existing shifts are deleted");
        (await _db.ShiftInstances.IgnoreQueryFilters().CountAsync(i => i.CompanyId == s.CompanyId)).Should().Be(0);
        (await _db.Chores.IgnoreQueryFilters().CountAsync(c => c.CompanyId == s.CompanyId)).Should().Be(0, "chores are molecule-tied and deleted");

        var vacation = await _db.TimeOffRequests.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == s.VacationId);
        vacation.Should().NotBeNull("vacations must survive the move");
        vacation!.Status.Should().Be(RequestStatus.Approved, "vacations are untouched");
    }

    [Fact]
    public async Task Move_ResetsUserMoleculeConfig_AndRebuildsGrants()
    {
        var s = await SeedAsync();

        await _svc.MoveCompanyToMoleculeAsync(s.CompanyId, s.TargetMoleculeId, actingAdminId: 999);
        _db.ChangeTracker.Clear(); // read true DB state, not stale tracked entities (ExecuteUpdate bypasses the tracker)

        var user = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == s.UserId);
        user.CompanyId.Should().Be(s.CompanyId, "the user stays in the company, which moved");
        user.JobTypeId.Should().BeNull("old-molecule job type is reset");
        user.DepartmentId.Should().BeNull();
        user.HomeTypeId.Should().BeNull();
        (await _db.UserShiftCategories.CountAsync(uc => uc.UserId == s.UserId)).Should().Be(0, "old-molecule category memberships are cleared");

        // Grants for the user were rebuilt: old grant deleted, template re-applied for the new hierarchy.
        _grants.Verify(g => g.AssignRoleTemplateGrantsAsync(s.UserId, It.IsAny<string>(), It.IsAny<GrantScope>(), 999), Times.Once);
        (await _db.Grants.IgnoreQueryFilters().CountAsync(g => g.UserId == s.UserId && g.MoleculeId == s.SourceMoleculeId))
            .Should().Be(0, "grants scoped to the old molecule are removed");
    }

    [Fact]
    public async Task Move_ToSameMolecule_ReturnsError_AndChangesNothing()
    {
        var s = await SeedAsync();

        var result = await _svc.MoveCompanyToMoleculeAsync(s.CompanyId, s.SourceMoleculeId, actingAdminId: 999);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MoveSameMolecule");
        (await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(a => a.CompanyId == s.CompanyId)).Should().Be(1, "a no-op move must not delete shifts");
    }

    [Fact]
    public async Task Move_GrantRebuildThrows_RollsBackEverything()
    {
        var s = await SeedAsync();
        _grants.Setup(g => g.AssignRoleTemplateGrantsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<GrantScope>(), It.IsAny<int?>()))
            .ThrowsAsync(new System.InvalidOperationException("boom"));

        var result = await _svc.MoveCompanyToMoleculeAsync(s.CompanyId, s.TargetMoleculeId, actingAdminId: 999);
        _db.ChangeTracker.Clear(); // the rollback undid the DB write; detach the stale tracked company so we re-read the DB

        result.Success.Should().BeFalse();
        var company = await _db.Companies.IgnoreQueryFilters().FirstAsync(c => c.Id == s.CompanyId);
        company.MoleculeId.Should().Be(s.SourceMoleculeId, "a failed move rolls back the molecule change");
        (await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(a => a.CompanyId == s.CompanyId)).Should().Be(1, "a failed move rolls back the shift deletions");
        (await _db.Chores.IgnoreQueryFilters().CountAsync(c => c.CompanyId == s.CompanyId)).Should().Be(1);
    }

    [Fact]
    public async Task GetMoveImpact_CountsShiftsAndUsers()
    {
        var s = await SeedAsync();

        var impact = await _svc.GetMoveImpactAsync(s.CompanyId, s.TargetMoleculeId);

        impact.TargetMoleculeValid.Should().BeTrue();
        impact.ShiftInstances.Should().Be(1);
        impact.ShiftAssignments.Should().Be(1);
        impact.Chores.Should().Be(1);
        impact.AffectedUsers.Should().Be(1);
        impact.CompanyScopedShiftTypes.Should().Be(1);
    }
}
