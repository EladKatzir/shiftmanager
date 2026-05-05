using ShiftManager.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.Chores;

/// <summary>
/// Tests for ChoreService CRUD operations.
/// ChoreService is heavily auth-gated (CanUserManageChoresAsync → IGrantService.HasGrantAsync).
/// Tests mock IGrantService to allow/deny access as needed.
/// </summary>
public class ChoreCrudTests : MasterTestBase
{
    public ChoreCrudTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // HELPER: Create ChoreService with configurable grant authorization
    // ================================================================

    private ChoreService CreateChoreServiceWithGrants(
        int currentUserId, int tenantCompanyId,
        bool canManageChores = true, bool canManageForAssignee = true)
    {
        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(tenantCompanyId);

        var httpContextMock = CreateHttpContextAccessor(currentUserId, tenantCompanyId);

        var directorService = Mock.Of<IDirectorService>();

        var grantMock = new Mock<IGrantService>();
        grantMock.Setup(g => g.HasGrantAsync(currentUserId, "AssignChores"))
            .ReturnsAsync(canManageChores);
        grantMock.Setup(g => g.HasGrantForCompanyAsync(currentUserId, "AssignChores", It.IsAny<int>()))
            .ReturnsAsync(canManageForAssignee);

        var logger = Mock.Of<ILogger<ChoreService>>();

        var companyCacheMock = new Mock<ICompanyCacheService>();
        // Return the company from fixture data for molecule resolution
        companyCacheMock.Setup(c => c.GetCompanyAsync(It.IsAny<int>()))
            .ReturnsAsync((int companyId) =>
                Fixture.CompanyByName.Values.FirstOrDefault(c => c.Id == companyId));

        return new ChoreService(
            Db, tenantMock.Object, httpContextMock,
            directorService, grantMock.Object, logger, companyCacheMock.Object,
            BusyServiceMockFactory.Real(Db));
    }

    // ================================================================
    // CREATE TESTS
    // ================================================================

    [Fact]
    public async Task CreateChore_ValidInput_Succeeds()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        var (success, message, chore, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "Guard Duty");

        success.Should().BeTrue();
        chore.Should().NotBeNull();
        chore!.UserId.Should().Be(employee.Id);
        chore.Title.Should().Be("Guard Duty");
        chore.Date.Should().Be(date);
        chore.CompanyId.Should().Be(employee.CompanyId);
        chore.CreatedBy.Should().Be(lead.Id);
        chore.CanceledAt.Should().BeNull();

        TrackEntity(chore);
    }

    [Fact]
    public async Task CreateChore_SetsCompanyFromAssignee()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(31));

        var (success, _, chore, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "Patrol");

        success.Should().BeTrue();
        chore!.CompanyId.Should().Be(employee.CompanyId);

        TrackEntity(chore);
    }

    [Fact]
    public async Task CreateChore_WithNotes_PersistsNotes()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(32));

        var (success, _, chore, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "Kitchen Duty", notes: "Morning shift only");

        success.Should().BeTrue();
        chore!.Notes.Should().Be("Morning shift only");

        TrackEntity(chore);
    }

    [Fact]
    public async Task CreateChore_EmptyTitle_Fails()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(33));

        var (success, message, _, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "  ");

        success.Should().BeFalse();
        message.Should().Contain("title");
    }

    [Fact]
    public async Task CreateChore_NonExistentAssignee_Fails()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(34));

        var (success, message, _, _, _) = await service.CreateChoreAsync(
            99999, date, "Test Chore");

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    // ================================================================
    // CANCEL TESTS
    // ================================================================

    [Fact]
    public async Task CancelChore_ExistingChore_Succeeds()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(35));

        var (_, _, chore, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "Cleanup");
        TrackEntity(chore!);

        var (success, message) = await service.CancelChoreAsync(chore!.Id);

        success.Should().BeTrue();

        var canceled = await Db.Chores.FindAsync(chore.Id);
        canceled!.CanceledAt.Should().NotBeNull();
        canceled.CanceledBy.Should().Be(lead.Id);
    }

    [Fact]
    public async Task CancelChore_AlreadyCanceled_Fails()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(36));

        var (_, _, chore, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "Double Cancel Test");
        TrackEntity(chore!);

        await service.CancelChoreAsync(chore!.Id);
        var (success, _) = await service.CancelChoreAsync(chore.Id);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task CancelChore_NonExistentChore_Fails()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);

        var (success, message) = await service.CancelChoreAsync(99999);

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    // ================================================================
    // CONFLICT DETECTION TESTS
    // ================================================================

    [Fact]
    public async Task HasActiveChoreOnDate_WithExistingChore_ReturnsTrue()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(37));

        var (_, _, chore, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "Conflict Detection Test");
        TrackEntity(chore!);

        var hasChore = await service.HasActiveChoreOnDateAsync(employee.Id, date);
        hasChore.Should().BeTrue();
    }

    [Fact]
    public async Task CreateChore_DuplicateOnSameDate_ReturnsOverrideableWarning()
    {
        // Severity policy (2026-05-03): same-day duplicate chore is an overrideable warning,
        // not a hard error.
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(38));

        var (success1, _, chore1, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "First Chore");
        TrackEntity(chore1!);

        var (success2, message, _, validation, overrideToken) = await service.CreateChoreAsync(
            employee.Id, date, "Second Chore");

        success1.Should().BeTrue();
        success2.Should().BeFalse();
        message.Should().Be("BUSY_OVERRIDE_REQUIRED");
        validation!.Warnings.Should().Contain(w => w.Key == "CHORE_CONFLICT");
        overrideToken.Should().NotBeNullOrEmpty();
    }

    // ================================================================
    // AUTHORIZATION TESTS
    // ================================================================

    [Fact]
    public async Task CreateChore_WithoutPermission_Fails()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateChoreServiceWithGrants(
            employee.Id, employee.CompanyId, canManageChores: false);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(39));

        var (success, message, _, _, _) = await service.CreateChoreAsync(
            employee.Id, date, "Unauthorized Chore");

        success.Should().BeFalse();
        message.Should().Contain("permission");
    }
}
