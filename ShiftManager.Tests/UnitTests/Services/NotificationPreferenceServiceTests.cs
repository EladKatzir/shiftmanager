using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Services.Notifications;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// DB-backed tests for NotificationPreferenceService (real SQLite, no tenant resolver → no filter).
/// NPS-01 engagement default/get/set · NPS-02 mutes add/remove · NPS-03 ShouldSendEmail precedence
/// · NPS-04 catch-up throttle fire-once + self-heal reset.
/// </summary>
public class NotificationPreferenceServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly NotificationPreferenceService _service;

    private const int UserId = 7;
    private const int CompanyId = 1;

    public NotificationPreferenceServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _service = new NotificationPreferenceService(_db, new Mock<ILogger<NotificationPreferenceService>>().Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // Minimal concrete events for the precedence tests.
    private sealed record ActionableEvent : NotificationEvent
    {
        public override NotificationCategory Category => NotificationCategory.ShiftAssignment;
        public override bool PersonallyActionable => true;
    }
    private sealed record SocialEvent : NotificationEvent
    {
        public override NotificationCategory Category => NotificationCategory.Social;
        public override bool PersonallyActionable => false;
    }
    private sealed record SecurityEvent : NotificationEvent
    {
        public override NotificationCategory Category => NotificationCategory.AccountSecurity;
        public override bool PersonallyActionable => true;
        public override bool SecurityCritical => true;
    }

    [Fact] // NPS-01
    public async Task EngagementMode_DefaultsEngaged_AndPersists()
    {
        (await _service.GetEngagementModeAsync(UserId, CompanyId)).Should().Be(EngagementMode.Engaged);

        await _service.SetEngagementModeAsync(UserId, CompanyId, EngagementMode.Quiet);
        (await _service.GetEngagementModeAsync(UserId, CompanyId)).Should().Be(EngagementMode.Quiet);

        await _service.SetEngagementModeAsync(UserId, CompanyId, EngagementMode.Engaged);
        (await _service.GetEngagementModeAsync(UserId, CompanyId)).Should().Be(EngagementMode.Engaged);
    }

    [Fact] // NPS-02
    public async Task CategoryMutes_AddAndRemove()
    {
        (await _service.GetMutedCategoriesAsync(UserId, CompanyId)).Should().BeEmpty();

        await _service.SetCategoryMuteAsync(UserId, CompanyId, NotificationCategory.Social, muted: true);
        await _service.SetCategoryMuteAsync(UserId, CompanyId, NotificationCategory.Social, muted: true); // idempotent

        var muted = await _service.GetMutedCategoriesAsync(UserId, CompanyId);
        muted.Should().ContainSingle().Which.Should().Be(NotificationCategory.Social);

        await _service.SetCategoryMuteAsync(UserId, CompanyId, NotificationCategory.Social, muted: false);
        (await _service.GetMutedCategoriesAsync(UserId, CompanyId)).Should().BeEmpty();
    }

    [Fact] // NPS-03a: Quiet suppresses non-actionable, allows actionable
    public async Task ShouldSendEmail_QuietMode()
    {
        await _service.SetEngagementModeAsync(UserId, CompanyId, EngagementMode.Quiet);

        (await _service.ShouldSendEmailAsync(UserId, CompanyId, new SocialEvent())).Should().BeFalse();
        (await _service.ShouldSendEmailAsync(UserId, CompanyId, new ActionableEvent())).Should().BeTrue();
    }

    [Fact] // NPS-03b: muted category suppresses; security-critical overrides mute + quiet
    public async Task ShouldSendEmail_MuteAndSecurityCritical()
    {
        await _service.SetEngagementModeAsync(UserId, CompanyId, EngagementMode.Quiet);
        await _service.SetCategoryMuteAsync(UserId, CompanyId, NotificationCategory.ShiftAssignment, muted: true);
        await _service.SetCategoryMuteAsync(UserId, CompanyId, NotificationCategory.AccountSecurity, muted: true);

        // Actionable but muted → suppressed.
        (await _service.ShouldSendEmailAsync(UserId, CompanyId, new ActionableEvent())).Should().BeFalse();
        // Security-critical → always emails, even Quiet + muted.
        (await _service.ShouldSendEmailAsync(UserId, CompanyId, new SecurityEvent())).Should().BeTrue();
    }

    [Fact] // NPS-04: catch-up fires once, guarded, then self-heals after reading below threshold
    public async Task CatchUp_FiresOnce_ThenResets()
    {
        await _service.SetEngagementModeAsync(UserId, CompanyId, EngagementMode.Quiet);

        // Below threshold → no catch-up.
        (await _service.TryBeginCatchUpAsync(UserId, CompanyId, unreadCount: 19)).Should().BeFalse();
        // Crossing threshold → fire once.
        (await _service.TryBeginCatchUpAsync(UserId, CompanyId, unreadCount: 20)).Should().BeTrue();
        // Still high, guard pending → no re-fire.
        (await _service.TryBeginCatchUpAsync(UserId, CompanyId, unreadCount: 31)).Should().BeFalse();
        // User reads down below threshold → guard self-heals (no fire on this call).
        (await _service.TryBeginCatchUpAsync(UserId, CompanyId, unreadCount: 3)).Should().BeFalse();
        // Accumulates to threshold again → fires again.
        (await _service.TryBeginCatchUpAsync(UserId, CompanyId, unreadCount: 22)).Should().BeTrue();
    }

    [Fact] // NPS-04b: Engaged users never get a catch-up
    public async Task CatchUp_EngagedNeverFires()
    {
        await _service.SetEngagementModeAsync(UserId, CompanyId, EngagementMode.Engaged);
        (await _service.TryBeginCatchUpAsync(UserId, CompanyId, unreadCount: 99)).Should().BeFalse();
    }
}
