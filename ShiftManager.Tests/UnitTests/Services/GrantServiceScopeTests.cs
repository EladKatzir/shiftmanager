using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

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
