using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Middleware;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.UnitTests.Security;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Phase 1 (language learning) tests.
///
/// CS-01: CultureScope switches both CurrentCulture + CurrentUICulture, restores on dispose.
/// CS-02: CultureScope with null/blank/unknown name is a no-op (ambient culture preserved).
/// PL-01: LearnAsync persists PreferredLanguage when it differs (returns true).
/// PL-02: LearnAsync is a no-op when PreferredLanguage already matches (returns false).
/// PL-03: LearnAsync ignores an unauthenticated request (returns false, no write).
/// PL-04: LearnAsync ignores a non-integer NameIdentifier claim (returns false).
/// PL-05: InvokeAsync skips LearnAsync when the endpoint already set
///        HttpContext.Items["LanguageExplicitlySet"] on this request (doesn't clobber the
///        endpoint's explicit write with the request's stale resolved culture).
/// PL-06: InvokeAsync still learns when that flag is absent (companion — guards against
///        over-blocking).
/// PL-07: LearnAsync updates a switched-company user's OWN row (IgnoreQueryFilters) even though
///        the tenant query filter would otherwise scope db.Users to the CURRENTLY VIEWED company,
///        not the caller's home CompanyId.
/// </summary>
public class PreferredLanguagePhase1Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public PreferredLanguagePhase1Tests()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        // No ITenantResolver → no Users query filter (mirrors existing service unit tests).
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void SeedUser(int id, string? preferredLanguage)
    {
        _db.Users.Add(new AppUser
        {
            Id = id,
            CompanyId = 1,
            Email = $"user{id}@test.local",
            DisplayName = $"User {id}",
            PreferredLanguage = preferredLanguage
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private static DefaultHttpContext AuthedContext(string nameIdentifier)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, nameIdentifier) },
            authenticationType: "TestAuth");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private string? StoredLanguage(int id) =>
        _db.Users.AsNoTracking().First(u => u.Id == id).PreferredLanguage;

    [Fact] // CS-01
    public void CultureScope_SwitchesAndRestores()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");

            using (new CultureScope("he-IL"))
            {
                CultureInfo.CurrentCulture.Name.Should().Be("he-IL");
                CultureInfo.CurrentUICulture.Name.Should().Be("he-IL");
            }

            CultureInfo.CurrentCulture.Name.Should().Be("en-US");
            CultureInfo.CurrentUICulture.Name.Should().Be("en-US");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Theory] // CS-02
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-culture-xyz")]
    public void CultureScope_NullOrUnknown_IsNoOp(string? name)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");

            using (new CultureScope(name))
            {
                CultureInfo.CurrentUICulture.Name.Should().Be("en-US");
            }

            CultureInfo.CurrentUICulture.Name.Should().Be("en-US");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact] // PL-01
    public async Task LearnAsync_PersistsWhenDifferent()
    {
        SeedUser(5, preferredLanguage: null);
        var ctx = AuthedContext("5");

        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("he-IL");
            var updated = await PreferredLanguageLearningMiddleware.LearnAsync(ctx, _db, CancellationToken.None);

            updated.Should().BeTrue();
            StoredLanguage(5).Should().Be("he-IL");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact] // PL-02
    public async Task LearnAsync_NoOpWhenAlreadyMatches()
    {
        SeedUser(6, preferredLanguage: "en-US");
        var ctx = AuthedContext("6");

        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var updated = await PreferredLanguageLearningMiddleware.LearnAsync(ctx, _db, CancellationToken.None);

            updated.Should().BeFalse();
            StoredLanguage(6).Should().Be("en-US");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact] // PL-03
    public async Task LearnAsync_IgnoresUnauthenticated()
    {
        SeedUser(7, preferredLanguage: null);
        var ctx = new DefaultHttpContext(); // anonymous (no Identity authenticated)

        var updated = await PreferredLanguageLearningMiddleware.LearnAsync(ctx, _db, CancellationToken.None);

        updated.Should().BeFalse();
        StoredLanguage(7).Should().BeNull();
    }

    [Fact] // PL-04
    public async Task LearnAsync_IgnoresNonIntegerClaim()
    {
        SeedUser(8, preferredLanguage: null);
        var ctx = AuthedContext("not-an-int");

        var updated = await PreferredLanguageLearningMiddleware.LearnAsync(ctx, _db, CancellationToken.None);

        updated.Should().BeFalse();
        StoredLanguage(8).Should().BeNull();
    }

    [Fact] // PL-05
    public async Task InvokeAsync_SkipsLearn_WhenLanguageExplicitlySetFlagPresent()
    {
        // Simulates a POST to /Api/My/Language: the endpoint already wrote PreferredLanguage
        // explicitly earlier in this same request. The request's CurrentUICulture is still the
        // OLD culture (a new culture cookie only takes effect on the NEXT request) — if the
        // passive learner ran anyway, it would clobber the endpoint's write with that stale value.
        SeedUser(9, preferredLanguage: "en-US");
        var ctx = AuthedContext("9");

        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("he-IL");

            var middleware = new PreferredLanguageLearningMiddleware(inner =>
            {
                inner.Items["LanguageExplicitlySet"] = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(ctx, _db, NullLogger<PreferredLanguageLearningMiddleware>.Instance);

            StoredLanguage(9).Should().Be("en-US",
                "the endpoint already set the preference explicitly on this request — the passive learner must not clobber it");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact] // PL-06 (companion to PL-05 — guards against over-blocking)
    public async Task InvokeAsync_StillLearns_WhenFlagAbsent()
    {
        SeedUser(10, preferredLanguage: "en-US");
        var ctx = AuthedContext("10");

        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("he-IL");

            var middleware = new PreferredLanguageLearningMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(ctx, _db, NullLogger<PreferredLanguageLearningMiddleware>.Instance);

            StoredLanguage(10).Should().Be("he-IL",
                "without the explicit-set flag, ordinary passive learning must still work");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact] // PL-07
    public async Task LearnAsync_UpdatesOwnRow_ForSwitchedCompanyUser()
    {
        // A separate AppDbContext WITH an active ITenantResolver (unlike this class's shared _db,
        // which deliberately has none) — needed to actually reproduce the tenant-query-filter bug:
        // the resolver reports the company the caller is CURRENTLY VIEWING (99), which differs
        // from the user's own home CompanyId (1). Without IgnoreQueryFilters, db.Users would be
        // scoped to CompanyId==99 and the caller's own row (CompanyId==1) would never match.
        using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        connection.Open();
        // DynamicModelCacheKeyFactory — same isolation pattern as MultiCompanyTenantIsolationTests /
        // HierarchyFollowsSwitchTests: without it, EF's default model cache (keyed only by context
        // TYPE) would reuse the model built by this test class's own resolver-less shared `_db`
        // (built in the constructor, which runs before every test), silently binding this
        // context's query filter closures to the WRONG (or no) resolver.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, DynamicModelCacheKeyFactory>()
            .Options;

        var tenantResolverMock = new Mock<ITenantResolver>();
        tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(99);

        await using var db = new AppDbContext(options, tenantResolverMock.Object);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new AppUser
        {
            Id = 20,
            CompanyId = 1, // home company — differs from the resolver's current tenant (99)
            Email = "switched@test.local",
            DisplayName = "Switched User",
            PreferredLanguage = "en-US"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var ctx = AuthedContext("20");
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("he-IL");

            var updated = await PreferredLanguageLearningMiddleware.LearnAsync(ctx, db, CancellationToken.None);

            updated.Should().BeTrue(
                "the passive learner must update the caller's OWN row even when they're currently viewing a different company than their home CompanyId");

            var stored = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == 20);
            stored.PreferredLanguage.Should().Be("he-IL");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
        }
    }
}
