using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for <see cref="ActiveCompanySelectorService"/>.
/// Uses a FK-off SQLite in-memory harness so parent Company/User rows need not be seeded
/// (mirrors CompanyMembershipServiceTests). An IHttpContextAccessor backed by a
/// DefaultHttpContext is used so cookie reads/writes happen on the same in-process object.
/// </summary>
public sealed class ActiveCompanySelectorServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a user + primary membership.
    /// </summary>
    private async Task SeedUserAsync(int userId, int companyId)
    {
        _db.Users.Add(new AppUser
        {
            Id = userId, CompanyId = companyId,
            Email = $"u{userId}@x", DisplayName = $"U{userId}"
        });
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            UserId = userId, CompanyId = companyId, IsPrimary = true
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Builds a DefaultHttpContext with the given NameIdentifier claim and an optional
    /// pre-existing request cookie (simulates "cookie already set in browser").
    /// </summary>
    private static DefaultHttpContext MakeHttpContext(int userId, string? existingCookieValue = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"));

        if (existingCookieValue != null)
        {
            // Inject a request cookie so GetSelectedCompanyId() can read it back.
            ctx.Request.Headers["Cookie"] = $"{ActiveCompanySelectorService.CookieName}={existingCookieValue}";
        }

        return ctx;
    }

    private IActiveCompanySelectorService MakeSut(DefaultHttpContext ctx)
    {
        var accessor = new HttpContextAccessorStub(ctx);
        var membershipService = new CompanyMembershipService(
            _db, NullLogger<CompanyMembershipService>.Instance);
        return new ActiveCompanySelectorService(accessor, membershipService);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Tests
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectCompanyAsync_ValidMember_ReturnsTrueAndWritesCookie()
    {
        // Arrange
        await SeedUserAsync(userId: 1, companyId: 10);
        var ctx = MakeHttpContext(userId: 1);
        var sut = MakeSut(ctx);

        // Act
        var result = await sut.SelectCompanyAsync(10);

        // Assert
        result.Should().BeTrue("user 1 is a member of company 10");
        ctx.Response.Headers.ContainsKey("Set-Cookie").Should().BeTrue(
            "a successful selection must write the member_selected_company cookie");
    }

    [Fact]
    public async Task SelectCompanyAsync_NotAMember_ReturnsFalseAndNoCookie()
    {
        // Arrange
        await SeedUserAsync(userId: 2, companyId: 10);
        var ctx = MakeHttpContext(userId: 2);
        var sut = MakeSut(ctx);

        // Act: company 99 — user 2 is NOT a member
        var result = await sut.SelectCompanyAsync(99);

        // Assert
        result.Should().BeFalse("user 2 is not a member of company 99");
        ctx.Response.Headers.ContainsKey("Set-Cookie").Should().BeFalse(
            "no cookie must be written when membership check fails");
    }

    [Fact]
    public void GetSelectedCompanyId_WithCookiePresent_ReturnsValue()
    {
        // Arrange: inject a request cookie
        var ctx = MakeHttpContext(userId: 3, existingCookieValue: "10");
        var sut = MakeSut(ctx);

        // Act
        var id = sut.GetSelectedCompanyId();

        // Assert
        id.Should().Be(10);
    }

    [Fact]
    public void GetSelectedCompanyId_NoCookie_ReturnsNull()
    {
        var ctx = MakeHttpContext(userId: 4);
        var sut = MakeSut(ctx);

        sut.GetSelectedCompanyId().Should().BeNull();
    }

    [Fact]
    public async Task ClearSelectionAsync_DeletesCookie()
    {
        // Arrange
        var ctx = MakeHttpContext(userId: 5, existingCookieValue: "10");
        var sut = MakeSut(ctx);

        // Act
        await sut.ClearSelectionAsync();

        // Assert: Response.Cookies.Delete emits a Set-Cookie directive that EXPIRES the cookie.
        // ASP.NET Core renders this as the named cookie with an empty value plus an expiry in the
        // past (Unix epoch) and/or max-age=0. Asserting on the delete directive — not just the
        // name's presence — proves the cookie is actually being removed, not re-set.
        var setCookie = ctx.Response.Headers["Set-Cookie"].ToString();
        setCookie.Should().Contain(ActiveCompanySelectorService.CookieName,
            "the delete directive must target the member_selected_company cookie");

        var lower = setCookie.ToLowerInvariant();
        (lower.Contains("expires=thu, 01 jan 1970") || lower.Contains("max-age=0"))
            .Should().BeTrue(
                $"ClearSelectionAsync must EXPIRE the cookie (past expiry or max-age=0), but Set-Cookie was: {setCookie}");
    }

    [Fact]
    public async Task SelectCompanyAsync_NullHttpContext_ReturnsFalseAndNoCookie()
    {
        // Arrange: an accessor whose HttpContext is null (e.g. background/non-request context).
        var accessor = new HttpContextAccessorStub(null);
        var membershipService = new CompanyMembershipService(
            _db, NullLogger<CompanyMembershipService>.Instance);
        var sut = new ActiveCompanySelectorService(accessor, membershipService);

        // Act
        var result = await sut.SelectCompanyAsync(10);

        // Assert: no context → no user, no cookie write, returns false.
        result.Should().BeFalse("with no HttpContext the selector cannot resolve a user or write a cookie");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Minimal IHttpContextAccessor stub (no Moq dependency on a sealed interface)
    // ────────────────────────────────────────────────────────────────────────────

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
        public HttpContextAccessorStub(HttpContext? ctx) => HttpContext = ctx;
    }
}
