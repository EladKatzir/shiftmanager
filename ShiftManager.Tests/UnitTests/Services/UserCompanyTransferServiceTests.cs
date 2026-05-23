using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
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

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}
