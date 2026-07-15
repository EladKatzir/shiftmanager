using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for DeskTeamViewService — owner-scoped CRUD for saved dynamic (TargetCompany × JobType)
/// "team table" views used by the /Calendar/Team page.
///
/// Deliberately a SEPARATE entity/service from TeamCalendar/TeamCalendarService: TeamCalendar's list
/// endpoint (api/team-calendars) returns ALL of an owner's calendars, so reusing it for these dynamic
/// filter views would leak them into the /MyTeam page. TeamCalendar/TeamCalendarService/MyTeam are not
/// touched by this change.
///
/// DTV-01: CreateAsync then ListForOwnerAsync returns the view for its owner with correct fields
/// DTV-02: A view owned by user A is NOT returned in user B's ListForOwnerAsync (owner isolation)
/// DTV-03: DeleteAsync soft-deletes — excluded from ListForOwnerAsync afterward
/// DTV-04: RenameAsync updates the Name for the owner
/// </summary>
public class DeskTeamViewServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;

    private const int TestCompanyId = 1;
    private const int OwnerAId = 10;
    private const int OwnerBId = 11;

    public DeskTeamViewServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    /// <summary>
    /// Builds a DeskTeamViewService whose "current user" (ClaimTypes.NameIdentifier) is ownerId
    /// and whose current tenant (ITenantResolver) is companyId — mirrors ChoreServiceTests' pattern
    /// for services that resolve the caller from HttpContext claims instead of a method parameter.
    /// </summary>
    private DeskTeamViewService CreateService(int ownerId, int companyId = TestCompanyId)
    {
        var tenantResolverMock = new Mock<ITenantResolver>();
        tenantResolverMock.Setup(x => x.GetCurrentTenantId()).Returns(companyId);

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, ownerId.ToString()),
            new Claim("CompanyId", companyId.ToString())
        }, "TestAuth"));
        httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        return new DeskTeamViewService(_db, tenantResolverMock.Object, httpContextAccessorMock.Object);
    }

    /// <summary>
    /// DTV-01: CreateAsync persists a DeskTeamView; ListForOwnerAsync returns it for its owner
    /// with the correct TargetCompanyId/JobTypeId.
    /// </summary>
    [Fact]
    public async Task Create_ThenList_ReturnsOwnerScopedView()
    {
        var service = CreateService(OwnerAId);

        var v = await service.CreateAsync(targetCompanyId: 5, jobTypeId: 2, name: "Alhut Tzafona");
        var mine = await service.ListForOwnerAsync();

        mine.Should().Contain(x => x.Id == v.Id);
        v.TargetCompanyId.Should().Be(5);
        v.JobTypeId.Should().Be(2);
        v.Name.Should().Be("Alhut Tzafona");
        v.OwnerId.Should().Be(OwnerAId);
        v.CompanyId.Should().Be(TestCompanyId);
        v.IsDeleted.Should().BeFalse();
    }

    /// <summary>
    /// DTV-02: A view owned by user A must not appear in user B's ListForOwnerAsync.
    /// </summary>
    [Fact]
    public async Task ListForOwnerAsync_ExcludesOtherOwnersViews()
    {
        var serviceA = CreateService(OwnerAId);
        var serviceB = CreateService(OwnerBId);

        var viewA = await serviceA.CreateAsync(targetCompanyId: 5, jobTypeId: 2, name: "A's Table");

        var ownedByB = await serviceB.ListForOwnerAsync();

        ownedByB.Should().NotContain(x => x.Id == viewA.Id);
    }

    /// <summary>
    /// DTV-03: DeleteAsync soft-deletes — the row survives with IsDeleted=true but drops out of the list.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_SoftDeletes_ExcludedFromList()
    {
        var service = CreateService(OwnerAId);
        var v = await service.CreateAsync(targetCompanyId: 5, jobTypeId: 2, name: "To Delete");

        await service.DeleteAsync(v.Id);

        var mine = await service.ListForOwnerAsync();
        mine.Should().NotContain(x => x.Id == v.Id);

        var fromDb = await _db.DeskTeamViews.IgnoreQueryFilters().FirstAsync(x => x.Id == v.Id);
        fromDb.IsDeleted.Should().BeTrue();
    }

    /// <summary>
    /// DTV-04: RenameAsync updates the Name for the owning user.
    /// </summary>
    [Fact]
    public async Task RenameAsync_UpdatesName_ForOwner()
    {
        var service = CreateService(OwnerAId);
        var v = await service.CreateAsync(targetCompanyId: 5, jobTypeId: 2, name: "Old Name");

        await service.RenameAsync(v.Id, "New Name");

        var mine = await service.ListForOwnerAsync();
        mine.Single(x => x.Id == v.Id).Name.Should().Be("New Name");
    }
}
