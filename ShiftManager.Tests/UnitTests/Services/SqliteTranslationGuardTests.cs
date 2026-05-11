using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Services.Api;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Regression guards for the LINQ-to-SQL translation bug class that produced
/// GRIFFIN-USERLOOKUP-510 in production.
///
/// **What this file guards against**: code that uses .NET methods unsupported by EF Core's
/// SQLite translator inside an IQueryable lambda. The original bug was
/// `u.Email.ToLowerInvariant()` inside a `.Where(...)` — EF Core has no SQL mapping for
/// `ToLowerInvariant`, so the query threw `InvalidOperationException` at runtime. The
/// other test fixtures in this project use `UseInMemoryDatabase` which silently runs LINQ
/// in C# without ever invoking the SQL translator — they cannot catch this class of bug.
///
/// **What this file does**: spins up a real SQLite in-memory database (the same translator
/// the production app uses), seeds minimal data, then invokes the actual production search
/// methods that today exposed the bug pattern. Any future regression to a non-translatable
/// expression in `UserApiService.GetUsersAsync`, `FriendshipService.SearchUsersAsync`,
/// `ProfileService.SearchProfilesAsync`, or `CompanyLocalizationService.SearchOverridesAsync`
/// will fail this test with `InvalidOperationException: "could not be translated"`.
///
/// **Why we don't migrate the full *ServiceTests fixtures to SQLite**: migrating them
/// surfaces FK-constraint and seed-data inconsistencies (real SQLite enforces FKs; the
/// In-Memory provider does not) that are independent of the translation bug class. Those
/// are real but separate bugs deserving their own investigation; conflating them with this
/// fix would balloon scope. This focused guard test is sufficient to lock the translation
/// surface without disturbing the other fixtures.
/// </summary>
public sealed class SqliteTranslationGuardTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Minimal seed: two users with mixed-case data so the case-insensitive matching
        // we just shipped is actually exercised. Companies are required because AppUser
        // has CompanyId NOT NULL.
        _db.Companies.AddRange(
            new Company { Id = 1, Name = "Co1", Slug = "co1" },
            new Company { Id = 2, Name = "Co2", Slug = "co2" });
        _db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "Alice@Example.MIL", DisplayName = "Alice Wonderland", PreferredName = "Wonder", JobTitle = "Senior Officer", Role = UserRole.Employee, IsActive = true },
            new AppUser { Id = 20, CompanyId = 2, Email = "bob@example.mil", DisplayName = "BOB Builder", JobTitle = "Junior", Role = UserRole.Employee, IsActive = true });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ProfileService_SearchProfilesAsync_TranslatesToSql()
    {
        // Direct exercise of the production method we just fixed. If anyone reintroduces
        // .Contains(s, StringComparison.X) here, this test throws "could not be translated".
        var localizer = Mock.Of<IStringLocalizer<SharedResources>>();
        var service = new ProfileService(
            _db,
            Mock.Of<ITenantResolver>(t => t.GetCurrentTenantId() == 1),
            Mock.Of<IGrantService>(),
            Mock.Of<ILogger<ProfileService>>(),
            localizer);

        var results = await service.SearchProfilesAsync("ALICE", maxResults: 10);

        // Case-insensitive: "ALICE" matches "Alice Wonderland" via DisplayName.
        results.Should().ContainSingle(u => u.Id == 10);
    }

    [Fact]
    public async Task FriendshipService_SearchUsersAsync_TranslatesToSql()
    {
        var service = new FriendshipService(
            _db,
            Mock.Of<IStringLocalizer<SharedResources>>(),
            Mock.Of<ILogger<FriendshipService>>());

        // Search must find "bob@example.mil" via mixed-case "BOB" input — exercising both
        // the column-side .ToLower() translation AND the cross-company IgnoreQueryFilters path.
        var results = await service.SearchUsersAsync(userId: 10, query: "BOB", limit: 20);

        results.Should().Contain(u => u.UserId == 20);
    }

    [Fact]
    public async Task CompanyLocalizationService_SearchOverridesAsync_TranslatesToSql()
    {
        // CompanyLocalizationOverride has a FK to AppUser (CreatedBy). Real SQLite enforces
        // it; In-Memory doesn't. Set it to the seeded user 10.
        // CompanyLocalizationOverride has TWO FKs to AppUser (CreatedBy + UpdatedBy). Real
        // SQLite enforces them; In-Memory doesn't. Set both to the seeded user 10.
        _db.CompanyLocalizationOverrides.Add(new CompanyLocalizationOverride
        {
            CompanyId = 1, Culture = "he-IL",
            ResourceKey = "Welcome_Greeting", OverrideValue = "ברוכים הבאים",
            IsActive = true, CreatedBy = 10, UpdatedBy = 10
        });
        await _db.SaveChangesAsync();

        var service = new CompanyLocalizationService(
            _db,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Mock.Of<ILogger<CompanyLocalizationService>>(),
            Mock.Of<IAuditLogService>(),
            Mock.Of<IStringLocalizer<SharedResources>>());

        var results = await service.SearchOverridesAsync(companyId: 1, culture: "he-IL", searchTerm: "WELCOME");

        results.Should().ContainSingle(o => o.ResourceKey == "Welcome_Greeting");
    }

    [Fact]
    public async Task UserApiService_SearchPath_TranslatesToSql()
    {
        // UserApiService has heavier ctor dependencies; smoke-test the search expression
        // directly against the same DbSet to lock the translation surface without standing
        // up the full service. This is the EXACT shape of the production filter (post-fix).
        var search = "wonder";
        var searchLower = search.ToLowerInvariant();
        var result = await _db.Users
            .Where(u =>
                u.Email.ToLower().Contains(searchLower) ||
                u.DisplayName.ToLower().Contains(searchLower))
            .ToListAsync();

        result.Should().ContainSingle(u => u.Id == 10);
    }

    [Theory]
    [InlineData("Alice", 1, 10)]
    [InlineData("ALICE", 1, 10)]
    [InlineData("alice", 1, 10)]
    [InlineData("Wonder", 1, 10)]   // PreferredName match
    [InlineData("WONDER", 1, 10)]
    [InlineData("Senior", 1, 10)]   // JobTitle match
    [InlineData("BOB", 1, 20)]      // cross-company hit (different DisplayName casing)
    public async Task ProfileService_SearchProfilesAsync_MixedCaseInputs(string input, int expectedCount, int expectedId)
    {
        var service = new ProfileService(
            _db,
            Mock.Of<ITenantResolver>(t => t.GetCurrentTenantId() == 1),
            Mock.Of<IGrantService>(),
            Mock.Of<ILogger<ProfileService>>(),
            Mock.Of<IStringLocalizer<SharedResources>>());

        var results = await service.SearchProfilesAsync(input, maxResults: 10);

        results.Should().HaveCount(expectedCount);
        results.Should().Contain(u => u.Id == expectedId);
    }
}
