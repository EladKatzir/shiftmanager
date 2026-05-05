using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.IntegrationTests;

/// <summary>
/// Integration tests for the Military Rank System, specifically testing:
/// - Katzin (Lead) assignment with rank validation
/// - Feature flag enforcement for rank eligibility
/// - Eligible assignee filtering by officer rank
/// </summary>
public class MilitaryRankIntegrationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IFeatureFlagService> _mockFeatureFlagService;
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly Mock<IDirectorService> _mockDirectorService;
    private readonly Mock<IGrantService> _mockGrantService;

    // Test user IDs
    private const int ManagerUserId = 1;
    private const int EnlistedUserId = 10;
    private const int OfficerUserId = 11;
    private const int InactiveOfficerUserId = 12;

    public MilitaryRankIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("MilitaryRankTestDb_" + Guid.NewGuid())
            .Options;
        _db = new AppDbContext(options);

        _mockFeatureFlagService = new Mock<IFeatureFlagService>();
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _mockDirectorService = new Mock<IDirectorService>();
        _mockGrantService = new Mock<IGrantService>();

        // Setup default grant behavior for Manager user
        _mockGrantService.Setup(g => g.HasGrantAsync(ManagerUserId, It.IsAny<string>())).ReturnsAsync(true);
        _mockGrantService.Setup(g => g.HasGrantForCompanyAsync(ManagerUserId, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);

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

        // Create manager user (for making assignments)
        var manager = new AppUser
        {
            Id = ManagerUserId,
            Email = "manager@test.com",
            DisplayName = "Test Manager",
            CompanyId = 1,
            Role = UserRole.Manager,
            IsActive = true,
            Rank = MilitaryRank.Seren, // Captain - Officer
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(manager);

        // Create enlisted user (Samal - NCO, not an officer)
        var enlistedUser = new AppUser
        {
            Id = EnlistedUserId,
            Email = "enlisted@test.com",
            DisplayName = "Enlisted User",
            CompanyId = 1,
            Role = UserRole.Employee,
            IsActive = true,
            Rank = MilitaryRank.Samal, // Sergeant - NCO (not an officer)
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(enlistedUser);

        // Create officer user (Seren - Captain)
        var officerUser = new AppUser
        {
            Id = OfficerUserId,
            Email = "officer@test.com",
            DisplayName = "Officer User",
            CompanyId = 1,
            Role = UserRole.Employee,
            IsActive = true,
            Rank = MilitaryRank.Seren, // Captain - Officer
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(officerUser);

        // Create inactive officer user
        var inactiveOfficer = new AppUser
        {
            Id = InactiveOfficerUserId,
            Email = "inactive.officer@test.com",
            DisplayName = "Inactive Officer",
            CompanyId = 1,
            Role = UserRole.Employee,
            IsActive = false,
            Rank = MilitaryRank.Segen, // Lieutenant - Officer (but inactive)
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(inactiveOfficer);

        await _db.SaveChangesAsync();
    }

    private OnDutyService CreateService(bool enforceRankEligibility = true)
    {
        // Setup feature flag service
        _mockFeatureFlagService
            .Setup(f => f.IsEnabledAsync(FeatureFlagSeed.Flags.EnforceRankEligibility, null, null))
            .ReturnsAsync(enforceRankEligibility);

        // Setup HTTP context with manager user
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, ManagerUserId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = claimsPrincipal };
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        return new OnDutyService(
            _db,
            _mockHttpContextAccessor.Object,
            _mockDirectorService.Object,
            _mockGrantService.Object,
            NullLogger<OnDutyService>.Instance,
            _mockFeatureFlagService.Object,
            BusyServiceMockFactory.NoOp());
    }

    #region Katzin (Lead) Assignment Tests

    [Fact]
    public async Task CreateOnDuty_KatzinWithEnlistedRank_ShouldFail_WhenFlagEnabled()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);
        var date = DateOnly.FromDateTime(DateTime.Today);

        // Act - Try to assign Lead (Katzin) duty to enlisted user
        var result = await service.CreateOnDutyAsync(
            assigneeId: EnlistedUserId,
            date: date,
            type: OnDutyType.Lead,
            notes: "Test Katzin assignment");

        // Assert
        result.Success.Should().BeFalse("Enlisted user should not be eligible for Katzin duty");
        result.Message.Should().Be("OFFICER_RANK_REQUIRED",
            "Error message should indicate officer rank is required");
        result.OnDuty.Should().BeNull();
    }

    [Fact]
    public async Task CreateOnDuty_KatzinWithOfficerRank_ShouldSucceed()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);
        var date = DateOnly.FromDateTime(DateTime.Today);

        // Act - Assign Lead (Katzin) duty to officer user
        var result = await service.CreateOnDutyAsync(
            assigneeId: OfficerUserId,
            date: date,
            type: OnDutyType.Lead,
            notes: "Test Katzin assignment");

        // Assert
        result.Success.Should().BeTrue("Officer should be eligible for Katzin duty");
        result.OnDuty.Should().NotBeNull();
        result.OnDuty!.UserId.Should().Be(OfficerUserId);
        result.OnDuty.Type.Should().Be(OnDutyType.Lead);
    }

    [Fact]
    public async Task CreateOnDuty_HakamWithEnlistedRank_ShouldSucceed_WhenFlagEnabled()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);
        var date = DateOnly.FromDateTime(DateTime.Today);

        // Act - Assign Hakam duty to enlisted user (Hakam doesn't require officer)
        var result = await service.CreateOnDutyAsync(
            assigneeId: EnlistedUserId,
            date: date,
            type: OnDutyType.Hakam,
            notes: "Test Hakam assignment");

        // Assert
        result.Success.Should().BeTrue("Hakam duty should not require officer rank");
        result.OnDuty.Should().NotBeNull();
        result.OnDuty!.UserId.Should().Be(EnlistedUserId);
        result.OnDuty.Type.Should().Be(OnDutyType.Hakam);
    }

    #endregion

    #region Feature Flag Tests

    [Fact]
    public async Task CreateOnDuty_KatzinWithEnlistedRank_ShouldSucceed_WhenFlagDisabled()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: false);
        var date = DateOnly.FromDateTime(DateTime.Today);

        // Act - Try to assign Lead (Katzin) duty to enlisted user with flag disabled
        var result = await service.CreateOnDutyAsync(
            assigneeId: EnlistedUserId,
            date: date,
            type: OnDutyType.Lead,
            notes: "Test Katzin assignment with flag disabled");

        // Assert
        result.Success.Should().BeTrue(
            "Enlisted user should be allowed for Katzin duty when feature flag is disabled");
        result.OnDuty.Should().NotBeNull();
        result.OnDuty!.UserId.Should().Be(EnlistedUserId);
        result.OnDuty.Type.Should().Be(OnDutyType.Lead);
    }

    [Fact]
    public async Task CreateOnDuty_OfficerAssignment_ShouldSucceed_RegardlessOfFlag()
    {
        // Arrange - Test with flag disabled
        var service = CreateService(enforceRankEligibility: false);
        var date = DateOnly.FromDateTime(DateTime.Today);

        // Act
        var result = await service.CreateOnDutyAsync(
            assigneeId: OfficerUserId,
            date: date,
            type: OnDutyType.Lead,
            notes: "Test with flag disabled");

        // Assert
        result.Success.Should().BeTrue("Officer should always be eligible for Katzin duty");
        result.OnDuty.Should().NotBeNull();
    }

    #endregion

    #region Eligible Assignees Filtering Tests

    [Fact]
    public async Task GetEligibleUsersForDuty_WithOfficerRequirement_ShouldReturnOnlyOfficers()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var eligibleUsers = await service.GetEligibleUsersForDutyAsync(
            dutyType: OnDutyType.Lead,
            requireOfficer: true);

        // Assert
        eligibleUsers.Should().NotBeEmpty("There should be active officers");
        eligibleUsers.Should().OnlyContain(u => u.Rank.IsOfficer(),
            "Only officers should be returned when requireOfficer is true");
        eligibleUsers.Should().NotContain(u => u.Id == EnlistedUserId,
            "Enlisted user should not be in the list");
        eligibleUsers.Should().NotContain(u => u.Id == InactiveOfficerUserId,
            "Inactive officers should not be in the list");
    }

    [Fact]
    public async Task GetEligibleUsersForDuty_WithoutOfficerRequirement_ShouldReturnAllActiveUsers()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var eligibleUsers = await service.GetEligibleUsersForDutyAsync(
            dutyType: OnDutyType.Hakam,
            requireOfficer: false);

        // Assert
        eligibleUsers.Should().NotBeEmpty("There should be active users");
        eligibleUsers.Should().Contain(u => u.Id == EnlistedUserId,
            "Enlisted user should be included when officer not required");
        eligibleUsers.Should().Contain(u => u.Id == OfficerUserId,
            "Officer should also be included");
        eligibleUsers.Should().NotContain(u => u.Id == InactiveOfficerUserId,
            "Inactive users should not be included");
    }

    [Fact]
    public async Task GetEligibleUsersForDuty_ShouldExcludeInactiveUsers()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var eligibleUsers = await service.GetEligibleUsersForDutyAsync(
            dutyType: OnDutyType.Lead,
            requireOfficer: true);

        // Assert
        eligibleUsers.Should().NotContain(u => u.Id == InactiveOfficerUserId,
            "Inactive officers should be excluded even when they have officer rank");
    }

    #endregion

    #region User Eligibility Check Tests

    [Fact]
    public async Task IsUserEligibleForDuty_EnlistedForOfficerDuty_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var isEligible = await service.IsUserEligibleForDutyAsync(
            userId: EnlistedUserId,
            dutyType: OnDutyType.Lead,
            requireOfficer: true);

        // Assert
        isEligible.Should().BeFalse("Enlisted user should not be eligible when officer required");
    }

    [Fact]
    public async Task IsUserEligibleForDuty_OfficerForOfficerDuty_ShouldReturnTrue()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var isEligible = await service.IsUserEligibleForDutyAsync(
            userId: OfficerUserId,
            dutyType: OnDutyType.Lead,
            requireOfficer: true);

        // Assert
        isEligible.Should().BeTrue("Officer should be eligible for officer duty");
    }

    [Fact]
    public async Task IsUserEligibleForDuty_InactiveOfficer_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var isEligible = await service.IsUserEligibleForDutyAsync(
            userId: InactiveOfficerUserId,
            dutyType: OnDutyType.Lead,
            requireOfficer: true);

        // Assert
        isEligible.Should().BeFalse("Inactive officer should not be eligible");
    }

    [Fact]
    public async Task IsUserEligibleForDuty_NonExistentUser_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var isEligible = await service.IsUserEligibleForDutyAsync(
            userId: 9999, // Non-existent user
            dutyType: OnDutyType.Hakam,
            requireOfficer: false);

        // Assert
        isEligible.Should().BeFalse("Non-existent user should not be eligible");
    }

    #endregion

    #region RequiresOfficerForDutyType Tests

    [Fact]
    public async Task RequiresOfficerForDutyType_Lead_ShouldReturnTrue()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var requiresOfficer = await service.RequiresOfficerForDutyTypeAsync(OnDutyType.Lead);

        // Assert
        requiresOfficer.Should().BeTrue("Lead (Katzin) duty should require officer rank");
    }

    [Fact]
    public async Task RequiresOfficerForDutyType_Hakam_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Act
        var requiresOfficer = await service.RequiresOfficerForDutyTypeAsync(OnDutyType.Hakam);

        // Assert
        requiresOfficer.Should().BeFalse("Hakam duty should not require officer rank");
    }

    [Fact]
    public async Task RequiresOfficerForDutyType_CustomTypeWithOfficerRequirement_ShouldReturnTrue()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Create a custom duty type that requires officer
        var customType = new OnDutyTypeConfig
        {
            TypeValue = 100,
            NameEn = "Special Officer Duty",
            NameHe = "תורנות קצין מיוחדת",
            RequiresOfficerRank = true,
            IsActive = true,
            CreatedBy = ManagerUserId
        };
        _db.OnDutyTypeConfigs.Add(customType);
        await _db.SaveChangesAsync();

        // Act
        var requiresOfficer = await service.RequiresOfficerForDutyTypeAsync((OnDutyType)100);

        // Assert
        requiresOfficer.Should().BeTrue(
            "Custom duty type with RequiresOfficerRank=true should require officer");
    }

    [Fact]
    public async Task RequiresOfficerForDutyType_CustomTypeWithoutOfficerRequirement_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService(enforceRankEligibility: true);

        // Create a custom duty type that doesn't require officer
        var customType = new OnDutyTypeConfig
        {
            TypeValue = 101,
            NameEn = "General Duty",
            NameHe = "תורנות כללית",
            RequiresOfficerRank = false,
            IsActive = true,
            CreatedBy = ManagerUserId
        };
        _db.OnDutyTypeConfigs.Add(customType);
        await _db.SaveChangesAsync();

        // Act
        var requiresOfficer = await service.RequiresOfficerForDutyTypeAsync((OnDutyType)101);

        // Assert
        requiresOfficer.Should().BeFalse(
            "Custom duty type with RequiresOfficerRank=false should not require officer");
    }

    #endregion

    #region Rank Boundary Tests

    [Fact]
    public async Task CreateOnDuty_HighestNCORank_ShouldFail_WhenFlagEnabled()
    {
        // Arrange - Create user with RavNagad (highest NCO rank, still not officer)
        var ncoUser = new AppUser
        {
            Id = 20,
            Email = "ravnagad@test.com",
            DisplayName = "Rav Nagad User",
            CompanyId = 1,
            Role = UserRole.Employee,
            IsActive = true,
            Rank = MilitaryRank.RavNagad, // Highest NCO - value 8
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(ncoUser);
        await _db.SaveChangesAsync();

        var service = CreateService(enforceRankEligibility: true);
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        // Act
        var result = await service.CreateOnDutyAsync(
            assigneeId: ncoUser.Id,
            date: date,
            type: OnDutyType.Lead);

        // Assert
        result.Success.Should().BeFalse(
            "RavNagad (value 8) should not be eligible - officers start at value 9");
        result.Message.Should().Be("OFFICER_RANK_REQUIRED");
    }

    [Fact]
    public async Task CreateOnDuty_LowestOfficerRank_ShouldSucceed_WhenFlagEnabled()
    {
        // Arrange - Create user with SegenMishne (lowest officer rank)
        var lowestOfficer = new AppUser
        {
            Id = 21,
            Email = "segenmishne@test.com",
            DisplayName = "Segen Mishne User",
            CompanyId = 1,
            Role = UserRole.Employee,
            IsActive = true,
            Rank = MilitaryRank.SegenMishne, // Lowest officer - value 9
            PasswordHash = Array.Empty<byte>()
        };
        _db.Users.Add(lowestOfficer);
        await _db.SaveChangesAsync();

        var service = CreateService(enforceRankEligibility: true);
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));

        // Act
        var result = await service.CreateOnDutyAsync(
            assigneeId: lowestOfficer.Id,
            date: date,
            type: OnDutyType.Lead);

        // Assert
        result.Success.Should().BeTrue(
            "SegenMishne (value 9) should be eligible - first officer rank");
        result.OnDuty.Should().NotBeNull();
    }

    #endregion

    public void Dispose()
    {
        _db?.Dispose();
    }
}
