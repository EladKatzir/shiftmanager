using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.OnDuty;

/// <summary>
/// Tests for OnDutyService CRUD operations.
/// OnDutyService is auth-gated via IGrantService (AssignHakamDuties/AssignKatzinDuties)
/// and has feature-flag-gated rank eligibility checks.
/// OnDuty is global (no CompanyId, no tenant query filter).
/// </summary>
public class OnDutyCrudTests : MasterTestBase
{
    public OnDutyCrudTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // HELPER: Create OnDutyService with configurable grant + feature flags
    // ================================================================

    private OnDutyService CreateOnDutyServiceWithGrants(
        int currentUserId, int tenantCompanyId,
        bool canManageHakam = true, bool canManageKatzin = true,
        bool enforceRankEligibility = false)
    {
        var httpContextMock = CreateHttpContextAccessor(currentUserId, tenantCompanyId);
        var directorService = Mock.Of<IDirectorService>();

        var grantMock = new Mock<IGrantService>();
        grantMock.Setup(g => g.HasGrantAsync(currentUserId, "AssignHakamDuties"))
            .ReturnsAsync(canManageHakam);
        grantMock.Setup(g => g.HasGrantAsync(currentUserId, "AssignKatzinDuties"))
            .ReturnsAsync(canManageKatzin);
        grantMock.Setup(g => g.HasGrantForCompanyAsync(currentUserId, "AssignHakamDuties", It.IsAny<int>()))
            .ReturnsAsync(canManageHakam);
        grantMock.Setup(g => g.HasGrantForCompanyAsync(currentUserId, "AssignKatzinDuties", It.IsAny<int>()))
            .ReturnsAsync(canManageKatzin);

        var logger = Mock.Of<ILogger<OnDutyService>>();

        var featureFlagMock = new Mock<IFeatureFlagService>();
        featureFlagMock.Setup(f => f.IsEnabledAsync(
                Data.SeedData.FeatureFlagSeed.Flags.EnforceRankEligibility,
                It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(enforceRankEligibility);

        return new OnDutyService(
            Db, httpContextMock, directorService,
            grantMock.Object, logger, featureFlagMock.Object);
    }

    // ================================================================
    // CREATE TESTS
    // ================================================================

    [Fact]
    public async Task CreateOnDuty_Hakam_Succeeds()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(50));

        var (success, message, onDuty) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam);

        success.Should().BeTrue(message);
        onDuty.Should().NotBeNull();
        onDuty!.UserId.Should().Be(employee.Id);
        onDuty.Date.Should().Be(date);
        onDuty.Type.Should().Be(OnDutyType.Hakam);
        onDuty.CreatedBy.Should().Be(lead.Id);
        onDuty.CanceledAt.Should().BeNull();

        TrackEntity(onDuty);
    }

    [Fact]
    public async Task CreateOnDuty_Lead_Succeeds()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(51));

        var (success, _, onDuty) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Lead);

        success.Should().BeTrue();
        onDuty!.Type.Should().Be(OnDutyType.Lead);

        TrackEntity(onDuty);
    }

    [Fact]
    public async Task CreateOnDuty_WithNotes_PersistsNotes()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(52));

        var (success, _, onDuty) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam, notes: "Special instructions");

        success.Should().BeTrue();
        onDuty!.Notes.Should().Be("Special instructions");

        TrackEntity(onDuty);
    }

    // ================================================================
    // CANCEL TESTS
    // ================================================================

    [Fact]
    public async Task CancelOnDuty_ExistingAssignment_Succeeds()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(53));

        var (_, _, onDuty) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam);
        TrackEntity(onDuty!);

        var (success, _) = await service.CancelOnDutyAsync(onDuty!.Id);

        success.Should().BeTrue();

        var canceled = await Db.OnDuties.FindAsync(onDuty.Id);
        canceled!.CanceledAt.Should().NotBeNull();
        canceled.CanceledBy.Should().Be(lead.Id);
    }

    [Fact]
    public async Task CancelOnDuty_AlreadyCanceled_Fails()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(54));

        var (_, _, onDuty) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam);
        TrackEntity(onDuty!);

        await service.CancelOnDutyAsync(onDuty!.Id);
        var (success, _) = await service.CancelOnDutyAsync(onDuty.Id);

        success.Should().BeFalse();
    }

    // ================================================================
    // CONFLICT DETECTION TESTS
    // ================================================================

    [Fact]
    public async Task HasActiveOnDutyOnDate_WithExisting_ReturnsTrue()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(55));

        var (_, _, onDuty) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam);
        TrackEntity(onDuty!);

        var hasOnDuty = await service.HasActiveOnDutyOnDateAsync(
            employee.Id, date, OnDutyType.Hakam);
        hasOnDuty.Should().BeTrue();
    }

    [Fact]
    public async Task CreateOnDuty_DuplicateOnSameDateAndType_Fails()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(56));

        var (success1, _, onDuty1) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam);
        TrackEntity(onDuty1!);

        var (success2, message, _) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam);

        success1.Should().BeTrue();
        success2.Should().BeFalse();
        message.Should().Contain("already has an active");
    }

    [Fact]
    public async Task CreateOnDuty_SameDateDifferentType_Succeeds()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(57));

        var (success1, _, onDuty1) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam);
        TrackEntity(onDuty1!);

        var (success2, _, onDuty2) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Lead);
        if (onDuty2 != null) TrackEntity(onDuty2);

        success1.Should().BeTrue();
        success2.Should().BeTrue();
    }

    // ================================================================
    // AUTHORIZATION TESTS
    // ================================================================

    [Fact]
    public async Task CreateOnDuty_WithoutPermission_Fails()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var service = CreateOnDutyServiceWithGrants(
            employee.Id, employee.CompanyId,
            canManageHakam: false, canManageKatzin: false);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(58));

        var (success, message, _) = await service.CreateOnDutyAsync(
            employee.Id, date, OnDutyType.Hakam);

        success.Should().BeFalse();
        message.Should().Contain("permission");
    }

    [Fact]
    public async Task CreateOnDuty_NonExistentAssignee_Fails()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var service = CreateOnDutyServiceWithGrants(lead.Id, lead.CompanyId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(59));

        var (success, message, _) = await service.CreateOnDutyAsync(
            99999, date, OnDutyType.Hakam);

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }
}
