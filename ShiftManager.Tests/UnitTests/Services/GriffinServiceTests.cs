using System.Net;
using System.Net.Sockets;
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

    // ---------- helpers ----------

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

    /// <summary>
    /// Captures every outgoing HttpRequestMessage so tests can assert URL/method/headers.
    /// </summary>
    private HttpClient CreateCapturingMockHttpClient(
        HttpStatusCode statusCode,
        string content,
        List<HttpRequestMessage> capturedRequests)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                capturedRequests.Add(req);
                return new HttpResponseMessage { StatusCode = statusCode, Content = new StringContent(content) };
            });
        return new HttpClient(handler.Object);
    }

    private HttpClient CreateThrowingMockHttpClient(Exception toThrow)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(toThrow);
        return new HttpClient(handler.Object);
    }

    // ---------- BuildAuthenticationUrl ----------

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
        var tokenConsumerUrl = "https://myapp.local/auth/callback?returnUrl=%2Fdashboard&foo=bar";

        var result = _service.BuildAuthenticationUrl(GriffinBaseUrl, tokenConsumerUrl);

        result.Should().EndWith(tokenConsumerUrl);
    }

    // ---------- ExchangeTokenAsync: URL CONTRACT (regression guard for the bug we just fixed) ----------

    [Fact]
    public async Task ExchangeTokenAsync_UsesTokenQueryParameter_NotHash()
    {
        var requests = new List<HttpRequestMessage>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => CreateCapturingMockHttpClient(HttpStatusCode.OK, "real-jwt", requests));

        await _service.ExchangeTokenAsync("hashed-token-A", GriffinBaseUrl, 10);

        requests.Should().HaveCount(1);
        var url = requests[0].RequestUri!.ToString();
        url.Should().Contain("?token=hashed-token-A");
        url.Should().NotContain("?hash=");
    }

    [Fact]
    public async Task ExchangeTokenAsync_UrlEncodesTokenValue()
    {
        var requests = new List<HttpRequestMessage>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => CreateCapturingMockHttpClient(HttpStatusCode.OK, "jwt", requests));

        await _service.ExchangeTokenAsync("a b+c/d", GriffinBaseUrl, 10);

        // Use OriginalString (the URI as constructed) rather than ToString() — the latter is
        // a display-friendly rendering that decodes %20→space and would mask encoding errors
        // even when the bytes actually sent on the wire are correct. OriginalString reflects
        // exactly what HttpClient transmitted.
        var url = requests[0].RequestUri!.OriginalString;
        url.Should().Contain("token=a%20b%2Bc%2Fd");
    }

    // ---------- ExchangeTokenAsync: response shape tolerance (item #3) ----------

    [Fact]
    public async Task ExchangeTokenAsync_PlainTextJwt_ReturnsIt()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, "eyJhbGciOiJIUzI1NiJ9.payload.sig"));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value.Should().Be("eyJhbGciOiJIUzI1NiJ9.payload.sig");
    }

    [Fact]
    public async Task ExchangeTokenAsync_JsonQuotedJwt_ReturnsUnquoted()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, "\"eyJ.payload.sig\""));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value.Should().Be("eyJ.payload.sig");
    }

    [Fact]
    public async Task ExchangeTokenAsync_JsonObject_WithTokenField_ExtractsJwt()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, """{"token":"eyJ.payload.sig","expiresAt":"2026-01-01"}"""));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value.Should().Be("eyJ.payload.sig");
    }

    [Theory]
    [InlineData("jwt")]
    [InlineData("JWT")]
    [InlineData("accessToken")]
    [InlineData("access_token")]
    [InlineData("AccessToken")]
    [InlineData("Token")]
    public async Task ExchangeTokenAsync_JsonObject_WithAlternativeFieldNames_ExtractsJwt(string fieldName)
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, $"{{\"{fieldName}\":\"eyJ.payload.sig\"}}"));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value.Should().Be("eyJ.payload.sig");
    }

    [Fact]
    public async Task ExchangeTokenAsync_EmptyBody_FailsWithEmptyResponse()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, "   "));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.EmptyResponse);
        result.Error.Stage.Should().Be(GriffinStage.TokenExchange);
        result.Error.ErrorToken.Should().Be("GRIFFIN-TOKENEXCHANGE-300");
    }

    // ---------- ExchangeTokenAsync: HTTP status classification ----------

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, GriffinErrorCode.HttpBadRequest, "GRIFFIN-TOKENEXCHANGE-200")]
    [InlineData(HttpStatusCode.Unauthorized, GriffinErrorCode.HttpUnauthorized, "GRIFFIN-TOKENEXCHANGE-201")]
    [InlineData(HttpStatusCode.Forbidden, GriffinErrorCode.HttpForbidden, "GRIFFIN-TOKENEXCHANGE-202")]
    [InlineData(HttpStatusCode.NotFound, GriffinErrorCode.HttpNotFound, "GRIFFIN-TOKENEXCHANGE-203")]
    [InlineData(HttpStatusCode.InternalServerError, GriffinErrorCode.HttpServerError, "GRIFFIN-TOKENEXCHANGE-204")]
    [InlineData(HttpStatusCode.BadGateway, GriffinErrorCode.HttpServerError, "GRIFFIN-TOKENEXCHANGE-204")]
    [InlineData(HttpStatusCode.ServiceUnavailable, GriffinErrorCode.HttpServerError, "GRIFFIN-TOKENEXCHANGE-204")]
    [InlineData(HttpStatusCode.GatewayTimeout, GriffinErrorCode.HttpServerError, "GRIFFIN-TOKENEXCHANGE-204")]
    [InlineData(HttpStatusCode.Conflict, GriffinErrorCode.HttpOther, "GRIFFIN-TOKENEXCHANGE-205")]
    public async Task ExchangeTokenAsync_HttpStatus_MapsToSpecificErrorCode(
        HttpStatusCode status, GriffinErrorCode expected, string expectedToken)
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(status, "error body"));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(expected);
        result.Error.ErrorToken.Should().Be(expectedToken);
        result.Error.HttpStatus.Should().Be((int)status);
        result.Error.ResponsePreview.Should().Contain("error body");
    }

    // ---------- ExchangeTokenAsync: network classification ----------

    [Fact]
    public async Task ExchangeTokenAsync_Timeout_MapsToNetworkTimeout()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateThrowingMockHttpClient(new TaskCanceledException("request timed out")));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.NetworkTimeout);
    }

    [Fact]
    public async Task ExchangeTokenAsync_DnsFailure_MapsToDnsFailure()
    {
        var dnsEx = new HttpRequestException("no such host", new SocketException((int)SocketError.HostNotFound));
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateThrowingMockHttpClient(dnsEx));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Error!.Code.Should().Be(GriffinErrorCode.NetworkDnsFailure);
    }

    [Fact]
    public async Task ExchangeTokenAsync_ConnectionRefused_MapsToConnectionRefused()
    {
        var refused = new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused));
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateThrowingMockHttpClient(refused));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Error!.Code.Should().Be(GriffinErrorCode.NetworkConnectionRefused);
    }

    // ---------- ExtractTokenFromResponse (pure unit) ----------

    [Theory]
    [InlineData("eyJ.payload.sig", "eyJ.payload.sig")]
    [InlineData("\"eyJ.payload.sig\"", "eyJ.payload.sig")]
    [InlineData("   eyJ.payload.sig   ", "eyJ.payload.sig")]
    [InlineData("""{"token":"eyJ.payload.sig"}""", "eyJ.payload.sig")]
    [InlineData("""{"jwt":"eyJ.payload.sig","other":1}""", "eyJ.payload.sig")]
    [InlineData("""{"accessToken":"eyJ.payload.sig"}""", "eyJ.payload.sig")]
    public void ExtractTokenFromResponse_KnownShapes_ExtractsToken(string body, string expected)
    {
        GriffinService.ExtractTokenFromResponse(body).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("""{"other":"value"}""")]
    public void ExtractTokenFromResponse_EmptyOrUnknownShape_ReturnsNullOrGarbage(string body)
    {
        var result = GriffinService.ExtractTokenFromResponse(body);
        // Either null (empty) or fallback to trimmed body — both are acceptable;
        // we just want to confirm we don't throw.
        if (body.Trim().Length == 0 || body.Trim() == "\"\"")
            result.Should().BeNull();
        else
            result.Should().NotBe(""); // fallback, passed through to caller
    }

    // ---------- ValidateTokenAsync: case-insensitive truthy/falsy parsing (item #4) ----------

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("  true  ")]
    [InlineData("\"true\"")]
    [InlineData("\"TRUE\"")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("YES")]
    public async Task ValidateTokenAsync_TruthyResponses_ReturnsTrue(string body)
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, body));

        var result = await _service.ValidateTokenAsync("tok", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData("\"false\"")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("NO")]
    public async Task ValidateTokenAsync_FalsyResponses_ReturnsFalseWithoutError(string body)
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, body));

        var result = await _service.ValidateTokenAsync("tok", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();      // call succeeded
        result.Value.Should().BeFalse();       // token not valid (clean negative)
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("{}")]
    [InlineData("undefined")]
    public async Task ValidateTokenAsync_UnexpectedShape_Fails(string body)
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, body));

        var result = await _service.ValidateTokenAsync("tok", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.UnexpectedResponseShape);
        result.Error.Stage.Should().Be(GriffinStage.TokenValidation);
    }

    [Fact]
    public async Task ValidateTokenAsync_ServerError_Fails()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.InternalServerError, "boom"));

        var result = await _service.ValidateTokenAsync("tok", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.HttpServerError);
    }

    // ---------- GetClaimsAsync ----------

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
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, claimsJson));

        var result = await _service.GetClaimsAsync("valid-token", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value!.UniqueID.Should().Be("jdoe@8200");
        result.Value.EmailAddress.Should().Be("jdoe@test.local");
    }

    [Fact]
    public async Task GetClaimsAsync_ForbiddenResponse_FailsHttpForbidden()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.Forbidden, ""));

        var result = await _service.GetClaimsAsync("token", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.HttpForbidden);
        result.Error.Stage.Should().Be(GriffinStage.ClaimsRetrieval);
    }

    [Fact]
    public async Task GetClaimsAsync_InvalidJson_FailsUnreadable()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, "<html>nope</html>"));

        var result = await _service.GetClaimsAsync("token", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.UnreadableResponse);
    }

    [Fact]
    public async Task GetClaimsAsync_EmptyBody_FailsEmpty()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, ""));

        var result = await _service.GetClaimsAsync("token", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.EmptyResponse);
    }

    [Fact]
    public async Task GetClaimsAsync_MissingEmail_FailsMissingEmail()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK,
                """{"UniqueID":"u1","EmailAddress":"","DisplayName":"D","GivenName":"G","iat":"now"}"""));

        var result = await _service.GetClaimsAsync("token", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.MissingEmailAddress);
    }

    [Fact]
    public async Task GetClaimsAsync_MissingUniqueId_FailsMissingUniqueId()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK,
                """{"UniqueID":"","EmailAddress":"a@b.c","DisplayName":"D","GivenName":"G","iat":"now"}"""));

        var result = await _service.GetClaimsAsync("token", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.MissingUniqueId);
    }

    // ---------- ValidateAndGetClaimsAsync ----------

    [Fact]
    public async Task ValidateAndGetClaimsAsync_CachesClaimsOnSuccess()
    {
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
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object));

        var r1 = await _service.ValidateAndGetClaimsAsync("token123", GriffinBaseUrl, 10);
        r1.Success.Should().BeTrue();
        r1.Value!.EmailAddress.Should().Be("jdoe@test.local");

        var r2 = await _service.ValidateAndGetClaimsAsync("token123", GriffinBaseUrl, 10);
        r2.Success.Should().BeTrue();

        callCount.Should().Be(2); // first call = validate+getClaims; second call served from cache
    }

    [Fact]
    public async Task ValidateAndGetClaimsAsync_TokenInvalid_FailsUnauthorized()
    {
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, "\"false\""));

        var result = await _service.ValidateAndGetClaimsAsync("bad-token", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.HttpUnauthorized);
    }

    // ---------- AuthenticateUserAsync ----------

    [Fact]
    public async Task AuthenticateUserAsync_MissingEmail_FailsWithMissingEmail()
    {
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
                    Content = new StringContent("""{"UniqueID":"u","EmailAddress":"","DisplayName":"D","GivenName":"G","iat":"now"}""")
                };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        var config = new GriffinConfig { CompanyId = CompanyId, BaseUrl = GriffinBaseUrl, TimeoutSeconds = 10 };

        var result = await _service.AuthenticateUserAsync("token", config, "127.0.0.1");

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.MissingEmailAddress);
    }

    [Fact]
    public async Task AuthenticateUserAsync_UserNotFound_AutoProvisionDisabled_FailsUserNotRegistered()
    {
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
                    Content = new StringContent("""{"UniqueID":"u","EmailAddress":"new@test.local","DisplayName":"D","GivenName":"G","iat":"now"}""")
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

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.UserNotRegistered);
        result.Error.Stage.Should().Be(GriffinStage.UserLookup);
    }

    [Fact]
    public async Task AuthenticateUserAsync_InactiveUser_FailsUserDeactivated()
    {
        _db.Users.Add(new AppUser
        {
            Id = 1, CompanyId = CompanyId,
            Email = "jdoe@test.local", DisplayName = "John Doe",
            Role = UserRole.Employee, IsActive = false
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
                    Content = new StringContent("""{"UniqueID":"u","EmailAddress":"jdoe@test.local","DisplayName":"D","GivenName":"G","iat":"now"}""")
                };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        var config = new GriffinConfig { CompanyId = CompanyId, BaseUrl = GriffinBaseUrl, TimeoutSeconds = 10 };

        var result = await _service.AuthenticateUserAsync("token", config, "127.0.0.1");

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.UserDeactivated);
    }

    [Fact]
    public async Task AuthenticateUserAsync_ExistingUser_ReturnsClaimsPrincipal()
    {
        _db.Users.Add(new AppUser
        {
            Id = 1, CompanyId = CompanyId,
            Email = "jdoe@test.local", DisplayName = "John Doe",
            Role = UserRole.Employee, IsActive = true, RoleTemplateId = null
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
                    Content = new StringContent("""{"UniqueID":"jdoe@8200","EmailAddress":"jdoe@test.local","DisplayName":"John Doe","GivenName":"John","iat":"now"}""")
                };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object));
        _hierarchyServiceMock.Setup(h => h.GetUserHierarchyContextAsync(1))
            .ReturnsAsync((UserHierarchyContext?)null);

        var config = new GriffinConfig { CompanyId = CompanyId, BaseUrl = GriffinBaseUrl, TimeoutSeconds = 10 };

        var result = await _service.AuthenticateUserAsync("token", config, "127.0.0.1");

        result.Success.Should().BeTrue();
        result.Value!.Identity!.IsAuthenticated.Should().BeTrue();
        result.Value.FindFirst("CompanyId")!.Value.Should().Be(CompanyId.ToString());
        result.Value.FindFirst("AuthMethod")!.Value.Should().Be("Griffin");
        result.Value.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value.Should().Be("jdoe@test.local");

        _securityLoggerMock.Verify(l => l.LogAuthenticationSuccess(
            1, "jdoe@test.local", It.IsAny<string>(), "127.0.0.1"), Times.Once);
    }

    // ---------- ErrorToken format sanity ----------

    [Fact]
    public void GriffinApiError_ErrorTokenFormat_IsStable()
    {
        var err = new GriffinApiError(GriffinStage.TokenExchange, GriffinErrorCode.HttpNotFound, "detail");
        err.ErrorToken.Should().Be("GRIFFIN-TOKENEXCHANGE-203");
    }
}
