using FluentAssertions;
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
/// Tests for CompanyLocalizationService — verifies override CRUD, caching,
/// validation rules, and security measures.
///
/// CL-01: UpsertOverrideAsync creates new override with HTML encoding
/// CL-02: UpsertOverrideAsync updates existing override
/// CL-03: GetOverridesAsync returns cached results on second call
/// CL-04: DeleteOverrideAsync soft-deletes (sets IsActive=false)
/// CL-05: ValidateCulture rejects invalid culture codes
/// CL-06: ValidateResourceKey rejects special characters
/// CL-07: ValidateOverrideValue rejects control characters
/// CL-08: SearchOverridesAsync filters by culture and search term
/// </summary>
public class CompanyLocalizationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MemoryCache _cache;
    private readonly Mock<IAuditLogService> _auditLogMock;
    private readonly Mock<IStringLocalizer<SharedResources>> _localizerMock;
    private readonly CompanyLocalizationService _service;

    private const int TestCompanyId = 1;
    private const int TestUserId = 10;

    public CompanyLocalizationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _cache = new MemoryCache(new MemoryCacheOptions());

        _auditLogMock = new Mock<IAuditLogService>();
        _auditLogMock.Setup(x => x.LogUserActionAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        // Return the key itself as the value (simulates key not found in resource file)
        _localizerMock.Setup(x => x[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, key));

        var loggerMock = new Mock<ILogger<CompanyLocalizationService>>();

        _service = new CompanyLocalizationService(
            _db,
            _cache,
            loggerMock.Object,
            _auditLogMock.Object,
            _localizerMock.Object);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// CL-01: UpsertOverrideAsync creates a new override and HTML-encodes the value.
    /// </summary>
    [Fact]
    public async Task CL01_UpsertOverrideAsync_CreatesWithHtmlEncoding()
    {
        // Act
        await _service.UpsertOverrideAsync(TestCompanyId, "en-US", "Dashboard_Title",
            "My <b>Custom</b> Dashboard", TestUserId);

        // Assert — value should be HTML-encoded in DB
        var fromDb = await _db.CompanyLocalizationOverrides
            .FirstOrDefaultAsync(o => o.CompanyId == TestCompanyId && o.ResourceKey == "Dashboard_Title");

        fromDb.Should().NotBeNull();
        fromDb!.OverrideValue.Should().Contain("&lt;b&gt;");
        fromDb.OverrideValue.Should().NotContain("<b>");
        fromDb.IsActive.Should().BeTrue();
        fromDb.CreatedBy.Should().Be(TestUserId);

        // Verify audit was logged
        _auditLogMock.Verify(x => x.LogUserActionAsync(
            TestUserId, "LocalizationOverrideCreated", It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// CL-02: UpsertOverrideAsync updates an existing override.
    /// </summary>
    [Fact]
    public async Task CL02_UpsertOverrideAsync_UpdatesExisting()
    {
        // Arrange
        await _service.UpsertOverrideAsync(TestCompanyId, "en-US", "Button_Save", "Save", TestUserId);

        // Act — update it
        await _service.UpsertOverrideAsync(TestCompanyId, "en-US", "Button_Save", "Save Changes", TestUserId);

        // Assert — only one record, with updated value
        var count = await _db.CompanyLocalizationOverrides
            .CountAsync(o => o.CompanyId == TestCompanyId && o.ResourceKey == "Button_Save");
        count.Should().Be(1);

        var fromDb = await _db.CompanyLocalizationOverrides
            .FirstAsync(o => o.CompanyId == TestCompanyId && o.ResourceKey == "Button_Save");
        fromDb.OverrideValue.Should().Be("Save Changes");
    }

    /// <summary>
    /// CL-03: GetOverridesAsync returns cached results on second call.
    /// </summary>
    [Fact]
    public async Task CL03_GetOverridesAsync_UsesCacheOnSecondCall()
    {
        // Arrange — seed an override directly
        _db.CompanyLocalizationOverrides.Add(new CompanyLocalizationOverride
        {
            CompanyId = TestCompanyId,
            Culture = "he-IL",
            ResourceKey = "Button_Cancel",
            OverrideValue = "ביטול",
            IsActive = true,
            CreatedBy = TestUserId,
            UpdatedBy = TestUserId
        });
        await _db.SaveChangesAsync();

        // Act — first call (cache miss, loads from DB)
        var result1 = await _service.GetOverridesAsync(TestCompanyId, "he-IL");

        // Modify DB directly (cache should still serve old data)
        var entity = await _db.CompanyLocalizationOverrides.FirstAsync();
        entity.OverrideValue = "בטל";
        await _db.SaveChangesAsync();

        // Act — second call (should be cache hit, still returns old value)
        var result2 = await _service.GetOverridesAsync(TestCompanyId, "he-IL");

        // Assert
        result1.Should().ContainKey("Button_Cancel");
        result2.Should().ContainKey("Button_Cancel");
        result2["Button_Cancel"].Should().Be("ביטול", "cache should serve the original value");
    }

    /// <summary>
    /// CL-04: DeleteOverrideAsync soft-deletes (sets IsActive=false).
    /// </summary>
    [Fact]
    public async Task CL04_DeleteOverrideAsync_SoftDeletes()
    {
        // Arrange
        await _service.UpsertOverrideAsync(TestCompanyId, "en-US", "Page_Title", "Custom Title", TestUserId);

        // Act
        await _service.DeleteOverrideAsync(TestCompanyId, "en-US", "Page_Title", TestUserId);

        // Assert — record still in DB but IsActive=false
        var fromDb = await _db.CompanyLocalizationOverrides
            .FirstOrDefaultAsync(o => o.CompanyId == TestCompanyId && o.ResourceKey == "Page_Title");
        fromDb.Should().NotBeNull();
        fromDb!.IsActive.Should().BeFalse();

        // GetOverridesAsync should not include deleted overrides
        _service.InvalidateCache(TestCompanyId, "en-US");
        var overrides = await _service.GetOverridesAsync(TestCompanyId, "en-US");
        overrides.Should().NotContainKey("Page_Title");
    }

    /// <summary>
    /// CL-05: UpsertOverrideAsync rejects invalid culture codes.
    /// </summary>
    [Fact]
    public async Task CL05_ValidateCulture_RejectsInvalidCulture()
    {
        // Act & Assert
        var act = () => _service.UpsertOverrideAsync(TestCompanyId, "fr-FR", "Test_Key", "value", TestUserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid culture*");
    }

    /// <summary>
    /// CL-06: UpsertOverrideAsync rejects resource keys with special characters.
    /// </summary>
    [Fact]
    public async Task CL06_ValidateResourceKey_RejectsSpecialCharacters()
    {
        // Act & Assert — spaces not allowed
        var act1 = () => _service.UpsertOverrideAsync(TestCompanyId, "en-US", "Invalid Key", "value", TestUserId);
        await act1.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*alphanumeric*");

        // Act & Assert — HTML/script injection in key
        var act2 = () => _service.UpsertOverrideAsync(TestCompanyId, "en-US", "<script>", "value", TestUserId);
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// CL-07: UpsertOverrideAsync rejects control characters in values.
    /// </summary>
    [Fact]
    public async Task CL07_ValidateOverrideValue_RejectsControlCharacters()
    {
        // Act & Assert — null byte (control character)
        var valueWithNull = "Hello\0World";
        var act = () => _service.UpsertOverrideAsync(TestCompanyId, "en-US", "Test_Key", valueWithNull, TestUserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*control characters*");
    }

    /// <summary>
    /// CL-08: SearchOverridesAsync filters by culture and search term.
    /// </summary>
    [Fact]
    public async Task CL08_SearchOverridesAsync_FiltersByCultureAndTerm()
    {
        // Arrange — seed overrides in both cultures
        await _service.UpsertOverrideAsync(TestCompanyId, "en-US", "Button_Save", "Save", TestUserId);
        await _service.UpsertOverrideAsync(TestCompanyId, "en-US", "Button_Cancel", "Cancel", TestUserId);
        await _service.UpsertOverrideAsync(TestCompanyId, "he-IL", "Button_Save", "שמירה", TestUserId);

        // Act — search English only, filter by "Save"
        var results = await _service.SearchOverridesAsync(TestCompanyId, culture: "en-US", searchTerm: "Save");

        // Assert
        results.Should().HaveCount(1);
        results.First().ResourceKey.Should().Be("Button_Save");
        results.First().Culture.Should().Be("en-US");
    }
}
