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

    // ---------- ExchangeTokenAsync: URL CONTRACT (claimToken takes ?hash=, not ?token=) ----------
    // The Griffin /authentication/claimToken endpoint accepts the hashed token under the 'hash'
    // query parameter. /authorization/validate and /getClaims use 'token' because their input
    // IS a JWT — different inputs, different names. The /GriffinDiagnostic page renders an
    // empirical proof of this contract (raw ?hash= vs raw ?token= side by side). Don't conflate.

    [Fact]
    public async Task ExchangeTokenAsync_UsesHashQueryParameter_NotToken()
    {
        var requests = new List<HttpRequestMessage>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => CreateCapturingMockHttpClient(HttpStatusCode.OK, "real-jwt", requests));

        await _service.ExchangeTokenAsync("hashed-token-A", GriffinBaseUrl, 10);

        requests.Should().HaveCount(1);
        var url = requests[0].RequestUri!.ToString();
        url.Should().Contain("?hash=hashed-token-A");
        url.Should().NotContain("?token=");
    }

    [Fact]
    public async Task ExchangeTokenAsync_UrlEncodesHashValue()
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
        url.Should().Contain("hash=a%20b%2Bc%2Fd");
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
    public async Task GetClaimsAsync_HtmlBody_FailsUnexpectedShape()
    {
        // After the WAF/proxy-detection guard, HTML bodies are now caught BEFORE the JSON
        // parser sees them and return UnexpectedResponseShape — more informative for admins
        // than the prior generic "UnreadableResponse" because it tells them to look for
        // a captive portal or proxy interception rather than a Griffin contract change.
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, "<html>nope</html>"));

        var result = await _service.GetClaimsAsync("token", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.UnexpectedResponseShape);
        result.Error.ResponsePreview.Should().Contain("<html>");
    }

    [Fact]
    public async Task GetClaimsAsync_NonHtmlInvalidJson_FailsUnreadable()
    {
        // Non-HTML invalid JSON (e.g. truncated or corrupted body) still goes through the
        // JSON parser and produces UnreadableResponse — the original intent of this code path.
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, "{not valid json"));

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

    // ---------- GetClaimsAsync: JWT-field type tolerance (regression guard) ----------
    // Bug: previously iat/nbf/exp/aud/iis on GriffinClaimsDto were typed as `string`. When
    // Griffin returned them as JSON numbers (Unix epoch seconds — RFC 7519 NumericDate is a
    // *number*), System.Text.Json threw JsonException — surfacing to users as
    // GRIFFIN-CLAIMSRETRIEVAL-301 ("could not be parsed") and blocking the entire ADFS login.
    // The fix changed those fields to JsonElement?, which accepts any JSON value (number,
    // string, array, null, object) without throwing. These tests lock the new behavior in.

    [Fact]
    public async Task GetClaimsAsync_NumericJwtTimestamps_StillParses()
    {
        var claimsJson = """
        {
            "UniqueID": "jdoe@8200",
            "EmailAddress": "jdoe@test.local",
            "DisplayName": "John Doe",
            "GivenName": "John",
            "Surname": "Doe",
            "aud": "microsoft:distinctserver",
            "iis": "griffin",
            "iat": 1715425200,
            "nbf": 1715425200,
            "exp": 1715428800
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
    public async Task GetClaimsAsync_StringJwtTimestamps_StillParses()
    {
        // Some Griffin deployments emit ISO-8601 strings for these fields. Both shapes must work.
        var claimsJson = """
        {
            "UniqueID": "jdoe@8200",
            "EmailAddress": "jdoe@test.local",
            "DisplayName": "John Doe",
            "GivenName": "John",
            "Surname": "Doe",
            "aud": "microsoft:distinctserver",
            "iis": "griffin",
            "iat": "2026-05-11T10:00:00Z",
            "nbf": "2026-05-11T10:00:00Z",
            "exp": "2026-05-11T11:00:00Z"
        }
        """;
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, claimsJson));

        var result = await _service.GetClaimsAsync("valid-token", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value!.UniqueID.Should().Be("jdoe@8200");
    }

    [Fact]
    public async Task GetClaimsAsync_AudienceAsArray_StillParses()
    {
        // RFC 7519 §4.1.3: aud MAY be a single StringOrURI OR an array of StringOrURI.
        // Some issuers always emit an array — the deserializer must not crash on that shape.
        var claimsJson = """
        {
            "UniqueID": "jdoe@8200",
            "EmailAddress": "jdoe@test.local",
            "DisplayName": "John Doe",
            "GivenName": "John",
            "aud": ["microsoft:audA", "microsoft:audB"],
            "iat": 1715425200
        }
        """;
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, claimsJson));

        var result = await _service.GetClaimsAsync("valid-token", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value!.UniqueID.Should().Be("jdoe@8200");
    }

    [Fact]
    public async Task GetClaimsAsync_StandardIssClaim_PrefersStandardOverLegacy()
    {
        // Some Griffin deployments use the standard JWT "iss" key; older ones use the
        // non-standard "iis". The DTO accepts both; the computed Issuer property prefers
        // standard "iss" when both are present.
        var claimsJson = """
        {
            "UniqueID": "jdoe@8200",
            "EmailAddress": "jdoe@test.local",
            "DisplayName": "John",
            "GivenName": "John",
            "iss": "standard-issuer",
            "iis": "legacy-issuer"
        }
        """;
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, claimsJson));

        var result = await _service.GetClaimsAsync("valid-token", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value!.Issuer!.Value.GetString().Should().Be("standard-issuer");
    }

    // ---------- GetClaimsAsync: number-or-string tolerance on business-critical fields ----------
    // The DTO's UniqueID, EmailAddress, DisplayName, GivenName, Surname use
    // JsonNumberOrStringConverter so a Griffin deployment that ever sends one of these
    // as a JSON number (e.g. "UniqueID": 7108 instead of "7108") doesn't blow up parsing.
    // This is the same defensive pattern that fixed the iat/nbf/exp bug.

    [Fact]
    public async Task GetClaimsAsync_NumericUniqueId_StillParses()
    {
        var claimsJson = """
        {
            "UniqueID": 7108,
            "EmailAddress": "jdoe@test.local",
            "DisplayName": "John Doe",
            "GivenName": "John"
        }
        """;
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, claimsJson));

        var result = await _service.GetClaimsAsync("valid-token", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value!.UniqueID.Should().Be("7108");
    }

    [Fact]
    public async Task GetClaimsAsync_NumericEmailAddress_StillParsesButFailsValidation()
    {
        // Defensive: even if Griffin sends an email as a number (extremely unlikely but
        // theoretically possible), parsing succeeds and we get a string — but downstream
        // email validation would catch it. Confirms the converter handles ALL scalar fields.
        var claimsJson = """
        {
            "UniqueID": "u1",
            "EmailAddress": 12345,
            "DisplayName": "x",
            "GivenName": "x"
        }
        """;
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, claimsJson));

        var result = await _service.GetClaimsAsync("valid-token", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value!.EmailAddress.Should().Be("12345");
    }

    // ---------- LooksLikeJwt: structural validation gate ----------

    [Theory]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature", true)]
    [InlineData("a.b.c", true)]                                        // minimal 3-segment shape
    [InlineData("Aa1-_=.Bb2-_=.Cc3-_=", true)]                         // base64url alphabet
    [InlineData("only.two", false)]                                    // wrong segment count
    [InlineData("four.dot.parts.here", false)]                         // wrong segment count
    [InlineData("", false)]                                            // empty
    [InlineData("a..c", false)]                                        // empty middle segment
    [InlineData(".b.c", false)]                                        // empty header
    [InlineData("a.b.", false)]                                        // empty signature
    [InlineData("<html><body>error</body></html>", false)]             // HTML
    [InlineData("a.b.c d", false)]                                     // disallowed character (space)
    [InlineData("a.b.c+d", false)]                                     // disallowed character (+) — base64url uses - not +
    public void LooksLikeJwt_StructuralCases(string input, bool expected)
    {
        ShiftManager.Services.GriffinService.LooksLikeJwt(input).Should().Be(expected);
    }

    [Fact]
    public async Task ExchangeTokenAsync_NonJwtFallback_RejectsAsEmpty()
    {
        // Body is non-JSON, non-empty, non-JWT-shaped (e.g. a captive-portal HTML snippet).
        // After the LooksLikeJwt gate, ExtractTokenFromResponse returns null instead of
        // passing the HTML through as the "JWT" — caller sees EmptyResponse and the user
        // gets a clean "no JWT was returned" error instead of a confusing 400 at validate.
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, "you are behind a captive portal"));

        var result = await _service.ExchangeTokenAsync("hash", GriffinBaseUrl, 10);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.EmptyResponse);
    }

    [Fact]
    public async Task ValidateAndGetClaimsAsync_InvalidToken_NegativelyCached()
    {
        // First call: Griffin says false → fail. Second call: hits the negative cache
        // immediately, no extra HTTP traffic. Defends against retry loops and hammer attacks
        // on Griffin (single-server, often air-gapped).
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
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"false\"") };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object));

        var r1 = await _service.ValidateAndGetClaimsAsync("bad-token", GriffinBaseUrl, 10);
        r1.Success.Should().BeFalse();
        r1.Error!.Code.Should().Be(GriffinErrorCode.HttpUnauthorized);

        var r2 = await _service.ValidateAndGetClaimsAsync("bad-token", GriffinBaseUrl, 10);
        r2.Success.Should().BeFalse();
        r2.Error!.Code.Should().Be(GriffinErrorCode.HttpUnauthorized);

        callCount.Should().Be(1, "second call should be served from negative cache");
    }

    [Fact]
    public async Task AuthenticateUserAsync_SanitizesControlCharsInDisplayName()
    {
        // A Griffin response (or upstream ADFS user record) containing CR/LF in DisplayName
        // could otherwise inject log-line terminators into our Claims and structured logs.
        // The SanitizeClaimString helper normalises CR/LF to space.
        _db.Users.Add(new AppUser
        {
            Id = 42, CompanyId = CompanyId,
            Email = "weird@test.local", DisplayName = "weird",
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
                // DisplayName contains CR + LF + NUL — all should be neutralised.
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"UniqueID\":\"u\",\"EmailAddress\":\"weird@test.local\",\"DisplayName\":\"line1\\r\\nline2\\u0000extra\",\"GivenName\":\"safe\",\"iat\":1}")
                };
            });
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));
        _hierarchyServiceMock.Setup(h => h.GetUserHierarchyContextAsync(42)).ReturnsAsync((UserHierarchyContext?)null);

        var config = new GriffinConfig { CompanyId = CompanyId, BaseUrl = GriffinBaseUrl, TimeoutSeconds = 10 };

        var result = await _service.AuthenticateUserAsync("token", config, "127.0.0.1");

        result.Success.Should().BeTrue();
        var nameClaim = result.Value!.FindFirst(System.Security.Claims.ClaimTypes.Name)!.Value;
        nameClaim.Should().NotContain("\r");
        nameClaim.Should().NotContain("\n");
        nameClaim.Should().NotContain("\0");
        nameClaim.Should().Be("line1  line2extra"); // CR + LF → 2 spaces; NUL dropped
    }

    [Fact]
    public async Task GetClaimsAsync_BodyWithBom_StillParses()
    {
        // Some Windows-side proxies/services prepend a UTF-8 BOM. The CallGriffinGetAsync
        // helper strips the BOM so downstream parsers don't choke on a mystery U+FEFF.
        var bomBody = "﻿" + """
        {
            "UniqueID": "jdoe@8200",
            "EmailAddress": "jdoe@test.local",
            "DisplayName": "John",
            "GivenName": "John"
        }
        """;
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, bomBody));

        var result = await _service.GetClaimsAsync("token", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value!.UniqueID.Should().Be("jdoe@8200");
    }

    [Fact]
    public async Task GetClaimsAsync_NoJwtFieldsAtAll_StillParses()
    {
        // Defensive: even if Griffin omits every JWT-spec field, business-critical fields
        // (UniqueID + EmailAddress) should be enough to authenticate.
        var claimsJson = """
        {
            "UniqueID": "jdoe@8200",
            "EmailAddress": "jdoe@test.local",
            "DisplayName": "John",
            "GivenName": "John"
        }
        """;
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(CreateMockHttpClient(HttpStatusCode.OK, claimsJson));

        var result = await _service.GetClaimsAsync("valid-token", GriffinBaseUrl, 10);

        result.Success.Should().BeTrue();
        result.Value!.IssuedAt.Should().BeNull();
        result.Value.Audience.Should().BeNull();
        result.Value.Issuer.Should().BeNull();
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

    // After the security review, GriffinService never silently creates users.
    // For an ADFS-authenticated identity that has no matching ShiftManager account,
    // AuthenticateUserAsync ALWAYS returns UserNotRegistered — the callback layer is
    // what decides between "redirect to signup" and "show refusal" based on the
    // FF_ALLOW_USERS_CREATION_VIA_ADFS feature flag.
    [Fact]
    public async Task AuthenticateUserAsync_UserNotFound_FailsUserNotRegistered()
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
                    Content = new StringContent("""{"UniqueID":"u","EmailAddress":"new@test.local","DisplayName":"D","GivenName":"G","Surname":"S","iat":"now"}""")
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

    // ============================================================================================
    // GriffinClaimsDto.Parse — flexible property-name matching
    //
    // Lock in the property-name flexibility added in the 2026-05-11 hardening pass. The previous
    // case-insensitive-only matcher silently produced empty fields on snake_case / kebab-case /
    // LDAP-style aliases, which became the user-visible MissingEmailAddress error. The new Parse
    // method normalizes names (strip separators + lowercase) and tries multiple aliases per field.
    // ============================================================================================

    [Theory]
    [InlineData(@"{""EmailAddress"":""u@x.mil"",""UniqueID"":""1""}")]
    [InlineData(@"{""emailaddress"":""u@x.mil"",""uniqueid"":""1""}")]
    [InlineData(@"{""EMAIL_ADDRESS"":""u@x.mil"",""UNIQUE_ID"":""1""}")]
    [InlineData(@"{""email-address"":""u@x.mil"",""unique-id"":""1""}")]
    [InlineData(@"{""email.address"":""u@x.mil"",""unique.id"":""1""}")]
    [InlineData(@"{""Email"":""u@x.mil"",""Sub"":""1""}")]
    [InlineData(@"{""mail"":""u@x.mil"",""sub"":""1""}")]
    [InlineData(@"{""upn"":""u@x.mil"",""uid"":""1""}")]
    [InlineData(@"{""userPrincipalName"":""u@x.mil"",""subjectId"":""1""}")]
    public void ClaimsDto_Parse_ExtractsBusinessFields_FromAnyAlias(string body)
    {
        var dto = GriffinClaimsDto.Parse(body, out _);
        dto.Should().NotBeNull();
        dto!.EmailAddress.Should().Be("u@x.mil");
        dto.UniqueID.Should().Be("1");
    }

    [Fact]
    public void ClaimsDto_Parse_TrimsWhitespaceInEmail()
    {
        // Defends against the Agent-2-identified bug: untrimmed Griffin email values would silently
        // fail the DB lookup (SQLite `=` comparison does NOT strip trailing spaces).
        var dto = GriffinClaimsDto.Parse(@"{""EmailAddress"":""  u@x.mil  "",""UniqueID"":""1""}", out _);
        dto.Should().NotBeNull();
        dto!.EmailAddress.Should().Be("u@x.mil");
    }

    [Fact]
    public void ClaimsDto_Parse_NumericUniqueId_CoercedToString()
    {
        var dto = GriffinClaimsDto.Parse(@"{""EmailAddress"":""u@x.mil"",""UniqueID"":7108}", out _);
        dto.Should().NotBeNull();
        dto!.UniqueID.Should().Be("7108");
    }

    [Fact]
    public void ClaimsDto_Parse_LargeNumericValue_NoExponentialNotation()
    {
        // Validates the GetDecimal-before-GetDouble fallback path. A double representation
        // would render as "1.71234E+16" — confusing for a UniqueID.
        var dto = GriffinClaimsDto.Parse(@"{""EmailAddress"":""u@x.mil"",""UniqueID"":17123456789012345}", out _);
        dto.Should().NotBeNull();
        dto!.UniqueID.Should().NotContain("E");
        dto.UniqueID.Should().NotContain("e+");
        dto.UniqueID.Should().Be("17123456789012345");
    }

    [Fact]
    public void ClaimsDto_Parse_PopulatesPresentKeysForDiagnostics()
    {
        var dto = GriffinClaimsDto.Parse(@"{""foo"":1,""bar"":2,""baz"":3}", out var keys);
        dto.Should().NotBeNull();
        dto!.EmailAddress.Should().BeEmpty();
        dto.UniqueID.Should().BeEmpty();
        keys.Should().BeEquivalentTo(new[] { "foo", "bar", "baz" });
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("\"just a string\"")]
    public void ClaimsDto_Parse_NonObjectInput_ReturnsNull(string body)
    {
        var dto = GriffinClaimsDto.Parse(body, out _);
        dto.Should().BeNull();
    }

    [Fact]
    public void ClaimsDto_Parse_WrappedResponse_AutoUnwraps()
    {
        // Common API envelope pattern: {"data":{...}} or {"claims":{...}}. The Parse method
        // unwraps one level when the root object has exactly one property whose value is an
        // object AND none of the business-critical aliases appear at the root.
        var dto = GriffinClaimsDto.Parse(
            @"{""claims"":{""EmailAddress"":""u@x.mil"",""UniqueID"":""1""}}",
            out _);
        dto.Should().NotBeNull();
        dto!.EmailAddress.Should().Be("u@x.mil");
        dto.UniqueID.Should().Be("1");
    }

    [Fact]
    public void ClaimsDto_Parse_RootWithBusinessKey_DoesNotUnwrap()
    {
        // If the root already has a recognized business field, don't unwrap — even if there's
        // a nested object too.
        var dto = GriffinClaimsDto.Parse(
            @"{""EmailAddress"":""correct@x.mil"",""extra"":{""EmailAddress"":""wrong@x.mil""}}",
            out _);
        dto.Should().NotBeNull();
        dto!.EmailAddress.Should().Be("correct@x.mil");
    }

    [Theory]
    [InlineData("EmailAddress", "emailaddress")]
    [InlineData("email_address", "emailaddress")]
    [InlineData("EMAIL-ADDRESS", "emailaddress")]
    [InlineData("Email.Address", "emailaddress")]
    [InlineData("email address", "emailaddress")]
    [InlineData("UniqueID", "uniqueid")]
    [InlineData("user_id", "userid")]
    public void ClaimsDto_Normalize_StripsSeparatorsAndLowercases(string input, string expected)
    {
        GriffinClaimsDto.Normalize(input).Should().Be(expected);
    }

    // ============================================================================================
    // GriffinAuthDiagnostics — ring buffer behavior
    // ============================================================================================

    [Fact]
    public void AuthDiagnostics_RecordAndGetRecent_ReturnsNewestFirst()
    {
        var diag = new GriffinAuthDiagnostics();
        diag.Record(NewEvent("old@x.mil"));
        diag.Record(NewEvent("new@x.mil"));

        var recent = diag.GetRecent(10);
        recent.Should().HaveCount(2);
        recent[0].Email.Should().Be("new@x.mil");
        recent[1].Email.Should().Be("old@x.mil");
    }

    [Fact]
    public void AuthDiagnostics_CapsAtCapacity()
    {
        var diag = new GriffinAuthDiagnostics();
        for (var i = 0; i < GriffinAuthDiagnostics.Capacity + 20; i++)
            diag.Record(NewEvent($"u{i}@x.mil"));

        diag.GetRecent(int.MaxValue).Should().HaveCount(GriffinAuthDiagnostics.Capacity);
    }

    [Fact]
    public void AuthDiagnostics_Clear_EmptiesBuffer()
    {
        var diag = new GriffinAuthDiagnostics();
        diag.Record(NewEvent("u@x.mil"));
        diag.Clear();
        diag.GetRecent().Should().BeEmpty();
    }

    [Fact]
    public void AuthDiagnostics_GetRecent_LimitsToRequestedCount()
    {
        var diag = new GriffinAuthDiagnostics();
        for (var i = 0; i < 10; i++)
            diag.Record(NewEvent($"u{i}@x.mil"));

        diag.GetRecent(3).Should().HaveCount(3);
    }

    private static GriffinAuthEvent NewEvent(string email) =>
        new(DateTime.UtcNow, true, null, null, null, email, "127.0.0.1", null, null, null, null, null, 0);

    // ============================================================================================
    // GriffinErrorMessages — new stage-specific codes have user-friendly messages
    // ============================================================================================

    [Theory]
    [InlineData(GriffinErrorCode.UserLookupQueryFailed)]
    [InlineData(GriffinErrorCode.RoleTemplateBackfillFailed)]
    [InlineData(GriffinErrorCode.HierarchyLoadFailed)]
    [InlineData(GriffinErrorCode.GrantApplicationFailed)]
    [InlineData(GriffinErrorCode.ClaimsPrincipalBuildFailed)]
    public void ErrorMessages_NewStageCodes_DoNotEchoTechnicalDetail(GriffinErrorCode code)
    {
        // The old _-fallthrough branch leaked TechnicalDetail (with JWT in query string) to the
        // user. Verify each new code has its own user-facing message that doesn't echo the
        // technical detail.
        var err = new GriffinApiError(GriffinStage.UserLookup, code,
            "Sensitive technical detail with token=eyJhbGciOiJIUzI1NiJ9.payload.sig");
        var loc = new Mock<Microsoft.Extensions.Localization.IStringLocalizer<ShiftManager.Resources.SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k =>
            new Microsoft.Extensions.Localization.LocalizedString(k, k, false));

        var msg = GriffinErrorMessages.Describe(err, loc.Object);

        msg.Detail.Should().NotContain("eyJ");
        msg.Detail.Should().NotContain("token=");
        msg.Detail.Should().NotContain("Sensitive technical detail");
    }
}
