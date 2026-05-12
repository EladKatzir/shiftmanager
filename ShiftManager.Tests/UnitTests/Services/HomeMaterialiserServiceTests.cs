using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class HomeMaterialiserServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly HomeMaterialiserService _service;

    private const int TestMoleculeId = 1;
    private const int TestCompanyId = 1;
    private const int TestUserId = 10;

    public HomeMaterialiserServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        // Seed hierarchy
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "Area1", DisplayName = "Area 1" });
        _db.Molecules.Add(new Molecule { Id = TestMoleculeId, AreaId = 1, Name = "Mol1", DisplayName = "Molecule 1" });
        _db.Companies.Add(new Company { Id = TestCompanyId, MoleculeId = TestMoleculeId, Name = "Co1", DisplayName = "Company 1" });
        _db.Users.Add(new AppUser
        {
            Id = TestUserId,
            Email = "user@test.com",
            DisplayName = "User",
            CompanyId = TestCompanyId,
            IsActive = true,
            Role = UserRole.Employee
        });
        _db.SaveChanges();

        var homeTypeService = new HomeTypeService(_db, Mock.Of<ILogger<HomeTypeService>>());
        _service = new HomeMaterialiserService(
            _db,
            Mock.Of<ILogger<HomeMaterialiserService>>(),
            homeTypeService,
            hub: null);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    [Fact]
    public async Task Sync_ApprovedAfter_CreatesHomePmAndHomeAm()
    {
        // Approve an After for May 7 → expect HOME_PM on May 7 + HOME_AM on May 8
        var req = new TimeOffRequest
        {
            Id = 100,
            CompanyId = TestCompanyId,
            UserId = TestUserId,
            StartDate = new DateOnly(2026, 5, 7),
            EndDate = new DateOnly(2026, 5, 7),
            Type = TimeOffType.After,
            Status = RequestStatus.Approved
        };
        _db.TimeOffRequests.Add(req);
        await _db.SaveChangesAsync();

        await _service.SyncMaterialisedHomeRowsAsync(100);

        var rows = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
            .Where(sa => sa.SourceTimeOffRequestId == 100)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().Contain(r =>
            r.ShiftInstance.WorkDate == new DateOnly(2026, 5, 7)
            && r.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
            && r.UserId == TestUserId);
        rows.Should().Contain(r =>
            r.ShiftInstance.WorkDate == new DateOnly(2026, 5, 8)
            && r.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM
            && r.UserId == TestUserId);
    }

    [Fact]
    public async Task Sync_IsIdempotent_SecondCallNoOps()
    {
        var req = new TimeOffRequest
        {
            Id = 101,
            CompanyId = TestCompanyId,
            UserId = TestUserId,
            StartDate = new DateOnly(2026, 5, 7),
            EndDate = new DateOnly(2026, 5, 7),
            Type = TimeOffType.After,
            Status = RequestStatus.Approved
        };
        _db.TimeOffRequests.Add(req);
        await _db.SaveChangesAsync();

        await _service.SyncMaterialisedHomeRowsAsync(101);
        var firstCount = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.SourceTimeOffRequestId == 101);

        await _service.SyncMaterialisedHomeRowsAsync(101);
        var secondCount = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.SourceTimeOffRequestId == 101);

        firstCount.Should().Be(2);
        secondCount.Should().Be(2); // unchanged
    }

    [Fact]
    public async Task Sync_DeclinedAfter_DeletesAllRows()
    {
        var req = new TimeOffRequest
        {
            Id = 102,
            CompanyId = TestCompanyId,
            UserId = TestUserId,
            StartDate = new DateOnly(2026, 5, 7),
            EndDate = new DateOnly(2026, 5, 7),
            Type = TimeOffType.After,
            Status = RequestStatus.Approved
        };
        _db.TimeOffRequests.Add(req);
        await _db.SaveChangesAsync();

        // First materialise
        await _service.SyncMaterialisedHomeRowsAsync(102);
        (await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(sa => sa.SourceTimeOffRequestId == 102))
            .Should().Be(2);

        // Now decline + re-sync
        req.Status = RequestStatus.Declined;
        await _db.SaveChangesAsync();
        await _service.SyncMaterialisedHomeRowsAsync(102);

        (await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(sa => sa.SourceTimeOffRequestId == 102))
            .Should().Be(0);
    }

    [Fact]
    public async Task Sync_ApprovedVacation_CreatesHomePerDayPlusHomeAmTail()
    {
        // 3-day vacation May 7-9 → HOME on 5/7, 5/8, 5/9 + HOME_AM on 5/10
        var req = new TimeOffRequest
        {
            Id = 103,
            CompanyId = TestCompanyId,
            UserId = TestUserId,
            StartDate = new DateOnly(2026, 5, 7),
            EndDate = new DateOnly(2026, 5, 9),
            Type = TimeOffType.Vacation,
            Status = RequestStatus.Approved
        };
        _db.TimeOffRequests.Add(req);
        await _db.SaveChangesAsync();

        await _service.SyncMaterialisedHomeRowsAsync(103);

        var rows = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
            .Where(sa => sa.SourceTimeOffRequestId == 103)
            .ToListAsync();

        rows.Should().HaveCount(4);
        rows.Where(r => r.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME).Should().HaveCount(3);
        rows.Where(r => r.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM).Should().HaveCount(1);
    }
}
