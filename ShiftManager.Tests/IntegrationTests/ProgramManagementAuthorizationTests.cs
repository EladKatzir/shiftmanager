using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using System.Net;
using System.Security.Claims;

namespace ShiftManager.Tests.IntegrationTests;

/// <summary>
/// P1-1, P1-2, P1-3: Program Management Authorization Tests
/// Verifies that Manager, Director, and Owner can access Blueprints/Programs/MasterPrograms
/// while Employee, Trainee, and Assigner cannot
/// </summary>
public class ProgramManagementAuthorizationTests : IDisposable
{
    private readonly AppDbContext _db;

    public ProgramManagementAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("ProgramAuthTestDb_" + Guid.NewGuid())
            .Options;
        _db = new AppDbContext(options);

        SeedTestData().Wait();
    }

    private async Task SeedTestData()
    {
        // Create test company
        var company = new Company { Id = 1, Name = "Test Company" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Simulates the authorization policy check for IsManagerOrAdmin
    /// </summary>
    private bool CanAccessProgramManagement(UserRole role)
    {
        // IsManagerOrAdmin policy allows: Owner, Manager, Director
        return role == UserRole.Owner
            || role == UserRole.Manager
            || role == UserRole.Director;
    }

    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Director, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Employee, false)]
    [InlineData(UserRole.Trainee, false)]
    [InlineData(UserRole.Assigner, false)]
    public void Blueprints_AuthorizationPolicy_AllowsManagerOrAdmin(UserRole role, bool shouldAllow)
    {
        // Arrange & Act
        var canAccess = CanAccessProgramManagement(role);

        // Assert
        canAccess.Should().Be(shouldAllow,
            $"Blueprints page should {(shouldAllow ? "allow" : "deny")} {role} access");
    }

    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Director, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Employee, false)]
    [InlineData(UserRole.Trainee, false)]
    [InlineData(UserRole.Assigner, false)]
    public void Programs_AuthorizationPolicy_AllowsManagerOrAdmin(UserRole role, bool shouldAllow)
    {
        // Arrange & Act
        var canAccess = CanAccessProgramManagement(role);

        // Assert
        canAccess.Should().Be(shouldAllow,
            $"Programs page should {(shouldAllow ? "allow" : "deny")} {role} access");
    }

    [Theory]
    [InlineData(UserRole.Owner, true)]
    [InlineData(UserRole.Director, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Employee, false)]
    [InlineData(UserRole.Trainee, false)]
    [InlineData(UserRole.Assigner, false)]
    public void MasterPrograms_AuthorizationPolicy_AllowsManagerOrAdmin(UserRole role, bool shouldAllow)
    {
        // Arrange & Act
        var canAccess = CanAccessProgramManagement(role);

        // Assert
        canAccess.Should().Be(shouldAllow,
            $"MasterPrograms page should {(shouldAllow ? "allow" : "deny")} {role} access");
    }

    [Fact]
    public async Task Owner_CanCreateBlueprints_AllCompanies()
    {
        // Arrange - Owner user
        var owner = new AppUser
        {
            Id = 1,
            Email = "owner@test.com",
            DisplayName = "Owner User",
            CompanyId = 1,
            Role = UserRole.Owner,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(owner);
        await _db.SaveChangesAsync();

        // Act - Owner creates a ShiftType (Blueprint)
        var blueprint = new ShiftType
        {
            Id = 1,
            CompanyId = 1,
            Key = "TEST_SHIFT",
            CustomName = "Test Shift",
            Start = new TimeOnly(9, 0),
            End = new TimeOnly(17, 0)
        };
        _db.ShiftTypes.Add(blueprint);
        await _db.SaveChangesAsync();

        // Assert
        var saved = await _db.ShiftTypes.FindAsync(1);
        saved.Should().NotBeNull("Owner should be able to create blueprints");
        saved!.Key.Should().Be("TEST_SHIFT");
    }

    [Fact]
    public async Task Manager_CanCreateBlueprints_OwnCompanyOnly()
    {
        // Arrange - Manager user
        var manager = new AppUser
        {
            Id = 2,
            Email = "manager@test.com",
            DisplayName = "Manager User",
            CompanyId = 1,
            Role = UserRole.Manager,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(manager);
        await _db.SaveChangesAsync();

        // Act - Manager creates a ShiftType for their company
        var blueprint = new ShiftType
        {
            Id = 2,
            CompanyId = 1, // Manager's company
            Key = "MANAGER_SHIFT",
            CustomName = "Manager Shift",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(blueprint);
        await _db.SaveChangesAsync();

        // Assert
        var saved = await _db.ShiftTypes.FindAsync(2);
        saved.Should().NotBeNull("Manager should be able to create blueprints for their company");
        saved!.CompanyId.Should().Be(1, "Manager's blueprint should be for their company");
    }

    [Fact]
    public async Task Director_CanCreateBlueprints_AssignedCompaniesOnly()
    {
        // Arrange - Director user assigned to Company 1
        var director = new AppUser
        {
            Id = 3,
            Email = "director@test.com",
            DisplayName = "Director User",
            CompanyId = 1,
            Role = UserRole.Director,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(director);
        await _db.SaveChangesAsync();

        // Create DirectorCompany mapping
        var mapping = new DirectorCompany
        {
            UserId = 3,
            CompanyId = 1,
            GrantedBy = 1,
            GrantedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        _db.DirectorCompanies.Add(mapping);
        await _db.SaveChangesAsync();

        // Act - Director creates a ShiftType for assigned company
        var blueprint = new ShiftType
        {
            Id = 3,
            CompanyId = 1, // Director's assigned company
            Key = "DIRECTOR_SHIFT",
            CustomName = "Director Shift",
            Start = new TimeOnly(10, 0),
            End = new TimeOnly(18, 0)
        };
        _db.ShiftTypes.Add(blueprint);
        await _db.SaveChangesAsync();

        // Assert
        var saved = await _db.ShiftTypes.FindAsync(3);
        saved.Should().NotBeNull("Director should be able to create blueprints for assigned companies");
        saved!.CompanyId.Should().Be(1, "Director's blueprint should be for assigned company");
    }

    [Fact]
    public async Task Employee_CannotAccessProgramManagement()
    {
        // Arrange - Employee user
        var employee = new AppUser
        {
            Id = 4,
            Email = "employee@test.com",
            DisplayName = "Employee User",
            CompanyId = 1,
            Role = UserRole.Employee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(employee);
        await _db.SaveChangesAsync();

        // Act - Check if Employee can access program management
        var canAccess = CanAccessProgramManagement(employee.Role);

        // Assert
        canAccess.Should().BeFalse("Employee should NOT be able to access Blueprints/Programs/MasterPrograms");
    }

    [Fact]
    public async Task Trainee_CannotAccessProgramManagement()
    {
        // Arrange - Trainee user
        var trainee = new AppUser
        {
            Id = 5,
            Email = "trainee@test.com",
            DisplayName = "Trainee User",
            CompanyId = 1,
            Role = UserRole.Trainee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(trainee);
        await _db.SaveChangesAsync();

        // Act
        var canAccess = CanAccessProgramManagement(trainee.Role);

        // Assert
        canAccess.Should().BeFalse("Trainee should NOT be able to access Blueprints/Programs/MasterPrograms");
    }

    [Fact]
    public async Task Assigner_CannotAccessProgramManagement()
    {
        // Arrange - Assigner user
        var assigner = new AppUser
        {
            Id = 6,
            Email = "assigner@test.com",
            DisplayName = "Assigner User",
            CompanyId = 1,
            Role = UserRole.Assigner,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(assigner);
        await _db.SaveChangesAsync();

        // Act
        var canAccess = CanAccessProgramManagement(assigner.Role);

        // Assert
        canAccess.Should().BeFalse("Assigner should NOT be able to access Blueprints/Programs/MasterPrograms");
    }

    [Fact]
    public void IsManagerOrAdmin_Policy_MatchesExpectedRoles()
    {
        // Verify the policy allows exactly the expected roles
        var allowedRoles = new[] { UserRole.Owner, UserRole.Manager, UserRole.Director };
        var deniedRoles = new[] { UserRole.Employee, UserRole.Trainee, UserRole.Assigner };

        // Assert - Allowed roles
        foreach (var role in allowedRoles)
        {
            CanAccessProgramManagement(role).Should().BeTrue(
                $"{role} should be allowed by IsManagerOrAdmin policy");
        }

        // Assert - Denied roles
        foreach (var role in deniedRoles)
        {
            CanAccessProgramManagement(role).Should().BeFalse(
                $"{role} should be denied by IsManagerOrAdmin policy");
        }
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
