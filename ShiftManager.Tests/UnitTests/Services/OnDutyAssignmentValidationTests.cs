using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Phase 2d: tests for <see cref="OnDutyService.ValidateOnDutyAssignmentAsync"/>. Wiring tests over
/// <see cref="IBusyService.ValidateAsync"/> with <c>BusyTarget.OnDuty</c>; deep logic in BusyServiceTests.
/// </summary>
public class OnDutyAssignmentValidationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OnDutyService _service;
    private const int MoleculeId = 1;
    private const int CompanyId = 1;

    public OnDutyAssignmentValidationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new AppDbContext(options);

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "100"),
            new Claim("CompanyId", CompanyId.ToString())
        }, "TestAuth"));
        httpContextAccessorMock.Setup(x => x.HttpContext).Returns(ctx);

        _service = new OnDutyService(
            _db,
            httpContextAccessorMock.Object,
            Mock.Of<IDirectorService>(),
            Mock.Of<IGrantService>(),
            Mock.Of<ILogger<OnDutyService>>(),
            Mock.Of<IFeatureFlagService>(),
            BusyServiceMockFactory.Real(_db));

        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = MoleculeId, Name = "TestCo", DisplayName = "Test Co" });
        _db.SaveChanges();
    }

    private async Task SeedUserAsync(int userId, bool active = true)
    {
        _db.Users.Add(new AppUser { Id = userId, Email = $"u{userId}@t.com", DisplayName = $"U{userId}", CompanyId = CompanyId, IsActive = active, Role = UserRole.Employee });
        await _db.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ValidateOnDutyAssignmentAsync_ValidUser_NoConflicts_CanAssignTrue()
    {
        await SeedUserAsync(1);
        var result = await _service.ValidateOnDutyAssignmentAsync(userId: 1, new DateOnly(2026, 6, 15), OnDutyType.Hakam, MoleculeId);

        result.CanAssign.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateOnDutyAssignmentAsync_UserNotFound_ReturnsHardError()
    {
        var result = await _service.ValidateOnDutyAssignmentAsync(userId: 999, new DateOnly(2026, 6, 15), OnDutyType.Hakam, MoleculeId);

        result.CanAssign.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "USER_NOT_FOUND");
    }

    [Fact]
    public async Task ValidateOnDutyAssignmentAsync_InactiveUser_ReturnsHardError()
    {
        await SeedUserAsync(1, active: false);
        var result = await _service.ValidateOnDutyAssignmentAsync(userId: 1, new DateOnly(2026, 6, 15), OnDutyType.Hakam, MoleculeId);

        result.CanAssign.Should().BeFalse();
        // BusyService produces USER_INACTIVE for inactive users on the OnDuty path.
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateOnDutyAssignmentAsync_VacationOverlap_ReturnsWarning_NotBlocked()
    {
        await SeedUserAsync(1);
        var date = new DateOnly(2026, 6, 15);
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            UserId = 1, CompanyId = CompanyId, Status = RequestStatus.Approved,
            StartDate = date, EndDate = date,
            Type = TimeOffType.Vacation
        });
        await _db.SaveChangesAsync();

        var result = await _service.ValidateOnDutyAssignmentAsync(userId: 1, date, OnDutyType.Hakam, MoleculeId);

        result.CanAssign.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Key == "VACATION_CONFLICT");
    }
}
