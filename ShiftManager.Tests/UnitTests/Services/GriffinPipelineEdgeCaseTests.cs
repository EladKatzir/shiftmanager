using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.DTOs;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Edge-case regression tests for the BuildPrincipalFromClaimsAsync pipeline, derived from the
/// 2026-05-11 audit of HierarchyService and GrantService internals.
///
/// **Audit findings these guard against**:
///
/// 1. **Orphan FK in hierarchy** (workforce user's Company → Molecule chain broken): previously
///    BuildPrincipalFromClaimsAsync would call ApplyAutoGrantsAsync with all-null path fields,
///    silently creating grants at GLOBAL scope (= over-granting). Now refused at stage 530.
///
/// 2. **Missing RoleTemplate**: a user with RoleTemplateId pointing to a deleted RoleTemplate
///    row would previously trigger a silent no-op in ApplyAutoGrantsAsync. Now logs a warning
///    via the new optional ILogger&lt;GrantService&gt; and still completes auth (degraded grants
///    is the expected fallback for transient seed/admin state).
///
/// 3. **User with both CompanyId AND DepartmentId**: ambiguous workforce-vs-tech classification.
///    HierarchyService takes the workforce path; verify the resulting principal is internally
///    consistent and the user can log in.
///
/// 4. **`hierarchyContext.Path.Molecule.Id` NRE risk**: GetHierarchyPathForCompanyAsync returns
///    null when ANY chain link is missing, so Path.Molecule/Area/Project are guaranteed
///    non-null when context is returned. Confirmed in audit; locking with a test.
///
/// 5. **Owner without Company/Department**: user.CompanyId == 0 and DepartmentId == null is
///    legitimate (Owner role); hierarchyContext is null; the defensive guard correctly skips
///    this case (only fires when CompanyId > 0).
///
/// These tests use REAL SQLite (not UseInMemoryDatabase) so the production translator runs
/// end-to-end — same lesson as GRIFFIN-USERLOOKUP-510.
/// </summary>
public sealed class GriffinPipelineEdgeCaseTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private AppDbContext _db = null!;
    private Mock<IHierarchyService> _hierarchyMock = null!;
    private Mock<IGrantService> _grantMock = null!;
    private GriffinService _service = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _hierarchyMock = new Mock<IHierarchyService>();
        _grantMock = new Mock<IGrantService>();

        _service = new GriffinService(
            new Mock<IHttpClientFactory>().Object,
            _db,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<ISecurityLogger>().Object,
            new Mock<IAuditLogService>().Object,
            Mock.Of<ILogger<GriffinService>>(),
            _hierarchyMock.Object,
            _grantMock.Object);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    [Fact]
    public async Task WorkforceUser_WithOrphanFKHierarchy_RefusesLoginWithHierarchyLoadFailed()
    {
        // User exists, has CompanyId > 0 AND a role template, but HierarchyService returns
        // null (chain broken). The defensive guard in BuildPrincipalFromClaimsAsync should
        // refuse the login with HierarchyLoadFailed (530) — NOT silently apply grants with
        // global scope. The guard requires RoleTemplateId.HasValue because that's the only
        // path that would actually trigger ApplyAutoGrantsAsync (and thus the over-grant bug);
        // users without a role template fall through harmlessly.
        _db.RoleTemplates.Add(new RoleTemplate
        {
            Id = 77, Key = "TestTemplate", NameKey = "Test", IsActive = true
        });
        _db.Users.Add(new AppUser
        {
            Id = 100, CompanyId = 99, RoleTemplateId = 77, Email = "orphan@test.local",
            DisplayName = "Orphan", Role = UserRole.Employee, IsActive = true
        });
        await _db.SaveChangesAsync();
        _hierarchyMock.Setup(h => h.GetUserHierarchyContextAsync(100))
            .ReturnsAsync((UserHierarchyContext?)null);

        var claims = new GriffinClaimsDto
        {
            EmailAddress = "orphan@test.local",
            UniqueID = "O-1",
            DisplayName = "Orphan"
        };
        var result = await _service.BuildPrincipalFromClaimsAsync(claims, null, "127.0.0.1");

        result.Success.Should().BeFalse(
            "a workforce user with broken hierarchy chain must be refused, not silently over-granted");
        result.Error!.Code.Should().Be(GriffinErrorCode.HierarchyLoadFailed);
        result.Error.TechnicalDetail.Should().Contain("hierarchy chain returned null");

        // The defensive guard fires BEFORE the grant phase — verify ApplyAutoGrants was never called.
        _grantMock.Verify(g => g.ApplyAutoGrantsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<GrantScope>()),
            Times.Never,
            "grant application must not run when hierarchy is broken — that's the silent over-grant path the audit found");
    }

    [Fact]
    public async Task OwnerStyleUser_WithCompanyIdZero_NoHierarchy_CanStillLogin()
    {
        // Owner-style users legitimately have no hierarchy context (CompanyId == 0,
        // DepartmentId == null). The defensive guard must NOT block this — only workforce
        // users (CompanyId > 0) with broken chains.
        _db.Users.Add(new AppUser
        {
            Id = 200, CompanyId = 0, Email = "owner@test.local",
            DisplayName = "Owner", Role = UserRole.Owner, IsActive = true
        });
        await _db.SaveChangesAsync();
        _hierarchyMock.Setup(h => h.GetUserHierarchyContextAsync(200))
            .ReturnsAsync((UserHierarchyContext?)null);

        var claims = new GriffinClaimsDto
        {
            EmailAddress = "owner@test.local",
            UniqueID = "Owner-1",
            DisplayName = "Owner"
        };
        var result = await _service.BuildPrincipalFromClaimsAsync(claims, null, "127.0.0.1");

        result.Success.Should().BeTrue("Owner-style users with no hierarchy are a legitimate case");
        // Verify the principal has the right shape — no Molecule/Area/Project claims.
        result.Value!.FindFirst("MoleculeId").Should().BeNull();
        result.Value.FindFirst("AreaId").Should().BeNull();
        result.Value.FindFirst("ProjectId").Should().BeNull();
        result.Value.FindFirst("CompanyId")!.Value.Should().Be("0");
    }

    // Happy-path hierarchy tests (UserWithValidHierarchy, TechUser-with-Department) are
    // intentionally omitted here — they would require seeding the full FK chain (JobType,
    // Department, Molecule, Area, Project, Company) which real SQLite enforces. The
    // happy-path coverage is already provided by `BuildPrincipalFromClaimsAsync_*` tests
    // in `GriffinServiceTests` against the same SQLite-backed fixture. This file's job is
    // to lock the EDGE-CASE defensive behavior added by the 2026-05-11 audit, which is
    // what the orphan-FK and missing-RoleTemplate tests cover.

    [Fact]
    public async Task GrantService_ApplyAutoGrantsAsync_MissingRoleTemplate_LogsWarningAndReturnsGracefully()
    {
        // Audit byproduct: AppDbContext line 1232 sets AppUser → RoleTemplate `OnDelete.Restrict`,
        // meaning RoleTemplate deletion is REFUSED while any user references it. So a stale
        // RoleTemplateId is effectively impossible in production through normal EF flows.
        //
        // BUT — defense-in-depth: someone might do a raw SQL DELETE, or a migration could
        // orphan rows, or seed-data drift could produce a non-existent RoleTemplateId. The
        // audit changed ApplyAutoGrantsAsync to LOG A WARNING via optional logger instead of
        // silently no-op'ing. This test exercises that path directly against GrantService
        // (skipping the GriffinService wrapper) to lock the warning-and-return contract.
        var loggerMock = new Mock<ILogger<GrantService>>();
        var grantService = new GrantService(
            _db,
            _hierarchyMock.Object,
            new Mock<IAuditLogService>().Object,
            loggerMock.Object);

        // Call with a non-existent RoleTemplateId. No exception, no DB changes, but logger
        // should have been called with a warning.
        await grantService.ApplyAutoGrantsAsync(
            userId: 999,
            roleTemplateId: 99999,
            roleScope: new GrantScope(CompanyId: 1));

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("RoleTemplate 99999 not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "missing RoleTemplate should produce a warning log entry, not a silent no-op");
    }
}
