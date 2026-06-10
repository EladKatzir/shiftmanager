using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Services.Notifications;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// End-to-end gate wiring: NotificationService constructed WITH a real NotificationPreferenceService
/// must suppress the email channel when the recipient has muted the category, while still persisting
/// the in-app notification.
/// GI-01 unmuted → email sent. GI-02 muted → email suppressed, in-app still created.
/// </summary>
public class NotificationGateIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly Mock<IMailService> _mail;
    private readonly NotificationService _service;
    private readonly NotificationPreferenceService _prefs;

    private const int UserId = 50;
    private const int CompanyId = 1;

    public NotificationGateIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _db.Users.Add(new AppUser { Id = UserId, CompanyId = CompanyId, Email = "e@test.local", DisplayName = "E" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var tenant = new Mock<ITenantResolver>();
        tenant.Setup(x => x.GetCurrentTenantId()).Returns(CompanyId);

        _mail = new Mock<IMailService>();
        _mail.Setup(x => x.SendShiftAssignedEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>())).ReturnsAsync(OperationResult.Ok());
        _mail.Setup(x => x.SendMailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(OperationResult.Ok());

        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(x => x[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        localizer.Setup(x => x[It.IsAny<string>(), It.IsAny<object[]>()]).Returns<string, object[]>((k, a) => new LocalizedString(k, k));

        var localization = new Mock<ILocalizationService>();
        var companyLoc = new Mock<ICompanyLocalizationService>();

        _prefs = new NotificationPreferenceService(_db, new Mock<ILogger<NotificationPreferenceService>>().Object);

        _service = new NotificationService(
            _db,
            new Mock<ILogger<NotificationService>>().Object,
            tenant.Object,
            _mail.Object,
            localizer.Object,
            new Mock<IConfiguration>().Object,
            localization.Object,
            companyLoc.Object,
            _prefs);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact] // GI-01
    public async Task Unmuted_SendsEmail()
    {
        await _service.CreateShiftAddedNotificationAsync(UserId, "Morning", new DateOnly(2026, 6, 11), new TimeOnly(8, 0), new TimeOnly(16, 0));

        _mail.Verify(x => x.SendShiftAssignedEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()), Times.Once);
        (await _db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == UserId)).Should().Be(1);
    }

    [Fact] // GI-02
    public async Task MutedCategory_SuppressesEmail_ButKeepsInApp()
    {
        await _prefs.SetCategoryMuteAsync(UserId, CompanyId, NotificationCategory.ShiftAssignment, muted: true);

        await _service.CreateShiftAddedNotificationAsync(UserId, "Morning", new DateOnly(2026, 6, 11), new TimeOnly(8, 0), new TimeOnly(16, 0));

        _mail.Verify(x => x.SendShiftAssignedEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()), Times.Never);
        (await _db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == UserId)).Should().Be(1);
    }

    [Fact] // GI-03: NotifyAsync security-critical always emails, even Quiet + muted
    public async Task NotifyAsync_SecurityCritical_AlwaysEmails()
    {
        await _prefs.SetEngagementModeAsync(UserId, CompanyId, EngagementMode.Quiet);
        await _prefs.SetCategoryMuteAsync(UserId, CompanyId, NotificationCategory.AccountSecurity, muted: true);

        await _service.NotifyAsync(UserId, NotificationType.PasswordReset, NotificationCategory.AccountSecurity,
            "Password reset", "Your password was reset.", personallyActionable: true, securityCritical: true);

        _mail.Verify(x => x.SendMailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        (await _db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == UserId)).Should().Be(1);
    }

    [Fact] // GI-04: NotifyAsync muted non-security category suppresses email, keeps in-app
    public async Task NotifyAsync_Muted_Suppresses()
    {
        await _prefs.SetCategoryMuteAsync(UserId, CompanyId, NotificationCategory.Account, muted: true);

        await _service.NotifyAsync(UserId, NotificationType.RoleChanged, NotificationCategory.Account,
            "Role changed", "Your role changed.", personallyActionable: true, securityCritical: false);

        _mail.Verify(x => x.SendMailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        (await _db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == UserId)).Should().Be(1);
    }
}
