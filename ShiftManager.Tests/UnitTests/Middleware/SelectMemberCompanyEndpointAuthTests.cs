using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Data;
using ShiftManager.Middleware;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Middleware;

/// <summary>
/// Verifies that <see cref="ApiAuthenticationMiddleware"/> treats /Api/SelectMemberCompany as an
/// internal web-UI endpoint (cookie auth) rather than an API-key endpoint. The middleware's
/// IsInternalWebUiEndpoint is private, so we assert the observable behavior of InvokeAsync:
///  - an authenticated request WITH X-Requested-With is forwarded to _next (recognized + CSRF-ok);
///  - an authenticated request WITHOUT X-Requested-With is rejected with 403 (recognized as
///    internal, but blocked by the CSRF guard — proving it reached the internal branch and did NOT
///    fall through to the API-key path, which would have returned a JSON 401 "Missing X-API-Key").
/// SelectMolecule is included as the reference endpoint these assertions mirror.
/// </summary>
public sealed class SelectMemberCompanyEndpointAuthTests
{
    private sealed class NoopQueue : IBackgroundTaskQueue
    {
        public bool Enqueue(BackgroundTask workItem) => true;
        public System.Threading.Tasks.Task<bool> EnqueueAsync(BackgroundTask workItem, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(true);
        public System.Threading.Channels.ChannelReader<BackgroundTask> Reader
            => System.Threading.Channels.Channel.CreateUnbounded<BackgroundTask>().Reader;
    }

    private static AppDbContext MakeDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    private static (DefaultHttpContext ctx, AppDbContext db) MakeAuthedPost(string path, bool withXrw)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "POST";
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "5") }, "cookie"));
        if (withXrw)
            ctx.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        ctx.Response.Body = new System.IO.MemoryStream();
        return (ctx, MakeDb());
    }

    [Theory]
    [InlineData("/Api/SelectMemberCompany")]
    [InlineData("/Api/SelectMolecule")] // reference endpoint
    public async Task AuthenticatedPost_WithXRequestedWith_IsRecognizedInternal_ForwardsToNext(string path)
    {
        var nextCalled = false;
        var mw = new ApiAuthenticationMiddleware(
            _ => { nextCalled = true; return System.Threading.Tasks.Task.CompletedTask; },
            NullLogger<ApiAuthenticationMiddleware>.Instance, new NoopQueue());

        var (ctx, db) = MakeAuthedPost(path, withXrw: true);
        using (db)
        {
            await mw.InvokeAsync(ctx, db);
        }

        nextCalled.Should().BeTrue("the endpoint is an internal web-UI endpoint and passes the CSRF guard");
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData("/Api/SelectMemberCompany")]
    [InlineData("/Api/SelectMolecule")] // reference endpoint
    public async Task AuthenticatedPost_WithoutXRequestedWith_ReachesInternalBranch_Returns403NotApiKey401(string path)
    {
        var nextCalled = false;
        var mw = new ApiAuthenticationMiddleware(
            _ => { nextCalled = true; return System.Threading.Tasks.Task.CompletedTask; },
            NullLogger<ApiAuthenticationMiddleware>.Instance, new NoopQueue());

        var (ctx, db) = MakeAuthedPost(path, withXrw: false);
        using (db)
        {
            await mw.InvokeAsync(ctx, db);
        }

        // 403 = recognized internal but CSRF-blocked. (If it were NOT recognized as internal it
        // would fall through to the API-key path and return 401 "Missing X-API-Key".)
        ctx.Response.StatusCode.Should().Be(403);
        nextCalled.Should().BeFalse();
    }
}
