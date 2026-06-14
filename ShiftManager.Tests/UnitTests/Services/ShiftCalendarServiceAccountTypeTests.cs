using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Verifies that <see cref="ShiftCalendarService.GetUsersForCalendarAsync"/> excludes
/// GroupUser accounts from the shift roster while retaining Standard and Mil accounts.
/// Uses real SQLite so SQL translation is exercised (not UseInMemoryDatabase).
/// </summary>
public sealed class ShiftCalendarServiceAccountTypeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ShiftCalendarService _service;

    public ShiftCalendarServiceAccountTypeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var logger = Mock.Of<ILogger<ShiftCalendarService>>();
        var companyCacheMock = new Mock<ICompanyCacheService>();
        var localizationMock = new Mock<ICompanyLocalizationService>();
        localizationMock
            .Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);

        _service = new ShiftCalendarService(_db, logger, companyCacheMock.Object, localizationMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task SeedAsync()
    {
        var area = new Area { Id = 1, ProjectId = 1, Name = "Area", DisplayName = "Area" };
        _db.Areas.Add(area);
        var molecule = new Molecule { Id = 1, AreaId = 1, Name = "Mol", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        var company = new Company { Id = 1, MoleculeId = 1, Name = "Co", DisplayName = "Co" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        _db.Users.AddRange(
            new AppUser
            {
                Id = 1, Email = "std@test.com", DisplayName = "Standard",
                CompanyId = 1, IsActive = true, AccountType = AccountType.Standard,
                Role = UserRole.Employee
            },
            new AppUser
            {
                Id = 2, Email = "mil@test.com", DisplayName = "Mil",
                CompanyId = 1, IsActive = true, AccountType = AccountType.Mil,
                Role = UserRole.Employee
            },
            new AppUser
            {
                Id = 3, Email = "grp@test.com", DisplayName = "GroupUser",
                CompanyId = 1, IsActive = true, AccountType = AccountType.GroupUser,
                Role = UserRole.Employee
            });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetUsersForCalendarAsync_ExcludesGroupUser_RetainsStandardAndMil()
    {
        await SeedAsync();

        var result = await _service.GetUsersForCalendarAsync(moleculeId: 1, jobTypeId: null);

        result.Should().HaveCount(2);
        result.Select(u => u.AccountType).Should().NotContain(AccountType.GroupUser);
        result.Select(u => u.AccountType).Should().Contain(AccountType.Standard);
        result.Select(u => u.AccountType).Should().Contain(AccountType.Mil);
    }

    [Fact]
    public async Task GetUsersForCalendarAsync_GroupUserExcluded_ByName()
    {
        await SeedAsync();

        var result = await _service.GetUsersForCalendarAsync(moleculeId: 1, jobTypeId: null);

        result.Select(u => u.DisplayName).Should().NotContain("GroupUser");
        result.Select(u => u.DisplayName).Should().Contain("Standard");
        result.Select(u => u.DisplayName).Should().Contain("Mil");
    }
}
