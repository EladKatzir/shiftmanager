using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for ImportService — verifies archive validation, company matching,
/// and missing user detection.
///
/// IS-01: ValidateArchiveAsync rejects non-existent file
/// IS-02: ValidateArchiveAsync rejects archive without manifest.json
/// IS-03: ValidateArchiveAsync detects company mismatch
/// IS-04: ValidateArchiveAsync detects missing users referenced in archive
/// </summary>
public class ImportServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<IAuditLogService> _auditLogMock;
    private readonly Mock<IHttpContextAccessor> _httpContextMock;
    private readonly Mock<ICompanyCacheService> _companyCacheMock;
    private readonly ImportService _service;

    private const int TestCompanyId = 1;
    private readonly string _tempDir;

    public ImportServiceTests()
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

        _auditLogMock = new Mock<IAuditLogService>();
        _httpContextMock = new Mock<IHttpContextAccessor>();
        _companyCacheMock = new Mock<ICompanyCacheService>();

        // Default company setup
        _companyCacheMock.Setup(x => x.GetCompanyAsync(TestCompanyId))
            .ReturnsAsync(new Company { Id = TestCompanyId, Name = "Test Company" });

        var loggerMock = new Mock<ILogger<ImportService>>();

        _service = new ImportService(
            _db,
            _tenantResolverMock.Object,
            _auditLogMock.Object,
            loggerMock.Object,
            _httpContextMock.Object,
            _companyCacheMock.Object);

        _tempDir = Path.Combine(Path.GetTempPath(), $"ImportServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Creates a temporary ZIP file with the given entries.
    /// </summary>
    private string CreateTestZip(Dictionary<string, string> entries)
    {
        var zipPath = Path.Combine(_tempDir, $"test_{Guid.NewGuid()}.zip");
        using (var stream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Key);
                using var writer = new StreamWriter(zipEntry.Open());
                writer.Write(entry.Value);
            }
        }
        return zipPath;
    }

    /// <summary>
    /// IS-01: ValidateArchiveAsync returns error when the file does not exist.
    /// </summary>
    [Fact]
    public async Task IS01_ValidateArchiveAsync_RejectsNonExistentFile()
    {
        // Act
        var result = await _service.ValidateArchiveAsync("/tmp/nonexistent_archive.zip");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    /// <summary>
    /// IS-02: ValidateArchiveAsync returns error when the ZIP lacks manifest.json.
    /// </summary>
    [Fact]
    public async Task IS02_ValidateArchiveAsync_RejectsArchiveWithoutManifest()
    {
        // Arrange — create a ZIP with only data.ndjson, no manifest
        var zipPath = CreateTestZip(new Dictionary<string, string>
        {
            ["data.ndjson"] = "{\"type\":\"ShiftAssignment\",\"data\":{}}"
        });

        // Act
        var result = await _service.ValidateArchiveAsync(zipPath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("manifest.json"));
    }

    /// <summary>
    /// IS-03: ValidateArchiveAsync detects company mismatch between archive and current tenant.
    /// </summary>
    [Fact]
    public async Task IS03_ValidateArchiveAsync_DetectsCompanyMismatch()
    {
        // Arrange — manifest says CompanyId=999, but current tenant is 1
        var manifest = JsonSerializer.Serialize(new
        {
            CompanyId = 999,
            CompanyName = "Other Company",
            ExportedAt = DateTime.UtcNow.ToString("o"),
            Version = "1.0"
        });

        var zipPath = CreateTestZip(new Dictionary<string, string>
        {
            ["manifest.json"] = manifest,
            ["data.ndjson"] = ""
        });

        // Act
        var result = await _service.ValidateArchiveAsync(zipPath);

        // Assert
        result.CompanyMatch.Should().BeFalse();
        result.ArchiveCompanyName.Should().Be("Other Company");
        result.CurrentCompanyName.Should().Be("Test Company");
        result.Errors.Should().Contain(e => e.Contains("mismatch"));
    }

    /// <summary>
    /// IS-04: ValidateArchiveAsync detects users referenced in data.ndjson that don't exist in the DB.
    /// </summary>
    [Fact]
    public async Task IS04_ValidateArchiveAsync_DetectsMissingUsers()
    {
        // Arrange — add one existing user
        _db.Users.Add(new AppUser
        {
            Id = 1,
            Email = "existing@test.com",
            CompanyId = TestCompanyId,
            DisplayName = "Existing User",
            IsActive = true
        });
        await _db.SaveChangesAsync();

        // Manifest matching current company
        var manifest = JsonSerializer.Serialize(new
        {
            CompanyId = TestCompanyId,
            CompanyName = "Test Company",
            ExportedAt = DateTime.UtcNow.ToString("o"),
            Version = "1.0"
        });

        // Data file references existing@test.com (exists) and missing@test.com (doesn't exist)
        var dataLines = string.Join("\n",
            JsonSerializer.Serialize(new { type = "ShiftAssignment", data = new { userEmail = "existing@test.com" } }),
            JsonSerializer.Serialize(new { type = "ShiftAssignment", data = new { userEmail = "missing@test.com" } })
        );

        var zipPath = CreateTestZip(new Dictionary<string, string>
        {
            ["manifest.json"] = manifest,
            ["data.ndjson"] = dataLines
        });

        // Act
        var result = await _service.ValidateArchiveAsync(zipPath);

        // Assert
        result.MissingUsers.Should().Contain("missing@test.com");
        result.MissingUsers.Should().NotContain("existing@test.com");
    }
}
