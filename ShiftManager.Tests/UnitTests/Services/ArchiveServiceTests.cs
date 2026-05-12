using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for ArchiveService — verifies archive preview counting,
/// data type filtering, and cutoff date logic.
///
/// AR-01: PreviewArchiveAsync counts shifts before cutoff date
/// AR-02: PreviewArchiveAsync respects data type flags
/// AR-03: PreviewArchiveAsync counts time-off requests before cutoff
/// AR-04: PreviewArchiveAsync includes offline shift warning for TimeOff
/// AR-05: PreviewArchiveAsync returns zero counts when no data exists
/// </summary>
public class ArchiveServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly ArchiveService _service;

    private const int TestCompanyId = 1;

    public ArchiveServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(x => x.GetCurrentTenantId()).Returns(TestCompanyId);

        var auditLogMock = new Mock<IAuditLogService>();
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());
        var loggerMock = new Mock<ILogger<ArchiveService>>();
        var httpContextMock = new Mock<IHttpContextAccessor>();
        var configMock = new Mock<IConfiguration>();
        var sectionMock = new Mock<IConfigurationSection>();
        configMock.Setup(c => c.GetSection(It.IsAny<string>())).Returns(sectionMock.Object);
        var companyCacheMock = new Mock<ICompanyCacheService>();

        _service = new ArchiveService(
            _db,
            _tenantResolverMock.Object,
            auditLogMock.Object,
            envMock.Object,
            loggerMock.Object,
            httpContextMock.Object,
            configMock.Object,
            companyCacheMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private async Task SeedShiftDataAsync(DateOnly workDate, string shiftTypeKey = "MORNING")
    {
        var shiftType = new ShiftType
        {
            Scope = ShiftManager.Models.Support.ShiftScope.Molecule,
            Key = shiftTypeKey,
            Name = shiftTypeKey,
            MoleculeId = 1,
            JobTypeId = 1
        };
        _db.ShiftTypes.Add(shiftType);
        await _db.SaveChangesAsync();

        var instance = new ShiftInstance
        {
            CompanyId = TestCompanyId,
            ShiftTypeId = shiftType.Id,
            WorkDate = workDate,
            Name = $"Test {shiftTypeKey}",
            StaffingRequired = 1
        };
        _db.ShiftInstances.Add(instance);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// AR-01: PreviewArchiveAsync counts shifts before the cutoff date.
    /// </summary>
    [Fact]
    public async Task AR01_PreviewArchiveAsync_CountsShiftsBeforeCutoff()
    {
        // Arrange — seed shifts: 2 before cutoff, 1 after
        var cutoff = new DateOnly(2026, 6, 1);
        await SeedShiftDataAsync(new DateOnly(2026, 1, 15), "MORNING");
        await SeedShiftDataAsync(new DateOnly(2026, 3, 10), "AFTERNOON");
        await SeedShiftDataAsync(new DateOnly(2026, 8, 20), "NIGHT"); // After cutoff

        // Act
        var preview = await _service.PreviewArchiveAsync(cutoff, ArchiveDataTypes.Shifts);

        // Assert
        preview.CutoffDate.Should().Be(cutoff);
        preview.Counts.Should().ContainKey("ShiftInstances");
        preview.Counts["ShiftInstances"].Should().Be(2, "only 2 shifts are before the cutoff");
    }

    /// <summary>
    /// AR-02: PreviewArchiveAsync respects data type flags (Shifts only, no TimeOff).
    /// </summary>
    [Fact]
    public async Task AR02_PreviewArchiveAsync_RespectsDataTypeFlags()
    {
        // Arrange — seed shifts and time-off
        var cutoff = new DateOnly(2026, 6, 1);
        await SeedShiftDataAsync(new DateOnly(2026, 2, 1));

        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            CompanyId = TestCompanyId,
            UserId = 1,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 5),
            Status = RequestStatus.Approved
        });
        await _db.SaveChangesAsync();

        // Act — only request Shifts, not TimeOff
        var preview = await _service.PreviewArchiveAsync(cutoff, ArchiveDataTypes.Shifts);

        // Assert
        preview.Counts.Should().ContainKey("ShiftInstances");
        preview.Counts.Should().NotContainKey("TimeOffRequests", "TimeOff flag was not set");
    }

    /// <summary>
    /// AR-03: PreviewArchiveAsync counts time-off requests before cutoff.
    /// </summary>
    [Fact]
    public async Task AR03_PreviewArchiveAsync_CountsTimeOffBeforeCutoff()
    {
        // Arrange — 2 time-off before cutoff, 1 after
        var cutoff = new DateOnly(2026, 6, 1);
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            CompanyId = TestCompanyId, UserId = 1,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 5),
            Status = RequestStatus.Approved
        });
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            CompanyId = TestCompanyId, UserId = 2,
            StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2026, 3, 3),
            Status = RequestStatus.Approved
        });
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            CompanyId = TestCompanyId, UserId = 3,
            StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 10), // After cutoff
            Status = RequestStatus.Approved
        });
        await _db.SaveChangesAsync();

        // Act
        var preview = await _service.PreviewArchiveAsync(cutoff, ArchiveDataTypes.TimeOff);

        // Assert
        preview.Counts.Should().ContainKey("TimeOffRequests");
        preview.Counts["TimeOffRequests"].Should().Be(2);
    }

    /// <summary>
    /// AR-04: PreviewArchiveAsync includes warning about OFFLINE shifts when TimeOff is selected.
    /// </summary>
    [Fact]
    public async Task AR04_PreviewArchiveAsync_WarnsAboutOfflineShifts()
    {
        // Arrange — seed an OFFLINE shift before cutoff
        var cutoff = new DateOnly(2026, 6, 1);
        await SeedShiftDataAsync(new DateOnly(2026, 2, 1), ShiftType.KEY_OFFLINE);

        // Act
        var preview = await _service.PreviewArchiveAsync(cutoff, ArchiveDataTypes.TimeOff);

        // Assert
        preview.Counts.Should().ContainKey("OfflineShifts");
        preview.Counts["OfflineShifts"].Should().Be(1);
        preview.Warnings.Should().Contain(w => w.Contains("OFFLINE"));
    }

    /// <summary>
    /// AR-05: PreviewArchiveAsync returns zero counts when no data exists.
    /// </summary>
    [Fact]
    public async Task AR05_PreviewArchiveAsync_ReturnsZeroCounts_WhenEmpty()
    {
        // Arrange — empty database
        var cutoff = new DateOnly(2026, 6, 1);

        // Act
        var preview = await _service.PreviewArchiveAsync(cutoff, ArchiveDataTypes.All);

        // Assert
        preview.Counts.Values.Should().AllSatisfy(v => v.Should().Be(0));
        preview.EstimatedSizeBytes.Should().Be(0);
    }
}
