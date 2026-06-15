using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Migrations;

public class AssignShiftsCollapseSelfHealTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;

    public AssignShiftsCollapseSelfHealTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        // New unified grant (137) + two of the old ones (3 Alhut, 6 Tech).
        _db.GrantTypes.Add(new GrantType { Id = 137, Key = "AssignShifts", NameKey = "x", DescriptionKey = "x", IsSystem = true });
        _db.GrantTypes.Add(new GrantType { Id = 3, Key = "AssignAlhutShifts", NameKey = "x", DescriptionKey = "x", IsSystem = true });
        _db.GrantTypes.Add(new GrantType { Id = 6, Key = "AssignTechShifts", NameKey = "x", DescriptionKey = "x", IsSystem = true });
        _db.RoleTemplates.Add(new RoleTemplate { Id = 7, Key = "MoleculeAdmin", NameKey = "x" });
        // Stale template->old-grant mapping that the seed no longer produces.
        _db.RoleTemplateGrants.Add(new RoleTemplateGrant { RoleTemplateId = 7, GrantTypeId = 6 });
        // A user holding TWO old grants at the SAME molecule scope (must collapse to ONE AssignShifts).
        _db.Users.Add(new AppUser { Id = 50, Email = "u@test", CompanyId = 1, RoleTemplateId = 7, DisplayName = "U" });
        _db.Grants.Add(new Grant { UserId = 50, GrantTypeId = 3, MoleculeId = 9, CanOwn = true, IsAutoGrant = true });
        _db.Grants.Add(new Grant { UserId = 50, GrantTypeId = 6, MoleculeId = 9, CanOwn = true, IsAutoGrant = true });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Heal_DeprecatesOldGrants_RemovesStaleMappings_MigratesUserGrants_Idempotently()
    {
        await AssignShiftsCollapse.HealAsync(_db, NullLogger.Instance);

        // Old grant types marked deprecated.
        (await _db.GrantTypes.Where(g => g.Key == "AssignAlhutShifts" || g.Key == "AssignTechShifts")
            .AllAsync(g => g.IsDeprecated)).Should().BeTrue();
        // Stale template mapping for an old grant removed.
        (await _db.RoleTemplateGrants.AnyAsync(m => m.GrantTypeId == 6)).Should().BeFalse();
        // User's two old grant rows removed.
        (await _db.Grants.AnyAsync(g => g.UserId == 50 && (g.GrantTypeId == 3 || g.GrantTypeId == 6))).Should().BeFalse();
        // Exactly ONE AssignShifts row at the held scope (deduped; job-type pin dropped).
        var migrated = await _db.Grants.Where(g => g.UserId == 50 && g.GrantTypeId == 137).ToListAsync();
        migrated.Should().ContainSingle();
        migrated[0].MoleculeId.Should().Be(9);
        migrated[0].CanOwn.Should().BeTrue();
        migrated[0].JobTypeId.Should().BeNull();

        // Idempotent: a second run is a no-op (still exactly one AssignShifts row).
        await AssignShiftsCollapse.HealAsync(_db, NullLogger.Instance);
        (await _db.Grants.CountAsync(g => g.UserId == 50 && g.GrantTypeId == 137)).Should().Be(1);
    }
}
