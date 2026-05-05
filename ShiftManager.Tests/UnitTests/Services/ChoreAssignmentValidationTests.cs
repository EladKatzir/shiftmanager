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
/// Phase 2d: tests for <see cref="ChoreService.ValidateChoreAssignmentAsync"/>. The method is a
/// thin adapter over <see cref="IBusyService.ValidateAsync"/> with <c>BusyTarget.Chore</c>.
/// These tests verify the wiring: that errors and warnings produced by the underlying BusyService
/// surface correctly on the wrapper's return shape. The deep validation logic itself is exercised
/// by <c>BusyServiceTests</c>.
/// </summary>
public class ChoreAssignmentValidationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ChoreService _service;
    private const int MoleculeId = 1;
    private const int CompanyId = 1;

    public ChoreAssignmentValidationTests()
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

        _service = new ChoreService(
            _db,
            Mock.Of<ITenantResolver>(),
            httpContextAccessorMock.Object,
            Mock.Of<IDirectorService>(),
            Mock.Of<IGrantService>(),
            Mock.Of<ILogger<ChoreService>>(),
            Mock.Of<ICompanyCacheService>(),
            BusyServiceMockFactory.Real(_db));

        SeedHierarchy();
    }

    private void SeedHierarchy()
    {
        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = MoleculeId, Name = "TestCo", DisplayName = "Test Co" });
        _db.SaveChanges();
    }

    private async Task SeedUserAsync(int userId, int companyId = CompanyId, bool active = true)
    {
        _db.Users.Add(new AppUser { Id = userId, Email = $"u{userId}@t.com", DisplayName = $"U{userId}", CompanyId = companyId, IsActive = active, Role = UserRole.Employee });
        await _db.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ValidateChoreAssignmentAsync_ValidUser_NoConflicts_CanAssignTrue()
    {
        await SeedUserAsync(1);
        var date = new DateOnly(2026, 6, 15);

        var result = await _service.ValidateChoreAssignmentAsync(userId: 1, date, MoleculeId);

        result.CanAssign.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateChoreAssignmentAsync_UserNotFound_ReturnsHardErrorBlocked()
    {
        // Don't seed user 999.
        var date = new DateOnly(2026, 6, 15);

        var result = await _service.ValidateChoreAssignmentAsync(userId: 999, date, MoleculeId);

        result.CanAssign.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Errors[0].Key.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task ValidateChoreAssignmentAsync_UserOutsideMolecule_ReturnsHardError()
    {
        // Seed user in a sibling company belonging to a different molecule.
        _db.Companies.Add(new Company { Id = 2, MoleculeId = 99, Name = "OtherCo", DisplayName = "Other Co" });
        await SeedUserAsync(2, companyId: 2);

        var date = new DateOnly(2026, 6, 15);
        var result = await _service.ValidateChoreAssignmentAsync(userId: 2, date, MoleculeId);

        result.CanAssign.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "USER_NOT_IN_MOLECULE");
    }

    [Fact]
    public async Task ValidateChoreAssignmentAsync_VacationOverlap_ReturnsWarning_NotBlocked()
    {
        await SeedUserAsync(1);
        var date = new DateOnly(2026, 6, 15);
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            UserId = 1, CompanyId = CompanyId, Status = RequestStatus.Approved,
            StartDate = date.AddDays(-1), EndDate = date.AddDays(1),
            Type = TimeOffType.Vacation
        });
        await _db.SaveChangesAsync();

        var result = await _service.ValidateChoreAssignmentAsync(userId: 1, date, MoleculeId);

        result.CanAssign.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Key == "VACATION_CONFLICT");
    }
}
