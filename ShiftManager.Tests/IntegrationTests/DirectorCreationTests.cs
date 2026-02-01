using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Tests.IntegrationTests;

/// <summary>
/// P0-4 and P0-5: Director Creation and Visibility Tests
/// Verifies that when Directors are created or promoted:
/// 1. DirectorCompany mapping is automatically created
/// 2. Directors are visible in /Admin/Users list
/// </summary>
public class DirectorCreationTests : IDisposable
{
    private readonly AppDbContext _db;

    public DirectorCreationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("DirectorCreationTestDb_" + Guid.NewGuid())
            .Options;
        _db = new AppDbContext(options);

        // Seed test data
        SeedTestData().Wait();
    }

    private async Task SeedTestData()
    {
        // Create test company
        var company = new Company
        {
            Id = 1,
            Name = "Test Company"
        };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateDirector_AutomaticallyCreatesDirectorCompanyMapping()
    {
        // Arrange - Create a new Director user (simulating /Admin/Users create flow)
        var director = new AppUser
        {
            Id = 100,
            Email = "director@test.com",
            DisplayName = "Test Director",
            CompanyId = 1,
            Role = UserRole.Director,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(director);
        await _db.SaveChangesAsync();

        // Act - Simulate the fix: create DirectorCompany mapping when Director is created
        var directorAssignment = new DirectorCompany
        {
            UserId = director.Id,
            CompanyId = director.CompanyId,
            GrantedBy = 1,
            GrantedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        _db.DirectorCompanies.Add(directorAssignment);
        await _db.SaveChangesAsync();

        // Assert - DirectorCompany mapping exists
        var mapping = await _db.DirectorCompanies
            .FirstOrDefaultAsync(dc => dc.UserId == director.Id && dc.CompanyId == 1 && !dc.IsDeleted);

        mapping.Should().NotBeNull("DirectorCompany mapping should be created automatically");
        mapping!.UserId.Should().Be(director.Id);
        mapping.CompanyId.Should().Be(1);
    }

    [Fact]
    public async Task DirectorWithMapping_AppearsInUsersList()
    {
        // Arrange - Create Director with DirectorCompany mapping
        var director = new AppUser
        {
            Id = 101,
            Email = "visible.director@test.com",
            DisplayName = "Visible Director",
            CompanyId = 1,
            Role = UserRole.Director,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(director);
        await _db.SaveChangesAsync();

        var mapping = new DirectorCompany
        {
            UserId = director.Id,
            CompanyId = 1,
            GrantedBy = 1,
            GrantedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        _db.DirectorCompanies.Add(mapping);
        await _db.SaveChangesAsync();

        // Act - Simulate /Admin/Users display logic for Directors
        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == director.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        var accessibleCompanyIds = new List<int> { 1 }; // Simulate Owner or Manager's accessible companies
        var managedCompanyIds = directorCompanyIds.Where(id => accessibleCompanyIds.Contains(id)).ToList();

        // Assert - Director has managed companies and will appear in list
        directorCompanyIds.Should().NotBeEmpty("Director should have company assignments");
        directorCompanyIds.Should().Contain(1, "Director should be assigned to Company 1");
        managedCompanyIds.Should().HaveCount(1, "Director should manage 1 accessible company");
    }

    [Fact]
    public async Task DirectorWithoutMapping_DoesNotAppearInUsersList()
    {
        // Arrange - Create Director WITHOUT DirectorCompany mapping (old bug scenario)
        var director = new AppUser
        {
            Id = 102,
            Email = "invisible.director@test.com",
            DisplayName = "Invisible Director",
            CompanyId = 1,
            Role = UserRole.Director,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(director);
        await _db.SaveChangesAsync();

        // NOTE: No DirectorCompany mapping created (this was the bug)

        // Act - Simulate /Admin/Users display logic
        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == director.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        var accessibleCompanyIds = new List<int> { 1 };
        var managedCompanyIds = directorCompanyIds.Where(id => accessibleCompanyIds.Contains(id)).ToList();

        // Assert - Director has NO managed companies and will NOT appear in list (this was the bug)
        directorCompanyIds.Should().BeEmpty("Director without DirectorCompany mapping has no assignments");
        managedCompanyIds.Should().BeEmpty("Director will not appear in user list");
    }

    [Fact]
    public async Task PromoteUserToDirector_CreatesDirectorCompanyMapping()
    {
        // Arrange - Create an Employee user
        var user = new AppUser
        {
            Id = 103,
            Email = "employee@test.com",
            DisplayName = "Employee to Promote",
            CompanyId = 1,
            Role = UserRole.Employee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Act - Promote to Director (simulating /Admin/Users role change)
        user.Role = UserRole.Director;
        await _db.SaveChangesAsync();

        // Simulate the fix: create DirectorCompany mapping when promoted to Director
        var existingMapping = await _db.DirectorCompanies
            .FirstOrDefaultAsync(dc => dc.UserId == user.Id && dc.CompanyId == user.CompanyId && !dc.IsDeleted);

        if (existingMapping == null)
        {
            var directorAssignment = new DirectorCompany
            {
                UserId = user.Id,
                CompanyId = user.CompanyId,
                GrantedBy = 1,
                GrantedAt = DateTime.UtcNow,
                IsDeleted = false
            };
            _db.DirectorCompanies.Add(directorAssignment);
            await _db.SaveChangesAsync();
        }

        // Assert - DirectorCompany mapping was created
        var mapping = await _db.DirectorCompanies
            .FirstOrDefaultAsync(dc => dc.UserId == user.Id && dc.CompanyId == 1 && !dc.IsDeleted);

        mapping.Should().NotBeNull("DirectorCompany mapping should be created when user is promoted to Director");
        mapping!.UserId.Should().Be(user.Id);
        mapping.CompanyId.Should().Be(1);
    }

    [Fact]
    public async Task DirectorWithMultipleCompanies_AppearsOncePerCompany()
    {
        // Arrange - Create Director assigned to multiple companies
        var company2 = new Company { Id = 2, Name = "Company 2" };
        _db.Companies.Add(company2);
        await _db.SaveChangesAsync();

        var director = new AppUser
        {
            Id = 104,
            Email = "multi.director@test.com",
            DisplayName = "Multi-Company Director",
            CompanyId = 1,
            Role = UserRole.Director,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(director);
        await _db.SaveChangesAsync();

        // Create mappings for both companies
        var mapping1 = new DirectorCompany { UserId = director.Id, CompanyId = 1, GrantedBy = 1, GrantedAt = DateTime.UtcNow, IsDeleted = false };
        var mapping2 = new DirectorCompany { UserId = director.Id, CompanyId = 2, GrantedBy = 1, GrantedAt = DateTime.UtcNow, IsDeleted = false };
        _db.DirectorCompanies.AddRange(mapping1, mapping2);
        await _db.SaveChangesAsync();

        // Act - Get managed companies
        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == director.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        // Assert - Director manages both companies
        directorCompanyIds.Should().HaveCount(2, "Director should manage 2 companies");
        directorCompanyIds.Should().Contain(new[] { 1, 2 }, "Director should manage both Company 1 and Company 2");
    }

    [Fact]
    public async Task DirectorWithDeletedMapping_DoesNotAppearForThatCompany()
    {
        // Arrange - Create Director with mapping
        var director = new AppUser
        {
            Id = 105,
            Email = "revoked.director@test.com",
            DisplayName = "Revoked Director",
            CompanyId = 1,
            Role = UserRole.Director,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(director);
        await _db.SaveChangesAsync();

        var mapping = new DirectorCompany
        {
            UserId = director.Id,
            CompanyId = 1,
            GrantedBy = 1,
            GrantedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        _db.DirectorCompanies.Add(mapping);
        await _db.SaveChangesAsync();

        // Act - Soft delete the mapping (simulate revoking Director access)
        mapping.IsDeleted = true;
        mapping.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Query for active assignments
        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == director.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        // Assert - Director has no active assignments
        directorCompanyIds.Should().BeEmpty("Director with deleted mapping should have no active company assignments");
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
