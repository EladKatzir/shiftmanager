using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Security.Claims;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Services;

public class DirectorServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly DirectorService _service;

    // Grant keys used in tests (must match DirectorService constants)
    private const string DirectorHubAccessGrant = "DirectorHubAccess";
    private const string ManagerHomeAccessGrant = "ManagerHomeAccess";
    private const string AssignRolesGrant = "AssignRoles";
    private const string AdminAccessGrant = "AdminAccess";

    public DirectorServiceTests()
    {
        // Create InMemory database
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _grantServiceMock = new Mock<IGrantService>();
        _service = new DirectorService(_db, _httpContextAccessorMock.Object, _grantServiceMock.Object);
    }

    private void SetupUser(int userId, UserRole role)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Setup grant service based on role
        SetupGrantsForRole(userId, role);
    }

    private void SetupGrantsForRole(int userId, UserRole role)
    {
        // Reset all grants to false by default
        _grantServiceMock.Setup(g => g.HasGrantAsync(userId, It.IsAny<string>())).ReturnsAsync(false);
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(userId, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(false);
        _grantServiceMock.Setup(g => g.GetAccessibleCompanyIdsForGrantAsync(userId, It.IsAny<string>())).ReturnsAsync(new List<int>());

        switch (role)
        {
            case UserRole.Owner:
                // Owner has all grants for all companies including AdminAccess
                _grantServiceMock.Setup(g => g.HasGrantAsync(userId, AdminAccessGrant)).ReturnsAsync(true);
                _grantServiceMock.Setup(g => g.HasGrantAsync(userId, DirectorHubAccessGrant)).ReturnsAsync(true);
                _grantServiceMock.Setup(g => g.HasGrantAsync(userId, ManagerHomeAccessGrant)).ReturnsAsync(true);
                _grantServiceMock.Setup(g => g.HasGrantAsync(userId, AssignRolesGrant)).ReturnsAsync(true);
                _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(userId, DirectorHubAccessGrant, It.IsAny<int>())).ReturnsAsync(true);
                _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(userId, ManagerHomeAccessGrant, It.IsAny<int>())).ReturnsAsync(true);
                break;

            case UserRole.Director:
                // Director has director grants
                _grantServiceMock.Setup(g => g.HasGrantAsync(userId, DirectorHubAccessGrant)).ReturnsAsync(true);
                _grantServiceMock.Setup(g => g.HasGrantAsync(userId, AssignRolesGrant)).ReturnsAsync(true);
                break;

            case UserRole.Manager:
                // Manager has manager grants
                _grantServiceMock.Setup(g => g.HasGrantAsync(userId, ManagerHomeAccessGrant)).ReturnsAsync(true);
                _grantServiceMock.Setup(g => g.HasGrantAsync(userId, AssignRolesGrant)).ReturnsAsync(true);
                break;

            // Employee has no elevated grants
        }
    }

    [Fact]
    public async Task IsDirectorAsync_ReturnsTrue_ForOwner()
    {
        // Arrange
        SetupUser(1, UserRole.Owner);

        // Act
        var result = await _service.IsDirectorAsync();

        // Assert
        result.Should().BeTrue("Owner has Director permissions");
    }

    [Fact]
    public async Task IsDirectorAsync_ReturnsTrue_ForDirector()
    {
        // Arrange
        SetupUser(1, UserRole.Director);

        // Act
        var result = await _service.IsDirectorAsync();

        // Assert
        result.Should().BeTrue("Director role has Director permissions");
    }

    [Fact]
    public async Task IsDirectorAsync_ReturnsFalse_ForManager()
    {
        // Arrange
        SetupUser(1, UserRole.Manager);

        // Act
        var result = await _service.IsDirectorAsync();

        // Assert
        result.Should().BeFalse("Manager does not have Director permissions");
    }

    [Fact]
    public async Task IsDirectorAsync_ReturnsFalse_ForEmployee()
    {
        // Arrange
        SetupUser(1, UserRole.Employee);

        // Act
        var result = await _service.IsDirectorAsync();

        // Assert
        result.Should().BeFalse("Employee does not have Director permissions");
    }

    [Fact]
    public async Task IsDirectorOfAsync_ReturnsTrue_ForOwnerWithAnyCompany()
    {
        // Arrange
        SetupUser(1, UserRole.Owner);

        // Act - Owner should have access to ANY company, even one that doesn't exist
        var result = await _service.IsDirectorOfAsync(companyId: 999);

        // Assert
        result.Should().BeTrue("Owner has access to all companies via DirectorHubAccess grant");
    }

    [Fact]
    public async Task IsDirectorOfAsync_ReturnsTrue_ForDirectorWithGrant()
    {
        // Arrange
        SetupUser(10, UserRole.Director);
        // Simulate director having DirectorHubAccess grant for company 1
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(10, DirectorHubAccessGrant, 1)).ReturnsAsync(true);

        // Act
        var result = await _service.IsDirectorOfAsync(companyId: 1);

        // Assert
        result.Should().BeTrue("Director has DirectorHubAccess grant for company 1");
    }

    [Fact]
    public async Task IsDirectorOfAsync_ReturnsFalse_ForDirectorWithoutGrant()
    {
        // Arrange
        SetupUser(10, UserRole.Director);
        // Director has grant for company 1 but NOT company 2
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(10, DirectorHubAccessGrant, 1)).ReturnsAsync(true);
        _grantServiceMock.Setup(g => g.HasGrantForCompanyAsync(10, DirectorHubAccessGrant, 2)).ReturnsAsync(false);

        // Act - Try to access company 2
        var result = await _service.IsDirectorOfAsync(companyId: 2);

        // Assert
        result.Should().BeFalse("Director does NOT have DirectorHubAccess grant for company 2");
    }

    [Fact]
    public async Task IsDirectorOfAsync_ReturnsFalse_ForManagerRole()
    {
        // Arrange
        SetupUser(1, UserRole.Manager);

        // Act
        var result = await _service.IsDirectorOfAsync(companyId: 1);

        // Assert
        result.Should().BeFalse("Manager does not have DirectorHubAccess grant by default");
    }

    [Fact]
    public async Task CanAssignRoleAsync_Owner_CanAssignAnyRole()
    {
        // Arrange
        SetupUser(1, UserRole.Owner);

        // Act & Assert
        (await _service.CanAssignRoleAsync(UserRole.Owner)).Should().BeTrue();
        (await _service.CanAssignRoleAsync(UserRole.Director)).Should().BeTrue();
        (await _service.CanAssignRoleAsync(UserRole.Manager)).Should().BeTrue();
        (await _service.CanAssignRoleAsync(UserRole.Employee)).Should().BeTrue();
    }

    [Fact]
    public async Task CanAssignRoleAsync_Director_CannotAssignOwner()
    {
        // Arrange
        SetupUser(1, UserRole.Director);

        // Act
        var canAssignOwner = await _service.CanAssignRoleAsync(UserRole.Owner);

        // Assert
        canAssignOwner.Should().BeFalse("Director CANNOT assign Owner role - security critical!");
    }

    [Fact]
    public async Task CanAssignRoleAsync_Director_CanAssignDirectorManagerEmployee()
    {
        // Arrange
        SetupUser(1, UserRole.Director);

        // Act & Assert
        (await _service.CanAssignRoleAsync(UserRole.Director)).Should().BeTrue();
        (await _service.CanAssignRoleAsync(UserRole.Manager)).Should().BeTrue();
        (await _service.CanAssignRoleAsync(UserRole.Employee)).Should().BeTrue();
        (await _service.CanAssignRoleAsync(UserRole.Trainee)).Should().BeTrue();
    }

    [Fact]
    public async Task CanAssignRoleAsync_Manager_CanOnlyAssignEmployee()
    {
        // Arrange
        SetupUser(1, UserRole.Manager);

        // Act & Assert
        (await _service.CanAssignRoleAsync(UserRole.Owner)).Should().BeFalse();
        (await _service.CanAssignRoleAsync(UserRole.Director)).Should().BeFalse();
        (await _service.CanAssignRoleAsync(UserRole.Manager)).Should().BeFalse();
        (await _service.CanAssignRoleAsync(UserRole.Employee)).Should().BeTrue("Manager can assign Employee");
        (await _service.CanAssignRoleAsync(UserRole.Trainee)).Should().BeTrue("Manager can assign Trainee");
    }

    [Fact]
    public async Task CanAssignRoleAsync_Employee_CannotAssignAnyRole()
    {
        // Arrange
        SetupUser(1, UserRole.Employee);

        // Act & Assert
        (await _service.CanAssignRoleAsync(UserRole.Owner)).Should().BeFalse();
        (await _service.CanAssignRoleAsync(UserRole.Director)).Should().BeFalse();
        (await _service.CanAssignRoleAsync(UserRole.Manager)).Should().BeFalse();
        (await _service.CanAssignRoleAsync(UserRole.Employee)).Should().BeFalse("Employee cannot assign any role");
        (await _service.CanAssignRoleAsync(UserRole.Trainee)).Should().BeFalse("Employee cannot assign Trainee");
    }

    [Fact]
    public async Task GetDirectorCompanyIdsAsync_ReturnsAccessibleCompanies()
    {
        // Arrange
        SetupUser(10, UserRole.Director);
        // Setup grant service to return accessible companies
        _grantServiceMock.Setup(g => g.GetAccessibleCompanyIdsForGrantAsync(10, DirectorHubAccessGrant))
            .ReturnsAsync(new List<int> { 1, 2 });

        // Act
        var companyIds = await _service.GetDirectorCompanyIdsAsync();

        // Assert
        companyIds.Should().HaveCount(2);
        companyIds.Should().Contain(new[] { 1, 2 });
    }

    [Fact]
    public async Task GetDirectorCompanyIdsAsync_ReturnsEmptyWhenNoGrants()
    {
        // Arrange
        SetupUser(10, UserRole.Director);
        // Grant service returns empty list (no DirectorHubAccess grants)
        _grantServiceMock.Setup(g => g.GetAccessibleCompanyIdsForGrantAsync(10, DirectorHubAccessGrant))
            .ReturnsAsync(new List<int>());

        // Act
        var companyIds = await _service.GetDirectorCompanyIdsAsync();

        // Assert
        companyIds.Should().BeEmpty("No DirectorHubAccess grants means no accessible companies");
    }

    public void Dispose()
    {
        _db?.Dispose();
        _sqliteConnection.Dispose();
    }
}
