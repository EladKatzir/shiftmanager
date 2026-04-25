using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Middleware;

namespace ShiftManager.Tests.UnitTests.Middleware;

/// <summary>
/// Tests that ApiExceptionMiddleware maps each known exception type to the right HTTP status
/// code and includes the correlation ID in the JSON response.
///
/// These tests verify the contract that callers (and the FeedbackModal layer) depend on:
/// the JSON envelope is { error: { code, message, details, correlationId } } for every error path.
/// </summary>
public class ApiExceptionMiddlewareTests
{
    private static (HttpContext ctx, MemoryStream body) BuildApiContext(string correlationId = "test-corr-123")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/v1/test";
        ctx.Request.Method = "GET";
        ctx.Items["CorrelationId"] = correlationId;
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    private static async Task<JsonElement> InvokeAndParse(Exception toThrow, HttpContext ctx, MemoryStream body)
    {
        RequestDelegate next = _ => throw toThrow;
        var middleware = new ApiExceptionMiddleware(next, NullLogger<ApiExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task UnauthorizedAccessException_ProducesUnauthorizedWithCorrelationId()
    {
        var (ctx, body) = BuildApiContext();
        var json = await InvokeAndParse(new UnauthorizedAccessException("nope"), ctx, body);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
        json.GetProperty("error").GetProperty("correlationId").GetString().Should().Be("test-corr-123");
    }

    [Fact]
    public async Task KeyNotFoundException_ProducesNotFound()
    {
        var (ctx, body) = BuildApiContext();
        var json = await InvokeAndParse(new KeyNotFoundException("not here"), ctx, body);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("RESOURCE_NOT_FOUND");
    }

    [Fact]
    public async Task ArgumentNullException_ProducesBadRequest()
    {
        var (ctx, body) = BuildApiContext();
        var json = await InvokeAndParse(new ArgumentNullException("paramX"), ctx, body);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_REQUEST");
        json.GetProperty("error").GetProperty("message").GetString().Should().Contain("paramX");
    }

    [Fact]
    public async Task InvalidOperationException_ProducesBadRequest()
    {
        var (ctx, body) = BuildApiContext();
        var json = await InvokeAndParse(new InvalidOperationException("bad state"), ctx, body);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_REQUEST");
    }

    [Fact]
    public async Task TimeoutException_ProducesGatewayTimeout()
    {
        var (ctx, body) = BuildApiContext();
        var json = await InvokeAndParse(new TimeoutException(), ctx, body);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.GatewayTimeout);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("TIMEOUT");
    }

    [Fact]
    public async Task NotImplementedException_Produces501()
    {
        var (ctx, body) = BuildApiContext();
        var json = await InvokeAndParse(new NotImplementedException(), ctx, body);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.NotImplemented);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task GenericException_ProducesServerErrorWithoutLeakingInternals()
    {
        var (ctx, body) = BuildApiContext();
        var json = await InvokeAndParse(new Exception("internal-secret-detail"), ctx, body);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("SERVER_ERROR");
        var msg = json.GetProperty("error").GetProperty("message").GetString();
        msg.Should().NotContain("internal-secret-detail",
            "the generic 500 path must NOT leak internal exception messages to the client");
    }

    [Fact]
    public async Task NonApiPath_RethrowsToOuterHandler()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/Pages/Calendar/Month";
        ctx.Request.Method = "GET";
        var thrown = new InvalidOperationException("ui-error");

        RequestDelegate next = _ => throw thrown;
        var middleware = new ApiExceptionMiddleware(next, NullLogger<ApiExceptionMiddleware>.Instance);

        Func<Task> act = () => middleware.InvokeAsync(ctx);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message == "ui-error");
    }

    [Fact]
    public async Task DbUpdateConcurrencyException_ProducesConflict()
    {
        var (ctx, body) = BuildApiContext();
        // DbUpdateConcurrencyException(string?, IReadOnlyList<IUpdateEntry>) — pass an empty entries list
        var ex = new DbUpdateConcurrencyException("conflict", new List<Microsoft.EntityFrameworkCore.Update.IUpdateEntry>());
        var json = await InvokeAndParse(ex, ctx, body);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("CONCURRENCY_CONFLICT");
    }
}
