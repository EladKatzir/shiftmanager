using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.DTOs;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class GriffinServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<ISecurityLogger> _securityLoggerMock;
    private readonly Mock<IAuditLogService> _auditLogServiceMock;
    private readonly Mock<IHierarchyService> _hierarchyServiceMock;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly GriffinService _service;

    private const int CompanyId = 1;
    private const string GriffinBaseUrl = "http://griffin.test.local";

    public GriffinServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _securityLoggerMock = new Mock<ISecurityLogger>();
        _auditLogServiceMock = new Mock<IAuditLogService>();
        _hierarchyServiceMock = new Mock<IHierarchyService>();
        _grantServiceMock = new Mock<IGrantService>();

        _service = new GriffinService(
            _httpClientFactoryMock.Object,
            _db,
            _memoryCache,
            _securityLoggerMock.Object,
            _auditLogServiceMock.Object,
            Mock.Of<ILogger<GriffinService>>(),
            _hierarchyServiceMock.Object,
            _grantServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _memoryCache.Dispose();
    }

    private HttpClient CreateMockHttpClient(HttpStatusCode statusCode, string content)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });

        return new HttpClient(handler.Object);
    }

    // --- BuildAuthenticationUrl ---

    [Fact]
    public void BuildAuthenticationUrl_ConstructsCorrectUrl()
    {
        var tokenConsumerUrl = "https://myapp.local/auth/callback?returnUrl=%2Fdashboard";

        var result = _service.BuildAuthenticationUrl(GriffinBaseUrl, tokenConsumerUrl);

        result.Should().Be($"{GriffinBaseUrl}/authentication?tokenConsumerURL={tokenConsumerUrl}");
    }

    [Fact]
    public void BuildAuthenticationUrl_TrimsTrailingSlash()
    {
        var result = _service.BuildAuthenticationUrl(
            "http://griffin.test.local/",
            "https://myapp.local/callback");

        result.Should().StartWith("http://griffin.test.local/authentication?");
        result.Should().NotContain("//authentication");
    }

    [Fact]
    public void BuildAuthenticationUrl_PreservesTokenConsumerUrlAsIs()
    {
        // The method must NOT encode the tokenConsumerUrl
        var tokenConsumerUrl = "https://myapp.local/auth/callback?returnUrl=%2Fdashboard&foo=bar";

        var result = _service.BuildAuthenticationUrl(GriffinBaseUrl, tokenConsumerUrl);

        result.Should().EndWith(tokenConsumerUrl);
    }

    // --- ValidateTokenAsync ---

    [Fact]
    public async Task ValidateTokenAsync_SuccessResponse_True_ReturnsTrue()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "\"true\"");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ValidateTokenAsync("valid-token", GriffinBaseUrl, 10);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTokenAsync_SuccessResponse_False_ReturnsFalse()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "\"false\"");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ValidateTokenAsync("invalid-token", GriffinBaseUrl, 10);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_HttpError_ReturnsFalse()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.InternalServerError, "error");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ValidateTokenAsync("token", GriffinBaseUrl, 10);

        result.Should().BeFalse();
        _securityLoggerMock.Verify(l => l.LogAuthenticationFailure(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ValidateTokenAsync_Unauthorized_ReturnsFalse()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.Unauthorized, "");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ValidateTokenAsync("token", GriffinBaseUrl, 10);

        result.Should().BeFalse();
    }

    // --- GetClaimsAsync ---

    [Fact]
    public async Task GetClaimsAsync_ValidResponse_ReturnsDeserializedClaims()
    {
        var claimsJson = """
        {
            "UniqueID": "jdoe@8200",
            "EmailAddress": "jdoe@test.local",
            "DisplayName": "John Doe",
            "GivenName": "John",
            "iat": "2026-03-15T10:00:00Z"
        }
        """;
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, claimsJson);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.GetClaimsAsync("valid-token", GriffinBaseUrl, 10);

        result.Should().NotBeNull();
        result!.UniqueID.Should().Be("jdoe@8200");
        result.EmailAddress.Should().Be("jdoe@test.local");
        result.DisplayName.Should().Be("John Doe");
        result.GivenName.Should().Be("John");
    }

    [Fact]
    public async Task GetClaimsAsync_HttpError_ReturnsNull()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.Forbidden, "");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.GetClaimsAsync("token", GriffinBaseUrl, 10);

        result.Should().BeNull();
    }

    // --- ValidateAndGetClaimsAsync ---

    [Fact]
    public async Task ValidateAndGetClaimsAsync_CachesClaimsOnSuccess()
    {
        // First call: returns valid token + claims
        var callCount = 0;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount <= 1)
                {
                    // Validate call
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("\"true\"")
                    };
                }
                // GetClaims call
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"UniqueID":"jdoe@8200","EmailAddress":"jdoe@test.local","DisplayName":"John","GivenName":"John","iat":"now"}""")
                };
            });

        // Return a NEW HttpClient each time — the service uses `using`, which disposes after each call
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object));

        // First call — cache miss, hits API
        var result1 = await _service.ValidateAndGetClaimsAsync("token123", GriffinBaseUrl, 10);
        result1.Should().NotBeNull();
        result1!.EmailAddress.Should().Be("jdoe@test.local");

        // Second call with same token — should hit cache
        var result2 = await _service.ValidateAndGetClaimsAsync("token123", GriffinBaseUrl, 10);
        result2.Should().NotBeNull();
        result2!.EmailAddress.Should().Be("jdoe@test.local");

        // API should only have been called twice (validate + getClaims), not four times
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ValidateAndGetClaimsAsync_InvalidToken_ReturnsNull()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "\"false\"");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ValidateAndGetClaimsAsync("bad-token", GriffinBaseUrl, 10);

        result.Should().BeNull();
    }

    // --- AuthenticateUserAsync ---

    [Fact]
    public async Task AuthenticateUserAsync_MissingEmailAddress_ReturnsNull()
    {
        // Setup: validate succeeds, claims missing EmailAddress
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount <= 1)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"true\"") };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"UniqueID":"jdoe@8200","EmailAddress":"","DisplayName":"John","GivenName":"John","iat":"now"}""")
                };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        var config = new GriffinConfig
        {
            CompanyId = CompanyId,
            BaseUrl = GriffinBaseUrl,
            TimeoutSeconds = 10
        };

        var result = await _service.AuthenticateUserAsync("token", config, "127.0.0.1");

        result.Should().BeNull();
        _securityLoggerMock.Invocations
            .Should().ContainSingle(i => i.Method.Name == "LogSecurityThreat");
    }

    [Fact]
    public async Task AuthenticateUserAsync_UserNotFound_AutoProvisionDisabled_ReturnsNull()
    {
        // Setup: validate succeeds, claims with valid EmailAddress but no user in DB
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount <= 1)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"true\"") };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"UniqueID":"jdoe@8200","EmailAddress":"jdoe@test.local","DisplayName":"John","GivenName":"John","iat":"now"}""")
                };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        var config = new GriffinConfig
        {
            CompanyId = CompanyId,
            BaseUrl = GriffinBaseUrl,
            TimeoutSeconds = 10,
            AutoProvisionUsers = false
        };

        var result = await _service.AuthenticateUserAsync("token", config, "127.0.0.1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateUserAsync_ExistingUser_ReturnsClaimsPrincipal()
    {
        // Seed user
        _db.Users.Add(new AppUser
        {
            Id = 1,
            CompanyId = CompanyId,
            Email = "jdoe@test.local",
            DisplayName = "John Doe",
            Role = UserRole.Employee,
            IsActive = true,
            RoleTemplateId = null
        });
        await _db.SaveChangesAsync();

        // Setup HTTP mock
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount <= 1)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"true\"") };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"UniqueID":"jdoe@8200","EmailAddress":"jdoe@test.local","DisplayName":"John Doe","GivenName":"John","iat":"now"}""")
                };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object));
        _hierarchyServiceMock.Setup(h => h.GetUserHierarchyContextAsync(1))
            .ReturnsAsync((UserHierarchyContext?)null);

        var config = new GriffinConfig
        {
            CompanyId = CompanyId,
            BaseUrl = GriffinBaseUrl,
            TimeoutSeconds = 10
        };

        var result = await _service.AuthenticateUserAsync("token", config, "127.0.0.1");

        result.Should().NotBeNull();
        result!.Identity!.IsAuthenticated.Should().BeTrue();
        result.FindFirst("CompanyId")!.Value.Should().Be(CompanyId.ToString());
        result.FindFirst("AuthMethod")!.Value.Should().Be("Griffin");
        result.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value.Should().Be("jdoe@test.local");

        _securityLoggerMock.Verify(l => l.LogAuthenticationSuccess(
            1, "jdoe@test.local", It.IsAny<string>(), "127.0.0.1"), Times.Once);
    }

    [Fact]
    public async Task AuthenticateUserAsync_InactiveUser_ReturnsNull()
    {
        // Seed inactive user
        _db.Users.Add(new AppUser
        {
            Id = 1,
            CompanyId = CompanyId,
            Email = "jdoe@test.local",
            DisplayName = "John Doe",
            Role = UserRole.Employee,
            IsActive = false // inactive
        });
        await _db.SaveChangesAsync();

        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount <= 1)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"true\"") };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"UniqueID":"jdoe@8200","EmailAddress":"jdoe@test.local","DisplayName":"John","GivenName":"John","iat":"now"}""")
                };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        var config = new GriffinConfig
        {
            CompanyId = CompanyId,
            BaseUrl = GriffinBaseUrl,
            TimeoutSeconds = 10,
            AutoProvisionUsers = false
        };

        var result = await _service.AuthenticateUserAsync("token", config, "127.0.0.1");

        // Inactive user should NOT be found by the query (u.IsActive filter)
        result.Should().BeNull();
    }

    // --- ExchangeTokenAsync ---

    [Fact]
    public async Task ExchangeTokenAsync_Success_ReturnsToken()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "\"real-token-B-value\"");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ExchangeTokenAsync("hashed-token-A", GriffinBaseUrl, 10);

        result.Should().Be("real-token-B-value");
    }

    [Fact]
    public async Task ExchangeTokenAsync_UnquotedResponse_ReturnsToken()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "real-token-B-value");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ExchangeTokenAsync("hashed-token-A", GriffinBaseUrl, 10);

        result.Should().Be("real-token-B-value");
    }

    [Fact]
    public async Task ExchangeTokenAsync_HttpError_ReturnsNull()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.InternalServerError, "error");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ExchangeTokenAsync("hashed-token-A", GriffinBaseUrl, 10);

        result.Should().BeNull();
        _securityLoggerMock.Verify(l => l.LogAuthenticationFailure(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExchangeTokenAsync_EmptyResponse_ReturnsNull()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "   ");
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var result = await _service.ExchangeTokenAsync("hashed-token-A", GriffinBaseUrl, 10);

        result.Should().BeNull();
    }
}
