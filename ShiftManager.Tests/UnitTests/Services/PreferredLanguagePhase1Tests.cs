using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Middleware;
using ShiftManager.Models;
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
}
