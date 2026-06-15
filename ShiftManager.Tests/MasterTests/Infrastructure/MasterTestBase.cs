using ShiftManager.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.MasterTests.Infrastructure;

/// <summary>
/// Base class for all MasterTests. Provides access to the shared fixture
/// and helper methods for creating services with mocked external dependencies.
/// </summary>
[Collection("MasterTests")]
public abstract class MasterTestBase : IAsyncLifetime
{
    protected readonly MasterTestFixture Fixture;
    protected AppDbContext Db => Fixture.Db;

    private readonly List<object> _trackedEntities = new();

    protected MasterTestBase(MasterTestFixture fixture)
    {
        Fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Clean up any entities added during tests to avoid cross-test pollution
        foreach (var entity in _trackedEntities)
        {
            var entry = Db.Entry(entity);
            if (entry.State != Microsoft.EntityFrameworkCore.EntityState.Detached)
            {
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Deleted;
            }
        }
        if (_trackedEntities.Count > 0)
        {
            await Db.SaveChangesAsync();
            _trackedEntities.Clear();
        }
    }

    /// <summary>
    /// Track an entity for cleanup after the test completes.
    /// Call this after adding entities during write tests.
    /// </summary>
    protected T TrackEntity<T>(T entity) where T : class
    {
        _trackedEntities.Add(entity);
        return entity;
    }

    /// <summary>
    /// Track multiple entities for cleanup.
    /// </summary>
    protected void TrackEntities(params object[] entities)
    {
        _trackedEntities.AddRange(entities);
    }

    // ================================================================
    // SERVICE FACTORY METHODS
    // ================================================================

    /// <summary>
    /// Creates a ShiftAssignmentService with mocked external dependencies.
    /// Uses RestHours=11, WeeklyCap=48 defaults matching existing tests.
    /// </summary>
    protected ShiftAssignmentService CreateShiftAssignmentService(
        int restHours = 11, int weeklyCap = 48)
    {
        var localizer = Mock.Of<IStringLocalizer<SharedResources>>();
        var logger = Mock.Of<ILogger<ShiftAssignmentService>>();

        var hierarchySettingsMock = new Mock<IHierarchySettingsService>();
        hierarchySettingsMock
            .Setup(x => x.GetEffectiveSettingsAsync(It.IsAny<int>()))
            .ReturnsAsync(new EffectiveSettings(
                RestHours: restHours,
                WeeklyCap: weeklyCap,
                RestHoursSource: "Area",
                WeeklyCapSource: "Area"));

        var auditLogService = Mock.Of<IAuditLogService>();

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ApiKeyHmacSecret"]).Returns("test-hmac-secret-for-master-tests");

        var configCacheMock = Mock.Of<IAppConfigCacheService>();

        return new ShiftAssignmentService(
            Db, localizer, logger, hierarchySettingsMock.Object,
            auditLogService, configMock.Object, configCacheMock,
            BusyServiceMockFactory.Real(Db, configMock.Object, restHours, weeklyCap));
    }

    /// <summary>
    /// Creates a GrantService with mocked IHierarchyService.
    /// The hierarchy mock resolves UserHierarchyContext from the fixture's seeded data.
    /// </summary>
    protected GrantService CreateGrantService(UserHierarchyContext? defaultContext = null)
    {
        var hierarchyMock = new Mock<IHierarchyService>();

        if (defaultContext != null)
        {
            hierarchyMock
                .Setup(h => h.GetUserHierarchyContextAsync(It.IsAny<int>()))
                .ReturnsAsync(defaultContext);
        }
        else
        {
            // Return null by default; tests can configure specific contexts
            hierarchyMock
                .Setup(h => h.GetUserHierarchyContextAsync(It.IsAny<int>()))
                .ReturnsAsync((UserHierarchyContext?)null);
        }

        var auditLogService = Mock.Of<IAuditLogService>();
        return new GrantService(Db, hierarchyMock.Object, auditLogService);
    }

    /// <summary>
    /// Creates a GrantService with a hierarchy mock that resolves contexts per-user
    /// based on the fixture's seeded hierarchy.
    /// </summary>
    protected GrantService CreateGrantServiceWithHierarchy()
    {
        var hierarchyMock = new Mock<IHierarchyService>();
        var project = Fixture.ProjectByName["Shifty"];
        var area = Fixture.AreaByName["190"];

        hierarchyMock
            .Setup(h => h.GetUserHierarchyContextAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) =>
            {
                var user = Fixture.UserByEmail.Values.FirstOrDefault(u => u.Id == userId);
                if (user == null) return null;

                var company = Fixture.CompanyByName.Values.FirstOrDefault(c => c.Id == user.CompanyId);
                if (company == null) return null;

                var molecule = Fixture.MoleculeByName.Values.FirstOrDefault(m => m.Id == company.MoleculeId);
                if (molecule == null) return null;

                var jobType = user.JobTypeId.HasValue
                    ? Fixture.JobTypeByName.Values.FirstOrDefault(j => j.Id == user.JobTypeId.Value)
                    : null;

                return new UserHierarchyContext(
                    userId,
                    new HierarchyPath(project, area, molecule, company, null),
                    jobType,
                    IsWorkforce: molecule.Type == Models.Support.MoleculeType.Workforce,
                    IsTech: molecule.Type == Models.Support.MoleculeType.Tech);
            });

        var auditLogService = Mock.Of<IAuditLogService>();
        return new GrantService(Db, hierarchyMock.Object, auditLogService);
    }

    /// <summary>
    /// Creates a ChoreService with mocked external dependencies.
    /// </summary>
    protected ChoreService CreateChoreService(int currentUserId, int tenantCompanyId)
    {
        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(tenantCompanyId);

        var httpContextMock = CreateHttpContextAccessor(currentUserId, tenantCompanyId);

        var directorService = Mock.Of<IDirectorService>();
        var grantService = Mock.Of<IGrantService>();
        var logger = Mock.Of<ILogger<ChoreService>>();
        var companyCacheService = Mock.Of<ICompanyCacheService>();

        return new ChoreService(
            Db, tenantMock.Object, httpContextMock,
            directorService, grantService, logger, companyCacheService,
            BusyServiceMockFactory.Real(Db), new EligibilityEvaluator());
    }

    /// <summary>
    /// Creates an OnDutyService with mocked external dependencies.
    /// </summary>
    protected OnDutyService CreateOnDutyService(int currentUserId, int tenantCompanyId)
    {
        var httpContextMock = CreateHttpContextAccessor(currentUserId, tenantCompanyId);
        var directorService = Mock.Of<IDirectorService>();
        var grantService = Mock.Of<IGrantService>();
        var logger = Mock.Of<ILogger<OnDutyService>>();
        var featureFlagService = Mock.Of<IFeatureFlagService>();

        return new OnDutyService(
            Db, httpContextMock, directorService,
            grantService, logger, featureFlagService,
            BusyServiceMockFactory.Real(Db));
    }

    /// <summary>
    /// Creates a RoleService backed by a real GrantService with hierarchy resolution.
    /// </summary>
    protected RoleService CreateRoleService()
    {
        var grantService = CreateGrantServiceWithHierarchy();
        return new RoleService(
            Db,
            grantService,
            Mock.Of<ILogger<RoleService>>(),
            new Microsoft.Extensions.Localization.StringLocalizer<ShiftManager.Resources.SharedResources>(
                new Microsoft.Extensions.Localization.ResourceManagerStringLocalizerFactory(
                    Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Localization.LocalizationOptions { ResourcesPath = "Resources" }),
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)));
    }

    /// <summary>
    /// Creates a ConcurrencyService with a mocked logger.
    /// </summary>
    protected ConcurrencyService CreateConcurrencyService()
    {
        return new ConcurrencyService(Mock.Of<ILogger<ConcurrencyService>>());
    }

    /// <summary>
    /// Creates a ValidationService (stateless — no dependencies).
    /// </summary>
    protected ValidationService CreateValidationService()
    {
        return new ValidationService();
    }

    // ================================================================
    // HELPER METHODS
    // ================================================================

    /// <summary>
    /// Builds a mock IHttpContextAccessor with UserId and CompanyId claims.
    /// </summary>
    protected IHttpContextAccessor CreateHttpContextAccessor(int userId, int companyId)
    {
        var claims = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
            new System.Security.Claims.Claim("CompanyId", companyId.ToString())
        }, "TestAuth");

        var claimsPrincipal = new System.Security.Claims.ClaimsPrincipal(claims);
        var httpContext = new DefaultHttpContext { User = claimsPrincipal };

        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(a => a.HttpContext).Returns(httpContext);
        return mock.Object;
    }

    /// <summary>
    /// Looks up a test user by template key and company name.
    /// </summary>
    protected AppUser GetTestUser(string companyName, string templateKey)
    {
        var email = $"master.{companyName.ToLower()}.{templateKey.ToLower()}@test.com";
        return Fixture.UserByEmail[email];
    }

    /// <summary>
    /// Gets the UserHierarchyContext for a test user based on fixture data.
    /// </summary>
    protected UserHierarchyContext GetHierarchyContext(AppUser user)
    {
        var company = Fixture.CompanyByName.Values.First(c => c.Id == user.CompanyId);
        var molecule = Fixture.MoleculeByName.Values.First(m => m.Id == company.MoleculeId);
        var area = Fixture.AreaByName["190"];
        var project = Fixture.ProjectByName["Shifty"];
        var jobType = user.JobTypeId.HasValue
            ? Fixture.JobTypeByName.Values.FirstOrDefault(j => j.Id == user.JobTypeId.Value)
            : null;

        return new UserHierarchyContext(
            user.Id,
            new HierarchyPath(project, area, molecule, company, null),
            jobType,
            IsWorkforce: molecule.Type == Models.Support.MoleculeType.Workforce,
            IsTech: molecule.Type == Models.Support.MoleculeType.Tech);
    }
}
