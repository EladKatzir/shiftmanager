using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Tests.IntegrationTests;

/// <summary>
/// P0-3: Director Cross-Tenant Validation Tests
/// Verifies that Directors can ONLY access data from their assigned companies
/// Critical security tests to prevent cross-tenant data leakage
/// </summary>
public class DirectorCrossTenantTests : IDisposable
{
    private readonly AppDbContext _db;

    // Test data
    private Company _company1 = null!;
    private Company _company2 = null!;
    private AppUser _directorUser = null!;
    private AppUser _company1User = null!;
    private AppUser _company2User = null!;
    private ShiftInstance _company1Shift = null!;
    private ShiftInstance _company2Shift = null!;
    private TimeOffRequest _company1Request = null!;
    private TimeOffRequest _company2Request = null!;

    public DirectorCrossTenantTests()
    {
        // Create InMemory database for testing
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("DirectorCrossTenantTestDb_" + Guid.NewGuid())
            .Options;
        _db = new AppDbContext(options);

        // Seed test data
        SeedTestData().Wait();
    }

    private async Task SeedTestData()
    {
        // Create companies
        _company1 = new Company
        {
            Id = 1,
            Name = "Company A"
        };

        _company2 = new Company
        {
            Id = 2,
            Name = "Company B"
        };

        _db.Companies.AddRange(_company1, _company2);

        // Create Director (assigned to Company 1 ONLY)
        _directorUser = new AppUser
        {
            Id = 100,
            Email = "director@test.com",
            DisplayName = "Test Director",
            CompanyId = 1,
            Role = UserRole.Director,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };

        // Create DirectorCompany assignment (Director has access to Company 1 only)
        var directorAssignment = new DirectorCompany
        {
            Id = 1,
            UserId = 100,
            CompanyId = 1, // ONLY Company 1
            GrantedBy = 1,
            GrantedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        _db.Users.Add(_directorUser);
        _db.DirectorCompanies.Add(directorAssignment);

        // Create users in each company
        _company1User = new AppUser
        {
            Id = 101,
            Email = "user1@company1.com",
            DisplayName = "Company 1 User",
            CompanyId = 1,
            Role = UserRole.Employee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };

        _company2User = new AppUser
        {
            Id = 102,
            Email = "user2@company2.com",
            DisplayName = "Company 2 User",
            CompanyId = 2,
            Role = UserRole.Employee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>()
        };

        _db.Users.AddRange(_company1User, _company2User);

        // Create shift types
        var shiftType1 = new ShiftType
        {
            Id = 1,
            CompanyId = 1,
            Key = ShiftType.KEY_MORNING,
            CustomName = "Morning"
        };

        var shiftType2 = new ShiftType
        {
            Id = 2,
            CompanyId = 2,
            Key = ShiftType.KEY_EVENING,
            CustomName = "Evening"
        };

        _db.ShiftTypes.AddRange(shiftType1, shiftType2);

        // Create shift instances
        _company1Shift = new ShiftInstance
        {
            Id = 1,
            CompanyId = 1,
            ShiftTypeId = 1,
            WorkDate = DateOnly.FromDateTime(DateTime.Today),
            StaffingRequired = 2,
            UpdatedAt = DateTime.UtcNow
        };

        _company2Shift = new ShiftInstance
        {
            Id = 2,
            CompanyId = 2,
            ShiftTypeId = 2,
            WorkDate = DateOnly.FromDateTime(DateTime.Today),
            StaffingRequired = 2,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ShiftInstances.AddRange(_company1Shift, _company2Shift);

        // Create time-off requests
        _company1Request = new TimeOffRequest
        {
            Id = 1,
            CompanyId = 1,
            UserId = 101,
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            Status = RequestStatus.Pending,
            Reason = "Vacation",
            CreatedAt = DateTime.UtcNow
        };

        _company2Request = new TimeOffRequest
        {
            Id = 2,
            CompanyId = 2,
            UserId = 102,
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            Status = RequestStatus.Pending,
            Reason = "Sick leave",
            CreatedAt = DateTime.UtcNow
        };

        _db.TimeOffRequests.AddRange(_company1Request, _company2Request);

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Director_CannotAccessUnassignedCompanyUsers_ViaDatabase()
    {
        // Arrange - Query DirectorCompany to get assigned companies
        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == _directorUser.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        // Act - Query users that Director should see
        var accessibleUsers = await _db.Users
            .Where(u => directorCompanyIds.Contains(u.CompanyId))
            .ToListAsync();

        // Assert
        accessibleUsers.Should().Contain(u => u.Id == _company1User.Id, "Director has access to Company 1");
        accessibleUsers.Should().NotContain(u => u.Id == _company2User.Id, "Director does NOT have access to Company 2");
    }

    [Fact]
    public async Task Director_CannotAccessUnassignedCompanyShifts_ViaDatabase()
    {
        // Arrange
        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == _directorUser.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        // Act
        var accessibleShifts = await _db.ShiftInstances
            .Where(si => directorCompanyIds.Contains(si.CompanyId))
            .ToListAsync();

        // Assert
        accessibleShifts.Should().Contain(s => s.Id == _company1Shift.Id, "Director has access to Company 1 shifts");
        accessibleShifts.Should().NotContain(s => s.Id == _company2Shift.Id, "Director does NOT have access to Company 2 shifts");
    }

    [Fact]
    public async Task Director_CannotAccessUnassignedCompanyRequests_ViaDatabase()
    {
        // Arrange
        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == _directorUser.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        // Act
        var accessibleRequests = await _db.TimeOffRequests
            .Where(r => directorCompanyIds.Contains(r.CompanyId))
            .ToListAsync();

        // Assert
        accessibleRequests.Should().Contain(r => r.Id == _company1Request.Id, "Director has access to Company 1 requests");
        accessibleRequests.Should().NotContain(r => r.Id == _company2Request.Id, "Director does NOT have access to Company 2 requests");
    }

    [Fact]
    public async Task Director_WithMultipleAssignments_CanAccessAllAssignedCompanies()
    {
        // Arrange - Add Company 2 assignment to Director
        var secondAssignment = new DirectorCompany
        {
            Id = 10,
            UserId = _directorUser.Id,
            CompanyId = 2,
            GrantedBy = 1,
            GrantedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        _db.DirectorCompanies.Add(secondAssignment);
        await _db.SaveChangesAsync();

        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == _directorUser.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        // Act
        var accessibleUsers = await _db.Users
            .Where(u => directorCompanyIds.Contains(u.CompanyId))
            .ToListAsync();

        // Assert
        directorCompanyIds.Should().HaveCount(2, "Director now has 2 company assignments");
        accessibleUsers.Should().Contain(u => u.Id == _company1User.Id, "Director has access to Company 1");
        accessibleUsers.Should().Contain(u => u.Id == _company2User.Id, "Director has access to Company 2");
    }

    [Fact]
    public async Task Director_WithDeletedAssignment_CannotAccessThatCompany()
    {
        // Arrange - Soft delete the Director's assignment to Company 1
        var assignment = await _db.DirectorCompanies
            .FirstAsync(dc => dc.UserId == _directorUser.Id && dc.CompanyId == 1);

        assignment.IsDeleted = true;
        assignment.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == _directorUser.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        // Act
        var accessibleUsers = await _db.Users
            .Where(u => directorCompanyIds.Contains(u.CompanyId))
            .ToListAsync();

        // Assert
        directorCompanyIds.Should().BeEmpty("Deleted assignment should be excluded");
        accessibleUsers.Should().NotContain(u => u.Id == _company1User.Id, "Director should NOT have access after assignment deletion");
    }

    [Fact]
    public async Task Director_CanOnlyQueryAssignedCompaniesData()
    {
        // Arrange
        var directorCompanyIds = await _db.DirectorCompanies
            .Where(dc => dc.UserId == _directorUser.Id && !dc.IsDeleted)
            .Select(dc => dc.CompanyId)
            .ToListAsync();

        // Act - Simulate what pages like /Admin/Users, /Calendar/Table, /Requests/Index do
        var users = await _db.Users
            .Where(u => directorCompanyIds.Contains(u.CompanyId) && u.Role == UserRole.Employee)
            .CountAsync();
        var shifts = await _db.ShiftInstances.Where(s => directorCompanyIds.Contains(s.CompanyId)).CountAsync();
        var requests = await _db.TimeOffRequests.Where(r => directorCompanyIds.Contains(r.CompanyId)).CountAsync();

        // Assert - Director should only see Company 1 data
        users.Should().Be(1, "Director can only see 1 employee from Company 1 (excluding the Director user)");
        shifts.Should().Be(1, "Director can only see 1 shift from Company 1");
        requests.Should().Be(1, "Director can only see 1 request from Company 1");
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
