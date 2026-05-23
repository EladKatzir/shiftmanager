using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Integration tests for <see cref="DistributionListService"/> against a real SQLite engine (not the
/// In-Memory provider) so the molecule-scoped IgnoreQueryFilters() queries, the case-insensitive NOCASE
/// unique index, and the CompanyIdInterceptor stamping are all genuinely exercised.
///
/// FKs are relaxed (Foreign Keys=False) — the established from-scratch-fixture pattern — so we only seed
/// what the queries read (Companies with MoleculeId + Users), not the full Project→Area→Molecule chain.
/// The CompanyIdInterceptor is wired with a fake ITenantResolver so creates get the creator's CompanyId
/// stamped exactly as in production.
/// </summary>
public sealed class DistributionListServiceTests : IAsyncLifetime
{
    private const int MoleculeA = 100;
    private const int MoleculeB = 200;
    private const int TenantCompanyId = 1; // the acting manager's company (stamped onto created lists)

    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();

        // Wire the CompanyIdInterceptor with a fake tenant resolver (company 1), like production DI does.
        var tenant = new Mock<ITenantResolver>();
        tenant.Setup(t => t.HasTenant()).Returns(true);
        tenant.Setup(t => t.GetCurrentTenantId()).Returns(TenantCompanyId);
        var sp = new ServiceCollection().AddScoped(_ => tenant.Object).BuildServiceProvider();
        var interceptor = new CompanyIdInterceptor(sp, new ConfigurationBuilder().Build(), Mock.Of<ILogger<CompanyIdInterceptor>>());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Two companies in molecule A (cross-company members live here), one in molecule B (out of scope).
        _db.Companies.AddRange(
            new Company { Id = 1, Name = "Alpha", Slug = "alpha", MoleculeId = MoleculeA },
            new Company { Id = 2, Name = "Bravo", Slug = "bravo", MoleculeId = MoleculeA },
            new Company { Id = 3, Name = "Charlie", Slug = "charlie", MoleculeId = MoleculeB });
        _db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "Avi", Role = UserRole.Employee, IsActive = true },
            new AppUser { Id = 11, CompanyId = 2, Email = "b@x.mil", DisplayName = "Ben", Role = UserRole.Employee, IsActive = true },
            new AppUser { Id = 12, CompanyId = 3, Email = "c@x.mil", DisplayName = "Carol", Role = UserRole.Employee, IsActive = true },
            new AppUser { Id = 99, CompanyId = 1, Email = "mgr@x.mil", DisplayName = "Manager", Role = UserRole.Manager, IsActive = true });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private DistributionListService NewService(bool canManage = true)
    {
        var grant = new Mock<IGrantService>();
        grant.Setup(g => g.HasGrantWithScopeAsync(
                It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(canManage);
        return new DistributionListService(_db, grant.Object, Mock.Of<ILogger<DistributionListService>>());
    }

    [Fact]
    public async Task CreateAsync_CrossCompanyMembers_PersistsWithStampedCompanyId()
    {
        var svc = NewService();

        var result = await svc.CreateAsync(actingUserId: 99, moleculeId: MoleculeA, name: "Blue-qualified", userIds: new[] { 10, 11 });

        result.Ok.Should().BeTrue();
        var list = await _db.DistributionLists.IgnoreQueryFilters().Include(l => l.Members).SingleAsync();
        list.MoleculeId.Should().Be(MoleculeA);
        list.CompanyId.Should().Be(TenantCompanyId, "the interceptor stamps the creator's company");
        list.CreatedBy.Should().Be(99);
        list.Members.Select(m => m.UserId).Should().BeEquivalentTo(new[] { 10, 11 });
    }

    [Fact]
    public async Task CreateAsync_MemberOutsideMolecule_ReturnsInvalidMembers()
    {
        var svc = NewService();

        // User 12 belongs to company 3 (molecule B), so it must be rejected for a molecule-A list.
        var result = await svc.CreateAsync(99, MoleculeA, "Mixed", new[] { 10, 12 });

        result.Outcome.Should().Be(DistributionListOutcome.InvalidMembers);
        (await _db.DistributionLists.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameCaseInsensitive_ReturnsNameTaken()
    {
        var svc = NewService();
        (await svc.CreateAsync(99, MoleculeA, "Blue", new[] { 10 })).Ok.Should().BeTrue();

        var dup = await svc.CreateAsync(99, MoleculeA, "  bLuE  ", new[] { 11 });

        dup.Outcome.Should().Be(DistributionListOutcome.NameTaken);
    }

    [Fact]
    public async Task CreateAsync_SameNameDifferentMolecule_IsAllowed()
    {
        var svc = NewService();
        (await svc.CreateAsync(99, MoleculeA, "Shared", new[] { 10 })).Ok.Should().BeTrue();

        // Same name in a different molecule is fine — uniqueness is per molecule.
        var other = await svc.CreateAsync(99, MoleculeB, "Shared", new[] { 12 });

        other.Ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("", DistributionListOutcome.NameRequired)]
    [InlineData("   ", DistributionListOutcome.NameRequired)]
    public async Task CreateAsync_EmptyName_ReturnsNameRequired(string name, DistributionListOutcome expected)
    {
        var result = await NewService().CreateAsync(99, MoleculeA, name, new[] { 10 });
        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task CreateAsync_NoMembers_ReturnsNoMembers()
    {
        var result = await NewService().CreateAsync(99, MoleculeA, "Empty", System.Array.Empty<int>());
        result.Outcome.Should().Be(DistributionListOutcome.NoMembers);
    }

    [Fact]
    public async Task CreateAsync_WithoutGrant_ReturnsForbidden()
    {
        var result = await NewService(canManage: false).CreateAsync(99, MoleculeA, "Blue", new[] { 10 });

        result.Outcome.Should().Be(DistributionListOutcome.Forbidden);
        (await _db.DistributionLists.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_ChangesNameAndMembers_AndDeriversMoleculeFromList()
    {
        var svc = NewService();
        var created = await svc.CreateAsync(99, MoleculeA, "Original", new[] { 10 });

        var result = await svc.UpdateAsync(99, created.ListId, "Renamed", new[] { 10, 11 });

        result.Ok.Should().BeTrue();
        var list = await _db.DistributionLists.IgnoreQueryFilters().Include(l => l.Members).SingleAsync();
        list.Name.Should().Be("Renamed");
        list.Members.Select(m => m.UserId).Should().BeEquivalentTo(new[] { 10, 11 });
    }

    [Fact]
    public async Task DeleteAsync_RemovesListAndMembers()
    {
        var svc = NewService();
        var created = await svc.CreateAsync(99, MoleculeA, "Temp", new[] { 10, 11 });

        var result = await svc.DeleteAsync(99, created.ListId);

        result.Ok.Should().BeTrue();
        (await _db.DistributionLists.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await _db.DistributionListMembers.IgnoreQueryFilters().CountAsync()).Should().Be(0, "cascade removes members");
    }

    [Fact]
    public async Task GetListsForMoleculeAsync_ReturnsOnlyThatMoleculeWithMemberCounts()
    {
        var svc = NewService();
        await svc.CreateAsync(99, MoleculeA, "ListA", new[] { 10, 11 });
        await svc.CreateAsync(99, MoleculeB, "ListB", new[] { 12 });

        var listsA = await svc.GetListsForMoleculeAsync(MoleculeA);

        listsA.Should().ContainSingle();
        listsA[0].Name.Should().Be("ListA");
        listsA[0].MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task GetListsWithMembersAsync_ReturnsMembersForSelectedListsOnly()
    {
        var svc = NewService();
        var a = await svc.CreateAsync(99, MoleculeA, "Skilled", new[] { 10, 11 });

        var result = await svc.GetListsWithMembersAsync(new[] { a.ListId }, MoleculeA);

        result.Should().ContainSingle();
        result[0].MemberUserIds.Should().BeEquivalentTo(new[] { 10, 11 });
    }
}
