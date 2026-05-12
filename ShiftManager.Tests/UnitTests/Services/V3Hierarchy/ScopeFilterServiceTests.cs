using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Tests.UnitTests.Services.V3Hierarchy;

/// <summary>
/// Tests for A-018: Scope Filter Service
/// Verifies that scope filtering correctly resolves company IDs for different scopes.
/// </summary>
public class ScopeFilterServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly Mock<IHierarchyService> _hierarchyServiceMock;
    private readonly Mock<IUserPreferenceService> _userPreferenceServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<ILogger<ScopeFilterService>> _loggerMock;

    public ScopeFilterServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _grantServiceMock = new Mock<IGrantService>();
        _hierarchyServiceMock = new Mock<IHierarchyService>();
        _userPreferenceServiceMock = new Mock<IUserPreferenceService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<ScopeFilterService>>();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private ScopeFilterService CreateService()
    {
        return new ScopeFilterService(
            _httpContextAccessorMock.Object,
            _db,
            _grantServiceMock.Object,
            _hierarchyServiceMock.Object,
            _userPreferenceServiceMock.Object,
            _loggerMock.Object
        );
    }

    private void SetupHttpContext(int userId, Dictionary<string, string>? queryParams = null, Dictionary<string, string>? cookies = null)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext
        {
            User = principal
        };

        if (queryParams != null)
        {
            var query = new QueryCollection(queryParams.ToDictionary(
                kvp => kvp.Key,
                kvp => new Microsoft.Extensions.Primitives.StringValues(kvp.Value)
            ));
            httpContext.Request.Query = query;
        }

        if (cookies != null)
        {
            var cookiesMock = new Mock<IRequestCookieCollection>();
            foreach (var cookie in cookies)
            {
                cookiesMock.Setup(c => c.TryGetValue(cookie.Key, out It.Ref<string?>.IsAny))
                    .Callback(new TryGetValueCallback((string key, out string? value) =>
                    {
                        value = cookies.GetValueOrDefault(key);
                    }))
                    .Returns((string key, out string? value) =>
                    {
                        value = cookies.GetValueOrDefault(key);
                        return value != null;
                    });
            }
            httpContext.Request.Cookies = cookiesMock.Object;
        }

        _httpContextAccessorMock.Setup(h => h.HttpContext).Returns(httpContext);
    }

    private delegate void TryGetValueCallback(string key, out string? value);

    private async Task<TestEntities> SetupTestEntitiesAsync()
    {
        // Create hierarchy: Project -> Area -> Molecule -> Companies
        var project = new Project { Name = "TestProject", DisplayName = "Test Project" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "TestArea", DisplayName = "Test Area" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        var molecule = new Molecule { AreaId = area.Id, Name = "TestMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        var company1 = new Company { MoleculeId = molecule.Id, Name = "Company1", DisplayName = "Company 1" };
        var company2 = new Company { MoleculeId = molecule.Id, Name = "Company2", DisplayName = "Company 2" };
        var company3 = new Company { MoleculeId = molecule.Id, Name = "Company3", DisplayName = "Company 3" };
        _db.Companies.AddRange(company1, company2, company3);
        await _db.SaveChangesAsync();

        // Create user in company1
        var user = new AppUser
        {
            CompanyId = company1.Id,
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Setup hierarchy service mock
        var hierarchyPath = new HierarchyPath(project, area, molecule, company1, null);
        _hierarchyServiceMock.Setup(h => h.GetUserHierarchyContextAsync(user.Id))
            .ReturnsAsync(new UserHierarchyContext(
                UserId: user.Id,
                Path: hierarchyPath,
                JobType: null,
                IsWorkforce: true,
                IsTech: false
            ));

        return new TestEntities(project, area, molecule, new[] { company1, company2, company3 }, user);
    }

    private record TestEntities(
        Project Project,
        Area Area,
        Molecule Molecule,
        Company[] Companies,
        AppUser User
    );

    #region GetCurrentScope Tests

    [Fact]
    public void GetCurrentScope_WithQueryParam_ReturnsQueryScope()
    {
        // Arrange
        SetupHttpContext(1, queryParams: new Dictionary<string, string> { ["scope"] = "molecule" });
        var service = CreateService();

        // Act
        var (scopeType, scopeId) = service.GetCurrentScope("shifts");

        // Assert
        scopeType.Should().Be("molecule");
        scopeId.Should().BeNull();
    }

    [Fact]
    public void GetCurrentScope_WithoutQueryParam_DefaultsToCompany()
    {
        // Arrange
        SetupHttpContext(1);
        var service = CreateService();

        // Act
        var (scopeType, scopeId) = service.GetCurrentScope("shifts");

        // Assert
        scopeType.Should().Be("company");
        scopeId.Should().BeNull();
    }

    #endregion

    #region ResolveCompanyIdsForScopeAsync Tests

    [Fact]
    public async Task ResolveCompanyIdsForScope_MineScope_ReturnsUserCompany()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        var service = CreateService();

        // Act
        var companyIds = await service.ResolveCompanyIdsForScopeAsync("mine", null);

        // Assert
        companyIds.Should().HaveCount(1);
        companyIds.Should().Contain(entities.Companies[0].Id);
    }

    [Fact]
    public async Task ResolveCompanyIdsForScope_CompanyScope_ReturnsUserCompany()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        var service = CreateService();

        // Act
        var companyIds = await service.ResolveCompanyIdsForScopeAsync("company", null);

        // Assert
        companyIds.Should().HaveCount(1);
        companyIds.Should().Contain(entities.Companies[0].Id);
    }

    [Fact]
    public async Task ResolveCompanyIdsForScope_MoleculeScope_ReturnsAllMoleculeCompanies()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        var service = CreateService();

        // Act
        var companyIds = await service.ResolveCompanyIdsForScopeAsync("molecule", null);

        // Assert
        companyIds.Should().HaveCount(3);
        companyIds.Should().Contain(entities.Companies[0].Id);
        companyIds.Should().Contain(entities.Companies[1].Id);
        companyIds.Should().Contain(entities.Companies[2].Id);
    }

    [Fact]
    public async Task ResolveCompanyIdsForScope_AreaScope_ReturnsAllAreaCompanies()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        var service = CreateService();

        // Act
        var companyIds = await service.ResolveCompanyIdsForScopeAsync("area", null);

        // Assert
        companyIds.Should().HaveCount(3); // All companies in the area's molecule
        companyIds.Should().Contain(entities.Companies[0].Id);
        companyIds.Should().Contain(entities.Companies[1].Id);
        companyIds.Should().Contain(entities.Companies[2].Id);
    }

    [Fact]
    public async Task ResolveCompanyIdsForScope_InvalidScope_FallsBackToUserCompany()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        var service = CreateService();

        // Act
        var companyIds = await service.ResolveCompanyIdsForScopeAsync("invalid", null);

        // Assert
        companyIds.Should().HaveCount(1);
        companyIds.Should().Contain(entities.Companies[0].Id);
    }

    [Fact]
    public async Task ResolveCompanyIdsForScope_NoUser_ReturnsEmptyList()
    {
        // Arrange
        SetupHttpContext(99999); // Non-existent user
        var service = CreateService();

        // Act
        var companyIds = await service.ResolveCompanyIdsForScopeAsync("company", null);

        // Assert
        companyIds.Should().BeEmpty();
    }

    #endregion

    #region ValidateScopeAccessAsync Tests

    [Fact]
    public async Task ValidateScopeAccess_MineScope_AlwaysTrue()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        var service = CreateService();

        // Act
        var hasAccess = await service.ValidateScopeAccessAsync("mine", null, "shifts");

        // Assert
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateScopeAccess_CompanyScope_AlwaysTrue()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        var service = CreateService();

        // Act
        var hasAccess = await service.ValidateScopeAccessAsync("company", null, "shifts");

        // Assert
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateScopeAccess_MoleculeScope_WithGrant_ReturnsTrue()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        _grantServiceMock.Setup(g => g.HasGrantAsync(entities.User.Id, "ViewShiftsMolecule"))
            .ReturnsAsync(true);
        var service = CreateService();

        // Act
        var hasAccess = await service.ValidateScopeAccessAsync("molecule", null, "shifts");

        // Assert
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateScopeAccess_MoleculeScope_WithoutGrant_ReturnsFalse()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();
        SetupHttpContext(entities.User.Id);
        _grantServiceMock.Setup(g => g.HasGrantAsync(entities.User.Id, "ViewShiftsMolecule"))
            .ReturnsAsync(false);
        var service = CreateService();

        // Act
        var hasAccess = await service.ValidateScopeAccessAsync("molecule", null, "shifts");

        // Assert
        hasAccess.Should().BeFalse();
    }

    #endregion

    #region ShouldFilterToCurrentUserOnly Tests

    [Fact]
    public void ShouldFilterToCurrentUserOnly_MineScope_ReturnsTrue()
    {
        // Arrange
        SetupHttpContext(1);
        var service = CreateService();

        // Act
        var shouldFilter = service.ShouldFilterToCurrentUserOnly("mine");

        // Assert
        shouldFilter.Should().BeTrue();
    }

    [Fact]
    public void ShouldFilterToCurrentUserOnly_CompanyScope_ReturnsUserPreference()
    {
        // Arrange
        SetupHttpContext(1);
        _userPreferenceServiceMock.Setup(p => p.GetShowMyItemsOnly()).Returns(false);
        var service = CreateService();

        // Act
        var shouldFilter = service.ShouldFilterToCurrentUserOnly("company");

        // Assert
        shouldFilter.Should().BeFalse();
    }

    [Fact]
    public void ShouldFilterToCurrentUserOnly_MoleculeScope_ReturnsUserPreference()
    {
        // Arrange
        SetupHttpContext(1);
        _userPreferenceServiceMock.Setup(p => p.GetShowMyItemsOnly()).Returns(true);
        var service = CreateService();

        // Act
        var shouldFilter = service.ShouldFilterToCurrentUserOnly("molecule");

        // Assert
        shouldFilter.Should().BeTrue();
    }

    #endregion
}
