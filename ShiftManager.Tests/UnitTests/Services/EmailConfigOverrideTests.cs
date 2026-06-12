using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for EmailConfig.OverrideEnabled feature:
/// EC-01: GetEmailConfigAsync returns company override when OverrideEnabled=true
/// EC-02: GetEmailConfigAsync falls through to global when OverrideEnabled=false
/// EC-03: GetAllCompanyOverridesWithNamesAsync returns only per-company configs (not global)
/// EC-04: SetOverrideEnabledAsync toggles the flag and returns true when found
/// EC-05: SetOverrideEnabledAsync returns false when company has no override
/// </summary>
public class EmailConfigOverrideTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly Mock<IEncryptionService> _encryptionMock;
    private readonly Mock<ILogger<EmailConfigService>> _loggerMock;
    private readonly Mock<ITenantResolver> _tenantMock;

    public EmailConfigOverrideTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _encryptionMock = new Mock<IEncryptionService>();
        _loggerMock = new Mock<ILogger<EmailConfigService>>();
        _tenantMock = new Mock<ITenantResolver>();
    }

    private EmailConfigService BuildService(int tenantCompanyId)
    {
        _tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(tenantCompanyId);
        return new EmailConfigService(_db, _encryptionMock.Object, _tenantMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// EC-01: When OverrideEnabled=true, GetEmailConfigAsync returns the company-specific config.
    /// </summary>
    [Fact]
    public async Task GetEmailConfigAsync_ReturnsCompanyOverride_WhenOverrideEnabledTrue()
    {
        // Arrange
        _db.EmailConfigs.Add(new EmailConfig
        {
            CompanyId = null,
            Enabled = true,
            FromAddress = "global@test.com",
            OverrideEnabled = true,
            LastUpdated = DateTime.UtcNow
        });
        _db.EmailConfigs.Add(new EmailConfig
        {
            CompanyId = 42,
            Enabled = false,
            FromAddress = "company@test.com",
            OverrideEnabled = true,
            LastUpdated = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var svc = BuildService(42);

        // Act
        var result = await svc.GetEmailConfigAsync();

        // Assert
        result.Should().NotBeNull();
        result!.CompanyId.Should().Be(42);
        result.FromAddress.Should().Be("company@test.com");
    }

    /// <summary>
    /// EC-02: When OverrideEnabled=false, GetEmailConfigAsync falls through to global config.
    /// </summary>
    [Fact]
    public async Task GetEmailConfigAsync_FallsBackToGlobal_WhenOverrideEnabledFalse()
    {
        // Arrange
        _db.EmailConfigs.Add(new EmailConfig
        {
            CompanyId = null,
            Enabled = true,
            FromAddress = "global@test.com",
            OverrideEnabled = true,
            LastUpdated = DateTime.UtcNow
        });
        _db.EmailConfigs.Add(new EmailConfig
        {
            CompanyId = 99,
            Enabled = false,
            FromAddress = "company@test.com",
            OverrideEnabled = false,   // disabled override
            LastUpdated = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var svc = BuildService(99);

        // Act
        var result = await svc.GetEmailConfigAsync();

        // Assert: must return the global (CompanyId == null), not the disabled override
        result.Should().NotBeNull();
        result!.CompanyId.Should().BeNull("OverrideEnabled=false should cause fall-through to global");
        result.FromAddress.Should().Be("global@test.com");
    }

    /// <summary>
    /// EC-03: GetAllCompanyOverridesWithNamesAsync returns only rows with CompanyId != null.
    /// </summary>
    [Fact]
    public async Task GetAllCompanyOverridesWithNamesAsync_ReturnsOnlyCompanyRows()
    {
        // Arrange
        _db.EmailConfigs.Add(new EmailConfig
        {
            CompanyId = null,
            Enabled = true,
            FromAddress = "global@test.com",
            OverrideEnabled = true,
            LastUpdated = DateTime.UtcNow
        });
        _db.EmailConfigs.Add(new EmailConfig
        {
            CompanyId = 10,
            Enabled = true,
            FromAddress = "c10@test.com",
            OverrideEnabled = true,
            LastUpdated = DateTime.UtcNow
        });
        _db.EmailConfigs.Add(new EmailConfig
        {
            CompanyId = 20,
            Enabled = false,
            FromAddress = "c20@test.com",
            OverrideEnabled = false,
            LastUpdated = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var svc = BuildService(1);

        // Act
        var rows = await svc.GetAllCompanyOverridesWithNamesAsync();

        // Assert
        rows.Should().HaveCount(2, "only company-specific rows, not global");
        rows.Should().NotContain(r => r.CompanyId == 0);
        rows.Select(r => r.CompanyId).Should().BeEquivalentTo(new[] { 10, 20 });

        var c20 = rows.First(r => r.CompanyId == 20);
        c20.OverrideEnabled.Should().BeFalse();
    }

    /// <summary>
    /// EC-04: SetOverrideEnabledAsync toggles the flag and returns true when company override exists.
    /// </summary>
    [Fact]
    public async Task SetOverrideEnabledAsync_TogglesFlag_WhenOverrideExists()
    {
        // Arrange
        _db.EmailConfigs.Add(new EmailConfig
        {
            CompanyId = 55,
            Enabled = true,
            OverrideEnabled = true,
            LastUpdated = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var svc = BuildService(1);

        // Act — disable the override
        var result = await svc.SetOverrideEnabledAsync(55, false);

        // Assert
        result.Should().BeTrue("override exists for company 55");
        var updated = await _db.EmailConfigs.IgnoreQueryFilters()
            .FirstAsync(ec => ec.CompanyId == 55);
        updated.OverrideEnabled.Should().BeFalse();

        // Act — re-enable
        var reEnable = await svc.SetOverrideEnabledAsync(55, true);
        reEnable.Should().BeTrue();
        var reEnabled = await _db.EmailConfigs.IgnoreQueryFilters()
            .FirstAsync(ec => ec.CompanyId == 55);
        reEnabled.OverrideEnabled.Should().BeTrue();
    }

    /// <summary>
    /// EC-05: SetOverrideEnabledAsync returns false when no override exists for the company.
    /// </summary>
    [Fact]
    public async Task SetOverrideEnabledAsync_ReturnsFalse_WhenNoOverrideExists()
    {
        var svc = BuildService(1);

        var result = await svc.SetOverrideEnabledAsync(9999, false);

        result.Should().BeFalse("no override exists for company 9999");
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
