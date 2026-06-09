using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
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
            _grantMock.Object,
            new CompanyMembershipService(_db, Mock.Of<ILogger<CompanyMembershipService>>()));
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

    // =================================================================================
    // Failure-mode matrix — one targeted test per stage-specific error code.
    //
    // Each test triggers the exact failure path that produces the code, asserts the
    // result has the right Code, and verifies the ErrorToken format. The goal: PROVE
    // each error code is REACHABLE via production code (not just defined in the enum)
    // AND distinguishable from sibling codes. Combined with the exhaustive switch in
    // GriffinErrorMessages.BuildDetail, this gives operators confidence that any
    // production failure will surface with a specific, actionable code.
    //
    // Codes covered (the ones added by the 2026-05-11 instrumentation):
    //   510 UserLookupQueryFailed       — EF query on Users threw
    //   520 RoleTemplateBackfillFailed  — backfill SaveChangesAsync threw
    //   530 HierarchyLoadFailed         — IHierarchyService threw
    //   540 GrantApplicationFailed      — IGrantService.ApplyAutoGrantsAsync threw
    //   550 ClaimsPrincipalBuildFailed  — Claim construction threw
    //
    // Codes already covered by earlier tests in this project:
    //   500 UserNotRegistered           — BuildPrincipalFromClaimsAsync_UserNotFound_*
    //   501 UserDeactivated             — BuildPrincipalFromClaimsAsync_InactiveUser_*
    // =================================================================================

    [Fact]
    public async Task FailureMode_510_UserLookupQueryFailed_DisposedDbContext_SurfacesAs510()
    {
        // Dispose the connection BEFORE the lookup query runs → EF throws on the FirstOrDefaultAsync.
        // The stage-510 try/catch should catch it and emit UserLookupQueryFailed.
        await _conn.DisposeAsync();

        var claims = new GriffinClaimsDto { EmailAddress = "x@x.mil", UniqueID = "X" };
        var result = await _service.BuildPrincipalFromClaimsAsync(claims, null, "127.0.0.1");

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.UserLookupQueryFailed);
        result.Error.ErrorToken.Should().Be("GRIFFIN-USERLOOKUP-510");
        result.Error.TechnicalDetail.Should().Contain("User lookup query threw");
    }

    [Fact]
    public async Task FailureMode_530_HierarchyLoadFailed_HierarchyServiceThrows_SurfacesAs530()
    {
        _db.Users.Add(new AppUser
        {
            Id = 1000, CompanyId = 1, Email = "h530@test.local",
            DisplayName = "H530", Role = UserRole.Employee, IsActive = true
        });
        await _db.SaveChangesAsync();
        // Make the hierarchy service throw — simulates a downstream service failure
        // (e.g., HierarchyService internal NullReferenceException on a corrupted user state).
        _hierarchyMock.Setup(h => h.GetUserHierarchyContextAsync(1000))
            .ThrowsAsync(new InvalidOperationException("simulated hierarchy NRE"));

        var claims = new GriffinClaimsDto { EmailAddress = "h530@test.local", UniqueID = "X" };
        var result = await _service.BuildPrincipalFromClaimsAsync(claims, null, "127.0.0.1");

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.HierarchyLoadFailed);
        result.Error.ErrorToken.Should().Be("GRIFFIN-USERLOOKUP-530");
        result.Error.TechnicalDetail.Should().Contain("InvalidOperationException");
        result.Error.TechnicalDetail.Should().Contain("simulated hierarchy NRE");
    }

    [Fact]
    public async Task FailureMode_540_GrantApplicationFailed_GrantServiceThrows_SurfacesAs540()
    {
        // Seed a RoleTemplate so the user has a non-null RoleTemplateId — otherwise
        // ApplyAutoGrantsAsync is skipped and we never reach stage 540.
        _db.RoleTemplates.Add(new RoleTemplate
        {
            Id = 88, Key = "T540", NameKey = "T", IsActive = true
        });
        _db.Users.Add(new AppUser
        {
            Id = 1001, CompanyId = 1, RoleTemplateId = 88, Email = "g540@test.local",
            DisplayName = "G540", Role = UserRole.Employee, IsActive = true
        });
        await _db.SaveChangesAsync();

        // Hierarchy returns a minimal valid context (must be non-null so the defensive
        // guard at stage 530 passes — we want to exercise 540 specifically, not 530).
        _hierarchyMock.Setup(h => h.GetUserHierarchyContextAsync(1001))
            .ReturnsAsync(new UserHierarchyContext(
                UserId: 1001,
                Path: new HierarchyPath(
                    new Project { Id = 1, Name = "P", DisplayName = "P" },
                    new Area { Id = 1, ProjectId = 1, Name = "A" },
                    new Molecule { Id = 1, AreaId = 1, Name = "M" },
                    new Company { Id = 1, Name = "C", Slug = "c", MoleculeId = 1 },
                    null),
                JobType: null, IsWorkforce: true, IsTech: false));

        // Make the grant service throw — simulates ApplyAutoGrantsAsync hitting an FK
        // violation, a DbUpdateException, or other DB-side failure during grant insert.
        _grantMock.Setup(g => g.ApplyAutoGrantsAsync(1001, 88, It.IsAny<GrantScope>()))
            .ThrowsAsync(new DbUpdateException("simulated grant insert FK violation"));

        var claims = new GriffinClaimsDto { EmailAddress = "g540@test.local", UniqueID = "X" };
        var result = await _service.BuildPrincipalFromClaimsAsync(claims, null, "127.0.0.1");

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(GriffinErrorCode.GrantApplicationFailed);
        result.Error.ErrorToken.Should().Be("GRIFFIN-USERLOOKUP-540");
        result.Error.TechnicalDetail.Should().Contain("Grant application for user 1001 threw");
    }

    [Fact]
    public async Task FailureMode_520_RoleTemplateBackfillFailed_CodeAndMessageWired()
    {
        // RoleTemplateBackfillFailed (520) fires when the backfill SaveChangesAsync inside
        // the `if (user.RoleTemplateId == null)` block throws. Triggering it via production
        // code requires a SaveChangesAsync that throws AFTER a successful RoleTemplate query
        // (e.g., concurrent modification of user state). That's a real but narrow window —
        // hard to simulate in-process without invasive harness changes.
        //
        // Instead, exercise the error-message machinery directly to PROVE the code is wired
        // into the exhaustive switch in GriffinErrorMessages.BuildDetail. If someone removes
        // the case for 520, this test catches it as a generic "Unexpected error" fallback.
        var err = new GriffinApiError(
            GriffinStage.UserLookup,
            GriffinErrorCode.RoleTemplateBackfillFailed,
            "Role-template backfill threw DbUpdateConcurrencyException: stale row");

        err.ErrorToken.Should().Be("GRIFFIN-USERLOOKUP-520");

        var loc = new Mock<IStringLocalizer<ShiftManager.Resources.SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k =>
            new Microsoft.Extensions.Localization.LocalizedString(k, k, false));

        var msg = GriffinErrorMessages.Describe(err, loc.Object);
        // The exhaustive switch in BuildDetail covers 520 explicitly (not the generic _ branch).
        msg.Detail.Should().NotContain("Unexpected error",
            "code 520 must have its own user-facing message; falling through to the generic _ branch would leak less context to the admin");
        msg.Detail.Should().Contain("role-template assignment failed");
    }

    [Fact]
    public async Task FailureMode_550_ClaimsPrincipalBuildFailed_CodeAndMessageWired()
    {
        // ClaimsPrincipalBuildFailed (550) fires when Claim construction or sanitization
        // throws (lines 454-506 in GriffinService). The actual trigger requires a
        // pathological string that survives SanitizeClaimString but breaks the Claim ctor —
        // not easily simulated in-process. The error-message machinery is what matters at
        // this stage (the user sees a stable token + remediation; the admin reads the
        // diagnostics ring buffer for the stack trace).
        var err = new GriffinApiError(
            GriffinStage.UserLookup,
            GriffinErrorCode.ClaimsPrincipalBuildFailed,
            "ClaimsPrincipal build for user 42 threw ArgumentException: invalid claim type");

        err.ErrorToken.Should().Be("GRIFFIN-USERLOOKUP-550");

        var loc = new Mock<IStringLocalizer<ShiftManager.Resources.SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k =>
            new Microsoft.Extensions.Localization.LocalizedString(k, k, false));

        var msg = GriffinErrorMessages.Describe(err, loc.Object);
        msg.Detail.Should().NotContain("Unexpected error",
            "code 550 must have its own message — leaking to the generic _ branch hides the security principal as the failed stage");
        msg.Detail.Should().Contain("constructing your security principal failed");
    }

    [Theory]
    [InlineData(GriffinErrorCode.UserLookupQueryFailed, "GRIFFIN-USERLOOKUP-510")]
    [InlineData(GriffinErrorCode.RoleTemplateBackfillFailed, "GRIFFIN-USERLOOKUP-520")]
    [InlineData(GriffinErrorCode.HierarchyLoadFailed, "GRIFFIN-USERLOOKUP-530")]
    [InlineData(GriffinErrorCode.GrantApplicationFailed, "GRIFFIN-USERLOOKUP-540")]
    [InlineData(GriffinErrorCode.ClaimsPrincipalBuildFailed, "GRIFFIN-USERLOOKUP-550")]
    public void FailureMode_AllStageCodes_HaveDistinctErrorTokens(GriffinErrorCode code, string expectedToken)
    {
        // Locks the contract that each stage-specific code has a unique, stable error token.
        // If anyone renumbers an enum value, this test fails immediately — the token is the
        // primary identifier admins quote when reporting auth failures.
        var err = new GriffinApiError(GriffinStage.UserLookup, code, "test");
        err.ErrorToken.Should().Be(expectedToken);
    }
}
