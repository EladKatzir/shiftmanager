using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

public class OnDutyServiceEligibilityTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OnDutyService _service;

    public OnDutyServiceEligibilityTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        var mockConfiguration = new Mock<IConfiguration>();
        var mockGrantService = new Mock<IGrantService>();
        _service = new OnDutyService(_db, null!, null!, mockGrantService.Object, NullLogger<OnDutyService>.Instance, mockConfiguration.Object);

        // Seed test users with explicit Email values
        _db.Users.AddRange(
            new AppUser { Id = 1, DisplayName = "Enlisted", Email = "enlisted@test.com", Rank = MilitaryRank.Samal, IsActive = true, CompanyId = 1 },
            new AppUser { Id = 2, DisplayName = "Officer", Email = "officer@test.com", Rank = MilitaryRank.Seren, IsActive = true, CompanyId = 1 },
            new AppUser { Id = 3, DisplayName = "Inactive Officer", Email = "inactive@test.com", Rank = MilitaryRank.Segen, IsActive = false, CompanyId = 1 }
        );
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetEligibleUsersForDuty_WithOfficerRequirement_ReturnsOnlyOfficers()
    {
        var eligible = await _service.GetEligibleUsersForDutyAsync(OnDutyType.Lead, requireOfficer: true);

        eligible.Should().HaveCount(1);
        eligible.First().Id.Should().Be(2);
    }

    [Fact]
    public async Task GetEligibleUsersForDuty_WithoutOfficerRequirement_ReturnsAllActiveUsers()
    {
        var eligible = await _service.GetEligibleUsersForDutyAsync(OnDutyType.Hakam, requireOfficer: false);

        eligible.Should().HaveCount(2); // Excludes inactive
    }

    [Fact]
    public async Task IsUserEligibleForDuty_ReturnsFalse_WhenEnlistedForOfficerDuty()
    {
        var isEligible = await _service.IsUserEligibleForDutyAsync(1, OnDutyType.Lead, requireOfficer: true);

        isEligible.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserEligibleForDuty_ReturnsTrue_WhenOfficerForOfficerDuty()
    {
        var isEligible = await _service.IsUserEligibleForDutyAsync(2, OnDutyType.Lead, requireOfficer: true);

        isEligible.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserEligibleForDuty_ReturnsFalse_WhenUserInactive()
    {
        var isEligible = await _service.IsUserEligibleForDutyAsync(3, OnDutyType.Lead, requireOfficer: true);

        isEligible.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserEligibleForDuty_ReturnsFalse_WhenUserNotFound()
    {
        var isEligible = await _service.IsUserEligibleForDutyAsync(999, OnDutyType.Hakam, requireOfficer: false);

        isEligible.Should().BeFalse();
    }

    public void Dispose() => _db.Dispose();
}
