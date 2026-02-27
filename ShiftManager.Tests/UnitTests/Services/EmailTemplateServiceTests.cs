using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for EmailTemplateService — verifies template CRUD, variable replacement,
/// and default message resolution.
///
/// ET-01: SaveTemplateAsync creates a new template
/// ET-02: SaveTemplateAsync updates an existing template
/// ET-03: GetCustomMessageAsync returns null when template is disabled
/// ET-04: ReplaceVariables HTML-encodes variable values
/// ET-05: GetAvailableVariables returns correct variables for each template type
/// ET-06: GetDefaultMessage returns localized string for known template type
/// </summary>
public class EmailTemplateServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<IStringLocalizer<SharedResources>> _localizerMock;
    private readonly EmailTemplateService _service;

    private const int TestCompanyId = 1;

    public EmailTemplateServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(x => x.GetCurrentTenantId()).Returns(TestCompanyId);

        _localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        _localizerMock.Setup(x => x[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, key));

        var loggerMock = new Mock<ILogger<EmailTemplateService>>();

        _service = new EmailTemplateService(
            _db,
            _tenantResolverMock.Object,
            _localizerMock.Object,
            loggerMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    /// <summary>
    /// ET-01: SaveTemplateAsync creates a new template when none exists for the type.
    /// </summary>
    [Fact]
    public async Task ET01_SaveTemplateAsync_CreatesNewTemplate()
    {
        // Act
        var result = await _service.SaveTemplateAsync(
            EmailTemplateType.ShiftAssigned,
            "Hello {EmployeeName}, you have been assigned to {ShiftType}.",
            isEnabled: true,
            userId: 1);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0);
        result.CompanyId.Should().Be(TestCompanyId);
        result.TemplateType.Should().Be(EmailTemplateType.ShiftAssigned);
        result.CustomMessage.Should().Contain("{EmployeeName}");
        result.IsEnabled.Should().BeTrue();
        result.CreatedBy.Should().Be(1);

        // Verify persisted
        var fromDb = await _db.EmailTemplateCustomizations.FindAsync(result.Id);
        fromDb.Should().NotBeNull();
    }

    /// <summary>
    /// ET-02: SaveTemplateAsync updates an existing template instead of creating a duplicate.
    /// </summary>
    [Fact]
    public async Task ET02_SaveTemplateAsync_UpdatesExistingTemplate()
    {
        // Arrange — create initial template
        await _service.SaveTemplateAsync(
            EmailTemplateType.ChoreAssigned,
            "Original message",
            isEnabled: false,
            userId: 1);

        // Act — update it
        var updated = await _service.SaveTemplateAsync(
            EmailTemplateType.ChoreAssigned,
            "Updated message for {EmployeeName}",
            isEnabled: true,
            userId: 2);

        // Assert
        updated.CustomMessage.Should().Be("Updated message for {EmployeeName}");
        updated.IsEnabled.Should().BeTrue();
        updated.UpdatedBy.Should().Be(2);

        // Verify only one record exists
        var count = await _db.EmailTemplateCustomizations
            .CountAsync(t => t.CompanyId == TestCompanyId && t.TemplateType == EmailTemplateType.ChoreAssigned);
        count.Should().Be(1);
    }

    /// <summary>
    /// ET-03: GetCustomMessageAsync returns null when template is disabled.
    /// </summary>
    [Fact]
    public async Task ET03_GetCustomMessageAsync_ReturnsNull_WhenDisabled()
    {
        // Arrange — create disabled template
        await _service.SaveTemplateAsync(
            EmailTemplateType.TimeOffApproved,
            "Your time off has been approved!",
            isEnabled: false,
            userId: 1);

        // Act
        var result = await _service.GetCustomMessageAsync(EmailTemplateType.TimeOffApproved);

        // Assert
        result.Should().BeNull("template is disabled");
    }

    /// <summary>
    /// ET-04: ReplaceVariables HTML-encodes values to prevent XSS.
    /// </summary>
    [Fact]
    public void ET04_ReplaceVariables_HtmlEncodesValues()
    {
        // Arrange
        var message = "Hello {EmployeeName}, your shift on {Date}";
        var variables = new Dictionary<string, string>
        {
            { "EmployeeName", "<script>alert('xss')</script>" },
            { "Date", "2026-03-01" }
        };

        // Act
        var result = _service.ReplaceVariables(message, variables);

        // Assert
        result.Should().NotContain("<script>");
        result.Should().Contain("&lt;script&gt;");
        result.Should().Contain("2026-03-01");
    }

    /// <summary>
    /// ET-05: GetAvailableVariables returns correct variables for each template type.
    /// </summary>
    [Theory]
    [InlineData(EmailTemplateType.ShiftAssigned, "EmployeeName,ShiftType,Date,StartTime,EndTime")]
    [InlineData(EmailTemplateType.ChoreAssigned, "EmployeeName,ChoreTitle,Date")]
    [InlineData(EmailTemplateType.AccessRequestSubmitted, "OwnerName,RequesterName,RequesterEmail,CompanyName")]
    public void ET05_GetAvailableVariables_ReturnsCorrectVariables(EmailTemplateType type, string expectedCsv)
    {
        // Act
        var variables = _service.GetAvailableVariables(type);

        // Assert
        var expected = expectedCsv.Split(',');
        variables.Should().BeEquivalentTo(expected);
    }

    /// <summary>
    /// ET-06: GetDefaultMessage returns localized string for known template type.
    /// </summary>
    [Fact]
    public void ET06_GetDefaultMessage_ReturnsLocalizedString()
    {
        // Act
        var result = _service.GetDefaultMessage(EmailTemplateType.ShiftAssigned);

        // Assert — our mock returns the key itself
        result.Should().Be("Email_ShiftAssignedBody");
    }
}
