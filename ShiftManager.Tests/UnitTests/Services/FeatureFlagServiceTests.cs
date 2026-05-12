using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for FeatureFlagService — verifies 3-tier scope resolution,
/// cache behavior, and CRUD operations.
///
/// FF-01: Returns false for non-existent flag
/// FF-02: Global flag resolves correctly
/// FF-03: Company-specific flag overrides global
/// FF-04: User-specific flag overrides company
/// </summary>
public class FeatureFlagServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly FeatureFlagService _service;

    public FeatureFlagServiceTests()
    {
        // Real SQLite (NOT EF Core In-Memory) so the production translator runs end-to-end.
        // See GRIFFIN-USERLOOKUP-510 for the bug class this defends against.
        // `Foreign Keys=False` preserves the relaxed FK semantics the previous In-Memory
        // fixture used (existing tests reference CompanyId/UserId values without seeding
        // parent rows). Real SQLite enforces FKs by default — disabling it here keeps the
        // test contract unchanged while still exercising the SQL translator. Fixtures
        // designed FROM SCRATCH against SQLite (e.g., SqliteTranslationGuardTests) leave
        // FKs ON and seed parent rows explicitly.
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _cache = new MemoryCache(new MemoryCacheOptions());
        var loggerMock = new Mock<ILogger<FeatureFlagService>>();
        var localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns((string k) => new LocalizedString(k, k));

        _service = new FeatureFlagService(_db, _cache, loggerMock.Object, localizerMock.Object);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    /// <summary>
    /// FF-01: IsEnabledAsync returns false when the flag does not exist in the database.
    /// </summary>
    [Fact]
    public async Task FF01_IsEnabledAsync_ReturnsFalse_WhenFlagDoesNotExist()
    {
        // Act
        var result = await _service.IsEnabledAsync("NON_EXISTENT_FLAG");

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// FF-02: A global flag (no CompanyId, no UserId) resolves correctly.
    /// </summary>
    [Fact]
    public async Task FF02_IsEnabledAsync_ResolvesGlobalFlag()
    {
        // Arrange — create a global flag (CompanyId=null, UserId=null)
        _db.FeatureFlags.Add(new FeatureFlag
        {
            Name = "FF_TEST_GLOBAL",
            IsEnabled = true,
            CompanyId = null,
            UserId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.IsEnabledAsync("FF_TEST_GLOBAL");

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// FF-03: Company-specific flag overrides global flag when companyId is provided.
    /// Resolution priority: user > company > global.
    /// </summary>
    [Fact]
    public async Task FF03_IsEnabledAsync_CompanyOverridesGlobal()
    {
        // Arrange — global flag is enabled
        _db.FeatureFlags.Add(new FeatureFlag
        {
            Name = "FF_OVERRIDE_TEST",
            IsEnabled = true,
            CompanyId = null,
            UserId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Company-specific flag is disabled
        _db.FeatureFlags.Add(new FeatureFlag
        {
            Name = "FF_OVERRIDE_TEST",
            IsEnabled = false,
            CompanyId = 42,
            UserId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act — query with companyId=42
        var result = await _service.IsEnabledAsync("FF_OVERRIDE_TEST", companyId: 42);

        // Assert — company-specific (disabled) should override global (enabled)
        result.Should().BeFalse();

        // Also verify global still returns true when no company is specified
        _service.InvalidateCache("FF_OVERRIDE_TEST"); // Clear cache to avoid stale result
        var globalResult = await _service.IsEnabledAsync("FF_OVERRIDE_TEST");
        globalResult.Should().BeTrue();
    }

    /// <summary>
    /// FF-04: User-specific flag overrides company-specific flag.
    /// Resolution priority: user > company > global.
    /// </summary>
    [Fact]
    public async Task FF04_IsEnabledAsync_UserOverridesCompany()
    {
        // Arrange — company-specific flag is disabled
        _db.FeatureFlags.Add(new FeatureFlag
        {
            Name = "FF_USER_TEST",
            IsEnabled = false,
            CompanyId = 10,
            UserId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // User-specific flag is enabled
        _db.FeatureFlags.Add(new FeatureFlag
        {
            Name = "FF_USER_TEST",
            IsEnabled = true,
            CompanyId = 10,
            UserId = 99,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Act — query with userId=99, companyId=10
        var result = await _service.IsEnabledAsync("FF_USER_TEST", userId: 99, companyId: 10);

        // Assert — user-specific (enabled) should override company-specific (disabled)
        result.Should().BeTrue();
    }
}
