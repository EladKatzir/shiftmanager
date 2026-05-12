using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Tests.UnitTests.Services;

public class PurgeServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<IArchiveService> _archiveServiceMock;
    private readonly Mock<IAuditLogService> _auditLogServiceMock;
    private readonly Mock<ICompanyCacheService> _companyCacheMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly IConfiguration _configuration;
    private readonly PurgeService _service;

    private const int CompanyId = 1;
    private const int CurrentUserId = 100;

    public PurgeServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyId);

        _archiveServiceMock = new Mock<IArchiveService>();
        _auditLogServiceMock = new Mock<IAuditLogService>();
        _companyCacheMock = new Mock<ICompanyCacheService>();

        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, CurrentUserId.ToString()),
            new Claim("CompanyId", CompanyId.ToString())
        }, "TestAuth"));
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:Default", "Data Source=test.db" }
            })
            .Build();

        _service = new PurgeService(
            _db,
            _tenantResolverMock.Object,
            _archiveServiceMock.Object,
            _auditLogServiceMock.Object,
            Mock.Of<ILogger<PurgeService>>(),
            _configuration,
            _httpContextAccessorMock.Object,
            _companyCacheMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // --- ValidateConfirmation ---

    [Fact]
    public void ValidateConfirmation_CorrectFormat_ReturnsTrue()
    {
        var companyName = "Acme Corp";
        var cutoffDate = new DateOnly(2026, 1, 1);

        var confirmation = "DELETE ACME CORP BEFORE 2026-01-01";
        var result = _service.ValidateConfirmation(confirmation, companyName, cutoffDate);

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfirmation_WrongCompanyName_ReturnsFalse()
    {
        var result = _service.ValidateConfirmation(
            "DELETE WRONG COMPANY BEFORE 2026-01-01",
            "Acme Corp",
            new DateOnly(2026, 1, 1));

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfirmation_WrongDate_ReturnsFalse()
    {
        var result = _service.ValidateConfirmation(
            "DELETE ACME CORP BEFORE 2025-12-31",
            "Acme Corp",
            new DateOnly(2026, 1, 1));

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfirmation_NullConfirmation_ReturnsFalse()
    {
        var result = _service.ValidateConfirmation(null!, "Acme Corp", new DateOnly(2026, 1, 1));

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfirmation_EmptyConfirmation_ReturnsFalse()
    {
        var result = _service.ValidateConfirmation("  ", "Acme Corp", new DateOnly(2026, 1, 1));

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfirmation_NullCompanyName_ReturnsFalse()
    {
        var result = _service.ValidateConfirmation("DELETE  BEFORE 2026-01-01", null!, new DateOnly(2026, 1, 1));

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfirmation_CaseSensitive_UppercaseRequired()
    {
        // Company name gets uppercased, so lowercase input should fail
        var result = _service.ValidateConfirmation(
            "DELETE acme corp BEFORE 2026-01-01",
            "Acme Corp",
            new DateOnly(2026, 1, 1));

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfirmation_TrimsWhitespace()
    {
        var result = _service.ValidateConfirmation(
            "  DELETE ACME CORP BEFORE 2026-01-01  ",
            "Acme Corp",
            new DateOnly(2026, 1, 1));

        result.Should().BeTrue();
    }

    // --- PurgeDataAsync validation ---

    [Fact]
    public async Task PurgeDataAsync_ArchiveNotConfirmed_ReturnsError()
    {
        var request = new PurgeRequest
        {
            CutoffDate = new DateOnly(2025, 1, 1),
            Types = ArchiveDataTypes.Shifts,
            ArchiveConfirmed = false,
            TypedConfirmation = "DELETE TESTCO BEFORE 2025-01-01"
        };

        var result = await _service.PurgeDataAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("confirm that you have exported/archived");
    }

    [Fact]
    public async Task PurgeDataAsync_NoFreshArchive_ReturnsError()
    {
        _archiveServiceMock.Setup(a => a.ValidateFreshArchiveAsync(
            It.IsAny<DateOnly>(), It.IsAny<ArchiveDataTypes>()))
            .ReturnsAsync(false);

        var request = new PurgeRequest
        {
            CutoffDate = new DateOnly(2025, 1, 1),
            Types = ArchiveDataTypes.Shifts,
            ArchiveConfirmed = true,
            TypedConfirmation = "DELETE TESTCO BEFORE 2025-01-01"
        };

        var result = await _service.PurgeDataAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No fresh archive found");
    }

    [Fact]
    public async Task PurgeDataAsync_CompanyNotFound_ReturnsError()
    {
        _archiveServiceMock.Setup(a => a.ValidateFreshArchiveAsync(
            It.IsAny<DateOnly>(), It.IsAny<ArchiveDataTypes>()))
            .ReturnsAsync(true);
        _companyCacheMock.Setup(c => c.GetCompanyAsync(CompanyId))
            .ReturnsAsync((Company?)null);

        var request = new PurgeRequest
        {
            CutoffDate = new DateOnly(2025, 1, 1),
            Types = ArchiveDataTypes.Shifts,
            ArchiveConfirmed = true,
            TypedConfirmation = "DELETE TESTCO BEFORE 2025-01-01"
        };

        var result = await _service.PurgeDataAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Company not found");
    }

    [Fact]
    public async Task PurgeDataAsync_WrongConfirmation_ReturnsError()
    {
        _archiveServiceMock.Setup(a => a.ValidateFreshArchiveAsync(
            It.IsAny<DateOnly>(), It.IsAny<ArchiveDataTypes>()))
            .ReturnsAsync(true);
        _companyCacheMock.Setup(c => c.GetCompanyAsync(CompanyId))
            .ReturnsAsync(new Company { Id = CompanyId, Name = "TestCo", DisplayName = "Test Company", MoleculeId = 1 });

        var request = new PurgeRequest
        {
            CutoffDate = new DateOnly(2025, 1, 1),
            Types = ArchiveDataTypes.Shifts,
            ArchiveConfirmed = true,
            TypedConfirmation = "WRONG CONFIRMATION TEXT"
        };

        var result = await _service.PurgeDataAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Typed confirmation does not match");
    }

    [Fact]
    public async Task PurgeDataAsync_Duration_AlwaysPopulated()
    {
        // Even on validation failure, Duration should be set
        var request = new PurgeRequest
        {
            CutoffDate = new DateOnly(2025, 1, 1),
            Types = ArchiveDataTypes.Shifts,
            ArchiveConfirmed = false,
            TypedConfirmation = ""
        };

        var result = await _service.PurgeDataAsync(request);

        result.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
