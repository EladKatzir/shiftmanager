using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class MailServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<MailService>> _loggerMock;
    private readonly Mock<IEmailConfigService> _emailConfigServiceMock;
    private readonly Mock<IEmailApiLogService> _emailApiLogServiceMock;
    private readonly Mock<IStringLocalizer<SharedResources>> _localizerMock;
    private readonly Mock<IEmailTemplateService> _emailTemplateServiceMock;
    private readonly EmailBackgroundQueue _emailQueue;
    private readonly Mock<ILocalizationService> _localizationMock;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly IConfiguration _configuration;

    public MailServiceTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<MailService>>();
        _emailConfigServiceMock = new Mock<IEmailConfigService>();
        _emailApiLogServiceMock = new Mock<IEmailApiLogService>();
        _localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        _emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        _localizationMock = new Mock<ILocalizationService>();
        _tenantResolverMock = new Mock<ITenantResolver>();

        // Use a real EmailBackgroundQueue — its methods are not virtual and cannot be mocked.
        // The queue has 500 capacity so it will accept writes; we verify by reading from it.
        _emailQueue = new EmailBackgroundQueue(Mock.Of<ILogger<EmailBackgroundQueue>>());

        // Default configuration with email disabled
        var configData = new Dictionary<string, string?>
        {
            { "Email:Enabled", "false" },
            { "Email:ApiKey", "testkey12345678" },
            { "Email:ApiUrl", "https://mail.test.local/api/send" },
            { "Email:FromAddress", "noreply@test.local" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private MailService CreateService(ITenantResolver? tenantResolver = null)
    {
        return new MailService(
            _httpClientFactoryMock.Object,
            _loggerMock.Object,
            _configuration,
            _emailConfigServiceMock.Object,
            _emailApiLogServiceMock.Object,
            _localizerMock.Object,
            _emailTemplateServiceMock.Object,
            _emailQueue,
            _localizationMock.Object,
            tenantResolver);
    }

    /// <summary>
    /// Helper: read one queued email from the channel (non-blocking).
    /// Returns null if the queue is empty.
    /// </summary>
    private QueuedEmail? TryReadFromQueue()
    {
        return _emailQueue.Reader.TryRead(out var email) ? email : null;
    }

    // --- SendMailAsync (enqueue) ---

    [Fact]
    public async Task SendMailAsync_NullRecipient_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.SendMailAsync(null!, "Subject", "<p>Body</p>");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendMailAsync_EmptyRecipient_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.SendMailAsync("  ", "Subject", "<p>Body</p>");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendMailAsync_NullSubject_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.SendMailAsync("user@test.com", null!, "<p>Body</p>");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendMailAsync_EmptySubject_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.SendMailAsync("user@test.com", "   ", "<p>Body</p>");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendMailAsync_ValidInputs_EnqueuesAndReturnsTrue()
    {
        var service = CreateService();

        var result = await service.SendMailAsync("user@test.com", "Hello", "<p>World</p>");

        result.Should().BeTrue();

        var queued = TryReadFromQueue();
        queued.Should().NotBeNull();
        queued!.Recipient.Should().Be("user@test.com");
        queued.Subject.Should().Be("Hello");
        queued.HtmlBody.Should().Be("<p>World</p>");
    }

    [Fact]
    public async Task SendMailAsync_WithTenantResolver_CapturesCompanyId()
    {
        _tenantResolverMock.Setup(t => t.HasTenant()).Returns(true);
        _tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(42);
        var service = CreateService(_tenantResolverMock.Object);

        await service.SendMailAsync("user@test.com", "Hello", "<p>World</p>");

        var queued = TryReadFromQueue();
        queued.Should().NotBeNull();
        queued!.CompanyId.Should().Be(42);
    }

    [Fact]
    public async Task SendMailAsync_WithoutTenantResolver_UsesZeroCompanyId()
    {
        var service = CreateService(tenantResolver: null);

        await service.SendMailAsync("user@test.com", "Hello", "<p>World</p>");

        var queued = TryReadFromQueue();
        queued.Should().NotBeNull();
        queued!.CompanyId.Should().Be(0);
    }

    [Fact]
    public async Task SendMailAsync_NullInputs_DoNotEnqueue()
    {
        var service = CreateService();

        await service.SendMailAsync(null!, "Subject", "<p>Body</p>");

        var queued = TryReadFromQueue();
        queued.Should().BeNull("nothing should be enqueued for null recipient");
    }

    // --- SendMailDirectAsync ---

    [Fact]
    public async Task SendMailDirectAsync_NullRecipient_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.SendMailDirectAsync(null!, "Subject", "<p>Body</p>");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendMailDirectAsync_EmptySubject_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.SendMailDirectAsync("user@test.com", "  ", "<p>Body</p>");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendMailDirectAsync_EmailDisabled_ReturnsTrue()
    {
        // Email is disabled in config by default
        _emailConfigServiceMock.Setup(e => e.GetEmailConfigAsync()).ReturnsAsync((EmailConfig?)null);
        _emailApiLogServiceMock.Setup(l => l.LogEmailApiCallAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<int>(),
            It.IsAny<List<string>?>(), It.IsAny<int>())).ReturnsAsync(new EmailApiLog());

        var service = CreateService();

        // Returns true because email is disabled — no blocking of workflow
        var result = await service.SendMailDirectAsync("user@test.com", "Hello", "<p>World</p>");

        result.Should().BeTrue();
    }

    // --- SendShiftAssignedEmailAsync ---

    [Fact]
    public async Task SendShiftAssignedEmailAsync_EmptyRecipient_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.SendShiftAssignedEmailAsync(
            "", "John", "Morning", new DateOnly(2026, 3, 15),
            new TimeOnly(7, 0), new TimeOnly(15, 0));

        result.Should().BeFalse();
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_NullHttpClientFactory_ThrowsArgumentNullException()
    {
        var act = () => new MailService(
            null!,
            _loggerMock.Object,
            _configuration,
            _emailConfigServiceMock.Object,
            _emailApiLogServiceMock.Object,
            _localizerMock.Object,
            _emailTemplateServiceMock.Object,
            _emailQueue,
            _localizationMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new MailService(
            _httpClientFactoryMock.Object,
            null!,
            _configuration,
            _emailConfigServiceMock.Object,
            _emailApiLogServiceMock.Object,
            _localizerMock.Object,
            _emailTemplateServiceMock.Object,
            _emailQueue,
            _localizationMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        var act = () => new MailService(
            _httpClientFactoryMock.Object,
            _loggerMock.Object,
            null!,
            _emailConfigServiceMock.Object,
            _emailApiLogServiceMock.Object,
            _localizerMock.Object,
            _emailTemplateServiceMock.Object,
            _emailQueue,
            _localizationMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullTenantResolver_DoesNotThrow()
    {
        var act = () => new MailService(
            _httpClientFactoryMock.Object,
            _loggerMock.Object,
            _configuration,
            _emailConfigServiceMock.Object,
            _emailApiLogServiceMock.Object,
            _localizerMock.Object,
            _emailTemplateServiceMock.Object,
            _emailQueue,
            _localizationMock.Object,
            tenantResolver: null);

        act.Should().NotThrow();
    }
}
