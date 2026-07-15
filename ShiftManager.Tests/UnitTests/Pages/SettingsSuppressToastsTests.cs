using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.My;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Task 4 (#6): per-user opt-out for success toasts. Verifies AppUser.SuppressSuccessToasts is
/// persisted by SettingsModel.OnPostAsync. Uses a real SQLite (:memory:) AppDbContext (not
/// UseInMemoryDatabase) so EF behaves as in production — mirrors the harness pattern in
/// UsersAccountTypeHandlerTests.cs (localizer/audit/TempData wiring) and the minimal single-user
/// seeding style in HomeWeekSummaryTests.cs.
/// </summary>
public sealed class SettingsSuppressToastsTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;

    public SettingsSuppressToastsTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    private static Mock<IStringLocalizer<SharedResources>> BuildLocalizer()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        loc.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns<string, object[]>((k, _) => new LocalizedString(k, k));
        return loc;
    }

    private SettingsModel BuildModel(int userId, int companyId)
    {
        var tenantResolver = new Mock<ITenantResolver>();
        tenantResolver.Setup(t => t.GetCurrentTenantId()).Returns(companyId);

        var model = new SettingsModel(_db, tenantResolver.Object, BuildLocalizer().Object,
            NullLogger<SettingsModel>.Instance, Mock.Of<IAuditLogService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    private async Task<(int companyId, int userId)> SeedAsync()
    {
        const int companyId = 1;
        var user = new AppUser
        {
            CompanyId = companyId,
            Email = "target@test.com",
            DisplayName = "Target",
            Role = UserRole.Employee,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>(),
            SuppressSuccessToasts = false
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return (companyId, user.Id);
    }

    [Fact]
    public async Task Settings_Post_PersistsSuppressSuccessToasts()
    {
        var (companyId, userId) = await SeedAsync();

        var model = BuildModel(userId, companyId);
        model.SuppressSuccessToasts = true;

        await model.OnPostAsync();

        var reloaded = await _db.Users.FindAsync(userId);
        reloaded.Should().NotBeNull();
        reloaded!.SuppressSuccessToasts.Should().BeTrue();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
