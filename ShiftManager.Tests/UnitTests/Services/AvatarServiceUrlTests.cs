using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Regression tests for <see cref="AvatarService.GetAvatarUrl"/>:
/// when <paramref name="ownerCompanyId"/> is supplied the URL must be built from the
/// OWNER's company folder, not from the viewer's active tenant returned by
/// <see cref="ITenantResolver.GetCurrentTenantId"/>.
///
/// This matters for cross-company admin views where the viewer's tenant differs from
/// the avatar-owner's company — without the fix, the path would resolve to the wrong
/// company folder and the image would 404.
/// </summary>
public sealed class AvatarServiceUrlTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private Mock<ITenantResolver> _tenantResolverMock = null!;
    private AvatarService _sut = null!;

    // Viewer's active tenant — intentionally different from the owner's company
    private const int ViewerTenantId = 99;
    private const int OwnerCompanyId = 7;
    private const int UserId = 42;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Resolver returns the VIEWER's tenant (99), not the owner's company (7)
        _tenantResolverMock = new Mock<ITenantResolver>();
        _tenantResolverMock.Setup(r => r.GetCurrentTenantId()).Returns(ViewerTenantId);

        // IWebHostEnvironment is not exercised by GetAvatarUrl — pass a null mock
        var envMock = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();

        _sut = new AvatarService(_db, _tenantResolverMock.Object, envMock.Object,
            NullLogger<AvatarService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void GetAvatarUrl_WithOwnerCompanyId_UsesOwnerCompanyInPath()
    {
        // Resolver returns 99 (viewer's tenant); the URL must use ownerCompanyId = 7
        var url = _sut.GetAvatarUrl(UserId, $"{UserId}.jpg", thumbnail: false, ownerCompanyId: OwnerCompanyId);

        url.Should().Be($"/avatars/{OwnerCompanyId}/{UserId}.jpg",
            because: "the avatar file is stored under the owner's company folder, not the viewer's tenant");
    }

    [Fact]
    public void GetAvatarUrl_WithOwnerCompanyId_Thumbnail_UsesOwnerCompanyInPath()
    {
        var url = _sut.GetAvatarUrl(UserId, $"{UserId}.jpg", thumbnail: true, ownerCompanyId: OwnerCompanyId);

        url.Should().Be($"/avatars/{OwnerCompanyId}/{UserId}_thumb.jpg",
            because: "thumbnail path also must use the owner's company folder");
    }

    [Fact]
    public void GetAvatarUrl_WithoutOwnerCompanyId_FallsBackToResolverTenant()
    {
        // Backward-compatibility: when no ownerCompanyId is supplied, fall back to the active tenant
        var url = _sut.GetAvatarUrl(UserId, $"{UserId}.jpg", thumbnail: false);

        url.Should().Be($"/avatars/{ViewerTenantId}/{UserId}.jpg",
            because: "callers that don't supply ownerCompanyId retain the old resolver-based behaviour");
    }

    [Fact]
    public void GetAvatarUrl_EmptyAvatarFileName_ReturnsEmptyString()
    {
        // Edge case: no avatar → empty string regardless of ownerCompanyId
        var url = _sut.GetAvatarUrl(UserId, avatarFileName: null, thumbnail: false, ownerCompanyId: OwnerCompanyId);

        url.Should().BeEmpty(because: "null/empty avatarFileName means no avatar was uploaded");
    }
}
